using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// Batchmode preview renders of the arcade room, written to Logs/, so geometry and
    /// fallbacks can be checked without a headset. Run through tools/unity-batch.ps1.
    /// </summary>
    public static class ArcadePreview
    {
        private static string InstalledDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VR Pinball Launcher");

        /// <summary>The installed launcher's tables with media resolved exactly as the launcher does.</summary>
        public static List<TableEntry> LoadInstalledCatalog()
        {
            string configPath = Path.Combine(InstalledDirectory, "launcher-config.json");
            var config = JsonUtility.FromJson<LauncherConfig>(File.ReadAllText(configPath));
            string Resolve(string p) => string.IsNullOrEmpty(p) ? null : Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(InstalledDirectory, p));
            var settings = new CatalogSettings
            {
                TablesDirectory = config.tablesDirectory,
                SearchSubdirectories = config.searchSubdirectories,
                TableMediaDirectory = Resolve(config.tableMediaDirectory),
                WheelDirectory = Resolve(config.wheelDirectory)
            };
            return TableCatalog.Scan(settings, new DiskFileSystem());
        }

        /// <summary>Three cabinets side by side: full media, no playfield image, no media at all.</summary>
        public static void RenderCabinets()
        {
            Run("preview-cabinets.png", () =>
            {
                List<TableEntry> tables = LoadInstalledCatalog();
                TableEntry full = tables.First(t => t.Media.Playfield != null && t.Media.Backglass != null && t.Media.Wheel != null);
                TableEntry noPlayfield = new TableEntry(full.RelativePath, full.FullPath, full.Stem, full.Title, full.Manufacturer, full.Year,
                    new MediaSet { Wheel = full.Media.Wheel, Backglass = full.Media.Backglass });
                TableEntry bare = new TableEntry("Pinball Training Lab.vpx", "", "Pinball Training Lab", "Pinball Training Lab", null, 0, new MediaSet());

                var host = new GameObject("Preview");
                MediaCache cache = host.AddComponent<MediaCache>();
                TableEntry[] entries = { noPlayfield, full, bare };
                for (int i = 0; i < entries.Length; i++)
                {
                    CabinetView cabinet = CabinetView.Create(host.transform, cache);
                    cabinet.transform.localPosition = new Vector3((i - 1) * 1.0f, 0f, 0f);
                    cabinet.SetEntry(entries[i]);
                    cabinet.SetFocused(i == 1);
                }

                AddLights();
                return MakeCamera(new Vector3(0f, 1.66f, -2.2f), Quaternion.Euler(12f, 0f, 0f), 70f);
            });
        }

        /// <summary>The whole room from a standing player's eyes, headset-like field of view.</summary>
        public static void RenderArc()
        {
            Run("preview-arc.png", () =>
            {
                List<TableEntry> tables = LoadInstalledCatalog();
                string stateFile = Path.Combine(Path.GetTempPath(), "vrl-preview-state.json");
                if (File.Exists(stateFile)) File.Delete(stateFile);
                LauncherState state = LauncherState.Load(stateFile);
                var view = new TableListView(tables, state);
                view.Select(tables.First(t => t.Media.Playfield != null).RelativePath);

                var host = new GameObject("Preview");
                ArcRoom room = ArcRoom.Create(view, state, host.AddComponent<MediaCache>());
                var head = new Vector3(0f, 1.66f, 0f);
                room.Recenter(head, 0f);
                return MakeCamera(head, Quaternion.Euler(15f, 0f, 0f), 100f);
            });
        }

        /// <summary>Opens an empty scene, lets <paramref name="build"/> populate it and return a camera, renders, saves Logs/<paramref name="fileName"/>.</summary>
        public static void Run(string fileName, Func<Camera> build)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Camera camera = build();
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", fileName));
                Render(camera, path);
                Debug.Log($"[unity-batch] OK wrote {path}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[unity-batch] FAILED {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void AddLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.28f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.8f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        public static Camera MakeCamera(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            return camera;
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1600, height = 900;
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
