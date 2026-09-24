using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class InputLogicTests
    {
        [Test]
        public void TriggerLatch_FiresOncePerSqueezeWithHysteresis()
        {
            var latch = new TriggerLatch();
            Assert.IsFalse(latch.Update(0.5f));
            Assert.IsTrue(latch.Update(0.8f));
            Assert.IsFalse(latch.Update(0.9f));
            Assert.IsFalse(latch.Update(0.5f));   // still above release: no re-arm
            Assert.IsFalse(latch.Update(0.8f));
            Assert.IsFalse(latch.Update(0.3f));   // re-armed
            Assert.IsTrue(latch.Update(0.75f));
        }

        [Test]
        public void ButtonEdge_FiresOnPressOnly()
        {
            var edge = new ButtonEdge();
            Assert.IsTrue(edge.Update(true));
            Assert.IsFalse(edge.Update(true));
            Assert.IsFalse(edge.Update(false));
            Assert.IsTrue(edge.Update(true));
        }

        [Test]
        public void StickRepeat_StepsImmediatelyThenRepeatsAndAccelerates()
        {
            var stick = new StickRepeat();
            Assert.AreEqual(1, stick.Update(0.9f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.9f, 0.3f));                 // inside the initial delay
            Assert.AreEqual(1, stick.Update(0.9f, 0.2f));                 // 0.5 s held: first repeat
            Assert.AreEqual(0, stick.Update(0.9f, 0.1f));
            Assert.AreEqual(1, stick.Update(0.9f, 0.15f));                // next repeat after about 0.22 s
        }

        [Test]
        public void StickRepeat_ReleaseAndReverse()
        {
            var stick = new StickRepeat();
            Assert.AreEqual(-1, stick.Update(-0.8f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.8f, 0.016f));               // reversal releases first
            Assert.AreEqual(1, stick.Update(0.8f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.2f, 0.016f));               // released
            Assert.AreEqual(0, stick.Update(0.5f, 0.016f));               // below press threshold
        }

        [Test]
        public void HoldTimer_FiresOnceAfterTheDurationAndResetsOnRelease()
        {
            var hold = new HoldTimer(2f);
            Assert.IsFalse(hold.Update(true, 1f));
            Assert.AreEqual(0.5f, hold.Progress, 1e-5f);
            Assert.IsTrue(hold.Update(true, 1f));
            Assert.IsFalse(hold.Update(true, 1f));
            Assert.IsFalse(hold.Update(false, 0.016f));
            Assert.IsFalse(hold.IsHeld);
            Assert.AreEqual(0f, hold.Progress);
        }
    }
}
