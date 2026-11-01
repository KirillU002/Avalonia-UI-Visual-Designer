using Microsoft.Diagnostics.Tracing.Etlx;
using System.Text.Json;

if(args.Length!=1)throw new ArgumentException("trace path");
var etlx=TraceLog.CreateFromEventPipeDataFile(args[0],args[0]+".all-"+File.GetLastWriteTimeUtc(args[0]).Ticks+".etlx",new TraceLogOptions{KeepAllEvents=true});
using var log=new TraceLog(etlx);
var windows=new List<(double start,double end,int thread,string stage)>();
Console.WriteLine(JsonSerializer.Serialize(new{Providers=log.Events.GroupBy(e=>e.ProviderName).Select(g=>new {Name=g.Key,Count=g.Count()}).ToArray()}));
foreach(var item in log.Events.Where(e=>e.ProviderName.Contains("FormDesigner")).Take(3))
    Console.WriteLine(JsonSerializer.Serialize(new {Provider=item.ProviderName,Event=item.EventName,Payload=item.PayloadNames.Select((n,i)=>new {Name=n,Value=item.PayloadValue(i)})}));
foreach(var item in log.Events)
    if(item.ProviderName=="FormDesigner-LayoutDrop"&&item.EventName.StartsWith("Stage"))
    {
        // TraceLogging's anonymous object is one structured payload, unlike the
        // earlier manifest event with three flat fields. Handle both explicitly.
        var payload=JsonSerializer.SerializeToElement(item.PayloadNames.Contains("elapsedMs")
            ?new {name=item.PayloadByName("name"),elapsedMs=item.PayloadByName("elapsedMs")}
            :item.PayloadValue(0));
        if(!payload.TryGetProperty("elapsedMs",out var elapsed)||!payload.TryGetProperty("name",out var name))
            throw new InvalidDataException("Unrecognized LayoutDrop Stage payload.");
        var duration=elapsed.GetDouble();
        windows.Add((item.TimeStampRelativeMSec-duration,item.TimeStampRelativeMSec,item.ThreadID,name.GetString()!));
    }
Console.WriteLine(JsonSerializer.Serialize(new {StageIntervals=windows.Count}));
var stages=new[]{"AttachedUILayout","RefreshPropertyInspector","MeasureArrange","CreateUndoSnapshot"}
    .Concat(windows.Any(w=>w.stage=="TransportProbe")?new[]{"TransportProbe"}:Array.Empty<string>());
foreach(var stage in stages)
{
    var matching=windows.Where(w=>w.stage==stage).ToArray();
    var inclusive=new Dictionary<string,int>();var exclusive=new Dictionary<string,int>();var samples=0;
    foreach(var item in log.Events)
    {
        if(item.ProviderName!="Microsoft-DotNETCore-SampleProfiler")continue;
        var stack=item.CallStack();if(stack is null)continue;samples++;
        var frames=new List<string>();for(var f=stack;f is not null;f=f.Caller)frames.Add(f.CodeAddress.FullMethodName);
        var matched=matching.Any(w=>w.thread==item.ThreadID&&item.TimeStampRelativeMSec>=w.start&&item.TimeStampRelativeMSec<=w.end);
        if(matching.Length==0)matched=frames.Any(f=>f.Contains("RunLayoutDropBenchmark"))&&frames.Any(f=>stage switch{
            "AttachedUILayout"=>f.Contains("ExecuteLayoutPass"),"RefreshPropertyInspector"=>f.Contains("RebuildPropertyGrid"),
            "MeasureArrange"=>f.Contains("MeasureNativeDesignerLayout"),_=>f.Contains("RegisterHistorySnapshot")});
        if(!matched){samples--;continue;}
        var seen=new HashSet<string>();var first=true;
        for(var frame=stack;frame is not null;frame=frame.Caller)
        {
            var name=frame.CodeAddress.FullMethodName;
            if(first){exclusive[name]=exclusive.GetValueOrDefault(name)+1;first=false;}
            if(seen.Add(name))inclusive[name]=inclusive.GetValueOrDefault(name)+1;
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new {Stage=stage,Intervals=matching.Length,Classification=matching.Length==0?"Callstack filter, includes setup/reset":"EventSource intervals",UiSamples=samples,
        Inclusive=inclusive.OrderByDescending(p=>p.Value).Take(30),Exclusive=exclusive.OrderByDescending(p=>p.Value).Take(15)}));
}
