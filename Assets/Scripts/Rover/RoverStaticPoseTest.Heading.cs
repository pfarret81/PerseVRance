using UnityEngine;

// Same runtime assembly and folder as the main solver. Not a separate component.
public partial class RoverStaticPoseTest
{
    private bool headingOverrideEnabled;
    private Quaternion headingOverride;
    private Quaternion PoseHeading => headingOverrideEnabled ? headingOverride : referenceRotation;
    public float WheelRadius => radius;

    // The persistent mechanical reference is not recaptured or overwritten.
    public bool TrySolveAtHeading(Vector3 anchor, Quaternion heading)
    {
        Vector3 forward=Vector3.ProjectOnPlane(heading*Vector3.forward,Vector3.up);
        if (float.IsNaN(forward.x) || float.IsInfinity(forward.x) ||
            float.IsNaN(forward.z) || float.IsInfinity(forward.z) || forward.sqrMagnitude<0.5f)
        { LastFailure=Failure.InvalidConfiguration; return false; }
        bool oldEnabled=headingOverrideEnabled;
        Quaternion oldHeading=headingOverride;
        headingOverrideEnabled=true;
        headingOverride=Quaternion.LookRotation(forward.normalized,Vector3.up);
        try { return TrySolveAt(anchor,false); }
        finally { headingOverrideEnabled=oldEnabled; headingOverride=oldHeading; }
    }

    // Cold initialization only; all transforms restored before returning.
    public bool TryGetReferenceWheelGeometry(Vector3[] coordinates)
    {
        if (!ConfigurationValid(true) || coordinates==null || coordinates.Length!=6) return false;
        Vector3 savedPosition=transform.position;
        Quaternion savedRotation=transform.rotation;
        Quaternion[] saved=new Quaternion[4];
        for (int i=0;i<4;i++) saved[i]=joints[i].localRotation;
        bool oldEnabled=headingOverrideEnabled;
        headingOverrideEnabled=false;
        try
        {
            ApplyPose(referencePosition,new float[6]);
            for (int i=0;i<6;i++) coordinates[i]=transform.InverseTransformPoint(wheels[i].position);
            return true;
        }
        finally
        {
            transform.SetPositionAndRotation(savedPosition,savedRotation);
            for (int i=0;i<4;i++) joints[i].localRotation=saved[i];
            headingOverrideEnabled=oldEnabled;
        }
    }
}
