# Layout Designer Phase 7B — Grid-first Default

Дата: 04.11.2026. Baseline: `665f517db2520363e21f36c888ee57c892d29a16`. Исходное рабочее дерево было чистым. Изучены Architecture, Phase 2–6, Phase 7A/7A2, UndoHistoryCachePoC и PreviewPhase2. Git history не переписывалась.

## 1. Где изменён default

Grid теперь является default **создания** проекта/формы. Сохранён прежний native Grid Phase 2: document Version 2.4, LayoutSemanticsVersion=2, SurfaceLayoutMode=Grid, одна строка `*`, один столбец `*`, spacing 0. Новые типы root, Document Model и layout engine не вводились.

Production-изменения ограничены пятью файлами:

1. `Services/ProjectDocumentService.cs`: overload `AddForm(project, baseName)` создаёт Grid; существующая инициализация Grid вынесена в internal `ConfigureNewFormLayout` и переиспользована другими creation paths.
2. `Services/ProjectWorkspaceService.cs`: `CreateWorkspace`/`CreateForm(name)` создают Grid. Прежняя публичная сигнатура CreateForm сохранена; добавлена overload с явным layout. **Repair fallback** для пустого workspace явно создаёт Canvas.
3. `ViewModels/MainWindowViewModel.NativeLayout.cs`: Grid первый в существующем selector и выбран первоначально.
4. `ViewModels/MainWindowViewModel.cs`: начальный документ VM, New Project и parameterless CreateNewForm используют Grid. Явный Canvas сохраняется. После успешного создания pending choice сбрасывается к Grid.
5. `Views/MainWindow.axaml`: обновлён tooltip существующего selector; глобальные styles/цвета не менялись.

## 2. New Project и Add Form

Начальная форма нового VM и New Project без ручного выбора получают настоящий Grid. New Project использует существующие reset/session/dirty/history операции и не наследует layout активной формы.

Выбор `Canvas — свободное размещение` в существующем selector относится к **следующему созданию**: Add Form либо первой форме New Project. После этого selector снова показывает Grid. Открытие/переключение существующей Canvas-формы само по себе ничего не выбирает для следующей формы. API `CreateNewForm()` всегда означает новый Grid; `CreateNewForm(DesignerLayoutModes.Absolute)` явно создаёт Canvas.

Не добавлены новый мастер, скрытая preference или второй selector. Canvas остаётся рядом с Grid в основном UI. Его исходные v1 defaults (spacing, размеры, координаты) сохранены.

## 3. JSON backward compatibility

**Creation default и read fallback разделены.** Initializers `DesignerDocumentFileModel.LayoutSemanticsVersion=1`, `SurfaceLayoutMode=Absolute`, defaults size modes и `DesignerLayoutModes.NormalizeMode` не изменялись. JSON без layout/semantics fields продолжает означать исторический Canvas. Существующие Canvas, native Grid и старые experimental layouts читаются по своим сохранённым данным.

`WrapSingleDocument`, ApplyWorkspace/ApplyDocument, source import, duplicate, snapshots и recovery не вызывают Grid conversion. Temporary workspace shell при wrapping заменяется исходным документом. Пустой workspace, который восстанавливает EnsureWorkspaceDefaults, получает Canvas через **явный** аргумент — изменение default factory не меняет эту историческую политику.

Новые persisted fields, JSON format version bump и автоматическая миграция не добавлялись. Проверены отдельный legacy document без layout, Canvas с явными fields, workspace с отсутствующими fields, Grid JSON, mixed workspace Save/Load и сохранение ControlId/ParentId/geometry/spans/margins.

## 4. Designer, Toolbox и Inspector

Используются прежние Grid Definition Editor, native measurement, GridCellHitTesting, GridDropTarget и pointer interaction state machine. Новая Grid-форма сразу поддерживает Add Row/Column, definitions и placement.

Через настоящий Avalonia routed DragOver/Drop проверены Button, TextBox, DataGrid, ListBox, SplitButton, DropDownButton и StackPanel в ячейке [1,1]. У implicit root нет отдельного control Id: **ParentId="" означает root Grid**; у вложенного контейнера ParentId содержит его существующий Id. Создавать искусственный root Id ради этой задачи не требовалось.

После Drop сохраняются selection в VM/session/Designer и Inspector Grid.Row/Column=1. X/Y=0 являются нейтральными model hints, Canvas.Left/Top в экспорт Grid children не появляются. Undo удаляет добавление одной операцией, Redo возвращает тот же ControlId/placement. Размеры, bindings и Logic не изменяются default policy.

Canvas проверен отдельно через явное создание, routed Toolbox Drop, raw pointer move и resize. Используются свободные X/Y и размеры; Inspector показывает координаты. Grid Inspector показывает attached placement, spans, Margin/Alignment, size modes и definitions. Прежние Favorites, enum/color editors и русские tooltip descriptions остаются на прежнем механизме.

## 5. Preview, Export и импорт AXAML

Production Preview/Export code, Preview settings и `PreviewModePolicy` не изменены. AXAML Preview остаётся default JSON; явно выбранный Legacy переживает New Project. Оба Preview проверены на Grid с `Auto,*` и DataGrid в star-row: native control остаётся в visual tree и растёт при resize.

Export сохраняет настоящий `Window → Grid`; hidden RootCanvas не появляется. Существующий bypass Responsive Export для native Grid проверен. Явная Canvas-форма экспортирует Canvas и attached coordinates. C# code-behind/ViewModel и требуемые packages генерируются прежним pipeline.

Canvas/Grid source AXAML импортируется по исходнику. Creation default не влияет на source map/root, не включает native JSON semantics и не меняет imported Preview policy. No-op patch сохраняет comments и весь исходный текст. Structural editing по-прежнему ограничено существующими capabilities; source-preserving writer/VSIX/IPC production-код не изменялись.

## 6. Multi Form и настоящий runtime

Новая fixture создаётся **через production defaults**, без подмены DTO:

| Form | Создание | Root / содержимое |
|---|---|---|
| Form1 | New Project | Grid, Navigation StackPanel, DataGrid |
| Form2 | CreateNewForm() | Grid, TextBox + Button Logic, DataGrid |
| Form3 | Explicit Canvas | Canvas, Button (100,150), BackHome |
| Form4 | CreateNewForm() | Grid, nested StackPanel с ListBox/DropDownButton/SplitButton, DataGrid |

В данных — **явно разрешённые локальные fixture rows**, не SQL и не fallback при ошибке БД. BindingSource и его columns сохраняются; DataGrid имеет native rows. Fixture записана в workspace JSON и полный source ZIP, содержащий все четыре формы.

После restore/build запускается отдельный **Release-процесс** обычного generated App/ClassicDesktopLifetime. Test-only probe временно добавляется к exported startup/constructors и восстанавливает оригинальные bytes в finally. Проверяются native root/control types, шесть навигационных переходов, правильные form classes, Button Logic, selection ListBox, открытие двух Flyout, DataGrid rows и resize. У трёх data-bound форм отдельные ненулевые ViewModel. Canvas без bindings закономерно имеет null DataContext — генератор не обязан создавать неиспользуемый ViewModel; это не shared context.

Зафиксировано (`generated-runtime.txt`, `export/runtime.stdout.log`):

```text
PASS forms=4 roots=Grid/Grid/Canvas/Grid navigation=6 independentContexts=true rows=true resize=true CanvasPosition=true
HomeRows:   1200×735 → 1440×855
GridRows:   1200×759 → 1440×879
NestedRows: 1200×665 → 1440×785
```

Canvas Button остаётся в (100,150) после увеличения окна. Наличие имён в AXAML и успешная компиляция отдельно не выдаются за runtime PASS.

## 7. Performance checks

Метод: Release без debugger; один viewport 1600×1000, native Avalonia Windows backend; последовательные процессы before/after; 5 warm-up iterations, затем 30 для каждой из пяти операций (150 observations на вариант). Тяжёлые builds/restores не выполнялись одновременно с benchmark. Setup окна исключён из clock. Время измеряется до фактического актуального scene/attached layout и compositor commit, не до физического показа frame монитором. Принудительного RenderDesigner внутри измерения нет.

| Операция | Before median / P95, ms | After median / P95, ms | Process allocations median, KiB, before → after |
|---|---:|---:|---:|
| New Project | 112.66 / 143.35 | 121.04 / 194.31 | 12161.0 → 14086.7 |
| Add Form | 90.54 / 121.71 | 98.28 / 124.43 | 10691.4 → 12519.2 |
| Первый Toolbox Drop | 96.72 / 112.19 | 110.00 / 152.18 | 13846.9 → 17315.4 |
| Switch Form | 94.76 / 125.91 | 116.69 / 138.76 | 11857.5 → 14008.5 |
| Open legacy Canvas, 20 controls | 140.56 / 170.73 | 153.67 / 235.75 | 15339.0 → 15578.3 |

Здесь меняется семантика создаваемых форм: before — Canvas, after — Grid. Последний Open также уходит из default Canvas/Grid соответственно, это не microbenchmark идентичного предыдущего документа. Raw samples, model timings, GC deltas и counters сохранены в `performance-{before,after}.json`, сводки — `performance-summary.csv`, `performance-detail.csv`. Median — среднее 15/16 samples, P95 — nearest rank 29/30. CPU/GC/tiering/OS вариативность не устранена; повышенный P95 не скрыт и не объявлен доказанным GC-only эффектом.

Structural counters не выявили дополнительных ApplyDocument: **1** для New/Add/Switch/Open и **0** для первого Drop, одинаково before/after. Первый Drop создаёт **1 wrapper** в обоих вариантах; Grid использует существующий reuse path, offscreen native measurement (2 preview roots против 1), а Inspector создаёт 30 rows вместо 21 из-за native placement/sizing properties. Open Canvas создаёт 40 wrappers в обоих вариантах (прежние render passes), Inspector — 6 rows; дополнительных Canvas rebuild не появилось. Grid empty-form Inspector — 9 rows вместо Canvas 6.

Модельные median New/Add/Drop: 39.72/20.54/19.39 → 43.30/23.17/23.16 ms. Это измеренный расход прежнего native workflow и его Inspector при смене default, а не новая incremental/performance оптимизация. Задержки и allocations немного выросли; цель этого этапа не обещает устранение известных bottlenecks 300–500 controls. Большие формы и tails остаются отдельным backlog. Performance tests не имеют жёстких CI thresholds.

## 8. Build и regression

Baseline solution: 0 ошибок, 1 прежний NU1603 VSIX SDK.Analyzers; Native Grid 28/28, Preview Phase 2 28/28. До изменения production новый acceptance suite дал 7/26; после — **26/26**, включая реальные routed Drop и Preview controls.

Финальные результаты: `artifacts/layout-phase7b/`.

| Проверка | Результат |
|---|---|
| Solution Release | 0 ошибок, 1 прежний NU1603 |
| Smoke runner | 0 ошибок, 0 warnings |
| Phase 7B acceptance | **26/26** |
| Grid Root / Grid Editor / Grid Drop | **28/28**, **45/45**, **40/40** |
| Nested Layout / Pointer-Marquee / Undo history | **59/59**, **26/26**, **25/25** |
| Preview Phase 2 / Preview consistency | **28/28**, **32/32** |
| Generated four-form project | Restore/build/run PASS, navigation/resize/rows/Logic PASS |
| Broad Export/DesignerSurface/Inspector/Favorites/Multi Form/AXAML/VSIX/VsHost | **72/72**, полный повтор, exit 0 (`broad-final.log`) |
| Layout validation / Phase 7A.2 / Phase 7A UX | **55/55**, **13/13**, **15/15** |
| Stabilization audit | **25/25** в этом запуске |
| Preview Memory | **28/28**, в том числе 20 циклов каждого режима; 8 live SQL cases — SKIP |
| DataGrid offline / PostgreSQL offline | **18/18**, **20/20**; live SQL Server/PostgreSQL — SKIP |
| Optional Eremex cold/missing runtime/missing theme | 3 отдельных процесса PASS |
| Grid с Eremex TextEditor/DataGridControl | Existing Native Grid + broad vertical-slice scenarios PASS |
| ZIP source comparison | **16/16** entries byte/hash-equal запущенному folder export |

Первый broad запуск дал **70/72**: два старых Multi Form model-only fixtures выполняли UI Add Form без явного layout, затем Canvas Drop без native scene measurement. После смены default это стал Grid, и Drop закономерно отклонялся. Fixtures явно выбрали Canvas перед существующей UI-командой; их assertions geometry/selection/Inspector/DataGrid не менялись. Они отдельно прошли 2/2, затем полный повтор дал 72/72. Новые Grid tests используют actual arranged/routed Drop, а не фиктивный measurement fallback. Это исправление тестового setup, не подавление production ошибки или изменение DnD architecture.

В этом запуске generic Window negative control и inactive-form autosave прошли в составе 25/25. Их исторические intermittent failures из PreviewPhase2 не считаются исправленными новым default; assertions не ослаблены. Memory checks после 20 циклов каждого режима: old windows=0, active windows=0, clock observers=0. GC используется только существующими tests, production lifecycle не изменён.

Broad runner после PASS не удалил загруженный `ToolboxStandardItemsExport.dll` (прежний UnauthorizedAccessException в auto-cleanup). Функциональный 72/72 и cleanup failure фиксируются отдельно. Крупные owned outputs удалены после завершения процессов; raw logs, JSON/CSV, ZIP/source fixtures сохранены. Детали — `cleanup-summary.json`.

Новые Grid-default tests используют raw VM/factories/production New Project. Исторические export, Canvas pointer, provider и multi-form fixtures теперь явно задают v1 Canvas и explicit Canvas при добавлении своих прежних форм. Их old Canvas assertions не ослаблены: fixture перестала зависеть от неявного creation default. Старый `NativeGrid_CanvasCreation` проверяет explicit Canvas + reset selector к Grid. Тесты новой политики существуют отдельно, поэтому адаптация fixtures не маскирует работу Grid default.

Generated project сохраняет net6.0 и прежние packages. NETSDK1138 и CS8618 generated row DTO полей зафиксированы как warnings, не исправлялись unrelated изменениями Export Pipeline.

## 9. Eremex, данные и границы

Основной acceptance и standalone fixture используют registry стандартных controls без Eremex. Plugin architecture, NuGet, theme и лицензирование не менялись. Существующие Native Grid/Eremex и broad plugin checks переиспользуются отдельно. Optional vendor failures не являются основанием превращать Grid в Canvas.

В этом этапе live SQL Server/PostgreSQL не настраивались и пользовательские базы не использовались. Explicit local BindingSource в generated app не считается live database integration; offline/provider checks и live SKIP нужно читать раздельно.

## 10. Изменённые файлы

Production: `Services/ProjectDocumentService.cs`, `Services/ProjectWorkspaceService.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/MainWindowViewModel.NativeLayout.cs`, `Views/MainWindow.axaml`.

Tests, каталог `smoke-tests/FormDesigner.ExportSmokeTests`:

* Новые `Program.GridFirst.cs`, `Program.GridFirst.Benchmark.cs`, `Program.GridFirst.Export.cs`.
* `Program.cs`: dispatch, explicit Canvas baseline helper и old Multi Form fixture inputs.
* Explicit Canvas creation arguments: `Program.DataGridPreviewContracts.cs`, `Program.DataGridPreviewRegression.cs`, `Program.DesignerPointer.cs`, `Program.GridDrop.Pointer.cs`, `Program.GridEditor.cs`, `Program.Layout7.cs`, `Program.LayoutValidation.Properties.cs`, `Program.LayoutValidation.cs`, `Program.NativeGrid.cs`, `Program.NestedDrop.Export.cs`, `Program.NestedDrop.Pointer.cs`, `Program.PostgreSql.cs`, `Program.StandardItems.cs`.
* `smoke-tests/LayoutGrid/ProbeGridFirst.ps1`, `GridFirstRuntime.cs.txt`; этот документ. Полный diff list — `changed-files.txt` в evidence.

## 11. Ручная проверка и решение

Физический Toolbox DoDragDrop, Alt+Tab/Escape/release за пределами окна, DPI 125/150/200%, перенос между мониторами и интерактивный Visual Studio не объявляются пройденными raw/routed tests. Проверить selector и его разовый Canvas choice на реальном рабочем месте; открыть пользовательский старый Canvas, изменить координаты, сохранить/открыть и экспортировать; повторить новый Grid [1,1], selection/Inspector/Favorites, Undo/Redo; проверить узкое окно, resize DataGrid и навигацию экспортированного ZIP.

**Grid-first Default: READY WITH CONDITIONS.** Grid включён для новых проектов/форм, explicit Canvas доступен и не наследуется скрыто. Старые JSON и imported AXAML не мигрируются; Designer/оба Preview/Export и отдельный generated runtime сохраняют выбранную семантику.

Условия: physical DnD/DPI/interactive Visual Studio и platform matrix остаются ручными; live БД здесь не проверены; известные performance tails больших форм и историческая generic Window/UIA нестабильность не объявляются устранёнными сменой default. Новая Layout Phase не начиналась.
