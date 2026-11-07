using UnityEngine;

public class RoverGroundProbeDebug : MonoBehaviour
{
    [Header("Surfaces autorisées")]
    [SerializeField] private Collider[] groundSurfaces;

    [Header("Centres des roues : Empty E wheel")]
    [SerializeField] private Transform frontLeft;
    [SerializeField] private Transform middleLeft;
    [SerializeField] private Transform rearLeft;
    [SerializeField] private Transform frontRight;
    [SerializeField] private Transform middleRight;
    [SerializeField] private Transform rearRight;

    [Header("Diagnostic uniquement — unités monde")]
    [SerializeField, Min(0f)] private float startHeight = 0.5f;
    [SerializeField, Min(0.01f)] private float rayLength = 10f;
    [SerializeField, Min(0.001f)] private float markerRadius = 0.04f;

    private void OnDrawGizmosSelected()
    {
        DrawProbe(frontLeft);
        DrawProbe(middleLeft);
        DrawProbe(rearLeft);
        DrawProbe(frontRight);
        DrawProbe(middleRight);
        DrawProbe(rearRight);
    }

    private bool TryFindGround(Transform wheel, out RaycastHit closestHit)
    {
        closestHit = default;

        if (wheel == null || groundSurfaces == null)
            return false;

        Vector3 origin = wheel.position + Vector3.up * startHeight;
        Ray ray = new Ray(origin, Vector3.down);

        bool found = false;
        float closestDistance = rayLength;

        foreach (Collider surface in groundSurfaces)
        {
            if (surface == null ||
                !surface.enabled ||
                !surface.gameObject.activeInHierarchy ||
                surface.isTrigger)
            {
                continue;
            }

            if (surface.Raycast(ray, out RaycastHit hit, closestDistance))
            {
                closestHit = hit;
                closestDistance = hit.distance;
                found = true;
            }
        }

        return found;
    }

    private void DrawProbe(Transform wheel)
    {
        if (wheel == null)
            return;

        Vector3 origin = wheel.position + Vector3.up * startHeight;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(wheel.position, markerRadius);

        if (TryFindGround(wheel, out RaycastHit hit))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, hit.point);
            Gizmos.DrawSphere(hit.point, markerRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(hit.point, hit.point + hit.normal * 0.2f);
        }
        else
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + Vector3.down * rayLength);
        }
    }

    [ContextMenu("Report Ground Hits")]
    private void ReportGroundHits()
    {
        ReportHit(frontLeft);
        ReportHit(middleLeft);
        ReportHit(rearLeft);
        ReportHit(frontRight);
        ReportHit(middleRight);
        ReportHit(rearRight);
    }

    private void ReportHit(Transform wheel)
    {
        if (wheel == null)
        {
            Debug.LogWarning("Une référence de roue manque.", this);
            return;
        }

        if (TryFindGround(wheel, out RaycastHit hit))
        {
            Debug.Log(
                $"{wheel.name} → {hit.collider.name} ; " +
                $"Y sol = {hit.point.y:F4} m",
                this
            );
        }
        else
        {
            Debug.LogWarning($"{wheel.name} → aucune surface détectée.", this);
        }
    }
}