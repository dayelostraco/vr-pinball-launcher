using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class LruCacheTests
    {
        [Test]
        public void Add_BeyondCapacity_EvictsLeastRecentlyUsed()
        {
            var evicted = new List<string>();
            var cache = new LruCache<string, int>(2, (k, _) => evicted.Add(k));
            cache.Add("a", 1);
            cache.Add("b", 2);
            cache.TryGet("a", out _);   // "a" is now more recent than "b"
            cache.Add("c", 3);

            CollectionAssert.AreEqual(new[] { "b" }, evicted);
            Assert.IsTrue(cache.TryGet("a", out int a));
            Assert.AreEqual(1, a);
            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void Add_ExistingKey_ReplacesAndEvictsTheOldValue()
        {
            var evicted = new List<int>();
            var cache = new LruCache<string, int>(2, (_, v) => evicted.Add(v));
            cache.Add("a", 1);
            cache.Add("a", 2);
            CollectionAssert.AreEqual(new[] { 1 }, evicted);
            Assert.AreEqual(1, cache.Count);
        }

        [Test]
        public void Clear_EvictsEverything()
        {
            int evictions = 0;
            var cache = new LruCache<string, int>(3, (_, __) => evictions++);
            cache.Add("a", 1);
            cache.Add("b", 2);
            cache.Clear();
            Assert.AreEqual(2, evictions);
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void Comparer_IsHonoured()
        {
            var cache = new LruCache<string, int>(2, null, StringComparer.OrdinalIgnoreCase);
            cache.Add("A.png", 1);
            Assert.IsTrue(cache.TryGet("a.PNG", out _));
        }

        [Test]
        public void Capacity_MustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        }
    }
}
