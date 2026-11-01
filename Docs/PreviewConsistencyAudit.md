# Preview Consistency Audit & Stabilization

Дата: 01.11.2026. Baseline: `f7087b159ee4a695291d80914a5d9facae210e4d`, рабочее дерево перед началом было чистым. Изучены Layout Phase 6/7A/7A2, PreviewMemoryLeakFix, DataGridPreviewRegression и StandardToolboxControls. Пакеты, TargetFramework, JSON format, defaults Preview/Grid-first, SQL provider implementations и VSIX/IPC не изменялись.

## Результат и границы проверки

Подтверждены и исправлены ошибки построения Preview, состояния controls и упаковки встроенного Image при экспорте. Стандартный JSON Designer проверен по реальному реестру, native visual tree и фактической геометрии. Экспортированные приложения действительно запущены отдельными процессами. Успешная компиляция не использовалась как доказательство отображения.

Исходная сложная форма, на которой пользователь ранее наблюдал пропажу компонентов, не предоставлена. Воспроизведены конкретные механизмы потери controls в текущем коде; соответствие прежнему пользовательскому инциденту не установлено. Для произвольного импортированного AXAML полная fidelity **не достигнута**: source projection и export через модель имеют разные возможности. Теперь это явно объясняется в диагностике.

## 1. Существующие pipelines

```text
JSON / document session
  ├─ DesignerSurface
  │    ├─ Canvas wrappers / selection overlays
  │    └─ AxamlLayoutProjection.BuildJson → native Measure/Arrange (semantics v2)
  ├─ Legacy PreviewWindow.RenderDocument
  │    ├─ v1: AddControlsToCanvas → CreatePreviewWrapper → descriptor.BuildPreview
  │    └─ v2: RenderNativeLayoutDocument → BuildJson → descriptor leaf factory
  └─ MainWindowViewModel.GenerateExportedAxamlPreviewSnapshot
       → isolated export VM.GenerateXaml
       → GeneratedAxamlService: Window → UserControl, remove code-behind events
       → RuntimeAxamlPreviewLoader.Load
       → runtime DataContext / bindings / Logic

Export: GenerateXaml / generated C# → ExportPipelineService
       → .csproj + App + AXAML + ViewModels → normal desktop runtime
```

AXAML Preview использует тот же **сгенерированный** AXAML, что экспорт. Преобразование root выполняется в памяти; пользовательский source не перезаписывается. Имена native controls теперь сохраняются и в Canvas Preview, чтобы сопоставлять модель с фактическим деревом. Публичное default-поведение GeneratedAxamlService для других callers не изменено.

Legacy v1 сохраняет свободные координаты и wrappers. Для управляемых поддеревьев Grid/Stack/Wrap/Dock теперь переиспользуется существующий `BuildJson`, а не декоративная карточка с внутренним Canvas. Временная projection не меняет semantics/version исходного документа. Нового layout engine нет.

Designer использует служебные wrappers. Для Grid/StackPanel/WrapPanel и CheckBox attached-представление может быть shell, а native container геометрия вычисляется отдельной projection. Поэтому «Designer — да» ниже означает реальное attached designer-представление, а не утверждение, что каждый shell является тем же runtime-типом.

## 2. Registry-derived completeness

Источник списка: `vm.Registry.GetControls()` после регистрации built-ins. В основном JSON Toolbox зарегистрированы **14** типов. ComboBox, DockPanel и другие типы AXAML Import не добавлялись в этот список вручную. DockPanel проверен отдельно через существующий `Group.ChildLayoutMode=Dock`.

| TypeKey | Designer | Legacy native tree | AXAML native tree | Export native runtime |
|---|---|---|---|---|
| Button | Да | Button | Button | Button |
| CheckBox | Да, shell | CheckBox | CheckBox | CheckBox |
| TextBox | Да | TextBox | TextBox | TextBox |
| DataGrid | Да | DataGrid | DataGrid | DataGrid |
| TextBlock | Да | TextBlock | TextBlock | TextBlock |
| Border | Да | Border | Border | Border |
| WrapPanel | Да, shell + projection | WrapPanel | WrapPanel | WrapPanel |
| Grid | Да, shell + projection | Grid | Grid | Grid |
| StackPanel | Да, shell + projection | StackPanel | StackPanel | StackPanel |
| Image | Да | Image | Image | Image с загруженным Source |
| DropDownButton | Да | DropDownButton | DropDownButton | DropDownButton |
| ListBox | Да | ListBox | ListBox | ListBox |
| SplitButton | Да | SplitButton | SplitButton | SplitButton |
| Group | Да | Canvas по default ChildLayoutMode | Canvas | Canvas |

Проверены Canvas и Grid формы. Assertions требуют native instance, attachment к Window и корректный ParentId. Дополнительно в отдельном runtime открыты настоящие Flyout обоих видов кнопок и проверен ListBox.SelectedItem после выбора. Это программные routed/template interactions, не физический тест мышью.

## 3. Воспроизведённые дефекты и root cause

| ID | Приоритет | Воспроизведение / фактический результат | Конкретная причина | Исправление / статус |
|---|---|---|---|---|
| PC-01 | P0 | Provider бросает исключение при Initialize; стандартный Button исчезает из обоих Preview вместе со всем поддеревом | `ApplyNativeLayoutRootStyles` / `PrepareRuntimePreviewPluginContributions` пробрасывали ошибку необязательного provider. Native Legacy catch заменял всю форму сообщением; AXAML fallback попадал в тот же failure path | Изоляция каждого provider, агрегированная диагностика, остальные controls продолжают строиться |
| PC-02 | P1 | Hidden TextBlock в модели появляется в AXAML Preview | `WireStandardItemsAxaml.Refresh` назначал runtime default `IsVisible=true` всем named controls | Изменение visibility/enabled только при явном Logic override; authored literal/binding сохраняется |
| PC-03 | P1 | Disabled ListBox становится enabled в native Legacy | `RenderNativeLayoutDocument` перезаписывал factory result через getter, который по умолчанию возвращал true | Getter учитывает authored IsEnabled; runtime override сохраняет приоритет |
| PC-04 | P1 | Canvas-документ содержит WrapPanel, но в Legacy visual tree WrapPanel отсутствует | v1 `CreatePreviewWrapper` использовал декоративный builder и Canvas для children | Управляемое поддерево строится существующей native projection; внешнее Canvas placement сохраняется |
| PC-05 | P0 | Экспорт формы с default Image собирается, но падает в конструкторе Window: FileNotFoundException для assembly FormDesigner | Generated AXAML ссылался на `avares://FormDesigner/Assets/avalonia-logo.ico`; экспорт не переносил этот встроенный asset | Только используемые designer assets копируются/включаются как AvaloniaResource; в создаваемых файлах URI меняется на `/Assets/Designer/...`. Проверены folder export, ZIP и actual runtime |
| PC-06 | P1 | Недоступный TypeKey превращается в placeholder без сообщения об ошибке | `GetRequiredControl` возвращал fallback descriptor, который считался успешным native build | Проверка TryGetControl, MissingDescriptor, видимый aggregated warning. Неизвестный control не объявляется успешно созданным |
| PC-07 | P2 | ListBox Opacity=.5 в Canvas Legacy имеет эффективную opacity=.25 | Общая factory и v1 wrapper применяли opacity независимо | Для этого Canvas path opacity принадлежит wrapper; native Grid path сохраняет factory opacity |
| PC-08 | P2 | Dark root становится Light после Window→UserControl | Window-only RequestedThemeVariant удалялся, PreviewWindow применял document default | Literal Light/Dark из generated root применяется к PreviewWindow. Глобальная тема Designer не меняется |
| PC-09 | P1 | Импортированный ComboBox остаётся в модели как Group; оба Preview содержат 0 ComboBox | AXAML Import source projection знает исходный тип, но Preview/export повторно генерируют DTO, не original source map | **Остаётся**. Добавлены UnsupportedControl и resource/style warnings до построения Preview; source session не удерживается окном и не изменяется. Полный source-aware Preview требует отдельной задачи |
| PC-10 | P2 | При viewport 1600×900 старый Canvas Legacy остаётся в левом верхнем углу; AXAML/runtime центрируют фиксированный root 1200×800 | Legacy PreviewCanvas имеет MinWidth/MinHeight и растягивается, экспорт задаёт фиксированные Width/Height root Canvas | **Остаётся**. При исходном client size 1200×800 геометрия совпадает. Автоматическая смена семантики старых Canvas-проектов не выполнялась |
| PC-11 | P2 | У SQL DataGrid отличаются оформление header и фактические размеры TextBlock: Legacy `{12,13}`, AXAML `{13,15}`, при одинаковом DataGrid.FontSize=13 | `CreateModernDataGridPreview` не применяет набор локальных DataGridColumnHeader/DataGridCell Styles, который генерирует `AppendDataGridXaml`; Legacy использует Fluent defaults для части template | **Остаётся**. Видимые значения/колонки и внешняя геометрия исправны. Общую унификацию DataGrid styles следует выполнить отдельным небольшим изменением с tests native template properties |

Итого: 11 локализованных случаев; 8 исправлений, 3 ограничения с явным статусом. PC-01 воспроизведён контролируемой fault injection настоящего provider contract и построением native UI; это не утверждение о конкретном сбое Eremex на машине пользователя. Исправление PC-06 обеспечивает диагностику недоступного типа, а не поддержку отсутствующего plugin.

Initial reproducer: 6/11; strict native-type reproducer: 12/13; дополнительные opacity/descriptor проверки: 25/28 до соответствующих исправлений. После исправлений новый suite — **32/32**. Логи до исправления сохранены отдельно.

## 4. Visual Consistency Report

Фикстуры:

* RegistryNative: все 14 entries из registry, Grid root 4×4.
* RegistryCanvas: те же entries, старый semantics v1 и свободное размещение.
* NestedNative: 13 controls; Grid → DockPanel → horizontal StackPanel + реально переносящий элементы WrapPanel; Border → Grid → DataGrid + TextBlock + SplitButton.

Native scenes проверены при одинаковой client area в DIP: 800×600, 1200×800, 1600×900. В каждом режиме записаны X/Y/Width/Height, actual native type, IsVisible, attachment и ParentId. Геометрия Image сравнивается по authored внешнему Border, а не по внутреннему letterboxed bitmap.

| Scene | Models | Viewports | Designer ↔ AXAML, max Δ | Legacy ↔ AXAML, max Δ | Runtime ↔ AXAML, max Δ |
|---|---:|---|---:|---:|---:|
| RegistryNative | 14 | Все три | 0 DIP | 0 DIP | 0 DIP |
| NestedNative | 13 | Все три | 0 DIP | 0 DIP | 0 DIP |
| RegistryCanvas | 14 | 1200×800 | 0 DIP | 0 DIP | 0 DIP |

Допуск assertions — 1.1 DIP. Указанные нули получены измерением, а не округлением допуска. Итого 492 observation rows, включая 123 observations в трёх отдельных generated processes. Native Grid-сценарии дают 324 observations четырёх режимов.

Canvas при других размерах измерен и **не объявлен эквивалентным**. У фиксированного AXAML/runtime root возникает translation ±200 DIP по X и ±100/50 DIP по Y относительно client area. В тесте Designer DesignWidth/Height изменяется, а Preview сохраняет исходный snapshot 1200×800: это изменение document viewport тоже нельзя выдавать за идентичный responsive сценарий. При 1600×900 дополнительно проявляется PC-10. Полный raw report сохранён; такие различия не скрыты координатной поправкой или ослаблением native Grid assertions.

Min/Max dimensions, alignment, spans, Zoom/DPI и resize дополнительно покрываются существующими layout suites. Здесь не проводилось pixel-perfect screenshot сравнение шрифтов/тем, физическое перемещение между мониторами или испытание всех сочетаний авторских bindings.

## 5. Стили, ресурсы и imported AXAML

Отдельная runtime-loaded fixture проверяет DynamicResource и StaticResource из Window.Resources, локальный Style с FontSize=23, а также Dark ThemeVariant после адаптации root. Opacity проверена в обеих semantics и обоих Preview. Authored visibility и enabled проверяются по фактическим native controls.

Для **generated JSON AXAML** resources/styles, присутствующие в разметке, сохраняются при адаптации. Это не означает переноса произвольных App resources, code-behind, external plugin assemblies, converter instances и исходного DataContext пользовательского проекта.

Imported source fixture с ComboBox и Window.Resources подтверждает PC-09 в обоих режимах. Preview получает только value metadata source types; при несовпадении выдаёт UnsupportedControl с именем компонента. Исходный text, comments, checksum/session и результат `CreateActiveAxamlPatch()` остаются неизменными. Операция не заменяет source-preserving patch полной регенерацией пользовательского файла. Сохранение source и экспорт новой модели — разные операции.

## 6. DataGrid / реальные данные

Создана отдельная тестовая LocalDB instance `FDPCA_20261101_184159`, database `FDPreviewConsistency`. Пользовательские instances/databases не использовались. Customers: три различающиеся записи, Orders: две, EmptyTable: ноль.

После всех live/runtime/memory проверок тестовая БД и instance удалены; пользовательские instances оставлены. Удалены 323.7 MiB собственных bin/obj artifacts. Сохранены исходники fixtures/export, screenshots, JSON/CSV evidence и логи; размер evidence каталога после очистки около 1.34 MiB.

Проверены оба Preview: настоящие видимые значения, null, aliases с пробелами и точкой, несколько grids, пустая таблица с колонками, отсутствие источника, explicit static data, фильтр и его очистка, неверный запрос, disconnected server, timeout, reopen, close during query и Multi Form.

**47/47**: 18 offline contracts + 29 live SQL checks. Дополнительные native Grid и nested Grid сценарии проверяют настоящие строки и рост actual DataGrid Bounds после увеличения окна на 200×100 DIP. В отдельном экспортированном приложении действительно открыты Grid Customers и nested Grid Orders с независимыми ViewModels, количеством 3/2 и видимыми значениями; resize обеих форм сохраняет строки. Тестовый Windows-auth профиль не содержит пароля. Npgsql не добавился в SQL Server export. Отдельный live workflow global SQL profile → schema discovery → неизменный BindingSourceId → оба Preview также проходит.

Первая live попытка была неуспешна: тестовый SQL script был прочитан sqlcmd в неверной кодировке, и кириллица уже в БД оказалась mojibake. Проверено через UNICODE первой буквы (1056 вместо 1040); только тестовые таблицы пересозданы с `-f 65001`. Это ошибка подготовки fixture, не новый defect rows/binding. Первые логи сохранены. Старый test также отличал fallback по banner text: теперь проверяет фактический mode в PREVIEW_BUILD_SUMMARY, чтобы warning о неверном запросе не считался Legacy fallback.

Первые расширенные resize checks дали 43/47, затем 45/47 из-за трёх ошибок fixture: Top вместо Stretch; поиск имени Border-frame на внутреннем Grid; default nested Grid 3×3 вместо предполагавшейся одной ячейки. Измерены фактические parents и bounds: 3×3 корректно давал примерно +67/+33 DIP, а не требуемые +200/+100. Фикстура явно настроена как Stretch и 1×1. Assertions роста не ослаблены; production layout из-за этих тестовых ошибок не менялся. Итоговый полный прогон — 47/47, промежуточные логи сохранены.

PostgreSQL live **не проверен**: тестового сервера нет. Offline provider contracts не названы live integration. SQL services, query generation, metadata и драйверы не менялись.

## 7. Plugins / Eremex isolation

Необязательный provider, бросающий исключение при Initialize, больше не удаляет стандартные controls из формы. В AXAML pipeline такой provider не применяется повторно на root после ошибки подготовки. Ошибка агрегируется и видна пользователю; failure одного plugin не превращается в отказ всего стандартного Preview.

Это не гарантия загрузки любого произвольного AXAML с неразрешимым plugin type: runtime compiler может отвергнуть весь документ, после чего применяется существующий диагностируемый Legacy fallback. Экспериментальные Eremex сценарии проверяются отдельно; их результат не подменяет стандартную матрицу registry.

## 8. Диагностика и lifecycle

Добавлены PREVIEW_BUILD_START, PREVIEW_CONTROL_DISCOVERED, PREVIEW_CONTROL_CREATED, PREVIEW_CONTROL_SKIPPED, PREVIEW_CONTROL_FAILED, PREVIEW_LAYOUT_COMPLETED, PREVIEW_BINDING_FAILED, PREVIEW_RESOURCE_FAILED и PREVIEW_BUILD_SUMMARY.

Records содержат FormId, ControlId, Type, ParentId, PreviewMode, Created, Attached, Visible, numeric bounds, NativeType и FailureReason. Подробные component events включаются только через `FORMDESIGNER_PREVIEW_DIAGNOSTICS=1`; failures и summary доступны без постоянного подробного profiling. Нет логов на каждый кадр.

MainWindow использует существующие Output/Diagnostics: одна сводная запись на form/mode, до шести примеров в сообщении. Нет сотен popups. У Preview есть existing runtime warning badge; ошибка остаётся видимой даже при штатно скрытом badge. Missing descriptor/failed factory не выдаются за успешно созданные controls. Намеренный IsVisible=false обозначается AuthoredHidden.

События не включают raw exception messages, AXAML, SQL, credentials или connection strings. От exception записывается тип. Отдельные существующие DataGrid diagnostics продолжают давать query/load/runtime chain; общая binding failure диагностика добавлена на их failure path. Это **не** универсальный перехват всех Avalonia binding errors или resource converter failures.

Maps принадлежат PreviewWindow; Closed инвалидирует отложенную layout проверку и очищает ссылки на controls, MainWindow отписывается от событий при DISPOSED. Внешний source session/VM в новых maps не хранится. Animation clock/UIA исправления сохранены. GC.Collect в production не добавлялся.

## 9. Проверки

Baseline Release solution: **0 errors**, один прежний NU1603 (VS SDK Analyzer 17.7.20 заменён на доступный 17.7.22). Baseline layout validation 55/55 и DataGrid offline 18/18.

Финальные результаты:

| Проверка | Результат |
|---|---|
| Release FormDesigner.sln, включая VSIX package / VsHost | 0 errors, тот же 1 NU1603 |
| Новый Preview consistency suite | 32/32 |
| Generated restore/build + actual desktop runtime | 3/3 формы, native tree/иерархия/Image/Flyout/selection |
| JSON Save/Load | Canvas/Grid/Nested hierarchy, order, placement; оба Preview после открытия |
| ZIP resource packaging | Asset bytes + rewritten AXAML + AvaloniaResource, модель не изменена |
| SQL Preview offline + live | 47/47 |
| SQL settings/schema/BindingSource workflow | PASS, реальные metadata/rows, оба режима |
| SQL generated runtime / две native Grid формы / resize | PASS |
| Preview memory с LocalDB | 36/36; оба режима, 20 циклов каждого, empty/static/multiple/Multi Form/Eremex, cancellation |
| Layout validation / Phase 7A.2 | 55/55 и 13/13 |
| Native Grid / Grid Drop / Nested Layout | 28/28, 40/40, 59/59 |
| Pointer / Grid Editor / Phase 7A UX | 26/26, 45/45, 15/15 |
| Undo history / PostgreSQL offline | 25/25 и 20/20; PostgreSQL live SKIP |
| Stabilization audit, включая save/open/Multi Form/closed Preview | 25/25 |
| Broad selected regression: exports, DesignerSurface, source-preserving AXAML, VsHost/IPC | **70/72** |

Memory checks используют WeakReference на Window/DataContext/ItemsSource и тестовый GC после обработки очередей; не требуют немедленного возврата working set ОС. В 20 циклах каждого режима живых старых окон — 0, активных lifecycle окон — 0, observers animation clock — 0. Физическое UI Automation взаимодействие внешнего screen reader не тестировалось.

Generated projects сохраняют существующий net6.0; NETSDK1138 фиксируется как исходное ограничение, не устраняется обновлением TFM.

Два broad failures: `EremexTextEditorVerticalSlice`, `EremexDataGridControlVerticalSlice`. Generated Window constructor в test load context не разрешает `Avalonia.Diagnostics, Version=11.1.1.0`. Тот же failure path зафиксирован до этого аудита в UndoHistoryCachePoC.md. Текущая проверка его вновь воспроизвела. Eremex designer/layout/theme и memory сценарии проходят, но это **не** успешный standalone Eremex vertical-slice runtime. NuGet/loader policy ради зелёного результата не менялись.

Во время broad runner часть удаления bin не прошла с UnauthorizedAccessException для ещё загруженных generated DLL. Функциональный PASS и cleanup failure фиксируются отдельно; cleanup не считается проверкой UI или причиной двух Eremex failures.

## 10. Изменённые файлы

Production:

* `Services/PluginPreviewControlBuilder.cs` — изоляция contribution failures.
* `Services/PreviewRuntimeService.cs` — удаление diagnostics конкретной build-категории.
* `Services/ExportPipelineService.cs`, новый `Services/ExportPipelineService.Assets.cs` — перенос используемых встроенных designer assets в folder/ZIP/validation export.
* `ViewModels/MainWindowViewModel.cs` — сохранение runtime names в snapshot AXAML.
* `Views/PreviewWindow.axaml.cs` — build boundaries, native Canvas subtrees, descriptor detection, window theme, plugin isolation, opacity ownership.
* `Views/PreviewWindow.NativeLayout.cs` — переиспользование existing native projection, tracking native controls, runtime enabled/visibility.
* `Views/PreviewWindow.StandardItems.cs` — authored properties сохраняются при отсутствии Logic override.
* `Views/PreviewWindow.DataGrid.cs` — bridge query failure в общую Preview диагностику.
* Новый `Views/PreviewWindow.Consistency.cs` — value diagnostics, completeness checks, imported source limitations.
* `Views/PreviewWindow.Lifecycle.cs` — cleanup новых build maps/events.
* `Views/MainWindow.axaml.cs` — existing Output/Diagnostics aggregation, source metadata, unsubscribe.

Tests/documentation:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` — dispatch нового suite.
* Новые `Program.PreviewConsistency.cs`, `Program.PreviewConsistency.Geometry.cs` — registry matrix, native tree/geometry, properties/resources/plugin failure, source limitations, JSON/ZIP.
* `Program.DataGridPreviewRegression.cs` — проверка фактического AXAML mode вместо текста status; source metadata как при обычном UI запуске; actual template font observations и live Grid/nested Grid resize.
* `Program.DataGridPreviewExport.cs` — end-to-end SQL в Grid/nested Grid, отдельный generated process, две формы и resize с сохранением видимых значений.
* `smoke-tests/PreviewConsistency/ProbeRuntime.ps1`, `RuntimeProbe.cs.txt`, `CompareGeometry.py` — воспроизводимый separate-process probe и comparison.
* `smoke-tests/PreviewConsistency/README.md` — команды воспроизведения и границы автоматизации.
* Этот отчёт.

Evidence: `artifacts/preview-consistency-audit/`: baseline/final logs, JSON fixtures/manifests, `geometry.json`, три `*-runtime.json`, `visual-tree.csv`, `geometry-comparison.json`, ZIP и исходники трёх экспортированных проектов. Probe временно меняет startup только внутри generated fixture и восстанавливает bytes в finally. В production не добавлен runtime probe.

## 11. Оставшиеся ограничения и ручная проверка

1. PC-09: source-aware Preview/Export для типов, styles/resources и bindings импортированного пользовательского AXAML — отдельная задача. Сейчас имеется явная диагностика, а не обещание native fidelity.
2. PC-10: согласовать legacy fixed Canvas root resize policy отдельно, без неявной миграции старых проектов.
3. Повторить прежнюю пользовательскую сложную форму, когда её можно будет получить; установить режим и сопоставить diagnostics с контрольным export.
4. Физически открыть Flyout, вводить текст, выбирать ListBox/DataGrid, сортировать заголовком и закрывать Preview; automated routed interactions не равны проверке реальной мышью.
5. Проверить DPI 100/125/150/200% и несколько мониторов. Текущий четырёхрежимный geometry audit выполнен на одной Windows rendering конфигурации, в DIP, без pixel-perfect обещания.
6. PostgreSQL live остаётся непроверенным. Для произвольных внешних image/resource assemblies автоматическая упаковка не добавлялась: PC-05 исправлен для встроенных Assets текущего Designer.
7. Проверить App-level theme/resource dictionaries реального импортированного проекта. Отдельный generated runtime Style/StaticResource/DynamicResource test не доказывает загрузку чужого приложения целиком.
8. PC-11: синхронизировать локальные styles native DataGrid headers/cells и проверить фактические template properties. Нулевой delta внешних Bounds не означает одинаковую типографику.

## AXAML Preview readiness

**READY WITH CONDITIONS** для стандартных JSON-проектов: registry completeness, реальная nested geometry, видимые SQL rows и standalone runtime подтверждены. Условия: исправить/согласовать PC-10 и PC-11, продолжить экспериментальную plugin проверку и физическую UI/DPI проверку.

Для произвольных импортированных AXAML-проектов с source-only controls/resources — **NOT READY** до source-aware Preview решения PC-09. Этот результат не смешивается с успешной JSON матрицей. AXAML Preview основным режимом не делался; Grid-first defaults не менялись.
