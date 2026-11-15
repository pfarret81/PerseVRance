using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.Editor
{
    /// <summary>
    /// Lot 3 - Installation en un clic : ajoute les composants de mission et les relie aux systemes EXISTANTS.
    /// Ne modifie ni le rover, ni le follower, ni le XR Origin, ni les terrains. Annulable (Ctrl+Z).
    /// </summary>
    public static class MarsMissionSetup
    {
        const string ProfileFolder = "Assets/MarsHUD/Missions";
        const string ProfilePath = ProfileFolder + "/MarsSoilCompositionProfile.asset";
        const string MissionName = "Perseverance mission (lot 3)";

        static List<T> FindInLoadedScenes<T>() where T : Component
        {
            var result = new List<T>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return result;
        }

        [MenuItem("Mars HUD/Installer les missions Perseverance - lot 3")]
        static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Quitter le mode Play avant l'installation."); return; }
            var huds = FindInLoadedScenes<MarsHUDController>();
            if (huds.Count != 1) { Debug.LogError($"Installation lot 3 : {huds.Count} MarsHUDController trouves, 1 attendu."); return; }
            MarsHUDController hud = huds[0];
            if (!hud.catalog) { Debug.LogError("Le HUD n'a pas de catalogue : executer d'abord la configuration Jezero (lot 2).", hud); return; }

            RoverWaypointFollower follower = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoverWaypointFollower>() : null;
            if (!follower)
            {
                var followers = FindInLoadedScenes<RoverWaypointFollower>();
                if (followers.Count != 1) { Debug.LogError($"Installation lot 3 : {followers.Count} RoverWaypointFollower trouves. Selectionner le rover puis relancer."); return; }
                follower = followers[0];
            }
            if (!follower.Actuator || !follower.Actuator.Solver) { Debug.LogError("Le follower n'a pas d'actionneur/solveur assigne.", follower); return; }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Installer missions lot 3");
            int group = Undo.GetCurrentGroup();

            // Profil de composition simule (asset editable).
            var profile = AssetDatabase.LoadAssetAtPath<MarsSoilCompositionProfile>(ProfilePath);
            if (!profile)
            {
                if (!AssetDatabase.IsValidFolder(ProfileFolder)) AssetDatabase.CreateFolder("Assets/MarsHUD", "Missions");
                profile = MarsSoilCompositionProfile.CreateDefault();
                profile.name = "MarsSoilCompositionProfile";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var sampler = hud.GetComponent<MarsSoilSampler>();
            if (!sampler) sampler = Undo.AddComponent<MarsSoilSampler>(hud.gameObject);
            Undo.RecordObject(sampler, "Configurer prelevements");
            sampler.hud = hud;
            if (!sampler.profile) sampler.profile = profile;

            var panel = hud.GetComponent<MarsMissionPanel>();
            if (!panel) panel = Undo.AddComponent<MarsMissionPanel>(hud.gameObject);

            PerseveranceMission mission = null;
            foreach (var m in FindInLoadedScenes<PerseveranceMission>()) if (m.follower == follower) { mission = m; break; }
            if (!mission)
            {
                var go = new GameObject(MissionName);
                Undo.RegisterCreatedObjectUndo(go, "Creer mission Perseverance");
                SceneManager.MoveGameObjectToScene(go, follower.gameObject.scene);
                mission = Undo.AddComponent<PerseveranceMission>(go);
            }
            Undo.RecordObject(mission, "Configurer mission");
            mission.follower = follower;
            mission.hud = hud;
            mission.sampler = sampler;
            var ignore = new List<Transform>();
            if (hud.head && hud.head.root != follower.transform.root) ignore.Add(hud.head.root);   // XR Origin
            if (hud.transform.root != follower.transform.root) ignore.Add(hud.transform.root);
            mission.obstacleIgnoreRoots = ignore.ToArray();

            Undo.RecordObject(panel, "Configurer panneau");
            panel.hud = hud;
            panel.sampler = sampler;
            var vehicles = new List<MonoBehaviour>();
            if (panel.vehicles != null) foreach (var v in panel.vehicles) if (v && v != mission && v is IMarsVehicleMission) vehicles.Add(v);
            vehicles.Insert(0, mission);
            panel.vehicles = vehicles.ToArray();

            // Couche SOL : legende de l'indice d'ecart simule (meme teinte que la poussiere).
            Undo.RecordObject(hud.catalog, "Couche sol lot 3");
            int soilLayers = 0;
            if (hud.catalog.layers != null)
                foreach (var layer in hud.catalog.layers)
                {
                    if (layer == null || layer.theme != MarsMapTheme.Soil) continue;
                    soilLayers++;
                    layer.displayName = "Composition du sol - prélèvements";
                    layer.units = "Indice d'écart local 0-1 (simulé)";
                    layer.legendLow = "0 : proche du contexte";
                    layer.legendHigh = "1 : écart marqué";
                    layer.legendMode = MarsLegendMode.HueBlueToRed;
                    layer.provenance = "SIMULATION - prélèvements Perseverance";
                    layer.interpretation = "Révélé uniquement par les prélèvements. Valeurs simulées, non mesurées.";
                    layer.colorTexture = null;
                }
            EditorUtility.SetDirty(hud.catalog);
            EditorUtility.SetDirty(sampler); EditorUtility.SetDirty(panel); EditorUtility.SetDirty(mission);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            if (follower.gameObject.scene != hud.gameObject.scene) EditorSceneManager.MarkSceneDirty(follower.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = mission.gameObject;
            Debug.Log($"Lot 3 installe : mission '{mission.name}' -> follower '{follower.name}', HUD '{hud.name}', " +
                      $"{soilLayers} couche(s) sol mise(s) a jour, profil '{ProfilePath}'. Enregistrer la scene (Ctrl+S).", mission);
        }

        [MenuItem("Mars HUD/Verifier l'installation - lot 3")]
        static void Check()
        {
            var sb = new StringBuilder("Verification lot 3 (aucune modification) :\n");
            bool ok = true;
            var huds = FindInLoadedScenes<MarsHUDController>();
            ok &= Line(sb, huds.Count == 1, $"MarsHUDController unique ({huds.Count})");
            MarsHUDController hud = huds.Count == 1 ? huds[0] : null;
            ok &= Line(sb, hud && hud.GetComponent<MarsSoilSampler>(), "MarsSoilSampler sur le HUD");
            var panel = hud ? hud.GetComponent<MarsMissionPanel>() : null;
            ok &= Line(sb, panel, "MarsMissionPanel sur le HUD");
            var missions = FindInLoadedScenes<PerseveranceMission>();
            ok &= Line(sb, missions.Count == 1, $"PerseveranceMission unique ({missions.Count})");
            var mission = missions.Count == 1 ? missions[0] : null;
            ok &= Line(sb, mission && mission.follower && mission.follower.Actuator && mission.follower.Actuator.Solver, "Mission reliee au follower, a l'actionneur et au solveur");
            ok &= Line(sb, mission && mission.transform.root != (mission.follower ? mission.follower.transform.root : null), "Mission independante du rover");
            bool listed = false;
            if (panel && panel.vehicles != null) foreach (var v in panel.vehicles) if (v == mission && mission) listed = true;
            ok &= Line(sb, listed, "Mission listee dans le panneau");
            ok &= Line(sb, FindInLoadedScenes<UnityEngine.EventSystems.EventSystem>().Count == 1, "EventSystem unique (XR UI Input Module attendu)");
            Debug.Log(sb.Append(ok ? "=> OK structurel. Reste a tester en Play / casque." : "=> A corriger avant le test.").ToString());
        }

        static bool Line(StringBuilder sb, bool condition, string label)
        {
            sb.Append(condition ? "  [OK] " : "  [KO] ").Append(label).Append('\n');
            return condition;
        }
    }
}
