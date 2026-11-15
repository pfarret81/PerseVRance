using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD
{
    /// <summary>
    /// Lot 3 - Adaptateur d'ordres entre le HUD et le rover EXISTANT.
    /// Ne deplace jamais RoverRoot : il planifie un itineraire verifie (RoverRoutePlanner), le pose sur un
    /// RoverWaypointPath runtime independant du rover, puis le confie au RoverWaypointFollower via
    /// TryAdoptPathFromCurrentPose. N'utilise ni RestartFromReference, ni reflexion, ni segment droit de secours.
    /// Le Y des points est la hauteur sondee du sol, uniquement informative (pas une consigne d'altitude).
    /// Lot 5 : le prelevement est lance a la demande (RequestSample) quand le rover est a l'arret ;
    /// le prelevement automatique a l'arrivee reste disponible en option.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class PerseveranceMission : MonoBehaviour, IMarsVehicleMission
    {
        [Header("Systemes existants")]
        public RoverWaypointFollower follower;
        public MarsHUDController hud;
        public MarsSoilSampler sampler;
        [Header("Ordres - choix du prototype")]
        public string vehicleName = "Perseverance";
        public Color markerColor = new Color(1f, .62f, .18f);
        [Tooltip("Distance horizontale maximale d'une destination depuis le rover.")]
        [Min(1f)] public float maxDestinationDistance = 80f;
        [Tooltip("Temps de calcul maximal par image (ms). 72 Hz = 13,9 ms par image.")]
        [Range(.5f, 6f)] public float planningBudgetMilliseconds = 2.5f;
        [Tooltip("Racines ignorees par le controle d'obstacles (XR Origin, HUD...).")]
        public Transform[] obstacleIgnoreRoots = new Transform[0];
        public RoverRoutePlannerSettings planner = new RoverRoutePlannerSettings();
        [Tooltip("Lot 5 : false = le prelevement se lance depuis le HUD (bouton Prelever). true = automatique a l'arrivee (lot 3).")]
        public bool autoSampleOnArrival = false;
        [Header("Test sans casque (menu contextuel)")]
        public Transform editorTestTarget;

        readonly RoverRoutePlanner search = new RoverRoutePlanner();
        readonly List<RoverWaypointPath> ownedPaths = new List<RoverWaypointPath>(4);
        readonly List<Vector3> wheelBuffer = new List<Vector3>(6);
        static readonly Vector3[] Empty = new Vector3[0];
        Vector3[] route = Empty;
        float[] routeCumulative = new float[0];
        RoverWaypointPath displayedPath, missionPath;
        MarsOrderState state = MarsOrderState.Unavailable, stateBeforePlanning, resumeTo;
        bool resumeFollowerOnRefusal, pausedByUser, refusal, arrivedNotSampled;
        float workRemaining;
        int routeIndex, routeVersion, routeCounter;
        string lastMessage = "", statusLine = "";
        int statusKey = int.MinValue;
        Vector3 destination;
        bool hasDestination;

        public string VehicleName => vehicleName;
        public Color MarkerColor => markerColor;
        public MarsVehicleKind Kind => MarsVehicleKind.Ground;
        public bool RequiresKnownDestination => true;
        public bool IsAvailable => follower && follower.Actuator && follower.Actuator.Solver && follower.isActiveAndEnabled;
        public MarsOrderState OrderState => state;
        public string StatusLine => statusLine;
        public string LastMessage => lastMessage;
        public bool LastMessageIsRefusal => refusal;
        public bool HasDestination => hasDestination;
        public Vector3 Destination => destination;
        public IReadOnlyList<Vector3> Route => route;
        public int RouteProgressIndex => routeIndex;
        public int RouteVersion => routeVersion;
        public RoverRoutePlanner Planner => search;
        public bool CanPause => state == MarsOrderState.EnRoute || state == MarsOrderState.Working;
        public bool CanResume => state == MarsOrderState.Paused;
        /// <summary>Prelevement possible : rover pret, a l'arret, sans ordre ni prelevement en cours.</summary>
        public bool CanSample => IsAvailable && sampler && follower.IsReadyForRuntimePath &&
            (state == MarsOrderState.Idle || state == MarsOrderState.Completed) &&
            follower.State != RoverWaypointFollower.FollowState.Following && follower.State != RoverWaypointFollower.FollowState.Fault;

        /// <summary>Lance un prelevement a la position reelle du rover. Ne deplace pas le rover.</summary>
        public bool RequestSample(out string refusalReason)
        {
            refusalReason = "";
            if (!Application.isPlaying) return Reject("Prélèvement disponible uniquement en mode Play.", out refusalReason);
            if (!IsAvailable || !sampler) return Reject("Rover ou registre de prélèvements non configuré.", out refusalReason);
            if (follower.State == RoverWaypointFollower.FollowState.Fault) return Reject("Rover en défaut (" + follower.FaultReason + ").", out refusalReason);
            if (!follower.IsReadyForRuntimePath) return Reject("Suivi du rover non initialisé.", out refusalReason);
            if (state == MarsOrderState.Working) return Reject("Prélèvement déjà en cours.", out refusalReason);
            if (!CanSample) return Reject("Le rover doit être à l'arrêt, sans ordre en cours.", out refusalReason);
            state = MarsOrderState.Working;
            arrivedNotSampled = false;
            workRemaining = sampler.sampleDurationSeconds;
            SetMessage($"Prélèvement lancé — {workRemaining:F0} s.", false);
            return true;
        }

        void Awake()
        {
            if (!hud) hud = FindAnyObjectByType<MarsHUDController>();
            if (!sampler && hud) sampler = hud.GetComponent<MarsSoilSampler>();
        }

        public bool TryGetPose(out Vector3 position, out Vector3 forward)
        {
            position = default; forward = Vector3.forward;
            if (!IsAvailable) return false;
            var a = follower.Actuator;
            position = a.CenterPosition;
            forward = a.Heading * Vector3.forward;
            return true;
        }

        // ------------------------------------------------------------------ ordres
        public bool RequestDestination(Vector3 worldPoint, out string refusalReason)
        {
            refusalReason = "";
            if (!Application.isPlaying) return Reject("Ordres disponibles uniquement en mode Play.", out refusalReason);
            if (!IsAvailable) return Reject("Rover non configuré.", out refusalReason);
            if (follower.State == RoverWaypointFollower.FollowState.Fault)
                return Reject("Rover en défaut (" + follower.FaultReason + ").", out refusalReason);
            if (!follower.IsReadyForRuntimePath) return Reject("Suivi du rover non initialisé.", out refusalReason);
            Vector3 center = follower.Actuator.CenterPosition;
            float distance = RoverWaypointPath.HorizontalDistance(center, worldPoint);
            if (distance > maxDestinationDistance)
                return Reject($"Destination à {distance:F0} m : au-delà de la portée d'un ordre ({maxDestinationDistance:F0} m).", out refusalReason);
            if (distance < planner.goalTolerance)
                return Reject("Destination trop proche de la position actuelle du rover.", out refusalReason);

            if (state == MarsOrderState.Planning) search.Cancel();       // un nouvel ordre remplace le calcul en cours
            else
            {
                stateBeforePlanning = state;
                resumeFollowerOnRefusal = follower.State == RoverWaypointFollower.FollowState.Following;
            }
            // Pose figee pendant le calcul : le premier point du trajet doit correspondre au milieu reel des
            // roues centrales lors de l'adoption (tolerance du follower 0,10 m).
            follower.Pause();
            Physics.SyncTransforms();
            center = follower.Actuator.CenterPosition;
            var solver = follower.Actuator.Solver;
            wheelBuffer.Clear();
            for (int i = 0; i < solver.WheelCount; i++) if (solver.WheelCenter(i)) wheelBuffer.Add(solver.WheelCenter(i).position);
            if (!search.Begin(solver, follower.transform, center, follower.Actuator.Heading * Vector3.forward,
                    follower.Actuator.MinCurvature, follower.Actuator.MaxCurvature, follower.Actuator.Curvature,
                    wheelBuffer, worldPoint, planner, obstacleIgnoreRoots, out string why))
            {
                RestoreAfterRefusal();
                return Reject(why, out refusalReason);
            }
            state = MarsOrderState.Planning;
            SetMessage("Ordre reçu — calcul d'un itinéraire traversable…", false);
            return true;
        }

        bool Reject(string why, out string reason)
        {
            reason = why;
            SetMessage("Refusé : " + why + (HasActiveMission() ? " Mission précédente conservée." : ""), true);
            return false;
        }
        bool HasActiveMission() => state == MarsOrderState.EnRoute || state == MarsOrderState.Paused || state == MarsOrderState.Working;

        void RestoreAfterRefusal()
        {
            state = stateBeforePlanning == MarsOrderState.Planning ? MarsOrderState.Idle : stateBeforePlanning;
            if (resumeFollowerOnRefusal) follower.Resume();
        }

        void Commit()
        {
            arrivedNotSampled = false;
            var go = new GameObject("Perseverance mission route #" + (++routeCounter));
            SceneManager.MoveGameObjectToScene(go, follower.gameObject.scene);
            var candidate = go.AddComponent<RoverWaypointPath>();
            string why;
            if (!candidate.TrySetWorldPoints(search.Result, out why) ||
                !follower.TryAdoptPathFromCurrentPose(candidate, true, out why))
            {
                Destroy(go);
                RestoreAfterRefusal();
                Reject("trajet calculé mais refusé par le suivi du rover (" + why + ").", out _);
                return;
            }
            ownedPaths.Add(candidate);
            CleanupUnusedPaths();
            missionPath = candidate;
            pausedByUser = false;
            state = MarsOrderState.EnRoute;
            RefreshDisplayedRoute(true);
            destination = route[route.Length - 1];
            hasDestination = true;
            SetMessage($"Ordre accepté : {candidate.Length:F1} m, écart final {search.GoalMiss:F2} m, calcul {search.ElapsedSeconds:F1} s.", false);
        }

        void CleanupUnusedPaths()
        {
            for (int i = ownedPaths.Count - 1; i >= 0; i--)
            {
                var p = ownedPaths[i];
                if (!p) { ownedPaths.RemoveAt(i); continue; }
                if (p.IsInUse || p == follower.CurrentPath) continue;
                Destroy(p.gameObject); ownedPaths.RemoveAt(i);
            }
        }

        public void PauseMission()
        {
            if (!CanPause) return;
            resumeTo = state;
            if (state == MarsOrderState.EnRoute) follower.Pause();
            pausedByUser = true;
            state = MarsOrderState.Paused;
            SetMessage("Pause demandée.", false);
        }

        public void ResumeMission()
        {
            if (!CanResume) return;
            pausedByUser = false;
            state = resumeTo;
            if (state == MarsOrderState.EnRoute) follower.Resume();
            SetMessage("Reprise.", false);
        }

        // ------------------------------------------------------------------ boucle
        void Update()
        {
            if (!IsAvailable) { state = MarsOrderState.Unavailable; UpdateStatus(); return; }
            var fs = follower.State;
            if (fs == RoverWaypointFollower.FollowState.Fault && state != MarsOrderState.Fault)
            {
                if (state == MarsOrderState.Planning) search.Cancel();
                state = MarsOrderState.Fault;
                SetMessage("Défaut du suivi : " + follower.FaultReason, true);
            }
            switch (state)
            {
                case MarsOrderState.Unavailable:
                    state = MarsOrderState.Idle; break;
                case MarsOrderState.Planning:
                    if (search.StepSearch(planningBudgetMilliseconds))
                    {
                        if (search.State == RoverRoutePlanner.Status.Succeeded) Commit();
                        else { RestoreAfterRefusal(); Reject(search.Message, out _); }
                    }
                    break;
                case MarsOrderState.EnRoute:
                    if (follower.CurrentPath != missionPath)
                    {
                        state = fs == RoverWaypointFollower.FollowState.Following ? MarsOrderState.EnRoute : MarsOrderState.Idle;
                        missionPath = null;
                    }
                    else if (fs == RoverWaypointFollower.FollowState.Arrived)
                    {
                        if (autoSampleOnArrival)
                        {
                            state = MarsOrderState.Working;
                            workRemaining = sampler ? sampler.sampleDurationSeconds : 0f;
                            SetMessage("Arrivé — prélèvement en cours.", false);
                        }
                        else
                        {
                            state = MarsOrderState.Idle; arrivedNotSampled = true;
                            SetMessage("Arrivé à destination. Touchez « Prélever » pour analyser le sol.", false);
                        }
                    }
                    else if (fs == RoverWaypointFollower.FollowState.Paused && !pausedByUser)
                        state = MarsOrderState.Idle;   // pause externe (inspecteur) : on n'invente pas d'etat
                    break;
                case MarsOrderState.Working:
                    workRemaining -= Time.deltaTime;
                    if (workRemaining <= 0f) FinishSample();
                    break;
                case MarsOrderState.Idle:
                case MarsOrderState.Completed:
                    if (fs == RoverWaypointFollower.FollowState.Following && follower.CurrentPath != missionPath)
                        state = MarsOrderState.EnRoute; // parcours de scene lance hors HUD
                    break;
                case MarsOrderState.Fault:
                    if (fs != RoverWaypointFollower.FollowState.Fault) { state = MarsOrderState.Idle; SetMessage("Défaut levé.", false); }
                    break;
            }
            RefreshDisplayedRoute(false);
            UpdateRouteIndex();
            UpdateStatus();
        }

        void FinishSample()
        {
            if (sampler && sampler.TryCreateSample(follower.Actuator.CenterPosition, vehicleName, out var sample))
            {
                state = MarsOrderState.Completed;
                SetMessage($"Prélèvement #{sample.id} terminé : {sampler.Highlight(sample)}. Résultat sur la carte SOL.", false);
            }
            else
            {
                state = MarsOrderState.Completed;
                SetMessage("Prélèvement impossible : " + (sampler ? sampler.LastRefusal : "registre absent."), true);
            }
        }

        void RefreshDisplayedRoute(bool force)
        {
            var current = follower.CurrentPath;
            if (!force && current == displayedPath) return;
            displayedPath = current;
            route = current ? current.CopyCachedWorldPoints() ?? Empty : Empty;   // copie unique par changement de trajet
            if (routeCumulative.Length != route.Length) routeCumulative = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                routeCumulative[i] = routeCumulative[i - 1] + RoverWaypointPath.HorizontalDistance(route[i - 1], route[i]);
            routeIndex = 0;
            routeVersion++;
            if (current != missionPath) hasDestination = false;
        }

        void UpdateRouteIndex()
        {
            float progress = follower.Progress;
            while (routeIndex < route.Length - 1 && routeCumulative[routeIndex + 1] <= progress) routeIndex++;
        }

        void SetMessage(string message, bool isRefusal)
        {
            lastMessage = message; refusal = isRefusal;
            statusKey = int.MinValue;
            if (isRefusal) Debug.LogWarning("[Perseverance] " + message, this);
            else Debug.Log("[Perseverance] " + message, this);
        }

        void UpdateStatus()
        {
            int percent = follower && follower.PathLength > 0f ? Mathf.Clamp(Mathf.RoundToInt(100f * follower.Progress / follower.PathLength), 0, 100) : 0;
            int detail = state == MarsOrderState.Planning ? search.Expansions / 250 :
                         state == MarsOrderState.Working ? Mathf.CeilToInt(workRemaining) : percent;
            int key = ((int)state << 24) ^ detail ^ (arrivedNotSampled ? 1 << 30 : 0);
            if (key == statusKey) return;          // reconstruction de la chaine seulement quand l'affichage change
            statusKey = key;
            float length = follower ? follower.PathLength : 0f;
            switch (state)
            {
                case MarsOrderState.Unavailable: statusLine = "Indisponible : rover non configuré"; break;
                case MarsOrderState.Idle: statusLine = arrivedNotSampled ? "Arrivé — prêt à prélever" : "À l'arrêt — prêt"; break;
                case MarsOrderState.Planning: statusLine = $"Calcul d'itinéraire… ({search.Expansions} essais)"; break;
                case MarsOrderState.EnRoute: statusLine = $"En route — {percent} % de {length:F0} m"; break;
                case MarsOrderState.Paused: statusLine = resumeTo == MarsOrderState.Working ? "En pause — prélèvement suspendu" : $"En pause — {percent} %"; break;
                case MarsOrderState.Working: statusLine = $"Prélèvement en cours — {Mathf.CeilToInt(Mathf.Max(0f, workRemaining))} s"; break;
                case MarsOrderState.Completed: statusLine = "Prélèvement terminé — prêt"; break;
                case MarsOrderState.Fault: statusLine = "Défaut : " + (follower ? follower.FaultReason : ""); break;
            }
        }

        void OnDisable() { if (state == MarsOrderState.Planning) { search.Cancel(); RestoreAfterRefusal(); } }

        // ------------------------------------------------------------------ test sans casque
        [ContextMenu("TEST - Envoyer un ordre vers Editor Test Target")]
        void TestOrder()
        {
            if (!editorTestTarget) { Debug.LogWarning("Assigner Editor Test Target (un objet vide pose sur le terrain).", this); return; }
            RequestDestination(editorTestTarget.position, out _);
        }
        [ContextMenu("TEST - Prelever ici")] void TestSample() { RequestSample(out _); }
        [ContextMenu("TEST - Pause")] void TestPause() { PauseMission(); }
        [ContextMenu("TEST - Reprendre")] void TestResume() { ResumeMission(); }
        [ContextMenu("TEST - Rapport")]
        void TestReport()
        {
            Debug.Log($"[Perseverance] Etat={state}, suivi={(follower ? follower.State.ToString() : "-")}, statut='{statusLine}', " +
                      $"message='{lastMessage}', trajet={route.Length} points, planificateur: {search.Message}", this);
        }
    }
}
