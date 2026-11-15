using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Registre des prelevements de la session (non sauvegardes). Un prelevement est cree par le vehicule
    /// a sa position reelle d'arrivee ; il revele un petit disque de la carte du sol et devient consultable.
    /// Composition SIMULEE via MarsSoilCompositionProfile.
    /// Lot 5 : evenement SampleCreated (fil rouge de mission) et etiquette d'etape attachee au prelevement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsSoilSampler : MonoBehaviour
    {
        public MarsHUDController hud;
        public MarsSoilCompositionProfile profile;
        [Tooltip("Duree simulee du prelevement a l'arrivee (secondes) - choix du prototype.")]
        [Min(0f)] public float sampleDurationSeconds = 8f;
        [Tooltip("Rayon de la zone revelee sur la carte du sol autour de chaque prelevement.")]
        [Min(.5f)] public float revealRadiusMeters = 12f;
        [Min(1)] public int maxSamples = 256;

        public sealed class Sample
        {
            public int id;
            public Vector2 data;
            public Vector3 world;
            public float sessionTime;
            public string vehicle;
            public float[] regional;
            public float[] site;
            public int strongest;
            public float anomaly;
            /// <summary>Etape du fil rouge contenant le point (vide si hors etape).</summary>
            public string siteLabel = "";
        }

        /// <summary>Appele apres l'enregistrement d'un prelevement (fil rouge de mission).</summary>
        public event System.Action<Sample> SampleCreated;
        /// <summary>Fournit le nom de l'etape contenant un point monde (renseigne par MarsMissionPlan).</summary>
        public System.Func<Vector3, string> SiteLabelProvider;

        readonly List<Sample> samples = new List<Sample>(64);
        public IReadOnlyList<Sample> Samples => samples;
        public int Version { get; private set; }
        public MarsSoilCompositionProfile Profile => profile;
        public string LastRefusal { get; private set; } = "";

        void Awake()
        {
            if (!hud) hud = GetComponent<MarsHUDController>();
            if (!profile) profile = MarsSoilCompositionProfile.CreateDefault();
        }

        public bool TryCreateSample(Vector3 world, string vehicle, out Sample sample)
        {
            sample = null; LastRefusal = "";
            if (!hud || !hud.Ready || hud.Coordinates == null || hud.Discovery == null) { LastRefusal = "HUD non pret."; return false; }
            if (samples.Count >= maxSamples) { LastRefusal = "Capacite de prelevements du prototype atteinte."; return false; }
            int n = profile.Count;
            if (n == 0) { LastRefusal = "Profil de composition vide."; return false; }
            Vector2 data = hud.Coordinates.WorldToData(world);
            sample = new Sample
            {
                id = samples.Count + 1, data = data, world = world, sessionTime = Time.time, vehicle = vehicle,
                regional = new float[n], site = new float[n]
            };
            profile.Evaluate(data, sample.regional, sample.site);
            sample.anomaly = profile.AnomalyIndex(sample.regional, sample.site, out sample.strongest);
            if (!hud.Discovery.TryRevealSoilDisc(data, revealRadiusMeters)) { LastRefusal = hud.Discovery.LastRefusal; sample = null; return false; }
            sample.siteLabel = SiteLabelProvider != null ? SiteLabelProvider(world) ?? "" : "";
            samples.Add(sample);
            Version++;
            SampleCreated?.Invoke(sample);
            return true;
        }

        /// <summary>Couleur de la carte du sol : indice d'ecart du prelevement le plus proche couvrant le point.</summary>
        public bool TryGetSoilColor(Vector2 data, out Color color)
        {
            color = default;
            float best = float.PositiveInfinity;
            Sample found = null;
            float r2 = revealRadiusMeters * revealRadiusMeters;
            for (int i = 0; i < samples.Count; i++)
            {
                float d = (samples[i].data - data).sqrMagnitude;
                if (d <= r2 && d < best) { best = d; found = samples[i]; }
            }
            if (found == null) return false;
            color = MarsMapCatalog.EvaluateHueLegend(found.anomaly);
            return true;
        }

        public string ShortSummary()
        {
            if (samples.Count == 0) return "Aucun prélèvement. Arrêter Perseverance puis toucher « Prélever ».";
            Sample s = samples[samples.Count - 1];
            return $"{samples.Count} prélèvement(s) — dernier #{s.id} : {Highlight(s)}";
        }

        public string Highlight(Sample s)
        {
            if (s.strongest < 0) return "composition proche du contexte";
            float delta = s.site[s.strongest] - s.regional[s.strongest];
            return $"écart {profile.components[s.strongest].label} {(delta >= 0f ? "+" : "-")}{Mathf.Abs(delta):F1} pt";
        }

        public string BuildReport(int index, StringBuilder sb)
        {
            sb.Length = 0;
            if (index < 0 || index >= samples.Count) return "Aucun prélèvement.";
            Sample s = samples[index];
            sb.Append("PRÉLÈVEMENT #").Append(s.id).Append(" / ").Append(samples.Count).Append(" — ").Append(s.vehicle).Append('\n');
            if (!string.IsNullOrEmpty(s.siteLabel)) sb.Append(s.siteLabel).Append(" · ");
            sb.Append("E ").Append(s.data.x.ToString("F0")).Append(" m  N ").Append(s.data.y.ToString("F0")).Append(" m\n");
            for (int i = 0; i < profile.Count; i++)
            {
                float d = s.site[i] - s.regional[i];
                sb.Append(i == s.strongest ? "> " : "   ").Append(profile.components[i].label).Append(" ")
                  .Append(s.site[i].ToString("F1")).Append(" %  (")
                  .Append(d >= 0f ? "+" : "-").Append(Mathf.Abs(d).ToString("F1")).Append(")\n");
            }
            sb.Append("Indice d'écart ").Append(s.anomaly.ToString("F2")).Append(" — ").Append(Highlight(s)).Append('\n');
            sb.Append(profile.provenance);
            return sb.ToString();
        }
    }
}
