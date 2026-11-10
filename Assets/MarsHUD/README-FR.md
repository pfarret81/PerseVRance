# Mars HUD - Lot 1 : consultation VR

Livraison source pour le projet Unity 6000.6.7f1, XR Interaction Toolkit 3.6.1, Input System et URP existants.
Aucune compilation Unity, execution VR ou mesure de performance n'a ete effectuee par l'assistant.

## 1. Installation

1. Quitter le mode Play. Faire une sauvegarde ou un commit du projet.
2. Extraire le dossier Assets/MarsHUD de l'archive dans le dossier Assets du projet. Ce sont de nouveaux fichiers : ne remplacer aucun fichier XR, rover, scene ou input existant. Si Assets/MarsHUD existe deja, comparer avant de remplacer quoi que ce soit.
3. Attendre la compilation. Ne pas installer un package supplementaire. Les dependances utilisees sont Unity UI (deja utilise par l'EventSystem), Input System et XR Interaction Toolkit.
4. Dans le menu Unity, choisir GameObject > Mars HUD > Create consultation HUD.
5. Selectionner le nouvel objet MarsHUD, a la racine de la scene, hors XR Origin et hors rover. Le constructeur n'ajoute ni EventSystem, ni camera, ni RenderTexture. Il ne modifie pas les objets deja presents.
6. Dans Mars HUD Controller, assigner Head a XR Origin (XR Rig) > Camera Offset > Main Camera si ce n'est pas deja fait.
7. Laisser Catalog vide pour le premier essai : un catalogue de test sera cree en memoire. Ne pas assigner un catalogue vide par erreur.
8. Laisser Rover Marker Source vide pour le premier essai. Facultativement, y assigner un Transform reel representant le rover. Le carre jaune affiche exactement ce Transform, pas necessairement le centre mecanique nominal. Aucune connexion au follower n'est effectuee.
9. Conserver l'EventSystem et son XR UI Input Module existants. Conserver les interacteurs du template.
10. Lancer Play avec Quest Link. Les panneaux sont construits a l'execution, puis detruits a la fin de Play : c'est normal de ne pas avoir de Canvas enfant persistant dans la scene en edition.

Le constructeur initialise ses references dans Start. Une erreur de compilation empeche cette initialisation : envoyer la premiere erreur Console complete avant toute modification aleatoire des packages.

## 2. Commandes

- Y du controleur gauche : entrer/sortir du menu, par le binding de prototype <XRController>{LeftHand}/secondaryButton.
- F8 clavier : meme action, disponible quand Menu Action n'est pas assigne.
- Rayon du template + gachette : boutons et selection UI.
- Agrandir : focus exclusif de la carte.
- Gachette maintenue + mouvement du rayon sur la carte agrandie : deplacement du fond.
- + / - : zoom autour du centre de carte (pas autour du pointeur).
- Recentrer : reprend le suivi cartographique de l'astronaute dans la carte agrandie.
- Couche : couche suivante du theme. Le stub Atmosphere contient deux couches pour tester ce bouton.
- Masquer : ferme la fenetre ; les boutons de visibilite du bandeau permettent de la rouvrir.
- Reduire : retour compact. Il faut reduire avant d'agrandir une autre carte.

Le binding Y est un choix de prototype configurable. L'analyse de l'asset fourni a trouve PrimaryButton droit utilise pour Jump, et aucun binding PrimaryButton gauche, SecondaryButton ou MenuButton. Cela ne prouve pas l'absence de bindings dans d'autres assets ou scripts. Si Y a un autre effet, vider Menu Binding et assigner une InputActionReference dediee via Menu Action, ou choisir un autre binding confirme libre. Changer les bindings avant Play. Aucun asset XRI existant n'est modifie.

Le joystick n'est pas utilise pour naviguer dans les cartes. Aucune action de locomotion n'est desactivee : on peut encore se deplacer pendant le menu. Le panneau reste alors a son ancrage dans le monde. Pour le recuperer apres s'en etre eloigne, fermer le menu, attendre que le HUD revienne, puis le rouvrir. Ce lot ne verrouille pas le deplacement du joueur et ne garantit pas qu'aucun autre objet interactif de la scene ne reagisse a une entree partagee.

## 3. Etats et espace

Au demarrage : trois fenetres visibles et compactes. Pas d'etat epingle separe : pour ce lot, rester visible est represente par l'etat compact ou agrandi ; fermer une fenetre est le choix contraire.

Hors menu : le Canvas suit progressivement la position ET la rotation du casque. Toute l'UI est non interactive. Les cartes compactes suivent l'astronaute avec le nord en haut.

Menu ouvert : la pose courante du Canvas est conservee dans le monde. Tourner, pencher ou translater la tete ne deplace pas les panneaux. Les donnees et marqueurs continuent de se mettre a jour.

Carte agrandie : une seule. Les autres restent visibles, compactes et plus petites sur les cotes. Leurs boutons Couche et Masquer restent utilisables ; Agrandir est desactive tant que la carte active n'est pas reduite. Seule la carte agrandie accepte le glissement et le zoom. Une carte agrandie commence centree sur l'astronaute ; le premier glissement suspend ce suivi, Recentrer le retablit.

Fermer le menu reduit la carte active, conserve les choix de visibilite et reprend le suivi spatial en douceur. Le deplacement manuel et le zoom d'une ancienne vue agrandie sont reinitialises lors du prochain agrandissement. Aucun choix ne persiste entre sessions.

Le panneau est rendu comme de l'UI dans le monde : il n'est pas un overlay systeme garanti visible au travers du terrain. Aucune modification de gravite ou de profondeur globale du rendu n'est faite.

## 4. Valeurs initiales du prototype

- Distance au casque : 1,7 unite monde, supposee egale a 1,7 m dans le XR Origin existant.
- Echelle du Canvas : 0,001 ; largeur totale 1500 px, soit 1,5 unite monde.
- Decalage vertical : -0,08 unite monde.
- Constante de suivi exponentiel : 0,25 s (ce n'est pas un delai fixe).
- Largeur cartographique compacte : 160 m.
- Limites de zoom : 10 a 1500 m de largeur.
- Emprise du stub : 1000 x 1000 m.
- Rayon connu initial : 80 m autour du depart.
- Mise a jour des cartes et marqueurs : 10 Hz ; suivi spatial : LateUpdate.
- Maillage de fond et masque : grille 36 x 36. Frontiere de decouverte conservative par cellules, non pixel-perfect.

Ces choix ne sont pas des contraintes de mission ni des valeurs scientifiques. Les noms distanceMeters et verticalOffsetMeters supposent une echelle XR de 1 unite monde par metre ; World Units Per Meter du repere cartographique ne redimensionne pas le XR Origin.

## 5. Remplacer le stub par tes ressources

1. Hors Play, choisir Assets > Create > Mars HUD > Example stub catalog. Un asset de catalogue pre-rempli est cree sans ecraser un asset existant.
2. L'assigner au champ Catalog du HUD.
3. Renseigner son emprise Extent Center / Extent Size dans le repere de donnees. X = est, Y = nord. Les metres sont un repere local configurable, pas un georeferencement deja valide.
4. Pour chaque couche, choisir son theme et renseigner Display Name, Units, Legend Low/High, Time Or Depth et Provenance avec les metadonnees reelles.
5. Assigner Color Texture : image deja coloree. Le lot 1 ne decode pas un DEM numerique et ne calcule pas de hillshade. Une texture blanche/grise de DEM ne devient pas automatiquement une carte topographique interpretee.
6. Texture North At Top = vrai : v=1 correspond au nord ; mettre faux si l'image est inversee verticalement.
7. Les champs Low Color / High Color definissent l'echelle du fond synthetique et la barre de legende. Pour une image deja coloree, ils doivent correspondre a sa legende. Le systeme ne deduit pas l'echelle d'une image et n'assure pas une legende non lineaire ou multicategorie dans ce lot.
8. Valid Mask optionnel : image dont l'alpha vaut 0 pour les zones sans donnees et 1 pour les zones valides. Activer Read/Write sur CE masque dans ses import settings. Son emprise/orientation doit correspondre a Color Texture. Un masque non lisible desactive la couche concernee plutot que d'ignorer les trous silencieusement.
9. Pour les images sans transparence utile, utiliser une texture opaque. Si une image source a des trous transparents, fournir aussi le masque explicite : leur signification n'est pas inferee du rendu.

Sans Color Texture, la couche utilise un champ synthetique colore par sinus/cosinus. Garder alors une provenance TEST explicite. Cela ne produit aucune mesure scientifique et ne constitue pas une source exploitable pour les missions.

## 6. Coordonnees et couverture

MarsMapCoordinates est le seul passage entre monde et donnees :

    monde = origine + rotationY(northYaw) * (est, hauteur, nord) * worldUnitsPerMeter

- Sans Origin Reference, l'origine est la position du casque au demarrage.
- Avec Origin Reference, seule sa position initiale est utilisee. Sa rotation et son echelle ne sont pas implicitement reutilisees ; North Yaw Degrees fixe l'orientation.
- Le repere est capture au demarrage. Deplacer l'origine ensuite ne recale pas automatiquement les cartes.
- WorldToData renvoie les deux coordonnees horizontales. DataToWorld accepte une hauteur relative explicite.
- Une coordonnee donnee par DataToWorld ne constitue pas un contact terrain ou une destination valide pour le rover.
- Aucune transformation geographique, reprojection SIG ou correction de decalage raster/terrain n'est incluse.

Le masque de decouverte est separe du catalogue et du rendu. Le lot 1 ne connait qu'un disque initial commun Topographie/Atmosphere. Il ne revele pas de nouvelles zones lorsque l'astronaute se deplace. Le Sol reste inconnu, faute de prelevement. La couleur sombre signifie inconnu, violet signifie connu mais sans donnees, quasi noir signifie hors emprise. Un triangle cyan indique la vraie position projetee du casque et sa direction horizontale ; un carre jaune facultatif indique le Transform rover assigne. Les marqueurs hors de la vue sont omis, pas ramenes artificiellement au bord.

Une future acquisition devra mettre a jour MarsDiscoveryState et son Version, pas simplement deplacer une icone. Le lot 1 ne fournit pas encore l'API d'acquisition, de prelevement ou la persistance.

## 7. Verification regroupee dans Unity et le casque

A. Compilation : aucune erreur apres import ; un seul MarsHUD a la racine ; Head correctement assigne. Aucun composant XR/rover remplace.
B. Entree : Y ou action configuree ouvre le menu ; le rayon et la gachette activent les boutons. Si F8 fonctionne mais pas Y, diagnostiquer le binding OpenXR. Si le menu ouvre mais pas les clics, examiner uniquement les reglages UI du Near-Far Interactor et leurs actions, pas le rover.
C. Espace : ouvrir le menu, tourner la tete, se pencher et faire un petit pas : les panneaux doivent rester au meme endroit du monde. Fermer : ils reprennent leur suivi progressif sans saut impose.
D. Fenetres : masquer les trois, les rouvrir via le bandeau, agrandir une carte, verifier que les autres ne peuvent pas etre agrandies, reduire puis changer de carte. La touche HUD reste disponible meme quand toutes les fenetres sont cachees.
E. Carte : glisser seulement en grand format ; aucune navigation en compact ; tester +, -, Recentrer ; le nord reste en haut pendant la rotation du casque. Changer entre les deux couches Atmosphere de test.
F. Donnees : zone initiale connue seulement ; aller au-dela de cette zone ne revele rien ; carte Sol inconnue. Tester une texture identifiable aux quatre coins, une emprise non carree et un masque avec un trou connu. Les labels et unites doivent correspondre au catalogue, pas a une valeur inventee.
G. Recalage : utiliser le menu contextuel Check coordinate round trip en Play sur MarsMapCoordinates ; il ne prouve que l'aller-retour mathematique. Verifier ensuite au moins trois reperes physiques non alignes entre raster et terrain. Le recalage reel est a faire avec tes ressources, pas avec le stub.
H. Regression : le HUD ne commande pas le rover ; aucun second script ne modifie RoverRoot. Pas besoin de relancer tous les tests mecaniques pour ce lot, sauf anomalie nouvelle.
I. Casque : verifier taille des textes, profondeur, eblouissement, panneau lateral et confort du suivi pendant des mouvements normaux. Ajuster les reglages avant de figer le style.

Pour les performances, comparer sur la meme scene et le meme casque : objet HUD desactive / HUD compact / menu avec carte agrandie. Observer separément CPU, UI/Canvas, GPU et allocations GC, apres l'initialisation. Le budget total correspondant a 72 Hz est environ 13,89 ms par image : ce n'est pas un budget integralement attribuable au HUD. Ce lot n'inclut aucun resultat de mesure. La geometrie n'est pas reconstruite par un Update de chaque fenetre ; elle est invalidee a la frequence configuree quand necessaire. Les overlays de marqueurs sont rafraichis a cette frequence et reconstruisent aussi leur masque : optimisation eventuelle a justifier par profilage.

## 8. Architecture et limites

MarsHUDController : orchestration, construction du Canvas, etats et entrees menu.
MarsMapCoordinates : transformation locale testable.
MarsMapCatalog : couches, emprise, textures et metadonnees.
MarsDiscoveryState : connaissance des zones, sans mouvement de vehicule.
MarsMapGraphic : fond, vue et glissement de carte.
MarsMapOverlay : masque de connaissance et marqueurs issus de Transforms reels.
MarsHUDInstaller : commande d'installation manuelle et creation de catalogue.

Aucune camera supplementaire, RenderTexture, modification de scene automatique a l'import, reflexion sur des champs prives, solveur mecanique, calcul de trajectoire, controle de vol, acquisition scientifique ou sauvegarde n'est ajoute.

Avant d'activer les missions, il faudra un mode explicite de designation distinct du glissement, un contrat de commande/statut, la validation du terrain, puis une extension ciblee du follower. Rien ici ne pretend resoudre les limites existantes du rover.
