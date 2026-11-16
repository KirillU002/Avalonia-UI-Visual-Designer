# Universal metadata regression

Команды выполняются из корня репозитория, с доступным .NET SDK и обычным Windows UI session.

```powershell
dotnet build smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj -c Release
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-metadata-checks
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase1/generated
```

Checks создают native Designer/Legacy/AXAML Preview, проверяют Inspector, routed Toolbox events, JSON/Undo/Multi Form, source patches, collectible Type cache и Preview WeakReferences. Coverage CSV/JSON записываются в `Docs`; fixtures и логи — в игнорируемые `artifacts/universal-phase1`.

Probe восстанавливает/собирает экспортированный проект и запускает настоящее приложение с временной runtime-проверкой. В `finally` исходный startup восстанавливается, probe удаляется, проект пересобирается. Для проб нужен PowerShell 7. Успешный build сам по себе не считается native runtime PASS.

После завершения builds и других native suites можно отдельно измерить cold metadata lookup и 30 Inspector switches после трёх warm-up:

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-metadata-profile
```

Результат — `artifacts/universal-phase1/metadata-profile.json`. Измеряется синхронный selection/Inspector/layout, не compositor latency. Timing thresholds в функциональных тестах отсутствуют. GC используется только внутри тестов collectibility; production принудительный GC не вызывает.

Для отдельных checks доступен временный environment filter `METADATA_TEST_FILTER`; перед полным прогоном его необходимо удалить. Он не является пользовательской настройкой приложения. SQL live проверяется отдельными существующими DataGrid suites на специально выделенной тестовой БД; этот harness не подключается к пользовательским базам автоматически.

## Universal Controls Phase 2

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-controls2-checks
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase2/exports/Grid -ProbeFile Phase2RuntimeProbe.cs.txt -ResultName Grid-runtime.txt
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase2/exports/Canvas -ProbeFile Phase2RuntimeProbe.cs.txt -ResultName Canvas-runtime.txt
```

Suite проверяет RadioButton, NumericUpDown, Calendar, DatePicker, TimePicker, Separator: настоящий native visual tree, typed properties, spinner/Calendar keyboard/picker popups, routed Toolbox/pointer, source-preserving patches, JSON/Undo/Multi Form, layouts и collectibility. Проверяется также отсутствие смещения от theme Margin в legacy Canvas. Generated runtime запускается отдельно командами выше; только наличие AXAML не считается PASS. Фильтр одного сценария — `UNIVERSAL2_FILTER`; перед полным прогоном удалить.

После завершения build/native suites:

```powershell
$env:UNIVERSAL2_PROFILE_LABEL='comparison'
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-controls2-profile
$env:UNIVERSAL2_PROFILE_SCENE='new'
$env:UNIVERSAL2_PROFILE_LABEL='new-controls'
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-controls2-profile
Remove-Item Env:UNIVERSAL2_PROFILE_SCENE, Env:UNIVERSAL2_PROFILE_LABEL
```

По умолчанию профиль сохраняет прежнюю Phase 1 сцену для сравнения: три warm-up и 30 переключений. `new` — отдельная сцена с шестью новыми controls, шесть warm-up и 30 измерений. JSON содержит median/P95, allocations, GC, counts/stages и размер native Inspector tree. Измеряется синхронный selection/Inspector/layout; compositor/физическая мышь не измеряются. Производительность новых шести controls не сравнивается напрямую с исходными тремя.

## Universal PropertyGrid Phase 3A

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --propertygrid3a-checks
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase3a/exports/Grid -ProbeFile Phase3ARuntimeProbe.cs.txt -ResultName Grid-runtime.txt
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase3a/exports/Canvas -ProbeFile Phase3ARuntimeProbe.cs.txt -ResultName Canvas-runtime.txt
```

Проверяются native типизированные значения и редакторы, Value Source, Reset с удалением локального значения, минимальные source patches, защищённые Binding/Resource/Gradient, атомарный Undo/Redo, JSON, Multi Form, stale Row callbacks, повторный ввод после Enter и WeakReference на старые Row/session. Фильтр — `PROPERTYGRID3A_FILTER`; для полного прогона убрать. Runtime probes проверяют реальный visual tree, decimal/spinner, порядок Thickness/CornerRadius, brush alpha, fonts, GridLength units и resize. SQL/Preview memory/VSIX проверяются отдельными существующими suites.

Benchmark запускать последовательно, после завершения builds и других native suites:

```powershell
$env:PROPERTYGRID3A_SCENE='representative'
$env:PROPERTYGRID3A_LABEL='representative-after'
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --propertygrid3a-profile
$env:FORMDESIGNER_PROPERTYGRID_REUSE='0'
$env:PROPERTYGRID3A_LABEL='representative-disabled'
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --propertygrid3a-profile
Remove-Item Env:FORMDESIGNER_PROPERTYGRID_REUSE, Env:PROPERTYGRID3A_SCENE, Env:PROPERTYGRID3A_LABEL
```

Сцена `representative` содержит семь типов и 30 измерений каждого после трёх полных warm-up проходов. `original` — прежняя сцена, 30 измерений. Результат сохраняется в `artifacts/universal-phase3a/<label>.json`: selection, layout, callback compositor, allocations текущего UI thread, GC, профили этапов и проверка актуального native editor context. Callback compositor не означает физическое появление пикселей на мониторе. Cold setup всего окна отделён от steady-state. `FORMDESIGNER_PROPERTYGRID_REUSE=0` — диагностический rollback presentation reuse, не пользовательская настройка и не отключение typed editors/safety. Жёстких временных CI-порогов нет.

## Collections Phase 3B

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --collections3b-checks
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase3b/exports/Grid -ProbeFile Phase3BRuntimeProbe.cs.txt -ResultName Grid-runtime.txt
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/universal-phase3b/exports/Canvas -ProbeFile Phase3BRuntimeProbe.cs.txt -ResultName Canvas-runtime.txt
```

Checks проверяют metadata/capability guards, native routed Toolbox/editor input, atomic Undo/Redo/selection, nested remove/Logic/rename, JSON/Multi Form/Explorer, версии/повреждённые данные/cycles/depth, readonly imported structure/no-op patches, настоящие коллекции обоих Preview, popup/selection/tab/tree interaction, освобождение session при живом закрытом Window и 20 циклов editor open/close. Фильтр одного check — `COLLECTION3B_FILTER`; перед полной матрицей удалить. Дополнительные native/UIA failures сохранять вместе с exit code и Windows Event Log, повторный PASS не отменяет первый red/crash.

После builds/native suites отдельно:

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --collections3b-profile
$env:UNIVERSAL2_PROFILE_LABEL='phase3b-after'
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --universal-controls2-profile
Remove-Item Env:UNIVERSAL2_PROFILE_LABEL
```

Collection profile: 3 warm-up, 30 observations каждого сценария, начальные размеры 1/10/100 items, Open/Add/Reorder/Selection, NestedAdd при 100 и последовательности 10/100 отдельных Add при начальном размере 1. AddSequence сохраняет обычные отдельные Undo actions, а не выдаёт batch за одну операцию. Измеряются native editor/layout/compositor callback, UI-thread allocations, process-wide GC и stages; JSON — `artifacts/universal-phase3b/collection-performance.json`. Warm-up observations помечены отрицательным Iteration. Количество native property editors не растёт до числа всех коллекционных элементов; fields создаются только для selection. Timing thresholds в CI отсутствуют. Runtime probes запускают реальные экспортированные приложения, затем восстанавливают startup и удаляют probe.

## Universal Controls Phase 4A

Production versions/TFM не меняются. После Release build runner поддерживает:

* `--universal4a-checks` — 38 source/default/null/namespace/readonly/nested/native/memory checks. С Phase 4B imported Preview проверяет реальные source Padding/FontFamily; opaque values по-прежнему требуют предупреждения.
* `--universal4a-inventory` — исходный `Views/MainWindow.axaml`, metadata/source capabilities/property elements/collections. Node coverage не является property-editor coverage.
* `--universal4a-inspector-audit` — выбирает каждый imported control в прежнем PropertyGrid, сопоставляет X/Y с Canvas.Left/Top, считает реальные editable rows и проверяет no-op source после selection.
* `--universal4a-profile` — 3 warm-up + 30 measured import/patch для 10/100 NumericUpDown и MainWindow; parse/source-map stages, thread allocations, GC, managed/working-set snapshots.
* `--universal4a-ui-profile` — 20/100 controls, 3 warm-up + 30 selection/edit/patch/Undo/Redo; synchronous action + UpdateLayout, не first physical paint. История — один pending change, не H30.

Для inventory/profile/audit задать `UNIVERSAL4A_OUTPUT` (путь JSON); использовать отдельное имя для каждого прогона. Пример локального process-only capture (PowerShell из корня repository):

```powershell
$runner=(Resolve-Path smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll).Path
$capture=(Resolve-Path smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll).Path
$output=Join-Path (Get-Location) ('artifacts/phase4a-'+[guid]::NewGuid().ToString('N'))
$env:UNIVERSAL4A_OUTPUT=$output+'.json'
dotnet $capture ($output+'-capture') (Get-Command dotnet).Source -- $runner --universal4a-inventory
```

Capture output должен быть новым; при ненулевом exit/Captured/CaptureFailed остановиться и исследовать evidence. Не удалять старый capture и не считать повторный PASS исправлением native AV. Проверки Named Pipes и LocalDB требуют поддерживаемых normal permissions; sandbox AccessDenied не исправляется изменением IPC.

`Universal4APropertyElementIpc` — отдельный scenario общего smoke runner: property-element text edit, attribute addition, nested Header, version/checksum, Apply/ACK и no phantom patch. Можно выбрать через обычный `SMOKE_SCENARIO_NAMES_FILE`.

`--universal4a-generated` требует новый `UNIVERSAL4A_GENERATED_DIR`: создаёт существующий generated project и помещает в него patched AXAML fixture с техническим x:Class. Затем:

```powershell
pwsh -NoProfile -File smoke-tests/UniversalMetadata/ProbeRuntime.ps1 -ProjectPath artifacts/phase4a-generated -ProbeFile Phase4ARuntimeProbe.cs.txt -ResultName phase4a-runtime.txt
```

Probe проверяет настоящие Button values, Menu/nested/Separator, ComboBox popup/selection, Tab content/switch, Tree hierarchy и три resize. Это проверка patched source, не доказательство воспроизведения произвольного AXAML модельным Preview.

Для baseline сравнения использовать отдельный git archive исходного commit с теми же profile methods/fixtures. Полный текущий checks file зависит от новых production capabilities и не переносится в старый checkout целиком. Не запускать builds, SQL, другие UI suites параллельно с benchmark; debugger mode и функциональный объём должны быть явно указаны. Raw timing не является pass/fail threshold. Итоги и ограничения: [Phase 4A](../../Docs/UniversalControlsAndPropertiesPhase4A.md).

## Universal Controls Phase 4B

* `--universal4b-checks` — native fidelity 21 типов, геометрия Login/Nested при трёх размерах (1 DIP, одинаковый client area), existing fixtures, SourceMap/report integrity, imported Preview/ACK, native collections и 20+20 закрытий Preview/документа с WeakReference на source map. Закрытые окна специально удерживаются; освобождение metadata не зависит от GC Window/UIA.
* `--universal4b-profile` — 5 warm-up + 30 import/real patch для Small/Medium/MainWindow. `UNIVERSAL4B_OUTPUT` задаёт новый JSON; `UNIVERSAL4B_STAGES=0` отключает детальную instrumentation при измерении общей скорости. Без этой переменной — duration/bytes/GC/calls отдельных стадий. Вложенные стадии нельзя суммировать.
* `--universal4b-interactions` — 5 warm-up + 100 selection/edit/patch/Undo/Redo по N0…N99, затем 5 warm-up + 30 Initialization/Preview. Export snapshot и loading overlay входят в Preview latency; UI action включает UpdateLayout, а не время физического показа кадра. Один pending edit, без накопленной H30 истории.
* `--universal4b-large-stages` — 5 warm-up + 30 ApplyDocument/Inspector initialization, native Measure/Arrange и 100 индексированных SourceMap lookup на настоящем MainWindow. Off-screen projection — тот же метод, которым пользуется DesignerSurface; это не benchmark полного физического показа 1502 wrappers.
* `--universal4b-ack-profile` — 5 warm-up + 100 последовательных Edit/Patch/core ACK с настоящим rebase source baseline, без IPC/физического render. Проверяет сохранение ControlId и отсутствие phantom patch после каждого ACK; результат в `UNIVERSAL4B_OUTPUT`.

Используйте тот же process-local `NativeCapture`, что в примере Phase 4A; для длинного профиля `UIA_CAPTURE_TIMEOUT_SECONDS=1800`. Не перезаписывайте результаты и не запускайте benchmarks параллельно с тестами/сборками. Before/After используют один harness, Avalonia/.NET, fixture, debugger mode и конфигурацию Release. Metadata reflection после warm-up проверяется счётчиком BuildCount. JSON Designer Preview/Export не переключаются на imported projection.

Imported Preview использует прежний native builder `AxamlLayoutProjection`: это безопасная проекция доверенных framework controls. Произвольные Styles/Resources/Templates/Bindings не исполняются, ограничения агрегируются в существующей диагностике. Нативный runtime исходного AXAML и проекция сравниваются только на доверенных fixtures. Итоги и границы: [Phase 4B](../../Docs/UniversalControlsAndPropertiesPhase4B.md).

## Universal Controls Phase 5A

* `--universal5a-audit` — inventory установленных стандартных assemblies, coverage CSV и реальные строки PropertyGrid для шести новых controls. Каталог задаётся `UNIVERSAL5A_OUTPUT_DIR`. `UNIVERSAL5A_RUNTIME_VERIFIED=1` разрешено указывать только после успешных native/generated acceptance checks; discovery самостоятельно не доказывает runtime support.
* `--universal5a-checks` — регистрация, поиск Toolbox, routed Grid Drop, selection/Undo, JSON, native Designer и оба Preview, nested layout, source-preserving Edit/Reset/Undo, range/URI validation, opacity, тонкий GridSplitter и keyboard resize, Multi Form, 20+20 Preview cycles и GC удалённых native controls. `UNIVERSAL5A_FILTER` выбирает отдельный сценарий; фильтрованный PASS не заменяет полный прогон.
* `--universal5a-generated` — требует новый `UNIVERSAL5A_EXPORT_DIR`; экспортирует Grid/Canvas projects. `ProbeRuntime.ps1 -ProjectPath <export>/Grid -ProbeFile Phase5ARuntimeProbe.cs.txt -ResultName <unique>.txt` проверяет native visual tree, brushes/range/URI, реальные timer clicks RepeatButton и их прекращение после release, keyboard GridSplitter и resize. Повторить для Canvas. URI не открывается тестом.
* `--universal5a-profile` — 5 warm-up + 30 замеров каждого типа/операции: ToolboxCreate, Inspector, SaveLoad, ExportAxaml, LegacyPreview, AxamlPreview. Типы задаются `UNIVERSAL5A_PROFILE_TYPES` через запятую; новый output JSON — `UNIVERSAL5A_OUTPUT`. Inspector измеряется после выбора другого компонента; includes synchronous UpdateLayout, не физический first frame. Preview включает настоящий loading workflow. `UNIVERSAL5A_PROFILE_STAGES=1` включает opt-in stage/call counters; CpuMs фиксирует process CPU time. Временные VMs очищаются существующим DisposeExportOnlyViewModel, с проверкой отписки от static ValueTrace; GC внутри timed операций не форсируется. Для before/after файл profile переносится в git archive baseline без переноса production изменений.

Использовать существующий process-local NativeCapture с новым каталогом на каждый запуск. Не запускать benchmark одновременно со сборками/SQL/regression. Фиксировать внешнюю нагрузку, P95 и allocations, а не только median. Итог и ограничения: [Phase 5A](../../Docs/UniversalControlsAndPropertiesPhase5A.md).

## Universal Controls Phase 5B

* `--universal5b-checks` — native types/properties/tree, Grid/Canvas, routed root/nested Toolbox Drop, capacity guards, Points/Data/dash validation, Reset/Undo/source patches, trusted namespaces, opaque object Geometry, zoom/scaled selection, transparent shapes/resize, Multi Form, both Previews and 20+20 memory cycles. `UNIVERSAL5B_FILTER` — выбор одного сценария; filtered PASS не заменяет полный run.
* `--universal5b-generated` — новый `UNIVERSAL5B_EXPORT_DIR`; экспорт Grid/Canvas. `ProbeRuntime.ps1 -ProjectPath <export>/Grid -ProbeFile Phase5BRuntimeProbe.cs.txt -ResultName <unique>.txt` проверяет 8 native types, реальный Child/Content/Children, geometry/dashes/points, UniformGrid FirstColumn/order, Expander keyboard toggle, scrolling, Viewbox scaling, три resize. Повторить для Canvas. Fitted Bounds Viewbox могут отличаться от authored Canvas slot; offsets не подгоняются.
* `--universal5b-audit` — `UNIVERSAL5B_OUTPUT_DIR`, coverage CSV и actual Phase5BPropertyGrid.csv. `UNIVERSAL5B_RUNTIME_VERIFIED=1` указывать только после реальной native/generated acceptance.
* `--universal5a-profile` с `UNIVERSAL5A_PROFILE_TYPES=Viewbox,UniformGrid,Expander,ScrollViewer,Line,Polygon,Polyline,Path` переиспользует portable workflow benchmark Phase 5A и его VM cleanup. Before — те же существующие Button/NumericUpDown в archive baseline; опциональный `UNIVERSAL5A_PROFILE_STAGES=1` — instrumented run.
* `--universal5b-scale-profile` — 5 warm-up + 30 measured Add/Inspector/SaveLoad/export/двух Preview для 100 Canvas shapes и 100 nested children в ScrollViewer/StackPanel. JSON результата — `UNIVERSAL5B_OUTPUT`. В Preview проверяется actual native tree, temporary VM освобождается существующим cleanup.

Все runner сценарии запускать через существующий NativeCapture в отдельные directories. Не перестраивать runner bin, пока им пользуется запущенный suite (Windows DLL lock); build failure и первые red assertions сохранять. Новые Geometry literals ограничены documented mini-language; это не object graph/Template Designer. Итог: [Phase 5B](../../Docs/UniversalControlsAndPropertiesPhase5B.md).

## Phase 5C

* `--universal5c-checks` — новые Label/AccessText/ToggleButton/CalendarDatePicker/MaskedTextBox/ButtonSpinner: actual native types/properties, JSON reload, Grid/Canvas/nested layouts, routed Drop/resize, Structure selection, scalar edits/Reset/source patches, nullable atomic Undo, protected expressions, geometry, interactive controls и 20+20 Preview cycles. `UNIVERSAL5C_FILTER` ограничивает проверку; фильтрованный PASS не заменяет полный suite.
* `--universal5c-generated` требует fresh `UNIVERSAL5C_EXPORT_DIR`. `ProbeRuntime.ps1 -ProjectPath <export>/Grid -ProbeFile Phase5CRuntimeProbe.cs.txt -ResultName <unique>.txt` проверяет runtime native types, свойства, настоящие masked TextInput/three-state key events/calendar popup/Spin и resize. Повторить для Canvas.
* `--universal5c-audit`: `UNIVERSAL5C_OUTPUT_DIR`; обычный inventory + `StandardLibraryCoverage.csv`, раздельная assembly summary, requested-type availability и actual Inspector rows. `UNIVERSAL5C_RUNTIME_VERIFIED=1` разрешён после native/runtime acceptance. Strict Fully Supported не приравнивается к проверенному literal subset; roots/associated objects/template parts исключены из placement denominator с причинами.
* `--universal5c-context-diagnostics`: `UNIVERSAL5C_CONTEXT_OUTPUT` — отдельный JSON trace selected/unselected cases. `CONTEXT5C_TRACE_OUTPUT` у `--layout7a-checks` сохраняет attach/open/close trace исходного regression. Не связывать эти managed observations с историческим UIA AV без evidence.
* `--universal5a-profile` переиспользуется для новых типов через `UNIVERSAL5A_PROFILE_TYPES`. `--universal5c-operations-profile` + `UNIVERSAL5C_OPERATIONS_OUTPUT` добавляет 5 warm-up/30 measurements ToolboxInitialize, cached metadata enumeration, synchronous native Designer projection и actual Undo/Redo commands. Этот portable file можно скопировать без изменений в archived baseline harness; production baseline source при этом не меняется.

Отчёт: [Phase 5C](../../Docs/UniversalControlsAndPropertiesPhase5C.md). Full dump capture/accessibility policy прежние; первые неуспешные прогоны сохраняются.
