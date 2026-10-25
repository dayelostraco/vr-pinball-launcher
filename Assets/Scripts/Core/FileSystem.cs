using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VRLauncher
{
    /// <summary>The file-system calls the catalog needs, so tests can use an in-memory tree.</summary>
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);

        /// <summary>
        /// Files in <paramref name="directory"/> (and below it when <paramref name="recursive"/>)
        /// whose extension, including the dot, matches one of <paramref name="extensions"/>, ignoring case.
        /// </summary>
        IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions);
    }

    /// <summary>The real disk.</summary>
    public sealed class DiskFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions)
        {
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(directory, "*", option)
                .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
