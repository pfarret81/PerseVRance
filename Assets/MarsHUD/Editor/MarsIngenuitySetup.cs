using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.Editor
{
    /// <summary>
    /// Lot 4 - Installation d'Ingenuity en un clic. Prerequis : lot 3 installe.
    /// Utilise l'instance de scene existante (ou instancie Assets/Prefabs/Ingenuity/Ingenuity_Controller.prefab),
    /// ajoute un objet de mission independant et l'inscrit dans le panneau. Ne modifie ni l'asset prefab,
    /// ni les reglages de IngenuityFlightController / IngenuityRotorController. Annulable (Ctrl+Z).
    /// </summary>
    public static class MarsIngenuitySetup
    {
        const string PrefabPath = "Assets/Prefabs/Ingenuity/Ingenuity_Controller.prefab";
        const string MissionName = "Ingenuity mission (lot 4)";
        const string RotorType = "IngenuityRotorController";
        const string FlightType = "IngenuityFlightController";

        static List<T> FindInLoadedScenes<T>() where T : Component
        {
            var result = new List<T>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return result;
        }

        /// <summary>Instances de scene portant le script de rotors (recherche par nom de type, sans reflexion).</summary>
        static List<Rigidbody> FindIngenuityBodies()
        {
            var list = new List<Rigidbody>();
            foreach (var rb in FindInLoadedScenes<Rigidbody>())
                if (rb.GetComponent(RotorType) || rb.GetComponent(FlightType)) list.Add(rb);
            return list;
        }

        [MenuItem("Mars HUD/Installer Ingenuity - lot 4")]
        static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Quitter le mode Play avant l'installation."); return; }
            var huds = FindInLoadedScenes<MarsHUDController>();
            if (huds.Count != 1) { Debug.LogError($"Installation lot 4 : {huds.Count} MarsHUDController trouves, 1 attendu."); return; }
            MarsHUDController hud = huds[0];
            var panel = hud.GetComponent<MarsMissionPanel>();
            var rovers = FindInLoadedScenes<PerseveranceMission>();
            if (!panel || rovers.Count != 1) { Debug.LogError("Installer d'abord le lot 3 (Mars HUD > Installer les missions Perseverance - lot 3)."); return; }
            PerseveranceMission rover = rovers[0];

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Installer Ingenuity lot 4");
            int group = Undo.GetCurrentGroup();

            Rigidbody body = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<Rigidbody>() : null;
            if (body && !(body.GetComponent(RotorType) || body.GetComponent(FlightType))) body = null;
            if (!body)
            {
                var bodies = FindIngenuityBodies();
                if (bodies.Count > 1) { Debug.LogError($"{bodies.Count} Ingenuity dans la scene : selectionner celui a utiliser puis relancer."); return; }
                if (bodies.Count == 1) body = bodies[0];
            }
            if (!body)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (!prefab)
                {
                    foreach (var guid in AssetDatabase.FindAssets("Ingenuity_Controller t:Prefab"))
                    { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (prefab) break; }
                }
                if (!prefab) { Debug.LogError("Prefab introuvable : " + PrefabPath); return; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rover.follower ? rover.follower.gameObject.scene : hud.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(go, "Instancier Ingenuity");
                if (rover.follower)
                {
                    // position d'edition indicative : la mission repose l'appareil au sol a cote du rover au demarrage
                    Transform r = rover.follower.transform;
                    go.transform.position = r.position + r.right * 4f + Vector3.up * 1f;
                }
                body = go.GetComponent<Rigidbody>();
                if (!body) { Debug.LogError("Le prefab Ingenuity n'a pas de Rigidbody a la racine.", go); return; }
            }

            IngenuityMission mission = null;
            foreach (var m in FindInLoadedScenes<IngenuityMission>()) if (m.body == body) { mission = m; break; }
            if (!mission)
            {
                var go = new GameObject(MissionName);
                Undo.RegisterCreatedObjectUndo(go, "Creer mission Ingenuity");
                SceneManager.MoveGameObjectToScene(go, body.gameObject.scene);
                mission = Undo.AddComponent<IngenuityMission>(go);
            }
            Undo.RecordObject(mission, "Configurer mission Ingenuity");
            mission.body = body;
            mission.rotorController = body.GetComponent(RotorType) as Behaviour;
            mission.manualFlightController = body.GetComponent(FlightType) as Behaviour;
            mission.hud = hud;
            mission.perseverance = rover;
            var ignore = new List<Transform>();
            AddIgnoredRoot(ignore, hud.head ? hud.head.root : null, body, rover);   // XR Origin
            AddIgnoredRoot(ignore, hud.transform.root, body, rover);                 // HUD
            mission.obstacleIgnoreRoots = ignore.ToArray();

            Undo.RecordObject(panel, "Inscrire Ingenuity");
            var vehicles = new List<MonoBehaviour>();
            if (panel.vehicles != null) foreach (var v in panel.vehicles) if (v && v != mission && v is IMarsVehicleMission) vehicles.Add(v);
            if (!vehicles.Contains(rover)) vehicles.Insert(0, rover);
            vehicles.Add(mission);
            panel.vehicles = vehicles.ToArray();

            EditorUtility.SetDirty(mission); EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(body.gameObject.scene);
            if (hud.gameObject.scene != body.gameObject.scene) EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = mission.gameObject;
            Debug.Log($"Lot 4 installe : '{mission.name}' -> appareil '{body.name}' (rotors : {(mission.rotorController ? "OK" : "absent")}, " +
                      $"controleur manuel : {(mission.manualFlightController ? "desactive en Play pendant le pilotage HUD" : "absent")}), " +
                      $"{panel.vehicles.Length} vehicule(s) dans le panneau. Enregistrer la scene (Ctrl+S).", mission);
        }

        /// <summary>N'ignore jamais une racine qui contient le terrain, le rover ou l'appareil.</summary>
        static void AddIgnoredRoot(List<Transform> list, Transform root, Rigidbody body, PerseveranceMission rover)
        {
            if (!root || list.Contains(root)) return;
            if (body.transform.IsChildOf(root)) return;
            if (rover.follower && rover.follower.transform.IsChildOf(root)) return;
            if (root.GetComponentInChildren<TerrainCollider>(true)) return;
            list.Add(root);
        }

        [MenuItem("Mars HUD/Verifier l'installation - lot 4")]
        static void Check()
        {
            var sb = new StringBuilder("Verification lot 4 (aucune modification) :\n");
            bool ok = true;
            var huds = FindInLoadedScenes<MarsHUDController>();
            MarsHUDController hud = huds.Count == 1 ? huds[0] : null;
            ok &= Line(sb, hud, $"MarsHUDController unique ({huds.Count})");
            var panel = hud ? hud.GetComponent<MarsMissionPanel>() : null;
            ok &= Line(sb, panel, "MarsMissionPanel (lot 3) sur le HUD");
            var missions = FindInLoadedScenes<IngenuityMission>();
            ok &= Line(sb, missions.Count == 1, $"IngenuityMission unique ({missions.Count})");
            var m = missions.Count == 1 ? missions[0] : null;
            ok &= Line(sb, m && m.body, "Mission reliee au Rigidbody d'Ingenuity");
            ok &= Line(sb, m && m.perseverance && m.perseverance.follower, "Mission reliee a Perseverance (portee 250 m, depart)");
            ok &= Line(sb, m && m.hud == hud, "Mission reliee au HUD (decouverte)");
            ok &= Line(sb, m && m.body && !m.transform.IsChildOf(m.body.transform), "Mission independante de l'appareil");
            Line(sb, m && m.rotorController, "Script de rotors relie (facultatif)");
            Line(sb, m && m.manualFlightController, "Controleur manuel detecte : il sera desactive pendant le pilotage HUD (facultatif)");
            bool listed = false, rover = false;
            if (panel && panel.vehicles != null)
                foreach (var v in panel.vehicles) { if (m && v == m) listed = true; if (v is PerseveranceMission) rover = true; }
            ok &= Line(sb, listed && rover, "Perseverance et Ingenuity listes dans le panneau");
            ok &= Line(sb, FindIngenuityBodies().Count == 1, "Un seul appareil Ingenuity dans la scene");
            ok &= Line(sb, FindInLoadedScenes<TerrainCollider>().Count > 0 || (m && m.extraGroundRoots != null && m.extraGroundRoots.Length > 0),
                       "Sol detectable (TerrainCollider ou racines de sol declarees)");
            Debug.Log(sb.Append(ok ? "=> OK structurel. Reste a tester en Play / casque." : "=> A corriger avant le test.").ToString());
        }

        static bool Line(StringBuilder sb, bool condition, string label)
        {
            sb.Append(condition ? "  [OK] " : "  [--] ").Append(label).Append('\n');
            return condition;
        }
    }
}
