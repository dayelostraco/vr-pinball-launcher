using System;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public sealed class TableReadinessTests
    {
        private string root, table, exe, statePath;
        private LauncherState state;
        private LaunchCommand command;
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "readiness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            table = Path.Combine(root, "Example.vpx"); exe = Path.Combine(root, "VPinballX_BGFX64.exe");
            File.WriteAllText(table, "fixture"); File.WriteAllText(exe, "fixture");
            statePath = Path.Combine(root, "state.json"); state = LauncherState.Load(statePath);
            command = new LaunchCommand(exe, "", root, "Special profile");
        }
        [TearDown] public void TearDown() => Directory.Delete(root, true);
        private string Describe() => TableReadiness.Describe(table, command, exe, new[] { root }, state, "Example.vpx");

        [Test] public void UnexaminedEmbeddedScript_RemainsUnknown()
        {
            StringAssert.Contains("Not yet tested", Describe());
            StringAssert.Contains("requirements: unknown", Describe());
            StringAssert.Contains("Special profile", Describe());
        }
        [Test] public void MissingExecutable_RequiresAttentionEvenAfterStartup()
        {
            state.RecordLaunch("Example.vpx", DateTime.UtcNow, true, null); File.Delete(exe);
            StringAssert.Contains("Needs attention", Describe());
            StringAssert.Contains("Launch program missing", Describe());
        }
        [Test] public void OneLiteralRom_IsCheckedWithoutClaimingGameplay()
        {
            File.WriteAllText(Path.ChangeExtension(table, ".vbs"), "' cGameName = \"wrong\"\nConst cGameName = \"afm_113b\"");
            StringAssert.Contains("afm_113b: not found", Describe());
            File.WriteAllText(Path.Combine(root, "afm_113b.zip"), "not a real zip");
            StringAssert.Contains("ZIP found (contents unverified)", Describe());
            StringAssert.Contains("Not yet tested", Describe());
        }
        [Test] public void MultipleRomAssignments_AreNotGuessed()
        {
            File.WriteAllText(Path.ChangeExtension(table, ".vbs"), "cGameName = \"first\"\ncGameName = \"second\"");
            StringAssert.Contains("requirements: unknown", Describe());
        }
        [Test] public void ManifestChecksNativePluginAndRejectsTraversal()
        {
            File.WriteAllText(table + ".readiness.json", "{\"rom\":\"afm_113b\",\"plugins\":[\"serum\"]}");
            StringAssert.Contains("serum: native plugin not detected", Describe());
            string dir = Path.Combine(root, "plugins", "serum"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin-serum64.dll"), "fixture");
            StringAssert.Contains("serum: installed (enablement unverified)", Describe());
            File.WriteAllText(table + ".readiness.json", "{\"rom\":\"../outside\"}");
            StringAssert.Contains("Requirements could not be read", Describe());
        }
        [Test] public void LaunchResultsPersistAndAreSeparateFromPlayCounts()
        {
            DateTime when = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
            state.ToggleFavorite("Example.vpx"); state.RecordPlay("Example.vpx", when);
            state.RecordLaunch("Example.vpx", when, false, "Required ROM missing"); state.Save();
            state = LauncherState.Load(statePath);
            Assert.AreEqual(when, state.LastLaunchTime("Example.vpx"));
            Assert.AreEqual(1, state.PlayCount("Example.vpx")); Assert.IsTrue(state.IsFavorite("Example.vpx"));
            StringAssert.Contains("Required ROM missing", Describe()); StringAssert.Contains("Needs attention", Describe());
            state.RecordLaunch("Example.vpx", when.AddMinutes(1), true, null); state.Save(); state = LauncherState.Load(statePath);
            StringAssert.Contains("Startup confirmed - gameplay unverified", Describe());
            Assert.IsNull(state.LastLaunchProblem("Example.vpx")); Assert.AreEqual(1, state.PlayCount("Example.vpx"));
        }
        [Test] public void OldStateWithoutLaunchHistoryLoadsWithoutLoss()
        {
            File.WriteAllText(statePath, "{\"version\":1,\"favorites\":[\"Example.vpx\"],\"plays\":[]}");
            state = LauncherState.Load(statePath);
            Assert.IsTrue(state.IsFavorite("Example.vpx")); Assert.IsNull(state.LastLaunchTime("Example.vpx"));
            state.RecordLaunch("Example.vpx", DateTime.UtcNow, true, null); state.Save();
            Assert.IsTrue(LauncherState.Load(statePath).StartupConfirmed("Example.vpx"));
        }
    }
}
