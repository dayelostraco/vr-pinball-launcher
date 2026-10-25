using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRLauncher.Tests
{
    public class TableLauncherTests
    {
        private string root, table, executable;
        private GameObject owner;
        private TableLauncher launcher;
        private FakePlatform platform;

        private sealed class XrSession : IXrSession
        {
            public bool Initialized { get; set; }
            public bool HasLoader { get; set; } = true;
            public int Starts, Stops, Deinitializations, Disposed;
            public IEnumerator Initialize()
            {
                try { yield return null; Initialized = HasLoader; }
                finally { Disposed++; }
            }
            public void Start() { Starts++; }
            public void Stop() { Stops++; }
            public void Deinitialize() { Deinitializations++; Initialized = false; }
        }

        private sealed class FakePlatform : ITableLaunchPlatform
        {
            public string RuntimeLogPath { get; set; }
            public int Stops, Starts, Focuses, Launches;
            public Func<ProcessStartInfo, Process> Start;
            public Process Process;
            public ProcessStartInfo Requested;
            public bool FailRestart, FailFocus;
            public Process StartProcess(ProcessStartInfo info) { Launches++; Requested = info; return Process = Start(info); }
            public void StopXR() { Stops++; }
            public void StartXR() { Starts++; if (FailRestart) throw new InvalidOperationException("fixture XR failure"); }
            public void FocusWindow() { Focuses++; if (FailFocus) throw new InvalidOperationException("fixture focus failure"); }
        }

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "vrl-launch-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            table = Path.Combine(root, "Fixture.vpx");
            executable = Path.Combine(root, "fixture.exe");
            File.WriteAllText(table, "fixture, never opened by VPX");
            File.WriteAllText(executable, "placeholder, never executed");
            owner = new GameObject("Launch fixture");
            launcher = owner.AddComponent<TableLauncher>();
            platform = new FakePlatform { RuntimeLogPath = Path.Combine(root, "runtime.log") };
            launcher.Platform = platform;
            launcher.Configure(new LauncherConfig { vpinballExecutable = executable });
        }

        [TearDown]
        public void TearDown()
        {
            if (launcher.IsTableRunning()) launcher.KillCurrentTable();
            UnityEngine.Object.DestroyImmediate(owner);
            Directory.Delete(root, true);
        }

        [Test]
        public void MissingFilesFailBeforeStoppingXR()
        {
            File.Delete(executable);
            LogAssert.Expect(LogType.Error, new Regex("VPinballX executable not found"));
            Assert.IsFalse(launcher.LaunchTable(table));
            StringAssert.Contains("program could not be found", launcher.LastLaunchError);
            File.WriteAllText(executable, "fixture");
            File.Delete(table);
            LogAssert.Expect(LogType.Error, new Regex("Table file not found"));
            Assert.IsFalse(launcher.LaunchTable(table));
            StringAssert.Contains("table file could not be found", launcher.LastLaunchError);
            Assert.AreEqual(0, platform.Stops);
            Assert.AreEqual(0, platform.Launches);
            Assert.IsNull(launcher.GetCurrentTablePath());
        }

        private static IEnumerator DeferredRestart(Action cleanup, bool fail)
        {
            try
            {
                yield return null;
                if (fail) throw new InvalidOperationException("deferred XR failure");
            }
            finally { cleanup(); }
        }

        [Test]
        public void DeferredXrFailureNotifiesMenuOnceAndReleasesOperation()
        {
            int disposed = 0, notices = 0;
            launcher.OnXRRestartFailed += message => { notices++; StringAssert.Contains("VR could not restart", message); };
            launcher.BeginXrRestart(DeferredRestart(() => disposed++, true));
            launcher.PollXrRestart();
            Assert.AreEqual(0, notices);
            LogAssert.Expect(LogType.Error, "Could not restart XR: deferred XR failure");
            launcher.PollXrRestart();
            launcher.PollXrRestart();
            Assert.AreEqual(1, notices);
            Assert.AreEqual(1, disposed);
            StringAssert.Contains("VR could not restart", launcher.LastReturnNotice);
        }

        [Test]
        public void ReplacingOrCancellingRestartDisposesOldAttemptWithoutStaleFailure()
        {
            int disposed = 0;
            launcher.BeginXrRestart(DeferredRestart(() => disposed++, true));
            launcher.PollXrRestart();
            launcher.BeginXrRestart(DeferredRestart(() => disposed++, false));
            Assert.AreEqual(1, disposed);
            launcher.PollXrRestart();
            launcher.CancelXrRestart();
            launcher.CancelXrRestart();
            launcher.PollXrRestart();
            Assert.AreEqual(2, disposed);
            Assert.IsNull(launcher.LastReturnNotice);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void XrRestartStartsOnlyAnInitializedLoader(bool hasLoader)
        {
            var session = new XrSession { HasLoader = hasLoader };
            launcher.XrSession = session;
            launcher.StartXR();
            launcher.PollXrRestart();
            Assert.AreEqual(0, session.Starts);
            if (!hasLoader) LogAssert.Expect(LogType.Error, "Could not restart XR: Failed to initialize XR loader");
            launcher.PollXrRestart();
            Assert.AreEqual(hasLoader ? 1 : 0, session.Starts);
            Assert.AreEqual(1, session.Disposed);
            if (hasLoader)
            {
                launcher.StartXR();
                launcher.PollXrRestart();
                Assert.AreEqual(1, session.Starts, "A live XR session must not be initialized twice");
                launcher.StopXR();
                Assert.AreEqual(1, session.Stops);
                Assert.AreEqual(1, session.Deinitializations);
            }
        }

        [Test]
        public void StopCancelsIncompleteXrRestartBeforeItCanAcquireSession()
        {
            var session = new XrSession();
            launcher.XrSession = session;
            launcher.StartXR();
            launcher.PollXrRestart();
            launcher.StopXR();
            launcher.PollXrRestart();
            Assert.AreEqual(1, session.Disposed);
            Assert.AreEqual(0, session.Starts);
            Assert.AreEqual(0, session.Deinitializations);
            Assert.IsFalse(session.Initialized);
        }

        private static Process WaitingProcess(ProcessStartInfo ignored)
        {
            return Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c pause")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true });
        }

        [Test]
        public void RescueHotkeyRequiresANewPressAndClearsProgressOnReturn()
        {
            platform.Start = WaitingProcess;
            Assert.IsNull(launcher.ProgressText());
            Assert.IsTrue(launcher.LaunchTable(table));
            StringAssert.Contains("Starting VPX", launcher.ProgressText());
            StringAssert.Contains("Backspace", launcher.ProgressText());
            launcher.Tick(true);
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.IsFalse(launcher.LastSessionCounted);
            Assert.IsNull(launcher.ProgressText());
            Assert.IsTrue(launcher.LaunchTable(table));
            launcher.Tick(true);
            Assert.IsTrue(launcher.IsTableRunning(), "Held rescue key must not immediately stop the next launch");
            launcher.Tick(false);
            launcher.Tick(true);
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.AreEqual(2, platform.Starts);
        }

        [Test]
        public void ShutdownStopsOwnedProcessWithoutRestartingXrOrNotifyingMenu()
        {
            platform.Start = WaitingProcess;
            int exits = 0;
            launcher.OnTableExited += () => exits++;
            Assert.IsTrue(launcher.LaunchTable(table));
            using (var observed = Process.GetProcessById(platform.Process.Id))
            {
                launcher.Shutdown();
                Assert.IsTrue(observed.WaitForExit(5000));
                launcher.Shutdown();
            }
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.AreEqual(0, exits);
            Assert.AreEqual(0, platform.Starts);
        }

        [Test]
        public void OldFocusAttemptCannotAttachToTheNextLaunchedProcess()
        {
            platform.Start = WaitingProcess;
            Assert.IsTrue(launcher.LaunchTable(table));
            Assert.IsFalse(launcher.OwnsForegroundWindow);
            var focus = launcher.FocusVPinballWindow();
            Assert.IsTrue(focus.MoveNext());
            Assert.AreEqual(launcher.focusDelaySeconds, ((WaitForSecondsRealtime)focus.Current).waitTime);
            launcher.KillCurrentTable();
            Assert.IsTrue(launcher.LaunchTable(table));
            LogAssert.Expect(LogType.Warning, "Process exited before window focus could be set");
            Assert.IsFalse(focus.MoveNext());
            Assert.IsTrue(launcher.IsTableRunning());
        }

        [Test]
        public void AssignedProfileIsUsedAndShutdownHandlesAnAlreadyExitedHost()
        {
            launcher.Configure(new LauncherConfig {
                vpinballExecutable = "unused-default.exe",
                vpxProfiles = new[] { new VpxProfile { name = "Fixture", executable = executable, workingDirectory = "table" } },
                tableProfiles = new[] { null, new TableProfile { table = "Fixture", profile = "Fixture" } }
            });
            platform.Start = _ => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c exit 0") { UseShellExecute = false, CreateNoWindow = true });
            Assert.IsTrue(launcher.LaunchTable(table));
            Assert.AreEqual(executable, platform.Requested.FileName);
            Assert.AreEqual(root, platform.Requested.WorkingDirectory);
            Assert.IsTrue(platform.Process.WaitForExit(5000));
            launcher.Shutdown();
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.AreEqual(0, platform.Starts);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartFailureRestoresXRAndClearsRunningState(bool throws)
        {
            platform.Start = _ => throws ? throw new InvalidOperationException("fixture start failure") : null;
            LogAssert.Expect(LogType.Error, new Regex(throws ? "Error launching table:" : "Failed to start process"));
            Assert.IsFalse(launcher.LaunchTable(table));
            Assert.AreEqual(1, platform.Stops);
            Assert.AreEqual(1, platform.Starts);
            Assert.AreEqual(0, platform.Focuses);
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.IsNull(launcher.GetCurrentTablePath());
            Assert.IsNotNull(launcher.LastLaunchError);
        }

        [Test]
        public void InvalidProfilePathReturnsSetupGuidanceWithoutTouchingXR()
        {
            launcher.Configure(new LauncherConfig { vpinballExecutable = "bad\0path" });
            LogAssert.Expect(LogType.Error, new Regex("Could not resolve launch paths:"));
            Assert.IsFalse(launcher.LaunchTable(table));
            StringAssert.Contains("invalid path", launcher.LastLaunchError);
            Assert.AreEqual(0, platform.Stops);
        }

        [Test]
        public void FocusAndXRFailuresDoNotLoseProcessOwnershipOrCompletion()
        {
            platform.FailFocus = true;
            platform.FailRestart = true;
            platform.Start = _ => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c exit 0") { UseShellExecute = false, CreateNoWindow = true });
            int exited = 0;
            launcher.OnTableExited += () => exited++;
            LogAssert.Expect(LogType.Warning, new Regex("Window focus could not be scheduled:"));
            Assert.IsTrue(launcher.LaunchTable(table));
            Assert.IsTrue(launcher.IsTableRunning());
            Assert.IsTrue(platform.Process.WaitForExit(5000));
            LogAssert.Expect(LogType.Error, new Regex("Could not restart XR:"));
            launcher.PollProcess();
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.AreEqual(1, exited);
            StringAssert.Contains("VR could not restart", launcher.LastReturnNotice);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletionUsesOnlyFreshMatchingLogAndNotifiesOnce(bool freshStartup)
        {
            File.WriteAllText(platform.RuntimeLogPath, "LoadGameFromFilename " + table + "\nStartup done\n");
            platform.Start = _ => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c exit 0") { UseShellExecute = false, CreateNoWindow = true });
            int exited = 0;
            launcher.OnTableExited += () => exited++;
            Assert.IsTrue(launcher.LaunchTable(table));
            Assert.AreEqual(table, launcher.GetCurrentTablePath());
            Assert.AreEqual(1, platform.Focuses);
            Assert.AreEqual(executable, platform.Requested.FileName);
            Assert.IsFalse(platform.Requested.UseShellExecute);
            StringAssert.Contains(table, platform.Requested.Arguments);
            LogAssert.Expect(LogType.Warning, "A table is already running!");
            Assert.IsFalse(launcher.LaunchTable(table));
            Assert.AreEqual(1, platform.Launches);
            if (freshStartup) File.AppendAllText(platform.RuntimeLogPath, "LoadGameFromFilename " + table + "\nStartup done\n");
            Assert.IsTrue(platform.Process.WaitForExit(5000));
            launcher.PollProcess();
            launcher.PollProcess();
            Assert.IsNull(launcher.ProgressText());
            Assert.AreEqual(1, exited);
            Assert.AreEqual(1, platform.Starts);
            Assert.IsFalse(launcher.IsTableRunning());
            Assert.IsNull(launcher.GetCurrentTablePath());
            Assert.AreEqual(freshStartup, launcher.LastStartupConfirmed);
            Assert.AreEqual(freshStartup, launcher.LastSessionCounted);
        }
    }
}
