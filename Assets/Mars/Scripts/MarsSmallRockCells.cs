using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class MarsSmallRockCells : MonoBehaviour
{
    [Header("References")]
    public MarsTerrainRegion region;
    public Transform observer;
    public GameObject[] variants = new GameObject[6];
    public Material sharedMaterial;

    [Header("Generation reproductible")]
    public int seed = 27183;
    [Min(4f)] public float cellSize = 8f;
    [Min(0f)] public float minDensity = 0.2f;
    [Min(0f)] public float maxDensity = 2f;
    [Min(1f)] public float densityPatchSize = 16f;
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    public Vector2 stretchXZ = new Vector2(0.85f, 1.15f);
    public Vector2 burialRange = new Vector2(0.10f, 0.40f);
    [Range(0f, 30f)] public float randomTilt = 12f;
    [Range(0f, 1f)] public float slopeAlignment = 0.7f;
    [Range(0f, 70f)] public float maximumSlope = 40f;

    [Header("Distance horizontale depuis le rectangle de cellule")]
    [Min(1f)] public float renderDistance = 20f;
    [Min(1f)] public float preloadMargin = 8f;
    [Min(1f)] public float retentionMargin = 16f;
    [Min(0.05f)] public float refreshInterval = 0.25f;

    [Header("Budget de generation sur le thread principal")]
    [Range(1, 16)] public int maxCellsPerFrame = 2;
    [Min(0.1f)] public float generationBudgetMs = 2f;
    [Min(16)] public int maxCachedCells = 512;

    [Header("Rendu des petits cailloux")]
    public ShadowCastingMode castShadows = ShadowCastingMode.Off;
    public bool receiveShadows = true;

    [Header("Diagnostic en Play Mode")]
    [SerializeField] private int cachedCells;
    [SerializeField] private int drawnCells;
    [SerializeField] private int cachedStones;
    [SerializeField] private int drawnStones;
    [SerializeField] private int pendingCells;

    private sealed class Source
    {
        public Mesh mesh;
        public Matrix4x4 toRoot;
    }

    private sealed class Cell
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
        public int stones;
    }

    private struct Request
    {
        public Vector2Int key;
        public float distance2;
    }

    private readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
    private readonly List<Vector2Int> removals = new List<Vector2Int>();
    private readonly List<Request> requests = new List<Request>();
    private readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();
    private Source[] sources;
    private Material material;
    private GameObject generatedRoot;
    private Vector3 origin;
    private Vector2 regionSize;
    private Vector2 noiseOffset;
    private int columns, rows;
    private float nextRefresh;
    private bool ready;

    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    private static bool RangeOK(Vector2 r, float min, float max) =>
        Finite(r.x) && Finite(r.y) && r.x >= min && r.y >= r.x && r.y <= max;
    private static float Range(System.Random r, float min, float max) =>
        min + (float)r.NextDouble() * (max-min);

    private void OnEnable()
    {
        try
        {
            Initialize();
            ready = true;
            nextRefresh = 0f;
        }
        catch (Exception exception)
        {
            Debug.LogError("Small Rocks : " + exception.Message, this);
            enabled = false;
        }
    }

    private void Initialize()
    {
        if (region == null || observer == null || !region.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Renseigner Region active et Observer.");
        if (Quaternion.Angle(region.transform.rotation, Quaternion.identity) > 0.01f ||
            (region.transform.lossyScale-Vector3.one).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Region attendue sans rotation et a l'echelle monde 1.");
        regionSize = region.Size;
        if (regionSize.x <= 0 || regionSize.y <= 0 || region.tiles == null)
            throw new InvalidOperationException("Region vide ou dimensions invalides.");
        if (!Finite(cellSize) || cellSize < 4 || cellSize > 32 ||
            !Finite(minDensity) || !Finite(maxDensity) || minDensity < 0 ||
            maxDensity < minDensity || maxDensity <= 0 ||
            !Finite(densityPatchSize) || densityPatchSize < 1 ||
            cellSize*cellSize*maxDensity > 2048)
            throw new InvalidOperationException("Cellule 4..32 m, densites ordonnees et positives, 2048 candidats/cellule maximum.");
        if (!Finite(renderDistance) || renderDistance < 1 || renderDistance > 100 ||
            !Finite(preloadMargin) || preloadMargin < 1 || !Finite(retentionMargin) ||
            retentionMargin < preloadMargin || !Finite(refreshInterval) || refreshInterval < 0.05f ||
            maxCellsPerFrame < 1 || !Finite(generationBudgetMs) || generationBudgetMs <= 0)
            throw new InvalidOperationException("Verifier les distances et budgets ; retention >= preload requis.");
        if (!RangeOK(scaleRange,0.01f,3f) || !RangeOK(stretchXZ,0.01f,2f) ||
            !RangeOK(burialRange,0f,1f))
            throw new InvalidOperationException("Intervalles d'echelle/enfoncement invalides.");
        int side = 2*Mathf.CeilToInt((renderDistance+retentionMargin)/cellSize)+3;
        if ((long)side*side > maxCachedCells)
            throw new InvalidOperationException($"Budget cache insuffisant : prevoir au moins {side*side} cellules avec ces distances.");
        if (variants == null || variants.Length == 0)
            throw new InvalidOperationException("Renseigner les variantes Small depuis Project.");
        sources = new Source[variants.Length];
        material = sharedMaterial;
        for (int i=0;i<variants.Length;i++)
        {
            GameObject source = variants[i];
            if (source == null || source.scene.IsValid())
                throw new InvalidOperationException("Chaque variante doit etre un asset FBX/prefab de Project, pas un objet de scene.");
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers = source.GetComponentsInChildren<MeshRenderer>(true);
            if (filters.Length != 1 || renderers.Length != 1 ||
                filters[0].sharedMesh == null || filters[0].gameObject != renderers[0].gameObject)
                throw new InvalidOperationException($"{source.name} : un seul mesh et renderer attendu.");
            Mesh mesh = filters[0].sharedMesh;
            if (!mesh.isReadable)
                throw new InvalidOperationException($"{source.name} : activer Read/Write dans l'import FBX.");
            if (mesh.subMeshCount != 1 || mesh.vertexCount == 0)
                throw new InvalidOperationException($"{source.name} : mesh non vide avec un seul sous-mesh requis.");
            Matrix4x4 toRoot = source.transform.worldToLocalMatrix * filters[0].transform.localToWorldMatrix;
            if (toRoot.determinant <= 0)
                throw new InvalidOperationException($"{source.name} : transformation miroir non prise en charge.");
            if (Quaternion.Angle(source.transform.localRotation,Quaternion.identity)>0.01f ||
                (source.transform.localScale-Vector3.one).sqrMagnitude>0.000001f)
                throw new InvalidOperationException($"{source.name} : racine a rotation 0 et echelle 1 requise (utiliser un prefab parent si besoin).");
            if (material == null) material=renderers[0].sharedMaterial;
            if (material == null)
                throw new InvalidOperationException("Renseigner un materiau partage.");
            if (sharedMaterial == null && renderers[0].sharedMaterial != material)
                throw new InvalidOperationException("Materiaux differents : renseigner Shared Material pour imposer un materiau commun.");
            sources[i] = new Source { mesh=mesh, toRoot=toRoot };
        }
        origin=region.transform.position;
        columns=Mathf.CeilToInt(regionSize.x/cellSize);
        rows=Mathf.CeilToInt(regionSize.y/cellSize);
        var random=new System.Random(seed);
        noiseOffset=new Vector2(Range(random,-10000,10000),Range(random,-10000,10000));
        generatedRoot=new GameObject("SmallRocks_RuntimeCells");
        generatedRoot.hideFlags=HideFlags.DontSave;
        generatedRoot.transform.SetParent(transform,false);
        // Geometrie exprimee en coordonnees monde : parent independant des transforms de cet objet.
        generatedRoot.transform.SetParent(null,true);
        generatedRoot.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        generatedRoot.transform.localScale=Vector3.one;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(generatedRoot,gameObject.scene);
    }

    private float Distance2(Vector2Int key, Vector3 point)
    {
        float x0=origin.x+key.x*cellSize;
        float z0=origin.z+key.y*cellSize;
        float x1=origin.x+Mathf.Min((key.x+1)*cellSize,regionSize.x);
        float z1=origin.z+Mathf.Min((key.y+1)*cellSize,regionSize.y);
        float dx=point.x-Mathf.Clamp(point.x,x0,x1);
        float dz=point.z-Mathf.Clamp(point.z,z0,z1);
        return dx*dx+dz*dz;
    }

    private void Refresh(Vector3 view)
    {
        float keep=renderDistance+retentionMargin;
        removals.Clear();
        foreach (var pair in cells)
            if (Distance2(pair.Key,view)>keep*keep) removals.Add(pair.Key);
        foreach(Vector2Int key in removals)
        {
            DestroyCell(cells[key]);
            cells.Remove(key);
        }
        queue.Clear();
        requests.Clear();
        float preload=renderDistance+preloadMargin;
        int x0=Mathf.Clamp(Mathf.FloorToInt((view.x-origin.x-preload)/cellSize),0,columns-1);
        int x1=Mathf.Clamp(Mathf.FloorToInt((view.x-origin.x+preload)/cellSize),0,columns-1);
        int z0=Mathf.Clamp(Mathf.FloorToInt((view.z-origin.z-preload)/cellSize),0,rows-1);
        int z1=Mathf.Clamp(Mathf.FloorToInt((view.z-origin.z+preload)/cellSize),0,rows-1);
        for(int z=z0;z<=z1;z++)
            for(int x=x0;x<=x1;x++)
            {
                Vector2Int key=new Vector2Int(x,z);
                float d=Distance2(key,view);
                if(d<=preload*preload && !cells.ContainsKey(key))
                    requests.Add(new Request{key=key,distance2=d});
            }
        requests.Sort((a,b)=>a.distance2.CompareTo(b.distance2));
        foreach(Request request in requests) queue.Enqueue(request.key);
    }

    private void LateUpdate()
    {
        if(!ready || observer==null) return;
        try
        {
            Vector3 view=observer.position;
            if(Time.unscaledTime>=nextRefresh)
            {
                Refresh(view);
                nextRefresh=Time.unscaledTime+refreshInterval;
            }
            double started=Time.realtimeSinceStartupAsDouble;
            int generated=0;
            while(queue.Count>0 && generated<maxCellsPerFrame && cells.Count<maxCachedCells)
            {
                Vector2Int key=queue.Dequeue();
                if(cells.ContainsKey(key) || Distance2(key,view)>
                    (renderDistance+preloadMargin)*(renderDistance+preloadMargin)) continue;
                cells.Add(key,BuildCell(key));
                generated++;
                if((Time.realtimeSinceStartupAsDouble-started)*1000.0>=generationBudgetMs) break;
            }
            cachedCells=cells.Count;
            cachedStones=drawnCells=drawnStones=0;
            foreach(var pair in cells)
            {
                Cell cell=pair.Value;
                cachedStones+=cell.stones;
                if(cell.renderer==null) continue;
                bool visible=Distance2(pair.Key,view)<=renderDistance*renderDistance;
                cell.renderer.enabled=visible;
                if(visible){drawnCells++;drawnStones+=cell.stones;}
            }
            pendingCells=queue.Count;
        }
        catch(Exception exception)
        {
            Debug.LogError("Small Rocks : "+exception.Message,this);
            enabled=false;
        }
    }

    private Cell BuildCell(Vector2Int key)
    {
        int cellSeed;
        unchecked
        {
            uint hash=(uint)seed;
            hash=(hash^(uint)key.x)*16777619u;
            hash=(hash^(uint)key.y)*16777619u;
            hash^=hash>>16;
            cellSeed=(int)(hash&0x7fffffffu);
        }
        var random=new System.Random(cellSeed);
        Vector3 basePosition=new Vector3(origin.x+key.x*cellSize,origin.y,origin.z+key.y*cellSize);
        float width=Mathf.Min(cellSize,regionSize.x-key.x*cellSize);
        float length=Mathf.Min(cellSize,regionSize.y-key.y*cellSize);
        float expectedCandidates=width*length*maxDensity;
        int candidates=Mathf.FloorToInt(expectedCandidates);
        if(random.NextDouble()<expectedCandidates-candidates) candidates++;
        var combines=new List<CombineInstance>(candidates);
        long vertices=0;
        for(int i=0;i<candidates;i++)
        {
            Vector3 point=new Vector3(basePosition.x+Range(random,0,width),0,basePosition.z+Range(random,0,length));
            float noise=Mathf.Clamp01(Mathf.PerlinNoise(point.x/densityPatchSize+noiseOffset.x,
                point.z/densityPatchSize+noiseOffset.y));
            float mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.35f,0.65f,noise));
            if(random.NextDouble()>Mathf.Lerp(minDensity,maxDensity,mask)/maxDensity) continue;
            if(!region.TryGetTerrain(point,out Terrain terrain) || terrain.terrainData==null ||
                !terrain.gameObject.activeInHierarchy) continue;
            if(Quaternion.Angle(terrain.transform.rotation,Quaternion.identity)>0.01f ||
                (terrain.transform.lossyScale-Vector3.one).sqrMagnitude>0.000001f)
                throw new InvalidOperationException("Terrains sans rotation et echelle monde 1 requis.");
            Vector3 local=terrain.transform.InverseTransformPoint(point);
            Vector3 size=terrain.terrainData.size;
            Vector3 normal=terrain.terrainData.GetInterpolatedNormal(
                Mathf.Clamp01(local.x/size.x),Mathf.Clamp01(local.z/size.z));
            normal=terrain.transform.TransformDirection(normal).normalized;
            if(Vector3.Angle(Vector3.up,normal)>maximumSlope) continue;
            float ground=terrain.SampleHeight(point)+terrain.transform.position.y;
            Source source=sources[random.Next(sources.Length)];
            float scale=Range(random,scaleRange.x,scaleRange.y);
            Vector3 scaling=new Vector3(scale*Range(random,stretchXZ.x,stretchXZ.y),scale,
                scale*Range(random,stretchXZ.x,stretchXZ.y));
            Quaternion rotation=Quaternion.FromToRotation(Vector3.up,
                Vector3.Slerp(Vector3.up,normal,slopeAlignment).normalized)*Quaternion.Euler(
                Range(random,-randomTilt,randomTilt),Range(random,0,360),Range(random,-randomTilt,randomTilt));
            Matrix4x4 oriented=Matrix4x4.TRS(Vector3.zero,rotation,scaling)*source.toRoot;
            Bounds bounds=source.mesh.bounds;
            float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 p=bounds.center+Vector3.Scale(bounds.extents,new Vector3(
                    (corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                float y=oriented.MultiplyPoint3x4(p).y;
                minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
            }
            Vector3 position=new Vector3(point.x-basePosition.x,
                ground-basePosition.y-minY-Range(random,burialRange.x,burialRange.y)*(maxY-minY),
                point.z-basePosition.z);
            combines.Add(new CombineInstance{mesh=source.mesh,subMeshIndex=0,
                transform=Matrix4x4.Translate(position)*oriented});
            vertices+=source.mesh.vertexCount;
        }
        Cell result=new Cell{stones=combines.Count};
        if(combines.Count==0) return result;
        Mesh mesh=null;
        GameObject go=null;
        try
        {
            mesh=new Mesh{name=$"Small_C{key.x}_R{key.y}",hideFlags=HideFlags.DontSave};
            mesh.indexFormat=vertices>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.CombineMeshes(combines.ToArray(),true,true,false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            go=new GameObject(mesh.name);
            go.hideFlags=HideFlags.DontSave;
            go.layer=gameObject.layer;
            go.transform.SetParent(generatedRoot.transform,false);
            go.transform.position=basePosition;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=castShadows;
            renderer.receiveShadows=receiveShadows;
            renderer.enabled=false;
            result.gameObject=go;result.mesh=mesh;result.renderer=renderer;
            return result;
        }
        catch
        {
            if(go!=null) Destroy(go);
            if(mesh!=null) Destroy(mesh);
            throw;
        }
    }

    private void DestroyCell(Cell cell)
    {
        if(cell.gameObject!=null) Destroy(cell.gameObject);
        if(cell.mesh!=null) Destroy(cell.mesh);
    }

    private void OnDisable()
    {
        ready=false;
        foreach(Cell cell in cells.Values) DestroyCell(cell);
        cells.Clear();queue.Clear();requests.Clear();removals.Clear();
        if(generatedRoot!=null) Destroy(generatedRoot);
        generatedRoot=null;sources=null;material=null;
        cachedCells=cachedStones=drawnCells=drawnStones=pendingCells=0;
    }
}
