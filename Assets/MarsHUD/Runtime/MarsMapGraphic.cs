using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsHUD
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MarsMapGraphic : MaskableGraphic, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public MarsHUDController Host { get; set; }
        public MarsMapTheme Theme { get; set; }
        public MarsMapLayer Layer { get; private set; }
        public Vector2 Center { get; private set; }
        public float SpanMeters { get; private set; } = 160f;
        int pointerId = int.MinValue;
        Vector2 lastLocal;
        public override Texture mainTexture => Layer != null && Layer.colorTexture ? Layer.colorTexture : Texture2D.whiteTexture;
        public float HeightMeters => SpanMeters * rectTransform.rect.height / Mathf.Max(1f, rectTransform.rect.width);

        public Vector2 LocalToData(Vector2 p)
        {
            Rect r = rectTransform.rect;
            return Center + new Vector2((p.x-r.center.x)/r.width*SpanMeters, (p.y-r.center.y)/r.height*HeightMeters);
        }
        public Vector2 DataToLocal(Vector2 p)
        {
            Rect r = rectTransform.rect;
            Vector2 d = p-Center;
            return r.center + new Vector2(d.x/SpanMeters*r.width, d.y/HeightMeters*r.height);
        }
        public void SetView(Vector2 center, float span)
        {
            if ((center-Center).sqrMagnitude < .000001f && Mathf.Approximately(span, SpanMeters)) return;
            Center=center; SpanMeters=span; SetVerticesDirty();
        }
        public void SetLayer(MarsMapLayer layer)
        {
            Layer=layer; SetAllDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!Host || !Host.Ready) return;
            const int cells = 36;
            Rect rect = rectTransform.rect;
            for (int y=0; y<=cells; y++) for (int x=0; x<=cells; x++)
            {
                Vector2 local = new Vector2(Mathf.Lerp(rect.xMin,rect.xMax,x/(float)cells), Mathf.Lerp(rect.yMin,rect.yMax,y/(float)cells));
                Vector2 data = LocalToData(local);
                Vector2 uv = Host.Catalog.DataToUV(data);
                bool inExtent=Host.Catalog.Contains(data);
                bool known=Host.Discovery.IsKnown(Theme,data);
                bool valid=Layer!=null;
                Vector2 textureUV=uv;
                if (Layer!=null && !Layer.textureNorthAtTop) textureUV.y=1f-textureUV.y;
                if (valid && Layer.validMask && Layer.validMask.isReadable && inExtent)
                    valid=Layer.validMask.GetPixelBilinear(textureUV.x,textureUV.y).a>.5f;
                Color color;
                if (!inExtent) color=new Color(.015f,.018f,.025f);
                else if (!known) color=new Color(.045f,.055f,.075f);
                else if (!valid) color=new Color(.2f,.08f,.2f);
                else if (Layer.colorTexture) color=Color.white;
                else
                {
                    float v=Mathf.Clamp01(.5f+.23f*Mathf.Sin(data.x*.047f)+.22f*Mathf.Cos(data.y*.033f));
                    color=Color.Lerp(Layer.lowColor,Layer.highColor,v);
                }
                if (Layer!=null && Layer.colorTexture && (!known || !valid || !inExtent)) color=Color.black;
                vh.AddVert(local,color,textureUV);
            }
            for(int y=0;y<cells;y++) for(int x=0;x<cells;x++)
            {
                int i=y*(cells+1)+x;
                vh.AddTriangle(i,i+cells+1,i+1); vh.AddTriangle(i+1,i+cells+1,i+cells+2);
            }
        }
        bool Point(PointerEventData e, out Vector2 p)
        {
            var hit=e.pointerCurrentRaycast;
            if(hit.gameObject==gameObject)
            {
                Vector3 local=rectTransform.InverseTransformPoint(hit.worldPosition);
                p=new Vector2(local.x,local.y); return true;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out p);
        }
        public void OnBeginDrag(PointerEventData e)
        {
            if (!Host.CanNavigate(Theme) || pointerId!=int.MinValue || !Point(e,out lastLocal)) return;
            pointerId=e.pointerId;
        }
        public void OnDrag(PointerEventData e)
        {
            if(e.pointerId!=pointerId || !Host.CanNavigate(Theme) || !Point(e,out var p)) return;
            Vector2 delta=p-lastLocal; lastLocal=p;
            Rect r=rectTransform.rect;
            Host.Pan(Theme, -new Vector2(delta.x/r.width*SpanMeters,delta.y/r.height*HeightMeters));
        }
        public void OnEndDrag(PointerEventData e) { if(e.pointerId==pointerId) pointerId=int.MinValue; }
        protected override void OnDisable() { pointerId=int.MinValue; base.OnDisable(); }
    }
}
