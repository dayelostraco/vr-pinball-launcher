using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class WheelIndexTests
    {
        [Test]
        public void ExactThenNormalizedThenYearMatchAndMissingArt()
        {
            var index = new WheelIndex(new[] { "Table (Maker 1995).png" });
            Assert.AreEqual("Table (Maker 1995).png", index.Find("Table (Maker 1995)", out string strategy));
            Assert.AreEqual("exact", strategy);
            Assert.IsNotNull(index.Find("VR ROOM Table_Maker_1995", out strategy));
            Assert.AreEqual("normalized", strategy);
            Assert.IsNotNull(index.Find("Table (Maker 1995) VR v2", out strategy));
            Assert.AreEqual("title+year", strategy);
            Assert.IsNull(index.Find("Other table", out strategy));
            Assert.IsNull(strategy);
        }

        [Test]
        public void LeastDecoratedCandidateWinsRegardlessOfEnumerationOrder()
        {
            const string plain = "Table (Maker 1995).png";
            const string decorated = "Table (Maker 1995) VR Room.png";
            foreach (var paths in new[] { new[] { decorated, plain }, new[] { plain, decorated, plain, ".png" } })
            {
                var index = new WheelIndex(paths);
                Assert.AreEqual(plain, index.Find("Table (Maker 1995) v3", out _));
                Assert.IsNull(index.Find("", out _));
            }
        }

        [Test]
        public void UnknownManufacturerHasNoSubtitleAndMissingMediaUsesEmptySet()
        {
            var entry = new TableEntry("A.vpx", "A.vpx", "A", "A", null, 0, null);
            Assert.AreEqual("", entry.Subtitle);
            Assert.IsNotNull(entry.Media);
            Assert.IsNull(entry.Media.Wheel);
        }
    }
}
