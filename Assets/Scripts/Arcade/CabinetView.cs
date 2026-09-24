using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// One arcade cabinet built from primitives: legs, body, tilted playfield, backbox with
    /// backglass, and the wheel on the front apron. Local frame: origin on the floor at the
    /// front centre, +Z away from the player, +Y up. Art is unlit (screens), the body is lit.
    /// Missing media falls back so every cabinet still looks finished: no playfield shows a
    /// dark playfield with the wheel as a decal, no backglass shows the wheel on the backbox,
    /// and no wheel shows the title on the apron.
    /// </summary>
    public sealed class CabinetView : MonoBehaviour
    {
        public static readonly Vector3 PlayfieldCenter = new Vector3(0f, 1.31f, 0.56f);
        public static readonly Quaternion PlayfieldRotation = Quaternion.Euler(55f, 0f, -90f);
        private static readonly Vector3 PlayfieldSize = new Vector3(1.25f, 0.70f, 1f);
        private static readonly Vector3 BackglassCenter = new Vector3(0f, 1.95f, 1.075f);
        private static readonly Vector3 BackglassSize = new Vector3(0.68f, 0.3825f, 1f);
        private static readonly Vector3 BackglassWheelSize = new Vector3(0.38f, 0.38f, 1f);
        private static readonly Vector3 ApronCenter = new Vector3(0f, 0.66f, -0.005f);
        private const float ApronWheelSize = 0.30f;
        private const float DecalSize = 0.40f;
        private const float PulseSeconds = 0.4f;
        public const float DimTint = 0.55f;

        private static Material bodyMaterial;
        private static Texture2D darkTexture;

        private MediaCache cache;
        private Material playfieldMaterial;
        private Material backglassMaterial;
        private Material wheelMaterial;
        private Material decalMaterial;
        private Transform backglass;
        private GameObject apronWheel;
        private GameObject decal;
        private TextMeshPro marquee;
        private TextMeshPro placeholder;
        private Texture stillPlayfield;
        private bool showingVideo;
        private bool focused = true;
        private int version;
        private Coroutine pulse;

        public TableEntry Entry { get; private set; }

        public Texture PlayfieldTexture => playfieldMaterial.mainTexture;
        public Texture BackglassTexture => backglassMaterial.mainTexture;
        public bool WheelDecalVisible => decal.activeSelf;
        public bool ApronWheelVisible => apronWheel.activeSelf;
        public string MarqueeText => marquee.gameObject.activeSelf ? marquee.text : null;
        public string PlaceholderText => placeholder.gameObject.activeSelf ? placeholder.text : null;

        /// <summary>A 1x1 near-black texture for blank screens (Sprites/Default renders white without one).</summary>
        public static Texture2D DarkTexture
        {
            get
            {
                if (darkTexture == null)
                {
                    darkTexture = new Texture2D(1, 1) { name = "CabinetDark" };
                    darkTexture.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.07f));
                    darkTexture.Apply();
                }
                return darkTexture;
            }
        }

        private static Material BodyMaterial
        {
            get
            {
                if (bodyMaterial == null)
                {
                    bodyMaterial = new Material(Shader.Find("Standard")) { name = "CabinetBody", color = new Color(0.09f, 0.09f, 0.11f) };
                    bodyMaterial.SetFloat("_Metallic", 0.4f);
                    bodyMaterial.SetFloat("_Glossiness", 0.55f);
                }
                return bodyMaterial;
            }
        }

        public static CabinetView Create(Transform parent, MediaCache cache)
        {
            var go = new GameObject("Cabinet");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CabinetView>();
            view.cache = cache;
            view.Build();
            view.SetEntry(null);
            return view;
        }

        private void Build()
        {
            Material body = BodyMaterial;
            foreach (float x in new[] { -0.32f, 0.32f })
            {
                foreach (float z in new[] { 0.06f, 1.04f })
                {
                    Box("Leg", new Vector3(x, 0.18f, z), new Vector3(0.06f, 0.36f, 0.06f), body);
                }
            }
            Box("Body", new Vector3(0f, 0.66f, 0.55f), new Vector3(0.72f, 0.60f, 1.10f), body);
            Box("HeadSupport", new Vector3(0f, 1.295f, 1.0f), new Vector3(0.72f, 0.67f, 0.20f), body);
            Box("LockdownBar", new Vector3(0f, 0.97f, 0.02f), new Vector3(0.72f, 0.05f, 0.08f), body);
            Box("Backbox", new Vector3(0f, 1.95f, 1.20f), new Vector3(0.74f, 0.64f, 0.24f), body);

            playfieldMaterial = ArtMaterial("Playfield");
            Quad("Playfield", PlayfieldCenter, PlayfieldRotation, PlayfieldSize, playfieldMaterial);

            decalMaterial = ArtMaterial("WheelDecal");
            decalMaterial.renderQueue += 1;   // always drawn over the playfield it sits on
            Vector3 normal = PlayfieldRotation * Vector3.back;
            decal = Quad("WheelDecal", PlayfieldCenter + normal * 0.005f, Quaternion.Euler(55f, 0f, 0f),
                         new Vector3(DecalSize, DecalSize, 1f), decalMaterial).gameObject;

            backglassMaterial = ArtMaterial("Backglass");
            backglass = Quad("Backglass", BackglassCenter, Quaternion.identity, BackglassSize, backglassMaterial);

            wheelMaterial = ArtMaterial("ApronWheel");
            apronWheel = Quad("ApronWheel", ApronCenter, Quaternion.identity,
                              new Vector3(ApronWheelSize, ApronWheelSize, 1f), wheelMaterial).gameObject;

            marquee = Label("Marquee", ApronCenter + new Vector3(0f, 0f, -0.005f), new Vector2(0.66f, 0.28f));
            placeholder = Label("Placeholder", BackglassCenter + new Vector3(0f, 0f, -0.01f), new Vector2(0.66f, 0.36f));
        }

        /// <summary>Shows a table's art, or clears the cabinet when <paramref name="entry"/> is null.</summary>
        public void SetEntry(TableEntry entry)
        {
            version++;
            Entry = entry;
            showingVideo = false;
            stillPlayfield = DarkTexture;
            playfieldMaterial.mainTexture = DarkTexture;
            backglassMaterial.mainTexture = DarkTexture;
            backglass.localScale = BackglassSize;
            decal.SetActive(false);
            apronWheel.SetActive(false);
            marquee.gameObject.SetActive(false);
            placeholder.gameObject.SetActive(false);

            if (entry == null) return;

            MediaSet media = entry.Media;
            int requested = version;

            if (media.Wheel == null)
            {
                ShowTitle(entry.Title);
            }
            else
            {
                Load(media.Wheel, requested, wheel =>
                {
                    if (wheel == null)
                    {
                        ShowTitle(entry.Title);
                        return;
                    }
                    wheelMaterial.mainTexture = wheel;
                    apronWheel.SetActive(true);
                    if (media.Playfield == null)
                    {
                        decalMaterial.mainTexture = wheel;
                        decal.SetActive(true);
                    }
                    if (media.Backglass == null)
                    {
                        backglassMaterial.mainTexture = wheel;
                        backglass.localScale = BackglassWheelSize;
                    }
                });
            }

            if (media.Playfield != null)
            {
                Load(media.Playfield, requested, still =>
                {
                    if (still == null) return;
                    stillPlayfield = still;
                    if (!showingVideo) playfieldMaterial.mainTexture = still;
                });
            }

            if (media.Backglass != null)
            {
                Load(media.Backglass, requested, art =>
                {
                    if (art != null) backglassMaterial.mainTexture = art;
                });
            }
        }

        /// <summary>An empty cabinet with a message on the backbox, e.g. "No favorites yet".</summary>
        public void SetPlaceholder(string message)
        {
            SetEntry(null);
            placeholder.text = message;
            placeholder.gameObject.SetActive(true);
        }

        public void SetFocused(bool isFocused)
        {
            focused = isFocused;
            ApplyTint(focused ? 1f : DimTint);
        }

        public void ShowVideo(Texture texture)
        {
            showingVideo = true;
            playfieldMaterial.mainTexture = texture;
        }

        public void ShowStill()
        {
            showingVideo = false;
            playfieldMaterial.mainTexture = stillPlayfield;
        }

        /// <summary>A brief brightening, used when the table is launched.</summary>
        public void Pulse()
        {
            if (!Application.isPlaying) return;
            if (pulse != null) StopCoroutine(pulse);
            pulse = StartCoroutine(PulseRoutine());
        }

        private IEnumerator PulseRoutine()
        {
            for (float t = 0f; t < PulseSeconds; t += Time.unscaledDeltaTime)
            {
                ApplyTint(1f + 0.6f * Mathf.Sin(Mathf.PI * t / PulseSeconds));
                yield return null;
            }
            ApplyTint(focused ? 1f : DimTint);
            pulse = null;
        }

        private void ShowTitle(string title)
        {
            marquee.text = title;
            marquee.gameObject.SetActive(true);
        }

        private void Load(string path, int requested, Action<Texture2D> apply)
        {
            cache.Request(path, texture =>
            {
                // Ignore loads that finish after the cabinet moved on to another table.
                if (this != null && requested == version) apply(texture);
            });
        }

        private void ApplyTint(float brightness)
        {
            var tint = new Color(brightness, brightness, brightness, 1f);
            playfieldMaterial.color = tint;
            backglassMaterial.color = tint;
            wheelMaterial.color = tint;
            decalMaterial.color = tint;
        }

        private static Material ArtMaterial(string name) =>
            new Material(Shader.Find("Sprites/Default")) { name = name, mainTexture = DarkTexture };

        private Transform Box(string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            return Place(go, name, position, Quaternion.identity, size, material);
        }

        private Transform Quad(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            return Place(go, name, position, rotation, size, material);
        }

        private Transform Place(GameObject go, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            go.name = name;
            RemoveCollider(go);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private TextMeshPro Label(string name, Vector3 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.5f;
            text.fontSizeMax = 6f;
            text.color = Color.white;
            go.SetActive(false);
            return text;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        private void OnDestroy()
        {
            foreach (Material material in new[] { playfieldMaterial, backglassMaterial, wheelMaterial, decalMaterial })
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
