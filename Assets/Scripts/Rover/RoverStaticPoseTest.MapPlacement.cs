using System.Collections.Generic;
using UnityEngine;

// Same folder/assembly as RoverStaticPoseTest.cs. Not a separate component.
public partial class RoverStaticPoseTest
{
    [SerializeField, HideInInspector] private bool useScenePhysicsSurfaces;
    private readonly RaycastHit[] sceneProbeHits = new RaycastHit[128];
    private HashSet<Collider> sceneAllowedSurfaces;
    private Collider[] sceneAllowedSource;
    private int sceneSurfaceLayerMask;
    private bool sceneProbeWasUsed;
    public int SceneProbeOverflowCount { get; private set; }

    private void BuildSceneSurfaceFilter()
    {
        if (sceneAllowedSurfaces != null && ReferenceEquals(sceneAllowedSource, surfaces)) return;
        if (sceneAllowedSurfaces == null) sceneAllowedSurfaces = new HashSet<Collider>();
        else sceneAllowedSurfaces.Clear();
        sceneAllowedSource = surfaces;
        sceneSurfaceLayerMask = 0;
        if (surfaces == null) return;
        foreach (Collider c in surfaces)
            if (c)
            {
                sceneAllowedSurfaces.Add(c);
                sceneSurfaceLayerMask |= 1 << c.gameObject.layer;
            }
    }

    private bool TryProbeScenePhysics(Vector3 wheelCenter, out RaycastHit hit)
    {
        sceneProbeWasUsed = true;
        return TrySceneSurfaceRay(new Ray(wheelCenter + Vector3.up * probeStartHeight,
            Vector3.down), probeLength, out hit);
    }

    private bool TrySceneSurfaceRay(Ray ray, float length, out RaycastHit closest)
    {
        closest = default;
        BuildSceneSurfaceFilter();
        if (sceneAllowedSurfaces.Count == 0) return false;
        int count = Physics.RaycastNonAlloc(ray, sceneProbeHits, length,
            sceneSurfaceLayerMask, QueryTriggerInteraction.Ignore);
        // Full buffers are unordered and may omit the closest allowed surface.
        if (count == sceneProbeHits.Length) { SceneProbeOverflowCount++; return false; }
        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = sceneProbeHits[i];
            if (!candidate.collider || !sceneAllowedSurfaces.Contains(candidate.collider)) continue;
            if (candidate.distance < nearest)
            { nearest = candidate.distance; closest = candidate; found = true; }
        }
        return found;
    }

#if UNITY_EDITOR
    public bool EditorTryRelocate(Vector3 groundPoint, float yawDegrees,
        Collider[] newSurfaces, Transform[] steeringPivots, bool commit,
        Vector3[] previewWheels, out Vector3 previewRoot, out Quaternion previewRotation,
        out Vector3 actualMiddle, out string message)
    {
        previewRoot = transform.position; previewRotation = transform.rotation;
        actualMiddle = groundPoint; message = "";
        if (Application.isPlaying || !Finite(groundPoint.x) || !Finite(groundPoint.y) ||
            !Finite(groundPoint.z) || !Finite(yawDegrees) || newSurfaces == null ||
            newSurfaces.Length == 0 || steeringPivots == null || steeringPivots.Length != 4 ||
            previewWheels == null || previewWheels.Length != 6)
        { message = "Configuration ou position invalide ; operation hors Play uniquement."; return false; }

        Collider[] oldSurfaces = surfaces;
        bool oldMode = useScenePhysicsSurfaces;
        Vector3 oldReferencePosition = referencePosition, oldPosition = transform.position;
        Quaternion oldReferenceRotation = referenceRotation, oldRotation = transform.rotation;
        Quaternion[] oldJoints = new Quaternion[4];
        // Validate before dereferencing joint transforms.
        surfaces = newSurfaces;
        if (!ConfigurationValid(true))
        { surfaces = oldSurfaces; message = "References mecaniques invalides : racine, echelle, articulations ou capture manquante."; return false; }
        for (int i = 0; i < 4; i++) oldJoints[i] = joints[i].localRotation;
        bool keep = false;
        try
        {
            useScenePhysicsSurfaces = true; sceneAllowedSource = null;
            bool[] steered = new bool[6];
            for (int j = 0; j < 4; j++)
            {
                Transform pivot = steeringPivots[j];
                if (!pivot || pivot == transform || !pivot.IsChildOf(transform))
                { message = "Pivot de direction invalide."; return false; }
                for (int k = 0; k < j; k++) if (pivot == steeringPivots[k])
                { message = "Pivot de direction duplique."; return false; }
                int found = -1;
                for (int i = 0; i < 6; i++) if (wheels[i].IsChildOf(pivot))
                {
                    if (found != -1) { message = "Plusieurs roues sous un pivot de direction."; return false; }
                    found = i;
                }
                if (found < 0 || steered[found])
                { message = "Association direction/roue invalide."; return false; }
                steered[found] = true;
            }
            Vector3[] nominal = new Vector3[6];
            if (!TryGetReferenceWheelGeometry(nominal))
            { message = "Impossible de lire la geometrie de reference."; return false; }
            Vector3 middle = Vector3.zero; int middleCount = 0;
            for (int i = 0; i < 6; i++) if (!steered[i]) { middle += nominal[i]; middleCount++; }
            if (middleCount != 2) { message = "Deux roues centrales attendues."; return false; }
            middle *= 0.5f;
            Quaternion yaw = Quaternion.AngleAxis(yawDegrees, Vector3.up);
            Vector3 offset = yaw * new Vector3(middle.x, 0f, middle.z);
            referencePosition = groundPoint - offset;
            referenceRotation = yaw;
            float[] zero = new float[6];
            ApplyPose(referencePosition, zero);
            Physics.SyncTransforms();
            float sum = 0f;
            int overflowBefore = SceneProbeOverflowCount;
            for (int i = 0; i < 6; i++)
            {
                if (!EditorTrySurfaceAt(wheels[i].position, out RaycastHit hit))
                { message = SceneProbeOverflowCount > overflowBefore ?
                    "Tampon de sondes plein : placement refuse." :
                    "Surface absente sous la roue " + wheels[i].name + "."; return false; }
                sum += hit.point.y + radius - (wheels[i].position.y - transform.position.y);
            }
            referencePosition = new Vector3(referencePosition.x, sum / 6f, referencePosition.z);
            ApplyPose(referencePosition, zero);
            InvalidateRuntime();
            sceneProbeWasUsed = false;
            bool solved = TrySolveAtHeading(referencePosition, yaw);
            if (!sceneProbeWasUsed)
            { message = "Edition de TryProbeGround manquante : appliquer le remplacement du README."; return false; }
            if (!solved)
            { message = SceneProbeOverflowCount > overflowBefore ? "Tampon de sondes plein : placement refuse." :
                "Pose refusee : " + LastFailure + ", residu=" + (LastMaxError * 1000f).ToString("F3") + " mm."; return false; }
            previewRoot = transform.position; previewRotation = transform.rotation;
            actualMiddle = Vector3.zero;
            for (int i = 0; i < 6; i++)
            { previewWheels[i] = wheels[i].position; if (!steered[i]) actualMiddle += previewWheels[i]; }
            actualMiddle *= 0.5f;
            message = "Pose acceptee : " + LastIterations + " passages, residu=" +
                (LastMaxError * 1000f).ToString("F3") + " mm, surfaces=" + surfaces.Length + ".";
            keep = commit;
            return true;
        }
        finally
        {
            if (!keep)
            {
                surfaces = oldSurfaces; useScenePhysicsSurfaces = oldMode;
                referencePosition = oldReferencePosition; referenceRotation = oldReferenceRotation;
                transform.SetPositionAndRotation(oldPosition, oldRotation);
                for (int i = 0; i < 4; i++) joints[i].localRotation = oldJoints[i];
            }
            sceneAllowedSource = null;
            InvalidateRuntime();
            Physics.SyncTransforms();
        }
    }

    public bool EditorTrySurfaceAt(Vector3 worldXZ, out RaycastHit hit)
    {
        hit = default;
        if (surfaces == null) return false;
        float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
        foreach (Collider c in surfaces)
        {
            if (!c || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
            Bounds b = c.bounds;
            top = Mathf.Max(top, b.max.y); bottom = Mathf.Min(bottom, b.min.y);
        }
        if (!Finite(top) || !Finite(bottom)) return false;
        Ray ray = new Ray(new Vector3(worldXZ.x, top + 2f, worldXZ.z), Vector3.down);
        return TrySceneSurfaceRay(ray, top - bottom + 4f, out hit);
    }
#endif
}
