param(
    [string]$In,
    [string]$Out,
    [int]$Size = 512,
    [double]$Floor = 0.42,    # darkest the interior may go, as a fraction of full brightness
    [double]$Ceil = 1.0
)

# Tone-maps the greyscale detail instead of discarding it.
#
# whiten.ps1 set RGB to pure white, which was a mistake for these sources: their alpha is
# effectively binary - 26-46% fully opaque, 55-72% fully transparent, only 1-3% partial - so
# all of the wet sheen, rim highlight and interior mottling lives in the RGB. Flatten RGB and
# every splat becomes a solid silhouette.
#
# The decal shader multiplies texture RGB by the particle's startColor, so RGB has to stay
# bright enough for the tint to read while still carrying variation. Remapping the measured
# luminance range into [Floor, Ceil] does both: the darkest interior lands at Floor rather than
# near-zero, highlights stay near white, and the per-creature tint still comes through.
#
# Levels are measured only over pixels the alpha keeps, so the black background can't drag the
# range down.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Tone
{
    public static string Run(string inPath, string outPath, int size, double floor, double ceil)
    {
        using (Bitmap src = new Bitmap(inPath))
        using (Bitmap scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }

            Rectangle r = new Rectangle(0, 0, size, size);
            BitmapData bd = scaled.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * size];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);

            // Pass 1: measure the luminance range inside the kept area.
            double lo = 1.0, hi = 0.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    if (px[i + 3] < 64) continue;
                    double lum = (0.299*px[i+2] + 0.587*px[i+1] + 0.114*px[i]) / 255.0;
                    if (lum < lo) lo = lum;
                    if (lum > hi) hi = lum;
                }
            if (hi - lo < 0.01) { lo = 0; hi = 1; }   // degenerate: leave it alone

            // Pass 2: remap.
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    double lum = (0.299*px[i+2] + 0.587*px[i+1] + 0.114*px[i]) / 255.0;
                    double t = (lum - lo) / (hi - lo);
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    double v = floor + (ceil - floor) * t;

                    // Premultiply by alpha, so RGB is black wherever the texture is transparent.
                    //
                    // This is the fix for decals rendering as squares. Writing a tone-mapped grey
                    // into every pixel - including the fully transparent margin - means a shader
                    // using premultiplied-alpha blending draws that margin as a solid rectangle,
                    // because it takes RGB as the already-composited colour and never consults
                    // alpha. Inside the splat alpha is 255 so nothing changes; outside it the
                    // colour collapses to black and disappears.
                    double a = px[i + 3] / 255.0;
                    byte b = (byte)Math.Round(Math.Min(1.0, v) * a * 255);
                    px[i] = b; px[i+1] = b; px[i+2] = b;
                }

            Marshal.Copy(px, 0, bd.Scan0, px.Length);
            scaled.UnlockBits(bd);
            scaled.Save(outPath, ImageFormat.Png);

            return string.Format("luminance {0:0.00}..{1:0.00} remapped to {2:0.00}..{3:0.00}", lo, hi, floor, ceil);
        }
    }
}
'@

$r = [Tone]::Run($In, $Out, $Size, $Floor, $Ceil)
Write-Output "$([System.IO.Path]::GetFileName($In)): $r"
