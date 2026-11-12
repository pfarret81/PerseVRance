using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarsTerrainRegion : MonoBehaviour
{
    public int gridSize = 8;
    public int samplesPerTile = 513;
    public float tileWidth;
    public float tileLength;
    public float altitudeOffset = -2700f;
    public float heightRange = 300f;
    public double referenceLatitudeDegrees;
    public double projectionXScale;
    public double projectedSouthWestEasting;
    public double projectedSouthWestNorthing;
    public int sourceColumnOffset = 11500;
    public int sourceRowOffset = 11500;
    public Terrain[] tiles;

    public Vector2 Size => new Vector2(gridSize * tileWidth, gridSize * tileLength);

    public bool TryGetTerrain(Vector3 worldPosition, out Terrain terrain)
    {
        terrain = null;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 size = Size;
        if (tileWidth <= 0f || tileLength <= 0f || local.x < 0f || local.z < 0f ||
            local.x > size.x || local.z > size.y || tiles == null)
            return false;
        int col = Mathf.Min(Mathf.FloorToInt(local.x / tileWidth), gridSize - 1);
        int row = Mathf.Min(Mathf.FloorToInt(local.z / tileLength), gridSize - 1);
        int index = row * gridSize + col;
        if (index < 0 || index >= tiles.Length)
            return false;
        terrain = tiles[index];
        return terrain != null;
    }
}
