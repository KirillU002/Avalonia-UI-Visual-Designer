using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System.Linq;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static partial class BuiltInControlRegistrar
{
    private static void RegisterCollectionControls(IDesignerRegistry registry)
    {
        foreach (var type in new[] { "Menu", "ComboBox", "TabControl", "TreeView" })
        {
            var properties = CreateCommonProperties(includeText: false, includeBackground: true, includeForeground: true, includeBorder: true, includeFont: true, includePadding: true).ToList();
            properties.Add(new() { Key = "Items", Title = "Items", Category = "Data", Editor = PropertyEditorKind.Collection, IsCollection = true });
            properties.Add(new() { Key = "ContextMenu.Items", Title = "ContextMenu.Items", Category = "Data", Editor = PropertyEditorKind.Collection, IsCollection = true });
            properties.Add(new() { Key = "IsEnabled", Title = "IsEnabled", Category = "Behavior", Editor = PropertyEditorKind.Bool, DefaultValueJson = "true" });
            properties.Add(new() { Key = "SelectedIndex", Title = "SelectedIndex", Category = "Behavior", Editor = PropertyEditorKind.Number, DefaultValueJson = "-1" });
            if (type == "TreeView") properties.Add(new() { Key = "SelectionMode", Title = "SelectionMode", Category = "Behavior", Editor = PropertyEditorKind.Enum, DefaultValueJson = "\"Single\"" });
            RegisterSimple(registry, type, type, "Коллекции", "Стандартный Avalonia " + type + ". Элементы редактируются через Items в Inspector.", false, DesignerLayoutModes.Absolute,
                properties, _ => CreateDefinition(type, (nameof(DesignControlModel.Width), 260d), (nameof(DesignControlModel.Height), type is "Menu" or "ComboBox" ? 40d : 180d),
                    (nameof(DesignControlModel.Background), "#FFFFFF"), (nameof(DesignControlModel.Foreground), "#0F172A"), (nameof(DesignControlModel.Padding), 4d)));
        }
    }
}
