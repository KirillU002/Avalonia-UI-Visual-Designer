using FormDesigner.Models;
using System.Collections.Generic;

namespace FormDesigner.Services;

public sealed class HelpContentService
{
    public IReadOnlyList<HelpCarouselSlide> CreateCarouselSlides() =>
        new List<HelpCarouselSlide>
        {
            new()
            {
                Id = "designer-canvas",
                Title = "Designer Canvas",
                Subtitle = "Визуальная сборка формы",
                Description = "Перетаскивайте Button, TextBox, TextBlock, Border, DataGrid и другие controls на Canvas, меняйте размеры и проверяйте результат в Preview.",
                Icon = "DC",
                AccentBrush = "#2563EB",
                Tags = new[] { "Canvas", "Toolbox", "Preview" }
            },
            new()
            {
                Id = "property-inspector",
                Title = "Property Inspector",
                Subtitle = "Свойства выбранного control",
                Description = "Width, Height, Text, Background, Foreground и layout-свойства доступны в одном месте. Русские подсказки объясняют смысл свойств, не переводя технические имена.",
                Icon = "PI",
                AccentBrush = "#0F766E",
                Tags = new[] { "Properties", "Tooltips", "Layout" }
            },
            new()
            {
                Id = "datagrid-designer",
                Title = "DataGrid Designer",
                Subtitle = "Колонки, source и runtime binding",
                Description = "DataGrid поддерживает manual columns, SQL source, DLL source, Column Editor, preview rows и Export C# ViewModel с ItemsSource.",
                Icon = "DG",
                AccentBrush = "#7C3AED",
                Tags = new[] { "DataGrid", "SQL", "DLL" }
            },
            new()
            {
                Id = "preview-window",
                Title = "Preview Window",
                Subtitle = "Проверка формы до Export",
                Description = "Preview показывает runtime-поведение без мутации editor state: выбранный control, Inspector и Project Explorer не должны сбрасываться.",
                Icon = "PV",
                AccentBrush = "#0284C7",
                Tags = new[] { "Preview", "Runtime", "Isolation" }
            },
            new()
            {
                Id = "export-pipeline",
                Title = "Export Pipeline",
                Subtitle = "AXAML, C#, NuGet и Validate Build",
                Description = "Export генерирует AXAML, code-behind/ViewModel, DTO, nuget.config, README.generated.md и может проверить dotnet restore/build.",
                Icon = "EX",
                AccentBrush = "#EA580C",
                Tags = new[] { "AXAML", "C#", "Build" }
            },
            new()
            {
                Id = "dll-sql",
                Title = "DLL / SQL Data Sources",
                Subtitle = "Источник данных без путаницы",
                Description = "DLL import показывает статусы и таблицы, SQL preview использует реальные rows при настроенном source, а DataSourceKey защищает от смешивания таблиц.",
                Icon = "DS",
                AccentBrush = "#16A34A",
                Tags = new[] { "DLL", "SQL", "Binding" }
            },
            new()
            {
                Id = "logs",
                Title = "Logs / Diagnostics",
                Subtitle = "Понятная диагностика",
                Description = "Problems, Output и Logs помогают понять ошибки DLL, Export, Validate Build, DataGrid source и Preview/Runtime mismatch.",
                Icon = "LG",
                AccentBrush = "#475569",
                Tags = new[] { "Logs", "Diagnostics", "Build" }
            }
        };

    public IReadOnlyList<HelpSection> CreateSections() =>
        new List<HelpSection>
        {
            Home(),
            QuickStart(),
            Interface(),
            Forms(),
            Properties(),
            DataGrid(),
            DllSql(),
            PreviewExport(),
            Diagnostics(),
            DeveloperConcepts(),
            Tips(),
            About()
        };

    private static HelpSection Home() =>
        new()
        {
            Id = "home",
            Title = "Главная",
            Subtitle = "Справочный центр Avalonia Designer для Alpha 3.0.",
            Icon = "⌂",
            FeatureCards = new[]
            {
                Card("Alpha 3.0", "Стабилизационный релиз после Alpha 2.0: Multi Form, Property Inspector, Export, DataGrid, DLL Import и Preview/Export consistency.", "A3", "#2563EB"),
                Card("MVVM + Canvas", "Проект использует MVVM, но Designer Canvas остаётся удобным визуальным рабочим местом для быстрого прототипирования форм.", "MV", "#0F766E"),
                Card("Export", "Генерируются AXAML, C#, ViewModel, DTO, nuget.config и README.generated.md для дальнейшей работы в VS/Rider.", "EX", "#EA580C"),
                Card("DataGrid", "DataGrid больше не ограничивается визуальными колонками: есть source identity, preview rows, runtime ItemsSource и C# binding.", "DG", "#7C3AED")
            },
            QuickActions = new[]
            {
                Action("Создать первую форму", "Короткий путь от New Project до Preview.", "quick-start", "1"),
                Action("Понять интерфейс", "Где Toolbox, Inspector, Project Explorer и Export Pipeline.", "interface", "2"),
                Action("Настроить DataGrid", "Колонки, SQL/DLL source и runtime binding.", "datagrid", "3"),
                Action("Технические понятия", "Canvas, MVVM, AXAML, Binding, DataContext и Export Pipeline.", "developer", "4")
            },
            Articles = new[]
            {
                Article(
                    "Что это за проект",
                    "Avalonia UI Visual Designer помогает собрать формы Avalonia UI визуально, проверить их в Preview и экспортировать в AXAML/C# проект. Alpha 3.0 не является production-ready релизом, но основные сценарии уже стабилизированы для тестирования и дальнейшей разработки.",
                    "Визуальный Canvas для controls.",
                    "Multi Form и Project Explorer.",
                    "Property Inspector с русскими подсказками.",
                    "DataGrid с SQL/DLL/manual source.",
                    "Export Pipeline и Validate Build.")
            }
        };

    private static HelpSection QuickStart() =>
        Section(
            "quick-start",
            "Быстрый старт",
            "Минимальный путь от пустого проекта до exported app.",
            "▶",
            new[]
            {
                Card("1. Новый проект", "Нажмите «Новый проект», чтобы очистить формы, selection, binding caches и export state.", "01", "#2563EB"),
                Card("2. Добавьте форму", "Используйте Add Form, если нужно несколько окон или сценариев. Active Form отображается в Project Explorer.", "02", "#0F766E"),
                Card("3. Перетащите controls", "Button, TextBox, TextBlock, Border и DataGrid добавляются из Toolbox на Designer Canvas.", "03", "#7C3AED"),
                Card("4. Проверьте Export", "Откройте Export Pipeline, настройте NuGet source и запустите Validate Build.", "04", "#EA580C")
            },
            Article(
                "Типовой flow",
                "Работайте короткими циклами: добавили control, настроили свойства, открыли Preview, затем проверили Export.",
                "New Project очищает старое состояние.",
                "Add Form не должен ломать Property Inspector.",
                "Preview не должен менять selection.",
                "Export не должен вызывать ApplyDocument."),
            Article(
                "Что проверить перед Export",
                "Перед переносом generated code в отдельный проект убедитесь, что DataGrid source настроен, Validate Build прошёл, а Logs не содержат критичных ошибок.",
                "Проверьте ItemsSource для DataGrid.",
                "Проверьте NuGet source.",
                "Проверьте README.generated.md.")
        );

    private static HelpSection Interface() =>
        Section(
            "interface",
            "Интерфейс",
            "Основные зоны рабочего окна и за что они отвечают.",
            "UI",
            new[]
            {
                Card("Toolbar", "New Project, Open, Save, Preview, Export Pipeline и Help Center.", "TB", "#2563EB"),
                Card("Toolbox", "Список controls: технические названия Button, TextBox, DataGrid остаются английскими.", "TX", "#0F766E"),
                Card("Project Explorer", "Формы, controls и активный документ. Используйте его для навигации в Multi Form.", "PE", "#7C3AED"),
                Card("Property Inspector", "Компактные строки свойств и tooltip-описания на русском.", "PI", "#EA580C")
            },
            Article(
                "Designer Canvas",
                "Canvas показывает выбранную форму и visual wrappers. Drag, resize и selection работают в editor state и не должны зависеть от Export.",
                "Canvas.Left / Canvas.Top отвечают за позицию.",
                "ZIndex задаёт порядок наложения.",
                "Zoom не должен менять модель формы."),
            Article(
                "Правая панель",
                "Правая панель разделена на Properties, Data, Plugins, Export и Logic. Layout tab в Alpha 3.0 скрыта по feature flag, если она не нужна как отдельный инструмент.",
                "Data mode показывает source выбранного DataGrid.",
                "Column Editor редактирует реальные DataGrid columns.",
                "Logic отвечает за events/actions.")
        );

    private static HelpSection Forms() =>
        Section(
            "forms",
            "Работа с формами",
            "Multi Form, active form и безопасное переключение документов.",
            "MF",
            new[]
            {
                Card("New Project", "Полностью очищает старые формы, controls, DLL/source caches, selection и export artifacts state.", "NP", "#2563EB"),
                Card("Add Form", "Создаёт новую форму без повреждения Property Inspector и selection текущей формы.", "AF", "#0F766E"),
                Card("Rename/Delete", "Обновляет Project Explorer, tab header и active form без stale properties.", "RD", "#7C3AED")
            },
            Article(
                "Active Form",
                "Все операции Canvas, Inspector, Preview и Data mode должны работать от Active Form. Если у двух форм одинаковые DataGrid names, UI показывает FormName / DataGridName.",
                "Selection должен соответствовать active form.",
                "Inspector не должен показывать свойства старой формы.",
                "Export читает snapshot, а не mutates active document."),
            Article(
                "Типичные ошибки",
                "Если после переключения форм видны старые properties, значит где-то остался stale SelectedControl или CurrentInspectorControlId.",
                "Не переиспользуйте старые ids после New Project.",
                "Не вызывайте ApplyDocument из Preview/Export.",
                "Не сбрасывайте SelectedControl при background refresh.")
        );

    private static HelpSection Properties() =>
        Section(
            "properties",
            "Свойства элементов",
            "Property Inspector редактирует model, а не случайные UI controls.",
            "PR",
            new[]
            {
                Card("Основные", "Name, Text, Content, Width, Height, IsVisible и IsLocked.", "ON", "#2563EB"),
                Card("Внешний вид", "Background, Foreground, BorderBrush, CornerRadius, Opacity, FontSize.", "VK", "#0F766E"),
                Card("Layout", "Canvas.Left/Top, Margin, Padding, HorizontalAlignment, VerticalAlignment.", "LY", "#7C3AED"),
                Card("Tooltips", "Русские описания показываются только при наведении, чтобы список оставался компактным.", "TT", "#EA580C")
            },
            Article(
                "Правила редактирования",
                "Property edit должен быть локальным и предсказуемым: не запускать полный ApplyDocument, не пересобирать весь Canvas и не ломать text editing.",
                "RebuildPropertyGrid должен быть idempotent.",
                "Width/Height не должны блокировать Inspector.",
                "Пустой Button Content сохраняется как пустой текст."),
            Article(
                "Почему имена не переводятся",
                "Property names остаются техническими: Width, Height, Text, ItemsSource. На русском переводятся только описания, статусы, подсказки и ошибки.",
                "Так проще сопоставлять UI с Avalonia API.",
                "Generated AXAML/C# не зависит от локализации.")
        );

    private static HelpSection DataGrid() =>
        Section(
            "datagrid",
            "DataGrid",
            "Колонки, источники данных, Preview rows и runtime Export.",
            "DG",
            new[]
            {
                Card("Column Editor", "Главное место редактирования DataGridColumnModel: Add, Remove, Duplicate, Reorder, Sync from source.", "CE", "#2563EB"),
                Card("Data mode", "Показывает source/schema/sample для выбранного DataGrid и открывает Column Editor.", "DM", "#0F766E"),
                Card("Runtime binding", "Export генерирует ItemsSource, DTO, loader и DataContext.", "RB", "#7C3AED"),
                Card("Preview consistency", "Если Preview показывает demo rows, runtime тоже получает demo rows; если source real SQL, Preview грузит SQL rows.", "PC", "#EA580C")
            },
            Article(
                "Источники DataGrid",
                "DataGrid может работать без source, с SQL query, DLL table или demo rows. Source identity должен храниться как DataSourceKey, а не просто TableName.",
                "Manual columns подходят для layout/demo.",
                "SQL source требует connection string и query/table.",
                "DLL source требует уникальный DllId + Namespace + TypeName + TableName.",
                "SchemaOnly не должен молча показывать fake data."),
            Article(
                "Export DataGrid",
                "AXAML должен содержать ItemsSource и валидные column bindings. C# должен содержать ObservableCollection, row DTO, loader и DataContext.",
                "Не генерировать {Binding } или Path=\"\".",
                "Bool columns можно экспортировать как DataGridCheckBoxColumn.",
                "Header остаётся исходным именем колонки, Binding использует safe C# property.",
                "Multi Form + Multi DataGrid получают уникальные row types и properties.")
        );

    private static HelpSection DllSql() =>
        Section(
            "dll-sql",
            "DLL / SQL",
            "Data binding sources, metadata и безопасная загрузка rows.",
            "DS",
            new[]
            {
                Card("SQL Preview", "Настроенный SQL source должен показывать реальные rows в Preview, не demo «текст 1».", "SQL", "#2563EB"),
                Card("DLL Import", "DLL отображаются карточками: status, counts, tables/types/errors и действия Remove/Reload.", "DLL", "#0F766E"),
                Card("DataSourceKey", "Полный ключ защищает от одинаковых table names в разных DLL.", "KEY", "#7C3AED"),
                Card("Top N", "Preview rows ограничены, сортируются до Top N и грузятся async.", "TOP", "#EA580C")
            },
            Article(
                "SQL source",
                "Если connection string и query заполнены, Preview должен выполнить limited query, а Export может сгенерировать SQL loader при разрешённом export connection string.",
                "Если строка подключения не экспортируется, generated code содержит TODO.",
                "NuGet package зависит от provider.",
                "DBNull читается безопасно."),
            Article(
                "DLL source",
                "DLL import извлекает LINQ to SQL metadata: TableAttribute, ColumnAttribute, primary key, nullable, db type и namespace/type.",
                "Ошибки загрузки не скрываются.",
                "Failed DLL можно удалить.",
                "Preview real rows используется только если provider умеет безопасно их получить.")
        );

    private static HelpSection PreviewExport() =>
        Section(
            "preview-export",
            "Preview / Export",
            "Проверка runtime-вида и генерация проекта.",
            "PX",
            new[]
            {
                Card("Preview", "Изолированное runtime-окно без влияния на active form, selected control и Inspector.", "PV", "#2563EB"),
                Card("Export Pipeline", "AXAML, C#, ViewModel, DTO, nuget.config, README.generated.md и artifacts.", "EX", "#0F766E"),
                Card("Validate Build", "Показывает этапы restore/build, NuGet source, stdout/stderr и deduplicated warnings.", "VB", "#7C3AED"),
                Card("NuGet source", "Можно указать custom HTTP/HTTPS/local source и allowInsecureConnections для HTTP.", "NG", "#EA580C")
            },
            Article(
                "Export flow",
                "Export должен брать snapshot project model и быть read-only относительно editor state.",
                "Не вызывать ApplyDocument.",
                "Не менять ActiveForm.",
                "Не сбрасывать SelectedControl.",
                "Не пересобирать Canvas без причины."),
            Article(
                "Generated project",
                "Exported project должен быть самодостаточным: dotnet restore и dotnet build должны работать из папки проекта за счёт generated nuget.config.",
                "nuget.org используется по умолчанию.",
                "README.generated.md объясняет restore/build.",
                "Validate Build использует тот же nuget.config.")
        );

    private static HelpSection Diagnostics() =>
        Section(
            "diagnostics",
            "Диагностика",
            "Problems, Output, Logs и structured diagnostics.",
            "LG",
            new[]
            {
                Card("Problems", "Краткие проблемы текущего проекта: warnings/errors и что требует внимания.", "PB", "#DC2626"),
                Card("Output", "Поток событий Export, Build, Preview, DLL и DataGrid.", "OP", "#2563EB"),
                Card("Logs", "Фильтры по severity/category, copy selected/all и Open logs folder.", "LS", "#0F766E"),
                Card("Details", "Для DLL/export/build ошибок сохраняются подробности: command, path, exception, stdout/stderr.", "DT", "#7C3AED")
            },
            Article(
                "Что искать в логах",
                "Смотрите event names: EXPORT_PIPELINE_START, VALIDATE_BUILD_STEP_START, DLL_LOAD_FAILED, DATAGRID_PREVIEW_DATA_MODE, PREVIEW_RUNTIME_DATAGRID_MODE_MISMATCH.",
                "Warning не всегда блокирует Export.",
                "Error требует действия до Beta-quality результата.",
                "Detailed logs помогают воспроизвести ошибку вне UI."),
            Article(
                "Help Center diagnostics",
                "Окно справки логирует HELP_WINDOW_OPENED, HELP_WINDOW_MAXIMIZED, HELP_SECTION_SELECTED, HELP_CAROUSEL_SLIDE_CHANGED, HELP_SEARCH_PERFORMED и HELP_WINDOW_CLOSED.",
                "Diagnostics нужны только для расследования UX-багов.",
                "Справка не должна менять editor state.")
        );

    private static HelpSection DeveloperConcepts() =>
        Section(
            "developer",
            "Для разработчиков",
            "Технические понятия, которые помогают понимать Designer, Preview и generated project.",
            "DEV",
            new[]
            {
                Card("Canvas", "Основная поверхность дизайнера. Controls размещаются по координатам X/Y, а Canvas.Left и Canvas.Top определяют позицию.", "CV", "#2563EB"),
                Card("MVVM", "Model-View-ViewModel отделяет интерфейс от состояния и команд. View показывает UI, ViewModel управляет логикой, Model хранит данные проекта.", "MV", "#0F766E"),
                Card("AXAML", "Разметка Avalonia UI, похожая на XAML. Export Pipeline генерирует AXAML автоматически из модели проекта.", "AX", "#7C3AED"),
                Card("Binding", "Связывает свойство UI со свойством ViewModel. Для DataGrid это ItemsSource, SelectedItem и Binding колонок.", "BD", "#EA580C"),
                Card("DataContext", "Объект, в котором Avalonia ищет свойства для Binding. Без DataContext ItemsSource и другие bindings не работают.", "DC", "#0284C7"),
                Card("ViewModel", "Хранит состояние окна, команды, ObservableCollection и свойства, к которым привязан UI.", "VM", "#9333EA"),
                Card("Export Pipeline", "Берёт модель проекта и генерирует AXAML, C#, ViewModel, csproj, nuget.config и README.generated.md.", "EX", "#16A34A"),
                Card("DataGrid Binding", "DataGrid показывает rows из ObservableCollection через ItemsSource, а колонки читают свойства row-модели.", "DG", "#DC2626"),
                Card("NuGet Restore", "nuget.config задаёт package sources. Validate Build выполняет restore/build и показывает команды и ошибки.", "NG", "#475569")
            },
            Article(
                "Canvas",
                "Canvas — основная поверхность дизайнера. На нём элементы размещаются по координатам. Когда пользователь перетаскивает Button или DataGrid, конструктор сохраняет его X, Y, Width и Height в модели проекта, а затем использует эти данные при Preview и Export.",
                "X/Y соответствуют Canvas.Left и Canvas.Top.",
                "Width/Height задают размер control.",
                "Такой подход удобен для визуального конструктора."),
            Article(
                "MVVM",
                "MVVM — архитектурный подход, при котором интерфейс отделён от логики. View показывает элементы, ViewModel содержит свойства и команды, а Model хранит данные проекта.",
                "View отвечает за отображение.",
                "ViewModel хранит состояние и команды.",
                "Binding связывает View и ViewModel."),
            Article(
                "AXAML",
                "AXAML — разметка Avalonia UI. Она описывает визуальное дерево окна: Window, Grid, Canvas, Button, TextBox, DataGrid и другие controls.",
                "AXAML похож на XAML.",
                "Export Pipeline генерирует AXAML автоматически.",
                "AXAML должен совпадать с тем, что пользователь видел в Preview."),
            Article(
                "Binding и DataContext",
                "Binding связывает свойство UI со свойством ViewModel. DataContext указывает, где искать эти свойства. Если DataContext не назначен, Binding не сможет найти ItemsSource, Text, SelectedItem или другие свойства.",
                "DataGrid обычно использует ItemsSource.",
                "Колонки DataGrid используют Binding к свойствам row-модели.",
                "SelectedItem можно привязать к выбранной строке."),
            Article(
                "Preview / Runtime / Export",
                "Designer Preview — предварительный просмотр внутри конструктора. Runtime — приложение, запущенное после Export. Export — генерация AXAML/C# проекта.",
                "Preview не должен менять editor state.",
                "Runtime должен показывать те же rows и layout, что Preview.",
                "Export Pipeline должен быть read-only относительно конструктора."),
            Article(
                "Export Pipeline и NuGet",
                "Export Pipeline берёт модель проекта, генерирует AXAML, C#, ViewModel, csproj, nuget.config и README.generated.md. Validate Build выполняет dotnet restore/build и использует NuGet sources из generated nuget.config.",
                "Custom NuGet source задаётся в Export settings.",
                "HTTP source требует allowInsecureConnections.",
                "Ошибки restore/build сохраняются в Logs.")
        );

    private static HelpSection Tips() =>
        Section(
            "tips",
            "Горячие клавиши / советы",
            "Короткие правила, которые экономят время.",
            "⌁",
            new[]
            {
                Card("F1", "Открыть Help Center, если команда назначена в текущей сборке.", "F1", "#2563EB"),
                Card("Короткие циклы", "Preview после заметного изменения, Export/Validate Build перед переносом в новый проект.", "CY", "#0F766E"),
                Card("DataGrid first", "Сначала настройте source/schema, потом синхронизируйте Column Editor.", "DG", "#7C3AED"),
                Card("Logs first", "При ошибке build смотрите exact command, working directory и NuGet source.", "LG", "#EA580C")
            },
            Article(
                "Best practices",
                "Не редактируйте generated code как единственный источник правды. Возвращайтесь в Designer, исправляйте модель и снова делайте Export.",
                "Давайте controls уникальные понятные Name.",
                "Проверяйте Preview и Export вместе.",
                "Не храните секреты connection string в проекте без явного решения."),
            Article(
                "Когда что-то выглядит странно",
                "Если Preview и Runtime расходятся, проверьте data mode, ItemsSource, DataContext, source key и generated README.",
                "Если Inspector показывает старые свойства, смените selection и проверьте active form.",
                "Если DLL import упал, откройте details.")
        );

    private static HelpSection About() =>
        Section(
            "about",
            "О проекте",
            "Версия, статус и назначение.",
            "i",
            new[]
            {
                Card("Alpha 3.0", "Текущая версия с упором на стабилизацию Multi Form, Property Inspector, Export, DataGrid и Preview/Export consistency.", "A3", "#2563EB"),
                Card(".NET / Avalonia", "Проект построен на C#, .NET и Avalonia UI 11.", "AV", "#0F766E"),
                Card("Visual Designer", "Интерфейс можно собирать визуально: через Toolbox, Canvas и Property Inspector.", "VD", "#7C3AED"),
                Card("Export", "Конструктор генерирует AXAML, C#, ViewModel, README.generated.md и NuGet-конфигурацию.", "EX", "#EA580C")
            },
            Article(
                "Назначение",
                "Avalonia UI Visual Designer — это визуальный конструктор пользовательских интерфейсов для Avalonia UI. Он позволяет создавать формы, размещать элементы управления на Canvas, редактировать свойства, просматривать результат в Preview и экспортировать проект в AXAML/C#.",
                "Toolbox добавляет controls на форму.",
                "Property Inspector редактирует свойства выбранного элемента.",
                "Preview и Export помогают проверить результат вне режима редактирования."),
            Article(
                "Статус Alpha",
                "Проект находится на стадии Alpha 3.0. Основные сценарии уже реализованы, но приложение продолжает развиваться и требует тестирования перед Beta.",
                "Важна регрессия Multi Form.",
                "Важна сборка generated project.",
                "Важны Preview/Export consistency и DataGrid runtime rows.")
        );

    private static HelpSection Section(
        string id,
        string title,
        string subtitle,
        string icon,
        IReadOnlyList<HelpFeatureCard> featureCards,
        params HelpArticle[] articles) =>
        new()
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            FeatureCards = featureCards,
            Articles = articles
        };

    private static HelpArticle Article(string title, string body, params string[] bullets) =>
        new()
        {
            Title = title,
            Body = body,
            Bullets = bullets
        };

    private static HelpFeatureCard Card(string title, string description, string icon, string accentBrush) =>
        new()
        {
            Title = title,
            Description = description,
            Icon = icon,
            AccentBrush = accentBrush
        };

    private static HelpQuickAction Action(string title, string description, string targetSectionId, string icon) =>
        new()
        {
            Title = title,
            Description = description,
            TargetSectionId = targetSectionId,
            Icon = icon
        };
}
