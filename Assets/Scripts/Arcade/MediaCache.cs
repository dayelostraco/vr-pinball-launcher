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
    /// Pinned textures (<see cref="RequestPinned"/>) never enter the LRU, so they are
    /// never evicted; they are destroyed only when this MediaCache is destroyed.
    /// </summary>
    public sealed class MediaCache : MonoBehaviour
    {
        public const int Capacity = 45;

        private LruCache<string, Texture2D> cache;
        private readonly Dictionary<string, Texture2D> pinnedCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pinning =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action<Texture2D>>> pending =
            new Dictionary<string, List<Action<Texture2D>>>(StringComparer.OrdinalIgnoreCase);

        // Created lazily: Awake does not run for components added in edit mode.
        private LruCache<string, Texture2D> Cache =>
            cache ?? (cache = new LruCache<string, Texture2D>(Capacity, (_, tex) => DestroyTexture(tex), StringComparer.OrdinalIgnoreCase));

        public void Request(string path, Action<Texture2D> onLoaded) => RequestInternal(path, onLoaded, pin: false);

        /// <summary>
        /// Like <see cref="Request"/>, but the loaded texture is pinned: it is kept in its own
        /// set, never placed in the LRU, and so is never evicted. A path already cached in the
        /// LRU is moved into the pinned set. Concurrent requests (pinned or not) for the same
        /// path share one load.
        /// </summary>
        public void RequestPinned(string path, Action<Texture2D> onLoaded) => RequestInternal(path, onLoaded, pin: true);

        private void RequestInternal(string path, Action<Texture2D> onLoaded, bool pin)
        {
            if (string.IsNullOrEmpty(path))
            {
                onLoaded(null);
                return;
            }

            if (pinnedCache.TryGetValue(path, out Texture2D pinned))
            {
                onLoaded(pinned);
                return;
            }

            if (Cache.TryGet(path, out Texture2D cached))
            {
                if (pin) MoveToPinned(path, cached);
                onLoaded(cached);
                return;
            }

            if (pin) pinning.Add(path);

            if (!Application.isPlaying)
            {
                Texture2D loaded = LoadImmediate(path);
                if (loaded != null) StoreLoaded(path, loaded);
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

        private void MoveToPinned(string path, Texture2D texture)
        {
            Cache.Remove(path);
            pinnedCache[path] = texture;
        }

        private void StoreLoaded(string path, Texture2D texture)
        {
            if (pinning.Remove(path))
            {
                pinnedCache[path] = texture;
            }
            else
            {
                Cache.Add(path, texture);
            }
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
                    StoreLoaded(path, texture);
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
            foreach (Texture2D texture in pinnedCache.Values) DestroyTexture(texture);
            pinnedCache.Clear();
        }
    }
}
