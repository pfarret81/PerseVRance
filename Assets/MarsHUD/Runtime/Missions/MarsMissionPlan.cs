using System.Collections.Generic;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 5 - Fil rouge de la mission : suite ordonnee d'etapes (enfants MarsMissionCheckpoint) terminee
    /// par une arrivee. Une seule etape est active a la fois ; elle est validee par la condition choisie
    /// (par defaut : prelevement de Perseverance dans le rayon), puis la suivante devient active.
    /// Les etapes sont des objets de scene independants du rover. Ce composant n'envoie aucun ordre.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class MarsMissionPlan : MonoBehaviour
    {
        public MarsHUDController hud;
        public MarsSoilSampler sampler;
        public PerseveranceMission perseverance;
        public string missionName = "Fil rouge — Jezero";
        [Range(1f, 20f)] public float checkHz = 4f;

        public enum StepState { Done, Current, Future }

        readonly List<MarsMissionCheckpoint> steps = new List<MarsMissionCheckpoint>(8);
        public IReadOnlyList<MarsMissionCheckpoint> Steps => steps;
        public int CurrentIndex { get; private set; }
        public bool IsFinished => steps.Count > 0 && CurrentIndex >= steps.Count;
        /// <summary>Incremente a chaque changement d'etape (carte, note).</summary>
        public int Version { get; private set; }
        public string LastEvent { get; private set; } = "";
        public float LastEventTime { get; private set; } = -999f;
        float nextCheck;
        bool subscribed;

        void Awake()
        {
            if (!hud) hud = FindAnyObjectByType<MarsHUDController>();
            if (!sampler && hud) sampler = hud.GetComponent<MarsSoilSampler>();
            if (!perseverance) perseverance = FindAnyObjectByType<PerseveranceMission>();
            Rebuild();
        }

        void OnEnable()
        {
            if (sampler && !subscribed) { sampler.SampleCreated += OnSample; sampler.SiteLabelProvider = SiteLabel; subscribed = true; }
        }

        void OnDisable()
        {
            if (!subscribed) return;
            subscribed = false;
            if (!sampler) return;
            sampler.SampleCreated -= OnSample;
            if (sampler.SiteLabelProvider == (System.Func<Vector3, string>)SiteLabel) sampler.SiteLabelProvider = null;
        }

        /// <summary>Relit les etapes (enfants actifs, dans l'ordre de la hierarchie) et recommence au debut.</summary>
        public void Rebuild()
        {
            steps.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                var cp = child.GetComponent<MarsMissionCheckpoint>();
                if (cp) steps.Add(cp);
            }
            CurrentIndex = 0;
            Version++;
        }

        public bool IsArrival(int i) => i == steps.Count - 1;
        public StepState StateOf(int i) => i < CurrentIndex ? StepState.Done : i == CurrentIndex ? StepState.Current : StepState.Future;

        public bool TryGetCurrent(out MarsMissionCheckpoint checkpoint)
        {
            checkpoint = !IsFinished && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;
            return checkpoint;
        }

        /// <summary>"Étape 2/3 — Nom" ou "Arrivée — Nom".</summary>
        public string StepTitle(int i)
        {
            if (i < 0 || i >= steps.Count) return "";
            var cp = steps[i];
            return IsArrival(i) ? "Arrivée — " + cp.displayName : $"Étape {i + 1}/{steps.Count - 1} — {cp.displayName}";
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>Etape dont le rayon contient le point (l'etape courante est prioritaire).</summary>
        public int StepContaining(Vector3 world)
        {
            if (!IsFinished && CurrentIndex < steps.Count && steps[CurrentIndex] &&
                Flat(world, steps[CurrentIndex].transform.position) <= steps[CurrentIndex].radiusMeters) return CurrentIndex;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] && Flat(world, steps[i].transform.position) <= steps[i].radiusMeters) return i;
            return -1;
        }

        string SiteLabel(Vector3 world)
        {
            int i = StepContaining(world);
            return i < 0 ? "" : StepTitle(i);
        }

        void OnSample(MarsSoilSampler.Sample sample)
        {
            if (!TryGetCurrent(out var cp)) return;
            int inside = StepContaining(sample.world);
            bool byRover = !perseverance || sample.vehicle == perseverance.VehicleName;
            if (inside == CurrentIndex && byRover && cp.requirement == MarsMissionCheckpoint.Requirement.PerseveranceSample)
                Complete($"prélèvement #{sample.id}");
            else if (inside > CurrentIndex)
                Event($"Prélèvement #{sample.id} enregistré dans « {steps[inside].displayName} », mais l'étape en cours est « {cp.displayName} ».");
            else if (inside < 0 && cp.requirement == MarsMissionCheckpoint.Requirement.PerseveranceSample)
                Event($"Prélèvement #{sample.id} hors étape : « {cp.displayName} » est à {Flat(sample.world, cp.transform.position):F0} m (rayon {cp.radiusMeters:F0} m).");
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f / checkHz;
            if (!TryGetCurrent(out var cp)) return;
            Vector3 c = cp.transform.position;
            switch (cp.requirement)
            {
                case MarsMissionCheckpoint.Requirement.PerseveranceArrival:
                    if (perseverance && perseverance.TryGetPose(out Vector3 rover, out _) && Flat(rover, c) <= cp.radiusMeters)
                        Complete("Perseverance sur place");
                    break;
                case MarsMissionCheckpoint.Requirement.AstronautArrival:
                    if (hud && hud.Head && Flat(hud.Head.position, c) <= cp.radiusMeters) Complete("astronaute sur place");
                    break;
            }
        }

        void Complete(string how)
        {
            var cp = steps[CurrentIndex];
            bool arrival = IsArrival(CurrentIndex);
            CurrentIndex++;
            Version++;
            Event(arrival || IsFinished
                ? $"Mission accomplie : « {cp.displayName} » atteint ({how})."
                : $"Étape validée : « {cp.displayName} » ({how}). Suivante : « {steps[CurrentIndex].displayName} ».");
        }

        void Event(string text)
        {
            LastEvent = text; LastEventTime = Time.unscaledTime;
            Debug.Log("[Fil rouge] " + text, this);
        }

        // ------------------------------------------------------------------ test sans casque
        [ContextMenu("TEST - Valider l'etape courante")]
        void TestComplete() { if (!IsFinished && steps.Count > 0) Complete("validation manuelle"); }
        [ContextMenu("TEST - Recommencer la mission")]
        void TestRestart() { Rebuild(); Event("Mission recommencée."); }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, .3f, .25f, .8f);
            Transform prev = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                if (!t.gameObject.activeSelf || !t.GetComponent<MarsMissionCheckpoint>()) continue;
                if (prev) Gizmos.DrawLine(prev.position + Vector3.up * .3f, t.position + Vector3.up * .3f);
                prev = t;
            }
        }
    }
}
