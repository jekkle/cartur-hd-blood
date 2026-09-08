param(
    [string]$In,
    [string]$Out,
    [int]$Size = 512,
    [double]$AlphaGamma = 1.0     # 1.0 = leave alpha exactly as authored
)

# For sources whose alpha is already correct coverage.
#
# repair.ps1 exists for art whose alpha was keyed on brightness - it rebuilds the mask from
# max(alpha, luminance) and lifts it. Running that here would be actively harmful: these files
# already carry 21-46% fully-opaque pixels, and lifting would flatten the soft rims that make
# the pool read as liquid.
#
# All that's needed is to discard the RGB - which is near-black, and would render the blood
# almost invisible - and replace it with white, so each creature's startColor supplies the
# colour. The alpha passes through untouched.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Whiten
{
    public static string Run(string inPath, string outPath, int size, double alphaGamma)
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

            int opaque = 0, any = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    byte a = px[i + 3];

                    if (alphaGamma != 1.0 && a > 0)
                        a = (byte)Math.Round(Math.Pow(a / 255.0, alphaGamma) * 255);

                    px[i] = 255; px[i + 1] = 255; px[i + 2] = 255;
                    px[i + 3] = a;

                    if (a > 250) opaque++;
                    if (a > 8) any++;
                }

            Marshal.Copy(px, 0, bd.Scan0, px.Length);
            scaled.UnlockBits(bd);
            scaled.Save(outPath, ImageFormat.Png);

            double total = size * (double)size;
            return string.Format("{0:0.#}% covered, {1:0.#}% fully opaque", 100 * any / total, 100 * opaque / total);
        }
    }
}
'@

$r = [Whiten]::Run($In, $Out, $Size, $AlphaGamma)
Write-Output "$([System.IO.Path]::GetFileName($In)): $r"
