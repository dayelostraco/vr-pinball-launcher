using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableCatalogTests
    {
        private const string Tables = @"C:\fake\Tables";
        private const string Media = @"C:\fake\Launcher\Media\Tables";
        private const string Wheels = @"C:\fake\Launcher\Media\Wheel";

        private static CatalogSettings Settings(bool recursive = true) => new CatalogSettings
        {
            TablesDirectory = Tables,
            SearchSubdirectories = recursive,
            TableMediaDirectory = Media,
            WheelDirectory = Wheels
        };

        private static string T(string name) => Path.Combine(Tables, name);
        private static string M(string table, string file) => Path.Combine(Media, table, file);
        private static string W(string file) => Path.Combine(Wheels, file);

        [Test]
        public void Scan_MissingTablesDirectory_ReturnsEmpty()
        {
            Assert.IsEmpty(TableCatalog.Scan(Settings(), new FakeFileSystem()));
        }

        [Test]
        public void Scan_FindsTablesRecursivelyAndSortsByStem()
        {
            var fs = new FakeFileSystem().Add(
                T("White Water (Williams 1993).vpx"),
                T(@"Sub\Attack from Mars (Bally 1995).vpx"),
                T("notes.txt"));

            var tables = TableCatalog.Scan(Settings(), fs);

            CollectionAssert.AreEqual(
                new[] { @"Sub\Attack from Mars (Bally 1995).vpx", "White Water (Williams 1993).vpx" },
                tables.Select(t => t.RelativePath).ToArray());
            Assert.AreEqual(T(@"Sub\Attack from Mars (Bally 1995).vpx"), tables[0].FullPath);
            Assert.AreEqual("Attack from Mars (Bally 1995)", tables[0].Stem);
        }

        [Test]
        public void Scan_NotRecursive_IgnoresSubfolders()
        {
            var fs = new FakeFileSystem().Add(T("A (Bally 1990).vpx"), T(@"Sub\B (Bally 1991).vpx"));
            Assert.AreEqual(1, TableCatalog.Scan(Settings(recursive: false), fs).Count);
        }

        [Test]
        public void Scan_ParsesDisplayTitleManufacturerAndYear()
        {
            var fs = new FakeFileSystem().Add(T("Simpsons Pinball Party, The (Stern 2003).vpx"));
            TableEntry entry = TableCatalog.Scan(Settings(), fs).Single();
            Assert.AreEqual("The Simpsons Pinball Party", entry.Title);
            Assert.AreEqual("Stern", entry.Manufacturer);
            Assert.AreEqual(2003, entry.Year);
            Assert.AreEqual("Stern · 2003", entry.Subtitle);
        }

        [Test]
        public void Subtitle_IsEmptyWithoutManufacturer()
        {
            var fs = new FakeFileSystem().Add(T("Pinball Training Lab.vpx"));
            Assert.AreEqual(string.Empty, TableCatalog.Scan(Settings(), fs).Single().Subtitle);
        }

        [Test]
        public void Media_PerTableFolderIsUsedAndWinsOverWheelPack()
        {
            const string name = "Attack from Mars (Bally 1995)";
            var fs = new FakeFileSystem().Add(
                T(name + ".vpx"),
                M(name, TableCatalog.WheelFile), M(name, TableCatalog.PlayfieldFile),
                M(name, TableCatalog.BackglassFile), M(name, TableCatalog.VideoFile),
                W(name + ".png"));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(M(name, "wheel.png"), media.Wheel);
            Assert.AreEqual(M(name, "table.png"), media.Playfield);
            Assert.AreEqual(M(name, "bg.png"), media.Backglass);
            Assert.AreEqual(M(name, "table.mp4"), media.Video);
        }

        [Test]
        public void Media_WheelFallsBackToPackUsingTitleYearMatch()
        {
            var fs = new FakeFileSystem().Add(
                T("Big Bang Bar (Capcom 1996) VPW 2.1.vpx"),
                W("Big Bang Bar (Capcom 1996).png"));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(W("Big Bang Bar (Capcom 1996).png"), media.Wheel);
            Assert.IsNull(media.Playfield);
        }

        [Test]
        public void Media_PartialFolderLeavesOthersNull()
        {
            const string name = "Congo (Williams 1995)";
            var fs = new FakeFileSystem().Add(T(name + ".vpx"), M(name, TableCatalog.VideoFile));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(M(name, "table.mp4"), media.Video);
            Assert.IsNull(media.Playfield);
            Assert.IsNull(media.Backglass);
            Assert.IsNull(media.Wheel);
        }

        [Test]
        public void Media_NoMediaOrWheelDirectories_AllNull()
        {
            var fs = new FakeFileSystem().Add(T("Congo (Williams 1995).vpx"));
            var settings = Settings();
            settings.TableMediaDirectory = null;
            settings.WheelDirectory = null;

            MediaSet media = TableCatalog.Scan(settings, fs).Single().Media;

            Assert.IsNull(media.Wheel);
            Assert.IsNull(media.Video);
        }

        [Test]
        public void WheelIndex_PrefersLeastDecoratedName()
        {
            var index = new WheelIndex(new[] { W("X-Men (Stern 2012) Alt.png"), W("X-Men (Stern 2012).png") });
            Assert.AreEqual(W("X-Men (Stern 2012).png"), index.Find("X-Men (Stern 2012) VPW", out string strategy));
            Assert.AreEqual("title+year", strategy);
        }
    }
}
