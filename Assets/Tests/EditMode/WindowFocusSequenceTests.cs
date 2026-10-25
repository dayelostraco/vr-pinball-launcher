using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class WindowFocusSequenceTests
    {
        private sealed class Target : IWindowFocusTarget
        {
            public bool Running = true, Minimized, Accepted = true;
            public bool FailGet, FailRestore, FailTop, FailClear, FailActivate;
            public IntPtr Window = new IntPtr(1);
            public int Refreshes, Restores, Activations;
            public readonly List<bool> Topmost = new List<bool>();
            public bool IsRunning => Running;
            public IntPtr GetWindow() { if (FailGet) throw new InvalidOperationException("get"); return Window; }
            public bool IsMinimized(IntPtr window) => Minimized;
            public void Refresh() { Refreshes++; }
            public void Restore(IntPtr window) { Restores++; if (FailRestore) throw new InvalidOperationException("restore"); }
            public void SetTopmost(IntPtr window, bool value)
            {
                Topmost.Add(value);
                if (value ? FailTop : FailClear) throw new InvalidOperationException("topmost");
            }
            public bool Activate(IntPtr window) { Activations++; if (FailActivate) throw new InvalidOperationException("activate"); return Accepted; }
        }

        [Test]
        public void SuccessRestoresMinimizedWindowAndClearsTopmostBeforeReturning()
        {
            var target = new Target { Minimized = true };
            var warnings = new List<string>();
            CollectionAssert.AreEqual(new[] { 2f, 0.2f }, WindowFocusSequence.Run(target, 2f, _ => { }, warnings.Add).ToArray());
            Assert.AreEqual(1, target.Restores);
            Assert.AreEqual(1, target.Activations);
            CollectionAssert.AreEqual(new[] { true, false }, target.Topmost);
            Assert.IsEmpty(warnings);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeniedOrThrowingActivationClearsTopmostBeforeEveryRetry(bool throws)
        {
            var target = new Target { Accepted = false, FailActivate = throws };
            using (var sequence = WindowFocusSequence.Run(target, 0f, _ => { }, _ => { }).GetEnumerator())
            {
                Assert.IsTrue(sequence.MoveNext());
                while (sequence.MoveNext()) Assert.AreEqual(false, target.Topmost.Last());
            }
            Assert.AreEqual(10, target.Activations);
            Assert.AreEqual(20, target.Topmost.Count);
        }

        [Test]
        public void MissingWindowRefreshesAtMostTenTimes()
        {
            var target = new Target { Window = IntPtr.Zero };
            var warnings = new List<string>();
            Assert.AreEqual(11, WindowFocusSequence.Run(target, 1f, _ => { }, warnings.Add).Count());
            Assert.AreEqual(10, target.Refreshes);
            Assert.AreEqual(0, target.Activations);
            StringAssert.Contains("multiple attempts", warnings.Last());
        }

        [Test]
        public void ProcessExitStopsBeforeAnyWindowMutation()
        {
            var target = new Target { Running = false };
            Assert.AreEqual(1, WindowFocusSequence.Run(target, 1f, _ => { }, _ => { }).Count());
            Assert.AreEqual(0, target.Topmost.Count);
            target.Running = true;
            target.Accepted = false;
            using (var sequence = WindowFocusSequence.Run(target, 0f, _ => { }, _ => { }).GetEnumerator())
            {
                sequence.MoveNext();
                sequence.MoveNext();
                target.Running = false;
                Assert.IsFalse(sequence.MoveNext());
            }
            Assert.AreEqual(1, target.Activations);
        }

        [Test]
        public void NativeFailuresAreReportedAndCleanupIsStillAttempted()
        {
            var warnings = new List<string>();
            var target = new Target { FailGet = true };
            WindowFocusSequence.Run(target, 0f, _ => { }, warnings.Add).ToArray();
            Assert.IsTrue(warnings.Any(w => w.Contains("getting window handle")));
            target = new Target { Minimized = true, FailRestore = true, FailTop = true, FailClear = true };
            WindowFocusSequence.Run(target, 0f, _ => { }, warnings.Add).ToArray();
            Assert.IsTrue(warnings.Any(w => w.Contains("restoring window")));
            Assert.IsTrue(warnings.Any(w => w.Contains("setting window topmost")));
            Assert.IsTrue(warnings.Any(w => w.Contains("removing topmost")));
            CollectionAssert.AreEqual(new[] { true, false }, target.Topmost);
        }

        [Test]
        public void ExitDuringRestoreDelayNeverActivatesStaleWindow()
        {
            var target = new Target { Minimized = true };
            using (var sequence = WindowFocusSequence.Run(target, 0f, _ => { }, _ => { }).GetEnumerator())
            {
                sequence.MoveNext();
                sequence.MoveNext();
                target.Running = false;
                Assert.IsFalse(sequence.MoveNext());
            }
            Assert.AreEqual(0, target.Activations);
            Assert.IsEmpty(target.Topmost);
        }
    }
}
