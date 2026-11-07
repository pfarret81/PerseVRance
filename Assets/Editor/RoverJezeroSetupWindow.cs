using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class RoverJezeroSetupWindow : EditorWindow
{
    [SerializeField] private RoverStaticPoseTest solver;
    [SerializeField] private Terrain[] terrains=new Terrain[4];
    [SerializeField] private Vector3 startPoint;
    [SerializeField] private bool pointChosen;
    [SerializeField] private float headingDegrees;
    private bool picking;

    [MenuItem("Tools/Rover/Jezero Setup")]
    private static void Open() { GetWindow<RoverJezeroSetupWindow>("Rover Jezero"); }
    private void OnEnable() { SceneView.duringSceneGui+=DuringSceneGUI; }
    private void OnDisable() { SceneView.duringSceneGui-=DuringSceneGUI; }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Hors Play, uniquement dans une COPIE de la scene Jezero. Ne modifie ni le XR ni TerrainData. Le cap 0 pointe vers +Z (nord).",MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            solver=(RoverStaticPoseTest)EditorGUILayout.ObjectField("Rover solver",solver,typeof(RoverStaticPoseTest),true);
            if (GUILayout.Button("Use Selected Rover"))
            {
                solver=Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoverStaticPoseTest>() : null;
                pointChosen=false;
            }
            if (terrains==null || terrains.Length!=4) terrains=new Terrain[4];
            for (int i=0;i<4;i++)
                terrains[i]=(Terrain)EditorGUILayout.ObjectField("Terrain "+(i+1),terrains[i],typeof(Terrain),true);
            headingDegrees=EditorGUILayout.FloatField("Heading degrees",headingDegrees);
            EditorGUILayout.LabelField("0 = nord, 90 = est");
            if (GUILayout.Button(picking ? "Cancel Picking" : "Pick Start In Scene"))
            { picking=!picking; SceneView.RepaintAll(); }
            EditorGUILayout.LabelField("Point",pointChosen ? startPoint.ToString("F3") : "Non choisi");
            if (picking) EditorGUILayout.HelpBox("Cliquer gauche sur l'un des 4 terrains dans Scene. Alt permet de naviguer.",MessageType.Info);
            using (new EditorGUI.DisabledScope(!solver || !pointChosen))
                if (GUILayout.Button("Prepare And Preview (Undoable)")) Prepare();
        }
    }
    private TerrainCollider[] GetColliders()
    {
        if (terrains==null || terrains.Length!=4) throw new InvalidOperationException("Assigner les quatre terrains.");
        TerrainCollider[] result=new TerrainCollider[4];
        for (int i=0;i<4;i++)
        {
            Terrain t=terrains[i];
            if (!t || !t.gameObject.activeInHierarchy || !t.terrainData)
                throw new InvalidOperationException("Terrain absent, inactif ou sans TerrainData.");
            TerrainCollider c=t.GetComponent<TerrainCollider>();
            if (!c || !c.enabled || c.isTrigger || c.terrainData!=t.terrainData)
                throw new InvalidOperationException("TerrainCollider absent/inactif ou TerrainData different du terrain visible.");
            for (int j=0;j<i;j++) if (c==result[j]) throw new InvalidOperationException("Terrain renseigne deux fois.");
            result[i]=c;
        }
        return result;
    }
    private static bool RayOnTerrains(TerrainCollider[] colliders,Ray ray,float length,out RaycastHit nearestHit)
    {
        nearestHit=default; bool found=false; float nearest=length;
        for (int i=0;i<colliders.Length;i++)
            if (colliders[i].Raycast(ray,out RaycastHit hit,nearest))
            { nearest=hit.distance; nearestHit=hit; found=true; }
        return found;
    }
    private void DuringSceneGUI(SceneView view)
    {
        if (!picking || Application.isPlaying) return;
        Event e=Event.current;
        if (e.type==EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type!=EventType.MouseDown || e.button!=0 || e.alt) return;
        try
        {
            TerrainCollider[] colliders=GetColliders();
            Physics.SyncTransforms();
            Ray ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (RayOnTerrains(colliders,ray,float.MaxValue,out RaycastHit hit))
            {
                startPoint=hit.point; pointChosen=true; picking=false;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
        catch (Exception ex) { Debug.LogWarning(ex.Message); picking=false; Repaint(); }
    }
    private static Transform[] GetTransforms(SerializedObject so,string field,int count)
    {
        SerializedProperty property=so.FindProperty(field);
        if (property==null || !property.isArray || property.arraySize!=count)
            throw new InvalidOperationException("References invalides : "+field);
        Transform[] values=new Transform[count];
        for (int i=0;i<count;i++)
        {
            values[i]=property.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (!values[i]) throw new InvalidOperationException("Reference manquante : "+field);
        }
        return values;
    }
    private void Prepare()
    {
        if (Application.isPlaying || !solver || !pointChosen) return;
        int undoGroup=-1;
        try
        {
            Transform root=solver.transform;
            if (!root.gameObject.scene.IsValid() || string.IsNullOrEmpty(root.gameObject.scene.path) ||
                root.parent || (root.lossyScale-Vector3.one).sqrMagnitude>0.000001f)
                throw new InvalidOperationException("Rover requis dans une scene enregistree, sans parent, echelle 1.");
            if (float.IsNaN(headingDegrees) || float.IsInfinity(headingDegrees))
                throw new InvalidOperationException("Cap invalide.");
            TerrainCollider[] colliders=GetColliders();
            for (int i=0;i<4;i++)
                if (colliders[i].gameObject.scene!=root.gameObject.scene)
                    throw new InvalidOperationException("Le rover et les terrains doivent appartenir a la meme copie de scene.");
            RoverStraightPathDriver driver=root.GetComponent<RoverStraightPathDriver>();
            if (!driver) throw new InvalidOperationException("Ajouter le driver existant au meme RoverRoot avant ce reglage.");
            SerializedObject so=new SerializedObject(solver);
            so.Update();
            Transform[] joints=GetTransforms(so,"joints",4);
            Transform[] wheels=GetTransforms(so,"wheels",6);
            if (!so.FindProperty("hasReference").boolValue || so.FindProperty("referenceJoints").arraySize!=4)
                throw new InvalidOperationException("La reference mecanique du banc de test doit etre conservee.");
            Quaternion[] restJoints=new Quaternion[4];
            for (int i=0;i<4;i++) restJoints[i]=so.FindProperty("referenceJoints").GetArrayElementAtIndex(i).quaternionValue;
            float radius=so.FindProperty("radius").floatValue;
            if (!(radius>0f)) throw new InvalidOperationException("Rayon invalide.");
            if (!EditorUtility.DisplayDialog("Configurer la copie du rover ?",
                "Scene : "+root.gameObject.scene.path+"\nRover : "+root.name+
                "\nRemplace ses surfaces, son placement et sa reference. XR et TerrainData inchanges.","Configurer","Annuler")) return;
            Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Prepare Rover On Jezero");
            Undo.RecordObjects(new UnityEngine.Object[] { root,solver,driver,joints[0],joints[1],joints[2],joints[3] },"Prepare Rover On Jezero");
            root.SetPositionAndRotation(new Vector3(startPoint.x,0f,startPoint.z),Quaternion.Euler(0f,headingDegrees,0f));
            for (int i=0;i<4;i++) joints[i].localRotation=restJoints[i];
            Physics.SyncTransforms();
            float top=float.NegativeInfinity,bottom=float.PositiveInfinity;
            for (int i=0;i<4;i++)
            { top=Mathf.Max(top,colliders[i].bounds.max.y); bottom=Mathf.Min(bottom,colliders[i].bounds.min.y); }
            top+=2f; bottom-=2f;
            float minCorrection=float.PositiveInfinity,maxCorrection=float.NegativeInfinity;
            for (int i=0;i<6;i++)
            {
                Vector3 center=wheels[i].position;
                Ray ray=new Ray(new Vector3(center.x,top,center.z),Vector3.down);
                if (!RayOnTerrains(colliders,ray,top-bottom,out RaycastHit hit))
                    throw new InvalidOperationException("Aucun terrain sous "+wheels[i].name+". Choisir un point plus eloigne des bords.");
                float correction=hit.point.y+radius-center.y;
                minCorrection=Mathf.Min(minCorrection,correction);
                maxCorrection=Mathf.Max(maxCorrection,correction);
            }
            root.position+=Vector3.up*((minCorrection+maxCorrection)*0.5f);
            so.Update();
            SerializedProperty surfaces=so.FindProperty("surfaces"); surfaces.arraySize=4;
            for (int i=0;i<4;i++) surfaces.GetArrayElementAtIndex(i).objectReferenceValue=colliders[i];
            so.FindProperty("referencePosition").vector3Value=root.position;
            so.FindProperty("referenceRotation").quaternionValue=root.rotation;
            so.FindProperty("hasReference").boolValue=true;
            so.FindProperty("axisSigns").arraySize=4;
            for (int i=0;i<4;i++)
            {
                float alignment=Vector3.Dot(joints[i].TransformDirection(Vector3.forward),root.right);
                if (Mathf.Abs(alignment)<0.5f) throw new InvalidOperationException("Axe inattendu : "+joints[i].name);
                so.FindProperty("referenceJoints").GetArrayElementAtIndex(i).quaternionValue=restJoints[i];
                so.FindProperty("axisSigns").GetArrayElementAtIndex(i).floatValue=Mathf.Sign(alignment);
            }
            so.ApplyModifiedProperties();
            SerializedObject driverSO=new SerializedObject(driver);
            driverSO.Update();
            driverSO.FindProperty("solver").objectReferenceValue=solver;
            driverSO.FindProperty("run").boolValue=false;
            driverSO.FindProperty("speed").floatValue=0.10f;
            driverSO.FindProperty("pathLength").floatValue=3f;
            driverSO.ApplyModifiedProperties();
            RoverGroundProbeDebug diagnostic=root.GetComponent<RoverGroundProbeDebug>();
            if (!diagnostic) diagnostic=Undo.AddComponent<RoverGroundProbeDebug>(root.gameObject);
            SerializedObject diagnosticSO=new SerializedObject(diagnostic);
            diagnosticSO.Update();
            diagnosticSO.FindProperty("solver").objectReferenceValue=solver;
            diagnosticSO.ApplyModifiedProperties();
            solver.InvalidateRuntime();
            if (!solver.TrySolveAt(root.position,false))
                throw new InvalidOperationException("Pose initiale refusee : "+solver.LastFailure+". Choisir un secteur moins accidente ; limites du solveur non modifiees.");
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            for (int i=0;i<4;i++)
                if (PrefabUtility.IsPartOfPrefabInstance(joints[i])) PrefabUtility.RecordPrefabInstancePropertyModifications(joints[i]);
            if (PrefabUtility.IsPartOfPrefabInstance(solver)) PrefabUtility.RecordPrefabInstancePropertyModifications(solver);
            if (PrefabUtility.IsPartOfPrefabInstance(driver)) PrefabUtility.RecordPrefabInstancePropertyModifications(driver);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"Jezero pret : reference={solver.ReferencePosition.ToString("F3")}, cap={headingDegrees:F1} deg, " +
                $"erreur={solver.LastMaxError*1000f:F3} mm. Parcours configure a 3 m, 0.10 m/s, en pause.",solver);
            Selection.activeGameObject=root.gameObject;
            SceneView.RepaintAll();
        }
        catch (Exception ex)
        {
            if (undoGroup>=0)
            {
                Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undoGroup);
                if (solver) solver.InvalidateRuntime();
            }
            Debug.LogWarning("Preparation annulee : "+ex.Message);
        }
    }
}
