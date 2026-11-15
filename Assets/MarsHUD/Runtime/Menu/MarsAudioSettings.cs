using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Reglages audio persistants (PlayerPrefs). Volume general = AudioListener.volume (agit sur tous les sons
    /// sans mixer ni package). Musique et Effets sont lus par MarsMusicPlayer / MarsAudioChannel et par le clic UI.
    /// </summary>
    public static class MarsAudioSettings
    {
        const string KMaster = "PerseVRance.Volume.Master", KMusic = "PerseVRance.Volume.Music", KEffects = "PerseVRance.Volume.Effects";
        static bool loaded;
        static float master = 1f, music = .7f, effects = .9f;
        public static event System.Action Changed;

        public static float Master { get { Load(); return master; } set { Load(); master = Mathf.Clamp01(value); Apply(); } }
        public static float Music { get { Load(); return music; } set { Load(); music = Mathf.Clamp01(value); Apply(); } }
        public static float Effects { get { Load(); return effects; } set { Load(); effects = Mathf.Clamp01(value); Apply(); } }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = PlayerPrefs.GetFloat(KMaster, 1f);
            music = PlayerPrefs.GetFloat(KMusic, .7f);
            effects = PlayerPrefs.GetFloat(KEffects, .9f);
            AudioListener.volume = master;
        }

        static void Apply()
        {
            AudioListener.volume = master;
            PlayerPrefs.SetFloat(KMaster, master);
            PlayerPrefs.SetFloat(KMusic, music);
            PlayerPrefs.SetFloat(KEffects, effects);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { loaded = false; Changed = null; Load(); }
    }
}
