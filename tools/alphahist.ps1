param([string]$Dir)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class AlphaHist
{
    // Looks for a rendered backdrop card - a large region of near-constant mid alpha with
    // straight, axis-aligned edges. Some of these images have a grey rectangle behind the
    // subject, and because alpha tracks brightness that rectangle arrives as semi-transparent
    // coverage. It would render in game as a translucent square of blood around the splat.
    //
    // Two signals: a spike in the mid-alpha band, and long perfectly straight runs where alpha
    // steps across a column or row. A real splat rim is irregular and never does that.
    public static string Analyse(string path)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[bd.Stride * h];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            bmp.UnlockBits(bd);

            long zero = 0, low = 0, mid = 0, high = 0, full = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte a = px[y * bd.Stride + x * 4 + 3];
                    if (a == 0) zero++;
                    else if (a < 40) low++;
                    else if (a < 150) mid++;
                    else if (a < 250) high++;
                    else full++;
                }

            // Straight-edge test: for each column, how many rows have alpha>20 there? A backdrop
            // rectangle makes many adjacent columns share an identical tall count.
            int[] colCount = new int[w];
            for (int x = 0; x < w; x++)
            {
                int c = 0;
                for (int y = 0; y < h; y++)
                    if (px[y * bd.Stride + x * 4 + 3] > 20) c++;
                colCount[x] = c;
            }

            int longestRun = 0, run = 1;
            for (int x = 1; x < w; x++)
            {
                if (Math.Abs(colCount[x] - colCount[x - 1]) <= 2 && colCount[x] > h / 4)
                    run++;
                else
                { if (run > longestRun) longestRun = run; run = 1; }
            }
            if (run > longestRun) longestRun = run;

            double total = w * (double)h;
            string verdict = (mid / total > 0.12 && longestRun > w / 5)
                ? "BACKDROP LIKELY"
                : "clean";

            return string.Format(
                "a=0 {0,5:0.#}%  1-39 {1,4:0.#}%  40-149 {2,5:0.#}%  150-249 {3,4:0.#}%  250+ {4,5:0.#}%  flatRun {5,4}px  {6}",
                100*zero/total, 100*low/total, 100*mid/total, 100*high/total, 100*full/total, longestRun, verdict);
        }
    }
}
'@

foreach ($f in Get-ChildItem -Path $Dir -Filter *.png | Sort-Object Name) {
    Write-Output ("{0,-16} {1}" -f $f.Name, [AlphaHist]::Analyse($f.FullName))
}
