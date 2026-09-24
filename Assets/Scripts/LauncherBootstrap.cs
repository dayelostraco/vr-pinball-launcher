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

        private void Start()
        {
            Application.runInBackground = true;
            Camera head = Camera.main;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color(0.02f, 0.02f, 0.03f);

            LauncherConfig config = LauncherConfig.Instance;
            launcher = GetOrAdd<TableLauncher>();
            GetOrAdd<VRControllerInput>();
            GetOrAdd<ControllerBridge>();
            _ = UnityMainThreadDispatcher.Instance;
            MediaCache cache = gameObject.AddComponent<MediaCache>();

            List<TableEntry> tables = TableCatalog.Scan(LauncherPaths.CatalogSettingsFrom(config), new DiskFileSystem());
            LogCatalog(tables, config);

            state = LauncherState.Load(Path.Combine(Application.persistentDataPath, "state.json"));
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
            if (tables.Count == 0) room.ShowNotice($"Tables folder: {config.tablesDirectory}");

            fader = ScreenFader.Create(head);
            fader.SetImmediate(1f);

            input = gameObject.AddComponent<LauncherInput>();
            input.InputEnabled = false;
            input.Previous += room.Previous;
            input.Next += room.Next;
            input.CycleView += room.CycleView;
            input.ToggleFavorite += ToggleFavorite;
            input.Launch += () => StartCoroutine(LaunchSelected());
            input.Quit += Quit;
            launcher.OnTableExited += OnTableExited;

            StartCoroutine(Arrive());
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
        private IEnumerator Arrive()
        {
            float waited = 0f;
            while (waited < XrReadyTimeoutSeconds && !HeadsetTracked())
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (waited >= XrReadyTimeoutSeconds) Debug.LogWarning("Headset not tracked after 5 s; placing the room from the current camera pose.");

            yield return null;   // one more frame so the camera has the tracked pose
            Transform head = Camera.main.transform;
            room.Recenter(head.position, head.eulerAngles.y);
            room.Resume();
            yield return fader.Fade(0f, FadeSeconds);
            input.InputEnabled = true;
        }

        private static bool HeadsetTracked()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete || manager.activeLoader == null) return false;
            InputDevice headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return headset.isValid && headset.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private IEnumerator LaunchSelected()
        {
            TableEntry entry = view.Selected;
            if (launching || entry == null || launcher.IsTableRunning()) yield break;

            launching = true;
            input.InputEnabled = false;
            room.PulseCenter();
            yield return fader.Fade(1f, FadeSeconds, $"Loading {entry.Title}…");
            room.Suspend();

            if (launcher.LaunchTable(entry.FullPath))
            {
                launchedEntry = entry;
            }
            else
            {
                room.Resume();
                room.ShowNotice($"Could not start {entry.Title}. See the log.");
                yield return fader.Fade(0f, FadeSeconds);
                input.InputEnabled = true;
                launching = false;
            }
        }

        private void OnTableExited()
        {
            // TableLauncher can raise this event twice for one exit (a synchronous call from
            // KillCurrentTable plus a queued call from the process Exited handler, or a race
            // between the Update poll and that handler). launchedEntry is cleared after the
            // first call, so a second call is a no-op instead of double-recording the play or
            // starting a second, overlapping Arrive() coroutine.
            if (launchedEntry == null) return;

            state.RecordPlay(launchedEntry.RelativePath, DateTime.UtcNow);
            SaveState();
            launchedEntry = null;

            room.ClearNotice();
            room.Refresh();
            fader.SetImmediate(1f, string.Empty);
            launching = false;
            StartCoroutine(Arrive());
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

        private T GetOrAdd<T>() where T : Component
        {
            T existing = FindFirstObjectByType<T>();
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
