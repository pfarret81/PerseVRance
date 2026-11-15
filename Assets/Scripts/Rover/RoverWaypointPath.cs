using UnityEngine;

public class RoverWaypointPath : MonoBehaviour
{
    [SerializeField] private Transform[] points;
    private Vector3[] cached;
    private float[] cumulative;
    public float Length { get; private set; }
    public Vector3 End=>cached[cached.Length-1];
    public int PointCount=>runtimeWorldPoints!=null ? runtimeWorldPoints.Length : (points==null ? 0 : points.Length);
    public Transform Point(int i)=>runtimeWorldPoints!=null ? null : points[i];
    public bool BuildCache(out string error)
    {
        error="";
        if(runtimeWorldPoints!=null)
        {
            if(cached==null || cumulative==null || cached.Length<2)
            { error="Cache runtime indisponible."; return false; }
            return true;
        }
        if (points==null || points.Length<2) { error="Deux points minimum requis."; return false; }
        cached=new Vector3[points.Length]; cumulative=new float[points.Length];
        for (int i=0;i<points.Length;i++)
        {
            if (!points[i]) { error="Point manquant."; return false; }
            cached[i]=points[i].position;
            if (!Finite(cached[i].x) || !Finite(cached[i].z)) { error="Point invalide."; return false; }
            if (i==0) continue;
            float d=HorizontalDistance(cached[i-1],cached[i]);
            if (d<0.01f) { error="Deux points consecutifs sont trop proches."; return false; }
            cumulative[i]=cumulative[i-1]+d;
        }
        Length=cumulative[cumulative.Length-1]; return true;
    }
    public Vector3 Sample(float s)
    {
        s=Mathf.Clamp(s,0f,Length);
        for (int i=0;i<cached.Length-1;i++) if (s<=cumulative[i+1])
            return Vector3.Lerp(cached[i],cached[i+1],(s-cumulative[i])/(cumulative[i+1]-cumulative[i]));
        return End;
    }
    public bool ProjectWindow(Vector3 position,float minS,float maxS,out float s,out float error)
    {
        s=minS; error=float.PositiveInfinity;
        minS=Mathf.Clamp(minS,0f,Length); maxS=Mathf.Clamp(maxS,minS,Length);
        for (int i=0;i<cached.Length-1;i++)
        {
            if (cumulative[i+1]<minS || cumulative[i]>maxS) continue;
            Vector3 a=cached[i],b=cached[i+1],ab=b-a,ap=position-a;
            ab.y=0f; ap.y=0f;
            float t=Mathf.Clamp01(Vector3.Dot(ap,ab)/ab.sqrMagnitude);
            float candidate=Mathf.Clamp(Mathf.Lerp(cumulative[i],cumulative[i+1],t),
                Mathf.Max(minS,cumulative[i]),Mathf.Min(maxS,cumulative[i+1]));
            Vector3 projected=Vector3.Lerp(a,b,(candidate-cumulative[i])/(cumulative[i+1]-cumulative[i]));
            float d=HorizontalDistance(position,projected);
            if (d<error) { error=d; s=candidate; }
        }
        return !float.IsInfinity(error);
    }
    public static float HorizontalDistance(Vector3 a,Vector3 b)
    { a.y=0f; b.y=0f; return Vector3.Distance(a,b); }
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private void OnDrawGizmos()
    {
        if (points==null) return;
        Gizmos.color=Color.yellow;
        for (int i=0;i<points.Length;i++)
        {
            if (!points[i]) continue;
            Gizmos.DrawWireSphere(points[i].position,0.06f);
            if (i>0 && points[i-1]) Gizmos.DrawLine(points[i-1].position,points[i].position);
        }
    }

    // Lot 3A : points monde immuables une fois pris en charge par un follower.
    private Vector3[] runtimeWorldPoints;
    private int runtimeReaders;
    public bool IsRuntimePath => runtimeWorldPoints != null;
    public bool IsInUse => runtimeReaders > 0;
    internal void AcquireRuntimeReadLock() { runtimeReaders++; }
    internal void ReleaseRuntimeReadLock() { runtimeReaders=Mathf.Max(0,runtimeReaders-1); }

    public bool TrySetWorldPoints(System.Collections.Generic.IReadOnlyList<Vector3> source, out string error)
    {
        error="";
        if (IsInUse) { error="Parcours actif : construire un candidat distinct avant de remplacer le parcours."; return false; }
        if (source==null || source.Count<2) { error="Deux points minimum requis."; return false; }
        var newPoints=new Vector3[source.Count];
        var newCumulative=new float[source.Count];
        for(int i=0;i<source.Count;i++)
        {
            Vector3 v=source[i];
            if(!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) { error="Point monde non fini."; return false; }
            newPoints[i]=v;
            if(i==0) continue;
            float distance=HorizontalDistance(newPoints[i-1],v);
            if(distance<.01f) { error="Deux points consecutifs sont trop proches."; return false; }
            newCumulative[i]=newCumulative[i-1]+distance;
            if(!Finite(newCumulative[i])) { error="Longueur de parcours non finie."; return false; }
        }
        // Affectations seulement apres validation complete. Aucun Transform du rover touche.
        runtimeWorldPoints=newPoints;
        cached=newPoints;
        cumulative=newCumulative;
        Length=newCumulative[newCumulative.Length-1];
        return true;
    }

    public Vector3[] CopyCachedWorldPoints()
    {
        if(cached==null || cached.Length<2) return null;
        return (Vector3[])cached.Clone();
    }

}
