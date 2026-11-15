using UnityEngine;
using UnityEngine.UI;

namespace MarsHUD
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MarsHUDController))]
    public sealed class MarsLayerPresentation : MonoBehaviour
    {
        sealed class View
        {
            public MarsMapGraphic map;
            public Text detail;
            public Transform legend;
            public Image[] steps;
            public RawImage continuousLegend;
            public MarsMapLayer lastLayer;
            public bool initialized;
        }
        View[] views;
        Texture2D sharedHueTexture;
        void Start()
        {
            var hud=GetComponent<MarsHUDController>();
            if(!hud.Ready) { enabled=false; return; }
            var maps=GetComponentsInChildren<MarsMapGraphic>(true);
            views=new View[maps.Length];
            for(int i=0;i<maps.Length;i++)
            {
                Canvas canvas=maps[i].GetComponentInParent<Canvas>();
                Transform detail=canvas?canvas.transform.Find("Layer information"):null;
                Transform legend=canvas?canvas.transform.Find("Color scale"):null;
                if(!detail || !legend || !detail.TryGetComponent<Text>(out var label))
                {
                    Debug.LogError("MarsLayerPresentation attend la hierarchie HUD 1.1 : Layer information / Color scale.",this);
                    views=null; enabled=false; return;
                }
                Image[] steps=legend.GetComponentsInChildren<Image>(true);
                if(!legend.GetComponent<CanvasRenderer>()) legend.gameObject.AddComponent<CanvasRenderer>();
                RawImage continuous=legend.GetComponent<RawImage>();
                if(!continuous) continuous=legend.gameObject.AddComponent<RawImage>();
                continuous.raycastTarget=false;
                continuous.color=Color.white;
                continuous.enabled=false;
                views[i]=new View {map=maps[i],detail=label,legend=legend,steps=steps,continuousLegend=continuous};
                label.resizeTextForBestFit=true;
                label.resizeTextMinSize=12;
                label.resizeTextMaxSize=18;
            }
            UpdateViews();
        }
        Texture2D HueTexture()
        {
            if(sharedHueTexture) return sharedHueTexture;
            const int width=256;
            sharedHueTexture=new Texture2D(width,1,TextureFormat.RGBA32,false,false);
            sharedHueTexture.name="MarsHUD hue legend (shared runtime)";
            sharedHueTexture.filterMode=FilterMode.Bilinear;
            sharedHueTexture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color[width];
            for(int x=0;x<width;x++) pixels[x]=MarsMapCatalog.EvaluateHueLegend(x/(float)(width-1));
            sharedHueTexture.SetPixels(pixels);
            sharedHueTexture.Apply(false,true);
            return sharedHueTexture;
        }
        void OnDestroy()
        {
            if(sharedHueTexture) Destroy(sharedHueTexture);
        }
        void LateUpdate() { UpdateViews(); }
        void UpdateViews()
        {
            if(views==null) return;
            foreach(var view in views)
            {
                if(!view.map || !view.detail || !view.legend) continue;
                var layer=view.map.Layer;
                if(view.initialized && view.lastLayer==layer) continue;
                view.initialized=true; view.lastLayer=layer;
                if(layer==null)
                {
                    view.legend.gameObject.SetActive(false);
                    view.detail.text="Couche indisponible - verifier la ressource ou le masque";
                    continue;
                }
                bool hue=layer.legendMode==MarsLegendMode.HueBlueToRed;
                view.legend.gameObject.SetActive(layer.legendMode!=MarsLegendMode.Hidden);
                view.continuousLegend.enabled=hue;
                if(hue) view.continuousLegend.texture=HueTexture();
                int count=view.steps.Length;
                for(int s=0;s<count;s++)
                {
                    view.steps[s].enabled=!hue;
                    if(hue) continue;
                    Color c;
                    if(layer.legendMode==MarsLegendMode.EndpointsOnly)
                        c=s<count/4?layer.lowColor:(s>=count*3/4?layer.highColor:Color.clear);
                    else c=Color.Lerp(layer.lowColor,layer.highColor,count<=1?0f:s/(float)(count-1));
                    view.steps[s].color=c;
                }
                string endpoints=layer.legendMode==MarsLegendMode.Hidden?"":(" | "+layer.legendLow+" / "+layer.legendHigh);
                string time=string.IsNullOrEmpty(layer.timeOrDepth)?"":" | "+layer.timeOrDepth;
                string coverage=view.map.Theme==MarsMapTheme.Soil?"Aucun prelevement : inconnu":"Sombre : inconnu / Violet : sans donnees";
                view.detail.text=layer.displayName+"\n"+layer.units+endpoints+"\n"+layer.provenance+time+"\n"+
                    (layer.approximateRegistration?"Recalage approximatif | ":"")+coverage;
            }
        }
    }
}
