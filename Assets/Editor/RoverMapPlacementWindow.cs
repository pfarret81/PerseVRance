using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class RoverMapPlacementWindow : EditorWindow
{
    [SerializeField] private RoverStaticPoseTest rover;
    [SerializeField] private MarsTerrainRegion region;
    [SerializeField] private Transform rocksRoot;
    [SerializeField] private RoverWaypointPath path;
    [SerializeField] private bool movePath = true;
    [SerializeField] private Vector3 groundPoint;
    [SerializeField] private float yaw;
    private bool picking, hasPoint, previewValid;
    private readonly Vector3[] previewWheels = new Vector3[6];
    private Vector3 previewRoot;
    private Quaternion previewRotation;
    private string status = "Choisir le rover, la region et le depart.";

    [MenuItem("Tools/Rover/Place Rover On New Map")]
    private static void Open()
    {
        RoverMapPlacementWindow window = GetWindow<RoverMapPlacementWindow>("Rover : placement");
        if (Selection.activeGameObject)
        {
            RoverStaticPoseTest selected = Selection.activeGameObject.GetComponentInParent<RoverStaticPoseTest>();
            if (selected) window.AssignRover(selected);
        }
        if (!window.region)
        {
            MarsTerrainRegion[] regions = UnityEngine.Object.FindObjectsByType<MarsTerrainRegion>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (regions.Length == 1) window.region = regions[0];
        }
        window.Show();
    }
    private void OnEnable() { SceneView.duringSceneGui += DrawScene; }
    private void OnDisable() { SceneView.duringSceneGui -= DrawScene; }
    private void OnSelectionChange() { Repaint(); }
    private void AssignRover(RoverStaticPoseTest value)
    {
        rover = value; previewValid = false; hasPoint = false;
        if (!rover) return;
        Vector3 forward = rover.ReferenceForward;
        yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        RoverWaypointFollower follower = rover.GetComponent<RoverWaypointFollower>();
        if (follower)
        {
            SerializedObject so = new SerializedObject(follower);
            SerializedProperty p = so.FindProperty("path");
            if (p != null) path = p.objectReferenceValue as RoverWaypointPath;
        }
    }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Hors Play. Le clic choisit le sol sous le milieu des roues centrales. " +
            "La previsualisation ne deplace pas durablement le rover. Valider enregistre le nouveau depart.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            RoverStaticPoseTest next = (RoverStaticPoseTest)EditorGUILayout.ObjectField("Rover", rover, typeof(RoverStaticPoseTest), true);
            if (next != rover) AssignRover(next);
            EditorGUI.BeginChangeCheck();
            region = (MarsTerrainRegion)EditorGUILayout.ObjectField("Region terrain", region, typeof(MarsTerrainRegion), true);
            rocksRoot = (Transform)EditorGUILayout.ObjectField("Racine rochers", rocksRoot, typeof(Transform), true);
            path = (RoverWaypointPath)EditorGUILayout.ObjectField("Parcours existant", path, typeof(RoverWaypointPath), true);
            movePath = EditorGUILayout.Toggle("Replacer le parcours", movePath);
            yaw = EditorGUILayout.FloatField("Cap monde (degres)", yaw);
            Vector3 point = EditorGUILayout.Vector3Field("Point au sol", groundPoint);
            if (point != groundPoint) { groundPoint = point; hasPoint = true; }
            if (EditorGUI.EndChangeCheck()) { previewValid = false; SceneView.RepaintAll(); }
            EditorGUILayout.LabelField("Cap : 0 = +Z ; 90 = +X.");
            if (GUILayout.Button(picking ? "Annuler le choix" : "Choisir le depart dans Scene"))
            { picking = !picking; SceneView.RepaintAll(); }
            using (new EditorGUI.DisabledScope(!rover || !region || !hasPoint))
            {
                if (GUILayout.Button("Previsualiser les six appuis")) Execute(false);
                if (GUILayout.Button("Valider et placer le rover")) Execute(true);
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(status, MessageType.None);
        EditorGUILayout.HelpBox("La validation des appuis ne verifie pas le volume du chassis ni la " +
            "traversabilite du parcours. Choisir un depart degage. Aucun changement du XR Origin.", MessageType.Warning);
    }
    private Transform[] ReadSteering()
    {
        RoverKinematicActuator actuator = rover.GetComponent<RoverKinematicActuator>();
        if (!actuator || actuator.Solver != rover) throw new InvalidOperationException("Actuator absent ou solveur incorrect.");
        SerializedObject so = new SerializedObject(actuator);
        SerializedProperty p = so.FindProperty("steeringPivots");
        if (p == null || !p.isArray || p.arraySize != 4) throw new InvalidOperationException("Quatre pivots de direction requis.");
        Transform[] result = new Transform[4];
        for (int i = 0; i < 4; i++) result[i] = p.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
        return result;
    }
    private Collider[] CollectSurfaces()
    {
        if (!rover || !region || !rover.gameObject.scene.IsValid() ||
            rover.gameObject.scene != region.gameObject.scene)
            throw new InvalidOperationException("Rover et region doivent appartenir a la meme scene chargee.");
        if (rocksRoot && rocksRoot.gameObject.scene != rover.gameObject.scene)
            throw new InvalidOperationException("Les rochers doivent appartenir a la meme scene.");
        if (rocksRoot && (rocksRoot == rover.transform || rocksRoot.IsChildOf(rover.transform) ||
            rover.transform.IsChildOf(rocksRoot)))
            throw new InvalidOperationException("Choisir une racine dediee aux rochers, independante du rover.");
        HashSet<Collider> set = new HashSet<Collider>();
        if (region.tiles != null)
            foreach (Terrain tile in region.tiles)
                if (tile)
                {
                    TerrainCollider c = tile.GetComponent<TerrainCollider>();
                    if (c) set.Add(c);
                }
        if (set.Count == 0) throw new InvalidOperationException("Aucun TerrainCollider dans la region.");
        if (rocksRoot)
            foreach (Collider c in rocksRoot.GetComponentsInChildren<Collider>(true))
                if (c && !c.isTrigger && c.transform != rover.transform && !c.transform.IsChildOf(rover.transform)) set.Add(c);
        Collider[] result = new Collider[set.Count]; set.CopyTo(result);
        return result;
    }
    private void ValidatePath()
    {
        RoverWaypointFollower follower = rover.GetComponent<RoverWaypointFollower>();
        if (!follower) throw new InvalidOperationException("Follower absent sur ce rover.");
        SerializedProperty p = new SerializedObject(follower).FindProperty("path");
        if (!path || p == null || p.objectReferenceValue != path)
            throw new InvalidOperationException("Choisir le parcours deja reference par le follower.");
        if (path.transform == rover.transform || path.transform.IsChildOf(rover.transform))
            throw new InvalidOperationException("Le parcours doit rester independant du rover.");
        string error;
        if (!path.BuildCache(out error)) throw new InvalidOperationException(error);
        for (int i = 0; i < path.PointCount; i++)
            if (!path.Point(i) || path.Point(i).IsChildOf(rover.transform))
                throw new InvalidOperationException("Point absent ou enfant du rover.");
    }
    private void Execute(bool commit)
    {
        previewValid = false; picking = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int undoGroup = -1;
        try
        {
            Collider[] surfaces = CollectSurfaces();
            Transform[] steering = ReadSteering();
            ValidatePath();
            Vector3 oldForward = rover.ReferenceForward;
            Quaternion oldHeading = Quaternion.LookRotation(oldForward, Vector3.up);
            Vector3 oldFirst = path.Point(0).position;
            if (commit)
            {
                if (!EditorUtility.DisplayDialog("Placer le rover",
                    "Rover : " + rover.name + "\nScene : " + rover.gameObject.scene.name +
                    "\nNouveau depart et surfaces.\nLe suivi restera en pause. Continuer ?", "Placer", "Annuler")) return;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Place Rover On New Map");
                Undo.RegisterFullObjectHierarchyUndo(rover.gameObject, "Place Rover On New Map");
                // Points may live outside the path hierarchy.
                if (movePath)
                    for (int i = 0; i < path.PointCount; i++) Undo.RecordObject(path.Point(i), "Move Rover Path");
            }
            Vector3 actualMiddle;
            bool ok = rover.EditorTryRelocate(groundPoint, yaw, surfaces, steering, commit,
                previewWheels, out previewRoot, out previewRotation, out actualMiddle, out status);
            if (!ok)
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                SceneView.RepaintAll(); Repaint(); return;
            }
            previewValid = true;
            if (commit)
            {
                if (movePath)
                {
                    Quaternion delta = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Inverse(oldHeading);
                    for (int i = 0; i < path.PointCount; i++)
                    {
                        Transform point = path.Point(i);
                        Vector3 offset = point.position - oldFirst; offset.y = 0f;
                        Vector3 target = actualMiddle + delta * offset;
                        if (!region.TryGetTerrain(target, out Terrain targetTile) || !targetTile)
                            throw new InvalidOperationException("Un point du parcours replace sort de la region. Changer le cap ou le depart.");
                        if (rover.EditorTrySurfaceAt(target, out RaycastHit hit)) target.y = hit.point.y;
                        point.position = target;
                        EditorUtility.SetDirty(point);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(point);
                    }
                    string error;
                    if (!path.BuildCache(out error)) throw new InvalidOperationException(error);
                }
                else if (RoverWaypointPath.HorizontalDistance(actualMiddle, path.Point(0).position) > 0.50f)
                    throw new InvalidOperationException("Parcours conserve trop loin du nouveau depart. Cocher Replacer le parcours.");
                ConfigureControllers();
                EditorUtility.SetDirty(rover);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rover);
                foreach (Transform t in rover.GetComponentsInChildren<Transform>(true))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                EditorSceneManager.MarkSceneDirty(rover.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
                status += " Depart enregistre ; suivi en pause. Enregistrer la scene puis Play > Resume.";
                Debug.Log(status, rover);
            }
        }
        catch (Exception ex)
        {
            if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
            if (rover) rover.InvalidateRuntime();
            previewValid = false; status = "Operation annulee : " + ex.Message;
            Debug.LogWarning(status);
        }
        SceneView.RepaintAll(); Repaint();
    }
    private void ConfigureControllers()
    {
        foreach (MonoBehaviour component in rover.GetComponents<MonoBehaviour>())
        {
            if (!component) continue;
            string name = component.GetType().Name;
            if (name == "RoverStraightPathDriver" || name == "RoverArcPathDriver" || name == "RoverFlatDriveTest")
            {
                Undo.RecordObject(component, "Disable Legacy Rover Driver");
                component.enabled = false; EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
        RoverWaypointFollower follower = rover.GetComponent<RoverWaypointFollower>();
        RoverKinematicActuator actuator = rover.GetComponent<RoverKinematicActuator>();
        Undo.RecordObject(follower, "Pause Rover Follower");
        Undo.RecordObject(actuator, "Enable Rover Actuator");
        follower.enabled = true; actuator.enabled = true;
        rover.enabled = true;
        SerializedObject so = new SerializedObject(follower);
        SerializedProperty run = so.FindProperty("run");
        if (run == null) throw new InvalidOperationException("Champ run du follower introuvable.");
        run.boolValue = false; so.ApplyModifiedProperties();
        EditorUtility.SetDirty(follower); EditorUtility.SetDirty(actuator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(follower);
        PrefabUtility.RecordPrefabInstancePropertyModifications(actuator);
    }
    private void DrawScene(SceneView scene)
    {
        Event e = Event.current;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (picking && region)
        {
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { picking = false; e.Use(); Repaint(); }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Physics.SyncTransforms();
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float nearest = float.PositiveInfinity; bool found = false;
                if (region.tiles != null)
                    foreach (Terrain tile in region.tiles)
                    {
                        if (!tile) continue;
                        TerrainCollider c = tile.GetComponent<TerrainCollider>();
                        if (!c || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                        if (c.Raycast(ray, out RaycastHit hit, nearest))
                        { nearest = hit.distance; groundPoint = hit.point; found = true; }
                    }
                if (found)
                {
                    hasPoint = true; picking = false; previewValid = false;
                    status = "Point choisi. Regler le cap, puis previsualiser.";
                }
                else status = "Aucun TerrainCollider de cette region sous le clic.";
                e.Use(); Repaint(); scene.Repaint();
            }
        }
        if (!hasPoint || e.type != EventType.Repaint) return;
        Handles.color = Color.cyan;
        Handles.DrawWireDisc(groundPoint, Vector3.up, 0.3f);
        Quaternion heading = Quaternion.AngleAxis(yaw, Vector3.up);
        Handles.ArrowHandleCap(0, groundPoint + Vector3.up * 0.1f, heading, 1.5f, EventType.Repaint);
        Handles.Label(groundPoint + Vector3.up * 0.3f, "Depart : milieu des roues centrales");
        if (!previewValid || !rover) return;
        Handles.color = Color.green;
        for (int i = 0; i < 6; i++)
        {
            Handles.DrawWireDisc(previewWheels[i], heading * Vector3.right, rover.WheelRadius);
            Handles.DrawLine(previewWheels[i], previewWheels[i] - Vector3.up * rover.WheelRadius);
        }
        Handles.ArrowHandleCap(0, previewRoot, previewRotation, 1f, EventType.Repaint);
    }
}
