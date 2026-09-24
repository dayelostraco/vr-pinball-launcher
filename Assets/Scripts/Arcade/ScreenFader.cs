using System.Collections;
using TMPro;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// A head-locked black overlay with one line of text, used to fade out before a table
    /// launches ("Loading ...") and to fade the room back in afterwards.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        private const float Distance = 0.35f;
        private Material material;
        private MeshRenderer overlay;
        private TextMeshPro label;

        public float Alpha { get; private set; }
        public string Text => label.text;

        public static ScreenFader Create(Camera camera)
        {
            camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f);

            var go = new GameObject("ScreenFader");
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, Distance);
            var fader = go.AddComponent<ScreenFader>();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Overlay";
            Collider collider = quad.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(3f, 3f, 1f);   // covers a 110 degree view at this distance
            fader.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 5000 };
            fader.overlay = quad.GetComponent<MeshRenderer>();
            fader.overlay.sharedMaterial = fader.material;

            var textObject = new GameObject("Label");
            textObject.transform.SetParent(go.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            fader.label = textObject.AddComponent<TextMeshPro>();
            fader.label.rectTransform.sizeDelta = new Vector2(0.4f, 0.08f);
            fader.label.alignment = TextAlignmentOptions.Center;
            fader.label.enableAutoSizing = true;
            fader.label.fontSizeMin = 0.1f;
            fader.label.fontSizeMax = 2f;
            fader.label.fontMaterial.renderQueue = 5001;   // a per-object copy, so other text is unaffected

            fader.SetImmediate(0f);
            return fader;
        }

        public void SetImmediate(float alpha, string text = null)
        {
            if (text != null) label.text = text;
            Apply(alpha);
        }

        public IEnumerator Fade(float to, float seconds, string text = null)
        {
            if (text != null) label.text = text;
            float from = Alpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            Apply(to);
        }

        private void Apply(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
            material.color = new Color(0f, 0f, 0f, Alpha);
            label.alpha = Alpha;
            overlay.enabled = Alpha > 0.001f;
            label.enabled = Alpha > 0.001f;
        }
    }
}
