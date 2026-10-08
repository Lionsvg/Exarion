# Mesure le niveau d'un WAV PCM 16 bits : crete et RMS par canal.
#   pwsh tools\wav-niveau.ps1 fichier.wav [debut] [fin]
# Debut et fin en secondes ; par defaut on saute la premiere seconde, le
# temps que la carte son demarre et que les sources posees par --exec
# commencent a jouer. Le WAV est celui que ecrit --audio-dump : c'est la
# « capture d'ecran du son », et ce script en est le diff-png.

param([Parameter(Mandatory = $true)][string]$Fichier, [double]$Debut = 1.0, [double]$Fin = -1.0)

$ErrorActionPreference = "Stop"

$octets = [System.IO.File]::ReadAllBytes((Resolve-Path $Fichier))

# Parcours des morceaux RIFF : « fmt » donne le format, « data » les echantillons.
$canaux = 0; $frequence = 0; $bits = 0; $donnees = -1; $taille = 0
$i = 12
while ($i + 8 -le $octets.Length) {
    $id = [System.Text.Encoding]::ASCII.GetString($octets, $i, 4)
    $longueur = [BitConverter]::ToInt32($octets, $i + 4)

    if ($id -eq "fmt ") {
        $canaux = [BitConverter]::ToUInt16($octets, $i + 10)
        $frequence = [BitConverter]::ToInt32($octets, $i + 12)
        $bits = [BitConverter]::ToUInt16($octets, $i + 22)
    }
    elseif ($id -eq "data") {
        $donnees = $i + 8
        $taille = [Math]::Min($longueur, $octets.Length - $donnees)
        break
    }

    $i += 8 + $longueur + ($longueur % 2)
}

if ($bits -ne 16 -or $donnees -lt 0) { throw "WAV PCM 16 bits attendu ($bits bits, donnees a $donnees)" }

$images = [int]($taille / (2 * $canaux))
$premier = [int][Math]::Min($images, [Math]::Max(0, $Debut * $frequence))
$dernier = if ($Fin -lt 0) { $images } else { [int][Math]::Min($images, $Fin * $frequence) }

$crete = New-Object double[] $canaux
$carres = New-Object double[] $canaux

for ($n = $premier; $n -lt $dernier; $n++) {
    for ($c = 0; $c -lt $canaux; $c++) {
        $v = [BitConverter]::ToInt16($octets, $donnees + 2 * ($n * $canaux + $c)) / 32768.0
        $a = [Math]::Abs($v)
        if ($a -gt $crete[$c]) { $crete[$c] = $a }
        $carres[$c] += $v * $v
    }
}

$compte = [Math]::Max(1, $dernier - $premier)
"$Fichier : $canaux canaux, $frequence Hz, {0:0.00} s mesurees" -f ($compte / $frequence)
for ($c = 0; $c -lt $canaux; $c++) {
    $nom = if ($canaux -eq 2) { @("gauche", "droite")[$c] } else { "canal $c" }
    "  {0,-7} crete {1:0.0000}   RMS {2:0.0000}" -f $nom, $crete[$c], [Math]::Sqrt($carres[$c] / $compte)
}
