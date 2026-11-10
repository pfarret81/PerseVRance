using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MarsHUD
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MarsMapCoordinates), typeof(MarsDiscoveryState))]
    public sealed class MarsHUDController : MonoBehaviour
    {
        [Header("References")]
        public Transform head;
        public MarsMapCatalog catalog;
        [Tooltip("Optionnel : position reelle du rover. Pas de duplication animee. Aucun ordre envoye.")]
        public Transform roverMarkerSource;
        [Header("Menu : action dediee optionnelle, sinon binding local + F8")]
        public InputActionReference menuAction;
        [Tooltip("Prototype : Y gauche. Modifier ici si conflit avec une autre fonction.")]
        public string menuBinding = "<XRController>{LeftHand}/secondaryButton";
        [Header("Reglages de prototype")]
        [Min(.3f)] public float distanceMeters = 1.7f;
        [Min(.0001f)] public float canvasWorldScale = .001f;
        public float verticalOffsetMeters = -.08f;
        [Min(.01f)] public float followTimeSeconds = .25f;
        [Min(1f)] public float compactSpanMeters = 160f;
        [Min(.1f)] public float minSpanMeters = 10f;
        [Min(1f)] public float maxSpanMeters = 1500f;
        [Range(2f,30f)] public float mapRefreshHz = 10f;
        public bool MenuOpen { get; private set; }
        public bool Ready { get; private set; }
        public Transform Head => head;
        public MarsMapCoordinates Coordinates { get; private set; }
        public MarsDiscoveryState Discovery { get; private set; }
        public MarsMapCatalog Catalog => catalog;

        sealed class Window
        {
            public RectTransform root, mapRect;
            public CanvasGroup controls;
            public MarsMapGraphic map;
            public MarsMapOverlay overlay;
            public Text detail;
            public RectTransform legend;
            public Image[] legendSteps;
            public Button expand;
            public bool visible=true;
            public int layer;
            public Vector2 center;
            public float span;
            public bool following=true;
            public int lastDiscoveryVersion=-1;
            public bool detailsInitialized;
            public MarsMapLayer lastLayer;
        }
        readonly Window[] windows=new Window[3];
        readonly string[] names={"Topographie","Atmosphere","Sol"};
        Canvas canvas;
        RectTransform canvasRect;
        CanvasGroup menuControls, interactionGate;
        Text modeText;
        Font font;
        int expanded=-1;
        float nextRefresh;
        InputAction localToggle, activeToggle;
        bool enabledExternal;
        bool ownsCatalog;

        void Start()
        {
            if(!head && Camera.main) head=Camera.main.transform;
            if(!head) { Debug.LogError("MarsHUD : assigner la Main Camera a Head.",this); enabled=false; return; }
            if(transform.parent) { Debug.LogError("MarsHUD : placer l'objet HUD a la racine de la scene (hors XR Origin).",this); enabled=false; return; }
            Coordinates=GetComponent<MarsMapCoordinates>();
            Discovery=GetComponent<MarsDiscoveryState>();
            Coordinates.Initialize(head.position);
            Discovery.Initialize(Coordinates.WorldToData(head.position));
            if(!catalog) { catalog=MarsMapCatalog.CreateStub(); ownsCatalog=true; }
            ValidateCatalog();
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Ready=true;
            Build();
            PlaceImmediately();
            ConfigureInput();
            if(!FindFirstObjectByType<EventSystem>() || !FindFirstObjectByType<XRUIInputModule>())
                Debug.LogWarning("MarsHUD : EventSystem/XR UI Input Module actif introuvable. Aucun objet ajoute automatiquement.",this);
            Refresh(true);
        }
        void ValidateCatalog()
        {
            if(catalog.extentSize.x<=0 || catalog.extentSize.y<=0)
                Debug.LogWarning("MarsHUD : emprise invalide, corriger le catalogue.",this);
            foreach(var l in catalog.layers)
                if(l!=null && l.validMask && !l.validMask.isReadable)
                {
                    Debug.LogError("MarsHUD : masque non lisible pour "+l.displayName+". Couche desactivee dans ce prototype : activer Read/Write sur le masque.",this);
                    invalidMasks=true;
                }
        }
        bool invalidMasks;
        void ConfigureInput()
        {
            if(menuAction && menuAction.action!=null)
            {
                activeToggle=menuAction.action;
                enabledExternal=!activeToggle.enabled;
                if(enabledExternal) activeToggle.Enable();
            }
            else
            {
                localToggle=new InputAction("MarsHUD Toggle",InputActionType.Button);
                localToggle.AddBinding("<Keyboard>/f8");
                if(!string.IsNullOrWhiteSpace(menuBinding)) localToggle.AddBinding(menuBinding);
                activeToggle=localToggle; activeToggle.Enable();
            }
        }
        void OnEnable()
        {
            if(!Ready || activeToggle==null) return;
            if(localToggle!=null || enabledExternal) activeToggle.Enable();
            if(canvas) canvas.gameObject.SetActive(true);
        }
        void OnDisable()
        {
            if(localToggle!=null) localToggle.Disable();
            else if(enabledExternal && activeToggle!=null) activeToggle.Disable();
            if(canvas) canvas.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            localToggle?.Dispose();
            if(canvas) Destroy(canvas.gameObject);
            if(ownsCatalog && catalog) Destroy(catalog);
        }
        void Update()
        {
            if(!Ready) return;
            if(activeToggle!=null && activeToggle.WasPressedThisFrame()) ToggleMenu();
            if(Time.unscaledTime>=nextRefresh)
            {
                nextRefresh=Time.unscaledTime+1f/Mathf.Max(2f,mapRefreshHz);
                Refresh(false);
            }
        }
        void LateUpdate()
        {
            if(!Ready || MenuOpen) return;
            Pose target=TargetPose();
            float a=1f-Mathf.Exp(-Time.unscaledDeltaTime/Mathf.Max(.01f,followTimeSeconds));
            canvasRect.position=Vector3.Lerp(canvasRect.position,target.position,a);
            canvasRect.rotation=Quaternion.Slerp(canvasRect.rotation,target.rotation,a);
        }
        Pose TargetPose()
        {
            Vector3 position=head.position+head.forward*distanceMeters+head.up*verticalOffsetMeters;
            return new Pose(position,head.rotation);
        }
        void PlaceImmediately()
        {
            Pose p=TargetPose(); canvasRect.SetPositionAndRotation(p.position,p.rotation);
        }
        public void ToggleMenu()
        {
            MenuOpen=!MenuOpen;
            if(!MenuOpen && expanded>=0) { expanded=-1; Layout(); }
            interactionGate.interactable=MenuOpen;
            interactionGate.blocksRaycasts=MenuOpen;
            menuControls.alpha=MenuOpen?1f:0f;
            menuControls.interactable=MenuOpen;
            menuControls.blocksRaycasts=MenuOpen;
            modeText.text=MenuOpen?"MENU : panneaux ancres dans le monde":"HUD : suivi retarde | bouton HUD : menu";
            foreach(var w in windows)
            {
                w.controls.interactable=MenuOpen;
                w.controls.blocksRaycasts=MenuOpen;
                w.map.raycastTarget=MenuOpen && expanded==(int)w.map.Theme;
            }
            Layout();
            // Aucun changement aux action maps ni aux composants de locomotion.
            Refresh(true);
        }
        public bool CanNavigate(MarsMapTheme theme) => MenuOpen && expanded==(int)theme;
        public void Pan(MarsMapTheme theme,Vector2 delta)
        {
            if(!CanNavigate(theme)) return;
            Window w=windows[(int)theme]; w.following=false;
            w.center=ClampCenter(w.center+delta); Refresh(true);
        }
        Vector2 ClampCenter(Vector2 p)
        {
            Vector2 half=catalog.extentSize*.5f;
            return new Vector2(Mathf.Clamp(p.x,catalog.extentCenter.x-half.x,catalog.extentCenter.x+half.x),
                Mathf.Clamp(p.y,catalog.extentCenter.y-half.y,catalog.extentCenter.y+half.y));
        }
        void Zoom(int index,float factor)
        {
            if(!CanNavigate((MarsMapTheme)index)) return;
            windows[index].span=Mathf.Clamp(windows[index].span*factor,minSpanMeters,Mathf.Max(minSpanMeters,maxSpanMeters)); Refresh(true);
        }
        void Expand(int i)
        {
            if(!MenuOpen) return;
            if(expanded==i) expanded=-1;
            else if(expanded<0)
            {
                expanded=i; windows[i].center=Coordinates.WorldToData(head.position);
                windows[i].span=compactSpanMeters; windows[i].following=true;
            }
            Layout(); Refresh(true);
        }
        void Recenter(int i)
        {
            windows[i].following=true; Refresh(true);
        }
        void CycleLayer(int i)
        {
            int count=catalog.Count((MarsMapTheme)i);
            if(count<1) return;
            Window w=windows[i]; w.layer=(w.layer+1)%count;
            var next=catalog.Get((MarsMapTheme)i,w.layer);
            if(next!=null && next.validMask && !next.validMask.isReadable) next=null;
            w.map.SetLayer(next); Refresh(true);
        }
        void Refresh(bool force)
        {
            Vector2 player=Coordinates.WorldToData(head.position);
            for(int i=0;i<3;i++)
            {
                Window w=windows[i];
                if(!w.visible) continue;
                if(expanded!=i || w.following) w.center=player;
                var oldCenter=w.map.Center;
                float span=expanded==i?w.span:compactSpanMeters;
                bool dirty=force || (oldCenter-w.center).sqrMagnitude>.000001f ||
                    !Mathf.Approximately(w.map.SpanMeters,span) || Discovery.Version!=w.lastDiscoveryVersion;
                w.map.SetView(w.center,span);
                if(dirty) w.map.SetVerticesDirty();
                w.lastDiscoveryVersion=Discovery.Version;
                w.overlay.SetVerticesDirty();
                if(!w.detailsInitialized || w.lastLayer!=w.map.Layer)
                {
                    var l=w.map.Layer;
                    w.lastLayer=l; w.detailsInitialized=true;
                    for(int step=0;step<w.legendSteps.Length;step++)
                        w.legendSteps[step].color=l==null?Color.gray:Color.Lerp(l.lowColor,l.highColor,step/15f);
                    w.detail.text=l==null?"Couche indisponible":l.displayName+" | "+l.provenance+"\n"+
                        l.units+" | "+l.legendLow+" -> "+l.legendHigh+
                        (string.IsNullOrEmpty(l.timeOrDepth)?"":" | "+l.timeOrDepth)+
                        (i==2?"\nAucun prelevement : donnees inconnues":"\nSombre : inconnu | Violet : sans donnees | N = haut");
                    if(invalidMasks && l==null) w.detail.text="Couche indisponible : verifier le catalogue / masque";
                }
            }
        }
        RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform));
            var r=(RectTransform)go.transform; r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(.5f,.5f); r.pivot=new Vector2(.5f,.5f);
            r.sizeDelta=size; r.anchoredPosition=position; return r;
        }
        Image Panel(RectTransform r,Color c)
        {
            var image=r.gameObject.AddComponent<Image>(); image.color=c; image.raycastTarget=false; return image;
        }
        Text Label(string name,Transform parent,string text,Vector2 size,Vector2 position,int fontSize=18)
        {
            var r=Rect(name,parent,size,position);
            var t=r.gameObject.AddComponent<Text>(); t.font=font; t.text=text; t.fontSize=fontSize;
            t.color=Color.white; t.alignment=TextAnchor.MiddleCenter; t.raycastTarget=false;
            t.horizontalOverflow=HorizontalWrapMode.Wrap; return t;
        }
        Button Button(Transform parent,string text,Vector2 size,Vector2 position,Action action)
        {
            var r=Rect(text,parent,size,position);
            var image=Panel(r,new Color(.12f,.22f,.3f,.98f)); image.raycastTarget=true;
            var button=r.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors; colors.highlightedColor=new Color(.5f,.85f,1f); button.colors=colors;
            button.navigation=new Navigation { mode=Navigation.Mode.None };
            button.onClick.AddListener(()=>action());
            Label("Label",r,text,size,Vector2.zero,18); return button;
        }
        void Build()
        {
            canvasRect=Rect("MarsHUD Canvas (runtime)",transform,new Vector2(1500,850),Vector2.zero);
            canvasRect.localScale=Vector3.one*canvasWorldScale;
            canvas=canvasRect.gameObject.AddComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace;
            interactionGate=canvasRect.gameObject.AddComponent<CanvasGroup>();
            interactionGate.interactable=false; interactionGate.blocksRaycasts=false;
            canvas.worldCamera=head.GetComponent<Camera>();
            if(!canvas.worldCamera) canvas.worldCamera=Camera.main;
            canvasRect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            modeText=Label("Mode",canvasRect,"HUD : suivi retarde | bouton HUD : menu",new Vector2(1400,42),new Vector2(0,380),21);
            var bar=Rect("Window visibility",canvasRect,new Vector2(1400,50),new Vector2(0,325));
            menuControls=bar.gameObject.AddComponent<CanvasGroup>();
            for(int i=0;i<3;i++)
            {
                int index=i;
                Button(bar,names[i]+" : afficher / masquer",new Vector2(360,44),new Vector2((i-1)*400,0),()=>
                {
                    windows[index].visible=!windows[index].visible;
                    if(!windows[index].visible && expanded==index) expanded=-1;
                    Layout(); Refresh(true);
                });
            }
            Button(canvasRect,"Fermer menu",new Vector2(190,40),new Vector2(0,-390),()=> { if(MenuOpen) ToggleMenu(); });
            for(int i=0;i<3;i++)
            {
                int index=i;
                var w=new Window(); windows[i]=w;
                w.root=Rect(names[i],canvasRect,new Vector2(430,500),Vector2.zero);
                Panel(w.root,new Color(.025f,.04f,.065f,.97f));
                Label("Title",w.root,names[i],new Vector2(400,36),new Vector2(0,215),24);
                w.mapRect=Rect("Map",w.root,new Vector2(400,260),new Vector2(0,35));
                w.map=w.mapRect.gameObject.AddComponent<MarsMapGraphic>();
                w.map.Host=this; w.map.Theme=(MarsMapTheme)i; w.map.raycastTarget=false;
                var layer=catalog.Get((MarsMapTheme)i,0);
                if(layer!=null && layer.validMask && !layer.validMask.isReadable) layer=null;
                w.map.SetLayer(layer);
                var overlayRect=Rect("Fog and live markers",w.mapRect,w.mapRect.sizeDelta,Vector2.zero);
                w.overlay=overlayRect.gameObject.AddComponent<MarsMapOverlay>(); w.overlay.Map=w.map; w.overlay.raycastTarget=false;
                w.detail=Label("Layer information",w.root,"",new Vector2(400,80),new Vector2(0,-160),16);
                w.legend=Rect("Legend",w.root,new Vector2(320,4),new Vector2(0,-197));
                w.legendSteps=new Image[16];
                for(int step=0;step<16;step++)
                {
                    var cell=Rect("Step "+step,w.legend,new Vector2(20,4),new Vector2(-150+step*20,0));
                    w.legendSteps[step]=Panel(cell,Color.white);
                }
                var controls=Rect("Controls",w.root,new Vector2(400,48),new Vector2(0,-215));
                w.controls=controls.gameObject.AddComponent<CanvasGroup>();
                w.expand=Button(controls,"Agrandir",new Vector2(112,40),new Vector2(-140,0),()=>Expand(index));
                Button(controls,"Couche",new Vector2(100,40),new Vector2(-20,0),()=>CycleLayer(index));
                Button(controls,"Masquer",new Vector2(100,40),new Vector2(95,0),()=>
                {
                    w.visible=false; if(expanded==index) expanded=-1; Layout();
                });
                var nav=Rect("Navigation",w.root,new Vector2(400,42),new Vector2(0,-90));
                Button(nav,"+",new Vector2(60,38),new Vector2(-140,0),()=>Zoom(index,.75f));
                Button(nav,"-",new Vector2(60,38),new Vector2(-70,0),()=>Zoom(index,1f/.75f));
                Button(nav,"Recentrer",new Vector2(135,38),new Vector2(60,0),()=> { if(CanNavigate((MarsMapTheme)index)) Recenter(index); });
                w.controls.interactable=false; w.controls.blocksRaycasts=false;
                // Navigation recoit aussi le verrouillage du mode menu.
                var navGroup=nav.gameObject.AddComponent<CanvasGroup>();
                navGroups[i]=navGroup;
            }
            menuControls.alpha=0f; menuControls.blocksRaycasts=false; menuControls.interactable=false;
            Layout();
        }
        readonly CanvasGroup[] navGroups=new CanvasGroup[3];
        void Layout()
        {
            for(int i=0;i<3;i++)
            {
                Window w=windows[i];
                w.root.gameObject.SetActive(w.visible);
                bool large=expanded==i;
                float width=expanded<0?430f:(large?820f:260f);
                float height=large?620f:500f;
                float x;
                if(expanded<0) x=(i-1)*470f;
                else if(large) x=0;
                else
                {
                    int rank=0; for(int j=0;j<i;j++) if(j!=expanded) rank++;
                    x=rank==0?-575f:575f;
                }
                w.root.sizeDelta=new Vector2(width,height); w.root.anchoredPosition=new Vector2(x,-20);
                // Les panneaux lateraux sont mis a l'echelle, leurs coordonnees internes restent coherentes.
                if(expanded>=0 && !large)
                {
                    w.root.sizeDelta=new Vector2(430,500); w.root.localScale=Vector3.one*.60f;
                    w.mapRect.sizeDelta=new Vector2(400,260); w.mapRect.anchoredPosition=new Vector2(0,55);
                }
                else
                {
                    w.root.localScale=Vector3.one;
                    w.mapRect.sizeDelta=large?new Vector2(780,360):new Vector2(400,260);
                    w.mapRect.anchoredPosition=large?new Vector2(0,65):new Vector2(0,55);
                }
                w.overlay.rectTransform.sizeDelta=w.mapRect.sizeDelta;
                w.detail.rectTransform.anchoredPosition=new Vector2(0,large?-212:-160);
                ((RectTransform)w.controls.transform).anchoredPosition=new Vector2(0,large?-280:-220);
                ((RectTransform)navGroups[i].transform).anchoredPosition=new Vector2(0,large?-145:-100);
                if(w.legend) w.legend.anchoredPosition=new Vector2(0,large?-256:-197);
                var title=w.root.Find("Title") as RectTransform;
                title.anchoredPosition=new Vector2(0,large?280:215);
                w.expand.GetComponentInChildren<Text>().text=large?"Reduire":"Agrandir";
                w.expand.interactable=expanded<0 || large;
                w.controls.interactable=MenuOpen; w.controls.blocksRaycasts=MenuOpen;
                w.map.raycastTarget=MenuOpen && large;
                navGroups[i].interactable=MenuOpen && large; navGroups[i].blocksRaycasts=MenuOpen && large;
                navGroups[i].alpha=large?1f:.35f;
                w.map.SetVerticesDirty(); w.overlay.SetVerticesDirty();
            }
        }
    }
}
