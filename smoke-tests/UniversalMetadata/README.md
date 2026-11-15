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

* `--universal4a-checks` — 38 source/default/null/namespace/readonly/nested/native/memory checks. Model-based Preview limitations проверяются как предупреждения, а не как visual consistency PASS.
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
