using System.Collections.Generic;
using UnityEngine;

namespace MarsHUD
{
    /// <summary>
    /// Lot 6 - Catalogue des missions affichees sur le globe du menu principal. Donnees pures (independantes des
    /// scenes et du rover). Coordonnees planetocentriques, longitude Est positive (0-360 ou -180..180 accepte).
    /// </summary>
    [CreateAssetMenu(menuName = "PerseVRance/Catalogue des missions", fileName = "PerseVRance Missions")]
    public sealed class MarsMissionCatalog : ScriptableObject
    {
        public enum Difficulty { Decouverte, Intermediaire, Expert }

        [System.Serializable]
        public sealed class Mission
        {
            public string id = "mission";
            public string title = "Mission";
            public string site = "Site";
            [Range(-90f, 90f)] public float latitude;
            [Tooltip("Longitude Est (deg). Ouest = negatif.")]
            public float longitudeEast;
            [TextArea(3, 8)] public string description = "";
            [TextArea(2, 6)] public string objectives = "";
            public string duration = "";
            public Difficulty difficulty = Difficulty.Decouverte;
            [Tooltip("Nom de la scene (Build Settings). Vide = mission non jouable.")]
            public string sceneName = "";
            [Tooltip("false = affichee comme \"Bientot disponible\".")]
            public bool available;
            public Color color = new Color(1f, .48f, .22f);
        }

        public List<Mission> missions = new List<Mission>();

        public static string Coordinates(Mission m)
        {
            float lon = Mathf.Repeat(m.longitudeEast + 180f, 360f) - 180f;
            return $"{Mathf.Abs(m.latitude):F2}\u00b0 {(m.latitude >= 0f ? "N" : "S")}  \u00b7  {Mathf.Abs(lon):F2}\u00b0 {(lon >= 0f ? "E" : "O")}";
        }

        public static string DifficultyLabel(Difficulty d) =>
            d == Difficulty.Decouverte ? "D\u00e9couverte" : d == Difficulty.Intermediaire ? "Interm\u00e9diaire" : "Expert";

        /// <summary>Contenu par defaut (utilise par l'outil d'installation, ou a l'execution si aucun catalogue n'est assigne).</summary>
        public void FillDefaults(string jezeroScene)
        {
            missions.Clear();
            missions.Add(new Mission
            {
                id = "jezero", title = "Jezero \u2014 Sortie extrav\u00e9hiculaire", site = "Crat\u00e8re Jezero, bordure ouest d'Isidis Planitia",
                latitude = 18.38f, longitudeEast = 77.58f,
                description = "Vous sortez aux c\u00f4t\u00e9s de Perseverance dans l'ancien lac du crat\u00e8re Jezero. Ingenuity cartographie le terrain " +
                              "et les a\u00e9rosols ; Perseverance se d\u00e9place et pr\u00e9l\u00e8ve le sol sur vos ordres depuis le HUD.",
                objectives = "\u2022 Suivre le fil rouge jusqu'\u00e0 l'arriv\u00e9e\n\u2022 Faire explorer la zone par Ingenuity\n\u2022 Envoyer Perseverance et analyser le sol",
                duration = "20 \u00e0 30 min", difficulty = Difficulty.Decouverte, sceneName = jezeroScene, available = true,
                color = new Color(1f, .48f, .22f)
            });
            missions.Add(new Mission
            {
                id = "gale", title = "Gale \u2014 Aeolis Mons", site = "Crat\u00e8re Gale (site de Curiosity)",
                latitude = -5.4f, longitudeEast = 137.8f,
                description = "Mission \u00e0 venir : ascension des strates s\u00e9dimentaires du mont Sharp.",
                objectives = "\u00c0 d\u00e9finir", duration = "\u2014", difficulty = Difficulty.Intermediaire, available = false,
                color = new Color(.55f, .75f, 1f)
            });
            missions.Add(new Mission
            {
                id = "olympus", title = "Olympus Mons", site = "Tharsis, plus haut volcan du syst\u00e8me solaire",
                latitude = 18.65f, longitudeEast = 226.2f,
                description = "Mission \u00e0 venir : reconnaissance des falaises de la base du volcan.",
                objectives = "\u00c0 d\u00e9finir", duration = "\u2014", difficulty = Difficulty.Expert, available = false,
                color = new Color(.55f, .75f, 1f)
            });
            missions.Add(new Mission
            {
                id = "valles", title = "Valles Marineris", site = "Canyon \u00e9quatorial, Coprates Chasma",
                latitude = -13.9f, longitudeEast = -59.2f,
                description = "Mission \u00e0 venir : descente dans le plus grand canyon de Mars.",
                objectives = "\u00c0 d\u00e9finir", duration = "\u2014", difficulty = Difficulty.Expert, available = false,
                color = new Color(.55f, .75f, 1f)
            });
        }
    }
}
