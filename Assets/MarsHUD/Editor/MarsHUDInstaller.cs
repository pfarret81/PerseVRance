using UnityEditor;
using UnityEngine;

namespace MarsHUD.Editor
{
    public static class MarsHUDInstaller
    {
        [MenuItem("GameObject/Mars HUD/Create consultation HUD", false, 10)]
        static void CreateHUD()
        {
            var go=new GameObject("MarsHUD");
            Undo.RegisterCreatedObjectUndo(go,"Create Mars HUD");
            var hud=Undo.AddComponent<MarsHUDController>(go);
            if(Camera.main) hud.head=Camera.main.transform;
            Selection.activeGameObject=go;
            EditorGUIUtility.PingObject(go);
            Debug.Log("MarsHUD cree a la racine. Verifier Head et les reglages. Aucun XR Origin, EventSystem ou rover modifie.",go);
        }
        [MenuItem("Assets/Create/Mars HUD/Example stub catalog")]
        static void CreateCatalog()
        {
            var catalog=MarsMapCatalog.CreateStub();
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/MarsHUD-StubCatalog.asset");
            AssetDatabase.CreateAsset(catalog,path);
            AssetDatabase.SaveAssets(); Selection.activeObject=catalog;
        }
    }
}
