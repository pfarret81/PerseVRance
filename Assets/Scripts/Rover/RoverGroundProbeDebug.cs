using System.Text;
using UnityEngine;

public class RoverGroundProbeDebug : MonoBehaviour
{
    [SerializeField] private RoverStaticPoseTest solver;
    [SerializeField, Min(0.001f)] private float markerRadius=0.04f;
    private RoverStaticPoseTest Source => solver ? solver : GetComponent<RoverStaticPoseTest>();
    private void OnDrawGizmosSelected()
    {
        RoverStaticPoseTest source=Source;
        if (!source) return;
        for (int i=0;i<source.WheelCount;i++)
        {
            Transform wheel=source.WheelCenter(i);
            if (!wheel) continue;
            Gizmos.color=Color.cyan;
            Gizmos.DrawWireSphere(wheel.position,markerRadius);
            if (source.TryProbeGround(wheel.position,out RaycastHit hit))
            {
                Gizmos.color=Color.green;
                Gizmos.DrawLine(wheel.position,hit.point);
                Gizmos.DrawSphere(hit.point,markerRadius);
                Gizmos.color=Color.yellow;
                Gizmos.DrawLine(hit.point,hit.point+hit.normal*0.2f);
            }
            else
            {
                Gizmos.color=Color.red;
                Gizmos.DrawLine(wheel.position,wheel.position-Vector3.up);
            }
        }
    }
    [ContextMenu("Report Ground Hits")]
    private void ReportGroundHits()
    {
        RoverStaticPoseTest source=Source;
        if (!source) { Debug.LogWarning("Assigner le solveur sur le meme rover.",this); return; }
        Physics.SyncTransforms();
        StringBuilder report=new StringBuilder("Appuis : source unique = solveur.\n");
        for (int i=0;i<source.WheelCount;i++)
        {
            Transform wheel=source.WheelCenter(i);
            if (!wheel) { report.AppendLine("Reference de roue manquante."); continue; }
            if (source.TryProbeGround(wheel.position,out RaycastHit hit))
                report.AppendLine($"{wheel.name} -> {hit.collider.name}; point={hit.point.ToString("F4")}; normale={hit.normal.ToString("F4")}");
            else report.AppendLine($"{wheel.name} -> aucune surface.");
        }
        Debug.Log(report.ToString(),this);
    }
}
