# Phase 4D — Structural AXAML Performance и VSIX synchronization

Дата: 16.11.2026. Исходный HEAD `36e0464b6dea0d18197093a87c80ccae0a9def29`, ветка `main`, рабочее дерево перед началом чистое. Продукт Beta 0.1, Avalonia 11.1.1, Windows 10 x64 build 19045, .NET runtime 8.0.32, SDK 10.0.402. Изучены Phase 4A/4B/4C/3B.3/5E и действующая реализация. NuGet, TargetFramework, JSON contract, IPC wire protocol, набор controls/structural operations, тема и accessibility не менялись. Commit/push не выполнялись.

**STRUCTURAL EDITING PERFORMANCE: READY WITH CONDITIONS.**

Performance/source/IPC gate реализован и проверен. **Полный memory gate не зелёный: Preview Memory 29/36.** Удержание воспроизводится и на сохранённой исходной сборке Phase 4C; ниже приведены heap/GC-root доказательства. Безусловная стабильность Preview, установленного VSIX и DPI не заявляется. UIA AV остаётся UNRESOLVED.

## 1. Подтверждённые причины задержек

Сопоставимый baseline снят с исходного Release runner, сохранённого в `artifacts/universal-phase4d/before-bin`. Использован неизменённый `--universal4c-profile`: 5 warm-up, затем 30 измерений на каждую из восьми операций, Small/Medium/Large. До и после — по 720 samples. MainWindow: 483577 bytes, SHA-256 `06DDDF98789FD874FBB8C871022E49312DD5C1A74987C6618FCFBE22A0F17697`.

Обнаружены конкретные повторные действия:

1. `GenerateExportedAxamlPreviewSnapshot` строил модельный AXAML/export workspace перед использованием source native projection. На большом source документе этот промежуточный путь создавал очень много временных объектов; часть source preview запускалась через exception/fallback. Пользовательский исходный AXAML при этом всё равно не загружался runtime loader.
2. `AxamlCandidate` сериализовал уже независимо созданный DTO в JSON и тут же десериализовал его. Mapper уже копирует mutable collections; дополнительный deep clone избыточен.
3. После структурного patch один и тот же текст повторно токенизировался в `ImportAxamlProjection` и импортёре, хотя writer уже разобрал и проверил результат.
4. `BeginGridEditorUndoBatch` запрашивал снимок текущего состояния повторно, в том числе сразу после завершённой структурной транзакции. Для последовательных операций повторную сериализацию можно пропустить при достоверно актуальном snapshot. После произвольного изменения/загрузки применяется консервативная invalidation.
5. ACK повторно импортировал точно тот же source и перестраивал PropertyGrid, хотя текст, модель и source identities уже были проверены при локальном commit.

Дополнительное подтверждение лишнего export VM: в каждом из 30 больших baseline Preview создавались 27 Inspector rows; после изменения — 0 дополнительных rows в этой границе. Designer PropertyGrid при этом не удаляется. Stage самой native source projection: 99,86 → 80,44 мс median; главное ускорение полного Preview достигнуто вне этого builder, без упрощения его дерева.

На baseline большого Insert: `CreateUndoSnapshot` 128,13 мс, 82,26 MB, 2 вызова; `AXAML.StructuralPatch` 106,00 мс; `AXAML.StructuralSourceMapUpdate` 92,67 мс, 60,34 MB. Stages вложены и **не суммируются**. Повторное metadata reflection discovery не объявляется причиной без соответствующего измерения.

## 2. Минимальные изменения

* Imported Preview сразу создаёт snapshot с `SourceKind=ImportedAxaml` и независимым Document DTO, затем использует прежний `SetImportedSourceContext` / `AxamlLayoutProjection`. Удалена промежуточная генерация модельного AXAML и клонирование export workspace. JSON Preview/Export не менялись. Безопасная native projection, opaque warnings и запрет исполнения пользовательских bindings/templates сохранены.
* `AxamlCandidate` возвращает результат прежнего mapper. Отдельный regression меняет имя, custom properties и список controls кандидата и проверяет отсутствие изменений в live model/source.
* `AxamlPatchResult.ParsedSyntax` — internal ссылка на уже проверенный immutable syntax result. Existing importer принимает его через internal `ImportParsed`; обычный публичный `Import` работает как раньше. XML validation и проверка identities/ParentId сохранены.
* Документный флаг актуальности history snapshot устраняет лишнюю сериализацию между последовательными source operations. Общая точка изменения состояния инвалидирует флаг, включая untracked notifications. Один logical command по-прежнему создаёт один history entry. Первый Insert после загрузки остаётся консервативным: в измерении ещё 2 сериализации.
* ACK переиспользует Source Map/ControlModel только при совпадении текущего snapshot, snapshot проверенной проекции и точного текста ACK. Scalar edit после структурного command инвалидирует право такого переиспользования. Delayed ACK использует прежний полный путь, сохраняет более новые dirty edits.
* При ACK архивируются только отсутствующие в новом map IDs; присутствующие записи не копируют избыточные старые maps. Доказательства удаления для Undo сохраняются в документе. Глобальные caches/strong references не добавлены.
* Existing busy state сопровождает структурный commit и блокирует reentrant commit; состояние снимается в `finally`. Ни `Task.Delay`, ни обращения к UI controls через `Task.Run` не добавлены.

## 3. Performance Before/After

Статистика — без удаления outliers. Allocations: managed allocations текущего UI thread, MB = 10⁶ bytes. GC — process-wide collection count, peak Working Set — high-water mark процесса, **не** retained heap отдельного действия. Benchmarks, build, SQL и UI suites не запускались нами параллельно. В системе оставался чужой длительно работающий dotnet process; он не останавливался. Поэтому абсолютные latency/P95 зависят от конкурирующей нагрузки.

Timer structural commands включает синхронную работу VM; Undo/Redo settle остаётся вне timer, как в финальной методике 4C. Preview Refresh включает создание/Show/ожидание loading и Close test helper; это не время физического первого кадра. Измерения не подменяются историческими цифрами 4B/4C.

Большой MainWindow, 30 samples на строку:

| Операция | До median/P95, мс | После median/P95, мс | Alloc до → после, MB |
|---|---:|---:|---:|
| Insert | 600,24 / 709,55 | 547,76 / 644,50 | 273,51 → 250,88 |
| Remove subtree | 329,60 / 431,87 | 278,39 / 297,00 | 205,70 → 173,68 |
| Reorder | 322,68 / 410,90 | 277,89 / 341,51 | 225,05 → 193,02 |
| Reparent | 523,48 / 648,36 | 470,13 / 577,80 | 253,04 → 229,39 |
| Undo | 344,79 / 452,60 | 341,14 / 404,15 | 192,53 → 187,07 |
| Redo | 293,88 / 357,77 | 287,22 / 319,93 | 186,88 → 181,44 |
| Create patch | 81,09 / 141,27 | 93,19 / 106,45 | 74,41 → 74,41 |
| Preview Refresh | 1223,43 / 1367,04 | 416,75 / 452,96 | 1050,08 → 71,19 |

Median Preview уменьшился на 65,9%, его allocations — на 93,2%. Insert — на 8,7%, Remove — на 15,5%, Reorder — на 13,9%, Reparent — на 10,2%. **Существенного ускорения Undo нет.** Median самостоятельной генерации patch вырос на 14,9% при практически идентичных allocations; P95 при этом снизился. Этот результат не скрывается и не называется ускорением writer: его основной алгоритм не менялся, границы safe validation сохранены.

Не все малые/средние группы ускорились: Medium Reorder median 15,00 → 16,62 мс, Medium Insert P95 42,16 → 59,95 мс. Детальная variance/GC сохранена; этих данных недостаточно, чтобы объявить конкурирующую нагрузку доказанной причиной каждого выброса. Общего утверждения об ускорении всех сценариев нет.

Полные Small/Medium/Large, variance, GC, peak memory и invocation counts: [Performance CSV](AxamlStructuralPhase4DPerformance.csv), [Stages CSV](AxamlStructuralPhase4DStages.csv). Raw samples и capture metadata — вне Git в `artifacts/universal-phase4d/{before,after}`. Проверка целостности подтверждает 720+720 samples, 30 неповторяющихся iterations в каждой группе.

ACK-only profile — отдельная граница, подготовка patch/локальный commit/transport вне timer, 5 warm-up + 30 samples на сцену. Это **не** полный VSIX round-trip и не Before/After benchmark ACK.

| Сцена | Median/P95 ACK, мс | Alloc, MB |
|---|---:|---:|
| Small | 1,35 / 2,65 | 0,17 |
| Medium | 4,96 / 11,49 | 3,60 |
| Large | 73,51 / 249,60 | 44,61 |

Во всех 90 measured ACK проверяется identity Source Map/selection. Полный `MarkDocumentSaved` всё ещё сохраняет form/workspace state и обновляет tabs/explorer; это остаётся стоимостью ACK. Small имеет единичный большой outlier, отражённый в StdDev CSV; он не удалён. [ACK CSV](AxamlStructuralPhase4DAck.csv).

## 4. Source Map и incremental synchronization

Существенное безопасное переиспользование реализовано **на точном ACK**, а не как новый универсальный incremental parser. Идентичный Source Map и неизменённые live ControlModel сохраняются, не выполняется повторный import/PropertyGrid rebuild. Parsed syntax writer также используется повторно в первом проверочном remap.

После самого Insert/Remove/Reorder/Reparent сохраняется полный semantic remap существующим importer. Он проверяет точное множество ControlId и ParentId перед публикацией, сохраняет namespaces/property elements и source spans. Для большого Insert stage source-map update: 92,67 → 86,89 мс, 60,34 → 54,88 MB. Native map не обновляется сдвигом offsets наугад: перенос subtree, namespace context и archived Undo fragments делают такой путь отдельной задачей. Delayed ACK и несовпадение projection snapshot получают полный fallback, конфликтный/неоднозначный результат отклоняется.

При локальном commit неизменённые ControlModel сохраняются по ID; новый Source Map проверяется до изменения live collection. ControlId/selection/ParentId и raw subtree XML проверяются существующей 4C матрицей. Нового Document Model, Metadata Engine, Preview Engine или XML parser нет.

## 5. Undo/Redo и AXAML safety

Прежние insert/remove/reorder/reparent, panels Grid/Canvas/StackPanel/DockPanel/WrapPanel и две static item collections сохраняют source-preserving writer. Bindings, resources, styles/templates, comments, namespace prefixes, неизвестные attributes и внутренний XML перемещаемого subtree не регенерируются. Unsafe mapping/attached expressions/template contexts остаются запрещёнными.

Проверяются фактические edits через общий VS text validator, exact Undo исходного текста, Redo, повторный import и source identities. Scalar edit перед structural command не теряется при оптимизации истории. ACK старой проекции с новым scalar snapshot отклоняется. Для delayed ACK восстановление более нового рабочего source выполняется до публикации нового baseline; сохранённым становится отправленный snapshot, а не более новые edits.

## 6. VSIX/IPC и transaction safety

Wire protocol и архитектура VSIX остаются прежними. Изменён только lifecycle существующего VsHost bridge:

* Одновременно допускается один in-flight patch. Duplicate/stale ACK не сохраняет более новые edits и не меняет чужую session.
* ACK проверяет request/document/session, version, checksum и точное expected text. Сначала синхронизируется VM, только после успеха продвигается transport baseline. Ошибка VM synchronization не оставляет bridge с преждевременно продвинутой версией.
* После реального disconnect сервер снова принимает подключение. Неопределённый результат отправленного patch не replay-ится. Apply блокируется до explicit Reload фактического VS buffer.
* Автоматический Open при reconnect с несохранённым состоянием возвращает existing Error envelope с `RECOVERY_RELOAD_REQUIRED`; он не выбрасывает dirty edits. Explicit Reload использует прежние ReloadRequested/ReloadDocument messages.
* Invalid ACK, conflict/Error и send failure требуют reload. Отсутствующий ACK оставляет Apply заблокированным; автоматического retry нет. Пользователь может явно запросить reload. CloseDocument аннулирует pending transaction; поздний ACK игнорируется.

Новый реальный named-pipe сценарий проверяет text buffer validator, delayed/duplicate ACK, повторный Apply до ACK, разрыв после применения buffer до ACK, reconnect без replay, явный recovery, invalid ACK и закрытие документа во время patch. Он не является интерактивной проверкой установленного VSIX.

Разделение состояния сохранено: локальная working projection может отличаться от acknowledged VS baseline и остаётся dirty до Apply/ACK. Потеря IPC не означает, что локальная операция опубликована в VS; она не откатывается молча и не повторяется автоматически. Явный Reload согласует состояние с фактическим buffer. Синхронный local command не имеет нового cancellation API; закрытие/сообщения UI обрабатываются после него. Физическая DTE buffer latency и network transit отдельно не benchmark-ились.

## 7. UI thread, PropertyGrid, Explorer и Preview

Структурные команды остаются синхронными на Avalonia UI thread. Измеряемые command latency — также время его блокировки; полный first-render pipeline отдельно не измерялся. Busy state и reentrant guard не означают, что индикатор успеет отрисоваться до синхронной работы. Большие Insert/Reparent около половины секунды всё ещё заметны; фоновой обработки UI controls нет.

Точное ACK не вызывает `ApplyDocument`, re-import и PropertyGrid rebuild. Полный RestoreFromSnapshot при Undo/Redo остаётся, отсюда ограниченное ускорение. Favorites/search/categories/typed editors и source Value Source не переписаны. Архитектура preview не заменена: ускорена подготовка входа прежней безопасной source projection, а не точность layout. Binding/resource значения не вычисляются и не выдаются за полное визуальное соответствие произвольному AXAML.

## 8. Memory и UI Automation

Дополнительная isolated retention-проверка прошла: 100 Add/Remove операций на small source, 865,51 мс суммарно, 90,61 MB all-thread allocations, GC 6/2/1. Managed heap без принудительного GC до/после цикла 18,08/16,33 MB; handles 573 → 577, threads 34 → 34. Это не измерение утечки по разнице heap/handles. После New Project и lifecycle cleanup принадлежащей тесту VM, трёх GC/finalizer/dispatcher проходов **0/52 WeakReference живы**: два source document и 50 удалённых моделей. Managed heap после cleanup 9,16 MB. Это доказательство сборки этих объектов в данном сценарии, не доказательство отсутствия любых native/Window leaks. Логи: `memory.log`, `memory-capture/result.json`.

История удалённых source fragments документная; длительная сессия может сохранять необходимые для Undo source strings/maps. Нового global metadata/editor cache нет. Peak Working Set полного benchmark: до 1462,77 MB, после 616,28 MB; это процессный peak разных запусков, не steady-state retained heap.

### Красный Preview Memory и ограниченная диагностика

Полный live matrix: **29/36**, exit 1; NativeCapture не зарегистрировал AV. Красные checks: `Legacy_TwentyCycles`, `Legacy_Eremex`, `Legacy_CancelledOwnerClose`, `Legacy_CloseDuringAsyncContractLoad`, `AXAML_NoDataGrid`, `AXAML_StaticRows`, `AXAML_TwentyCycles`. Во всех красных assertions остался жив закрытый PreviewWindow; lifecycle logs содержат CLOSED/DISPOSED и PREVIEW_AUTOMATION_RELEASED. Это не считается доказательством постоянной утечки и не скрывается успешными другими checks.

Выполнено ограниченное диагностическое сравнение **сохранённого baseline runner** и current runner, одинаковый `TwentyCycles` filter, без изменения production/таймаутов, с opt-in heap dump при удержании:

| Сборка | Focused Memory | Heap dumps |
|---|---:|---:|
| Phase 4C baseline | 0/2 | 2 |
| Phase 4D current | 1/2 | 1 |

Это не повтор полной матрицы для замены 29/36. Два случая не позволяют оценить изменение частоты. Они показывают, что для воспроизведения удержания изменения 4D не требуются; Legacy JSON Preview вообще не использует новую source Preview ветку.

В baseline heap `retained-5679078fc0a04225ae367e3e1def2036.dmp` SOS обнаружил один root:

```text
HandleTable: ref counted handle, RefCount 1
 -> Avalonia.Win32.Automation.RootAutomationNode
 -> Avalonia.Automation.Peers.WindowAutomationPeer
 -> FormDesigner.Views.PreviewWindow (01757b7251a0)
```

В current heap `retained-78f7cc901cc2408c8e71d674e4ec1380.dmp` есть PreviewWindow `014173024c38`, но `gcroot` нашёл **0 unique roots**. Наличие объекта в heap само по себе не означает живой root: освобождение COM reference между проверкой WeakReference и остановкой для dump совместимо с этими данными, но точный момент освобождения не установлен. Владелец baseline COM reference и временное либо постоянное удержание не определены. Это ограниченно изолированная прежняя UIA/COM retention-проблема, а не подтверждённая новая утечка Document Model/Source Map. Связь с прежним null-provider AV **не доказана**.

Три heap dumps (177–224 MB) сохранены вне Git в `retention-baseline/` и `retention-current/`; уникальные имена, capture metadata, `hashes.json`, SOS `heap-stats.log` / `window-gcroot.log` сохранены. SHA-256 baseline исследованного heap: `AD5017B98B0F7FECE103DBE06CA2F697FD76D480B95D07C7F6D37EF7F36E5004`; current: `19F75D82DAB38A915A351267731E4ECE8E5D059503A1877EF301E0541E09E4EE`. Это **managed heap diagnostics**, не новые native crash dumps. Ни UIA disable, ни COM lifetime workaround, ни увеличение GC wait/retry не применялись.

Исторический `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` crash остаётся **UNRESOLVED**. Accessibility не отключалась, production COM/AutomationPeer lifetime не менялся. В 32 capture runs: native crashes 0, capture failures 0, capture timeouts 0. Assertion/permission failures имеют exit 1 и учтены отдельно, а не объявлены успешными. Отсутствие нового AV не доказывает устранение старого. Ordinary Window retention остаётся отдельным известным ограничением; новый полный Stabilization audit не запускался.

## 9. Build, regression и первые красные прогоны

Baseline и финальный solution build — PASS, 0 ошибок. Solution сохраняет прежнее предупреждение NU1603 о VSSDK analyzer fallback 17.7.20 → 17.7.22; версии не менялись. Финальный runner, включая source-retention probe, собирается без ошибок/предупреждений. Generated template остаётся net6.0 с прежним NETSDK1138, не мигрировался.

| Проверка | Фактический результат |
|---|---|
| Phase 4D checks | 14/14 |
| Phase 4C | 85/85 |
| Phase 4A / Phase 4B | 38/38 / 52/52 |
| Phase 5E | 58/58 |
| PropertyGrid / Collections | 30/30 / 30/30 |
| Layout7 | 15/15 |
| Metadata | 45/45 |
| Pointer / Grid Drop | 26/26 / 40/40 |
| Broad, normal environment | 72/72 |
| Named Pipes/VSIX buffer scenarios, normal environment | 3/3, включая новый transaction/recovery case |
| SQL Server LocalDB | 47/47 |
| SQL source/settings/schema workflow | PASS, реальные данные и оба Preview |
| Preview Memory, первый полный live run | **29/36**, красный результат сохранён |
| Source-session retention | PASS, 0/52 weak references alive после cleanup |
| Paired structural benchmark | 720+720 samples, integrity PASS |
| Exact ACK profile | 90 samples; 0 source-map rebuild / 0 Inspector refresh |
| SQL Export/Generated Runtime | PASS, Customers=3, nested Orders=2, visible values/resize |
| Structural source Export/Generated Runtime | PASS, 20 native controls, 8 inserted types, parent/order/Canvas/raw Tag, Combo/List selection/popup |
| Installed VSIX / physical DPI | **НЕ ВЫПОЛНЕНО**, checklist подготовлен |

SQL runtime проверен отдельным fresh owned LocalDB fixture после остановки первой live matrix на Memory failure; исходный Memory результат не перезаписывался. Оба созданных GUID LocalDB instances удалены в `finally`, пользовательские instances/данные не затрагивались. PostgreSQL live server отсутствовал: только явно маркированные contract/offline regressions, без заявления live PASS. Eremex isolation и JSON/Multi Form/Export покрыты существующими матрицами; сторонняя production integration не переписана.

Evidence: `acceptance/results.json` и suite logs, `normal-final/{broad-normal,ipc}.log`, `live-final/`, `live-export-final/`, `generated/structural-runtime.txt`, `capture-summary.json`, исходные Before/After JSON. Все пути относительно ignored `artifacts/universal-phase4d`.

Первые красные результаты сохранены:

* `checks-first.log`: 12/13, тест предполагал exact reuse сразу после первого Insert. Pending scalar defaults означали, что ACK text отличался от рабочей проекции; необходим полный fallback. После уточнения контракта добавлен snapshot guard и отдельный тест против ACK старой scalar projection. Финальный standalone suite — 14/14.
* `ipc-second.log`, `ipc-trace.log`: новый recovery-тест timeout, старые два сценария PASS. Trace локализовал порядок test-client Send/Receive: ожидание завершения server send до чтения блокировалось на named-pipe backpressure. Тест теперь читает одновременно с асинхронной отправкой; timeout/retry не увеличивались. Production transport ради этого не менялся.
* `ipc-receive.log`: 0/3, `UnauthorizedAccessException` в sandbox при named-pipe connect. Отдельный normal-environment run `ipc-receive-normal.log` — 3/3, без отключения тестов и изменения IPC permissions production.
* В successful Broad run автоматическая очистка собственной generated output получила `UnauthorizedAccessException` для занятого DLL; в раннем IPC run — `IOException` для занятого каталога. Assertions/exit code успешны; каталоги сохранены, логи и capture metadata не удалялись. Это cleanup warning, а не native crash или исправленная lifecycle-проблема.

Metadata tests повторно сгенерировали прежний coverage с платформенными NewLine/line-ending отличиями. CSV/JSON семантически сравнены с HEAD, нормализуя только переводы строк; все остальные значения идентичны. Исходные файлы восстановлены только после проверки, regenerated evidence сохранено в ignored `regenerated-coverage`. Новых capabilities/coverage claims нет.

## 10. Установленная Visual Studio и DPI

VS Community 2022 17.14.37806.7 доступна и имеет открытую пользовательскую solution/sample AXAML. Инструмент интерактивного управления получил `FrameArrived timed out`, затем `coordinate input geometry is unavailable`. Accessibility tree прочитан, но операции установленного VSIX не выполнены. Пользовательская VS не закрывалась и её настройки/DPI не менялись.

Подготовлен [подробный manual acceptance checklist](Phase4DVisualStudioAcceptance.md): установка текущей VSIX сборки; Insert/Remove/Reorder/Reparent; buffer/ACK; Undo/Redo; manual conflict; reconnect/close/restart; DPI 100/125/150%, scroll/zoom/multiple monitors. Автоматические IPC tests не заменяют эту приёмку.

## 11. Изменённые файлы

Production:

* `AvaloniaDesigner.VsHost/VsHostBridge.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.Structure.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs`
* `ViewModels/MainWindowViewModel.AxamlStructure.cs`
* `ViewModels/MainWindowViewModel.cs`

Tests/evidence:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4D.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4DIpc.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4DPerformance.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal4DMemory.cs`
* `smoke-tests/UniversalMetadata/Summarize4DPerformance.py`
* `smoke-tests/UniversalMetadata/README.md`
* `Docs/AxamlStructuralPhase4DPerformance.csv`
* `Docs/AxamlStructuralPhase4DStages.csv`
* `Docs/AxamlStructuralPhase4DAck.csv`
* `Docs/Phase4DVisualStudioAcceptance.md`
* `Docs/UniversalControlsAndPropertiesPhase4D.md`

Ignored scripts, raw JSON, logs и crash capture metadata находятся в `artifacts/universal-phase4d`; бинарные dumps не включаются в Git.

## 12. Ограничения и следующий этап

Нет универсального incremental AST/source-map updater и частичного перестроения Preview subtree. Полный semantic import структурной транзакции и RestoreFromSnapshot остаются; source safety имеет приоритет. Автоматическое background UI editing не реализовано. ACK/workspace persistence и PropertyGrid P95 требуют отдельного профилирования. Patch-only median вырос, несмотря на снижение P95; нужны дополнительные сопоставимые серии прежде, чем делать причинный вывод об этой вариации.

Перед расширением возможностей: завершить installed-VSIX/DPI acceptance, профилировать оставшиеся UI model/history/layout stages, отдельно проектировать проверяемую incremental remap с full fallback и bounded source-history retention. Сначала сохранить текущие source/ACK safety gates. Новые структурные операции, controls и следующая фаза автоматически не начинаются.

Условия итоговой оценки: принять измеренное ограничение полного remap/Undo и UI blocking; отдельно закрыть подтверждённый красный Preview Memory gate с сохранёнными heap evidence; завершить installed-VSIX/DPI acceptance. Эта оценка **не означает Memory 36/36, устранение UIA AV или безусловную release readiness**. Новых подтверждённых повреждений AXAML/Undo или потери IPC edits по проверенной границе не найдено. Работы этой фазы завершены, следующая фаза не начата.
