# Phase 4E — Preview Memory Gate и UIA/COM lifetime

Дата: 16.11.2026. Исходный HEAD `7ca31a0a5bd133c04d702b84db4d6654eafbd7c6`, `main`, рабочее дерево перед началом чистое. Avalonia 11.1.1.0, .NET 8.0.32 x64, SDK 10.0.402, Windows 10 build 19045. NuGet/TFM, тема, DesignerSurface, Document Model, PropertyGrid, Export и VSIX architecture не менялись.

**MEMORY STABILITY: READY WITH CONDITIONS.** Подтверждён внешний client-lifetime фактор удержания; три независимых полных Memory Gate восстановлены: **36/36, 36/36, 36/36**. Production-код не изменён: небезопасного COM/GCHandle workaround не требуется и не добавлено. Исторический native UIA AV остаётся UNRESOLVED. Регрессии и 1820 stress cycles прошли; оптимизации Phase 4D сохранены.

## 1. Исходные семь отказов

В Phase 4D полный live run завершился **29/36**, exit 1. Все семь failures относятся к `FormDesigner.Views.PreviewWindow`, а не к семи разным категориям объектов:

| Check | Режим / сценарий | Окон в выборке | Зафиксированное состояние |
|---|---|---:|---|
| Legacy_TwentyCycles | 20 последовательных Show/Close | 20 | ≥1 Window жив после штатного wait/GC |
| Legacy_Eremex | 3 Preview с Eremex | 3 | То же |
| Legacy_CancelledOwnerClose | Preview owner/child, отмена первого Close, затем реальное закрытие | 2 | То же |
| Legacy_CloseDuringAsyncContractLoad | Закрытие при незавершённой contract query | 1 | То же |
| AXAML_NoDataGrid | AXAML Preview без DataGrid | 3 | То же |
| AXAML_StaticRows | AXAML Preview со статическими rows | 3 | То же |
| AXAML_TwentyCycles | 20 последовательных Show/Close | 20 | То же |

В сохранённых lifecycle records каждого отказа есть CLOSED, DISPOSED и PREVIEW_AUTOMATION_RELEASED. Ожидалось освобождение Window, contexts, items и grids после прежнего dispatcher drain/full GC. Старый assertion останавливался на первом живом Window, поэтому **точное число живых объектов по каждому из семи checks не записано**. Оно не заменяется придуманным значением 1.

В исходном 29/36 run heap не снимался. Три имеющихся heap — **другие focused TwentyCycles runs**: baseline Legacy, baseline AXAML и current Legacy. Для остальных пяти cases индивидуальный retaining path из прошлого запуска восстановить нельзя. Таблица [Initial failures CSV](PreviewMemoryPhase4EInitialFailures.csv) отделяет эти ограничения от подтверждённых данных. Общая причина всех семи не предполагается автоматически.

Классификация: PreviewWindow — семь старых failures; обычные Window — отдельный negative control/probe; PropertyGrid, Collection Editor, Popup/ContextMenu/Flyout и DesignerSurface — не самостоятельные failed checks из этих семи. Их lifecycle проверяется дополнительной матрицей. Test harness — отдельная новая ошибка forensic file protocol, описанная ниже.

## 2. Качество исходных dumps и retaining paths

Все три SHA-256 Phase 4D повторно проверены. Это Windows `dotnet-dump --type Heap`, flags **0x241a25**, не FullMemory user-mode dump. CLR/managed heap, managed stacks, module/thread streams и SOS CCW доступны. Native private writable pages присутствуют, но полнота всех native/remote COM структур не гарантирована. Heap серверного процесса не содержит heap удалённого клиента; private symbols UIAutomationCore отсутствуют. Поэтому удалённого владельца каждого reference из одного такого dump определить нельзя.

| Heap | Объекты PreviewWindow | SOS result |
|---|---:|---|
| baseline `5679078f…` | 1 | Ref-counted COM root, RefCount 1 |
| baseline `74dd573e…` | 1 | 0 unique strong roots на момент dump; Closed/Disposed flags = 1 |
| current `78f7cc90…` | 1 | 0 unique strong roots на момент dump |

Первый retaining path:

```text
HandleTable: STRONG ref-counted handle 0000017573097000
 -> RootAutomationNode 01757b727b48
 -> WindowAutomationPeer 01757b727a10
 -> PreviewWindow 01757b7251a0
```

`dumpccw 000001b61511fd00`: managed object `01757b727b48`, RefCount **1**, Outer IUnknown **0**, реальные IRawElementProviderSimple/Fragment/FragmentRoot interface pointers. Это CCW управляемого provider, не обнаруженный RCW приложения. `AutomationNode.s_nodes` — ConditionalWeakTable; она не является самостоятельным strong root этой цепочки.

В двух остальных heap наличие выделенного Window при отсутствии strong root **не доказывает постоянную утечку**. Reference мог быть освобождён между GC/WeakReference assertion и остановкой для dump; точный момент не восстановлен. Не добавлялись дополнительные GC или waits ради зелёного результата.

Повторно не выполнялся отвергнутый descendant-disconnect эксперимент Phase 3B.1, вызвавший другой AV. Старые animation-clock roots, root disconnect timing, SendMessage deferral и canceled owner Close из PreviewMemoryLeakFix изучены; они не переписывались без новых доказательств.

## 3. Новое контрольное воспроизведение вне Designer

Переиспользован `smoke-tests/UiaStability/MinimalAvalonia` и одинаковый `Common/PlainWindowProbe.cs`. Только обычный Window с TextBox, стандартный Fluent, тот же Avalonia/.NET/Windows. Нет DesignerSurface, PropertyGrid, SQL, Export или plugins. WeakReference factory — NoInlining, UI thread/dispatcher реальный, не headless.

В ambient окружении с оставшимся от предыдущей интерактивной диагностики клиентом получено **18/20** закрытых обычных Window. Процесс probe оставлен живым через opt-in forensic hold, снят heap. После завершения **собственной** диагностической REPL/client session и запроса того же GC checkpoint в том же процессе: **18 → 0**. Приложения пользователя не закрывались, Windows accessibility не отключалась.

В этом heap отдельно получен `gcroot` **для каждого из 18 Window**: по одному ref-counted root RefCount 1 → RootAutomationNode → WindowAutomationPeer → Window. Полные адреса/paths — `plain-hold-ambient/all-window-gcroots.log`; parser подтвердил 18/18. Это не вывод только из process working set.

Клиент: `codex-computer-use` PID 33572, старт 20:20:14, executable из bundled `@oai/sky`, parent ChatGPT PID 42680. Эти metadata сопоставлены с нашим использованием клиента в Phase 4D. Сам список процессов, загрузивших UIAutomationCore, **не считается списком владельцев references**.

Для исключения вывода по одной корреляции выполнены новые comparisons:

| Условие | Наблюдение |
|---|---|
| Новый helper только перечисляет окна, без accessibility snapshot | 0/20 retained |
| Read-only UIA tree snapshot одного собственного probe Window, затем Close | 1/1 retained |
| Повторный GC при том же живом snapshot client | 1/1 retained |
| Повтор с атомарным forensic protocol; завершение того же owned client; GC в том же сервере | **1/1 → 0/1** |
| After heap последнего процесса | Нет экземпляра `Avalonia.Controls.Window`; server exit 0 |

В heap после реального snapshot найдены **два**, а не один root:

```text
ref-counted handle, RefCount 3
 -> RootAutomationNode -> WindowAutomationPeer -> ordinary Window

ref-counted handle, RefCount 2
 -> AutomationNode -> TextBoxAutomationPeer
 -> cached child peers -> Border / composition visual
 -> ServerCompositionTarget -> TopLevel render closure -> ordinary Window
```

В этой цепочке нет Designer objects или test static fixture. Это доказательство зависимости удержания проверенного Window от external client references/cache. Оно не доказывает дефект Avalonia/Windows и не определяет post-factum владельца всех семи старых failures. Read-only snapshot всё равно создаёт долгоживущие UIA references.

Доказательства: `plain-hold-ambient/`, `own-client-active-20/`, `own-client-snapshot/gcroot.log`, `atomic-snapshot/TextBox/hold-samples.jsonl`, `atomic-snapshot/after-heap-stats.log`. Два checkpoint последнего case — 18:42:40 UTC alive=1 при живом client и 18:42:46 UTC alive=0 после client exit; server продолжал работу. [Минимальный reproducer / техническое описание](Phase4EUiAutomationRetentionReproducer.md) подготовлен для upstream, не опубликован и не отправлен.

## 4. Production и test/diagnostic lifecycle

Подтверждённый controlled root: внешний клиент удерживает COM provider; provider удерживает peer и Window. Простое закрытие HWND или запись PREVIEW_AUTOMATION_RELEASED не является измерением нулевого CCW reference count. Существующий per-window root disconnect не даёт приложению права принудительно освобождать чужие references.

[Microsoft UiaDisconnectProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider) описывает освобождение UIA ресурсов, UIA_E_ELEMENTNOTAVAILABLE для связанных запросов, запрет outbound COM внутри SendMessage и опасность повторной публикации provider при reentrant WM_GETOBJECT. Нынешний production adapter уже учитывает эти условия. Его pinned reflection metadata/COM lifetime/GCHandle не менялись.

Исправление в контролируемой нами части — завершать **принадлежащую диагностике** client session до post-close GC gate. Это не глобальное отключение UIA, не остановка чужих screen readers, не обход assertions и не новая логика production Close. Existing owned process UIA clients по-прежнему штатно получают STOP; после подтверждённого exit выполняется прежний checkpoint. Отдельно сохраняются наблюдения при подключённом клиенте.

Не найден новый application-owned persistent root среди исследованных Window. Влияние `DataGrid.ItemsSource`, PropertyGrid, Collection Editor или Source Map как причины всех старых failures не подтверждено; минимальное приложение показывает, что они не обязательны для данного удержания. Нельзя переносить этот вывод на любые будущие leaks.

## 5. Изменения диагностики и исправление file protocol

Изменён только smoke/diagnostic код:

* Memory Gate выводит value-only census **перед прежними assertions**: количество Windows/contexts/items/grids alive, GC counts, active windows, retained IDs. Wait 1000 ms, ForceFullGc и критерии освобождения не менялись.
* Minimal probe имеет opt-in pause до Close и forensic hold после исходного checkpoint. Это диагностическая граница для heap/client-lifetime comparisons; normal execution не получает новых ожиданий. GC вызывается только тестом по явному checkpoint.
* `WriteProbeSignal.ps1` публикует завершённый запрос через atomic rename, отказывает при существующем unconsumed request и ограничен repository artifacts. Логи/dumps не удаляет.
* NativeCapture умеет **opt-in** full dump второго шанса CLR `0xE0434352`, с отдельным `managed-…` именем и прежними SHA-256/protected archive. AV path и exception disposition не изменены. Минимальный probe также записывает unhandled managed diagnostics.
* Stress дополнен imported Preview и WeakReference на известные popup roots; после штатного завершения owned client проверяются Window/popup/editor references. Assertions усилены. Owned stress VM завершает существующий dispose lifecycle.

Первый single-window forensic case завершился managed exit `-532462766`, без AV; тогда capture собирал только AV, managed stack не получен. Это не называется UIA crash и точная причина первого события не выдумывается.

Отдельный детерминированный **intentional** locked-input experiment с новым capture получил full dump 421801576 bytes, SHA-256 `c8c9cae2b45c30b442cd9a4f04fae902ef2c4af46ec506e8b8637d5d215a944a`. SOS: **System.IO.IOException, HRESULT 0x80070020**, SafeFileHandle.CreateFile → File.ReadAllText → PlainWindowProbe.Run. Ошибка нашего нового управляющего file protocol воспроизводится без snapshot client. Atomic publisher устраняет этот класс race; повтор настоящего snapshot/teardown с ним завершился exit 0. Эти данные не выдаются за stack самого первого termination.

Первый build новых diagnostic checks получил CS1061 из-за отсутствующего `Avalonia.LogicalTree` using; исправлен только smoke file, исходный log сохранён. Первый анализ census ошибочно ожидал 36 записей; фактически 44 GC checkpoints из-за промежуточных TwentyCycles checks. Итоговый parser различает tests, checkpoints и уникальные Window.

## 6. Memory Before/After

| Граница | До | После |
|---|---|---|
| Полный SQL-enabled Preview Memory | 29/36 | **36/36 × 3 независимых процесса** |
| Ordinary minimal Window, тот же живой сервер | 18/20 alive при прежнем diagnostic client | 0/20 после client exit/GC |
| Explicit snapshot minimal Window | 1/1 alive при client references | 0/1; after heap без Window |
| Дополнительные GC/waits в Memory Gate | Прежняя методика | **Не добавлены** |
| Production lifecycle / accessibility | Прежний adapter | **Не изменены / включена** |

В каждом полном run: **36 tests, 44 GC checkpoints, 116 уникальных PreviewWindow**, 0 alive Windows/contexts/items/grids на каждом checkpoint. Количество 216 window observations включает повторные проверки одних и тех же объектов и не называется 216 уникальными окнами. Сохранены оба режима, Eremex, Multi Form, canceled owner Close, SendMessage, delayed/async query и реальные LocalDB rows.

Результат не подменяется working set. Managed/private bytes, handles/threads/GC/peak сохраняются в stress/performance observations; private bytes включают managed и native память и не считаются точным native heap allocation count. Нет доказательства отсутствия любых leaks при произвольном долгоживущем внешнем клиенте.

## 7. Stress, regression и performance

| Проверка | Результат |
|---|---|
| Build solution | PASS, 0 ошибок; прежний NU1603 VSSDK analyzer fallback |
| Финальный smoke runner / MinimalAvalonia / NativeCapture | PASS |
| Phase 4D / 4C | 14/14 / 85/85 |
| Phase 4A / 4B / 5E | 38/38 / 52/52 / 58/58 |
| PropertyGrid / Collections / Layout7 | 30/30 / 30/30 / 15/15 |
| Metadata / Pointer / Grid Drop | 45/45 / 26/26 / 40/40 |
| Broad | 72/72 вне sandbox |
| VSIX/VsHost real Named Pipes/buffer scenarios | 3/3 |
| SQL Server LocalDB / source workflow | 47/47 / PASS, реальные rows/schema/оба Preview |
| Preview Memory | **36/36 × 3**; 348 уникальных PreviewWindow, 132 GC checkpoints |
| SQL generated project | Restore/build/run PASS; Customers=3, Orders=2, visible values/resize |
| Structural generated project | PASS, 20 native controls, 8 inserted types, real parent/order/Canvas/Combo/List selection/popup |

PostgreSQL live server не использовался; существующие offline/contract regressions маркированы отдельно. Eremex isolation покрыта прежними suites и отдельным collection/Preview lifecycle. Generated template сохраняет прежний net6.0/NETSDK1138; версии не менялись.

Все stress cases выполнялись отдельно, без параллельных UI suites и без retry. Собственный MTA Tree UIA client обращается только к HWND целевого PID, читает реальные pattern-availability properties; никакие пользовательские команды не исполняются. Перед финальным GC подтверждён STOP/exit клиента. Checkpoints при ещё живом клиенте сохраняют возможное удержание и не выдаются за leak-free state.

| Независимые lifecycle cases | Циклы | Финальные живые references |
|---|---:|---:|
| Minimal ordinary Window, ambient | 100 | 0 |
| Minimal ordinary Window, owned Tree UIA client | 100 | 0 после client exit |
| Ordinary Window в Designer smoke host, тот же shared probe/client | 100 | 0 |
| Collection Editor: Menu, ContextMenu, Flyout, TreeView, TabControl, ComboBox, ListBox | 7 × 100 | 0 Windows/sessions |
| Legacy / AXAML / imported AXAML Preview | 3 × 100 | 0 |
| Popup/Flyout / Menu/ContextMenu | 2 × 100 | 0 tracked Window/popup references |
| Nested mutations / Multi Form / New Project | 3 × 100 | 0 |
| Реальный Undo/Redo при активных UIA queries | 20 | 0 |
| **Всего stress** | **1820** | **0 после owned-client cleanup** |

[Stress CSV](PreviewMemoryPhase4EStress.csv) и [resource checkpoints CSV](PreviewMemoryPhase4EResources.csv) содержат duration, managed bytes, private bytes, handles, threads и active windows каждые 25 циклов. Это process observations при connected client; private bytes не равны точному native heap size. Проверяются реальные объекты WeakReference, а не только снижение рабочего набора. Полный ручной пользовательский запуск Standalone/установленного VSIX не выдаётся за выполненный: shell MainWindow создавался реальным Avalonia smoke host с TestHostServices; чистый probe запускался отдельным приложением без Designer.

Сопоставимый benchmark использует прежний `--universal4c-profile`: 5 warm-up, 30 samples на каждую из восьми операций Small/Medium/Large, неизменный MainWindow 483577 bytes, SHA-256 `06DDDF98789FD874FBB8C871022E49312DD5C1A74987C6618FCFBE22A0F17697`. Before — сохранённый final Phase 4D `after/structural.json`, After — fresh Phase 4E output. Это одинаковые workflow/timer boundaries, не новая оптимизация business code. Client environment изменён осознанно: прежняя собственная GUI UIA session завершена. Чужой длительный dotnet process не останавливался; абсолютные P95/variance имеют ограничение фоновой нагрузки. Benchmark не выполнялся одновременно с нашими builds/SQL/stress/UI tools.

Итоговый integrity check: **720+720 samples**, 30 неповторяющихся iterations в каждой группе; outliers не отбрасывались. Preview timer включает Show/loading helper/Close, не physical first pixel; allocations — current UI-thread managed allocations, GC — process-wide, peak Working Set — high-water mark отдельного process run. Undo/Redo settle остаётся вне timer.

| Большой AXAML | Phase 4D median/P95, мс | Phase 4E median/P95, мс | Allocations MB, D → E |
|---|---:|---:|---:|
| Insert | 547,76 / 644,50 | 500,48 / 557,76 | 250,88 → 242,45 |
| Reorder | 277,89 / 341,51 | 265,53 / 285,97 | 193,02 → 184,63 |
| Reparent | 470,13 / 577,80 | 418,29 / 476,81 | 229,39 → 220,98 |
| Remove subtree | 278,39 / 297,00 | 259,51 / 287,30 | 173,68 → 165,29 |
| Undo | 341,14 / 404,15 | 291,72 / 328,61 | 187,07 → 178,67 |
| Redo | 287,22 / 319,93 | 268,34 / 287,94 | 181,44 → 173,04 |
| Create patch | 93,19 / 106,45 | 79,87 / 107,04 | 74,41 → 74,41 |
| Preview Refresh | 416,75 / 452,96 | 411,16 / 437,96 | 71,19 → 71,16 |

Large Preview GC totals по 30 samples: **131/48/10 → 128/41/0** (gen0/gen1/gen2). Large Insert: **384/223/66 → 370/205/50**. Peak Working Set полного процесса **616,28 → 630,35 MB**, то есть вырос на 2,3%, несмотря на нулевые post-client weak references. Это не называется leak и не скрывается. Patch P95 немного вырос: 106,45 → 107,04 мс. Полные Small/Medium/Large, GC/variance/peak: [Performance CSV](PreviewMemoryPhase4EPerformance.csv).

Нового алгоритмического ускорения в business code не заявляется: production diff пуст, UIA client environment изменён, фоновые процессы не контролировались. Оптимизации 4D не откатывались. Данных достаточно для отсутствия существенной деградации заданных больших workflow в этой серии, но не для обещания таких latency в любом окружении.

## 8. Native crash и ограничения

`uiautomationcore.dll → ProviderCallouts::GetPatternProvider`, null provider, остаётся **UNRESOLVED**. В heap retention нет истории появления того null provider или native call boundary исходных WER crashes. Совпадение технологии UIA не устанавливает причинный механизм. Memory 36/36 не объявляется исправлением native AV. Все новые AV сохраняются прежним full-capture pipeline; intentional CLR/file failure учитывается отдельно от native crashes.

Сводка capture этой фазы: **47 process runs, 0 native AV, 0 capture failures, 0 capture timeouts**. Два nonzero exits: первый неожиданный managed termination forensic prototype без полного managed dump и отдельный intentional locked-file self-test с full CLR dump. Оба сохранены; они не объявлены успешными runtime tests и не смешаны с историческим UIA AV. Последний исправленный snapshot/teardown case завершился exit 0. Capture summary/manifest/SHA-256 и protected copies остаются вне Git.

Обследованы существующие server heaps и controlled client teardown, не восстановлены все remote COM owner tables каждого исторического case. Production остаётся совместимой с живой accessibility. Полноценная installed VSIX/DPI manual acceptance из 4D по-прежнему отдельная незавершённая работа; этот этап её не заменяет.

## 9. Изменённые файлы и рекомендации

Production files не изменены. Diagnostic/test files:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PreviewMemory.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.UiaStability.cs`
* `smoke-tests/UiaStability/Common/PlainWindowProbe.cs`
* `smoke-tests/UiaStability/MinimalAvalonia/Program.cs`
* `smoke-tests/UiaStability/NativeCapture/Program.cs`
* `smoke-tests/UiaStability/RunCaptured.ps1`
* `smoke-tests/UiaStability/WriteProbeSignal.ps1`
* Documentation и machine-readable evidence этой фазы.

Дополнительно: `smoke-tests/UniversalMetadata/Summarize4EPerformance.py`, `smoke-tests/UiaStability/README.md`, `Docs/PreviewMemoryPhase4EInitialFailures.csv`, `PreviewMemoryPhase4EStress.csv`, `PreviewMemoryPhase4EResources.csv`, `PreviewMemoryPhase4EPerformance.csv`, `Phase4EUiAutomationRetentionReproducer.md` и этот отчёт. Regenerated generic metadata coverage семантически сравнен с HEAD с нормализацией только NewLine; содержательных изменений нет, исходные файлы восстановлены после проверки, generated evidence сохранён отдельно в `regenerated-coverage`.

Перед следующими memory audits фиксировать client lifetime и разделять connected-client observation / post-client GC gate; освобождать только собственные diagnostics resources. Для нового production leak сначала получать per-case census/heap, а не изменять COM pointers. Сохранять прежний native crash capture и отдельно завершить VSIX/DPI acceptance. Ни обновление dependencies, ни следующая фаза автоматически не начинаются.

Условия оценки: отсутствие continuing retention подтверждено указанной clean-process матрицей; долгоживущий внешний UIA client по-прежнему способен удерживать закрытый объект. Не восстановлены индивидуальные roots всех семи исторических assertions, и permanent production leak на их основании не доказан. Не найден новый P0/P1 в проверенной production границе. Native AV не исправлен. Оценка не означает универсальной leak freedom или завершённой installed-VSIX/DPI приёмки. Commit/push не выполнялись, следующая фаза не начата.
