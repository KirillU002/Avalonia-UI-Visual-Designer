# Universal Controls & Properties — Phase 5B

Дата: 15.11.2026. Baseline `759b86abb73188075b8fb43a254cebb71e186d08`, main, Beta 0.1. До изменений Git working tree чистый. Avalonia 11.1.1, Designer net8.0, Windows 10 x64 build 19045, SDK 10.0.402 / runtime 8.0.32. NuGet, TargetFramework, global theme, VSIX/IPC и Preview default не менялись. Commit/tag/release не создавались. Phase 4C и следующая фаза не начинались.

## 1. Результат и scope

Добавлены **Viewbox, UniformGrid, Expander, ScrollViewer, Line, Polygon, Polyline, Path**. Это восемь reviewed вертикалей с конкретным безопасным property subset. Discovery не объявляется полной поддержкой стандартной библиотеки.

Контейнеры используют прежние ParentId/StackOrder/CustomProperties и native Avalonia Measure/Arrange. Добавлены два значения существующей строки ChildLayoutMode: Content и Uniform; новые поля/версия JSON не вводились. Обычные старые Canvas/Grid документы не мигрируются. LayoutSemanticsVersion сохраняется; для Canvas используется существующий LegacyCanvasLayoutProjection managed-subtree adapter.

Shapes используют существующие Metadata Engine, PropertyGrid и scalar bag с property-specific codecs. Для geometry сохраняется проверенный текст mini-language, без Geometry.ToString как serialization contract. Отдельный PropertyGrid, коллекционная модель или rendering/export pipeline не создавались.

## 2. Аудит стандартной библиотеки

Inventory получен из реально подключённых Avalonia.Controls/Base, стандартного DataGrid, ColorPicker и ItemsRepeater. Все публичные Control-derived типы, включая abstract/base/infrastructure для аудита, находятся в [UniversalControlCoverage.csv](UniversalControlCoverage.csv); property capabilities — [UniversalPropertyCoverage.csv](UniversalPropertyCoverage.csv), сводка — [UniversalCoverageSummary.json](UniversalCoverageSummary.json).

Категории: 1 — проверенная native вертикаль с ограниченными properties; 2 — существующие adapters/partial capabilities, регрессии перечислены отдельно; 3 — source-only subset без прямого Toolbox; 4 — discovery candidate без Designer adapter; 5 — нужен специализированный item/object editor; 6 — root/base/infrastructure, не обычный Toolbox control. Runtime PASS выставляется только по предоставленному acceptance evidence, не по Reflection.

| Показатель | Phase 5A | Phase 5B |
|---|---:|---:|
| Публичные Control-derived типы в inventory | 146 | 146 |
| Built-in descriptors (включая alias Group) | 33 | 41 |
| Native types с прямым Toolbox descriptor | 32 | 40 |
| Пары type/property/alias | 8951 | 10043 |

Категории текущего CSV: native acceptance с ограниченным subset — 14; остальные зарегистрированные adapters — 26; source subset без Toolbox — 6; обнаружены без adapter — 22; нужен specialized editor — 3; root/base/infrastructure — 75. Даже первая категория не означает все свойства Avalonia.

Оставшиеся candidate types без прямого Toolbox descriptor: AutoCompleteBox, ButtonSpinner, CalendarDatePicker, Canvas, Carousel, ColorPicker, ColorView, ComboBoxItem, DockPanel, ExperimentalAcrylicBorder, ItemsRepeater, Label, LayoutTransformControl, ListBoxItem, MaskedTextBox, MenuItem, PathIcon, RefreshContainer, RelativePanel, ReversibleStackPanel, SelectableTextBlock, Arc, Sector, SplitView, TabItem, ThemeVariantScope, ToggleSplitButton, TransitioningContentControl, TreeViewItem.

Canvas/DockPanel в этом списке не означают утрату layout поддержки: они представлены root/существующим Group layout adapter. ComboBoxItem/ListBoxItem/MenuItem/TabItem/TreeViewItem редактируются через существующие коллекции; они не отдельные свободные Toolbox roots. Остальные кандидаты требуют отдельной pipeline acceptance. Abstract/base/infrastructure не регистрировались автоматически.

Проверены actual APIs: Viewbox.Child (не WPF Content), UniformGrid.Rows/Columns/FirstColumn, Expander.Header/Content/IsExpanded/ExpandDirection, Shape.StrokeJoin (не выдуманный StrokeLineJoin), Polygon/Polyline.Points типа IList<Point>, Shape.StrokeDashArray типа AvaloniaList<double>, Path.Data типа Geometry.

## 3. Существующие дополнительные controls

Menu/TreeView/TabControl, ContextMenu/Flyout и item hierarchy уже реализованы Phase 3B. Они не дублировались в Toolbox. MenuItem/TabItem/TreeViewItem остаются collection-owned workflows, не свободными layout roots. ItemsControl требует полноценного item/template adapter и не регистрировался автоматически. Image/Border уже имеют descriptors и прежние специализированные handlers; известные ограничения source/style/content остаются. ContentControl и HeaderedContentControl рассматриваются как архитектурные базовые типы; их наличие не означает готовый универсальный Content editor. GroupBox не обнаружен в установленных assemblies.

Полный remaining inventory с Namespace/Assembly/BaseType, JSON, Inspector, import, Preview/export/runtime и причинами — в CSV. Abstract и служебные classes не добавлялись в Toolbox.

## 4. Shared metadata и typed properties

* Viewbox: Stretch, StretchDirection, один Child.
* UniformGrid: Rows, Columns, FirstColumn, ordered Children. 0 для Rows/Columns означает native auto count. Отрицательные counts/FirstColumn отклоняются до изменения модели; Grid.Row/Column не выдаются за механизм размещения UniformGrid children.
* Expander: literal/null Header через reviewed object-property policy, IsExpanded/ExpandDirection и один Content. Объектный Header/Binding/Resource не превращается в строку.
* ScrollViewer: HorizontalScrollBarVisibility/VerticalScrollBarVisibility, AllowAutoHide и один Content. Runtime scrolling использует native Extent/Viewport/Offset.
* Line: StartPoint/EndPoint, Stroke/StrokeThickness/dashes/Stretch, applicable inherited properties.
* Polygon/Polyline: Points, applicable Fill/Stroke/StrokeThickness/Stretch/StrokeJoin/dashes.
* Path: Data mini-language, Fill/Stroke/StrokeThickness/Stretch/StrokeJoin/dashes.

Общие свойства берутся из прежних adapters и Avalonia registration metadata. Descriptor/Inspector/export appearance фильтруются по настоящим native APs: у Viewbox/Shapes не появляются фиктивные Background/Foreground/Font, у UniformGrid нет Font. Категории, Favorites, поиск, русский hint, Value Source, Reset и Undo используют существующий PropertyGrid. ChildLayoutMode/Orientation/Spacing switch для фиксированных native container semantics скрыт.

## 5. Point / Points / dash / Geometry codecs

Point — invariant x,y с проверкой finite values. Points и dash array — только property-specific reviewed keys Shapes; любой IEnumerable не становится editable. Поддержан literal coordinate format `0,0 140,0 70,80`; dash `3,2`, неотрицательные значения, empty array и null. Для Data используется StreamGeometry.Parse стандартной Avalonia mini-language, без исполнения пользовательского кода.

Ограничения литералов: до 16384 символов, до 2048 числовых tokens / 1024 points; finite coordinates с модулем ≤1e6. Неизвестные команды/невалидная grammar отклоняются; Bindings/Resources/markup extensions не парсятся как geometry. JSON хранит string literal, после load создаётся новый native value. Исходный Data/Points/dash текст после validation сохраняется; Point может нормализовать пробелы/числовую запись. Serialization не вызывает произвольные getters/ToString complex objects.

Object/property-element Geometry (GeometryGroup/PathGeometry и др.), Bindings, Styles, Resources и Templates остаются read-only/partial source capabilities. Полный визуальный Geometry/Points editor не реализовывался. Эти ограничения не скрыты за TextBox.

| Control | Discovered | Displayable | Editable | Serializable | Importable | Exportable | Inspector editable keys |
|---|---:|---:|---:|---:|---:|---:|---:|
| Viewbox | 176 | 31 | 23 | 25 | 25 | 23 | 23 |
| UniformGrid | 179 | 33 | 25 | 27 | 27 | 25 | 25 |
| Expander | 198 | 47 | 37 | 40 | 40 | 37 | 37 |
| ScrollViewer | 211 | 49 | 37 | 42 | 42 | 37 | 37 |
| Line | 185 | 39 | 31 | 33 | 33 | 31 | 31 |
| Polygon | 184 | 36 | 28 | 30 | 30 | 28 | 28 |
| Polyline | 184 | 36 | 28 | 30 | 30 | 28 | 28 |
| Path | 184 | 36 | 28 | 30 | 30 | 28 | 28 |

Сводные capability flags по всем зарегистрированным типам: Displayable=2077, Editable=1263, Serializable=1699, Importable=1644, Exportable=1268. Это capability inventory, включая aliases и контекстные placement properties, а не число одновременно доступных строк. Actual Inspector keys отдельно выгружены в [Phase5BPropertyGrid.csv](Phase5BPropertyGrid.csv); различия обусловлены parent context, layout и specialized rows. 237 editable пар добавлены восемью типами, ещё две — StrokeDashArray у Rectangle/Ellipse.

## 6. Native container adapter и DesignerSurface

В прежнем AxamlLayoutProjection.BuildJson добавлена reviewed ветка контейнеров. UniformGrid получает actual ordered children; Viewbox/ContentControl получают один Child/Content. Нарушение capacity не отбрасывает детей молча: create/reparent/drop/load/export guards сохраняют текущую модель и сообщают ограничение. Несколько компонентов помещаются во вложенный Grid/StackPanel/etc.

Designer переиспользует прежние selection/resize wrappers внутри настоящих native hosts. Child layout/масштабирование остаётся Avalonia; wrapper Canvas — служебный overlay, не пользовательский root и не экспортируемый Content. Для старых Canvas managed subtree переиспользован прежний native factory mode. Обычные старые Canvas controls/координаты/JSON fallback не преобразуются.

Проверены native hierarchy, nested ParentId/order, прозрачные shapes, routed pointer selection и resize, zoom, scaled Viewbox selection/bounds, root Grid/Canvas, native Drop во все четыре пустых hosts, Undo/Redo и Multi Form. Explorer получает существующие model nodes/descriptors и container badges; новая параллельная иерархия не вводилась.

Empty Viewbox без Child в Avalonia имеет нулевые arranged bounds. Для Designer measurement/interaction временно используется прозрачный native Border target 48×32, аналогичный существующей minimum target policy. Это только editor chrome: model child не создаётся, JSON/export остаются empty Viewbox, Preview/runtime не получают placeholder. Специальный тест проверяет отсутствие scaffold в AXAML/модели.

## 7. Подтверждённые проблемы и минимальные исправления

1. **Legacy implicit spacing у новых hosts**: PlacementAttributes при неизвестном не-Canvas режиме добавлял LayoutSpacing в Margin детей Content/Uniform. Viewbox child 150×60 получал дополнительные 12 DIP, Designer/Legacy fit 300×120 расходился с AXAML fit 300×144. Для новых modes spacing не инъектируется; explicit Margin сохраняется. Старые Stack/Grid правила не переписывались.
2. **Native children отображались старым drawn Canvas builder**: при actual Designer render flag managed-native factory уже снимался; nested Button/TextBox становились Border imitation. В пределах нового native host переиспользуется тот же native factory flag, что при existing managed measurement; вне host старый Canvas workflow сохранён.
3. **Fixed dimensions внутри managed Canvas**: native Button/TextBox создавались без Width/Height и принимали theme natural size (например 115×41 вместо 120×35). Designer wrapper теперь применяет уже измеренные dimensions в существующем managed-native режиме.
4. **Alignment native theme vs wrapper**: v1 export пропускает default Top/Stretch, TextBox получает theme Center; wrapper навязывал Top, что давало 10 DIP разницы в UniformGrid. Использованы реальные effective alignment после native measurement; хранится только value-only enum snapshot, не controls/VM.
5. **Viewbox transformed bounds**: старые selection/drop bounds переводили origin, но сохраняли unscaled Width/Height. Применён native TransformToAABB для wrapper/drop geometry. Layout engine не дублировался.
6. **Пустой Viewbox не принимал первый Drop**: воспроизведён отдельным routed test 3/4; добавлен описанный editor-only target. Полный набор Child/Content guards не разрешает silently overwrite существующего ребёнка.
7. **Занятый Content в Canvas Drop (P1)**: TryCreateControlFromToolboxDrop вызывал throwing capacity guard CreateControlCore, а UI Drop не обрабатывал это исключение. Отдельный тест сначала упал; добавлена preflight-проверка с StatusText и возвратом null до изменения модели. Проверены Viewbox/Expander/ScrollViewer, controls остаются неизменными.
8. **Fill Line/Polyline не достигал native control (P1)**: общий starter shape preset добавлял Fill в CustomProperties; Inspector показывал fallback row, но свойства не было в reviewed StandardControlProperties и общей генерации. ShapeFillPipeline воспроизвёл native Line.Fill=null после редактирования. Fill — реальное унаследованное Shape property; оно добавлено в ту же metadata policy, поэтому Brush editor, native setter, import и serializer теперь используют общий путь. Проверены оба Preview, source import и заново собранные Grid/Canvas runtime.
9. **Незавершённая Path mini-language (P1 validation)**: Avalonia PathMarkupParser.ReadDouble для `M 0` выбрасывает System.IO.InvalidDataException. Codec обрабатывал FormatException/ArgumentException/OverflowException, поэтому TryEncode пропускал исключение вместо validation failure. Добавлена обработка только этого подтверждённого parser exception в ShapeLiteralCodec.TryParse. Проверяются пустые/неполные M/L/Q/A, нечётные Points и invariant Point round-trip при ru-RU. Исключения native interop/AV не подавляются.

## 8. JSON, Undo/Redo, Multi Form

JSON contract не расширен runtime objects. Все новые literals проходят прежний CustomProperties.ValueJson. Проверено создание/редактирование, save/open, stable Id/ParentId/order, Points atomic Undo/Redo, source Reset/Undo, смешанные формы с Polygon/Path, оба root layouts. UniformGrid settings и Expander state восстанавливаются как native AP values. ParentId cycles/invalid parents обрабатываются существующими guards; capacity single-content проверяется дополнительно до ApplyDocument.

Неудачный create/reparent не меняет controls. Workspace serialization обновляет form.UpdatedUtc (`PersistActiveFormDocumentState`), поэтому byte equality двух ExportWorkspaceJson вызовов не является проверкой отсутствия model mutation; regression сравнивает фактическую коллекцию controls.

## 9. AXAML Import / Round-trip

Новые типы используют общие metadata/literal accessors. Добавлены container source semantics для Viewbox.Child/UniformGrid.Children; прежние Expander/ScrollViewer source paths сохранены, включая scalar Content у Expander. Для стандартных типов принимаются default Avalonia namespace, точные `using:...` и `clr-namespace:...;assembly=...` их доверенной assembly; это не загрузка произвольной DLL.

Для каждого типа проверены minimal edit, Reset, Undo/Redo, точный исходный текст при Undo, unknown attributes/comments/namespaces/Binding/Resource preservation. Object Geometry остаётся opaque/read-only. Структурное редактирование imported collections не добавлялось, full AXAML regeneration не выполняется. Старые SourceMap/version/checksum/ApplyPatch/ACK guards сохраняются.

## 10. Preview / Export / Generated Runtime

| Control | Designer native | Legacy native | AXAML native | JSON | Generated Grid/Canvas |
|---|---|---|---|---|---|
| Viewbox | PASS, native Child wrappers | PASS | PASS | PASS | PASS, native scaling |
| UniformGrid | PASS, ordered Children | PASS | PASS | PASS | PASS, Rows/Columns/FirstColumn и automatic placement |
| Expander | PASS | PASS | PASS | PASS | PASS, реальные keyboard header toggles и сохранённый Content |
| ScrollViewer | PASS | PASS | PASS | PASS | PASS, Extent/Viewport и изменение Offset |
| Line | PASS | PASS | PASS | PASS | PASS, native Point values/Stroke/dashes |
| Polygon | PASS | PASS | PASS | PASS | PASS, IList<Point> contents/Fill/Stroke |
| Polyline | PASS | PASS | PASS | PASS | PASS, native points/dashes |
| Path | PASS | PASS | PASS | PASS | PASS, Geometry bounds/Fill/Stroke |

Новые Grid/Canvas generated projects реально прошли restore/build и отдельные процессы runtime. Проверки не ограничены наличием тега в AXAML. `exports-second`/`runtime-second-*` и исправленный Canvas resize probe сохраняют evidence. Runtime использует стандартные Avalonia controls; Designer scaffold в проект не экспортируется.

Геометрия Designer/Legacy/AXAML сравнивается по native transformed frames с допуском 2 DIP и одинаковой client area, включая nested controls. Это не pixel-perfect обещание для произвольной темы/платформы. Grid и Canvas проекты экспортированы отдельно; runtime probes проверяют native types/hierarchy/properties, реальные keyboard toggles Expander, scrolling, Viewbox scaling и resize 800×600/1200×800/1600×900. Generated C#/ViewModel/startup компилируются в составе настоящего проекта; нового отдельного C# exporter не создано.

Canvas.GetLeft/Top — положение authored slot. Viewbox может вернуть fitted native Bounds внутри него; runtime test сравнивает стабильность native coordinates и attached values при resize, а не вводит произвольную поправку. Root Canvas и Grid сохраняются, скрытого Canvas в новых native hosts нет.

## 11. Performance

Сопоставимая пара: git archive HEAD Phase 5A и финальная реализация, один portable harness, 5 warm-up + 30 samples каждого типа/операции. Числа — median/P95 в мс; allocations — median MB текущего UI thread.

| Существующий control / операция | Before median/P95 | After median/P95 | Allocations Before → After, MB |
|---|---:|---:|---:|
| Button / ToolboxCreate | 128,19/207,83 | 94,91/154,23 | 14,20 → 14,20 |
| Button / Inspector | 62,53/126,45 | 38,80/101,04 | 1,52 → 1,52 |
| Button / SaveLoad | 10,19/38,07 | 9,04/22,70 | 1,61 → 1,69 |
| Button / ExportAxaml | 20,50/41,84 | 12,04/52,21 | 0,63 → 0,63 |
| Button / LegacyPreview | 327,32/350,28 | 332,67/347,95 | 7,67 → 7,68 |
| Button / AxamlPreview | 388,85/418,05 | 377,76/413,29 | 11,44 → 11,44 |
| NumericUpDown / ToolboxCreate | 163,95/211,47 | 129,69/159,56 | 20,37 → 20,37 |
| NumericUpDown / Inspector | 115,41/174,21 | 86,59/125,95 | 10,17 → 10,17 |
| NumericUpDown / SaveLoad | 10,58/39,22 | 8,94/38,43 | 1,61 → 1,69 |
| NumericUpDown / ExportAxaml | 19,74/31,35 | 11,49/19,83 | 0,62 → 0,62 |
| NumericUpDown / LegacyPreview | 332,22/358,50 | 324,80/361,72 | 10,19 → 10,19 |
| NumericUpDown / AxamlPreview | 389,12/406,55 | 382,38/389,33 | 11,90 → 11,89 |

| Новый control | Toolbox median/P95 | Inspector median/P95 | Inspector allocations, MB | Legacy Preview median/P95 | AXAML Preview median/P95 |
|---|---:|---:|---:|---:|---:|
| Viewbox | 85,36/113,63 | 54,57/81,71 | 3,96 | 324,51/341,78 | 376,86/390,16 |
| UniformGrid | 90,68/111,98 | 59,56/94,52 | 4,77 | 330,19/357,10 | 377,21/389,46 |
| Expander | 125,72/158,67 | 84,18/131,83 | 7,58 | 329,48/338,86 | 382,98/395,40 |
| ScrollViewer | 114,88/145,88 | 79,98/122,10 | 7,39 | 327,23/337,79 | 382,73/395,08 |
| Line | 99,19/149,06 | 72,35/136,50 | 7,46 | 327,45/364,67 | 376,76/407,71 |
| Polygon | 106,53/144,40 | 71,67/146,69 | 5,80 | 332,63/339,95 | 387,96/393,93 |
| Polyline | 106,50/149,49 | 67,05/120,90 | 5,80 | 326,78/338,58 | 365,21/395,04 |
| Path | 107,05/145,35 | 67,85/138,41 | 5,80 | 330,43/337,98 | 388,37/431,07 |

Полные SaveLoad/Export/GC и остальные парные результаты: [Phase5BPerformance.csv](Phase5BPerformance.csv). Metadata builds после warm-up: 0. Preview измеряет штатную загрузку, а не только создание Control.

После последних Fill/parser изменений заново измерены Line/Polyline и обе 100-control сцены; результаты остальных неизменённых типов сохранены из предыдущего After-процесса. Import before/after повторён последовательно на финальном build. Первичные datasets сохранены с суффиксом `-initial`, происхождение объединённой таблицы записано в `dataset-provenance.json`. В каждой итоговой группе ровно 30 samples после 5 warm-up.

Наблюдаемое уменьшение median обычного Inspector не объявляется отдельной оптимизацией. Хвост Button Export вырос: P95 41,84 → 52,21 мс при median 20,50 → 12,04 мс и почти одинаковых allocations. Один из хвостовых samples содержит Gen0/Gen1 GC; причина всего разброса не установлена. Это отдельное P2-ограничение измерения, а не доказательство постоянной регрессии; для вывода нужен профиль Export без чередования с Preview. Масштабная оптимизация не выполнялась.

| Import scene | Before median/P95 | After median/P95 | Allocations Before → After, MB |
|---|---:|---:|---:|
| Small | 0,93/1,94 | 1,03/1,23 | 0,22 → 0,22 |
| Medium | 9,45/11,54 | 9,71/11,71 | 2,02 → 2,02 |
| MainWindow | 46,67/72,32 | 43,90/66,94 | 33,66 → 33,68 |

Исторические Phase 4B 49,45 мс / 35,30 MB / Native MeasureArrange 192,73 мс служат контекстом; вывод о регрессии делается по новой сопоставимой паре. Первичный import-before.json сохранён отдельно, итоговая таблица использует парные performance-final/import-before/after. Native Measure/Arrange большого MainWindow отдельно в этой фазе не перепрофилирован; 100-control Preview ниже включает реальную native layout работу.

| Стресс-сцена / операция | Median/P95, мс | Allocations, MB | GC 0/1/2 за 30 samples |
|---|---:|---:|---|
| 100Shapes / Add | 59,51/73,51 | 21,43 | 18/9/4 |
| 100Shapes / Inspector | 23,44/49,98 | 3,86 | 5/3/0 |
| 100Shapes / SaveLoad | 173,96/268,98 | 90,81 | 52/43/14 |
| 100Shapes / Export | 8,02/34,64 | 4,99 | 9/5/1 |
| 100Shapes / LegacyPreview | 363,57/405,47 | 88,21 | 88/40/8 |
| 100Shapes / AXAMLPreview | 576,32/631,00 | 122,93 | 78/41/14 |
| 100Nested / Add | 55,33/64,84 | 18,47 | 9/0/0 |
| 100Nested / Inspector | 50,73/70,26 | 6,44 | 15/8/3 |
| 100Nested / SaveLoad | 154,02/230,34 | 74,04 | 35/31/6 |
| 100Nested / Export | 59,77/77,51 | 5,15 | 4/3/0 |
| 100Nested / LegacyPreview | 364,57/392,49 | 74,23 | 76/33/3 |
| 100Nested / AXAMLPreview | 607,08/649,70 | 124,18 | 98/58/9 |

Для новых 100-shape/100-nested сценариев прежней полноценной native вертикали не было; это измеренные текущие затраты, не заявление об ускорении. Разброс представлен P95; GC и сырые samples сохранены в ignored performance-final. Максимальный peak working set profile controls: Before 937,84 MB, After 1065,75 MB. After объединяет процессы для 10 типов и повторных Line/Polyline; поэтому peak не сравнивается как leak metric.

Методика: Release, штатный UI dispatcher/Window, process-local crash capture; одинаковый portable Phase 5A profile в git archive baseline и current. Warm-up + не менее 30 samples. Без параллельных наших build/SQL/regression. Внешняя нагрузка ОС не фиксировалась; ранний baseline при иной нагрузке сохранён отдельно от сопоставимых пар. Inspector включает synchronous UpdateLayout, не физический first frame. Preview включает прежний loading workflow; current-thread allocations не равны полному native memory расходу.

Temporary benchmark VMs очищаются прежним cleanup с отпиской static ValueTrace/timers; production GC.Collect не добавлен. Сырые первые результаты и повторные эксперименты не удаляются.

## 12. Memory, UI Automation, plugins

Для новой группы выполняются 20 Legacy + 20 AXAML Preview cycles с forced-root WeakReference/lifecycle assertions; полная Preview Memory повторяется с SQL. Metadata cache остаётся ConditionalWeakTable + fixed trusted framework policies; новые global strong refs на plugin Type/Assembly не добавлены. Container alignment cache содержит только IDs/enums.

UIA historical null provider crash (`uiautomationcore.dll`, ProviderCallouts::GetPatternProvider) остаётся **UNRESOLVED**. Успешные cycles не объявляются исправлением. Accessibility/interop не отключались и не менялись. Существующий NativeCapture сохраняет отдельные run directories/metadata/hash; dumps не добавляются в Git. Внешний client-dependent ordinary Window retention остаётся отдельным известным риском.

Новая группа: 20 Legacy + 20 AXAML cycles — PASS, closed PreviewWindow/DataContexts и managed references не удерживаются проверяемыми roots. Полная SQL-enabled Preview Memory: **36/36**, включая close during load/query, cancellation и multiple DataGrid. Metadata collectible cache regression: **45/45** вместе с остальными metadata assertions.

На момент финального отчёта сохранены 65 process capture manifests этого этапа, Captured=0, CaptureFailed=0, TimedOut=0. Nonzero managed assertion exits сохранены отдельно; это не native AV. Ноль новых AV не устанавливает root cause исторического UIA crash. После native/JSON projection и закрытия окон дополнительно прошли Stabilization **25/25**, но это не отменяет client-dependent обычный Window retention при иных UIA условиях.

## 13. Build / regression / SQL / VSIX

| Проверка | Результат |
|---|---|
| Build Solution Release | PASS, 0 ошибок; прежний NU1603 VS SDK analyzer resolution |
| Smoke runner Release | PASS, 0 ошибок/предупреждений |
| Phase 5B | первоначальный полный 67/67; после capacity fix 71/71; финальный расширенный набор **73/73** |
| Phase 5A | **47/47**, повторён после capacity fix |
| Phase 4B / Phase 4A | **52/52 / 38/38** |
| PropertyGrid / Collections | **30/30 / 30/30** |
| Metadata / Phase 2 / stabilization3B3 | **45/45 / 40/40 / 15/15**; Metadata повторён после geometry/fill fix |
| Broad Regression | **72/72**, normal environment; включает Eremex optional slices, export/build/runtime/VSIX |
| Nested Layout / Pointer / Grid Drop | **59/59 / 26/26 / 40/40**, повторены после capacity fix |
| Grid Editor | **45/45** |
| Layout Phase 7A | первый прогон **14/15**, DeferredContextMenu; последующий current **15/15**, архивный baseline **15/15**, standalone baseline menu **1/1** |
| Stabilization | **25/25** |
| PostgreSQL offline | **20/20**; live SKIP, сервера нет |
| SQL DataGrid Preview | **47/47**, отдельная LocalDB; real Settings/schema/source scenario PASS |
| Preview Memory | **36/36** с LocalDB |
| SQL generated runtime | PASS: отдельный process, Customers=3, Orders=2, real cells, independent ViewModels, resize |
| Новые generated Grid / Canvas | оба restore/build/run PASS, native=8 и interaction probes |
| Existing generated collections Grid / Canvas | оба PASS |
| Existing compiled source AXAML runtime | PASS |
| VSIX/VsHost IPC | отдельный source Open/Patch/ACK/conflict scenario **1/1**, прежние IPC regression в Broad PASS |

Полная матрица выполнена до финальных локальных guards. После capacity fix повторены Phase 5B/5A и затронутые Nested/Pointer/Grid Drop/Layout7; после Fill/parser fix — полный Phase 5B, Metadata, PropertyGrid, Collections, Phase 4A/4B, новый export и оба generated runtime. SQL/IPC не менялись и повторно не прогонялись без необходимости. Их успешные результаты не выдаются за новый запуск.

Первый Layout7 failure не скрыт. Стабильной воспроизводимости и доказанной production причины не получено, context-menu код не менялся. Это intermittent acceptance risk: проверить вручную и при повторении записать focus/Popup lifecycle. Нельзя объявлять все первые прогоны зелёными.

Broad сохранил прежний cleanup warning UnauthorizedAccessException: загруженный ToolboxStandardItemsExport.dll не удалось удалить из test artifacts; assertions 72/72, force cleanup не выполнялся. Generated net6.0 выдаёт прежний NETSDK1138; ранний runtime restore также NU1900 при недоступности vulnerability feed. Версии/TFM не менялись; security audit feed не выдаётся за успешный.

SQL LocalDB — отдельный GUID instance и fixture database, реальные Customers/Orders rows и значения cells. Пользовательские базы не затрагивались; cleanup в finally. PostgreSQL offline/live статусы различаются. Интерактивный установленный VSIX/devenv.exe не подменяется automated Named Pipes acceptance; архитектура не менялась.

## 14. Первые красные прогоны

Сохранены first-checks (49/51): Point test ожидал exact whitespace вместо native equivalence, capacity test сравнивал volatile workspace UpdatedUtc. Исправлены ожидания, не production serialization. Первый geometry compile использовал отсутствующий в Avalonia 11 Matrix.TransformBounds; заменён реальным Rect.TransformToAABB, compile log сохранён.

Geometry red logs выявили implicit spacing, native/drawn child mode, отсутствующие fixed dimensions и alignment, перечисленные выше. Nested Drop first 3/4 выявил пустой Viewbox. Первоначальный Canvas runtime probe неверно приравнял fitted Viewbox.Bounds к Canvas.Left/Top; native layout semantics зафиксирована отдельно, invariant resize check исправлен. Первые красные outputs не заменены последующими green результатами.

Дополнительно сохранены capacity-red (0/1), edge-ShapeFillPipeline (0/1) и edge-GeometryValidation (0/1) с точными managed stacks. Это подтверждённые проблемы, исправленные до завершения этапа. Build canvas-capacity-red-build первоначально не смог скопировать DLL, занятую другим нашим suite (MSB3027/MSB3021); runner больше не перестраивался во время его использования. Это отдельный build/harness failure, не успешный test run.

Первый расширенный прогон после Fill fix: 72/73. Native Fill уже применялся, но export assertion сравнивал `#123456` с каноническим `#ff123456` буквально. Исправлено сравнение Color, без изменения serializer. При расширении Fill Undo теста программно заданный seed не был зафиксирован как история; harness приведён к existing typed-editor pattern EndPropertyGridTextEdit + Begin/CommitUndoBatch до пользовательского edit. Production Undo не менялся. Отдельный исправленный Fill/Undo test — 1/1, затем полный расширенный набор — 73/73.

## 15. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.DropGeometry.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.Json.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs`
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`
* `DesignerSystem/Metadata/MetadataCoverageReport.cs`
* `DesignerSystem/Metadata/ShapeLiteralCodec.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `Docs/Phase5BPerformance.csv`
* `Docs/Phase5BPropertyGrid.csv`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase5B.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `Models/DesignControlModel.cs`
* `Models/DesignerLayoutModes.cs`
* `Models/ToolboxItem.cs`
* `Services/LegacyCanvasLayoutProjection.cs`
* `ViewModels/MainWindowViewModel.Metadata.cs`
* `ViewModels/MainWindowViewModel.NativeLayout.cs`
* `ViewModels/MainWindowViewModel.NestedDrop.cs`
* `ViewModels/MainWindowViewModel.cs`
* `Views/MainWindow.NativeLayout.cs`
* `Views/MainWindow.axaml.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5A.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5B.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5BPerformance.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase5BRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/README.md`

Raw logs, baseline archive, profiles, generated projects, capture evidence и SQL fixture находятся в ignored artifacts/universal-phase5b; бинарные dumps/локальные settings/секреты не добавляются.

## 16. Ограничения и ручная acceptance

* Shapes: typed text mini-language и bounded literal arrays; нет визуального редактора handles/control points/Geometry object graph. Complex property elements/Bindings/Resources сохраняются, не превращаются в строки.
* Single-child hosts принимают один визуальный model child; для нескольких нужен вложенный layout. Полноценный Content/Header object editor не добавлен.
* Imported AXAML остаётся safe subset; произвольные Styles/Templates/Resources/VM не исполняются. Structural source collection editing readonly.
* Не заявляются проверенные Linux/macOS, все DPI/multi-monitor/screen reader scenarios, live PostgreSQL и ручной установленный VSIX. UIA AV unresolved.
* Дополнительно вручную: вид Toolbox glyphs, first Drop/resize тонких shapes, Viewbox nested child resize при разных zoom, скроллинг/pointer gestures, несколько реальных AXAML проектов, VS text buffer конфликт/Undo/повторное открытие, accessibility client.
* Performance больших shapes/коллекций оценена в конкретной среде; native memory/driver/UIA costs не заменяются current-thread allocations.

## 17. Рекомендации и итог

**ADVANCED CONTROLS: READY WITH CONDITIONS.**

Восемь native компонентов проходят Designer → PropertyGrid → JSON → оба Preview → Export → compiled runtime в указанном subset. Подтверждённые capacity/Fill/parser validation P1 устранены; новых известных P0/P1 в проверенном pipeline не осталось. Readiness ограничена typed geometry literals, single-content policy, импортируемым safe subset, ручной VS/DPI/accessibility acceptance, историческим UIA risk и отмеченным intermittent context-menu test.

Следующий этап отдельно согласовать: визуальное редактирование Geometry/Points, specialized Content/Header/Items workflows, оставшиеся реальные candidate controls и адресное расследование UIA только при новых evidence. Автоматически следующая фаза и Phase 4C не начинаются.
