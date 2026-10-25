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
    [ExecuteAlways] // Preview rooms must release generated materials on destruction.
    public sealed class ArcRoom : MonoBehaviour
    {
        /// <summary>The centred playfield sits this far below eye height, seated or standing.</summary>
        public const float HeadAbovePlayfield = 0.35f;
        public const float MaxScroll = 2f;
        private const float ScrollSmoothTime = 0.08f;   // settles in about 0.25 s
        private const float VideoDelay = 0.3f;
        private static readonly Vector3 InfoPlateOffset = new Vector3(0f, 0.85f, -0.65f);

        private readonly Dictionary<int, CabinetView> cabinets = new Dictionary<int, CabinetView>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private TableListView view;
        private LauncherState state;
        private MediaCache cache;
        private string coinDoorImage;
        private TextMeshPro infoTitle;
        private TextMeshPro infoDetail;
        private TextMeshPro infoStatus;
        private TextMeshPro viewTabs;
        private GameObject detailsPanel;
        private TextMeshPro detailsTitle;
        private TextMeshPro detailsBody;
        private bool detailsOpen;
        private string notice;
        private float scroll;
        private float scrollVelocity;
        private VideoPlayer video;
        private RenderTexture videoTexture;
        private CabinetView videoCabinet;
        private string videoPath;
        private float videoCountdown = -1f;
        private bool suspended;
        private readonly System.Random random = new System.Random();

        public float ScrollOffset => scroll;
        public string InfoTitle => infoTitle.text;
        public string InfoDetail => infoDetail.text;
        public string InfoStatus => infoStatus.text;
        public string ViewTabs => viewTabs.text;
        public bool DetailsVisible => detailsPanel.activeSelf;
        public string DetailsText => detailsBody.text;
        public Func<TableEntry, string> ReadinessDetails { get; set; }

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

            // The centre slot never moves, so one spotlight on it is the "key light".
            Vector3 target = ArcLayout.SlotPosition(0f) + CabinetView.PlayfieldCenter;
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 36f;
            key.range = 6f;
            key.intensity = 4f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(0f, 3.2f, 0.9f);
            key.transform.localRotation = Quaternion.LookRotation(target - key.transform.localPosition);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.22f;
            fill.color = new Color(0.75f, 0.8f, 1f);
            fill.transform.SetParent(transform, false);
            fill.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);

            for (int k = -ArcLayout.MaxOffset; k <= ArcLayout.MaxOffset; k++)
            {
                cabinets[k] = CabinetView.Create(transform, cache, coinDoorImage);
            }

            BuildSelectionMarker();

            var plate = new GameObject("InfoPlate").transform;
            plate.SetParent(transform, false);
            plate.localPosition = ArcLayout.SlotPosition(0f) + InfoPlateOffset;
            plate.localRotation = Quaternion.Euler(25f, 0f, 0f);
            PanelQuad(plate, "PlateBackground", new Vector3(0f, 0.12f, 0.012f), new Vector2(1.2f, 0.52f), new Color(0.025f, 0.035f, 0.05f));
            PanelQuad(plate, "AccentRule", new Vector3(0f, 0.38f, 0.005f), new Vector2(1.2f, 0.006f), new Color(0.25f, 0.7f, 0.85f));
            viewTabs = PlateLine(plate, "ViewTabs", 0.31f, new Vector2(1.1f, 0.07f));
            viewTabs.fontSizeMax = 2.1f;
            infoTitle = PlateLine(plate, "Title", 0.17f, new Vector2(1.1f, 0.13f));
            infoTitle.fontStyle = FontStyles.Bold;
            infoTitle.fontSizeMax = 4.5f;
            infoTitle.textWrappingMode = TextWrappingModes.Normal;
            infoDetail = PlateLine(plate, "Detail", 0.065f, new Vector2(1.08f, 0.065f));
            infoDetail.fontSizeMax = 1.8f;
            infoDetail.textWrappingMode = TextWrappingModes.Normal;
            infoDetail.color = new Color(0.77f, 0.83f, 0.88f);
            infoStatus = PlateLine(plate, "Status", -0.025f, new Vector2(1.08f, 0.075f));
            infoStatus.textWrappingMode = TextWrappingModes.Normal;
            infoStatus.fontSizeMax = 1.4f;
            infoStatus.color = new Color(0.45f, 0.8f, 0.9f);
            var reminder = PlateLine(plate, "ControlsReminder", -0.105f, new Vector2(1.08f, 0.04f));
            reminder.text = "A: play · Click left stick: details & controls";
            reminder.fontSizeMax = 1.3f;
            reminder.color = new Color(0.68f, 0.76f, 0.82f);
            BuildDetailsPanel();

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
            ChangeView(1);
        }

        public void JumpLetter(int direction)
        {
            view.JumpLetter(direction);
            scroll = 0f;
            scrollVelocity = 0f;
            Assign();
        }

        public void SurpriseMe()
        {
            view.SelectRandom(random);
            scroll = 0f;
            scrollVelocity = 0f;
            Assign();
        }

        public void ToggleDetails()
        {
            detailsOpen = !detailsOpen;
            UpdateInfo();
        }

        private void BuildDetailsPanel()
        {
            detailsPanel = new GameObject("TableDetails");
            Transform panel = detailsPanel.transform;
            panel.SetParent(transform, false);
            panel.localPosition = ArcLayout.SlotPosition(0f) + new Vector3(1.2f, 1.15f, -0.9f);
            panel.localRotation = Quaternion.Euler(0f, 35f, 0f);
            PanelQuad(panel, "DetailsBackground", new Vector3(0f, 0f, 0.012f), new Vector2(1.0f, 1.55f), new Color(0.025f, 0.04f, 0.055f));

            detailsTitle = PlateLine(panel, "DetailsTitle", 0.65f, new Vector2(0.9f, 0.15f));
            detailsTitle.textWrappingMode = TextWrappingModes.Normal;
            detailsTitle.color = new Color(0.85f, 0.95f, 1f);
            detailsBody = PlateLine(panel, "DetailsBody", 0.18f, new Vector2(0.88f, 0.72f));
            detailsBody.alignment = TextAlignmentOptions.Left;
            detailsBody.textWrappingMode = TextWrappingModes.Normal;
            HintGroup(panel, "NavigationHints", -0.225f,
                "<b>NAVIGATE</b>\nLeft sideways: tables\nLeft up/down: views\nRight up/down: letters");
            HintGroup(panel, "ActionHints", 0.225f,
                "<b>PLAY & EXPLORE</b>\nA: play · B: favorite · X: views\nRight click: Surprise Me\nHold Y: quit");
            var footer = PlateLine(panel, "DetailsFooter", -0.69f, new Vector2(0.9f, 0.09f));
            footer.text = "Click left stick to close · A to play\nF2 on PC: setup";
            footer.color = new Color(0.65f, 0.75f, 0.8f);
            detailsPanel.SetActive(false);
        }

        public void PreviousView()
        {
            ChangeView(-1);
        }

        private void ChangeView(int direction)
        {
            view.CycleView(direction);
            if (view.Selected == null) detailsOpen = false;
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
            detailsPanel.SetActive(false);
        }

        public void Resume()
        {
            suspended = false;
            videoCountdown = VideoDelay;
            UpdateInfo();
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
            infoStatus.color = notice == null ? new Color(0.45f, 0.8f, 0.9f) : new Color(1f, 0.8f, 0.45f);
            viewTabs.text = string.Join("   ", new[] { ViewKind.All, ViewKind.Favorites, ViewKind.Recent }
                .Select(kind => kind == view.Kind
                    ? "<color=#7DE3FF><b>[ " + kind.ToString().ToUpperInvariant() + " ]</b></color>"
                    : "<color=#8994A3>" + kind.ToString().ToUpperInvariant() + "</color>"));
            detailsPanel.SetActive(detailsOpen && !suspended);
            if (entry == null && detailsOpen)
            {
                detailsTitle.text = "Controls";
                detailsBody.text = "No table selected.\nUse left stick up/down or X\nto switch views.";
            }
            if (entry != null && detailsOpen)
            {
                detailsTitle.text = entry.Title;
                DateTime? last = state.LastPlayed(entry.RelativePath);
                detailsBody.text = "Manufacturer: " + (entry.Manufacturer ?? "Unknown")
                    + "\nYear: " + (entry.Year > 0 ? entry.Year.ToString() : "Unknown")
                    + "\nSessions played: " + state.PlayCount(entry.RelativePath)
                    + "\nLast played: " + (last.HasValue ? last.Value.ToLocalTime().ToString("d") : "Never")
                    + "\nFavorite: " + (state.IsFavorite(entry.RelativePath) ? "Yes" : "No")
                    + (ReadinessDetails == null ? "" : "\n\n" + ReadinessDetails(entry));
            }
        }

        private string DetailLine(TableEntry entry)
        {
            var parts = new List<string>();
            if (entry.Subtitle.Length > 0) parts.Add(entry.Subtitle);
            if (state.IsFavorite(entry.RelativePath)) parts.Add("<color=#FFC940>Favorite</color>");
            int playCount = state.PlayCount(entry.RelativePath);
            parts.Add(playCount == 1 ? "Played 1 time" : $"Played {playCount} times");
            DateTime? last = state.LastPlayed(entry.RelativePath);
            if (last.HasValue) parts.Add("Last played " + RelativeTime.Format(last.Value, DateTime.UtcNow));
            return string.Join(" · ", parts);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
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
            ownedMaterials.Add(material);
            material.SetFloat("_Glossiness", gloss);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void BuildSelectionMarker()
        {
            var marker = new GameObject("SelectionMarker").transform;
            marker.SetParent(transform, false);
            marker.localPosition = ArcLayout.SlotPosition(0f) + new Vector3(0f, 0.012f, 0.5f);
            marker.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Color cyan = new Color(0.18f, 0.55f, 0.65f);
            PanelQuad(marker, "LeftRail", new Vector3(-0.43f, 0f, 0f), new Vector2(0.012f, 1.25f), cyan);
            PanelQuad(marker, "RightRail", new Vector3(0.43f, 0f, 0f), new Vector2(0.012f, 1.25f), cyan);
            PanelQuad(marker, "FrontRail", new Vector3(0f, -0.625f, 0f), new Vector2(0.87f, 0.012f), cyan);
        }

        private void PanelQuad(Transform parent, string name, Vector3 position, Vector2 size, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Collider collider = quad.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var material = new Material(Shader.Find("Standard")) { color = color };
            ownedMaterials.Add(material);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);
            material.SetFloat("_Glossiness", 0f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void HintGroup(Transform plate, string name, float x, string label)
        {
            TextMeshPro hint = PlateLine(plate, name, -0.46f, new Vector2(0.43f, 0.22f));
            hint.transform.localPosition += Vector3.right * x;
            hint.text = label;
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.fontSizeMax = 1.4f;
            hint.color = new Color(0.68f, 0.76f, 0.82f);
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
            foreach (Material material in ownedMaterials)
            {
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
            ownedMaterials.Clear();
            if (videoTexture != null)
            {
                if (Application.isPlaying) Destroy(videoTexture); else DestroyImmediate(videoTexture);
            }
        }
    }
}
