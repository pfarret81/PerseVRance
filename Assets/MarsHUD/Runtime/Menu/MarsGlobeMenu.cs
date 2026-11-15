using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Menu principal PerseVRance : globe de Mars rotatif (glisser du rayon XR UI, inertie, fleches),
    /// marqueurs des missions a leur latitude/longitude, fenetre de description, quick menu au poignet gauche,
    /// titre anime, champ d'etoiles statique et fondu vers la scene de mission.
    ///
    /// Repere : ce transform est le CENTRE du globe ; son +Z pointe a l'oppose du visiteur (le visiteur est du cote -Z).
    /// Le pivot (enfant) porte la rotation ; le modele texture de l'utilisateur est un enfant du pivot dont la rotation
    /// locale sert de calibration (pole nord sur +Y du pivot, longitude 0 face au visiteur quand le pivot est a l'identite).
    /// L'UI est construite a l'execution (comme le HUD) ; les reperes des missions sont visibles en mode edition (Gizmos).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsGlobeMenu : MonoBehaviour
    {
        [Header("Donnees")]
        public MarsMissionCatalog catalog;
        [Header("Globe")]
        [Tooltip("Pivot de rotation (enfant). Cree automatiquement s'il manque.")]
        public Transform pivot;
        [Tooltip("Modele texture de Mars, enfant du pivot. Sa rotation locale = calibration.")]
        public Transform globeModel;
        [Min(.1f)] public float radius = 1f;
        [Tooltip("Decalage de longitude ajoute a toutes les missions (calibration fine sans tourner le modele).")]
        public float longitudeOffsetDegrees;
        [Range(0f, 45f)] public float maxPitchDegrees = 35f;
        [Header("Interaction")]
        public float idleSpinDegreesPerSecond = 6f;
        [Min(0f)] public float inertiaDamping = 2.5f;
        [Min(1f)] public float alignSeconds = .8f;
        [Header("Presentation")]
        public string title = "PerseVRance";
        public string subtitle = "Simulation de sortie extrav\u00e9hiculaire sur Mars";
        public bool starfield = true;
        [Min(100)] public int starCount = 1800;
        [Header("Quick menu (poignet gauche)")]
        [Tooltip("Manette ou main gauche. Recherche par nom si vide.")]
        public Transform leftHand;
        public Vector3 quickMenuLocalPosition = new Vector3(0f, .055f, .02f);
        public Vector3 quickMenuLocalEuler = new Vector3(60f, 0f, 0f);
        [Header("Placement")]
        [Tooltip("Au demarrage (des que le casque est suivi) et apres un recentrage Oculus, le menu se place devant le regard.")]
        public bool placeInFrontOfHead = true;
        [Min(1f)] public float viewDistance = 3.2f;
        [Tooltip("Hauteur du centre du globe par rapport aux yeux (m).")]
        public float heightFromEyes = -.1f;
        [Header("Liens")]
        public MarsPauseMenu pauseMenu;
        public Transform head;

        // --- etat
        MarsMissionCatalog runtimeCatalog;
        float yaw, pitch, yawVelocity, pitchVelocity, targetYaw, targetPitch, alignTime = -1f, lastTouch = -100f, introTime;
        int selected = -1;
        bool dragging, interacted;
        RectTransform markerCanvas, infoCanvas, titleCanvas, sideCanvas;
        CanvasGroup titleGroup, infoGroup, hintGroup;
        RectTransform[] markers = new RectTransform[0];
        Text[] markerLabels = new Text[0];
        MarsRoundedPanel[] markerDots = new MarsRoundedPanel[0];
        Vector3[] localDirs = new Vector3[0];
        bool[] markerVisible = new bool[0];
        Text infoKicker, infoTitle, infoSite, infoDesc, infoObjectives, infoMeta, infoStatus;
        Button startButton;
        MarsWristQuickMenu quick;
        bool placed;
        Vector3 lastHeadLocalPos;
        float lastHeadLocalYaw;

        public MarsMissionCatalog Catalog => catalog ? catalog : runtimeCatalog;
        public int MissionCount => Catalog ? Catalog.missions.Count : 0;
        public int Selected => selected;
        public MarsMissionCatalog.Mission MissionAt(int i) => Catalog && i >= 0 && i < Catalog.missions.Count ? Catalog.missions[i] : null;
        public bool InfoOpen => infoGroup && infoGroup.alpha > .5f;

        /// <summary>Direction unitaire d'une mission dans le repere du pivot (Nord = +Y, longitude 0 vers -Z).</summary>
        public Vector3 LocalDirection(float latitude, float longitudeEast) =>
            Quaternion.Euler(latitude, -(longitudeEast + longitudeOffsetDegrees), 0f) * Vector3.back;

        void Awake()
        {
            if (!catalog) { runtimeCatalog = ScriptableObject.CreateInstance<MarsMissionCatalog>(); runtimeCatalog.FillDefaults(""); }
            if (!head && Camera.main) head = Camera.main.transform;
            if (!pivot)
            {
                pivot = transform.Find("Globe pivot");
                if (!pivot) { pivot = new GameObject("Globe pivot").transform; pivot.SetParent(transform, false); }
            }
            if (!pauseMenu) pauseMenu = FindAnyObjectByType<MarsPauseMenu>();
            pivot.localPosition = Vector3.zero; pivot.localScale = Vector3.one;
            yaw = 20f; pitch = -8f;
            ApplyRotation();
            Camera cam = head ? head.GetComponent<Camera>() : Camera.main;
            BuildTitle(cam);
            BuildMarkers(cam);
            BuildInfo(cam);
            BuildSide(cam);
            if (starfield) BuildStars();
            quick = new GameObject("PerseVRance quick menu").AddComponent<MarsWristQuickMenu>();
            quick.transform.SetParent(transform, false);
            quick.Init(this, cam, ResolveLeftHand(), quickMenuLocalPosition, quickMenuLocalEuler);
            MarsAudioSettings.Load();
        }

        void Start()
        {
            MarsSceneFader.FadeIn(1.5f);
            introTime = Time.unscaledTime;
            // Diagnostic unique : etat reel du menu au demarrage (Console).
            var first = MissionAt(0);
            Debug.Log($"[PerseVRance] Menu : modele={(globeModel ? globeModel.name : "AUCUN")}" +
                      $" (enfant du pivot : {(globeModel && globeModel.IsChildOf(pivot))}), catalogue={(catalog ? catalog.name : "par defaut")}," +
                      $" missions={MissionCount}, main gauche={(leftHand ? leftHand.name : "introuvable")}," +
                      $" scene de la 1re mission=\"{(first != null ? ResolveScene(first) : "")}\".", this);
            int shown = 0, total = 0;
            if (globeModel) foreach (var r in globeModel.GetComponentsInChildren<Renderer>(true)) { total++; if (r.enabled && r.gameObject.activeInHierarchy) shown++; }
            if (!globeModel || shown == 0)
                Debug.LogWarning($"[PerseVRance] Modele de Mars sans rendu actif ({shown}/{total} Renderer). Assignez globeModel ou relancez l'outil Lot 6.", this);
            if (globeModel && globeModel.gameObject.isStatic)
                Debug.LogWarning("[PerseVRance] Le modele de Mars est marque Static : il ne peut pas tourner. Decochez Static.", globeModel);
        }

        /// <summary>
        /// Scene a charger pour une mission : celle du catalogue si elle est dans les Build Settings, sinon (mission
        /// disponible sans scene valide) la premiere scene des Build Settings autre que le menu.
        /// </summary>
        public string ResolveScene(MarsMissionCatalog.Mission m)
        {
            if (m == null || !m.available) return "";
            if (!string.IsNullOrEmpty(m.sceneName) && Application.CanStreamedLevelBeLoaded(m.sceneName)) return m.sceneName;
            string menuScene = gameObject.scene.name;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name) && name != menuScene) return name;
            }
            return "";
        }

        public bool CanStart(int index) => !string.IsNullOrEmpty(ResolveScene(MissionAt(index))) && !MarsSceneFader.Busy;

        /// <summary>Lance la mission (fiche ou quick menu).</summary>
        public bool StartMission(int index, out string why)
        {
            why = "";
            var m = MissionAt(index);
            if (m == null) { why = "Aucune mission s\u00e9lectionn\u00e9e."; return false; }
            if (!m.available) { why = "Mission pas encore disponible."; return false; }
            string scene = ResolveScene(m);
            if (string.IsNullOrEmpty(scene)) { why = "Aucune sc\u00e8ne de mission dans les Build Settings."; return false; }
            return MarsSceneFader.LoadScene(scene, out why);
        }

        Transform ResolveLeftHand()
        {
            if (leftHand) return leftHand;
            string[] names = { "Left Controller", "LeftHand Controller", "Left Hand Controller", "LeftHand", "Left Hand" };
            Transform root = head;
            while (root && root.parent) root = root.parent;
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var n in names)
                    if (t.name == n) return leftHand = t;
            return null;
        }

        // ------------------------------------------------------------------ construction
        void BuildTitle(Camera cam)
        {
            titleCanvas = MarsUiKit.WorldCanvas("PerseVRance title", transform, new Vector2(1600f, 420f), .0011f, cam);
            titleCanvas.localPosition = new Vector3(0f, radius + .55f, -radius * .3f);
            titleGroup = MarsUiKit.Group(titleCanvas.gameObject);
            titleGroup.blocksRaycasts = false; titleGroup.alpha = 0f;
            string t = title.Replace("VR", "<color=#FF7A38>VR</color>");
            MarsUiKit.Label("Title", titleCanvas, t, new Vector2(1600f, 260f), new Vector2(0f, 60f), 200, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            MarsUiKit.Label("Subtitle", titleCanvas, subtitle, new Vector2(1600f, 80f), new Vector2(0f, -110f), 54, MarsUiKit.TextDim);
            var hint = MarsUiKit.Rect("Hint", titleCanvas, new Vector2(1500f, 70f), new Vector2(0f, -185f));
            hintGroup = MarsUiKit.Group(hint.gameObject); hintGroup.alpha = 0f; hintGroup.blocksRaycasts = false;
            MarsUiKit.Label("Text", hint, "Faites glisser la plan\u00e8te avec le rayon, ou parcourez les missions sur votre poignet gauche",
                new Vector2(1500f, 70f), Vector2.zero, 36, MarsUiKit.Cyan);
        }

        void BuildMarkers(Camera cam)
        {
            float px = .001f;
            markerCanvas = MarsUiKit.WorldCanvas("Globe markers", transform, new Vector2(2400f, 2400f) * radius, px, cam, 1);
            // Surface de glisser : premier enfant (moins prioritaire au raycast que les marqueurs ajoutes ensuite).
            var drag = MarsUiKit.Rect("Drag surface", markerCanvas, new Vector2(2300f, 2300f) * radius, Vector2.zero);
            drag.gameObject.AddComponent<CanvasRenderer>();
            var surface = drag.gameObject.AddComponent<MarsGlobeDragSurface>();
            surface.Host = this; surface.color = new Color(0f, 0f, 0f, 0f);

            var list = Catalog ? Catalog.missions : null;
            int n = list != null ? list.Count : 0;
            markers = new RectTransform[n]; markerLabels = new Text[n]; markerDots = new MarsRoundedPanel[n];
            localDirs = new Vector3[n]; markerVisible = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var m = list[i];
                localDirs[i] = LocalDirection(m.latitude, m.longitudeEast);
                var r = MarsUiKit.Rect("Marker " + m.id, markerCanvas, new Vector2(64f, 64f), Vector2.zero);
                Color c = m.available ? m.color : new Color(.6f, .7f, .8f);
                var dot = MarsUiKit.Plate(r, new Color(c.r, c.g, c.b, .9f), Color.white, 32f, 4f, true);
                var b = r.gameObject.AddComponent<Button>();
                b.targetGraphic = dot; b.navigation = new Navigation { mode = Navigation.Mode.None };
                int index = i;
                b.onClick.AddListener(() => { MarsUiKit.PlayClick(); Select(index, true); });
                var label = MarsUiKit.Label("Label", r, m.available ? m.title : m.title + "  \u00b7 bient\u00f4t", new Vector2(520f, 50f),
                    new Vector2(300f, 0f), 30, m.available ? Color.white : MarsUiKit.TextDim, TextAnchor.MiddleLeft, m.available ? FontStyle.Bold : FontStyle.Normal);
                var outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, .85f); outline.effectDistance = new Vector2(2f, -2f);
                markers[i] = r; markerLabels[i] = label; markerDots[i] = dot; markerVisible[i] = true;
            }
        }

        void BuildInfo(Camera cam)
        {
            infoCanvas = MarsUiKit.WorldCanvas("Mission info", transform, new Vector2(620f, 700f), .0013f, cam, 2);
            infoCanvas.localPosition = new Vector3(radius + .55f, -.05f, -radius * 1.1f);
            infoCanvas.localRotation = Quaternion.Euler(0f, 35f, 0f);
            infoGroup = MarsUiKit.Group(infoCanvas.gameObject);
            MarsUiKit.Show(infoGroup, false);
            var panel = MarsUiKit.Panel("Panel", infoCanvas, new Vector2(620f, 700f), Vector2.zero, MarsUiKit.Accent).rectTransform;
            infoKicker = MarsUiKit.Label("Kicker", panel, "MISSION", new Vector2(560f, 30f), new Vector2(0f, 310f), 20, MarsUiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            infoTitle = MarsUiKit.Label("Title", panel, "", new Vector2(560f, 80f), new Vector2(0f, 260f), 34, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            infoSite = MarsUiKit.Label("Site", panel, "", new Vector2(560f, 56f), new Vector2(0f, 200f), 20, MarsUiKit.TextDim, TextAnchor.UpperLeft);
            infoDesc = MarsUiKit.Label("Description", panel, "", new Vector2(560f, 170f), new Vector2(0f, 85f), 22, MarsUiKit.TextMain, TextAnchor.UpperLeft);
            MarsUiKit.Label("ObjectivesTitle", panel, "OBJECTIFS", new Vector2(560f, 26f), new Vector2(0f, -15f), 18, MarsUiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            infoObjectives = MarsUiKit.Label("Objectives", panel, "", new Vector2(560f, 110f), new Vector2(0f, -85f), 21, MarsUiKit.TextMain, TextAnchor.UpperLeft);
            infoMeta = MarsUiKit.Label("Meta", panel, "", new Vector2(560f, 30f), new Vector2(0f, -160f), 20, MarsUiKit.TextDim, TextAnchor.MiddleLeft);
            infoStatus = MarsUiKit.Label("Status", panel, "", new Vector2(560f, 50f), new Vector2(0f, -205f), 19, new Color(1f, .75f, .45f), TextAnchor.MiddleCenter);
            startButton = MarsUiKit.Button(panel, "Commencer la mission", new Vector2(360f, 70f), new Vector2(-85f, -290f), MarsUiKit.Accent, StartSelected, 24);
            MarsUiKit.Button(panel, "Fermer", new Vector2(160f, 70f), new Vector2(200f, -290f), MarsUiKit.Cyan, CloseInfo, 22);
        }

        void BuildSide(Camera cam)
        {
            sideCanvas = MarsUiKit.WorldCanvas("Menu actions", transform, new Vector2(320f, 480f), .0013f, cam, 2);
            sideCanvas.localPosition = new Vector3(-(radius + .75f), -.25f, -radius * .45f);
            sideCanvas.localRotation = Quaternion.Euler(0f, -28f, 0f);
            var panel = MarsUiKit.Panel("Panel", sideCanvas, new Vector2(320f, 480f), Vector2.zero, MarsUiKit.Cyan).rectTransform;
            MarsUiKit.Label("Head", panel, "MENU", new Vector2(280f, 30f), new Vector2(0f, 210f), 20, MarsUiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            MarsUiKit.Button(panel, "< Tourner", new Vector2(130f, 60f), new Vector2(-70f, 130f), MarsUiKit.Cyan, () => Nudge(-40f));
            MarsUiKit.Button(panel, "Tourner >", new Vector2(130f, 60f), new Vector2(70f, 130f), MarsUiKit.Cyan, () => Nudge(40f));
            MarsUiKit.Button(panel, "< Mission", new Vector2(130f, 60f), new Vector2(-70f, 70f), MarsUiKit.Cyan, () => Step(-1, true));
            MarsUiKit.Button(panel, "Mission >", new Vector2(130f, 60f), new Vector2(70f, 70f), MarsUiKit.Cyan, () => Step(1, true));
            MarsUiKit.Button(panel, "Options", new Vector2(270f, 60f), new Vector2(0f, -10f), MarsUiKit.Cyan, OpenOptions);
            MarsUiKit.Button(panel, "Recentrer", new Vector2(270f, 60f), new Vector2(0f, -90f), MarsUiKit.Cyan, Recenter);
            MarsUiKit.Button(panel, "Quitter l'application", new Vector2(270f, 60f), new Vector2(0f, -170f), new Color(1f, .45f, .4f), QuitApp);
        }

        void BuildStars()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Particles/Standard Unlit");
            if (!sh) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (!sh) return;                                           // pas de shader de particules : pas d'etoiles
            var go = new GameObject("Starfield");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = starCount;
            main.startLifetime = 1e6f; main.startSpeed = 0f; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            var rng = new System.Random(7);
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < starCount; i++)
            {
                Vector3 d = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                if (d.sqrMagnitude < .01f || d.sqrMagnitude > 1f) { i--; continue; }
                p.position = d.normalized * 70f;
                float b = .35f + .65f * (float)rng.NextDouble();
                p.startColor = new Color(b, b, b * (.9f + .1f * (float)rng.NextDouble()), 1f);
                p.startSize = .08f + .22f * (float)rng.NextDouble() * (float)rng.NextDouble();
                p.startLifetime = 1e6f;
                ps.Emit(p, 1);
            }
            ps.Pause();                                                // statique : aucune simulation par image
        }

        // ------------------------------------------------------------------ interaction
        public void OnGlobeTouched() { lastTouch = Time.unscaledTime; interacted = true; alignTime = -1f; yawVelocity = pitchVelocity = 0f; dragging = true; }
        public void OnGlobeDrag(Vector2 deltaMeters, float dt)
        {
            float k = Mathf.Rad2Deg / radius;
            float dy = -deltaMeters.x * k, dp = deltaMeters.y * k;
            yaw += dy; pitch = Mathf.Clamp(pitch + dp, -maxPitchDegrees, maxPitchDegrees);
            if (dt > 1e-4f) { yawVelocity = Mathf.Lerp(yawVelocity, dy / dt, .5f); pitchVelocity = Mathf.Lerp(pitchVelocity, dp / dt, .5f); }
            lastTouch = Time.unscaledTime; dragging = true;
        }
        public void OnGlobeReleased() { dragging = false; lastTouch = Time.unscaledTime; }

        /// <summary>Rotation par bouton (alternative au glisser) : anime le globe de deltaDegrees vers la droite.</summary>
        public void Nudge(float deltaDegrees)
        {
            interacted = true; lastTouch = Time.unscaledTime; dragging = false;
            targetYaw = (alignTime >= 0f ? targetYaw : yaw) - deltaDegrees;
            targetPitch = pitch;
            alignTime = 0f; yawVelocity = pitchVelocity = 0f;
        }

        public void Step(int delta, bool openInfo)
        {
            int n = MissionCount; if (n == 0) return;
            int i = selected < 0 ? (delta > 0 ? 0 : n - 1) : (selected + delta + n) % n;
            Select(i, openInfo);
        }

        /// <summary>Selectionne une mission : le globe tourne pour l'amener face au visiteur ; ouvre la fenetre si demande.</summary>
        public void Select(int index, bool openInfo)
        {
            var m = MissionAt(index); if (m == null) return;
            selected = index; interacted = true; lastTouch = Time.unscaledTime;
            targetYaw = m.longitudeEast + longitudeOffsetDegrees;
            targetPitch = Mathf.Clamp(-m.latitude, -maxPitchDegrees, maxPitchDegrees);
            targetYaw = yaw + Mathf.DeltaAngle(yaw, targetYaw);
            alignTime = 0f; yawVelocity = pitchVelocity = 0f; dragging = false;
            if (openInfo) OpenInfo(index);
            else if (InfoOpen) OpenInfo(index);
            if (quick) quick.Refresh();
        }

        public void OpenInfo(int index)
        {
            var m = MissionAt(index); if (m == null) return;
            if (selected != index) { Select(index, true); return; }
            infoKicker.text = m.available ? "MISSION DISPONIBLE" : "MISSION \u00c0 VENIR";
            infoKicker.color = m.available ? MarsUiKit.Accent : MarsUiKit.TextDim;
            infoTitle.text = m.title;
            infoSite.text = m.site + "\n" + MarsMissionCatalog.Coordinates(m);
            infoDesc.text = m.description;
            infoObjectives.text = m.objectives;
            infoMeta.text = "Dur\u00e9e : " + (string.IsNullOrEmpty(m.duration) ? "\u2014" : m.duration) + "    Difficult\u00e9 : " + MarsMissionCatalog.DifficultyLabel(m.difficulty);
            bool loadable = CanStart(index);
            startButton.interactable = loadable;
            MarsUiKit.SetButtonText(startButton, m.available ? "Commencer la mission" : "Bient\u00f4t disponible");
            infoStatus.text = !m.available ? "Cette mission sera ajout\u00e9e dans une prochaine version."
                : loadable ? "Sc\u00e8ne : " + ResolveScene(m) : "Aucune sc\u00e8ne de mission dans les Build Settings (outil Lot 6).";
            MarsUiKit.Show(infoGroup, true);
        }

        public void CloseInfo() { MarsUiKit.Show(infoGroup, false); lastTouch = Time.unscaledTime; }

        void StartSelected()
        {
            var m = MissionAt(selected); if (m == null) return;
            if (!StartMission(selected, out string why)) infoStatus.text = why;
            else { infoStatus.text = "Chargement\u2026"; startButton.interactable = false; }
        }

        void OpenOptions() { if (pauseMenu) pauseMenu.OpenOptions(); }
        void QuitApp() { if (pauseMenu) pauseMenu.AskQuit(); else MarsPauseMenu.QuitApplication(); }

        // ------------------------------------------------------------------ boucle
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime - introTime;
            TrackPlacement(t);
            titleGroup.alpha = Mathf.Clamp01((t - .6f) / 1.6f);
            titleCanvas.localScale = Vector3.one * .0011f * Mathf.Lerp(.92f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - .6f) / 2f)));
            hintGroup.alpha = interacted ? Mathf.MoveTowards(hintGroup.alpha, 0f, dt) : Mathf.Clamp01((t - 3f) / 1f);

            if (alignTime >= 0f)
            {
                alignTime += dt;
                float a = 1f - Mathf.Exp(-dt * 4.5f / Mathf.Max(.2f, alignSeconds));
                yaw = Mathf.Lerp(yaw, targetYaw, a);
                pitch = Mathf.Lerp(pitch, targetPitch, a);
                if (Mathf.Abs(yaw - targetYaw) < .05f && Mathf.Abs(pitch - targetPitch) < .05f) alignTime = -1f;
            }
            else if (!dragging)
            {
                float decay = Mathf.Exp(-inertiaDamping * dt);
                yawVelocity *= decay; pitchVelocity *= decay;
                yaw += yawVelocity * dt;
                pitch = Mathf.Clamp(pitch + pitchVelocity * dt, -maxPitchDegrees, maxPitchDegrees);
                bool idle = Time.unscaledTime - lastTouch > (interacted ? 25f : 0f) && !InfoOpen;
                if (idle && Mathf.Abs(yawVelocity) < 1f) yaw += idleSpinDegreesPerSecond * dt;
            }
            yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            if (alignTime >= 0f) targetYaw = yaw + Mathf.DeltaAngle(yaw, targetYaw);
            ApplyRotation();
            UpdateMarkers();
        }

        /// <summary>
        /// Place le menu (globe, titre, panneaux, etoiles) devant le regard, a viewDistance, sans tangage.
        /// Seul ce transform racine est deplace : aucune autre donnee n'est modifiee.
        /// </summary>
        public void Recenter()
        {
            if (!head) return;
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) { f = head.up; f.y = 0f; }
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            transform.SetPositionAndRotation(head.position + f * viewDistance + Vector3.up * heightFromEyes,
                                             Quaternion.LookRotation(f, Vector3.up));
            placed = true;
            UpdateMarkers();
        }

        /// <summary>
        /// Premier placement des que le suivi du casque est actif (pose locale non nulle, ou 1 s au plus tard),
        /// puis replacement si la pose locale de la tete saute d'un coup (recentrage Oculus / Link).
        /// </summary>
        void TrackPlacement(float t)
        {
            if (!placeInFrontOfHead || !head) return;
            Vector3 lp = head.localPosition;
            float ly = head.localEulerAngles.y;
            if (!placed)
            {
                if (lp.sqrMagnitude > 1e-6f || t > 1f) { Recenter(); Debug.Log($"[PerseVRance] Menu place devant le casque ({viewDistance:F1} m).", this); }
            }
            else if ((lp - lastHeadLocalPos).sqrMagnitude > .25f || Mathf.Abs(Mathf.DeltaAngle(ly, lastHeadLocalYaw)) > 40f)
                Recenter();                                               // saut impossible a 72 Hz : recentrage du suivi
            lastHeadLocalPos = lp; lastHeadLocalYaw = ly;
        }

        void ApplyRotation()
        {
            pivot.localRotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
        }

        void UpdateMarkers()
        {
            Vector3 center = transform.position;
            Vector3 eye = head ? head.position : center - transform.forward * 3f;
            Vector3 toEye = eye - center;
            float dist = toEye.magnitude;
            Vector3 toEyeN = dist > 1e-4f ? toEye / dist : -transform.forward;
            Quaternion face = Quaternion.LookRotation(-toEyeN, Vector3.up);
            markerCanvas.SetPositionAndRotation(center, face);
            titleCanvas.rotation = Quaternion.LookRotation(titleCanvas.position - eye, Vector3.up);
            float pulse = 1f + .18f * Mathf.Sin(Time.unscaledTime * 5f);
            for (int i = 0; i < markers.Length; i++)
            {
                Vector3 n = pivot.TransformDirection(localDirs[i]);
                bool visible = Vector3.Dot(n, toEyeN) > .18f;
                if (visible != markerVisible[i]) { markerVisible[i] = visible; markers[i].gameObject.SetActive(visible); }
                if (!visible) continue;
                Vector3 p = center + n * (radius * 1.015f);
                markers[i].SetPositionAndRotation(p + toEyeN * .02f, face);
                markers[i].localScale = Vector3.one * (i == selected ? pulse : 1f);
            }
        }

        // ------------------------------------------------------------------ edition
        [ContextMenu("Apercu : aligner sur la premiere mission")]
        void PreviewFirst()
        {
            var cat = catalog; if (!cat || cat.missions.Count == 0 || !pivot) return;
            var m = cat.missions[0];
            pivot.localRotation = Quaternion.AngleAxis(Mathf.Clamp(-m.latitude, -maxPitchDegrees, maxPitchDegrees), Vector3.right) *
                                  Quaternion.AngleAxis(m.longitudeEast + longitudeOffsetDegrees, Vector3.up);
        }
        [ContextMenu("Apercu : pivot a l'identite (calibration)")]
        void PreviewIdentity() { if (pivot) pivot.localRotation = Quaternion.identity; }

        void OnDrawGizmos()
        {
            Transform p = pivot ? pivot : transform;
            Gizmos.color = new Color(1f, .5f, .2f, .35f);
            Gizmos.DrawWireSphere(transform.position, radius);
            var cat = catalog; if (!cat) return;
            foreach (var m in cat.missions)
            {
                Vector3 n = p.TransformDirection(LocalDirection(m.latitude, m.longitudeEast));
                Vector3 pos = transform.position + n * radius;
                Gizmos.color = m.available ? new Color(1f, .45f, .15f) : new Color(.55f, .75f, 1f);
                Gizmos.DrawSphere(pos, radius * .025f);
                Gizmos.DrawLine(pos, pos + n * radius * .12f);
            }
            // Meridien 0 et equateur pour la calibration.
            Gizmos.color = new Color(.4f, .8f, 1f, .5f);
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= 72; i++)
            {
                Vector3 q = transform.position + p.TransformDirection(LocalDirection(0f, i * 5f - longitudeOffsetDegrees)) * radius * 1.005f;
                if (i > 0) Gizmos.DrawLine(prev, q); prev = q;
            }
            for (int i = 0; i <= 36; i++)
            {
                Vector3 q = transform.position + p.TransformDirection(LocalDirection(i * 5f - 90f, -longitudeOffsetDegrees)) * radius * 1.005f;
                if (i > 0) Gizmos.DrawLine(prev, q); prev = q;
            }
        }
    }
}
