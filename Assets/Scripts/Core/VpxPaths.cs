using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VRLauncher
{
    /// <summary>Path rules mirrored by SetupPaths.ps1 and checked by shared contract fixtures.</summary>
    public static class VpxPaths
    {
        public static string Resolve(string path, string appDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(appDirectory, expanded));
        }

        public static bool IsNative(string runner) => Regex.IsMatch(Path.GetFileName(runner) ?? "", @"^VPinballX(?:_|64|\.exe)", RegexOptions.IgnoreCase);

        public static string Target(string runner, string explicitTarget, string appDirectory)
        {
            if (IsNative(runner)) return runner;
            if (string.Equals(Path.GetFileName(runner), "VpxBackgroundInput.exe", StringComparison.OrdinalIgnoreCase))
            {
                string sidecar = Path.Combine(Path.GetDirectoryName(runner), "VpxBackgroundInput.vpx-path.txt");
                if (File.Exists(sidecar)) return Resolve(File.ReadAllText(sidecar), Path.GetDirectoryName(runner));
            }
            return Resolve(explicitTarget, appDirectory);
        }

        public static string[] RomDirectories(string table, string actualVpx, string registeredPaths)
        {
            var paths = new List<string>();
            if (!string.IsNullOrEmpty(table)) paths.Add(Path.Combine(Path.GetDirectoryName(table), "pinmame", "roms"));
            // Relative registry paths cannot be attributed to a working directory reliably.
            foreach (string path in (registeredPaths ?? "").Split(';'))
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
                if (Path.IsPathRooted(expanded) && !paths.Contains(expanded)) paths.Add(expanded);
            }
            if (!string.IsNullOrEmpty(actualVpx))
            {
                string root = Path.GetDirectoryName(actualVpx);
                paths.Add(Path.Combine(root, "VPinMAME", "roms"));
                paths.Add(Path.Combine(root, "plugins", "pinmame", "pinmame", "roms"));
            }
            return paths.ToArray();
        }
    }
}
