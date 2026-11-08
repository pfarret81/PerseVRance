using UnityEngine;
using Unity.Profiling;

// No Update: exactly one driver is allowed to call this component.
[DisallowMultipleComponent]
public class RoverKinematicActuator : MonoBehaviour
{
    [SerializeField] private RoverStaticPoseTest solver;
    [Header("Avant G, avant D, arriere G, arriere D")]
    [SerializeField] private Transform[] steeringPivots=new Transform[4];
    [Header("Garde-fous de commande du prototype")]
    [SerializeField, Range(1f,80f)] private float maxSteeringAngle=35f;
    [SerializeField, Min(1f)] private float steeringSpeed=30f;
    public RoverStaticPoseTest Solver=>solver;
    public Quaternion Heading { get; private set; }
    public float Curvature { get; private set; }
    public float MinCurvature { get; private set; }
    public float MaxCurvature { get; private set; }
    public float DistanceTravelled { get; private set; }
    public float PeakAcceptedError { get; private set; }
    public int PeakIterations { get; private set; }
    public string LastError { get; private set; }="";
    public bool LastCommandClamped { get; private set; }
    public Vector3 CenterPosition => ready ?
        (solver.WheelCenter(middleA).position+solver.WheelCenter(middleB).position)*0.5f : transform.position;

    private bool ready;
    private int middleA,middleB;
    private Vector3 middle;
    private double anchorX,anchorZ;
    private RoverStraightPathDriver legacyStraight;
    private RoverArcPathDriver legacyArc;
    private readonly Vector3[] referenceWheels=new Vector3[6],previousWheels=new Vector3[6];
    private readonly Quaternion[] restSteering=new Quaternion[4];
    private readonly float[] signs=new float[4],angles=new float[4],workAngles=new float[4],candidateAngles=new float[4];
    private readonly int[] steerWheelIndices=new int[4];
    private readonly bool[] used=new bool[6];
    private static readonly ProfilerMarker Marker=new ProfilerMarker("Rover.Actuation");
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private bool Fail(string reason) { LastError=reason; return false; }

    public bool Initialize()
    {
        if (ready) return true;
        legacyStraight=GetComponent<RoverStraightPathDriver>();
        legacyArc=GetComponent<RoverArcPathDriver>();
        if (!solver || solver.transform!=transform || !solver.isActiveAndEnabled ||
            (legacyStraight && legacyStraight.isActiveAndEnabled) || (legacyArc && legacyArc.isActiveAndEnabled) ||
            steeringPivots==null || steeringPivots.Length!=4 || !(maxSteeringAngle>0f) ||
            !Finite(maxSteeringAngle) || !(steeringSpeed>0f) || !Finite(steeringSpeed))
            return Fail("Configuration invalide ou ancien driver encore actif.");
        if (!solver.InitializeRuntime() || !solver.TryGetReferenceWheelGeometry(referenceWheels))
            return Fail("Reference mecanique indisponible.");
        System.Array.Clear(used,0,6);
        for (int j=0;j<4;j++)
        {
            Transform pivot=steeringPivots[j];
            if (!pivot || pivot==transform || !pivot.IsChildOf(transform)) return Fail("Pivot de direction invalide.");
            for (int k=0;k<j;k++) if (pivot==steeringPivots[k]) return Fail("Pivot duplique.");
            int index=-1;
            for (int i=0;i<6;i++) if (solver.WheelCenter(i).IsChildOf(pivot))
            { if (index!=-1) return Fail("Plusieurs roues sous un pivot."); index=i; }
            if (index<0 || used[index]) return Fail("Association pivot/roue invalide.");
            used[index]=true; steerWheelIndices[j]=index;
            float alignment=Vector3.Dot(pivot.TransformDirection(Vector3.forward),transform.up);
            if (Mathf.Abs(alignment)<0.5f) return Fail("Axe Z de direction inattendu.");
            signs[j]=Mathf.Sign(alignment); restSteering[j]=pivot.localRotation;
        }
        middleA=-1; middleB=-1;
        for (int i=0;i<6;i++) if (!used[i])
        { if (middleA<0) middleA=i; else if (middleB<0) middleB=i; else return Fail("Trop de roues centrales."); }
        if (middleA<0 || middleB<0) return Fail("Deux roues centrales attendues.");
        middle=(referenceWheels[middleA]+referenceWheels[middleB])*0.5f;
        float maxLateral=0f;
        for (int i=0;i<6;i++) maxLateral=Mathf.Max(maxLateral,Mathf.Abs(referenceWheels[i].x-middle.x));
        for (int j=0;j<4;j++)
        {
            Vector3 p=referenceWheels[steerWheelIndices[j]];
            bool front=j<2,left=j%2==0;
            if ((front && p.z<=middle.z) || (!front && p.z>=middle.z) ||
                (left && p.x>=middle.x) || (!left && p.x<=middle.x)) return Fail("Ordre des pivots incorrect.");
        }
        float upper=1f/(maxLateral+0.05f);
        MinCurvature=-FindLimit(-1f,upper); MaxCurvature=FindLimit(1f,upper);
        ready=true;
        return ResetToReference();
    }
    private float FindLimit(float direction,float upper)
    {
        float low=0f,high=upper;
        for (int i=0;i<24;i++)
        { float k=(low+high)*0.5f; if (TryAngles(direction*k,workAngles)) low=k; else high=k; }
        return low*0.999f;
    }
    private bool TryAngles(float k,float[] output)
    {
        for (int i=0;i<6;i++) if (1f-k*(referenceWheels[i].x-middle.x)<=0.05f) return false;
        for (int j=0;j<4;j++)
        {
            Vector3 p=referenceWheels[steerWheelIndices[j]];
            output[j]=Mathf.Atan2(k*(p.z-middle.z),1f-k*(p.x-middle.x))*Mathf.Rad2Deg;
            if (Mathf.Abs(output[j])>maxSteeringAngle) return false;
        }
        return true;
    }
    private float MaximumChange(float[] goal)
    {
        float result=0f;
        for (int j=0;j<4;j++) result=Mathf.Max(result,Mathf.Abs(goal[j]-angles[j]));
        return result;
    }
    private void ApplyAngles(float[] values)
    {
        for (int j=0;j<4;j++) steeringPivots[j].localRotation=restSteering[j]*
            Quaternion.AngleAxis(values[j]*signs[j],Vector3.forward);
    }
    public bool ResetToReference()
    {
        if (!ready) return Initialize();
        System.Array.Clear(angles,0,4); ApplyAngles(angles);
        if (!solver.ResetRuntimePose()) return Fail("Pose de depart refusee : "+solver.LastFailure);
        Heading=Quaternion.LookRotation(solver.ReferenceForward,Vector3.up);
        anchorX=transform.position.x; anchorZ=transform.position.z;
        Curvature=0f; DistanceTravelled=0f; PeakAcceptedError=solver.LastMaxError;
        PeakIterations=solver.LastIterations; LastError=""; LastCommandClamped=false;
        return true;
    }
    public bool TryAdvance(float distance,float requestedCurvature,float dt)
    {
        using (Marker.Auto()) return AdvanceTransaction(distance,requestedCurvature,dt);
    }
    private bool AdvanceTransaction(float distance,float requestedCurvature,float dt)
    {
        if (!Initialize()) return false;
        if (!Finite(distance) || distance<0f || !Finite(requestedCurvature) || !Finite(dt) || dt<0f ||
            !solver.isActiveAndEnabled || (legacyStraight && legacyStraight.isActiveAndEnabled) ||
            (legacyArc && legacyArc.isActiveAndEnabled)) return Fail("Commande invalide ou pilotes concurrents.");
        float target=Mathf.Clamp(requestedCurvature,MinCurvature,MaxCurvature);
        LastCommandClamped=Mathf.Abs(target-requestedCurvature)>0.000001f;
        if (!TryAngles(target,candidateAngles)) return Fail("Courbure cible invalide.");
        float next=target;
        float allowed=steeringSpeed*dt;
        if (MaximumChange(candidateAngles)>allowed)
        {
            float low=0f,high=1f;
            for (int iteration=0;iteration<12;iteration++)
            {
                float blend=(low+high)*0.5f;
                float k=Mathf.Lerp(Curvature,target,blend);
                if (TryAngles(k,workAngles) && MaximumChange(workAngles)<=allowed) low=blend; else high=blend;
            }
            next=Mathf.Lerp(Curvature,target,low);
            if (!TryAngles(next,candidateAngles)) return Fail("Limitation de braquage impossible.");
        }
        // Stable SE(2) increment: do not subtract a far-away ICR at tiny curvature.
        float phi=distance*next;
        float phi2=phi*phi;
        float sin=Mathf.Sin(phi),halfSin=Mathf.Sin(phi*0.5f);
        float oneMinusCos=2f*halfSin*halfSin;
        float centerX,centerZ;
        if (Mathf.Abs(phi)<0.01f)
        {
            centerX=distance*phi*(0.5f-phi2/24f+phi2*phi2/720f);
            centerZ=distance*(1f-phi2/6f+phi2*phi2/120f);
        }
        else
        {
            centerX=distance*oneMinusCos/phi;
            centerZ=distance*sin/phi;
        }
        Vector3 localDelta=new Vector3(
            centerX+middle.x*oneMinusCos-middle.z*sin,0f,
            centerZ+middle.z*oneMinusCos+middle.x*sin);
        Vector3 worldDelta=Heading*localDelta;
        double candidateX=anchorX+worldDelta.x,candidateZ=anchorZ+worldDelta.z;
        Vector3 anchor=new Vector3((float)candidateX,transform.position.y,(float)candidateZ);
        Quaternion heading=(Quaternion.AngleAxis(phi*Mathf.Rad2Deg,Vector3.up)*Heading).normalized;
        for (int i=0;i<6;i++) previousWheels[i]=solver.WheelCenter(i).position;
        ApplyAngles(candidateAngles);
        if (!solver.TrySolveAtHeading(anchor,heading))
        { ApplyAngles(angles); return Fail("Pose refusee : "+solver.LastFailure); }
        if (distance>0f)
        {
            for (int i=0;i<6;i++)
            {
                Transform wheel=solver.WheelCenter(i);
                Vector3 normal=Vector3.up;
                if (solver.TryProbeGround(wheel.position,out RaycastHit hit)) normal=hit.normal;
                Vector3 rolling=Vector3.Cross(wheel.TransformDirection(Vector3.right).normalized,normal).normalized;
                float ds=Vector3.Dot(wheel.position-previousWheels[i],rolling);
                wheel.localRotation*=Quaternion.AngleAxis(ds/solver.WheelRadius*Mathf.Rad2Deg,Vector3.right);
            }
        }
        System.Array.Copy(candidateAngles,angles,4);
        Curvature=next; Heading=heading; anchorX=candidateX; anchorZ=candidateZ; DistanceTravelled+=distance;
        PeakAcceptedError=Mathf.Max(PeakAcceptedError,solver.LastMaxError);
        PeakIterations=Mathf.Max(PeakIterations,solver.LastIterations);
        LastError=""; return true;
    }
}
