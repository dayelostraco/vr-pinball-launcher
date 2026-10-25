using System;

namespace VRLauncher
{
    /// <summary>Uses only the current table's fresh log section; never infers that a game is playable.</summary>
    public sealed class LaunchFeedback
    {
        private readonly string tablePath;
        private bool matchingTable;
        private bool problemBeforeStartup;
        public bool StartupCompleted { get; private set; }
        public string Problem { get; private set; }

        public LaunchFeedback(string tablePath) => this.tablePath = tablePath;

        public void Observe(string line)
        {
            if (line.Contains("LoadGameFromFilename"))
            {
                matchingTable = line.IndexOf(tablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                return;
            }
            if (!matchingTable) return;
            if (line.Contains("Startup done")) StartupCompleted = true;
            if (line.Contains("OnScriptError")) SetProblem("The table reported a script error.");
            else if (line.IndexOf("rom", StringComparison.OrdinalIgnoreCase) >= 0
                && (line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0))
                SetProblem("Required ROM files could not be loaded.");
        }

        private void SetProblem(string message)
        {
            Problem = message;
            if (!StartupCompleted) problemBeforeStartup = true;
        }

        public bool CountSession(int exitCode, double seconds, bool cancelled) =>
            !cancelled && !problemBeforeStartup && (StartupCompleted || (exitCode == 0 && seconds >= 15));

        public string ReturnNotice(int exitCode, double seconds, bool cancelled)
        {
            if (cancelled) return "Launch stopped. A: try again or browse another table.";
            if (Problem != null) return Problem + " A: retry or browse another table. See the launch log.";
            if (exitCode != 0) return $"VPX closed with an error (code {exitCode}). A: retry or choose another table.";
            if (!StartupCompleted && seconds < 15)
                return "VPX closed before startup was confirmed. A: retry or choose another table.";
            return null;
        }
    }
}
