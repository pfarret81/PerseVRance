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
        public enum SpatialMode { Passive, Opening, Menu, Closing }

        [Header("References conservees")]
        public Transform head;
        public MarsMapCatalog catalog;
        public Transform roverMarkerSource;
        [Header("Entree menu - ne modifie pas l'asset XRI")]
        public InputActionReference menuAction;
        public string menuBinding = "<XRController>{LeftHand}/secondaryButton";
        [Header("Disposition haute - valeurs du prototype")]
        [Range(0f,75f)] public float passiveElevationDegrees = 45f;
        [Min(.4f)] public float passiveDistanceMeters = 1.1f;
        [Range(15f,60f)] public float passiveSpreadDegrees = 32f;
        [Min(.01f)] public float followTimeSeconds = .25f;
        [Header("Menu frontal - capture a l'ouverture")]
        [Min(.4f)] public float menuDistanceMeters = 1.1f;
        [Range(15f,60f)] public float menuSpreadDegrees = 27f;
        public float menuVerticalOffsetMeters = 0f;
        [Min(.01f)] public float transitionSeconds = .32f;
        [Header("Dimensions - suppose 1 unite monde = 1 metre XR")]
        [Min(.0001f)] public float windowWorldScale = .00085f;
        [Range(.4f,1f)] public float sideWindowScale = .68f;
        [Header("Cartographie conservee")]
        [Min(1f)] public float compactSpanMeters = 160f;
        [Min(.1f)] public float minSpanMeters = 10f;
        [Min(1f)] public float maxSpanMeters = 1500f;
        [Range(2f,30f)] public float mapRefreshHz = 10f;
        [Header("Presentation")]
        public Color panelFill = new Color(.025f,.055f,.085f,.9f);
        public Color buttonFill = new Color(.06f,.12f,.17f,.95f);
        [Range(0f,32f)] public float cornerRadiusPixels = 20f;
        [SerializeField] SpatialMode spatialMode = SpatialMode.Passive;

        public bool MenuOpen => requestedMenu;
        public SpatialMode Mode => spatialMode;
        public bool Ready { get; private set; }
        public Transform Head => head;
        public MarsMapCoordinates Coordinates { get; private set; }
        public MarsDiscoveryState Discovery { get; private set; }
        public MarsMapCatalog Catalog => catalog;

        sealed class Window
        {
            public RectTransform root, viewport, header, detailRect, footer, navigation, legend;
            public CanvasGroup gate, footerGroup, navigationGroup;
            public MarsMapGraphic map;
            public MarsMapOverlay overlay;
            public MarsRoundedPanel panel;
            public Text title, detail;
            public Button expand;
            public Image[] legendSteps;
            public bool visible=true, following=true, detailsInitialized;
            public int layer, lastDiscoveryVersion=-1;
            public Vector2 center;
            public float span;
            public MarsMapLayer lastLayer;
        }
        readonly Window[] windows = new Window[3];
        readonly string[] names = { "TOPOGRAPHIE", "ATMOSPHERE", "COMPOSITION DU SOL" };
        readonly Color[] accents = {
            new Color(.91f,.64f,.35f,1f), new Color(.25f,.78f,.9f,1f), new Color(.9f,.77f,.38f,1f)
        };
        readonly Pose[] transitionStarts = new Pose[3];
        readonly Vector3[] transitionStartScales = new Vector3[3];
        readonly RectTransform[] ownedRoots = new RectTransform[4];
        readonly Button[] visibilityButtons = new Button[3];
        RectTransform menuRoot;
        CanvasGroup menuGate;
        Font font;
        InputAction localToggle, activeToggle;
        bool enabledExternal, ownsCatalog, requestedMenu;
        int expanded = -1;
        float nextRefresh, transitionElapsed, targetYaw, smoothYaw;
        Vector3 smoothHeadPosition, capturedHeadPosition;
        Quaternion capturedYaw;
        bool startupComplete;

        void Start()
        {
            if (!head && Camera.main) head=Camera.main.transform;
            if (!head) { Debug.LogError("MarsHUD : assigner Main Camera a Head.",this); enabled=false; return; }
            if (transform.parent) { Debug.LogError("MarsHUD doit etre a la racine de la scene, hors XR Origin.",this); enabled=false; return; }
            if (Mathf.Abs(transform.lossyScale.x-1f)>.001f || Mathf.Abs(transform.lossyScale.y-1f)>.001f || Mathf.Abs(transform.lossyScale.z-1f)>.001f)
            { Debug.LogError("MarsHUD : conserver une echelle (1,1,1) sur l'objet racine.",this); enabled=false; return; }
            Coordinates=GetComponent<MarsMapCoordinates>();
            Discovery=GetComponent<MarsDiscoveryState>();
            Coordinates.Initialize(head.position);
            Discovery.Initialize(Coordinates.WorldToData(head.position));
            if (!catalog) { catalog=MarsMapCatalog.CreateStub(); ownsCatalog=true; }
            if (catalog.extentSize.x<=0f || catalog.extentSize.y<=0f)
            { Debug.LogError("MarsHUD : emprise du catalogue invalide.",this); enabled=false; return; }
            ValidateMasks();
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            smoothHeadPosition=head.position;
            targetYaw=ReadHorizontalYaw(0f);
            smoothYaw=targetYaw;
            spatialMode=SpatialMode.Passive;
            requestedMenu=false;
            Ready=true;
            Build();
            ApplyLayout();
            for (int i=0;i<3;i++) SetWindowPose(i,PassivePose(i),TargetScale(i));
            SetInteractionState();
            RefreshMaps(true);
            ConfigureInput();
            startupComplete=true;
            if (!FindAnyObjectByType<EventSystem>() || !FindAnyObjectByType<XRUIInputModule>())
                Debug.LogWarning("MarsHUD : EventSystem / XR UI Input Module actif introuvable. Aucun composant ajoute automatiquement.",this);
        }
        float ReadHorizontalYaw(float fallback)
        {
            Vector3 forward=head.forward;
            forward.y=0f;
            // Pres de la verticale, la direction horizontale n'est pas fiable.
            if (forward.sqrMagnitude < .02f) return fallback;
            return Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
        }
        void ValidateMasks()
        {
            if (catalog.layers==null) return;
            foreach (var layer in catalog.layers)
                if (layer!=null && layer.validMask && !layer.validMask.isReadable)
                    Debug.LogError("MarsHUD : masque non lisible, couche indisponible : "+layer.displayName,this);
        }
        MarsMapLayer AvailableLayer(int i, int index)
        {
            if (catalog.layers==null) return null;
            var layer=catalog.Get((MarsMapTheme)i,index);
            if (layer!=null && layer.validMask && !layer.validMask.isReadable) return null;
            return layer;
        }
        void ConfigureInput()
        {
            if (menuAction && menuAction.action!=null)
            {
                activeToggle=menuAction.action;
                enabledExternal=!activeToggle.enabled;
                if (enabledExternal) activeToggle.Enable();
            }
            else
            {
                localToggle=new InputAction("MarsHUD Toggle",InputActionType.Button);
                localToggle.AddBinding("<Keyboard>/f8");
                if (!string.IsNullOrWhiteSpace(menuBinding)) localToggle.AddBinding(menuBinding);
                activeToggle=localToggle;
                activeToggle.Enable();
            }
        }
        void OnEnable()
        {
            if (!startupComplete) return;
            if (localToggle!=null || enabledExternal) activeToggle?.Enable();
            for (int i=0;i<3;i++) if (windows[i]!=null) windows[i].root.gameObject.SetActive(windows[i].visible);
            if (menuRoot) menuRoot.gameObject.SetActive(true);
        }
        void OnDisable()
        {
            if (localToggle!=null) localToggle.Disable();
            else if (enabledExternal) activeToggle?.Disable();
            foreach (var r in ownedRoots) if (r) r.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            localToggle?.Dispose();
            foreach (var r in ownedRoots) if (r) Destroy(r.gameObject);
            if (ownsCatalog && catalog) Destroy(catalog);
        }
        void Update()
        {
            if (!Ready) return;
            if (!head) { Debug.LogError("MarsHUD : reference Head perdue.",this); enabled=false; return; }
            if (activeToggle!=null && activeToggle.WasPressedThisFrame()) ToggleMenu();
            if (Time.unscaledTime>=nextRefresh)
            {
                nextRefresh=Time.unscaledTime+1f/Mathf.Max(2f,mapRefreshHz);
                RefreshMaps(false);
            }
        }
        void LateUpdate()
        {
            if (!Ready || !head) return;
            float dt=Time.unscaledDeltaTime;
            targetYaw=ReadHorizontalYaw(targetYaw);
            float a=1f-Mathf.Exp(-dt/Mathf.Max(.01f,followTimeSeconds));
            smoothHeadPosition=Vector3.Lerp(smoothHeadPosition,head.position,a);
            smoothYaw=Mathf.LerpAngle(smoothYaw,targetYaw,a);

            if (spatialMode==SpatialMode.Passive)
            {
                for (int i=0;i<3;i++) SetWindowPose(i,PassivePose(i),TargetScale(i));
            }
            else if (spatialMode==SpatialMode.Opening || spatialMode==SpatialMode.Closing)
            {
                transitionElapsed+=dt;
                float t=Mathf.Clamp01(transitionElapsed/Mathf.Max(.01f,transitionSeconds));
                float eased=t*t*(3f-2f*t);
                for (int i=0;i<3;i++)
                {
                    Pose target=requestedMenu?MenuPose(i):PassivePose(i);
                    Pose p=new Pose(Vector3.Lerp(transitionStarts[i].position,target.position,eased),
                        Quaternion.Slerp(transitionStarts[i].rotation,target.rotation,eased));
                    SetWindowPose(i,p,Vector3.Lerp(transitionStartScales[i],TargetScale(i),eased));
                }
                if (t>=1f)
                {
                    spatialMode=requestedMenu?SpatialMode.Menu:SpatialMode.Passive;
                    SetInteractionState();
                }
            }
            // SpatialMode.Menu : aucune mise a jour des poses, ancrage monde strict.
        }
        static Pose FacingPose(Vector3 eye, Quaternion yaw, float azimuth, float elevation, float distance, float yOffset)
        {
            float az=azimuth*Mathf.Deg2Rad;
            float el=elevation*Mathf.Deg2Rad;
            Vector3 local=new Vector3(Mathf.Sin(az)*Mathf.Cos(el),Mathf.Sin(el),Mathf.Cos(az)*Mathf.Cos(el))*distance;
            local.y+=yOffset;
            Vector3 position=eye+yaw*local;
            // La face visible du Canvas est son cote -Z.
            Quaternion rotation=Quaternion.LookRotation(position-eye,Vector3.up);
            return new Pose(position,rotation);
        }
        Pose PassivePose(int i)
        {
            return FacingPose(smoothHeadPosition,Quaternion.Euler(0f,smoothYaw,0f),(i-1)*passiveSpreadDegrees,
                passiveElevationDegrees,passiveDistanceMeters,0f);
        }
        Pose MenuPose(int i)
        {
            float az;
            if (expanded<0) az=(i-1)*menuSpreadDegrees;
            else if (expanded==i) az=0f;
            else
            {
                int rank=0;
                for (int j=0;j<i;j++) if (j!=expanded) rank++;
                az=rank==0?-38f:38f;
            }
            return FacingPose(capturedHeadPosition,capturedYaw,az,0f,menuDistanceMeters,menuVerticalOffsetMeters);
        }
        Vector3 TargetScale(int i)
        {
            float multiplier=requestedMenu && expanded>=0 && expanded!=i?sideWindowScale:1f;
            return Vector3.one*(windowWorldScale*multiplier);
        }
        void SetWindowPose(int i, Pose pose, Vector3 scale)
        {
            windows[i].root.SetPositionAndRotation(pose.position,pose.rotation);
            windows[i].root.localScale=scale;
        }
        public void ToggleMenu()
        {
            if (!Ready) return;
            requestedMenu=!requestedMenu;
            if (requestedMenu)
            {
                capturedHeadPosition=head.position;
                capturedYaw=Quaternion.Euler(0f,ReadHorizontalYaw(targetYaw),0f);
                PositionMenuBar();
            }
            else
            {
                expanded=-1;
            }
            for (int i=0;i<3;i++)
            {
                var r=windows[i].root;
                transitionStarts[i]=new Pose(r.position,r.rotation);
                transitionStartScales[i]=r.localScale;
            }
            transitionElapsed=0f;
            spatialMode=requestedMenu?SpatialMode.Opening:SpatialMode.Closing;
            ApplyLayout();
            SetInteractionState();
            RefreshMaps(false);
        }
        void PositionMenuBar()
        {
            Vector3 p=capturedHeadPosition+capturedYaw*new Vector3(0f,-.37f,menuDistanceMeters);
            menuRoot.SetPositionAndRotation(p,Quaternion.LookRotation(p-capturedHeadPosition,Vector3.up));
            menuRoot.localScale=Vector3.one*windowWorldScale;
        }
        void SetInteractionState()
        {
            bool interact=spatialMode==SpatialMode.Menu;
            menuGate.alpha=interact?1f:0f;
            menuGate.interactable=interact;
            menuGate.blocksRaycasts=interact;
            for (int i=0;i<3;i++)
            {
                var w=windows[i];
                w.gate.interactable=interact;
                w.gate.blocksRaycasts=interact;
                w.footerGroup.alpha=interact?1f:0f;
                w.footerGroup.interactable=interact;
                w.footerGroup.blocksRaycasts=interact;
                bool navigate=interact && expanded==i;
                w.navigationGroup.alpha=navigate?1f:0f;
                w.navigationGroup.interactable=navigate;
                w.navigationGroup.blocksRaycasts=navigate;
                w.map.raycastTarget=navigate;
                w.expand.interactable=expanded<0 || expanded==i;
            }
        }
        public bool CanNavigate(MarsMapTheme theme) => spatialMode==SpatialMode.Menu && expanded==(int)theme;
        public void Pan(MarsMapTheme theme, Vector2 delta)
        {
            if (!CanNavigate(theme)) return;
            var w=windows[(int)theme];
            w.following=false;
            Vector2 half=catalog.extentSize*.5f;
            Vector2 p=w.center+delta;
            w.center=new Vector2(Mathf.Clamp(p.x,catalog.extentCenter.x-half.x,catalog.extentCenter.x+half.x),
                Mathf.Clamp(p.y,catalog.extentCenter.y-half.y,catalog.extentCenter.y+half.y));
            RefreshMaps(false);
        }
        void Zoom(int i,float factor)
        {
            if (!CanNavigate((MarsMapTheme)i)) return;
            windows[i].span=Mathf.Clamp(windows[i].span*factor,minSpanMeters,Mathf.Max(minSpanMeters,maxSpanMeters));
            RefreshMaps(false);
        }
        void Recenter(int i)
        {
            if (!CanNavigate((MarsMapTheme)i)) return;
            windows[i].following=true;
            RefreshMaps(false);
        }
        void Expand(int i)
        {
            if (spatialMode!=SpatialMode.Menu) return;
            if (expanded==i) expanded=-1;
            else if (expanded<0)
            {
                expanded=i;
                windows[i].following=true;
                windows[i].span=compactSpanMeters;
            }
            else return;
            ApplyLayout();
            for (int j=0;j<3;j++) SetWindowPose(j,MenuPose(j),TargetScale(j));
            SetInteractionState();
            RefreshMaps(true);
        }
        void SetVisible(int i,bool visible)
        {
            if (spatialMode!=SpatialMode.Menu) return;
            windows[i].visible=visible;
            if (!visible && expanded==i) expanded=-1;
            ApplyLayout();
            for (int j=0;j<3;j++) SetWindowPose(j,MenuPose(j),TargetScale(j));
            SetInteractionState();
            UpdateVisibilityButtons();
            RefreshMaps(true);
        }
        void CycleLayer(int i)
        {
            if (spatialMode!=SpatialMode.Menu || catalog.layers==null) return;
            int count=catalog.Count((MarsMapTheme)i);
            if (count<1) return;
            var w=windows[i];
            w.layer=(w.layer+1)%count;
            w.map.SetLayer(AvailableLayer(i,w.layer));
            RefreshMaps(true);
        }
        void RefreshMaps(bool force)
        {
            if (!Ready) return;
            Vector2 player=Coordinates.WorldToData(head.position);
            for (int i=0;i<3;i++)
            {
                Window w=windows[i];
                if (!w.visible) continue;
                if (expanded!=i || w.following) w.center=player;
                float span=expanded==i?w.span:compactSpanMeters;
                bool dirty=force || (w.map.Center-w.center).sqrMagnitude>.000001f ||
                    !Mathf.Approximately(w.map.SpanMeters,span) || Discovery.Version!=w.lastDiscoveryVersion;
                w.map.SetView(w.center,span);
                if (dirty) w.map.SetVerticesDirty();
                w.lastDiscoveryVersion=Discovery.Version;
                w.overlay.SetVerticesDirty();
                if (!w.detailsInitialized || w.lastLayer!=w.map.Layer)
                {
                    var layer=w.map.Layer;
                    w.detailsInitialized=true; w.lastLayer=layer;
                    for (int s=0;s<w.legendSteps.Length;s++)
                        w.legendSteps[s].color=layer==null?Color.gray:Color.Lerp(layer.lowColor,layer.highColor,s/15f);
                    if (layer==null) w.detail.text="Couche indisponible - verifier le catalogue";
                    else w.detail.text=layer.displayName+" | "+layer.provenance+"\n"+layer.units+" | "+layer.legendLow+" -> "+layer.legendHigh+
                        (string.IsNullOrEmpty(layer.timeOrDepth)?"":" | "+layer.timeOrDepth)+
                        (i==2?"\nAucun prelevement : inconnu":"\nSombre : inconnu / Violet : sans donnees / N en haut");
                }
            }
        }
        void ApplyLayout()
        {
            for (int i=0;i<3;i++)
            {
                Window w=windows[i];
                bool large=requestedMenu && expanded==i;
                float width=large?760f:440f;
                float height=large?640f:460f;
                w.root.sizeDelta=new Vector2(width,height);
                w.root.gameObject.SetActive(w.visible);
                w.header.sizeDelta=new Vector2(width-40f,38f);
                w.header.anchoredPosition=new Vector2(0f,large?290f:200f);
                w.viewport.sizeDelta=large?new Vector2(720f,390f):new Vector2(404f,240f);
                w.viewport.anchoredPosition=large?new Vector2(0f,65f):new Vector2(0f,50f);
                w.map.rectTransform.sizeDelta=w.viewport.sizeDelta;
                w.overlay.rectTransform.sizeDelta=w.viewport.sizeDelta;
                w.detailRect.sizeDelta=new Vector2(width-44f,large?76f:104f);
                w.detailRect.anchoredPosition=new Vector2(0f,large?-208f:-128f);
                w.detail.fontSize=large?18:15;
                w.navigation.anchoredPosition=new Vector2(0f,-150f);
                w.footer.anchoredPosition=new Vector2(0f,large?-286f:-208f);
                w.legend.anchoredPosition=new Vector2(0f,large?-254f:-187f);
                w.expand.GetComponentInChildren<Text>().text=large?"Reduire":"Agrandir";
                w.map.SetVerticesDirty(); w.overlay.SetVerticesDirty();
            }
        }
        RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform));
            var r=(RectTransform)go.transform;
            r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);
            r.pivot=new Vector2(.5f,.5f);
            r.sizeDelta=size; r.anchoredPosition=position;
            return r;
        }
        RectTransform CreateCanvas(string name,Vector2 size)
        {
            var r=Rect(name,transform,size,Vector2.zero);
            r.localScale=Vector3.one*windowWorldScale;
            var c=r.gameObject.AddComponent<Canvas>();
            c.renderMode=RenderMode.WorldSpace;
            c.worldCamera=head.GetComponent<Camera>();
            if (!c.worldCamera) c.worldCamera=Camera.main;
            r.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            return r;
        }
        MarsRoundedPanel Plate(RectTransform rect,Color fill,Color border,float radius,float thickness,bool raycast=false)
        {
            var g=rect.gameObject.AddComponent<MarsRoundedPanel>();
            g.fillColor=fill; g.borderColor=border; g.radius=radius; g.borderWidth=thickness;
            g.raycastTarget=raycast; g.SetVerticesDirty();
            return g;
        }
        Text Label(string name,Transform parent,string text,Vector2 size,Vector2 position,int fontSize,Color textColor)
        {
            var r=Rect(name,parent,size,position);
            var t=r.gameObject.AddComponent<Text>();
            t.font=font; t.text=text; t.fontSize=fontSize;
            t.color=textColor; t.alignment=TextAnchor.MiddleCenter;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;
            t.raycastTarget=false; return t;
        }
        Button MakeButton(Transform parent,string text,Vector2 size,Vector2 position,Color accent,Action action)
        {
            var r=Rect(text,parent,size,position);
            var plate=Plate(r,buttonFill,new Color(accent.r,accent.g,accent.b,.65f),10f,1f,true);
            var button=r.gameObject.AddComponent<Button>();
            button.targetGraphic=plate;
            var colors=button.colors;
            colors.normalColor=Color.white;
            colors.highlightedColor=new Color(1.15f,1.2f,1.25f,1f);
            colors.pressedColor=new Color(.65f,.85f,.95f,1f);
            colors.disabledColor=new Color(.55f,.55f,.55f,.45f);
            colors.fadeDuration=.08f;
            button.colors=colors;
            button.navigation=new Navigation { mode=Navigation.Mode.None };
            button.onClick.AddListener(()=>action());
            Label("Label",r,text,size,Vector2.zero,18,new Color(.9f,.96f,1f));
            return button;
        }
        void Build()
        {
            for (int i=0;i<3;i++)
            {
                int index=i;
                Window w=new Window(); windows[i]=w;
                w.root=CreateCanvas(names[i]+" Window (runtime)",new Vector2(440f,460f));
                ownedRoots[i]=w.root;
                w.gate=w.root.gameObject.AddComponent<CanvasGroup>();
                w.panel=Plate(w.root,panelFill,new Color(accents[i].r,accents[i].g,accents[i].b,.75f),cornerRadiusPixels,1.6f);
                w.title=Label("Title",w.root,names[i],new Vector2(400f,38f),new Vector2(0f,200f),24,accents[i]);
                w.header=w.title.rectTransform;
                w.viewport=Rect("Rounded map viewport",w.root,new Vector2(404f,240f),new Vector2(0f,50f));
                Plate(w.viewport,Color.white,Color.clear,12f,0f);
                var mask=w.viewport.gameObject.AddComponent<Mask>(); mask.showMaskGraphic=false;
                var mapRect=Rect("Map",w.viewport,w.viewport.sizeDelta,Vector2.zero);
                // Explicite aussi au constructeur pour ne pas dependre d'un attribut herite.
                mapRect.gameObject.AddComponent<CanvasRenderer>();
                w.map=mapRect.gameObject.AddComponent<MarsMapGraphic>();
                w.map.Host=this; w.map.Theme=(MarsMapTheme)i; w.map.raycastTarget=false;
                w.map.SetLayer(AvailableLayer(i,0));
                var overlay=Rect("Fog and live markers",mapRect,mapRect.sizeDelta,Vector2.zero);
                overlay.gameObject.AddComponent<CanvasRenderer>();
                w.overlay=overlay.gameObject.AddComponent<MarsMapOverlay>();
                w.overlay.Map=w.map; w.overlay.raycastTarget=false;
                w.detail=Label("Layer information",w.root,"",new Vector2(396f,104f),new Vector2(0f,-128f),15,new Color(.78f,.86f,.9f));
                w.detailRect=w.detail.rectTransform;
                w.legend=Rect("Color scale",w.root,new Vector2(320f,5f),new Vector2(0f,-187f));
                w.legendSteps=new Image[16];
                for (int s=0;s<16;s++)
                {
                    var cell=Rect("Color "+s,w.legend,new Vector2(20f,5f),new Vector2(-150f+s*20f,0f));
                    var image=cell.gameObject.AddComponent<Image>(); image.raycastTarget=false;
                    w.legendSteps[s]=image;
                }
                w.footer=Rect("Window controls",w.root,new Vector2(404f,38f),new Vector2(0f,-208f));
                w.footerGroup=w.footer.gameObject.AddComponent<CanvasGroup>();
                w.expand=MakeButton(w.footer,"Agrandir",new Vector2(122f,36f),new Vector2(-134f,0f),accents[i],()=>Expand(index));
                MakeButton(w.footer,"Couche",new Vector2(118f,36f),Vector2.zero,accents[i],()=>CycleLayer(index));
                MakeButton(w.footer,"Masquer",new Vector2(122f,36f),new Vector2(134f,0f),accents[i],()=>SetVisible(index,false));
                w.navigation=Rect("Map navigation",w.root,new Vector2(400f,38f),new Vector2(0f,-150f));
                w.navigationGroup=w.navigation.gameObject.AddComponent<CanvasGroup>();
                MakeButton(w.navigation,"+",new Vector2(62f,36f),new Vector2(-140f,0f),accents[i],()=>Zoom(index,.75f));
                MakeButton(w.navigation,"-",new Vector2(62f,36f),new Vector2(-67f,0f),accents[i],()=>Zoom(index,1f/.75f));
                MakeButton(w.navigation,"Recentrer",new Vector2(150f,36f),new Vector2(74f,0f),accents[i],()=>Recenter(index));
            }
            menuRoot=CreateCanvas("Window visibility menu (runtime)",new Vector2(1040f,66f));
            ownedRoots[3]=menuRoot;
            Plate(menuRoot,new Color(.02f,.045f,.07f,.9f),new Color(.3f,.6f,.7f,.65f),18f,1.2f);
            menuGate=menuRoot.gameObject.AddComponent<CanvasGroup>();
            for (int i=0;i<3;i++)
            {
                int index=i;
                visibilityButtons[i]=MakeButton(menuRoot,"",new Vector2(326f,44f),new Vector2((i-1)*342f,0f),accents[i],
                    ()=>SetVisible(index,!windows[index].visible));
            }
            UpdateVisibilityButtons();
        }
        void UpdateVisibilityButtons()
        {
            for (int i=0;i<3;i++)
                visibilityButtons[i].GetComponentInChildren<Text>().text=(windows[i].visible?"Masquer ":"Afficher ")+names[i];
        }
    }
}
