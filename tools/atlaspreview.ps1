param([string]$Atlas, [string]$Out)

Add-Type -AssemblyName System.Drawing

# The atlas as it will read in game: tinted red, on ground-coloured earth. The file itself is
# white-on-transparent so the engine can tint it, which makes it look blank in an image viewer.

$img = [System.Drawing.Bitmap]::FromFile($Atlas)
$w = $img.Width; $h = $img.Height

$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$canvas = New-Object -TypeName System.Drawing.Bitmap -ArgumentList @([int]$w, [int]$h, $fmt)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::FromArgb(255, 74, 82, 58))

$tint = New-Object System.Drawing.Imaging.ColorMatrix
$tint.Matrix00 = 0.55; $tint.Matrix11 = 0.04; $tint.Matrix22 = 0.03; $tint.Matrix33 = 1.0
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetColorMatrix($tint)

$dest = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$g.DrawImage($img, $dest, 0, 0, $w, $h, [System.Drawing.GraphicsUnit]::Pixel, $attr)

# cell divider, to show the 2x2 grid the sheet animation will index
$pen = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(90, 255, 255, 255), [single]2)
$g.DrawLine($pen, ($w/2), 0, ($w/2), $h)
$g.DrawLine($pen, 0, ($h/2), $w, ($h/2))

$font = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]20, [System.Drawing.FontStyle]::Bold)
$brush = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::White)
$names = @("frame 0  tendril", "frame 1  spatter", "frame 2  spikes", "frame 3  soft")
for ($i = 0; $i -lt 4; $i++) {
    $x = ($i % 2) * ($w/2) + 12
    $y = [Math]::Floor($i / 2) * ($h/2) + 10
    $g.DrawString($names[$i], $font, $brush, $x, $y)
}

$g.Dispose(); $img.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Output "wrote $Out"
