# Phase 4C — Structural AXAML Editing

Дата: 16.11.2026. Baseline: `d4621b8`, `main`, исходное дерево Git чистое; изменения Phase 5E уже закоммичены пользователем. Avalonia 11.1.1, Designer net8.0, Windows x64. Версии NuGet, TargetFramework, глобальная тема, JSON contract и IPC protocol не меняются. Новые Toolbox types не регистрируются.

## 1. Архитектура

Используются прежние `AxamlSyntaxDocument`, `AxamlImportService`, metadata, source references, `AxamlPatchWriter`, `DesignerDocumentSession` и Inspector. Новый partial writer планирует операции над исходными spans; это не второй parser и не сериализация импортированного документа.

Designer хранит отдельно последний подтверждённый buffer baseline и рабочую проекцию текущих edits. После структурной операции writer строит минимальные text edits относительно baseline; importer обновляет рабочие source map/capabilities с сохранением ControlId. Visual Studio продолжает владеть text buffer. До ACK рабочая проекция не выдаётся за сохранённый исходник.

Новые элементы проходят существующий descriptor exporter. Для source fragments у того же exporter включается scoped режим: literal brushes вместо ресурсов генерируемого проекта и настоящий layout root вместо legacy decoration wrapper. Настройки пользователя/тема не переключаются. Canvas использует существующий Group descriptor, без дублирующей регистрации.

## 2. Capabilities и ограничения безопасности

`AxamlStructuralCapability` описывает CanAddChild, CanRemove, CanReorder, CanReparent, CanEditContent, CanEditCollection, CanInsertSibling, CanModifyLayout и reason. Это контекстная policy над теми же syntax/map/metadata. Распознавание XML само по себе не даёт права изменения.

Поддерживаемые parents: Grid, StackPanel, Canvas, DockPanel, WrapPanel; также bounded static ComboBox/ListBox Items. Неоднозначный Content/Children, bound ItemsSource/SelectedItem/SelectedValue, Items templates, текст/CDATA/processing instructions в child host, неизвестные visual siblings для вставки/reorder блокируются. Exact-span extraction/deletion известного узла может сохранить opaque соседей. Удаление содержащего opaque subtree узла остаётся ограничено.

Структурное изменение внутри Styles/Resources/ControlTemplate/DataTemplate не включено: эти узлы не превращаются в обычные controls. Raw XML существующего control переносится целиком, включая неизвестные атрибуты и вложенные property elements. При смене namespace scope, xml:space, унаследованных XAML DataType/CompileBindings (включая aliases) или непреобразуемого attached expression операция отклоняется. Unknown атрибут с похожим именем, например u:Grid.Row из стороннего namespace, не считается Avalonia attached property и сохраняется.

Существующие clipboard duplicate/paste/group/ungroup для imported source ограничены, чтобы JSON-клон не терял opaque AXAML. JSON Designer сохраняет прежнее поведение.

## 3. Поддерживаемые операции

| Операция | Реализованная граница |
|---|---|
| Insert | Button, TextBox, TextBlock, CheckBox, Border, StackPanel, Grid, Canvas; existing descriptors |
| Remove | Известный control/subtree; сохранение внешних comments и соседей; opaque subtree guard |
| Reorder | Source order поддерживаемых siblings, включая Grid/Canvas Z-order по порядку; explicit ZIndex не переписывается |
| Move/Reparent | Между поддерживаемыми panels; циклы/invalid ParentId/namespace conflicts блокируются |
| Grid placement | Реальные native measured tracks, Row/Column, spans с ограничением по target grid; definitions не перестраиваются |
| Canvas placement | Local Left/Top относительно фактического source layout; порядок siblings |
| StackPanel insertion | Orientation и native child bounds для insertion index |
| DockPanel | Child order, сохранение literal Dock edge при переносе между DockPanel; новый edge по умолчанию Left |
| WrapPanel | Native layout остаётся у Avalonia; visual drop добавляет в конец, reorder API/команды меняют source order |

Border добавляется как пустой native control; произвольное structural Content editing в Border/Window/UserControl пока не включено. Новые контейнеры можно заполнять до первого ACK. Root Canvas без отдельного model ID обслуживается существующей virtual surface policy.

## 4. Source-preserving edits

Writer сопоставляет прежние mapped child slots с текущим ParentId/order. При изменении slot переносит исходный XML узла; неизменённые ветки не регенерируются. Scalar edits внутри перенесённого subtree применяются к его raw fragment. Переставляются только необходимые element spans, не весь Window.

Внешние comments не принадлежат control и остаются в исходных slots; внутренние comments едут вместе с узлом. Whitespace за пределами edit spans, quotes, namespace prefixes, Bindings, Resources и unknown attributes сохраняются. Для нового descriptor fragment используется newline/indent unit окружения; существующий raw fragment не переформатируется.

Пересекающиеся ranges, неоднозначные identities, циклы, некорректный XML и потеря ParentId при повторном импорте отклоняются до изменения live модели. Итог проверяется прежней validation и syntax parsing. Unsafe результат не отправляется в buffer.

## 5. Undo/Redo и ACK

Операция предварительно проверяется на candidate DTO, затем изменяет только затронутые live controls в существующем undo batch. Новый JSON format или второй history engine не вводится. Selection-only/no-op reorder не создают структурных изменений. Явные metadata literals нового descriptor fragment переносятся в прежние CustomProperties до commit. Это предотвращает ошибочную интерпретацию CornerRadius/Padding/BorderThickness как Reset при следующем patch; отсутствующие attributes не превращаются в локальные defaults. Новые модели получают UsesSourceLayout=true.

JSON history восстанавливает model; source writer восстанавливает соответствующий XML. До ACK проверяется точное восстановление текста. После ACK документный архив raw source references позволяет вернуть удалённый control с bindings/неизвестными attributes и прежним ControlId; whitespace вокруг повторной вставки может следовать текущему стилю.

Archive принадлежит документу и содержит strings/syntax/source-map references, а не live controls, VM или plugin types. Первый pending fragment сохраняется неизменным: более поздний edit не меняет исходную базу ранее отправленного ACK. ACK сверяется с acknowledged designer snapshot, а не с текущим более новым состоянием; последующие edits остаются dirty. Failed reload не сбрасывает текущий structural baseline.

## 6. Source map и идентичность

Patch result включает внутренние offsets control starts. При переносе/вставке они пересчитываются вместе с text edits и используются существующим importer через path identity map. Учитываются named и unnamed controls. Source ParentId и множество ControlId проверяются на соответствие candidate.

Для первого корректного пути после structural action выполняется полный безопасный re-import рабочей проекции. Это измеряемая стоимость, не заявленный incremental source map. Lookup source children индексируется по ParentId внутри одного patch; глобального кэша документов не добавлено.

## 7. DesignerSurface, Explorer и PropertyGrid

Сохраняются текущие DesignerSurface/wrappers, Explorer и PropertyGrid. После commit дерево обновляется по live ParentId; выбранный control разрешается по стабильному Id. Source metadata нового родителя становится доступна сразу, до ACK.

Routed Toolbox drop использует существующий UI путь. Реальный pointer drag проверяется отдельно от прямого вызова VM. Подтверждённая ошибка hit-test: импортированный контейнер искался по authored/default model sizes, хотя отображался по native bounds. Теперь выбор parent использует те же измеренные bounds. Source drag preview меняет только visual position до release; Escape/capture loss не должны записывать Canvas literals.

Multi-selection reparent, clipboard cloning и общий Content/template authoring не включены. PropertyGrid и глобальная тема не переписаны.

## 8. Collection Editor

Выбраны две структуры: статические ComboBoxItem и ListBoxItem. Existing CollectionEditorSession читает source hierarchy вместо JSON collection DTO storage и выполняет Add/Remove/Reorder через общий structural transaction. Source Items не подменяются сгенерированной коллекцией. Новое имя учитывает также opaque XML, чтобы не создать namescope collision.

ItemsSource/сложные templates/неоднозначное содержимое остаются read-only. Встроенные поля item editor для imported Items показаны read-only с объяснением; простые свойства можно редактировать, выбрав сам source item в Explorer/обычном PropertyGrid. JSON Collection Editor и его специализированные editors сохранены.

## 9. VSIX/VsHost/IPC

Прежний explicit Apply/Save отправляет edits через Named Pipes. ExpectedVersion/checksum, `VsTextPatch.Validate`, ACK и conflict handling сохраняются; wire protocol не расширен. Avalonia/Eremex assemblies в devenv.exe не переносятся.

Новый IPC scenario выполняет Insert/Reorder/Reparent/Remove/Undo/Redo с последовательными ACK, затем конфликт с manual buffer edit. Клиент имитирует VS buffer transport и использует настоящий span validator; это не ручной installed-VSIX acceptance.

Отдельная normal IPC проверка до финального runtime applicability fix — **2/2**: прежний scalar property-element scenario и новый StructuralIpc. Новый scenario применил шесть последовательных операций/ACK через настоящий Named Pipe и VsTextPatch validator, сохранил unknown attribute/Binding/comment, проверил отсутствие phantom patch после ACK и отказ при stale checksum. Финальный контроль того же transport после applicability fix приведён в таблице regression.

## 10. Preview и generated runtime

Рабочая source map используется прежней безопасной native projection. После structural operation проверяются фактические native types, hierarchy, Grid/Canvas placement и static Items. Bindings/Resources/Templates не исполняются во время анализа; limitations imported Preview остаются прежними.

После исправления первый новый valid generated output: `artifacts/universal-phase4c/generated-fixed/project`. Исходный patched AXAML передан компилятору generated project; его restore/build и отдельный native process завершились успешно. Probe подтвердил **20 native nodes**, все восемь inserted types, source ParentId и фактический logical child order, Canvas.Left=30/Top=40, неизменный Tag, отсутствие Deleted, ComboBox/ListBox по три items, реализованные элементы ComboBox popup и native selection. Probe удалён, App startup восстановлен, чистый проект повторно собран. Финальный scoped probe также подтвердил восемь emitted CornerRadius/Padding/BorderThickness literals на Button/TextBox/Border после следующих структурных операций; проверялись только attributes, действительно emitted соответствующим descriptor.

Generated template сохраняет прежний net6.0 и выдаёт NETSDK1138; TargetFramework по условиям фазы не обновлялся. Успех этого доверенного fixture не означает исполнение или полное визуальное соответствие произвольных пользовательских AXAML/resources.

Подтверждён отдельный root cause ложной property capability: старый Common список AXAML accessors содержал BorderThickness/CornerRadius/Padding у panels и FontSize/FontWeight у Border. Designer native projection игнорировала неприменимые свойства, но последующие structural patches могли добавлять их из legacy defaults в source. Первая compiled runtime проверка получила восемь AVLN2000. Общий существующий BuildPropertiesFor теперь фильтрует такие accessors по реальному NativeType через AvaloniaMetadataCatalog, сохраняя применимые inherited/attached/CLR properties. Не создаётся отдельный whitelist для fragments. Новый regression NativePropertyApplicability проверяет отсутствие фиктивной редактируемости и атрибутов.

## 11. Performance

Методика: Release, один UI process через NativeCapture, без параллельных UI suites/build/SQL во время benchmark; 5 warm-up + 30 measurements. Small/Medium — доверенные fixtures, Large — текущий Views/MainWindow.axaml. Duration и UI-thread allocations, process-wide GC, peak working set и nested stage counters сохраняются раздельно. Peak working set — high-water mark всего процесса, а не отдельной операции; nested stages не суммируются как независимые интервалы. P95 — nearest-rank для 30 samples. Synchronous preview creation не является измерением физического first frame. PreviewRefresh включает существующий GUI generation-failure/fallback и UI helper settle (минимум около 140 мс); отдельно приводится native projection stage. Для Undo/Redo фиксированные 160 мс до command и 100 мс после перенесены за границу timer; измеряется синхронный command, а не физический render всей DesignerSurface.

Измеряются Insert, Reorder, Reparent, RemoveSubtree, Undo, Redo, Patch, PreviewRefresh; `AXAML.StructuralSourceMapUpdate` выделяет import/map работу внутри transactions. Исторические Phase 4B 49,45 ms/35,30 MB относятся к чистому import другого объёма capabilities и не подменяют стоимость полного нового structural action.

Финальный raw: `artifacts/universal-phase4c/core-performance/structural.json`; **720 observations, 24 groups, 30 на группу**, 5 warm-up на сцену. У каждой операции одинаковая последовательность состояния, отдельного ACK между ними в benchmark нет. Small — 4 Button + Grid/StackPanel; Medium — 100 Button + Grid/StackPanel. Large — 483 577 UTF-8 bytes, MainWindow SHA-256 `06DDDF98789FD874FBB8C871022E49312DD5C1A74987C6618FCFBE22A0F17697`, побайтно тот же fixture Phase 4B.

**Small**

| Операция | Median, ms | P95, ms | UI allocations, MB | SD, ms | GC 0/1/2, сумма 30 |
|---|---:|---:|---:|---:|---|
| Insert | 2.80 | 6.01 | 1.75 | 1.04 | 3/3/0 |
| Reorder | 1.33 | 5.49 | 0.71 | 1.19 | 4/1/1 |
| Reparent | 2.90 | 6.78 | 1.95 | 1.03 | 2/2/0 |
| RemoveSubtree | 1.07 | 1.96 | 0.63 | 0.79 | 1/1/0 |
| Undo | 3.03 | 3.60 | 0.73 | 0.62 | 1/1/0 |
| Redo | 2.28 | 2.71 | 0.53 | 1.17 | 2/2/1 |
| Patch | 0.42 | 0.51 | 0.14 | 0.03 | 0/0/0 |
| PreviewRefresh | 339.78 | 348.66 | 4.21 | 9.70 | 8/3/2 |

**Medium**

| Операция | Median, ms | P95, ms | UI allocations, MB | SD, ms | GC 0/1/2, сумма 30 |
|---|---:|---:|---:|---:|---|
| Insert | 35.03 | 43.39 | 18.43 | 3.84 | 35/27/5 |
| Reorder | 19.48 | 30.52 | 12.75 | 5.64 | 21/16/4 |
| Reparent | 25.24 | 34.16 | 18.86 | 4.04 | 33/8/3 |
| RemoveSubtree | 13.44 | 19.13 | 11.61 | 3.09 | 17/5/1 |
| Undo | 16.43 | 19.53 | 9.16 | 1.41 | 18/1/0 |
| Redo | 14.39 | 28.73 | 9.76 | 4.77 | 20/12/4 |
| Patch | 4.46 | 6.74 | 5.22 | 1.02 | 10/0/0 |
| PreviewRefresh | 375.48 | 393.82 | 62.38 | 10.33 | 53/39/30 |

**Large**

| Операция | Median, ms | P95, ms | UI allocations, MB | SD, ms | GC 0/1/2, сумма 30 |
|---|---:|---:|---:|---:|---|
| Insert | 547.63 | 630.67 | 273.50 | 33.49 | 382/216/45 |
| Reorder | 305.43 | 359.19 | 220.34 | 23.10 | 212/102/9 |
| Reparent | 496.00 | 571.84 | 261.41 | 41.92 | 350/190/27 |
| RemoveSubtree | 304.54 | 351.67 | 201.51 | 28.19 | 218/105/15 |
| Undo | 342.44 | 385.71 | 188.35 | 36.09 | 270/132/26 |
| Redo | 275.23 | 360.46 | 186.89 | 33.67 | 236/91/20 |
| Patch | 77.03 | 128.15 | 74.41 | 16.62 | 72/43/13 |
| PreviewRefresh | 1181.72 | 1283.71 | 1042.82 | 57.48 | 387/193/63 |

Peak process working set: **1280.9 MB**. Это lifetime high-water, не retained heap и не расход конкретной операции. CPU/allocations всех render threads отдельно не измерялись.

**Стадии Large Insert (вложенные интервалы):**

| Стадия | Median, ms | UI allocations, MB | Calls |
|---|---:|---:|---:|
| CreateUndoSnapshot | 128.29 | 82.26 | 2 |
| AXAML.StructuralPatch | 91.41 | 39.43 | 1 |
| AXAML.StructuralSourceMapUpdate | 86.25 | 60.34 | 1 |
| CommitUndoBatch | 64.09 | 36.60 | 1 |
| AXAML.SourceMapProjection | 63.97 | 42.09 | 1 |
| InspectorMetadataLookup | 0.27 | 0.32 | 53 |

Два вызова CreateUndoSnapshot — сериализация состояния до/после одного transaction; это **одна undo entry**, что отдельно проверяется functional tests. StructuralSourceMapUpdate — один вызов на Insert, а не повторный reflection discovery. ApplyDocument в обычный structural commit не добавлен; history restore сохраняет прежний ApplyDocument path.

Главные наблюдаемые затраты: whole-document DTO/history snapshots, полный source patch planning и re-import/map validation. Metadata lookup Inspector занимает малую часть времени. Большой Preview дополнительно проходит существующую неудачную попытку model export до native source projection; поэтому его общий расход превышает чистую native projection stage. Fallback не скрывается: 35 Large opens (warm-up + measured) отражены в логе.

До Phase 4C таких structural transactions не было, поэтому **парный Before/After ускорения не заявляется**. Phase 4B 49,45 ms/35,30 MB — чистый import, 192,73 ms — native Measure/Arrange; это иные границы измерения. Ранее сохранённые workflow samples с UI settle внутри Undo не сравниваются с финальным command timing как production speedup.

Ограничение P2: Large Insert/Reparent занимают около половины секунды; Undo/Redo и allocators существенно дороже Small/Medium. Preview helper около 1,2 s и 1 GB временных UI allocations; native stage не равна этому полному пути. Сохранность XML выбрана раньше incremental update. Нужны последующие адресные работы над snapshots/source-map/ненужной model-export подготовкой Preview.

Полные данные: `Docs/AxamlStructuralPhase4CPerformance.csv`, `Docs/AxamlStructuralPhase4CStages.csv`. Отдельный native pointer/routed drop проверяет настоящий UI путь; API timings не выдаются за latency drag-to-first-frame полной DesignerSurface.

## 12. Memory и UI Automation

Документный archive освобождается при закрытии/смене AXAML context. WeakReference test держит VM живой, но требует освобождения прежнего structural buffer после New Project. Preview lifetime дополнительно проверяется прежней полной матрицей.

Длительная undo history и множество pending новых узлов могут сохранять несколько source strings, AST и reference maps: это стоимость точного source recovery. Глобального strong cache источников/плагинов нет; постоянный bounded retention/pruning history в этой фазе не переписывался.

UIA native crash `ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Accessibility не отключается; прежний process-local full capture/evidence vault используется без изменения глобального WER. Ноль новых crash не означает исправление старого null provider.

Новый **DocumentArchiveReleased PASS**: прежний buffer/archive собирается GC после New Project при ещё живой VM. Phase 4B source-map release checks и Preview Memory **36/36** прошли. Утверждение ограничено проверенными lifecycle scenarios; длительная pending/history нагрузка остаётся P2 из-за хранения исходников и maps.

Финальный evidence audit: **93 process-local NativeCapture runs, 0 captured AV, 0 capture failures, 0 timeouts** (`artifacts/universal-phase4c/final-evidence-audit.json`). Это число процессов разных проверок, а не 93 одинаковых stress iterations. В запусках фазы новых native AV не зафиксировано. Managed assertions/ошибки compilation/CLR exit benchmark учтены отдельно. Отсутствие AV не устанавливает причину старого UIA null provider и не закрывает ordinary Window retention.

## 13. Build и regression

Solution Release: **PASS, 0 ошибок**, прежний NU1603 у Microsoft.VisualStudio.SDK.Analyzers. Runner Release: PASS, 0 ошибок/предупреждений. Generated structural/SQL проекты собирались и запускались отдельными процессами; прежний generated net6.0 выдаёт NETSDK1138.

| Проверка | Последний результат | Evidence в artifacts/universal-phase4c/ |
|---|---:|---|
| Phase 4C | **85/85** | accepted-source/universal4c-checks.log |
| Phase 4A / 4B | **38/38; 52/52** | accepted-source/ |
| Phase 5E | **58/58** | delivery-validation/universal5e-checks.log |
| PropertyGrid / Collections | **30/30; 30/30** | delivery-validation/ |
| Layout7 / Metadata | **15/15; 45/45** | delivery-validation/ |
| Native pointer / routed GridDrop | **26/26; 40/40** | delivery-validation/; accepted-source/ |
| Broad, normal Windows environment | **72/72** | accepted-normal/broad-normal.log |
| Отдельный Named Pipes IPC | **2/2**, включая StructuralIpc | accepted-normal/ipc.log |
| SQL Server LocalDB, CONTRACT+LIVE | **47/47** | accepted-sql/live-datagrid-preview-checks.log |
| SQL profile/schema/BindingSource workflow | **PASS**, оба Preview | accepted-sql/live-datagrid-preview-source-check.log |
| Preview Memory с live SQL | **36/36** | accepted-sql/live-preview-memory-checks.log |
| Generated SQL runtime | **PASS**, Customers=3, Orders=2 | accepted-sql/live-datagrid-preview-export-check.log |
| Generated structural runtime | **PASS**, 20 source nodes, 8 native literal checks | accepted-generated/structural-scoped-runtime.txt; structural-runtime-expectations.json |

AXAML source tests проверяют реальный text edit application, spans/source identities, reset/scalar composition, exact pre-ACK Undo/Redo, late ACK, document switch и rejection без изменения source. JSON/Multi Form/Grid/Canvas/Export/Eremex/VSIX isolation проверяются существующими regression suites. Live PostgreSQL не выполнялся: отдельный server не настроен; offline/provider contracts сохранены. Ручной installed-VSIX тест не выдаётся за выполненный.

## 14. Первые красные результаты

Все evidence находятся вне Git в `artifacts/universal-phase4c/`; filtered diagnostic runs не заменяют full suite.

* Первая компиляция: local `source` shadowing в новом guard; исправлено имя. Отдельная ошибка collection adapter: выбран LiteralProperty вместо прежнего round-trip property accessor; исправлен вызов существующего adapter.
* Первый functional run **32/58**: theme resource references и legacy container wrappers в descriptor fragments. Исправлен ограниченный source-fragment режим существующего exporter.
* Следующий expanded run **55/62**, затем **60/62**: Canvas использовал неверный registry alias; collection DTO snapshot не содержал ParentId. Использованы Group descriptor и существующий полный ToControlFileModel.
* **61/62**: ChildLayoutMode ListBoxItem не соответствовал source metadata; исправлен source-default нового item.
* **65/66** и отдельный pointer red: настоящий drag/capture начинался, но выбирался parent по неверной геометрии. Сохранён pointer trace, исправлен imported hit-test, отдельный native pointer check стал PASS.
* Первый full regression: **68/70**, Phase4A **37/38**, Phase4B **50/52**. Grid fixture передавал local coordinates API absolute coordinates; no-op dirty test теперь сравнивает исходное состояние. Старые assertions read-only ComboBox и отсутствующего pending source reference заменены проверками нового, явно ограниченного capability/working-map контракта. Проверки native state и сохранности patch сохранены.

* Первый GridDrop: **39/40** — старый запрет source Grid Drop; assertion заменён routed drop + проверкой нового native/source control.
* Первый normal IPC: **1/2** — новый элемент уже был первым, поэтому reorder в 0 корректно оказался no-op. Fixture теперь явно вставляет в конец; production no-op guard сохранён.
* Первый normal Broad: **70/72**. Старое assertion запрещало StackPanel reorder; теперь проверяется фактический AXAML order. StartupNoWorkspace получил null native hit target: production startup не менялся, test harness теперь один раз ожидает compositor commit вместо предположения о готовности frame после фиксированного ожидания. Первичный failure сохранён; это не доказанное исправление production startup или UIA.
* Normal Broad после этих конкретных изменений: **72/72**. Existing cleanup сообщил UnauthorizedAccessException для собственной generated DLL после успешных assertions; process exit 0, crash capture false. Исходные материалы/пользовательские файлы не удалялись.
* Public edit composition дополнительно проверяется реальным `VsTextPatch.Validate`: scalar insert и раскрытие self-closing host с общим offset объединяются в один transport edit. Checks сохранности XML проверяют также CRLF/tabs/quotes, attached Binding и неизвестный attribute с похожим attached именем.

* Расширенная матрица сначала дала **79/81**: fixtures предполагали materialized Item при ItemsSource и отдельный ControlModel корневого Canvas. Это противоречило прежнему importer contract. Проверки теперь требуют opaque bound Items и используют вложенный Canvas во втором документе; после исправления fixtures и дополнения safety cases — **83/83**.
* Первый compiled structural project не собрался: восемь AVLN2000 из ложных Common property accessors, описанных выше. Лог и неуспешный generated output сохранены. API source insertion itself ранее проходила checks; именно compilation выявила отсутствовавшую end-to-end проверку. Для native Canvas placement runtime fixture также исправлен ввод: API принимает absolute designer coordinates, поэтому к 30/40 добавляется фактический origin target Canvas.

* Первые profile attempts: после Small/Medium первый process завершился с CLR exit `0xE0434352`, без AV/timeout и без managed stack. Следующий диагностический запуск вернул exit 1 и сохранил managed stack/480 partial samples: helper напрямую вызывал JSON-export preview, не воспроизводя существующий GUI catch/fallback. На Large такой model export отклоняется из-за TextBox-конфигурации. Helper теперь повторяет GUI generation-failure path и затем подключает прежнюю imported native projection. Fallback явно логируется; export не повторяется. Production Preview/Export для этого не переписывались.
* Первый полный валидный workflow benchmark до последнего literal fix сохранил 720 samples/24 groups в `performance-final/`; он не заменяется финальным замером.
* Проверка generated AXAML обнаружила потерю явных literals новых controls: legacy DTO не содержал их metadata Local storage, а writer трактовал отсутствие как Reset. Исправлена инициализация через прежние accessors/codecs; новый `PendingNativeDefaultsStable` проверяет сохранение значений между операциями. Первый расширенный run **84/85** неверно требовал CornerRadius от TextBox descriptor, который его не экспортирует. Fixture теперь проверяет реально emitted Padding TextBox и CornerRadius Button/Border; текущий результат **85/85**.
* Расширенный runtime-probe сначала использовал flat visual search и встретил два Grid с именем Root: исходный Grid и template Grid ScrollBar внутри TextBox. XML и manifest не имели duplicate names. Диагностика сохранила обе ancestry chains; lookup заменён на Window XAML NameScope с проверкой membership/type, parents/order и свойств, без выбора первого совпадения.

* Первые полные workflow samples включали 260 мс fixed UI settle из общего GridEditorUndo helper. Они сохранены отдельно; финальная методика выносит settle за границу timer Undo/Redo. Разница результатов не называется оптимизацией production.

## 15. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs`
* `ViewModels/CollectionEditorSession.cs`
* `ViewModels/MainWindowViewModel.Collections.cs`
* `ViewModels/MainWindowViewModel.cs`
* `Views/MainWindow.GridDrop.cs`
* `Views/MainWindow.axaml.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlLayout.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.DataGridPreviewRegression.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.GridDrop.Pointer.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Startup.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4A.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4B.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/README.md`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.Structure.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlStructuralPolicy.cs`
* `Docs/AxamlStructuralPhase4CPerformance.csv`
* `Docs/AxamlStructuralPhase4CStages.csv`
* `Docs/UniversalControlsAndPropertiesPhase4C.md`
* `ViewModels/MainWindowViewModel.AxamlCollections.cs`
* `ViewModels/MainWindowViewModel.AxamlStructure.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4C.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4CIpc.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4CPerformance.cs`
* `smoke-tests/UniversalMetadata/Phase4CRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/Summarize4CPerformance.py`

Генерируемый общий metadata coverage сохранён без содержательных изменений; нормализованы только платформенные NewLine/line-ending различия diagnostic regeneration. Новых Toolbox types, NuGet/TargetFramework изменений и IPC wire fields нет. Git HEAD остаётся исходным; изменения этой фазы в рабочем дереве, commit/push не выполнялись.

## 16. Оставшиеся ограничения и manual acceptance

* Только перечисленные panels и две static Items структуры. Произвольные ContentProperty/Items collections, templates, custom controls, ContextMenu/Flyout structural authoring не заявлены.
* Bound layout properties и attached property elements при reparent — read-only; перенос panel control в Items и перенос Item из коллекции не включены; ambiguous namespace/directive context блокируется.
* Full re-import/source history имеют измеряемую CPU/memory стоимость; оптимизации не выдаются за incremental engine.
* Existing imported Preview остаётся частичной безопасной проекцией. DynamicResource/Binding сохраняется в XML, но его значение не вычисляется Designer.
* UIA crash и ordinary Window retention не считаются устранёнными. Ручные installed VSIX/DPI/platform checks не заменяются smoke tests.

Ручная матрица: установить VSIX; открыть AXAML с comments/resources/bindings; добавить каждый из восьми типов в поддерживаемые panels; проверить source и native Preview; переместить subtree, Undo/Redo до/после Save; переключить/закрыть документы и перезапустить VsHost; изменить buffer вручную между Apply и ACK и проверить отказ без overwrite. Проверить grid Auto/star tracks, scroll/zoom, DPI/multi-monitor и accessibility clients.

## 17. Итог и Phase 4D

**STRUCTURAL AXAML EDITING: READY WITH CONDITIONS.**

Пять panels, восемь insert types, remove/reorder/reparent, две static Items структуры и существующий explicit Apply/Save → Named Pipe → buffer validator → ACK прошли автоматизированную проверку. Источник сохраняется spans/raw fragments, unsafe mapping/context отклоняется, typed local values новых узлов не сбрасываются. Подтверждённых незакрытых P0/P1 по реализованной границе после исправлений не обнаружено.

Условия: ограниченные capabilities, стоимость больших source transactions/history/Preview, известный UIA risk и неисполненная installed-VSIX/DPI manual acceptance. Обычное Window retention остаётся прежним ограничением. Это не универсальное authoring произвольного AXAML и не утверждение об исправлении native UIA crash.

Рекомендуется отдельно расширять Content/collection capabilities по реальным fixtures, профилировать full re-import/retained source history и завершить installed-VSIX acceptance. Phase 4D автоматически не начинается.
