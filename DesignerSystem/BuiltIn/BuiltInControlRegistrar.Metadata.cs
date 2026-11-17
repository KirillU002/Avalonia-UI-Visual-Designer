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
            var common = CreateCommonProperties(includeText: StandardControlProperties.TextPropertyName(type) is not null, includeBackground: StandardControlProperties.HasNativeProperty(type,"Background"), includeForeground: StandardControlProperties.HasNativeProperty(type,"Foreground"), includeFont: StandardControlProperties.HasNativeProperty(type,"FontFamily"));
            var properties = common.Concat(StandardControlProperties.PropertiesFor(type).Select(StandardControlProperties.Descriptor)).ToArray();
            var category=shape||type=="PathIcon"?"Фигуры":type is "Viewbox" or "UniformGrid" or "GridSplitter"?"Макет":StandardControlProperties.IsContainer(type)?"Контейнеры":type is "ScrollBar" or "RepeatButton" or "ToggleButton" or "CalendarDatePicker" or "MaskedTextBox"?"Ввод":type=="HyperlinkButton"?"Навигация":"Стандартные";
            var description=type switch {"SelectableTextBlock"=>"Текст с нативным выделением и копированием; Text и оформление сохраняются раздельно.","PathIcon"=>"Векторная иконка с проверяемым литералом Data и Foreground.","Arc"=>"Дуга: углы StartAngle/SweepAngle в градусах и нативная обводка.","Sector"=>"Сектор эллипса: углы в градусах, заливка и обводка.","ScrollBar"=>"Полоса прокрутки: диапазон, значение и размер видимой области.","GridSplitter"=>"Разделитель строк или столбцов Grid; в Designer перемещается как компонент.","Rectangle"=>"Прямоугольник с заливкой, обводкой и радиусами углов.","Ellipse"=>"Эллипс с заливкой и обводкой.","HyperlinkButton"=>"Кнопка-ссылка с проверяемым HTTP/HTTPS URI.","RepeatButton"=>"Кнопка, повторяющая Click при удержании; Delay и Interval в миллисекундах.","Viewbox"=>"Масштабирует один Child. Для нескольких компонентов используйте вложенный Layout.","UniformGrid"=>"Равные ячейки; порядок дочерних элементов, Rows, Columns и FirstColumn.","Expander"=>"Header и один Content; раскрытие и направление через metadata.","ScrollViewer"=>"Прокручиваемый Content; один дочерний компонент или вложенный Layout.","Line"=>"Линия: типизированные StartPoint/EndPoint и обводка.","Polygon"=>"Замкнутый многоугольник; проверяемый список координат Points.","Polyline"=>"Ломаная; проверяемые Points и StrokeDashArray.","Path"=>"Геометрия Avalonia path mini-language с проверкой и сохранением исходного литерала.",_=>"Стандартный Avalonia control; типизированные свойства из metadata."};
            description=type switch {"Label"=>"Текстовая подпись с нативным Content; сложный Target доступен только для просмотра.","AccessText"=>"Текст с клавишей доступа: знак подчёркивания перед символом.","ToggleButton"=>"Кнопка-переключатель: IsChecked, nullable и трёхпозиционный режим.","CalendarDatePicker"=>"Выбор даты с нативным календарём, диапазоном и форматированием.","MaskedTextBox"=>"Ввод по проверяемой стандартной маске .NET; Mask и Text сохраняются раздельно.","ButtonSpinner"=>"Кнопки Increase/Decrease и один Content; изменение содержимого выполняет обработчик Spin приложения.",_=>description};
            registry.RegisterControl(new DelegatingControlDescriptor(type, type, category, description,
                StandardControlProperties.IsContainer(type), StandardControlProperties.IsContainer(type), StandardControlProperties.IsContainer(type)?StandardControlProperties.ContainerMode(type):DesignerLayoutModes.Absolute, properties,
                _ => {
                    var definition=CreateDefinition(type, (nameof(DesignControlModel.Width), type=="PathIcon"?32d:type == "Calendar" ? 360d : shape?140d:StandardControlProperties.IsContainer(type)?320d:type=="GridSplitter"?8d:220d), (nameof(DesignControlModel.Height), type=="PathIcon"?32d:type == "Calendar" ? 300d : shape?90d:StandardControlProperties.IsContainer(type)?200d:type=="GridSplitter"?140d:type == "Separator" ? 4d:type=="ScrollBar"?20d:40d),
                    (nameof(DesignControlModel.Text), type == "RadioButton" ? "Вариант" : type=="HyperlinkButton"?"Открыть ссылку":type=="RepeatButton"?"Повторить":"Переключатель"), (nameof(DesignControlModel.Foreground), "#0F172A"),
                    (nameof(DesignControlModel.Background), "Transparent"), (nameof(DesignControlModel.FontFamily), "Inter"),
                    (nameof(DesignControlModel.FontSize), 14d));
                    if(shape){definition.CustomProperties["Fill"]=JsonSerializer.Serialize("#DBEAFE");definition.CustomProperties["Stroke"]=JsonSerializer.Serialize("#1D4ED8");definition.CustomProperties["StrokeThickness"]="1";}
                    if(type=="ScrollBar"){definition.CustomProperties["Orientation"]=JsonSerializer.Serialize("Horizontal");definition.CustomProperties["ViewportSize"]="10";}
                    if(type=="GridSplitter"){definition.CustomProperties["ResizeDirection"]=JsonSerializer.Serialize("Columns");definition.CustomProperties["ResizeBehavior"]=JsonSerializer.Serialize("PreviousAndNext");}
                    if(type=="Expander"){definition.CustomProperties["Header"]=JsonSerializer.Serialize("Раздел");definition.CustomProperties["IsExpanded"]="true";}
                    if(type=="UniformGrid"){definition.CustomProperties["Rows"]="2";definition.CustomProperties["Columns"]="2";}
                    if(type=="Line"){definition.CustomProperties["StartPoint"]=JsonSerializer.Serialize("0,10");definition.CustomProperties["EndPoint"]=JsonSerializer.Serialize("140,70");}
                    if(type is "Polygon" or "Polyline")definition.CustomProperties["Points"]=JsonSerializer.Serialize("0,0 140,0 70,80");
                    if(type is "Path" or "PathIcon")definition.CustomProperties["Data"]=JsonSerializer.Serialize("M 0,0 L 140,0 L 70,80 Z");
                    if(type is "Arc" or "Sector"){definition.CustomProperties["StartAngle"]="15";definition.CustomProperties["SweepAngle"]="240";}
                    if(type=="SelectableTextBlock")definition.BuiltInProperties[nameof(DesignControlModel.Text)]="Выделяемый текст";
                    if(type is "Label" or "AccessText")definition.BuiltInProperties[nameof(DesignControlModel.Text)] = "_Имя";
                    if(type=="ToggleButton")definition.BuiltInProperties[nameof(DesignControlModel.Text)] = "Переключить";
                    if(type=="MaskedTextBox"){definition.CustomProperties["Mask"]=JsonSerializer.Serialize("000-000");definition.BuiltInProperties[nameof(DesignControlModel.Text)]="123456";}
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
