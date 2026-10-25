using System;
using System.Collections.Generic;
using System.IO;

namespace VRLauncher
{
    /// <summary>Absolute directories the catalog scans. Media and wheel directories may be null.</summary>
    public sealed class CatalogSettings
    {
        public string TablesDirectory;
        public bool SearchSubdirectories = true;
        public string TableMediaDirectory;
        public string WheelDirectory;
    }

    /// <summary>
    /// Finds every .vpx table and resolves its media: first from
    /// &lt;TableMediaDirectory&gt;\&lt;stem&gt;\ (fetched by tools/fetch_media.py), then, for the
    /// wheel only, from the flat wheel pack.
    /// </summary>
    public static class TableCatalog
    {
        public const string WheelFile = "wheel.png";
        public const string PlayfieldFile = "table.png";
        public const string BackglassFile = "bg.png";
        public const string VideoFile = "table.mp4";

        public static List<TableEntry> Scan(CatalogSettings settings, IFileSystem fs)
        {
            var tables = new List<TableEntry>();
            if (string.IsNullOrEmpty(settings.TablesDirectory) || !fs.DirectoryExists(settings.TablesDirectory))
            {
                return tables;
            }

            WheelIndex wheels = !string.IsNullOrEmpty(settings.WheelDirectory) && fs.DirectoryExists(settings.WheelDirectory)
                ? new WheelIndex(fs.GetFiles(settings.WheelDirectory, false, ".png", ".jpg", ".jpeg"))
                : null;

            foreach (string path in fs.GetFiles(settings.TablesDirectory, settings.SearchSubdirectories, ".vpx"))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                ParsedName name = TableNaming.Parse(stem);
                tables.Add(new TableEntry(
                    Path.GetRelativePath(settings.TablesDirectory, path),
                    path,
                    stem,
                    name.Title,
                    name.Manufacturer,
                    name.Year,
                    ResolveMedia(stem, settings.TableMediaDirectory, wheels, fs)));
            }

            tables.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Stem, b.Stem));
            return tables;
        }

        private static MediaSet ResolveMedia(string stem, string mediaRoot, WheelIndex wheels, IFileSystem fs)
        {
            string folder = string.IsNullOrEmpty(mediaRoot) ? null : Path.Combine(mediaRoot, stem);

            string Existing(string file)
            {
                if (folder == null)
                {
                    return null;
                }
                string candidate = Path.Combine(folder, file);
                return fs.FileExists(candidate) ? candidate : null;
            }

            var media = new MediaSet
            {
                Wheel = Existing(WheelFile),
                Playfield = Existing(PlayfieldFile),
                Backglass = Existing(BackglassFile),
                Video = Existing(VideoFile)
            };

            if (media.Wheel == null && wheels != null)
            {
                media.Wheel = wheels.Find(stem, out _);
            }

            return media;
        }
    }
}
