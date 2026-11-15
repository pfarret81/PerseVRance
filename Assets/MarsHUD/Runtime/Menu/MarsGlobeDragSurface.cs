using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Surface invisible devant le globe : recoit le glisser du rayon XR UI (meme principe que MarsMapGraphic)
    /// et le transmet au menu en metres dans le plan de la surface. Ne dessine rien (aucun sommet).
    /// </summary>
    public sealed class MarsGlobeDragSurface : MaskableGraphic, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        public MarsGlobeMenu Host;
        int pointerId = int.MinValue;
        Vector2 lastLocal;

        // Quad totalement transparent : il garde une profondeur de canvas (indispensable au raycast UI) et
        // cullTransparentMesh est desactive pour que le CanvasRenderer ne l'ecarte pas. Rien n'est visible.
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color32 c = new Color32(0, 0, 0, 0);
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector2.up);
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector2.one);
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector2.right);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
        protected override void OnEnable() { base.OnEnable(); canvasRenderer.cullTransparentMesh = false; }

        bool Point(PointerEventData e, out Vector2 p)
        {
            var hit = e.pointerCurrentRaycast;
            if (hit.gameObject == gameObject)
            {
                Vector3 local = rectTransform.InverseTransformPoint(hit.worldPosition);
                p = new Vector2(local.x, local.y) * rectTransform.lossyScale.x;   // metres
                return true;
            }
            p = default; return false;
        }

        public void OnPointerDown(PointerEventData e) { if (Host) Host.OnGlobeTouched(); }
        public void OnBeginDrag(PointerEventData e)
        {
            if (!Host || pointerId != int.MinValue || !Point(e, out lastLocal)) return;
            pointerId = e.pointerId;
            Host.OnGlobeTouched();
        }
        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId || !Host || !Point(e, out var p)) return;
            Vector2 delta = p - lastLocal; lastLocal = p;
            Host.OnGlobeDrag(delta, Time.unscaledDeltaTime);
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            if (Host) Host.OnGlobeReleased();
        }
        protected override void OnDisable() { pointerId = int.MinValue; base.OnDisable(); }
    }
}
