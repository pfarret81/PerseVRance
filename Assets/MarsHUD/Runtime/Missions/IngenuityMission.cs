using System.Collections.Generic;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 4 - Mission d'exploration d'Ingenuity commandee depuis le HUD.
    /// - Seul systeme qui deplace l'appareil pendant la session : le controleur manuel existant
    ///   (IngenuityFlightController) est desactive tant que ce composant est actif, puis restaure.
    ///   Le corps est rendu cinematique et deplace par Rigidbody.MovePosition (gravite du projet inchangee).
    /// - Le script de rotors existant est conserve : active en vol, desactive au sol.
    /// - Trajet calcule par IngenuityRoutePlanner : vol a 3 m du sol, contournement des obstacles, portee
    ///   de 250 m autour de Perseverance. Aucun segment droit de secours.
    /// - Decouverte continue commune (topographie + poussiere) via MarsDiscoveryState.TryRevealDisc.
    /// - A destination : vol stationnaire, en attente du prochain ordre.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class IngenuityMission : MonoBehaviour, IMarsVehicleMission
    {
        enum Flight { Landed, SpinUp, Flying, Hover }

        [Header("Appareil existant (prefab Ingenuity_Controller)")]
        public Rigidbody body;
        [Tooltip("IngenuityRotorController : actif en vol, desactive au sol.")]
        public Behaviour rotorController;
        [Tooltip("IngenuityFlightController (pilotage manuel) : desactive pendant le pilotage HUD pour qu'un seul systeme deplace l'appareil.")]
        public Behaviour manualFlightController;
        public bool stopRotorsOnGround = true;
        [Header("Systemes existants")]
        public MarsHUDController hud;
        public PerseveranceMission perseverance;
        [Header("Vol - valeurs validees (lot 4)")]
        public string vehicleName = "Ingenuity";
        public Color markerColor = new Color(.55f, 1f, .35f);
        [Tooltip("Vitesse horizontale de croisiere (m/s).")]
        [Min(.1f)] public float cruiseSpeed = 2f;
        [Tooltip("Vitesse verticale maximale (m/s).")]
        [Min(.1f)] public float verticalSpeed = 1.5f;
        [Min(.1f)] public float acceleration = 2f;
        [Min(10f)] public float yawSpeedDegrees = 90f;
        [Range(0f, 15f)] public float maxTiltDegrees = 7f;
        [Tooltip("Distance horizontale maximale entre Perseverance et tout point du vol (m).")]
        [Min(5f)] public float maxRangeFromPerseverance = 250f;
        [Min(0f)] public float rotorSpinUpSeconds = 1.5f;
        [Header("Decouverte continue (topographie + poussiere)")]
        [Min(1f)] public float discoveryRadius = 30f;
        [Min(0f)] public float initialRevealRadius = 40f;
        [Tooltip("Distance parcourue entre deux empreintes de decouverte (m).")]
        [Min(1f)] public float discoverySpacing = 7.5f;
        [Header("Depart")]
        public bool placeNextToPerseveranceAtStart = true;
        [Min(2f)] public float startDistanceFromPerseverance = 4f;
        [Tooltip("Correction de la hauteur posee (m) si les pieds du modele ne touchent pas exactement le sol.")]
        public float landedHeightOffset = 0f;
        [Header("Calcul")]
        [Range(.5f, 6f)] public float planningBudgetMilliseconds = 2.5f;
        [Tooltip("Racines ignorees par les controles d'obstacles (XR Origin, HUD...). Le rover n'y figure pas : il est contourne.")]
        public Transform[] obstacleIgnoreRoots = new Transform[0];
        [Tooltip("Racines a traiter comme sol en plus des TerrainCollider (ex. sol en maillage).")]
        public Transform[] extraGroundRoots = new Transform[0];
        public IngenuityRoutePlannerSettings planner = new IngenuityRoutePlannerSettings();
        [Header("Test sans casque (menu contextuel)")]
        public Transform editorTestTarget;

        readonly IngenuityRoutePlanner search = new IngenuityRoutePlanner();
        readonly Collider[] overlaps = new Collider[16];
        static readonly Vector3[] Empty = new Vector3[0];
        Transform[] ignoreBuffer = new Transform[0];
        Vector3[] route = Empty;
        float[] cumulative = new float[0], segmentSpeed = new float[0];
        Vector3[] pendingRoute;
        Flight flight = Flight.Landed;
        MarsOrderState state = MarsOrderState.Unavailable, beforePlanning;
        bool placed, controlTaken, restoreManual, restoreKinematic, paused, braking, refusal, pendingCommit;
        float s, v, spinUpRemaining, pivotHeight, lastAgl = float.NaN, nextAgl;
        int segment, routeVersion;
        Vector3 destination, pendingDestination;
        bool hasDestination;
        Vector2 lastReveal;
        bool hasReveal;
        string lastMessage = "", statusLine = "", placementNote = "";
        int statusKey = int.MinValue;
        float yaw;

        // ------------------------------------------------------------------ IMarsVehicleMission
        public string VehicleName => vehicleName;
        public Color MarkerColor => markerColor;
        public MarsVehicleKind Kind => MarsVehicleKind.Aerial;
        public bool RequiresKnownDestination => false;   // Ingenuity explore : destination inconnue autorisee
        public bool IsAvailable => body && hud && hud.Ready && placed && controlTaken && isActiveAndEnabled;
        public MarsOrderState OrderState => state;
        public string StatusLine => statusLine;
        public string LastMessage => lastMessage;
        public bool LastMessageIsRefusal => refusal;
        public bool CanPause => state == MarsOrderState.EnRoute;
        public bool CanResume => state == MarsOrderState.Paused;
        public bool HasDestination => hasDestination;
        public Vector3 Destination => destination;
        public IReadOnlyList<Vector3> Route => route;
        public int RouteProgressIndex => segment;
        public int RouteVersion => routeVersion;
        public IngenuityRoutePlanner Planner => search;
        public bool IsAirborne => flight != Flight.Landed;

        public bool TryGetPose(out Vector3 position, out Vector3 forward)
        {
            position = default; forward = Vector3.forward;
            if (!body || !placed) return false;
            position = body.transform.position;   // pose interpolee affichee
            forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return true;
        }

        // ------------------------------------------------------------------ cycle de vie
        void Awake()
        {
            if (!hud) hud = FindAnyObjectByType<MarsHUDController>();
            if (!perseverance) perseverance = FindAnyObjectByType<PerseveranceMission>();
        }

        void OnEnable() { TakeControl(); }

        void TakeControl()
        {
            if (controlTaken || !body) return;
            if (manualFlightController && manualFlightController.enabled) { manualFlightController.enabled = false; restoreManual = true; }
            restoreKinematic = !body.isKinematic;
            body.isKinematic = true;          // deplacement par MovePosition ; la gravite du projet n'est pas modifiee
            yaw = body.rotation.eulerAngles.y;
            pivotHeight = ComputePivotHeight();
            controlTaken = true;
            SetRotors(flight != Flight.Landed || !stopRotorsOnGround);
        }

        void OnDisable()
        {
            if (state == MarsOrderState.Planning) search.Cancel();
            if (!controlTaken || !body) return;
            controlTaken = false;
            if (restoreKinematic) body.isKinematic = false;
            if (restoreManual && manualFlightController) manualFlightController.enabled = true;
            restoreManual = restoreKinematic = false;
        }

        /// <summary>Hauteur du pivot au-dessus du point le plus bas du modele (pieds), mesuree dans la pose initiale.</summary>
        float ComputePivotHeight()
        {
            float min = float.PositiveInfinity;
            foreach (var r in body.GetComponentsInChildren<Renderer>()) if (r.enabled) min = Mathf.Min(min, r.bounds.min.y);
            if (float.IsPositiveInfinity(min))
                foreach (var c in body.GetComponentsInChildren<Collider>()) if (!c.isTrigger) min = Mathf.Min(min, c.bounds.min.y);
            return float.IsPositiveInfinity(min) ? .5f : Mathf.Max(0f, body.position.y - min);
        }

        /// <summary>Racines ignorees (XR Origin, HUD). Le rover n'est PAS ignore : il est contourne comme un obstacle.</summary>
        Transform[] IgnoreRoots() => obstacleIgnoreRoots ?? ignoreBuffer;

        void SetRotors(bool on) { if (rotorController && rotorController.enabled != on) rotorController.enabled = on; }

        // ------------------------------------------------------------------ placement initial
        void TryPlace()
        {
            if (!hud || !hud.Ready || !body) return;
            if (!placeNextToPerseveranceAtStart) { FinishPlacement(""); return; }
            if (!perseverance || !perseverance.follower || !perseverance.follower.IsReadyForRuntimePath) return;   // rover pas encore pose
            if (!perseverance.TryGetPose(out Vector3 rover, out Vector3 fwd)) return;
            fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // candidats autour du rover : droite, gauche, arriere-droite, arriere-gauche, arriere, avant-droite, avant-gauche
            Vector3[] dirs = { right, -right, (right - fwd).normalized, (-right - fwd).normalized, -fwd, (right + fwd).normalized, (-right + fwd).normalized };
            var ignore = IgnoreRoots();
            for (int k = 0; k < dirs.Length; k++)
            {
                for (float extra = 0f; extra <= 2.01f; extra += 1f)
                {
                    Vector3 p = rover + dirs[k] * (startDistanceFromPerseverance + extra);
                    if (!search.ProbeAt(new Vector2(p.x, p.z), rover.y + 50f, planner, body.transform, ignore, extraGroundRoots, out float gy, out float top)) continue;
                    if (top > gy + .15f) continue;                                    // rocher ou objet sous l'appareil
                    p.y = gy + pivotHeight + landedHeightOffset;
                    if (!SpotIsClear(p)) continue;                                   // le rover compte ici comme obstacle
                    body.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Quaternion.LookRotation(fwd).eulerAngles.y, 0f));
                    body.position = p; body.rotation = body.transform.rotation;
                    yaw = body.rotation.eulerAngles.y;
                    Physics.SyncTransforms();
                    FinishPlacement($"posé à {Vector3.Distance(new Vector3(rover.x, 0f, rover.z), new Vector3(p.x, 0f, p.z)):F1} m de Perseverance");
                    return;
                }
            }
            FinishPlacement("aucun emplacement dégagé près de Perseverance : position de la scène conservée");
        }

        bool SpotIsClear(Vector3 pivot)
        {
            float bottom = pivot.y - pivotHeight + .08f;
            float radius = Mathf.Max(.3f, planner.vehicleRadius);
            Vector3 a = new Vector3(pivot.x, bottom + radius, pivot.z), b = new Vector3(pivot.x, bottom + Mathf.Max(radius, pivotHeight * 2f), pivot.z);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (!c || c.transform.IsChildOf(body.transform) || c is CharacterController || c is TerrainCollider) continue;
                if (Contains(obstacleIgnoreRoots, c.transform)) continue;
                return false;
            }
            return true;
        }

        static bool Contains(Transform[] roots, Transform t)
        {
            if (roots != null) foreach (var r in roots) if (r && t.IsChildOf(r)) return true;
            return false;
        }

        void FinishPlacement(string note)
        {
            placed = true;
            flight = Flight.Landed;
            SetRotors(!stopRotorsOnGround);
            placementNote = note;
            Vector3 p = body.position;
            if (initialRevealRadius > 0f) Reveal(p, initialRevealRadius, true);
            state = MarsOrderState.Idle;
            SetMessage(string.IsNullOrEmpty(note) ? "Prêt au sol." : "Prêt au sol (" + note + ").", false);
        }

        // ------------------------------------------------------------------ ordres
        public bool RequestDestination(Vector3 worldPoint, out string refusalReason)
        {
            refusalReason = "";
            if (!Application.isPlaying) return Reject("ordres disponibles uniquement en mode Play.", out refusalReason);
            if (!IsAvailable) return Reject("Ingenuity non configuré ou pas encore posé.", out refusalReason);
            if (!perseverance || !perseverance.TryGetPose(out Vector3 rover, out _))
                return Reject("position de Perseverance indisponible : portée non vérifiable.", out refusalReason);
            float fromRover = Flat(rover, worldPoint);
            if (fromRover > maxRangeFromPerseverance)
                return Reject($"destination à {fromRover:F0} m de Perseverance : au-delà de la portée ({maxRangeFromPerseverance:F0} m).", out refusalReason);

            // Point de depart du calcul : position actuelle, ou point d'arret si l'appareil est en mouvement.
            if (state != MarsOrderState.Planning) beforePlanning = state;
            if (state == MarsOrderState.Planning) search.Cancel();
            pendingCommit = false;
            Vector3 from = body.position;
            if (flight == Flight.Flying && v > 0f && route.Length >= 2)
            {
                braking = true;
                from = Sample(Mathf.Min(s + v * v / (2f * acceleration), cumulative[cumulative.Length - 1]), out _, out _);
            }
            if (Flat(from, worldPoint) < 1f) { RestoreAfterRefusal(); return Reject("destination trop proche de l'appareil.", out refusalReason); }
            if (!search.Begin(from, worldPoint, rover, maxRangeFromPerseverance, verticalSpeed / cruiseSpeed, planner,
                    body.transform, IgnoreRoots(), extraGroundRoots, out string why))
            {
                RestoreAfterRefusal();
                return Reject(why, out refusalReason);
            }
            state = MarsOrderState.Planning;
            SetMessage(flight == Flight.Landed ? "Ordre reçu — calcul du plan de vol…" : "Ordre reçu — vol stationnaire pendant le calcul du plan de vol…", false);
            return true;
        }

        bool Reject(string why, out string reason)
        {
            reason = why;
            bool kept = state == MarsOrderState.EnRoute || state == MarsOrderState.Paused;
            SetMessage("Refusé : " + why + (kept ? " Vol précédent conservé." : ""), true);
            return false;
        }

        void RestoreAfterRefusal()
        {
            braking = false;
            state = beforePlanning == MarsOrderState.Planning ? (flight == Flight.Landed ? MarsOrderState.Idle : MarsOrderState.Completed) : beforePlanning;
            if (state == MarsOrderState.Paused) paused = true;
        }

        void PlanningDone()
        {
            if (search.State != IngenuityRoutePlanner.Status.Succeeded) { RestoreAfterRefusal(); Reject(search.Message, out _); return; }
            var list = search.Result;
            pendingRoute = new Vector3[list.Count];
            for (int i = 0; i < list.Count; i++) pendingRoute[i] = list[i];
            pendingDestination = pendingRoute[pendingRoute.Length - 1];
            pendingCommit = true;     // applique quand l'appareil est arrete au point de depart du calcul
        }

        void Commit()
        {
            pendingCommit = false;
            braking = false;
            pendingRoute[0] = body.position;          // ecart de quelques centimetres avec le point d'arret prevu
            SetRoute(pendingRoute);
            pendingRoute = null;
            destination = pendingDestination; hasDestination = true;
            paused = false;
            if (flight == Flight.Landed) { flight = Flight.SpinUp; spinUpRemaining = rotorSpinUpSeconds; SetRotors(true); }
            else if (flight == Flight.Hover) flight = Flight.Flying;    // SpinUp : le compte a rebours continue
            state = MarsOrderState.EnRoute;
            float detour = search.StraightDistance > .5f ? Mathf.Max(0f, search.ResultLength / search.StraightDistance - 1f) : 0f;
            SetMessage($"Plan de vol accepté : {search.ResultLength:F0} m, environ {EstimatedSeconds():F0} s" +
                       (detour > .03f ? $", contournement +{detour * 100f:F0} %" : ", sans contournement") +
                       $", calcul {search.ElapsedSeconds:F1} s.", false);
        }

        float EstimatedSeconds()
        {
            float t = 0f;
            for (int i = 1; i < route.Length; i++) t += (cumulative[i] - cumulative[i - 1]) / Mathf.Max(.05f, segmentSpeed[i - 1]);
            return t + (flight == Flight.SpinUp ? spinUpRemaining : 0f);
        }

        void SetRoute(Vector3[] points)
        {
            route = points;
            int n = points.Length;
            cumulative = new float[n];
            segmentSpeed = new float[Mathf.Max(1, n - 1)];
            for (int i = 1; i < n; i++)
            {
                Vector3 d = points[i] - points[i - 1];
                float len = d.magnitude, h = new Vector2(d.x, d.z).magnitude, dz = Mathf.Abs(d.y);
                cumulative[i] = cumulative[i - 1] + len;
                float limit = float.PositiveInfinity;
                if (h > 1e-4f) limit = Mathf.Min(limit, cruiseSpeed * len / h);
                if (dz > 1e-4f) limit = Mathf.Min(limit, verticalSpeed * len / dz);
                segmentSpeed[i - 1] = float.IsPositiveInfinity(limit) ? cruiseSpeed : limit;
            }
            s = 0f; v = 0f; segment = 0;
            routeVersion++;
        }

        public void PauseMission()
        {
            if (!CanPause) return;
            paused = true; state = MarsOrderState.Paused;
            SetMessage("Pause : vol stationnaire.", false);
        }

        public void ResumeMission()
        {
            if (!CanResume) return;
            paused = false; state = MarsOrderState.EnRoute;
            SetMessage("Reprise du vol.", false);
        }

        // ------------------------------------------------------------------ boucles
        void Update()
        {
            if (!placed) { TryPlace(); UpdateStatus(); return; }
            if (!IsAvailable) { UpdateStatus(); return; }
            if (state == MarsOrderState.Planning && !pendingCommit && search.StepSearch(planningBudgetMilliseconds)) PlanningDone();
            if (pendingCommit && (flight != Flight.Flying || v <= 1e-3f)) Commit();
            if (flight == Flight.SpinUp && !paused && (spinUpRemaining -= Time.deltaTime) <= 0f) flight = Flight.Flying;
            if (flight == Flight.Flying || flight == Flight.Hover) ContinuousDiscovery();
            if (Time.unscaledTime >= nextAgl && flight != Flight.Landed) { nextAgl = Time.unscaledTime + .5f; lastAgl = MeasureAgl(); }
            UpdateStatus();
        }

        void FixedUpdate()
        {
            if (!IsAvailable || flight != Flight.Flying || route.Length < 2) return;
            float dt = Time.fixedDeltaTime;
            float total = cumulative[cumulative.Length - 1];
            float target = (paused || braking || pendingCommit) ? 0f : segmentSpeed[Mathf.Clamp(segment, 0, segmentSpeed.Length - 1)];
            for (int j = segment + 1; j <= segment + 2 && j < segmentSpeed.Length; j++)   // anticipe les limites suivantes
                target = Mathf.Min(target, Mathf.Sqrt(segmentSpeed[j] * segmentSpeed[j] + 2f * acceleration * Mathf.Max(0f, cumulative[j] - s)));
            target = Mathf.Min(target, Mathf.Sqrt(2f * acceleration * Mathf.Max(0f, total - s)));
            v = Mathf.MoveTowards(v, target, acceleration * dt);
            s = Mathf.Min(total, s + v * dt);
            Vector3 p = Sample(s, out segment, out Vector3 dir);
            Vector2 horizontal = new Vector2(dir.x, dir.z);
            float hSpeed = v * horizontal.magnitude;
            if (horizontal.sqrMagnitude > .01f && v > .05f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, yawSpeedDegrees * dt);
            float tilt = maxTiltDegrees * Mathf.Clamp01(hSpeed / Mathf.Max(.1f, cruiseSpeed));
            body.MovePosition(p);
            body.MoveRotation(Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f));
            if (s >= total - 1e-3f && v < .02f && !pendingCommit && !braking) Arrive();
        }

        void Arrive()
        {
            flight = Flight.Hover; v = 0f;
            body.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
            Reveal(body.position, discoveryRadius, true);
            if (state == MarsOrderState.EnRoute) { state = MarsOrderState.Completed; SetMessage("Arrivé : vol stationnaire, zone cartographiée. En attente d'un ordre.", false); }
        }

        Vector3 Sample(float distance, out int seg, out Vector3 direction)
        {
            int n = route.Length;
            seg = Mathf.Clamp(segment, 0, n - 2);
            while (seg < n - 2 && cumulative[seg + 1] < distance) seg++;
            while (seg > 0 && cumulative[seg] > distance) seg--;
            float len = cumulative[seg + 1] - cumulative[seg];
            float t = len > 1e-5f ? Mathf.Clamp01((distance - cumulative[seg]) / len) : 1f;
            direction = len > 1e-5f ? (route[seg + 1] - route[seg]) / len : Vector3.zero;
            return Vector3.LerpUnclamped(route[seg], route[seg + 1], t);
        }

        // ------------------------------------------------------------------ decouverte
        void ContinuousDiscovery()
        {
            Vector3 p = body.position;
            Vector2 xz = new Vector2(p.x, p.z);
            if (hasReveal && (xz - lastReveal).sqrMagnitude < discoverySpacing * discoverySpacing) return;
            Reveal(p, discoveryRadius, false);
        }

        void Reveal(Vector3 world, float radius, bool force)
        {
            lastReveal = new Vector2(world.x, world.z); hasReveal = true;
            if (!hud || !hud.Ready) return;
            Vector2 data = hud.Coordinates.WorldToData(world);
            if (!hud.Catalog.Contains(data)) return;          // hors emprise : rien a reveler
            if (!hud.Discovery.TryRevealDisc(data, radius) && force)
                Debug.LogWarning("[Ingenuity] Découverte refusée : " + hud.Discovery.LastRefusal, this);
        }

        float MeasureAgl()
        {
            if (state == MarsOrderState.Planning) return lastAgl;   // tampons du planificateur occupes
            Vector3 p = body.position;
            if (!search.ProbeAt(new Vector2(p.x, p.z), p.y + 50f, planner, body.transform, IgnoreRoots(), extraGroundRoots, out float gy, out _)) return float.NaN;
            return p.y - gy;
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // ------------------------------------------------------------------ etat affiche
        void SetMessage(string message, bool isRefusal)
        {
            lastMessage = message; refusal = isRefusal; statusKey = int.MinValue;
            if (isRefusal) Debug.LogWarning("[Ingenuity] " + message, this); else Debug.Log("[Ingenuity] " + message, this);
        }

        void UpdateStatus()
        {
            float total = cumulative.Length > 0 ? cumulative[cumulative.Length - 1] : 0f;
            int percent = total > 0f ? Mathf.Clamp(Mathf.RoundToInt(100f * s / total), 0, 100) : 0;
            int agl = float.IsNaN(lastAgl) ? -1 : Mathf.RoundToInt(lastAgl * 10f);
            int detail = state == MarsOrderState.Planning ? search.Expansions / 500 : percent * 1000 + agl;
            int key = ((int)state << 26) ^ ((int)flight << 22) ^ detail ^ (placed ? 1 << 30 : 0);
            if (key == statusKey) return;
            statusKey = key;
            string alt = agl >= 0 ? $", {lastAgl:F1} m du sol" : "";
            if (!placed) { statusLine = body ? "Mise en place près de Perseverance…" : "Indisponible : appareil non assigné"; return; }
            if (!IsAvailable) { statusLine = "Indisponible : Ingenuity non configuré"; return; }
            switch (state)
            {
                case MarsOrderState.Idle: statusLine = flight == Flight.Landed ? "Au sol — prêt" : "Vol stationnaire — prêt" + alt; break;
                case MarsOrderState.Planning: statusLine = $"Calcul du plan de vol… ({search.Expansions} cellules)"; break;
                case MarsOrderState.EnRoute: statusLine = flight == Flight.SpinUp ? "Démarrage des rotors…" : $"En vol — {percent} % de {total:F0} m" + alt; break;
                case MarsOrderState.Paused: statusLine = "En pause — vol stationnaire" + alt; break;
                case MarsOrderState.Completed: statusLine = "Vol stationnaire à destination" + alt; break;
                default: statusLine = state.ToString(); break;
            }
        }

        // ------------------------------------------------------------------ test sans casque
        [ContextMenu("TEST - Envoyer un ordre vers Editor Test Target")]
        void TestOrder()
        {
            if (!editorTestTarget) { Debug.LogWarning("Assigner Editor Test Target (objet vide pose sur le terrain).", this); return; }
            RequestDestination(editorTestTarget.position, out _);
        }
        [ContextMenu("TEST - Pause")] void TestPause() { PauseMission(); }
        [ContextMenu("TEST - Reprendre")] void TestResume() { ResumeMission(); }
        [ContextMenu("TEST - Rapport")]
        void TestReport()
        {
            Debug.Log($"[Ingenuity] Etat={state}, vol={flight}, pose={(body ? body.position.ToString("F2") : "-")}, sol={lastAgl:F2} m, " +
                      $"statut='{statusLine}', message='{lastMessage}', trajet={route.Length} points, planificateur: {search.Message}, " +
                      $"controleur manuel {(manualFlightController ? (manualFlightController.enabled ? "ACTIF (conflit)" : "desactive") : "absent")}", this);
        }
    }
}
