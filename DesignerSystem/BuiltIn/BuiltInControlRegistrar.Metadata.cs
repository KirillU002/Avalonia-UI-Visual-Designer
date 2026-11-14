using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.DesignerSystem.Descriptors;
using System.Linq;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static partial class BuiltInControlRegistrar
{
    private static void RegisterMetadataControls(IDesignerRegistry registry)
    {
        foreach (var pair in StandardControlProperties.PilotTypes)
        {
            var type = pair.Key;
            var common = CreateCommonProperties(includeText: type == "ToggleSwitch", includeBackground: true, includeForeground: true, includeFont: true);
            var properties = common.Concat(StandardControlProperties.PropertiesFor(type).Select(StandardControlProperties.Descriptor)).ToArray();
            registry.RegisterControl(new DelegatingControlDescriptor(type, type, "Стандартные", "Стандартный Avalonia control; типизированные свойства из metadata.",
                false, false, DesignerLayoutModes.Absolute, properties,
                _ => CreateDefinition(type, (nameof(DesignControlModel.Width), 220d), (nameof(DesignControlModel.Height), 40d),
                    (nameof(DesignControlModel.Text), "Переключатель"), (nameof(DesignControlModel.Foreground), "#0F172A"),
                    (nameof(DesignControlModel.Background), "Transparent"), (nameof(DesignControlModel.FontFamily), "Inter"),
                    (nameof(DesignControlModel.FontSize), 14d)),
                (node, context) =>
                {
                    var native = StandardControlProperties.Create(node);
                    if (context.Mode == DesignerPreviewMode.Designer) native.IsHitTestVisible = false;
                    return native;
                }, AppendXamlFromBridge));
        }
    }
}
