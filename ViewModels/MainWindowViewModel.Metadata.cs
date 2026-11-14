using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.PluginContracts;
using System;
using System.Linq;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
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
        var kind = property.Editor switch
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
        if (kind == PropertyGridEditorKind.Enum)
            row.SetOptions(StandardControlProperties.Options(property).Select(v => new PropertyGridOptionViewModel(v,
                v == MetadataValueCodec.NullText ? "Не задано (null)" : v)));
        return row;
    }
}
