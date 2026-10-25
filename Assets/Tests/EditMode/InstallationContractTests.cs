using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public sealed class InstallationContractTests
    {
        [Serializable] private sealed class Fixture { public Case[] cases; }
        [Serializable] private sealed class Case
        {
            public string name, runner, table, profileName, profileRunner, ini, working, assignment;
            public string expectedRunner, expectedWorking, expectedIni, expectedProfile;
        }
        [Test] public void LaunchesMatchSharedSetupContract()
        {
            string root = Path.Combine(Path.GetTempPath(), "Launcher contract");
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "..", "tests", "fixtures", "launch-contract.json"));
            foreach (Case c in JsonUtility.FromJson<Fixture>(json).cases)
            {
                var profiles = new[] { new VpxProfile { name = c.profileName, executable = c.profileRunner, iniFile = c.ini, workingDirectory = c.working } };
                var assignments = string.IsNullOrEmpty(c.assignment) ? Array.Empty<TableProfile>() : new[] { new TableProfile { table = "sAMPLE", profile = c.assignment } };
                LaunchCommand cmd = VpxProfiles.Resolve(c.table, c.runner, profiles, assignments, root);
                Assert.AreEqual(Path.Combine(root, c.expectedRunner), cmd.Executable, c.name);
                Assert.AreEqual(Path.Combine(root, c.expectedWorking), cmd.WorkingDirectory, c.name);
                Assert.AreEqual(c.expectedProfile, cmd.ProfileName ?? "", c.name);
                string ini = string.IsNullOrEmpty(c.expectedIni) ? "" : Path.Combine(root, c.expectedIni);
                Assert.AreEqual(ini, cmd.IniFile ?? "", c.name);
                Assert.AreEqual((ini.Length == 0 ? "" : "-Ini \"" + ini + "\" ") + "-Play \"" + Path.Combine(root, c.table) + "\"", cmd.Arguments, c.name);
            }
        }
        [Test] public void UnknownHelperAndRegisteredRomRootsAreHandledConservatively()
        {
            Assert.AreEqual("", VpxPaths.Target(@"C:\Helpers\Watchdog.exe", "", @"C:\Launcher"));
            string[] roots = VpxPaths.RomDirectories(@"C:\Tables\Sample.vpx", @"C:\VPX\VPinballX_BGFX64.exe", @"D:\roms;E:\roms;relative");
            CollectionAssert.AreEqual(new[] { @"C:\Tables\pinmame\roms", @"D:\roms", @"E:\roms", @"C:\VPX\VPinMAME\roms", @"C:\VPX\plugins\pinmame\pinmame\roms" }, roots);
        }
    }
}
