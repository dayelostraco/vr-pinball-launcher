using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Starts an external VR-controller input bridge process when the launcher
    /// starts and terminates it when the launcher exits. The bridge reads the VR
    /// controllers and simulates keyboard input for VPinballX (it cannot live in
    /// Unity, since Unity releases its XR session while a table is running).
    ///
    /// Configured via LauncherConfig: enableControllerBridge, controllerBridgePath,
    /// controllerBridgeArgs, controllerBridgeWorkingDir.
    /// </summary>
    public class ControllerBridge : MonoBehaviour
    {
        private Process bridgeProcess;
        internal Func<ProcessStartInfo, Process> StartProcess { get; set; } = Process.Start;

        void Start() => StartBridge(LauncherConfig.Instance, LauncherPaths.AppDirectory);

        internal void StartBridge(LauncherConfig config, string appDirectory)
        {
            if (!config.enableControllerBridge) return;
            try
            {
                // Repeated initialization must not orphan an earlier bridge.
                if (bridgeProcess != null && !bridgeProcess.HasExited) return;
                bridgeProcess?.Dispose();
                bridgeProcess = null;
                string path = VpxPaths.Resolve(config.controllerBridgePath, appDirectory);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    UnityEngine.Debug.LogWarning($"ControllerBridge: executable not found: '{path}'. Review controller setup.");
                    return;
                }
                string workingDir = VpxPaths.Resolve(config.controllerBridgeWorkingDir, appDirectory);
                if (string.IsNullOrEmpty(workingDir)) workingDir = Path.GetDirectoryName(path);
                string arguments = BridgeArguments.Resolve(config.controllerBridgeArgs, appDirectory);
                // Validate before starting AutoHotkey so missing native libraries
                // cannot open an error dialog over the launcher.
                if (!BridgeArguments.Validate(path, arguments, workingDir, appDirectory, new DiskFileSystem(), out string missing))
                {
                    UnityEngine.Debug.LogWarning($"ControllerBridge: not starting the bridge -- {missing} Review controller setup.");
                    return;
                }
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = workingDir
                };
                bridgeProcess = StartProcess(startInfo);
                if (bridgeProcess == null)
                {
                    UnityEngine.Debug.LogError("ControllerBridge: Windows returned no bridge process.");
                    return;
                }
                UnityEngine.Debug.Log($"ControllerBridge: started '{path}' {arguments} (cwd: {workingDir})");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"ControllerBridge: failed to start: {ex.Message}");
            }
        }

        void OnApplicationQuit()
        {
            KillBridge();
        }

        void OnDestroy()
        {
            KillBridge();
        }

        internal void KillBridge()
        {
            if (bridgeProcess == null)
            {
                return;
            }

            try
            {
                if (!bridgeProcess.HasExited)
                {
                    bridgeProcess.Kill();
                    bridgeProcess.WaitForExit(2000);
                    UnityEngine.Debug.Log("ControllerBridge: bridge process terminated");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"ControllerBridge: error killing bridge: {ex.Message}");
            }
            finally
            {
                bridgeProcess.Dispose();
                bridgeProcess = null;
            }
        }

    }
}
