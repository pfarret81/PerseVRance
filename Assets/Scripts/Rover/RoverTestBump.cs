using UnityEngine;

[RequireComponent(
    typeof(MeshFilter),
    typeof(MeshRenderer),
    typeof(MeshCollider))]
public class RoverTestBump : MonoBehaviour
{
    [SerializeField] private Renderer appearanceSource;

    [SerializeField, Min(0.1f)] private float width = 0.60f;
    [SerializeField, Min(0.1f)] private float length = 1.00f;
    [SerializeField, Min(0.001f)] private float height = 0.03f;
    [SerializeField, Range(8, 128)] private int segments = 40;

    [ContextMenu("Build Test Bump")]
    private void BuildTestBump()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("Générer la bosse hors du mode Play.", this);
            return;
        }

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float y = height * 0.5f *
                (1f - Mathf.Cos(2f * Mathf.PI * t));

            vertices[i * 2] =
                new Vector3(-width * 0.5f, y, t * length);

            vertices[i * 2 + 1] =
                new Vector3(width * 0.5f, y, t * length);

            if (i == segments)
                continue;

            int v = i * 2;
            int index = i * 6;

            triangles[index] = v;
            triangles[index + 1] = v + 2;
            triangles[index + 2] = v + 1;

            triangles[index + 3] = v + 1;
            triangles[index + 4] = v + 2;
            triangles[index + 5] = v + 3;
        }

        Mesh mesh = new Mesh
        {
            name = "RoverTestBump",
            vertices = vertices,
            triangles = triangles
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        const string folder = "Assets/RoverTests";

        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
        {
            UnityEditor.AssetDatabase.CreateFolder(
                "Assets", "RoverTests"
            );
        }

        string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
            folder + "/RoverTestBump.asset"
        );

        UnityEditor.AssetDatabase.CreateAsset(mesh, path);

        MeshFilter filter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        MeshCollider meshCollider = GetComponent<MeshCollider>();

        UnityEditor.Undo.RecordObjects(
            new Object[] { filter, meshRenderer, meshCollider },
            "Assign Rover Test Bump"
        );

        filter.sharedMesh = mesh;

        meshCollider.sharedMesh = null;
        meshCollider.convex = false;
        meshCollider.isTrigger = false;
        meshCollider.sharedMesh = mesh;

        if (appearanceSource != null)
            meshRenderer.sharedMaterial = appearanceSource.sharedMaterial;

        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log("Bosse de test générée et enregistrée.", this);
#endif
    }
}