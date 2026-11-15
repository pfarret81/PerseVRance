using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 5 - Une etape du fil rouge. Objet vide pose sur le terrain, enfant direct de MarsMissionPlan.
    /// L'ordre des etapes est l'ordre des enfants dans la hierarchie ; le dernier enfant est l'arrivee.
    /// Deplacer l'objet dans la vue Scene suffit a redefinir l'etape (aucune donnee liee au rover).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarsMissionCheckpoint : MonoBehaviour
    {
        public enum Requirement
        {
            [Tooltip("Perseverance doit realiser un prelevement dans le rayon.")] PerseveranceSample,
            [Tooltip("Perseverance doit entrer dans le rayon.")] PerseveranceArrival,
            [Tooltip("L'astronaute doit entrer dans le rayon.")] AstronautArrival
        }

        public string displayName = "Étape";
        [Tooltip("Consigne courte affichee dans la note (facultatif).")]
        public string instruction = "";
        [Min(1f)] public float radiusMeters = 8f;
        public Requirement requirement = Requirement.PerseveranceSample;

        public string RequirementText()
        {
            switch (requirement)
            {
                case Requirement.PerseveranceArrival: return "amener Perseverance";
                case Requirement.AstronautArrival: return "s'y rendre à pied";
                default: return "prélèvement Perseverance";
            }
        }

        void OnDrawGizmos()
        {
            bool arrival = transform.parent && transform.GetSiblingIndex() == transform.parent.childCount - 1;
            Gizmos.color = arrival ? new Color(1f, .85f, .2f, .9f) : new Color(1f, .3f, .25f, .9f);
            Vector3 c = transform.position;
            const int n = 32;
            Vector3 prev = c + new Vector3(radiusMeters, .1f, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * radiusMeters, .1f, Mathf.Sin(a) * radiusMeters);
                Gizmos.DrawLine(prev, next); prev = next;
            }
            Gizmos.DrawLine(c, c + Vector3.up * 4f);
            Gizmos.DrawSphere(c + Vector3.up * 4f, .35f);
        }
    }
}
