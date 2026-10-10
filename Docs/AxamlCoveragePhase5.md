# AXAML Import Phase 5: Standard Avalonia Controls

## Root Cause

В Phase 4 `AxamlControlMetadata.Known` не содержал ItemsControl, ListBox,
TreeView, ProgressBar и их item containers. `AxamlImportService.ImportElement`
получал null descriptor и сохранял subtree как `UnsupportedControl`.
Общий `Items` traversal уже существовал; отдельный importer не требовался.

Проверен настоящий `Views/MainWindow.axaml`, исходник не изменён:

| Live control | Count | Реальные конструкции |
| --- | ---: | --- |
| ItemsControl | 25 | ItemsSource Binding; ItemTemplate, местами ItemsPanel, IsVisible Binding/literal |
| ListBox | 2 | ItemsSource и SelectedItem Binding; ItemTemplate, MaxHeight; event/resource attributes |
| TreeView | 3 | ItemsSource/SelectedItem Binding; Styles, ItemTemplate или DataTemplates; attached properties |
| ProgressBar | 1 | IsIndeterminate=True, Height=10, literal Foreground/Background |

Во всём XML ItemsControl больше: template descendants не являются live controls.
Ни один из этих 31 live controls не содержит статических live descendants.
Их data/template trees не исполняются ради роста coverage.

## Implementation

- Metadata: ItemsControl/ListBox используют общий `Items` descriptor;
  TreeView/TreeViewItem допускают статические TreeViewItem children через тот же
  механизм. ListBoxItem использует `SingleContent`; ProgressBar - leaf.
  Поддерживаются direct children и property wrappers, включая `ItemsControl.Items`.
- Native factory Phase 4 переиспользуется, без второго Designer/Binding Engine.
  Создаются только явно перечисленные доверенные Avalonia controls.
  ParentId и source order сохраняются; никакого flatten в Canvas.
- Статические items отображаются штатными ItemsControl/ListBox/TreeView.
  В design projection используется native StackPanel для реализации статических
  items без viewport virtualization. Пользовательский ItemsPanel сохраняется,
  но не исполняется. Это design-time projection, не runtime equivalence.
- TreeViewItem остаётся native item container. Header содержит selectable designer
  wrapper; nested items остаются в `TreeViewItem.Items`. IsExpanded управляет
  видимостью, не существованием детей в document/source map. Collapsed descendants
  получают нулевые preview bounds и возвращаются после раскрытия.
- ListBoxItem сохраняет native selection semantics и selectable Content wrapper.
  Designer selection независима от source SelectedItem/SelectedIndex.
  Selection handlers не исполняют пользовательские events/bindings.
- Native Measure/Arrange и shared surface используют одинаковые literal layout
  values. Width/Height, Margin, alignment и size constraints не превращаются
  в фиктивные Canvas coordinates. Tree item geometry применяется и к native items.
- Off-screen layout подготавливает ScrollContentPresenter внутри новых native
  Items controls. Эти presenters иначе подключают ItemsPresenter только после
  visual-root attachment. Прежний путь TabControl/Expander не заменяется широким
  преждевременным обходом всех template presenters.
- Общий запрет overlay child-host для новых native Items controls не применяется
  к прежнему TabControl projection: его отдельный page overlay сохранён.
  Stress regression выбирает все owning ancestor pages, а не только одну вкладку;
  проверяется возврат native Expander/ComboBox и новых controls в visual tree.
- Bound collections остаются пустыми, без mock items и пользовательского
  DataContext. Для выбора пустого owner используется 24px design-time minimum
  extent, не записываемый в source. Literal source constraints остаются приоритетными.
- Inspector переиспользует metadata editors и existing CustomProperties bag:
  ListBox SelectedIndex/SelectionMode; ListBoxItem Content; TreeView SelectionMode;
  TreeViewItem Header/IsExpanded; ProgressBar Minimum/Maximum/Value,
  IsIndeterminate/Orientation. Numeric literals finite, invariant-culture.
  JSON schema, plugin contracts и standalone workflow не меняются.
- Binding/resource/property-element values read-only на уровне свойства.
  SelectedItem/SelectedValue ownership блокирует competing SelectedIndex.
  Qualified literals поддерживаются через source aliases; неизвестный inherited
  qualified value сохраняется без синтеза competing attribute.

### Source Preservation

Patch writer по-прежнему меняет только owned source spans. No-edit сохраняет
исходный текст целиком. Unknown attributes, comments, namespaces, Styles,
Resources, Templates и Bindings не регенерируются.

Static children вместе с ItemsSource остаются opaque: две коллекции не смешиваются.
Opaque items не сдвигают source SelectedIndex. Reorder всех `Items` containers
блокируется, пока отсутствует безопасный source-order patch. Deletion guards для
opaque descendants, version/checksum и ACK остаются действующими.
VsHost не пишет AXAML на диск; Apply идёт в Visual Studio text buffer.

## MainWindow Coverage

| Metric | Phase 4 | Phase 5 |
| --- | ---: | ---: |
| XML elements | 2507 | 2507 |
| Live visual candidates | 1473 | 1473 |
| Imported | 1439 | 1470 |
| Coverage | 97.69% | 99.80% |
| Opaque visual elements | 34 | 3 |
| Skipped subtree boundaries | 34 | 3 |
| Partial imported | 765 | 795 |

Recovered: **+31 controls**, **+2.10 процентного пункта**. Denominator не менялся.
ItemsControl +25, TreeView +3, ListBox +2, ProgressBar +1. Это coverage
модели/source map одного stress document, не 99.80% Avalonia API.

### Editability

Из 1470 импортированных live elements: 675 Editable, 795 PartiallyEditable,
0 read-only imported elements; все 1470 имеют хотя бы одно editable property.
Три custom elements Opaque и не создаются в editable projection.
Количество editable properties не означает полную поддержку всех свойств.
У новых bound Items controls безопасные размеры/положение в исходном layout
редактируемы, но ItemsSource/SelectedItem и templates остаются preserved.

Model coverage, native presence, nonzero visible bounds и visual fidelity -
разные показатели. В настоящем MainWindow collection data/templates не
исполняются; owner controls могут оставаться пустыми. Статические данные,
Tree hierarchy, selected ListBox item и ProgressBar проверяются отдельными
небольшими fixtures на shared surface.

После выбора owning ancestor tabs и раскрытия Expander только в test projection:
в native visual tree присутствуют **31/31 новых owners**, все с ненулевыми bounds:
25 ItemsControl, 2 ListBox, 3 TreeView, 1 ProgressBar. Это не означает, что
bound collections заполнены или что скрытые/закрытые элементы исходного приложения
показываются в его обычном runtime. Предыдущий baseline также проверен:
4/4 native Expander и 45/45 ComboBox представлены при выборе owning страниц.

## Remaining Blockers

| Type | Boundaries | Blocked live descendants | Reason |
| --- | ---: | ---: | --- |
| views:DesignerToolbox | 1 | 0 | UnsupportedCustomControl |
| views:DesignerSurface | 1 | 0 | UnsupportedCustomControl |
| views:DesignerPropertyInspector | 1 | 0 | UnsupportedCustomControl |

Произвольные project assemblies/custom controls не загружаются через Reflection.
Eremex/DataGrid AXAML import, Binding/Style/Resource/Template editors не добавлены.
Visual headers, dynamic data hierarchies, custom ItemsPanel, complex SelectionMode
flag combinations и item reorder остаются preserved/partial, не полностью editable.

## Tests and Packaging

Fixtures: `Samples/RoundTrip/Phase5/{Items,List,Tree,Progress}.axaml`.
Новые scenarios: static items, native ListBox selection, Tree ParentId/expanded
hierarchy, Header/IsExpanded patches, numeric range/Value/Orientation,
ItemsSource/SelectedItem/Value bindings, qualified values, templates/resources,
comments/unknown syntax, inherited Items wrappers, source-order guard,
pointer selection/Inspector, native item geometry, no-edit identity,
second edit after ACK, external conflict, real MainWindow and Named Pipes Apply/ACK.

Final solution build (`dotnet build FormDesigner.sln --no-restore -m:1`):
**0 ошибок**, одно прежнее NU1603 предупреждение VSSDK о fallback версии
Microsoft.VisualStudio.SDK.Analyzers. Smoke runner build: 0 warnings/errors.
Отдельный restore solution не требовался: TFM/dependency graph не изменены.

| Final suite | Passed |
| --- | ---: |
| AXAML, включая 25 новых Phase 5 scenarios | 105/105 |
| DesignerSurface | 14/14 |
| VSIX bridge / packaging | 5/5 |
| VsHost / IPC | 10/10 |
| Multi Form | 13/13 |
| New Project | 7/7 |
| PropertyGrid | 7/7 |
| Inspector | 12/12 |
| Eremex TextEditor / DataGridControl vertical slices | 5/5 |
| SimpleFormExport | 1/1 |
| GridLayoutExport | 1/1 |
| RuntimePreviewCanLoadSimpleWindow | 1/1 |
| Net8RuntimeBaseline | 1/1 |
| ExportToProjectBuildValidation | 1/1 |

Итого в финальных suite logs: **173 уникальных scenarios**, **183 успешных
запуска**, **0 failures**. Filters частично пересекаются; повторные запуски не
считаются новыми тестами. Ранние диагностические прогоны в этот итог не включены.
SimpleAvaloniaApp, Canvas add/move/resize, ACK/second edit, checksum conflicts,
Grid/StackPanel/DockPanel, TabControl/TabItem и Expander/ComboBox остались GREEN.
Полные logs и native PNG: `artifacts/diagnostics/axaml-phase5/`.
Snapshots рисуются на временном белом backdrop теста, без изменения темы Designer.

ProgressBar дополнительно проверен в показанном `VsHostWindow`: Value=120.5,
range=0..200, Percentage=60.25, ширина native `PART_Indicator`=180.75px при
ширине control=300px. Off-screen test без visual-root attachment мог иметь
правильный desired Width индикатора, но нулевые Bounds. Test теперь ждёт
окончания настоящего coalesced render, а не предполагает, что хватило 120ms.
Production ProgressBar/layout engine не подменяются ручными расчётами.

Items reorder regression использует допустимые различные StackOrder значения:
отрицательное значение штатно нормализуется в ноль и не доказывает перестановку.

В existing Export build-validation smoke ожидание async результата выполняется
через уже имеющийся `Pump`: после инициализации настоящего UI dispatcher простой
`GetResult` блокировал UI continuation. Production Export Pipeline не изменён.

## VSIX

В HEAD была версия **0.1.16**; Phase 5 выпускается как **0.1.17**.
Пакет: `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`.
VSIX остаётся net472; VsHost - net8.0. NuGet и TargetFramework не меняются.
Автоматический XML audit 12 tracked csproj/props/targets, 32 package declarations:
**package version changes=0**, **framework changes=0**.

Проверен итоговый archive: 250 файлов VsHost runtime, в том числе exe/dll/deps.json/
runtimeconfig.json, FormDesigner, Avalonia и Eremex/plugin dependencies.
`runtimeconfig.tfm=net8.0`; SHA256 упакованных FormDesigner.dll и VsHost.dll
совпадает с финальной сборкой. В root VSIX нет Avalonia/Eremex/FormDesigner visual
assemblies. Для финальных VsHost process tests runtime извлекался из VSIX;
после проверки удалён только этот временный каталог. Доступен обычный
framework-dependent runtime, не self-contained publish; нужен .NET 8 runtime.
Установка и ручной click flow в Visual Studio здесь не заявляются.

## Changed Files

- `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs`
- `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Views/MainWindow.axaml.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlPhase4.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlPhase5.cs`
- `Samples/RoundTrip/Phase5/Items.axaml`
- `Samples/RoundTrip/Phase5/List.axaml`
- `Samples/RoundTrip/Phase5/Tree.axaml`
- `Samples/RoundTrip/Phase5/Progress.axaml`
- `AvaloniaDesigner.VSIX/AvaloniaDesignerVsixPackage.cs`
- `AvaloniaDesigner.VSIX/source.extension.vsixmanifest`
- `Docs/AxamlCoveragePhase5.md`
- `README.md`
