using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

public static class JezeroPlayerPlacement
{
    [MenuItem("Mars/Placer le XR Origin sur Jezero")]
    private static void Place()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Arrete le mode Play avant le placement.");
            return;
        }

        XROrigin origin = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<XROrigin>()
            : null;

        if (origin == null)
        {
            Debug.LogError(
                "Selectionne XR Origin (XR Rig) dans la Hierarchy.");
            return;
        }

        GameObject terrainObject = GameObject.Find("Jezero_Test_Terrain");
        Terrain terrain = terrainObject != null
            ? terrainObject.GetComponent<Terrain>()
            : null;

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError(
                "Terrain actif nomme Jezero_Test_Terrain introuvable.");
            return;
        }

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;

        Vector3 spawn = terrainPosition + new Vector3(
            terrainSize.x * 0.5f,
            0f,
            terrainSize.z * 0.5f);

        float groundY =
            terrain.SampleHeight(spawn) + terrainPosition.y;

        const float clearance = 0.15f;
        spawn.y = groundY + clearance;

        Undo.RecordObject(origin.transform, "Placer le XR Origin");
        origin.transform.position = spawn;

        EditorUtility.SetDirty(origin.transform);
        EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);

        Debug.Log(
            $"XR Origin place : {spawn.ToString("F3")}\n" +
            $"Sol au centre : {groundY:F3} m\n" +
            $"Marge initiale : {clearance:F2} m",
            origin);
    }
}