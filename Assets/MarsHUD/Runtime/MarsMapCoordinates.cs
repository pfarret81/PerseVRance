using UnityEngine;

namespace MarsHUD
{
    [DisallowMultipleComponent]
    public sealed class MarsMapCoordinates : MonoBehaviour
    {
        [Tooltip("Origine des donnees dans le monde. Sans reference, le demarrage du casque est utilise.")]
        public Transform originReference;
        [Tooltip("Rotation du nord cartographique autour de Y monde ; 0 = +Z.")]
        public float northYawDegrees;
        [Min(0.0001f)] public float worldUnitsPerMeter = 1f;
        public Vector3 WorldOrigin { get; private set; }
        public bool IsInitialized { get; private set; }
        Quaternion rotation;

        public void Initialize(Vector3 fallbackOrigin)
        {
            WorldOrigin = originReference ? originReference.position : fallbackOrigin;
            rotation = Quaternion.Euler(0f, northYawDegrees, 0f);
            worldUnitsPerMeter = Mathf.Max(0.0001f, worldUnitsPerMeter);
            IsInitialized = true;
        }
        public Vector2 WorldToData(Vector3 world)
        {
            Vector3 p = Quaternion.Inverse(rotation) * (world - WorldOrigin) / worldUnitsPerMeter;
            return new Vector2(p.x, p.z);
        }
        public Vector3 DataToWorld(Vector2 eastNorth, float heightAboveOriginMeters = 0f)
        {
            return WorldOrigin + rotation * new Vector3(eastNorth.x, heightAboveOriginMeters, eastNorth.y) * worldUnitsPerMeter;
        }
        public Vector2 DirectionToData(Vector3 direction)
        {
            Vector3 d = Quaternion.Inverse(rotation) * direction;
            return new Vector2(d.x, d.z).normalized;
        }
        [ContextMenu("Check coordinate round trip")]
        void CheckRoundTrip()
        {
            if (!IsInitialized) { Debug.LogWarning("Executer la scene avant ce controle.", this); return; }
            Vector2 probe = new Vector2(12.34f, -56.78f);
            float error = Vector2.Distance(probe, WorldToData(DataToWorld(probe)));
            Debug.Log($"MarsHUD round trip: {error:G6} m. Verifie la conversion uniquement, pas le recalage du terrain.", this);
        }
    }
}
