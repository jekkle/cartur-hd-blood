param([string]$Dir)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Ink
{
    // For premultiplied art, RGB should equal alpha where the ink is white. If RGB sits well
    // below alpha, the texture carries its own grey and every tint applied to it comes out
    // darker and less saturated than the tint asks for.
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

            double sr=0, sg=0, sb=0; int n=0;          // solid pixels only
            double ratioSum=0; int ratioN=0;           // RGB/alpha over all inked pixels
            for (int y=0; y<h; y++)
                for (int x=0; x<w; x++)
                {
                    int i = y*bd.Stride + x*4;
                    int b=px[i], g=px[i+1], rr=px[i+2], a=px[i+3];
                    if (a >= 250) { sr+=rr; sg+=g; sb+=b; n++; }
                    if (a >= 32)
                    {
                        double lum = 0.299*rr + 0.587*g + 0.114*b;
                        ratioSum += lum / a; ratioN++;
                    }
                }
            if (n == 0) return "no solid pixels";
            return string.Format("solid px: R{0,3:0} G{1,3:0} B{2,3:0}   ink/alpha {3,4:0.00}  (1.00 = pure white ink)",
                                 sr/n, sg/n, sb/n, ratioSum/Math.Max(1,ratioN));
        }
    }
}
'@
foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-26} {1}" -f $f.Name, [Ink]::Check($f.FullName))
}
