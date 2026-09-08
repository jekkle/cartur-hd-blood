param([string]$Dir)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class BlobCheck
{
    // Separates a ragged spatter from a solid blob, numerically.
    //
    // Both can measure well on "opaque core" and "black background" while looking completely
    // different in game: a spatter is sparse inside its own outline, a blob is filled. The
    // discriminator is how much of the alpha bounding box is actually covered.
    //
    //   spatter  - tendrils and separated droplets, so coverage/bbox is low
    //   blob     - one filled mass, so coverage/bbox approaches 1
    //
    // This is the test that would have rejected the round pools immediately, instead of me
    // judging them by eye and shipping them.
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

            int minX = w, minY = h, maxX = -1, maxY = -1;
            long covered = 0, opaque = 0;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte a = px[y * bd.Stride + x * 4 + 3];
                    if (a < 24) continue;
                    covered++;
                    if (a > 240) opaque++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

            if (maxX < 0)
                return "EMPTY";

            // Largest connected run of fully-opaque pixels, as a share of the frame.
            //
            // Average density alone is not enough: a file can hold one solid blob AND a wide
            // scatter of droplets, and the wide bounding box dilutes the blob out of the average.
            // That is exactly how a blob-plus-droplets file passed the first version of this test.
            // Finding the biggest single solid region catches it regardless of what surrounds it.
            // Shape of the largest solid region, not merely its size. A spatter's core is
            // legitimately one big connected opaque area - it just has arms radiating off it, so
            // it fills little of its own bounding box. A bad blob is compact and fills most of
            // its box. Size alone rejected everything; compactness is what actually separates
            // the two.
            double[] core = LargestRegionCompactness(px, bd.Stride, w, h);
            double compactness = core[0];
            double coreShare = core[1];   // that region's area as a share of the frame

            double bbox = (maxX - minX + 1) * (double)(maxY - minY + 1);
            double density = covered / bbox;          // how filled the silhouette is
            double opaqueShare = opaque / (double)(w * h);
            double extent = bbox / (w * (double)h);   // how much of the frame it spans

            // Compact AND large. A single droplet is compact by nature and entirely correct,
            // so compactness on its own wrongly condemns sparse droplet marks. Only a region
            // that is both solid-shaped and occupies real area is a blob.
            bool bigCore = coreShare > 0.02;

            string verdict;
            if ((compactness > 0.50 && bigCore) || density > 0.62) verdict = "BLOB - reject";
            else if ((compactness > 0.42 && bigCore) || density > 0.52) verdict = "borderline";
            else verdict = "spatter - good";

            return string.Format("density {0,5:0.##}  coreCompactness {1,5:0.##}  coreShare {2,5:0.##}  {3}",
                                 density, compactness, coreShare, verdict);
        }
    }

    // Iterative flood fill - a recursive one would blow the stack on a megapixel region.
    // Returns how much of its OWN bounding box the largest opaque region fills.
    private static double[] LargestRegionCompactness(byte[] px, int stride, int w, int h)
    {
        bool[] seen = new bool[w * h];
        int[] stack = new int[w * h];
        long best = 0;
        double bestCompactness = 0;
        double bestShare = 0;

        for (int start = 0; start < w * h; start++)
        {
            if (seen[start]) continue;
            int sy = start / w, sx = start % w;
            if (px[sy * stride + sx * 4 + 3] <= 240) { seen[start] = true; continue; }

            int top = 0;
            stack[top++] = start;
            seen[start] = true;
            long area = 0;
            int rMinX = w, rMinY = h, rMaxX = -1, rMaxY = -1;

            while (top > 0)
            {
                int cur = stack[--top];
                int cy = cur / w, cx = cur % w;
                area++;
                if (cx < rMinX) rMinX = cx;
                if (cx > rMaxX) rMaxX = cx;
                if (cy < rMinY) rMinY = cy;
                if (cy > rMaxY) rMaxY = cy;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (seen[ni]) continue;
                    seen[ni] = true;
                    if (px[ny * stride + nx * 4 + 3] > 240)
                        stack[top++] = ni;
                }
            }

            if (area > best)
            {
                best = area;
                double rBox = (rMaxX - rMinX + 1) * (double)(rMaxY - rMinY + 1);
                bestCompactness = rBox > 0 ? area / rBox : 0;
                bestShare = area / (double)(w * h);
            }
        }
        return new double[] { bestCompactness, bestShare };
    }
}
'@

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-20} {1}" -f $f.Name, [BlobCheck]::Check($f.FullName))
}
