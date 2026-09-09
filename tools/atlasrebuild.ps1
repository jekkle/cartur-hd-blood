# Rebuilds the ground atlas from a chosen subset of the old one's cells.
#
# The owner reviewed all 16 cells and cut 10: the round blobs, both diagonal streaks
# (those belong in the air sheet, which already carries them), and six others. What
# survives is 5 large and 2 small.
#
# Grid has to fit exactly. Empty cells are not harmless - Variants.Build makes a material
# per cell and an empty one produces an invisible decal, so every slot must hold art.
# 3x3 of 1024px is the tightest fit for 5 large + 2 small: rows 0-1 are the large set
# (6 slots) and row 2 is the small set (3 slots), matching Variants.Build's rule that the
# LAST row is the small set. That leaves one duplicate in each group, which only weights
# the random pick - cell 1 is duplicated because it is the most irregular of the large
# survivors (aspect 1.59), and repaired 16 because it is the only small shape that is not
# a starburst.
#
# Cell 16 is repaired rather than dropped: its alpha is sound (corr(alpha,lum) 0.93,
# against 0.99 for known-good cell 15 - high correlation is the signature of
# premultiplied art, not a defect) but 36% of its covered pixels have a channel above
# alpha, which premultiplied art can never have. So it is premultiplied and toned, not
# alpha-rebuilt.
#
# Normal-map cells are copied verbatim. Premultiplying or toning a normal map is
# meaningless - those are encoded vectors, not colour.
param(
    [string]$SrcAlbedo = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets\blood_splat_atlas.png",
    [string]$SrcNormal = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets\blood_splat_atlas_n.png",
    [string]$OutDir    = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets",
    [string]$Preview   = "$env:USERPROFILE\Desktop\Cartur Blood - GROUND cells",
    # Source cell numbers (1-16, reading order) in target reading order. Last row = small.
    [int[]]$Layout     = @(1,2,5, 7,8,1, 15,16,16),
    [int]$Cols = 3,
    [int]$Rows = 3,
    [int]$Cell = 1024,
    # Cells needing premultiplication repair.
    [int[]]$RepairCells = @(16),
    [int]$RepairLum = 145,
    # Tone EVERY cell to this mean luminance, 0 to leave each as authored.
    #
    # The surviving cells were authored across a 135-201 range, which reads as some splats
    # being wet blood and others being pale wash - the inconsistency the owner noticed.
    # Normalising is what makes a mixed set look like one set.
    [int]$ToneAll = 0
)

Add-Type -AssemblyName System.Drawing

$code = @'
using System;using System.Drawing;using System.Drawing.Imaging;using System.Drawing.Drawing2D;
using System.IO;using System.Runtime.InteropServices;using System.Text;

public static class AtlasRebuild
{
    static Bitmap Cut(Bitmap src,int idx,int srcCols,int srcRows,int cell)
    {
        int c=(idx-1)%srcCols, r=(idx-1)/srcCols;
        int cw=src.Width/srcCols, ch=src.Height/srcRows;
        Bitmap b=new Bitmap(cell,cell,PixelFormat.Format32bppArgb);
        using(Graphics g=Graphics.FromImage(b)){
            g.CompositingMode=CompositingMode.SourceCopy;
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(src,new Rectangle(0,0,cell,cell),
                        new Rectangle(c*cw,r*ch,cw,ch),GraphicsUnit.Pixel);
        }
        return b;
    }

    static double MeanLum(Bitmap b)
    {
        BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[b.Width*4]; double s=0; long n=0;
            for(int j=0;j<b.Height;j++){
                Marshal.Copy((IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride),row,0,row.Length);
                for(int i=0;i<row.Length;i+=4)
                    if(row[i+3]>200){ s+=0.299*row[i+2]+0.587*row[i+1]+0.114*row[i]; n++; }
            }
            return n>0?s/n:0;
        } finally { b.UnlockBits(d); }
    }

    static string Repair(Bitmap b,int targetLum)
    {
        // Pass 1: premultiply. Pass 2: scale to the target luminance, clamped to alpha so
        // the invariant that was just restored cannot be broken again.
        BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        long viol=0,cov=0;
        try{
            byte[] row=new byte[b.Width*4];
            for(int j=0;j<b.Height;j++){
                IntPtr p=(IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride);
                Marshal.Copy(p,row,0,row.Length);
                for(int i=0;i<row.Length;i+=4){
                    byte a=row[i+3];
                    if(a>16){ cov++;
                        if(Math.Max(row[i+2],Math.Max(row[i+1],row[i]))>a+8) viol++; }
                    double f=a/255.0;
                    for(int k=0;k<3;k++) row[i+k]=(byte)Math.Min(a,Math.Round(row[i+k]*f));
                }
                Marshal.Copy(row,0,p,row.Length);
            }
        } finally { b.UnlockBits(d); }

        double before=MeanLum(b);
        if(before>1){
            double factor=targetLum/before;
            BitmapData d2=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try{
                byte[] row=new byte[b.Width*4];
                for(int j=0;j<b.Height;j++){
                    IntPtr p=(IntPtr)(d2.Scan0.ToInt64()+(long)j*d2.Stride);
                    Marshal.Copy(p,row,0,row.Length);
                    for(int i=0;i<row.Length;i+=4){
                        byte a=row[i+3];
                        for(int k=0;k<3;k++)
                            row[i+k]=(byte)Math.Min(a,Math.Min(255.0,row[i+k]*factor));
                    }
                    Marshal.Copy(row,0,p,row.Length);
                }
            } finally { b.UnlockBits(d2); }
        }
        return string.Format("premultiplied ({0:0.0}% violating), lum {1:0.0} -> {2:0.0}",
                             cov>0?viol*100.0/cov:0, before, MeanLum(b));
    }

    static string ToneTo(Bitmap b,int targetLum)
    {
        double before=MeanLum(b);
        if(before<=1) return "";
        double factor=targetLum/before;
        BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[b.Width*4];
            for(int j=0;j<b.Height;j++){
                IntPtr p=(IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride);
                Marshal.Copy(p,row,0,row.Length);
                for(int i=0;i<row.Length;i+=4){
                    byte a=row[i+3];
                    // Clamped to alpha so premultiplication survives the scale.
                    for(int k=0;k<3;k++)
                        row[i+k]=(byte)Math.Min(a,Math.Min(255.0,row[i+k]*factor));
                }
                Marshal.Copy(row,0,p,row.Length);
            }
        } finally { b.UnlockBits(d); }
        return string.Format("  lum {0:0.0} -> {1:0.0}",before,MeanLum(b));
    }

    public static string Run(string srcA,string srcN,string outDir,string previewDir,
                             int[] layout,int cols,int rows,int cell,
                             int[] repairCells,int repairLum,int toneAll)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(previewDir);
        StringBuilder log=new StringBuilder();

        int srcCols=4, srcRows=4;
        int w=cols*cell, h=rows*cell;

        // Source and destination are the same path. new Bitmap(path) holds a file lock for
        // the lifetime of the object, so saving over it inside the using block fails with
        // GDI+'s uninformative "a generic error occurred". Write to a temp file, let the
        // handle close, then move it into place.
        string tmpA=Path.Combine(outDir,"_rebuild_albedo.png");
        string tmpN=Path.Combine(outDir,"_rebuild_normal.png");
        string dstA=Path.Combine(outDir,"blood_splat_atlas.png");
        string dstN=Path.Combine(outDir,"blood_splat_atlas_n.png");

        using(Bitmap sa=new Bitmap(srcA))
        using(Bitmap albedo=new Bitmap(w,h,PixelFormat.Format32bppArgb))
        using(Graphics ga=Graphics.FromImage(albedo))
        {
            ga.CompositingMode=CompositingMode.SourceCopy;
            for(int i=0;i<layout.Length;i++){
                int tc=i%cols, tr=i/cols;
                using(Bitmap c=Cut(sa,layout[i],srcCols,srcRows,cell)){
                    string note="";
                    if(Array.IndexOf(repairCells,layout[i])>=0)
                        note="  REPAIRED: "+Repair(c,repairLum);
                    if(toneAll>0)
                        note+="  TONED:"+ToneTo(c,toneAll);
                    ga.DrawImage(c,tc*cell,tr*cell,cell,cell);
                    log.AppendLine(string.Format("slot {0,2} (col{1} row{2}) <- cell {3,2}{4}{5}",
                        i+1,tc,tr,layout[i], tr==rows-1?"  [small]":"          ", note));
                }
            }
            albedo.Save(tmpA,ImageFormat.Png);
        }
        File.Copy(tmpA,dstA,true); File.Delete(tmpA);

        if(File.Exists(srcN))
        {
            using(Bitmap sn=new Bitmap(srcN))
            using(Bitmap normal=new Bitmap(w,h,PixelFormat.Format32bppArgb))
            using(Graphics gn=Graphics.FromImage(normal))
            {
                gn.CompositingMode=CompositingMode.SourceCopy;
                for(int i=0;i<layout.Length;i++){
                    int tc=i%cols, tr=i/cols;
                    using(Bitmap c=Cut(sn,layout[i],srcCols,srcRows,cell))
                        gn.DrawImage(c,tc*cell,tr*cell,cell,cell);
                }
                normal.Save(tmpN,ImageFormat.Png);
            }
            File.Copy(tmpN,dstN,true); File.Delete(tmpN);
            log.AppendLine("normal atlas rebuilt to the same layout (cells copied verbatim).");
        }

        log.AppendLine(string.Format("atlas now {0}x{1}, {2} cells of {3}px, grid {4}x{5}.",
                                     w,h,layout.Length,cell,cols,rows));
        return log.ToString();
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing -ErrorAction Stop
[AtlasRebuild]::Run($SrcAlbedo,$SrcNormal,$OutDir,$Preview,$Layout,$Cols,$Rows,$Cell,$RepairCells,$RepairLum,$ToneAll)
