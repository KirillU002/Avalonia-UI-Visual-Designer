# Universal Controls & Properties — Phase 5A

Дата: 15.11.2026. Baseline: `62b6fd476127c81c8847de5974fd83fc4aed3fc3`, `main`, Beta 0.1. Рабочее дерево до изменений было чистым. Avalonia 11.1.1, продукт/Designer net8.0, SDK 10.0.402, runtime 8.0.32, Windows 10 x64 build 19045. Версии пакетов, TargetFramework, JSON contract, Preview default, темы и VSIX protocol не менялись. Коммиты/теги/релизы не создавались. Phase 4C/5B не начинались.

## 1. Результат и архитектура

Добавлены **ScrollBar, GridSplitter, Rectangle, Ellipse, HyperlinkButton, RepeatButton**. Это reviewed группа с проверяемым набором scalar свойств; универсальная поддержка любого Content, Geometry или Template не заявляется.

Используется прежняя цепочка:

`DesignerRegistry → DelegatingControlDescriptor → StandardControlProperties → AvaloniaMetadataCatalog/MetadataValueCodec → PropertyGrid/CustomProperties → native projection → Legacy Preview / export AXAML → AXAML Preview / generated runtime`.

Сохранена прежняя двухаргументная сигнатура MetadataCoverageReport.Write; runtime evidence передаётся отдельной перегрузке. В AXAML Import тип и literal accessors берутся из того же StandardControlProperties. SourceMap, capabilities, минимальные patches, Reset/Undo и ACK остаются существующими. Для plugins сохранён отдельный boundary. Не создавались второй Metadata Engine, Inspector, layout engine или экспортёр.

## 2. Полный inventory и классификация

[UniversalControlCoverage.csv](UniversalControlCoverage.csv) содержит фактический список публичных Control-derived типов: Namespace, Assembly, BaseType, visual/abstract, container/child semantics, Toolbox, Designer, Inspector, import, оба Preview, JSON, export и runtime evidence. [UniversalPropertyCoverage.csv](UniversalPropertyCoverage.csv) содержит property-level capabilities; [UniversalCoverageSummary.json](UniversalCoverageSummary.json) — сводку.

Старый inventory охватывал Avalonia.Controls, Avalonia.Base и Avalonia.Controls.DataGrid. В Phase 5A в существующий доверенный каталог включены **уже подключённые** Avalonia.Controls.ColorPicker и Avalonia.Controls.ItemsRepeater. Это расширение аудита установленных стандартных assemblies, без загрузки произвольных DLL и без автоматической регистрации обнаруженных controls. Для Viewbox исправлена audit metadata: Child, а не отсутствие содержимого.

Категории CSV: 1 — новая проверенная вертикаль с ограниченным property subset; 2 — существующий descriptor/частичная поддержка, подробные acceptance results в регрессиях; 3 — source subset без прямого Toolbox entry; 4 — обнаруженный кандидат без JSON Designer adapter; 5 — требуется специализированный item/geometry editor; 6 — root/base/infrastructure, не обычный Toolbox component. Категории описывают основной workflow и не заменяют подробные колонки.

Window/UserControl — root workflows; ContentControl, Panel, Decorator, HeaderedContentControl и TemplatedControl — архитектурные базовые типы. ItemsControl требует осмысленного Items/template workflow и не добавлен как универсальный контейнер. Abstract/runtime helpers не регистрировались.

| Метрика | Baseline | Phase 5A |
|---|---:|---:|
| Публичные Control-derived типы в audit scope | 140 | 146 |
| Built-in descriptors, включая Group alias | 27 | 33 |
| Прямые standard native Toolbox types | 26 | 32 |
| Property pairs в существующих editing/import/root workflows | 7785 | 8951 |
| Displayable pairs | 1580 | 1864 |
| Editable pairs | 814 | 1024 |
| Serializable pairs | 1298 | 1520 |
| Importable pairs | 1243 | 1465 |
| Exportable pairs | 819 | 1029 |

Число 146 выросло из-за шести дополнительно учтённых package types: ColorPicker, ColorView, ItemsRepeater, ColorPreviewer, ColorSlider, ColorSpectrum. Это не шесть Toolbox entries: новая Toolbox группа состоит из других шести типов, выбранных выше. Property report ограничен editing/import/root workflows; он не содержит все свойства всех infrastructure типов. Пары включают inherited/attached aliases и повторяются для каждого control, а не являются уникальными registrations.

Распределение audit categories: 1 — 6; 2 — 26; 3 — 8; 4 — 24; 5 — 7; 6 — 75. Ни 146 discovery types, ни 8951 pairs не объявляются полностью поддержанными.

Обнаруженные кандидаты без прямого Toolbox entry: AutoCompleteBox, ButtonSpinner, CalendarDatePicker, Canvas/DockPanel (существует root/Group workflow), Carousel, ColorPicker, ColorView, ComboBoxItem, Expander, ExperimentalAcrylicBorder, ItemsRepeater, Label, LayoutTransformControl, ListBoxItem, MaskedTextBox, MenuItem, PathIcon, RefreshContainer, RelativePanel, ReversibleStackPanel, ScrollViewer, SelectableTextBlock, Arc, Line, Path, Polygon, Polyline, Sector, SplitView, TabItem, ThemeVariantScope, ToggleSplitButton, TransitioningContentControl, TreeViewItem, Viewbox. Candidate — не рекомендация добавить без adapters и acceptance; item types используются в существующих collection workflows.

## 3. Выбор controls и оставшиеся кандидаты

| Кандидат | Решение и причина |
|---|---|
| ScrollBar | Добавлен: native RangeBase + Orientation/ViewportSize; без отдельного descriptor диапазона |
| GridSplitter | Добавлен: настоящий GridSplitter, native resize в Preview/runtime |
| Rectangle / Ellipse | Добавлены: Shape Fill/Stroke, без несуществующих Background/Font properties |
| HyperlinkButton / RepeatButton | Добавлены: переиспользуется literal Content/Button workflow; URI и repeat interval через metadata |
| Viewbox / ScrollViewer / Expander | Отложены: необходим reviewed JSON single-child/Content/Header adapter в существующей native hierarchy projection. Нельзя разрешить произвольных детей для leaf descriptor |
| UniformGrid | Найден в Avalonia.Controls.Primitives; нужен panel adapter, definitions/placement и полноценная hierarchy acceptance |
| Line / Path / Polygon / Polyline | Найдены; Point/Geometry/Points требуют безопасных typed/nested/collection editors, а не ToString/TextBox |
| GroupBox | Стандартный тип отсутствует в проверенных установленных assemblies; фиктивный control не добавлен |
| Menu / TreeView / TabControl | Уже реализованы в Phase 3B; не дублировались |
| MenuItem / TreeViewItem / TabItem | Collection-owned item workflows; не новые свободно размещаемые root controls |
| CheckBox | Уже в Toolbox; старое покрытие свойств частично, полноценный новый IsChecked adapter этой фазой не заявляется |
| ColorPicker / ColorView / ItemsRepeater и вспомогательные типы | Теперь учтены inventory; обнаружение не означает Toolbox/JSON/runtime support |

Полный список отсутствующих прямых Toolbox entries и их причин — в CSV, включая служебные типы, которые добавлять не нужно.

## 4. Свойства и PropertyGrid

Новые свойства описываются списком проверенных ключей существующего StandardControlProperties; тип/default/owner/kind/validation берутся из Avalonia metadata. Сохраняются existing typed editors, категории, поиск, Favorites, русские hints, Value Source, Reset и Undo. Не исполняются getters пользовательских компонентов.

* ScrollBar: Minimum/Maximum/Value, Orientation, ViewportSize, SmallChange/LargeChange.
* GridSplitter: ResizeDirection/ResizeBehavior, ShowsPreview, DragIncrement/KeyboardIncrement.
* Rectangle/Ellipse: Fill/Stroke/StrokeThickness, Stretch, StrokeDashOffset, StrokeLineCap/StrokeJoin; RadiusX/RadiusY только у Rectangle.
* HyperlinkButton: NavigateUri, IsVisited, ClickMode; RepeatButton: Delay, Interval, ClickMode.
* Применимые общие свойства — через существующие model/layout adapters и common metadata keys: размеры, ограничения, Margin/Alignment, IsEnabled/IsVisible/Opacity, Padding/Border/Font только когда они действительно есть у native типа.

NavigateUri использует типизированный Uri codec: null или absolute HTTP/HTTPS, до 8192 символов; file/javascript/relative не принимаются. Импорт не выполняет навигацию; runtime tests не открывают URL. ScrollBar.ViewportSize поддерживает собственный Avalonia default NaN, сериализуемый как специальное scalar string значение; generic numeric codec по-прежнему отклоняет NaN. Range validation не смешивает decimal/double.

StrokeDashArray, Commands/CommandParameter, Templates, Resources, Bindings и сложные коллекции не превращены в TextBox. Существующие read-only/partial rows и collection capabilities сохраняются; complex Content у HyperlinkButton/RepeatButton этой JSON-вертикалью не редактируется. Shape Inspector и source capability map фильтруют Background/Foreground/Font: таких native свойств у Rectangle/Ellipse нет.

| Control | Discovered pairs | Displayable | Editable | Serializable | Importable | Exportable |
|---|---:|---:|---:|---:|---:|---:|
| ScrollBar | 199 | 48 | 39 | 41 | 41 | 39 |
| GridSplitter | 193 | 46 | 37 | 39 | 39 | 37 |
| Rectangle | 185 | 39 | 30 | 32 | 32 | 30 |
| Ellipse | 183 | 37 | 28 | 30 | 30 | 28 |
| HyperlinkButton | 203 | 50 | 38 | 40 | 40 | 38 |
| RepeatButton | 203 | 50 | 38 | 40 | 40 | 38 |

[Phase5APropertyGrid.csv](Phase5APropertyGrid.csv) дополнительно снимается с настоящих PropertyGrid rows после selection: количество rows/editable совпало с колонками Displayable/Editable таблицы. Для шести типов суммарно 270 rows и 210 editable rows. X/Y/Text — существующие model aliases (Canvas placement/Content), не новые Avalonia properties. Importable/Serializable включают source-only property state и не означают permission на structural editing или исполнение expressions. Exportable — доступен literal/layout adapter; таблица не утверждает независимую ручную acceptance каждой комбинации свойств.

## 5. Toolbox, DesignerSurface, Layout

Шесть descriptors зарегистрированы один раз. Добавлены небольшие glyph icons и логичные русские группы: Фигуры, Макет, Ввод, Навигация. Существующие provider groups, поиск, drag architecture, тема и цвета не перерабатывались.

Проверены настоящий native control в Designer, selection после routed Toolbox Drop, стабильный ControlId при Undo/Redo, Grid placement без Canvas offsets, Canvas absolute placement, Grid/StackPanel/DockPanel/WrapPanel и nested hierarchy, ParentId/child order, Multi Form. Для Canvas/деревьев используется прежняя проекция. GridSplitter в Designer выбирается/редактируется как компонент; native resizing выполняется в Preview/runtime.

Три подтверждённых дефекта, обнаруженных acceptance tests:

1. **Opacity применялась дважды**: StandardControlProperties.Create выставлял native Opacity, затем MainWindow.RenderDesigner задавал тот же коэффициент visualLayer. При 0,6 effective opacity была 0,36. Wrapper больше не повторяет opacity для действительно созданного native metadata control; legacy fallback сохраняет прежний путь. Добавлен regression test произведения opacity по visual ancestors.
2. **Тонкие controls раздувались до 40×24**: DesignControlModel.OnWidth/HeightChanged содержали старый общий minimum, GridSplitter Width=8 становилась 40 до экспорта. Для четырёх новых геометрических/тонких типов разрешён minimum 1 DIP; Inspector/resize paths используют эту политику. Helpers JsonIgnore, JSON contract не меняется. Старые controls/Canvas defaults, включая Separator, не мигрируются. Проверяется Width=8 в модели, JSON и native Designer.

3. **Отрицательные RepeatButton timing values**: IsValidValue(-10) у Delay/Interval в установленной Avalonia возвращает true, но при routed Space Down на native RepeatButton с Delay=-10 получен ArgumentOutOfRangeException из DispatcherTimer.Interval. Inspector тоже принимал это значение: отдельный первый regression — **0/1**, `timer-red.log`. В общий TryEncode добавлена проверка неотрицательных Delay/Interval, русская подсказка; допустимый zero сохранён. Теперь invalid edit показывает validation error и не меняет модель. Regression покрывает оба свойства. Это локальная managed validation ошибка новой группы; исходный UIA AV этим результатом не объясняется.

## 6. JSON, Undo, Multi Form

Используются существующие BuiltInProperties/CustomProperties.ValueJson и snapshots. Проверены Create/Edit → JSON → Open, тип/ControlId/ParentId/Grid placement, shapes brushes/radius, numeric/enum/URI values, Add Undo/Redo с восстановлением идентичности, source Reset/Undo и независимые формы. Существующие старые Grid/Canvas и property-grid Undo regressions запускаются отдельно. Runtime Avalonia objects в JSON не сохраняются.

## 7. AXAML Import и source-preserving safety

Для каждого нового типа проверены literal property edit, единственный минимальный patch, сохранность quote style/comments/unknown attribute, Undo до точного исходного текста, Redo, Reset и Undo Reset. Отдельно проверены Binding/StaticResource/DynamicResource и readonly коллекции. Source map/version/checksum/IPC не переписаны. Обнаруженный тип не означает разрешение структурного XML редактирования.

Styles/Resources/Templates и сложные expressions не исполняются импортером. Импортированные коллекции сохраняют ограничения Phase 4A/4B; массовый Add/Remove/Reorder XML nodes не добавлен. Shared factory применяется к поддержанному source subset, произвольный AXAML не объявляется визуально эквивалентным runtime.

## 8. Preview, Export, Generated Runtime

| Control | Designer native tree | Legacy native tree | AXAML native tree | JSON | Generated Grid/Canvas runtime |
|---|---|---|---|---|---|
| ScrollBar | PASS | PASS | PASS | PASS | PASS, range/viewport |
| GridSplitter | PASS, Width=8 | PASS | PASS | PASS | PASS, adjacent star tracks resize |
| Rectangle | PASS | PASS | PASS | PASS | PASS, Fill/Stroke/radius |
| Ellipse | PASS | PASS | PASS | PASS | PASS, Fill/Stroke |
| HyperlinkButton | PASS | PASS | PASS | PASS | PASS, native URI/IsVisited; browser не запускался |
| RepeatButton | PASS | PASS | PASS | PASS | PASS, реальные повторные Click и остановка после release |

Grid и Canvas проекты реально экспортированы, восстановлены, собраны и запущены отдельными процессами. Probe проверяет native type, присутствие в actual visual tree, ненулевые Bounds, реальные AP values, timer interaction RepeatButton и GridSplitter keyboard events. Designer → Legacy/AXAML geometry comparison для обоих root layouts проходит с допуском 1 DIP при одинаковой client area 1000×700 (Window chrome исключён из сравнения). В generated runtime дополнительно проверены authored Width/Height и resize 800×600, 1200×800, 1600×900; Canvas coordinates сохраняются. GridSplitter resize имеет смысл только с Grid parent; в Canvas проверяется presence/geometry, без заявления изменения соседних tracks. AXAML/C# code-behind/ViewModel собираются как самостоятельный generated project, placeholders для новых типов отсутствуют. Не создавался отдельный C# serialization pipeline.

Первый GridSplitter experiment ожидал изменения обоих pixel tracks. Native diagnostic показал корректную Avalonia pixel semantics (200→209 у первого, второй остаётся 200); тест исправлен на парные star tracks для проверяемого поведения. Это исправление предположения теста, не production workaround. NETSDK1138 старого generated net6.0 сохранён: TargetFramework не менялся.

## 9. Performance

Исходный ориентир Phase 4B: MainWindow import 49,45 мс / 35,30 МБ, native Measure/Arrange 192,73 мс; imported Preview около 597 мс и Undo P95 около 151,98 мс — прежние P2, в этом этапе не оптимизировались.

Сопоставимый large-fixture benchmark текущей фазы (5 warm-up + 30), baseline archive с тем же harness:

| Операция | Before Median/P95, мс | After Median/P95, мс | Median allocations Before/After, МБ |
|---|---:|---:|---:|
| MainWindow Import | 52,25/85,77 | 44,43/68,85 | 35,314/35,313 |
| ApplyDocument + Inspector init | 85,06/146,93 | 83,17/96,72 | 30,425/30,423 |
| Native Measure/Arrange | 184,77/291,33 | 185,14/273,34 | 61,273/61,283 |
| SourceMap lookup ×100 | 0,068/0,086 | 0,065/0,076 | <0,001/<0,001 |

Парсер не оптимизировался в Phase 5A; более низкое время import в одном сравнении не объявляется новым гарантированным ускорением. Подтверждённой регрессии большого AXAML нет. Состав MainWindow fixture и instrumentation одинаковы; source map не регенерируется иным алгоритмом.

**Финальный workflow benchmark с одинаковым cleanup** (`clean-before.json`, `clean-after.json`): каждая ячейка Median/P95 в мс. Inspector включает layout после реального перехода с TextBox; SaveLoad включает освобождение принадлежащей benchmark временной VM.

| Тип/версия | ToolboxCreate | Inspector | SaveLoad | Export AXAML | Legacy Preview | AXAML Preview |
|---|---:|---:|---:|---:|---:|---:|
| Button Before | 113,76/153,94 | 47,49/83,84 | 10,42/40,73 | 14,66/26,18 | 328,93/357,68 | 395,01/444,05 |
| Button After | 104,32/141,76 | 38,69/60,84 | 9,51/27,10 | 11,66/16,78 | 332,50/345,34 | 391,78/426,87 |
| NumericUpDown Before | 131,51/156,55 | 90,91/133,49 | 8,99/46,63 | 12,48/41,65 | 320,84/339,13 | 388,65/406,31 |
| NumericUpDown After | 118,95/144,30 | 83,77/130,70 | 8,78/48,78 | 9,23/18,62 | 330,70/345,05 | 387,48/406,82 |
| ScrollBar | 136,67/181,68 | 96,67/151,16 | 9,92/51,32 | 11,62/25,88 | 314,48/337,64 | 388,20/407,73 |
| GridSplitter | 126,67/156,25 | 82,05/158,82 | 10,51/50,35 | 12,54/33,54 | 328,91/340,33 | 382,46/395,13 |
| Rectangle | 103,17/115,80 | 70,78/99,68 | 9,56/42,26 | 10,72/48,41 | 323,00/337,12 | 381,10/405,72 |
| Ellipse | 101,50/127,91 | 56,42/82,68 | 9,74/18,26 | 11,00/22,59 | 332,70/359,18 | 364,95/383,08 |
| HyperlinkButton | 126,79/191,57 | 83,98/135,87 | 9,79/42,71 | 10,50/50,66 | 324,61/342,61 | 388,74/415,31 |
| RepeatButton | 135,43/180,20 | 86,91/147,04 | 10,66/45,01 | 14,83/39,05 | 327,44/338,64 | 389,49/412,27 |

| Inspector allocations | Before, МБ | After, МБ |
|---|---:|---:|
| Button | 1,576 | 1,590 |
| NumericUpDown | 10,657 | 10,662 |
| ScrollBar | — | 9,618 |
| GridSplitter | — | 8,031 |
| Rectangle | — | 7,098 |
| Ellipse | — | 5,858 |
| HyperlinkButton | — | 7,710 |
| RepeatButton | — | 7,911 |

MetadataBuilds во всех измеряемых workflow операциях после warm-up: **0**. GC counts за весь measured workflow: baseline 2 типа — 182/169/10, current 8 типов — 710/616/30 (Gen0/1/2). Объём работы различен: суммарные counts не являются before/after регрессией. Peak working set соответственно 990,92 и 1186,16 МБ; это cumulative peak тестового процесса с сотнями load/Preview, не обычный расход одного открытого проекта и не post-close retained heap.

Первые raw profiles сохранены: `workflow-before/after.json`. Там NumericUpDown Inspector дал 97,91 → 111,30 мс при 10,656 → 10,662 МБ. Дополнительный stage/CPU эксперимент (`trace-before/after.json`, по 30 + warm-up) не воспроизвёл устойчивое ухудшение: 92,20/215,74 → 86,33/135,25 мс, median process CPU 93,75 мс в обоих; rows=51, reused editors=58, created editors=34; metadata builds=0. Metadata lookup около 0,245 → 0,216 мс, RefreshPropertyInspector 14,71 → 14,28 мс; этапы вложенные, не суммируются. Основная стоимость остаётся в materialization/bindings/native layout, не в повторном discovery. Рост в первом прогоне нельзя обоснованно приписать Metadata Engine; timing variance и высокая стоимость UI остаются P2.

Также подтверждён **test harness lifecycle defect**: SaveLoad создавал временные VM и не вызывал существующий cleanup. Их удерживал static PropertyGridRowViewModel.ValueTrace; оставались VM timers/subscriptions. Исправлен только benchmark: DisposeExportOnlyViewModel в finally, остановка timers/отписки, assertion отсутствия target в static invocation list; production lifetime не переписывался. Финальные одинаковые before/after выполнены заново. Восьмитипный process peak уменьшился с 3856,93 до 1186,16 МБ; нельзя всю разницу объявить устранённой production утечкой. Старые raw цифры и эксперимент не удалены. Финальные wall time ещё зависят от CPU load, GC, Win32/UIA и native layout; performance SLA не вводится.


Методика нового workflow benchmark: Release, реальное Windows окно, один harness в archived baseline и текущей сборке; 5 warm-up + 30 замеров каждой операции/типа. Inspector — переход с другого выбранного control и synchronous UpdateLayout; это не время физического первого кадра. SaveLoad включает JSON и восстановление отдельной VM, export — генерацию AXAML; Preview включает существующий loading workflow. Current-thread allocations не включают все фоновые/native allocations.

Рядом выполнялся независимый пользовательский dotnet расчёт с заметной загрузкой и примерно 17 GB working set; он не останавливался. Сравнение отражает эту среду, абсолютные задержки и P95 нельзя считать лабораторными SLA. Benchmarks Designer выполняются последовательно, отдельно от нашей SQL/regression/build матрицы. Обычный Inspector Phase 3A и single-control workflow имеют разный состав сцены; напрямую смешивать их medians нельзя.

## 10. Memory, UI Automation, plugins

Для новой группы проверены 20 Legacy + 20 AXAML Preview Open/Close с WeakReference/lifecycle assertions, освобождение удалённых native controls при живом Designer/VM, отсутствие накопления закрытых Preview. Полная Preview Memory suite повторяется с живой изолированной SQL LocalDB. GC используется только тестами, production GC.Collect не добавлен.

Metadata продолжает ConditionalWeakTable<Type, Lazy<...>>. Дополнительный immutable support entry содержит только явно поставляемые framework types/native property keys; plugin Type/Assembly в новом глобальном strong cache не попадают. Eremex plugin boundary/themes/лицензии не менялись; стандартные controls не требуют Eremex. Existing isolation/dependency closure/IPC vertical slices проверены в общей регрессии.

UIA native crash в uiautomationcore.dll/ProviderCallouts::GetPatternProvider остаётся **UNRESOLVED**. Причина null provider не установлена, accessibility не отключалась, interop не менялся. Успешные циклы не являются исправлением исходного AV. Использовался существующий process-local NativeCapture с отдельными run directories, metadata/hashes; binary dumps не добавляются в Git. Удержание обычных Window с внешним UIA client остаётся отдельным известным ограничением; collectibility Preview не доказывает его отсутствие.

Проверено 50 manifests NativeCapture для runner/diagnostic/profile процессов: Captured=0, CaptureFailed=0, TimedOut=0. Четыре ненулевых exits — сохранённые assertion failures opacity, pixel GridSplitter ожидания/diagnostic и negative RepeatButton validation; это не native AV. Generated runtime probes завершились exit 0; они проверяются отдельными process/result outputs. Ноль новых captured AV не доказывает исправление исторического null-provider crash.

## 11. Build / regression / SQL / VSIX

| Проверка | Результат |
|---|---|
| Solution Release / VSIX packaging | PASS, 0 errors, один прежний NU1603 SDK Analyzers 17.7.20 → cached 17.7.22 |
| Smoke runner build | PASS, 0 errors, 0 warnings |
| Phase 5A final | **47/47**: 6×registration/native Grid/Canvas/routed drop/source round-trip, URI/NaN, installed package discovery, opacity, geometry, GridSplitter, hierarchy, Multi Form, memory |
| Metadata / PropertyGrid / Collections | **45/45 / 30/30 / 30/30** |
| Phase 2 / Phase 4A / Phase 4B | **40/40 / 38/38 / 52/52** |
| Reset/Undo Phase 3B.3 / Stabilization | **15/15 / 25/25** |
| Broad, normal environment | **72/72** |
| Nested Layout / Pointer / Grid Drop | **59/59 / 26/26 / 40/40** |
| Grid Editor / Layout 7A | **45/45 / 15/15** |
| PostgreSQL offline | **20/20**, live SKIP |
| SQL Server LocalDB | **47/47**, CONTRACT и LIVE обозначены раздельно; реальные Customers=3, Orders=2 |
| SQL source workflow | PASS, settings/schema/BindingSource, оба Preview, настоящие значения ячеек |
| Preview Memory + live SQL | **36/36**, 20+20 cycles, CloseDuringQuery, DataContext/ItemsSource/lifecycle/UIA regressions |
| SQL generated project | restore/build/separate-process run PASS; Grid Customers=3, nested Grid Orders=2, независимые VM/forms, resize |
| Property-element Named Pipes IPC | **1/1**, три последовательных изменения, ApplyPatch/ACK/rebase/version/checksum |
| Generated collections Grid / Canvas | restore/build/run PASS: Menu/nested/Separator, Combo/List, Tab content, Tree, ContextMenu/Flyout, resize |
| Compiled patched AXAML source | restore/build/run PASS: scalar values, nested controls/collections, selection, 3 размера |
| Новые controls generated Grid / Canvas | restore/build/run **PASS/PASS**, native=6, authored bounds/brushes/range/URI/repeat/resize |
| Eremex / plugins / VSIX dependency boundary | Broad vertical slices и isolation/dependency closure PASS; интерактивная Visual Studio не проверена |

Полная матрица находится в artifacts/universal-phase5a/final-validation. После уточнения audit scope (ColorPicker/ItemsRepeater/Viewbox) выполнены отдельные финальные build, **46/46 + 45/45 + 52/52 + 38/38** (до найденной затем timing validation ошибки), новый inventory и повторный generated runtime шести controls; логи verified-* и runtime-verified-*. После timing fix выполнены ещё один solution build и **47/47 + PropertyGrid 30/30 + Metadata 45/45 + Phase 4A 38/38**; положительные timing/интерактивные сценарии также проверены. После сохранения двухаргументной сигнатуры coverage API — отдельный финальный solution/smoke build и audit с теми же числами, без изменения rendering/validation logic. Остальные green результаты относятся к полной предшествующей матрице с теми же шестью control adapters, до этого узкого metadata-аудита.

Старые generated net6.0 projects выдавали NETSDK1138; финальные restore также выдали NU1900 при недоступности vulnerability feed nuget.org. Restore/build/run успешны, это не подтверждение security audit зависимостей. В Broad сохранён прежний cleanup warning IOException: загруженный LayoutContainerExport не удалось удалить; принудительная очистка не выполнялась.

LocalDB использовал собственный instance `UniversalMetadata_293a4a760e6f426b93c73222ae4d8564` (остановлен и удалён в finally) и только fixture DB, cleanup в finally. Пользовательские базы и серверы не менялись. Проверяются настоящие строки/ячейки, fake demo rows не добавлялись. PostgreSQL live без сервера не заявляется.

VSIX/VsHost проверяются build/packaging и настоящим Named Pipes процессным тестом ApplyPatch/ACK/version/checksum. Архитектура внешнего хоста не меняется; новые Avalonia dependencies не переносятся в devenv.exe. Ручная проверка установленного расширения не подменяется автоматическим IPC.

## 12. Первые красные прогоны

Сохранены `opacity-red.log` (0/1, подтверждённая double opacity), `checks2.log` (38/40, неверное ожидание pixel GridSplitter resize), `runtime-first-grid.log` и `splitter-diagnostic.log` (отдельный assertion failure того же pixel-track ожидания). После исправлений отдельные успешные прогоны не переименовывают первые в зелёные. Дополнительно сохранён `timer-red.log` (0/1) с native routed-key воспроизведением и принятым Inspector отрицательным Delay; после минимального guard отдельный test 1/1 и full 47/47. Build/codec validation logs и native capture manifests находятся в ignored artifacts/universal-phase5a; retries для маскировки native crash не использовались.

## 13. Изменённые файлы

* DesignerSystem/Metadata/{AvaloniaMetadataCatalog,StandardControlProperties,MetadataCoverageReport}.cs.
* DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs.
* DesignerSystem/AxamlRoundTrip/AxamlImportService.cs.
* ViewModels/MainWindowViewModel.cs и MainWindowViewModel.Metadata.cs.
* Models/DesignControlModel.cs и ToolboxItem.cs.
* Views/MainWindow.axaml.cs и DesignerToolbox.axaml.
* smoke-tests/FormDesigner.ExportSmokeTests/Program.cs, новые Program.Universal5A.cs и Program.Universal5APerformance.cs.
* smoke-tests/UniversalMetadata/Phase5ARuntimeProbe.cs.txt и README.md.
* Docs/UniversalControlCoverage.csv, UniversalPropertyCoverage.csv, UniversalCoverageSummary.json, Phase5APropertyGrid.csv и настоящий отчёт.

Raw logs, before archive, generated projects, benchmark JSON, SQL fixture и capture evidence остаются вне Git в artifacts/universal-phase5a. Секреты/локальные настройки/базы/dumps не включаются.

## 14. Ограничения, ручная acceptance и следующий этап

1. Новая группа поддерживает проверенный literal subset; Geometry/Points/StrokeDashArray, arbitrary Content/Commands/Templates/Styles/Resources не заявляются универсально editable. HTTP/HTTPS URI restriction намеренная; full URI scheme editor не добавлялся.
2. Viewbox/UniformGrid/ScrollViewer/Expander требуют полного container adapter и pipeline tests; discovery не включает их в JSON Designer автоматически. Остальные missing candidates видны в inventory.
3. Imported Preview остаётся частичной native projection Phase 4B; структурное редактирование source collections readonly, исходный текст не регенерируется.
4. UIA original native AV unresolved; full external-client stress/root cause investigation не повторялись без новых данных.
5. Нужна ручная acceptance: Toolbox glyphs/тонкие resize handles, реальные pointer drag GridSplitter в Preview/runtime, HyperlinkButton navigation с явным нажатием, accessibility/screen reader, несколько DPI/мониторов, установленный VSIX с реальным text buffer и повторным открытием документов. Keyboard и native runtime проверки не заменяют эту UX-матрицу.
6. PostgreSQL live, Linux/macOS и общая cross-platform fidelity не проверены. Existing warnings/performance costs не скрываются.

Рекомендуемое продолжение после отдельного решения: небольшой reviewed single-child/panel adapter для Viewbox/ScrollViewer/UniformGrid; затем безопасный Point/Geometry/Points editor и Shape collections. Поддержку контейнеров подтверждать native child hierarchy/runtime, а не только регистрацией. Phase 5B и Phase 4C автоматически не начинались.

## 15. Итоговая оценка

**STANDARD CONTROLS: READY WITH CONDITIONS.**

Шесть новых controls проходят проверенные Designer/PropertyGrid/JSON/native Preview/export/generated-runtime workflows; три подтверждённые ошибки (двойная Opacity, minimum размера, negative RepeatButton timing) устранены минимально и покрыты регрессиями. Новых подтверждённых P0/P1 после исправлений в проверенной матрице нет.

Условия: property coverage ограничено безопасными reviewed capabilities; сложные коллекции/Geometry/Templates не объявлены editable. UIA original AV unresolved, live PostgreSQL и установленный VSIX/DPI/accessibility требуют отдельной acceptance. Высокие UI allocations/P95 остаются P2, хотя повторный metadata discovery не зафиксирован. Полная поддержка всех стандартных Avalonia типов/свойств не заявляется. Phase 5A завершена, Phase 5B/4C не начинались.
