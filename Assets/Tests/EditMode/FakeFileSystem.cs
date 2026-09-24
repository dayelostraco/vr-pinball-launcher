using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VRLauncher.Tests
{
    /// <summary>In-memory file tree for catalog tests. Directories exist when they hold a file.</summary>
    public sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystem Add(params string[] paths)
        {
            foreach (string path in paths)
            {
                files.Add(Path.GetFullPath(path));
            }
            return this;
        }

        public bool FileExists(string path) => files.Contains(Path.GetFullPath(path));

        public bool DirectoryExists(string path)
        {
            string prefix = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return files.Any(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions)
        {
            string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            return files
                .Where(f => recursive
                    ? f.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase))
                .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
