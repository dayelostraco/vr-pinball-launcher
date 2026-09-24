using System;
using System.Linq;

namespace VRLauncher
{
    /// <summary>
    /// Builds a file:// URI from an absolute path, escaping each segment so characters such as
    /// '#', '%', spaces and apostrophes in table names cannot change the URI's meaning.
    /// (new Uri(path) would treat '#' as the start of a fragment.)
    /// </summary>
    public static class FileUri
    {
        public static string FromPath(string fullPath)
        {
            string forward = fullPath.Replace('\\', '/');
            bool posix = forward.StartsWith("/", StringComparison.Ordinal);
            var segments = forward.Split('/')
                .Select((segment, i) => !posix && i == 0 ? segment : Uri.EscapeDataString(segment));
            return (posix ? "file://" : "file:///") + string.Join("/", segments);
        }
    }
}
