# Exarion

Un moteur 3D écrit à la main en **C# / .NET 9**, rendu **OpenGL 3.3**, sans moteur de jeu
en dessous. Fenêtre, entrées et accès GPU passent par Silk.NET ; tout le reste — boucle de
simulation, physique, éclairage, ombres, occlusion, HDR, audio, import de modèles, décodage
PNG, éditeur de niveau — est écrit dans ce dépôt.

**Ce dépôt livre un moteur, pas un jeu.** Il y a de quoi construire un jeu ; il n'y a pas de
jeu fini dedans. La section [Ce qui manque](#ce-qui-manque) est aussi importante que le reste.

---

## Récupérer le moteur

Il n'y a **pas d'exécutable à installer** : le moteur se distribue en code source, pour que
chacun le compile et le modifie à sa façon.

- **Une version figée** : page [Releases](https://github.com/Lionsvg/Exarion/releases), fichier
  *Source code (zip)*, à décompresser où l'on veut.
- **La dernière version** : `git clone https://github.com/Lionsvg/Exarion.git`

Il faut le [SDK .NET 9](https://dotnet.microsoft.com/download) et un GPU capable d'OpenGL 3.3
(donc à peu près tout depuis 2010). Rien d'autre : les cinq paquets NuGet se téléchargent
seuls à la première compilation.

## Démarrer en trois commandes

```bash
dotnet test
```

Plusieurs centaines de tests, quelques secondes. Ils tournent **sans ouvrir de fenêtre** : toute
la logique de jeu vit dans un projet qui ne connaît ni OpenGL ni Silk.NET.

```bash
dotnet run --project samples/Exarion.Sample -c Release
```

Le jeu d'exemple : une carte, un personnage qui marche, 187 lignes. ZQSD pour se déplacer,
souris pour regarder, Échap pour quitter.

```bash
dotnet run --project src/Exarion.Game -c Release
```

Le bac à sable : menu, éditeur de niveau, physique des caisses, PNJ, console de debug (F1).
C'est là que tout se règle et se démontre.

⚠️ **Le personnage animé n'est pas fourni.** Le bac à sable cherche un modèle `.glb` dans un
dossier `Model3D/` et démarre sans lui s'il n'y en a pas ; les 22 tests qui l'utilisent sont
alors **ignorés**, pas en échec. **Conséquence visible : les PNJ et les joueurs distants en
réseau sont simulés mais invisibles** — c'est ce modèle qui les dessine. Pour les voir, déposer
dans `Model3D/` un glTF binaire skinné ; ses animations « Walking » et « Running » servent de
marche et de course, à défaut les deux premières.

---

## Les projets, et la règle qui les sépare

| Projet | Rôle | Dépend de |
|---|---|---|
| `src/Exarion.Core` | Logique : temps, physique, cartes, audio, animation, entités, PNJ | **rien** |
| `src/Exarion.Engine` | Fenêtre, GPU, passes de rendu, périphérique audio | Core + Silk.NET + miniaudio |
| `src/Exarion.Game` | Le bac à sable : menu, éditeur, console, contenu de démo | Core + Engine |
| `samples/Exarion.Sample` | Un jeu minimal | Core + Engine |
| `tests/Exarion.Core.Tests` | Les tests | Core |

**`Exarion.Core` ne référence aucune bibliothèque graphique, et ce n'est pas une coquetterie
d'architecture.** Ce projet a changé de moteur trois fois (s&box, puis Source SDK 2013, puis
Unity) avant d'écrire le sien. Ce qui a survécu aux trois, c'est exactement le code qui ne
dépendait de rien. La conséquence pratique : la physique, l'économie, le round, les PNJ se
testent en ligne de commande, sans GPU, en deux secondes.

`samples/Exarion.Sample` **ne référence jamais `Exarion.Game`**. C'est délibéré et c'est une
mesure : un moteur qui n'a qu'un seul jeu peut être arbitrairement couplé à ce jeu sans que
personne ne s'en aperçoive. Il faut un second client pour que le couplage se voie. Les
extractions de `RenderPipeline`, `Screenshot` et `MapLighting` ont toutes été **découvertes en
écrivant ce fichier d'exemple**, pas en relisant le moteur.

---

## Écrire son propre jeu

Le squelette complet est dans [`samples/Exarion.Sample/Program.cs`](samples/Exarion.Sample/Program.cs).
Il tient en trois idées.

**1. Un hôte, trois événements.** `EngineHost` ouvre la fenêtre et tient la boucle à pas fixe.

```csharp
using var engine = new EngineHost(new EngineOptions { Title = "Mon jeu", Width = 1280, Height = 720 });

engine.Load     += () => Load(engine);
engine.Simulate += (dt, step) => Simulate((float)dt);   // 64 fois par seconde, toujours
engine.Render   += alpha => Render(engine, alpha);      // aussi souvent que la machine peut

engine.Run();
```

`Simulate` reçoit **toujours** le même `dt`, quelle que soit la machine. `Render` reçoit
`alpha`, la fraction de pas écoulée, pour interpoler entre deux états simulés.

**2. Un pipeline, une ligne.** `RenderPipeline` possède les neuf objets de rendu (scène,
ombres, ciel, interface, occlusion, HDR, halo, particules, chronomètre GPU) et enchaîne les
passes dans le seul ordre qui marche.

```csharp
_render = new RenderPipeline(engine.Gl, frame.X, frame.Y);
_render.Scene.Shadows = _render.Shadows;
_render.Scene.LoadLights(map);              // les lumières de la carte
```

**3. Trois méthodes à fournir.** Le jeu implémente `IWorldRenderer` :

```csharp
// Toute la géométrie, sans matériau. Appelée plusieurs fois par image
// (ombre, occlusion) : les deux passes DOIVENT voir la même chose.
public void DrawGeometry(GeometrySink sink) => sink(_levelMesh, Matrix4x4.Identity, null);

// La passe finale : matériaux, teintes, os.
public void DrawOpaque(SceneRenderer scene) => scene.Draw(_levelMesh, Matrix4x4.Identity, Vector3.One);

// L'interface, déjà en couleurs d'affichage.
public void DrawHud(HudRenderer hud, int w, int h) { ... }
```

Puis une ligne par image :

```csharp
_render.Render(frame, _camera, _sun, this, particules, alpha);
```

L'ordre des passes, les tampons multi-échantillonnés, la résolution avant le halo, le tone
mapping après les particules mais avant l'interface : tout ça appartient au moteur, et il
n'en existe qu'une copie. Chaque contrainte de cet ordre a coûté une session de mise au
point — elles sont commentées une par une dans
[`RenderPipeline.cs`](src/Exarion.Engine/Graphics/RenderPipeline.cs).

---

## Ce que le moteur sait faire

**Simulation** — boucle à pas fixe déterministe en nanosecondes entières (jamais des deltas
accumulés : ça dérive, mesuré à 49 pas au lieu de 50 sur une seconde). Collision AABB et
déplacement façon Quake. Corps rigides avec rotation : OBB, théorème des axes séparateurs,
points de contact par découpage de Sutherland-Hodgman, solveur à impulsions séquentielles,
frottement de Coulomb, amortissement et mise en sommeil. Lancer de rayon et zones de dégâts.

**Rendu** — éclairage PBR Cook-Torrance, jusqu'à 32 lumières (directionnelle, ponctuelle,
spot). Ombres par shadow mapping + PCF. Occlusion ambiante en espace écran. Rendu HDR
flottant, exposition, tone mapping ACES, halo (bloom) par flou gaussien séparable en
demi-résolution. MSAA. Ciel procédural. Particules instanciées. Texte bitmap.

**Contenu** — import **glTF binaire (`.glb`)** écrit à la main : géométrie, matériau,
squelette, animations, skinning GPU, et mélange de poses par **phase** (marcher et courir
n'ont pas la même durée de cycle ; les échantillonner au même temps absolu donne le grand
écart). Décodeur et encodeur **PNG maison** — il lit les textures 2048×2048 sorties de
Blender. Format de carte texte, versionné, lisible et diffable.

**Réseau** — serveur **autoritaire** sur UDP, sérialisation binaire maison, entrées envoyées en
redondance, instantanés, interpolation des joueurs distants, **prédiction client**,
**réconciliation** par rejeu, et **compensation de latence** par rembobinage des hitbox. Le
netcode vit dans `Exarion.Core` au-dessus d'une interface de transport : un réseau simulé
déterministe (latence, gigue, perte, duplication, désordre) permet de le tester sans socket ni
seconde machine.

```bash
dotnet run --project src/Exarion.Game -c Release -- --exec "host 27015"
```

puis, depuis une autre machine ou un autre terminal, `connect <ip> 27015`. `net` donne le débit,
l'aller-retour et l'écart de prédiction ; `net_predict 0` la coupe, pour sentir ce qu'elle
apporte.

📏 Mesuré entre deux processus, 16 s de jeu : **1 seule correction sur 1 024 pas**, écart de
prédiction courant **0,0 cm**, pire **2,2 cm**. Un instantané fait 11 octets + 18 par joueur.

**Audio** — mixage logiciel maison par-dessus miniaudio : spatialisation à puissance
constante, atténuation par la distance, effet Doppler, occlusion par filtre passe-bas.
Enregistrement en WAV, qui sert de « capture d'écran du son ».

**Outils** — éditeur de niveau intégré (placement, redimensionnement par face, grille,
annulation par instantanés), entités à propriétés avec sorties vers entrées, points
d'apparition, checkpoints, événements, PNJ avec perception et poursuite. Console de debug
avec une soixantaine de variables réglables en direct, sauvegardées dans `exarion.cfg`.

**Géométrie mobile** — portes, herses, ascenseurs et ponts, déclarés **entièrement en
données** : un `func_door` avec un axe, une distance, une vitesse, et il répond aux entrées
`Ouvrir` / `Fermer` / `Basculer`. Un volume déclencheur le vise par son nom, et une plateforme
qui monte **porte le joueur** :

```
entity func_door 0 0 0  0 0  name=porte_hall axis=y distance=5 speed=2.5 width=3 height=4.5 wait=4
entity trigger_volume 0 0 6  0 0  size=6 target=porte_hall input=Ouvrir
```

Deux lignes dans un fichier texte, aucune ligne de C#. Exemple complet et commenté :
[`maps/portes.map`](maps/portes.map).

---

## Vérifier sans yeux humains

C'est la partie du dépôt qui a le plus servi. Le moteur se pilote **en mode headless** : il
exécute des commandes de console, prend une capture à un instant donné, et se ferme.

```bash
dotnet run --project src/Exarion.Game -c Release -- --seconds 3 --shot-at 2 --screenshot rendu.png --exec "play" --exec "tp 0 1.2 19" --exec "look 0 0"
```

| Option | Effet |
|---|---|
| `--seconds N` | ferme au bout de N secondes |
| `--screenshot fichier` | écrit une capture PNG |
| `--shot-at N` | à quel instant la prendre (sinon 1 s — donc en pleine chute pour une pile de caisses) |
| `--exec "commande"` | joue une commande de console au démarrage, répétable |
| `--novsync` `--shadows 0\|1` `--tickrate N` | réglages de lancement |

Cette boucle a trouvé des défauts qu'aucun test unitaire ne voyait : un éclairage dix fois
trop faible, des ombres invérifiables sous un plafond fermé, un seuil de halo comparé sur la
mauvaise échelle (62 pixels modifiés au lieu de 1 626).

⚠️ **Deux pièges de mesure, tous deux payés cher.**

- **`dotnet run --no-build` exécute le binaire existant.** Enchaîné derrière un `dotnet build`,
  il peut lancer l'ancien exécutable — d'où plusieurs mesures qui « prouvaient » qu'une
  correction ne servait à rien. Pour comparer deux versions, utiliser `--no-incremental` et
  **jamais** `--no-build`.
- **Les images/seconde ne sont comparables que dos à dos dans la même session.** Le facteur
  atteint 4 selon que la fenêtre est visible ou non. Pour juger le coût de la physique il y a
  `perf 1`, qui donne le coût du pas en millisecondes.

---

## Ce qui manque

Dit franchement, parce que la liste compte autant que le reste.

- **Le tir n'est pas arbitré par le serveur.** Le rembobinage des hitbox est écrit et testé,
  mais le tir du bac à sable reste **local** : il ne passe pas par le réseau. C'est le dernier
  fil à tirer de la phase 3.
- **Le client ne charge pas la carte du serveur.** Il prédit dans *sa* carte ; si le serveur en
  fait tourner une autre, la prédiction est fausse (`net` affiche le nom pour qu'on le voie).
- **Le réseau n'a jamais tourné entre deux vraies machines** — loopback et réseau simulé
  seulement. Pas de delta-compression, pas de jeton de session.
- **Pas de jeu.** `RoundMachine`, l'économie et un arsenal de 7 armes existent et sont testés
  dans `Core`, mais rien n'est branché sur une partie jouable.
- **Pas de recherche de chemin.** Les PNJ marchent en ligne droite et se coincent sur tout
  obstacle de plus de 45 cm.
- **Une seule entité agit : `func_door`.** Le câblage sorties → entrées fonctionne de bout en
  bout, mais il n'y a encore rien d'autre à viser — pas d'entité qui allume une lumière, joue
  un son, compte, ou réveille un PNJ.
- **Une porte qui se referme sur le joueur le repousse**, elle ne l'écrase pas et ne rouvre
  pas. Pas de notion de blocage.
- **Pas de rotation dans le format de carte.** Les boîtes de décor sont alignées sur les axes ;
  seuls les corps dynamiques tournent. Une porte coulisse ou monte, elle ne pivote pas.
- **Pas de modèle 3D plaçable dans une carte.** L'import glTF existe, mais aucun type d'entité
  ne permet de poser un `.glb` dans un niveau.
- **Pas de script.** Les licences ont été tranchées pour du Lua, rien n'est installé.
- **Linux n'a jamais été essayé.** Windows et macOS oui.
- **Le ressenti n'est pas réglé.** Vitesse, recul, saut, inertie : ça ne se calcule pas, et
  personne n'a encore joué assez longtemps pour le dire. Les variables de console existent
  pour ça (`speed_run`, `jump_height`, `friction`, `sensitivity`, `fov`).

---

## Licence — libre, à condition de créditer

Exarion est distribué sous **[licence Apache 2.0](LICENSE)**. En clair :

- ✅ on peut l'utiliser, le **modifier**, le redistribuer et le **vendre**, y compris dans un
  jeu ou un moteur commercial, sans rien payer ni demander ;
- ⚠️ **le crédit est obligatoire** : toute copie redistribuée, modifiée ou non, en source ou en
  binaire, doit garder le fichier [`NOTICE`](NOTICE) (ou son texte dans la documentation, ou
  dans l'écran de crédits), ainsi que la licence ;
- ⚠️ un fichier modifié doit **indiquer qu'il a été modifié** ;
- le nom « Exarion » n'est pas cédé : on peut dire qu'un projet est basé sur Exarion, pas le
  présenter comme Exarion.

Le texte qui fait foi est [`LICENSE`](LICENSE) ; ce résumé n'a pas de valeur juridique.

## Dépendances

Cinq paquets, **tous MIT ou plus permissif**, usage commercial et distribution binaire
compris : `Silk.NET.Windowing`, `Silk.NET.Input`, `Silk.NET.OpenGL`, `Silk.NET.Maths`,
`Hexa.NET.MiniAudio` (et `miniaudio` natif, Unlicense/MIT-0). NUnit sert au développement
seulement.

Rien n'entre ici sans passer par [`DEPENDENCIES.md`](DEPENDENCIES.md) d'abord : licence
vérifiée sur le dépôt officiel, jamais sur un blog. **Toute licence copyleft (GPL, LGPL,
AGPL) est refusée** — le but est un moteur redistribuable en binaire. ImageSharp a été
écartée pour ça, d'où le décodeur PNG maison ; OpenAL Soft aussi, d'où miniaudio.

Aucun code n'est repris d'un moteur existant. Tout est implémenté à partir de techniques
publiques et documentées, et commenté en français.

---

## Où lire la suite

| Fichier | Contenu |
|---|---|
| [`DESIGN.md`](DESIGN.md) | les décisions d'architecture et pourquoi |
| [`ROADMAP.md`](ROADMAP.md) | ce qui est fait, ce qui vient |
| [`DEPENDENCIES.md`](DEPENDENCIES.md) | chaque dépendance, sa licence, sa justification |
| [`CLAUDE.md`](CLAUDE.md) | le carnet de bord : chaque piège rencontré, mesuré, et comment il a été attrapé |

`CLAUDE.md` est long et ce n'est pas un tutoriel — c'est la mémoire du projet, écrite pour ne
jamais redécouvrir deux fois le même piège. Si un comportement du moteur surprend, la réponse
y est probablement, avec la mesure qui va avec.
