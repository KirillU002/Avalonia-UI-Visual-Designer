# DropDownButton, SplitButton и ListBox в основном Designer

Проверено 30 октября 2026 года. Использована установленная Avalonia 11.1.1. Версии NuGet, TargetFramework, тема, DesignerSurface, драйверы БД и VSIX/IPC в рамках этого расширения не менялись.

## Реализация и переиспользование

Основной Toolbox получает компоненты из `DesignerRegistry` через `BuiltInControlRegistrar`. Дескриптор задаёт начальные значения и свойства; существующие bridges создают canvas/Preview-представление и AXAML. Модель `DesignControlModel` уже содержит геометрию, оформление и сериализуемый property bag. Перетаскивание, выделение, изменение размеров, undo, избранное Inspector и переключение форм используют существующий механизм.

DropDownButton и SplitButton теперь зарегистрированы в категории «Стандартные». ListBox добавлен в основной JSON Designer; импорт AXAML использует существующий Phase 5. Общая фабрика создаёт настоящий `Avalonia.Controls.ListBox` и для нового компонента, и для Phase 5. Второй import pipeline не создавался.

Используются настоящие `DropDownButton`, `SplitButton` и `Flyout`. Flyout содержит вертикальный список стандартных кнопок. Пункты имеют устойчивые ID: переименование и перестановка сохраняют назначенную Logic. В runtime и generated code нажатие закрывает Flyout и вызывает существующий обработчик `Button.Click`. Основная часть SplitButton имеет независимый обработчик. В списке источников Logic пункты показаны как «Имя кнопки → Название пункта».

## Свойства и редактор

Общие свойства: размеры, положение и существующие layout-параметры, Margin, Padding, Background, Foreground, BorderBrush, BorderThickness, FontFamily, FontSize, FontWeight, IsEnabled, IsVisible и Opacity. Для кнопок доступны Content, CornerRadius, Flyout и Flyout.Placement.

В Inspector у кнопок нажмите «Изменить меню…», у ListBox — «Изменить элементы…». Отдельное окно позволяет добавлять, удалять, переименовывать и перемещать элементы. Для меню также доступны IsEnabled пункта и «Настроить действие в Logic…». Изменения применяются кнопкой «Применить»; «Отмена» оставляет исходную коллекцию.

ListBox поддерживает статические Items, SelectedIndex, SelectedItem, SelectionMode, ItemsSource, BindingSource и attached-свойства `ScrollViewer.HorizontalScrollBarVisibility` / `VerticalScrollBarVisibility`. Поле ItemTemplate выбирает путь Text-привязки внутри настоящего `DataTemplate`, а не добавляет вымышленное Avalonia-свойство.

Русские описания остаются tooltip существующих строк Inspector. Механизм избранных свойств сохранён.

Обычный сценарий:

1. Перетащите компонент из Toolbox → «Стандартные» на форму.
2. Настройте оформление в Inspector и откройте редактор Flyout или Items.
3. Для основной части SplitButton выберите саму кнопку в Logic; для пункта используйте кнопку настройки действия в редакторе меню.
4. Для ListBox выберите статические Items либо BindingSource и поле отображения ItemTemplate.
5. Откройте обычный Preview или AXAML Preview, затем экспортируйте проект. Внешнюю конфигурацию БД задавайте способом, описанным в README.generated.md соответствующего экспорта.

## BindingSource и данные

ListBox выбирает источник через существующий BindingSource editor. При экспорте используются имеющиеся row DTO, ObservableCollection, SelectedItem и SQL Server / PostgreSQL loaders. Для ListBox без DataGrid тоже создаётся нужный ViewModel и подключается DataContext.

Canvas и оба Preview загружают ListBox через общий `PreviewRowsLoader`, но с отключёнными demo/sample fallback в копии источника. Пустой реальный результат остаётся пустым; ошибка отображается в Preview. Кнопка «Обновить данные» в Inspector повторно читает источник. Загрузка асинхронная и отменяется при закрытии окна.

Проверен настоящий DLL-источник с 12 различными строками: исходные имена и количество сохранены в обоих Preview, даже при включённых demo-флагах источника. Отдельно проверена таблица с нулём строк. PostgreSQL проверен на недоступном локальном порту: оба Preview и скомпилированный ViewModel показывают ошибку с пустой коллекцией, без sample rows. Это проверка отказа, а не успешной работы с сервером PostgreSQL.

## Preview, экспорт и импорт

В обычном Preview используются native controls; AXAML Preview загружает экспортированную разметку. Для новых компонентов в AXAML Preview сохраняются runtime-имена, по которым подключаются существующие Logic и BindingSource. Старый двухаргументный API `GeneratedAxamlService.Create` сохранён; сохранение имён включается для форм с новыми компонентами.

AXAML содержит реальные теги, Flyout, статические x:String или ItemsSource/DataTemplate. C# содержит независимые обработчики основной кнопки и пунктов меню. Дополнительные пакеты для трёх компонентов не требуются. NuGet зависимости BindingSource выбирает существующий export pipeline.

Проверен отдельный процесс приложения, созданного настоящим export pipeline: build, запуск, основная часть SplitButton, стрелка и Flyout, действие пункта с закрытием меню, открытие DropDownButton и выбор Gamma в ListBox. Проверка не ограничена загрузкой generated assembly внутри Designer.

Импорт DropDownButton/SplitButton добавлен в существующие metadata/projection. ListBox сохраняет Phase 5. При изменении Width импортированного DropDownButton проверено точное совпадение остального AXAML, включая комментарий, неизвестный атрибут, binding и вложенные Flyout/MenuFlyout. Редактирование поддержанных scalar properties использует существующие patches; полная регенерация исходного AXAML не выполняется.

## Проверки

Основной набор содержит шесть новых сценариев:

| Сценарий | Что проверяет |
| --- | --- |
| ToolboxStandardItemsExport | Toolbox drop всех трёх типов, native canvas, Inspector, размеры/выделение, коллекции, JSON, оба Preview, Logic, AXAML/C#, compiled window, отдельный generated process |
| ToolboxStandardListBindingExport | 12 реальных DLL-строк в обоих Preview, ItemsSource/DataTemplate, compiled ObservableCollection и выбор объекта |
| ToolboxStandardMultiFormExport | Две формы с независимыми контролами и обработчиками, generated project build |
| ToolboxStandardRoundTrip | Импорт трёх типов, native menu-button projection и минимальное изменение исходного AXAML |
| ToolboxStandardEmptyListExport | Пустой реальный источник в обоих Preview и generated project build |
| ToolboxStandardPostgresListExport | Provider loader, NuGet selection, отсутствие секрета, ошибка без fake rows в обоих Preview и compiled ViewModel |

Результаты новых сценариев: **6/6**. Выбранные существующие regression scenarios: **37/37**. Отдельные Eremex regression scenarios: **3/3**. Всего **46 различных smoke scenarios**. Проверены обычные контролы, Inspector/tooltip/избранное, Logic, DLL/SQL fallback, DataGrid, Phase 4/5 Import, Multi Form, Preview и export build.

Полный `FormDesigner.sln`: **0 ошибок**, одно прежнее предупреждение **NU1603** о VSSDK analyzer. Упаковка VSIX прошла; для Windows IsolatedStorage упаковщика потребовался запуск сборки вне песочницы. Сборка smoke runner с Designer/VsHost: **0 ошибок, 0 предупреждений**. PostgreSQL offline/contract checks: **20/20**, real-server integration — **SKIP**.

Это целевая регрессия, а не весь smoke-набор репозитория. После проверки добавлена очистка Logic пунктов при удалении кнопки; повторный прогон затронутого сценария после JSON reload, обоих Preview и отдельного generated process — **1/1**, лог `final-items-fix.log`.

Команды запуска новых проверок:

```powershell
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -m:1 --verbosity minimal
$env:SMOKE_SCENARIO_FILTER = 'ToolboxStandard'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\toolbox-standard\manual
Remove-Item Env:SMOKE_SCENARIO_FILTER
```

Логи текущей проверки: `artifacts/toolbox-standard/final.log`, `final-items-fix.log`, `regression.log`, `eremex.log`, `postgres-checks.log`, `solution-build.log`. Отдельный процесс записывает `standalone-process.txt`. Сводка — `validation.json`. Изображение реального окна редактора проверено в `items-editor.png`.

Runner завершился с exit code 0. Часть успешных временных build-артефактов не удалена автоматической очисткой из-за загруженных DLL; такие записи `ARTIFACT_CLEANUP_FAILED` не являются падениями сценариев.

## Ограничения

- Редактор коллекций JSON Designer поддерживает плоские текстовые элементы и пункты меню. Редактора произвольных templates, вложенных меню и произвольного содержимого Flyout нет.
- В импортированном AXAML коллекции, bindings, styles и сложное содержимое сохраняются в исходном файле. Редактор коллекций для них не включён; доступные scalar properties редактируются точечными patches.
- Внешний путь ItemsSource требует подходящего DataContext приложения. Designer не создаёт внешние объекты по имени пути.
- Если один BindingSource одновременно используется DataGrid с явно выбранным Demo mode, экспортируемый ListBox не получает demo-коллекцию и остаётся пустым. Для реальных данных отключите Demo или выделите ListBox отдельный реальный источник.
- Существующий экспорт DLL-источника не создаёт переносимый runtime loader этой DLL. Его generated коллекция начинается пустой; проверены BindingSource/ItemsSource и выбор добавленного реального объекта. SQL Server/PostgreSQL используют существующие loaders.
- Доступного сервера SQL Server/PostgreSQL нет. Успешное чтение из живой БД и работа внешней конфигурации с таким сервером здесь не проверены.
- Интерактивность автоматизированно проверена через native template parts и события в Windows. Ручной пользовательский mouse/keyboard smoke и Linux/macOS runtime не выполнялись.

## Файлы расширения Toolbox

Новые:

- `Models/StandardItemsProperties.cs`
- `DesignerSystem/BuiltIn/BuiltInControlRegistrar.StandardItems.cs`
- `DesignerSystem/BuiltIn/StandardItemsControlFactory.cs`
- `DesignerSystem/Binding/StandardListBoxRowsLoader.cs`
- `ViewModels/MainWindowViewModel.StandardItems.cs`
- `Views/MainWindow.StandardItems.cs`
- `Views/PreviewWindow.StandardItems.cs`
- `Views/StandardItemsEditorWindow.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.StandardItems.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.StandardItemsProcess.cs`
- `Docs/StandardToolboxControls.md`

Точки подключения в существующих файлах:

- `Models/DesignerControlTypes.cs`, `Models/ExportedAxamlPreviewModel.cs`
- `DesignerSystem/BuiltIn/BuiltInControlRegistrar.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`, `AxamlImportService.cs`, `AxamlPhase4Projection.cs`
- `Services/GeneratedAxamlService.cs`, `Services/PreviewRuntimeService.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `Views/PreviewWindow.axaml.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
- `README.md`, `smoke-tests/README.md`

В рабочем дереве также остаются изменения предыдущих этапов PostgreSQL. Они не относятся к списку этого расширения.
