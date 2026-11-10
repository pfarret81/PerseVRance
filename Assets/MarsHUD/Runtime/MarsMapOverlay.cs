using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MarsMapOverlay : MaskableGraphic
    {
        public MarsMapGraphic Map { get; set; }
        static void Quad(VertexHelper vh, Vector2 min, Vector2 max, Color color)
        {
            int i=vh.currentVertCount;
            vh.AddVert(new Vector3(min.x,min.y,0f),color,Vector2.zero);
            vh.AddVert(new Vector3(min.x,max.y,0f),color,Vector2.zero);
            vh.AddVert(new Vector3(max.x,max.y,0f),color,Vector2.zero);
            vh.AddVert(new Vector3(max.x,min.y,0f),color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
        }
        bool Valid(Vector2 p)
        {
            var h=Map.Host;
            var l=Map.Layer;
            if(!h.Catalog.Contains(p) || !h.Discovery.IsKnown(Map.Theme,p) || l==null) return false;
            if(l.validMask && l.validMask.isReadable)
            {
                Vector2 uv=h.Catalog.DataToUV(p);
                if(!l.textureNorthAtTop) uv.y=1f-uv.y;
                return l.validMask.GetPixelBilinear(uv.x,uv.y).a>.5f;
            }
            return true;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if(!Map || !Map.Host || !Map.Host.Ready) return;
            Rect r=rectTransform.rect;
            const int n=36;
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                Vector2 a=new Vector2(Mathf.Lerp(r.xMin,r.xMax,x/(float)n),Mathf.Lerp(r.yMin,r.yMax,y/(float)n));
                Vector2 b=a+new Vector2(r.width/n,r.height/n);
                Vector2 center=Map.LocalToData((a+b)*.5f);
                bool valid=Valid(Map.LocalToData(a)) && Valid(Map.LocalToData(b)) &&
                    Valid(Map.LocalToData(new Vector2(a.x,b.y))) && Valid(Map.LocalToData(new Vector2(b.x,a.y)));
                if(valid) continue;
                Color c;
                if(!Map.Host.Catalog.Contains(center)) c=new Color(.015f,.018f,.025f,1f);
                else if(!Map.Host.Discovery.IsKnown(Map.Theme,center)) c=new Color(.045f,.055f,.075f,1f);
                else c=new Color(.24f,.08f,.24f,1f);
                Quad(vh,a,b,c);
            }
            DrawMarker(vh,Map.Host.Head.position,new Color(.2f,.95f,1f),true);
            if(Map.Host.roverMarkerSource) DrawMarker(vh,Map.Host.roverMarkerSource.position,new Color(1f,.75f,.2f),false);
        }
        void DrawMarker(VertexHelper vh, Vector3 world, Color color, bool astronaut)
        {
            Vector2 p=Map.DataToLocal(Map.Host.Coordinates.WorldToData(world));
            Rect r=rectTransform.rect;
            if(!r.Contains(p) || p.x<r.xMin+10 || p.x>r.xMax-10 || p.y<r.yMin+10 || p.y>r.yMax-10) return;
            if(!astronaut) { Quad(vh,p-Vector2.one*5,p+Vector2.one*5,color); return; }
            Vector2 d=Map.Host.Coordinates.DirectionToData(Map.Host.Head.forward);
            if(d.sqrMagnitude<.01f) d=Vector2.up;
            Vector2 side=new Vector2(d.y,-d.x);
            int i=vh.currentVertCount;
            vh.AddVert(p+d*10,color,Vector2.zero);
            vh.AddVert(p-d*7-side*6,color,Vector2.zero);
            vh.AddVert(p-d*7+side*6,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);
        }
    }
}
