# Universal Controls — Phase 5G

Дата: 17.11.2026. Baseline commit `8b6521fc67e827778f7c25360129364197274d2d`, Git status до изменений — clean. Avalonia 11.1.1, host net8.0 x64, runtime 8.0.32, SDK 10.0.402, Windows 10 build 19045.6456. Сохранены TargetFramework, NuGet versions, JSON contract, Export Pipeline, VSIX/IPC architecture, глобальная тема и accessibility.

**STANDARD LIBRARY COMPLETENESS: READY WITH CONDITIONS.** Это готовность проверенных ограниченных workflows; полная стандартная библиотека и все свойства Avalonia не объявляются поддержанными.

## 1. Исходное состояние и аудит

Изучены Phase 5C/5D/5E/5F и Phase 4F, текущие registry, metadata, PropertyGrid, projection, Preview и export adapters. Использована реально установленная Avalonia: существующий AvaloniaMetadataCatalog, без второго discovery/metadata/parser механизма и без массовой регистрации.

Исходный core scope: 133 публичных обнаруженных Control-derived типа, исторически 72 применимых (66 standalone + 6 child-only), 61 Partial. Прежние связанные Popup/ToolTip/ContextMenu, невизуальные NativeMenu/Flyout, инфраструктура, root/base/presenter types не превращались в обычные Toolbox controls. DataGrid, ColorPicker, ItemsRepeater и сторонние библиотеки учитываются раздельно.

Полный текущий список: [StandardLibraryCoverage.csv](StandardLibraryCoverage.csv), [StandardLibraryCoverageSummary.json](StandardLibraryCoverageSummary.json). Фактическая runtime evidence дополняет eligibility и не повышает статус до Fully Supported автоматически.

## 2. Все 11 исходно отсутствовавших типов

[Phase5GRemainingControls.csv](Phase5GRemainingControls.csv) содержит namespace/full type, Assembly, BaseType, роль, Toolbox/Designer/PropertyGrid/JSON/Import/Round-trip/обоих Preview/Export/Runtime и специальные требования. Все 11 public types действительно присутствуют в Avalonia.Controls 11.1.1.

| Тип в Avalonia.Controls | Классификация | Решение |
|---|---|---|
| AutoCompleteBox | Input с источником данных и filtering | Нужен reviewed data/selection adapter; не добавлен формально |
| ExperimentalAcrylicBorder | Экспериментальный material/compositor control | Нужна platform/material acceptance |
| LayoutTransformControl | Однодетный Decorator | Реализован Child и bounded LayoutTransform |
| Primitives.HeaderedItemsControl | Архитектурный item base | Исключён только из дополнительного corrected Toolbox scope |
| RefreshContainer | Специализированный refresh container | Нужен связанный RefreshProvider/visualizer editor |
| RelativePanel | Constraint layout | Нужен редактор sibling-reference constraints |
| ReversibleStackPanel | Стандартный ordered panel | Реализован native panel/Children/ReverseOrder |
| SplitView | Двухслотовый container | Нужны разные Pane/Content ownership и selection |
| ThemeVariantScope | Theme/resource scope | Требуется reviewed scope/resource lifecycle |
| ToggleSplitButton | Input + связанный Flyout | Требуется объединённая toggle/flyout acceptance |
| TransitioningContentControl | Однодетный animated content host | Реализован Content и IsTransitionReversed; PageTransition read-only |

Остаются **8 Metadata Discovered Only**, из которых 7 входят в скорректированный применимый scope. Ни один служебный тип не добавлен ради процента. Дочерние элементы коллекций не входят в эти 11; прежние 6 child-only типов продолжают использовать Collection Editor.

## 3. Control Coverage

| Метрика | Before | After |
|---|---:|---:|
| Исторический Partial core | 61/72 — 84,72% | 64/72 — 88,89% |
| Corrected применимый core | 61/71 — 85,92% | 64/71 — 90,14% |
| Strict Fully Supported | 0/72 | 0/72 |
| Metadata-only core | 11 | 8 |

Corrected scope исключает только архитектурный HeaderedItemsControl. Экспериментальные и специализированные, но действительно пользовательские controls остаются в знаменателе. Исторические 72 сохранены для сравнения, процент не подменяется новым. Семейства DataGrid 1/1 Partial, ColorPicker 0/2 и ItemsRepeater 0/1 не смешаны с core.

## 4. Реализованные компоненты

**ReversibleStackPanel:** native Children, ReverseOrder, Orientation, Spacing, существующий Stack layout mode. В runtime сравнивается фактический порядок по Y bounds двух children. Фиктивные Grid.Row/Column не используются для управления этим panel.

**LayoutTransformControl:** native Decorator.Child, LayoutTransform и UseRenderTransform. Литерал LayoutTransform использует тот же bounded native transform codec, что RenderTransform. Один Child или вложенный Layout, без второй layout engine. Проверены оба JSON layouts, imported source и native generated runtime.

**TransitioningContentControl:** native Content с одним Child, IsTransitionReversed, реальный framework PageTransition default. PageTransition/Transitions доступны read-only; пользовательские animation objects не десериализуются. Generated runtime проверяет начальный TextBlock Content и замену содержимого на новый native TextBlock.

Toolbox использует existing metadata registrar, русские descriptions, категории и glyphs; поиск/duplicate detection, routed Grid Drop, selection/resize и Undo/Redo проверены. Grid-first, Canvas и текущий Document Model сохранены.

## 5. Подтверждённое исправление source projection

Первый набор Phase 5G дал **83/85**. У LayoutTransformControl в imported Preview терялся Child: shared source projection присоединял Child только к Border, а не общему Decorator. JSON projection уже использовал соответствующий контракт. Замена проверки Border на Decorator в существующем adapter восстановила Child и ожидаемые LayoutTransform values; source/import/JSON/native tests проходят. XML Parser и layout algorithms не переписывались.

Второй первоначальный отказ, TransitioningContentControl Designer, был ошибкой тестового ожидания: Designer хранит реальный TextBlock внутри своего wrapper. Проверка уточнена по реальному native descendant; production Content не подменялся и не исправлялся ради теста. Generated runtime отдельно требует настоящий TextBlock Content без designer wrapper.

## 6. Новые и расширенные свойства

* **FlowDirection:** стандартный enum, только там, где AP реально существует; существующие typed enum editor/validation/JSON/AXAML/native projection/export.
* **OpacityMask:** nullable SolidColorBrush с alpha. Gradient/object/Binding/Resource не превращаются в строку. Новый local editor сохраняет прежнюю Advanced category сложного свойства.
* **TextDecorations:** небольшой property-specific typed text adapter для Underline, Strikethrough, Overline, Baseline и их неповторяющихся комбинаций; максимум 4 имени/128 символов. null очищает, `None` не придумывается как несуществующий native token. Custom pen/offset object collections остаются opaque.
* **SelectionBrush / SelectionForegroundBrush:** native brush properties TextBox/MaskedTextBox и других применимых типов; Advanced category. CaretIndex представлен read-only как transient state; существующая authored SelectionStart/SelectionEnd поддержка не переписана.
* **RenderTransform:** помимо matrix поддерживаются native translate/translateX/Y, rotate, scale/scaleX/Y, skew/skewX/Y и последовательности операций.
* **LayoutTransform / UseRenderTransform / ReverseOrder / Orientation / Spacing / IsTransitionReversed:** actual installed control-specific properties, без WPF API по аналогии.

OpacityMask/Decorations/selection appearance и Transform editors используют существующий PropertyGrid, а не отдельную систему. Defaults, categories, hints, search, Favorites, Value Source, Reset и history сохраняются. Nested object edits не введены; literal edit не выполняет новый полный Inspector rebuild на каждый вложенный параметр.

## 7. Transform Editor и TransformGroup safety

Native API probe установил `translate(4px,6px)`/`rotate(30deg)`/`scale(2,0.5)`/`skew(5deg,10deg)` и native order. Unitless translate(4,6) отвергается framework parser. Codec не заменяет native parsing собственной математикой: разрешён bounded список имён/чисел и затем TransformOperations.Parse.

Ограничения: ≤2048 символов, ≤16 operations, конечные параметры abs≤1e6, результирующая matrix конечна и abs coefficients≤1e9. Неподдержанные functions, markup, NaN/Infinity и чрезмерные values отвергаются. JSON хранит validated authored literal, не произвольный object.ToString.

Литеральная последовательность операций **не объявляется object TransformGroup editor**. Imported `<TransformGroup><TranslateTransform/><RotateTransform/></TransformGroup>` сохраняется точно и read-only. Не происходит неявного flatten/замены group. Regression сравнивает native matrices для translate→rotate и rotate→translate: порядок различается и сохраняется. MatrixTransform представлен native matrix literal; произвольные bound/mutable transform graphs остаются отдельной архитектурой.

## 8. BoxShadow, Transitions, ToolTip и complex values

Existing BoxShadows editor проверен на multiple/inset shadows, finite bounds, отрицательный blur, malformed/oversized input; JSON/source/native/runtime chain сохранена. GUI shadow canvas не добавлялся и не заявляется.

Transitions/PageTransition требуют typed animation object model: transition Property, duration/easing, lifecycle и source/object serialization. CollectionV1 editor для Menu/Tree items не подходит для произвольных ITransition/IPageTransition. Поэтому они read-only; arbitrary object deserialization отсутствует.

ToolTip.Tip остаётся literal string/null editor с native getter assertions в унаследованном proof. Rich object ToolTip/Template/Resources не редактируются как строки. Commands, ItemsSource, SelectedItem/DataContext и пользовательские ViewModels не исполняются при импорте.

## 9. Property Coverage — исходная методика

| Показатель | Before, count / 4935 | Before, % | After, count / 4935 | After, % |
|---|---:|---:|---:|---:|
| Discovered | 4935 | 100.00 | 4935 | 100.00 |
| Displayable | 3252 | 65.90 | 3462 | 70.15 |
| Editable | 2343 | 47.48 | 2579 | 52.26 |
| JSON Serializable | 2582 | 52.32 | 2818 | 57.10 |
| AXAML Importable | 2548 | 51.63 | 2783 | 56.39 |
| Source Patchable | 2527 | 51.21 | 2760 | 55.93 |
| Exportable | 2343 | 47.48 | 2579 | 52.26 |
| Editable ∩ Exportable | 2343 | 47.48 | 2579 | 52.26 |
| Preview, проверенный lower bound | 147 | 2.98 | 195 | 3.95 |
| End-to-End, проверенный lower bound | 147 | 2.98 | 195 | 3.95 |

4935 применимых canonical «тип–свойство» пар и 332 unique native identities сохранены. Aliases не считаются отдельными свойствами (5207 raw pairs / 272 alias duplicates). Inherited pairs и unique identities показываются отдельно. Safe unique identities: **177 → 184**. Eligibility для literal/adapted workflows не равна поддержке всех выражений.

Подробные таблицы: [Phase5GPropertyCoverage.csv](Phase5GPropertyCoverage.csv), [Phase5GPropertyCoverageDetails.csv](Phase5GPropertyCoverageDetails.csv), [Phase5GPropertyCoverageSummary.json](Phase5GPropertyCoverageSummary.json). Preview/End-to-End — separately verified lower bounds по пересечению Inspector/native Preview proof и двух compiled/running generated apps.

## 10. Corrected Property Coverage и таблица пробелов

**2343/4788 — 48,93% → 2579/4788 — 53,86%.** Исключены 71 пары архитектурного HeaderedItemsControl и 76 runtime/caret/selected-text state пар остальных применимых controls. Getter-only diagnostics были исключены прежним inventory; authorable getter collections, Styles, Resources, Templates, Commands и Transitions не выкидывались для повышения процента. Initial SelectionStart/End остаются применимыми там, где есть явный поддержанный authoring workflow.

Методика и точные before/after counts: [Phase5GAdjustedCoverage.json](Phase5GAdjustedCoverage.json). Таблица [Phase5GPropertyGaps.csv](Phase5GPropertyGaps.csv) содержит Property, OwnerType, PropertyType, canonical identity, applicable components, inherited/attached/read-only/runtime flags, aliases, Displayable/Editable/JSON/Import/Patch/Preview/Export и причину ограничения. Число missing applicable компонентов не выдаётся за число полностью поддержанных controls.

Главные оставшиеся пробелы: complex animation/resource/template graphs, data/command expressions, Cursor platform lifetime, rich content и associated object ownership. Они не объявлены Editable только по Reflection.

## 11. Property Coverage новых типов

| Тип | Applicable | Displayable | Editable/JSON/Export | Import | Patch | Unsupported |
|---|---:|---:|---:|---:|---:|---:|
| ReversibleStackPanel | 56 | 44 | 36 | 37 | 36 | 20 |
| LayoutTransformControl | 53 | 44 | 36 | 36 | 35 | 17 |
| TransitioningContentControl | 69 | 57 | 47 | 47 | 47 | 22 |

Это limited native slices. Structural authoring arbitrary imported Children/Content и произвольные Templates не становятся разрешёнными от регистрации типа. Strict Fully Supported остаётся 0.

## 12. JSON / AXAML Import и Round-trip

Для всех 16 типов выполнялись edit → JSON file save/read/open → actual native Designer → Legacy/AXAML/imported Preview. Сравниваются восстановленные модели, actual native AP values и container Child/Content.

Для новых literals проверены single minimal attribute patch, Reset, Undo Reset, Redo Reset, Undo edit до точного исходного текста; сохранены escaping, кавычки, комментарий и неизвестный namespace/attribute. Binding, StaticResource, DynamicResource и unsupported TransformGroup object elements read-only и остаются неизменными. Source Map, version/checksum, conflict/ACK и исходный XML сохраняют existing contracts. Полной AXAML regeneration нет.

## 13. Preview / Export / Generated Runtime

Два новых generated applications **Grid и Canvas** прошли restore/build/run: **195 unique canonical native property pairs на 16 actual Avalonia types в каждом**. Проверены known brush colors, transform matrices/order, decoration locations, Geometry.Bounds/FillContains, native container hierarchy/arrangement и replacement Content. Не используются placeholders в generated runtime.

End-to-End lower bound: **147/4935 (2,98%) → 195/4935 (3,95%)**. Это сертификация конкретной literal matrix, а не 52,26% доказанных arbitrary runtime scenarios.

Отдельный patched AXAML generated runtime: 20 native nodes, восемь inserted types, source parent/order/Canvas/raw Tag, Combo/List collections, selection/popup. SQL generated runtime: Customers=3, nested Orders=2, independent forms и resize.

Первый generated Grid probe FAILED: ожидания ошибочно назначили всем child TextBlock полную property configuration основного TextBlock. Фактический AXAML основного control был корректен. Manifest ограничен 16 намеренно настроенными root controls, nested Child/Content остаются отдельными assertions; повтор выполнен после исправления harness в новом output directory. Production Export не менялся ради этого отказа.

## 14. MaskedTextBox Performance

Один harness, 5 warm-up + 30 измерений на type/operation, последовательные процессы, UI-thread allocations в decimal MB. P95 — nearest-rank. GC counts суммарно за 30 samples, peak working set — process high-water mark, не footprint одной операции. Полные ToolboxCreate/Inspector/SaveLoad/ExportAxaml/LegacyPreview/AxamlPreview данные: [Phase5GPerformance.csv](Phase5GPerformance.csv); nested stages: [Phase5GInspectorStages.csv](Phase5GInspectorStages.csv). Вложенные stage durations не складываются.

| Inspector | Before Median/P95, мс | After Median/P95, мс | Before/After MB | After GC0/1/2 |
|---|---:|---:|---:|---:|
| Button | 45.87/91.29 | 65.64/121.48 | 2.28/2.31 | 5/5/0 |
| Calendar | 66.09/118.96 | 109.30/203.62 | 9.82/10.11 | 23/23/1 |
| DataGrid | 60.49/92.95 | 85.83/152.26 | 5.40/5.46 | 9/9/0 |
| NumericUpDown | 90.34/194.69 | 107.30/142.30 | 10.26/10.56 | 21/21/1 |
| MaskedTextBox | 97.00/139.87 | 106.71/178.22 | 12.20/12.53 | 25/25/1 |

Неизменённый baseline повторно собран из исходного commit в ignored directory, без изменения рабочего дерева; запущен тот же текущий harness. Это дополнительная сравнительная серия, первые результаты выше сохранены:

| Late baseline Inspector | Median/P95, мс | MB |
|---|---:|---:|
| Button | 55.94/127.51 | 2.28 |
| Calendar | 73.01/140.41 | 9.81 |
| DataGrid | 76.99/117.78 | 5.40 |
| NumericUpDown | 104.65/152.47 | 10.25 |
| MaskedTextBox | 111.98/248.22 | 12.20 |

| Новый тип, Inspector | Median/P95, мс | MB |
|---|---:|---:|
| ReversibleStackPanel | 60.92/116.15 | 4.91 |
| LayoutTransformControl | 69.19/124.52 | 5.11 |
| TransitioningContentControl | 93.96/139.94 | 7.34 |

Import/patch: [Phase5GImportPerformance.csv](Phase5GImportPerformance.csv), по 30 samples после warm-up:

| Phase | Сцена | Операция | Median/P95, мс | MB |
|---|---|---|---:|---:|
| After | Small | Import | 1.62/11.99 | 0.37 |
| After | Small | Patch | 2.04/2.79 | 0.55 |
| After | Medium | Import | 15.99/25.92 | 3.40 |
| After | Medium | Patch | 36.41/46.45 | 5.24 |
| After | MainWindow | Import | 99.79/125.39 | 53.79 |
| After | MainWindow | Patch | 108.13/127.34 | 54.96 |
| LateBaseline | Small | Import | 1.72/12.55 | 0.37 |
| LateBaseline | Small | Patch | 2.03/3.46 | 0.53 |
| LateBaseline | Medium | Import | 15.22/21.72 | 3.33 |
| LateBaseline | Medium | Patch | 35.10/43.71 | 5.00 |
| LateBaseline | MainWindow | Import | 80.33/97.84 | 52.03 |
| LateBaseline | MainWindow | Patch | 82.53/99.90 | 51.14 |


Контролируемый one-factor эксперимент на финальной DLL: при исходном selection TextBox → MaskedTextBox Inspector Median/P95 **106,71/178,22 мс, 12,53 МБ**, а MaskedTextBox → MaskedTextBox **46,51/111,46 мс, 2,21 МБ**. Во втором случае новые editor controls не создаются; в первом — 38. Layout median **46,87→1,85 мс**, MetadataLookup **0,376→0,374 мс** (82 вызова). Это доказательство стоимости неоднородного editor realization/binding/layout, а не исправление production или устранение каждого outlier. Уже существующий same-type reuse работает.

Число property rows MaskedTextBox выросло **71→75**, realizations **37→38**, allocations **12,20→12,53 МБ (+2,73%)**. Добавлены реально редактируемые FlowDirection/OpacityMask/selection brush descriptors; новые сложные editor rows остаются в Advanced. Финальные вложенные stage medians: PropertyRowsValuesDescriptors 5,39 мс / 0,82 МБ; MetadataLookup 0,38 / 0,42; EditorControlCreationAndBindings 2,92 / 1,02; VisualProjection 10,31 / 1,88; RefreshPropertyInspector 22,23 / 2,81; Selection.Changed 36,89 / 2,95. Остальная стоимость видна на внешней границе selection и UpdateLayout, а не объясняется повторным Reflection scanning; MetadataBuilds=0 в warm samples.

Повторная неизменённая Phase 5F DLL из того же исходного commit дала MaskedTextBox **111,98/248,22 мс, 12,20 МБ** против ранних **97,00/139,87 мс, 12,20 МБ**; Button **55,94/127,51** против **45,87/91,29 мс**. Таким образом, вариативность при неизменной production реализации измерена напрямую. Это не доказывает внешнее происхождение всех outliers. В текущем Before/After latency выросла у нескольких controls; P95 и процессный разброс не объявлены исправленными. Изменение GC за 30 samples видно в CSV. Без причинного доказательства весь рост времени нельзя списать на окружение; безопасный локальный production pooling/row migration не принят. **MaskedTextBox/Inspector latency остаётся P2**, нужен отдельный проверяемый reuse prototype с stale-context/collection/selection/memory gates.

MainWindow import в текущей серии **99,79/125,39 мс, 53,79 МБ**. Phase 5F report фиксировал **72,60/88,82 мс, 52,03 МБ** другим по времени запуском: нынешний рост задержки явно сохранён, равенство производительности не заявляется. Исторические Phase 4B 49,45 мс / 35,30 МБ — другой объём metadata/source mapping; это контекст, не сопоставимая Before/After пара Phase 5G. Дополнительная сопоставимая Phase 5F серия того же harness: MainWindow import **80,33/97,84 мс, 52,03 МБ**, patch **82,53/99,90 мс, 51,14 МБ**. Вклад расширенной карты свойств подтверждён счётчиками: property resolution **60 872→64 442** вызова; median **7,96→12,50 мс**, source-reference generation **5,74→11,13 мс**, 9,04→9,68 МБ; общий nested SourceMapProjection **62,95→76,31 мс**, 44,06→45,82 МБ. Синтаксический parse **1,40→1,43 мс**; layout projection здесь не измеряется. Причина дополнительной работы — существующий проход по всем доступным source-patchable descriptors для каждого из 1502 model nodes, теперь с новыми общими свойствами. Весь latency рост нельзя объяснить только числом descriptors: добавилось 3570 resolution calls, но временной вклад зависит от GC/общего разброса. Кэш/алгоритм parser/source map не менялся; ускорение не заявляется. **Large import latency/cost остаётся документированным P2**, source-map perf optimization в этой фазе не принималась. Полная стадийная таблица: [Phase5GImportStages.csv](Phase5GImportStages.csv).

Исходные/последующие/stage/GC/variance/peak данные сохранены. Не принят небезопасный pooling, скрывающий stale callbacks или удерживающий старые RowVM/ControlModel. PropertyGrid не переписывался; performance risk не объявляется устранённым по одному удачному повтору.

## 15. Memory и UI Automation

**Preview Memory 36/36 × 3** в независимых SQL-enabled процессах. В каждом 44 census checkpoints; tracked Window/Context/Items/Grid survivors = 0. Дополнительные новые 10-cycle Legacy/AXAML tests также проходят. GC/waits/assertions не ослаблялись; одинаковые окна в нескольких checkpoints не суммировались как уникальные.

Native UIA crash `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Связь с source Decorator bug, PropertyGrid latency или Memory Gate не заявляется. COM/GCHandle/AutomationPeer lifecycle не менялись; accessibility не отключалась. Owned UIA client ownership из Phase 4E сохранён, посторонние clients/reader processes не завершались.

В 45 retained native-capture process records: native Captured=0, CaptureFailed=0, TimedOut=0. Четыре exit 1 — сохранённые managed assertion failures исходных/focused source-projection tests; пути и PID находятся в EvidenceSummary. Первый generated probe с FAIL и child exit 0 учитывается отдельно как неуспешная assertion, а не успешный runtime. Новых native crash dumps не потребовалось; ноль сбоев не доказывает устранение прежнего AV.

## 16. VSIX / Standalone regression

Общая metadata-driven архитектура используется Standalone и VsHost. Automated Phase 4F 15/15 и normal IPC 3/3 проходят; stale version/checksum/ACK/conflicts остаются защищёнными. Broad 72/72 покрывает JSON/Grid-first/Canvas/Multi Form/DataGrid/Eremex/export.

Current VSIX packaging/dependency closure/relative paths — PASS, версия 0.1.18 сохранена. 639 entries, dependency closure и отсутствие dumps/settings/secrets подтверждены; Avalonia/Eremex отсутствуют в корне bridge. SHA-256 VSIX `cae1e4bf55a7d1d0d9b058728b3106ff2b8b576fa8e4a83109bdd836eafee886`, FormDesigner DLL `1f647c569d0f4441f905e6c6c7664221795c314a5e852bf92d0bf695bf5a029b`. Текущий VSIX содержит ту же production DLL, что прошла native tests.

**INSTALLED VSIX: NOT VERIFIED. PHYSICAL DPI/DND: NOT VERIFIED.** Current payload интерактивно не устанавливался/не принимался. Routed pointer/geometry tests не подменяют физическую мышь/DPI 100/125/150%; предыдущие ручные действия с иным Phase 4F payload не зачтены за текущий.

## 17. Build / smoke / regression

| Gate | Фактический результат |
|---|---|
| Phase 5G | 85/85 |
| Phase 5F | 73/73 |
| Phase 5E | 58/58 |
| Phase 4F | 15/15 |
| Phase 4D | 14/14 |
| Phase 4C / Structural Editing | 85/85 |
| Phase 4A / Round-trip | 38/38 |
| Phase 4B | 52/52 |
| PropertyGrid | 30/30 |
| Collections | 30/30 |
| Layout7 / ContextMenu | 15/15 |
| Metadata | 45/45 |
| Designer Pointer | 26/26 |
| Grid Drop | 40/40 |
| Broad, normal environment | 72/72 |
| Normal IPC, live VsHost | 3/3 |
| SQL Server LocalDB | 47/47 |
| Preview Memory, independent SQL-enabled processes | 36/36 × 3 |
| New generated Grid / Canvas | 195 native pairs / 16 types в каждом |
| Structural generated runtime | 20 native nodes + hierarchy/layout/selection/popup assertions |
| SQL generated runtime | PASS, real Customers=3 / Orders=2 |
| VSIX packaging | PASS |
| Installed VSIX / physical DPI/DnD | NOT VERIFIED |

Solution build — 0 errors, прежний NU1603 SDK analyzer fallback; runner — 0 errors/0 warnings. Generated projects сохраняют прежний net6.0 и NETSDK1138 EOL warning; TFM/packages не менялись ради зелёного build. PostgreSQL live — SKIP, test server отсутствует; LocalDB им не подменён. Broad cleanup warning locked DLL сохранён; принудительного удаления не было.

## 18. Первые failures и надёжность диагностики

1. Phase 5G first checks: **83/85**, native capture child exit 1, no native AV. Source Decorator.Child исправлен; designer wrapper assertion уточнён. Focused красные прогоны и native reference диагностика сохранены.
2. Diagnostic build CS0122: попытка harness обратиться к internal adapter. Использован прежний CreateSmokeControlNode helper; production visibility не расширялась.
3. Matrix aggregate Set-Content получил sharing violation при записи читаемого results.json. PropertyGrid assertions/capture были успешны (30/30, exit 0); повторять их для зелёного результата не требовалось. Продолжены только оставшиеся suites в новых directories; новые live results пишутся отдельными неизменяемыми файлами.
4. Generated Grid expected manifest неправильно включал child TextBlock как fully configured root. Probe FAILED, материал сохранён; исправлен только harness predicate и создан новый проект/output. Успешный rerun без изменения не выдавался за исправление.

5. Дополнительная финальная parallel solution build вернула exit 1 при 0 warnings/0 errors без текста причины; первый log сохранён. Последовательная `dotnet build FormDesigner.sln -c Release --no-restore -m:1 -nr:false -p:NuGetAudit=false` успешно собрала solution с прежним NU1603. Точная причина первого MSBuild process failure не доказана; это не скрытый зелёный retry.

Ни одна managed assertion/build/harness I/O ошибка не классифицирована как native UIA AV или успешный тест до assertions. Retries и удаление проблемных tests не добавлялись. Full capture/metadata/modules/SHA-256/protected archives остаются доступны; dumps в Git не добавлены.

## 19. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs`
* `DesignerSystem/Metadata/StandardControlProperties.Extensions.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `DesignerSystem/Metadata/TextDecorationsLiteralCodec.cs`
* `DesignerSystem/Metadata/TransformLiteralCodec.cs`
* `Docs/Phase5GAdjustedCoverage.json`
* `Docs/Phase5GControlAcceptance.csv`
* `Docs/Phase5GEvidenceSummary.json`
* `Docs/Phase5GImportPerformance.csv`
* `Docs/Phase5GImportStages.csv`
* `Docs/Phase5GInspectorStages.csv`
* `Docs/Phase5GPerformance.csv`
* `Docs/Phase5GPropertyCoverage.csv`
* `Docs/Phase5GPropertyCoverageDetails.csv`
* `Docs/Phase5GPropertyCoverageSummary.json`
* `Docs/Phase5GPropertyGaps.csv`
* `Docs/Phase5GPropertyPriority.csv`
* `Docs/Phase5GRemainingControls.csv`
* `Docs/StandardLibraryCoverage.csv`
* `Docs/StandardLibraryCoverageSummary.json`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase5G.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `Models/DesignerLayoutModes.cs`
* `Models/ToolboxItem.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5APerformance.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5G.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5GChecks.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase5GRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/Summarize5GAdjustedCoverage.py`
* `smoke-tests/UniversalMetadata/Summarize5GPerformance.py`
* `smoke-tests/UniversalMetadata/Summarize5GPropertyCoverage.py`

Raw evidence, generated projects и profiling files: ignored `artifacts/universal-phase5g`. [Phase5GEvidenceSummary.json](Phase5GEvidenceSummary.json) содержит выбранные results/capture records и hashes. Пользовательские изменения не удалялись; destructive Git operations, commit/push/tag/release не выполнялись.

## 20. Ограничения и следующий конкретный шаг

* 8 core types остаются metadata-only; corrected scope не подменяет историческую метрику. Нет универсального specialized editor для SplitView/RelativePanel/AutoCompleteBox/resource scope.
* Transform operation literals не являются object TransformGroup designer. Custom decorations, gradients, transitions, Templates/Resources/Commands/Bindings сохраняют capability ограничения.
* ToolTip rich Content и imported complex collection structural editing не расширялись; Phase 4C whitelist новых типов не менялся.
* MaskedTextBox selection/paste/IME границы, Inspector P95, large AXAML, physical DPI и installed VSIX остаются отдельными acceptance/performance risks; UIA UNRESOLVED.

Следующий конкретный шаг — принять именно current VSIX SHA в изолированном VS по existing checklist (новые literals, Apply/Save/Undo/Redo, conflict, reopen, pending ACK close/restart, DPI) и отдельно измерять локальный editor reuse prototype с доказательством stale-context safety и Memory Gate, если он нужен по performance данным. Новая фаза автоматически не начинается.

**STANDARD LIBRARY COMPLETENESS: READY WITH CONDITIONS.**
