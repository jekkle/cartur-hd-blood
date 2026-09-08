param(
    [string]$Dir,
    [string]$Out,
    [string]$NormalOut,
    [int]$Cell = 512,
    [double]$NormalStrength = 2.5
)

# The per-pixel work is done in compiled C#. The PowerShell version of this took minutes on a
# 1024x1024 normal map - a million pixels times eight neighbour samples, each a PS function
# call. Same algorithm, one Add-Type away from instant.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class AtlasGen
{
    // Builds a 2x2 variant atlas plus a normal map derived from its alpha.
    //
    // Why an atlas: the decal material is shared by every blood effect in the game, so a single
    // texture would make every splat the same shape rotated. Unity's Texture Sheet Animation
    // can pick a random cell per particle, turning one material into four variants.
    //
    // Why a normal map from alpha: alpha is effectively a height field here - thick blood in the
    // core, tapering to nothing at the rim - so Sobel gradients over it give a tangent-space
    // normal that lets the decal catch light instead of reading as a flat sticker.
    public static string Build(string[] files, string outPath, string normalPath, int cell, double strength)
    {
        int size = cell * 2;
        byte[] px = new byte[size * size * 4];   // BGRA, tightly packed

        for (int i = 0; i < files.Length && i < 4; i++)
        {
            if (!System.IO.File.Exists(files[i]))
                return "MISSING " + files[i];

            using (Bitmap src = new Bitmap(files[i]))
            using (Bitmap scaled = new Bitmap(cell, cell, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, cell, cell);
                }

                Rectangle r = new Rectangle(0, 0, cell, cell);
                BitmapData bd = scaled.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] buf = new byte[bd.Stride * cell];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                scaled.UnlockBits(bd);

                int ox = (i % 2) * cell;
                int oy = (i / 2) * cell;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        int s = y * bd.Stride + x * 4;
                        int d = ((oy + y) * size + (ox + x)) * 4;
                        px[d] = buf[s]; px[d+1] = buf[s+1]; px[d+2] = buf[s+2]; px[d+3] = buf[s+3];
                    }
            }
        }

        Save(px, size, outPath);

        // --- normal map ---
        byte[] nrm = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double tl = H(px, size, cell, x-1, y-1), t = H(px, size, cell, x, y-1), tr = H(px, size, cell, x+1, y-1);
                double l  = H(px, size, cell, x-1, y),                                  rr = H(px, size, cell, x+1, y);
                double bl = H(px, size, cell, x-1, y+1), b = H(px, size, cell, x, y+1), br = H(px, size, cell, x+1, y+1);

                double dx = (tr + 2*rr + br) - (tl + 2*l + bl);
                double dy = (bl + 2*b  + br) - (tl + 2*t + tr);

                double nx = -dx * strength, ny = -dy * strength, nz = 1.0;
                double len = Math.Sqrt(nx*nx + ny*ny + nz*nz);
                nx /= len; ny /= len; nz /= len;

                int d = (y * size + x) * 4;
                nrm[d]   = (byte)Math.Round((nz * 0.5 + 0.5) * 255);
                nrm[d+1] = (byte)Math.Round((ny * 0.5 + 0.5) * 255);
                nrm[d+2] = (byte)Math.Round((nx * 0.5 + 0.5) * 255);
                nrm[d+3] = 255;
            }

        Save(nrm, size, normalPath);
        return "ok " + size + "x" + size;
    }

    // Alpha as height, clamped inside the owning atlas cell so one splat's edge never shades
    // its neighbour across a cell boundary.
    private static double H(byte[] px, int size, int cell, int x, int y)
    {
        int cx0 = (x < 0 ? 0 : x / cell) * cell;
        int cy0 = (y < 0 ? 0 : y / cell) * cell;
        if (cx0 > size - cell) cx0 = size - cell;
        if (cy0 > size - cell) cy0 = size - cell;
        if (x < cx0) x = cx0; else if (x >= cx0 + cell) x = cx0 + cell - 1;
        if (y < cy0) y = cy0; else if (y >= cy0 + cell) y = cy0 + cell - 1;
        return px[(y * size + x) * 4 + 3] / 255.0;
    }

    private static void Save(byte[] px, int size, string path)
    {
        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            Rectangle r = new Rectangle(0, 0, size, size);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            if (bd.Stride == size * 4)
            {
                Marshal.Copy(px, 0, bd.Scan0, px.Length);
            }
            else
            {
                for (int y = 0; y < size; y++)
                    Marshal.Copy(px, y * size * 4, (IntPtr)(bd.Scan0.ToInt64() + y * bd.Stride), size * 4);
            }
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

# Four genuinely distinct, non-directional shapes. Directional art (drips, streaks) is
# excluded: ParticleDecal randomises the decal's Z rotation, so anything with an implied
# "down" would spin to point sideways.
$files = @(
    (Join-Path $Dir "fixed_tendril_splat.png"),
    (Join-Path $Dir "fixed_spatter_cloud.png"),
    (Join-Path $Dir "fixed_radial_spikes.png"),
    (Join-Path $Dir "fixed_soft_cloud.png")
)

$r = [AtlasGen]::Build($files, $Out, $NormalOut, $Cell, $NormalStrength)
Write-Output "$r"
Write-Output "atlas  -> $Out"
Write-Output "normal -> $NormalOut"
