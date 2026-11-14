# Universal Controls — Phase 2

Дата: 14.11.2026. Baseline: `856d7e33bd944adaf3be3f7dc3d686dd5a4d4ba9`, чистое рабочее дерево. Основа — [Phase 1](UniversalControlsAndPropertiesPhase1.md). NuGet, TargetFramework, глобальная тема, JSON format, Document Model, VSIX/IPC и Grid-first defaults не менялись. Коммиты/теги/релиз не создавались.

## 1. Выбранная группа и аудит

Добавлены **RadioButton, NumericUpDown, Calendar, DatePicker, TimePicker, Separator**. Это шесть законченных leaf controls через существующий metadata engine и descriptor/preview/export инфраструктуру.

CheckBox уже был в Toolbox и повторно не регистрируется. В установленной Avalonia 11.1.1 обнаружены также HyperlinkButton, ScrollBar и UniformGrid; наличие CLR-типа не означает готового adapter. Для этого этапа выбрана группа без изменения parent/children semantics. Separator не получил фиктивного Orientation: такого свойства у данного native типа в используемой версии нет.

| Кандидат / группа | Состояние до этапа | Решение / приоритет дальше |
|---|---|---|
| Шесть добавленных controls | Нет JSON Toolbox/export adapter; native типы обнаружены | Общая reviewed scalar policy + стандартные native templates |
| CheckBox, Slider, ProgressBar, ToggleSwitch | Уже есть | Сохранены, отдельная Phase 1 regression |
| ScrollViewer, Expander | Partial AXAML Import, нет JSON descriptor | Следующая группа: single Content/Header adapter и правила размещения детей |
| ItemsControl, TreeView/TreeViewItem, TabControl/TabItem, ComboBoxItem | Partial source import; полного JSON hierarchy workflow нет | Нужны проверенные item hierarchy, SelectedItem/Index, collection/template editors; существующий специализированный ComboBox сохранён |
| Viewbox | Обнаружен, полного descriptor нет | Single-child adapter, stretch semantics и bounds после resize |
| Menu/MenuItem, ContextMenu, Flyout | Разные item/attachment workflows | Специализированные descriptors/редакторы; не обычные текстовые свойства |
| ScrollBar, UniformGrid | Публичные authorable primitives, но общий discovery помечает primitives infrastructure | Отдельный reviewed допуск после native layout/export tests; фильтр primitives массово не снимается |
| GridSplitter | Обнаружен | Pointer/resize semantics требуют отдельного design/runtime разделения |
| HyperlinkButton | Обнаружен | Uri/navigation safety, команды и отдельный runtime acceptance |

Полный актуальный список: [Control Coverage CSV](UniversalControlCoverage.csv). Abstract/base/infrastructure/popup types не добавляются автоматически. Прежние standard descriptors прошли registry-derived Preview/runtime проверки; наличие adapter в отчёте не заменяет проверку native tree.

## 2. Переиспользование metadata

`StandardControlProperties.SupportedTypes` расширяет прежний `PilotTypes`, оставленный совместимым для Phase 1 tests. Один support policy задаёт список проверенных ключей; тип, AP identity, registration defaults, nullable/read-only, editor kind и inherited/attached сведения поступают из `AvaloniaMetadataCatalog`.

Один native factory применяет properties в Designer и Legacy Preview; один literal codec используется Inspector, JSON bag, source import и export attributes. AXAML Preview загружает тот же AXAML, что generated project. Добавление компонента не потребовало отдельного набора setter/switch веток для каждого pipeline. Специальная часть — только text Content для RadioButton и разные стартовые размеры Calendar/Separator.

К существующим Styled/Direct/Attached/inherited свойствам добавлены безопасные scalar codecs для **decimal, DateTime, DateTimeOffset, TimeSpan**. Decimal сериализуется JSON number без преобразования в double; dates — явный ISO round-trip, duration — invariant `c`. Metadata defaults не читают getters instances, пользовательские DLL/VM не исполняются.

Общий reviewed набор для новой группы: IsEnabled, Min/MaxWidth/Height, Padding, BorderBrush/BorderThickness, CornerRadius, Horizontal/VerticalContentAlignment, FontStyle/Stretch, ClipToBounds — только если соответствующее AP реально существует. Width/Height, Margin/alignment, visibility/opacity, font/colors, Grid/Canvas/Dock placement обслуживаются существующими model/layout adapters. Attached placement остаётся parent-context dependent.

ClockIdentifier — native string с закрытым доменом 12HourClock/24HourClock; Inspector использует enum-choice editor. Numeric/date format strings проверяются native .NET formatters до записи. Nullable values сохраняются JSON null. Infinity допускается для MaxWidth/MaxHeight; снятие ограничения использует отсутствие записи, как существующий layout adapter, и не превращается в protected binding string. Сложный/неизвестный JSON value не перезаписывается scalar editor.

## 3. Inspector: сохранённые механизмы и исправления

Сохранены категории, поиск, Favorites, selection/context guards, Undo transactions, Color и прежние специализированные collection/Grid/DataGrid editors. Date/time — типизированные scalar editors поверх существующего text template с ISO/time validation и русской подсказкой; новый визуальный календарь внутри Inspector не заявляется.

Подтверждённый конфликт: старые appearance rows для BorderThickness/CornerRadius выигрывали дедупликацию и записывали dormant scalar model field вместо нового typed CustomProperties value. Теперь `ApplyMetadataPropertyRow` переводит такие строки в общий metadata editor до capability/deduplication. Native parent size-constraint rows и protected/read-only source rows сохраняются. Нового Property Inspector не создано.

Сравнение modified/default у metadata rows использует typed scalar equality. Это сохраняет различие на единицу около decimal.MaxValue, которое терялось при старом double comparison, и корректно сравнивает structs/null/brush colors. Исходные Model/JSON данные не округляются.

## 4. Layout и pointer UX

Пройден реальный routed Toolbox Drop, Undo/Redo и raw pointer selection для каждого нового control. Designer-only native input mask использует прежний DesignerPreviewMode; в обоих Preview и runtime controls интерактивны. Нельзя назначить Canvas.Left/Top непосредственному Grid child.

Выявлено реальное перекрытие: floating Layout toolbar поверх viewport перехватывал клик по коротким controls в Row 0. Hit-test показывал toolbar Border вне DesignerCanvas; native pointer handler даже не вызывался. Существующий toolbar вынесен в Auto-row над viewport, с wrapping. Это локальная правка XAML host, без изменения пользовательского layout, координатных поправок или переписывания interaction state machine. Повторный тест проверяет именно исходную первую ячейку, а не переносит fixture в другую строку ради PASS.

Проверены Grid, Canvas, StackPanel, DockPanel, WrapPanel и вложенный Grid → StackPanel: native controls присутствуют с ожидаемыми properties, ParentId/order сохраняются в JSON. Дополнительно проверены Multi Form isolation и collectibility удалённого native NumericUpDown при остающемся живым Designer/VM.

Общий geometry regression выявил ещё один подтверждённый дефект новой группы: Separator в старой Canvas-форме смещался в AXAML Preview/Export на 12×4 DIP относительно wrapper bounds. Fluent theme устанавливает ему Margin=12,4; legacy placement использует wrapper frame без control Margin, а export не переопределял theme value. Дополнительная проверка native coordinates выявила необходимость нормализовать также control внутри wrapper, а не только export. Общая metadata factory теперь начинает с Margin=0; native/source layout adapters затем применяют авторское значение из модели. Для metadata controls в **legacy absolute parent** экспорт явно пишет Margin=0, повторяя историческую семантику. Native layout placement (включая Canvas в новом native hierarchy) остаётся под управлением своего adapter. Проверка сравнивает фактические координаты native controls, а не только attribute strings.

## 5. AXAML Import / Round-trip

Для каждого нового типа проверены native projection после изменения Inspector, no-op, минимальная замена одного attribute value, сохранение и повторный patch, version/checksum conflict. Ресурсы, namespace, comments, unknown attrs, binding и template/style subtrees сохраняются текстом; пользовательские bindings/VM при импорте не выполняются.

Дополнительный root cause: `ApplySourceLayout` после shared factory снова применял старые Min/Max attributes из original source. Изменённый MinWidth=50 отображался как 40. Теперь constraint берётся из текущего model accessor только при разрешённом literal edit и изменении относительно snapshot; иначе используется original attribute. Binding выражения не вычисляются/не подменяются.

Editable относится к проверенным literals, PartiallyEditable — к source control в целом. Bindings/templates/resources/unknowns остаются read-only/opaque по capability policy; не выполняется полная регенерация AXAML. Канонизация изменённого date attribute допустима и ограничена самим value span; неизменённые short-date/formatting сохраняются точно.

## 6. Сложные свойства и ограничения

* Calendar поддерживает single-date workflow. SelectedDates/multiple-range authoring требует collection editor; не выставляется как строка.
* Content у RadioButton — literal text; visual Content hierarchy/Commands требует отдельного descriptor workflow. Полноценный arbitrary binding authoring для всех новых AP не заявляется по одному BindableTarget flag.
* Items/ItemsSource/SelectedItem, ControlTemplate/DataTemplate, Styles/Resources, Commands, Flyout/ContextMenu не становятся scalar fields. Существующие специализированные editors сохранены.
* Imported AXAML с произвольным пользовательским template/resource tree сохраняется, но его source-faithful runtime reproduction целиком не заявляется. Неподдерживаемая структурная операция остаётся запрещённой.
* Shared scalar policy отклоняет строки, начинающиеся с markup `{...}`, включая escaped/composite forms. Они не исполняются как bindings; обычные FormatString=F2 и YearFormat=yyyy поддержаны. Расширение literal escaping потребует отдельного согласованного JSON/source codec; исходный AXAML с такими значениями сохраняется read-only. Дополнительный отрицательный эксперимент подтвердил отказ ещё до Preview, а не предполагаемый AXAML loader crash.

## 7. Coverage до/после

| Метрика | Phase 1 | Phase 2 |
|---|---:|---:|
| Публичные visual CLR types | 140 | 140 |
| Toolbox descriptors, включая Group | 17 | 23 |
| Типы с подробным property audit | 30 | 36 |
| Пар «тип–property/alias» в подробном CSV | 5957 | 7139 |
| JSON editable adapters, исходный Phase 1 отчёт | 252 | Для сопоставления требуется учёт contextual rows ниже |
| Contextual layout adapters, ранее недосчитанные | 216 у прежних типов | Дополнительно учтены в новой классификации |
| Editable по уточнённой общей методике | 468 у прежних типов | 694 = 468 + **226 у новых controls** |
| Importable adapters | 587 | 801 = 587 + 214 |
| Shared metadata literal adapters | 24 | 129 = 24 + **105 новых** |

1182 дополнительных discovered rows означают расширение подробного аудита на уже обнаруженные шесть типов, а не появление новых Avalonia API. Новая детализация: Discovered=7139, Displayable=954, Editable=694, Serializable=954, Importable=801, Exportable=695, Unsupported=6185.

| Новый control | Discovered | Displayable | Editable | Serializable | Importable | Exportable | Unsupported | Shared literals |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| RadioButton | 204 | 37 | 37 | 37 | 35 | 37 | 167 | 16 |
| NumericUpDown | 207 | 42 | 42 | 42 | 40 | 42 | 165 | 22 |
| Calendar | 198 | 39 | 39 | 39 | 37 | 39 | 159 | 19 |
| DatePicker | 196 | 41 | 41 | 41 | 39 | 41 | 155 | 21 |
| TimePicker | 190 | 35 | 35 | 35 | 33 | 35 | 155 | 15 |
| Separator | 187 | 32 | 32 | 32 | 30 | 32 | 155 | 12 |

226 editable entries включают common/placement свойства в подходящем parent context, не только специфические Value/SelectedDate. Это количество reviewed adapters, а не число индивидуально выполненных комбинаций значений; native tests проверяют представительные свойства каждого типа и общие codecs. Property assignment ReadOnly не равен immutable collection: прежние специализированные collection editors учитываются отдельно. Сводка и данные: [JSON](UniversalCoverageSummary.json), [полный property CSV](UniversalPropertyCoverage.csv).

Discovered означает найденную public schema. Displayable — наличие действующего Inspector/source adapter; Editable — разрешённый JSON literal/специализированный старый adapter, с parent/source capabilities. Serializable/Importable/Exportable — наличие соответствующего reviewed adapter, не право сериализовать любой CLR object. Unsupported означает отсутствие такого adapter. Сложные значения остаются ограниченными независимо от scalar support того же property name.

В Phase 2 дополнительно учтены уже существующие contextual layout rows (Margin/alignment/Grid/Dock/size constraints), которые Phase 1 CSV недосчитывал. Этот методологический прирост не выдаётся за новые реализованные свойства. Старые даты/constraints/bindings в JSON автоматически не мигрируются.

## 8. Производительность и память

Release, Windows, без debugger и параллельных builds/native suites. Одинаковая Phase 1 сцена (Slider/ProgressBar/ToggleSwitch), 3 warm-up + 30 switches; новая сцена измерена отдельно: 6 warm-up + 30 switches. Таймер охватывает синхронный selection/Inspector/Window.UpdateLayout; это не first compositor frame. Allocations — UI thread. GC counts относятся ко всему интервалу warm-up + measured switches, не доказывают причину P95.

| Сцена / замер | Median, ms | P95, ms | Median allocations | Metadata rebuilds |
|---|---:|---:|---:|---:|
| Переданный baseline Phase 1 | 166 | 253 | Не задано | — |
| Свежий замер до изменений, прежние 3 controls | 145.02 | 325.32 | 20,529,336 B / 19.58 MiB | 0 |
| Финальный код, те же 3 controls | **140.54** | **248.59** | 20,510,944 B / 19.56 MiB | **0** |
| Новые 6 controls, отдельная сцена | **145.51** | **254.90** | 22,586,476 B / 21.54 MiB | **0** |

Первый измеренный switch после уже открытого окна: 265.44 ms до / 254.75 ms после; для новой сцены 261.03 ms. Это не cold startup всего приложения и не изолированное первое создание Inspector. Warm-up отделён от median/P95. Дополнительный диагностический baseline дал 134.56/314.74 ms; промежуточные прогоны также колебались. Наблюдаемые изменения timing не выдаются за доказанную performance-оптимизацию: специальная оптимизация Inspector в production **не внедрялась**.

В финальном сопоставимом замере median selection=56.00 ms, последующий UpdateLayout=95.31 ms, stage RefreshPropertyInspector=22.15 ms / 1.70 MB и ровно один вызов на switch. Medians стадий не складываются в median суммы. Для новой сцены: selection=58.86 ms, UpdateLayout=86.93 ms, RefreshPropertyInspector=24.76 ms / 2.00 MB, один вызов. Median rows: 37 у прежней / 40 у новой сцены. Новый Inspector содержит 1076–1383 native visual nodes в зависимости от control; это больше, чем число RowVM.

GC 0/1/2: до 63/62/15, после 63/63/15; новая сцена 85/78/20. Высокая стоимость materialization/layout и allocations остаётся **P2**. Существенного ухудшения сопоставимой сцены в этой серии не наблюдалось; отдельный устойчивый выигрыш от изменений не доказан. Полные observations/stages сохранены в `artifacts/universal-phase2/inspector-before.json`, `inspector-after-final.json`, `inspector-new-controls.json`; команды воспроизведения — в `smoke-tests/UniversalMetadata/README.md`.

Root cause на уровне измеренных стадий: один RebuildPropertyGrid, а не несколько; metadata rebuilds=0; значительная стоимость приходится на повторную materialization/layout дерева редакторов. В baseline активный Inspector содержал около 1272 native visual nodes для Slider. Скрытый Legacy inspector имел 0 visual descendants и не доказан bottleneck; его binding не удалялся ради предположительного ускорения. Glyph buttons занимали лишь часть дерева, поэтому глобальная/локальная смена темы ради неподтверждённого выигрыша не выполнялась.

Рециклинг visual rows потребует отдельной проверки stale editor/context/callback safety; mutation контекста прежней RowVM ради скорости здесь не внедрялась. Кэш metadata не удерживает VM/controls. Native removed-control WeakReference, новые Preview collectibility checks и прежний Preview Memory suite проверяются отдельно от Working Set процесса. Production GC.Collect не добавлялся.

## 9. Build / smoke / generated runtime

| Проверка | Результат |
|---|---|
| Release solution build | **0 errors, 1 прежний NU1603**: VS SDK Analyzers 17.7.20 → 17.7.22 |
| Release smoke runner build | **0 errors, 0 warnings** |
| Universal Controls Phase 2 | **40/40** |
| Metadata Phase 1 / collectible cache | **45/45** |
| Grid-first / Grid Editor / Grid Drop | **26/26, 45/45, 40/40** |
| Nested Layout / Pointer-Marquee | **59/59, 26/26** |
| Undo history / Beta data integrity | **25/25, 16/16** |
| Preview consistency / Preview Phase 2 | **32/32, 28/28** |
| Stabilization / Layout 7A.2 | **25/25, 13/13** |
| PostgreSQL offline | **20 passed, 0 failed**; live явно SKIP |
| Optional plugin cold / missing theme / missing runtime | **3/3 отдельных процессов** |
| Broad regression | **72/72** на окончательном коде |
| SQL LocalDB DataGrid | **47/47** + отдельный Settings → Schema → BindingSource → оба Preview |
| Preview Memory | **36/36**, включая 20 cycles обоих режимов и CloseDuringQuery |
| SQL generated runtime | Restore/build/run PASS: Customers=3, Orders=2, видимые значения, независимые VM, resize |
| Новые generated Grid / Canvas apps | **2/2 PASS**, по 6 native controls, decimal/date/time, spinner/Calendar/picker popups, Canvas coordinates, resize |
| Общие generated apps | **3/3 PASS**: RegistryNative=23, RegistryCanvas=23, NestedNative=13 controls |

Автоматические VSIX/VsHost/IPC checks входят в broad: реальный запуск VsHost, общий DesignerSurface, complete package runtime, minimal patches, stale-version rejection и disconnect safety. Проверка установленного расширения в интерактивном Visual Studio этим не заменяется. Broad включает DataGrid, Eremex vertical slices, Multi Form, Favorites, Save/Load, Export и source-preserving Round-trip.

Generated fixtures сохранили прежний TargetFramework net6.0 и существующее предупреждение NETSDK1138 об окончании его поддержки. Оно не подавлялось и не устранялось обновлением TargetFramework в рамках этого этапа.

**Geometry comparison:** 800×600, 1200×800, 1600×900, нормализованный client viewport; Designer/Legacy/AXAML/отдельный exported runtime. Получено **708 geometry records, 531 сравнений, 0 mismatches, фактический max delta=0 DIP** в этом Windows окружении. Max вычислен из всех исходных records, а не только из превышений tolerance. Наличие native instances/attach подтверждено; этот результат не означает pixel-perfect rendering на других DPI/платформах и не отменяет намеренное clipping старого Canvas при уменьшении окна.

Финальная матрица `final-results.json`: 26 запусков/операций, все exit codes=0; live SQL результаты — отдельный `live-results.json`. LocalDB/memory 36 проверялись в первой полной матрице; последующие правки касались metadata Margin и тестового coordinate reference, DataGrid/SQL/Preview lifecycle после них не менялись. На окончательном коде suite 40 повторно проверил collectibility окон с шестью новыми controls и удалённого native control при живом Designer/VM.

Функциональных flaky failures в последних матрицах не наблюдалось. Сохранилось прежнее **artifact cleanup warning**: runner пытается удалить загруженные ToolboxStandardItemsExport/CommunityToolkit DLL до завершения процесса. Это не называется PASS очистки и не скрывается повторным запуском. После завершения процессов удалены только собственные временные bin/obj и тестовые plugin copies; логи, исходные failures и generated sources сохранены, в task artifacts осталось около 4 MB.

Промежуточные failures сохранены: 16/30 первого expanded suite включали неверные предположения harness о generated C# initialization и template part name, а также настоящие typed-row/toolbar проблемы. C# export использует InitializeComponent и generated AXAML, а не inline AvaloniaXamlLoader string; native picker part оказался PART_FlyoutButton. Исправлены test assumptions, проверки не заменены Model-only PASS. Расширенный suite затем выявил source constraint mismatch; он исправлен отдельно.

Первая полная матрица дала Preview consistency **29/32** и FAIL native Canvas runtime — это воспроизведение theme Margin defect, не flaky retry. После export fix — 32/32 и runtime PASS. Новый усиленный coordinate check сначала дал 39/40 из-за неверного nearest-Canvas reference внутри wrapper; reference заменён на общий Preview host, затем добавлена проверка native Designer coordinates и Margin normalization в factory. Промежуточная ошибка namespace в новом harness также исправлена; она не относится к product runtime. Окончательный 40/40 включает все эти проверки.

## 10. Изменённые файлы

Production:

* `DesignerSystem/Metadata/StandardControlProperties.cs` — reviewed types/properties, общий codec/factory/export policy.
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs` — date/time editors/codecs.
* `DesignerSystem/Metadata/MetadataCoverageReport.cs` — отдельные capability columns и contextual accounting.
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs` — регистрация шести типов через существующий descriptor.
* `ViewModels/MainWindowViewModel.cs`, `MainWindowViewModel.Metadata.cs`, `PropertyGridRowViewModel.cs` — shared Inspector rows, точное сравнение значений, bridge/export.
* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`, `AxamlImportService.cs`, `AxamlPhase4Projection.cs`, `AxamlLayoutProjection.cs` — общая metadata/source projection и исправление stale constraints.
* `Views/DesignerSurface.axaml` — toolbar над viewport.

Tests/docs:

* `smoke-tests/FormDesigner.ExportSmokeTests/Program.UniversalControls2.cs`, `Program.cs` — suite/profile flags и harness.
* `smoke-tests/UniversalMetadata/Phase2RuntimeProbe.cs.txt`, `ProbeRuntime.ps1`, `README.md` — запуск native generated apps и инструкции.
* `Docs/UniversalControlCoverage.csv`, `UniversalPropertyCoverage.csv`, `UniversalCoverageSummary.json`, этот отчёт — воспроизводимый coverage.

NuGet, TargetFramework, JSON schema, Preview/Export architecture, VSIX/IPC и глобальная тема не изменялись. Служебные benchmarks/logs/fixtures находятся в игнорируемом `artifacts/universal-phase2`; они не добавляются в Git.

## 11. Оставшиеся ограничения и ручная приёмка

* Обычные JSON projects и проверенные scalar source edits поддержаны; произвольный imported AXAML остаётся partial workflow.
* Physical mouse/system drag, DPI/несколько мониторов, внешние UIA clients, интерактивно установленный VSIX в devenv.exe, Linux/macOS runtime этим запуском не проверялись. Routed Windows UI/IPC не называются физическим acceptance.
* PostgreSQL live не проверен без сервера; offline tests не заменяют live integration.
* Nullable/date/time edit в Inspector использует validated text format; сложные collection/template/content editors отложены.
* Metadata DefaultValue — значение регистрации AvaloniaProperty. Вычисленные theme/resource defaults могут отличаться; они не выдаются за прочитанные effective values живого native control.
* Полный lifecycle стороннего PluginLoader/unload не переписывался; проверено сохранение существующего optional Eremex boundary и collectible metadata cache.
* Ручной checklist: первая строка Grid после открытия проекта, клики по toolbar и узкому Separator, spinner/nullable values, Calendar keyboard/mouse, date/time popup accept/cancel, Favorites/search/undo после form switch, реальные DPI, installed VSIX ApplyPatch/conflict и проекты с авторскими templates/resources.

## 12. Рекомендации Phase 3

1. Отдельно исследовать безопасное сохранение/рециклинг Inspector visual tree с неизменяемыми edit-context guards; не заменять это одной глобальной mutable RowVM.
2. Следующая небольшая группа — single-content containers ScrollViewer/Viewbox/Expander с общей child metadata и единым native/JSON/export поведением.
3. Затем item hierarchy для TabControl/ItemsControl/TreeView и collection/selection editors; отдельно Menu/ContextMenu/Flyout attachment и commands.
4. Расширять reviewed property policy только с runtime, source-preservation и memory tests; discovery не является автоматическим разрешением Toolbox/Export.

Phase 2 завершается на этом. Phase 3 автоматически не начинается.
