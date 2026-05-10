using Avalonia.Controls;
using FormDesigner.DesignerSystem.Descriptors;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.BuiltIn;

internal interface IBuiltInPreviewBridge
{
    Control BuildPreview(string typeKey, IDesignControlNode control, IPreviewContext context);
}

internal interface IBuiltInXamlBridge
{
    void AppendXaml(string typeKey, IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context);
}

internal static class BuiltInControlRegistrar
{
    public static void Register(IDesignerRegistry registry)
    {
        RegisterSimple(registry, DesignerControlTypes.Button, "Button", "Ввод", "Кнопка для запуска действий.", false, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeText: true, includeBackground: true, includeBorder: true, includeFont: true, includePadding: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.Button, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.Button,
                    (nameof(DesignControlModel.Text), "Кнопка"),
                    (nameof(DesignControlModel.Width), 160d),
                    (nameof(DesignControlModel.Height), 40d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#2563EB"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#FFFFFF"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#1D4ED8"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.CornerRadius), 8d),
                    (nameof(DesignControlModel.FontSize), 14d),
                    (nameof(DesignControlModel.FontWeight), "SemiBold"),
                    (nameof(DesignControlModel.Padding), 10d));
            });

        RegisterSimple(registry, DesignerControlTypes.TextBox, "TextBox", "Ввод", "Однострочное поле для ввода текста.", false, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeText: true, includePlaceholder: true, includeBackground: true, includeBorder: true, includeFont: true, includePadding: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.TextBox, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.TextBox,
                    (nameof(DesignControlModel.PlaceholderText), "Введите текст"),
                    (nameof(DesignControlModel.Width), 220d),
                    (nameof(DesignControlModel.Height), 40d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#FFFFFF"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#94A3B8"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.CornerRadius), 8d),
                    (nameof(DesignControlModel.FontSize), 14d),
                    (nameof(DesignControlModel.Padding), 10d));
            });

        RegisterSimple(registry, DesignerControlTypes.TextBlock, "TextBlock", "Контент", "Статическая текстовая подпись.", false, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeText: true, includeForeground: true, includeFont: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.TextBlock, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.TextBlock,
                    (nameof(DesignControlModel.Text), "Текст"),
                    (nameof(DesignControlModel.Width), 200d),
                    (nameof(DesignControlModel.Height), 32d),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.FontSize), 18d),
                    (nameof(DesignControlModel.FontWeight), "SemiBold"));
            });

        RegisterSimple(registry, DesignerControlTypes.CheckBox, "CheckBox", "Ввод", "Переключатель логического значения.", false, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeText: true, includeForeground: true, includeFont: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.CheckBox, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.CheckBox,
                    (nameof(DesignControlModel.Text), "Флажок"),
                    (nameof(DesignControlModel.Width), 180d),
                    (nameof(DesignControlModel.Height), 32d),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.FontSize), 14d));
            });

        RegisterSimple(registry, DesignerControlTypes.Border, "Border", "Макет", "Контейнер для дочерних элементов.", true, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeText: true, includeBackground: true, includeBorder: true, includeForeground: true, includeFont: true, includePadding: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.Border, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.Border,
                    (nameof(DesignControlModel.Text), "Контейнер"),
                    (nameof(DesignControlModel.Width), 280d),
                    (nameof(DesignControlModel.Height), 180d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#F8FAFC"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#CBD5E1"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.CornerRadius), 16d),
                    (nameof(DesignControlModel.Padding), 12d),
                    (nameof(DesignControlModel.FontSize), 16d),
                    (nameof(DesignControlModel.FontWeight), "SemiBold"));
            });

        RegisterSimple(registry, DesignerControlTypes.Image, "Image", "Медиа", "Место для картинки или иконки.", false, DesignerLayoutModes.Absolute,
            CreateCommonProperties(includeImage: true, includeBackground: true, includeBorder: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.Image, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.Image,
                    (nameof(DesignControlModel.Text), "Изображение"),
                    (nameof(DesignControlModel.Width), 220d),
                    (nameof(DesignControlModel.Height), 180d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#F8FAFC"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#CBD5E1"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.CornerRadius), 12d),
                    (nameof(DesignControlModel.ImageSource), "avares://FormDesigner/Assets/avalonia-logo.ico"),
                    (nameof(DesignControlModel.Stretch), "Uniform"));
            });

        RegisterSimple(registry, DesignerControlTypes.Group, "Group", "Структура", "Служебный контейнер для группировки элементов.", true, DesignerLayoutModes.Absolute,
            CreateCommonProperties(),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.Group, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.Group,
                    (nameof(DesignControlModel.Width), 240d),
                    (nameof(DesignControlModel.Height), 160d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "Transparent"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#94A3B8"),
                    (nameof(DesignControlModel.BorderThickness), 0d),
                    (nameof(DesignControlModel.CornerRadius), 0d),
                    (nameof(DesignControlModel.Padding), 0d));
            });

        RegisterSimple(registry, DesignerControlTypes.StackLayout, "Stack Layout", "Макет", "Автоматически выстраивает дочерние элементы в стопку.", true, DesignerLayoutModes.Stack,
            CreateCommonProperties(includeBackground: true, includeBorder: true, includePadding: true, includeLayout: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.StackLayout, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.StackLayout,
                    (nameof(DesignControlModel.Width), 320d),
                    (nameof(DesignControlModel.Height), 220d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#F8FAFC"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#CBD5E1"),
                    (nameof(DesignControlModel.BorderThickness), 0d),
                    (nameof(DesignControlModel.CornerRadius), 0d),
                    (nameof(DesignControlModel.Padding), 12d),
                    (nameof(DesignControlModel.LayoutOrientation), DesignerLayoutModes.Vertical),
                    (nameof(DesignControlModel.LayoutSpacing), 12d));
            });

        RegisterSimple(registry, DesignerControlTypes.LayoutGrid, "Grid Layout", "Макет", "Контейнер с авто-раскладкой по ячейкам.", true, DesignerLayoutModes.Grid,
            CreateCommonProperties(includeBackground: true, includeBorder: true, includePadding: true, includeGrid: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.LayoutGrid, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.LayoutGrid,
                    (nameof(DesignControlModel.Text), "Grid Layout"),
                    (nameof(DesignControlModel.Width), 360d),
                    (nameof(DesignControlModel.Height), 240d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#FFFFFF"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#94A3B8"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.Padding), 12d),
                    (nameof(DesignControlModel.LayoutSpacing), 10d),
                    (nameof(DesignControlModel.Columns), 3),
                    (nameof(DesignControlModel.Rows), 3),
                    (nameof(DesignControlModel.ShowGridLines), true));
            });

        RegisterSimple(registry, DesignerControlTypes.FlexLayout, "Flex Layout", "Макет", "Контейнер с переносом элементов по строкам или колонкам.", true, DesignerLayoutModes.Flex,
            CreateCommonProperties(includeBackground: true, includeBorder: true, includePadding: true, includeLayout: true),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.FlexLayout, context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.FlexLayout,
                    (nameof(DesignControlModel.Width), 360d),
                    (nameof(DesignControlModel.Height), 220d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#F8FAFC"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#CBD5E1"),
                    (nameof(DesignControlModel.BorderThickness), 0d),
                    (nameof(DesignControlModel.CornerRadius), 0d),
                    (nameof(DesignControlModel.Padding), 12d),
                    (nameof(DesignControlModel.LayoutOrientation), DesignerLayoutModes.Horizontal),
                    (nameof(DesignControlModel.LayoutSpacing), 12d));
            });

        RegisterSimple(registry, DesignerControlTypes.DataGrid, "DataGrid", "Данные", "Таблица с привязкой к BindingSource.", false, DesignerLayoutModes.Absolute,
            CreateDataGridProperties(),
            context =>
            {
                var themeDefaults = DesignerThemeCatalog.GetControlDefaults(DesignerControlTypes.DataGrid, context.ActiveTheme);
                var palette = DesignerThemeCatalog.Get(context.ActiveTheme);
                return CreateDefinition(DesignerControlTypes.DataGrid,
                    (nameof(DesignControlModel.Text), "DataGrid"),
                    (nameof(DesignControlModel.Width), 540d),
                    (nameof(DesignControlModel.Height), 260d),
                    (nameof(DesignControlModel.Background), themeDefaults.Background ?? "#FFFFFF"),
                    (nameof(DesignControlModel.Foreground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.BorderBrush), themeDefaults.BorderBrush ?? "#94A3B8"),
                    (nameof(DesignControlModel.BorderThickness), 1d),
                    (nameof(DesignControlModel.FontSize), 13d),
                    (nameof(DesignControlModel.AutoGenerateColumns), false),
                    (nameof(DesignControlModel.BindingSourceId), context.BindingSources.FirstOrDefault()?.Id ?? string.Empty),
                    (nameof(DesignControlModel.DataGridRowBackground), palette.DataGridRowBackground),
                    (nameof(DesignControlModel.DataGridAlternateRowBackground), palette.DataGridAlternateRowBackground),
                    (nameof(DesignControlModel.DataGridTextAlignment), DesignControlModel.DataGridTextAlignmentLeft),
                    (nameof(DesignControlModel.DataGridGlowColor), palette.AccentStrongBrush),
                    (nameof(DesignControlModel.DataGridHeaderBackground), themeDefaults.Background ?? palette.DataGridHeaderBackground),
                    (nameof(DesignControlModel.DataGridHeaderForeground), themeDefaults.Foreground ?? palette.DataGridHeaderForeground),
                    (nameof(DesignControlModel.DataGridRowForeground), themeDefaults.Foreground ?? "#0F172A"),
                    (nameof(DesignControlModel.DataGridHoverRowBackground), "#EFF6FF"),
                    (nameof(DesignControlModel.DataGridSelectedRowBackground), "#DBEAFE"),
                    (nameof(DesignControlModel.DataGridSelectedRowForeground), "#0F172A"),
                    (nameof(DesignControlModel.DataGridGridLineBrush), themeDefaults.BorderBrush ?? "#D7E2EE"),
                    (nameof(DesignControlModel.DataGridOuterBorderBrush), palette.AccentStrongBrush),
                    (nameof(DesignControlModel.DataGridHeaderFontSize), 13d),
                    (nameof(DesignControlModel.DataGridHeaderFontWeight), "SemiBold"),
                    (nameof(DesignControlModel.DataGridRowFontSize), 13d),
                    (nameof(DesignControlModel.DataGridRowFontWeight), "Normal"),
                    (nameof(DesignControlModel.DataGridHeaderHeight), 46d),
                    (nameof(DesignControlModel.DataGridRowHeight), 36d),
                    (nameof(DesignControlModel.DataGridCellPadding), 14d),
                    (nameof(DesignControlModel.DataGridShowHeader), true),
                    (nameof(DesignControlModel.DataGridShowRowLines), true),
                    (nameof(DesignControlModel.DataGridShowColumnLines), true),
                    (nameof(DesignControlModel.DataGridShowAlternatingRows), true),
                    (nameof(DesignControlModel.ShowFilterRow), true),
                    (nameof(DesignControlModel.FilterMode), DesignControlModel.DataGridFilterModeContains),
                    (nameof(DesignControlModel.ShowGroupPanel), true),
                    (nameof(DesignControlModel.AllowGrouping), true));
            });
    }

    private static void RegisterSimple(
        IDesignerRegistry registry,
        string typeKey,
        string title,
        string category,
        string description,
        bool canHostChildren,
        string childLayoutMode,
        IReadOnlyList<DesignPropertyDescriptor> properties,
        System.Func<IDescriptorContext, DesignerControlDefinition> defaultFactory)
    {
        registry.RegisterControl(new DelegatingControlDescriptor(
            typeKey,
            title,
            category,
            description,
            isContainer: canHostChildren,
            canHostChildren: canHostChildren,
            childLayoutMode: childLayoutMode,
            properties: properties,
            defaultFactory: defaultFactory,
            previewBuilder: BuildPreviewFromBridge,
            xamlBuilder: AppendXamlFromBridge));
    }

    private static Control BuildPreviewFromBridge(IDesignControlNode control, IPreviewContext context)
    {
        var bridge = context.Services.GetService(typeof(IBuiltInPreviewBridge)) as IBuiltInPreviewBridge;
        return bridge is null
            ? new TextBlock { Text = control.TypeKey }
            : bridge.BuildPreview(control.TypeKey, control, context);
    }

    private static void AppendXamlFromBridge(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        var bridge = context.Services.GetService(typeof(IBuiltInXamlBridge)) as IBuiltInXamlBridge;
        if (bridge is null)
        {
            writer.WriteLine(indentLevel, $"<!-- Missing XAML bridge for {control.TypeKey} -->");
            return;
        }

        bridge.AppendXaml(control.TypeKey, writer, control, indentLevel, context);
    }

    private static DesignerControlDefinition CreateDefinition(string typeKey, params (string Key, object? Value)[] values)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = typeKey,
            DescriptorId = typeKey
        };

        foreach (var (key, value) in values)
            definition.BuiltInProperties[key] = value;

        return definition;
    }

    private static IReadOnlyList<DesignPropertyDescriptor> CreateCommonProperties(
        bool includeText = false,
        bool includePlaceholder = false,
        bool includeImage = false,
        bool includeBackground = false,
        bool includeForeground = false,
        bool includeBorder = false,
        bool includeFont = false,
        bool includePadding = false,
        bool includeLayout = false,
        bool includeGrid = false)
    {
        var result = new List<DesignPropertyDescriptor>
        {
            new() { Key = nameof(DesignControlModel.Name), Title = "Имя", Category = "General", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.Name) },
            new() { Key = nameof(DesignControlModel.Width), Title = "Ширина", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Width) },
            new() { Key = nameof(DesignControlModel.Height), Title = "Высота", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Height) },
            new() { Key = nameof(DesignControlModel.X), Title = "X", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.X) },
            new() { Key = nameof(DesignControlModel.Y), Title = "Y", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Y) },
            new() { Key = nameof(DesignControlModel.Opacity), Title = "Прозрачность", Category = "Appearance", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Opacity) },
            new() { Key = nameof(DesignControlModel.IsVisible), Title = "Виден", Category = "Behavior", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.IsVisible) }
        };

        if (includeText)
            result.Add(new() { Key = nameof(DesignControlModel.Text), Title = "Текст", Category = "Content", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.Text), IsBindable = true });

        if (includePlaceholder)
            result.Add(new() { Key = nameof(DesignControlModel.PlaceholderText), Title = "Placeholder", Category = "Content", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.PlaceholderText) });

        if (includeImage)
            result.Add(new() { Key = nameof(DesignControlModel.ImageSource), Title = "Источник изображения", Category = "Content", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.ImageSource) });

        if (includeBackground)
            result.Add(new() { Key = nameof(DesignControlModel.Background), Title = "Фон", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.Background) });

        if (includeForeground)
            result.Add(new() { Key = nameof(DesignControlModel.Foreground), Title = "Цвет текста", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.Foreground) });

        if (includeBorder)
        {
            result.Add(new() { Key = nameof(DesignControlModel.BorderBrush), Title = "Цвет границы", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.BorderBrush) });
            result.Add(new() { Key = nameof(DesignControlModel.BorderThickness), Title = "Толщина границы", Category = "Appearance", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.BorderThickness) });
            result.Add(new() { Key = nameof(DesignControlModel.CornerRadius), Title = "Скругление", Category = "Appearance", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.CornerRadius) });
        }

        if (includeFont)
        {
            result.Add(new() { Key = nameof(DesignControlModel.FontFamily), Title = "Шрифт", Category = "Typography", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.FontFamily) });
            result.Add(new() { Key = nameof(DesignControlModel.FontSize), Title = "Размер шрифта", Category = "Typography", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.FontSize) });
            result.Add(new() { Key = nameof(DesignControlModel.FontWeight), Title = "Толщина шрифта", Category = "Typography", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.FontWeight) });
        }

        if (includePadding)
            result.Add(new() { Key = nameof(DesignControlModel.Padding), Title = "Внутренний отступ", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Padding) });

        if (includeLayout)
        {
            result.Add(new() { Key = nameof(DesignControlModel.LayoutOrientation), Title = "Ориентация", Category = "Layout", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.LayoutOrientation) });
            result.Add(new() { Key = nameof(DesignControlModel.LayoutSpacing), Title = "Шаг между элементами", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.LayoutSpacing) });
        }

        if (includeGrid)
        {
            result.Add(new() { Key = nameof(DesignControlModel.Columns), Title = "Колонки", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Columns) });
            result.Add(new() { Key = nameof(DesignControlModel.Rows), Title = "Строки", Category = "Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.Rows) });
            result.Add(new() { Key = nameof(DesignControlModel.ShowGridLines), Title = "Показывать линии", Category = "Appearance", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.ShowGridLines) });
        }

        return result;
    }

    private static IReadOnlyList<DesignPropertyDescriptor> CreateDataGridProperties()
    {
        var result = CreateCommonProperties(includeBackground: true, includeBorder: true, includeForeground: true, includeFont: true).ToList();
        result.Add(new() { Key = nameof(DesignControlModel.Text), Title = "\u0417\u0430\u0433\u043E\u043B\u043E\u0432\u043E\u043A \u0442\u0430\u0431\u043B\u0438\u0446\u044B", Category = "Content", Editor = PropertyEditorKind.Text, BuiltInPropertyName = nameof(DesignControlModel.Text) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridGlowColor), Title = "Glow", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridGlowColor) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridRowBackground), Title = "\u0412\u0441\u0435 \u0441\u0442\u0440\u043E\u043A\u0438", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridRowBackground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridAlternateRowBackground), Title = "\u0427\u0435\u0440\u0435\u0434\u0443\u044E\u0449\u0438\u0435\u0441\u044F \u0441\u0442\u0440\u043E\u043A\u0438", Category = "Appearance", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridAlternateRowBackground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHeaderBackground), Title = "Фон заголовка", Category = "DataGrid/Header", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridHeaderBackground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHeaderForeground), Title = "Текст заголовка", Category = "DataGrid/Header", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridHeaderForeground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridRowForeground), Title = "Текст строк", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridRowForeground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHoverRowBackground), Title = "Hover строки", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridHoverRowBackground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridSelectedRowBackground), Title = "Выбранная строка", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridSelectedRowBackground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridSelectedRowForeground), Title = "Текст выбранной строки", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridSelectedRowForeground) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridGridLineBrush), Title = "Линии сетки", Category = "DataGrid/Lines", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridGridLineBrush) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridOuterBorderBrush), Title = "Внешняя рамка", Category = "DataGrid/Lines", Editor = PropertyEditorKind.Color, BuiltInPropertyName = nameof(DesignControlModel.DataGridOuterBorderBrush) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHeaderFontSize), Title = "Размер шрифта заголовка", Category = "DataGrid/Header", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.DataGridHeaderFontSize) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHeaderFontWeight), Title = "Насыщенность заголовка", Category = "DataGrid/Header", Editor = PropertyEditorKind.Enum, BuiltInPropertyName = nameof(DesignControlModel.DataGridHeaderFontWeight), Options = CreateFontWeightOptions() });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridRowFontSize), Title = "Размер шрифта строк", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.DataGridRowFontSize) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridRowFontWeight), Title = "Насыщенность строк", Category = "DataGrid/Rows", Editor = PropertyEditorKind.Enum, BuiltInPropertyName = nameof(DesignControlModel.DataGridRowFontWeight), Options = CreateFontWeightOptions() });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridHeaderHeight), Title = "Высота заголовка", Category = "DataGrid/Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.DataGridHeaderHeight) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridRowHeight), Title = "Высота строки", Category = "DataGrid/Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.DataGridRowHeight) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridCellPadding), Title = "Отступ ячейки", Category = "DataGrid/Layout", Editor = PropertyEditorKind.Number, BuiltInPropertyName = nameof(DesignControlModel.DataGridCellPadding) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridShowHeader), Title = "Показывать заголовок", Category = "DataGrid/Visibility", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.DataGridShowHeader) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridShowRowLines), Title = "Линии строк", Category = "DataGrid/Visibility", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.DataGridShowRowLines) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridShowColumnLines), Title = "Линии колонок", Category = "DataGrid/Visibility", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.DataGridShowColumnLines) });
        result.Add(new() { Key = nameof(DesignControlModel.DataGridShowAlternatingRows), Title = "Чередование строк", Category = "DataGrid/Visibility", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.DataGridShowAlternatingRows) });
        result.Add(new() { Key = nameof(DesignControlModel.ShowFilterRow), Title = "Строка фильтра", Category = "DataGrid/Filtering", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.ShowFilterRow) });
        result.Add(new()
        {
            Key = nameof(DesignControlModel.FilterMode),
            Title = "Режим фильтра",
            Category = "DataGrid/Filtering",
            Editor = PropertyEditorKind.Enum,
            BuiltInPropertyName = nameof(DesignControlModel.FilterMode),
            Options = new[]
            {
                new PropertyOption { Value = DesignControlModel.DataGridFilterModeContains, Title = "Contains" },
                new PropertyOption { Value = DesignControlModel.DataGridFilterModeStartsWith, Title = "StartsWith" },
                new PropertyOption { Value = DesignControlModel.DataGridFilterModeEquals, Title = "Equals" }
            }
        });
        result.Add(new() { Key = nameof(DesignControlModel.ShowGroupPanel), Title = "Панель группировки", Category = "DataGrid/Grouping", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.ShowGroupPanel) });
        result.Add(new() { Key = nameof(DesignControlModel.AllowGrouping), Title = "Разрешить группировку", Category = "DataGrid/Grouping", Editor = PropertyEditorKind.Bool, BuiltInPropertyName = nameof(DesignControlModel.AllowGrouping) });
        result.Add(new()
        {
            Key = nameof(DesignControlModel.DataGridTextAlignment),
            Title = "\u0412\u044B\u0440\u0430\u0432\u043D\u0438\u0432\u0430\u043D\u0438\u0435 \u0442\u0435\u043A\u0441\u0442\u0430",
            Category = "Appearance",
            Editor = PropertyEditorKind.Enum,
            BuiltInPropertyName = nameof(DesignControlModel.DataGridTextAlignment),
            Options = new[]
            {
                new PropertyOption { Value = DesignControlModel.DataGridTextAlignmentLeft, Title = "\u0421\u043B\u0435\u0432\u0430" },
                new PropertyOption { Value = DesignControlModel.DataGridTextAlignmentCenter, Title = "\u041F\u043E \u0446\u0435\u043D\u0442\u0440\u0443" },
                new PropertyOption { Value = DesignControlModel.DataGridTextAlignmentRight, Title = "\u0421\u043F\u0440\u0430\u0432\u0430" }
            }
        });
        return result;
    }

    private static IReadOnlyList<PropertyOption> CreateFontWeightOptions()
    {
        return new[]
        {
            new PropertyOption { Value = "Normal", Title = "Normal" },
            new PropertyOption { Value = "Medium", Title = "Medium" },
            new PropertyOption { Value = "SemiBold", Title = "SemiBold" },
            new PropertyOption { Value = "Bold", Title = "Bold" }
        };
    }
}
