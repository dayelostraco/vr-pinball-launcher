using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// Applies Assets/Icons/AppIcon_*.png as the Standalone player icons.
    ///
    /// Run from the menu, or headlessly via
    ///   -executeMethod VRLauncher.EditorTools.IconSetup.Apply
    ///
    /// Unity stores the result in ProjectSettings.asset, so this only needs
    /// running when the artwork changes.
    /// </summary>
    public static class IconSetup
    {
        private const string IconDir = "Assets/Icons";
        private const string Prefix = "AppIcon_";

        [MenuItem("Tools/VR Launcher/Apply App Icons")]
        public static void Apply()
        {
            Dictionary<int, Texture2D> bySize = LoadIcons();

            if (bySize.Count == 0)
            {
                Debug.LogError($"No {Prefix}*.png found in {IconDir} - cannot set icons.");
                return;
            }

            NamedBuildTarget target = NamedBuildTarget.Standalone;
            int[] required = PlayerSettings.GetIconSizes(target, IconKind.Any);

            if (required == null || required.Length == 0)
            {
                Debug.LogError("Unity reported no icon slots for Standalone.");
                return;
            }

            Texture2D[] icons = new Texture2D[required.Length];
            for (int i = 0; i < required.Length; i++)
            {
                icons[i] = Nearest(bySize, required[i]);
            }

            PlayerSettings.SetIcons(target, icons, IconKind.Any);
            AssetDatabase.SaveAssets();

            Debug.Log($"Applied app icons to {required.Length} Standalone slot(s): " +
                      string.Join(", ", required));
        }

        private static Dictionary<int, Texture2D> LoadIcons()
        {
            var bySize = new Dictionary<int, Texture2D>();

            if (!Directory.Exists(IconDir))
            {
                return bySize;
            }

            foreach (string path in Directory.GetFiles(IconDir, Prefix + "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int size;
                if (!int.TryParse(name.Substring(Prefix.Length), out size))
                {
                    continue;
                }

                string assetPath = path.Replace('\\', '/');
                PrepareImporter(assetPath);

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null)
                {
                    bySize[size] = tex;
                }
            }

            return bySize;
        }

        /// <summary>
        /// Icon source textures must be uncompressed and full-resolution, or
        /// Unity bakes a blurry, block-artefacted icon into the executable.
        /// </summary>
        private static void PrepareImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; dirty = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
            if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; dirty = true; }
            if (importer.alphaIsTransparency != true) { importer.alphaIsTransparency = true; dirty = true; }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static Texture2D Nearest(Dictionary<int, Texture2D> bySize, int want)
        {
            Texture2D exact;
            if (bySize.TryGetValue(want, out exact))
            {
                return exact;
            }

            // Prefer the smallest source at or above the requested size;
            // downscaling keeps detail that upscaling cannot invent.
            int best = -1;
            foreach (int size in bySize.Keys)
            {
                if (size >= want && (best == -1 || size < best))
                {
                    best = size;
                }
            }

            if (best == -1)
            {
                foreach (int size in bySize.Keys)
                {
                    if (best == -1 || size > best)
                    {
                        best = size;
                    }
                }
            }

            return bySize[best];
        }
    }
}
