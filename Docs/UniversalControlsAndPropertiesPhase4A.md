# Universal Controls — Phase 4A: AXAML Import и source-preserving round-trip

Дата: 15.11.2026. Baseline: `39767631f806b4e13da533249606a5eb090b2910`, `main`, исходное дерево Git чистое. Продукт Beta 0.1; Avalonia 11.1.1, .NET 8, Windows 10 x64. Изучены отчёты Phase 1, 2, 3A, 3B и 3B.3. NuGet/TargetFramework, JSON schema, Toolbox, тема, VSIX/IPC и Export architecture не менялись. Коммит/tag/release не создавались.

## 1. Архитектура и исходные границы

```text
VSIX OpenInAvaloniaDesignerCommand / VsDocumentBuffer: текст и версия VS buffer
  -> VsHostBridgeClient -> NamedPipeProtocolConnection -> VsHostBridge.OpenDocument
  -> AxamlImportService.Import
     -> DesignerDocumentFormat.Validate -> AxamlSyntaxDocument.Parse (исходные spans)
     -> AxamlControlMetadata + AvaloniaMetadataCatalog / StandardControlProperties
     -> AxamlRoundTripPropertyMap -> capabilities / source references / snapshot values
     -> DesignerDocumentFileModel, ParentId / StackOrder
  -> MainWindowViewModel.LoadAxamlImportedDocument / DesignerDocumentSession
     -> существующий PropertyGrid / typed editors / Value Source / Reset
     -> AxamlLayoutProjection + AxamlPhase4Projection / DesignerSurface wrappers
  -> CreateActiveAxamlPatch -> AxamlPatchWriter.CreatePatch -> text edits
  -> VsHostBridge.ApplyAsync: ExpectedVersion + ExpectedChecksum
  -> VSIX VsTextPatch.Validate / VsDocumentBuffer.TryApplyPatch
  -> PatchApplied ACK -> MainWindowViewModel.MarkAxamlRoundTripSaved
```

Visual Studio владеет buffer и сохранением файла. Host возвращает edits, а не пишет файл поверх VS. Архитектура и протокол сохранены. Import не запускает пользовательские ViewModels, bindings, templates или сторонние constructors. Общая metadata перечисляет signatures и registrations только доверенных framework types; discovery не означает разрешение записи.

До этапа существовали ручные common model accessors, metadata literals девяти controls, специализированные Menu/Items/Expander adapters, shared ParentId projection, source capabilities и checksum guard. Большинство property elements были opaque. Четырёхсторонние Padding/CornerRadius старого Button не проходили double-only adapter. FontFamily/часть behavior properties отсутствовали в source policy. Исторический Button design preview использовал Border; это не проверка native Button API.

## 2. Реализованное расширение metadata

`AxamlControlMetadata.LiteralProperties` дополняет существующие специализированные accessors проверенной source policy. Тип, AP identity, OwnerType, inherited/attached, nullable, default, validation и editor получаются из прежнего `AvaloniaMetadataCatalog`. Список policy содержит имена, а не отдельные ручные parsers/setters каждого контрола:

* IsEnabled, IsHitTestVisible, Focusable, ClipToBounds;
* MinWidth/MinHeight/MaxWidth/MaxHeight;
* Padding, BorderThickness, CornerRadius;
* FontFamily, FontStyle, FontStretch;
* HorizontalContentAlignment, VerticalContentAlignment;
* DockPanel.Dock, только в соответствующем parent context.

Свойство допускается лишь при существующем writable AvaloniaProperty. Старые Width/Height/Margin/alignment/Grid placement/Content/Text/model adapters сохранены. Сложные ItemsSource/SelectedItem/ToolTip/Commands/Cursor/templates/resources не превращаются в scalar строки. Стандартный DataGrid не получает новый source descriptor автоматически.

Immutable literal/accessor maps кэшируются только для shipped стандартных типов. Они содержат schema и функции с аргументом model, не конкретные controls, sessions, source documents или RowVM. Plugin Type/Assembly в них не попадают; прежний ConditionalWeakTable lifecycle metadata engine сохранён.

## 3. Property elements

`AxamlPropertyElementInfo` — source metadata, не новый parser/Document Model. Хранятся owner/property, metadata, исходный element/span, возможный scalar text span, mode/reason и вложенный syntax. Используется существующий tokenizer.

| Конструкция | Результат |
|---|---|
| Button.FontFamily/Padding/CornerRadius, NumericUpDown.Value и другие reviewed scalar elements | Один plain text span, typed validation, edit, Reset и Undo/Redo |
| Button.Content со StackPanel/TextBlock | Существующий child-property adapter, ParentId и порядок; nested literal edits |
| Единственный Window.Content/UserControl.Content root | Импорт вложенного root без flattening или смены layout |
| Grid.RowDefinitions / ColumnDefinitions с definition objects | Читаются прежним adapter; property-element structure read-only |
| Control.ContextMenu | Owner/property распознаны; attachment subtree сохраняется, структурный authoring не включён |
| Binding/StaticResource/DynamicResource в scalar element | Исходное expression и Value Source отображаются read-only, не вычисляются |
| CDATA/comments/child objects/атрибуты внутри scalar element | Preserve-only; scalar editor не перезаписывает такой объект |
| Два значения, attribute + element, чужой owner/namespace | Неоднозначный mapping блокируется |

Scalar element допускается только при единственном mapping, правильном стандартном namespace/owner, reviewed metadata, непустом plain text и успешном codec. Существующие пробелы вне value span сохраняются. `xml:space`, CDATA, комментарии и пустой/self-closing value не канонизируются. Property elements attached owners вне base chain пока не получили scalar adapter.

## 4. Patch safety, Reset и Undo

Существующий attribute меняется только внутри ValueSpan с сохранением quotes; новый literal добавляется прежним writer. Для scalar property element меняется лишь text span с XML escaping. Reset удаляет ровно property element либо attribute; он не записывает registration default вместо unset. Undo возвращает модель и точный первоначальный source spelling, entities, prefixes, CRLF и соседние attributes/comments. Сохраняется синхронная доступность history commands из Phase 3B.3.

В Inspector используются прежние typed rows, категории/Favorites/поиск, revision guards и atomic transactions. Source metadata применяется к native controls общей функцией `ApplySourceScalars`; простые imported Button используют существующую native factory. Pending Reset Padding не устанавливает локальный default поверх style/inheritance. Designer input mask сохраняется отдельно от authored IsHitTestVisible/Focusable.

Подтверждённый P1: writer сравнивал только значение с snapshot и терял **присутствие** нового local value, равного registration default. Edit FontStyle Italic → Normal при отсутствии исходного attribute не записывал Normal; runtime продолжал использовать Style=Italic. Отдельная компиляция новых проверок против прежнего production DLL дала 36/38: этот случай и explicit nullable default не проходили. `AddsMetadataLocal` теперь отличает новый local entry от отсутствия значения; default literal записывается, Undo удаляет его. Null namespace guard учитывает также добавленный local null при null registration default. Проверка загружает настоящий Window и подтверждает FontStyle=Normal поверх Style.

После Save/ACK source baseline меняется. Undo удалённого scalar property element восстанавливает значение через безопасный новый attribute, если исходного span уже нет; точное старое представление element/quotes после такого rebase не обещается. Семантика decimal, сохранение комментария, Undo history и Redo Reset проверены отдельно. Полной регенерации нет.

Добавлены guards duplicate attribute aliases и markup после XML entity decoding: `&#123;Binding ...}` не получает права literal edit. AXAML string literal `(null)` не преобразуется в null marker Inspector: такой metadata string сохраняется read-only. Неподдерживаемый brush сохраняется read-only вместо exception при native creation. Binding/Resource/complex capability блокирует только соответствующее свойство; writer также отклоняет обход этого ограничения через model.

External checksum mismatch не создаёт edits. Version/checksum/ACK и VS buffer conflict protection не заменены silent overwrite. No-op import, selection и rebase не должны генерировать phantom changes.

## 5. Вложенные коллекции

Выбран bounded scope: **Menu/MenuItem, ComboBox/ComboBoxItem, TreeView/TreeViewItem, TabControl/TabItem**. Импорт, существующий ParentId/order, выбор nested item, простой Header/Content edit, минимальный patch, Undo/Redo и native projection проверяются отдельно. ComboBoxItem считается attached только после открытия настоящего popup; TabItem проверяется через template-owned native header/content.

Imported collections не переводятся в JSON collection DTO и не заменяются новым списком. Guard `CollectionReadOnlyReason` / `OpenCollectionEditor` из Phase 3B оставляет source Add/Remove/Reorder read-only. Для JSON тот же Collection Editor сохраняет atomic operations и экспорт. ContextMenu/Flyout attachments сохраняются, но их полное source hierarchy authoring не объявляется реализованным.

## 6. Coverage

Актуальный stress fixture — текущий `Views/MainWindow.axaml`. Старые приблизительные 1470/1473 относятся к другому состоянию самого файла. Одинаковый XML проверен старым кодом из `git archive` baseline и текущим кодом. Узлы, атрибуты, Inspector rows и безопасные patches считаются отдельно.

| Метрика | До | После |
|---|---:|---:|
| XML elements | 2537 | 2537 |
| Live visual candidates | 1505 | 1505 |
| Recognized/projected controls | 1502 | 1502 |
| Opaque visual controls | 3 | 3 |
| Атрибуты на 1502 projected controls, без xmlns | 4711 | 4711 |
| Recognized properties/identity directives по реальной API metadata | 4391 | 4391 |
| Source-patchable присутствующие attributes | 2795 | 2955 |
| Реально editable присутствующие attributes в PropertyGrid | 2020 | 2264 |
| Сохраняемые markup expressions на этих controls | 810 | 810 |
| Recognized Items owners (static/bound) | 80 | 80 |
| Structurally editable imported collections | 0 | 0 |

PropertyGrid audit действительно выбирает **каждый из 1502 controls** в прежнем VM и проверяет read-only состояние rows, затем проверяет отсутствие phantom patch. X/Y сопоставлены с Canvas.Left/Top; другие отсутствующие Inspector keys не считаются доступными. Source-patchable не означает наличие UI editor. Gap после этапа: **691** присутствующий source-patchable attribute без доступной строки Inspector, в основном Grid placement, definitions, StackPanel Orientation/Spacing. Он не скрыт ростом schema slots.

Таблицы по всем **19 source types fixture**: [до](AxamlImportPhase4A-before.csv), [после](AxamlImportPhase4A-after.csv). Они включают число instances/attributes, recognized, patchable, Inspector editable и отсутствующие UI keys. Это не список всех 140 discovered типов и не обещание их Toolbox support.

Все **62 property elements** fixture получили owner/property metadata; **0** из них являются новым editable scalar text element: в этой форме они содержат complex values/collections. Scalar element editing доказан отдельными fixtures, не выдуманным процентом MainWindow. **1034 opaque XML nodes** включают Styles/Setters/Templates и metadata subtrees; их нельзя путать с тремя opaque visual controls. Последние — собственные `views:DesignerToolbox`, `views:DesignerSurface`, `views:DesignerPropertyInspector`; их импорт не исполняет пользовательский код.

80 Items owners: ComboBox 48, TabControl 2, TreeView 3, ItemsControl 23, ListBox 4. Структурное редактирование всех этих source collections остаётся read-only. Binding/Resource expressions сохраняются, а не считаются unsupported control.

Общий `UniversalPropertyCoverage.csv`: 7785 type–property/alias pairs на 39 audit types; source adapters **869 → 1243**, JSON editable pairs **814 → 814**. Это статический policy inventory, не 1243 проверенных UI editors и не количество уникальных AvaloniaProperty registrations. Runtime capabilities по исходному значению/parent могут дополнительно ограничить запись.

## 7. Производительность

Первая реализация расширенных accessors дала реальную регрессию. При 3 warm-up + 30 измерениях MainWindow import median вырос 76.31 → 117.63 ms, patch 34.60 → 76.41 ms; patch allocations 27.08 → 94.11 MB. Причина по call path: `PropertiesFor` создавал новый массив accessors/closures при каждом чтении в import, snapshot, validation и patch. Metadata type discovery уже кэшировался.

Минимальное решение — immutable source accessor cache, без кэша пользовательских документов или результата patch. Дополнительно исключено двойное `p.Read` одного non-null literal и повторное форматирование registration default, уже хранящегося в immutable literal descriptor. Опциональные стадии существующего LayoutDropProfile: AXAML.SyntaxParse и AXAML.SourceMapProjection. Последняя включает создание model/capabilities/source references/snapshot values, а не только Dictionary.Add.

Release, Windows x64, .NET 8; baseline DLL из git archive исходного commit. Одинаковые XML и действия, 3 warm-up + **30** измерений каждого сценария; P95 nearest-rank, allocations — UI-thread managed bytes, MB десятичные. NativeCapture debugger присутствовал **в обоих** режимах; builds, SQL и другие native suites не выполнялись одновременно с этими замерами. Это не cold startup и не прогноз абсолютной скорости без debugger. Before игнорировал часть новых attributes; функциональный объём после больше, поэтому это стоимость расширения на одном XML, а не чистый A/B одной оптимизации.

| XML / операция | До median/P95, ms | После median/P95, ms | До/после median allocations, MB |
|---|---:|---:|---:|
| Small: 10 NumericUpDown, import | 1.34 / 1.37 | 1.15 / 2.92 | 1.118 / 0.802 |
| Small: patch Opacity | 1.94 / 2.44 | 1.54 / 1.67 | 1.203 / 0.628 |
| Medium: 100 NumericUpDown, import | 16.95 / 28.37 | 16.98 / 21.69 | 10.937 / 7.691 |
| Medium: patch Opacity | 31.26 / 34.52 | 26.81 / 29.55 | 11.813 / 5.999 |
| MainWindow: 1502 projected controls, import | 76.46 / 116.32 | 106.77 / 144.88 | 103.090 / 134.038 |
| MainWindow: patch Opacity | 26.26 / 34.75 | 55.27 / 65.23 | 27.076 / 51.318 |

**На большом MainWindow остаётся измеренная регрессия**, несмотря на локальные улучшения. Расширены policy/snapshot/validation обходы; writer по-прежнему проверяет всю source map, а не только изменённый control. Рост общего набора capability slots 26929 → 42638 сам по себе не доказывает единственную причину CPU/allocations. Typed property bag parsing и повторные checks — направления следующего профилирования, не доказанный GC root cause. Новый incremental patch engine в этом этапе не внедрялся.

| Сцена UI / операция | До median/P95, ms | После median/P95, ms | До/после allocations, MB |
|---|---:|---:|---:|
| 20 controls: selection + Inspector/layout | 17.66 / 89.83 | 17.48 / 91.90 | 3.225 / 1.989 |
| 20: edit Value | 9.46 / 42.82 | 9.61 / 44.63 | 1.445 / 1.472 |
| 20: active patch | 2.11 / 8.18 | 1.75 / 6.97 | 2.537 / 1.390 |
| 20: Undo | 43.86 / 205.33 | 44.18 / 214.44 | 7.781 / 5.426 |
| 20: Redo | 42.37 / 184.20 | 43.30 / 195.22 | 7.802 / 5.431 |
| 100: selection + Inspector/layout | 19.09 / 30.04 | 18.12 / 23.43 | 4.576 / 3.444 |
| 100: edit Value | 13.56 / 14.18 | 13.36 / 19.35 | 4.932 / 5.208 |
| 100: active patch | 12.64 / 14.08 | 8.12 / 15.25 | 12.541 / 6.780 |
| 100: Undo | 48.68 / 68.34 | 46.97 / 53.31 | 14.256 / 12.596 |
| 100: Redo | 48.26 / 134.07 | 50.13 / 68.49 | 14.260 / 12.610 |

UI timing — synchronous action + Window.UpdateLayout, без compositor/first physical paint. История — один pending change с Undo/Redo и проверкой возврата исходного текста, не H30. После каждого цикла проверяется no-op patch после Undo. Это другая сцена, чем PropertyGrid 3A 96.03/146.86 ms; прямое сравнение этих чисел недопустимо. P95 смешанный, устойчивое ускорение всех UI действий **не заявляется**.

После: SyntaxParse median/P95 0.032/0.037 ms small, 0.235/0.256 medium, 1.360/5.872 MainWindow. SourceMapProjection — 0.993/2.763, 15.258/20.053, 71.230/91.977 ms соответственно; ровно один вызов на import. Property edit/patch работают с уже созданной map; повторный parse нужен при новом source/ACK/rebase. Не внедрялась полная повторная генерация AXAML.

GC totals за 30 измеренных import+patch: small 4/1/0 → 2/1/1, medium 41/11/2 → 26/16/2, MainWindow 250/70/19 → 350/72/20. UI deltas и все raw samples сохранены в summary/artifacts; они не доказывают причинность P95. Managed/working-set medians после: small 15.89/135.58 MB, medium 17.23/144.70, MainWindow 36.61/195.43. Это snapshots без forced GC, не доказательство leak/retention.

## 8. История красных прогонов

* Диагностический harness сначала имел ошибки сборки и неправильный выбор profile target: source NumericUpDown имеет Group projection alias. Первый невалидный profile завершился managed exception; исправлен test target lookup по source reference. Это не исходный UIA AV.
* Неизвестный флаг старого runner ошибочно запустил default smoke workflow; запуск остановлен, собственный output перемещён в ignored artifacts. Он не учитывается в acceptance.
* Новая suite: первые 23/25, затем 25/26. ComboBoxItem искался при закрытом popup; проверка исправлена открытием popup и строгим GetVisualRoot assertion. Button не был native Button — добавлен bounded native adapter, а не ослаблена проверка типа.
* Затем 26/26, 29/29, 32/32 и 33/33 после Reset/ACK semantics. Model Preview checks сначала дали 34/35 и 33/35: Legacy не назначает Name, а Created diagnostic conflated property warning with control creation. Проверка привязана к настоящему native control в PreviewSurface и detailed diagnostics; partial source-property warning больше не означает отсутствие созданного control.
* Новый default/null regression до исправления writer — **36/38**, после — **38/38**.
* Первый normal Broad: **71/72**, без зафиксированного native AV. `AxamlCapabilityStylesAndBindings` ожидал неподдерживаемый Padding=1,2,3,4. Ожидание обновлено согласно новому typed adapter; отрицательные Binding/invalid Thickness и exact source assertions сохранены. Первый log/capture result сохранён отдельно; следующий normal Broad — 72/72.
* Первая compiled-source runtime fixture не собралась: prefix `a:` был задан, а default namespace для unprefixed Grid.Row отсутствовал. Исправлена **сама fixture**, не пользовательский source writer; повторный restore/build/run успешен.
* Финальный gate обнаружил **14/15** Phase 3B.3: дополнительная Brush.Parse проверка мешала metadata-owned nullable Reset BorderBrush. Она ограничена legacy adapters; metadata остаётся под своим codec. Обе suites после изменения: 15/15 и 38/38. Неуспешный gate остановлен до LocalDB, его logs не перезаписаны.

## 9. Ограничения и следующий этап

Imported AXAML остаётся частично редактируемым workflow. Не реализованы arbitrary styles/resources/template evaluation, binding authoring, полный root PropertyGrid, универсальное structural source collection editing, безопасное редактирование всех CLR properties и custom plugin controls. Legacy Grid.Row/Column fields остаются model/source-patchable, но их source Inspector UI не объявляется универсальным.

Не всякий design backdrop является настоящим source control. Полное pixel/semantic соответствие произвольного AXAML не гарантируется. AXAML Preview для JSON остаётся export-based default; imported workflow и его experimental limitations не переключались.

**Подтверждено отдельно:** диалог Legacy/AXAML Preview строится из упрощённой модели и не переносит source-only typed values во все legacy scalar fields. Для Button.Padding=1,2,3,4 в обоих режимах фактически получено 0,0,0,0. Это не считается visual consistency PASS. Добавлено агрегируемое `UnsupportedSourceProperty` предупреждение в существующую панель и `PREVIEW_SOURCE_PROPERTY_PARTIAL`; Button остаётся attached native control, файл не изменяется. Пропуски FontFamily/FontStyle/FontStretch/Padding/BorderThickness/CornerRadius без соответствующего JSON metadata adapter больше не скрываются. Копируются только keys/ids, не source map/session; новый словарь очищается при Close.

Отдельная проверка **patched исходного AXAML** использует прежний RuntimeAxamlPreviewLoader и compiled generated-project fixture: фактические native values, Menu/nested/Separator, Combo popup/selection, Tab content/switch, Tree hierarchy, resize 800×600/1200×800/1600×900 проходят. Fixture получает только технический x:Class существующего generated project. Это не доказательство готовности модельного Preview для произвольного imported AXAML с пользовательскими assemblies/VM. Для такого Preview сохраняется отдельное ограничение; Round-trip source не заменяется модельным export.

Native Windows UI Automation null-provider crash остаётся **UNRESOLVED**. Его отсутствие в новых прогонах не доказывает исправление. Production accessibility не отключена, защищённый full capture/evidence vault сохранён. Историческое client-dependent Window retention не приравнивается к crash или постоянной managed leak.

Для следующего отдельного этапа: source capabilities/UI coverage gaps, bounded structural insertion/removal с comment/namespace ownership, attachment slots ContextMenu/Flyout, safe definition-object patches и UX ограничений imported Preview. **Phase 4B автоматически не начата.**

## 10. Изменённые файлы

Production:

* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs` — reviewed source scalars, shipped-type/literal caches, общая native apply, immutable defaults.
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs` — root Content, scalar elements, source guards, accessor cache, opt-in stages.
* `DesignerSystem/AxamlRoundTrip/AxamlPropertyElementInfo.cs` — новый source-only metadata record.
* `DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs` — source property-element references/report, без JSON schema changes.
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs` — text-span edit/remove, local presence/default/null semantics.
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs` — source scalar apply и текущий Dock literal.
* `DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs` — bounded native leaf Button; unset не заменяется локальным default.
* `ViewModels/MainWindowViewModel.Metadata.cs` — source metadata в прежних typed rows.
* `ViewModels/MainWindowViewModel.PropertyMetadata.cs` — Value Source/Reset для scalar elements и readonly expressions.
* `ViewModels/MainWindowViewModel.cs` — исходное значение readonly property element в существующем capability adapter.
* `Views/MainWindow.axaml.cs` — вызов общей source scalar apply, без переписывания DesignerSurface.
* `Views/PreviewWindow.Consistency.cs` — source-only property gaps/агрегируемые warnings, корректный Created при partial property support.
* `Views/PreviewWindow.Lifecycle.cs` — очистка нового словаря value-only keys при Close.

Tests/docs:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` — новые entry points и IPC scenario.
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4A.cs` — 38 checks, profiles, inventory и generated fixture.
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4AInspectorAudit.cs` — фактический audit rows, включая source aliases.
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlCapabilities.cs` — новый typed Thickness вместо прежнего unsupported ожидания; отрицательные guards сохранены.
* `smoke-tests/UniversalMetadata/Phase4ARuntimeProbe.cs.txt`, `README.md` — native compiled runtime и команды воспроизведения.
* `Docs/UniversalPropertyCoverage.csv`, `AxamlImportPhase4A-before.csv`, `AxamlImportPhase4A-after.csv`, этот отчёт — фактическое покрытие и ограничения.

Private evidence находится в ignored `artifacts/universal-phase4a`: baseline archive, raw profiles/audits, summary, первые failed runs, capture manifests, final validation и generated fixtures. Бинарные dumps не добавляются в Git. Нет изменений NuGet/TFM/VSIX protocol/Document Model/Toolbox/global theme и пользовательских settings.

## 11. Ручная приёмка и оставшиеся риски

1. Установленное расширение в настоящем devenv.exe: открытие/переключение/закрытие документов, caret/text buffer, Apply/ACK, конфликт с ручным изменением, source Undo после Save. Автоматический IPC не считается такой ручной проверкой.
2. Source-проекты с пользовательским code-behind, plugin assemblies, сложными templates/resources и prefix-qualified attached attributes. Неизвестное сохраняется, но визуальная/редакторская поддержка не полная.
3. Реальные DPI/multi-monitor, другие Windows builds, screen readers/внешние UIA clients. UIA null-provider и client-dependent native retention не объявляются устранёнными.
4. PostgreSQL live — отдельная неподтверждённая интеграция без предоставленного сервера; сервер не устанавливался.
5. Большие AXAML: остаётся измеренная P2-регрессия CPU/allocations; следующий отдельный profiling должен отличить стоимость новых capabilities от лишнего повторного scalar reading/validation.
6. Модельный Preview/Export импортированного документа не равен исходному AXAML. Для source-only properties показывается предупреждение; сохранённый source следует запускать в исходном приложении. JSON Preview/Export сохраняют прежний workflow.

## 12. Build, regression и runtime results

| Проверка | Результат |
|---|---|
| Solution Release build / VSIX packaging | PASS, 0 errors; прежний NU1603 VS SDK Analyzers 17.7.20 → available 17.7.22 |
| Новый Phase 4A suite | **38/38**, native values/visual trees, readonly expressions, minimal patches, default/null, Reset/ACK/history, memory |
| PropertyGrid 3A | **30/30**, финальный повтор |
| Collections 3B | **30/30**, финальный повтор |
| Metadata | **45/45**, финальный повтор |
| Universal Controls Phase 2 | **40/40**, финальный повтор |
| Reset/Undo Phase 3B.3 | **15/15**, после исправления nullable Brush gate |
| Broad normal environment | **72/72**, последний полный прогон; первоначальные 71/72 сохранены |
| Новый property-element IPC scenario | **1/1**; три последовательных изменения, version/checksum/Apply/ACK/rebase |
| Stabilization / ordinary closed Window control | **25/25**; 6 ordinary closed Windows, alive=0 в этом запуске |
| Nested Layout / Pointer / Grid Drop / Grid Editor / Layout7A | **59/59, 26/26, 40/40, 45/45, 15/15** в матрице Phase 4A |
| SQL Server LocalDB | **47/47**, CONTRACT/LIVE labeled; source workflow и отдельный generated runtime PASS |
| Preview Memory с LocalDB | **36/36**, повтор после Preview diagnostics changes |
| PostgreSQL offline/contract | **20/20**; live SKIP, без сервера |
| Eremex / plugins / VS host | Existing isolation/vertical slices и VsHost/IPC в broad/core PASS; PostgreSQL Eremex live SKIP |
| Generated collection Grid / Canvas | restore/build/run PASS отдельными процессами; native collections, selection, popups и resize |
| Compiled patched-source fixture | restore/build/run PASS; свойства, вложенная иерархия, интерактивность и 3 resize |

Финальный supported-environment gate: `artifacts/universal-phase4a/final-validation2`, итог **FINAL_ACCEPTANCE_PASS**. Сборка: `build-accepted.log`, `smoke-accepted-build.log`; новые checks — `accepted-universal4a-checks.log`. Результаты model Preview ограничения **не** включены как «совпадение Padding»: тесты подтверждают warning и присутствие native Button, а фактическое расхождение записано в разделе 9.

SQL использовал отдельные GUID LocalDB instances; последний `UniversalMetadata_e412b5982aa845b1a0dd2f64c1e3775c`, собственные MDF/LDF в artifacts. Customers=3, Orders=2, реальные видимые cells, независимые формы/VM и runtime resize проверены. Instance остановлен/удалён в finally; пользовательские базы не затрагивались. Ошибка подключения не подменялась demo rows. PostgreSQL live не проверен.

Preview memory включает 20 Legacy + 20 AXAML cycles, Multi Form, DataContext/ItemsSource, animation clock, cancelled close и Close during SQL. Для TwentyCycles в обоих режимах aliveWindows=0, activeWindows=0, animation clock до/после=0. Source replacement WeakReference test проходит при остающемся живым VM; source metadata cache не удерживает старый документ/model. PropertyGrid/Collection editor lifecycle regression сохранён. `GC.Collect` используется только существующими test helpers; production GC не добавлялся.

В завершённых captured процессах Phase 4A **не записано новых AV**, capture failures или timeouts; исходный UIA crash остаётся UNRESOLVED. Обычный Window control без внешнего client не отменяет историческое client-dependent retention. Full source UIA stress/research заново не проводился, отключений accessibility/COM-provider workaround нет.

В final broad есть прежний cleanup warning: loaded `ToolboxStandardItemsExport.dll` не удалён автоматическим cleanup успешного собственного output. Assertions 72/72 завершились, process exit=0; warning не скрыт и принудительная очистка не выполнялась. Generated templates остаются net6.0 и дают прежний **NETSDK1138**; TargetFramework в этой фазе не менялся. Интерактивный установленный VSIX и другие платформы/DPI не считаются проверенными автоматическими smoke tests.

Sandbox IPC 69/72 / normal 72/72 — прежнее ограничение среды из Phase 3B.2–3B.3; оно не выдаётся за новый результат этой фазы. Новые IPC и финальные broad/SQL выполнялись с normal permissions, production IPC не изменялся ради sandbox. Первые source-assertion и harness failures сохранены в разделе 8; успешный повтор не выдаётся за полностью зелёный первый прогон.

## 13. Итоговая оценка

**AXAML ROUND-TRIP: READY WITH CONDITIONS.**

Для reviewed стандартных scalar properties и выбранных nested structures расширены реальные Inspector capabilities; minimal source edit/reset/history/ACK/default/null semantics проверены, Binding/Resource/unknown source сохраняются. Новый P1 потери explicit local default исправлен с red-before и native runtime evidence. Подтверждённых незакрытых P0/P1 round-trip в проверенном subset не осталось.

Условия: structural source collections и часть Inspector keys остаются read-only/не выставлены; модельный imported Preview/Export не воспроизводит все source properties и теперь предупреждает об этом; для большого MainWindow остаётся P2-регрессия CPU/allocations; UIA crash и client-dependent retention остаются отдельным native risk; установленный VSIX/DPI/live PostgreSQL требуют своей приёмки. Это **не** оценка полной поддержки произвольного Avalonia AXAML или готовности его Preview.

После этого этапа работа остановлена. **Phase 4B не начата.**
