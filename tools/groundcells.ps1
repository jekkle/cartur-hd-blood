# Exports the ground atlas as numbered per-cell PNGs plus a numbered contact sheet.
#
# Numbering matches Variants.Build exactly: reading order, row 0 at the top, and the
# LAST row is the "small" set (Plugin.SizeAwareVariants). So a number here is the same
# cell the mod picks - say "remove 7" and it maps to one material with no ambiguity.
#
# The art is premultiplied with the shape in the alpha, so it looks near-black in a
# viewer. Each cell is therefore composited over light grey the way the game composites
# it over ground: out = rgb + bg*(1-a). That is the true render, not a re-tint.
param(
    [string]$Atlas  = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets\blood_splat_atlas.png",
    [string]$OutDir = "$env:USERPROFILE\Desktop\Cartur Blood - GROUND cells",
    [int]$Cols = 4,
    [int]$Rows = 4,
    [int]$SmallRows = 1,
    [int]$Bg = 216
)

Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class GroundCells
{
    public static string Run(string atlasPath, string outDir, int cols, int rows, int smallRows, int bg)
    {
        Directory.CreateDirectory(outDir);
        StringBuilder log = new StringBuilder();

        using (Bitmap atlas = new Bitmap(atlasPath))
        {
            int cw = atlas.Width / cols;
            int ch = atlas.Height / rows;
            int thumb = 512;

            using (Bitmap sheet = new Bitmap(cols * thumb, rows * thumb, PixelFormat.Format32bppArgb))
            using (Graphics gs = Graphics.FromImage(sheet))
            using (Font f = new Font("Segoe UI", 44, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 20, FontStyle.Regular))
            {
                gs.Clear(Color.FromArgb(255, bg, bg, bg));
                gs.InterpolationMode = InterpolationMode.HighQualityBicubic;

                int idx = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        idx++;
                        bool isSmall = r >= rows - smallRows;
                        string kind = isSmall ? "SMALL" : "LARGE";

                        using (Bitmap cell = Composite(atlas, c * cw, r * ch, cw, ch, bg))
                        {
                            string name = string.Format("{0:00}_{1}_col{2}_row{3}.png", idx, kind, c, r);
                            cell.Save(Path.Combine(outDir, name), ImageFormat.Png);
                            gs.DrawImage(cell, c * thumb, r * thumb, thumb, thumb);
                            log.AppendLine(string.Format("{0,2}  {1,-5}  col{2} row{3}  {4}", idx, kind, c, r, name));
                        }

                        Rectangle box = new Rectangle(c * thumb + 10, r * thumb + 10, 118, 66);
                        gs.FillRectangle(new SolidBrush(Color.FromArgb(205, 0, 0, 0)), box);
                        gs.DrawString(idx.ToString(), f, Brushes.White, box.X + 8, box.Y + 2);
                        gs.DrawString(kind, fs, Brushes.White, box.X + 62, box.Y + 34);
                        gs.DrawRectangle(new Pen(Color.FromArgb(110, 0, 0, 0), 2f),
                                         c * thumb, r * thumb, thumb - 2, thumb - 2);
                    }
                }

                sheet.Save(Path.Combine(outDir, "ALL - numbered.png"), ImageFormat.Png);
            }
        }
        return log.ToString();
    }

    // Premultiplied composite over a flat background. Row-by-row through Marshal.Copy so
    // no /unsafe compile is needed, and stride padding is respected.
    static Bitmap Composite(Bitmap src, int x, int y, int w, int h, int bg)
    {
        Bitmap outb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        BitmapData sd = src.LockBits(new Rectangle(x, y, w, h),
                                     ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData od = outb.LockBits(new Rectangle(0, 0, w, h),
                                      ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[w * 4];
            for (int j = 0; j < h; j++)
            {
                Marshal.Copy((IntPtr)(sd.Scan0.ToInt64() + (long)j * sd.Stride), row, 0, w * 4);
                for (int i = 0; i < row.Length; i += 4)
                {
                    double inv = 1.0 - (row[i + 3] / 255.0);
                    row[i + 0] = (byte)Math.Min(255.0, row[i + 0] + bg * inv);
                    row[i + 1] = (byte)Math.Min(255.0, row[i + 1] + bg * inv);
                    row[i + 2] = (byte)Math.Min(255.0, row[i + 2] + bg * inv);
                    row[i + 3] = 255;
                }
                Marshal.Copy(row, 0, (IntPtr)(od.Scan0.ToInt64() + (long)j * od.Stride), w * 4);
            }
        }
        finally
        {
            src.UnlockBits(sd);
            outb.UnlockBits(od);
        }
        return outb;
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing -ErrorAction Stop
[GroundCells]::Run($Atlas, $OutDir, $Cols, $Rows, $SmallRows, $Bg)
