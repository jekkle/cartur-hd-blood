# Simulates what a creature death actually looks like, instead of judging a texture flat.
#
# The graph says vanilla fires 88 blood_cloud particles per death across three systems
# (soft cloud 30, splat 50, blobs 8) at 0.5 units. A texture that reads fine on its own
# can still bloom into a white cloud when 88 copies overlap, which is exactly the
# "explosion of mist" complaint - so the honest test is to stack 88 of them.
#
# Compositing is premultiplied: canvas = canvas*(1-a) + rgb*tint, the same operation the
# particle shader performs. Tint stands in for the per-creature startColor Valheim
# multiplies in (neck green, greydwarf yellow, boar red).
param(
    [string]$Ours  = "D:\Ai\Modding\Valheim\cartur-hd-blood\src\Assets\blood_mist.png",
    [string]$Their = "$env:USERPROFILE\Desktop\Valheim VFX Textures\BLOOD ONLY\1024x1024_brains-7264202d.png",
    [string]$Out   = "$env:USERPROFILE\Desktop\Cartur Blood - AIR rework\MIST - 88 puffs, theirs vs ours.png",
    [int]$Count = 88,
    [int]$Seed  = 1337,
    # Labels must be passed, not assumed. An earlier run compared two of our own variants
    # while the panels still read "HDVT" and "OURS", which is exactly the kind of artifact
    # that outlives the conversation that produced it.
    [string]$LabelLeft   = "HDVT  brains-7264202d  1024",
    [string]$SubLeft     = "what is drawing now",
    [string]$LabelRight  = "OURS  blood_mist  1024, toned",
    [string]$SubRight    = ""
)

Add-Type -AssemblyName System.Drawing

$code = @'
using System;using System.Drawing;using System.Drawing.Imaging;using System.Drawing.Drawing2D;
using System.IO;using System.Runtime.InteropServices;using System.Text;

public static class MistSim
{
    class Img { public int W,H; public byte[] P; }   // BGRA, premultiplied

    static Img Load(string path,int size)
    {
        using(Bitmap src=new Bitmap(path))
        using(Bitmap b=new Bitmap(size,size,PixelFormat.Format32bppArgb))
        {
            using(Graphics g=Graphics.FromImage(b)){
                g.CompositingMode=CompositingMode.SourceCopy;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(src,new Rectangle(0,0,size,size));
            }
            Img im=new Img{W=size,H=size,P=new byte[size*size*4]};
            BitmapData d=b.LockBits(new Rectangle(0,0,size,size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{
                for(int j=0;j<size;j++)
                    Marshal.Copy((IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride),im.P,j*size*4,size*4);
            } finally { b.UnlockBits(d); }
            return im;
        }
    }

    // Straight-alpha sources (HDVT's brains is one) must be premultiplied before they can
    // be composited correctly, otherwise the halo is exactly the pale bloom we are testing
    // for and the comparison would be measuring our own mistake instead of theirs.
    static void Premultiply(Img im)
    {
        long viol=0,cov=0;
        for(int i=0;i<im.P.Length;i+=4){
            byte a=im.P[i+3];
            if(a>16)cov++;
            if(Math.Max(im.P[i],Math.Max(im.P[i+1],im.P[i+2]))>a+8)viol++;
        }
        if(cov==0 || viol*100.0/cov<=5) return;          // already premultiplied
        for(int i=0;i<im.P.Length;i+=4){
            double a=im.P[i+3]/255.0;
            for(int k=0;k<3;k++) im.P[i+k]=(byte)Math.Round(im.P[i+k]*a);
        }
    }

    static Bitmap Sim(Img mist,int count,int canvas,int puff,int seed,
                      double tr,double tg,double tb,int bg)
    {
        double[] acc=new double[canvas*canvas*3];
        for(int i=0;i<acc.Length;i++) acc[i]=bg;

        Random rng=new Random(seed);
        for(int n=0;n<count;n++)
        {
            // Tight cluster: these all spawn at one death point.
            double sc=0.55+rng.NextDouble()*0.75;
            int size=(int)(puff*sc);
            double ang=rng.NextDouble()*Math.PI*2, rr=Math.Pow(rng.NextDouble(),0.6)*canvas*0.20;
            int ox=(int)(canvas/2.0+Math.Cos(ang)*rr-size/2.0);
            int oy=(int)(canvas/2.0+Math.Sin(ang)*rr-size/2.0);

            for(int j=0;j<size;j++)
            {
                int cy=oy+j; if(cy<0||cy>=canvas) continue;
                int sy=(int)((long)j*mist.H/size);
                for(int i=0;i<size;i++)
                {
                    int cx=ox+i; if(cx<0||cx>=canvas) continue;
                    int sx=(int)((long)i*mist.W/size);
                    int si=(sy*mist.W+sx)*4;
                    double a=mist.P[si+3]/255.0;
                    if(a<=0.002) continue;
                    int ci=(cy*canvas+cx)*3;
                    acc[ci+0]=acc[ci+0]*(1-a)+mist.P[si+0]*tb;
                    acc[ci+1]=acc[ci+1]*(1-a)+mist.P[si+1]*tg;
                    acc[ci+2]=acc[ci+2]*(1-a)+mist.P[si+2]*tr;
                }
            }
        }

        Bitmap outb=new Bitmap(canvas,canvas,PixelFormat.Format32bppArgb);
        BitmapData d=outb.LockBits(new Rectangle(0,0,canvas,canvas),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{
            byte[] row=new byte[canvas*4];
            for(int j=0;j<canvas;j++){
                for(int i=0;i<canvas;i++){
                    int ci=(j*canvas+i)*3;
                    row[i*4+0]=(byte)Math.Min(255,Math.Max(0,acc[ci+0]));
                    row[i*4+1]=(byte)Math.Min(255,Math.Max(0,acc[ci+1]));
                    row[i*4+2]=(byte)Math.Min(255,Math.Max(0,acc[ci+2]));
                    row[i*4+3]=255;
                }
                Marshal.Copy(row,0,(IntPtr)(d.Scan0.ToInt64()+(long)j*d.Stride),row.Length);
            }
        } finally { outb.UnlockBits(d); }
        return outb;
    }

    public static string Run(string ours,string theirs,string outPath,int count,int seed,
                             string labL,string subL,string labR,string subR)
    {
        int canvas=560, puff=300, bg=120;               // bg ~ dirt/grass value
        double tr=0.62, tg=0.10, tb=0.09;               // red creature tint

        Img a=Load(theirs,512); Premultiply(a);
        Img b=Load(ours,512);   Premultiply(b);

        using(Bitmap left=Sim(a,count,canvas,puff,seed,tr,tg,tb,bg))
        using(Bitmap right=Sim(b,count,canvas,puff,seed,tr,tg,tb,bg))
        using(Bitmap sheet=new Bitmap(canvas*2+30,canvas+64,PixelFormat.Format32bppArgb))
        using(Graphics g=Graphics.FromImage(sheet))
        using(Font f=new Font("Segoe UI",17,FontStyle.Bold))
        using(Font fs=new Font("Segoe UI",12,FontStyle.Regular))
        {
            g.Clear(Color.FromArgb(255,32,32,32));
            g.DrawImage(left,0,58); g.DrawImage(right,canvas+30,58);
            g.DrawString(labL,f,Brushes.White,4,6);
            g.DrawString(subL,fs,Brushes.Gainsboro,4,34);
            g.DrawString(labR,f,Brushes.White,canvas+34,6);
            g.DrawString(string.IsNullOrEmpty(subR)
                            ? "same "+count+" puffs, same seed, same tint" : subR,
                         fs,Brushes.Gainsboro,canvas+34,34);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            sheet.Save(outPath,ImageFormat.Png);
        }
        return "wrote "+outPath;
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing -ErrorAction Stop
[MistSim]::Run($Ours, $Their, $Out, $Count, $Seed, $LabelLeft, $SubLeft, $LabelRight, $SubRight)

