using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Menu en jeu (et page Options/Quitter du menu principal). Ouverture : bouton Menu de la manette gauche
    /// (le HUD des cartes reste sur Y/secondaryButton), Echap au clavier dans l'editeur.
    /// Contenu : Reprendre, Options (volumes general / musique / effets), Recommencer la mission, Retour au menu
    /// principal, Quitter l'application (avec confirmation). Le panneau est pose devant la tete a l'ouverture
    /// (monde, pas colle au visage) et se ferme si l'on s'en eloigne.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsPauseMenu : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("true dans la scene du menu principal : pas de Recommencer / Retour au menu.")]
        public bool isMainMenu;
        [Tooltip("Nom de la scene du menu principal (Build Settings).")]
        public string mainMenuScene = "PerseVRance_Menu";
        [Header("Entrees")]
        public InputActionReference openAction;
        public string openBinding = "<XRController>{LeftHand}/{MenuButton}";
        public bool escapeKeyInEditor = true;
        [Header("Comportement")]
        [Tooltip("Met la simulation en pause (Time.timeScale = 0) tant que le menu est ouvert. Le HUD utilise le temps non mis a l'echelle.")]
        public bool pauseTime = true;
        [Min(.5f)] public float distanceFromHead = 1.1f;
        [Min(.5f)] public float autoCloseDistance = 2.5f;
        public MarsHUDController hud;
        public Transform head;

        enum Page { Main, Options, Confirm }
        enum Pending { None, Restart, MainMenu, Quit }
        RectTransform canvas;
        CanvasGroup group, mainPage, optionsPage, confirmPage;
        Text confirmText, masterValue, musicValue, effectsValue, statusText;
        Button restartButton, menuButton;
        InputAction localAction, activeAction;
        Pending pending;
        bool open, timePaused;
        float previousTimeScale = 1f;
        Vector3 openHeadPosition;

        public bool IsOpen => open;

        void Awake()
        {
            if (!head && Camera.main) head = Camera.main.transform;
            if (!hud) hud = FindAnyObjectByType<MarsHUDController>();
            Build(head ? head.GetComponent<Camera>() : Camera.main);
            SetVisible(false);
        }

        void Start() { if (!isMainMenu) MarsSceneFader.FadeIn(1f); }

        void OnEnable()
        {
            if (openAction && openAction.action != null) activeAction = openAction.action;
            else
            {
                if (localAction == null)
                {
                    localAction = new InputAction("PerseVRance Pause", InputActionType.Button);
                    if (!string.IsNullOrWhiteSpace(openBinding)) localAction.AddBinding(openBinding);
                }
                activeAction = localAction;
            }
            activeAction.Enable();
            MarsAudioSettings.Changed += RefreshVolumes;
            RefreshVolumes();
        }

        void OnDisable()
        {
            if (activeAction == localAction && localAction != null) localAction.Disable();
            MarsAudioSettings.Changed -= RefreshVolumes;
            ResumeTime();
        }

        void OnDestroy() { localAction?.Dispose(); ResumeTime(); }

        // ------------------------------------------------------------------ construction
        void Build(Camera cam)
        {
            canvas = MarsUiKit.WorldCanvas("PerseVRance pause menu", transform, new Vector2(520f, 600f), .0012f, cam, 50);
            group = MarsUiKit.Group(canvas.gameObject);
            var panel = MarsUiKit.Panel("Panel", canvas, new Vector2(520f, 600f), Vector2.zero, MarsUiKit.Accent).rectTransform;
            MarsUiKit.Label("Title", panel, isMainMenu ? "PERSE<color=#FF7A38>VR</color>ANCE" : "PAUSE", new Vector2(460f, 50f), new Vector2(0f, 255f), 30,
                Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            statusText = MarsUiKit.Label("Status", panel, "", new Vector2(460f, 30f), new Vector2(0f, -275f), 17, MarsUiKit.TextDim);

            mainPage = PageGroup(panel, "Main");
            float y = 165f;
            MarsUiKit.Button(mainPage.transform, isMainMenu ? "Retour" : "Reprendre", new Vector2(400f, 62f), new Vector2(0f, y), MarsUiKit.Cyan, Close, 22); y -= 80f;
            MarsUiKit.Button(mainPage.transform, "Options du son", new Vector2(400f, 62f), new Vector2(0f, y), MarsUiKit.Cyan, () => ShowPage(Page.Options), 22); y -= 80f;
            if (!isMainMenu)
            {
                restartButton = MarsUiKit.Button(mainPage.transform, "Recommencer la mission", new Vector2(400f, 62f), new Vector2(0f, y), MarsUiKit.Accent, () => Ask(Pending.Restart), 22); y -= 80f;
                menuButton = MarsUiKit.Button(mainPage.transform, "Retour au menu principal", new Vector2(400f, 62f), new Vector2(0f, y), MarsUiKit.Accent, () => Ask(Pending.MainMenu), 22); y -= 80f;
            }
            MarsUiKit.Button(mainPage.transform, "Quitter l'application", new Vector2(400f, 62f), new Vector2(0f, y), new Color(1f, .45f, .4f), () => Ask(Pending.Quit), 22);

            optionsPage = PageGroup(panel, "Options");
            MarsUiKit.Label("Head", optionsPage.transform, "SON", new Vector2(420f, 30f), new Vector2(0f, 185f), 20, MarsUiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            masterValue = VolumeRow(optionsPage.transform, "G\u00e9n\u00e9ral", 120f, d => MarsAudioSettings.Master += d);
            musicValue = VolumeRow(optionsPage.transform, "Musique", 40f, d => MarsAudioSettings.Music += d);
            effectsValue = VolumeRow(optionsPage.transform, "Effets", -40f, d => MarsAudioSettings.Effects += d);
            MarsUiKit.Button(optionsPage.transform, "Retour", new Vector2(240f, 60f), new Vector2(0f, -170f), MarsUiKit.Cyan, () => ShowPage(Page.Main), 22);

            confirmPage = PageGroup(panel, "Confirm");
            confirmText = MarsUiKit.Label("Question", confirmPage.transform, "", new Vector2(440f, 160f), new Vector2(0f, 90f), 24, Color.white);
            MarsUiKit.Button(confirmPage.transform, "Confirmer", new Vector2(190f, 64f), new Vector2(-105f, -60f), new Color(1f, .45f, .4f), Confirm, 22);
            MarsUiKit.Button(confirmPage.transform, "Annuler", new Vector2(190f, 64f), new Vector2(105f, -60f), MarsUiKit.Cyan, () => ShowPage(Page.Main), 22);
            ShowPage(Page.Main);
        }

        static CanvasGroup PageGroup(RectTransform parent, string name)
        {
            var r = MarsUiKit.Rect(name, parent, parent.sizeDelta, Vector2.zero);
            return MarsUiKit.Group(r.gameObject);
        }

        Text VolumeRow(Transform parent, string label, float y, System.Action<float> change)
        {
            MarsUiKit.Label(label, parent, label, new Vector2(170f, 56f), new Vector2(-135f, y), 22, MarsUiKit.TextMain, TextAnchor.MiddleLeft);
            MarsUiKit.Button(parent, "-", new Vector2(64f, 56f), new Vector2(10f, y), MarsUiKit.Cyan, () => change(-.1f), 30);
            var value = MarsUiKit.Label(label + " value", parent, "100 %", new Vector2(100f, 56f), new Vector2(97f, y), 22, Color.white);
            MarsUiKit.Button(parent, "+", new Vector2(64f, 56f), new Vector2(185f, y), MarsUiKit.Cyan, () => change(.1f), 30);
            return value;
        }

        void RefreshVolumes()
        {
            if (!masterValue) return;
            masterValue.text = Mathf.RoundToInt(MarsAudioSettings.Master * 100f) + " %";
            musicValue.text = Mathf.RoundToInt(MarsAudioSettings.Music * 100f) + " %";
            effectsValue.text = Mathf.RoundToInt(MarsAudioSettings.Effects * 100f) + " %";
        }

        void ShowPage(Page p)
        {
            MarsUiKit.Show(mainPage, p == Page.Main);
            MarsUiKit.Show(optionsPage, p == Page.Options);
            MarsUiKit.Show(confirmPage, p == Page.Confirm);
            if (p != Page.Confirm) pending = Pending.None;
            statusText.text = "";
        }

        void Ask(Pending what)
        {
            pending = what;
            confirmText.text = what == Pending.Quit ? "Quitter PerseVRance ?"
                : what == Pending.MainMenu ? "Revenir au menu principal ?\nLa progression de la mission sera perdue."
                : "Recommencer la mission depuis le d\u00e9but ?";
            MarsUiKit.Show(mainPage, false); MarsUiKit.Show(optionsPage, false); MarsUiKit.Show(confirmPage, true);
            statusText.text = "";
        }

        void Confirm()
        {
            string why = "";
            switch (pending)
            {
                case Pending.Quit: QuitApplication(); return;
                case Pending.MainMenu:
                    if (MarsSceneFader.LoadScene(mainMenuScene, out why)) { statusText.text = "Chargement\u2026"; return; }
                    break;
                case Pending.Restart:
                    if (MarsSceneFader.LoadScene(SceneManager.GetActiveScene().name, out why)) { statusText.text = "Chargement\u2026"; return; }
                    break;
            }
            ShowPage(Page.Main);
            statusText.text = why;
        }

        public static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ ouverture
        public void Toggle() { if (open) Close(); else Open(); }
        public void OpenOptions() { if (!open) Open(); ShowPage(Page.Options); }
        public void AskQuit() { if (!open) Open(); Ask(Pending.Quit); }

        public void Open()
        {
            if (open || MarsSceneFader.Busy) return;
            if (hud && hud.MenuOpen) hud.ToggleMenu();          // un seul menu a la fois
            if (head)
            {
                Vector3 f = head.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 p = head.position + f * distanceFromHead + Vector3.down * .12f;
                canvas.SetPositionAndRotation(p, Quaternion.LookRotation(f, Vector3.up));
                openHeadPosition = head.position;
            }
            open = true;
            ShowPage(Page.Main);
            SetVisible(true);
            if (pauseTime && !isMainMenu && !timePaused) { previousTimeScale = Time.timeScale; Time.timeScale = 0f; timePaused = true; }
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            SetVisible(false);
            ResumeTime();
        }

        void ResumeTime()
        {
            if (!timePaused) return;
            timePaused = false;
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        }

        void SetVisible(bool v) { MarsUiKit.Show(group, v); canvas.gameObject.SetActive(v); }

        void Update()
        {
            bool pressed = activeAction != null && activeAction.WasPressedThisFrame();
#if UNITY_EDITOR
            if (escapeKeyInEditor && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) pressed = true;
#endif
            if (pressed) Toggle();
            if (open && head && autoCloseDistance > 0f)
            {
                Vector3 d = head.position - openHeadPosition; d.y = 0f;
                if (d.sqrMagnitude > autoCloseDistance * autoCloseDistance) Close();
            }
        }
    }
}
