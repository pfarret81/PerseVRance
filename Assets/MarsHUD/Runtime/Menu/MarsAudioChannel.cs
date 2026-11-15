using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - A placer sur un AudioSource existant (ambiance, effets du rover...) pour qu'il suive le canal choisi.
    /// Le volume de base est celui de l'AudioSource au demarrage.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MarsAudioChannel : MonoBehaviour
    {
        public enum Channel { Music, Effects }
        public Channel channel = Channel.Effects;
        AudioSource source;
        float baseVolume = 1f;

        void Awake() { source = GetComponent<AudioSource>(); baseVolume = source.volume; }
        void OnEnable() { MarsAudioSettings.Changed += Apply; Apply(); }
        void OnDisable() { MarsAudioSettings.Changed -= Apply; }
        void Apply()
        {
            if (!source) return;
            source.volume = baseVolume * (channel == Channel.Music ? MarsAudioSettings.Music : MarsAudioSettings.Effects);
        }
    }
}
