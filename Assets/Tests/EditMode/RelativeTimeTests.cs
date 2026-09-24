using System;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class RelativeTimeTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [TestCase(0, "just now")]
        [TestCase(-30, "just now")]            // clock skew: last play in the future
        [TestCase(59, "just now")]
        [TestCase(60, "1 minute ago")]
        [TestCase(45 * 60, "45 minutes ago")]
        [TestCase(60 * 60, "1 hour ago")]
        [TestCase(23 * 3600, "23 hours ago")]
        [TestCase(30 * 3600, "yesterday")]
        [TestCase(3 * 86400, "3 days ago")]
        [TestCase(13 * 86400, "13 days ago")]
        public void Format_ReadsNaturally(int secondsAgo, string expected)
        {
            Assert.AreEqual(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));
        }

        [Test]
        public void Format_OlderThanTwoWeeks_ShowsTheDate()
        {
            Assert.AreEqual("on 1 Aug 2026", RelativeTime.Format(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc), Now));
        }
    }
}
