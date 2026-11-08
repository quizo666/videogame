using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PixelForge {
public sealed class AmbientRegion {
    public string Id=Guid.NewGuid().ToString("N"),Name="Область",MaskAsset;
    public int Effect=0,Cycles=1;
    public double Amplitude=1,Lightness=5,Phase=0;
    public bool Enabled=true;
    [ScriptIgnore] public bool[] Mask {get;set;}
    byte[] distance;int width,height;Rect bounds;
    public void InvalidateMask(){distance=null;}
    public void Prepare(int w,int h){
        if(distance!=null&&w==width&&h==height)return;width=w;height=h;distance=new byte[w*h];var q=new Queue<int>();int left=w,right=-1,top=h,bottom=-1;
        for(int i=0;i<Mask.Length;i++){if(!Mask[i])continue;int x=i%w,y=i/w;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);bool edge=x==0||y==0||x==w-1||y==h-1||!Mask[i-1]||!Mask[i+1]||!Mask[i-w]||!Mask[i+w];distance[i]=(byte)(edge?1:255);if(edge)q.Enqueue(i);}
        while(q.Count>0){int i=q.Dequeue();if(distance[i]>=3)continue;int x=i%w,y=i/w;Action<int> add=n=>{if(distance[n]==255){distance[n]=(byte)(distance[i]+1);q.Enqueue(n);}};if(x>0)add(i-1);if(x<w-1)add(i+1);if(y>0)add(i-w);if(y<h-1)add(i+w);}
        bounds=right<left?Rect.Empty:new Rect(left,top,right-left+1,bottom-top+1);
    }
    public double Edge(int pixel){return Math.Min(1,distance[pixel]/2.0);}
    [ScriptIgnore] public Rect Bounds {get{return bounds;}}
}
public sealed class AmbientProject {
    public int Version=1,Width=320,Height=180,Colors=64,Fps=12,OutputWidth=1280,OutputHeight=720;
    public double Duration=6;
    public string Name="Фон из фото",Background="#172235",LibraryId;
    public bool Cover=true;
    public List<AmbientRegion> Regions=new List<AmbientRegion>();
    [ScriptIgnore] public Frame Photo {get;set;}
    [ScriptIgnore] public Frame Base {get;set;}
    [ScriptIgnore] public int FrameCount {get{return Math.Max(2,(int)Math.Round(Duration*Fps));}}
    public void Validate(){
        if(Version!=1||Width<16||Height<16||Width>640||Height>640||(long)Width*Height>262144||Colors<16||Colors>256||Fps<1||Fps>60||double.IsNaN(Duration)||double.IsInfinity(Duration)||Duration<.25||Duration>20||FrameCount>240||(long)Width*Height*FrameCount>64000000)throw new Exception("Холст: 16…640 px, до 262144 пикселей; 16…256 цветов; 0.25…20 с; до 240 кадров и 64 млн пикселей анимации.");
        new Cutscene {Width=Width,Height=Height,OutputWidth=OutputWidth,OutputHeight=OutputHeight,Duration=1,Crossfade=0}.Validate();ColorConverter.ConvertFromString(Background);
        if(Regions==null||Regions.Count>32)throw new Exception("До 32 областей.");foreach(var r in Regions)if(r==null||r.Effect<0||r.Effect>2||r.Cycles<1||r.Cycles>8||!Finite(r.Amplitude)||r.Amplitude<0||r.Amplitude>6||!Finite(r.Lightness)||r.Lightness<0||r.Lightness>30||!Finite(r.Phase)||Math.Abs(r.Phase)>3600||r.Mask==null||r.Mask.Length!=Width*Height)throw new Exception("Некорректная область: амплитуда 0…6 px, свет 0…30%, циклы 1…8, маска по размеру холста.");
    }
    static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
}
public static class AmbientEngine {
    public static AmbientProject Clone(AmbientProject p){var copy=new JavaScriptSerializer().Deserialize<AmbientProject>(new JavaScriptSerializer().Serialize(p));copy.Photo=p.Photo;copy.Base=p.Base;for(int n=0;n<copy.Regions.Count;n++)copy.Regions[n].Mask=(bool[])p.Regions[n].Mask.Clone();return copy;}
    public static Frame Pixelize(AmbientProject p,CancellationToken token){
        if(p.Photo==null)throw new Exception("Загрузите фото.");var source=p.Photo;var result=new Frame(p.Width,p.Height);var bg=(Color)ColorConverter.ConvertFromString(p.Background);
        double scale=p.Cover?Math.Max(p.Width/(double)source.W,p.Height/(double)source.H):Math.Min(p.Width/(double)source.W,p.Height/(double)source.H),ox=(p.Width-source.W*scale)/2,oy=(p.Height-source.H*scale)/2;
        for(int y=0;y<p.Height;y++){token.ThrowIfCancellationRequested();for(int x=0;x<p.Width;x++){int d=(y*p.Width+x)*4;result.Data[d]=bg.B;result.Data[d+1]=bg.G;result.Data[d+2]=bg.R;result.Data[d+3]=255;double cx=(x+.5-ox)/scale,cy=(y+.5-oy)/scale;if(cx<0||cy<0||cx>=source.W||cy>=source.H)continue;
            int x0=Math.Max(0,(int)Math.Floor((x-ox)/scale)),x1=Math.Min(source.W,Math.Max(x0+1,(int)Math.Ceiling((x+1-ox)/scale))),y0=Math.Max(0,(int)Math.Floor((y-oy)/scale)),y1=Math.Min(source.H,Math.Max(y0+1,(int)Math.Ceiling((y+1-oy)/scale)));long b=0,g=0,r=0,n=0;
            for(int sy=y0;sy<y1;sy++)for(int sx=x0;sx<x1;sx++){int i=(sy*source.W+sx)*4,a=source.Data[i+3];b+=(source.Data[i]*a+bg.B*(255-a))/255;g+=(source.Data[i+1]*a+bg.G*(255-a))/255;r+=(source.Data[i+2]*a+bg.R*(255-a))/255;n++;}if(n>0){result.Data[d]=(byte)(b/n);result.Data[d+1]=(byte)(g/n);result.Data[d+2]=(byte)(r/n);}
        }}return TemporalStabilizer.QuantizeFrame(result,p.Colors);
    }
    public static void ResizeMasks(AmbientProject p,int oldW,int oldH){if(oldW==p.Width&&oldH==p.Height)return;foreach(var r in p.Regions){var mask=new bool[p.Width*p.Height];for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++)mask[y*p.Width+x]=r.Mask[Math.Min(oldH-1,(int)((y+.5)*oldH/p.Height))*oldW+Math.Min(oldW-1,(int)((x+.5)*oldW/p.Width))];r.Mask=mask;r.InvalidateMask();}}
    public static Frame Render(AmbientProject p,double time){
        if(p.Base==null)throw new Exception("Сначала пикселизируйте фото.");var image=p.Base.Clone();double u=((time%p.Duration)+p.Duration)%p.Duration/p.Duration;
        for(int pass=0;pass<2;pass++)foreach(var r in p.Regions){if(!r.Enabled||(pass==0?r.Effect==2:r.Effect!=2))continue;r.Prepare(p.Width,p.Height);if(r.Bounds.IsEmpty)continue;double phase=2*Math.PI*r.Cycles*u+r.Phase*Math.PI/180;
            for(int y=(int)r.Bounds.Top;y<r.Bounds.Bottom;y++)for(int x=(int)r.Bounds.Left;x<r.Bounds.Right;x++){int pixel=y*p.Width+x;if(!r.Mask[pixel])continue;int dst=pixel*4;double edge=r.Edge(pixel);
                if(r.Effect==2){double amount=r.Lightness/100*Math.Sin(phase)*edge;for(int c=0;c<3;c++){double v=image.Data[dst+c];image.Data[dst+c]=(byte)Math.Round(amount>=0?v+(255-v)*amount:v*(1+amount));}continue;}
                double wave=r.Effect==0?Math.Sin(phase+(y-r.Bounds.Top)*.35):Math.Sin(phase+(x-r.Bounds.Left)*.07)*Math.Pow(Math.Max(0,(r.Bounds.Bottom-1-y)/Math.Max(1,r.Bounds.Height-1)),1.5);
                int dx=(int)Math.Round(r.Amplitude*wave*edge),sx=x-dx;if(sx<0||sx>=p.Width||!r.Mask[y*p.Width+sx])continue;int src=(y*p.Width+sx)*4;for(int c=0;c<4;c++)image.Data[dst+c]=p.Base.Data[src+c];
            }
        }return image;
    }
    public static List<Frame> Frames(AmbientProject p,CancellationToken token){p.Validate();var frames=new List<Frame>();for(int n=0;n<p.FrameCount;n++){token.ThrowIfCancellationRequested();frames.Add(Render(p,n/(double)p.Fps));}return frames;}
    public static Cutscene ToScene(AmbientProject p,List<Frame> frames){var scene=new Cutscene {Width=p.Width,Height=p.Height,OutputWidth=p.OutputWidth,OutputHeight=p.OutputHeight,Fps=p.Fps,Duration=frames.Count/(double)p.Fps,Background=p.Background,Seamless=true,Crossfade=0};scene.Layers.Add(new SceneLayer {Name=p.Name,Frames=frames,Fps=p.Fps,Duration=scene.Duration,LabelColor="#2E967D",Loop=true,ProceduralLoop=true});return scene;}
}
public static class AmbientFile {
    static void Image(ZipArchive zip,string path,Frame f){using(var entry=zip.CreateEntry(path,CompressionLevel.Fastest).Open())using(var buffer=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(f.Bitmap()));encoder.Save(buffer);buffer.Position=0;buffer.CopyTo(entry);}}
    static Frame Image(ZipArchive zip,string path){var e=zip.GetEntry(path);if(e==null||e.Length>128000000)throw new Exception("Нет изображения в проекте.");using(var s=e.Open())using(var buffer=new MemoryStream()){s.CopyTo(buffer);buffer.Position=0;var b=BitmapFrame.Create(buffer,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);if((long)b.PixelWidth*b.PixelHeight>32000000)throw new Exception("Фото превышает 32 млн пикселей.");return Frame.From(b);}}
    public static void Save(AmbientProject p,string path){p.Validate();if(p.Photo==null||p.Base==null)throw new Exception("Нет подготовленного фото.");string stage=path+".writing-"+Guid.NewGuid().ToString("N");try{using(var zip=ZipFile.Open(stage,ZipArchiveMode.Create)){Image(zip,"photo.png",p.Photo);Image(zip,"base.png",p.Base);for(int n=0;n<p.Regions.Count;n++){var r=p.Regions[n];r.MaskAsset="masks/"+n.ToString("D3")+".bin";using(var s=zip.CreateEntry(r.MaskAsset,CompressionLevel.Optimal).Open()){var bytes=r.Mask.Select(v=>(byte)(v?1:0)).ToArray();s.Write(bytes,0,bytes.Length);}}using(var w=new StreamWriter(zip.CreateEntry("background.json").Open()))w.Write(new JavaScriptSerializer().Serialize(p));}if(File.Exists(path))File.Replace(stage,path,null);else File.Move(stage,path);}finally{if(File.Exists(stage))File.Delete(stage);}}
    public static AmbientProject Load(string path){using(var zip=ZipFile.OpenRead(path)){var e=zip.GetEntry("background.json");if(e==null||e.Length>2000000)throw new Exception("Неизвестный проект фона.");AmbientProject p;using(var r=new StreamReader(e.Open()))p=new JavaScriptSerializer().Deserialize<AmbientProject>(r.ReadToEnd());if(p==null||p.Width<16||p.Height<16||p.Width>640||p.Height>640||p.Regions==null||p.Regions.Count>32)throw new Exception("Некорректный проект.");foreach(var region in p.Regions){if(region.MaskAsset==null||!region.MaskAsset.StartsWith("masks/",StringComparison.Ordinal)||region.MaskAsset.Contains(".."))throw new Exception("Некорректная маска.");var mask=zip.GetEntry(region.MaskAsset);if(mask==null||mask.Length!=p.Width*p.Height)throw new Exception("Маска повреждена.");using(var s=mask.Open()){region.Mask=new bool[p.Width*p.Height];for(int n=0;n<region.Mask.Length;n++){int b=s.ReadByte();if(b<0||b>1)throw new Exception("Маска повреждена.");region.Mask[n]=b==1;}}}p.Validate();p.Photo=Image(zip,"photo.png");p.Base=Image(zip,"base.png");if(p.Base.W!=p.Width||p.Base.H!=p.Height)throw new Exception("Размер холста повреждён.");if(string.IsNullOrEmpty(p.LibraryId))p.LibraryId=BackgroundLibrary.FileId(path);return p;}}
}
public sealed class AmbientCanvas : FrameworkElement {
    public Frame Image;public bool[] Mask;public bool ShowMask=true;public int Radius=8;public Action BeforeStroke,StrokeFinished;public Action<int,int,bool> Paint;
    double zoom,panX,panY;bool drawing,panning;Point last,panStart;double startX,startY;BitmapSource overlay;
    public AmbientCanvas(){ClipToBounds=true;Focusable=true;Cursor=Cursors.Cross;ToolTip="ЛКМ — область анимации, ПКМ — стереть маску. Колесо — масштаб; средняя кнопка — перемещение холста.";
        MouseWheel+=(s,e)=>{if(Image==null)return;var old=Area();var point=e.GetPosition(this);double z=old.Width/Image.W,x=(point.X-old.X)/z,y=(point.Y-old.Y)/z;zoom=Math.Max(.05,Math.Min(32,z*(e.Delta>0?1.25:.8)));panX=point.X-ActualWidth/2-(x-Image.W/2.0)*zoom;panY=point.Y-ActualHeight/2-(y-Image.H/2.0)*zoom;InvalidateVisual();e.Handled=true;};
        MouseDown+=(s,e)=>{if(Image==null)return;Focus();if(e.ChangedButton==MouseButton.Middle){panning=true;panStart=e.GetPosition(this);startX=panX;startY=panY;CaptureMouse();return;}if(Mask==null)return;var p=Pixel(e.GetPosition(this));if(p.X<0)return;if(BeforeStroke!=null)BeforeStroke();drawing=true;last=p;CaptureMouse();Stamp(p,e.ChangedButton==MouseButton.Right);};
        MouseMove+=(s,e)=>{if(!IsMouseCaptured)return;if(panning){var p=e.GetPosition(this);panX=startX+p.X-panStart.X;panY=startY+p.Y-panStart.Y;InvalidateVisual();return;}if(!drawing)return;var now=Pixel(e.GetPosition(this));if(now.X<0){last=now;return;}int steps=last.X<0?0:(int)Math.Max(Math.Abs(now.X-last.X),Math.Abs(now.Y-last.Y));for(int n=0;n<=steps;n++)Stamp(steps==0?now:new Point(last.X+(now.X-last.X)*n/steps,last.Y+(now.Y-last.Y)*n/steps),e.RightButton==MouseButtonState.Pressed);last=now;};MouseUp+=(s,e)=>Finish();LostMouseCapture+=(s,e)=>Finish();
    }
    public void Fit(){zoom=panX=panY=0;InvalidateVisual();}
    public void ResetMask(){overlay=null;InvalidateVisual();}
    public void Finish(){bool was=drawing;drawing=panning=false;if(IsMouseCaptured)ReleaseMouseCapture();if(was&&StrokeFinished!=null)StrokeFinished();}
    Rect Area(){if(Image==null)return Rect.Empty;double z=zoom>0?zoom:Math.Max(.001,Math.Min(Math.Max(1,ActualWidth-32)/Image.W,Math.Max(1,ActualHeight-32)/Image.H));return new Rect((ActualWidth-Image.W*z)/2+panX,(ActualHeight-Image.H*z)/2+panY,Image.W*z,Image.H*z);}
    Point Pixel(Point p){var a=Area();return a.Contains(p)?new Point(Math.Min(Image.W-1,(int)((p.X-a.X)*Image.W/a.Width)),Math.Min(Image.H-1,(int)((p.Y-a.Y)*Image.H/a.Height))):new Point(-1,-1);}
    void Stamp(Point p,bool erase){if(Paint==null||Image==null)return;int x=(int)Math.Round(p.X),y=(int)Math.Round(p.Y);for(int yy=Math.Max(0,y-Radius);yy<=Math.Min(Image.H-1,y+Radius);yy++)for(int xx=Math.Max(0,x-Radius);xx<=Math.Min(Image.W-1,x+Radius);xx++)if((xx-x)*(xx-x)+(yy-y)*(yy-y)<=Radius*Radius)Paint(xx,yy,erase);ResetMask();}
    protected override void OnRender(DrawingContext dc){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14,18,27)),null,new Rect(0,0,ActualWidth,ActualHeight));if(Image==null){var t=new FormattedText("Загрузите фото или откройте демо фон",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),18,Brushes.LightSlateGray);dc.DrawText(t,new Point(24,40));return;}Rect area=Area();var group=new DrawingGroup();RenderOptions.SetBitmapScalingMode(group,BitmapScalingMode.NearestNeighbor);using(var g=group.Open()){g.DrawImage(Image.Bitmap(),area);if(ShowMask&&Mask!=null){if(overlay==null){var f=new Frame(Image.W,Image.H);for(int n=0;n<Mask.Length;n++)if(Mask[n]){f.Data[n*4]=160;f.Data[n*4+1]=230;f.Data[n*4+2]=70;f.Data[n*4+3]=85;}overlay=f.Bitmap();}g.DrawImage(overlay,area);}}dc.DrawDrawing(group);}
}
}

