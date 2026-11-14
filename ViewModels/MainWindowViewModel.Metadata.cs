using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.PluginContracts;
using System;
using System.Linq;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private PropertyGridRowViewModel ApplyMetadataPropertyRow(PropertyGridRowViewModel row)
    {
        if (row.UsesMetadata || row.IsReadOnly || SelectedControl is not { } target || SelectedMetadataProperty(row.Key) is not { } property
            || UsesNativeLayout && FormDesigner.Models.LayoutPlacementProperties.SizeConstraintKeys.Contains(row.Key)) return row;
        var stored = target.CustomProperties.FirstOrDefault(p => p.Key == row.Key)?.ValueJson;
        string value;
        try { value = StandardControlProperties.Read(property, stored); }
        catch (Exception error) when (error is System.Text.Json.JsonException or FormatException)
        { return CreateReadOnlyRow(row.Category, row.Key, row.Label, "Некорректное сохранённое значение", "Сложное/неизвестное значение сохранено; scalar editor не заменяет binding или object."); }
        return CreateMetadataPropertyRow(property, value, text =>
        {
            if (StandardControlProperties.TryWriteModel(target, property, text)) NotifyDesignerStateChanged();
        });
    }
    private AvaloniaPropertyInfo? SelectedMetadataProperty(string key)
    {
        if (SelectedControl is null) return null;
        var sourceType = SelectedControl.Type;
        if (_activeAxamlRoundTripDocument?.SourceMap.TryGet(SelectedControl.Id, out var source) == true)
            sourceType = source.Element.LocalName;
        return StandardControlProperties.Find(sourceType, key);
    }

    private PropertyGridRowViewModel CreateMetadataPropertyRow(AvaloniaPropertyInfo property, string value, Action<string> apply)
    {
        var kind = StandardControlProperties.EditorFor(property) switch
        {
            MetadataEditor.Boolean => PropertyGridEditorKind.Bool,
            MetadataEditor.Enumeration => PropertyGridEditorKind.Enum,
            MetadataEditor.Number => PropertyGridEditorKind.Number,
            MetadataEditor.Color => PropertyGridEditorKind.Color,
            MetadataEditor.Thickness => PropertyGridEditorKind.Thickness,
            MetadataEditor.CornerRadius => PropertyGridEditorKind.CornerRadius,
            MetadataEditor.GridLength => PropertyGridEditorKind.GridLength,
            MetadataEditor.Font => PropertyGridEditorKind.Font,
            _ => PropertyGridEditorKind.Text
        };
        var category = NormalizePropertyGridCategory(StandardControlProperties.CategoryFor(property));
        var row = CreateRow(category, property.Key, property.Name, kind, value,
            $"Avalonia {property.OwnerType.Name}.{property.Name}. {StandardControlProperties.Hint(property)}",
            (item, text) =>
            {
                if (!StandardControlProperties.TryEncode(property, text, out _))
                {
                    item.ValidationMessage = "Недопустимое значение " + property.Name;
                    item.ValidationState = PropertyGridValidationState.Error;
                    return;
                }
                item.ValidationMessage = "";
                item.ValidationState = PropertyGridValidationState.None;
                apply(text);
            }, (_, flag) => apply(flag ? "True" : "False"), boolValue: value == "True", isAdvanced: true);
        row.UsesMetadata = true;
        row.LiteralEquals = (left, right) => StandardControlProperties.TryParseValue(property, left, out var a)
            && StandardControlProperties.TryParseValue(property, right, out var b)
            && (a is Avalonia.Media.ISolidColorBrush ba && b is Avalonia.Media.ISolidColorBrush bb ? ba.Color == bb.Color : Equals(a, b));
        if (kind == PropertyGridEditorKind.Enum)
            row.SetOptions(StandardControlProperties.Options(property).Select(v => new PropertyGridOptionViewModel(v,
                v == MetadataValueCodec.NullText ? "Не задано (null)" : v)));
        return row;
    }
}
