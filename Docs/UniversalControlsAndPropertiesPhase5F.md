# Universal Controls — Phase 5F

Дата: 17.11.2026. Исходный commit: `9fb14e4084a447746ad4c0123e5c7a345bfcbcc9`; исходный Git status — clean. Пользовательские изменения не удалялись. Avalonia 11.1.1, host .NET 8.0.32 x64, SDK 10.0.402, Windows 10 build 19045.6456. NuGet, TargetFramework, JSON/IPC contracts, VSIX architecture и глобальная тема не менялись.

**STANDARD CONTROLS & PROPERTIES: READY WITH CONDITIONS.** Это готовность проверенных ограниченных workflows. Полная поддержка всех свойств Avalonia, установленного VSIX и физического DPI/DnD не заявляется.

## 1. Исходные данные и методика

Изучены отчёты Phase 5C/5D/5E и 4C/4D/4E/4F. Использованы существующие AvaloniaMetadataCatalog, reviewed property policy, общий PropertyGrid, source map/patch writer, native projection и export bridge. Второй metadata engine, parser, PropertyGrid или Document Model не создавались.

Сохранены два разных знаменателя: **72 применимых типа Avalonia.Controls** и **4935 применимых пар «тип–свойство»**. 133 публичных Control-derived типа обнаруживаются в этой сборке; абстрактные/корневые/инфраструктурные типы, presenters и связанные объекты не превращались в Toolbox items. В 72 входят 66 самостоятельных и 6 дочерних типов. HeaderedItemsControl остаётся в историческом знаменателе для сопоставимости, но его архитектурная роль явно отмечена. DataGrid, ColorPicker, ItemsRepeater и сторонние библиотеки учитываются отдельно.

Полный актуальный перечень и capabilities: [StandardLibraryCoverage.csv](StandardLibraryCoverage.csv), [StandardLibraryCoverageSummary.json](StandardLibraryCoverageSummary.json), [Phase5FControlAcceptance.csv](Phase5FControlAcceptance.csv). Обнаружение через Reflection не считается поддержкой; strict Fully Supported по критерию всех writable properties/collections остаётся **0/72**.

## 2. Оставшиеся 15 типов исходного покрытия

[Phase5FRemainingControls.csv](Phase5FRemainingControls.csv) содержит Type, Namespace, Assembly, BaseType, роль, Toolbox/Designer/PropertyGrid/JSON/Import/Round-trip/Preview/Export и runtime evidence для всех 15 типов. Все существуют и публичны в установленной Avalonia 11.1.1.

| Тип | Роль / решение Phase 5F |
|---|---|
| SelectableTextBlock | Добавлен как самостоятельный текстовый control |
| PathIcon | Добавлен как векторная иконка |
| Arc | Добавлен как нативная фигура |
| Sector | Добавлен как нативная фигура |
| AutoCompleteBox | Нужен reviewed adapter для ItemsSource, filtering и selection |
| ExperimentalAcrylicBorder | Экспериментальный compositor/material; требуется отдельная platform acceptance |
| LayoutTransformControl | Нужна проверенная модель layout transform и дочернего измерения |
| HeaderedItemsControl | Архитектурный item base; не добавлен ради процента |
| RefreshContainer | Нужны RefreshProvider и специализированное interaction editing |
| RelativePanel | Нужен редактор именованных относительных constraints |
| ReversibleStackPanel | Нужна отдельная проверка ReverseOrder, вложенного layout и export |
| SplitView | Нужны раздельные слоты Pane и Content |
| ThemeVariantScope | Нужна проверка theme/resource scope и наследования |
| ToggleSplitButton | Нужна совместная проверка toggle state и associated Flyout |
| TransitioningContentControl | Нужна проверка animation/content lifecycle |

После фазы остаются **11 Metadata Discovered Only** типов из этой таблицы. Popup/ToolTip/ContextMenu, Flyout и NativeMenu — связанные объекты, а не новые обычные Toolbox items. Дочерние MenuItem/TabItem/TreeViewItem/ComboBoxItem/ListBoxItem/TabStripItem сохраняют существующие collection adapters.

## 3. Добавленные компоненты и Control Coverage

**Partial: 57/72 → 61/72; 79,17% → 84,72%.** Strict Full: 0/72 → 0/72. Неизменённые отдельные семейства: DataGrid 1/1 Partial; ColorPicker 0/2; ItemsRepeater 0/1.

* **SelectableTextBlock:** Text, typography, wrapping/trimming, LineHeight, selection brushes; в Preview и generated runtime проверены SelectAll/ClearSelection. Inlines и произвольные complex text graphs не стали строковыми редакторами.
* **PathIcon:** bounded path mini-language Data, Foreground и общие свойства. Иконка имеет отдельный Toolbox glyph, категорию «Фигуры» и исходный размер 32×32.
* **Arc / Sector:** реальные Avalonia.Controls.Shapes types, StartAngle/SweepAngle в градусах, Fill/Stroke/StrokeThickness, Stretch, StrokeJoin и StrokeDashArray. Новые свойства не заимствованы из WPF.

Новые дескрипторы создаются существующим metadata registrar. Проверены поиск Toolbox, отсутствие дубликатов, русские подсказки, routed Drag & Drop в Grid, selection, resize и Undo/Redo. Прозрачная фигура выбирается через существующий Designer wrapper; runtime hit-testing semantics не менялись. JSON Grid/Canvas и общие layout mechanisms сохранены.

## 4. Новые общие свойства и ограничения форматов

Добавлены reviewed adapters для **RenderTransform, RenderTransformOrigin и Clip на 55 core root types**. Это 165 eligible пар, включая новые controls; вместе с остальными свойствами четырёх новых типов общий прирост составляет **318 непересекающихся пар**.

| Свойство | Поддержанный литерал | Защита |
|---|---|---|
| RenderTransform | `matrix(m11,m12,m21,m22,offsetX,offsetY)` | Только одна матрица, 6 конечных чисел; до 256 символов и abs ≤ 1e6; null разрешён |
| RenderTransformOrigin | Две DIP координаты либо два процента | Одинаковые единицы обеих осей; конечные значения; null запрещён |
| Clip | Стандартная path mini-language Avalonia | До 16384 символов / 2048 чисел, конечные координаты abs ≤ 1e6; null разрешён |
| PathIcon.Data | Та же проверяемая Geometry mini-language | Авторский литерал хранится в existing JSON bag |

Geometry/Transform graphs, Bindings/Resources и object property elements не сериализуются через произвольный ToString. Матрицу разбирает тот же TransformOperations parser, который использует AXAML ITransform converter. RelativePoint имеет явное известное display representation с проверенным обратным разбором. Числовая StrokeDashArray экспортируется в comma-separated формате с invariant round-trip числами; исходный авторский JSON остаётся сохранённым.

## 5. Property Coverage Before/After

| Метрика | Before | After |
|---|---|---|
| Discovered | 4935/4935 (100,00%) | 4935/4935 (100,00%) |
| Displayable | 3049/4935 (61,78%) | 3252/4935 (65,90%) |
| Editable | 2025/4935 (41,03%) | 2343/4935 (47,48%) |
| JSON Serializable | 2264/4935 (45,88%) | 2582/4935 (52,32%) |
| AXAML Importable | 2233/4935 (45,25%) | 2548/4935 (51,63%) |
| Source Patchable | 2212/4935 (44,82%) | 2527/4935 (51,21%) |
| Exportable | 2025/4935 (41,03%) | 2343/4935 (47,48%) |
| Editable ∩ Exportable | 2025/4935 (41,03%) | 2343/4935 (47,48%) |
| Preview verified lower bound | 91/4935 (1,84%) | 147/4935 (2,98%) |
| End-to-End verified lower bound | 91/4935 (1,84%) | 147/4935 (2,98%) |

Уникальные native identities учитываются отдельно: **332 total; safely editable/exportable 167 → 177**. Raw pairs with aliases — 5207; исключены 272 alias-дубликата. Наследуемые свойства остаются отдельными парами для каждого применимого типа, но не считаются новыми уникальными свойствами. 72 runtime-only ActualThemeVariant signatures помечены отдельно и не стали Editable; ради роста процента исходный знаменатель не менялся.

Discovered/Displayable/Editable/JSON/Import/Source Patch/Export — eligibility существующих reviewed adapters для поддержанных значений, а не доказательство всех возможных expressions. Preview и End-to-End — отдельно подтверждённый lower bound. Непроверенные пары не повышались до End-to-End по наличию дескриптора.

Подробности для каждого типа, inherited/attached/CLR signatures, aliases, unsupported/runtime-only properties: [Phase5FPropertyCoverage.csv](Phase5FPropertyCoverage.csv), [Phase5FPropertyCoverageDetails.csv](Phase5FPropertyCoverageDetails.csv), [Phase5FPropertyCoverageSummary.json](Phase5FPropertyCoverageSummary.json).

## 6. Property Coverage новых типов

| Тип | Applicable | Discovered | Displayable | Editable / JSON / Import / Patch / Export | Unsupported |
|---|---:|---:|---:|---:|---:|
| SelectableTextBlock | 73 | 73 | 57 | 47 | 26 |
| PathIcon | 64 | 64 | 52 | 42 | 22 |
| Arc | 59 | 59 | 47 | 38 | 21 |
| Sector | 59 | 59 | 47 | 38 | 21 |

До регистрации каждого из этих типов discovered уже существовал, остальные перечисленные capabilities были 0. Успех bounded workflow не означает поддержку всех applicable properties.

## 7. Рейтинг пробелов и complex capabilities

[Phase5FPropertyPriority.csv](Phase5FPropertyPriority.csv) ранжирует отсутствующие canonical identities, исключая alias inflation; runtime-only состояние выделено. У common RenderTransform/Origin/Clip после расширения остаются 6 дочерних типов без этого scalar editor; их собственный item editor не подменяется editor владельца.

| Группа | Решение / риск |
|---|---|
| RenderTransform / Origin / Clip | Небольшие bounded adapters реализованы; object graphs opaque |
| Cursor | Широко применим, но lifetime/platform cursor требует отдельного verified adapter |
| FlowDirection | Кандидат typed enum; требуется отдельная geometry/mirroring acceptance |
| Transitions / Effect / FocusAdorner | Нужны specialized collection/object editors; сейчас read-only/opaque |
| Items / Content / Header | Existing Collection Editor и bounded content adapters; arbitrary ItemsSource не включён |
| Commands / DataContext / Bindings | Не исполняются при импорте, не превращаются в строки |
| Resources / Styles / Templates | Source-preserving representation; универсальный Template Designer не реализован |
| Geometry | Только ограниченная литеральная грамматика; сложные object elements read-only |

## 8. PropertyGrid

Существующие typed editors, validation, Value Source, defaults, Reset, атомарный Undo/Redo, Favorites, категории и поиск сохранены. JSON null поддержан для nullable complex literals. Проверены смена selection, Add Form и независимое восстановление форм.

Fallback rows для сложных свойств сохранены у типов без reviewed adapter (отдельный DataGrid regression). Для типов с adapter не создаётся дублирующая read-only строка. Общая политика editable не включается принудительно для всех обнаруженных свойств.

## 9. JSON и AXAML Round-trip

Для всех 12 типов в property proof выполнялись edit → JSON file save/read/open → оба native Preview. Сравнивался фактический восстановленный model state, а не только успешность парсинга.

Для новых литералов проверены минимальный один attribute edit, Reset, Undo Reset, Redo Reset, Undo edit до точного исходного текста. Сохранялись одинарные кавычки, XML escaping, неизвестный namespace/attribute и соседний комментарий. Binding, StaticResource, DynamicResource и object property elements RotateTransform/RectangleGeometry остаются read-only и сохраняются без изменения.

Source map/patch writer, document version/checksum, conflict detection, ACK и JSON contract не менялись. Полная AXAML regeneration не применялась. Новые controls импортируются и допускают scalar edits; whitelist структурной вставки Phase 4C не расширялся: новые четыре типа не объявляются структурно вставляемыми в imported AXAML.

## 10. Preview и Generated Runtime

Действительные native types/properties проверены в Designer, Legacy Preview, AXAML Preview и imported Preview. Два экспортированных приложения **Grid и Canvas** прошли restore/build/run: **12 native types, 147 property pairs в каждом**, включая матрицу, origin, Geometry.Bounds/FillContains, brushes, dashes и SelectableTextBlock interaction. Geometry semantics проверены на явном ограниченном fixture, не для произвольных paths.

Отдельный generated runtime из source-preserving структурного сценария подтвердил **20 native controls**, восемь insert types, parent/order/Canvas placement/raw Tag, Combo/List collections и native selection/popup. SQL generated application подтвердил Customers=3, nested Orders=2, независимые формы и resize.

End-to-End certified lower bound: **91/4935 → 147/4935 (1,84% → 2,98%)**. Это canonical пары с actual Inspector/JSON/Designer/обоими Preview/imported Preview и двумя compiled/running generated apps, а не 47,48% доказанных runtime сценариев. Runtime placeholders не добавлялись.

## 11. Export safety

Phase 5E assertions для unsupported ItemsSource, invalid enum/Binding/Geometry/complex JSON, incomplete collection и cancelled atomic ZIP publication проходят. Старый корректный output при preflight отказе не заменяется частичным. Реализованное ранее ограничение общей folder publication transaction не объявляется устранённым.

Первый red run новых тестов выявил legacy transform syntax, следующий — space-delimited dashes. Исправления сделаны в shared literal/export boundaries; assertions проверяют реальные native values. Полный export pipeline не переписывался.

## 12. Performance

Методика: тот же существующий профиль, 5 warm-up + 30 samples/type/operation, последовательные процессы без параллельной сборки или другого benchmark. Измеряются UI-thread allocations, Median/P95, GC, variance и process peak working set; последнее является high-water mark процесса, а не изолированным footprint одной операции.

| Inspector | Before Median/P95, мс | Final Median/P95, мс | Allocated MB | GC0/1/2 за 30 |
|---|---|---|---|---|
| Button | 37,86 / 86,98 | 44,63 / 94,28 | 2,27 → 2,28 | [3, 3, 0] → [5, 5, 1] |
| NumericUpDown | 84,85 / 113,60 | 98,90 / 134,39 | 10,24 → 10,25 | [20, 20, 0] → [19, 19, 0] |
| MaskedTextBox | 85,77 / 119,85 | 104,93 / 177,84 | 12,18 → 12,20 | [22, 22, 0] → [20, 20, 0] |

Контрольный запуск архивной production DLL Phase 4F в конце текущей серии (тот же harness, те же 30 samples):

| Late Before | Median, мс | P95, мс | Allocated MB |
|---|---|---|---|
| Button | 59,37 | 104,06 | 2,27 |
| NumericUpDown | 105,95 | 189,21 | 10,24 |
| MaskedTextBox | 88,74 | 121,93 | 12,18 |

Import сравнивался с архивной Phase 4F DLL (SHA-256 `5a7e0f6e…70ffb`) и финальной DLL, одним harness/fixture, 5 warm-up + 30 samples на сцену:

| Сцена | Phase | Import Median/P95, мс | Import MB | Patch Median/P95, мс |
|---|---|---|---|---|
| Small | Before | 1,54 / 4,32 | 0,35 | 1,76 / 11,25 |
| Medium | Before | 12,50 / 19,69 | 3,22 | 20,67 / 39,59 |
| MainWindow | Before | 82,84 / 108,75 | 50,02 | 70,98 / 91,75 |
| Small | After | 1,39 / 1,99 | 0,37 | 1,80 / 2,86 |
| Medium | After | 14,12 / 14,86 | 3,33 | 33,11 / 35,06 |
| MainWindow | After | 72,60 / 88,82 | 52,03 | 75,87 / 87,39 |

Полные 30-sample Toolbox/Inspector/JSON/Export/Preview результаты четырёх новых компонентов: [Phase5FNewControlsPerformance.csv](Phase5FNewControlsPerformance.csv). Отдельный import/patch CSV: [Phase5FImportPerformance.csv](Phase5FImportPerformance.csv).

Подтверждённая локальная регрессия — изменение disclosure category: три прежние read-only строки `Advanced` стали editable в раскрытой `Behavior`. При неизменном числе строк это увеличило rebindings Button 80→86, NumericUpDown 82→88, MaskedTextBox 84→90 и число создаваемых editor views. Исправление возвращает эти свойства в **прежний Advanced**, сохраняя пользовательское раскрытие и возможность редактирования; отдельный regression раскрывает категорию и выполняет edit. Пулы, новый PropertyGrid или перестановка Control по ключам не вводились.

Промежуточный код с раскрытой Behavior: Button Median/P95 **55,60/87,92 мс, 2,31 МБ**; NumericUpDown **106,26/176,80 мс, 11,44 МБ**; MaskedTextBox **127,18/273,26 мс, 12,42 МБ**. После исправления: **44,63/94,28 и 2,28 МБ**, **98,90/134,39 и 10,25 МБ**, **104,93/177,84 и 12,20 МБ** соответственно. ViewsReused возвращены точно к 80/82/84, ViewsCreated — к 1/29/37, число строк — 56/63/71. Extra allocations NumericUpDown устранены почти до исходного уровня. Это исправление лишней начальной UI-нагрузки, а не заявление об устранении всего latency tail.

Metadata reflection не является основным bottleneck: BuildCount после warm-up = 0; lookup на Button составляет около 0,27 мс против десятков миллисекунд selection/binding/layout. Отдельные stages `Selection.Changed`, `Selection.LegacyDescriptorEditors`, `DesignerStateChanged` оставлены как opt-in измерения. Основные затраты находятся в active editor DataContext/binding updates и native layout при переходе TextBox→target, а не повторном Reflection discovery. Необоснованный глобальный metadata cache не добавлялся.

Системный разброс подтверждён **повторным дифференциальным запуском архивной Phase 4F production DLL**, а не заявлен по предположению: неизменённый Button baseline Median вырос 37,86→59,37 мс, NumericUpDown 84,85→105,95 мс при тех же allocations. Это не доказывает внешнее происхождение каждого outlier. В частности, MaskedTextBox final P95 **177,84 мс** остаётся выше early/late baseline **119,85/121,93 мс**: остаточная latency регрессия сохраняется как **P2**, её точный остаточный механизм не установлен. Общего ускорения Inspector и исправления P95 не заявляется; все серии сохранены, лучший повтор не выбирался.

Для большого MainWindow один и тот же import/patch harness с 30 samples дал **82,84→72,60 мс Median**, **108,75→88,82 мс P95**, allocations **50,02→52,03 МБ**. Source Patch Median **70,98→75,87 мс**, P95 **91,75→87,39 мс**. Увеличение allocations соответствует расширению source/property mapping; отдельного алгоритмического ускорения parser/source map не вводилось. Подтверждённой latency регрессии большого import в этой паре нет, а небольшое увеличение работы/allocations source adapters явно показано. Исторические 49,45 мс / 35,30 МБ Phase 4B не сравниваются напрямую с текущим инструментированным harness и расширенным mapping.

Эксперимент отключения binding у скрытой legacy descriptor-панели отклонён: доказанного allocation/latency выигрыша не получено, исходная привязка восстановлена. Проверка materialization выполнялась отдельно от benchmarks; она не используется как замена измерению live selection cost. Строки Advanced при ручном раскрытии имеют реальную стоимость редакторов — benchmark default disclosure не объявляется измерением всех раскрытых категорий.


Артефакты содержат исходный baseline, rejected experiment и accepted-code measurements; результаты не подменялись лучшим повтором. Performance данные и stage breakdown: [Phase5FPerformance.csv](Phase5FPerformance.csv), [Phase5FInspectorStages.csv](Phase5FInspectorStages.csv).

## 13. Memory и UI Automation

Preview Memory **36/36 в трёх независимых SQL-enabled процессах**. В каждом 44 census checkpoints; max survivors Window/Context/Items/Grid = 0. Также пройдены новые Legacy/AXAML 10-cycle checks и Phase 5B 20-cycle checks. GC/assertions/waits не ослаблялись. Независимые snapshots не суммировались как уникальные окна. Source/session retention дополнительно проверяет Phase 4D regression.

UIA native crash `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Accessibility, AutomationPeer, COM/GCHandle lifecycle не отключались и не менялись. Memory Gate не считается исправлением старого AV. Использовался прежний NativeCapture pipeline с отдельными directories/result metadata и защитой dump archives; новые binary dumps в Git не добавлены.

Forensic inventory: 75 завершённых launch-capture records; captured native AV=0, capture failures=0, timeouts=0. Nonzero child exits=3; исходные failed assertions сохранены. Отсутствие нового AV не устанавливает старый root cause.

## 14. VSIX / Standalone

Current Release VSIX packaging audit — PASS: manifest/version, 639 entries, relative paths, required host executable/runtimeconfig/deps, runtime dependency closure и отсутствие visual runtime assemblies в корне devenv bridge. Версия сохранена 0.1.18. SHA-256: `becac468f05f0c01cf1aa5d692c0173ef25231d633af963c93e54e15c07404b9`.

Phase 4F automated integration 15/15 и normal-environment IPC 3/3 проходят, включая ACK, stale version/checksum/conflicts и session behavior. General property adapters используют тот же Inspector/patch path. Broad 72/72 покрывает Standalone, Grid-first/Canvas, Multi Form, JSON, export, DataGrid/Eremex isolation.

**INSTALLED VSIX: NOT VERIFIED для текущего Phase 5F payload. PHYSICAL DPI/DND: NOT VERIFIED.** Ранние ручные действия с другой сборкой Phase 4F не подменяют эту приёмку; установка/ручной запуск текущего расширения не выполнялись.

## 15. Regression / Build

| Проверка | Результат |
|---|---|
| Solution / runner build | PASS; 0 errors |
| Phase 5F, final category/disclosure | 73/73 |
| Phase 5E | 58/58 |
| Phase 5B shared shapes/containers | 73/73 |
| PropertyGrid Phase 3A | 30/30 |
| Collections Phase 3B | 30/30 |
| Layout7 | 15/15 |
| Phase 4A / 4B | 38/38; 52/52 |
| Phase 4C / 4D | 85/85; 14/14 |
| Phase 4F automated integration | 15/15 |
| Metadata / pointer / Grid Drop | 45/45; 26/26; 40/40 |
| Broad normal environment / IPC | 72/72; 3/3 |
| SQL LocalDB / live source | 47/47; PASS |
| Preview Memory, clean processes | 36/36 × 3 |
| New generated Grid / Canvas | 2/2 apps; 147 native property pairs each |
| Patched AXAML generated runtime | 20 native nodes; 8 inserted types; parent/order/property checks |
| SQL generated runtime | PASS; Customers=3 / Orders=2 / resize |
| Installed VSIX / physical DPI | NOT VERIFIED |
| UIA historical crash | UNRESOLVED |

Solution build: 0 ошибок; существующий NU1603 (SDK analyzer fallback 17.7.20→17.7.22) сохранён. Runner build: 0 ошибок/0 warnings. Generated projects сохраняют прежний net6.0 и NETSDK1138 EOL warning; TargetFramework ради зелёного build не менялся. PostgreSQL live не выполнялся: отсутствие test server помечено SKIP, LocalDB результат им не подменяется.

Broad temporary cleanup сообщил locked ToolboxStandardItemsExport.dll после 72/72; assertions/exit code успешны, файлы не удалялись принудительно. Named Pipes проверены вне ограниченного sandbox, production IPC ради sandbox permissions не менялся.

## 16. Первые красные прогоны

* `first-checks`: **36 PASS / 26 FAIL**, child exit 1. Legacy `1,0,0,1,3,4` принимался Transform.Parse, но AXAML ITransform converter отвергал его. Исправлен parser/contract на `matrix(...)`, добавлена проверка отклонения legacy grammar.
* `focused-1`: **0 PASS / 1 FAIL**, child exit 1 — диагностическое сужение того же transform-format отказа перед исправлением, с текстом ошибки AXAML converter.
* `second-checks`: **57 PASS / 5 FAIL**, child exit 1. Native AXAML numeric collection converter отвергал `StrokeDashArray="2 1"`. Исправлен typed numeric export в `2,1`; JSON авторского литерала не менялся.
* Затем набор расширен assertions и проверкой read-only fallback; итог **73/73**. Первые logs/capture result.json сохранены отдельно, успешный повтор без изменения кода не используется как исправление.
* Эксперимент с hidden legacy binding не дал доказанного улучшения и отклонён. Native inspection показал 0 visual descendants у скрытых ItemsControl; первоначальная гипотеза о втором созданном visual editor tree не подтверждена. MainWindow.axaml восстановлен точно, его изменений в final diff нет.

Все перечисленные red tests — managed assertion failures; native AV/process crash этим не объявляется.

## 17. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs`
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`
* `DesignerSystem/Metadata/ShapeLiteralCodec.cs`
* `DesignerSystem/Metadata/StandardControlProperties.Extensions.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `DesignerSystem/Metadata/TransformLiteralCodec.cs`
* `Docs/Phase5FControlAcceptance.csv`
* `Docs/Phase5FEvidenceSummary.json`
* `Docs/Phase5FImportPerformance.csv`
* `Docs/Phase5FInspectorStages.csv`
* `Docs/Phase5FNewControlsPerformance.csv`
* `Docs/Phase5FPerformance.csv`
* `Docs/Phase5FPropertyCoverage.csv`
* `Docs/Phase5FPropertyCoverageDetails.csv`
* `Docs/Phase5FPropertyCoverageSummary.json`
* `Docs/Phase5FPropertyPriority.csv`
* `Docs/Phase5FRemainingControls.csv`
* `Docs/StandardLibraryCoverage.csv`
* `Docs/StandardLibraryCoverageSummary.json`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase5F.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `Models/ToolboxItem.cs`
* `ViewModels/MainWindowViewModel.PropertyMetadata.cs`
* `ViewModels/MainWindowViewModel.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5F.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase5FRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/Summarize5FPropertyCoverage.py`

Raw diagnostics/JSON files/generated apps/profiling copies находятся в ignored `artifacts/universal-phase5f`. Полный итоговый перечень проверок и hashes: [Phase5FEvidenceSummary.json](Phase5FEvidenceSummary.json). Dumps, secrets, пользовательские settings и binaries в Git не включались.

## 18. Оставшиеся ограничения

1. 11 core types остаются Metadata Discovered Only; strict Full — 0/72. Complex property authoring и arbitrary bindings/resources/templates не объявлены реализованными.
2. Common matrix/Clip adapters имеют ограниченный литеральный контракт. Geometry visual editor, transform graphs, animations, gradients и template design не реализованы.
3. Imported structural create whitelist не расширен на четыре новых типа; scalar import/round-trip проверен отдельно.
4. Button Inspector P95/allocations и большие AXAML остаются отдельными performance рисками; измерения приведены без заявления причинного ускорения при изменённой нагрузке.
5. UIA crash UNRESOLVED; MaskedTextBox selection ограничения, установочная VSIX/DPI приёмка и full folder publication transaction не объявлены исправленными.

## 19. Следующий конкретный шаг

Пройти отдельный manual acceptance gate именно текущего SHA-256 VSIX в изолированном VS: открыть безопасную копию AXAML с четырьмя новыми controls, проверить typed scalar edits/Apply/Save/Reset/Undo/Redo/reopen, ручной version conflict и повторный host connect. Отдельно проверить физические selection/resize/DnD и zoom при DPI 100/125/150%; structural insertion проверять только для разрешённых типов Phase 4C.

Для дальнейшего property coverage приоритетны bounded Cursor/FlowDirection adapters с native/runtime/geometry acceptance и отдельный проект complex collection/template authoring. Для Inspector нужен локальный measured prototype по active editor binding/layout, без отключения diagnostics/accessibility или нового PropertyGrid. Новая фаза, release/tag, commit/push автоматически не выполняются.

**STANDARD CONTROLS & PROPERTIES: READY WITH CONDITIONS.** Новые ограниченные workflows проходят native/JSON/Preview/Export/runtime assertions; перечисленные внешние и непроверенные gates сохраняются.
