using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace VRLauncher
{
    /// <summary>
    /// The arcade room: floor, back wall, lights, 7 cabinets on an arc, and the info plate
    /// under the centred cabinet. Cabinet k always shows view.EntryAt(k); navigating shifts
    /// the entries and lets the arc glide back into place. Only the centred cabinet plays video.
    /// </summary>
    public sealed class ArcRoom : MonoBehaviour
    {
        /// <summary>The centred playfield sits this far below eye height, seated or standing.</summary>
        public const float HeadAbovePlayfield = 0.35f;
        public const float MaxScroll = 2f;

        /// <summary>Soft magenta for the back wall neon, kept dim so it never competes with the art.</summary>
        public static readonly Color NeonColor = new Color(0.62f, 0.07f, 0.46f);
        private const float ScrollSmoothTime = 0.08f;   // settles in about 0.25 s
        private const float VideoDelay = 0.3f;
        private static readonly Vector3 InfoPlateOffset = new Vector3(0f, 0.45f, -0.40f);

        private readonly Dictionary<int, CabinetView> cabinets = new Dictionary<int, CabinetView>();
        private TableListView view;
        private LauncherState state;
        private MediaCache cache;
        private string coinDoorImage;
        private TextMeshPro infoTitle;
        private TextMeshPro infoDetail;
        private TextMeshPro infoStatus;
        private string notice;
        private float scroll;
        private float scrollVelocity;
        private VideoPlayer video;
        private RenderTexture videoTexture;
        private CabinetView videoCabinet;
        private string videoPath;
        private float videoCountdown = -1f;
        private bool suspended;

        public float ScrollOffset => scroll;
        public string InfoTitle => infoTitle.text;
        public string InfoDetail => infoDetail.text;
        public string InfoStatus => infoStatus.text;

        public CabinetView CabinetAt(int offset) => cabinets[offset];

        public static ArcRoom Create(TableListView view, LauncherState state, MediaCache cache, string coinDoorImage = null)
        {
            var room = new GameObject("ArcRoom").AddComponent<ArcRoom>();
            room.view = view;
            room.state = state;
            room.cache = cache;
            room.coinDoorImage = coinDoorImage;
            room.Build();
            room.Assign();
            return room;
        }

        private void Build()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.10f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.06f;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);

            Surface("Floor", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(40f, 40f, 1f), new Color(0.03f, 0.03f, 0.04f), 0.8f);
            Surface("BackWall", new Vector3(0f, 3f, 6.5f), Quaternion.identity, new Vector3(30f, 8f, 1f), new Color(0.04f, 0.04f, 0.06f), 0.2f);
            BuildNeon();

            // The centre slot never moves, so one spotlight on it is the "key light".
            Vector3 target = ArcLayout.SlotPosition(0f) + CabinetView.PlayfieldCenter;
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 45f;
            key.range = 6f;
            key.intensity = 3f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(0f, 3.2f, 0.9f);
            key.transform.localRotation = Quaternion.LookRotation(target - key.transform.localPosition);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.3f;
            fill.color = new Color(0.75f, 0.8f, 1f);
            fill.transform.SetParent(transform, false);
            fill.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);

            for (int k = -ArcLayout.MaxOffset; k <= ArcLayout.MaxOffset; k++)
            {
                cabinets[k] = CabinetView.Create(transform, cache, coinDoorImage);
            }

            var plate = new GameObject("InfoPlate").transform;
            plate.SetParent(transform, false);
            plate.localPosition = ArcLayout.SlotPosition(0f) + InfoPlateOffset;
            plate.localRotation = Quaternion.Euler(35f, 0f, 0f);
            infoTitle = PlateLine(plate, "Title", 0.07f, new Vector2(0.9f, 0.08f));
            infoDetail = PlateLine(plate, "Detail", 0f, new Vector2(0.9f, 0.05f));
            infoStatus = PlateLine(plate, "Status", -0.06f, new Vector2(0.9f, 0.045f));
            infoStatus.color = new Color(0.7f, 0.7f, 0.75f);

            videoTexture = new RenderTexture(1920, 1080, 0) { name = "PlayfieldVideo" };
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.isLooping = true;
            video.skipOnDrop = true;
            video.source = VideoSource.Url;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = videoTexture;
            video.prepareCompleted += OnVideoPrepared;
            video.errorReceived += OnVideoError;
        }

        public void Next()
        {
            if (view.Items.Count == 0) return;
            view.Next();
            scroll = Mathf.Clamp(scroll + 1f, -MaxScroll, MaxScroll);
            Assign();
        }

        public void Previous()
        {
            if (view.Items.Count == 0) return;
            view.Previous();
            scroll = Mathf.Clamp(scroll - 1f, -MaxScroll, MaxScroll);
            Assign();
        }

        public void CycleView()
        {
            view.CycleView();
            scroll = 0f;
            scrollVelocity = 0f;
            Assign();
        }

        /// <summary>Re-reads favorites and play history (for example after returning from a table).</summary>
        public void Refresh()
        {
            view.Refresh();
            Assign();
        }

        /// <summary>Places the room so the centred playfield is ahead of and just below the head.</summary>
        public void Recenter(Vector3 headPosition, float yawDegrees)
        {
            float floor = headPosition.y - (CabinetView.PlayfieldCenter.y + HeadAbovePlayfield);
            transform.SetPositionAndRotation(new Vector3(headPosition.x, floor, headPosition.z), Quaternion.Euler(0f, yawDegrees, 0f));
        }

        public void PulseCenter() => CabinetAt(0).Pulse();

        public void ShowNotice(string message)
        {
            notice = message;
            UpdateInfo();
        }

        public void ClearNotice()
        {
            notice = null;
            UpdateInfo();
        }

        /// <summary>Stops video while a table is running.</summary>
        public void Suspend()
        {
            suspended = true;
            StopVideo();
        }

        public void Resume()
        {
            suspended = false;
            videoCountdown = VideoDelay;
        }

        private void Assign()
        {
            int count = view.Items.Count;
            IReadOnlyList<int> visible = ArcLayout.VisibleOffsets(count);
            TableEntry oldCentre = CabinetAt(0).Entry;

            foreach (KeyValuePair<int, CabinetView> pair in cabinets)
            {
                TableEntry entry = visible.Contains(pair.Key) ? view.EntryAt(pair.Key) : null;
                if (!ReferenceEquals(pair.Value.Entry, entry))
                {
                    pair.Value.SetEntry(entry);
                }
                pair.Value.SetFocused(pair.Key == 0);
            }

            if (count == 0)
            {
                CabinetAt(0).SetPlaceholder(view.EmptyMessage);
            }

            if (!ReferenceEquals(oldCentre, CabinetAt(0).Entry))
            {
                StopVideo();
                videoCountdown = VideoDelay;
            }

            UpdateInfo();
            Layout();
        }

        private void Layout()
        {
            // An empty view still shows its placeholder cabinet.
            IReadOnlyList<int> visible = ArcLayout.VisibleOffsets(Math.Max(view.Items.Count, 1));
            foreach (KeyValuePair<int, CabinetView> pair in cabinets)
            {
                float position = pair.Key + scroll;
                bool show = visible.Contains(pair.Key) && Mathf.Abs(position) <= ArcLayout.MaxOffset + 0.5f;
                pair.Value.gameObject.SetActive(show);
                if (show)
                {
                    pair.Value.transform.localPosition = ArcLayout.SlotPosition(position);
                    pair.Value.transform.localRotation = ArcLayout.SlotRotation(position);
                }
            }
        }

        private void UpdateInfo()
        {
            TableEntry entry = view.Selected;
            infoTitle.text = entry?.Title ?? view.EmptyMessage;
            infoDetail.text = entry == null ? string.Empty : DetailLine(entry);
            infoStatus.text = notice ?? view.PositionLabel;
        }

        private string DetailLine(TableEntry entry)
        {
            var parts = new List<string>();
            if (entry.Subtitle.Length > 0) parts.Add(entry.Subtitle);
            if (state.IsFavorite(entry.RelativePath)) parts.Add("<color=#FFC940>Favorite</color>");
            DateTime? last = state.LastPlayed(entry.RelativePath);
            if (last.HasValue) parts.Add("Last played " + RelativeTime.Format(last.Value, DateTime.UtcNow));
            return string.Join(" · ", parts);
        }

        private void Update()
        {
            if (scroll != 0f)
            {
                scroll = Mathf.SmoothDamp(scroll, 0f, ref scrollVelocity, ScrollSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                if (Mathf.Abs(scroll) < 0.0005f)
                {
                    scroll = 0f;
                    scrollVelocity = 0f;
                }
                Layout();
            }

            if (videoCountdown >= 0f && scroll == 0f)
            {
                videoCountdown -= Time.unscaledDeltaTime;
                if (videoCountdown < 0f) StartVideo();
            }
        }

        private void StartVideo()
        {
            if (!Application.isPlaying || suspended) return;
            CabinetView centre = CabinetAt(0);
            string path = centre.Entry?.Media.Video;
            if (path == null || (path == videoPath && video.isPlaying)) return;

            video.Stop();
            videoCabinet = centre;
            videoPath = path;
            video.url = path;
            video.Prepare();
        }

        private void StopVideo()
        {
            if (video != null) video.Stop();
            if (videoCabinet != null) videoCabinet.ShowStill();
            videoCabinet = null;
            videoPath = null;
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (suspended || videoCabinet == null || source.url != videoPath) return;
            source.Play();
            videoCabinet.ShowVideo(videoTexture);
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"ArcRoom: video failed for {videoPath}: {message}");
            if (videoCabinet != null) videoCabinet.ShowStill();
            videoCabinet = null;
            videoPath = null;
        }

        private void BuildNeon()
        {
            // Unlit sprites rather than lights: it reads as neon without any lighting cost, and it
            // stops drawing with the rest of the room while a table runs.
            var neon = new Material(Shader.Find("Sprites/Default")) { name = "Neon", color = NeonColor };
            var glow = new Material(Shader.Find("Sprites/Default"))
            {
                name = "NeonGlow",
                color = new Color(NeonColor.r, NeonColor.g, NeonColor.b, 0.18f)
            };

            NeonQuad("NeonGlow", new Vector3(0f, 3.30f, 6.44f), new Vector3(12.4f, 0.30f, 1f), glow);
            NeonQuad("NeonStrip", new Vector3(0f, 3.30f, 6.42f), new Vector3(12f, 0.04f, 1f), neon);

            var signObject = new GameObject("NeonSign");
            signObject.transform.SetParent(transform, false);
            signObject.transform.localPosition = new Vector3(0f, 4.00f, 6.42f);
            var sign = signObject.AddComponent<TextMeshPro>();
            sign.text = "PINBALL";
            sign.rectTransform.sizeDelta = new Vector2(4f, 0.8f);
            sign.alignment = TextAlignmentOptions.Center;
            sign.textWrappingMode = TextWrappingModes.NoWrap;
            sign.enableAutoSizing = true;
            sign.fontSizeMin = 1f;
            sign.fontSizeMax = 20f;
            sign.characterSpacing = 12f;
            sign.color = NeonColor;
        }

        private void NeonQuad(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void Surface(string name, Vector3 position, Quaternion rotation, Vector3 scale, Color color, float gloss)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", gloss);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static TextMeshPro PlateLine(Transform plate, string name, float y, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(plate, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = 4f;
            text.color = Color.white;
            return text;
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                if (Application.isPlaying) Destroy(videoTexture); else DestroyImmediate(videoTexture);
            }
        }
    }
}
