using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.Editor
{
    /// <summary>
    /// Lot 5 - Outils d'edition (annulables par Ctrl+Z) :
    ///  1. Configurer les engins : remet les pivots de direction du rover dans l'ordre attendu par
    ///     RoverKinematicActuator (avant G, avant D, arriere G, arriere D) d'apres la geometrie de reference
    ///     REELLE des roues, puis relie Ingenuity a son appareil. Passe par SerializedObject (equivalent a une
    ///     saisie dans l'inspecteur), sans reflexion et sans modifier le code des controleurs valides.
    ///  2. Installer le fil rouge : echantillon-type de sol, prelevement a la demande, plan de mission d'exemple.
    ///  3. Ajouter une etape au centre de la vue Scene.
    /// </summary>
    public static class MarsLot5Setup
    {
        const string PlanName = "Mission thread (lot 5)";
        const string ProfilePath = "Assets/MarsHUD/Missions/MarsSoilCompositionProfile.asset";

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

        static bool NotInPlayMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            Debug.LogError("Lot 5 : quitter le mode Play avant d'utiliser cet outil.");
            return false;
        }

        // =================================================================== 1. engins
        [MenuItem("Mars HUD/Lot 5 - Configurer les engins")]
        static void ConfigureVehicles()
        {
            if (!NotInPlayMode()) return;
            var log = new StringBuilder("=== Lot 5 - Configuration des engins ===\n");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lot 5 - Configurer les engins");
            int group = Undo.GetCurrentGroup();
            bool changed = false;

            var actuators = FindInLoadedScenes<RoverKinematicActuator>();
            if (actuators.Count == 0) log.Append("[ERREUR] Aucun RoverKinematicActuator dans les scenes ouvertes.\n");
            foreach (var a in actuators) changed |= RepairSteering(a, log);

            changed |= ConfigureIngenuity(log);

            Undo.CollapseUndoOperations(group);
            if (changed) for (int i = 0; i < SceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
            log.Append(changed ? "Modifications appliquees a la scene (Ctrl+Z pour annuler). Enregistrer la scene.\n"
                               : "Aucune modification necessaire.\n");
            Debug.Log(log.ToString());
        }

        static bool RepairSteering(RoverKinematicActuator a, StringBuilder log)
        {
            log.Append("-- Rover '").Append(a.name).Append("'\n");
            var solver = a.Solver;
            if (!solver) { log.Append("[ERREUR] Champ Solver vide sur RoverKinematicActuator.\n"); return false; }
            var so = new SerializedObject(a);
            var prop = so.FindProperty("steeringPivots");
            if (prop == null || !prop.isArray || prop.arraySize != 4) { log.Append("[ERREUR] steeringPivots doit contenir 4 elements.\n"); return false; }
            var pivots = new Transform[4];
            for (int j = 0; j < 4; j++) pivots[j] = prop.GetArrayElementAtIndex(j).objectReferenceValue as Transform;

            var geometry = new Vector3[6];
            if (!solver.TryGetReferenceWheelGeometry(geometry))
            { log.Append("[ERREUR] Geometrie de reference indisponible : capturer la reference du RoverStaticPoseTest (configuration validee).\n"); return false; }

            // Association pivot -> roue, exactement comme RoverKinematicActuator.Initialize.
            var wheelOf = new int[4];
            var used = new bool[6];
            for (int j = 0; j < 4; j++)
            {
                if (!pivots[j] || !pivots[j].IsChildOf(a.transform) || pivots[j] == a.transform)
                { log.Append($"[ERREUR] Pivot {j} vide ou hors du rover.\n"); return false; }
                int index = -1;
                for (int i = 0; i < 6; i++)
                {
                    var w = solver.WheelCenter(i);
                    if (w && w.IsChildOf(pivots[j])) { if (index != -1) { log.Append($"[ERREUR] Plusieurs roues sous {pivots[j].name}.\n"); return false; } index = i; }
                }
                if (index < 0 || used[index]) { log.Append($"[ERREUR] Pivot {pivots[j].name} : aucune roue (ou roue deja prise).\n"); return false; }
                used[index] = true; wheelOf[j] = index;
                float alignment = Vector3.Dot(pivots[j].TransformDirection(Vector3.forward), a.transform.up);
                if (Mathf.Abs(alignment) < .5f) log.Append($"[ATTENTION] {pivots[j].name} : axe Z non vertical ({alignment:F2}) - refuse par l'actionneur.\n");
            }
            int ma = -1, mb = -1;
            for (int i = 0; i < 6; i++) if (!used[i]) { if (ma < 0) ma = i; else if (mb < 0) mb = i; }
            if (ma < 0 || mb < 0) { log.Append("[ERREUR] Deux roues centrales attendues.\n"); return false; }
            Vector3 middle = (geometry[ma] + geometry[mb]) * .5f;

            // Classement d'apres les positions mesurees (repere local de RoverRoot), pas d'apres les noms.
            var sorted = new Transform[4];
            for (int j = 0; j < 4; j++)
            {
                Vector3 p = geometry[wheelOf[j]];
                if (Mathf.Abs(p.z - middle.z) < 1e-4f || Mathf.Abs(p.x - middle.x) < 1e-4f)
                { log.Append($"[ERREUR] {pivots[j].name} est aligne sur l'essieu central : classement impossible.\n"); return false; }
                int slot = (p.z > middle.z ? 0 : 2) + (p.x < middle.x ? 0 : 1);
                if (sorted[slot]) { log.Append($"[ERREUR] {pivots[j].name} et {sorted[slot].name} occupent la meme position ({SlotName(slot)}).\n"); return false; }
                sorted[slot] = pivots[j];
                log.Append($"   {pivots[j].name,-14} roue {solver.WheelCenter(wheelOf[j]).name,-12} x={p.x - middle.x,6:F2} z={p.z - middle.z,6:F2} -> {SlotName(slot)}\n");
            }
            ReportMast(a.transform, middle, log);

            bool same = true;
            for (int j = 0; j < 4; j++) same &= sorted[j] == pivots[j];
            if (same) { log.Append("   Ordre des pivots deja correct.\n"); return false; }
            Undo.RecordObject(a, "Ordre des pivots");
            for (int j = 0; j < 4; j++) prop.GetArrayElementAtIndex(j).objectReferenceValue = sorted[j];
            so.ApplyModifiedProperties();
            log.Append("[CORRIGE] steeringPivots = ")
               .Append(sorted[0].name).Append(", ").Append(sorted[1].name).Append(", ")
               .Append(sorted[2].name).Append(", ").Append(sorted[3].name).Append(" (avant G, avant D, arriere G, arriere D).\n");
            return true;
        }

        static string SlotName(int slot) => slot == 0 ? "avant G" : slot == 1 ? "avant D" : slot == 2 ? "arriere G" : "arriere D";

        /// <summary>Information seulement : sur Perseverance, le mat est a l'avant. Rien n'est modifie.</summary>
        static void ReportMast(Transform rover, Vector3 middle, StringBuilder log)
        {
            Transform head = null;
            foreach (var t in rover.GetComponentsInChildren<Transform>(true)) if (t.name == "head") { head = t; break; }
            if (!head) return;
            Vector3 local = rover.InverseTransformPoint(head.position) - middle;
            log.Append(local.z >= 0f
                ? "   Mat (head) a l'avant du sens de marche (+Z de RoverRoot) : coherent.\n"
                : "[ATTENTION] Mat (head) a l'ARRIERE du sens de marche (+Z de RoverRoot) : le rover roulera mat en arriere. Le visuel n'est pas modifie ; me le signaler si ce n'est pas voulu.\n");
        }

        static bool ConfigureIngenuity(StringBuilder log)
        {
            var missions = FindInLoadedScenes<IngenuityMission>();
            if (missions.Count == 0) { log.Append("-- Ingenuity : aucune IngenuityMission (lancer d'abord Mars HUD > Installer Ingenuity - lot 4).\n"); return false; }
            bool changed = false;
            foreach (var m in missions)
            {
                log.Append("-- Ingenuity '").Append(m.name).Append("'\n");
                if (!m.body)
                {
                    var bodies = new List<Rigidbody>();
                    foreach (var rb in FindInLoadedScenes<Rigidbody>())
                        if (rb.GetComponent("IngenuityRotorController") || rb.GetComponent("IngenuityFlightController")) bodies.Add(rb);
                    if (bodies.Count != 1) { log.Append($"[ERREUR] Body vide et {bodies.Count} appareil(s) Ingenuity dans la scene : assigner Body a la main.\n"); continue; }
                    Undo.RecordObject(m, "Ingenuity body");
                    m.body = bodies[0]; changed = true;
                    log.Append("[CORRIGE] Body = ").Append(bodies[0].name).Append('\n');
                }
                if (!m.rotorController) { var b = m.body.GetComponent("IngenuityRotorController") as Behaviour; if (b) { Undo.RecordObject(m, "Ingenuity rotors"); m.rotorController = b; changed = true; log.Append("[CORRIGE] Rotor Controller relie.\n"); } }
                if (!m.manualFlightController) { var b = m.body.GetComponent("IngenuityFlightController") as Behaviour; if (b) { Undo.RecordObject(m, "Ingenuity vol"); m.manualFlightController = b; changed = true; log.Append("[CORRIGE] Manual Flight Controller relie (desactive au Play par la mission).\n"); } }
                if (!m.hud) { var h = FindInLoadedScenes<MarsHUDController>(); if (h.Count == 1) { Undo.RecordObject(m, "Ingenuity HUD"); m.hud = h[0]; changed = true; } }
                if (!m.perseverance) { var p = FindInLoadedScenes<PerseveranceMission>(); if (p.Count == 1) { Undo.RecordObject(m, "Ingenuity rover"); m.perseverance = p[0]; changed = true; } }
                if (m.placementWaitSeconds <= 0f) { Undo.RecordObject(m, "Ingenuity delai"); m.placementWaitSeconds = 3f; changed = true; }
                if (!m.enabled || !m.gameObject.activeInHierarchy) log.Append("[ATTENTION] IngenuityMission desactivee.\n");
                if (m.body && !m.body.gameObject.activeInHierarchy) log.Append("[ATTENTION] L'appareil Ingenuity est desactive dans la hierarchie.\n");
                foreach (var panel in FindInLoadedScenes<MarsMissionPanel>())
                {
                    bool listed = false;
                    foreach (var v in panel.vehicles) listed |= v == m;
                    if (listed) continue;
                    Undo.RecordObject(panel, "Inscrire Ingenuity");
                    var arr = new MonoBehaviour[panel.vehicles.Length + 1];
                    panel.vehicles.CopyTo(arr, 0); arr[arr.Length - 1] = m; panel.vehicles = arr; changed = true;
                    log.Append("[CORRIGE] Ingenuity inscrit dans le panneau de mission.\n");
                }
                log.Append(m.body ? "   Ingenuity relie : " + m.body.name + "\n" : "");
            }
            return changed;
        }

        // =================================================================== 2. fil rouge
        [MenuItem("Mars HUD/Lot 5 - Installer le fil rouge et l'echantillon-type")]
        static void InstallMissionThread()
        {
            if (!NotInPlayMode()) return;
            var huds = FindInLoadedScenes<MarsHUDController>();
            var rovers = FindInLoadedScenes<PerseveranceMission>();
            if (huds.Count != 1 || rovers.Count != 1) { Debug.LogError($"Lot 5 : {huds.Count} HUD et {rovers.Count} PerseveranceMission trouves (1 et 1 attendus). Installer d'abord les lots 3 et 4."); return; }
            var hud = huds[0]; var rover = rovers[0];
            var sampler = hud.GetComponent<MarsSoilSampler>();
            if (!sampler) { Debug.LogError("Lot 5 : MarsSoilSampler absent du HUD (installer le lot 3)."); return; }
            var log = new StringBuilder("=== Lot 5 - Fil rouge et echantillon-type ===\n");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lot 5 - Fil rouge");
            int group = Undo.GetCurrentGroup();

            // Echantillon-type (asset existant mis a jour, ou cree)
            var profile = sampler.profile ? sampler.profile : AssetDatabase.LoadAssetAtPath<MarsSoilCompositionProfile>(ProfilePath);
            if (!profile)
            {
                profile = MarsSoilCompositionProfile.CreateDefault();
                if (!AssetDatabase.IsValidFolder("Assets/MarsHUD/Missions")) AssetDatabase.CreateFolder("Assets/MarsHUD", "Missions");
                AssetDatabase.CreateAsset(profile, ProfilePath);
                log.Append("Profil cree : ").Append(ProfilePath).Append('\n');
            }
            else
            {
                Undo.RecordObject(profile, "Echantillon-type");
                profile.components = MarsSoilCompositionProfile.AverageBasalticSoil();
                profile.provenance = "SIMULATION - échantillon-type : sol basaltique moyen martien (APXS, O'Connell-Cooper et al. 2017)";
                EditorUtility.SetDirty(profile);
                log.Append("Echantillon-type applique au profil existant (13 composes).\n");
            }
            if (sampler.profile != profile) { Undo.RecordObject(sampler, "Profil"); sampler.profile = profile; }
            AssetDatabase.SaveAssets();

            Undo.RecordObject(rover, "Prelevement a la demande");
            rover.autoSampleOnArrival = false;
            log.Append("Perseverance : prelevement a la demande (bouton Prelever).\n");

            var plans = FindInLoadedScenes<MarsMissionPlan>();
            MarsMissionPlan plan;
            if (plans.Count > 0) { plan = plans[0]; log.Append("Plan existant conserve : ").Append(plan.name).Append('\n'); }
            else
            {
                var go = new GameObject(PlanName);
                Undo.RegisterCreatedObjectUndo(go, "Plan de mission");
                SceneManager.MoveGameObjectToScene(go, hud.gameObject.scene);
                plan = go.AddComponent<MarsMissionPlan>();
                plan.hud = hud; plan.sampler = sampler; plan.perseverance = rover;
                CreateExampleSteps(plan, rover, log);
            }
            var note = plan.GetComponent<MarsMissionNote>();
            if (!note) { note = Undo.AddComponent<MarsMissionNote>(plan.gameObject); if (!note) note = plan.GetComponent<MarsMissionNote>(); }
            if (note) { note.plan = plan; note.hud = hud; }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            log.Append("Etapes = enfants de '").Append(plan.name).Append("' (ordre de la hierarchie, dernier = arrivee). Les deplacer dans la vue Scene.\n");
            log.Append("Verifier que chaque etape est dans l'emprise des cartes. Enregistrer la scene.\n");
            Debug.Log(log.ToString(), plan);
            Selection.activeGameObject = plan.gameObject;
        }

        static void CreateExampleSteps(MarsMissionPlan plan, PerseveranceMission rover, StringBuilder log)
        {
            Transform root = rover.follower ? rover.follower.transform : rover.transform;
            Vector3 start = root.position;
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            string[] names = { "Bord du delta", "Affleurement clair", "Dépôt sombre", "Point d'observation" };
            float[] ahead = { 20f, 45f, 70f, 95f }, side = { 0f, 12f, -10f, 5f };
            for (int i = 0; i < names.Length; i++)
            {
                Vector3 p = Ground(start + fwd * ahead[i] + right * side[i], root);
                AddStep(plan, names[i], p, -1);
                log.Append($"   Etape {i + 1} '{names[i]}' a {ahead[i]:F0} m devant le rover\n");
            }
            log.Append("   Exemple : les etapes 3 et arrivee sont au-dela de la zone connue initiale -> reconnaissance par Ingenuity.\n");
        }

        static MarsMissionCheckpoint AddStep(MarsMissionPlan plan, string name, Vector3 position, int siblingIndex)
        {
            var go = new GameObject("Etape - " + name);
            Undo.RegisterCreatedObjectUndo(go, "Etape");
            go.transform.SetParent(plan.transform, false);
            go.transform.position = position;
            if (siblingIndex >= 0) go.transform.SetSiblingIndex(siblingIndex);
            var cp = go.AddComponent<MarsMissionCheckpoint>();
            cp.displayName = name;
            cp.requirement = MarsMissionCheckpoint.Requirement.PerseveranceSample;
            cp.radiusMeters = 8f;
            return cp;
        }

        /// <summary>Hauteur du sol : Terrain actif si present, sinon premier collider sous le point hors rover.</summary>
        static Vector3 Ground(Vector3 p, Transform ignore)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain) { p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p; }
            var hits = Physics.RaycastAll(p + Vector3.up * 500f, Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits) if (!(ignore && h.transform.IsChildOf(ignore)) && h.point.y > best) best = h.point.y;
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        // =================================================================== 3. ajout d'etape
        [MenuItem("Mars HUD/Lot 5 - Ajouter une etape au centre de la vue Scene")]
        static void AddStepAtSceneView()
        {
            if (!NotInPlayMode()) return;
            var plans = FindInLoadedScenes<MarsMissionPlan>();
            if (plans.Count != 1) { Debug.LogError("Lot 5 : installer d'abord le fil rouge (un seul MarsMissionPlan attendu)."); return; }
            var view = SceneView.lastActiveSceneView;
            if (view == null) { Debug.LogError("Lot 5 : ouvrir une vue Scene."); return; }
            var plan = plans[0];
            int count = plan.GetComponentsInChildren<MarsMissionCheckpoint>(true).Length;
            Vector3 p = Ground(view.pivot, plan.perseverance && plan.perseverance.follower ? plan.perseverance.follower.transform : null);
            int index = count > 0 ? count - 1 : -1;   // insere avant l'arrivee
            var cp = AddStep(plan, "Nouvelle étape " + (count), p, index);
            EditorSceneManager.MarkSceneDirty(plan.gameObject.scene);
            Selection.activeGameObject = cp.gameObject;
            Debug.Log("Lot 5 : etape ajoutee avant l'arrivee. Renommer Display Name et ajuster le rayon dans l'inspecteur.", cp);
        }
    }
}
