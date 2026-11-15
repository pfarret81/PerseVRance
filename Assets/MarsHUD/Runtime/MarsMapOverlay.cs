using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MarsMapOverlay : MaskableGraphic
    {
        public MarsMapGraphic Map { get; set; }
        MarsMissionPanel missions;
        MarsSoilSampler soil;
        bool searched;
        void FindSources()
        {
            if (searched || !Map || !Map.Host) return;
            missions=Map.Host.GetComponent<MarsMissionPanel>();
            soil=Map.Host.GetComponent<MarsSoilSampler>();
            searched=true;
        }
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
            FindSources();
            DrawSamples(vh);
            bool vehicles=DrawVehicles(vh);
            // Repli lot 1 : marqueur simple si aucune mission n'est installee.
            if(!vehicles && Map.Host.roverMarkerSource) DrawMarker(vh,Map.Host.roverMarkerSource.position,new Color(1f,.75f,.2f),false);
            DrawMarker(vh,Map.Host.Head.position,new Color(.2f,.95f,1f),true);
        }
        Vector2 ToLocal(Vector3 world) => Map.DataToLocal(Map.Host.Coordinates.WorldToData(world));
        bool Inside(Vector2 p,float margin)
        {
            Rect r=rectTransform.rect;
            return p.x>=r.xMin+margin && p.x<=r.xMax-margin && p.y>=r.yMin+margin && p.y<=r.yMax-margin;
        }
        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 d=b-a;
            float len=d.magnitude;
            if(len<.01f) return;
            Vector2 n=new Vector2(-d.y,d.x)/len*(width*.5f);
            int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero); vh.AddVert(a+n,color,Vector2.zero);
            vh.AddVert(b+n,color,Vector2.zero); vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
        }
        /// <summary>Trait tirete continu d'un segment a l'autre (8 px plein, 6 px vide).</summary>
        static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color, ref float phase)
        {
            const float on=8f, period=14f;
            Vector2 d=b-a;
            float len=d.magnitude;
            if(len<.01f) return;
            Vector2 u=d/len;
            float t=0f;
            while(t<len)
            {
                float local=phase%period;
                float span=local<on ? Mathf.Min(on-local,len-t) : Mathf.Min(period-local,len-t);
                if(local<on) Line(vh,a+u*t,a+u*(t+span),width,color);
                t+=span; phase+=span;
            }
        }
        static void Diamond(VertexHelper vh, Vector2 p, float size, Color color)
        {
            int i=vh.currentVertCount;
            vh.AddVert(p+new Vector2(0f,size),color,Vector2.zero); vh.AddVert(p+new Vector2(size,0f),color,Vector2.zero);
            vh.AddVert(p+new Vector2(0f,-size),color,Vector2.zero); vh.AddVert(p+new Vector2(-size,0f),color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
        }
        void DrawSamples(VertexHelper vh)
        {
            if(!soil || Map.Theme==MarsMapTheme.Atmosphere) return;
            var list=soil.Samples;
            for(int i=0;i<list.Count;i++)
            {
                Vector2 p=Map.DataToLocal(list[i].data);
                if(!Inside(p,8f)) continue;
                Diamond(vh,p,8f,new Color(0f,0f,0f,.85f));
                Diamond(vh,p,5.5f,MarsMapCatalog.EvaluateHueLegend(list[i].anomaly));
            }
        }
        bool DrawVehicles(VertexHelper vh)
        {
            if(!missions) return false;
            var list=missions.Vehicles;
            bool any=false;
            for(int v=0;v<list.Count;v++)
            {
                var m=list[v];
                if(m==null || !m.TryGetPose(out Vector3 pos,out Vector3 fwd)) continue;
                any=true;
                bool selected=missions.SelectedIndex==v;
                if(Map.Theme==MarsMapTheme.Topography)
                {
                    var route=m.Route;
                    int start=Mathf.Clamp(m.RouteProgressIndex,0,Mathf.Max(0,route.Count-1));
                    int remaining=route.Count-start;
                    if(remaining>=2)
                    {
                        int stride=Mathf.Max(1,remaining/220);   // borne le nombre de segments dessines
                        Color c=new Color(m.MarkerColor.r,m.MarkerColor.g,m.MarkerColor.b,selected?.95f:.6f);
                        bool dashed=m.Kind==MarsVehicleKind.Aerial;   // lot 4 : trajet aerien tirete, distinct du rover
                        float width=selected?3.5f:2.5f, phase=0f;
                        Vector2 prev=ToLocal(pos);
                        for(int i=start+1;i<route.Count;i+=stride)
                        {
                            Vector2 next=ToLocal(route[i]);
                            if(Inside(prev,-40f) || Inside(next,-40f)) { if(dashed) Dashed(vh,prev,next,width,c,ref phase); else Line(vh,prev,next,width,c); }
                            else phase=0f;
                            prev=next;
                        }
                        Vector2 last=ToLocal(route[route.Count-1]);
                        if(dashed) Dashed(vh,prev,last,width,c,ref phase); else Line(vh,prev,last,width,c);
                    }
                    if(m.HasDestination)
                    {
                        Vector2 d=ToLocal(m.Destination);
                        if(Inside(d,10f))
                        {
                            Color cross=m.Kind==MarsVehicleKind.Aerial?m.MarkerColor:Color.white;
                            Line(vh,d+new Vector2(-10f,-10f),d+new Vector2(10f,10f),5f,new Color(0f,0f,0f,.85f));
                            Line(vh,d+new Vector2(-10f,10f),d+new Vector2(10f,-10f),5f,new Color(0f,0f,0f,.85f));
                            Line(vh,d+new Vector2(-9f,-9f),d+new Vector2(9f,9f),3f,cross);
                            Line(vh,d+new Vector2(-9f,9f),d+new Vector2(9f,-9f),3f,cross);
                        }
                    }
                }
                Vector2 p=ToLocal(pos);
                if(!Inside(p,10f)) continue;
                Vector2 dir=Map.Host.Coordinates.DirectionToData(fwd);
                if(dir.sqrMagnitude<.01f) dir=Vector2.up;
                if(m.Kind==MarsVehicleKind.Aerial)
                {
                    // Ingenuity : croix de rotors sur fond sombre, contour blanc si selectionne
                    float arm=selected?11f:9f;
                    if(selected) { Line(vh,p+new Vector2(-arm-2f,-arm-2f),p+new Vector2(arm+2f,arm+2f),7f,Color.white); Line(vh,p+new Vector2(-arm-2f,arm+2f),p+new Vector2(arm+2f,-arm-2f),7f,Color.white); }
                    Line(vh,p+new Vector2(-arm,-arm),p+new Vector2(arm,arm),5f,new Color(0f,0f,0f,.9f));
                    Line(vh,p+new Vector2(-arm,arm),p+new Vector2(arm,-arm),5f,new Color(0f,0f,0f,.9f));
                    Line(vh,p+new Vector2(-arm+1f,-arm+1f),p+new Vector2(arm-1f,arm-1f),3f,m.MarkerColor);
                    Line(vh,p+new Vector2(-arm+1f,arm-1f),p+new Vector2(arm-1f,-arm+1f),3f,m.MarkerColor);
                    Quad(vh,p-Vector2.one*3f,p+Vector2.one*3f,m.MarkerColor);
                    continue;
                }
                if(selected) { Quad(vh,p-Vector2.one*10f,p+Vector2.one*10f,Color.white); Quad(vh,p-Vector2.one*8f,p+Vector2.one*8f,new Color(0f,0f,0f,.9f)); }
                Quad(vh,p-Vector2.one*6f,p+Vector2.one*6f,m.MarkerColor);
                Line(vh,p,p+dir*13f,3f,m.MarkerColor);
            }
            return any;
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
