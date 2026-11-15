using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.Editor
{
    public sealed class MarsJezeroMapSetup : EditorWindow
    {
        MarsHUDController hud;
        GameObject terrainRoot;
        Texture2D hillshade, dust;
        bool hillshadeNorthAtTop=true, dustNorthAtTop=true;
        Vector2 scroll;
        [MenuItem("Mars HUD/Configurer Jezero - lot 2")]
        static void Open() { GetWindow<MarsJezeroMapSetup>("Jezero - Cartes"); }
        void OnEnable()
        {
            minSize=new Vector2(510f,430f);
            hillshade=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MarsHUD/Maps/hillshade_zone.jpg");
            dust=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MarsHUD/Maps/dust_zone.jpg");
            if(Selection.activeGameObject && Selection.activeGameObject.GetComponentsInChildren<Terrain>(false).Length>0)
                terrainRoot=Selection.activeGameObject;
            var candidates=new List<MarsHUDController>();
            for(int s=0;s<SceneManager.sceneCount;s++)
            {
                Scene scene=SceneManager.GetSceneAt(s);
                if(!scene.isLoaded) continue;
                foreach(var r in scene.GetRootGameObjects()) candidates.AddRange(r.GetComponentsInChildren<MarsHUDController>(true));
            }
            if(candidates.Count==1) hud=candidates[0];
        }
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Configurer les cartes sur le terrain EXISTANT. Ne regenere aucun terrain. Les recadrages manuels restent approximatifs, meme si les dimensions sont correctes.",MessageType.Info);
            hud=(MarsHUDController)EditorGUILayout.ObjectField("HUD existant",hud,typeof(MarsHUDController),true);
            terrainRoot=(GameObject)EditorGUILayout.ObjectField("Groupe de terrains ACTIF",terrainRoot,typeof(GameObject),true);
            hillshade=(Texture2D)EditorGUILayout.ObjectField("Hillshade",hillshade,typeof(Texture2D),false);
            dust=(Texture2D)EditorGUILayout.ObjectField("Poussiere TES",dust,typeof(Texture2D),false);
            hillshadeNorthAtTop=EditorGUILayout.Toggle("Hillshade : nord en haut",hillshadeNorthAtTop);
            dustNorthAtTop=EditorGUILayout.Toggle("Poussiere : nord en haut",dustNorthAtTop);
            EditorGUILayout.HelpBox("Nord en haut est une hypothese initiale a verifier. Chaque image est appliquee a l'emprise complete du groupe choisi. Aucune unite physique ni valeur par pixel n'est reconstruite depuis les JPEG.",MessageType.Warning);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if(GUILayout.Button("Analyser l'emprise (sans modification)",GUILayout.Height(30f)))
                {
                    try { var b=Analyze(out int count); Debug.Log($"Cartes : {count} terrains actifs, sud-ouest ({b.min.x:F3}, {b.min.z:F3}), emprise {b.size.x:F3} x {b.size.z:F3} unites monde.",terrainRoot); }
                    catch(Exception ex) { Debug.LogError(ex.Message); }
                }
                if(GUILayout.Button("Configurer les deux cartes",GUILayout.Height(36f))) Configure();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Poussiere : bornes utilisateur uniquement",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("0 = bleu ; 1 = rouge. Palette intermediaire non deduite.");
            EditorGUILayout.LabelField("Aucun controleur de vehicule ni acquisition automatique active.");
            EditorGUILayout.EndScrollView();
        }
        Bounds Analyze(out int count)
        {
            if(!terrainRoot || !terrainRoot.scene.IsValid() || !terrainRoot.activeInHierarchy)
                throw new InvalidOperationException("Choisir le groupe de terrains actif dans la Hierarchy, pas un asset du Project.");
            var terrains=terrainRoot.GetComponentsInChildren<Terrain>(false);
            var boxes=new List<Bounds>();
            double area=0;
            bool first=true;
            Bounds total=new Bounds();
            foreach(var t in terrains)
            {
                if(!t.enabled) continue;
                if(!t.terrainData) throw new InvalidOperationException("Terrain sans TerrainData : "+t.name);
                Vector3 scale=t.transform.lossyScale;
                if((scale-Vector3.one).sqrMagnitude>1e-8f || Quaternion.Angle(t.transform.rotation,Quaternion.identity)>.01f)
                    throw new InvalidOperationException("Le configurateur attend des Terrain sans rotation et d'echelle monde (1,1,1) : "+t.name);
                Vector3 size=t.terrainData.size;
                if(size.x<=0f || size.z<=0f) throw new InvalidOperationException("Dimensions de tuile invalides.");
                var b=new Bounds(t.transform.position+size*.5f,size);
                boxes.Add(b);
                if(first) { total=b; first=false; } else total.Encapsulate(b);
                area+=(double)size.x*size.z;
            }
            count=boxes.Count;
            if(first) throw new InvalidOperationException("Aucun Terrain actif utilisable sous ce groupe.");
            const float tolerance=.02f;
            for(int i=0;i<boxes.Count;i++) for(int j=i+1;j<boxes.Count;j++)
            {
                float ix=Mathf.Min(boxes[i].max.x,boxes[j].max.x)-Mathf.Max(boxes[i].min.x,boxes[j].min.x);
                float iz=Mathf.Min(boxes[i].max.z,boxes[j].max.z)-Mathf.Max(boxes[i].min.z,boxes[j].min.z);
                if(ix>tolerance && iz>tolerance) throw new InvalidOperationException("Chevauchement de terrains : verifier le groupe selectionne et les anciens terrains.");
            }
            double boundingArea=(double)total.size.x*total.size.z;
            if(Math.Abs(area-boundingArea)>Math.Max(1.0,boundingArea*.0001))
                throw new InvalidOperationException("L'ensemble ne remplit pas une emprise rectangulaire complete. Verifier les tuiles actives.");
            return total;
        }
        void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            string createdAsset=null;
            int undoGroup=-1;
            bool success=false;
            try
            {
                if(!hud || !hud.gameObject.scene.IsValid()) throw new InvalidOperationException("Assigner le HUD existant dans la scene.");
                if(hud.transform.parent || (hud.transform.lossyScale-Vector3.one).sqrMagnitude>1e-8f)
                    throw new InvalidOperationException("Le HUD doit rester a la racine, echelle (1,1,1).");
                if(!hillshade || !dust) throw new InvalidOperationException("Assigner les deux images du Project.");
                Bounds b=Analyze(out int count);
                if(terrainRoot.scene!=hud.gameObject.scene) throw new InvalidOperationException("HUD et terrain doivent etre dans la meme scene pour cette configuration.");
                if(!EditorUtility.DisplayDialog("Configurer les cartes Jezero",$"{count} terrains ; {b.size.x:F2} x {b.size.z:F2} unites monde.\nUn nouveau catalogue sera cree et assigne au HUD. L'ancien catalogue et les terrains ne seront pas modifies.\nLe recalage des images recadrees restera approximatif.","Configurer","Annuler")) return;
                Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Configurer cartes Jezero");
                var coordinates=hud.GetComponent<MarsMapCoordinates>();
                Undo.RecordObject(coordinates,"Origine cartographique");
                Transform origin=hud.transform.Find("Map southwest origin");
                if(!origin)
                {
                    var go=new GameObject("Map southwest origin");
                    Undo.RegisterCreatedObjectUndo(go,"Creer origine carte");
                    Undo.SetTransformParent(go.transform,hud.transform,"Parenter origine carte");
                    origin=go.transform;
                }
                Undo.RecordObject(origin,"Placer origine carte");
                origin.position=b.min;
                origin.rotation=Quaternion.identity;
                origin.localScale=Vector3.one;
                coordinates.originReference=origin;
                coordinates.northYawDegrees=0f;
                coordinates.worldUnitsPerMeter=1f;
                var c=ScriptableObject.CreateInstance<MarsMapCatalog>();
                c.extentSize=new Vector2(b.size.x,b.size.z);
                c.extentCenter=c.extentSize*.5f;
                c.layers=new[] {
                    new MarsMapLayer {
                        theme=MarsMapTheme.Topography, displayName="Relief - hillshade", colorTexture=hillshade,
                        textureNorthAtTop=hillshadeNorthAtTop, units="Ombrage, pas une altitude",
                        provenance="NASA Mars Trek", legendMode=MarsLegendMode.Hidden,
                        approximateRegistration=true, legendLow="", legendHigh="",
                        interpretation="Image recadree manuellement, supposee couvrir l'emprise complete. Aucune altitude deduite du gris."
                    },
                    new MarsMapLayer {
                        theme=MarsMapTheme.Atmosphere, displayName="Poussiere - MGS TES Global Dust", colorTexture=dust,
                        textureNorthAtTop=dustNorthAtTop, units="Echelle fournie 0-1 ; unite non documentee",
                        provenance="NASA Mars Trek / TES_Dust", legendMode=MarsLegendMode.EndpointsOnly,
                        lowColor=Color.blue, highColor=Color.red, legendLow="0 : bleu", legendHigh="1 : rouge",
                        approximateRegistration=true,
                        interpretation="Bornes rapportees par l'utilisateur. Pas de lecture numerique par pixel, date et palette intermediaire non documentees."
                    },
                    new MarsMapLayer {
                        theme=MarsMapTheme.Soil, displayName="Composition du sol", units="Aucune mesure",
                        provenance="Prelevements a implementer", legendMode=MarsLegendMode.Hidden,
                        legendLow="", legendHigh="", approximateRegistration=false,
                        interpretation="Aucune composition n'est generee dans ce lot."
                    }
                };
                string folder="Assets/MarsHUD/Maps";
                if(!AssetDatabase.IsValidFolder("Assets/MarsHUD")) AssetDatabase.CreateFolder("Assets","MarsHUD");
                if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/MarsHUD","Maps");
                createdAsset=AssetDatabase.GenerateUniqueAssetPath(folder+"/Jezero-MarsTrek.asset");
                AssetDatabase.CreateAsset(c,createdAsset);
                Undo.RecordObject(hud,"Assigner catalogue Jezero"); hud.catalog=c;
                if(!hud.GetComponent<MarsLayerPresentation>()) Undo.AddComponent<MarsLayerPresentation>(hud.gameObject);
                if(!hud.GetComponent<MarsMapAcquisition>()) Undo.AddComponent<MarsMapAcquisition>(hud.gameObject);
                EditorUtility.SetDirty(coordinates); EditorUtility.SetDirty(hud); EditorUtility.SetDirty(c);
                if(PrefabUtility.IsPartOfPrefabInstance(hud)) PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
                if(PrefabUtility.IsPartOfPrefabInstance(coordinates)) PrefabUtility.RecordPrefabInstancePropertyModifications(coordinates);
                EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                success=true;
                Selection.activeGameObject=hud.gameObject;
                Debug.Log($"Cartes configurees : {count} terrains, {b.size.x:F3} x {b.size.z:F3}. Origine sud-ouest={origin.position}. Catalogue={createdAsset}. Recalage approximatif a verifier sur trois reperes. Aucun terrain regenere.",hud);
            }
            catch(Exception ex) { Debug.LogError("Configuration cartes interrompue : "+ex.Message); }
            finally
            {
                if(!success && undoGroup>=0)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    if(!string.IsNullOrEmpty(createdAsset)) AssetDatabase.DeleteAsset(createdAsset);
                }
            }
        }
    }
}
