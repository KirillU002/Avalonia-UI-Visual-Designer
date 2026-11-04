# Preview Phase 2 — стабилизация AXAML Preview

Дата: 01.11.2026. Область: обычные JSON-проекты Designer. Импортированный пользовательский AXAML — отдельный экспериментальный workflow.

## Baseline и метод проверки

Исходный commit: `642de41063088727c105675b50a5a0097dccbe8c`. Рабочее дерево перед началом было чистым. История Git не изменялась. Прочитан `Docs/PreviewConsistencyAudit.md`; использованы существующие Preview, native layout projection, registry, Settings и Export Pipeline.

Исходная Release-сборка solution: 0 ошибок, 1 прежнее предупреждение NU1603 в VSIX (SDK.Analyzers 17.7.20 разрешается в 17.7.22). NuGet versions, TargetFramework, глобальная тема, Grid default и VSIX/IPC production-код не менялись.

Evidence текущего этапа: `artifacts/preview-phase2/`. UI-проверки выполняются последовательно на реальных Avalonia Window/controls с обработкой Dispatcher и применёнными templates. Standalone runtime — отдельный процесс с ClassicDesktopLifetime, временным test-only probe в экспортированном проекте и восстановлением исходных startup bytes в `finally`. Это не проверка только XML или модели, но и не ручное тестирование мышью.

## 1. DataGrid: причина расхождения и исправление

Экспорт уже генерировал локальные `DataGrid.Styles`, а Legacy builder создавал DataGrid с темой Fluent и не применял эти правила. Общая высота могла совпадать, скрывая расхождение типографики, padding, цветов и рамки. В Designer связанный DataGrid представлен декоративной таблицей из Border/TextBlock, со своим несовпадающим padding и прозрачным фоном header cell.

Baseline native measurements: Legacy header — белый фон, `#99000000` foreground, рамка 0, padding `12,0,0,0`, FontSize 12/Normal, MinHeight 32; AXAML — авторские `#ff213457`, `#fff7ecd2`, рамка `1,1,0,1`, padding `10,6,10,6`, Segoe UI 19/Bold, MinHeight 52. Actual Height был 52 в обоих режимах: сравнение внешних Bounds эту ошибку не обнаруживало.

Добавлен `Services/DataGridVisualStyle.cs`: единая спецификация локальных header/cell/row styles. Она генерирует экспортируемые setters и применяет такие же setters к native DataGrid в Legacy и пустому native Designer DataGrid. Декоративный Designer header получает из неё общие визуальные параметры. AXAML Preview загружает эти же экспортируемые styles. Глобальные application styles не изменяются.

Проверяются после применения templates: Background, Foreground, BorderBrush, BorderThickness, Padding, FontFamily, FontSize, FontWeight, MinHeight, фактическая Height, HeaderTemplate. Для связанного Designer измеряется именно Border/TextBlock, а не несуществующий в этом режиме DataGridColumnHeader. Две header-фикстуры в Canvas/Grid проверены четырьмя путями; сравнение сохраняется в `header-style-comparison.json`.

## 2. Canvas: две подтверждённые причины

**Положение root.** Legacy использовал MinWidth/MinHeight и растягивание Canvas, экспорт — фиксированные Width/Height с неявным alignment. Технический UserControl AXAML Preview также сохранял фиксированный размер Window. При client area 800×600 у формы 1200×800 root смещался на −200/−100 DIP. Поэтому элементы меняли положение относительно окна при resize.

Root Canvas теперь явно фиксированного авторского размера, выровнен Left/Top и обрезает выходящее содержимое. JSON AXAML Preview растягивает технический UserControl shell по client area, сохраняя внутренний RootCanvas и его координаты. Grid/native layout semantics не менялись, Canvas не преобразуется в Grid и не адаптируется автоматически. В документ JSON миграция не вносится.

**Изменение геометрии export-only копии.** `LoadDocumentSnapshotForExportGeneration` вызывал `ClampAllControlsToSurface()`. Воспроизведено на Eremex-фикстуре и отдельном стандартном Button: у Canvas 900×620 элемент (790,490), 420×180 превращался в 110×130 в AXAML Preview. Это общий loader, а не Eremex rendering bug. Удалён clamp только из export-only загрузки уже созданного документа. Он не должен переписывать авторские offsets/dimensions; clipping выполняет viewport. Обычная загрузка старых проектов, пользовательские move/resize и существующие ограничения редактирования не переписывались.

`CanvasOverflowGeometryPreserved` был красным до исправления (`overflow-baseline.log`). После исправления он проверяет AXAML geometry и неизменность исходной модели. Проверяются root origin, Button (100,150), client sizes 800×600, 1200×800, 1600×900 и 953×647; также Canvas внутри Grid и Grid внутри Canvas. Изменение размера Preview не меняет координаты элемента.

**Вложенный Grid в документе v1.** Дополнительная строгая фикстура обнаружила ещё одно расхождение: Designer использовал `LayoutArrangementHelper.ArrangeChildren` (равномерные ячейки, без реальных definitions), а Legacy managed subtree применял defaults v2. При `RowDefinitions="80,*"`, `ColumnDefinitions="120,*"` положение Button отличалось от AXAML/runtime до 186 DIP. Важны и defaults v1 Export: Stretch/Top могут быть опущены, чтобы применились значения Avalonia/theme; нулевой Margin заменяется существующим parent spacing.

Добавлен небольшой `LegacyCanvasLayoutProjection`: адаптер этого **существующего export contract** к существующему `AxamlLayoutProjection.BuildJson`, а не новый layout engine. Он работает с копиями моделей с исходными ControlId, фиксированными размерами v1 и не меняет документ. Native Measure/Arrange вычисляет frames только managed subtrees; обычные Canvas children сохраняют свободные X/Y. Designer использует измеренные frames и существующий bounds map для координат/selection; Legacy использует тот же адаптер. Временная measurement factory использует native Button/TextBox/TextBlock, чтобы учитывать theme defaults, а не измерять декоративный Border как Button. V2 и source AXAML paths не изменены; прежняя публичная сигнатура BuildJson сохранена и делегирует новой overload без placement callback. `GridInsideLegacyCanvasGeometry` и standalone четырёхрежимное сравнение стали зелёными без специальных координатных поправок. Некорректные definitions в Designer дают status diagnostic с прежним fallback отображением, а не исключение из render handler.

## 3. Eremex и Avalonia.Diagnostics

Два исходных broad failures воспроизведены без изменения проверок: `EremexTextEditorVerticalSlice`, `EremexDataGridControlVerticalSlice`. Падал constructor **сгенерированного Debug-окна**, вызванный reflection в test AssemblyLoadContext: отсутствовала `Avalonia.Diagnostics, Version=11.1.1.0`.

Точный инициатор — сгенерированный `MainWindow.InitializeComponent → Avalonia.DevToolsExtensions.AttachDevTools`, а не Eremex theme. Test loader делегировал все Avalonia assemblies в Default Release-host; Diagnostics там намеренно отсутствует, хотя DLL доступна в собственных dependencies Debug-приложения. Исправлен только `GeneratedApplicationLoadContext`: при отсутствии именно optional Diagnostics в host использовать собственный dependency resolver generated app. Core Avalonia identities остаются shared, проверки ABI/native types не ослаблены. Пакет не добавлялся как обязательная runtime-зависимость Designer.

Причина подтверждена чтением metadata/IL **без загрузки DLL** (`eremex-dependency-evidence.json`): у обоих generated Debug-приложений имеются reference и AttachDevTools call; у Release нет ни reference, ни вызова. Оба приложения отдельно запущены в Debug и Release. Поэтому это не доказательство дефекта Avalonia или Eremex и не основание менять их версии.

## 4. Optional plugin boundary

* `plugin.runtime.json` Eremex задаёт `preloadRuntimeAssemblies: false`. Для остальных plugins старое поведение по умолчанию сохранено.
* Discovery регистрирует metadata/descriptors, не загружая vendor runtime. Reflection schemas и ABI validation ленивые, запускаются при реальном использовании. Runtime contribution возвращается только для использованных Eremex types; native type tokens вынесены из неиспользуемого JIT path.
* Runtime assemblies при первом использовании по-прежнему получают согласованную Default identity для встраивания native controls в Avalonia visual tree. Theme применяется scoped к используемому preview root. Удаление theme из eager ABI scan не подавляет ошибку theme: она диагностируется в существующей preview boundary.
* Manifest loading перенесён внутрь error boundary PluginLoader. Повреждённый optional runtime даёт PluginLoadReport, а не прекращает запуск стандартного Designer.
* Дополнительный cold negative test удалил `Eremex.Avalonia.Controls.dll`: исходно AXAML snapshot падал в `GetAllRequiredExportPackages`, поскольку lazy discovery всё же публиковал descriptors неполного пакета. Теперь наличие всех заявленных `runtimeAssemblies` проверяется **без их загрузки**, до регистрации. Отсутствие core DLL или theme DLL даёт явный report с именем файла; стандартные controls продолжают отображаться в обоих Preview. Red/green evidence: `missing-runtime-baseline.log`, `preview-phase2-missing-runtime-final.log`, `preview-phase2-missing-theme-final.log`.
* Повторный Load той же DLL (path + timestamp + size) переиспользует существующий PluginLoadContext. Три reload не создают новые contexts. Контексты остаются host-scoped; runtime assemblies в Default не обещают hot-unload. Изменённые binary versions безопаснее применять перезапуском.
* Для сборки только core добавлен MSBuild switch `BuildExperimentalEremexPlugin=false`. Обычная поставка сохраняет сборку/упаковку plugin. ReferenceOutputAssembly остаётся false; root Designer не получает native vendor reference.

Cold-process проверка: 0 загруженных `Eremex.*` assemblies после discovery и стандартного Designer/двух Preview/Export; первое явное создание Eremex активирует runtime. Standard export не содержит Eremex packages. Genuine missing-theme процесс создаётся из отдельной копии plugin без theme DLL: стандартные controls остаются attached, отсутствие theme указано в plugin diagnostics, отсутствующий descriptor диагностируется Preview. Старый JSON с отсутствующим Eremex descriptor сохраняет ControlId, Type, Name и custom properties; стандартный native Button остаётся в visual tree. Отсутствующие plugin controls не удаляются из JSON.

## 5. Preview mode, settings и ошибки

Используется существующая настройка `Preview.UseExportedAxamlPreview`, новый независимый пользовательский переключатель не добавлен. Presence-aware serialization сохраняет тот же JSON boolean/key:

| Состояние | Решение |
|---|---|
| Явно сохранено false | Legacy, сохраняется после Save/Load |
| Явно сохранено true | AXAML, сохраняется после Save/Load |
| Поле отсутствует, JSON-проект | AXAML — новый default после пройденного prerequisite gate |
| Поле отсутствует, imported AXAML session | Прежний Legacy workflow |
| Imported AXAML + явный opt-in | Экспериментальный AXAML model snapshot с существующей диагностикой ограничений |

Отсутствие значения сохраняется при unrelated Settings Apply/Save, поэтому новый default не записывает вымышленный пользовательский выбор и не распространяется на import. Null Preview section нормализуется при чтении settings. Фактический запуск проверяется через `MainWindow.LaunchPreviewAsync`, а не только getter модели.

Переключение `PreviewModePolicy.DefaultForJson` выполнено только после native/runtime/header prerequisite проверок, broad 72/72 и отдельного VsHost-without-plugins 6/6. С новым default проверки Settings и фактического F5 — 28/28. Все явно сохранённые значения имеют приоритет над default.

Автоматический молчаливый fallback убран в двух местах: ошибка генерации в MainWindow и ошибка загрузки AXAML в PreviewWindow. Окно показывает ошибку; при включённой существующей fallback-настройке предлагает кнопку «Переключиться на обычный Preview». Только нажатие запускает Legacy для этого окна, без изменения сохранённого режима. Legacy остаётся доступным в Settings. Текст существующей настройки теперь описывает предложение, а не автоматическое переключение.

Диагностика: `PREVIEW_MODE_SELECTED`, `PREVIEW_MODE_DEFAULT_RESOLVED`, `AXAML_PREVIEW_START/SUCCESS/FAILED`, `LEGACY_PREVIEW_FALLBACK_OFFERED`; поля ProjectId, FormId, PreviewMode, SourceKind, Reason. В события не передаются SQL connection strings, пароли или raw provider exceptions. Mode subscribers отписываются при завершении Preview lifetime.

## 6. Дополнительные blockers, найденные перед сменой default

1. AXAML runtime wiring Button.Click Logic было ограничено native layout v2. Обычная Canvas-кнопка отображалась, но не выполняла основное Logic-действие. Удалён только этот version filter. Routed Click test: исходно 1/2 (Canvas fail), после — оба варианта работают.
2. Исключение AppendXaml стороннего descriptor оставляло comment вместо control и valid-looking частичный Preview. Isolated export VM теперь собирает безопасные сведения о failed adapters; генерация **AXAML Preview snapshot** отказывается от такого результата. MainWindow показывает ошибку и explicit Legacy offer. Test-only fault adapter воспроизводил отсутствие этой защиты. Общее поведение сторонних неисправных adapters в обычном Export не переписывалось: добавлена диагностика, но там прежний comment/partial-output path остаётся экспериментальным ограничением.

## 7. Проверки и результаты

| Проверка | Результат |
|---|---|
| Release solution / smoke runner | 0 ошибок; solution — 1 прежний NU1603, runner — 0 warnings |
| Новый Phase 2 suite после включения default | **28/28** (`default-final.log`); до включения — prerequisite 25/25 |
| Preview consistency, registry-derived 14 controls | **32/32**, повторено после исправления v1 managed geometry (`consistency-final.log`) |
| Основные standalone registry/nested exports | 3/3 запуска; 27 групп geometry comparison без mismatch |
| Дополнительные Canvas/Grid/header/Eremex exports | 6/6 запуска; 54 группы geometry comparison без mismatch |
| Суммарное четырёхрежимное geometry сравнение | **81/81** групп: 9 сцен × 3 client sizes × Designer/Legacy/Runtime относительно AXAML; допуск 1.1 DIP |
| Applied header styles | Canvas и Grid: Designer/Legacy/AXAML/Runtime совпадают; `header-style-comparison.json` |
| Broad export/Preview/round-trip/VSIX/VsHost regression | **72/72** до и после включения default (`broad.log`, `broad-final.log`); исходные Eremex failures закрыты |
| Core без Eremex | Сборка `BuildExperimentalEremexPlugin=false`: 0 ошибок, 0 Eremex файлов в output |
| Отдельный VsHost без каталога Plugins | **6/6**, actual child process/Named Pipes/OpenDocument/minimal patch/conflict/disconnect |
| Cold Eremex / missing core / missing theme | 3 отдельных процесса PASS; metadata discovery не загружает vendor runtime, неполные пакеты диагностируются |
| Live SQL Server DataGrid | **47/47**, повторено с новым default (`datagrid-preview-checks-final-default.log`): 18 deterministic contract и 29 live checks |
| Live Settings → schema → BindingSource → два Preview | PASS, повторено (`datagrid-preview-source-check-final.log`) |
| Live SQL exported runtime | PASS, повторено: отдельный процесс, Customers=3 и Orders=2, реальные видимые значения, independent ViewModels, Grid/nested Grid resize |
| Preview memory | **36/36** до и после включения default (`preview-memory-checks-final-default.log`), включая live LocalDB |
| Pointer / Grid Drop / Native Grid | **26/26**, **40/40**, **28/28**, повторены после v1 geometry fix |
| Nested Layout / Grid Editor / Undo cache | **59/59**, **45/45**, **25/25**, повторены после v1 geometry fix |
| Layout validation / Phase 7A.2 / Phase 7A UX | **55/55**, **13/13**, **15/15**, повторены; соответствующие `*-final.log` |
| PostgreSQL offline | **20/20**, live SKIP |
| Общий stabilization audit | Один полный повтор **25/25**; финальный полный повтор **24/25**, причина ниже. Не объявляется полностью зелёным |

Два отдельных Eremex приложения также запущены в Release, помимо Debug. Это runtime проверки, а не только builds. Отчёт `eremex-dependency-evidence.json` фиксирует различия IL и references Debug/Release.

Для SQL создана новая собственная LocalDB instance `FDPV2_20261101_205643_18bad1`, БД `FDPreviewPhase2`, MDF/LDF внутри task artifacts. Использовалась Windows authentication, пароль не требовался. Customers содержала три различающиеся кириллические строки, Orders — две, EmptyTable — ноль. Проверялись реальные native cells, nullable/metadata mapping, aliases, пустой/ошибочный источник, timeout/cancellation/reopen/multi-grid/multi-form; PostgreSQL live этим не подменяется. По окончании всех live tests БД и собственная instance удалены, MDF/LDF осталось 0 (`localdb/cleanup.log`). Пользовательские instances `MSSQLLocalDB` и `FormDesignerPreviewP0_20261030_183814` сохранены и не использовались.

Memory regression проверяет WeakReference на PreviewWindow, DataContext и ItemsSource, clock observers, close during async load и UI Automation close path. После 20 циклов **каждого** режима: alive old PreviewWindow = 0, active windows = 0, clock observers = 0; сборка мусора выполняется только тестом, не production. Не требуется немедленное уменьшение process working set.

**Незелёные/интермиттирующие наблюдения сохранены.** Первый общий audit дал 24/25 из-за `InactiveDirtyFormAutosavesAndKeepsRecovery`. Без изменения production autosave или assertions этот сценарий прошёл в трёх отдельных процессах и двух последующих полных запусках с диагностикой; dirty inactive form и текст recovery подтверждены. Причина первого сбоя не доказана, он не считается исправленным этим этапом. В финальном общем audit другой failure: `ClosedPlainWindowNegativeControl` удержал 6/6 **обычных** Avalonia Window. В том же процессе Legacy/AXAML PreviewWindow освободились (0/6), отдельный полный Preview memory suite прошёл 36/36. Этот framework/environment negative-control failure не скрывается, не выдаётся за Preview PASS и не «лечится» production GC. Отдельное исследование generic Window/UIA retention остаётся в backlog.

Broad runner дважды не смог удалить собственный загруженный `ToolboxStandardItemsExport.dll` в конце процесса (UnauthorizedAccessException). Это cleanup failure после функциональных 72/72, а не падение сценария. После завершения процессов собственные крупные bin/obj, копии runtime/plugins и остатки runs удалены: **28 paths, 0 cleanup failures**, task artifacts уменьшены с 1 832 086 386 до примерно 1 672 039 bytes (`cleanup-summary.json`). JSON/CSV/logs/manifests и generated source сохранены. Для повторного запуска standalone probes требуется обычный restore/build этих исходников.

## 8. Ограничения и ручная проверка

* PostgreSQL live: SKIP, реальный сервер не доступен. Offline provider tests не выдаются за live integration.
* Произвольный импортированный AXAML по-прежнему не имеет source-aware Preview всего чужого приложения. Source-only controls/resources/styles/bindings не заменяются полной регенерацией исходника. Round-trip/minimal patch workflow не изменён.
* JSON Toolbox registry содержит 14 стандартных controls; ComboBox не зарегистрирован как новый JSON Toolbox control. Проверки импортированного ComboBox относятся к существующему import workflow и его явной диагностике, а не к обещанию полной JSON поддержки.
* Eremex остаётся экспериментальным; отсутствие plugin диагностируется и не удаляет исходные model records. Не обещается восстановление произвольного неизвестного plugin без его assemblies.
* Физическая мышь, экранный диктор/UIA внешнего приложения, DPI 100/125/150/200%, перенос между мониторами и полноценный interactive `devenv.exe` не проверены этой автоматизацией. VsHost process/IPC и VSIX packaging проверяются отдельно от Visual Studio UI.
* Generated projects сохраняют прежний net6.0: NETSDK1138 остаётся исходным предупреждением. Версия не обновлялась.
* Общий audit не полностью зелёный из-за описанного negative-control Window retention; разовый autosave failure тоже сохранён в evidence, его причина не установлена. Это ограничения валидации, а не заявление об исправлении соответствующих подсистем.

Ручной checklist: открыть прежний Canvas JSON и сложную Grid/nested форму; проверить resize и ввод; открыть меню DropDown/Split и выбрать ListBox/DataGrid мышью; проверить сортировку и видимые SQL значения; переключить оба Preview режима, перезапустить Designer; вызвать отсутствующий plugin/ошибку AXAML и осознанно открыть Legacy; открыть исходный AXAML через VSIX, внести minimal patch и проверить comments/styles/bindings. Не превращать успешный build в утверждение о прохождении этих ручных сценариев.

## 9. Изменённые файлы

Production: `Services/DataGridVisualStyle.cs`, `Services/LegacyCanvasLayoutProjection.cs`; `DesignerSystem/Infrastructure/PluginLoading.cs`, `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.Json.cs`; `FormDesigner.csproj`; `Localization/SettingsTextCatalog.cs`; `Models/AppSettingsModel.cs`, `Models/ExportedAxamlPreviewModel.cs`, `Models/PreviewModeDecision.cs`; `Services/AppSettingsService.cs`; `ViewModels/MainWindowViewModel.cs`, `ViewModels/SettingsWindowViewModel.cs`; `Views/MainWindow.axaml.cs`, `Views/MainWindow.NativeLayout.cs`, `Views/PreviewWindow.axaml.cs`, `Views/PreviewWindow.NativeLayout.cs`, `Views/PreviewWindow.DataGrid.cs`, `Views/PreviewWindow.StandardItems.cs`, `Views/PreviewWindow.Lifecycle.cs`, `Views/PreviewWindow.Mode.cs`; Eremex plugin manifest, `EremexPlugin.cs`, оба descriptors, `EremexCompatibilityValidator.cs`.

Tests: `Program.cs`, `Program.PreviewPhase2.cs`, `Program.PreviewPhase2.Export.cs`, `Program.PreviewPhase2.Dependencies.cs`, `Program.PreviewConsistency.cs`, `Program.PreviewConsistency.Geometry.cs`, diagnostic observations в неизменённых assertions `Program.Stabilization.cs` в `smoke-tests/FormDesigner.ExportSmokeTests`; `ProbeRuntime.ps1`, `RuntimeProbe.cs.txt`, `CompareGeometry.py`, `CompareHeaderStyles.py`, `README.md` в `smoke-tests/PreviewConsistency`; этот отчёт.

## Итоговое решение

**AXAML Preview default readiness: READY WITH CONDITIONS** для обычных JSON-проектов со стандартными controls. Default включён; явно сохранённый Legacy/AXAML выбор не переписывается. Canvas остаётся доступным и остаётся default нового layout; Grid-first не включался.

Условия: физическая UI/DPI/Visual Studio проверка остаётся ручной; Eremex экспериментален; generic Window negative-control retention и разовый невоспроизведённый autosave сбой остаются отдельными investigation items. Подтверждённой новой критической Preview/Save/Open/round-trip регрессии в выполненных проверках нет.

Для произвольного импортированного AXAML: **NOT READY** как source-faithful Preview всего приложения. Новый JSON default не включается для такого workflow автоматически. Legacy и явный экспериментальный opt-in сохранены. Другие layout phases не начинались.
