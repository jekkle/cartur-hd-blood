param(
    [string]$GroundAtlas,
    [string]$AirAtlas,
    [string]$Out,
    [double]$PxPerMetre = 42
)

# Shows the atlas frames at their real relative sizes.
#
# The point is scale, not art: a spray droplet is authored at 0.05-0.7 world units and a
# blood_cloud "Big Splat" at 5-12, so they differ by more than a hundredfold in area. Preview
# sheets draw every frame the same size, which makes it impossible to see that a droplet is a
# speck and one boss-death cloud fills the screen. Everything here is drawn at one fixed
# pixels-per-metre with a scale bar, composited premultiplied the way the engine will.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ScaleMock
{
    private static byte[] _px; private static int _w, _h, _stride;

    public static void Begin(int w, int h)
    {
        _w = w; _h = h; _stride = w * 4;
        _px = new byte[_stride * h];
        for (int i = 0; i < w * h; i++)
        {
            _px[i*4+0] = 58; _px[i*4+1] = 82; _px[i*4+2] = 74; _px[i*4+3] = 255;
        }
    }

    /// Draws one atlas cell, scaled, composited with premultiplied alpha and blood-tinted.
    public static void Draw(string atlasPath, int cols, int rows, int frame,
                            int cx, int cy, int sizePx, double alpha)
    {
        using (Bitmap src = new Bitmap(atlasPath))
        {
            int cw = src.Width / cols, ch = src.Height / rows;
            int ox = (frame % cols) * cw, oy = (frame / cols) * ch;

            using (Bitmap cell = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(cell))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, new Rectangle(0, 0, sizePx, sizePx),
                                     new Rectangle(ox, oy, cw, ch), GraphicsUnit.Pixel);
                }

                Rectangle r = new Rectangle(0, 0, sizePx, sizePx);
                BitmapData bd = cell.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] cp = new byte[bd.Stride * sizePx];
                Marshal.Copy(bd.Scan0, cp, 0, cp.Length);
                cell.UnlockBits(bd);

                double tR = 0.62, tG = 0.05, tB = 0.04;

                for (int y = 0; y < sizePx; y++)
                    for (int x = 0; x < sizePx; x++)
                    {
                        int dx = cx - sizePx/2 + x, dy = cy - sizePx/2 + y;
                        if (dx < 0 || dy < 0 || dx >= _w || dy >= _h) continue;

                        int s = y * bd.Stride + x * 4;
                        double a = cp[s+3] / 255.0 * alpha;
                        if (a <= 0.002) continue;
                        double v = cp[s+2] * alpha;   // premultiplied grey carrier

                        int d = dy * _stride + dx * 4;
                        _px[d+2] = (byte)Math.Min(255, v * tR + _px[d+2] * (1 - a));
                        _px[d+1] = (byte)Math.Min(255, v * tG + _px[d+1] * (1 - a));
                        _px[d+0] = (byte)Math.Min(255, v * tB + _px[d+0] * (1 - a));
                    }
            }
        }
    }

    public static void Save(string path)
    {
        using (Bitmap bmp = new Bitmap(_w, _h, PixelFormat.Format32bppArgb))
        {
            Rectangle r = new Rectangle(0, 0, _w, _h);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < _h; y++)
                Marshal.Copy(_px, y * _stride, (IntPtr)(bd.Scan0.ToInt64() + y * bd.Stride), _stride);
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

$W = 1700; $H = 1000
[ScaleMock]::Begin($W, $H)

function M([double]$metres) { return [int][Math]::Max(2, [Math]::Round($metres * $PxPerMetre)) }

$rand = New-Object System.Random(7)

# --- big blood_cloud mist: 10 units. Queen / Morgen death. Mist row, frames 8-11.
[ScaleMock]::Draw($AirAtlas, 4, 4, 10, 420, 400, (M 10), 0.85)
[ScaleMock]::Draw($AirAtlas, 4, 4, 8,  520, 470, (M 7),  0.65)

# --- a ground decal at 3 units, for reference against it
[ScaleMock]::Draw($GroundAtlas, 4, 4, 0, 1150, 300, (M 3), 1.0)
[ScaleMock]::Draw($GroundAtlas, 4, 4, 6, 1330, 400, (M 2.2), 1.0)

# --- a troll death decal at 6 units
[ScaleMock]::Draw($GroundAtlas, 4, 4, 4, 1180, 720, (M 6), 1.0)

# --- a hit's worth of airborne droplets: 0.05-0.7 units, the common case
for ($i = 0; $i -lt 90; $i++) {
    $f = @(0,1,2,3,4,5,6,7,12,13)[$rand.Next(10)]
    $sz = 0.08 + $rand.NextDouble() * 0.55
    $x = 300 + $rand.Next(340)
    $y = 760 + [int](($rand.NextDouble() - 0.5) * 200)
    [ScaleMock]::Draw($AirAtlas, 4, 4, $f, $x, $y, (M $sz), (0.55 + $rand.NextDouble() * 0.45))
}

# --- small blood_cloud wisps: 0.1-0.5 units, 34 of the 37 systems
for ($i = 0; $i -lt 40; $i++) {
    $f = 8 + $rand.Next(4)
    $sz = 0.12 + $rand.NextDouble() * 0.38
    $x = 780 + $rand.Next(200)
    $y = 780 + [int](($rand.NextDouble() - 0.5) * 160)
    [ScaleMock]::Draw($AirAtlas, 4, 4, $f, $x, $y, (M $sz), (0.5 + $rand.NextDouble() * 0.4))
}

[ScaleMock]::Save($Out)

# --- labels and a scale bar
$bmp = [System.Drawing.Bitmap]::FromFile($Out)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$f1 = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]19, [System.Drawing.FontStyle]::Bold)
$f2 = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]15, [System.Drawing.FontStyle]::Regular)
$wh = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::White)
$bk = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::FromArgb(200,0,0,0))

function Label([string]$t, [int]$x, [int]$y, $font) {
    $g.DrawString($t, $font, $bk, ($x+2), ($y+2))
    $g.DrawString($t, $font, $wh, $x, $y)
}

Label "AIR - blood_cloud 'Big Splat' 10 units   (Queen / Morgen death, mist frames 8-11)" 40 40 $f1
Label "only 3 of 37 blood_cloud systems are this big" 40 70 $f2
Label "GROUND decals - 2 to 3 units" 1010 190 $f1
Label "GROUND - troll death, 6 units" 1010 620 $f1
Label "AIR - a hit's droplets, 0.08-0.63 units  (the common case)" 250 900 $f1
Label "AIR - small blood_cloud wisps, 0.12-0.5 units  (34 of 37)" 700 940 $f2

# scale bar: 1 metre
$pen = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::White, [single]3)
$bx = 40; $by = $H - 60
$g.DrawLine($pen, $bx, $by, ($bx + [int]$PxPerMetre), $by)
$g.DrawLine($pen, $bx, ($by-8), $bx, ($by+8))
$g.DrawLine($pen, ($bx + [int]$PxPerMetre), ($by-8), ($bx + [int]$PxPerMetre), ($by+8))
Label "1 metre" $bx ($by + 10) $f2

$g.Dispose()
$tmp = "$Out.tmp.png"
$bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Move-Item -Force $tmp $Out
Write-Output "wrote $Out"
