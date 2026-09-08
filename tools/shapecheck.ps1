param([string]$Dir)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ShapeCheck
{
    // Measures the OVERALL SILHOUETTE, which the blob test does not look at.
    //
    // blobcheck answers "is this a filled lump or ragged spatter" - about edge detail and
    // density. It says nothing about whether the outline is round, and a radial burst is
    // ragged at the edge while still being circular overall. A whole sheet can therefore pass
    // the blob test and still read as "everything is circles".
    //
    //   aspect      - longest axis over shortest, from the alpha bounding box. 1.0 is square.
    //   radialCV    - coefficient of variation of the outline's distance from the centroid,
    //                 sampled in 36 sectors. A disc or an even radial burst is near-uniform and
    //                 scores low; a lopsided, elongated or multi-lobed shape scores high.
    //   lopsided    - how far the centre of mass sits from the bounding-box centre, as a share
    //                 of the radius. Symmetric bursts sit dead centre.
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

            const int Thresh = 24;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            double sumX = 0, sumY = 0; long n = 0;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * bd.Stride + x * 4 + 3] < Thresh) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    sumX += x; sumY += y; n++;
                }

            if (maxX < 0 || n == 0)
                return "EMPTY";

            double bw = maxX - minX + 1, bh = maxY - minY + 1;
            double aspect = Math.Max(bw, bh) / Math.Min(bw, bh);

            double cx = sumX / n, cy = sumY / n;

            // Outline distance per angular sector, measured from the centre of mass.
            const int Sectors = 36;
            double[] maxR = new double[Sectors];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * bd.Stride + x * 4 + 3] < Thresh) continue;
                    double dx = x - cx, dy = y - cy;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    double a = Math.Atan2(dy, dx);
                    if (a < 0) a += Math.PI * 2;
                    int s = (int)(a / (Math.PI * 2) * Sectors);
                    if (s >= Sectors) s = Sectors - 1;
                    if (d > maxR[s]) maxR[s] = d;
                }

            double mean = 0; int used = 0;
            for (int i = 0; i < Sectors; i++) if (maxR[i] > 0) { mean += maxR[i]; used++; }
            if (used == 0) return "EMPTY";
            mean /= used;

            double var = 0;
            for (int i = 0; i < Sectors; i++) if (maxR[i] > 0) var += (maxR[i] - mean) * (maxR[i] - mean);
            double cv = Math.Sqrt(var / used) / mean;

            double boxCx = minX + bw / 2, boxCy = minY + bh / 2;
            double offset = Math.Sqrt((cx - boxCx) * (cx - boxCx) + (cy - boxCy) * (cy - boxCy)) / (mean > 0 ? mean : 1);

            string verdict;
            if (aspect > 1.6 || cv > 0.34 || offset > 0.18) verdict = "irregular - good";
            else if (aspect > 1.3 || cv > 0.26) verdict = "slightly irregular";
            else verdict = "CIRCULAR";

            return string.Format("aspect {0,4:0.##}  radialCV {1,5:0.##}  lopsided {2,5:0.##}  {3}",
                                 aspect, cv, offset, verdict);
        }
    }
}
'@

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-24} {1}" -f $f.Name, [ShapeCheck]::Check($f.FullName))
}
