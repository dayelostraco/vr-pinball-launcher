using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Scans directories for Visual Pinball table files (.vpx)
    /// </summary>
    public class TableScanner : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Directory containing .vpx table files")]
        public string tablesDirectory = @"C:\Visual Pinball\Tables";

        [Tooltip("Directory containing wheel images")]
        public string wheelDirectory = @"Media\Wheel";

        [Tooltip("Search subdirectories for tables")]
        public bool searchSubdirectories = true;

        private List<TableInfo> cachedTables = new List<TableInfo>();

        // A "(Manufacturer Year)" group, e.g. "(Bally 1995)".
        private static readonly Regex YearGroupRegex =
            new Regex(@"\([^)]*\b(?:19|20)\d{2}\b[^)]*\)", RegexOptions.Compiled);

        private static readonly Regex YearTokenRegex =
            new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        // VPX VR-room conversions prefix the title; wheel art never does.
        private static readonly Regex VrRoomPrefixRegex =
            new Regex(@"^vr\s*room\s+", RegexOptions.Compiled);

        public class TableInfo
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public string Directory { get; set; }
            public string WheelImagePath { get; set; }

            public TableInfo(string fullPath)
            {
                FullPath = fullPath;
                Name = Path.GetFileNameWithoutExtension(fullPath);
                Directory = Path.GetDirectoryName(fullPath);
                WheelImagePath = null;
            }
        }

        /// <summary>
        /// Scans the configured directory for .vpx files
        /// </summary>
        /// <returns>List of found table files</returns>
        public List<TableInfo> ScanForTables()
        {
            cachedTables.Clear();

            if (string.IsNullOrEmpty(tablesDirectory))
            {
                Debug.LogError("Tables directory is not configured!");
                return cachedTables;
            }

            if (!Directory.Exists(tablesDirectory))
            {
                Debug.LogError($"Tables directory does not exist: {tablesDirectory}");
                return cachedTables;
            }

            try
            {
                SearchOption searchOption = searchSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                string[] tableFiles = Directory.GetFiles(
                    tablesDirectory,
                    "*.vpx",
                    searchOption
                );

                cachedTables = tableFiles
                    .Select(path => new TableInfo(path))
                    .OrderBy(table => table.Name)
                    .ToList();

                Debug.Log($"Found {cachedTables.Count} table(s) in {tablesDirectory}");

                // Load wheel images for tables
                LoadWheelImages();

                return cachedTables;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error scanning for tables: {ex.Message}");
                return cachedTables;
            }
        }

        /// <summary>
        /// Gets the cached list of tables (call ScanForTables first)
        /// </summary>
        public List<TableInfo> GetTables()
        {
            return cachedTables;
        }

        /// <summary>
        /// Refreshes the table list
        /// </summary>
        public void RefreshTables()
        {
            ScanForTables();
        }

        /// <summary>
        /// Loads wheel images for scanned tables
        /// </summary>
        private void LoadWheelImages()
        {
            if (string.IsNullOrEmpty(wheelDirectory))
            {
                Debug.LogWarning("Wheel directory not configured - skipping wheel image loading");
                return;
            }

            // Support both absolute and relative paths
            string wheelPath = wheelDirectory;
            if (!Path.IsPathRooted(wheelPath))
            {
                // Relative to working directory
                wheelPath = Path.Combine(Application.dataPath, "..", wheelDirectory);
                wheelPath = Path.GetFullPath(wheelPath);
            }

            if (!Directory.Exists(wheelPath))
            {
                Debug.LogWarning($"Wheel directory does not exist: {wheelPath} - skipping wheel image loading");
                return;
            }

            Debug.Log($"Scanning for wheel images in: {wheelPath}");

            // Scan for image files (png, jpg, jpeg)
            string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg" };
            var imageFiles = new List<string>();

            foreach (var ext in imageExtensions)
            {
                imageFiles.AddRange(Directory.GetFiles(wheelPath, ext, SearchOption.TopDirectoryOnly));
            }

            Debug.Log($"Found {imageFiles.Count} wheel images");

            // Index the images once, under three progressively looser keys, so
            // matching stays O(tables) rather than rescanning the whole
            // collection per table.
            var byExact = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            var byNormalized = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var byTitleYear = new Dictionary<string, string>(System.StringComparer.Ordinal);

            foreach (var img in imageFiles)
            {
                string imageName = Path.GetFileNameWithoutExtension(img);
                AddCandidate(byExact, imageName, img);
                AddCandidate(byNormalized, Normalize(imageName), img);
                AddCandidate(byTitleYear, TitleYearSignature(imageName), img);
            }

            // Match images to tables, loosening only as far as needed. Table
            // files carry author/version decoration ("... VPW v1.0.1") that
            // wheel art does not, so exact matching alone finds almost nothing.
            int matchedCount = 0;
            foreach (var table in cachedTables)
            {
                string normalized = Normalize(table.Name);
                string titleYear = TitleYearSignature(table.Name);

                string matchingImage;
                string strategy;

                if (byExact.TryGetValue(table.Name, out matchingImage))
                {
                    strategy = "exact";
                }
                else if (byNormalized.TryGetValue(normalized, out matchingImage))
                {
                    strategy = "normalized";
                }
                else if (byTitleYear.TryGetValue(titleYear, out matchingImage))
                {
                    strategy = "title+year";
                }
                else
                {
                    matchingImage = null;
                    strategy = null;
                }

                if (matchingImage != null)
                {
                    table.WheelImagePath = matchingImage;
                    matchedCount++;
                    Debug.Log($"Matched wheel image for '{table.Name}' [{strategy}]: {Path.GetFileName(matchingImage)}");
                }
                else
                {
                    // Log the keys that were tried - without this a miss is
                    // silent and indistinguishable from a missing directory.
                    Debug.LogWarning(
                        $"No wheel image for '{table.Name}' " +
                        $"(tried exact, normalized '{normalized}', title+year '{titleYear}')");
                }
            }

            Debug.Log($"Matched {matchedCount} of {cachedTables.Count} wheel images to tables");
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

            string existing;
            if (map.TryGetValue(key, out existing) &&
                Path.GetFileNameWithoutExtension(existing).Length <= Path.GetFileNameWithoutExtension(path).Length)
            {
                return;
            }

            map[key] = path;
        }

        /// <summary>
        /// Case/punctuation-insensitive form: underscores become spaces,
        /// parentheses are dropped, whitespace is collapsed, and a leading
        /// "VR ROOM" marker is removed.
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string result = name.Replace('_', ' ').Replace("(", "").Replace(")", "");
            result = WhitespaceRegex.Replace(result, " ").Trim().ToLowerInvariant();
            result = VrRoomPrefixRegex.Replace(result, "");

            return result.Trim();
        }

        /// <summary>
        /// Reduces a name to "title manufacturer year", discarding the author
        /// and version decoration that follows it.
        /// </summary>
        private static string TitleYearSignature(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // Preferred form: truncate after the "(Manufacturer Year)" group.
            Match group = YearGroupRegex.Match(name);
            if (group.Success)
            {
                return Normalize(name.Substring(0, group.Index + group.Length));
            }

            // Underscore-separated names have no parentheses to anchor on, so
            // fall back to the last year token. Using the last one keeps titles
            // that begin with a year (e.g. "2001 (Gottlieb 1971)") intact.
            string normalized = Normalize(name);
            MatchCollection years = YearTokenRegex.Matches(normalized);
            if (years.Count > 0)
            {
                Match last = years[years.Count - 1];
                return normalized.Substring(0, last.Index + last.Length).Trim();
            }

            return normalized;
        }

        void Awake()
        {
            // Load configuration first thing
            LauncherConfig config = LauncherConfig.Instance;
            tablesDirectory = config.tablesDirectory;
            searchSubdirectories = config.searchSubdirectories;
            wheelDirectory = config.wheelDirectory;

            Debug.Log($"TableScanner.Awake: Loaded config - tablesDirectory={tablesDirectory}, searchSubdirs={searchSubdirectories}, wheelDirectory={wheelDirectory}");
        }

        void Start()
        {
            Debug.Log($"TableScanner.Start: Scanning for tables in {tablesDirectory}");
            // Scan on startup
            ScanForTables();
        }
    }
}
