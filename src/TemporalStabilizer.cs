using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelForge {
// Shared palette, short noise filter and fixed-color stationary spans. Alpha is never edited.
public static class TemporalStabilizer {
    public static Frame QuantizeFrame(Frame source,int colors){if(colors<16||colors>256)throw new Exception("Палитра: 16…256 цветов.");return Quantize(new List<Frame>{source},colors)[0];}
    static readonly object gate=new object();
    static readonly ConditionalWeakTable<List<Frame>,Dictionary<string,Task<List<Frame>>>> cache=new ConditionalWeakTable<List<Frame>,Dictionary<string,Task<List<Frame>>>>();
    public static event Action Completed;
    public static Task<List<Frame>> Prepare(List<Frame> frames,int strength,int colors,bool loop,int referenceThreshold=56){
        if(strength==0||frames.Count<2)return Task.FromResult(frames);
        string key=strength+":"+colors+":"+loop+":"+referenceThreshold;lock(gate){var states=cache.GetOrCreateValue(frames);Task<List<Frame>> task;if(states.TryGetValue(key,out task))return task;
            if(states.Count>=3){var old=states.FirstOrDefault(p=>p.Value.IsCompleted);if(old.Key!=null)states.Remove(old.Key);}
            task=Task.Run(()=>Compute(frames,strength,colors,loop,referenceThreshold));states[key]=task;
            task.ContinueWith(t=>{var notify=Completed;var app=Application.Current;if(notify!=null&&app!=null&&!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(()=>notify()));},TaskScheduler.Default);return task;}
    }
    public static IList<Frame> Display(SceneLayer layer){if(layer.Stabilization==0||layer.Frames.Count<2)return layer.Frames;var task=Prepare(layer.Frames,layer.Stabilization,layer.PaletteSize,layer.Loop,layer.ReferenceThreshold);return task.Status==TaskStatus.RanToCompletion?task.Result:layer.Frames;}
    public static async Task PrepareScene(Cutscene scene,CancellationToken token){foreach(var l in scene.Layers){token.ThrowIfCancellationRequested();if(l.Stabilization>0&&l.Frames.Count>1){var task=Prepare(l.Frames,l.Stabilization,l.PaletteSize,l.Loop,l.ReferenceThreshold);while(!task.IsCompleted){await Task.Delay(30,token);}await task;}}}
    sealed class Sample {public int R,G,B,N;}
    static int Distance(byte[] a,int p,byte[] b,int q){return Math.Max(Math.Abs(a[p]-b[q]),Math.Max(Math.Abs(a[p+1]-b[q+1]),Math.Abs(a[p+2]-b[q+2])));}
    static int Code(byte[] a,int p){return (a[p]>>3)|((a[p+1]>>3)<<5)|((a[p+2]>>3)<<10);}
    static int Channel(Sample p,int c){return c==0?p.R:c==1?p.G:p.B;}
    static List<Sample> Palette(IList<Frame> frames,int colors){
        var histogram=new Dictionary<int,long[]>();long total=frames.Sum(f=>(long)f.W*f.H);int step=(int)Math.Max(1,total/500000);
        foreach(var f in frames)for(int pixel=0;pixel<f.W*f.H;pixel+=step){int p=pixel*4;if(f.Data[p+3]<128)continue;int code=Code(f.Data,p);long[] value;if(!histogram.TryGetValue(code,out value)){value=new long[4];histogram[code]=value;}value[0]+=f.Data[p+2];value[1]+=f.Data[p+1];value[2]+=f.Data[p];value[3]++;}
        var points=histogram.Values.Select(v=>new Sample {R=(int)(v[0]/v[3]),G=(int)(v[1]/v[3]),B=(int)(v[2]/v[3]),N=(int)v[3]}).ToList();if(points.Count==0)return new List<Sample>{new Sample()};
        var boxes=new List<List<Sample>>{points};
        while(boxes.Count<colors){int index=-1,axis=0;double best=-1;for(int n=0;n<boxes.Count;n++){var b=boxes[n];if(b.Count<2)continue;int r=b.Max(p=>p.R)-b.Min(p=>p.R),g=b.Max(p=>p.G)-b.Min(p=>p.G),blue=b.Max(p=>p.B)-b.Min(p=>p.B),c=r>=g&&r>=blue?0:g>=blue?1:2;double score=Math.Max(r,Math.Max(g,blue))*(1+Math.Log(1+b.Sum(p=>p.N)));if(score>best){best=score;index=n;axis=c;}}
            if(index<0)break;var sorted=boxes[index].OrderBy(p=>Channel(p,axis)).ToList();int half=sorted.Sum(p=>p.N)/2,count=0,split=1;for(int n=0;n<sorted.Count-1;n++){count+=sorted[n].N;split=n+1;if(count>=half)break;}boxes[index]=sorted.Take(split).ToList();boxes.Add(sorted.Skip(split).ToList());}
        var result=new List<Sample>();foreach(var b in boxes){double weight=b.Sum(p=>p.N),r=b.Sum(p=>(double)p.R*p.N)/weight,g=b.Sum(p=>(double)p.G*p.N)/weight,blue=b.Sum(p=>(double)p.B*p.N)/weight;result.Add(b.OrderBy(p=>(p.R-r)*(p.R-r)+(p.G-g)*(p.G-g)+(p.B-blue)*(p.B-blue)).First());}return result;
    }
    static List<Frame> Quantize(IList<Frame> frames,int colors){var palette=Palette(frames,colors);var lookup=new Dictionary<int,Sample>();var output=new List<Frame>();foreach(var original in frames){var f=original.Clone();for(int p=0;p<f.Data.Length;p+=4){if(f.Data[p+3]<128)continue;int code=Code(f.Data,p);Sample nearest;if(!lookup.TryGetValue(code,out nearest)){int r=f.Data[p+2],g=f.Data[p+1],b=f.Data[p];nearest=palette.OrderBy(c=>(c.R-r)*(c.R-r)+(c.G-g)*(c.G-g)+(c.B-b)*(c.B-b)).First();lookup[code]=nearest;}f.Data[p]=(byte)nearest.B;f.Data[p+1]=(byte)nearest.G;f.Data[p+2]=(byte)nearest.R;}output.Add(f);}return output;}
    static int PatchPart(byte[] a,byte[] b,int pixel){int p=pixel*4;if(a[p+3]<128&&b[p+3]<128)return 0;return Math.Abs(a[p+3]-b[p+3])>=128?255:Distance(a,p,b,p);}
    static double Patch(IList<Frame> frames,int a,int b,int pixel){var f=frames[a];var aa=f.Data;var bb=frames[b].Data;int x=pixel%f.W,y=pixel/f.W,n=1;double sum=PatchPart(aa,bb,pixel);if(x>0){sum+=PatchPart(aa,bb,pixel-1);n++;}if(x<f.W-1){sum+=PatchPart(aa,bb,pixel+1);n++;}if(y>0){sum+=PatchPart(aa,bb,pixel-f.W);n++;}if(y<f.H-1){sum+=PatchPart(aa,bb,pixel+f.W);n++;}return sum/n;}
    static int Index(int frame,int count,bool loop){if(loop)return (frame%count+count)%count;return frame<0||frame>=count?-1:frame;}
    static bool LocalMotion(Frame a,Frame b,int pixel){
        int x=pixel%a.W,y=pixel/a.W,strong=0;for(int yy=Math.Max(0,y-1);yy<=Math.Min(a.H-1,y+1);yy++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(a.W-1,x+1);xx++){int q=(yy*a.W+xx)*4;if((a.Data[q+3]<128)!=(b.Data[q+3]<128))return true;if(a.Data[q+3]>=128&&Distance(a.Data,q,b.Data,q)>=56&&++strong>=2)return true;}return false;
    }
    // The descriptor is used only to detect movement. Output colors are observed palette
    // entries, never spatial averages. Comparing against a fixed reference prevents drift.
    static List<byte[]> Descriptors(IList<Frame> frames){
        var result=new List<byte[]>();foreach(var f in frames){var descriptor=new byte[f.Data.Length];for(int y=0;y<f.H;y++)for(int x=0;x<f.W;x++){
            int b=0,g=0,r=0,alpha=0,n=0;for(int yy=Math.Max(0,y-2);yy<=Math.Min(f.H-1,y+2);yy++)for(int xx=Math.Max(0,x-2);xx<=Math.Min(f.W-1,x+2);xx++){int q=(yy*f.W+xx)*4;n++;if(f.Data[q+3]<128)continue;b+=f.Data[q];g+=f.Data[q+1];r+=f.Data[q+2];alpha+=255;}
            int p=(y*f.W+x)*4;descriptor[p]=(byte)(b/n);descriptor[p+1]=(byte)(g/n);descriptor[p+2]=(byte)(r/n);descriptor[p+3]=(byte)(alpha/n);
        }result.Add(descriptor);}return result;
    }
    static bool Stationary(IList<Frame> source,List<byte[]> descriptors,int a,int b,int pixel,int colorLimit,int areaLimit){
        int p=pixel*4;var aa=source[a].Data;var bb=source[b].Data;
        if(aa[p+3]<128||bb[p+3]<128||Distance(aa,p,bb,p)>colorLimit||Distance(descriptors[a],p,descriptors[b],p)>areaLimit)return false;
        // A changing local silhouette is motion even when its average color is similar.
        if(LocalMotion(source[a],source[b],pixel))return false;
        return true;
    }
    static void HoldStationary(IList<Frame> source,IList<Frame> originals,List<Frame> output,int strength,bool loop,int referenceThreshold){
        int count=source.Count,pixels=source[0].W*source[0].H;
        int minimum=strength==1?8:strength==2?6:4,colorLimit=referenceThreshold,areaLimit=referenceThreshold;
        if(count<minimum)return;var descriptors=Descriptors(source);
        for(int pixel=0;pixel<pixels;pixel++){
            int p=pixel*4,origin=0;
            if(loop){int largest=-1;for(int f=0;f<count;f++){int previous=(f+count-1)%count;int d=Distance(descriptors[f],p,descriptors[previous],p)+Math.Abs(descriptors[f][p+3]-descriptors[previous][p+3]);if(d>largest){largest=d;origin=f;}}}
            int start=0;while(start<count){int anchor=(origin+start)%count,end=start+1;while(end<count){int next=(origin+end)%count,previous=(origin+end-1)%count;if(!Stationary(source,descriptors,anchor,next,pixel,colorLimit,areaLimit)||!Stationary(source,descriptors,previous,next,pixel,colorLimit,areaLimit))break;end++;}
                if(end-start>=minimum){int reference=anchor;for(int i=start;i<end;i++)if((origin+i)%count==0){reference=0;break;}var original=originals[reference].Data;int best=original[p]|(original[p+1]<<8)|(original[p+2]<<16);
                    for(int i=start;i<end;i++){var data=output[(origin+i)%count].Data;data[p]=(byte)best;data[p+1]=(byte)(best>>8);data[p+2]=(byte)(best>>16);}
                }start=end;
            }
        }
    }
    public static List<Frame> Compute(IList<Frame> input,int strength,int colors,bool loop,int referenceThreshold=56){
        if(strength<0||strength>3||colors<16||colors>256||referenceThreshold<8||referenceThreshold>128)throw new Exception("Стабилизация: сила 0…3, палитра 16…256 цветов.");
        if(input.Count==0)return new List<Frame>();if(input.Any(f=>f.W!=input[0].W||f.H!=input[0].H))throw new Exception("Для стабилизации нужны одинаковые размеры кадров.");
        if(strength==0||input.Count<2)return input.Select(f=>f.Clone()).ToList();
        var source=Quantize(input,colors);var output=source.Select(f=>f.Clone()).ToList();int count=source.Count,pixels=source[0].W*source[0].H,radius=strength,threshold=strength==1?40:strength==2?64:90;double patchLimit=threshold*.65;
        var samples=new int[7];
        for(int pass=0;pass<(loop?2:1);pass++)for(int frame=0;frame<count;frame++)for(int pixel=0;pixel<pixels;pixel++){
            int p=pixel*4;if(source[frame].Data[p+3]<128)continue;int accepted=0;
            for(int d=-radius;d<=radius;d++){int n=Index(frame+d,count,loop);if(n<0||source[n].Data[p+3]<128||Distance(source[frame].Data,p,source[n].Data,p)>threshold||Patch(source,frame,n,pixel)>patchLimit||LocalMotion(source[frame],source[n],pixel))continue;samples[accepted++]=n;}
            int best=frame,score=int.MaxValue;for(int k=0;k<accepted;k++){int sum=0;for(int j=0;j<accepted;j++)sum+=Distance(source[samples[k]].Data,p,source[samples[j]].Data,p);if(sum<score||(sum==score&&samples[k]==frame)){score=sum;best=samples[k];}}
            int previous=Index(frame-1,count,loop);bool hold=false;if(previous>=0&&(pass>0||frame>0)&&output[previous].Data[p+3]>=128&&Distance(output[previous].Data,p,source[frame].Data,p)<=threshold&&Patch(source,frame,previous,pixel)<=patchLimit&&!LocalMotion(source[frame],source[previous],pixel)){
                for(int d=1;d<=radius;d++){int future=Index(frame+d,count,loop);if(future>=0&&source[future].Data[p+3]>=128&&Distance(output[previous].Data,p,source[future].Data,p)<=12&&Patch(source,frame,future,pixel)<=patchLimit&&!LocalMotion(source[frame],source[future],pixel)){hold=true;break;}}}
            for(int c=0;c<3;c++)output[frame].Data[p+c]=hold?output[previous].Data[p+c]:source[best].Data[p+c];
        }
        HoldStationary(source,input,output,strength,loop,referenceThreshold);
        output[0]=input[0].Clone(); // Preserve the artistic reference exactly.
        return output;
    }
}
public interface IFrameEdit {int StartFrame {get;}IEnumerable<int> AffectedFrames {get;}void Undo();}
public sealed class FrameSetEdit : IFrameEdit {
    readonly IList<Frame> target,previous;readonly int start;
    public FrameSetEdit(IList<Frame> target,IList<Frame> previous,int start){this.target=target;this.previous=previous;this.start=start;}
    public int StartFrame {get{return start;}}public IEnumerable<int> AffectedFrames {get{return Enumerable.Range(0,target.Count);}}
    public void Undo(){for(int n=0;n<target.Count;n++)target[n]=previous[n];}
}
}
