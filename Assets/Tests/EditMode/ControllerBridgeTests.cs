using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRLauncher.Tests
{
    public class ControllerBridgeTests
    {
        private string root;
        private GameObject owner;
        private ControllerBridge bridge;
        private LauncherConfig config;
        private int starts;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "vrl-bridge-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            owner = new GameObject("Bridge fixture");
            bridge = owner.AddComponent<ControllerBridge>();
            config = new LauncherConfig { enableControllerBridge = true, controllerBridgePath = "custom.exe" };
            File.WriteAllText(Path.Combine(root, "custom.exe"), "not executed");
            starts = 0;
            bridge.StartProcess = _ => { starts++; return null; };
        }

        [TearDown]
        public void TearDown()
        {
            bridge.KillBridge();
            UnityEngine.Object.DestroyImmediate(owner);
            Directory.Delete(root, true);
        }

        [Test]
        public void DisabledAndMissingBridgeNeverStartProcess()
        {
            config.enableControllerBridge = false;
            bridge.StartBridge(config, root);
            config.enableControllerBridge = true;
            config.controllerBridgePath = "missing.exe";
            LogAssert.Expect(LogType.Warning, new Regex("executable not found"));
            bridge.StartBridge(config, root);
            Assert.AreEqual(0, starts);
        }

        [Test]
        public void MissingAutoHotkeyDependencyDoesNotLaunchErrorDialog()
        {
            config.controllerBridgePath = "AutoHotkey.exe";
            config.controllerBridgeArgs = "\"controls.ahk\"";
            File.WriteAllText(Path.Combine(root, "AutoHotkey.exe"), "not executed");
            File.WriteAllText(Path.Combine(root, "controls.ahk"), "; fixture");
            LogAssert.Expect(LogType.Warning, new Regex("not starting the bridge"));
            bridge.StartBridge(config, root);
            Assert.AreEqual(0, starts);
        }

        [Test]
        public void NativeStartFailureIsReportedWithoutLeavingOwnedProcess()
        {
            bridge.StartProcess = _ => throw new InvalidOperationException("fixture failure");
            LogAssert.Expect(LogType.Error, new Regex("failed to start: fixture failure"));
            bridge.StartBridge(config, root);
            Assert.DoesNotThrow(() => bridge.KillBridge());
        }

        [Test]
        public void NullProcessResultIsReportedAsFailure()
        {
            LogAssert.Expect(LogType.Error, "ControllerBridge: Windows returned no bridge process.");
            bridge.StartBridge(config, root);
            Assert.AreEqual(1, starts);
        }

        [Test]
        public void InvalidConfiguredPathIsReportedWithoutEscapingStartup()
        {
            config.controllerBridgePath = "bad\0path";
            LogAssert.Expect(LogType.Error, new Regex("failed to start:"));
            bridge.StartBridge(config, root);
            Assert.AreEqual(0, starts);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PathsAreResolvedAndOwnedProcessIsClosed(bool alreadyExited)
        {
            config.controllerBridgeArgs = "--custom-option";
            Process process = null;
            bridge.StartProcess = info =>
            {
                Assert.AreEqual(Path.Combine(root, "custom.exe"), info.FileName);
                Assert.AreEqual(root, info.WorkingDirectory);
                Assert.AreEqual("--custom-option", info.Arguments);
                Assert.IsFalse(info.UseShellExecute);
                string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                // Both commands remain local; the long-lived fixture waits for
                // keyboard input and is terminated through the bridge owner.
                process = Process.Start(new ProcessStartInfo(shell, alreadyExited ? "/c exit 0" : "/c pause")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true });
                return process;
            };
            bridge.StartBridge(config, root);
            Assert.IsNotNull(process);
            if (alreadyExited) Assert.IsTrue(process.WaitForExit(5000));
            else
            {
                Process first = process;
                bridge.StartBridge(config, root);
                Assert.AreSame(first, process, "Repeated initialization orphaned the existing bridge");
            }
            int id = process.Id;
            bridge.KillBridge();
            Assert.DoesNotThrow(() => bridge.KillBridge());
            // Windows may retain an exited PID briefly while another handle is
            // closing; termination, rather than disappearance, is the contract.
            try { using (var remaining = Process.GetProcessById(id)) Assert.IsTrue(remaining.HasExited); }
            catch (ArgumentException) { }
        }
    }
}
