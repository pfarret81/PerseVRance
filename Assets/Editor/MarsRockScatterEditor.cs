using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(MarsRockScatter))]
public sealed class MarsRockScatterEditor : Editor
{
    private struct Occupied
    {
        public Vector2 position;
        public float radius;
        public float gap;
    }

    private const float BucketSize = 2f;
    private readonly Dictionary<Vector2Int, List<Occupied>> occupied =
        new Dictionary<Vector2Int, List<Occupied>>();
    private float largestRadius;
    private float largestGap;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var settings = (MarsRockScatter)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Prototype local Big + Medium, instances classiques. Zone limitee a 256 m, " +
            "12000 candidats maximum. Genere d'abord Big, puis Medium. " +
            "Pas de modification des FBX/prefabs sources.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generer Big + Medium", GUILayout.Height(32)))
                Generate(settings);
            if (GUILayout.Button("Effacer uniquement les rochers de cet outil"))
                Clear(settings);
        }
    }

    private static float RandomRange(System.Random random, float low, float high)
        => low + (float)random.NextDouble() * (high - low);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool RangeValid(Vector2 range, float low, float high)
        => Finite(range.x) && Finite(range.y) && range.x >= low &&
            range.y >= range.x && range.y <= high;

    private static void Validate(MarsRockScatter s)
    {
        if (s.region == null || !s.region.gameObject.activeInHierarchy ||
            s.region.tiles == null || s.region.tiles.Length == 0)
            throw new InvalidOperationException("Choisir une region active contenant les terrains.");
        if (!s.gameObject.scene.IsValid() || EditorUtility.IsPersistent(s))
            throw new InvalidOperationException("Utiliser un composant dans la scene, pas un prefab asset.");
        if (s.generatedRoot != null || (s.rocks != null && s.rocks.Count > 0))
            throw new InvalidOperationException("Effacer la generation precedente avec le bouton de cet outil.");
        if (!Finite(s.areaSize) || s.areaSize <= 0f || s.areaSize > 256f ||
            !Finite(s.clearCenterRadius) || s.clearCenterRadius < 0f)
            throw new InvalidOperationException("Zone entre 0 et 256 m ; rayon libre positif ou nul.");
        if (Quaternion.Angle(s.region.transform.rotation, Quaternion.identity) > 0.01f ||
            (s.region.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Region sans rotation, echelle monde (1,1,1) requise.");
        double candidates = 0;
        for (int index = 0; index < 2; index++)
        {
            MarsRockScatter.Category c = s.GetCategory(index);
            if (!c.generate) continue;
            if (!Finite(c.minDensity) || !Finite(c.maxDensity) ||
                c.minDensity < 0 || c.maxDensity < c.minDensity || c.maxDensity <= 0 ||
                !Finite(c.patchSize) || c.patchSize <= 0 || !Finite(c.gap) || c.gap < 0 ||
                !Finite(c.nominalDiameter) || c.nominalDiameter <= 0 ||
                !RangeValid(c.scaleRange, 0.01f, 5f) ||
                !RangeValid(c.stretchXZ, 0.01f, 3f) || !RangeValid(c.burial, 0f, 1f))
                throw new InvalidOperationException($"Verifier les plages et densites : {c.label}.");
            if (!Finite(c.tilt) || c.tilt < 0 || c.tilt > 45 ||
                !Finite(c.maxSlope) || c.maxSlope < 0 || c.maxSlope > 80 ||
                !Finite(c.slopeAlignment) || c.slopeAlignment < 0 || c.slopeAlignment > 1)
                throw new InvalidOperationException($"Angles invalides : {c.label}.");
            candidates += s.areaSize * (double)s.areaSize * c.maxDensity;
            if (c.variants == null || c.variants.Length == 0)
                throw new InvalidOperationException($"Aucune variante : {c.label}.");
            foreach (GameObject prefab in c.variants)
            {
                if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                    throw new InvalidOperationException($"Renseigner toutes les variantes {c.label} depuis Project.");
                if (!prefab.activeSelf ||
                    Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.01f ||
                    (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException($"{prefab.name} : racine active, rotation 0, echelle 1.");
                if (prefab.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    throw new InvalidOperationException($"{prefab.name} : aucun Rigidbody pour ces rochers statiques.");
                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 1 || !renderers[0].enabled ||
                    renderers[0].GetComponent<MeshFilter>()?.sharedMesh == null)
                    throw new InvalidOperationException($"{prefab.name} : un seul MeshRenderer avec MeshFilter attendu.");
                Transform current = renderers[0].transform;
                while (current != null)
                {
                    if (!current.gameObject.activeSelf)
                        throw new InvalidOperationException($"{prefab.name} : hierarchie visuelle inactive.");
                    if (current == prefab.transform) break;
                    current = current.parent;
                }
                bool solid = false;
                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                    solid |= collider.enabled && !collider.isTrigger && collider.gameObject.activeSelf;
                if (!solid && !s.addBoxColliderIfMissing)
                    throw new InvalidOperationException($"{prefab.name} : collider manquant.");
            }
        }
        if (candidates > 12000)
            throw new InvalidOperationException("Plus de 12000 candidats : reduire la zone ou les densites.");
    }

    private Vector2Int Key(Vector2 point) => new Vector2Int(
        Mathf.FloorToInt(point.x / BucketSize), Mathf.FloorToInt(point.y / BucketSize));

    private bool Overlaps(Vector2 point, float radius, float gap)
    {
        Vector2Int center = Key(point);
        int reach = Mathf.CeilToInt((radius + largestRadius + Mathf.Max(gap, largestGap)) / BucketSize);
        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (occupied.TryGetValue(center + new Vector2Int(dx, dz), out List<Occupied> list))
                    foreach (Occupied other in list)
                    {
                        float limit = radius + other.radius + Mathf.Max(gap, other.gap);
                        if ((point - other.position).sqrMagnitude < limit * limit) return true;
                    }
        return false;
    }

    private void Reserve(Vector2 point, float radius, float gap)
    {
        Vector2Int key = Key(point);
        if (!occupied.TryGetValue(key, out List<Occupied> list))
            occupied[key] = list = new List<Occupied>();
        list.Add(new Occupied { position = point, radius = radius, gap = gap });
        largestRadius = Mathf.Max(largestRadius, radius);
        largestGap = Mathf.Max(largestGap, gap);
    }

    private void Generate(MarsRockScatter s)
    {
        int undoGroup = -1;
        try
        {
            Validate(s);
            occupied.Clear();
            largestRadius = largestGap = 0f;
            Vector3 center = s.centerMarker != null ? s.centerMarker.position :
                s.region.transform.TransformPoint(new Vector3(s.region.Size.x * 0.5f, 0f, s.region.Size.y * 0.5f));
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Scatter Big et Medium");
            Undo.RecordObject(s, "Enregistrer le scatter");
            GameObject group = new GameObject("Mars_Rocks_Generated");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, s.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(group, "Creer les rochers");
            s.generatedRoot = group;
            s.rocks = new List<MarsRockScatter.Rock>();
            int[] counts = new int[2];
            for (int category = 0; category < 2; category++)
            {
                MarsRockScatter.Category c = s.GetCategory(category);
                if (!c.generate) continue;
                GameObject categoryRoot = new GameObject(c.label);
                categoryRoot.transform.SetParent(group.transform, false);
                Undo.RegisterCreatedObjectUndo(categoryRoot, "Creer la categorie");
                var random = new System.Random(unchecked(s.seed + category * 104729));
                Vector2 offset = new Vector2(RandomRange(random, -10000, 10000), RandomRange(random, -10000, 10000));
                int candidates = Mathf.RoundToInt(s.areaSize * s.areaSize * c.maxDensity);
                for (int i = 0; i < candidates; i++)
                {
                    if (i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Scatter", $"{c.label} : {i}/{candidates} candidats", i / (float)Mathf.Max(1,candidates)))
                        throw new OperationCanceledException("Generation annulee.");
                    Vector3 point = new Vector3(center.x + RandomRange(random, -0.5f, 0.5f) * s.areaSize,
                        0f, center.z + RandomRange(random, -0.5f, 0.5f) * s.areaSize);
                    float noise = Mathf.Clamp01(Mathf.PerlinNoise(
                        point.x / c.patchSize + offset.x, point.z / c.patchSize + offset.y));
                    float mask = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.35f, 0.65f, noise));
                    float localDensity = Mathf.Lerp(c.minDensity, c.maxDensity, mask);
                    if (random.NextDouble() > localDensity / c.maxDensity) continue;
                    if (new Vector2(point.x-center.x, point.z-center.z).sqrMagnitude <
                        s.clearCenterRadius * s.clearCenterRadius) continue;
                    if (!s.region.TryGetTerrain(point, out Terrain terrain) ||
                        terrain.terrainData == null || !terrain.gameObject.activeInHierarchy) continue;
                    if (Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f ||
                        (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                        throw new InvalidOperationException("Les terrains doivent rester sans rotation et a l'echelle 1.");
                    Vector3 local = terrain.transform.InverseTransformPoint(point);
                    Vector3 terrainSize = terrain.terrainData.size;
                    Vector3 normal = terrain.terrainData.GetInterpolatedNormal(
                        Mathf.Clamp01(local.x / terrainSize.x), Mathf.Clamp01(local.z / terrainSize.z));
                    normal = terrain.transform.TransformDirection(normal).normalized;
                    if (Vector3.Angle(Vector3.up, normal) > c.maxSlope) continue;
                    point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                    int variant = random.Next(c.variants.Length);
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(c.variants[variant], categoryRoot.transform);
                    instance.name = $"{c.label}_{counts[category]:D4}_V{variant+1}";
                    Quaternion align = Quaternion.FromToRotation(Vector3.up,
                        Vector3.Slerp(Vector3.up, normal, c.slopeAlignment).normalized);
                    instance.transform.SetPositionAndRotation(point, align * Quaternion.Euler(
                        RandomRange(random,-c.tilt,c.tilt), RandomRange(random,0,360), RandomRange(random,-c.tilt,c.tilt)));
                    float scale = RandomRange(random,c.scaleRange.x,c.scaleRange.y);
                    instance.transform.localScale = new Vector3(scale*RandomRange(random,c.stretchXZ.x,c.stretchXZ.y),
                        scale,scale*RandomRange(random,c.stretchXZ.x,c.stretchXZ.y));
                    MeshRenderer renderer = instance.GetComponentInChildren<MeshRenderer>();
                    Bounds bounds = renderer.bounds;
                    float radius = new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
                    Vector2 footprintCenter = new Vector2(bounds.center.x,bounds.center.z);
                    if (Overlaps(footprintCenter,radius,c.gap))
                    {
                        DestroyImmediate(instance);
                        continue;
                    }
                    float burial = RandomRange(random,c.burial.x,c.burial.y);
                    Vector3 final = instance.transform.position;
                    final.y += point.y - bounds.min.y - burial*bounds.size.y;
                    instance.transform.position = final;
                    bool solid = false;
                    foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                        solid |= collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
                    if (!solid && s.addBoxColliderIfMissing)
                        renderer.gameObject.AddComponent<BoxCollider>();
                    Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                    bool[] states = new bool[colliders.Length];
                    for (int k=0;k<colliders.Length;k++) states[k]=colliders[k].enabled;
                    bounds = renderer.bounds;
                    s.rocks.Add(new MarsRockScatter.Rock {
                        instance=instance, renderer=renderer, colliders=colliders,
                        originalColliderStates=states, originalRendererState=renderer.enabled,
                        category=category, boundsCenter=bounds.center,boundsSize=bounds.size,
                        sizeFactor=Mathf.Clamp(Mathf.Sqrt(Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))
                            / c.nominalDiameter),0.5f,2f)
                    });
                    Undo.RegisterCreatedObjectUndo(instance, "Creer un rocher");
                    Reserve(footprintCenter,radius,c.gap);
                    counts[category]++;
                }
            }
            EditorUtility.SetDirty(s);
            EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = group;
            Debug.Log($"Scatter termine : Big={counts[0]}, Medium={counts[1]}. Graine={s.seed}.\n" +
                $"Zone={s.areaSize:F1} x {s.areaSize:F1} m ; centre XZ={center.x:F2}, {center.z:F2}.\n" +
                "Les densites min/max sont des intensites avant rejet par pente, exclusion et espacement. " +
                "Instances classiques, aucune optimisation GPU explicite.",s);
        }
        catch (Exception exception)
        {
            if (undoGroup>=0) Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError(exception.Message,s);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    private static void Clear(MarsRockScatter s)
    {
        Undo.IncrementCurrentGroup();
        int id=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Effacer le scatter");
        Undo.RecordObject(s,"Effacer les references");
        if(s.generatedRoot!=null) Undo.DestroyObjectImmediate(s.generatedRoot);
        s.generatedRoot=null;
        s.rocks=new List<MarsRockScatter.Rock>();
        EditorUtility.SetDirty(s);
        EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
        Undo.CollapseUndoOperations(id);
    }
}
