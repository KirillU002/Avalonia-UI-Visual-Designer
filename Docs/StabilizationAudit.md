# Alpha 4.0 — аудит стабильности

Дата: 31.10.2026. Исходный commit: `0f807cc` — «Реализовано визуальное перетаскивание компонентов между ячейками Grid».

## 1. Итог и ограничение прохода

Воспроизведены семь проблем. Исправлены шесть: три P0, связанные с открытием/сохранением и защитой несохранённых документов, и три P1, связанные с распознаванием workspace и сохранностью backups. Одна P2 — удержание закрытых Preview с native Grid/DataGrid — оставлена в backlog для установления GC root. Архитектура, версии NuGet и TargetFramework не менялись. Новые возможности продукта не добавлялись.

Аудит не является заявлением об отсутствии остальных ошибок. Подтверждённые дефекты отделены от кодовых рисков и непроверенных сценариев. Полный новый audit runner имеет два известных failing memory checks; они не переименованы в успешные tests.

## 2. Baseline до production-изменений

- `git status --short` был пустым. Пользовательских незакоммиченных изменений не было. Git history, ветка и commit не переписывались.
- Последние commits: `0f807cc` — Grid placement; `a340118` — Grid Editor; `cece503` — native Grid root; `c88fdd1` — Marquee; `99396e6` — DataGrid Preview.
- `FormDesigner.sln --no-restore -m:1`: **0 ошибок, одно существующее NU1603** в зависимости VSIX SDK analyzers: запрошена 17.7.20, разрешена 17.7.22. Пакеты ради предупреждения не обновлялись.
- Smoke runner: **0 ошибок, 0 предупреждений** после последовательной сборки. Первая попытка была запущена одновременно с solution и получила CS2012 из-за общего PDB. Это ошибка организации проверки, а не дефект продукта; исходный лог сохранён отдельно.
- Существующие focused suites: Grid Drop **40/40**, Grid Editor **45/45**, native Grid **28/28**, pointer **26/26**, DataGrid offline **18/18**, PostgreSQL offline **20/20** — всего 177.
- Широкая выборка существующих smoke scenarios: **72/72** с учётом rerun трёх IPC-сценариев. В sandbox они получили Access denied в NamedPipeClientStream.Connect; вне sandbox все три прошли. Отказ sandbox не записан как ошибка IPC продукта.
- Live SQL: отдельная LocalDB, **25/25 live checks** плюс те же 18 offline contracts, итог существующего runner **43/43**.
- Live PostgreSQL не проводился: тестового сервера нет. Контрактные проверки не считаются integration tests.

Baseline-логи: `artifacts/stabilization-audit/baseline-*.log`. Список 72 сценариев: `scenario-names.txt`. Успешные generated outputs автоматически очищены штатным smoke harness.

## 3. Карта аудита

| Область | Что исследовано / чем проверено |
| --- | --- |
| MainWindow, MainWindowViewModel | Open/Save, await boundaries, активные sessions, busy/settings callbacks, Preview lifecycle |
| DesignerDocumentSession, Document Model | Владение Controls/selection/history, snapshots/dirty, dispose, JSON restore всех форм |
| DesignerSurface, Toolbox | Реальные raw/routed pointer suites, drag/drop, capture, resize, Marquee, недоступный descriptor |
| Layout Phase 1–4 | Actual Measure/Arrange, Grid definitions, ParentId, spans, nested reparent, stale geometry, zoom, Undo/Redo |
| Property Inspector | Selection/session binding, Favorites, отсутствие лишнего rebuild, изменения после Add Form, enum/layout editors |
| Preview / Export | Legacy/AXAML, opacity/CornerRadius, actual Grid geometry, стандартные items controls, независимость export snapshot |
| DataGrid / BindingSource | Пустой/unbound Grid, реальные SQL rows и visible cells, aliases, фильтры, multiple grids/forms, cancellation/timeout |
| SQL Server | Изолированная LocalDB с fixture; connections и пустые/ошибочные запросы в обоих Preview |
| PostgreSQL | Offline provider/settings/export/secret contracts и mixed-provider export; без live server |
| DLL Import | Metadata/cache cleanup, explicit sample fallback и сохранение реальных строк |
| Eremex | Designer controls, отсутствие влияния plugin theme на chrome, Grid placement и generated build |
| AXAML Import / Round-trip | Реальный MainWindow, comments/unknown attributes/raw content/styles/bindings, minimal patches, stale versions |
| VSIX / VsHost / IPC | Solution packaging, команда, dependency closure, отсутствие Avalonia references в bridge, реальный VsHost process/pipe/reload/disconnect |
| Settings / Help | Сохранение, Apply/Cancel, конечный viewport ScrollViewer, повторное открытие, поиск в Help |
| Производительность | Ограниченный native Designer на 100 controls, repeated measured hit tests, GC/WeakReference закрытых Preview |

Матрица не покрывает каждое сочетание всех свойств/controls. DockPanel проверен существующим AXAML layout сценарием; полноценный новый DockPanel drag workflow не создавался.

## 4. Приоритеты и статусы

| Id | Приоритет | Проблема | Результат |
| --- | --- | --- | --- |
| STAB-01 | P0 | Отклонённый native JSON уже заменил workspace/disposed текущую сессию | Исправлено |
| STAB-02 | P1 | Ошибочный workspace интерпретируется как пустой single-document JSON | Исправлено |
| STAB-03 | P0 | Async Save подтверждает другую/более новую ревизию; AXAML save может записать patch другой сессии | Исправлено |
| STAB-04 | P1 | Два быстрых backup получают одинаковый путь и один уничтожает предыдущую ревизию | Исправлено |
| STAB-05 | P1 | Retention одного документа удаляет backup другого с общим префиксом имени | Исправлено |
| STAB-07 | P0 | Чистая активная форма скрывает unsaved другой формы от Close/New/Open и autosave | Исправлено |
| STAB-06 | P2 | Закрытые Preview с native Grid/DataGrid остаются живыми после GC | Воспроизведено; root требует профилирования; не исправлялось предположительным cleanup |

До исправлений первые 12 новых checks дали **0/12**. Это tests конкретных наблюдаемых последствий, а не проверки наличия строк в коде. STAB-05 найден затем при проверке соседнего backup workflow; отдельный assertion также сначала падал.

Прежние tests в основном проверяли успешный load/save, работу конкретной активной формы и результат patch. Они не задерживали storage между await, не проверяли identity старой сессии после отказа загрузки, не сохраняли два backup в одну секунду и не сравнивали retention соседних имён. Close-during-query tests проверяли отмену/отсутствие render после закрытия, но не собираемость Window после GC.

## 5. STAB-01 — preflight до замены workspace

### Воспроизведение

1. Открыть JSON или source AXAML, добавить несохранённое изменение и выбрать control.
2. Передать в LoadDocumentJson документ с неподдерживаемым LayoutSemanticsVersion=3.
3. Либо открыть workspace с допустимой активной формой и недопустимой второй формой.

Фактически исключение возникало после DisposeAllDocumentSessions и замены workspace/active form. В случае AXAML ещё раньше очищался source context. Выбранный control и старая сессия уже не соответствовали открытому проекту. Недопустимая неактивная форма принималась и могла упасть только при последующем переключении.

Ожидание: отказ от входного документа сохраняет текущую Workspace, Session, Controls, selection, dirty/history и исходный AXAML.

### Root cause и исправление

Native validation находилась внутри ApplyDocument, до которого ApplyWorkspace уже выполнял разрушительные операции над текущей сессией. ClearAxamlRoundTripContext вызывался ещё до этой проверки.

Добавлен preflight всех входящих форм и их CurrentSnapshot до изменения активного состояния. Используется существующий ValidateNativeDocument, без второго layout engine. Проверяются также структурные null-коллекции/элементы и идентификаторы форм. Только после успешной проверки выполняются очистка AXAML context, persist старой формы и dispose старых sessions. Старый Canvas не конвертируется.

Это предотвращает воспроизведённые ошибки входных моделей; не заявляется универсальный rollback произвольного будущего exception в любом plugin callback.

## 6. STAB-02 — malformed workspace не является одиночной формой

### Воспроизведение

Открыть при существующем проекте JSON с `Project.Forms=null`, `Project.Forms="invalid"` или `Project=null`.

Фактически TryDeserializeWorkspace перехватывал ошибку либо нормализовал null Project. Возвращённый false означал для caller «попробовать одиночный DesignerDocumentFileModel». Неизвестные свойства Project игнорировались, и открывалась пустая форма.

### Исправление

Распознавание workspace и его валидация разделены: false возвращается, когда маркера Project нет. Для распознанного, но некорректного workspace возвращается диагностическая ошибка через исключение до применения документа. Имя Project распознаётся без учёта регистра в соответствии с существующими JSON options сервиса. Отсутствующие необязательные поля старого формата продолжают получать модельные defaults; явно повреждённая обязательная структура отклоняется.

## 7. STAB-03 — snapshot и identity асинхронного Save

### Воспроизведение

Tests используют реальный MainWindow и его Save methods с управляемой задержкой storage:

1. Начать JSON/AXAML Save и задержать OpenWriteAsync.
2. Изменить control либо создать новый проект.
3. Разрешить запись и проверить output, dirty state и CurrentDocumentPath.
4. Для path AXAML отдельно задержать ReadAllTextAsync, открыть другую AXAML-сессию с тем же исходным текстом, изменить её и завершить read.

Фактически JSON Save помечал текущее более новое состояние сохранённым и мог назначить новому проекту путь старого. AXAML Save при поздней правке сообщал ошибку после фактической записи, поскольку ACK сравнивался с уже изменённой моделью. После задержанного чтения patch строился из новой активной AXAML-сессии и мог попасть в исходный файл старого документа.

### Root cause

После await код повторно обращался к VM, active session и source context без привязки к операции, а MarkDocumentSaved без acknowledgedSnapshot подтверждал текущую ревизию. Содержимое файла и сохранённый snapshot переставали совпадать.

### Исправление

- Перед I/O захватываются VM/Workspace/Session identity, source context, content snapshot и encoding.
- AXAML patch строится из захваченного документа. После read/backup и перед записью проверяется identity. Patch нового документа не может записаться в старый путь.
- После завершения записи metadata обновляется только для соответствующего workspace/source session. Уже начатая stream-запись содержит захваченную ревизию, даже если UI переключился.
- Переиспользован существующий acknowledgedSnapshot механизм MarkAxamlRoundTripSaved, уже применяемый IPC ACK. Source-preserving writer и IPC protocol не переписывались.
- JSON MarkWorkspaceSaved подтверждает сохранённый snapshot каждой записанной формы. Новые формы и поздние изменения не становятся clean. После переключения формы внутри того же workspace подтверждаются правильные ревизии всех форм.
- Повторный Save во время активной записи явно отклоняется с сообщением и возможностью повторить после завершения; два writer не гоняются за одним файлом.
- Recovery draft удаляется только при отсутствии несохранённых изменений всего текущего workspace.

Проверены обычный Save без конкурентных изменений, поздние правки, новый проект, Multi Form, второй Save и восстановление работоспособности после отклонённой конкурентной записи. Изменение свойств во время записи не блокируется и не теряется.

## 8. STAB-04 / STAB-05 — сохранность backup

STAB-04 воспроизводился двумя последовательными backup разных ревизий в одну секунду. Имя содержало timestamp только до секунды, а File.Create обнулял существующий файл. Новые имена содержат полное исходное имя, timestamp до долей секунды и Guid; FileMode.CreateNew исключает тихое перезаписывание.

STAB-05 воспроизводился на `MainWindow.axaml` и `MainWindow.copy.axaml`: после семи backup первого документа старый backup второго исчезал. ListBackups использовал только StartsWith базового имени, поэтому TrimBackups получал чужие записи.

Теперь принадлежность определяется полным именем документа и проверяемым timestamp/Guid suffix. Legacy-имена с прежним timestamp тоже распознаются, но остаток имени проверяется целиком: `copy.` не считается частью timestamp. Проверены сохранение другой истории, лимит пяти версий и чтение старого backup. Backup content и формат пользовательского JSON/AXAML не изменяются.

## 9. STAB-07 — защита всего workspace при чистой активной форме

При финальном review общего confirmation workflow найден дополнительный P0. Воспроизведение: сохранить проект с Form1/Form2, изменить Form1, перейти на чистую Form2, вызвать обработчик подтверждения Close/New/Open. Диалог не появлялся и операция разрешалась. Автосохранение также пропускалось; переход на чистую сессию мог удалить recovery.

Root: `HasUnsavedChanges` описывает только active session, но использовался как признак dirty всего проекта. Оба новых tests сначала дали 0/2.

Добавлен вычисляемый `HasUnsavedWorkspaceChanges` поверх существующих snapshots и Form.IsDirty. Он используется в защите Close/New/Open, autosave, очистке recovery и workspace status. Семантика active-session `HasUnsavedChanges` и host-buffer IPC не менялась. Если после выбранного Save осталась поздняя правка, закрытие/замена проекта не продолжается. Отдельный Save-confirmation test проверяет этот сценарий через задержанный host file.

Это не новая Document Model: общий dirty predicate переиспользует существующие form/session state. Проверены Cancel при неактивной dirty форме, запись её данных в recovery, сохранение recovery при переключении обратно на чистую форму и запрет Close после поздней правки во время Save.

## 10. STAB-06 — подтверждённое удержание Preview, backlog

Для каждого режима выполнялись warm-up, шесть открытий/закрытий native Grid/DataGrid Preview, idle 1 секунда и два GC с ожиданием finalizers. Factory помечена NoInlining, ссылки на окна в тесте — WeakReference. Отдельно тот же процесс закрывает шесть обычных Avalonia Window с Button.

Результат финального запуска:

| Сценарий | Живых окон после GC | Managed memory до → после |
| --- | --- | --- |
| Обычный Avalonia Window, 6 закрытий | 0/6 | Контроль без измерения delta |
| Legacy native Grid/DataGrid Preview, 6 закрытий | 6/6 | 269 902 472 → 272 252 608 bytes |
| AXAML native Grid/DataGrid Preview, 6 закрытий | 6/6 | 281 050 880 → 283 653 960 bytes |

Оба memory assertions остаются failing. Наблюдение повторилось в двух запусках. Это подтверждает удержание окон в проверенной конфигурации и рост managed heap, но не устанавливает владельца сильной ссылки и не доказывает одинаковое поведение всех controls. Ordinary Window control исключает общее удержание любого окна тестовым кодом.

Root ещё не установлен. Изучены stop/cancel при Closed, render timer, context/build services и subscriptions; отключать их наугад не стали. Следующий этап: GC-root profile с минимальным DataGrid/Preview fixture, отделение Avalonia DataGrid/theme subscriptions от кода Preview, затем regression test для найденного владельца. Большой heap dump в этом проходе не создавался, debugger NuGet не устанавливались.

## 11. Производительность и дополнительные риски

Ограниченная проверка на 100 native controls в настоящем Designer:

```text
modelMs=741
designerShowAndIdleMs=904
hitTests=200; hitTestMs=56; hitTestAllocatedBytes=40106088
```

Время показа включает искусственное ожидание idle 120 ms; это не чистое время одного render и не benchmark. Зависания на этой выборке не обнаружены. В ResolveGridDropTarget есть сериализация геометрического fingerprint на частых вызовах; измеренный объём allocations — повод для отдельной оптимизации с сохранением stale-target guards. В этом проходе механизм не менялся.

Кодовые риски для следующих воспроизводящих проверок, **не включённые в число семи подтверждённых дефектов**:

- RestoreBackupButton_Click читает backup через JSON loader; отдельно проверить полный UI workflow восстановления AXAML backup.
- AppSettingsService использует общий `.tmp` путь; проверить перекрытие timer-save и Apply/Save при медленном диске.
- PluginLoader намеренно удерживает load contexts до shutdown; отдельно измерить повторный rescan/failed assemblies. Существующие DLL cache cleanup tests не доказывают отсутствие всех AssemblyLoadContext leaks.

## 12. Финальная проверка

| Проверка | Результат |
| --- | --- |
| Полный solution, включая VsHost/VSIX packaging | 0 ошибок; одно прежнее NU1603 |
| Smoke runner | 0 ошибок, 0 предупреждений |
| Новые persistence/backup/workspace protection checks | 21/21 |
| Bounded Designer performance / plain-window GC control | 2/2 |
| Полный новый audit runner | **23/25**, два известных P2 memory failures |
| Grid Drop / Grid Editor / native Grid / pointer | 40/40, 45/45, 28/28, 26/26 |
| DataGrid / PostgreSQL offline | 18/18, 20/20 |
| Широкая regression matrix | 72/72 на окончательном коде, включая реальные VsHost/IPC tests вне sandbox |
| Live SQL Server | 25/25 live; с 18 offline contracts runner завершён 43/43 |
| Exported Grid application runtime | NativeButton в Grid[1,1]; DataGrid 570×319 → 810×439 DIP при resize; root=Grid |

Широкая выборка включает DesignerSurface, Multi Form, Inspector/Favorites, session disposal, Settings/Help, стандартные items controls, Preview/Export properties, SQL/DLL fallback, Eremex, Grid export, реальный MainWindow AXAML, minimal patches/conflicts и VsHost/Named Pipes/dependency closure.

Live SQL использует созданный только для этого аудита экземпляр LocalDB `FormDesignerAudit_20261031_134601`, Windows authentication, отдельную базу и fixture Customers/Orders/EmptyTable. Проверяется ItemsSource и видимый текст всех различающихся строк, а не только успешный SQL query. Проверены aliases, empty/unbound, ошибки запроса/соединения, фильтры, несколько grids/forms, reopen, cancellation и timeout. Production/user databases не использовались. PostgreSQL live отсутствует.

После live tests созданный экземпляр LocalDB остановлен и удалён. Временные generated run directories удалены после завершения процессов, с проверкой абсолютных путей внутри каталога этого аудита. Логи, списки scenarios и небольшие воспроизводящие fixtures сохранены: весь `artifacts/stabilization-audit` после cleanup — около 214 KiB. Внутрипроцессный cleanup smoke harness дважды получил временную блокировку загруженных generated assemblies; cleanup после выхода процессов прошёл. Другие artifacts не удалялись.

Сгенерированное приложение отдельно экспортировано/собрано и запущено в native окне вне рабочего экрана. Probe проверил parent/placement кнопки, существование TextBox/DataGrid и реальное изменение bounds после увеличения окна, затем восстановил generated Program.cs. Логи: `runtime-export.log` и `runtime-check.log`. Это runtime-проверка layout, а не live database integration экспортированного приложения.

Для generated restore/build process-only `NuGetAudit=false` исключает ожидание недоступного vulnerability feed. Manifest и версии пакетов не менялись. Generated projects продолжают использовать прежний net6.0; существующее NETSDK1138 не устранялось обновлением TFM.

### Повторный запуск

```powershell
dotnet build .\FormDesigner.sln --no-restore -m:1 --verbosity minimal
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --stabilization-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --grid-drop-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --grid-editor-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --native-grid-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --designer-pointer-checks
```

Не запускать две сборки одного source project одновременно: у solution и smoke runner общие obj/PDB. Для IPC и LocalDB нужны соответствующие права Windows. Полный `--stabilization-checks` сейчас возвращает exit 1 из-за двух открытых STAB-06 assertions; это ожидаемый документированный результат аудита, а не зелёный статус.

## 13. Изменённые файлы

| Файл | Изменение |
| --- | --- |
| `Services/ProjectWorkspaceService.cs` | Различение не-workspace и malformed workspace, ранняя структурная проверка |
| `Services/DocumentBackupService.cs` | Уникальные CreateNew backups, точная принадлежность и legacy matching |
| `ViewModels/MainWindowViewModel.cs` | Preflight до замены workspace/очистки source context; workspace dirty predicate |
| Новый `ViewModels/MainWindowViewModel.DocumentPersistence.cs` | Проверка всех форм/snapshots, подтверждение записанной workspace revision |
| `Views/MainWindow.axaml.cs` | Захват snapshot/encoding, guards после await, корректный ACK/dirty/recovery, защита неактивных dirty форм |
| Новый `Views/MainWindow.DocumentSave.cs` | Малое transient Save scope и защита от одновременной записи |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` | Dispatch audit checks, injectable test storage |
| Новый `smoke-tests/FormDesigner.ExportSmokeTests/Program.Stabilization.cs` | Воспроизведение, regression, GC/performance probes |
| Новый `Docs/StabilizationAudit.md` | Этот отчёт |

Production Preview, Export, Grid interaction, SQL/PostgreSQL services, Eremex descriptors, VSIX/IPC, NuGet и TargetFramework не менялись. Исправления ограничены открытием/сохранением/backup и защитой несохранённого workspace.

## 14. Что осталось проверить

- Установить и устранить GC root STAB-06; после этого memory tests должны проходить без ослабления ожиданий.
- Физическая мышь, несколько мониторов и DPI 125/150/200%; текущие raw/routed tests не заменяют такую матрицу.
- Interactive Visual Studio с несколькими редакторами и реальной командой меню; packaging и реальные VsHost process/pipe tests не являются этим сценарием.
- Live PostgreSQL, реальные пользовательские DLL/Eremex datasets; текущая проверка не выдает contracts за серверную интеграцию.
- Продолжительный session/memory soak и проекты на тысячи controls. Измерение на 100 controls не доказывает корректность всех крупных проектов.
- Потеря питания, disk-full и внешние writers в минимальном промежутке атомарной замены файлов. Они не моделировались как live filesystem fault injection.

Рекомендованный следующий проход: сначала STAB-06 с профилированием, затем UI-восстановление AXAML backups и concurrent Settings saves, после воспроизведения — узкие исправления. Новые Layout-фазы и расширение Toolbox в этот аудит не входят.
