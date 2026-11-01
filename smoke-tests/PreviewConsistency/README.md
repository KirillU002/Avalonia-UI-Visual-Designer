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

`visual-tree.csv` содержит X/Y/Width/Height в DIP, actual type, ParentId, attached/visible flags и размер viewport. `geometry-comparison.json` сравнивает все четыре режима. Native Grid assertions используют допуск 1.1 DIP. Legacy Canvas differences измеряются и сохраняются, но не объявляются responsive equivalence: причины перечислены в Docs/PreviewConsistencyAudit.md.

Для live SQL tests требуется **отдельная тестовая** БД с `smoke-tests/SqlServer/DataGridPreviewFixture.sql`. При запуске скрипта через sqlcmd задайте `-f 65001`, чтобы сохранить кириллицу. Пароль не записывайте в исходники или логи. Подключение передаётся через `FORMDESIGNER_TEST_SQLSERVER`; без него live проверки явно SKIP.

```powershell
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-checks
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-source-check
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --datagrid-preview-export-check
dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll --preview-memory-checks
```

DataGrid live checks проверяют видимые native cells, empty/error/alias/filter/multi-grid cases и actual resize Grid/nested Grid. Export check действительно запускает две native формы с SQL rows и проверяет resize, а не только build. Memory suite использует WeakReference и GC только в тесте.

Imported-source limitation test намеренно подтверждает отсутствие ComboBox native instance в model-based Preview и требует явную диагностику. Его PASS означает «ограничение объяснено и source не повреждён», а не поддержку исходного ComboBox/resources. См. PC-09 в отчёте.
