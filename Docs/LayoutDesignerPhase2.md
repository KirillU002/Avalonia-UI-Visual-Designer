# Layout Designer Phase 2 — настоящий Grid Root

Дата: 30 октября 2026 года. Основа реализации — [LayoutDesignerArchitecture.md](LayoutDesignerArchitecture.md), анализ checkout `c88fdd1`. Phase 3 не начиналась.

## 1. Результат и границы

Добавлена отдельная семантика новых Grid-форм: настоящий Avalonia Grid рассчитывает геометрию в Designer, Legacy Preview и AXAML Preview, а экспорт сохраняет эту структуру. Canvas остаётся значением по умолчанию при создании формы. Старые JSON и существующие экспериментальные layout-документы автоматически не конвертируются.

Служебный Canvas редактора остался: он рисует фон, selection, resize handles и marquee. Это не пользовательский root. Экспорт Grid-формы создаёт `Window → Grid`, без скрытого `RootCanvas`.

Phase 2 предоставляет минимальное размещение: новый элемент в Grid получает ячейку 0/0. Дальнейшее размещение задаётся в Inspector. Визуальное определение произвольной ячейки, подсветка drop target и drag между ячейками относятся к Phase 4.

## 2. Модели и создание формы

Переиспользованы `DesignerDocumentFileModel`, `DesignerControlFileModel`, `DesignControlModel`, `DesignerFormDocument`, `DesignerDocumentSession` и существующий `ProjectDocumentService`. Отдельного root control с новым Id нет: root по-прежнему описывают `SurfaceLayoutMode`, `SurfaceGridRowDefinitions` и `SurfaceGridColumnDefinitions`. `ParentId = ""` означает дочерний элемент root, а Id контейнера — вложенный элемент.

В существующей панели создания формы добавлен выбор:

- **Canvas — свободное размещение**: прежняя фабрика и прежняя геометрия, вариант по умолчанию.
- **Grid — адаптивный**: `SurfaceLayoutMode = Grid`, одна строка `*`, один столбец `*`, spacing 0 и новая layout-семантика.

Старые overloads `CreateNewForm()` и `AddForm(project, baseName)` сохранены и создают Canvas. Новая перегрузка принимает тип root. UI использует существующую команду создания формы, а не параллельный workflow.

Для новых элементов native формы режимы размеров первоначально `Auto`. Горизонтальное растягивание задаёт существующий HorizontalAlignment; DataGrid и контейнеры также получают VerticalAlignment=Stretch. Это позволяет сочетать заголовок в Auto-строке с растягиваемым DataGrid в `*`-строке.

## 3. JSON и совместимость

Новые Grid-формы записываются с `Version = "2.4"`, `LayoutSemanticsVersion = 2`. Старые документы без нового поля получают значение 1 и остаются на прежнем пути размещения. Установленный в старом проекте экспериментальный Grid сам по себе не включает новую семантику.

В control DTO добавлены `WidthMode` и `HeightMode` со значениями `Fixed` или `Auto`. Отсутствующее поле означает Fixed. Числовые Width/Height сохраняются как fixed value/design hint; `NaN` в JSON не записывается. Новые поля проходят Clone, snapshot, Undo/Redo, Preview и вторичные формы Export.

Сохраняются definitions, ParentId, Grid.Row/Column/Spans, Margin и alignment. Неизвестная версия семантики, несуществующий родитель или цикл ParentId отклоняются до применения документа. Проверены отдельный document JSON и workspace с Canvas/Grid, включая запись файла и повторное открытие.

Обратная совместимость означает чтение старых файлов текущим Designer. Чтение новой семантики старой версией Designer не обещается.

## 4. DesignerSurface и настоящий Measure/Arrange

Существующий `AxamlLayoutProjection` расширен partial-файлом `AxamlLayoutProjection.Json.cs`. Адаптер строит дерево настоящих Avalonia panels из существующих моделей и использует тот же MeasurementRoot и offscreen-подготовку, что AXAML projection. Расчёт Auto, star, фиксированных tracks, margin, alignment и spans выполняет Avalonia, собственного layout engine нет.

`JsonLayoutTree.Measure` возвращает временные bounds относительно родителя. Они используются для отрисовки, selection и определения контейнера. Bounds не записываются вместо авторских Width/Height или X/Y. При изменении размера формы выполняется новое Measure/Arrange.

В служебном Canvas содержимое рисуется в измеренных рамках. Selection frame — отдельный sibling без hit-testing; его толщина не меняет геометрию контрола. Margin не применяется повторно к отображаемому preview. Для контейнеров убрана ложная иллюстрация равных ячеек: реальная геометрия берётся из измерения.

Рекурсивный адаптер поддерживает существующие Grid, Stack, Flex/Wrap, Group и Border. Для неподдерживаемого контейнера с дочерними controls выводится явная ошибка вместо молчаливого выравнивания дерева. Полноценный Nested Layout Designer и новые типы контейнеров в Phase 2 не добавлялись.

## 5. Добавление, selection и resize

Добавление стандартного Button, TextBox или TextBlock использует существующий Toolbox pipeline. Для Grid-parent устанавливаются Row=0, Column=0, spans=1 и нейтральные X/Y=0. Эти X/Y не являются Canvas placement и не экспортируются как Canvas.Left/Top. В Status объясняется выбранная ячейка и возможность изменить placement через Inspector.

Координатный drag для дочерних элементов managed layout явно ограничен: клик выбирает элемент и показывает объяснение. Он не начинает Move и не включает Marquee. Свободное перемещение в настоящем Canvas-parent остаётся на существующем пути. Существующая state machine pointer interaction, capture, Escape и сброс при смене формы не заменялись.

Resize начинает отсчёт от измеренного размера, переводит Width/Height в Fixed и меняет только размеры. ParentId, Grid placement и тип root сохраняются. Возврат Auto доступен в Inspector. Полноценного track resize, cell snapping и drag placement нет.

## 6. Property Inspector и definitions

Использован прежний Property Inspector с прежним механизмом избранных свойств. В обычном режиме доступны:

- Grid.Row, Grid.Column, Grid.RowSpan, Grid.ColumnSpan для Grid-parent;
- Width, Height, Width mode, Height mode;
- Margin, HorizontalAlignment, VerticalAlignment;
- RowDefinitions/ColumnDefinitions при выборе самой формы.

X/Y скрыты для управляемого layout. Правка числового Width/Height включает Fixed даже при вводе того же числового значения. Неверный Margin не повреждает модель и даёт сообщение в Status.

Grid toolbar открывает существующую вкладку Layout с редактором строк/колонок. Сохранены add/delete/reorder и Auto/Star/Fixed. Для native формы поддержаны нулевые и дробные значения GridLength без прежнего ограничения минимум 1; legacy-ветка не меняет семантику. Строки definitions в Inspector валидируются через Avalonia parser. Root Grid нельзя случайно переключить в Absolute через старый selector: для Canvas создаётся другая форма.

## 7. Preview

**Legacy Preview** для семантики 2 размещает построенное native дерево непосредственно в `PreviewSurfaceBorder`. Использует существующие factories runtime controls, DataGrid loader, BindingSource, visibility/enabled state и Logic. При resize меняется arrangement уже существующих controls, а не пересоздаётся Canvas с рассчитанными координатами.

**AXAML Preview** использует существующий export/runtime loading. Обнаружена отдельная причина фиксированного размера: экспортируемый Window превращается в UserControl, который наследовал Window.Width/Height. Для native формы добавлена опция удаления этих двух размеров у preview UserControl; production Window сохраняет начальный размер. Старый overload и поведение Canvas Preview сохранены.

Проверены оба режима на настоящих Avalonia окнах: Grid root, рост DataGrid при resize, строки DLL BindingSource и Eremex resize. DLL fixture возвращает 12 различных записей; проверены ItemsSource и видимые значения `Real order 001`/`002`. Это проверка реального DLL provider, а не live SQL/PostgreSQL integration.

## 8. Export и runtime

Существующие AXAML/C# export, ViewModel generation и Multi Form pipeline сохранены. Для native формы:

- root — растягиваемый Grid с исходными definitions, без фиксированных Width/Height;
- Auto children не получают Width/Height, Fixed children получают явные размеры;
- attached Grid placement, margin и alignment генерируются непосредственно из модели;
- Canvas.Left/Top выводятся только для настоящего Absolute-parent;
- Responsive Export Mode не запускает Canvas→StackPanel эвристику над авторским Grid;
- вторичные формы восстанавливают собственные layout semantics и size modes.

В смешанном проекте сохранено существующее правило startup form: первая форма проекта остаётся MainWindow, Grid может быть вторичной формой. Изменение startup policy не входило в Phase 2. Отдельный сценарий экспорта single Grid проверяет именно фактический `MainWindow.axaml`, а не только текст активной формы.

Три новых экспортируемых проекта проходят restore/build: Grid, Canvas+Grid и Grid+Eremex. Runtime probe запускает созданный приложением MainWindow на установленном .NET 6.0.36, показывает настоящее окно за пределами экрана и меняет его размеры. Временная test-only замена entry point восстанавливается в finally; исходники экспортируемого приложения после проверки не содержат probe.

Зафиксирован результат:

```text
LAYOUT_RUNTIME_OK runtime=6.0.36 before=570, 319 after=810, 439 root=Grid
```

Проверено наличие настоящих TextBox, Button, DataGrid, отсутствие фиксированного размера у root и увеличение DataGrid на 240×120 вместе с окном. Это runtime Measure/Arrange в созданном приложении, а не вывод только из успешной компиляции.

## 9. Eremex

Конкретные Canvas-зависимости найдены в `EremexTextEditorDescriptor.ExportAxaml` и `EremexDataGridControlDescriptor.ExportAxaml`: descriptors сами генерировали Width/Height и Canvas.Left/Top.

Для них хватило небольшого адаптера: host передаёт optional placement через существующий словарный контракт `BuiltInProperties`. При наличии native placement два descriptor заменяют свои positional атрибуты на host-атрибуты и не дублируют Margin. В Canvas optional payload отсутствует и сохраняется прежняя генерация. Публичный plugin contract, Eremex package и VSIX/IPC не изменены.

Проверены экспорт TextEditor, экспорт трёх DataGridControl, сгенерированный Eremex project build и resize DataGridControl в обоих Preview. Для будущих сторонних descriptors, которые жёстко генерируют Canvas coordinates, автоматическая совместимость не обещается: им потребуется такой же placement adapter. Скрытого Canvas обхода нет.

## 10. Тесты и результаты

Результаты финального запуска и команды приведены ниже; логи сохраняются в `artifacts/layout-phase2/`.

| Проверка | Результат |
| --- | --- |
| FormDesigner.sln, включая VsHost/VSIX | 0 ошибок, 1 прежнее предупреждение NU1603 |
| Native Grid checks | 28/28 |
| Новые generated project scenarios | 3/3, restore/build |
| Existing layout/DesignerSurface/Inspector/Multi Form/AXAML/Eremex scenarios | 30/30, включая generated project build |
| Pointer/marquee regression | 26/26 |
| DataGrid offline contracts | 18/18; live SQL Server — SKIP |
| PostgreSQL offline contracts | 20/20; live PostgreSQL — SKIP |
| Generated application resize runtime | PASS, .NET 6.0.36, 570×319 → 810×439 |

Новые native checks покрывают создание Canvas/Grid через фабрику и UI, Button/TextBox/TextBlock, document/workspace JSON, старый JSON, Inspector, определения и Undo/Redo, нативные margin/alignment/spans, Designer resize/selection, оба Preview resize, responsive export bypass, смешанные формы, nested Grid, DLL data binding, Eremex placement/resize, дробные tracks, неверный Margin, файл Save/Load, неизвестную семантику и реальный pointer routing selection/resize/marquee.

В песочнице полный solution build остановился в VSIX packager на `IsolatedStorageException`; после разрешённого повторного запуска штатная упаковка `.vsix` завершилась успешно. Это не ошибка компиляции нового layout кода. Generated net6 проекты сохранили существующий TFM; отмечены NETSDK1138 и NU1900 из-за недоступного vulnerability feed. В дополнительном массовом smoke прогоне `NuGetAudit=false` задаётся только окружением процесса, чтобы не ждать недоступный feed на каждом restore. NuGet versions и файлы проектов не изменены.

Baseline 30/30 объединяет три завершённых сценария из `regression.log` и оставшиеся 27 из `regression-remaining.log`; первый процесс остановлен между проверками для повторного запуска остатка без задержек audit feed. Итоговый список: `regression-summary.log`. Smoke runner сообщил о file lock при автоматической очистке одного успешного run-каталога; результаты тестов и сборок успешны, остаточные артефакты не удалялись принудительно.

### Воспроизведение

```powershell
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'Codex-AvaloniaReview-dotnet'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build .\FormDesigner.sln --no-restore -m:1
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --native-grid-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --designer-pointer-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --datagrid-preview-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --postgres-checks
$env:SMOKE_SCENARIO_FILTER = 'NativeGridPhase2'
$env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS = '1'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\layout-phase2\export
$run = Get-Content .\artifacts\layout-phase2\export\latest-run.txt
& .\smoke-tests\LayoutGrid\ProbeGeneratedLayout.ps1 -ProjectPath (Join-Path $run 'NativeGridPhase2Export')
```

## 11. Изменённые файлы

| Файлы | Назначение |
| --- | --- |
| `Models/DesignerDocumentFileModel.cs`, `Models/DesignControlModel.cs` | Версия семантики и Auto/Fixed modes |
| `Services/ProjectDocumentService.cs` | Создание Grid-формы через прежнюю фабрику |
| `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`, новый `AxamlLayoutProjection.Json.cs` | Общий Avalonia MeasurementRoot и JSON tree adapter |
| `ViewModels/MainWindowViewModel.cs`, новый `MainWindowViewModel.NativeLayout.cs` | UI choice, snapshots, placement, Inspector, native export |
| `ViewModels/LayoutDefinitionEditorItem.cs` | Native GridLength в существующем редакторе |
| новый `ViewModels/NativeLayoutExportNode.cs` | Optional placement поверх существующего plugin dictionary |
| `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, новый `MainWindow.NativeLayout.cs` | Выбор root, измеренная геометрия, selection decoration, безопасные drag/resize |
| `Views/PreviewWindow.axaml.cs`, новый `PreviewWindow.NativeLayout.cs` | Настоящее layout tree в Legacy Preview |
| `Services/GeneratedAxamlService.cs` | Stretch runtime root AXAML Preview только для native Grid |
| `Plugins/EremexDesignerPlugin/Descriptors/EremexTextEditorDescriptor.cs`, `EremexDataGridControlDescriptor.cs` | Native placement без изменения Canvas path |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новый `Program.NativeGrid.cs` | Native и generated-project regression tests |
| новый `smoke-tests/LayoutGrid/ProbeGeneratedLayout.ps1` | Воспроизводимая runtime resize проверка экспортируемого приложения |
| новый `Docs/LayoutDesignerPhase2.md` | Этот отчёт |

NuGet-пакеты, TargetFramework, SQL Server/PostgreSQL services, AXAML source patching, глобальные стили и VSIX/IPC production-код не изменены. Документ Phase 1 использован как исходный план и не переписывался.

## 12. Ограничения и ручная проверка

1. Новые элементы в Grid намеренно перекрываются в ячейке 0/0 до настройки placement. Автоматического распределения и сложного cell hit-testing нет. Grid по умолчанию пока не включён.
2. Для managed layout координатный drag ограничен; положение меняется через Inspector. Resize сейчас задаёт оба размера Fixed и использует существующие ограничения минимальных размеров/жеста. Cell-aware snapping и отдельные track handles отложены.
3. Поддержка nested native containers ограничена уже существующими видами модели; нет нового DockPanel workflow, сложных templates и произвольных plugin containers.
4. Ручная проверка нужна для физического Toolbox DnD из всех docking-панелей, DPI/zoom, длинных drag вне окна, визуального оформления handles и Eremex на разных темах. Автоматические pointer tests используют настоящий Avalonia raw input routing, но не заменяют проверку аппаратной мышью.
5. Проверены сборки VsHost/VSIX и host-neutral surface tests. Интерактивное открытие нового Grid-документа внутри реального Visual Studio не проводилось.
6. Live SQL Server/PostgreSQL в рамках Phase 2 не запускались. Проверки DLL и offline data contracts не объявляются реальной интеграцией с БД. Production data pipeline не менялся.
7. Не проводилась матрица Windows/Linux/macOS и всех сочетаний themes/window modes. Runtime exported resize проверен на текущем Windows checkout.

После Phase 2 работа остановлена. Визуальный Grid Editor следующего этапа и расширенное cell placement автоматически не запускаются.
