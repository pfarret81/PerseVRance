using System;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Profil de composition SIMULEE pour les prelevements du prototype. Ce ne sont pas des mesures :
    /// les valeurs par defaut sont des exemples a remplacer par une source scientifique si besoin.
    /// La composition depend uniquement de la position (deterministe) : un meme lieu donne toujours
    /// le meme resultat, et chaque site presente un leger ecart local par rapport a son contexte regional.
    /// </summary>
    [CreateAssetMenu(menuName = "Mars HUD/Soil composition profile (simulated)", fileName = "MarsSoilCompositionProfile")]
    public sealed class MarsSoilCompositionProfile : ScriptableObject
    {
        [Serializable]
        public sealed class SoilComponent
        {
            public string label = "Oxyde";
            [Min(0f)] public float referencePercent = 10f;
            [Tooltip("Amplitude (points de %) de la variation regionale lente.")]
            [Min(0f)] public float regionalVariation = 1f;
            [Tooltip("Amplitude (points de %) de l'ecart propre a chaque site preleve : la 'decouverte'.")]
            [Min(0f)] public float localVariation = .8f;
        }

        public string provenance = "SIMULATION du prototype - profil d'exemple, aucune mesure";
        public SoilComponent[] components = new SoilComponent[0];
        [Min(1f)] public float regionalScaleMeters = 60f;
        [Min(.5f)] public float localCellMeters = 6f;
        public int seed = 1729;

        public int Count => components == null ? 0 : components.Length;

        public static MarsSoilCompositionProfile CreateDefault()
        {
            var p = CreateInstance<MarsSoilCompositionProfile>();
            p.name = "Profil de sol par defaut (exemple simule)";
            p.components = new[]
            {
                Make("SiO2", 45f, 1.5f, 1.2f), Make("FeOT", 18f, 1.2f, 1.4f), Make("Al2O3", 10f, .8f, .7f),
                Make("MgO", 8f, .8f, 1.0f), Make("CaO", 7f, .6f, .6f), Make("SO3", 6f, 1.0f, 1.3f),
                Make("Na2O", 3f, .3f, .3f), Make("Autres", 3f, .3f, .3f)
            };
            return p;
        }
        static SoilComponent Make(string label, float reference, float regional, float local) =>
            new SoilComponent { label = label, referencePercent = reference, regionalVariation = regional, localVariation = local };

        /// <summary>
        /// Remplit regional[] (contexte lent) et site[] (valeur prelevee) en % normalises a 100.
        /// Les tableaux doivent avoir la taille Count.
        /// </summary>
        public void Evaluate(Vector2 data, float[] regional, float[] site)
        {
            int n = Count;
            float sumR = 0f, sumS = 0f;
            Vector2 q = data / Mathf.Max(1f, regionalScaleMeters);
            int cx = Mathf.FloorToInt(data.x / Mathf.Max(.5f, localCellMeters));
            int cy = Mathf.FloorToInt(data.y / Mathf.Max(.5f, localCellMeters));
            for (int i = 0; i < n; i++)
            {
                SoilComponent c = components[i];
                float r = Mathf.Max(0f, c.referencePercent + c.regionalVariation * ValueNoise(q.x, q.y, seed + 101 * i));
                float l = c.localVariation * Hash(cx, cy, seed * 7 + 31 * i);
                regional[i] = r;
                site[i] = Mathf.Max(0f, r + l);
                sumR += regional[i]; sumS += site[i];
            }
            if (sumR > 0f) for (int i = 0; i < n; i++) regional[i] *= 100f / sumR;
            if (sumS > 0f) for (int i = 0; i < n; i++) site[i] *= 100f / sumS;
        }

        /// <summary>Plus grand ecart local relatif a l'amplitude locale configuree, borne a [0,1].</summary>
        public float AnomalyIndex(float[] regional, float[] site, out int strongest)
        {
            strongest = -1;
            float best = 0f;
            for (int i = 0; i < Count; i++)
            {
                float amplitude = Mathf.Max(.01f, components[i].localVariation);
                float v = Mathf.Abs(site[i] - regional[i]) / amplitude;
                if (v > best) { best = v; strongest = i; }
            }
            return Mathf.Clamp01(best);
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
            }
        }
        static float ValueNoise(float x, float y, int s)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(x0, y0, s), Hash(x0 + 1, y0, s), fx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, s), Hash(x0 + 1, y0 + 1, s), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}
