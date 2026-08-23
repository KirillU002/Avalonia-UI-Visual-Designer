# Avalonia UI Visual Designer — baseline перед Host Architecture Refactor

**Дата измерения:** 23.08.2026  
**Назначение:** зафиксировать наблюдаемое состояние standalone-приложения до любого выделения DesignerDocumentSession, DesignerSurface, host abstractions, AvaloniaDesigner.VsHost.exe или VSIX.

Документ отражает фактические команды и smoke tests. Он не является планом рефакторинга и не меняет production-код.

## 1. Состояние проекта

Проект представляет собой standalone Avalonia-приложение с единым основным проектом FormDesigner.csproj. В нём физически расположены модели, services, Designer Canvas, MainWindowViewModel, Preview, Export Pipeline, DataGrid, Settings и diagnostics. Plugin contracts и встроенные plugins вынесены в отдельные проекты.

| Характеристика | Значение |
| --- | --- |
| Основной TargetFramework | net6.0 |
| UI framework | Avalonia 11.1.5 |
| Тип приложения | самостоятельное desktop-приложение |
| Основной автоматический набор | smoke-tests/FormDesigner.ExportSmokeTests |
| Количество smoke scenarios | 261 |
| Eremex line | 1.0.98 на Avalonia 11.1.5 |

Production-код в рамках baseline не изменялся.

## 2. Git baseline

| Поле | Значение |
| --- | --- |
| Branch | main |
| Commit | 6cc6fb00c8bef15c887f659b6dcc556e05d43252 |
| Короткий commit | 6cc6fb0 макет gridControl из eremex |
| Дата commit | 2026-07-31 14:24:42 +0300 |
| Рабочая директория до baseline | dirty |

До начала работ уже существовал untracked-файл Docs/VisualStudioExtensionFeasibility.md. Поэтому tag не создавался: baseline SHA зафиксирован документом, а создание pre-designer-host-refactor или alpha-3-pre-host-refactor требует отдельного подтверждения после очистки или осознанного коммита рабочей директории.

## 3. Environment

| Компонент | Фактическое значение |
| --- | --- |
| ОС | Windows 10 10.0.19045, win-x64 |
| Активный SDK | .NET SDK 9.0.200, MSBuild 17.13.8 |
| Установленные SDK | 6.0.428, 9.0.200 |
| Установленные .NET runtimes | 6.0.36, 7.0.20, 8.0.13, 9.0.2 |
| Установленные ASP.NET Core runtimes | 6.0.36, 7.0.20, 8.0.13, 9.0.2 |
| Установленные WindowsDesktop runtimes | 6.0.36, 7.0.20, 8.0.13, 9.0.2 |
| global.json | отсутствует |
| Workloads | wasm-tools-net7, wasm-tools-net8 |

Зафиксированные команды:

    dotnet --info
    dotnet --list-sdks
    dotnet --list-runtimes

Хотя команды исполнялись через SDK 9.0.200, все исходные проекты baseline используют net6.0. .NET 6 установлен, но является out-of-support платформой; это риск сопровождения, а не ошибка текущей сборки.

## 4. Solution structure

FormDesigner.sln содержит четыре проекта:

| Project | Назначение | TargetFramework | Project references | Важные NuGet packages |
| --- | --- | --- | --- | --- |
| FormDesigner.csproj | standalone UI, модели, services, Canvas, Preview, Export, Settings, diagnostics | net6.0 | PluginContracts, DemoDesignerPlugin, MinimalDesignerPlugin, EremexDesignerPlugin | Avalonia 11.1.5, DataGrid 11.1.5, CommunityToolkit.Mvvm 8.2.1, Microsoft.Data.SqlClient 5.2.2 |
| PluginContracts/FormDesigner.PluginContracts.csproj | контракт registry, descriptors и plugins | net6.0 | — | Avalonia 11.1.5 |
| Plugins/DemoDesignerPlugin/DemoDesignerPlugin.csproj | demo-plugin | net6.0 | PluginContracts | Avalonia 11.1.5 |
| Plugins/MinimalDesignerPlugin/MinimalDesignerPlugin.csproj | минимальный sample-plugin | net6.0 | PluginContracts | Avalonia 11.1.5 |

Дополнительные проекты существуют в repository, но не перечислены как отдельные проекты FormDesigner.sln:

| Project | Назначение | TargetFramework | Project references | Важные NuGet packages |
| --- | --- | --- | --- | --- |
| Plugins/EremexDesignerPlugin/EremexDesignerPlugin.csproj | Eremex adapter plugin | net6.0 | PluginContracts | Avalonia 11.1.5, DataGrid 11.1.5, Eremex Controls 1.0.98, DeltaDesign 1.0.98 |
| smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj | консольный headless smoke runner | net6.0 | FormDesigner | прямых NuGet packages нет |
| templates/DesignerPluginTemplate/DesignerPluginTemplate.csproj | шаблон пользовательского plugin | net6.0 | PluginContracts | Avalonia 11.1.5 |

Компактная dependency map:

    FormDesigner
    ├── FormDesigner.PluginContracts
    ├── DemoDesignerPlugin ───────┐
    ├── MinimalDesignerPlugin ────┼── FormDesigner.PluginContracts
    └── EremexDesignerPlugin ─────┘
        ├── Eremex.Avalonia.Controls 1.0.98
        └── Eremex.Avalonia.Themes.DeltaDesign 1.0.98

    FormDesigner.ExportSmokeTests ── FormDesigner
    DesignerPluginTemplate ───────── FormDesigner.PluginContracts

FormDesigner.csproj является крупным aggregation point: исходные папки Models, Services, DesignerSystem, Views и ViewModels пока не разделены на самостоятельные assemblies. Это исходная точка для будущего host refactor, но в baseline не менялось.

## 5. Dependency versions

### Основной standalone project

| Package | Version |
| --- | --- |
| Avalonia | 11.1.5 |
| Avalonia.Controls.DataGrid | 11.1.5 |
| Avalonia.Controls.ItemsRepeater | 11.1.5 |
| Avalonia.Controls.ColorPicker | 11.1.5 |
| Avalonia.Desktop | 11.1.5 |
| Avalonia.Themes.Fluent | 11.1.5 |
| Avalonia.Fonts.Inter | 11.1.5 |
| Avalonia.Markup.Xaml.Loader | 11.1.5 |
| Avalonia.Svg.Skia | 11.0.0.18 |
| Avalonia.Diagnostics | 11.1.5, Debug |
| CommunityToolkit.Mvvm | 8.2.1 |
| Microsoft.Data.SqlClient | 5.2.2 |
| System.Reflection.MetadataLoadContext | 6.0.0 |

### Plugin compatibility line

| Компонент | Version |
| --- | --- |
| PluginContracts Avalonia | 11.1.5 |
| Eremex plugin Avalonia/DataGrid/Fluent | 11.1.5 |
| Eremex.Avalonia.Controls | 1.0.98 |
| Eremex.Avalonia.Themes.DeltaDesign | 1.0.98 |

Прямые Avalonia dependencies текущих исходных проектов выровнены на 11.1.5; отдельное исключение — Avalonia.Svg.Skia 11.0.0.18. Сборка не сообщает конфликтов, но это следует учитывать при любом будущем обновлении Avalonia. Смешивать Eremex линии Avalonia 11 и Avalonia 12 в одном in-process host нельзя.

## 6. Build

Проведён чистый build только со стандартной очисткой build artifacts bin/obj, без удаления пользовательских проектов или данных:

    dotnet clean FormDesigner.sln
    dotnet restore FormDesigner.sln
    dotnet build FormDesigner.sln
    dotnet build FormDesigner.sln --no-restore

| Шаг | Результат | Warnings | Errors | Duration |
| --- | --- | ---: | ---: | ---: |
| dotnet clean FormDesigner.sln | success | 0 | 0 | 6.76 s |
| dotnet restore FormDesigner.sln | success | 0 | 0 | 0.75 s |
| dotnet build FormDesigner.sln | success | 0 | 0 | 14.31 s |
| dotnet build FormDesigner.sln --no-restore | success | 0 | 0 | 6.79 s |
| dotnet build smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj --no-restore | success | 0 | 0 | 15.44 s |

dotnet выводит только информационное сообщение о доступном обновлении workload. Оно не является compiler warning. Restore на уровне solution в этот запуск был стабилен.

## 7. Tests

В repository обнаружен один исходный test-project: smoke-tests/FormDesigner.ExportSmokeTests. Это не dotnet test suite, а самостоятельный консольный headless runner с 261 scenario definitions в Program.cs.

### Полный smoke run

Команда:

    dotnet run --project smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj --no-build

Результат не может считаться полностью green:

| Показатель | Результат |
| --- | --- |
| Определено сценариев | 261 |
| Выполнено до зависания | 187 |
| Passed до зависания | 185 |
| Failed до зависания | 2 |
| Не выполнено в полном запуске | 74 |
| Точка остановки | scenario 188: DotnetRestoreWorksFromExportedProjectRoot |

AssertDotnetRestoreWorksFromExportedProjectRoot запускает дочерний dotnet restore через RunProcess, который использует process.WaitForExit() без timeout. В данном baseline дочерний restore не вывел сообщения несколько минут и полный runner был остановлен вручную. Причина зависания не диагностирована; нельзя утверждать, что это именно ошибка NuGet source или generated project.

### Зафиксированные failed scenarios

1. SqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled — ожидается diagnostic mode DemoData, фактически получен SampleRows.
2. DllTablePreviewClearlyMarksSampleRows — при включённом sample fallback не появились ожидаемые ограниченные preview rows.

### Дополнительные целевые прогоны после остановки полного runner

| Filter | Результат |
| --- | --- |
| Eremex | 3/3 passed |
| MultiForm | 13/13 passed |
| NewProject | 6/6 passed |
| AddForm | 8/8 passed |
| RuntimePreview | 10/10 passed |
| SaveLoad | 1/1 passed |
| PluginFallback | 1/1 passed |

Эти filtered runs частично пересекаются с уже выполненными 187 сценариями и не должны суммироваться как единый total. Они служат целевой проверкой рискованных standalone областей, расположенных после зависшего scenario.

| Область | Характер проверки | Ограничение |
| --- | --- | --- |
| SQL | fake provider, generated code и runtime model | реальный SQL Server, credentials и сеть не используются |
| DLL | временно собранные test assemblies и metadata | реальные пользовательские DLL с произвольными dependency graphs не покрыты |
| NuGet restore/build | generated project и локальная среда | зависит от сети/NuGet и сейчас не ограничен timeout в RunProcess |
| Eremex | локально собранный plugin, visual/template probes | состояние commercial license/trial в production окружении отдельно не проверялось |
| UI | Avalonia headless/runtime smoke | нет полного desktop UI automation с ручными drag/drop, DPI и multi-monitor |

## 8. Regression coverage

| Подсистема / сценарий | Статус | Фактическое покрытие или пробел |
| --- | --- | --- |
| New Project и очистка старого состояния | Covered | NewProject*, освобождение forms/controls и очистка data/DLL/export caches |
| Open/Save JSON project | Partially covered | SaveLoadMultiFormProject; интерактивные StorageProvider/file-dialog flows не покрыты |
| Save As | Not covered | отдельного scenario не найдено |
| Закрытие проекта и подтверждение несохранённых изменений | Partially covered | Settings flag покрыт, полный window-close workflow не покрыт |
| Add Form и switch form | Covered | AddForm*, SwitchFormsClearsInspector, MultiFormDocumentStateIsolation |
| Delete Form | Not covered | отдельного scenario не найдено |
| Selection и Property Inspector после switch | Covered | active form, inspector и selection scenarios |
| Разные DataGrid на нескольких формах | Covered | MultiFormSqlExport*, MultiFormSameControlNamesPropertyGridEdit |
| Add control и Toolbox drop | Covered | MultiFormToolboxDropPropertyEdit, plugin vertical slices |
| Move/resize/z-order | Partially covered | drag, resize, bounds/order export покрыты; нет desktop pointer automation |
| Delete control и multi-selection | Not covered | отдельного полноценно подтверждённого scenario не найдено |
| Undo/Redo | Partially covered | очистка history при New Project проверяется; user-level Undo/Redo операции не покрыты |
| Базовые свойства | Covered | Text, Width/Height, foreground/background, opacity, enum ComboBox и CornerRadius |
| Полный Property Inspector | Partially covered | базовые, DataGrid и plugin paths есть; все editor types не покрыты системно |
| Legacy Preview | Covered | simple form, standard controls, DataGrid, Eremex vertical slices |
| Runtime AXAML Preview | Covered | loader, root transform, theme host, simple window, DataGrid и Eremex |
| Simple/Multi Form Export | Covered | AXAML/C#/ViewModel/build scenarios, включая secondary forms |
| Full export restore | Partially covered | много generated builds проходят, но DotnetRestoreWorksFromExportedProjectRoot завис |
| Standard DataGrid | Covered | columns, group/filter panel, bindings, SQL/DLL/manual modes, exported build |
| SQL | Partially covered | SQL generator/preview/cache/multi-form используют test provider; live server не покрыт |
| DLL | Partially covered | import metadata, duplicate names, cache/release и binding keys; один sample fallback scenario failed |
| BindingSource | Covered | workflow, preview/runtime parity, export bindings |
| Plugin discovery/fallback | Partially covered | Eremex loading/missing plugin и PluginFallbackExport; hot reload отсутствует |
| Settings | Covered | apply/save/cancel/reset, localization, persistence, SQL/NuGet, scroll layout |
| Help Center | Covered | open/reuse/navigation/search/maximized layout/content |

## 9. Standalone functionality

Automated baseline подтверждает следующие standalone пути:

- создание нового проекта с очисткой формы, selection, undo/redo history и data caches;
- работа нескольких форм: добавление, переход между формами, сохранение isolation и редактирование после перехода;
- Toolbox drop, редактирование properties и экспорт без ApplyDocument-мутаций editor state в проверяемых сценариях;
- Settings и Help Center;
- Plugin fallback и save/load Multi Form project;
- экспорт standard controls, DataGrid и generated solution/build paths, которые успели завершиться.

Это не равно полной ручной UI-сертификации. Baseline не включает проверку реальных системных file dialogs, clipboard, DPI/multi-monitor, accessibility или полный набор pointer gestures в desktop runtime.

## 10. Preview

| Режим | Статус | Evidence |
| --- | --- | --- |
| Legacy Preview | Covered | Preview/export control order, bounds/key properties, standard DataGrid, Eremex vertical slices |
| Runtime AXAML Preview | Covered | in-memory/string path, отсутствие precompiled temp-resource loader, корректный UserControl root, host theme, simple window/DataGrid |
| Preview state isolation | Covered | PreviewGenerationDoesNotMutateEditorState, AxamlPreviewDoesNotMutateEditorState |
| Preview с внешними production SQL/DLL sources | Partially covered | test provider/schema paths, без live SQL/DLL environment |

RuntimePreview filtered run завершился 10/10. Ошибки fallback diagnostics и transform root properties имеют coverage. Визуальная fidelity на реальных мониторах в baseline отдельно не измерялась.

## 11. Export Pipeline

Export Pipeline покрыт simple form, control order/Z-order, generated AXAML/C#, ViewModel, NuGet config, DataGrid, SQL и Multi Form scenarios. Проверены secondary forms: отдельные DataContext, loaders и connection contexts для SQL DataGrid не зависят от active form.

Подтверждены generated DataGrid AXAML и runtime binding, SQL loader и package references, отсутствие demo seed для SQL/no-source modes, NuGet.config/custom source behavior и build diagnostics.

Ограничение baseline: итоговый dotnet restore generated project scenario не завершился из-за отсутствия timeout в test helper. Следующие этапы не должны считать full Export Pipeline green, пока этот шаг не станет детерминированным и два failed data fallback scenarios не будут закрыты или осознанно обновлены.

## 12. DataGrid

### Standard Avalonia DataGrid

Сильно покрыт smoke scenarios: columns, cell style, clipping, grouping/filter row, horizontal scroll, preview/export parity, manual columns, SQL/DLL schema, Column Editor и runtime rows. Multi Form SQL export проверен отдельными scenarios.

Два связанных с fallback data mode отклонения отражены в разделе Known issues; они не отменяют остальные успешные SQL/DLL DataGrid scenarios.

### Eremex DataGridControl

Текущая реализация является Phase 1 vertical slice. EremexDataGridControlVerticalSlice passed в целевом прогоне. Подтверждённый public API ограничен ItemsSource, AutoGenerateColumns, базовыми options column/filter/group/search/selection/appearance и реальным visual/template lifecycle.

Не заявлены как завершённые explicit сложные column collections, bands, summaries, templates, server mode, master-detail и advanced grouping. Это known incomplete feature, а не failed baseline в подтверждённом Phase 1 scope.

## 13. SQL / DLL / BindingSource

| Источник | Текущее состояние | Ограничение |
| --- | --- | --- |
| BindingSource | покрыт preview/runtime/export scenarios | user-specific objects и все collection types не проверены исчерпывающе |
| SQL Server | конфигурация, string builder, loaders, cache, Preview/Export parity и Multi Form покрыты | нет live SQL Server integration run |
| DLL | import metadata, LINQ-to-SQL attributes, duplicate table identities, cache cleanup и binding keys покрыты | один sample fallback scenario failed; arbitrary production DLL graph не покрыт |
| Demo/empty modes | значительная часть правил покрыта | diagnostic label mismatch и DLL fallback regression остаются |

## 14. Plugin System

Plugin system строится вокруг FormDesigner.PluginContracts; встроенные demo/minimal plugins и EremexDesignerPlugin используют этот contract. Smoke coverage включает plugin fallback export, missing-plugin preservation внутри Eremex vertical slices и runtime contribution paths.

Текущий baseline подтверждает plugins для построенной Debug-конфигурации. Не подтверждены hot reload, обновление plugin на диске во время работы, конфликты версий произвольных third-party dependencies, подписанная доставка и marketplace workflow.

Важное правило для будущего host refactor: plugin registry, descriptor metadata, serialization fallback и export contributions — существующее поведение standalone, которое нельзя заменять вторым VS-specific registry.

## 15. Eremex

| Возможность | Статус baseline | Подтверждение / граница |
| --- | --- | --- |
| Совместимая line | Working | host Avalonia 11.1.5, Eremex Controls/DeltaDesign 1.0.98 |
| Eremex.TextEditor | Working in automated vertical slice | EremexTextEditorVerticalSlice passed: plugin, Canvas/Preview/Runtime AXAML/export/generated app path |
| Eremex.DataGridControl | Working in automated Phase 1 vertical slice | EremexDataGridControlVerticalSlice passed; advanced Grid API намеренно вне scope |
| Theme isolation | Working in automated check | EremexThemeDoesNotChangeDesignerChrome passed |
| DeltaDesign location | Scoped | локально для Eremex Canvas/Legacy Preview, isolated Runtime AXAML Preview и exported App.axaml; не в Application.Current.Styles |
| License/trial state | Not covered | baseline не подтверждает состояние конкретной commercial license/trial на пользовательской машине |

Проверка theme isolation не выявила глобальной регрессии: тест гарантирует, что DeltaDesignTheme не добавляется в Application.Current.Styles, а применяется к subtree Eremex. На данном automated baseline не обнаружен KNOWN_BASELINE_REGRESSION изменения toolbar, Toolbox, Property Inspector, Settings или Help Center глобальной темой. Полное визуальное ручное сравнение chrome не выполнялось в этом этапе.

## 16. Memory / Performance

Это лёгкий baseline, не полноценный benchmark. Debug standalone executable был запущен без открытия пользовательского проекта и скрыт после измерения.

| Метрика | Результат | Условия |
| --- | ---: | --- |
| Cold startup до WaitForInputIdle плюс короткая стабилизация | 6.06 s | bin/Debug/net6.0/FormDesigner.exe, Windows 10, Debug |
| Working Set | 74.9 MiB | после старта, без открытого user project |
| Private Memory | 84.1 MiB | после старта, без открытого user project |

Логические освобождения объектов и caches имеют automated coverage: NewProjectReleasesOldFormsAndControls, NewProjectClearsDataDllExportCaches, RemoveDllReleasesMetadataAndCaches прошли в основном прогоне.

Числовые измерения для проекта с ~20 controls, нескольких forms, Eremex DataGrid, 20 переключений forms и многократного открытия/закрытия Preview не выполнены: в runner нет стабильного performance harness с одинаковым desktop workload. Это пробел baseline, а не доказательство memory leak.

## 17. Known issues

1. **SqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled failed.** При включённом demo fallback ожидается diagnostic mode DemoData, но получен SampleRows.
2. **DllTablePreviewClearlyMarksSampleRows failed.** При включённом sample fallback не создаются ожидаемые ограниченные preview rows. Нужно определить: это регрессия реализации или устаревшее ожидание теста.
3. **Full smoke runner может зависнуть на external restore.** RunProcess вызывает WaitForExit() без timeout; scenario DotnetRestoreWorksFromExportedProjectRoot не завершился и не дал diagnostic output.
4. **.NET 6 является out-of-support.** Текущая линия работает и собирается, но дальнейшее планирование host architecture должно учитывать отдельную migration decision; в baseline версия не обновлялась.
5. **Smoke project не включён в FormDesigner.sln.** Его требуется строить и запускать отдельной командой. Будущий CI/refactor workflow не должен ограничиться dotnet build FormDesigner.sln.
6. **Нет live integration tests** для реального SQL Server, произвольных пользовательских DLL и состояния Eremex license/trial.

## 18. Features currently under development

| Возможность | Статус |
| --- | --- |
| Eremex DataGridControl advanced API | Phase 1: базовый vertical slice работает; complex columns/bands/summaries/templates/server mode/master-detail/advanced grouping не реализованы |
| Full deterministic generated-project restore smoke | blocked by timeout-less external process behavior |
| Data source sample fallback diagnostics | имеются два failed regression scenarios |
| Future Standalone + Visual Studio Host architecture | исследование подготовлено, implementation в baseline не начиналась |

## 19. Что нельзя сломать будущим рефакторингом

Ниже — regression contract, основанный только на подтверждённых сценариях baseline.

1. Standalone FormDesigner должен собираться на текущей линии net6.0/Avalonia 11.1.5 до принятия отдельного migration decision.
2. New Project обязан очищать старые forms, controls, selection, undo/redo history, data/DLL/export caches и не переиспользовать старые identifiers.
3. Multi Form обязан сохранять изоляцию documents, корректно переключать active form и очищать устаревший Property Inspector target.
4. После Add Form должны работать редактирование Text/foreground/background, drag и экспорт первой формы.
5. Toolbox, selection и Property Inspector должны продолжать работать для standard controls, plugin controls и Eremex descriptors.
6. Legacy Preview и Runtime AXAML Preview не должны мутировать editor state; Runtime AXAML Preview обязан работать без temp-file precompiled resource path.
7. Preview и Export должны сохранять control order, bounds и ключевые properties в подтверждённых сценариях.
8. Standard Avalonia DataGrid должен сохранять columns, group/filter structure, bindings и data mode rules; SQL Multi Form export не должен зависеть от active form.
9. SQL/DLL/BindingSource pipeline не должен подставлять fake rows для configured real sources; известные fallback regression scenarios должны быть учтены отдельно, а не замаскированы.
10. Export должен продолжать генерировать AXAML, C#, ViewModel, NuGet config и secondary forms, не мутируя editor state.
11. Settings и Help Center должны сохранять проверенные paths: persistence, language/apply, section navigation, scrolling и singleton window reuse.
12. Plugin contracts, missing-plugin fallback serialization и export contributions должны оставаться едиными для всех будущих host, а не дублироваться.
13. Eremex TextEditor и Phase 1 DataGridControl должны использовать совместимую line 1.0.98 и scoped DeltaDesign theme; theme не должна менять global Designer chrome.
14. Будущий host refactor не должен скрывать перечисленные known issues. После каждого этапа они должны быть либо воспроизведены, либо закрыты отдельной задачей с обновлённым baseline.

## 20. Итоговый baseline verdict

**Verdict: READY WITH CONDITIONS.**

Основание:

- чистый solution build и отдельная сборка smoke project проходят с 0 warnings и 0 errors;
- ключевые standalone paths (New Project, Multi Form, Preview, Eremex, Settings, Help, Plugin fallback) имеют положительные targeted smoke results;
- однако полный smoke suite не green: два data fallback failures и один timeout-less external restore шаг не позволяют назвать baseline полностью стабильным.

До начала выделения DesignerDocumentSession рекомендуется сначала:

1. зафиксировать отдельными issue две failed data fallback проверки и определить их ожидаемое поведение;
2. сделать полный smoke runner time-bounded и диагностируемым для внешних dotnet restore/dotnet build процессов;
3. повторить полный 261-scenario run до детерминированного итогового результата;
4. сохранить этот commit SHA как tag только после явного решения по текущей dirty working tree.

После выполнения этих условий следующий архитектурный этап может быть ограничен аккуратным выделением DesignerDocumentSession, без перехода к VSIX или изменения standalone UX.

