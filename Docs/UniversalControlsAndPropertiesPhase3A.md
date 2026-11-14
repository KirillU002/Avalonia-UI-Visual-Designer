# Universal Controls — Phase 3A

Дата: 14.11.2026. Baseline HEAD: `a1d98d6f792c19b157b5d2c757d43d6eb5b49b56`, `main`, чистое рабочее дерево. Основа — [Phase 1](UniversalControlsAndPropertiesPhase1.md), [Phase 2](UniversalControlsAndPropertiesPhase2.md). Toolbox, NuGet, TargetFramework, Document Model/JSON schema, глобальная тема, VSIX/IPC, SQL/PostgreSQL и Preview/Export architecture не менялись. Коммиты/теги/релизы не создавались.

## 1. Архитектура существующего Inspector

Отдельного PropertyInspectorViewModel в текущем коде нет. Owner — `MainWindowViewModel`, его `RebuildPropertyGrid`, существующие `PropertyGridRowViewModel`/`PropertyGridCategoryViewModel`, `DesignerPropertyInspector` и editor templates. Metadata поступает из прежнего `AvaloniaMetadataCatalog` и reviewed policy `StandardControlProperties`. Старые model/descriptor/layout adapters сохранены; новое обнаружение не разрешает запись произвольного AP/CLR object.

```text
Selection -> MainWindowViewModel.RebuildPropertyGrid
  -> BuildPropertyGridRows + reviewed read-only complex signatures
  -> existing specialized/metadata rows + source capability guards
  -> DescribeInspectorProperty -> native schema + current capability/value source
  -> existing categories/search/Favorites/context dictionaries
  -> one completed projection notification
  -> DesignerPropertyInspector.SynchronizePresentation
     -> current category views + stable ContentControl slots
     -> NEW immutable-context RowVM as Content
     -> recycling existing row/editor template views
  -> Avalonia binding/layout -> one bounded native effective-value read

Edit -> existing validated setter -> document session -> Undo snapshot
Reset metadata literal -> remove CustomProperties entry -> same transaction
Source Reset -> AxamlPatchWriter -> one attribute deletion
```

Это один прежний Inspector. Presentation slots — UI ownership внутри этого UserControl, а не второй document model/PropertyGrid. RowVM с target callbacks не превращаются в глобальные изменяемые контексты. Категории, поиск, Favorites, русские подсказки, inline ComboBox, Color picker, тема и scroll host сохранены.

## 2. Доказанный root cause и эксперименты

Свежий неизменённый baseline Phase 2 дал **127.55 ms median / 306.08 ms P95**, median allocations **20,507,092 B**, metadata rebuilds=0. Исторический результат 140.54/248.59 ms не выдаётся за идентичный по шуму запуск.

Основная стоимость — повторная materialization/стилизация/layout native дерева строк после `PropertyGridCategories.Clear`, создания новых категорий и items containers. Пример: около 1164 native visual nodes и 20 text editors для 37 RowVM. Каталог metadata уже кэширован; повторного reflection discovery на selection нет. Один основной Rebuild, а не несколько.

Test-only category reuse + Replace RowVM почти не уменьшил allocations: **137.24 ms / 20,527,796 B** baseline против **133.82 ms / 20,494,036 B**. Items containers всё равно создавались заново. Корректный native slot experiment дал **60.26 ms / 4,532,496 B** при 1205 visual nodes/20 editors и проверенных актуальных Row contexts. Дополнительные nodes — presentation hosts, не потеря controls.

Первые низкие результаты slots experiment с отсутствовавшими editors отвергнуты. Неверная передача DataContext и конкурирующие ItemsSource bindings выявлены строгими assertions. Эти runs не считаются performance evidence. Исходный prototype и observations оставлены только в игнорируемых artifacts; экспериментальные ветки удалены из постоянного benchmark harness.

## 3. Локальная оптимизация

Существующие category/row XAML templates сохранены. Источником items теперь управляет presentation adapter. Ранее root/category ItemsSource bindings повторно назначали модельные коллекции при смене DataContext и разрушали slots. Вместо них owner завершённой проекции назначает текущий набор UI hosts.

`PropertyGridRecyclingTemplate` создаёт или переиспользует view, явно обновляя DataContext. Ресурс editor template остаётся отдельным для разных kinds. Смена типа редактора выбирает соответствующий template; текущие RowVM/context guards остаются независимыми от view. Отсутствующие categories/slots удаляются, при смене document session представление очищается. На detach снимается owner subscription и разовая LayoutUpdated subscription. Глобального кэша native controls/VM нет.

Диагностический environment switch `FORMDESIGNER_PROPERTYGRID_REUSE=0` сохраняет обычный путь построения тех же templates для сравнения/rollback; пользовательской настройки не добавлено. Он отключает только reuse, не metadata/validation/source safety.

## 4. Универсальная metadata и capability

`PropertyGridPropertyMetadata` оборачивает исходный `AvaloniaPropertyInfo`: Name/DisplayName, OwnerType, PropertyType, registration DefaultValue/HasDefault, native ReadOnly, Nullable, Attached, inherited declaration/InheritsValue, binding-target capability, editor hint, Category и ValidationRules. CurrentValue, Description, effective value, ValueSource, current read-only/capability и наличие local value находятся на прежнем RowVM.

Переименованные model fields сопоставляются с реальными AP/CLR names (например Text→Content, GridRow→Grid.Row, PlaceholderText→Watermark). Designer-only legacy поля не получают выдуманный native property. Discovered/Displayable/Editable/Serializable/Importable/Exportable различаются. IsBindable отражает API; оно не означает готовый arbitrary binding authoring UI.

DefaultValue — metadata registration, не результат исполнения instance getter и не предполагаемое значение темы. Effective value читается через публичный `GetDiagnostic` **из Avalonia.Base**, без добавления пакета Avalonia.Diagnostics. Допускаются только shipped native types; plugin/user getters для discovery не вызываются. Plugin metadata сохраняет прежний ConditionalWeakTable lifecycle.

## 5. Редакторы

| Значение | Реализованное поведение | Граница |
|---|---|---|
| String | Прежний inline TextBox; empty string; явный nullable null | Reserved scalar marker `(null)` и markup restrictions прежнего codec сохранены |
| Bool / bool? | CheckBox / native inline nullable ComboBox | Programmatic view reconciliation не является edit |
| Enum / nullable enum | Native ComboBox, options из metadata, typed serialization | Nullable enum codec проверен; новые свойства ради демонстрации не регистрируются |
| int/long/float/double/decimal, nullable | Invariant bounded codec, overflow/finite/AP validation, точное typed equality | Для shipped range controls проверяются известные literal Minimum/Maximum; неизвестные expressions не исполняются |
| Thickness | Summary принимает uniform/2/4 values; lazy Left/Top/Right/Bottom | Для старого scalar Padding/BorderThickness сохраняется прежний subset adapter |
| CornerRadius | Lazy TopLeft/TopRight/BottomRight/BottomLeft, одна операция на commit | Старые double-only model adapters не выдаются за полноценные четырёхзначные |
| GridLength | Auto/Star/Pixel и weight/value внутри compact Row/ColumnDefinitions | Использован существующий Grid definition item/adapter, без add/remove/general Collection Editor |
| Brush/Color | Прежний Color picker, ARGB/alpha, solid colors, nullable null; безопасный swatch converter | Gradient/resource/binding не преобразуются в SolidColorBrush |
| Font | Прежние FontFamily/FontSize/FontWeight editors + metadata FontStyle/Stretch | Только реально существующие и поддержанные типы/значения |

Составные fields создаются только при раскрытии; полный literal меняется одной прежней transaction. GridLength не меняет unit от показа редактора; per-track «вернуть *» не удаляет коллекцию definitions. Ввод недопустимого значения не меняет model.

Существующий TextBox терял повторный ввод после Enter до следующего focus cycle. Это воспроизведено routed TextInput + focus change и исправлено началом нового edit по следующему реальному input. Native focus сохраняется. Смена immutable Row context отменяет прежний draft, а не применяет его к другому target.

## 6. Value Source и сложные значения

Различаются Model (legacy), RegistrationDefault, LocalValue, Style, Binding, Resource, Inherited, Complex и Unknown. Row tooltip содержит источник и отдельно native effective value. Local отмечается modified glyph, Style — S, inherited — стрелкой, Binding — ƒ, Resource — R.

Для JSON reviewed literal наличие CustomProperties entry отличается от его отсутствия, даже если literal равен registration default. Для source сначала анализируются исходный attribute/property element и capability. Bindings, Dynamic/StaticResource и неизвестные expressions защищаются на уровне одного свойства. Source Styles сохраняются; их ресурсы/VM не исполняются ради discovery. Проверенная native priority используется для effective Style/inherited values, а не для выдумывания отсутствующей модели.

В Advanced добавлены безопасные read-only representations существующих complex signatures: Styles, Resources, Template/DataTemplates, DataContext, ItemsSource/ItemTemplate, ContextMenu/ContextFlyout/Flyout, SelectedItem. Getters/ToString произвольных objects не вызываются. Существующие качественные DataGrid/items/flyout action editors имеют приоритет перед этими read-only rows. Unknown JSON сохраняется; отсутствие codec не маскируется строковой сериализацией.

## 7. Reset и AXAML safety

Reviewed metadata Reset удаляет entry, а не записывает registration default или null. Явный default остаётся local value с доступным Reset. Native Style/inheritance может снова проявиться. Явный nullable null — отдельный edit и сохраняется JSON null.

Imported literal удаляется только при editable source capability и известном metadata-owned adapter. Patch удаляет единственный attribute span; остальные formatting/comments/styles/namespaces/unknown attrs остаются вне edits. Undo/Redo сохраняют различие присутствия/отсутствия entry. Новый xmlns:x для Reset не нужен, поскольку `{x:Null}` не генерируется.

Выявленная неоднозначность explicit MaxWidth/MaxHeight=Infinity при import исправлена: исходный явный literal сохраняется entry, поэтому no-op его не удаляет. JSON/native layout default normalization вне source import сохранена. Pending source Reset size constraint не применяется поверх native Style как локальный default.

Для legacy fields без представления «unset» оставлено явно подписанное восстановление **значения модели**, а не заявляется Avalonia.ClearValue. В imported AXAML такой reset блокируется, если безопасного remove adapter нет. Все literal edits продолжают проходить прежний source capability/patch writer. Полной регенерации AXAML нет.

## 8. Undo, selection и безопасность callback

Новый recycling выявил реальную проблему native two-way SelectedItem: при смене Options он записывал default в новый Row, менял документ от selection и очищал redo branch. `PropertyGridEnumEditor` оставляет native ComboBox UI, но отделяет reconciliation от явного input/commit. Отложенный popup старого Row не применяет значение новому target.

Независимые Inspector edits/Reset получили явную границу перед прежним history grouping; strokes одного edit/compound остаются в одном batch. Snapshot storage/serialization/history cache не переписывались. Прежнее временное coalescing остальных gestures не менялось.

Row context содержит document projection revision; он меняется при фактическом ApplyDocument, включая Undo/Open, а не при каждом harmless Inspector rebuild. Это блокирует callback по старому восстановленному объекту с тем же ControlId. Текущий edit принадлежит конкретному Row reference; поздний cancel другого Row не закрывает новый edit. Ссылки завершённого edit очищаются.

## 9. Performance Before/After

Release / .NET 8 / Windows, debugger отсутствует; один и тот же native viewport **1600×920 DIP**, RenderScaling=1. Builds, native suites и profiler не выполнялись одновременно. Timings не являются CI pass/fail thresholds.

**Сопоставимый прежний benchmark Phase 2:** три warm-up, 30 switches между Slider/ProgressBar/ToggleSwitch, синхронный selection + layout. На неизменённом HEAD: **127.55 / 306.08 ms median/P95**, **20,507,092 B** median allocations. На финальном коде: **96.03 / 146.86 ms**, **5,479,916 B**: median −24.7%, P95 −52.0%, allocations −73.3%. Исторические 140.54/248.59 ms из отчёта Phase 2 сохранены как reference, а не как идентичный локальный запуск.

**Контролируемое сравнение UI reuse:** финальный код в обоих режимах, те же typed editors/value-source/readonly rows; `FORMDESIGNER_PROPERTYGRID_REUSE=0` отключает только presentation reuse. По три полных warm-up прохода, затем **30 наблюдений каждого из семи типов** (210 на режим). P95 — nearest rank; allocations — median UI-thread bytes, MB здесь десятичные. GC — process-wide deltas за observation (включая callback compositor), не только CPU selection.

| Control | Без reuse median/P95, ms | Reuse median/P95, ms | Allocations до/после, MB | Compositor callback после median/P95, ms |
|---|---:|---:|---:|---:|
| Button | 84.62 / 118.08 | 28.37 / 61.82 | 19.27 / 3.82 | 30.08 / 63.46 |
| DataGrid | 123.07 / 151.62 | 41.73 / 60.89 | 24.88 / 7.93 | 44.81 / 63.48 |
| Grid | 95.29 / 115.49 | 34.29 / 64.13 | 19.05 / 3.95 | 36.88 / 66.01 |
| Calendar | 121.84 / 148.43 | 58.22 / 78.67 | 25.09 / 10.61 | 61.38 / 83.32 |
| NumericUpDown | 126.67 / 145.45 | 31.90 / 54.47 | 27.04 / 4.84 | 34.33 / 59.09 |
| ListBox | 122.50 / 163.09 | 45.25 / 79.13 | 25.19 / 7.17 | 47.80 / 81.99 |
| DatePicker | 102.80 / 158.10 | 61.07 / 78.11 | 26.68 / 8.78 | 64.61 / 85.35 |
| **Mixed 210** | **110.33 / 153.17** | **43.62 / 73.88** | **25.09 / 7.17** | **47.57 / 79.97** |

Mixed median −60.46%, allocations −71.44%. GC Gen0/1/2 за измеряемые observations: **397/264/81 → 161/127/28**. Metadata rebuilds на switches: **0** в обоих режимах.

Cold `FirstWindowAndInspectorSetupMs`: disabled **1580.09 ms**, enabled **2017.30 ms**. Это по одному cold sample всего MainWindow + initial Inspector, не изолированный Inspector и не median/P95 cold-start. В этом запуске cold enabled медленнее; улучшение cold-start не заявляется. Показатели cold не смешаны со steady-state.

Для проверки warm-up sensitivity сохранена первая серия с одним warm-up проходом: mixed **111.16/160.77 → 53.05/95.01 ms**, allocations **25.08 → 7.37 MB**; первые measured transitions были дороже. Original scene с instrumentation дала **171.41/240.20 → 99.48/141.08 ms**. Эти сцены не усредняются с семью типами. Старый JSON field `Mode=baseline` был label-only; actual disabled/enabled задавался env, в финальном harness Mode записывает реальный flag. Raw observations всех принятых серий сохранены; отвергнутые пустые editors отдельно помечены в §2.

Профиль финальной mixed series (median per observation, stages inclusive и частично вложенные):

| Stage | ms | Allocations, B | Calls |
|---|---:|---:|---:|
| InspectorSelectionChanged | 21.6868 | 2,622,736 | 1 |
| InspectorMetadataLookup | 0.3350 | 425,176 | 55 |
| InspectorPropertyRowsValuesDescriptors | 1.2782 | 576,352 | 1 |
| InspectorRowDescriptorsDefaultsAndCreation | 0.0476 | 49,216 | 56 |
| InspectorCategoryGrouping | 0.0237 | 32,320 | 6 |
| InspectorFavoritesResolution | 0.0067 | 4,648 | 56 |
| InspectorVisualProjection | 7.0029 | 1,243,124 | 1 |
| InspectorEditorControlCreationAndBindings | 1.0794 | 448,464 | 17 |
| InspectorRecycledBindings | 6.3009 | 985,744 | 60 |
| InspectorEffectiveValueReading | 0.2872 | 57,232 | 1 |
| InspectorMeasureArrange | 15.7804 | 4,543,484 | 1 |
| RefreshPropertyInspector | 10.4604 | 2,068,480 | 1 |

Selection event, completed Inspector rebuild и visual refresh — по одному на observation. `BuildNativeProjection` / `CreateDesignerWrappers` / `RebuildVisualTree` здесь 0: selection не перестраивает Designer layout. View reuse оставляет небольшой объём создания editors при смене типа строки: median 17 template builds и 60 recycled binding scopes, вместо полной materialization строк. Native-template creation и binding setup измерены совместным scope; model value reads включены в descriptor/row scope. Exclusive стоимость каждого внутреннего Avalonia binding/CLR getter отдельно не измерялась, nested medians не складываются.

Основной измеренный выигрыш — layout **72.23 → 15.78 ms median** и allocations native presentation. Сам metadata lookup — **0.335 ms** на 55 cached обращений; row/value/descriptor scope — **1.278 ms**, reflection rebuild отсутствует. Оставшиеся затраты: rebind/layout и GC pressure; metadata reflection не является главным bottleneck.

Во всех 11 observations верхнего 5% финальной mixed серии зарегистрирован GC в широком окне observation (selection → compositor/inspection). Это корреляция, не доказательство доли GC в каждой CPU-паузе. Один DataGrid sample: 97.42 ms, из них layout 65.20 ms; один Grid sample: selection 70.16 ms из 85.84 ms. ETW CPU/JIT/compositor trace не снимался; JIT, конкретный OS scheduling или shader compilation не объявляются установленной причиной. В сравнении P95 снизился, но tail не исчез.

Compositor callback — наблюдаемая граница после native layout, **не** измерение physical first visible pixel. UI-thread allocations не включают весь render-thread/native heap. Результаты показывают настоящее обновление существующего Inspector с проверенными active editor/Row contexts, а не ускорение отдельного synthetic metadata-метода. Raw: `final-disabled.json`, `final-after.json`, `performance-summary.json`, `phase2-comparable-final.log` в task artifacts. Повторяемые команды и diagnostic rollback описаны в `smoke-tests/UniversalMetadata/README.md`.


## 10. Coverage

Числа — пары «тип–свойство/alias» из обновлённого `UniversalPropertyCoverage.csv`, не число уникальных AvaloniaProperty. Discovered не означает Editable.

| Capability | Phase 2 | Phase 3A |
|---|---:|---:|
| Discovered | 7139 | 7139 |
| Displayable | 954 | 1206 |
| Editable | 694 | 694 |
| Serializable | 954 | 954 |
| Importable | 801 | 801 |
| Exportable | 695 | 695 |
| Unsupported / opaque outside displayed coverage | 6185 | 5933 |
| Contextual Layout | 288 | 288 |

Детализация текущей reviewed policy (pairs/aliases, не обещание отдельного runtime-теста каждого свойства):

| Control | Discovered | Displayable | Editable | Exportable |
|---|---:|---:|---:|---:|
| Button | 201 | 37 | 29 | 29 |
| DataGrid | 226 | 36 | 27 | 27 |
| Grid | 179 | 29 | 21 | 21 |
| Calendar | 198 | 46 | 39 | 39 |
| NumericUpDown | 207 | 49 | 42 | 42 |
| ListBox | 210 | 44 | 35 | 36 |
| DatePicker | 196 | 48 | 41 | 41 |

252 дополнительных read-only representations **не** включены в editable/serializable/exportable counts. Typed UI и семантика уже reviewed 694 editable pairs улучшены без массового разрешения новых свойств. Общие model aliases не получают fictitious Avalonia metadata. Read-only complex values сохраняются существующим source/model workflow, а не новым ToString codec.

## 11. Функциональная проверка

| Проверка | Результат |
|---|---|
| Solution / regression runner Release | 0 errors; прежний NU1603 только в solution, runner 0 warnings |
| PropertyGrid 3A | 30/30, включая реальные редакторы/native values, source Reset/Infinity и collectibility Row/session |
| Phase 1 metadata | 45/45 |
| Phase 2 controls | baseline 40/40; первый финальный 38/40; после исправления range guard 40/40 |
| Grid-first / Grid Editor / Grid Drop | 26/26, 45/45, 40/40 |
| Nested Layout / Pointer | 59/59, 26/26 |
| Undo cache / Beta data integrity | 25/25, 16/16 |
| Layout 7A.2 | первый 12/13, затем 13/13; enum проверяется реальными routed KeyDown, а не programmatic SelectedItem write-back |
| Preview Consistency / Preview Phase 2 | 32/32, 28/28 |
| Eremex cold / missing theme / missing runtime isolation | 3 отдельных процесса PASS |
| PostgreSQL offline | 20/20; live SKIP |
| SQL Server isolated LocalDB | 47/47; отдельный settings/schema/BindingSource → оба Preview PASS |
| SQL generated runtime | PASS: Grid Customers=3, nested Grid Orders=2, independent VM, видимые реальные значения и resize |
| Preview Memory | 36/36; по 20 циклов Legacy/AXAML, UIA/async/cancellation/real SQL |
| Generated 3A Runtime Grid + Canvas | 2/2 приложений: по 6 native controls, spinner/Calendar/pickers, typed structs/fonts/alpha, GridLength, Reset/default, resize |
| RegistryNative / RegistryCanvas / NestedNative Runtime | 3/3 приложений; 23/23/13 native controls |
| Geometry comparison | 27 сравнений scene/viewport/mode, 0 mismatches, max 0.000 DIP, размеры 800×600, 1200×800, 1600×900 |

Broad regression: **первый 71/72**, второй **71/72** (после снятия первого guard выявился следующий initial/headless Favorites дефект), targeted rendered Favorite check **1/1**, затем полный последний **72/72**. Это отдельные прогоны; первые reds не переписаны. Последний включает DesignerSurface/session selection, Multi Form/New/Open/Save, export/generated apps, Eremex vertical slices и VSIX/VsHost Named Pipes/minimal patch/version conflict/disconnect. VSIX packaging проверен сборкой и smoke; установленная интерактивная Visual Studio вручную не проверялась.

Первые red не скрыты: range validation ошибочно сравнивал изменяемый Maximum с его прежним Maximum; условия исправлены, добавлены raise-Maximum / invalid-Minimum assertions и сохранена точность около decimal.MaxValue. Прежний smoke исходного Inspector обнаружил прямую зависимость от MainWindow: native effective-value read и null dispatch переведены на host events существующего UserControl. Следующая проверка обнаружила отсутствие initial presentation до Window.Show; Context проецируется и до attach без подписки на VM, attach переиспользует уже созданные views. Проверка rendered Favorite command/parameter теперь проходит. Отрицательный UIA control и одиночный Legacy retention описаны отдельно ниже. Development-red во время реализации (неверные contexts prototype, history coalescing, explicit-default reset, повторный текст после Enter) не считаются зелёными; соответствующие regressions теперь включены в 30 checks.

Generated projects сохраняют прежний net6.0 и получают NETSDK1138; TFM автоматически не менялся. Часть broad cleanup ранее сообщала AccessDenied для loaded DLL; PASS относится к функциональной проверке, не к успешному удалению этих файлов. Остатки удаляются из task artifacts после завершения native процессов. Интерактивный installed VSIX, physical mouse/DPI, external screen reader и PostgreSQL live не выдаются за автоматизированные PASS.

JSON/Undo tests проверяют nullable/enum/decimal, struct order, fonts/brush alpha, Reset absence, ControlId/ParentId/Grid placement. Multi Form проверяет независимые values/reset/history и Save/Open. Source tests включают no-op Infinity, один remove span, no namespace insertion, protected bindings/resources и повторные patches/conflicts прежних suites.

## 12. Memory

* Новые WeakReference checks: старый Row после selection и старая document session после New Project освобождаются при живых MainWindow/VM. Recycled controls не сохраняют прежний Row в callbacks. Multi Form contexts/history не смешиваются.
* Отдельный existing Preview memory suite: **36/36**, Legacy/AXAML по **20 Open/Close**, aliveWindows=0, activeWindows=0, clockObservers=0 на checkpoints 5/10/15/20. Проверены DataContext, ItemsSource, DataGrid, UIA, cancelled close, async contract и закрытие во время реального SQL query.
* В первом полном Phase 2 прогоне один закрытый Legacy Preview остался жив после strict GC checkpoint. Lifecycle дошёл до CLOSED/DISPOSED, UIA release был записан. В двух повторных полных 40/40 это не воспроизвелось. Его точный root **не установлен**; нельзя автоматически приписывать ему root другого окна или считать проблему исправленной повтором. Статус: **UNRESOLVED / NOT REPRODUCED on repeat**.
* Отрицательный plain Window control красный в первом stabilization (**24/25**, alive=1/6). В отдельном forensic-повторе **24/25**, alive=3/6. Для всех трёх объектов SOS из dotnet-dump 10.0.750501 показал одну точную цепочку:

```text
ref counted COM handle (RefCount: 1)
  -> Avalonia.Win32.Automation.RootAutomationNode
  -> Avalonia.Automation.Peers.WindowAutomationPeer
  -> Avalonia.Controls.Window
```

Это настоящие ordinary Window, не PreviewWindow и не Inspector cache. Их адреса: `02cc68e896d0`, `02cc68f5c178`, `02cc68f8a8d8`; по одному unique root. Цепочка совпадает с ранее доказанной в BetaReadinessAudit. Оба Preview collectibility tests того forensic-процесса прошли. Native/UIA integration не менялась; общий Win32 accessibility lifecycle остаётся отдельной задачей. Исторический uiautomationcore crash в этом этапе не воспроизведён, причинная связь не утверждается.

Текстовые evidence: `artifacts/universal-phase3a/forensic-1/window-stat.txt`, `window-addresses.txt`, `plain-window-gcroot.txt`, `memory-diagnostics/ready.json`, suite.log. Heap dump **634,639,542 B** удалён после анализа. Диагностический инструмент установлен только в task artifacts; зависимости приложения не менялись. Dump-on-retained helper получил optional пути через env, не включён в production. После проверки локальный diagnostic tool и собственные generated bin/obj удалены; task artifacts уменьшены до 63,996,486 B (логи, observations, fixtures и source проектов сохранены). Первые memory reds сохранены; все suites не объявляются безусловно зелёными.

Кэши schemas содержат shipped Types/metadata, не native controls, DataContext, sessions или VM. Presentation cache ограничен текущими categories/slots; старые contexts не хранятся для будущего использования. Свёрнутый GridLength editor очищает handlers предыдущего Row при rebind. Production GC.Collect не добавлен.

## 13. Изменённые файлы

Всего 30 файлов:

* `Controls/PropertyGridCompoundEditor.cs`
* `Controls/PropertyGridEditorTemplates.cs`
* `Controls/PropertyGridEnumEditor.cs`
* `Controls/PropertyGridGridLengthEditor.cs`
* `Controls/PropertyGridRecyclingTemplate.cs`
* `Controls/PropertyGridTextEditor.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
* `DesignerSystem/Metadata/MetadataCoverageReport.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `Docs/UniversalControlsAndPropertiesPhase3A.md`
* `Docs/UniversalPropertyCoverage.csv`
* `ViewModels/MainWindowViewModel.Metadata.cs`
* `ViewModels/MainWindowViewModel.PropertyMetadata.cs`
* `ViewModels/MainWindowViewModel.cs`
* `ViewModels/PropertyGridPropertyMetadata.cs`
* `ViewModels/PropertyGridRowViewModel.cs`
* `Views/DesignerPropertyInspector.Presentation.cs`
* `Views/DesignerPropertyInspector.axaml`
* `Views/DesignerPropertyInspector.axaml.cs`
* `Views/MainWindow.axaml.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Layout72.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PreviewMemory.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PropertyGrid3A.Checks.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PropertyGrid3A.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase3ARuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/README.md`

Artifacts с logs/benchmarks/generated fixtures игнорируются Git. Изменений csproj/package/TFM, DesignerSurface, Export Pipeline или VSIX/IPC production-кода нет.

## 14. Ограничения и ручная приёмка

* Legacy double-only property adapters не стали автоматически nullable/четырёхкомпонентными. Полное unset для их обязательных полей потребует отдельного совместимого adapter решения, а не скрытого изменения JSON semantics.
* Generic Collection/Nested Object/Template Designer не реализованы; specialized Grid definitions и прежние editors сохранены.
* Arbitrary imported AXAML остаётся partial/source-preserving workflow. Complex values показываются безопасно, без исполнения пользовательских VM/templates ради discovery.
* First compositor update — наблюдаемая граница, не доказательство физических photons/экранного screenshot latency. Реальная мышь, screen readers/external UIA, DPI/мультимонитор, Linux/macOS и installed VSIX требуют отдельной ручной приёмки.
* PostgreSQL live без сервера не называется проверенным. SQL тесты выполняются только в изолированной LocalDB.
* Manual checklist: keyboard/mouse enum dropdown, долгий текст/Enter/повторный ввод/Escape, compound focus switching, Color picker alpha/null/reset, scroll/Favorites/search после смены формы, source с Resource/Binding/Style, реальный VSIX ApplyPatch/conflict и внешняя UI Automation.

## 15. Рекомендации Phase 3B

1. Специализированные Collection/Nested Object editors поверх того же capability/ownership слоя.
2. Постепенные typed overrides legacy scalar fields с доказанной совместимостью всех native/export adapters; не массовая замена model defaults.
3. Расширение безопасного literal escaping и source-reset coverage отдельными проверяемыми группами.
4. Если performance tails остаются значимыми — отдельное измерение DataGrid selection/diagnostic/layout stages, затем только подтверждённая локальная оптимизация.

**PROPERTYGRID FOUNDATION: READY WITH CONDITIONS.** Typed editors, source safeguards, native/runtime behaviour и measurable UI reuse подтверждены. Условия: отдельная приёмка external UIA/physical UI/DPI/installed VSIX, расследование единичного Legacy retention и дальнейшее расширение legacy unset/complex capabilities отдельными этапами. Общая platform/UIA проблема не объявляется исправленной этим PropertyGrid этапом.

Phase 3B автоматически не начинается.
