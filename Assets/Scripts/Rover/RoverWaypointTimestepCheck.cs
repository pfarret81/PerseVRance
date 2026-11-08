using System.Text;
using UnityEngine;

public class RoverWaypointTimestepCheck : MonoBehaviour
{
    [SerializeField] private RoverWaypointFollower follower;
    [SerializeField, Min(10f)] private float maxSimulatedSeconds=300f;
    [ContextMenu("Run 30-72-120 Hz Synthetic Check")]
    private void RunCheck()
    {
        if (!Application.isPlaying || !follower)
        { Debug.LogWarning("En Play, assigner le follower.",this); return; }
        int[] rates={30,72,120};
        Vector3 baselinePosition=Vector3.zero;
        Quaternion baselineRotation=Quaternion.identity;
        StringBuilder report=new StringBuilder("Suivi de parcours : pas synthetiques, pas un test XR de rendu.\n");
        for (int trial=0;trial<rates.Length;trial++)
        {
            if (!follower.RestartFromReference()) { Debug.LogWarning("Rejeu impossible.",this); return; }
            follower.Resume();
            float dt=1f/rates[trial]; int steps=0;
            int limit=Mathf.CeilToInt(maxSimulatedSeconds*rates[trial]);
            while (follower.State==RoverWaypointFollower.FollowState.Following && steps<limit)
            { follower.SimulateStep(dt); steps++; }
            if (follower.State!=RoverWaypointFollower.FollowState.Arrived)
            { follower.Pause(); Debug.LogWarning("Comparaison interrompue : faute ou temps maximal depasse.",this); return; }
            Transform root=follower.transform;
            if (trial==0) { baselinePosition=root.position; baselineRotation=root.rotation; }
            report.AppendLine($"dt=1/{rates[trial]}, duree={steps*dt:F3} s, progression={follower.Progress:F3}/{follower.PathLength:F3} m, " +
                $"distance au but={follower.EndDistance:F3} m, ecart lateral pic={follower.PeakCrossTrack:F3} m, " +
                $"erreur appuis pic={follower.Actuator.PeakAcceptedError*1000f:F3} mm, " +
                $"ecart final vs 30 Hz={Vector3.Distance(root.position,baselinePosition)*1000f:F3} mm / " +
                $"{Quaternion.Angle(root.rotation,baselineRotation):F4} deg.");
        }
        follower.Pause(); Debug.Log(report.ToString(),this);
    }
}
