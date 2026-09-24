namespace VRLauncher
{
    /// <summary>Absolute paths to a table's media; null where the file does not exist.</summary>
    public sealed class MediaSet
    {
        public string Wheel;
        public string Playfield;
        public string Backglass;
        public string Video;
    }

    /// <summary>One .vpx table, its display name and its media.</summary>
    public sealed class TableEntry
    {
        /// <summary>Path relative to the tables directory; the key for favorites and play history.</summary>
        public string RelativePath { get; }
        public string FullPath { get; }
        /// <summary>File name without extension, e.g. "Attack from Mars (Bally 1995)".</summary>
        public string Stem { get; }
        public string Title { get; }
        /// <summary>Null when the file name has no "(Manufacturer Year)" group.</summary>
        public string Manufacturer { get; }
        /// <summary>0 when unknown.</summary>
        public int Year { get; }
        public MediaSet Media { get; }

        /// <summary>"Bally · 1995", or empty when the manufacturer is unknown.</summary>
        public string Subtitle => Manufacturer == null ? string.Empty : $"{Manufacturer} · {Year}";

        public TableEntry(string relativePath, string fullPath, string stem, string title,
                          string manufacturer, int year, MediaSet media)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Stem = stem;
            Title = title;
            Manufacturer = manufacturer;
            Year = year;
            Media = media ?? new MediaSet();
        }
    }
}
