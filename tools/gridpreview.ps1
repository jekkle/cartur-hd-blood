param([string]$Atlas, [string]$Out, [int]$Cols = 4, [int]$Rows = 2, [string]$Labels = "")

Add-Type -AssemblyName System.Drawing

# The atlas tinted red on ground colour, with cell borders and frame numbers. Needed because
# the file itself is white-on-transparent and looks blank in any viewer.

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

$cw = $w / $Cols
$ch = $h / $Rows
$pen = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(80, 255, 255, 255), [single]2)
for ($c = 1; $c -lt $Cols; $c++) { $g.DrawLine($pen, ($c * $cw), 0, ($c * $cw), $h) }
for ($r = 1; $r -lt $Rows; $r++) { $g.DrawLine($pen, 0, ($r * $ch), $w, ($r * $ch)) }

$names = @()
if ($Labels -ne "") { $names = $Labels.Split(",") }

$font = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]18, [System.Drawing.FontStyle]::Bold)
$white = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::White)
$black = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::Black)

for ($i = 0; $i -lt ($Cols * $Rows); $i++) {
    $x = ($i % $Cols) * $cw + 10
    $y = [Math]::Floor($i / $Cols) * $ch + 8
    $label = "$i"
    if ($i -lt $names.Count) { $label = "$i  " + $names[$i].Trim() }
    $g.DrawString($label, $font, $black, ($x + 2), ($y + 2))
    $g.DrawString($label, $font, $white, $x, $y)
}

$g.Dispose(); $img.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Output "wrote $Out"
