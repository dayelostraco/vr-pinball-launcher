using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace VRLauncher.Tests
{
    public class LauncherBootstrapTests
    {
        private string root, statePath;
        private GameObject owner, cameraObject;
        private LauncherBootstrap bootstrap;
        private LauncherConfig config;
        private ArcRoom room;
        private bool previousBackground;
        private int previousVSync, previousFrameRate;

        private sealed class Platform : ITableLaunchPlatform
        {
            public string RuntimeLogPath { get; set; }
            public bool FailStart;
            public int Starts, Restarts;
            public Process Process;
            public Process StartProcess(ProcessStartInfo info)
            {
                Starts++;
                if (FailStart) throw new InvalidOperationException("fixture start failed");
                return Process = System.Diagnostics.Process.Start(new ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c exit 0")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            public void StopXR() { }
            public void StartXR() { Restarts++; }
            public void FocusWindow() { }
        }

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "vrl-bootstrap-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            statePath = Path.Combine(root, "state.json");
            string executable = Path.Combine(root, "VPinballX64.exe");
            File.WriteAllText(executable, "not executed");
            config = new LauncherConfig { tablesDirectory = root, vpinballExecutable = executable, wheelDirectory = "", tableMediaDirectory = "" };
            owner = new GameObject("Bootstrap fixture");
            cameraObject = new GameObject("Head fixture", typeof(Camera));
            bootstrap = owner.AddComponent<LauncherBootstrap>();
            previousBackground = Application.runInBackground;
            previousVSync = QualitySettings.vSyncCount;
            previousFrameRate = Application.targetFrameRate;
            bootstrap.TrackingAvailable = () => true;
            bootstrap.FrameSeconds = () => 1f;
            bootstrap.Schedule = Drain;
        }

        [TearDown]
        public void TearDown()
        {
            // Initialize is driven explicitly in EditMode, where this component's
            // ordinary runtime OnDestroy callback is not guaranteed to run.
            bootstrap.DisposeScene();
            var launcher = owner.GetComponent<TableLauncher>();
            if (launcher != null && launcher.IsTableRunning()) launcher.KillCurrentTable();
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            Application.runInBackground = previousBackground;
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousFrameRate;
            Directory.Delete(root, true);
        }

        private ArcRoom Build()
        {
            room = bootstrap.Initialize(config, statePath, cameraObject.GetComponent<Camera>());
            cameraObject.GetComponentInChildren<ScreenFader>().FrameSeconds = () => 0.2f;
            return room;
        }

        [Test]
        public void InvalidCatalogPathStillBuildsAnAccessibleSetupMenu()
        {
            config.tablesDirectory = "invalid\0path";
            LogAssert.Expect(LogType.Warning, new Regex("Could not read the tables folder:"));
            Build();
            Drain(bootstrap.Arrive());
            Assert.IsNotNull(room);
            Assert.IsTrue(owner.GetComponent<LauncherInput>().InputEnabled);
            Assert.IsFalse(owner.GetComponent<TableLauncher>().IsTableRunning());
        }

        private static void Drain(IEnumerator routine)
        {
            int steps = 0;
            try
            {
                while (routine.MoveNext())
                {
                    Assert.Less(++steps, 100, "Fixture coroutine did not finish");
                    if (routine.Current is IEnumerator nested && !(routine.Current is CustomYieldInstruction)) Drain(nested);
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }

        [Test]
        public void EmptyCatalogBuildsNavigableViewsAndExplainsLocation()
        {
            Build();
            StringAssert.Contains(root, room.InfoStatus);
            Assert.IsNotNull(owner.GetComponent<TableLauncher>());
            var input = owner.GetComponent<LauncherInput>();
            Assert.IsFalse(input.InputEnabled, "Input must wait for headset arrival");
            input.InputEnabled = true;
            MenuInputFrame frame = default;
            input.ReadInput = () => frame;
            input.Tick(0.02f);
            frame.Buttons = MenuButtons.View;
            input.Tick(0.02f);
            StringAssert.Contains("[ FAVORITES ]", room.ViewTabs);
            Assert.Throws<InvalidOperationException>(() => bootstrap.Initialize(config, statePath, cameraObject.GetComponent<Camera>()));
        }

        [Test]
        public void NavigationAndFavoriteActionsUpdateRoomAndPersistOnlyFixtureState()
        {
            File.WriteAllText(Path.Combine(root, "Alpha (Maker 1995).vpx"), "fixture");
            File.WriteAllText(Path.Combine(root, "Bravo (Maker 1996).vpx"), "fixture");
            Build();
            var input = owner.GetComponent<LauncherInput>();
            MenuInputFrame frame = default;
            input.ReadInput = () => frame;
            input.InputEnabled = true;
            input.Tick(0.02f);
            Assert.AreEqual("Alpha", room.InfoTitle);
            frame.Buttons = MenuButtons.RightArrow;
            input.Tick(0.02f);
            Assert.AreEqual("Bravo", room.InfoTitle);
            frame.Buttons = MenuButtons.Favorite;
            input.Tick(0.02f);
            Assert.IsTrue(LauncherState.Load(statePath).IsFavorite("Bravo (Maker 1996).vpx"));
            frame.Buttons = MenuButtons.Details;
            input.Tick(0.02f);
            Assert.IsTrue(room.DetailsVisible);
            StringAssert.Contains("Not yet tested", room.DetailsText);
            frame.Buttons = MenuButtons.LeftArrow;
            input.Tick(0.02f);
            Assert.AreEqual("Alpha", room.InfoTitle);
        }

        [Test]
        public void MissingHeadCameraFailsBeforeCreatingSceneObjects()
        {
            Assert.Throws<ArgumentNullException>(() => bootstrap.Initialize(config, statePath, null));
            Assert.IsNull(owner.GetComponent<TableLauncher>());
            Assert.IsFalse(File.Exists(statePath));
        }

        [Test]
        public void SceneDisposalReleasesOwnedRoomAndCanBeRepeated()
        {
            Build();
            bootstrap.DisposeScene();
            Assert.IsTrue(room == null);
            Assert.IsNull(cameraObject.GetComponentInChildren<ScreenFader>());
            Assert.DoesNotThrow(() => bootstrap.DisposeScene());
        }

        [Test]
        public void HeadsetTimeoutUsesProvidedCameraAndEnablesNavigation()
        {
            Build();
            bootstrap.TrackingAvailable = () => false;
            cameraObject.transform.position = new Vector3(2f, 1f, 3f);
            LogAssert.Expect(LogType.Warning, "Headset not tracked after 5 s; placing the room from the current camera pose.");
            Drain(bootstrap.Arrive());
            Assert.IsTrue(owner.GetComponent<LauncherInput>().InputEnabled);
            Assert.AreEqual(0f, cameraObject.GetComponentInChildren<ScreenFader>().Alpha);
            Assert.AreEqual(2f, room.transform.position.x);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LaunchReturnRestoresRenderingAndRecordsOnlyConfirmedPlays(bool startupConfirmed)
        {
            string table = Path.Combine(root, "Alpha (Maker 1995).vpx");
            File.WriteAllText(table, "fixture");
            Build();
            var platform = new Platform { RuntimeLogPath = Path.Combine(root, "runtime.log") };
            var launcher = owner.GetComponent<TableLauncher>();
            launcher.Platform = platform;
            Drain(bootstrap.Arrive());
            var input = owner.GetComponent<LauncherInput>();
            MenuInputFrame frame = default;
            input.ReadInput = () => frame;
            input.Tick(0.02f);
            frame.Buttons = MenuButtons.Enter;
            input.Tick(0.02f);
            Assert.IsFalse(input.InputEnabled);
            Assert.IsFalse(cameraObject.GetComponent<Camera>().enabled);
            Assert.AreEqual(60, Application.targetFrameRate);
            Drain(bootstrap.LaunchSelected());
            Assert.AreEqual(1, platform.Starts, "Busy launch created a second process");
            if (startupConfirmed) File.WriteAllText(platform.RuntimeLogPath, "LoadGameFromFilename " + table + "\nStartup done\n");
            Assert.IsTrue(platform.Process.WaitForExit(5000));
            launcher.PollProcess();
            Assert.IsTrue(input.InputEnabled);
            Assert.IsTrue(cameraObject.GetComponent<Camera>().enabled);
            Assert.AreEqual(previousFrameRate, Application.targetFrameRate);
            Assert.AreEqual(previousVSync, QualitySettings.vSyncCount);
            var state = LauncherState.Load(statePath);
            Assert.AreEqual(startupConfirmed ? 1 : 0, state.PlayCount("Alpha (Maker 1995).vpx"));
            Assert.AreEqual(startupConfirmed, state.StartupConfirmed("Alpha (Maker 1995).vpx"));
        }

        [Test]
        public void FailedLaunchReturnsToMenuWithSavedExplanation()
        {
            File.WriteAllText(Path.Combine(root, "Alpha.vpx"), "fixture");
            Build();
            owner.GetComponent<TableLauncher>().Platform = new Platform { RuntimeLogPath = Path.Combine(root, "runtime.log"), FailStart = true };
            LogAssert.Expect(LogType.Error, new Regex("Error launching table:"));
            Drain(bootstrap.LaunchSelected());
            Assert.IsTrue(owner.GetComponent<LauncherInput>().InputEnabled);
            Assert.IsTrue(cameraObject.GetComponent<Camera>().enabled);
            StringAssert.Contains("retry", room.InfoStatus);
            var saved = LauncherState.Load(statePath);
            Assert.IsNotNull(saved.LastLaunchProblem("Alpha.vpx"));
            Assert.AreEqual(0, saved.PlayCount("Alpha.vpx"));
        }
    }
}
