using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>Reglages du planificateur de vol d'Ingenuity (valeurs du prototype validees au lot 4).</summary>
    [System.Serializable]
    public sealed class IngenuityRoutePlannerSettings
    {
        [Tooltip("Hauteur du centre de l'appareil au-dessus du sol pendant le vol (m).")]
        [Range(1f, 10f)] public float flightHeight = 3f;
        [Tooltip("Taille des cellules de la grille de recherche (m).")]
        [Range(.5f, 3f)] public float cellSize = 1f;
        [Tooltip("Marge de recherche autour du rectangle depart-destination (m) : place disponible pour contourner.")]
        [Range(5f, 80f)] public float searchMargin = 30f;
        [Tooltip("Rayon de l'appareil, rotors compris (m).")]
        [Range(.2f, 2f)] public float vehicleRadius = .6f;
        [Tooltip("Marge laterale ajoutee autour des obstacles contournes (m).")]
        [Range(0f, 3f)] public float lateralMargin = .6f;
        [Tooltip("Garde verticale minimale sous le centre de l'appareil au-dessus d'un objet survole (m). " +
                 "Tout objet qui depasse (hauteur de vol - garde) au-dessus du sol est contourne.")]
        [Range(.2f, 5f)] public float verticalClearance = 1f;
        [Tooltip("Penalite par metre de denivele dans le cout de recherche (favorise les trajets plats).")]
        [Range(0f, 4f)] public float climbPenalty = .5f;
        [Range(1f, 2f)] public float heuristicWeight = 1.15f;
        [Min(1000)] public int maxExpansions = 120000;
        [Min(1f)] public float maxSeconds = 15f;
        [Tooltip("Nombre maximal de recherches successives quand la verification geometrique decouvre un obstacle.")]
        [Range(1, 8)] public int maxVerificationRounds = 5;
        [Tooltip("Pas des controles de volume le long du trajet (m).")]
        [Range(.2f, 1f)] public float verificationStep = .5f;
        [Tooltip("Hauteur de depart des sondages au-dessus du point le plus haut (m) et longueur totale des sondages (m).")]
        public float probeAbove = 200f, probeLength = 600f;
        [Tooltip("Nombre maximal de cellules de la grille (borne memoire du prototype).")]
        [Min(10000)] public int maxCells = 300000;
    }

    /// <summary>
    /// Planificateur de vol d'Ingenuity : A* incremental sur grille 2D, profil vertical calcule a hauteur
    /// constante au-dessus du sol, puis verification geometrique du volume balaye.
    /// - Sol = TerrainCollider (ou racines declarees comme sol) : le vol suit le relief.
    /// - Tout autre collider non ignore qui depasse (hauteur de vol - garde) est un obstacle contourne
    ///   lateralement, avec la marge laterale.
    /// - Pente du profil limitee a vitesse verticale / vitesse horizontale (montee et descente).
    /// - Toutes les cellules restent dans la portee autour du centre de portee (Perseverance).
    /// Aucun repli en ligne droite : si aucun trajet verifie n'existe, la demande est refusee avec la cause.
    /// </summary>
    public sealed class IngenuityRoutePlanner
    {
        public enum Status { Idle, Running, Succeeded, Failed }
        enum Phase { Search, Smooth, Verify }

        const byte Unknown = 0, Free = 1, Obstacle = 2, NoGround = 3, OutOfRange = 4, Forced = 5;

        public Status State { get; private set; } = Status.Idle;
        public string Message { get; private set; } = "";
        public int Expansions { get; private set; }
        public int Round { get; private set; }
        public float ElapsedSeconds => (float)clock.Elapsed.TotalSeconds;
        public IReadOnlyList<Vector3> Result => result;
        public float ResultLength { get; private set; }
        public float StraightDistance { get; private set; }
        public int ProbeCount { get; private set; }

        readonly Stopwatch clock = new Stopwatch();
        readonly Stopwatch step = new Stopwatch();
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly Collider[] overlaps = new Collider[24];
        readonly List<Vector3> result = new List<Vector3>(512);
        readonly List<int> cellPath = new List<int>(512);
        readonly List<int> anchors = new List<int>(128);
        readonly List<Vector2> polyline = new List<Vector2>(512);
        readonly List<float> altitudes = new List<float>(512);
        readonly List<float> envelope = new List<float>(512);
        readonly List<Vector2Int> inflation = new List<Vector2Int>(32);

        IngenuityRoutePlannerSettings s;
        Transform[] ignoreRoots, groundRoots;
        Transform self;
        Vector3 start, goal, rangeCenter;
        float rangeSq, grade, probeTop;
        Phase phase;
        int nx, ny, startCell, goalCell, verifyIndex, smoothAnchor;
        Vector2 origin;
        int obstacleCells, noGroundCells, outOfRangeCells, forcedCells, overflow;

        // grille (reutilisee, agrandie seulement si necessaire)
        float[] ground = new float[0], g = new float[0];
        byte[] raw = new byte[0], infl = new byte[0];
        int[] parent = new int[0], seen = new int[0], closed = new int[0];
        int generation;
        int[] heapCell = new int[1024];
        float[] heapF = new float[1024];
        int heapCount;

        public void Cancel()
        {
            if (State == Status.Running) { State = Status.Failed; Message = "Calcul annulé."; }
            clock.Stop();
        }

        /// <param name="startPosition">Position actuelle (ou point d'arret prevu) du centre de l'appareil.</param>
        /// <param name="goalPosition">Destination (seul XZ est utilise ; l'altitude est calculee).</param>
        /// <param name="climbGrade">Vitesse verticale / vitesse horizontale.</param>
        public bool Begin(Vector3 startPosition, Vector3 goalPosition, Vector3 rangeCenterPosition, float maxRange,
                          float climbGrade, IngenuityRoutePlannerSettings settings, Transform vehicleRoot,
                          Transform[] ignored, Transform[] extraGround, out string refusal)
        {
            refusal = "";
            s = settings ?? new IngenuityRoutePlannerSettings();
            settings = s;
            self = vehicleRoot; ignoreRoots = ignored; groundRoots = extraGround;
            start = startPosition; goal = goalPosition; rangeCenter = rangeCenterPosition;
            // Si l'appareil est deja au-dela de la portee (le rover s'est eloigne), il peut encore revenir :
            // la limite est relachee a sa distance actuelle, jamais au-dela.
            float startRange = Flat(startPosition, rangeCenterPosition) + settings.cellSize * 2f;
            rangeSq = Mathf.Max(maxRange, startRange) * Mathf.Max(maxRange, startRange);
            grade = Mathf.Clamp(climbGrade, .05f, 5f);
            result.Clear(); ResultLength = 0f; Expansions = 0; Round = 0; ProbeCount = 0;
            obstacleCells = noGroundCells = outOfRangeCells = forcedCells = overflow = 0;
            StraightDistance = Flat(start, goal);
            probeTop = Mathf.Max(start.y, goal.y, rangeCenter.y) + s.probeAbove;

            float c = s.cellSize;
            Vector2 a = new Vector2(Mathf.Min(start.x, goal.x), Mathf.Min(start.z, goal.z)) - Vector2.one * s.searchMargin;
            Vector2 b = new Vector2(Mathf.Max(start.x, goal.x), Mathf.Max(start.z, goal.z)) + Vector2.one * s.searchMargin;
            origin = a;
            nx = Mathf.CeilToInt((b.x - a.x) / c) + 1;
            ny = Mathf.CeilToInt((b.y - a.y) / c) + 1;
            if ((long)nx * ny > s.maxCells) { refusal = "zone de recherche trop grande pour le prototype."; return Fail(refusal); }
            Allocate(nx * ny);

            // disque d'inflation (marge laterale + rayon de l'appareil)
            inflation.Clear();
            float inflate = s.vehicleRadius + s.lateralMargin + c * .5f;
            int r = Mathf.CeilToInt(inflate / c);
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                if (Mathf.Sqrt(dx * dx + dy * dy) * c <= inflate) inflation.Add(new Vector2Int(dx, dy));

            startCell = CellOf(start); goalCell = CellOf(goal);
            if (startCell < 0 || goalCell < 0) { refusal = "point hors de la grille."; return Fail(refusal); }
            byte goalRaw = Raw(goalCell);
            if (goalRaw == NoGround) { refusal = "aucun terrain sous la destination."; return Fail(refusal); }
            if (goalRaw == OutOfRange) { refusal = "destination hors de portée."; return Fail(refusal); }
            if (!Inflated(goalCell)) { refusal = "destination occupée ou trop proche d'un obstacle (objet de plus de " + (s.flightHeight - s.verticalClearance).ToString("F1") + " m)."; return Fail(refusal); }

            clock.Reset(); clock.Start();
            State = Status.Running; Message = "Recherche…";
            BeginSearch();
            return true;
        }

        bool Fail(string why) { State = Status.Failed; Message = why; return false; }

        void Allocate(int n)
        {
            if (ground.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                ground = new float[size]; g = new float[size];
                raw = new byte[size]; infl = new byte[size];
                parent = new int[size]; seen = new int[size]; closed = new int[size];
                generation = 0;
            }
            System.Array.Clear(raw, 0, n);
            System.Array.Clear(infl, 0, n);
        }

        // ------------------------------------------------------------------ grille
        int CellOf(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - origin.x) / s.cellSize), y = Mathf.FloorToInt((p.z - origin.y) / s.cellSize);
            return x < 0 || y < 0 || x >= nx || y >= ny ? -1 : y * nx + x;
        }
        Vector2 CenterOf(int cell) => new Vector2(origin.x + (cell % nx + .5f) * s.cellSize, origin.y + (cell / nx + .5f) * s.cellSize);
        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        bool Ignored(Collider c)
        {
            if (!c || c.isTrigger || c is CharacterController) return true;
            Transform t = c.transform;
            if (self && t.IsChildOf(self)) return true;
            if (ignoreRoots != null) for (int i = 0; i < ignoreRoots.Length; i++) if (ignoreRoots[i] && t.IsChildOf(ignoreRoots[i])) return true;
            return false;
        }
        bool IsGround(Collider c)
        {
            if (c is TerrainCollider) return true;
            if (groundRoots != null) for (int i = 0; i < groundRoots.Length; i++) if (groundRoots[i] && c.transform.IsChildOf(groundRoots[i])) return true;
            return false;
        }

        /// <summary>Sondage vertical : hauteur du sol et sommet du plus haut obstacle non ignore.</summary>
        bool ProbeColumn(Vector2 xz, out float groundY, out float obstacleTop, out bool overflowed)
        {
            ProbeCount++;
            groundY = float.NegativeInfinity; obstacleTop = float.NegativeInfinity;
            int n = Physics.RaycastNonAlloc(new Ray(new Vector3(xz.x, probeTop, xz.y), Vector3.down), hits, s.probeLength, ~0, QueryTriggerInteraction.Ignore);
            overflowed = n >= hits.Length;
            for (int i = 0; i < n; i++)
            {
                var col = hits[i].collider;
                if (Ignored(col)) continue;
                float y = hits[i].point.y;
                if (IsGround(col)) { if (y > groundY) groundY = y; }
                else if (y > obstacleTop) obstacleTop = y;
            }
            return !float.IsNegativeInfinity(groundY);
        }

        /// <summary>Sondage isole hors calcul (placement, hauteur sol). Refuse pendant un calcul.</summary>
        public bool ProbeAt(Vector2 xz, float fromY, IngenuityRoutePlannerSettings settings, Transform vehicleRoot,
                            Transform[] ignored, Transform[] extraGround, out float groundY, out float obstacleTop)
        {
            groundY = obstacleTop = float.NegativeInfinity;
            if (State == Status.Running) return false;
            s = settings ?? new IngenuityRoutePlannerSettings();
            self = vehicleRoot; ignoreRoots = ignored; groundRoots = extraGround;
            probeTop = fromY;
            return ProbeColumn(xz, out groundY, out obstacleTop, out _);
        }

        byte Raw(int cell)
        {
            byte v = raw[cell];
            if (v != Unknown) return v;
            Vector2 p = CenterOf(cell);
            float dx = p.x - rangeCenter.x, dz = p.y - rangeCenter.z;
            if (dx * dx + dz * dz > rangeSq) { outOfRangeCells++; return raw[cell] = OutOfRange; }
            if (!ProbeColumn(p, out float gy, out float top, out bool over)) { noGroundCells++; ground[cell] = float.NaN; return raw[cell] = NoGround; }
            ground[cell] = gy;
            if (over) { overflow++; obstacleCells++; return raw[cell] = Obstacle; }   // prudence : tampon plein
            if (top > gy + s.flightHeight - s.verticalClearance) { obstacleCells++; return raw[cell] = Obstacle; }
            return raw[cell] = Free;
        }

        bool Inflated(int cell)
        {
            byte v = infl[cell];
            if (v != Unknown) return v == Free;
            if (Raw(cell) != Free) { infl[cell] = Obstacle; return false; }
            int cx = cell % nx, cy = cell / nx;
            for (int i = 0; i < inflation.Count; i++)
            {
                int x = cx + inflation[i].x, y = cy + inflation[i].y;
                if (x < 0 || y < 0 || x >= nx || y >= ny) continue;
                byte rv = Raw(y * nx + x);
                if (rv == Obstacle || rv == Forced) { infl[cell] = Obstacle; return false; }
            }
            infl[cell] = Free; return true;
        }

        /// <summary>Le depart est exempte de la marge laterale (l'appareil y est deja) mais pas des obstacles.</summary>
        bool Passable(int cell)
        {
            if (Inflated(cell)) return true;
            if (Raw(cell) != Free) return false;
            int dx = cell % nx - startCell % nx, dy = cell / nx - startCell / nx;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * s.cellSize;
            return d <= s.vehicleRadius + s.lateralMargin + s.cellSize;
        }

        /// <summary>Altitude de vol de la cellule : sol le plus haut des 3x3 voisines + hauteur de vol.</summary>
        float CellAltitude(int cell)
        {
            int cx = cell % nx, cy = cell / nx;
            float best = float.NegativeInfinity;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= nx || y >= ny) continue;
                int c = y * nx + x;
                byte rv = Raw(c);
                if (rv == NoGround || rv == OutOfRange) continue;
                if (ground[c] > best) best = ground[c];
            }
            return (float.IsNegativeInfinity(best) ? start.y - s.flightHeight : best) + s.flightHeight;
        }

        // ------------------------------------------------------------------ A*
        void BeginSearch()
        {
            phase = Phase.Search;
            generation++;
            if (generation == int.MaxValue) { System.Array.Clear(seen, 0, seen.Length); System.Array.Clear(closed, 0, closed.Length); generation = 1; }
            heapCount = 0;
            seen[startCell] = generation; g[startCell] = 0f; parent[startCell] = -1;
            Push(startCell, H(startCell));
            Round++;
        }

        float H(int cell)
        {
            int dx = cell % nx - goalCell % nx, dy = cell / nx - goalCell / nx;
            return Mathf.Sqrt(dx * dx + dy * dy) * s.cellSize * s.heuristicWeight;
        }

        void Push(int cell, float f)
        {
            if (heapCount == heapCell.Length) { System.Array.Resize(ref heapCell, heapCount * 2); System.Array.Resize(ref heapF, heapCount * 2); }
            int i = heapCount++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapF[p] <= f) break;
                heapCell[i] = heapCell[p]; heapF[i] = heapF[p]; i = p;
            }
            heapCell[i] = cell; heapF[i] = f;
        }

        int Pop()
        {
            int top = heapCell[0];
            int lastCell = heapCell[--heapCount]; float lastF = heapF[heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= heapCount) break;
                int m = l + 1 < heapCount && heapF[l + 1] < heapF[l] ? l + 1 : l;
                if (heapF[m] >= lastF) break;
                heapCell[i] = heapCell[m]; heapF[i] = heapF[m]; i = m;
            }
            if (heapCount > 0) { heapCell[i] = lastCell; heapF[i] = lastF; }
            return top;
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Avance le calcul pendant au plus budgetMs. Retourne true quand le calcul est termine.</summary>
        public bool StepSearch(float budgetMs)
        {
            if (State != Status.Running) return true;
            step.Reset(); step.Start();
            long budget = (long)(budgetMs * Stopwatch.Frequency / 1000.0);
            int counter = 0;
            while (State == Status.Running)
            {
                if ((++counter & 15) == 0 && step.ElapsedTicks > budget) break;
                if (clock.Elapsed.TotalSeconds > s.maxSeconds) { Failure("temps de calcul dépassé (" + s.maxSeconds.ToString("F0") + " s)."); break; }
                switch (phase)
                {
                    case Phase.Search: SearchStep(); break;
                    case Phase.Smooth: SmoothStep(); break;
                    case Phase.Verify: VerifyStep(); break;
                }
            }
            step.Stop();
            return State != Status.Running;
        }

        void SearchStep()
        {
            for (int k = 0; k < 8; k++)
            {
                if (heapCount == 0) { Failure(NoRouteReason()); return; }
                int cell = Pop();
                if (closed[cell] == generation) continue;
                closed[cell] = generation;
                if (cell == goalCell) { BuildCellPath(); return; }
                if (++Expansions > s.maxExpansions) { Failure("recherche trop longue (" + s.maxExpansions + " cellules explorées) : destination trop éloignée ou trop encombrée."); return; }
                int cx = cell % nx, cy = cell / nx;
                float alt = CellAltitude(cell);
                for (int d = 0; d < 8; d++)
                {
                    int x = cx + DX[d], y = cy + DY[d];
                    if (x < 0 || y < 0 || x >= nx || y >= ny) continue;
                    int n = y * nx + x;
                    if (closed[n] == generation || !Passable(n)) continue;
                    if (d >= 4 && (!Passable(cy * nx + x) || !Passable(y * nx + cx))) continue;   // pas de coin coupe
                    float cost = (d >= 4 ? 1.41421356f : 1f) * s.cellSize + s.climbPenalty * Mathf.Abs(CellAltitude(n) - alt);
                    float ng = g[cell] + cost;
                    if (seen[n] == generation && ng >= g[n]) continue;
                    seen[n] = generation; g[n] = ng; parent[n] = cell;
                    Push(n, ng + H(n));
                }
            }
        }

        string NoRouteReason()
        {
            var r = "aucun itinéraire de vol sans obstacle vers cette destination";
            var parts = "";
            if (obstacleCells + forcedCells > 0) parts += $"obstacles contournés impossibles ({obstacleCells + forcedCells} cellules)";
            if (outOfRangeCells > 0) parts += (parts.Length > 0 ? ", " : "") + "limite de portée";
            if (noGroundCells > 0) parts += (parts.Length > 0 ? ", " : "") + "bord du terrain";
            if (overflow > 0) parts += (parts.Length > 0 ? ", " : "") + overflow + " sondages saturés";
            return parts.Length > 0 ? r + " (blocages : " + parts + ")." : r + ".";
        }

        void Failure(string why) { State = Status.Failed; Message = why; clock.Stop(); }

        void BuildCellPath()
        {
            cellPath.Clear();
            for (int c = goalCell; c >= 0; c = parent[c]) { cellPath.Add(c); if (c == startCell) break; }
            cellPath.Reverse();
            anchors.Clear(); anchors.Add(0);
            smoothAnchor = 0;
            phase = Phase.Smooth;
        }

        // ------------------------------------------------------------------ lissage (raccourcis verifies)
        bool Visible(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            int samples = Mathf.Max(1, Mathf.CeilToInt(len / (s.cellSize * .25f)));
            int last = -1;
            for (int i = 0; i <= samples; i++)
            {
                Vector2 p = a + d * (i / (float)samples);
                int c = CellOf(new Vector3(p.x, 0f, p.y));
                if (c == last) continue;
                last = c;
                if (c < 0 || !Passable(c)) return false;
            }
            return true;
        }

        Vector2 PathPoint(int index)
        {
            if (index == 0) return new Vector2(start.x, start.z);
            if (index == cellPath.Count - 1) return new Vector2(goal.x, goal.z);
            return CenterOf(cellPath[index]);
        }

        void SmoothStep()
        {
            int last = cellPath.Count - 1;
            if (smoothAnchor >= last) { BuildProfile(); return; }
            Vector2 a = PathPoint(smoothAnchor);
            int best = smoothAnchor + 1;
            // avance tant que le raccourci reste dans des cellules praticables (portee locale bornee)
            for (int j = smoothAnchor + 2; j <= last && j - smoothAnchor <= 80; j++)
            {
                if (Visible(a, PathPoint(j))) best = j; else break;
            }
            anchors.Add(best);
            smoothAnchor = best;
        }

        void BuildProfile()
        {
            polyline.Clear(); altitudes.Clear();
            float c = s.cellSize;
            for (int k = 0; k < anchors.Count - 1; k++)
            {
                Vector2 a = PathPoint(anchors[k]), b = PathPoint(anchors[k + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / c));
                for (int i = (k == 0 ? 0 : 1); i <= n; i++) polyline.Add(Vector2.Lerp(a, b, i / (float)n));
            }
            if (polyline.Count < 2) polyline.Add(new Vector2(goal.x, goal.z));
            for (int i = 0; i < polyline.Count; i++)
            {
                int cell = CellOf(new Vector3(polyline[i].x, 0f, polyline[i].y));
                altitudes.Add(cell >= 0 ? CellAltitude(cell) : start.y);
            }
            // enveloppe : anticipe les reliefs voisins (fenetre +/- 2 points)
            envelope.Clear();
            for (int i = 0; i < altitudes.Count; i++)
            {
                float m = altitudes[i];
                for (int k = Mathf.Max(0, i - 2); k <= Mathf.Min(altitudes.Count - 1, i + 2); k++) m = Mathf.Max(m, altitudes[k]);
                envelope.Add(m);
            }
            // pente limitee (montee ET descente) en ne faisant que relever le profil : la garde au sol est conservee
            for (int i = envelope.Count - 2; i >= 0; i--)
                envelope[i] = Mathf.Max(envelope[i], envelope[i + 1] - grade * Vector2.Distance(polyline[i], polyline[i + 1]));
            for (int i = 1; i < envelope.Count; i++)
                envelope[i] = Mathf.Max(envelope[i], envelope[i - 1] - grade * Vector2.Distance(polyline[i], polyline[i - 1]));

            result.Clear();
            result.Add(start);                                                      // position reelle (sol ou vol)
            for (int i = 0; i < polyline.Count; i++)
            {
                var p = new Vector3(polyline[i].x, envelope[i], polyline[i].y);
                if (i == 0 && Mathf.Abs(p.y - start.y) < .02f) continue;           // deja a l'altitude de vol
                result.Add(p);
            }
            verifyIndex = 0;
            phase = Phase.Verify;
        }

        // ------------------------------------------------------------------ verification du volume balaye
        void VerifyStep()
        {
            if (verifyIndex >= result.Count - 1) { Success(); return; }
            Vector3 a = result[verifyIndex], b = result[verifyIndex + 1];
            bool takeoff = verifyIndex == 0 && Flat(a, b) < .05f;                  // segment vertical initial
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / s.verificationStep));
            for (int i = (verifyIndex == 0 ? 1 : 0); i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                if (Blocked(p, takeoff, out Collider by))
                {
                    if (takeoff) { Failure("décollage impossible : " + by.name + " au-dessus de l'appareil."); return; }
                    int cell = CellOf(p);
                    if (cell < 0 || cell == startCell || cell == goalCell || Round >= s.maxVerificationRounds)
                    { Failure($"obstacle non contournable près de {by.name} (vérification du volume de vol)."); return; }
                    raw[cell] = Forced; forcedCells++;
                    InvalidateInflation(cell);
                    BeginSearch();
                    return;
                }
            }
            verifyIndex++;
        }

        bool Blocked(Vector3 p, bool takeoff, out Collider by)
        {
            by = null;
            int n = Physics.OverlapSphereNonAlloc(p, s.vehicleRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (Ignored(c)) continue;
                if (takeoff && IsGround(c)) continue;      // au sol, la sphere touche le terrain sous l'appareil
                by = c; return true;
            }
            return false;
        }

        void InvalidateInflation(int cell)
        {
            int cx = cell % nx, cy = cell / nx;
            for (int i = 0; i < inflation.Count; i++)
            {
                int x = cx + inflation[i].x, y = cy + inflation[i].y;
                if (x < 0 || y < 0 || x >= nx || y >= ny) continue;
                infl[y * nx + x] = Unknown;
            }
            infl[cell] = Obstacle;
        }

        void Success()
        {
            float length = 0f;
            for (int i = 1; i < result.Count; i++) length += Vector3.Distance(result[i - 1], result[i]);
            ResultLength = length;
            State = Status.Succeeded;
            Message = $"trajet vérifié ({result.Count} points, {Round} recherche(s), {ProbeCount} sondages).";
            clock.Stop();
        }
    }
}
