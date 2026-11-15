using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.DesignerSystem.Descriptors;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static partial class BuiltInControlRegistrar
{
    private static void RegisterMetadataControls(IDesignerRegistry registry)
    {
        foreach (var pair in StandardControlProperties.SupportedTypes)
        {
            var type = pair.Key;
            var shape = StandardControlProperties.IsShape(type);
            var common = CreateCommonProperties(includeText: StandardControlProperties.HasLiteralContent(type), includeBackground: !shape, includeForeground: !shape, includeFont: !shape);
            var properties = common.Concat(StandardControlProperties.PropertiesFor(type).Select(StandardControlProperties.Descriptor)).ToArray();
            var category=shape?"Фигуры":type=="GridSplitter"?"Макет":type is "ScrollBar" or "RepeatButton"?"Ввод":type=="HyperlinkButton"?"Навигация":"Стандартные";
            var description=type switch {"ScrollBar"=>"Полоса прокрутки: диапазон, значение и размер видимой области.","GridSplitter"=>"Разделитель строк или столбцов Grid; в Designer перемещается как компонент.","Rectangle"=>"Прямоугольник с заливкой, обводкой и радиусами углов.","Ellipse"=>"Эллипс с заливкой и обводкой.","HyperlinkButton"=>"Кнопка-ссылка с проверяемым HTTP/HTTPS URI.","RepeatButton"=>"Кнопка, повторяющая Click при удержании; Delay и Interval в миллисекундах.",_=>"Стандартный Avalonia control; типизированные свойства из metadata."};
            registry.RegisterControl(new DelegatingControlDescriptor(type, type, category, description,
                false, false, DesignerLayoutModes.Absolute, properties,
                _ => {
                    var definition=CreateDefinition(type, (nameof(DesignControlModel.Width), type == "Calendar" ? 360d : shape?140d:type=="GridSplitter"?8d:220d), (nameof(DesignControlModel.Height), type == "Calendar" ? 300d : shape?90d:type=="GridSplitter"?140d:type == "Separator" ? 4d:type=="ScrollBar"?20d:40d),
                    (nameof(DesignControlModel.Text), type == "RadioButton" ? "Вариант" : type=="HyperlinkButton"?"Открыть ссылку":type=="RepeatButton"?"Повторить":"Переключатель"), (nameof(DesignControlModel.Foreground), "#0F172A"),
                    (nameof(DesignControlModel.Background), "Transparent"), (nameof(DesignControlModel.FontFamily), "Inter"),
                    (nameof(DesignControlModel.FontSize), 14d));
                    if(shape){definition.CustomProperties["Fill"]=JsonSerializer.Serialize("#DBEAFE");definition.CustomProperties["Stroke"]=JsonSerializer.Serialize("#1D4ED8");definition.CustomProperties["StrokeThickness"]="1";}
                    if(type=="ScrollBar"){definition.CustomProperties["Orientation"]=JsonSerializer.Serialize("Horizontal");definition.CustomProperties["ViewportSize"]="10";}
                    if(type=="GridSplitter"){definition.CustomProperties["ResizeDirection"]=JsonSerializer.Serialize("Columns");definition.CustomProperties["ResizeBehavior"]=JsonSerializer.Serialize("PreviousAndNext");}
                    return definition;
                },
                (node, context) =>
                {
                    var native = StandardControlProperties.Create(node);
                    if (context.Mode == DesignerPreviewMode.Designer) native.IsHitTestVisible = false;
                    return native;
                }, AppendXamlFromBridge));
        }
    }
}
