using System.IO;
using UnityEditor;
using UnityEngine;

public static class JezeroTerrainCheck
{
    [MenuItem("Mars/Verifier le terrain selectionne")]
    private static void Check()
    {
        Terrain terrain = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Terrain>()
            : null;

        if (terrain == null)
        {
            Debug.LogError("Selectionne le GameObject du Terrain.");
            return;
        }

        TerrainData data = terrain.terrainData;
        int n = data.heightmapResolution;

        if (n != 1025)
        {
            Debug.LogError($"Resolution inattendue : {n}, attendu : 1025.");
            return;
        }

        string path = EditorUtility.OpenFilePanel(
            "Selectionner Jezero_test_1025.raw", "", "raw");

        if (string.IsNullOrEmpty(path))
            return;

        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length != n * n * 2)
        {
            Debug.LogError($"Taille RAW incorrecte : {bytes.Length} octets.");
            return;
        }

        float[,] heights = data.GetHeights(0, 0, n, n);
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        float errorFlipped = 0f;
        float errorUnflipped = 0f;

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float actual = heights[z, x] * data.size.y;

                min = Mathf.Min(min, actual);
                max = Mathf.Max(max, actual);

                int flippedIndex = ((n - 1 - z) * n + x) * 2;
                int directIndex = (z * n + x) * 2;

                float expectedFlipped =
                    ReadUInt16LE(bytes, flippedIndex) / 65535f * 50f;

                float expectedDirect =
                    ReadUInt16LE(bytes, directIndex) / 65535f * 50f;

                errorFlipped = Mathf.Max(
                    errorFlipped, Mathf.Abs(actual - expectedFlipped));

                errorUnflipped = Mathf.Max(
                    errorUnflipped, Mathf.Abs(actual - expectedDirect));
            }
        }

        Debug.Log(
            $"Verification Jezero\n" +
            $"Resolution : {n}\n" +
            $"Dimensions : {data.size.ToString("F3")}\n" +
            $"Position : {terrain.transform.position.ToString("F3")}\n" +
            $"Echelle Transform : {terrain.transform.localScale}\n" +
            $"Hauteur locale min/max : {min:F5} / {max:F5} m\n" +
            $"Altitude source reconstruite : " +
            $"{min - 2450f:F5} / {max - 2450f:F5} m\n" +
            $"Erreur maximale avec flip : {errorFlipped:F6} m\n" +
            $"Erreur maximale sans flip : {errorUnflipped:F6} m",
            terrain);
    }

    private static ushort ReadUInt16LE(byte[] bytes, int index)
    {
        return (ushort)(bytes[index] | (bytes[index + 1] << 8));
    }
}