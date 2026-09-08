param([string]$Dir)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Polarity
{
    // Some of this batch arrived as a BLACK subject on a WHITE background, the opposite of the
    // rest. That matters enormously: every repair path treats brightness as coverage, so an
    // inverted image would key the background in as solid blood and erase the subject entirely.
    //
    // Detected from the frame's border, which is background in either case. Also reports the
    // real alpha coverage, since a file can carry correct alpha and still be tonally inverted.
    public static string Check(string path)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * h];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            bmp.UnlockBits(bd);

            // Border sample: 6px frame around the edge.
            double borderLum = 0; int borderN = 0;
            int band = 6;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (x >= band && x < w - band && y >= band && y < h - band) continue;
                    int i = y * bd.Stride + x * 4;
                    borderLum += 0.299*px[i+2] + 0.587*px[i+1] + 0.114*px[i];
                    borderN++;
                }
            borderLum /= Math.Max(1, borderN);

            // Centre sample for contrast.
            double midLum = 0; int midN = 0;
            for (int y = h/3; y < 2*h/3; y++)
                for (int x = w/3; x < 2*w/3; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    midLum += 0.299*px[i+2] + 0.587*px[i+1] + 0.114*px[i];
                    midN++;
                }
            midLum /= Math.Max(1, midN);

            long opaque = 0, transparent = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte a = px[y * bd.Stride + x * 4 + 3];
                    if (a > 250) opaque++;
                    else if (a == 0) transparent++;
                }

            double total = w * (double)h;
            string polarity = borderLum > 128 ? "INVERTED (white bg, dark subject)" : "normal (black bg)";

            return string.Format("{0}x{1} borderLum={2,5:0.#} centreLum={3,5:0.#} alpha: opaque {4,4:0.#}% clear {5,4:0.#}%  {6}",
                                 w, h, borderLum, midLum, 100*opaque/total, 100*transparent/total, polarity);
        }
    }
}
'@

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-16} {1}" -f $f.Name, [Polarity]::Check($f.FullName))
}
