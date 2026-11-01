using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Services;

// Opt-in UI-thread profiling of one Drop. Records contain values only; no control,
// window, model or dispatcher callback is retained after the scope is disposed.
public sealed class LayoutDropProfile : IDisposable
{
    [ThreadStatic] private static LayoutDropProfile? _current;
    private readonly long _started = Stopwatch.GetTimestamp(), _allocated = GC.GetAllocatedBytesForCurrentThread();
    private readonly Dictionary<string, long> _counters = new();
    private readonly Dictionary<string, StageResult> _stages = new();
    private readonly List<StageScope> _openStages=new();
    private readonly List<string> _notes=new();
    public IReadOnlyList<string> Notes=>_notes;
    private readonly bool _captureStacks=Environment.GetEnvironmentVariable("FORMDESIGNER_LAYOUT_DROP_STACKS")=="1";
    private Action<string, string>? _write;
    private bool _disposed;
    private readonly bool _toolbox;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IReadOnlyCollection<StageResult> Stages => _stages.Values;
    public IReadOnlyDictionary<string, long> Counters => _counters;
    public double ElapsedMs { get; private set; }
    public long AllocatedBytes { get; private set; }
    public sealed record StageResult(string Stage, int Calls, double ElapsedMs, long AllocatedBytes, long CreatedTrackedObjects, long UpdatedControls)
    {public double EndMs {get;init;} public long AffectedControls {get;init;}}
    private long CreatedTrackedObjects => _counters.GetValueOrDefault("PreviewControlsCreated") + _counters.GetValueOrDefault("DesignerWrappersCreated")
        + _counters.GetValueOrDefault("ContextMenusCreated") + _counters.GetValueOrDefault("InspectorRowsCreated");

    private LayoutDropProfile(string documentId, string controlId, Action<string, string>? write)
    {
        _write = write;
        _toolbox=controlId=="toolbox";
        _current = this;
        try { if(write is not null){var detail=JsonSerializer.Serialize(new { Id, documentId, controlId });write.Invoke("LAYOUT_DROP_PROFILE_START",detail);
            if(_toolbox)write.Invoke("TOOLBOX_DROP_PROFILE_START",detail);} }
        catch { _current = null; _write = null; throw; }
    }
    public static LayoutDropProfile Begin(string documentId, string controlId, Action<string, string>? write = null)
    {
        if (_current is not null) throw new InvalidOperationException("A Drop profile is already active on this thread.");
        return new(documentId, controlId, write);
    }
    public static LayoutDropProfile? BeginIfEnabled(string documentId, string controlId, Action<string, string> write)
        => _current is null && Environment.GetEnvironmentVariable("FORMDESIGNER_LAYOUT_DROP_PROFILE") == "1"
            ? Begin(documentId, controlId, write) : null;
    public static IDisposable? Stage(string name) => _current is { } owner ? new StageScope(owner, name) : null;
    public static void Count(string name, long value = 1)
    {
        if (_current is { } owner) owner._counters[name] = owner._counters.GetValueOrDefault(name) + value;
    }
    public static void Affect(string id)
    {if(_current is { } owner)foreach(var scope in owner._openStages)scope.Affect(id);}
    public static void Note(string name)
    {if(_current is {_captureStacks:true} owner&&owner._notes.Count<12)owner._notes.Add(name+"\n"+new StackTrace(1,false));}
    private sealed class StageScope : IDisposable
    {
        private readonly LayoutDropProfile _owner;
        private readonly string _name;
        private readonly long _time = Stopwatch.GetTimestamp(), _bytes = GC.GetAllocatedBytesForCurrentThread();
        private readonly long _created, _updated;
        private HashSet<string>? _affected;
        public void Affect(string id)=>(_affected??=new()).Add(id);
        public StageScope(LayoutDropProfile owner, string name)
        { _owner = owner; _name = name; _created = owner.CreatedTrackedObjects; _updated = owner._counters.GetValueOrDefault("DesignerWrappersUpdated");owner._openStages.Add(this); }
        public void Dispose()
        {
            var previous = _owner._stages.GetValueOrDefault(_name) ?? new(_name, 0, 0, 0, 0, 0);
            _owner._stages[_name] = previous with { Calls = previous.Calls + 1,
                ElapsedMs = previous.ElapsedMs + Stopwatch.GetElapsedTime(_time).TotalMilliseconds,
                AllocatedBytes = previous.AllocatedBytes + GC.GetAllocatedBytesForCurrentThread() - _bytes,
                CreatedTrackedObjects = previous.CreatedTrackedObjects + _owner.CreatedTrackedObjects - _created,
                UpdatedControls = previous.UpdatedControls + _owner._counters.GetValueOrDefault("DesignerWrappersUpdated") - _updated,
                EndMs=Stopwatch.GetElapsedTime(_owner._started).TotalMilliseconds,AffectedControls=previous.AffectedControls+(_affected?.Count??0) };
            _owner._openStages.Remove(this);
            LayoutDropEventSource.Log.Stage(_owner.Id,_name,Stopwatch.GetElapsedTime(_time).TotalMilliseconds);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ElapsedMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocated;
        if (ReferenceEquals(_current, this)) _current = null;
        var write = _write; _write = null;
        if(write is null)return;
        foreach (var stage in _stages.Values)
        {
            var detail=JsonSerializer.Serialize(new {Id,stage.Stage,stage.Calls,stage.ElapsedMs,stage.AllocatedBytes,stage.CreatedTrackedObjects,stage.UpdatedControls,stage.AffectedControls,stage.EndMs});
            write?.Invoke("LAYOUT_DROP_PROFILE_STAGE",detail);
            if(_toolbox)write?.Invoke("TOOLBOX_DROP_PROFILE_STAGE",detail);
            if(stage.Stage=="RefreshPropertyInspector")write?.Invoke("PROPERTY_INSPECTOR_REFRESH_PROFILE",detail);
            if(stage.Stage is "FirstCompositorCommit" or "FirstRenderAfterDrop")write?.Invoke("LAYOUT_FIRST_RENDER_COMPLETED",detail);
        }
        if(_counters.TryGetValue("DesignerWrappersUpdated",out var reused))write?.Invoke("LAYOUT_VISUAL_WRAPPERS_REUSED",JsonSerializer.Serialize(new {Id,Count=reused}));
        if(_counters.TryGetValue("DesignerWrappersCreated",out var created))write?.Invoke("LAYOUT_VISUAL_WRAPPERS_CREATED",JsonSerializer.Serialize(new {Id,Count=created}));
        var end=JsonSerializer.Serialize(new {Id,ElapsedMs,AllocatedBytes,Counters=_counters});
        write?.Invoke("LAYOUT_DROP_PROFILE_END",end);
        if(_toolbox)write?.Invoke("TOOLBOX_DROP_PROFILE_END",end);
    }
}
