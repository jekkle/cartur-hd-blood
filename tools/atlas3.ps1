param(
    [string[]]$Files,
    [string]$Out,
    [string]$NormalOut,
    [int]$Cols = 4,
    [int]$Rows = 2,
    [int]$Cell = 512,
    [double]$NormalStrength = 2.5
)

# Generalisation of the 2x2 builder to any Cols x Rows grid, so the variant count isn't
# baked into the tooling. 4x2 at 512 gives eight splats in a 2048x1024 sheet - both
# dimensions still powers of two, which keeps GPU compression happy.
#
# Pixel work stays in compiled C#: the PowerShell version of the normal map took minutes.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class AtlasGenN
{
    public static string Build(string[] files, string outPath, string normalPath,
                               int cols, int rows, int cell, double strength)
    {
        int w = cols * cell, h = rows * cell;
        byte[] px = new byte[w * h * 4];

        int slots = cols * rows;
        for (int i = 0; i < files.Length && i < slots; i++)
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

                // Unity numbers sheet frames from the top-left, left to right then down.
                int ox = (i % cols) * cell;
                int oy = (i / cols) * cell;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        int s = y * bd.Stride + x * 4;
                        int d = ((oy + y) * w + (ox + x)) * 4;
                        px[d] = buf[s]; px[d+1] = buf[s+1]; px[d+2] = buf[s+2]; px[d+3] = buf[s+3];
                    }
            }
        }

        Save(px, w, h, outPath);

        byte[] nrm = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double tl = H(px,w,h,cell,x-1,y-1), t = H(px,w,h,cell,x,y-1), tr = H(px,w,h,cell,x+1,y-1);
                double l  = H(px,w,h,cell,x-1,y),                             rr = H(px,w,h,cell,x+1,y);
                double bl = H(px,w,h,cell,x-1,y+1), b = H(px,w,h,cell,x,y+1), br = H(px,w,h,cell,x+1,y+1);

                double dx = (tr + 2*rr + br) - (tl + 2*l + bl);
                double dy = (bl + 2*b  + br) - (tl + 2*t + tr);

                double nx = -dx * strength, ny = -dy * strength, nz = 1.0;
                double len = Math.Sqrt(nx*nx + ny*ny + nz*nz);
                nx /= len; ny /= len; nz /= len;

                int d2 = (y * w + x) * 4;
                nrm[d2]   = (byte)Math.Round((nz * 0.5 + 0.5) * 255);
                nrm[d2+1] = (byte)Math.Round((ny * 0.5 + 0.5) * 255);
                nrm[d2+2] = (byte)Math.Round((nx * 0.5 + 0.5) * 255);
                // Mirror the albedo's alpha rather than writing 255. Some decal shaders use the
                // normal map's alpha as the coverage mask, and vanilla's brains_n does carry
                // alpha - a fully opaque normal map then renders the decal as a solid square.
                // Copying the real coverage is safe either way: matched if the shader reads it,
                // ignored if it doesn't.
                nrm[d2+3] = px[d2+3];
            }

        Save(nrm, w, h, normalPath);
        return "ok " + w + "x" + h + " (" + cols + "x" + rows + " of " + cell + ")";
    }

    // Alpha as height, clamped within the owning cell so gradients never bleed across a cell
    // boundary and shade the neighbouring splat.
    private static double H(byte[] px, int w, int h, int cell, int x, int y)
    {
        int cx0 = (x < 0 ? 0 : x / cell) * cell;
        int cy0 = (y < 0 ? 0 : y / cell) * cell;
        if (cx0 > w - cell) cx0 = w - cell;
        if (cy0 > h - cell) cy0 = h - cell;
        if (x < cx0) x = cx0; else if (x >= cx0 + cell) x = cx0 + cell - 1;
        if (y < cy0) y = cy0; else if (y >= cy0 + cell) y = cy0 + cell - 1;
        return px[(y * w + x) * 4 + 3] / 255.0;
    }

    private static void Save(byte[] px, int w, int h, string path)
    {
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w * 4, (IntPtr)(bd.Scan0.ToInt64() + y * bd.Stride), w * 4);
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

$r = [AtlasGenN]::Build($Files, $Out, $NormalOut, $Cols, $Rows, $Cell, $NormalStrength)
Write-Output "$r"
