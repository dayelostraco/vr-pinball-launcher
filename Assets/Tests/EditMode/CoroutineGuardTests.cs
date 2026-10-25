using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class CoroutineGuardTests
    {
        private static IEnumerator Child(bool fail, Action dispose)
        {
            try
            {
                yield return "child";
                if (fail) throw new InvalidOperationException("nested failure");
            }
            finally { dispose(); }
        }

        private static IEnumerator Parent(bool fail, Action dispose)
        {
            try
            {
                yield return "parent";
                yield return Child(fail, dispose);
                yield return "done";
            }
            finally { dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedOperationsPreserveYieldOrderAndReportDeferredFailure(bool fail)
        {
            int disposed = 0;
            var errors = new List<Exception>();
            var yielded = new List<object>();
            IEnumerator guarded = CoroutineGuard.Run(Parent(fail, () => disposed++), errors.Add);
            while (guarded.MoveNext()) yielded.Add(guarded.Current);
            CollectionAssert.AreEqual(fail ? new[] { "parent", "child" } : new[] { "parent", "child", "done" }, yielded);
            Assert.AreEqual(2, disposed);
            Assert.AreEqual(fail ? 1 : 0, errors.Count);
            if (fail) Assert.AreEqual("nested failure", errors[0].Message);
        }

        [Test]
        public void CancellationDisposesNestedOperationsWithoutReportingFailure()
        {
            int disposed = 0;
            var errors = new List<Exception>();
            IEnumerator guarded = CoroutineGuard.Run(Parent(false, () => disposed++), errors.Add);
            guarded.MoveNext();
            guarded.MoveNext();
            ((IDisposable)guarded).Dispose();
            Assert.AreEqual(2, disposed);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void MissingOperationOrFailureHandlerIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => CoroutineGuard.Run(null, _ => { }).MoveNext());
            Assert.Throws<ArgumentNullException>(() => CoroutineGuard.Run(new object[0].GetEnumerator(), null).MoveNext());
        }

        [Test]
        public void PlainEnumeratorsNeedNoDisposableImplementation()
        {
            var guarded = CoroutineGuard.Run(new[] { 1, 2 }.GetEnumerator(), _ => Assert.Fail());
            Assert.IsTrue(guarded.MoveNext());
            Assert.AreEqual(1, guarded.Current);
            Assert.IsTrue(guarded.MoveNext());
            Assert.AreEqual(2, guarded.Current);
            Assert.IsFalse(guarded.MoveNext());
        }

        private sealed class BrokenCleanup : IEnumerator, IDisposable
        {
            private readonly bool wait;
            public BrokenCleanup(bool wait = false) { this.wait = wait; }
            public object Current => null;
            public bool MoveNext() => wait;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => throw new InvalidOperationException("cleanup failure");
        }

        private static IEnumerator OwnBrokenChild(Action disposed)
        {
            try { yield return new BrokenCleanup(); }
            finally { disposed(); }
        }

        [Test]
        public void CleanupFailureStillDisposesParent()
        {
            bool disposed = false;
            var errors = new List<Exception>();
            var guarded = CoroutineGuard.Run(OwnBrokenChild(() => disposed = true), errors.Add);
            Assert.IsFalse(guarded.MoveNext());
            Assert.IsTrue(disposed);
            Assert.AreEqual("cleanup failure", errors[0].Message);
        }

        private static IEnumerator BrokenCancellation(Action disposed)
        {
            try { yield return new BrokenCleanup(true); }
            finally { disposed(); throw new InvalidOperationException("parent cleanup failure"); }
        }

        [Test]
        public void CancellationCollectsCleanupFailuresAfterReleasingEveryOwner()
        {
            bool parentDisposed = false;
            var errors = new List<Exception>();
            var guarded = CoroutineGuard.Run(BrokenCancellation(() => parentDisposed = true), errors.Add);
            Assert.IsTrue(guarded.MoveNext());
            ((IDisposable)guarded).Dispose();
            Assert.IsTrue(parentDisposed);
            Assert.AreEqual(1, errors.Count);
            var aggregate = errors[0] as AggregateException;
            Assert.IsNotNull(aggregate);
            Assert.AreEqual(2, aggregate.InnerExceptions.Count);
        }
    }
}
