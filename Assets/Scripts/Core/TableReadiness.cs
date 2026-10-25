using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;

namespace VRLauncher
{
    /// <summary>File evidence only. Never treats a launch or a ROM ZIP as proof of gameplay.</summary>
    public static class TableReadiness
    {
        [DataContract]
        private sealed class Requirements
        {
            [DataMember] public string rom;
            [DataMember] public string[] plugins = null; // Populated by the JSON contract.
        }

        public static string Describe(string table, LaunchCommand command, string actualVpx, string[] romDirectories,
                                      LauncherState state, string relativePath)
        {
            var lines = new List<string>();
            bool missingIni = !string.IsNullOrEmpty(command.IniFile) && !File.Exists(command.IniFile);
            bool missing = !File.Exists(table) || !File.Exists(command.Executable) || !Directory.Exists(command.WorkingDirectory) || missingIni;
            string evidence = "ROM / DMD requirements: unknown (embedded script not scanned)";
            try
            {
                Requirements requirements = null;
                string manifest = table + ".readiness.json";
                if (File.Exists(manifest))
                    requirements = JsonData.Read<Requirements>(File.ReadAllText(manifest));
                else
                {
                    string script = Path.ChangeExtension(table, ".vbs");
                    if (File.Exists(script))
                    {
                        // Only a single literal assignment is evidence; dynamic/multiple ROMs remain unknown.
                        string uncommented = string.Join("\n", File.ReadAllLines(script).Where(l => !l.TrimStart().StartsWith("'")));
                        string[] names = Regex.Matches(uncommented, @"(?im)^\s*(?:Const\s+)?(?:cGameName|GameName|Controller\.GameName)\s*=\s*""([a-z0-9_\-]+)""")
                            .Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        if (names.Length == 1) requirements = new Requirements { rom = names[0] };
                    }
                }
                if (requirements != null)
                {
                    var checks = new List<string>();
                    if (!string.IsNullOrEmpty(requirements.rom))
                    {
                        if (!Regex.IsMatch(requirements.rom, @"\A[a-zA-Z0-9_\-]+\z")) throw new InvalidDataException("Invalid ROM name");
                        bool found = (romDirectories ?? Array.Empty<string>()).Any(d => !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, requirements.rom + ".zip")));
                        checks.Add("ROM " + requirements.rom + (found ? ": ZIP found (contents unverified)" : ": not found in checked libraries"));
                        // A library may be configured outside our detected roots; absence is uncertain, not a definitive failure.
                    }
                    foreach (string plugin in requirements.plugins ?? Array.Empty<string>())
                    {
                        if (!Regex.IsMatch(plugin ?? "", @"\A(?:pinmame|serum|scoreview|flexdmd|pup|b2s)\z")) throw new InvalidDataException("Unsupported plugin name");
                        bool found = !string.IsNullOrEmpty(actualVpx) && File.Exists(Path.Combine(Path.GetDirectoryName(actualVpx), "plugins", plugin, "plugin-" + plugin + "64.dll"));
                        checks.Add(plugin + (string.IsNullOrEmpty(actualVpx) ? ": target unknown; review helper in setup" : found ? ": installed (enablement unverified)" : ": native plugin not detected"));
                    }
                    evidence = checks.Count > 0 ? string.Join("\n", checks) : "Requirements not specified";
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is SerializationException)
            { evidence = "Requirements could not be read; check sidecar / permissions"; }
            string problem = state.LastLaunchProblem(relativePath);
            string status = missing || !string.IsNullOrEmpty(problem) ? "Needs attention" : "Not yet tested";
            // Startup confirmation is useful evidence, but cannot establish that coins, audio and scoring work.
            if (!missing && string.IsNullOrEmpty(problem) && state.StartupConfirmed(relativePath)) status = "Startup confirmed - gameplay unverified";
            lines.Add(status);
            lines.Add("Profile: " + (command.ProfileName ?? "Default") + " / " + Path.GetFileName(command.Executable));
            if (!File.Exists(table)) lines.Add("Table file missing");
            if (!File.Exists(command.Executable)) lines.Add("Launch program missing");
            if (!Directory.Exists(command.WorkingDirectory)) lines.Add("Working folder missing");
            if (missingIni) lines.Add("Profile INI missing");
            lines.Add(evidence);
            DateTime? attempt = state.LastLaunchTime(relativePath);
            lines.Add("Last launch: " + (attempt.HasValue ? attempt.Value.ToLocalTime().ToString("g") : "Not recorded"));
            if (attempt.HasValue) lines.Add(problem ?? (state.StartupConfirmed(relativePath) ? "VPX initialization confirmed" : "Closed without startup confirmation"));
            return string.Join("\n", lines);
        }
    }
}
