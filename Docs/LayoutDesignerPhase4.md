# Layout Designer Phase 4 — Grid Drag & Drop и размещение по ячейкам

Дата проверки: 31.10.2026. Основа: `LayoutDesignerArchitecture.md`, `LayoutDesignerPhase2.md`, `LayoutDesignerPhase3.md`, код на базе коммита `a340118`.

## 1. Результат и границы этапа

Для JSON-форм с `LayoutSemanticsVersion = 2` реализовано визуальное добавление компонентов из Toolbox в конкретную ячейку Grid, перемещение существующего компонента между ячейками и вложенными Grid. Наведение показывает целевой Grid, область с учётом spans, предварительные bounds компонента и предупреждение о перекрытии. Placement фиксируется при Drop/PointerReleased одной Undo-операцией.

Designer использует прежний служебный Canvas для overlays. Пользовательская структура остаётся настоящим Grid. Дочерние элементы получают ParentId и Grid.Row/Column/Spans; X/Y обнуляются и не экспортируются как Canvas.Left/Canvas.Top.

Canvas остаётся вариантом по умолчанию при создании формы. Старые формы и экспериментальные layouts v1 автоматически не конвертируются. Source-backed AXAML Grid Drag & Drop явно недоступен: исходный документ не регенерируется.

## 2. Исследованный существующий pipeline

До Phase 4 Toolbox передавал тип компонента и идентификатор формы через Avalonia DragDrop. `DesignerCanvas_DragOver/Drop` определяли контейнер и использовали координаты Canvas. `CreateControlCore` переводил их в локальные координаты для абсолютного parent. Для native Grid Phase 2 назначала ячейку 0/0; обычное перемещение Grid child было отключено с предложением использовать Inspector.

Оставались Canvas-specific участки:

- свободное движение selection roots и snap по X/Y;
- `ReparentControl` с абсолютной точкой отпускания;
- равномерное деление прямоугольника в старом `RenderGridCellDropHint`;
- создание Toolbox control до применения дальнейшего placement;
- pointer capture движущимся wrapper, который может заменяться при render.

Новый native Grid путь подключён перед этими обработчиками. Canvas/v1 сохраняют прежний путь. Определения строк/колонок, native Measure/Arrange, document session, selection, Inspector и Undo из предыдущих этапов переиспользованы. Document Model, Preview loaders, Export Pipeline и layout engine не переписывались.

## 3. Grid Cell Hit Testing

`ResolveGridDropTarget` использует `NativeGridMeasurements`, полученные существующим `AxamlLayoutProjection.JsonLayoutTree` после реальных Avalonia Measure/Arrange. Размеры tracks берутся из фактических измерений Auto/Star/Pixel. Равномерного деления Grid нет.

Точка приводится существующим `GetDesignCanvasPosition` к DIP формы. Zoom и преобразования visual tree учитываются до определения track; RenderScaling фиксируется в диагностике. Отдельный пересчёт DPI поверх Avalonia не применяется.

Track выбирается по накопленным фактическим размерам. Левая/верхняя граница включена, правая/нижняя исключена. Нулевой track не может стать mouse target. Область, которую не занимают фактически измеренные tracks, не принимается за последнюю ячейку. Для collapsed/нулевых tracks остаётся Grid Definition Editor.

Measurement связывается с активной сессией и fingerprint геометрии документа. Перед commit повторно проверяются parent, cell, spans и measurement. При изменении definitions, структуры/placement или формы старая подсветка не может примениться к новому документу. Пустой root допускает первоначальное native measurement без фиктивных controls.

## 4. Drop Targets и вложенные Grid

Среди measured Grid под указателем выбирается наиболее глубокий видимый контейнер, далее учитывается визуальный Z-order. Root представлен пустым ParentId, вложенный Grid — существующим Id control. Учитываются измеренная внутренняя область, Margin/обрамление контейнера и clipping его родителей в Designer.

Скрытые контейнеры не выбираются. Заблокированный Grid либо заблокированный ancestor дают недопустимую цель с объяснением. Перенос Grid в себя или собственного потомка запрещён до изменения модели. Cyclic ParentId не создаётся.

При reparent меняются существующие ParentId и placement того же объекта. ControlId, имя, свойства, bindings, Logic, размерные режимы, Margin и alignment сохраняются. Другие controls автоматически не перемещаются.

## 5. Подсветка и предварительная геометрия

В существующем `GuideOverlayCanvas` рисуются:

- контур целевого Grid;
- область целевой ячейки/занимаемых spans;
- пунктирная рамка фактического предварительного положения компонента;
- компактная подпись с индексами, spans или причиной запрета.

Допустимая цель синяя, фактическое перекрытие — оранжевое, запрет — красный. Overlay и его элементы не участвуют в hit testing, Document Model, JSON, Preview или Export.

Предварительные bounds получаются дополнительным native Measure/Arrange копии существующего дерева с предложенным placement. При копировании явно сохраняются Id: обычный `DesignControlModel.Clone` предназначен для создания новых элементов и генерирует новые Id. Авторская модель во время hover не меняется.

Предупреждение о перекрытии основано на пересечении реальных projected rectangles видимых соседей с учётом clipping/opacity. Сам факт общей ячейки не считается перекрытием: например, узкие элементы с Left/Right alignment могут не пересекаться. Перекрытие разрешено и не приводит к автоматической перестановке соседей.

Проекция кэшируется для одной цели/снимка геометрии. Обычное движение внутри той же ячейки не пересоздаёт тяжёлый preview и не создаёт диагностическую запись на каждый пиксель.

## 6. Toolbox, перемещение и spans

Toolbox использует прежний `DragDrop.DoDragDrop`. В payload добавлен внутренний идентификатор жеста. Проверяются тип из действующего registry, исходная форма, текущий gesture и свежая подсветка. Старый Drop после Escape, смены формы или начала нового жеста отклоняется. Отсутствующий plugin control не превращается молча в fallback placeholder.

`CreateControlCore` получает окончательный Grid target и применяет его до добавления элемента в Controls. Не возникает промежуточного элемента в 0/0 с последующей второй Undo-записью.

При перемещении сохраняются RowSpan/ColumnSpan. Проверяется, что весь span помещается от выбранной ячейки до конца целевого Grid. При переполнении Drop отменяется с русским объяснением и предложением выбрать другую ячейку или явно изменить span в Inspector. Не производится silent clamp.

Программный `CreateControl` для fixtures/команд сохраняет детерминированное размещение 0/0. Визуальный Toolbox Drag & Drop использует фактическую выбранную ячейку. Export-only fixtures без отрисованного Designer создают исходные controls программно; полноценные routed Toolbox проверки выполняются отдельно в окне Designer.

## 7. Selection, Inspector и pointer capture

Существующая interaction state machine расширена двумя состояниями: `GridControlPending` и `MovingGridControl`. При нажатии выбирается control и захватывается стабильный DesignerCanvas. Только после движения более существующего порога 4 DIP начинается Grid drag. Обычный/повторный клик не начинает Marquee и не меняет placement.

Grid move, Marquee, Canvas move, обычный resize и Grid Definition resize не запускаются одновременно. Escape, PointerCaptureLost, выход за допустимую область с отпусканием, смена формы и закрытие окна завершают/отменяют временный drag. До commit исходная модель остаётся прежней, поэтому отмена не требует восстановления частично изменённых координат.

Toolbox сохраняет системное управление capture операции Avalonia DragDrop. Вложенный DragLeave при переходе между дочерними visuals больше не завершает native Toolbox gesture; настоящий выход за форму завершает подсветку.

После успешного Drop control остаётся selected. Inspector обновляется существующим механизмом, показывает Row/Column/Spans и read-only ParentId. Полный ApplyDocument не вызывается. Существующий Inspector, Favorites и описания свойств не заменялись.

Обычный клик не вызывает дополнительный синхронный RenderDesigner при release: такой rebuild заменял ruler visuals до обновления input scene и ломал pointer regression Grid Editor. Используется существующее отложенное обновление selection. После реального перемещения render выполняется.

Перенос нескольких selected controls как группы в этой фазе явно ограничен: отображается сообщение, multi-selection сохраняется, один случайный элемент не перемещается. Стандартные selection, Ctrl/Shift и Marquee не заменяются новой системой.

## 8. Undo/Redo и JSON

Создание и move используют существующие Grid Editor Undo boundaries. ParentId, row/column и spans входят в одну транзакцию. Undo возвращает прежний parent/placement, Redo восстанавливает новое с тем же ControlId. Hover, invalid target и cancel не создают изменений документа.

Формат JSON не менялся: версия native-документа 2.4, LayoutSemanticsVersion=2. Сохраняются существующие definitions, control Id, ParentId, placement и порядок Controls. Transient target, measurements, ghost и gesture token в JSON не попадают. Проверен файл Save → новый ViewModel → Load → GenerateXaml.

## 9. Preview, Export, DataGrid и Eremex

Legacy и AXAML Preview используют сохранённую layout structure и existing native pipeline. Новая логика не подменяет Grid на Canvas. В обоих режимах тест находит настоящий Avalonia Button после move, проверяет attached row/column и фактические bounds. Для AXAML дополнительно проверено, что режим действительно загрузился без fallback.

AXAML/C# generated project pipeline не изменялся. Экспорт сохраняет настоящий root Grid и Grid.Row/Column/Spans. Проверены отсутствие Canvas.Left/Top у Grid children, отсутствие designer overlays, generated files, restore и build.

Стандартный DataGrid и Eremex DataGridControl проходят настоящий routed Toolbox Drop и raw pointer move через рамку компонента. Внутренние жесты редактирования/resize заголовков DataGrid не переназначаются. Eremex descriptors не менялись: экспорт использует исправления Phase 2. Generated Eremex scenario с перемещением в другую колонку собирается.

Responsive Export не преобразует native Grid повторно. Проверены прежние Canvas responsive modes и смешанный Canvas/Grid multi-form export.

## 10. AXAML Round-trip и совместимость Canvas

Source AXAML Grid placement/reparent в этом этапе read-only: операция отвергается по фактическим `AxamlLayoutBounds`, а не по старым Width/Height hints модели. Routed Drop проверяет, что число controls и minimal patch не изменились. Patch writer и source mapping не менялись; comments, unknown attributes, raw content, namespaces/styles/bindings покрываются прежними round-trip сценариями.

Canvas продолжает свободно размещать и двигать controls по X/Y. Изменения не включают native семантику для старого JSON. Проверены старые drag/resize/marquee, selection, независимые form sessions, Inspector/Favorites и Undo/Redo.

## 11. Diagnostics

Добавлены события:

`GRID_DRAG_START`, `GRID_DROP_TARGET_RESOLVED`, `GRID_CELL_HIT_TEST`, `GRID_DROP_PREVIEW`, `GRID_DROP_COMMITTED`, `GRID_DROP_CANCELLED`, `GRID_REPARENT_START`, `GRID_REPARENT_SUCCESS`, `GRID_REPARENT_FAILED`.

Структурированная запись содержит DocumentId исходного жеста, ControlId, OldParentId/NewParentId, OldRow/Column, NewRow/Column, Zoom, Dpi (Avalonia RenderScaling) и Reason. Для нового Toolbox control ControlId известен после создания; до него поле пустое. Смена формы не подменяет исходный DocumentId отменённого move.

Локальная коллекция ограничена 512 записями. Hit/target/preview записываются при смене цели, валидности или overlap state; PointerMoved внутри той же цели не создаёт поток логов. Connection strings, queries и секреты не добавляются в эти события.

## 12. Проверки

Все результаты ниже получены на текущем коде, а не перенесены из отчётов предыдущих фаз.

| Проверка | Результат |
| --- | --- |
| `FormDesigner.sln --no-restore -m:1` | 0 ошибок; одно прежнее предупреждение NU1603 в VSIX SDK analyzers |
| Core и smoke runner build | 0 ошибок, 0 предупреждений |
| Новые Grid Drop checks | 40/40, включая nested Toolbox и visual span rejection |
| Grid Editor regressions | 45/45 |
| Native Grid Phase 2 regressions | 28/28 |
| Designer pointer / Marquee / Canvas drag / resize | 26/26 |
| DataGrid offline contracts | 18/18; live SQL Server skipped |
| PostgreSQL offline contracts | 20/20; live PostgreSQL skipped |
| Native/Grid Editor/Grid Drop generated export scenarios | 6/6; restore и build каждого проекта |
| Общая regression matrix | 32/32: sessions, Inspector/Favorites, Multi Form, Undo/Redo, AXAML import/round-trip, Canvas/Responsive export, Eremex |
| Generated application runtime | Успешный запуск native Avalonia окна, placement и resize проверены |

Новые tests проверяют реальные Pixel/Auto/Star tracks, zero/unallocated regions, root/nested Toolbox Drop, nested reparent, cycles, locks/visibility, spans, overlap и отсутствие ложного overlap, Id/properties/bindings/Logic, Undo/Redo, JSON, selection/Inspector, Escape/capture/outside/form/close, stale gesture/geometry, source AXAML read-only, throttled diagnostics, zoom, стандартный и Eremex DataGrid.

Pointer tests используют настоящий Avalonia visual tree и raw pointer routing. Toolbox events подаются как routed DragOver/Drop через реальный DesignerCanvas. Это проверяет обработчики и геометрию, но не заменяет физический системный DoDragDrop мышью между окнами.

Runtime probe запускает **сгенерированное** приложение, а не Designer Preview. Реальные результаты:

```text
GRID_DROP_RUNTIME_OK id=NativeButton row=1 column=1 bounds=420, 24, 71, 41
LAYOUT_RUNTIME_OK runtime=6.0.36 before=570, 319 after=810, 439 root=Grid
```

Окно показывается вне рабочего экрана, получает native layout, увеличивается на 240×120 DIP и закрывается. Проверены настоящий root Grid, отсутствие фиксированных Width/Height у root, существование TextBox/DataGrid/Button, parent и attached placement перемещённой кнопки. Probe временно меняет только generated artifact Program.cs, восстанавливает его байты и пересобирает обычное приложение. Generated projects сохраняют прежний net6.0; существующее предупреждение NETSDK1138 не устранялось сменой TFM.

Логи и визуальная проверка: `artifacts/layout-phase4/solution-build.log`, `grid-drop-final.log`, `grid-editor-final.log`, `native-grid-final.log`, `designer-pointer-final.log`, `export.log`, `regression.log`, `generated-runtime.log`, `grid-drop-preview.png`. Снимок native Designer во время drag просмотрен: target [1,1], границы Grid и отдельная ghost рамка соответствуют измеренным tracks.

В массовых generated builds `NuGetAudit=false` задан только окружением процесса, чтобы не ждать недоступный vulnerability feed. Версии зависимостей и project manifests не менялись.

### Воспроизведение

```powershell
dotnet build .\FormDesigner.sln --no-restore -m:1 --verbosity minimal
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --grid-drop-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --grid-editor-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --native-grid-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --designer-pointer-checks
$env:SMOKE_SCENARIO_FILTER = 'GridDropPhase4Export'
$env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS = '1'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\layout-phase4\export
$run = Get-Content .\artifacts\layout-phase4\export\latest-run.txt
& .\smoke-tests\LayoutGrid\ProbeGeneratedLayout.ps1 -ProjectPath (Join-Path $run 'GridDropPhase4Export') -VerifyGridDropPlacement
```

## 13. Изменённые файлы

| Файл | Назначение |
| --- | --- |
| Новый `Models/GridDropTarget.cs` | Transient target и структурированная диагностика |
| `Models/DesignerInteraction.cs` | Pending/moving Grid состояния |
| Новый `ViewModels/MainWindowViewModel.GridDrop.cs` | Native hit test, preview projection, валидация, atomic commit |
| `ViewModels/MainWindowViewModel.cs` | Toolbox/reparent dispatch и применение final placement до Controls.Add |
| `ViewModels/MainWindowViewModel.NativeLayout.cs` | Read-only ParentId в существующем Inspector |
| `ViewModels/MainWindowViewModel.GridEditor.cs` | Сброс measurement fingerprint при смене документа |
| Новый `Views/MainWindow.GridDrop.cs` | Pointer/Toolbox lifecycle, target overlay, cancellation, diagnostics |
| `Views/MainWindow.NativeLayout.cs` | Связь actual measurement со snapshot геометрии |
| `Views/MainWindow.axaml.cs` | Routing native Grid и source read-only guard |
| `Views/MainWindow.DesignerInteraction.cs` | Общий cancel/capture lifecycle и native DragLeave |
| Новые `Program.GridDrop.cs`, `Program.GridDrop.Pointer.cs` в smoke runner | Model, actual layout, routed input, Preview и export checks |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` | Регистрация checks/export scenarios, корректное построение export-only native fixture |
| `smoke-tests/LayoutGrid/ProbeGeneratedLayout.ps1` | Опциональная проверка placement перемещённой кнопки в generated runtime |
| Новый `Docs/LayoutDesignerPhase4.md` | Этот отчёт |

Не менялись NuGet, TargetFramework, глобальная тема, Toolbox descriptors, SQL Server/PostgreSQL, VSIX/IPC, Preview/Export production-код и AXAML patch writer.

## 14. Ограничения и ручная проверка

1. Перенос нескольких selected controls как группы явно недоступен; selection сохраняется. Поэлементный drag поддержан.
2. Grid cell drag реализован для native JSON v2. Source AXAML Grid остаётся read-only. Смена Grid↔Canvas parent и полноценный StackPanel/DockPanel/WrapPanel placement не добавлены.
3. Нулевой Auto/Star track нельзя выбрать указателем: сначала задайте размер через Grid Editor. Spans не меняются drag-операцией автоматически.
4. Тяжёлый nested preview строится при смене target, а не на каждый PointerMoved; stress test сотен plugin controls не проводился.
5. Raw/routed tests прошли при native RenderScaling=1 и Designer zoom 0.75/1.5. Физические мониторы с DPI 125/150/200%, перенос окна между мониторами и аппаратная мышь требуют ручного smoke.
6. Вручную проверить системный Toolbox DoDragDrop, частый вход/выход за Canvas, отпускание за пределами окна, Alt+Tab/Escape, scrolling и resize окна во время drag, маленькие nested Grid, перекрывающиеся controls и читаемость длинных предупреждений.
7. Проверены сборки VsHost/VSIX, но интерактивный запуск внутри Visual Studio не проводился. Реальные SQL Server/PostgreSQL в этой фазе не использовались; offline tests не заявляются как live integration.
8. Generated Eremex export собран и Eremex Designer input/Preview покрыты regression tests; отдельный интерактивный runtime-сеанс экспортированного Eremex окна с пользовательскими данными не проводился.

Phase 4 ограничена визуальным Grid placement. Phase 5 не начата.
