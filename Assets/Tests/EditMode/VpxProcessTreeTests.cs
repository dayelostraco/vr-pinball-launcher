using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class VpxProcessTreeTests
    {
        [Test]
        public void HiddenFixtureTreeIsNotForegroundAndRescueStopsBothProcesses()
        {
            Assert.IsFalse(VpxProcessTree.OwnsForeground(null));
            string directory = Path.Combine(Path.GetTempPath(), "vrl-process-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            string pidFile = Path.Combine(directory, "child.txt");
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            string script = "$child = Start-Process -FilePath '" + shell.Replace("'", "''") +
                "' -ArgumentList '-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"' -WindowStyle Hidden -PassThru; " +
                "[IO.File]::WriteAllText('" + pidFile.Replace("'", "''") + ".tmp', [string]$child.Id); " +
                "[IO.File]::Move('" + pidFile.Replace("'", "''") + ".tmp', '" + pidFile.Replace("'", "''") + "'); Start-Sleep -Seconds 60";
            Process root = null, child = null;
            try
            {
                root = Process.Start(new ProcessStartInfo(shell, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
                    { UseShellExecute = false, CreateNoWindow = true });
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(pidFile) && deadline.Elapsed.TotalSeconds < 10) Thread.Sleep(50);
                Assert.IsTrue(File.Exists(pidFile), "Fixture child did not start");
                child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)));
                Assert.IsFalse(VpxProcessTree.OwnsForeground(root));
                Assert.IsTrue(VpxProcessTree.OwnsForeground(root, root.Id));
                Assert.IsTrue(VpxProcessTree.OwnsForeground(root, child.Id));
                Assert.IsFalse(VpxProcessTree.OwnsForeground(root, Process.GetCurrentProcess().Id));
                VpxProcessTree.Kill(root);
                Assert.IsTrue(root.WaitForExit(5000), "Wrapper survived rescue");
                Assert.IsTrue(child.WaitForExit(5000), "Child survived rescue");
                Assert.IsFalse(VpxProcessTree.OwnsForeground(root));
            }
            finally
            {
                // Handles belong only to the self-expiring fixtures created above.
                foreach (var process in new[] { child, root })
                {
                    if (process == null) continue;
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); }
                    process.Dispose();
                }
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void UnassociatedProcessCannotOwnForeground()
        {
            using (var process = new Process())
                Assert.IsFalse(VpxProcessTree.OwnsForeground(process, 1));
        }
    }
}
