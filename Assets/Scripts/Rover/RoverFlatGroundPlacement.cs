using UnityEngine;

public class RoverFlatGroundPlacement : MonoBehaviour
{
    [SerializeField] private Collider testGround;
    [SerializeField] private Transform[] wheelCenters = new Transform[6];

    [SerializeField, Min(0.001f)] private float wheelRadius = 0.2627f;
    [SerializeField, Min(0.01f)] private float rayLength = 10f;

    // Tolérance du diagnostic, pas une caractéristique mécanique.
    [SerializeField, Min(0f)] private float maxGapSpread = 0.005f;

    [ContextMenu("Place On Flat Ground")]
    private void PlaceOnFlatGround()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Utiliser cette commande hors du mode Play.", this);
            return;
        }

        if (testGround == null ||
            wheelCenters == null ||
            wheelCenters.Length != 6)
        {
            Debug.LogError("Renseigner le sol et exactement six centres de roue.", this);
            return;
        }

        float sum = 0f;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        foreach (Transform wheel in wheelCenters)
        {
            if (wheel == null)
            {
                Debug.LogError("Un centre de roue n'est pas renseigné.", this);
                return;
            }

            Ray ray = new Ray(
                wheel.position + Vector3.up * 0.5f,
                Vector3.down
            );

            if (!testGround.Raycast(ray, out RaycastHit hit, rayLength))
            {
                Debug.LogError($"Sol introuvable sous {wheel.name}.", wheel);
                return;
            }

            float gap = wheel.position.y - hit.point.y - wheelRadius;
            sum += gap;
            min = Mathf.Min(min, gap);
            max = Mathf.Max(max, gap);
        }

        float spread = max - min;

        if (spread > maxGapSpread)
        {
            Debug.LogWarning(
                $"Placement annulé : dispersion des écarts = {spread:F4} m. " +
                $"Minimum = {min:F4} m ; maximum = {max:F4} m. " +
                "Vérifier la pose de référence avant de continuer.",
                this
            );
            return;
        }

        float correction = -sum / wheelCenters.Length;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(transform, "Place Rover On Flat Ground");
#endif

        transform.position += Vector3.up * correction;

        Debug.Log(
            $"Translation verticale = {correction:F4} m. " +
            $"Dispersion des écarts = {spread:F4} m. " +
            $"Écart résiduel maximal = " +
            $"{Mathf.Max(Mathf.Abs(min + correction), Mathf.Abs(max + correction)):F4} m.",
            this
        );
    }
}