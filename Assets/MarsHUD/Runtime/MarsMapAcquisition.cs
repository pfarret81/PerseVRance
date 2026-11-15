using UnityEngine;

namespace MarsHUD
{
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MarsHUDController))]
    public sealed class MarsMapAcquisition : MonoBehaviour
    {
        public Transform acquisitionSource;
        [Tooltip("Laisser desactive avant integration d'une vraie source d'exploration.")]
        public bool automaticAcquisition=false;
        [Min(.01f)] public float footprintRadiusMeters=50f;
        [Range(.2f,20f)] public float acquisitionHz=5f;
        [Tooltip("Espacement minimal entre nouvelles empreintes ; pas une interpolation de vol.")]
        [Min(.1f)] public float minSourceTravelMeters=10f;
        MarsHUDController hud;
        float nextAcquisition;
        Vector2 lastPosition;
        bool hasLastPosition;
        Transform lastSource;
        public string LastRefusal { get; private set; }="";
        void Start() { hud=GetComponent<MarsHUDController>(); }
        void Update()
        {
            if(!automaticAcquisition || !acquisitionSource || Time.unscaledTime<nextAcquisition) return;
            if(!hud || !hud.Ready) return;
            nextAcquisition=Time.unscaledTime+1f/Mathf.Max(.2f,acquisitionHz);
            if(lastSource!=acquisitionSource) { hasLastPosition=false; lastSource=acquisitionSource; }
            Vector2 p=hud.Coordinates.WorldToData(acquisitionSource.position);
            if(hasLastPosition && (p-lastPosition).sqrMagnitude<minSourceTravelMeters*minSourceTravelMeters) return;
            if(AcquireAtWorld(acquisitionSource.position)) { hasLastPosition=true; lastPosition=p; }
            else { automaticAcquisition=false; Debug.LogWarning("Acquisition suspendue : "+LastRefusal,this); }
        }
        public bool AcquireAtWorld(Vector3 world)
        {
            if(!hud) hud=GetComponent<MarsHUDController>();
            if(!hud || !hud.Ready) { LastRefusal="HUD non initialise."; return false; }
            if(float.IsNaN(world.x) || float.IsInfinity(world.x) || float.IsNaN(world.y) || float.IsInfinity(world.y) ||
               float.IsNaN(world.z) || float.IsInfinity(world.z)) { LastRefusal="Position non finie."; return false; }
            Vector2 p=hud.Coordinates.WorldToData(world);
            if(!hud.Catalog.Contains(p)) { LastRefusal="Centre d'acquisition hors emprise cartographique."; return false; }
            bool accepted=hud.Discovery.TryRevealDisc(p,footprintRadiusMeters);
            LastRefusal=hud.Discovery.LastRefusal;
            return accepted;
        }
        [ContextMenu("TEST Play - acquire at configured source")]
        void TestAcquire()
        {
            if(!Application.isPlaying) { Debug.LogWarning("Test d'acquisition uniquement en Play.",this); return; }
            if(!acquisitionSource) { Debug.LogWarning("Assigner une source de TEST explicite ; aucun acteur choisi automatiquement.",this); return; }
            if(!AcquireAtWorld(acquisitionSource.position)) Debug.LogWarning(LastRefusal,this);
        }
    }
}
