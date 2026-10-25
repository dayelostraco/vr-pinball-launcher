using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class LaunchFeedbackTests
    {
        [Test]
        public void IgnoresOtherTableLogsAndTracksMatchingStartup()
        {
            var launch = new LaunchFeedback(@"D:\Tables\Mass Effect.vpx");
            launch.Observe(@"LoadGameFromFilename D:\Tables\Other.vpx");
            launch.Observe("Startup done");
            Assert.IsFalse(launch.StartupCompleted);
            launch.Observe(@"LoadGameFromFilename D:\Tables\Mass Effect.vpx");
            launch.Observe("Startup done");
            Assert.IsTrue(launch.StartupCompleted);
            Assert.IsTrue(launch.CountSession(0, 8, false));
            Assert.IsNull(launch.ReturnNotice(0, 8, false));
        }

        [Test]
        public void FailedStartupDoesNotCountAndOffersRetry()
        {
            var launch = new LaunchFeedback("table.vpx");
            launch.Observe("LoadGameFromFilename table.vpx");
            launch.Observe("ERROR OnScriptError compile failure");
            launch.Observe("Startup done"); // VPX may still print this after a script error.
            Assert.IsFalse(launch.CountSession(1, 3, false));
            Assert.IsFalse(launch.CountSession(0, 20, false));
            StringAssert.Contains("script error", launch.ReturnNotice(1, 3, false));
            StringAssert.Contains("retry", launch.ReturnNotice(1, 3, false));
        }

        [Test]
        public void MissingTelemetryAndCancellationRemainHonest()
        {
            var launch = new LaunchFeedback("table.vpx");
            StringAssert.Contains("before startup was confirmed", launch.ReturnNotice(0, 2, false));
            StringAssert.Contains("code 42", launch.ReturnNotice(42, 20, false));
            Assert.IsFalse(launch.CountSession(0, 2, false));
            Assert.IsTrue(launch.CountSession(0, 20, false));
            Assert.IsFalse(launch.CountSession(0, 20, true));
            StringAssert.Contains("stopped", launch.ReturnNotice(0, 20, true));
        }
    }
}
