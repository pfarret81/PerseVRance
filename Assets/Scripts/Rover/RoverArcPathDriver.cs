using System.Text;
using UnityEngine;
using Unity.Profiling;

[DisallowMultipleComponent]
public class RoverArcPathDriver : MonoBehaviour
{
    [SerializeField] private RoverStaticPoseTest solver;
    [Header("Empty de direction : avant G, avant D, arriere G, arriere D")]
    [SerializeField] private Transform[] steeringPivots=new Transform[4];
    [Header("Trajectoire du milieu des deux roues centrales")]
    [SerializeField, Min(0.1f)] private float turnRadius=5f;
    [SerializeField, Range(-120f,120f)] private float sweepDegrees=30f;
    [SerializeField, Min(0.001f)] private float speed=0.10f;
    [SerializeField] private bool run;
    [SerializeField] private bool maintainPoseWhilePaused=true;
    [Header("Reglages choisis pour le prototype, pas valeurs NASA")]
    [SerializeField, Range(1f,80f)] private float maxSteeringAngle=35f;
    [SerializeField, Min(1f)] private float steeringSpeed=30f;
    [SerializeField, Min(0.0001f)] private float maxSpatialStep=0.005f;
    [SerializeField, Range(1,64)] private int maxStepsPerFrame=8;

    public enum ArcPhase { AligningSteering, Driving, Completed, Fault }
    public ArcPhase Phase { get; private set; }=ArcPhase.AligningSteering;
    public float DistanceTravelled { get; private set; }
    public float PathLength => totalLength;
    public float PeakAcceptedError { get; private set; }
    public int PeakIterations { get; private set; }
    public string FaultReason { get; private set; }="";

    private bool ready,steeringCaptured;
    private RoverStraightPathDriver straightDriver;
    private ArcPhase phaseBeforeFault;
    private float radiusUsed,turnSign,totalLength;
    private Vector3 origin,center;
    private Quaternion baseHeading;
    private readonly Quaternion[] restSteering=new Quaternion[4];
    private readonly float[] steeringSigns=new float[4];
    private readonly float[] angles=new float[4],targets=new float[4],savedAngles=new float[4];
    private readonly int[] steerWheelIndices=new int[4];
    private readonly Vector3[] referenceWheels=new Vector3[6],previousWheels=new Vector3[6];
    private readonly float[] wheelTravel=new float[6];
    private readonly bool[] usedWheels=new bool[6];
    private static readonly ProfilerMarker Marker=new ProfilerMarker("Rover.KinematicUpdate");

    private void Start() { InitializeArc(); }
    private bool InitializeArc()
    {
        if (ready) return true;
        straightDriver=GetComponent<RoverStraightPathDriver>();
        if (!solver || solver.transform!=transform || !solver.isActiveAndEnabled ||
            (straightDriver && straightDriver.isActiveAndEnabled) ||
            steeringPivots==null || steeringPivots.Length!=4 ||
            !(turnRadius>0f) || !Finite(turnRadius) || Mathf.Abs(sweepDegrees)<0.1f || !Finite(sweepDegrees) ||
            !(speed>0f) || !Finite(speed) || !(steeringSpeed>0f) || !Finite(steeringSpeed) || !(maxSteeringAngle>0f) || !Finite(maxSteeringAngle) ||
            !(maxSpatialStep>0f) || !Finite(maxSpatialStep) || maxStepsPerFrame<1)
        { SetFault("Configuration invalide. Desactiver le driver rectiligne et assigner les 4 pivots."); return false; }
        if (!solver.InitializeRuntime() || !solver.TryGetReferenceWheelGeometry(referenceWheels))
        { SetFault("Reference mecanique ou geometrie indisponible."); return false; }
        System.Array.Clear(usedWheels,0,6);
        for (int j=0;j<4;j++)
        {
            Transform pivot=steeringPivots[j];
            if (!pivot || pivot==transform || !pivot.IsChildOf(transform))
            { SetFault("Pivot de direction invalide."); return false; }
            for (int k=0;k<j;k++) if (pivot==steeringPivots[k])
            { SetFault("Pivot de direction duplique."); return false; }
            int found=-1;
            for (int i=0;i<6;i++)
                if (solver.WheelCenter(i).IsChildOf(pivot))
                {
                    if (found!=-1) { SetFault("Un pivot de direction contient plusieurs roues."); return false; }
                    found=i;
                }
            if (found<0 || usedWheels[found])
            { SetFault("Impossible d'associer chaque pivot a une roue unique."); return false; }
            usedWheels[found]=true; steerWheelIndices[j]=found;
            float alignment=Vector3.Dot(pivot.TransformDirection(Vector3.forward),transform.up);
            if (Mathf.Abs(alignment)<0.5f)
            { SetFault("Axe Z de direction insuffisamment vertical."); return false; }
            steeringSigns[j]=Mathf.Sign(alignment);
            restSteering[j]=pivot.localRotation; angles[j]=0f;
        }
        Vector3 middle=Vector3.zero; int middleCount=0;
        for (int i=0;i<6;i++) if (!usedWheels[i]) { middle+=referenceWheels[i]; middleCount++; }
        if (middleCount!=2) { SetFault("Deux roues centrales non directrices attendues."); return false; }
        middle*=0.5f;
        for (int j=0;j<4;j++)
        {
            Vector3 wheel=referenceWheels[steerWheelIndices[j]];
            bool front=j<2,left=j%2==0;
            if ((front && wheel.z<=middle.z) || (!front && wheel.z>=middle.z) ||
                (left && wheel.x>=middle.x) || (!left && wheel.x<=middle.x))
            { SetFault("Ordre attendu des pivots : avant G, avant D, arriere G, arriere D."); return false; }
        }
        radiusUsed=turnRadius; turnSign=Mathf.Sign(sweepDegrees);
        totalLength=radiusUsed*Mathf.Abs(sweepDegrees)*Mathf.Deg2Rad;
        float curvature=turnSign/radiusUsed;
        for (int i=0;i<6;i++)
            if (1f-curvature*(referenceWheels[i].x-middle.x)<=0.05f)
            { SetFault("Rayon trop serre pour ce prototype sans inversion des roues."); return false; }
        for (int j=0;j<4;j++)
        {
            Vector3 wheel=referenceWheels[steerWheelIndices[j]];
            targets[j]=Mathf.Atan2(curvature*(wheel.z-middle.z),
                1f-curvature*(wheel.x-middle.x))*Mathf.Rad2Deg;
            if (Mathf.Abs(targets[j])>maxSteeringAngle)
            { SetFault("Angle de direction au-dela du garde-fou du prototype. Augmenter le rayon."); return false; }
        }
        steeringCaptured=true;
        origin=solver.ReferencePosition;
        baseHeading=Quaternion.LookRotation(solver.ReferenceForward,Vector3.up);
        center=origin+baseHeading*new Vector3(middle.x+turnSign*radiusUsed,0f,middle.z);
        DistanceTravelled=0f; PeakAcceptedError=0f; PeakIterations=0;
        System.Array.Clear(wheelTravel,0,6);
        Phase=ArcPhase.AligningSteering; FaultReason="";
        if (!solver.TrySolveAtHeading(origin,baseHeading))
        { SetFault("Pose initiale refusee : "+solver.LastFailure); return false; }
        ReadMetrics(); ready=true;
        return true;
    }
    private void Update()
    {
        if (Phase==ArcPhase.Fault) return;
        using (Marker.Auto()) Tick(Time.deltaTime);
    }
    private void Tick(float dt)
    {
        if (!ready && !InitializeArc()) return;
        if (!Finite(dt) || dt<0f || !(speed>0f) || !Finite(speed) || !(steeringSpeed>0f) || !Finite(steeringSpeed) ||
            !(maxSpatialStep>0f) || !Finite(maxSpatialStep) || maxStepsPerFrame<1 || !solver.isActiveAndEnabled ||
            (straightDriver && straightDriver.isActiveAndEnabled))
        { SetFault("Duree/parametres invalides ou controleurs concurrents."); return; }
        if (!run || Phase==ArcPhase.Completed)
        {
            if (maintainPoseWhilePaused && !SolveAtDistance(DistanceTravelled,false))
                SetFault("Appui a l'arret refuse : "+solver.LastFailure);
            return;
        }
        if (Phase==ArcPhase.AligningSteering)
        {
            bool aligned=true;
            for (int j=0;j<4;j++)
            {
                savedAngles[j]=angles[j];
                angles[j]=Mathf.MoveTowards(angles[j],targets[j],steeringSpeed*dt);
                if (Mathf.Abs(angles[j]-targets[j])>0.0001f) aligned=false;
            }
            ApplySteering();
            if (!SolveAtDistance(DistanceTravelled,false))
            {
                for (int j=0;j<4;j++) angles[j]=savedAngles[j];
                ApplySteering(); SetFault("Braquage refuse : "+solver.LastFailure); return;
            }
            if (aligned) Phase=ArcPhase.Driving;
            return;
        }
        float distance=Mathf.Min(speed*dt,totalLength-DistanceTravelled);
        if (distance>maxSpatialStep*maxStepsPerFrame+0.000001f)
        { SetFault("Budget de sous-pas depasse. Aucun saut impose."); return; }
        int steps=distance>0f ? Mathf.CeilToInt(distance/maxSpatialStep) : 0;
        float step=steps>0 ? distance/steps : 0f;
        for (int i=0;i<steps;i++)
        {
            float next=DistanceTravelled+step;
            if (!SolveAtDistance(next,true))
            { SetFault("Virage refuse : "+solver.LastFailure); return; }
            DistanceTravelled=next;
        }
        if (totalLength-DistanceTravelled<=0.000001f)
        { DistanceTravelled=totalLength; Phase=ArcPhase.Completed; run=false; }
    }
    private void ApplySteering()
    {
        for (int j=0;j<4;j++) steeringPivots[j].localRotation=restSteering[j]*
            Quaternion.AngleAxis(angles[j]*steeringSigns[j],Vector3.forward);
    }
    private bool SolveAtDistance(float distance,bool roll)
    {
        float yaw=turnSign*distance/radiusUsed*Mathf.Rad2Deg;
        Quaternion rotation=Quaternion.AngleAxis(yaw,Vector3.up);
        Vector3 anchor=center+rotation*(origin-center);
        anchor.y=transform.position.y;
        for (int i=0;i<6;i++) previousWheels[i]=solver.WheelCenter(i).position;
        if (!solver.TrySolveAtHeading(anchor,rotation*baseHeading)) return false;
        if (roll)
        {
            for (int i=0;i<6;i++)
            {
                Transform wheel=solver.WheelCenter(i);
                Vector3 normal=Vector3.up;
                if (solver.TryProbeGround(wheel.position,out RaycastHit hit)) normal=hit.normal;
                Vector3 rollingDirection=Vector3.Cross(
                    wheel.TransformDirection(Vector3.right).normalized,normal).normalized;
                float movement=Vector3.Dot(wheel.position-previousWheels[i],rollingDirection);
                wheel.localRotation*=Quaternion.AngleAxis(
                    movement/solver.WheelRadius*Mathf.Rad2Deg,Vector3.right);
                wheelTravel[i]+=Mathf.Abs(movement);
            }
        }
        ReadMetrics(); return true;
    }
    private void ReadMetrics()
    {
        PeakAcceptedError=Mathf.Max(PeakAcceptedError,solver.LastMaxError);
        PeakIterations=Mathf.Max(PeakIterations,solver.LastIterations);
    }
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private void SetFault(string reason)
    {
        phaseBeforeFault=Phase; Phase=ArcPhase.Fault; run=false; FaultReason=reason;
        Debug.LogWarning("Arc arrete : "+reason,this);
    }
    [ContextMenu("Pause")]
    public void Pause() { run=false; }
    [ContextMenu("Resume")]
    public void Resume()
    {
        if (!Application.isPlaying) return;
        if (!ready && !InitializeArc()) return;
        if (Phase!=ArcPhase.Fault && Phase!=ArcPhase.Completed) run=true;
    }
    [ContextMenu("Retry Current Pose After Fault")]
    private void RetryCurrentPose()
    {
        if (!Application.isPlaying || !ready || Phase!=ArcPhase.Fault) return;
        Physics.SyncTransforms();
        if (SolveAtDistance(DistanceTravelled,false))
        { Phase=phaseBeforeFault; FaultReason=""; run=false; }
    }
    [ContextMenu("Restart Arc From Reference")]
    private void RestartArc()
    {
        if (!Application.isPlaying || !solver) return;
        run=false;
        if (steeringCaptured)
            for (int j=0;j<4;j++) steeringPivots[j].localRotation=restSteering[j];
        if (!solver.ResetRuntimePose())
        { SetFault("Retour a la reference refuse."); return; }
        ready=false; Phase=ArcPhase.AligningSteering;
        InitializeArc();
    }
    [ContextMenu("Report Arc")]
    private void ReportArc()
    {
        StringBuilder report=new StringBuilder();
        report.AppendLine($"Phase={Phase}, Run={run}, distance centrale={DistanceTravelled:F4}/{totalLength:F4} m, " +
            $"cap ajoute={turnSign*DistanceTravelled/Mathf.Max(radiusUsed,0.001f)*Mathf.Rad2Deg:F3} deg, " +
            $"erreur pic={PeakAcceptedError*1000f:F3} mm, passages pic={PeakIterations}, faute={FaultReason}.");
        for (int j=0;j<4;j++)
            report.AppendLine($"Direction {steeringPivots[j]?.name} : {angles[j]:F3} deg (convention yaw corps).");
        if (solver)
            for (int i=0;i<6;i++) report.AppendLine($"Roulement {solver.WheelCenter(i)?.name} : {wheelTravel[i]:F4} m cumules projetes.");
        Debug.Log(report.ToString(),this);
    }
}
