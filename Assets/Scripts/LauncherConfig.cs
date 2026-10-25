using System;
using System.IO;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Configuration settings for the VR Launcher
    /// Supports loading from JSON file for easy customization
    /// </summary>
    [Serializable]
    public class LauncherConfig
    {
        [Header("Visual Pinball Settings")]
        [Tooltip("Path to VPinballX_GL64.exe")]
        public string vpinballExecutable = @"C:\Visual Pinball\VPinballX_GL64.exe";

        [Tooltip("Actual VPX executable when vpinballExecutable points to a launch helper")]
        public string realVpinballExecutable = "";

        [Tooltip("Extra VPX installs (e.g. 10.8.0) that tables can be assigned to in tableProfiles")]
        public VpxProfile[] vpxProfiles = new VpxProfile[0];

        [Tooltip("Tables (file name without .vpx) that launch with a vpxProfiles install instead of vpinballExecutable")]
        public TableProfile[] tableProfiles = new TableProfile[0];

        [Tooltip("Directory containing .vpx table files")]
        public string tablesDirectory = @"C:\Visual Pinball\Tables";

        [Tooltip("Search subdirectories for tables")]
        public bool searchSubdirectories = true;

        [Tooltip("Directory containing wheel images (supports both relative and absolute paths)")]
        public string wheelDirectory = @"Media\Wheel";

        [Tooltip("Folder of per-table media fetched by tools/fetch_media.py, one subfolder per table (supports both relative and absolute paths)")]
        public string tableMediaDirectory = @"Media\Tables";

        [Header("Application Settings")]
        [Tooltip("Legacy configuration field; currently unused. Diagnostics are written to Player.log.")]
        public bool showDebugConsole = false;

        [Header("VR Controller Bridge")]
        [Tooltip("Launch an external controller bridge process when the launcher starts, and kill it when the launcher exits")]
        public bool enableControllerBridge = false;

        [Tooltip("Path to the controller bridge executable to launch (e.g. AutoHotkey exe, or a compiled bridge). Leave empty to disable.")]
        public string controllerBridgePath = "";

        [Tooltip("Command-line arguments for the bridge (e.g. the full path to the .ahk script, quoted)")]
        public string controllerBridgeArgs = "";

        [Tooltip("Working directory for the bridge process. Leave empty to use the executable's folder. For the AutoHotkey script, set this to the folder containing auto_oculus_touch.dll.")]
        public string controllerBridgeWorkingDir = "";

        private static LauncherConfig _instance;
        private static readonly string ConfigFileName = "launcher-config.json";
        public static string LoadWarning { get; private set; }
        private static System.Diagnostics.Process setupProcess;
        internal static Func<System.Diagnostics.ProcessStartInfo, System.Diagnostics.Process> StartSetupProcess { get; set; } = System.Diagnostics.Process.Start;
        public static bool SetupWindowOpen
        {
            get
            {
                if (setupProcess == null) return false;
                if (!setupProcess.HasExited) return true;
                setupProcess.Dispose();
                setupProcess = null;
                return false;
            }
        }

        /// <summary>
        /// Gets the singleton instance of the config
        /// </summary>
        public static LauncherConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the full path to the config file
        /// </summary>
        public static string ConfigFilePath
        {
            get
            {
                // Store config next to executable in standalone builds
                string path = Application.dataPath;
                if (Application.isEditor)
                {
                    // In editor, store in project root
                    path = Path.Combine(Application.dataPath, "..");
                }
                else
                {
                    // In build, store next to exe
                    path = Path.GetDirectoryName(Application.dataPath);
                }

                return Path.Combine(path, ConfigFileName);
            }
        }

        /// <summary>
        /// Loads configuration from file or creates default
        /// </summary>
        public static LauncherConfig Load() => Load(ConfigFilePath);

        public static LauncherConfig Load(string configPath)
        {
            LoadWarning = null;

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    LauncherConfig config = JsonUtility.FromJson<LauncherConfig>(json);
                    if (config == null) throw new InvalidDataException("Configuration was empty.");
                    Debug.Log($"Loaded configuration from: {configPath}");
                    Debug.Log($"Config values - tablesDirectory: {config.tablesDirectory}, vpinballExecutable: {config.vpinballExecutable}");
                    return config;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error loading config: {ex.Message}");
                    LoadWarning = "Launcher configuration could not be read.";
                    Debug.Log("Using default configuration");
                    return CreateDefault();
                }
            }
            else
            {
                Debug.Log($"Config file not found at: {configPath}");
                LauncherConfig config = CreateDefault();
                config.Save(configPath);
                return config;
            }
        }

        /// <summary>
        /// Saves configuration to file
        /// </summary>
        public void Save(string configPath)
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                string temporary = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(temporary, json);
                    if (File.Exists(configPath)) File.Replace(temporary, configPath, null);
                    else File.Move(temporary, configPath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                Debug.Log($"Saved configuration to: {configPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a default configuration
        /// </summary>
        private static LauncherConfig CreateDefault()
        {
            LauncherConfig config = new LauncherConfig();

            // Try to auto-detect common installation paths
            string[] commonRoots = new string[]
            {
                @"C:\Visual Pinball", @"D:\Visual Pinball", @"C:\Games\Visual Pinball",
                @"C:\Program Files\Visual Pinball", @"C:\Program Files (x86)\Visual Pinball"
            };

            foreach (string root in commonRoots)
            {
                foreach (string name in new[] { "VPinballX_BGFX64.exe", "VPinballX_GL64.exe", "VPinballX64.exe" })
                {
                    string path = Path.Combine(root, name);
                    if (!File.Exists(path)) continue;
                    config.vpinballExecutable = path;
                    config.realVpinballExecutable = path;
                    config.tablesDirectory = Path.Combine(
                        Path.GetDirectoryName(path),
                        "Tables"
                    );
                    Debug.Log($"Auto-detected Visual Pinball at: {path}");
                    return config;
                }
            }

            return config;
        }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            try { return ValidatePaths(out errorMessage); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                errorMessage = "A configured path is invalid or inaccessible. Open setup to review locations.";
                return false;
            }
        }

        private bool ValidatePaths(out string errorMessage)
        {
            if (string.IsNullOrEmpty(vpinballExecutable))
            {
                errorMessage = "VPinball executable path is not set";
                return false;
            }

            if (!File.Exists(LauncherPaths.Resolve(vpinballExecutable)))
            {
                errorMessage = $"VPinball executable not found: {vpinballExecutable}";
                return false;
            }

            if (!Path.GetFileName(vpinballExecutable).StartsWith("VPinballX", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(realVpinballExecutable) && !File.Exists(LauncherPaths.Resolve(realVpinballExecutable)))
            {
                errorMessage = "The VPX target for the configured helper could not be found";
                return false;
            }

            if (string.IsNullOrEmpty(tablesDirectory))
            {
                errorMessage = "Tables directory is not set";
                return false;
            }

            if (!Directory.Exists(LauncherPaths.Resolve(tablesDirectory)))
            {
                errorMessage = $"Tables directory not found: {tablesDirectory}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public static void OpenSetupWindow() => OpenSetupWindow(Path.GetDirectoryName(ConfigFilePath));

        internal static void OpenSetupWindow(string directory)
        {
            if (SetupWindowOpen) return;
            string script = Path.Combine(directory, "tools", "setup.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("The setup tool is not installed. Reinstall the launcher or edit launcher-config.json.", script);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -File \"" + script + "\" -LauncherDirectory \"" + directory + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };
            setupProcess = StartSetupProcess(start);
            if (setupProcess == null) throw new InvalidOperationException("Windows did not start the setup tool.");
            Debug.Log("Desktop setup window opened.");
        }
    }
}
