using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace VRLauncher
{
    /// <summary>
    /// Favorites and play history, persisted as JSON. Keys are table paths relative to the
    /// tables directory, so renaming a table drops its history. An unreadable file is moved
    /// aside to "state.json.bad" and the launcher starts fresh rather than failing.
    /// </summary>
    public sealed class LauncherState
    {
        public const int CurrentVersion = 1;

        [DataContract]
        private sealed class PlayRecord
        {
            [DataMember] public string path;
            [DataMember] public string last;   // ISO 8601, UTC
            [DataMember] public int count;
        }

        [DataContract]
        private sealed class LaunchRecord
        {
            [DataMember] public string path;
            [DataMember] public string when;
            [DataMember] public string result;
            [DataMember] public bool startupConfirmed;
        }

        // Keep the version-one list representation compatible with existing backups.
        [DataContract]
        private sealed class StateData
        {
            [DataMember] public int version = CurrentVersion;
            [DataMember] public List<string> favorites = new List<string>();
            [DataMember] public List<PlayRecord> plays = new List<PlayRecord>();
            [DataMember] public List<LaunchRecord> launches = new List<LaunchRecord>();
            [DataMember] public int lastView;
            [DataMember] public string lastSelectedPath;
        }

        private readonly StateData data;

        public string FilePath { get; }

        /// <summary>Set when an unreadable state file was moved aside during Load.</summary>
        public string LoadWarning { get; private set; }

        private LauncherState(string filePath, StateData data)
        {
            FilePath = filePath;
            this.data = data;
        }

        public static LauncherState Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new LauncherState(filePath, new StateData());
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new LauncherState(filePath, new StateData())
                {
                    LoadWarning = $"State file could not be read ({ex.Message}); started fresh."
                };
            }

            StateData loaded = null;
            string problem = null;
            try
            {
                loaded = JsonData.Read<StateData>(json);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is SerializationException)
            {
                problem = ex.Message;
            }

            if (problem == null && (loaded == null || loaded.version != CurrentVersion || loaded.favorites == null || loaded.plays == null))
            {
                problem = "unrecognised contents";
            }

            if (problem == null)
            {
                if (loaded.launches == null) loaded.launches = new List<LaunchRecord>();
                if (loaded.favorites.Any(string.IsNullOrWhiteSpace)
                    || loaded.plays.Any(p => p == null || string.IsNullOrWhiteSpace(p.path) || p.count < 0)
                    || loaded.launches.Any(p => p == null || string.IsNullOrWhiteSpace(p.path)))
                    problem = "invalid history record";
            }
            if (problem == null)
            {
                return new LauncherState(filePath, loaded);
            }

            string bad = filePath + ".bad";
            try
            {
                if (File.Exists(bad))
                {
                    File.Delete(bad);
                }
                File.Move(filePath, bad);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new LauncherState(filePath, new StateData())
                {
                    LoadWarning = $"State file was unreadable ({problem}); could not move it aside ({ex.Message}); started fresh."
                };
            }

            return new LauncherState(filePath, new StateData())
            {
                LoadWarning = $"State file was unreadable ({problem}); moved it to {bad} and started fresh."
            };
        }

        public bool IsFavorite(string relativePath) => data.favorites.Contains(relativePath);

        public ViewKind LastView => Enum.IsDefined(typeof(ViewKind), data.lastView) ? (ViewKind)data.lastView : ViewKind.All;
        public string LastSelectedPath => data.lastSelectedPath;

        public void RememberBrowsing(ViewKind kind, string relativePath)
        {
            data.lastView = (int)kind;
            data.lastSelectedPath = relativePath;
        }

        /// <summary>Flips the favorite flag and returns the new value.</summary>
        public bool ToggleFavorite(string relativePath)
        {
            if (data.favorites.Remove(relativePath))
            {
                return false;
            }
            data.favorites.Add(relativePath);
            return true;
        }

        public void RecordPlay(string relativePath, DateTime utcNow)
        {
            PlayRecord record = data.plays.FirstOrDefault(p => p.path == relativePath);
            if (record == null)
            {
                record = new PlayRecord { path = relativePath };
                data.plays.Add(record);
            }
            record.last = utcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            if (record.count < int.MaxValue) record.count++;
        }

        public DateTime? LastPlayed(string relativePath)
        {
            PlayRecord record = data.plays.FirstOrDefault(p => p.path == relativePath);
            return record == null ? null : ParseTime(record.last);
        }

        public int PlayCount(string relativePath) =>
            data.plays.FirstOrDefault(p => p.path == relativePath)?.count ?? 0;

        public void RecordLaunch(string path, DateTime utcNow, bool startupConfirmed, string problem)
        {
            LaunchRecord record = data.launches.FirstOrDefault(p => p.path == path);
            if (record == null) { record = new LaunchRecord { path = path }; data.launches.Add(record); }
            record.when = utcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            record.startupConfirmed = startupConfirmed;
            record.result = problem;
        }

        public string LastLaunchProblem(string path)
        {
            string result = data.launches.FirstOrDefault(p => p.path == path)?.result;
            return string.IsNullOrEmpty(result) ? null : result;
        }
        public bool StartupConfirmed(string path) => data.launches.FirstOrDefault(p => p.path == path)?.startupConfirmed ?? false;
        public DateTime? LastLaunchTime(string path) => ParseTime(data.launches.FirstOrDefault(p => p.path == path)?.when);

        /// <summary>Played tables, most recent first.</summary>
        public IReadOnlyList<string> RecentPaths() => data.plays
            .Select(p => (p.path, when: ParseTime(p.last)))
            .Where(p => p.when.HasValue)
            .OrderByDescending(p => p.when.Value)
            .ThenBy(p => p.path, StringComparer.Ordinal)
            .Select(p => p.path)
            .ToList();

        /// <summary>Writes atomically: a temp file, then a replace, so a crash never leaves half a file.</summary>
        public void Save()
        {
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, JsonData.Write(data));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static DateTime? ParseTime(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed.ToUniversalTime()
                : (DateTime?)null;
    }
}
