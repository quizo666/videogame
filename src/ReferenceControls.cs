using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
namespace PixelForge {
public sealed partial class CutsceneEditor {
    DispatcherTimer referenceDelay;string referenceLayer;int pendingReference;
    void SetupReferenceSlider(){referenceDelay=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(220)};referenceDelay.Tick+=(s,e)=>CommitReference();referenceSlider.ValueChanged+=(s,e)=>{if(updating||selected==null)return;referenceLabel.Text="Порог смены цвета: "+referenceSlider.Value.ToString("0")+" / 255";referenceLayer=selected.Id;pendingReference=(int)Math.Round(referenceSlider.Value);referenceDelay.Stop();referenceDelay.Start();};referenceSlider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((s,e)=>BeginLiveEdit("ReferenceThreshold")));referenceSlider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((s,e)=>{CommitReference();EndLiveEdit();}));}
    void CommitReference(){referenceDelay.Stop();if(selected==null||selected.Id!=referenceLayer||selected.Locked||exporting)return;try{CommitLayerValue("ReferenceThreshold",pendingReference);}catch(Exception ex){Tell(ex.Message);}}
}
}
