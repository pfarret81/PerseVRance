using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Collections;

public sealed class MarsBigRockScatterTest : EditorWindow
{
    private const string GroupName = "Mars_BigRocks_Test";
    [SerializeField] private MarsTerrainRegion region;
    [SerializeField] private Transform centerMarker;
    [SerializeField] private GameObject[] prefabs = new GameObject[6];
    [SerializeField] private int seed = 12345;
    [SerializeField] private float areaSize = 64f;
    [SerializeField] private float density = 0.02f;
    [SerializeField] private bool useDensityMask = true;
    [SerializeField] private float densityPatchSize = 20f;
    [SerializeField] private float sparseAcceptance = 0.1f;
    [SerializeField] private float minimumSpacing = 0.9f;
    [SerializeField] private float clearCenterRadius = 2f;
    [SerializeField] private Vector2 uniformScale = new Vector2(0.8f, 1.2f);
    [SerializeField] private Vector2 xzScale = new Vector2(0.85f, 1.15f);
    [SerializeField] private Vector2 burial = new Vector2(0.05f, 0.35f);
    [SerializeField] private float tiltDegrees = 8f;
    [SerializeField] private float slopeAlignment = 0.5f;
    [SerializeField] private float maximumSlope = 35f;
    private Vector2 scroll;

    [MenuItem("Mars/Rochers/Test local Big")]
    private static void Open()
    {
        var window = GetWindow<MarsBigRockScatterTest>("Scatter Big - Test");
        window.minSize = new Vector2(440f, 560f);
        if (Selection.activeGameObject != null)
            window.region = Selection.activeGameObject.GetComponent<MarsTerrainRegion>();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(
            "Test editeur uniquement. Cree des instances classiques sur une zone limitee. " +
            "Les prefabs doivent posseder leurs colliders, sans Rigidbody. " +
            "Aucun culling des rochers ni streaming dans cette version.", MessageType.Info);
        region = (MarsTerrainRegion)EditorGUILayout.ObjectField(
            "Region", region, typeof(MarsTerrainRegion), true);
        centerMarker = (Transform)EditorGUILayout.ObjectField(
            "Centre optionnel (XZ)", centerMarker, typeof(Transform), true);
        EditorGUILayout.LabelField("Sans centre : centre geometrique de la region.");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Six prefabs Big", EditorStyles.boldLabel);
        if (prefabs == null || prefabs.Length != 6)
            Array.Resize(ref prefabs, 6);
        for (int i = 0; i < 6; i++)
            prefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                $"Variante {i + 1}", prefabs[i], typeof(GameObject), false);
        EditorGUILayout.Space();
        seed = EditorGUILayout.IntField("Graine", seed);
        areaSize = EditorGUILayout.FloatField("Cote zone (m)", areaSize);
        density = EditorGUILayout.FloatField("Densite cible (/m2)", density);
        useDensityMask = EditorGUILayout.Toggle(
            "Repartition variable", useDensityMask);

        using (new EditorGUI.DisabledScope(!useDensityMask))
        {
            densityPatchSize = EditorGUILayout.Slider(
                "Echelle des zones (m)", densityPatchSize, 2f, 100f);

            sparseAcceptance = EditorGUILayout.Slider(
                "Acceptation zones pauvres", sparseAcceptance, 0.01f, 1f);
        }
        minimumSpacing = EditorGUILayout.FloatField("Espacement centres (m)", minimumSpacing);
        clearCenterRadius = EditorGUILayout.FloatField("Rayon libre au centre (m)", clearCenterRadius);
        uniformScale = EditorGUILayout.Vector2Field("Echelle globale min/max", uniformScale);
        xzScale = EditorGUILayout.Vector2Field("Facteur X/Z min/max", xzScale);
        burial = EditorGUILayout.Vector2Field("Enfoncement min/max [0,1]", burial);
        tiltDegrees = EditorGUILayout.Slider("Inclinaison aleatoire", tiltDegrees, 0f, 30f);
        slopeAlignment = EditorGUILayout.Slider("Alignement a la pente", slopeAlignment, 0f, 1f);
        maximumSlope = EditorGUILayout.Slider("Pente maximale", maximumSlope, 0f, 60f);
        int target = Mathf.Clamp(Mathf.RoundToInt(areaSize * areaSize * density), 0, 1001);
        EditorGUILayout.LabelField($"Cible indicative : {target} rochers (limite du test : 1000)");
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Generer le test local", GUILayout.Height(34)))
                Generate();
        EditorGUILayout.EndScrollView();
    }

    private static bool ValidRange(Vector2 value, float lower, float upper)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) &&
            value.x >= lower && value.y >= value.x && value.y <= upper;
    }

    private void ValidateSetup()
    {
        if (region == null || !region.gameObject.scene.IsValid() ||
            !region.gameObject.scene.isLoaded || !region.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Choisir la region active de la scene.");
        if (region.tiles == null || region.tiles.Length == 0 ||
            region.Size.x <= 0 || region.Size.y <= 0)
            throw new InvalidOperationException("Region ou tableau de terrains invalide.");
        if (Quaternion.Angle(region.transform.rotation, Quaternion.identity) > 0.01f ||
            (region.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Region attendue sans rotation, echelle monde (1,1,1).");
        if (!float.IsFinite(areaSize) || areaSize <= 0f || areaSize > 128f ||
            !float.IsFinite(density) || density <= 0f ||
            !float.IsFinite(minimumSpacing) || minimumSpacing < 0f ||
            !float.IsFinite(clearCenterRadius) || clearCenterRadius < 0f)
            throw new InvalidOperationException("Zone : ]0,128] m ; densite positive ; distances positives ou nulles.");
        if (!ValidRange(uniformScale, 0.01f, 10f) ||
            !ValidRange(xzScale, 0.01f, 10f) || !ValidRange(burial, 0f, 1f))
            throw new InvalidOperationException("Verifier les intervalles min/max.");
        int count = Mathf.RoundToInt(areaSize * areaSize * density);
        if (count < 1 || count > 1000)
            throw new InvalidOperationException("Le test doit demander entre 1 et 1000 rochers.");
    }

    private void Generate()
    {
        GameObject group = null;
        int undoGroup = -1;
        try
        {
            ValidateSetup();
            foreach (GameObject root in region.gameObject.scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == GroupName)
                        throw new InvalidOperationException(
                            "Mars_BigRocks_Test existe deja. Le supprimer avant de regenerer.");
            }
            if (prefabs == null || prefabs.Length != 6)
                throw new InvalidOperationException("Renseigner les six prefabs.");
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                    throw new InvalidOperationException("Les six entrees doivent etre des prefabs du Project.");
                if (!prefab.activeSelf ||
                    Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.01f ||
                    (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException($"{prefab.name} : racine active, rotation 0 et echelle 1 requises.");
                if (prefab.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    throw new InvalidOperationException($"{prefab.name} : retirer les Rigidbody pour ce test statique.");
                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 1 || !renderers[0].enabled ||
                    !renderers[0].gameObject.activeSelf ||
                    renderers[0].GetComponent<MeshFilter>()?.sharedMesh == null)
                    throw new InvalidOperationException($"{prefab.name} : un seul MeshRenderer actif avec MeshFilter attendu.");
                bool validCollider = false;
                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                    if (collider.enabled && !collider.isTrigger && collider.gameObject.activeSelf)
                        validCollider = true;
                if (!validCollider)
                    throw new InvalidOperationException($"{prefab.name} : collider solide actif requis.");
            }
            int target = Mathf.RoundToInt(areaSize * areaSize * density);
            Vector3 center = centerMarker != null ? centerMarker.position :
                region.transform.TransformPoint(new Vector3(region.Size.x * 0.5f, 0f, region.Size.y * 0.5f));
            var random = new System.Random(seed);

            // Generateur separe : le decalage du bruit ne consomme pas
            // les tirages utilises pour les placements et les variantes.
            var maskRandom = new System.Random(unchecked(seed ^ 0x51F15E));

            Vector2 densityOffset = new Vector2(
                Range(maskRandom, -10000f, 10000f),
                Range(maskRandom, -10000f, 10000f));
            var accepted = new List<Vector2>(target);
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Scatter Big local");
            group = new GameObject(GroupName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, region.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(group, "Creer le groupe de rochers");
            int attempts = 0;
            int maxAttempts = target * 50;
            int[] variantCounts = new int[6];
            while (accepted.Count < target && attempts++ < maxAttempts)
            {
                float x = center.x + Range(random, -areaSize * 0.5f, areaSize * 0.5f);
                float z = center.z + Range(random, -areaSize * 0.5f, areaSize * 0.5f);
                Vector2 point = new Vector2(x, z);
                if (useDensityMask)
                {
                    float patchSize = Mathf.Max(0.01f, densityPatchSize);

                    float noise = Mathf.Clamp01(Mathf.PerlinNoise(
                        x / patchSize + densityOffset.x,
                        z / patchSize + densityOffset.y));

                    // Augmente le contraste entre les zones,
                    // sans creer une frontiere brutale.
                    float mask = Mathf.InverseLerp(0.35f, 0.65f, noise);
                    mask = Mathf.SmoothStep(0f, 1f, mask);

                    float acceptance = Mathf.Lerp(
                        Mathf.Clamp01(sparseAcceptance), 1f, mask);

                    if (random.NextDouble() >= acceptance)
                        continue;
                }

                if ((point - new Vector2(center.x, center.z)).sqrMagnitude <
                    clearCenterRadius * clearCenterRadius)
                    continue;
                bool tooClose = false;
                foreach (Vector2 p in accepted)
                    if ((p - point).sqrMagnitude < minimumSpacing * minimumSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                if (tooClose)
                    continue;
                Vector3 ground = new Vector3(x, 0f, z);
                if (!region.TryGetTerrain(ground, out Terrain terrain) ||
                    terrain.terrainData == null || !terrain.gameObject.activeInHierarchy)
                    continue;
                if (Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f ||
                    (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException("Terrains attendus sans rotation et a l'echelle 1.");
                Vector3 local = terrain.transform.InverseTransformPoint(ground);
                Vector3 size = terrain.terrainData.size;
                Vector3 normal = terrain.terrainData.GetInterpolatedNormal(
                    Mathf.Clamp01(local.x / size.x), Mathf.Clamp01(local.z / size.z));
                normal = terrain.transform.TransformDirection(normal).normalized;
                if (Vector3.Angle(Vector3.up, normal) > maximumSlope)
                    continue;
                ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
                int variant = random.Next(prefabs.Length);
                Quaternion align = Quaternion.FromToRotation(Vector3.up,
                    Vector3.Slerp(Vector3.up, normal, slopeAlignment).normalized);
                Quaternion rotation = align * Quaternion.Euler(
                    Range(random, -tiltDegrees, tiltDegrees), Range(random, 0f, 360f),
                    Range(random, -tiltDegrees, tiltDegrees));
                float scale = Range(random, uniformScale.x, uniformScale.y);
                Vector3 scaling = new Vector3(
                    scale * Range(random, xzScale.x, xzScale.y), scale,
                    scale * Range(random, xzScale.x, xzScale.y));
                float burialFraction = Range(random, burial.x, burial.y);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[variant], group.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Creer un rocher");
                instance.name = $"Big_{accepted.Count:D3}_V{variant + 1}";
                instance.transform.SetPositionAndRotation(ground, rotation);
                instance.transform.localScale = scaling;
                SettleRockOnTerrain(instance, burialFraction);
                accepted.Add(point);
                variantCounts[variant]++;
            }
            EditorSceneManager.MarkSceneDirty(group.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = group;
            Debug.Log($"Scatter Big : {accepted.Count}/{target} rochers, graine {seed}.\n" +
                $"Centre XZ : {center.x:F2}, {center.z:F2}. Zone : {areaSize:F1} m de cote.\n" +
                $"Effectifs V1..V6 : {string.Join(", ", variantCounts)}.\n" +
                "Instances classiques : tous les colliders du test restent actifs. " +
                "Aucun culling ou rendu instancie explicite ajoute.", group);
            if (accepted.Count < target)
                Debug.LogWarning("Cible non atteinte : contraintes de pente, espacement ou limites de region.", group);
        }
        catch (Exception exception)
        {
            if (undoGroup >= 0)
                Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError(exception.Message);
        }
    }

    private static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private void SettleRockOnTerrain(
        GameObject instance,
        float burialFraction)
    {
        MeshFilter filter = instance.GetComponentInChildren<MeshFilter>();

        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException(
                $"{instance.name} : mesh introuvable.");

        Mesh mesh = filter.sharedMesh;

        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;
        float requiredShift = float.NegativeInfinity;

        using (Mesh.MeshDataArray meshData =
            MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            int count = meshData[0].vertexCount;

            if (count == 0)
                throw new InvalidOperationException(
                    $"{instance.name} : mesh sans sommets.");

            using (var vertices = new NativeArray<Vector3>(
                count,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory))
            {
                meshData[0].GetVertices(vertices);

                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;

                for (int i = 0; i < count; i++)
                {
                    Vector3 point = toWorld.MultiplyPoint3x4(vertices[i]);

                    if (!region.TryGetTerrain(point, out Terrain terrain) ||
                        terrain.terrainData == null)
                    {
                        throw new InvalidOperationException(
                            $"{instance.name} : une partie du rocher " +
                            "depasse la region de terrain disponible.");
                    }

                    float terrainY =
                        terrain.SampleHeight(point) +
                        terrain.transform.position.y;

                    minimumY = Mathf.Min(minimumY, point.y);
                    maximumY = Mathf.Max(maximumY, point.y);

                    requiredShift = Mathf.Max(
                        requiredShift,
                        terrainY - point.y);
                }
            }
        }

        float transformedHeight = maximumY - minimumY;
        float burialDepth =
            Mathf.Clamp01(burialFraction) * transformedHeight;

        Vector3 position = instance.transform.position;
        position.y += requiredShift - burialDepth;
        instance.transform.position = position;
    }
}
