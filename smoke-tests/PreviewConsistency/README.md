# Preview consistency probes

Запускать из корня репозитория, последовательно, без другого UI smoke runner. Используются установленные в проекте версии Avalonia/SDK и реальный Windows desktop backend; это не headless model tests. Окна размещаются за пределами экрана. Физическая мышь и мультимониторный DPI этим способом не проверяются.

```powershell
dotnet build smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj -c Release -m:1
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --preview-consistency-checks

foreach ($scene in 'RegistryNative','NestedNative','RegistryCanvas') {
    ./smoke-tests/PreviewConsistency/ProbeRuntime.ps1 `
        -ProjectPath ("artifacts/preview-consistency-audit/exports/"+$scene) `
        -Manifest ("artifacts/preview-consistency-audit/"+$scene+"-manifest.json") `
        -Scene $scene
}
py -3 smoke-tests/PreviewConsistency/CompareGeometry.py
```

Первый runner получает entries из DesignerRegistry, создаёт JSON fixtures/exports/manifests и измеряет Designer/Legacy/AXAML. Separate-process probe запускает обычный generated App/ClassicDesktopLifetime, проверяет native types, иерархию, Image.Source, Flyout и ListBox selection. Временно меняется только generated App.axaml.cs; bytes восстанавливаются в finally, исходники Designer не патчатся. Старый runtime result удаляется перед запуском, чтобы не принять stale evidence за новый результат.

`visual-tree.csv` содержит X/Y/Width/Height в DIP, actual type, ParentId, attached/visible flags и размер viewport. `geometry-comparison.json` сравнивает все четыре режима с допуском 1.1 DIP, включая Canvas. Сравниваются одинаковые client viewports; сохранение абсолютных координат Canvas не является responsive адаптацией.

Phase 2: `--preview-phase2-checks` проверяет settings/default, fallback, реальные headers и Canvas geometry; `--preview-phase2-plugin-cold`, `--preview-phase2-missing-theme`, `--preview-phase2-missing-runtime` запускаются **каждый в отдельном процессе**. `--preview-phase2-export-fixtures` создаёт шесть дополнительных fixtures в `artifacts/preview-phase2`; их запускают тем же `ProbeRuntime.ps1`. Для сравнения задайте `FORMDESIGNER_CONSISTENCY_ARTIFACTS` этому каталогу и выполните `CompareGeometry.py`, затем `CompareHeaderStyles.py artifacts/preview-phase2`. Последний читает `prereq-final.log` с фактическими template properties, поэтому сохраните вывод `--preview-phase2-checks` под этим именем. `--preview-phase2-dependency-report` читает metadata/IL Debug/Release Eremex fixtures после их сборки; подробности и ограничения — в `Docs/PreviewPhase2.md`.

Для live SQL tests требуется **отдельная тестовая** БД с `smoke-tests/SqlServer/DataGridPreviewFixture.sql`. При запуске скрипта через sqlcmd задайте `-f 65001`, чтобы сохранить кириллицу. Пароль не записывайте в исходники или логи. Подключение передаётся через `FORMDESIGNER_TEST_SQLSERVER`; без него live проверки явно SKIP.

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-checks
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-source-check
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-export-check
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --preview-memory-checks
```

DataGrid live checks проверяют видимые native cells, empty/error/alias/filter/multi-grid cases и actual resize Grid/nested Grid. Export check действительно запускает две native формы с SQL rows и проверяет resize, а не только build. Memory suite использует WeakReference и GC только в тесте.

Imported-source limitation test намеренно подтверждает отсутствие ComboBox native instance в model-based Preview и требует явную диагностику. Его PASS означает «ограничение объяснено и source не повреждён», а не поддержку исходного ComboBox/resources. См. PC-09 в отчёте.
