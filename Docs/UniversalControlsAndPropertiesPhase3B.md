# Universal Controls — Phase 3B

Дата: 14.11.2026. Продукт: Beta 0.1. Baseline HEAD `9f78cc0e5b2e6b08de83a9ae773640d98a5671e3`, main, дерево было чистым. Основа — отчёты Phase 1, 2, 3A и текущие specialized editors. NuGet, TargetFramework, тема, VSIX/IPC, SQL providers и глобальный Document Model не заменены. Коммиты/релизы не создавались.

## 1. Архитектура

До этапа существовали: плоский `StandardItemsEditorWindow` для строк ListBox и Flyout, `StandardMenuItem` со стабильным Id, отдельный DataGrid Column Editor, ParentId/StackOrder для visual layout, capability/source maps для AXAML. Обычные JSON Menu/ComboBox/TabControl/TreeView не были включены как полноценные Toolbox roots, хотя часть типов уже поддерживалась source projection.

Новый путь расширяет прежний editor window и PropertyGrid:

```text
Existing PropertyGrid.Items / Flyout.Items / ContextMenu.Items action
 -> MainWindowViewModel.OpenCollectionEditor (capability + binding/version guard)
 -> CollectionEditorSession (document/session/owner identity)
 -> existing StandardItemsEditorWindow overload, TreeView + DesignerPropertyInspector
 -> selected item -> existing CreateRow / typed editors / Favorites / search
 -> WriteCollection -> existing CustomProperties + existing atomic Undo batch
 -> DesignerChanged -> same native factory / AXAML exporter / Preview loader
```

Один MainWindowViewModel владеет edit context. Отдельного независимого Inspector/metadata engine нет: окно использует прежний DesignerPropertyInspector и те же categories/rows/transactions. Основной Inspector также показывает выбранный collection item; DesignerSurface сохраняет selection владельца, не рисует MenuItem/ContextMenu как свободные Canvas controls.

DataGrid Column Editor не переписан: прежний Columns action и существующий draft/apply workflow сохранены. Старый draft constructor StandardItemsEditorWindow оставлен для совместимости; новый workflow применяет действия сразу, с Undo/Redo.

## 2. Metadata и capabilities

`AvaloniaTypeInfo.Collections` дополняет исходный `AvaloniaMetadataCatalog` и его ConditionalWeakTable. Collection metadata читает публичные signatures, ItemType/CollectionType, getter/setter, не instance getters. Отдельного strong Type cache нет. `CollectionMetadataCatalog.Get` — facade к тому же Type metadata.

Поля: PropertyName, OwnerType, ItemType, CollectionType, IsReadOnly (нет публичного setter), CanAdd/Remove/Reorder/EditItems, IsControlCollection/IsDataCollection, SerializationStrategy, ImportCapability, ExportCapability. Read-only getter **не** означает immutable Items: reviewed adapter может изменять содержимое. Неизвестный IEnumerable остаётся PreserveOnly. Styles/Resources не объявлены редактируемыми; Panel.Children mutations ограничены уже поддерживаемыми Grid/Canvas/StackPanel/DockPanel/WrapPanel. DataGrid.Columns делегируется прежнему editor. Runtime binding/source capability отдельно может запретить статически разрешённую операцию.

## 3. Реализованные коллекции

| Owner/property | Native items | Разрешённый subset |
|---|---|---|
| Menu.Items | MenuItem, Separator | Header text, Name, IsEnabled, nested children, порядок |
| ContextMenu.Items у поддерживаемых owners | MenuItem, Separator | Вложенные пункты, открытие настоящего ContextMenu |
| ComboBox.Items | ComboBoxItem | Content text, Name, IsEnabled, порядок, SelectedIndex |
| ListBox.Items | ListBoxItem | То же, SelectionMode и SelectedIndex; старые string arrays читаются отдельно |
| TabControl.Items | TabItem | Header text, Content text, Name, IsEnabled, порядок, выбор вкладки |
| TreeView.Items | TreeViewItem | Header text, Name, IsEnabled, IsExpanded, nested children, порядок |
| DropDownButton/SplitButton.Flyout.Items | MenuItem, Separator в MenuFlyout | Вложенность, порядок; существующий Logic для пунктов |
| DataGrid.Columns | Existing column model | Прежний специализированный editor, без замены |

Четыре новых root descriptors зарегистрированы после проверки полного pipeline; MenuItem/TabItem/TreeViewItem/ContextMenu не зарегистрированы как произвольные Toolbox blocks. Нет автоматической регистрации всех IEnumerable/обнаруженных типов.

Root Items rows показывают count и Value Source. Bound ItemsSource/BindingSource/Resource блокирует конфликтующее редактирование. Неизвестные/повреждённые/future payloads остаются в JSON и получают read-only reason. Новые root adapters поддерживают **локальные** Items; внешний ItemsSource у этих четырёх roots требует отдельного data adapter и вызывает явную диагностику вместо подстановки local/demo items. Существующий bound ListBox/DataGrid workflow сохранён.

## 4. Nested properties

Сохраняются существующие typed string/bool/enum/numeric/Brush/Font editors. Thickness и CornerRadius имеют lazy четыре компонента, Grid definitions — Auto/Star/Pixel; состояние раскрытия хранится в bounded наборе строковых property paths (до 512), с document/control/key scope. Дочерние UI editors не создаются до раскрытия. Font properties остаются реальными отдельными Avalonia properties, фиктивного Font object не добавлено.

Вложенные collection item fields берутся только из whitelist, metadata owner — настоящий MenuItem/TabItem/etc. Нет обхода произвольного object graph. Cycle/duplicate Id/depth/item-count validation выполняется до записи: depth ≤16, всего ≤2000. Collections/templates/commands/resources вне адаптеров остаются read-only.

Reset поля DTO восстанавливает значение reviewed контракта (text empty, IsEnabled=True, IsExpanded=False) атомарно; tooltip прямо отличает это от Avalonia.ClearValue. Семантический unset/Reset Phase 3A для обычных native properties не заменён записью default literal.

## 5. JSON / Document Model / совместимость

Глобальная схема проекта не менялась. Используются существующие CustomProperties.ValueJson; новое значение `<property>.Collection` содержит `{Version:1, Items:[...]}`. Элементы расширяют прежний StandardMenuItem: Id, Kind, Name, Text, Content, IsEnabled, IsExpanded, Children. Runtime Avalonia objects не сериализуются.

Порядок задаётся порядком массива; parent для UI/Explorer вычисляется из Children и owner Id. Это collection-owned hierarchy, не второй Layout Engine и не отдельные fake controls в Controls. ParentId/Grid placement/ControlId top-level владельца не меняются.

Прежние ListBox string arrays и плоские Flyout arrays остаются читаемыми. Typed payload появляется только при явной операции редактора; старые project files не мигрируются при Open. Старые static ListBox SelectedItem/bindings, которым смена item type могла бы изменить семантику, блокируют автоматическое преобразование. Неизвестные JSON fields сохраняются extension data; mutation unknown/future contracts запрещена. Version-aware validation не исполняет пользовательские типы.

## 6. Undo / selection / Explorer

Add, Remove Subtree, Reorder, property edit и Reset используют прежние snapshots и единый batch. Независимые действия получают границу coalescing. Selection не пишет документ и не очищает redo. Удаление nested Flyout item удаляет относящийся Logic, Undo возвращает оба; Name rename переназначает ссылки Logic по стабильному item Id.

Session проверяет document/session/owner и selected item; старый Row callback не применяет значение к другому элементу/форме. Дополнительная UI selection memory хранит bounded hashes состояния и Id, не snapshots/VM; она восстанавливает выбранный item при Undo/Redo, не меняя Undo storage.

Reorder и property edits сохраняют existing PropertyGrid categories; смена selected item создаёт только его whitelist rows и переиспользует native presentation Phase 3A. Tree nodes синхронизируются по Id. Explorer использует отдельные collection node fields в прежнем StructureTreeItemModel; они не поддерживают canvas move/resize. Selection узла выбирает владельца на DesignerSurface и item context в том же PropertyGrid.

## 7. AXAML Import / Round-trip

Существующие ComboBoxItem/ListBoxItem/TabItem/TreeViewItem paths сохранены. Menu/MenuItem/ContextMenu распознаются source metadata; Separator допускается в menu hierarchy. Property-element ContextMenu и неизвестные subtrees сохраняются source-preserving policy. Структурный Collection Editor для imported AXAML **read-only**, с причиной; source bindings/styles/resources/templates/namespaces/comments не регенерируются. Существующие reviewed scalar minimal patches/version/checksum guard сохраняются.

## 8. Preview / Export

Designer и Legacy Preview используют расширенный StandardItemsControlFactory/CollectionControlFactory; AXAML Preview — существующий loader export AXAML. Единственный writer helper пишет nested native item tags/property elements, не ToString. Grid остаётся Grid, Canvas — Canvas. Generated C# initialization/event handlers остаются существующими, runtime проверяется отдельным процессом.

Generated Grid и Canvas: restore/build/run **PASS** отдельными процессами. В каждом проверены 6 настоящих roots и их collections: Menu/nested MenuItem/Separator, ComboBoxItem/ListBoxItem selection, TabItem content/switch, TreeView expand/collapse, ContextMenu и typed Flyout, resize 800×600/1200×800/1600×900. Первый Canvas build был красным (whitespace), исправление подтверждено повтором. Новые collection fixtures имеют одну форму; отдельная Multi Form JSON/history проверка и прежний broad Multi Form runtime/navigation пройдены, но специальная новая multi-window collection runtime fixture не создавалась.

## 9. Подтверждённые ошибки разработки и исправления

* Первые 13/19: новые root type keys были в registry/bridge, но прежний built-in classifier считал их plugins и генерировал placeholder в clean export/AXAML Preview. Добавлен reviewed factory type policy. Native tests не ослаблялись.
* Menu source Name не распознавался, поскольку новые source metadata не были включены в common property map. Теперь используются прежние common accessors; no-op source остаётся побайтно неизменным.
* Первый Canvas generated build: отсутствие пробела между x:Name и Width; PlacementAttributes имеет разные whitespace conventions у legacy/native. Добавлен явный разделитель, XML parse и Canvas runtime regression.
* Routed test helper Phase 2 знал только свои шесть типов; новый тест использует actual named native control, сохраняя строгую проверку типа/selection/placement.
* Удержание CollectionEditorSession воспроизведено даже после idle при намеренно живом закрытом Window. SOS подтвердил два roots: UIA/compositor → toolbar Button.Click closure → session; UIA/compositor → TextBlock binding DataContextNode → Category → Row action → session. Исправлены owned Click unsubscribe и release explicit Row/Category contexts/typed Row bindings при удалении принадлежащих Inspector template views. Это не искусственная очистка проекта и не production GC. Проверка с поисковым фильтром перед закрытием дополнительно выявила удержание старых row callbacks: они теперь содержат только EditorId/itemId/key и разрешают активную session при вызове, без strong session reference. После этого проверка с намеренно живым закрытым Window проходит; устаревший token отклоняет запись.

* Дополнительные red-before regression checks подтвердили два случая: одинаковый Name принимался в коллекциях разных owners/property, а `{Version:1,Items:null}` приводил к NullReferenceException. До изменения snapshots добавлена проверка имён в пределах формы (включая generated/reserved names); null Items теперь даёт FormatException/read-only reason с сохранением исходного JSON. Обе ошибки исправлены без изменения snapshot storage.
* Общий CornerRadius применялся native factory, но отсутствовал в новом collection AXAML writer. Он добавлен в общий путь; пять native checks проверяют фактическое значение 9 в обоих Preview.

## 10. Memory / native crash

Строгий session WeakReference check с намеренно живым закрытым Window, включая применение поиска до Close, после исправления **PASS**. Промежуточные 27/29 (два memory red) сохранены; последующие 29/29 и 30/30 получены после изменения lifecycle/callbacks, а не удалением проверок. Первый red и оба SOS roots сохранены, а не замаскированы повторным GC. В новой полной suite также есть 20 циклов editor open/close с сохранёнными Window references: проверяется освобождение sessions/data и отсутствие active edit context, не немедленное возвращение process Working Set ОС.

Отдельно один development-процесс прервался без итоговой строки после TreeView checks. Windows Event 1000: **14.11.2026 20:12:21**, dotnet.exe PID **40752**, **uiautomationcore.dll 7.2.19041.5794**, exception **0xc0000005**, offset **0x47593**. WER dump существовал в пользовательском CrashDumps; он прочитан, не изменён/удалён. Доступные managed stacks включают Win32 GetMessage/Dispatcher; native symbol stack отсутствует. Модуль/код похожи на исторические наблюдения, но одинаковая root cause или причинная связь с clearing contexts **не доказана**. Статус crash: **UNRESOLVED**. Второй сбой — **20:26:41**, PID **74048** (0x12140), тот же module/offset/code — зафиксирован в существующем Preview Phase 2 suite, exit **-1073741819**. В следующем отдельном запуске suite прошёл 28/28; это не отменяет native failure. Событие сохранено в native-crash-event.json, managed stacks — native-crash-managed-stacks.txt. Это не зелёный первый прогон и не исправленный native bug.

Полная следующая Collection suite завершилась **29/29**, а после добавления проверок null/конфликтов имён — **30/30**; это функциональный повтор, не доказательство отсутствия редкого native сбоя. Physical external UIA/accessibility acceptance остаётся условием.

Existing Preview Memory: **36/36**, по 20 Legacy/AXAML циклов, реальная LocalDB/close-during-query, aliveWindows=0/activeWindows=0/clockObservers=0 на checkpoints. Stabilization negative control ordinary Window **24/25**, alive=6/6; известный platform/UIA retention не объявляется исправленным.

Доказательства GC root: `artifacts/universal-phase3b/forensic/session-gcroot.txt` и `forensic2/session-gcroot.txt`; два собственных managed dumps и локальный dotnet-dump tool после извлечения evidence удалены; пользовательский WER dump не изменялся. Сохранены gcroot/stacks/Event Log, raw timings, fixtures и логи. Cleanup ограничен проверенными путями внутри artifacts/universal-phase3b; итоговый объём около 58,3 MiB. Production GC.Collect не добавлен.

## 11. Performance

Методика: Release без debugger, Avalonia 11.1.1, RenderScaling=1, одна и та же сцена, последовательный запуск после builds/runtime. 3 warm-up и минимум 30 измерений на сценарий; основной финальный profile — **495 samples = 450 измерений + 45 warm-up**. Seed/setup выполняется вне timer. Cold/setup не смешан со steady-state; отдельный полноценный cold-start benchmark приложения здесь не заявлен.

Elapsed включает action и явный UpdateLayout окна редактора. Allocations — **текущий UI thread** от начала действия до compositor callback, не allocations всех потоков. GC — сумма process-wide collection deltas за 30 измерений (Gen0/1/2). Callback относится к compositor окна редактора; это не физическое появление пикселей и не гарантия завершения render всех окон. Отсутствуют искусственные Task.Delay/timing CI thresholds в production.

| Начальный N | Операция | Median / P95, ms | Alloc median / P95, MiB | Compositor median / P95, ms | GC 0/1/2 за 30 |
|---:|---|---:|---:|---:|---|
| 1 | Add | 21.12 / 31.89 | 0.58 / 0.64 | 22.47 / 33.06 | 0/0/0 |
| 1 | AddSequence10 | 190.27 / 296.24 | 5.67 / 5.71 | 191.72 / 298.07 | 12/5/0 |
| 1 | AddSequence100 | 1947.06 / 2871.75 | 135.52 / 135.70 | 1951.48 / 2876.40 | 262/56/5 |
| 1 | Open | 54.88 / 77.95 | 6.65 / 6.69 | 59.41 / 82.74 | 15/15/0 |
| 1 | Reorder | 10.90 / 14.60 | 0.27 / 0.29 | 11.13 / 14.84 | 0/0/0 |
| 1 | Selection | 11.21 / 16.33 | 0.31 / 0.34 | 11.80 / 16.89 | 0/0/0 |
| 10 | Add | 39.95 / 69.17 | 2.45 / 2.46 | 41.29 / 70.54 | 2/2/0 |
| 10 | Open | 107.31 / 121.34 | 17.54 / 17.62 | 121.64 / 151.83 | 36/36/1 |
| 10 | Reorder | 19.28 / 54.68 | 1.79 / 1.84 | 20.02 / 55.23 | 4/4/1 |
| 10 | Selection | 19.57 / 25.08 | 0.57 / 0.58 | 20.22 / 25.70 | 0/0/0 |
| 100 | Add | 47.17 / 81.06 | 2.35 / 2.42 | 48.96 / 82.88 | 9/9/1 |
| 100 | NestedAdd | 44.05 / 71.94 | 2.18 / 2.24 | 44.80 / 72.69 | 5/5/0 |
| 100 | Open | 111.89 / 120.43 | 17.46 / 17.61 | 121.46 / 146.75 | 32/32/1 |
| 100 | Reorder | 21.27 / 26.35 | 1.68 / 1.70 | 21.84 / 27.04 | 0/0/0 |
| 100 | Selection | 23.74 / 42.65 | 0.56 / 0.58 | 24.29 / 43.32 | 1/1/0 |

N=1 AddSequence10/100 — **10/100 обычных отдельных действий** с соответствующими Undo/refresh, не одна batch операция. Последовательность 100 Add по-прежнему занимает около **1,95 s median / 2,87 s P95**, 135,5 MiB UI allocations: это оставшийся P2. Один Add при N=100 — **47,18 / 81,06 ms**. Reorder не вызывает RefreshPropertyInspector (0), Add/Selection — один, AddSequence — 10/100. Существующие categories при reorder/property edit сохраняются.

Счётчик NativeEditors в harness считает только `PropertyGridTextEditor` внутри окна коллекции: **2** во всех размерах, не все виды controls/checkboxes/TreeView containers. Это подтверждает отсутствие массового создания текстовых property editors для каждого item; не означает, что во всём UI лишь два объекта.

Ранее полный профиль дал 495 samples, но N=1 Selection оказался no-op (выбирался уже выбранный узел, 0 refresh). Эти 30 строк **исключены из выводов**, исправлен выбор другого Id. Отдельный повтор 132 samples дал 5,75/17,91 ms; финальный полный повтор выше снова содержит реальные selection changes с одним refresh. Старый JSON сохранён как `collection-performance-before-final-guards.json`, отдельный повтор — `collection-selection-performance.json`; сравнивать их как улучшение алгоритма нельзя.

Обычный Inspector — тот же `--universal-controls2-profile`, 30 после warm-up, без metadata rebuild при selection:

| Состояние | Median, ms | P95, ms | Alloc median, bytes |
|---|---:|---:|---:|
| Fresh baseline до изменений 3B | 99,68 | 168,61 | 5 302 600 |
| Финальный 3B | 102,21 | 163,74 | 5 588 712 |

Median +2,5%, P95 −2,9%, allocations +5,4%. **Ускорение обычного Inspector не заявлено**, allocations слегка выросли; статистическая значимость малой разницы не установлена. Старые Phase3A 96,03/146,86/5,48 MB — отдельный прогон, не подменяет свежий baseline. Ранее Collection Editor для этих иерархий отсутствовал, поэтому эквивалентного before/after collection benchmark нет.

Raw timings/stages/GC: `artifacts/universal-phase3b/collection-performance.json`, `collection-performance-summary.json`, `artifacts/universal-phase2/inspector-phase3b-before.json`, `inspector-phase3b-terminal.json`. Final profile завершился exit 0; все 15 сценариев имеют по 30 измерений. P95 вычислен nearest-rank. Различия P95 между прогонами наблюдаются, но причинная связь с конкретным GC/native/UIA событием не доказана; нового performance refactoring не выполнялось.


## 12. Build / regression

Все suites запускались последовательно в Release без debugger; тяжёлые build/native suites не выполнялись одновременно с performance profile. Таблица разделяет первый matrix run и подтверждения после исправлений. Подробные exit codes: `artifacts/universal-phase3b/final-results.json`, `last-results.json`, `terminal-results.json`.

| Проверка | Результат и предел доказательства |
|---|---|
| Solution Release / VSIX / VsHost | 0 ошибок, 1 прежний NU1603 (VS SDK.Analyzers 17.7.20 → 17.7.22); NuGet versions не менялись |
| Runner Release | 0 ошибок / 0 предупреждений |
| Collection 3B final functional checks | 30/30, включая реальные оба Preview, CornerRadius, name collisions, null JSON, memory и Undo |
| PropertyGrid 3A | 30/30; baseline тоже 30/30 |
| Metadata / Controls Phase 2 | 45/45 и 40/40 |
| Grid-first / Grid Editor / Grid Drop | 26/26, 45/45, 40/40 |
| Nested / routed Pointer-Marquee / Layout 7A.2 | 59/59, 26/26, 13/13 |
| Undo cache / Beta data-integrity | 25/25 и 16/16 |
| Preview consistency | 32/32 |
| Preview Phase 2 | Первый matrix run: native crash -1073741819, Event Log сохранён; отдельный повтор 28/28 не отменяет сбой |
| Existing stabilization | 24/25; ordinary Window negative control retained 6/6, обе Preview collectible checks PASS |
| LocalDB / DataGrid | 47/47; реальная изолированная LocalDB, Settings/schema/source delivery PASS; не все 47 checks являются live, labels CONTRACT/LIVE сохранены |
| SQL generated runtime | PASS: Customers=3, Orders=2, независимые VM/источники и resize; demo rows не подставлялись |
| Preview memory | 36/36, 20 циклов каждого режима, закрытие во время загрузки и реальные SQL rows |
| PostgreSQL | 20/20 offline/contract; live SKIP, сервера нет |
| Eremex isolation | Cold plugin / missing theme / missing runtime — PASS; лицензирование/themes/packages не изменялись |
| AXAML source / VSIX-VsHost-IPC | Broad minimal patch/ACK/stale version/disconnect scenarios PASS, новый imported Menu no-op и structural readonly PASS; интерактивный установленный VSIX не проверен |
| Новые generated Grid + Canvas | Restore/build/run PASS отдельными процессами, коллекции/интерактивность/resize реально проверены |
| Broad regression | Первый полный matrix run 72/72. Следующий 71/72: ToolboxStandardItemsExport ожидал Logic по фиксированным 100 ms во время LoadingOverlay. Test-only ожидание заменено bounded условием overlay hidden + ожидаемый Text, исходная проверка результата сохранена. Targeted повтор 1/1. Финальный полный результат указан ниже |

Финальный повтор после name/null/CornerRadius и test-only lifecycle исправлений: **Solution 0 errors / 1 existing warning**, **PropertyGrid 30/30**, **Broad 72/72**, **Grid generated runtime PASS**, **Canvas generated runtime PASS**. `edge-fixed-collections3b.log`: **30/30**. Красные промежуточные прогоны не удалены и не названы зелёными первыми прогонами.

При первоначальной разработке были также 13/19 (classifier/source integration), красный Canvas generated build (whitespace), memory red и native crash — они описаны в секциях 9–10, исходные логи сохранены. Performance harness один раз завершился managed exception: deadline compositor ожидания ошибочно отсчитывался от начала 100 Add, а не начала ожидания callback. Исправлен timer harness, не время production операции и не timing threshold; последующий полный profile завершился. Cleanup AccessDenied на загруженных smoke-output DLL фиксировался отдельно и не подменяет PASS/FAIL функционального сценария.

Изолированная GUID LocalDB остановлена и удалена после тестов; пользовательские базы не менялись. Два собственных managed retention dumps использовались только для SOS; production GC.Collect/глобальные UIA/native workaround не добавлены.

## 13. Изменённые файлы

Изменения разделены по существующим слоям; полный diff остаётся в рабочем дереве.

* Collection contract: `Models/StandardItemsProperties.cs`, новый `Models/CollectionItemsContract.cs`, `Models/StructureTreeItemModel.cs`.
* Metadata: `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`, новый `CollectionPropertyMetadata.cs`.
* Native/Toolbox: `DesignerSystem/BuiltIn/BuiltInControlRegistrar.cs`, новые `BuiltInControlRegistrar.Collections.cs`, `CollectionControlFactory.cs`, `StandardItemsControlFactory.cs`.
* VM/PropertyGrid: `ViewModels/MainWindowViewModel.cs`, `MainWindowViewModel.StandardItems.cs`, `MainWindowViewModel.PropertyMetadata.cs`, новые `MainWindowViewModel.Collections.cs`, `CollectionEditorSession.cs`, `PropertyGridRowViewModel.cs`.
* Existing UI/preview bridge: `Views/StandardItemsEditorWindow.cs`, новый `StandardItemsEditorWindow.Collections.cs`, `MainWindow.StandardItems.cs`, `MainWindow.axaml.cs`, `PreviewWindow.StandardItems.cs`, `PreviewWindow.axaml.cs`, `DesignerPropertyInspector.Presentation.cs`.
* Lazy compound presentation: `Controls/PropertyGridCompoundEditor.cs`, `PropertyGridGridLengthEditor.cs`.
* Source metadata/projection: `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`, `AxamlImportService.cs`, `AxamlPhase4Projection.cs`.
* Tests: `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новый `Program.Collections3B.cs`, `Program.StandardItems.cs` (bounded lifecycle wait вместо фиксированных 100 ms), `smoke-tests/UniversalMetadata/README.md`, новый `Phase3BRuntimeProbe.cs.txt`.
* Reports: этот документ, обновлённые `Docs/UniversalControlCoverage.csv`, `UniversalPropertyCoverage.csv`, `UniversalCoverageSummary.json`. Диагностические логи/JSON/harness scripts находятся в игнорируемом `artifacts/universal-phase3b`, не включены в production.

Покрытие: типов 140; descriptors 23 → 27. Scalar/alias policy pairs 7139 → 7785; Displayable 1206 → 1337, Editable 694 → 814, Serializable 954 → 1055, Importable 801 → 869, Exportable 695 → 819. Это **числа metadata policy**, не доказательство универсального runtime покрытия каждой из 7785 пар; фактически проверенные collection subsets перечислены выше. CSV подсчитан настоящим CSV parser с учётом multiline fields.

## 14. Ограничения / ручная приёмка

* Это reviewed collection adapters, **не универсальная поддержка всех коллекций Avalonia**. Styles/Resources/arbitrary CLR graphs/Template Designer не реализованы.
* Header/Content поддерживают text subset; размещение произвольного Grid/Button tree в TabItem.Content, drag между ветками collection и расширенные commands/templates требуют следующего отдельного adapter этапа.
* ContextMenu editor подключён к поддерживаемым item owners; не объявлен универсальным attached-object editor для каждого Toolbox control.
* Structural imported AXAML readonly; полноценная регенерация не выполняется.
* Live PostgreSQL, Linux/macOS, physical DPI/мультимонитор и установленный interactive VSIX требуют ручной проверки; automated VSIX/IPC не заменяет её.

Ручной acceptance checklist (не объявлен автоматически пройденным):

- [ ] В обычном MainWindow открыть Items action из PropertyGrid, создать nested menu/separator, изменить Header/Name/IsEnabled, Undo/Redo, Save/Open.
- [ ] Мышью/клавиатурой проверить Menu, ContextMenu по right-click, ComboBox popup, ListBox selection, вкладки и TreeView expand/collapse; убедиться, что modal editor не оставляет pointer capture.
- [ ] На двух формах открыть/закрыть редактор, переключить owner/форму, выполнить Undo после Remove Subtree и проверить Explorer/Inspector/Logic.
- [ ] Проверить typed compound focus, Reset, Favorites, поиск и возврат scroll/expand state; закрыть окно во время незавершённого ввода.
- [ ] Проверить 125/150/200% physical DPI, крупный TreeView scrolling и смену монитора; физические DPI/мышь не заменять routed событиями.
- [ ] В установленном VSIX импортировать вложенные items с bindings/resources/comments, применить разрешённый scalar patch и убедиться в сохранении исходной структуры.
- [ ] Повторить с внешним screen reader/UIA и собрать native symbols/stack при сбое. Без этого native crash не закрыт.

## 15. Следующий этап

1. Отдельная native/UI Automation stability investigation с crash symbols/stack, без предположений о root cause.
2. Whitelisted Content-control tree adapter для TabItem и explicit cross-branch move с сохранением Id.
3. Source structural patch adapter только после доказанной безопасной вставки/удаления.
4. Дополнительные data/command/template adapters и accessibility acceptance по отдельным vertical slices.

**COLLECTION EDITORS: NOT READY для безусловной стабильной приёмки.**

Рассмотренные JSON collection adapters функционально реализованы и проверены через native Designer/Preview и отдельные generated runtime. Однако два зафиксированных native access violations в UI Automation остаются без доказанной root cause. Их нельзя объявить исправленными по успешному повтору. Кроме того, 100 последовательных отдельных Add остаются затратными; произвольный Content/control graph и структурные source patches сознательно не включены в reviewed capability.

Для локальной проверки доступен ограниченный набор адаптеров; основание для следующей приёмки — отдельное расследование native crash и ручная accessibility/VSIX/DPI проверка, а не массовая регистрация остальных коллекций.

Phase 4 не начиналась. Version/tag/release не менялись.
