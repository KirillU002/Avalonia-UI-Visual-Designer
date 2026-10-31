# Layout Designer Phase 6 — UX, responsive validation и визуальная согласованность

Дата: 31.10.2026. Baseline: `05ceeb4d28807716c61685206337c75be5d2e7d7`. До начала рабочее дерево было чистым. Прочитаны Architecture, Phase 2–5, StabilizationAudit и PreviewMemoryLeakFix. Git history и пользовательские файлы не откатывались.

**Canvas остаётся вариантом по умолчанию.** Root factory, DTO defaults и версии semantics/JSON не переключались. Phase 7 не начиналась.

## 1. UX audit и область проверки

Проверены прежние DesignerDocumentSession, Surface wrappers/overlays, Grid Editor, общий Grid/nested DnD controller, native projection, Inspector, два Preview и Export. Существующий Avalonia Measure/Arrange используется для layout, ParentId/StackOrder — для иерархии. Второго layout engine, BindingSource или Inspector нет.

Grid, StackPanel, DockPanel, WrapPanel и вложенный Canvas сохраняют свои правила. Сохраняются глубокий target, клиппинг/paint order, spans, atomic Drop, selection, escape/capture cancellation и старый Canvas free positioning. Pointer regression выполняет настоящий Avalonia raw/routed input; это не вызов отдельных VM methods вместо UI.

Существующие insertion/cell/dock/ghost overlays из Phase 5 не меняли цвета или глобальную тему. Проверены их non-hit-testable поведение, удаление после операции, Z-order поверх opaque containers, отсутствие в export и отсутствие накопления. Добавлены проверки zoom 50/75/100/125/150/200% с реальным scroll viewport.

## 2. Найденные проблемы и root causes

Новых P0 потери данных не воспроизведено. Повторены audit/persistence tests. Подтверждённые P1/P2 исправлены локально:

| Id | Приоритет | Воспроизведение и причина | Исправление |
| --- | --- | --- | --- |
| L6-01 | P1 | A, 800×600: Button в AXAML имел Width=99, Legacy=225. Export пропускал Stretch/Top, полагаясь на defaults; Button theme задаёт Left. | Native placement экспортирует обе alignment явно. Legacy Canvas output сохраняет прежнюю политику. |
| L6-02 | P1 | Auto controls измерялись декоративными Border/TextBlock; wrap и intrinsic size отличались от настоящего Button/TextBox. Карточка неподключённого DataGrid также навязывала чужой minimum. В узком viewport смещалась соседняя колонка. | Native Designer использует настоящие базовые controls и пустой DataGrid без I/O; native Legacy Button/TextBlock получают те же literal content/font semantics, что export. Canvas factories сохранены. |
| L6-03 | P1 | Native TextBox вне render root занимал 82 DIP вместо 40: scrollbar оставался Visible и имел размер по умолчанию. Owner bindings обычно устанавливаются при visual attachment. | JSON offscreen preparation подключает Visibility/Viewport/Maximum к публичным свойствам того же ScrollViewer, если значения не заданы. Видимость/extent вычисляет Avalonia, собственной формулы нет. Source AXAML projection не менялся. |
| L6-04 | P1 | Native форма только с Button/TextBlock: AXAML click не выполнял Logic. Names сохранялись только для DataGrid/items; basic Button handler также отсутствовал. Новый test сначала дал 0/1. | Для native форм names сохраняются; Button использует существующий ExecuteStandardClick/Refresh, без новой event infrastructure. |
| L6-05 | P1 | MaxWidth/MaxHeight сохранялись в CustomProperties, но не участвовали в native projection/export. Ограничения leaf controls попадали в raw Inspector. | Четыре стандартных constraint properties проходят Inspector, native layout, JSON и export, включая plugin placement payload. |
| L6-06 | P1 | После 1920×1080 → 320×220 Preview/generated runtime сохранял старые DesiredSize: WrapPanel оставался шириной 1516 при client width 320. Переоценка children устраняла расхождение. Отдельный compiled B runtime воспроизвёл 11 mismatches. | Один coalesced deferred native measure после resize, без пересоздания controls, изменения constraints и перезагрузки данных; тот же принцип сгенерирован в C#. |
| L6-07 | P1 | Runtime compiler не разрешал schema URL Eremex из plugin load context, хотя DLL и XmlnsDefinition присутствовали. AXAML Preview уходил в Legacy fallback. | Runtime loader квалифицирует однозначные contributed URL maps через clr-namespace/assembly, сохраняя aliases. Reserved Avalonia/XAML namespaces защищены отдельным regression test (сначала 0/1). Пользовательский AXAML не переписывается. |
| L6-08 | P1 | В смешанной Eremex-форме basic Button имел 40 DIP в Legacy и 38 в AXAML из-за разных областей действия существующей theme. | Одинаковые существующие contributions применяются к user layout subtree/measurement. DesignerCanvas scope снимается при переходе на v1 Canvas; Application и chrome styles не меняются. Типы учитываются по всему проекту и текущим active Controls, без устаревшего active snapshot. |
| L6-09 | P2 | 100 hit tests на 500 controls выделяли 163.1 MB: каждый вызов сериализовал полный fingerprint и копировал его в GeometryKey. | Fingerprint кэшируется с exact invalidation, mutable property bag сравнивается без serialization; маленькие version tokens меняются только при точном изменении input/actual geometry, включая tracks. Хэши с риском collision не используются. |
| L6-10 | P2 | Запрос zoom 125% молча округлялся до 100%. Усиленный test с чтением фактического zoom дал 5/6; прежняя проверка reparent сама по себе этого не обнаруживала. | 125% включён в прежние presets и таблицу допустимых масштабов. Тест требует фактически запрошенный коэффициент до routed drag. |

Всего в этом проходе подтверждены 11 проблем: 8 P1 и 3 P2; исправлены 8 P1 и 2 P2. Оставшаяся P2: бейджи имён вложенных controls могут перекрывать друг друга и caption при 200% (`zoom-200.png`). Требуется отдельная политика подписей/hover, без изменения selection и глобальной темы. Не выдаётся за исправленную.

Дополнительный regression пустого Text/Watermark пройден: native controls сохраняют авторскую пустую строку. Некорректные fixture assumptions не засчитаны как product bugs: инструмент сначала искал настоящий DataGrid внутри старой Designer-card; программный CreateControl без завершённой Toolbox transaction сливал последующее Undo. Старый synthetic smoke shell также перезаписывал App.axaml и терял plugin theme: новые Phase 6 tests теперь используют настоящий ExportCurrentResultToProjectAsync, а не этот shell. В runtime probe снята ненужная compile-зависимость DataGrid для сцен без него; проверка BindingSource выполняется через существующее ItemsSource свойство. Expectations геометрии и Undo не ослаблялись.

## 3. Responsive scenarios

Матрица проверяет 800×600, 1200×800, 1600×900, 1920×1080 и последующее уменьшение до 320×220. Это DIP клиентской области, не размеры window decorations.

| Сценарий | Содержимое |
| --- | --- |
| A | Grid Auto/*, columns 2*/*, header span 2, Standard DataGrid и sidebar StackPanel с Button/TextBox |
| B | DockPanel Left + fill header, WrapPanel с девятью fixed buttons и Margin; меняется фактическое число строк |
| C | Grid → StackPanel → Border → Canvas; явный Canvas 500×200 и локальные X/Y |
| Eremex | A с Eremex DataGridControl/TextEditor и существующими plugin contributions |
| Constraints | A с авторскими Min/Max Width/Height у leaf controls |
| Mixed | Основная Grid-форма, Eremex на другой Grid-форме, отдельная старая Canvas-форма |
| Bound | A с Manual BindingSource и **явно разрешёнными локальными sample rows**, без group/filter/footer hosts |

Переполнение C при узком окне ожидаемо: авторский Canvas 500 DIP шире доступного места. Размеры, RowSpan/ColumnSpan и constraints не исправляются автоматически. Grid сам не делает фиксированное содержимое адаптивным.

## 4. Visual Consistency Report

`Program.LayoutValidation.cs` измеряет настоящие Surface wrappers, native Legacy и runtime AXAML controls. Сравнение нормализовано относительно DesignerCanvas/PreviewSurfaceBorder, а generated runtime — client origin Window. Записываются Scene, mode, viewport, X/Y/Width/Height, Name, ParentId и layout.

Иерархия проверяется независимо по ближайшему model control ancestor. В compiled runtime saved Id не внедряется в AXAML: actual controls сопоставляются через export NameScope, а фактический parent/container type коррелируется с исходным Id. Geometry tolerance — **1.1 DIP**, без специальных поправок для screenshot или Eremex. Layout constraints остаются авторскими.

Основные файлы доказательств: `artifacts/layout-phase6/visual-observations.json`, `visual-consistency.csv`, `visual-summary.json`, `runtime-*.log` и `runtime-observations-*.json`. Снимки `zoom-100.png`, `zoom-200.png` — дополнительная визуальная QA, не замена Bounds tests.

Итог: **860 наблюдений Bounds** — по 215 на режим, 7 сценариев × 5 viewport. Отдельное сопоставление с Designer даёт **645 сравнений**, 0 несовпадений иерархии/geometry за допуском. Семь действительно запущенных приложений получены через production Export Pipeline; в каждом проверено последовательное увеличение и уменьшение окна. NameCollision — отдельный восьмой compile regression.

| Scene | Измерений controls на режим | Max Δ Legacy, DIP | Max Δ AXAML, DIP | Max Δ Runtime, DIP |
| --- | --- | --- | --- | --- |
| A | 25 | 0 | 0 | 0 |
| B | 65 | 0 | 0 | 0 |
| C | 25 | 0 | 0 | 0 |
| Constraints | 25 | 0 | 0 | 0 |
| Eremex | 25 | 1 | 1 | 1 |
| Mixed | 25 | 0 | 0 | 0 |
| Bound | 25 | 1 | 1 | 1 |

Это проверка Bounds и иерархии, не заявление pixel-perfect rendering всех шрифтов/тем. Условия и допуск одинаковы для всех сцен; координатных offsets под screenshot не добавлено.

В Mixed числовая runtime matrix измеряет основную Grid-форму под общей plugin theme проекта. Остальные формы включены в export/build, переключение форм и сохранность иерархии проверяют отдельные Multi Form/JSON/Preview regression tests. Интерактивная навигация по всем окнам Mixed в экспортированном приложении остаётся ручным checklist, не объявляется частью 860 измерений.

## 5. Inspector, diagnostics, JSON и Undo

Margin, Padding, alignment, definitions/placement/spans, Orientation, Spacing, Dock и sizing переиспользуют прежние rows/categories/tooltips/Favorites. Ключи constraints сохранены; это не новые фиктивные свойства.

MinWidth/MinHeight допускают 0; MaxWidth/MaxHeight допускают Infinity как снятие ограничения. Числовые значения должны быть конечными и неотрицательными. Binding/resource expressions сохраняются и доступны read-only вместо замены литералом. WidthMode/HeightMode и сохранённые Width/Height от редактирования Min/Max не меняются. Противоречивые Min>Max диагностируются, не нормализуются молча.

Новые предупреждения добавлены в прежнюю Diagnostics panel: fixed Width больше доступного slot, выход за clip, Min>Max. Перекрытие Grid/Canvas — **Info**, может быть намеренным. Geometry warnings используют актуальный snapshot без I/O/нового Measure. Вывод ограничен 50 geometry messages, чтобы плотный overlay не переполнил панель. Прежние проверки Grid indices/spans сохранены.

Новых DTO полей или migrations нет: constraints остаются в CustomProperties, placement — ParentId/Grid fields/StackOrder. JSON reload и Undo/Redo проверяют реальные Inspector commits после завершённого Toolbox Drop, сохранение identity/selection и возврат constraints. Старые Canvas не конвертируются.

## 6. Resize lifecycle и безопасность данных

Viewport refresh только инвалидирует Measure снизу вверх после текущего layout pass. Несколько SizeChanged объединяются в один dispatcher callback. Закрытое окно callback не обновляет. Legacy/AXAML content, DataContext, BindingSource, SQL loaders и ItemsSource не пересоздаются.

Generated helper подключается только для native forms, в обоих C# generation paths и каждой форме. Его имя резервируется тем же export name map, чтобы control с таким же авторским Name не ломал build. Canvas generated code не получает helper.

## 7. Zoom и физический DPI

| Проверка | Результат |
| --- | --- |
| Zoom 50/75/100/125/150/200%, nested reparent, selection/capture/scroll | Пройдены raw/routed tests |
| Native Windows RenderScaling=1 (100%) | Зафиксирован в логах |
| Физический DPI 125/150/200%, перенос между мониторами | Не проверено |
| Аппаратный системный Toolbox DoDragDrop | Требуется ручной smoke |

Координаты берутся из Avalonia transforms/GetPosition в DIP. Дополнительного умножения на zoom/DPI нет. Изменение Zoom не меняет Document Model.

## 8. Performance baseline

Windows, тот же checkout, 100/300/500 controls во вложенном WrapPanel. Отдельно измеряются создание модели, открытие JSON, показ настоящего Designer, selection, switch формы, native tree build/Measure и 100 hit tests. Show включает явные 120 ms idle; baseline selection/switch включали 30 ms idle. Финальный selection измеряется до готовности Inspector для выбранного ControlId, Drop commit — до обновления native snapshot и UI layout. Switch включает создание пустой формы и возврат с 30 ms idle. Это не чистый microbenchmark. Cold JIT, GC и параллельные сборки могут влиять на timings, поэтому произвольных pass/fail thresholds нет.

До оптимизации:

| Controls | JSON open ms | Show+idle ms | Measure/build ms | Selection+idle ms | Switch+idle ms | Hit median ms | Allocated / 100 hits |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 100 | 46.9 | 2821 | 206 | 38.5 | 1001 | 0.352 | 32.95 MB |
| 300 | 39.9 | 1762 | 213 | 36.2 | 689 | 0.917 | 98.69 MB |
| 500 | 45.3 | 2619 | 333 | 29.8 | 1135 | 1.412 | 163.12 MB |

Первая оптимизация fingerprint уменьшила allocations на 500 controls до 83.34 MB / 100 hits и median до 0.200 ms. Профиль показал оставшееся копирование больших GeometryKey; оно заменено version tokens с exact comparisons. Финальный замер (`performance-final.log`):

| Controls | JSON open ms | Show+idle ms | Measure/build ms | Drop preview ms | Drop commit → UI ms | Selection → Inspector ms | Switch ms | Hit median ms | Allocated / 100 hits |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 100 | 49.3 | 2014 | 32.6 | 65.4 | 588 | 242 | 570 | 0.047 | 0.92 MB |
| 300 | 41.5 | 1626 | 60.9 | 57.1 | 487 | 108 | 518 | 0.167 | 2.74 MB |
| 500 | 68.0 | 1659 | 252.1 | 70.8 | 897 | 167 | 740 | 0.125 | 3.25 MB |

На 500 controls выделения hit-testing уменьшились примерно на 98%, median в этом замере — примерно в 11 раз. Selection timing до/после напрямую не сравнивается из-за усиленного критерия готовности. 0.9 секунды до UI commit на 500 controls остаются заметной задержкой; улучшение hover не означает устранения всех performance-проблем.

Сборка 500 controls программным циклом с действующим history/refresh занимала около 30–33 секунд; открытие готового JSON существенно быстрее. Массовое создание в активном редакторе и визуализация крупных проектов остаются кандидатами отдельного performance этапа. Общая память managed heap не интерпретируется как leak: проверяются WeakReference/владельцы, а не возврат working set ОС.

## 9. Preview memory и реальные данные

На отдельной instance `FormDesignerLayout6_5bb08f30132340579a36b0494764c0c0`, БД LayoutPhase6, созданных только для этого прогона, проверены настоящие SQL rows, пустая таблица, ошибки, cancellation, timeout, multiple grids/forms и видимые cells. Windows authentication, без паролей. Пользовательские/production БД не использовались.

DataGrid runner: **43/43** = 18 offline contracts + 25 live SQL. Memory runner: **36/36**, включая 8 live SQL cases, Eremex, close during loading/query и 20 циклов каждого режима. На checkpoints 5/10/15/20 старые окна освобождаются, clock observers возвращаются к исходным значениям. Production GC.Collect или массового обнуления DataContext нет.

БД drop, instance stop/delete завершены; `dataFiles=0`. Логи: `live-datagrid-preview.log`, `live-preview-memory.log`, `live-sql-setup.log`. Live PostgreSQL не запускался; его 20 offline contracts не объявляются серверной проверкой. Bound visual scene — явная локальная fixture, а не SQL integration.

## 10. Regression и build

Baseline и финальная сборка solution: **0 ошибок**, 1 прежнее NU1603 в VSIX SDK analyzers. Финальная сборка smoke runner: 0 ошибок, 0 предупреждений. Логи `solution-final-build.log`, `tests-build.log`.

| Suite | Результат | Характер проверки |
| --- | --- | --- |
| Новые Phase 6 checks | 55/55 | Настоящие Surface, оба Preview, Logic, constraints/Inspector, JSON/Undo, cache guards, theme scope, 6 фактических zoom |
| Grid Root / Grid Drop / Grid Editor | 28/28, 40/40, 45/45 | Native geometry, placement, editing, real routed pointer cases |
| Nested DnD / Pointer-Marquee | 59/59, 26/26 | Иерархия, overlays, cancellation/capture, Canvas, selection |
| Stabilization / PostgreSQL offline | 25/25, 20/20 | Persistence/AXAML safeguards; PG contracts без live server |
| DataGrid / Preview memory с изолированной LocalDB | 43/43, 36/36 | Offline + явно отмеченные live cases, видимые cells, WeakReference и clock subscriptions |
| Широкая regression matrix | 72/72 | Surface/Inspector, Multi Form, export, Round-trip, Settings/Help, DLL, VSIX/VsHost/IPC |
| Production generated restore/build | 8/8 | A/B/C/Eremex/Constraints/NameCollision/Mixed/Bound |
| Production generated runtime | 7/7 | 35 resize viewport cases, реальные Bounds/иерархия и manual BindingSource rows |

Cache/namespace protection повторно проверены целевыми suites после исправлений; после добавления 125% повторены Phase 6, Native Grid, Grid Editor и Pointer. Логи `verified-*.log`, `final-grid-drop.log`, `final-nested-layout.log`, `generated-production.log`, `runtime-production-*.log`.

Широкая matrix включает Surface, Inspector/Favorites, Multi Form, JSON/persistence, Canvas/Responsive export, Settings/Help, DLL, Eremex, source comments/styles/bindings/unknown attributes, checksum/version conflicts, реальные VsHost/Named Pipes и VSIX packaging/dependency closure. Интерактивный Visual Studio не подменяется этими tests.

NuGet/TFM не менялись. Generated projects сохраняют net6.0 и существующее NETSDK1138; у fixtures также встречаются CS0108 (имя Title) и CS8618 generated fields, ошибок сборки нет. `NuGetAudit=false` использован только в окружении test process. Временные большие outputs очищены после выхода процессов; текстовые доказательства и generated исходники сохранены. Широкий runner сообщил cleanup failure на загруженной DLL после 72/72; отдельная очистка после его завершения успешна. Это не скрывается как результат теста или ошибки compilation.

## 11. Изменённые файлы

- `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.Json.cs` — настоящий JSON offscreen sizing, constraints и scrollbar owner bindings.
- `Models/LayoutPlacementProperties.cs` — accessors четырёх стандартных constraints.
- `Services/PluginPreviewControlBuilder.cs`, `RuntimeAxamlPreviewLoader.cs` — существующие root contributions и безопасное runtime namespace qualification.
- `ViewModels/MainWindowViewModel.NativeLayout.cs`, `.NestedDrop.cs`, `.GridDrop.cs`, `.GridEditor.cs`, `.cs` — Inspector/export, scope types, resize helper/name reservation, cache guards и tokens.
- Новый `ViewModels/MainWindowViewModel.LayoutValidation.cs` — warnings в прежней Diagnostics panel.
- `Views/MainWindow.NativeLayout.cs`, `.axaml.cs` — native leaf controls и scoped user canvas theme с reset.
- `Views/DesignerSurface.axaml`, `Views/MainWindow.axaml` — только пункт 125% существующего zoom ComboBox; стили и глобальная тема сохранены.
- `Views/PreviewWindow.NativeLayout.cs`, `.StandardItems.cs`, `.axaml.cs` — native sizing/content, basic Logic и coalesced viewport measure.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новые `Program.LayoutValidation.cs`, `.Properties.cs` — geometry, properties, zoom, performance и generated fixtures.
- Новый `smoke-tests/LayoutGrid/ProbeResponsiveLayout.ps1` — отдельный compiled runtime, viewport/parent/layout/geometry comparison, восстановление generated entry point в finally.
- `Docs/LayoutDesignerPhase6.md` — этот отчёт.

Не переписывались Document Model и DesignerSurface architecture, schema/DB services, Eremex packages/theme values, source patch writer и VSIX/IPC protocol.

## 12. Воспроизведение

```powershell
dotnet build .\FormDesigner.sln --no-restore -m:1
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --layout-validation-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --layout-performance-checks
$env:SMOKE_SCENARIO_FILTER = 'LayoutPhase6_'
$env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS = '1'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\layout-phase6\export-repeat
```

Для runtime использовать `ProbeResponsiveLayout.ps1 -ProjectPath <generated-dir> -Scene A -ReferencePath artifacts/layout-phase6/visual-observations.json` после restore/build; для других scenarios заменить Scene. Probe меняет только test artifact, возвращает исходные Program.cs bytes и пересобирает обычный entry point в finally. После очистки bin/obj перед повторением нужны restore/build. Для повторения LocalDB подготовить новую изолированную БД с `smoke-tests/SqlServer/DataGridPreviewFixture.sql`; не указывать пользовательскую БД.

## 13. Remaining limitations / manual checklist

- Физические DPI 125/150/200%, несколько мониторов, mixed scaling, перенос окна между ними, аппаратный DoDragDrop/Alt+Tab/release вне окна.
- При 200% проверить читаемость name badges вложенных controls; их перекрытие зафиксировано как оставшийся P2 UX backlog.
- В exported Mixed открыть обе дополнительные формы, проверить навигацию и resize каждой, затем закрытие/повторное открытие.
- Light/Dark на пользовательских themes, длинные captions, очень узкие/нулевые tracks, тяжёлые Eremex datasets и длительный session soak.
- Native JSON v2 остаётся отдельной семантикой. Старые Canvas/v1 не мигрируют. Структурные операции imported AXAML остаются read-only; live PostgreSQL и interactive Visual Studio не заявляются проверенными.
- Отдельный backlog: массовое создание сотен controls, большое число plugin containers, виртуализация DataGrid при бесконечном измерении в Stack/ScrollViewer. У автора должны быть явные конечные viewport/constraints; автоматического исправления нет.
- Обычные Avalonia Window вне Preview могут сохранять известный native UIA root этой версии backend; Preview-specific cleanup не расширялся на весь продукт.

## 14. Готовность к Grid-first

Решение оценивает **проверенную native JSON workflow**, не произвольные AXAML templates/3rd-party plugins и не миграцию старых документов. До смены default нужен ручной DPI/system drag smoke, подтверждение UX на целевых рабочих местах, устранение пересечения name badges и отдельное решение о performance крупных форм. Автоматического переключения default нет.

READY WITH CONDITIONS
