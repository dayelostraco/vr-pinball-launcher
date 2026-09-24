using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class CabinetViewTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;
        private CabinetView cabinet;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-cab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            host = new GameObject("CabinetTest");
            cache = host.AddComponent<MediaCache>();
            cabinet = CabinetView.Create(host.transform, cache);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            Directory.Delete(dir, true);
        }

        private string Png(string name)
        {
            var tex = new Texture2D(4, 4);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }

        private static TableEntry Entry(MediaSet media) =>
            new TableEntry("Congo (Williams 1995).vpx", @"C:\t\Congo (Williams 1995).vpx", "Congo (Williams 1995)", "Congo", "Williams", 1995, media);

        [Test]
        public void FullMedia_ShowsPlayfieldBackglassAndApronWheel()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Png("w.png"), Playfield = Png("p.png"), Backglass = Png("b.png") }));

            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.PlayfieldTexture);
            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.BackglassTexture);
            Assert.IsTrue(cabinet.ApronWheelVisible);
            Assert.IsFalse(cabinet.WheelDecalVisible);
            Assert.IsNull(cabinet.MarqueeText);
        }

        [Test]
        public void NoPlayfield_ShowsDarkPlayfieldWithWheelDecal()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Png("w.png"), Backglass = Png("b.png") }));
            Assert.AreSame(CabinetView.DarkTexture, cabinet.PlayfieldTexture);
            Assert.IsTrue(cabinet.WheelDecalVisible);
        }

        [Test]
        public void NoBackglass_ShowsTheWheelOnTheBackbox()
        {
            string wheel = Png("w.png");
            cabinet.SetEntry(Entry(new MediaSet { Wheel = wheel, Playfield = Png("p.png") }));
            Texture2D wheelTexture = null;
            cache.Request(wheel, t => wheelTexture = t);
            Assert.AreSame(wheelTexture, cabinet.BackglassTexture);
        }

        [Test]
        public void NoWheel_ShowsTheTitleOnTheMarquee()
        {
            cabinet.SetEntry(Entry(new MediaSet { Playfield = Png("p.png") }));
            Assert.AreEqual("Congo", cabinet.MarqueeText);
            Assert.IsFalse(cabinet.ApronWheelVisible);
        }

        [Test]
        public void WheelFileMissingOnDisk_FallsBackToTitle()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Path.Combine(dir, "gone.png") }));
            Assert.AreEqual("Congo", cabinet.MarqueeText);
        }

        [Test]
        public void Video_ReplacesAndRestoresTheStill()
        {
            cabinet.SetEntry(Entry(new MediaSet { Playfield = Png("p.png") }));
            Texture still = cabinet.PlayfieldTexture;
            var videoTexture = new RenderTexture(4, 4, 0);

            cabinet.ShowVideo(videoTexture);
            Assert.AreSame(videoTexture, cabinet.PlayfieldTexture);
            cabinet.ShowStill();
            Assert.AreSame(still, cabinet.PlayfieldTexture);

            UnityEngine.Object.DestroyImmediate(videoTexture);
        }

        [Test]
        public void Placeholder_ShowsTheMessageAndClearsTheEntry()
        {
            cabinet.SetEntry(Entry(new MediaSet()));
            cabinet.SetPlaceholder("No favorites yet");
            Assert.IsNull(cabinet.Entry);
            Assert.AreEqual("No favorites yet", cabinet.PlaceholderText);

            cabinet.SetEntry(Entry(new MediaSet()));
            Assert.IsNull(cabinet.PlaceholderText);
        }

        [Test]
        public void PlayfieldFaceLooksUpAndTowardThePlayer()
        {
            Vector3 normal = CabinetView.PlayfieldRotation * Vector3.back;
            Assert.Greater(normal.y, 0.5f);
            Assert.Less(normal.z, -0.3f);
        }
    }
}
