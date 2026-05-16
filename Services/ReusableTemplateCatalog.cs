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
            CreateCustomersCrudDemo(),
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

    private static ReusableTemplateModel CreateCustomersCrudDemo()
    {
        const string rootId = "builtin-customers-form-root";
        const string detailsPanelId = "builtin-customers-details-panel";
        const string sourceId = "builtin-customers-source";

        return new ReusableTemplateModel
        {
            Id = "builtin-customers-crud-demo",
            Name = "Форма клиентов",
            Category = "Данные",
            Description = "Готовая demo form: Customers DataGrid, BindingSource с реальными полями, карточка деталей и рабочие interactions.",
            IsBuiltIn = true,
            Width = 1080,
            Height = 660,
            BindingSources = new List<BindingSourceFileModel>
            {
                new()
                {
                    Id = sourceId,
                    Name = "CustomersSource",
                    Path = "Customers",
                    ItemTypeName = "Customer",
                    Description = "Demo template source for the Customers CRUD form.",
                    SourceKind = "Manual",
                    SourceSchemaName = "dbo",
                    Fields = new List<BindingFieldFileModel>
                    {
                        Field("Id", "Id", "1001", "72", "int"),
                        Field("Name", "Name", "Анна Смирнова", "*", "string"),
                        Field("Email", "Email", "anna.smirnova@example.com", "220", "string"),
                        Field("Phone", "Phone", "+7 921 555-0148", "150", "string"),
                        Field("Status", "Status", "Активен", "118", "string")
                    }
                }
            },
            Controls = new List<DesignerControlFileModel>
            {
                Control(rootId, DesignerControlTypes.Group, "CustomersFormTemplate", 0, 0, 1080, 660, background: "Transparent"),
                Control("builtin-customers-bg", DesignerControlTypes.Border, "CustomersFormBackground", 0, 0, 1080, 660, rootId, background: "#F6F8FB", border: "#D7E2EE", radius: 24),
                Control("builtin-customers-title", DesignerControlTypes.TextBlock, "CustomersFormTitle", 28, 24, 360, 34, rootId, "Форма клиентов", background: "Transparent", foreground: "#0F172A", fontSize: 26, fontWeight: "Bold"),
                Control("builtin-customers-subtitle", DesignerControlTypes.TextBlock, "CustomersFormSubtitle", 30, 60, 560, 26, rootId, "Demo-сценарий для защиты: таблица, детали, действия и экспорт в Avalonia.", background: "Transparent", foreground: "#64748B", fontSize: 13),
                Control("builtin-customers-demo-badge-bg", DesignerControlTypes.Border, "CustomersDemoBadgeBackground", 858, 24, 190, 28, rootId, background: "#E0F2FE", border: "#7DD3FC", radius: 999),
                Control("builtin-customers-demo-badge", DesignerControlTypes.TextBlock, "CustomersDemoBadge", 878, 28, 150, 22, rootId, "Real DataGrid demo", background: "Transparent", foreground: "#0369A1", fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-customers-toggle-details", DesignerControlTypes.CheckBox, "ShowDetailsCheckBox", 830, 60, 220, 30, rootId, "Показывать детали", background: "Transparent", foreground: "#334155", fontSize: 13),

                Control("builtin-customers-grid-card", DesignerControlTypes.Border, "CustomersGridCard", 24, 110, 670, 510, rootId, background: "#FFFFFF", border: "#D7E2EE", radius: 18),
                Control("builtin-customers-grid-title", DesignerControlTypes.TextBlock, "CustomersGridTitle", 46, 130, 220, 28, rootId, "Клиенты", background: "Transparent", foreground: "#0F172A", fontSize: 19, fontWeight: "Bold"),
                Control("builtin-customers-grid-hint", DesignerControlTypes.TextBlock, "CustomersGridHint", 46, 156, 420, 22, rootId, "Выберите строку, чтобы заполнить карточку деталей.", background: "Transparent", foreground: "#64748B", fontSize: 12),
                DataGridControl("builtin-customers-grid", "CustomersGrid", 46, 184, 626, 410, rootId, sourceId, showFilterRow: false, showGroupPanel: false, showFooter: false),

                Control(detailsPanelId, DesignerControlTypes.Group, "CustomerDetailsPanel", 716, 110, 340, 510, rootId, background: "Transparent"),
                Control("builtin-customers-details-card", DesignerControlTypes.Border, "CustomerDetailsCard", 0, 0, 340, 510, detailsPanelId, background: "#FFFFFF", border: "#D7E2EE", radius: 18),
                Control("builtin-customers-details-title", DesignerControlTypes.TextBlock, "CustomerDetailsTitle", 22, 20, 220, 28, detailsPanelId, "Детали клиента", background: "Transparent", foreground: "#0F172A", fontSize: 19, fontWeight: "Bold"),
                Control("builtin-customers-details-hint", DesignerControlTypes.TextBlock, "CustomerDetailsHint", 22, 46, 290, 24, detailsPanelId, "Поля заполняются из выбранной строки.", background: "Transparent", foreground: "#64748B", fontSize: 12),

                Control("builtin-customers-name-label", DesignerControlTypes.TextBlock, "CustomerNameLabel", 22, 82, 120, 20, detailsPanelId, "Name", background: "Transparent", foreground: "#475569", fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-customers-name-box", DesignerControlTypes.TextBox, "CustomerNameTextBox", 22, 104, 296, 38, detailsPanelId, placeholder: "Имя клиента", background: "#F8FAFC", border: "#CBD5E1", radius: 10, textBindingPath: "CurrentCustomer.Name"),
                Control("builtin-customers-email-label", DesignerControlTypes.TextBlock, "CustomerEmailLabel", 22, 154, 120, 20, detailsPanelId, "Email", background: "Transparent", foreground: "#475569", fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-customers-email-box", DesignerControlTypes.TextBox, "CustomerEmailTextBox", 22, 176, 296, 38, detailsPanelId, placeholder: "email@example.com", background: "#F8FAFC", border: "#CBD5E1", radius: 10, textBindingPath: "CurrentCustomer.Email"),
                Control("builtin-customers-phone-label", DesignerControlTypes.TextBlock, "CustomerPhoneLabel", 22, 226, 120, 20, detailsPanelId, "Phone", background: "Transparent", foreground: "#475569", fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-customers-phone-box", DesignerControlTypes.TextBox, "CustomerPhoneTextBox", 22, 248, 296, 38, detailsPanelId, placeholder: "+7 000 000-00-00", background: "#F8FAFC", border: "#CBD5E1", radius: 10, textBindingPath: "CurrentCustomer.Phone"),
                Control("builtin-customers-status-label", DesignerControlTypes.TextBlock, "CustomerStatusLabel", 22, 298, 120, 20, detailsPanelId, "Status", background: "Transparent", foreground: "#475569", fontSize: 12, fontWeight: "SemiBold"),
                Control("builtin-customers-status-bg", DesignerControlTypes.Border, "CustomerStatusBadgeBackground", 22, 322, 160, 34, detailsPanelId, background: "#DCFCE7", border: "#BBF7D0", radius: 999),
                Control("builtin-customers-status-text", DesignerControlTypes.TextBlock, "CustomerStatusTextBlock", 36, 328, 132, 22, detailsPanelId, "Не выбран", background: "Transparent", foreground: "#166534", fontSize: 13, fontWeight: "SemiBold", textBindingPath: "CurrentCustomer.Status"),

                Control("builtin-customers-add", DesignerControlTypes.Button, "AddCustomerButton", 22, 384, 92, 38, detailsPanelId, "Добавить", background: "#16A34A", foreground: "#FFFFFF", border: "#15803D", radius: 10, fontWeight: "SemiBold", generatedButtonActionKey: "Add"),
                Control("builtin-customers-save", DesignerControlTypes.Button, "SaveCustomerButton", 124, 384, 92, 38, detailsPanelId, "Сохранить", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10, fontWeight: "SemiBold", generatedButtonActionKey: "Save"),
                Control("builtin-customers-delete", DesignerControlTypes.Button, "DeleteCustomerButton", 226, 384, 92, 38, detailsPanelId, "Удалить", background: "#EF4444", foreground: "#FFFFFF", border: "#DC2626", radius: 10, fontWeight: "SemiBold", generatedButtonActionKey: "Delete"),
                Control("builtin-customers-clear", DesignerControlTypes.Button, "ClearCustomerButton", 22, 434, 140, 38, detailsPanelId, "Очистить", background: "#F8FAFC", foreground: "#0F172A", border: "#CBD5E1", radius: 10, fontWeight: "SemiBold", generatedButtonActionKey: "Clear"),
                Control("builtin-customers-message", DesignerControlTypes.Button, "MessageCustomerButton", 172, 434, 146, 38, detailsPanelId, "Сообщение", background: "#0EA5E9", foreground: "#FFFFFF", border: "#0284C7", radius: 10, fontWeight: "SemiBold")
            },
            Interactions = new List<InteractionFileModel>
            {
                Interaction("CustomersGrid", InteractionModel.EventDataGridSelectionChanged, InteractionModel.ActionSetProperty, "CustomerNameTextBox", InteractionModel.TargetPropertyText, "Name"),
                Interaction("CustomersGrid", InteractionModel.EventDataGridSelectionChanged, InteractionModel.ActionSetProperty, "CustomerEmailTextBox", InteractionModel.TargetPropertyText, "Email"),
                Interaction("CustomersGrid", InteractionModel.EventDataGridSelectionChanged, InteractionModel.ActionSetProperty, "CustomerPhoneTextBox", InteractionModel.TargetPropertyText, "Phone"),
                Interaction("CustomersGrid", InteractionModel.EventDataGridSelectionChanged, InteractionModel.ActionSetProperty, "CustomerStatusTextBlock", InteractionModel.TargetPropertyText, "Status"),
                Interaction("ShowDetailsCheckBox", InteractionModel.EventCheckBoxChecked, InteractionModel.ActionToggleVisibility, "CustomerDetailsPanel", InteractionModel.TargetPropertyIsVisible),
                Interaction("ShowDetailsCheckBox", InteractionModel.EventCheckBoxUnchecked, InteractionModel.ActionToggleVisibility, "CustomerDetailsPanel", InteractionModel.TargetPropertyIsVisible),
                Interaction("ClearCustomerButton", InteractionModel.EventButtonClick, InteractionModel.ActionClearProperty, "CustomerNameTextBox", InteractionModel.TargetPropertyText),
                Interaction("ClearCustomerButton", InteractionModel.EventButtonClick, InteractionModel.ActionClearProperty, "CustomerEmailTextBox", InteractionModel.TargetPropertyText),
                Interaction("ClearCustomerButton", InteractionModel.EventButtonClick, InteractionModel.ActionClearProperty, "CustomerPhoneTextBox", InteractionModel.TargetPropertyText),
                Interaction("ClearCustomerButton", InteractionModel.EventButtonClick, InteractionModel.ActionClearProperty, "CustomerStatusTextBlock", InteractionModel.TargetPropertyText),
                Interaction("MessageCustomerButton", InteractionModel.EventButtonClick, InteractionModel.ActionShowMessage, textTemplate: "Демо форма клиентов готова: DataGrid, детали, кнопки и interactions экспортируются в Avalonia.", messageTitle: "Customers CRUD Demo")
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
        string fontWeight = "Normal",
        string textBindingPath = "",
        string generatedButtonActionKey = "",
        double padding = 8)
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
            Padding = padding,
            TextBindingPath = textBindingPath,
            GeneratedButtonActionKey = generatedButtonActionKey,
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
        string bindingSourceId,
        bool showFilterRow = true,
        bool showGroupPanel = true,
        bool showFooter = true)
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
            ShowFilterRow = showFilterRow,
            ShowGroupPanel = showGroupPanel,
            AllowGrouping = true,
            ShowFooter = showFooter,
            AutoGenerateColumns = false,
            BindingSourceId = bindingSourceId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            IsVisible = true
        };
    }

    private static InteractionFileModel Interaction(
        string sourceControlName,
        string eventName,
        string actionType,
        string targetControlName = "",
        string targetProperty = InteractionModel.TargetPropertyText,
        string sourcePath = "",
        string textTemplate = "",
        string messageTitle = "")
    {
        return new InteractionFileModel
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceControlName = sourceControlName,
            EventName = eventName,
            ActionType = actionType,
            TargetControlName = targetControlName,
            TargetProperty = targetProperty,
            SourcePath = sourcePath,
            TextTemplate = textTemplate,
            MessageTitle = messageTitle
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
