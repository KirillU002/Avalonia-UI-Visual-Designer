# Universal Controls Phase 3B.1 — UI Automation native crash investigation

Дата: 14.11.2026. Baseline: `0b6a1874b94bff81e364fd55973e333796f6b3a5`, main, дерево изначально чистое; Phase3B закоммичена. Продукт Beta0.1, Avalonia11.1.1, main/runner net8.0, runtime8.0.32 x64, SDK10.0.402, Windows10Home22H2 **19045.6456**. Изменения production-кода после исследования **не оставлены**. NuGet/TFM/JSON/VSIX/accessibility/settings не менялись. Phase4 не начиналась.

## 1. Исторические симптомы и материалы

Оба исходных WER dump найдены и прочитаны; пользовательские файлы не изменялись. Исходные отчёты 3B/3A/PreviewMemoryLeakFix/StabilizationAudit/BetaReadinessAudit исследованы, исторический Phase7A.2 crash отделён от новых наблюдений.

| PID | Время МСК | Exception thread | Size | Результат |
|---:|---|---:|---:|---|
|40752|14.11 20:12:21|74716|45 611 193 B|0xC0000005, uiautomationcore+0x47593|
|74048|14.11 20:26:41|63268|45 848 374 B|то же|

`previous-dumps.json` содержит SHA256 (082a80e9…729c82e / eed62c30…5045c2), exception/module/thread stream metadata. В обоих 96 threads, 168/169 modules; **Eremex отсутствует**. Наличие/отсутствие других assemblies не объявляется доказательством причины. Остальные WER dotnet dumps этого дня с 0xe0434352 классифицированы отдельно как managed termination.

## 2. Native stack и границы вывода

Использован установленный Microsoft DbgEng10.0.19041.3636 через маленький read-only host. Для symbol loading нужны локальные SDK dbghelp/symsrv рядом с диагностическим host; системные DLL/реестр не изменялись. Точный Microsoft public PDB для UIAutomationCore7.2.19041.5794: **5F5B60563531913BDC1025B38920B87B1**, 6 393 856 B. Это устранило прежнее неверное чтение ближайшего export `UiaReturnRawElementProvider+0x17f03` как имени функции.

```text
ProviderCallouts::GetPatternProvider+0x2f
UiaNode::ProviderGetPatternProvider+0x5a
UiaNode::ProviderGetPropertyValue+0x186
InProcClientAPIStub::UiaNode_GetPropertyValues+0xec
UiaNode::CrossProcess_GetPropertyValues+0x348
RemoteUiaNodeStub::Incoming_GetPropertyValues+0x1db
RemoteUiaNodeStub::OnMessage
InvokeOnCorrectContext_Callback
ProcessIncomingRequest / ChannelBasedServerConnection::OnData
ReadWriteChannelInfo::Service / OverlappedIOManager::IoThreadProc
```

В обоих RSI=0, `mov rax,[rsi]`; запрошен pattern **10002 (Value)**. Caller получает null из `UiaNode::GetSlotAsProvider` и передаёт его в callout без проверки. Это установленный **непосредственный механизм отказа**. Почему provider slot стал null — пока **не установлено**. Faulting worker не является Avalonia UI thread; UI thread в Win32 message loop. В fault stack нет managed frame/прямого Close/Dispose вызова. Minidumps содержат registers/stacks/части памяти, **не полный native heap**: соответствующий UiaNode object недоступен. Нельзя доказать race/use-after-free или виновника release по одному RSI=0.

Исходные и resolved stacks, module lists, disassembly и PDB identity находятся в `artifacts/universal-phase3b1`. Download исходника Avalonia11.1.1 подтвердил: GetPatternProvider сам обращается к peer, большая часть других getters маршалится через UI Dispatcher; crash происходит **до** COM-call provider, поэтому этот threading факт не объявляется root cause.

## 3. Baseline и тестовая инфраструктура

До изменений: solution 0 errors / 1 прежний NU1603, runner0/0. Collections30/30, PropertyGrid30/30, PreviewPhase2 28/28. Свежий Inspector96,64/138,63ms, allocations5 548 052 B; старые96,03/146,86/5,48MB — historical reference.

Runner [STAThread], реальный Avalonia Win32 dispatcher через SetupWithoutStarting, UI operations на UI thread. Не headless. Каждая stress scenario — новый процесс, собственный Application/Dispatcher, без retries. Controlled UIA clients — отдельные MTA процессы с HWND/PID guard; они не перечисляют desktop, не читают пользовательский text/SQL и не вызывают actions. Ambient external UIA clients не контролируются, поэтому режим без нашего клиента называется **ambient**, не “UIA off”.

Root client использует legacy UiaNode APIs только для диагностики; Tree client — shipped Windows UIAutomation managed wrapper, до128 descendants/depth8, запрашивает IsValuePatternAvailable, затем продолжает запросы при Close. Живое UIA остаётся включённым. Покрытие/команды — `smoke-tests/UiaStability`.

## 4. Эксперименты и классификация отказов

Первая серия Root-client: plain/editor/оба Preview/popup/menu/mutations/undo/forms/project, 100 циклов каждого, 1000 завершённых, exit0. Отдельный ambient plain100 exit0. Это не доказательство устранения редкого native crash.

Tree-client серия: оба Preview завершили по100 циклов без AV, но strict memory assertion упал (managed exit **-532462766**): 100 живых окон после idle/GC при ещё подключённом клиенте. Другие scenarios — см. итоговую таблицу. Первый forms harness создавал новую форму на каждый цикл; перед финальным замером изменён на переключение двух подготовленных forms, поэтому его memory growth не выдаётся за leak.

### GC root и client lifecycle

Снят собственный heap160 230 710 B, три Preview. SOS для каждого показал ref-counted COM handle RefCount1 → **AutomationNode (не RootAutomationNode)** → TextBlockAutomationPeer/NoneAutomationPeer → cached peer children → ChromeOverlayLayer/TitleBar → ValueStore → PreviewWindow. Root release диагностирован 3/3 (в серии100/100). Animation clock или collection session root в этой цепочке отсутствует.

Дифференциально изменён один фактор: завершение нашего UIA client **до** server GC assertion. Legacy3/3 и AXAML3/3 после этого освобождаются при неизменённом production cleanup. Это доказывает зависимость данного удержания от жизни клиентских references/cache, не доказывает постоянную утечку. При живом долгоживущем клиенте closed-window retention всё равно важен как P2 и не скрывается. Обычный Window negative control уже имел другой подтверждённый root RootAutomationNode → WindowAutomationPeer; историческая связь с AV не установлена.

### Отклонённый descendant-disconnect эксперимент

Для проверки гипотезы неполного root-only cleanup в существующем pinned adapter временно перечислены **уже существующие** providers закрываемого дерева, children перед root, в прежнем WM_DESTROY hook. Новые peers не создавались. Это привело к:

* Legacy PID67132, 21:56:27: **native AV**, exit-1073741819, offset **0x5d615**, `InProcClientAPIStub::InvokeInProcAPI+0x25`, invalid RCX, дальше тот же remote GetPropertyValues/IO worker path.
* AXAML PID41020: **managed termination**, клиент получил COMException **0x80004005 “Element provider is NULL. Cannot retrieve property values”**, server assertion потерял DONE. Это не второй native AV.

Это **другая signature**, её нельзя выдавать за воспроизведение исторического +0x47593. Эксперимент показывает небезопасность выбранного расширения disconnect во время входящих запросов. **Полностью отклонён и production-файл восстановлен**, diff Services/PreviewAutomationLifetime.cs пустой. Patch/source/logs/dump analysis сохранены; безопасным workaround он не является. Не менялись глобальная UIA, доступность живых controls или версии библиотек.

## 5. Итоговая stress matrix

Результаты отражают все серии, не только успешный повтор. `Alive` — число живых WeakReference targets, а не число аварий; editor cases содержат ссылки и на Window, и на session. `AliveSessions=0` проверяется отдельно. Нет жёстких timing/Working Set порогов.

| Режим | Scenario | Итог | Duration, s | Alive targets после GC |
|---|---|---|---:|---:|
|ambient|plain|100/100 assertions PASS|7.51|99|
|root client|plain|100/100 assertions PASS|17.12|100|
|root client|preview-legacy|100/100 assertions PASS|48.59|0|
|root client|preview-axaml|100/100 assertions PASS|49.36|0|
|root client|editor|100/100 assertions PASS|24.89|100|
|root client|popup|100/100 assertions PASS|21.31|100|
|root client|menu|100/100 assertions PASS|19.91|100|
|root client|mutations|100/100 assertions PASS|26.73|83|
|root client|undo|100/100 assertions PASS|81.83|100|
|root client|forms|100/100 assertions PASS|78.41|100|
|root client|project|100/100 assertions PASS|34.68|100|
|tree client connected|plain|100/100 assertions PASS|18.87|100|
|tree client connected|preview-legacy|FAIL, exit -532462766|51.61|—|
|tree client connected|preview-axaml|FAIL, exit -532462766|52.65|—|
|tree client connected|editor|100/100 assertions PASS|35.36|100|
|tree client connected|popup|100/100 assertions PASS|25.49|100|
|tree client connected|menu|100/100 assertions PASS|23.96|100|
|tree client connected|mutations|100/100 assertions PASS|36.40|100|
|tree client connected|undo|100/100 assertions PASS|85.77|100|
|tree client connected|forms|100/100 assertions PASS|51.60|100|
|tree client connected|project|100/100 assertions PASS|40.63|100|
|tree client released before GC|preview-legacy|100/100 assertions PASS|49.64|0|
|tree client released before GC|preview-axaml|100/100 assertions PASS|50.59|0|
|tree client released before GC|editor|100/100 assertions PASS|26.06|100|
|tree client released before GC|forms|100/100 assertions PASS|36.38|100|
|tree client released before GC|project|100/100 assertions PASS|40.04|100|

Tree-connected Preview прошли цикл Close, но **не прошли итоговый memory assertion**; завершение процесса managed exception не называется успешным тестом. После завершения собственного клиента strict Preview collectibility даёт 0/100 в обоих режимах. Это отдельный lifecycle эксперимент, не retry и не удаление исходного assertion. Collection sessions освобождаются (0/100), сами обычные editor Window могут оставаться в native UIA cache (100/100); общий ordinary Window negative control остаётся P2.

На checkpoints25/50/75/100 записаны elapsed, managed bytes, PrivateBytes, handles, threads, ActivePreviewWindows. Например root-client plain: managed10,67→29,94MB, handles856→861, threads91→92, alive25→100; tree-client AXAML финальный after-release: 0 старых Preview. Рост private bytes сам по себе не объявлен leak/исчерпанием. Ни один из этих процессов не завершился timeout/deadlock/resource-exhaustion exception. Full raw tables: `stress-*/results.json`, `checkpoints.jsonl`; новые native signatures разбираются отдельно.


## 6. Дифференциальная Phase3A / shutdown / concurrency

Phase3A `9f78cc0` собрана отдельно через git archive, без checkout/reset/worktree mutations. Изначально memory26/28: оба Eremex checks не дошли до assertions из-за test lookup `bin/Debug/net8.0/Plugins` при Release-only build, **не** retained Preview. Release plugin payload скопирован только внутри архивной fixture в этот прежний lookup; C# baseline не менялся. После исправления fixture: **28/28**, forced root UIA; current Phase3B в тех же условиях **28/28**. Оба stabilization24/25: ordinary Window negative control красный, обе Preview collectible проверки PASS. Это не доказательство одинаковой причины исторических AV; source diff показывает отсутствие изменений Win32 provider/Preview hook между этими commits.

Controlled Tree-client, plain100: два последовательных процесса **2/2 exit0**, и два одновременно работающих независимых процесса **2/2 exit0** (те же100 cycles/owned client). Это не тест concurrent доступа к одному Dispatcher: Application внутри каждого процесса отдельный, что намеренно исключает unsupported cross-thread UI use. Общие native regression suites используют один STA/Application **последовательно**.

Shutdown/restart: plain/editor/Legacy/AXAML по3 новых процесса ×1 цикл, **12/12 exit0**, COMPLETE и normal process exit, без зависших owned clients. Это повтор запуска test Application, не замена ручной проверки интерактивного installed Designer. Отдельная ClassicDesktopLifetime граница проверяется обоими generated runtime; она не является строго однофакторным сравнением с test SetupWithoutStarting.

Истинный Avalonia.Headless не запускался: пакет не является зависимостью этих suites, новый NuGet ради этого не добавлялся. Metadata/model-only checks не названы headless/Win32-negative-control. Наличие ambient UIA/screen reader за пределами owned clients не контролировалось; physical screen reader acceptance остаётся открытым.

## 7. Regression / Preview / Export

| Проверка | Результат |
|---|---|
|Solution / runner Release|0 ошибок; solution1 прежний NU1603, runner0 warnings|
|Collections / PropertyGrid|30/30, 30/30|
|Metadata / Phase2|45/45, 40/40|
|GridDrop / Nested / Pointer|40/40, 59/59, 26/26|
|PreviewPhase2 / consistency|28/28, 32/32|
|Undo cache|25/25|
|Current forced-root UIA memory без SQL|28/28; содержит20 циклов обоих режимов и native SendMessage/cancelled-close paths|
|Stabilization|**24/25**, ordinary Window negative control; это не “всё зелёное”|
|Broad|**72/72**, включая source-preserving AXAML, VSIX/VsHost/Named Pipes/ACK/conflicts и generated runtimes|
|LocalDB/DataGrid|**47/47**, CONTRACT/LIVE labels сохранены; Settings/schema/BindingSource/delivery обоих Preview PASS|
|Preview memory + SQL|**36/36**, cancellation/close-during-query, real rows, clock/context checks|
|SQL export/runtime|PASS, отдельное приложение: Customers3 / Orders2, independent VM и resize|
|Collection Grid/Canvas generated runtime|Оба restore/build/run **PASS**, реальные6 roots/коллекции, popup/tab/tree interaction и resize|
|Eremex isolation|Отдельные cold/missing-theme/missing-runtime процессы — **3/3 exit0**|

Все regression suites/native benchmarks запускались последовательно. Deliberate parallel plain experiment отделён и выполнялся после benchmarks, не подмешивался в timings. Бинарные сборки соответствуют восстановленному production HEAD; отклонённый эксперимент в них отсутствует.

LocalDB создана с GUID instance `UniversalMetadata_465d2ee5c4dc4b0bab8d4130c9d2aa89`, отдельная fixture Customers/Orders/EmptyTable; после DROP DATABASE instance остановлен/удалён. Пользовательские БД не менялись. VSIX packaging проверен solution build; интерактивная установка/Visual Studio не проверялась. Live PostgreSQL не запускался.

Первые reds сохранены: 26/28 отсутствующая Eremex fixture в архиве, 24/25 negative-control, два Tree-connected strict retention failures, rejected disconnect AV/COM errors. Нормальный последующий exit0 не отменяет ни один из них. Cleanup AccessDenied на loaded generated DLL в broad отмечен отдельно от функционального PASS.

## 8. Performance

Production/hot-path алгоритмы не изменены, поэтому ускорение от “исправления AV” не заявляется. Сначала отдельно повторён прежний Inspector benchmark: 3 warm-up, 30 measured transitions, Release, без debugger/параллельных builds/suites.

|Inspector|Median ms|P95 ms|Median UI-thread B|
|---|---:|---:|---:|
|Fresh task baseline|96,64|138,63|5 548 052|
|После диагностических работ|84,30|153,62|5 339 868|

Median ниже, **P95 выше на10,8%**, allocations ниже3,8%; это вариативность наблюдений, не доказанный эффект test-only кода. Metadata rebuilds0. Исторические Phase3A96,03/146,86/5,48MB и Phase3B102,21/163,74 не подменяют свежую пару.

Сокращённый N100-only profile165samples дал Add12,36ms, reorder6,30ms. Он имеет другую preceding warm-up/history sequence, поэтому **не принят как сопоставимое улучшение** относительно Phase3B полного1/10/100. После завершения всех прочих процессов повторён полный прежний порядок: **495samples =450 measured +45warm-up**, каждый scenario30 наблюдений. CI thresholds отсутствуют.

|100 items / операция|Phase3B median / P95 ms|Новый full profile median / P95 ms|Alloc median / P95 MiB|Compositor median / P95 ms|GC0/1/2 за30|
|---|---:|---:|---:|---:|---|
|Add|47,175 / 81,063|76.40 / 115.96|2.35 / 2.60|77.63 / 117.13|6/6/0|
|Reorder|21,265 / 26,354|43.42 / 67.45|1.81 / 1.86|44.01 / 68.25|3/3/0|

Полные остальные Open/Selection/NestedAdd/AddSequence10/100 и allocations/P95/GC — `performance-full/summary.json`. Timing включает действие + editor.UpdateLayout; allocation window до compositor callback относится к UI thread, не whole-process/native heap. Callback не является physical first pixel. Длительности100-cycle stress включают диагностические ожидания/client handshake/GC и не используются как latency одного действия.

В полном повторе Add median вырос относительно прежнего отчёта на62%, reorder — на104%. **Отсутствие performance-регрессии этими результатами не подтверждено**; при неизменённом production hot path и сильной зависимости от preceding sequence причина разброса не установлена. CPU/ETW attribution не снимался. Среда (ambient UIA/OS scheduling) не полностью контролируется; строгий причинный performance A/B production cleanup не получен, поскольку candidate был отклонён. Изменять Layout/Undo ради этих чисел не стали.

## 9. Изменённые файлы и воспроизводимость

Production-файлов в итоговом diff нет. Менялись только:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`: dispatch `--uia-stability`.
* Новый `Program.UiaStability.cs`: owned process/Window/session/checkpoint harness, mixed hierarchy/Undo/Close cases, bounded forensic hold, учёт client lifecycle.
* `Program.Collections3B.cs`: test-only output/count options, чтобы не перезаписывать прежние observations.
* `smoke-tests/UiaStability/RunStress.ps1`: fresh output guard, отдельные processes, timeout/exit/result classification, без retries.
* `UiaClient`/`UiaTreeClient`: два локальных диагностических клиента. net8.0-windows относится только к новому UIA helper; существующие TargetFramework не менялись.
* `DumpAnalyzer`: read-only DbgEng host, без debugger NuGet в приложении.
* `Experiments/PreviewAutomationLifetime.children.cs.txt`, `ReproduceRejectedExperiment.ps1`: отклонённый opt-in пример, не компилируется в приложение, применяется только в новой git-archive копии. Сам source experiment выполнен и его crash проанализирован; convenience isolated-driver имеет syntax check, но отдельный полный запуск этого wrapper не выдаётся за новый результат.
* `smoke-tests/UiaStability/README.md` и этот отчёт.

После всех процессов удалены только10 проверенных owned targets: собственный heap, локальный tool/PDB/SDK DLL outputs, bin/obj и распакованная архивная3A fixture. Пользовательские WER dumps сохранены; исходный небольшой source archive и все текстовые доказательства остались. Объём task artifacts уменьшен с1065MB до**11,42MiB**, cleanup failures0. Для повторного анализа PDB/tool надо получить заново по сохранённым identity/source commands.

Evidence — игнорируемые `artifacts/universal-phase3b1`: логи всех первых/повторных runs, JSON/module/exception inventories, symbolicated native stacks, SOS gcroot, отвергнутый patch и raw benchmarks. Бинарные dumps/SDK libraries/PDB/собственный heap не включены в Git. Source round-trip/VSIX protocol не менялись.

Primary references: [Microsoft UiaDisconnectProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider) — per-provider cleanup, запрет outbound call из input-synchronous SendMessage и reentrant republication; [server-side providers](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider) — resources уничтоженного элемента; [DbgEng](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-debugcreate); [pinned Avalonia AutomationNode](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/Automation/AutomationNode.cs). Из этих контрактов нельзя вывести подтверждённого виновника исторического crash без additional trace.

Реестр WER, глобальные UIA settings, system DLLs и accessibility не менялись. Диагностический сырой HWND-less provider probe вернул E_INVALIDARG на Disconnect, поэтому **не является** пригодным positive reproducer и не засчитан как успешная проверка teardown.

## 10. Решение и ограничения

**NOT READY.** Непосредственный native fault исторических двух dumps установлен точнее, но инициирующая причина null provider не доказана и безопасное исправление не получено. Дополнительный native crash произошёл в отклонённом эксперименте. Зелёные прогоны на восстановленном production path не закрывают исходную проблему.

Перед Phase4 требуется отдельный native lifetime trace/full crash heap и проверка upstream Win32/Avalonia contract под Windows10UIA7.2.19041.5794. Не назначать виновником Eremex, Inspector recycling, Windows или Preview только по module name/корреляции. Не отключать accessibility и не распространять unsafe disconnect на все окна. Physical screen reader/installed VSIX/DPI/другие Windows builds и многочасовой soak в этой среде не сертифицированы. PostgreSQL live не проверялся.

### Manual acceptance / следующий диагностический шаг

- [ ] Собрать full native crash heap/COM lifetime trace **на исходном +0x47593**, не подменять его snapshot с недостаточным heap или другой experimental signature.
- [ ] Повторить с настоящим долгоживущим screen reader/Inspect client: read-only navigation/value-pattern queries при Close Preview/editor и открытом popup.
- [ ] В отдельной VM сравнить Windows/UIAutomationCore builds при неизменённых app/runtime/settings; текущую систему не обновляли ради успеха.
- [ ] Проверить interactive VSIX, user close/cancel/send-message и shutdown вне smoke host, physical DPI/несколько мониторов.
- [ ] Отдельно решить closed ordinary Window/native client cache P2. Не заменять его production GC или blanket disconnect всех providers.

До этого безопасный режим работы с исследованием — изолированные процессы/сохранение всех diagnostics, **не** отключение accessibility, не retries и не опубликование rejected adapter.
