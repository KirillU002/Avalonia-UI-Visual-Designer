# Layout Designer Phase 7A.2 — измерения, Toolbox Drop и native crash

Дата: 1 ноября 2026. Исходный commit: `cbb78e330da319dc589cacea3c6caea8042d2060`. Рабочее дерево перед началом было чистым. Изучены Phase 7A, Phase 6, Phase 5 и PreviewMemoryLeakFix. Canvas остаётся вариантом по умолчанию. Phase 7B не начата.

## Результат и границы

**Grid-first: NOT READY.** Устранены подтверждённые полные перестроения в ограниченных сценариях Canvas, DockPanel и leaf reparent; уменьшена стоимость Inspector. Это не означает, что весь Drop стал incremental: служебная native layout projection и полные snapshots текущей формы остаются дорогими. Move на 500 элементах не ускорился. Native crash локализован в UI Automation, но причинная цепочка не доказана.

Главные цифры Release, 500 моделей, median/P95 до первого compositor commit:

| Операция | Before, ms | After, ms | UI allocations before → after, MiB |
|---|---:|---:|---:|
| Toolbox → Canvas | 624.3 / 667.5 | 110.4 / 142.5 | 141.55 → 42.98 |
| Toolbox → DockPanel | 600.0 / 654.9 | 211.3 / 277.6 | 163.27 → 74.98 |
| Reparent | 636.6 / 680.5 | 260.9 / 296.6 | 161.33 → 73.53 |
| Toolbox → Grid | 283.2 / 337.1 | 255.9 / 364.1 | 87.14 → 79.91 |
| Move | 200.5 / 364.0 | 212.6 / 345.5 | 62.78 → 63.01 |

У Grid улучшилась медиана, но ухудшился P95. Нельзя объявлять эту часть стабилизированной. Даже после warm-up остаются GC, JIT/tiering и выбросы времени compositor. Цель 100–150 ms выполнена не для всех типичных контейнеров и не для больших native-layout форм.

## Методология benchmark

- Release, без debugger, настоящий Avalonia Windows backend; runtime .NET 8.0.32, SDK 10.0.402; Windows 10.0.19045, AMD64 Family 25 Model 33, 16 logical processors, workstation GC.
- Один свежий процесс для каждой пары scenario/count; процессы идут последовательно. Полные before и after: по 28 процессов, в каждом первый Drop + 3 warm-up + **30 измерений** = 840 steady измерений на вариант, 1680 суммарно. Промежуточные проверки, CPU trace и Inspector ablation в эти матрицы не входят.
- Document viewport 1200×800 DIP, Zoom 100%, RenderScaling=1, одинаковые fixtures и точки размещения. After дополнительно сохраняет actualZoom, root size, ClientSize (1600×920); viewport не равен размеру внешнего окна. Новые control models одного типа Button 75×28; N включает контейнеры. Grid: десять Star колонок; Stack вертикальный, Wrap горизонтальный; Dock сохраняет LastChildFill. Часть большого Dock/Stack содержимого естественно выходит за viewport — это synthetic fixture, а не гарантия стоимости произвольного реального проекта.
- Toolbox: настоящий routed DragOver/Drop через DesignerSurface. Move/Reparent вызывают существующий production CommitGridControlDrop; реальная Down/Move/Up маршрутизация проверяется отдельными functional suites. Это **не hardware-mouse/native OLE Drag & Drop benchmark**.
- DragOver и ожидание его frame — вне Drop clock, но `HoverMs` сохранён. Это существенная оставшаяся задержка: для native контейнеров около 130 ms на 500 моделях. Её нельзя скрывать в отчёте о пользовательском ощущении DnD.
- Hover здесь первый после Undo/reset модели; это не стоимость каждого PointerMoved при уже валидном geometry cache. Повторное hover без model changes этой матрицей отдельно не измерялось.
- Clock: Drop handler → соответствие wrappers текущим моделям → attached Window.UpdateLayout → Compositor.RequestCommitAsync → следующий callback при DispatcherPriority.Input. Dispatcher timer не исключён из Move/Reparent времени. Нельзя считать возвращение handler окончанием обновления UI.
- `FirstCommitMs` — commit fence Avalonia compositor после layout; это нижняя измеримая граница обновления, **не доказательство физического появления пикселя на мониторе**. Время аппаратного scanout и OS scheduling не измерялось.
- Undo, возврат selection, ожидание восстановленных model references/native measurement key/frame — вне clock. Fixture не накапливает controls. Не вызывается принудительный Refresh внутри измеряемой операции.
- UI allocations — GC.GetAllocatedBytesForCurrentThread, process allocations — GC.GetTotalAllocatedBytes. GC deltas учитываются только внутри измеряемого интервала; reset и setup исключены. Это объём аллокаций, не retained heap/working set. MiB = 1048576 bytes.
- Обе матрицы используют scoped profiling. After дополнительно считает affected IDs/EventSource stages; этот overhead не вычитается из времени/allocations. Это сравнение инструментированных вариантов, а не утверждение о нулевой стоимости диагностики. Небольшое изменение Move нельзя приписывать одной причине без отдельного unprofiled A/B.
- Quantile — nearest rank (`ceil(q*N)-1`), median для N=30 — 15-й отсортированный sample, P95 — 29-й. Min/max и все выбросы сохранены; временных pass/fail порогов в CI нет.
- Первый Drop выполняется после показа окна и начального layout/templates; `showMs` включает 150 ms setup pump. Это cold **operation**, не полный process-start benchmark. После трёх warm-up JIT не прекращается: steady здесь означает повторяемую эксплуатационную последовательность, а не JIT-free microbenchmark.
- Тяжёлые restore/build и regression runners не запускались одновременно с матрицами. Before/after идут раздельно, без рандомизации; OS/background/tiering вариативность полностью не устранена. Это ограничивает причинное толкование небольших изменений вроде Move.

Исходные данные: `artifacts/layout-phase7a2/{before,after}-<Scenario>-<N>.json`; 34 observations в каждом, stages/counters/allocations/GC/JittedMethods. SHA256 DLL сохранены в before-binary-hashes.json/after-binary-hashes.json. Производственная before реализация — исходный commit с измерительным harness; after — текущая реализация. Сборки baseline и final — 0 ошибок. Промежуточный after-v1 перенесён в отдельный каталог и в итоговую статистику не включён.

Воспроизведение из корня репозитория (не запускать до завершения сборки):

```powershell
dotnet build FormDesigner.sln -c Release -m:1
dotnet build smoke-tests/FormDesigner.ExportSmokeTests -c Release -m:1
& smoke-tests/LayoutGrid/BenchmarkDrop.ps1 -Label current -Samples 30
# Для таблиц опубликованной пары before/after:
py -3 smoke-tests/LayoutGrid/SummarizeDropBenchmark.py artifacts/layout-phase7a2
```

Для сравнения новой пары сохранить её JSON под labels before/after в отдельном каталоге. Не перезаписывать исходные результаты. Профилирование opt-in: `FORMDESIGNER_LAYOUT_DROP_PROFILE=1`; подробные stacks дополнительно `FORMDESIGNER_LAYOUT_DROP_STACKS=1`. Benchmark использует value-only profile без записи большого diagnostic log внутри каждого измерения.

## Почему прежние 50 controls могли быть медленнее 500

Исторические одиночные замеры Phase 7A не содержали достаточных JIT/GC/quantile данных. Нельзя задним числом приписать разницу 357/369/217/249 ms одному фактору. Новая before-матрица с одинаковыми fixtures и Release в основном показывает ожидаемый рост с N; сравнение с историческими Debug/single-shot числами не является A/B.

В новой матрице напрямую зафиксированы: сотни/тысячи JIT compilations в первом Drop, меньший, но ненулевой JIT в steady; Gen2 collections; разная стоимость двух Undo snapshots; длительное ожидание commit. Примеры after, оставленные в выборке:

- Dock50 iteration10: 739.5 ms; handler 680.0 ms, SelectNewControl 386.5 ms; 21 JIT, GC1/1/0. Эти счётчики не доказывают единственную причину паузы.
- Wrap500 iteration23: 537.1 ms, из них FirstCompositorCommit 340.7 ms; 289 JIT, GC5/4/2. Нельзя назвать всё это временем CPU compositor.
- Move500 iteration27: 370.2 ms, snapshots 236.1 ms, GC3/3/2. Та же архитектура snapshots остаётся источником непредсказуемых пауз.

Полные таблицы приведены в приложении ниже.

## Подтверждённые bottlenecks и изменения

### Inspector: скрытые редакторы создавались вместе с видимыми

Каждая строка AXAML Inspector раньше создавала весь набор TextBox/ComboBox/CheckBox/color/action/read-only editors и скрывала неподходящие. Строки и bindings были корректны, но это увеличивало template/style/layout работу. CPU sample trace показывает ContentPresenter.ApplyTemplate/UpdateChild, property dictionary/binding publication и StyleBase.Attach; временная ablation с полностью скрытым Inspector даёт Wrap50 median 36.4 ms против 128.1 ms до исправления. Это диагностический эксперимент, **Inspector в продукте не отключён**.

Изменение: существующие editors вынесены в DataTemplates; ContentControl выбирает единственный template по EditorKind. Сохранены исходные bindings, handlers, validation, favorites, tooltips и категории. Text/Number используют существующий PropertyGridTextEditor, Enum/BindingSource — ComboBox, коллекции — прежние action editors. Unsupported editor kinds не получили фиктивной функциональности.

Reparent дополнительно вызывал RebuildPropertyGrid("GridDropMove") после ParentId notification, уже обновившего категории. Удалён только дублирующий caller. В тесте и профиле: 2 → 1 refresh, 54 → 27 созданных rows. Generic ApplyDocument не отключался; пустые delayed refresh не добавлялись.

### DockPanel и leaf reparent: чрезмерно строгий reuse guard

Phase 7A fast path требовал неизменной иерархии и размеров всех wrappers. DockPanel меняет размер последнего заполнителя при добавлении leaf; reparent меняет parent. Один такой переход включал full render для всей формы. На 500 моделях before: Dock — 500 созданных wrappers; Reparent — 500. Это подтверждено factory counters и identity checks.

Теперь после проверки всех IDs, model references, host tree и фактических native bounds разрешено:

- перенести **тот же leaf wrapper** между существующими child hosts;
- заменить только leaf с изменившимися размерами/содержимым;
- создать/удалить leaf wrapper при переходе из нулевых bounds в положительные и обратно;
- сохранить wrappers остальных controls.

Если меняется geometry контейнера, тип/иерархия неподдерживаемой структуры или authored state — остаётся безопасный full renderer. Перед мутацией выполняется validation, lookup IDs — HashSet; O(N²) поиска model по ID в цикле не добавлено. Offscreen Avalonia projection по-прежнему создаётся полностью, её стоимость отдельно видна в профиле.

Наблюдение про якобы второй RenderDesigner на reparent оказалось особенностью старого тестового helper: он сам вызывал f.Refresh после PointerUp. Продукт не исправлялся на основании этой ложной причины. Новый regression использует сырые routed Down/Move/Up + frame.

### Legacy Canvas: добавление одного plain control пересоздавало всю форму

В узком безопасном случае (Absolute root, плоские Button/TextBox/TextBlock без data/text binding, нет импортированной AXAML-сессии/Runtime mode/незавершённого authored render/multiselection) append создаёт один wrapper. Старый primary selection frame/grips/ZIndex обновляется, badges/minimap/selection overlay синхронизируются. Rect — ровно прежний X/Y/Width/Height, без coordinate corrections. До: 501 wrapper, после: 1 новый + 1 обновлённый предыдущий selection wrapper. Для nested/mixed Canvas остаётся прежний путь.

### DataGrid/Eremex и безопасность reuse

Для Standard DataGrid, Eremex.DataGridControl и Eremex.TextEditor используется точный private in-memory key: clone модели с нормализованными placement fields + clone BindingSource + revision preview cache + selection role Standard DataGrid. **Ключ никогда не логируется/сохраняется/экспортируется**; он может содержать локальную конфигурацию подключения в памяти, как и исходная модель. Неизменные content/bounds сохраняют control и ItemsSource, изменение метаданных/preview cache/selection/размера обновляет затронутый leaf. Неподдерживаемые plugins/items/bound controls сохраняют fallback.

Ключ снимается после preview factory. Cache revision изменяется при clear/load/failure; ключи очищаются с wrapper map при reset/full render. Revision пока общая: обновление одного preview cache консервативно инвалидирует все data-preview keys, а не только одну BindingSource. Проверены реальные Eremex plugin types, изменение EditorValue и refresh preview cache. Standard Designer DataGrid остаётся своим существующим designer skeleton; настоящие DataGrid/ItemsSource проверяются в обоих Preview. Матрица производительности использует Button fixtures: её latency нельзя переносить на формы из сотен DataGrid/Eremex controls.

Новый Drop hint не может скрыть ожидающую authored правку: generic DesignerChanged отмечает pending state, hint его не сбрасывает. При таком состоянии fast path запрещён, существующий renderer показывает новое содержимое. ControlId, ParentId, bindings, Logic, JSON format и Undo architecture не менялись.

## Подробный профиль ToolboxGrid500 после исправлений

Медианы по 30 операциям. Stage durations/allocations **inclusive и вложены**, их нельзя суммировать. Tracked objects — только factory roots, wrappers, menus и Inspector rows; не полный census CLR/visual descendants. Affected — уникальные IDs, явно помеченные в stage; 0 означает отсутствие инструментации IDs, а не отсутствие работы.

| Stage | Calls | ms | MiB | Tracked objects | Affected IDs |
|---|---:|---:|---:|---:|---:|
| ResolveDropTarget | 1 | 0.06 | <0.01 | 0 | 0 |
| ValidateDrop | 1 | 0.06 | <0.01 | 0 | 0 |
| CreateUndoSnapshot | 2 | 24.73 | 20.13 | 0 | 0 |
| CreateUndoTransaction | 1 | 3.14 | 2.82 | 0 | 0 |
| RebuildStructureTree | 1 | 0.61 | 0.37 | 0 | 0 |
| RefreshPropertyInspector | 1 | 9.39 | 1.71 | 30 | 0 |
| SelectNewControl | 1 | 22.93 | 6.03 | 30 | 0 |
| CreateControlCore | 1 | 26.00 | 7.80 | 30 | 1 |
| CommitUndoBatch | 1 | 24.53 | 21.07 | 0 | 0 |
| CommitToolboxDrop | 1 | 55.53 | 32.44 | 30 | 1 |
| BuildNativeProjection | 1 | 4.34 | 5.24 | 501 | 0 |
| MeasureArrange | 1 | 49.40 | 20.92 | 0 | 0 |
| CreateDesignerWrappers | 1 | 0.14 | 0.03 | 2 | 0 |
| UpdateExistingWrappers | 1 | 4.50 | 1.02 | 2 | 501 |
| RebuildVisualTree | 1 | 70.69 | 30.64 | 503 | 501 |
| DropEvent | 1 | 147.09 | 63.38 | 533 | 501 |
| DispatcherToVisual | 1 | 0.18 | <0.01 | 0 | 0 |
| AttachedUILayout | 1 | 79.23 | 14.60 | 0 | 0 |
| FirstCompositorCommit | 1 | 19.25 | 2.05 | 0 | 0 |
| NextInputReady | 1 | 10.90 | <0.01 | 0 | 0 |

`RebuildVisualTree` — историческое имя общей render-функции, включая incremental branch. Оно **не означает** пересоздание всех Designer wrappers. Здесь native projection создала 501 служебный control, новый wrapper — 1; placement assigned501 включает 500 старых и 1 новый wrapper. ApplyDocument в measured Drop не вызывается. Model update использует существующую active DocumentSession, а не загружает JSON заново.

Каждая observation дополнительно хранит LayoutPasses (число LayoutUpdated событий attached Window); это не точный счётчик каждого внутреннего Measure/Arrange call. UpdateLayout вызывается на attached-layout этапе и перед commit fence; второй вызов при valid layout обычно не выполняет новый полный проход. Никакие реальные layout semantics ради benchmark не отключались.

Undo остаётся атомарным: native BeginBatch flush + commit дают два полных сериализованных snapshots текущей формы, не всего Multi Form workspace. На Grid500 это 20.13 MiB внутри CreateUndoSnapshot и ещё 2.82 MiB DTO transaction creation (вложены в общие totals). На Move500 — 30.69 MiB и 66.2 ms median только snapshots. Убирать Begin flush по одному revision number небезопасно для non-observable property bags, поэтому snapshots не пропущены и command-based Undo не внедрялся.

События opt-in: TOOLBOX_DROP_PROFILE_START/STAGE/END, LAYOUT_FIRST_RENDER_COMPLETED, LAYOUT_VISUAL_WRAPPERS_REUSED/CREATED, PROPERTY_INSPECTOR_REFRESH_PROFILE; старые LAYOUT_DROP_PROFILE_* сохранены. У REUSED поле Count передаёт существующий DesignerWrappersUpdated counter (число обновлений в incremental pass, включая новый wrapper), а не точный census уникальных старых wrappers; identity проверяется regression отдельно. First-render event появляется только при реально измеренном fence, например в harness; завершение production handler не объявляется презентацией кадра. Profiling records содержат duration/allocations/calls/affected/counters/EndMs. Stack capture выключен по умолчанию. Не добавлены постоянные LayoutUpdated/dispatcher callbacks на каждый Drop.

CPU trace: локальный dotnet-trace 9.0.652701 в artifacts/layout-phase7a2/tools (не dependency Designer). `cpu-before.nettrace` собран отдельно от матрицы. Первоначальный EventSource manifest не дал usable interval events; UI callstack filter включает setup/reset и не является точной процентной атрибуцией Drop. Общепроцессные top stacks содержат ожидания I/O/compositor и не трактуются как CPU bottleneck. Новая opt-in self-describing EventSource проверена отдельным EventPipe transport probe: получен Stage с structured payload id/name/elapsedMs; reader учитывает вложенную TraceLogging structure (1 interval). Неудачный запуск probe в Windows PowerShell 5 сохранён отдельно: EventPipe требует .NET Core, повтор в PowerShell 7 успешен. Доказательство изменений здесь — stage timings/counters, identity regression и Inspector ablation, а не выдуманные CPU percentages.

Заключительный `cpu-after.nettrace`: Wrap100, свежий Release процесс, 1 first + 3 warm-up + 3 diagnostic samples, отдельно от matrix/builds. **161 Stage intervals** действительно декодированы и сопоставлены с thread/timestamps sample profiler; reset вне scope. Эти семь операций включают cold/warm-up, поэтому их CPU sample counts не являются steady median/P95 и не сравниваются процентами с before callstack-filter trace.

| Stage | Intervals | Совпавшие UI samples | Наблюдаемые stacks |
|---|---:|---:|---|
| AttachedUILayout | 7 | 335 | MeasureCore304, ContentPresenter.MeasureOverride205, TemplatedControl.ApplyTemplate161; exclusive PublishNext86/PropertyDictionary.InsertEntry65 |
| RefreshPropertyInspector | 7 | 98 | RebuildPropertyGrid/selection notification; exclusive PublishNext24, List.Remove/RemoveAt |
| MeasureArrange | 7 | 79 | UpdateNativeLayoutMeasurements/MeasureNativeDesignerLayout; exclusive InsertEntry18/PublishNext15/StyleBase.Attach6 |
| CreateUndoSnapshot | 14 | 57 | RegisterHistorySnapshot; Deserialize24/Serialize21 inclusive samples; JSON read/write/copy/hash exclusive |

Этот trace подтверждает оставшуюся template/property/layout/serialization работу, но не позволяет отнести любой ContentPresenter к Inspector: в attached tree есть и контролы формы. Полные имена frames и counts сохранены в cpu-stages-after.jsonl. Новая модель incremental projection не внедрялась без отдельной invalidation/lifecycle проверки.

## Native crash: доказательства и ограничение анализа

Найден исходный WER dump `C:\Users\Kirill\AppData\Local\CrashDumps\dotnet.exe.20120.dmp`; он не копировался в репозиторий, поскольку содержит память процесса. Исторический log Phase7A записан 2026-10-31T19:20:58.792Z. Application Error1000 в 22:20:59 МСК: dotnet.exe PID20120, `0xC0000005`, `uiautomationcore.dll` 7.2.19041.5794, offset `0x47593`. WER report ID `b25a1234-3096-4e03-abb8-136a988e6c49`. Dump timestamp 22:21:01 МСК, process uptime 49 seconds.

SHA256 dump: `C2B441BD8F83132617D74A69F5771C0A7E5E3924A3BFE824B83320F0E44F492E`.

Read-only разбор MINIDUMP exception/module streams (формат [Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_exception_stream)) и установленный системный DbgEng дали:

```text
OS thread 40328 / 0x9d88, debugger thread 33
rsi = 0
uiautomationcore + 0x47593: mov rax, qword ptr [rsi]
Exception: read at address 0, 0xC0000005
uiautomationcore internal frames
kernel32!BaseThreadInitThunk
ntdll!RtlUserThreadStart
```

Доступны export symbols, поэтому отображаемое `UiaReturnRawElementProvider+0x17f03` — ближайший export, **не доказанное имя внутренней аварийной функции**. В faulting stack нет managed/Avalonia/Eremex frames. Avalonia, Eremex, Skia/OpenGL/Vulkan и UIA modules были загружены — это не причинное доказательство. Связь с прежним UI Automation provider lifecycle возможна, но dump не доказывает caller/порядок release; animation clock также не объявляется причиной. `!analyze -v` недоступен из-за отсутствующих debug extensions/secure dbghelp loading; полноценная PDB/native heap/race analysis не выполнена.

Сохранены sanitized exception/module metadata, native stack и совпадающие Event Log records в artifacts/layout-phase7a2. Наличие записи от другого PID с managed exception не смешивалось с этим crash.

Минимальная диагностика: `FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY=<local path>` включает ограниченный 2 MiB JSONL на PID: UTC/native thread ID, Preview ID/mode/active count, startup/managed-unhandled/process-exit и один module-version snapshot при первом Preview. Нет Window references, exception messages, argv, env dump или connection strings. По умолчанию файл и expensive module enumeration не создаются. Это **не** перехват/восстановление native AV; WER/external debugger по-прежнему нужен. Registry/system debugger settings не менялись, нативная интеграция и прошлые UIA/animation fixes не переписывались.

Тестовый opt-in `FORMDESIGNER_PREVIEW_FORCE_UIA=1`: клиент на отдельном MTA thread **того же runner-процесса** получает настоящий UIA node HWND Preview через UiaNodeFromHandle, удерживает до Closed и освобождает на MTA. Логируется acquire/release. Это проверка held-node lifetime, не out-of-process screen reader, subscribed-event или несколько UIA clients.

Статус: **UNRESOLVED / NOT REPRODUCED**. Место сбоя найдено; точный root cause и исправление native race не заявлены.

Повторное UIA stress: три отдельных процесса PID21772/20204/4356, каждый **36/36**, exit0, новых WER dumps нет. Суммарно **348 acquired / 348 released UIA nodes**, release failures0; в каждом 116 Opened/116 Disposed. Оба режима включали по 20 Open/Close циклов; WeakReference на Window/DataContext/ItemsSource/Grids и animation observer assertions не ослаблены. Это ограниченный positive stress result, не доказательство отсутствия редкой гонки у других UIA клиентов.

## Regression и runtime

Исходный build и финальные Debug/Release solution builds: **0 errors, 1 существующий NU1603** (VS SDK Analyzer 17.7.20 → 17.7.22). NuGet/TargetFramework не изменялись. Runner build: 0 errors/0 warnings. SHA256 финальных Release app/runner совпадают с DLL итоговой after-матрицы; документация и отдельный trace reader после неё не меняли эти binaries.

Новые 13 deterministic проверок: один активный Inspector editor; Enter/text/enum binding после смены selection; raw routed reparent + один Inspector refresh + atomic Undo/Redo; Dock только affected leaf; сохранение настоящих Eremex preview controls; invalidation changed content и preview cache; обе runtime DataGrid Preview ветви с явными local rows; diagnostic events/fence; 10 циклов Drop/Undo/Redo/Undo без overlays/wrapper accumulation; Canvas append/X/Y/selection frame; pending authored text перед Drop. Проверки времени вынесены в benchmark, functional assertions не ослаблены.

| Suite | Результат |
|---|---:|
| Phase 7A.2 deterministic | 13/13 |
| Phase 7A UX/Zoom/badges | 15/15 |
| Nested Layout Phase5 | 59/59 |
| Grid Drop Phase4 | 40/40 |
| Grid Editor Phase3 | 45/45 |
| Native Grid Phase2 | 28/28 |
| Designer Pointer/Marquee | 26/26 |
| Phase6 geometry validation | 55/55 |
| Stabilization audit (включая два прежних memory regressions) | 25/25 |
| DataGrid offline contracts | 18/18 |
| PostgreSQL offline contracts | 20/20; live SKIP |
| Broad export/toolbox/settings/DLL/JSON/AXAML/VSIX/VsHost/IPC | 72/72 |
| Layout exports Phase2/3/5/6, generated restore/build | 13/13 |
| Multi Form ZIP export | 1/1 |
| SQL Server DataGrid Preview, contract+live | первый 42/43; повтор 43/43 |
| Preview memory + live LocalDB | 36/36 |
| Preview memory + live LocalDB + forced UIA | 36/36 × 3 процесса |

SQL fixture: создан новый изолированный LocalDB instance `FormDesignerPhase7A2_20261101`, база `FormDesignerPhase7A2`, `smoke-tests/SqlServer/DataGridPreviewFixture.sql`, Windows authentication, без пароля. Customers содержит три различающихся SQL значения, Orders два, EmptyTable пустая. Старый instance предыдущего этапа уже отсутствовал; пользовательские БД не использовались. Никакое системное ПО не устанавливалось.

После live/runtime проверок собственная база удалена через DROP DATABASE, instance остановлен и удалён; localdb-cleanup.log сохранён. Для повторения нужно заново создать изолированную fixture и передать её подключение через FORMDESIGNER_TEST_SQLSERVER.

Первый live прогон: `LIVE_SQL_Legacy_single` получил `counts=[]` в момент checkpoint, все остальные 24 live checks и 18 contracts прошли. Повтор того же suite без изменения assertions — **43/43**, включая первый Legacy. Это **не исправленный дефект**: первый log сохранён, причина единичного отсутствия DataGrid не доказана (в том числе нельзя без измерения списать его на cold connection/20-second harness deadline). Добавлено в remaining limitations; statements о «всех прогонах зелёных» нет.

Memory suites проверяют реальные native DataGrid visual cells, отдельные источники, empty/multiple/multiform, owner/cancelled close, async late completion, query cancellation и реальные SQL строки. Принудительный GC применяется только существующими тестами WeakReference, в production GC.Collect не добавлен. Ten-cycle wrapper test подтверждает стабильность map/overlay counts и IDs; это не полный heap/event census всех возможных Designer plugins.

Runtime ZIP: **PASS** в отдельном процессе, четыре формы (Canvas/Grid/Nested/DataGrid), шесть переходов OpenForm/BackHome, независимые DataContext, Button Logic, реальные стандартные Flyout/ListBox, Nested Stack/Wrap. DataGrid с шестью явно заданными local fixture rows растёт с **1200×672 до 1440×812 DIP**. Источник этой runtime-проверки не выдаётся за SQL.

Отдельный **live SQL generated application**: restore/build/run PASS, два окна Customers=3/Orders=2, реальные различающиеся значения видны в native DataGrid cells, независимые ViewModels. Использована только тестовая LocalDB с Windows authentication; никаких production credentials/серверов. Следовательно, Export проверен не только компиляцией. Production export/AXAML generator не менялись.

У generated ZIP build после возврата исходников 0 errors/5 warnings: прежний net6.0 NETSDK1138 и четыре CS8618 поля title. Instrumented probe build имел 7 warnings. Эти warnings не смешиваются с одним NU1603 solution build; TargetFramework и генератор ради их устранения не менялись. Probe временно добавляет runtime assertions в распакованную копию, затем восстанавливает исходные файлы; исходный ZIP не модифицируется.

Автоматическая уборка broad-run после 72 PASS дала IOException file-in-use у каталога GridLayoutExport. Все scenario/runner exit codes — 0; после выхода процесса повторная уборка **того же проверенного owned path** успешна. Это зафиксировано отдельно от результатов сценариев. Source ZIP и текстовые/JSON evidence сохранены; generated bin/obj и повторные ETLX-кэши удаляются после анализа. Исходный WER dump не удалялся.

Интерактивная Visual Studio с физическим пользователем не проверялась: VSIX packaging/bridge dependency closure, VsHost startup, named-pipe lifecycle/ACK/version conflicts и source patches проверены automated suite. Физический mouse/OLE DnD и DPI/несколько мониторов не заявлены как PASS. Live PostgreSQL сервера нет — только contracts и generated restore/build.

## Изменённые файлы

- `Controls/PropertyGridEditorTemplates.cs`, `Views/DesignerPropertyInspector.axaml`: создание только нужного editor template.
- `Views/MainWindow.LayoutUx.cs`, `Views/MainWindow.axaml.cs`: guarded incremental wrappers/Canvas append/content invalidation/pending authored guard и counters.
- `ViewModels/MainWindowViewModel.GridDrop.cs`: leaf reparent hint и устранение второго Inspector refresh.
- `ViewModels/MainWindowViewModel.GridEditor.cs`, `ViewModels/MainWindowViewModel.cs`: stages/model affected IDs, без изменения Undo/model semantics.
- `Services/LayoutDropProfile.cs`, `Services/LayoutDropEventSource.cs`: opt-in profiling, counters и EventSource.
- `Services/NativeCrashDiagnostics.cs`, `Program.cs`, `Views/PreviewWindow.Lifecycle.cs`: value-only opt-in crash journal.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, `Program.DropBenchmark.cs`, `Program.Layout72.cs`, `Program.PreviewUia.cs`, `Program.PreviewMemory.cs`: dispatch, matrix, deterministic regressions, optional UIA stress; прежние memory assertions сохранены.
- `smoke-tests/LayoutGrid/BenchmarkDrop.ps1`, `SummarizeDropBenchmark.py`, `RunPreviewCrashDiagnostics.ps1`, `InspectNativeDump.py`: воспроизводимость измерений/анализа.
- `smoke-tests/NativeDumpProbe/{NativeDumpProbe.csproj,Program.cs}`, `smoke-tests/DropTraceReport/{DropTraceReport.csproj,Program.cs}`: отдельные диагностические utilities, не solution/app dependencies. TraceReport использует локальные DLL установленного dotnet-trace; для повторения нужен тот же локальный tool.
- `Docs/LayoutDesignerPhase7A2.md`: этот отчёт.

Ни существующие package manifests/TFM, ни SQL/PostgreSQL services, ни Export/AXAML patch generation, ни VSIX/IPC архитектура не менялись.

## Оставшиеся ограничения и следующий отдельный этап

1. Native projection/MeasureArrange создаёт O(N) controls и измеряет всё дерево. Нужен отдельный эксперимент по session-scoped reuse projection и корректной invalidation для content/size/parent/data/zoom; не простой cache без lifecycle доказательств. Designer wrappers reuse уже работает — оптимизировать нужно также служебное дерево.
2. Undo snapshots остаются O(N), включая allocations/GC. Сначала исследовать безопасное исключение повторной DTO materialization/сериализации с сохранением non-observable bags и atomic rollback, а не сразу менять Undo на команды.
3. DragOver/native hit-testing на 500 controls всё ещё дорогой и не входит в Drop latency. Исследовать projection reuse между hover и commit с fingerprint validation.
4. GC/tiering/compositor outliers остаются. Для небольших разниц нужны дополнительные независимые/рандомизированные matrix runs и GC pause/JIT event correlation, не удаление неудобных samples.
5. Inspector больше не создаёт скрытые editors, но новая selection всё ещё создаёт реальные rows/templates. Оставшийся attached UI layout 60–80 ms на больших формах требует отдельного профиля; виртуализация не внедрялась вслепую.
6. Native UIA crash не закрыт. Для причинного вывода нужен повтор с journal + dump + matching symbols и workload UIA clients. Нет оснований отключать accessibility или объявлять виновным Eremex.

Manual checklist: hardware mouse Toolbox → все пять контейнеров и nested containers; Escape/Alt+Tab/release outside window; 50–200% Zoom, физические DPI100/125/150/200 и перенос между мониторами; самостоятельный right-click после reuse; Inspector color/collection/favorites; resize после repeated drops; реальный screen reader/UIA event client при 20 Preview циклах; Save/Open/Multi Form/Undo после длинной сессии. Физический DPI/multi-monitor и hardware OLE DnD этим автоматическим прогоном не сертифицированы. PostgreSQL live без тестового сервера не проверялся.

**Итог: NOT READY для Grid-first по умолчанию.** Полезные измеримые улучшения внесены, но 500-control native Drop/Move и unresolved native crash не позволяют снять условия готовности. Canvas default сохранён, Phase 7B не начата.

## Приложение: полная before/after матрица

### First compositor commit, ms (30 steady samples per cell)

| Scenario | N | Before median / P95 | Before min / max | After median / P95 | After min / max |
|---|---:|---:|---:|---:|---:|
| ToolboxGrid | 50 | 140.8 / 177.0 | 117.6 / 194.9 | 121.0 / 154.5 | 98.0 / 179.5 |
| ToolboxGrid | 100 | 147.0 / 166.5 | 128.8 / 166.7 | 126.6 / 151.6 | 108.8 / 185.0 |
| ToolboxGrid | 300 | 191.9 / 233.0 | 170.8 / 237.5 | 168.4 / 183.0 | 146.0 / 189.8 |
| ToolboxGrid | 500 | 283.2 / 337.1 | 223.4 / 343.4 | 255.9 / 364.1 | 215.0 / 414.2 |
| ToolboxStackPanel | 50 | 122.0 / 160.0 | 107.8 / 196.5 | 124.6 / 146.2 | 96.8 / 320.7 |
| ToolboxStackPanel | 100 | 140.5 / 199.7 | 116.6 / 222.8 | 110.6 / 146.8 | 97.7 / 162.7 |
| ToolboxStackPanel | 300 | 194.1 / 211.8 | 160.2 / 228.9 | 154.2 / 191.1 | 134.1 / 194.9 |
| ToolboxStackPanel | 500 | 266.6 / 362.7 | 217.9 / 488.8 | 236.3 / 300.0 | 201.7 / 387.7 |
| ToolboxDockPanel | 50 | 152.4 / 196.3 | 135.3 / 305.1 | 116.5 / 206.2 | 91.1 / 739.5 |
| ToolboxDockPanel | 100 | 207.8 / 239.8 | 171.5 / 243.5 | 115.7 / 157.1 | 97.4 / 175.9 |
| ToolboxDockPanel | 300 | 361.7 / 416.1 | 324.9 / 459.2 | 163.2 / 210.4 | 128.4 / 225.4 |
| ToolboxDockPanel | 500 | 600.0 / 654.9 | 467.1 / 656.1 | 211.3 / 277.6 | 198.7 / 371.7 |
| ToolboxWrapPanel | 50 | 128.1 / 206.2 | 106.1 / 294.3 | 116.4 / 139.7 | 91.9 / 147.4 |
| ToolboxWrapPanel | 100 | 133.5 / 165.2 | 121.7 / 196.2 | 115.9 / 140.5 | 97.8 / 168.6 |
| ToolboxWrapPanel | 300 | 197.1 / 227.4 | 154.3 / 264.2 | 151.3 / 199.5 | 132.5 / 203.5 |
| ToolboxWrapPanel | 500 | 242.7 / 288.6 | 209.5 / 305.1 | 218.4 / 272.8 | 200.5 / 537.1 |
| ToolboxCanvas | 50 | 169.7 / 207.9 | 140.6 / 255.0 | 84.2 / 109.2 | 54.5 / 135.6 |
| ToolboxCanvas | 100 | 211.8 / 243.7 | 194.0 / 302.8 | 97.9 / 116.0 | 60.1 / 116.3 |
| ToolboxCanvas | 300 | 403.5 / 443.3 | 388.6 / 552.3 | 107.8 / 134.6 | 73.7 / 157.9 |
| ToolboxCanvas | 500 | 624.3 / 667.5 | 577.3 / 808.4 | 110.4 / 142.5 | 97.4 / 146.8 |
| Move | 50 | 72.9 / 109.0 | 55.0 / 115.8 | 72.8 / 97.4 | 54.7 / 102.9 |
| Move | 100 | 84.7 / 116.0 | 62.7 / 127.5 | 73.2 / 91.2 | 66.6 / 140.0 |
| Move | 300 | 151.2 / 188.5 | 127.4 / 207.6 | 140.5 / 188.2 | 121.8 / 200.3 |
| Move | 500 | 200.5 / 364.0 | 151.2 / 375.7 | 212.6 / 345.5 | 170.0 / 370.2 |
| Reparent | 50 | 188.3 / 273.2 | 151.7 / 448.5 | 108.6 / 218.0 | 90.6 / 236.8 |
| Reparent | 100 | 230.4 / 255.2 | 192.9 / 279.4 | 127.4 / 187.6 | 92.0 / 279.7 |
| Reparent | 300 | 413.4 / 449.8 | 297.6 / 450.2 | 194.4 / 225.0 | 158.0 / 230.6 |
| Reparent | 500 | 636.6 / 680.5 | 504.2 / 710.7 | 260.9 / 296.6 | 194.9 / 303.9 |

### Allocations, MiB; GC collections across 30 measured operations

| Scenario | N | UI before / after | Process before / after | GC 0/1/2 before | GC 0/1/2 after |
|---|---:|---:|---:|---:|---:|
| ToolboxGrid | 50 | 30.93 / 23.88 | 31.08 / 24.03 | 66/53/12 | 56/55/17 |
| ToolboxGrid | 100 | 36.83 / 29.83 | 36.99 / 29.98 | 67/37/5 | 50/34/2 |
| ToolboxGrid | 300 | 61.78 / 54.76 | 62.00 / 54.96 | 92/69/4 | 75/50/1 |
| ToolboxGrid | 500 | 87.14 / 79.91 | 87.35 / 80.17 | 135/107/19 | 116/88/17 |
| ToolboxStackPanel | 50 | 27.59 / 21.40 | 27.69 / 21.52 | 59/47/12 | 58/53/20 |
| ToolboxStackPanel | 100 | 33.39 / 26.93 | 33.51 / 27.07 | 51/33/3 | 42/35/6 |
| ToolboxStackPanel | 300 | 57.19 / 50.94 | 57.33 / 51.09 | 98/70/16 | 72/46/8 |
| ToolboxStackPanel | 500 | 80.98 / 74.63 | 81.17 / 74.77 | 124/97/16 | 109/86/17 |
| ToolboxDockPanel | 50 | 37.52 / 21.79 | 37.67 / 21.92 | 82/59/11 | 52/45/14 |
| ToolboxDockPanel | 100 | 50.91 / 27.31 | 51.10 / 27.45 | 104/70/16 | 46/36/4 |
| ToolboxDockPanel | 300 | 106.08 / 51.25 | 106.39 / 51.49 | 192/164/21 | 72/51/8 |
| ToolboxDockPanel | 500 | 163.27 / 74.98 | 163.66 / 75.27 | 301/253/46 | 104/83/13 |
| ToolboxWrapPanel | 50 | 27.62 / 21.42 | 27.75 / 21.54 | 58/48/8 | 52/50/18 |
| ToolboxWrapPanel | 100 | 33.41 / 26.96 | 33.56 / 27.10 | 51/33/3 | 51/33/3 |
| ToolboxWrapPanel | 300 | 57.18 / 50.93 | 57.39 / 51.14 | 100/72/15 | 85/59/14 |
| ToolboxWrapPanel | 500 | 80.99 / 74.63 | 81.22 / 74.82 | 112/85/14 | 102/77/11 |
| ToolboxCanvas | 50 | 27.95 / 15.32 | 28.12 / 15.42 | 70/70/21 | 30/30/6 |
| ToolboxCanvas | 100 | 37.64 / 18.12 | 37.86 / 18.24 | 76/76/14 | 49/49/23 |
| ToolboxCanvas | 300 | 87.64 / 30.81 | 87.98 / 30.96 | 170/153/36 | 40/40/13 |
| ToolboxCanvas | 500 | 141.55 / 42.98 | 141.98 / 43.12 | 265/179/49 | 38/18/4 |
| Move | 50 | 5.93 / 5.95 | 5.95 / 5.97 | 14/14/6 | 11/11/3 |
| Move | 100 | 11.66 / 11.71 | 11.70 / 11.74 | 23/17/7 | 1/0/0 |
| Move | 300 | 37.69 / 37.80 | 37.76 / 37.86 | 57/56/18 | 53/52/15 |
| Move | 500 | 62.78 / 63.01 | 62.86 / 63.09 | 76/47/20 | 82/63/23 |
| Reparent | 50 | 35.45 / 19.93 | 35.61 / 20.02 | 87/87/23 | 39/39/4 |
| Reparent | 100 | 48.68 / 25.18 | 48.88 / 25.28 | 114/108/29 | 56/47/15 |
| Reparent | 300 | 104.13 / 49.34 | 104.45 / 49.45 | 214/187/43 | 91/91/26 |
| Reparent | 500 | 161.33 / 73.53 | 161.76 / 73.64 | 319/288/64 | 113/97/22 |

### First Drop versus steady state, N=500

| Scenario | First before / after, ms | First JIT before / after | Steady JIT median before / after |
|---|---:|---:|---:|
| ToolboxGrid | 643.1 / 645.5 | 643 / 600 | 15 / 14 |
| ToolboxStackPanel | 622.2 / 574.1 | 474 / 635 | 20 / 9 |
| ToolboxDockPanel | 1026.3 / 537.0 | 1437 / 491 | 12 / 10 |
| ToolboxWrapPanel | 625.6 / 500.1 | 371 / 599 | 7 / 14 |
| ToolboxCanvas | 782.5 / 212.6 | 2299 / 395 | 15 / 4 |
| Move | 564.2 / 514.8 | 512 / 284 | 6 / 5 |
| Reparent | 1217.8 / 645.2 | 1784 / 331 | 10 / 11 |

### After timeline, cumulative ms from Drop, N=500 (median)

| Scenario | Handler | Visual ready | Layout complete | Commit | Next Input | Hover outside clock |
|---|---:|---:|---:|---:|---:|---:|
| ToolboxGrid | 147.1 | 147.3 | 232.5 | 255.9 | 271.3 | 134.0 |
| ToolboxStackPanel | 151.8 | 151.9 | 221.9 | 236.3 | 249.7 | 137.0 |
| ToolboxDockPanel | 131.6 | 131.7 | 195.7 | 211.3 | 219.1 | 136.2 |
| ToolboxWrapPanel | 128.0 | 128.1 | 195.4 | 218.4 | 227.8 | 136.9 |
| ToolboxCanvas | 64.5 | 64.6 | 95.0 | 110.4 | 117.3 | 22.2 |
| Move | 75.3 | 194.8 | 194.9 | 212.6 | 218.6 | 6.1 |
| Reparent | 70.1 | 244.4 | 244.5 | 260.9 | 266.5 | 6.0 |

