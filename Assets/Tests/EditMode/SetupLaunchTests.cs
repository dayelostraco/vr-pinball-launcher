using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class SetupLaunchTests
    {
        private string root;
        private Func<ProcessStartInfo, Process> originalStarter;
        private Process child;
        private int starts;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(LauncherConfig.SetupWindowOpen, "Close setup before running the isolated lifecycle tests");
            root = Path.Combine(Path.GetTempPath(), "vrl setup test " + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(root, "tools"));
            File.WriteAllText(Path.Combine(root, "tools", "setup.ps1"), "# not executed");
            originalStarter = LauncherConfig.StartSetupProcess;
            starts = 0;
            LauncherConfig.StartSetupProcess = info =>
            {
                starts++;
                Assert.IsFalse(info.UseShellExecute);
                Assert.IsTrue(info.CreateNoWindow);
                Assert.AreEqual(root, info.WorkingDirectory);
                StringAssert.Contains("\"" + Path.Combine(root, "tools", "setup.ps1") + "\"", info.Arguments);
                StringAssert.Contains("-LauncherDirectory \"" + root + "\"", info.Arguments);
                return child = Process.Start(new ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c pause")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true });
            };
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (LauncherConfig.SetupWindowOpen) { child.Kill(); Assert.IsTrue(child.WaitForExit(5000)); }
                Assert.IsFalse(LauncherConfig.SetupWindowOpen);
            }
            finally
            {
                LauncherConfig.StartSetupProcess = originalStarter;
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void SetupOwnsInputUntilExitAndRepeatedOpenDoesNotCreateAnotherWindow()
        {
            LauncherConfig.OpenSetupWindow(root);
            Assert.IsTrue(LauncherConfig.SetupWindowOpen);
            LauncherConfig.OpenSetupWindow(root);
            Assert.AreEqual(1, starts);
            child.Kill();
            Assert.IsTrue(child.WaitForExit(5000));
            Assert.IsFalse(LauncherConfig.SetupWindowOpen);
            LauncherConfig.OpenSetupWindow(root);
            Assert.AreEqual(2, starts);
            Assert.IsTrue(LauncherConfig.SetupWindowOpen);
        }

        [Test]
        public void MissingSetupToolCannotStartAnyProcess()
        {
            File.Delete(Path.Combine(root, "tools", "setup.ps1"));
            Assert.Throws<FileNotFoundException>(() => LauncherConfig.OpenSetupWindow(root));
            Assert.AreEqual(0, starts);
            Assert.IsFalse(LauncherConfig.SetupWindowOpen);
        }

        [Test]
        public void FailedSetupStartDoesNotSuppressMenu()
        {
            LauncherConfig.StartSetupProcess = _ => null;
            Assert.Throws<InvalidOperationException>(() => LauncherConfig.OpenSetupWindow(root));
            Assert.IsFalse(LauncherConfig.SetupWindowOpen);
            LauncherConfig.StartSetupProcess = _ => throw new InvalidOperationException("fixture start failure");
            Assert.Throws<InvalidOperationException>(() => LauncherConfig.OpenSetupWindow(root));
            Assert.IsFalse(LauncherConfig.SetupWindowOpen);
        }
    }
}
