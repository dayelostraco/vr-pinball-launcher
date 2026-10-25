using System;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class LauncherStateTests
    {
        private string dir;
        private string file;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-state-" + Guid.NewGuid().ToString("N"));
            file = Path.Combine(dir, "state.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static readonly DateTime Noon = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [TestCase("{\"path\":\"A.vpx\",\"count\":-5}")]
        [TestCase("{\"path\":\"\",\"count\":1}")]
        [TestCase("{\"path\":\"A.vpx\",\"count\":true}")]
        [TestCase("{\"path\":\"A.vpx\",\"count\":1.5}")]
        [TestCase("{\"path\":\"A.vpx\",\"count\":2147483648}")]
        public void Load_InvalidRecord_PreservesOriginalAndStartsFresh(string record)
        {
            Directory.CreateDirectory(dir);
            string original = "{\"version\":1,\"favorites\":[],\"plays\":[" + record + "]}";
            File.WriteAllText(file, original);
            var state = LauncherState.Load(file);
            Assert.IsNotNull(state.LoadWarning);
            Assert.AreEqual(0, state.PlayCount("A.vpx"));
            Assert.AreEqual(original, File.ReadAllText(file + ".bad"));
        }

        [Test]
        public void Load_MissingFile_IsEmpty()
        {
            LauncherState state = LauncherState.Load(file);
            Assert.IsFalse(state.IsFavorite("a.vpx"));
            Assert.IsEmpty(state.RecentPaths());
            Assert.IsNull(state.LoadWarning);
        }

        [Test]
        public void Favorites_SurviveSaveAndLoad()
        {
            LauncherState state = LauncherState.Load(file);
            Assert.IsTrue(state.ToggleFavorite("a.vpx"));
            state.Save();

            LauncherState reloaded = LauncherState.Load(file);
            Assert.IsTrue(reloaded.IsFavorite("a.vpx"));
            Assert.IsFalse(reloaded.ToggleFavorite("a.vpx"));
            Assert.IsFalse(reloaded.IsFavorite("a.vpx"));
        }

        [Test]
        public void Browsing_SurvivesSaveAndLoadWithoutLosingHistory()
        {
            var state = LauncherState.Load(file);
            state.ToggleFavorite("a.vpx");
            state.RecordPlay("a.vpx", Noon);
            state.RememberBrowsing(ViewKind.Favorites, "a.vpx");
            state.Save();
            var restored = LauncherState.Load(file);
            Assert.AreEqual(ViewKind.Favorites, restored.LastView);
            Assert.AreEqual("a.vpx", restored.LastSelectedPath);
            Assert.IsTrue(restored.IsFavorite("a.vpx"));
            Assert.AreEqual(1, restored.PlayCount("a.vpx"));
        }

        [Test]
        public void Browsing_OldStateDefaultsToAllAndInvalidViewIsSafe()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{\"version\":1,\"favorites\":[],\"plays\":[]}");
            var state = LauncherState.Load(file);
            Assert.AreEqual(ViewKind.All, state.LastView);
            Assert.IsNull(state.LastSelectedPath);
            state.RememberBrowsing((ViewKind)99, "gone.vpx");
            state.Save();
            Assert.AreEqual(ViewKind.All, LauncherState.Load(file).LastView);
        }

        [Test]
        public void RecordPlay_TracksCountAndLastPlayed()
        {
            LauncherState state = LauncherState.Load(file);
            state.RecordPlay("a.vpx", Noon);
            state.RecordPlay("a.vpx", Noon.AddHours(1));
            state.Save();

            LauncherState reloaded = LauncherState.Load(file);
            Assert.AreEqual(2, reloaded.PlayCount("a.vpx"));
            Assert.AreEqual(Noon.AddHours(1), reloaded.LastPlayed("a.vpx"));
            Assert.IsNull(reloaded.LastPlayed("b.vpx"));
        }

        [Test]
        public void RecentPaths_NewestFirst()
        {
            LauncherState state = LauncherState.Load(file);
            state.RecordPlay("old.vpx", Noon);
            state.RecordPlay("new.vpx", Noon.AddDays(1));
            state.RecordPlay("mid.vpx", Noon.AddHours(5));
            CollectionAssert.AreEqual(new[] { "new.vpx", "mid.vpx", "old.vpx" }, state.RecentPaths());
        }

        [Test]
        public void Load_Corrupt_IsSetAsideAndStartsEmpty()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{ not json");

            LauncherState state = LauncherState.Load(file);

            Assert.IsFalse(File.Exists(file));
            Assert.AreEqual("{ not json", File.ReadAllText(file + ".bad"));
            Assert.IsNotNull(state.LoadWarning);
            Assert.IsEmpty(state.RecentPaths());
        }

        [Test]
        public void Load_EmptyFile_IsSetAside()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "");

            LauncherState state = LauncherState.Load(file);

            Assert.IsTrue(File.Exists(file + ".bad"));
            Assert.IsNotNull(state.LoadWarning);
        }

        [Test]
        public void Load_Corrupt_ReplacesAnOlderBadFile()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file + ".bad", "older");
            File.WriteAllText(file, "newer garbage");

            LauncherState.Load(file);

            Assert.AreEqual("newer garbage", File.ReadAllText(file + ".bad"));
        }

        [Test]
        public void Load_UnknownVersion_IsSetAside()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{\"version\": 99, \"favorites\": [], \"plays\": []}");

            LauncherState state = LauncherState.Load(file);

            Assert.IsNotNull(state.LoadWarning);
            Assert.IsTrue(File.Exists(file + ".bad"));
        }

        [Test]
        public void Load_FileLockedByAnotherProcess_StartsFreshWithAWarning()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{\"version\": 1, \"favorites\": [], \"plays\": []}");

            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                LauncherState state = LauncherState.Load(file);

                Assert.IsNotNull(state.LoadWarning);
                Assert.IsFalse(state.IsFavorite("a.vpx"));
                Assert.IsEmpty(state.RecentPaths());
            }
        }

        [Test]
        public void Save_CreatesTheDirectory()
        {
            LauncherState state = LauncherState.Load(file);
            state.ToggleFavorite("a.vpx");
            state.Save();
            Assert.IsTrue(File.Exists(file));
            Assert.IsFalse(File.Exists(file + ".tmp"));
        }

        [Test]
        public void MaximumPlayCountNeverWrapsNegativeAndLatestTimeStillUpdates()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{\"version\":1,\"favorites\":[],\"plays\":[{\"path\":\"A.vpx\",\"count\":2147483647}]}");
            var state = LauncherState.Load(file);
            state.RecordPlay("A.vpx", Noon);
            state.Save();
            var restored = LauncherState.Load(file);
            Assert.IsNull(restored.LoadWarning);
            Assert.AreEqual(int.MaxValue, restored.PlayCount("A.vpx"));
            Assert.AreEqual(Noon, restored.LastPlayed("A.vpx"));
        }

        [Test]
        public void FailedReplacementPreservesOriginalAndRemovesTemporaryFile()
        {
            var state = LauncherState.Load(file);
            state.Save();
            string original = File.ReadAllText(file);
            state.ToggleFavorite("A.vpx");
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => state.Save());
            Assert.AreEqual(original, File.ReadAllText(file));
            Assert.AreEqual(1, Directory.GetFiles(dir).Length);
        }

        [Test]
        public void FailedQuarantinePreservesUnreadableStateAndExplainsFailure()
        {
            Directory.CreateDirectory(file + ".bad");
            File.WriteAllText(file, "bad json");
            var state = LauncherState.Load(file);
            StringAssert.Contains("could not move it aside", state.LoadWarning);
            Assert.AreEqual("bad json", File.ReadAllText(file));
        }
    }
}
