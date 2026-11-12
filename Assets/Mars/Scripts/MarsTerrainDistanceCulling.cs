using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MarsTerrainRegion))]
public sealed class MarsTerrainDistanceCulling : MonoBehaviour
{
    [Header("Reference de distance")]
    [SerializeField] private Transform observer;

    [Header("Distances horizontales en metres")]
    [Min(0f)]
    [SerializeField] private float showDistance = 1300f;

    [Min(1f)]
    [SerializeField] private float hideDistance = 1500f;

    [Min(0.02f)]
    [SerializeField] private float updateInterval = 0.2f;

    [Header("Diagnostic en execution")]
    [SerializeField] private int visibleTileCount;
    [SerializeField] private int totalTileCount;

    private sealed class Entry
    {
        public Terrain terrain;
        public bool originallyDrawn;
        public bool visible;
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
    }

    private Entry[] entries;
    private float nextUpdate;

    private void OnValidate()
    {
        showDistance = Mathf.Max(0f, showDistance);
        hideDistance = Mathf.Max(showDistance + 1f, hideDistance);
        updateInterval = Mathf.Max(0.02f, updateInterval);
    }

    private void OnEnable()
    {
        if (observer == null)
        {
            Debug.LogError(
                "Assigne Observer : Main Camera du XR Origin " +
                "ou un objet de test.", this);
            enabled = false;
            return;
        }

        MarsTerrainRegion region = GetComponent<MarsTerrainRegion>();

        if (region.tiles == null || region.tiles.Length == 0)
        {
            Debug.LogError("La region ne contient aucune tuile.", this);
            enabled = false;
            return;
        }

        entries = new Entry[region.tiles.Length];
        totalTileCount = 0;

        for (int i = 0; i < region.tiles.Length; i++)
        {
            Terrain tile = region.tiles[i];

            if (tile == null || tile.terrainData == null)
                continue;

            // Rectangles calcules pour des terrains statiques,
            // sans rotation et avec une echelle monde de 1.
            if (Quaternion.Angle(tile.transform.rotation,
                    Quaternion.identity) > 0.01f ||
                (tile.transform.lossyScale - Vector3.one).sqrMagnitude
                    > 0.000001f)
            {
                Debug.LogError(
                    "Les terrains doivent rester sans rotation " +
                    "et a l'echelle monde (1,1,1).", tile);
                enabled = false;
                return;
            }

            Vector3 p = tile.transform.position;
            Vector3 size = tile.terrainData.size;

            entries[i] = new Entry
            {
                terrain = tile,
                originallyDrawn = tile.drawHeightmap,
                visible = true,
                minX = p.x,
                maxX = p.x + size.x,
                minZ = p.z,
                maxZ = p.z + size.z
            };

            totalTileCount++;
        }

        Evaluate();
        nextUpdate = Time.unscaledTime + updateInterval;
    }

    private void LateUpdate()
    {
        if (observer == null || entries == null)
            return;

        if (Time.unscaledTime < nextUpdate)
            return;

        nextUpdate = Time.unscaledTime + updateInterval;
        Evaluate();
    }

    private void Evaluate()
    {
        Vector3 p = observer.position;
        float showSquared = showDistance * showDistance;
        float hideSquared = hideDistance * hideDistance;
        visibleTileCount = 0;

        foreach (Entry entry in entries)
        {
            if (entry == null || entry.terrain == null)
                continue;

            float closestX = Mathf.Clamp(p.x, entry.minX, entry.maxX);
            float closestZ = Mathf.Clamp(p.z, entry.minZ, entry.maxZ);

            float dx = p.x - closestX;
            float dz = p.z - closestZ;
            float distanceSquared = dx * dx + dz * dz;

            if (entry.visible && distanceSquared > hideSquared)
                entry.visible = false;
            else if (!entry.visible && distanceSquared < showSquared)
                entry.visible = true;

            bool draw = entry.originallyDrawn && entry.visible;

            if (entry.terrain.drawHeightmap != draw)
                entry.terrain.drawHeightmap = draw;

            if (draw)
                visibleTileCount++;
        }
    }

    private void OnDisable()
    {
        if (entries != null)
        {
            foreach (Entry entry in entries)
            {
                if (entry != null && entry.terrain != null)
                    entry.terrain.drawHeightmap = entry.originallyDrawn;
            }
        }

        entries = null;
        visibleTileCount = 0;
        totalTileCount = 0;
    }
}