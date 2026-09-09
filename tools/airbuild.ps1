# Builds the vanilla-behaviour air textures: one fixed image per material, sized to what
# that material actually draws at.
#
#   blood_cloud -> mist   1024   draws 0.5 units typical, up to 12 on big creatures
#   blood_splat -> splat   256   draws <= 0.6
#   blood_drop  -> drop    128   draws 0.05, and vanilla ships an 8x8 for this class
#
# Replaces the 16-frame 2048 sheet. No texture-sheet animation, no size threshold, no
# random frame pick - that is how vanilla assigns particle textures, one per material.
#
# Tone pass: the source art measures 156-214 mean luminance, which is what blooms pale.
# Vanilla's own blood_cloud texture was a small dark blob, so scaling down to ~140 (the
# ground set's dark range) restores vanilla's darkness rather than departing from it.
# RGB is scaled and clamped to alpha, preserving premultiplication - the source measured
# 0.0-1.5% violation, so it is already premultiplied and must stay that way.
param(
    [string]$Sheet   = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets\blood_droplet.png",
    [string]$OutDir  = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets",
    [string]$Preview = "$env:USERPROFILE\Desktop\Cartur Blood - AIR rework",
    [int]$TargetLum  = 140,
    # Which sheet cell feeds the mist. Row 2 is the mist row; col 2 is the dense cloud and
    # col 3 the wispier, more filamented one.
    [int]$MistCol = 2,
    [int]$MistRow = 2
)

Add-Type -AssemblyName System.Drawing

$code = @'
using System;using System.Drawing;using System.Drawing.Imaging;using System.Drawing.Drawing2D;
using System.IO;using System.Runtime.InteropServices;using System.Text;

public static class AirBuild
{
    // Mean luminance over strongly-covered pixels only. Faint edge pixels would drag the
    // average toward zero and make every image look darker than it reads.
    static double MeanLum(Bitmap b)
    {
        BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[b.Width*4]; double sum=0; long n=0;
            for(int j=0;j<b.Height;j++){
                Marshal.Copy((IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride),row,0,row.Length);
                for(int i=0;i<row.Length;i+=4)
                    if(row[i+3]>200){ sum+=0.299*row[i+2]+0.587*row[i+1]+0.114*row[i]; n++; }
            }
            return n>0?sum/n:0;
        } finally { b.UnlockBits(d); }
    }

    static void Tone(Bitmap b,double factor)
    {
        BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[b.Width*4];
            for(int j=0;j<b.Height;j++){
                IntPtr p=(IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride);
                Marshal.Copy(p,row,0,row.Length);
                for(int i=0;i<row.Length;i+=4){
                    byte a=row[i+3];
                    for(int k=0;k<3;k++){
                        double v=row[i+k]*factor;
                        // Clamp to alpha: premultiplied art can never have a channel above it.
                        row[i+k]=(byte)Math.Min(a,Math.Min(255.0,v));
                    }
                }
                Marshal.Copy(row,0,p,row.Length);
            }
        } finally { b.UnlockBits(d); }
    }

    static Bitmap Cell(Bitmap sheet,int col,int row,int cols,int rows,int outSize)
    {
        int cw=sheet.Width/cols, ch=sheet.Height/rows;
        Bitmap cell=new Bitmap(outSize,outSize,PixelFormat.Format32bppArgb);
        using(Graphics g=Graphics.FromImage(cell)){
            g.CompositingMode=CompositingMode.SourceCopy;
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(sheet,new Rectangle(0,0,outSize,outSize),
                        new Rectangle(col*cw,row*ch,cw,ch),GraphicsUnit.Pixel);
        }
        return cell;
    }

    // A plain soft round droplet, generated rather than cropped. At 0.05 world units this
    // covers a few screen pixels, so a photographic droplet's highlights and rim are pure
    // waste - a radial falloff is all that survives, and vanilla's own equivalent is 8x8.
    static Bitmap Droplet(int size)
    {
        Bitmap b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        BitmapData d=b.LockBits(new Rectangle(0,0,size,size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[size*4];
            double c=(size-1)/2.0, rad=c*0.92;
            for(int j=0;j<size;j++){
                for(int i=0,x=0;i<row.Length;i+=4,x++){
                    double dx=(x-c)/rad, dy=(j-c)/rad;
                    double dist=Math.Sqrt(dx*dx+dy*dy);
                    // Solid core to ~55%, smooth shoulder to the edge.
                    double a = dist>=1.0 ? 0 : (dist<=0.55 ? 1.0 : 1.0-((dist-0.55)/0.45));
                    a=a*a*(3-2*a);                       // smoothstep
                    byte av=(byte)Math.Round(a*255);
                    // Slight vertical shading so it does not read as a flat disc, then
                    // premultiplied on the way out.
                    double lum=(0.78+0.22*(1.0-(j/(double)size)))*(140.0/255.0);
                    byte v=(byte)Math.Min(av,Math.Round(lum*av));
                    row[i]=v;row[i+1]=v;row[i+2]=v;row[i+3]=av;
                }
                Marshal.Copy(row,0,(IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride),row.Length);
            }
        } finally { b.UnlockBits(d); }
        return b;
    }

    public static string Run(string sheetPath,string outDir,string previewDir,int targetLum,
                             int mistCol,int mistRow)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(previewDir);
        StringBuilder log=new StringBuilder();

        using(Bitmap sheet=new Bitmap(sheetPath))
        {
            // (name, sourceCol, sourceRow, outputSize) - row 2 is the mist row, row 1 col 0
            // is the lumpy irregular blob that suits a small splat.
            object[][] jobs = new object[][] {
                new object[]{ "blood_mist.png",  mistCol, mistRow, 1024 },
                new object[]{ "blood_splat.png", 0, 1,  256 },
            };

            foreach(object[] job in jobs)
            {
                string name=(string)job[0];
                using(Bitmap cell=Cell(sheet,(int)job[1],(int)job[2],4,4,(int)job[3]))
                {
                    double before=MeanLum(cell);
                    if(before>1) Tone(cell,targetLum/before);
                    double after=MeanLum(cell);
                    cell.Save(Path.Combine(outDir,name),ImageFormat.Png);
                    cell.Save(Path.Combine(previewDir,name),ImageFormat.Png);
                    log.AppendLine(string.Format("{0,-17} {1}px  lum {2:0.0} -> {3:0.0}",
                        name,(int)job[3],before,after));
                }
            }

            using(Bitmap drop=Droplet(128))
            {
                drop.Save(Path.Combine(outDir,"blood_drops.png"),ImageFormat.Png);
                drop.Save(Path.Combine(previewDir,"blood_drops.png"),ImageFormat.Png);
                log.AppendLine(string.Format("{0,-17} {1}px  lum {2:0.0}  (generated)",
                    "blood_drops.png",128,MeanLum(drop)));
            }
        }
        return log.ToString();
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing -ErrorAction Stop
[AirBuild]::Run($Sheet, $OutDir, $Preview, $TargetLum, $MistCol, $MistRow)
