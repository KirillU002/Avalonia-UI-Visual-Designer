using CommunityToolkit.Mvvm.ComponentModel;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public sealed class CollectionTreeNode : ObservableObject
{
    public string Id { get; }
    public string ParentId { get; internal set; } = "";
    private string _title = "";
    public string Title { get => _title; internal set => SetProperty(ref _title, value); }
    public ObservableCollection<CollectionTreeNode> Children { get; } = new();
    internal CollectionTreeNode(string id) => Id = id;
}

public sealed class CollectionEditorSession : IDisposable
{
    private MainWindowViewModel? _owner;
    private readonly Dictionary<string, CollectionTreeNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _legacyIds = new();
    private readonly Dictionary<string, string> _selectionByState = new(StringComparer.Ordinal);
    private string _lastState = "";
    public string DocumentId { get; }
    public string SessionId { get; }
    public string OwnerId { get; }
    public string PropertyName { get; }
    public string ItemKind { get; }
    public string EditorId { get; } = Guid.NewGuid().ToString("N");
    public string SelectedItemId { get; private set; } = "";
    public bool IsDisposed => _owner is null;
    public bool IsSynchronizing { get; private set; }
    public string ValidationMessage { get; private set; } = "";
    public ObservableCollection<CollectionTreeNode> Roots { get; } = new();
    public int Count => _nodes.Count;
    public CollectionTreeNode? SelectedNode => _nodes.GetValueOrDefault(SelectedItemId);
    public event EventHandler? Changed;
    internal CollectionEditorSession(MainWindowViewModel owner, DesignControlModel control, string property)
    { _owner = owner; OwnerId = control.Id; DocumentId = owner.ActiveDocumentId; SessionId = owner.DocumentSessionId; PropertyName = property; ItemKind = owner.ImportedCollectionKind(control,property) is { Length: > 0 } imported?imported:CollectionItemsContract.ItemKind(control.Type, property); Refresh(); }
    internal bool IsCurrent => _owner is { } vm && vm.ActiveCollectionEditor == this && vm.DocumentSessionId == SessionId && vm.ActiveDocumentId == DocumentId && vm.SelectedControl?.Id == OwnerId && vm.GetControl(OwnerId) is not null;
    internal CollectionItemsContract Read()
    {
        var control = _owner?.GetControl(OwnerId) ?? throw new InvalidOperationException("Владелец коллекции закрыт.");
        if(_owner!.ImportedCollectionKind(control,PropertyName).Length>0)return _owner.ReadImportedCollection(control,ItemKind);
        if (CollectionItemsContract.Read(control, PropertyName) is { } typed) return typed;
        var values = PropertyName == StandardItemsProperties.MenuKey ? StandardItemsProperties.Menu(control)
            : PropertyName == "Items" && control.Type == "ListBox" ? StandardItemsProperties.Read(control, "Items", Array.Empty<string>()).Select((text, index) => new StandardMenuItem { Id = LegacyId(index), Text = text, Kind = ItemKind }).ToList()
            : new List<StandardMenuItem>();
        return new() { Items = values };
    }
    private string LegacyId(int index)
    { if (!_legacyIds.TryGetValue(index, out var value)) _legacyIds[index] = value = "legacy_" + index; return value; }
    public StandardMenuItem? SelectedItem => IsCurrent ? CollectionItemsContract.Flatten(Read().Items).FirstOrDefault(i => i.Id == SelectedItemId) : null;
    public void Select(string? id)
    {
        if (!IsCurrent || IsSynchronizing || id is not null && !_nodes.ContainsKey(id)) return;
        var next = id ?? ""; if (next == SelectedItemId) return;
        _owner!.EndPropertyGridTextEdit(); SelectedItemId = next; RememberSelection(_lastState); _owner.RefreshCollectionSelection(this); Changed?.Invoke(this, EventArgs.Empty);
    }
    internal void Refresh()
    {
        if (_owner is null) return;
        var data = Read(); CollectionItemsContract.Validate(data, ItemKind);
        var state = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
        if (state != _lastState && _selectionByState.TryGetValue(state, out var selected)) SelectedItemId = selected;
        var active = new HashSet<string>(StringComparer.Ordinal); IsSynchronizing = true;
        try
        {
            void Sync(ObservableCollection<CollectionTreeNode> target, IEnumerable<StandardMenuItem> items, string parent)
            {
                var index = 0;
                foreach (var item in items)
                {
                    active.Add(item.Id);
                    if (!_nodes.TryGetValue(item.Id, out var node)) _nodes[item.Id] = node = new(item.Id);
                    node.ParentId = parent; node.Title = item.Kind + " · " + (string.IsNullOrEmpty(item.Name) ? item.Text : item.Name);
                    var old = target.IndexOf(node);
                    if (old < 0) target.Insert(index, node); else if (old != index) target.Move(old, index);
                    Sync(node.Children, item.Children, item.Id); index++;
                }
                while (target.Count > index) target.RemoveAt(target.Count - 1);
            }
            Sync(Roots, data.Items, OwnerId);
            foreach (var id in _nodes.Keys.Where(id => !active.Contains(id)).ToArray()) _nodes.Remove(id);
            if (!_nodes.ContainsKey(SelectedItemId)) SelectedItemId = Roots.FirstOrDefault()?.Id ?? "";
            _lastState = state; RememberSelection(state);
            ValidationMessage = "";
        }
        finally { IsSynchronizing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void RememberSelection(string state)
    { if (state.Length == 0) return; if (_selectionByState.Count >= 256 && !_selectionByState.ContainsKey(state)) _selectionByState.Remove(_selectionByState.Keys.First()); _selectionByState[state] = SelectedItemId; }
    private bool Change(Action<CollectionItemsContract> action, string? selection = null, bool propertyEdit = false)
    {
        if (!IsCurrent) return false;
        try
        {
            var previousSelection = SelectedItemId; RememberSelection(_lastState);
            var data = Read(); action(data); CollectionItemsContract.Validate(data, ItemKind);
            if (!_owner!.WriteCollection(this, data, propertyEdit)) return false;
            if (selection is not null) SelectedItemId = selection;
            Refresh(); if (!propertyEdit && SelectedItemId != previousSelection) _owner.RefreshCollectionSelection(this);
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { ValidationMessage = error.Message; Changed?.Invoke(this, EventArgs.Empty); return false; }
    }
    public bool Add(bool nested = false, bool separator = false)
    {
        var item = new StandardMenuItem { Kind = separator ? "Separator" : ItemKind, Text = separator ? "" : "Новый элемент" };
        return Change(data =>
        {
            var parent = nested ? CollectionItemsContract.Flatten(data.Items).FirstOrDefault(i => i.Id == SelectedItemId) : null;
            if (nested && parent?.Kind is not ("MenuItem" or "TreeViewItem")) throw new FormatException("Выберите MenuItem или TreeViewItem для вложенной вставки.");
            (parent?.Children ?? data.Items).Add(item);
        }, item.Id);
    }
    private static List<StandardMenuItem>? Siblings(List<StandardMenuItem> root, string id)
    { if (root.Any(i => i.Id == id)) return root; foreach (var item in root) if (Siblings(item.Children, id) is { } found) return found; return null; }
    public bool Remove() => Change(data => { var siblings = Siblings(data.Items, SelectedItemId); if (siblings is null) throw new FormatException("Выберите элемент."); siblings.RemoveAll(i => i.Id == SelectedItemId); });
    public bool Move(int delta) => Change(data =>
    {
        var siblings = Siblings(data.Items, SelectedItemId) ?? throw new FormatException("Выберите элемент.");
        var from = siblings.FindIndex(i => i.Id == SelectedItemId); var to = from + delta;
        if (to < 0 || to >= siblings.Count) throw new FormatException("Достигнута граница коллекции.");
        var item = siblings[from]; siblings.RemoveAt(from); siblings.Insert(to, item);
    });
    public bool SetValue(string id, string property, string text) => Change(data =>
    {
        if (id != SelectedItemId) throw new FormatException("Выбран другой элемент; устаревший editor отклонён.");
        var item = CollectionItemsContract.Flatten(data.Items).Single(i => i.Id == id);
        switch (property)
        {
            case "Name": item.Name = text; break;
            case "Header": case "Content" when item.Kind != "TabItem": item.Text = text; break;
            case "Content": item.Content = text; break;
            case "IsEnabled": item.IsEnabled = bool.Parse(text); break;
            case "IsExpanded": item.IsExpanded = bool.Parse(text); break;
            default: throw new FormatException("Свойство не входит в whitelist редактора.");
        }
    }, propertyEdit: true);
    public void Dispose()
    { _owner = null; Changed?.Invoke(this, EventArgs.Empty); Changed = null; Roots.Clear(); _nodes.Clear(); _legacyIds.Clear(); _selectionByState.Clear(); }
}
