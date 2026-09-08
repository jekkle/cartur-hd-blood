param(
    [string]$In,
    [string]$Out,
    [int]$Size = 512,
    [double]$Fill = 0.86,      # fraction of the frame the content should span
    [int]$AlphaThreshold = 6   # alpha below this counts as empty when finding the bounds
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class AutoCrop
{
    // Crops to the content's bounding box, then rescales it to fill a set fraction of a square
    // frame, centred.
    //
    // Needed because art delivered with the subject small in a large frame wastes the decal
    // quad: the quad's size is fixed by the game, so if only a tenth of the texture has any
    // blood in it, the blood renders at a tenth of the intended size. Normalising every source
    // to the same fill fraction makes the atlas cells visually consistent, which matters more
    // once cells are chosen by decal size.
    //
    // The bounding box is measured on alpha, not luminance - alpha is the coverage we actually
    // render, and these sources are so dark that a luminance test would miss most of the shape.
    public static string Run(string inPath, string outPath, int size, double fill, int alphaThreshold)
    {
        using (Bitmap src = new Bitmap(inPath))
        {
            int w = src.Width, h = src.Height;
            Rectangle full = new Rectangle(0, 0, w, h);
            BitmapData bd = src.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * h];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            src.UnlockBits(bd);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * bd.Stride + x * 4 + 3] < alphaThreshold)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

            if (maxX < 0)
                return "EMPTY " + inPath;

            int cw = maxX - minX + 1;
            int ch = maxY - minY + 1;

            // Square the crop around the content's centre, so rescaling can't distort the shape.
            int side = Math.Max(cw, ch);
            int ccx = minX + cw / 2;
            int ccy = minY + ch / 2;
            int sx = ccx - side / 2;
            int sy = ccy - side / 2;

            int target = (int)Math.Round(size * fill);
            int offset = (size - target) / 2;

            using (Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.FromArgb(0, 255, 255, 255));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // Source rect may extend past the edges when the content sits near a border;
                // DrawImage clips it and the transparent clear leaves the margin intact.
                g.DrawImage(src,
                    new Rectangle(offset, offset, target, target),
                    new Rectangle(sx, sy, side, side),
                    GraphicsUnit.Pixel);

                dst.Save(outPath, ImageFormat.Png);
            }

            double occupied = 100.0 * cw * ch / (w * (double)h);
            return string.Format("ok content was {0}x{1} ({2:0.#}% of frame) -> {3}x{3} at {4:0}% fill",
                                 cw, ch, occupied, size, fill * 100);
        }
    }
}
'@

$r = [AutoCrop]::Run($In, $Out, $Size, $Fill, $AlphaThreshold)
Write-Output "$([System.IO.Path]::GetFileName($In)): $r"
