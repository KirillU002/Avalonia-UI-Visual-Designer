using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private long _editingPropertyGridRevision;
    private long _inspectorDocumentRevision;
    private PropertyGridRowViewModel? _editingPropertyGridRow;
    // Shipped, non-collectible types only; plugin discovery retains its existing CWT lifecycle.
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> InspectorStandardTypes = new(() =>
        AvaloniaMetadataCatalog.DiscoverControls().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
    private string InspectorSourceType => SelectedControl is { } target && _activeAxamlRoundTripDocument?.SourceMap.TryGet(target.Id, out var source) == true
        ? source.Element.LocalName : SelectedControl?.Type == "Group" ? "Canvas" : SelectedControl?.Type ?? "Window";
    private static string NativeInspectorKey(string key, string type) => key switch
    {
        "Text" when type is "Button" or "RadioButton" or "CheckBox" or "ToggleSwitch" or "SplitButton" or "DropDownButton" => "Content",
        "X" => "Canvas.Left", "Y" => "Canvas.Top", "GridRow" => "Grid.Row", "GridColumn" => "Grid.Column",
        "GridRowSpan" => "Grid.RowSpan", "GridColumnSpan" => "Grid.ColumnSpan", "LayoutOrientation" => "Orientation",
        "LayoutSpacing" => "Spacing", "GridRowDefinitions" or "SurfaceGridRowDefinitions" => "RowDefinitions",
        "GridColumnDefinitions" or "SurfaceGridColumnDefinitions" => "ColumnDefinitions", "FormTitle" => "Title",
        "DesignWidth" => "Width", "DesignHeight" => "Height", "SurfaceBackground" => "Background",
        "PlaceholderText" => "Watermark", "ImageSource" => "Source", _ => key
    };
    private static PropertyGridValueSource ClassifySourceExpression(string? value)
    {
        var text = value?.TrimStart() ?? "";
        return text.StartsWith("{Binding", StringComparison.Ordinal) || text.StartsWith("{CompiledBinding", StringComparison.Ordinal)
            || text.StartsWith("{TemplateBinding", StringComparison.Ordinal) || text.StartsWith("{MultiBinding", StringComparison.Ordinal) ? PropertyGridValueSource.Binding
            : text.StartsWith("{StaticResource", StringComparison.Ordinal) || text.StartsWith("{DynamicResource", StringComparison.Ordinal) ? PropertyGridValueSource.Resource
            : text.StartsWith("{", StringComparison.Ordinal) && text != "{x:Null}" ? PropertyGridValueSource.Complex : PropertyGridValueSource.LocalValue;
    }
    private PropertyGridRowViewModel DescribeInspectorProperty(PropertyGridRowViewModel row)
    {
        using var lookup = LayoutDropProfile.Stage("InspectorMetadataLookup");
        var typeName = InspectorSourceType;
        if (!InspectorStandardTypes.Value.TryGetValue(typeName, out var type)) return row;
        if (SelectedControl is null && row.Key is "SurfaceGridRowDefinitions" or "SurfaceGridColumnDefinitions") type = typeof(Grid);
        var nativeKey = NativeInspectorKey(row.Key, typeName);
        var property = AvaloniaMetadataCatalog.Get(type).Properties.FirstOrDefault(p => p.Key == nativeKey);
        if (property is null) return row; // Designer-only legacy fields do not acquire a fictitious Avalonia schema.
        if (row.Key == "Margin" && property.PropertyType == typeof(Thickness)) row.UseEditor(PropertyGridEditorKind.Thickness);
        if (row.Key is "GridRowDefinitions" or "GridColumnDefinitions" or "SurfaceGridRowDefinitions" or "SurfaceGridColumnDefinitions")
            row.UseEditor(PropertyGridEditorKind.GridDefinitions);
        var reviewed = StandardControlProperties.Find(typeName, row.Key);
        bool? local = null; var sourceKind = PropertyGridValueSource.Model; var detail = "";
        if (row.IsReadOnly && property.Editor is MetadataEditor.Collection or MetadataEditor.ReadOnly) sourceKind = PropertyGridValueSource.Complex;
        Action? reset = null; var sourceProperty = false;
        if (SelectedControl is { } target)
        {
            if (reviewed is not null)
            {
                row.UsesMetadata = true;
                var stored = target.CustomProperties.FirstOrDefault(p => p.Key == row.Key);
                local = stored is not null; sourceKind = local.Value ? PropertyGridValueSource.LocalValue : PropertyGridValueSource.RegistrationDefault;
                if (stored is not null)
                {
                    try { using var json = JsonDocument.Parse(stored.ValueJson);
                        sourceKind = json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? PropertyGridValueSource.Complex
                            : json.RootElement.ValueKind == JsonValueKind.String ? ClassifySourceExpression(json.RootElement.GetString()) : sourceKind; }
                    catch (JsonException) { sourceKind = PropertyGridValueSource.Complex; }
                }
                reset = () => RemoveMetadataLocalValue(row, target, row.Key);
            }
            if (_activeAxamlRoundTripDocument?.SourceMap.TryGet(target.Id, out var reference) == true)
            {
                sourceProperty = true;
                var mapped = AxamlRoundTripPropertyMap.PropertiesFor(typeName).FirstOrDefault(p => p.Key == row.Key);
                var attribute = mapped?.ResolveExistingAttribute(reference.Element) ?? reference.Element.FindAttribute(nativeKey);
                if (attribute is not null)
                {
                    var expression = ClassifySourceExpression(attribute.Value);
                    if (reviewed is not null && local == false && expression == PropertyGridValueSource.LocalValue && reference.Capability.CanEditProperty(row.Key))
                    { sourceKind = PropertyGridValueSource.RegistrationDefault; detail = "Локальный attribute будет удалён минимальным patch."; }
                    else { local = true; sourceKind = expression; detail = "Исходный attribute; минимальный patch."; }
                }
                else if (reference.Element.Children.Any(c => c.LocalName.EndsWith("." + property.Name, StringComparison.Ordinal)))
                { sourceKind = PropertyGridValueSource.Complex; detail = "Property element сохраняется без регенерации."; }
                else if (reference.Element.Children.Where(c => c.LocalName.EndsWith(".Styles", StringComparison.Ordinal))
                    .SelectMany(AxamlImportStructureReport.Descendants).FirstOrDefault(e => e.LocalName == "Setter" && e.GetAttributeValue("Property") == nativeKey) is { } setter)
                {
                    sourceKind = ClassifySourceExpression(setter.GetAttributeValue("Value"));
                    if (sourceKind == PropertyGridValueSource.LocalValue) sourceKind = PropertyGridValueSource.Style;
                    detail = "Исходный Style сохраняется; source projection не вычисляет его ресурсы.";
                }
                else if (reviewed is null) sourceKind = PropertyGridValueSource.Unknown;
            }
        }
        var importable = AxamlRoundTripPropertyMap.PropertiesFor(type.Name).Any(p => p.AttributeNames.Contains(nativeKey));
        var schema = new PropertyGridPropertyMetadata(property, row.Label, row.Category, !row.IsReadOnly && sourceKind is not (PropertyGridValueSource.Binding or PropertyGridValueSource.Resource or PropertyGridValueSource.Complex), reviewed is not null || !row.IsReadOnly,
            importable, reviewed is not null || !row.IsReadOnly, "Типизированный parser; AvaloniaProperty.IsValidValue; известные Minimum/Maximum; source capability guard.");
        row.SetPropertyMetadata(schema, sourceKind, local, detail, reset, sourceProperty && reset is null);
        if (sourceKind is PropertyGridValueSource.Binding or PropertyGridValueSource.Resource or PropertyGridValueSource.Complex)
            row.ProtectValue("Значение сохраняется; scalar editor не подменяет Binding/Resource/сложный объект.");
        return row;
    }
    private IEnumerable<PropertyGridRowViewModel> BuildMetadataReadOnlyRows()
    {
        if (SelectedControl is null || !InspectorStandardTypes.Value.TryGetValue(InspectorSourceType, out var type)) yield break;
        var names = StandardControlProperties.ReadOnlyInspectorKeys;
        foreach (var p in AvaloniaMetadataCatalog.Get(type).Properties.Where(p => names.Contains(p.Key)))
            yield return CreateReadOnlyRow(PropertyGridCategoryAdvanced, p.Key, p.Name, "<" + p.PropertyType.Name + ">",
                "Безопасное представление типа; instance getter не вызывается. Специализированный редактор / source-preserving workflow.");
    }
    private void RemoveMetadataLocalValue(PropertyGridRowViewModel row, DesignControlModel target, string key)
    {
        if (row.IsReadOnly || !CanApplyPropertyGridRow(row, "<unset>")) return;
        if (_isEditingPropertyGrid) EndPropertyGridTextEdit();
        _lastHistoryMutationUtc = DateTime.MinValue;
        BeginUndoBatch();
        try
        {
            foreach (var stored in target.CustomProperties.Where(p => p.Key == key).ToArray()) target.CustomProperties.Remove(stored);
            NotifyDesignerStateChanged(); RebuildPropertyGrid("RemoveLocalValue");
        }
        finally { CommitUndoBatch(); }
    }
    public void ResetPropertyGridProperty(PropertyGridRowViewModel row)
    {
        if (!row.CanResetToDefault || !CanApplyPropertyGridRow(row, "<reset>")) return;
        if (_isEditingPropertyGrid) EndPropertyGridTextEdit();
        _lastHistoryMutationUtc = DateTime.MinValue; BeginUndoBatch();
        try { row.ResetToDefault(); } finally { CommitUndoBatch(); }
    }
    internal void RefreshNativeInspectorValues(Control? native)
    {
        if (native is null || !AvaloniaMetadataCatalog.IsStandard(native.GetType()) || _isEditingPropertyGrid) return;
        using var stage = LayoutDropProfile.Stage("InspectorEffectiveValueReading");
        var supported = AvaloniaMetadataCatalog.Get(native.GetType()).Properties.Select(p => p.Property).Where(p => p is not null).ToHashSet();
        foreach (var row in PropertyGridCategories.SelectMany(c => c.Rows).Distinct())
        {
            if (row.IsReadOnly || row.SourceDetail.StartsWith("Исходный Style", StringComparison.Ordinal) || row.PropertyMetadata?.Native.Property is not { } property || !supported.Contains(property)) continue;
            var diagnostic = native.GetDiagnostic(property);
            var source = row.ValueSource;
            if (row.UsesMetadata && row.HasLocalValue == false) source = diagnostic.Priority switch
            {
                BindingPriority.Style or BindingPriority.StyleTrigger or BindingPriority.Template => PropertyGridValueSource.Style,
                BindingPriority.Inherited => PropertyGridValueSource.Inherited, BindingPriority.Unset => PropertyGridValueSource.RegistrationDefault,
                _ => source
            };
            var text = MetadataValueCodec.Display(diagnostic.Value);
            row.SetNativeValueSource(source, text);
            if (row.UsesMetadata && row.HasLocalValue == false)
            {
                if (diagnostic.Value is IBrush and not ISolidColorBrush) row.ProtectValue("Сложный Brush сохраняется; автоматическая замена SolidColorBrush запрещена.");
                else if (StandardControlProperties.TryParseValue(row.PropertyMetadata.Native, text, out _)) row.Refresh(text, diagnostic.Value is true, row.IsFavorite);
            }
        }
    }
}
