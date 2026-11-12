using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class JezeroFinalImporter : EditorWindow
{
    private const int SourceN = 4097;
    private const int Grid = 8;
    private const int Step = 512;
    private const int TileN = Step + 1;
    private const float HeightRange = 300f;
    private const float AltitudeOffset = -2700f;
    private const double MarsRadius = 3396190.0;
    private const string RootName = "Jezero_Final_8x8";
    private string rawPath = "";
    private string jsonPath = "";
    private Terrain reference;

    [Serializable] private sealed class RasterInfo
    {
        public int[] size;
        public double[] geoTransform;
    }

    [MenuItem("Mars/Importer Jezero final 8x8")]
    private static void Open()
    {
        var window = GetWindow<JezeroFinalImporter>("Jezero 8x8");
        window.minSize = new Vector2(500f, 300f);
        if (Selection.activeGameObject != null)
            window.reference = Selection.activeGameObject.GetComponent<Terrain>();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("RAW 4097 -> 64 terrains de 513 echantillons", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Import specifique a l'extrait Jezero (11500,11500), altitude -2700 / -2400 m. " +
            "La correction horizontale est locale et approximative. " +
            "Le terrain de reference et ses assets ne seront pas modifies.", MessageType.Info);
        reference = (Terrain)EditorGUILayout.ObjectField(
            "Terrain de reference", reference, typeof(Terrain), true);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("RAW : " + (string.IsNullOrEmpty(rawPath) ? "non choisi" : Path.GetFileName(rawPath)));
        if (GUILayout.Button("Choisir Jezero_final_4097_H300.raw"))
            rawPath = EditorUtility.OpenFilePanel("RAW UInt16 little-endian", "", "raw");
        EditorGUILayout.LabelField("JSON : " + (string.IsNullOrEmpty(jsonPath) ? "non choisi" : Path.GetFileName(jsonPath)));
        if (GUILayout.Button("Choisir Jezero_final_4097_metadata.json"))
            jsonPath = EditorUtility.OpenFilePanel("Metadonnees gdalinfo JSON", "", "json");
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generer les 64 tuiles", GUILayout.Height(32)))
                Generate();
        }
    }

    private void Generate()
    {
        GameObject root = null;
        string assetFolder = null;
        bool success = false;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Arreter le mode Play.");
            if (reference == null || reference.terrainData == null)
                throw new InvalidOperationException("Choisir un Terrain de reference.");
            if (!File.Exists(rawPath) || !File.Exists(jsonPath))
                throw new InvalidOperationException("Choisir les fichiers RAW et JSON.");
            foreach (MarsTerrainRegion existing in Resources.FindObjectsOfTypeAll<MarsTerrainRegion>())
                if (existing.gameObject.scene.IsValid() && existing.name == RootName)
                    throw new InvalidOperationException("Un groupe Jezero_Final_8x8 existe deja dans une scene chargee.");

            RasterInfo info = JsonUtility.FromJson<RasterInfo>(File.ReadAllText(jsonPath));
            if (info == null || info.size == null || info.size.Length != 2 ||
                info.size[0] != SourceN || info.size[1] != SourceN ||
                info.geoTransform == null || info.geoTransform.Length != 6)
                throw new InvalidOperationException("JSON incompatible : raster 4097 x 4097 attendu.");
            double[] gt = info.geoTransform;
            if (Math.Abs(gt[0] - 4588866.0) > 0.001 ||
                Math.Abs(gt[3] - 1095118.0) > 0.001 ||
                Math.Abs(gt[1] - 1.0) > 1e-9 || Math.Abs(gt[5] + 1.0) > 1e-9 ||
                Math.Abs(gt[2]) > 1e-9 || Math.Abs(gt[4]) > 1e-9)
                throw new InvalidOperationException(
                    "Geotransform inattendue. Ce script attend l'extrait (11500,11500) " +
                    "du GeoTIFF Jezero original, sans reprojection.");
            byte[] bytes = File.ReadAllBytes(rawPath);
            if (bytes.LongLength != (long)SourceN * SourceN * 2)
                throw new InvalidOperationException("Taille RAW incorrecte.");

            double northAtCenter = gt[3] + SourceN * 0.5 * gt[5];
            double latitudeRad = northAtCenter / MarsRadius;
            double correction = Math.Cos(latitudeRad);
            float tileWidth = (float)(Step * gt[1] * correction);
            float tileLength = (float)(Step * -gt[5]);

            assetFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/Jezero_Final_Data");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(assetFolder));
            File.Copy(jsonPath, assetFolder + "/SourceMetadata.json");
            root = new GameObject(RootName);
            SceneManagerMove(root, reference);
            MarsTerrainRegion region = root.AddComponent<MarsTerrainRegion>();
            region.gridSize = Grid;
            region.samplesPerTile = TileN;
            region.tileWidth = tileWidth;
            region.tileLength = tileLength;
            region.altitudeOffset = AltitudeOffset;
            region.heightRange = HeightRange;
            region.referenceLatitudeDegrees = latitudeRad * 180.0 / Math.PI;
            region.projectionXScale = correction;
            region.projectedSouthWestEasting = gt[0] + 0.5 * gt[1];
            region.projectedSouthWestNorthing = gt[3] + (SourceN - 0.5) * gt[5];
            region.tiles = new Terrain[Grid * Grid];

            TerrainLayer[] layers = reference.terrainData.terrainLayers;
            TerrainLayer firstLayer = layers.Length > 0 ? layers[0] : null;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            for (int row = 0; row < Grid; row++)
            {
                for (int col = 0; col < Grid; col++)
                {
                    int index = row * Grid + col;
                    if (EditorUtility.DisplayCancelableProgressBar("Generation Jezero",
                        $"Tuile {index + 1}/64 : colonne {col}, ligne sud-nord {row}", index / 64f))
                        throw new OperationCanceledException("Generation annulee.");
                    float[,] heights = new float[TileN, TileN];
                    for (int z = 0; z < TileN; z++)
                    {
                        int sourceRow = SourceN - 1 - (row * Step + z);
                        int start = (sourceRow * SourceN + col * Step) * 2;
                        for (int x = 0; x < TileN; x++)
                        {
                            int p = start + x * 2;
                            float h = (bytes[p] | (bytes[p + 1] << 8)) / 65535f;
                            heights[z, x] = h;
                            min = Mathf.Min(min, h * HeightRange);
                            max = Mathf.Max(max, h * HeightRange);
                        }
                    }
                    TerrainData data = new TerrainData();
                    data.name = $"Jezero_C{col:D2}_R{row:D2}_Data";
                    AssetDatabase.CreateAsset(data, assetFolder + "/" + data.name + ".asset");
                    data.heightmapResolution = TileN;
                    data.size = new Vector3(tileWidth, HeightRange, tileLength);
                    data.alphamapResolution = 16;
                    data.baseMapResolution = 128;
                    if (firstLayer != null)
                    {
                        data.terrainLayers = new[] { firstLayer };
                        float[,,] weights = new float[16, 16, 1];
                        for (int z = 0; z < 16; z++)
                            for (int x = 0; x < 16; x++)
                                weights[z, x, 0] = 1f;
                        data.SetAlphamaps(0, 0, weights);
                    }
                    data.SetHeights(0, 0, heights);
                    EditorUtility.SetDirty(data);
                    GameObject go = Terrain.CreateTerrainGameObject(data);
                    go.name = $"Jezero_C{col:D2}_R{row:D2}";
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(col * tileWidth, 0f, row * tileLength);
                    go.layer = reference.gameObject.layer;
                    Terrain tile = go.GetComponent<Terrain>();
                    tile.materialTemplate = reference.materialTemplate;
                    tile.drawInstanced = reference.drawInstanced;
                    tile.heightmapPixelError = 5f;
                    tile.basemapDistance = reference.basemapDistance;
                    tile.allowAutoConnect = false;
                    tile.drawTreesAndFoliage = false;
                    region.tiles[index] = tile;
                }
            }

            for (int row = 0; row < Grid; row++)
                for (int col = 0; col < Grid; col++)
                    region.tiles[row * Grid + col].SetNeighbors(
                        Get(region, col - 1, row), Get(region, col, row + 1),
                        Get(region, col + 1, row), Get(region, col, row - 1));

            float seamError = CheckSeams(region);
            if (seamError > 0.005f)
                throw new InvalidOperationException($"Erreur de raccord inattendue : {seamError:F6} m.");
            foreach (Terrain tile in region.tiles)
                tile.Flush();
            EditorUtility.SetDirty(region);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.RegisterCreatedObjectUndo(root, "Creer Jezero 8x8 (assets conserves)");
            Selection.activeGameObject = root;
            success = true;
            Debug.Log(
                $"Jezero 8x8 cree : 64 terrains de 513 x 513.\n" +
                $"Tuile : {tileWidth:F3} x {tileLength:F3} m.\n" +
                $"Region : {region.Size.x:F3} x {region.Size.y:F3} m.\n" +
                $"Latitude de reference : {region.referenceLatitudeDegrees:F6} degres.\n" +
                $"Hauteurs locales RAW : {min:F4} / {max:F4} m.\n" +
                $"Altitudes source : {min + AltitudeOffset:F4} / {max + AltitudeOffset:F4} m.\n" +
                $"Ecart maximal des raccords Unity : {seamError:F6} m.\n" +
                $"Assets : {assetFolder}\n" +
                "Generation terminee. Aucun culling de distance ajoute. " +
                "Desactiver les anciens terrains et replacer le XR avant le test VR.", root);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!success)
            {
                if (root != null)
                    DestroyImmediate(root);
                if (!string.IsNullOrEmpty(assetFolder) && AssetDatabase.IsValidFolder(assetFolder))
                    AssetDatabase.DeleteAsset(assetFolder);
            }
        }
    }

    private static void SceneManagerMove(GameObject root, Terrain reference)
    {
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, reference.gameObject.scene);
    }

    private static Terrain Get(MarsTerrainRegion region, int col, int row)
    {
        return col < 0 || row < 0 || col >= Grid || row >= Grid
            ? null : region.tiles[row * Grid + col];
    }

    private static float CheckSeams(MarsTerrainRegion region)
    {
        float error = 0f;
        for (int row = 0; row < Grid; row++)
        {
            for (int col = 0; col < Grid; col++)
            {
                TerrainData a = Get(region, col, row).terrainData;
                if (col + 1 < Grid)
                {
                    float[,] edgeA = a.GetHeights(TileN - 1, 0, 1, TileN);
                    float[,] edgeB = Get(region, col + 1, row).terrainData.GetHeights(0, 0, 1, TileN);
                    for (int k = 0; k < TileN; k++)
                        error = Mathf.Max(error, Mathf.Abs(edgeA[k, 0] - edgeB[k, 0]) * HeightRange);
                }
                if (row + 1 < Grid)
                {
                    float[,] edgeA = a.GetHeights(0, TileN - 1, TileN, 1);
                    float[,] edgeB = Get(region, col, row + 1).terrainData.GetHeights(0, 0, TileN, 1);
                    for (int k = 0; k < TileN; k++)
                        error = Mathf.Max(error, Mathf.Abs(edgeA[0, k] - edgeB[0, k]) * HeightRange);
                }
            }
        }
        return error;
    }
}
