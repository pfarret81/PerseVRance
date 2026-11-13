using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarsRockScatter : MonoBehaviour
{
    [Serializable]
    public sealed class Category
    {
        public string label = "Big";
        public bool generate = true;
        public GameObject[] variants = new GameObject[6];
        public float minDensity = 0.003f;
        public float maxDensity = 0.03f;
        public float patchSize = 25f;
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        public Vector2 stretchXZ = new Vector2(0.85f, 1.15f);
        public Vector2 burial = new Vector2(0.05f, 0.35f);
        public float tilt = 8f;
        [Range(0f, 1f)] public float slopeAlignment = 0.5f;
        public float maxSlope = 35f;
        public float gap = 0.08f;
        public float nominalDiameter = 0.65f;
        public float renderDistance = 180f;
        public float colliderDistance = 40f;
    }

    [Serializable]
    public sealed class Rock
    {
        public GameObject instance;
        public MeshRenderer renderer;
        public Collider[] colliders;
        public bool[] originalColliderStates;
        public bool originalRendererState;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public int category;
        public float sizeFactor = 1f;
    }

    [Header("Generation : outils dans l'Inspector")]
    public MarsTerrainRegion region;
    public Transform centerMarker;
    public float areaSize = 128f;
    public float clearCenterRadius = 2f;
    public int seed = 12345;
    public bool addBoxColliderIfMissing = true;
    public Category big = new Category();
    public Category medium = new Category
    {
        label = "Medium", minDensity = 0.01f, maxDensity = 0.10f,
        patchSize = 20f, nominalDiameter = 0.27f,
        renderDistance = 80f, colliderDistance = 30f,
        gap = 0.04f, burial = new Vector2(0.1f, 0.4f)
    };

    [Header("Execution : distances en metres")]
    public Transform observer;
    public Transform[] additionalPhysicsActors = new Transform[0];
    [Min(0.02f)] public float updateInterval = 0.2f;
    [Min(0f)] public float renderHysteresis = 10f;
    [Min(0f)] public float colliderHysteresis = 5f;
    public bool distanceCulling = true;
    public bool distanceColliders = true;

    [SerializeField, HideInInspector] public GameObject generatedRoot;
    [SerializeField, HideInInspector] public List<Rock> rocks = new List<Rock>();
    [SerializeField] private int visibleRocks;
    [SerializeField] private int enabledColliders;
    private float nextUpdate;

    public Category GetCategory(int index) => index == 0 ? big : medium;

    private void OnEnable()
    {
        nextUpdate = 0f;
        if (rocks != null && rocks.Count > 0 && observer == null)
            Debug.LogWarning("MarsRockScatter : Observer absent, culling non applique.", this);
    }

    private void LateUpdate()
    {
        if (observer == null || rocks == null || Time.unscaledTime < nextUpdate)
            return;
        nextUpdate = Time.unscaledTime + Mathf.Max(0.02f, updateInterval);
        visibleRocks = 0;
        enabledColliders = 0;
        Vector3 view = observer.position;
        foreach (Rock rock in rocks)
        {
            if (rock.instance == null || rock.renderer == null)
                continue;
            Category settings = GetCategory(rock.category);
            Bounds bounds = new Bounds(rock.boundsCenter, rock.boundsSize);
            float viewDistance2 = bounds.SqrDistance(view);
            float renderRadius = Mathf.Max(0f, settings.renderDistance) * rock.sizeFactor;
            bool drawn = rock.renderer.enabled;
            float renderLimit = renderRadius + (drawn ? Mathf.Max(0f, renderHysteresis) : 0f);
            bool render = rock.originalRendererState &&
                (!distanceCulling || viewDistance2 <= renderLimit * renderLimit);
            if (drawn != render)
                rock.renderer.enabled = render;
            if (render) visibleRocks++;

            float actorDistance2 = viewDistance2;
            if (additionalPhysicsActors != null)
                foreach (Transform actor in additionalPhysicsActors)
                    if (actor != null)
                        actorDistance2 = Mathf.Min(actorDistance2, bounds.SqrDistance(actor.position));
            if (rock.colliders == null) continue;
            for (int i = 0; i < rock.colliders.Length; i++)
            {
                Collider collider = rock.colliders[i];
                if (collider == null) continue;
                float limit = Mathf.Max(0f, settings.colliderDistance) +
                    (collider.enabled ? Mathf.Max(0f, colliderHysteresis) : 0f);
                bool original = rock.originalColliderStates != null &&
                    i < rock.originalColliderStates.Length && rock.originalColliderStates[i];
                bool enable = original && (!distanceColliders || actorDistance2 <= limit * limit);
                if (collider.enabled != enable) collider.enabled = enable;
                if (enable) enabledColliders++;
            }
        }
    }

    private void OnDisable()
    {
        if (rocks == null) return;
        foreach (Rock rock in rocks)
        {
            if (rock.renderer != null) rock.renderer.enabled = rock.originalRendererState;
            if (rock.colliders == null) continue;
            for (int i = 0; i < rock.colliders.Length; i++)
                if (rock.colliders[i] != null && rock.originalColliderStates != null &&
                    i < rock.originalColliderStates.Length)
                    rock.colliders[i].enabled = rock.originalColliderStates[i];
        }
    }
}
