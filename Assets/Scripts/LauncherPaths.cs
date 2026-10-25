using System.IO;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Resolves configured directories. Relative paths are relative to the launcher's own
    /// folder (next to vr-launch.exe), or to the project root in the editor.
    /// </summary>
    public static class LauncherPaths
    {
        public static string AppDirectory => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            : Path.GetDirectoryName(Application.dataPath);

        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return VpxPaths.Resolve(path, AppDirectory);
        }

        public static CatalogSettings CatalogSettingsFrom(LauncherConfig config) => new CatalogSettings
        {
            TablesDirectory = Resolve(config.tablesDirectory),
            SearchSubdirectories = config.searchSubdirectories,
            TableMediaDirectory = Resolve(config.tableMediaDirectory),
            WheelDirectory = Resolve(config.wheelDirectory)
        };
    }
}
