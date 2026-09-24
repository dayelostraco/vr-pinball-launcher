using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ArcLayoutTests
    {
        [Test]
        public void CentreSlotIsStraightAhead()
        {
            Vector3 centre = ArcLayout.SlotPosition(0f);
            Assert.AreEqual(0f, centre.x, 1e-5f);
            Assert.AreEqual(ArcLayout.Radius, centre.z, 1e-5f);
        }

        [Test]
        public void PositiveOffsetsAreToTheRightAndOnTheArc()
        {
            for (float k = -3f; k <= 3f; k += 0.5f)
            {
                Assert.AreEqual(ArcLayout.Radius, ArcLayout.SlotPosition(k).magnitude, 1e-4f);
            }
            Assert.Greater(ArcLayout.SlotPosition(1f).x, 0f);
        }

        [Test]
        public void CabinetsFaceTheCentre()
        {
            // A cabinet's +Z points away from the player, i.e. along the radius.
            Vector3 outward = ArcLayout.SlotRotation(2f) * Vector3.forward;
            Vector3 radial = ArcLayout.SlotPosition(2f).normalized;
            Assert.Greater(Vector3.Dot(outward, radial), 0.9999f);
        }

        [TestCase(0, new int[0])]
        [TestCase(1, new[] { 0 })]
        [TestCase(2, new[] { 0, 1 })]
        [TestCase(4, new[] { -1, 0, 1, 2 })]
        [TestCase(7, new[] { -3, -2, -1, 0, 1, 2, 3 })]
        [TestCase(42, new[] { -3, -2, -1, 0, 1, 2, 3 })]
        public void VisibleOffsets_NeverRepeatATable(int count, int[] expected)
        {
            CollectionAssert.AreEqual(expected, ArcLayout.VisibleOffsets(count));
        }
    }
}
