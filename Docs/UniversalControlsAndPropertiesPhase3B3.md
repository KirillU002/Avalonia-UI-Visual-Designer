# Phase 3B.3 — AXAML Undo/Reset и финальный stability gate

Дата: 15.11.2026. Baseline: `aa9fc5965319cd59a98559eb6c2a66ad9616981b`, ветка `main`, исходное рабочее дерево чистое. Изучены отчёты Phase 3A, 3B, 3B.1 и 3B.2. Существующие NuGet/TFM, тема, Document Model, PropertyGrid, Preview/Export и VSIX architecture не заменялись. Phase 4 не начата; commit/tag/release не создавались.

## 1. Root cause P1

Дефект воспроизведён на исходном коде: импорт `NumericUpDown Value='12.5'`, Reset удаляет атрибут, вызов Undo не возвращает его. До исправления зафиксированы:

```text
model before Reset:       Minimum=0, Value=12.5
CurrentSnapshot before:   Minimum=0, Value=12.5
UndoSnapshot after Reset: Minimum=0, Value=12.5
HasUndo=true, UndoEditorCommand.CanExecute=false
after attempted Undo: UndoCount=1, RedoCount=0, model Value отсутствует
```

Snapshot не терял значение. `EditorCommand.Execute` проверял закэшированный CanExecute и возвращался, не вызывая Undo. Общий refresh команд дебаунсится на 80 ms; `ScheduleEditorCommandRefresh` останавливает и запускает timer заново, в частности на Output/diagnostic notifications. Наличие snapshot и состояние toolbar/keyboard command могли расходиться. Поздний refresh не исполняет уже пропущенное действие. Добавлять задержку в тест вместо исправления нельзя.

Точный путь:

```text
PropertyGrid row Reset / MainWindowViewModel.ResetPropertyGridProperty
  RemoveMetadataLocalValue -> BeginUndoBatch -> remove CustomProperties entry
    CommitUndoBatch -> NotifyDesignerStateChanged -> RegisterHistorySnapshot
      DesignerDocumentSession.PushUndoSnapshot -> RaiseDocumentStateProperties
        [раньше только общий отложенный refresh команд]
UndoEditorCommand.Execute -> CanExecute(false) -> no-op
```

`RestoreFromSnapshot`/`ApplyDocument` и `AxamlPatchWriter` в этом failed run не вызывались по Undo. Проверенная альтернативная гипотеза — suppression во время Inspector interaction — не подтвердилась: edit/interaction flags были false. Source Map и исходный literal оставались корректными. Доказательства: `p1-before.log`, `p1-diagnostic-before.log`, `p1-command-before.log` в ignored task artifacts.

## 2. Минимальное исправление

В `MainWindowViewModel.RaiseDocumentStateProperties` добавлены два синхронных `RefreshState` только для существующих Undo/Redo commands. Они выполняются в том же UI turn, когда изменяется история. Общий debounce остальных команд сохранён. Не изменены storage/serialization snapshots, transactions/coalescing, AXAML generation, source spans или IPC protocol.

После исправления исходный check прошёл **1/1**, затем полный PropertyGrid — **30/30**. Доступность Undo/Redo теперь проверяется непосредственно после Reset/navigation, без Dispatcher sleep.

## 3. Отдельные regression tests и source-preserving результаты

Новая suite `--stabilization3b3-checks`: **15/15**. Первая версия того же test source в отдельной `git archive` копии исходного commit дала **0/15**: команды ещё не готовы в том же UI turn. Это red-before/green-after, а не retry для скрытия ошибки. Затем расширена проверка реального изменения Auto/Star/Pixel definitions; итоговая suite также 15/15.

Проверены три последовательных Reset → Undo → Redo, одна snapshot на Reset, ControlId/ParentId/selection, Edit → Reset → два Undo → два Redo, Save/rebase и checksum conflict. Source no-op после Undo возвращает **точные исходные байты** string fixture: одинарные кавычки, `12.50`, XML entities, табуляция/CRLF, element namespace prefix `a:`, `x:Name`, комментарий, соседний control и неизвестный для редактора Tag.

| Значение | Проверка |
|---|---|
| string | GroupName с apostrophe/entity, Unicode edit и escaping; Reset/Undo/Redo |
| bool | IsEnabled=False, возврат присутствия/отсутствия local entry |
| enum | Calendar.FirstDayOfWeek=Monday |
| nullable | RadioButton.IsChecked=`{x:Null}`; null не подменяется default literal |
| Thickness | Padding=1,2,3,4 |
| CornerRadius | 1,3,5,7 с правильным порядком компонентов |
| Brush | BorderBrush=#80446688 |
| attached | Grid.Row edit через существующий source model/drag path, Undo/Redo, exact source |
| GridLength | RowDefinitions Auto,2*,120 → Auto,3*,140 → Undo → Redo без смены units |
| Binding | read-only/reset-blocked, соседний literal edit и Undo сохраняют expression |
| Static/DynamicResource | такие же guards, исходные expressions не исполняются и не заменяются |

Legacy source placement не получил выдуманный Inspector GridRow editor или новый semantic-unset adapter. Первый новый test run был **14/15**: fixture ошибочно искала отсутствующую source Inspector row GridRow. Проверка переведена на уже поддержанный model/drag update path; production capabilities не расширялись и assertion сохранения source не ослаблен. Этот первый красный log сохранён.

Раздельные imported documents не смешивают history; загрузка нового source очищает старую историю и отклоняет stale Row. JSON Multi Form проверяет переключение Grid/Canvas форм, независимые histories, command availability и Save/Load. Физическое нажатие Reset/Undo в установленном VSIX не выдаётся за выполненную ручную проверку. Новые tests вызывают те же public Reset/command entry points, а существующий PropertyGrid проверяет native editors.

## 4. Source Map / VSIX authority

`AxamlImportService -> source map/capabilities -> current model -> AxamlPatchWriter` сохранён. Reset удаляет один attribute span; Undo восстанавливает модель из прежнего snapshot, после чего writer не создаёт edits к исходному тексту. Полной регенерации импортированного AXAML нет. Save/rebase даёт новый согласованный source baseline; внешний checksum mismatch блокируется.

VSIX по-прежнему владеет VS text buffer; отдельный VsHost возвращает patches. Версии/checksum/ACK/conflict guard не менялись ради этого исправления. Результаты реального автоматического IPC и supported-environment regression приведены ниже; интерактивная установленная Visual Studio — manual acceptance.

## 5. Collection Editor и совместимость

Полная Collections suite: **30/30**. Menu, ContextMenu, ComboBox, ListBox, TabControl, TreeView, Flyout: Add/Remove/Reorder, nested subtree operations, item edit/Reset, атомарный Undo/Redo, стабильные Id/selection и JSON contracts. ParentId/layout владельца не меняются из-за редактирования collection-owned hierarchy.

PropertyGrid 30/30 дополнительно проверяет typed values в native Preview после JSON, Favorites/search, stale rows, Multi Form и source guards. Pointer/Marquee, Grid/Canvas, nested layout — отдельные существующие runners, без нового layout engine. Изменение доступности команд применяется также к Add/Remove и переключению форм, не меняя их snapshots.

## 6. Надёжность native diagnostics

Перед любыми новыми UI запусками создан bootstrap archive **11 существующих dumps / 643390058 B**, с повторным SHA-256. Включён важный полный dump Phase 3B.2; исходные файлы не изменялись. Утраченные в Phase 3B.2 исторические WER binaries не восстановлены; старые hashes/stacks/metadata остаются границей доказательств.

Новый test-only `EvidenceStore` в существующем NativeCapture:

- До launch защищает существующие related `dotnet.exe.*.dmp`, `FormDesigner.exe.*.dmp`, `AvaloniaDesigner.VsHost.exe.*.dmp` из текущего CrashDumps.
- Отдельный vault по умолчанию `artifacts/uia-evidence-vault`, вне WER rotation; reparse paths запрещены. Важный dump вне WER можно сохранить явным `--preserve`.
- Каждый capture run требует свежую директорию и exclusive CreateNew marker. Новые dump names содержат UTC/PID/TID/GUID.
- Vault адресуется SHA-256: существующая копия проверяется, не перезаписывается; новая копия создаётся CreateNew, flush-to-disk и повторный hash. На время чтения исходного dump запрещены writes/deletion.
- Отдельные run/preservation/module/dump/result manifests. Raw CLI argument values не записываются.
- Проверяется free space: резерв 2 GiB плюс размер копии/оценка capture. Недостаток места останавливает запуск или явно отмечает CaptureFailed, не удаляет старые материалы.
- **Retention только ручной:** нет expiry, cleanup/prune/delete API, глобальных WER settings или публикации dumps. Повреждённая existing backup остаётся для анализа; автоматического repair/overwrite нет.
- RunCaptured/RunStress используют protected NativeCapture; останавливаются при failed/missing record, CaptureFailed или захваченном exception даже при последующем exit 0. Автоматических retries нет.

Storage checks **7/7**: verified copy/reuse, unchanged source, metadata/retention, deterministic insufficient-space guard, CreateNew metadata и corrupt-backup refusal. Реальный intentional fault self-test: exit **-1073741819**, full dump **53148552 B**, SHA-256 `f0f75f73116d8f084093050dd80c84c295d9b47661792c07f4143df1b920af9d`, отдельная protected copy совпала. Reused capture directory отклонена с exit 2, manifest не изменён. Это намеренный диагностический AV, **не новый crash Designer**. Существующий struct/COM/accessibility lifetime не правился.

## 7. Ограниченная UIA проверка

Не повторяется вся 2550-cycle matrix Phase 3B.2. Проверяется новый релевантный сценарий: Undo/Redo действительно исполняются во время запросов собственного UIA client, а не могут быть незаметным disabled no-op. В stress harness добавлены assertions, что Undo возвращает удалённый subtree, Redo удаляет его снова. У прежнего helper клиентский lease 100 ms завершался до двух history operations с 520 ms Dispatcher pumping. Для этого отдельного case lease установлен 2000 ms, дополнительно проверяется, что клиент не закончил queries до Undo и после Redo. Это длительность диагностического запроса, не скрытая задержка пользовательского Drop и не performance measurement. Depth=24, максимум 128 nodes; для Flyout обязательный реальный ValuePattern provider.

Ограниченная матрица завершена: **190 cycles**, все target/controller exit 0, Captured=false, CaptureFailed=false:

| Отдельный процесс / сценарий | Циклы |
|---|---:|
| Обычный Window + собственный Tree UIA client | 50 |
| Collection Editor + фактические Undo/Redo под UIA queries | 50 |
| Native Flyout PopupRoot + ValuePattern provider | 50 |
| Legacy Preview + Tree client | 20 |
| AXAML Preview + Tree client | 20 |

Перед матрицей два коротких sanity runs проверили обновлённый driver и lease assertions. Это отдельные observations, не retries скрытого AV. Full capture/storage self-test с намеренным AV в число product crashes не включён.

Исторический `ProviderCallouts::GetPatternProvider` null-provider fault остаётся **UNRESOLVED**. Принадлежность дефекта Windows/Avalonia/нашим callbacks не доказана. Production accessibility, GCHandle/COM reference counting и AutomationPeer не изменялись. Ни ноль повторений, ни новый storage helper не являются исправлением этого native риска.

## 8. Window retention

Свежий Stabilization: **25/25**, обычные закрытые Window — **0/6 alive**. Старый 24/25 сохранён в прежних отчётах; исправление его retaining chain не заявляется. В новой controlled Window50 matrix при живом клиенте checkpoint содержит 50 удержанных Window, после завершения собственного client — **0/50**. Editor sessions/Preview после освобождения lease также освобождаются. Связь зависит от client lifetime, но это не GC root исходного faulting UiaNode и не доказательство постоянной утечки. Нового failing retained object для gcroot не получено; прежние SOS chains не выдаются за свежие dumps. Windows accessibility не отключается.

## 9. Sandbox / normal environment

Предыдущие **69/72 sandbox + 3/3 вне sandbox** не переписаны. В этой фазе те же три целевых sandbox tests снова дали **0/3** с `UnauthorizedAccessException`, stacks `NamedPipeClientStream.TryConnect`:

1. AxamlHardeningIpcEditReloadLifecycle.
2. VsHostCanStartAndAcceptConnection.
3. BridgeSurvivesVsHostDisconnect.

IPC production code не изменён. Полный supported-environment run **72/72**, exit 0, Captured=false/CaptureFailed=false, 260747 ms. Все три проблемных sandbox scenarios вошли в эти 72. Отказ sandbox и успех normal environment разделены, первый результат 0/3 не заменён зелёным. Это подтверждает ограничение разрешений среды, а не исправление IPC production code. Ручная установка VSIX/devenv acceptance не заменяется автоматическим запуском VsHost. Автоочистка только собственных generated smoke outputs после успешных assertions получила AccessDenied на загруженной ToolboxStandardItemsExport.dll; лог сохранён, diagnostic vault не затрагивался и файл принудительно не удалялся.

## 10. Performance comparison

Используется существующий full profile, порядок counts **1/10/100**, Open/Add/Reorder/Selection, NestedAdd при 100 и AddSequence10/100 при 1. Fresh Release processes без debugger, одинаковые SDK/.NET/Avalonia/viewport/scaling, 3 warm-ups и 30 measured observations на scenario. Before — архив `aa9fc59`, after — текущий код; alternate before-a/after-a/before-b/after-b. Тяжёлые build/SQL/stress задачи до измерений завершаются.

Все четыре profiles завершены exit 0: **1980 raw samples = 180 warm-up + 1800 measured**. На каждую count/operation пару — 30 measured samples в run, 60 before и 60 after при объединении двух runs. Scaling=1.0, editor 900×650 DIP, одна Menu-модель. Synchronous duration включает action+UpdateLayout; allocations считаются на UI thread до compositor callback, а не whole-process/native heap. P95 nearest-rank, variance sample ddof=1. Callback не является физическим first pixel.

| Variant | Items | Operation | Median/P95 ms | Alloc median/P95 MiB | GC0/1/2 за 60 | Variance ms² |
|---|---:|---|---:|---:|---|---:|
| Before | 1 | Add | 18.32 / 29.39 | 0.57 / 0.64 | 1/1/0 | 61.12 |
| After | 1 | Add | 19.87 / 28.97 | 0.57 / 0.64 | 0/0/0 | 42.97 |
| Before | 1 | Reorder | 8.51 / 13.86 | 0.28 / 0.31 | 0/0/0 | 9.38 |
| After | 1 | Reorder | 9.23 / 16.77 | 0.28 / 0.33 | 3/3/0 | 29.37 |
| Before | 10 | Add | 37.03 / 100.33 | 2.45 / 2.51 | 13/13/1 | 485.58 |
| After | 10 | Add | 36.98 / 69.17 | 2.45 / 2.51 | 9/9/0 | 230.86 |
| Before | 10 | Reorder | 17.70 / 49.53 | 1.78 / 1.84 | 7/7/0 | 142.04 |
| After | 10 | Reorder | 17.69 / 47.69 | 1.79 / 1.84 | 6/6/0 | 97.01 |
| Before | 100 | Add | 36.08 / 70.05 | 2.35 / 2.41 | 10/10/1 | 296.78 |
| After | 100 | Add | 43.35 / 75.94 | 2.35 / 2.41 | 9/9/1 | 284.31 |
| Before | 100 | Reorder | 16.17 / 48.03 | 1.70 / 1.78 | 6/6/0 | 183.79 |
| After | 100 | Reorder | 20.87 / 52.53 | 1.69 / 1.75 | 10/10/1 | 160.18 |

N100 по отдельным повторениям:

| Run | Add median/P95 ms | Reorder median/P95 ms |
|---|---:|---:|
| Before A | 20.78 / 77.75 | 10.18 / 44.33 |
| After A | 43.35 / 82.57 | 20.58 / 52.53 |
| Before B | 37.18 / 67.52 | 17.09 / 49.49 |
| After B | 42.95 / 75.63 | 21.06 / 55.70 |

Повтор того же baseline binary изменил Add median с 20.78 до 37.18 ms: это подтверждает run-to-run variability при неизменённом коде. Но конкретное объяснение через OS scheduling/JIT/GC/UIA **не доказано**. В stage data Before B и After A повышены не только history stages, но и Inspector/structure tree; это не позволяет приписать всю разницу двум command RefreshState без отдельного causal profile.

**Ускорение и отсутствие timing-регрессии не заявляются.** Объединённый N100 Add median вырос примерно на 20%, Reorder — на 29%; наблюдение оставлено как P2 performance uncertainty. Allocations практически те же; N10 медианы совпадают, P95 ниже. Возможное влияние лишних command Hint notifications можно локально профилировать позже, но это не основание для большого рефакторинга или отката исправления потерянного Undo. Исторические 47/21 ms Phase 3B и 76/43 ms Phase 3B.1 сохраняются как исторические результаты, а не контрольные замеры этой пары. Их нельзя смешивать и с другим порядком 10/50/100 Phase 3B.2.

Полные Open/Selection/NestedAdd/AddSequence10/100, min/max, variance и GC каждого run находятся в `performance/summary.json`; raw samples сохранены. Новая массовая performance optimization не выполнялась.

## 11. Build и regression

Уже завершены:

| Suite | Результат |
|---|---:|
| Solution Release | 0 errors, 1 прежний NU1603 VS SDK Analyzers |
| PropertyGrid | 30/30 |
| Collections | 30/30 |
| Metadata | 45/45 |
| Universal Phase 2 | 40/40 |
| New Phase 3B.3 | 15/15 |
| Stabilization | 25/25 |
| Nested Layout | 59/59 |
| Pointer/Marquee | 26/26 |
| Grid Drop | 40/40 |
| Grid Editor | 45/45 |
| Layout 7A | 15/15 |
| PostgreSQL offline | 20/20; live server не настроен |
| PostgreSQL/Eremex contracts | exit 0; live PostgreSQL не заявлен |

Дополнительные обязательные результаты:

| Suite | Результат |
|---|---|
| Broad, normal environment | **72/72**, отдельный разрешённый запуск вне sandbox |
| Три IPC cases, sandbox | **0/3**, AccessDenied при TryConnect; не замаскированы повтором |
| LocalDB / DataGrid | **47/47**, Windows authentication, реальные значения ячеек |
| SQL source workflow | PASS: Settings/schema/stable BindingSourceId, оба Preview, 3 реальные строки |
| Preview Memory | **36/36**, 20 Legacy и 20 AXAML cycles, live SQL, close during loading |
| SQL generated runtime | PASS, Customers=3 / Orders=2, независимые ViewModels, nested Grid, resize |
| Collections generated Grid / Canvas | оба PASS, 6 native roots, реальные item types, nested Menu/Separator, Combo/List selection, tabs/content, tree expansion, ContextMenu/MenuFlyout, resize 800×600/1200×800/1600×900 |

SQL использовал только созданный GUID LocalDB instance `UniversalMetadata_16858d7378654295a771def6aca054b9`, который остановлен и удалён после проверок. PostgreSQL live и installed VSIX manual не заявляются. Первые P1 red, diagnostic fixture red и sandbox failures сохранены; повтор после конкретного исправления/смены permission context явно отделён от первого запуска.

## 12. Изменённые файлы

- `ViewModels/MainWindowViewModel.cs`: **4 строки production diff**, синхронная актуализация двух history commands.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новый `Program.Stabilization3B3.cs`: dedicated regression dispatch и tests.
- `Program.UiaStability.cs`: strict proof фактического Undo/Redo в concurrent-query case.
- `smoke-tests/UiaStability/NativeCapture/Program.cs`, новый `EvidenceStore.cs`: unique runs/dumps, preflight/archive/hash/manifests/free space и storage checks.
- `smoke-tests/UiaStability/RunCaptured.ps1`, `RunStress.ps1`, `README.md`: safe default diagnostics и явная retention policy.
- Этот отчёт. Остальное — ignored raw task artifacts; бинарные dumps в Git не включены.

## 13. Ограничения и ручная приёмка

Native UIA origin остаётся неизвестным. Не проверены длительный внешний screen reader soak, все ambient clients, другие Windows/DPI builds и интерактивная установленная Visual Studio. В imported source сохраняется прежний subset capabilities: неподдержанный legacy semantic Reset не превращается в произвольное удаление; сложные expressions остаются read-only. PostgreSQL live отсутствует. Full dumps содержат память процесса и должны оставаться приватными; manual retention требует контроля свободного места.

Следующий native шаг при положительном AV — уже защищённый full dump исходной сигнатуры, затем slot writer/COM lifetime trace. Без нового evidence повторять весь предыдущий native audit и менять accessibility не следует. Новых controls/properties не добавлено, Phase 4 не начата.

## 14. Итоговая оценка

**READY WITH CONDITIONS.** Подтверждённый P1 закрыт минимальным исправлением, PropertyGrid 30/30 и Collections 30/30, normal Broad 72/72, Preview/Export/generated runtime и live SQL/memory проверки проходят. Новых подтверждённых P0/P1 не обнаружено.

Условия:

1. UIA null-provider crash остаётся **UNRESOLVED**. В ограниченной новой матрице он не повторился; защищённый full capture готов, accessibility не отключена. Это не означает, что native bug исправлен.
2. Сохранены прежний client-dependent Window retention P2 и новый количественный performance uncertainty для N100. Постоянная утечка/причина timing-разницы в этой фазе не доказаны.
3. Installed VSIX/physical screen reader/другие Windows и DPI требуют отдельной ручной приёмки; sandbox IPC не считается поддерживаемым рабочим окружением. PostgreSQL live не проверялся.
4. Evidence vault приватный и без автоматической очистки; operator контролирует свободное место и retention вручную. Capture отслеживает свой target PID, не произвольные внешние UIA client/devenv процессы.

Доказательства и первые красные observations находятся в ignored `artifacts/universal-phase3b3`: `bootstrap-evidence.json`, `p1-*-before.log`, `new-checks-first.log`, `new-checks-baseline.log`, `extended-checks.log`, `core-results.json`, `ipc-sandbox.log`, `broad-normal.log`, `live-results.json`, `uia-*/`, `full-capture-check/`, `performance/`. Коммит/tag/release не создавались. **Phase 4 не начата.**
