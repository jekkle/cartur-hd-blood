param(
    [string]$In,
    [string]$Out,
    [int]$Size = 512,
    [double]$Gamma = 0.45,      # <1 lifts the mid/low alpha up toward opaque
    [double]$Floor = 0.06,      # alpha below this is treated as background and dropped
    [switch]$White              # rewrite RGB to white so the game tints it
)

Add-Type -AssemblyName System.Drawing

# Repairs a luminance-keyed PNG into a usable particle texture.
#
# The incoming alpha is proportional to brightness, so dark blood reads as near-transparent.
# What we actually want is coverage: "is there blood here at all". So we take the union of
# alpha and luminance as the coverage signal, drop everything under a small floor as
# background, then gamma-lift the remainder so the body of the splat becomes opaque while
# the rim stays soft. RGB is optionally flattened to white, because a texture that carries
# its own near-black colour renders as a dark smear no matter what tint is applied.

$src = [System.Drawing.Bitmap]::FromFile($In)
$w = $src.Width; $h = $src.Height
$lockFmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$bd = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $lockFmt)
$stride = $bd.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $bytes, 0, $bytes.Length)
$src.UnlockBits($bd)

for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $w; $x++) {
        $i = $row + $x * 4
        $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]; $a = $bytes[$i+3]

        $lum = (0.299*$r + 0.587*$g + 0.114*$b) / 255.0
        $av = $a / 255.0

        # coverage = union of the two signals; either one indicates blood is present
        $cov = [Math]::Max($av, $lum)
        if ($cov -le $Floor) { $cov = 0 } else { $cov = ($cov - $Floor) / (1 - $Floor) }
        if ($cov -gt 0) { $cov = [Math]::Pow($cov, $Gamma) }

        $bytes[$i+3] = [byte][Math]::Round([Math]::Min(1.0, $cov) * 255)

        if ($White) {
            # Premultiplied: black where transparent, so a premultiplied-alpha shader can't draw
            # the transparent margin as a solid square.
            $wv = [byte][Math]::Round(255 * ($bytes[$i+3] / 255.0))
            $bytes[$i] = $wv; $bytes[$i+1] = $wv; $bytes[$i+2] = $wv
        }
    }
}

$fixed = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$w, [int]$h, $lockFmt)
$bd2 = $fixed.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $lockFmt)
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $bd2.Scan0, $bytes.Length)
$fixed.UnlockBits($bd2)
$src.Dispose()

# downscale to the target power-of-two; 2048 is far more than a particle needs
$outBmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$Size, [int]$Size, $lockFmt)
$g2 = [System.Drawing.Graphics]::FromImage($outBmp)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g2.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$g2.DrawImage($fixed, 0, 0, $Size, $Size)
$g2.Dispose()
$fixed.Dispose()

$outBmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$outBmp.Dispose()
Write-Output "repaired -> $Out (${Size}x${Size})"
