using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

[TestFixture, NonParallelizable]
public class VpxBackgroundInputTests
{
    private string directory;
    private string Sidecar { get { return Path.Combine(directory, "VpxBackgroundInput.vpx-path.txt"); } }
    private string LogFile { get { return Path.Combine(directory, "VpxBackgroundInput.log"); } }

    [SetUp]
    public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "vrl-host-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TearDown]
    public void TearDown() { Directory.Delete(directory, true); }

    [TestCase("", "\"\"")]
    [TestCase("A table.vpx", "\"A table.vpx\"")]
    [TestCase("a\\b", "\"a\\b\"")]
    [TestCase("a\"b", "\"a\\\"b\"")]
    [TestCase("a\\", "\"a\\\\\"")]
    [TestCase("a\\\"b", "\"a\\\\\\\"b\"")]
    public void QuotesWindowsArguments(string input, string expected) { Assert.That(VpxBackgroundInput.Quote(input), Is.EqualTo(expected)); }

    [Test]
    public void MissingRelativeNonexistentAndRecursiveTargetsNeverStart()
    {
        Action<Process> forbidden = p => Assert.Fail("Invalid target reached focus callback");
        Assert.That(VpxBackgroundInput.Execute(new string[0], directory, forbidden), Is.EqualTo(2));
        foreach (string path in new[] { "relative.exe", Path.Combine(directory, "missing.exe"), typeof(VpxBackgroundInput).Assembly.Location })
        {
            File.WriteAllText(Sidecar, path);
            Assert.That(VpxBackgroundInput.Execute(new string[0], directory, forbidden), Is.EqualTo(2));
        }
        Assert.That(File.ReadAllText(LogFile), Does.Contain("Missing").And.Contain("Invalid VPX executable"));
    }

    [Test]
    public void InvalidExecutableProducesActionableFailure()
    {
        string invalid = Path.Combine(directory, "invalid.exe");
        File.WriteAllText(invalid, "not executable"); File.WriteAllText(Sidecar, invalid);
        Assert.That(VpxBackgroundInput.Execute(new string[0], directory, p => Assert.Fail()), Is.EqualTo(1));
        Assert.That(File.ReadAllText(LogFile), Does.Contain("Launch failed:"));
    }

    [Test]
    public void LoggingFailureDoesNotReplaceLaunchResult()
    {
        string locked = Path.Combine(directory, "locked"); File.WriteAllText(locked, "file, not directory");
        Assert.DoesNotThrow(() => VpxBackgroundInput.Log(locked, "ignored"));
        Directory.CreateDirectory(LogFile);
        Assert.DoesNotThrow(() => VpxBackgroundInput.Log(directory, "ignored"));
    }

    [TestCase("VPPlayer", "", true)]
    [TestCase("SDL_app", "visual pinball player - fixture", true)]
    [TestCase("Other", "Visual Pinball Editor", false)]
    [TestCase("Other", "Virtual DMD", false)]
    public void PlayerIdentityRejectsEditorAndCompanions(string cls, string title, bool expected)
    {
        Assert.That(VpxBackgroundInput.IsPlayerWindow(cls, title), Is.EqualTo(expected));
    }

    [Test]
    public void NativeEnumerationOfHiddenOwnedChildDoesNotStealFocus()
    {
        var start = new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul") {
            UseShellExecute = false, CreateNoWindow = true
        };
        using (var child = Process.Start(start))
        {
            // A hidden CMD has no VPPlayer window. This executes the real native
            // enumeration, but its ownership filter cannot act on another app.
            VpxBackgroundInput.FocusPlayer(child);
            Assert.That(child.HasExited, Is.True);
        }
    }

    [Test]
    public void SelfTestChecksInheritedHintWithoutFocusingAnything()
    {
        Assert.That(VpxBackgroundInput.Execute(new[] { "--self-test" }, directory, p => Assert.Fail()), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChildExitAndEnvironmentSurviveFocusFailure(bool failFocus)
    {
        File.WriteAllText(Sidecar, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
        string[] args = { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command",
            "if ($env:SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS -eq '1') { exit 42 } else { exit 9 }" };
        int calls = 0;
        int result = VpxBackgroundInput.Execute(args, directory, p => { calls++; if (failFocus) throw new InvalidOperationException("fixture focus denied"); });
        Assert.That(result, Is.EqualTo(42)); Assert.That(calls, Is.EqualTo(1));
        if (failFocus) Assert.That(File.ReadAllText(LogFile), Does.Contain("Focus handoff failed: fixture focus denied"));
    }

    [Test]
    public void StableWindowHandoffStopsAfterSuccess()
    {
        long time = 0; int calls = 0; var messages = new List<string>();
        VpxBackgroundInput.FocusUntilReady(() => false, () => time, () => new IntPtr(7),
            window => { calls++; Assert.That(time, Is.EqualTo(2000)); Assert.That(window, Is.EqualTo(new IntPtr(7))); },
            () => new IntPtr(7), () => time += 500, messages.Add);
        Assert.That(calls, Is.EqualTo(1)); Assert.That(messages[0], Does.Contain("Focused"));
    }

    [Test]
    public void ReplacedOrDisappearingWindowsRestartSettlingPeriod()
    {
        long time = 0; var attempts = new List<long>();
        Func<IntPtr> candidate = () => time < 1000 ? new IntPtr(1) : time < 1500 ? IntPtr.Zero : new IntPtr(2);
        VpxBackgroundInput.FocusUntilReady(() => false, () => time, candidate, w => attempts.Add(time),
            () => time >= 4000 ? new IntPtr(2) : IntPtr.Zero, () => time += 500, s => { });
        Assert.That(attempts, Is.EqualTo(new long[] { 3500, 4000 }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExitAndTimeoutStopWithoutFocusingUnrelatedWindows(bool exit)
    {
        long time = 0; string message = null;
        VpxBackgroundInput.FocusUntilReady(() => exit && time >= 1000, () => time, () => IntPtr.Zero,
            w => Assert.Fail("No player window exists"), () => IntPtr.Zero, () => time += 500, s => message = s);
        Assert.That(time, Is.EqualTo(exit ? 1000 : 120000));
        Assert.That(message, Does.Contain(exit ? "exited" : "Timed out"));
    }
}
