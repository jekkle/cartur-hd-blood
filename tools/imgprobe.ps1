param([string]$Dir)

Add-Type -AssemblyName System.Drawing

# For each PNG: size, pixel format, whether alpha is actually used, and how much of the
# frame is non-black. The alpha question is what decides whether these drop straight in
# or need keying, and it can't be judged by looking at the image.

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    $bmp = [System.Drawing.Bitmap]::FromFile($f.FullName)
    $w = $bmp.Width; $h = $bmp.Height
    $fmt = $bmp.PixelFormat

    $lockFmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $lockFmt)
    $stride = $bd.Stride
    $bytes = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($bd)
    $bmp.Dispose()

    $step = 2   # sample every 2nd pixel; plenty at these sizes
    $total = 0; $alphaPartial = 0; $alphaZero = 0
    $lumSum = 0.0; $lit = 0; $maxLum = 0
    $satSum = 0.0

    for ($y = 0; $y -lt $h; $y += $step) {
        $row = $y * $stride
        for ($x = 0; $x -lt $w; $x += $step) {
            $i = $row + $x * 4
            $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]; $a = $bytes[$i+3]
            $total++
            if ($a -eq 0) { $alphaZero++ }
            elseif ($a -lt 250) { $alphaPartial++ }

            $lum = [int](0.299*$r + 0.587*$g + 0.114*$b)
            $lumSum += $lum
            if ($lum -gt $maxLum) { $maxLum = $lum }
            if ($lum -gt 12) {
                $lit++
                $mx = [Math]::Max($r, [Math]::Max($g, $b))
                $mn = [Math]::Min($r, [Math]::Min($g, $b))
                if ($mx -gt 0) { $satSum += ($mx - $mn) / [double]$mx }
            }
        }
    }

    $pctZero = [Math]::Round(100 * $alphaZero / $total, 1)
    $pctPartial = [Math]::Round(100 * $alphaPartial / $total, 1)
    $pctLit = [Math]::Round(100 * $lit / $total, 1)
    $meanLum = [Math]::Round($lumSum / $total, 1)
    $meanSat = 0
    if ($lit -gt 0) { $meanSat = [Math]::Round($satSum / $lit, 2) }

    $verdict = "has alpha"
    if ($pctZero -lt 1 -and $pctPartial -lt 1) { $verdict = "OPAQUE (needs keying)" }

    Write-Output ("{0,-22} {1}x{2} {3,-18} a0={4}% aPart={5}% lit={6}% meanLum={7} maxLum={8} sat={9}  {10}" -f `
        $f.Name, $w, $h, $fmt, $pctZero, $pctPartial, $pctLit, $meanLum, $maxLum, $meanSat, $verdict)
}
