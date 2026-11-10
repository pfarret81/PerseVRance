using System;
using UnityEngine;

namespace MarsHUD
{
    public enum MarsMapTheme { Topography, Atmosphere, Soil }

    [Serializable]
    public sealed class MarsMapLayer
    {
        public MarsMapTheme theme;
        public string displayName = "Couche de test";
        [Tooltip("Image deja coloree. Aucun decodage de DEM ni d'unites scientifiques implicite.")]
        public Texture2D colorTexture;
        [Tooltip("Masque optionnel : alpha = 0 signifie sans donnees. Lecture CPU necessaire.")]
        public Texture2D validMask;
        public bool textureNorthAtTop = true;
        public string units = "Sans unite (test)";
        public string legendLow = "Bas (test)";
        public string legendHigh = "Haut (test)";
        public string timeOrDepth = "";
        public string provenance = "STUB - non scientifique";
        public Color lowColor = new Color(0.16f, 0.25f, 0.35f);
        public Color highColor = new Color(0.87f, 0.64f, 0.29f);
    }

    [CreateAssetMenu(menuName = "Mars HUD/Map catalog", fileName = "MarsMapCatalog")]
    public sealed class MarsMapCatalog : ScriptableObject
    {
        [Tooltip("Emprise en metres du repere de donnees ; pas des coordonnees geographiques.")]
        public Vector2 extentCenter = Vector2.zero;
        public Vector2 extentSize = new Vector2(1000f, 1000f);
        public MarsMapLayer[] layers = new MarsMapLayer[0];
        public bool Contains(Vector2 p)
        {
            Vector2 d = p - extentCenter;
            return Mathf.Abs(d.x) <= extentSize.x * .5f && Mathf.Abs(d.y) <= extentSize.y * .5f;
        }
        public Vector2 DataToUV(Vector2 p)
        {
            return new Vector2((p.x - extentCenter.x) / Mathf.Max(.01f, extentSize.x) + .5f,
                (p.y - extentCenter.y) / Mathf.Max(.01f, extentSize.y) + .5f);
        }
        public int Count(MarsMapTheme theme)
        {
            int n = 0;
            foreach (var l in layers) if (l != null && l.theme == theme) n++;
            return n;
        }
        public MarsMapLayer Get(MarsMapTheme theme, int index)
        {
            int n = 0;
            foreach (var l in layers)
                if (l != null && l.theme == theme && n++ == index) return l;
            return null;
        }
        public static MarsMapCatalog CreateStub()
        {
            var c = CreateInstance<MarsMapCatalog>();
            c.layers = new[] {
                new MarsMapLayer {theme=MarsMapTheme.Topography, displayName="Relief synthetique - TEST"},
                new MarsMapLayer {theme=MarsMapTheme.Atmosphere, displayName="Aerosols - TEST", lowColor=new Color(.12f,.22f,.45f), highColor=new Color(.9f,.4f,.15f)},
                new MarsMapLayer {theme=MarsMapTheme.Atmosphere, displayName="Couche alternative - TEST", lowColor=new Color(.08f,.35f,.3f), highColor=new Color(.8f,.8f,.2f)},
                new MarsMapLayer {theme=MarsMapTheme.Soil, displayName="Sol - aucun prelevement"}
            };
            return c;
        }
    }
}
