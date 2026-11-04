using UnityEngine;

public class AvatarTracker : MonoBehaviour
{
    [Header("VR Tracking Sources")]
    [SerializeField] private Transform headSource;
    [SerializeField] private Transform leftHandSource;
    [SerializeField] private Transform rightHandSource;

    [Header("Avatar Parts")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform body;

    private void LateUpdate()
    {
        // Synchronisation de la tête avec le casque VR
        if (headSource != null && head != null)
        {
            head.SetPositionAndRotation(
                headSource.position,
                headSource.rotation
            );
        }

        if (headSource != null && body != null)
        {
            Vector3 bodyPosition = headSource.position;
            bodyPosition.y -= 1.0f;

            // Récupère uniquement la rotation autour de Y
            Vector3 bodyRotation = new Vector3(
                0f,
                headSource.eulerAngles.y,
                0f
            );

            body.SetPositionAndRotation(
                bodyPosition,
                Quaternion.Euler(bodyRotation)
            );
        }

        // Synchronisation de la main gauche
        if (leftHandSource != null && leftHand != null)
        {
            leftHand.SetPositionAndRotation(
                leftHandSource.position,
                leftHandSource.rotation
            );
        }

        // Synchronisation de la main droite
        if (rightHandSource != null && rightHand != null)
        {
            rightHand.SetPositionAndRotation(
                rightHandSource.position,
                rightHandSource.rotation
            );
        }
    }
}
