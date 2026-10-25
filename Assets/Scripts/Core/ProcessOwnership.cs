using System;
using System.Collections.Generic;

namespace VRLauncher
{
    public readonly struct ProcessIdentity
    {
        public readonly int Id;
        public readonly DateTime StartedUtc;
        public ProcessIdentity(int id, DateTime startedUtc) { Id = id; StartedUtc = startedUtc; }
    }

    /// <summary>
    /// Validates every generation of a process tree, not just its newest leaf.
    /// A reused parent PID must never authorize input or termination of another
    /// application's descendants. Unreadable identities fail closed.
    /// </summary>
    public static class ProcessOwnership
    {
        public static List<ProcessIdentity> FindOwned(ProcessIdentity root,
            IReadOnlyDictionary<int, int> parents, Func<int, DateTime?> readStartedUtc,
            DateTime snapshotStartedUtc)
        {
            var owned = new List<ProcessIdentity> { root };
            var visited = new HashSet<int> { root.Id };
            for (int index = 0; index < owned.Count; index++)
            {
                ProcessIdentity parent = owned[index];
                foreach (var link in parents)
                {
                    if (link.Value != parent.Id || !visited.Add(link.Key)) continue;
                    DateTime? started = readStartedUtc(link.Key);
                    if (!started.HasValue || started.Value < parent.StartedUtc || started.Value > snapshotStartedUtc) continue;
                    owned.Add(new ProcessIdentity(link.Key, started.Value));
                }
            }
            return owned;
        }
    }
}
