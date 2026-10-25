using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRLauncher.EditorTools
{
    /// <summary>Reproducible screenshots using a synthetic library, without table files or community artwork.</summary>
    public static class ReleasePreview
    {
        public static void Render()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var entries = new List<TableEntry>();
                string directory = Path.GetFullPath("Logs/release-preview");
                Directory.CreateDirectory(directory);
                foreach (string title in new[] { "Aurora", "Comet", "Deep Space", "Orbit", "Starlight" })
                {
                    float hue = 0.48f + entries.Count * 0.13f;
                    var media = new MediaSet {
                        Playfield = DrawArt(directory, title + "-table", hue, true),
                        Backglass = DrawArt(directory, title + "-backglass", hue, false)
                    };
                    entries.Add(new TableEntry(title + ".vpx", title + ".vpx", title, title, "Demo library", 2026, media));
                }
                string statePath = Path.Combine(Path.GetTempPath(), "launcher-demo-" + Guid.NewGuid().ToString("N") + ".json");
                var state = LauncherState.Load(statePath);
                state.ToggleFavorite(entries[0].RelativePath);
                state.ToggleFavorite(entries[2].RelativePath);
                state.RecordPlay(entries[1].RelativePath, DateTime.UtcNow);
                var view = new TableListView(entries, state);
                var host = new GameObject("Demo");
                var room = ArcRoom.Create(view, state, host.AddComponent<MediaCache>());
                var head = new Vector3(0, 1.66f, 0);
                room.Recenter(head, 0);
                var camera = ArcadePreview.MakeCamera(head, Quaternion.Euler(15, 0, 0), 100);
                for (int step = 0; step < 5; step++)
                {
                    if (step == 1) { view.Next(); room.Refresh(); room.ShowNotice("Browse: left stick sideways / arrow keys"); }
                    if (step == 2) { room.CycleView(); room.ShowNotice("Favorites: left stick up/down / V"); }
                    if (step == 3) { room.CycleView(); room.ShowNotice("Recently played: pick up where you left off"); }
                    if (step == 4) { room.ToggleDetails(); room.ShowNotice("Details: left stick click / I. Setup: F2 on PC"); }
                    ArcadePreview.RenderImage(camera, Path.Combine(directory, "step-" + step + ".png"));
                }
                Debug.Log("[unity-batch] OK release demo uses synthetic library; no live state written");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        private static string DrawArt(string directory, string name, float hue, bool playfield)
        {
            int width = playfield ? 256 : 512, height = playfield ? 512 : 256;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            Color accent = Color.HSVToRGB(hue % 1f, 0.7f, 1f);
            var bumpers = new[] { new Vector2(.32f, .65f), new Vector2(.68f, .65f), new Vector2(.5f, .79f) };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var p = new Vector2((float)x / width, (float)y / height);
                Color color = Color.Lerp(new Color(.025f, .035f, .09f), accent * .25f, p.y);
                if ((x * 73 + y * 137) % 1297 < 3) color = Color.Lerp(accent, Color.white, .65f);
                if (playfield)
                {
                    if (Mathf.Abs(p.x - .08f) < .01f || Mathf.Abs(p.x - .92f) < .01f) color = accent;
                    foreach (var bumper in bumpers)
                    {
                        float d = Vector2.Distance(p, bumper);
                        if (d < .075f) color = accent * .7f;
                        if (Mathf.Abs(d - .075f) < .008f || d < .025f) color = Color.white;
                    }
                    if (SegmentDistance(p, new Vector2(.19f, .23f), new Vector2(.44f, .15f)) < .02f ||
                        SegmentDistance(p, new Vector2(.81f, .23f), new Vector2(.56f, .15f)) < .02f) color = Color.white;
                    if (Mathf.Abs(p.y - (.35f + .08f * Mathf.Sin(p.x * 12f))) < .008f) color = accent;
                }
                else
                {
                    float d = Vector2.Distance(p, new Vector2(.5f, .5f));
                    if (d < .22f) color = Color.Lerp(accent * .2f, accent, p.x);
                    if (Mathf.Abs(d - .32f) < .009f) color = accent;
                }
                pixels[y * width + x] = color;
            }
            if (playfield)
            {
                // CabinetView maps landscape VPX captures onto the tilted playfield.
                var rotated = new Color[pixels.Length];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    rotated[x * height + height - 1 - y] = pixels[y * width + x];
                UnityEngine.Object.DestroyImmediate(image);
                image = new Texture2D(height, width, TextureFormat.RGB24, false);
                pixels = rotated;
            }
            image.SetPixels(pixels);
            image.Apply();
            string path = Path.Combine(directory, name + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            return path;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude));
        }
    }
}
