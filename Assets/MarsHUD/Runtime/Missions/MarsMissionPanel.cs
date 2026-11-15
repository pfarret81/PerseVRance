using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 3 - Commandes de vehicule ajoutees aux fenetres existantes, sans modifier MarsHUDController.
    /// Navigation et ordre sont separes : un ordre n'est emis que si (1) la carte TOPOGRAPHIE est agrandie
    /// dans le menu, (2) un vehicule est explicitement selectionne, (3) le geste est un toucher bref
    /// (pas un glisser). Le HUD ne deplace aucun vehicule : il transmet la destination a IMarsVehicleMission.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(400)]
    public sealed class MarsMissionPanel : MonoBehaviour
    {
        public MarsHUDController hud;
        public MarsSoilSampler sampler;
        [Tooltip("Composants implementant IMarsVehicleMission (lot 3 : PerseveranceMission ; lot 4 : Ingenuity).")]
        public MonoBehaviour[] vehicles = new MonoBehaviour[0];
        [Range(1f, 20f)] public float textRefreshHz = 5f;
        public Color accent = new Color(.3f, .78f, .95f);
        public Color buttonFill = new Color(.06f, .12f, .17f, .95f);
        public Color warning = new Color(1f, .72f, .35f);

        readonly List<IMarsVehicleMission> list = new List<IMarsVehicleMission>(4);
        public IReadOnlyList<IMarsVehicleMission> Vehicles => list;
        public int SelectedIndex { get; private set; } = -1;
        public IMarsVehicleMission Selected => SelectedIndex >= 0 && SelectedIndex < list.Count ? list[SelectedIndex] : null;

        Font font;
        bool built;
        MarsMapGraphic topoMap;
        RectTransform topoRoot, soilRoot;
        CanvasGroup strip, compactTopo, reportCard, compactSoil;
        Text status, message, compactStatus, report, soilSummary;
        Button pause, resume;
        Button[] vehicleButtons = new Button[0];
        Text[] vehicleLabels = new Text[0];
        string shownMessage = "", lastVehicleMessage;
        bool shownIsWarning;
        int reportIndex = -1, reportVersion = -1, summaryVersion = -1, selectedShown = -2;
        float nextText;
        bool lastTopoExpanded;
        readonly StringBuilder sb = new StringBuilder(512);

        const string Hint = "Touchez brièvement la carte pour désigner la destination. Glisser = déplacer la carte, sans ordre.";
        const string NoSelection = "Sélectionnez un véhicule pour donner un ordre. Sans sélection, la carte reste en navigation.";

        void Awake()
        {
            if (!hud) hud = GetComponent<MarsHUDController>();
            if (!sampler) sampler = GetComponent<MarsSoilSampler>();
            list.Clear();
            if (vehicles != null)
                foreach (var b in vehicles)
                    if (b is IMarsVehicleMission m) list.Add(m);
                    else if (b) Debug.LogWarning($"{b.name} n'implemente pas IMarsVehicleMission : ignore.", this);
        }

        void LateUpdate()
        {
            if (!built) { if (hud && hud.Ready) Build(); if (!built) return; }
            bool topoExpanded = hud.CanNavigate(MarsMapTheme.Topography);
            bool soilExpanded = hud.CanNavigate(MarsMapTheme.Soil);
            if (lastTopoExpanded && !topoExpanded) Select(-1);   // securite : plus de selection hors carte agrandie
            lastTopoExpanded = topoExpanded;
            Show(strip, topoExpanded, true);
            Show(compactTopo, !topoExpanded && list.Count > 0, false);
            Show(reportCard, soilExpanded, true);
            Show(compactSoil, !soilExpanded, false);
            if (Time.unscaledTime >= nextText) { nextText = Time.unscaledTime + 1f / textRefreshHz; RefreshTexts(); }
        }

        static void Show(CanvasGroup g, bool visible, bool interactive)
        {
            if (!g) return;
            float a = visible ? 1f : 0f;
            if (g.alpha != a) g.alpha = a;
            bool i = visible && interactive;
            if (g.interactable != i) g.interactable = i;
            if (g.blocksRaycasts != i) g.blocksRaycasts = i;
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var map in hud.GetComponentsInChildren<MarsMapGraphic>(true))
            {
                var canvas = map.GetComponentInParent<Canvas>(true);
                if (!canvas) continue;
                if (map.Theme == MarsMapTheme.Topography) { topoMap = map; topoRoot = (RectTransform)canvas.transform; }
                else if (map.Theme == MarsMapTheme.Soil) soilRoot = (RectTransform)canvas.transform;
            }
            if (!topoMap || !topoRoot || !soilRoot) { Debug.LogWarning("Fenetres TOPOGRAPHIE/SOL introuvables : panneau de mission inactif.", this); enabled = false; return; }
            topoMap.Tapped += OnTap;

            // --- TOPOGRAPHIE agrandie (viewport 720 x 390 centre en y=65 : haut a y=260)
            strip = Group("Vehicle orders (lot 3)", topoRoot, new Vector2(720f, 116f), new Vector2(0f, 202f), true);
            vehicleButtons = new Button[list.Count]; vehicleLabels = new Text[list.Count];
            for (int v = 0; v < list.Count; v++)
            {
                int index = v;
                vehicleButtons[v] = MakeButton(strip.transform, list[v].VehicleName, new Vector2(200f, 40f), new Vector2(-250f + v * 210f, 30f), list[v].MarkerColor, () => Select(SelectedIndex == index ? -1 : index));
                vehicleLabels[v] = vehicleButtons[v].GetComponentInChildren<Text>();
            }
            pause = MakeButton(strip.transform, "Pause", new Vector2(120f, 40f), new Vector2(170f, 30f), accent, () => Selected?.PauseMission());
            resume = MakeButton(strip.transform, "Reprendre", new Vector2(130f, 40f), new Vector2(295f, 30f), accent, () => Selected?.ResumeMission());
            status = Label("Status", strip.transform, "", new Vector2(696f, 26f), new Vector2(0f, -8f), 18, new Color(.9f, .96f, 1f), TextAnchor.MiddleLeft);
            message = Label("Message", strip.transform, "", new Vector2(696f, 40f), new Vector2(0f, -38f), 15, new Color(.78f, .86f, .9f), TextAnchor.UpperLeft);

            // --- TOPOGRAPHIE compacte (viewport 404 x 240 centre en y=50 : haut a y=170)
            compactTopo = Group("Vehicle status (lot 3)", topoRoot, new Vector2(404f, 30f), new Vector2(0f, 155f), false);
            compactStatus = Label("Status", compactTopo.transform, "", new Vector2(392f, 28f), Vector2.zero, 15, new Color(.9f, .96f, 1f), TextAnchor.MiddleLeft);

            // --- SOL agrandi : fiche de prelevement en haut a gauche de la carte
            reportCard = Group("Soil sample report (lot 3)", soilRoot, new Vector2(340f, 318f), new Vector2(-180f, 92f), true);
            report = Label("Report", reportCard.transform, "", new Vector2(318f, 262f), new Vector2(0f, 20f), 15, new Color(.9f, .96f, 1f), TextAnchor.UpperLeft);
            MakeButton(reportCard.transform, "Préc.", new Vector2(100f, 34f), new Vector2(-60f, -132f), accent, () => Browse(-1));
            MakeButton(reportCard.transform, "Suiv.", new Vector2(100f, 34f), new Vector2(60f, -132f), accent, () => Browse(1));

            // --- SOL compact : resume du dernier prelevement
            compactSoil = Group("Soil sample summary (lot 3)", soilRoot, new Vector2(404f, 46f), new Vector2(0f, 147f), false);
            soilSummary = Label("Summary", compactSoil.transform, "", new Vector2(392f, 44f), Vector2.zero, 14, new Color(.9f, .96f, 1f), TextAnchor.MiddleLeft);

            built = true;
            Select(-1);
            RefreshTexts();
        }

        void OnDestroy() { if (topoMap) topoMap.Tapped -= OnTap; }

        void Select(int index)
        {
            SelectedIndex = index >= 0 && index < list.Count ? index : -1;
            selectedShown = -2;
            SetLocalMessage("", false);
        }

        void OnTap(MarsMapGraphic map, Vector2 data)
        {
            if (map.Theme != MarsMapTheme.Topography || !hud.CanNavigate(MarsMapTheme.Topography)) return;
            var vehicle = Selected;
            if (vehicle == null) { SetLocalMessage(NoSelection, false); return; }   // geste de navigation : aucun ordre
            if (!hud.Catalog.Contains(data)) { SetLocalMessage("Refusé : point hors de l'emprise des cartes.", true); return; }
            if (!hud.Discovery.IsKnown(MarsMapTheme.Topography, data)) { SetLocalMessage("Refusé : zone non cartographiée (inconnue).", true); return; }
            Vector3 world = hud.Coordinates.DataToWorld(data);
            vehicle.RequestDestination(world, out _);
            lastVehicleMessage = null;   // le message du vehicule (accepte/refuse) remplace l'affichage
            RefreshTexts();
        }

        void SetLocalMessage(string text, bool isWarning)
        {
            shownMessage = text; shownIsWarning = isWarning;
            var v = Selected; lastVehicleMessage = v != null ? v.LastMessage : null;
            if (built) ApplyMessage();
        }

        void ApplyMessage()
        {
            string text = string.IsNullOrEmpty(shownMessage) ? (Selected != null ? Hint : NoSelection) : shownMessage;
            if (message.text != text) message.text = text;
            Color c = shownIsWarning ? warning : new Color(.78f, .86f, .9f);
            if (message.color != c) message.color = c;
        }

        void Browse(int delta)
        {
            if (!sampler || sampler.Samples.Count == 0) return;
            reportIndex = Mathf.Clamp(reportIndex + delta, 0, sampler.Samples.Count - 1);
            reportVersion = -1;
            RefreshTexts();
        }

        void RefreshTexts()
        {
            if (!built) return;
            var v = Selected;
            if (v != null && !ReferenceEquals(v.LastMessage, lastVehicleMessage))
            {
                lastVehicleMessage = v.LastMessage;
                if (!string.IsNullOrEmpty(lastVehicleMessage)) { shownMessage = lastVehicleMessage; shownIsWarning = v.LastMessageIsRefusal; }
            }
            ApplyMessage();
            var shownVehicle = v ?? (list.Count > 0 ? list[0] : null);
            string line = shownVehicle == null ? "Aucun véhicule installé" : shownVehicle.VehicleName.ToUpperInvariant() + " : " + shownVehicle.StatusLine;
            if (status.text != line) status.text = line;
            if (compactStatus.text != line) compactStatus.text = line;
            pause.interactable = v != null && v.CanPause;
            resume.interactable = v != null && v.CanResume;
            if (selectedShown != SelectedIndex)
            {
                selectedShown = SelectedIndex;
                for (int i = 0; i < vehicleLabels.Length; i++)
                    vehicleLabels[i].text = (i == SelectedIndex ? "> " : "") + list[i].VehicleName + (i == SelectedIndex ? " (ordre)" : "");
            }
            if (sampler)
            {
                if (summaryVersion != sampler.Version) { summaryVersion = sampler.Version; soilSummary.text = sampler.ShortSummary(); reportIndex = sampler.Samples.Count - 1; reportVersion = -1; }
                if (reportVersion != sampler.Version)
                {
                    reportVersion = sampler.Version;
                    report.text = sampler.Samples.Count == 0
                        ? "Aucun prélèvement.\n\nSélectionnez Perseverance sur la carte TOPOGRAPHIE agrandie, puis touchez une destination : le prélèvement est automatique à l'arrivée.\n\n" + (sampler.Profile ? sampler.Profile.provenance : "")
                        : sampler.BuildReport(reportIndex, sb);
                }
            }
        }

        // ------------------------------------------------------------------ construction UI (style du lot 1)
        RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.sizeDelta = size; r.anchoredPosition = position;
            r.localScale = Vector3.one;
            return r;
        }
        MarsRoundedPanel Plate(RectTransform r, Color fill, Color border, float radius, float width, bool raycast)
        {
            r.gameObject.AddComponent<CanvasRenderer>();
            var g = r.gameObject.AddComponent<MarsRoundedPanel>();
            g.fillColor = fill; g.borderColor = border; g.radius = radius; g.borderWidth = width;
            g.raycastTarget = raycast; g.SetVerticesDirty();
            return g;
        }
        CanvasGroup Group(string name, RectTransform parent, Vector2 size, Vector2 position, bool blocks)
        {
            var r = Rect(name, parent, size, position);
            // Fond opaque bloquant : un toucher sur le bandeau n'atteint jamais la carte en dessous.
            Plate(r, new Color(.02f, .045f, .07f, .88f), new Color(accent.r, accent.g, accent.b, .55f), 12f, 1.2f, blocks);
            var g = r.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f; g.interactable = false; g.blocksRaycasts = false;
            return g;
        }
        Text Label(string name, Transform parent, string text, Vector2 size, Vector2 position, int fontSize, Color color, TextAnchor anchor)
        {
            var r = Rect(name, parent, size, position);
            r.gameObject.AddComponent<CanvasRenderer>();
            var t = r.gameObject.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }
        Button MakeButton(Transform parent, string text, Vector2 size, Vector2 position, Color tint, System.Action action)
        {
            var r = Rect(text, parent, size, position);
            var plate = Plate(r, buttonFill, new Color(tint.r, tint.g, tint.b, .75f), 10f, 1.2f, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = plate;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.2f, 1.25f, 1f);
            colors.pressedColor = new Color(.65f, .85f, .95f, 1f);
            colors.disabledColor = new Color(.55f, .55f, .55f, .45f);
            colors.fadeDuration = .08f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => action());
            Label("Label", r, text, size, Vector2.zero, 17, new Color(.9f, .96f, 1f), TextAnchor.MiddleCenter);
            return b;
        }
    }
}
