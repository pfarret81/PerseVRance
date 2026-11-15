using UnityEngine;

// Lot 3 - extension additive, lecture seule. Meme dossier/assembly que les autres fichiers partiels.
// N'ecrit aucun champ du solveur et ne deplace aucun transform.
public partial class RoverStaticPoseTest
{
    /// <summary>
    /// Vrai si le collider fait partie de la liste blanche des surfaces de roulage
    /// (TerrainColliders et, si configures, rochers franchissables).
    /// </summary>
    public bool IsNavigationSurface(Collider c)
    {
        if (!c) return false;
        BuildSceneSurfaceFilter();
        return sceneAllowedSurfaces != null && sceneAllowedSurfaces.Contains(c);
    }

    /// <summary>Angle maximal du domaine du solveur (lecture seule) : le planificateur reste en dessous.</summary>
    public float NavigationMaxPoseAngle => maxPoseAngle;
}
