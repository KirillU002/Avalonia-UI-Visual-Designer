# Universal Controls — Phase 5D

Дата: 16.11.2026. Исходный commit: `824d224932bb3d3851618aa9c840451d768f17c5`, ветка `main`; до изменений дерево Git чистое. Продукт Beta 0.1, Avalonia 11.1.1, Designer net8.0; Windows 10 x64, build 19045, SDK 10.0.402. Версии пакетов, TargetFramework, тема, JSON contract и VSIX/IPC не менялись. Коммиты, теги и релизы в этой фазе не создавались.

## 1. Результат и границы

Добавлены четыре корневых компонента **ContentControl, ItemsControl, Carousel, TabStrip** и **TabStripItem** через существующий Collection Editor. TabStripItem создаётся внутри коллекции, а не отдельным свободно размещаемым Toolbox item.

ContentControl принимает один визуальный Content через ParentId; ItemsControl, Carousel и TabStrip используют прежний `Items.Collection`, version 1, стабильные Id и порядок элементов. Общий Metadata Engine, существующий PropertyGrid, native projection, Preview и export bridge расширены без параллельных подсистем. Runtime Avalonia objects не сериализуются.

Это ограниченные проверенные workflows. Произвольные ItemsSource, ItemTemplate, ControlTemplate, Resources и Styles не становятся безопасно редактируемыми вследствие регистрации типа.

## 2. Аудит стандартной библиотеки

Основной список: [StandardLibraryCoverage.csv](StandardLibraryCoverage.csv), [сводка](StandardLibraryCoverageSummary.json). Все публичные обнаруженные Control-derived типы: [UniversalControlCoverage.csv](UniversalControlCoverage.csv). Сборки Controls, DataGrid, ColorPicker и ItemsRepeater учитываются раздельно; темы и сторонние библиотеки в знаменатель не входят.

В Avalonia.Controls знаменатель сохранён: 72 применимых типа, включая 66 самостоятельных и 6 дочерних. Абстрактные/базовые/служебные типы, template presenters, корневые Window/UserControl и связанные Popup/ToolTip/ContextMenu исключены из процента Toolbox. NativeMenu/NativeMenuItem и Flyout/MenuFlyout перечислены отдельно как связанные невизуальные объекты. Ни один инфраструктурный класс не добавлен ради процента.

| Assembly | Applicable | Partial Before → After | Strict Full | Partial % Before → After | Missing After |
|---|---:|---:|---:|---:|---:|
| Avalonia.Controls | 72 | 52 → 57 | 0 | 72.22 → 79.17 | 15 |
| Avalonia.Controls.ColorPicker | 2 | 0 → 0 | 0 | 0.00 → 0.00 | 2 |
| Avalonia.Controls.DataGrid | 1 | 1 → 1 | 0 | 100.00 → 100.00 | 0 |
| Avalonia.Controls.ItemsRepeater | 1 | 0 → 0 | 0 | 0.00 → 0.00 | 1 |

Core: **52/72 → 57/72 (72,22% → 79,17%)**. Это четыре root adapters и один дочерний item adapter. Strict Fully Supported: **0**, без заявления поддержки всех свойств.

`Fully Supported` остаётся строгим критерием: проверенная цепочка и отсутствие неподдержанных writable свойств/коллекций. Проверенная scalar/collection slice остаётся `Partially Supported`; частичный процент нельзя называть полным покрытием Avalonia.

### Оставшиеся самостоятельные типы

| Тип | Роль / текущий статус | Что требуется |
|---|---|---|
| `Avalonia.Controls.AutoCompleteBox` | Standalone; metadata only | ItemsSource/filtering/value completion adapter |
| `Avalonia.Controls.ExperimentalAcrylicBorder` | Standalone; metadata only | Специализированный adapter и отдельная native/runtime приёмка |
| `Avalonia.Controls.LayoutTransformControl` | Standalone; metadata only | Безопасный Transform object editor |
| `Avalonia.Controls.PathIcon` | Standalone; metadata only | Проверяемая Geometry + theme semantics |
| `Avalonia.Controls.Primitives.HeaderedItemsControl` | Standalone; metadata only | Преимущественно базовый тип; не добавлять ради процента |
| `Avalonia.Controls.RefreshContainer` | Standalone; metadata only | Специализированный adapter и отдельная native/runtime приёмка |
| `Avalonia.Controls.RelativePanel` | Standalone; metadata only | Sibling-reference constraints |
| `Avalonia.Controls.ReversibleStackPanel` | Standalone; metadata only | Специализированный adapter и отдельная native/runtime приёмка |
| `Avalonia.Controls.SelectableTextBlock` | Standalone; metadata only | Выделение текста и native input |
| `Avalonia.Controls.Shapes.Arc` | Standalone; metadata only | Специализированный adapter и отдельная native/runtime приёмка |
| `Avalonia.Controls.Shapes.Sector` | Standalone; metadata only | Специализированный adapter и отдельная native/runtime приёмка |
| `Avalonia.Controls.SplitView` | Standalone; metadata only | Pane + Content и правила двух областей |
| `Avalonia.Controls.ThemeVariantScope` | Standalone; metadata only | ThemeVariant scope и наследование |
| `Avalonia.Controls.ToggleSplitButton` | Standalone; metadata only | Toggle + flyout/event workflow |
| `Avalonia.Controls.TransitioningContentControl` | Standalone; metadata only | Переходы/async lifetime и Content |

Осталось **15/72 (20,83%)** в сохранённом знаменателе. Raw discovery ToolboxCandidate — консервативная эвристика; она не равна reviewed descriptor admission или подтверждению полной поддержки.

Все перечисленные типы существуют как public в установленной сборке. AutoCompleteBox требует адаптера источника данных/фильтрации; SplitView — двух различных областей содержимого; RelativePanel — связей между элементами; TransitioningContentControl — отдельной проверки переходов и lifetime. HeaderedItemsControl имеет низкий приоритет как преимущественно базовый тип. Для этих компонентов не заявлены Toolbox/Preview/Export adapters. Template presenters, NativeMenu, Popup и ToolTip не подменяются такими адаптерами.

## 3. Новые компоненты и Toolbox

| Компонент | Designer / данные | Inspector / Preview / Export |
|---|---|---|
| ContentControl | Один дочерний control через ParentId, Content layout; вложенный Layout для нескольких children | Общие реальные Avalonia properties; native ContentControl и Content, Grid/Canvas |
| ItemsControl | Локальные ContentControl items с текстовым Content, именем, IsEnabled и порядком | Прежний Collection Editor; отсутствует фиктивный SelectedIndex; native ItemsControl |
| Carousel | Те же локальные items; SelectedIndex | Native Carousel; Next/Previous; ItemTemplate/PageTransition не редактируются как строки |
| TabStrip | Локальные TabStripItem с Content/IsEnabled/Name, порядок, SelectedIndex | Native TabStrip/TabStripItem и выбор; это полоса выбора, не страницы TabControl |

Категории: ContentControl — контейнеры; три коллекционных control — существующая группа коллекций. Сохранены поиск, русские подсказки, иконки, выбор после создания и единственная регистрация каждого root. TabStripItem редактируется через Items.

Строковое Content коллекционного элемента — проверенная часть существующего контракта. Произвольное визуальное содержимое каждого Carousel item, item templates, data-driven autocomplete и смешанные object collections в этой фазе не реализованы. Структурное редактирование imported AXAML остаётся read-only; Phase 4C не начиналась.

## 4. Доработки существующих компонентов

* **MaskedTextBox:** PromptChar использует общий безопасный char codec; принимается один допустимый символ. Добавлены SelectionStart/SelectionEnd, неотрицательная validation. При native creation сначала применяются Mask/настройки, затем Text, затем границы selection. В export границы записываются после Text. JSON сохраняет authored значения; обычную native коэрцию Avalonia за пределами текста Designer не заменяет.
* **Calendar:** добавлен реальный SelectionMode, enum editor, JSON/source/export. Это не редактор коллекции SelectedDates: сложная коллекция остаётся защищённой.
* **Path:** StrokeLineCap и StrokeDashOffset используют тот же scalar metadata/codec, что существующие Shape properties; Geometry editor не переписан.
* **Label:** внутренний Text корректно сопоставлен с native Content при описании Inspector metadata. Это исправление metadata identity, а не новое фиктивное Text property Avalonia.
* В descriptors коллекционных controls SelectedIndex оставлен только там, где он реально существует. Menu/TreeView/ItemsControl не получают выдуманного свойства.

Независимый CaretIndex не добавлен: тест показал, что его применение может схлопнуть заданное выделение. Selection хранится как пара границ; не создаётся противоречивая тройка состояний. Это не исправление известного поведения синтетического SelectAll/TextInput в нативном MaskedTextBox. Физическая клавиатура, paste/IME и экранный диктор требуют ручной проверки.

### Исправлен подтверждённый P1 Export

Дополнительный negative test получил три частичных Folder Export проекта: ItemsSource был неподдержан, control пропущен, а API успешно записал проект. Root cause: перехват исключения на уровне одного control подходил для диагностики генерации, но не останавливал запись distribution artifacts.

Добавлен минимальный preflight `EnsureLocalItemsSourcesExportable` перед Project/ZIP/Validate Build. Он использует существующие `IsRoot`/`HasExternalSource`, проверяет текущие controls и документы неактивных форм, выдаёт понятную ошибку до создания файлов. Поддерживаемый ListBox BindingSource workflow не затрагивается. Три regression tests проверяют отказ Folder Export и ZIP с проблемой на неактивной форме; исходный property bag сохраняется. Это не универсальный новый Export Pipeline и не реализация внешних ItemsSource для новых controls.

## 5. Property Coverage

[Каноническая таблица до/после](Phase5DPropertyCoverage.csv), [детализация](Phase5DPropertyCoverageDetails.csv), [сводка и определение метрики](Phase5DPropertyCoverageSummary.json).

Знаменатель — writable применимые native/CLR signatures и разрешённые contextual attached properties. Read-only диагностическое состояние и чужие attached registrations исключены; сложные writable значения и известные authorable getter collections (Items/Children/Styles/Resources и другие) остаются в знаменателе. Read-only setter сам по себе не означает неизменяемую коллекцию. Aliases одной регистрации схлопываются по native owner/name. Пары «тип–свойство» и глобально уникальные свойства приведены отдельно; наследование не выдаётся за новые уникальные свойства.

Показатель — консервативное покрытие проверенных literal/collection/content/asset adapters: **Editable ∩ Exportable / Applicable**. Discovered, Displayable, Editable, JSON Serializable, Importable, Source Patchable и Exportable — разные колонки. Source Patchable означает разрешённый literal subset; Binding/Resource/Template не становятся patchable literals. Коллекционные/Content/asset adapters и ограниченный whitelist свойств дочерних items учитываются отдельно от literal flags и только при существующем adapter соответствующей стороны Before/After. Это не исчерпывающий процент поддержки object graphs.

| Core показатель | Before | After |
|---|---:|---:|
| Применимые пары тип–свойство | 4935 | 4935 |
| Глобально уникальные native/CLR identities | 332 | 332 |
| Пары с aliases | 5207 | 5207 |
| Исключённые alias-дубликаты | 272 | 272 |
| Displayable | 2300 | 2486 |
| Editable (JSON Designer) | 1502 | 1637 |
| JSON Serializable | 1891 | 2043 |
| AXAML Importable | 1868 | 2020 |
| Source Patchable (literal subset) | 1849 | 1999 |
| Exportable | 1502 | 1637 |
| Editable ∩ Exportable | 1502 | 1637 |
| Уникальные identities с такой цепочкой | 152 | 156 |
| Unsupported для полной цепочки | 3433 | 3298 |
| Property Coverage по парам | 30.44% | 33.17% |
| По глобально уникальным identities | 45.78% | 46.99% |

`Unsupported` здесь — отсутствие подтверждённой JSON-edit/export цепочки, а не отсутствие любого import/preservation workflow. Это сигнатурный консервативный знаменатель, не рекомендация показывать все свойства в Inspector. Новые pairs не выдаются за новые уникальные registrations.

| Тип (After) | Applicable | Displayable | Editable | JSON | Import | Source patch | Export | Unsupported |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ContentControl | 67 | 44 | 35 | 37 | 37 | 37 | 35 | 32 |
| ItemsControl | 69 | 44 | 30 | 35 | 35 | 34 | 30 | 39 |
| Carousel | 77 | 46 | 31 | 36 | 36 | 35 | 31 | 46 |
| TabStrip | 76 | 46 | 31 | 36 | 36 | 35 | 31 | 45 |
| TabStripItem | 68 | 44 | 3 | 37 | 37 | 37 | 3 | 65 |
| MaskedTextBox | 100 | 56 | 46 | 48 | 48 | 48 | 46 | 54 |
| Calendar | 74 | 50 | 40 | 42 | 42 | 42 | 40 | 34 |
| Path | 58 | 38 | 30 | 32 | 32 | 32 | 30 | 28 |
| Label | 68 | 45 | 35 | 37 | 37 | 37 | 35 | 33 |

Для двух сторон использованы один перечень сигнатур и одна методика: baseline CSV сохранён до изменений; after CSV получен из финальной сборки. Таблица отражает допуск adapters, а не доказательство корректности всех комбинаций значений. Реальные typed editors/native values проверены отдельными тестами; Favorites, поиск, категории и Value Source сохранены.

## 6. AXAML Import / source-preserving round-trip

Добавлены metadata для ContentControl, Carousel, TabStrip/TabStripItem; ItemsControl использует прежний import adapter. В импортированном ContentControl распознаётся и literal Content, и один вложенный control. Content property elements, native item hierarchy, namespaces и исходная source map сохраняются. Carousel/TabStrip SelectedIndex включён в reviewed metadata, иначе старый literal accessor не давал безопасного Reset.

Проверены изменение одного атрибута, Reset, Undo/Redo и точное восстановление patch text; неизвестные атрибуты сохраняются. Для новых свойств MaskedTextBox, Calendar и Path есть отдельные source Reset/Undo регрессии. Binding expressions остаются защищёнными и не исполняются. Никакая операция не регенерирует весь исходный AXAML.

Структурные Add/Remove/Reorder для импортированных коллекций не разрешены. Comments/resources/styles/templates, checksum/version/ACK/conflict protection проверяются прежними Phase 4A/4B и IPC suites; поддержка произвольного AXAML по-прежнему частичная.

## 7. Designer, JSON, Preview и Generated Runtime

Новые root controls проверяются на реальном native visual tree. В Grid не назначаются Canvas offsets; в Canvas сохраняются authored позиции. Пройдены routed toolbox events, pointer resize, selection/Undo/Redo, вложенные StackPanel/WrapPanel/DockPanel/UniformGrid/Viewbox и переключение форм. JSON сравнивается по полной модели controls, включая Id/ParentId/property bag/items/order, а не только по наличию файла.

| Phase / native roots | Grid | Canvas |
|---|---|---|
| 5D: 4 | Restore / Build / Run / assertions PASS | Restore / Build / Run / assertions PASS |
| 5C: 6 | Restore / Build / Run / assertions PASS | Restore / Build / Run / assertions PASS |
| 5B: 8 | Restore / Build / Run / assertions PASS | Restore / Build / Run / assertions PASS |
| 5A: 6 | Restore / Build / Run / assertions PASS | Restore / Build / Run / assertions PASS |

**8/8 свежих exported applications**, текущий production binary. Phase 5D: native ContentControl/ItemsControl/Carousel/TabStrip и реальные дочерние controls с видимым TextBlock; MaskedTextBox selection/Prompt, Next/Previous и resize. Phase 5A дополнительно подтвердил реальный resize GridSplitter и RepeatButton hold/release; Phase 5B — nested Content, Geometry, UniformGrid, scrolling, Expander keyboard; Phase 5C — masked input, nullable toggle, Calendar popup и Spin. После probe временный код удалён, App startup восстановлен и каждый проект пересобран. Положительный exit code сам по себе не принимается: probe result должен начинаться с PASS.

Экспортированные приложения используются без placeholder controls. Для new controls проверяются действительные item type/content/order, ContentControl с TextBox, Carousel Next/Previous и отображаемый item, TabStrip selection, Mask/Prompt/selection и resize. Это автоматизированный native runtime acceptance, а не ручная проверка установленного продукта.

## 8. Performance и PropertyGrid P95

Release, один UI процесс без параллельных UI suites/build/SQL benchmarks; 5 warm-up и 30 измерений на control/operation. Median — среднее центральных значений; P95 — nearest rank. Allocations — UI thread, GC counts — process-wide. Синхронный selection + Window.UpdateLayout не является измерением первого compositor frame. Before снят с неизменённого baseline binary; after — финальная сборка. В after Inspector добавлены timestamps разделения selection/layout, без изменения поведения UI.

Полные median/P95/allocations/GC/variance и peak working set: [Phase5DPerformance.csv](Phase5DPerformance.csv); стадии Inspector: [Phase5DInspectorStages.csv](Phase5DInspectorStages.csv). GC — суммы по 30 операциям; peak working set накапливается в процессе и не является доказательством retention.

| Inspector | Median Before → After, ms | P95 Before → After, ms | Median MiB Before → After |
|---|---:|---:|---:|
| Button | 39.20 → 37.71 | 80.18 → 72.58 | 1.55 → 1.55 |
| NumericUpDown | 99.49 → 97.48 | 156.62 → 141.52 | 10.22 → 10.22 |
| MaskedTextBox | 92.41 → 105.56 | 195.99 → 136.00 | 9.65 → 11.42 |

| Новый control / Inspector | Median / P95, ms | Median MiB |
|---|---:|---:|
| ContentControl | 61.76 / 121.75 | 6.11 |
| ItemsControl | 50.71 / 78.74 | 3.01 |
| Carousel | 49.98 / 76.56 | 3.64 |
| TabStrip | 60.80 / 87.66 | 3.64 |

| Shared operation | Median / P95 Before, ms | Median / P95 After, ms | Median MiB Before → After |
|---|---:|---:|---:|
| ToolboxInitialize | 5.07 / 10.93 | 4.51 / 11.55 | 1.22 → 1.25 |
| MetadataDiscoveryCached | 0.55 / 0.74 | 0.52 / 1.22 | 0.02 → 0.02 |
| DesignerProjection | 9.69 / 30.45 | 6.79 / 28.19 | 2.07 → 2.06 |
| UndoRedo | 103.17 / 453.12 | 90.67 / 411.92 | 7.86 → 7.82 |

| Inspector decomposition After | Selection median, ms | UpdateLayout median, ms |
|---|---:|---:|
| Button | 34.62 | 2.49 |
| NumericUpDown | 43.08 | 50.57 |
| MaskedTextBox | 50.69 | 55.86 |

Medians отдельных стадий не складываются в median общей операции. NumericUpDown: metadata lookup 0,23 ms, RefreshPropertyInspector 14,00 ms, 34 editor creations и один refresh; остальной selection pipeline и native layout остаются существенными. MaskedTextBox: после трёх новых rows число editor creations выросло **33 → 39**, allocations **9,65 → 11,42 MiB**, median вырос. Это подтверждённая стоимость дополнительных rows/материализации, а не metadata rescanning. P95 колеблется вместе с GC/UI работой; ускорение от production optimization не заявляется, поскольку такой оптимизации не было. Полная атрибуция оставшихся allocations требует allocation-stack профиля; P2 не закрыта. Нет доказательств, что заменой Metadata Engine можно устранить хвост.

| AXAML corpus | Import median/P95 After, ms | Import median MiB | Patch median/P95 After, ms |
|---|---:|---:|---:|
| MainWindow | 46.11 / 67.50 | 33.68 | 66.27 / 74.33 |
| Medium | 9.69 / 11.25 | 2.02 | 24.23 / 30.05 |
| Small | 0.95 / 1.29 | 0.22 | 1.44 / 1.70 |

Исторические Phase 5C import и Phase 5B scale данные выделены в CSV как Historical, не как новый запуск архивного binary в этой фазе. В отличие от common workflow Before/After они не являются контролируемым экспериментом одного дня. Native Measure/Arrange большого imported MainWindow (192,73 ms в Phase 4B) и imported Preview около 597 ms заново не измерялись; эти ограничения не закрыты.

| 100-control scene / After | Add median/P95, ms | Inspector median/P95, ms | Legacy Preview median/P95, ms | AXAML Preview median/P95, ms |
|---|---:|---:|---:|---:|
| 100Shapes | 57.97 / 68.32 | 23.41 / 29.74 | 363.76 / 398.17 | 579.56 / 661.16 |
| 100Nested | 55.66 / 73.75 | 36.36 / 64.69 | 358.65 / 403.07 | 606.77 / 662.00 |

100 shapes и 100 nested children прошли native visual-tree проверки в обеих Preview; Save/Load/Export измерены в CSV. Свежая матрица 300/500 элементов в Phase 5D не выполнялась. Все measured metadata rebuild counters — 0. Производительность больших форм остаётся ограничением; P95/allocations не объявлены исправленными.

Metadata rebuilds в steady-state отсутствуют. Каталог не пересоздаётся при selection. Исходный stage profile показывает metadata lookup в долях миллисекунды и повторное создание editor views на переключении разных наборов rows. Основной вклад в хвост дают UI projection/measure/arrange и GC; это не доказательство ошибки Metadata Engine. Масштабная оптимизация PropertyGrid не выполнялась. Полный causal allocation-stack профиль рендера остаётся отдельной P2 задачей.

## 9. Memory, UIA, SQL, VSIX

Новых native AV: **0** в 48 process-local capture runs (включая красные assertion runs). Managed failures при этом сохранены и не считаются успехом. Восемь generated processes также запускались под NativeCapture. Два сценария Memory20 Phase 5D подтвердили сборку 20 + 20 закрытых Preview; общий Preview Memory **36/36**, включая live SQL и CloseDuringQuery. Это не проверка GC root обычного Window; исторический ordinary-Window/UIA retention не объявлен устранённым.

SQL Server: отдельный GUID LocalDB instance и fixture, **47/47**, settings/schema/BindingSource/both Previews PASS; generated runtime Grid Customers=3, nested Grid Orders=2, реальные видимые значения, независимые ViewModels и resize PASS. Удалён только собственный тестовый DB/instance. PostgreSQL offline/contract **20/20**, live SKIP: test server не задан.

VSIX/VsHost/IPC: Broad normal **72/72**, дополнительные source/property-element IPC **1/1**. Это автоматизированная проверка host/transport/patch/ACK/conflict paths, не ручная установленного VSIX. Новый sandbox run Broad в этой фазе не выполнялся: использована нормальная среда; прежний 69/72 из Phase 3B.2/3B.3 не переписан и не назван 72/72 в sandbox. Eremex isolation покрыт Broad и Grid Drop; стандартные generated приложения не требуют Eremex.

Исторический `uiautomationcore.dll / ProviderCallouts::GetPatternProvider` crash остаётся **UNRESOLVED**. Ноль новых AV в выполненных запусках не означает устранение root cause. Accessibility не отключалась. Native capture использован для test runners и generated runtime; full dumps/metadata/hashes сохраняются локально вне Git. Новый параметр CaptureDirectory у ProbeRuntime требует свежую директорию, не меняет глобальные WER settings и сохраняет отрицательный assertion gate.

Ручная установка VSIX в Visual Studio, DPI/несколько мониторов, physical input/IME и экранный диктор не выполнены. Avalonia assemblies в devenv.exe не переносились. Linux/macOS runtime не заявлен. PostgreSQL live не проверен; сервер не устанавливался.

## 10. Build / regression / первые неудачи

| Проверка | Результат |
|---|---:|
| Solution Release build | 0 errors / 1 прежний NU1603 warning |
| Smoke runner build (финальная проверка visual tree) | 0 errors / 0 warnings |
| Phase 5D, включая visible native items и Export guard | **49/49** |
| Phase 5C / 5B / 5A | 55/55 · 73/73 · 47/47 |
| Metadata | 45/45; повторно после production fix PASS |
| PropertyGrid / Collections | 30/30 · 30/30; Collections повторно после fix PASS |
| Phase 4A / 4B / Phase 2 | 38/38 · 52/52 · 40/40 |
| Nested / Pointer / Grid Drop | 59/59 · 26/26 · 40/40 |
| Layout Phase 7A | **14/15**, DeferredContextMenu FAIL |
| Broad (normal permissions) | **72/72** |
| Additional source-property IPC | 1/1 |
| LocalDB / Preview Memory | 47/47 · 36/36 |
| PostgreSQL offline | 20/20; live SKIP |
| Fresh exported projects Grid/Canvas, 5A–5D | **8/8 restore/build/native runtime PASS** |
| Property coverage integrity | PASS; одинаковый знаменатель Before/After, без alias inflation |
| Performance | 5 warm-up + 30 per operation; raw summaries/GC/variance сохранены |

**Общий gate не полностью зелёный:** первый и единственный Layout7 run дал 14/15. `DeferredContextMenu`: old wrapper detached, old menu closed, current wrapper без открытого меню. Это известный intermittent managed UX failure, **P2**; он не связывается с UIA AV и не скрывается последующим retry. Native crash capture для этого процесса: Captured=false, ExitCode=1. Основные новые workflows/JSON/Preview/Export проходят; подтверждённых новых P0/P1 после Export fix не осталось.

Артефакты воспроизведения и проверки: `artifacts/universal-phase5d/` — baseline log/CSV, first/second/third/fourth/final/visual checks, export-repro (0/3), final-validation, generated-validation, before/performance. Директория ignored; binary dumps в Git не добавлены. Известные warnings: VSSDK NU1603, generated net6.0 NETSDK1138; в Broad cleanup не удалены собственные загруженные test DLLs (UnauthorizedAccessException), функциональные assertions 72/72 завершились. Пользовательские файлы не очищались.

Первые прогоны 5D не скрыты: 19/36, 29/36, 44/46; затем 46/46 после конкретных исправлений. После этого отдельное воспроизведение Export дало 0/3: созданы частичные проекты. Финальный suite расширен до 49 проверок. Первый выявил отсутствующие native preview routes и неполную source metadata; также исправлены некорректные assertions, сравнивавшие целый workspace с UI/export metadata вместо полной модели controls. Третий выявил конфликт caret/selection и ошибочное ожидание Text editor для opaque imported Content expression. Caret не включён в поддерживаемый набор, Label JSON metadata проверена отдельно; сохранность imported expressions остаётся в других регрессиях. Это последовательная разработка с сохранением красных логов, не retries без изменения причины.

Первые две сборки имели 3 missing namespace errors, затем 1 ошибку имени test API; они сохранены. После исправления test nullable warning устранён. Baseline solution build: 0 ошибок, известный NU1603 для cached VSSDK analyzers fallback. Generated net6.0 warning NETSDK1138 не устранялся сменой TargetFramework.

## 11. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Collections.cs`
* `DesignerSystem/BuiltIn/CollectionControlFactory.cs`
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`
* `DesignerSystem/Metadata/CollectionPropertyMetadata.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `DesignerSystem/Metadata/StandardLibraryCoverageReport.cs`
* `Docs/Phase5DInspectorStages.csv`
* `Docs/Phase5DPerformance.csv`
* `Docs/Phase5DPropertyCoverage.csv`
* `Docs/Phase5DPropertyCoverageDetails.csv`
* `Docs/Phase5DPropertyCoverageSummary.json`
* `Docs/StandardLibraryCoverage.csv`
* `Docs/StandardLibraryCoverageSummary.json`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase5D.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `Models/CollectionItemsContract.cs`
* `Models/DesignerLayoutModes.cs`
* `Models/ToolboxItem.cs`
* `ViewModels/MainWindowViewModel.Collections.cs`
* `ViewModels/MainWindowViewModel.PropertyMetadata.cs`
* `ViewModels/MainWindowViewModel.cs`
* `Views/MainWindow.axaml.cs`
* `Views/PreviewWindow.axaml.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5APerformance.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5D.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal5DCoverage.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase5DRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/ProbeRuntime.ps1`
* `smoke-tests/UniversalMetadata/README.md`
* `smoke-tests/UniversalMetadata/SummarizePropertyCoverage.py`

## 12. Ограничения и завершение Phase 5

* Общая поддержка стандартной библиотеки остаётся частичной; Templates/Styles/Resources/Commands/произвольные CLR objects не превращены в текстовые поля.
* Collection adapters новых controls поддерживают локальные типизированные items, а не произвольные ItemsSource. Внешний источник блокирует конфликтующее редактирование/export вместо молчаливой подмены локальными items.
* Native UIA crash — unresolved; окно retention и preview ownership оцениваются раздельно, accessibility сохранена.
* MaskedTextBox selection state поддержан в проверенном сценарии; историческая synthetic text replacement проблема и ручной IME/paste остаются ограничениями.
* PropertyGrid P95, большие AXAML, opaque Geometry и installed VSIX/DPI остаются отдельными acceptance/performance рисками.
* Intermittent ContextMenu failure не объявляется исправленным этой фазой; существующая regression сохранена.

Следующий шаг для завершения серии Phase 5: согласовать перечень требуемых пользовательских controls, отдельно проверить AutoCompleteBox, SelectableTextBlock/PathIcon и двухобластной SplitView; подготовить матрицу приемлемых сложных свойств. Нужен allocation-stack профиль PropertyGrid и ручная VSIX/DPI/input приёмка. Структурное редактирование импортированных коллекций относится к отдельной Phase 4C. Следующая фаза автоматически не начинается.

**STANDARD CONTROLS COMPLETENESS: READY WITH CONDITIONS.**

Четыре новых root controls и TabStripItem прошли полную проверенную цепочку до работающего native приложения; закрыт P1 частичного Export. Условия: известный ContextMenu 14/15, unresolved UIA risk, PropertyGrid allocations/P95 и ручная VSIX/DPI/input приёмка. Это готовность проверенного bounded scope Phase 5D, а не полная поддержка всей Avalonia.Controls.
