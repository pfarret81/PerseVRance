using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Outil de validation manuelle du sous-lot 3A, pas un controleur de deplacement.
public sealed class RoverRuntimePathCheck : MonoBehaviour
{
    public RoverWaypointFollower follower;
    public RoverWaypointPath candidatePath;
    [Tooltip("Faux par defaut : une adoption acceptee laisse le rover en pause.")]
    public bool resumeAfterAcceptance=false;
    readonly List<GameObject> ownedCandidates=new List<GameObject>();
    Vector3[] candidatePreview;

    bool Ready()
    {
        if(!Application.isPlaying || !follower || !follower.IsReadyForRuntimePath || !follower.CurrentPath || !follower.Actuator)
        { Debug.LogWarning("Test : entrer en Play, assigner un follower initialise et sans defaut.",this); return false; }
        return true;
    }
    [ContextMenu("1 - Prepare remaining EXISTING route candidate (no motion)")]
    public void PrepareRemainingRoute()
    {
        if(!Ready()) return;
        Vector3[] source=follower.CurrentPath.CopyCachedWorldPoints();
        if(source==null) { Debug.LogWarning("Cache source indisponible.",this); return; }
        var remaining=new List<Vector3>();
        remaining.Add(follower.Actuator.CenterPosition);
        float s=0f;
        for(int i=1;i<source.Length;i++)
        {
            s+=RoverWaypointPath.HorizontalDistance(source[i-1],source[i]);
            if(s>follower.Progress+.01f && RoverWaypointPath.HorizontalDistance(remaining[remaining.Count-1],source[i])>=.0101f)
                remaining.Add(source[i]);
        }
        if(remaining.Count<2) { Debug.LogWarning("Pas assez de parcours restant. Tester avant l'arrivee.",this); return; }
        CleanupUnused();
        var go=new GameObject("TEST - remaining route candidate");
        SceneManager.MoveGameObjectToScene(go,follower.gameObject.scene);
        var candidate=go.AddComponent<RoverWaypointPath>();
        if(!candidate.TrySetWorldPoints(remaining,out string reason))
        { Destroy(go); Debug.LogWarning(reason,this); return; }
        ownedCandidates.Add(go); candidatePath=candidate;
        candidatePreview=candidate.CopyCachedWorldPoints();
        Debug.Log($"Candidat prepare : {candidate.PointCount} points, {candidate.Length:F3} m. Suite du trajet existant, PAS une nouvelle destination planifiee. Aucun ordre envoye.",this);
    }
    [ContextMenu("2 - Validate candidate only (no new order)")]
    public void ValidateCandidate()
    {
        if(!Ready()) return;
        Vector3 position=follower.transform.position;
        Quaternion rotation=follower.transform.rotation;
        var originalPath=follower.CurrentPath;
        float progress=follower.Progress;
        var state=follower.State;
        bool accepted=follower.TryValidateReplacementPath(candidatePath,out string reason);
        bool unchanged=position==follower.transform.position && rotation==follower.transform.rotation &&
            originalPath==follower.CurrentPath && progress==follower.Progress && state==follower.State;
        Debug.Log($"Validation candidat : {(accepted?"ACCEPTE (validation structurelle seulement)":"REFUSE : "+reason)}. Etat/pose conserves={unchanged}.",this);
    }
    [ContextMenu("3 - Adopt candidate from CURRENT pose")]
    public void AdoptCandidate()
    {
        if(!Ready()) return;
        Vector3 position=follower.transform.position;
        Quaternion rotation=follower.transform.rotation;
        bool accepted=follower.TryAdoptPathFromCurrentPose(candidatePath,resumeAfterAcceptance,out string reason);
        float positionDelta=Vector3.Distance(position,follower.transform.position);
        float angleDelta=Quaternion.Angle(rotation,follower.transform.rotation);
        Debug.Log($"Adoption : {(accepted?"ACCEPTEE":"REFUSEE : "+reason)}. Delta immediat racine={positionDelta:G6} m / {angleDelta:G6} deg. Etat={follower.State}. Aucun test de traversabilite globale effectue.",this);
    }
    [ContextMenu("4 - Probe allowed ground below CURRENT rover (no motion)")]
    public void ProbeGround()
    {
        if(!Ready()) return;
        var solver=follower.Actuator.Solver;
        Physics.SyncTransforms();
        if(!solver.TryGetNavigationSurfaceBounds(out Bounds bounds,out string reason)) { Debug.LogWarning(reason,this); return; }
        Vector3 p=follower.Actuator.CenterPosition;
        if(solver.TryProbeNavigationGround(new Vector2(p.x,p.z),bounds,out RaycastHit hit,out reason))
            Debug.Log($"Sol autorise : {hit.collider.name}, point={hit.point}, normale={hit.normal}, pente locale={Vector3.Angle(hit.normal,Vector3.up):F2} deg. Ce n'est pas une validation des six appuis.",this);
        else Debug.LogWarning("Sondage refuse : "+reason,this);
    }
    [ContextMenu("5 - Check active-path rejection (no new order)")]
    public void CheckActiveRejection()
    {
        if(!Ready()) return;
        var original=follower.CurrentPath;
        var state=follower.State;
        float progress=follower.Progress;
        Vector3 position=follower.transform.position;
        Quaternion rotation=follower.transform.rotation;
        bool accepted=follower.TryAdoptPathFromCurrentPose(original,true,out string reason);
        bool preserved=original==follower.CurrentPath && state==follower.State && progress==follower.Progress &&
            position==follower.transform.position && rotation==follower.transform.rotation;
        Debug.Log($"Refus du meme parcours attendu : refus={!accepted}, conservation={preserved}, raison={reason}",this);
    }
    [ContextMenu("Delete unused TEST candidates")]
    public void CleanupUnused()
    {
        for(int i=ownedCandidates.Count-1;i>=0;i--)
        {
            GameObject go=ownedCandidates[i];
            if(!go) { ownedCandidates.RemoveAt(i); continue; }
            var p=go.GetComponent<RoverWaypointPath>();
            if(p && p.IsInUse) continue;
            Destroy(go); ownedCandidates.RemoveAt(i);
        }
    }
    void OnDrawGizmosSelected()
    {
        if(candidatePreview==null) return;
        Gizmos.color=new Color(.1f,.9f,1f);
        for(int i=1;i<candidatePreview.Length;i++) Gizmos.DrawLine(candidatePreview[i-1],candidatePreview[i]);
    }
}
