using System.Collections.Generic;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class HeldKeyTests
    {
        [Test]
        public void SessionEndReleasesAndRequiresPhysicalRelease()
        {
            var key = new HeldKey();
            var sent = new List<bool>();
            bool Send(bool down) { sent.Add(down); return true; }
            key.Update(true, true, Send); // Held on arrival must not leak into VPX.
            Assert.IsEmpty(sent);
            key.Update(false, true, Send);
            key.Update(true, true, Send);
            key.Update(true, true, Send);
            key.Update(true, false, Send);
            key.Update(true, true, Send);
            CollectionAssert.AreEqual(new[] { true, false }, sent);
            key.Update(false, true, Send);
            key.Update(true, true, Send);
            key.Release(Send);
            key.Release(Send);
            CollectionAssert.AreEqual(new[] { true, false, true, false }, sent);
        }

        [Test]
        public void FailedInjectionIsRetriedWithoutInventingHeldState()
        {
            var key = new HeldKey();
            key.Update(false, true, _ => true);
            key.Update(true, true, _ => false);
            var sent = new List<bool>();
            bool Send(bool down) { sent.Add(down); return true; }
            key.Update(true, true, Send);
            key.Release(_ => false);
            key.Release(Send);
            key.Update(false, false, Send);
            key.Update(true, true, Send);
            key.Update(false, true, Send);
            CollectionAssert.AreEqual(new[] { true, false, true, false }, sent);
        }
    }
}
