using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class BridgeArgumentsTests
    {
        private readonly string root = Path.GetFullPath("bridge-fixtures");

        [TestCase(null, null)]
        [TestCase(" ", null)]
        [TestCase("--custom-option", null)]
        [TestCase("\"unterminated.ahk", null)]
        [TestCase("\"folder with spaces/script.AHK\" --option", "folder with spaces/script.AHK")]
        [TestCase("script.ahk\t--option", "script.ahk")]
        [TestCase("script.ahk", "script.ahk")]
        public void ExtractsOnlyCompleteLeadingScriptPath(string arguments, string expected)
        {
            Assert.AreEqual(expected, BridgeArguments.ScriptPath(arguments));
        }

        [Test]
        public void ResolvesScriptRelativeToAppAndPreservesRemainingArguments()
        {
            string expected = "\"" + Path.Combine(root, "script.ahk") + "\"";
            Assert.AreEqual(expected + " --literal \"two words\"", BridgeArguments.Resolve("script.ahk --literal \"two words\"", root));
            Assert.AreEqual(expected, BridgeArguments.Resolve(" \"script.ahk\" ", root));
            Assert.AreEqual("--compiled-option", BridgeArguments.Resolve("--compiled-option", root));
            Assert.AreEqual("", BridgeArguments.Resolve(null, root));
        }

        [Test]
        public void CompiledBridgeDoesNotRequireAutoHotkeySourceOrDlls()
        {
            var files = new FakeFileSystem().Add(Path.Combine(root, "custom.exe"));
            Assert.IsTrue(BridgeArguments.Validate("custom.exe", "--test", root, root, files, out string error));
            Assert.IsNull(error);
            Assert.IsFalse(BridgeArguments.Validate("custom.exe", "", Path.Combine(root, "missing"), root, files, out error));
            StringAssert.Contains("folder", error);
        }

        [Test]
        public void AutoHotkeyRequiresScriptAndEveryNativeDependency()
        {
            var files = new FakeFileSystem().Add(Path.Combine(root, "AutoHotkey.exe"));
            Assert.IsFalse(BridgeArguments.Validate("AutoHotkey.exe", "", root, root, files, out string error));
            StringAssert.Contains("arguments", error);
            Assert.IsFalse(BridgeArguments.Validate("AutoHotkey.exe", "script.ahk", root, root, files, out error));
            StringAssert.Contains("script not found", error);
            files.Add(Path.Combine(root, "script.ahk"));
            foreach (string dependency in new[] { "auto_oculus_touch.ahk", "auto_oculus_touch.dll", "vJoyInterface.dll" })
            {
                Assert.IsFalse(BridgeArguments.Validate("AutoHotkey.exe", "script.ahk", root, root, files, out error));
                StringAssert.Contains(dependency, error);
                files.Add(Path.Combine(root, dependency));
            }
            Assert.IsTrue(BridgeArguments.Validate("AutoHotkey.exe", "script.ahk", root, root, files, out error));
            Assert.IsNull(error);
        }
    }
}
