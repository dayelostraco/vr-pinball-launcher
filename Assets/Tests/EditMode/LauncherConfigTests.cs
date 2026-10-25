using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRLauncher.Tests
{
    public class LauncherConfigTests
    {
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "vrl-config-" + Guid.NewGuid()); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        [Test]
        public void SaveAndLoadRoundTripAndReplace()
        {
            string path = Path.Combine(root, "launcher-config.json");
            var config = new LauncherConfig { tablesDirectory = root, vpinballExecutable = Path.Combine(root, "VPinballX_BGFX64.exe") };
            config.Save(path);
            Assert.AreEqual(root, LauncherConfig.Load(path).tablesDirectory);
            config.searchSubdirectories = false;
            config.Save(path);
            Assert.IsFalse(LauncherConfig.Load(path).searchSubdirectories);
            Assert.AreEqual(1, Directory.GetFiles(root).Length);
        }

        [Test]
        public void InvalidJsonIsPreserved()
        {
            string path = Path.Combine(root, "config.json");
            File.WriteAllText(path, "broken json");
            LogAssert.Expect(LogType.Error, new Regex("Error loading config:"));
            Assert.IsNotNull(LauncherConfig.Load(path));
            Assert.IsNotNull(LauncherConfig.LoadWarning);
            Assert.AreEqual("broken json", File.ReadAllText(path));
        }

        [Test]
        public void ValidationExplainsMissingFieldsAndFiles()
        {
            var config = new LauncherConfig { vpinballExecutable = "" };
            Assert.IsFalse(config.Validate(out string error));
            StringAssert.Contains("not set", error);
            config.vpinballExecutable = Path.Combine(root, "helper.exe");
            Assert.IsFalse(config.Validate(out error));
            File.WriteAllText(config.vpinballExecutable, "fixture");
            config.realVpinballExecutable = Path.Combine(root, "missing.exe");
            Assert.IsFalse(config.Validate(out error));
            StringAssert.Contains("target", error);
            config.realVpinballExecutable = "";
            config.tablesDirectory = "";
            Assert.IsFalse(config.Validate(out error));
            config.tablesDirectory = Path.Combine(root, "missing");
            Assert.IsFalse(config.Validate(out error));
            config.tablesDirectory = root;
            Assert.IsTrue(config.Validate(out error));
            Assert.IsNull(error);
        }

        [Test]
        public void MissingConfigCreatesDefaultsOnlyAtRequestedPath()
        {
            string path = Path.Combine(root, "new.json");
            var config = LauncherConfig.Load(path);
            Assert.IsNotNull(config);
            Assert.IsTrue(File.Exists(path));
            Assert.IsNull(LauncherConfig.LoadWarning);
            Assert.AreEqual(config.tablesDirectory, LauncherConfig.Load(path).tablesDirectory);
        }

        [Test]
        public void FailedAtomicSavePreservesExistingConfigAndCleansStagingFile()
        {
            string path = Path.Combine(root, "locked.json");
            var config = new LauncherConfig { tablesDirectory = root };
            config.Save(path);
            string original = File.ReadAllText(path);
            config.tablesDirectory = "new value";
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                LogAssert.Expect(LogType.Error, new Regex("Error saving config:"));
                config.Save(path);
            }
            Assert.AreEqual(original, File.ReadAllText(path));
            Assert.AreEqual(1, Directory.GetFiles(root).Length);
        }

        [Test]
        public void PathsUseAppDirectoryAndEmptyOptionalMediaPathsRemainEmpty()
        {
            Assert.AreEqual(Path.Combine(LauncherPaths.AppDirectory, "Media"), LauncherPaths.Resolve("Media"));
            Assert.IsNull(LauncherPaths.Resolve(null));
            var config = new LauncherConfig { tablesDirectory = root, tableMediaDirectory = "", wheelDirectory = null, searchSubdirectories = false };
            var settings = LauncherPaths.CatalogSettingsFrom(config);
            Assert.AreEqual(root, settings.TablesDirectory);
            Assert.IsFalse(settings.SearchSubdirectories);
            Assert.IsNull(settings.TableMediaDirectory);
            Assert.IsNull(settings.WheelDirectory);
            StringAssert.EndsWith("launcher-config.json", LauncherConfig.ConfigFilePath);
        }
    }
}
