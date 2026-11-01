using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.DesignerSystem;

/// <summary>Derived captions only; owned and cleared by one document session.</summary>
public sealed class UndoHistoryDescriptionCache
{
    private readonly Dictionary<Pair, (string Title, string Description)> _entries = new(new PairComparer());
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _documentId;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS") == "1";
    private string? _context;
    private int _historyCount;

    internal UndoHistoryDescriptionCache(string documentId)
    {
        _documentId = documentId;
        // Diagnostic A/B switch, deliberately not a persisted user setting.
        Enabled = Environment.GetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE") != "0";
    }

    public bool Enabled { get; }
    public int Count => _entries.Count;
    public long HitCount { get; private set; }
    public long MissCount { get; private set; }

    public void Prepare(IReadOnlyList<string> snapshots, string captionContext)
    {
        if (!string.Equals(_context, captionContext, StringComparison.Ordinal))
        {
            Clear("CaptionContextChanged");
            _context = captionContext;
        }
        Prune(snapshots);
    }

    public (string Title, string Description) GetOrCreate(
        string previous, string current, Func<string, string, (string Title, string Description)> describe)
    {
        var pair = new Pair(previous, current);
        if (Enabled && _entries.TryGetValue(pair, out var description))
        {
            HitCount++;
            Log("UNDO_HISTORY_CACHE_HIT", "UnchangedPair");
            return description;
        }

        MissCount++;
        Log("UNDO_HISTORY_CACHE_MISS", Enabled ? "NewPair" : "Disabled");
        description = describe(previous, current);
        if (Enabled)
            _entries[pair] = description;
        return description;
    }

    internal void Prune(IReadOnlyList<string> snapshots)
    {
        _historyCount = snapshots.Count;
        if (_entries.Count == 0)
            return;
        var reachable = new HashSet<Pair>(new PairComparer());
        for (var i = 1; i < snapshots.Count; i++)
            reachable.Add(new Pair(snapshots[i - 1], snapshots[i]));
        var removed = 0;
        // Removing the current entry during Dictionary enumeration is supported on .NET 8.
        foreach (var pair in _entries.Keys)
            if (!reachable.Contains(pair) && _entries.Remove(pair))
                removed++;
        if (removed > 0)
            Log("UNDO_HISTORY_CACHE_INVALIDATED", "UnreachablePairs:" + removed);
    }

    internal void Clear(string reason)
    {
        _entries.Clear();
        _historyCount = 0;
        _context = null;
        Log("UNDO_HISTORY_CACHE_CLEARED", reason);
    }

    public void ReportStatistics() => Log("UNDO_HISTORY_CACHE_STATS", "RefreshCompleted");

    private void Log(string code, string reason)
    {
        if (!_diagnostics)
            return;
        // No snapshot JSON, captions, controls, factories or ViewModels are retained/logged.
        Trace.WriteLine(code + " " + JsonSerializer.Serialize(new
        {
            SessionId = _sessionId, DocumentId = _documentId, HistoryCount = _historyCount,
            CacheCount = Count, HitCount, MissCount, Reason = reason
        }));
    }

    private readonly record struct Pair(string Previous, string Current);
    private sealed class PairComparer : IEqualityComparer<Pair>
    {
        public bool Equals(Pair x, Pair y) => ReferenceEquals(x.Previous, y.Previous) && ReferenceEquals(x.Current, y.Current);
        public int GetHashCode(Pair pair) => HashCode.Combine(RuntimeHelpers.GetHashCode(pair.Previous), RuntimeHelpers.GetHashCode(pair.Current));
    }
}
