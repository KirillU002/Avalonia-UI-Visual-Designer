# Universal Controls & Properties — Phase 5C

Дата: 16.11.2026. Baseline `51999eea27c0ef635f6bd404912f8e2c38fcabab`, ветка main, Beta 0.1. До начала работы Git working tree чистый. Avalonia 11.1.1, Designer net8.0, Windows 10 x64 build 19045, SDK 10.0.402 / runtime 8.0.32. Исследованы отчёты Phase 1, 2, 3A, 3B, 4A, 4B, 5A, 5B и текущие registry/metadata/Inspector/layout/import/export adapters.

## 1. Результат и границы

В Toolbox добавлены **Label, AccessText, ToggleButton, CalendarDatePicker, MaskedTextBox и ButtonSpinner**. Это шесть проверяемых native вертикалей с ограниченным набором свойств. Завершение Phase 5C не означает полного покрытия Avalonia.

Использованы существующие AvaloniaMetadataCatalog, StandardControlProperties, DelegatingControlDescriptor, PropertyGrid, CustomProperties, ParentId, native projection, Preview, export bridge и source-preserving accessors. Не добавлялись второй Inspector, metadata cache, parser или export pipeline. NuGet, TargetFramework, JSON contract, тема, Grid-first, VSIX/IPC и Preview defaults не менялись. Коммиты, теги и релизы не создавались. Phase 4C не начиналась.

## 2. Методика полного аудита

[StandardLibraryCoverage.csv](StandardLibraryCoverage.csv) содержит Type, Namespace, Assembly, BaseType, роль, applicability, status, Toolbox, DesignerSurface, PropertyGrid, JSON, AXAML Import/Round-trip, оба Preview, Export, Generated Runtime, collection strategy и требования к специализированному editor. [StandardLibraryCoverageSummary.json](StandardLibraryCoverageSummary.json) содержит раздельные показатели assemblies и фактическую доступность всех специально запрошенных типов.

Inventory строится через существующий каталог из реально подключённых framework assemblies. Сторонние DLL не загружаются. `StandardLibraryCoverageReport` — только reporting policy поверх каталога, без второй discovery/registration системы. Associated NativeMenu/NativeMenuItem/Flyout/MenuFlyout проверяются отдельно как non-Control objects.

В знаменатель placement coverage входят конкретные Standalone и ChildOnly controls. Отдельно вынесены RootWorkflow (Window/UserControl), abstract, architectural base, infrastructure/template parts и attached objects. AccessText, ToggleButton, TabStrip и другие пригодные публичные primitives не исключаются только из-за namespace. ContentControl и ItemsControl включены в знаменатель как полезные concrete hosts, даже при отсутствии полноценного adapter. HeaderedContentControl/TemplatedControl — архитектурные основы; Presenters, AdornerLayer и VisualLayerManager не добавлялись как обычные widgets.

Политика одинаково применяется к Before и After. Canvas/DockPanel считаются существующими Group/layout workflows, а не отсутствующими controls. MenuItem/ComboBoxItem/ListBoxItem/TabItem/TreeViewItem учитываются как дочерние элементы через существующие collection editors. ContextMenu/ToolTip/Popup и nonvisual menus имеют отдельные роли, не повышают процент обычного Toolbox.

Assemblies Avalonia.Controls, Avalonia.Controls.DataGrid, ColorPicker и ItemsRepeater не объединяются в один процент. Themes — ресурсы/зависимости, не каталог controls. Eremex не входит в standard denominator.

## 3. Что означает Fully Supported

Для этого отчёта используется строгий критерий: подтверждённый pipeline плюс отсутствие неподдерживаемых writable native properties и коллекций. Полная поддержка не присваивается за Reflection, descriptor или scalar subset. Проверенная bounded native вертикаль отдельно отмечается VerifiedScalarPipeline; её общий status остаётся Partially Supported, пока отсутствуют необходимые complex/template/resource editors.

Поэтому число Fully Supported может быть нулём. Это честный показатель полного authoring coverage, а не отрицание работающих controls. Runtime evidence в общей таблице предоставлено только для проверенных групп Phase 5A/5B/5C; более старые adapters не получают новый PASS автоматически.

| Assembly | Applicable / Standalone / ChildOnly | Fully Before → After | Partial Before → After | JSON Designer gaps Before → After | Full % | Partial % Before → After |
|---|---:|---:|---:|---:|---:|---:|
| Avalonia.Controls | 72 / 66 / 6 | 0 → 0 | 46 → 52 | 26 → 20 | 0.00 | 63.89 → 72.22 |
| Avalonia.Controls.ColorPicker | 2 / 2 / 0 | 0 → 0 | 0 → 0 | 2 → 2 | 0.00 | 0.00 → 0.00 |
| Avalonia.Controls.DataGrid | 1 / 1 / 0 | 0 → 0 | 1 → 1 | 0 → 0 | 0.00 | 100.00 → 100.00 |
| Avalonia.Controls.ItemsRepeater | 1 / 1 / 0 | 0 → 0 | 0 → 0 | 1 → 1 | 0.00 | 0.00 → 0.00 |

Доступность запрошенных типов: Image=public, ItemsControl=public, ContentControl=public, HeaderedContentControl=public, ContentPresenter=public, TextPresenter=public, Label=public, AccessText=public, ToggleButton=public, CheckBox=public, RadioButton=public, ButtonSpinner=public, NumericUpDown=public, Calendar=public, CalendarDatePicker=public, DatePicker=public, TimePicker=public, AutoCompleteBox=public, Carousel=public, TransitioningContentControl=public, Menu=public, MenuItem=public, NativeMenu=public, ContextMenu=public, TreeView=public, TreeViewItem=public, TabControl=public, TabItem=public, TabStrip=public, TabStripItem=public, SelectingItemsControl=public, Grid=public, Canvas=public, StackPanel=public, DockPanel=public, WrapPanel=public, UniformGrid=public, ScrollViewer=public, Viewbox=public, GridSplitter=public, RelativePanel=public, Popup=public, Flyout=public, ToolTip=public, AdornerLayer=public, Decorator=public, VisualLayerManager=public, GroupBox=absent.

**Полностью поддерживаемых по строгому критерию: 0.** Работающий subset не переименовывается в полную поддержку. В текущей acceptance отдельно подтверждены bounded native pipelines: ButtonSpinner, CalendarDatePicker, Expander, GridSplitter, HyperlinkButton, Label, MaskedTextBox, AccessText, ScrollBar, ToggleButton, UniformGrid, RepeatButton, ScrollViewer, Ellipse, Line, Path, Polygon, Polyline, Rectangle, Viewbox.

Частично поддерживаемые: Border [Avalonia.Controls], Button [Avalonia.Controls], ButtonSpinner [Avalonia.Controls], Calendar [Avalonia.Controls], CalendarDatePicker [Avalonia.Controls], Canvas [Avalonia.Controls], CheckBox [Avalonia.Controls], ComboBox [Avalonia.Controls], ComboBoxItem [Avalonia.Controls], DataGrid [Avalonia.Controls.DataGrid], DatePicker [Avalonia.Controls], DockPanel [Avalonia.Controls], DropDownButton [Avalonia.Controls], Expander [Avalonia.Controls], Grid [Avalonia.Controls], GridSplitter [Avalonia.Controls], HyperlinkButton [Avalonia.Controls], Image [Avalonia.Controls], Label [Avalonia.Controls], ListBox [Avalonia.Controls], ListBoxItem [Avalonia.Controls], MaskedTextBox [Avalonia.Controls], Menu [Avalonia.Controls], MenuItem [Avalonia.Controls], NumericUpDown [Avalonia.Controls], AccessText [Avalonia.Controls], ScrollBar [Avalonia.Controls], ToggleButton [Avalonia.Controls], UniformGrid [Avalonia.Controls], ProgressBar [Avalonia.Controls], RadioButton [Avalonia.Controls], RepeatButton [Avalonia.Controls], ScrollViewer [Avalonia.Controls], Separator [Avalonia.Controls], Ellipse [Avalonia.Controls], Line [Avalonia.Controls], Path [Avalonia.Controls], Polygon [Avalonia.Controls], Polyline [Avalonia.Controls], Rectangle [Avalonia.Controls], Slider [Avalonia.Controls], SplitButton [Avalonia.Controls], StackPanel [Avalonia.Controls], TabControl [Avalonia.Controls], TabItem [Avalonia.Controls], TextBlock [Avalonia.Controls], TextBox [Avalonia.Controls], TimePicker [Avalonia.Controls], ToggleSwitch [Avalonia.Controls], TreeView [Avalonia.Controls], TreeViewItem [Avalonia.Controls], Viewbox [Avalonia.Controls], WrapPanel [Avalonia.Controls].

Только source subset: ItemsControl [Avalonia.Controls].

Обнаружены, но не имеют полного Designer adapter: AutoCompleteBox [Avalonia.Controls], Carousel [Avalonia.Controls], ColorPicker [Avalonia.Controls.ColorPicker], ColorView [Avalonia.Controls.ColorPicker], ContentControl [Avalonia.Controls], ExperimentalAcrylicBorder [Avalonia.Controls], ItemsRepeater [Avalonia.Controls.ItemsRepeater], LayoutTransformControl [Avalonia.Controls], PathIcon [Avalonia.Controls], HeaderedItemsControl [Avalonia.Controls], TabStrip [Avalonia.Controls], TabStripItem [Avalonia.Controls], RefreshContainer [Avalonia.Controls], RelativePanel [Avalonia.Controls], ReversibleStackPanel [Avalonia.Controls], SelectableTextBlock [Avalonia.Controls], Arc [Avalonia.Controls], Sector [Avalonia.Controls], SplitView [Avalonia.Controls], ThemeVariantScope [Avalonia.Controls], ToggleSplitButton [Avalonia.Controls], TransitioningContentControl [Avalonia.Controls].

Unsupported: нет отдельных строк этой категории.

NativeMenu/NativeMenuItem не имеют нового authoring/runtime adapter: строка Attachment workflow обозначает роль, не доказанную реализацию. Existing Flyout/MenuFlyout/ContextMenu используют специализированные workflows Phase 3B. Все исключённые типы и причины перечислены построчно в CSV, а не удалены из inventory.

## 4. Выбор и реализация шести controls

| Компонент | Поддерживаемый workflow | Проверяемые свойства / взаимодействие |
|---|---|---|
| Label | Literal Content через прежний model.Text | Padding, appearance/layout, text escaping; Target read-only |
| AccessText | Native Text, mnemonic underscore | ShowAccessKey, TextAlignment, TextWrapping, LetterSpacing, LineSpacing, MaxLines, native AccessKey |
| ToggleButton | Literal Content и nullable three-state | IsChecked, IsThreeState, ClickMode; keyboard Space |
| CalendarDatePicker | Native selected/display date и календарь | SelectedDate, DisplayDate/Start/End, FirstDayOfWeek, IsTodayHighlighted, SelectedDateFormat, CustomDateFormatString; popup open/close |
| MaskedTextBox | Отдельные Mask и model.Text | AsciiOnly, HidePromptOnLeave, ResetOnPrompt/Space, Watermark, IsReadOnly, MaxLength; native formatted input |
| ButtonSpinner | Один visual Content через ParentId | AllowSpin, ShowButtonSpinner, ButtonSpinnerLocation, ValidSpinDirection; native Spin event |

ButtonSpinner не имеет выдуманного Value: событие Spin обрабатывает приложение. NumericUpDown остаётся отдельным готовым numeric workflow. Label.Target, MaskedTextBox.Culture/MaskProvider/MaskCompleted/MaskFull, AccessText.Inlines и CalendarDatePicker.BlackoutDates не превращены в TextBox editors. Команды, bindings, templates и resources сохраняют прежние capability guards.

Для MaskedTextBox отдельно сохранена runtime input граница: injected SelectAll + TextInput не заменил уже заполненный текст ни в generated app, ни в минимальном native reference Window с одним MaskedTextBox. Чтение IL установленной Avalonia.Controls 11.1.1 подтвердило использование MaskedTextProvider.InsertAt в OnTextInput. Native Delete-selection → посимвольный KeyDown/TextInput/KeyUp форматирует 654321 как 654-321 в обоих generated layouts. Это не доказательство повреждения JSON/Designer, но и не acceptance физического Ctrl+A replacement, paste/IME; эти сценарии остаются ограничением и требуют отдельной проверки. Avalonia и поведение native control не патчились.

Toolbox получил существующие группы «Стандартные», «Ввод», «Контейнеры», русские hints, glyphs, поиск и уникальную регистрацию. Глобального редизайна нет. AccessText и ToggleButton явно разрешены в candidate policy; остальные Primitives автоматически не регистрируются.

## 5. Общие свойства и сериализация

TextPropertyName описывает разницу native Content и Text поверх прежнего model.Text. Этот один policy применяется registrar, native factory и export bridge. Конфигурация Mask применяется до Text, чтобы native MaskedTextBox и AXAML loader давали одинаковое форматирование. Для остальных controls сохраняются прежние codecs, native AP identity, defaults и parent-context placement.

Mask валидируется стандартным BCL MaskedTextProvider и ограничена 1024 символами. CustomDateFormatString проверяется .NET formatter. DateTime codec принимает прежние yyyy-MM-dd/O и дополнительно однозначный ISO yyyy-MM-ddTHH:mm:ss; culture-dependent произвольный ввод не включён. Даты сериализуются прежним round-trip форматом. Runtime Avalonia objects не попадают в JSON, произвольные ToString не используются для complex serialization.

| Control | Discovered | Displayable | Editable | Serializable | Importable | Exportable | Unsupported | Actual Inspector editable keys |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Label | 193 | 45 | 35 | 37 | 37 | 35 | 148 | 35 |
| AccessText | 196 | 46 | 36 | 39 | 39 | 36 | 150 | 36 |
| ToggleButton | 203 | 50 | 38 | 40 | 40 | 38 | 153 | 38 |
| CalendarDatePicker | 202 | 52 | 42 | 44 | 44 | 42 | 150 | 42 |
| MaskedTextBox | 232 | 56 | 43 | 45 | 45 | 43 | 176 | 43 |
| ButtonSpinner | 196 | 47 | 38 | 40 | 40 | 38 | 149 | 38 |

Native Toolbox types: 40 → 46; built-in descriptors с Group alias: 41 → 47; property/alias rows: 10043 → 11265. Эти rows не являются числом уникальных AP registrations или редактируемых полей.

Actual Inspector — [Phase5CPropertyGrid.csv](Phase5CPropertyGrid.csv), полный capability inventory — [UniversalPropertyCoverage.csv](UniversalPropertyCoverage.csv). Различия чисел вызваны parent context, aliases и specialized rows; UI safety/readonly не определяется одним Reflection флагом.

Часто не покрываемые writable signatures (число type/property пар): Right=114, FlowDirection=114, IsTabStop=114, TabIndex=114, HotKey=67, LineHeight=61, TextAlignment=61, LetterSpacing=60, MaxLines=60, TextWrapping=60, Name=59, DataContext=59, Height=59, Width=59, IsScrollInertiaEnabled=59, BaselineOffset=59, TextTrimming=59, Text=59. Для Content/Header/Items/ItemsSource/Selection/Commands/Bindings/Resources/Styles/Templates/Geometry состояние различается: existing specialized collection/data/layout adapters, bounded literals или read-only/opaque. Все свойства единого вида не объявляются editable автоматически.

## 6. Исправления существующих механизмов

**UniformGrid.Children metadata.** Phase 5B уже поддерживала native Children и ParentId/StackOrder, но CollectionMetadataCatalog перечислял только Grid/Canvas/StackPanel/WrapPanel/DockPanel. UniformGrid ошибочно получал PreserveOnly/CanAdd=false. Добавлен в ту же whitelist с ExistingParentIdAndStackOrder. Source structural capability остаётся read-only; generic collection editor для произвольных IEnumerable не создавался.

**Поздний ContextRequested на retired wrapper.** До изменений baseline Layout7 снова дал 14/15. Подтверждён отдельный deterministic defect: после selection/render старый wrapper отсоединяется, но его ContextRequested lambda продолжает открывать меню на захваченном объекте. Новый regression сначала упал 0/1.

Оба входа — ContextRequested и отложенный pointer-release — используют OpenCurrentControlContextMenu: проверяют captured DocumentId, наличие control и актуального attached wrapper; меню создаётся на текущем shell. Уже открытое меню повторно не закрывается/открывается. После смены формы или удаления control late request ничего не создаёт. Accessibility, AutomationPeer и native interop не менялись.

Это доказанное исправление stale-wrapper lifecycle. Оно не устанавливает причину исторического UIA AV. Исходный intermittent baseline не получил полного trace до изменения; нельзя утверждать, что каждый прежний failure имел именно эту причину. Диагностика исходного теста сохраняет old/current wrapper attachment и menu.IsOpen.

До исправления: baseline Layout7 **14/15**, затем три инструментированных полных прогона **15/15**; это differential observations, не стирание первого red. Deterministic retired-wrapper regression: **0/1 → 1/1**; после fix все три lifecycle scenarios **3/3**, финальный Layout7 **15/15**. Новые traces показывают, что selection действительно заменяет и отсоединяет старый shell; актуальный wrapper сохраняет открытое меню. Нет доказательства связи ContextMenu с null-provider UIA AV.

## 7. DesignerSurface, Explorer и Layout

Использована прежняя native projection. ButtonSpinner вошёл в существующую single-content policy: capacity checks выполняются до mutation, ChildLayoutMode не превращает spinner в Canvas/Grid. В дочернем Content применяются native layout semantics, не Canvas.Left/Top. Standard templates не менялись; input spinner buttons блокируется только в editing surface так же, как header/scroll gestures существующих native hosts.

Проверены Grid, Canvas, StackPanel, DockPanel, WrapPanel, nested spinner/TextBox, selection, routed Toolbox Drop/resize, Undo/Redo и дерево Structure/Explorer. Tree selection проходит через существующий SelectedStructureItem и выбирает соответствующий PropertyGrid context. Designer child TextBox использует прежний selection wrapper с model identity; Preview/runtime содержат непосредственно native named TextBox.

Геометрия шести root controls сравнивается по actual transformed native frames Designer/Legacy/AXAML, client area 1000×750, tolerance 2 DIP. Grid/Canvas проверены отдельно. Auto/Stretch может заполнять всю Grid cell; routed resize fixture явно использует Fixed размеры, аналогично Phase 5B. Произвольные координатные поправки не добавлялись.

## 8. JSON, Undo/Redo и Multi Form

Сохранены прежние model fields и CustomProperties.ValueJson. Проверяется equality фактических controls до/после save/open: Id, Name, ParentId, order, layout и property bag. Native свойства повторно проверяются именно после JSON reload в обоих Preview.

Nullable ToggleButton IsChecked редактируется одной Undo transaction; Undo восстанавливает True, Redo возвращает null. Multi Form сохраняет раздельные формы ToggleButton и MaskedTextBox, control identity и свойства. Canvas root выбирается явно; historical JSON fallback и Grid-first не менялись.

## 9. AXAML Import / Round-trip

Type resolution и scalar schema используют общий metadata engine. Source map для Label/ToggleButton и ButtonSpinner сохраняет native Content semantics; Text у AccessText/MaskedTextBox использует существующий model text accessor. Для каждого типа проверены edit, один minimal patch, Reset attribute deletion, Undo до точного исходного AXAML, Redo и Undo Reset. Unknown attributes, namespace prefix, binding/resource expressions сохраняются.

Отдельно импортирован AXAML всей новой группы: проверяются фактические native types/properties в обоих Preview и no-op source equality. Native content не означает разрешения небезопасного structural Add/Remove/Reorder XML. Исходная разметка не регенерируется, пользовательские getters/VM/bindings/templates при discovery не исполняются.

## 10. Preview и generated runtime

| Control | Designer | Legacy / AXAML Preview после JSON | Imported source native | Generated Grid / Canvas |
|---|---|---|---|---|
| Label | PASS native | PASS | PASS | PASS Content/Padding |
| AccessText | PASS native | PASS | PASS | PASS Text/AccessKey/ShowAccessKey/typography |
| ToggleButton | PASS native | PASS | PASS | PASS nullable three-state keyboard toggle |
| CalendarDatePicker | PASS native | PASS | PASS | PASS date/format/calendar popup |
| MaskedTextBox | PASS native | PASS | PASS | PASS Mask/Text и native TextInput formatting |
| ButtonSpinner | PASS native Content hierarchy | PASS | PASS | PASS прямой named TextBox Content и native Spin |

Новые Grid/Canvas проекты экспортированы из модели, прошли restore/build и отдельный runtime process. Probe проверяет exact native types, actual properties, размеры, интерактивность, named hierarchy и resize 800×600/1200×800/1600×900. Наличие тегов/успех компиляции не подменяет эти проверки. Generated C#/ViewModel/startup собираются штатным exporter; placeholders в runtime не вводились.

## 11. Performance

В таблицах median/P95 в мс, allocations — median MB UI thread.

| Существующий control / операция | Before median/P95 | After median/P95 | Allocations Before → After, MB |
|---|---:|---:|---:|
| Button / ToolboxCreate | 89,44/108,31 | 93,74/121,83 | 14,20 → 14,20 |
| Button / Inspector | 33,17/56,94 | 35,98/66,74 | 1,52 → 1,52 |
| Button / SaveLoad | 8,62/38,02 | 8,76/24,13 | 1,69 → 1,77 |
| Button / ExportAxaml | 10,07/15,91 | 10,93/18,17 | 0,63 → 0,63 |
| Button / LegacyPreview | 324,99/345,79 | 328,01/348,72 | 7,57 → 7,63 |
| Button / AxamlPreview | 374,91/408,16 | 378,23/395,95 | 11,52 → 11,44 |
| NumericUpDown / ToolboxCreate | 118,95/150,68 | 132,68/195,01 | 20,37 → 20,37 |
| NumericUpDown / Inspector | 79,44/108,02 | 93,57/150,35 | 10,17 → 10,17 |
| NumericUpDown / SaveLoad | 8,24/46,88 | 8,99/39,71 | 1,69 → 1,78 |
| NumericUpDown / ExportAxaml | 9,24/36,34 | 13,36/39,61 | 0,62 → 0,62 |
| NumericUpDown / LegacyPreview | 327,23/339,31 | 326,33/350,37 | 10,19 → 10,19 |
| NumericUpDown / AxamlPreview | 377,53/390,46 | 377,59/390,55 | 11,90 → 11,89 |

| Новый control | Toolbox median/P95 | Inspector median/P95 | Inspector allocations MB | Legacy median/P95 | AXAML median/P95 |
|---|---:|---:|---:|---:|---:|
| Label | 100,31/127,15 | 60,33/117,12 | 5,87 | 324,89/338,16 | 377,25/383,96 |
| AccessText | 108,26/151,86 | 77,50/100,14 | 6,99 | 324,94/336,01 | 377,60/396,25 |
| ToggleButton | 120,93/159,59 | 78,03/128,25 | 7,20 | 328,28/359,95 | 380,55/446,90 |
| CalendarDatePicker | 126,74/154,75 | 83,48/119,27 | 10,46 | 325,50/340,93 | 383,94/396,00 |
| MaskedTextBox | 131,77/161,07 | 92,55/135,00 | 9,60 | 326,36/364,21 | 383,42/396,57 |
| ButtonSpinner | 109,04/132,60 | 78,01/108,50 | 7,74 | 330,09/338,51 | 383,62/405,80 |

| Операция portable harness | Before median/P95 | After median/P95 | Allocations Before → After, MB |
|---|---:|---:|---:|
| ToolboxInitialize | 4,46/11,53 | 4,33/11,35 | 1,14 → 1,22 |
| MetadataDiscoveryCached | 0,49/0,63 | 0,46/0,79 | 0,02 → 0,02 |
| DesignerProjection | 6,36/28,00 | 8,42/26,99 | 2,06 → 2,07 |
| UndoRedo | 84,17/395,54 | 96,07/385,95 | 7,83 → 7,88 |

| AXAML scene | Before import median/P95 | After import median/P95 | Allocations Before → After, MB |
|---|---:|---:|---:|
| Small | 0,95/1,21 | 0,92/0,99 | 0,22 → 0,22 |
| Medium | 9,49/10,99 | 9,21/35,55 | 2,02 → 2,02 |
| MainWindow | 43,55/66,17 | 44,85/63,13 | 33,69 → 33,68 |

Полные SaveLoad/export/GC результаты: [Phase5CPerformance.csv](Phase5CPerformance.csv). Raw samples и process captures сохранены в artifacts/universal-phase5c/performance. Сумма metadata builds в измеряемых control selection/create samples после warm-up: 0. MetadataDiscoveryCached в отдельных первых warm-up строит inventory один раз, не при каждом выборе.

Интерпретация: большого AXAML import regression в этой паре не видно (43,55 → 44,85 мс median, 66,17 → 63,13 мс P95, allocations 33,69 → 33,68 MB). Существующий NumericUpDown Inspector показал рост 79,44 → 93,57 мс median и 108,02 → 150,35 мс P95 при одинаковых 10,17 MB allocations и отсутствии повторного metadata build. DesignerProjection median вырос на 2,06 мс, UndoRedo — на 11,90 мс, их P95 немного снизился. Причина вариативности отдельных UI сценариев не установлена отдельным stage profile; это сохранённый P2 performance risk, а не заявление об ускорении или доказательство отсутствия регрессий. Масштабная оптимизация ради отчёта не выполнялась.

Исторические Phase 4B/5B цифры приведены как контекст в прежних отчётах; вывод о регрессии здесь делается по новой сопоставимой паре. Результат не является гарантией физического first render, всех больших real-world AXAML или неизменного P95 под внешней нагрузкой. Не выполнялся новый 300/500-control stress: текущая фаза сохраняет прежние ограничения больших форм и проверяет прежний большой MainWindow fixture.

Методика: Release, 5 warm-up + 30 samples, тот же portable Phase 5A workflow benchmark и отдельный идентичный operations harness в git archive baseline/current. До/после измеряются одни и те же существующие controls; для новых types нет выдуманного Before. Собственные builds/SQL/regression не выполняются параллельно benchmark. Allocations — current UI thread, GC counters — process-wide; native memory и физический first frame этим не заменяются.

ToolboxInitialize измеряет создание временного VM/Toolbox; MetadataDiscoveryCached включает enumeration и cached lookup; DesignerProjection — synchronous RenderDesigner/UpdateLayout; UndoRedo — реальные commands и UI jobs. Temporary VMs отписываются существующим lifecycle cleanup. Production GC.Collect не добавлен.

## 12. Memory, UIA и plugins

Новая группа: **20 Legacy + 20 AXAML Preview cycles — PASS**, forced-root weak-reference assertions для закрытых окон/контекстов. Полная SQL-enabled Preview Memory — **36/36**, включая несколько DataGrid, close during loading/query, cancellation и повторное открытие. Metadata/collectible cache regression — **45/45** вместе с остальными metadata assertions.

На момент отчёта сохранены 46 process capture manifests Phase 5C: Captured=0, CaptureFailed=0, TimedOut=0. Managed assertion failures и compile errors не являются native AV и сохранены отдельно. Generated runtime processes завершились успешно. Global strong plugin Type/Assembly caches не добавлены; прежний CWT lifecycle сохранён.

Исторический native crash uiautomationcore.dll / ProviderCallouts::GetPatternProvider остаётся **UNRESOLVED**. Успешные cycles не доказывают исправления. При новом AV используется существующий full NativeCapture с уникальными directories/metadata/hash. Dumps не добавляются в Git, accessibility не отключалась. Внешний UIA client-dependent Window retention не объявляется устранённым.

## 13. Build, regression и ограничения среды

| Проверка | Итог |
|---|---|
| Baseline solution / final solution | PASS, 0 ошибок; прежний NU1603 Visual Studio SDK analyzer fallback |
| Smoke runner | PASS, 0 ошибок/предупреждений |
| Phase 5C | **55/55** |
| Phase 5B / Phase 5A | **73/73 / 47/47** |
| Metadata / PropertyGrid / Collections | **45/45 / 30/30 / 30/30** |
| Phase 4A / Phase 4B / Phase 2 | **38/38 / 52/52 / 40/40** |
| Nested Layout / Pointer / Grid Drop | **59/59 / 26/26 / 40/40** |
| Layout7 | baseline **14/15**, финальный **15/15**, lifecycle cases **3/3** |
| Broad | первый sandbox **69/72** (Named Pipes AccessDenied); normal environment **72/72** |
| LocalDB DataGrid Preview | **47/47**, реальные строки |
| Settings/schema/source Preview scenario | PASS |
| Preview Memory с SQL | **36/36** |
| SQL generated runtime | PASS, Customers=3 / Orders=2, independent forms/ViewModels, реальные cells и resize |
| PostgreSQL offline | **20/20**, live SKIP |
| Generated Phase 5C Grid / Canvas | restore/build/run **PASS / PASS**, native=6 и interaction probes |
| VSIX/VsHost source IPC | **1/1**, Open/Patch/ACK/conflict; прежние IPC scenarios также в Broad |

Первый Broad в sandbox: 69/72; AxamlHardeningIpcEditReloadLifecycle, VsHostCanStartAndAcceptConnection и BridgeSurvivesVsHostDisconnect получили UnauthorizedAccessException в NamedPipeClientStream.TryConnect. В normal environment без production IPC изменений полный Broad прошёл 72/72. Сохранено прежнее cleanup warning: загруженный ToolboxStandardItemsExport.dll не удалось удалить из artifacts; force cleanup не выполнялся. Результаты normal environment не подменяются sandbox limitations. NativeCapture exit codes проверены; аварийное завершение до assertions не считается PASS. Первичные красные прогоны перечислены ниже, без retries-маскировки. Existing generated net6.0 может выдавать NETSDK1138; TFM и NuGet не менялись.

LocalDB использует собственный GUID instance/database и реальные Customers/Orders rows; cleanup выполняется только для своего fixture. PostgreSQL live не проверяется без сервера. Автоматизированные VSIX/VsHost/Named Pipes проверки не выдаются за ручную установку расширения в Visual Studio. Avalonia/Eremex в devenv.exe не переносились.

## 14. Первые красные прогоны

Сохранены baseline Layout7 14/15 и последующие результаты. Первый build smoke harness не прошёл из-за ошибочного owner ContextRequestedEvent и конфликта имени helper Control со static property; production build был успешен. Исправлены ссылки на реальный Avalonia.Controls.Control API.

Первый Phase 5C: 29/49. Большинство failures объединял неподдержанный ISO timestamp без fractional seconds; codec расширен однозначным форматом. Resize fixture пытался нажать Auto/Stretch corner вне viewport, теперь использует Fixed размеры. Spinner assertion не учитывал прежний Designer selection wrapper/model identity; native TextBox и его Text/identity проверяются с учётом chrome, а runtime требует прямой named Content. Второй прогон — 47/49, исправлены оставшиеся wrapper assertions; затем 49/49.

Дополнительная geometry fixture сначала искала DesignerCanvas в чужом XAML namescope (0/2); исправлен доступ через actual DesignerSurface, затем 2/2. Explorer fixture приведён к реальному пользовательскому пути SelectedStructureItem вместо повторного выбора уже выбранного seed control. Retired context request дал отдельный red 0/1 перед production fix; после него проверяются late request, document switch и removed control.

Первый generated runtime build не прошёл из-за ошибочного GetShowAccessKey в test probe; заменён реальным ShowAccessKeyProperty API. Затем runtime assertions правильно выявили описанный выше masked-selection input сценарий; все FAIL result-файлы сохранены. Выявлен test harness defect: ProbeRuntime проверял только exit code/наличие result и печатал FAIL как output. Теперь он требует PASS prefix и завершает script ошибкой при failed assertions даже при exit code 0 приложения. После корректного native Delete/input сценария Grid/Canvas дали PASS. Это изменение только test acceptance gate, не production serializer/control.

Все первичные outputs и captures находятся в ignored artifacts/universal-phase5c. Subsequent PASS не выдаётся за полностью зелёный первый прогон.

## 15. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs`
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`
* `DesignerSystem/Metadata/CollectionPropertyMetadata.cs`
* `DesignerSystem/Metadata/MetadataCoverageReport.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `DesignerSystem/Metadata/StandardLibraryCoverageReport.cs`
* `Docs/Phase5CPerformance.csv`
* `Docs/Phase5CPropertyGrid.csv`
* `Docs/StandardLibraryCoverage.csv`
* `Docs/StandardLibraryCoverageSummary.json`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase5C.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `Models/DesignerLayoutModes.cs`
* `Models/ToolboxItem.cs`
* `ViewModels/MainWindowViewModel.cs`
* `Views/MainWindow.NativeLayout.cs`
* `Views/MainWindow.axaml.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Layout7.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5C.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5CDiagnostics.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5CPerformance.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase5CRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/ProbeRuntime.ps1`
* `smoke-tests/UniversalMetadata/README.md`

## 16. Ограничения и следующие шаги

* Полное authoring coverage стандартной библиотеки не достигнуто. Complex Content/Header/Items/SelectedItem, Commands, Styles/Resources, bindings/templates, collection structure и arbitrary geometry требуют специализированных editors.
* Label.Target reference editor, MaskedTextBox Culture/PromptChar/provider customization, Calendar blackout dates/advanced selection не реализованы. ButtonSpinner сам не изменяет numeric Value; это нативный event/content control.
* Image asset/source handling, ItemsControl/ContentControl, AutoCompleteBox, Carousel/TransitioningContentControl и остальные gaps перечислены в CSV. Их не добавляли только ради процента.
* Structural imported collection editing остаётся scope отдельной Phase 4C. Сохранность source приоритетнее ширины XML mutations.
* UIA AV unresolved; установленный VSIX, real text buffer conflicts, physical mouse/access keys, DPI/multi-monitor, screen readers, Linux/macOS и live PostgreSQL требуют отдельной acceptance.
* Performance большого imported Preview и tail latency остаётся ограничением; изолированная массовая оптимизация не начиналась.
* В текущей паре повышен NumericUpDown Inspector P95; нужен отдельный stage profile при воспроизводимом изолированном workload. Показатели отдельных UI операций не улучшаются автоматически от расширения metadata policy.
* Ручная проверка новой группы: label/access-key focus navigation, mask typing/copy/paste/delete/IME, nullable toggle, calendar popup keyboard selection, spinner mouse/wheel and application Spin handler, Fixed/Auto layout при разных DPI и zoom.

**STANDARD LIBRARY COVERAGE: READY WITH CONDITIONS.**

Phase 5C дала воспроизводимый inventory с честным знаменателем и шесть новых работающих native pipelines, исправила UniformGrid collection policy и подтверждённый late-context request defect. Полное покрытие Avalonia не объявляется; новые controls имеют явные scalar/content capabilities. Основные regression и generated runtime проходят, новых подтверждённых P0/P1 в проверенном pipeline не осталось. Исторический UIA AV, перечисленная ручная acceptance и ограничения complex/large-form workflows сохраняются.

Рекомендуемое отдельное продолжение: Image asset/source adapter; ItemsControl/ContentControl content/template workflows; востребованные AutoCompleteBox/selection controls; reference/object editors; дополнительные real-project и accessibility acceptance. Структурный source editor требует собственного согласованного scope Phase 4C. Следующая фаза автоматически не начиналась.
