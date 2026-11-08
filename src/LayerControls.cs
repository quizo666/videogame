using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelForge {
public sealed partial class CutsceneEditor {
    readonly Dictionary<string,Slider> layerSliders=new Dictionary<string,Slider>();
    readonly Dictionary<TextBox,string> editOwners=new Dictionary<TextBox,string>();
    readonly CheckBox lockedLayer=new CheckBox {Content="Заблокировать слой"},soloLayer=new CheckBox {Content="Соло: только этот слой"};
    Cutscene liveBefore;string liveLayerId,liveProperty;bool liveChanged;
    Point layerDragStart;string layerDragId;ListBoxItem dropMarker;Brush markerBrush;Thickness markerThickness;

    void PushHistory(Cutscene snapshot){history.Push(snapshot);if(history.Count>32)history=new Stack<Cutscene>(history.Take(32).Reverse());if(WorkspaceEdited!=null)WorkspaceEdited();}
    void BeginLiveEdit(string property){
        if(selected==null||selected.Locked||exporting)return;
        if(liveBefore!=null&&liveLayerId==selected.Id&&liveProperty==property)return;
        EndLiveEdit();liveBefore=Clone(scene);liveLayerId=selected.Id;liveProperty=property;liveChanged=false;
    }
    void EndLiveEdit(){var snapshot=liveBefore;bool changed=liveChanged;liveBefore=null;liveLayerId=liveProperty=null;liveChanged=false;if(snapshot!=null&&changed)PushHistory(snapshot);}
    bool CanChange(string property){return selected!=null&&(!selected.Locked||property=="Locked"||property=="Visible"||property=="Solo"||property=="Name"||property=="LabelColor");}
    bool CommitLayerValue(string property,object value){
        if(exporting||updating||selected==null)return false;
        if(!CanChange(property))throw new Exception("Слой заблокирован. Сначала снимите блокировку L.");
        var field=typeof(SceneLayer).GetField(property);if(field==null)throw new Exception("Неизвестное свойство слоя.");
        if(Equals(field.GetValue(selected),value))return false;
        var candidate=Clone(scene);field.SetValue(candidate.Layers[scene.Layers.IndexOf(selected)],value);candidate.Validate();
        if(liveBefore!=null&&liveLayerId==selected.Id&&liveProperty==property)liveChanged=true;else{EndLiveEdit();PushHistory(Clone(scene));}
        field.SetValue(selected,value);dirty=true;
        bool previous=updating;updating=true;
        if(layerFields.ContainsKey(property)){if(value is string)layerFields[property].Text=(string)value;else Set(layerFields,property,Convert.ToDouble(value));layerFields[property].BorderBrush=new SolidColorBrush(Color.FromRgb(59,73,98));layerFields[property].ToolTip=null;}
        SyncSliders();updating=previous;
        if(property=="Name"||property=="Visible"||property=="Solo"||property=="Locked"||property=="LabelColor"){RefreshLayers();SyncLayerInspector();}
        RefreshView();Tell("Слой «"+selected.Name+"»: "+PropertyLabel(property)+" = "+(value is double?((double)value).ToString("0.###"):value.ToString())+" · Ctrl+Z — отменить");if(property=="Stabilization"||property=="PaletteSize"||property=="ReferenceThreshold"||property=="Loop")OnStabilizationChanged();return true;
    }
    static string PropertyLabel(string key){switch(key){case "Brightness":return "яркость";case "EndBrightness":return "яркость в конце";case "Opacity":return "непрозрачность";case "Scale":return "масштаб";case "Rotation":return "поворот";case "Locked":return "блокировка";case "Solo":return "соло";case "Visible":return "видимость";default:return key;}}
    void CommitLayerField(string key){EndLiveEdit();if(selected==null)return;var box=layerFields[key];try{object value=key=="Name"?(object)(box.Text.Trim().Length==0?"Слой":box.Text.Trim()):Number(layerFields,key);CommitLayerValue(key,value);box.BorderBrush=new SolidColorBrush(Color.FromRgb(59,73,98));box.ToolTip=null;}catch(Exception ex){box.BorderBrush=Brushes.Orange;box.ToolTip=ex.Message;throw;}}
    void InstallFieldEditing(Panel parent,Dictionary<string,TextBox> fields,string key,TextBox box){
        bool isLayer=ReferenceEquals(fields,layerFields);
        box.GotKeyboardFocus+=(s,e)=>{if(isLayer)editOwners[box]=selected==null?null:selected.Id;};
        box.PreviewKeyDown+=(s,e)=>{if(e.Key!=Key.Enter&&e.Key!=Key.Escape)return;e.Handled=true;
            if(e.Key==Key.Escape){if(isLayer)SyncLayerInspector();else ShowProject();return;}
            try{if(isLayer)CommitLayerField(key);else ApplyProject();}catch(Exception ex){box.BorderBrush=Brushes.Orange;box.ToolTip=ex.Message;Tell(ex.Message);}
        };
        if(!isLayer)return;
        box.LostKeyboardFocus+=(s,e)=>{string owner;if(updating||selected==null||!editOwners.TryGetValue(box,out owner)||owner!=selected.Id||!CanChange(key))return;try{CommitLayerField(key);}catch(Exception ex){Tell(ex.Message);}};
        if(key=="Name")return;
        var slider=new Slider {Minimum=0,Maximum=1,Margin=new Thickness(0,0,0,10),Height=20,ToolTip="Изменяется сразу. Двойной щелчок — сброс. Ctrl+Z — отменить весь жест."};layerSliders[key]=slider;parent.Children.Add(slider);
        slider.PreviewMouseLeftButtonDown+=(s,e)=>BeginLiveEdit(key);slider.PreviewMouseLeftButtonUp+=(s,e)=>EndLiveEdit();slider.LostMouseCapture+=(s,e)=>EndLiveEdit();
        slider.PreviewKeyDown+=(s,e)=>{if(IsSliderKey(e.Key))BeginLiveEdit(key);};slider.PreviewKeyUp+=(s,e)=>{if(IsSliderKey(e.Key))EndLiveEdit();};slider.LostKeyboardFocus+=(s,e)=>EndLiveEdit();
        slider.MouseDoubleClick+=(s,e)=>{EndLiveEdit();double reset=key.Contains("Opacity")||key.Contains("Scale")?1:key=="Fps"?scene.Fps:0;if(key=="Duration")reset=scene.Duration-(selected==null?0:selected.Start);try{CommitLayerValue(key,Math.Max(slider.Minimum,Math.Min(slider.Maximum,reset)));}catch(Exception ex){Tell(ex.Message);}e.Handled=true;};
        slider.ValueChanged+=(s,e)=>{if(updating||exporting||selected==null)return;double value=Math.Round(slider.Value,Decimals(key));try{CommitLayerValue(key,value);}catch(Exception ex){Tell(ex.Message);bool old=updating;updating=true;slider.Value=Convert.ToDouble(typeof(SceneLayer).GetField(key).GetValue(selected));updating=old;}};
    }
    static bool IsSliderKey(Key key){return key==Key.Left||key==Key.Right||key==Key.Up||key==Key.Down||key==Key.PageUp||key==Key.PageDown||key==Key.Home||key==Key.End;}
    static int Decimals(string key){if(key=="X"||key=="Y"||key=="EndX"||key=="EndY"||key=="Fps")return 0;if(key.Contains("Brightness")||key.Contains("Rotation"))return 1;if(key=="Start"||key=="Duration"||key.Contains("Fade"))return 3;return 2;}
    void SyncSliders(){
        if(selected==null){foreach(var s in layerSliders.Values)s.IsEnabled=false;return;}
        foreach(var pair in layerSliders){string key=pair.Key;var slider=pair.Value;double value=Convert.ToDouble(typeof(SceneLayer).GetField(key).GetValue(selected)),min=0,max=1,step=.01;
            switch(key){case "X":case "EndX":min=-scene.Width;max=scene.Width*2;step=1;break;case "Y":case "EndY":min=-scene.Height;max=scene.Height*2;step=1;break;case "Scale":case "EndScale":min=.01;max=8;break;case "Brightness":case "EndBrightness":min=-100;max=100;step=1;break;case "Rotation":case "EndRotation":min=-180;max=180;step=1;break;case "Fps":min=1;max=60;step=1;break;case "Start":max=Math.Max(0,scene.Duration-selected.Duration);step=1.0/scene.Fps;break;case "Duration":min=Math.Max(.001,selected.FadeIn+selected.FadeOut);max=scene.Duration-selected.Start;step=1.0/scene.Fps;break;case "FadeIn":max=selected.Duration-selected.FadeOut;break;case "FadeOut":max=selected.Duration-selected.FadeIn;break;}
            min=Math.Min(min,value);max=Math.Max(max,value);slider.Minimum=min;slider.Maximum=Math.Max(min,max);slider.SmallChange=step;slider.LargeChange=step*10;slider.TickFrequency=step;slider.Value=value;slider.IsEnabled=!selected.Locked&&max>min;
        }
    }
    void SyncLayerInspector(){bool old=updating;updating=true;
        if(selected!=null){layerFields["Name"].Text=selected.Name;foreach(var pair in layerFields){if(pair.Key=="Name")continue;Set(layerFields,pair.Key,Convert.ToDouble(typeof(SceneLayer).GetField(pair.Key).GetValue(selected)));pair.Value.BorderBrush=new SolidColorBrush(Color.FromRgb(59,73,98));pair.Value.ToolTip=null;}visible.IsChecked=selected.Visible;loopLayer.IsChecked=selected.Loop;animate.IsChecked=selected.Animate;smooth.IsChecked=selected.Smooth;lockedLayer.IsChecked=selected.Locked;soloLayer.IsChecked=selected.Solo;}
        foreach(var pair in layerFields)pair.Value.IsReadOnly=selected==null||(selected.Locked&&pair.Key!="Name");
        lockedLayer.IsEnabled=soloLayer.IsEnabled=visible.IsEnabled=selected!=null;loopLayer.IsEnabled=animate.IsEnabled=smooth.IsEnabled=selected!=null&&!selected.Locked;
        SyncSliders();updating=old;SyncAnimationControls();
    }
    void BindFlag(CheckBox box,string property){box.Click+=(s,e)=>{if(updating)return;EndLiveEdit();try{CommitLayerValue(property,box.IsChecked==true);}catch(Exception ex){Tell(ex.Message);SyncLayerInspector();}};}
    void InitializeLayerControls(){
        BindFlag(visible,"Visible");BindFlag(loopLayer,"Loop");BindFlag(animate,"Animate");BindFlag(smooth,"Smooth");BindFlag(lockedLayer,"Locked");BindFlag(soloLayer,"Solo");
        layers.HorizontalContentAlignment=HorizontalAlignment.Stretch;layers.AllowDrop=true;
        layers.PreviewMouseLeftButtonDown+=(s,e)=>{if(Ancestor<Button>(e.OriginalSource as DependencyObject)!=null||Ancestor<TextBox>(e.OriginalSource as DependencyObject)!=null){layerDragId=null;return;}var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);var l=item==null?null:item.Tag as SceneLayer;layerDragId=l==null||l.Locked?null:l.Id;layerDragStart=e.GetPosition(layers);};
        layers.PreviewMouseMove+=(s,e)=>{if(layerDragId==null||e.LeftButton!=MouseButtonState.Pressed||exporting)return;var p=e.GetPosition(layers);if(Math.Abs(p.X-layerDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-layerDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;string id=layerDragId;layerDragId=null;DragDrop.DoDragDrop(layers,new DataObject("PixelForge.Layer",id),DragDropEffects.Move);ClearDropMarker();};
        layers.DragOver+=(s,e)=>{ClearDropMarker();if(!e.Data.GetDataPresent("PixelForge.Layer")||exporting){e.Effects=DragDropEffects.None;return;}var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);if(item!=null){dropMarker=item;markerBrush=item.BorderBrush;markerThickness=item.BorderThickness;item.BorderBrush=Brushes.MediumAquamarine;item.BorderThickness=e.GetPosition(item).Y<item.ActualHeight/2?new Thickness(0,2,0,0):new Thickness(0,0,0,2);}e.Effects=DragDropEffects.Move;e.Handled=true;};
        layers.DragLeave+=(s,e)=>ClearDropMarker();layers.Drop+=(s,e)=>{ClearDropMarker();if(exporting||!e.Data.GetDataPresent("PixelForge.Layer"))return;var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);if(item==null)return;var source=scene.Layers.FirstOrDefault(l=>l.Id==(string)e.Data.GetData("PixelForge.Layer"));var target=item.Tag as SceneLayer;try{MoveLayerTo(source,target,e.GetPosition(item).Y>=item.ActualHeight/2);}catch(Exception ex){Tell(ex.Message);}e.Handled=true;};
        view.SelectLayer=l=>{EndLiveEdit();Select(l);RefreshLayerSelection();inspector.SelectedIndex=1;};
        timeline.ToggleFlag=(l,property)=>{try{ToggleFlag(l,property);}catch(Exception ex){Tell(ex.Message);}};
        PreviewKeyDown+=(s,e)=>{if(Keyboard.FocusedElement is TextBox||exporting)return;try{if(e.Key==Key.Delete){Delete();e.Handled=true;}if(e.Key==Key.D&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){Duplicate();e.Handled=true;}if(e.Key==Key.F2){FocusLayerName();e.Handled=true;}}catch(Exception ex){Tell(ex.Message);}};
    }
    static T Ancestor<T>(DependencyObject element) where T:DependencyObject{while(element!=null){var found=element as T;if(found!=null)return found;try{element=VisualTreeHelper.GetParent(element);}catch{element=LogicalTreeHelper.GetParent(element);}}return null;}
    void ClearDropMarker(){if(dropMarker!=null){dropMarker.BorderBrush=markerBrush;dropMarker.BorderThickness=markerThickness;dropMarker=null;}}
    void RefreshLayerSelection(){bool old=updating;updating=true;foreach(ListBoxItem item in layers.Items)if(item.Tag==selected){layers.SelectedItem=item;break;}updating=old;}
    void FocusLayerName(){FocusInlineName();}
    void ToggleFlag(SceneLayer layer,string property){if(layer==null||exporting)return;EndLiveEdit();Select(layer);RefreshLayerSelection();bool current=(bool)typeof(SceneLayer).GetField(property).GetValue(layer);CommitLayerValue(property,!current);}
    ListBoxItem BuildLayerItem(SceneLayer l){
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});for(int n=0;n<3;n++)row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var text=CreateInlineLayerName(l);row.Children.Add(text);
        string[] properties={"Visible","Solo","Locked"},labels={"V","S","L"},tips={"Видимость слоя","Соло: скрыть остальные слои","Блокировка от случайной правки"};
        for(int n=0;n<3;n++){string property=properties[n];bool active=(bool)typeof(SceneLayer).GetField(property).GetValue(l);var b=new Button {Content=labels[n],Width=26,Height=26,FontSize=11,Padding=new Thickness(0),Margin=new Thickness(1,0,0,0),ToolTip=tips[n],Background=new SolidColorBrush(active?Color.FromRgb(45,114,96):Color.FromRgb(48,56,72)),Foreground=active?Brushes.White:Brushes.LightSlateGray};Grid.SetColumn(b,n+1);b.Click+=(s,e)=>{try{ToggleFlag(scene.Layers.FirstOrDefault(x=>x.Id==l.Id),property);}catch(Exception ex){Tell(ex.Message);}e.Handled=true;};row.Children.Add(b);}
        var item=new ListBoxItem {Tag=l,Content=new Border {BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(l.LabelColor)),BorderThickness=new Thickness(4,0,0,0),Padding=new Thickness(4,0,0,0),Child=row},HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(5),Margin=new Thickness(0,0,0,2)};
        var menu=new ContextMenu {Foreground=Brushes.Black};Action<string,Action> add=(title,action)=>{var entry=new MenuItem {Header=title,Foreground=Brushes.Black};entry.Click+=(s,e)=>{if(exporting)return;var current=scene.Layers.FirstOrDefault(x=>x.Id==l.Id);if(current==null)return;Select(current);try{action();}catch(Exception ex){Tell(ex.Message);}};menu.Items.Add(entry);};
        add("Переименовать · F2",FocusLayerName);add(l.Visible?"Скрыть":"Показать",()=>ToggleFlag(selected,"Visible"));add(l.Solo?"Выключить соло":"Соло",()=>ToggleFlag(selected,"Solo"));add(l.Locked?"Разблокировать":"Заблокировать",()=>ToggleFlag(selected,"Locked"));AddLabelMenu(menu,l);menu.Items.Add(new Separator());add("Дублировать · Ctrl+D",Duplicate);add("Удалить · Delete",Delete);item.ContextMenu=menu;return item;
    }
    void MoveLayerTo(SceneLayer source,SceneLayer target,bool after){
        if(source==null||target==null||source==target)return;if(source.Locked)throw new Exception("Сначала разблокируйте слой.");EndLiveEdit();var order=scene.Layers.AsEnumerable().Reverse().ToList();int old=order.IndexOf(source),index=order.IndexOf(target)+(after?1:0);order.RemoveAt(old);if(old<index)index--;order.Insert(Math.Max(0,Math.Min(order.Count,index)),source);var result=order.AsEnumerable().Reverse().ToList();if(result.SequenceEqual(scene.Layers))return;Remember();scene.Layers=result;selected=source;RefreshLayers();ShowLayer();RefreshView();Tell("Порядок слоёв изменён. Ctrl+Z — отменить.");
    }
    async Task TestLiveLayers(string dir){
        var alphaFrame=new Frame(2,1);for(int p=0;p<alphaFrame.Data.Length;p+=4){alphaFrame.Data[p]=140;alphaFrame.Data[p+1]=120;alphaFrame.Data[p+2]=100;alphaFrame.Data[p+3]=(byte)(p==0?128:0);}var original=alphaFrame.Clone();
        var dark=SceneRenderer.AdjustBrightness(alphaFrame,-50);var light=SceneRenderer.AdjustBrightness(alphaFrame,50);
        if(dark.Data[2]!=50||dark.Data[1]!=60||dark.Data[0]!=70||light.Data[2]!=178||light.Data[1]!=188||light.Data[0]!=198||dark.Data[3]!=128||light.Data[7]!=0||!alphaFrame.Data.SequenceEqual(original.Data))throw new Exception("Brightness RGB/alpha/source preservation failed");
        if(!SceneRenderer.AdjustBrightness(alphaFrame,0).Data.SequenceEqual(alphaFrame.Data))throw new Exception("Neutral brightness changed image");
        scene=new Cutscene {Width=32,Height=18,Duration=2,Crossfade=.2};var background=new SceneLayer {Name="Фон",Duration=2,Frames=new List<Frame>{Paint(32,18,(x,y)=>Color.FromRgb(100,120,140))}};
        var frontFrame=Paint(6,6,(x,y)=>Color.FromRgb(80,100,120));frontFrame.Data[3]=0;var foreground=new SceneLayer {Name="Персонаж",Duration=2,X=2,Y=2,EndX=2,EndY=2,Frames=new List<Frame>{frontFrame}};scene.Layers.Add(background);scene.Layers.Add(foreground);selected=foreground;time=.5;history.Clear();ShowProject();ShowLayer();RefreshLayers();RefreshView();
        if(SceneRenderer.Pick(scene,time,new Point(2.2,2.2))!=background||SceneRenderer.Pick(scene,time,new Point(3.2,3.2))!=foreground)throw new Exception("Topmost alpha-aware layer selection failed");
        layerFields["Brightness"].Text="-25";var enter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};layerFields["Brightness"].RaiseEvent(enter);
        if(selected.Brightness!=-25||history.Count!=1)throw new Exception("Enter did not apply field immediately");Undo();if(selected.Brightness!=0)throw new Exception("Entered value undo failed");
        layerFields["Opacity"].Text="2";var invalidEnter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};layerFields["Opacity"].RaiseEvent(invalidEnter);if(selected.Opacity!=1||history.Count!=0)throw new Exception("Invalid live input mutated project/history");SyncLayerInspector();
        var baseline=SceneRenderer.Render(scene,time);BeginLiveEdit("Brightness");layerSliders["Brightness"].Value=-10;layerSliders["Brightness"].Value=-35;layerSliders["Brightness"].Value=-50;
        if(selected.Brightness!=-50||history.Count!=0||SceneRenderer.Render(scene,time).Data.SequenceEqual(baseline.Data))throw new Exception("Live slider did not update rendering during gesture");EndLiveEdit();if(history.Count!=1)throw new Exception("Slider gesture did not group undo");Undo();if(selected.Brightness!=0||!SceneRenderer.Render(scene,time).Data.SequenceEqual(baseline.Data))throw new Exception("Slider gesture undo did not restore complete state");
        BeginLiveEdit("Brightness");EndLiveEdit();if(history.Count!=0)throw new Exception("Unchanged slider gesture polluted undo history");
        CommitLayerValue("Locked",true);if(layerSliders["Brightness"].IsEnabled||!layerFields["X"].IsReadOnly)throw new Exception("Locked inspector not disabled");bool rejected=false;try{CommitLayerValue("X",99.0);}catch{rejected=true;}if(!rejected||selected.X!=2)throw new Exception("Locked layer accepted transform edit");double xBefore=selected.X;view.MoveSelected(25,25);if(selected.X!=xBefore)throw new Exception("Locked canvas move accepted");
        if(SceneRenderer.Pick(scene,time,new Point(3.2,3.2))==selected)throw new Exception("Locked layer picked for moving");rejected=false;try{Delete();}catch{rejected=true;}if(!rejected||scene.Layers.Count!=2)throw new Exception("Locked layer deletion accepted");ToggleFlag(selected,"Locked");if(selected.Locked)throw new Exception("Quick layer unlock failed");
        CommitLayerValue("Solo",true);var solo=SceneRenderer.Render(scene,time);int corner=(17*solo.W+31)*4;if(solo.Data[corner+2]!=23||solo.Data[corner+1]!=34||solo.Data[corner]!=53)throw new Exception("Solo did not hide other layers in renderer");CommitLayerValue("Solo",false);
        var third=new SceneLayer {Name="Эффект",Duration=2,Frames=new List<Frame>{Paint(2,2,(x,y)=>Colors.Yellow)}};scene.Layers.Add(third);RefreshLayers();var ids=scene.Layers.Select(l=>l.Id).ToArray();MoveLayerTo(scene.Layers[0],scene.Layers[2],false);if(scene.Layers.Last().Id!=ids[0])throw new Exception("Drop-above layer order incorrect");Undo();if(!scene.Layers.Select(l=>l.Id).SequenceEqual(ids))throw new Exception("Reorder undo failed");MoveLayerTo(scene.Layers[2],scene.Layers[0],true);if(scene.Layers[0].Id!=ids[2])throw new Exception("Drop-below layer order incorrect");Undo();
        Select(scene.Layers[1]);CommitLayerValue("Brightness",35.0);CommitLayerValue("EndBrightness",-20.0);CommitLayerValue("Locked",true);CommitLayerValue("Solo",true);SceneFile.Save(scene,Path.Combine(dir,"live-controls.cutscene"));var saved=SceneFile.Load(Path.Combine(dir,"live-controls.cutscene"));if(saved.Layers[1].Brightness!=35||saved.Layers[1].EndBrightness!=-20||!saved.Layers[1].Locked||!saved.Layers[1].Solo||!SceneRenderer.Render(saved,time).Data.SequenceEqual(SceneRenderer.Render(scene,time).Data))throw new Exception("Live properties/flags persistence and rendering failed");
        var animated=new Cutscene {Width=16,Height=16,Duration=1,Crossfade=0};animated.Layers.Add(new SceneLayer {Duration=1,Animate=true,Smooth=false,Brightness=0,EndBrightness=100,Frames=new List<Frame>{Paint(16,16,(x,y)=>Color.FromRgb(100,120,140))}});var halfway=SceneRenderer.Render(animated,.5);if(halfway.Data[2]!=178||halfway.Data[1]!=188||halfway.Data[0]!=198)throw new Exception("Animated brightness interpolation failed");
        animated.Layers[0].Animate=false;animated.Layers[0].Brightness=50;animated.Duration=animated.Layers[0].Duration=.1;animated.Fps=10;string video=Path.Combine(dir,"brightness.mp4"),decoded=Path.Combine(dir,"brightness-decoded.png");await SceneExport.Encode(animated,video,false,(i,n)=>{},System.Threading.CancellationToken.None);await SceneExport.Run("-hide_banner -loglevel error -y -i \""+video+"\" -frames:v 1 \""+decoded+"\"",System.Threading.CancellationToken.None);var encoded=Frame.Load(decoded);if(Math.Abs(encoded.Data[2]-178)>5||Math.Abs(encoded.Data[1]-188)>5||Math.Abs(encoded.Data[0]-198)>5)throw new Exception("Exported video did not contain brightness correction");
        Demo();Select(scene.Layers[2]);inspector.SelectedIndex=0;var host=Window.GetWindow(this);host.Width=1380;host.Height=900;host.UpdateLayout();await Task.Delay(100);var listShot=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);listShot.Render(host);Frame.From(listShot).Save(Path.Combine(dir,"layer-management.png"));
        inspector.SelectedIndex=1;CommitLayerValue("Brightness",-25.0);host.UpdateLayout();await Task.Delay(100);var propShot=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);propShot.Render(host);Frame.From(propShot).Save(Path.Combine(dir,"live-properties.png"));
    }
}
}

