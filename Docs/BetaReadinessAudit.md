# Beta Readiness Phase 1 — аудит и стабилизация

Дата: 04.11.2026. Проверяется Alpha 4.0, HEAD `a28f238bfc95fd0df6ebe49caa3d289c5ba5524c`.

## 1. Baseline и границы

До изменений `git status --short` пустой. Изучены отчёты Layout Phase 7B, Preview Phase 2, Preview Consistency Audit, Stabilization Audit, Preview Memory Leak Fix и Undo History Cache PoC, а также текущие пути загрузки, сохранения, recovery, Preview, экспорта и тестовые harnesses.

Исходная Release-сборка solution: **0 ошибок**, прежнее **NU1603** в VSIX: SDK.Analyzers 17.7.20 разрешается в 17.7.22. Начальные Grid-first checks **26/26**, Stabilization **25/25**; generic plain-Window negative control и оба Preview GC checkpoints прошли этот прогон. Это не отменяет историческую нестабильность, описанную в предыдущих отчётах.

Версия остаётся Alpha 4.0. Grid-first и JSON AXAML Preview defaults сохранены. NuGet, TargetFramework, тема, Document Model, Undo storage, Preview/Export production pipeline и VSIX/IPC не менялись. Tags/releases не создавались. Evidence находится в `artifacts/beta-readiness`; предыдущие результаты не перезаписывались.

## 2. Новые подтверждённые ошибки

Найдены, воспроизведены и исправлены **шесть независимых проблем: три P0 и три P1**. Изменены пять production-файлов. Первоначальный исследовательский тест пустых IDs оказался неверной гипотезой: для старого Canvas это поддерживаемый repair path. Он заменён положительной проверкой совместимости и не включён в число багов.

| ID | Приоритет | Воспроизведение | Root cause | Исправление |
|---|---|---|---|---|
| BETA-01 | P0 | Сохранить recovery с валидным workspace; временно открыть файл с `FileShare.Delete`, запрещающим второе чтение; вызвать `TryLoadDraftAsync`; снять блокировку | `AutosaveRecoveryService.TryLoadDraftAsync` при любом исключении вызывал `TryDeleteDraft`. Sharing violation удалял единственную исправную копию. Так же уничтожались исходные байты частично повреждённого JSON | Чтение recovery больше не удаляет файл. После снятия блокировки восстановление можно повторить; явный discard и штатные lifecycle cleanup остаются доступны |
| BETA-02 | P0 | В рабочей сессии открыть Canvas JSON с двумя одинаковыми непустыми ControlId; дополнительный случай — `duplicate`/`DUPLICATE` в Canvas и Grid | Native validation пропускала v1 Canvas. Кроме того, native `ToDictionary` сравнивал case-sensitive, а history/часть model maps используют OrdinalIgnoreCase. Исключение возникало уже после dispose старой session/замены workspace | Общий preflight повторяющихся непустых IDs использует OrdinalIgnoreCase до любых destructive изменений, для активной/неактивных форм и CurrentSnapshot. Missing legacy IDs по-прежнему получают свежие ID |
| BETA-03 | P0 | В Recovery выбрать «Open normally», затем открыть другой чистый проект | MainWindow очищал recovery при смене DocumentSessionId, New/Open/Close/Save, не различая принадлежащий текущей работе и отложенный черновик. Autosave другого документа также мог занять этот единственный slot | Pending recovery защищён до явного Restore/Delete. Все автоматические удаления проходят общий guard. Autosave другого документа приостановлен с объяснением в существующем AutosaveStatusText; после явного Delete возобновляется. При read failure оригинал также защищён |
| BETA-04 | P1 | Переключиться на форму и сразу удалить StackPanel/Group с детьми; выполнить Undo | DeleteSelected использовал общий batch с группировкой 500 ms. Restore/activation обновляет timestamp session, поэтому Delete сливался с предыдущей правкой; Undo возвращал слишком старое состояние без удалённого поддерева | Delete использует существующий структурный Begin/CommitGridEditorUndoBatch, уже применяемый Grid Editor. До/после операции установлена отдельная граница; storage/serialization не менялись. Проверены Canvas, Grid и реальный MainWindow |
| BETA-05 | P1 | В реальном MainWindow с BindingSource удалить контейнер, Undo, Redo | Getters AvailableSelectedSourcePreviewRowModes/AvailableControlPreviewRowModes при каждом refresh возвращали новый Where-iterator. ComboBox заменял ItemsSource, сбрасывал selection и через TwoWay писал временный null в PreviewRowMode. Reentrant BindingSource_PropertyChanged → RegisterHistorySnapshot очищал Redo | Стабильная identity существующей общей коллекции и один список разрешённых PostgreSQL modes на ViewModel. SQL/PG queries, источник данных и форматы не менялись. Проверен native Undo/Redo и переключение SQL/PG sources |
| BETA-06 | P1 | Читать recovery, пока публикуется новый draft; держать разрешающий rename/read/write reader и выполнить SaveDraftAsync | File.Copy поверх конечного recovery менял уже опубликованный файл на месте. Читатель видел новые байты в старом stream; File.Exists мог стать true раньше доступности полного JSON | Полностью записанный temp публикуется через File.Replace для существующего файла, File.Move для первого сохранения, в том же каталоге. Старый reader сохраняет старый JSON, новый reader получает новый draft. Формат recovery не менялся |

`edge-confirmed-before.log`: **1/4** — два recovery assertions и Canvas duplicate-ID падали, контрольный Grid preflight проходил. Для Canvas зафиксированы `sessionDisposed=True`, `sameWorkspace=False`, controls=2. После исправления тот же вход отклоняется до замены session. `edge-before.log` содержит более ранний exploratory прогон, включая отвергнутую гипотезу blank IDs; он сохранён отдельно.

Новые tests не ограничиваются проверкой исключения: сравниваются исходные байты recovery, повторное чтение, identity Workspace/Session/SelectedControl и признак dispose. Production `GC.Collect` не добавлялся.

Расширенный red-прогон `edge-four-causes-before.log`: **9/14**; дополнительно воспроизведены case-insensitive IDs, deferred recovery и оба Delete-after-switch. После исправлений **15/15** (`edge-final.log`), включая cross-provider native ComboBox/Redo и 20 New Project со WeakReference на старые sessions: **0 живых**.

BETA-05 найден именно в реальном UI, модельные Delete/Undo/Redo уже проходили. Сначала восстановились IDs/ParentId, но Redo оставался пустым. `workflow-redo-check.log` сохраняет этот провал; `redo-reset-stack.txt` фиксирует Avalonia ComboBox → BindingSourceModel.PreviewRowMode → BindingSource_PropertyChanged → RegisterHistorySnapshot. После fix native workflow сохраняет redo=1, затем выполняет Redo и повторный Undo.

Защита recovery намеренно не создаёт второй slot/новый формат. Если старый recovery отложен или пока не читается, пользователь должен сначала восстановить/удалить его либо явно сохранять текущую работу в JSON. Непрочитанный/отложенный файл больше не уничтожается ради нового autosave.

BETA-06 обнаружен при forensic-повторе: `InactiveDirtyFormAutosavesAndKeepsRecovery` увидел file=True, draft=False, status «Черновик ещё не создавался». После завершения записи тот же файл содержал валидный ожидаемый workspace. Затем **детерминированный** reader test дал `publication-before.log`: **15/16**, «Publishing recovery mutated bytes belonging to an existing reader». Он не использует гонку/тайминговый порог: старый stream обязан сохранять прежнее содержимое после атомарной замены. Запись не ускорялась отключением проверок и test не был переведён на чтение с бесконечными retry. Обрыв процесса/питания посередине Copy отдельно не симулировался; такой потенциальный риск не выдаётся за воспроизведённую потерю.

Первый вариант BETA-06 с одним File.Move(overwrite:true) **не прошёл** тот же test: Windows отказала в замене под открытым reader. Этот результат сохранён в `publication-after.log` (**15/16**). Использован File.Replace для существующего файла; `atomic-replace-after.log`: **16/16**, без ослабления assertions.

## 3. Сквозной workflow и проверка данных

`--beta-workflow`: четыре формы Grid/Grid/Canvas/Grid; Form1 Button-навигация/TextBox, Form2 DockPanel → StackPanel → Button/TextBox, Form3 абсолютный Canvas, Form4 nested StackPanel/стандартные items controls/DataGrid + BindingSource. В Grid-формах также есть независимые локальные источники данных; sample data включены **явно**, это не SQL/demo fallback.

Реальный MainWindow: Inspector text commit, 48 переключений форм, удаление поддерева, Undo/Redo, физический Save через host file API, New Project/dispose старой session, Open сохранённого JSON. Сравниваются полные сериализованные DTO всех форм: IDs, ParentId, порядок, placement, definitions, свойства, BindingSources, Logic и DataGrid settings. Затем открываются 8 native Preview windows (по обоим режимам для каждой формы), создаются folder export и ZIP.

Первый workflow test ошибочно присваивал только draft row.Value без UI commit; это ошибка harness, исправлена вызовом реального CommitPropertyGridTextEdit и не включена в bugs. Следующие провалы Delete/Redo уже отражали подтверждённые ошибки продукта; исходные логи сохранены. Пользовательская предварительная ручная проверка Grid-first учитывается отдельно от автоматизации этого аудита.

## 4. Матрица regression

Полные логи сохраняются отдельно для первого прогона каждой suite. Повторы не заменяют исходный результат.

| Suite / область | Результат |
|---|---|
| Baseline solution / Release runner | 0 errors; solution 1 прежнее NU1603, runner 0 warnings |
| Новые независимые regression checks | **16/16** после BETA-06; первая расширенная публикация была 15/16 до исправления |
| Native end-to-end, 4 формы | PASS: 48 switches, Inspector, subtree Delete/Undo/Redo, Save/New/Open, 8 Preview windows, folder/ZIP |
| Broad regression, первый restricted process | **69/72**, три Named Pipes UnauthorizedAccessException; не считается полностью зелёным |
| Три IPC tests, обычная Windows-сессия | **3/3**: edit/reload, старт/подключение VsHost, disconnect |
| Broad после BETA-01…05, обычная Windows-сессия | **72/72** |
| Последний broad после BETA-06 | **72/72**, `broad-atomic-final.log` |
| Native Grid Phase 2 | 28/28 |
| Grid Definition Editor | 45/45 |
| Grid Drag & Drop | 40/40 |
| Nested Layout | 59/59 |
| Routed pointer / Marquee | 26/26 |
| Layout validation / Zoom / properties | 55/55 |
| Phase 7A.2 / Phase 7A | 13/13; 15/15 |
| Undo history cache / branching / memory | 25/25 |
| Grid-first / Canvas backward compatibility | 26/26 |
| Preview Phase 2 | 28/28 |
| Preview Consistency | 32/32 |
| PostgreSQL offline | 20/20; live SKIP |
| Optional plugin cold start / missing runtime / missing theme | Три отдельные processes PASS; стандартный Designer/Preview/Export не требуют загрузки vendor assemblies |
| SQL Server DataGrid contract + live | **47/47** |
| SQL Settings → schema → BindingSource → native cells | PASS, отдельный source workflow |
| SQL exported standalone runtime | PASS, реальные Customers/Orders, Grid/nested Grid, resize |
| Preview memory с live SQL | **36/36** |
| Stabilization baseline / restricted repetition | 25/25; 25/25 |
| Stabilization, обычная Windows-сессия после BETA-01…05 | **24/25**: ordinary Window negative control, оба Preview tests PASS |
| Forensic Stabilization repetition | **23/25**: ordinary Window и пойманная гонка публикации autosave (BETA-06); повтор не заменяет предыдущий результат |
| Последний Stabilization после BETA-06, без forensic-паузы | **25/25**, ordinary Window=0 в этом процессе; это не исправление/отмена ранее подтверждённого UIA root |
| Inactive dirty form autosave после BETA-06 | **30/30** отдельных процессов; это повторы одного сценария, а не 30 новых независимых tests |

Полная native/SQL/Preview matrix выполнена после BETA-01…05. После последнего локального изменения публикации recovery (BETA-06) повторены solution/VSIX build, runner build, 16 recovery/identity/Undo checks, полный broad 72, Stabilization 25, 30 cold-process autosave repetitions, native end-to-end, folder runtime и ZIP runtime. Native layout/SQL/Preview production pipelines в BETA-06 не изменялись; их полная большая matrix второй раз после этой локальной I/O-правки не запускалась.

### Visual consistency и runtime

Список стандартных типов получен из registry: **Button, CheckBox, TextBox, DataGrid, TextBlock, Border, WrapPanel, Grid, StackPanel, Image, DropDownButton, ListBox, SplitButton, Group**. DockPanel представлен существующим Group.ChildLayoutMode=Dock. ComboBox source AXAML не выдаётся за пятнадцатый зарегистрированный JSON Toolbox control.

Три standalone generated applications: RegistryNative, RegistryCanvas, NestedNative. Restore/build/запуск, реальные native controls в visual tree; размеры client viewport 800×600, 1200×800, 1600×900. **27 сравнений сцена/viewport/режим, 492 наблюдения controls в четырёх режимах, 0 geometry mismatches**, max delta=0 DIP в этом Windows/theme окружении, tolerance=1.1 DIP. Проверяются тип/attached/visible/ParentId и Bounds. Flyouts и ListBox selection проверены runtime probe. Это не screenshot-only или поиск имён в AXAML. Bound DataGrid в Designer остаётся дизайнерским представлением; реальные native cells/значения доказываются обоими Preview и отдельным SQL runtime.

Четырёхформенный обычный exported runtime: **PASS** (`generated-runtime.txt`): Grid/Grid/Canvas/Grid, шесть navigation transitions, независимые DataContext для bound forms, реальные явно заданные локальные строки, Logic GridApply → GridInput, native DataGridRow, Grid resize и Canvas position 100/150. Canvas без bindings может законно иметь null DataContext. **ZIP распакован в новый каталог, restore/build/run повторены: тот же PASS** (`zip-runtime/generated-runtime.txt`). Все **16/16 source files ZIP побайтно равны folder export** после восстановления временной probe-инструментации. Проверка сгенерированных source/AXAML/csproj не нашла локальных путей C:/Users, D:/Проекты или оставшихся avares://FormDesigner asset URI (`export-integrity.json`).

Generated builds сохраняют прежние NETSDK1138 (экспортированный net6.0) и CS8618 DTO warnings; успешный build отдельно не выдаётся за доказательство UI. Целевые frameworks не обновлялись.

### Memory и оставшийся P2

Legacy/AXAML: по **20 последовательных Open/Close**, контроль каждые пять циклов, alive PreviewWindow=0, activeWindows=0, clockObservers=0. Отдельно NoDataGrid/Empty/Static/Multiple/MultiForm/Eremex, отмена Close, закрытие во время delay/реального SQL query, UIA/send-message lifecycle. Window, DataContext и ItemsSource проверяются WeakReference; assertions не ослаблялись. New Project ×20: старых document sessions живо **0**. Удаление DLL/metadata/cache и повторная загрузка plugin также покрыты broad/cold suites; 20 циклов всех произвольных пользовательских DLL/версий plugin не заявляются.

**Подтверждённый известный P2, не исправлен:** обычные Avalonia Window удерживаются в native UI Automation. В основном ordinary-проходе alive=6/6, в forensic-повторе alive=1/6. Использован локальный диагностический dotnet-dump 10.0.750501 (только artifacts, без изменения project NuGet). SOS обнаружил один точный Avalonia.Controls.Window и одну цепочку:

```text
ref counted COM handle (RefCount: 1)
 → Avalonia.Win32.Automation.RootAutomationNode
 → Avalonia.Automation.Peers.WindowAutomationPeer
 → Avalonia.Controls.Window
```

Доказательства: `memory-diagnostics/ready.json`, `window-stat.txt`, `window-address.txt`, `plain-window-gcroot.txt`. Это обычное окно negative control без DataGrid/Designer/Preview. Scope cleanup Preview уже отключает его собственный provider; оба Preview tests того же процесса дают alive=0. Win32 cleanup на все окна/Settings/Help здесь не расширялся: требуется отдельная проверка lifecycle/accessibility. Временный dump (~610 MiB) удалён после извлечения текстового evidence. Исторический native AV uiautomationcore.dll не воспроизводился, причинная связь с этим root не утверждается.

Удержание обычных Window не скрыто исключением отрицательного контроля. **24/25 и 23/25 остаются красными прогонами**, даже при успешных 36 Preview memory checks. Непосредственно связанная с File.Copy autosave гонка выделена в BETA-06; остальные исторические flaky наблюдения не объявляются автоматически исправленными.

## 5. Performance

**1350 steady-state измерений**, 45 комбинаций операция/N × 30 повторов; дополнительно 150 warm-up samples. Release, .NET 8.0.32, debugger=false, 16 logical processors, RenderScaling=1. Drop viewport=1200×800 DIP, Zoom=100%; native Designer window=1600×1000. Benchmarks не запускались параллельно с restore/build или другими UI suites. Время UI включает ожидание actual layout/первого compositor commit; это не измерение физического появления пикселя на мониторе.

Lifecycle benchmark: New/Open, смена selection, реальный RebuildPropertyGrid, AXAML Preview до загрузки/layout/commit (закрытие исключено из замера), folder export с записью файлов. Три warm-up итерации на N. Drop benchmark Phase 7A.2: четыре warm-up, actual routed Toolbox drop, существующий Move внутри WrapPanel и reparent между StackPanel; Undo между повторениями возвращает seed и проверяет ParentId/число controls. Нет накопления добавленных controls и нет жёстких timing assertions. N включает контейнеры; performance fixture состоит из Button и лёгких layout-контейнеров. Это не benchmark 500 DataGrid/Eremex.

В ячейках **median / P95, ms**:

| Операция | 20 | 50 | 100 | 300 | 500 |
|---|---:|---:|---:|---:|---:|
| New Project | 134.1 / 209.0 | 125.6 / 150.7 | 145.7 / 178.5 | 186.1 / 321.6 | 190.6 / 456.3 |
| Open Project | 126.6 / 221.8 | 163.9 / 212.3 | 243.0 / 352.3 | 546.3 / 765.1 | 892.0 / 1244.9 |
| Toolbox → Grid / Add | 141.0 / 266.3 | 139.8 / 189.3 | 140.9 / 183.3 | 199.6 / 272.5 | 293.8 / 763.1 |
| Move | 54.3 / 85.5 | 58.7 / 108.9 | 76.8 / 106.2 | 130.1 / 193.6 | 181.5 / 244.7 |
| Reparent | 114.3 / 152.0 | 120.1 / 187.3 | 137.0 / 169.1 | 216.9 / 266.6 | 236.7 / 307.9 |
| Selection | 118.4 / 182.6 | 134.8 / 182.2 | 180.1 / 262.4 | 341.4 / 602.9 | 498.4 / 850.3 |
| Inspector refresh | 80.4 / 133.1 | 82.8 / 131.2 | 84.7 / 199.1 | 91.8 / 200.0 | 92.1 / 136.6 |
| AXAML Preview | 320.8 / 386.0 | 364.2 / 396.6 | 447.5 / 531.9 | 729.4 / 849.3 | 1084.5 / 1463.6 |
| Export folder | 24.9 / 82.5 | 37.1 / 51.4 | 62.8 / 104.3 | 178.1 / 252.0 | 286.5 / 394.8 |

Все allocations и GC0/1/2 для 45 combinations находятся в `performance/summary-data.json`, `summary.md` и raw JSON. Lifecycle measurements используют total process allocations, Drop — UI-thread allocations; эти области не смешиваются. Например, Toolbox Grid 100/500: **30.02/80.69 MiB UI-thread median**; AXAML Preview 100/500: **58.20/250.46 MiB process median**. Для Grid 100 время handler median=68.65 ms, first commit=140.94 ms, input-ready=142.16 ms. Inspector 100: action=9.56 ms, full visible=84.70 ms; быстрый возврат метода не выдан за готовый UI.

Cold/first-operation samples сохранены отдельно от Steady. Холодный запуск всего приложения отдельным performance benchmark не измерялся. Это baseline **после UI/data стабилизации BETA-01…05**, а не paired before/after оценка ускорения safety fixes; исторические Phase 7A показатели нельзя причинно сравнивать с другой fixture/историей/окружением. Последняя BETA-06 меняет только recovery publication, которая в performance fixture не вызывалась; повторный benchmark ради этой I/O-правки не запускался.

**P2 backlog:** noticeable selection/layout latency, allocations и P95 на 300–500 controls, в особенности Grid Drop 500 (763 ms P95), Open 500 (1.245 s P95) и Preview 500 (1.464 s P95). На обычных 20–100 controls Move/Reparent и Drop существенно дешевле, но selection также имеет хвосты. Связь конкретного хвоста с GC/compositor здесь не объявляется доказанной; масштабный performance refactoring не выполнялся. Абсолютный Private Bytes процесса не использовался как критерий managed leak.

## 6. Изоляция БД и plugins

Создан отдельный LocalDB instance `FDBeta_20261104_130738`, база `FDBeta`, файлы внутри `artifacts/beta-readiness/localdb`. Fixture: Customers=3, Orders=2, EmptyTable=0. Существующие `MSSQLLocalDB` и `FormDesignerPreviewP0_20261030_183814` не использовались. Ограниченный процесс не получил доступ к LocalDB registry/credentials; создание выполнено в обычном процессе Windows. Ошибка окружения не считается дефектом продукта.

Оба Preview проверяют реальные native DataGridRow/cell values, а не только ItemsSource.Count. Есть unbound/empty, статические данные, aliases, несколько grids/forms, ошибки запроса/соединения без demo fallback, cancellation/timeout. После проверок **FDBeta и owned instance удалены**, MDF/LDF=0, исходные пользовательские instance names сохранены (`localdb/cleanup.log`, `instances-before.txt`, `instances-after.txt`).

Live PostgreSQL не проверяется без отдельного тестового сервера. Сервер не устанавливался. Offline tests не заменяют live acceptance.

## 7. Ограничения и ручная приёмка

* Physical Toolbox/system DragDrop, Alt+Tab/release вне окна, реальные DPI 125/150/200% и перенос между мониторами требуют ручной приёмки. Routed events и coordinate-transform tests не считаются физической мышью.
* Интерактивный Visual Studio experimental instance не проверяется автоматически. Packaging, реальный VsHost process и Named Pipes проверяются отдельно.
* Произвольный импортированный AXAML не имеет обещания source-faithful Preview всего приложения. Minimal-patch ограничения, opaque nodes и явная диагностика сохраняются; JSON Preview default на import не распространяется.
* Eremex экспериментален и необязателен; vendor integration не является условием работы стандартных controls.
* Live PostgreSQL — отдельный acceptance blocker для выпуска с безусловно заявленной поддержкой PostgreSQL. Для ограниченного Beta scope это должно быть явно обозначено как непроверенная интеграция.
* Повреждённый recovery, который невозможно распарсить, сохраняется для ручного восстановления/копирования. До устранения причины/явного удаления файла autosave нового документа приостановлен; обычный Save JSON доступен. Отдельный UI безопасного quarantine/discard непарсируемого файла в этом этапе не добавлялся.
* Отдельного stress run на 500 тяжёлых DataGrid/Eremex, произвольных пользовательских DLL и множестве версий plugin не было. Большие performance fixtures используют Button/layout containers. Native root retention обычных окон и исторический UIA crash требуют отдельной platform investigation.

Ручной acceptance checklist:

1. На реальном рабочем месте повторить Toolbox system DragDrop, Escape/Alt+Tab/capture loss/release вне окна для Grid/Canvas/nested containers; Undo/Redo и Inspector после каждого Drop.
2. DPI 100/125/150/200%, Zoom 50/75/100/125/150/200%, scroll и перенос между физическими мониторами; особенно selection/handles/cell highlights на 125%.
3. Открыть собственный старый Canvas и сложный JSON, Save/Open, оба Preview, сравнить client area и resize; явно выбрать Legacy и проверить сохранение настройки.
4. Visual Studio experimental hive: команда Tools, несколько AXAML buffers, Apply/ACK, внешний конфликт, close/reopen VsHost. Автоматические IPC/packaging проверки не заменяют этот пункт.
5. Провести live PostgreSQL acceptance на отдельном test server: schema, quoted identifiers, empty/error, несколько forms, Preview/Export/runtime и cancellation. До этого не заявлять live поддержку проверенной.
6. Проверить отложенный/заблокированный recovery в пользовательском UI: сохранность исходника, понятный paused status, Restore/Delete и возобновление autosave; текущую новую работу явно сохранить в JSON.

## 8. Изменённые файлы

Production: `Services/AutosaveRecoveryService.cs`, `ViewModels/MainWindowViewModel.DocumentPersistence.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/MainWindowViewModel.PostgreSql.cs`, `Views/MainWindow.axaml.cs`.

Tests: dispatch в `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`; новые `Program.BetaReadiness.cs`, `Program.BetaWorkflow.cs`, `Program.BetaPerformance.cs`, `smoke-tests/BetaReadiness/SummarizePerformance.py`; только переопределение output directory через environment в `Program.GridFirst.Benchmark.cs`, `Program.PreviewPhase2.cs`, `Program.DataGridPreviewRegression.cs`, `Program.DataGridPreviewExport.cs`. Assertions существующих suites не ослаблялись.

`Program.Stabilization.cs`: один test-only opt-in forensic checkpoint перед **неизменённым** ordinary Window assertion. Он активен только при FORMDESIGNER_BETA_HOLD_NEGATIVE_CONTROL=1 и не вызывается в production; ожидает снятие dump/release, затем исходная проверка всё равно выполняется.

Этот отчёт: `Docs/BetaReadinessAudit.md`.

### Evidence и воспроизводимость

Основные команды (Release runner предварительно собирается `dotnet build smoke-tests/FormDesigner.ExportSmokeTests -c Release --no-restore -m:1`):

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --beta-readiness-checks
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --beta-workflow
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --stabilization-checks
py -3 smoke-tests/BetaReadiness/SummarizePerformance.py artifacts/beta-readiness/performance
```

Live suites требуют **нового изолированного** test instance и FORMDESIGNER_TEST_SQLSERVER; использованная база уже удалена. Для специальных suites доступны output overrides FORMDESIGNER_GRID_FIRST_ARTIFACTS, FORMDESIGNER_PREVIEW2_ARTIFACTS, FORMDESIGNER_CONSISTENCY_ARTIFACTS, FORMDESIGNER_DATAGRID_ARTIFACTS. Для raw Drop benchmark сохранены DROP_BENCH_COUNT/SCENARIO/SAMPLES/LABEL/OUTPUT_DIRECTORY в task script и result metadata.

Evidence: baseline/final/atomic build logs; first и final suite logs; `final-results.json`; `broad-atomic-final.log`; `atomic-replace-after.log`; `stabilization-atomic-final.log`; `autosave-repeat-results.json`; `workflow-atomic-final.log`; folder/ZIP source и runtime results; `consistency-final/visual-tree.csv`, `geometry-comparison.json`; performance raw JSON и summary; text GC-root evidence. `final-results.json` относится к большой matrix до BETA-06; поздние recovery/broad/ZIP прогоны имеют отдельные явно названные логи и не подменяют его.

После завершения всех процессов удалены только owned generated bin/obj, plugin runtime copies, диагностический tool directory и временный dump. **27 каталогов, 0 cleanup failures**, оставшийся evidence **9.46 MiB** вместо 1013.53 MiB (размер dump удалён раньше и в эту сумму не входит). Исходники generated apps, ZIP, JSON/CSV и логи сохранены. Повторный запуск standalone probes требует обычного restore/build. Предыдущие audit artifacts, пользовательские данные и Git history не очищались.

## 9. Решение

**BETA READY WITH CONDITIONS** для ограниченной Windows Beta существующего JSON Designer со стандартными Avalonia controls, Grid/Canvas, обоими Preview и Export. **Шесть новых подтверждённых ошибок исправлены (3 P0 + 3 P1); открытых подтверждённых P0/P1 из этого аудита не осталось.** Это не утверждение отсутствия любых неизвестных ошибок.

Условия выпуска: завершить physical UI/DPI/interactive VS acceptance; явно определить scope PostgreSQL (live остаётся blocker для полной заявленной поддержки), arbitrary AXAML Preview и experimental Eremex; принять/отдельно исправить известный ordinary Window/UIA P2 и performance tails больших форм. Нельзя объявлять всю regression неизменно зелёной: сохранены 69/72, 24/25, 23/25 и development red/green этапы, даже при последних 72/72 и 25/25.

Следующий этап — ограниченная ручная Beta acceptance и отдельная platform/UIA investigation обычных окон, затем focused performance work по measured P2. Layout/Preview architecture не переписывать ради этих выводов. Framework/support-policy (включая generated net6.0 warning) требует отдельного решения, без автоматического обновления в этом этапе.

Версия Alpha 4.0 сохранена. Beta release, Git tag и следующая Layout phase **не создавались**.
