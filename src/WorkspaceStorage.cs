using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PixelForge {
public sealed class BackgroundEntry {
    public string Id,Name,Fingerprint,UpdatedUtc;
    public int Width,Height,Fps;public double Duration;
    [ScriptIgnore] public string FilePath {get{return Path.Combine(BackgroundLibrary.Root,Id,"background.bgscene");}}
    [ScriptIgnore] public string ThumbnailPath {get{return Path.Combine(BackgroundLibrary.Root,Id,"thumbnail.png");}}
}
public static class BackgroundLibrary {
    public static string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"library","backgrounds");
    static readonly object gate=new object();public static event Action Changed;
    static bool SafeId(string id){return id!=null&&id.Length==32&&id.All(c=>Uri.IsHexDigit(c));}
    public static string FileId(string path){using(var h=SHA256.Create())using(var s=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").Substring(0,32).ToLowerInvariant();}
    public static string Fingerprint(AmbientProject p){using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms,Encoding.UTF8,true)){
        w.Write(p.Name??"");w.Write(p.Width);w.Write(p.Height);w.Write(p.Colors);w.Write(p.Fps);w.Write(p.Duration);w.Write(p.OutputWidth);w.Write(p.OutputHeight);w.Write(p.Cover);w.Write(p.Background??"");
        foreach(var f in new[]{p.Photo,p.Base}){w.Write(f.W);w.Write(f.H);w.Write(f.Data);}foreach(var r in p.Regions){w.Write(r.Name??"");w.Write(r.Effect);w.Write(r.Cycles);w.Write(r.Amplitude);w.Write(r.Lightness);w.Write(r.Phase);w.Write(r.Enabled);foreach(bool b in r.Mask)w.Write(b);}w.Flush();ms.Position=0;using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(ms)).Replace("-","");}}
    public static Frame Thumbnail(Frame source,int w=240,int h=135){var result=new Frame(w,h);double scale=Math.Min(w/(double)source.W,h/(double)source.H);int rw=Math.Max(1,(int)(source.W*scale)),rh=Math.Max(1,(int)(source.H*scale)),ox=(w-rw)/2,oy=(h-rh)/2;for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=(y*w+x)*4;result.Data[p]=34;result.Data[p+1]=27;result.Data[p+2]=20;result.Data[p+3]=255;if(x<ox||x>=ox+rw||y<oy||y>=oy+rh)continue;int sx=Math.Min(source.W-1,(x-ox)*source.W/rw),sy=Math.Min(source.H-1,(y-oy)*source.H/rh);Buffer.BlockCopy(source.Data,(sy*source.W+sx)*4,result.Data,p,4);}return result;}
    public static void AtomicText(string path,string text){string tmp=path+".writing-"+Guid.NewGuid().ToString("N");try{File.WriteAllText(tmp,text,Encoding.UTF8);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    public static List<BackgroundEntry> List(){lock(gate){if(!Directory.Exists(Root))return new List<BackgroundEntry>();var entries=new List<BackgroundEntry>();foreach(string dir in Directory.GetDirectories(Root)){string file=Path.Combine(dir,"info.json");if(!File.Exists(file))continue;try{var e=new JavaScriptSerializer().Deserialize<BackgroundEntry>(File.ReadAllText(file));if(e!=null&&SafeId(e.Id)&&File.Exists(e.FilePath))entries.Add(e);}catch{}}return entries.OrderByDescending(e=>e.UpdatedUtc,StringComparer.Ordinal).ToList();}}
    public static BackgroundEntry Upsert(AmbientProject p,bool preserveExisting=false){p.Validate();if(p.Photo==null||p.Base==null)throw new Exception("Нет готового фона для галереи.");if(!SafeId(p.LibraryId))p.LibraryId=Guid.NewGuid().ToString("N");string fingerprint=Fingerprint(p);BackgroundEntry entry;
        lock(gate){Directory.CreateDirectory(Root);var old=List().FirstOrDefault(e=>e.Id==p.LibraryId);if(old!=null&&old.Fingerprint==fingerprint)return old;if(old!=null&&preserveExisting)p.LibraryId=Guid.NewGuid().ToString("N");
            string directory=Path.Combine(Root,p.LibraryId);Directory.CreateDirectory(directory);entry=new BackgroundEntry {Id=p.LibraryId,Name=p.Name,Fingerprint=fingerprint,Width=p.Width,Height=p.Height,Fps=p.Fps,Duration=p.Duration,UpdatedUtc=DateTime.UtcNow.ToString("o")};AmbientFile.Save(p,entry.FilePath);string temp=entry.ThumbnailPath+".writing.png";Thumbnail(p.Base).Save(temp);if(File.Exists(entry.ThumbnailPath))File.Replace(temp,entry.ThumbnailPath,null);else File.Move(temp,entry.ThumbnailPath);AtomicText(Path.Combine(directory,"info.json"),new JavaScriptSerializer().Serialize(entry));}
        var changed=Changed;if(changed!=null)changed();return entry;
    }
    public static AmbientProject Load(string id){if(!SafeId(id))throw new Exception("Некорректный фон галереи.");lock(gate){var p=AmbientFile.Load(Path.Combine(Root,id,"background.bgscene"));p.LibraryId=id;return p;}}
    public static void ImportExisting(){string marker=Path.Combine(Root,"existing-imported.flag");if(File.Exists(marker))return;foreach(string name in new[]{"examples","exports","projects"}){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);if(!Directory.Exists(dir))continue;foreach(string file in Directory.GetFiles(dir,"*.bgscene",SearchOption.AllDirectories))try{var p=AmbientFile.Load(file);if(string.IsNullOrEmpty(p.LibraryId))p.LibraryId=FileId(file);Upsert(p,true);}catch{}}Directory.CreateDirectory(Root);AtomicText(marker,"1");}
}
public sealed class SpriteWorkspace {
    public int BrushSize=1,EraseSize=1;public string ReferenceThreshold="56";
    public string Cell="4",OffsetX="0",OffsetY="0",Tolerance="55",Levels="8",KeyColor="#00FF00",Ink="#F6D7B0",Cleanup="2",Fps="12",Count="96",Stability="2",StableColors="64";
    public bool Key=true,Auto=true,Edges=false,Grid=true;public int Current,Tool,EraseScope;
    [ScriptIgnore] public List<Frame> Sources=new List<Frame>();[ScriptIgnore] public List<Frame> Frames=new List<Frame>();
    static void WriteFrame(BinaryWriter w,Frame f){w.Write(f.W);w.Write(f.H);w.Write(f.Data);}
    static Frame ReadFrame(BinaryReader r,ref long total){int w=r.ReadInt32(),h=r.ReadInt32();if(w<1||h<1||w>8192||h>8192)throw new Exception("Некорректный кадр проекта.");total+=(long)w*h;if(total>64000000)throw new Exception("Слишком много исходных пикселей проекта.");var f=new Frame(w,h);int offset=0;while(offset<f.Data.Length){int n=r.Read(f.Data,offset,f.Data.Length-offset);if(n<=0)throw new Exception("Кадр проекта обрезан.");offset+=n;}return f;}
    public void Write(Stream stream){if(Sources.Count!=Frames.Count||Frames.Count>240)throw new Exception("Некорректные кадры спрайтов.");using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){w.Write("STUDIO-SPRITES-1");w.Write(Frames.Count);for(int n=0;n<Frames.Count;n++){WriteFrame(w,Sources[n]);WriteFrame(w,Frames[n]);}}}
    public void Read(Stream stream){using(var r=new BinaryReader(stream,Encoding.UTF8,true)){if(r.ReadString()!="STUDIO-SPRITES-1")throw new Exception("Неизвестные данные спрайтов.");int count=r.ReadInt32();if(count<0||count>240)throw new Exception("Некорректное число кадров.");long total=0;for(int n=0;n<count;n++){Sources.Add(ReadFrame(r,ref total));Frames.Add(ReadFrame(r,ref total));if(n>0&&(Sources[n].W!=Sources[0].W||Sources[n].H!=Sources[0].H||Frames[n].W!=Frames[0].W||Frames[n].H!=Frames[0].H))throw new Exception("Кадры разного размера.");}}}
}
public sealed class StudioManifest {
    public int Version=1,ActiveTab=1;public string Name="Новый проект",VideoEntry;
    public double SceneTime;public string SelectedLayer,SelectedRegion;public int ScenePanel,BackgroundFrame,BackgroundPanel;
    public SpriteWorkspace Sprites=new SpriteWorkspace();public AmbientProject EmptyBackground=new AmbientProject();public List<string> GalleryIds=new List<string>();
}
public sealed class StudioState {
    public StudioManifest Manifest=new StudioManifest();public Cutscene Scene=new Cutscene();public AmbientProject Background=new AmbientProject();public Dictionary<string,AmbientProject> Library=new Dictionary<string,AmbientProject>();public string VideoPath;public Frame Preview;
}
public static class StudioFile {
    public static void CleanupVideo(string path){if(string.IsNullOrEmpty(path))return;try{string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","studio-assets")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,full=Path.GetFullPath(path),directory=Path.GetDirectoryName(full);Guid id;if(!directory.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(directory),"N",out id))return;if(File.Exists(full))File.Delete(full);if(Directory.Exists(directory)&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}}
    static void CopyFile(ZipArchive zip,string entry,string file){using(var source=File.OpenRead(file))using(var target=zip.CreateEntry(entry,CompressionLevel.Fastest).Open())source.CopyTo(target);}
    static string Temp(){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","studio-io",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return dir;}
    static void Cleanup(string dir){string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","studio-io")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(Path.GetFullPath(dir).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(dir))Directory.Delete(dir,true);}
    public static void Save(StudioState state,string path){if(state.VideoPath!=null&&File.Exists(state.VideoPath)&&new FileInfo(state.VideoPath).Length>2147483648)throw new Exception("Исходное видео превышает лимит 2 ГБ для одного файла проекта.");string temp=Temp(),stage=path+".writing-"+Guid.NewGuid().ToString("N");try{
        SceneFile.Save(state.Scene,Path.Combine(temp,"scene.cutscene"));bool hasBackground=state.Background.Photo!=null&&state.Background.Base!=null;if(hasBackground)AmbientFile.Save(state.Background,Path.Combine(temp,"background.bgscene"));state.Manifest.EmptyBackground=hasBackground?new AmbientProject():state.Background;state.Manifest.GalleryIds=state.Library.Keys.ToList();state.Manifest.VideoEntry=state.VideoPath!=null&&File.Exists(state.VideoPath)?"source/video"+Path.GetExtension(state.VideoPath):null;
        using(var zip=ZipFile.Open(stage,ZipArchiveMode.Create)){CopyFile(zip,"scene.cutscene",Path.Combine(temp,"scene.cutscene"));if(hasBackground)CopyFile(zip,"background.bgscene",Path.Combine(temp,"background.bgscene"));using(var s=zip.CreateEntry("sprites.bin",CompressionLevel.Optimal).Open())state.Manifest.Sprites.Write(s);
            foreach(var entry in state.Library){string file=Path.Combine(temp,entry.Key+".bgscene");AmbientFile.Save(entry.Value,file);CopyFile(zip,"gallery/"+entry.Key+".bgscene",file);}if(state.Manifest.VideoEntry!=null)CopyFile(zip,state.Manifest.VideoEntry,state.VideoPath);if(state.Preview!=null){string png=Path.Combine(temp,"preview.png");BackgroundLibrary.Thumbnail(state.Preview,320,180).Save(png);CopyFile(zip,"preview.png",png);}using(var w=new StreamWriter(zip.CreateEntry("studio.json").Open()))w.Write(new JavaScriptSerializer().Serialize(state.Manifest));}
        if(File.Exists(path))File.Replace(stage,path,null);else File.Move(stage,path);
    }finally{if(File.Exists(stage))File.Delete(stage);Cleanup(temp);}}
    static void Extract(ZipArchive zip,string name,string target,long limit){var e=zip.GetEntry(name);if(e==null||e.Length>limit)throw new Exception("Ресурс проекта отсутствует или слишком большой: "+name);using(var s=e.Open())using(var file=File.Create(target))s.CopyTo(file);}
    public static StudioState Load(string path){string temp=Temp();try{using(var zip=ZipFile.OpenRead(path)){var e=zip.GetEntry("studio.json");if(e==null||e.Length>2000000)throw new Exception("Это не проект PixelForge Studio.");StudioManifest manifest;using(var r=new StreamReader(e.Open()))manifest=new JavaScriptSerializer().Deserialize<StudioManifest>(r.ReadToEnd());if(manifest==null||manifest.Version!=1||manifest.Sprites==null||manifest.GalleryIds==null||manifest.GalleryIds.Count>128)throw new Exception("Неизвестная версия проекта.");var state=new StudioState {Manifest=manifest};Extract(zip,"scene.cutscene",Path.Combine(temp,"scene.cutscene"),2147483648);state.Scene=SceneFile.Load(Path.Combine(temp,"scene.cutscene"));var bg=zip.GetEntry("background.bgscene");if(bg!=null){Extract(zip,bg.FullName,Path.Combine(temp,"background.bgscene"),512000000);state.Background=AmbientFile.Load(Path.Combine(temp,"background.bgscene"));}else{state.Background=manifest.EmptyBackground??new AmbientProject();state.Background.Validate();}
            var sprites=zip.GetEntry("sprites.bin");if(sprites==null)throw new Exception("Нет спрайтов проекта.");using(var s=sprites.Open())manifest.Sprites.Read(s);
            foreach(string id in manifest.GalleryIds){if(id==null||id.Length!=32||!id.All(Uri.IsHexDigit))throw new Exception("Некорректный фон проекта.");string file=Path.Combine(temp,id+".bgscene");Extract(zip,"gallery/"+id+".bgscene",file,512000000);state.Library[id]=AmbientFile.Load(file);}
            if(manifest.VideoEntry!=null){if(!manifest.VideoEntry.StartsWith("source/video",StringComparison.Ordinal)||manifest.VideoEntry.Contains("..")||manifest.VideoEntry.Contains('\\'))throw new Exception("Некорректное видео проекта.");string assets=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","studio-assets",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(assets);state.VideoPath=Path.Combine(assets,"video"+Path.GetExtension(manifest.VideoEntry));Extract(zip,manifest.VideoEntry,state.VideoPath,2147483648);}
            return state;
        }}finally{Cleanup(temp);}}
}
public sealed class RecentProject {public string Path,Name,OpenedUtc;}
public static class RecentProjects {
    public static string FilePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings","recent-projects.json");
    public static List<RecentProject> List(){try{return new JavaScriptSerializer().Deserialize<List<RecentProject>>(File.ReadAllText(FilePath))??new List<RecentProject>();}catch{return new List<RecentProject>();}}
    public static void Touch(string path){var list=List().Where(p=>!string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase)).ToList();list.Insert(0,new RecentProject {Path=Path.GetFullPath(path),Name=Path.GetFileNameWithoutExtension(path),OpenedUtc=DateTime.UtcNow.ToString("o")});Directory.CreateDirectory(Path.GetDirectoryName(FilePath));BackgroundLibrary.AtomicText(FilePath,new JavaScriptSerializer().Serialize(list.Take(20).ToList()));}
}
}

