using System.Collections.Generic;
using System.Linq;

namespace VRLauncher.Tests
{
    /// <summary>Catalog entries without media, for list and room tests.</summary>
    public static class TestEntries
    {
        public static List<TableEntry> Make(int count) =>
            Named(Enumerable.Range(0, count).Select(i => $"Table {i:00} (Bally {1990 + i})").ToArray());

        public static List<TableEntry> Named(params string[] stems) => stems
            .Select(s =>
            {
                ParsedName name = TableNaming.Parse(s);
                return new TableEntry(s + ".vpx", @"C:\fake\Tables\" + s + ".vpx", s, name.Title, name.Manufacturer, name.Year, new MediaSet());
            })
            .ToList();
    }
}
