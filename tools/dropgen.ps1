param(
    [string]$Out,
    [int]$Size = 256,
    [int]$Seed = 7
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DropGen
{
    // An airborne blood droplet for the particle spray.
    //
    // Generated rather than cropped out of a photo or an AI image because the shape is simple
    // enough to describe exactly, and doing it this way gives a correct alpha channel by
    // construction - no keying, no dark-pixels-lost-to-the-background problem.
    //
    // The particles render as view-aligned billboards, so the shape must read from any angle:
    // a slightly irregular blob, not a teardrop with an implied direction. Irregularity comes
    // from a couple of sine harmonics on the radius, which is enough to stop it looking like a
    // machine-drawn circle without introducing a preferred axis.
    //
    // White RGB with the shape entirely in alpha, same as the ground decal, so each creature's
    // startColor still tints its own blood.
    public static string Build(string path, int size, int seed)
    {
        var rand = new Random(seed);
        double half = size / 2.0;
        double baseR = half * 0.62;

        // harmonic wobble: amplitude, frequency, phase
        double a1 = 0.10, f1 = 3 + rand.NextDouble() * 2, p1 = rand.NextDouble() * Math.PI * 2;
        double a2 = 0.05, f2 = 7 + rand.NextDouble() * 3, p2 = rand.NextDouble() * Math.PI * 2;

        // one small satellite, offset enough to read as a separate speck
        double satAng = rand.NextDouble() * Math.PI * 2;
        double satDist = half * 0.72;
        double satR = half * 0.10;
        double satX = half + Math.Cos(satAng) * satDist;
        double satY = half + Math.Sin(satAng) * satDist;

        double feather = size * 0.018;   // rim softness in pixels; small keeps the edge wet-looking

        byte[] px = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x + 0.5 - half;
                double dy = y + 0.5 - half;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double ang = Math.Atan2(dy, dx);

                double r = baseR * (1.0 + a1 * Math.Sin(f1 * ang + p1) + a2 * Math.Sin(f2 * ang + p2));
                double cov = Clamp01((r - dist) / feather + 0.5);

                double sdx = x + 0.5 - satX;
                double sdy = y + 0.5 - satY;
                double sdist = Math.Sqrt(sdx * sdx + sdy * sdy);
                double satCov = Clamp01((satR - sdist) / feather + 0.5);

                double a = Math.Max(cov, satCov);

                // smoothstep, so the rim reads as a curved surface rather than a linear ramp
                a = a * a * (3 - 2 * a);

                int i = (y * size + x) * 4;
                px[i] = 255; px[i+1] = 255; px[i+2] = 255;
                px[i+3] = (byte)Math.Round(a * 255);
            }

        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            Rectangle rect = new Rectangle(0, 0, size, size);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size; y++)
                Marshal.Copy(px, y * size * 4, (IntPtr)(bd.Scan0.ToInt64() + y * bd.Stride), size * 4);
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }

        return "ok " + size + "x" + size;
    }

    private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
}
'@

$r = [DropGen]::Build($Out, $Size, $Seed)
Write-Output "$r -> $Out"
