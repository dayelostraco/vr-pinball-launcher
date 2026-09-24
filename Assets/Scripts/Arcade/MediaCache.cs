using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace VRLauncher
{
    /// <summary>
    /// Loads table art off the main thread and keeps the most recently used textures
    /// (15 tables x 3 images), destroying the rest. Several requests for the same file
    /// share one load. In edit mode (tests, preview renders) loads are synchronous.
    /// </summary>
    public sealed class MediaCache : MonoBehaviour
    {
        public const int Capacity = 45;

        private LruCache<string, Texture2D> cache;
        private readonly Dictionary<string, List<Action<Texture2D>>> pending =
            new Dictionary<string, List<Action<Texture2D>>>(StringComparer.OrdinalIgnoreCase);

        // Created lazily: Awake does not run for components added in edit mode.
        private LruCache<string, Texture2D> Cache =>
            cache ?? (cache = new LruCache<string, Texture2D>(Capacity, (_, tex) => DestroyTexture(tex), StringComparer.OrdinalIgnoreCase));

        public void Request(string path, Action<Texture2D> onLoaded)
        {
            if (string.IsNullOrEmpty(path))
            {
                onLoaded(null);
                return;
            }

            if (Cache.TryGet(path, out Texture2D cached))
            {
                onLoaded(cached);
                return;
            }

            if (!Application.isPlaying)
            {
                Texture2D loaded = LoadImmediate(path);
                if (loaded != null) Cache.Add(path, loaded);
                onLoaded(loaded);
                return;
            }

            if (pending.TryGetValue(path, out var waiting))
            {
                waiting.Add(onLoaded);
                return;
            }

            pending[path] = new List<Action<Texture2D>> { onLoaded };
            StartCoroutine(Load(path));
        }

        private IEnumerator Load(string path)
        {
            Texture2D texture = null;
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(FileUri.FromPath(path), true))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    texture = DownloadHandlerTexture.GetContent(request);
                    texture.name = Path.GetFileName(path);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    Cache.Add(path, texture);
                }
                else
                {
                    Debug.LogWarning($"MediaCache: could not load {path}: {request.error}");
                }
            }

            List<Action<Texture2D>> callbacks = pending[path];
            pending.Remove(path);
            foreach (Action<Texture2D> callback in callbacks)
            {
                callback(texture);
            }
        }

        private static Texture2D LoadImmediate(string path)
        {
            if (!File.Exists(path)) return null;

            var texture = new Texture2D(2, 2) { name = Path.GetFileName(path), wrapMode = TextureWrapMode.Clamp };
            if (texture.LoadImage(File.ReadAllBytes(path)))
            {
                return texture;
            }

            DestroyTexture(texture);
            return null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        private void OnDestroy()
        {
            cache?.Clear();
        }
    }
}
