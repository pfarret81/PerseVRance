using UnityEngine;
using Unity.Profiling;

// Historical name and serialized fields retained for scene migration.
public partial class RoverStaticPoseTest : MonoBehaviour
{
    [Header("Surfaces communes au solveur et au diagnostic")]
    [SerializeField] private Collider[] surfaces;
    [Header("Rockers G/D puis bogies G/D")]
    [SerializeField] private Transform[] joints = new Transform[4];
    [Header("Six Empty E wheel, ordre libre mais stable")]
    [SerializeField] private Transform[] wheels = new Transform[6];
    [SerializeField, Min(0.001f)] private float radius = 0.2627f;
    [Header("Garde-fous du prototype, pas limites NASA")]
    [SerializeField, Min(0.00001f)] private float contactTolerance = 0.001f;
    [SerializeField, Range(1,100)] private int maxIterations = 60;
    [SerializeField, Min(0.00001f)] private float derivativeStep = 0.0005f;
    [SerializeField, Min(0f)] private float regularization = 0.00001f;
    [SerializeField, Min(0.001f)] private float maxHeightCorrection = 0.15f;
    [SerializeField, Range(1f,45f)] private float maxPoseAngle = 15f;
    [SerializeField, Min(0.01f)] private float probeStartHeight = 1f;
    [SerializeField, Min(0.1f)] private float probeLength = 10f;

    [SerializeField, HideInInspector] private bool hasReference;
    [SerializeField, HideInInspector] private Vector3 referencePosition;
    [SerializeField, HideInInspector] private Quaternion referenceRotation;
    [SerializeField, HideInInspector] private Quaternion[] referenceJoints;
    [SerializeField, HideInInspector] private float[] axisSigns;

    public enum Failure { None, InvalidConfiguration, MissingSurface,
        NumericalFailure, OutsideTestDomain, NoConvergence }
    public Failure LastFailure { get; private set; }
    public int LastIterations { get; private set; }
    public float LastMaxError { get; private set; } = float.PositiveInfinity;
    public Vector3 LastAnchor { get; private set; }
    public Vector3 ReferencePosition => referencePosition;
    public Vector3 ReferenceForward => Vector3.ProjectOnPlane(
        referenceRotation * Vector3.forward, Vector3.up).normalized;
    public bool HasRuntimeSolution => hasSolution;
    public int WheelCount => wheels == null ? 0 : wheels.Length;
    public int JointCount => joints == null ? 0 : joints.Length;
    public Quaternion WheelRotation(int i) => wheels[i].localRotation;
    public Quaternion JointRotation(int i) => joints[i].localRotation;
    public Transform WheelCenter(int i) => wheels != null && i >= 0 && i < wheels.Length ? wheels[i] : null;

    private readonly float[] p = new float[6], errors = new float[6];
    private readonly float[] shiftedErrors = new float[6], delta = new float[6], accepted = new float[6];
    private readonly float[,] jacobian = new float[6,6];
    private readonly double[,] system = new double[6,7];
    private readonly Quaternion[] savedJoints = new Quaternion[4], initialWheels = new Quaternion[6];
    private readonly Vector3[] savedWheelPositions = new Vector3[6];
    private bool initialized, hasSolution;
    private static readonly ProfilerMarker SolveMarker = new ProfilerMarker("Rover.SolvePose");
    private void OnEnable() { InvalidateRuntime(); }
    public void InvalidateRuntime() { initialized = false; hasSolution = false; }

    private bool ConfigurationValid(bool requireReference)
    {
        if (transform.parent != null || (transform.lossyScale-Vector3.one).sqrMagnitude > 0.000001f ||
            surfaces == null || surfaces.Length == 0 || joints == null || joints.Length != 4 ||
            wheels == null || wheels.Length != 6 || !(radius > 0f) || !(contactTolerance > 0f) ||
            !(derivativeStep > 0f) || maxIterations < 1 || !(maxHeightCorrection > 0f) ||
            !(maxPoseAngle > 0f) || !(probeLength > 0f) || !(probeStartHeight > 0f) ||
            !(regularization >= 0f)) return false;
        for (int i=0;i<4;i++)
        {
            if (!joints[i] || joints[i]==transform || !joints[i].IsChildOf(transform)) return false;
            for (int j=0;j<i;j++) if (joints[i]==joints[j]) return false;
        }
        for (int i=0;i<6;i++)
        {
            if (!wheels[i] || wheels[i]==transform || !wheels[i].IsChildOf(transform)) return false;
            for (int j=0;j<i;j++) if (wheels[i]==wheels[j]) return false;
            for (int j=0;j<4;j++) if (wheels[i]==joints[j]) return false;
        }
        for (int i=0;i<surfaces.Length;i++)
            if (surfaces[i] && (surfaces[i].transform==transform || surfaces[i].transform.IsChildOf(transform))) return false;
        if (!requireReference) return true;
        return hasReference && referenceJoints != null && referenceJoints.Length==4 &&
            axisSigns != null && axisSigns.Length==4 && ReferenceForward.sqrMagnitude > 0.5f;
    }
    public bool InitializeRuntime()
    {
        if (initialized) return true;
        if (!ConfigurationValid(true)) { LastFailure=Failure.InvalidConfiguration; return false; }
        for (int i=0;i<6;i++) initialWheels[i]=wheels[i].localRotation;
        Physics.SyncTransforms();
        initialized=true; hasSolution=false; LastAnchor=referencePosition; LastFailure=Failure.None;
        return true;
    }
    public bool ResetRuntimePose()
    {
        if (!InitializeRuntime()) return false;
        hasSolution=false;
        System.Array.Clear(p,0,6); ApplyPose(referencePosition,p);
        for (int i=0;i<6;i++) wheels[i].localRotation=initialWheels[i];
        LastAnchor=referencePosition;
        return TrySolveAt(referencePosition,false);
    }

    // Single contact query used by BOTH solver and gizmos. No separate surface list.
    public bool TryProbeGround(Vector3 wheelCenter, out RaycastHit closestHit)
    {
        closestHit=default;
        if (surfaces==null) return false;
        Ray ray=new Ray(wheelCenter+Vector3.up*probeStartHeight,Vector3.down);
        bool found=false;
        float nearest=probeLength;
        for (int i=0;i<surfaces.Length;i++)
        {
            Collider surface=surfaces[i];
            if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy || surface.isTrigger) continue;
            if (surface.Raycast(ray,out RaycastHit hit,nearest))
            { nearest=hit.distance; closestHit=hit; found=true; }
        }
        return found;
    }
    public bool TrySolveAt(Vector3 anchor,bool rotateWheels)
    {
        using (SolveMarker.Auto()) return SolveTransaction(anchor,rotateWheels);
    }
    private bool SolveTransaction(Vector3 anchor,bool rotateWheels)
    {
        LastIterations=0; LastMaxError=float.PositiveInfinity; LastFailure=Failure.None;
        if (!InitializeRuntime() || !Finite(anchor.x) || !Finite(anchor.y) || !Finite(anchor.z))
        { LastFailure=Failure.InvalidConfiguration; return false; }
        Vector3 savedPosition=transform.position;
        Quaternion savedRotation=transform.rotation;
        for (int i=0;i<4;i++) savedJoints[i]=joints[i].localRotation;
        for (int i=0;i<6;i++) savedWheelPositions[i]=wheels[i].position;
        System.Array.Clear(p,0,6);
        if (hasSolution) { System.Array.Copy(accepted,p,6); p[0]+=LastAnchor.y-anchor.y; }
        bool success=false;
        for (int iteration=0;iteration<maxIterations;iteration++)
        {
            LastIterations=iteration+1;
            if (!WithinDomain(p)) { LastFailure=Failure.OutsideTestDomain; break; }
            ApplyPose(anchor,p);
            if (!ReadErrors(errors)) break;
            LastMaxError=MaxAbs(errors);
            if (LastMaxError<=contactTolerance) { success=true; break; }
            bool derivativeValid=true;
            for (int col=0;col<6;col++)
            {
                float old=p[col]; p[col]=old+derivativeStep;
                ApplyPose(anchor,p);
                bool valid=ReadErrors(shiftedErrors);
                p[col]=old;
                if (!valid) { derivativeValid=false; break; }
                for (int row=0;row<6;row++) jacobian[row,col]=(shiftedErrors[row]-errors[row])/derivativeStep;
            }
            ApplyPose(anchor,p);
            if (!derivativeValid) break;
            System.Array.Clear(system,0,system.Length);
            for (int row=0;row<6;row++)
            {
                for (int col=0;col<6;col++)
                    for (int k=0;k<6;k++) system[row,col]+=jacobian[k,row]*jacobian[k,col];
                system[row,row]+=regularization;
                for (int k=0;k<6;k++) system[row,6]-=jacobian[k,row]*errors[k];
            }
            if (!SolveLinearSystem(system,delta)) { LastFailure=Failure.NumericalFailure; break; }
            float scale=1f;
            for (int i=0;i<6;i++)
            {
                float maxStep=i==0 ? 0.02f : 0.03f;
                if (Mathf.Abs(delta[i])>maxStep) scale=Mathf.Min(scale,maxStep/Mathf.Abs(delta[i]));
            }
            for (int i=0;i<6;i++) p[i]+=delta[i]*scale;
        }
        if (!success && LastFailure==Failure.None)
        {
            if (!WithinDomain(p)) LastFailure=Failure.OutsideTestDomain;
            else
            {
                ApplyPose(anchor,p);
                if (ReadErrors(errors))
                {
                    LastMaxError=MaxAbs(errors); success=LastMaxError<=contactTolerance;
                    if (!success) LastFailure=Failure.NoConvergence;
                }
            }
        }
        if (!success)
        {
            transform.SetPositionAndRotation(savedPosition,savedRotation);
            for (int i=0;i<4;i++) joints[i].localRotation=savedJoints[i];
            return false;
        }
        ApplyPose(anchor,p);
        if (rotateWheels) RollWheels();
        System.Array.Copy(p,accepted,6); LastAnchor=anchor; hasSolution=true; LastFailure=Failure.None;
        return true;
    }
    private void ApplyPose(Vector3 anchor,float[] values)
    {
        transform.SetPositionAndRotation(anchor + Vector3.up * values[0], PoseHeading *
            Quaternion.AngleAxis(values[1]*Mathf.Rad2Deg,Vector3.right) *
            Quaternion.AngleAxis(values[2]*Mathf.Rad2Deg,Vector3.forward));
        SetJoint(0,values[3]); SetJoint(1,-values[3]); SetJoint(2,values[4]); SetJoint(3,values[5]);
    }
    private void SetJoint(int index,float angle)
    {
        joints[index].localRotation=referenceJoints[index]*Quaternion.AngleAxis(
            angle*Mathf.Rad2Deg*axisSigns[index],Vector3.forward);
    }
    private bool WithinDomain(float[] values)
    {
        if (!Finite(values[0]) || Mathf.Abs(values[0])>maxHeightCorrection) return false;
        for (int i=1;i<6;i++)
            if (!Finite(values[i]) || Mathf.Abs(values[i])>maxPoseAngle*Mathf.Deg2Rad) return false;
        return true;
    }
    private bool ReadErrors(float[] result)
    {
        for (int i=0;i<6;i++)
        {
            if (!TryProbeGround(wheels[i].position,out RaycastHit hit))
            { LastFailure=Failure.MissingSurface; LastMaxError=float.PositiveInfinity; return false; }
            result[i]=wheels[i].position.y-hit.point.y-radius;
            if (!Finite(result[i])) { LastFailure=Failure.NumericalFailure; return false; }
        }
        return true;
    }
    private void RollWheels()
    {
        Vector3 heading=ReferenceForward;
        Vector3 rollingAxis=Vector3.Cross(Vector3.up,heading).normalized;
        for (int i=0;i<6;i++)
        {
            float distance=Vector3.Dot(wheels[i].position-savedWheelPositions[i],heading);
            float sign=Mathf.Sign(Vector3.Dot(wheels[i].TransformDirection(Vector3.right).normalized,rollingAxis));
            wheels[i].localRotation*=Quaternion.AngleAxis(distance/radius*Mathf.Rad2Deg*sign,Vector3.right);
        }
    }
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    private static float MaxAbs(float[] values)
    {
        float result=0f;
        for (int i=0;i<values.Length;i++) result=Mathf.Max(result,Mathf.Abs(values[i]));
        return result;
    }
    [ContextMenu("Capture Reference Pose")]
    private void CaptureReferencePose()
    {
        if (Application.isPlaying || !ConfigurationValid(false))
        { Debug.LogWarning("Capture refusee : verifier references, racine sans parent et echelle 1.",this); return; }
        float[] signs=new float[4]; Quaternion[] rotations=new Quaternion[4];
        for (int i=0;i<4;i++)
        {
            float alignment=Vector3.Dot(joints[i].TransformDirection(Vector3.forward),transform.right);
            if (Mathf.Abs(alignment)<0.5f) { Debug.LogWarning("Axe inattendu.",joints[i]); return; }
            signs[i]=Mathf.Sign(alignment); rotations[i]=joints[i].localRotation;
        }
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this,"Capture Rover Reference Pose");
#endif
        referencePosition=transform.position; referenceRotation=transform.rotation;
        referenceJoints=rotations; axisSigns=signs; hasReference=true; InvalidateRuntime();
        Debug.Log("Reference capturee.",this);
    }
    private void RecordPoseUndo()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObjects(new Object[] { transform,joints[0],joints[1],joints[2],joints[3] },"Rover Static Pose");
#endif
    }
    [ContextMenu("Restore Reference Pose")]
    private void RestoreReferencePose()
    {
        if (Application.isPlaying || !ConfigurationValid(true)) return;
        RecordPoseUndo(); System.Array.Clear(p,0,6); ApplyPose(referencePosition,p); InvalidateRuntime();
    }
    [ContextMenu("Solve Static Test Pose")]
    private void SolveStaticTestPose()
    {
        if (Application.isPlaying || !ConfigurationValid(true)) return;
        RecordPoseUndo(); InvalidateRuntime(); InitializeRuntime();
        bool ok=TrySolveAt(referencePosition,false);
        Debug.Log($"Resolution statique : {ok}, passages={LastIterations}, erreur={LastMaxError*1000f:F3} mm, statut={LastFailure}.",this);
        InvalidateRuntime();
    }
    [ContextMenu("Report Last Solve")]
    private void ReportLastSolve()
    {
        Debug.Log($"Passages={LastIterations}, erreur={LastMaxError*1000f:F3} mm, cache={hasSolution}, statut={LastFailure}.",this);
    }
private static bool SolveLinearSystem(
    double[,] matrix, float[] result)
    {

        for (int column = 0; column < 6; column++)
        {
            int pivot = column;

            for (int row = column + 1; row < 6; row++)
            {
                if (System.Math.Abs(matrix[row, column]) >
                    System.Math.Abs(matrix[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (System.Math.Abs(matrix[pivot, column]) < 1e-12)
                return false;

            for (int k = column; k <= 6; k++)
            {
                double temporary = matrix[column, k];
                matrix[column, k] = matrix[pivot, k];
                matrix[pivot, k] = temporary;
            }

            double divisor = matrix[column, column];

            for (int k = column; k <= 6; k++)
                matrix[column, k] /= divisor;

            for (int row = 0; row < 6; row++)
            {
                if (row == column)
                    continue;

                double factor = matrix[row, column];

                for (int k = column; k <= 6; k++)
                    matrix[row, k] -= factor * matrix[column, k];
            }
        }

        for (int i = 0; i < 6; i++)
        {
            double value = matrix[i, 6];

            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;

            result[i] = (float)value;
        }

        return true;
    }
}
