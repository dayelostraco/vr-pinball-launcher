using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

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
            Texture2D result = new Texture2D(1, 1);
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
    }
}
