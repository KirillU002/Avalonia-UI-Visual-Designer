using FormDesigner.Models;
using System;
using System.Collections.Generic;

namespace FormDesigner.Services;

public static class ReusableTemplateCatalog
{
    public static IReadOnlyList<ReusableTemplateModel> CreateBuiltInTemplates()
    {
        return new[]
        {
            CreateLoginForm(),
            CreateCrudToolbar(),
            CreateSearchPanel(),
            CreateDataGridFilterPanel(),
            CreateHeaderFooter(),
            CreateCardBlock()
        };
    }

    private static ReusableTemplateModel CreateLoginForm()
    {
        const string rootId = "builtin-login-root";
        return new ReusableTemplateModel
        {
            Id = "builtin-login-form",
            Name = "Форма входа",
            Category = "Формы",
            Description = "Готовая карточка входа: заголовок, логин, пароль, remember me и кнопка.",
            IsBuiltIn = true,
            Width = 360,
            Height = 260,
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "LoginTemplate", 0, 0, 360, 260, background: "Transparent"),
                Control("builtin-login-card", DesignerControlTypes.Border, "LoginCard", 0, 0, 360, 260, rootId, background: "#FFFFFF", border: "#BFDBFE", thickness: 1, radius: 22),
                Control("builtin-login-title", DesignerControlTypes.TextBlock, "LoginTitle", 28, 26, 280, 34, rootId, "Вход в систему", background: "Transparent", foreground: "#0F172A", fontSize: 22, fontWeight: "Bold"),
                Control("builtin-login-subtitle", DesignerControlTypes.TextBlock, "LoginSubtitle", 28, 62, 300, 26, rootId, "Введите учетные данные", background: "Transparent", foreground: "#64748B", fontSize: 13),
                Control("builtin-login-user", DesignerControlTypes.TextBox, "LoginTextBox", 28, 102, 304, 36, rootId, placeholder: "Логин или email", background: "#F8FAFC", border: "#CBD5E1"),
                Control("builtin-login-password", DesignerControlTypes.TextBox, "PasswordTextBox", 28, 148, 304, 36, rootId, placeholder: "Пароль", background: "#F8FAFC", border: "#CBD5E1"),
                Control("builtin-login-remember", DesignerControlTypes.CheckBox, "RememberCheckBox", 28, 194, 160, 30, rootId, "Запомнить меня", background: "Transparent"),
                Control("builtin-login-submit", DesignerControlTypes.Button, "LoginButton", 204, 192, 128, 38, rootId, "Войти", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 12, fontWeight: "SemiBold")
            }
        };
    }

    private static ReusableTemplateModel CreateCrudToolbar()
    {
        const string rootId = "builtin-crud-root";
        return new ReusableTemplateModel
        {
            Id = "builtin-crud-toolbar",
            Name = "CRUD-панель",
            Category = "Панели",
            Description = "Панель действий для справочника: создать, изменить, удалить, обновить и поиск.",
            IsBuiltIn = true,
            Width = 680,
            Height = 76,
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "CrudToolbarTemplate", 0, 0, 680, 76, background: "Transparent"),
                Control("builtin-crud-bg", DesignerControlTypes.Border, "CrudToolbarBackground", 0, 0, 680, 76, rootId, background: "#EFF6FF", border: "#BFDBFE", radius: 18),
                Control("builtin-crud-add", DesignerControlTypes.Button, "AddButton", 18, 19, 96, 38, rootId, "Создать", background: "#16A34A", foreground: "#FFFFFF", border: "#15803D", radius: 11, fontWeight: "SemiBold"),
                Control("builtin-crud-edit", DesignerControlTypes.Button, "EditButton", 124, 19, 104, 38, rootId, "Изменить", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 11, fontWeight: "SemiBold"),
                Control("builtin-crud-delete", DesignerControlTypes.Button, "DeleteButton", 238, 19, 92, 38, rootId, "Удалить", background: "#EF4444", foreground: "#FFFFFF", border: "#DC2626", radius: 11, fontWeight: "SemiBold"),
                Control("builtin-crud-refresh", DesignerControlTypes.Button, "RefreshButton", 340, 19, 104, 38, rootId, "Обновить", background: "#F8FAFC", foreground: "#0F172A", border: "#CBD5E1", radius: 11, fontWeight: "SemiBold"),
                Control("builtin-crud-search", DesignerControlTypes.TextBox, "ToolbarSearchTextBox", 462, 19, 198, 38, rootId, placeholder: "Поиск...", background: "#FFFFFF", border: "#93C5FD")
            }
        };
    }

    private static ReusableTemplateModel CreateSearchPanel()
    {
        const string rootId = "builtin-search-root";
        return new ReusableTemplateModel
        {
            Id = "builtin-search-panel",
            Name = "Панель поиска",
            Category = "Панели",
            Description = "Компактный блок фильтров: текст поиска, статус и кнопка применения.",
            IsBuiltIn = true,
            Width = 560,
            Height = 132,
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "SearchPanelTemplate", 0, 0, 560, 132, background: "Transparent"),
                Control("builtin-search-bg", DesignerControlTypes.Border, "SearchPanelBackground", 0, 0, 560, 132, rootId, background: "#FFFFFF", border: "#D7E2EE", radius: 18),
                Control("builtin-search-title", DesignerControlTypes.TextBlock, "SearchTitle", 22, 18, 220, 28, rootId, "Поиск и фильтр", background: "Transparent", fontSize: 18, fontWeight: "Bold"),
                Control("builtin-search-box", DesignerControlTypes.TextBox, "SearchTextBox", 22, 58, 330, 38, rootId, placeholder: "Введите текст для поиска", background: "#F8FAFC", border: "#CBD5E1"),
                Control("builtin-search-active", DesignerControlTypes.CheckBox, "OnlyActiveCheckBox", 22, 98, 180, 26, rootId, "Только активные", background: "Transparent"),
                Control("builtin-search-button", DesignerControlTypes.Button, "ApplySearchButton", 372, 58, 156, 38, rootId, "Применить", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 11, fontWeight: "SemiBold")
            }
        };
    }

    private static ReusableTemplateModel CreateDataGridFilterPanel()
    {
        const string rootId = "builtin-grid-root";
        const string sourceId = "builtin-grid-source";
        return new ReusableTemplateModel
        {
            Id = "builtin-datagrid-filter-panel",
            Name = "DataGrid + фильтр",
            Category = "Данные",
            Description = "Поисковая панель и DataGrid с BindingSource, filter row, group panel и footer.",
            IsBuiltIn = true,
            Width = 820,
            Height = 460,
            BindingSources = new List<BindingSourceFileModel>
            {
                new()
                {
                    Id = sourceId,
                    Name = "TemplateTableSource",
                    Path = "Items",
                    ItemTypeName = "TableRow",
                    Description = "Источник данных шаблона DataGrid.",
                    SourceKind = "Manual",
                    SourceSchemaName = "dbo",
                    Fields = new List<BindingFieldFileModel>
                    {
                        Field("Id", "Id", "1001", "110", "int"),
                        Field("Name", "Name", "Строка таблицы", "*", "string"),
                        Field("Status", "Status", "В работе", "130", "string"),
                        Field("Amount", "Amount", "1250", "120", "decimal", BindingFieldModel.AlignmentRight, BindingFieldModel.SummaryTypeSum, "Итого: {0:N2}")
                    }
                }
            },
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "DataGridPanelTemplate", 0, 0, 820, 460, background: "Transparent"),
                Control("builtin-grid-panel", DesignerControlTypes.Border, "FilterPanelBackground", 0, 0, 820, 84, rootId, background: "#EFF6FF", border: "#BFDBFE", radius: 18),
                Control("builtin-grid-title", DesignerControlTypes.TextBlock, "GridPanelTitle", 20, 16, 220, 24, rootId, "Список", background: "Transparent", fontSize: 18, fontWeight: "Bold"),
                Control("builtin-grid-search", DesignerControlTypes.TextBox, "GridSearchTextBox", 20, 42, 330, 34, rootId, placeholder: "Поиск по таблице...", background: "#FFFFFF", border: "#93C5FD"),
                Control("builtin-grid-filter", DesignerControlTypes.Button, "GridFilterButton", 368, 42, 112, 34, rootId, "Найти", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10, fontWeight: "SemiBold"),
                Control("builtin-grid-reset", DesignerControlTypes.Button, "GridResetButton", 490, 42, 116, 34, rootId, "Сбросить", background: "#F8FAFC", foreground: "#0F172A", border: "#CBD5E1", radius: 10),
                DataGridControl("builtin-grid-table", "ItemsDataGrid", 0, 104, 820, 356, rootId, sourceId)
            }
        };
    }

    private static ReusableTemplateModel CreateHeaderFooter()
    {
        const string rootId = "builtin-header-footer-root";
        return new ReusableTemplateModel
        {
            Id = "builtin-header-footer",
            Name = "Шапка и подвал",
            Category = "Каркас",
            Description = "Готовый верхний заголовок с действиями и нижняя информационная строка.",
            IsBuiltIn = true,
            Width = 760,
            Height = 180,
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "HeaderFooterTemplate", 0, 0, 760, 180, background: "Transparent"),
                Control("builtin-header-bg", DesignerControlTypes.Border, "HeaderBackground", 0, 0, 760, 76, rootId, background: "#0F172A", border: "#1E293B", radius: 18),
                Control("builtin-header-title", DesignerControlTypes.TextBlock, "HeaderTitle", 24, 18, 330, 34, rootId, "Панель управления", background: "Transparent", foreground: "#FFFFFF", fontSize: 22, fontWeight: "Bold"),
                Control("builtin-header-action", DesignerControlTypes.Button, "HeaderActionButton", 596, 19, 136, 38, rootId, "Действие", background: "#38BDF8", foreground: "#082F49", border: "#0EA5E9", radius: 12, fontWeight: "SemiBold"),
                Control("builtin-footer-bg", DesignerControlTypes.Border, "FooterBackground", 0, 112, 760, 68, rootId, background: "#F8FAFC", border: "#CBD5E1", radius: 16),
                Control("builtin-footer-text", DesignerControlTypes.TextBlock, "FooterText", 24, 133, 420, 26, rootId, "Готово. Последнее обновление: сегодня", background: "Transparent", foreground: "#475569", fontSize: 13),
                Control("builtin-footer-button", DesignerControlTypes.Button, "FooterHelpButton", 616, 126, 116, 36, rootId, "Справка", background: "#FFFFFF", foreground: "#0F172A", border: "#CBD5E1", radius: 10)
            }
        };
    }

    private static ReusableTemplateModel CreateCardBlock()
    {
        const string rootId = "builtin-card-root";
        return new ReusableTemplateModel
        {
            Id = "builtin-card-block",
            Name = "Карточка",
            Category = "Контент",
            Description = "Карточка с заголовком, описанием, статусом и основной кнопкой.",
            IsBuiltIn = true,
            Width = 360,
            Height = 220,
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "CardBlockTemplate", 0, 0, 360, 220, background: "Transparent"),
                Control("builtin-card-bg", DesignerControlTypes.Border, "CardBackground", 0, 0, 360, 220, rootId, background: "#FFFFFF", border: "#D7E2EE", radius: 22),
                Control("builtin-card-status", DesignerControlTypes.TextBlock, "CardStatus", 24, 22, 112, 24, rootId, "Активно", background: "#DCFCE7", foreground: "#166534", border: "#BBF7D0", radius: 999, fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-card-title", DesignerControlTypes.TextBlock, "CardTitle", 24, 62, 280, 32, rootId, "Заголовок карточки", background: "Transparent", foreground: "#0F172A", fontSize: 20, fontWeight: "Bold"),
                Control("builtin-card-text", DesignerControlTypes.TextBlock, "CardText", 24, 100, 306, 54, rootId, "Короткое описание блока интерфейса, которое можно заменить на свой текст.", background: "Transparent", foreground: "#64748B", fontSize: 13),
                Control("builtin-card-button", DesignerControlTypes.Button, "CardActionButton", 24, 166, 140, 36, rootId, "Открыть", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 11, fontWeight: "SemiBold")
            }
        };
    }

    private static DesignerControlFileModel Control(
        string id,
        string type,
        string name,
        double x,
        double y,
        double width,
        double height,
        string parentId = "",
        string text = "",
        string placeholder = "",
        string background = "#FFFFFF",
        string foreground = "#0F172A",
        string border = "#94A3B8",
        double thickness = 1,
        double radius = 6,
        double fontSize = 14,
        string fontWeight = "Normal")
    {
        return new DesignerControlFileModel
        {
            Id = id,
            Type = type,
            Name = name,
            ParentId = parentId,
            Text = text,
            PlaceholderText = placeholder,
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            BorderThickness = thickness,
            CornerRadius = radius,
            FontSize = fontSize,
            FontWeight = fontWeight,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            IsVisible = true
        };
    }

    private static DesignerControlFileModel DataGridControl(
        string id,
        string name,
        double x,
        double y,
        double width,
        double height,
        string parentId,
        string bindingSourceId)
    {
        return new DesignerControlFileModel
        {
            Id = id,
            Type = DesignerControlTypes.DataGrid,
            Name = name,
            ParentId = parentId,
            Text = "DataGrid",
            Background = "#FFFFFF",
            Foreground = "#0F172A",
            BorderBrush = "#60A5FA",
            DataGridOuterBorderBrush = "#60A5FA",
            DataGridHeaderBackground = "#E2E8F0",
            DataGridHeaderForeground = "#0F172A",
            DataGridRowBackground = "#FFFFFF",
            DataGridAlternateRowBackground = "#F8FAFC",
            DataGridSelectedRowBackground = "#DBEAFE",
            DataGridHoverRowBackground = "#EFF6FF",
            DataGridGridLineBrush = "#D7E2EE",
            DataGridHeaderHeight = 46,
            DataGridRowHeight = 34,
            DataGridCellPadding = 12,
            DataGridShowHeader = true,
            DataGridShowRowLines = true,
            DataGridShowColumnLines = true,
            DataGridShowAlternatingRows = true,
            ShowFilterRow = true,
            ShowGroupPanel = true,
            AllowGrouping = true,
            ShowFooter = true,
            AutoGenerateColumns = false,
            BindingSourceId = bindingSourceId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            IsVisible = true
        };
    }

    private static BindingFieldFileModel Field(
        string header,
        string path,
        string sampleValue,
        string width,
        string typeName,
        string alignment = BindingFieldModel.AlignmentLeft,
        string summaryType = BindingFieldModel.SummaryTypeNone,
        string summaryFormat = "")
    {
        return new BindingFieldFileModel
        {
            Header = header,
            Path = path,
            SampleValue = sampleValue,
            Width = width,
            TypeName = typeName,
            CellAlignment = alignment,
            HeaderAlignment = BindingFieldModel.AlignmentLeft,
            TextTrimming = BindingFieldModel.TextTrimmingCharacterEllipsis,
            TextWrapping = BindingFieldModel.TextWrappingNoWrap,
            MaxLines = 1,
            MinWidth = 56,
            MaxWidth = 0,
            AllowResize = true,
            AllowSort = true,
            AllowFilter = true,
            SummaryType = summaryType,
            SummaryFormat = summaryFormat
        };
    }
}
