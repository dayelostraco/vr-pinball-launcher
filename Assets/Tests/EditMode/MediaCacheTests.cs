using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRLauncher.Tests
{
    public class MediaCacheTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            host = new GameObject("MediaCacheTest");
            cache = host.AddComponent<MediaCache>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            Directory.Delete(dir, true);
        }

        private string WritePng(string name, int width, int height)
        {
            var tex = new Texture2D(width, height);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }

        [Test]
        public void Request_LoadsTheImageAndCachesIt()
        {
            string path = WritePng("wheel.png", 8, 4);
            Texture2D first = null, second = null;

            cache.Request(path, t => first = t);
            cache.Request(path, t => second = t);

            Assert.IsNotNull(first);
            Assert.AreEqual(8, first.width);
            Assert.AreSame(first, second);
        }

        [Test]
        public void Request_MissingFile_GivesNull()
        {
            bool called = false;
            Texture2D result = null;
            cache.Request(Path.Combine(dir, "nope.png"), t => { called = true; result = t; });
            Assert.IsTrue(called);
            Assert.IsNull(result);
        }

        [Test]
        public void Request_NullPath_GivesNull()
        {
            bool called = false;
            cache.Request(null, t => { called = true; Assert.IsNull(t); });
            Assert.IsTrue(called);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PinnedTextureSurvivesCacheEvictionAndIsDestroyedWithOwner(bool cachedFirst)
        {
            string path = WritePng("pinned.png", 2, 2);
            Texture2D pinned = null;
            if (cachedFirst) cache.Request(path, texture => pinned = texture);
            Texture2D original = pinned;
            cache.RequestPinned(path, texture => pinned = texture);
            if (cachedFirst) Assert.AreSame(original, pinned);
            FillCache();
            Assert.IsTrue(pinned != null);
            cache.Request(path, texture => Assert.AreSame(pinned, texture));
            UnityEngine.Object.DestroyImmediate(host);
            Assert.IsTrue(pinned == null, "Cache owner leaked its pinned texture");
        }

        [Test]
        public void FailedPinDoesNotPermanentlyPinLaterOrdinaryRequest()
        {
            string path = Path.Combine(dir, "later.png");
            cache.RequestPinned(path, texture => Assert.IsNull(texture));
            WritePng("later.png", 2, 2);
            Texture2D first = null;
            cache.Request(path, texture => first = texture);
            FillCache();
            Assert.IsTrue(first == null, "Failed request left a stale pin reservation");
        }

        [Test]
        public void InvalidImageIsNotCachedAndCanBeReplacedByValidImage()
        {
            string path = Path.Combine(dir, "invalid.png");
            File.WriteAllText(path, "not a PNG");
            cache.Request(path, texture => Assert.IsNull(texture));
            WritePng("invalid.png", 2, 2);
            cache.Request(path, texture => Assert.IsNotNull(texture));
        }

        private void FillCache()
        {
            for (int i = 0; i <= MediaCache.Capacity; i++)
                cache.Request(WritePng("other-" + i + ".png", 2, 2), _ => { });
        }

        [UnityTest]
        public IEnumerator ConcurrentRequestsShareAsyncLoadAndPinnedOwnership()
        {
            IEnumerator operation = null;
            int schedules = 0, delivered = 0;
            cache.ScheduleLoad = load => { schedules++; operation = load; };
            string path = WritePng("async.png", 8, 4);
            Texture2D first = null, second = null;
            cache.Request(path, texture => { first = texture; delivered++; });
            cache.RequestPinned(path, texture => { second = texture; delivered++; });
            Assert.AreEqual(1, schedules);
            Assert.AreEqual(0, delivered);
            yield return DrainLoad(operation);
            Assert.AreEqual(2, delivered);
            Assert.AreSame(first, second);
            Assert.AreEqual(8, first.width);
            Assert.AreEqual(TextureWrapMode.Clamp, first.wrapMode);
            cache.Request(path, texture => Assert.AreSame(first, texture));
            Assert.AreEqual(1, schedules);
            UnityEngine.Object.DestroyImmediate(host);
            Assert.IsTrue(first == null);
        }

        [UnityTest]
        public IEnumerator AsyncFailureClearsPendingRequestsAndAllowsRetry()
        {
            IEnumerator operation = null;
            cache.ScheduleLoad = load => operation = load;
            string path = Path.Combine(dir, "retry.png");
            int failed = 0;
            cache.RequestPinned(path, texture => { Assert.IsNull(texture); failed++; });
            LogAssert.Expect(LogType.Warning, new Regex("MediaCache: could not load"));
            yield return DrainLoad(operation);
            Assert.AreEqual(1, failed);
            WritePng("retry.png", 2, 2);
            Texture2D result = null;
            cache.Request(path, texture => result = texture);
            yield return DrainLoad(operation);
            Assert.IsNotNull(result);
            cache.ScheduleLoad = null;
            FillCache();
            Assert.IsTrue(result == null, "Failed async request retained its pin reservation");
        }

        [UnityTest]
        public IEnumerator BrokenSubscriberDoesNotPreventOtherViewsReceivingTexture()
        {
            IEnumerator operation = null;
            cache.ScheduleLoad = load => operation = load;
            string path = WritePng("callback.png", 2, 2);
            cache.Request(path, _ => throw new InvalidOperationException("fixture subscriber failure"));
            Texture2D result = null;
            cache.Request(path, texture => result = texture);
            LogAssert.Expect(LogType.Exception, new Regex("fixture subscriber failure"));
            yield return DrainLoad(operation);
            Assert.IsNotNull(result);
        }

        private static IEnumerator DrainLoad(IEnumerator operation)
        {
            // Poll the real file request without entering PlayMode or launching
            // the application scene; always dispose the request on assertion failure.
            try
            {
                Assert.IsTrue(operation.MoveNext());
                var request = (AsyncOperation)operation.Current;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!request.isDone && timer.Elapsed.TotalSeconds < 10) yield return null;
                Assert.IsTrue(request.isDone, "Local texture request timed out");
                Assert.IsFalse(operation.MoveNext());
            }
            finally { (operation as IDisposable)?.Dispose(); }
        }
    }
}
