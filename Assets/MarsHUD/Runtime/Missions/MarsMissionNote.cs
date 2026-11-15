using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 5 - Petite note discrete dans la vue principale : prochaine etape du fil rouge, distance et
    /// direction depuis l'astronaute. Canvas monde sans raycaster (jamais interactif, aucun ordre possible),
    /// suivi paresseux du regard en lacet, masque quand le menu des cartes est ouvert.
    /// Pas de camera ni de RenderTexture supplementaire.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(450)]
    public sealed class MarsMissionNote : MonoBehaviour
    {
        public MarsMissionPlan plan;
        public MarsHUDController hud;
        [Header("Placement (relatif au regard horizontal)")]
        [Range(.6f, 3f)] public float distanceMeters = 1.5f;
        [Range(-45f, 45f)] public float yawOffsetDegrees = -20f;
        [Tooltip("Negatif = sous l'horizon.")]
        [Range(-45f, 20f)] public float pitchDegrees = -24f;
        [Range(.15f, .8f)] public float widthMeters = .42f;
        [Range(.5f, 10f)] public float followSharpness = 2.5f;
        [Header("Discretion")]
        [Range(0f, 1f)] public float backgroundAlpha = .32f;
        [Range(.2f, 1f)] public float textAlpha = .8f;
        public bool hideWhenMenuOpen = true;
        [Min(4f)] public float eventDisplaySeconds = 7f;
        [Range(.5f, 10f)] public float textRefreshHz = 2f;

        const float W = 420f, H = 74f;
        RectTransform root;
        CanvasGroup group;
        Text title, detail;
        Font font;
        bool built, snapped;
        float nextText;
        int shownKey = int.MinValue;
        static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };

        void Awake()
        {
            if (!plan) plan = GetComponent<MarsMissionPlan>();
            if (!hud) hud = FindAnyObjectByType<MarsHUDController>();
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Mission note (lot 5)", typeof(RectTransform));
            go.layer = hud.gameObject.layer;
            root = (RectTransform)go.transform;
            root.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var cam = hud.Head ? hud.Head.GetComponent<Camera>() : null;
            canvas.worldCamera = cam ? cam : Camera.main;
            root.sizeDelta = new Vector2(W, H);
            root.localScale = Vector3.one * (widthMeters / W);
            group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;

            go.AddComponent<CanvasRenderer>();
            var plate = go.AddComponent<MarsRoundedPanel>();
            plate.fillColor = new Color(.02f, .045f, .07f, backgroundAlpha);
            plate.borderColor = new Color(1f, .35f, .3f, .35f);
            plate.radius = 10f; plate.borderWidth = 1f; plate.raycastTarget = false;
            plate.SetVerticesDirty();

            var dot = Child("Thread mark", root, new Vector2(6f, 50f), new Vector2(-W * .5f + 12f, 0f));
            dot.gameObject.AddComponent<CanvasRenderer>();
            var img = dot.gameObject.AddComponent<Image>();
            img.color = new Color(1f, .3f, .25f, .75f); img.raycastTarget = false;

            title = Label("Title", new Vector2(W - 36f, 30f), new Vector2(10f, 16f), 18, new Color(.92f, .96f, 1f, textAlpha));
            detail = Label("Detail", new Vector2(W - 36f, 34f), new Vector2(10f, -16f), 14, new Color(.78f, .86f, .9f, textAlpha));
            built = true;
        }

        RectTransform Child(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.gameObject.layer = parent.gameObject.layer;
            r.SetParent(parent, false);
            r.sizeDelta = size; r.anchoredPosition = pos;
            return r;
        }

        Text Label(string name, Vector2 size, Vector2 pos, int fontSize, Color color)
        {
            var r = Child(name, root, size, pos);
            r.gameObject.AddComponent<CanvasRenderer>();
            var t = r.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = fontSize;
            t.raycastTarget = false;
            return t;
        }

        void LateUpdate()
        {
            if (!built) { if (hud && hud.Ready && hud.Head && plan) Build(); if (!built) return; }
            bool visible = plan.isActiveAndEnabled && plan.Steps.Count > 0 &&
                           (!hideWhenMenuOpen || hud.Mode == MarsHUDController.SpatialMode.Passive);
            float target = visible ? 1f : 0f;
            if (group.alpha != target) group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 4f);
            if (group.alpha <= 0f) { snapped = false; return; }
            Follow();
            if (Time.unscaledTime >= nextText) { nextText = Time.unscaledTime + 1f / textRefreshHz; Refresh(); }
        }

        void Follow()
        {
            Transform head = hud.Head;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(head.up, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) return;
            Quaternion look = Quaternion.LookRotation(flat.normalized, Vector3.up) *
                              Quaternion.Euler(-pitchDegrees, yawOffsetDegrees, 0f);
            Vector3 goal = head.position + look * Vector3.forward * distanceMeters;
            float k = snapped ? 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime) : 1f;
            snapped = true;
            Vector3 p = Vector3.Lerp(root.position, goal, k);
            root.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position, Vector3.up));
        }

        void Refresh()
        {
            bool recent = Time.unscaledTime - plan.LastEventTime < eventDisplaySeconds;
            if (!plan.TryGetCurrent(out var cp))
            {
                int k0 = 1 << 30 | (recent ? 1 : 0);
                if (k0 == shownKey) return;
                shownKey = k0;
                title.text = "FIL ROUGE · Mission accomplie";
                detail.text = recent ? plan.LastEvent : "Toutes les étapes sont validées.";
                return;
            }
            Vector3 head = hud.Head.position, c = cp.transform.position;
            float dx = c.x - head.x, dz = c.z - head.z;
            int meters = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            int dir = 0;
            if (hud.Coordinates != null)
            {
                Vector2 d = hud.Coordinates.DirectionToData(new Vector3(dx, 0f, dz));
                float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;     // 0 = nord, sens horaire
                dir = Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8;
            }
            int key = (plan.Version << 20) ^ (meters << 4) ^ (dir << 1) ^ (recent ? 1 : 0);
            if (key == shownKey) return;          // chaines reconstruites seulement si l'affichage change
            shownKey = key;
            title.text = "FIL ROUGE · " + plan.StepTitle(plan.CurrentIndex);
            detail.text = recent ? plan.LastEvent
                : $"{meters} m · {Cardinals[dir]} · {cp.RequirementText()}" + (string.IsNullOrEmpty(cp.instruction) ? "" : " · " + cp.instruction);
        }

        void OnDestroy() { if (root) Destroy(root.gameObject); }
    }
}
