using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.Metadata;

/// <summary>
/// Reviewed support policy on top of discovery. This list enables complete vertical
/// slices, not every discovered control/property. The same metadata/codec owns Inspector,
/// native creation, literal AXAML import and exported attributes.
/// </summary>
public static class StandardControlProperties
{
    public static readonly IReadOnlySet<string> ReadOnlyInspectorKeys = new HashSet<string>(StringComparer.Ordinal)
        { "Styles", "Resources", "Template", "DataTemplates", "DataContext", "ItemsSource", "ItemTemplate", "ContextMenu", "ContextFlyout", "Flyout", "SelectedItem", "Command", "CommandParameter", "StrokeDashArray", "Target", "Culture", "MaskProvider", "MaskCompleted", "MaskFull", "Inlines", "BlackoutDates" };
    public static readonly IReadOnlyDictionary<string, Type> PilotTypes = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Type>(
        new Dictionary<string, Type>(StringComparer.Ordinal)
        { ["Slider"] = typeof(Slider), ["ProgressBar"] = typeof(ProgressBar), ["ToggleSwitch"] = typeof(ToggleSwitch) });
    public static readonly IReadOnlyDictionary<string, Type> SupportedTypes = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Type>(
        PilotTypes.Concat(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["RadioButton"] = typeof(RadioButton), ["NumericUpDown"] = typeof(NumericUpDown), ["Calendar"] = typeof(Avalonia.Controls.Calendar),
            ["DatePicker"] = typeof(DatePicker), ["TimePicker"] = typeof(TimePicker), ["Separator"] = typeof(Separator),
            ["ScrollBar"] = typeof(ScrollBar), ["GridSplitter"] = typeof(GridSplitter), ["Rectangle"] = typeof(Rectangle),
            ["Ellipse"] = typeof(Ellipse), ["HyperlinkButton"] = typeof(HyperlinkButton), ["RepeatButton"] = typeof(RepeatButton),
            ["Viewbox"] = typeof(Viewbox), ["UniformGrid"] = typeof(UniformGrid), ["Expander"] = typeof(Expander), ["ScrollViewer"] = typeof(ScrollViewer),
            ["Line"] = typeof(Line), ["Polygon"] = typeof(Polygon), ["Polyline"] = typeof(Polyline), ["Path"] = typeof(Path),
            ["Label"] = typeof(Label), ["AccessText"] = typeof(AccessText), ["ToggleButton"] = typeof(ToggleButton),
            ["CalendarDatePicker"] = typeof(CalendarDatePicker), ["MaskedTextBox"] = typeof(MaskedTextBox), ["ButtonSpinner"] = typeof(ButtonSpinner)
        }).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    private static readonly string[] CommonSafeKeys = { "IsEnabled", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Padding", "BorderBrush", "BorderThickness",
        "CornerRadius", "HorizontalContentAlignment", "VerticalContentAlignment", "FontStyle", "FontStretch", "ClipToBounds" };
    private static readonly IReadOnlyDictionary<string, string[]> Keys = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Slider"] = new[] { "Minimum", "Maximum", "Value", "SmallChange", "LargeChange", "Orientation", "IsSnapToTickEnabled", "TickFrequency", "TickPlacement", "IsEnabled", "Padding" },
        ["ProgressBar"] = new[] { "Minimum", "Maximum", "Value", "IsIndeterminate", "Orientation", "ShowProgressText", "IsEnabled", "Padding" },
        ["ToggleSwitch"] = new[] { "IsChecked", "IsThreeState", "IsEnabled", "Padding", "CornerRadius" },
        ["RadioButton"] = new[] { "IsChecked", "GroupName" },
        ["NumericUpDown"] = new[] { "Minimum", "Maximum", "Value", "Increment", "FormatString", "AllowSpin", "ShowButtonSpinner", "IsReadOnly" },
        ["Calendar"] = new[] { "DisplayDate", "DisplayDateStart", "DisplayDateEnd", "SelectedDate", "FirstDayOfWeek", "IsTodayHighlighted", "DisplayMode" },
        ["DatePicker"] = new[] { "SelectedDate", "MinYear", "MaxYear", "YearVisible", "MonthVisible", "DayVisible", "YearFormat", "MonthFormat", "DayFormat" },
        ["TimePicker"] = new[] { "SelectedTime", "ClockIdentifier", "MinuteIncrement" },
        ["Separator"] = Array.Empty<string>(),
        ["ScrollBar"] = new[]{"Minimum","Maximum","Value","Orientation","ViewportSize","SmallChange","LargeChange"},
        ["GridSplitter"] = new[]{"ResizeDirection","ResizeBehavior","ShowsPreview","DragIncrement","KeyboardIncrement"},
        ["Rectangle"] = new[]{"Fill","Stroke","StrokeThickness","Stretch","RadiusX","RadiusY","StrokeDashOffset","StrokeLineCap","StrokeJoin"},
        ["Ellipse"] = new[]{"Fill","Stroke","StrokeThickness","Stretch","StrokeDashOffset","StrokeLineCap","StrokeJoin"},
        ["HyperlinkButton"] = new[]{"NavigateUri","IsVisited","ClickMode"},
        ["RepeatButton"] = new[]{"Delay","Interval","ClickMode"},
        ["Viewbox"] = new[]{"Stretch","StretchDirection"},
        ["UniformGrid"] = new[]{"Rows","Columns","FirstColumn"},
        ["Expander"] = new[]{"Header","IsExpanded","ExpandDirection"},
        ["ScrollViewer"] = new[]{"HorizontalScrollBarVisibility","VerticalScrollBarVisibility","AllowAutoHide"},
        ["Line"] = new[]{"StartPoint","EndPoint","Fill","Stroke","StrokeThickness","StrokeDashArray","StrokeDashOffset","StrokeLineCap","StrokeJoin","Stretch"},
        ["Polygon"] = new[]{"Points","Fill","Stroke","StrokeThickness","StrokeDashArray","Stretch","StrokeJoin"},
        ["Polyline"] = new[]{"Points","Fill","Stroke","StrokeThickness","StrokeDashArray","Stretch","StrokeJoin"},
        ["Path"] = new[]{"Data","Fill","Stroke","StrokeThickness","StrokeDashArray","Stretch","StrokeJoin"},
        ["Label"] = Array.Empty<string>(),
        ["AccessText"] = new[]{"ShowAccessKey","TextAlignment","TextWrapping","LetterSpacing","LineSpacing","MaxLines"},
        ["ToggleButton"] = new[]{"IsChecked","IsThreeState","ClickMode"},
        ["CalendarDatePicker"] = new[]{"SelectedDate","DisplayDate","DisplayDateStart","DisplayDateEnd","FirstDayOfWeek","IsTodayHighlighted","SelectedDateFormat","CustomDateFormatString"},
        ["MaskedTextBox"] = new[]{"Mask","AsciiOnly","HidePromptOnLeave","ResetOnPrompt","ResetOnSpace","Watermark","IsReadOnly","MaxLength","HorizontalContentAlignment","VerticalContentAlignment"},
        ["ButtonSpinner"] = new[]{"AllowSpin","ShowButtonSpinner","ButtonSpinnerLocation","ValidSpinDirection"}
    };
    // Cache only non-collectible, explicitly shipped types; never plugin Type/Assembly keys.
    private sealed record SupportEntry(IReadOnlyList<AvaloniaPropertyInfo> Properties, IReadOnlySet<string> NativeKeys);
    private static readonly Lazy<IReadOnlyDictionary<string, SupportEntry>> Supported = new(() =>
        SupportedTypes.ToDictionary(pair => pair.Key, pair => new SupportEntry(Keys[pair.Key].Concat(PilotTypes.ContainsKey(pair.Key) ? Array.Empty<string>() : CommonSafeKeys).Concat(typeof(Shape).IsAssignableFrom(pair.Value)?new[]{"StrokeDashArray"}:Array.Empty<string>()).Distinct(StringComparer.Ordinal)
            .Select(key => AvaloniaMetadataCatalog.Get(pair.Value).Properties.FirstOrDefault(p => p.Key == key))
            .Where(p => p is { ReadOnly: false, Property: not null }).Cast<AvaloniaPropertyInfo>().ToArray(),
            AvaloniaMetadataCatalog.Get(pair.Value).Properties.Where(p=>p.Property is not null).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal)), StringComparer.Ordinal));
    public static IReadOnlyList<AvaloniaPropertyInfo> PropertiesFor(string? type) =>
        type is not null && Supported.Value.TryGetValue(type, out var entry) ? entry.Properties : Array.Empty<AvaloniaPropertyInfo>();
    public static bool HasNativeProperty(string type, string key) => Supported.Value.TryGetValue(type,out var entry) && entry.NativeKeys.Contains(key);
    public static bool IsShape(string type) => SupportedTypes.TryGetValue(type,out var native) && typeof(Shape).IsAssignableFrom(native);
    public static AvaloniaPropertyInfo? Find(string? type, string key) => PropertiesFor(type).FirstOrDefault(p => p.Key == key);
    public static MetadataEditor EditorFor(AvaloniaPropertyInfo property) => ShapeLiteralCodec.Supports(property) || IsLiteralHeader(property) ? MetadataEditor.Text : property.OwnerType == typeof(TimePicker) && property.Key == "ClockIdentifier"
        ? MetadataEditor.Enumeration : property.Editor;
    public static string CategoryFor(AvaloniaPropertyInfo property) => property.Key is "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight"
        || property.Editor is MetadataEditor.Thickness or MetadataEditor.CornerRadius ? "Layout"
        : property.Editor is MetadataEditor.Color or MetadataEditor.Font || property.Key.StartsWith("Font", StringComparison.Ordinal) ? "Appearance" : "Behavior";
    public static DesignPropertyDescriptor Descriptor(AvaloniaPropertyInfo property) => new()
    {
        Key = property.Key, Title = property.Name, Category = CategoryFor(property),
        Description = $"Avalonia {property.OwnerType.Name}.{property.Name}. {Hint(property)}",
        Editor = EditorFor(property) switch
        {
            MetadataEditor.Boolean => PropertyEditorKind.Bool, MetadataEditor.Enumeration => PropertyEditorKind.Enum,
            MetadataEditor.Number => PropertyEditorKind.Number, MetadataEditor.Color => PropertyEditorKind.Color, _ => PropertyEditorKind.Text
        },
        DefaultValueJson = Encode(property.DefaultValue), IsBindable = property.Bindable,
        Options = Options(property).Select(text => new PropertyOption { Value = text, Title = text == MetadataValueCodec.NullText ? "Не задано (null)" : text }).ToArray()
    };
    public static string Hint(AvaloniaPropertyInfo property) => IsViewport(property) ? "Размер видимой области. NaN — нативный автоматический режим ScrollBar; конечное число должно быть неотрицательным." : property.Editor switch
    {
        _ when ShapeLiteralCodec.Supports(property) => property.Key=="Data"?"Проверяемая path mini-language Avalonia (M/L/C/Q/A/Z); до 16384 символов. Объекты Geometry, Bindings и Resources не преобразуются в строку.":property.Key=="Points"?"Координаты x,y через пробел: 0,0 100,0 50,80. До 1024 точек; конечные значения.":"Неотрицательные длины штрихов через пробел или запятую; пустое значение очищает список.",
        _ when IsLiteralHeader(property) => "Текстовый Header. Объектные значения, Bindings и Resources сохраняются отдельно и не заменяются текстом.",
        _ when property.OwnerType == typeof(RepeatButton) && property.Key is "Delay" or "Interval" => "Интервал в миллисекундах, не меньше нуля. Отрицательные значения недопустимы для нативного таймера.",
        MetadataEditor.Thickness => "Отступы в DIP: одно, два или четыре числа (left,top,right,bottom).",
        MetadataEditor.CornerRadius => "Радиусы углов в DIP; значение проверяется Avalonia.",
        MetadataEditor.GridLength => "Размер Grid: Auto, *, 2* или число DIP.",
        MetadataEditor.DateTime => "Дата ISO 8601: yyyy-MM-dd или дата/время с offset. Для nullable доступно (null).",
        MetadataEditor.TimeSpan => "Время/интервал: hh:mm:ss (например 14:30:00); (null) очищает nullable значение.",
        MetadataEditor.Number => "Число с десятичной точкой; для nullable доступно (null). MaxWidth/MaxHeight допускают Infinity.",
        _ when property.PropertyType == typeof(Uri) => "Абсолютный HTTP/HTTPS URI. Переход выполняется только при нажатии HyperlinkButton; (null) очищает ссылку.",
        _ when property.Nullable => "Допускается явное значение (null). Bindings редактируются отдельным механизмом.",
        _ => "Типизированное литеральное свойство; недопустимое значение не изменяет документ."
    };
    public static IReadOnlyList<string> Options(AvaloniaPropertyInfo property)
    {
        if (property.OwnerType == typeof(TimePicker) && property.Key == "ClockIdentifier") return new[] { "12HourClock", "24HourClock" };
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var options = type.IsEnum ? Enum.GetNames(type).ToList() : type == typeof(bool) ? new List<string> { "False", "True" } : new();
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) options.Insert(0, MetadataValueCodec.NullText);
        return options;
    }
    public static string Read(AvaloniaPropertyInfo property, string? json)
    {
        if (json is null) return property.DefaultText;
        using var value = JsonDocument.Parse(json);
        return value.RootElement.ValueKind switch
        {
            JsonValueKind.Null => MetadataValueCodec.NullText,
            JsonValueKind.True => "True", JsonValueKind.False => "False",
            JsonValueKind.String => value.RootElement.GetString()!,
            JsonValueKind.Number => value.RootElement.GetRawText(),
            _ => throw new FormatException("Complex JSON is not a scalar Avalonia property: " + property.Key)
        };
    }
    public static bool TryEncode(AvaloniaPropertyInfo property, string text, out string json)
    {
        json = "null";
        if (property.ReadOnly || !TryParseValue(property, text, out var value)
            || property.Property?.IsValidValue(value) == false) return false;
        // Avalonia accepts negative RepeatButton values at registration level,
        // but its DispatcherTimer throws when a press starts that interval.
        if (property.OwnerType == typeof(RepeatButton) && property.Key is "Delay" or "Interval" && value is int timing && timing < 0) return false;
        if (property.OwnerType == typeof(UniformGrid) && property.Key is "Rows" or "Columns" or "FirstColumn" && value is int count && count < 0) return false;
        if (property.OwnerType == typeof(TimePicker) && property.Key == "ClockIdentifier" && !Options(property).Contains(text, StringComparer.Ordinal)) return false;
        try
        {
            if (property.OwnerType == typeof(NumericUpDown) && property.Key == "FormatString" && value is string numberFormat) _ = 0m.ToString(numberFormat, CultureInfo.InvariantCulture);
            if (property.OwnerType == typeof(DatePicker) && property.Key is "YearFormat" or "MonthFormat" or "DayFormat" && value is string dateFormat) _ = DateTime.UnixEpoch.ToString(dateFormat, CultureInfo.InvariantCulture);
            if (property.OwnerType == typeof(CalendarDatePicker) && property.Key == "CustomDateFormatString" && value is string calendarFormat) _ = DateTime.UnixEpoch.ToString(calendarFormat, CultureInfo.InvariantCulture);
            if (property.OwnerType == typeof(MaskedTextBox) && property.Key == "Mask" && value is string { Length: > 0 } mask)
            {
                if (mask.Length > 1024) return false;
                _ = new System.ComponentModel.MaskedTextProvider(mask, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception error) when (error is FormatException or ArgumentException) { return false; }
        json = ShapeLiteralCodec.Supports(property) && value is not null ? JsonSerializer.Serialize(text.Trim()) : Encode(value); return true;
    }
    private static string Encode(object? value) => value switch
    {
        null => "null", bool flag => JsonSerializer.Serialize(flag), double number when double.IsFinite(number) => JsonSerializer.Serialize(number),
        int number => JsonSerializer.Serialize(number), long number => JsonSerializer.Serialize(number), decimal number => JsonSerializer.Serialize(number),
        _ => JsonSerializer.Serialize(MetadataValueCodec.Display(value))
    };
    public static bool TryParseValue(AvaloniaPropertyInfo property, string text, out object? value)
    {
        if (ShapeLiteralCodec.Supports(property)) return ShapeLiteralCodec.TryParse(property,text,out value);
        if (IsLiteralHeader(property)) { value=text is MetadataValueCodec.NullText or "{x:Null}"?null:text;return !text.TrimStart().StartsWith('{')||text=="{x:Null}"; }
        if (IsViewport(property) && text == "NaN") { value=double.NaN; return true; }
        if (property.Key is "MaxWidth" or "MaxHeight" && text == "Infinity") { value = double.PositiveInfinity; return true; }
        return MetadataValueCodec.TryParse(property.PropertyType, text, out value);
    }
    private static bool IsViewport(AvaloniaPropertyInfo property) => property.OwnerType == typeof(ScrollBar) && property.Key == "ViewportSize";
    private static bool IsLiteralHeader(AvaloniaPropertyInfo property) => property.Key=="Header" && property.OwnerType==typeof(HeaderedContentControl);
    public static bool IsContainer(string type) => type is "Viewbox" or "UniformGrid" or "Expander" or "ScrollViewer" or "ButtonSpinner";
    public static bool IsSingleChild(string type) => IsContainer(type) && type != "UniformGrid";
    public static string ContainerMode(string type) => type=="UniformGrid"?DesignerLayoutModes.Uniform:DesignerLayoutModes.Content;
    public static void AttachChildren(Control host, System.Collections.Generic.IReadOnlyList<Control> children) => AttachChildren(host,children,false);
    public static void AttachChildren(Control host, System.Collections.Generic.IReadOnlyList<Control> children,bool designerEmptyTarget)
    {
        if (host is UniformGrid panel) { foreach(var child in children)panel.Children.Add(child);return; }
        if (children.Count>1)throw new InvalidOperationException("Контейнер "+host.Name+" принимает один Content/Child. Поместите несколько controls во вложенный Layout.");
        var content=children.SingleOrDefault();
        // An empty Viewbox legitimately arranges to zero in Avalonia. This is
        // editor interaction chrome only; no model child or runtime/export node.
        if(content is null && designerEmptyTarget && host is Viewbox)
            content=new Border{MinWidth=48,MinHeight=32,Background=Avalonia.Media.Brushes.Transparent,IsHitTestVisible=false};
        if(host is Viewbox viewbox)viewbox.Child=content;
        else if(host is ContentControl control)control.Content=content;
    }
    public static bool HasLiteralContent(string type) => type is "ToggleSwitch" or "RadioButton" or "HyperlinkButton" or "RepeatButton" or "Label" or "ToggleButton";
    public static string? TextPropertyName(string type) => HasLiteralContent(type) ? "Content" : type is "AccessText" or "MaskedTextBox" ? "Text" : null;
    public static string Attributes(string type, IReadOnlyDictionary<string, string> values, Func<string, string> escape, bool placementOwnsConstraints = false)
    {
        var result = new System.Text.StringBuilder();
        foreach (var property in PropertiesFor(type))
        {
            if (placementOwnsConstraints && LayoutPlacementProperties.SizeConstraintKeys.Contains(property.Key)) continue;
            if (!values.TryGetValue(property.Key, out var json)) continue;
            var text = Read(property, json);
            if (!TryEncode(property, text, out _)) throw new FormatException("Invalid Avalonia property: " + property.Key);
            result.Append(' ').Append(property.Key).Append("=\"").Append(text == MetadataValueCodec.NullText ? "{x:Null}" : escape(text)).Append('"');
        }
        return result.ToString();
    }
    public static Control Create(IDesignControlNode node, string? sourceType = null, bool applyCommon = true, Func<string, bool>? commonFilter = null)
    {
        var type = sourceType ?? node.TypeKey;
        if (!SupportedTypes.TryGetValue(type, out var clrType)) throw new NotSupportedException(type);
        var control = (Control)Activator.CreateInstance(clrType)!;
        control.Name = node.Name;
        // Placement adapters own Margin. In legacy Canvas the native view lives at
        // (0,0) inside a positioned wrapper; a theme margin must not add another offset.
        // Native/source layout adapters subsequently apply the authored model margin.
        control.Margin = default;
        // Old model fields remain authoritative; placement belongs to existing layout adapters.
        var common = new[] { "Width", "Height", "Opacity", "IsVisible", "Background", "Foreground", "FontFamily", "FontSize", "FontWeight" };
        foreach (var name in applyCommon ? common : Array.Empty<string>())
        {
            if (commonFilter is not null && !commonFilter(name)) continue;
            if (!node.BuiltInProperties.TryGetValue(name, out var value) || value is null) continue;
            var property = AvaloniaMetadataCatalog.Get(clrType).Properties.FirstOrDefault(p => p.Key == name && !p.ReadOnly && p.Property is not null);
            if (property is not null && MetadataValueCodec.TryParse(property.PropertyType, MetadataValueCodec.Display(value), out var parsed))
                control.SetValue(property.Property!, parsed);
        }
        if(type=="Expander"&&control is Expander expander&&node.CustomProperties.TryGetValue("Content",out var literalContent))
            expander.Content=JsonSerializer.Deserialize<string>(literalContent);
        foreach (var property in PropertiesFor(type))
        {
            if (!node.CustomProperties.TryGetValue(property.Key, out var json)) continue;
            var text = Read(property, json);
            if (!TryEncode(property, text, out _) || !TryParseValue(property, text, out var value))
                throw new FormatException("Invalid Avalonia property: " + property.Key);
            control.SetValue(property.Property!, value);
        }
        // Mask/format configuration must exist before applying the model's text.
        if (TextPropertyName(type) is { } textKey)
        {
            var textProperty = AvaloniaMetadataCatalog.Get(clrType).Properties.First(p => p.Key == textKey && p.Property is not null);
            control.SetValue(textProperty.Property!, node.GetString("Text", type));
        }
        return control;
    }
    public static bool TryWriteModel(DesignControlModel model, AvaloniaPropertyInfo property, string value)
    {
        if (!TryEncode(property, value, out var json) || !IsWithinModelRange(model, property, value)) return false;
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == property.Key);
        if (property.Key is "MaxWidth" or "MaxHeight" && value == "Infinity")
        {
            if (stored is not null) model.CustomProperties.Remove(stored);
            return true;
        }
        if (stored is null) model.CustomProperties.Add(stored = new DesignPropertyValueModel { Key = property.Key });
        stored.ValueJson = json; return true;
    }
    public static bool IsWithinModelRange(DesignControlModel model, AvaloniaPropertyInfo property, string text)
    {
        if (model.Type is not ("Slider" or "ProgressBar" or "NumericUpDown" or "ScrollBar") || property.Key is not ("Value" or "Minimum" or "Maximum")) return true;
        if (!TryParseValue(property, text, out var candidate)) return false;
        if (candidate is null) return true;
        object? Bound(string key)
        {
            var p = Find(model.Type, key);
            if (p is null) return null;
            try { return TryParseValue(p, Read(p, model.CustomProperties.FirstOrDefault(v => v.Key == key)?.ValueJson), out var result) ? result : null; }
            catch (Exception error) when (error is JsonException or FormatException) { return null; }
        }
        if (candidate is not IComparable comparable) return true;
        var minimum = Bound("Minimum"); var maximum = Bound("Maximum");
        return (property.Key == "Minimum" || minimum is null || comparable.CompareTo(minimum) >= 0)
            && (property.Key == "Maximum" || maximum is null || comparable.CompareTo(maximum) <= 0);
    }
}
