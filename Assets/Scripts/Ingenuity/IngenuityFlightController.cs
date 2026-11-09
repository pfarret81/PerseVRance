
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class IngenuityFlightController : MonoBehaviour
{
    [Header("Mars Physics")]
    public float marsGravity = 3.71f;

    [Header("Vertical Flight")]
    public float climbSpeed = 1.5f;
    public float verticalKp = 8f;
    public float verticalKd = 4f;

    [Header("Horizontal Flight")]
    public float tiltAngle = 7f;
    public float rotationSpeed = 35f;
    public float inputSmoothing = 2f;
    public float horizontalDamping = 0.8f;

    public Transform modelReference;
    public float horizontalAcceleration = 2f;

    private Rigidbody rb;

    private float targetAltitude;
    private Quaternion initialRotation;

    private float pitchInput;
    private float rollInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
    }

    void Start()
    {
        targetAltitude = rb.position.y;
        initialRotation = rb.rotation;
    }

    void FixedUpdate()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        // -----------------------------
        // 1. COMMANDES
        // -----------------------------

        float targetPitchInput = 0f;
        float targetRollInput = 0f;

        // Flèches haut / bas : avant et arrière
        if (keyboard.upArrowKey.isPressed)
            targetPitchInput = 1f;
        else if (keyboard.downArrowKey.isPressed)
            targetPitchInput = -1f;

        // Flèches gauche / droite : déplacements latéraux
        if (keyboard.leftArrowKey.isPressed)
            targetRollInput = -1f;
        else if (keyboard.rightArrowKey.isPressed)
            targetRollInput = 1f;

        // Progressivité des commandes
        pitchInput = Mathf.MoveTowards(
            pitchInput,
            targetPitchInput,
            inputSmoothing * Time.fixedDeltaTime
        );

        rollInput = Mathf.MoveTowards(
            rollInput,
            targetRollInput,
            inputSmoothing * Time.fixedDeltaTime
        );

        // -----------------------------
        // 2. COMMANDES VERTICALES
        // -----------------------------

        if (keyboard.spaceKey.isPressed)
        {
            targetAltitude += climbSpeed * Time.fixedDeltaTime;
        }

        if (keyboard.leftCtrlKey.isPressed)
        {
            targetAltitude -= climbSpeed * Time.fixedDeltaTime;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            modelReference.forward,
            Vector3.up
        ).normalized;

        Vector3 right = Vector3.Cross(
            Vector3.up,
            forward
        ).normalized;

        Vector3 movement =
            forward * targetPitchInput +
            right * targetRollInput;

        rb.AddForce(
            movement * horizontalAcceleration,
            ForceMode.Acceleration
        );

        // -----------------------------
        // 3. INCLINAISON DU DRONE
        // -----------------------------

        float targetPitch = pitchInput * tiltAngle;
        float targetRoll = -rollInput * tiltAngle;

        Quaternion desiredRotation =
            initialRotation *
            Quaternion.Euler(targetPitch, 0f, targetRoll);

        rb.MoveRotation(
            Quaternion.RotateTowards(
                rb.rotation,
                desiredRotation,
                rotationSpeed * Time.fixedDeltaTime
            )
        );

        // -----------------------------
        // 4. CONTROLE D'ALTITUDE
        // -----------------------------

        float altitudeError =
            targetAltitude - rb.position.y;

        float verticalVelocity = rb.linearVelocity.y;

        float accelerationCommand =
            verticalKp * altitudeError
            - verticalKd * verticalVelocity;

        // -----------------------------
        // 5. GRAVITE MARTIENNE
        // -----------------------------

        rb.AddForce(
            Vector3.down * rb.mass * marsGravity,
            ForceMode.Force
        );

        // -----------------------------
        // 6. POUSSEE DES ROTORS
        // -----------------------------

        float verticalProjection =
            Vector3.Dot(rb.transform.up, Vector3.up);

        verticalProjection = Mathf.Max(
            verticalProjection,
            0.25f
        );

        float thrustAcceleration =
            (marsGravity + accelerationCommand)
            / verticalProjection;

        thrustAcceleration = Mathf.Max(
            0f,
            thrustAcceleration
        );

        float thrust = rb.mass * thrustAcceleration;

        rb.AddForce(
            rb.transform.up * thrust,
            ForceMode.Force
        );

        // -----------------------------
        // 7. FREINAGE HORIZONTAL
        // -----------------------------

        Vector3 horizontalVelocity =
            Vector3.ProjectOnPlane(
                rb.linearVelocity,
                Vector3.up
            );

        rb.AddForce(
            -horizontalVelocity * horizontalDamping,
            ForceMode.Acceleration
        );
    }
}
