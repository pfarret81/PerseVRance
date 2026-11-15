using System.Collections.Generic;
using UnityEngine;

namespace MarsHUD
{
    [DisallowMultipleComponent]
    public sealed class MarsDiscoveryState : MonoBehaviour
    {
        [Min(0f)] public float initialKnownRadiusMeters=80f;
        [Header("Bornes de stockage - choix du prototype")]
        [Min(1f)] public float indexCellSizeMeters=128f;
        [Min(1)] public int maxAcquisitionDiscs=4096;
        [Min(1f)] public float maxFootprintRadiusMeters=1000f;
        public Vector2 InitialKnownCenter { get; private set; }
        public int Version { get; private set; }
        public int AcquisitionCount => discs.Count;
        public string LastRefusal { get; private set; }="";
        public bool Initialized { get; private set; }
        struct Disc { public Vector2 center; public float radius; }
        readonly List<Disc> discs=new List<Disc>();
        readonly Dictionary<Vector2Int,List<int>> buckets=new Dictionary<Vector2Int,List<int>>();
        float capturedCellSize;
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector2 p) => Finite(p.x) && Finite(p.y);
        public void Initialize(Vector2 start)
        {
            InitialKnownCenter=start;
            capturedCellSize=Mathf.Max(1f,indexCellSizeMeters);
            discs.Clear(); buckets.Clear(); LastRefusal="";
            Initialized=true; Version++;
        }
        Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x/capturedCellSize),Mathf.FloorToInt(p.y/capturedCellSize));
        public bool IsKnown(MarsMapTheme theme,Vector2 p)
        {
            if(!Initialized || !Finite(p) || theme==MarsMapTheme.Soil) return false;
            float r=Mathf.Max(0f,initialKnownRadiusMeters);
            if((p-InitialKnownCenter).sqrMagnitude<=r*r) return true;
            if(!buckets.TryGetValue(Cell(p),out var indices)) return false;
            for(int i=0;i<indices.Count;i++)
            {
                Disc d=discs[indices[i]];
                if((p-d.center).sqrMagnitude<=d.radius*d.radius) return true;
            }
            return false;
        }
        bool Refuse(string reason) { LastRefusal=reason; return false; }
        public bool TryRevealDisc(Vector2 center,float radiusMeters)
        {
            LastRefusal="";
            if(!Initialized) return Refuse("Etat de decouverte non initialise.");
            if(!Finite(center) || Mathf.Abs(center.x)>10000000f || Mathf.Abs(center.y)>10000000f || !Finite(radiusMeters) || radiusMeters<=0f || radiusMeters>maxFootprintRadiusMeters)
                return Refuse("Empreinte de decouverte invalide ou trop grande.");
            float initial=Mathf.Max(0f,initialKnownRadiusMeters);
            if(Vector2.Distance(center,InitialKnownCenter)+radiusMeters<=initial) return true;
            if(discs.Count>0)
            {
                Disc last=discs[discs.Count-1];
                if(Vector2.Distance(center,last.center)+radiusMeters<=last.radius) return true;
            }
            if(discs.Count>=maxAcquisitionDiscs) return Refuse("Capacite de decouverte du prototype atteinte.");
            Vector2Int min=Cell(center-Vector2.one*radiusMeters),max=Cell(center+Vector2.one*radiusMeters);
            if((long)(max.x-min.x+1)*(max.y-min.y+1)>4096L) return Refuse("Empreinte trop grande pour la taille des cellules de l'index.");
            int index=discs.Count;
            discs.Add(new Disc {center=center,radius=radiusMeters});
            for(int y=min.y;y<=max.y;y++) for(int x=min.x;x<=max.x;x++)
            {
                var key=new Vector2Int(x,y);
                if(!buckets.TryGetValue(key,out var list)) { list=new List<int>(); buckets.Add(key,list); }
                list.Add(index);
            }
            Version++; return true;
        }
    }
}
