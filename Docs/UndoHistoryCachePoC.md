# Phase 7A.4 — кэш описаний Undo/Redo в document session

Дата: 1 ноября 2026. Основа: [IncrementalLayoutProjectionFeasibility.md](IncrementalLayoutProjectionFeasibility.md). Commit: `dfa8570f9ef472fd73b74a3d385e54f1bc1e59f0`.

## 1. Область изменения и baseline

Это один PoC: memoization Title/Description соседних snapshot pairs. Snapshot storage, сериализация, grouping500ms, Undo transactions, восстановление документа, renderer и native layout projection остаются прежними. Canvas остаётся default. Preview/Export, VSIX/IPC, NuGet и TargetFramework не менялись.

При начале работы уже существовали незакоммиченные результаты Phase7A.3: feasibility report, HistoryCost/UndoFeasibility harness, расширение DropBenchmark/Program и исследовательские scripts. Они сохранены. Before в этом отчёте — **тот же Release binary с cache disabled**, не исторические цифры другого запуска. В `artifacts/undo-history-cache-poc/experiment-environment.json` сохранены commit, hashes приложения/runner и Git status.

## 2. Root cause и точный call path

```text
Drop / Inspector edit / NotifyDesignerStateChanged
  → MainWindowViewModel.RegisterHistorySnapshot
      → Serialize(CreateDocumentFileModel())
      → PushUndoSnapshot / clear redo / SetCurrentSnapshot
      → PersistActiveFormDocumentState (существующая повторная сериализация)
      → RaiseDocumentStateProperties
          → RebuildUndoRedoHistoryItems
              → BuildHistorySnapshots
                  undo.Reverse + current + redo
              → для КАЖДОЙ соседней пары: DescribeHistoryTransition
                  → TryReadHistoryDocument(previous)
                  → TryReadHistoryDocument(current)
                  → сравнение полных DTO, создание Title/Description
```

Core Undo: `TakeUndoSnapshot → PushRedoSnapshot → RestoreFromSnapshot → ApplyDocument → PersistActiveFormDocumentState → RaiseDocumentStateProperties`. Redo симметричен. Другие уведомления состояния также вызывают `RaiseDocumentStateProperties`, даже когда пары истории не менялись.

Дорогая повторная работа — не snapshot storage, а полный разбор **двух JSON на каждую старую пару** при каждом refresh. История H30 повторно разбирает 60 DTO. Описание зависит от предыдущего и текущего состояния; кэш по одному current snapshot или индексу был бы неверен при branching.

## 3. Реализация кэша и identity

`DesignerDocumentSession.HistoryDescriptions` владеет одним `UndoHistoryDescriptionCache`. Отдельный runtime SessionId нужен только для диагностики; DocumentId берётся из существующей session.

Ключ — пара immutable string references. Сравнение использует `ReferenceEquals` для обоих снимков, hash — `RuntimeHelpers.GetHashCode`. JSON не хешируется и не сравнивается целиком при lookup. Совпадение hash обрабатывается comparer; оно не означает совпадения ключей. Новая строка с тем же содержимым даёт miss: это безопасный консервативный выбор, а не изменение equality пользовательской истории.

Контекст captions: версия formatter `v1`, имена CurrentCulture/CurrentUICulture. При смене контекста записи очищаются. Текущий formatter использует русский текст и culture-sensitive числовое форматирование; локализация самих подписей этим PoC не добавлялась.

Значение содержит только `(string Title, string Description)`. DTO, native controls, Window, ViewModel, factory delegate, ItemsSource и event subscriptions не сохраняются. На miss вызывается существующий `DescribeHistoryTransition`; исключение не кэшируется и не маскируется. JSON malformed fallback остаётся в исходном formatter.

`RebuildUndoRedoHistoryItems` продолжает создавать UI rows, Index/PositionText/StateText и флаги Current/Past/Future. Эти динамические данные **не кэшируются**. Список и порядок истории остаются прежними.

## 4. Invalidation и границы жизни

| Событие | Действие derived cache |
|---|---|
| Новый snapshot / coalescing | Новая string reference → новый pair; старые недостижимые пары удаляются |
| Undo / Redo | На завершённом SetCurrentSnapshot prune по текущей полной последовательности; captions ещё достижимых пар можно переиспользовать |
| Undo → новое действие | После clear redo и SetCurrentSnapshot удаляются пары старой ветки; новый predecessor учитывается в ключе |
| Любой history refresh | Defensive prune по reachable adjacent pairs, затем hit/miss |
| ResetHistory / SetHistoryState | Полная очистка derived cache перед заменой истории |
| New / Open / Close / Delete Form | Существующий lifecycle заменяет/Dispose session; её cache очищается |
| Form switch | Собственная session каждой формы; существующий LoadActiveFormState вызывает SetHistoryState, captions консервативно строятся заново |
| Restore документа | Prune на завершённой смене current; UI current row пересчитывается |
| Изменение caption context | Clear, новый context, дальнейшее заполнение на miss |
| Обычный UpdateDocument / Persist | Не вызывает полного Clear; prune удаляет только устаревшие пары |

Во время промежуточного pop/push Undo prune не выполняется: иначе можно было бы удалить ещё нужные пары до завершения транзакции. Существующие публичные Stack<string> не заменены новым типом; прямые stack mutations защищены последующим SetCurrentSnapshot/refresh. Это не новый event-based history engine.

Каждая session имеет отдельный dictionary. Индекс или DocumentId не является общим static key. Повторное открытие того же ID создаёт новый объект cache. Сериализованные Undo snapshots, если они присутствуют в открываемом workspace, восстанавливаются как раньше; чужой runtime cache не переносится.

## 5. Memory safety

Количество entries не превышает число достижимых соседних пар: `max(0, HistoryCount − 1)`. Отдельной накопительной истории cache нет. Текущая snapshot storage не имеет введённого этим PoC нового лимита; cache следует её фактической длине, а не заводит независимый unlimited журнал. Dictionary capacity может сохранять пик вместимости, но удалённые key/value references очищаются. Reset/Dispose освобождает все entries; при сборке session освобождается и сам dictionary.

Не используются static dictionary или WeakReference в production. Factory не сохраняется. Тесты WeakReference отдельно проверяют orphan snapshot string, disposed session и объект, захваченный временным formatter closure. GC.Collect используется исключительно в regression tests, не в приложении и не в performance measurements.

Длинный пользовательский Text может входить в Description; такие strings тоже занимают память. Поэтому cache не объявляется бесплатным: он меняет повторные DTO allocations на O(H) captions и pair references, bounded текущей историей. Полные DTO не удерживаются.

## 6. Диагностика и rollback

По умолчанию cache включён. Для диагностического возврата к исходному вычислению:

```powershell
$env:FORMDESIGNER_UNDO_HISTORY_CACHE='0' # задать до создания sessions / перезапустить Designer
```

`FORMDESIGNER_UNDO_HISTORY_CACHE=1` включает cache. Флаг не является пользовательской настройкой и не записывается в проект. Он читается при создании session; уже созданные sessions не переключаются посреди операции.

`FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS=1` включает Trace-события:

- `UNDO_HISTORY_CACHE_HIT` / `UNDO_HISTORY_CACHE_MISS`;
- `UNDO_HISTORY_CACHE_INVALIDATED` / `UNDO_HISTORY_CACHE_CLEARED`;
- `UNDO_HISTORY_CACHE_STATS` после refresh.

Поля: SessionId, DocumentId, HistoryCount, CacheCount, HitCount, MissCount, Reason. Счётчики накопительные в пределах session, в benchmark записываются дельты. Подробные Trace-события выключены при обычной работе; formatter JSON и caption contents не логируются. Подключённый TraceListener/debugger позволяет собрать события. Тест проверяет все пять событий и отсутствие snapshot/caption payload.

HistoryCount обозначает число состояний в полной последовательности, включая current, а H в benchmark — undo depth. Поэтому H30 обычно соответствует31 состоянию до действия и32 после нового шага.

Rollback не требует миграции, изменения JSON, удаления истории или обратных patches: отключить cache до запуска sessions. Для удаления PoC из кода достаточно убрать derived cache, точки Prepare/GetOrCreate/ReportStatistics в VM и lifecycle hooks в session. Исходный formatter остаётся на месте. Никаких destructive Git операций не требуется.

## 7. Методология before/after

Release, .NET8, без debugger, один последовательный процесс за раз. Нет параллельного restore/build или тяжёлых regression suites. Каждая arm запускается в отдельном процессе, порядок on/off чередуется между fixtures. Один initial observation + три warm-ups отдельно от 30 steady samples. Median — nearest-rank 15-й sample из30, P95 —29-й; min/max, GC0/1/2 и allocations сохраняются в raw JSON. Нет CI threshold по миллисекундам.

VM family: N20/50/100/300/500 × H0/10/30 × cache off/on; Add, Move, Reparent, Resize, Property, core Undo, core Redo, HistoryRefresh. История создаётся из canonical DTO текущей версии и immutable snapshots. Проверяется атомарность одного шага и точное восстановление строки snapshot. Это VM CPU/managed allocation workload, **не видимая latency UI**. Undo/Redo замерены напрямую через production UndoCommand/RedoCommand, без 160+100ms ожиданий старого test helper.

UI family: существующий Phase7A.2 DropBenchmark, cache off/on, без отключения Inspector/layout/diagnostics. Toolbox Canvas/Grid/Dock, Move, Reparent. Измеряются handler, actual visual readiness, attached layout, compositor commit и следующий Input callback. Viewport формы1200×800 DIP, zoom1, RenderScaling1; внешние декорации окна не используются как размер формы. Реальные Avalonia routed DragOver/Drop events; это не physical mouse/OLE drag.

Toolbox проходит routed DragOver/Drop; Move/Reparent вызывают существующий CommitGridControlDrop после разрешения target по реальной geometry, как исходный harness. Полная pointer routing проверяется отдельными suites. UI reset использует core Undo без фиксированного260ms ожидания старого helper. Таким образом, это одинаковый rapid-cycle workload для off/on, но его абсолютные frame latency нельзя напрямую сравнивать со старой серией, где между действиями был дополнительный settle interval.

История подготовлена одинаковыми30 реальными VM edits до показа окна; H0/H10 оставляют нужную часть истории, H30 сохраняет все30 шагов. Во всех arms одинаковое конечное authored состояние. После каждой операции выполняется настоящий Undo вне forward clock. Следующая операция создаёт новую ветку; в steady fixtures H означает undo depth, после reset может оставаться один redo snapshot. Это одинаково для off/on и соответствует прежней paired методологии. Pure HistoryRefresh измеряется до мутаций с H реальными парами.

UI allocations охватывают scope до NextInputReady; FirstCommit latency заканчивается раньше, на compositor fence. Это не physical scanout и не гарантия, что пользователь уже увидел пиксели. Stage times вложены, их нельзя складывать как независимые затраты. Время hover/подготовки target находится вне Drop clock, как в предыдущем исследовании.

Третья family — только CaptionPairs: Prepare/prune + GetOrCreate/DescribeHistoryTransition по H парам, без создания History UI rows. Delegate исходного formatter создаётся вне timed interval; reflection внутри clock отсутствует. Первый cold fill отделён от steady lookups. Нет изменения production-кода ради этого измерения.

## 8. Результаты

Завершена полная matrix: **12600 steady measurements** —7200 VM (240 arms),4500 UI (150 arms),900 CaptionPairs (30 arms). В каждой arm30 samples; initial/warm-ups не входят в quantiles. Validator проверил все ожидаемые operation/N/H/on-off combinations, один Undo entry на каждое UI действие, отсутствие entries при disabled и размер cache≤reachable pairs. Ни одна arm не отброшена.

Полные raw JSON, CSV со всеми median/P95/min/max/allocations/GC/hits/misses и сводные таблицы: `artifacts/undo-history-cache-poc`. Исследовательские JSON/log/summary занимают около35MiB до generated-project regression artifacts. Первоначальные двухsample pilots находятся отдельно и не участвуют в quantiles. App SHA256 во всех families и после full solution build: `7062B533344A834E24AC221DA8C4DD4811878FD4C59B56DCD5614E320A6366CC`. Runner для CaptionPairs перекомпилирован после VM/UI серии только ради нового harness entry; отдельные hashes сохранены в experiment-environment.json и caption-environment.json. Before/after каждой family используют один и тот же binary, production DLL общая для всех families.

### Cold cache и exclusive captions

CaptionPairs100/H30, steady: **57.049 / 91.841 → 0.0158 / 0.0291ms**; allocated **18613736 → 2128bytes**; GC0/1/2 за30 samples **33/0/0 → 0/0/0**; on900 hits/0 misses. Это cache lookup + reachable-pair pruning без History UI rows.

Первый cold fill той же fixture: off240.70ms/18613736bytes, on215.82ms/18616760bytes. Это **один initial observation на arm, не cold median/P95**. Оба режима всё ещё строят все30 descriptions; этот одиночный timing не доказывает cold acceleration. Первый Grid Drop100/H30 после setup —368.26/366.14ms off/on, отдельно от steady190.89/136.25ms. App startup полностью не бенчмаркировался; FirstDrop — первая операция после fixture/window setup, а не запуск приложения с нуля.

### Проверенный end-to-end результат на100 controls, H30

| Операция | Off P50/P95, ms | On P50/P95, ms | Off/On allocated, MiB |
|---|---:|---:|---:|
| Toolbox → Canvas | 151.82 / 173.48 | 85.57 / 106.60 | 35.19 / 18.01 |
| Toolbox → Grid | 190.89 / 222.23 | 136.25 / 159.67 | 46.86 / 30.25 |
| Toolbox → DockPanel | 183.83 / 218.33 | 133.42 / 296.81 | 44.66 / 27.93 |
| Move | 163.98 / 194.33 | 78.99 / 139.85 | 28.55 / 11.13 |
| Reparent | 193.19 / 236.78 | 127.52 / 267.14 | 41.98 / 24.62 |

Это выигрыш в **реальной UI-операции до compositor commit**, не только в formatter. Но P95 Dock/Reparent в этой серии ухудшился; нельзя писать, что все frame tails устранены. На H0 нет существенного бюджета старых captions для экономии: allocations почти одинаковы, latency местами выше или ниже из-за полного остального UI workload. Все samples, включая неудобные tails, сохранены; ни outlier filtering, ни time-based pass threshold не применялись.

Проверка механизма на Grid100/H30: CreateUndoSnapshot, включающий captions, P50 **80.29 → 7.96ms**; BuildNativeProjection **0.920 → 0.919ms**; RefreshPropertyInspector **9.51 → 9.17ms**. За30 forward actions formatter вызван930 раз при off и60 при on; on получил870 hits. В этом UI path меняются две граничные пары, поэтому miss count больше одной пары на действие. Консервативная reference identity сохраняет корректность при повторной сериализации current.

Чистый VM HistoryRefresh100/H30: **57.42 / 114.61 → 0.05 / 0.07ms**, allocated **17.76MiB → 8392bytes**,900 hits и0 misses за30 повторов. Новый VM Add100/H30: **62.05 / 66.88 → 8.26 / 8.70ms**. VM Undo100/H30: **62.14 / 65.14 → 6.45 / 8.67ms**. Это CPU/model family, а не полная UI latency Undo. Reset-Undo из Drop fixture включает ручное восстановление selection для следующего действия и не выдаётся за отдельный пользовательский Undo benchmark.

## 9. Regression strategy и проверяемые свойства

Новая suite `--undo-history-cache-checks` содержит25 проверок:

- hit неизменённой пары, miss новой reference и изменённого predecessor;
- смена caption context, отсутствие записи после исключения formatter;
- Add/Move/Reparent/Resize/Property/DataGrid/Logic через настоящие Undo/Redo commands с точным восстановлением snapshot;
- baseline-equivalent Title/Description, текущая строка и размер reachable cache;
- новая ветка после Undo, coalescing текущего шага, немедленный Reset;
- New/Open/Close lifecycle, переключение и удаление формы;
- selection/Grid placement/Inspector в реальном PointerFixture Window, JSON round-trip;
-20 повторов Undo/Redo, rich DataGrid + BindingSource fields/custom properties/Logic;
- WeakReference на orphan snapshot, disposed session и formatter closure owner;
- все диагностические события без snapshot payload, disabled path с исходными captions.

Сравнение с baseline formatter выполняется для каждой соседней пары; dynamic current/past/future не берутся из cache. Fixture предварительно использует canonical DTO текущего serializer: исходный raw seed2.3 при restore штатно нормализуется в2.4 и русские Window settings, поэтому нельзя ошибочно требовать byte equality именно с неканонизированным legacy seed. Ожидаемые результаты продукта ради зелёного статуса не менялись.

Общие suites проверяют Layout Phase2–7A, routed pointer/capture/marquee, nested reparent, Undo/Redo, Multi Form, Save/Open race fixes, Property Inspector, JSON, DataGrid/Eremex, Preview memory, AXAML round-trip и VSIX/VsHost/IPC. Результаты приведены в разделе13. Общий прогон не полностью зелёный; красные результаты не скрыты повторными запусками.

## 10. Изменённые файлы этого PoC

| Файл | Изменение |
|---|---|
| `DesignerSystem/UndoHistoryDescriptionCache.cs` | Session-owned pair cache, prune, diagnostics, A/B switch |
| `DesignerSystem/DesignerDocumentSession.cs` | Владение cache, Reset/SetHistoryState/SetCurrentSnapshot/Dispose boundaries |
| `ViewModels/MainWindowViewModel.cs` | Cached captions в существующем RebuildUndoRedoHistoryItems |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` | Два CLI entry points новой suite/benchmark; прежние A3 additions сохранены |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.DropBenchmark.cs` | H10, cache counters, opt-in direct reset Undo; сохранены предыдущие research additions |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.UndoHistoryCache.cs` |25 regression checks |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.UndoHistoryCacheBenchmark.cs` | CPU/Undo/history UI и exclusive caption-pair families |
| `smoke-tests/LayoutGrid/BenchmarkUndoHistoryCache.ps1` | Последовательные paired processes, одинаковые fixtures, resume completed cases |
| `smoke-tests/LayoutGrid/SummarizeUndoHistoryCache.py` | Полнота матрицы, atomicity/cache bounds, quantiles/allocations/GC, CSV/JSON/tables |
| `smoke-tests/LayoutGrid/CheckUndoHistoryCacheRegression.ps1` | Общие targeted/broad suites с cache enabled |
| `Docs/UndoHistoryCachePoC.md` | Этот отчёт |

Snapshot DTO/JSON schema, exports, Preview lifecycle, package manifests и Layout/VSIX architecture не редактировались. Отдельные изменения A3, уже присутствовавшие в Git, не являются частью production PoC и не удалялись.

## 11. Ограничения и ручной checklist

- Cold cache по-прежнему должен построить H captions. Переключение формы через SetHistoryState консервативно очищает cache; ускорение такого cold switch не заявляется.
- История UI по-прежнему создаёт O(H) rows. Snapshot capture/persist/restore и полная native projection остались прежними. На500 controls они заметны даже при H0.
- UI P95 не гарантированно улучшается в каждой паре. GC/JIT/scheduling/frame callbacks сохранены; этот PoC не устанавливает причинность каждого tail.
- Performance fixtures — обычные Buttons и layout hosts. Они не измеряют тяжёлые DataGrid/Eremex/plugin формы; для этих компонентов проводятся функциональные regressions.
- Physical mouse/OLE DnD, DPI/несколько мониторов, внешние UI Automation clients и интерактивная Visual Studio остаются отдельными ручными проверками. Исторический UIA native crash этим managed-caption PoC не объявляется исправленным.
- Live PostgreSQL не требуется для cache correctness и не подменяется mock integration. Повторная live database validation должна быть явно отделена от offline suites.

Ручной checklist: открыть Canvas/Grid формы с H30; выполнить Add/Move/Reparent/Resize/Text; просмотреть подписи и current row после Undo/Redo; сделать Undo → новое действие; переключить/удалить форму; Save/Open workspace; закрыть проект; повторить с cache disabled после перезапуска. Проверить обычный и AXAML Preview с DataGrid и повторное закрытие окон. Не менять авторские layout constraints и не конвертировать Canvas автоматически.

Воспроизведение измерений (build отдельно от performance):

```powershell
dotnet build smoke-tests/FormDesigner.ExportSmokeTests -c Release -m:1
& smoke-tests/LayoutGrid/BenchmarkUndoHistoryCache.ps1 -Family Vm
& smoke-tests/LayoutGrid/BenchmarkUndoHistoryCache.ps1 -Family Ui
& smoke-tests/LayoutGrid/BenchmarkUndoHistoryCache.ps1 -Family Captions
$env:PYTHONIOENCODING='utf-8'
py -3 smoke-tests/LayoutGrid/SummarizeUndoHistoryCache.py artifacts/undo-history-cache-poc --complete
& smoke-tests/LayoutGrid/CheckUndoHistoryCacheRegression.ps1
```

Scripts пропускают уже завершённые JSON. Для независимого повторного эксперимента следует выбрать новый output directory/переместить предыдущую matrix; нельзя смешивать старые и новые binaries в одной matrix. До нового запуска сохранить hashes и Build/viewport metadata.

## 12. Полная performance matrix

P50/P95 и MiB округлены только для таблиц. GC — суммы по30 timed intervals, hits/misses — их дельты; точные bytes/min/max доступны в CSV. Нулевое округлённое MiB не означает отсутствие служебных allocations. VM/CaptionPairs/UI families нельзя смешивать между собой.

### Caption pairs — без UI rows

| Operation | N | H | Off P50/P95 ms | On P50/P95 ms | Off/On MiB | Off GC0/1/2 | On GC0/1/2 | On hit/miss |
|---|---:|---:|---:|---:|---:|---|---|---|
| CaptionPairs | 20 | 0 | 0.00/0.00 | 0.00/0.00 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| CaptionPairs | 20 | 10 | 14.42/17.24 | 0.01/0.02 | 1.209/0.001 | 2/1/0 | 0/0/0 | 300/0 |
| CaptionPairs | 20 | 30 | 31.32/50.30 | 0.02/0.03 | 3.629/0.002 | 7/1/0 | 0/0/0 | 900/0 |
| CaptionPairs | 50 | 0 | 0.00/0.00 | 0.00/0.00 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| CaptionPairs | 50 | 10 | 31.24/40.59 | 0.01/0.02 | 2.976/0.001 | 6/1/0 | 0/0/0 | 300/0 |
| CaptionPairs | 50 | 30 | 28.66/105.36 | 0.01/0.03 | 8.929/0.002 | 17/0/0 | 0/0/0 | 900/0 |
| CaptionPairs | 100 | 0 | 0.00/0.00 | 0.00/0.00 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| CaptionPairs | 100 | 10 | 20.58/53.20 | 0.02/0.03 | 5.917/0.001 | 11/0/0 | 0/0/0 | 300/0 |
| CaptionPairs | 100 | 30 | 57.05/91.84 | 0.02/0.03 | 17.751/0.002 | 33/0/0 | 0/0/0 | 900/0 |
| CaptionPairs | 300 | 0 | 0.00/0.00 | 0.00/0.00 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| CaptionPairs | 300 | 10 | 60.96/107.11 | 0.01/0.02 | 32.812/0.001 | 46/19/12 | 0/0/0 | 300/0 |
| CaptionPairs | 300 | 30 | 182.86/190.67 | 0.02/0.02 | 98.438/0.002 | 130/72/30 | 0/0/0 | 900/0 |
| CaptionPairs | 500 | 0 | 0.00/0.00 | 0.00/0.00 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| CaptionPairs | 500 | 10 | 112.12/186.35 | 0.01/0.01 | 54.517/0.001 | 78/53/22 | 0/0/0 | 300/0 |
| CaptionPairs | 500 | 30 | 305.62/318.97 | 0.02/0.04 | 163.547/0.002 | 182/113/16 | 0/0/0 | 900/0 |

### VM / CPU — команды без Window

| Operation | N | H | Off P50/P95 ms | On P50/P95 ms | Off/On MiB | Off GC0/1/2 | On GC0/1/2 | On hit/miss |
|---|---:|---:|---:|---:|---:|---|---|---|
| Add | 20 | 0 | 2.91/5.53 | 2.93/5.39 | 0.576/0.577 | 2/2/2 | 2/2/2 | 0/30 |
| Add | 20 | 10 | 12.29/13.85 | 2.93/3.95 | 1.788/0.582 | 9/5/3 | 3/3/3 | 300/30 |
| Add | 20 | 30 | 13.49/13.90 | 2.82/3.24 | 4.210/0.590 | 0/0/0 | 0/0/0 | 900/30 |
| Add | 50 | 0 | 6.24/7.17 | 6.17/7.70 | 1.113/1.113 | 5/4/3 | 5/4/3 | 0/30 |
| Add | 50 | 10 | 13.05/15.25 | 6.16/7.20 | 4.091/1.119 | 8/2/2 | 5/4/3 | 300/30 |
| Add | 50 | 30 | 31.93/34.35 | 6.11/8.07 | 10.046/1.126 | 20/2/1 | 2/2/1 | 900/30 |
| Add | 100 | 0 | 11.44/13.97 | 11.12/12.30 | 2.010/2.011 | 7/6/5 | 2/1/0 | 0/30 |
| Add | 100 | 10 | 25.26/27.79 | 10.97/12.72 | 7.928/2.015 | 12/1/0 | 1/1/1 | 300/30 |
| Add | 100 | 30 | 62.05/66.88 | 8.26/8.70 | 19.766/2.024 | 37/1/1 | 2/1/0 | 900/30 |
| Add | 300 | 0 | 29.65/33.60 | 29.78/35.06 | 7.100/7.100 | 13/11/7 | 12/11/7 | 0/30 |
| Add | 300 | 10 | 72.57/77.80 | 28.49/35.58 | 39.913/7.106 | 50/28/15 | 9/6/5 | 300/30 |
| Add | 300 | 30 | 196.20/204.61 | 13.57/19.10 | 105.545/7.113 | 134/71/31 | 8/7/3 | 900/30 |
| Add | 500 | 0 | 26.09/56.18 | 22.58/51.38 | 11.685/11.685 | 16/16/8 | 15/15/8 | 0/30 |
| Add | 500 | 10 | 124.19/128.02 | 28.94/36.95 | 66.205/11.691 | 95/61/29 | 14/13/6 | 300/30 |
| Add | 500 | 30 | 331.05/367.01 | 17.56/20.85 | 175.242/11.698 | 224/141/51 | 9/8/0 | 900/30 |
| HistoryRefresh | 20 | 0 | 0.03/0.05 | 0.02/0.03 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| HistoryRefresh | 20 | 10 | 16.94/18.63 | 0.04/0.07 | 1.212/0.003 | 2/1/0 | 0/0/0 | 300/0 |
| HistoryRefresh | 20 | 30 | 31.88/50.02 | 0.05/0.09 | 3.634/0.008 | 7/0/0 | 0/0/0 | 900/0 |
| HistoryRefresh | 50 | 0 | 0.03/0.04 | 0.03/0.04 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| HistoryRefresh | 50 | 10 | 27.19/41.76 | 0.04/0.07 | 2.978/0.003 | 7/2/1 | 0/0/0 | 300/0 |
| HistoryRefresh | 50 | 30 | 29.31/125.76 | 0.06/0.16 | 8.934/0.008 | 18/2/1 | 0/0/0 | 900/0 |
| HistoryRefresh | 100 | 0 | 0.04/0.06 | 0.03/0.05 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| HistoryRefresh | 100 | 10 | 20.49/69.90 | 0.04/0.14 | 5.919/0.003 | 13/2/1 | 0/0/0 | 300/0 |
| HistoryRefresh | 100 | 30 | 57.42/114.61 | 0.05/0.07 | 17.757/0.008 | 34/0/0 | 0/0/0 | 900/0 |
| HistoryRefresh | 300 | 0 | 0.03/0.06 | 0.02/0.04 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| HistoryRefresh | 300 | 10 | 61.97/99.58 | 0.04/0.07 | 32.814/0.003 | 43/10/10 | 0/0/0 | 300/0 |
| HistoryRefresh | 300 | 30 | 187.04/192.42 | 0.05/0.10 | 98.443/0.008 | 131/57/31 | 0/0/0 | 900/0 |
| HistoryRefresh | 500 | 0 | 0.03/0.04 | 0.02/0.03 | 0.000/0.000 | 0/0/0 | 0/0/0 | 0/0 |
| HistoryRefresh | 500 | 10 | 103.22/113.36 | 0.05/0.07 | 54.519/0.003 | 78/56/23 | 0/0/0 | 300/0 |
| HistoryRefresh | 500 | 30 | 302.50/324.36 | 0.05/0.07 | 163.553/0.008 | 180/110/14 | 0/0/0 | 900/0 |
| Move | 20 | 0 | 2.98/4.92 | 2.94/5.11 | 0.564/0.565 | 4/4/4 | 4/4/4 | 0/30 |
| Move | 20 | 10 | 9.94/12.38 | 2.88/3.85 | 1.776/0.570 | 8/5/4 | 5/4/4 | 300/30 |
| Move | 20 | 30 | 13.57/15.49 | 2.73/3.52 | 4.199/0.578 | 6/5/4 | 3/3/3 | 900/30 |
| Move | 50 | 0 | 6.65/7.45 | 6.82/7.50 | 1.380/1.380 | 11/11/11 | 11/11/11 | 0/30 |
| Move | 50 | 10 | 13.76/16.43 | 6.43/7.35 | 4.358/1.386 | 12/6/6 | 8/8/8 | 300/30 |
| Move | 50 | 30 | 31.52/32.56 | 6.54/7.25 | 10.314/1.393 | 20/4/2 | 2/2/2 | 900/30 |
| Move | 100 | 0 | 12.60/13.64 | 12.39/13.46 | 2.739/2.739 | 6/4/3 | 13/10/9 | 0/30 |
| Move | 100 | 10 | 23.75/24.83 | 11.03/12.71 | 8.658/2.745 | 16/7/4 | 2/1/1 | 300/30 |
| Move | 100 | 30 | 62.37/65.98 | 8.48/11.08 | 20.497/2.753 | 38/20/3 | 3/2/2 | 900/30 |
| Move | 300 | 0 | 14.50/16.32 | 14.75/17.22 | 9.695/9.695 | 16/16/11 | 15/15/10 | 0/30 |
| Move | 300 | 10 | 76.87/87.15 | 14.31/21.16 | 42.510/9.701 | 64/50/26 | 13/11/9 | 300/30 |
| Move | 300 | 30 | 198.25/204.29 | 13.04/15.27 | 108.140/9.708 | 142/80/40 | 7/5/3 | 900/30 |
| Move | 500 | 0 | 24.18/33.47 | 23.35/26.42 | 16.131/16.132 | 15/15/11 | 17/17/13 | 0/30 |
| Move | 500 | 10 | 128.92/138.81 | 22.95/26.10 | 70.651/16.137 | 95/59/33 | 14/14/10 | 300/30 |
| Move | 500 | 30 | 326.89/340.30 | 22.04/24.67 | 179.689/16.145 | 211/114/38 | 10/9/1 | 900/30 |
| Property | 20 | 0 | 2.04/4.93 | 2.12/5.18 | 0.356/0.357 | 5/5/5 | 5/5/5 | 0/30 |
| Property | 20 | 10 | 4.82/5.55 | 2.06/2.64 | 1.568/0.362 | 5/3/1 | 2/1/1 | 300/30 |
| Property | 20 | 30 | 12.48/12.79 | 2.09/2.60 | 3.991/0.370 | 0/0/0 | 1/1/1 | 900/30 |
| Property | 50 | 0 | 1.91/2.56 | 1.99/4.58 | 0.868/0.869 | 4/4/4 | 4/4/4 | 0/30 |
| Property | 50 | 10 | 11.29/13.41 | 1.84/2.86 | 3.846/0.874 | 8/4/2 | 6/3/3 | 300/30 |
| Property | 50 | 30 | 30.78/31.40 | 1.92/2.49 | 9.803/0.882 | 19/5/2 | 0/0/0 | 900/30 |
| Property | 100 | 0 | 3.21/3.82 | 3.26/4.35 | 1.721/1.721 | 2/2/2 | 5/5/5 | 0/30 |
| Property | 100 | 10 | 22.62/23.55 | 3.34/4.53 | 7.640/1.727 | 16/5/2 | 7/2/1 | 300/30 |
| Property | 100 | 30 | 61.17/62.52 | 3.34/3.68 | 19.478/1.734 | 38/11/2 | 1/1/1 | 900/30 |
| Property | 300 | 0 | 9.61/13.18 | 10.13/11.73 | 6.647/6.648 | 9/8/7 | 12/11/10 | 0/30 |
| Property | 300 | 10 | 71.14/74.35 | 9.77/11.00 | 39.464/6.653 | 64/48/26 | 6/5/2 | 300/30 |
| Property | 300 | 30 | 194.49/199.64 | 9.98/14.58 | 105.096/6.661 | 133/72/28 | 5/4/0 | 900/30 |
| Property | 500 | 0 | 17.16/20.94 | 17.07/19.93 | 11.054/11.055 | 16/13/11 | 15/14/9 | 0/30 |
| Property | 500 | 10 | 120.37/124.26 | 16.85/21.58 | 65.573/11.060 | 86/55/25 | 14/13/6 | 300/30 |
| Property | 500 | 30 | 321.78/337.90 | 15.65/19.07 | 174.608/11.068 | 196/101/24 | 6/6/0 | 900/30 |
| Redo | 20 | 0 | 2.91/3.40 | 2.96/3.29 | 0.501/0.501 | 0/0/0 | 0/0/0 | 0/30 |
| Redo | 20 | 10 | 5.18/5.85 | 2.87/3.17 | 1.712/0.507 | 4/1/1 | 1/1/1 | 300/30 |
| Redo | 20 | 30 | 13.15/14.03 | 2.96/4.31 | 4.135/0.514 | 23/5/2 | 3/3/2 | 900/30 |
| Redo | 50 | 0 | 2.67/4.14 | 2.67/6.13 | 1.099/1.100 | 1/0/0 | 0/0/0 | 0/30 |
| Redo | 50 | 10 | 11.69/12.60 | 2.53/3.22 | 4.077/1.105 | 8/1/1 | 5/3/1 | 300/30 |
| Redo | 50 | 30 | 31.21/31.84 | 2.62/3.62 | 10.037/1.113 | 18/3/0 | 0/0/0 | 900/30 |
| Redo | 100 | 0 | 4.24/5.48 | 4.17/5.34 | 2.103/2.104 | 8/4/3 | 5/2/1 | 0/30 |
| Redo | 100 | 10 | 23.74/24.46 | 4.18/5.39 | 8.023/2.109 | 17/7/2 | 7/2/1 | 300/30 |
| Redo | 100 | 30 | 62.30/63.35 | 4.32/4.98 | 19.866/2.117 | 37/3/0 | 2/2/1 | 900/30 |
| Redo | 300 | 0 | 12.49/13.59 | 13.09/15.98 | 8.370/8.371 | 15/11/6 | 18/14/9 | 0/30 |
| Redo | 300 | 10 | 74.59/77.35 | 12.62/16.20 | 41.185/8.376 | 65/48/23 | 10/7/1 | 300/30 |
| Redo | 300 | 30 | 198.82/206.93 | 13.18/19.17 | 106.817/8.384 | 136/74/29 | 10/8/1 | 900/30 |
| Redo | 500 | 0 | 22.07/24.41 | 21.58/26.49 | 13.863/13.864 | 23/20/9 | 21/18/7 | 0/30 |
| Redo | 500 | 10 | 127.29/133.19 | 22.03/26.56 | 68.447/13.869 | 95/55/24 | 21/18/5 | 300/30 |
| Redo | 500 | 30 | 328.82/343.68 | 20.57/24.12 | 177.416/13.877 | 206/95/26 | 14/11/2 | 900/30 |
| Reparent | 20 | 0 | 2.15/4.96 | 2.14/4.92 | 0.374/0.374 | 3/3/3 | 3/3/3 | 0/30 |
| Reparent | 20 | 10 | 5.98/6.78 | 2.15/2.83 | 1.586/0.380 | 4/2/1 | 2/2/1 | 300/30 |
| Reparent | 20 | 30 | 12.76/13.47 | 2.45/3.05 | 4.009/0.388 | 12/3/2 | 1/1/1 | 900/30 |
| Reparent | 50 | 0 | 4.79/5.45 | 4.81/5.65 | 0.905/0.905 | 5/5/4 | 4/4/4 | 0/30 |
| Reparent | 50 | 10 | 11.61/13.25 | 4.63/5.49 | 3.883/0.911 | 12/6/5 | 4/4/3 | 300/30 |
| Reparent | 50 | 30 | 31.04/35.12 | 4.75/5.12 | 9.840/0.918 | 20/7/2 | 1/1/1 | 900/30 |
| Reparent | 100 | 0 | 4.51/9.53 | 8.19/9.91 | 1.791/1.792 | 6/3/2 | 6/3/1 | 0/30 |
| Reparent | 100 | 10 | 22.62/24.31 | 3.45/4.28 | 7.711/1.797 | 12/4/1 | 3/1/1 | 300/30 |
| Reparent | 100 | 30 | 61.24/63.16 | 3.81/5.78 | 19.549/1.805 | 36/12/1 | 4/2/1 | 900/30 |
| Reparent | 300 | 0 | 10.13/12.61 | 10.48/12.59 | 6.844/6.844 | 13/11/7 | 22/20/16 | 0/30 |
| Reparent | 300 | 10 | 73.42/80.13 | 9.96/10.91 | 39.660/6.850 | 68/49/29 | 8/6/4 | 300/30 |
| Reparent | 300 | 30 | 196.34/212.43 | 9.85/11.94 | 105.288/6.857 | 137/78/33 | 9/8/4 | 900/30 |
| Reparent | 500 | 0 | 17.70/21.75 | 17.73/20.72 | 11.384/11.385 | 19/19/13 | 24/23/18 | 0/30 |
| Reparent | 500 | 10 | 120.97/127.09 | 16.97/20.00 | 65.903/11.390 | 87/62/25 | 12/8/6 | 300/30 |
| Reparent | 500 | 30 | 327.47/379.65 | 16.01/18.56 | 174.941/11.398 | 207/110/33 | 7/4/0 | 900/30 |
| Resize | 20 | 0 | 2.10/4.38 | 2.14/4.82 | 0.356/0.357 | 3/3/3 | 3/3/3 | 0/30 |
| Resize | 20 | 10 | 5.39/6.00 | 2.09/2.50 | 1.568/0.362 | 4/2/2 | 2/2/1 | 300/30 |
| Resize | 20 | 30 | 12.56/13.39 | 2.18/2.91 | 3.991/0.370 | 11/4/3 | 2/2/2 | 900/30 |
| Resize | 50 | 0 | 3.66/4.76 | 4.62/5.15 | 0.868/0.868 | 2/2/2 | 3/3/3 | 0/30 |
| Resize | 50 | 10 | 11.45/12.20 | 2.43/4.81 | 3.846/0.874 | 10/5/4 | 6/6/5 | 300/30 |
| Resize | 50 | 30 | 30.94/32.32 | 3.36/4.88 | 9.803/0.882 | 19/2/1 | 2/1/1 | 900/30 |
| Resize | 100 | 0 | 3.29/3.70 | 3.42/4.57 | 1.721/1.721 | 0/0/0 | 11/10/10 | 0/30 |
| Resize | 100 | 10 | 22.56/26.37 | 3.31/4.38 | 7.640/1.726 | 12/4/1 | 3/3/1 | 300/30 |
| Resize | 100 | 30 | 60.93/63.41 | 3.39/4.00 | 19.478/1.734 | 37/14/1 | 2/1/1 | 900/30 |
| Resize | 300 | 0 | 10.20/13.04 | 10.10/11.70 | 6.647/6.647 | 19/19/16 | 18/18/15 | 0/30 |
| Resize | 300 | 10 | 71.52/73.91 | 9.63/13.50 | 39.461/6.653 | 63/44/27 | 11/10/6 | 300/30 |
| Resize | 300 | 30 | 193.49/204.70 | 9.57/11.49 | 105.092/6.661 | 128/74/25 | 6/5/1 | 900/30 |
| Resize | 500 | 0 | 17.28/20.78 | 17.52/21.26 | 11.054/11.056 | 22/22/20 | 23/23/20 | 0/30 |
| Resize | 500 | 10 | 122.15/131.32 | 17.16/20.49 | 65.573/11.060 | 88/56/26 | 15/12/8 | 300/30 |
| Resize | 500 | 30 | 323.98/350.63 | 15.89/19.65 | 174.609/11.068 | 204/99/31 | 9/8/3 | 900/30 |
| Undo | 20 | 0 | 2.99/3.36 | 3.05/3.49 | 0.501/0.501 | 0/0/0 | 0/0/0 | 0/30 |
| Undo | 20 | 10 | 5.35/6.08 | 4.16/4.92 | 1.712/0.628 | 4/2/2 | 4/3/2 | 270/60 |
| Undo | 20 | 30 | 12.79/13.22 | 4.24/5.05 | 4.136/0.635 | 1/1/1 | 2/1/1 | 870/60 |
| Undo | 50 | 0 | 2.69/3.78 | 2.72/6.69 | 1.100/1.100 | 6/4/4 | 7/6/4 | 0/30 |
| Undo | 50 | 10 | 11.77/13.44 | 3.60/4.57 | 4.081/1.403 | 8/4/2 | 2/2/2 | 270/60 |
| Undo | 50 | 30 | 31.51/32.54 | 3.74/5.66 | 10.038/1.411 | 19/4/1 | 3/1/0 | 870/60 |
| Undo | 100 | 0 | 4.43/5.21 | 4.49/5.67 | 2.104/2.106 | 7/5/4 | 10/6/5 | 0/30 |
| Undo | 100 | 10 | 23.87/24.57 | 6.34/6.67 | 8.027/2.701 | 16/9/3 | 1/1/1 | 270/60 |
| Undo | 100 | 30 | 62.14/65.14 | 6.45/8.67 | 19.866/2.709 | 37/4/1 | 7/1/1 | 870/60 |
| Undo | 300 | 0 | 13.16/15.76 | 13.05/14.98 | 8.371/8.371 | 22/19/13 | 19/15/10 | 0/30 |
| Undo | 300 | 10 | 74.78/77.68 | 18.99/20.99 | 41.185/11.658 | 63/46/23 | 23/20/12 | 270/60 |
| Undo | 300 | 30 | 197.90/203.69 | 19.80/30.39 | 106.815/11.665 | 142/84/33 | 21/18/10 | 870/60 |
| Undo | 500 | 0 | 22.28/25.50 | 22.10/26.63 | 13.863/13.864 | 24/21/10 | 27/25/14 | 0/30 |
| Undo | 500 | 10 | 128.13/131.76 | 32.54/36.39 | 68.448/19.321 | 91/60/25 | 23/21/7 | 270/60 |
| Undo | 500 | 30 | 329.85/340.25 | 31.18/35.53 | 177.419/19.329 | 218/121/38 | 22/19/2 | 870/60 |

### UI — Drop до compositor commit

| Operation | N | H | Off P50/P95 ms | On P50/P95 ms | Off/On MiB | Off GC0/1/2 | On GC0/1/2 | On hit/miss |
|---|---:|---:|---:|---:|---:|---|---|---|
| Move | 20 | 0 | 66.88/97.15 | 61.02/86.75 | 2.380/2.381 | 4/4/1 | 6/6/2 | 0/30 |
| Move | 20 | 10 | 66.91/96.89 | 61.44/112.40 | 3.574/2.388 | 5/0/0 | 5/5/1 | 300/30 |
| Move | 20 | 30 | 73.17/121.85 | 72.62/103.17 | 5.962/2.399 | 6/1/0 | 5/5/1 | 900/30 |
| Move | 50 | 0 | 60.70/103.30 | 90.84/109.69 | 5.651/5.688 | 0/0/0 | 38/38/13 | 0/30 |
| Move | 50 | 10 | 83.86/133.35 | 67.02/127.89 | 8.581/5.662 | 17/17/5 | 18/18/15 | 300/30 |
| Move | 50 | 30 | 115.49/151.25 | 66.77/121.68 | 14.429/5.669 | 29/29/7 | 2/2/0 | 900/30 |
| Move | 100 | 0 | 85.16/121.62 | 84.29/122.61 | 11.162/11.158 | 14/10/2 | 15/11/3 | 0/30 |
| Move | 100 | 10 | 108.90/133.67 | 78.99/127.25 | 16.976/11.124 | 26/20/2 | 15/12/5 | 300/30 |
| Move | 100 | 30 | 163.98/194.33 | 78.99/139.85 | 28.545/11.135 | 61/55/16 | 25/20/10 | 900/30 |
| Move | 300 | 0 | 156.83/194.96 | 146.26/194.32 | 34.523/34.524 | 52/52/12 | 51/50/15 | 0/30 |
| Move | 300 | 10 | 218.68/242.98 | 145.55/194.39 | 66.992/34.523 | 91/73/23 | 54/54/19 | 300/30 |
| Move | 300 | 30 | 346.21/381.07 | 164.00/195.09 | 131.946/34.532 | 169/106/34 | 59/59/23 | 900/30 |
| Move | 500 | 0 | 194.94/243.75 | 200.34/275.69 | 57.570/57.568 | 78/75/18 | 82/80/21 | 0/30 |
| Move | 500 | 10 | 309.71/351.37 | 188.02/248.33 | 111.508/57.564 | 132/82/20 | 69/65/10 | 300/30 |
| Move | 500 | 30 | 528.65/583.72 | 200.50/273.51 | 219.409/57.575 | 259/113/26 | 72/67/12 | 900/30 |
| Reparent | 20 | 0 | 91.55/242.70 | 103.50/193.51 | 16.627/16.674 | 39/39/7 | 51/51/20 | 0/30 |
| Reparent | 20 | 10 | 115.82/282.45 | 102.75/176.30 | 17.858/16.671 | 47/46/14 | 35/35/4 | 300/30 |
| Reparent | 20 | 30 | 127.58/290.79 | 115.75/187.47 | 20.245/16.683 | 50/43/14 | 50/50/20 | 900/30 |
| Reparent | 50 | 0 | 103.24/218.23 | 114.46/199.80 | 19.645/19.649 | 37/37/3 | 41/41/7 | 0/30 |
| Reparent | 50 | 10 | 125.18/242.83 | 128.02/261.33 | 22.571/19.653 | 49/49/10 | 40/40/6 | 300/30 |
| Reparent | 50 | 30 | 146.94/231.65 | 109.45/206.88 | 28.426/19.654 | 61/43/13 | 39/39/5 | 900/30 |
| Reparent | 100 | 0 | 127.44/175.89 | 133.43/181.51 | 24.600/24.609 | 55/50/15 | 59/52/17 | 0/30 |
| Reparent | 100 | 10 | 145.75/197.33 | 127.86/230.49 | 30.400/24.609 | 56/43/6 | 50/43/10 | 300/30 |
| Reparent | 100 | 30 | 193.19/236.78 | 127.52/267.14 | 41.975/24.619 | 98/63/23 | 54/45/13 | 900/30 |
| Reparent | 300 | 0 | 206.87/236.18 | 200.18/224.69 | 46.004/45.999 | 92/92/27 | 85/85/23 | 0/30 |
| Reparent | 300 | 10 | 248.70/292.35 | 194.26/213.18 | 78.525/45.996 | 124/97/28 | 82/82/21 | 300/30 |
| Reparent | 300 | 30 | 363.64/406.43 | 200.66/242.64 | 143.431/46.009 | 194/123/33 | 88/88/25 | 900/30 |
| Reparent | 500 | 0 | 255.23/334.11 | 242.83/284.90 | 68.048/67.236 | 110/106/23 | 107/103/21 | 0/30 |
| Reparent | 500 | 10 | 340.13/370.31 | 237.38/285.42 | 121.227/68.040 | 161/112/20 | 107/104/20 | 300/30 |
| Reparent | 500 | 30 | 577.42/661.53 | 260.86/381.54 | 229.070/68.058 | 284/145/32 | 109/105/22 | 900/30 |
| ToolboxCanvas | 20 | 0 | 98.01/229.59 | 85.17/137.42 | 13.540/13.548 | 49/48/20 | 27/27/4 | 0/30 |
| ToolboxCanvas | 20 | 10 | 91.69/249.83 | 68.04/180.81 | 14.705/13.514 | 37/35/9 | 18/14/3 | 300/30 |
| ToolboxCanvas | 20 | 30 | 101.04/245.61 | 95.04/160.35 | 17.067/13.530 | 40/30/8 | 49/47/21 | 900/30 |
| ToolboxCanvas | 50 | 0 | 92.56/130.19 | 104.08/124.04 | 15.199/15.198 | 35/35/10 | 36/36/13 | 0/30 |
| ToolboxCanvas | 50 | 10 | 103.54/133.57 | 85.87/219.13 | 18.086/15.196 | 41/41/11 | 31/31/6 | 300/30 |
| ToolboxCanvas | 50 | 30 | 115.05/196.11 | 85.09/116.96 | 23.878/15.203 | 42/30/1 | 29/29/6 | 900/30 |
| ToolboxCanvas | 100 | 0 | 90.82/109.31 | 92.09/109.67 | 18.015/18.027 | 31/31/5 | 39/39/11 | 0/30 |
| ToolboxCanvas | 100 | 10 | 122.81/160.48 | 88.85/111.98 | 23.727/18.043 | 51/51/14 | 27/27/3 | 300/30 |
| ToolboxCanvas | 100 | 30 | 151.82/173.48 | 85.57/106.60 | 35.186/18.015 | 61/43/2 | 27/27/2 | 900/30 |
| ToolboxCanvas | 300 | 0 | 103.50/127.27 | 97.41/130.88 | 30.702/30.703 | 39/39/11 | 43/43/16 | 0/30 |
| ToolboxCanvas | 300 | 10 | 188.38/212.97 | 103.66/145.56 | 63.002/30.705 | 99/77/38 | 37/37/8 | 300/30 |
| ToolboxCanvas | 300 | 30 | 304.37/317.55 | 96.45/111.72 | 127.402/30.709 | 163/112/40 | 34/34/6 | 900/30 |
| ToolboxCanvas | 500 | 0 | 118.03/152.17 | 122.41/159.04 | 43.020/43.011 | 44/27/13 | 45/26/13 | 0/30 |
| ToolboxCanvas | 500 | 10 | 249.58/262.98 | 110.71/141.11 | 96.476/42.998 | 119/73/30 | 33/12/0 | 300/30 |
| ToolboxCanvas | 500 | 30 | 449.73/468.64 | 109.98/122.48 | 203.568/43.007 | 230/104/36 | 31/12/0 | 900/30 |
| ToolboxDockPanel | 20 | 0 | 104.55/317.96 | 115.77/203.36 | 18.326/18.297 | 43/43/10 | 49/49/15 | 0/30 |
| ToolboxDockPanel | 20 | 10 | 117.22/282.15 | 116.40/248.36 | 19.509/18.418 | 43/42/8 | 45/45/11 | 270/60 |
| ToolboxDockPanel | 20 | 30 | 122.14/324.51 | 122.02/253.79 | 21.890/18.445 | 50/41/9 | 46/46/12 | 870/60 |
| ToolboxDockPanel | 50 | 0 | 111.81/281.90 | 111.09/269.48 | 21.665/21.685 | 46/40/8 | 44/42/9 | 0/30 |
| ToolboxDockPanel | 50 | 10 | 115.42/282.66 | 114.83/298.91 | 24.590/21.960 | 44/33/2 | 51/45/12 | 270/60 |
| ToolboxDockPanel | 50 | 30 | 147.28/197.33 | 118.96/233.35 | 30.424/22.013 | 65/45/13 | 45/39/7 | 870/60 |
| ToolboxDockPanel | 100 | 0 | 134.60/257.25 | 127.48/294.95 | 27.266/27.342 | 60/50/17 | 61/49/19 | 0/30 |
| ToolboxDockPanel | 100 | 10 | 159.80/216.98 | 140.17/177.41 | 33.048/27.894 | 81/58/25 | 61/46/16 | 270/60 |
| ToolboxDockPanel | 100 | 30 | 183.83/218.33 | 133.42/296.81 | 44.664/27.930 | 92/53/17 | 62/48/18 | 870/60 |
| ToolboxDockPanel | 300 | 0 | 156.39/207.33 | 154.57/189.86 | 51.197/51.199 | 80/53/14 | 73/50/9 | 0/30 |
| ToolboxDockPanel | 300 | 10 | 237.88/268.51 | 160.34/188.48 | 83.675/54.436 | 118/80/17 | 85/58/13 | 270/60 |
| ToolboxDockPanel | 300 | 30 | 365.13/398.31 | 161.89/185.57 | 148.593/54.474 | 199/116/33 | 74/48/7 | 870/60 |
| ToolboxDockPanel | 500 | 0 | 239.13/281.37 | 252.39/307.03 | 74.808/74.985 | 109/90/16 | 114/96/21 | 0/30 |
| ToolboxDockPanel | 500 | 10 | 355.12/400.78 | 232.60/305.03 | 128.866/80.211 | 171/126/23 | 112/88/12 | 270/60 |
| ToolboxDockPanel | 500 | 30 | 529.67/590.99 | 236.22/276.18 | 236.686/80.197 | 271/134/21 | 110/85/12 | 870/60 |
| ToolboxGrid | 20 | 0 | 122.63/299.25 | 123.93/244.70 | 20.356/20.341 | 58/57/22 | 68/68/30 | 0/30 |
| ToolboxGrid | 20 | 10 | 109.11/313.26 | 127.73/149.87 | 21.494/20.418 | 51/47/10 | 50/50/0 | 270/60 |
| ToolboxGrid | 20 | 30 | 140.39/329.77 | 134.78/236.75 | 23.885/20.466 | 59/49/13 | 72/65/16 | 870/60 |
| ToolboxGrid | 50 | 0 | 129.45/296.47 | 148.70/299.50 | 23.884/23.847 | 59/59/19 | 56/54/16 | 0/30 |
| ToolboxGrid | 50 | 10 | 128.64/228.22 | 127.84/256.69 | 26.802/24.163 | 56/46/11 | 55/54/14 | 270/60 |
| ToolboxGrid | 50 | 30 | 164.94/226.36 | 127.28/172.89 | 32.496/24.213 | 80/61/22 | 58/57/17 | 870/60 |
| ToolboxGrid | 100 | 0 | 122.65/161.88 | 122.52/172.24 | 29.640/29.668 | 57/42/10 | 59/42/11 | 0/30 |
| ToolboxGrid | 100 | 10 | 147.23/182.90 | 134.55/193.02 | 35.391/30.224 | 68/42/10 | 60/43/12 | 270/60 |
| ToolboxGrid | 100 | 30 | 190.89/222.23 | 136.25/159.67 | 46.859/30.255 | 99/59/17 | 67/49/18 | 870/60 |
| ToolboxGrid | 300 | 0 | 169.68/190.65 | 166.04/203.40 | 54.706/54.693 | 75/54/2 | 76/51/3 | 0/30 |
| ToolboxGrid | 300 | 10 | 229.78/271.44 | 184.63/212.68 | 86.801/57.839 | 117/74/9 | 83/59/4 | 270/60 |
| ToolboxGrid | 300 | 30 | 378.21/407.91 | 177.58/213.46 | 151.338/57.922 | 202/115/30 | 77/54/4 | 870/60 |
| ToolboxGrid | 500 | 0 | 283.49/322.94 | 256.99/302.19 | 80.100/79.951 | 133/116/30 | 114/97/12 | 0/30 |
| ToolboxGrid | 500 | 10 | 373.71/420.09 | 276.63/335.72 | 132.991/85.296 | 176/124/21 | 128/101/23 | 270/60 |
| ToolboxGrid | 500 | 30 | 572.32/641.33 | 264.81/303.45 | 239.834/85.222 | 287/163/28 | 134/109/26 | 870/60 |


## 13. Build и functional regression

`dotnet build FormDesigner.sln -c Release -m:1 --no-restore`: **0 errors**,1 прежний warning NU1603 — VisualStudio.SDK запрашивает Analyzers≥17.7.20, используется уже выбранная17.7.22. Это не обновление пакетов данным PoC. Smoke runner Release: **0 errors / 0 warnings**. Production hash после сборки совпадает с benchmark DLL.

| Suite | Первичный cache-on результат |
|---|---:|
| Undo History Cache |25/25 |
| Layout Phase7A.2 |13/13 |
| Layout Phase7A UX |15/15 |
| Nested Layout |59/59 |
| Grid Drop |40/40 |
| Grid Editor |45/45 |
| Native Grid |28/28 |
| Designer Pointer / Marquee |26/26 |
| Layout Validation |55/55 |
| Stabilization audit |**24/25** |
| DataGrid offline contracts |18/18 |
| PostgreSQL offline contracts |20/20 |
| Preview Memory |**23/28** |
| Broad selected regression (generated builds/runtime, Multi Form, AXAML, VSIX/VsHost/IPC и др.) |**70/72** |

Это не только model tests: PointerFixture использует настоящее Avalonia Window и native/routed input; Inspector проверяется по visual descendants; Drop ждёт actual native measurements/wrapper identity/layout/compositor; memory suite открывает/закрывает настоящие PreviewWindow и проверяет WeakReference. Physical mouse и интерактивная Visual Studio этим не заявляются.

### Красные результаты и контролируемые повторения

**Stabilization:** ClosedPlainWindowNegativeControl удержал6/6 обычных Avalonia Window, не содержащих DesignerDocumentSession/cache. Полная неизменённая suite повторена отдельно: cache off24/25, cache on24/25 — тот же отрицательный контроль; обе Preview collectible проверки в этой suite проходят. Изолированный plain-window сценарий проходит1/1 с off и1/1 с on. Это зависит от предшествующего UI-сценария/окружения и согласуется с ранее описанным ограничением обычного Window в PreviewMemoryLeakFix.md. Новый dump/GC-root analysis здесь не проводился; конкретный root этого прогона не объявляется установленным. Preview workaround к plain Window и ослабление assertion не добавлялись.

**Preview Memory:** первый полный cache-on прогон23/28, failures: Legacy_EmptyGrid, Legacy_MultiForm, AXAML_MultipleGrids, AXAML_TwentyCycles, AXAML_Eremex — закрытый Window ещё жив после стандартного idle/GC checkpoint. Lifecycle уже содержит Closed/Disposed/AutomationReleased, active/pending counters не показывают активную загрузку. Это не доказывает причину удержания.

Затем выполнены последовательные полные повторения **без изменения assertions/ожиданий/production-кода**: cache off **28/28**, cache on **28/28**. В cache-on repeat Legacy и AXAML прошли каждый20 циклов, aliveWindows=0, activeWindows=0, clockObservers=0; contexts/ItemsSource также прошли исходные WeakReference проверки. Cache-specific orphan/session/closure tests25/25 стабильны. Первый красный лог сохранён отдельно от повторов. **Нестабильное удержание Preview остаётся незакрытым наблюдением; отсутствие memory-проблем всего продукта или его связь с cache не доказаны.** По структуре cache содержит только strings и не хранит Window/VM/delegate; новый удерживающий reference path от cache не найден.

**Broad70/72:** EremexTextEditorVerticalSlice и EremexDataGridControlVerticalSlice падают при создании generated Window: inner `FileNotFoundException`, `Avalonia.Diagnostics, Version=11.1.1.0`. Сгенерированный проект собран, но этот runtime smoke не прошёл. Повтор **только этих двух сценариев с cache off** воспроизводит те же2/2 failures. Это не исправлялось добавлением Diagnostics package в Export/Release host: такое изменение выходит за рамки cache PoC и могло бы скрыть проблему runtime dependency resolution. Точное исправление loader/export policy требует отдельной задачи. Остальные70 сценариев, включая layout exports, source-preserving patches и bridge/VsHost contracts, прошли. Нет утверждения, что весь Export/runtime matrix зелёный.

Первоначальный orchestrator остановился на stabilization failure; оставшиеся suites выполнены отдельно последовательно с тем же Release/cache-on. Финальный orchestrator теперь собирает список failures и продолжает независимые suites, **возвращая exit1**, если есть хотя бы одна красная suite. Assertions существующих tests не менялись. Повторы не заменяют первичные результаты в таблице и не складываются с ними ради увеличения pass count.

Логи: `artifacts/undo-history-cache-poc/regression/{suite}.log`, `stabilization-cache-0-repeat.log`, `stabilization-cache-1-repeat.log`, `plain-window-cache-{0,1}.log`, `preview-memory-cache-{0,1}-repeat.log`, `eremex-cache-off.log`. Live SQL Server/LocalDB и PostgreSQL variables не заданы; live integration явно SKIP, production DB не использовались. Исследовательские данные вместе с оставшимися regression artifacts занимают около36.5MiB; больших dumps/новых diagnostic tools не создавалось.

## 14. Решение и границы приёмки

**Решение: KEEP.** Кэш неизменённых caption pairs устраняет повторный разбор старых snapshots, снижает UI-thread allocations и улучшает реальные Add/Move/Reparent при H10/H30, в том числе на обычных50–100 controls. Snapshot storage/serialization/atomicity/Undo/Redo остаются прежними; rollback доступен одним process flag. Тесты snapshot equivalence, branching, Multi Form, selection, rich DataGrid/Logic и освобождения cache-owned references проходят. Польза не ограничена синтетическим formatter benchmark.

KEEP относится к **этому изолированному PoC**, а не к выпускной сертификации всего Designer. Новых сбоев Undo/Redo или cache-specific retention в выполненных проверках не обнаружено. При этом общие tests содержат перечисленные failures, а первоначальная memory suite нестабильна. Эти результаты не выдаются за полностью пройденную regression и не объявляются исправленными отключением/включением cache.

Оставшиеся работы: отдельный controlled GC-root проход для нестабильных Preview checkpoints; диагностика missing Avalonia.Diagnostics в Eremex generated-runtime smoke; подтверждение physical DnD/DPI/мультимониторного UX и UI tails. Они **не начаты автоматически**. Full snapshot storage и renderer не переписывались; переход на commands/incremental projection по этим данным не требуется для сохранения достигнутого выигрыша.

**Grid-first readiness: NOT READY** для безусловного переключения default. На100/H30 Grid стал136.25/159.67ms P50/P95, но на500/H0 остаётся256.99/302.19ms, некоторые P95 Dock/Reparent не улучшились, есть незакрытые runtime/memory acceptance observations и physical QA. Это не означает, что500 controls — обязательный типичный случай: положительный эффект на50–100 подтверждён отдельно. Default Canvas сохранён. Phase7B и другие оптимизации не начинались.