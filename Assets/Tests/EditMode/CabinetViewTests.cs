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
        public void FullMedia_ShowsPlayfieldBackglassAndTopper()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Png("w.png"), Playfield = Png("p.png"), Backglass = Png("b.png") }));

            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.PlayfieldTexture);
            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.BackglassTexture);
            Assert.IsTrue(cabinet.TopperVisible);
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
            Assert.IsFalse(cabinet.TopperVisible);
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

        [Test]
        public void Wedge_FacesPointOutward()
        {
            Mesh mesh = CabinetMesh.Wedge(0.72f, 0.078f, 1.08f, 0.96f, 1.66f);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3 inside = Vector3.zero;
            foreach (Vector3 vertex in vertices) inside += vertex;
            inside /= vertices.Length;

            Assert.AreEqual(8, triangles.Length / 3);   // 2 side triangles + 3 quads
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Assert.Greater(Vector3.Dot(normal, (a + b + c) / 3f - inside), 0f, $"triangle {i / 3} faces inward");
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void PlayfieldBase_FillsTheGapJustUnderThePlayfield()
        {
            Transform wedge = cabinet.transform.Find("PlayfieldBase");
            Assert.IsNotNull(wedge);
            Assert.IsNull(cabinet.transform.Find("HeadSupport"));

            Vector3 normal = CabinetView.PlayfieldRotation * Vector3.back;
            float highest = float.MinValue;
            foreach (Vector3 vertex in wedge.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                float above = Vector3.Dot(vertex - CabinetView.PlayfieldCenter, normal);
                Assert.LessOrEqual(above, 0.001f, "wedge pokes through the playfield");
                highest = Mathf.Max(highest, above);
            }
            Assert.Greater(highest, -0.02f, "wedge leaves a visible gap under the playfield");
        }

        [Test]
        public void Playfield_HasSideRails()
        {
            Assert.IsNotNull(cabinet.transform.Find("RailLeft"));
            Assert.IsNotNull(cabinet.transform.Find("RailRight"));
        }

        [Test]
        public void Front_HasACoinDoorWithTwoLitSlots()
        {
            Assert.IsNotNull(cabinet.transform.Find("CoinDoor"));
            Assert.IsNotNull(cabinet.transform.Find("CoinDoorTrim"));
            int slots = 0;
            foreach (Transform child in cabinet.transform)
            {
                if (child.name == "CoinSlot") slots++;
            }
            Assert.AreEqual(2, slots);
        }

        [Test]
        public void CoinDoorPhoto_ReplacesTheBuiltInSlots()
        {
            CabinetView withPhoto = CabinetView.Create(host.transform, cache, Png("coindoor.png"));

            Assert.IsTrue(withPhoto.CoinDoorPhotoVisible);
            foreach (Transform child in withPhoto.transform)
            {
                if (child.name == "CoinSlot") Assert.IsFalse(child.gameObject.activeSelf);
            }
        }

        [Test]
        public void CoinDoorPhoto_MissingFile_KeepsTheBuiltInDoor()
        {
            CabinetView missing = CabinetView.Create(host.transform, cache, Path.Combine(dir, "nope.jpg"));

            Assert.IsFalse(missing.CoinDoorPhotoVisible);
            int litSlots = 0;
            foreach (Transform child in missing.transform)
            {
                if (child.name == "CoinSlot" && child.gameObject.activeSelf) litSlots++;
            }
            Assert.AreEqual(2, litSlots);
        }

        [Test]
        public void CoinDoorPhoto_SurvivesLruEviction()
        {
            CabinetView withPhoto = CabinetView.Create(host.transform, cache, Png("coindoor.png"));

            for (int i = 0; i < MediaCache.Capacity + 5; i++)
            {
                cache.Request(Png($"other{i}.png"), t => { });
            }

            Assert.IsTrue(withPhoto.CoinDoorPhotoTexture != null);
            Assert.IsTrue(withPhoto.CoinDoorPhotoVisible);
        }

        [Test]
        public void CoinDoor_IsLandscapeLikeTheRealPart()
        {
            Vector3 size = cabinet.transform.Find("CoinDoor").localScale;
            Assert.AreEqual(0.35f, size.x, 1e-4f);
            Assert.AreEqual(0.30f, size.y, 1e-4f);
        }
    }
}
