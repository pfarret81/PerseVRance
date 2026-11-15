using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Quick menu au poignet gauche du menu principal : mission precedente / suivante (le globe s'aligne
    /// automatiquement) et ouverture de la fiche. Visible seulement quand le panneau est tourne vers la tete
    /// (geste "regarder sa montre") ; se manipule avec le rayon de la main droite.
    /// </summary>
    public sealed class MarsWristQuickMenu : MonoBehaviour
    {
        [Range(-1f, 1f)] public float showDot = .35f;
        MarsGlobeMenu host;
        Transform hand, head;
        RectTransform canvas;
        CanvasGroup group;
        Text counter, missionTitle, missionSite, status;
        Button launch;
        int shownIndex = int.MinValue;
        bool visible;

        public void Init(MarsGlobeMenu menu, Camera cam, Transform leftHand, Vector3 localPos, Vector3 localEuler)
        {
            host = menu; hand = leftHand; head = cam ? cam.transform : null;
            canvas = MarsUiKit.WorldCanvas("Quick menu canvas", leftHand ? leftHand : transform, new Vector2(340f, 270f), .00055f, cam, 5);
            canvas.localPosition = localPos;
            canvas.localRotation = Quaternion.Euler(localEuler);
            group = MarsUiKit.Group(canvas.gameObject);
            var panel = MarsUiKit.Panel("Panel", canvas, new Vector2(340f, 270f), Vector2.zero, MarsUiKit.Accent).rectTransform;
            counter = MarsUiKit.Label("Counter", panel, "MISSIONS", new Vector2(300f, 26f), new Vector2(0f, 112f), 18, MarsUiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            missionTitle = MarsUiKit.Label("Title", panel, "", new Vector2(300f, 34f), new Vector2(0f, 80f), 22, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            missionSite = MarsUiKit.Label("Site", panel, "", new Vector2(300f, 26f), new Vector2(0f, 52f), 16, MarsUiKit.TextDim, TextAnchor.MiddleLeft);
            MarsUiKit.Button(panel, "<", new Vector2(70f, 56f), new Vector2(-120f, -2f), MarsUiKit.Cyan, () => host.Step(-1, false), 28);
            MarsUiKit.Button(panel, "Voir", new Vector2(140f, 56f), new Vector2(0f, -2f), MarsUiKit.Accent, OpenCurrent, 22);
            MarsUiKit.Button(panel, ">", new Vector2(70f, 56f), new Vector2(120f, -2f), MarsUiKit.Cyan, () => host.Step(1, false), 28);
            launch = MarsUiKit.Button(panel, "Lancer la mission", new Vector2(310f, 56f), new Vector2(0f, -70f), MarsUiKit.Accent, Launch, 22);
            status = MarsUiKit.Label("Status", panel, "", new Vector2(310f, 26f), new Vector2(0f, -115f), 15, new Color(1f, .75f, .45f));
            MarsUiKit.Show(group, false);
            if (!leftHand) Debug.LogWarning("[PerseVRance] Quick menu : manette gauche introuvable (renseigner MarsGlobeMenu.leftHand).", menu);
            Refresh();
        }

        void Launch()
        {
            if (host.Selected < 0) { host.Step(1, false); return; }
            if (host.StartMission(host.Selected, out string why)) { status.text = "Chargement\u2026"; launch.interactable = false; }
            else status.text = why;
        }

        void OpenCurrent()
        {
            if (host.Selected < 0) host.Step(1, true);
            else host.OpenInfo(host.Selected);
        }

        public void Refresh()
        {
            if (!host || !missionTitle) return;
            int i = host.Selected;
            if (i == shownIndex) return;
            shownIndex = i;
            if (launch) launch.interactable = i >= 0 && host.CanStart(i);   // seulement au changement de selection
            if (status) status.text = "";
            var m = host.MissionAt(i);
            counter.text = m == null ? $"MISSIONS \u00b7 {host.MissionCount}" : $"MISSION {i + 1} / {host.MissionCount}";
            missionTitle.text = m == null ? "Choisir une mission" : m.title;
            missionSite.text = m == null ? "< > pour parcourir" : (m.available ? MarsMissionCatalog.Coordinates(m) : "Bient\u00f4t disponible");
        }

        void Update()
        {
            if (!hand || !head || !canvas) return;
            Vector3 toPanel = canvas.position - head.position;
            float d = toPanel.magnitude;
            bool show = d > 1e-3f && d < .9f && Vector3.Dot(canvas.forward, toPanel / d) > showDot;
            if (show != visible) { visible = show; group.interactable = show; group.blocksRaycasts = show; }
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            Refresh();
        }
    }
}
