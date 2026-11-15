using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoverRuntimePathCheckInstaller
{
    [MenuItem("GameObject/Mars HUD/Create rover API check (3A)",false,20)]
    static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Creer l'outil hors Play."); return; }
        var selected=Selection.activeGameObject;
        var follower=selected?selected.GetComponent<RoverWaypointFollower>():null;
        if(!follower)
        { Debug.LogWarning("Selectionner dans la Hierarchy la racine portant RoverWaypointFollower."); return; }
        var go=new GameObject("Rover mission API check - 3A");
        SceneManager.MoveGameObjectToScene(go,follower.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(go,"Creer outil de test API rover");
        var check=Undo.AddComponent<RoverRuntimePathCheck>(go);
        check.follower=follower;
        check.resumeAfterAcceptance=false;
        Selection.activeGameObject=go;
        Debug.Log("Outil cree a la racine, hors rover. Aucun parcours ou deplacement modifie. Tests accessibles dans le menu contextuel du composant en Play.",go);
    }
}
