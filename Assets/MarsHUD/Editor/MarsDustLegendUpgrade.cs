using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsHUD.Editor
{
    public static class MarsDustLegendUpgrade
    {
        [MenuItem("Mars HUD/Mettre a jour la legende poussiere (hue)")]
        static void Upgrade()
        {
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Arreter Play avant la mise a jour du catalogue.");
                MarsHUDController hud=Selection.activeGameObject?Selection.activeGameObject.GetComponent<MarsHUDController>():null;
                if(!hud)
                {
                    var candidates=new List<MarsHUDController>();
                    for(int s=0;s<SceneManager.sceneCount;s++)
                    {
                        Scene scene=SceneManager.GetSceneAt(s);
                        if(!scene.isLoaded) continue;
                        foreach(var root in scene.GetRootGameObjects()) candidates.AddRange(root.GetComponentsInChildren<MarsHUDController>(true));
                    }
                    if(candidates.Count!=1) throw new InvalidOperationException("Selectionner l'objet MarsHUD a mettre a jour dans la Hierarchy.");
                    hud=candidates[0];
                }
                var catalog=hud.catalog;
                if(!catalog || !AssetDatabase.Contains(catalog)) throw new InvalidOperationException("Le HUD doit referencer un catalogue .asset persistant du lot 2.");
                var layers=new List<MarsMapLayer>();
                if(catalog.layers!=null) foreach(var layer in catalog.layers)
                {
                    if(layer==null || layer.theme!=MarsMapTheme.Atmosphere || !layer.colorTexture) continue;
                    string name=(layer.displayName??"").ToLowerInvariant();
                    string source=layer.provenance??"";
                    if(name.Contains("poussi") || source.Contains("TES_Dust") || layer.colorTexture.name=="dust_zone") layers.Add(layer);
                }
                if(layers.Count==0) throw new InvalidOperationException("Aucune couche poussiere identifiable. Dans le catalogue, choisir manuellement Legend Mode = Hue Blue To Red pour la bonne couche.");
                if(!EditorUtility.DisplayDialog("Legende poussiere","Appliquer le degrade hue bleu -> cyan -> vert -> jaune -> orange -> rouge au catalogue actuellement assigne ? Les images, l'emprise et le recalage restent inchanges.","Appliquer","Annuler")) return;
                Undo.RecordObject(catalog,"Legende poussiere hue");
                foreach(var layer in layers)
                {
                    layer.legendMode=MarsLegendMode.HueBlueToRed;
                    layer.lowColor=Color.blue; layer.highColor=Color.red;
                    layer.legendLow="0 : bleu"; layer.legendHigh="1 : rouge";
                    layer.interpretation="Degrade de teinte bleu-cyan-vert-jaune-orange-rouge rapporte par l'utilisateur. Echelle affichee 0-1 ; aucune unite physique, date ou valeur numerique locale deduite du JPEG.";
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                EditorGUIUtility.PingObject(catalog);
                Debug.Log("Legende hue mise a jour. Aucun terrain, repere de carte ou texture source modifie.",catalog);
            }
            catch(Exception ex) { Debug.LogError("Mise a jour legende : "+ex.Message); }
        }
    }
}
