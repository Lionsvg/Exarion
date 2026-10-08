# ===========================================================================
#  Exarion - comparaison A/B de deux copies du depot, sans yeux humains
#
#    pwsh tools\ab-captures.ps1 -Reference <dossier>          reference contre copie de travail
#    pwsh tools\ab-captures.ps1 -Reference <dossier> -Bruit   reference contre ELLE-MEME
#
#  Une refonte qui ne change rien doit le PROUVER. Ce script lance les memes
#  scenarios headless sur deux copies du depot - une reference figee (par
#  exemple `git worktree add --detach <dossier> <commit>`) et la copie de
#  travail - puis compare trois choses :
#
#   - les CAPTURES, pixel par pixel, au seuil 6. Deux lancements du meme
#     binaire ne donnent pas deux images egales au bit pres (voir
#     diff-png.ps1) : compter les ecarts non nuls ne veut rien dire ;
#   - la CONSOLE : chaque variable avec sa valeur, son defaut, ses bornes et
#     sa description, et chaque commande. Une commande perdue ou un defaut
#     change en deplacant du code se voit ICI, et nulle part ailleurs - aucune
#     capture ne montre une commande qu'on n'appelle pas ;
#   - le JOURNAL de demarrage, sans les lignes de cadence (img/s, pas/s).
#
#  ⚠️ LE BRUIT SE MESURE AVANT DE CONCLURE. -Bruit lance la reference deux
#  fois et compare les deux : c'est l'ecart qu'il faut battre. Une scene
#  animee ou reseau ne sera jamais a zero, et un ecart du meme ordre que le
#  bruit ne prouve rien - ni dans un sens, ni dans l'autre.
#
#  CODE DE SORTIE : 2 des qu'un ecart existe - pixels, console ou journal -,
#  y compris le bruit des particules du scenario « effets » (quelques pixels
#  a peine au-dessus du seuil). Le code dit qu'il y a QUELQUE CHOSE a juger ;
#  chaque ecart se juge ensuite contre -Bruit, jamais au code seul.
#
#  ⚠️ Les deux copies doivent lire le MEME exarion.cfg : il est charge au
#  demarrage depuis le dossier de l'executable. Le script recopie celui de
#  la copie de travail dans la reference, sinon on comparerait deux reglages.
# ===========================================================================

#Requires -Version 7

param(
    [Parameter(Mandatory = $true)][string]$Reference,
    [string]$Courant = (Split-Path -Parent $PSScriptRoot),
    [string]$Sortie = (Join-Path ([System.IO.Path]::GetTempPath()) "exarion-ab"),
    [int]$Seuil = 6,
    [switch]$Bruit,
    [string[]]$Seulement
)

$ErrorActionPreference = "Stop"

function Etape([string]$texte) { Write-Host ""; Write-Host "== $texte" -ForegroundColor Cyan }
function Echec([string]$texte) { Write-Host "[ECHEC] $texte" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
#  Les scenarios
#
#  ⚠️ character_speed 0 partout ou un personnage est visible : l'horloge
#  d'animation depend de la fraction de pas a l'instant de la capture, qui
#  change a chaque lancement. Une pose libre donne a elle seule des centaines
#  de pixels d'ecart (mesure du 17/08 : 231). Figee, elle ne bouge plus.
#
#  --shot-at compte en temps de SIMULATION, --seconds en temps REEL : la
#  marge de quatre secondes couvre le chargement.
# ---------------------------------------------------------------------------
$scenarios = @(
    @{ Nom = "menu"; Jeu = "bac"; Shot = 4; Args = @("--exec", "character_speed 0") },
    @{ Nom = "partie"; Jeu = "bac"; Shot = 4; Args = @("--exec", "play", "--exec", "character_speed 0",
            "--exec", "tp 0 1 25", "--exec", "look 15 -8") },
    @{ Nom = "effets"; Jeu = "bac"; Shot = 1.2; Args = @("--exec", "play", "--exec", "character_speed 0",
            "--exec", "tp 0 1 25", "--exec", "look 0 -10",
            "--exec", "fx_explode 0 1 14 1", "--exec", "fx_smoke 3 0.8 16 20") },
    @{ Nom = "editeur"; Jeu = "bac"; Shot = 2; Args = @("--map", "formes", "--exec", "character_speed 0",
            "--exec", "editor", "--exec", "ed_ent_select 4") },
    @{ Nom = "pnj"; Jeu = "bac"; Shot = 3; Args = @("--map", "formes", "--exec", "play",
            "--exec", "character_speed 0") },
    @{ Nom = "portes"; Jeu = "bac"; Shot = 2.5; Args = @("--map", "portes", "--exec", "play",
            "--exec", "ent_send porte_hall Ouvrir") },
    @{ Nom = "reseau"; Jeu = "bac"; Shot = 3; Args = @("--exec", "play", "--exec", "character_speed 0",
            "--exec", "host 27099") },
    @{ Nom = "exemple"; Jeu = "exemple"; Shot = 1; Args = @() }
)

# « -Seulement partie,editeur » arrive en UNE chaine quand le script est
# lance par « pwsh -File » : on decoupe nous-memes.
if ($Seulement) {
    $voulus = @($Seulement | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() })
    $scenarios = @($scenarios | Where-Object { $voulus -contains $_.Nom })
}

# ---------------------------------------------------------------------------
#  Comparaison d'images, compilee : la boucle de diff-png.ps1 en PowerShell
#  prend plusieurs secondes par image, et on en compare huit a chaque etape.
#  Meme regle que lui : un pixel compte s'il s'ecarte de Seuil ou plus, en
#  somme des trois canaux.
# ---------------------------------------------------------------------------
Add-Type -AssemblyName System.Drawing

# Les versions recentes de System.Drawing.Common deleguent a des assemblages
# prives : sans eux la compilation refuse Bitmap (« IImage non reference »).
$references = @("System.Drawing.Common", "System.Drawing.Primitives")
foreach ($prive in "System.Private.Windows.GdiPlus", "System.Private.Windows.Core") {
    try { Add-Type -AssemblyName $prive; $references += $prive } catch { }
}

Add-Type -ReferencedAssemblies $references -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ExarionPngDiff
{
    // Rend { largeur, hauteur, differents, ecart max, non nuls, xmin, ymin,
    // xmax, ymax }, ou null si les tailles different. Le cadre entoure les
    // pixels au-dessus du seuil : il dit OU regarder, ce qu'un compte seul ne
    // dit pas - une caisse qui s'endort une image plus tot et un HUD qui
    // change ne se lisent pas de la meme facon.
    public static long[] Compare(string a, string b, int seuil)
    {
        using (var ia = new Bitmap(a))
        using (var ib = new Bitmap(b))
        {
            if (ia.Width != ib.Width || ia.Height != ib.Height)
                return null;

            int w = ia.Width, h = ia.Height;
            var rect = new Rectangle(0, 0, w, h);
            BitmapData da = ia.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = ib.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int n = w * h * 4;
            var ba = new byte[n];
            var bb = new byte[n];
            Marshal.Copy(da.Scan0, ba, 0, n);
            Marshal.Copy(db.Scan0, bb, 0, n);
            ia.UnlockBits(da);
            ib.UnlockBits(db);

            long differents = 0, max = 0, nonNuls = 0;
            long xmin = w, ymin = h, xmax = -1, ymax = -1;

            for (int i = 0; i < n; i += 4)
            {
                int d = Math.Abs(ba[i] - bb[i]) + Math.Abs(ba[i + 1] - bb[i + 1]) + Math.Abs(ba[i + 2] - bb[i + 2]);

                if (d > 0) nonNuls++;
                if (d >= seuil)
                {
                    differents++;
                    if (d > max) max = d;

                    long p = i / 4, x = p % w, y = p / w;
                    if (x < xmin) xmin = x;
                    if (x > xmax) xmax = x;
                    if (y < ymin) ymin = y;
                    if (y > ymax) ymax = y;
                }
            }

            return new long[] { w, h, differents, max, nonNuls, xmin, ymin, xmax, ymax };
        }
    }
}
"@

# ---------------------------------------------------------------------------
#  Compilation et lancement
# ---------------------------------------------------------------------------

# ⚠️ --no-incremental, et on lance le binaire QU'ON VIENT DE COMPILER, dans
# le meme script, apres verification du code de sortie. C'est la seule facon
# de ne jamais comparer un binaire perime - le piege qui a fait conclure trois
# fois de travers (voir CLAUDE.md, --no-build).
function Compiler([string]$racine) {
    dotnet build (Join-Path $racine "Exarion.sln") -c Release --nologo -v quiet --no-incremental | Out-Null
    if ($LASTEXITCODE -ne 0) { Echec "La compilation echoue dans $racine" }
}

function Binaire([string]$racine, [string]$jeu) {
    if ($jeu -eq "exemple") { return Join-Path $racine "samples\Exarion.Sample\bin\Release\net9.0\Exarion.Sample.dll" }
    return Join-Path $racine "src\Exarion.Game\bin\Release\net9.0\Exarion.Game.dll"
}

# ⚠️ UN JEU QUI NE SE FERME PAS NE DOIT PAS BLOQUER LE SCRIPT. C'est arrive
# a la premiere version : « --seconds 5,2 » (voir Nombre) etait lu comme 0,
# donc sans limite, et le script a attendu dix minutes un processus qui ne
# finirait jamais. Au-dela de la limite, on tue et on le DIT.
function Lancer([string]$racine, [string]$jeu, [string[]]$arguments, [string]$journal, [int]$limite = 60) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
    $psi.ArgumentList.Add((Binaire $racine $jeu))
    foreach ($a in $arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $p = [System.Diagnostics.Process]::Start($psi)

    # Lecture asynchrone des deux flux : lire l'un puis l'autre peut bloquer
    # si le second remplit son tampon pendant qu'on attend le premier.
    $sortie = $p.StandardOutput.ReadToEndAsync()
    $erreurs = $p.StandardError.ReadToEndAsync()

    if (-not $p.WaitForExit($limite * 1000)) {
        $p.Kill($true)
        Write-Host "  [!] arrete au bout de $limite s : le jeu ne s'est pas ferme seul" -ForegroundColor Red
    }

    $p.WaitForExit()
    Set-Content -LiteralPath $journal -Value ($sortie.Result + $erreurs.Result) -Encoding utf8NoBOM
}

# ⚠️ LA CULTURE, ENCORE. « {0} » -f 5.2 donne « 5,2 » sur un poste francais,
# et le jeu lit ses nombres en culture invariante : 5,2 devenait 0. Meme
# piege que 2.4 et 4.2 (voir CLAUDE.md), paye une fois de plus ici.
function Nombre([double]$x) { $x.ToString([System.Globalization.CultureInfo]::InvariantCulture) }

# La derniere commande d'un lancement a-t-elle repondu ? Sinon la sortie a
# ete tronquee quelque part, et une comparaison « identique » ne vaudrait rien.
function Verifier-Echo([string]$journal, [string]$derniere) {
    $attendu = "exec > $derniere"
    if (-not (Get-Content $journal | Where-Object { $_.Trim() -eq $attendu })) {
        Write-Host "  [!] $([System.IO.Path]::GetFileName($journal)) TRONQUE : pas de reponse a « $derniere »" -ForegroundColor Red
        $script:tronques++
    }
}
$script:tronques = 0

# Lignes de cadence et de chemins : elles changent d'un lancement a l'autre
# ou d'une copie a l'autre sans rien dire du code.
function Normaliser([string]$fichier, [string]$racine) {
    if (-not (Test-Path $fichier)) { return @() }
    $echappe = [regex]::Escape($racine)
    # Les arguments et la capture portent le chemin du dossier de resultats,
    # qui differe forcement entre les deux cotes.
    Get-Content $fichier | Where-Object {
        $_ -notmatch '^\[\s*[\d,\.]+ s\]' -and $_ -notmatch 'img/s' -and
        $_ -notmatch '^\s*Capture' -and $_ -notmatch '^\s*Arguments :'
    } | ForEach-Object {
        # Le port du client est choisi par le systeme : il change a chaque fois.
        ($_ -replace $echappe, '<racine>') -replace 'depuis le port \d+', 'depuis le port <n>'
    }
}

function Executer([string]$racine, [string]$dossier) {
    New-Item -ItemType Directory -Force $dossier | Out-Null

    foreach ($s in $scenarios) {
        $png = Join-Path $dossier "$($s.Nom).png"
        $log = Join-Path $dossier "$($s.Nom).log"
        Remove-Item $png -ErrorAction SilentlyContinue

        if ($s.Jeu -eq "exemple") {
            $arguments = @("--seconds", "4", "--screenshot", $png)
        }
        else {
            $arguments = @("--seconds", (Nombre ($s.Shot + 4)), "--shot-at", (Nombre $s.Shot),
                "--screenshot", $png) + $s.Args
        }

        Lancer $racine $s.Jeu $arguments $log
        $etat = if (Test-Path $png) { "ok" } else { "CAPTURE MANQUANTE" }
        Write-Host ("  {0,-9} {1}" -f $s.Nom, $etat)
    }

    # --- Inventaire de la console -----------------------------------------
    # Deux passes : la premiere donne les noms, la seconde demande l'aide de
    # chacun - c'est l'aide qui porte le defaut, les bornes et la description.
    $liste = Join-Path $dossier "console-liste.log"
    Lancer $racine "bac" @("--seconds", "3", "--exec", "list") $liste

    $noms = Get-Content $liste | ForEach-Object {
        if ($_ -match '^\s*exec\s+(\S+) (=|\(\))') { $Matches[1] }
    }

    # ⚠️ PAR LOTS DE 40. La console ne garde que 200 lignes, et l'echo des
    # --exec ne voyait plus rien une fois ce tampon plein : la premiere version
    # de ce script ne comparait en realite que 66 entrees sur 125, en
    # affichant « identique ». Chaque lot reste loin de la limite, et on
    # VERIFIE que la derniere commande de chaque lot a bien repondu.
    $fichierAide = Join-Path $dossier "console-aide.log"
    Set-Content -LiteralPath $fichierAide -Value @() -Encoding utf8NoBOM

    for ($debut = 0; $debut -lt $noms.Count; $debut += 40) {
        $lot = $noms[$debut..([Math]::Min($noms.Count, $debut + 40) - 1)]
        $aide = @("--seconds", "3")
        foreach ($nom in $lot) { $aide += @("--exec", "help $nom") }

        $morceau = Join-Path $dossier "console-aide-$debut.log"
        Lancer $racine "bac" $aide $morceau
        Verifier-Echo $morceau "help $($lot[-1])"
        Get-Content $morceau | Add-Content -LiteralPath $fichierAide
        Remove-Item $morceau
    }

    # --- Une session scriptee ----------------------------------------------
    # Les captures ne passent que par une poignee de commandes. Celle-ci en
    # joue une soixantaine, dans un ordre fixe et toutes a t=0 - donc sans
    # pas de simulation entre deux : la sortie est deterministe et se compare
    # ligne a ligne. Rien n'y ecrit sur le disque (ni save, ni map_save, ni
    # ed_save, ni screenshot). Ni « map_list » ni « snd » : le premier depend
    # des fichiers non suivis du dossier maps/, le second du nombre de tampons
    # deja remplis par la carte son - ni l'un ni l'autre ne dit rien du code.
    $session = @(
        "map", "npc", "movers", "ent", "ent_targets", "crates", "fx",
        "spawn 3", "crates", "stack 4", "boom", "crates", "tp 1 2 3", "look 10 -5",
        "walk 1", "walk 0", "editor", "ed_select 3", "ed_shape", "ed_shape rampe", "ed_axis y",
        "ed_move 2", "ed_size 1", "ed_undo", "ed_redo", "ed_entity", "ed_entity npc_dummy 1 0 1",
        "ed_ent_select 0", "ed_prop name testeur", "ed_prop", "ed_list", "ed_grid 0.5", "ed_shape bloc",
        "play", "menu", "map_load portes", "map", "ent_send porte_hall Ouvrir", "ent_send porte_hal Ouvrir",
        "ent_send porte_hall Ouvre", "movers", "npc", "map_load formes", "npc", "ent", "map_sandbox", "map",
        "net", "net_name bob", "host 27098", "net_predict 0", "disconnect", "net",
        "character_blend 0.5", "light_sun 2", "light_sun", "carry_debug", "carry_debug",
        "fx_explode 0 1 0 1", "fx_smoke 0 1 0 5", "fx_impact_at 0 0 0", "fx", "fx_clear", "fx",
        "help gravity", "gravity 20", "gravity", "reset gravity", "phys_iterations 5", "list phys")
    $script = @("--seconds", "3")
    foreach ($commande in $session) { $script += @("--exec", $commande) }
    Lancer $racine "bac" $script (Join-Path $dossier "console-session.log")
    Verifier-Echo (Join-Path $dossier "console-session.log") $session[-1]

    Write-Host ("  console   {0} entrees, session de {1} commandes" -f $noms.Count, $session.Count)
}

# ---------------------------------------------------------------------------
#  Deroulement
# ---------------------------------------------------------------------------
$Reference = (Resolve-Path $Reference).Path
$Courant = (Resolve-Path $Courant).Path

$dossierA = Join-Path $Sortie "reference"
$dossierB = Join-Path $Sortie ($(if ($Bruit) { "reference-bis" } else { "courant" }))
$racineB = if ($Bruit) { $Reference } else { $Courant }

# Meme reglages des deux cotes.
$cfg = Join-Path $Courant "src\Exarion.Game\bin\Release\net9.0\exarion.cfg"
$cfgRef = Join-Path $Reference "src\Exarion.Game\bin\Release\net9.0\exarion.cfg"

Etape "Compilation"
Compiler $Reference
if (-not $Bruit) { Compiler $Courant }

if (Test-Path $cfg) { Copy-Item $cfg $cfgRef -Force } else { Remove-Item $cfgRef -ErrorAction SilentlyContinue }

Etape "Reference : $Reference"
Executer $Reference $dossierA

Etape ("{0} : {1}" -f $(if ($Bruit) { "Reference, second lancement" } else { "Copie de travail" }), $racineB)
Executer $racineB $dossierB

Etape "Comparaison (seuil $Seuil)"
$ecarts = 0

foreach ($s in $scenarios) {
    $a = Join-Path $dossierA "$($s.Nom).png"
    $b = Join-Path $dossierB "$($s.Nom).png"

    if (-not (Test-Path $a) -or -not (Test-Path $b)) {
        Write-Host ("  {0,-9} capture manquante" -f $s.Nom) -ForegroundColor Red
        $ecarts++
        continue
    }

    $r = [ExarionPngDiff]::Compare($a, $b, $Seuil)

    if ($null -eq $r) {
        Write-Host ("  {0,-9} TAILLES DIFFERENTES" -f $s.Nom) -ForegroundColor Red
        $ecarts++
        continue
    }

    $total = $r[0] * $r[1]
    $couleur = if ($r[2] -eq 0) { "Green" } else { "Yellow" }

    # ⚠️ Un ecart de PIXELS compte, comme un ecart de console. Jusqu'au 08/10
    # il s'affichait en jaune sans toucher au code de sortie : l'outil
    # repondait 0 - « identique » - sur une image differente. Aucune
    # conclusion fausse n'en est sortie parce que les chiffres etaient lus,
    # mais un outil qui dit « rien » quand il y a quelque chose finit
    # toujours par etre cru.
    if ($r[2] -gt 0) { $ecarts++ }
    $cadre = if ($r[2] -gt 0) { "   zone x {0}-{1} y {2}-{3}" -f $r[5], $r[7], $r[6], $r[8] } else { "" }
    Write-Host ("  {0,-9} {1,7} pixels >= {2}  ({3:0.000} %)   max {4,3}/765   non nuls {5}{6}" -f
        $s.Nom, $r[2], $Seuil, (100.0 * $r[2] / $total), $r[3], $r[4], $cadre) -ForegroundColor $couleur
}

foreach ($fichier in "console-liste.log", "console-aide.log", "console-session.log") {
    $la = Normaliser (Join-Path $dossierA $fichier) $Reference
    $lb = Normaliser (Join-Path $dossierB $fichier) $racineB
    $diff = Compare-Object $la $lb

    if ($diff) {
        Write-Host ("  {0,-17} {1} ligne(s) differente(s)" -f $fichier, $diff.Count) -ForegroundColor Red
        $diff | Select-Object -First 20 | ForEach-Object { Write-Host ("      {0} {1}" -f $_.SideIndicator, $_.InputObject) }
        $ecarts++
    }
    else {
        Write-Host ("  {0,-17} identique ({1} lignes)" -f $fichier, $la.Count) -ForegroundColor Green
    }
}

# Le journal de demarrage du scenario « menu » : cartes, personnage, audio.
# Compare-Object refuse une liste vide : sans le scenario menu, on se tait.
$ja = @(Normaliser (Join-Path $dossierA "menu.log") $Reference)
$jb = @(Normaliser (Join-Path $dossierB "menu.log") $racineB)
$diffJ = if ($ja.Count -gt 0 -and $jb.Count -gt 0) { Compare-Object $ja $jb } else { $null }

if ($ja.Count -eq 0 -or $jb.Count -eq 0) {
    Write-Host "  journal (menu)    non compare (scenario menu absent)"
}
elseif ($diffJ) {
    Write-Host ("  journal (menu)    {0} ligne(s) differente(s)" -f $diffJ.Count) -ForegroundColor Yellow
    $diffJ | Select-Object -First 20 | ForEach-Object { Write-Host ("      {0} {1}" -f $_.SideIndicator, $_.InputObject) }
    $ecarts++
}
else {
    Write-Host ("  journal (menu)    identique ({0} lignes)" -f $ja.Count) -ForegroundColor Green
}

if ($script:tronques -gt 0) {
    Write-Host "  $($script:tronques) journal(aux) tronque(s) : la comparaison de la console est INCOMPLETE" -ForegroundColor Red
    $ecarts++
}

Write-Host ""
Write-Host "Resultats : $Sortie"
if ($ecarts -gt 0) { exit 2 }
