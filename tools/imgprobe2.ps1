param([string]$Dir)

Add-Type -AssemblyName System.Drawing

# The question the first probe raised: alpha exists, but is the COLOUR usable?
# A particle texture is sampled as RGB * alpha. If alpha marks a shape whose RGB is
# near-black, the particle renders as a black smear regardless of tint. So measure
# luminance only over pixels the alpha actually keeps, and check how tightly alpha
# tracks luminance (if they diverge, alpha is a silhouette bolted onto a dark render).

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    $bmp = [System.Drawing.Bitmap]::FromFile($f.FullName)
    $w = $bmp.Width; $h = $bmp.Height
    $lockFmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $lockFmt)
    $stride = $bd.Stride
    $bytes = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($bd)
    $bmp.Dispose()

    $step = 2
    $kept = 0; $keptLumSum = 0.0; $dark = 0
    $aSum = 0.0; $lSum = 0.0; $alSum = 0.0; $aaSum = 0.0; $llSum = 0.0; $n = 0
    $solid = 0   # alpha > 240

    for ($y = 0; $y -lt $h; $y += $step) {
        $row = $y * $stride
        for ($x = 0; $x -lt $w; $x += $step) {
            $i = $row + $x * 4
            $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]; $a = $bytes[$i+3]
            $lum = 0.299*$r + 0.587*$g + 0.114*$b

            $n++
            $aSum += $a; $lSum += $lum
            $alSum += $a * $lum; $aaSum += $a * $a; $llSum += $lum * $lum

            if ($a -gt 64) {
                $kept++
                $keptLumSum += $lum
                if ($lum -lt 25) { $dark++ }
            }
            if ($a -gt 240) { $solid++ }
        }
    }

    $keptMeanLum = 0
    $pctDark = 0
    if ($kept -gt 0) {
        $keptMeanLum = [Math]::Round($keptLumSum / $kept, 1)
        $pctDark = [Math]::Round(100 * $dark / $kept, 1)
    }
    $pctKept = [Math]::Round(100 * $kept / $n, 1)
    $pctSolid = [Math]::Round(100 * $solid / $n, 1)

    # Pearson correlation between alpha and luminance
    $num = $n * $alSum - $aSum * $lSum
    $den = [Math]::Sqrt(($n * $aaSum - $aSum * $aSum) * ($n * $llSum - $lSum * $lSum))
    $corr = 0
    if ($den -gt 0) { $corr = [Math]::Round($num / $den, 2) }

    Write-Output ("{0,-22} kept(a>64)={1,5}% solid={2,5}% meanLum(kept)={3,6} darkPx={4,5}% corr(a,lum)={5}" -f `
        $f.Name, $pctKept, $pctSolid, $keptMeanLum, $pctDark, $corr)
}
