# Compare deux PNG pixel par pixel.
#   .\diff.ps1 a.png b.png [seuil]
# Le seuil par defaut est 6 : en dessous, on compte du bruit de calcul
# (deux lancements identiques du meme binaire ne donnent PAS deux images
# bit a bit egales - c'est ce qui a fait conclure "96,7 % des pixels
# differents" a une autre session).

param([string]$A, [string]$B, [int]$Seuil = 6)

Add-Type -AssemblyName System.Drawing

$ia = [System.Drawing.Bitmap]::FromFile((Resolve-Path $A))
$ib = [System.Drawing.Bitmap]::FromFile((Resolve-Path $B))

if ($ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) {
    "TAILLES DIFFERENTES : $($ia.Width)x$($ia.Height) contre $($ib.Width)x$($ib.Height)"
    exit 1
}

$w = $ia.Width; $h = $ia.Height
$rect = New-Object System.Drawing.Rectangle 0,0,$w,$h
$fmt  = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb

$da = $ia.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
$db = $ib.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)

$n = $w * $h * 4
$ba = New-Object byte[] $n
$bb = New-Object byte[] $n
[System.Runtime.InteropServices.Marshal]::Copy($da.Scan0, $ba, 0, $n)
[System.Runtime.InteropServices.Marshal]::Copy($db.Scan0, $bb, 0, $n)

$ia.UnlockBits($da); $ib.UnlockBits($db)

$differents = 0; $max = 0; $somme = 0; $bruit = 0

for ($i = 0; $i -lt $n; $i += 4) {
    $d = [Math]::Abs($ba[$i]   - $bb[$i])   +
         [Math]::Abs($ba[$i+1] - $bb[$i+1]) +
         [Math]::Abs($ba[$i+2] - $bb[$i+2])

    if ($d -gt 0) { $bruit++ }
    if ($d -ge $Seuil) { $differents++; $somme += $d; if ($d -gt $max) { $max = $d } }
}

$total = $w * $h
$pct = [Math]::Round(100.0 * $differents / $total, 4)

"$A contre $B"
"  taille          : ${w}x${h} = $total pixels"
"  differents (>=$Seuil) : $differents  ($pct %)"
"  ecart max       : $max / 765"
"  somme des ecarts: $somme"
"  non nuls (>0)   : $bruit   <- inclut le bruit, ne rien en conclure"

$ia.Dispose(); $ib.Dispose()
