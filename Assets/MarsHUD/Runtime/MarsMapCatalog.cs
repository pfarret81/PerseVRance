using System;
using UnityEngine;

namespace MarsHUD
{
    public enum MarsMapTheme { Topography, Atmosphere, Soil }
    // Valeurs explicites : preservation des catalogues deja serialises.
    public enum MarsLegendMode { Gradient=0, EndpointsOnly=1, Hidden=2, HueBlueToRed=3 }

    [Serializable]
    public sealed class MarsMapLayer
    {
        public MarsMapTheme theme;
        public string displayName="Couche de test";
        [Tooltip("Image deja coloree ; ce n'est pas un decodeur DEM.")]
        public Texture2D colorTexture;
        [Tooltip("Masque optionnel : alpha nul = sans donnees. Read/Write requis sur ce masque.")]
        public Texture2D validMask;
        public bool textureNorthAtTop=true;
        public string units="Sans unite (test)";
        public string legendLow="Bas (test)";
        public string legendHigh="Haut (test)";
        public string timeOrDepth="";
        public string provenance="STUB - non scientifique";
        public Color lowColor=new Color(.16f,.25f,.35f);
        public Color highColor=new Color(.87f,.64f,.29f);
        [Header("Presentation lot 2")]
        public MarsLegendMode legendMode=MarsLegendMode.Gradient;
        public bool approximateRegistration=true;
        [TextArea] public string interpretation="Donnees de test";
    }

    [CreateAssetMenu(menuName="Mars HUD/Map catalog", fileName="MarsMapCatalog")]
    public sealed class MarsMapCatalog : ScriptableObject
    {
        public Vector2 extentCenter=Vector2.zero;
        public Vector2 extentSize=new Vector2(1000f,1000f);
        public MarsMapLayer[] layers=new MarsMapLayer[0];
        public bool Contains(Vector2 p)
        {
            Vector2 d=p-extentCenter;
            return Mathf.Abs(d.x)<=extentSize.x*.5f && Mathf.Abs(d.y)<=extentSize.y*.5f;
        }
        public Vector2 DataToUV(Vector2 p)
        {
            return new Vector2((p.x-extentCenter.x)/Mathf.Max(.01f,extentSize.x)+.5f,
                (p.y-extentCenter.y)/Mathf.Max(.01f,extentSize.y)+.5f);
        }
        public int Count(MarsMapTheme theme)
        {
            int count=0;
            if(layers!=null) foreach(var layer in layers) if(layer!=null && layer.theme==theme) count++;
            return count;
        }
        public MarsMapLayer Get(MarsMapTheme theme,int index)
        {
            int count=0;
            if(layers!=null) foreach(var layer in layers)
                if(layer!=null && layer.theme==theme && count++==index) return layer;
            return null;
        }
        public static Color EvaluateHueLegend(float normalizedValue)
        {
            return Color.HSVToRGB((2f/3f)*(1f-Mathf.Clamp01(normalizedValue)),1f,1f);
        }
        public static MarsMapCatalog CreateStub()
        {
            var c=CreateInstance<MarsMapCatalog>();
            c.layers=new[] {
                new MarsMapLayer {theme=MarsMapTheme.Topography, displayName="Relief synthetique - TEST"},
                new MarsMapLayer {theme=MarsMapTheme.Atmosphere, displayName="Aerosols - TEST", lowColor=new Color(.12f,.22f,.45f), highColor=new Color(.9f,.4f,.15f)},
                new MarsMapLayer {theme=MarsMapTheme.Atmosphere, displayName="Couche alternative - TEST", lowColor=new Color(.08f,.35f,.3f), highColor=new Color(.8f,.8f,.2f)},
                new MarsMapLayer {theme=MarsMapTheme.Soil, displayName="Sol - aucun prelevement"}
            };
            return c;
        }
    }
}
