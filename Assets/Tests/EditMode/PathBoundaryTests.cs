using System;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class PathBoundaryTests
    {
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "vrl-path-" + Guid.NewGuid()); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        [Test]
        public void RealFilesystemHonorsExtensionAndRecursion()
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "A.VPX"), "fixture");
            File.WriteAllText(Path.Combine(root, "ignore.txt"), "fixture");
            File.WriteAllText(Path.Combine(root, "nested", "B.vpx"), "fixture");
            var fs = new DiskFileSystem();
            Assert.IsTrue(fs.DirectoryExists(root));
            Assert.IsTrue(fs.FileExists(Path.Combine(root, "A.VPX")));
            Assert.AreEqual(1, fs.GetFiles(root, false, ".vpx").Count);
            Assert.AreEqual(2, fs.GetFiles(root, true, ".vpx").Count);
        }

        [Test]
        public void HelperSidecarAndExplicitPathsHaveDeterministicPrecedence()
        {
            string runner = Path.Combine(root, "VpxBackgroundInput.exe");
            string vpx = Path.Combine(root, "VPinballX_BGFX64.exe");
            Assert.AreEqual("", VpxPaths.Resolve(null, root));
            Assert.IsFalse(VpxPaths.IsNative(null));
            Assert.AreEqual(vpx, VpxPaths.Target(vpx, "ignored", root));
            Assert.AreEqual(vpx, VpxPaths.Target(runner, "VPinballX_BGFX64.exe", root));
            File.WriteAllText(Path.Combine(root, "VpxBackgroundInput.vpx-path.txt"), "  \"other.exe\"  ");
            Assert.AreEqual(Path.Combine(root, "other.exe"), VpxPaths.Target(runner, vpx, root));
            Assert.AreEqual(vpx, VpxPaths.Resolve(vpx, root));
            Assert.AreEqual(vpx, VpxPaths.Target(Path.Combine(root, "unknown.exe"), vpx, root));
            Assert.IsEmpty(VpxPaths.RomDirectories(null, null, null));
            var roots = VpxPaths.RomDirectories(Path.Combine(root, "A.vpx"), vpx, root + ";" + root + ";relative");
            Assert.AreEqual(4, roots.Length);
            var command = VpxProfiles.Resolve("A.vpx", "VPinballX_BGFX64.exe", null, null, root);
            Assert.AreEqual(vpx, command.Executable);
            Assert.IsNull(command.IniFile);
        }
    }
}
