param(
    [int]$Size = 512,
    [int]$Seed = 1,
    [int]$Tendrils = 9,        # rays of tapered spatter off the core
    [int]$Droplets = 8,        # detached elongated droplets
    [double]$CoreScale = 0.24, # core radius as fraction of canvas
    [double]$Reach = 0.80,     # how far tendrils/droplets reach, 0..1 of half-canvas
    [double]$Edge = 0.10,      # softness band. small = crisp, wet-looking edge
    [double]$Wobble = 0.30,    # core irregularity
    [string]$Out = "splat.png"
)

Add-Type -AssemblyName System.Drawing

# Metaball field, but shaped like a real impact splat rather than a cloud of circles:
#
#   core      - several overlapping blobs jittered around dead centre, so the mass stays
#               centred. A decal gets a random Z rotation every time it spawns, so an
#               off-centre shape would visibly swing around the impact point.
#   tendrils  - chains of shrinking blobs along rays from the core, which merge into
#               tapered spikes. This corona is what reads as "blood" instead of "blob".
#   droplets  - detached chains of 2-3 blobs, so they land elongated, not as dots.
#
# Alpha also thins toward the shape edge, which is how a real splat looks wet at the
# centre and dry at the rim. Colour stays white; the game tints it.

$rand = New-Object System.Random($Seed)
$half = $Size / 2.0
$blobs = New-Object System.Collections.ArrayList

function Add-Blob([double]$x, [double]$y, [double]$r) {
    [void]$blobs.Add(@($x, $y, $r))
}

# --- core: jittered cluster held near centre -------------------------------------
$coreR = $Size * $CoreScale
Add-Blob $half $half $coreR
for ($i = 0; $i -lt 5; $i++) {
    $a = $rand.NextDouble() * [Math]::PI * 2
    $d = $coreR * $Wobble * $rand.NextDouble()
    Add-Blob ($half + [Math]::Cos($a) * $d) ($half + [Math]::Sin($a) * $d) ($coreR * (0.55 + $rand.NextDouble() * 0.35))
}

# --- tendrils: tapered spikes radiating out of the core --------------------------
for ($t = 0; $t -lt $Tendrils; $t++) {
    $a = $rand.NextDouble() * [Math]::PI * 2
    $len = $half * $Reach * (0.30 + $rand.NextDouble() * 0.70)
    $steps = 6 + $rand.Next(5)
    $curve = ($rand.NextDouble() - 0.5) * 0.7   # slight bend, so spikes aren't dead straight
    for ($s = 1; $s -le $steps; $s++) {
        $f = $s / [double]$steps
        $dist = $coreR * 0.7 + $len * $f
        $ang = $a + $curve * $f
        $r = $coreR * 0.42 * [Math]::Pow(1 - $f, 1.45)   # taper to a point
        if ($r -lt 1.2) { continue }
        Add-Blob ($half + [Math]::Cos($ang) * $dist) ($half + [Math]::Sin($ang) * $dist) $r
    }
}

# --- droplets: detached, elongated along their flight direction ------------------
for ($d = 0; $d -lt $Droplets; $d++) {
    $a = $rand.NextDouble() * [Math]::PI * 2
    $dist = $half * (0.42 + $rand.NextDouble() * ($Reach - 0.30))
    $r = $Size * (0.010 + $rand.NextDouble() * 0.026)
    $x = $half + [Math]::Cos($a) * $dist
    $y = $half + [Math]::Sin($a) * $dist
    $stretch = 1 + $rand.NextDouble() * 2.2
    $n = 1 + [int]$stretch
    for ($s = 0; $s -lt $n; $s++) {
        $f = $s / [double][Math]::Max(1, $n)
        Add-Blob ($x + [Math]::Cos($a) * $r * $s * 0.9) ($y + [Math]::Sin($a) * $r * $s * 0.9) ($r * (1 - $f * 0.55))
    }
}

# --- rasterise -------------------------------------------------------------------
$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$Size, [int]$Size, $fmt)
$rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
$bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $fmt)
$stride = $bd.Stride
$bytes = New-Object byte[] ($stride * $Size)

# flatten blob list to plain arrays; per-pixel ArrayList indexing is the slow part
$n = $blobs.Count
$bx = New-Object double[] $n
$by = New-Object double[] $n
$br = New-Object double[] $n
for ($i = 0; $i -lt $n; $i++) { $bx[$i] = $blobs[$i][0]; $by[$i] = $blobs[$i][1]; $br[$i] = $blobs[$i][2] * $blobs[$i][2] }

$fadeStart = ($half - 2) * 0.90

for ($y = 0; $y -lt $Size; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $Size; $x++) {
        $sum = 0.0
        for ($i = 0; $i -lt $n; $i++) {
            $dx = $x - $bx[$i]
            $dy = $y - $by[$i]
            $d2 = $dx * $dx + $dy * $dy
            if ($d2 -lt 1) { $d2 = 1 }
            $sum += $br[$i] / $d2
        }

        $t = ($sum - (1.0 - $Edge)) / ($Edge * 2)
        if ($t -lt 0) { $t = 0 } elseif ($t -gt 1) { $t = 1 }
        $a = $t * $t * (3 - 2 * $t)

        # wet centre / drier rim: opacity climbs with field strength well past the cutoff
        if ($a -gt 0) {
            $dens = 0.70 + 0.30 * [Math]::Min(1.0, ($sum - 1.0) / 1.8)
            if ($dens -lt 0.70) { $dens = 0.70 }
            $a *= $dens
        }

        $dxc = $x - $half; $dyc = $y - $half
        $rad = [Math]::Sqrt($dxc * $dxc + $dyc * $dyc)
        if ($rad -gt $fadeStart) {
            $f = 1 - (($rad - $fadeStart) / (($half - 2) - $fadeStart))
            if ($f -lt 0) { $f = 0 }
            $a *= $f
        }

        $i2 = $row + $x * 4
        $bytes[$i2]     = 255
        $bytes[$i2 + 1] = 255
        $bytes[$i2 + 2] = 255
        $bytes[$i2 + 3] = [byte][Math]::Round($a * 255)
    }
}

[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $bd.Scan0, $bytes.Length)
$bmp.UnlockBits($bd)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "wrote $Out  (${Size}x${Size}, seed $Seed, $n blobs)"
