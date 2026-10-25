using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRLauncher
{
    /// <summary>
    /// Scene entry point. Builds the catalog, state, list view, arcade room, fader and input,
    /// and runs the launch and return flow around TableLauncher: fade out with "Loading ...",
    /// launch, and when VPX exits, record the play, wait for XR, recentre and fade back in.
    /// </summary>
    public sealed class LauncherBootstrap : MonoBehaviour
    {
        private const float FadeSeconds = 0.4f;
        private const float XrReadyTimeoutSeconds = 5f;

        private TableLauncher launcher;
        private LauncherInput input;
        private ArcRoom room;
        private ScreenFader fader;
        private LauncherState state;
        private TableListView view;
        private bool launching;
        private bool quitNoticeShown;
        private TableEntry launchedEntry;
        private Camera headCamera;
        private bool inBackgroundMode;
        private int previousVSyncCount;
        private int previousTargetFrameRate;
        private UnityEngine.InputSystem.InputAction ownedPositionAction, ownedRotationAction;
        private UnityEngine.InputSystem.XR.TrackedPoseDriver poseDriver;
        internal Func<bool> TrackingAvailable { get; set; } = HeadsetTracked;
        internal Func<float> FrameSeconds { get; set; } = () => Time.unscaledDeltaTime;
        internal Action<IEnumerator> Schedule { get; set; }

        private IEnumerator Start()
        {
            Initialize(LauncherConfig.Instance, Path.Combine(Application.persistentDataPath, "state.json"), Camera.main);
            return Arrive();
        }

        /// <summary>Builds the scene from explicit inputs; headset arrival is a separate lifecycle step.</summary>
        internal ArcRoom Initialize(LauncherConfig config, string statePath, Camera head)
        {
            if (head == null) throw new ArgumentNullException(nameof(head), "Launcher scene requires a head camera.");
            if (room != null) throw new InvalidOperationException("Launcher scene is already initialized.");
            Application.runInBackground = true;
            headCamera = head;
            AddHeadTracking(head);
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color(0.02f, 0.02f, 0.03f);

            launcher = GetOrAdd<TableLauncher>();
            launcher.Configure(config);
            GetOrAdd<VRControllerInput>();
            GetOrAdd<ControllerBridge>();
            MediaCache cache = gameObject.AddComponent<MediaCache>();

            string setupError;
            bool setupNeeded = !config.Validate(out setupError) || LauncherConfig.LoadWarning != null;
            List<TableEntry> tables;
            try { tables = TableCatalog.Scan(LauncherPaths.CatalogSettingsFrom(config), new DiskFileSystem()); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is System.Security.SecurityException)
            {
                Debug.LogWarning("Could not read the tables folder: " + ex.Message);
                tables = new List<TableEntry>();
                setupNeeded = true;
            }
            LogCatalog(tables, config);

            state = LauncherState.Load(statePath);
            if (state.LoadWarning != null) Debug.LogWarning(state.LoadWarning);

            view = new TableListView(tables, state);
            string coinDoorImage = LauncherPaths.Resolve(@"Media\Cabinet\coindoor.jpg");
            if (coinDoorImage != null && File.Exists(coinDoorImage))
            {
                Debug.Log($"Coin door photo: {coinDoorImage}");
            }
            else
            {
                Debug.Log("Coin door photo: none (built-in door)");
                coinDoorImage = null;
            }
            room = ArcRoom.Create(view, state, cache, coinDoorImage);
            room.ReadinessDetails = entry =>
            {
                try
                {
                    LaunchCommand command = VpxProfiles.Resolve(entry.FullPath, config.vpinballExecutable, config.vpxProfiles, config.tableProfiles, LauncherPaths.AppDirectory);
                    string actual = VpxPaths.Target(command.Executable, command.ProfileName == null ? config.realVpinballExecutable : "", LauncherPaths.AppDirectory);
                    string registered = WindowsRomRegistry.ReadRomPaths();
                    return TableReadiness.Describe(entry.FullPath, command, actual, VpxPaths.RomDirectories(entry.FullPath, actual, registered), state, entry.RelativePath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException)
                { return "Readiness could not be checked. Open F2 setup to review paths."; }
            };
            if (tables.Count == 0) room.ShowNotice($"Tables folder: {config.tablesDirectory}");
            if (setupNeeded)
            {
                Debug.LogWarning("Setup needs attention: " + (LauncherConfig.LoadWarning ?? setupError));
                room.ShowNotice("Setup needs attention. Press F2 on the PC to review locations and checks.");
            }

            fader = ScreenFader.Create(head);
            fader.SetImmediate(1f);

            input = gameObject.AddComponent<LauncherInput>();
            input.IsSuppressed = () => LauncherConfig.SetupWindowOpen;
            input.InputEnabled = false;
            input.Previous += () => { room.ClearNotice(); room.Previous(); };
            input.Next += () => { room.ClearNotice(); room.Next(); };
            input.CycleView += () => { room.ClearNotice(); room.CycleView(); };
            input.PreviousView += () => { room.ClearNotice(); room.PreviousView(); };
            input.JumpLetter += direction => { room.ClearNotice(); room.JumpLetter(direction); };
            input.SurpriseMe += () => { room.ClearNotice(); room.SurpriseMe(); };
            input.ToggleDetails += room.ToggleDetails;
            input.OpenSetup += () =>
            {
                try { LauncherConfig.OpenSetupWindow(); }
                catch (Exception ex) { Debug.LogWarning(ex.Message); room.ShowNotice("Setup tool is unavailable. Reinstall the launcher or review its configuration file."); }
            };
            input.ToggleFavorite += ToggleFavorite;
            input.Launch += () => RunRoutine(LaunchSelected());
            input.Quit += Quit;
            launcher.OnTableExited += OnTableExited;
            launcher.OnXRRestartFailed += ShowXrFailure;

            return room;
        }

        private void Update()
        {
            if (input == null) return;
            if (input.QuitHeld)
            {
                float remaining = (1f - input.QuitHoldProgress) * LauncherInput.QuitHoldSeconds;
                room.ShowNotice($"Keep holding Y to quit... {remaining:0.0}s");
                quitNoticeShown = true;
            }
            else if (quitNoticeShown)
            {
                room.ClearNotice();
                quitNoticeShown = false;
            }
        }

        /// <summary>Waits for the headset, places the room in front of it, and fades in.</summary>
        internal IEnumerator Arrive(string message = null)
        {
            float waited = 0f;
            while (waited < XrReadyTimeoutSeconds && !TrackingAvailable())
            {
                waited += FrameSeconds();
                yield return null;
            }
            if (waited >= XrReadyTimeoutSeconds) Debug.LogWarning("Headset not tracked after 5 s; placing the room from the current camera pose.");

            yield return null;   // one more frame so the camera has the tracked pose
            Transform head = headCamera.transform;
            room.Recenter(head.position, head.eulerAngles.y);
            room.Resume();
            if (message != null)
            {
                fader.SetImmediate(1f, message);
                yield return new WaitForSecondsRealtime(1.2f);
            }
            yield return fader.Fade(0f, FadeSeconds);
            input.InputEnabled = true;
            Debug.Log("Launcher ready: navigation enabled");
        }

        /// <summary>
        /// With XR Plugin Management and the OpenXR plugin 1.18, Unity no longer moves the Main
        /// Camera to follow the headset by itself; without a pose driver the camera stays frozen
        /// at its editor pose and the whole room appears to swing around the player's head instead
        /// of the player looking around the room. Adding a TrackedPoseDriver at runtime restores
        /// tracking. Because Reset() (which is what fills in the default action bindings when the
        /// component is added in the editor) only runs in editor mode, a driver added at runtime
        /// gets no bindings for free, so its inputs are built and assigned here explicitly.
        /// </summary>
        private void AddHeadTracking(Camera head)
        {
            UnityEngine.InputSystem.XR.TrackedPoseDriver driver =
                head.gameObject.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver == null)
            {
                driver = head.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            }
            poseDriver = driver;

            driver.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            // TrackedPoseDriver's positionInput/rotationInput setters unbind the old action and bind
            // the new one whenever the component is already active and enabled, and binding an
            // action that has no InputActionReference (i.e. one we own directly, as here) enables
            // that action as part of the bind. So assigning these properties after AddComponent is
            // enough to get them enabled; the explicit enable checks below are a safety net in case
            // that ever changes.
            ownedPositionAction = new UnityEngine.InputSystem.InputAction("Head Position", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition");
            ownedRotationAction = new UnityEngine.InputSystem.InputAction("Head Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation");
            driver.positionInput = new UnityEngine.InputSystem.InputActionProperty(ownedPositionAction);
            driver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(ownedRotationAction);

            // trackingStateInput is deliberately left unbound. If the OpenXR runtime (Virtual
            // Desktop) ever reports <XRHMD>/trackingState as 0 or leaves it unset, a bound driver
            // would treat the pose as invalid and freeze the camera again, i.e. exactly the bug this
            // change fixes. With trackingStateInput unbound, TrackedPoseDriver's default
            // m_CurrentTrackingState (Position | Rotation) always treats the pose as valid, so
            // tracking follows positionInput/rotationInput unconditionally.
            if (!driver.positionInput.action.enabled) driver.positionInput.action.Enable();
            if (!driver.rotationInput.action.enabled) driver.rotationInput.action.Enable();

            Debug.Log($"Head tracking: TrackedPoseDriver on {head.name}");
        }

        private static bool HeadsetTracked()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete || manager.activeLoader == null) return false;
            InputDevice headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return headset.isValid && headset.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        internal IEnumerator LaunchSelected()
        {
            TableEntry entry = view.Selected;
            if (launching || entry == null || launcher.IsTableRunning()) yield break;

            launching = true;
            input.InputEnabled = false;
            room.ClearNotice();
            room.PulseCenter();
            yield return fader.Fade(1f, FadeSeconds, $"Starting {entry.Title}…\nHanding the headset to VPX");
            room.Suspend();

            if (launcher.LaunchTable(entry.FullPath))
            {
                launchedEntry = entry;
                EnterBackgroundMode();
            }
            else
            {
                state.RecordLaunch(entry.RelativePath, DateTime.UtcNow, false, launcher.LastLaunchError ?? "The table could not start.");
                SaveState();
                string reason = (launcher.LastLaunchError ?? "The table could not start.") + " A: retry or browse another table.";
                room.ShowNotice(reason);
                launching = false;
                yield return Arrive(reason);
            }
        }

        /// <summary>
        /// Stops the launcher from spending GPU time while a table runs. VPX ends our XR
        /// session before it launches, but Unity keeps rendering the arcade room to the
        /// desktop window regardless, which time-slices the GPU against VPX and Virtual
        /// Desktop and adds latency the user feels in the headset. Disabling the camera
        /// stops all GPU work; uncapping vsync and capping the frame rate at 60 keeps the
        /// CPU loop bounded without starving it, because VRControllerInput still needs to
        /// run every frame to map controller buttons to key presses for VPX.
        /// </summary>
        private void EnterBackgroundMode()
        {
            inBackgroundMode = true;
            previousVSyncCount = QualitySettings.vSyncCount;
            previousTargetFrameRate = Application.targetFrameRate;

            headCamera.enabled = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            Debug.Log("Background mode: rendering paused while the table runs");
        }

        /// <summary>
        /// Restores normal rendering after a table exits. Safe to call when not in
        /// background mode (a no-op), so OnTableExited can call it unconditionally.
        /// </summary>
        private void ExitBackgroundMode()
        {
            if (!inBackgroundMode) return;

            if (headCamera != null) headCamera.enabled = true;
            QualitySettings.vSyncCount = previousVSyncCount;
            Application.targetFrameRate = previousTargetFrameRate;
            inBackgroundMode = false;

            Debug.Log("Background mode: rendering resumed");
        }

        private void OnTableExited()
        {
            // Also guard against duplicate exit notifications from external integrations.
            if (launchedEntry == null) return;

            // Fader is already at alpha 1 here, so re-enabling the camera shows black first,
            // then the fade-in in Arrive() reveals the room.
            ExitBackgroundMode();

            if (launcher.LastSessionCounted) state.RecordPlay(launchedEntry.RelativePath, DateTime.UtcNow);
            state.RecordLaunch(launchedEntry.RelativePath, DateTime.UtcNow, launcher.LastStartupConfirmed, launcher.LastReturnNotice);
            SaveState();
            launchedEntry = null;

            room.ClearNotice();
            if (launcher.LastReturnNotice != null) room.ShowNotice(launcher.LastReturnNotice);
            room.Refresh();
            fader.SetImmediate(1f, string.Empty);
            launching = false;
            RunRoutine(Arrive(launcher.LastReturnNotice));
        }

        private void RunRoutine(IEnumerator routine)
        {
            if (Schedule != null) Schedule(routine);
            else StartCoroutine(routine);
        }

        private void ShowXrFailure(string message)
        {
            if (room != null) room.ShowNotice(message);
        }

        private void ToggleFavorite()
        {
            TableEntry entry = view.Selected;
            if (entry == null) return;
            state.ToggleFavorite(entry.RelativePath);
            SaveState();
            room.Refresh();
        }

        private void SaveState()
        {
            try
            {
                state.RememberBrowsing(view.Kind, view.Selected?.RelativePath);
                state.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Could not save {state.FilePath}: {ex.Message}");
            }
        }

        private void Quit()
        {
            if (launcher.IsTableRunning()) return;
            Debug.Log("Quitting launcher");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnApplicationQuit()
        {
            if (state != null && view != null) SaveState();
        }

        private void OnDestroy() => DisposeScene();

        /// <summary>Releases only objects/actions created by this bootstrap; safe to repeat.</summary>
        internal void DisposeScene()
        {
            if (launcher != null)
            {
                launcher.OnTableExited -= OnTableExited;
                launcher.OnXRRestartFailed -= ShowXrFailure;
            }
            ExitBackgroundMode();
            if (poseDriver != null)
            {
                if (poseDriver.positionInput.action == ownedPositionAction) poseDriver.positionInput = default;
                if (poseDriver.rotationInput.action == ownedRotationAction) poseDriver.rotationInput = default;
            }
            ownedPositionAction?.Dispose();
            ownedRotationAction?.Dispose();
            ownedPositionAction = null;
            ownedRotationAction = null;
            if (room != null)
            {
                if (Application.isPlaying) Destroy(room.gameObject); else DestroyImmediate(room.gameObject);
                room = null;
            }
            if (fader != null)
            {
                if (Application.isPlaying) Destroy(fader.gameObject); else DestroyImmediate(fader.gameObject);
                fader = null;
            }
        }

        private T GetOrAdd<T>() where T : Component
        {
            T existing = GetComponent<T>();
            return existing != null ? existing : gameObject.AddComponent<T>();
        }

        private static void LogCatalog(List<TableEntry> tables, LauncherConfig config)
        {
            Debug.Log($"Catalog: {tables.Count} table(s) in {config.tablesDirectory}; " +
                      $"missing wheel {tables.Count(t => t.Media.Wheel == null)}, " +
                      $"playfield {tables.Count(t => t.Media.Playfield == null)}, " +
                      $"backglass {tables.Count(t => t.Media.Backglass == null)}, " +
                      $"video {tables.Count(t => t.Media.Video == null)}");
            foreach (TableEntry table in tables)
            {
                var missing = new List<string>();
                if (table.Media.Wheel == null) missing.Add("wheel");
                if (table.Media.Playfield == null) missing.Add("playfield");
                if (table.Media.Backglass == null) missing.Add("backglass");
                if (table.Media.Video == null) missing.Add("video");
                if (missing.Count > 0) Debug.Log($"  {table.Stem}: missing {string.Join(", ", missing)}");
            }
        }
    }
}
