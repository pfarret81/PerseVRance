using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Lot 3 - Planificateur d'itineraire du prototype (pas un planificateur NASA).
// Recherche A* ponderee sur des arcs de cercle en MARCHE AVANT uniquement, dont la courbure reste
// dans le domaine de direction de RoverKinematicActuator (avec marge). Chaque arc est verifie par
// des sondages verticaux sur la liste blanche du solveur (TryProbeNavigationGround) sous les six roues,
// puis par un controle de volume (OverlapBox) contre les colliders hors liste blanche.
// Le planificateur ne deplace rien, n'appelle pas le solveur de suspension et ne modifie pas le follower.
// Il n'ajoute JAMAIS de segment droit de secours : sans itineraire verifie, la demande est refusee.
// Execution decoupee en tranches (Step) sur le thread principal, car l'API Physics l'exige.

[Serializable]
public sealed class RoverRoutePlannerSettings
{
    [Header("Primitives - marche avant uniquement")]
    [Min(.2f)] public float primitiveLength = 1.0f;
    [Min(.05f)] public float checkSpacing = .25f;
    [Min(.1f)] public float outputSpacing = .40f;
    [Range(3, 9)] public int curvatureSamples = 5;
    [Tooltip("Fraction du domaine de courbure de l'actionneur utilisee par le planificateur.")]
    [Range(.3f, 1f)] public float curvatureMargin = .75f;
    [Header("Discretisation")]
    [Min(.1f)] public float cellSize = .5f;
    [Range(8, 72)] public int headingBins = 32;
    [Min(.05f)] public float probeCell = .15f;
    [Header("Criteres de franchissement - choix du prototype")]
    [Tooltip("Inclinaison maximale du plan des six roues. Plafonnee sous l'angle du solveur.")]
    [Range(1f, 40f)] public float maxTiltDegrees = 12f;
    [Tooltip("Pente locale maximale sous chaque roue (normale du sol).")]
    [Range(5f, 80f)] public float maxSurfaceSlopeDegrees = 30f;
    [Tooltip("Ecart maximal d'une roue au plan moyen (articulation rocker-bogie absorbee).")]
    [Min(.01f)] public float maxWheelResidual = .12f;
    [Tooltip("Variation maximale de hauteur d'une roue entre deux controles successifs.")]
    [Min(.01f)] public float maxWheelStep = .10f;
    [Header("Obstacles hors liste blanche")]
    public LayerMask obstacleMask = ~0;
    [Min(0f)] public float bodyClearance = .35f;
    [Min(.1f)] public float bodyHeight = 1.4f;
    [Min(0f)] public float footprintMargin = .15f;
    [Header("Recherche")]
    [Tooltip("Distance horizontale finale acceptee entre la fin du trajet et le point designe.")]
    [Min(.1f)] public float goalTolerance = .6f;
    [Tooltip("Nombre de primitives droites (verifiees) exigees en fin de trajet : le suivi pure pursuit\n" +
             "du follower converge ainsi sur le dernier point (tolerance d'arrivee 0,08 m).")]
    [Range(0, 3)] public int finalStraightPrimitives = 1;
    [Min(1f)] public float searchMargin = 25f;
    [Min(100)] public int maxExpansions = 20000;
    [Min(.5f)] public float maxSeconds = 20f;
    [Range(1f, 3f)] public float heuristicWeight = 1.5f;
    [Min(0f)] public float curvatureCost = .4f;
    [Min(0f)] public float curvatureChangeCost = .6f;
    [Min(0f)] public float tiltCost = 1.5f;
}

public sealed class RoverRoutePlanner
{
    public enum Status { Idle, Running, Succeeded, Failed }
    public Status State { get; private set; } = Status.Idle;
    public string Message { get; private set; } = "";
    public int Expansions { get; private set; }
    public int ProbeCount { get; private set; }
    public float ElapsedSeconds => (float)total.Elapsed.TotalSeconds;
    public IReadOnlyList<Vector3> Result => result;
    public float ResultLength { get; private set; }
    public float GoalMiss { get; private set; }
    public Vector3 Goal { get; private set; }

    struct Node { public float x, z, yaw, k, g; public int parent, straight; }
    struct Probe { public bool ok; public float y; public float slope; }
    struct HeapItem { public float f; public int node; }

    const int NoGround = 0, Slope = 1, Tilt = 2, Step = 3, Obstacle = 4, Domain = 5;
    static readonly string[] ReasonNames = { "sol absent ou hors liste blanche", "pente locale", "inclinaison du rover",
        "marche / rupture de hauteur", "obstacle", "hors domaine de recherche" };

    readonly List<Node> nodes = new List<Node>(8192);
    readonly List<HeapItem> heap = new List<HeapItem>(8192);
    readonly Dictionary<long, float> bestG = new Dictionary<long, float>(16384);
    readonly Dictionary<long, Probe> probes = new Dictionary<long, Probe>(65536);
    readonly Dictionary<EntityId, bool> obstacleCache = new Dictionary<EntityId, bool>(256);
    readonly List<Vector3> result = new List<Vector3>(512);
    readonly Collider[] overlap = new Collider[48];
    readonly int[] rejections = new int[6];
    readonly Stopwatch total = new Stopwatch();
    readonly Stopwatch slice = new Stopwatch();

    Vector2[] wheels = new Vector2[0];          // x lateral (droite), y longitudinal (avant), repere du rover
    float[] heightsA = new float[0], heightsB = new float[0];
    float[] curvatures = new float[0];
    double n00, n01, n02, n11, n12, n22;        // inverse de la matrice normale du plan h = a + b x + c z
    float halfWidth, halfLength, kAbsMax, maxTiltTan, maxSurfaceSlope;
    RoverStaticPoseTest solver;
    Transform roverRoot;
    Transform[] ignoreRoots;
    Bounds surfaceBounds;
    Rect domain;
    Vector2 goal;
    Vector3 startCenter;
    float startCurvature;
    RoverRoutePlannerSettings s;
    int closestNode;
    float closestDistance;

    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>
    /// Prepare une recherche. Ne deplace rien. Retourne false avec un motif si la demande est refusee d'emblee.
    /// </summary>
    public bool Begin(RoverStaticPoseTest poseSolver, Transform rover, Vector3 center, Vector3 forward,
        float minCurvature, float maxCurvature, float currentCurvature, IReadOnlyList<Vector3> wheelWorldCenters,
        Vector3 goalWorld, RoverRoutePlannerSettings settings, Transform[] ignore, out string refusal)
    {
        Reset();
        refusal = "";
        s = settings ?? new RoverRoutePlannerSettings();
        solver = poseSolver; roverRoot = rover; ignoreRoots = ignore;
        Goal = goalWorld;
        if (!solver || !rover) return Refuse("Solveur ou rover absent.", out refusal);
        if (!Finite(center.x) || !Finite(center.z) || !Finite(goalWorld.x) || !Finite(goalWorld.z))
            return Refuse("Coordonnees non finies.", out refusal);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return Refuse("Cap du rover indetermine.", out refusal);
        forward.Normalize();
        if (wheelWorldCenters == null || wheelWorldCenters.Count < 4) return Refuse("Geometrie des roues indisponible.", out refusal);
        if (!(maxCurvature > 0f) || !(minCurvature < 0f)) return Refuse("Domaine de courbure de l'actionneur invalide.", out refusal);
        if (!solver.TryGetNavigationSurfaceBounds(out surfaceBounds, out string boundsRefusal))
            return Refuse(boundsRefusal, out refusal);

        // Empreinte des roues dans le repere du cap courant (lecture seule des transforms).
        Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up));
        int count = wheelWorldCenters.Count;
        if (wheels.Length != count) { wheels = new Vector2[count]; heightsA = new float[count]; heightsB = new float[count]; }
        halfWidth = 0f; halfLength = 0f;
        double s00 = 0, s01 = 0, s02 = 0, s11 = 0, s12 = 0, s22 = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 local = inv * (wheelWorldCenters[i] - center);
            wheels[i] = new Vector2(local.x, local.z);
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
            halfLength = Mathf.Max(halfLength, Mathf.Abs(local.z));
            s00 += 1; s01 += local.x; s02 += local.z; s11 += local.x * local.x; s12 += local.x * local.z; s22 += local.z * local.z;
        }
        double det = s00 * (s11 * s22 - s12 * s12) - s01 * (s01 * s22 - s12 * s02) + s02 * (s01 * s12 - s11 * s02);
        if (Math.Abs(det) < 1e-9) return Refuse("Geometrie des roues degeneree pour l'ajustement de plan.", out refusal);
        n00 = (s11 * s22 - s12 * s12) / det; n01 = (s02 * s12 - s01 * s22) / det; n02 = (s01 * s12 - s02 * s11) / det;
        n11 = (s00 * s22 - s02 * s02) / det; n12 = (s01 * s02 - s00 * s12) / det; n22 = (s00 * s11 - s01 * s01) / det;
        halfWidth += solver.WheelRadius + s.footprintMargin;
        halfLength += solver.WheelRadius + s.footprintMargin;

        float tilt = Mathf.Min(s.maxTiltDegrees, Mathf.Max(1f, solver.NavigationMaxPoseAngle - 3f));
        maxTiltTan = Mathf.Tan(tilt * Mathf.Deg2Rad);
        maxSurfaceSlope = s.maxSurfaceSlopeDegrees;

        float kmin = minCurvature * s.curvatureMargin, kmax = maxCurvature * s.curvatureMargin;
        kAbsMax = Mathf.Max(Mathf.Abs(kmin), kmax);
        int samples = Mathf.Max(3, s.curvatureSamples | 1);
        if (curvatures.Length != samples) curvatures = new float[samples];
        int half = samples / 2;
        for (int i = 0; i < samples; i++)
        {
            int j = i - half;
            curvatures[i] = j < 0 ? kmin * (-j) / half : j > 0 ? kmax * j / half : 0f;
        }
        startCurvature = Mathf.Clamp(currentCurvature, kmin, kmax);

        // Destination : sol autorise sous le point, pente admissible.
        goal = new Vector2(goalWorld.x, goalWorld.z);
        if (!surfaceBounds.Contains(new Vector3(goal.x, surfaceBounds.center.y, goal.y)))
            return Refuse("Destination hors des surfaces de roulage.", out refusal);
        Probe gp = ProbeAt(goal.x, goal.y);
        if (!gp.ok) return Refuse("Pas de sol de roulage autorise sous la destination.", out refusal);
        if (gp.slope > maxSurfaceSlope) return Refuse($"Destination sur une pente locale trop forte ({gp.slope:F0} deg).", out refusal);

        startCenter = center;
        Vector2 start = new Vector2(center.x, center.z);
        float m = s.searchMargin;
        float xMin = Mathf.Max(Mathf.Min(start.x, goal.x) - m, surfaceBounds.min.x);
        float xMax = Mathf.Min(Mathf.Max(start.x, goal.x) + m, surfaceBounds.max.x);
        float zMin = Mathf.Max(Mathf.Min(start.y, goal.y) - m, surfaceBounds.min.z);
        float zMax = Mathf.Min(Mathf.Max(start.y, goal.y) + m, surfaceBounds.max.z);
        domain = Rect.MinMaxRect(xMin, zMin, xMax, zMax);

        Node root = new Node { x = center.x, z = center.z, yaw = Mathf.Atan2(forward.x, forward.z), k = startCurvature, g = 0f, parent = -1 };
        nodes.Add(root);
        bestG[Key(root.x, root.z, root.yaw, 0)] = 0f;
        Push(Heuristic(root.x, root.z) * s.heuristicWeight, 0);
        closestNode = 0; closestDistance = Vector2.Distance(start, goal);
        State = Status.Running;
        Message = "Calcul d'itineraire en cours.";
        total.Restart();
        return true;
    }

    public void Cancel()
    {
        if (State == Status.Running) { State = Status.Failed; Message = "Calcul annule."; }
        total.Stop();
    }

    void Reset()
    {
        nodes.Clear(); heap.Clear(); bestG.Clear(); probes.Clear(); obstacleCache.Clear(); result.Clear();
        Array.Clear(rejections, 0, rejections.Length);
        Expansions = 0; ProbeCount = 0; ResultLength = 0f; GoalMiss = 0f; Message = "";
        State = Status.Idle;
        total.Reset();
    }

    bool Refuse(string reason, out string refusal)
    {
        refusal = reason; Message = reason; State = Status.Failed; return false;
    }

    /// <summary>Avance la recherche pendant au plus budgetMilliseconds. Retourne true quand elle est terminee.</summary>
    public bool StepSearch(float budgetMilliseconds)
    {
        if (State != Status.Running) return true;
        slice.Restart();
        double budget = Mathf.Max(.2f, budgetMilliseconds);
        while (slice.Elapsed.TotalMilliseconds < budget)
        {
            if (heap.Count == 0) { Fail("Aucun itineraire traversable : toutes les possibilites ont ete explorees."); return true; }
            HeapItem item = Pop();
            Node n = nodes[item.node];
            long key = Key(n.x, n.z, n.yaw, n.straight);
            if (bestG.TryGetValue(key, out float best) && n.g > best + 1e-4f) continue;
            float d = Vector2.Distance(new Vector2(n.x, n.z), goal);
            if (d <= s.goalTolerance && n.straight >= s.finalStraightPrimitives && item.node != 0) { Succeed(item.node, d); return true; }
            if (++Expansions > s.maxExpansions) { Fail("Budget de recherche epuise."); return true; }
            for (int c = 0; c < curvatures.Length; c++) Expand(item.node, curvatures[c]);
        }
        if (total.Elapsed.TotalSeconds > s.maxSeconds) { Fail("Temps de calcul maximal depasse."); return true; }
        return false;
    }

    void Expand(int parentIndex, float k)
    {
        Node p = nodes[parentIndex];
        int steps = Mathf.Max(1, Mathf.CeilToInt(s.primitiveLength / s.checkSpacing));
        float[] prev = heightsA, cur = heightsB;
        // La pose de depart (racine) est celle du rover reel : elle n'est pas refusee, mais si elle sort
        // des criteres, le controle de marche du premier pas est saute faute de reference fiable.
        bool havePrev = EvaluatePose(p.x, p.z, p.yaw, prev, out _, out _, out int parentReason);
        if (!havePrev && parentIndex != 0) { rejections[parentReason]++; return; }
        float tiltSum = 0f;
        float x = p.x, z = p.z, yaw = p.yaw;
        for (int j = 1; j <= steps; j++)
        {
            float dist = s.primitiveLength * j / steps;
            Arc(p.x, p.z, p.yaw, k, dist, out x, out z, out yaw);
            if (!domain.Contains(new Vector2(x, z))) { rejections[Domain]++; return; }
            if (!EvaluatePose(x, z, yaw, cur, out float tiltTan, out float groundY, out int reason)) { rejections[reason]++; return; }
            if (havePrev)
                for (int w = 0; w < cur.Length; w++)
                    if (Mathf.Abs(cur[w] - prev[w]) > s.maxWheelStep) { rejections[Step]++; return; }
            havePrev = true;
            if ((j == steps || j == steps / 2) && Blocked(x, z, yaw, groundY)) { rejections[Obstacle]++; return; }
            tiltSum += tiltTan;
            float[] t = prev; prev = cur; cur = t;
        }
        float meanTilt = tiltSum / steps;
        float cost = s.primitiveLength * (1f + s.curvatureCost * Mathf.Abs(k) / kAbsMax + s.tiltCost * meanTilt / maxTiltTan)
            + s.curvatureChangeCost * Mathf.Abs(k - p.k) / kAbsMax;
        float g = p.g + cost;
        int straight = Mathf.Abs(k) < 1e-6f ? p.straight + 1 : 0;
        long key = Key(x, z, yaw, straight);
        if (bestG.TryGetValue(key, out float old) && old <= g) return;
        bestG[key] = g;
        nodes.Add(new Node { x = x, z = z, yaw = yaw, k = k, g = g, parent = parentIndex, straight = straight });
        int index = nodes.Count - 1;
        float h = Heuristic(x, z);
        if (h + s.goalTolerance < closestDistance) { closestDistance = h + s.goalTolerance; closestNode = index; }
        Push(g + s.heuristicWeight * h, index);
    }

    float Heuristic(float x, float z) => Mathf.Max(0f, Vector2.Distance(new Vector2(x, z), goal) - s.goalTolerance);

    static void Arc(float x0, float z0, float yaw0, float k, float dist, out float x, out float z, out float yaw)
    {
        // Convention Unity : yaw autour de +Y, avant = (sin yaw, cos yaw), courbure positive = virage a droite
        // (meme convention que RoverKinematicActuator et le pure pursuit du follower).
        if (Mathf.Abs(k) < 1e-5f)
        {
            x = x0 + Mathf.Sin(yaw0) * dist; z = z0 + Mathf.Cos(yaw0) * dist; yaw = yaw0; return;
        }
        yaw = yaw0 + k * dist;
        x = x0 + (Mathf.Cos(yaw0) - Mathf.Cos(yaw)) / k;
        z = z0 + (Mathf.Sin(yaw) - Mathf.Sin(yaw0)) / k;
    }

    bool EvaluatePose(float x, float z, float yaw, float[] heights, out float tiltTan, out float groundY, out int reason)
    {
        tiltTan = 0f; groundY = 0f; reason = NoGround;
        float sy = Mathf.Sin(yaw), cy = Mathf.Cos(yaw);
        double r0 = 0, r1 = 0, r2 = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            Vector2 o = wheels[i];
            float wx = x + cy * o.x + sy * o.y;
            float wz = z - sy * o.x + cy * o.y;
            Probe pr = ProbeAt(wx, wz);
            if (!pr.ok) { reason = NoGround; return false; }
            if (pr.slope > maxSurfaceSlope) { reason = Slope; return false; }
            heights[i] = pr.y;
            r0 += pr.y; r1 += pr.y * o.x; r2 += pr.y * o.y;
        }
        double a = n00 * r0 + n01 * r1 + n02 * r2;
        double b = n01 * r0 + n11 * r1 + n12 * r2;
        double c = n02 * r0 + n12 * r1 + n22 * r2;
        tiltTan = (float)Math.Sqrt(b * b + c * c);
        groundY = (float)a;
        if (tiltTan > maxTiltTan) { reason = Tilt; return false; }
        for (int i = 0; i < wheels.Length; i++)
        {
            double plane = a + b * wheels[i].x + c * wheels[i].y;
            if (Math.Abs(heights[i] - plane) > s.maxWheelResidual) { reason = Step; return false; }
        }
        return true;
    }

    Probe ProbeAt(float x, float z)
    {
        float cell = s.probeCell;
        int ix = Mathf.FloorToInt(x / cell), iz = Mathf.FloorToInt(z / cell);
        long key = ((long)ix << 32) ^ (uint)iz;
        if (probes.TryGetValue(key, out Probe cached)) return cached;
        Probe p = new Probe();
        Vector2 xz = new Vector2((ix + .5f) * cell, (iz + .5f) * cell);
        if (solver.TryProbeNavigationGround(xz, surfaceBounds, out RaycastHit hit, out _))
        {
            p.ok = true; p.y = hit.point.y;
            p.slope = Vector3.Angle(hit.normal, Vector3.up);
        }
        ProbeCount++;
        probes.Add(key, p);
        return p;
    }

    bool Blocked(float x, float z, float yaw, float groundY)
    {
        Vector3 center = new Vector3(x, groundY + s.bodyClearance + s.bodyHeight * .5f, z);
        Vector3 half = new Vector3(halfWidth, s.bodyHeight * .5f, halfLength);
        Quaternion rot = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
        int count = Physics.OverlapBoxNonAlloc(center, half, overlap, rot, s.obstacleMask, QueryTriggerInteraction.Ignore);
        if (count >= overlap.Length) return true; // tampon plein : prudence
        for (int i = 0; i < count; i++) if (IsObstacle(overlap[i])) return true;
        return false;
    }

    bool IsObstacle(Collider c)
    {
        if (!c) return false;
        EntityId id = c.GetEntityId();
        if (obstacleCache.TryGetValue(id, out bool known)) return known;
        bool obstacle = !c.isTrigger && !solver.IsNavigationSurface(c) && !(c is CharacterController) &&
            !(roverRoot && (c.transform == roverRoot || c.transform.IsChildOf(roverRoot)));
        if (obstacle && ignoreRoots != null)
            foreach (Transform t in ignoreRoots)
                if (t && (c.transform == t || c.transform.IsChildOf(t))) { obstacle = false; break; }
        obstacleCache.Add(id, obstacle);
        return obstacle;
    }

    long Key(float x, float z, float yaw, int straight)
    {
        long run = Mathf.Min(straight, s.finalStraightPrimitives);
        int ix = Mathf.FloorToInt(x / s.cellSize), iz = Mathf.FloorToInt(z / s.cellSize);
        float turns = yaw / (2f * Mathf.PI);
        turns -= Mathf.Floor(turns);
        int ih = Mathf.RoundToInt(turns * s.headingBins) % s.headingBins;
        return (run << 60) | ((long)(ix & 0xFFFFF) << 40) | ((long)(iz & 0xFFFFF) << 20) | (long)ih;
    }

    void Succeed(int goalNode, float miss)
    {
        result.Clear();
        // Remonte la chaine de noeuds en pile pour eviter toute allocation de liste temporaire.
        int depth = 0;
        for (int i = goalNode; i > 0; i = nodes[i].parent) depth++;
        result.Add(startCenter);
        for (int level = depth; level >= 1; level--)
        {
            int i = goalNode;
            for (int up = 1; up < level; up++) i = nodes[i].parent;
            Node n = nodes[i], p = nodes[n.parent];
            int m = Mathf.Max(1, Mathf.CeilToInt(s.primitiveLength / s.outputSpacing));
            for (int j = 1; j <= m; j++)
            {
                Arc(p.x, p.z, p.yaw, n.k, s.primitiveLength * j / m, out float x, out float z, out _);
                Probe pr = ProbeAt(x, z);
                result.Add(new Vector3(x, pr.ok ? pr.y : startCenter.y, z));
            }
        }
        ResultLength = depth * s.primitiveLength;
        GoalMiss = miss;
        total.Stop();
        State = Status.Succeeded;
        Message = $"Itineraire trouve : {ResultLength:F1} m, {result.Count} points, ecart final {miss:F2} m, " +
                  $"{Expansions} expansions, {ProbeCount} sondages, {ElapsedSeconds:F2} s.";
    }

    void Fail(string headline)
    {
        total.Stop();
        State = Status.Failed;
        int sum = 0;
        for (int i = 0; i < rejections.Length; i++) sum += rejections[i];
        string detail = "";
        if (sum > 0)
        {
            int first = -1, second = -1;
            for (int i = 0; i < rejections.Length; i++)
            {
                if (first < 0 || rejections[i] > rejections[first]) { second = first; first = i; }
                else if (second < 0 || rejections[i] > rejections[second]) second = i;
            }
            detail = $" Blocages principaux : {ReasonNames[first]} ({100f * rejections[first] / sum:F0} %)";
            if (second >= 0 && rejections[second] > 0) detail += $", {ReasonNames[second]} ({100f * rejections[second] / sum:F0} %)";
            detail += ".";
        }
        Message = headline + $" Approche maximale : {closestDistance:F1} m de la destination." + detail;
    }

    void Push(float f, int node)
    {
        heap.Add(new HeapItem { f = f, node = node });
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (heap[parent].f <= heap[i].f) break;
            HeapItem t = heap[parent]; heap[parent] = heap[i]; heap[i] = t; i = parent;
        }
    }

    HeapItem Pop()
    {
        HeapItem top = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < heap.Count && heap[l].f < heap[m].f) m = l;
            if (r < heap.Count && heap[r].f < heap[m].f) m = r;
            if (m == i) break;
            HeapItem t = heap[m]; heap[m] = heap[i]; heap[i] = t; i = m;
        }
        return top;
    }
}
