using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.EditorTools
{
    /// <summary>
    /// Lot 6 - Outils d'installation :
    ///  1. scene de mission : planificateur permissif, fermeture du menu des cartes a distance, menu en jeu, musique ;
    ///  2. scene du menu principal : globe, catalogue, quick menu, menu Options/Quitter, lumiere, Build Settings ;
    ///  3. (optionnel, explicite) elargir le domaine angulaire du solveur de pose.
    /// Aucune reflexion : composants publics, SerializedObject pour les champs [SerializeField] du solveur.
    /// </summary>
    public static class MarsLot6Setup
    {
        const string CatalogFolder = "Assets/MarsHUD/Data";
        const string CatalogPath = CatalogFolder + "/PerseVRance Missions.asset";
        const string MenuScenePref = "PerseVRance.MenuSceneName";

        // ------------------------------------------------------------------ 1. scene de mission
        [MenuItem("Mars HUD/Lot 6 - Installer dans la scene de mission")]
        public static void InstallMission()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            { EditorUtility.DisplayDialog("Lot 6", "Enregistrez d'abord la scene de mission.", "OK"); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lot 6 - scene de mission");
            int group = Undo.GetCurrentGroup();
            var report = new List<string>();

            foreach (var m in FindAll<PerseveranceMission>(true))
            {
                Undo.RecordObject(m, "Planificateur permissif");
                m.plannerProfile = PerseveranceMission.PlannerProfile.Permissive;
                EditorUtility.SetDirty(m);
                report.Add($"Planificateur de {m.name} : profil Permissive (valeurs du lot 3 conservees dans 'planner').");
            }

            var hud = Object.FindAnyObjectByType<MarsHUDController>(FindObjectsInactive.Include);
            if (hud)
            {
                if (!hud.GetComponent<MarsMenuDistanceGuard>())
                {
                    var g = Undo.AddComponent<MarsMenuDistanceGuard>(hud.gameObject);
                    g.hud = hud;
                }
                report.Add("Fermeture automatique du menu des cartes : MarsMenuDistanceGuard sur " + hud.name + " (2,5 m).");
            }
            else report.Add("ATTENTION : MarsHUDController introuvable - fermeture a distance non installee.");

            var pause = Object.FindAnyObjectByType<MarsPauseMenu>(FindObjectsInactive.Include);
            if (!pause)
            {
                var go = new GameObject("PerseVRance Pause Menu");
                Undo.RegisterCreatedObjectUndo(go, "Menu en jeu");
                pause = go.AddComponent<MarsPauseMenu>();
            }
            Undo.RecordObject(pause, "Menu en jeu");
            pause.isMainMenu = false;
            pause.hud = hud;
            pause.mainMenuScene = EditorPrefs.GetString(MenuScenePref, pause.mainMenuScene);
            EditorUtility.SetDirty(pause);
            report.Add($"Menu en jeu : bouton Menu de la manette gauche (Echap dans l'editeur) ; retour vers \u00ab {pause.mainMenuScene} \u00bb.");

            if (!Object.FindAnyObjectByType<MarsMusicPlayer>(FindObjectsInactive.Include))
            {
                var go = new GameObject("PerseVRance Music");
                Undo.RegisterCreatedObjectUndo(go, "Musique");
                go.AddComponent<MarsMusicPlayer>();
                report.Add("Lecteur de musique ajoute (vide : glisser un AudioClip dans 'playlist').");
            }

            AddToBuild(scene.path, -1);
            var catalog = LoadOrCreateCatalog(scene.name);
            var jezero = catalog.missions.FirstOrDefault(x => x.id == "jezero");
            if (jezero != null) { jezero.sceneName = scene.name; jezero.available = true; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            report.Add($"Scene ajoutee aux Build Settings ; mission Jezero du catalogue reliee a \u00ab {scene.name} \u00bb.");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            string text = string.Join("\n", report);
            Debug.Log("[Lot 6] " + text);
            EditorUtility.DisplayDialog("Lot 6 - scene de mission", text + "\n\nEnregistrez la scene (Ctrl+S).", "OK");
        }

        // ------------------------------------------------------------------ 2. menu principal
        [MenuItem("Mars HUD/Lot 6 - Construire le menu principal (scene ouverte)")]
        public static void BuildMainMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            { EditorUtility.DisplayDialog("Lot 6", "Enregistrez d'abord la nouvelle scene du menu (ex. Assets/Scenes/PerseVRance_Menu.unity).", "OK"); return; }
            Camera cam = Camera.main;
            if (!cam)
            {
                EditorUtility.DisplayDialog("Lot 6", "Aucune camera MainCamera dans la scene.\nCopiez-collez le XR Origin (et l'EventSystem) " +
                    "depuis la scene de mission, puis relancez l'outil.", "OK");
                return;
            }
            Transform origin = cam.transform;
            while (origin.parent) origin = origin.parent;
            bool hasXrUi = FindAll<UnityEngine.EventSystems.BaseInputModule>(true)
                .Any(m => m.GetType().Name == "XRUIInputModule");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lot 6 - menu principal");
            int group = Undo.GetCurrentGroup();
            var report = new List<string>();

            GameObject selected = Selection.activeGameObject;
            var menu = Object.FindAnyObjectByType<MarsGlobeMenu>(FindObjectsInactive.Include);
            if (!menu)
            {
                var go = new GameObject("PerseVRance Menu");
                Undo.RegisterCreatedObjectUndo(go, "Menu principal");
                Vector3 f = origin.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                go.transform.SetPositionAndRotation(origin.position + Vector3.up * 1.55f + f * 3.2f, Quaternion.LookRotation(f, Vector3.up));
                menu = go.AddComponent<MarsGlobeMenu>();
                report.Add("Menu cree a 3,2 m devant le XR Origin, a hauteur des yeux (1,55 m).");
            }
            Undo.RecordObject(menu, "Menu principal");
            if (!menu.pivot)
            {
                var p = new GameObject("Globe pivot");
                Undo.RegisterCreatedObjectUndo(p, "Pivot");
                p.transform.SetParent(menu.transform, false);
                menu.pivot = p.transform;
            }

            // Modele de Mars : l'objet selectionne (hors XR Origin), sinon celui deja assigne, sinon une sphere provisoire.
            Transform model = menu.globeModel;
            if (selected && selected.GetComponentInChildren<Renderer>() && !selected.transform.IsChildOf(origin) && selected != menu.gameObject
                && !selected.transform.IsChildOf(menu.transform))
                model = selected.transform;
            if (!model)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "Mars (provisoire - remplacer par votre modele)";
                Object.DestroyImmediate(s.GetComponent<Collider>());
                Undo.RegisterCreatedObjectUndo(s, "Sphere provisoire");
                model = s.transform;
                report.Add("ATTENTION : aucun modele selectionne - sphere provisoire. Selectionnez votre modele de Mars et relancez.");
            }
            if (model.parent != menu.pivot)
            {
                Undo.SetTransformParent(model, menu.pivot, "Modele de Mars");
                FitModel(model, menu.pivot, menu.radius);
                report.Add($"Modele \u00ab {model.name} \u00bb place sous le pivot et mis a l'echelle (rayon {menu.radius:F2} m).");
            }
            foreach (var c in model.GetComponentsInChildren<Collider>()) Undo.DestroyObjectImmediate(c);   // l'UI gere le glisser
            menu.globeModel = model;

            menu.catalog = LoadOrCreateCatalog(null);
            menu.head = cam.transform;
            var left = FindLeftHand(origin);
            if (left) { menu.leftHand = left; report.Add("Quick menu : rattache a \u00ab " + left.name + " \u00bb."); }
            else report.Add("ATTENTION : manette gauche introuvable - renseigner MarsGlobeMenu.leftHand.");

            var pause = Object.FindAnyObjectByType<MarsPauseMenu>(FindObjectsInactive.Include);
            if (!pause)
            {
                var go = new GameObject("PerseVRance Options");
                Undo.RegisterCreatedObjectUndo(go, "Options");
                pause = go.AddComponent<MarsPauseMenu>();
            }
            Undo.RecordObject(pause, "Options");
            pause.isMainMenu = true; pause.pauseTime = false; pause.head = cam.transform; pause.mainMenuScene = scene.name;
            menu.pauseMenu = pause;
            EditorUtility.SetDirty(pause);

            if (!Object.FindAnyObjectByType<MarsMusicPlayer>(FindObjectsInactive.Include))
            {
                var go = new GameObject("PerseVRance Music");
                Undo.RegisterCreatedObjectUndo(go, "Musique");
                go.AddComponent<MarsMusicPlayer>();
            }
            if (!FindAll<Light>(true).Any(l => l.type == LightType.Directional))
            {
                var go = new GameObject("Soleil");
                Undo.RegisterCreatedObjectUndo(go, "Soleil");
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.3f; l.color = new Color(1f, .96f, .9f);
                go.transform.rotation = Quaternion.LookRotation(menu.transform.forward + menu.transform.right * .9f - Vector3.up * .25f);
                report.Add("Lumiere directionnelle ajoutee (eclairage rasant pour le relief).");
            }
            EditorUtility.SetDirty(menu);

            AddToBuild(scene.path, 0);
            EditorPrefs.SetString(MenuScenePref, scene.name);
            report.Add($"Scene \u00ab {scene.name} \u00bb placee en premier dans les Build Settings (scene de demarrage).");
            if (!hasXrUi) report.Add("ATTENTION : aucun EventSystem avec XRUIInputModule - copiez celui de la scene de mission.");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = menu.gameObject;
            string text = string.Join("\n", report);
            Debug.Log("[Lot 6] " + text);
            EditorUtility.DisplayDialog("Lot 6 - menu principal", text + "\n\nCalibrez le modele (LISEZMOI-LOT6, section Modele de Mars) puis enregistrez.", "OK");
        }

        // ------------------------------------------------------------------ 3. solveur (optionnel)
        [MenuItem("Mars HUD/Lot 6 - Option : elargir le domaine angulaire du solveur (15 -> 22 deg)")]
        public static void WidenSolver()
        {
            var solvers = FindAll<RoverStaticPoseTest>(true);
            if (solvers.Count == 0) { EditorUtility.DisplayDialog("Lot 6", "Aucun RoverStaticPoseTest dans la scene.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Lot 6 - domaine du solveur",
                    "Le solveur de pose (non remplace) refuse toute pose dont le tangage, le roulis ou un angle d'articulation " +
                    "rocker-bogie depasse maxPoseAngle (15 deg). C'est la limite la plus stricte du planificateur.\n\n" +
                    "Passer a 22 deg autorise des pentes et des debattements plus forts. Modification annulable (Ctrl+Z).",
                    "Passer a 22 deg", "Annuler")) return;
            foreach (var s in solvers)
            {
                var so = new SerializedObject(s);
                var p = so.FindProperty("maxPoseAngle");
                if (p == null) continue;
                p.floatValue = Mathf.Max(p.floatValue, 22f);
                so.ApplyModifiedProperties();
                Debug.Log($"[Lot 6] {s.name} : maxPoseAngle = {p.floatValue:F0} deg.", s);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // ------------------------------------------------------------------ 4. Read/Write des modeles
        [MenuItem("Mars HUD/Outils - Activer Read-Write des modeles (selection ou Rock_Small)")]
        public static void EnableReadWrite()
        {
            var paths = new List<string>();
            foreach (var o in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && !paths.Contains(p)) paths.Add(p);
            }
            if (paths.Count == 0)
            {
                // Par nom de fichier, puis par nom de maillage (le message de MarsSmallRockCells cite le maillage).
                foreach (var guid in AssetDatabase.FindAssets("Rock_Small t:Model"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!paths.Contains(p)) paths.Add(p);
                }
                foreach (var guid in AssetDatabase.FindAssets("t:Model"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (paths.Contains(p) || !p.StartsWith("Assets/")) continue;
                    foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(p))
                        if (sub is Mesh mesh && mesh.name.StartsWith("Rock_Small")) { paths.Add(p); break; }
                }
                // Le composant de la scene ouverte peut aussi pointer un maillage d'un autre nom : on suit ses MeshFilter.
                foreach (var mono in FindAll<MonoBehaviour>(true))
                {
                    if (!mono || mono.GetType().Name != "MarsSmallRockCells") continue;
                    var so = new SerializedObject(mono);
                    var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                        Object o = it.objectReferenceValue;
                        Mesh m = o as Mesh;
                        if (!m && o is GameObject g && g.GetComponentInChildren<MeshFilter>(true)) m = g.GetComponentInChildren<MeshFilter>(true).sharedMesh;
                        if (!m && o is MeshFilter mf) m = mf.sharedMesh;
                        string p = m ? AssetDatabase.GetAssetPath(m) : "";
                        if (!string.IsNullOrEmpty(p) && !paths.Contains(p)) paths.Add(p);
                    }
                }
            }
            int changed = 0;
            foreach (var p in paths)
            {
                if (!(AssetImporter.GetAtPath(p) is ModelImporter mi))
                { Debug.LogWarning("[Lot 6] Pas un modele importe (FBX/OBJ) : " + p + " - cocher Read/Write a la main si c'est un maillage .asset."); continue; }
                if (mi.isReadable) { Debug.Log("[Lot 6] Deja lisible : " + p); continue; }
                mi.isReadable = true;
                mi.SaveAndReimport();
                changed++;
                Debug.Log("[Lot 6] Read/Write active : " + p);
            }
            EditorUtility.DisplayDialog("Read/Write", changed > 0 ? $"{changed} modele(s) reimporte(s) avec Read/Write active."
                : "Aucun modele a modifier (selectionnez les FBX dans la fenetre Project, ou ils sont deja lisibles).", "OK");
        }

        // ------------------------------------------------------------------ utilitaires
        /// <summary>Composants de toutes les scenes ouvertes, objets inactifs compris (sans FindObjectsByType).</summary>
        static List<T> FindAll<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                    result.AddRange(root.GetComponentsInChildren<T>(includeInactive));
            }
            return result;
        }

        static void FitModel(Transform model, Transform pivot, float radius)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            model.localPosition = Vector3.zero;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float current = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            if (current > 1e-5f) model.localScale *= radius / current;
            b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            model.position += pivot.position - b.center;
        }

        static Transform FindLeftHand(Transform origin)
        {
            string[] names = { "Left Controller", "LeftHand Controller", "Left Hand Controller", "LeftHand", "Left Hand" };
            var all = origin.GetComponentsInChildren<Transform>(true);
            foreach (var n in names)
                foreach (var t in all)
                    if (t.name == n) return t;
            return null;
        }

        static MarsMissionCatalog LoadOrCreateCatalog(string jezeroScene)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MarsMissionCatalog>(CatalogPath);
            if (!catalog)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:MarsMissionCatalog"))
                {
                    catalog = AssetDatabase.LoadAssetAtPath<MarsMissionCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                    if (catalog) break;
                }
            }
            if (catalog) return catalog;
            if (!AssetDatabase.IsValidFolder(CatalogFolder)) AssetDatabase.CreateFolder("Assets/MarsHUD", "Data");
            catalog = ScriptableObject.CreateInstance<MarsMissionCatalog>();
            catalog.FillDefaults(jezeroScene ?? "");
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static void AddToBuild(string path, int index)
        {
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == path);
            var entry = new EditorBuildSettingsScene(path, true);
            if (index < 0 || index > list.Count) list.Add(entry); else list.Insert(index, entry);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
