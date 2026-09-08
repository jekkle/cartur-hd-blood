param([string]$Dir, [string]$Out)

Add-Type -AssemblyName System.Drawing

$files = @("splat_a.png","splat_b.png","splat_c.png","droplet.png")
$cell = 512
$cols = 2
$w = $cell * $cols
$h = $cell * 2

$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$canvas = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$w, [int]$h, $fmt)
$g = [System.Drawing.Graphics]::FromImage($canvas)

# muted earth background so red-on-ground reads honestly, rather than red on white
$g.Clear([System.Drawing.Color]::FromArgb(255, 74, 82, 58))

$font = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]26, [System.Drawing.FontStyle]::Bold)
$label = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::White)
$shadow = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::Black)

# blood tint applied via a colour matrix, exactly like tinting the particle at runtime
$tint = New-Object System.Drawing.Imaging.ColorMatrix
$tint.Matrix00 = 0.62; $tint.Matrix11 = 0.05; $tint.Matrix22 = 0.04; $tint.Matrix33 = 1.0
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetColorMatrix($tint)

for ($i = 0; $i -lt $files.Count; $i++) {
    $path = Join-Path $Dir $files[$i]
    if (-not (Test-Path $path)) { continue }
    $img = [System.Drawing.Bitmap]::FromFile($path)

    $cx = ($i % $cols) * $cell
    $cy = [Math]::Floor($i / $cols) * $cell
    $dest = New-Object System.Drawing.Rectangle $cx, $cy, $cell, $cell
    $g.DrawImage($img, $dest, 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)

    $g.DrawString($files[$i], $font, $shadow, ($cx + 14), ($cy + 12))
    $g.DrawString($files[$i], $font, $label, ($cx + 12), ($cy + 10))
    $img.Dispose()
}

$g.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Output "wrote $Out"
