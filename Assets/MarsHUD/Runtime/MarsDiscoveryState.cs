using UnityEngine;

namespace MarsHUD
{
    [DisallowMultipleComponent]
    public sealed class MarsDiscoveryState : MonoBehaviour
    {
        [Min(0f)] public float initialKnownRadiusMeters = 80f;
        public Vector2 InitialKnownCenter { get; private set; }
        public int Version { get; private set; }
        public void Initialize(Vector2 start)
        {
            InitialKnownCenter = start;
            Version++;
        }
        public bool IsKnown(MarsMapTheme theme, Vector2 p)
        {
            // Lot 1 : aucun prelevement et aucune acquisition de drone simules.
            if (theme == MarsMapTheme.Soil) return false;
            return (p - InitialKnownCenter).sqrMagnitude <= initialKnownRadiusMeters * initialKnownRadiusMeters;
        }
    }
}
