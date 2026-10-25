using System;
using System.IO;

namespace VRLauncher
{
    /// <summary>
    /// The supported AutoHotkey contract places the script first, followed by
    /// literal script arguments. Compiled/custom bridges own their argument syntax.
    /// Relative script paths use the launcher directory, never the caller's cwd.
    /// </summary>
    public static class BridgeArguments
    {
        public static string ScriptPath(string arguments)
        {
            string text = (arguments ?? "").Trim();
            if (text.Length == 0) return null;
            int end;
            string first;
            if (text[0] == '"')
            {
                end = text.IndexOf('"', 1);
                if (end < 0) return null;
                first = text.Substring(1, end - 1);
            }
            else
            {
                end = text.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                first = end < 0 ? text : text.Substring(0, end);
            }
            return first.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase) ? first : null;
        }

        public static string Resolve(string arguments, string appDirectory)
        {
            string text = (arguments ?? "").Trim();
            string script = ScriptPath(text);
            if (script == null) return text;
            int consumed = text[0] == '"' ? script.Length + 2 : script.Length;
            return "\"" + VpxPaths.Resolve(script, appDirectory) + "\"" + text.Substring(consumed);
        }

        public static bool Validate(string executable, string arguments, string workingDirectory,
            string appDirectory, IFileSystem files, out string missing)
        {
            missing = null;
            if (!files.DirectoryExists(workingDirectory))
            {
                missing = $"bridge folder not found: '{workingDirectory}'.";
                return false;
            }
            // A compiled bridge need not ship the source script or these particular DLLs.
            if (!Path.GetFileName(executable).StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase)) return true;
            string script = ScriptPath(arguments);
            if (script == null)
            {
                missing = "AutoHotkey arguments must start with the quoted .ahk script path.";
                return false;
            }
            string scriptPath = VpxPaths.Resolve(script, appDirectory);
            if (!files.FileExists(scriptPath))
            {
                missing = $"bridge script not found: '{scriptPath}'.";
                return false;
            }
            // The bridge's includes and native loads are relative to its script folder.
            string directory = Path.GetDirectoryName(scriptPath);
            foreach (string file in new[] { "auto_oculus_touch.ahk", "auto_oculus_touch.dll", "vJoyInterface.dll" })
            {
                if (!files.FileExists(Path.Combine(directory, file)))
                {
                    missing = $"'{file}' is missing from '{directory}'.";
                    return false;
                }
            }
            return true;
        }
    }
}
