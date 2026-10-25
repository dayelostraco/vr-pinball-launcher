using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.Management;

namespace VRLauncher
{
    /// <summary>
    /// Launches Visual Pinball tables and monitors the process
    /// </summary>
    public class TableLauncher : MonoBehaviour
    {
        // Windows API for window focus management
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // Polled globally rather than through the Input System: while a table
        // is running VPinballX holds focus, and Keyboard.current only reports
        // keys when this window is focused - which is never, at the one moment
        // the rescue hotkey is needed.
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_BACK = 0x08;

        // Window show commands
        private const int SW_RESTORE = 9;

        // SetWindowPos flags
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [Header("Configuration")]
        [Tooltip("Path to VPinballX_GL64.exe")]
        public string vpinballExecutable = @"C:\Visual Pinball\VPinballX_GL64.exe";

        [Tooltip("Time in seconds to wait before attempting to focus the VP window")]
        public float focusDelaySeconds = 2.0f;

        private VpxProfile[] vpxProfiles = new VpxProfile[0];
        private TableProfile[] tableProfiles = new TableProfile[0];

        private Process currentProcess;
        private bool backspaceWasPressed = false;
        private bool isTableRunning = false;
        private string currentTablePath;
        private readonly Stopwatch launchClock = new Stopwatch();
        private LaunchFeedback feedback;
        private string runtimeLogPath;
        private long runtimeLogOffset;
        private string pendingLogLine = "";
        private float nextLogRead;
        private bool cancelled;
        private GUIStyle progressStyle;
        private ITableLaunchPlatform platform;
        private IEnumerator xrRestart;
        internal IXrSession XrSession { get; set; }

        private IXrSession GetXrSession()
        {
            if (XrSession != null) return XrSession;
            var manager = XRGeneralSettings.Instance?.Manager;
            return manager == null ? null : new UnityXrSession(manager);
        }

        internal ITableLaunchPlatform Platform
        {
            get => platform ?? (platform = new WindowsLaunchPlatform(this));
            set => platform = value;
        }

        public string LastLaunchError { get; private set; }
        public string LastReturnNotice { get; private set; }
        public bool LastSessionCounted { get; private set; }
        public bool LastStartupConfirmed => feedback != null && feedback.StartupCompleted;
        public bool OwnsForegroundWindow => isTableRunning && VpxProcessTree.OwnsForeground(currentProcess);

        public delegate void TableExitedHandler();
        public event TableExitedHandler OnTableExited;
        public event Action<string> OnXRRestartFailed;

        /// <summary>
        /// Launches a Visual Pinball table
        /// </summary>
        /// <param name="tablePath">Full path to the .vpx file</param>
        /// <returns>True if launch was successful</returns>
        public bool LaunchTable(string tablePath)
        {
            LastLaunchError = null;
            LastReturnNotice = null;
            LastSessionCounted = false;
            if (isTableRunning)
            {
                UnityEngine.Debug.LogWarning("A table is already running!");
                LastLaunchError = "Another table is still running.";
                return false;
            }

            LaunchCommand command;
            try { command = VpxProfiles.Resolve(tablePath, vpinballExecutable, vpxProfiles, tableProfiles, LauncherPaths.AppDirectory); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogError("Could not resolve launch paths: " + ex.Message);
                LastLaunchError = "The launch profile contains an invalid path. Review locations in setup.";
                return false;
            }
            if (!File.Exists(command.Executable))
            {
                UnityEngine.Debug.LogError($"VPinballX executable not found: {command.Executable}");
                LastLaunchError = "The configured VPX program could not be found.";
                return false;
            }

            if (!File.Exists(tablePath))
            {
                UnityEngine.Debug.LogError($"Table file not found: {tablePath}");
                LastLaunchError = "This table file could not be found.";
                return false;
            }

            try
            {
                currentTablePath = tablePath;
                feedback = new LaunchFeedback(tablePath);
                cancelled = false;
                runtimeLogPath = Platform.RuntimeLogPath;
                runtimeLogOffset = File.Exists(runtimeLogPath) ? new FileInfo(runtimeLogPath).Length : 0;
                pendingLogLine = "";
                launchClock.Restart();

                // Stop Unity's XR session before launching VPinballX
                UnityEngine.Debug.Log("Stopping Unity XR session before launching table...");
                Platform.StopXR();

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = command.Executable,
                    Arguments = command.Arguments,
                    UseShellExecute = false,
                    WorkingDirectory = command.WorkingDirectory
                };

                if (command.ProfileName != null)
                    UnityEngine.Debug.Log($"Using VPX profile '{command.ProfileName}' for this table");

                UnityEngine.Debug.Log($"Launching table: {Path.GetFileName(tablePath)}");
                UnityEngine.Debug.Log($"Command: {startInfo.FileName} {startInfo.Arguments}");

                currentProcess = Platform.StartProcess(startInfo);

                if (currentProcess != null)
                {
                    isTableRunning = true;
                    // Poll on the Unity thread: one exit path, including immediately exiting hosts.

                    // Start coroutine to focus the VPinballX window after a delay
                    try { Platform.FocusWindow(); }
                    catch (Exception ex) { UnityEngine.Debug.LogWarning("Window focus could not be scheduled: " + ex.Message); }

                    return true;
                }
                else
                {
                    UnityEngine.Debug.LogError("Failed to start process");
                    LastLaunchError = "Windows could not start the configured VPX program.";
                    currentTablePath = null;
                    // Restart XR if launch failed
                    RestartXR();
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Error launching table: {ex.Message}");
                LastLaunchError = "Windows could not start VPX. See the launch log for details.";
                currentTablePath = null;
                RestartXR();
                return false;
            }
        }

        private void FinishTable()
        {
            if (!isTableRunning || currentProcess == null) return;
            ReadRuntimeLog();
            int exitCode = currentProcess.HasExited ? currentProcess.ExitCode : -1;
            launchClock.Stop();
            LastSessionCounted = feedback.CountSession(exitCode, launchClock.Elapsed.TotalSeconds, cancelled);
            LastReturnNotice = feedback.ReturnNotice(exitCode, launchClock.Elapsed.TotalSeconds, cancelled);
            UnityEngine.Debug.Log($"Launch result: {currentTablePath}; exit={exitCode}; seconds={launchClock.Elapsed.TotalSeconds:F1}; startupConfirmed={feedback.StartupCompleted}; cancelled={cancelled}; problem={feedback.Problem}");
            UnityEngine.Debug.Log($"Table exited: {Path.GetFileName(currentTablePath)}");
            isTableRunning = false;
            currentTablePath = null;
            currentProcess.Dispose();
            currentProcess = null;
            RestartXR();
            OnTableExited?.Invoke();
        }

        private void RestartXR()
        {
            try { Platform.StartXR(); }
            catch (Exception ex)
            {
                ReportXrFailure(ex);
            }
        }

        private void ReportXrFailure(Exception ex)
        {
            // Initialization can fail on a later coroutine frame, after the
            // ordinary process-exit event has already returned control to the menu.
            UnityEngine.Debug.LogError("Could not restart XR: " + ex.Message);
            LastReturnNotice = "VR could not restart. Check the active XR runtime and restart the launcher.";
            if (LastLaunchError != null) LastLaunchError += " VR could not restart; check the active XR runtime.";
            OnXRRestartFailed?.Invoke(LastReturnNotice);
        }

        /// <summary>
        /// Checks if a table is currently running
        /// </summary>
        public bool IsTableRunning()
        {
            return isTableRunning;
        }

        /// <summary>
        /// Gets the currently running table path
        /// </summary>
        public string GetCurrentTablePath()
        {
            return currentTablePath;
        }

        void Awake()
        {
            // Load configuration
            Configure(LauncherConfig.Instance);
        }

        internal void Configure(LauncherConfig config)
        {
            vpinballExecutable = config.vpinballExecutable;
            vpxProfiles = config.vpxProfiles ?? new VpxProfile[0];
            tableProfiles = config.tableProfiles ?? new TableProfile[0];

            UnityEngine.Debug.Log($"TableLauncher configured to use VPinballX at: {vpinballExecutable}");
            foreach (TableProfile entry in tableProfiles)
            {
                if (entry == null) continue;
                UnityEngine.Debug.Log($"Table '{entry.table}' uses VPX profile '{entry.profile}'");
            }
        }

        internal void PollProcess()
        {
            if (isTableRunning && Time.unscaledTime >= nextLogRead)
            {
                nextLogRead = Time.unscaledTime + 0.5f;
                ReadRuntimeLog();
            }
            // Process completion is handled once, on the Unity thread.
            if (isTableRunning && currentProcess != null && currentProcess.HasExited)
            {
                UnityEngine.Debug.Log("Detected table exit via polling");
                FinishTable();
            }
        }

        void Update()
        {
            Tick((GetAsyncKeyState(VK_BACK) & 0x8000) != 0);
        }

        internal void Tick(bool backspacePressed)
        {
            PollXrRestart();
            PollProcess();

            // Allow manual exit with Backspace - kills VPinballX and returns to
            // the menu. Polled via GetAsyncKeyState so it still works while
            // VPinballX has focus, which it always does while a table is up.
            if (backspacePressed && !backspaceWasPressed)
            {
                backspaceWasPressed = true;
                if (isTableRunning)
                {
                    UnityEngine.Debug.Log("BACKSPACE pressed - Killing VPinballX process and returning to menu");
                    KillCurrentTable();
                }
            }
            else if (!backspacePressed)
            {
                backspaceWasPressed = false;
            }
        }

        /// <summary>
        /// Manually kills the current VPinballX process
        /// </summary>
        public void KillCurrentTable()
        {
            if (!isTableRunning) return;
            cancelled = true;
            if (currentProcess != null && !currentProcess.HasExited)
            {
                try
                {
                    UnityEngine.Debug.Log("Terminating VPinballX process");
                    VpxProcessTree.Kill(currentProcess);
                    currentProcess.WaitForExit(2000);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Error killing process: {ex.Message}");
                }
            }

            if (currentProcess == null || currentProcess.HasExited) FinishTable();
        }

        private void ReadRuntimeLog()
        {
            if (feedback == null || runtimeLogPath == null) return;
            try
            {
                using (var stream = new FileStream(runtimeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < runtimeLogOffset) { runtimeLogOffset = 0; pendingLogLine = ""; }
                    stream.Position = runtimeLogOffset;
                    byte[] bytes = new byte[(int)Math.Min(65536, stream.Length - runtimeLogOffset)];
                    int count = stream.Read(bytes, 0, bytes.Length);
                    runtimeLogOffset += count;
                    string[] lines = (pendingLogLine + System.Text.Encoding.UTF8.GetString(bytes, 0, count)).Split('\n');
                    for (int i = 0; i < lines.Length - 1; i++) feedback.Observe(lines[i]);
                    pendingLogLine = lines[lines.Length - 1];
                }
            }
            catch (IOException) { } // Logging is optional; process-exit recovery must still work.
            catch (UnauthorizedAccessException) { }
        }

        private void OnGUI()
        {
            // Desktop-only progress: Unity has released XR to VPX. Stop drawing once startup is confirmed.
            string progress = ProgressText();
            if (progress == null) return;
            if (progressStyle == null)
                progressStyle = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Box(new Rect((Screen.width - 680) / 2f, (Screen.height - 160) / 2f, 680, 160), progress, progressStyle);
        }

        internal string ProgressText()
        {
            if (!isTableRunning || feedback == null || (feedback.StartupCompleted && feedback.Problem == null)) return null;
            string title = Path.GetFileNameWithoutExtension(currentTablePath);
            string stage = feedback.Problem ?? (launchClock.Elapsed.TotalSeconds >= 30 ? "Still waiting for VPX startup" : "Starting VPX");
            return $"{title}\n{stage} · {launchClock.Elapsed.TotalSeconds:0}s\nBackspace: stop and return to the launcher";
        }

        /// <summary>
        /// Waits for VPinballX window to appear and sets focus to it
        /// </summary>
        internal IEnumerator FocusVPinballWindow()
        {
            foreach (float delay in WindowFocusSequence.Run(new FocusTarget(this), focusDelaySeconds,
                message => UnityEngine.Debug.Log(message), message => UnityEngine.Debug.LogWarning(message)))
                yield return new WaitForSecondsRealtime(delay);
        }

        private sealed class FocusTarget : IWindowFocusTarget
        {
            private readonly TableLauncher owner;
            private readonly Process process;
            internal FocusTarget(TableLauncher owner) { this.owner = owner; process = owner.currentProcess; }
            // An old focus coroutine must never start operating on the next table.
            public bool IsRunning => process != null && ReferenceEquals(owner.currentProcess, process) && !process.HasExited;
            public IntPtr GetWindow() => process.MainWindowHandle;
            public bool IsMinimized(IntPtr window) => IsIconic(window);
            public void Refresh() => process.Refresh();
            public void Restore(IntPtr window) => ShowWindow(window, SW_RESTORE);
            public bool Activate(IntPtr window) => SetForegroundWindow(window);
            public void SetTopmost(IntPtr window, bool value)
            {
                if (!SetWindowPos(window, value ? HWND_TOPMOST : HWND_NOTOPMOST,
                    0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        /// <summary>
        /// Stops Unity's XR session
        /// </summary>
        internal void StopXR()
        {
            CancelXrRestart();
            var xrManager = GetXrSession();
            if (xrManager != null && xrManager.Initialized)
            {
                UnityEngine.Debug.Log("Stopping XR...");
                xrManager.Stop();
                xrManager.Deinitialize();
                UnityEngine.Debug.Log("XR stopped");
            }
        }

        /// <summary>
        /// Starts Unity's XR session
        /// </summary>
        internal void StartXR()
        {
            var xrManager = GetXrSession();
            if (xrManager != null && !xrManager.Initialized)
            {
                UnityEngine.Debug.Log("Starting XR...");
                BeginXrRestart(StartXRCoroutine(xrManager));
            }
        }

        // The pinned XR manager yields only between loader attempts. Driving the
        // guarded iterator on Update makes cancellation explicit: an old attempt
        // cannot resume and acquire XR after a new table has taken ownership.
        internal void BeginXrRestart(IEnumerator operation)
        {
            CancelXrRestart();
            xrRestart = CoroutineGuard.Run(operation, ReportXrFailure);
        }

        internal void PollXrRestart()
        {
            if (xrRestart != null && !xrRestart.MoveNext())
                CancelXrRestart();
        }

        internal void CancelXrRestart()
        {
            var previous = xrRestart;
            xrRestart = null;
            (previous as IDisposable)?.Dispose();
        }

        private IEnumerator StartXRCoroutine(IXrSession session)
        {
            UnityEngine.Debug.Log("Initializing XR...");
            yield return session.Initialize();

            if (session.HasLoader)
            {
                UnityEngine.Debug.Log("XR Loader initialized, starting subsystems...");
                session.Start();
                UnityEngine.Debug.Log("XR started successfully");
            }
            else
            {
                throw new InvalidOperationException("Failed to initialize XR loader");
            }
        }

        void OnApplicationQuit()
        {
            Shutdown();
        }

        void OnDestroy()
        {
            Shutdown();
        }

        internal void Shutdown()
        {
            CancelXrRestart();
            TerminateTableProcess("launcher shutdown");

            // Clean up process monitoring
            if (currentProcess != null)
            {
                currentProcess.Dispose();
                currentProcess = null;
            }
        }

        /// <summary>
        /// Kills VPinballX on shutdown, without restarting XR or raising
        /// OnTableExited - there is no menu left to return to. Process.Dispose
        /// only releases the handle, so without this an orphaned VPinballX
        /// keeps running and holds the OpenXR runtime.
        /// </summary>
        private void TerminateTableProcess(string reason)
        {
            if (currentProcess == null)
            {
                return;
            }

            try
            {
                if (currentProcess.HasExited)
                {
                    return;
                }

                UnityEngine.Debug.Log($"Terminating VPinballX on {reason}");
                VpxProcessTree.Kill(currentProcess);
                currentProcess.WaitForExit(2000);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Error terminating VPinballX on {reason}: {ex.Message}");
            }
            finally
            {
                isTableRunning = false;
            }
        }

        private sealed class WindowsLaunchPlatform : ITableLaunchPlatform
        {
            private readonly TableLauncher owner;
            internal WindowsLaunchPlatform(TableLauncher owner) { this.owner = owner; }
            public string RuntimeLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VPinballX", "10.8", "vpinball.log");
            public Process StartProcess(ProcessStartInfo startInfo) => Process.Start(startInfo);
            public void StopXR() => owner.StopXR();
            public void StartXR() => owner.StartXR();
            public void FocusWindow() => owner.StartCoroutine(owner.FocusVPinballWindow());
        }

        private sealed class UnityXrSession : IXrSession
        {
            private readonly XRManagerSettings manager;
            internal UnityXrSession(XRManagerSettings manager) { this.manager = manager; }
            public bool Initialized => manager.isInitializationComplete;
            public bool HasLoader => manager.activeLoader != null;
            public IEnumerator Initialize() => manager.InitializeLoader();
            public void Start() => manager.StartSubsystems();
            public void Stop() => manager.StopSubsystems();
            public void Deinitialize() => manager.DeinitializeLoader();
        }
    }
}
