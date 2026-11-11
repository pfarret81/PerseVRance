using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MarsRoundedPanel : MaskableGraphic
    {
        [Min(0f)] public float radius = 18f;
        [Min(0f)] public float borderWidth = 1.5f;
        public Color fillColor = new Color(.025f, .055f, .085f, .90f);
        public Color borderColor = new Color(.25f, .65f, .8f, .8f);
        const int StepsPerCorner = 8;
        const int BoundaryCount = 4 * (StepsPerCorner + 1);

        static Vector2 Boundary(Rect rect, float r, int corner, float t)
        {
            Vector2 center;
            switch (corner)
            {
                case 0: center = new Vector2(rect.xMax-r, rect.yMin+r); break;
                case 1: center = new Vector2(rect.xMax-r, rect.yMax-r); break;
                case 2: center = new Vector2(rect.xMin+r, rect.yMax-r); break;
                default: center = new Vector2(rect.xMin+r, rect.yMin+r); break;
            }
            float angle = (-90f + corner * 90f + t * 90f) * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect outer = rectTransform.rect;
            if (outer.width <= 0f || outer.height <= 0f) return;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(outer.width, outer.height) * .5f);
            float b = Mathf.Clamp(borderWidth, 0f, Mathf.Min(outer.width, outer.height) * .49f);
            Rect inner = new Rect(outer.xMin+b, outer.yMin+b, outer.width-2f*b, outer.height-2f*b);
            float ir = Mathf.Max(0f, r-b);
            Color fill = fillColor * color;
            Color border = borderColor * color;
            vh.AddVert(outer.center, fill, Vector2.zero);
            for (int c=0; c<4; c++) for (int s=0; s<=StepsPerCorner; s++)
                vh.AddVert(Boundary(inner,ir,c,s/(float)StepsPerCorner),fill,Vector2.zero);
            for (int i=0; i<BoundaryCount; i++)
                vh.AddTriangle(0, 1+i, 1+(i+1)%BoundaryCount);
            if (b <= 0f) return;
            int ring = vh.currentVertCount;
            for (int c=0; c<4; c++) for (int s=0; s<=StepsPerCorner; s++)
            {
                float t = s/(float)StepsPerCorner;
                vh.AddVert(Boundary(inner,ir,c,t),border,Vector2.zero);
                vh.AddVert(Boundary(outer,r,c,t),border,Vector2.zero);
            }
            for (int i=0; i<BoundaryCount; i++)
            {
                int a=ring+2*i;
                int next=ring+2*((i+1)%BoundaryCount);
                vh.AddTriangle(a,a+1,next+1);
                vh.AddTriangle(a,next+1,next);
            }
        }
    }
}
