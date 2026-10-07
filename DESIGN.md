# Exarion 1 — FPS tactique, moteur maison

> **Objectif technique** : moteur 3D écrit par nous, en C# / .NET.
> **Contrainte** : un seul développeur.

> ## ⚠️ CE DOCUMENT DÉCRIT LA CIBLE, PLUS LE PROGRAMME (16/08/2026)
>
> **Le livrable est le MOTEUR, pas le jeu.** La phase 5 — round, économie, bombe, map jouable,
> playtest 5v5 — a été supprimée du plan. Voir `ROADMAP.md`.
>
> **Ce document reste utile, et il ne faut pas le jeter** : il dit *pour quoi* le moteur est
> taillé, et c'est ce qui justifie la moitié de ses choix techniques. Le pas fixe à 64 Hz, le
> tir hitscan, le générateur déterministe par graine, les bruits de pas directionnels
> « obligatoires » — tout ça vient des sections ci-dessous. Sans elles, ces décisions
> paraîtraient arbitraires.
>
> Lire les sections 1 à 3 comme un **cahier des charges de référence**, pas comme une liste de
> tâches. L'objectif produit d'origine (F2P monétisé par cosmétiques) ne pilote plus le
> programme, mais il reste la raison pour laquelle **le moteur doit être commercialisable** —
> donc la raison de la règle n°2 sur les licences, qui elle ne bouge pas.

---

## 0. Comment on en est arrivé là

Trois moteurs ont été essayés avant de trancher. L'historique compte, parce que chaque
abandon a été motivé par un fait vérifié, pas par une préférence.

| Moteur | Pourquoi écarté |
|---|---|
| **s&box** (Source 2 + C#) | ~140 joueurs simultanés sur toute la plateforme, mesurés. Netcode non serveur-autoritaire. Export standalone en preview, soumis à approbation Valve. |
| **Source SDK 2013** | Excellent netcode gratuit (`player_lagcompensation.cpp`), mais licence **SOURCE 1 SDK non commerciale** : distribution gratuite obligatoire. Le modèle F2P + skins est explicitement interdit. |
| **Unity 6 + FishNet** | Techniquement viable et légal. Écarté par choix : volonté d'écrire notre propre moteur. |

**Ce qui a survécu à ces trois changements** : ce document, et 963 lignes de logique de jeu
en C# pur — parce qu'elles n'ont jamais dépendu d'un moteur. C'est la leçon centrale du
projet, et la règle d'architecture qui suit en découle.

### Chiffres à garder en tête

- **GoldSrc** (moteur de Half-Life, nov. 1998) : ~30 personnes chez Valve, ~2 ans — et
  **construit sur le moteur de Quake sous licence**, pas de zéro.
- **Counter-Strike** (1999) : **2 personnes**, parce qu'elles n'ont écrit que le gameplay.

Écrire un moteur est un projet en soi. Estimation réaliste pour ce projet, seul, à
15 h/semaine, avec assistance IA : **~1 250 h, soit environ 20 mois** — pour un moteur
*vide*, sans gameplay dedans.

---

## 1. Le core loop à valider

```
Phase d'achat (15s) → Round (1m45 max) → Résultat → Phase d'achat → ...
Premier camp à 7 rounds gagnés = fin de match
```

Si ce loop n'est pas fun en 5v5 avec 2 armes sur 1 map, **aucun ajout ne le sauvera**.

## 2. Périmètre du prototype

### Inclus
| Système | Détail minimal |
|---|---|
| Déplacement | Marche, accroupi, saut. **Précision dégradée en mouvement** |
| Tir | Hitscan pur, pas de balistique |
| Recul | Pattern **fixe et mémorisable** + reset après ~0.4 s sans tir |
| Hitbox | 4 zones : tête ×4, torse ×1, ventre ×1.25, jambes ×0.75 |
| Armes | 7 (couteau, 2 pistolets, SMG, pompe, fusil, sniper) |
| Économie | Kill +300, victoire +3250, défaite cumulative 1400→3400 |
| Bombe | Pose 3s, désamorçage 7s, timer 40s |
| Camps | 5 attaquants vs 5 défenseurs, switch à la mi-temps |
| Map | **1 seule**, 2 sites, 3 voies |
| Audio | Bruits de pas directionnels **obligatoires** |

### Exclu du prototype
Compétences/agents, matchmaking, classement, anticheat, skins, killcam, plusieurs maps.

## 3. Ce qui va réellement poser problème

Par difficulté réelle, pas apparente :

1. **Le netcode.** Prédiction client + réconciliation + lag compensation (rembobinage des
   hitbox côté serveur). C'est *le* sujet dur, et il ne disparaît pas parce qu'on change de moteur.
2. **Le renderer.** Nouveau problème introduit par le choix du moteur maison.
3. **Le level design.** Une map équilibrée, c'est des mois de playtest.
4. **Le feeling du tir.** Invisible dans une spec, décisif à jouer.

---

## 4. Règle d'architecture — non négociable

**`Exarion.Core` ne référence AUCUN moteur, aucune bibliothèque graphique, rien.**

Cette règle a déjà sauvé le projet trois fois. Concrètement, `Exarion.Core` :

- ne connaît pas `Vector3` d'un moteur : il a son propre `Vec2`
- ne connaît pas les joueurs : il passe par l'interface `IRoundWorld`
- se teste **en 197 ms en ligne de commande**, sans lancer quoi que ce soit

Toute logique de jeu va dans `Core`. Le moteur ne fait que l'exécuter et la dessiner.

### État vérifié — 15/08/2026

```
dotnet test  ->  50 tests, 50 réussis, 0 échec, 197 ms
dotnet build ->  0 erreur
```

| Suite | Tests | Couvre |
|---|---|---|
| `RoundMachineTests` | 13 | états, élimination, fin de match, primes cumulatives |
| `BallisticsTests` | 20 | zones, atténuation, patterns de recul, dispersion |
| `EconomyTests` | 17 | porte-monnaie, achats refusés sans effet de bord |

---

## 5. Structure de la solution

```
Exarion.sln
  src/
    Exarion.Core/          ✅ FAIT - logique pure, zéro dépendance
      RoundTypes.cs          enums + RoundConfig (tous les réglages)
      IRoundWorld.cs         frontière logique <-> monde
      RoundMachine.cs        machine à états du round
      Vec2.cs                vecteur minimal
      Ballistics.cs          recul, dispersion, dégâts
      Arsenal.cs             7 armes + patterns
      BuyCatalog.cs          catalogue et prix
      Economy.cs             Wallet, Loadout, BuyService
    Exarion.Engine/        ⬜ à écrire
    Exarion.Game/          ⬜ à écrire
    Exarion.Editor/        ⬜ à écrire
  tests/
    Exarion.Core.Tests/    ✅ FAIT - 50 tests
  reference/
    layout/MapBuilder.cs     coordonnées exactes de de_exarion, en mètres
```

## 6. Plan du moteur

Ce qu'on **n'écrit pas** — ces briques existent, sont éprouvées, et gratuites :

| Besoin | Bibliothèque |
|---|---|
| Fenêtre, entrées, boucle | Silk.NET.Windowing / Input |
| Accès GPU (Vulkan / OpenGL) | Silk.NET |
| Physique et collisions | JoltPhysicsSharp |
| Transport réseau | GameNetworkingSockets, ou UDP maison |
| Chargement de modèles | glTF via SharpGLTF |
| Audio | miniaudio via bindings, ou OpenAL-Soft |
| UI d'outils | Dear ImGui via ImGui.NET |

Ce qu'on **écrit** :

| Module | Contenu |
|---|---|
| Boucle et temps | tick fixe serveur, interpolation client |
| Renderer | pipeline forward, matériaux, éclairage |
| Scène et culling | découpage spatial, visibilité |
| Contrôleur joueur | modèle Quake : friction + accélération projetée |
| **Netcode** | prédiction, réconciliation, **rembobinage de hitbox** |
| Pipeline d'assets | import, formats internes |
| Éditeur | ou export depuis Blender |

### Ordre de construction

Chaque étape doit produire quelque chose de **visible ou mesurable**. Pas de module écrit
« pour plus tard ».

1. Fenêtre + boucle + triangle à l'écran
2. Caméra, maillages, textures
3. Physique + contrôleur joueur — **on peut marcher dans une boîte**
4. Chargement de la map depuis les coordonnées de `reference/layout/`
5. Tir hitscan branché sur `Ballistics` — **le jeu devient jouable en solo**
6. HUD
7. **Netcode** : d'abord 2 clients qui se voient, puis prédiction, puis lag compensation
8. Bombe, sites, fin de round

---

## 7. Jalons

> **Ordre de travail décidé le 15/08/2026 : le MOTEUR d'abord, le jeu ensuite.**
> Le terrain d'essai est un **bac à sable**, pas un mini Counter-Strike. Tant que le
> moteur n'est pas solide, ajouter des règles de jeu ne fait que masquer ses défauts.
> La logique de jeu (round, économie, achat) reste écrite et testée dans `Core`,
> en attente d'être branchée.

### Moteur

| # | Livrable | Critère de réussite | État |
|---|---|---|---|
| M0 | Logique de jeu testée hors moteur | tests au vert | ✅ 15/08 |
| M1 | Fenêtre + boucle à pas fixe + triangle | cadence tenue, 0 pas sauté | ✅ 15/08 |
| M2 | Caméra, rendu 3D, collision, déplacement | on marche dans une pièce | ✅ 15/08 |
| M3 | Lancer de rayon, zones de dégâts | on tire sur des cibles | ✅ 15/08 |
| M4 | **Physique dynamique** : caisses, piles, explosions | une pile de 40 caisses reste stable | ✅ 15/08 |
| M5 | **Texte à l'écran** : police bitmap 5×7 | libellés lisibles | ✅ 15/08 |
| M6 | **Console de debug** : variables réglables à chaud | régler sans recompiler | ✅ 15/08 |
| M7 | **Éclairage** : 3 types, espace linéaire, spéculaire | 715 img/s avec 12 lumières | ✅ 15/08 |
| M8 | **Ombres** (shadow mapping, PCF) | coût ~8 %, **validé par capture** | ✅ 15/08 |
| M9 | **Textures** : PNG maison, procédurales, captures d'écran | grille lisible, rendu vérifiable | ✅ 15/08 |
| M10 | **Matériaux PBR** (Cook-Torrance GGX) | validé par capture | ✅ 15/08 |
| M11 | Chargement de modèles (glTF) | ⬜ |
| M12 | Son 3D | on entend d'où vient un pas | ⬜ |

> Le détail du plan complet, avec estimations et points de décision, est dans
> **[ROADMAP.md](ROADMAP.md)**.
| M8 | **Réseau** : 2 clients qui se voient | pas de désync en LAN | ⬜ |
| M9 | Prédiction + réconciliation | jouable à 80 ms de ping | ⬜ |
| M10 | Lag compensation | toucher est juste | ⬜ |

### ~~Jeu — après le moteur~~ — RETIRÉ DU PLAN le 16/08

| # | Livrable | État |
|---|---|---|
| ~~J1~~ | ~~Round + économie branchés~~ | ❌ hors périmètre |
| ~~J2~~ | ~~Bombe : pose, désamorçage, explosion~~ | ❌ hors périmètre |
| ~~J3~~ | ~~Map à 2 sites, playtest 5v5~~ | ❌ hors périmètre |

⚠️ **On perd au passage le seul juge extérieur du projet.** « Des gens veulent refaire une
partie » ne dépendait pas de nous ; le bac à sable, si. La validation repose désormais
entièrement sur des mesures, et sur un humain qui regarde et écoute.

---

## 8. Environnement vérifié

| Outil | Version | État |
|---|---|---|
| .NET SDK | 9.0.306 et 8.0.417 | ✅ (.NET 10 non installé, migration triviale) |
| Visual Studio 2022 | 17.14 + workload C++ | ✅ |
| Python | 3.14.3 | ✅ |
| git | 2.53.0 | ✅ |
| Espace libre E: | 109 Go | ✅ |

## 8bis. J1 — mesures réelles

Lancé sur la machine cible (RTX 5060 Ti, OpenGL 3.3.0 NVIDIA 591.86) :

```
64 Hz  / 6 s  ->  384 pas simulés, 0 sauté     (384 = 64 × 6, exact)
128 Hz / 5 s  ->  636 pas simulés, 4 sautés    (636 + 4 = 640 = 128 × 5, exact)
Rendu : ~100 img/s, indépendant de la simulation
```

La séparation simulation/rendu est démontrée contre une horloge réelle, pas seulement
en test unitaire. Les 4 pas sautés à 128 Hz sont un hoquet ponctuel détecté et compté
par `SkippedSteps` — l'instrumentation fait son travail.

Lancer :

```powershell
dotnet run --project src\Exarion.Game -c Release -- --seconds 10 --tickrate 64
```

Options : `--seconds N` (fermeture auto, pour valider sans intervention),
`--tickrate N`, `--novsync`.

## 8ter. J2 — collision, déplacement, rendu 3D

**Décision : pas de moteur physique externe.** Pour un greybox fait de boîtes alignées sur
les axes, un balayage AABB écrit à la main suffit, se teste sans fenêtre, et n'ajoute aucune
dépendance. Jolt viendra pour les grenades et les objets qui roulent, pas pour marcher.

| Fichier | Rôle |
|---|---|
| `Core/Physics/Aabb.cs` | Boîte alignée, somme de Minkowski |
| `Core/Physics/CollisionWorld.cs` | Balayage (méthode des tranches), déplacement avec glissement |
| `Core/Physics/CharacterMotor.cs` | Modèle Quake : friction + accélération projetée, accroupi, saut, franchissement de marche |
| `Engine/Graphics/Camera.cs` | Matrices vue/projection |
| `Engine/Graphics/MeshBuilder.cs` | Génère la géométrie **depuis le monde de collision** |

**Choix structurant : la géométrie visible est générée depuis le monde de collision.**
Le « mur invisible » et le « mur qu'on traverse » sont deux des bugs les plus pénibles d'un
FPS. Ici ils ne peuvent pas exister par construction.

### Mesures

```
Niveau : 26 boîtes, 312 triangles
100 img/s, 64 pas/s, 0 pas sauté
88 tests au vert (dont 23 de physique), 69 ms
```

## 8quater. M4 — physique dynamique

`Core/Physics/RigidBody.cs` et `PhysicsWorld.cs`. Des caisses qui tombent, glissent,
s'empilent, se poussent et volent quand ça explose.

**Choix assumé : aucune rotation.** Les corps restent alignés sur les axes. Une caisse ne
basculera jamais sur un coin — c'est une limite réelle. En échange : 200 lignes, parfaitement
déterministe, testable sans fenêtre, et impossible à faire exploser numériquement. Quand il
faudra de vraies rotations, Jolt prendra le relais et l'interface bougera peu.

Ordre d'un pas :

1. gravité et frottement
2. déplacement contre le décor **statique** (balayage + glissement)
3. séparation des corps **dynamiques** entre eux (3 passes)
4. **ressortie du décor statique** ← l'étape qu'on oublie, voir pièges
5. mise en sommeil de ce qui ne bouge plus

Le sommeil n'est pas un détail : sans lui une pile vibre indéfiniment et brûle du CPU pour
rien. Les caisses endormies sont assombries à l'écran — on voit d'un coup d'œil ce que le
moteur simule encore.

### Bac à sable

`Exarion.Game` n'est plus un mini-CS mais un terrain d'essai du moteur.

| Touche | Effet |
|---|---|
| F | poser une caisse devant soi |
| V | lâcher une pile de 5 |
| G | explosion à l'endroit visé |
| C | tout effacer |
| K | vue des collisions (fil de fer) |
| Clic | tirer — la balle pousse les caisses |

Décor : 32 boîtes — escalier de 8 marches, 3 plateformes à sauter, 5 piliers, une rampe et
un enclos pour empiler.

## 8quinquies. M5 / M6 — texte et console

### Police bitmap

`Engine/Graphics/BitmapFont.cs` : glyphes 5×7, une colonne par octet, bit 0 en haut.
Couvre l'ASCII 32–93 ; les minuscules sont rendues en majuscules, usage classique des
consoles de debug.

Charger un `.ttf` aurait demandé FreeType, un atlas de textures, des métriques et du kerning.
Pour des libellés de HUD et une console, une grille de pixels suffit et ne dépend de rien.
Un pixel de glyphe = un rectangle du `HudRenderer` déjà écrit.

### Console

`Core/Console/ConsoleSystem.cs` — **dans `Core`, donc testé** (32 cas).
`Engine/DebugConsole.cs` — saisie clavier et affichage seulement.

**F1** ouvre la console. Pas la touche sous Échap : sa position et son caractère changent
selon la disposition du clavier.

```
help              aide
list [filtre]     liste variables et commandes
<nom>             affiche une valeur
<nom> <valeur>    change une valeur, bornée automatiquement
reset [nom]       valeur(s) par défaut
Tab               complète
Flèches haut/bas  historique
```

Variables exposées : `speed_run`, `speed_walk`, `speed_crouch`, `jump_height`, `gravity`,
`accel_ground`, `accel_air`, `friction`, `step_height`, `fov`, `sensitivity`, `crate_mass`,
`crate_size`, `bullet_force`, `boom_radius`, `boom_force`, `phys_iterations`, `phys_sleep`,
`wireframe`, `showstats`.

Commandes : `spawn [n]`, `stack [n]`, `boom`, `crates [clear]`, `tp`.

**C'est l'outil qui compte le plus pour le réglage du feeling.** Le feeling ne se calcule pas,
il s'essaie — et l'essayer suppose de changer une valeur en une seconde, pas en trente.

## 8sexies. Éclairage

`Engine/Graphics/Light.cs` et `SceneRenderer.cs`. Pipeline **avant** : chaque surface évalue
toutes les lumières. Suffisant tant qu'on reste sous quelques dizaines de sources ; un
pipeline différé ne se justifierait qu'avec des centaines.

Trois types : **directionnelle** (le soleil), **ponctuelle** (une ampoule), **projecteur**
(un cône).

### Deux décisions qui changent tout visuellement

**1. Éclairage en espace linéaire.** Les couleurs qu'on écrit (« gris 0.5 ») sont en sRGB,
un espace perceptuel non linéaire. Additionner des lumières dans cet espace donne un rendu
délavé et des ombres grises. On convertit en linéaire avant d'éclairer, on revient en sRGB
à la fin. C'était un défaut du rendu précédent.

**2. Atténuation fenêtrée.** La loi physique `1/d²` ne s'annule jamais : chaque lumière
devrait être évaluée pour chaque pixel de la scène. On la multiplie par une fenêtre qui
s'annule proprement à la portée :

```
atténuation = saturate(1 − (d/portée)⁴)² / (d² + 1)
```

Ajouts : spéculaire Blinn-Phong (le demi-vecteur évite l'artefact de coupure du Phong
classique aux angles rasants), exposition réglable, et compression douce des hautes
lumières `x/(x+1)` pour éviter les zones cramées.

### Mesure

```
12 lumières (1 directionnelle + 9 ponctuelles + 2 projecteurs)
avec VSync   : 100 img/s
sans VSync   : 715 img/s
simulation   : 64 pas/s, 0 pas sauté dans les deux cas
```

La simulation reste verrouillée quel que soit le framerate — c'est exactement ce que le pas
fixe doit garantir.

### Variables de console

`light_enabled`, `light_sun`, `light_lamps`, `light_ambient`, `light_specular`,
`light_shininess`, `exposure`.

## 8septies. Ombres

`Engine/Graphics/ShadowMap.cs`. Shadow mapping directionnel.

**Principe** : on rend la scène une première fois *depuis la lumière*, en ne gardant que la
profondeur. Au rendu final, chaque pixel est reprojeté dans cet espace — si quelque chose
était plus proche de la lumière que lui, il est dans l'ombre.

### Les trois défauts classiques, et ce qu'on fait contre

| Défaut | Cause | Parade appliquée |
|---|---|---|
| **Acné** — rayures sombres sur les surfaces éclairées | résolution finie de la carte | biais **proportionnel à l'inclinaison** : une surface rasante en a bien plus besoin |
| **Peter-panning** — l'ombre se décolle, l'objet semble léviter | biais trop grand | ne rendre que les **faces arrière** dans la passe d'ombre : l'erreur se déplace à l'intérieur des objets |
| **Bords en escalier** | un texel = un grand carré au sol | **PCF** : moyenne des texels voisins |

Deux détails qui comptent : le volume d'ombre **suit le joueur** (une carte 2048 ne peut pas
couvrir 78 m avec assez de finesse), et la bordure de la texture est **blanche** — sans quoi
tout ce qui dépasse la zone couverte serait dans l'ombre.

### ⚠️ Piège : la projection orthographique aussi

`Matrix4x4.CreateOrthographic` de System.Numerics souffre du **même défaut** que sa version
perspective : profondeur `[0,1]` au lieu de `[-1,1]`. L'utiliser écraserait toute la carte
d'ombre dans la moitié du tampon. Construite à la main dans `ShadowMap.OrthographicForOpenGL`.

### Coût mesuré

```
sans ombres                       ~3 240 img/s
avec ombres (2048, PCF 3x3)       ~2 980 img/s      -> environ 8 %
```

Deux essais consécutifs dans des conditions identiques. **Les valeurs absolues ne sont pas
comparables d'une session à l'autre** — la visibilité de la fenêtre les fait varier d'un
facteur 4. Seuls les rapports mesurés dos à dos ont du sens.

### Variables de console

`shadow_strength`, `shadow_bias`, `shadow_softness`, `shadow_extent`, `shadow_res`,
`shadow_cullfront`.

## 8octies. Textures, PNG, captures d'écran

### PNG maison, zéro dépendance

`Core/Assets/Png.cs` — décodeur **et** encodeur.

ImageSharp est sous **licence Six Labors Split**, pas librement commerciale : écartée par la
règle de `DEPENDENCIES.md`. StbImageSharp serait MIT, mais .NET embarque déjà `ZLibStream`,
c'est-à-dire la seule partie réellement difficile du format. Le reste n'est que du découpage
de blocs et du défiltrage de lignes.

Porte : 8 bits par canal, types de couleur 0/2/4/6, les 5 filtres (None, Sub, Up, Average,
Paeth). Pas d'entrelacement Adam7 — rarissime, et ça doublerait la complexité.

**Testé par aller-retour** : on encode, on décode, on compare les pixels. Aucun fichier
binaire de référence dans le dépôt. Les filtres sont testés en fabriquant à la main des flux
qui les utilisent, puisque notre encodeur n'écrit que le filtre 0.

### Textures procédurales

`Core/Assets/TextureGen.cs` — grille de développement, damier, uni. **Une case par mètre** :
l'échelle du décor devient lisible d'un coup d'œil. Générée en mémoire, donc aucun asset à
charger, versionner ou distribuer.

### Projection planaire monde

Les UV sont calculées **dans le shader, depuis la position monde**, selon l'axe dominant de la
normale. Conséquence : densité de texels identique partout. Une caisse d'1 m et un mur de 20 m
ont exactement les mêmes carreaux.

Calculé dans le shader et non par sommet, parce que les caisses sont un **cube unitaire mis à
l'échelle** — leurs UV de sommets seraient étirées.

### ⚡ Captures d'écran : le changement le plus important

`screenshot [fichier]` en console, ou `--screenshot fichier.png` en ligne de commande.
Utilise l'encodeur PNG maison.

**Ça change la nature du travail.** Jusqu'ici je ne pouvais pas voir le rendu, et le bug
d'étirement l'avait prouvé. Dès la première capture, deux défauts invisibles autrement :

1. **L'éclairage était ~10× trop faible.** Lampes à intensité 14 avec atténuation en `1/d²` :
   à 10 m il ne reste qu'un centième. La salle était quasiment noire. Corrigé à **140**,
   ambiante relevée à 0.30, exposition à 1.4 — **valeurs choisies en regardant des captures**.
2. **Les ombres étaient invérifiables** : la salle avait un plafond plein, donc la seule
   lumière qui projette des ombres était bloquée. Le toit couvre désormais la **moitié nord**
   seulement, la moitié sud est à ciel ouvert.

Vérifié ensuite par A/B `--shadows 0` / `--shadows 1` : les piliers projettent des ombres
longues, nettes, **collées à leur base**. Ni acné, ni peter-panning.

Combiné à `--exec "commande"`, on peut régler un paramètre et en voir l'effet **sans ouvrir
le jeu** :

```powershell
dotnet run --project src\Exarion.Game -c Release -- `
  --seconds 3 --screenshot rendu.png --exec "light_lamps 200" --exec "exposure 2"
```

## 8nonies. Matériaux — PBR

`Engine/Graphics/Material.cs` et le shader de `SceneRenderer`. Modèle **metallic-roughness**,
celui de glTF et de la quasi-totalité des moteurs actuels. Le Blinn-Phong est remplacé par
**Cook-Torrance avec distribution GGX**.

La réflexion spéculaire se décompose en trois termes :

| Terme | Rôle |
|---|---|
| **D** — distribution GGX | quelle proportion des microfacettes est orientée pour réfléchir vers l'œil. Donne sa **forme** au reflet |
| **G** — Smith / Schlick-GGX | quelle proportion n'est ni masquée ni ombrée par ses voisines. Compte aux angles rasants |
| **F** — Fresnel Schlick | tout devient miroir quand on le regarde de biais. **C'est ce qui manquait le plus au Blinn-Phong** |

Deux paramètres suffisent :

- **Rugosité** — 0 = miroir, 1 = mat. Contrôle la taille du reflet.
- **Métallicité** — 0 = diélectrique (béton, plastique, bois), 1 = métal.
  Les valeurs intermédiaires n'ont pas de sens physique : une surface est métallique ou
  elle ne l'est pas.

La différence physique est réelle : un **diélectrique** réfléchit ~4 % sans teinter et diffuse
le reste avec sa couleur ; un **métal** ne diffuse *rien* et teinte son reflet. C'est pourquoi
un métal apparaît noir s'il n'a rien à réfléchir.

Conservation de l'énergie appliquée : `kd = (1 − F) × (1 − métallicité)` — ce qui part en
spéculaire n'est plus disponible pour la diffusion.

### Vérifié par capture

`mat_metallic 1` + `mat_roughness 0.18` : la scène devient noire, ne subsistent que des
reflets spéculaires nets teintés par les lampes, avec des traînées miroir au sol.
Comportement de métal manuel, obtenu sans réglage ad hoc.

Variables : `mat_roughness`, `mat_metallic`.

## 8decies. Réalisme du rendu

Quatre ajouts, du moins cher au plus structurant :

| Ajout | Effet |
|---|---|
| **MSAA ×4** | supprime les arêtes en escalier. C'est ce qui trahissait le plus le prototype |
| **Ciel dégradé** | `SkyRenderer` : zénith → horizon + halo solaire, en un seul triangle plein écran sans tampon de sommets. Un fond noir supprimait toute profondeur |
| **Tone mapping ACES** | remplace `x/(x+1)`, qui délavait les couleurs vives et donnait un rendu laiteux. ACES conserve la saturation dans les hautes lumières — c'est la courbe du cinéma |
| **Ambiante hémisphérique** | ce qui regarde vers le haut reçoit la couleur du ciel, vers le bas celle du sol. Une ambiante uniforme aplatit tout ; celle-ci donne l'impression d'un objet **posé dans un lieu** |

Coût mesuré : **~2 750 img/s** avec MSAA ×4, ciel, ombres et 12 lumières.

Variables : `sky_brightness`, `sky_sun_size`, `light_ambient`.

### Commandes de caméra

`tp [x y z]` et `look <lacet> [tangage]` permettent de **reproduire exactement un cadrage**.
Sans ça, impossible de comparer deux rendus : chaque capture montrait autre chose.

## 8undecies. Frottement de contact

`PhysicsWorld.ResolvePair` applique désormais un **frottement de Coulomb** entre corps :
opposé au mouvement tangentiel, d'intensité plafonnée par la force normale. Plus on appuie,
plus ça accroche.

### ⚠️ Deux pièges, trouvés par les tests

1. **Le contact au REPOS n'avait aucun frottement.** La résolution sortait avant, faute de
   rapprochement — or c'est précisément là que le frottement compte. Sans lui, une caisse posée
   sur une autre n'est jamais entraînée et une pile se disperse au moindre choc. Corrigé en
   estimant la force normale par le **poids supporté** pendant le pas de temps.
2. **Résoudre un contact réveillait les corps à chaque passe**, donc aucune pile ne
   s'endormait plus et le moteur simulait indéfiniment des caisses immobiles. On ne réveille
   désormais que sur un **vrai impact** (`along < -0.05`).

### Limite assumée : pas de rotation

Les corps restent alignés sur les axes. Une caisse ne bascule jamais sur un coin. Passer à
de vraies rotations demande de la collision OBB ou convexe — c'est un **point de décision**
(voir `ROADMAP.md`) : soit plusieurs semaines de travail, soit intégrer Jolt (MIT).

## 9. Pièges déjà rencontrés

- **Accumuler des deltas en virgule flottante fait dériver la simulation.** Première
  version de `FixedTimestep` : `accumulateur += deltaImage`, puis soustractions
  répétées. Résultat mesuré : **49 pas au lieu de 50** sur une seconde à 50 Hz, parce
  que `1/50` n'est pas exact en binaire. Corrigé en raisonnant en **temps absolu** et
  en **nanosecondes entières**. Trouvé par un test, pas en jeu — c'est précisément ce
  genre de dérive qui désynchronise un client d'un serveur.
- **`SkippedSteps` doit être distinct de `TotalSteps`.** Quand la machine décroche,
  l'horloge de jeu saute en avant pour rester alignée sur le temps réel, mais ces pas
  ne sont pas simulés. Les confondre donnerait un compteur de ticks mensonger.
- **Silk.NET : ne pas libérer le contexte d'entrées deux fois.** `OnClosing` puis
  `Dispose()` lève une `ObjectDisposedException`. Mettre la référence à `null` après
  libération.
- **La normale d'un balayage AABB vaut `-signe(direction)`, sans exception.** Première
  version : le signe était inversé lors de l'échange `t1`/`t2`, donc correct vers +X mais
  faux vers −Y. Conséquence : le joueur n'était **jamais détecté au sol**, donc ni friction,
  ni saut, ni posture. Un seul bug, 8 tests rouges.
- **`Matrix4x4.CreatePerspectiveFieldOfView` de System.Numerics est en profondeur `[0,1]`**
  (convention DirectX). OpenGL 3.3 attend `[-1,1]`. L'utiliser telle quelle écrase toute la
  scène dans la moitié proche du tampon de profondeur. La matrice est construite à la main
  dans `Camera.GetProjectionMatrix`.
- **🔴 `glUniformMatrix4fv` : `transpose` doit être `false` avec System.Numerics.**
  Le bug le plus coûteux jusqu'ici — l'image était **étirée**, et aucun test ne pouvait le
  voir. System.Numerics est en convention *vecteur-ligne* (`v * M`), GLSL en *vecteur-colonne*
  (`M * v`) : GLSL a donc besoin de la **transposée**. Or la mémoire de System.Numerics est
  ligne-major, et OpenGL lit le tableau comme des **colonnes** quand `transpose = false` —
  ce qui produit exactement la transposée voulue. Avec `true`, le terme de division
  perspective (`M34 = -1`) se retrouvait à la mauvaise place, d'où la déformation.
  **Leçon retenue :** toute la géométrie de caméra est désormais couverte par
  `CameraTests` (14 cas) — un point droit devant doit tomber au centre, le plan proche
  sur `z = -1`, un angle de 45° sur le bord exact de l'écran.
- **Champ de vision : le spécifier en HORIZONTAL, pas en vertical.** 74° vertical (mon
  réglage initial) fait ~106° horizontal : bien trop large, les bords s'étirent. On donne
  90° horizontal comme CS et on dérive le vertical du format d'image — comportement « Hor+ »,
  un écran 21:9 voit plus sur les côtés au lieu de perdre en hauteur.
- **Utiliser `FramebufferSize`, jamais `Window.Size`.** Les deux diffèrent sur un écran à
  mise à l'échelle DPI, et le viewport doit être posé au chargement, pas seulement sur
  l'événement de redimensionnement.
- **🔴 Une exception dans un gestionnaire de variable de console tuait tout le jeu.** Les
  *commandes* étaient protégées, pas les *variables*. Or leurs gestionnaires appellent du code
  moteur qui peut échouer — `shadow_res 8192` demande une texture de 256 Mo. L'exception
  remontait jusqu'au gestionnaire clavier et faisait tomber le processus, pour une simple
  valeur mal choisie. Corrigé : `try` autour du `Changed`, retour à la valeur précédente,
  message d'erreur. Le `reset` global continue aussi malgré une variable récalcitrante.
- **Un crash sans trace est un crash qu'on ne corrigera pas.** Ajout de `plantage.txt` :
  gestionnaire global + rapport avec pile d'appels, temps de simulation, nombre de corps et
  état de la console. Sans ça, un plantage se résume à « la fenêtre s'est fermée ».
- **Les mesures de framerate ne sont comparables qu'au sein d'une même session.** Mesuré
  715 img/s puis 3 240 img/s pour une scène quasi identique : la visibilité de la fenêtre
  fait varier le résultat d'un facteur 4. Pour comparer deux options, il faut un drapeau en
  ligne de commande et **deux essais consécutifs** — d'où `--shadows 0|1`.
- **Un test qui échoue n'accuse pas toujours le code.** 4 échecs de physique venaient d'une
  pièce de test trop petite : le joueur atteignait le mur (vitesse 0) ou sortait du sol et
  tombait. Vérifier le décor du test avant de soupçonner la simulation.
- **🔴 Séparer deux corps dynamiques peut les enfoncer dans le décor.** La résolution de
  contact déplace les positions **directement**, sans balayage. Une caisse poussée vers le bas
  par celle du dessus traversait donc le sol, puis tombait à l'infini — mesuré à **−273 m**.
  Il faut une étape de **ressortie du statique** (`CollisionWorld.Depenetrate`) **après** chaque
  passe de séparation. Symptôme trompeur : une caisse seule fonctionnait parfaitement, le bug
  n'apparaissait qu'à partir de deux.
- **NUnit 4 a supprimé les assertions classiques** (`Assert.AreEqual`, `Assert.Greater`…).
  Unity livre NUnit 3. Migration : `using NUnit.Framework.Legacy` + `ClassicAssert.`.
  L'analyseur proteste ensuite (NUnit2005) — désactivé volontairement dans le `.csproj`.
  `Assert.Throws`, lui, existe toujours sur `Assert`.
- **Deux instances Unity ne peuvent pas ouvrir le même projet.** Enchaîner `-executeMethod`
  puis `-runTests` échoue si la première n'a pas fini de sortir. *(Historique, plus applicable.)*

## 10. Licence et distribution

Plus aucune contrainte héritée : le moteur est le nôtre, les bibliothèques utilisées sont
sous licences permissives (MIT, Apache, zlib). **Vérifier la licence de chaque dépendance
avant de l'ajouter** et tenir la liste à jour ici.

Le jeu pourra être distribué gratuitement et monétisé par cosmétiques, sans royalties.
