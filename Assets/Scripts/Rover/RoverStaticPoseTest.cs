using UnityEngine;
using Unity.Profiling;

public class RoverStaticPoseTest : MonoBehaviour
{
    [Header("Surfaces : sol et plateforme")]
    [SerializeField] private Collider[] surfaces;

    [Header("Ordre : rocker gauche, rocker droit, bogie gauche, bogie droit")]
    [SerializeField] private Transform[] joints = new Transform[4];

    [Header("Les six Empty E wheel, une fois chacun")]
    [SerializeField] private Transform[] wheels = new Transform[6];

    [SerializeField, Min(0.001f)] private float radius = 0.2627f;

    [Header("Test en mode Play — rover immobile")]
    [SerializeField] private bool solveContinuously;

    [Header("Avance courte — mode Play uniquement")]
    [SerializeField] private bool advanceTest;
    [SerializeField, Min(0.001f)] private float testSpeed = 0.05f;
    [SerializeField, Min(0.001f)] private float testDistance = 0.10f;

    private Vector3 runtimeAnchorOffset;
    private float travelledDistance;

    private readonly Vector3[] previousWheelPositions = new Vector3[6];
    private readonly Quaternion[] previousJointRotations = new Quaternion[4];

    private readonly float[] lastAcceptedParameters = new float[6];
    private bool hasRuntimeSolution;

    private int lastSolveIterations;
    private float lastSolveError;

    private readonly float[] parameterBuffer = new float[6];
    private readonly float[] errorBuffer = new float[6];
    private readonly float[] shiftedErrorBuffer = new float[6];
    private readonly float[] deltaBuffer = new float[6];

    private readonly float[,] jacobianBuffer = new float[6, 6];
    private readonly double[,] linearSystemBuffer = new double[6, 7];

    private static readonly ProfilerMarker UpdateMarker =
        new ProfilerMarker("Rover.KinematicUpdate");

    [SerializeField, HideInInspector]
    private bool hasReference;

    [SerializeField, HideInInspector]
    private Vector3 referencePosition;

    [SerializeField, HideInInspector]
    private Quaternion referenceRotation;

    [SerializeField, HideInInspector]
    private Quaternion[] referenceJoints;

    [SerializeField, HideInInspector]
    private float[] axisSigns;

    [ContextMenu("Capture Reference Pose")]
    private void CaptureReferencePose()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Capturer la référence hors du mode Play.", this);
            return;
        }

        if (!ReferencesValid())
            return;

        float[] capturedSigns = new float[4];
        Quaternion[] capturedRotations = new Quaternion[4];

        for (int i = 0; i < joints.Length; i++)
        {
            capturedRotations[i] = joints[i].localRotation;

            Vector3 axis = joints[i].TransformDirection(Vector3.forward);
            float alignment = Vector3.Dot(axis, transform.right);

            if (Mathf.Abs(alignment) < 0.5f)
            {
                Debug.LogError(
                    "Capture annulée : axe d'articulation inattendu.",
                    joints[i]
                );
                return;
            }

            capturedSigns[i] = Mathf.Sign(alignment);
        }

#if UNITY_EDITOR
    UnityEditor.Undo.RecordObject(this, "Capture Rover Reference Pose");
#endif

        referencePosition = transform.position;
        referenceRotation = transform.rotation;
        referenceJoints = capturedRotations;
        axisSigns = capturedSigns;
        hasReference = true;

        Debug.Log(
            "Référence capturée. Elle restera fixe jusqu'à une nouvelle capture.",
            this
        );
    }

    [ContextMenu("Report Last Solve")]
    private void ReportLastSolve()
    {
        Debug.Log(
            $"Dernière résolution : {lastSolveIterations} passage(s) ; " +
            $"erreur maximale = {lastSolveError * 1000f:F3} mm ; " +
            $"cache actif = {hasRuntimeSolution}.",
            this
        );
    }

    private bool ReferenceAvailable()
    {
        if (!hasReference ||
            referenceJoints == null || referenceJoints.Length != 4 ||
            axisSigns == null || axisSigns.Length != 4)
        {
            Debug.LogWarning(
                "Capturer d'abord la pose avec Capture Reference Pose.",
                this
            );
            return false;
        }

        return true;
    }

    [ContextMenu("Restore Reference Pose")]
    private void RestoreReferencePose()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Restaurer la référence hors du mode Play.", this);
            return;
        }

        if (!ReferencesValid() || !ReferenceAvailable())
            return;

#if UNITY_EDITOR
    UnityEditor.Undo.RecordObjects(
        new Object[]
        {
            transform, joints[0], joints[1], joints[2], joints[3]
        },
        "Restore Rover Reference Pose"
    );
#endif

        ApplyPose(new float[6]);

        Debug.Log("Pose de référence restaurée.", this);
    }

    [ContextMenu("Solve Static Test Pose")]
    private void SolveStaticTestPose()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "En mode Play, utiliser Solve Continuously.",
                this
            );
            return;
        }

        TrySolvePose(true);
    }

    private bool TrySolvePose(bool reportSuccess)
    {
        if (!ReferencesValid() || !ReferenceAvailable())
            return false;

#if UNITY_EDITOR
    if (!Application.isPlaying)
    {
        UnityEditor.Undo.RecordObjects(
            new Object[]
            {
                transform, joints[0], joints[1], joints[2], joints[3]
            },
            "Solve Rover Static Pose"
        );
    }
#endif

        // Paramètres :
        // hauteur en mètres ; autres valeurs en radians.
        // [hauteur, tangage, roulis, rocker couplé, bogie G, bogie D]
        float[] parameters = parameterBuffer;
        float[] errors = errorBuffer;

        System.Array.Clear(parameters, 0, parameters.Length);
        System.Array.Clear(errors, 0, errors.Length);

        if (Application.isPlaying && hasRuntimeSolution)
        {
            System.Array.Copy(
                lastAcceptedParameters,
                parameters,
                parameters.Length
            );
        }

        bool success = false;
        int iterations = 0;

        for (int iteration = 0; iteration < 60; iteration++)
        {
            iterations = iteration + 1;
            ApplyPose(parameters);

            if (!ReadErrors(errors))
                break;

            if (MaxAbs(errors) <= 0.001f)
            {
                success = true;
                break;
            }

            float[,] jacobian = jacobianBuffer;
            const float epsilon = 0.0005f;

            bool derivativeValid = true;

            for (int column = 0; column < 6; column++)
            {
                parameters[column] += epsilon;
                ApplyPose(parameters);

                float[] shiftedErrors = shiftedErrorBuffer;
                bool valid = ReadErrors(shiftedErrors);

                parameters[column] -= epsilon;

                if (!valid)
                {
                    derivativeValid = false;
                    break;
                }

                for (int row = 0; row < 6; row++)
                {
                    jacobian[row, column] =
                        (shiftedErrors[row] - errors[row]) / epsilon;
                }
            }

            ApplyPose(parameters);

            if (!derivativeValid)
                break;

            // Moindres carrés régularisés : (JᵀJ + λI) delta = -Jᵀe.
            double[,] system = linearSystemBuffer;
            System.Array.Clear(system, 0, system.Length);

            for (int row = 0; row < 6; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    for (int sample = 0; sample < 6; sample++)
                    {
                        system[row, column] +=
                            jacobian[sample, row] * jacobian[sample, column];
                    }
                }

                system[row, row] += 0.00001;

                for (int sample = 0; sample < 6; sample++)
                    system[row, 6] -= jacobian[sample, row] * errors[sample];
            }

            float[] delta = deltaBuffer;

            if (!SolveLinearSystem(system, delta))
                break;

            // Limitation du pas numérique, pas des limites mécaniques.
            float stepScale = 1f;

            for (int i = 0; i < 6; i++)
            {
                float maxStep = i == 0 ? 0.02f : 0.03f;

                if (Mathf.Abs(delta[i]) > maxStep)
                    stepScale = Mathf.Min(
                        stepScale, maxStep / Mathf.Abs(delta[i])
                    );
            }

            for (int i = 0; i < 6; i++)
                parameters[i] += delta[i] * stepScale;
        }

        ApplyPose(parameters);

        // Vérification finale, y compris après le dernier pas.
        success = ReadErrors(errors) && MaxAbs(errors) <= 0.001f;

        // Domaine de ce petit test, pas limites réelles du rover.
        bool smallPose = Mathf.Abs(parameters[0]) <= 0.15f;

        for (int i = 1; i < 6; i++)
            smallPose &= Mathf.Abs(parameters[i]) <= 15f * Mathf.Deg2Rad;

        if (!success || !smallPose)
        {
            hasRuntimeSolution = false;
            lastSolveIterations = iterations;
            lastSolveError = MaxAbs(errors);

            ApplyPose(new float[6]);

            Debug.LogWarning(
                "Résolution refusée : convergence insuffisante, " +
                "surface manquante ou sortie du domaine du petit test. " +
                "Pose de référence restaurée.",
                this
            );
            return false;
        }

        lastSolveIterations = iterations;
        lastSolveError = MaxAbs(errors);

        if (Application.isPlaying)
        {
            System.Array.Copy(
                parameters,
                lastAcceptedParameters,
                parameters.Length
            );

            hasRuntimeSolution = true;
        }

        if (reportSuccess)
        {
            Debug.Log(
                $"Pose statique résolue en {iterations} itérations. " +
                $"Erreur verticale maximale = {MaxAbs(errors):F5} m.\n" +
                $"Hauteur : {parameters[0]:F4} m ; " +
                $"tangage : {parameters[1] * Mathf.Rad2Deg:F3}° ; " +
                $"roulis : {parameters[2] * Mathf.Rad2Deg:F3}° ; " +
                $"rocker couplé : {parameters[3] * Mathf.Rad2Deg:F3}° ; " +
                $"bogie G : {parameters[4] * Mathf.Rad2Deg:F3}° ; " +
                $"bogie D : {parameters[5] * Mathf.Rad2Deg:F3}°.",
                this
            );
        }
        return true;
    }

    private void ApplyPose(float[] p)
    {
        Vector3 anchorOffset = Application.isPlaying
            ? runtimeAnchorOffset
            : Vector3.zero;

        transform.position =
            referencePosition + anchorOffset + Vector3.up * p[0];

        transform.rotation = referenceRotation
            * Quaternion.AngleAxis(p[1] * Mathf.Rad2Deg, Vector3.right)
            * Quaternion.AngleAxis(p[2] * Mathf.Rad2Deg, Vector3.forward);

        SetJoint(0, p[3]);
        SetJoint(1, -p[3]);
        SetJoint(2, p[4]);
        SetJoint(3, p[5]);
    }

    private void SetJoint(int index, float angleRadians)
    {
        joints[index].localRotation = referenceJoints[index]
            * Quaternion.AngleAxis(
                angleRadians * Mathf.Rad2Deg * axisSigns[index],
                Vector3.forward
            );
    }

    private bool ReadErrors(float[] errors)
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            Ray ray = new Ray(
                wheels[i].position + Vector3.up,
                Vector3.down
            );

            bool found = false;
            float nearest = 10f;
            float groundY = 0f;

            foreach (Collider surface in surfaces)
            {
                if (surface == null ||
                    !surface.enabled ||
                    !surface.gameObject.activeInHierarchy ||
                    surface.isTrigger)
                {
                    continue;
                }

                if (surface.Raycast(ray, out RaycastHit hit, nearest))
                {
                    nearest = hit.distance;
                    groundY = hit.point.y;
                    found = true;
                }
            }

            if (!found)
                return false;

            errors[i] = wheels[i].position.y - groundY - radius;
        }

        return true;
    }

    private bool ReferencesValid()
    {
        if (surfaces == null || surfaces.Length == 0 ||
            joints == null || joints.Length != 4 ||
            wheels == null || wheels.Length != 6)
        {
            Debug.LogError("Vérifier les tailles des trois tableaux.", this);
            return false;
        }

        return TransformGroupValid(joints) && TransformGroupValid(wheels);
    }

    private bool TransformGroupValid(Transform[] group)
    {
        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] == null ||
                group[i] == transform ||
                !group[i].IsChildOf(transform))
            {
                Debug.LogError(
                    "Référence absente, égale à RoverRoot ou extérieure à RoverRoot.",
                    this
                );
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if (group[i] == group[j])
                {
                    Debug.LogError("Référence renseignée deux fois.", this);
                    return false;
                }
            }
        }

        return true;
    }

    private static float MaxAbs(float[] values)
    {
        float maximum = 0f;

        foreach (float value in values)
            maximum = Mathf.Max(maximum, Mathf.Abs(value));

        return maximum;
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

    private void RotateWheelsFromDisplacement(Vector3 heading)
    {
        Vector3 positiveRollingAxis =
            Vector3.Cross(Vector3.up, heading).normalized;

        for (int i = 0; i < 6; i++)
        {
            Vector3 displacement =
                wheels[i].position - previousWheelPositions[i];

            float longitudinalDistance =
                Vector3.Dot(displacement, heading);

            Vector3 axle =
                wheels[i].TransformDirection(Vector3.right).normalized;

            float sign = Mathf.Sign(
                Vector3.Dot(axle, positiveRollingAxis)
            );

            float angle =
                longitudinalDistance / radius * Mathf.Rad2Deg * sign;

            wheels[i].localRotation *= Quaternion.AngleAxis(
                angle,
                Vector3.right
            );
        }
    }

    private void Start()
    {
        hasRuntimeSolution = false;
        runtimeAnchorOffset = Vector3.zero;
        travelledDistance = 0f;

        lastSolveIterations = 0;
        lastSolveError = float.PositiveInfinity;
    }

    private void UpdateRover()
    {
        if (!solveContinuously)
            return;

        if (!ReferencesValid() || !ReferenceAvailable())
        {
            solveContinuously = false;
            advanceTest = false;
            return;
        }

        Vector3 heading = Vector3.ProjectOnPlane(
            referenceRotation * Vector3.forward,
            Vector3.up
        ).normalized;

        if (heading.sqrMagnitude < 0.5f)
        {
            solveContinuously = false;
            advanceTest = false;

            Debug.LogError("Direction horizontale de référence invalide.", this);
            return;
        }

        float distance = 0f;

        if (advanceTest)
        {
            float remaining = Mathf.Max(
                0f, testDistance - travelledDistance
            );

            distance = Mathf.Min(
                testSpeed * Time.deltaTime,
                remaining
            );
        }

        Vector3 previousAnchorOffset = runtimeAnchorOffset;
        Vector3 previousRootPosition = transform.position;
        Quaternion previousRootRotation = transform.rotation;

        for (int i = 0; i < 4; i++)
            previousJointRotations[i] = joints[i].localRotation;

        for (int i = 0; i < 6; i++)
            previousWheelPositions[i] = wheels[i].position;

        runtimeAnchorOffset += heading * distance;

        if (!TrySolvePose(false))
        {
            // Refuser ce déplacement et conserver la dernière pose affichée.
            runtimeAnchorOffset = previousAnchorOffset;

            transform.SetPositionAndRotation(
                previousRootPosition,
                previousRootRotation
            );

            for (int i = 0; i < 4; i++)
                joints[i].localRotation = previousJointRotations[i];

            solveContinuously = false;
            advanceTest = false;

            Debug.LogWarning(
                "Déplacement refusé après un échec de résolution. " +
                "Dernière pose conservée.",
                this
            );
            return;
        }

        if (distance > 0f)
        {
            RotateWheelsFromDisplacement(heading);
            travelledDistance += distance;
        }

        if (advanceTest &&
            travelledDistance >= testDistance - 0.000001f)
        {
            advanceTest = false;

            Debug.Log(
                $"Avance courte terminée : {travelledDistance:F3} m.",
                this
            );
        }
    }

    private void Update()
    {
        if (!solveContinuously)
            return;

        using (UpdateMarker.Auto())
        {
            UpdateRover();
        }
    }
}