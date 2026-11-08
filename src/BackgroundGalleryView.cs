using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PixelForge {
public sealed class BackgroundGalleryView : UserControl {
    WrapPanel cards=new WrapPanel();TextBox search=new TextBox {Width=250,Margin=new Thickness(0,0,10,8)};TextBlock status=new TextBlock();bool working;
    public Action<AmbientProject> OpenBackground;public Action<AmbientProject,List<Frame>> UseBackground;
    public bool IsWorking {get{return working;}}
    public BackgroundGalleryView(){var root=new DockPanel {Margin=new Thickness(20)};Content=root;var head=new StackPanel();DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);head.Children.Add(new TextBlock {Text="ГАЛЕРЕЯ ФОНОВ",FontSize=25,FontWeight=FontWeights.Bold,Foreground=Brushes.MediumAquamarine});head.Children.Add(new TextBlock {Text="Фото, маски и циклы сохраняются здесь автоматически. Фоны доступны в любом проекте.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSlateGray,Margin=new Thickness(0,6,0,14)});var tools=new WrapPanel();head.Children.Add(tools);tools.Children.Add(new TextBlock {Text="Поиск:",Margin=new Thickness(0,8,8,8)});tools.Children.Add(search);search.ToolTip="Поиск по имени фона";search.TextChanged+=(s,e)=>Refresh();Button(tools,"Обновить",()=>Refresh());Button(tools,"Импорт .bgscene",async()=>await Import());status.Foreground=Brushes.MediumAquamarine;status.Margin=new Thickness(0,10,0,0);status.TextWrapping=TextWrapping.Wrap;DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);root.Children.Add(new ScrollViewer {Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});}
    void Button(Panel panel,string label,Action action){var b=new Button {Content=label};b.Click+=(s,e)=>{if(working)return;try{action();}catch(Exception ex){status.Text=ex.Message;}};panel.Children.Add(b);}
    public void Refresh(){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}cards.Children.Clear();var list=BackgroundLibrary.List().Where(e=>string.IsNullOrWhiteSpace(search.Text)||(e.Name??"").IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();foreach(var entry in list){var body=new StackPanel {Margin=new Thickness(10),Width=240};try{var bitmap=Frame.Load(entry.ThumbnailPath).Bitmap();bitmap.Freeze();var image=new Image {Source=bitmap,Width=240,Height=135,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);body.Children.Add(image);}catch{body.Children.Add(new TextBlock {Text="Нет миниатюры",Height=135});}body.Children.Add(new TextBlock {Text=entry.Name,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,5)});body.Children.Add(new TextBlock {Text=entry.Width+" × "+entry.Height+" · "+entry.Duration.ToString("0.##")+" с · "+entry.Fps+" fps",Foreground=Brushes.LightSlateGray,Margin=new Thickness(0,0,0,9)});var actions=new WrapPanel();body.Children.Add(actions);Button(actions,"Открыть",()=>{var p=BackgroundLibrary.Load(entry.Id);if(OpenBackground!=null)OpenBackground(p);});Button(actions,"В монтаж",async()=>await Use(entry));Button(body,"Создать копию",async()=>{try{working=true;var p=BackgroundLibrary.Load(entry.Id);p.LibraryId=Guid.NewGuid().ToString("N");p.Name+=" · копия";await Task.Run(()=>BackgroundLibrary.Upsert(p));Refresh();}catch(Exception ex){status.Text=ex.Message;}finally{working=false;}});cards.Children.Add(new Border {Child=body,Background=new SolidColorBrush(Color.FromRgb(29,37,52)),Margin=new Thickness(0,0,14,14),CornerRadius=new CornerRadius(4)});}status.Text=list.Count==0?"Галерея пуста. Создайте фон в третьей вкладке или импортируйте .bgscene.":"Фонов: "+list.Count+" · библиотека хранится в library/backgrounds рядом с приложением.";}
    async Task Use(BackgroundEntry entry){try{working=true;status.Text="Готовлю фон «"+entry.Name+"»…";var p=BackgroundLibrary.Load(entry.Id);var frames=await Task.Run(()=>AmbientEngine.Frames(p,CancellationToken.None));if(UseBackground!=null)UseBackground(p,frames);status.Text="Фон добавлен в монтаж.";}catch(Exception ex){status.Text=ex.Message;}finally{working=false;}}
    async Task Import(){var dialog=new OpenFileDialog {Filter="Фон PixelForge|*.bgscene",Multiselect=true};if(dialog.ShowDialog()!=true)return;try{working=true;foreach(string file in dialog.FileNames){var p=AmbientFile.Load(file);if(string.IsNullOrEmpty(p.LibraryId))p.LibraryId=BackgroundLibrary.FileId(file);await Task.Run(()=>BackgroundLibrary.Upsert(p,true));}Refresh();}catch(Exception ex){status.Text="Импорт: "+ex.Message;}finally{working=false;}}
}
}

