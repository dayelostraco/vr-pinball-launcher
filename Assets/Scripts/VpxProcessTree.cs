using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRLauncher
{
    /// <summary>Rescue/shutdown must stop the VPX child as well as a windowless wrapper.</summary>
    internal static class VpxProcessTree
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Entry
        {
            public uint size, usage, pid;
            public IntPtr heap;
            public uint module, threads, parent;
            public int priority;
            public uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

        public static bool OwnsForeground(Process root)
        {
            if (root == null) return false;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return OwnsForeground(root, (int)pid);
        }

        internal static bool OwnsForeground(Process root, int pid)
        {
            if (root == null) return false;
            try
            {
                if (root.HasExited) return false;
                if (pid == root.Id) return true;
                foreach (ProcessIdentity identity in OwnedSnapshot(root))
                    if (identity.Id == pid) return true;
            }
            catch (ArgumentException) { } // Foreground process exited during inspection.
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { } // Fail closed if ownership is inaccessible.
            return false;
        }

        private static List<Entry> Snapshot()
        {
            var entries = new List<Entry>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot != new IntPtr(-1))
            {
                try
                {
                    var entry = new Entry { size = (uint)Marshal.SizeOf(typeof(Entry)) };
                    if (Process32FirstW(snapshot, ref entry))
                        do { entries.Add(entry); } while (Process32NextW(snapshot, ref entry));
                }
                finally { CloseHandle(snapshot); }
            }
            return entries;
        }

        public static void Kill(Process root)
        {
            var identities = OwnedSnapshot(root);
            for (int i = identities.Count - 1; i >= 0; i--)
            {
                try
                {
                    using (Process child = Process.GetProcessById(identities[i].Id))
                        if (child.StartTime.ToUniversalTime() == identities[i].StartedUtc && !child.HasExited) child.Kill();
                }
                catch (ArgumentException) { } // It already exited.
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception ex) { UnityEngine.Debug.LogWarning("Could not stop launch process: " + ex.Message); }
            }
        }

        private static List<ProcessIdentity> OwnedSnapshot(Process root)
        {
            var identity = new ProcessIdentity(root.Id, root.StartTime.ToUniversalTime());
            DateTime snapshotStarted = DateTime.UtcNow;
            var parents = new Dictionary<int, int>();
            foreach (var entry in Snapshot()) parents[(int)entry.pid] = (int)entry.parent;
            return ProcessOwnership.FindOwned(identity, parents, ReadStartedUtc, snapshotStarted);
        }

        private static DateTime? ReadStartedUtc(int id)
        {
            try { using (Process process = Process.GetProcessById(id)) return process.StartTime.ToUniversalTime(); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
    }
}
