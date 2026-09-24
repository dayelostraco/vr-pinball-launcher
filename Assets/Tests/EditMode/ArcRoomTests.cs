using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ArcRoomTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;
        private LauncherState state;
        private ArcRoom room;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-room-" + Guid.NewGuid().ToString("N"));
            state = LauncherState.Load(Path.Combine(dir, "state.json"));
            host = new GameObject("RoomTest");
            cache = host.AddComponent<MediaCache>();
        }

        [TearDown]
        public void TearDown()
        {
            if (room != null) UnityEngine.Object.DestroyImmediate(room.gameObject);
            UnityEngine.Object.DestroyImmediate(host);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private (TableListView view, List<TableEntry> all) Build(int count)
        {
            List<TableEntry> all = TestEntries.Make(count);
            var view = new TableListView(all, state);
            room = ArcRoom.Create(view, state, cache);
            return (view, all);
        }

        private int ActiveCabinets() =>
            Enumerable.Range(-3, 7).Count(k => room.CabinetAt(k).gameObject.activeSelf);

        [Test]
        public void ShowsSevenCabinetsCentredOnTheSelection()
        {
            var (view, all) = Build(10);
            Assert.AreEqual(7, ActiveCabinets());
            Assert.AreSame(all[0], room.CabinetAt(0).Entry);
            Assert.AreSame(all[9], room.CabinetAt(-1).Entry);
            Assert.AreEqual(all[0].Title, room.InfoTitle);
            Assert.AreEqual("All · 1 / 10", room.InfoStatus);
        }

        [Test]
        public void Next_ShiftsEntriesAndStartsTheGlide()
        {
            var (_, all) = Build(10);
            room.Next();
            Assert.AreSame(all[1], room.CabinetAt(0).Entry);
            Assert.AreSame(all[0], room.CabinetAt(-1).Entry);
            Assert.AreEqual(1f, room.ScrollOffset, 1e-5f);
        }

        [Test]
        public void RapidNext_ClampsScrollAndKeepsCentreInSync()
        {
            var (view, all) = Build(10);
            for (int i = 0; i < 5; i++) room.Next();
            Assert.AreEqual(ArcRoom.MaxScroll, room.ScrollOffset, 1e-5f);
            Assert.AreSame(view.Selected, room.CabinetAt(0).Entry);
            Assert.AreSame(all[5], room.CabinetAt(0).Entry);
        }

        [Test]
        public void FewTables_OnlyShowThatManyCabinets()
        {
            Build(2);
            Assert.AreEqual(2, ActiveCabinets());
            Assert.IsTrue(room.CabinetAt(1).gameObject.activeSelf);
            Assert.IsFalse(room.CabinetAt(-1).gameObject.activeSelf);
        }

        [Test]
        public void EmptyView_ShowsPlaceholder()
        {
            Build(3);
            room.CycleView();   // Favorites: none yet
            Assert.AreEqual(1, ActiveCabinets());
            Assert.AreEqual("No favorites yet", room.CabinetAt(0).PlaceholderText);
            Assert.AreEqual("No favorites yet", room.InfoTitle);
        }

        [Test]
        public void Refresh_ShowsFavoriteAndLastPlayed()
        {
            var (_, all) = Build(3);
            state.ToggleFavorite(all[0].RelativePath);
            state.RecordPlay(all[0].RelativePath, DateTime.UtcNow.AddDays(-3));
            room.Refresh();
            StringAssert.Contains("Favorite", room.InfoDetail);
            StringAssert.Contains("Last played 3 days ago", room.InfoDetail);
            StringAssert.StartsWith("Bally · 1990", room.InfoDetail);
        }

        [Test]
        public void Notice_OverridesStatusUntilCleared()
        {
            Build(3);
            room.ShowNotice("Keep holding Y to quit... 1.2s");
            Assert.AreEqual("Keep holding Y to quit... 1.2s", room.InfoStatus);
            room.ClearNotice();
            Assert.AreEqual("All · 1 / 3", room.InfoStatus);
        }

        [Test]
        public void Recenter_PutsThePlayfieldAheadAndBelowTheEyes()
        {
            Build(3);
            var head = new Vector3(1f, 1.2f, -2f);   // seated
            room.Recenter(head, 90f);                 // looking along +X

            Vector3 playfield = room.CabinetAt(0).transform.TransformPoint(CabinetView.PlayfieldCenter);
            Assert.AreEqual(head.y - ArcRoom.HeadAbovePlayfield, playfield.y, 1e-4f);
            Assert.AreEqual(head.x + ArcLayout.Radius + CabinetView.PlayfieldCenter.z, playfield.x, 1e-4f);
            Assert.AreEqual(head.z, playfield.z, 1e-4f);
        }
    }
}
