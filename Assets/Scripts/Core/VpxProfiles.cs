using System;
using System.IO;

namespace VRLauncher
{
    /// <summary>
    /// An extra Visual Pinball install that some tables run on instead of the default one,
    /// e.g. a 10.8.0 install kept for tables that break on 10.8.1.
    /// </summary>
    [Serializable]
    public class VpxProfile
    {
        /// <summary>Name that tableProfiles refer to, e.g. "10.8.0".</summary>
        public string name = "";

        /// <summary>Path to the VPinballX executable.</summary>
        public string executable = "";

        /// <summary>Optional settings file, passed as -Ini. Empty uses VPX's default.</summary>
        public string iniFile = "";

        /// <summary>Working directory. Empty uses the executable's folder; "table" uses the table's folder.</summary>
        public string workingDirectory = "";
    }

    /// <summary>Assigns one table, by file name without extension, to a VpxProfile.</summary>
    [Serializable]
    public class TableProfile
    {
        public string table = "";
        public string profile = "";
    }

    /// <summary>The executable, arguments and working directory for one table launch.</summary>
    public sealed class LaunchCommand
    {
        public string Executable { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
        /// <summary>The profile name, or null for the default install.</summary>
        public string ProfileName { get; }

        public LaunchCommand(string executable, string arguments, string workingDirectory, string profileName)
        {
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            ProfileName = profileName;
        }
    }

    public static class VpxProfiles
    {
        /// <summary>Working-directory value that means "the folder the table is in".</summary>
        public const string TableFolder = "table";

        /// <summary>
        /// Builds the launch command for a table. Tables with no assignment, or whose profile
        /// name isn't defined, use the default executable. Matching ignores case.
        /// </summary>
        public static LaunchCommand Resolve(string tablePath, string defaultExecutable,
                                            VpxProfile[] profiles, TableProfile[] tables)
        {
            VpxProfile profile = FindProfile(Path.GetFileNameWithoutExtension(tablePath), profiles, tables);
            if (profile == null)
            {
                return new LaunchCommand(defaultExecutable, $"-Play \"{tablePath}\"",
                                         Path.GetDirectoryName(defaultExecutable), null);
            }

            string args = $"-Play \"{tablePath}\"";
            if (!string.IsNullOrEmpty(profile.iniFile))
                args = $"-Ini \"{profile.iniFile}\" " + args;

            string workingDir;
            if (string.IsNullOrEmpty(profile.workingDirectory))
                workingDir = Path.GetDirectoryName(profile.executable);
            else if (string.Equals(profile.workingDirectory, TableFolder, StringComparison.OrdinalIgnoreCase))
                workingDir = Path.GetDirectoryName(tablePath);
            else
                workingDir = profile.workingDirectory;

            return new LaunchCommand(profile.executable, args, workingDir, profile.name);
        }

        private static VpxProfile FindProfile(string stem, VpxProfile[] profiles, TableProfile[] tables)
        {
            if (profiles == null || tables == null) return null;

            foreach (TableProfile entry in tables)
            {
                if (entry == null || !string.Equals(entry.table, stem, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (VpxProfile profile in profiles)
                {
                    if (profile != null && string.Equals(profile.name, entry.profile, StringComparison.OrdinalIgnoreCase))
                        return profile;
                }
                return null;
            }
            return null;
        }
    }
}
