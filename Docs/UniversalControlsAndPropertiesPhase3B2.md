# Phase 3B.2 — Native UI Automation Provider Lifetime

Дата: 15 ноября 2026. Исходный commit: `727fe421d7668e057ac0b4417e95f2ff157dc82e` (`main`, Phase 3B.1). В начале working tree был чистым. Production-код не изменён. Phase 4 не начата.

**Итог расследования: UNRESOLVED.** Происхождение null provider в двух исторических UIA crashes не установлено. Новые проверки отделяют зависимое от клиента удержание окон и ошибку диагностического setup от исходного native fault. Ни отсутствие повторения, ни место разыменования null не являются исправлением/root cause.

## 1. Baseline и исходные доказательства

Изучены Phase 3B.1, Phase 3B, PreviewMemoryLeakFix, StabilizationAudit и соответствующие исходники. Повторно не запускался отвергнутый disconnect дочерних providers. В окончательном production path по-прежнему только root-disconnect Preview; accessibility не отключалась.

Среда: Windows 10 Home 22H2, build **19045.6456**, x64; SDK **10.0.402**, целевой runtime **.NET 8.0.32**, Avalonia **11.1.1**. SDK/PowerShell .NET 10 не следует смешивать с runtime тестируемого приложения. UIAutomationCore fixed file version **7.2.19041.5794**; строка `FileVersion` содержит также устаревшее display-значение `7.2.19041.1`. Новый journal пишет оба значения. Main product остаётся Beta 0.1; NuGet/TFM существующих проектов не менялись.

| Факт / гипотеза | Доказательство | Чего не хватает |
|---|---|---|
| Факт: два исходных AV имеют одну сигнатуру | PID 40752 и 74048, Event Log, сохранённые symbolicated stacks, SHA-256 | Оригинальные бинарные файлы теперь недоступны; см. инцидент ниже |
| Факт: provider-аргумент равен нулю до COM-вызова | `ProviderCallouts::GetPatternProvider+0x2f`, `RSI=0`, `mov rax,[rsi]`; caller `UiaNode::ProviderGetPatternProvider` | Полный heap исходного `UiaNode`, история изменения его слота |
| Гипотеза: преждевременный Release/CCW/GC | Lifetime теоретически требует проверки | Нет AddRef/Release trace, идентичности исходного CCW и привязки к control |
| Гипотеза: race с Close/Popup/Dispatcher | В стеке incoming UIA request на IO worker | Не зафиксирован соответствующий Close/Disconnect на том же node |
| Гипотеза: Collection Editor породил дефект | Исходные наблюдения пришлись на GUI regression | Отрицательные изолированные tests не устанавливают причинность; нет положительного минимального reproducer |
| Гипотеза: Windows/Avalonia defect | Fault находится в Windows UIA, используются Avalonia providers | Module name и место падения не определяют виновника |
| Факт: connected client удерживает обычные окна и без Designer | Новый минимальный probe и одинаковый differential test | Связь этого удержания с историческим null не установлена |

Исторические файлы:

| PID / время MSK 14.11 | Faulting thread | Размер | SHA-256 |
|---|---:|---:|---|
| 40752 / 20:12:21 | 74716 | 45 611 193 B | `082a80e9b7e8dca70767197f784a77f4be25acb6c2b176cfd1c472343728c82e` |
| 74048 / 20:26:41 | 63268 | 45 848 374 B | `eed62c305cda3b992d0bdd96f08346dd950a160479412e632a1208d3fc5045c2` |

Exception `0xC0000005`, module RVA **0x47593**, Value pattern **10002 / 0x2712**. По сохранённому disassembly `GetSlotAsProvider` передал ноль в callout. Почему получен ноль — неизвестно. Это не доказательство конкретного control, освобождённого CCW или записи нуля определённым потоком.

Сохранённый native call path:

```text
OverlappedIOManager::IoThreadProc
  ReadWriteChannelInfo::Service / ChannelBasedServerConnection::OnData
    ProcessIncomingRequest / InvokeOnCorrectContext_Callback
      RemoteUiaNodeStub::OnMessage / Incoming_GetPropertyValues
        UiaNode::CrossProcess_GetPropertyValues
          InProcClientAPIStub::InvokeInProcAPI / UiaNode_GetPropertyValues
            UiaNode::ProviderGetPropertyValue
              UiaNode::ProviderGetPatternProvider
                ProviderCallouts::GetPatternProvider  [RSI = 0]
```

Faulting worker не имеет managed frames в сохранённом stack; UI thread находился в Win32 message loop. Нельзя сопоставить его с произвольным managed `AutomationPeer` по временному совпадению.

Отвергнутый Phase 3B.1 experiment PID 67132 имеет **другую** сигнатуру: `InProcClientAPIStub::InvokeInProcAPI+0x25`, RVA **0x5d615**, неканонический RCX. Его не объединяли с исходными двумя AV; соответствующий source остаётся opt-in текстом, не production-кодом.

## 2. Качество dumps и инцидент сохранности доказательств

Исходные dump flags **0x200121**, 96 threads, 168/169 modules. Это minidumps с registers/stacks/частью памяти, **без MiniDumpWithFullMemory**. Они позволили получить native stacks, CLR module state и disassembly, но не прочитать необходимый native heap `UiaNode`. Наличие CLR modules не означает наличие полного managed heap. COM reference count и owner исходного provider не определены. В сохранённом module inventory Eremex не обнаружен.

Использовался matching Microsoft PDB identity `5F5B60563531913BDC1025B38920B87B1`. Имена получены с PDB, не по ближайшему export. Public symbols не дают полного private UIA object layout и истории записи в слот.

**Во время этого этапа допущена диагностическая ошибка.** До проверки одного real target была запущена серия из 16 controller cases. Helper обращался к `Process.Modules.Single` в LOAD_DLL до появления mapping в enumeration, завершался с managed exception. Такие отказы создали новые WER dumps `dotnet.exe`. После серии исходные 40752/74048 и experimental 67132 в WER folder исчезли; вероятная причина — автоматическая ротация WER (наблюдались 10 более новых файлов). Явного удаления этих файлов и изменения registry не было, но организация серии была ошибочной.

Read-only поиск WER archive/queue: 42 dotnet report directories, пригодных dump/cab копий не найдено. Сохранены прежние SHA/header inventories, native stacks, modules, disassembly и Event Log. Поэтому качества старых dumps выше оцениваются **по сохранённым материалам Phase 3B.1**, а не выдаются за новый анализ отсутствующих бинарных файлов. Старые reports не переписаны. Этот инцидент ограничивает возможность повторной проверки доказательств.

Controller исправлен: PE SizeOfImage читается из файла модуля, собственные managed errors возвращают код 2, серия останавливается на первом failed observation. README требует сначала сохранить отдельные копии исходных dumps и проверить один target. Dumps не включаются в Git и не публикуются.

## 3. Новый локальный full crash capture

Добавлен test-only `smoke-tests/UiaStability/NativeCapture`, net8.0, без сторонних packages. Он запускает только свой target с `DEBUG_ONLY_THIS_PROCESS`, получает debug events и вызывает `MiniDumpWriteDump` на first-chance AV внутри UIAutomationCore либо любом second-chance AV. AV передаётся дальше как NOT_HANDLED; recovery/retry нет. Второй breakpoint также не подавляется. Ограничение времени относится только к своему процессу.

Запрашиваются full memory, handles, unloaded modules, thread/process/memory information. На диске проверены **flags 0x201826 + Memory64 stream**. Self-test с намеренным native fault создал dump **55 199 196 B**, exit **-1073741819**, без timeout. Первый self-test выявил неверное packing `MINIDUMP_EXCEPTION_INFORMATION`; исправлено `Pack=4`, затем capture проверен повторно. Это проверка capture helper, не приложение и не исправление UIA.

Journal хранит UTC, PID/TID, exception code/address/chance, UIA RVA при наличии mapping, версию модуля, dump path/size и target exit. Modules сохраняются отдельно. Секреты не логируются. No WER registry/системной установки debugger/accessibility changes. ProcDump/WinDbg executable не потребовались: использованы установленные DbgEng/DbgHelp API и существующий read-only DumpAnalyzer. `dotnet-dump` установлен только локально в ignored task directory для SOS, версия 10.0.750501; первая попытка через ограниченную сеть не прошла, разрешённая загрузка завершилась успешно.

В раннем controller child stdout не передавался в redirected log. Поэтому первые regression observations имеют exit/result, но неполный assertion journal. Затем добавлено дублирование только собственных std handles; проверка target `dotnet --version` вернула ожидаемый stdout. Первоначальные результаты не удалены и не считаются полным зелёным assertion log.

## 4. Lifecycle Avalonia → COM → UIA

Реальная цепочка для pinned 11.1.1:

```text
WM_GETOBJECT
  Win32 WindowImpl / Avalonia control automation peer
    AutomationNode.GetOrCreate(peer)
      ConditionalWeakTable<AutomationPeer, AutomationNode>
        RootAutomationNode для IRootProvider, иначе AutomationNode
          CLR COM Callable Wrapper / IRawElementProviderSimple
            UiaReturnRawElementProvider / native UIA nodes / external client
```

`AutomationNode` хранит Peer и подписывается на ChildrenChanged/PropertyChanged; root дополнительно на FocusChanged. CWT сам по себе не является глобальным strong root. Native COM reference через CCW может удерживать node, peer и control. `Release(peer)` в backend удаляет CWT mapping; это не эквивалент принудительному освобождению всех внешних COM references. Мы не меняли GCHandle, IUnknown/AddRef/Release, не создавали фиктивные providers и не добавляли `Marshal.ReleaseComObject` к чужим объектам.

`GetPatternProvider` проверяет поддерживаемый peer pattern и возвращает `this` или null. **Допустимый null return value для неподдерживаемого pattern не равен null input provider в историческом crash.** Fault находится до обращения к COM vtable, поэтому такой return из Avalonia не объясняет его.

Большинство peer navigation/property операций используют `InvokeSync` на UI Dispatcher. `GetPatternProvider` обращается к peer напрямую. Это область для проверки thread affinity upstream, но не доказанная причина данного AV: исторический fault произошёл до managed callback.

`RootAutomationNode.HostRawElementProvider` зависит от живого `WindowImpl`; после разрушения HWND может вернуть null. Стандартный Win32 teardown вызывает `UiaReturnRawElementProvider(hwnd,0,0,null)`. Это уведомление о destroyed window, а не доказательство завершения всех ранее выданных descendant references.

Существующий `PreviewAutomationLifetime.CaptureDisconnect` ищет **уже созданный** root в CWT и не создаёт peers ради teardown. `PreviewWindow` вызывает disconnect до Avalonia WM_DESTROY teardown, блокирует reentrant WM_GETOBJECT в этот момент, откладывает Close при `InSendMessageEx != 0`. Closed очищает owned animation/data activity lifecycle. Этот путь оставлен без изменений; blanket descendant disconnect по-прежнему отвергнут.

## 5. Новый full dump: другой сбой, доказан test-harness root cause

Первый расширенный ContextMenu case завершился exit **-1073741819** на cycle 0. Сохранён полный dump `editor-controls/ContextMenu-capture/av-43972-68252.dmp`, **625 408 410 B**, SHA-256 **`c27d686d60a97876dcd25f6c84c2ca634e91eb2787fca18e255c54ed6e3e41f4`**; UTC 08:54:15, PID **43972**, TID **68252**. Journal exception address **0x7FF7A2D8FE19**, не UIAutomationCore; module UIA ещё не был загружен.

DbgEng + SOS `ip2md` / `pe` / managed heap показали:

```text
System.NullReferenceException (0x80004003)
StandardItemsEditorWindow..ctor(MainWindowViewModel, CollectionEditorSession)+0x229
Program.UiaStressCycle+0x4bc
Program.RunUiaStability+0xaa9
Program.Main+0x114
```

Причина: новый harness ошибочно запросил `ContextMenu.Items` у `Button`. Проверенный CollectionControlFactory не поддерживает этот owner; `OpenCollectionEditor` вернул null с диагностикой, а тест передал null в non-null constructor. Исправлен **только harness**: ContextMenu owner = Menu, явная проверка возвращённой session; затем отдельный setup sanity и новый полный case. Production UI проверяет capabilities, поэтому этот тестовый отказ не объявлен обнаруженной ошибкой UIA/пользовательского workflow.

`0xC0000005` и exit code сами по себе не определяют unmanaged root cause: в данном dump это JIT null dereference, преобразованный CLR в NRE. Исходный красный case сохранён. Классификация первого сообщения о нём уточнена после SOS, не засчитана как повторение `+0x47593`.

## 6. Минимальный probe и дифференциальные проверки

Новый `MinimalAvalonia` зависит только от Avalonia Desktop/Fluent 11.1.1. Нет DesignerSurface, PropertyGrid, Export, SQL, plugins и ссылки на основной Designer. Реальные HWND, STA, настоящий UI Dispatcher. Control factory ограничена TextBox/Menu/ContextMenu/Flyout/TreeView/TabControl/ComboBox/ListBox. Для popup cases owner закрывается с открытым popup, чтобы не скрывать lifecycle дефект.

Два режима: обычное закрытие и root-only disconnect, соответствующий существующему Preview lifecycle. Второй режим **не** production candidate и не отключает descendants. Passive probes читают только уже существующие CWT entries, не создают peers/COM interfaces. Runtime hash IDs помогают сопоставлять наблюдения внутри цикла, но не являются доказательством native pointer identity/COM refcount.

В TextBox cases до client acquisition наблюдалось 0 providers, после acquisition/BEFORE_CLOSE/WM_DESTROY/CLOSED/AFTER_CLOSE — 16. Это показывает живые managed provider mappings при Close в данном тесте. Не доказывает состояние native UiaNode исторического crash.

| Сценарий | Циклы | Native UIA AV | Удержание при своём клиенте → после его выхода |
|---|---:|---:|---|
| Minimal: 8 типов × normal/root-disconnect × 50 | 800 | 0 | 50 → 0 для каждого case |
| Одинаковый PlainWindowProbe, Phase 3A `9f78cc0…` | 200 | 0 | 200 → 0 |
| Тот же probe, Phase 3B `0b6a187…` | 200 | 0 | 200 → 0 |
| Тот же probe, Phase 3B.1 production `727fe42…` | 200 | 0 | 200 → 0 |
| Тот же probe, minimal Avalonia | 200 | 0 | 200 → 0 |
| Minimal TextBox, без собственного клиента | 200 | 0 | 0 → 0 |
| Collection Editor: 7 типов × 50 | 350 | 0 | sessions после завершения клиента: 0 |
| Legacy Preview + Tree client | 100 | 0 | 100 → 0 |
| AXAML Preview + Tree client | 100 | 0 | 100 → 0 |
| UIA непосредственно к PopupRoot Menu / ContextMenu / Flyout | 150 (3×50) | 0 | 50 → 0 для каждого case |
| UIA непосредственно к Flyout PopupRoot, depth 24, обязательный ValuePattern provider | 50 | 0 | 50 → 0 |

Differential source `Common/PlainWindowProbe.cs` один и тот же во всех четырёх сборках; source archives создавались через `git archive`, не checkout/reset. Сценарий Window/TextBox, 200 iterations, один client protocol, .NET/Avalonia/Windows одинаковы. Desktop initialization в Designer использует реальный App/resources, minimal — Fluent; execution общего probe во всех случаях `SetupWithoutStarting` + одинаковый Dispatcher pump. Отдельная 800-cycle matrix использует ClassicDesktopLifetime и не смешивается с ним как один A/B.

Menu editor 50 циклов завершился до ошибочного ContextMenu setup. Остальные 6 типов завершены после исправления setup в новой серии; первоначальная серия не переименована в зелёную. Nested Add/Remove проверялись только для Menu/ContextMenu/Flyout/TreeView; плоским ComboBox/ListBox/TabControl не приписывается поддержка nested items. Native UIA request acquisition проверялся до Close; assert завершения client query и session disposal оставлены.

Дополнительно проверен отдельный native popup HWND, а не только owner Window. Подтверждён тип `Avalonia.Controls.Primitives.PopupRoot`; HWND принадлежит target PID. Это важно: owner RawView traversal не гарантирует обход отдельного popup. В исходном depth-8 Flyout обходе было 10 nodes и 0 ValuePattern providers — TextBox находился глубже шаблона. В отдельном ограниченном depth-24 case стало **30 nodes / 1 ValuePattern provider**; присутствие provider является обязательным setup assertion. После двух sanity cycles выполнены 50 measured stress cycles с этим условием, Captured=false, exit 0. Для иных cases прежняя глубина сохранена. Это исправление **охвата теста**, не accessibility/production workaround.

Это **отрицательный диагностический probe**, а не положительный reproducer исторического AV. **2550 запланированных завершённых циклов** не включают development failures и короткие sanity runs. Ноль AV не доказывает исправление. Ни connection teardown, ни process-exit без ошибки не заменяют соответствующие assertions.

## 7. UIA clients, shutdown и пределы изоляции

Собственный клиент — существующий external MTA `UiaTreeClient`: PID-check HWND, ограниченный RawView traversal (128 nodes / default depth 8, opt-in depth 24 для отдельного popup case, hard limit 32), запрос ValuePattern availability до/после Close, без чтения пользовательских текстов и вызова actions. Процесс клиента завершается явно; сначала фиксируется connected retention, затем released retention. В no-owned-client tests Windows accessibility не отключалась. Идентичность всех ambient клиентов и исторического клиента **не установлена**; фраза «без клиента» не используется вместо «без нашего клиента».

Подтверждена доступность ETW providers `Microsoft-Windows-UIAutomationCore` `{820A42D8-38C4-465D-B64E-D7D56EA1D612}`, COMRuntime и DotNETRuntime. Выполнен только read-only inventory; COM/ETW lifetime trace и PID correlation исторического запроса не сняты. System-wide tracing, смена Windows build, screen reader configuration и глобальная accessibility не менялись.

Новые native tests выполнялись отдельными процессами, последовательно внутри каждой матрицы. Общего межпроцессного Avalonia Application/Dispatcher нет. Существующий smoke host использует nested Dispatcher pump и завершение процесса вместо обычного app lifetime; minimal ClassicDesktopLifetime проверен отдельно. Исторический shutdown race этим не исключён. Результаты диагностического повторного PropertyGrid запуска, пересекавшегося с другими функциональными checks, не являются performance A/B.

## 8. Window retention — отдельный P2

Сохранённый SOS Phase 3B.1 устанавливал цепочки:

```text
native COM ref-counted handle → RootAutomationNode → WindowAutomationPeer → Window
native child AutomationNode → TextBlock/NoneAutomationPeer → cached children
  → ChromeOverlayLayer / TitleBar / ValueStore → PreviewWindow
```

Это источник удержания в тех snapshots, **не** GC root исторического faulting UiaNode. Само существование CCW reference при активном клиенте совместимо с его lifetime. Нельзя назвать это постоянной утечкой только по WeakReference после GC.

Новые minimal/differential cases показали освобождение всех окон после завершения собственного клиента. В свежем полном Stabilization прогоне ordinary negative control: **0/6 alive**, audit exit 0. На forensic hold снят heap (728 999 688 B, collect exit 0; tool сообщил один PARTIAL_COPY retry), в нём нет живых экземпляров обычного `Avalonia.Controls.Window`/RootAutomationNode. Поэтому новую gcroot цепочку для этих шести окон получить невозможно — они уже собраны. Прежнее 24/25 в этом запуске не повторилось; production fix не заявляется. Older native client/cache P2 и связь с исходным crash остаются отдельными вопросами.

## 9. Performance

Release, .NET 8.0.32, **без debugger**, render scaling **1.0**, одна Menu-модель, editor 900×650 DIP, порядок counts 10 → 50 → 100. По каждому count: 3 warm-up и 30 measured iterations. Порядок операций: Open → Add → Reorder → Remove → Selection → NestedAdd → NestedRemove. Перед iteration загружается один и тот же seed; дерево и session реально существуют. **693 samples = 63 warm-up + 630 measured**, для каждого из 21 count/operation сочетаний — 30 measured. Нет CI timing thresholds, restore/build/SQL/stress процессы до profile завершены.

Synchronous time включает действие + editor.UpdateLayout. Allocations — UI-thread bytes до compositor callback, включая обработанную за это время очередь; это более широкое окно, чем synchronous time, и не whole-process/native allocations. Callback не является первым физическим пикселем. P95 — nearest rank `ceil(.95*N)`, variance — sample variance с ddof=1. Min/max, Open/Selection и все raw observations сохранены в `performance/summary.json` и исходном JSON.

| Items | Operation | Median / P95 ms | Alloc median / P95 MiB | GC0/1/2 за 30 | Variance ms² | Compositor median / P95 ms |
|---:|---|---:|---:|---|---:|---:|
| 10 | Add | 8.98 / 32.93 | 0.73 / 0.81 | 3/3/0 | 95.01 | 10.26 / 33.79 |
| 10 | Remove | 8.45 / 33.79 | 0.73 / 4.75 | 6/6/1 | 146.16 | 9.22 / 34.94 |
| 10 | Reorder | 4.34 / 15.72 | 0.41 / 4.66 | 0/0/0 | 21.19 | 4.57 / 16.21 |
| 10 | NestedAdd | 8.53 / 29.55 | 0.67 / 4.70 | 1/1/0 | 65.71 | 9.73 / 31.35 |
| 10 | NestedRemove | 7.97 / 29.13 | 0.62 / 4.63 | 3/3/0 | 72.49 | 9.49 / 30.83 |
| 50 | Add | 8.23 / 11.18 | 1.43 / 1.45 | 0/0/0 | 14.31 | 9.17 / 12.14 |
| 50 | Remove | 8.51 / 36.20 | 13.04 / 13.07 | 19/15/2 | 116.78 | 9.70 / 59.95 |
| 50 | Reorder | 4.30 / 30.84 | 0.96 / 12.81 | 8/8/1 | 121.73 | 4.61 / 31.12 |
| 50 | NestedAdd | 7.39 / 26.89 | 1.30 / 13.00 | 5/5/1 | 26.67 | 8.78 / 29.05 |
| 50 | NestedRemove | 7.41 / 27.77 | 1.26 / 12.94 | 9/9/1 | 75.00 | 8.78 / 61.31 |
| 100 | Add | 9.35 / 37.01 | 2.29 / 2.35 | 7/7/1 | 82.38 | 10.50 / 38.13 |
| 100 | Remove | 8.70 / 10.00 | 2.27 / 23.35 | 14/13/0 | 0.49 | 9.78 / 82.55 |
| 100 | Reorder | 5.98 / 39.63 | 19.29 / 23.08 | 27/27/1 | 241.31 | 6.50 / 40.15 |
| 100 | NestedAdd | 8.30 / 27.95 | 2.13 / 23.28 | 18/9/1 | 67.58 | 10.17 / 30.42 |
| 100 | NestedRemove | 7.95 / 22.88 | 2.16 / 23.33 | 23/12/1 | 20.39 | 9.02 / 89.02 |

Исторический Phase 3B N100: Add median 47.175 ms, Reorder 21.265 ms. Phase 3B.1 full 1/10/100 дал 76.40/43.42 ms, тогда как его N100-only дал 12.36/6.30 ms. Новый порядок 10/50/100 и дополнительные Remove изменяют preceding history/queue/GC sequence. Поэтому **9.35/5.98 ms не объявлены ускорением production**. В этой фазе performance fix не было; причинный Before/After одного исправления отсутствует. Текущие числа — отдельный воспроизводимый baseline.

Allocations/P95 остаются неоднородными: например, N100 Remove sync P95 10.00 ms, compositor P95 82.55 ms; измерение allocations охватывает эту дополнительную работу/ожидание. GC встречается в том же observation window, но его наличие не доказывает, что именно он вызвал каждый выброс. CPU/ETW attribution queued callbacks не снимался. Высокую стоимость отдельных observations не стали исправлять изменением Layout/Undo вне native investigation. Обычный PropertyGrid performance benchmark Phase 3A отдельно здесь не переизмерялся; вместо этого выполнены функциональные regression и collection profile.

## 10. Regression и первоначальные красные результаты

| Проверка | Результат / границы |
|---|---|
| Solution Release | **0 errors, 1 warning NU1603**: VS SDK Analyzers 17.7.20 не найден, используется ранее разрешённый 17.7.22. Версии не изменялись |
| Smoke runner / новые diagnostic projects | Сборки 0 errors; исправленные compiler/setup ошибки development сохранены в ранних logs |
| Collections | **30/30** с полным assertion journal |
| PropertyGrid | **29/30** первый run; diagnostic repeat тоже 29/30; отдельный failed check ниже |
| Metadata | **45/45** |
| Universal Phase 2 | **40/40** |
| Stabilization | **25/25**, plain Window 0/6; это новый observation, а не исправление старого P2 |
| Nested layout / Pointer / Grid Drop / Grid Editor / Layout7A | Все пять отдельных runners exit 0, Captured=false. Их ранний stdout утрачен из-за controller forwarding bug; новые агрегированные x/y не выдумываются |
| PostgreSQL offline / PostgreSQL–Eremex contracts | Оба runners exit 0; PostgreSQL live не проверен |
| Broad, полный исправленный diagnostic driver в sandbox | **69/72**, exit 1, Captured=false, 314014 ms. Три AccessDenied IPC failures |
| Те же 3 IPC cases вне sandbox | **3/3**, отдельный run 22229 ms. Это не переименование полного первого run в 72/72 |
| LocalDB / DataGrid | **47/47**, real rows, оба Preview; source workflow отдельно PASS |
| Preview Memory с live LocalDB | **36/36**, включая TwentyCycles: 20 Legacy + 20 AXAML, DataGrid/несколько grids/MultiForm/закрытие при loading |
| SQL generated runtime | PASS: отдельный процесс, Grid Customers=3, nested Grid Orders=2, реальные видимые значения, независимые VM и resize |
| Collection generated runtime Grid / Canvas | **Оба PASS**: native roots=6, nested Menu/Separator, Combo/List items, tabs/content, tree expand/collapse, ContextMenu, MenuFlyout, resize 800×600 / 1200×800 / 1600×900 |
| VSIX / VsHost | VSIX собран solution build; реальные автоматические VsHost/Named Pipes/ACK/round-trip checks в Broad + отдельный разрешённый IPC run. Installed VS GUI вручную не проверялся |

Полный Broad исходно был прерван 300-second bound: старый timeout path повторно вызывал TerminateProcess и завершил controller с exit 2 без result.json. Это **incomplete**, не AV и не pass. Исправлена однократная termination; отдельный timeout self-check дал controller **124**, target **57005 / 0xDEAD**, TimedOut=true, Captured=false, 1397 ms. В следующем запуске был неверно задан NuGet cache: получен NU1301 на `127.0.0.1:9`; после подтверждения причины остановлено только собственное process tree. Его красные результаты/abort marker сохранены. С правильным `NUGET_PACKAGES`, process-only `NuGetAudit=false` и лимитом 1800 s весь Broad завершён. Production NuGet config не менялся; vulnerability-feed audit этим run не сертифицируется.

Broad failures: `AxamlHardeningIpcEditReloadLifecycle`, `VsHostCanStartAndAcceptConnection`, `BridgeSurvivesVsHostDisconnect`. Все три stacks показывают отказ `NamedPipeClientStream.TryConnect` внутри sandbox. Отдельное выполнение вне sandbox прошло 3/3 с тем же кодом. Ручная Visual Studio / screen reader проверка этим не заменяется.

Первое создание изолированной LocalDB в sandbox завершилось registry/SSPI errors **до live assertions**. Этот запуск не засчитан как live test. После разрешённого запуска вне sandbox создан отдельный GUID instance, выполнена fixture Windows authentication без паролей, пройдены перечисленные live checks; созданный instance остановлен и удалён. Пользовательские базы/серверы не изменялись. PostgreSQL server не устанавливался.

**Отдельный P1: Undo после Reset импортированного AXAML.** `SourceResetRemovesOnlyAttribute` воспроизведён и изолированно: после Undo `Value='12.5'` у NumericUpDown остаётся удалённым, `Minimum='0'`, Binding и комментарий сохранены. Это конкретное нарушение восстановления source property в automated API workflow, а не native UIA failure. Архивные **Phase 3A и Phase 3B также дали 0/1** для этого же check: дефект предшествует Phase 3B.2. Production repair не включён в native lifetime investigation; нужен отдельный разбор snapshot/Reset/patch interaction и проверка UI-кнопки Reset. Нельзя считать PropertyGrid полностью зелёным или закрыть этот риск ссылкой на прежние 30/30.

Новые P0/P1 от production patch не вносились — такого patch нет. Это не отменяет найденного существовавшего P1 и незакрытого native риска. Автоматические повторы использовались для диагностики/изменённого permission или environment context, а не retry-until-green. Первые failures сохранены; исходные AV по-прежнему не воспроизведены в новых корректных cases.

## 11. Изменённые файлы

Только diagnostics/tests/documentation:

- `smoke-tests/UiaStability/NativeCapture/{NativeCapture.csproj,Program.cs}` — локальный full capture.
- `smoke-tests/UiaStability/MinimalAvalonia/{MinimalAvalonia.csproj,Program.cs}` — standalone diagnostic probe.
- `smoke-tests/UiaStability/Common/PlainWindowProbe.cs` — общий differential source.
- `smoke-tests/UiaStability/RunCaptured.ps1`, `README.md` — fresh outputs, stop-on-first-failure, инструкции.
- `smoke-tests/UiaStability/UiaTreeClient/Program.cs` — opt-in ограничение глубины для доказанного gap в popup traversal; default 8 сохранён.
- `smoke-tests/FormDesigner.ExportSmokeTests/{Program.UiaDifferential.cs,Program.cs,FormDesigner.ExportSmokeTests.csproj}` — test dispatch/link.
- `Program.UiaStability.cs` — per-control reviewed owners и отказ от null session.
- `Program.Collections3B.cs` — opt-in Remove/NestedRemove и counts 10/50/100; прежний default profile сохранён.
- `Program.PropertyGrid3A.Checks.cs` — richer failure message для незелёного Undo assertion.
- Этот отчёт.

Git tags/history не менялись, commit не создавался. Бинарные dumps, local tools, generated source archives и диагностические логи находятся только в ignored `artifacts/universal-phase3b2`; они не являются release assets.

## 12. Отклонённые объяснения и оставшиеся риски

- «GetPatternProvider вернул null для unsupported pattern, значит это root cause» — неверно: в историческом crash нулевой *входной provider*.
- «Любой exit C0000005 — повторение исходного native crash» — опровергнуто новым NRE dump.
- «Collection Editor доказанно виноват» / «доказанно невиновен» — ни одно утверждение не подтверждено отрицательной матрицей.
- «Eremex виноват» — нет связи в исходном module inventory/stack; minimal не использует plugin.
- «CCW преждевременно собран GC» — нет refcount trace; connected-client retention показывает противоположное поведение в новых tests, но не состояние исходного provider.
- «Root disconnect всех descendants исправит проблему» — прежний эксперимент вызвал другой AV, не применён повторно.
- «Старый Window P2 исправлен» — нет production change; новый отрицательный результат не объясняет старую retaining chain.

Риски: timing-dependent native race, неизвестный ambient UIA client, Windows UIA private internals, неизвестный slot writer, отсутствие old full heaps, debugger timing perturbation, ограниченные Windows/DPI/screen-reader coverage. Native crash fix не получен. Полноценная сертификация accessibility/installed VSIX на этих tests не основана.

## 13. Следующий конкретный шаг

1. Сначала сохранить доступные original dumps в отдельный immutable-by-convention local evidence folder с SHA-256, не полагаться на WER rotation.
2. На **исходном RVA 0x47593** остановиться на first chance с этим capture helper; сохранить full native heap, все threads/modules, UTC и сценарий. Новый NRE snapshot для этого не подходит.
3. С matching PDB получить `UiaNode`-кандидат из caller, прочитать доступный slot/COM state и связать его с конкретным provider. При отсутствии private type info явно отделять inferred layout от доказанного.
4. Для live node поставить data breakpoint на **подтверждённый адрес слота**, снять writer stack/поток при его обнулении/invalidating teardown. Не использовать выдуманный offset и не изменять interop lifetime ради гипотезы.
5. В отдельной контролируемой Windows VM сопоставить local UIA/COM trace с PID собственного client и Window WM_GETOBJECT/WM_DESTROY. Сначала проверить permissions/PID filters, не включать бесконтрольный desktop-wide trace. При положительном minimal reproduction подготовить upstream package без Designer и приватных dumps.

До получения этих данных **UNRESOLVED**. Зелёная стресс-матрица не является основанием объявить null-provider defect исправленным или начать Phase 4.

## 14. Контракты и воспроизводимость

Primary references: [IRawElementProviderSimple.GetPatternProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementprovidersimple-getpatternprovider), [UiaDisconnectProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider), [UiaReturnRawElementProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiareturnrawelementprovider), [COM Callable Wrapper lifetime](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/com-callable-wrapper), [pinned AutomationNode](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/Automation/AutomationNode.cs), [pinned RootAutomationNode](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/Automation/RootAutomationNode.cs).

Локальные evidence: `old-dump-metadata-preserved.json`, `relevant-windows-events.json`, `minimal-valid-results.json`, `differential/results-*`, `editor-controls*`, `stress-preview-*`, `popup-direct/`, `popup-value/`, `context-native.txt`, `context-clr-details.txt`, `retention/`, `regression-results.json`, `final-checks-results.json`, `live-results.json`, `performance/` и отдельные первые/повторные logs. Инструкции запуска находятся в `smoke-tests/UiaStability/README.md`.

**Итоговый статус: UNRESOLVED.** Происхождение null provider не доказано; следующий необходимый результат — writer/lifetime trace исходного UiaNode при положительном воспроизведении. Production-код оставлен без изменений. Phase 4 не начата.
