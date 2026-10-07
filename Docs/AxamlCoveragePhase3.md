# AXAML coverage Phase 3, VSIX 0.1.14

## Blocker impact report до изменений

Исследован настоящий `Views/MainWindow.axaml`, не snapshot/fixture. Исходник
не изменялся. SHA-256 UTF-8 текста:
`245A2291637F3FF7798651BC74386D050BB34702C66057EF952EB2C13CC32476`,
462480 UTF-16 code units. Baseline Phase 2: 225 / 1473, 25 skipped boundaries.

`Instances` здесь означает число верхних **границ opaque subtree**, а не все
экземпляры типа в XML. Потомки границ не пересчитываются как отдельные границы.
`Blocked` не включает сам blocker. `Known` считает потомков, типы которых уже
поддерживались в Phase 2; это верхняя оценка, не обещание безопасного traversal
через следующий неизвестный родитель. Все стандартные типы имеют namespace
`https://github.com/avaloniaui`; custom type имеет `using:FormDesigner.Views`.

| Type | Instances | Blocked descendants | Phase 2 known descendants | Category | Priority |
| --- | ---: | ---: | ---: | --- | --- |
| TabControl | 2 | 1140 | 1035 | Selector / ItemsControl | HIGH, вместе с TabItem |
| ScrollViewer | 7 | 49 | 42 | ContentControl | HIGH |
| WrapPanel | 5 | 32 | 25 | Panel | HIGH |
| Button с visual Content | 2 | 2 | 2 | ContentControl | MEDIUM, общий Content mechanism |
| ComboBox | 3 | 0 | 0 | Selector / ItemsControl | LOW для этого baseline |
| ItemsControl | 2 | 0 | 0 | ItemsControl | LOW, templates/bindings |
| ListBox | 1 | 0 | 0 | Selector / ItemsControl | LOW |
| TreeView | 1 | 0 | 0 | Selector / ItemsControl | LOW |
| ProgressBar | 1 | 0 | 0 | Control | LOW |
| views:DesignerSurface | 1 | 0 | 0 | CustomControl | OUT OF SCOPE |
| **Всего** | **25** | **1223** | **1104** | | |

25 boundary roots + 1223 visual descendants = 1248 opaque visual candidates.
Первый ComboBox по source order скрывал **0** visual descendants, поэтому
он не был первым по реализации. TabItem находится внутри TabControl и
не образует отдельную Phase 2 boundary, но необходим для разблокирования страниц.

### Все 25 границ

`Children` включает непосредственные XML property wrappers, если они есть;
`Blocked` и `Known` исключают styles/resources/templates и property wrappers.
Тип и namespace определяются последним сегментом path и таблицей выше.

| Source path | Children | Blocked | Known |
| --- | ---: | ---: | ---: |
| Window/Grid/Border[1]/Grid/Border[2]/StackPanel/ComboBox | 0 | 0 | 0 |
| Window/Grid/Grid/Grid/Button[1] | 1 | 1 | 1 |
| Window/Grid/Grid/Grid/Border[1]/Grid/TabControl | 6 | 62 | 44 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Border[1]/ScrollViewer | 1 | 1 | 0 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Border[2]/Grid/WrapPanel | 6 | 12 | 5 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Border[3]/Grid/ScrollViewer | 1 | 17 | 17 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Border[3]/Grid/Border[2]/StackPanel/ComboBox | 0 | 0 | 0 |
| Window/Grid/Grid/Grid/Border[3]/Grid/views:DesignerSurface | 0 | 0 | 0 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Grid/Border/Grid/StackPanel/WrapPanel | 4 | 8 | 8 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Grid/Border/Grid/WrapPanel | 4 | 4 | 4 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Grid/Grid/Border/Grid/TreeView | 2 | 0 | 0 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Grid/Grid/Grid/Border[2]/ScrollViewer | 1 | 1 | 1 |
| Window/Grid/Grid/Grid/Border[3]/Grid/Grid/Grid/ScrollViewer | 1 | 27 | 24 |
| Window/Grid/Grid/Grid/Border[5]/Grid/TabControl | 6 | 1078 | 991 |
| Window/Grid/Grid/Grid/Button[2] | 1 | 1 | 1 |
| Window/Grid/Grid/Border[1]/Grid/Border[2]/Grid/WrapPanel | 4 | 4 | 4 |
| Window/Grid/Grid/Border[1]/Grid/Border[2]/Grid/StackPanel[2]/ComboBox | 0 | 0 | 0 |
| Window/Grid/Grid/Border[1]/Grid/Grid[1]/ScrollViewer | 1 | 1 | 0 |
| Window/Grid/Grid/Border[1]/Grid/Grid[2]/ScrollViewer | 1 | 1 | 0 |
| Window/Grid/Grid/Border[1]/Grid/Grid[3]/ScrollViewer | 1 | 1 | 0 |
| Window/Grid/Border[3]/Grid/Border/Grid/WrapPanel | 4 | 4 | 4 |
| Window/Grid/Border[3]/Grid/Border/Grid/Grid[2]/Border[1]/StackPanel/ItemsControl | 1 | 0 | 0 |
| Window/Grid/Border[4]/Grid/Border/Grid/Border/ListBox | 1 | 0 | 0 |
| Window/Grid/ItemsControl | 1 | 0 | 0 |
| Window/Grid/Border[5]/Grid/Border/StackPanel/ProgressBar | 0 | 0 | 0 |

## Implementation decision

Добавлены четыре стандартных типа: **TabControl, TabItem, ScrollViewer,
WrapPanel**. Уже существующий Button получил безопасный single Content child.
Не добавлены ComboBox, Expander, DataGrid, Eremex/custom controls.
Главный прирост даёт пара TabControl/TabItem; ScrollViewer/WrapPanel встречаются
в разблокированных страницах и потому важнее, чем показывают только верхние границы.

Раньше `AxamlImportService.ImportElement` выбирал известный тип из списка;
TabControl/ScrollViewer/WrapPanel попадали в `AddOpaqueSubtree` с
`UnsupportedControl`. Button с visual child попадал туда с `UnsupportedContent`.
Рекурсивно все потомки получали `OpaqueAncestor`. Это безопасно сохраняло source,
но блокировало 1035 ранее известных controls только под двумя TabControl.

### Небольшая metadata model

`AxamlControlMetadata` описывает SourceType, ProjectionType, ContainerKind,
ChildProperty и допустимый ItemType. ContainerKind:
`None`, `PanelChildren`, `SingleContent`, `HeaderedContent`, `Items`, `Decorator`.
Traversal единый, разрешён только для явно перечисленных standard controls.
Plugin contracts не менялись: существующий descriptor сообщает panel layout,
но не описывает Content/Header/Items AXAML slots. Новая metadata относится
именно к source projection, не к plugin ABI и не к произвольному runtime type lookup.

`ParentId` сохраняет hierarchy. Group alias для некоторых контейнеров использует
существующую designer model, но оригинальный type живёт в SourceMap.Element;
patch writer читает свойства по **source type**, а не Group alias.
JSON schema не менялась; новые literal properties хранятся в существующем
CustomProperties bag. Standalone JSON behaviour не переключается на AXAML traversal.

### Контейнеры и native layout

- TabControl принимает только статические TabItem; TabItem сохраняет единственный
  Content child. Все страницы входят в model/source map, отображается выбранная.
  SelectedIndex доступен в Inspector; inactive pages получают нулевые preview bounds,
  но не удаляются из model. Header и SelectedIndex обновляются отдельными source spans.
- ScrollViewer сохраняет единственный Content child и обе ScrollBarVisibility.
  Используется настоящий Avalonia ScrollViewer/ScrollContentPresenter. При
  off-screen Measure/Arrange нет visual-root attachment; Content и scroll axes
  штатного presenter инициализируются явно перед UpdateChild. Это повторяет
  необходимые owner values, не заменяет layout engine.
- WrapPanel использует настоящий Avalonia WrapPanel и Orientation, а не Canvas
  coordinates; порядок source children сохраняется.
- Button с visual Content поддерживает вложенность; редактор Content/Text
  блокируется, чтобы не создать attribute, конкурирующий с visual child.
- Child property wrappers (`TabControl.Items`, `TabItem.Content`,
  `ScrollViewer.Content`, `Button.Content`) не становятся controls и не меняют ParentId.

Native projection не загружает user AXAML, DataContext, assemblies, styles,
templates или bindings открытого solution. Используется текущий общий
DesignerSurface и существующий interaction controller; второго Canvas нет.
Точная визуальная эквивалентность работающему MainWindow **не заявляется**.

Avalonia API/source: [TabControl](https://docs.avaloniaui.net/controls/navigation/tabcontrol),
[ScrollViewer](https://v11.docs.avaloniaui.net/docs/reference/controls/scrollviewer/),
[ScrollContentPresenter 11.1.1](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs).

### Safety / source preservation

Неизвестный parent остаётся opaque subtree; generic flatten/transparent wrapper
не введён. Несколько Content children, несколько wrappers, одновременные
Content attribute и child, raw text content, не поддерживаемое безопасно,
остаются opaque. При ItemsSource даже статические children не вычисляются:
items subtree остаётся opaque. Header property element, resource expression
или binding сохраняется и имеет read-only property editor.

Styles/resources/templates исключаются из live visual candidates, не исполняются
и не блокируют независимый Content traversal. Comments/unknown attributes/nodes
сохраняются в OriginalText. `AxamlPatchWriter` меняет только editable attribute
value spans; opaque values, смена hierarchy/type и неподдерживаемая перестановка
детей блокируют patch. No-edit Apply даёт исходный текст text-for-text.
Checksum/version validation, IPC, VS buffer ownership не менялись.

## MainWindow coverage

| Metric | Phase 2 | Phase 3 |
| --- | ---: | ---: |
| XML elements | 2507 | 2507 |
| Live visual candidates | 1473 | 1473 |
| Imported designer controls | 225 | **1251** |
| Coverage | 15.27% | **84.93%** |
| Partial imported controls | 154 | 669 |
| Opaque/skipped visual candidates | 1248 | **222** |
| Skipped boundaries | 25 | 76 |

Recovered **+1026** visual elements, **+69.65 процентного пункта**.
Новые 76 boundaries не являются ухудшением: теперь обнаруживаются blockers
глубоко внутри ранее целиком opaque страниц. Ранее их закрывали две границы TabControl.
Imported означает model/source-map coverage, **не** одновременно видимые controls
и **не** полностью реализованные properties/API этих controls.

Classification: property wrappers **62**, template roots **52**, resource roots
**0**, Style nodes **74**, Setter nodes **312**, bindings **1259** (1255 attributes
+ 4 Binding nodes). Wrapper/template/style counts не являются непересекающимися
категориями; template descendants исключены из 1473. Denominator не изменился.

### Remaining blockers

Осталось **9 типов**, поэтому top-10 содержит только 9 реальных строк.

| Type | Boundaries | Blocked descendants | Known descendants |
| --- | ---: | ---: | ---: |
| Expander | 4 | 140 | 133 |
| ComboBox | 38 | 6 | 0 |
| ItemsControl | 25 | 0 | 0 |
| TreeView | 3 | 0 | 0 |
| ListBox | 2 | 0 | 0 |
| ProgressBar | 1 | 0 | 0 |
| views:DesignerToolbox | 1 | 0 | 0 |
| views:DesignerSurface | 1 | 0 | 0 |
| views:DesignerPropertyInspector | 1 | 0 | 0 |

76 roots + 146 descendants = 222 opaque visual candidates. Наибольший следующий
vertical slice по этой метрике: Expander. Реализовывать Phase 4 автоматически
не стали. ComboBoxItem-потомки пока не известны importer, поэтому Known = 0.

## Diagnostics / tests

Кнопка `Подробнее` использует прежний AXAML Import Report. Теперь там дополнительно
выводятся aggregated blocker impact, категория, namespace, полный source path,
XML children, visual/known/reachable descendants и reason каждого boundary.
Reachable estimate исключает следующий неизвестный тип и не является гарантией
безопасного import: реальные цифры даёт только projection.

Новые fixtures: `Samples/RoundTrip/Coverage/{Tabs,Content,Wrap,Preserved}.axaml`.
Новые сценарии в `Program.AxamlCoverage.cs`:

1. AxamlCoverageTabs: все страницы, selected/inactive bounds, Inspector Header,
   single-span patch, ACK identity, SelectedIndex, переключение проекции.
2. AxamlCoverageContent: explicit Content wrappers, nested Button.Content,
   native scroll measurement выше viewport, Text preservation, visibility enum patch.
3. AxamlCoverageWrap: native wrapping и Orientation Inspector/patch.
4. AxamlCoveragePreserved: templates/resources/bindings, unknown sibling/parent,
   read-only opaque values, single-span Content edit, checksum conflict.
5. AxamlCoverageUnsafeContent: ambiguous Content и bound ItemsSource.
6. AxamlCoverageRealMainWindow: настоящий файл, parse/report/hierarchy/native layout,
   shared surface nonblank render и Inspector Header minimal patch.
7. AxamlCoverageIpc: Named Pipes open, Header edit, Apply, source-preserving buffer
   edits, ACK и second edit after ACK. Receive работает одновременно с Apply,
   как существующий lifecycle test; последовательное ожидание Send до Receive
   может заблокировать pipe writer.

Старый `AxamlCapabilityNoCanvasSafeIdentity` использует неподдерживаемый Expander
вместо теперь поддержанного TabControl; его opaque/no-fallback-insertion assertions
сохранены. Simple GREEN проверяет add/move/resize/Inspector/Apply/ACK/second edit.

Результаты полного regression run и упаковки фиксируются после проверки в
`artifacts/diagnostics/axaml-phase3/`. Generated smoke outputs очищаются прежней
retention policy; сохраняются только компактные logs/report и один PNG.

### Проверка 2026-10-04

`dotnet build FormDesigner.sln --no-restore -m:1` успешен, 0 errors.
Прежний NU1603: Microsoft.VisualStudio.SDK требует SDK.Analyzers >= 17.7.20,
доступна 17.7.22. Dependencies и suppression не менялись.

| Suite / filter | Result |
| --- | ---: |
| Axaml (включая Simple GREEN, layouts, complex, JSON и все 7 Phase 3) | 60/60 |
| DesignerSurface | 14/14 |
| VsHost | 10/10 |
| Vsix | 5/5 |
| SaveLoadMultiFormProject | 1/1 |
| MultiFormDocumentStateIsolation | 1/1 |
| MultiFormToolboxDropPropertyEdit | 1/1 |
| MultiFormSameControlNamesPropertyGridEdit | 1/1 |
| NewProject | 7/7 |
| EremexTextEditorVerticalSlice | 1/1 |
| EremexDataGridControlVerticalSlice | 1/1 |

Итого **98 уникальных сценариев / 102 успешных исполнения** (filters пересекаются).
Каждый сценарий также собирает generated project. Реальный отдельный VsHost
дважды открыл актуальный MainWindow, а также SimpleAvaloniaApp и Complex fixture
в прямом и обратном порядке. Native shared-surface screenshot просмотрен:
не пустой, но design-time placeholders/overlays не являются runtime equivalence.

При удалении некоторых компактных run folders были transient IOException/file-lock
warnings; bin/obj успешных scenarios очищены. Retention продолжает обрабатывать
оставшиеся небольшие каталоги. Это не build/test failure, но не заявляем идеально
безошибочную очистку. Cleanup lifecycle в Phase 3 не менялся.

Итоговый VSIX содержит **130 entries**, из них **121 под VsHost/**; version в
реальном `extension.vsixmanifest` **0.1.14**. Проверены EXE, deps/runtimeconfig,
FormDesigner/PluginContracts/Avalonia.Controls DLL и pkgdef/Protocol в корне.
Упакованный FormDesigner.dll имеет тот же SHA-256, что актуальный build output:
`74799F9A5D7706EBA194DD850D74D4513A70AF26AAC24958FBF30EC6F48F181E`.
Avalonia/FormDesigner/Eremex visual DLL в корне VSIX **0**; bridge dependency
closure/command table/runtime packaging guards прошли. Реальная установка нового
VSIX в пользовательскую Visual Studio в этом запуске не выполнялась.

## Changed files

- `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs` (new metadata/literal properties).
- `DesignerSystem/AxamlRoundTrip/AxamlBlockerImpact.cs` (new impact analysis).
- `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs` (generic safe slots traversal).
- `DesignerSystem/AxamlRoundTrip/AxamlImportStructureReport.cs` (classification/report).
- `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs` (native containers/pages).
- `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs` (source type / literal property bag).
- `ViewModels/MainWindowViewModel.cs` (source capabilities / Inspector rows).
- `Views/MainWindow.axaml.cs` (existing shared surface preview, inactive pages).
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlCoverage.cs` (new tests).
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlCapabilities.cs` (opaque fixture).
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` (scenario registration).
- Four `Samples/RoundTrip/Coverage/*.axaml` fixtures.
- `AvaloniaDesigner.VSIX/{AvaloniaDesignerVsixPackage.cs,source.extension.vsixmanifest}`
  (version only, 0.1.14; command registration unchanged).
- This report and link in `Docs/VisualStudioPoCArchitecture.md`.

## Manual verification / limitations

Install `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix` 0.1.14
after closing Visual Studio. Existing command remains
`Tools -> Open in Avalonia UI Visual Designer`.

1. Recheck SimpleAvaloniaApp: add Button, move/resize, Inspector, Apply, VS dirty
   buffer, comment/unknown attribute, second edit after ACK.
2. Open actual `Views/MainWindow.axaml`; `Подробнее` must show 1251 / 1473.
3. Select TabControl and change literal SelectedIndex in Inspector; inactive
   pages must stay in source. Select TabItem and edit literal Header; diff should
   only change Header. Bound/resource Header stays read-only.
4. Edit VS buffer externally before Apply; stale patch must be rejected.

Tab header mouse switching, interactive scroll viewport editing, dynamic items,
DataGrid/Eremex/custom AXAML import, bindings/styles/templates editing remain out
of scope. Header selection uses existing Inspector rather than a second tab designer.
Opaque subtree operations and new nested-container insertion remain conservative.
Automated projection, IPC and external VsHost tests do not replace manual testing
of actual Visual Studio buffer integration. MainWindow is **partially supported**.
