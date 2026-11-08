using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PixelForge {
public sealed partial class CutsceneEditor {
    public Action WorkspaceEdited;
    public bool WorkspaceDirty {get{return dirty;}}
    public bool WorkspaceWorking {get{return exporting;}}
    public Cutscene CaptureWorkspace(){Stop();EndLiveEdit();return Clone(scene);}
    public void InstallWorkspace(Cutscene value){Stop();EndLiveEdit();scene=value;projectPath=null;selected=scene.Layers.LastOrDefault();history.Clear();time=0;dirty=false;ShowProject();ShowLayer();RefreshLayers();RefreshView();view.SetZoom(0);}
    public void MarkWorkspaceSaved(){dirty=false;}
    public void CaptureWorkspaceView(StudioManifest m){m.SceneTime=time;m.SelectedLayer=selected==null?null:selected.Id;m.ScenePanel=inspector.SelectedIndex;}
    public void RestoreWorkspaceView(StudioManifest m){time=double.IsNaN(m.SceneTime)||double.IsInfinity(m.SceneTime)?0:Math.Max(0,Math.Min(scene.Duration,m.SceneTime));selected=scene.Layers.FirstOrDefault(l=>l.Id==m.SelectedLayer)??scene.Layers.LastOrDefault();inspector.SelectedIndex=Math.Max(0,Math.Min(3,m.ScenePanel));ShowLayer();RefreshLayers();RefreshView();}
}
public sealed partial class AmbientBackgroundEditor {
    public Action WorkspaceEdited;
    public Func<bool> IsAutomatedRun;bool allowAutoGalleryTests;
    DispatcherTimer galleryDebounce;Task<BackgroundEntry> gallerySaving;int gallerySavedRevision=-1;BackgroundEntry lastGalleryEntry;
    public bool WorkspaceDirty {get{return dirty;}}
    public bool WorkspaceWorking {get{return busy;}}
    public AmbientProject CaptureWorkspace(){Stop();canvas.Finish();EndGesture();return AmbientEngine.Clone(project);}
    public void InstallWorkspace(AmbientProject value,bool isEdited=false){Stop();canvas.Finish();EndGesture();if(galleryDebounce!=null)galleryDebounce.Stop();project=value;selected=project.Regions.FirstOrDefault();path=null;history.Clear();currentFrame=0;revision++;cached=null;lastGalleryEntry=BackgroundLibrary.List().FirstOrDefault(e=>e.Id==project.LibraryId);gallerySavedRevision=lastGalleryEntry==null?-1:revision;dirty=isEdited;Sync();canvas.Fit();if(isEdited&&WorkspaceEdited!=null)WorkspaceEdited();}
    public void MarkWorkspaceSaved(){dirty=false;}
    public void CaptureWorkspaceView(StudioManifest m){m.BackgroundFrame=currentFrame;m.SelectedRegion=selected==null?null:selected.Id;m.BackgroundPanel=side.SelectedIndex;}
    public void RestoreWorkspaceView(StudioManifest m){currentFrame=Math.Max(0,Math.Min(project.FrameCount-1,m.BackgroundFrame));selected=project.Regions.FirstOrDefault(r=>r.Id==m.SelectedRegion)??project.Regions.FirstOrDefault();side.SelectedIndex=Math.Max(0,Math.Min(1,m.BackgroundPanel));Sync();}
    void InitializeLibrarySync(){galleryDebounce=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(1400)};galleryDebounce.Tick+=async(s,e)=>{galleryDebounce.Stop();try{await FlushGallery();}catch(Exception ex){Tell("Галерея: "+ex.Message);}};}
    void EnsureGalleryIdentity(){if(string.IsNullOrEmpty(project.LibraryId))project.LibraryId=Guid.NewGuid().ToString("N");}
    void ScheduleGallerySave(){if(WorkspaceEdited!=null)WorkspaceEdited();if(IsAutomatedRun!=null&&IsAutomatedRun()&&!allowAutoGalleryTests)return;if(project.Base==null||galleryDebounce==null)return;EnsureGalleryIdentity();galleryDebounce.Stop();galleryDebounce.Start();}
    public async Task<BackgroundEntry> FlushGallery(){
        if(galleryDebounce!=null)galleryDebounce.Stop();if(project.Base==null||project.Photo==null)return null;
        while(gallerySaving!=null){await gallerySaving;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);}
        if(gallerySavedRevision==revision&&lastGalleryEntry!=null)return lastGalleryEntry;
        EnsureGalleryIdentity();int version=revision;var snapshot=AmbientEngine.Clone(project);gallerySaving=Task.Run(()=>BackgroundLibrary.Upsert(snapshot));try{var entry=await gallerySaving;if(version==revision){gallerySavedRevision=version;lastGalleryEntry=entry;}return entry;}finally{gallerySaving=null;}
    }
    public bool OpenLibraryBackground(AmbientProject value){if(!MayClose())return false;InstallWorkspace(value,true);Tell("Фон открыт из галереи. Изменения автоматически обновляют эту запись.");return true;}
    public async Task<BackgroundEntry> TestAutomaticGallery(){allowAutoGalleryTests=true;try{ChangeRegion(r=>r.Amplitude=1.3);for(int n=0;n<80;n++){await Task.Delay(50);if(gallerySavedRevision==revision&&lastGalleryEntry!=null)return lastGalleryEntry;}throw new Exception("Automatic gallery save did not complete after editing.");}finally{allowAutoGalleryTests=false;}}
}
public sealed partial class MainWindow {
    SpriteWorkspace CaptureSprites(){editor.FinishStroke();return new SpriteWorkspace {BrushSize=editor.BrushSize,EraseSize=editor.EraseSize,ReferenceThreshold=referenceThreshold.Text,Cell=cell.Text,OffsetX=offsetX.Text,OffsetY=offsetY.Text,Tolerance=tolerance.Text,Levels=levels.Text,KeyColor=keyColor.Text,Ink=editor.Ink.ToString(),Cleanup=cleanup.Text,Fps=fps.Text,Count=count.Text,Stability=stabilityStrength.Text,StableColors=stableColors.Text,Key=key.IsChecked==true,Auto=autoBackground.IsChecked==true,Edges=edgeBackground.IsChecked==true,Grid=grid.IsChecked==true,Current=current,Tool=tool.SelectedIndex,EraseScope=eraseScope.SelectedIndex,Sources=new List<Frame>(sources),Frames=new List<Frame>(frames)};}
    void InstallSprites(SpriteWorkspace state){clock.Stop();editor.FinishStroke();player.Close();videoPath=null;editor.BrushSize=Math.Max(1,Math.Min(64,state.BrushSize));editor.EraseSize=Math.Max(1,Math.Min(64,state.EraseSize));referenceThreshold.Text=state.ReferenceThreshold??"56";SyncBrushSizes();cell.Text=state.Cell;offsetX.Text=state.OffsetX;offsetY.Text=state.OffsetY;tolerance.Text=state.Tolerance;levels.Text=state.Levels;keyColor.Text=state.KeyColor;cleanup.Text=state.Cleanup;fps.Text=state.Fps;count.Text=state.Count;stabilityStrength.Text=state.Stability;stableColors.Text=state.StableColors;key.IsChecked=state.Key;autoBackground.IsChecked=state.Auto;edgeBackground.IsChecked=state.Edges;grid.IsChecked=state.Grid;original.IsChecked=false;ink.Text=state.Ink;try{editor.Ink=ParseColor(state.Ink);}catch{editor.Ink=System.Windows.Media.Colors.White;}tool.SelectedIndex=Math.Max(0,Math.Min(2,state.Tool));eraseScope.SelectedIndex=Math.Max(0,Math.Min(1,state.EraseScope));sources=state.Sources;frames=state.Frames;undo.Clear();current=Math.Max(0,Math.Min(state.Current,frames.Count-1));editor.Frame=null;editor.InvalidateVisual();RefreshTimeline();changed=false;}
}
}
