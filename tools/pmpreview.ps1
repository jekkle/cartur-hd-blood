param([string]$Atlas, [string]$Out, [int]$Cols = 4, [int]$Rows = 3, [string]$Labels = "")

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PmPreview
{
    // Composites the atlas the way the engine will: PREMULTIPLIED alpha.
    //
    //     out = src.rgb * tint + bg * (1 - src.a)
    //
    // The earlier preview used System.Drawing's DrawImage, which blends straight alpha and so
    // multiplies an already-premultiplied colour by alpha a second time. Fully opaque areas are
    // unaffected - premultiplied and straight agree there - but anything with partial alpha came
    // out far too dark, which made feathered edges look like a grey haze that isn't in the file.
    public static string Render(string atlasPath, string outPath, int cols, int rows)
    {
        using (Bitmap src = new Bitmap(atlasPath))
        {
            int w = src.Width, h = src.Height;
            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData sd = src.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] sp = new byte[sd.Stride * h];
            Marshal.Copy(sd.Scan0, sp, 0, sp.Length);
            src.UnlockBits(sd);

            // Ground colour, and a blood tint applied to the texture's greyscale.
            double bgR = 74, bgG = 82, bgB = 58;
            double tR = 0.62, tG = 0.05, tB = 0.04;

            byte[] dp = new byte[sd.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * sd.Stride + x * 4;
                    double a = sp[i + 3] / 255.0;
                    // Greyscale carrier, already multiplied by alpha in the file.
                    double v = sp[i + 2];

                    double outR = v * tR + bgR * (1 - a);
                    double outG = v * tG + bgG * (1 - a);
                    double outB = v * tB + bgB * (1 - a);

                    dp[i]     = (byte)Math.Min(255, Math.Max(0, outB));
                    dp[i + 1] = (byte)Math.Min(255, Math.Max(0, outG));
                    dp[i + 2] = (byte)Math.Min(255, Math.Max(0, outR));
                    dp[i + 3] = 255;
                }

            using (Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData bd = dst.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(dp, 0, bd.Scan0, dp.Length);
                dst.UnlockBits(bd);
                dst.Save(outPath, ImageFormat.Png);
            }
            return w + "x" + h;
        }
    }
}
'@

$r = [PmPreview]::Render($Atlas, $Out, $Cols, $Rows)

# Grid lines and frame numbers drawn on top of the composited result.
$bmp = [System.Drawing.Bitmap]::FromFile($Out)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$cw = $bmp.Width / $Cols
$ch = $bmp.Height / $Rows
$pen = New-Object -TypeName System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [single]2)
for ($c = 1; $c -lt $Cols; $c++) { $g.DrawLine($pen, ($c*$cw), 0, ($c*$cw), $bmp.Height) }
for ($rw = 1; $rw -lt $Rows; $rw++) { $g.DrawLine($pen, 0, ($rw*$ch), $bmp.Width, ($rw*$ch)) }

$names = @(); if ($Labels -ne "") { $names = $Labels.Split(",") }
$font = New-Object -TypeName System.Drawing.Font -ArgumentList @("Consolas", [single]18, [System.Drawing.FontStyle]::Bold)
$white = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::White)
$black = New-Object -TypeName System.Drawing.SolidBrush -ArgumentList @([System.Drawing.Color]::Black)
for ($i = 0; $i -lt ($Cols*$Rows); $i++) {
    $x = ($i % $Cols) * $cw + 10
    $y = [Math]::Floor($i / $Cols) * $ch + 8
    $t = "$i"; if ($i -lt $names.Count) { $t = "$i  " + $names[$i].Trim() }
    $g.DrawString($t, $font, $black, ($x+2), ($y+2))
    $g.DrawString($t, $font, $white, $x, $y)
}
$g.Dispose()
$tmp = "$Out.tmp.png"
$bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Move-Item -Force $tmp $Out
Write-Output "wrote $Out ($r, premultiplied compositing)"
