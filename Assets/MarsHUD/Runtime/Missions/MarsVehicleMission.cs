using System.Collections.Generic;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>Etat d'un ordre tel qu'affiche par le HUD. Il est derive des systemes reels du vehicule.</summary>
    public enum MarsOrderState
    {
        Unavailable,   // vehicule non pret ou non configure
        Idle,          // aucun ordre HUD en cours
        Planning,      // calcul d'itineraire en cours
        EnRoute,       // trajet verifie adopte par le controleur existant
        Paused,        // pause demandee depuis le HUD
        Working,       // tache a destination (prelevement, releve...)
        Completed,     // tache terminee
        Fault          // defaut du controleur existant
    }

    /// <summary>
    /// Contrat minimal entre le HUD et un vehicule. Le HUD ne deplace jamais un vehicule :
    /// il transmet une destination ; le vehicule planifie, valide puis confie le trajet a son controleur existant.
    /// </summary>
    public interface IMarsVehicleMission
    {
        string VehicleName { get; }
        Color MarkerColor { get; }
        bool IsAvailable { get; }
        MarsOrderState OrderState { get; }
        /// <summary>Ligne d'etat courte, deja formatee pour l'affichage.</summary>
        string StatusLine { get; }
        /// <summary>Dernier message d'acceptation ou de refus (vide si aucun).</summary>
        string LastMessage { get; }
        bool LastMessageIsRefusal { get; }
        bool CanPause { get; }
        bool CanResume { get; }

        bool TryGetPose(out Vector3 position, out Vector3 forward);
        bool HasDestination { get; }
        Vector3 Destination { get; }
        /// <summary>Copie du trajet reellement adopte par le controleur (monde). Vide si aucun.</summary>
        IReadOnlyList<Vector3> Route { get; }
        /// <summary>Indice du premier point non encore parcouru dans Route.</summary>
        int RouteProgressIndex { get; }
        int RouteVersion { get; }

        /// <summary>Demande une destination (monde). Retourne false si la demande est refusee immediatement.</summary>
        bool RequestDestination(Vector3 worldPoint, out string refusal);
        void PauseMission();
        void ResumeMission();
    }
}
