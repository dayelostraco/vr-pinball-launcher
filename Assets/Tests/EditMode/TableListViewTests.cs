using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableListViewTests
    {
        private string dir;
        private LauncherState state;
        private static readonly DateTime Noon = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-view-" + Guid.NewGuid().ToString("N"));
            state = LauncherState.Load(Path.Combine(dir, "state.json"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void All_ListsEveryTableAndStartsAtTheFirst()
        {
            var view = new TableListView(TestEntries.Make(5), state);
            Assert.AreEqual(ViewKind.All, view.Kind);
            Assert.AreEqual(5, view.Items.Count);
            Assert.AreEqual(0, view.SelectedIndex);
        }

        [Test]
        public void NextAndPrevious_WrapAround()
        {
            var view = new TableListView(TestEntries.Make(3), state);
            view.Previous();
            Assert.AreEqual(2, view.SelectedIndex);
            view.Next();
            view.Next();
            Assert.AreEqual(1, view.SelectedIndex);
        }

        [Test]
        public void EntryAt_WrapsBothWays()
        {
            List<TableEntry> all = TestEntries.Make(4);
            var view = new TableListView(all, state);
            Assert.AreSame(all[3], view.EntryAt(-1));
            Assert.AreSame(all[1], view.EntryAt(5));
        }

        [Test]
        public void Empty_HasNoSelectionAndNavigationIsSafe()
        {
            var view = new TableListView(new List<TableEntry>(), state);
            view.Next();
            view.Previous();
            Assert.IsNull(view.Selected);
            Assert.IsNull(view.EntryAt(2));
            Assert.AreEqual("No tables found", view.EmptyMessage);
        }

        [Test]
        public void CycleView_GoesAllFavoritesRecentAll()
        {
            var view = new TableListView(TestEntries.Make(2), state);
            view.CycleView();
            Assert.AreEqual(ViewKind.Favorites, view.Kind);
            view.CycleView();
            Assert.AreEqual(ViewKind.Recent, view.Kind);
            view.CycleView();
            Assert.AreEqual(ViewKind.All, view.Kind);
        }

        [Test]
        public void CycleView_KeepsTheSelectedTableWhenItIsInTheNextView()
        {
            List<TableEntry> all = TestEntries.Make(5);
            state.ToggleFavorite(all[1].RelativePath);
            state.ToggleFavorite(all[3].RelativePath);
            var view = new TableListView(all, state);
            view.Select(all[3].RelativePath);

            view.CycleView();

            Assert.AreSame(all[3], view.Selected);
            Assert.AreEqual(1, view.SelectedIndex);
        }

        [Test]
        public void CycleView_StartsAtTheTopWhenTheTableIsNotInTheNextView()
        {
            List<TableEntry> all = TestEntries.Make(5);
            state.ToggleFavorite(all[1].RelativePath);
            var view = new TableListView(all, state);
            view.Select(all[4].RelativePath);

            view.CycleView();

            Assert.AreSame(all[1], view.Selected);
        }

        [Test]
        public void Favorites_UnfavoriteSelected_MovesToTheNextOne()
        {
            List<TableEntry> all = TestEntries.Make(5);
            foreach (int i in new[] { 0, 2, 4 }) state.ToggleFavorite(all[i].RelativePath);
            var view = new TableListView(all, state);
            view.CycleView();
            view.Select(all[2].RelativePath);

            state.ToggleFavorite(all[2].RelativePath);
            view.Refresh();

            Assert.AreEqual(2, view.Items.Count);
            Assert.AreSame(all[4], view.Selected);
        }

        [Test]
        public void Favorites_UnfavoriteLastRemaining_BecomesEmpty()
        {
            List<TableEntry> all = TestEntries.Make(3);
            state.ToggleFavorite(all[2].RelativePath);
            var view = new TableListView(all, state);
            view.CycleView();

            state.ToggleFavorite(all[2].RelativePath);
            view.Refresh();

            Assert.IsNull(view.Selected);
            Assert.AreEqual(0, view.SelectedIndex);
            Assert.AreEqual("No favorites yet", view.EmptyMessage);
        }

        [Test]
        public void Recent_IsNewestFirstAndFollowsNewPlays()
        {
            List<TableEntry> all = TestEntries.Make(4);
            state.RecordPlay(all[0].RelativePath, Noon);
            state.RecordPlay(all[2].RelativePath, Noon.AddHours(1));
            var view = new TableListView(all, state);
            view.CycleView();
            view.CycleView();
            CollectionAssert.AreEqual(new[] { all[2], all[0] }, view.Items);

            view.Select(all[0].RelativePath);
            state.RecordPlay(all[0].RelativePath, Noon.AddHours(2));
            view.Refresh();

            CollectionAssert.AreEqual(new[] { all[0], all[2] }, view.Items);
            Assert.AreSame(all[0], view.Selected);
        }

        [Test]
        public void Recent_And_Favorites_IgnoreMissingTables()
        {
            List<TableEntry> all = TestEntries.Make(2);
            state.RecordPlay("Renamed Table (Bally 1999).vpx", Noon);
            state.ToggleFavorite("Deleted Table (Bally 1998).vpx");
            var view = new TableListView(all, state);

            view.CycleView();
            Assert.AreEqual(0, view.Items.Count);
            view.CycleView();
            Assert.AreEqual(0, view.Items.Count);
            Assert.AreEqual("Nothing played yet", view.EmptyMessage);
        }

        [Test]
        public void PositionLabel_ShowsViewAndPosition()
        {
            var view = new TableListView(TestEntries.Make(42), state);
            view.Next();
            Assert.AreEqual("All · 2 / 42", view.PositionLabel);
            view.CycleView();
            Assert.AreEqual("Favorites", view.PositionLabel);
        }
    }
}
