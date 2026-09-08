param(
    [string]$In,
    [string]$Out,
    [double]$X = 0.0,      # sub-rect origin, as a fraction of the source
    [double]$Y = 0.0,
    [double]$W = 0.4,      # sub-rect size, as a fraction of the source
    [double]$H = 0.4,
    [int]$Size = 512,
    [double]$Fill = 1.0,    # share of the output the cropped content occupies
    [double]$Feather = 0.22 # alpha falloff at the crop border, as a share of the content size
)

# Cuts a sub-region out of a larger spatter and re-centres it as its own texture.
#
# The point is to get small marks that actually look like blood. Purpose-drawn small marks came
# back as clusters of near-perfect glossy circles - they measure well on alpha but read as
# bubbles. A fragment of a real spatter is irregular by construction: ragged blobs, broken
# tendrils, uneven droplets, none of it circular.
#
# Alpha is carried through untouched, so premultiplication survives the crop.

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SubCrop
{
    public static string Run(string inPath, string outPath,
                             double fx, double fy, double fw, double fh, int size,
                             double fill, double feather)
    {
        using (Bitmap src = new Bitmap(inPath))
        {
            int sx = (int)Math.Round(src.Width  * fx);
            int sy = (int)Math.Round(src.Height * fy);
            int sw = (int)Math.Round(src.Width  * fw);
            int sh = (int)Math.Round(src.Height * fh);

            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            if (sx + sw > src.Width)  sw = src.Width  - sx;
            if (sy + sh > src.Height) sh = src.Height - sy;
            if (sw < 4 || sh < 4)
                return "sub-rect too small";

            // Square it off so the crop can be scaled without distorting the shapes.
            int side = Math.Max(sw, sh);
            int cx = sx + sw / 2, cy = sy + sh / 2;
            int ox = cx - side / 2, oy = cy - side / 2;

            using (Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.FromArgb(0, 0, 0, 0));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                // Scaled to a share of the frame rather than stretched to fill it. Filling
                // magnifies the fragment, so individual droplets end up huge and run off every
                // edge - which reads as a blurry close-up, not a small mark. Keeping the content
                // near its source scale, centred with margin, preserves droplet size.
                int target = (int)Math.Round(size * fill);
                int off = (size - target) / 2;
                g.DrawImage(src, new Rectangle(off, off, target, target),
                                 new Rectangle(ox, oy, side, side), GraphicsUnit.Pixel);
                g.Dispose();

                // Feather the crop border to transparent.
                //
                // A spatter continues past wherever it was cut, so a straight crop ends its alpha
                // abruptly and the cut itself renders as a hard rectangle - the same square
                // artefact, this time baked into the artwork instead of caused by the shader.
                // Fading alpha towards the border makes the fragment read as isolated spatter.
                Feather(dst, off, target, feather);

                dst.Save(outPath, ImageFormat.Png);
            }
            return string.Format("cropped {0}x{0} from source rect {1},{2} {3}px", size, ox, oy, side);
        }
    }

    private static void Feather(Bitmap bmp, int off, int target, double feather)
    {
        if (feather <= 0) return;

        int size = bmp.Width;
        Rectangle r = new Rectangle(0, 0, size, size);
        BitmapData bd = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        byte[] px = new byte[bd.Stride * size];
        Marshal.Copy(bd.Scan0, px, 0, px.Length);

        double band = target * feather;
        if (band < 1) band = 1;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * bd.Stride + x * 4;
                if (px[i + 3] == 0) continue;

                // Radial falloff, not a fade toward the rectangle's edges.
                //
                // Fading toward the edges still leaves a rectangular signature: wherever the
                // source carries faint spatter right across the crop, the square outline shows
                // as a soft patch. Measuring from the centre outward has no straight edges to
                // betray, so the fragment reads as an isolated piece of spatter.
                double cx = off + target / 2.0;
                double cy = off + target / 2.0;
                double dx = x - cx, dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double radius = target / 2.0;

                double f = (radius - dist) / band;
                if (f < 0) f = 0; else if (f > 1) f = 1;
                f = f * f * (3 - 2 * f);   // smoothstep

                double a = px[i + 3] * f;
                px[i + 3] = (byte)Math.Round(a);
                // Kept premultiplied: RGB has to fall with alpha or the faded rim would still
                // draw colour where it is meant to be gone.
                px[i]     = (byte)Math.Round(px[i]     * f);
                px[i + 1] = (byte)Math.Round(px[i + 1] * f);
                px[i + 2] = (byte)Math.Round(px[i + 2] * f);
            }

        Marshal.Copy(px, 0, bd.Scan0, px.Length);
        bmp.UnlockBits(bd);
    }
}
'@

$r = [SubCrop]::Run($In, $Out, $X, $Y, $W, $H, $Size, $Fill, $Feather)
Write-Output "$([System.IO.Path]::GetFileName($Out)): $r"
