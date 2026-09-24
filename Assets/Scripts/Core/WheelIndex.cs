using System;
using System.Collections.Generic;
using System.IO;

namespace VRLauncher
{
    /// <summary>
    /// Looks up wheel art for a table in a flat wheel pack, loosening the match only as far
    /// as needed: exact file name, then normalised name, then title plus manufacturer and year.
    /// Table files carry author/version decoration ("... VPW v1.0.1") that wheel art does not.
    /// </summary>
    public sealed class WheelIndex
    {
        private readonly Dictionary<string, string> byExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> byNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> byTitleYear = new Dictionary<string, string>(StringComparer.Ordinal);

        public WheelIndex(IEnumerable<string> imagePaths)
        {
            foreach (string image in imagePaths)
            {
                string name = Path.GetFileNameWithoutExtension(image);
                AddCandidate(byExact, name, image);
                AddCandidate(byNormalized, TableNaming.Normalize(name), image);
                AddCandidate(byTitleYear, TableNaming.TitleYearSignature(name), image);
            }
        }

        /// <summary>The best wheel image for a table stem, or null. Strategy is "exact", "normalized" or "title+year".</summary>
        public string Find(string tableStem, out string strategy)
        {
            if (byExact.TryGetValue(tableStem, out string image)) { strategy = "exact"; return image; }
            if (byNormalized.TryGetValue(TableNaming.Normalize(tableStem), out image)) { strategy = "normalized"; return image; }
            if (byTitleYear.TryGetValue(TableNaming.TitleYearSignature(tableStem), out image)) { strategy = "title+year"; return image; }
            strategy = null;
            return null;
        }

        /// <summary>
        /// Records a lookup key, preferring the least decorated filename when
        /// several images share it, so "Table (Maker 1995).png" wins over
        /// "Table (Maker 1995) BW Mod.png".
        /// </summary>
        private static void AddCandidate(Dictionary<string, string> map, string key, string path)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (map.TryGetValue(key, out string existing) &&
                Path.GetFileNameWithoutExtension(existing).Length <= Path.GetFileNameWithoutExtension(path).Length)
            {
                return;
            }

            map[key] = path;
        }
    }
}
