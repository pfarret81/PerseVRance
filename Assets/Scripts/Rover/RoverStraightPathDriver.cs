using UnityEngine;
using Unity.Profiling;

[DisallowMultipleComponent]
public class RoverStraightPathDriver : MonoBehaviour
{
    [SerializeField] private RoverStaticPoseTest solver;
    [SerializeField, Min(0.001f)] private float speed = 0.10f;
    [SerializeField, Min(0.001f)] private float pathLength = 3.60f;
    [SerializeField] private bool run;
    [SerializeField] private bool maintainPoseWhilePaused = true;
    [Header("Garde-fous de deplacement, pas limites mecaniques")]
    [SerializeField, Min(0.0001f)] private float maxSpatialStep = 0.005f;
    [SerializeField, Range(1, 64)] private int maxStepsPerFrame = 8;

    public enum MotionState { Paused, Moving, Completed, Fault }
    public MotionState State { get; private set; } = MotionState.Paused;
    public float DistanceTravelled { get; private set; }
    public float PathLength => pathLength;
    public float Speed => speed;
    public RoverStaticPoseTest Solver => solver;
    public string FaultReason { get; private set; } = "";
    public float PeakAcceptedError { get; private set; }
    public int PeakIterations { get; private set; }
    public float AcceptedMotionTime { get; private set; }

    private Vector3 anchor, heading;
    private bool ready;
    private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("Rover.KinematicUpdate");

    private void Start() { InitializeDriver(); }
    private bool InitializeDriver()
    {
        if (ready) return true;
        if (!solver || solver.transform != transform || !solver.isActiveAndEnabled ||
            !(speed > 0f) || !(pathLength > 0f) || !(maxSpatialStep > 0f) || maxStepsPerFrame < 1)
        { SetFault("Configuration invalide : solveur actif sur le meme RoverRoot et parametres positifs requis."); return false; }
        if (!solver.InitializeRuntime() || !solver.TrySolveAt(solver.ReferencePosition, false))
        { SetFault("Pose initiale refusee. Verifier la reference et les surfaces."); return false; }
        anchor = solver.LastAnchor;
        anchor.y = transform.position.y;
        heading = solver.ReferenceForward;
        DistanceTravelled = 0f;
        AcceptedMotionTime = 0f;
        PeakAcceptedError = solver.LastMaxError;
        PeakIterations = solver.LastIterations;
        ready = true;
        State = run ? MotionState.Moving : MotionState.Paused;
        return true;
    }
    private void Update()
    {
        if (State == MotionState.Fault) return;
        using (UpdateMarker.Auto()) SimulateStep(Time.deltaTime);
    }

    // Also used by the deterministic timestep check. No independent solver Update.
    public void SimulateStep(float dt)
    {
        if (!ready && !InitializeDriver()) return;
        if (State == MotionState.Fault) return;
        if (!solver || !solver.isActiveAndEnabled ||
            float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f ||
            !(speed > 0f) || !(pathLength > 0f) || !(maxSpatialStep > 0f) || maxStepsPerFrame < 1)
        { SetFault("Parametres ou duree de pas invalides."); return; }
        float remaining = Mathf.Max(0f, pathLength - DistanceTravelled);
        if (remaining <= 0.000001f)
        { run = false; State = MotionState.Completed; }
        if (!run)
        {
            if (State != MotionState.Completed) State = MotionState.Paused;
            if (maintainPoseWhilePaused)
            {
                anchor.y = transform.position.y;
                if (!solver.TrySolveAt(anchor, false))
                { SetFault("Resolution a l'arret refusee : " + solver.LastFailure); return; }
                ReadMetrics();
            }
            return;
        }
        State = MotionState.Moving;
        float distance = Mathf.Min(speed * dt, remaining);
        if (distance > maxSpatialStep * maxStepsPerFrame + 0.000001f)
        { SetFault("Budget de sous-pas depasse : image trop longue ou vitesse trop elevee. Aucun saut impose."); return; }
        int steps = distance > 0f ? Mathf.CeilToInt(distance / maxSpatialStep) : 0;
        float step = steps > 0 ? distance / steps : 0f;
        for (int i = 0; i < steps; i++)
        {
            Vector3 candidate = anchor + heading * step;
            candidate.y = transform.position.y;
            if (!solver.TrySolveAt(candidate, true))
            { SetFault("Deplacement refuse : " + solver.LastFailure); return; }
            anchor = candidate;
            DistanceTravelled += step;
            AcceptedMotionTime += step / speed;
            ReadMetrics();
        }
        if (pathLength - DistanceTravelled <= 0.000001f)
        {
            DistanceTravelled = pathLength;
            run = false;
            State = MotionState.Completed;
        }
    }
    private void ReadMetrics()
    {
        PeakAcceptedError = Mathf.Max(PeakAcceptedError, solver.LastMaxError);
        PeakIterations = Mathf.Max(PeakIterations, solver.LastIterations);
    }
    private void SetFault(string reason)
    {
        run = false; State = MotionState.Fault; FaultReason = reason;
        Debug.LogWarning("Rover arrete : " + reason, this);
    }
    [ContextMenu("Pause")]
    public void Pause()
    {
        run = false;
        if (State != MotionState.Fault && State != MotionState.Completed) State = MotionState.Paused;
    }
    [ContextMenu("Resume")]
    public void Resume()
    {
        if (!Application.isPlaying) return;
        if (!ready && !InitializeDriver()) return;
        if (State == MotionState.Fault || State == MotionState.Completed) return;
        run = true; State = MotionState.Moving;
    }
    [ContextMenu("Retry Current Pose After Fault")]
    private void RetryCurrentPose()
    {
        if (!Application.isPlaying || !ready) return;
        Physics.SyncTransforms();
        anchor.y = transform.position.y;
        if (!solver.TrySolveAt(anchor, false)) return;
        FaultReason = ""; State = MotionState.Paused; run = false;
        ReadMetrics();
    }
    [ContextMenu("Restart Path From Reference")]
    private void RestartPathMenu() { RestartPathFromReference(); }
    public bool RestartPathFromReference()
    {
        if (!Application.isPlaying) return false;
        run = false;
        if (!ready && !InitializeDriver()) return false;
        Physics.SyncTransforms();
        if (!solver.ResetRuntimePose())
        { SetFault("Redemarrage refuse : pose initiale impossible."); return false; }
        anchor = solver.LastAnchor; anchor.y = transform.position.y;
        heading = solver.ReferenceForward;
        DistanceTravelled = 0f; AcceptedMotionTime = 0f;
        PeakAcceptedError = solver.LastMaxError; PeakIterations = solver.LastIterations;
        State = MotionState.Paused; FaultReason = "";
        return true;
    }
    [ContextMenu("Report Path")]
    private void ReportPath()
    {
        Debug.Log($"Etat={State}, distance={DistanceTravelled:F4}/{pathLength:F4} m, " +
            $"temps de mouvement accepte={AcceptedMotionTime:F3} s, erreur max acceptee={PeakAcceptedError * 1000f:F3} mm, " +
            $"pic de passages={PeakIterations}, dernier statut={solver.LastFailure}, faute={FaultReason}.", this);
    }
}
