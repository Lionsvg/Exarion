# Dépendances d'Exarion

> **Règle du projet** : rien n'entre ici sans que sa licence soit vérifiée et
> compatible avec une distribution commerciale (jeu gratuit + vente de cosmétiques).
>
> Aucun code n'est copié d'un moteur existant. Les techniques employées (pas fixe,
> prédiction client, rembobinage de hitbox, modèle de déplacement Quake) sont
> publiques et documentées depuis des décennies ; les implémentations sont écrites ici.

## Dépendances actuelles

| Paquet | Version | Licence | Rôle |
|---|---|---|---|
| `Silk.NET.Windowing` | 2.23.0 | MIT | Création de fenêtre, boucle |
| `Silk.NET.Input` | 2.23.0 | MIT | Clavier, souris, manettes |
| `Silk.NET.OpenGL` | 2.23.0 | MIT | Accès GPU |
| `Silk.NET.Maths` | transitif | MIT | Types vectoriels de l'API fenêtre |
| `Hexa.NET.MiniAudio` | 1.0.1 | MIT | Liaison C# de miniaudio |
| `miniaudio` (natif, embarqué) | via 1.0.1 | **Unlicense / MIT-0** | Sortie audio, toutes plateformes |
| `HexaGen.Runtime` | 1.1.24 | MIT | Transitif de la liaison |
| `NUnit` | 4.2.2 | MIT | Tests (développement uniquement) |
| `NUnit3TestAdapter` | 4.6.0 | MIT | Exécution des tests |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | MIT | Infrastructure de test |
| `coverlet.collector` | 6.0.2 | MIT | Couverture de code |
| .NET / `System.Numerics` | 9.0 | MIT | Runtime, maths vectorielles |

**Toutes MIT.** Aucune contrainte de redistribution, aucune royaltie, aucune obligation
d'ouvrir le code du jeu.

## Candidates non encore intégrées

À évaluer au moment où le besoin se présente, licence à vérifier avant ajout :

| Besoin | Candidate | Licence annoncée |
|---|---|---|
| Physique | JoltPhysicsSharp | MIT (Jolt : MIT) |
| Transport réseau | GameNetworkingSockets | BSD 3-clauses |
| Modèles 3D | SharpGLTF | MIT |
| ~~Images PNG~~ | ~~ImageSharp~~ | ❌ **Six Labors Split — écartée**, pas librement commerciale |
| ~~Images PNG~~ | ~~StbImageSharp~~ | MIT, mais **inutile** : décodeur écrit maison sur `ZLibStream` |
| ~~Audio~~ | ~~OpenAL-Soft~~ | ❌ **LGPL v2 — écartée**, copyleft |
| ~~Audio~~ | ~~miniaudio~~ | ✅ **intégrée le 16/08** — voir le tableau du haut |
| Script Lua | **LuaCSharp** (`nuskey8/Lua-CSharp`) | ✅ **MIT vérifiée** — voir ci-dessous |
| ~~Script Lua~~ | ~~MoonSharp~~ | BSD 3-clauses, mais **abandonnée** — voir ci-dessous |
| ~~Script Lua~~ | ~~NLua + KeraLua~~ | MIT, mais **binaires natifs par plateforme** |
| UI d'outils | ImGui.NET | MIT |

### Script Lua — vérifié le 17/08/2026

Licences lues sur les dépôts officiels et sur nuget.org.

| Candidate | Licence | Verdict |
|---|---|---|
| **LuaCSharp** 0.5.6 | ✅ **MIT** | **retenue** |
| MoonSharp 2.0.0 | BSD 3-clauses (permissive) | écartée — dernière version NuGet **octobre 2016** |
| NLua | MIT | écartée — dépend de KeraLua + Lua natif |
| Lua (référence C) | MIT | non nécessaire : LuaCSharp ne l'utilise pas |

**Aucune n'est copyleft.** Les trois passeraient la règle n°2. Le choix se fait donc sur des
critères techniques, pas juridiques.

**Pourquoi LuaCSharp :**

- **Pure C#, aucun binaire natif.** C'est l'argument décisif après l'expérience de miniaudio :
  pas de dossier `runtimes/`, pas d'architecture à vérifier, ça tourne partout où .NET tourne.
- **Sans génération d'IL** — compatible AOT, donc compatible avec une éventuelle publication en
  natif plus tard.
- **Maintenue** : 962 commits, dernière version **29/07/2026**. MoonSharp n'a pas publié depuis
  près de dix ans.
- Lua **5.2**, .NET Standard 2.1 et .NET 6/8/10 → compatible .NET 9.

⚠️ **Trois réserves honnêtes, à garder en tête :**

1. **Version 0.5.6 — le paquet n'a pas atteint la 1.0.** L'API peut changer. À isoler derrière
   une façade maison, comme miniaudio l'est derrière `AudioDevice`.
2. **71 800 téléchargements au total, 1 064 pour cette version.** C'est peu. MoonSharp, elle, a
   dix ans d'usage réel derrière elle — abandonnée mais éprouvée. Le compromis est assumé.
3. **Ses trois dépendances transitives sont vérifiées** (17/08) :
   `LuaCSharp.Annotations` et `LuaCSharp.SourceGenerator` viennent du **même dépôt MIT** ;
   `Microsoft.Bcl.TimeProvider` est **MIT**, publiée par Microsoft. Cette dernière ne concerne
   que .NET Standard 2.1 et .NET 6 — **sans objet sur .NET 9**.

✅ **Rien ne bloque plus l'installation côté licences.** La chaîne complète est MIT.

⚠️ **Le script n'est PAS le premier chantier.** Les entités du 17/08 n'ont encore aucun
comportement. Un système de **sorties → entrées** façon Source les rend vivantes sans aucune
dépendance, et c'est ce qui se fait d'abord. Le Lua se greffe dessus : une sortie pourra appeler
un script au lieu d'une entrée intégrée.

### Audio — vérifié le 16/08/2026

Licences lues sur les dépôts officiels, conformément à l'étape 1 de la procédure.

**OpenAL-Soft — ÉCARTÉE.** `COPYING` du dépôt <https://github.com/kcat/openal-soft>
annonce « GNU LIBRARY GENERAL PUBLIC LICENSE, Version 2, June 1991 ». C'est du copyleft :
la règle n°2 s'applique, comme pour ImageSharp. Utilisable en liaison dynamique sous
conditions, mais on ne s'y engage pas alors qu'une alternative sans aucune contrainte existe.

**miniaudio — RETENUE comme candidate.** `LICENSE` de <https://github.com/mackron/miniaudio>
offre **deux licences au choix** : *Public Domain (Unlicense)* ou *MIT No Attribution*.
Usage commercial et redistribution binaire autorisés, et **l'attribution n'est même pas
exigée** — c'est plus permissif que le MIT du reste du projet.

**Elle règle le problème du multi-plateforme**, qui était l'objection décisive contre un
backend maison sur WASAPI. Un seul fichier source C, et des backends pour :

| Plateforme | Backends |
|---|---|
| Windows | WASAPI, DirectSound, WinMM |
| macOS, iOS | Core Audio |
| Linux | ALSA, PulseAudio, JACK |
| BSD | sndio, audio(4), OSS |
| Android | AAudio, OpenSL\|ES |
| Navigateur | Web Audio |

**Liaison retenue : `Hexa.NET.MiniAudio` 1.0.1, MIT**, liaison 1:1 avec l'API C.
Architectures **vérifiées en inspectant le paquet installé**, pas en lisant une page :

```
runtimes\win-x64      win-x86      win-arm64      → miniaudio.dll
runtimes\linux-x64    linux-arm64                 → libminiaudio.so
runtimes\osx-x64      osx-arm64                   → libminiaudio.dylib
runtimes\android-x64  android-arm64               → libminiaudio.so
```

Mac (Intel **et** Apple Silicon) et Linux sont couverts. C'était l'objection décisive.

⚠️ **Non vérifié** : la version exacte de miniaudio embarquée dans le paquet.

⚠️ **On n'utiliserait que le BAS NIVEAU**, c'est-à-dire « remplis ce tampon » — le rôle que
Silk.NET joue pour la fenêtre et OpenGL. La spatialisation, le mélange, l'atténuation et
l'occlusion restent écrits ici, conformément à la règle n°3. miniaudio propose aussi un moteur
3D complet et des décodeurs WAV/FLAC/MP3 : on s'en sert ou pas, c'est un choix à faire au
moment venu.

## Ce qui est écrit ici, et n'appartient qu'au projet

- `FixedTimestep` — boucle à pas fixe en nanosecondes entières
- `RoundMachine` — machine à états du round
- `Ballistics` / `Arsenal` — recul, dispersion, dégâts, patterns
- `Economy` — porte-monnaie, inventaire, validation d'achat
- `ShaderProgram`, `Mesh` — enveloppes OpenGL
- `EngineHost` — fenêtre et boucle

## Procédure avant d'ajouter une dépendance

1. Vérifier la licence sur le dépôt officiel, pas sur un article de blog
2. Vérifier qu'elle autorise l'usage commercial **et** la distribution binaire
3. L'ajouter à ce tableau **avant** de l'installer
4. Si la licence est copyleft (GPL, LGPL, AGPL) : s'arrêter et en discuter
