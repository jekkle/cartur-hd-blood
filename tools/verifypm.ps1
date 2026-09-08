param([string]$Dir)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class VerifyPM
{
    // Confirms RGB is black wherever alpha is zero. If any transparent pixel still carries
    // colour, a premultiplied-alpha shader can draw it - which is what made the decals square.
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

            long transparent = 0, leaking = 0;
            int worst = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * bd.Stride + x * 4;
                    if (px[i + 3] != 0) continue;
                    transparent++;
                    int m = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    if (m > 2) { leaking++; if (m > worst) worst = m; }
                }

            string verdict = leaking == 0 ? "OK premultiplied" : "LEAKING colour into transparent pixels";
            return string.Format("{0}x{1}  transparent {2}  leaking {3}  worstRGB {4}  {5}",
                                 w, h, transparent, leaking, worst, verdict);
        }
    }
}
'@

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-28} {1}" -f $f.Name, [VerifyPM]::Check($f.FullName))
}
