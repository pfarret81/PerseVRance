using System.Text;
using UnityEngine;

// Synthetic dt replay: does NOT change headset refresh rate or render frequency.
public class RoverTimestepCheck : MonoBehaviour
{
    [SerializeField] private RoverStraightPathDriver driver;
    [ContextMenu("Run 30-72-120 Hz Synthetic Check")]
    private void RunCheck()
    {
        if (!Application.isPlaying || !driver)
        { Debug.LogWarning("En Play, assigner le driver.", this); return; }
        int[] rates = { 30, 72, 120 };
        Quaternion[] baselineJoints = new Quaternion[4];
        Quaternion[] baselineWheels = new Quaternion[6];
        Vector3 baselinePosition = Vector3.zero;
        Quaternion baselineRotation = Quaternion.identity;
        StringBuilder report = new StringBuilder("Comparaison de pas synthetiques, pas un test de rendu XR.\n");
        for (int trial = 0; trial < rates.Length; trial++)
        {
            if (!driver.RestartPathFromReference())
            { Debug.LogWarning("Comparaison interrompue : redemarrage impossible.", this); return; }
            driver.Resume();
            float dt = 1f / rates[trial];
            int frames = 0;
            while (driver.State == RoverStraightPathDriver.MotionState.Moving && frames < 100000)
            { driver.SimulateStep(dt); frames++; }
            if (driver.State != RoverStraightPathDriver.MotionState.Completed)
            { driver.Pause(); Debug.LogWarning("Comparaison interrompue : faute ou limite de pas atteinte.", this); return; }
            RoverStaticPoseTest solver = driver.Solver;
            float positionDifference = 0f, rotationDifference = 0f;
            float jointDifference = 0f, wheelDifference = 0f;
            if (trial == 0)
            {
                baselinePosition = solver.transform.position;
                baselineRotation = solver.transform.rotation;
                for (int i = 0; i < 4; i++) baselineJoints[i] = solver.JointRotation(i);
                for (int i = 0; i < 6; i++) baselineWheels[i] = solver.WheelRotation(i);
            }
            else
            {
                positionDifference = Vector3.Distance(baselinePosition, solver.transform.position);
                rotationDifference = Quaternion.Angle(baselineRotation, solver.transform.rotation);
                for (int i = 0; i < 4; i++)
                    jointDifference = Mathf.Max(jointDifference, Quaternion.Angle(baselineJoints[i], solver.JointRotation(i)));
                for (int i = 0; i < 6; i++)
                    wheelDifference = Mathf.Max(wheelDifference, Quaternion.Angle(baselineWheels[i], solver.WheelRotation(i)));
            }
            report.AppendLine($"dt=1/{rates[trial]}, pas={frames}, duree simulee={frames * dt:F3} s, distance={driver.DistanceTravelled:F4} m, " +
                $"temps accepte={driver.AcceptedMotionTime:F3} s, erreur pic={driver.PeakAcceptedError * 1000f:F3} mm, " +
                $"ecart final vs 30 Hz : position={positionDifference * 1000f:F3} mm, " +
                $"corps={rotationDifference:F4} deg, articulations={jointDifference:F4} deg, roues={wheelDifference:F4} deg.");
        }
        driver.Pause();
        Debug.Log(report.ToString(), this);
    }
}
