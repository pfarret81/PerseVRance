using System;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Profil de composition SIMULEE pour les prelevements du prototype. Ce ne sont pas des mesures.
    /// Lot 5 : l'echantillon-type par defaut reprend les teneurs moyennes du "sol basaltique moyen" (ABS)
    /// derive des analyses APXS de Spirit, Opportunity et Curiosity (O'Connell-Cooper et al., 2017,
    /// JGR Planets, doi:10.1002/2017JE005268). Les ecarts regionaux et locaux sont tires d'une graine
    /// deterministe : un meme lieu donne toujours le meme resultat, chaque site differe legerement.
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
            [Tooltip("Couleur du segment dans le diagramme. Alpha 0 = couleur automatique.")]
            public Color color = new Color(0f, 0f, 0f, 0f);
        }

        public string provenance = "SIMULATION - échantillon-type : sol basaltique moyen martien (APXS, O'Connell-Cooper et al. 2017)";
        public SoilComponent[] components = new SoilComponent[0];
        [Min(1f)] public float regionalScaleMeters = 60f;
        [Min(.5f)] public float localCellMeters = 6f;
        public int seed = 1729;

        public int Count => components == null ? 0 : components.Length;

        static readonly Color[] Palette =
        {
            new Color(.86f, .80f, .62f), new Color(.80f, .32f, .20f), new Color(.55f, .70f, .85f), new Color(.45f, .75f, .45f),
            new Color(.95f, .92f, .85f), new Color(.98f, .85f, .25f), new Color(.70f, .55f, .90f), new Color(.40f, .85f, .85f),
            new Color(.95f, .55f, .75f), new Color(.60f, .60f, .60f), new Color(.95f, .65f, .35f), new Color(.35f, .50f, .95f),
            new Color(.75f, .90f, .40f)
        };

        public Color ColorOf(int i)
        {
            if (i < 0 || i >= Count) return Color.gray;
            Color c = components[i].color;
            return c.a > .01f ? c : Palette[i % Palette.Length];
        }

        /// <summary>
        /// Echantillon-type : sol basaltique moyen (ABS, % massiques). Variation regionale ~ 0,5 sigma,
        /// variation locale ~ 0,6 sigma des ecarts-types publies. Normalise a 100 % au calcul.
        /// </summary>
        public static SoilComponent[] AverageBasalticSoil() => new[]
        {
            Make("SiO2", 45.44f, .80f, .95f, Palette[0]),  Make("FeO", 17.39f, .90f, 1.10f, Palette[1]),
            Make("Al2O3", 9.68f, .38f, .45f, Palette[2]),  Make("MgO", 8.23f, .42f, .50f, Palette[3]),
            Make("CaO", 6.62f, .22f, .27f, Palette[4]),    Make("SO3", 6.09f, .75f, .92f, Palette[5]),
            Make("Na2O", 2.71f, .22f, .26f, Palette[6]),   Make("TiO2", .95f, .07f, .09f, Palette[7]),
            Make("P2O5", .88f, .05f, .07f, Palette[8]),    Make("Cl", .69f, .07f, .08f, Palette[9]),
            Make("K2O", .46f, .04f, .05f, Palette[10]),    Make("Cr2O3", .38f, .04f, .05f, Palette[11]),
            Make("MnO", .35f, .02f, .02f, Palette[12])
        };

        public static MarsSoilCompositionProfile CreateDefault()
        {
            var p = CreateInstance<MarsSoilCompositionProfile>();
            p.name = "Echantillon-type : sol basaltique moyen (simule)";
            p.components = AverageBasalticSoil();
            return p;
        }
        static SoilComponent Make(string label, float reference, float regional, float local, Color color) =>
            new SoilComponent { label = label, referencePercent = reference, regionalVariation = regional, localVariation = local, color = color };

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
