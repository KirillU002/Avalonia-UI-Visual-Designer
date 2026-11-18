# Phase 7A. Единый Toolbox и распознавание AXAML

Работа начата от коммита 1cfb9c1 после Phase 6C. Проверены отчёты 5E, 5F, 5G, 6A, 6B, 6C, 4C и 4F, фактические точки запуска, общий DesignerDocumentSession, регистрация дескрипторов, загрузка плагинов, importer, source map и обе фабрики представлений.

## Причина различия между хостами

До изменений Standalone показывал 64 карточки, VsHost — 58. Все 58 встроенных дескрипторов совпадали. При запуске Standalone после встроенной регистрации вызывался `PluginLoader.LoadFromFolder`. Точка входа VsHost создавала реестр со встроенными дескрипторами и провайдером данных, пропуская загрузчик плагинов. Общие DesignerSurface, Toolbox и Metadata Engine получали разные входные наборы. Это воспроизведено в отдельных процессах до изменения кода.

| Источник | Standalone до | VsHost до | Standalone после | VsHost из VSIX после |
|---|---:|---:|---:|---:|
| Встроенные | 58 | 58 | 58 | 58 |
| Demo.DesignerPlugin | 3 | 0 | 3 | 3 |
| MinimalDesignerPlugin | 1 | 0 | 1 | 1 |
| Eremex.DesignerPlugin | 2 | 0 | 2 | 2 |
| **Всего** | **64** | **58** | **64** | **64** |

Был и отдельный дефект поставки. При Release-сборке solution необязательный проект Eremex вне solution пересобирался в Debug. В VSIX попадала прежняя Release DLL. Карточки при этом совпадали, но обе записи Eremex теряли сведения о нативном типе. Теперь FormDesigner.csproj передаёт дочерним проектам конфигурацию родителя.

## Реестр и поставка

Обе точки входа используют `DesignerRegistry.CreateForHost`. Исходные значения, тип контейнера, свойства, фабрика Preview и AXAML writer принадлежат существующему `IControlDescriptor`. Необязательный контракт плагина предоставляет имя нативного типа и сборки. За допустимость вложения в JSON отвечают descriptor и layout, в AXAML дополнительно проверяются Content/Children/Items, родитель, поддерево и source map. Подсказки и Drop используют одну политику вставки с учётом соответствия Group → Canvas.

Автотест запускает обычную инициализацию Standalone из копии Release и VsHost из распакованного VSIX. После инициализации он читает `MainWindow.DataContext`, окончательные `ToolboxGroups` и реестр. Сравниваются порядок, заголовки, категории, описания, иконки, свойства, возможности вставки, видимость, провайдеры, метаданные и результаты пяти поисков. Каждый ID уникален. Поиск Grid нашёл шесть одинаковых карточек, включая Demo и Eremex. В каждом хосте успешно зарегистрированы три плагина. На работающих экземплярах VM выполнено открытие AXAML с Grid и Button. Повторное чтение Toolbox в режиме импорта подтвердило те же 64 карточки, их порядок, категории, иконки и подсказки Drop. [Снимки до и после открытия AXAML](../artifacts/universal-phase7a/release-validation/active-document-parity/).

Metadata Engine обнаружил 146 типов в проверенном наборе Avalonia assemblies в каждом хосте. 64 — размер Toolbox данной поставки с плагинами. Это другой показатель, чем покрытие стандартной библиотеки в Phase 5. Canvas соответствует карточке Group. DockPanel распознаётся по своему CLR-типу и представлен режимом Dock у контейнера в JSON. Отдельных одноимённых карточек для них нет.

## Каталог по порядку Toolbox

В столбце «До» указана видимость Standalone/VsHost, после изменения она **да/да для всех строк**. Полное имя содержит TypeName и CLR namespace. PG — число уникальных ключей строк Inspector при выборе карточки. Полные свойства дескриптора, описание и число свойств framework metadata находятся в JSON-снимке.

В столбце «Designer/Legacy» знак + означает, что фабрика вернула ожидаемый тип или дерево с таким дочерним типом. Обозначение «обол.» означает другое представление. Это проверка типа, без претензии на проверку каждого его свойства. «Импорт» обозначает адаптер проекции либо чтение PE-метаданных. Вставка также зависит от контейнера. Runtime подтверждён поиском нативного типа у именованного элемента или его потомка в запущенном экспортированном приложении. «Блок» — отказ от нативного экспорта с runtime-ссылками.

| ComponentId | Полное имя типа | Assembly | Группа / категория | Название · иконка | До | PG | Designer/Legacy | Импорт | Вставка | Runtime |
|---|---|---|---|---|---|---:|---|---|---|---|

| Button | Avalonia.Controls.Button | Avalonia.Controls | Стандартные / Ввод | Button · AV | да/да | 57 | +/+ | адаптер | да | да |
| CheckBox | Avalonia.Controls.CheckBox | Avalonia.Controls | Стандартные / Ввод | CheckBox · AV | да/да | 57 | обол./+ | адаптер | да | да |
| TextBox | Avalonia.Controls.TextBox | Avalonia.Controls | Стандартные / Ввод | TextBox · AV | да/да | 64 | +/+ | адаптер | да | да |
| DataGrid | Avalonia.Controls.DataGrid | Avalonia.Controls.DataGrid | Стандартные / Данные | DataGrid · AV | да/да | 62 | +/+ | адаптер | нет | да |
| Carousel | Avalonia.Controls.Carousel | Avalonia.Controls | Стандартные / Коллекции | Carousel · ↔ | да/да | 60 | +/+ | адаптер | нет | да |
| ComboBox | Avalonia.Controls.ComboBox | Avalonia.Controls | Стандартные / Коллекции | ComboBox · AV | да/да | 59 | +/+ | адаптер | нет | да |
| ItemsControl | Avalonia.Controls.ItemsControl | Avalonia.Controls | Стандартные / Коллекции | ItemsControl · Ic | да/да | 57 | +/+ | адаптер | нет | да |
| Menu | Avalonia.Controls.Menu | Avalonia.Controls | Стандартные / Коллекции | Menu · AV | да/да | 58 | +/+ | адаптер | нет | да |
| TabControl | Avalonia.Controls.TabControl | Avalonia.Controls | Стандартные / Коллекции | TabControl · AV | да/да | 59 | +/+ | адаптер | нет | да |
| TabStrip | Avalonia.Controls.Primitives.TabStrip | Avalonia.Controls | Стандартные / Коллекции | TabStrip · ▤ | да/да | 59 | +/+ | адаптер | нет | да |
| TreeView | Avalonia.Controls.TreeView | Avalonia.Controls | Стандартные / Коллекции | TreeView · AV | да/да | 59 | +/+ | адаптер | нет | да |
| TextBlock | Avalonia.Controls.TextBlock | Avalonia.Controls | Стандартные / Контент | TextBlock · AV | да/да | 61 | +/+ | адаптер | да | да |
| Border | Avalonia.Controls.Border | Avalonia.Controls | Стандартные / Макет | Border · AV | да/да | 53 | +/+ | адаптер | да | да |
| WrapPanel | Avalonia.Controls.WrapPanel | Avalonia.Controls | Стандартные / Макет | Flex Layout · AV | да/да | 52 | обол./обол. | адаптер | нет | да |
| Grid | Avalonia.Controls.Grid | Avalonia.Controls | Стандартные / Макет | Grid Layout · AV | да/да | 51 | +/+ | адаптер | да | да |
| StackPanel | Avalonia.Controls.StackPanel | Avalonia.Controls | Стандартные / Макет | Stack Layout · AV | да/да | 53 | +/+ | адаптер | да | да |
| Image | Avalonia.Controls.Image | Avalonia.Controls | Стандартные / Медиа | Image · AV | да/да | 51 | +/+ | адаптер | нет | да |
| AccessText | Avalonia.Controls.Primitives.AccessText | Avalonia.Controls | Стандартные | AccessText · _A | да/да | 59 | +/+ | адаптер | нет | да |
| Calendar | Avalonia.Controls.Calendar | Avalonia.Controls | Стандартные | Calendar · AV | да/да | 63 | +/+ | адаптер | нет | да |
| DatePicker | Avalonia.Controls.DatePicker | Avalonia.Controls | Стандартные | DatePicker · AV | да/да | 63 | +/+ | адаптер | нет | да |
| DropDownButton | Avalonia.Controls.DropDownButton | Avalonia.Controls | Стандартные | DropDownButton · AV | да/да | 61 | +/+ | адаптер | нет | да |
| Label | Avalonia.Controls.Label | Avalonia.Controls | Стандартные | Label · Lb | да/да | 58 | +/+ | адаптер | нет | да |
| ListBox | Avalonia.Controls.ListBox | Avalonia.Controls | Стандартные | ListBox · AV | да/да | 66 | +/+ | адаптер | нет | да |
| NumericUpDown | Avalonia.Controls.NumericUpDown | Avalonia.Controls | Стандартные | NumericUpDown · AV | да/да | 64 | +/+ | адаптер | нет | да |
| ProgressBar | Avalonia.Controls.ProgressBar | Avalonia.Controls | Стандартные | ProgressBar · AV | да/да | 60 | +/+ | адаптер | нет | да |
| RadioButton | Avalonia.Controls.RadioButton | Avalonia.Controls | Стандартные | RadioButton · AV | да/да | 62 | +/+ | адаптер | нет | да |
| SelectableTextBlock | Avalonia.Controls.SelectableTextBlock | Avalonia.Controls | Стандартные | SelectableTextBlock · I↔ | да/да | 61 | +/+ | адаптер | нет | да |
| Separator | Avalonia.Controls.Separator | Avalonia.Controls | Стандартные | Separator · AV | да/да | 54 | +/+ | адаптер | нет | да |
| Slider | Avalonia.Controls.Slider | Avalonia.Controls | Стандартные | Slider · AV | да/да | 63 | +/+ | адаптер | нет | да |
| SplitButton | Avalonia.Controls.SplitButton | Avalonia.Controls | Стандартные | SplitButton · AV | да/да | 61 | +/+ | адаптер | нет | да |
| TimePicker | Avalonia.Controls.TimePicker | Avalonia.Controls | Стандартные | TimePicker · AV | да/да | 57 | +/+ | адаптер | нет | да |
| ToggleSwitch | Avalonia.Controls.ToggleSwitch | Avalonia.Controls | Стандартные | ToggleSwitch · AV | да/да | 60 | +/+ | адаптер | нет | да |
| Group | Avalonia.Controls.Canvas | Avalonia.Controls | Стандартные / Структура | Group · AV | да/да | 51 | обол./обол. | Canvas | да | да |
| CalendarDatePicker | Avalonia.Controls.CalendarDatePicker | Avalonia.Controls | Ввод | CalendarDatePicker · ▦ | да/да | 65 | +/+ | адаптер | нет | да |
| MaskedTextBox | Avalonia.Controls.MaskedTextBox | Avalonia.Controls | Ввод | MaskedTextBox · #_ | да/да | 75 | +/+ | адаптер | нет | да |
| RepeatButton | Avalonia.Controls.RepeatButton | Avalonia.Controls | Ввод | RepeatButton · ↻ | да/да | 63 | +/+ | адаптер | нет | да |
| ScrollBar | Avalonia.Controls.Primitives.ScrollBar | Avalonia.Controls | Ввод | ScrollBar · ↕ | да/да | 61 | +/+ | адаптер | нет | да |
| ToggleButton | Avalonia.Controls.Primitives.ToggleButton | Avalonia.Controls | Ввод | ToggleButton · ☑ | да/да | 63 | +/+ | адаптер | нет | да |
| ButtonSpinner | Avalonia.Controls.ButtonSpinner | Avalonia.Controls | Контейнеры | ButtonSpinner · ↕ | да/да | 60 | +/+ | адаптер | нет | да |
| ContentControl | Avalonia.Controls.ContentControl | Avalonia.Controls | Контейнеры | ContentControl · Cc | да/да | 56 | +/+ | адаптер | нет | да |
| Expander | Avalonia.Controls.Expander | Avalonia.Controls | Контейнеры | Expander · ⌄ | да/да | 59 | +/+ | адаптер | нет | да |
| LayoutTransformControl | Avalonia.Controls.LayoutTransformControl | Avalonia.Controls | Контейнеры | LayoutTransformControl · ⤢ | да/да | 45 | +/+ | адаптер | нет | да |
| ReversibleStackPanel | Avalonia.Controls.ReversibleStackPanel | Avalonia.Controls | Контейнеры | ReversibleStackPanel · ⇅ | да/да | 46 | +/+ | адаптер | нет | да |
| ScrollViewer | Avalonia.Controls.ScrollViewer | Avalonia.Controls | Контейнеры | ScrollViewer · ↕ | да/да | 59 | +/+ | адаптер | нет | да |
| TransitioningContentControl | Avalonia.Controls.TransitioningContentControl | Avalonia.Controls | Контейнеры | TransitioningContentControl · ↝ | да/да | 58 | +/+ | адаптер | нет | да |
| GridSplitter | Avalonia.Controls.GridSplitter | Avalonia.Controls | Макет | GridSplitter · ⋮ | да/да | 59 | +/+ | адаптер | нет | да |
| UniformGrid | Avalonia.Controls.Primitives.UniformGrid | Avalonia.Controls | Макет | UniformGrid · ▦ | да/да | 46 | +/+ | адаптер | нет | да |
| Viewbox | Avalonia.Controls.Viewbox | Avalonia.Controls | Макет | Viewbox · ⤢ | да/да | 44 | +/+ | адаптер | нет | да |
| HyperlinkButton | Avalonia.Controls.HyperlinkButton | Avalonia.Controls | Навигация | HyperlinkButton · ↗ | да/да | 63 | +/+ | адаптер | нет | да |
| Arc | Avalonia.Controls.Shapes.Arc | Avalonia.Controls | Фигуры | Arc · ◜ | да/да | 50 | +/+ | адаптер | нет | да |
| Ellipse | Avalonia.Controls.Shapes.Ellipse | Avalonia.Controls | Фигуры | Ellipse · ○ | да/да | 50 | +/+ | адаптер | нет | да |
| Line | Avalonia.Controls.Shapes.Line | Avalonia.Controls | Фигуры | Line · ╱ | да/да | 52 | +/+ | адаптер | нет | да |
| Path | Avalonia.Controls.Shapes.Path | Avalonia.Controls | Фигуры | Path · ∿ | да/да | 51 | +/+ | адаптер | нет | да |
| PathIcon | Avalonia.Controls.PathIcon | Avalonia.Controls | Фигуры | PathIcon · ◇ | да/да | 55 | +/+ | адаптер | нет | да |
| Polygon | Avalonia.Controls.Shapes.Polygon | Avalonia.Controls | Фигуры | Polygon · △ | да/да | 49 | +/+ | адаптер | нет | да |
| Polyline | Avalonia.Controls.Shapes.Polyline | Avalonia.Controls | Фигуры | Polyline · ⌁ | да/да | 49 | +/+ | адаптер | нет | да |
| Rectangle | Avalonia.Controls.Shapes.Rectangle | Avalonia.Controls | Фигуры | Rectangle · ▭ | да/да | 52 | +/+ | адаптер | нет | да |
| Sector | Avalonia.Controls.Shapes.Sector | Avalonia.Controls | Фигуры | Sector · ◔ | да/да | 50 | +/+ | адаптер | нет | да |
| Eremex.DataGridControl | Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl | Eremex.Avalonia.Controls | Eremex / Data | DataGridControl · EMX | да/нет | 59 | +/+ | PE | нет | да |
| Eremex.TextEditor | Eremex.AvaloniaUI.Controls.Editors.TextEditor | Eremex.Avalonia.Controls | Eremex / Editors | TextEditor · EMX | да/нет | 40 | +/+ | PE | нет | да |
| Demo.GridControl | DemoDesignerPlugin.Controls.DemoGridControl | DemoDesignerPlugin | Пользовательские плагины / Demo Plugins | Demo GridControl · PL | да/нет | 35 | +/+ | PE | нет | блок |
| Minimal.HelloCard | MinimalDesignerPlugin.Controls.HelloCard | MinimalDesignerPlugin | Пользовательские плагины / SDK Examples | Hello Card · PL | да/нет | 32 | +/+ | PE | нет | блок |
| Demo.DevButton | DemoDesignerPlugin.Controls.DemoDevButton | DemoDesignerPlugin | Пользовательские плагины / Ввод | Кнопка-карточка · PL | да/нет | 33 | +/+ | PE | нет | блок |
| Demo.TreeList | DemoDesignerPlugin.Controls.DemoTreeList | DemoDesignerPlugin | Пользовательские плагины / Данные | TreeList · PL | да/нет | 41 | +/+ | PE | нет | блок |

## Инвентарь Metadata Engine и адаптеров импорта

В четырёх сборках обнаружено 146 типов Control, включая 8 абстрактных. Полностью квалифицированную CLR-ссылку разрешают 146/146, стандартный XML URI — 145/146. Для Avalonia.Controls.Remote.RemoteWidget требуется квалификация. У 67 типов есть явный source adapter. Это наличие адаптера, его применимость к конкретному узлу зависит от контекста.

<details><summary>Список типов по сборкам и наличию source adapter</summary>

**Avalonia.Controls · есть адаптер · 66**. `Avalonia.Controls.Border`, `Avalonia.Controls.Button`, `Avalonia.Controls.ButtonSpinner`, `Avalonia.Controls.Calendar`, `Avalonia.Controls.CalendarDatePicker`, `Avalonia.Controls.Canvas`, `Avalonia.Controls.Carousel`, `Avalonia.Controls.CheckBox`, `Avalonia.Controls.ComboBox`, `Avalonia.Controls.ComboBoxItem`, `Avalonia.Controls.ContentControl`, `Avalonia.Controls.ContextMenu`, `Avalonia.Controls.DatePicker`, `Avalonia.Controls.DockPanel`, `Avalonia.Controls.DropDownButton`, `Avalonia.Controls.Expander`, `Avalonia.Controls.Grid`, `Avalonia.Controls.GridSplitter`, `Avalonia.Controls.HyperlinkButton`, `Avalonia.Controls.Image`, `Avalonia.Controls.ItemsControl`, `Avalonia.Controls.Label`, `Avalonia.Controls.LayoutTransformControl`, `Avalonia.Controls.ListBox`, `Avalonia.Controls.ListBoxItem`, `Avalonia.Controls.MaskedTextBox`, `Avalonia.Controls.Menu`, `Avalonia.Controls.MenuItem`, `Avalonia.Controls.NumericUpDown`, `Avalonia.Controls.Panel`, `Avalonia.Controls.PathIcon`, `Avalonia.Controls.Primitives.AccessText`, `Avalonia.Controls.Primitives.ScrollBar`, `Avalonia.Controls.Primitives.TabStrip`, `Avalonia.Controls.Primitives.TabStripItem`, `Avalonia.Controls.Primitives.ToggleButton`, `Avalonia.Controls.Primitives.UniformGrid`, `Avalonia.Controls.ProgressBar`, `Avalonia.Controls.RadioButton`, `Avalonia.Controls.RepeatButton`, `Avalonia.Controls.ReversibleStackPanel`, `Avalonia.Controls.ScrollViewer`, `Avalonia.Controls.SelectableTextBlock`, `Avalonia.Controls.Separator`, `Avalonia.Controls.Shapes.Arc`, `Avalonia.Controls.Shapes.Ellipse`, `Avalonia.Controls.Shapes.Line`, `Avalonia.Controls.Shapes.Path`, `Avalonia.Controls.Shapes.Polygon`, `Avalonia.Controls.Shapes.Polyline`, `Avalonia.Controls.Shapes.Rectangle`, `Avalonia.Controls.Shapes.Sector`, `Avalonia.Controls.Slider`, `Avalonia.Controls.SplitButton`, `Avalonia.Controls.StackPanel`, `Avalonia.Controls.TabControl`, `Avalonia.Controls.TabItem`, `Avalonia.Controls.TextBlock`, `Avalonia.Controls.TextBox`, `Avalonia.Controls.TimePicker`, `Avalonia.Controls.ToggleSwitch`, `Avalonia.Controls.TransitioningContentControl`, `Avalonia.Controls.TreeView`, `Avalonia.Controls.TreeViewItem`, `Avalonia.Controls.Viewbox`, `Avalonia.Controls.WrapPanel`.

**Avalonia.Controls · только распознавание · 63**. `Avalonia.Controls.AutoCompleteBox`, `Avalonia.Controls.Chrome.CaptionButtons`, `Avalonia.Controls.Chrome.TitleBar`, `Avalonia.Controls.Control`, `Avalonia.Controls.DataValidationErrors`, `Avalonia.Controls.DatePickerPresenter`, `Avalonia.Controls.Decorator`, `Avalonia.Controls.Embedding.EmbeddableControlRoot`, `Avalonia.Controls.ExperimentalAcrylicBorder`, `Avalonia.Controls.FlyoutPresenter`, `Avalonia.Controls.IconElement`, `Avalonia.Controls.MenuBase`, `Avalonia.Controls.MenuFlyoutPresenter`, `Avalonia.Controls.NativeControlHost`, `Avalonia.Controls.NativeMenuBar`, `Avalonia.Controls.Notifications.NotificationCard`, `Avalonia.Controls.Notifications.WindowNotificationManager`, `Avalonia.Controls.Presenters.ContentPresenter`, `Avalonia.Controls.Presenters.ItemsPresenter`, `Avalonia.Controls.Presenters.ScrollContentPresenter`, `Avalonia.Controls.Presenters.TextPresenter`, `Avalonia.Controls.Primitives.AdornerLayer`, `Avalonia.Controls.Primitives.CalendarButton`, `Avalonia.Controls.Primitives.CalendarDayButton`, `Avalonia.Controls.Primitives.CalendarItem`, `Avalonia.Controls.Primitives.ChromeOverlayLayer`, `Avalonia.Controls.Primitives.DateTimePickerPanel`, `Avalonia.Controls.Primitives.HeaderedContentControl`, `Avalonia.Controls.Primitives.HeaderedItemsControl`, `Avalonia.Controls.Primitives.HeaderedSelectingItemsControl`, `Avalonia.Controls.Primitives.LightDismissOverlayLayer`, `Avalonia.Controls.Primitives.OverlayLayer`, `Avalonia.Controls.Primitives.OverlayPopupHost`, `Avalonia.Controls.Primitives.PickerPresenterBase`, `Avalonia.Controls.Primitives.Popup`, `Avalonia.Controls.Primitives.PopupRoot`, `Avalonia.Controls.Primitives.RangeBase`, `Avalonia.Controls.Primitives.SelectingItemsControl`, `Avalonia.Controls.Primitives.TemplatedControl`, `Avalonia.Controls.Primitives.TextSelectionHandle`, `Avalonia.Controls.Primitives.TextSelectorLayer`, `Avalonia.Controls.Primitives.Thumb`, `Avalonia.Controls.Primitives.Track`, `Avalonia.Controls.Primitives.VisualLayerManager`, `Avalonia.Controls.RefreshContainer`, `Avalonia.Controls.RefreshVisualizer`, `Avalonia.Controls.RelativePanel`, `Avalonia.Controls.Remote.RemoteWidget`, `Avalonia.Controls.Shapes.Shape`, `Avalonia.Controls.Spinner`, `Avalonia.Controls.SplitView`, `Avalonia.Controls.ThemeVariantScope`, `Avalonia.Controls.TickBar`, `Avalonia.Controls.TimePickerPresenter`, `Avalonia.Controls.ToggleSplitButton`, `Avalonia.Controls.ToolTip`, `Avalonia.Controls.TopLevel`, `Avalonia.Controls.UserControl`, `Avalonia.Controls.VirtualizingCarouselPanel`, `Avalonia.Controls.VirtualizingPanel`, `Avalonia.Controls.VirtualizingStackPanel`, `Avalonia.Controls.Window`, `Avalonia.Controls.WindowBase`.

**Avalonia.Controls.ColorPicker · только распознавание · 5**. `Avalonia.Controls.ColorPicker`, `Avalonia.Controls.ColorView`, `Avalonia.Controls.Primitives.ColorPreviewer`, `Avalonia.Controls.Primitives.ColorSlider`, `Avalonia.Controls.Primitives.ColorSpectrum`.

**Avalonia.Controls.DataGrid · есть адаптер · 1**. `Avalonia.Controls.DataGrid`.

**Avalonia.Controls.DataGrid · только распознавание · 10**. `Avalonia.Controls.DataGridCell`, `Avalonia.Controls.DataGridColumnHeader`, `Avalonia.Controls.DataGridRow`, `Avalonia.Controls.DataGridRowGroupHeader`, `Avalonia.Controls.Primitives.DataGridCellsPresenter`, `Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter`, `Avalonia.Controls.Primitives.DataGridDetailsPresenter`, `Avalonia.Controls.Primitives.DataGridFrozenGrid`, `Avalonia.Controls.Primitives.DataGridRowHeader`, `Avalonia.Controls.Primitives.DataGridRowsPresenter`.

**Avalonia.Controls.ItemsRepeater · только распознавание · 1**. `Avalonia.Controls.ItemsRepeater`.

</details>


## Распознавание и сохранность исходного файла

Доверенный индекс строится лениво по реальным XML namespace mappings установленных сборок. Для типа проверяются URI, CLR namespace и явно заданная assembly. Для property element дополнительно проверяются владелец и член. `using:` поддерживается для индексированных пространств имён. При неизвестной assembly или отсутствии контекста локальной сборки тип остаётся неразрешённым. Явный сброс `xmlns=""` отличим от старых документов Designer без пространства имён.

| Capability | Фактическое условие |
|---|---|
| Recognized | Есть однозначные сведения о CLR-типе либо владельце и члене property element. |
| Inspectable | Доступны исходные атрибуты. При наличии identity дополнительно видны тип, assembly, базовый класс и свойства. |
| Editable | Есть запись source map, разрешение на patch и поддерживаемое значение конкретного свойства. |
| Structural Editable | Структурная политика разрешила операцию в данном контейнере. |
| Previewable | Есть проверенная нативная проекция. Воспроизведение содержимого и Binding зависит от адаптера. |
| Exportable | Для типа есть соответствующий дескриптор и поддерживаемый нативный экспорт JSON-проекта. |
| Opaque | Исходное поддерево сохранено вне редактируемой проекции, в том числе если отдельные типы внутри известны. |

В смешанном fixture прежний адаптер определял тип для **12/32** узлов. После подключения метаданных тестовой DLL и Eremex известен тип или владелец свойства для **29/32**. Количество узлов в source map выросло с **9 до 11**. Для `u:Button`, `u:DataGridTextColumn` и `bad:Button` сохранены XML имя, URI, атрибуты и место в дереве. Отдельно проверены xmlns без значения и отсутствие подтверждённой локальной сборки.

**Реальный MainWindow.axaml.** Обеим версиям переданы идентичные байты файла из коммита 1cfb9c1. До изменений корневой Panel оставлял всю форму без визуальной проекции. После изменений число узлов source map выросло с **0 до 1521**. Во всём исходном дереве прежний адаптер определял CLR-тип для **2040/2556** элементов, включая содержимое непрозрачных поддеревьев. При обычном импорте без проектной DLL новый разрешитель определяет тип либо владельца свойства для **2549/2556** элементов. Семь узлов — экземпляры DesignerToolbox, DesignerSurface, DesignerPropertyInspector и PropertyGridTextEditor из проектных пространств имён. После явного подключения метаданных FormDesigner.dll результат составляет **2556/2556**. Число различных CLR-идентичностей выросло с 24 до 38. Это результат для конкретного файла, включая повторяющиеся теги и property elements.

**Стандартные компоненты.** Корневой Panel отображается нативным Panel с исходным порядком детей. Структурная вставка непосредственно в Panel не включена. DataGrid получает нативные DataGridTextColumn и DataGridCheckBoxColumn для проверенных типов, с буквальными заголовками. Пользовательские колонки, шаблоны, Binding и ItemsSource сохраняются в источнике. Для Image создаётся нативный Image. Импорт не загружает внешние изображения по URI. Shapes и PathIcon проверены на фактический класс.

**Пользовательские и сторонние типы.** Команда «Метаданные DLL…» читает PE metadata через существующую подсистему DLL Import. Доступны публичные типы, объявленный базовый класс, читаемые публичные экземплярные свойства и XML namespace mappings. Для установленных плагинов используются пути из реестра и runtime-манифеста. Тестовый custom control имеет конструктор, выбрасывающий исключение при вызове. Его тип и свойство CategoryCode успешно прочитаны. Для реальной сборки Eremex проверены vendor URI, имя Eremex.AvaloniaUI.Controls.Editors.TextEditor и отсутствие дополнительной runtime-загрузки сборки при чтении PE. Автоматического поиска всех NuGet, исполнения custom-контролов или загрузки Avalonia в devenv нет.

**Explorer и Inspector.** Переключатель «Исходный AXAML» раскрывает существующее syntax tree со всеми элементами, включая property elements, ресурсы, стили и вложенные DataTemplates. Узел из source map выбирает модельный компонент. Для остальных Inspector показывает исходные значения с экранированием, сведения о типе и причину ограничения. После ACK старый узел отвергается, подключённые сборки метаданных сохраняются в пределах сессии.

**Drag & Drop и Undo.** Карточка остаётся видимой при запрете вставки. Для Grid, Canvas, StackPanel, DockPanel и WrapPanel тест вызывает команду Toolbox Drop. В JSON используются реальные размеры контейнера после Measure/Arrange. Для импортированного файла проверены положительный случай с Button и отказ для DataGrid, включая фактический patch. Для новых адаптеров скалярное значение с исходным атрибутом можно изменить. Если модель не различает отсутствие атрибута и локальное значение по умолчанию, Inspector блокирует добавление такого скаляра. Для явно заданного Width проверены редактирование, ACK, точное Undo и Redo. На текущем исходнике MainWindow.axaml дополнительно проверены пустой patch после открытия, замена ровно одного исходного span при редактировании Width кнопки и восстановление точного текста при Undo. комментарии, чужая колонка, Bindings и Resources сохранены.

## Preview и экспорт

Для всех 64 карточек получены строки PropertyGrid. Фабрика Designer вернула ожидаемый тип или содержащее его дерево для **61/64**, фабрика Legacy Preview — для **62/64**. CheckBox в Designer представлен Grid, в Legacy — нативным CheckBox. Для Group и WrapPanel обе фабрики возвращают Border. Это проверка фабрик отдельных компонентов. Раскладка контейнеров дополнительно проверена нативной layout-проекцией.

В экспортированном приложении обнаружены **60/60** заявленных нативных типов из 64 карточек. В выборку вошли Avalonia DataGrid, оба Eremex, Shapes и PathIcon. Проба обходит визуальное и логическое деревья запущенного окна. Два дополнительных проекта Phase 6C проверяют Grid и Canvas с данными, шаблонами и сменой темы.

Четыре дескриптора — Demo.DevButton, Demo.GridControl, Demo.TreeList и Minimal.HelloCard — указывают в разметке на свои design-time DLL. Поставляемой зависимости для нативного проекта у них нет. При включённых runtime-ссылках Export возвращает диагностику с ComponentId до публикации. Для каждого случая проверены папка и ZIP с контрольным прежним содержимым. Предусмотренный ранее режим placeholder остаётся отдельным режимом. Для импортированного файла с непрозрачной разметкой или неполной модельной проекцией диагностика предлагает сохранить исходный AXAML. Новых NuGet-зависимостей нет.

## Производительность
Release, один UI-поток. Для каждой операции выполнены два прогрева и 20 измерений. P95 — ближайший ранг, allocations — медиана прироста GC.GetAllocatedBytesForCurrentThread. Поиск измерен на заполненном Toolbox. Разрешение проверяет все узлы при уже построенном индексе. Preview включает построение дерева и Measure/Arrange без открытия окна. Измерения на MainWindow выполнены на текущем файле с интерфейсом Phase 7A, 2566 XML-элементов.

| Документ | Операция | Median, мс | P95, мс | Allocations, КиБ |
|---|---|---:|---:|---:|
| Phase7ARecognition.axaml | Registry | 13,14 | 17,98 | 1362,8 |
| Phase7ARecognition.axaml | Toolbox | 0,09 | 0,10 | 34,3 |
| Phase7ARecognition.axaml | Search | 0,03 | 0,05 | 8,8 |
| Phase7ARecognition.axaml | Import | 1,38 | 1,98 | 471,3 |
| Phase7ARecognition.axaml | Resolve | 0,06 | 0,07 | 16,9 |
| Phase7ARecognition.axaml | Inspector | 1,99 | 3,74 | 1339,1 |
| Phase7ARecognition.axaml | Preview | 1,34 | 1,75 | 284,6 |
| MainWindow.axaml | Import | 91,55 | 225,65 | 47428,5 |
| MainWindow.axaml | Resolve | 1,09 | 6,18 | 252,7 |
| MainWindow.axaml | Inspector | 2,40 | 8,65 | 1564,9 |
| MainWindow.axaml | Preview | 113,41 | 250,05 | 47046,0 |

**Запуск хостов.** Одинаковый измеритель работал с архивными и итоговыми DLL. Секундомер охватывает инициализацию Avalonia lifetime до создания главного окна с готовым Toolbox. По десять свежих PID на сочетание хоста и версии после отдельного прогрева, до/после чередовались. Измерение исключает запуск debugger и не охватывает ручную команду в Visual Studio. При десяти значениях P95 равен максимальному наблюдению.

| Хост | Версия | Карточек | Median, мс | P95, мс | Allocations, МиБ |
|---|---|---:|---:|---:|---:|
| standalone | до | 64 | 2639,33 | 2742,61 | 147,10 |
| standalone | после | 64 | 2694,07 | 2757,95 | 150,27 |
| vshost | до | 58 | 2691,30 | 2747,84 | 147,15 |
| vshost | после | 64 | 2732,81 | 2842,69 | 151,03 |

Медиана запуска Standalone выросла на 54,74 мс (2,07%), VsHost — на 41,52 мс (1,54%). Медианные выделения UI-потока выросли на 3,17 и 3,88 МиБ соответственно. В VsHost к инициализации добавилась загрузка трёх плагинов. Диапазоны десяти наблюдений до/после пересекаются у обоих хостов. Отдельной причины всей разницы у Standalone этот опыт не устанавливает. Данные оставлены как ограничение производительности. [Первичные измерения хостов](../artifacts/universal-phase7a/release-validation/performance/startup.json) · [измерения операций](../artifacts/universal-phase7a/release-validation/performance/samples.json).

## Регрессионные прогоны и память

| Проверка | Итог |
|---|---|
| Build Solution, MSBuild Release | Успешно. Предупреждение NU1603 VSSDK, в локальном NuGet используется Analyzers 17.7.22 вместо 17.7.20 |
| Phase 7A | **17/17** составных проверок AXAML, source map, границ редактирования, Explorer, Drop, Undo и безопасности экспорта |
| Живые регистрации Standalone / VsHost | **64/64** карточки и 5 одинаковых поисковых выдач |
| Phase 6C / 6B / 6A / 5G | **11/11**, **44/44**, **63/63**, **85/85** |
| PropertyGrid / Collections / Layout7 | **30/30**, **30/30**, **15/15** |
| AXAML Import 4A / Structural Editing 4C | **37/38** в первой матрице итоговых бинарников, отдельно 38/38. Structural Editing **85/85** |
| Broad Regression | **72/72** |
| VSIX/VsHost 4F / Named Pipes IPC | **15/15**, **4/4** |
| Generated Runtime | 60 нативных типов. Дополнительно Grid и Canvas с DataTemplates, реальными строками и сменой темы |
| SQL Server LocalDB | **47/47**. Отдельно прошли схема → Preview и запуск экспортированного проекта с реальными строками Customers/Orders |
| Preview Memory | **36/36 × 3**, разные PID: 58444, 53936, 20308 |

Во всех трёх финальных Memory Gate код завершения 0. NativeCapture не зафиксировал аварий и тайм-аутов. Во всех 132 записях MEMORY_GATE_INVENTORY поле AliveWindows равно нулю. Критерии и очистка таймера DataGrid из Phase 6B сохранены. Исторический native crash UI Automation остаётся UNRESOLVED. Live PostgreSQL не запускался, его проверки в общем наборе помечены CONTRACT.

В наборе распознавания проверяются все 18 классов входных данных из задания. Один смешанный документ объединяет стандартные контролы, панели, коллекции, DataGrid.Columns, Shapes, Icons, стили, ресурсы и вложенные DataTemplates. На тех же узлах проверяются qualified namespaces, чужая assembly, attached Grid.Row, исходные атрибуты и сложная Binding-строка. Отдельные тесты покрывают явный сброс пространства имён, корневой Panel и полное реальное окно. Эти сценарии не дают разрешения на правку произвольных шаблонов или создание пользовательских типов в Preview.

### Первые неуспешные результаты

- Первая сборка разметки завершилась AXN0002 из-за объявления XML namespace ниже корня. Объявление перенесено на Window. Следующая ошибка компиляции теста была связана с отсутствующим импортом пространства имён.
- На первом запуске смешанного fixture у DataGrid не восстановилось исходное имя. Подключена карта общих скалярных свойств с проверкой применимости.
- Два прогона показали, что после ACK Undo для первоначально отсутствующего Width создаёт явное значение 140. Введена проверка возможности редактирования отсутствующего legacy-скаляра на новых адаптерах. Проверяются оба исходных состояния, включая явно заданное значение, совпадающее с умолчанием конструктора.
- Первый тест пакета выявил отсутствие identity у обеих карточек Eremex при совпадающих ID. Устранён дефект конфигурации сборки. В первом полном экспорте каталога AXAML compiler не смог разрешить типы Demo. Теперь неподдерживаемый нативный экспорт останавливается до записи в Folder/ZIP, остальные 60 типов проверены в живом приложении.
- При переходе тестов Drop с прямого создания на обычную команду получено 10/15. Тест вызывал команду до измерения целевого контейнера. После Measure/Arrange используются координаты фактического контейнера. В первом профиле также обнаружилось, что при нулевой проекции MainWindow отсутствовал ожидаемый TextBlock. Добавлены нативная проекция корневого Panel и отдельный сценарий выбора исходного узла.
- На финальных бинарниках UNIVERSAL4A_Nested_ComboBox воспроизвёл зафиксированный в Phase 6B отказ. После открытия popup и предусмотренного существующим тестом интервала 80 мс ComboBoxItem.GetVisualRoot() вернул null. В тот же период выполнялись другие оконные проверки. Источник отказа не установлен. Сценарий и его тайминг сохранены. Результат отдельного запуска — 38/38; он не заменяет первую запись 37/38.
- В том числе в итоговом Broad-прогоне 72/72 очистка временного каталога выдала UnauthorizedAccessException для ToolboxStandardItemsExport.dll. Это предупреждение записано в журнале после подсчёта проверок. При повторной сборке зависимостей smoke runner задача Avalonia MSBuild получила буквальный шаблон **/*.paml и завершилась MSB4018. Первая ошибка сохранена. Итоговый smoke runner собран поверх уже собранной Release-поставки с BuildProjectReferences=false. SHA-256 FormDesigner.dll одинаков у Standalone, тестов и в payload VsHost: 821cb09b38f69ad656e4fc236585e6bca7c247b7e92db37279feba54f7eb82b1.

### Артефакты и воспроизведение

Пакет AvaloniaDesigner.VSIX версии 0.1.18, SHA-256 f88388627946aaa7282e1c397611b7d27b449f38edf961a8fffc8769a1ef78ab. Для измерения паритета сборка теста читает VM в процессе хоста. Named Pipes и ACK проверяются самостоятельными IPC-сценариями. Полные снимки содержат карточки, описания, свойства, все 146 CLR-типов, XML namespace capabilities и результаты поиска. [Standalone](../artifacts/universal-phase7a/release-validation/parity/standalone.json) · [VsHost из VSIX](../artifacts/universal-phase7a/release-validation/parity/vshost.json) · [первые прогоны](../artifacts/universal-phase7a/acceptance/) · [итоговые журналы](../artifacts/universal-phase7a/release-validation/).

После сборки Release команда TestPhase7AToolbox.ps1 принимает RunnerDirectory, StandaloneDirectory, Vsix и новый OutputDirectory. У smoke runner доступны режимы --phase7a-checks, --phase7a-views, --phase7a-profile и --phase7a-catalog с каталогом результата. Для последнего ProbeRuntime.ps1 запускает сгенерированное приложение с Phase7ACatalogProbe.cs.txt. [Тест поставки](../smoke-tests/UniversalMetadata/TestPhase7AToolbox.ps1) · [фикстура AXAML](../smoke-tests/UniversalMetadata/Phase7ARecognition.axaml) · [результаты по типам](../artifacts/universal-phase7a/release-validation/catalog/materialized.json).

## Итоговая оценка

**UNIFIED TOOLBOX & CONTROL RECOGNITION: READY WITH CONDITIONS**

| Показатель | Статус |
|---|---|
| STANDALONE TOOLBOX PARITY | PASS, 64 карточки |
| VSIX TOOLBOX PARITY | PASS, 64 карточки во VsHost из собранного VSIX |
| STANDARD CONTROL RECOGNITION | PASS для проверенного индекса, с отдельными ограничениями source adapters |
| CUSTOM CONTROL RECOGNITION | PARTIAL, чтение предоставленной DLL либо непрозрачный исходный узел |
| THIRD-PARTY CONTROL RECOGNITION | PARTIAL, DataGrid и Eremex подтверждены, прочие DLL требуют доступной metadata |
| INSTALLED VSIX VERIFICATION | NOT VERIFIED |

## Изменённые файлы

- [App.axaml.cs](../App.axaml.cs)
- [AvaloniaDesigner.VsHost/VsHostApp.axaml.cs](../AvaloniaDesigner.VsHost/VsHostApp.axaml.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlBlockerImpact.cs](../DesignerSystem/AxamlRoundTrip/AxamlBlockerImpact.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs](../DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlImportService.cs](../DesignerSystem/AxamlRoundTrip/AxamlImportService.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs](../DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlPropertyElementInfo.cs](../DesignerSystem/AxamlRoundTrip/AxamlPropertyElementInfo.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs](../DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlStructuralPolicy.cs](../DesignerSystem/AxamlRoundTrip/AxamlStructuralPolicy.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlSyntax.cs](../DesignerSystem/AxamlRoundTrip/AxamlSyntax.cs)
- [DesignerSystem/AxamlRoundTrip/AxamlTypeRecognition.cs](../DesignerSystem/AxamlRoundTrip/AxamlTypeRecognition.cs)
- [DesignerSystem/Binding/ReflectionBindingMetadataProvider.Axaml.cs](../DesignerSystem/Binding/ReflectionBindingMetadataProvider.Axaml.cs)
- [DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs](../DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs)
- [DesignerSystem/Infrastructure/DesignerRegistry.cs](../DesignerSystem/Infrastructure/DesignerRegistry.cs)
- [DesignerSystem/Infrastructure/PluginLoading.cs](../DesignerSystem/Infrastructure/PluginLoading.cs)
- [DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs](../DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs)
- [Docs/UnifiedToolboxAndControlRecognitionPhase7A.md](../Docs/UnifiedToolboxAndControlRecognitionPhase7A.md)
- [FormDesigner.csproj](../FormDesigner.csproj)
- [Models/ToolboxItem.cs](../Models/ToolboxItem.cs)
- [PluginContracts/DesignerContracts.cs](../PluginContracts/DesignerContracts.cs)
- [Plugins/DemoDesignerPlugin/Descriptors/DemoDevButtonDescriptor.cs](../Plugins/DemoDesignerPlugin/Descriptors/DemoDevButtonDescriptor.cs)
- [Plugins/DemoDesignerPlugin/Descriptors/DemoGridControlDescriptor.cs](../Plugins/DemoDesignerPlugin/Descriptors/DemoGridControlDescriptor.cs)
- [Plugins/DemoDesignerPlugin/Descriptors/DemoTreeListDescriptor.cs](../Plugins/DemoDesignerPlugin/Descriptors/DemoTreeListDescriptor.cs)
- [Plugins/EremexDesignerPlugin/Descriptors/EremexDataGridControlDescriptor.cs](../Plugins/EremexDesignerPlugin/Descriptors/EremexDataGridControlDescriptor.cs)
- [Plugins/EremexDesignerPlugin/Descriptors/EremexTextEditorDescriptor.cs](../Plugins/EremexDesignerPlugin/Descriptors/EremexTextEditorDescriptor.cs)
- [Plugins/MinimalDesignerPlugin/Descriptors/HelloCardDescriptor.cs](../Plugins/MinimalDesignerPlugin/Descriptors/HelloCardDescriptor.cs)
- [ViewModels/MainWindowViewModel.AxamlStructure.cs](../ViewModels/MainWindowViewModel.AxamlStructure.cs)
- [ViewModels/MainWindowViewModel.SourceExplorer.cs](../ViewModels/MainWindowViewModel.SourceExplorer.cs)
- [ViewModels/MainWindowViewModel.cs](../ViewModels/MainWindowViewModel.cs)
- [Views/DesignerToolbox.axaml](../Views/DesignerToolbox.axaml)
- [Views/MainWindow.axaml](../Views/MainWindow.axaml)
- [Views/MainWindow.axaml.cs](../Views/MainWindow.axaml.cs)
- [smoke-tests/FormDesigner.ExportSmokeTests/Program.ToolboxParity.cs](../smoke-tests/FormDesigner.ExportSmokeTests/Program.ToolboxParity.cs)
- [smoke-tests/FormDesigner.ExportSmokeTests/Program.UnifiedRecognition.cs](../smoke-tests/FormDesigner.ExportSmokeTests/Program.UnifiedRecognition.cs)
- [smoke-tests/FormDesigner.ExportSmokeTests/Program.cs](../smoke-tests/FormDesigner.ExportSmokeTests/Program.cs)
- [smoke-tests/UniversalMetadata/Phase7ACatalogProbe.cs.txt](../smoke-tests/UniversalMetadata/Phase7ACatalogProbe.cs.txt)
- [smoke-tests/UniversalMetadata/Phase7ARecognition.axaml](../smoke-tests/UniversalMetadata/Phase7ARecognition.axaml)
- [smoke-tests/UniversalMetadata/TestPhase7AToolbox.ps1](../smoke-tests/UniversalMetadata/TestPhase7AToolbox.ps1)

## Оставшиеся ограничения и предложения для Phase 7B

Установленный VSIX и физический DPI/DnD 100/125/150% остаются NOT VERIFIED. UI Automation native crash остаётся UNRESOLVED. Историческая причина Legacy_CancelledOwnerClose неизвестна. Inspector P95 остаётся P2. Представление большого дерева, выражения внутри шаблонов и проектный контекст сторонних сборок требуют отдельных проверок.

Для следующего этапа предлагаются ручная приёмка установленного VSIX с теми же AXAML, разрешение локальной assembly из подтверждённой проектной модели и проверка типизированных runtime-зависимостей Demo/Minimal. При дальнейшем расширении импорта стоит начать с наиболее частых непрозрачных поддеревьев реальных файлов.
