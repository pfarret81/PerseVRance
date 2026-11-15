using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Ferme automatiquement le menu des cartes du HUD quand l'astronaute s'eloigne de l'endroit ou il l'a ouvert.
    /// Utilise uniquement l'API publique du controleur (MenuOpen, Mode, ToggleMenu, Head) : le controleur n'est pas modifie.
    /// Distance horizontale (la hauteur de la tete varie quand on se penche).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsMenuDistanceGuard : MonoBehaviour
    {
        public MarsHUDController hud;
        [Tooltip("Distance horizontale (m) depuis la position de la tete a l'ouverture au-dela de laquelle le menu se ferme.")]
        [Min(.5f)] public float maxDistance = 2.5f;
        bool tracking;
        Vector3 anchor;

        void Awake() { if (!hud) hud = GetComponent<MarsHUDController>(); if (!hud) hud = FindAnyObjectByType<MarsHUDController>(); }

        void Update()
        {
            if (!hud || !hud.Head) return;
            bool open = hud.MenuOpen;
            if (!open) { tracking = false; return; }
            Vector3 head = hud.Head.position;
            if (!tracking) { tracking = true; anchor = head; return; }
            float dx = head.x - anchor.x, dz = head.z - anchor.z;
            if (dx * dx + dz * dz > maxDistance * maxDistance)
            {
                tracking = false;
                hud.ToggleMenu();
            }
        }
    }
}
