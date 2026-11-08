using UnityEngine;
using Unity.Profiling;

[DisallowMultipleComponent]
public class RoverWaypointFollower : MonoBehaviour
{
    [SerializeField] private RoverKinematicActuator actuator;
    [SerializeField] private RoverWaypointPath path;
    [SerializeField] private bool run;
    [SerializeField] private bool maintainPoseWhilePaused=true;
    [Header("Commande du prototype, pas caracteristiques NASA")]
    [SerializeField, Min(0.01f)] private float cruiseSpeed=0.20f;
    [SerializeField, Min(0.01f)] private float lookAhead=1.50f;
    [SerializeField, Min(0f)] private float curvatureSlowdownGain=3f;
    [SerializeField, Min(0.001f)] private float acceleration=0.15f;
    [SerializeField, Min(0.001f)] private float deceleration=0.25f;
    [SerializeField, Min(0.01f)] private float arrivalSlowdownDistance=0.60f;
    [SerializeField, Min(0.005f)] private float arrivalTolerance=0.08f;
    [SerializeField, Min(0.05f)] private float maxCrossTrack=0.75f;
    [SerializeField, Min(0.1f)] private float maxSaturationDuration=5f;
    [SerializeField, Min(0.0001f)] private float maxSpatialStep=0.005f;
    [SerializeField, Range(1,64)] private int maxStepsPerFrame=8;

    public enum FollowState { Paused, Following, Arrived, Fault }
    public FollowState State { get; private set; }=FollowState.Paused;
    public RoverKinematicActuator Actuator=>actuator;
    public float Progress { get; private set; }
    public float CurrentSpeed { get; private set; }
    public float CurrentCrossTrack { get; private set; }
    public float PeakCrossTrack { get; private set; }
    public float PeakSpeed { get; private set; }
    public float PathLength=>path ? path.Length : 0f;
    public float EndDistance=>ready ? RoverWaypointPath.HorizontalDistance(actuator.CenterPosition,path.End) : float.PositiveInfinity;
    public string FaultReason { get; private set; }="";
    private bool ready;
    private float saturationTime;
    private static readonly ProfilerMarker Marker=new ProfilerMarker("Rover.KinematicUpdate");
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);

    private bool ConfigurationValid()
    {
        return actuator && actuator.transform==transform && actuator.isActiveAndEnabled && path &&
            Finite(cruiseSpeed) && cruiseSpeed>0f && Finite(lookAhead) && lookAhead>0f &&
            Finite(curvatureSlowdownGain) && curvatureSlowdownGain>=0f &&
            Finite(acceleration) && acceleration>0f && Finite(deceleration) && deceleration>0f &&
            Finite(arrivalSlowdownDistance) && arrivalSlowdownDistance>arrivalTolerance &&
            Finite(arrivalTolerance) && arrivalTolerance>0f && Finite(maxCrossTrack) && maxCrossTrack>0f &&
            Finite(maxSaturationDuration) && maxSaturationDuration>0f && Finite(maxSpatialStep) && maxSpatialStep>0f &&
            maxStepsPerFrame>0;
    }
    private void Start() { InitializeFollower(); }
    private bool InitializeFollower()
    {
        if (ready) return true;
        if (!ConfigurationValid()) { SetFault("Configuration du suivi invalide."); return false; }
        if (path.transform==transform || path.transform.IsChildOf(transform))
        { SetFault("Le parcours doit etre independant du rover."); return false; }
        for (int i=0;i<path.PointCount;i++)
            if (path.Point(i) && (path.Point(i)==transform || path.Point(i).IsChildOf(transform)))
            { SetFault("Un point du parcours est attache au rover."); return false; }
        if (!path.BuildCache(out string error)) { SetFault(error); return false; }
        if (!actuator.Initialize()) { SetFault(actuator.LastError); return false; }
        if (RoverWaypointPath.HorizontalDistance(actuator.CenterPosition,path.Sample(0f))>0.50f)
        { SetFault("Premier point trop loin du milieu des roues centrales (plus de 0.50 m)."); return false; }
        Progress=0f; CurrentSpeed=0f; PeakSpeed=0f; CurrentCrossTrack=0f; PeakCrossTrack=0f;
        saturationTime=0f; FaultReason=""; ready=true;
        if (!UpdateTracking()) return false;
        State=run ? FollowState.Following : FollowState.Paused;
        return true;
    }
    private void Update()
    {
        if (State==FollowState.Fault) return;
        using (Marker.Auto()) SimulateStep(Time.deltaTime);
    }
    public void SimulateStep(float dt)
    {
        if (!ready && !InitializeFollower()) return;
        if (State==FollowState.Fault) return;
        if (!ConfigurationValid() || !Finite(dt) || dt<0f)
        { SetFault("Parametres ou pas temporel invalides."); return; }
        if (!run || State==FollowState.Arrived)
        {
            CurrentSpeed=0f;
            if (State!=FollowState.Arrived) State=FollowState.Paused;
            if (maintainPoseWhilePaused && !actuator.TryAdvance(0f,actuator.Curvature,dt)) SetFault(actuator.LastError);
            return;
        }
        State=FollowState.Following;
        if (!UpdateTracking() || CheckArrival()) return;
        Vector3 target=path.Sample(Mathf.Min(path.Length,Progress+lookAhead));
        Vector3 offset=target-actuator.CenterPosition; offset.y=0f;
        Vector3 local=Quaternion.Inverse(actuator.Heading)*offset;
        if (local.z < -0.05f || offset.sqrMagnitude<0.000001f)
        { SetFault("Point de visee derriere le rover ou trop proche. Pas de marche arriere automatique."); return; }
        float requested=2f*local.x/offset.sqrMagnitude;
        float command=Mathf.Clamp(requested,actuator.MinCurvature,actuator.MaxCurvature);
        if (Mathf.Abs(command-requested)>0.00001f) saturationTime+=dt; else saturationTime=0f;
        if (saturationTime>maxSaturationDuration)
        { SetFault("Courbure demandee hors du domaine de direction pendant trop longtemps."); return; }
        float targetSpeed=cruiseSpeed/(1f+curvatureSlowdownGain*Mathf.Abs(command));
        float remaining=Mathf.Max(path.Length-Progress,EndDistance);
        targetSpeed*=Mathf.Clamp01(remaining/arrivalSlowdownDistance);
        if (CurrentSpeed<0.005f && Mathf.Abs(command-actuator.Curvature)>0.01f) targetSpeed=0f;
        CurrentSpeed=Mathf.MoveTowards(CurrentSpeed,targetSpeed,
            (targetSpeed>CurrentSpeed ? acceleration : deceleration)*dt);
        PeakSpeed=Mathf.Max(PeakSpeed,CurrentSpeed);
        float distance=CurrentSpeed*dt;
        if (Progress>path.Length-lookAhead) distance=Mathf.Min(distance,EndDistance);
        if (distance>maxSpatialStep*maxStepsPerFrame+0.000001f)
        { SetFault("Budget de sous-pas depasse. Aucun saut impose."); return; }
        int steps=Mathf.Max(1,Mathf.CeilToInt(distance/maxSpatialStep));
        float step=distance/steps,subDt=dt/steps;
        for (int i=0;i<steps;i++)
        {
            if (!actuator.TryAdvance(step,requested,subDt)) { SetFault(actuator.LastError); return; }
            if (!UpdateTracking() || CheckArrival()) return;
        }
    }
    private bool UpdateTracking()
    {
        float back=Mathf.Max(0f,Progress-0.25f);
        float ahead=Mathf.Min(path.Length,Progress+Mathf.Max(2f*lookAhead,1f));
        if (!path.ProjectWindow(actuator.CenterPosition,back,ahead,out float projected,out float error))
        { SetFault("Projection sur le parcours impossible."); return false; }
        Progress=Mathf.Max(Progress,projected); CurrentCrossTrack=error;
        PeakCrossTrack=Mathf.Max(PeakCrossTrack,error);
        if (error>maxCrossTrack) { SetFault("Ecart lateral au parcours au-dela du garde-fou."); return false; }
        return true;
    }
    private bool CheckArrival()
    {
        if (path.Length-Progress<=Mathf.Max(lookAhead,arrivalTolerance) && EndDistance<=arrivalTolerance)
        { State=FollowState.Arrived; run=false; CurrentSpeed=0f; return true; }
        return false;
    }
    private void SetFault(string reason)
    {
        State=FollowState.Fault; FaultReason=reason; run=false; CurrentSpeed=0f;
        Debug.LogWarning("Suivi arrete : "+reason,this);
    }
    [ContextMenu("Pause")]
    public void Pause()
    { run=false; CurrentSpeed=0f; if (State==FollowState.Following) State=FollowState.Paused; }
    [ContextMenu("Resume")]
    public void Resume()
    {
        if (!Application.isPlaying) return;
        if (!ready && !InitializeFollower()) return;
        if (State==FollowState.Fault || State==FollowState.Arrived) return;
        run=true; State=FollowState.Following;
    }
    [ContextMenu("Retry Current Pose After Fault")]
    private void RetryCurrentPose()
    {
        if (!Application.isPlaying || !ready || State!=FollowState.Fault) return;
        Physics.SyncTransforms();
        if (!actuator.TryAdvance(0f,actuator.Curvature,0f)) return;
        if (!UpdateTracking()) return;
        State=FollowState.Paused; FaultReason=""; saturationTime=0f; run=false;
    }
    [ContextMenu("Restart Path From Reference")]
    private void RestartMenu() { RestartFromReference(); }
    public bool RestartFromReference()
    {
        if (!Application.isPlaying || !actuator) return false;
        run=false;
        if (!actuator.ResetToReference()) { SetFault(actuator.LastError); return false; }
        ready=false; State=FollowState.Paused;
        return InitializeFollower();
    }
    [ContextMenu("Report Path Follow")]
    private void ReportPathFollow()
    {
        if (!actuator) { Debug.LogWarning("Actuator non assigne.",this); return; }
        Debug.Log($"Etat={State}, progression={Progress:F3}/{PathLength:F3} m, distance finale={EndDistance:F3} m, " +
            $"trajet central={actuator.DistanceTravelled:F3} m, vitesse={CurrentSpeed:F3} m/s, pic vitesse={PeakSpeed:F3} m/s, " +
            $"ecart lateral pic={PeakCrossTrack:F3} m, courbure={actuator.Curvature:F4} 1/m, " +
            $"erreur appuis pic={actuator.PeakAcceptedError*1000f:F3} mm, passages pic={actuator.PeakIterations}, faute={FaultReason}.",this);
    }
}
