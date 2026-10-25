using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRLauncher.Tests
{
    public class VRControllerInputTests
    {
        private GameObject owner;
        private VRControllerInput input;
        private readonly List<string> sent = new List<string>();

        [SetUp]
        public void SetUp()
        {
            sent.Clear();
            owner = new GameObject("Legacy input fixture");
            input = owner.AddComponent<VRControllerInput>();
            // Tests record intent; they never synthesize operating-system keys.
            input.KeySink = (key, down) => { sent.Add(key + ":" + down); return true; };
        }

        [TearDown]
        public void TearDown()
        {
            input.ReleaseKeys();
            UnityEngine.Object.DestroyImmediate(owner);
        }

        [TestCase(0, 0x0D)]
        [TestCase(1, 0x35)]
        [TestCase(2, 0x68)]
        [TestCase(3, 0x62)]
        [TestCase(4, 0x31)]
        [TestCase(5, 0x65)]
        [TestCase(14, 0xA0)]
        [TestCase(15, 0xA1)]
        public void LegacyButtonMapsToOneOwnedKeyPressAndRelease(int button, int key)
        {
            input.Tick(_ => false, true);
            input.Tick(index => index == button, true);
            input.Tick(index => index == button, true);
            input.Tick(_ => false, true);
            CollectionAssert.AreEqual(new[] { key + ":True", key + ":False" }, sent);
        }

        [Test]
        public void FocusLossReleasesKeyAndHeldControlMustRearm()
        {
            input.Tick(_ => false, true);
            input.Tick(button => button == 0, true);
            input.Tick(button => button == 0, false);
            input.Tick(button => button == 0, true);
            CollectionAssert.AreEqual(new[] { "13:True", "13:False" }, sent);
            input.Tick(_ => false, true);
            input.Tick(button => button == 0, true);
            input.enableControllerInput = false;
            input.Tick(button => button == 0, true);
            input.ReleaseKeys();
            CollectionAssert.AreEqual(new[] { "13:True", "13:False", "13:True", "13:False" }, sent);
        }

        [Test]
        public void AlreadyHeldButtonsAndUnmappedButtonsNeverProduceNewKeys()
        {
            input.Tick(_ => true, true);
            Assert.IsEmpty(sent);
            input.Tick(_ => false, true);
            input.Tick(button => button == 9, true);
            Assert.IsEmpty(sent);
        }

        [Test]
        public void NativePacketsUseWindowsInputLayoutAndReleaseFlag()
        {
            input.KeySink = null;
            input.debugLogging = true;
            var flags = new List<uint>();
            input.NativeSend = (count, packets, size) =>
            {
                Assert.AreEqual(1, count);
                Assert.AreEqual(IntPtr.Size == 8 ? 40 : 28, size, "Windows INPUT ABI size");
                Assert.AreEqual(1, packets[0].type, "INPUT_KEYBOARD");
                Assert.AreEqual(0x0D, packets[0].u.ki.wVk);
                Assert.AreEqual(0, packets[0].u.ki.wScan);
                flags.Add(packets[0].u.ki.dwFlags);
                return 1;
            };
            input.Tick(_ => false, true);
            input.Tick(button => button == 0, true);
            input.ReleaseKeys();
            CollectionAssert.AreEqual(new uint[] { 0, 2 }, flags);
        }

        [Test]
        public void FailedNativeInjectionDoesNotClaimAKeyNeedsReleasing()
        {
            input.KeySink = null;
            int attempts = 0;
            input.NativeSend = (_, packets, size) => { attempts++; return 0; };
            input.Tick(_ => false, true);
            LogAssert.Expect(LogType.Warning, new Regex("Legacy controller key injection failed:"));
            input.Tick(button => button == 0, true);
            input.ReleaseKeys();
            Assert.AreEqual(1, attempts);
        }
    }
}
