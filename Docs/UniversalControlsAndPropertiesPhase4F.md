# Phase 4F — Visual Studio Extension Integration, End-to-End Acceptance & Stability

Дата: 17.11.2026. Baseline: `f5a347a9a2a29f9d782610249f4bbfc4d0d46a47`, `main`, исходное дерево Git чистое. Windows 10 22H2 19045.6456 x64; .NET runtime 8.0.32, SDK 10.0.402; Avalonia 11.1.1; Visual Studio Community 2022 17.14.37806.7. VSIX 0.1.18; номер версии, NuGet, TargetFramework, IPC protocol и JSON contract не изменены. Новых компонентов/свойств нет.

## 1. Архитектура и границы проверки

Сохранена цепочка `devenv.exe → VSIX Bridge (net472) → Named Pipes → AvaloniaDesigner.VsHost.exe (net8.0) → существующий MainWindow/DesignerSurface`. VSIX владеет VS text buffer; VsHost хранит acknowledged source baseline и рабочую Designer-проекцию, возвращая minimal text edits. Общие PropertyGrid, Collection Editor, importer/source map/patch writer, история и Export Pipeline не заменялись.

Assembly references финального VSIX: mscorlib, Microsoft.VisualStudio.Shell.15.0, Shell.Framework, Threading, Interop, System, System.Core, System.Design, AvaloniaDesigner.Host.Protocol. Прямых ссылок на Avalonia, Eremex и FormDesigner нет; visual DLLs находятся в отдельном VsHost payload. Это проверка dependency boundary, а не полный census всех managed assemblies любых сторонних расширений в пользовательском devenv.

Изучены отчёты 4B/4C/4D/4E/3B.3 и текущие VSIX manifest/VSCT, launcher, transport, buffer, session, source map, writer и ACK handlers. Структурные операции и source semantics сохранены: пять panel-контейнеров, ограниченный safe subset коллекций, opaque Templates/Resources, fallback semantic remap, exact-ACK reuse. Полного incremental parser или второй модели не добавлено.

## 2. Подтверждённые ошибки и минимальные исправления

### 2.1. Несохранённые Designer edits при OpenDocument/DocumentChanged

Root cause: `VsHostBridge.OpenDocumentAsync` проверял pending patch, checksum и version, но обычный OpenDocument/DocumentChanged мог вызвать `LoadAxamlImportedDocument` при `HasUnsavedChanges`. Защита `_requiresReload` действовала только после uncertain IPC и не покрывала обычное переключение файла или повторное открытие. Рабочая source-проекция и история заменялись без Apply/ACK.

Первый новый regression run: **3 PASS / 3 FAIL**. Подтверждены три разных пути: тот же document, другой document, DocumentChanged текущего document. Перед заменой сессии теперь возвращается `UNSAVED_DESIGNER_CHANGES`, статус предлагает Apply к исходному документу либо явный Reload. Source, snapshot, session и selection остаются прежними. Явный Reload сохранён как осознанный recovery/discard path. Это не реализация нескольких одновременно сохраняемых Designer-сессий.

Первоначальная реализация guard использовала только HasUnsavedChanges. Второй Broad и focused trace выявили отдельную ошибку этой итерации: dirty=True, sourceChanges=False. Snapshot включает workspace/projection state и не является точным признаком AXAML edits. Финальный guard обращается к существующему CreateActiveAxamlPatch: реальные source changes защищены даже до обновления snapshot dirty flag; валидный no-change source не блокируется из-за workspace-only различий. Непроверяемое dirty состояние и ошибки validation обрабатываются консервативно. Checksum/version проверяются раньше guard. Первый Open заменяет стартовую JSON-модель host по прежнему контракту; защита применяется к уже активному AXAML.

Добавлены четыре проверки этой границы. Prototype rapid-edit fixture первоначально использовал Binding и не достигал условия literal edit; сохранён его первый failure, fixture исправлен на literal, Binding не делался редактируемым. Focused run также выявил ошибочное распространение helper на initial JSON seed; scope уточнён. Финальные targeted checks — 15/15, real-pipe lifecycle — 1/1 перед полным Broad.

### 2.2. Устаревший receive callback

Root cause: receive loop читал mutable `_connection`; его `finally` без identity guard закрывал текущий connection. Завершение старого loop после подключения нового могло отключить новый pipe. Детерминированный тест задерживает EOF старого stream, устанавливает replacement, затем завершает EOF; исходный код терял replacement.

Loop теперь захватывает собственный connection; disconnect использует `Interlocked.CompareExchange` с ожидаемым экземпляром. Callback завершившегося Process проверяет identity процесса. Автоматического повторения patch нет.

### 2.3. Завершение запросов и ACK

Dispose закрывал connection, но сам не завершал pending request completions. Теперь ожидающие запросы получают ObjectDisposedException; после Dispose повторный EnsureConnected отклоняется. `SendPatchAppliedAsync` больше не возвращает успешный CompletedTask при отсутствующем pipe: неопределённый результат buffer/ACK требует recovery, а не ложного успеха.

Первый transport run: **6 PASS / 3 FAIL**; отдельные failures — stale loop, stranded request, disconnected ACK. Исправления проверяются на source-linked production `VsHostBridgeClient`, а не на копии transport implementation. В net8 runner есть только SDK/DTO stand-ins: они не эмулируют DTE или настоящий VS package.

### 2.4. Штатное завершение host и сохранность dirty edits

Первый shutdown run: **9 PASS / 2 FAIL**. Bridge не запрашивал shutdown дочернего host при teardown; существующий HostShutdown, напротив, мог безусловно закрыть окно с неприменёнными правками. Добавлен bounded best-effort `ShutdownAsync`, который посылает существующий HostShutdown, затем освобождает transport. Package Dispose вызывает его через JoinableTaskFactory.Run; UI-awaiting transport не добавлен, compiler warning о синхронном GetResult устранён переходом на async.

VsHost автоматически закрывается только при отсутствии фактических source edits/pending ACK (для JSON-состояния сохранён dirty guard). Workspace-only snapshot difference при валидном no-change patch не удерживает чистое окно. Иначе окно остаётся открытым с объяснением. Это намеренно сохранённая возможность восстановления, а не скрытый orphan process. Forced kill, retry неидемпотентного patch и освобождение чужих COM pointers не используются. Аварийное завершение devenv и восстановление оставшегося dirty host не объявляются полностью проверенными.

## 3. Packaging и установка

Финальный VSIX: **219 356 683 bytes**, **639 entries**, версия **0.1.18**. SHA-256: `4f35dd41ecb90ef27d95b5b93f81de364bcfb13235a07bde9686567ab2524c88`.

Проверены manifest, executable, runtimeconfig, deps, FormDesigner DLL, relative paths и dependency closure runtime/native/RID assets. В корне bridge нет visual runtime assemblies. Dumps, MDF/LDF, logs, user/suo files и файлы secrets/settings из запрещённого списка отсутствуют; JSON payload состоит из deps/runtimeconfig и plugin.runtime.json. Один protocol PDB присутствует как symbols; PDB не является crash dump. Первый локальный audit ошибочно запрещал любые PDB — это предположение проверки исправлено, production packaging ради него не менялся.

Расширение установлено CLI VSIXInstaller в изолированный `RootSuffix Codex4F20261117`, instance `3b0ec686`. Предварительная тестовая установка удалена штатным installer, затем установлен финальный пакет той же версии. Обычная пользовательская установка не менялась агентом. Финальная директория: `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_3b0ec686Codex4F20261117\Extensions\o33ky5fj.zmx`. SHA-256 семи ключевых установленных файлов совпали с entries последнего VSIX. После последнего host-only refinement тестовая установка обновлена ещё раз; интерактивный VS при этом не запускался. Встроенного developer absolute-path fallback нет; launcher использует соседний `VsHost\AvaloniaDesigner.VsHost.exe`.

## 4. Реальная Visual Studio: что подтверждено

Первый запуск тестового VS потребовал ручного завершения экрана входа; аутентификация не автоматизировалась. Инструмент Computer Use затем возвращал stale element indexes, `FrameArrived/window capture timed out` и `coordinate input geometry is unavailable`. Физические действия выполнял пользователь; результат независимо сверялся по сохранённым AXAML, checksum и журналу.

В ранее установленной обычной сборке (`17.0_3b0ec686\Extensions\o2u0zfn5.0ot`) реально выполнены command → отдельный VsHost → import копии → PropertyGrid edit → ApplyPatch → VS buffer → ACK → Save. В `artifacts/universal-phase4f/manual-sample/MainWindow.axaml` изменён Content на Phase4F и добавлен TextBlock. Версии 2→3→4; ACK checksums совпали с файлом. После обратного удаления только вставленного TextBlock и возврата Content исходный текст совпал побайтно: комментарий, namespace declarations, неизвестный custom attribute и соседние controls сохранены. Исходный sample и рабочий Views/MainWindow.axaml агентом не редактировались.

Дополнительный ручной reopen копии подтвердил сохранённый Phase4F и restart host в той же старой установке. Это **не проверка финального DLL**: `/Edit` перенаправил файл в уже запущенный обычный VS, что обнаружено по extension root/журналу. Затем отдельный финальный VS запущен без `/Edit`, через `FinalPhase4FTest.sln`; PID 10156 и CommandLine с RootSuffix подтверждены. Это был UI-launch предыдущей итерации пакета до последнего source-guard refinement. Пользователь выбрал продолжение **без дальнейшей ручной проверки**. Установка финального payload и запуск экспериментального VS подтверждены, интерактивное E2E финальной сборки — **NOT VERIFIED**. Успех старой установки не переносится на новую только из-за совпадающей версии 0.1.18.

Полная матрица физических DPI 100/125/150%, resize/reparent мышью, manual conflict и закрытие VS во время patch не выполнена. Для них оставлен [воспроизводимый checklist](Phase4FVisualStudioAcceptance.md).

## 5. Source-preserving, история и Multi Document

Writer/parser не менялись. Существующие tests проверяют реальные source bytes/edits, XML well-formedness, ControlId/ParentId/order, Grid/Canvas/StackPanel/DockPanel/WrapPanel, namespace prefixes, comments/whitespace, bindings/resources, Styles/Templates и unknown attributes. Undo/Redo и Reset не заменяются регенерацией AXAML. Unsafe mapping и template descendants остаются ограничены capabilities.

У одного bridge/host остаётся **один активный AXAML buffer/session**. Clean switch поддержан; stale/wrong-document ACK не меняет другую сессию. Dirty switch теперь отклоняется без потери source/history/selection; Apply должен завершиться для исходного документа либо пользователь выбирает явный Reload. Независимые истории нескольких одновременно открытых AXAML в одном host не реализованы и не объявляются поддержанными. JSON Multi Form сохраняет существующий путь.

## 6. IPC, ACK и конфликты

Protocol не изменён. Сохранены expectedVersion/checksum, реальное чтение VS buffer до edits, один in-flight structural patch, delayed/duplicate ACK guards, snapshot-bound dirty state и explicit recovery после uncertain disconnect. Source buffer обновляется DTE UndoContext, а не File.WriteAllText. Ошибки не приводят к автоматическому replay Insert/Reparent.

Реальные pipe scenarios 4D/4C/4A отдельно проверяют normal ACK, delayed/duplicate/invalid ACK, потерю ACK после применения текста, reconnect без replay, CloseDocument с поздним ACK, scalar property elements и source preservation. Новые 4F memory-stream cases вызывают настоящий host message handler и дополнительно проверяют dirty replacement/close/shutdown; они не выдаются за интерактивный DTE тест.

VSIX не добавляет полноценную новую подписку на все DTE document lifecycle events: закрытый исходный buffer отклоняется прежней проверкой FindOpenDocument при Apply, затем требуется recovery. Это безопасная граница, но мгновенное автоматическое отражение любого VS close/change и полный reconnect lifecycle после аварийного выхода IDE остаются отдельной ручной приёмкой.

## 7. Независимые VsHost процессы

Из распакованного финального VSIX выполнены **5 warm-up + 50 измеряемых независимых процессов** под существующим NativeCapture. Каждому выделены уникальные pipe/capture-directory. В каждом: Hello/ACK, OpenDocument первого source, CloseDocument/barrier, OpenDocument другого source и HostShutdown. Проверены CanEdit, request/document IDs, exit code и отсутствие собственного surviving PID.

Итог: **55/55 процессов exit 0**, **110 OpenDocument**, **55 CloseDocument**, **55 HostShutdown**, **0 survivors**, **0 captured native crashes/timeouts/capture failures**. Это реальные desktop VsHost, не headless substitute; VSSDK/DTE в этих процессах нет. Per-process PID, modules, timings, private/working-set bytes, handles/threads и capture results сохранены вне Git.

Финальная серия относится к пакету SHA-256 4f35dd41…; предварительная серия 55/55 сохранена отдельно и не используется как её замена. Дополнительно `ProbeInstalledVsHost.ps1` запустил текущий executable из установленного `o33ky5fj.zmx` под full capture, открыл три реальных source fixtures и подтвердил exit 0. Усиленный external-process smoke с явным установленным путём открыл те же fixtures вперёд/назад — 1/1, с проверкой ненулевого exit code. Это проверка реального installed host, но не выполнение команды текущего VSIX внутри devenv.

Первая настройка этого harness имела неверный путь к exe: host не запускался, parent завершился managed `0xE0434352` (exit -532462766). `Captured=false`, full managed dump не получен; сохранены launch/result metadata. Это не native UIA AV и не успешная итерация stress. После исправления пути использован отдельный fresh run directory; первоначальный результат не удалён.

## 8. Memory и UI Automation

Полный SQL-enabled Memory Gate: **36/36 в каждом из трёх независимых процессов**. В каждом — 44 post-GC checkpoints; AliveWindows/Contexts/Items/Grids/ActiveWindows равны 0. В inventory суммарно 216 наблюдений Window references с повторным учётом растущих TwentyCycles batches; это **не 216 уникальных окон**. Только сценарии TwentyCycles дают 120 реальных open/close Preview в двух режимах за три процесса; дополнительные сценарии проверяют Multi Form, owner/cancelled Close, async SQL cancellation и Eremex. Критерии GC, ожидания и assertions не ослаблялись.

Собственный Computer Use/UIA client завершён до gate; чужие клиенты/экранные дикторы не завершались, accessibility не отключалась. Удержание во время жизни внешнего UIA client остаётся известной границей 4E. Успешный gate не означает универсального отсутствия retention при любом внешнем клиенте.

Отдельно выполнены 100 source Insert/Remove: **52/52 weak references собраны после закрытия source-сессии**, alive=0. Allocations всей серии 90,51 MB; heap 18,27→16,48 MB, после закрытия/GC 9,47 MB; handles 570→574, threads 34→34. Небольшое увеличение handles не выдаётся за доказанную утечку или отсутствие любой native retention.

55 host-процессов после двух document opens имели private bytes median **202,52 MB**, range **200,61–204,50 MB**; working set median **214,71 MB**, range **213,51–217,15 MB**; handles **864–878**, threads **91–93**. Эти показатели — ресурсы процесса, а не размер managed heap. После штатного shutdown surviving PID нет.

Исторический `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Нового такого AV не получено; связать старый null provider с Window retention или исправленными bridge callbacks по этим тестам нельзя. Существующие full dump/SHA-256/metadata/unique directory/protected vault сохранены; новых COM/GCHandle workaround нет.

## 9. Производительность

Сопоставимый core benchmark использует тот же `Views/MainWindow.axaml`, small/medium fixtures, 5 warm-up + 30 observations на каждую из восьми операций и трёх сцен: **720 measured samples**. Baseline взят из raw Phase 4E workflow, а не из округлённого Phase 4D Insert 547,76 мс. Полные Median/P95/StdDev, allocations, GC0/1/2 и peak working set — в [CSV](Phase4FPerformance.csv). Samples/outliers не отбрасывались.

| Large, операция | 4E Median/P95, мс | 4F Median/P95, мс | 4E→4F allocations, MB |
|---|---:|---:|---:|
| Insert | 500,48 / 557,76 | 536,37 / 552,12 | 242,45→242,45 |
| Remove subtree | 259,51 / 287,30 | 274,45 / 302,53 | 165,29→165,29 |
| Reorder | 265,53 / 285,97 | 287,13 / 305,53 | 184,63→184,63 |
| Reparent | 418,29 / 476,81 | 452,11 / 481,40 | 220,98→220,98 |
| Undo | 291,72 / 328,61 | 331,59 / 352,01 | 178,67→178,67 |
| Redo | 268,34 / 287,94 | 289,17 / 313,83 | 173,04→173,04 |
| Create patch | 79,87 / 107,04 | 79,90 / 108,89 | 74,41→74,41 |
| Imported Preview | 411,16 / 437,96 | 421,53 / 445,63 | 71,16→71,16 |

Preview median +2,52%; allocations прежние. Core peak WS в этой серии **630,35→591,39 MB**. У нескольких structural medians рост около 6–14%; причина временной вариации **не изолирована**. Algorithms/importer/writer/Preview в этой фазе не менялись, но фоновые процессы и состояние ОС не контролировались как независимый фактор. Поэтому нельзя ни приписать рост конкретному production change, ни объявить полную performance-equivalence. Сохраняется P2 больших документов; speculative optimization не выполнялась. Full semantic remap, history restoration и synchronous UI work остаются известными bottlenecks 4D.

ACK-only: ещё 90 samples (5 warm-up + 30 на сцену), Large **68,69/120,05 мс**, **44,61 MB**, 0 source-map rebuilds. Это `MarkAxamlRoundTripSaved`, без подготовки patch, transit и DTE. Для сравнения тот же профиль 4D давал 73,51/249,60 мс; причинное ускорение новой фазы не заявляется. Peak ACK-profile WS **1596,01 MB** против **1529,07 MB** в 4D остаётся ограничением ресурсоёмкого повторного workflow; успешные weak-reference checks не заменяют анализа этого peak. [ACK CSV](Phase4FAckPerformance.csv).

PropertyGrid: original scene, 30 samples, native editors со сверкой ContextControlId; selection+layout **89,23/187,19 мс**, median allocation **6,90 MB**, selection отдельно **55,64/118,79 мс**, metadata rebuilds=0, GC0/1/2=[14,8,0]. Phase 3A имела другой набор доступных свойств; её 96,03/146,86 мс и 5,48 MB не являются равным Before для доказательства ускорения этой фазы. Высокий P95 остаётся P2. RenderScaling=1 в native profile не сертифицирует физический DPI/мышь.

Отдельные 50 measured debugger-controlled host runs: launch+Hello median/P95 **4229,88/4517,61 мс**, первый small OpenDocument **1606,87/2178,80 мс**, повторное открытие другого source **636,09/719,97 мс**. Здесь включены debugger/process/window initialization и настоящий IPC; это не чистый importer и не latency команды в devenv. Child managed allocations не инструментировались: вместо вымышленного значения 0 записаны private/working-set bytes. Фактические интервалы старой ручной установки сохранены только как smoke evidence, не как сопоставимый benchmark финальной сборки.

## 10. Build и regression

Финальная production-сборка решения: **0 ошибок**, прежний NU1603 (локальный fallback SDK.Analyzers 17.7.20→17.7.22); версии зависимостей не менялись. Smoke runner после обновления контракта: **0 ошибок / 0 предупреждений**. В промежуточном build был VSTHRD002 для sync GetResult; финальный async shutdown/JTF path его устраняет, предупреждение не подавлялось.

| Проверка | Результат / граница |
|---|---|
| Phase 4F host/client regressions | **15/15**, исходные красные прогоны сохранены |
| Packaging / dependency closure / relative paths | **PASS**, установленный payload сверяется по hashes |
| Phase 4C | **85/85** |
| Phase 4D | **14/14** |
| Phase 4A / 4B | **38/38 / 52/52** |
| Phase 5E | **58/58** |
| PropertyGrid / Collections | **30/30 / 30/30** |
| Layout7 / Metadata | **15/15 / 45/45** |
| Pointer / routed Grid Drop | **26/26 / 40/40**, включает Eremex isolation; не physical DPI |
| Broad normal Windows | Два первых run **71/72**; финальный **72/72** |
| Dedicated real-pipe IPC | **3/3**, финальный source guard |
| SQL Server LocalDB | **47/47** + live source pipeline и generated SQL runtime |
| Preview Memory | **36/36 ×3**, отдельные процессы |
| Source/session memory | **52 references, 0 alive** после 100 операций |
| Generated patched-source runtime | **20 native nodes**, 8 вставленных типов, parent/order/Canvas/raw Tag, Combo/List=3 и native selection/popup |
| Native desktop host cycles | **55/55**, 110 opens, 55 closes/shutdowns, 0 surviving PID |
| Performance | 720 core +90 ACK +30 Inspector samples; все validity assertions выполнены |

Первый Broad 71/72 не скрыт. `AxamlHardeningIpcEditReloadLifecycle` после delayed ACK намеренно сохранял «Next edit» dirty, затем использовал обычный OpenDocument для discard. Новый guard корректно запрещает это. Тест усилен: реальный pipe теперь сначала проверяет `UNSAVED_DESIGNER_CHANGES` и сохранность нового edit, затем использует explicit ReloadDocument. Исходные asserts о minimal edits, late ACK, checksum/version и реальном MainWindow сохранены. После contract correction второй Broad остался 71/72: обнаружена описанная выше ошибочная трактовка workspace dirty как source dirty. Focused trace и три targeted boundaries привели к source-based guard; initial seed дополнительно покрыт четвёртым тестом. После этих production refinements полный Broad — 72/72, IPC — 3/3. При заключительном аудите обнаружены hardcoded Debug пути старого harness для внешнего host и VSIX archive: они заменены build configuration самого runner (AssemblyConfigurationAttribute), существующий override установленного exe сохранён. Добавлена явная проверка exit code дочернего host. Новый полный Release-aligned Broad снова 72/72, IPC 3/3; актуальный Release package проходит существующие packaging assertions. Ранее пройденные Debug-asset проверки не выдаются за проверку последнего payload. Ни один первый отказ не заменён успешным повтором без изменения исследуемого условия/кода. Пропущенные первым wrapper IPC cases засчитаны только после отдельного финального выполнения.

В Broad остались известные cleanup warnings о заблокированном DLL внутри собственного generated output. Это не failed assertion и не native AV; файлы принудительно не удалялись. Все suites выполнялись последовательно; IPC/LocalDB — в normal Windows permissions. Sandbox permissions production-кодом не обходились. PostgreSQL live не проверялся, его отсутствие явно SKIP в прежнем SQL harness.

Standalone покрывается JSON Save/Open, Grid-first/Canvas, Toolbox/routed Drop, PropertyGrid, Collection Editor, selection, resize, Explorer, Multi Form, оба Preview, Export и generated runtime в существующей матрице. Оно не объявляется ручным полным UX-тестом Standalone. Structural source changes проверяются actual AXAML, а не только успешным return метода.

## 11. Изменённые файлы

Production:

* `AvaloniaDesigner.VSIX/AvaloniaDesignerVsixPackage.cs` — package teardown вызывает bounded shutdown bridge.
* `AvaloniaDesigner.VSIX/VsHostBridgeClient.cs` — connection identity, pending request completion, честная ошибка disconnected ACK, async shutdown/local dispose.
* `AvaloniaDesigner.VsHost/VsHostBridge.cs` — защита dirty session при Open/DocumentChanged и сохранность edits при shutdown.

Tests/diagnostics:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, `.csproj` — новые flags и source link реального client.
* `Program.Universal4F.cs`, `Program.Universal4FProcess.cs`, `VsixTransportTestTypes.cs` — regression и isolated desktop processes; SDK stand-ins не заменяют devenv.
* `Program.AxamlHardening.cs` — strengthened real-pipe dirty/reload lifecycle test и обязательный child exit-code assertion. `Program.cs` также выбирает host/VSIX assets по фактической build configuration вместо hardcoded Debug.
* `smoke-tests/UniversalMetadata/CheckPhase4FPackage.py`, `Summarize4FPerformance.py`, `ProbeInstalledVsHost.ps1` — воспроизводимый payload audit и проверка количества/сопоставимости samples.

Документация: этот отчёт, `Phase4FVisualStudioAcceptance.md`, `Phase4FPerformance.csv`, `Phase4FPerformance.host.json`, `Phase4FAckPerformance.csv`, `Phase4FEvidenceSummary.json`. Memory/core performance измерялись до последнего host-only refinement, но SHA-256 общей FormDesigner.dll до/после совпал (`5a7e0f6e219a3ab994a2ab39ec393614caac0a97569dd9f733655e347fc70ffb`); эти проверки не вызывают изменённый bridge. Host/IPC/Broad/process checks повторены для последнего VsHost. Generic coverage семантически сопоставлен с HEAD с нормализацией только newline; исходные bytes восстановлены, regenerated copies сохранены в artifacts. Исходные пользовательские изменения не удалялись. Dump/binary/runtime artifacts в Git не добавляются; все raw logs/captures лежат в `artifacts/universal-phase4f/`.

## 12. Оставшиеся условия и ручная приёмка

1. Финальный installed-VSIX E2E и physical DPI/DnD остаются NOT VERIFIED. Пройденный старой установкой smoke не подменяет эту границу. Полный порядок приёмки и критерии source diff/ACK — в [checklist](Phase4FVisualStudioAcceptance.md).
2. Один активный AXAML document на host; dirty switch блокируется. Отдельные concurrent histories, полный VS document-event lifecycle и восстановление dirty host после аварийного завершения IDE не объявляются завершёнными.
3. UIA native crash UNRESOLVED; новые данные для причинной связи с corrected bridge lifecycle не получены. При следующем AV сохранить full dump/modules/stacks/metadata/SHA-256 прежним forensic pipeline.
4. Large Insert/Reparent/Undo, Inspector P95 и peak ACK-profile memory остаются P2. Нужен отдельный измеряемый этап, а не небезопасная замена source patches регенерацией.
5. Safe subset arbitrary Bindings/Styles/Templates и imported collections не расширялся. Capabilities/opaque warnings сохраняются.

Следующий конкретный шаг — приёмка **текущего SHA-256 VSIX** в экспериментальном VS по checklist: особенно dirty document switching, ручной конфликт, close/restart во время pending ACK и DPI 100/125/150%. Новый release/tag, обновление packages и следующая фаза автоматически не выполняются.

## 13. Итоговая оценка

**VSIX INTEGRATION: READY WITH CONDITIONS.**

* **AUTOMATED TESTS: PASS.** Phase 4F 15/15, актуальный Release-aligned Broad 72/72, IPC 3/3; остальные gates указаны выше. Первые failures сохранены.
* **INSTALLED VSIX: NOT VERIFIED** для интерактивного E2E последней сборки. Установка и hashes последнего payload, native installed-host launch/import/shutdown подтверждены; real VS PropertyGrid/Apply/Save/reopen подтверждены только для другой ранее установленной сборки.
* **PHYSICAL DPI/DND: NOT VERIFIED.** Матрица 100/125/150% не выполнена; native RenderScaling=1 и сохранённый inserted TextBlock её не заменяют.
* **UIA CRASH: UNRESOLVED.** Отсутствие новых AV не устанавливает старый root cause.

Forensic summary: 158 launch-capture results; 0 captured native AV, 0 capture failures, 0 timeouts. Девять nonzero results сохранены: восемь assertion/test-setup failures и один первоначальный managed termination при ошибочном пути; нового full dump в этой фазе не получено. Первоначальный managed failure без dump не объявляется native UIA crash или успешным stress cycle. Raw summary — artifacts/universal-phase4f/capture-summary-final.json, машиночитаемые выбранные результаты — Phase4FEvidenceSummary.json.

Условная готовность относится к проверенной автоматизированной границе. Полная installed-VSIX/DPI приёмка и публичная release readiness не заявляются. Нового подтверждённого повреждения AXAML или продолжающегося P1 в финальной матрице не обнаружено. Нет новых controls/properties, изменений JSON/IPC/TFM/NuGet, accessibility workaround, release/tag, commit/push. Работы Phase 4F завершены; следующая фаза не начата.

