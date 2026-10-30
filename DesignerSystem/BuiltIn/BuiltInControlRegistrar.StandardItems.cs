using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static partial class BuiltInControlRegistrar
{
    private static void RegisterStandardItems(IDesignerRegistry registry)
    {
        foreach (var type in new[] { DesignerControlTypes.DropDownButton, DesignerControlTypes.SplitButton, DesignerControlTypes.ListBox })
        {
            var list = type == DesignerControlTypes.ListBox;
            var properties = CreateCommonProperties(includeText: !list, includeBackground: true, includeForeground: true,
                includeBorder: true, includeFont: true, includePadding: true).ToList();
            properties.Add(new() { Key = "IsEnabled", Title = "IsEnabled", Category = "Behavior", Editor = PropertyEditorKind.Bool, DefaultValueJson = "true" });
            properties.Add(new() { Key = list ? "Items" : StandardItemsProperties.MenuKey, Title = list ? "Items" : "Flyout", Category = "Data", Editor = PropertyEditorKind.Collection, IsCollection = true });
            if (list)
            {
                properties.Add(new() { Key = "ItemsSource", Title = "ItemsSource", Category = "Data", Editor = PropertyEditorKind.Binding, IsBindable = true, DefaultValueJson = "\"\"" });
                properties.Add(new() { Key = "SelectedIndex", Title = "SelectedIndex", Category = "Behavior", Editor = PropertyEditorKind.Number, DefaultValueJson = "-1" });
                properties.Add(new() { Key = "SelectedItem", Title = "SelectedItem", Category = "Behavior", Editor = PropertyEditorKind.Binding, IsBindable = true, DefaultValueJson = "\"\"" });
                properties.Add(new() { Key = "SelectionMode", Title = "SelectionMode", Category = "Behavior", Editor = PropertyEditorKind.Enum, DefaultValueJson = "\"Single\"" });
                properties.Add(new() { Key = "ItemTemplate.DisplayField", Title = "ItemTemplate", Category = "Data", Editor = PropertyEditorKind.Binding, DefaultValueJson = "\"\"" });
                properties.Add(new() { Key = "ScrollViewer.HorizontalScrollBarVisibility", Title = "ScrollViewer.HorizontalScrollBarVisibility", Category = "Layout", Editor = PropertyEditorKind.Enum, DefaultValueJson = "\"Disabled\"" });
                properties.Add(new() { Key = "ScrollViewer.VerticalScrollBarVisibility", Title = "ScrollViewer.VerticalScrollBarVisibility", Category = "Layout", Editor = PropertyEditorKind.Enum, DefaultValueJson = "\"Auto\"" });
            }
            else properties.Add(new() { Key = "Flyout.Placement", Title = "Flyout.Placement", Category = "Behavior", Editor = PropertyEditorKind.Enum, DefaultValueJson = "\"Bottom\"" });
            if (list) properties.Add(new() { Key = nameof(DesignControlModel.BindingSourceId), BuiltInPropertyName = nameof(DesignControlModel.BindingSourceId), Title = "BindingSource", Editor = PropertyEditorKind.Binding, Category = "Data" });
            RegisterSimple(registry, type, type, "Стандартные", list ? "Список со статическими элементами или BindingSource." : "Стандартная кнопка Avalonia с Flyout.",
                false, DesignerLayoutModes.Absolute, properties, _ =>
                {
                    var result = CreateDefinition(type, (nameof(DesignControlModel.Text), type),
                        (nameof(DesignControlModel.Width), list ? 240d : 180d), (nameof(DesignControlModel.Height), list ? 180d : 40d),
                        (nameof(DesignControlModel.Background), "#FFFFFF"), (nameof(DesignControlModel.Foreground), "#0F172A"),
                        (nameof(DesignControlModel.BorderBrush), "#94A3B8"), (nameof(DesignControlModel.Padding), 8d),
                        (nameof(DesignControlModel.GeneratedButtonActionKey), "None"));
                    if (list) result.CustomProperties["Items"] = JsonSerializer.Serialize(new[] { "Элемент 1", "Элемент 2", "Элемент 3" });
                    else result.CustomProperties[StandardItemsProperties.MenuKey] = JsonSerializer.Serialize(new[] { new StandardMenuItem { Text = "Действие 1" }, new StandardMenuItem { Text = "Действие 2" } });
                    return result;
                });
        }
    }
}
