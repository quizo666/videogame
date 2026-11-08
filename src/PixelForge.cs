using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PixelForge {
public sealed class Frame {
    public int W, H; public byte[] Data;
    public Frame(int w,int h) { W=w; H=h; Data=new byte[w*h*4]; }
    public Frame Clone() { return new Frame(W,H) { Data=(byte[])Data.Clone() }; }
    public BitmapSource Bitmap() { return BitmapSource.Create(W,H,96,96,PixelFormats.Bgra32,null,Data,W*4); }
    public static Frame From(BitmapSource b) {
        var c=new FormatConvertedBitmap(b,PixelFormats.Bgra32,null,0);
        var f=new Frame(c.PixelWidth,c.PixelHeight); c.CopyPixels(f.Data,f.W*4,0); return f;
    }
    public static Frame Load(string path) {
        using(var s=File.OpenRead(path)) { return From(BitmapFrame.Create(s,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad)); }
    }
    public void Save(string path) { var enc=new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Bitmap())); using(var s=File.Create(path)) enc.Save(s); }
}
public static class Pixels {
    static bool BackgroundHue(byte[] data,int p,Color bg) {
        double bm=(bg.R+bg.G+bg.B)/3.0,cm=(data[p]+data[p+1]+data[p+2])/3.0;
        double br=bg.R-bm,bg2=bg.G-bm,bb=bg.B-bm,r=data[p+2]-cm,g=data[p+1]-cm,b=data[p]-cm;
        double a=br*br+bg2*bg2+bb*bb,c=r*r+g*g+b*b;
        return a>1800&&c>500&&(br*r+bg2*g+bb*b)/Math.Sqrt(a*c)>.90;
    }
    // Infer edge colors from nearby foreground samples using C = alpha*F + (1-alpha)*background.
    // Work from a snapshot so one correction cannot feed the next one.
    public static Frame CleanEdges(Frame frame,Color bg,int strength) {
        if(strength<=0)return frame;
        var output=frame.Clone();int w=frame.W,h=frame.H,n=w*h;var data=frame.Data;
        var depth=new byte[n];var queue=new Queue<int>();
        for(int i=0;i<n;i++) {
            if(data[i*4+3]==0)continue;int x=i%w,y=i/w;bool edge=false;
            for(int dy=-1;dy<=1&&!edge;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=w||yy>=h||data[(yy*w+xx)*4+3]==0){edge=true;break;}}
            depth[i]=(byte)(edge?1:255);if(edge)queue.Enqueue(i);
        }
        while(queue.Count>0){int i=queue.Dequeue();if(depth[i]>=strength+2)continue;int x=i%w,y=i/w;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=w||yy>=h)continue;int j=yy*w+xx;if(depth[j]==255){depth[j]=(byte)(depth[i]+1);queue.Enqueue(j);}}}
        int dominant=bg.B>=bg.G&&bg.B>=bg.R?0:bg.G>=bg.R?1:2;double[] background={bg.B,bg.G,bg.R};
        bool chromatic=Math.Max(bg.R,Math.Max(bg.G,bg.B))-Math.Min(bg.R,Math.Min(bg.G,bg.B))>=45;
        for(int i=0;i<n;i++) {
            if(depth[i]==0||depth[i]>strength)continue;int p=i*4,x=i%w,y=i/w;double best=double.MaxValue,bestAlpha=1;int bestPixel=-1;
            for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++) {
                int xx=x+dx,yy=y+dy,ds=dx*dx+dy*dy;if(ds==0||ds>20||xx<0||yy<0||xx>=w||yy>=h)continue;
                int j=yy*w+xx,q=j*4;if(depth[j]==0||depth[j]<depth[i])continue;
                if(depth[j]==depth[i]&&(BackgroundHue(data,q,bg)||ds>5))continue;
                double den=0,num=0;for(int c=0;c<3;c++){double v=data[q+c]-background[c];den+=v*v;num+=(data[p+c]-background[c])*v;}
                if(den<3600)continue;double alpha=num/den;if(alpha<.04||alpha>.94)continue;
                double error=0;for(int c=0;c<3;c++){double d=data[p+c]-(alpha*data[q+c]+(1-alpha)*background[c]);error+=d*d;}error=Math.Sqrt(error/3);
                if(error>(chromatic?10+strength*7:8+strength*3))continue;
                if(chromatic){int a=(dominant+1)%3,b=(dominant+2)%3;double excess=(data[p+dominant]-(data[p+a]+data[p+b])/2.0)-(data[q+dominant]-(data[q+a]+data[q+b])/2.0);if(excess<12)continue;}
                else if(alpha>.82||depth[j]<2)continue;
                double score=error+Math.Sqrt(ds)*2+(depth[j]>depth[i]?0:4);
                if(score<best){best=score;bestPixel=q;bestAlpha=alpha;}
            }
            if(bestPixel<0)continue;
            if(bestAlpha<.24+strength*.08){for(int c=0;c<4;c++)output.Data[p+c]=0;}
            else for(int c=0;c<3;c++)output.Data[p+c]=data[bestPixel+c];
        }
        // Small detached remnants are removed only when their hue matches the keyed background.
        var seen=new bool[n];
        for(int i=0;i<n;i++) {
            if(seen[i]||data[i*4+3]==0)continue;var component=new List<int>();queue.Enqueue(i);seen[i]=true;bool allBackground=true;
            while(queue.Count>0){int j=queue.Dequeue(),x=j%w,y=j/w;component.Add(j);if(!BackgroundHue(data,j*4,bg))allBackground=false;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=w||yy>=h)continue;int k=yy*w+xx;if(!seen[k]&&data[k*4+3]!=0){seen[k]=true;queue.Enqueue(k);}}}
            if(allBackground&&component.Count<=strength*2)foreach(int j in component)for(int c=0;c<4;c++)output.Data[j*4+c]=0;
        }
        return output;
    }
    public static Color BackgroundColor(Frame f) {
        var bins=new Dictionary<int,List<int>>();
        Action<int,int> sample=(x,y)=>{ int p=(y*f.W+x)*4; if(f.Data[p+3]<128)return; int code=(f.Data[p+2]/16<<8)|(f.Data[p+1]/16<<4)|f.Data[p]/16;
            if(!bins.ContainsKey(code))bins[code]=new List<int>(); bins[code].Add(p); };
        int step=Math.Max(1,Math.Max(f.W,f.H)/256);
        for(int x=0;x<f.W;x+=step){sample(x,0);sample(x,f.H-1);}for(int y=0;y<f.H;y+=step){sample(0,y);sample(f.W-1,y);}
        if(bins.Count==0)return Colors.Transparent;
        var points=bins.OrderByDescending(b=>b.Value.Count).ThenBy(b=>b.Key).First().Value;
        return Color.FromRgb((byte)points.Average(p=>f.Data[p+2]),(byte)points.Average(p=>f.Data[p+1]),(byte)points.Average(p=>f.Data[p]));
    }
    public static bool[] BackgroundMask(Frame f,Color bg,int tolerance,bool edgesOnly) {
        var mask=new bool[f.W*f.H];
        for(int i=0;i<mask.Length;i++){int p=i*4;mask[i]=f.Data[p+3]<128||Math.Max(Math.Abs(f.Data[p+2]-bg.R),Math.Max(Math.Abs(f.Data[p+1]-bg.G),Math.Abs(f.Data[p]-bg.B)))<=tolerance;}
        if(!edgesOnly)return mask;
        var connected=new bool[mask.Length];var queue=new Queue<int>();
        Action<int> add=i=>{if(mask[i]&&!connected[i]){connected[i]=true;queue.Enqueue(i);}};
        for(int x=0;x<f.W;x++){add(x);add((f.H-1)*f.W+x);}for(int y=0;y<f.H;y++){add(y*f.W);add(y*f.W+f.W-1);}
        while(queue.Count>0){int i=queue.Dequeue(),x=i%f.W,y=i/f.W;if(x>0)add(i-1);if(x<f.W-1)add(i+1);if(y>0)add(i-f.W);if(y<f.H-1)add(i+f.W);}
        return connected;
    }
    public static Frame Process(Frame src,int cell,int ox,int oy,bool key,Color bg,int tolerance,int levels,bool edgesOnly=false,int cleanup=0) {
        int w=(src.W+cell-1)/cell,h=(src.H+cell-1)/cell;
        var mask=key?BackgroundMask(src,bg,tolerance,edgesOnly):null;
        var dst=new Frame(w,h);
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
            var bins=new Dictionary<int,int>();var sums=new Dictionary<int,long[]>(); int solid=0,total=0;
            for(int sy=y*cell+oy;sy<Math.Min((y+1)*cell+oy,src.H);sy++)
            for(int sx=x*cell+ox;sx<Math.Min((x+1)*cell+ox,src.W);sx++) {
                if(sx<0||sy<0) continue; total++; int p=(sy*src.W+sx)*4;
                int b=src.Data[p],g=src.Data[p+1],r=src.Data[p+2];
                if(src.Data[p+3]<128 || (key && mask[p/4])) continue;
                solid++;
                int qr=(int)Math.Round(r*(levels-1)/255.0)*255/(levels-1),qg=(int)Math.Round(g*(levels-1)/255.0)*255/(levels-1),qb=(int)Math.Round(b*(levels-1)/255.0)*255/(levels-1);
                int rgb=(qr<<16)|(qg<<8)|qb;if(!bins.ContainsKey(rgb)){bins[rgb]=0;sums[rgb]=new long[3];}bins[rgb]++;sums[rgb][0]+=b;sums[rgb][1]+=g;sums[rgb][2]+=r;
            }
            if(solid==0 || solid*2<total) continue;
            int color=bins.OrderByDescending(k=>k.Value).ThenBy(k=>k.Key).First().Key;
            int q=(y*w+x)*4;for(int c=0;c<3;c++)dst.Data[q+c]=(byte)(sums[color][c]/bins[color]);dst.Data[q+3]=255;
        }
        if(key&&cleanup>0)dst=CleanEdges(dst,bg,cleanup);
        for(int p=0;p<dst.Data.Length;p+=4)if(dst.Data[p+3]!=0)for(int c=0;c<3;c++)dst.Data[p+c]=(byte)((int)Math.Round(dst.Data[p+c]*(levels-1)/255.0)*255/(levels-1));
        return dst;
    }
    // Find repeated edge spacing on the first source frame. This is a suggestion, not a reconstruction model.
    public static int SuggestCell(Frame f) {
        double[] ex=new double[f.W],ey=new double[f.H];
        for(int y=1;y<f.H;y++) for(int x=1;x<f.W;x++) {
            int p=(y*f.W+x)*4,a=p-4,b=p-f.W*4;
            for(int c=0;c<3;c++) { ex[x]+=Math.Abs(f.Data[p+c]-f.Data[a+c]); ey[y]+=Math.Abs(f.Data[p+c]-f.Data[b+c]); }
        }
        double best=0; int answer=1;
        for(int cell=2;cell<=Math.Min(24,Math.Min(f.W,f.H)/8);cell++) {
            double topX=0,topY=0;
            for(int off=0;off<cell;off++) {
                double sx=0,sy=0; int nx=0,ny=0;
                for(int x=off;x<f.W;x+=cell) { sx+=ex[x]; nx++; }
                for(int y=off;y<f.H;y+=cell) { sy+=ey[y]; ny++; }
                topX=Math.Max(topX,sx/Math.Max(1,nx)); topY=Math.Max(topY,sy/Math.Max(1,ny));
            }
            double avgX=ex.Average(),avgY=ey.Average();
            double score=((topX/(avgX+1))+(topY/(avgY+1)))/Math.Sqrt(cell);
            if(score>best) { best=score; answer=cell; }
        }
        return answer;
    }
    public static Frame Sheet(IList<Frame> frames,int columns) {
        if(frames.Count==0) throw new Exception("Нет кадров.");
        int w=frames[0].W,h=frames[0].H,rows=(frames.Count+columns-1)/columns;
        if((long)w*columns*h*rows>64000000) throw new Exception("Sprite sheet слишком большой. Уменьшите число кадров или размер.");
        var output=new Frame(w*columns,h*rows);
        for(int n=0;n<frames.Count;n++) for(int y=0;y<h;y++) Buffer.BlockCopy(frames[n].Data,y*w*4,output.Data,((n/columns*h+y)*output.W+(n%columns*w))*4,w*4);
        return output;
    }
}
public sealed class PixelStroke : IFrameEdit {
    readonly IList<Frame> frames;readonly bool erase,forward;readonly Color ink;
    readonly Dictionary<int,Dictionary<int,uint>> before=new Dictionary<int,Dictionary<int,uint>>();
    public readonly int StartFrame;
    int IFrameEdit.StartFrame {get{return StartFrame;}}
    public IEnumerable<int> AffectedFrames {get{return before.Keys;}}
    public bool HasChanges {get{return before.Count>0;}}
    public bool IsForwardErase {get{return forward;}}
    public PixelStroke(IList<Frame> frames,int start,bool erase,bool forward,Color ink){this.frames=frames;StartFrame=start;this.erase=erase;this.forward=forward&&erase;this.ink=ink;}
    static uint Read(byte[] d,int p){return (uint)d[p]|((uint)d[p+1]<<8)|((uint)d[p+2]<<16)|((uint)d[p+3]<<24);}
    static void Write(byte[] d,int p,uint value){for(int c=0;c<4;c++)d[p+c]=(byte)(value>>(c*8));}
    public void Paint(int x,int y){
        for(int n=StartFrame;n<(forward?frames.Count:StartFrame+1);n++){
            var f=frames[n];if(x<0||y<0||x>=f.W||y>=f.H)continue;int pixel=y*f.W+x,p=pixel*4;
            uint old=Read(f.Data,p),value=erase?0u:(uint)ink.B|((uint)ink.G<<8)|((uint)ink.R<<16)|0xff000000u;
            if(old==value)continue;if(!before.ContainsKey(n))before[n]=new Dictionary<int,uint>();
            if(!before[n].ContainsKey(pixel))before[n][pixel]=old;Write(f.Data,p,value);
        }
    }
    public void Undo(){foreach(var frame in before)foreach(var pixel in frame.Value)Write(frames[frame.Key].Data,pixel.Key*4,pixel.Value);}
}
public sealed class Editor : FrameworkElement {
    public Frame Frame; public bool Grid=true,Original=false; public string Tool="Карандаш"; public Color Ink=Colors.White;public int BrushSize=1,EraseSize=1;
    public Action<bool> BeforeStroke;public Action<int,int> PixelPainted;public Action StrokeEnded;public Action AfterStroke; public Action<Color> BackgroundPicked; public bool PickingBackground; Point last=new Point(-1,-1),hoverPixel=new Point(-1,-1);bool strokeActive;
    public Editor() { Focusable=true; MouseDown+=Down; MouseMove+=Move; MouseUp+=(s,e)=>FinishStroke();LostMouseCapture+=(s,e)=>FinishStroke();MouseLeave+=(s,e)=>{hoverPixel=new Point(-1,-1);InvalidateVisual();}; }
    public void FinishStroke(){bool ended=strokeActive;strokeActive=false;if(IsMouseCaptured)ReleaseMouseCapture();last=new Point(-1,-1);if(ended&&StrokeEnded!=null)StrokeEnded();}
    Rect Area() { if(Frame==null) return Rect.Empty; double z=Math.Max(0.01,Math.Min((ActualWidth-40)/Frame.W,(ActualHeight-40)/Frame.H)); if(z>=1) z=Math.Max(1,Math.Floor(z)); return new Rect((ActualWidth-Frame.W*z)/2,(ActualHeight-Frame.H*z)/2,Frame.W*z,Frame.H*z); }
    protected override void OnRender(DrawingContext dc) {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20,24,34)),null,new Rect(0,0,ActualWidth,ActualHeight));
        if(Frame==null) { var text=new FormattedText("Загрузите видео или PNG-кадры\nлибо откройте демо",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),20,Brushes.LightSlateGray); dc.DrawText(text,new Point(40,60)); return; }
        Rect r=Area(); dc.PushClip(new RectangleGeometry(r));
        for(double y=r.Y;y<r.Bottom;y+=12) for(double x=r.X;x<r.Right;x+=12) dc.DrawRectangle(new SolidColorBrush(((int)((x-r.X)/12)+(int)((y-r.Y)/12))%2==0 ? Color.FromRgb(55,62,77):Color.FromRgb(43,49,63)),null,new Rect(x,y,12,12));
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.NearestNeighbor); dc.DrawImage(Frame.Bitmap(),r);
        if(Grid&&!Original&&r.Width/Frame.W>=5) { var pen=new Pen(new SolidColorBrush(Color.FromArgb(70,210,220,240)),0.5); double z=r.Width/Frame.W; for(int x=0;x<=Frame.W;x++) dc.DrawLine(pen,new Point(r.X+x*z,r.Y),new Point(r.X+x*z,r.Bottom)); for(int y=0;y<=Frame.H;y++) dc.DrawLine(pen,new Point(r.X,r.Y+y*z),new Point(r.Right,r.Y+y*z)); }
        dc.Pop();
    }
    Point Pixel(Point p) { var r=Area(); return r.Contains(p)?new Point(Math.Min(Frame.W-1,(int)((p.X-r.X)*Frame.W/r.Width)),Math.Min(Frame.H-1,(int)((p.Y-r.Y)*Frame.H/r.Height))):new Point(-1,-1); }
    void Down(object s,MouseButtonEventArgs e) { if(Frame==null) return; var p=Pixel(e.GetPosition(this)); if(p.X<0) return;
        if(PickingBackground){int i=((int)p.Y*Frame.W+(int)p.X)*4;if(Frame.Data[i+3]<128)return;PickingBackground=false;Cursor=Cursors.Arrow;if(BackgroundPicked!=null)BackgroundPicked(Color.FromRgb(Frame.Data[i+2],Frame.Data[i+1],Frame.Data[i]));return;}
        if(Original)return; Focus(); if(Tool=="Пипетка") { int i=((int)p.Y*Frame.W+(int)p.X)*4; Ink=Color.FromRgb(Frame.Data[i+2],Frame.Data[i+1],Frame.Data[i]); if(AfterStroke!=null) AfterStroke(); return; }FinishStroke();if(BeforeStroke!=null)BeforeStroke(e.ChangedButton==MouseButton.Right||Tool=="Ластик");strokeActive=true;CaptureMouse(); Paint(p,e.ChangedButton==MouseButton.Right); last=p; }
    void Move(object s,MouseEventArgs e) {if(Frame==null)return;var p=Pixel(e.GetPosition(this));hoverPixel=p;InvalidateVisual();if(!IsMouseCaptured)return; if(p.X<0) { last=p; return; } bool erase=e.RightButton==MouseButtonState.Pressed; if(last.X<0) Paint(p,erase); else { int steps=(int)Math.Max(Math.Abs(p.X-last.X),Math.Abs(p.Y-last.Y)); for(int n=0;n<=steps;n++) Paint(new Point(Math.Round(last.X+(p.X-last.X)*n/Math.Max(1,steps)),Math.Round(last.Y+(p.Y-last.Y)*n/Math.Max(1,steps))),erase); } last=p; }
    void Paint(Point p,bool erase) {int size=erase||Tool=="Ластик"?EraseSize:BrushSize;BrushFootprint.Visit((int)p.X,(int)p.Y,size,(x,y)=>{if(x>=0&&y>=0&&x<Frame.W&&y<Frame.H&&PixelPainted!=null)PixelPainted(x,y);});InvalidateVisual(); }
}
public sealed partial class MainWindow : Window {
    public bool AutomatedTest;
    CutsceneEditor cutscenes;AmbientBackgroundEditor backgrounds;TabControl mainTabs;
    List<Frame> sources=new List<Frame>(), frames=new List<Frame>(); Stack<IFrameEdit> undo=new Stack<IFrameEdit>();PixelStroke activeStroke;
    ComboBox eraseScope=new ComboBox();Grid workspace=new Grid();ScrollViewer settingsScroll;StackPanel settings;
    Editor editor=new Editor(); ListBox timeline=new ListBox(); TextBlock status=new TextBlock(),info=new TextBlock();
    TextBox cell=Box("4"),offsetX=Box("0"),offsetY=Box("0"),tolerance=Box("55"),levels=Box("8"),fps=Box("12"),count=Box("96"),ink=Box("#F6D7B0"),keyColor=Box("#00FF00"),cleanup=Box("2");
    CheckBox key=new CheckBox { Content="Убрать цвет фона",IsChecked=true },grid=new CheckBox { Content="Пиксельная сетка",IsChecked=true },original=new CheckBox { Content="Показать исходник" };
    CheckBox autoBackground=new CheckBox { Content="Автоцвет по краям кадра",IsChecked=true },edgeBackground=new CheckBox { Content="Только фон, связанный с краем",IsChecked=false };
    ComboBox tool=new ComboBox(); DispatcherTimer clock=new DispatcherTimer(); bool busy,changed; int current; string videoPath;
    MediaPlayer player=new MediaPlayer();
    public MainWindow() {
        Title="PixelForge Studio · Спрайты и катсцены"; Width=1380; Height=900; MinWidth=720; MinHeight=600; Background=Brush("#141822"); Foreground=Brush("#E8EDF7"); FontFamily=new FontFamily("Segoe UI"); FontSize=13;
        var tabStyle=new Style(typeof(TabItem));tabStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#273247")));tabStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));tabStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type TabItem}'><Border x:Name='surface' Background='{TemplateBinding Background}' Padding='12,8' Margin='0,0,3,0'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='surface' Property='Background' Value='#366958'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));Resources.Add(typeof(TabItem),tabStyle);
        var checkStyle=new Style(typeof(CheckBox));checkStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#E8EDF7")));checkStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,2,0,3)));Resources.Add(typeof(CheckBox),checkStyle);
        var style=new Style(typeof(Button)); style.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#2A354A"))); style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White)); style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8))); style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0))); style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,8,8))); Resources.Add(typeof(Button),style);
        var ts=new Style(typeof(TextBox)); ts.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#222B3B"))); ts.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White)); ts.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#3B4962"))); ts.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7))); Resources.Add(typeof(TextBox),ts);
        var root=new DockPanel { Margin=new Thickness(20) }; Content=root;
        var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text="PIXELFORGE",FontSize=27,FontWeight=FontWeights.Bold,Foreground=Brush("#A7F3C6") });
        header.Children.Add(new TextBlock { Text="Видео → единая сетка → покадровая правка → спрайты",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,3,0,12) });
        var top=new WrapPanel(); header.Children.Add(top);
        Add(top,"Открыть видео",async()=>await OpenVideo()); Add(top,"Импорт PNG",()=>ImportPng()); Add(top,"Демо",()=>Demo());Add(top,"Убрать рябь",async()=>await StabilizeSprites()); Add(top,"Сохранить спрайты",()=>SaveProject()); Add(top,"Открыть спрайты",()=>LoadProject());
        Add(top,"Удалить фон",()=>{key.IsChecked=true;original.IsChecked=false;Apply();}); Add(top,"Выбрать фон кликом",()=>PickBackground());
        Add(top,"Очистить кайму",()=>{if(Number(cleanup,0,3)==0)cleanup.Text="2";key.IsChecked=true;original.IsChecked=false;Apply();});
        status.Text="Готово. Все данные обрабатываются на этом компьютере."; status.TextWrapping=TextWrapping.Wrap; status.Margin=new Thickness(0,12,0,0); status.Foreground=Brush("#A7F3C6"); DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        var bottom=new StackPanel { Margin=new Thickness(0,6,0,0) }; DockPanel.SetDock(bottom,Dock.Bottom); root.Children.Add(bottom);
        var controls=new WrapPanel { Margin=new Thickness(0,8,0,0) }; bottom.Children.Add(controls);
        Add(controls,"◀",()=>Select(current-1)); Add(controls,"▶ / Пауза",()=>{ if(frames.Count==0)return; clock.Interval=TimeSpan.FromSeconds(1.0/Number(fps,1,60)); clock.IsEnabled=!clock.IsEnabled; }); Add(controls,"▶",()=>Select(current+1));
        Add(controls,"Экспорт кадра",()=>ExportOne()); Add(controls,"Все PNG + sheet",()=>ExportAll()); controls.Children.Add(info);
        timeline.Height=83; timeline.Background=Brush("#1C2331"); timeline.BorderThickness=new Thickness(0); timeline.Foreground=Brushes.White;
        var factory=new FrameworkElementFactory(typeof(StackPanel)); factory.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal); timeline.ItemsPanel=new ItemsPanelTemplate(factory); ScrollViewer.SetHorizontalScrollBarVisibility(timeline,ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(timeline,ScrollBarVisibility.Disabled);
        timeline.SelectionChanged+=(s,e)=>{editor.FinishStroke(); if(timeline.SelectedIndex>=0) { current=timeline.SelectedIndex; UpdateFrame(); } }; bottom.Children.Add(timeline);
        root.Children.Add(workspace);workspace.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(320)});workspace.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        workspace.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});workspace.RowDefinitions.Add(new RowDefinition {Height=new GridLength(0)});
        var side=new StackPanel { Margin=new Thickness(0,0,12,12) };settings=side;settingsScroll=new ScrollViewer {Content=side,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)};workspace.Children.Add(settingsScroll);
        Label(side,"01   ИЗВЛЕЧЕНИЕ КАДРОВ"); Row(side,"Кадров в секунду",fps); Row(side,"Лимит кадров (до 240)",count);
        var extract=new Button { Content="Извлечь из видео",HorizontalAlignment=HorizontalAlignment.Stretch }; extract.Click+=async(s,e)=>await Extract(); side.Children.Add(extract);
        Label(side,"02   ПИКСЕЛЬНАЯ СЕТКА"); Row(side,"Ячейка в исходнике, px",cell); Row(side,"Сдвиг сетки X",offsetX); Row(side,"Сдвиг сетки Y",offsetY);
        side.Children.Add(new TextBlock { Text="Ячейка 4: каждый блок 4 × 4 пикселя исходника превращается в один пиксель спрайта. Одна сетка применяется ко всей анимации.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,4,0,8) });
        Add(side,"Подобрать размер сетки",()=>{ if(sources.Count>0) { cell.Text=Pixels.SuggestCell(sources[0]).ToString(); Tell("Размер — подсказка по первому кадру. Проверьте результат и примените."); } });
        side.Children.Add(key); side.Children.Add(autoBackground); side.Children.Add(edgeBackground); Row(side,"Цвет фона, HEX",keyColor); Row(side,"Допуск фона (0–255)",tolerance); Row(side,"Очистка каймы, 0–3",cleanup); Row(side,"Уровней на канал (2–32)",levels);AddSpriteStabilizationControls(side);
        side.Children.Add(new TextBlock { Text="Кайма: 0 — выкл., 1 — мягко, 2 — обычно, 3 — сильнее. Цвет края восстанавливается по соседним пикселям; мелкие точки цвета фона удаляются.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,4,0,8) });
        side.Children.Add(new TextBlock { Text="Автоцвет подходит для ровного фона. Если между руками/ногами остались пятна — отключите ограничение по краю.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,4,0,8) });
        Add(side,"Применить ко всем кадрам",()=>Apply());
        Label(side,"03   ПРАВКА КАДРА"); tool.ItemsSource=new[]{"Карандаш","Ластик","Пипетка"}; tool.SelectedIndex=0; tool.Margin=new Thickness(0,0,0,8); tool.SelectionChanged+=(s,e)=>editor.Tool=(string)tool.SelectedItem; side.Children.Add(tool);
        side.Children.Add(new TextBlock {Text="Ластик применяется к:",Margin=new Thickness(0,0,0,5)});eraseScope.ItemsSource=new[]{"Только этому кадру","Этому и всем последующим"};eraseScope.SelectedIndex=0;eraseScope.Margin=new Thickness(0,0,0,8);side.Children.Add(eraseScope);
        side.Children.Add(new TextBlock {Text="В следующих кадрах стираются те же координаты. Персонаж не отслеживается; предыдущие кадры сохраняются. Работает и с ПКМ.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,0,0,10)});
        Row(side,"Цвет кисти, HEX",ink); ink.TextChanged+=(s,e)=>{ try { editor.Ink=ParseColor(ink.Text); } catch {} }; editor.Ink=ParseColor(ink.Text);
        AddBrushSizes(side);grid.Content="Показать линии пиксельной сетки";grid.ToolTip="Только отображение границ пикселей. Не меняет обработку или экспорт.";
        Add(side,"Отменить штрих  ·  Ctrl+Z",()=>Undo()); side.Children.Add(grid); side.Children.Add(original); grid.Click+=(s,e)=>UpdateFrame(); original.Click+=(s,e)=>UpdateFrame();
        side.Children.Add(new TextBlock { Text="ЛКМ — рисовать. ПКМ — стирать.\nОбщий холст и сетка сохраняются для всех кадров. Повторная обработка заменяет ручные правки.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#94A3B8"),Margin=new Thickness(0,16,0,0) });
        Grid.SetColumn(editor,1);workspace.Children.Add(editor);workspace.SizeChanged+=(s,e)=>ResponsiveLayout();Loaded+=(s,e)=>ResponsiveLayout();
        editor.BeforeStroke=erase=>{clock.Stop();activeStroke=new PixelStroke(frames,current,erase,eraseScope.SelectedIndex==1,editor.Ink);};
        editor.PixelPainted=(x,y)=>{if(activeStroke!=null){activeStroke.Paint(x,y);if(activeStroke.HasChanges)changed=true;MarkStudioDirty();}};
        editor.StrokeEnded=()=>{if(activeStroke!=null&&activeStroke.HasChanges){undo.Push(activeStroke);foreach(int n in activeStroke.AffectedFrames)RefreshThumbnail(n);Tell(activeStroke.IsForwardErase?"Штрих применён к текущему и последующим кадрам. Ctrl+Z отменяет его целиком.":"Штрих применён к текущему кадру.");}activeStroke=null;};
        editor.AfterStroke=()=>{ if(tool.SelectedIndex==2) ink.Text=editor.Ink.ToString(); };
        editor.BackgroundPicked=color=>{keyColor.Text=color.ToString();autoBackground.IsChecked=false;key.IsChecked=true;original.IsChecked=false;UpdateFrame();Tell("Цвет фона выбран. Нажмите «Удалить фон», чтобы применить ко всем кадрам.");};
        clock.Tick+=(s,e)=>Select((current+1)%Math.Max(1,frames.Count));
        PreviewKeyDown+=(s,e)=>{if(mainTabs!=null&&mainTabs.SelectedIndex!=0)return; if(e.Key==Key.Escape&&editor.PickingBackground){editor.PickingBackground=false;editor.Cursor=Cursors.Arrow;original.IsChecked=false;UpdateFrame();Tell("Выбор фона отменён.");e.Handled=true;} if(e.Key==Key.Z&&(Keyboard.Modifiers&ModifierKeys.Control)!=0) { Undo(); e.Handled=true; } };
        cutscenes=new CutsceneEditor();backgrounds=new AmbientBackgroundEditor();backgrounds.IsAutomatedRun=()=>AutomatedTest;backgrounds.Transfer=(p,f)=>{cutscenes.AddAmbient(p,f);mainTabs.SelectedIndex=1;};backgrounds.TestTransfer=(p,f)=>cutscenes.TestAmbientTransfer(p,f);mainTabs=new TabControl {Background=Brush("#141822"),Foreground=Foreground,BorderThickness=new Thickness(0)};mainTabs.Items.Add(new TabItem {Header="01 · Подготовка спрайтов",Content=root});mainTabs.Items.Add(new TabItem {Header="02 · Монтаж катсцен",Content=cutscenes});mainTabs.Items.Add(new TabItem {Header="03 · Фон из фото",Content=backgrounds});Content=mainTabs;
        Add(top,"В монтаж катсцены →",()=>{if(frames.Count==0){Tell("Сначала загрузите или подготовьте кадры.");return;}cutscenes.AddSequence(frames,"Спрайты · "+frames.Count+" кадров",Number(fps,1,60));mainTabs.SelectedIndex=1;});
        mainTabs.SelectionChanged+=(s,e)=>{if(e.Source==mainTabs){clock.Stop();cutscenes.Stop();backgrounds.Stop();editor.FinishStroke();}};
        Closing+=(s,e)=>OnStudioClosing(e);
        InitializeStudioShell();
    }
    static TextBox Box(string s) { return new TextBox { Text=s,Width=93,Margin=new Thickness(3,0,0,7) }; }
    static Brush Brush(string s) { return (Brush)new BrushConverter().ConvertFromString(s); }
    static Color ParseColor(string s) { return (Color)ColorConverter.ConvertFromString(s); }
    static int Number(TextBox b,int min,int max) { int n; if(!int.TryParse(b.Text,out n)||n<min||n>max) throw new Exception("Введите целое число от "+min+" до "+max+" (получено: "+b.Text+")."); return n; }
    void Label(Panel p,string s) { p.Children.Add(new TextBlock { Text=s,FontWeight=FontWeights.Bold,Foreground=Brush("#A7F3C6"),Margin=new Thickness(0,16,0,12) }); }
    void Row(Panel p,string s,TextBox box) { var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});Grid.SetColumn(box,1);row.Children.Add(box);row.Children.Add(new TextBlock {Text=s,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,7)});p.Children.Add(row); }
    void Add(Panel p,string title,Action action) { var b=new Button { Content=new TextBlock {Text=title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center} }; b.Click+=(s,e)=>{ if(busy) return; try {editor.FinishStroke();action(); } catch(Exception ex) { Tell(ex.Message); } }; p.Children.Add(b); }
    void ResponsiveLayout(){if(workspace.ActualWidth<=0)return;bool narrow=workspace.ActualWidth<850&&workspace.ActualHeight>=240;workspace.ColumnDefinitions[0].Width=narrow?new GridLength(1,GridUnitType.Star):new GridLength(Math.Max(285,Math.Min(380,workspace.ActualWidth*.28)));workspace.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);workspace.RowDefinitions[0].Height=narrow?new GridLength(Math.Max(64,Math.Min(190,workspace.ActualHeight*.34))):new GridLength(1,GridUnitType.Star);workspace.RowDefinitions[1].Height=narrow?new GridLength(1,GridUnitType.Star):new GridLength(0);Grid.SetColumn(editor,narrow?0:1);Grid.SetRow(editor,narrow?1:0);}
    void RefreshThumbnail(int n){if(n<0||n>=timeline.Items.Count)return;var panel=timeline.Items[n] as StackPanel;if(panel!=null)((Image)panel.Children[0]).Source=frames[n].Bitmap();}
    void Tell(string s) { status.Text=s; }
    bool ReplaceOK() { return !changed||MessageBox.Show("Заменить текущие кадры? Несохранённые правки будут потеряны.","PixelForge",MessageBoxButton.YesNo)==MessageBoxResult.Yes; }
    void PickBackground(){if(sources.Count==0){Tell("Сначала загрузите кадры.");return;}clock.Stop();original.IsChecked=true;UpdateFrame();editor.PickingBackground=true;editor.Cursor=Cursors.Cross;Tell("Нажмите на фон исходного кадра. Esc — отменить выбор.");}
    Frame ConvertFrame(Frame f,int c,int x,int y,Color color,int t,int l){Color background=autoBackground.IsChecked==true?Pixels.BackgroundColor(f):color;bool remove=key.IsChecked==true&&background.A!=0;return Pixels.Process(f,c,x,y,remove,background,t,l,edgeBackground.IsChecked==true,Number(cleanup,0,3));}
    void Reset(List<Frame> input) {
        int c=Number(cell,1,128),x=Number(offsetX,0,c-1),y=Number(offsetY,0,c-1),t=Number(tolerance,0,255),l=Number(levels,2,32); Color color=ParseColor(keyColor.Text);
        var converted=input.Select(f=>ConvertFrame(f,c,x,y,color,t,l)).ToList();
        sources=input;frames=converted;undo.Clear();current=0;changed=true;MarkStudioDirty();RefreshTimeline();
        Tell("Готово: "+frames.Count+" кадров · "+frames[0].W+" × "+frames[0].H+" px.");
    }
    void Apply() { Apply(true); }
    void Apply(bool confirm) {
        if(sources.Count==0) { Tell("Сначала загрузите кадры."); return; }
        int c=Number(cell,1,128),x=Number(offsetX,0,c-1),y=Number(offsetY,0,c-1),t=Number(tolerance,0,255),l=Number(levels,2,32); Color color=ParseColor(keyColor.Text);
        if(confirm&&changed&&MessageBox.Show("Пересчитать все кадры и заменить ручные правки?","PixelForge",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        frames=sources.Select(f=>ConvertFrame(f,c,x,y,color,t,l)).ToList(); undo.Clear(); changed=true;MarkStudioDirty(); RefreshTimeline(); Tell("Готово: "+frames.Count+" кадров · "+frames[0].W+" × "+frames[0].H+" px. Общая сетка, бинарная прозрачность.");
    }
    void RefreshTimeline() { clock.Stop(); timeline.Items.Clear(); for(int i=0;i<frames.Count;i++) { var p=new StackPanel { Width=65 }; p.Children.Add(new Image { Source=frames[i].Bitmap(),Width=48,Height=44,Stretch=Stretch.Uniform }); p.Children.Add(new TextBlock { Text=(i+1).ToString("D3"),HorizontalAlignment=HorizontalAlignment.Center }); timeline.Items.Add(p); } Select(Math.Min(current,frames.Count-1)); }
    void Select(int n) {editor.FinishStroke(); if(frames.Count==0)return; current=Math.Max(0,Math.Min(n,frames.Count-1)); timeline.SelectedIndex=current; UpdateFrame(); }
    void UpdateFrame() { if(frames.Count==0)return; if(original.IsChecked!=true){editor.PickingBackground=false;editor.Cursor=Cursors.Arrow;} editor.Frame=original.IsChecked==true?sources[current]:frames[current]; editor.Original=original.IsChecked==true; editor.Grid=grid.IsChecked==true; editor.InvalidateVisual(); info.Text="Кадр "+(current+1)+" / "+frames.Count+"   ·   "+frames[current].W+" × "+frames[current].H; }
    void Undo() {editor.FinishStroke(); if(undo.Count==0)return;var u=undo.Pop();u.Undo();foreach(int n in u.AffectedFrames)RefreshThumbnail(n);Select(u.StartFrame);changed=true;MarkStudioDirty();Tell("Штрих отменён на всех затронутых кадрах."); }
    void ImportPng() {
        if(!ReplaceOK())return; var dlg=new OpenFileDialog { Filter="PNG frames|*.png",Multiselect=true }; if(dlg.ShowDialog()!=true)return;
        var paths=dlg.FileNames.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray(); if(paths.Length>240)throw new Exception("Максимум 240 кадров за импорт.");
        var input=new List<Frame>(); long pixels=0;
        foreach(string path in paths) { var f=Frame.Load(path); pixels+=(long)f.W*f.H; if(pixels>32000000)throw new Exception("Слишком много исходных пикселей. Уменьшите разрешение или число кадров (лимит 32 млн)."); if(input.Count>0&&(f.W!=input[0].W||f.H!=input[0].H))throw new Exception("PNG должны иметь одинаковые размеры холста."); input.Add(f); }
        Reset(input);
    }
    async Task OpenVideo() {
        if(busy)return;
        var dlg=new OpenFileDialog { Filter="Видео|*.mp4;*.wmv;*.avi;*.mov|Все файлы|*.*" }; if(dlg.ShowDialog()!=true)return;
        try { busy=true; IsEnabled=false; await LoadVideo(dlg.FileName); videoPath=dlg.FileName;MarkStudioDirty(); Tell("Видео: "+Path.GetFileName(videoPath)+" · "+player.NaturalVideoWidth+" × "+player.NaturalVideoHeight+" · "+player.NaturalDuration.TimeSpan.TotalSeconds.ToString("0.0")+" с. Нажмите «Извлечь из видео»."); } catch(Exception ex) { videoPath=null; Tell("Видео не открыто: "+ex.Message); } finally { busy=false; IsEnabled=true; }
    }
    async Task LoadVideo(string path) {
        player.Close(); var ready=new TaskCompletionSource<bool>();
        EventHandler opened=(s,e)=>ready.TrySetResult(true); EventHandler<ExceptionEventArgs> failed=(s,e)=>ready.TrySetException(e.ErrorException);
        player.MediaOpened+=opened; player.MediaFailed+=failed;
        try { player.ScrubbingEnabled=true; player.Volume=0; player.Open(new Uri(path)); if(await Task.WhenAny(ready.Task,Task.Delay(15000))!=ready.Task) throw new Exception("Декодер не ответил. Попробуйте MP4/H.264 или PNG-кадры."); await ready.Task; player.Pause(); if(!player.HasVideo||!player.NaturalDuration.HasTimeSpan)throw new Exception("Не удалось прочитать видеодорожку."); }
        finally { player.MediaOpened-=opened; player.MediaFailed-=failed; }
    }
    async Task<Frame> Capture(double seconds,int w,int h) {
        player.Position=TimeSpan.FromSeconds(seconds);
        await Task.Delay(160); await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
        var visual=new DrawingVisual(); using(var dc=visual.RenderOpen()) dc.DrawVideo(player,new Rect(0,0,w,h));
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32); bitmap.Render(visual); return Frame.From(bitmap);
    }
    async Task Extract() {
        if(busy)return; if(videoPath==null) { Tell("Сначала откройте видео."); return; }
        try {
            int rate=Number(fps,1,60),limit=Number(count,1,240); if(!ReplaceOK())return;
            busy=true; IsEnabled=false; clock.Stop(); int width=player.NaturalVideoWidth,height=player.NaturalVideoHeight;
            double scale=Math.Min(1.0,768.0/Math.Max(width,height)); width=Math.Max(1,(int)(width*scale)); height=Math.Max(1,(int)(height*scale));
            int n=Math.Min(limit,Math.Max(1,(int)Math.Ceiling(player.NaturalDuration.TimeSpan.TotalSeconds*rate)));
            if((long)width*height*n>32000000)throw new Exception("Слишком много кадров для памяти. Уменьшите лимит кадров (до 32 млн исходных пикселей).");
            var input=new List<Frame>();
            for(int i=0;i<n;i++) { Tell("Извлечение "+(i+1)+" / "+n+"…"); input.Add(await Capture(i/(double)rate,width,height)); }
            Reset(input); Tell("Извлечено "+n+" кадров. Видео декодируется средствами Windows; проверьте последовательность перед экспортом.");
        } catch(Exception ex) { Tell(ex.Message); } finally { busy=false; IsEnabled=true; }
    }
    void ExportOne() { if(frames.Count==0)return; var d=new SaveFileDialog { Filter="PNG|*.png",FileName="frame_"+current.ToString("D4")+".png" }; if(d.ShowDialog()==true) { frames[current].Save(d.FileName); Tell("Кадр сохранён: "+d.FileName); } }
    void ExportAll() {
        if(frames.Count==0)return; int rate=Number(fps,1,60);
        var d=new SaveFileDialog { Filter="Папка экспорта (выберите имя)|*.folder",FileName="sprites.folder" }; if(d.ShowDialog()!=true)return;
        string dir=Path.Combine(Path.GetDirectoryName(d.FileName),Path.GetFileNameWithoutExtension(d.FileName));
        if(Directory.Exists(dir)&&Directory.EnumerateFileSystemEntries(dir).Any()) { Tell("Выберите новое имя: папка уже содержит файлы."); return; }
        Directory.CreateDirectory(dir); int columns=(int)Math.Ceiling(Math.Sqrt(frames.Count));
        for(int i=0;i<frames.Count;i++)frames[i].Save(Path.Combine(dir,"frame_"+i.ToString("D4")+".png"));
        Pixels.Sheet(frames,columns).Save(Path.Combine(dir,"spritesheet.png"));
        File.WriteAllText(Path.Combine(dir,"animation.json"),"{\n  \"image\": \"spritesheet.png\",\n  \"frameWidth\": "+frames[0].W+",\n  \"frameHeight\": "+frames[0].H+",\n  \"frameCount\": "+frames.Count+",\n  \"columns\": "+columns+",\n  \"fps\": "+rate+",\n  \"order\": \"row-major\"\n}");
        Tell("Экспортировано: "+dir);
    }
    void SaveProject() {
        if(frames.Count==0)return; int rate=Number(fps,1,60);
        var d=new SaveFileDialog { Filter="PixelForge project|*.pxf",FileName="animation.pxf" }; if(d.ShowDialog()!=true)return;
        int clean=Number(cleanup,0,3);
        using(var s=new BinaryWriter(File.Create(d.FileName))) { s.Write("PXF3"); s.Write(cell.Text);s.Write(offsetX.Text);s.Write(offsetY.Text);s.Write(tolerance.Text);s.Write(levels.Text);s.Write(keyColor.Text);s.Write(key.IsChecked==true);s.Write(autoBackground.IsChecked==true);s.Write(edgeBackground.IsChecked==true);s.Write(clean);s.Write(rate);s.Write(frames.Count); for(int i=0;i<frames.Count;i++) { WriteFrame(s,sources[i]);WriteFrame(s,frames[i]); } }
        changed=false; Tell("Проект сохранён вместе с исходными кадрами и правками.");
    }
    static void WriteFrame(BinaryWriter w,Frame f) { w.Write(f.W);w.Write(f.H);w.Write(f.Data); }
    static Frame ReadFrame(BinaryReader r,ref long total) { int w=r.ReadInt32(),h=r.ReadInt32(); if(w<1||h<1||w>8192||h>8192)throw new Exception("Некорректный размер кадра."); total+=(long)w*h; if(total>64000000)throw new Exception("Проект превышает лимит памяти."); var f=new Frame(w,h); if(r.Read(f.Data,0,f.Data.Length)!=f.Data.Length)throw new Exception("Проект повреждён.");return f; }
    void LoadProject() {
        if(!ReplaceOK())return; var d=new OpenFileDialog { Filter="PixelForge project|*.pxf" }; if(d.ShowDialog()!=true)return;
        using(var r=new BinaryReader(File.OpenRead(d.FileName))) {
            string version=r.ReadString();if(version!="PXF1"&&version!="PXF2"&&version!="PXF3")throw new Exception("Неизвестный формат проекта.");
            string c=r.ReadString(),ox=r.ReadString(),oy=r.ReadString(),t=r.ReadString(),l=r.ReadString(),color=r.ReadString(); bool k=r.ReadBoolean();bool automatic=version!="PXF1"?r.ReadBoolean():false,edges=version!="PXF1"?r.ReadBoolean():false; int clean=version=="PXF3"?r.ReadInt32():0; int rate=r.ReadInt32(),n=r.ReadInt32();
            if(clean<0||clean>3)throw new Exception("Некорректная настройка очистки каймы.");
            if(n<1||n>240||rate<1||rate>60)throw new Exception("Некорректный проект.");
            var src=new List<Frame>();var dst=new List<Frame>();long total=0;
            for(int i=0;i<n;i++) { src.Add(ReadFrame(r,ref total));dst.Add(ReadFrame(r,ref total)); if(i>0&&(src[i].W!=src[0].W||src[i].H!=src[0].H||dst[i].W!=dst[0].W||dst[i].H!=dst[0].H))throw new Exception("Размеры кадров различаются."); }
            cell.Text=c;offsetX.Text=ox;offsetY.Text=oy;tolerance.Text=t;levels.Text=l;keyColor.Text=color;key.IsChecked=k;autoBackground.IsChecked=automatic;edgeBackground.IsChecked=edges;cleanup.Text=clean.ToString();fps.Text=rate.ToString(); sources=src;frames=dst;undo.Clear();current=0;changed=false;RefreshTimeline();MarkStudioDirty();Tell("Проект открыт.");
        }
    }
    public void Demo() {
        if(!ReplaceOK())return; cell.Text="6";offsetX.Text="0";offsetY.Text="0";keyColor.Text="#00FF00"; key.IsChecked=true;
        var demo=new List<Frame>(); for(int n=0;n<8;n++) { var f=new Frame(288,288); for(int i=0;i<f.Data.Length;i+=4) { f.Data[i+1]=255;f.Data[i+3]=255; }
            Action<int,int,int,int,Color> rect=(x,y,w,h,c)=>{ for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++) { int p=(yy*f.W+xx)*4; f.Data[p]=c.B;f.Data[p+1]=c.G;f.Data[p+2]=c.R;f.Data[p+3]=255; } };
            int bob=(n%4==1||n%4==2)?6:0,step=(n<4?n:7-n)*6;
            rect(114,42+bob,54,48,Color.FromRgb(241,191,142));rect(108,36+bob,66,18,Color.FromRgb(51,43,65));rect(156,60+bob,6,6,Colors.Black);
            rect(108,96+bob,66,72,Color.FromRgb(83,117,218));rect(96-step/2,102+bob,12,60,Color.FromRgb(241,191,142));rect(174+step/2,108+bob,12,54,Color.FromRgb(241,191,142));
            rect(114-step/2,168+bob,24,66-bob,Color.FromRgb(43,53,83));rect(144+step/2,168+bob,24,66-bob,Color.FromRgb(43,53,83));rect(108-step/2,234,36,12,Color.FromRgb(21,26,42));rect(144+step/2,234,36,12,Color.FromRgb(21,26,42));demo.Add(f); }
        Reset(demo); Tell("Демо: 8 кадров, сетка 6 px → 48 × 48. Можно рисовать, сохранять проект и экспортировать.");
    }
    public async Task SceneSmoke(string dir){try{mainTabs.SelectedIndex=1;await Task.Delay(100);await cutscenes.SelfTest(dir);changed=false;Application.Current.Shutdown(0);}catch{changed=false;Application.Current.Shutdown(1);}}
    public async Task Smoke(string dir) {
        try {
            Directory.CreateDirectory(dir); Demo();
            if(frames.Count!=8||frames[0].W!=48||frames[0].Data[3]!=0)throw new Exception("Demo processing failed");
            if(!frames[0].Data.Where((b,i)=>i%4==3).All(b=>b==0||b==255))throw new Exception("Alpha not binary");
            frames[0].Save(Path.Combine(dir,"test-frame.png")); var re=Frame.Load(Path.Combine(dir,"test-frame.png")); if(!re.Data.SequenceEqual(frames[0].Data))throw new Exception("PNG round-trip failed");
            var sheet=Pixels.Sheet(frames,4); if(sheet.W!=192||sheet.H!=96)throw new Exception("Sheet dimensions failed"); sheet.Save(Path.Combine(dir,"test-sheet.png"));
            for(int n=0;n<frames.Count;n++)for(int y=0;y<48;y++)for(int x=0;x<48;x++)for(int c=0;c<4;c++)if(sheet.Data[(((n/4*48+y)*sheet.W)+(n%4*48+x))*4+c]!=frames[n].Data[(y*48+x)*4+c])throw new Exception("Sheet placement failed");
            using(var ms=new MemoryStream()) { using(var w=new BinaryWriter(ms,System.Text.Encoding.UTF8,true)) {WriteFrame(w,sources[0]);WriteFrame(w,frames[0]);} ms.Position=0;using(var r=new BinaryReader(ms)) {long total=0;if(!ReadFrame(r,ref total).Data.SequenceEqual(sources[0].Data)||!ReadFrame(r,ref total).Data.SequenceEqual(frames[0].Data))throw new Exception("Project frame round-trip failed");} }
            var exact=new Frame(8,8);for(int i=0;i<exact.Data.Length;i+=4) {exact.Data[i]=(byte)((i/4%8)/4*255);exact.Data[i+3]=255;} var small=Pixels.Process(exact,4,0,0,false,Colors.Lime,0,8);if(small.W!=2||small.Data[0]!=0||small.Data[4]!=255)throw new Exception("Grid test failed");
            var bgTest=new Frame(7,7);for(int y=0;y<7;y++)for(int x=0;x<7;x++){int p=(y*7+x)*4;bool ring=x>=1&&x<=5&&y>=1&&y<=5&&(x==1||x==5||y==1||y==5);for(int c=0;c<3;c++)bgTest.Data[p+c]=(byte)(ring?0:250);bgTest.Data[p+3]=255;}
            var detected=Pixels.BackgroundColor(bgTest);if(detected.R!=250||detected.G!=250||detected.B!=250)throw new Exception("Auto background detection failed");
            var cut=Pixels.Process(bgTest,1,0,0,true,detected,10,8,true);if(cut.Data[3]!=0||cut.Data[(3*7+3)*4+3]!=255||cut.Data[(1*7+1)*4+3]!=255)throw new Exception("Connected background protection failed");
            var all=Pixels.Process(bgTest,1,0,0,true,detected,10,8,false);if(all.Data[(3*7+3)*4+3]!=0)throw new Exception("Global background removal failed");
            var faded=bgTest.Clone();faded.Data[0]=240;var tolerant=Pixels.BackgroundMask(faded,detected,12,true);if(!tolerant[0])throw new Exception("Background tolerance failed");
            bgTest.Save(Path.Combine(dir,"background-before.png"));cut.Save(Path.Combine(dir,"background-after.png"));
            var halo=new Frame(30,22);Action<int,int,byte,byte,byte> put=(x,y,r,g,b)=>{int p=(y*halo.W+x)*4;halo.Data[p]=b;halo.Data[p+1]=g;halo.Data[p+2]=r;halo.Data[p+3]=255;};
            for(int y=4;y<=16;y++)for(int x=4;x<=15;x++){bool edge=x==4||x==15||y==4||y==16;put(x,y,(byte)(edge?117:180),(byte)(edge?71:110),(byte)(edge?108:30));}
            put(4,9,36,22,210);put(1,1,20,10,190);
            for(int y=4;y<=14;y++)for(int x=23;x<=27;x++)put(x,y,20,30,150);
            put(22,19,255,125,10);
            var originalHalo=halo.Clone();var fixedHalo=Pixels.CleanEdges(halo,Colors.Blue,2);
            int edgeP=(8*halo.W+4)*4,lowP=(9*halo.W+4)*4;
            if(fixedHalo.Data[edgeP+2]!=180||fixedHalo.Data[edgeP+1]!=110||fixedHalo.Data[edgeP]!=30)throw new Exception("Edge foreground reconstruction failed");
            if(fixedHalo.Data[lowP+3]!=0||fixedHalo.Data[(1*halo.W+1)*4+3]!=0)throw new Exception("Background-dominant rim or detached speck removal failed");
            for(int y=4;y<=14;y++)for(int x=23;x<=27;x++)for(int c=0;c<4;c++)if(fixedHalo.Data[(y*halo.W+x)*4+c]!=halo.Data[(y*halo.W+x)*4+c])throw new Exception("Genuine blue region was changed");
            if(fixedHalo.Data[(19*halo.W+22)*4+3]!=255)throw new Exception("Orange spark was removed");
            if(!Pixels.CleanEdges(halo,Colors.Blue,0).Data.SequenceEqual(halo.Data))throw new Exception("Cleanup-off changed pixels");
            if(!halo.Data.SequenceEqual(originalHalo.Data))throw new Exception("Cleanup mutated input");
            if(!fixedHalo.Data.SequenceEqual(Pixels.CleanEdges(halo,Colors.Blue,2).Data))throw new Exception("Cleanup not deterministic");
            if(fixedHalo.Data.Where((v,i)=>i%4==3).Any(v=>v!=0&&v!=255))throw new Exception("Cleanup alpha not binary");
            halo.Save(Path.Combine(dir,"halo-before.png"));fixedHalo.Save(Path.Combine(dir,"halo-after.png"));
            var keyed=halo.Clone();for(int p=0;p<keyed.Data.Length;p+=4)if(keyed.Data[p+3]==0){keyed.Data[p]=255;keyed.Data[p+1]=keyed.Data[p+2]=0;keyed.Data[p+3]=255;}
            var processed=Pixels.Process(keyed,1,0,0,true,Colors.Blue,25,32,false,2);
            if(processed.Data[lowP+3]!=0||processed.Data[edgeP+3]!=255||processed.Data[edgeP]>=60)throw new Exception("Keying + grid + cleanup integration failed");
            var transparent=new Frame(8,8);for(int y=2;y<6;y++)for(int x=2;x<6;x++)transparent.Data[(y*8+x)*4+3]=255;
            var preserved=ConvertFrame(transparent,1,0,0,Colors.Blue,55,8);if(preserved.Data[(3*8+3)*4+3]!=255)throw new Exception("Transparent PNG black foreground was removed");
            var testFrames=new List<Frame>();for(int n=0;n<5;n++){var f=new Frame(6,6);for(int p=0;p<f.Data.Length;p+=4){f.Data[p]=(byte)(n+20);f.Data[p+1]=70;f.Data[p+2]=140;f.Data[p+3]=255;}testFrames.Add(f);}
            var snapshots=testFrames.Select(f=>f.Clone()).ToList();var stroke=new PixelStroke(testFrames,2,true,true,Colors.White);stroke.Paint(1,1);stroke.Paint(1,1);stroke.Paint(2,1);
            for(int n=0;n<5;n++){if(n<2&&!testFrames[n].Data.SequenceEqual(snapshots[n].Data))throw new Exception("Forward eraser touched previous frames");if(n>=2&&(testFrames[n].Data[(1*6+1)*4+3]!=0||testFrames[n].Data[(1*6+2)*4+3]!=0))throw new Exception("Forward eraser missed later frames");}
            var overlap=new PixelStroke(testFrames,3,false,true,Colors.Yellow);overlap.Paint(1,1);
            if(testFrames[3].Data[(1*6+1)*4+3]!=255||testFrames[4].Data[(1*6+1)*4+3]!=0)throw new Exception("Pencil propagated despite eraser-only scope");
            overlap.Undo();stroke.Undo();for(int n=0;n<5;n++)if(!testFrames[n].Data.SequenceEqual(snapshots[n].Data))throw new Exception("Multi-frame undo failed or overlapping stroke lost original pixels");
            var local=new PixelStroke(testFrames,1,true,false,Colors.White);local.Paint(3,3);for(int n=0;n<5;n++)if(n!=1&&!testFrames[n].Data.SequenceEqual(snapshots[n].Data))throw new Exception("Local eraser touched other frames");local.Undo();
            testFrames[2].Data[(2*6+2)*4+3]=0;var emptyOrigin=new PixelStroke(testFrames,2,true,true,Colors.White);emptyOrigin.Paint(2,2);if(testFrames[3].Data[(2*6+2)*4+3]!=0||testFrames[4].Data[(2*6+2)*4+3]!=0)throw new Exception("Forward eraser skipped transparent origin");emptyOrigin.Undo();
            var lastStroke=new PixelStroke(testFrames,4,true,true,Colors.White);lastStroke.Paint(5,5);lastStroke.Paint(-1,0);lastStroke.Undo();
            var demoSnapshots=frames.Select(f=>f.Clone()).ToList();Select(3);eraseScope.SelectedIndex=1;editor.BeforeStroke(true);editor.PixelPainted(20,12);editor.StrokeEnded();
            for(int n=0;n<frames.Count;n++)if(n<3&&!frames[n].Data.SequenceEqual(demoSnapshots[n].Data)||n>=3&&frames[n].Data[(12*frames[n].W+20)*4+3]!=0)throw new Exception("Editor callback forward erasing failed");
            Undo();for(int n=0;n<frames.Count;n++)if(!frames[n].Data.SequenceEqual(demoSnapshots[n].Data))throw new Exception("Editor undo integration failed");eraseScope.SelectedIndex=0;Select(0);
            await Task.Delay(300); UpdateLayout(); var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); bmp.Render(this); Frame.From(bmp).Save(Path.Combine(dir,"preview.png"));
            foreach(int size in new[]{720,900,1600}){
                Width=size;Height=size==720?640:900;UpdateLayout();ResponsiveLayout();UpdateLayout();await Task.Delay(120);
                if(settings.ActualWidth>settingsScroll.ViewportWidth+1)throw new Exception("Settings exceed scroll viewport at width "+size);
                if(editor.ActualWidth<100||editor.ActualHeight<90)throw new Exception("Canvas unusable at width "+size);
                foreach(var child in settings.Children){var text=child as TextBlock;if(text!=null&&text.TextWrapping==TextWrapping.Wrap&&text.ActualHeight+text.Margin.Top+text.Margin.Bottom+1<text.DesiredSize.Height)throw new Exception("Help text clipped at width "+size);}
                var shot=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(this);Frame.From(shot).Save(Path.Combine(dir,"layout-"+size+".png"));
                if(size==1600){settingsScroll.ScrollToBottom();UpdateLayout();var bottomShot=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bottomShot.Render(this);Frame.From(bottomShot).Save(Path.Combine(dir,"editor-controls.png"));}
            }
            string videoResult="Video decoding not tested by this check.";
            if(File.Exists(Path.Combine(dir,"test.avi"))) {
                await LoadVideo(Path.Combine(dir,"test.avi"));
                var a=await Capture(0,96,96);var b=await Capture(1,96,96);
                a.Save(Path.Combine(dir,"video-first.png"));b.Save(Path.Combine(dir,"video-later.png"));
                if(a.Data.SequenceEqual(b.Data)||a.Data.Where((v,i)=>i%4==1).Max()<200)throw new Exception("Video seek/capture failed: blank or repeated frames");
                videoResult="PASS: generated AVI decoding; seeking to distinct frames; captured green background.";
            }
            File.WriteAllText(Path.Combine(dir,"checks.txt"),"PASS: demo processing; binary alpha; PNG round-trip; sheet geometry and per-pixel placement; project frame round-trip; grid dominant color.\r\nPASS: automatic background detection; connected background removal; enclosed same-color detail protection; global background removal; tolerance.\r\nPASS: edge color reconstruction; background-dominant halo removal; isolated background speck removal; genuine blue region preservation; orange spark preservation; cleanup disabled; deterministic output; binary alpha; integrated key/grid/cleanup.\r\nPASS: forward eraser from middle frame; previous frames unchanged; repeated and overlapping stroke undo; pencil stays local; local eraser; transparent origin; last frame; editor callbacks and undo integration.\r\nPASS: responsive 720/900/1600 widths; settings fit viewport; wrapped help height; usable canvas.\r\n"+videoResult+"\r\n"); changed=false; Application.Current.Shutdown(0);
        } catch(Exception e) { File.WriteAllText(Path.Combine(dir,"checks.txt"),"FAIL: "+e);changed=false; Application.Current.Shutdown(1); }
    }
}
public static class Program {
    [STAThread] public static void Main(string[] args) {
        var app=new Application(); var window=new MainWindow();window.AutomatedTest=args.Length==2&&new[]{"--self-test","--scene-test","--stability-test","--ambient-test","--studio-test"}.Contains(args[0]);
        app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"PixelForge"); e.Handled=true;};
        if(args.Length==2&&args[0]=="--self-test") window.Loaded+=async(s,e)=>await window.Smoke(args[1]);
        if(args.Length==2&&args[0]=="--stability-test") window.Loaded+=async(s,e)=>await window.StabilitySmoke(args[1]);
        if(args.Length==2&&args[0]=="--ambient-test") window.Loaded+=async(s,e)=>await window.AmbientSmoke(args[1]);
        if(args.Length==2&&args[0]=="--studio-test") window.Loaded+=async(s,e)=>await window.StudioSmoke(args[1]);
        if(args.Length==2&&args[0]=="--scene-test") window.Loaded+=async(s,e)=>await window.SceneSmoke(args[1]);
        app.Run(window);
    }
}
}









