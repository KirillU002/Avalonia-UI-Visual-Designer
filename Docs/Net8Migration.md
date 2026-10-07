# Controlled .NET 8 migration

## Read-only audit, 2026-10-07

Найдено 11 исходных csproj. Solution перечисляет 7 проектов; Eremex plugin
строится через ProjectReference FormDesigner, smoke/sample/template находятся
вне solution. DesignerSystem/services/shared DesignerSurface являются исходниками
FormDesigner, отдельного core csproj нет. SDK всех проектов: Microsoft.NET.Sdk.

| Project | Current TFM | Output | Important dependencies | Candidate | Reason / action |
| --- | --- | --- | --- | --- | --- |
| FormDesigner | net6.0 | WinExe | Avalonia 11.1.1, Svg.Skia 11.0.0.18, Toolkit 8.2.1, SqlClient 5.2.2, MetadataLoadContext 6.0.0 | net8.0 | SAFE_TO_MIGRATE; сохранить версии, проверить native XAML/plugins/preview/export |
| PluginContracts/FormDesigner.PluginContracts | net6.0 | Library | Avalonia 11.1.1 | net8.0 | SAFE_TO_MIGRATE; используется только modern designer/plugins, не VSIX |
| Plugins/DemoDesignerPlugin | net6.0 | Library | Avalonia 11.1.1, contracts | net8.0 | SAFE_TO_MIGRATE; согласовать output Plugins с host |
| Plugins/MinimalDesignerPlugin | net6.0 | Library | Avalonia 11.1.1, contracts | net8.0 | SAFE_TO_MIGRATE; согласовать output Plugins с host |
| Plugins/EremexDesignerPlugin | net6.0 | Library | Eremex Controls/DeltaDesign 1.0.98, Avalonia 11.1.1 | net8.0 | SAFE_TO_MIGRATE candidate; net6 assets доступны net8, runtime подтвердить vertical slices |
| AvaloniaDesigner.VsHost | net6.0 | WinExe | FormDesigner, Host.Protocol | net8.0 | SAFE_TO_MIGRATE; отдельный process, изменить путь упаковки и проверить runtimeconfig/IPC |
| AvaloniaDesigner.Host.Protocol | netstandard2.0;net6.0 | Library | Только BCL, без visual/VSSDK packages | netstandard2.0;net8.0 | SAFE_TO_MIGRATE modern leg; сохранить существующий compatible leg для VSIX |
| AvaloniaDesigner.VSIX | net472 | Library, VSIX | VisualStudio.SDK 17.9.37000, VSSDK.BuildTools 17.10.2178, AsyncPackage, System.Design/WPF | net472 | KEEP; in-process VSSDK загружается .NET Framework внутри devenv.exe |
| smoke-tests/FormDesigner.ExportSmokeTests | net6.0 | Exe | FormDesigner, VsHost, Protocol, linked nonvisual VsTextPatch | net8.0 | SAFE_TO_MIGRATE; обновить paths к migrated runtime, не TFM экспортируемых проектов |
| Samples/VisualStudioPoC/SimpleAvaloniaApp | net6.0 | WinExe | Avalonia/Desktop/Fluent/Inter 11.1.1 | net8.0 | SAFE_TO_MIGRATE; не меняем AXAML fixture |
| templates/DesignerPluginTemplate | net6.0 | Library | Avalonia 11.1.1, contracts | net8.0 | SAFE_TO_MIGRATE; template ссылается на migrated contracts |

Исходные TFM: net6.0, netstandard2.0, net472. Полный scan csproj/props/targets
включая ignored files обнаружил 13 source/config files и 20 generated build
files; последние не являются source, не редактируются. Generated project files
в artifacts на момент initial scan отсутствовали. Directory.Build.props/targets,
Directory.Packages.props и global.json отсутствуют. Central package management,
PackageVersion/VersionOverride/PackageReference Update отсутствуют.
NuGet.config сохраняется: nuget.org, существующий source mapping.

Установлены SDK 9.0.200 (используется) и 6.0.428, .NET 8 runtime/ref pack 8.0.13.
SDK 9 уже собирает net8.0, поэтому SDK/global.json не меняем. Avalonia не требует
net8.0-windows. Cross-platform проекты сохраняют обычный net8.0.

## Compatibility boundaries

VSIX использует текущий AsyncPackage/COM/DTE/Shell внутри devenv.exe. Его нельзя
перевести простым TFM edit в .NET 8: нужна другая out-of-process extensibility
модель и перенос SDK APIs, что вне этой миграции. Microsoft подтверждает
[ограничение VSSDK](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models?view=visualstudio).
Ни VSSDK, ни command registration не обновляются.

Protocol уже multitargeted. Только modern leg заменяется на net8.0;
net472 bridge выбирает netstandard2.0. DTO, serialization, checksum и Named Pipes
не меняются. Multitargeting других библиотек не добавляется.

Eremex nuspec 1.0.98 содержит net6.0 dependency group и lib assets. Он требует
Avalonia 11.1.1, Svg.Skia 11.0.0.18, Toolkit 8.2.1, DynamicData 8.1.1,
SkiaSharp 2.88.6. net6 assets могут быть выбраны net8 restore; реальная
совместимость принимается только после build/template/layout/runtime smoke.
Пакеты не обновляются даже при blocker: тогда проект надо оставить/откатить TFM
и отдельно согласовать смену пакета.

Production ExportPipelineService и Models/ProjectModels не меняются.
Их существующий generated-project target **net6.0** сохраняется по scope запрету
на изменение Export Pipeline/JSON behaviour. Это не технический blocker net8,
а отдельный контракт экспорта; смена generated defaults требует отдельной задачи.
Существующие пользовательские export/artifacts не мигрируются и не удаляются.

## Package snapshot

До source edits сохранён `artifacts/diagnostics/net8-migration/audit-before.json`:
все 11 проектов, SDK/TFM/output/dependencies/references и **32 PackageReference**.
После миграции эти же Include/Update/Version/VersionOverride/Condition entries
автоматически сравнены, а не проверены только git diff.
Результат: **32 before / 32 after / NUGET VERSION CHANGES = 0**.
Сохранены `audit-after.json`, `package-versions-after.json`,
`package-version-diff.json`, `resolved-graphs.json`. Avalonia 11.1.1,
Eremex 1.0.98, Toolkit 8.2.1 и обе версии VSSDK packages не менялись.
VSIX resolved graph не содержит Avalonia/Eremex/FormDesigner visual packages.

## Final framework table

| Project | Before | After | Status | Reason |
| --- | --- | --- | --- | --- |
| FormDesigner | net6.0 | net8.0 | MIGRATED | Compile, native Avalonia UI/component smoke, preview/export/JSON проверены |
| FormDesigner.PluginContracts | net6.0 | net8.0 | MIGRATED | Modern-only consumer graph; VSIX не ссылается на contracts |
| DemoDesignerPlugin | net6.0 | net8.0 | MIGRATED | Compatible contracts, output в net8.0/Plugins |
| MinimalDesignerPlugin | net6.0 | net8.0 | MIGRATED | Compatible contracts, output в net8.0/Plugins |
| EremexDesignerPlugin | net6.0 | net8.0 | MIGRATED | Current package net6 assets совместимы; оба real vertical slices прошли |
| AvaloniaDesigner.VsHost | net6.0 | net8.0 | MIGRATED | External process, publish/packaged IPC/runtime проверены |
| AvaloniaDesigner.Host.Protocol | netstandard2.0;net6.0 | netstandard2.0;net8.0 | MODERN LEG MIGRATED | netstandard2.0 обязателен текущему net472 bridge |
| AvaloniaDesigner.VSIX | net472 | net472 | KEPT | In-process AsyncPackage/VSSDK requires .NET Framework |
| FormDesigner.ExportSmokeTests | net6.0 | net8.0 | MIGRATED | Runner реально работает на 8.0.13 |
| SimpleAvaloniaApp | net6.0 | net8.0 | MIGRATED | Build и external VsHost AXAML open/reopen прошли |
| DesignerPluginTemplate | net6.0 | net8.0 | MIGRATED | Build против migrated contracts прошёл |

Итого: 11 проектов, 10 получили net8.0 target; 9 single-targeted net8.0,
1 Protocol multitargeted, 1 VSIX net472. Новое multitargeting не вводилось.
Blocked-by-package проектов нет. net8.0-windows не понадобился.
Для будущего net8 VSIX потребуется перенос на out-of-process extensibility
с отдельным согласованием SDK/API/архитектуры. Удалять netstandard2.0 из Protocol
можно только после исчезновения текущего .NET Framework consumer.

## Implementation scope

Production C# compatibility fixes не понадобились. Изменены TFM в десяти csproj,
output paths трёх plugins/template и VSIX content path к net8 VsHost.
UI, DesignerSurface, AXAML importer/patch writer, Export Pipeline, JSON model,
Named Pipes DTO/serialization/checksum, VSCT/registration и Eremex API не менялись.
VSIX product/manifest version поднята с 0.1.14 до **0.1.15** для обновления установки.

При runtime closure audit найден существовавший packaging gap: динамически
загружаемые Plugins из FormDesigner output не попадали в VsHost build/publish.
В VsHost csproj добавлены два MSBuild Copy targets: после Build копируется полный
готовый `Plugins` subtree, после Publish он копируется в publish directory.
Нет ручного списка dependency DLL или ProjectReference на visual assemblies
из VSIX. Вся plugin/native/theme closure остаётся внутри `VsHost/Plugins`.
Промежуточный вариант через ResolvedFileToPublish дал NETSDK1152 duplicate items;
финальный AfterPublish Copy не изменяет SDK publish item graph и проходит publish.

Smoke changes: runtime paths обновлены на net8.0; `Net8RuntimeBaseline` проверяет
Environment.Version и assembly TargetFrameworkAttribute; packaging smoke проверяет
runtimeconfig/net8 и plugin closure. External host tests принимают опциональный
`FORMDESIGNER_SMOKE_VSHOST_EXECUTABLE` для проверки build/publish/VSIX deployment.

Один старый PropertyGrid assertion ожидал `SELECTED_CONTROL_SAME_SUPPRESSED`,
но `DesignerDocumentSession.SetSelection` уже возвращает при `!changed` раньше
VM tracing hook. Это не TFM regression: production selection code не менялся.
Тест теперь проверяет отсутствие SelectionChanged, сохранение canonical selection
и identity всех существующих PropertyGrid rows, а не устаревшее имя trace event.

## Validation

Все результаты относятся к Debug на этой Windows машине с .NET runtime 8.0.13.
После TFM edits очищено 18 существовавших regeneratable bin/obj folders с проверкой
absolute path внутри repository и отсутствия tracked source. Export results,
пользовательские проекты, global NuGet cache и исходники не удалялись.

| Command | Result | Log in artifacts/diagnostics/net8-migration |
| --- | --- | --- |
| dotnet restore FormDesigner.sln | PASS | restore.log |
| dotnet build FormDesigner.sln | PASS, 0 errors | build.log, build-final.log |
| dotnet build smoke-tests/FormDesigner.ExportSmokeTests | PASS, 0 warnings/errors | smoke-build-final.log |
| dotnet build Samples/VisualStudioPoC/SimpleAvaloniaApp | PASS, 0 warnings/errors | sample-build.log |
| dotnet build templates/DesignerPluginTemplate | PASS, 0 warnings/errors | template-build.log |
| dotnet publish AvaloniaDesigner.VsHost --self-contained false | PASS | publish-final.log |

Solution build оставляет 2 occurrences одного **NU1603**: VisualStudio.SDK
17.9.37000 требует SDK.Analyzers >=17.7.20; 17.7.20 недоступен, restore выбирает
17.7.22. Версии PackageReference не менялись для устранения этого warning.
SDK остаётся установленным 9.0.200, global.json не создавался.

| Smoke suite | Passed |
| --- | --- |
| Net8 runtime baseline | 1/1 |
| AXAML Round-trip / Simple / Grid / StackPanel / DockPanel / partial / complex / real MainWindow / ACK/conflicts | 60/60 |
| DesignerSurface | 14/14 |
| VsHost / Named Pipes | 10/10 |
| VSIX registration/architecture/runtime packaging guards | 5/5 |
| SaveLoadMultiFormProject (JSON) | 1/1 |
| MultiForm | 13/13 |
| NewProject | 7/7 |
| PropertyGrid | 7/7 |
| PropertyInspector | 5/5 |
| RuntimePreview | 10/10 |
| OpenFormPreviewAndExport | 1/1 |
| Eremex TextEditor vertical slice | 1/1 |
| Eremex DataGridControl vertical slice | 1/1 |

Это **127 уникальных сценариев / 136 suite executions** из-за пересечения filters.
Первый PropertyGrid run имел 1 failure, объяснённый и исправленный выше;
финальные результаты всех перечисленных suites зелёные. Вне этого списка общий
unfiltered smoke run не считается пройденным: пробный запуск был прерван на
ValidateBuildShowsRestoreAndBuildSteps, затем использовались целевые filters.
Полный suite и Release configuration не заявляются проверенными.
Машиночитаемая сводка: `test-results.json`, подробные `smoke-*.log` сохранены.

Дополнительно четыре deployment tests: Hello/HelloAck + OpenDocument и
Simple/Complex/real MainWindow open/reopen в обоих размещениях (publish и
распакованный VSIX). Все 4/4 прошли. Реальный MainWindow сохраняет coverage
**1251 / 1473**, opaque 222, skipped boundaries 76. Его исходный UTF-8 checksum:
`245A2291637F3FF7798651BC74386D050BB34702C66057EF952EB2C13CC32476`.
AXAML не модифицирован миграцией; MainWindow не является fully supported.

Отдельный Windows PowerShell/.NET Framework CLR 4 client загрузил именно
netstandard Protocol из распакованного VSIX root, получил HelloAck от .NET 8
VsHost и открыл SimpleAvaloniaApp и настоящий MainWindow. Оба PartiallyEditable,
без checksum mismatch. В Framework client не загрузились visual assemblies.
Лог: `framework-client-probe.log`, воспроизводимый probe script лежит рядом.
Этот тест подтверждает runtime границу, но не VSSDK package loading в devenv.exe.

## Packaging result and remaining manual check

Файл обновления:
`AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`, version **0.1.15**.

Проверены exe/dll/deps/runtimeconfig/shared assemblies, Avalonia/native runtime,
plugins и Eremex/theme dependencies. Host subtree: **250 files / 264990562 bytes**;
publish имеет те же количество и размер файлов. SHA-256 packaged DLL совпадает
с built net8 VsHost/Protocol; Protocol в root совпадает с built netstandard2.0.
В root VSIX нет Avalonia/Eremex/FormDesigner DLL. Это framework-dependent host:
на машине запуска требуется **.NET 8 runtime**, не только .NET 6/SDK references.

Временные publish/extracted-VSIX каталоги тестов удалены после проверки:
530114268 bytes, только два проверенных path под artifacts/temp. Итоговый VSIX
и bin/net8 runtime оставлены для установки/запуска; компактные diagnostics сохранены.

Расширение **не устанавливалось и не проверялось кликом в живом Visual Studio**
в этом прогоне. Отдельный standalone exe с пользовательскими settings/recovery
тоже не запускался: standalone UI/components/JSON workflows проверены автоматическими
smoke с тестовыми sessions. Финальный interactive acceptance ещё требуется:

1. Установить VSIX 0.1.15 в обычную VS 2022 Community instance и перезапустить VS.
2. Проверить package-load log и Tools -> Open in Avalonia UI Visual Designer.
3. Открыть unsaved Simple AXAML, запустить host, изменить Content/X/Y/Width,
   Apply, проверить dirty buffer, ACK и второй edit.
4. Открыть реальный MainWindow, проверить partial projection и сохранение source.
5. Запустить standalone bin/Debug/net8.0/FormDesigner.exe: New/Open/Save,
   Multi Form, Inspector, Preview, Export, не меняя пользовательские export TFMs.

Обновлять package versions/SDK или менять Export defaults для этой проверки
не требуется. Для generated net6 exports по-прежнему нужен прежний runtime/tooling.
