using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Construction d'UI monde dans le style du HUD (MarsRoundedPanel, police LegacyRuntime).
    /// Utilise par le menu principal, le quick menu et le menu en jeu. Aucune allocation par image :
    /// tout est cree une fois a l'initialisation.
    /// </summary>
    public static class MarsUiKit
    {
        public static readonly Color Accent = new Color(1f, .48f, .22f);
        public static readonly Color Cyan = new Color(.35f, .78f, .95f);
        public static readonly Color PanelFill = new Color(.02f, .045f, .07f, .92f);
        public static readonly Color ButtonFill = new Color(.06f, .11f, .16f, .95f);
        public static readonly Color TextMain = new Color(.92f, .96f, 1f);
        public static readonly Color TextDim = new Color(.62f, .72f, .80f);

        static Font font;
        public static Font Font
        {
            get { if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return font; }
        }

        static AudioSource clickSource;
        /// <summary>Son de clic UI facultatif (assigne par MarsMusicPlayer.uiClick). Volume = canal Effets.</summary>
        public static AudioClip ClickClip;

        public static void PlayClick()
        {
            if (!ClickClip) return;
            if (!clickSource)
            {
                var go = new GameObject("MarsUi click audio");
                clickSource = go.AddComponent<AudioSource>();
                clickSource.playOnAwake = false; clickSource.spatialBlend = 0f;
                clickSource.ignoreListenerPause = true;
            }
            clickSource.PlayOneShot(ClickClip, MarsAudioSettings.Effects);
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.sizeDelta = size; r.anchoredPosition = position;
            r.localScale = Vector3.one;
            return r;
        }

        /// <summary>Canvas monde avec TrackedDeviceGraphicRaycaster (meme principe que le HUD).</summary>
        public static RectTransform WorldCanvas(string name, Transform parent, Vector2 size, float metersPerPixel, Camera eventCamera, int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.sizeDelta = size;
            r.localScale = Vector3.one * metersPerPixel;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = eventCamera ? eventCamera : Camera.main;
            c.sortingOrder = sortingOrder;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            return r;
        }

        public static MarsRoundedPanel Plate(RectTransform r, Color fill, Color border, float radius, float width, bool raycast)
        {
            if (!r.GetComponent<CanvasRenderer>()) r.gameObject.AddComponent<CanvasRenderer>();
            var g = r.gameObject.AddComponent<MarsRoundedPanel>();
            g.fillColor = fill; g.borderColor = border; g.radius = radius; g.borderWidth = width;
            g.raycastTarget = raycast; g.SetVerticesDirty();
            return g;
        }

        public static MarsRoundedPanel Panel(string name, Transform parent, Vector2 size, Vector2 position, Color border, bool blocks = true)
        {
            var r = Rect(name, parent, size, position);
            return Plate(r, PanelFill, new Color(border.r, border.g, border.b, .6f), 16f, 1.5f, blocks);
        }

        public static Text Label(string name, Transform parent, string text, Vector2 size, Vector2 position, int fontSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var r = Rect(name, parent, size, position);
            r.gameObject.AddComponent<CanvasRenderer>();
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = anchor; t.fontStyle = style;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, Vector2 size, Vector2 position, Color tint, System.Action action, int fontSize = 20)
        {
            var r = Rect(text, parent, size, position);
            var plate = Plate(r, ButtonFill, new Color(tint.r, tint.g, tint.b, .8f), Mathf.Min(12f, size.y * .5f), 1.5f, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = plate;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.25f, 1.3f, 1f);
            colors.pressedColor = new Color(.65f, .85f, .95f, 1f);
            colors.disabledColor = new Color(.55f, .55f, .55f, .45f);
            colors.fadeDuration = .08f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { PlayClick(); action?.Invoke(); });
            Label("Label", r, text, size - new Vector2(8f, 0f), Vector2.zero, fontSize, TextMain);
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b ? b.GetComponentInChildren<Text>() : null;
            if (t && t.text != text) t.text = text;
        }

        public static CanvasGroup Group(GameObject go)
        {
            var g = go.GetComponent<CanvasGroup>();
            if (!g) g = go.AddComponent<CanvasGroup>();
            return g;
        }

        public static void Show(CanvasGroup g, bool visible)
        {
            if (!g) return;
            g.alpha = visible ? 1f : 0f; g.interactable = visible; g.blocksRaycasts = visible;
        }
    }
}
