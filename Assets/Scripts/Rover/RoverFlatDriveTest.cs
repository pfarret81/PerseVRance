using UnityEngine;

public class RoverFlatDriveTest : MonoBehaviour
{
    [SerializeField] private Transform[] wheelCenters = new Transform[6];
    [SerializeField, Min(0.001f)] private float wheelRadius = 0.2627f;

    [Tooltip("Vitesse de test choisie pour le prototype, en m/s.")]
    [SerializeField] private float speed = 0.05f;

    [SerializeField] private bool drive;

    private void Start()
    {
        if (wheelCenters == null || wheelCenters.Length != 6)
        {
            StopWithError("Renseigner exactement six Empty de roue.");
            return;
        }

        for (int i = 0; i < wheelCenters.Length; i++)
        {
            if (wheelCenters[i] == null)
            {
                StopWithError("Un Empty de roue n'est pas renseigné.");
                return;
            }

            for (int j = 0; j < i; j++)
            {
                if (wheelCenters[i] == wheelCenters[j])
                {
                    StopWithError("Un Empty de roue est renseigné deux fois.");
                    return;
                }
            }
        }
    }

    private void Update()
    {
        if (!drive)
            return;

        Vector3 forward = transform.forward;
        float distance = speed * Time.deltaTime;

        transform.position += forward * distance;

        Vector3 rollingAxis = Vector3.Cross(Vector3.up, forward).normalized;
        float angle = distance / wheelRadius * Mathf.Rad2Deg;

        foreach (Transform wheel in wheelCenters)
        {
            Vector3 localXInWorld = wheel.TransformDirection(Vector3.right);
            float sign = Mathf.Sign(Vector3.Dot(localXInWorld, rollingAxis));

            wheel.localRotation *=
                Quaternion.AngleAxis(angle * sign, Vector3.right);
        }
    }

    private void StopWithError(string message)
    {
        Debug.LogError(message, this);
        enabled = false;
    }
}