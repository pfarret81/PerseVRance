using System.Text;
using UnityEngine;

public class RoverGeometryReport : MonoBehaviour
{
    [Header("Articulations")]
    [SerializeField] private Transform rockerLeft;
    [SerializeField] private Transform bogieLeft;
    [SerializeField] private Transform rockerRight;
    [SerializeField] private Transform bogieRight;

    [Header("Centres des roues")]
    [SerializeField] private Transform frontLeft;
    [SerializeField] private Transform middleLeft;
    [SerializeField] private Transform rearLeft;
    [SerializeField] private Transform frontRight;
    [SerializeField] private Transform middleRight;
    [SerializeField] private Transform rearRight;

    [ContextMenu("Report Reference Geometry")]
    private void ReportReferenceGeometry()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Effectuer le relevé hors du mode Play.", this);
            return;
        }

        Transform[] points =
        {
            rockerLeft, bogieLeft, rockerRight, bogieRight,
            frontLeft, middleLeft, rearLeft,
            frontRight, middleRight, rearRight
        };

        foreach (Transform point in points)
        {
            if (point == null)
            {
                Debug.LogError("Renseigner les dix références.", this);
                return;
            }
        }

        StringBuilder report = new StringBuilder();
        report.AppendLine("Géométrie dans le repère RoverRoot");
        report.AppendLine("Positions : X droite, Y haut, Z avant.");

        foreach (Transform point in points)
        {
            Vector3 position = transform.InverseTransformPoint(point.position);

            report.AppendLine(
                $"{point.name} : position = {position.ToString("F5")}"
            );
        }

        report.AppendLine("Axes Z des articulations dans RoverRoot :");

        for (int i = 0; i < 4; i++)
        {
            Vector3 axis = transform.InverseTransformDirection(
                points[i].TransformDirection(Vector3.forward)
            ).normalized;

            report.AppendLine(
                $"{points[i].name} : axe = {axis.ToString("F5")}"
            );
        }

        Debug.Log(report.ToString(), this);
    }
}