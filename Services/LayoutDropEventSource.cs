using System.Diagnostics.Tracing;

namespace FormDesigner.Services;

[EventSource(Name="FormDesigner-LayoutDrop")]
internal sealed class LayoutDropEventSource:EventSource
{
    public static readonly LayoutDropEventSource Log=new();
    private LayoutDropEventSource():base(EventSourceSettings.EtwSelfDescribingEventFormat){}
    [NonEvent]
    public void Stage(string id,string name,double elapsedMs)
    {if(IsEnabled())Write("Stage",new EventSourceOptions{Level=EventLevel.Informational},new{ id,name,elapsedMs });}
}
