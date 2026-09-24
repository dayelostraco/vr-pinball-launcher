using System;
using System.Collections.Generic;
using System.Linq;

namespace VRLauncher
{
    public enum ViewKind
    {
        All,
        Favorites,
        Recent
    }

    /// <summary>
    /// The list the arc shows (all tables, favorites, or recently played) and the selection
    /// within it. Navigation wraps. Switching views or refreshing keeps the same table
    /// selected whenever it is still in the list.
    /// </summary>
    public sealed class TableListView
    {
        private readonly IReadOnlyList<TableEntry> all;
        private readonly Dictionary<string, TableEntry> byPath;
        private readonly LauncherState state;
        private List<TableEntry> items = new List<TableEntry>();

        public ViewKind Kind { get; private set; } = ViewKind.All;
        public IReadOnlyList<TableEntry> Items => items;
        public int SelectedIndex { get; private set; }
        public TableEntry Selected => items.Count == 0 ? null : items[SelectedIndex];

        public TableListView(IReadOnlyList<TableEntry> all, LauncherState state)
        {
            this.all = all;
            this.state = state;
            byPath = all.GroupBy(e => e.RelativePath).ToDictionary(g => g.Key, g => g.First());
            Rebuild(null, 0);
        }

        public void Next()
        {
            if (items.Count > 0) SelectedIndex = (SelectedIndex + 1) % items.Count;
        }

        public void Previous()
        {
            if (items.Count > 0) SelectedIndex = (SelectedIndex - 1 + items.Count) % items.Count;
        }

        /// <summary>The entry <paramref name="offset"/> places from the selection, wrapping; null when empty.</summary>
        public TableEntry EntryAt(int offset)
        {
            int n = items.Count;
            if (n == 0) return null;
            return items[((SelectedIndex + offset) % n + n) % n];
        }

        public void CycleView()
        {
            TableEntry keep = Selected;
            Kind = (ViewKind)(((int)Kind + 1) % 3);
            Rebuild(keep, 0);
        }

        /// <summary>Rebuilds the list after favorites or play history changed.</summary>
        public void Refresh() => Rebuild(Selected, SelectedIndex);

        public bool Select(string relativePath)
        {
            int index = items.FindIndex(e => e.RelativePath == relativePath);
            if (index < 0) return false;
            SelectedIndex = index;
            return true;
        }

        /// <summary>"All · 12 / 42", or just the view name when it is empty.</summary>
        public string PositionLabel => items.Count == 0 ? Kind.ToString() : $"{Kind} · {SelectedIndex + 1} / {items.Count}";

        public string EmptyMessage
        {
            get
            {
                switch (Kind)
                {
                    case ViewKind.Favorites: return "No favorites yet";
                    case ViewKind.Recent: return "Nothing played yet";
                    default: return "No tables found";
                }
            }
        }

        private void Rebuild(TableEntry keep, int fallbackIndex)
        {
            switch (Kind)
            {
                case ViewKind.Favorites:
                    items = all.Where(e => state.IsFavorite(e.RelativePath)).ToList();
                    break;
                case ViewKind.Recent:
                    items = state.RecentPaths()
                        .Select(p => byPath.TryGetValue(p, out TableEntry e) ? e : null)
                        .Where(e => e != null)
                        .ToList();
                    break;
                default:
                    items = all.ToList();
                    break;
            }

            int kept = keep == null ? -1 : items.IndexOf(keep);
            SelectedIndex = kept >= 0 ? kept : (items.Count == 0 ? 0 : Math.Min(fallbackIndex, items.Count - 1));
        }
    }
}
