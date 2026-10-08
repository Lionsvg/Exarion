# Exarion Engine — plan de route

> Document de pilotage. `DESIGN.md` dit *ce qu'on construit et pourquoi*,
> celui-ci dit *dans quel ordre et à quel coût*.
>
> Dernière mise à jour : 16/08/2026

---

## Cadrage — décidé le 16/08, renforcé le même jour

**LE LIVRABLE EST LE MOTEUR 3D. Pas le jeu.**

Première décision : on ne traite ni les **assets**, ni l'**anti-triche**, ni le **backend**
(comptes, boutique, matchmaking), ni l'**hébergement**, ni l'**UI**. Chacun pèse l'équivalent
d'une phase entière et **aucun n'est chiffré ici**.

Seconde décision, prise dans la foulée : **la phase 5 est supprimée**. Round, économie, bombe,
map jouable, playtest 5v5 — plus au programme. Ce qui reste : **réseau** (phase 3) et
**outillage** (phase 4).

⚠️ **Ce que ça change, et qu'il ne faut pas perdre de vue** : le projet n'a plus de juge
extérieur. « Des gens veulent refaire une partie » était le seul critère qui ne dépendait pas
de nous. Désormais **le bac à sable est le livrable**, et la validation passe entièrement par
des mesures — et par un humain qui regarde et écoute.

⚠️ **Les règles de licence restent entières.** Un moteur commercialisable ne peut pas plus
embarquer du copyleft qu'un jeu : c'est la règle n°2 qui a écarté ImageSharp, puis OpenAL Soft.

> ⚠️ **La liste de ce qui reste À TESTER est en tête de `CLAUDE.md`.** Ce document dit ce qu'il
> reste à *construire* ; celui-là dit ce qui est construit mais **jamais validé par un humain**.
> Les deux se lisent avant de reprendre le travail.

## Où on en est, vérifié

| | |
|---|---|
| Tests automatisés | **585 au vert**, ~2 s |
| Clients du moteur | **2** — le bac à sable et `samples/Exarion.Sample` (187 lignes, sans `Exarion.Game`) |
| Compilation | 0 erreur dans `src/` (26 avertissements d'analyseurs NUnit dans les tests) |
| Rendu | **0,557 ms de GPU par image** (1280×720), soit 3,3 % d'un budget 60 img/s. Détail par passe : `gpu 1` |
| Physique | **0,17 ms/pas** au repos, 3,5 ms au pire — 1 à 22 % du budget |
| Dépendances | 11 — **10 MIT, 1 Unlicense/MIT-0** (miniaudio) |
| Machine cible | RTX 5060 Ti, OpenGL 3.3 |

**Acquis** : boucle à pas fixe déterministe · collision et déplacement façon Quake · lancer de
rayon et zones de dégâts · **corps rigides en rotation complète** (OBB maison, R1→R7) ·
éclairage PBR · ombres · textures et PNG maison · ciel, MSAA, ACES · texte à l'écran ·
console de debug · captures d'écran · **profileurs physique ET GPU** · **modèles 3D animés avec mélange de poses** · SSAO · bloom · **particules** · sauvegarde des réglages ·
**pipeline de rendu réutilisable + jeu d'exemple + README**.

**Ce qui n'existe pas** : le tir n'est pas encore arbitré par le serveur. **Les phases 1, 2 et 3 sont terminées.**

✅ **Le moteur est utilisable de l'extérieur depuis le 17/08.** `RenderPipeline` possède l'ordre
des passes ; un jeu fournit trois méthodes. Prouvé par un second client qui ne référence jamais
`Exarion.Game` — c'est lui qui a fait remonter `Screenshot`, `MapLighting` et
`FixedTimestep.RenderTime`. Migration neutre à l'image : **0 pixel** différent sur 921 600.

✅ **Les entités AGISSENT depuis le 17/08 (E17).** `func_door` — porte, herse, ascenseur, pont —
déclarée entièrement en données, et le câblage `trigger_volume.OnEnter → porte.Ouvrir` arrive
enfin quelque part. Une plateforme qui monte **porte le joueur**. Vérifié dans le vrai jeu :
enchaînement complet en console, 51 759 pixels changés à caméra identique, HUD mesurable
(joueur monté de `Y 0,40` à `Y 4,00` sur une course déclarée de 3,6 m).

⚠️ **Utilisable ne veut pas dire fini.** Les manques sont listés franchement dans le
[`README.md`](README.md) : **peu d'entités qui agissent** (portes et logique ; la
recherche de chemin et la logique sont arrivées le 08/10, E16 et E18), pas de rotation dans le format, pas de modèle plaçable, pas de script, Linux non testé,
ressenti non réglé.

---

## Principe directeur

> **Chaque étape doit produire quelque chose de visible ou de mesurable.**
> Aucun module écrit « pour plus tard ».

⚠️ **Corollaire devenu caduc le 16/08.** La logique de jeu (round, économie, achat) est écrite et testée dans `Core` depuis le premier jour, mais la phase 5 étant supprimée, elle **ne sera jamais branchée**. Elle reste comme témoin : c'est elle qui a survécu aux trois changements de moteur, et c'est la preuve de la règle n°1.

---

## Phase 1 — Rendu présentable

*Objectif : que le moteur cesse de ressembler à un prototype de 1996.*

| Étape | Contenu | Effort | Risque |
|---|---|---|---|
| ~~**1.1 Éclairage**~~ | ✅ **15/08** — 3 types de lumières, atténuation fenêtrée, spéculaire Blinn-Phong, espace linéaire, compression des hautes lumières. **Mesuré : 715 img/s avec 12 lumières.** | fait | — |
| ~~**1.2 Ombres**~~ | ✅ **15/08** — shadow mapping directionnel, PCF, biais dépendant de l'inclinaison, culling des faces avant. **Coût mesuré : ~8 %.** ⚠️ **rendu non validé visuellement** | fait | — |
| ~~**1.3 Textures**~~ | ✅ **15/08** — décodeur/encodeur PNG **maison** (zéro dépendance), textures procédurales, projection planaire monde, mipmaps + anisotrope. **Plus : capture d'écran PNG** | fait | — |
| ~~**1.4 Matériaux**~~ | ✅ **15/08** — PBR Cook-Torrance : GGX + Smith + Fresnel, métallique/rugosité, conservation de l'énergie. **Validé par capture** | fait | — |
| ~~**1.5 Modèles**~~ | ✅ **16/08** — import glTF **maison**, en 6 étapes (M1→M6) : conteneur GLB, géométrie, matériaux et textures, squelette, animations, skinning GPU. **Aucune dépendance ajoutée.** Vérifié par capture : le personnage court | fait | — |
| ~~**1.6 Post-traitement**~~ | ✅ **15/08** — MSAA ×4, ciel dégradé, tone mapping ACES, ambiante hémisphérique. **~2 750 img/s** | fait | — |
| ~~**1.7 Occlusion ambiante (SSAO)**~~ | ✅ **16/08** — hémisphérique, bruit 4×4 + flou de même taille, appliquée à la seule ambiante. Mesuré : 1,2 % des pixels en éclairage normal, 2,1 % quand l'ambiante domine. ✅ **Coût enfin mesuré le 16/08 : 0,226 ms, soit 41 % du temps GPU** | fait | — |
| ~~**1.8 Bloom**~~ | ✅ **16/08** — pipeline **HDR** complet (tampon flottant, MSAA résolu, tone mapping en passe finale unique) puis extraction à seuil adouci et flou gaussien séparable en demi-résolution. ⚠️ Discret faute de **surfaces émissives** dans la scène — c'est du contenu, pas du moteur | fait | — |

**Piège connu** : le shadow mapping est le sujet le plus ingrat du rendu temps réel. Acné,
ombres décollées, cascades mal réglées — et **c'est purement visuel, donc je ne peux pas le
déboguer sans toi.** Prévoir des allers-retours.

## Phase 2 — Le monde prend vie

| Étape | Contenu | Effort | Risque |
|---|---|---|---|
| ~~**2.1 Son 3D**~~ | ✅ **16/08** — miniaudio en bas niveau, mélangeur, spatialisation et occlusion **maison**. ⚠️ Avant/arrière indistinguables : limite du panoramique d'amplitude, il faudrait une HRTF | fait | — |
| ~~**2.2 Animation**~~ | ✅ **16/08** — squelette et skinning GPU avec M4→M6, **mélange de poses** avec `PoseBlend` + `Animator`. Le piège était temporel : deux cycles de durées différentes se désynchronisent si on les échantillonne au temps absolu | fait | — |
| ~~**2.3 Particules**~~ | ✅ **16/08** — `ParticleSystem` dans `Core` (pas fixe, déterministe), billboards instanciés dans le tampon HDR. Le piège était la couleur : les particules sont en **linéaire**, un « gris 0,15 » ressort à 0,59 à l'écran | fait | — |
| ~~**2.4 Config**~~ | ✅ **16/08** — `save` / `load`, chargement automatique au démarrage. Seules les valeurs **modifiées** sont écrites, sinon les défauts seraient figés | fait | — |

**Le son est sous-estimé.** Dans un FPS tactique, entendre d'où vient un pas est une
mécanique de jeu, pas de l'habillage. À traiter comme tel.

### 2.1 découpé — décidé le 16/08

**Dépendance tranchée : miniaudio** (Unlicense / MIT-0), via `Hexa.NET.MiniAudio` (MIT).
OpenAL Soft écartée : **LGPL v2 vérifiée sur son dépôt**. Voir `DEPENDENCIES.md`.

⚠️ **On n'utilise QUE le bas niveau de miniaudio** — « remplis ce tampon ». Le rôle exact que
Silk.NET joue pour la fenêtre et OpenGL. Mélange, atténuation, panoramique et occlusion sont
écrits dans `Exarion.Core`, testables en ligne de commande comme le reste.

| # | Étape | État |
|---|---|---|
| **S1** | Sortie audio : périphérique ouvert, un son sort | ✅ **16/08** — mesuré sur le WAV |
| **S2** | Mélangeur maison : N voix, WAV maison pour la vérification | ✅ **16/08** — 16 tests, 4 mutations |
| **S3** | Spatialisation : atténuation par distance, panoramique, Doppler | ✅ **16/08** — 0,2498 mesuré pour 0,2500 prédit |
| **S4** | Occlusion simple : filtre passe-bas derrière un mur | ✅ **16/08** — 20,9× sur un 6 kHz |

⚠️⚠️ **JE NE PEUX PAS ENTENDRE.** C'est la première fois qu'une étape échappe complètement à
mes moyens de vérification. La parade : le WAV est au son ce que la capture d'écran est à
l'image. Un fichier se mesure — amplitude, équilibre gauche/droite selon la position de la
source, décroissance avec la distance. Le **ressenti**, lui, ne se mesurera jamais : il faudra
que tu écoutes.

## Phase 3 — Le réseau

*La partie la plus difficile du projet. Elle mérite son propre découpage.*

| Étape | Contenu | Effort | Risque |
|---|---|---|---|
| ~~**3.1 Transport**~~ | ✅ **17/08** — UDP maison, sérialisation binaire, réseau simulé déterministe pour les tests | fait | — |
| ~~**3.2 Connexion**~~ | ✅ **17/08** — serveur autoritaire, poignée de main réessayée, entrées en redondance. Vérifié entre **deux processus** | fait | — |
| **3.3 Instantanés** | ✅ réplication d'état — ❌ **delta-compression PAS faite** (état complet à chaque instantané) | partiel | — |
| ~~**3.4 Interpolation**~~ | ✅ **17/08** — horloge de lecture sur le pas serveur, appariement par identifiant | fait | — |
| ~~**3.5 Prédiction**~~ | ✅ **17/08** — le client simule immédiatement, avec le MÊME CharacterMotor que le serveur | fait | — |
| ~~**3.6 Réconciliation**~~ | ✅ **17/08** — rejeu depuis l'état serveur, correction absorbée par l'affichage. **1 correction sur 1 024 pas**, pire écart 2,2 cm | fait | — |
| **3.7 Lag compensation** | ✅ rembobinage écrit et testé dans Core — ⚠️ **le tir du jeu ne passe pas encore par le serveur** | partiel | — |

**Ce qui joue en notre faveur** : le pas fixe est déterministe et testé, le générateur
aléatoire est reproductible par graine, les entrées sont déjà des structures, et la
simulation est isolée du rendu. Ces quatre choses ont été faites *exprès* pour cette phase.

**Ce qui joue contre** : c'est le domaine où une erreur ne se voit pas tout de suite. Un bug
de réconciliation se manifeste comme « ça tremble parfois » trois semaines plus tard.
**Chaque étape doit être testée automatiquement avant la suivante**, sans exception.

## Phase 4 — Outillage

| Étape | Contenu | Effort |
|---|---|---|
| **4.1 Éditeur de niveaux** | ⚠️ **première étape faite le 17/08** — voir le découpage ci-dessous | reste 12–25 j |
| ~~**4.2 Format de map**~~ | ✅ **16/08** — format texte `.map`, chargement à chaud, 16 tests. Le décor n'est plus écrit en dur : `BuildLevel()` est devenu `BuildSandboxMap()` et rend une `MapData` comme n'importe quel fichier | fait |
| ~~**4.3 Profileur**~~ | ✅ **16/08** — physique (`perf 1`) **et rendu** (`gpu 1`, horodatages OpenGL). Le coût de la SSAO, ouvert depuis 1.7, est enfin mesuré : **0,226 ms, 41 % du temps GPU** | fait |

**Alternative sérieuse à 4.1** : exporter depuis Blender. 3 jours au lieu de 15, au prix de
dépendre d'un outil externe. À trancher le moment venu.

### 4.1 découpé — première étape livrée le 17/08

**La demande était « un éditeur à la Half-Life, comme Unreal ». Ce n'est pas ce qui est livré,
et il faut le dire clairement** : Hammer et l'éditeur d'Unreal représentent des décennies-homme.
Ce qui est fait, c'est le **cœur** de Hammer — la boucle sélectionner / déplacer / redimensionner
sur une grille / annuler / sauvegarder — plus un menu et des états d'application propres.

| # | Étape | État |
|---|---|---|
| **E1** | Menu et états : Jouer / Éditeur / Choisir une carte / Quitter | ✅ **17/08** — capture |
| **E2** | Vol libre, sélection au rayon, grille au sol | ✅ **17/08** — capture |
| **E3** | Déplacer, redimensionner, créer, dupliquer, supprimer, accrochage | ✅ **17/08** — 20 tests |
| **E4** | Annuler / rétablir groupés par geste | ✅ **17/08** — 5 mutations |
| **E5** | Enregistrement, et retour en partie sur la carte éditée | ✅ **17/08** — mesuré sur le fichier |
| **E11** | **Entités** dans le format : type + propriétés clé/valeur | ✅ **17/08** — 14 tests |
| **E12** | Points d'apparition multiples honorés par le moteur | ✅ **17/08** — mesuré |
| **E13** | Runtime : checkpoints, déclencheurs, événements | ✅ **17/08** — 18 tests, 4 mutations. ⚠️ Les sorties sont émises, **aucune ENTRÉE n'agit encore** |
| **E14** | PNJ : perception, ronde, poursuite, retour au poste | ✅ **17/08** — 22 tests, 3 mutations. Le calcul de chemin est venu avec E16 |
| **E16** | **Calcul de chemin** : grille 2,5D, A*, composantes fortement connexes | ✅ **08/10** — 22 tests, 9 mutations, `maps/chemins.map`. ⚠️ la grille ne voit que le décor (ni portes, ni caisses) |
| **E17** | Entrées côté cible : une porte qui s'ouvre vraiment | ✅ **17/08** — `func_door`, voir plus haut |
| **E18** | Entités **logiques** : relais, minuterie, compteur ; fils de câblage dans l'éditeur | ✅ **08/10** — 16 tests, 6 mutations, `maps/logique.map`. ⚠️ F10–F12 non vérifiées au clavier |
| **E19** | **Ambiance** comme cibles : lumière et son commutables ; PNJ qu'on réveille | ✅ **08/10** — 18 tests, 4 mutations ; capture, WAV (440,0 Hz), HUD. ⚠️ pas de touche d'éditeur dédiée (`ed_entity env_light`) |
| **E15** | Script Lua | ⏳ **licences tranchées le 17/08 : LuaCSharp, MIT.** Rien d'installé |
| **E6** | Édition des **lumières** | ⏳ la carte les porte, l'éditeur ne les touche pas |
| **E7** | Manipulateurs à la souris (gizmos), glisser une face | ⏳ tout passe par le clavier |
| **E8** | Sélection **multiple**, copier/coller, groupes | ⏳ |
| **E9** | Textures : choisir, aligner, décaler | ⏳ le décor n'a qu'une couleur par boîte |
| **E10** | Géométrie non alignée : rotation, brosses non rectangulaires | ⏳ **le format ne sait décrire que des boîtes alignées** |

⚠️ **E10 est la limite structurelle, et elle est en amont de l'éditeur.** `MapBox` porte un
centre, une taille et une couleur — pas de rotation. Aucun éditeur ne fera tourner un mur tant
que le format ne saura pas l'écrire. C'est un chantier de format, pas d'interface.

⚠️ **Ce qui n'est PAS dans ce découpage et ne peut pas y être** : l'animation d'entités, les
prefabs, la compilation de visibilité, les surfaces déformables. Ce sont des sujets à part
entière, chacun de l'ordre d'une phase.

### Scripting — la décision qui reste (E15)

**Half-Life ne script PAS ses objets en Lua.** Les entités de Source sont des classes C++ avec
des propriétés clé/valeur et un système de **sorties → entrées** que Hammer câble à la souris :
`OnTrigger → porte, Ouvrir`. Le Lua vient d'ailleurs — Garry's Mod *par-dessus* Source, Roblox,
LÖVE. Ce modèle-là donne checkpoints, déclencheurs et événements **sans aucune dépendance**,
et c'est ce que E13 propose de faire.

Trois voies, à trancher **avant** E13 si on veut du script :

| Voie | Coût | Licence |
|---|---|---|
| **Sorties → entrées, façon Source** | ~3–5 j, zéro dépendance | — |
| **Lua** (MoonSharp, ou NLua + Lua natif) | ~4–6 j + liaison | à vérifier, règle n°2 |
| **C# à chaud** (Roslyn scripting) | ~3–5 j | MIT, mais ~10 Mo et compilation à l'exécution |

⚠️ **Recommandation** : commencer par les sorties → entrées. C'est ce qui rend les entités
vivantes, ça se teste en ligne de commande, et ça n'engage rien. Le script se greffe dessus
ensuite si le besoin se confirme.

## ~~Phase 5 — Le jeu~~ — SUPPRIMÉE le 16/08

**Décision : on fait le MOTEUR 3D, pas le jeu.** Round, économie, bombe, map jouable et
playtest 5v5 sortent du périmètre.

Ce n'est pas un abandon des règles qui en découlaient. En particulier, **la règle n°2 sur les
licences reste entière** : un moteur destiné à être commercialisable ne peut pas plus embarquer
du copyleft qu'un jeu. C'est elle qui a écarté ImageSharp, puis OpenAL Soft.

⚠️ **Conséquence directe** : `RoundMachine`, `Economy`, `BuyCatalog` et leurs tests deviennent
du **code orphelin** — écrit, testé, et désormais sans destination. À trancher (garder comme
démonstration, ou supprimer).

⚠️ **Ce qui reste utilisé par le moteur**, et ne bouge pas : `Arsenal` et `Ballistics` (le bac à
sable tire), et `CombatTarget.RayVsAabb` dont `PhysicsWorld.Raycast` se sert.

⚠️ **Ce qui disparaît aussi** : la preuve finale. Le critère « des gens veulent refaire une
partie » était le seul juge extérieur du projet. Sans lui, **le bac à sable devient le livrable**,
et c'est à l'œil et à l'oreille qu'on jugera — donc au tien.

---

## Estimation globale, sans enrobage

| Phase | Optimiste | Réaliste (×2) |
|---|---|---|
| ~~1 — Rendu~~ | ✅ fait | — |
| ~~2 — Monde~~ | ✅ fait | — |
| 3 — Réseau | 26–38 j | ~64 j |
| 4 — Outillage | 10–18 j | ~28 j |
| ~~5 — Jeu~~ | supprimée | — |
| **Reste à faire** | **36–56 j** | **~92 jours-homme** |

À **15 h/semaine**, environ **2 jours-homme par semaine** : **~46 semaines, soit près d'un an.**

*Pour mémoire, le plan initial pesait ~200 jours-homme sur deux ans. Les phases 1 et 2 sont
faites, la 5 est supprimée : il reste **moins de la moitié**.*

Ce chiffre n'est pas là pour décourager. Il est là pour que la décision soit prise les yeux
ouverts, et pour qu'on mesure l'avancement contre quelque chose de réel.

**Les estimations logicielles se trompent d'un facteur 2 en moyenne, la mienne comprise.**

---

## Où mon aide vaut, et où elle ne vaut rien

**Forte** — code algorithmique bien spécifié :
- protocole réseau, prédiction, rembobinage
- parsers, formats de fichiers, sérialisation
- mathématiques, structures de données
- **tests automatisés** — c'est là que j'ai le plus d'impact

**Faible** — tout ce qui se juge à l'œil ou à la main :
- bugs de rendu. Longtemps **je ne voyais pas l'écran**, et le bug d'étirement l'a prouvé.
  ⚡ **Ça a changé le 15/08** : la commande `screenshot` écrit un PNG que je peux relire.
  J'ai immédiatement trouvé deux défauts invisibles autrement — l'éclairage 10× trop faible,
  et des ombres invérifiables sous un plafond fermé. **À utiliser systématiquement
  désormais** : `--screenshot fichier.png --exec "commande"`.
- le *feeling*. Vitesse, recul, saut, inertie : entièrement toi. La console existe pour ça.
- les performances réelles. Tu lances, je lis des chiffres.

**Conséquence pratique** : plus le travail est visuel, plus je dois écrire de tests et de
diagnostics pour compenser. C'est pour ça que la caméra a 14 tests et que le moteur affiche
ses compteurs à chaque seconde.

---

## Points de décision à venir

| Quand | Question | Enjeu |
|---|---|---|
| ~~Phase 1.5~~ | ✅ **Tranché le 16/08 : import glTF maison.** L'inspection du vrai fichier a décidé — glTF 2.0 standard, 1 maillage, 1 peau. Le conteneur tient en une page, et `System.Text.Json` est dans le runtime. **Une journée, zéro dépendance.** |
| ~~Phase 2.2~~ | ✅ **Tranché le 16\08 : animation maison.** Import glTF, squelette, skinning GPU et mélange de poses tiennent en trois fichiers de `Core`, testables en ligne de commande. Zéro dépendance. |
| Phase 3.1 | UDP maison ou GameNetworkingSockets (Valve, BSD) ? | fiabilité contre contrôle |
| Phase 4.1 | Éditeur maison ou export Blender ? | 15 j contre 3 j |
| ~~Rotation des corps~~ | ✅ **Tranché le 15/08 : OBB maison.** Cohérent avec la règle de propriété. R1 (SAT), R2 (points de contact), R3 (inertie) et R4 (solveur) faits. |

**Règle** : toute nouvelle dépendance passe par `DEPENDENCIES.md` *avant* d'être installée,
et si elle est copyleft on s'arrête pour en discuter.

---

## Chantier en cours — rotation des corps rigides (OBB maison)

Décidé le 15/08/2026 contre l'intégration de Jolt. Découpé en étapes testables :

| # | Étape | État |
|---|---|---|
| **R1** | `Obb` + théorème des axes séparateurs (15 axes), normale et profondeur | ✅ **15/08** — 17 tests |
| **R2** | Points de contact (clipping de Sutherland-Hodgman face contre face) | ✅ **15/08** — 17 tests |
| **R3** | Orientation + vitesse angulaire + tenseur d'inertie sur `RigidBody` | ✅ **15/08** — 25 tests |
| **R4** | Impulsions avec bras de levier : c'est là que les caisses basculent | ✅ **15/08** — 11 tests |
| **R5** | Frottement angulaire, amortissement, sommeil | ✅ **16/08** — 4 tests, **partiel** |
| **R6** | Rendu : passer la matrice de rotation aux caisses | ✅ **16/08** — vérifié par capture |
| **R7** | OBB contre décor statique, frottement de Coulomb | ✅ **16/08** — contact spéculatif |

**Le chantier rotation est terminé.** Décidé le 15/08 contre l'intégration de Jolt, livré en
sept étapes et deux jours. Détail des pièges et des mesures dans `CLAUDE.md`.

Deux choses méritent d'être retenues du découpage lui-même :

- **R7 a dû être partiellement avancé dans R4**, et ce n'était pas un choix. Le découpage
  supposait que le décor pouvait rester hors du solveur jusqu'à la fin ; c'est faux, la chaîne
  de contacts ne peut pas être coupée en deux. Mesure qui a tranché : caisse du bas rendue
  **statique** → tout redevenait stable.
- **R7 a échoué une première fois pendant R5, pour une raison qui ne le concernait pas.** Deux
  bugs le sabotaient — le corps endormi traité comme un puits de vitesse, et la règle de
  réveil — corrigés seulement après. Rejoué ensuite, il est passé du premier coup.
  **Une correction qui échoue sur une base cassée ne prouve rien contre elle.**

**Garde-fou** : chaque étape doit garder les 269 tests au vert. En particulier
`Une_Pile_Reste_Stable_Et_Ne_Tremble_Pas` et `Beaucoup_De_Caisses_Restent_Stables`, les
détecteurs d'instabilité, et leur miroir dans `RotationTests` :
`Une_Caisse_En_Porte_A_Faux_Bascule_Et_Tombe` — car un solveur sans bras de levier fait tenir
une caisse posée sur un centimètre carré de table, et **aucun ancien test ne le voyait**.

## Prochaine étape immédiate

Deux chantiers sont clos — la rotation des corps (R1→R7) et l'import de modèles (M1→M6).
**Il ne reste que SSAO et bloom pour finir la phase 1.**

### Dette laissée derrière, par ordre de gravité

1. ⚠️ **La passe d'ombre ne fait pas le skinning.** Elle a son propre shader : l'ombre du
   personnage reste en pose de liaison pendant qu'il court. **Défaut introduit à M6**, ~0,5 j.
2. ⚠️ **À valider à la main** : la suppression de l'amortissement forfaitaire au sol change le
   ressenti. Caisse lancée à 8 m/s : **0,99 m avant, 3,17 m maintenant** (la physique dit 3,9).
   Réglable en direct avec `friction`.
3. **Deux freinages redondants au sol** cohabitent encore — l'amortissement de R5 et le
   frottement de Coulomb de R7.
4. **Le rendu n'est pas profilé.** Seule la physique l'est. On sait qu'elle coûte 1 % du
   budget ; on ne sait pas où passent les 99 autres.

✅ **Matrice des normales corrigée le 16/08**, et **revérifiée à M3** sur le premier vrai
modèle : l'éclairage du personnage est juste.

### Puis la phase 1

**1.7 SSAO** puis **1.8 bloom**. La SSAO est la plus utile des deux : elle assombrit les
recoins et les points de contact, ce qui *ancre* les objets au sol. Sans elle, une caisse
posée semble toujours flotter un peu.

**Inutile d'optimiser le solveur** (profilage du 16/08) : 1 % du budget au repos, 22 % au pire.
Le vrai sujet de performance reste le réseau, phase 3.
