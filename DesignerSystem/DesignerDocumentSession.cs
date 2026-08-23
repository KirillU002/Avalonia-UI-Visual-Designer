using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace FormDesigner.DesignerSystem;

/// <summary>
/// Runtime-состояние одной открытой формы в Designer. Этот объект не знает о
/// Window, dialogs или глобальных настройках: он владеет только состоянием
/// конкретного документа.
/// </summary>
public sealed class DesignerDocumentSession : IDisposable
{
    private bool _isSynchronizingSelection;
    private bool _isDisposed;
    private DesignControlModel? _selectedControl;
    private string _currentSnapshot = "";
    private string _savedSnapshot = "";

    public DesignerDocumentSession(DesignerFormDocument? formDocument, DesignerDocumentFileModel document, bool isTransient = false)
    {
        FormDocument = formDocument;
        Document = document ?? throw new ArgumentNullException(nameof(document));
        IsTransient = isTransient;
        DocumentId = string.IsNullOrWhiteSpace(formDocument?.Id)
            ? Guid.NewGuid().ToString("N")
            : formDocument.Id;

        Controls.CollectionChanged += Controls_CollectionChanged;
        SelectedControlIds.CollectionChanged += SelectedControlIds_CollectionChanged;
    }

    public string DocumentId { get; }
    public bool IsTransient { get; }
    public DesignerFormDocument? FormDocument { get; private set; }
    public DesignerDocumentFileModel Document { get; private set; }
    public ObservableCollection<DesignControlModel> Controls { get; } = new();
    public ObservableCollection<string> SelectedControlIds { get; } = new();
    public Stack<string> UndoSnapshots { get; } = new();
    public Stack<string> RedoSnapshots { get; } = new();
    public DateTime LastHistoryMutationUtc { get; set; } = DateTime.UtcNow;
    public long Revision { get; private set; }
    public bool IsDisposed => _isDisposed;

    public DesignControlModel? SelectedControl => _selectedControl;
    public string CurrentSnapshot => _currentSnapshot;
    public string SavedSnapshot => _savedSnapshot;
    public bool IsDirty => !string.Equals(_currentSnapshot, _savedSnapshot, StringComparison.Ordinal);
    public bool CanUndo => UndoSnapshots.Count > 0;
    public bool CanRedo => RedoSnapshots.Count > 0;

    public event EventHandler<DesignerDocumentSessionSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? DirtyStateChanged;

    public void UpdateDocument(DesignerDocumentFileModel document, DesignerFormDocument? formDocument = null)
    {
        ThrowIfDisposed();
        Document = document ?? throw new ArgumentNullException(nameof(document));
        if (formDocument is not null)
            FormDocument = formDocument;

        Revision++;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(IEnumerable<DesignControlModel>? controls, DesignControlModel? primaryControl)
    {
        ThrowIfDisposed();
        var normalized = (controls ?? Array.Empty<DesignControlModel>())
            .Where(IsCurrentControl)
            .DistinctBy(control => control.Id)
            .ToList();

        var normalizedPrimary = IsCurrentControl(primaryControl)
            ? primaryControl
            : normalized.LastOrDefault();

        if (normalizedPrimary is not null && normalized.All(control => control.Id != normalizedPrimary.Id))
            normalized.Add(normalizedPrimary);

        var oldControl = _selectedControl;
        var oldIds = SelectedControlIds.ToList();
        var newIds = normalized.Select(control => control.Id).ToList();
        var changed = !AreSameIds(oldIds, newIds)
            || !AreSameControl(oldControl, normalizedPrimary);

        if (!changed)
            return;

        _isSynchronizingSelection = true;
        try
        {
            SelectedControlIds.Clear();
            foreach (var id in newIds)
                SelectedControlIds.Add(id);

            _selectedControl = normalizedPrimary;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        SelectionChanged?.Invoke(this, new DesignerDocumentSessionSelectionChangedEventArgs(oldControl, _selectedControl, oldIds, newIds));
    }

    public void SetSelectedControl(DesignControlModel? control)
    {
        SetSelection(control is null ? Array.Empty<DesignControlModel>() : new[] { control }, control);
    }

    public void ClearSelection()
    {
        SetSelection(Array.Empty<DesignControlModel>(), null);
    }

    public void SetHistoryState(
        IEnumerable<string>? undoSnapshots,
        IEnumerable<string>? redoSnapshots,
        string? currentSnapshot,
        string? savedSnapshot)
    {
        ThrowIfDisposed();
        UndoSnapshots.Clear();
        foreach (var snapshot in undoSnapshots ?? Array.Empty<string>())
            UndoSnapshots.Push(snapshot);

        RedoSnapshots.Clear();
        foreach (var snapshot in redoSnapshots ?? Array.Empty<string>())
            RedoSnapshots.Push(snapshot);

        _currentSnapshot = currentSnapshot ?? "";
        _savedSnapshot = savedSnapshot ?? "";
        LastHistoryMutationUtc = DateTime.UtcNow;
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetHistory(string currentSnapshot, bool markAsSaved)
    {
        SetHistoryState(
            Array.Empty<string>(),
            Array.Empty<string>(),
            currentSnapshot,
            markAsSaved ? currentSnapshot : _savedSnapshot);
    }

    public void SetCurrentSnapshot(string snapshot)
    {
        ThrowIfDisposed();
        var wasDirty = IsDirty;
        _currentSnapshot = snapshot ?? "";
        LastHistoryMutationUtc = DateTime.UtcNow;
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        if (wasDirty != IsDirty)
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSavedSnapshot(string snapshot)
    {
        ThrowIfDisposed();
        var wasDirty = IsDirty;
        _savedSnapshot = snapshot ?? "";
        Revision++;
        if (wasDirty != IsDirty)
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PushUndoSnapshot(string snapshot)
    {
        ThrowIfDisposed();
        UndoSnapshots.Push(snapshot);
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PushRedoSnapshot(string snapshot)
    {
        ThrowIfDisposed();
        RedoSnapshots.Push(snapshot);
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? TakeUndoSnapshot()
    {
        ThrowIfDisposed();
        if (UndoSnapshots.Count == 0)
            return null;

        var snapshot = UndoSnapshots.Pop();
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    public string? TakeRedoSnapshot()
    {
        ThrowIfDisposed();
        if (RedoSnapshots.Count == 0)
            return null;

        var snapshot = RedoSnapshots.Pop();
        Revision++;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    public IReadOnlyList<string> GetUndoSnapshotsForPersistence()
    {
        return UndoSnapshots.Reverse().ToList();
    }

    public IReadOnlyList<string> GetRedoSnapshotsForPersistence()
    {
        return RedoSnapshots.Reverse().ToList();
    }

    public void ClearRuntimeState()
    {
        ThrowIfDisposed();
        ClearSelection();
        Controls.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Controls.CollectionChanged -= Controls_CollectionChanged;
        SelectedControlIds.CollectionChanged -= SelectedControlIds_CollectionChanged;
        SelectionChanged = null;
        DocumentChanged = null;
        HistoryChanged = null;
        DirtyStateChanged = null;
        Controls.Clear();
        SelectedControlIds.Clear();
        UndoSnapshots.Clear();
        RedoSnapshots.Clear();
        _selectedControl = null;
    }

    private void Controls_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        var selected = SelectedControlIds
            .Select(FindControl)
            .Where(control => control is not null)
            .Cast<DesignControlModel>()
            .ToList();
        var primary = IsCurrentControl(_selectedControl) ? _selectedControl : selected.LastOrDefault();
        SetSelection(selected, primary);
    }

    private void SelectedControlIds_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isDisposed || _isSynchronizingSelection)
            return;

        var selected = SelectedControlIds
            .Select(FindControl)
            .Where(control => control is not null)
            .Cast<DesignControlModel>()
            .DistinctBy(control => control.Id)
            .ToList();
        var primary = selected.FirstOrDefault(control => AreSameControl(control, _selectedControl))
            ?? selected.LastOrDefault();
        SetSelection(selected, primary);
    }

    private DesignControlModel? FindControl(string? id)
    {
        return Controls.FirstOrDefault(control => string.Equals(control.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCurrentControl(DesignControlModel? control)
    {
        return control is not null && Controls.Any(candidate => AreSameControl(candidate, control));
    }

    private static bool AreSameControl(DesignControlModel? left, DesignControlModel? right)
    {
        return ReferenceEquals(left, right)
            || (left is not null
                && right is not null
                && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreSameIds(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(DesignerDocumentSession));
    }
}

public sealed class DesignerDocumentSessionSelectionChangedEventArgs : EventArgs
{
    public DesignerDocumentSessionSelectionChangedEventArgs(
        DesignControlModel? oldSelectedControl,
        DesignControlModel? selectedControl,
        IReadOnlyList<string> oldSelectedControlIds,
        IReadOnlyList<string> selectedControlIds)
    {
        OldSelectedControl = oldSelectedControl;
        SelectedControl = selectedControl;
        OldSelectedControlIds = oldSelectedControlIds;
        SelectedControlIds = selectedControlIds;
    }

    public DesignControlModel? OldSelectedControl { get; }
    public DesignControlModel? SelectedControl { get; }
    public IReadOnlyList<string> OldSelectedControlIds { get; }
    public IReadOnlyList<string> SelectedControlIds { get; }
}
