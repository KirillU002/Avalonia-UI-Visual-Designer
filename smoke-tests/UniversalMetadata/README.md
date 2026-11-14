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
