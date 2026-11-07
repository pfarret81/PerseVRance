using UnityEngine;

public class IngenuityRotorController : MonoBehaviour
{
    [Header("Rotor References")]
    public Transform upperRotor;
    public Transform lowerRotor;

    [Header("Rotor Settings")]
    public float rotationSpeedRPM = 300f;

    void Update()
    {
        // Conversion RPM vers degrés par seconde
        float rotationSpeed = rotationSpeedRPM * 6f;

        upperRotor.Rotate(
            Vector3.right,
            rotationSpeed * Time.deltaTime,
            Space.Self
        );

        lowerRotor.Rotate(
            Vector3.left,
            -rotationSpeed * Time.deltaTime,
            Space.Self
        );
    }
}
