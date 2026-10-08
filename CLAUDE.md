# Exarion — contexte du projet

> Ce fichier est rechargé automatiquement à chaque session. Il contient ce qu'il ne faut
> **jamais** avoir à redécouvrir. Le détail vit dans `DESIGN.md`, `ROADMAP.md` et
> `DEPENDENCIES.md` — les lire avant toute décision d'architecture.

---

## Le projet en trois lignes

**Exarion** : un **moteur 3D écrit à la main** en C# / .NET 9 + OpenGL 3.3 via Silk.NET,
taillé pour un FPS tactique 5v5 façon Counter-Strike. Développeur **seul**.

⚠️ **Le livrable est le MOTEUR, pas le jeu** (tranché le 16/08 — voir le périmètre plus bas).
Le moteur doit rester **commercialisable** : c'est de là que vient la règle n°2 sur les
licences, et elle ne bouge pas.

## Règles non négociables

1. **`Exarion.Core` ne référence AUCUN moteur, aucune bibliothèque graphique.**
   Toute logique de jeu et de physique y vit et s'y teste en ligne de commande.
   Cette règle a déjà sauvé le projet **trois fois** (voir historique ci-dessous).

2. **Aucune dépendance sans passer par `DEPENDENCIES.md` d'abord.**
   Licence vérifiée sur le dépôt officiel, usage commercial ET distribution binaire
   autorisés. Copyleft (GPL/LGPL/AGPL) → on s'arrête et on en discute.
   *ImageSharp a été écartée pour ça (licence Six Labors Split).*

3. **« Ça doit être MON truc à moi ».** Aucun code copié d'un moteur existant. On
   implémente à partir de techniques publiques et documentées. Code commenté **en
   français**, compréhensible et reprenable seul.

4. **Rien n'est « fait » sans preuve.** Un test qui passe, une mesure, ou une capture
   d'écran. « Ça compile » ne veut rien dire.

5. **On pousse sur GitHub avant de passer à la suite.** Dépôt de développement **privé** :
   <https://github.com/Lionsvg/Exariondev> (renommé le 07/10/2026, c'était « Exarion »). Une
   étape terminée et vérifiée se commite et se pousse — pas de travail qui ne vit que sur le
   disque.
   ⚠️ L'adresse mail du dépôt est `196537507+Lionsvg@users.noreply.github.com`, réglée
   **localement**. GitHub refuse tout commit exposant l'adresse privée : ne pas la remettre.

   ⚠️⚠️ **<https://github.com/Lionsvg/Exarion> est désormais le dépôt PUBLIC, et il ne reçoit
   JAMAIS l'historique de développement** : celui-ci contient `Model3D` (modèle Meshy dont la
   redistribution n'est pas tranchée), et un historique git garde même ce qu'on a supprimé.
   On y publie un **instantané** avec `pwsh tools\publier.ps1 -Version x.y.z` : export de HEAD
   par `git archive` (exclusions dans `.gitattributes`), compilation et tests **sans** Model3D,
   puis un commit unique + un tag. Ajouter `-SansPousser` pour tout vérifier sans rien envoyer.
   ⚠️ **Vérifier que `origin` pointe sur `Exariondev`** — juste après le renommage il pointait
   encore sur l'URL devenue publique ; le script refuse de tourner dans ce cas.

   **Licence publique : Apache 2.0** + fichier `NOTICE` (07/10/2026). La demande était « ils
   peuvent vendre, modifier et tout, mais c'est obligatoire de m'avoir en crédit » : Apache
   l'impose par sa section 4(d) — le `NOTICE` doit suivre toute redistribution. ⚠️ Elle
   n'impose PAS un crédit **à l'écran** : un fichier texte livré avec le jeu suffit.

   📏 **v0.1.0 publiée le 07/10/2026** (release, code source seul, pas d'exécutable — choix de
   l'utilisateur). Le zip téléchargé depuis GitHub, décompressé dans un dossier vierge :
   **563 tests réussis, 22 ignorés, 0 échec** — les 22 sont ceux du personnage. 151 fichiers,
   aucun `.glb`. Bac à sable, carte à PNJ et jeu d'exemple lancés depuis l'instantané : capture
   écrite, aucun plantage.
   ⚠️⚠️ **Sans Model3D, les PNJ et les joueurs distants sont INVISIBLES** : simulés (le HUD
   montre le garde à 2,2 m/s) mais `Program.cs` sort avant de les dessiner quand
   `_characterModel` est nul. Constaté sur la version publique, documenté dans le README.
   Il manque un rendu de repli — la silhouette de primitives de l'éditeur existe déjà.

## Historique — pourquoi on en est là

| Moteur | Écarté parce que |
|---|---|
| s&box | ~140 joueurs simultanés mesurés sur toute la plateforme |
| Source SDK 2013 | licence **non commerciale** : le F2P + skins est interdit |
| Unity 6 + FishNet | viable et légal — écarté par **choix** : moteur maison |

Ce qui a survécu aux trois changements : la doc, et la logique de jeu en C# pur.
**C'est la preuve que la règle n°1 est la bonne.**

## État vérifié (dernière mise à jour : 08/10/2026)

```
703 tests au vert  ·  0 erreur  ·  0 avertissement dans src/
0,557 ms de GPU par image (1280x720)  ·  64 pas/s  ·  physique 0,17 ms au repos
Program.cs : 84 lignes (5 156 avant la refonte du 07/10)
```

## Refonte du 07/10/2026 — le bac à sable éclaté, sans changer UN pixel

**Program.cs faisait 5 156 lignes**, une seule classe statique, ~140 champs statiques, et une
méthode `RegisterConsole` de 1 000 lignes. Il fait 84 lignes : `Main` et le rapport de plantage.
Tout le reste est dans `src/Exarion.Game/`, une responsabilité par dossier :

| Dossier | Contenu |
|---|---|
| `SandboxApp.cs` | **orchestre** : crée les sous-systèmes, déroule chargement / pas / image dans l'ordre d'origine |
| `World/` | `GameWorld` (carte, décor, physique, joueur, entités, PNJ, portes — aucun appel GL), `SandboxMap`, `EventFeed` |
| `Gameplay/` | `PlayerController` (regard, entrée, tir, caisses), `SandboxFx` (particules + générateur unique), `Aim` |
| `Editing/` | `EditorController`, `EditorView` (aides, surcouche, panneau), `EditorGrid` |
| `Rendering/` | `SandboxScene` (`IWorldRenderer`), `CharacterRig`, `MarkerRenderer`, `SceneLighting`, `RenderSettings` |
| `Ui/` | `MenuScreen`, `SandboxHud`, `Screenshots` |
| `Sound/`, `Networking/`, `Input/`, `Diagnostics/` | `SandboxAudio`, `NetSession`, `InputDevices`, `PerfMonitor` |
| `Commands/` | la console, **une famille par fichier** (`PlayerCommands`, `RenderCommands`, `EditorCommands`…) |

`MapLibrary` (dossier des cartes, noms de fichiers sûrs) est descendu dans **Core**, avec 11 tests.

📏 **La preuve est l'outil `tools/ab-captures.ps1`**, écrit pour l'occasion : huit scénarios
headless, l'inventaire COMPLET de la console (valeur, défaut, bornes, description de chaque
entrée) et une session scriptée de 70 commandes, comparés à une copie figée du dépôt (`git
worktree`). Après les 9 étapes, contre le commit d'avant la refonte : **7 scénarios identiques
au bit près, console identique ligne à ligne.** Chaque étape a été commitée et poussée séparément.

⚠️⚠️ **L'ORDRE D'INITIALISATION DES CHAMPS STATIQUES ÉTAIT UN PIÈGE ACTIF.** Pendant la refonte,
un champ comme `_world = new GameWorld(_fx.Particles)` placé AVANT `_fx` dans le fichier
recevait `null` en silence. `SandboxApp` est une INSTANCE qui crée tout dans son constructeur,
dans un ordre explicite : le piège a disparu avec les champs statiques.

### Sept défauts trouvés EN DÉPLAÇANT le code — tous corrigés, tous mesurés

| Défaut | Depuis | Preuve |
|---|---|---|
| **L'occlusion sonore ne voyait AUCUN mur** : `AudioWorld.World` gardait le monde vide du démarrage | 4.2 | 6 kHz derrière un pilier : RMS 0,0299 / 0,0301 (×1,0) → **0,0017 / 0,0307 (×18)** |
| **`gravity` et `phys_*` perdus au changement de carte** : le joueur à 20, les caisses à 15,24 | R4 | commande `phys` avant / après `map_load` |
| **Les `--exec` se taisaient après 200 lignes** (tampon de console borné) | 2.4 | 126 `help` → 66 réponses avant, **126** après |
| **Les 4 caisses et le mannequin sur TOUTES les cartes** (le « pilier noir ») | 17/08 | `portes` : CAISSES 4 → 0, la porte redevient visible |
| **Les PNJ couraient À RECULONS** : pas de demi-tour modèle/moteur, contrairement aux joueurs distants | E14 | capture : un garde en poursuite montrait son DOS |
| **Sans modèle, PNJ et joueurs distants INVISIBLES** | E14 | silhouette de primitives, ombre comprise |
| **Ombres de PNJ dans l'éditeur**, où les corps sont cachés | E14 | passe d'ombre et passe finale lisent la même liste |

⚠️ **Le deuxième et le troisième sont le MÊME piège que les ConVar de la SSAO et de
`snd_volume`** : un objet recréé (monde physique, monde de collision) que la valeur réglée ne
suit pas. La commande **`phys`** affiche les réglages EFFECTIFS, lus sur le monde courant — c'est
l'écart entre elle et la ConVar qui trahit le défaut.

⚠️ **L'outil A/B a lui-même menti DEUX fois**, et c'est noté pour ne pas recommencer. Sa première
version annonçait l'inventaire de la console « identique » alors qu'elle n'en comparait que 66
entrées sur 125 (le tampon de 200 lignes, encore) : il vérifie désormais que la DERNIÈRE commande
de chaque lancement a répondu. Puis, le 08/10 : un écart de **pixels** s'affichait en jaune sans
compter dans le code de sortie — l'outil rendait 0 sur une image différente. Il rend maintenant 2
dès qu'un écart existe, bruit des particules compris : le code dit qu'il y a quelque chose à
juger, chaque écart se juge contre `-Bruit`.

✅ **Nouveaux types d'entités** : `prop_crate` (`size`, `mass`, `count` — une pile posée au sol)
et `prop_mannequin`. Éditeur : **F8** caisses, **F9** mannequin. Le décor intégré les déclare
aux coordonnées d'avant, d'où des captures identiques au bit près.

✅ **Le moteur a un SECOND client depuis le 17/08** — `samples/Exarion.Sample`, 187 lignes,
qui ne référence que `Exarion.Core` et `Exarion.Engine`. Vérifié par capture : carte chargée,
ciel, ombres, lumières ponctuelles, occlusion de contact, textures, interface. C'est la seule
preuve que le moteur est utilisable de l'extérieur — voir le chantier « utilisable » plus bas.

✅ **macOS est vérifié (16/08)** — Apple M3, OpenGL 4.1 Metal, miniaudio sur la sortie
intégrée à 48 000 Hz, 60 img/s en VSync. L'OpenGL 3.3 déprécié depuis 2018 passe sans
histoire. **Linux reste non testé.**

✅ **Le son 3D a été écouté par un humain (16/08).** La première ligne de la dette de
vérification ci-dessous est donc levée. Chaîne mesurée en plus de l'écoute : `snd_test 440 0`
→ pic 0,2121 des deux côtés, **440,0 Hz** relevés par comptage de passages à zéro sur le WAV.

---

# ⚠️⚠️ CE QUI RESTE À TESTER — LIRE CECI EN PREMIER

> Passation du **16/08/2026**. Les phases 1 et 2 sont terminées et poussées, mais **une partie
> du travail n'a jamais été validée par un humain**, et je n'ai aucun moyen de le faire à sa
> place. Cette liste est la dette de vérification, pas la dette de code.

## 1. Rien de tout ça n'a été écouté ni regardé par quelqu'un

| Sujet | Comment le tester | Réglages |
|---|---|---|
| ~~**Son 3D**~~ | ✅ **écouté le 16/08, tout marche.** Reste à juger finement l'occlusion et l'atténuation en jeu | `snd_volume`, `snd_ref`, `snd_max`, `snd_rolloff`, `snd_doppler`, `snd_occlusion` |
| **Impacts et explosions** | Tirer, appuyer sur **G** | `fx_impact`, `particles` |
| **Mélange marche ↔ course** | `character_blend` de 0 à 1 en direct | `character_blend`, `character_offset`, `character_speed` |
| **SSAO et bloom** | Jamais validés à l'œil depuis leur réglage initial | `ssao_strength`, `ssao_radius`, `bloom_intensity`, `bloom_threshold` |
| **Glissement des caisses** | Lancer une caisse : 3,17 m à 8 m/s (la physique dit 3,9). Trop ? pas assez ? | `friction`, `crate_mass` |

⚠️ **`character_offset` est le plus susceptible d'être faux.** Rien ne garantit que « Walking »
et « Running » démarrent sur le **même pied** — c'est une propriété du contenu, pas du moteur.
Si la jambe d'attaque saute quand on bouge `character_blend`, mettre `character_offset 0.5`.

`save` écrit les valeurs trouvées dans `exarion.cfg`, rechargé au démarrage.

## 2. Écrit et testé unitairement, JAMAIS exercé en vrai

- **Carte son à 44 100 Hz.** Tout a été mesuré sur une seule carte à 48 000. Le rééchantillonnage
  est testé unitairement, le chemin réel ne l'a jamais été.
- ✅ ~~**Mac**~~ — **validé par un humain le 16/08** : Apple M3, OpenGL 4.1 Metal, miniaudio sur
  la sortie intégrée à 48 000 Hz, 60 img/s. L'OpenGL déprécié depuis 2018 passe sans histoire.
  ⚠️ **Linux reste non testé** : les binaires natifs sont dans le paquet (vérifié), rien n'y a
  jamais été lancé.
- **`Animator.CrossFadeTo`.** Le fondu enchaîné a ses tests, mais aucune situation de jeu ne
  l'appelle. Sa limite est connue : il part d'une **pose figée**, visible au-delà de 0,5 s.
- **Débordement du réservoir de particules.** `Dropped` est testé, jamais atteint en jeu.

✅ ~~**Les 4 caisses de départ ne font PAS partie de la carte.**~~ **Corrigé le 07/10** : une
carte déclare ses caisses (`prop_crate`) et son mannequin (`prop_mannequin`), le décor intégré
déclare les siens. Voir la refonte du 07/10 en tête de fichier.

## 3. Non mesuré — et je le dis plutôt que de laisser croire

- **Coût CPU de soumission du rendu.** `gpu 1` mesure le **GPU seul**. Une passe peut être
  quasi gratuite pour lui et coûteuse à préparer.
- **Coût CPU du mélangeur audio.**
- **Une seule machine, une seule résolution** : RTX 5060 Ti, 1280×720. Rien ne dit ce que ça
  donne sur un GPU d'entrée de gamme en 1440p.
- **`light_sun 0` ne coupe PAS la passe d'ombre** : l'A/B sur les ombres n'a donc jamais été
  fait. Le 0,022 ms vient d'une mesure directe.
- **Version exacte de miniaudio** embarquée dans `Hexa.NET.MiniAudio` : le paquet ne la publie pas.

## 4. Deux décisions qui attendent, et que je n'ai pas prises

1. **Code orphelin.** La phase 5 étant supprimée, `RoundMachine`, `IRoundWorld`, `RoundTypes`,
   `Economy` et `BuyCatalog` — plus `RoundMachineTests` et `EconomyTests` — n'ont plus de
   destination. **Garder, archiver, ou supprimer ?** Ils sont le témoin historique de la règle
   n°1 (ils ont survécu à trois changements de moteur).
   ⚠️ **Ne pas toucher** à `Arsenal`, `Ballistics` (le bac à sable tire avec) ni à
   `CombatTarget.RayVsAabb` (utilisé par `PhysicsWorld.Raycast`).

2. **Le réseau est-il encore dans le périmètre ?** La phase 3 — prédiction client,
   réconciliation, lag compensation — est du netcode de **jeu**, très spécifique à un FPS
   compétitif. Maintenant que le livrable est le moteur 3D, la **phase 4** (format de map,
   éditeur) est peut-être plus cohérente à faire d'abord. **Question posée, jamais tranchée.**

## 5. Limites acceptées, à ne PAS traiter comme des bugs

- **Avant/arrière indistinguables** en audio. Limite du panoramique d'amplitude, pas un défaut.
  Un test (`L_Avant_Et_L_Arriere_Sont_INDISTINGUABLES`) la fige exprès. Il faudrait une HRTF.
- **Pas de réverbération** : un couloir sonne comme l'extérieur.
- **WAV uniquement**, pas d'Ogg ni de MP3.
- **Pas de texture de particule** : le disque est calculé dans le nuanceur.
- **30 avertissements** dans le projet de **tests** (analyseurs NUnit). Recomptés le 08/10 et
  attribués par `git blame` : 26 du 16/08, 4 du 17/08 — aucun plus récent. `src/` est bien à
  **zéro**. ⚠️ Un nouveau test n'en ajoute AUCUN : `TestContext.Out.WriteLine`, pas
  `TestContext.WriteLine` (NUnit1033), et `Assert.That(…, Is.Empty)` plutôt que `CollectionAssert`.

## 6. Quatre pièges qui m'ont eu DANS CETTE SESSION

1. **`--no-build`.** J'ai passé six étapes à diagnostiquer une fumée « toujours blanche » que
   j'avais déjà corrigée : le binaire était périmé. **Ne jamais l'utiliser**, même derrière un
   `dotnet build` réussi dans la même commande. ⚠️ `lancer.bat` le faisait lui-même — compilation
   puis `dotnet run --no-build` — jusqu'au 08/10 ; retiré, et vérifié par un lancement avec capture.
2. **Un test écrit à mélange 0,5 ne prouve rien sur le slerp** — à mi-chemin, linéaire et
   sphérique donnent le même résultat. Mon test passait avec une implémentation fausse.
3. **Une mesure contaminée.** Mon atténuation audio donnait 16,9× au lieu de 4× : la source
   lointaine était derrière un mur, le chiffre mélangeait distance et occlusion.
4. **Diviser par le nombre d'images au lieu du nombre de mesures.** Le profileur GPU annonçait
   0,005 ms par image à 1 530 img/s. Le signal : les chiffres **baissaient** quand ça accélérait.

---

⚠️ Le projet **de tests**, lui, sort 30 avertissements (analyseurs NUnit qui suggèrent
`Assert.That` au lieu de `ClassicAssert`). Ils sont antérieurs et sans effet sur le moteur.

**Acquis** : boucle à pas fixe déterministe · collision AABB + déplacement Quake ·
physique dynamique (caisses, piles, explosions, frottement de Coulomb) · lancer de rayon
et zones de dégâts · éclairage PBR Cook-Torrance · ombres (shadow mapping + PCF) ·
textures + PNG **maison** · ciel · MSAA · ACES · texte bitmap · console de debug ·
captures d'écran.

**Absent** : le tir n'est pas encore arbitré par le serveur. Aucun contenu de jeu — et c'est **désormais volontaire**, voir le
périmètre ci-dessous.

**Le décor se charge depuis un fichier depuis 4.2** : format texte `.map` maison, dossier
`maps/`, rechargement à chaud. Le bac à sable lui-même passe par ce chemin.

**Les modèles 3D animés fonctionnent depuis M6** : import glTF maison, texture, squelette,
animations et skinning GPU. Le personnage du dossier `Model3D/` court dans la scène.
**Depuis 2.2 les cycles se mélangent** : marche et course, et tout ce qu'il y a entre.

**La rotation est visible à l'écran depuis R6.** Les caisses basculent et le rendu suit.

## ⚠️ Périmètre — la phase 5 est SUPPRIMÉE (16/08)

**Le livrable est le MOTEUR 3D, pas le jeu.** Round, économie, bombe, map jouable et playtest
5v5 sortent du programme. Il reste **phase 3 (réseau)** et **phase 4 (outillage)**.

Deux conséquences à ne pas redécouvrir :

1. **`RoundMachine`, `Economy` et `BuyCatalog` sont devenus orphelins** — écrits, testés
   (`RoundMachineTests`, `EconomyTests`), et sans destination. Ne pas les rebrancher par
   réflexe. En revanche `Arsenal` et `Ballistics` **servent** (le bac à sable tire), et
   `CombatTarget.RayVsAabb` est utilisé par `PhysicsWorld.Raycast` : ceux-là restent.

2. **Le projet n'a plus de juge extérieur.** « Des gens veulent refaire une partie » était le
   seul critère indépendant de nous. Le **bac à sable devient le livrable**, et la validation
   repose entièrement sur des mesures — plus un humain qui regarde et écoute.

⚠️ **La règle n°2 sur les licences ne bouge pas.** Un moteur commercialisable ne peut pas plus
embarquer du copyleft qu'un jeu.

## Chantier terminé — format de map (4.2), et le décor sort du code

**Le décor était écrit en dur** : 32 boîtes posées ligne par ligne dans `BuildLevel()`.
Déplacer un mur demandait de recompiler, et rien d'autre que le programme ne pouvait décrire un
niveau. C'était le trou le plus visible d'un moteur censé être réutilisable.

`MapData` + `MapFile` sont dans **`Core`** (règle n°1) : une carte se lit, se valide et se
compare sans jamais ouvrir de fenêtre. `BuildLevel()` est devenu `BuildSandboxMap()` et rend
une `MapData` — **le bac à sable passe désormais par le même chemin qu'un fichier**, ce qui
est la seule façon d'être sûr que ce chemin marche.

Console : `map`, `map_list`, `map_save [nom]`, `map_load <nom>`, `map_reload`, `map_sandbox`.
Ligne de commande : `--map <nom>`. Les cartes vivent dans `maps/`.

⚠️ **Sans `--map`, c'est le bac à sable INTÉGRÉ qui sert**, jamais un fichier trouvé sur le
disque. Un décor de référence qui change tout seul rendrait toutes les mesures incomparables
d'une session à l'autre.

📏 **La preuve, et elle est au niveau des données** : bac à sable intégré → `map_save` →
`--map` → `map_save` à nouveau. Les deux fichiers sont **octet pour octet identiques**.
La carte lue décrit donc exactement le décor du code.

📸 **Une carte écrite ENTIÈREMENT À LA MAIN se charge et se rend** (`maps/essai.map`, 10
boîtes, 4 lumières). C'est la seule preuve qui justifie d'avoir choisi le texte plutôt que le
binaire : si personne ne peut écrire une carte dans un éditeur, autant faire du binaire.

⚠️⚠️ **UNE LIGNE MAL FORMÉE EST FATALE, et c'est l'INVERSE du choix fait pour `exarion.cfg`.**
Un réglage qu'on ne sait pas relire, on l'ignore : on perd une préférence. Une **boîte** qu'on
ne sait pas relire, on ne peut pas l'ignorer — il manquerait un mur, et le défaut se
manifesterait comme un joueur qui traverse le décor, trois heures plus tard, sans aucun lien
visible avec le fichier. La carte est lue **en entier avant d'être appliquée** : un fichier
fautif laisse donc le monde courant intact, et l'erreur porte son **numéro de ligne**.

⚠️ **Les flottants sont écrits SANS PERTE**, pas arrondis pour faire joli. Une marche calculée
à 2,2500002 deviendrait 2,25, et le fichier relu ne décrirait plus tout à fait la carte
enregistrée — un déplacement invisible et **cumulatif** à chaque enregistrement. Depuis
.NET Core 3.0 le rendu par défaut d'un float est la plus courte chaîne qui se relit à
l'identique : on a l'exactitude *et* « 60 » plutôt que « 60.000000 ».

⚠️ **La culture, encore.** Même piège que 2.4, réel sur cette machine. Vérifié par mutation :
en retirant `CultureInfo.InvariantCulture`, **3 tests sur 16 tombent**. Et en arrondissant à
deux décimales, **3 également**.

⚠️ **Les lumières font partie de la carte, et cohabitent avec `light_sun`/`light_lamps` par un
RAPPORT.** La carte porte des intensités absolues ; au chargement les deux ConVar prennent la
valeur déclarée, et l'éclairage est ensuite mis à l'échelle de leur écart à cette référence —
donc facteur exactement 1 au chargement. **Un réglage explicite de l'utilisateur gagne sur la
carte** : l'écraser jetterait précisément ce que 2.4 servait à garder.

⚠️ **`--exec` s'exécute désormais APRÈS le chargement de la carte**, plus à la fin de
`RegisterConsole()`. Constaté : `--exec "map_save arene"` répondait « aucune carte à
enregistrer ». La règle reste la même — la ligne de commande a le dernier mot, sur le `.cfg`
comme sur ce que la carte impose.

🐛⚠️ **DÉCOUVERTE QUI CONTREDIT LA DOC : les captures d'écran ne sont PAS reproductibles au bit
près sur cette machine.** Deux runs strictement identiques donnent **96,7 %** de pixels
différents — et **99,97 %** même en figeant les caisses, l'animation et les particules, et
**encore** en capturant à la première image. Ce n'est donc pas de la dérive de souris ni de
l'interpolation. **L'A/B par SHA-256 utilisé en R6 ne peut pas se rejouer ici.**

À la place, on compare **le signal au bruit** : écart moyen entre deux runs du décor intégré
**31,90/255**, entre le décor intégré et la carte chargée **21,37** et **28,74** — donc *sous*
le bruit de fond. Combiné à l'aller-retour octet pour octet, ça suffit. **La cause de cette
non-reproductibilité n'est pas identifiée, et c'est une dette à part entière** : elle prive le
projet de son outil de non-régression visuelle.

✅ **CORRECTION DU 07/10 — LES CAPTURES SONT REPRODUCTIBLES, à deux conditions.** Mesuré avec
`tools/ab-captures.ps1`, la même build contre elle-même : **6 scènes sur 8 identiques au bit
près** (même SHA-256), et 7 sur 8 dès que l'animation est figée dans l'éditeur aussi. Les deux
conditions :

1. **`character_speed 0`** partout où un personnage est visible. L'horloge d'animation vaut
   `_simTime + alpha × pas`, et `alpha` dépend de l'instant réel de la capture.
2. **Pas de particules.** Elles sont interpolées par le même `alpha` : sur 31 captures de la scène
   « effets », **31 empreintes différentes**, référence comprise (écart de 0 à 33 pixels).

La scène « partie » est **bimodale** : elle a deux images possibles, toujours les deux mêmes
(empreintes `6FE43478` et `484E56F6`), sans doute selon le pas où une caisse s'endort. Un écart de
13 pixels sur elle n'est donc pas une régression tant que l'image produite est l'une des deux.
La mesure des 96,7 % du 17/08 n'a pas été reproduite ; sa cause reste inconnue (seuil 0 sur une
scène animée ? autre machine ?), mais l'outil de non-régression visuelle EXISTE à nouveau.

## Calcul de chemin (08/10) — E16, la plus grosse absence de l'IA, comblée

**Avant** : un PNJ marchait en ligne droite et restait collé à tout obstacle de plus de 45 cm.
**Maintenant** : `NavGrid` (`Core/Navigation/`) découpe le décor en colonnes de 25 cm, garde
chaque dessus de boîte où le **vrai corps** du PNJ tient, relie les sols voisins (montée ≤ 45 cm,
chute ≤ 2 m), et un A* trouve le chemin. Grille **2,5D** : plusieurs étages par colonne, donc
estrades, escaliers, passerelles et passages dessous.

📸 **Preuve** : `maps/chemins.map`, écrite à la main — une ronde entre un point derrière un mur de
20 m et un point sur une estrade de 1,80 m à escalier. À 11 s, le garde a atteint l'estrade (HUD :
altitude **1,8**, `ONREACHGOAL`) et repart. **Même carte avec `nav_enabled 0`** : collé au mur en
(0 ; 0,7), **0,0 m/s** — exactement le défaut du 17/08.

Console : `nav` (grille, composantes, recherches, chemin de chaque PNJ), `nav_enabled 0|1`,
`nav_show 1` (le chemin en lattes : **bleu** vers la cible, **orange** au plus près). Le HUD
affiche désormais l'**altitude** de chaque PNJ, et `CHEMIN i/n` ou `INACCESSIBLE`.

📏 **A/B contre 6ce870a** (`tools/ab-captures.ps1`) : 7 scénarios sur 8 identiques au seuil 6 ;
`pnj` ne diffère que sur la ligne de HUD du garde (zone x 244-470, y 84-103 : l'altitude
insérée, position et état inchangés). Console : 3 entrées de plus, session de 70 commandes
identique. Le garde de `formes.map` marche en ligne droite — le calcul de chemin ne s'y
déclenche pas, et c'est ce qui rend la comparaison possible.

⚠️⚠️ **LE DÉFAUT DE MA PREMIÈRE VERSION, ET IL AURAIT RENDU LE JEU INJOUABLE.** Une cible
inaccessible (joueur sur une estrade sans escalier) laissait la liste du chemin vide — et « pas de
chemin » déclenchait un recalcul. Donc un A* **à chaque pas**. Mesuré : un A* qui échoue visite
tout ce qui est accessible, **18 ms** sur 60 × 60 m, soit plus d'une image à 64 pas/s pour **un
seul** garde. Les 5 premiers tests étaient verts ; c'est la **durée** d'un test (2 s au lieu de
quelques ms) qui a mis la puce à l'oreille, la relecture du code l'a confirmé. Désormais figé : le
nombre de recherches en 6 s est borné (12 mesurées, 384 avec le défaut).

⚠️⚠️ **D'OÙ LES COMPOSANTES FORTEMENT CONNEXES.** Le graphe est **orienté** : on descend d'une
estrade sans marches, on n'y monte pas. Tarjan, écrit **sans récursion** (une composante de 57 000
sols ferait une pile de 57 000 appels), puis la table « qui atteint qui », en bits. « Cette cible
est-elle accessible ? » devient une lecture de bit. Une cible inaccessible ne lance plus qu'une
recherche **bornée** (4 096 sols) qui rend le chemin vers le point **le plus proche** : le garde
vient au pied de l'estrade et attend, tourné vers le joueur, au lieu de pousser contre la paroi.

📏 **Coûts en régime établi** (200 itérations de chauffe — les durées affichées par les tests
incluent la compilation à la volée et sont 5 à 9 fois trop pessimistes) :

| Opération | Coût |
|---|---|
| Approche d'une cible inaccessible (était 18 ms) | **0,89 ms** |
| Détour de 70 m | 0,92 ms |
| Diagonale de 80 m entre 40 boîtes | 1,08 ms |
| Construction, 60 × 60 m + 40 boîtes (une fois par carte) | 21 ms |
| Terrain dégagé, trajet de 36 × 18 m | **144 sols visités** sur 25 568 |

⚠️ **L'heuristique OCTILE et le départage par g ne sont pas du confort.** Sur un trajet en biais,
des milliers de cases ont exactement le même coût estimé. Vérifié par mutation : sans départage,
ou avec la distance à vol d'oiseau, `En_Terrain_Degage_L_A_Etoile_Ne_Fouille_Pas_Tout` tombe.

⚠️ **Le corps est testé à partir d'une HAUTEUR DE MARCHE**, pas depuis le sol. Première version :
devant chaque contremarche, le bas du corps chevauchait la marche suivante, et un escalier devenait
une suite d'îlots. Aucun chemin ne montait.

⚠️ **Une chute est une arête de PLUSIEURS cases.** Au pied d'un mur, sur un demi-corps de large, le
sol n'est pas marchable (le corps toucherait la paroi) : le dessus d'une estrade n'avait aucun
voisin au sol. Un vrai PNJ qui sort d'un bord atterrit à un demi-corps du mur : c'est cette arête.

⚠️ **La ligne droite doit ARRIVER au niveau de la cible.** Sans ce test, un joueur sur une
passerelle est « en ligne droite » de tout le sol qui passe dessous : le PNJ marcherait jusque
sous ses pieds. Et l'**arrivée** regarde désormais la hauteur (`ArriveHeight`, 1,5 m) : un point
de ronde sur une passerelle était « atteint » dès que le garde passait dessous. Ces règles ne
s'appliquent que navigation active : **`nav_enabled 0` reste le comportement d'avant E16 à
l'identique** — c'est ce qui en fait une contre-épreuve.

⚠️ **La ligne droite d'abord, le chemin seulement si quelque chose barre la route.** Une fois sur
un chemin, la ligne droite n'est re-testée qu'aux recalculs (0,5 s) : au ras d'un coin, elle
s'ouvre et se ferme d'un pas à l'autre, et chaque fermeture relancerait un A*.

⚠️ **Repérer le joueur REMET le chemin à zéro.** Sans ça, un garde en plein contournement suivait
encore jusqu'à 0,5 s le chemin de sa ronde, dos au joueur qu'il venait de voir.

📏 **9 mutations, toutes attrapées** :

| Mutation | Tests qui tombent |
|---|---|
| la table dit « tout est accessible » | 2 |
| la ligne droite sans test d'arrivée | 2 |
| pas de départage | 1 |
| heuristique à vol d'oiseau | 1 |
| pas de chemin partiel (on reste au départ) | 3 |
| pas de fermeture transitive | 2 |
| recalcul à chaque pas | 1 |
| arrivée sans la hauteur | 1 |
| pas de remise à zéro en repérant le joueur | 1 |

⚠️ **`AgentNavigationTests` met un VRAI `CharacterMotor` sur le chemin**, chaque test avec sa
contre-épreuve `nav_enabled 0`. C'est la leçon de l'ascenseur (E17) : 27 tests verts sur
`MoverWorld`, et un joueur qui ne montait pas.

### Ce qui reste — à ne pas prendre pour des bugs

- **La grille ne voit que le DÉCOR.** Portes, caisses et autres PNJ n'y sont pas : un garde
  planifie à travers une porte fermée et **attend devant en poussant** jusqu'à ce qu'elle s'ouvre.
  ✅ Depuis le 08/10 il BUTE bien sur elle (voir plus bas) ; avant, il la traversait.
- **La grille est CONSERVATRICE** : un couloir juste aussi large que le corps peut être jugé
  infranchissable selon l'alignement des cases de 25 cm.
- **Les formes ne comptent pas** : la grille lit les AABB, comme la collision. Une rampe ne se monte
  pas — c'était déjà vrai pour le joueur.
- **Pas de saut** : un trou se contourne, ou ne se franchit pas.
- **Grille construite au chargement de la carte**, jamais mise à jour en cours de partie.
- **Aucun ressenti jugé** : un garde qui contourne « bien » ou « bizarrement », ça se regarde.

## Une porte qui se referme sur quelqu'un (08/10) — elle repart, comme dans Source

**Avant** : une porte avançait sans regarder. Refermée sur le joueur, elle le **chevauchait**, et
c'était au `CharacterMotor` de l'en sortir — le README le notait : *« elle le repousse, pas de
notion de blocage »*. **Maintenant** : `Mover.Step` reçoit les obstacles du pas (joueur, PNJ,
caisses) et, si la course en touche un, applique la propriété **`blocked`** :

| `blocked` | Effet |
|---|---|
| `reverse` (défaut) | repart dans l'autre sens — la porte de Source |
| `stop` | attend sur place que la voie se libère |
| `push` | passe quand même — le comportement d'avant |

Sortie **`OnBloque`** (`target_blocked` / `input_blocked`), émise **une fois par blocage**.

📏 **Vérifié en jeu**, `logique.map`, joueur dans l'embrasure, porte ouverte puis refermée seule
(`wait=3`) : **`reverse` → à 6 s `Ouverture 67 %`, à 8,5 s `Ouvert 100 %`** — jamais fermée ;
**`push` → `Fermeture 34,9 %` puis `Ferme 0 %`**, refermée sur le joueur. Capture : `PORTE
OUVERTURE 79 %` vue depuis l'embrasure.

🐛⚠️ **TROUVÉ EN JEU, PAS EN TEST : une porte doit pouvoir S'ÉLOIGNER de ce qu'elle chevauche
déjà.** Premier essai : `tp 0 0 0` met le joueur DANS la porte fermée ; à l'ouverture, la position
suivante le chevauchait encore, la porte se jugeait bloquée, repartait, se rebloquait — et oscillait
sur place, **0 %** à 6 s comme à 8,5 s. Seul ce que la course touche NOUVELLEMENT l'arrête
désormais. Les 5 premiers tests passaient avec le défaut ; le 6ᵉ le fige.

⚠️ **Un PASSAGER n'est pas un obstacle** : sinon tout ascenseur se figerait dès qu'on monte dessus.
⚠️ **Une sortie par blocage, pas une par pas** : en mode `stop`, une porte qui attend émettrait
sinon `OnBloque` 64 fois par seconde.

📏 **5 mutations, toutes attrapées** : passager compté comme obstacle, `OnBloque` à chaque pas,
blocage sans retour, rien ne bloque (3 tests), chevauchement existant compté. 6 tests. **A/B
contre 2f777a4 : identique**, console comprise (`effets` à 2 pixels, dans la variation connue).

## Ambiance et PNJ comme CIBLES (08/10) — allumer une lumière, jouer un son, réveiller un garde

Le README le disait en une ligne : *« rien encore qui allume une lumière, joue un son, ou réveille
un PNJ »*. Trois récepteurs de plus pour le câblage sorties → entrées :

| Type / cible | Entrées | Propriétés |
|---|---|---|
| `env_light` | Allumer, Eteindre, Basculer | color (`r,g,b` linéaire, **virgules**), intensity, range, start_off |
| `env_sound` | Jouer, Arreter, Basculer | hz, volume, start_on |
| `npc_dummy` **nommé** | Patrouiller, Arreter, Rentrer | — |

Console : **`env`**. `EnvWorld` (Core) ne porte que l'ÉTAT ; le rendu d'une lampe et la voix d'un
son vivent dans le jeu, qui lit cet état — exactement comme une porte.

📏 **Trois preuves, une par sens** (`maps/logique.map` étendue) :
- 📸 **lampe** : allumée contre éteinte (`ent_send lampe Eteindre`), **142 097 pixels changés**
  (15,4 %), l'éclairage rouge sur le mur disparaît ;
- 🔊 **sirène** : enregistrement WAV avec et sans `ent_send sirene Jouer` — **RMS 0,0140 / 0,0094
  contre 0,0005 / 0,0009** (gauche/droite : la sirène est à gauche du joueur, ça s'entend dans
  les chiffres). Hauteur mesurée par passages à zéro : **440,0 Hz** sur 6 bips sur 7 (le
  septième est le bruit d'un son d'événement). Et la cohérence : un bip à 50 % du temps, de crête
  0,029, doit donner un RMS de 0,0145 — mesuré 0,0140 ;
- 📸 **veilleur** : endormi (`IDLE`, immobile) à 1 s, en ronde (`PATROL`) à 5 s — réveillé par la
  herse qui s'ouvre (`target_open=veilleur input_open=Patrouiller`).

⚠️ **La version de l'ambiance ne bouge qu'au CHANGEMENT.** C'est elle qui fait reconstruire
l'éclairage : un « Allumer » reçu à chaque passage sur une lampe déjà allumée le reconstruirait
64 fois par seconde. Et sans elle, l'interrupteur marchait dans Core et la lampe ne s'allumait
qu'au chargement de carte suivant. Vérifié par mutation.

⚠️ **Un son en boucle est un BIP, pas une note tenue** : `SoundGen.Beep`, 0,25 s de son puis
0,25 s de silence. `Tone` bouclé « respirerait » au rythme de la boucle — son enveloppe
anti-claquement le ramène à zéro à chaque tour. Les deux bouts du bip valent zéro : la boucle se
raccorde sans discontinuité (testé).

⚠️ **Les sons d'ambiance se synchronisent dans TOUS les modes, console ouverte comprise**, et la
lampe se relit à chaque image : c'est par la console qu'on les actionne en headless.

⚠️ **« Patrouiller » sans ronde laisse le PNJ au repos** — en Patrol sans point à viser, il
resterait immobile dans un état que le HUD afficherait comme une ronde en cours.

⚠️ **Les lampes commutables passent APRÈS celles de la carte** : au-delà de 32 lumières, ce sont
elles qui sautent, pas l'éclairage que la carte déclare.

📏 **4 mutations, toutes attrapées** : version qui bouge sans changement, couleur négative
acceptée, « Patrouiller » sans ronde, PNJ non inscrits. Les fils de câblage de l'éditeur
connaissent les nouvelles cibles, dans l'ordre d'inscription du jeu. **A/B contre dc136a2 : les
huit scénarios identiques** ; console : `env` en plus, et la liste des types.

## Les PNJ et les portes (08/10) — un garde traversait une porte fermée, et voyait au travers

🐛 **Les PNJ ne connaissaient que le DÉCOR** : leur `CharacterMotor` et leur ligne de vue
recevaient `Level`, construit une fois au chargement. Portes et caisses leur étaient
transparentes : un garde traversait une herse fermée, et repérait le joueur derrière. Le joueur,
lui, bute sur `Walkable` — décor, caisses, portes, reconstruit à chaque pas par `PrepareStep`.
`AgentWorld.Update` reçoit désormais ce même monde (`solid`), pour le déplacement ET la vue.

📸 **A/B sur deux binaires**, `maps/logique.map` (un garde dont la ronde passe sous la herse),
minuterie arrêtée donc herse fermée, capture à 4 s : **référence 80d388f : garde en z = −3,9,
passé AU TRAVERS** ; **après : z = 0,6, 0,0 m/s, arrêté devant**. 4 tests (dont une contre-épreuve
sans monde solide), 2 mutations attrapées (déplacement, vue). A/B des huit scénarios contre
80d388f : identiques, console comprise (`effets` à 1 pixel, dans le bruit).

⚠️ **Le calcul de chemin reste sur le décor**, volontairement : une porte s'ouvre et se ferme, la
grille ne se reconstruit pas à chaque fois. Conséquence assumée : devant une porte fermée, le
garde **pousse** (vitesse voulue non nulle) jusqu'à ce qu'elle s'ouvre, au lieu de chercher un
autre chemin.

⚠️ **Les PNJ ne se voient toujours pas entre eux et ne se bousculent pas** : ils ne sont pas dans
`Walkable`.

## Entités LOGIQUES (08/10) — relais, minuterie, compteur, et le câblage enfin visible

**Avant** : une seule entité AGISSAIT, `func_door`. Le câblage sorties → entrées marchait, mais
rien ne pouvait se poser ENTRE un déclencheur et une porte. `LogicWorld` (Core) ajoute :

| Type | Entrées | Sortie | Propriétés |
|---|---|---|---|
| `logic_relay` | Declencher, Activer, Desactiver, Basculer, Annuler | `OnDeclenche` | delay, once, start_disabled |
| `logic_timer` | Demarrer, Arreter, Basculer, Declencher | `OnMinuterie` | interval, start_disabled |
| `logic_counter` | Ajouter, Retirer, Remettre | `OnMax` | max, start |

Toutes ont `name`, `target`, `input`. Entrées en **français sans accent**, comme celles des
portes : c'est la langue des récepteurs du moteur. Console : **`logic`** (état, câblage, file,
refus). Éditeur : **F10** relais, **F11** minuterie, **F12** compteur — ⚠️ **non vérifiées au
clavier** (F10 est la touche de menu de Windows, F12 celle du débogueur) ; la voie vérifiée est
`ed_entity logic_relay`.

📸 **Preuve** : `maps/logique.map`, écrite à la main. Deux `ent_send compteur Ajouter` à t = 0 →
le compteur atteint 2/2 → le relais attend 1 s → la porte s'ouvre : **`PORTE OUVERT 100 %`** sur
la capture à 3,2 s, et la minuterie a basculé la herse à 2,00 s. **Contre-épreuve, un seul
passage : `PORTE FERME 0 %`**, la herse s'ouvre quand même.

⚠️⚠️ **UNE BOUCLE NE PEUT PAS GELER LE JEU.** Deux relais qui se visent sans délai
s'appelleraient à l'infini dans le même pas. Toute sortie logique passe par une FILE ; `Step` en
sort tout ce qui est dû AVANT de livrer, donc une sortie demandée pendant la livraison part au pas
suivant. Une boucle devient un **battement à 64 Hz**, visible dans le fil d'événements. Testé :
100 pas → 100 événements, une seule sortie en attente.

⚠️ **HONNÊTETÉ : la règle « au moins un pas » à la mise en file est une deuxième ceinture.**
Vérifié par mutation : sans elle, les 13 tests passent, boucle comprise — c'est la sortie de file
avant livraison qui protège. On garde les deux ; retirer les deux gèlerait le jeu, et ce test-là
ne s'écrit pas : il ne rendrait jamais la main.

⚠️ **La file est BORNÉE (256) et le refus est COMPTÉ** (`logic` l'affiche). Une sortie ne vise
qu'une cible, donc rien ne se multiplie seul — mais une minuterie rapide qui nourrit un relais
lent remplirait la file sans fin. Même règle que le réservoir de particules : un débordement
silencieux fait « arrêter de marcher » un niveau sans raison visible.

⚠️ **LA MINUTERIE NE DÉRIVE PAS** : l'échéance suivante se calcule depuis la PRÉCÉDENTE, pas
depuis « maintenant ». Vérifié par mutation : recalée sur maintenant, 0,3 s donne ~96 sorties en
30 s au lieu de 100. Et le test m'a eu au passage : écrit à 30 s pile, il comptait **99** — ce
n'était pas une dérive, c'était 0,3 en `float` (0,30000001), dont la centième échéance tombe à
30,0000012 s, un pas trop tard.

⚠️ **Redémarrer une minuterie qui tourne ne repousse PAS son échéance** — sinon un déclencheur
qui renvoie « Demarrer » à chaque passage la ferait taire indéfiniment. **Un compteur n'émet
qu'en ATTEIGNANT son maximum**, pas à chaque ajout au-delà : sinon trois passages de trop
rouvriraient trois fois une porte ouverte. **Un relais désactivé ENTEND l'entrée** (livrée) mais
ne relaie pas : répondre « entrée inconnue » ferait chercher une faute de frappe inexistante.

⚠️ **La logique avance dans `PrepareStep`, pas dans `UpdateEntities`** — pour la même raison que
les portes : ce pas-là tourne aussi console ouverte et derrière le menu. Une minuterie qui
s'arrêterait dès qu'on tape `ent_send` serait invérifiable en headless.

📏 **6 mutations** : dérive de la minuterie, compteur qui émet à chaque ajout, file sans borne,
« Demarrer » qui repousse l'échéance → chacune attrapée par son test ; plus la deuxième ceinture
ci-dessus, **non attrapée, et c'est écrit**. `EntitySchemaTests` couvre les trois nouveaux
catalogues (3 mutations de défaut affiché, toutes attrapées).

### Les FILS du câblage, dessinés dans l'éditeur

Une latte **jaune** de chaque entité vers la cible qu'elle vise (`target`, `target_open`,
`target_close`), une bille au bout qui reçoit. Une latte **rouge** dressée au-dessus d'une entité
dit « je vise un nom que personne ne porte » — `porte_hal` au lieu de `porte_hall`, la faute de
carte la plus courante, qui ne se voyait jusqu'ici qu'EN JOUANT (`CibleAbsente`). La cible est
résolue comme le moteur la résout : seules les entités qui REÇOIVENT (portes, puis logique, dans
l'ordre d'inscription), la première qui porte le nom gagne. 📸 Vérifié par capture : les quatre
fils de `logique.map`.

🐛 **Au passage, un défaut d'affichage antérieur** : la fin des deux premières lignes d'aide de
l'éditeur (`TAB PANNEAU`, `CTRL+Z/Y`, `CTRL+S`, `P DEPART`) passait SOUS le panneau de
propriétés, qui couvre toute la hauteur à droite. Constaté sur une capture ; l'aide tient
maintenant en cinq lignes de 90 caractères au plus (10,2 px par caractère à l'échelle 1,7).

📏 **A/B contre a7440ab** : `editeur` change comme prévu (aide réorganisée, fil du volume vers la
porte de `formes.map`) ; console : `logic` en plus, et la liste des types d'`ed_entity`. Six
scénarios identiques. ⚠️ **`effets` varie de 0 à 7 pixels d'un lancement à l'autre** (deux
reprises : 0 puis 6 ; bruit mesuré sur la référence seule : 0 et 1). L'écart n'est pas
systématique — une reprise redonne l'image exacte — mais je n'ai pas isolé pourquoi il dépasse
parfois le bruit mesuré : les particules sont interpolées à l'affichage, c'est la piste.

## Câblage des PNJ et catalogue des propriétés (08/10) — trois défauts, un test qui les voit

🐛 **Les sorties des PNJ n'étaient livrées à PERSONNE.** `OnSpotPlayer -> porte.Ouvrir`
s'affichait dans le fil d'événements, et la porte ne bougeait pas : `GameWorld` rapportait les
événements des PNJ sans jamais appeler `Send`. Volumes et portes livraient ; les PNJ étaient le
seul émetteur oublié. `AgentWorld.Update` reçoit désormais le monde des entités. Un test
d'intégration met un vrai garde et une vraie porte, et vérifie qu'elle s'ouvre — avec sa
contre-épreuve sans routeur, qui reproduit l'ancien comportement.

🐛 **Le panneau de propriétés affichait CINQ défauts faux, et en omettait trois** :

| Propriété | Affiché | Lu par le moteur |
|---|---|---|
| `size` (et `height`) des volumes | 4 | **3** |
| `depth` des portes | 0,2 | **0,3** |
| `input_spot` / `input_lose` des PNJ | Trigger / Untrigger | **Alert / Calm** |
| `input_goal` (PNJ), `input_open` / `input_close` (portes) | absentes | Reached / Trigger |

C'était exactement le risque nommé en tête de `EntitySchema` — « un défaut affiché faux se lit
comme l'état du moteur » — et rien ne le vérifiait. **`EntitySchemaTests`** compare maintenant,
pour chaque type, une entité SANS propriété et la même avec TOUS les défauts du catalogue écrits
en clair : elles doivent se comporter à l'identique. Aucune valeur n'est recopiée dans le test,
il confronte le moteur à lui-même. ⚠️ **Ce qu'il ne voit pas** : une clé lue par le code et
OUBLIÉE du catalogue — absente des deux côtés, elle ne change rien.

🐛 **Ce test a trouvé un vrai défaut du moteur au passage.** Un checkpoint sans `name` ni `order`,
franchi deux fois, comptait **double** : son nom de repli était le nombre de checkpoints déjà
franchis, donc « 0 » puis « 1 ». Contraire à la règle « un checkpoint ne s'enregistre qu'une
fois », que le test existant ne vérifiait qu'avec un checkpoint NOMMÉ. Le nom de repli est
désormais le rang de l'entité dans la carte (`#n`), stable d'un passage à l'autre.

✅ **La livraison des fins de course des portes est descendue dans `Core`**
(`MoverWorld.DeliverOutputs`) : elle vivait dans `GameWorld`, donc ne se testait pas en ligne de
commande — et le défaut affiché pour `input_open` ne pouvait être confronté à rien.

📏 **5 mutations, toutes attrapées** : chacune des trois anciennes valeurs du catalogue, l'ancien
nom de repli, la livraison retirée.

📏 **A/B contre 44470ed** : seul le panneau de l'éditeur change (garde sélectionné : `ALERT` /
`CALM` au lieu de `TRIGGER` / `UNTRIGGER`, plus la ligne `INPUT GOAL`). Six scénarios identiques,
`effets` à 2 pixels au seuil (bruit des particules, max 9/765), console identique.

## PNJ (17/08) — E14, une IA pilotée par des PROPRIÉTÉS

**L'intention : que l'auteur du niveau n'écrive pas une ligne de code.** Tout se déclare dans
l'éditeur, tout se câble en sorties :

```
npc_dummy  behaviour=patrol  path=ronde_nord  speed=2.2  sight=22  fov=110  name=garde_1
           target=alarme  input_spot=Declencher
```

Sorties : `OnSpotPlayer`, `OnLosePlayer`, `OnReachGoal`. Elles entrent dans le **même** flux
d'événements que les volumes de E13. États : `Idle`, `Patrol`, `Chase`, `Return`.

⚠️⚠️ **RIEN N'EST RÉÉCRIT : UN PNJ EST UN `CharacterMotor`.** Le même que le joueur, plus un
cerveau qui lui fabrique des `MoveInput`. Il hérite gratuitement du déplacement façon Quake, de
la gravité, du franchissement de marches et de la collision — tous testés depuis des semaines.
Un second déplacement aurait donné deux comportements à faire diverger.

⚠️⚠️ **LA LIGNE DE VUE PART DES YEUX, DES DEUX CÔTÉS.** Vérifié par mutation : tirée depuis les
pieds, un muret de 50 cm rend le garde **aveugle** — et on chercherait le défaut dans le champ
de vision. Le champ, lui, est **horizontal** : un joueur sur une marche, droit devant, doit rester
visible.

⚠️⚠️ **L'HYSTERESIS N'EST PAS DU CONFORT.** À la limite de portée, la vue s'accroche et se perd
d'une image à l'autre. Vérifié par mutation : sans le délai de 2,5 s, une vue qui clignote produit
**200 événements** au lieu de 2. Même classe de défaut que la mémoire des volumes.

⚠️ **Le rayon d'arrivée (0,6 m) doit dépasser la distance parcourue en un pas** (7,4 cm à
4,76 m/s). Vérifié par mutation : à 0,001 m le PNJ dépasse son point, revient, redépasse — il
vibre sur place et n'atteint qu'**1 point au lieu de 3**.

⚠️ **En poursuite on vise les PIEDS du joueur, pas ses yeux.** Viser les yeux ferait marcher le
PNJ vers un point en hauteur, donc en permanence contre le mur sous le joueur.

⚠️ **Un Animator PAR PNJ, avec une phase décalée** (`i × 0,37`). Un seul partagé donnerait à tous
les gardes la même pose au même instant — trois personnes au pas, jambe gauche synchronisée.
✅ **C'est le premier vrai usage du mélange de poses (2.2)** : la note de l'époque disait « le
branchement tient en une ligne le jour où il y aura une entité ». C'est
`animator.Weight = vitesse / vitesse_de_course`.

⚠️ **Les PNJ sont instanciés en entrant en PARTIE, pas au chargement de la carte.** Constaté :
juste après en avoir posé deux dans l'éditeur, la console répondait « aucun PNJ dans la carte ».
`AgentWorld` prend un instantané. C'est aussi le bon comportement — entrer en partie remet les
gardes à leur poste.

📏 **Mesuré sur le HUD**, qui affiche position, état et vitesse de chaque PNJ précisément pour
que la capture soit **vérifiable** : garde_1 parcourt **8,7 m en 4 s à 2,2 m/s** (8,8 attendus),
garde_2 12,2 m à 3,4 m/s. Chaque PNJ a bien ses propres réglages. Poursuite vérifiée :
`npc_dummy.OnSpotPlayer -> alarme.Declencher`, garde en `CHASE`, teinté de rouge à l'écran.

✅ ~~**IL N'Y A AUCUN CALCUL DE CHEMIN.**~~ **Fait le 08/10 — voir E16 juste au-dessus.** Le
constat du 17/08 : un garde lancé en poursuite s'arrêtait net contre une estrade de 1,80 m, à
0,0 m/s. Avec un escalier, il monte désormais ; sans, il vient au pied et attend.

⚠️ **Les PNJ ne se voient pas entre eux** et ne collisionnent pas entre eux.

Console : `npc`.

## Entités VIVANTES (17/08) — sorties → entrées, façon Source

`EntityWorld` dans `Core`. Un volume émet `OnEnter` / `OnExit`, visant une **cible par son nom**
avec une **entrée** : `piege.OnEnter → porte_hall.Ouvrir`. C'est le modèle de Half-Life 2, et
c'est de la **donnée** : ça se lit dans le fichier de carte, ça se compare, ça se teste en ligne
de commande, et ça ne peut pas boucler à l'infini par accident.

⚠️⚠️ **UN VOLUME DOIT SE SOUVENIR QU'IL CONTENAIT DÉJÀ LE JOUEUR.** Sans cette mémoire, il émet
à **chaque pas** — 64 fois par seconde. Vérifié par mutation : 100 pas dans un volume donnent
**100 événements au lieu d'un**. Un checkpoint « franchi » deviendrait « franchi 64 fois », et
un événement une avalanche qui noie tout le reste.

⚠️ **Un checkpoint ne s'enregistre qu'UNE FOIS, même sans `once`.** C'est ce qui le distingue
d'un déclencheur : un aller-retour ne doit pas faire croire à deux checkpoints franchis.

⚠️ **Le test se fait contre la BOÎTE du joueur, pas ses pieds.** Un volume posé au sol raterait
sinon quelqu'un qui saute par-dessus, alors que son corps le traverse.

⚠️ **Une taille nulle retombe sur la valeur par défaut.** Un intervalle inversé n'est jamais
croisé par `Intersects` : le déclencheur serait **muet en silence**. Même piège que les boîtes.

⚠️ **Une sortie NON câblée émet quand même.** Un déclenchement sans cible doit se voir : le taire
ferait croire que le volume ne marche pas, alors qu'il marche mais ne vise personne — deux
problèmes très différents.

⚠️ **`Events` décrit la DERNIÈRE mise à jour et se vide.** Ne pas le vider ferait grossir la
liste indéfiniment et rejouerait éternellement le premier déclenchement.

⚠️ **Changer de carte recharge tout.** Un indice de volume renvoie à une autre entité d'une carte
à l'autre : un déclencheur consommé bloquerait un déclencheur sans rapport au niveau suivant.

📏 **Vérifié bout en bout** : deux volumes superposés, `[entite] trigger_checkpoint.OnEnter` puis
`[entite] trigger_volume.OnEnter -> porte_hall.Ouvrir` dans la console, et à l'écran
`CHECKPOINTS 1 : HALL` plus le câblage en clair. Un déclenchement fait aussi des étincelles et
un son — sinon on ne sait pas s'il a eu lieu.

Console : `ent`, `ent_rearm`.

✅ ~~**Aucune ENTRÉE n'est implémentée côté cible.**~~ **Fait le 17/08 — voir E17 ci-dessous.**

## E17 (17/08) — la géométrie qui BOUGE, et le câblage qui arrive enfin quelque part

E13 donnait des sorties ; il n'y avait **rien à viser**. Un niveau Exarion était du décor
strictement immobile. `func_door` est la première entité qui **agit** :

```
entity func_door 0 0 0  0 0  name=porte_hall axis=y distance=5 speed=2.5 width=3 height=4.5 wait=4
entity trigger_volume 0 0 6  0 0  size=6 target=porte_hall input=Ouvrir
```

Entrées : `Ouvrir`, `Fermer`, `Basculer`. Sorties de fin de course : `OnOuvert`, `OnFerme`
(cibles `target_open` / `target_close`, séparées de `target` — sinon une porte se viserait
elle-même en boucle). Console : `movers`, `ent_send <cible> <entrée>`, `ent_targets`,
`carry_debug`. Éditeur : **F7**.

⚠️ **UNE PORTE ET UN ASCENSEUR SONT LA MÊME CLASSE**, comme `func_door` et `func_movelinear`
dans Source. Deux classes auraient donné deux comportements à faire diverger pour zéro gain.

⚠️ **Le format interdit les espaces dans une valeur** → pas de `move=0 1 0`. D'où
`axis=x|y|z` + `distance` **signée**. Le déplacement en diagonale n'est pas exprimable : limite
assumée. Une porte **ne pivote pas** non plus, `MapBox` n'a pas d'orientation.

📏 **Chaîne complète vérifiée dans le vrai jeu**, pas seulement en test :
`[entite] trigger_volume.OnEnter -> porte_hall.Ouvrir`, puis `[entite] func_door.OnOuvert`,
puis `[entite] func_door.OnFerme` (refermeture automatique à `wait=4`). Capture à caméra
identique : **51 759 pixels changés sur 921 600** (5,6 %), écart max 470/765.

🐛🐛 **LE DÉFAUT DE LA SESSION, ET IL EST EXEMPLAIRE : 27 TESTS VERTS, ASCENSEUR CASSÉ.**
La plateforme montait à `Y 3,80` et le joueur restait à **`Y 0,02`** — au sol. Trouvé
uniquement parce que le HUD affiche l'altitude, donc parce que la **capture était mesurable**.

La cause, après instrumentation (`carry_debug`) : `CharacterMotor.CheckGrounded` fait un
**balayage** de 5 cm vers le bas, et un balayage **ignore ce qui chevauche déjà** la boîte au
départ — volontaire, sinon on resterait coincé dans un mur. Or `pieds += déplacement` laisse la
dérive flottante décider : la position du joueur s'accumule, celle de la plateforme se
recalcule par interpolation, et une fois sur deux le joueur passe un cheveu **sous** la surface.
Donc en chevauchement. Donc pas au sol. Donc en chute libre — au sol la première image, en
chute ensuite. Mesuré : `balayage(hit=False)` avec la plateforme pile sous les pieds.

**Le correctif est `CarryRider`, qui REPOSE les pieds sur le dessus à chaque pas** au lieu
d'ajouter un delta. ⚠️ **Le jeu de contact de 2 mm, lui, n'est PAS ce qui corrige** — je l'ai
d'abord écrit ainsi et c'était faux : mis à zéro, les 497 tests passent. Ce qui est vérifié par
mutation : sans le recalage le test d'intégration tombe, un jeu **négatif** le fait tomber, et
un jeu de 8 cm aussi (hors des 5 cm du balayage). La valeur est encadrée par la mesure, son
intérêt propre reste de la prudence.

⚠️⚠️ **LE TEST QUI MANQUAIT — et c'est la leçon, pas l'anecdote.** Mes 27 tests vérifiaient
`MoverWorld` **contre lui-même**. Aucun ne mettait un vrai `CharacterMotor` sur une vraie
plateforme. `Un_CharacterMotor_Monte_VRAIMENT_Sur_Une_Plateforme` le fait, et lui seul attrape
le défaut. **Exactement la même classe d'erreur que « le joueur contre les caisses » du 16/08 :
la suite peut être verte et le jeu injouable parce que personne ne teste les pièces ensemble.**
Les deux sont dans `Core` — ce test était possible depuis le début.

⚠️ **`Delta` est le déplacement RÉEL** (`centre − centre précédent`), jamais
`direction × vitesse × dt`. Au dernier pas la course est écrêtée par la butée : la valeur
théorique dépasse le mouvement effectif, et le passager serait poussé au-delà de la plateforme,
un peu plus à chaque aller-retour. Vérifié par mutation : 4 tests tombent.

⚠️ **`Carries` teste la boîte d'AVANT le pas.** Contre la boîte d'après, la tolérance devient
une **limite de vitesse déguisée** : à 12 cm et 1/64 s, tout ce qui dépasse 7,7 m/s largue son
passager — et le seuil dépendrait du tickrate, donc le défaut n'apparaîtrait que sur certaines
machines. Vérifié par mutation : seul `Une_Plateforme_RAPIDE_Ne_Largue_Pas_Son_Passager` tombe.

⚠️ **Quatre états de livraison, pas un booléen** : `NonCablee`, `CibleAbsente`,
`EntreeInconnue`, `Delivree`. Avant E17 les quatre se ressemblaient — « rien ne s'est passé ».
Trois sont des **fautes dans la carte** qu'il faut montrer à l'auteur : viser `porte_hal` au
lieu de `porte_hall`, ou dire `Ouvre` au lieu de `Ouvrir`. Sans la distinction, on relit son
déclencheur alors qu'il marche parfaitement et vise un nom qui n'existe pas.

⚠️ **`EntityWorld.Load` efface les cibles inscrites**, et les movers se réinscrivent APRÈS.
Sans ça, une porte de la carte précédente resterait joignable sous son nom — actionnée mais
invisible. Même classe de défaut que l'indice de volume conservé d'une carte à l'autre.

⚠️ **L'ordre dans la boucle n'est pas négociable** : `StepMovers` → `RebuildWalkable` →
`CarryPlayer` → `_motor.Step`. Une boîte de collision qui date du pas précédent laisse
traverser une porte fine pendant son mouvement.

⚠️ **Les portes bougent aussi console ouverte**, volontairement : la console est le seul moyen
d'en actionner une en headless, les `--exec` partant tous à t=0.

✅ **[Corrigé le 07/10 : `prop_crate` et `prop_mannequin`.]** ⚠️ **Le mannequin décoratif et les 4 caisses du bac à sable gênent toute carte chargée.**
Pour les captures ci-dessus il a fallu `character_z -55` et `crates clear`. C'est la même dette
que la note « les 4 caisses ne font pas partie de la carte » — elle empire à mesure qu'on
charge de vraies cartes, et elle n'est **toujours pas traitée**.

### Script Lua — licences tranchées, rien d'installé

✅ **LuaCSharp (`nuskey8/Lua-CSharp`) est retenue : MIT, chaîne transitive MIT comprise.**
Pure C#, **aucun binaire natif** — c'est l'argument décisif après miniaudio et ses dossiers
`runtimes/`. Sans génération d'IL, donc compatible AOT. Lua 5.2. Maintenue (dernière version
29/07/2026).

❌ **MoonSharp écartée** : BSD 3-clauses, donc recevable, mais **dernière version NuGet
octobre 2016**. ❌ **NLua écartée** : MIT, mais elle dépend de Lua natif, donc des binaires par
plateforme.

⚠️ **Réserve : LuaCSharp est en 0.5.6, pas en 1.0**, et totalise 71 800 téléchargements. C'est
peu. À isoler derrière une façade maison, exactement comme miniaudio l'est derrière
`AudioDevice`. Détail dans `DEPENDENCIES.md`.

⚠️ **Le paquet n'est PAS installé.** Rien n'est écrit côté script. Le câblage sorties → entrées
passe d'abord : une sortie pourra ensuite appeler un script au lieu d'une entrée intégrée.

## Entités dans les cartes (17/08) — la brique qui manquait

⚠️⚠️ **LES ENTITÉS EXISTENT MAIS NE FONT RIEN ENCORE.** On peut les poser, les déplacer, les
nommer, les enregistrer et les relire. Seuls les **points d'apparition** sont interprétés par le
moteur. Checkpoints, déclencheurs, événements et PNJ sont du **décor de données** : E13 et E14
dans `ROADMAP.md`.

`MapEntity` = un **type** (chaîne libre) + position + lacet/tangage + des **propriétés
clé/valeur**. C'est le modèle de Hammer, et il évite de toucher au format à chaque nouveau type
d'entité. Types connus : `info_player_spawn`, `trigger_checkpoint`, `trigger_volume`,
`npc_dummy`, `info_waypoint`.

⚠️ **Un type INCONNU est conservé, pas refusé.** Refuser ferait *perdre* l'entité en relisant
une carte faite pour une version ultérieure — le pire des deux mondes.

⚠️⚠️ **`MapEntity` EST UNE CLASSE, pas une structure**, contrairement à `MapBox` et `MapLight`.
`MapData.Clone()` doit donc les **cloner** et pas les `AddRange`. Vérifié par mutation : avec une
copie superficielle, modifier la copie fait passer l'original de 4 à **999**, et l'annulation de
l'éditeur restitue correctement la géométrie mais **pas** les entités. On chercherait le bug
dans l'annulation alors qu'il serait dans la copie.

⚠️ **Les propriétés sont un `SortedDictionary`, pas un `Dictionary`.** L'ordre de parcours d'un
`Dictionary` n'est pas garanti : deux enregistrements de la même carte donneraient deux fichiers
différents et l'aller-retour octet pour octet de 4.2 tomberait **de façon intermittente**.

⚠️ **Format passé en version 2**, mais il **lit encore la 1** (`MinimumVersion`). Sans cette
borne basse, le bump aurait rendu illisibles les deux cartes du dépôt. **`arene.map` et
`essai.map` restent volontairement en version 1** : ce sont la preuve vivante de la
compatibilité arrière.

⚠️ **Le format découpe sur les espaces** : une valeur de propriété n'en contient pas. Elles sont
remplacées par `_` à l'écriture, plutôt que d'écrire un fichier qu'on ne saurait pas relire.
Limite assumée — aucune propriété utile aujourd'hui n'a besoin d'espaces.

⚠️ **Une propriété sans `=` est FATALE.** Une faute de frappe avalée donnerait un déclencheur
qui ne se déclenche pas, sans que rien ne le dise.

📏 **Vérifié sur le fichier** : entité `info_player_spawn` en `9 0 -9` → le moteur démarre en
**(9, 0, −9)**, alors que la carte déclare `spawn 0 1 12`. Sans entité, repli sur **(0, 1, 12)**.
Le choix est le **premier** point d'apparition, pas un tirage : un tirage rendrait les captures
headless incomparables d'une session à l'autre.

Éditeur : `F2` apparition · `F3` checkpoint · `F4` déclencheur · `F5` PNJ · `F6` point de passage.
⚠️ **On démarre à F2 parce que F1 ouvre la console.** Console : `ed_entity`, `ed_ent_select`,
`ed_prop`, `ed_list`.

⚠️ **Au clic, l'entité gagne à égalité de distance.** Un marqueur est presque toujours posé *sur*
une boîte ; privilégier la boîte rendrait les entités quasi impossibles à désigner.

## Formes et panneau de propriétés (17/08) — « tout était le même cube »

**Le reproche, et il était juste** : poser une boîte, un PNJ ou un checkpoint donnait
**exactement le même cube de 60 cm**, distingué par la seule couleur. On ne pouvait pas voir
ce qu'on éditait — ni la nature, ni la portée, ni l'orientation de ce qu'on posait.

### 1. Cinq formes, et un format qui reste compatible

`MapShape` : **bloc, sphère, cylindre, rampe, cône**. `MapBox` porte une forme, le format passe
en **version 3**, et `MinimumVersion` reste à 1. Console : `ed_shape`. Clavier : **B**.

⚠️⚠️ **LA COLLISION RESTE LA BOÎTE ENGLOBANTE, POUR TOUTES LES FORMES.** Une rampe **ne se
monte pas** : on bute dessus comme sur un mur. C'est une entorse assumée au principe « ce qu'on
voit est ce qui bloque » affiché en tête de `MeshBuilder`, et elle est **bornée** : une forme ne
déborde jamais de sa boîte, donc la collision est toujours plus *généreuse* que le visuel. Le
pire défaut possible est « je suis bloqué par du vide », qui se voit tout de suite — pas « je
traverse un mur », qui se découvre trois heures plus tard. La collision convexe est un chantier
à part (`CharacterMotor` et `PhysicsWorld` ne connaissent que des AABB).

⚠️⚠️ **LE BLOC N'ÉCRIT RIEN DANS LE FICHIER.** Ce n'est pas de l'économie de place : c'est ce
qui garantit qu'une carte sans forme exotique se réenregistre **octet pour octet** comme avant.
📏 Vérifié par mutation : en écrivant systématiquement le nom de la forme, `Une_Carte_SANS_Forme_S_Ecrit_Exactement_Comme_Avant`
tombe — **et lui seul**. La preuve de 4.2 serait perdue sans qu'aucune autre vérification ne bronche.

⚠️ **La normale d'un ellipsoïde n'est PAS la direction du centre vers le point** : c'est
`(x/a², y/b², z/c²)` normalisé. Sur une boule les deux coïncident — l'erreur ne se verrait
**jamais** sur une sphère de test, seulement sur une boîte aplatie. Même classe de piège que la
matrice des normales de R6. D'où la carte de démonstration qui contient un cylindre **étiré** et
une rampe **plus large que haute**.

⚠️ **Un nom de forme inconnu est FATAL**, avec son numéro de ligne. Retomber sur le bloc en
silence muerait une rampe en cube, donc boucherait un passage, et on chercherait le défaut dans
la géométrie plutôt que dans une faute de frappe. Les noms **anglais** sont acceptés en plus des
français (`sphere`, `wedge`, `cylinder`) : le format est fait pour être écrit à la main.

### 2. Une silhouette par type d'entité

Tout est bâti à partir des primitives ci-dessus — **aucun modèle à charger**, donc un marqueur ne
peut pas manquer parce qu'un asset est absent.

| Type | Ce qu'on voit |
|---|---|
| `info_player_spawn` | socle plat + cône + **flèche du lacet** |
| `trigger_checkpoint` / `trigger_volume` | le **volume réel**, en fil de fer, à sa taille `size`/`height` |
| `npc_dummy` | une **silhouette humaine de 1,80 m** + les deux bords de son champ de vision |
| `info_waypoint` | une bille sur un mât |
| `func_door` | sa **course** : axe et distance, invisibles autrement |

⚠️ **Le volume d'un déclencheur est calculé par `EntityWorld.VolumeOf`**, la même fonction qui
teste le joueur. Un marqueur de taille fixe mentirait sur la portée — c'est précisément ce qu'on
reprochait au cube.

⚠️ **Les bords du champ de vision partent des YEUX**, comme la ligne de vue elle-même. C'est la
seule chose qui rende `sight` et `fov` **jugeables** : deux nombres ne disent pas si le garde
couvre le couloir, deux lattes posées dans le décor, si.

⚠️ **Le PNJ n'est PAS le modèle glTF.** Il est facultatif au démarrage, et il serait dessiné en
pose de liaison, bras écartés — ce qui ressemble à un bug plutôt qu'à un repère. Les PNJ simulés
sont en outre **masqués dans l'éditeur** : `AgentWorld` est rechargé à chaque reconstruction de
géométrie, ils apparaîtraient donc à côté de leur propre marqueur.

🐛 **Défaut trouvé À L'ÉCRAN et nulle part ailleurs** : le surlignage de sélection était resté un
**cube**. Une sphère sélectionnée disparaissait dans un cube orange — on ne voyait plus l'objet
qu'on était en train de régler. Aucun test de `Core` ne peut voir ce que le surlignage dessine.

### 3. Le panneau de propriétés (façon Roblox Studio)

`PropertyPanel` + `EntitySchema` dans **`Core`** (règle n°1) : la machine à états d'un panneau —
focalisé ou non, en saisie ou non, tampon validé ou abandonné — se teste en ligne de commande.
Le jeu ne fait que traduire les touches et dessiner `Fields`.

**Tab** prend/rend le clavier · **↑↓** choisir · **Entrée** modifier · **←→** changer la forme ·
**Échap** abandonner.

⚠️⚠️ **LA VALEUR AFFICHÉE ET LA VALEUR ÉDITÉE NE SONT PAS LA MÊME CHAÎNE.** L'affichage arrondit
à quatre décimales ; le tampon part de la valeur **complète**. Sinon, ouvrir un champ et le
valider sans rien changer déplacerait le mur de 2e-7 — invisible, et cumulatif à chaque passage.
📏 Vérifié par mutation : en faisant partir le tampon de `Display`,
`Ouvrir_Un_Champ_Et_Valider_Sans_Rien_Changer_Ne_DEPLACE_Rien` tombe, **et lui seul**.

⚠️⚠️ **LA CONTREPARTIE, trouvée par le test et pas en relisant le code** : si la frappe *ajoute*
à cette valeur exacte, taper « 7.5 » sur un champ valant 2 donne **27,5**. La valeur s'ouvre donc
« toute sélectionnée », comme dans Excel : la première frappe **remplace**, le retour arrière
efface tout puis passe en édition normale.

⚠️ **Une valeur GRISE veut dire « absente du fichier, c'est le défaut du moteur ».** Sans cette
nuance on croit avoir réglé une propriété qu'on n'a jamais écrite, et copier l'entité sur une
autre carte ne donne pas le même résultat.

⚠️ **Vider un champ RETIRE la propriété** au lieu d'écrire `""`. Les deux ne sont pas la même
chose : l'absente prend le défaut du moteur, la vide impose la chaîne vide.

⚠️ **`EntitySchema` doit suivre le code, et c'est son seul risque.** Chaque clé y a été relevée
dans le code qui la **lit** — `AgentWorld`, `EntityWorld`, `Mover`, le câblage des portes — pas
recopiée d'une note. Une clé inventée serait pire qu'une clé absente : elle apparaîtrait dans le
panneau, on la réglerait, et rien ne se passerait. Un défaut affiché faux a le même effet, en
plus discret.

⚠️ **La saisie passe par `KeyChar`, jamais par `KeyDown`.** `KeyDown` donne une touche
**physique** : sur un clavier français, `Key.Q` produit un « a ». Déduire le caractère de la
touche donnerait un nom de cible faux sur tout clavier non américain — et juste sur le mien.

⚠️ **Le panneau avale TOUT pendant la saisie.** Sinon « C » dupliquerait l'objet à chaque lettre
d'un nom de cible, et ZQSD ferait voler la caméra pendant qu'on tape.

📸 **Preuve** : `maps/formes.map`, écrite **entièrement à la main** en version 3, se charge et se
rend — 10 objets, 6 entités, les cinq formes alignées au même volume de 2 m. Le panneau affiche
les neuf réglages du PNJ, `target`/`input_spot`/`input_lose` **grisés** parce qu'absents du
fichier.

## Chantier en cours — menu et éditeur de niveaux (4.1, première étape)

⚠️⚠️ **CE N'EST PAS UN ÉDITEUR À LA HAMMER, ET IL NE FAUT PAS LE PRÉSENTER COMME TEL.** La
demande était « comme Half-Life ou Unreal » ; ce qui est livré est le **cœur** de cette boucle —
sélectionner, déplacer, redimensionner sur une grille, annuler, enregistrer — plus un menu et
des états d'application. Le découpage complet, avec ce qui manque (E6→E10), est dans
`ROADMAP.md`.

**Trois états** : `Menu`, `Playing`, `Editing`. Les entrées sont **dispatchées selon l'état**,
sinon le clic de sélection tirerait une balle et les flèches feraient tourner la caméra en même
temps qu'elles déplacent un mur.

`EditorState` et `MenuModel` sont dans `Core` : l'historique d'annulation, qui est la partie la
plus facile à casser d'un éditeur, se teste en ligne de commande.

📏 **Chaîne complète vérifiée sur le FICHIER**, pas à l'œil : sur `maps/essai.map`, la boîte 7
passe de `0 0.9 -7` à `0 2.9 -7` après 2 pas de 1 m, les 10 boîtes et le point de départ
préservés. Puis 3 opérations + 3 annulations + enregistrement redonnent **exactement**
`box 0 0.9 -7  8 1.8 4`.

⚠️⚠️ **L'ANNULATION SE FAIT PAR INSTANTANÉS, PAS PAR COMMANDES INVERSES.** Une carte fait
quelques dizaines de kilooctets : on copie tout avant chaque opération. Les inverses sont là où
vivent les bugs — il suffit qu'une opération ait été **bornée** (un redimensionnement qui bute
sur la taille minimale) pour que son inverse ne la défasse plus, et la carte dérive en silence.

⚠️ **Un geste maintenu ne compte que pour UNE annulation.** `BeginStroke` / `EndStroke` groupent.
Vérifié par mutation : sans le regroupement, 60 appels donnent **61 annulations** au lieu d'une.

⚠️ **Le « rétablir » est invalidé par toute nouvelle opération.** Sans ça il ressuscite une
branche abandonnée de l'historique et produit une carte qui n'a jamais existé.

⚠️ **Redimensionner déplace UNE face**, l'opposée ne bouge pas — sémantique de Hammer. Grossir
symétriquement serait plus simple mais désalignerait les deux faces.

⚠️ **La création accroche le COIN, pas le centre.** Accrocher le centre d'une boîte de taille
impaire poserait ses faces entre deux lignes, et plus rien ne s'emboîterait.

⚠️ **Déplacer n'accroche PAS à la grille**, ça avance d'un multiple du pas. Vérifié par mutation :
en accrochant, une boîte alignée sur 0,5 se fait recentrer au premier appui et perd son
alignement — défaut insidieux, parce qu'il n'arrive qu'une fois.

⚠️ **Le bornage de taille doit être EXACT, pas calculé.** Un test exigeait ≥ 0,05 et mesurait
**0,0499999523** après 200 réductions : on reconstruit min/max depuis le centre à chaque appel,
et l'erreur d'arrondi s'accumule. **Une borne qui dérive n'est pas une borne.**

⚠️ **Entrer dans l'éditeur EFFACE les caisses.** Le décor va bouger sous elles ; les garder les
laisserait coincées dans des murs déplacés.

⚠️ **La géométrie est reconstruite une fois par IMAGE, pas par appui.** Une flèche maintenue
produit vingt opérations par seconde, et chacune reconstruirait le maillage et le monde de
collision.

⚠️ **La sélection est marquée par une boîte gonflée de 2 cm dessinée par-dessus.** On ne peut pas
teinter une boîte du décor : tout le décor est un **seul** maillage. Le gonflement évite le
combat de profondeur.

⚠️ **La grille est faite de lattes de 1,2 cm**, pas de lignes : le moteur ne dessine que des
triangles, il n'a pas de primitive de ligne.

Clavier de l'éditeur : `ZQSD` voler · `Espace`/`X` monter-descendre · `Maj` vite ·
clic sélectionner · `1/2/3` axe · `←→` déplacer · `↑↓` redimensionner · `Entrée` créer ·
`B` forme (`Maj+B` en arrière) · `Tab` panneau de propriétés ·
`Suppr` effacer · `C` dupliquer · `G` grille (`Maj+G` plus fine) · `H` trame ·
`Ctrl+Z`/`Ctrl+Y` · `Ctrl+S` · `P` poser le départ · `Échap` menu.

🐛 **Deux défauts trouvés en RELISANT le code, pas en jouant** — corrigés le 17/08 :

1. **`AimPointOnFloor()` partait de l'œil du JOUEUR**, pas de la caméra de l'éditeur. Le corps
   simulé reste figé là où on l'a quitté pendant que la caméra vole : `P` posait donc le point de
   départ par rapport à une position abandonnée, à des dizaines de mètres de ce qu'on regarde.
   `AimOrigin` distingue maintenant les deux selon l'état.

2. **`Ctrl` partageait ses lettres avec les touches de vol.** `Ctrl+Z` faisait voler en avant
   pendant qu'on annulait, `Ctrl+S` reculer pendant qu'on enregistrait — le raccourci marchait,
   mais la caméra partait avec, donc on ne voyait pas le résultat de sa propre annulation.
   `Ctrl` coupe désormais le déplacement, et « descendre » est passé de `Ctrl` à **`X`**.

⚠️ **Ces deux défauts avaient en commun d'être invisibles aux tests** : `EditorState` est correct,
c'est le *branchement* des entrées qui était faux. Les tests de `Core` ne peuvent pas voir ça —
même leçon que « le joueur contre les caisses » du 16/08.

Console : `menu`, `play`, `editor`, `ed_select`, `ed_axis`, `ed_move`, `ed_size`, `ed_undo`,
`ed_redo`, `ed_save`, `ed_grid`, `ed_show_grid`. **Elles existent d'abord pour la vérification** :
sans elles l'éditeur ne serait atteignable qu'au clavier, donc invérifiable en headless.

## Chantier terminé — profileur GPU, et le coût du rendu ENFIN mesuré

`GpuProfiler` (Engine) chronomètre chaque passe avec des horodatages OpenGL. Console : `gpu 1`,
rapport une fois par seconde à côté de `perf`.

📏 **Le relevé, RTX 5060 Ti, 1280×720, ~1 500 img/s, moyenné sur ~1 570 relevés/s :**

| Passe | ms | Part du GPU |
|---|---|---|
| `ssao calc` | **0,189** | 34 % |
| `geometrie` | 0,180 | 32 % |
| `bloom` | 0,101 | 18 % |
| `ssao geom` | 0,037 | 7 % |
| `ombre` | 0,022 | 4 % |
| `ciel` | 0,014 | 2,5 % |
| `composition` | 0,013 | 2,3 % |
| `hud` | 0,001 | 0,2 % |
| **total** | **0,557 ms** | **3,3 % d'un budget 60 img/s** |

✅ **LA SSAO COÛTE 0,226 ms, soit 41 % du temps GPU.** C'est la réponse à l'aveu ouvert depuis
1.7 (« coût non mesuré »). Confirmé en A/B : `ssao 0` fait tomber le total à **0,335 ms**.
De même `bloom 0` → **0,456 ms**. Les mesures se recoupent.

📏 **Particules** : 2 000 bouffées de fumée coûtent **0,16 à 0,32 ms** de GPU. Ça varie
beaucoup avec la surface couverte à l'écran — normal, un billboard translucide se paie au
pixel, et de la fumée qui grossit finit par remplir l'écran.

⚠️⚠️ **LE PIÈGE CENTRAL D'UN PROFILEUR GPU : lire un résultat trop tôt DÉTRUIT la mesure.**
Le GPU travaille avec plusieurs images de retard. Demander le résultat juste après avoir posé
la requête force le pilote à attendre — et ce n'est pas seulement lent, c'est **faux** : on
supprime le recouvrement CPU/GPU, chaque passe paraît plus chère, et on profile la situation
qu'on a créée en mesurant. D'où l'anneau : on lit l'image N−16, terminée depuis longtemps.

🐛 **Deux bugs trouvés par les chiffres eux-mêmes, dans l'ordre :**

1. **Moyenne divisée par le nombre d'IMAGES au lieu du nombre de MESURES.** Résultat :
   0,005 ms de GPU total à 1 530 img/s — une image de 0,65 ms rendue en un centième de son
   temps. Le signal qui a alerté : les chiffres **baissaient quand le jeu accélérait**.
2. **Anneau trop court.** Trois images à 1 500 img/s ne font que 2 ms : sur une fenêtre d'une
   seconde on ne relevait parfois que **6 mesures sur 1 500**. Le risque n'était pas la
   faiblesse de l'échantillon mais son **biais** — seules les images où le GPU avait pris de
   l'avance arrivaient à temps, donc les moins chargées. Porté à 16 images. **Vérifié : les
   valeurs n'ont pas bougé** (0,557 contre 0,569), le biais n'était pas matériel ici.

⚠️ **Le nombre de relevés est affiché dans le rapport**, et c'est ce qui a permis de voir le
second bug. Une moyenne sans son effectif ne se juge pas.

⚠️ **`light_sun 0` ne coupe PAS la passe d'ombre** — mon A/B sur les ombres était donc invalide.
Le 0,022 ms vient de la mesure directe, pas d'une différence.

⚠️ **Ceci mesure le temps GPU, pas le temps CPU de soumission.** Une passe peut être quasi
gratuite pour le GPU et coûteuse à préparer. Non mesuré.

## Chantier terminé — son 3D (2.1)

**Dépendance tranchée le 16/08 : miniaudio**, licence **Unlicense / MIT-0** lue sur le dépôt.
**OpenAL Soft écartée** : son `COPYING` dit « GNU LIBRARY GENERAL PUBLIC LICENSE, Version 2 » —
copyleft, donc règle n°2. Liaison `Hexa.NET.MiniAudio` 1.0.1 (MIT). Architectures **vérifiées
en inspectant le paquet installé** : `win-x64/x86/arm64`, `linux-x64/arm64`, `osx-x64/arm64`,
Android. Mac et Linux sont couverts — c'était l'objection décisive contre un backend WASAPI.

⚠️ **On n'utilise QUE le bas niveau** — « remplis ce tampon ». `AudioMixer`, `AudioClip`,
`SoundGen`, `Wav`, `AudioRecorder` sont dans `Core` et se testent en ligne de commande.
`AudioDevice` (Engine) est la **seule** classe qui touche miniaudio.

⚠️⚠️ **JE NE PEUX PAS ENTENDRE.** Le WAV est au son ce que la capture d'écran est à l'image :
`--audio-dump fichier.wav --audio-seconds N` enregistre ce que la carte reçoit. Sans ça, un
défaut audio ne se découvrirait qu'à l'oreille de quelqu'un d'autre.

📏 **Chaîne vérifiée de bout en bout par la mesure** : `snd_test 440 -1` → pic gauche **0,3000**,
pic droite **0,0000**, et **441,0 Hz** mesurés par comptage de passages à zéro. Puis
`snd_volume 0.25` + panoramique +1 → droite **0,0750**, exactement la valeur attendue
(0,5 × 0,6 × 0,25).

🐛 **Un vrai bug trouvé par cette mesure** : `snd_volume` valait 0,7 dans la ConVar et 1,0 dans
le champ du mélangeur. Le setter n'étant appelé qu'au changement, la console affichait 0,7 et
le son sortait à 1,0. **C'est le piège des ConVar déjà documenté pour la SSAO** — et c'est
l'amplitude relevée dans le fichier qui l'a trahi, rien d'autre.

⚠️⚠️ **`AudioMixer.Mix` TOURNE SUR LE FIL AUDIO** : ni verrou, ni allocation, ni exception. Le
fil de jeu remplit les champs d'une voix **puis** lève un drapeau avec `Volatile.Write` ; le
fil audio lit le drapeau **puis** les champs. Sans cette barrière, le processeur peut
réordonner et le fil audio voit une voix « active » dont le clip n'est pas encore écrit.

⚠️ **Panoramique à PUISSANCE CONSTANTE**, pas linéaire. Le réflexe `(1±p)/2` donne 0,5 des deux
côtés au centre — et l'oreille perçoit la **puissance**, donc un son qui traverse l'écran
s'affaiblit en passant au milieu. Avec la racine, la somme des carrés vaut 1 partout.
Vérifié par mutation : en linéaire, la puissance tombe à **0,625** à un panoramique de −0,5.

⚠️ **Bornage obligatoire à l'écriture du WAV.** Vérifié par mutation : sans lui, **+1,5 revient
à −0,50** — le signe s'inverse d'un échantillon à l'autre, ce qui s'entend comme une explosion
de bruit, pas comme une saturation.

⚠️ **La fréquence vient de la CARTE**, elle ne se décide pas : demander 48 000 sur un
périphérique à 44 100 donne 44 100. Le mélangeur est construit sur la valeur réelle, sinon tous
les sons joueraient trop aigus.

### S3 — spatialisation, et S4 — occlusion

`Spatializer` (fonctions pures) + `AudioWorld` (suivi des sources), les deux dans `Core`.
Partage des rôles : **le fil de JEU calcule** distances, produits scalaires et lancers de rayon ;
**le fil audio applique**. C'est ce qui permet à l'occlusion de parcourir tout le décor sans
jamais risquer de bloquer la carte son.

📏 **Atténuation vérifiée au chiffre près** (modèle inverse borné, `ref/(ref+rolloff·(d−ref))`) :
source à 3 m → **0,2498** mesuré pour **0,2500** prédit ; à 12 m → **0,0625** pour 0,0625.
Rapport **4,00×**, exactement la formule.

📏 **Occlusion mesurée** : un 6 kHz derrière un pilier passe de **0,0469 à 0,0022** — **20,9×**.
Un mur fait **deux** choses : il étouffe (passe-bas d'ordre 1) et il atténue (gain 0,45). N'en
faire qu'une donne soit un son lointain mais clair, soit un son étouffé mais aussi fort — ni
l'un ni l'autre ne se lit comme « de l'autre côté d'une cloison ».

⚠️⚠️ **L'AVANT ET L'ARRIÈRE SONT INDISTINGUABLES, et c'est une limite de la technique.** Le
panoramique d'amplitude n'a que deux haut-parleurs et une seule information : gauche/droite.
Une source droit devant et une source droit derrière donnent **rigoureusement** le même
résultat. Un test (`L_Avant_Et_L_Arriere_Sont_INDISTINGUABLES`) fige cette limite pour qu'elle
ne soit pas redécouverte comme un bug. **Pour un FPS tactique c'est une vraie perte
d'information de jeu** — la corriger demande une HRTF, donc des données de mesure.

⚠️ **Le rayon d'occlusion s'arrête À la source.** Vérifié par mutation : avec une portée fixe,
tout son ayant un mur quelque part derrière lui est étouffé — c'est-à-dire presque tous, dans
un couloir.

⚠️ **La borne basse de l'atténuation n'est pas une précaution.** Vérifié par mutation : sans
elle, une source à la position exacte du joueur donne un gain **infini**. Et ce n'est pas
théorique — c'est ce que fait tout jeu pour les pas du joueur lui-même.

⚠️ **La mémoire du passe-bas survit d'un tampon à l'autre.** La remettre à zéro à chaque appel
ferait un claquement toutes les quelques millisecondes, à la frontière des tampons. Elle est en
revanche remise à zéro quand une voix est **réutilisée**, sinon le nouveau son démarrerait sur
la valeur de l'ancien.

⚠️ **Doppler borné à [0,25 ; 4].** À la vitesse du son la formule diverge ; une vitesse
aberrante — un corps éjecté par une explosion — suffit à y arriver.

⚠️ **Piège de mesure rencontré** : la première mesure d'atténuation donnait un rapport de
**16,9×** au lieu de 4×. La source lointaine se trouvait **derrière le mur de l'arène** : le
chiffre mélangeait distance et occlusion. Refaite avec `snd_occlusion 0`, elle tombe pile.
Toujours isoler ce qu'on mesure.

Console : `snd`, `snd_volume`, `snd_test [hz] [pan]`, `snd_impact [pan]`, `snd_spatial`,
`snd_occlusion`, `snd_ref`, `snd_max`, `snd_rolloff`, `snd_doppler`,
`snd_place [x y z] [hz]` (son en boucle, pour tourner autour), `snd_stop`.

⚠️ **AUCUN RESSENTI VALIDÉ.** Rien de tout ça ne dit si c'est agréable ou lisible en jeu.
Il faut écouter.

## Chantier terminé — particules (2.3)

`ParticleSystem` dans `Core` (réservoir fixe de 4 096, aucune allocation après
construction), `ParticleEffects` pour les recettes, `ParticleRenderer` dans `Engine`.
Console : `particles`, `fx_impact`, `fx_explode`, `fx_impact_at`, `fx_smoke`, `fx_clear`, `fx`.

**Simulation au PAS FIXE**, comme la physique — pas au rendu comme l'animation. Une animation
est une *fonction du temps* ; une particule a un état qui s'accumule. La simuler au rendu
ferait dépendre sa trajectoire du framerate, et deux captures `--seconds 3` ne donneraient plus
la même image. Prix à payer : des bonds de 15 cm à 10 m/s, d'où `Particle.Previous` et
l'interpolation au rendu.

⚠️⚠️ **LES COULEURS SONT EN LINÉAIRE HDR, PAS EN COULEURS D'ÉCRAN.** Piège mesuré : un « gris
0,15 », écrit spontanément en pensant à un gris sombre, traverse exposition ×1,4 + ACES + sRGB
et **ressort à 0,59** — quatre fois trop clair. La première fumée était un cumulus blanc.
Pour viser une valeur d'écran `v` : `linéaire ≈ ACES⁻¹(v^2,2) / 1,4`, soit **0,037** pour un
gris d'écran de 0,25. Vérifié par capture : couleur 0,04 pleinement opaque → **67/255**, la
valeur prédite exactement.

📏 **Les particules alimentent VRAIMENT le bloom** : `bloom 1` contre `bloom 0` sur une
explosion modifie **53 251 pixels sur 921 600** (5,8 %, écart max 641/765). En 1.8, la scène
entière n'en donnait que **1 626** — ×32. C'est la réponse à la note de 1.8 qui disait que le
bloom resterait discret faute de surface émissive : maintenant il y en a.

📏 **Le rebond est TOUT le coût, et c'est mesuré.**

| Charge | Coût du pas | Part des 15,6 ms |
|---|---|---|
| 3 400 particules **sans** rebond | **0,089 ms** | 0,6 % |
| 3 480 dont **1 080 qui rebondissent** | **2,42 ms** (pire 4,52) | 16 % (pire 29 %) |

Soit **27×**. Un rebond = un rayon contre les 32 boîtes du décor, ≈ 2,2 µs. Mettre `Bounce = 0`
sur la fumée et la poussière n'est donc pas un détail d'optimisation, c'est la conception.

⚠️ **Le réservoir REFUSE quand il est plein, il ne recycle pas.** Recycler la plus vieille
serait plus joli mais coûte un parcours complet par ajout, et le tableau n'est plus trié par
âge dès la première mort. `Dropped` compte les refus et `fx` les affiche — un débordement
silencieux est ce qui fait qu'un système de particules « arrête de marcher » sans raison.

⚠️ **Tri seulement pour les opaques.** L'additif est indépendant de l'ordre (additionner des
lumières donne le même total dans n'importe quel sens) ; la transparence ordinaire, non.
Profondeur **testée mais pas écrite** : sans le test, les particules traverseraient les murs ;
avec l'écriture, la première dessinée ferait un trou noir en forme de disque.

**Instanciation** : un quadrilatère envoyé une fois, 32 octets par particule au lieu de 160.
Le billboard est orienté **dans le nuanceur** à partir des axes de la caméra.

⚠️⚠️ **`--no-build` M'A ENCORE EU, et j'avais cité la règle dans la même session.** J'ai passé
six étapes à diagnostiquer une fumée « toujours blanche » après l'avoir corrigée : le binaire
était périmé. Sans `--no-build`, la même image donne 67,53,51 là où elle donnait 78,41,28 sans
particules — la fumée neutralise bien le pilier chaud. **Ne jamais utiliser `--no-build`, même
derrière un `dotnet build` réussi dans la même commande.**

## Chantier terminé — mélange de poses (2.2)

Une animation seule fait bouger un personnage. Elle ne fait pas un jeu : entre l'arrêt, la
marche et la course il faut des états **intermédiaires**, et personne n'anime à la main les
quinze vitesses entre 1,4 et 5 m/s. `PoseBlend` mélange deux poses, `Animator` gère le temps.

⚠️⚠️ **LE PIÈGE EST TEMPOREL, PAS GÉOMÉTRIQUE.** « Walking » dure **1,033 s**, « Running »
**0,633 s**. Les échantillonner au même temps **absolu** les désynchronise aussitôt : à
t = 0,633 s la course repose le pied, la marche n'en est qu'à 61 % et a la jambe en l'air. Le
mélange donne le grand écart — et **ça empire avec le temps**. On raisonne donc en **phase**
(0,25 = « au quart du pas », dans les deux cycles), et la phase avance à une cadence
elle-même mélangée, sinon la fréquence du pas sauterait à la bascule.

📏 Mesuré par mutation : en échantillonnant au temps absolu, la désynchronisation est
attrapée **dès un mélange de 0,15** (0,64 attendu, 1,05 obtenu — 64 % d'erreur).

⚠️ **`UpdateCharacterPose()` est appelé PLUSIEURS FOIS par image** (ombre, profondeur SSAO,
passe finale) — les trois doivent décrire le même personnage au même instant. L'avance se
calcule donc depuis une **horloge**, jamais depuis un delta reçu : les appels suivants
trouvent un écart nul. Avec un delta, l'animation tournerait au **triple** de sa vitesse.

⚠️ **L'horloge est `_simTime + alpha × pas`** : lisse — elle avance entre deux pas fixes, sinon
ça saccade au-dessus de 64 img/s. Ce raisonnement vit désormais dans le moteur, sur
`FixedTimestep.RenderTime`, avec 3 tests et 3 mutations (voir le chantier « utilisable »).

❌ **CORRECTION DU 17/08 — « deux captures `--seconds 3` rendent la même pose » ÉTAIT FAUX.**
Mesuré : deux lancements de la *même* commande, personnage plein cadre, donnent **231 pixels
différents sur 921 600, écart max 278/765**. Ce n'est pas du bruit — c'est un membre à une
autre place. La cause est `alpha` : la capture part sur la première image qui franchit
`_simTime > N`, et cette image tombe à un `alpha` différent à chaque lancement, soit jusqu'à
15,6 ms d'animation d'écart.

📏 **Conséquence pratique pour toute comparaison A/B** : sur une scène **animée**, un écart de
quelques centaines de pixels ne prouve RIEN. Il faut soit `character_speed 0` (fige la pose),
soit `character_phase` (impose la phase), soit comparer sur une scène statique. Le jeu
d'exemple, lui, est **exactement reproductible** — deux captures identiques au bit près,
mesuré : 0 pixel non nul sur 921 600. C'est le meilleur harnais A/B du dépôt.

⚠️ **Un test écrit à mélange 0,5 ne prouve rien sur le slerp.** À mi-chemin le point est
symétrique : l'interpolation linéaire y donne **exactement** le même résultat. Écrit d'abord à
0,5, le test sur le vrai modèle **passait avec une implémentation fausse** — vérifié. À 0,25 il
tombe : `RightLeg` (95,9° d'écart marche/course) donne 22,89° au lieu de 23,99°.

**Ce que le moteur ne peut pas deviner** : `character_offset`. Rien ne garantit que deux
cycles démarrent sur le **même pied** — c'est une propriété du contenu. 0,5 inverse la jambe
d'attaque. À régler à l'œil.

Console : `character_blend` (0 marche → 1 course), `character_offset`, `character_phase`
(-1 = libre ; imposer la phase est le **seul** moyen de comparer deux mélanges au même instant
de la foulée, leurs cadences étant différentes), `character_speed`.

⚠️ **Rien ne pilote le mélange automatiquement.** Il n'y a pas encore de personnage de jeu lié
au `CharacterMotor` — le mannequin de la scène est un accessoire. Le branchement tient en une
ligne (`_characterBlend = f(vitesse)`) le jour où il y aura une entité.

## Réglages sauvegardés (2.4) — `save` et `load`

`save` écrit `exarion.cfg` à côté de l'exécutable, `load` le relit, et le chargement est
**automatique au démarrage** — avant les `--exec`, pour qu'une commande passée en ligne de
commande garde le dernier mot.

**Seules les valeurs MODIFIÉES sont écrites.** Un fichier listant les soixante variables serait
illisible, et surtout il **figerait les défauts** : une amélioration du moteur n'atteindrait
jamais quelqu'un qui a déjà un fichier.

⚠️⚠️ **LA CULTURE. Le piège de ce module, et il était RÉEL sur cette machine.** Sans
`CultureInfo.InvariantCulture`, `9.81f` s'écrit `"9,81"` en français — et se relit comme 981,
ou pas du tout. Vérifié par mutation : en retirant la culture invariante, **deux tests
tombent**, dont l'aller-retour simple qui n'appliquait plus aucune valeur. Ce défaut aurait été
invisible sur un poste anglais.

Les `.cfg` sont dans `.gitignore` : ce sont des réglages personnels, les versionner imposerait
ses goûts à tout le monde.

## Chantier terminé — rendu HDR et bloom (1.8)

Avant, **chaque shader** appliquait lui-même exposition, tone mapping et sRGB. Ça marchait,
mais ça perdait tout ce qui dépasse le blanc : une lampe à 8× la luminosité du papier
ressortait à 1,0, exactement comme le papier. Le bloom a précisément besoin de cette information.

La scène est donc rendue dans un tampon **flottant** (`HdrTarget`), et le tone mapping devient
une passe finale appliquée **une seule fois** à l'image entière.

⚠️ **Conséquence assumée : le ciel passe désormais dans la courbe ACES.** Il y échappait, avec
sa propre conversion sRGB. Vérifié par capture — le dégradé reste propre.

⚠️ **Le MSAA impose deux tampons** : un multi-échantillonné où l'on dessine, et un ordinaire
obtenu par **résolution** (`glBlitFramebuffer`) — un tampon multi-échantillonné ne se lit pas
comme une texture.

⚠️ **Le HUD est dessiné APRÈS la composition** : il est déjà en couleurs d'affichage, le passer
dans l'ACES le délaverait.

📏 **Le piège du seuil, mesuré.** Première version : **62 pixels** modifiés sur 921 600, halo
invisible. Le seuil se comparait aux valeurs **avant exposition**, une échelle qui n'est pas
celle de l'image finale. En exposant d'abord, on passe à **1 626 pixels** — ×26. Le seuil 1,0
désigne alors le point où le tone mapping commence à rouler vers le blanc.

⚠️ **Le bloom reste discret, et c'est du CONTENU, pas du moteur** : la scène n'a **aucune
surface émissive**. Les lampes sont des lumières ponctuelles qui éclairent des murs, pas de la
géométrie lumineuse. Le seul vrai émetteur est le soleil — et là l'écart max monte à 120 contre
28 dans la cour. Des néons ou des écrans changeront ça.

Console : `bloom`, `bloom_threshold`, `bloom_knee`, `bloom_intensity`, `bloom_passes`.

**Flou gaussien SÉPARABLE**, horizontal puis vertical : un flou 2D de rayon N coûte N×N lectures
par pixel, séparé il en coûte N+N. À rayon 9, 81 contre 18. Le tout en **demi-résolution** — un
halo est flou par nature.

## Chantier terminé — occlusion ambiante (SSAO)

L'ambiante actuelle est une **constante** : un point au fond d'un recoin reçoit autant de
lumière du ciel qu'un point en plein air. Les objets ne se posent donc nulle part — une caisse
au sol semble toujours flotter un peu, faute de la pénombre qui se forme dans un angle.

| # | Étape | État |
|---|---|---|
| **S1** | Tampon normales + profondeur en espace vue | ✅ **16/08** — vérifié par capture |
| **S2** | Calcul de l'occlusion (noyau hémisphérique) | ✅ **16/08** — vérifié par capture |
| **S3** | Flou, application à l'ambiante, réglages | ✅ **16/08** — mesuré |

Console : `ssao_debug` (0 rien, 1 normales, 2 profondeur), `ssao_debug_range`.

**S1 — pourquoi une passe de géométrie de plus.** On ne peut pas réutiliser le tampon de
profondeur du rendu final : l'occlusion doit être connue **avant** d'éclairer, et le MSAA rend
ce tampon pénible à relire. `ForEachDrawable()` factorise le parcours de la géométrie — la
passe d'ombre et celle-ci dessinent exactement la même chose, et deux copies divergentes
donneraient une occlusion décalée, introuvable à l'œil.

⚠️ La visualisation de profondeur normalisait sur le **plan lointain (500 m)** : une scène
d'intérieur tient dans 30 m, soit 6 % de l'échelle, et tout ressortait blanc. Les données
étaient bonnes, l'affichage inutilisable. `ssao_debug_range` règle la plage.

**S2 — deux choix qui font toute la méthode.**

**Une DEMI-sphère, pas une sphère.** Les points d'échantillonnage vivent dans l'hémisphère
orienté par la normale. Une sphère entière serait un contresens : la moitié des points
tomberait *derrière* la surface, toujours occultée, et tout serait assombri de moitié
uniformément. Les points sont en plus resserrés vers le centre — ce qui occulte vraiment un
point, c'est ce qui le touche de près.

**Le bruit n'est pas un défaut, c'est l'outil.** Avec un noyau fixe, tous les pixels
échantillonnent les mêmes directions : l'erreur devient un **motif** régulier, parfaitement
visible. En tournant la base d'un angle différent à chaque pixel (mosaïque 4×4 répétée), la
même erreur devient du **grain** — moche, mais qu'un flou efface. On échange une structure
contre du bruit. C'est tout l'objet de S3.

⚠️ **Le rayon est LE réglage qui compte** : il fixe l'échelle des détails assombris. Mesuré :
à 0,45 m l'effet était à peine perceptible dans une scène où les piliers font 1 m. Défaut porté
à **0,8 m**, intensité **1,3**. Au-delà, des halos apparaissent autour des silhouettes.

⚠️ Les valeurs initiales des `ConVar` doivent **coïncider** avec les champs de `Ssao` : le
setter n'est appelé qu'au changement, et une console qui affiche autre chose que la réalité
est un piège.

**S3 — le flou fait exactement la taille de la mosaïque de bruit.** Ce n'est pas un
adoucissement approximatif : chaque pixel de la mosaïque 4×4 a tourné la base d'un angle
différent, donc moyenner une fenêtre de 4×4 fait la moyenne des **quatre orientations**. Le
grain s'annule exactement. Plus large, ça baverait sur les silhouettes ; plus étroit, il
resterait un motif.

⚠️ **L'occlusion ne s'applique QU'À L'AMBIANTE, jamais aux lumières.** Une lampe qui éclaire un
recoin doit l'éclairer : l'occlusion décrit ce que le **ciel** ne peut pas atteindre, pas ce
qui est dans le noir. L'appliquer partout donne l'aspect terne et sale d'une SSAO mal branchée.

📏 **Mesuré** (`ssao 0` contre `ssao 1`, comparaison de pixels) : **1,2 %** des pixels modifiés
en éclairage normal, **2,1 %** quand l'ambiante domine, écart max 115/765. L'effet suit bien la
part d'ambiante — c'est la signature d'un branchement correct, et il est **volontairement
discret**.

⚠️⚠️ **COÛT NON MESURÉ, et c'est un aveu.** Quatre essais : `ssao 1` a donné 1 670 img/s au
deuxième et 2 945 au quatrième, pour un réglage identique. Le framerate n'est comparable que
**dos à dos dans une même session** — piège déjà connu — et quatre lancements sont quatre
sessions. Il faudrait pouvoir basculer `ssao` **en cours de partie** (les `--exec` s'exécutent
tous à t=0). Ce qu'on peut dire : même au pire relevé, 1 670 img/s font 0,6 ms par image, pour
un budget de 16,7 ms à 60 img/s.

## Chantier terminé — import de modèles 3D (glTF maison)

Le modèle du projet est dans `Model3D/`, en **`.glb`** — glTF binaire. Inspecté le 16/08 :
glTF 2.0 sorti de l'exportateur Khronos de Blender, **1 maillage, 1 peau de 24 os,
2 animations (`Running`, `Walking`)**, 1 matériau, textures PNG embarquées. Attributs :
`POSITION, NORMAL, TEXCOORD_0, JOINTS_0, WEIGHTS_0` — le jeu standard d'un maillage skinné.

⚠️ **Toujours exporter en `.glb`, jamais en `.fbx`.** Le FBX est propriétaire Autodesk et
exigerait une bibliothèque tierce ; le glTF est une spécification Khronos publique, et son
conteneur tient en une page. C'est ce qui rend l'import maison réaliste.
`System.Text.Json` fait partie du runtime .NET : **ce n'est pas une dépendance**.

| # | Étape | État |
|---|---|---|
| **M1** | Conteneur GLB : en-tête, morceaux JSON/BIN | ✅ **16/08** — 8 tests |
| **M2** | Accesseurs et géométrie : un maillage statique à l'écran | ✅ **16/08** — 8 tests, capture |
| **M3** | Matériau et texture de base (PNG embarqué) | ✅ **16/08** — 2 tests, capture |
| **M4** | Squelette, peau, matrices inverses de liaison | ✅ **16/08** — 5 tests |
| **M5** | Échantillonnage des animations | ✅ **16/08** — 7 tests |
| **M6** | Skinning GPU | ✅ **16/08** — capture, 2 poses |

**M2 — le piège qui a coûté le plus** : un maillage **skinné IGNORE la transformation de son
nœud**, la spécification l'impose (ce sont les os qui la portent). Ce personnage a un squelette
exporté en **centimètres** — ses os se translatent de 14, 36, 35 — et son nœud racine porte
donc une échelle de 0,01 pour rattraper. Le maillage, lui, est déjà en mètres. En lui
appliquant quand même cette échelle, on obtenait un personnage de **1,7 cm** au lieu de 1,70 m.

Indices dans les bornes, normales unitaires, aucune valeur invalide : **aucune assertion
technique n'aurait bronché.** Seul `Le_Personnage_Est_A_Taille_Humaine_Et_Debout` l'a attrapé.
Écrire ce genre de test « bon sens » vaut plus que dix vérifications de structure.

**M3 — deux façons de plaquer une texture, et il faut les deux.** Le shader a désormais
`uUseMeshUv` :
- **projection planaire depuis le monde** pour le décor. Il est fait de boîtes mises à
  l'échelle : leurs UV de sommets seraient étirées, et un mur de 20 m n'aurait pas les mêmes
  carreaux qu'une caisse de 1 m ;
- **UV du maillage** pour les modèles importés. Un personnage a une texture *dépliée* ; la
  projeter depuis le monde barbouillerait son visage avec le carrelage du sol.

La texture se choisit **par dessin** (`Material.Map`), plus une fois pour toute la scène.

📏 **Notre décodeur PNG maison lit les textures de Blender/Meshy** — 2048×2048. Il n'avait
jamais vu que ses propres aller-retours, ce qui ne prouvait rien : un encodeur tiers utilise
d'autres filtres de ligne et d'autres tailles de bloc.

📸 **Preuve du plaquage** : le patch **« SCP » est lisible** sur le gilet. Un dépliage faux ou
retourné en V ne produirait jamais de texte lisible. Le sens des V est donc bon — les données
PNG arrivent ligne du haut en premier, et `glTexImage2D` fait correspondre cette première
ligne à `v = 0`, exactement là où glTF l'attend.

⚠️ Le visage du modèle est **orange et sans détail** : c'est la texture générée par Meshy, pas
un défaut du moteur.

**M4 — la matrice inverse de liaison, et comment la vérifier.** C'est la pièce qu'on comprend
mal au début : le maillage est modélisé une fois, dans une pose de repos ; pour qu'un os le
déforme, il faut d'abord exprimer les sommets dans le repère de cet os **tel qu'il était au
repos**. C'est exactement ce que fait cette matrice.

📏 **Le test qui prouve tout** : `La_Pose_De_Liaison_Se_Referme_Sur_Elle_Meme`. La matrice
inverse de liaison composée avec la transformation monde de son os doit donner **l'identité**,
puisque l'une fait le trajet inverse de l'autre. Convention de matrice inversée, hiérarchie
parcourue de travers, matrices dans le désordre : n'importe laquelle de ces erreurs fait
exploser le produit. Aucune vérification de structure ne dit ça.

⚠️ Ce test pouvait passer **à vide** — identité × identité fait identité. `Le_Personnage_A_Un_Squelette`
exige donc que plus de 15 des 24 matrices aient une translation réelle.

⚠️ Piège au passage : `stackalloc` **dans une boucle** s'accumule jusqu'au retour de la
méthode. Sur 8 321 sommets, ça faisait 200 Ko de pile. L'analyseur CA2014 l'a signalé — il
faut les sortir des boucles.

**M5 — une rotation ne s'interpole pas comme un point.** Mélanger deux quaternions
linéairement fait passer le résultat *par l'intérieur* de la sphère : la rotation accélère au
milieu du trajet, et un membre semble se détendre d'un coup. `Slerp` suit l'arc.

📏 **Deux mutations, deux leçons** :
- Désactiver le garde-fou du « chemin le plus court » ne cassait **aucun test sur le vrai
  fichier** — Blender exporte ses quaternions dans un hémisphère cohérent, le cas ne s'y
  présente jamais. Un test **fabriqué à la main** (`Une_Rotation_Prend_Le_Chemin_Le_Plus_Court`)
  couvre désormais le cas, avec `q` et `-q`.
- Ce test a révélé mieux : **`Quaternion.Slerp` de System.Numerics inverse déjà** le second
  quaternion quand le produit scalaire est négatif. Ma négation manuelle était du **code mort**,
  supprimée. Le test reste — il vérifie le comportement, d'où qu'il vienne.

⚠️ **`La_Pose_Change_Vraiment_Au_Cours_De_L_Animation`** existe pour la même raison que le
garde-fou de M4 : une animation qui ne fait rien satisfait toutes les propriétés de bon
comportement qu'on peut imaginer.

**M6 — le skinning tient en une ligne.** Chaque os propose une déformation du sommet ; on en
fait la **moyenne pondérée** par les poids. Un sommet au milieu du coude est tiré à moitié par
le bras et à moitié par l'avant-bras, et se place naturellement entre les deux. On mélange les
**matrices**, pas les positions : équivalent ici, et une seule transformation au lieu de quatre.

- `SkinnedVertex` est un type **séparé** de `Vertex`. Sinon chaque sommet du décor et chaque
  caisse traîneraient huit flottants inutiles.
- **48 os maximum**, et ce n'est pas arbitraire : OpenGL 3.3 ne garantit que 1024 composantes
  d'uniform dans le shader de sommets, et 48 matrices en mangent 768.
- ⚠️ **`ClearBones()` après chaque dessin animé.** Les uniforms survivent au dessin : sans ça
  le maillage rigide suivant serait déformé par les os du précédent.
- L'animation est échantillonnée **au rendu**, pas à la simulation — c'est de l'affichage pur,
  et l'échantillonner au pas fixe la ferait saccader dès que le rendu dépasse 64 img/s.

✅ **La passe d'ombre fait aussi le skinning** (corrigé le 16/08). Elle a son propre shader :
sans ces lignes, l'ombre restait figée en pose de liaison — bras écartés, immobile — sous un
corps qui court. Les matrices d'os sont calculées **une fois par image et partagées** entre
les deux passes : deux poses différentes donneraient l'ombre d'un autre personnage.

⚠️⚠️ **PIÈGE D'OUTILLAGE, qui m'a fait conclure trois fois de travers** :
`dotnet run --no-build` exécute le binaire **existant**. Enchaîné derrière un `dotnet build`
dans la même commande, il peut lancer l'ANCIEN exécutable — et j'ai passé plusieurs mesures à
« prouver » que ma correction ne servait à rien, sur des captures produites par un binaire
périmé. **Pour toute comparaison A/B, utiliser `--no-incremental` et surtout PAS `--no-build`.**

📏 Vérifié proprement : `uSkinned` forcé à 0 → empreinte `A2434A5B…`, actif → `C591E423…`.
La différence est réelle et reproductible.

Console : `character_anim` (0 = Running, 1 = Walking), `character_speed` (0 fige la pose),
`character_yaw`, `character_z`.

## Chantier terminé — rotation des corps (OBB maison)

Choix acté le 15/08/2026 **contre** l'intégration de Jolt, par cohérence avec la règle n°3.
Découpage en 7 étapes dans `ROADMAP.md`.

- **R1 ✅** `Obb` + théorème des axes séparateurs (15 axes), 17 tests.
  ⚠️ Piège central déjà traité : **les 9 axes croisés ne sont pas unitaires**. Comparer des
  profondeurs sur des axes de longueurs différentes fait choisir un axe absurde. Ils sont
  normalisés, et les axes dégénérés (sources quasi parallèles) sont ignorés.
- **R2 ✅** Points de contact : `ObbManifold` — face de référence, face incidente, découpage
  de Sutherland-Hodgman, jusqu'à 8 points. 17 tests, zéro allocation.
  ⚠️ Deux pièges traités : le **tremblement par bascule d'axe** — deux caisses à plat donnent
  la *même* profondeur sur l'axe de A et sur celui de B, et le bruit de calcul choisissait au
  hasard, faisant sauter le polygone d'un corps à l'autre ; une hystérésis (5 % + 1 mm, A
  prioritaire) rend le choix reproductible. Et les **points d'appui qui affleurent** : les
  jeter à zéro rétrécirait le polygone d'un pas à l'autre, on les garde jusqu'à 1 mm.
  📏 Mesuré sur 8 344 contacts de face aléatoires : **0 point hors du corps de référence**,
  donc le filet de sécurité ne se déclenche jamais.
- **R3 ✅** Orientation, vitesse angulaire, inertie : `Inertia` + `RigidBody` étendu
  (`Orientation`, `AngularVelocity`, `ApplyImpulseAt`, `GetVelocityAt`, `GetObb`). 25 tests.
  ⚠️ **Le tenseur n'existe pas en mémoire** : diagonal dans le repère propre, et on n'a
  jamais besoin que de son *action* sur un vecteur — `R·I⁻¹·Rᵀ·v` se lit en trois lignes.
  ⚠️ Intégration par **exponentielle exacte** (angle `|ω|·dt` autour de `ω`), pas par la
  linéarisation `q += 0,5·ω·q·dt` qui coupe le virage : un tour complet ne bouclait pas.
  📏 **Vérifié par mutation** : inverser l'ordre de composition n'est attrapé que par
  *un seul* test sur 25 ; et sans renormalisation la norme atteint 1,00116 après 100 000 pas,
  soit une caisse **0,23 % plus grosse** (`Transform` met à l'échelle du carré de la norme).
  ⚠️ **Absent volontairement** : le terme gyroscopique `ω × (I·ω)`. Instable en explicite,
  invisible sur une caisse. Conséquence : pas d'effet Dzhanibekov, un corps libre ne culbute pas.
- **R4 ✅** Solveur à impulsions séquentielles (`ContactSolver`) : les caisses **basculent**.
  Impulsions cumulées bornées sur le **total** (pas l'incrément), passe de vitesse et passe de
  position **séparées** — corriger une position sans toucher aux vitesses est ce qui évite
  d'injecter de l'énergie. Frottement de Coulomb plafonné par l'impulsion normale **réelle**,
  plus par une estimation du poids. 11 tests.
  ⚠️ **Le décor a dû entrer dans le solveur** (contenu prévu pour R7). Sans lui, une caisse
  posée sur une autre glissait sur le côté et tombait : le solveur partage l'impulsion, le sol
  absorbait la part du bas **hors solveur**, la contrainte n'était jamais satisfaite et le
  contact pompait du couple à chaque pas. Preuve : en rendant la caisse du bas **statique**,
  tout était parfaitement stable.
  ⚠️ **Deux vieux raccourcis supprimés** : `Grounded → Velocity.Y = 0` annulait la descente
  *avant* le solveur, donc l'impulsion normale valait zéro, donc la limite de Coulomb aussi —
  une caisse posée sur une autre n'était plus entraînée du tout. Et le rebond de
  `MoveAndSlide` rendait 12 % de la vitesse à **chaque** contact, si lent soit-il : même seuil
  de restitution que le solveur désormais.
  📏 Passes de dégagement portées de 3 à **10** (mesuré : chevauchement restant 9,9 cm → 3,8 →
  1,5 ; au-delà c'est le sommeil qui fige le reste).
- **R5 ✅** Amortissement et sommeil. 4 tests. Trois corrections, dont deux vrais bugs :
  ⚠️ **Un corps endormi était un puits de vitesse.** Le solveur le traitait comme mobile alors
  qu'il n'est plus intégré : il encaissait la moitié de chaque impulsion sans jamais la rendre.
  Mesure : une caisse posée dessus gardait **0,2366 m/s** indéfiniment — exactement
  l'incrément de gravité d'un pas — donc ne s'endormait jamais. Un corps endormi a désormais
  masse et inertie **infinies**, comme un statique.
  ⚠️ **Le réveil ne peut pas se juger sur la vitesse relative.** Deux caisses vibrant chacune
  à 0,07 en sens contraire affichent 0,14 de relatif : chacune assez calme pour s'endormir,
  assez « vive » pour réveiller l'autre — oscillation sans fin. On juge le **voisin sur sa
  vitesse propre**, avec un seuil qui domine `g·dt` (0,238 m/s), sinon tout corps éveillé
  paraît mobile au moment où le solveur regarde.
  ⚠️ **L'amortissement n'agit qu'au contact.** Appliqué en vol, il fige les caisses lancées
  (mesuré : moitié de la rotation perdue en 0,5 s à 1,5/s). Et après la passe de vitesse,
  sinon il amortit la gravité.
- **R6 ✅** Rendu de la rotation. `ModelMatrix(body)` = `Scale · Rotation · Translation`, une
  **seule** fonction pour les deux passes (ombre et rendu final) — deux matrices calculées
  séparément finissent par diverger, et une ombre décollée de sa caisse ne se voit pas.
  📸 **Vérifié par capture** : caisses en vol à des orientations variées, éclairage par face
  cohérent. C'est la première preuve visuelle depuis R1.
  ✅ **Matrice des normales corrigée le 16/08** : le shader utilisait `mat3(uModel)`, exact
  seulement pour une échelle uniforme. `SceneRenderer.NormalMatrix()` calcule maintenant la
  transposée de l'inverse côté CPU.
  ⚠️ **Impossible à vérifier visuellement aujourd'hui, et pour une raison instructive** : les
  normales d'une **boîte** sont toutes des axes propres, et mettre `(1,0,0)` à l'échelle donne
  `(sx,0,0)` — qui se renormalise en `(1,0,0)`. Le défaut ne peut donc PAS se manifester sur
  des cubes, quelle que soit leur échelle. Il mordra à l'étape 1.5, avec de vrais modèles, et
  se traduira par un éclairage un peu faux — pas une image cassée. Corrigé d'avance pour ça.
  📏 Preuve de non-régression : capture **bit à bit identique** avant/après (SHA-256).
- **R7 ✅** Frottement de Coulomb contre le décor, par **contact spéculatif**. Les boîtes du
  décor sont gonflées de 2 mm, la marge est retranchée de la profondeur, et la passe de vitesse
  vise `-séparation/dt` : le corps s'arrête pile sur la surface, mais c'est le **solveur** qui
  le fait — donc l'impulsion normale est réelle, et le frottement a de quoi mordre.
  `MoveAndSlide` ne touche plus aux vitesses ; il ne garde que son rôle vital, empêcher une
  caisse lâchée de 60 m de traverser le sol en un pas.
  ⚠️ **La marge doit rester sous `seuil de sommeil × dt`** = 0,14/64 = **2,2 mm**. Au-delà, le
  contact spéculatif autorise une approche de `marge/dt` et la pile pompe à cette vitesse sans
  jamais pouvoir s'endormir. Mesuré à 1 cm : pics de 0,70 m/s ≈ 0,64 = marge/dt.
  📏 **Preuve** : `Le_Decor_Freine_Vraiment_La_Rotation` coupe l'amortissement de R5 et vérifie
  que la toupie s'arrête quand même. Sans R7 elle tournait à 6,000 rad/s indéfiniment.
  🗑️ **L'amortissement forfaitaire au sol a été supprimé** — redondant, et il ne dépendait pas
  de la charge. Caisse lancée à 8 m/s : **0,99 m avant, 3,17 m maintenant**. La physique dit
  3,9 m (`v²/2μg`). ⚠️ **Changement de ressenti, à valider à la main.**

### Pourquoi R7 avait échoué la première fois

Tenté pendant R5, il cassait le sommeil des piles et j'avais reculé. La cause n'était pas le
contact spéculatif : **deux bugs le sabotaient**, corrigés seulement après, en R5 même — le
corps endormi traité comme un *puits de vitesse*, et la règle de réveil. Rejoué sur le socle
réparé, il est passé du premier coup. **Leçon : une correction qui échoue sur une base cassée
ne prouve rien contre elle.**

## Le joueur et les caisses — corrigé le 16/08, trouvé en JOUANT

**Premier défaut remonté par un humain, et aucun des 264 tests ne le voyait.** On ne pouvait
pas monter sur une caisse : elle filait sous les pieds.

Deux causes qui se cumulaient :

1. `CharacterMotor` ne recevait que le **décor**. Les caisses lui étaient transparentes — il
   les traversait. `Program.RebuildWalkable()` lui fournit désormais décor + caisses à chaque
   pas (emprise alignée de l'OBB : pour une caisse droite c'est identique, pour une basculée
   ça évite de passer au travers par un coin).
2. `PushWith` chassait la caisse sur laquelle on se tenait. Elle ignore maintenant tout corps
   dont le dessus est sous les pieds — c'est un appui, pas un obstacle. Et sa boîte de test
   est élargie de 6 cm, sinon plus rien ne serait poussé du tout : les boîtes se *touchent*
   maintenant au lieu de se chevaucher, et `Aabb.Intersects` est strict.

**Deuxième défaut du même essai (16/08)** : coller une caisse **de face** ne la bougeait pas,
mais la longer **de côté** la décalait. Depuis que les caisses sont solides, un joueur qui
appuie de face est *bloqué* — sa vitesse réelle vaut zéro, et la poussée se calculait dessus.
De biais, il gardait sa composante tangentielle, d'où le dérapage.

`CharacterMotor.WishVelocity` expose désormais la vitesse **voulue**, avant collision, et
`PushWith` ne pousse que la composante dirigée **vers** le corps. Mesuré par mutation : avec
l'ancienne vitesse réelle, la caisse n'avance que de 4,8 cm au lieu de 30.

**La leçon**, à ne pas réapprendre : la suite de tests peut être verte et le jeu injouable.
Elle vérifie la physique entre corps ; personne ne vérifiait le **joueur contre les corps**.
Les deux défauts ont été trouvés en jouant, aucun test ne les voyait — et ils sont tous deux
la conséquence directe d'une correction précédente.

## Coût du solveur — mesuré le 16/08, enfin

Commande : `--exec "perf 1"` — une ligne par seconde avec le coût du pas physique.
**Ne jamais juger le solveur aux img/s** : à 3 000 images par seconde le rendu noie tout.

| Situation | Moyenne | Part du budget (15,6 ms) |
|---|---|---|
| 44 caisses, effondrement en cours | 3,5 ms | 22 % |
| 44 caisses, 1 s plus tard | 1,1 ms | 7 % |
| 44 caisses posées, 8 encore actives | 0,30 ms | 2 % |
| 44 caisses endormies | 0,17 ms | 1,1 % |
| 4 caisses endormies | **0,006 ms** | 0,04 % |

**Le solveur n'est pas un problème de performance à cette échelle.** Deux conclusions
contre-intuitives, toutes deux mesurées :

1. **Les pics à 15–19 ms ne viennent PAS du solveur.** Ils sont identiques à 4v/3p et à
   12v/10p, et surtout ils sont encore là **avec 4 caisses et zéro contact** (pic 14,4 ms).
   C'est le coût de démarrage — compilation à la volée. C'est lui, et non la physique, qui
   explique le « 1 pas sauté » au lancement de chaque session.
2. **Baisser le nombre de passes est une fausse économie.** À 4v/3p, les 44 caisses sont
   *toutes encore actives* après 3 s avec 181 contacts, contre 40 actives et 88 contacts à
   12v/10p. Moins de passes → la pile ne se pose jamais → plus de contacts à résoudre, plus
   longtemps. **Les réglages actuels sont les moins chers.**

**Garde-fou** : `Une_Pile_Reste_Stable_Et_Ne_Tremble_Pas` et
`Beaucoup_De_Caisses_Restent_Stables` sont les détecteurs d'instabilité. Ils doivent rester
verts à chaque étape.

---

## RÉSEAU — PHASE 3 COMPLÈTE (3.1 → 3.7), 17/08

**Les sept étapes sont faites.** Prédiction, réconciliation et lag compensation comprises —
les trois marquées « risque très élevé » dans la roadmap.

📏 **Mesuré entre deux vrais processus, UDP réel, 16 s de jeu :**
`PREDICTION ON · CORR 1 · ECART 0,0 CM · PIRE 2,2 CM` côté hôte immobile,
`CORR 1 · PIRE 23,4 CM` côté client qui courait (les 23 cm sont pris en butant dans un
pilier). **Une seule correction sur 1 024 pas**, et l'écart courant retombe à zéro.

Console ajoutée : `net_predict 0|1` — couper la prédiction en direct est le **seul** moyen de
sentir ce qu'elle apporte.

### 3.5 — prédiction

Le client simule **lui-même**, immédiatement, avec sa propre entrée. Sans ça, appuyer sur
« avancer » envoie une entrée, le serveur simule, renvoie une position : le joueur ne bouge
qu'au bout d'un **aller-retour complet**. À 60 ms de ping, chaque pas se voit 60 ms après
l'appui — le jeu passe de « nerveux » à « pâteux » sans qu'aucun chiffre du profileur ne bouge.

⚠️⚠️ **LA PRÉDICTION EXIGE LA MÊME PHYSIQUE DES DEUX CÔTÉS**, et c'est la règle n°1 du projet
qui la rend possible : le client et le serveur partagent **littéralement** le même
`CharacterMotor` et le même `CollisionWorld`. Deux implémentations divergeraient, et la
correction se déclencherait en permanence.

### 3.6 — réconciliation

Quand l'instantané arrive, il concerne un pas **déjà passé** côté client. On remet le joueur à
l'état que le serveur dicte pour ce pas-là, puis on **rejoue** toutes les entrées émises depuis.

⚠️⚠️ **`CharacterMotor.Save()` / `Restore()` — trois champs, et seulement trois** : position,
vitesse, hauteur. `Grounded` et `Stance` sont **recalculés** à chaque pas ; les restaurer
laisserait croire qu'ils portent de l'information.

⚠️⚠️ **LA CORRECTION EST ABSORBÉE PAR L'AFFICHAGE, PAS PAR LA SIMULATION.** La simulation
repart de la vérité tout de suite — sinon on prédirait à partir d'un mensonge et l'erreur
s'accumulerait. Mais afficher ce saut tel quel tirerait le joueur en arrière à chaque paquet :
l'écart devient un décalage **visuel** résorbé exponentiellement en quelques dixièmes.

⚠️ **Le seuil de correction (2 cm) est nécessaire, pas confortable.** Le rejeu ne redonne
jamais le bit près — les flottants ne sont pas associatifs. Corriger sur un micromètre ferait
sauter le joueur **64 fois par seconde**.

🐛 **Un défaut livré puis corrigé, dont le symptôme ne ressemblait pas à la cause.** Quand le
serveur n'a encore rien accusé (`acked = 0`), je vidais tout l'historique. Le test de
convergence **passait** — le joueur revenait bien sur la vérité — mais le compteur de
corrections restait à **zéro** et le client se retrouvait **en retard** sur le serveur au lieu
d'être en avance. Autrement dit la prédiction ne servait plus à rien, en silence. Trois cas
distincts se cachaient là : rien d'accusé encore / pas déjà élagué / vraiment perdu.

### 3.7 — compensation de latence

Le serveur garde une seconde d'historique par joueur et **rembobine** les cibles à l'instant que
le tireur voyait, avant le lancer de rayon.

⚠️⚠️ **LE PRIX EST RÉEL : « se faire tirer dessus derrière un mur ».** La victime s'est mise à
couvert, mais le serveur juge sur une position d'il y a ~90 ms. C'est un **arbitrage** : sans
compensation on punit le tireur, avec on punit celui qui se cache. Tous les FPS compétitifs
choisissent le tireur.

⚠️ **On interpole entre deux échantillons**, on ne prend pas le plus proche : le tireur voyait
une position interpolée, et une position échantillonnée introduirait jusqu'à 7 cm d'erreur à la
course — l'ordre de grandeur d'une tête.

⚠️ **Le décor n'est PAS rembobiné** (il ne bouge pas), et il borne la balle : sans ça on
toucherait à travers un mur.

⚠️ **Un rembobinage trop ancien est REFUSÉ**, pas extrapolé — extrapoler validerait un tir sur
une cible imaginaire.

### 8 mutations tracées sur 3.5/3.6/3.7

| Mutation | Tests qui tombent |
|---|---|
| la prédiction ne simule pas | 3 |
| on ne corrige jamais | 2 |
| on corrige toujours (bruit flottant compris) | 1 |
| la **vitesse** n'est pas restaurée au rejeu | 2 |
| la position seule est restaurée (ni vitesse ni hauteur) | 1 |
| la prédiction rejouée n'est pas réécrite dans l'historique | 1 |
| l'historique n'est jamais élagué | 3 |
| pas de rembobinage, on juge sur le présent | 1 |

⚠️⚠️ **DEUX LEÇONS DE MÉTHODE, PAYÉES DANS CETTE SESSION.**

1. **Une mutation qui ne tue rien peut viser la mauvaise ligne.** `Restore(...)` existe à
   **deux** endroits avec des indentations différentes ; ma mutation frappait la branche
   « vraiment perdu », jamais exécutée dans le scénario. J'ai conclu quatre fois de suite que
   le test ne discriminait pas — alors qu'il discriminait parfaitement. **Vérifier que la
   mutation touche le code exécuté avant de conclure quoi que ce soit sur le test.**
2. **Comparer la position du client à celle du serveur ne discrimine rien** : le serveur publie
   un instantané à **chaque pas**, donc le client est ré-ancré toutes les 15 ms et une vitesse
   fausse n'a pas le temps de se voir. Ce qui discrimine est une propriété **physique** : en
   chute libre la vitesse verticale ne peut varier que de `g·dt = 0,238 m/s` par pas. Une
   correction qui la remettrait à zéro la ferait sauter de plusieurs m/s en un pas.

### Ce que je n'ai toujours PAS vérifié

- **Rien n'a tourné entre deux vraies MACHINES.** Loopback et réseau simulé uniquement.
- ✅ ~~**Le client ne charge pas la carte du serveur.**~~ **Fait le 08/10** — voir « Le client joue
  sur la carte du serveur » plus bas.
- ✅ ~~**`Fire`/`HitReport` ne sont pas branchés dans le jeu.**~~ **Fait le 08/10** — voir « Le
  serveur arbitre le tir » plus bas.
- **Pas de delta-compression, pas de jeton de session.**
- **Le ressenti n'est pas jugé.** Il faut jouer à deux, avec du vrai ping.

---

### Le serveur arbitre le tir (08/10) — le dernier fil de la phase 3

**Avant** : `LagCompensation` était écrite et testée, mais **personne ne l'appelait** — le tir du
bac à sable restait local. **Maintenant** : chaque tir en ligne (souris, ou `net_fire`) part au
serveur avec l'**instant que le joueur voyait** (`NetClient.ViewTick`, le pas serveur fractionnaire
de l'interpolation). Le serveur tient un historique par joueur, rembobine les autres, tranche, et
renvoie un `HitReport` au tireur (protocole **version 3**). Les effets locaux (étincelles, son)
restent immédiats : seul le VERDICT vient du serveur.

📏 **Deux vrais processus** (`formes`, cible arrêtée à 6,7 m dans l'axe) : *« tir 1 : TOUCHE
joueur 2 a 6,3 m »* — 6,7 m moins la demi-largeur de 0,4 m, **au centimètre** — puis, après un
quart de tour, *« tir 2 : rate »*. Côté serveur : `tirs 2  touches 1  rembobinage decisif 0  refuses
0`. Capture du tireur : le verdict au HUD, la cible sous le réticule.

📏 **En réseau simulé, 60 ms de latence, cible qui court en travers** : le tireur vise LÀ OÙ IL LA
VOIT → **touché, et `RewindMattered`** — sans rembobinage, ce tir ratait. **Contre-épreuve** : viser
là où la cible EST vraiment sur le serveur → **raté**. Le serveur juge ce que le tireur voyait.

⚠️⚠️ **L'INSTANT VU, PAS LE DERNIER PAS REÇU.** Le joueur voit les autres AVEC le retard
d'interpolation. Vérifié par mutation : en envoyant le dernier pas reçu, **seule la contre-épreuve
tombe** — le test principal passe encore, l'écart (31 ms, 15 cm) restant sous la demi-largeur d'un
corps. C'est ce qui justifie d'avoir les deux tests.

⚠️ **Tout ce qui vient du réseau est hostile** : un tir annoncé à plus de **1,5 m** de l'œil du
tireur côté serveur est **REFUSÉ**, et dit comme tel (`Rejected`) — un client honnête qui tomberait
là a un problème de prédiction qu'il faut voir, pas un tir raté. 1,5 m couvre l'écart honnête
(la prédiction devance le serveur d'un aller-retour). L'origine envoyée est donc l'œil PRÉDIT.

⚠️ **Jamais le futur** : un instant vu postérieur au pas courant du serveur est ramené au présent.

📏 **3 mutations attrapées** : jugement au présent (2 tests), dernier pas reçu au lieu de l'instant
vu (la contre-épreuve), origine non contrôlée. 3 tests réseau. **A/B contre 0f6847d : les huit
scénarios identiques** ; console : `net_fire` en plus.

⚠️ **Ce qui reste** : un tir n'a **aucun effet** — pas de points de vie, pas de mort. C'est du
contenu de jeu, hors périmètre (phase 5 supprimée). Le verdict est affiché, compté, et c'est tout.

### `delay` — tirer APRÈS la connexion, en headless

Tous les `--exec` partent à t = 0 : impossible de tirer après une connexion, d'ouvrir une porte à
mi-course, de basculer un réglage au milieu d'une mesure. **`delay <secondes> <commande>`**
(`ConsoleSystem`, Core — donc disponible pour tout jeu) exécute la commande plus tard, en temps de
**simulation** (une fenêtre masquée tourne au ralenti). Sa sortie est recopiée sur la sortie
standard, préfixée `delai`. Une commande programmée pendant l'exécution attend le pas suivant —
même règle que la logique. `2,5` est accepté comme `2.5`. 8 tests.

```powershell
dotnet run --project src\Exarion.Game -c Release -- --exec play --exec "connect 127.0.0.1" --exec "delay 4 net_fire" --exec "delay 5 net"
```

### Le client joue sur la carte du serveur (08/10)

**Avant** : le serveur annonçait le NOM de sa carte, le client l'affichait — et prédisait dans
SA carte. Un client sur un autre décor était corrigé en permanence sans que rien ne dise pourquoi.

**Maintenant** : le Welcome porte une **empreinte du décor solide** (`MapFile.Fingerprint`,
protocole **version 2**), et `MapSync.Decide` (Core, fonction pure) tranche : **garder** la carte
courante si son décor concorde, **charger** celle du serveur si on l'a, sinon **refuser** — se
déconnecter en disant pourquoi. `net` affiche l'empreinte et la décision.

📏 **Vérifié sur deux vrais processus, UDP en boucle locale** :
- serveur sur `arene`, client sur `formes` → *« carte du serveur chargee : arene »*, capture du
  client sur le décor d'arene ;
- serveur sur `arene` **avec un mur déplacé en mémoire**, même nom → *« « arene » existe ici mais
  son decor DIFFERE (empreinte 303785d1 contre e8d80e3c) »*, client déconnecté ;
- les deux sur le bac à sable intégré → *« identique à la carte courante »*.

⚠️⚠️ **L'EMPREINTE PORTE SUR LE DÉCOR SOLIDE, PAS SUR LE FICHIER.** Ma première version hachait le
texte enregistré entier. Elle aurait refusé deux joueurs sur le bac à sable INTÉGRÉ dès que leurs
réglages d'éclairage différaient : ses intensités viennent d'`exarion.cfg`, et les lumières font
partie du texte. Or le serveur ne simule QUE les boîtes. L'empreinte prend donc le centre et la
taille de chaque boîte, **en bits flottants** : ni culture, ni saut de ligne du système (`AppendLine`
écrit « \r\n » sous Windows, « \n » sous macOS — un client Mac aurait été refusé par un serveur
Windows) ne peuvent s'en mêler.

⚠️ **Le serveur annonce le NOM DE FICHIER, pas le nom écrit dedans.** Constaté en le testant :
`arene.map` s'appelle « bac a sable » à l'intérieur (enregistrée depuis le bac à sable). Le client
cherchait alors `bac_a_sable.map` — un autre fichier, ou aucun.

⚠️ **Un nom, plusieurs candidats** : le bac à sable intégré s'appelle « bac a sable », et un
`bac_a_sable.map` peut traîner dans le dossier. `MapSync` prend celui dont le décor concorde.

⚠️ **La vérification passe ENTRE la réception et la première prédiction.** Placée après l'envoi
de l'entrée, le premier pas après le Welcome prédisait encore dans l'ancien décor : mesuré
`PIRE 39,1 CM` au démarrage, **26,0** une fois déplacée. Pour comparaison, **sans aucun changement
de carte : `PIRE 22,3 CM`** — les corrections du démarrage sont la base existante, pas la synchro
(une mesure de chaque : à confirmer).

⚠️ **Le monde de prédiction suit la carte.** Il était capturé à la connexion : un `map_load`
ultérieur laissait le client prédire dans l'ancien `CollisionWorld`. Tout changement de carte
remet la vérification à faire, et le client se recale — ou se déconnecte.

📏 **4 mutations attrapées** : empreinte sans la taille, « le nom suffit », Welcome sans empreinte,
nom interne annoncé. 8 tests `MapSyncTests` + 2 `NetTests`. **A/B contre 3047d9f : tout identique**,
y compris le scénario `reseau` (l'hôte se connecte à lui-même : « garder »).

⚠️ **Ce qui reste** : les **entités** (portes, logique) ne sont pas synchronisées — chaque client
fait tourner les siennes ; une porte ouverte chez l'un est fermée chez l'autre. Et l'hôte qui
change de carte (`map_load`) ne fait pas changer celle du SERVEUR : son propre client se
déconnecte, faute de concorder.

## Réseau — l'étape précédente (3.1 → 3.4)

**Deux processus Exarion, un serveur autoritaire, de l'UDP réel : les joueurs se voient.**

Console : `host [port]` · `connect <hôte> [port]` · `disconnect` · `net` · `net_name` ·
`walk 0|1` (marche automatique, outil de capture).

⚠️⚠️ **CE QUI EST FAIT, ET CE QUI NE L'EST PAS.** À dire clairement, parce que « le réseau
marche » serait faux :

| # | Étape | État |
|---|---|---|
| 3.1 | Transport UDP + sérialisation binaire | ✅ |
| 3.2 | Serveur autoritaire, connexion, entrées transmises | ✅ |
| 3.3 | Instantanés (réplication d'état) | ✅ **sans delta-compression** |
| 3.4 | Interpolation des entités distantes | ✅ |
| 3.5 | Prédiction client | ✅ |
| 3.6 | Réconciliation | ✅ |
| 3.7 | Lag compensation | ✅ |

**Conséquence pratique de l'absence de 3.5 : TON PROPRE mouvement a la latence complète.**
Sur 127.0.0.1 c'est invisible (16 ms mesurés) ; à 60 ms de ping ce sera injouable. C'est
normal et attendu — la prédiction est précisément ce qui règle ça, et elle est notée « risque
très élevé » dans la roadmap. **Ne pas conclure que le netcode est cassé en le constatant.**

### Ce qui est mesuré

📏 **Deux vrais processus, UDP réel** : `SERVEUR 2 CLIENT(S)`, `RTT 16 MS`, joueur distant
répliqué de `Z 20,00` à `Z 13,00` après 7 m de marche, visible à l'écran avec son ombre. Les
deux HUD s'accordent au centième.

📏 **Taille d'un instantané** : 11 octets d'en-tête + 18 par joueur. À 10 joueurs et 64 Hz,
**12,2 ko/s par client** — largement sous la MTU, donc jamais fragmenté.

📏 **26 tests réseau**, tous sur un réseau simulé déterministe (latence, gigue, perte,
duplication, désordre). **8 mutations tracées** — voir plus bas.

### Les décisions, et pourquoi

⚠️⚠️ **UDP, PAS TCP.** TCP retient tous les paquets suivants jusqu'à retransmettre celui qui
manque (blocage de tête de file). Dans un jeu, ça veut dire que la position d'il y a 200 ms
bloque celle d'il y a 10 ms — alors que la première n'a plus **aucun** intérêt. On choisit
donc ce qui mérite d'être fiable : un instantané perdu est **oublié** (le suivant arrive dans
15 ms) ; une entrée perdue est couverte par **redondance** (chaque paquet porte les 8
dernières) ; seule la **poignée de main** est réessayée.

⚠️⚠️ **`INetTransport` EST CE QUI REND LE NETCODE TESTABLE**, et c'est la décision structurante.
Un défaut de netcode se manifeste comme « ça tremble parfois », trois semaines plus tard, chez
quelqu'un qui a 200 ms de latence. `LoopbackTransport` rejoue ça **à la graine près** en
quelques millisecondes. Aucune socket dans `Core` — la seule vit dans
`Engine/Net/UdpTransport`, exactement comme miniaudio derrière `AudioDevice`.

⚠️ **`System.Net.Sockets` fait partie du runtime** : ce n'est pas une dépendance au sens de
`DEPENDENCIES.md`, pas plus que `System.Text.Json` pour glTF. **Aucun paquet ajouté.**

⚠️ **L'hôte se connecte à son propre serveur par 127.0.0.1**, deux sockets, deux ports. Il
pourrait lire l'état du serveur directement — ce serait un piège : l'hôte aurait un
comportement différent des autres joueurs, donc des défauts qui n'apparaîtraient que chez eux.

### Les pièges traités, chacun figé par un test

🐛 **Une conception faible que j'ai livrée puis corrigée** : j'interpolais d'abord sur
l'**heure d'arrivée** des paquets. Ça marche sur un réseau parfait et se dégrade très mal —
avec 15 ms de gigue, la **vitesse apparente** du joueur distant varie d'autant. Mesuré : le
plus grand pas faisait **6,3 fois** le pas moyen (111 mm contre 18). L'horloge de lecture
avance maintenant avec le temps local et se recale doucement sur le **pas serveur**, qui est
régulier par construction. Mutation : remettre l'ancienne version fait tomber le test.

⚠️ **Tout ce qui vient du réseau est hostile.** La vitesse du moteur est proportionnelle à la
longueur du vecteur de déplacement : un client modifié qui envoie `(1000, 1000)` courrait mille
fois plus vite. `TimedInput.Sanitize` le ramène sur le cercle unité en gardant la **direction**.
Et le protocole n'a **aucun message « je suis ici »** — un test fige cette propriété.

⚠️ **Un paquet tronqué ou un NaN ne doit pas tuer le serveur.** `NetReader` lève un drapeau
`Failed` et rend zéro ; un NaN est traité comme une lecture ratée (il se propagerait dans les
positions puis la physique, et figerait tout sans qu'aucune ligne ne le signale).

⚠️ **Un instantané en retard est JETÉ.** UDP livre dans le désordre : sans ce test, un paquet
tardif fait reculer tout le monde d'une image — c'est **le** tremblement qu'on met des semaines
à attribuer au réseau plutôt qu'au rendu.

⚠️ **Une entrée dupliquée n'est appliquée qu'une fois.** La redondance fait arriver la même
entrée jusqu'à 8 fois. L'appliquer deux fois ferait avancer deux fois plus vite — et l'écart
grandirait avec la perte, donc **les mauvaises connexions courraient plus vite que les bonnes**.

⚠️ **Un `Hello` répété ne crée pas de joueur fantôme.** Le client réessaie tant qu'il n'a pas
le `Welcome`, et c'est justement le `Welcome` qui a pu se perdre. Testé à **60 % de perte**.

⚠️ **L'interpolation apparie par IDENTIFIANT, pas par indice.** Par indice, un joueur qui se
déconnecte décale la liste et on interpolerait la position d'un joueur vers celle d'un autre —
un glissement instantané à travers la carte.

⚠️ **Les paquets en vol ne partagent pas leur tampon.** Sans copie, tous arrivent avec le
contenu du dernier envoyé. Défaut retors : tout marche tant qu'un seul paquet vole à la fois.
Mutation : **9 tests tombent**.

⚠️ **`SIO_UDP_CONNRESET` sous Windows.** Un ICMP « port unreachable » reçu après un envoi fait
échouer la **lecture** suivante sur une socket UDP sans connexion — le serveur casse dès qu'un
client se ferme brutalement. Désactivé à l'ouverture.

⚠️ **Socket NON bloquante.** Une lecture bloquante ferait tourner le jeu à la cadence du réseau.

⚠️ **Pas de DNS dans la boucle de jeu** : `Dns.GetHostAddresses` bloque, parfois plusieurs
secondes. La résolution se fait dans la commande `connect`, pas dans `OnSimulate`.

📏 **8 mutations tracées** : instantané en retard non jeté → 1 test ; entrée dupliquée → 1 ;
entrée non bornée → 1 ; tampon partagé → **9** ; appariement par indice → 1 ; `Hello` répété →
1 ; interpolation sur l'heure d'arrivée → 1. ⚠️ **La 8ᵉ ne discrimine pas** : retirer le
repliement de l'angle dans `[0, 2π[` ne fait tomber aucun test, parce que sur cette machine le
cast flottant→ushort négatif enroule **par hasard** sur la bonne valeur. On la garde quand même
— le C# déclare ce résultat *non spécifié*. C'est écrit dans le code.

### Ce que je n'ai pas pu vérifier

- **Aucun test sur deux MACHINES.** Tout est en loopback ou en réseau simulé. La latence réelle,
  la MTU d'un vrai routeur, le NAT : jamais exercés.
- **Pas de delta-compression** (3.3 partielle) : chaque instantané renvoie l'état complet.
  Suffisant à 10 joueurs, pas au-delà.
- **Pas de jeton de session** : le client filtre sur l'adresse du serveur, mais quelqu'un qui
  usurpe l'adresse source peut injecter un instantané. Il faudrait un jeton dans le `Welcome`.
- **Le ressenti n'est pas jugé**, et il ne peut pas l'être avant 3.5.

---

## Chantier terminé — le moteur devient utilisable de l'extérieur (17/08)

Le moteur marchait. Il n'était pas **utilisable** : l'enchaînement des passes de rendu vivait
dans les 242 lignes de `OnRender` du bac à sable. Écrire un second jeu voulait dire recopier
cet enchaînement — donc maintenir deux séquences qui divergeraient, exactement ce que cette
doc dénonce partout ailleurs.

⚠️⚠️ **LE PIÈGE ÉVITÉ, et il faut le nommer** : créer une classe `ExarionGame` qui *refait* le
pipeline à côté de celui de `Program.cs`. Ç'aurait été deux séquences de rendu concurrentes.
**On déplace, on ne duplique pas.** Le bac à sable a été migré SUR le pipeline extrait, pas
laissé de côté.

### Ce qui a bougé

| Pièce | Où | Pourquoi |
|---|---|---|
| `RenderPipeline` + `IWorldRenderer` | `Engine/Graphics` | possède les 9 objets de rendu et l'ordre des passes. Le jeu fournit 3 méthodes |
| `Screenshot` | `Engine/Graphics` | l'exemple en avait besoin et ne pouvait pas y accéder |
| `MapLighting.LoadLights` | `Engine/Graphics` | la traduction `MapLight → Light`, recopiée à l'identique dans les deux jeux |
| `FixedTimestep.RenderTime` | `Core` | l'horloge d'animation : une règle du pas fixe, pas une astuce du bac à sable |

Les anciens champs de rendu de `Program.cs` sont devenus des accesseurs en lecture
(`_scene => _render?.Scene`) : les ~3 000 lignes qui les utilisaient compilent sans y toucher.
`Program.cs` : 3 115 → 3 041 lignes.

### La preuve

📏 **A/B contre HEAD, capture du bac à sable** : **0 pixel différent** sur 921 600 au seuil 6,
et seulement **2 pixels** non nuls (écart ≤ 5/765). Toute la migration est neutre à l'image.

📸 **`samples/Exarion.Sample`, 187 lignes, tourne.** Ciel, ombres, lumières ponctuelles,
occlusion de contact, textures, interface — depuis un projet qui **ne référence jamais**
`Exarion.Game`.

⚠️ **C'est le second client qui a trouvé les manques, pas la relecture du moteur.** Un moteur
avec un seul jeu peut être arbitrairement couplé à ce jeu : personne ne s'en aperçoit parce que
rien ne l'exerce autrement. Les quatre extractions ci-dessus ont toutes été découvertes en
écrivant l'exemple. **Sa longueur est une mesure** : chaque ligne y est une ligne que tous les
jeux devraient réécrire.

### Le piège de RenderTime, et pourquoi le bac à sable ne l'utilise PAS

`RenderTime` = `SimulatedTime + alpha × pas`. Trois propriétés : lisse, déterministe,
et **idempotente** — on peut la relire trois fois dans la même image sans rien faire avancer,
ce qui est vital puisqu'une image échantillonne la pose trois fois.

📏 **3 mutations tracées** : alpha oublié → 1 test tombe ; signe inversé → 2 ; alpha doublé → 2.
Le test d'idempotence, lui, **ne tombe sous aucune** — une expression pure est idempotente par
construction. C'est un garde-fou, pas un discriminant, et c'est écrit dans le test.

⚠️ **Le bac à sable garde `_simTime`, et c'est mesuré.** Basculer son horloge d'animation sur
`RenderTime` **double** l'écart entre deux captures identiques (231 → 511 pixels) :
`SimulatedTime` **saute en avant** quand la machine décroche, et le nombre de pas sautés varie
d'un lancement à l'autre (24 puis 15 sur le même essai). L'horloge d'un jeu peut se permettre
ça ; celle qui sert à comparer deux captures, non. `_simTime` compte les pas **réellement
exécutés** : il ne dépend pas de la charge.

### `README.md` existe enfin

Quelqu'un qui clonait le dépôt tombait sur 78 Ko de ce carnet de bord. Le README dit en une
page ce qu'est le projet, comment le lancer, comment écrire son jeu, **et ce qui manque**
(réseau, recherche de chemin, entrées d'entités, script, Linux, ressenti). Ses trois commandes
ont été exécutées mot pour mot avant d'être écrites.

⚠️ Erreur attrapée en le rédigeant : j'y avais écrit « 12 lumières » — c'est le contenu d'une
scène de mesure, pas la limite. `SceneRenderer.MaxLights` vaut **32**. Un README qui invente
un chiffre est pire qu'un README absent.

---

## Outils de vérification — À UTILISER SYSTÉMATIQUEMENT

```powershell
dotnet test                          # 703 tests, ~5 s
dotnet build --no-incremental        # doit rester à 0 erreur, 0 avertissement dans src/
dotnet run --project samples\Exarion.Sample -c Release   # le second client doit tourner
```

⚠️ **Le jeu d'exemple fait partie des outils de vérification.** S'il cesse de compiler, c'est
que le moteur vient de redevenir dépendant de son bac à sable — et il est le seul à le dire.

📏 **Non-régression complète : `tools\ab-captures.ps1`** (depuis le 07/10). Il compare la copie
de travail à une copie FIGÉE du dépôt :

```powershell
git worktree add --detach ..\reference <commit>          # une fois
pwsh tools\ab-captures.ps1 -Reference ..\reference        # ~4 min
pwsh tools\ab-captures.ps1 -Reference ..\reference -Bruit # la référence contre ELLE-MÊME
```

Huit captures (menu, partie, effets, éditeur, PNJ, portes, réseau, jeu d'exemple), l'inventaire
complet de la console et une session scriptée de 70 commandes. **Mesurer le bruit d'abord**
(`-Bruit`) : un écart du même ordre ne prouve rien. Pour un changement VOULU, l'outil doit montrer
exactement ce qui devait changer, et rien d'autre — puis on avance la référence (`git -C
..\reference checkout --detach HEAD`). `-Seulement partie,editeur` pour cibler.

📏 **Le son se mesure aussi** : `--audio-dump f.wav --audio-seconds N`, puis
`tools\wav-niveau.ps1 f.wav` donne crête et RMS par canal. C'est ce qui a prouvé que
l'occlusion ne voyait plus aucun mur.

📏 **Comparaison de deux captures** : `tools\diff-png.ps1 a.png b.png [seuil]`. Il vit dans le
dépôt parce qu'il a déjà été réécrit deux fois — la première version dormait dans un dossier
temporaire de session, donc perdue.

⚠️ **Seuil 6 par défaut, et ce n'est pas arbitraire** : deux lancements du même binaire ne
donnent pas deux images bit à bit égales. Compter les écarts non nuls fait conclure « 96,7 %
des pixels diffèrent » sur un rendu **inchangé** — c'est arrivé. Le script affiche les deux
comptes, et dit lequel ne veut rien dire.

⚠️ **Sur Mac, les chemins sont en `/` et il n'y a pas de PowerShell** — les commandes de ce
fichier se transposent telles quelles dans zsh en remplaçant `\` par `/` et les backticks de
continuation par `\`.

**Capture d'écran + pilotage headless** — c'est ce qui permet de vérifier le rendu
sans yeux humains :

```powershell
dotnet run --project src\Exarion.Game -c Release -- `
  --seconds 3 --screenshot rendu.png `
  --exec "tp 0 1 25" --exec "look 20 35" --exec "light_lamps 200"
```

Options : `--seconds N` `--screenshot fichier` **`--shot-at N`** `--exec "commande"` (répétable)
`--novsync` `--shadows 0|1` `--tickrate N` `--sensitivity N` **`--map nom`**

⚠️ **Pour agir APRÈS t = 0** (tirer après une connexion, ouvrir une porte à mi-course) :
`--exec "delay 4 net_fire"`. La sortie des commandes différées sort préfixée `delai`.

Variables utiles en console : `perf` (coût du pas physique), `phys_iterations`,
`phys_velocity_iterations`, `phys_sleep`, `crate_size`, `light_lamps`.

⚠️ **`--shot-at` n'existe que depuis R6, et il change tout pour la physique.** La capture était
câblée à 1 s : une pile lâchée est encore en vol, une explosion n'a pas fini de retomber. On
photographiait systématiquement le transitoire au lieu du résultat.

⚠️⚠️ **`--shot-at` compte en secondes de SIMULATION, `--seconds` en temps RÉEL.** Tant que ça
tourne à 64 pas/s les deux coïncident et rien ne se voit. Mais **une fenêtre masquée est
bridée par macOS** — mesuré à 4 pas/s, soit 16× plus lent : `--shot-at 3` demande alors 48 s
réelles, `--seconds 25` ferme avant, et **la capture n'est jamais écrite**. Le programme ne
signale rien, il rend simplement la main sans fichier. Constaté le 16/08 sur trois runs
d'affilée, et d'abord attribué à tort au rechargement de carte. **Devant une capture
manquante, regarder les pas/s avant de soupçonner le code** — et viser tôt en temps de
simulation (`--shot-at 0.6 --seconds 90`) plutôt que l'inverse.

Cette boucle a déjà trouvé **deux défauts invisibles autrement** : éclairage 10× trop
faible, et ombres invérifiables sous un plafond fermé.

**En cas de plantage** : lire `src\Exarion.Game\bin\Release\net9.0\plantage.txt`.

## Pièges d'outillage du 08/10 — chacun a faussé une mesure au moins une fois

| Piège | Ce qui s'est passé | La parade |
|---|---|---|
| `Start-Process -ArgumentList` | recolle les arguments **sans guillemets** : `host 27120` devint deux arguments, le serveur s'ouvrit sur le port par défaut, et le client ne trouva personne | citer chaque argument soi-même (`'"' + $_ + '"'`) |
| `… \| Select-Object -First N` | arrête la LECTURE, pas le processus : deux jeux ont tourné en parallèle, et la comparaison est partie avant la seconde capture | `\| Out-Null`, puis lire le journal |
| Remplacement multiligne | certains fichiers du dépôt sont en **CRLF** (`EntitySchema.cs`), d'autres en LF : un motif tapé avec des sauts de ligne ne trouvait rien | vérifier `CRLF` / `LF` avant ; `mutation.ps1` refuse un motif trouvé 0 fois — c'est ce qui l'a révélé |
| `ab-captures.ps1` code 0 | rendait « rien » sur une image différente (corrigé, voir la refonte) | lire les chiffres, pas seulement le code |
| Durées des tests | incluent la compilation à la volée : 5 à 9 fois trop pessimistes | 200 itérations de chauffe avant de chronométrer |
| `effets` en A/B | 0 à 7 pixels selon le lancement, au-dessus du bruit mesuré une fois sur deux | refaire l'A/B avant de conclure ; cause non isolée |

## Pièges qui ont déjà coûté cher

| Piège | Rappel |
|---|---|
| `glUniformMatrix4fv` | `transpose = **false**` avec System.Numerics (vecteur-ligne → vecteur-colonne). Un `true` déformait toute l'image |
| `Matrix4x4.CreatePerspective/Orthographic` | profondeur `[0,1]` (DirectX). OpenGL veut `[-1,1]`. **Matrices construites à la main** |
| Accumuler des deltas de temps | dérive. Le pas fixe raisonne en **temps absolu, nanosecondes entières** |
| Séparer deux corps dynamiques | peut les enfoncer dans le décor → **ressortie du statique obligatoire** après chaque passe |
| Frottement au repos | le contact posé n'a pas d'impulsion d'impact : estimer la normale par le **poids** |
| Réveiller sur contact | ne réveiller que sur **vrai impact**, sinon rien ne s'endort jamais |
| Exception dans un handler de ConVar | **tue le processus**. Tout est sous `try` désormais |
| Mesures de framerate | comparables **uniquement** dos à dos dans la même session (facteur 4 selon la visibilité de la fenêtre) |
| `FramebufferSize` ≠ `Window.Size` | en DPI élevé. Toujours le framebuffer |

## Ce que je ne peux pas juger

Le **feeling** — vitesse, recul, saut, inertie. Ça ne se calcule pas. La console existe
pour que ce réglage se fasse en direct : `speed_run`, `jump_height`, `friction`,
`sensitivity`, `fov`. **Ne jamais prétendre qu'un réglage est bon sans retour humain.**
