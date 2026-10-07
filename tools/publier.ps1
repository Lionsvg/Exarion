# ===========================================================================
#  Exarion - publication de la version PUBLIQUE
#
#    pwsh tools\publier.ps1 -Version 0.1.0               publie
#    pwsh tools\publier.ps1 -Version 0.1.0 -SansPousser  prepare et verifie,
#                                                        n'envoie rien
#
#  Le depot de developpement (Exariondev, prive) et le depot public
#  (Exarion) n'ont PAS le meme historique, et c'est voulu : l'historique de
#  developpement contient Model3D, un modele genere par Meshy dont la
#  redistribution n'est pas tranchee. Pousser `main` vers le depot public le
#  publierait - meme retire du dernier commit, un historique git garde tout.
#
#  On publie donc un INSTANTANE : HEAD exporte par `git archive` (qui
#  respecte les export-ignore de .gitattributes), compile et teste SANS
#  Model3D, puis ajoute au depot public en un seul commit, avec un tag.
#
#  Rien n'est envoye si une seule verification echoue.
# ===========================================================================

#Requires -Version 7

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Depot = "https://github.com/Lionsvg/Exarion.git",
    [string]$Message,
    [switch]$SansPousser
)

$ErrorActionPreference = "Stop"
$racine = Split-Path -Parent $PSScriptRoot

function Etape([string]$texte) { Write-Host ""; Write-Host "== $texte" -ForegroundColor Cyan }
function Echec([string]$texte) { Write-Host "[ECHEC] $texte" -ForegroundColor Red; exit 1 }
function SansGit([string]$url) { ($url.Trim().TrimEnd('/') -replace '\.git$', '').ToLowerInvariant() }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Echec "Version attendue sous la forme 1.2.3, recu '$Version'." }
$tag = "v$Version"

# --- 1. Le depot de developpement --------------------------------------------
#
# git archive exporte le COMMIT, pas le disque : une modification non
# commitee serait absente de la version publiee, sans que rien ne le dise.
Etape "Depot de developpement"
if (git -C $racine status --porcelain --untracked-files=no) { Echec "Modifications non commitees. La version publiee doit correspondre a un commit." }
# Un fichier non suivi n'est pas une faute (une carte enregistree pour
# essai, une capture) mais il ne partira pas : on le dit.
$nonSuivis = git -C $racine ls-files --others --exclude-standard
if ($nonSuivis) { Write-Host "Non suivis, donc NON publies : $($nonSuivis -join ', ')" -ForegroundColor Yellow }
$commit = git -C $racine rev-parse --short HEAD

# origin ne doit JAMAIS etre le depot public : un simple `git push` y
# enverrait tout l'historique, Model3D compris. C'etait le cas juste apres
# le renommage de l'ancien depot en Exariondev (07/10/2026).
$origin = git -C $racine remote get-url origin 2>$null
if ($origin -and (SansGit $origin) -eq (SansGit $Depot)) {
    Echec "origin pointe sur le depot PUBLIC ($origin). Corriger d'abord : git remote set-url origin https://github.com/Lionsvg/Exariondev.git"
}

# L'identite du DEPOT, pas la globale : GitHub refuse un commit qui expose
# l'adresse privee.
$nom = git -C $racine config user.name
$mail = git -C $racine config user.email
if ($mail -notlike "*@users.noreply.github.com") { Echec "user.email n'est pas l'adresse noreply de GitHub : $mail" }
Write-Host "HEAD $commit, origin $origin"

# --- 2. Export ---------------------------------------------------------------
#
# Deux extractions du MEME zip : une pour compiler et tester, une pour
# publier. Compiler dans le dossier publie y laisserait des sorties de
# compilation qu'un `git add -A` pourrait ramasser.
Etape "Export de $commit"
$travail = Join-Path ([System.IO.Path]::GetTempPath()) ("exarion-publication-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$zip = "$travail.zip"
$verif = Join-Path $travail "verification"
$public = Join-Path $travail "public"
New-Item -ItemType Directory -Force $travail | Out-Null

git -C $racine archive --format=zip -o $zip HEAD
if ($LASTEXITCODE -ne 0) { Echec "git archive a echoue." }
Expand-Archive -LiteralPath $zip -DestinationPath $verif

# --- 3. Verifications : rien ne part sans preuve -----------------------------
Etape "Verifications"
$modeles = Get-ChildItem -LiteralPath $verif -Recurse -Force -File |
    Where-Object { $_.Extension -in ".glb", ".gltf", ".fbx" -or $_.FullName -match '[\\/]Model3D[\\/]' }
if ($modeles) { Echec "Des fichiers de modele sont dans l'export : $($modeles.FullName -join ', ')" }
foreach ($requis in "LICENSE", "NOTICE", "README.md", "Exarion.sln") {
    if (-not (Test-Path (Join-Path $verif $requis))) { Echec "$requis manque dans l'export." }
}
Write-Host "Aucun modele 3D, LICENSE et NOTICE presents."

# Jamais --no-build : il peut lancer un binaire perime (voir CLAUDE.md).
dotnet build (Join-Path $verif "Exarion.sln") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Echec "L'export ne compile pas." }
dotnet test (Join-Path $verif "Exarion.sln") -c Release --nologo
if ($LASTEXITCODE -ne 0) { Echec "Des tests echouent dans l'export." }

# --- 4. Un commit unique sur le depot public ---------------------------------
Etape "Commit et tag $tag"
git clone --quiet $Depot $public
if ($LASTEXITCODE -ne 0) { Echec "Clonage impossible : $Depot" }
if (git -C $public tag --list $tag) { Echec "Le tag $tag existe deja sur le depot public." }

# L'instantane REMPLACE l'etat precedent : un fichier supprime en
# developpement doit disparaitre du public aussi.
Get-ChildItem -LiteralPath $public -Force | Where-Object Name -ne ".git" | Remove-Item -Recurse -Force
Expand-Archive -LiteralPath $zip -DestinationPath $public

git -C $public config user.name $nom
git -C $public config user.email $mail
git -C $public add -A
if (-not $Message) { $Message = "Exarion $tag" }
$fichierMessage = Join-Path $travail "message.txt"
Set-Content -LiteralPath $fichierMessage -Value $Message -Encoding utf8NoBOM
git -C $public commit --quiet -F $fichierMessage
if ($LASTEXITCODE -ne 0) { Echec "Commit impossible (rien n'a change depuis la derniere version ?)." }
git -C $public tag -a $tag -m "Exarion $tag"
git -C $public show --stat --oneline HEAD | Select-Object -Last 5

if ($SansPousser) {
    Write-Host ""
    Write-Host "Pret, RIEN n'a ete envoye. Instantane : $public" -ForegroundColor Yellow
    exit 0
}

# --- 5. Envoi ----------------------------------------------------------------
#
# HEAD:main, et pas le nom de la branche locale : un clone de depot VIDE
# part sur la branche par defaut de la configuration git, pas forcement main.
Etape "Envoi vers $Depot"
git -C $public push --quiet origin HEAD:refs/heads/main
if ($LASTEXITCODE -ne 0) { Echec "Envoi de main refuse." }
git -C $public push --quiet origin $tag
if ($LASTEXITCODE -ne 0) { Echec "Envoi du tag $tag refuse." }

Write-Host ""
Write-Host "Publie : $tag (developpement $commit)" -ForegroundColor Green
Write-Host "Release : gh release create $tag --repo Lionsvg/Exarion --title `"Exarion $tag`" --notes-file <notes.md>"
