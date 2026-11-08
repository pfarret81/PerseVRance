using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoverWaypointInstaller
{
    [MenuItem("Tools/Rover/Install Waypoint Follow On Selected")]
    private static void Install()
    {
        if (Application.isPlaying) { Debug.LogWarning("Installer hors Play."); return; }
        RoverStaticPoseTest solver=Selection.activeGameObject ?
            Selection.activeGameObject.GetComponentInParent<RoverStaticPoseTest>() : null;
        if (!solver || !solver.gameObject.scene.IsValid() || string.IsNullOrEmpty(solver.gameObject.scene.path))
        { Debug.LogWarning("Selectionner un RoverRoot dans une copie de scene enregistree."); return; }
        Transform root=solver.transform;
        if (root.GetComponent<RoverWaypointFollower>())
        { Debug.LogWarning("Un follower existe deja : modifier le parcours existant plutot que reinstaller."); return; }
        RoverArcPathDriver arc=root.GetComponent<RoverArcPathDriver>();
        if (!arc) { Debug.LogWarning("Le driver d'arc valide est requis pour copier ses 4 pivots."); return; }
        SerializedObject source=new SerializedObject(arc); source.Update();
        SerializedProperty pivots=source.FindProperty("steeringPivots");
        if (pivots==null || pivots.arraySize!=4) { Debug.LogWarning("Pivots du driver d'arc invalides."); return; }
        Transform[] steering=new Transform[4];
        for (int j=0;j<4;j++)
        {
            steering[j]=pivots.GetArrayElementAtIndex(j).objectReferenceValue as Transform;
            if (!steering[j]) { Debug.LogWarning("Un pivot de direction manque."); return; }
        }
        Vector3[] coordinates=new Vector3[6];
        if (!solver.TryGetReferenceWheelGeometry(coordinates))
        { Debug.LogWarning("Reference mecanique invalide. Ne pas recapturer une pose articulee."); return; }
        Vector3 middle=Vector3.zero; int count=0;
        for (int i=0;i<6;i++)
        {
            bool steered=false;
            for (int j=0;j<4;j++) if (solver.WheelCenter(i).IsChildOf(steering[j])) steered=true;
            if (!steered) { middle+=coordinates[i]; count++; }
        }
        if (count!=2) { Debug.LogWarning("Deux roues centrales attendues."); return; }
        middle*=0.5f;
        if (!EditorUtility.DisplayDialog("Installer le suivi de parcours ?",
            "Scene : "+root.gameObject.scene.path+"\nRover : "+root.name+
            "\nDesactive les anciens drivers, ajoute la commande partagee et un parcours d'environ 9 m. XR et terrains inchanges.",
            "Installer","Annuler")) return;
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Install Rover Waypoint Follow");
        try
        {
            Undo.RecordObject(arc,"Disable Arc Driver"); arc.enabled=false;
            RoverStraightPathDriver straight=root.GetComponent<RoverStraightPathDriver>();
            if (straight) { Undo.RecordObject(straight,"Disable Straight Driver"); straight.enabled=false; }
            RoverKinematicActuator actuator=root.GetComponent<RoverKinematicActuator>();
            if (!actuator) actuator=Undo.AddComponent<RoverKinematicActuator>(root.gameObject);
            SerializedObject actuatorSO=new SerializedObject(actuator); actuatorSO.Update();
            actuatorSO.FindProperty("solver").objectReferenceValue=solver;
            actuatorSO.FindProperty("steeringPivots").arraySize=4;
            for (int j=0;j<4;j++) actuatorSO.FindProperty("steeringPivots").GetArrayElementAtIndex(j).objectReferenceValue=steering[j];
            actuatorSO.ApplyModifiedProperties();
            GameObject pathGO=new GameObject("RoverPath_Waypoints");
            SceneManager.MoveGameObjectToScene(pathGO,root.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(pathGO,"Create Rover Path");
            RoverWaypointPath path=Undo.AddComponent<RoverWaypointPath>(pathGO);
            Quaternion heading=Quaternion.LookRotation(solver.ReferenceForward,Vector3.up);
            Vector3 start=solver.ReferencePosition+heading*new Vector3(middle.x,0f,middle.z);
            List<Vector3> local=new List<Vector3>();
            local.Add(new Vector3(0,0,0)); local.Add(new Vector3(0,0,0.75f)); local.Add(new Vector3(0,0,1.5f));
            for (int j=1;j<=12;j++)
            {
                float t=j/12f;
                local.Add(new Vector3(0.20f*(1f-Mathf.Cos(2f*Mathf.PI*t)),0,1.5f+6f*t));
            }
            local.Add(new Vector3(0,0,8.25f)); local.Add(new Vector3(0,0,9f));
            Transform[] nodes=new Transform[local.Count];
            Physics.SyncTransforms(); int unresolved=0;
            for (int i=0;i<local.Count;i++)
            {
                GameObject node=new GameObject("P"+i.ToString("00"));
                SceneManager.MoveGameObjectToScene(node,root.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(node,"Create Rover Waypoint");
                node.transform.SetParent(pathGO.transform,false);
                Vector3 p=start+heading*local[i];
                if (solver.TryProbeGround(p,out RaycastHit hit)) p.y=hit.point.y+0.05f; else unresolved++;
                node.transform.position=p; nodes[i]=node.transform;
            }
            SerializedObject pathSO=new SerializedObject(path); pathSO.Update();
            pathSO.FindProperty("points").arraySize=nodes.Length;
            for (int i=0;i<nodes.Length;i++) pathSO.FindProperty("points").GetArrayElementAtIndex(i).objectReferenceValue=nodes[i];
            pathSO.ApplyModifiedProperties();
            RoverWaypointFollower follower=Undo.AddComponent<RoverWaypointFollower>(root.gameObject);
            SerializedObject followSO=new SerializedObject(follower); followSO.Update();
            followSO.FindProperty("actuator").objectReferenceValue=actuator;
            followSO.FindProperty("path").objectReferenceValue=path;
            followSO.FindProperty("run").boolValue=false; followSO.ApplyModifiedProperties();
            RoverWaypointTimestepCheck checker=Undo.AddComponent<RoverWaypointTimestepCheck>(root.gameObject);
            SerializedObject checkSO=new SerializedObject(checker); checkSO.Update();
            checkSO.FindProperty("follower").objectReferenceValue=follower; checkSO.ApplyModifiedProperties();
            if (PrefabUtility.IsPartOfPrefabInstance(arc)) PrefabUtility.RecordPrefabInstancePropertyModifications(arc);
            if (straight && PrefabUtility.IsPartOfPrefabInstance(straight)) PrefabUtility.RecordPrefabInstancePropertyModifications(straight);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            Selection.activeGameObject=root.gameObject;
            Debug.Log("Suivi installe : parcours d'environ 9 m, largeur laterale 0.40 m, en pause. " +
                "Verifier une zone libre de 10 m devant le rover. Points sans projection visuelle au sol : "+unresolved+". Leur Y ne commande pas l'altitude du rover.",follower);
        }
        catch (Exception ex)
        {
            Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
            Debug.LogWarning("Installation annulee : "+ex.Message);
        }
    }
}

[CustomEditor(typeof(RoverWaypointPath))]
public class RoverWaypointPathEditor : Editor
{
    private void OnSceneGUI()
    {
        if (Application.isPlaying) return;
        RoverWaypointPath path=(RoverWaypointPath)target;
        for (int i=0;i<path.PointCount;i++)
        {
            Transform point=path.Point(i); if (!point) continue;
            Handles.Label(point.position,"P"+i.ToString("00"));
            EditorGUI.BeginChangeCheck();
            Vector3 position=Handles.PositionHandle(point.position,Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(point,"Move Rover Waypoint"); point.position=position;
                EditorSceneManager.MarkSceneDirty(point.gameObject.scene);
            }
        }
    }
}
