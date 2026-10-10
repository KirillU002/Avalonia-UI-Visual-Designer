# AXAML Import Phase 4: Expander / ComboBox

## Root cause и настоящий AXAML

В Phase 3 `AxamlControlMetadata.Known` не содержал Expander, ComboBox и
ComboBoxItem. `AxamlImportService.ImportElement` получал null descriptor и
вызывал `AddOpaqueSubtree(UnsupportedControl)`; дети получали `OpaqueAncestor`.
Механизмы `HeaderedContent` / `Items` уже существовали, но эти типы ими не
пользовались. Native projection и Inspector не получали соответствующих models.

Исследован **настоящий** `Views/MainWindow.axaml`, исходник не изменён:

| Конструкция | Во всём XML | Live visual tree |
| --- | ---: | ---: |
| Expander | 7 | 4 |
| ComboBox | 66 | 45 |
| ComboBoxItem | 6 | 6 |

У четырёх live Expander literal Header и один Border Content child; три имеют
`IsExpanded=False`, один `True`. Header: «Расширенный внешний вид»,
«Расширенное поведение», «Экспертные настройки», `Export profile`.
Из 45 live ComboBox у 44 `ItemsSource` Binding, у 40 `SelectedItem` Binding;
восемь содержат ItemTemplate. Один ComboBox содержит шесть статических items.
Templates и их controls не относятся к live visual denominator.

До изменений Expander образовывал 4 boundary и блокировал 140 visual descendants;
ComboBox образовывал 38 boundary и блокировал 6 descendants. Ещё семь ComboBox
находились под Expander и не были самостоятельными Phase 3 boundaries.

## Implementation

- Metadata: Expander использует `HeaderedContent/Content`, ComboBox - `Items`,
  допустимые статические children - ComboBoxItem и простые typed String values.
  Traversal общий; неизвестные wrappers не становятся transparent автоматически.
- ParentId сохраняет Content/Items hierarchy. Закрытый Expander **не удаляет**
  дочернее дерево из document/source map. Его потомки получают нулевые preview
  bounds до раскрытия; popup items ComboBox не превращаются в Canvas controls.
- `AxamlPhase4Projection` создаёт только доверенные native Avalonia controls.
  Одна фабрика используется Measure/Arrange и shared DesignerSurface. Content
  Expander содержит существующую designer wrapper, со штатными selection handlers;
  второе дизайнерское дерево или отдельный layout engine не добавлены.
- Native Expander поддерживает Header, literal Content, IsExpanded,
  ExpandDirection; ComboBox - статические items, SelectedIndex, PlaceholderText
  (свойство существует в используемой Avalonia 11.1.1). Width/Height, Margin,
  alignment остаются в общей source property map и native layout.
- Inspector использует metadata editors: text, boolean, enum, number. Новые
  literals хранятся в существующем CustomProperties bag; JSON schema не меняется.
  Safe edits вызывают существующий NotifyDesignerStateChanged, не ApplyDocument.
  Selection и IDs проверяются после commit; render coalescing не переделан.
- Binding/resource values сохраняются read-only на уровне свойства.
  Обычные attribute ItemsSource, SelectedItem, SelectedValue отображаются как
  preserved значения; qualified/inherited property forms также учитываются guards.
  При source SelectedItem/SelectedValue редактор SelectedIndex блокируется, чтобы
  не создать конкурирующий selection value (включая
  `SelectingItemsControl.SelectedItem` и inherited property elements).
  DataContext и пользовательские
  templates/styles не загружаются и не исполняются; mock items не подставляются.
- Static SelectedIndex сопоставляется с **исходным** порядком items. Opaque item
  не сдвигает индексы остальных. При ItemsSource статическое items subtree
  остаётся opaque: две независимые коллекции не смешиваются.
- Body text ComboBoxItem и typed String декодируется как XML text и отображается.
  Body text Content остаётся read-only: attribute patch writer не создаёт
  конкурирующий Content attribute. Literal `ComboBoxItem Content="..."` editable.
- Несколько Content children, смешанные text/visual children и Content attribute
  вместе с visual child остаются opaque. Header property elements сохраняются;
  в этой phase Header editor предназначен для literal attribute, не visual Header.
- Name badge не перекрывает Header/selected item новых native previews.
  Source-mapped Expander/ComboBox доступны для resize при editable Width/Height;
  правила остальных standalone controls не менялись.

### Source preservation

`AxamlPatchWriter` по-прежнему меняет только owned editable source spans.
Header, IsExpanded, ExpandDirection, SelectedIndex, PlaceholderText и item Content
используют общую literal property map. Перестановка ComboBox items, как и
TabControl pages, отклоняется: нельзя молча изменить semantics SelectedIndex.
Bindings, namespace, comments, Styles, Templates, unknown attributes и порядок
детей сохраняются. No-edit Apply возвращает input text-for-text. Checksum,
version, ACK и Visual Studio buffer ownership не менялись; диск VsHost не пишет.

Недоступные attached properties, **отсутствующие** в source, не понижают element
capability до partial. Например, отсутствие Grid.Row у Canvas child не делает
простой документ PartiallyEditable. Реально opaque source значения учитываются.

## MainWindow coverage

| Метрика | Phase 3 | Phase 4 |
| --- | ---: | ---: |
| XML elements | 2507 | 2507 |
| Live visual candidates | 1473 | 1473 |
| Imported | 1251 | **1439** |
| Coverage | 84.93% | **97.69%** |
| Partial imported | 669 | 765 |
| Opaque visual candidates | 222 | **34** |
| Skipped subtree boundaries | 76 | **34** |
| Expander imported | 0 | **4** |
| ComboBox imported | 0 | **45** |

Recovered **+188 visual elements**, **+12.76 процентного пункта**:
42 прежние boundaries + 146 заблокированных descendants. Среди восстановленных
descendants - семь ComboBox. После разблокирования Expander поддержаны все 140
его visual descendants; шесть статических ComboBoxItem тоже импортированы.
Denominator настоящего MainWindow не изменился. Typed String item values в новых
fixtures не считаются visual controls и не увеличивают искусственно coverage.

Stress test проверяет native visual descendants с model Tag и ненулевыми bounds:
**4 Expander и 45 ComboBox**, при preview-only раскрытии и выборе owning tab pages.
Это не означает одновременную видимость всех controls, выполнение bindings,
эквивалентность runtime MainWindow или 97.69% всего Avalonia API.
Отдельный тест меняет literal Header настоящего Expander через Inspector и
проверяет ровно один source span; исходный MainWindow на диске не изменяется.

### Remaining blockers

| Тип | Boundaries | Blocked live descendants |
| --- | ---: | ---: |
| ItemsControl | 25 | 0 |
| TreeView | 3 | 0 |
| ListBox | 2 | 0 |
| ProgressBar | 1 | 0 |
| views:DesignerToolbox | 1 | 0 |
| views:DesignerSurface | 1 | 0 |
| views:DesignerPropertyInspector | 1 | 0 |

Всего 34 opaque visual roots, live visual descendants внутри них нет.
Template descendants здесь намеренно не учитываются. Эти типы не реализованы
в Phase 4; Eremex AXAML import, Binding/Style/Template editors остаются вне scope.

## Diagnostics и regression fixtures

Всегда выводятся aggregated события `AXAML_EXPANDER_IMPORTED`,
`AXAML_EXPANDER_CHILDREN_IMPORTED`, `AXAML_COMBOBOX_IMPORTED`,
`AXAML_COMBOBOX_ITEMS_IMPORTED`, `AXAML_PROPERTY_OPAQUE`, `AXAML_COVERAGE_SUMMARY`.
Patch generation сохраняет `AXAML_PATCH_CREATED`.
Полный per-element/property trace включается через `Import(detailedDiagnostics: true)`
или `FORMDESIGNER_AXAML_IMPORT_DIAGNOSTICS=verbose`. AXAML Import Report по кнопке
«Подробнее» сохраняет полный inventory/path/reason, без огромного default trace.

Fixtures: `Samples/RoundTrip/Phase4/{ExpanderStack,ExpanderGrid,ComboStatic,
ComboBindings,TextItems,Combined}.axaml`. В `Program.AxamlPhase4.cs` добавлены
20 сценариев: literal/StackPanel/Grid Content, collapsed model retention,
Header/IsExpanded/direction, static items/index/placeholder, binding guards,
comments/unknown attributes, item Content/text values, no-edit identity,
unsafe Content/index mapping, diagnostics, ACK/second edit/conflict, настоящий
MainWindow/native surface и Named Pipes Apply/ACK/VS text-edit validation.

Тестовый harness прокручивает native Dispatcher loop для штатного 33-ms render
timer: `RunJobs()` без Windows message loop недостаточно для проверки нового кадра.
Production timer/lifecycle не изменялся.

## Результаты проверки

Проверка Debug 2026-10-09: **143 уникальных сценария**, **153 успешных запуска**
в целевых suites (часть фильтров пересекается), **0 failed**. Все 20 новых
Phase 4 сценариев прошли; полный unfiltered/Release runner не заявляется.

| Suite filter | Passed |
| --- | ---: |
| Axaml | 80/80 |
| DesignerSurface | 14/14 |
| Vsix | 5/5 |
| VsHost | 10/10 |
| MultiForm | 13/13 |
| NewProject | 7/7 |
| PropertyGrid | 7/7 |
| Inspector | 12/12 |
| Eremex | 5/5 |

`dotnet build FormDesigner.sln --no-restore -m:1`: 0 errors, одно существующее
NU1603 предупреждение VSSDK о fallback SDK.Analyzers 17.7.22. Smoke runner build:
0 errors / 0 warnings. Restore не потребовался: TFM/dependencies не менялись.
Автоматическое XML-сравнение tracked csproj/props/targets с HEAD: 11 csproj,
**PackageReference/PackageVersion changes = 0; TargetFramework changes = 0**.

Named Pipes и VSSDK packaging проверялись вне ограниченного sandbox:
в нём Windows pipe client/IsolatedStorage возвращали access denied.
Исправлений IPC или обновлений tooling для этого не делалось.
Generated-project smoke builds выполнялись с временным `NuGetAudit=false`,
чтобы не ждать сетевой vulnerability audit на каждом fixture; это не меняет
PackageReference и не является проверкой безопасности зависимостей.

Process/IPC tests запускали **VsHost, извлечённый из финального VSIX**, а не
случайную старую build-копию. Проверены 250 runtime files, exe/dll/deps.json/
runtimeconfig.json, FormDesigner, PluginContracts, Avalonia, plugin/Eremex
dependencies. SHA-256 FormDesigner.dll и VsHost.dll в архиве совпадает с
последней сборкой. В root VSIX нет Avalonia/Eremex/FormDesigner visual assemblies.
Временный распакованный runtime удалён после тестов.

Report, PNG native previews и полные logs находятся в
`artifacts/diagnostics/axaml-phase4/`; относительные README links и git diff
whitespace checks прошли. Было 5 cleanup warnings о locked run directories;
они не являются падениями tests. Regeneratable bin/obj удалялись per scenario,
cleanup architecture в этой задаче не менялась.

## Изменённые файлы

- `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlImportStructureReport.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs` (новый)
- `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Views/MainWindow.axaml.cs` (не исходный AXAML)
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlCapabilities.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlPhase4.cs` (новый)
- `Samples/RoundTrip/Phase4/ExpanderStack.axaml` (новый)
- `Samples/RoundTrip/Phase4/ExpanderGrid.axaml` (новый)
- `Samples/RoundTrip/Phase4/ComboStatic.axaml` (новый)
- `Samples/RoundTrip/Phase4/ComboBindings.axaml` (новый)
- `Samples/RoundTrip/Phase4/TextItems.axaml` (новый)
- `Samples/RoundTrip/Phase4/Combined.axaml` (новый)
- `AvaloniaDesigner.VSIX/AvaloniaDesignerVsixPackage.cs` (только version)
- `AvaloniaDesigner.VSIX/source.extension.vsixmanifest` (только version)
- `README.md`
- `Docs/AxamlCoveragePhase4.md` (новый)

## VSIX

В HEAD перед этой задачей уже была версия **0.1.15** после .NET 8 migration.
Phase 4 поэтому выпускается как **0.1.16**, без переиспользования номера.
Пакет Debug: `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`.
VSIX остаётся net472; VsHost - net8.0. Package versions и TargetFramework не менялись.
Установка и click flow в живом Visual Studio требуют отдельной ручной проверки;
автоматические process/IPC и packaging tests не выдаются за неё.
