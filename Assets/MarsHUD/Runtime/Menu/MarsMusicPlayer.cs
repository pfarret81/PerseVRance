using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Lecteur de musique facultatif (une par scene). Sans clip assigne, il ne joue rien.
    /// Plusieurs clips = playlist enchainee. Volume = canal Musique de MarsAudioSettings, fondu d'entree/sortie.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsMusicPlayer : MonoBehaviour
    {
        [Tooltip("Musique(s) de la scene. Laisser vide si aucune musique n'est encore prevue.")]
        public AudioClip[] playlist = new AudioClip[0];
        public bool shuffle;
        [Range(0f, 1f)] public float baseVolume = .8f;
        [Min(0f)] public float fadeSeconds = 2f;
        [Tooltip("Son de clic UI facultatif (boutons des menus).")]
        public AudioClip uiClick;

        AudioSource source;
        int index = -1;
        float fade, fadeTarget = 1f;
        public static MarsMusicPlayer Active { get; private set; }

        void Awake()
        {
            Active = this;
            if (uiClick) MarsUiKit.ClickClip = uiClick;
            source = GetComponent<AudioSource>();
            if (!source) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
        }

        void Start() { Next(); }
        void OnDestroy() { if (Active == this) Active = null; }

        void Next()
        {
            if (playlist == null || playlist.Length == 0) return;
            index = shuffle && playlist.Length > 1 ? (index + Random.Range(1, playlist.Length)) % playlist.Length : (index + 1) % playlist.Length;
            var clip = playlist[index];
            if (!clip) return;
            source.clip = clip;
            source.loop = playlist.Length == 1;
            source.Play();
        }

        /// <summary>Fondu de sortie (avant un changement de scene).</summary>
        public void FadeOut() { fadeTarget = 0f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fade = Mathf.MoveTowards(fade, fadeTarget, fadeSeconds > 0f ? dt / fadeSeconds : 1f);
            source.volume = baseVolume * MarsAudioSettings.Music * fade;
            if (fadeTarget > 0f && source.clip && !source.isPlaying && !source.loop && playlist.Length > 1) Next();
        }
    }
}
