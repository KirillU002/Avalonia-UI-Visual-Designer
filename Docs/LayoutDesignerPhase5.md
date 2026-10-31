# Layout Designer Phase 5 — вложенные контейнеры и перенос компонентов

Дата: 31.10.2026. Baseline: `ad77b9f` (Preview memory fix). Рабочее дерево перед началом было чистым. Изучены Architecture, Phase 2–4, StabilizationAudit и PreviewMemoryLeakFix.

## 1. Основа и область изменений

Расширен существующий native JSON pipeline (`LayoutSemanticsVersion=2`), а не создана вторая система Drag & Drop. Сохранены implicit Grid root, `DesignerDocumentSession`, flat Controls + ParentId, существующий StackOrder, GridDropTarget, pointer/capture controller, Grid Editor Undo boundaries и Property Inspector.

Поддержаны Grid, StackPanel, WrapPanel, DockPanel и Canvas **внутри native Grid-формы**. Существующие Canvas/v1 проекты сохраняют прежнюю семантику; автоматической миграции нет. Grid/Canvas выбор при создании формы и defaults не менялись.

DockPanel — режим `Children Layout = Dock` существующего Group или другого поддержанного контейнера. Например: Toolbox → Group → Inspector → Children Layout → Dock. Новый Toolbox control не добавлен. Group с Dock экспортируется непосредственно как DockPanel; Canvas-обхода или фиктивного Grid нет.

## 2. Native container hit testing

`AxamlLayoutProjection.JsonLayoutTree` по-прежнему строит настоящие Avalonia panels и выполняет Measure/Arrange. Новые transient measurements описывают:

- фактические bounds и clip каждого внутреннего panel;
- arranged children, margins и порядок;
- Orientation и LastChildFill;
- paint-order hit list, полученный обходом **нативного visual tree**, включая ZIndex/порядок siblings и clipping.

Алгоритм выбирает видимую foreground-ветвь, затем ближайший контейнер этой ветви, чья внутренняя область содержит точку. Глубокий контейнер позади перекрывающего sibling не выигрывает только за счёт глубины. Padding/Border/Margin уже учтены TranslatePoint; clipping согласован с существующими designer child hosts. Root представлен пустым ParentId.

Указатель переводится прежним GetDesignCanvasPosition в DIP формы. Дополнительного деления на DPI нет: zoom/scroll transforms выполняет Avalonia. Проверяется актуальность snapshot/session/геометрии, существование и lock родителей. Перенос в себя/потомка запрещён. При смене документа geometry cache очищается. В cache остаются только строки/числа/Rects, без native controls.

## 3. StackPanel insertion

Vertical/Horizontal используют arranged children, их реальные midpoints, margins и Spacing. Поддержаны начало, промежуток, конец и первый элемент. Линия вставки строится по границам соседей; model X/Y для расчёта не используются.

После Drop siblings сортируются через прежний StackOrder; новый или перемещённый элемент вставляется в вычисленную позицию. Относительный порядок остальных, включая скрытые элементы, сохраняется. В hover документ не меняется.

Новый пустой Auto-контейнер в flow иначе имеет нулевую native высоту. Поэтому **новые** native containers получают реальные авторские MinWidth=48 и MinHeight=32 через существующий CustomProperties. Эти стандартные Avalonia свойства участвуют в Measure, видны в Inspector, сохраняются в JSON и экспортируются. Это не фиктивная editor-only геометрия и не fixed Height: контейнер растёт по содержимому. Значения можно явно изменить, в том числе на 0. Уже сохранённые модели автоматически не меняются.

## 4. DockPanel placement

Внешние области выбирают Left/Right/Top/Bottom по ближайшей нормализованной стороне. Подсветка показывает сторону, ghost — результат настоящего proposed Measure/Arrange. DockPanel.Dock хранится типизированным accessor поверх CustomProperties; LastChildFill принадлежит контейнеру и не меняется drag-операцией.

При LastChildFill=True боковой элемент вставляется перед существующим последним, сохраняя его fill-роль. Пустой Dock допускает первый элемент; подсказка предупреждает, что он одновременно последний и поэтому заполняет доступное место. Центр пустого Dock или текущего последнего элемента допустим. **Занятый fill-центр для другого элемента блокируется**, чтобы не менять роль существующего элемента молча: пользователь выбирает сторону или явно меняет LastChildFill в Inspector.

LastChildFill с выражением read-only для визуального Drop. Строковые выражения сохраняются; binding не заменяется bool-литералом.

## 5. WrapPanel insertion/reorder

Для Horizontal/Vertical определения линий используются фактические arranged outer bounds детей, включая margins. Выбирается ближайшая реально размещённая строка/колонка, затем позиция внутри неё. Нет предположения об одной строке и равномерном размере элементов.

Порядок также хранится прежним StackOrder. Native projection, Designer visual order и Export используют его одинаково для Stack/Wrap/Dock. После изменения размера wrapping выполняет Avalonia.

В проверенной Avalonia 11.1.1 после расширения окна arranged children переходили из двух рядов в один, DesiredSize.Height уменьшался 80→40, а выделенный Grid Auto-row slot мог сохранять высоту 80 на наблюдаемом pass. Поэтому тест reflow проверяет **позиции детей и DesiredSize**, а не только allocated Bounds.Height. Это наблюдение native layout; собственного компенсирующего layout engine не добавлено.

## 6. Reparenting и placement conversion

Перемещение меняет тот же DesignControlModel: сохраняются Id, Name, styling, custom properties, BindingSourceId, Logic, plugin/descriptor metadata и DataGrid configuration.

| Target | Записывается | Другие placement values |
| --- | --- | --- |
| Grid | ParentId, Row/Column, существующие spans | Canvas X/Y нейтральны, Dock/order не экспортируются как активный placement |
| Stack/Wrap | ParentId, sibling order | X/Y нейтральны, Grid/Dock поля остаются неактивными |
| Dock | ParentId, DockPanel.Dock, sibling order | Grid/Canvas не являются активным placement |
| Canvas | ParentId и реальные локальные DIP с grab offset / прежним grid snap; Alt отключает snap | Grid/Dock/order не используются для позиционирования |

Spans сохраняются при переносе через flow. Если при возвращении в Grid они не помещаются, Drop отменяется с объяснением, без clamp. Общие Width/Height, modes, Margin и alignment сохраняются.

Особый Canvas-сценарий: удаление элемента из Stack может сдвинуть сам целевой Canvas-sibling. Первый ghost Measure учитывает **будущую** позицию родителя и корректирует локальные координаты. Следующие hover внутри той же цели только транслируют ghost. Если указатель оказался бы вне будущей clip-области Canvas, операция явно отклоняется.

Если затрагиваемый placement задан binding/неоднозначным CustomProperty (Canvas.Left/Top, X/Y, Grid.Row/Column/Spans, StackOrder, DockPanel.Dock), перенос отклоняется до mutation. Общие bindings не удаляются и не преобразуются в литералы.

## 7. Pointer, selection и overlays

Переиспользованы состояния GridControlPending/MovingGridControl как общий native layout gesture: press выбирает элемент, threshold отделяет click от drag, capture принадлежит стабильному DesignerCanvas. Grid definition resize, marquee, control resize и move не работают одновременно.

Escape, CaptureLost, отпускание вне формы, смена формы/проекта, закрытие окна и удаление source/target отменяют временное состояние. До commit модель не меняется. После Drop selection сохраняется, Inspector обновляется существующим путём; ApplyDocument для hover/move не вызывается.

Overlay показывает контейнер, cell/side, insertion line, proposed bounds, overlap либо причину отказа. Все элементы non-hit-testable, не входят в модель/JSON/AXAML. Grid diagnostics сохранены и используются общим controller; повторный hover внутри одной цели не создаёт поток записей.

Визуальный smoke обнаружил существовавшую проблему порядка слоёв: GuideOverlayCanvas объявлен до DesignerCanvas и скрывался непрозрачным StackPanel. На время native Drop overlay получает ZIndex выше designer/selection; ClearGuideOverlay снимает локальный override после Drop/Cancel. Routed regression проверяет и видимость слоя, и восстановление ZIndex. Снимок активной вставки сохраняется в `artifacts/layout-phase5/nested-insertion.png`.

Для Standard/Eremex DataGrid внешние 4 DIP рамки стали отдельным designer drag hit area: native header мог раньше захватить press до wrapper. Область resize в правом нижнем углу сохранена; внутренние header/cell gestures не переназначены. В Preview эти рамки отсутствуют.

Групповой перенос нескольких controls в managed layout явно ограничен прежним сообщением о выборе одного элемента. Multi-selection и legacy Canvas group move сохранены.

## 8. Undo/Redo и Inspector

Один Drop — одна существующая undo transaction, включая ParentId, положение, Dock и изменение порядка всех затронутых siblings. Undo восстанавливает старое размещение, Redo — новое с прежними Id. Toolbox Undo удаляет добавленный элемент, Redo возвращает его с тем же Id. Контейнер переносится вместе с потомками; их ParentId не меняется.

Inspector использует прежние rows/categories/favorites/tooltips. Показывается текущий тип размещения: Grid attached properties, generic children order, DockPanel.Dock или Canvas X/Y. Dock LastChildFill и минимальные размеры — настоящие свойства Avalonia. Неактивные прежние Grid/Canvas placement не предлагаются как текущий способ позиционирования. Русские описания передаются существующим tooltip механизмом.

## 9. JSON и AXAML round-trip

Новой Document Model, Children collection или версии файла нет. Сохраняются существующие version 2.4 / semantics 2, ParentId, StackOrder, Grid fields и CustomProperties (Dock, LastChildFill, minima). Transient hit metadata, ghost, gesture token и overlays не сериализуются.

Старый Canvas/v1 не конвертируется. Для imported AXAML структурные reparent/reorder/nested insertion read-only; исходник не регенерируется. Patch writer, source spans, namespaces/comments/styles/bindings/unknown attributes и IPC ACK не менялись.

## 10. Preview, Export и найденная ошибка writer

Legacy Preview получает тот же native panel adapter; AXAML Preview — настоящий export tree. DataGrid runtime loaders, DataContext, ItemsSource, SQL/PostgreSQL и cancellation pipeline не изменялись. Plugin placement использует прежний NativeLayout.Placement payload.

Новый hierarchy assertion воспроизвёл старую ошибку `AppendChildControlXaml`: дочерний descriptor писал в глобальный writer, пока буфер родителя ещё не был опубликован. Дети оказывались **до/вне** контейнера, несмотря на правильные attached attributes. Исправлено локально: child пишет в переданный parent StringBuilder с прежним shared context. Повторной сборки словаря всех controls на каждого child нет. Теперь родительская структура сохраняется и в AXAML Preview, и в generated application.

C# генерация/NameScope/Logic проверяются в запущенном generated окне: вложенная кнопка изменяет TextBox. Экспорт не включает overlays или скрытый Canvas; Canvas создаётся только для выбранного Absolute-container.

## 11. Производительность и memory

Проекция ghost выполняется при смене контейнера/cell/side/insertion или измеренной геометрии, а не на каждый PointerMoved. Canvas hover переиспользует измеренный размер и смещение будущего родителя; overlap пересчитывается по value measurements. Отдельный счётчик native preview passes используется в regression tests.

Overlays очищаются/заменяются, не накапливаются. Сессионный cache не содержит controls, timers или event subscriptions. Параметры placement перепроверяются перед commit. Capture и подписки взаимодействия остаются в прежнем controller, frame handlers принадлежат текущему visual subtree.

## 12. Проверки

Baseline до изменений: solution **0 ошибок, 1 прежнее NU1603**, runner **0 ошибок/предупреждений**, Grid Drop **40/40**, Pointer **26/26**, Preview memory без live DB **28/28**. Логи находятся в `artifacts/layout-phase5/baseline-*.log`.

Финальная сборка `FormDesigner.sln`: **0 ошибок, 1 прежнее NU1603**; smoke runner: **0 ошибок, 0 предупреждений**. `git diff --check` проходит. Логи: `solution-final-build.log`, `export-tests-build.log`.

Пройдены три generated project restore/build: стандартный nested Grid/Stack/Dock/Wrap, Eremex и mixed Canvas/Grid Multi Form. Проекты используют прежние TargetFramework и зависимости; при сборке generated net6.0 остаётся NETSDK1138 о завершении поддержки framework. Пакеты и TFM не обновлялись.

**Generated runtime действительно запущен.** Проверены Grid root, реальные StackPanel/DockPanel/WrapPanel, NameScope и действие перенесённой кнопки. При увеличении окна на 250×120 DIP DataGrid увеличился **510×299 → 760×419**, WrapPanel изменил **2 ряда → 1**, DesiredSize.Height **80 → 40**. Лог: `artifacts/layout-phase5/generated-runtime.log`. Это layout runtime с пустым DataGrid, не live DB test.

Для обоих Preview проверены настоящая иерархия, локальный BindingSource, Dock fill и Wrap reflow. Контрактные SQL/PostgreSQL проверки не объявляются подключением к серверу: переменные FORMDESIGNER_TEST_SQLSERVER/POSTGRES не заданы.

Regression результаты:

| Набор | Результат |
| --- | --- |
| Nested Layout (модель, native Measure, real routed pointer/DragEvent, Preview, WeakReference/cache) | 59/59 после финальной проверки видимости overlay |
| Grid Root / Grid Drop / Grid Editor / Pointer | 28/28 / 40/40 / 45/45 / 26/26 после финальной правки overlay |
| DataGrid offline contracts | 18/18 |
| PostgreSQL offline contracts | 20/20 |
| Stabilization audit | 25/25 |
| Preview memory, ограниченный и обычный Windows process | 28/28 в каждом |
| Расширенный smoke: Surface/Inspector/Favorites/Multi Form/JSON/Settings/Help/DLL/Eremex/Round-trip/VsHost/IPC/VSIX/Export | 72/72 |
| Новые generated projects | 3/3 |

После последней правки designer-only слоя повторены все пять layout/pointer suites (`final-*.log`), включая визуальный снимок активной вставки. Preview/Export, database loaders, IPC и memory lifecycle после их проверок не менялись.

Memory: в Legacy и AXAML после 20 последовательных циклов **aliveWindows=0**, activeWindows=0; clock observer count не растёт. Offscreen ghost regression выполняет 12 native projections с DataGrid и проверяет освобождение созданных controls при живом ViewModel. Production GC.Collect не добавлен; тестовый ForceFullGc используется только для WeakReference assertions.

Smoke runner сообщил об отказе очистки двух временных outputs после успешных scenarios (DLL ещё были заняты). После завершения процесса отдельная ограниченная очистка прошла успешно: `cleanup.log`. Это отделено от компиляции и результатов сценариев. Bin/obj трёх retained generated projects удалены после runtime, исходники и текстовые логи оставлены для ревью.

Повторить: `dotnet build FormDesigner.sln --no-restore -m:1`, затем build smoke runner и `dotnet smoke-tests/FormDesigner.ExportSmokeTests/bin/Debug/net8.0/FormDesigner.ExportSmokeTests.dll --nested-layout-checks`. Generated cases выбираются `SMOKE_SCENARIO_FILTER=NestedLayoutPhase5`; `ProbeNestedLayout.ps1 -ProjectPath <generated-dir>` запускается после restore/build. Probe временно меняет только entry point generated artifact и восстанавливает его в finally.

Промежуточные failing логи сохранены отдельно. Неверные fixture assumptions отделены от ошибок продукта: managed X/Y, clipped zoom point, DataGrid export mode и allocated Wrap slot. Три старых Grid Root assertions первоначально не принимали новую запись статуса `[0,0]`; возвращена понятная русская запись «строка 0, столбец 0», исходные assertions не менялись.

## 13. Файлы

- `Models/DesignerLayoutModes.cs`, `GridDropTarget.cs`, новый `LayoutPlacementProperties.cs` — Dock/ordered vocabulary, общий transient target, typed property bag accessors.
- `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.Json.cs`, новый `AxamlLayoutProjection.DropGeometry.cs` — native panels, actual hierarchy/clip/children measurements.
- `ViewModels/MainWindowViewModel.GridDrop.cs`, новый `.NestedDrop.cs` — общий resolver, proposal, conversion, order/commit и Inspector rows.
- `ViewModels/MainWindowViewModel.NativeLayout.cs`, `.GridEditor.cs`, `.cs` — minima, context editors, geometry invalidation, native ordering и local child export writer.
- `Views/MainWindow.GridDrop.cs`, `.NativeLayout.cs`, `.axaml.cs` — прежний controller для всех native containers, overlays, frame hit area и measured snapshot hookup.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новые `Program.NestedDrop*.cs` — model/native routed input, Preview, memory/cache и generated fixtures.
- Новый `smoke-tests/LayoutGrid/ProbeNestedLayout.ps1` — generated app hierarchy/Logic/resize/reflow probe с восстановлением Program.cs.
- `Docs/LayoutDesignerPhase5.md` — этот отчёт.

NuGet, TargetFramework, global styles, Toolbox registry, SQL/PostgreSQL services, Eremex packages/theme, VSIX/IPC и source patch writer не менялись.

## 14. Ограничения и ручная проверка

- Полный новый workflow предназначен для native JSON v2 Grid-форм. Legacy Canvas/v1 сохраняет прежнюю семантику. Imported AXAML structural editing не включено.
- Flow multi-drag, произвольные plugin containers, drag spans и новый root layout picker не добавлены. Zero-area/fully clipped targets нельзя выбрать мышью; minima/размеры задаются явно.
- Занятый Dock fill-center блокируется; LastChildFill не переключается автоматически. Binding placement не заменяется литералом.
- Проверить физический Toolbox DoDragDrop из разных dock panels, Alt/Tab/Escape и release вне окна, несколько мониторов с DPI 125/150/200%, маленькие вложенные панели и сложные overlaps/clipping.
- Проверить крупные проекты и тяжёлые пользовательские Eremex datasets; DataGrid в flow может получать бесконечное измерение вдоль основной оси по стандартным правилам StackPanel, поэтому для виртуализации следует задавать конечный viewport/размер.
- Физические DPI, интерактивная Visual Studio, Linux/macOS и live PostgreSQL не объявляются проверенными автоматически по зелёным routed tests/build. Preview memory regression относится к ранее исправленным roots, а не к отсутствию любых утечек продукта.

Phase 6 автоматически не начинается.
