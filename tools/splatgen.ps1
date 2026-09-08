param(
    [int]$Size = 512,
    [int]$Seed = 1,
    [int]$Satellites = 7,
    [double]$Spread = 0.62,      # how far satellites sit from centre, 0..1 of radius
    [double]$CoreScale = 0.34,   # core blob radius as fraction of canvas
    [double]$Threshold = 1.0,    # metaball cutoff
    [double]$Edge = 0.35,        # softness band; larger = softer edge
    [string]$Out = "splat.png"
)

Add-Type -AssemblyName System.Drawing

# Metaball field: every blob contributes r^2/d^2, summed, then thresholded. Gives organic
# merged shapes with soft edges - which is what a splat is - rather than obvious circles.
# Output is WHITE with the shape carried entirely in alpha, so the game can tint it at runtime.

$rand = New-Object System.Random($Seed)
$half = $Size / 2.0

# blob list: x, y, radius (all in pixels)
$blobs = New-Object System.Collections.ArrayList
[void]$blobs.Add(@($half, $half, $Size * $CoreScale))

for ($i = 0; $i -lt $Satellites; $i++) {
    $angle = $rand.NextDouble() * [Math]::PI * 2
    $dist = ($Size * 0.5) * $Spread * (0.35 + $rand.NextDouble() * 0.65)
    $r = $Size * (0.035 + $rand.NextDouble() * 0.10)
    $x = $half + [Math]::Cos($angle) * $dist
    $y = $half + [Math]::Sin($angle) * $dist
    [void]$blobs.Add(@($x, $y, $r))
}

# a few tiny specks further out, for spatter realism
for ($i = 0; $i -lt 5; $i++) {
    $angle = $rand.NextDouble() * [Math]::PI * 2
    $dist = ($Size * 0.5) * (0.62 + $rand.NextDouble() * 0.3)
    $r = $Size * (0.008 + $rand.NextDouble() * 0.022)
    $x = $half + [Math]::Cos($angle) * $dist
    $y = $half + [Math]::Sin($angle) * $dist
    [void]$blobs.Add(@($x, $y, $r))
}

$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$Size, [int]$Size, $fmt)
$rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
$bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $fmt)
$stride = $bd.Stride
$bytes = New-Object byte[] ($stride * $Size)

$maxEdge = $half - 2   # keep the shape inside the canvas so nothing clips

for ($y = 0; $y -lt $Size; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $Size; $x++) {
        $sum = 0.0
        foreach ($b in $blobs) {
            $dx = $x - $b[0]
            $dy = $y - $b[1]
            $d2 = $dx * $dx + $dy * $dy
            if ($d2 -lt 1) { $d2 = 1 }
            $sum += ($b[2] * $b[2]) / $d2
        }

        # smoothstep across the threshold band -> soft antialiased alpha
        $t = ($sum - ($Threshold - $Edge)) / ($Edge * 2)
        if ($t -lt 0) { $t = 0 } elseif ($t -gt 1) { $t = 1 }
        $a = $t * $t * (3 - 2 * $t)

        # fade anything approaching the canvas edge, so tiling/clipping never shows a hard cut
        $dxc = $x - $half; $dyc = $y - $half
        $rad = [Math]::Sqrt($dxc * $dxc + $dyc * $dyc)
        if ($rad -gt $maxEdge * 0.86) {
            $f = 1 - (($rad - $maxEdge * 0.86) / ($maxEdge * 0.14))
            if ($f -lt 0) { $f = 0 }
            $a *= $f
        }

        $i = $row + $x * 4
        $bytes[$i]     = 255   # B
        $bytes[$i + 1] = 255   # G
        $bytes[$i + 2] = 255   # R  (white; tint applied in game)
        $bytes[$i + 3] = [byte][Math]::Round($a * 255)
    }
}

[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $bd.Scan0, $bytes.Length)
$bmp.UnlockBits($bd)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "wrote $Out  (${Size}x${Size}, seed $Seed, $($blobs.Count) blobs)"
