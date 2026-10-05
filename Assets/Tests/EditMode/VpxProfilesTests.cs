using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class VpxProfilesTests
    {
        private const string DefaultExe = @"D:\Visual Pinball\VPinballX_BGFX64.exe";
        private const string Table = @"D:\Visual Pinball\Tables\Stranger Things (Original 2020).vpx";

        private static VpxProfile Legacy(string workingDir = "", string ini = @"D:\Legacy\legacy.ini") =>
            new VpxProfile
            {
                name = "10.8.0",
                executable = @"D:\Legacy\VPinballX_GL64.exe",
                iniFile = ini,
                workingDirectory = workingDir
            };

        private static TableProfile[] Assign(string table, string profile) =>
            new[] { new TableProfile { table = table, profile = profile } };

        [Test]
        public void Resolve_UnassignedTable_UsesDefaultInstall()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy() }, new TableProfile[0]);
            Assert.AreEqual(DefaultExe, cmd.Executable);
            Assert.AreEqual($"-Play \"{Table}\"", cmd.Arguments);
            Assert.AreEqual(@"D:\Visual Pinball", cmd.WorkingDirectory);
            Assert.IsNull(cmd.ProfileName);
        }

        [Test]
        public void Resolve_NullLists_UsesDefaultInstall()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, null, null);
            Assert.AreEqual(DefaultExe, cmd.Executable);
        }

        [Test]
        public void Resolve_AssignedTable_UsesProfileExeAndIni_CaseInsensitive()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy() },
                Assign("stranger things (original 2020)", "10.8.0"));
            Assert.AreEqual(@"D:\Legacy\VPinballX_GL64.exe", cmd.Executable);
            Assert.AreEqual($"-Ini \"D:\\Legacy\\legacy.ini\" -Play \"{Table}\"", cmd.Arguments);
            Assert.AreEqual(@"D:\Legacy", cmd.WorkingDirectory);
            Assert.AreEqual("10.8.0", cmd.ProfileName);
        }

        [Test]
        public void Resolve_ProfileWithoutIni_OmitsIniArgument()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy(ini: "") },
                Assign("Stranger Things (Original 2020)", "10.8.0"));
            Assert.AreEqual($"-Play \"{Table}\"", cmd.Arguments);
        }

        [Test]
        public void Resolve_TableWorkingDirectory_UsesTablesFolder()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy("table") },
                Assign("Stranger Things (Original 2020)", "10.8.0"));
            Assert.AreEqual(@"D:\Visual Pinball\Tables", cmd.WorkingDirectory);
        }

        [Test]
        public void Resolve_ExplicitWorkingDirectory_IsUsedAsIs()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy(@"E:\Somewhere") },
                Assign("Stranger Things (Original 2020)", "10.8.0"));
            Assert.AreEqual(@"E:\Somewhere", cmd.WorkingDirectory);
        }

        [Test]
        public void Resolve_UnknownProfileName_FallsBackToDefault()
        {
            LaunchCommand cmd = VpxProfiles.Resolve(Table, DefaultExe, new[] { Legacy() },
                Assign("Stranger Things (Original 2020)", "9.9"));
            Assert.AreEqual(DefaultExe, cmd.Executable);
            Assert.IsNull(cmd.ProfileName);
        }
    }
}
