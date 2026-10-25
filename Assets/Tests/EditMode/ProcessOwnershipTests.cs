using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class ProcessOwnershipTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void OwnsWrapperChildrenInParentBeforeChildOrder()
        {
            var parents = new Dictionary<int, int> { [30] = 20, [20] = 10, [40] = 99 };
            var times = new Dictionary<int, DateTime?> { [20] = Start.AddSeconds(1), [30] = Start.AddSeconds(2) };
            var result = ProcessOwnership.FindOwned(new ProcessIdentity(10, Start), parents, id => times[id], Start.AddSeconds(10));
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, result.Select(p => p.Id));
            Assert.AreEqual(times[30], result[2].StartedUtc);
        }

        [Test]
        public void ReusedAncestorCannotAuthorizeItsNewerGrandchild()
        {
            var parents = new Dictionary<int, int> { [20] = 10, [30] = 20 };
            var result = ProcessOwnership.FindOwned(new ProcessIdentity(10, Start), parents,
                id => id == 20 ? Start.AddSeconds(-1) : Start.AddSeconds(1), Start.AddSeconds(10));
            CollectionAssert.AreEqual(new[] { 10 }, result.Select(p => p.Id));
        }

        [Test]
        public void UnreadableOrReusedAfterSnapshotIdentitiesAreExcluded()
        {
            var parents = new Dictionary<int, int> { [20] = 10, [30] = 10 };
            var result = ProcessOwnership.FindOwned(new ProcessIdentity(10, Start), parents,
                id => id == 20 ? (DateTime?)null : Start.AddSeconds(20), Start.AddSeconds(10));
            CollectionAssert.AreEqual(new[] { 10 }, result.Select(p => p.Id));
        }

        [Test]
        public void CyclesCannotRevisitRootAndEmptySnapshotKeepsOnlyOwnedRoot()
        {
            var root = new ProcessIdentity(10, Start);
            var result = ProcessOwnership.FindOwned(root, new Dictionary<int, int> { [20] = 10, [10] = 20 }, _ => Start, Start);
            CollectionAssert.AreEqual(new[] { 10, 20 }, result.Select(p => p.Id));
            Assert.AreEqual(1, ProcessOwnership.FindOwned(root, new Dictionary<int, int>(), _ => null, Start).Count);
        }
    }
}
