using UnityEngine;

// Extension additive. Meme dossier/assembly que les autres fichiers partiels du solveur.
// Aucun composant supplementaire a ajouter au rover.
public partial class RoverStaticPoseTest
{
    public bool TryGetNavigationSurfaceBounds(out Bounds bounds,out string refusal)
    {
        bounds=default; refusal="";
        bool found=false;
        if(surfaces==null) { refusal="Aucune surface autorisee."; return false; }
        foreach(Collider c in surfaces)
        {
            if(!c || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
            Bounds b=c.bounds;
            if(!Finite(b.min.x) || !Finite(b.min.y) || !Finite(b.min.z) ||
               !Finite(b.max.x) || !Finite(b.max.y) || !Finite(b.max.z)) continue;
            if(!found) { bounds=b; found=true; } else bounds.Encapsulate(b);
        }
        if(!found) { refusal="Aucune surface autorisee active."; return false; }
        // Reconstruit le filtre depuis la liste autorisee, meme si ses elements ont change.
        sceneAllowedSource=null;
        BuildSceneSurfaceFilter();
        return true;
    }

    public bool TryProbeNavigationGround(Vector2 worldXZ,Bounds surfaceBounds,out RaycastHit hit,out string refusal)
    {
        hit=default; refusal="";
        if(!Finite(worldXZ.x) || !Finite(worldXZ.y) || !Finite(surfaceBounds.min.y) || !Finite(surfaceBounds.max.y))
        { refusal="Coordonnees ou bornes non finies."; return false; }
        float top=surfaceBounds.max.y+2f;
        float length=surfaceBounds.size.y+4f;
        if(!Finite(length) || length<=0f) { refusal="Hauteur de sondage invalide."; return false; }
        Ray ray=new Ray(new Vector3(worldXZ.x,top,worldXZ.y),Vector3.down);
        if(useScenePhysicsSurfaces)
        {
            int before=SceneProbeOverflowCount;
            if(TrySceneSurfaceRay(ray,length,out hit)) return true;
            refusal=SceneProbeOverflowCount>before ? "Tampon de sondage plein : candidat refuse." : "Aucune surface autorisee sous ce point.";
            return false;
        }
        bool found=false;
        float nearest=length;
        if(surfaces!=null) foreach(Collider c in surfaces)
        {
            if(!c || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
            if(c.Raycast(ray,out RaycastHit candidate,nearest))
            { hit=candidate; nearest=candidate.distance; found=true; }
        }
        if(!found) refusal="Aucune surface autorisee sous ce point.";
        return found;
    }
}
