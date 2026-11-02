using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

public static class JezeroPlayerPlacement
{
    [MenuItem("Mars/Placer le XR Origin pres des raccords")]
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
            Debug.LogError("Selectionne XR Origin (XR Rig).");
            return;
        }

        GameObject root = GameObject.Find("Jezero_4Tiles");

        if (root == null)
        {
            Debug.LogError("Groupe actif Jezero_4Tiles introuvable.");
            return;
        }

        Terrain southwest = null;

        foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>())
        {
            if (terrain.name == "Jezero_SO")
            {
                southwest = terrain;
                break;
            }
        }

        if (southwest == null || southwest.terrainData == null)
        {
            Debug.LogError("Tuile Jezero_SO introuvable.");
            return;
        }

        Vector3 size = southwest.terrainData.size;

        Vector3 spawn = southwest.transform.position + new Vector3(
            size.x - 5f,
            0f,
            size.z - 5f);

        float groundY = southwest.SampleHeight(spawn)
            + southwest.transform.position.y;

        spawn.y = groundY + 0.15f;

        Undo.RecordObject(origin.transform, "Placer le XR Origin");
        origin.transform.position = spawn;

        EditorUtility.SetDirty(origin.transform);
        EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);

        Debug.Log(
            $"Placement pres des raccords : {spawn.ToString("F3")}\n" +
            $"Hauteur du sol : {groundY:F3} m\n" +
            $"Altitude source estimee : {groundY - 2500f:F3} m\n" +
            "Depart dans SO, a 5 m des raccords nord et est.",
            origin);
    }
}