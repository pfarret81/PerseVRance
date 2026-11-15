using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Fondu au noir attache a la tete (une seule image noire sur un petit canvas monde, sans camera ni
    /// RenderTexture) et chargement de scene asynchrone. Cree a la demande.
    /// </summary>
    public sealed class MarsSceneFader : MonoBehaviour
    {
        static MarsSceneFader instance;
        CanvasGroup group;
        float target, speed = 1.5f;
        public static bool Busy { get; private set; }

        public static MarsSceneFader Get()
        {
            if (instance) return instance;
            Camera cam = Camera.main;
            if (!cam) return null;
            var go = new GameObject("PerseVRance fader", typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(cam.transform, false);
            r.localPosition = new Vector3(0f, 0f, Mathf.Max(cam.nearClipPlane * 1.5f, .06f));
            r.localRotation = Quaternion.identity;
            r.sizeDelta = new Vector2(100f, 100f);
            r.localScale = Vector3.one * .02f;                 // 2 m x 2 m : couvre tout le champ de vision
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;
            var img = MarsUiKit.Rect("Black", r, new Vector2(100f, 100f), Vector2.zero).gameObject;
            img.AddComponent<CanvasRenderer>();
            var image = img.AddComponent<Image>();
            image.color = Color.black; image.raycastTarget = false;
            instance = go.AddComponent<MarsSceneFader>();
            instance.group = go.AddComponent<CanvasGroup>();
            instance.group.blocksRaycasts = false; instance.group.interactable = false;
            instance.group.alpha = 0f;
            return instance;
        }

        /// <summary>Demarre noir puis s'eclaircit (debut de scene).</summary>
        public static void FadeIn(float seconds = 1.2f)
        {
            var f = Get(); if (!f) return;
            f.enabled = true; f.group.alpha = 1f; f.target = 0f; f.speed = 1f / Mathf.Max(.05f, seconds);
        }

        /// <summary>Fondu au noir puis chargement de la scene (nom ou chemin des Build Settings).</summary>
        public static bool LoadScene(string sceneName, out string refusal)
        {
            refusal = "";
            if (Busy) { refusal = "Chargement deja en cours."; return false; }
            if (string.IsNullOrEmpty(sceneName)) { refusal = "Aucune scene associee."; return false; }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            { refusal = $"La scene \u00ab {sceneName} \u00bb n'est pas dans les Build Settings."; return false; }
            var f = Get();
            if (!f) { Time.timeScale = 1f; SceneManager.LoadScene(sceneName); return true; }
            f.enabled = true;
            f.StartCoroutine(f.FadeAndLoad(sceneName));
            return true;
        }

        IEnumerator FadeAndLoad(string sceneName)
        {
            Busy = true;
            if (MarsMusicPlayer.Active) MarsMusicPlayer.Active.FadeOut();
            target = 1f; speed = 1.6f;
            while (group.alpha < .999f) yield return null;
            Time.timeScale = 1f;
            var op = SceneManager.LoadSceneAsync(sceneName);
            while (op != null && !op.isDone) yield return null;
        }

        void Update()
        {
            group.alpha = Mathf.MoveTowards(group.alpha, target, speed * Time.unscaledDeltaTime);
            if (group.alpha <= 0f && target <= 0f && !Busy) enabled = false;
        }
        void OnDestroy() { if (instance == this) { instance = null; Busy = false; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; Busy = false; }
    }
}
