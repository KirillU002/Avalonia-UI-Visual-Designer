# Preview: расследование и устранение удержания закрытых окон

Дата: 31.10.2026. Исходный commit: `0f807cc`. Предыдущий аудит: [StabilizationAudit.md](StabilizationAudit.md), STAB-06. Его незакоммиченные изменения сохранены. Пакеты, TargetFramework, Export, SQL/PostgreSQL и VSIX/IPC в этом исправлении не изменялись.

## 1. Воспроизведение и метод

Два исходных memory checks аудита воспроизводили удержание шести закрытых PreviewWindow в каждом режиме. Их проверки не ослаблялись. Это реальные native Avalonia Windows с Measure/Arrange, DataGrid templates и обработкой Dispatcher, а не модельные mock-окна.

Использован локально установленный `dotnet-dump 10.0.750501`, SOS `dumpheap`, `gcroot`, `dumpobj`, `dumparray`, WeakReference и диагностический runner. После закрытия ожидалась обработка очередей, затем **только тест** запускал полный GC. В production GC.Collect отсутствует. Рабочий набор процесса и возврат памяти ОС не использовались как критерий.

Сначала исследован dump из ограниченного процесса, затем независимый dump обычной Windows-сессии с активным внешним UI Automation клиентом. Они выявили **два разных долгоживущих root**, а не проблему DataGrid.ItemsSource. После устранения первого root второй оставался виден только при публикации native UIA provider. Это объясняет различие результатов между окружениями.

## 2. Root 1: бесконечная анимация loading overlay

Подтверждённая SOS цепочка в обоих режимах:

```text
strong GC handle
 → AvaloniaSynchronizationContext → Dispatcher → DispatcherPriorityQueue
 → DispatcherOperation → DispatcherTimer → MediaContext
 → MediaContextClock._observers → Clock → ClockObservable
 → AnimationInstance<double> → DoubleAnimator → AnimatorKeyFrame
 → ValueStore → BindingExpression → LogicalAncestorElementNode
 → ControlLocator.ControlTracker
 → Border "IndeterminateProgressBarIndicator" (TemplatedParent = ProgressBar)
 → composition visual / ServerCompositionTarget
 → TopLevel render delegate closure → PreviewWindow
```

В исходном AXAML индикатор имел постоянное `IsIndeterminate="True"`. `HideLoading()` скрывал его родителя, но не завершал бесконечную анимацию template. Глобальный media clock удерживал indicator и через composition target всё окно. В dump шести окон было 12 clock observers. Подтверждено также удержание Preview **без DataGrid**: по 3/3 окна в каждом режиме. Связь с DataGrid была корреляцией сценария, а не причиной утечки.

Исправление: индикатор изначально остановлен; `ShowLoading()` включает анимацию только на время загрузки; `HideLoading()` и `Closed` выключают `IsIndeterminate`. Это завершение конкретного ресурса, принадлежащего loading session. DataContext, коллекции и bindings не очищаются ради прохождения GC test.

После одного этого изменения два исходных tests проходили в ограниченной сессии; повторный SOS dump показывал 0 PreviewWindow, 0 DataGridPreviewContext и 0 AnimationInstance. В обычной Windows-сессии ещё сохранялся root 2.

## 3. Root 2: native UI Automation provider

Независимая SOS цепочка (ref-counted COM handle, RefCount=1):

```text
native COM/CCW handle
 → Avalonia.Win32.Automation.RootAutomationNode
 → WindowAutomationPeer
 → PreviewWindow
```

В этой цепочке нет DataGrid и animation clock. `AutomationNode.s_nodes` — ConditionalWeakTable и сам по себе не является сильным root; native COM reference удерживает provider, а provider — peer/окно. Утечка воспроизведена в обычной Windows-сессии на шести закрытых окнах.

В закреплённой Avalonia **11.1.1.0** обработчик WM_DESTROY сначала вызывает `Closed`, затем `UiaReturnRawElementProvider(..., null)`, но не отключает опубликованный provider через `UiaDisconnectProvider`. После `Closed` peer уже теряет platform implementation: попытка отключения возвращала **E_FAIL (0x80004005)** и на UI-, и на MTA-потоке. Тот же provider до уничтожения HWND возвращал S_OK. Простое перенесение вызова в Task.Run не исправляет потерянную host identity.

Использован публичный `Win32Properties.AddWndProcHookCallback` **только для PreviewWindow**. На WM_DESTROY, до штатного Avalonia handler и обнуления platform implementation, отключается уже существующий provider именно этого окна. На время destruction reentrant WM_GETOBJECT не публикует его повторно. Hook не перехватывает обычные запросы accessibility живого окна и не отключает providers других окон.

Поиск существующего provider из peer использует небольшой compatibility adapter `PreviewAutomationLifetime` для private Win32 API 11.1.1. Новые peer/provider ради cleanup не создаются. Кэш содержит лишь reflection metadata. Версия проверяется явно; при будущем обновлении backend адаптер необходимо пересмотреть. Ошибка cleanup диагностируется с типом и HRESULT.

Microsoft запрещает outbound UIA disconnect внутри синхронного SendMessage. Для такого закрытия `OnClosing` откладывает `Close()` в Dispatcher до выхода из Windows message, не блокируя UI. Обычное закрытие остаётся синхронным. Отменённое Closing не уничтожает HWND и не отключает provider. Cleanup на WM_DESTROY также защищает owned Preview от преждевременного отключения, если последующий Closing владельца отменит закрытие.

Первичные источники: [Microsoft UiaDisconnectProvider](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider), [Avalonia 11.1.1 WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/WindowImpl.AppWndProc.cs), [AutomationNode](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/Automation/AutomationNode.cs), [RootAutomationNode](https://github.com/AvaloniaUI/Avalonia/blob/11.1.1/src/Windows/Avalonia.Win32/Automation/RootAutomationNode.cs).

## 4. Жизненный цикл и загрузка

- Создание: уникальный PreviewId, режим Legacy/AXAML, bounded журнал из 64 value records. Статический счётчик хранит число окон, а не ссылки на них.
- Open: увеличивается active count, начинается асинхронная загрузка. Legacy строит control tree; AXAML загружает экспортированную разметку и runtime context. Оболочка/loading overlay у них общие, поэтому оба root одинаковы.
- Запросы: сохраняется существующий provider-specific pipeline и cancellation. После Close отменяется CTS, останавливается render timer; поздние finally не изменяют loading UI закрытого окна. Незавершённый контрактный provider в тесте не удерживает окно; его позднее завершение не восстанавливает UI.
- Close: завершаются принадлежащая окну animation и native provider, затем active count уменьшается один раз. `MainWindow._launchPreviewWindow` уже очищался штатным Closed handler; дополнительно это проверено при живом owner. MainWindowViewModel не содержит обнаруженного root.
- DISPOSED означает окончание отслеживаемых session operations; это не событие финализатора и не заявление о запуске GC. После этого lifecycle logger отписывается от окна.

Освобождаются сами PreviewWindow, runtime DataContext, DataGridPreviewContext, DataGrid и созданные для Preview ItemsSource. Исходные пользовательские BindingSource/данные документа не изменяются. Ссылки внутри закрытого дерева не зануляются массово; после устранения внешних roots циклический граф собирает CLR.

Добавлены события `PREVIEW_LIFECYCLE_CREATED`, `OPENED`, `CLOSING`, `CLOSED`, `DISPOSED` (с полным префиксом PREVIEW_LIFECYCLE_), `PREVIEW_DATA_LOAD_CANCELLED`, `PREVIEW_GC_ROOT_IDENTIFIED`; дополнительно RELEASED/RELEASE_FAILED для automation. Запись содержит PreviewId, Mode, ActiveWindows, PendingActivities и Reason. GC_ROOT_IDENTIFIED — однократное описание установленного SOS факта; production heap profiling не запускается. Пароли/connection strings в lifecycle diagnostics не передаются.

## 5. Regression tests

Новый `--preview-memory-checks` проверяет оба режима с настоящими показанными окнами:

- без DataGrid; пустой DataGrid; явные локальные строки; несколько DataGrid;
- reopen и 20 последовательных Open/Close с checkpoints 5/10/15/20;
- несколько форм и Eremex DataGridControl;
- закрытие во время initial delay, незавершённого async provider и SQL-запроса под реальной блокировкой таблицы;
- живой MainWindow owner, отменённый Close и native cross-thread WM_CLOSE;
- LocalDB с реальными строками, пустой таблицей и несколькими DataGrid. Проверяется и видимое значение «Анна SQL» в ячейке.

WeakReference отслеживает окно, runtime contexts/DataContext, DataGrid и ItemsSource, а не только количество вызовов Close. Исключены только процессные singleton `Array.Empty`; mutable empty collections проверяются. По value diagnostics проверяются однократные события и active count=0. Число subscriptions media clock должно вернуться к исходному, callback/lifetime записи не должны накапливаться. Исходные два audit tests сохранены без ослабления.

Интеграция SQL использует созданную для этой задачи отдельную LocalDB instance `FormDesignerPreviewMemory_20261031_152702`, базу `FormDesignerPreviewMemory`, Windows authentication и fixture Customers/Orders/EmptyTable. Production и пользовательские БД не использовались. Секретов в connection profile нет.

Для диагностики доступны `--preview-memory-dump` (пишет PID/Alive в `artifacts/preview-memory/dump-ready.json`, ожидает `release-dump.txt`, максимум 5 минут) и opt-in `FORMDESIGNER_MEMORY_DUMP_ON_RETAINED=1` (при провале новой memory-проверки снимает heap до продолжения теста). Это только smoke runner. Для owned fixture можно задать `FORMDESIGNER_MEMORY_DUMP_OWNED=1`. Локальный dotnet-dump должен быть в `artifacts/preview-memory/tools`; он не включён в зависимости приложения.

## 6. Результаты

| Проверка | Результат |
| --- | --- |
| Solution, включая VSIX/VsHost | **0 ошибок**, 1 прежнее NU1603 (VS SDK analyzers 17.7.20 → 17.7.22) |
| Smoke runner build | **0 ошибок, 0 предупреждений** |
| Два исходных красных tests | **2/2**, Legacy 0/6 и AXAML 0/6 живых окон после Close + idle + GC, в обоих окружениях |
| Полный исходный audit, restricted context baseline | **25/25**, ожидания исходных tests не менялись |
| Полный audit с внешним Windows UIA | **24/25**: оба Preview tests проходят; красный `ClosedPlainWindowNegativeControl` — обычные Avalonia Window без Preview-specific cleanup |
| Grid Drop | **40/40**, включая routed pointer events, zoom/DPI transforms и Eremex |
| Grid Editor | **45/45** |
| Native Grid | **28/28** |
| Pointer/Marquee | **26/26** |
| DataGrid Preview | **43/43**: 18 contracts и 25 live SQL, оба Preview, реальные видимые cells, empty/error/cancellation/timeout/reopen/multi-form |
| PostgreSQL offline | **20/20**, live PostgreSQL явно skipped |
| Финальный SOS heap, обычная Windows-сессия | После 12 закрытых owned Preview **0 PreviewWindow**, **0 AnimationInstance**, **0 RootAutomationNode**, **0 DataGridPreviewContext** |
| Новый полный memory runner, обычная Windows-сессия | **36/36**, включая 8 live LocalDB cases, оба режима, 20 cycles и native WM_CLOSE |
| Exported runtime с LocalDB | **PASS**, отдельный процесс, restore/build/run, Customers=3 / Orders=2, реальные видимые значения в двух формах и независимые ViewModels |
| Широкая существующая regression-выборка | Первый прогон **71/72**; `ToolboxStandardItemsExport` упал на primary SplitButton Logic в Legacy. Изолированный повтор **1/1** прошёл с прежними ожиданиями и без изменений кода; весь первый прогон не объявляется зелёным |

В новом расширенном прогоне первоначально **35/36**: один Legacy `CancelledOwnerClose` удерживал окно на checkpoint 1 секунда + GC. Три независимых последующих прогона этого сценария прошли **6/6**, а owned dump probe показал 0/12. Точный root этого единичного наблюдения не установлен; оно не скрыто и не приписано без доказательств UIA timing. Повторный полный прогон с автоматическим dump при удержании прошёл **36/36**; дополнительного retained dump не возникло. Промежуточные красные логи сохранены отдельно от итоговых.

Двадцать последовательных циклов каждого режима: на checkpoints 5/10/15/20 **0 живых окон**, clock observers **0 → 0**; освобождены по 40 захваченных context references и 20 ItemsSource за серию. Проверки не требуют уменьшения working set процесса.

Сводные текстовые доказательства: `artifacts/preview-memory/gcroot-legacy.log`, `gcroot-axaml.log`, `animation-owner.log`, `clock-observers-before.log`, `gcroot-elevated.log`, `heap-final.log`, `final-memory-repeat.log`, `final-audit*.log`, `regression-*.log`, `broad-regression.log`, `toolbox-rerun.log`, `generated-runtime.log`. Raw dumps не нужны для запуска приложения.

Повторный запуск:

```powershell
dotnet build .\FormDesigner.sln --no-restore -m:1
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --preview-memory-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --stabilization-checks
```

Для live SQL задать `FORMDESIGNER_TEST_SQLSERVER` на изолированную тестовую БД с fixture `artifacts/datagrid-preview-p0/fixture.sql`; без неё runner явно пропускает live cases. Native UI Automation coverage зависит от наличия UIA client; проверены restricted и обычная desktop sessions. Отрицательный контроль обычного Window намеренно не исправлялся Preview workaround и не ослаблялся ради 25/25 в UIA-сессии.

После проверок собственная LocalDB instance остановлена/удалена; созданные ею MDF/LDF и instance logs удалены после проверки точных путей и времени создания. Четыре heap dumps (~714 МБ), локальный diagnostic CLI и два созданных этим прогоном каталога generated outputs очищены. Сохранены небольшие текстовые SOS/runner доказательства и fixture scripts. Для нового profiling прогона dotnet-dump нужно установить заново; для live SQL создать новую изолированную fixture.

## 7. Изменённые файлы этого исправления

| Файл | Назначение |
| --- | --- |
| `Views/PreviewWindow.axaml` | Именованный остановленный по умолчанию loading progress |
| `Views/PreviewWindow.axaml.cs` | Управление анимацией, режим lifecycle, tracking async operations, cancellation diagnostic |
| `Views/PreviewWindow.Lifecycle.cs` | Жизненный цикл, active counter, targeted Win32 destruction cleanup |
| `Services/PreviewAutomationLifetime.cs` | Версионированный адаптер отключения существующего UIA provider |
| `Models/PreviewLifecycleDiagnostic.cs` | Структурированная запись lifecycle |
| `Views/MainWindow.axaml.cs` | Передача lifecycle в существующий log и unsubscribe |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` | Dispatch memory checks/dump probe |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.PreviewMemory.cs` | WeakReference, clock, native UI, LocalDB и multi-cycle tests |
| `Docs/PreviewMemoryLeakFix.md` | Этот отчёт |

Остальные dirty persistence/save файлы относятся к предыдущему аудиту. Они не перезаписаны и не выдаются за изменения memory fix.

## 8. Ограничения проверки

Live PostgreSQL для воспроизведения не нужен и не запускался. Linux/macOS и альтернативные Win32 backend versions не проверены. Нет утверждения о любых сторонних UIA clients, нескольких мониторах, интерактивной Visual Studio и длительном многочасовом soak. Compatibility adapter зависит от внутреннего API конкретной Avalonia 11.1.1; его нельзя переносить на другую версию без проверки. Двадцать циклов и SOS подтверждают отсутствие найденных roots в проверенных сценариях, а не отсутствие любых возможных утечек приложения.

Остались для отдельного наблюдения: единичный неповторившийся owned Preview checkpoint и нестабильный SplitButton smoke. Они не объявлены исправленными дефектами и не стали поводом менять Toolbox/Logic в memory fix. Обычные Avalonia Window вне Preview всё ещё могут удерживаться native UIA в этой версии backend; отрицательный контроль выявляет это ограничение. Следующий узкий diagnostic проход имеет смысл при устойчивом повторении с dump, а не с ослаблением assertions.

Новые Layout phases не начинались.
