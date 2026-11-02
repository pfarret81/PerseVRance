using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JezeroTilesBuilder
{
    private const int N = 1025;
    private const float Width = 971f;
    private const float Length = 1024f;
    private const float Height = 100f;

    [MenuItem("Mars/Creer les 4 tuiles Jezero")]
    private static void Build()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Arrete le mode Play.");
            return;
        }

        Terrain reference = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Terrain>()
            : null;

        if (reference == null || reference.terrainData == null)
        {
            Debug.LogError("Selectionne le Terrain prototype.");
            return;
        }

        TerrainLayer[] layers = reference.terrainData.terrainLayers;

        if (layers.Length == 0 || layers[0] == null)
        {
            Debug.LogError("Le prototype doit avoir une Terrain Layer.");
            return;
        }

        if (GameObject.Find("Jezero_4Tiles") != null)
        {
            Debug.LogError("Un groupe Jezero_4Tiles existe deja.");
            return;
        }

        const string assetFolder = "Assets/Jezero_4Tiles_Data";

        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            Debug.LogError(
                "Assets/Jezero_4Tiles_Data existe deja. " +
                "Aucun asset ne sera ecrase.");
            return;
        }

        string folder = EditorUtility.OpenFolderPanel(
            "Dossier contenant les quatre RAW", "", "");

        if (string.IsNullOrEmpty(folder))
            return;

        // Ordre : SO, SE, NO, NE.
        string[] names = { "SO", "SE", "NO", "NE" };
        float[][,] maps = new float[4][,];

        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(
                folder, $"Jezero_{names[i]}.raw");

            if (!File.Exists(path))
            {
                Debug.LogError($"Fichier introuvable : {path}");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length != N * N * 2)
            {
                Debug.LogError($"Taille incorrecte : {path}");
                return;
            }

            float[,] heights = new float[N, N];

            for (int z = 0; z < N; z++)
            {
                int sourceRow = N - 1 - z;

                for (int x = 0; x < N; x++)
                {
                    int index = (sourceRow * N + x) * 2;
                    int value = bytes[index] | (bytes[index + 1] << 8);
                    heights[z, x] = value / 65535f;
                }
            }

            maps[i] = heights;
        }

        float borderError = 0f;

        for (int k = 0; k < N; k++)
        {
            // Raccords est-ouest.
            borderError = Mathf.Max(borderError,
                Mathf.Abs(maps[0][k, N - 1] - maps[1][k, 0]));
            borderError = Mathf.Max(borderError,
                Mathf.Abs(maps[2][k, N - 1] - maps[3][k, 0]));

            // Raccords sud-nord.
            borderError = Mathf.Max(borderError,
                Mathf.Abs(maps[0][N - 1, k] - maps[2][0, k]));
            borderError = Mathf.Max(borderError,
                Mathf.Abs(maps[1][N - 1, k] - maps[3][0, k]));
        }

        if (borderError > 0f)
        {
            Debug.LogError(
                $"Bordures RAW differentes : {borderError * Height:F6} m. " +
                "Creation annulee.");
            return;
        }

        AssetDatabase.CreateFolder("Assets", "Jezero_4Tiles_Data");

        GameObject root = new GameObject("Jezero_4Tiles");
        Undo.RegisterCreatedObjectUndo(root, "Creer les tuiles Jezero");

        Terrain[] tiles = new Terrain[4];

        for (int i = 0; i < 4; i++)
        {
            TerrainData data = new TerrainData
            {
                name = $"Jezero_{names[i]}_Data",
                heightmapResolution = N,
                size = new Vector3(Width, Height, Length),
                alphamapResolution = 16,
                terrainLayers = new[] { layers[0] }
            };

            data.SetHeights(0, 0, maps[i]);

            float[,,] weights = new float[16, 16, 1];

            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    weights[z, x, 0] = 1f;

            data.SetAlphamaps(0, 0, weights);

            AssetDatabase.CreateAsset(
                data, $"{assetFolder}/{data.name}.asset");

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = $"Jezero_{names[i]}";
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(
                (i % 2) * Width, 0f, (i / 2) * Length);
            go.layer = reference.gameObject.layer;

            Terrain terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = reference.materialTemplate;
            terrain.heightmapPixelError = 5f;
            terrain.drawInstanced = reference.drawInstanced;
            terrain.allowAutoConnect = false;

            tiles[i] = terrain;
        }

        // SetNeighbors : gauche, nord, droite, sud.
        tiles[0].SetNeighbors(null, tiles[2], tiles[1], null);
        tiles[1].SetNeighbors(tiles[0], tiles[3], null, null);
        tiles[2].SetNeighbors(null, null, tiles[3], tiles[0]);
        tiles[3].SetNeighbors(tiles[2], null, null, tiles[1]);

        foreach (Terrain terrain in tiles)
            terrain.Flush();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log(
            "Quatre tuiles creees.\n" +
            "Raccords RAW : identiques.\n" +
            "Emprise du groupe : 1942 x 2048 m environ.\n" +
            "Reference verticale : -2500 m, plage : 100 m.\n" +
            "Desactive maintenant le Terrain prototype. " +
            "Le placement XR reste a adapter.",
            root);
    }
}