using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    public CollectionEditorSession? ActiveCollectionEditor { get; private set; }
    private readonly HashSet<string> _expandedPropertyPaths = new(StringComparer.Ordinal);
    private string ExpansionKey(PropertyGridRowViewModel row) => DocumentSessionId + ":" + row.ContextDocumentId + ":" + row.ContextControlId + ":" + row.Key;
    public bool IsPropertyGridExpanded(PropertyGridRowViewModel row) => _expandedPropertyPaths.Contains(ExpansionKey(row));
    public void SetPropertyGridExpanded(PropertyGridRowViewModel row, bool expanded)
    {
        var key = ExpansionKey(row);
        if (!expanded) _expandedPropertyPaths.Remove(key);
        else { if (_expandedPropertyPaths.Count >= 512) _expandedPropertyPaths.Remove(_expandedPropertyPaths.First()); _expandedPropertyPaths.Add(key); }
    }
    private bool DescribeCollectionProperty(PropertyGridRowViewModel row)
    {
        if (SelectedControl is not { } control || !CollectionControlFactory.Supports(control.Type)
            || row.Key is not ("Items" or "ContextMenu.Items" or "Flyout.Items")) return false;
        var type = row.Key == "ContextMenu.Items" ? typeof(ContextMenu) : row.Key == "Flyout.Items" ? typeof(MenuFlyout)
            : control.Type switch { "ItemsControl" => typeof(ItemsControl), "Carousel" => typeof(Carousel), "TabStrip" => typeof(TabStrip), "Menu" => typeof(Menu), "ComboBox" => typeof(ComboBox), "TabControl" => typeof(TabControl), "TreeView" => typeof(TreeView), _ => typeof(ListBox) };
        if (AvaloniaMetadataCatalog.Get(type).Properties.FirstOrDefault(p => p.Key == "Items") is not { } native) return false;
        var local = control.CustomProperties.Any(p => p.Key == CollectionItemsContract.StorageKey(row.Key) || p.Key == row.Key);
        var bound = row.Key == "Items" && CollectionItemsContract.HasExternalSource(control);
        var source = bound ? PropertyGridValueSource.Binding : local ? PropertyGridValueSource.LocalValue : PropertyGridValueSource.RegistrationDefault;
        if (bound && ClassifySourceExpression(StandardItemsProperties.Read(control, "ItemsSource", "")) == PropertyGridValueSource.Resource) source = PropertyGridValueSource.Resource;
        var reason = CollectionReadOnlyReason(control, row.Key);
        if (reason.Length != 0) row.ProtectValue(reason);
        row.SetPropertyMetadata(new(native, row.Label, row.Category, !row.IsReadOnly, !bound, false, !bound, "Reviewed collection adapter; 16 levels/2000 items; version/source/identity validation."), source, local, reason, blockReset: true);
        if (!row.IsReadOnly)
        {
            var items = CollectionItemsContract.Read(control, row.Key)?.Items;
            var count = items is not null ? CollectionItemsContract.Flatten(items).Count() : row.Key == StandardItemsProperties.MenuKey ? StandardItemsProperties.Menu(control).Count
                : control.Type == "ListBox" && row.Key == "Items" ? StandardItemsProperties.Read(control, "Items", Array.Empty<string>()).Length : 0;
            row.Refresh(count + " элементов", false, row.IsFavorite);
        }
        return true;
    }
    public string CollectionReadOnlyReason(DesignControlModel control, string property)
    {
        if (IsAxamlRoundTripDocument) return ImportedCollectionKind(control,property).Length>0&&GetAxamlStructuralCapability(control.Id).CanEditCollection
            ?"":"Структурное редактирование этого source Items/Binding/Template не поддерживается; XML сохраняется.";
        if (!CollectionControlFactory.Supports(control.Type) || string.IsNullOrEmpty(CollectionItemsContract.ItemKind(control.Type, property))) return "Нет проверенного адаптера коллекции.";
        if (property == "Items" && CollectionItemsContract.HasExternalSource(control)) return "ItemsSource управляется Binding/BindingSource либо содержит неизвестное значение. Локальные Items не заменяют источник.";
        try
        {
            CollectionItemsContract.ValidateLegacy(control, property);
            if (property == "Items" && control.Type == "ListBox" && CollectionItemsContract.Read(control, property) is null && !string.IsNullOrEmpty(StandardItemsProperties.Read(control, "SelectedItem", ""))) return "Прежний SelectedItem относится к строкам. Преобразование в ListBoxItem требует явного снятия этого выбора/Binding; значение сохранено.";
            var data = CollectionItemsContract.Read(control, property);
            if (data?.ExtensionData?.Count > 0 || data is not null && CollectionItemsContract.Flatten(data.Items).Any(i => i.ExtensionData?.Count > 0)) return "Неизвестная metadata коллекции сохранена. Изменения ограничены для совместимости.";
        }
        catch (FormatException error) { return error.Message; }
        return "";
    }
    public CollectionEditorSession? OpenCollectionEditor(string property)
    {
        if (SelectedControl is not { } control) return null;
        var reason = CollectionReadOnlyReason(control, property);
        if (reason.Length != 0) { StatusText = reason; return null; }
        if (ActiveCollectionEditor is { } old) CloseCollectionEditor(old);
        EndPropertyGridTextEdit();
        var session = new CollectionEditorSession(this, control, property); ActiveCollectionEditor = session;
        RefreshCollectionSelection(session); return session;
    }
    public void CloseCollectionEditor(CollectionEditorSession session)
    {
        if (ActiveCollectionEditor != session) return;
        EndPropertyGridTextEdit(); ActiveCollectionEditor = null; session.Dispose(); RebuildPropertyGrid("CollectionEditorClosed");
    }
    internal void RefreshCollectionSelection(CollectionEditorSession session)
    { if (ActiveCollectionEditor == session) { RefreshStructureSelection(); RebuildPropertyGrid("CollectionItemSelection"); } }
    internal bool WriteCollection(CollectionEditorSession session, CollectionItemsContract data, bool propertyEdit)
    {
        if(IsAxamlRoundTripDocument)return WriteImportedCollection(session,data);
        if (!session.IsCurrent || GetControl(session.OwnerId) is not { } owner || CollectionReadOnlyReason(owner, session.PropertyName).Length != 0) return false;
        CollectionItemsContract.Validate(data, session.ItemKind);
        ValidateCollectionItemNames(session, data);
        var raw = JsonSerializer.Serialize(data); var key = CollectionItemsContract.StorageKey(session.PropertyName);
        if (owner.CustomProperties.FirstOrDefault(p => p.Key == key)?.ValueJson == raw) return false;
        if (!propertyEdit) EndPropertyGridTextEdit();
        var owns = _undoBatchDepth == 0;
        if (owns) { _lastHistoryMutationUtc = DateTime.MinValue; BeginUndoBatch(); }
        try
        {
            var oldNames = StandardItemsProperties.IsMenuButton(owner.Type) ? CollectionItemsContract.Flatten(StandardItemsProperties.Menu(owner)).Where(i => i.Kind != "Separator").ToDictionary(i => i.Id, i => StandardItemsProperties.MenuSource(owner, i).Name) : new();
            StandardItemsProperties.Write(owner, key, data);
            if (StandardItemsProperties.IsMenuButton(owner.Type))
            {
                var names = CollectionItemsContract.Flatten(data.Items).Where(i => i.Kind != "Separator").ToDictionary(i => i.Id, i => StandardItemsProperties.MenuSource(owner, i).Name);
                foreach (var previous in oldNames)
                {
                    foreach (var rule in Interactions.Where(i => i.SourceControlName == previous.Value || i.TargetControlName == previous.Value).ToArray())
                    {
                        if (!names.TryGetValue(previous.Key, out var next)) { Interactions.Remove(rule); continue; }
                        if (rule.SourceControlName == previous.Value) rule.SourceControlName = next;
                        if (rule.TargetControlName == previous.Value) rule.TargetControlName = next;
                    }
                }
            }
            NotifyDesignerStateChanged(); RebuildStructureTree();
        }
        finally { if (owns) CommitUndoBatch(); }
        return true;
    }
    private void ValidateCollectionItemNames(CollectionEditorSession session, CollectionItemsContract data)
    {
        var reserved = new HashSet<string>(UnsafeGeneratedIdentifiers, StringComparer.OrdinalIgnoreCase);
        foreach (var control in Controls)
        {
            reserved.Add(control.Name); reserved.Add(GetExportControlName(control));
            if (!CollectionControlFactory.Supports(control.Type)) continue;
            foreach (var property in new[] { "Items", StandardItemsProperties.MenuKey, "ContextMenu.Items" })
            {
                if (control.Id == session.OwnerId && property == session.PropertyName) continue;
                try
                {
                    var items = CollectionItemsContract.Read(control, property)?.Items;
                    if (items is null && property == StandardItemsProperties.MenuKey && StandardItemsProperties.IsMenuButton(control.Type)) items = StandardItemsProperties.Menu(control);
                    if (items is not null) foreach (var item in CollectionItemsContract.Flatten(items)) reserved.Add(CollectionControlFactory.NativeName(control.Id, item));
                }
                catch (FormatException) { /* Unrelated invalid payload is preserved and diagnosed by its own property row. */ }
            }
        }
        foreach (var item in CollectionItemsContract.Flatten(data.Items))
            if (!reserved.Add(CollectionControlFactory.NativeName(session.OwnerId, item))) throw new FormatException("Name элемента должен быть уникальным в форме и не совпадать с зарезервированным именем.");
    }
    private IEnumerable<PropertyGridRowViewModel> CollectionItemRows(CollectionEditorSession session)
    {
        session.Refresh(); RefreshStructureSelection(); var item = session.SelectedItem;
        if (item is null) { yield return CreateReadOnlyRow(PropertyGridCategoryData, "@collection/empty", "Коллекция", "0 элементов", "Добавьте элемент в Collection Editor."); yield break; }
        var id = item.Id;
        var editorId = session.EditorId;
        yield return CreateReadOnlyRow(PropertyGridCategoryCommon, "@collection/type", "Type", item.Kind, "Настоящий Avalonia control внутри коллекции владельца; отдельный Canvas control не создаётся.");
        yield return CreateReadOnlyRow(PropertyGridCategoryCommon, "@collection/id", "ControlId", id, "Устойчивый Id элемента в существующем JSON property bag.");
        yield return CreateReadOnlyRow(PropertyGridCategoryLayout, "@collection/parent", "ParentId", session.SelectedNode?.ParentId ?? session.OwnerId, "Id владельца или вложенного элемента коллекции.");
        IEnumerable<(string Key, string Value, bool Boolean)> Values()
        {
            yield return ("Name", item.Name, false);
            if (item.Kind != "Separator") yield return (item.Kind is "MenuItem" or "TreeViewItem" or "TabItem" ? "Header" : "Content", item.Text, false);
            if (item.Kind == "TabItem") yield return ("Content", item.Content, false);
            yield return ("IsEnabled", item.IsEnabled.ToString(), true);
            if (item.Kind == "TreeViewItem") yield return ("IsExpanded", item.IsExpanded.ToString(), true);
        }
        var type = item.Kind switch { "ContentControl" => typeof(ContentControl), "TabStripItem" => typeof(TabStripItem), "MenuItem" => typeof(MenuItem), "ComboBoxItem" => typeof(ComboBoxItem), "ListBoxItem" => typeof(ListBoxItem), "TabItem" => typeof(TabItem), "TreeViewItem" => typeof(TreeViewItem), _ => typeof(Separator) };
        foreach (var value in Values())
        {
            var key = value.Key;
            void Apply(PropertyGridRowViewModel row, string text)
            {
                var active = ActiveCollectionEditor;
                if (active?.EditorId != editorId) { row.ValidationMessage = "Редактор коллекции закрыт или открыта другая session."; return; }
                if (!active.SetValue(id, key, text)) row.ValidationMessage = active.ValidationMessage;
            }
            var row = CreateRow(PropertyGridCategoryCommon, "@collection/" + key, key, value.Boolean ? PropertyGridEditorKind.Bool : PropertyGridEditorKind.Text, value.Value,
                "Проверенное свойство элемента; одна атомарная операция. Header/Content поддерживает текст, не произвольный object graph.", Apply,
                (r, flag) => Apply(r, flag.ToString()), boolValue: value.Value == "True");
            if (AvaloniaMetadataCatalog.Get(type).Properties.FirstOrDefault(p => p.Key == key) is { } native)
                row.SetPropertyMetadata(new(native, key, row.Category, true, true, false, true, "Whitelist; уникальный Name; ограничение глубины; source guard."), PropertyGridValueSource.LocalValue, true, "Элемент коллекции JSON.", () =>
                {
                    var reset = key switch { "IsEnabled" => "True", "IsExpanded" => "False", _ => "" };
                    EndPropertyGridTextEdit(); _lastHistoryMutationUtc = DateTime.MinValue; BeginUndoBatch();
                    try { if (ActiveCollectionEditor?.EditorId == editorId) ActiveCollectionEditor.SetValue(id, key, reset); RebuildPropertyGrid("CollectionItemReset"); } finally { CommitUndoBatch(); }
                }, false);
            row.ResetDescription = "Восстановить значение текстового/булевого поля контракта элемента; не Avalonia.ClearValue.";
            if(IsAxamlRoundTripDocument)
            {
                row.ProtectValue("В imported Items свойства редактируются выбором самого элемента в Explorer; структурные кнопки редактора доступны отдельно.");
            }
            yield return row;
        }
    }
    private IEnumerable<PropertyGridRowViewModel> HierarchicalCollectionRows(DesignControlModel control)
    {
        if (!CollectionControlFactory.Supports(control.Type)) yield break;
        foreach (var property in new[] { "Items", "ContextMenu.Items" }.Where(p => p != "Items" || CollectionControlFactory.IsRoot(control.Type)))
        {
            var reason = CollectionReadOnlyReason(control, property);
            yield return reason.Length == 0 ? CreateActionRow(PropertyGridCategoryData, property, property, "", "Коллекция Avalonia: Add/Remove/Reorder и свойства элементов через существующий PropertyGrid.", "Изменить коллекцию…")
                : CreateReadOnlyRow(PropertyGridCategoryData, property, property, "Read-only", reason);
        }
        if (!CollectionControlFactory.IsRoot(control.Type)) yield break;
        yield return CreateBoolRow(PropertyGridCategoryBehavior, "IsEnabled", "IsEnabled", StandardItemsProperties.Read(control, "IsEnabled", true), "Доступность Avalonia control.", v => { StandardItemsProperties.Write(control, "IsEnabled", v); NotifyDesignerStateChanged(); });
        if (control.Type is "ComboBox" or "TabControl" or "Carousel" or "TabStrip") yield return CreateNumberRow(PropertyGridCategoryBehavior, "SelectedIndex", "SelectedIndex", StandardItemsProperties.Read(control, "SelectedIndex", control.Type == "TabControl" ? 0 : -1), "Индекс выбранного элемента, -1 — нет выбора.", v => { StandardItemsProperties.Write(control, "SelectedIndex", Math.Max(-1, (int)v)); NotifyDesignerStateChanged(); });
        if (control.Type == "TreeView") yield return CreateEnumRow(PropertyGridCategoryBehavior, "SelectionMode", "SelectionMode", StandardItemsProperties.Read(control, "SelectionMode", "Single"), new[] { "Single", "Multiple", "Toggle", "AlwaysSelected" }, "Режим выбора TreeView.", v => { StandardItemsProperties.Write(control, "SelectionMode", v); NotifyDesignerStateChanged(); });
    }
    // The per-control exporter records diagnostics and can keep an incomplete preview.
    // Distribution exports must reject the known unsupported source before writing files,
    // including sources on inactive forms. ListBox's existing BindingSource adapter is separate.
    private void EnsureLocalItemsSourcesExportable()
    {
        var failures = Controls.Where(c => CollectionControlFactory.IsRoot(c.Type) && CollectionItemsContract.HasExternalSource(c))
            .Select(c => c.Name).Concat(CurrentProject.Forms.Where(f => !string.Equals(f.Id, ActiveDocumentId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => f.Document.Controls.Where(c => CollectionControlFactory.IsRoot(c.Type) && CollectionItemsContract.HasExternalSource(c))
                    .Select(c => f.Name + "/" + c.Name))).ToArray();
        if (failures.Length != 0)
            throw new NotSupportedException("Экспорт остановлен: внешний ItemsSource не поддерживается локальным collection adapter: " + string.Join(", ", failures) + ". Исходные данные сохранены.");
        foreach(var control in Controls) ValidateReviewedExportContract(StandardItemsControlFactory.Snapshot(control));
        foreach(var form in CurrentProject.Forms.Where(f=>f.Id!=ActiveDocumentId))
            foreach(var control in form.Document.Controls) ValidateReviewedExportContract(control);
    }
    private static void ValidateReviewedExportContract(DesignerControlFileModel model)
    {
        var type=StandardControlProperties.PropertyTypeFor(model.Type);
        if(type is null)return; // Plugins/DataGrid keep their existing specialized validators.
        foreach(var value in model.CustomProperties)
        {
            try
            {
                if(value.Key.EndsWith(".Collection",StringComparison.Ordinal))
                { _=CollectionItemsContract.Read(model,value.Key[..^".Collection".Length]);continue; }
                if(StandardControlProperties.Find(model.Type,value.Key) is {} property)
                { if(!StandardControlProperties.TryEncode(property,StandardControlProperties.Read(property,value.ValueJson),out _))throw new FormatException("Invalid literal");continue; }
                var native=AvaloniaMetadataCatalog.Get(type).Properties.FirstOrDefault(p=>p.Key==value.Key);
                if(native is null)continue;
                if(model.Type=="ListBox"&&value.Key is "Items" or "ItemsSource" or "SelectedItem")continue;
                using var json=JsonDocument.Parse(value.ValueJson);
                if(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ||json.RootElement.ValueKind==JsonValueKind.String&&ClassifySourceExpression(json.RootElement.GetString()) is PropertyGridValueSource.Binding or PropertyGridValueSource.Resource or PropertyGridValueSource.Complex)
                    throw new NotSupportedException("Нет безопасного export adapter для этого сложного значения");
            }
            catch(Exception error)when(error is FormatException or JsonException or NotSupportedException)
            { throw new NotSupportedException("Экспорт остановлен: "+model.Name+"."+value.Key+" — "+error.Message+". Исходное значение сохранено.",error); }
        }
    }
    private void AppendCollectionControlXaml(StringBuilder sb, DesignControlModel control, int indent)
    {
        var pad = Indent(indent); var type = control.Type; var flyout = StandardItemsProperties.IsMenuButton(type);
        if (CollectionControlFactory.IsRoot(type) && CollectionItemsContract.HasExternalSource(control))
            throw new NotSupportedException("Локальный collection adapter не заменяет внешний ItemsSource. Исходные данные сохранены; export требует отдельного data adapter.");
        var key = flyout ? StandardItemsProperties.MenuKey : "Items";
        var data = CollectionItemsContract.Read(control, key) ?? new();
        var attributes = $"x:Name=\"{GetExportControlName(control)}\" {PlacementAttributes(control)}{BackgroundAttribute(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{BorderStyleAttributes(control)} Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)} IsEnabled=\"{BoolToXaml(StandardItemsProperties.Read(control, "IsEnabled", true))}\"";
        attributes += $" CornerRadius=\"{ToInvariant(control.CornerRadius)}\"";
        if (flyout) attributes += $" Content=\"{StandardContentLiteral(control.Text)}\"";
        if (type is "ComboBox" or "TabControl" or "ListBox" or "Carousel" or "TabStrip") attributes += $" SelectedIndex=\"{StandardItemsProperties.Read(control, "SelectedIndex", type == "TabControl" ? 0 : -1)}\"";
        if (type is "ListBox" or "TreeView") attributes += $" SelectionMode=\"{EscapeXml(StandardItemsProperties.Read(control, "SelectionMode", "Single"))}\"";
        if (flyout && HasConfiguredInteractionEvent(control, InteractionModel.EventButtonClick)) attributes += $" Click=\"{GetExportControlName(control)}Click\"";
        sb.AppendLine($"{pad}<{type} {attributes}>");
        if (flyout) sb.AppendLine($"{Indent(indent + 1)}<{type}.Flyout><MenuFlyout>");
        CollectionControlFactory.AppendItems(sb, control.Id, data.Items, indent + (flyout ? 2 : 1), handlers: flyout,
            resolveName: flyout ? i => GetExportControlName(StandardItemsProperties.MenuSource(control, i)) : null);
        if (flyout) sb.AppendLine($"{Indent(indent + 1)}</MenuFlyout></{type}.Flyout>");
        CollectionControlFactory.AppendContextMenu(sb, control, indent + 1); sb.AppendLine($"{pad}</{type}>");
    }
    private void SelectCollectionStructureItem(StructureTreeItemModel item)
    {
        if (GetControl(item.CollectionOwnerId) is not { } owner) return;
        if (SelectedControl?.Id != owner.Id) SelectSingleControl(owner);
        var session = ActiveCollectionEditor;
        if (session is null || session.OwnerId != owner.Id || session.PropertyName != item.CollectionProperty)
            session = OpenCollectionEditor(item.CollectionProperty);
        session?.Select(item.CollectionItemId);
    }
    private void AddCollectionStructureChildren(DesignControlModel control, StructureTreeItemModel node)
    {
        if (!CollectionControlFactory.Supports(control.Type)) return;
        foreach (var property in new[] { StandardItemsProperties.IsMenuButton(control.Type) ? StandardItemsProperties.MenuKey : "Items", "ContextMenu.Items" })
        {
            try
            {
                var values = CollectionItemsContract.Read(control, property)?.Items;
                if (values is null && property == StandardItemsProperties.MenuKey) values = StandardItemsProperties.Menu(control);
                if (values is null) continue;
                void Add(StructureTreeItemModel parent, IEnumerable<StandardMenuItem> items)
                {
                    foreach (var item in items)
                    {
                        var child = new StructureTreeItemModel(null, control.Id + ":" + property + ":" + item.Id, item.Name.Length == 0 ? item.Text : item.Name, item.Kind, item.Text,
                            item.Children.Count > 0, false, false, false)
                        { CollectionOwnerId = control.Id, CollectionProperty = property, CollectionItemId = item.Id };
                        parent.Children.Add(child); Add(child, item.Children);
                    }
                }
                _ = CollectionItemsContract.Flatten(values).Count(); Add(node, values);
            }
            catch (FormatException) { /* Invalid/future payload remains in JSON and is reported by its property row. */ }
        }
    }
}
