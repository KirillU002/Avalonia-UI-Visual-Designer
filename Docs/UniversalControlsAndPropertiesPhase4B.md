# Universal Controls — Phase 4B: AXAML Import performance, Preview fidelity и round-trip

Дата: 15.11.2026. Baseline — `820a56adb94bc051c90b3c9fec0edb78a82b972f`, `main`, исходное дерево Git чистое. Изучены Phase 4A и связанные отчёты Phase 1–3B.3. Beta 0.1; Avalonia 11.1.1; Windows x64 10.0.19045; runtime .NET 8.0.32, SDK сборки 10.0.402. Версии пакетов и TargetFramework не менялись. Коммит, tag и release не создавались.

## 1. Итог и границы

Регрессия CPU/allocations **самого импорта** большого MainWindow устранена на сопоставимом benchmark. Native projection также ускорена. Полное открытие Designer/Inspector существенно не ускорилось, а более точный imported Preview требует больше CPU и allocations, чем прежний упрощённый модельный Preview. Эти результаты не объединяются в утверждение об ускорении всего приложения.

Imported Preview использует существующий native builder `AxamlLayoutProjection`, которым DesignerSurface измеряет source layout. Произвольный пользовательский AXAML не загружается runtime-loader ради отображения: bindings, пользовательские constructors, resources и templates не исполняются. JSON Preview/Export сохраняют прежние pipelines и настройки.

**AXAML PERFORMANCE & PREVIEW: READY WITH CONDITIONS** для проверенного safe literal subset. Это не готовность произвольного импортированного AXAML и не утверждение об устранении UIA AV.

## 2. Root cause и изменения импорта

После warm-up счётчик `AvaloniaMetadataCatalog.BuildCount` не увеличивается: повторное reflection discovery не оказалось основной причиной. На MainWindow выполняются 52 139 разрешений свойств и 1 502 построения source reference за один импорт. Затраты создавали повторные LINQ/enumerator/alias allocations, создание известного набора attribute names для каждого instance, многократное материализованное `SourceMap.Controls`, повторное вычисление LocalName и подробный текстовый отчёт со всеми XML paths даже при выключенной подробной диагностике.

Минимальные изменения:

* `AxamlSyntax`: LocalName вычисляется один раз для неизменяемого Name; FindAttribute использует прямой проход.
* Прежний bounded source-property cache расширен неизменяемым набором attribute names для каждого доверенного framework type. Alias resolution и ambiguity check сохраняют приоритет aliases и отказ от неоднозначного patch. Второго metadata engine нет.
* `HasSourceValue` проверяет qualified names без повторной конкатенации строк и LINQ. Проверки expressions, namespaces и property capabilities сохранены.
* `AxamlSourceMap.Controls` — стабильное read-only представление одной коллекции. Индекс ByControlId остаётся O(1); добавление/замена сохраняет прежний порядок. Карта строится до публикации и заменяется целиком при rebase.
* Полный текст Structure Report создаётся лениво при открытии отчёта. Summary/blockers/capabilities доступны сразу; verbose mode сохраняет подробные события. Отдельный regression сравнивает полный текст обычного и verbose отчётов.
* В native builder один parent lookup заменяет повторные обходы всех models для каждого узла. Убрано дублирующее применение source layout для controls, уже созданных Phase4Projection. Нативный Avalonia Measure/Arrange сохранён.

Нет глобальных ссылок на документы, controls или plugin AssemblyLoadContext. Расширенный static cache ограничен уже разрешёнными типами стандартной библиотеки; lifecycle существующей plugin metadata/CWT не менялся.

## 3. Методика измерений

Before — отдельный `git archive` исходного commit с теми же benchmark methods и, для stage profile, только такой же instrumentation. After — текущий код. Release, один Avalonia/.NET runtime, process-local NativeCapture, одинаковые fixtures. MainWindow совпадает побайтно: SHA-256 `06DDDF98789FD874FBB8C871022E49312DD5C1A74987C6618FCFBE22A0F17697`.

5 warm-up и 30 measured observations каждого import/patch/initialization сценария; interactions — 100 observations по N0…N99 после warm-up. P95 — nearest rank, `ceil(0.95*n)`. MB — 1 000 000 bytes; allocations — UI/current managed thread, GC — process-wide collections. Peak Working Set — process high-water mark, не retained managed heap и не изолированный peak одной операции. Stage measurements вложены и не суммируются. Cold start не смешивается со steady state.

Свои сборки, SQL и UI suites не выполнялись параллельно с benchmark. В системе обнаружена конкурирующая CPU-нагрузка другого dotnet-процесса; он не останавливался. Поэтому абсолютные P95/variance имеют ограничение среды. Все наблюдения, включая выбросы, сохранены.

## 4. Import / patch Before–After

Таблица без детальной instrumentation (`UNIVERSAL4B_STAGES=0`):

| Сцена | Import median/P95 до, мс | После, мс | Alloc до → после, MB | Patch median/P95 до → после, мс | Patch alloc, MB |
|---|---:|---:|---:|---:|---:|
| Small | 1.46/3.17 | 1.12/1.58 | 0.801 → 0.236 | 1.68/1.83 → 1.55/3.51 | 0.628 → 0.439 |
| Medium | 16.66/29.95 | 9.98/12.65 | 7.690 → 2.118 | 21.08/40.26 → 24.87/27.71 | 5.999 → 4.157 |
| MainWindow | 99.35/120.21 | 49.45/70.22 | 134.039 → 35.296 | 60.83/72.14 → 65.81/73.17 | 51.318 → 38.111 |

MainWindow import: median −50,2%, allocations −73,7%. Это ниже исторических 76,46 мс Phase 3B.3, но историческое число не является повторным сопоставимым прогоном старого функционального объёма. Основное доказательство — парный Phase4A/4B benchmark выше.

Полный patch MainWindow не ускорился: median вырос примерно на 8%, несмотря на снижение allocations. Writer выполняет прежние проверки всего документа; алгоритм patch/rebase в этой фазе не переписывался. Medium patch также не показал CPU-ускорения. Это оставшийся P2, а не скрытый положительный результат. Первый intermediate after-run дал 47,67 мс import и 62,24 мс patch; финальный повтор 49,45/65,81 мс не заменяет его.

| Сцена | GC0/1/2 до → после (30 пар Import+Patch) | Peak WS до → после, MB | σ import до → после, мс |
|---|---:|---:|---:|
| Small | [3, 1, 1] → [1, 0, 0] | 144.1 → 137.8 | 1.24 → 28.48 |
| Medium | [25, 15, 1] → [11, 7, 0] | 155.7 → 148.7 | 6.37 → 1.38 |
| MainWindow | [352, 83, 22] → [152, 76, 23] | 201.6 → 191.9 | 8.39 → 8.45 |

У Small после изменения σ=28,48 мс из-за сохранённого единичного выброса за P95; он не удалялся. Metadata discovery после warm-up: 0 новых builds во всех трёх сценах до и после.

## 5. Детальные стадии MainWindow

В этом отдельном прогоне instrumentation включена, поэтому его timing/bytes нельзя подставлять вместо общей таблицы. SourceMapProjection включает вложенные construction/resolution stages. Property discovery отсутствует в warm path, работает готовый catalog.

| Стадия | Calls/import | Median/P95 до → после, мс | Alloc до → после, MB | GC0/1/2 за 30 стадий до → после |
|---|---:|---:|---:|---:|
| FileModelConstruction | 1502 | 0.59/0.68 → 0.62/3.07 | 1.211 → 1.211 | [0, 0, 0] → [0, 0, 0] |
| MetadataLookup | 1502 | 0.17/0.19 → 0.18/0.23 | 0.132 → 0.132 | [1, 0, 0] → [1, 1, 0] |
| ModelConstruction | 1502 | 0.51/3.52 → 0.49/3.11 | 1.250 → 1.250 | [3, 0, 0] → [0, 0, 0] |
| PropertyElements | 1502 | 1.29/9.19 → 0.73/4.78 | 3.357 → 1.322 | [8, 3, 1] → [4, 2, 1] |
| PropertyResolution | 52139 | 18.76/32.55 → 7.03/19.82 | 25.111 → 4.588 | [39, 12, 2] → [11, 5, 2] |
| SourceMapProjection | 1 | 80.96/96.12 → 61.38/66.67 | 108.997 → 38.696 | [207, 44, 11] → [78, 39, 9] |
| SourceReferenceGeneration | 1502 | 6.44/13.04 → 5.77/21.44 | 7.218 → 6.866 | [18, 3, 0] → [11, 5, 1] |
| StructureReport | 1 | 13.12/31.73 → 2.08/2.84 | 28.326 → 1.085 | [53, 9, 2] → [0, 0, 0] |
| SyntaxParse | 1 | 1.37/4.16 → 4.21/9.73 | 2.710 → 2.731 | [4, 0, 0] → [32, 16, 16] |
| TypeResolution | 1505 | 1.17/2.90 → 0.37/0.57 | 2.894 → 0.133 | [3, 0, 0] → [0, 0, 0] |
| ValueConversion | 3433 | 2.59/4.09 → 2.79/7.35 | 0.900 → 0.897 | [2, 0, 0] → [1, 0, 0] |

Parse alloc практически прежние; его timing в последнем stage-run вырос одновременно с 16 Gen2 collections внутри измеренных parse scopes. Parser не переписывался. Ускорение доказано для property resolution/report/source map и общего import, а не для XML parser.

## 6. Большой документ: model / native projection / source map

На настоящем MainWindow: 5 warm-up + 30 observations, viewport 1200×800. Native Measure/Arrange — тот же off-screen метод, которым пользуется DesignerSurface, но это не физический показ всех 1502 wrappers в полном окне.

| Операция | Median/P95 до → после, мс | Alloc до → после, MB | GC0/1/2 до → после |
|---|---:|---:|---:|
| Large.ApplyDocumentInspector | 77.09/91.50 → 90.29/112.06 | 30.422 → 30.422 | [32, 7, 0] → [32, 21, 0] |
| Large.NativeMeasureArrange | 474.68/713.83 → 192.73/324.02 | 88.331 → 61.273 | [162, 127, 3] → [111, 45, 1] |
| Large.SourceLookup100 | 0.07/0.08 → 0.07/0.15 | 0.000 → 0.000 | [0, 0, 0] → [0, 0, 0] |

Peak WS этого процесса: 2904,8 → 1984,3 MB. Это не доказательство отсутствия retention. ApplyDocument/Inspector не получил заметного улучшения, allocation этого этапа остался 30,42 MB, median вырос 77,09 → 90,29 мс без изменения кода этого этапа. Ускорение здесь не заявляется. Физическое открытие всего большого дизайнерского окна 30 раз не измерялось.

## 7. Реальные interactions и инициализация

100 NumericUpDown в source StackPanel; выбор N0…N99, изменение 100 Value, реальные patches, Undo и Redo. N0 первой измеряемой selection уже мог быть выбран warm-up; остальные переходы меняют control. История — один pending edit с откатом, не H30 и не 30 батчей по 100 edits. Initialization включает создание/показ полного окна Designer и UpdateLayout; Preview включает генерацию export snapshot и штатный loading overlay.

| Операция | n | Median/P95 до → после, мс | Alloc до → после, MB | GC0/1/2 до → после |
|---|---:|---:|---:|---:|
| Select | 100 | 21.56/27.39 → 22.56/33.68 | 3.493 → 3.474 | [12, 3, 1] → [9, 1, 0] |
| Edit | 100 | 16.13/23.56 → 16.63/25.06 | 5.387 → 5.389 | [12, 1, 0] → [12, 2, 1] |
| Patch | 100 | 13.01/17.56 → 9.72/20.27 | 6.780 → 4.938 | [61, 8, 0] → [25, 2, 0] |
| Undo | 100 | 54.18/64.62 → 61.28/151.98 | 12.847 → 12.805 | [16, 8, 8] → [51, 22, 17] |
| Redo | 100 | 56.71/65.77 → 59.78/78.13 | 12.847 → 12.806 | [45, 12, 8] → [41, 10, 4] |
| Initialize | 30 | 2016.01/2847.47 → 2173.96/2749.29 | 441.883 → 446.590 | [798, 659, 11] → [809, 686, 13] |
| Preview | 30 | 465.39/552.18 → 596.63/918.15 | 38.886 → 82.213 | [49, 39, 1] → [126, 102, 1] |

Peak WS полного initialization/preview процесса: 3287,9 → 3326,4 MB. Median selection/edit почти прежняя; **Undo P95 64,62 → 151,98 мс**, median 54,18 → 61,28 мс; Gen2 collections в этих 100 Undo 8 → 17. Ускорение Undo/Redo не заявляется: это измеренный P2. GC и конкурентная нагрузка ограничивают вывод о стабильной величине хвоста, но отрицательный performance result не исключён из таблицы. RebuildVisualTree вызывается 3 раза на initialization в обеих версиях, около 1,35 с суммарно: дополнительных ApplyDocument/rebuild от оптимизации не выявлено. Рефакторинг этого существующего пути не выполнялся.

**Цена fidelity:** imported Preview median 465 → 597 мс, allocations 38,89 → 82,21 MB. Shared builder теперь строит настоящую вложенную native hierarchy, а не прежнюю упрощённую экспортную модель; measure/templates/arrange остаются дорогими. Собственно `AXAML.PreviewProjection` в первом after-run занимала около 29 мс/6,99 MB; остальное — snapshot/window/templates/layout/loading. Удаление дублирующего source apply снизило allocation, но не устранило общий рост. Это открытый P2 и условие готовности. Task.Delay ради маскировки задержки не добавлялся.

Дополнительный парный benchmark: **100 последовательных Edit → Patch → MarkAxamlRoundTripSaved/ACK**, 5 warm-up, 100 controls. Измеряется core rebase, без VS text buffer/IPC и без physical render. Проверены стабильный ControlId и отсутствие phantom patch после каждого ACK. Median/P95 **32,87/107,22 → 34,49/107,67 мс**, allocations **27,019 → 17,536 MB**, GC0/1/2 **[153,38,9] → [99,39,8]**, process peak WS **302,0 → 305,7 MB**. CPU gain не достигнут; full validation/reparse/rebase остаются bottleneck. Raw data — ack-before/after.json. В отличие от previous interaction loop, source baseline здесь действительно обновляется после каждого patch.

## 8. Preview fidelity: root causes и исправления

Прежние Preview создавали JSON/export model. Source типы, сохранённые через Group, и source-only values (четырёхсторонние Padding/BorderThickness/CornerRadius, FontFamily/Style и др.) терялись или упрощались. Предупреждение фиксировало расхождение, но не исправляло его.

Теперь оба режима imported Preview создают trusted native tree из source references и текущих model values тем же builder, который уже применяется при измерении DesignerSurface. Нового layout engine нет. Phase4 factory возвращает native Button/TextBox/TextBlock/CheckBox/Border; вложенный Content остаётся control hierarchy. Common literal properties применяются через существующие metadata/accessors и codec, сложные values не преобразуются в строковые substitutes.

Выявленные дополнительные причины:

* Общие defaults модели перезаписывали native/style Alignment и FontSize даже без source attribute. Применение ограничено явно заданными/изменёнными local values. Это воспроизведено на Wrapped Button, исправлено и проверено геометрией.
* В Designer wrappers source Margin мог применяться повторно; у native view он обнуляется после расчёта frame. Native Opacity не умножается повторно на visual layer.
* Импортированный root Canvas терял authored размеры/background; literal root values сохранены. Window background не перекрывает явно заданный Canvas background.
* TabItem measurement wrapper нужен off-screen расчёту, но не Preview: настоящий TabItem/Content сохраняется в Preview. Off-screen items realization не подменяет ItemsPanel реального Preview.
* Прежний test GridDefinitions ошибочно приравнивал extent Star-ячейки к Bounds Button. Runtime исходного fixture подтвердил native центрирование. Дополнительно найден реальный off-screen font gap: 38×29 вместо 41×30 DIP из-за отсутствия Window typography у Decorator. MeasurementRoot наследует значения существующих ControlContentThemeFontSize/ContentControlThemeFontFamily ресурсов; константная FontSize на каждый control не вводилась. Runtime и off-screen теперь 41×30.
* Pending Canvas insert до ACK не имел source reference и терял Background/IsVisible/Opacity в новой projection. Это воспроизведено в обоих Preview (0/2), исправлено применением тех же common metadata/scalar accessors для pending model, затем 2/2. Схема source insertion/patch не менялась.
* x:Name используется как native Name; input source остаётся нетронутым. Imported RequestedThemeVariant применяется только к PreviewWindow, глобальная тема Designer не меняется.

## 9. Coverage и фактическая геометрия

MainWindow source capabilities не расширялись искусственно: прежние 1502 projected controls / 1505 visual candidates, 3 opaque custom visual nodes; Phase 4A Inspector attributes 2264 не превращаются в обещание редактирования всех Avalonia properties.

| Компоненты | Проверено | Ограничение |
|---|---|---|
| Button, TextBox, TextBlock, Border | Native type, visual-root attachment, Bounds, Margin/Alignment/Opacity; styled scalars; actual Preview source Padding/FontFamily/FontStyle, Background/Foreground/BorderBrush/IsEnabled; Designer scalar test | Не все properties и не произвольный template |
| Grid, Canvas, StackPanel, DockPanel, WrapPanel, ScrollViewer | Native type и Bounds; Login/Nested с ParentId, Grid cells/definitions, Dock, wrapping, nested Content | Designer overlays/backdrops остаются служебной проекцией |
| TabControl, Menu, ComboBox, ListBox, TreeView | Native geometry; оба реальных Preview: menu popup/items/Separator, ComboBox/ListBox selection, Tab switching/content, TreeView expand/visible leaf | Imported structural Add/Remove/Reorder по-прежнему запрещены |
| Calendar, DatePicker, TimePicker, Slider, ProgressBar, ToggleSwitch | Native type, actual visual tree, Bounds и placement; typed values в существующих Phase2 tests | Это literal subset, не универсальная поддержка bindings/templates |
| DataGrid в source AXAML | Явный opaque warning с типом/именем/reason, стандартный sibling Button остаётся виден | Source DataGrid adapter/Columns не реализован этой фазой; не заявляется rendered |
| DataGrid JSON Designer | Существующий registry/Preview/export/BindingSource pipeline | Результат live SQL см. final matrix |

Новые доверенные Login/Nested fixtures сравнивают native runtime исходного AXAML и shared native projection при одинаковом client area **800×600, 1200×800, 1600×900**, без Window chrome. Проверяются реальные X/Y/Width/Height, Margin, alignment, тип и attachment; допуск 1 DIP. Canvas.Left/Top 23/37 не адаптируются к resize. Это не screenshot/pixel-perfect тест произвольного source проекта.

Проверены 16 существующих файлов Layout/{GridStackPanel,GridDefinitions,DockPanel,DeepNested,Bindings,Styles,Preservation}, Coverage/{Tabs,Content,Wrap,Preserved}, Phase4/{Combined,ComboStatic,ComboBindings}, Phase5/{Tree,List}. Для них проверено сохранение исходного AXAML и реальные capability counts. Styles/Bindings fixtures намеренно не выдаются за полностью совпавший runtime.

## 10. Property elements, opaque и диагностика

Button.Content/Border.Child/TabItem.Content сохраняют вложенную native hierarchy; Grid.RowDefinitions/ColumnDefinitions используют существующий Grid parser/constraints. Scalar property elements используют прежние source spans/typed codecs. ContextMenu attachments, DataTemplate/ControlTemplate, Resources/Styles и неизвестные properties не исполняются и не заменяются сгенерированным текстом.

В существующий PreviewControlDiagnostic добавлено RenderCapability: FullyRendered, PartiallyRendered, Placeholder, Opaque, Unsupported. FullyRendered относится только к проверенной safe literal projection; authored hidden control не считается потерянным. Partial root scope/styles распространяется на итоговую capability controls. Opaque source nodes получают диагностический source-span ID, поскольку model ControlId у них отсутствует. Фиктивные placeholders для обхода ошибок не создаются.

Warnings агрегируются: несколько примеров control/property/reason, общее число partial/opaque nodes и явное сообщение, что исходный AXAML не изменяется. Подробные per-control events — только при FORMDESIGNER_PREVIEW_DIAGNOSTICS=1, не каждый кадр. Непроецируемые root Window properties (например inherited FontSize/Padding/DataContext) имеют отдельное агрегированное предупреждение; Background/размеры документа и literal RequestedThemeVariant обработаны.

При ошибке native tree construction показывается существующая диагностика LayoutProjectionFailed; source остаётся нетронутым. Imported native branch не запускает молчаливую полную регенерацию/fallback. JSON preview mode, Legacy option, plugin isolation и настройки пользователя не менялись.

## 11. Round-trip, PropertyGrid и VSIX

Writer/protocol не переписывались. Edit → Reset → Undo → Redo сохраняет quotes, whitespace, XML escaping, comments, namespaces, unknown attributes, expressions, Styles и Templates. Scalar property-element edits остаются span edits; ambiguous mapping/foreign owner/duplicate aliases блокируются прежними capabilities. ACK заменяет source baseline/map, открытое Preview владеет отдельным wrapper старого snapshot и не изменяется от ACK редактора.

Visual Studio остаётся владельцем text buffer. ExpectedVersion/checksum, ApplyPatch/ACK и conflict detection сохранены. Source map не пересчитывается при простой selection; безопасный полный rebase после ACK остаётся прежним. Generated patched-source runtime проверяется отдельно от JSON Export; модельный Export импортированного source не объявляется полной реконструкцией исходного приложения.

Автоматические VSIX/VsHost/Named Pipes checks не заменяют ручную проверку установленного расширения в devenv.exe. Avalonia/Eremex в процесс Visual Studio не переносились.

## 12. Memory и UI Automation

После Close Preview сбрасываются owned source wrapper и source metadata fields, generation guard отменяет поздний layout completion. Source wrapper содержит syntax/map/capabilities, но не VM/session. Старый документ собирается после закрытия документа, даже если закрытые PreviewWindow намеренно удерживаются. Новый тест: 20 Legacy + 20 AXAML close/document-close cycles, 40 WeakReference source maps собраны.

Первый вариант memory test был красным (49/50): он ожидал GC SourceMap открытого редакторского документа. Подтверждена цепочка `PropertyGridRowViewModel.ValueTrace` (static delegate) → VM → ActiveAxamlSourceDocument → SourceMap; VM в harness продолжал владеть открытым документом. Исправлена семантика теста: New Project закрывает source document, закрытый PreviewWindow удерживается намеренно. Production lifetime/GC не менялись ради зелёного результата. Это не доказательство освобождения всех создаваемых harness VM; их static trace root также ограничивает интерпретацию peak memory initialization benchmark.

Исходный UIA `ProviderCallouts::GetPatternProvider` null-provider AV остаётся **UNRESOLVED**. Место падения не названо root cause. Accessibility не отключалась, interop/GCHandle/COM не менялись. Использовался существующий process-local full crash capture/evidence vault с уникальными каталогами, metadata и hashes. Старые dumps не удалялись; бинарные dumps не добавлены в Git. В этой фазе проверены 65 capture manifests: Captured=0, CaptureFailed=0, TimedOut=0. Шесть ненулевых exits относятся к сохранённым assertion failures (geometry, memory expectation, pending insertion и Broad), а не native AV. Ноль новых captures не означает исправление исходного AV.

## 13. Build и regression

| Проверка | Результат |
|---|---|
| Solution Release / VSIX packaging | PASS, 0 errors; один прежний NU1603 SDK Analyzers 17.7.20 → available 17.7.22 |
| Smoke runner build | PASS, 0 errors, 0 warnings |
| Phase 4B | **52/52**, включая pending Canvas insertion, native source brushes/state, source scope diagnostics и memory |
| Phase 4A | **38/38**, последний targeted repeat |
| PropertyGrid / Collections | **30/30 / 30/30** |
| Metadata / Universal Phase2 | **45/45 / 40/40** |
| Reset/Undo 3B.3 / Stabilization | **15/15 / 25/25** |
| Broad normal environment | **72/72** после typography fix; первый 71/72 сохранён |
| Nested / Pointer / Grid Drop / Grid Editor / Layout7A | **59/59 / 26/26 / 40/40 / 45/45 / 15/15** |
| PostgreSQL offline | **20/20**, live SKIP без сервера |
| SQL Server LocalDB | **47/47**, CONTRACT/LIVE помечены раздельно; реальные Customers=3, Orders=2 |
| SQL source workflow | PASS: settings → schema discovery → BindingSource → оба Preview |
| Preview Memory с live SQL | **36/36**, 20 Legacy + 20 AXAML, CloseDuringQuery, DataContext/ItemsSource/UIA/animation regressions |
| SQL generated runtime | restore/build/separate-process run PASS, реальные видимые cells, независимые VM/forms, resize |
| Property-element IPC | **1/1**, 3 последовательных изменения, version/checksum/Apply/ACK/rebase |
| Generated collections Grid / Canvas | restore/build/run PASS, native roots=6, Menu/Separator, Combo/List, Tab content, Tree, ContextMenu/Flyout, resize |
| Compiled patched-source runtime | restore/build/run PASS, scalar values, nested controls, popup/selection, tabs/tree, 3 размера |
| Eremex/plugin isolation / VSIX/VsHost | Existing vertical slices, dependency closure и IPC в Broad PASS; ручной devenv.exe не проверен |

Полная матрица: `artifacts/universal-phase4b/final-validation3`, итог FINAL_ACCEPTANCE_PHASE4B_PASS. После последнего узкого уточнения diagnostics (root expressions/templates, FormId fallback) выполнены новый solution build и targeted **52/52 + 38/38**, `verified-*.log`. Это отдельный финальный повтор, а не замена первых красных прогонов. Финальный inventory — `inventory-verified.json`: 2537 XML nodes, 1505 visual candidates, 1502 projected controls, 3 opaque visual nodes, 62 property elements; 4391 recognized present attributes, 2955 source-patchable present attributes, 42638 потенциальных editable slots и 810 preserved expressions. Slots не равны проверенным Inspector rows.

Изолированный instance `UniversalMetadata_6796d48979e447a186182dda9f3a37ee` остановлен и удалён в finally. Пользовательские базы не затрагивались. Generated probes использовали собственные test exports и восстанавливали startup после завершения; net6 generated projects сохранили прежний NETSDK1138. В Broad встречаются прежние cleanup warnings locked DLL/UnauthorizedAccessException: загруженные generated assemblies не удалось удалить в этом процессе; это не скрыто и не исправлялось принудительной очисткой.

## 14. Первые красные прогоны и ограничения среды

* `fidelity-build.log`: compile error теста — ClientSize имеет непубличный setter. Тест задаёт Window Width/Height без chrome и проверяет равенство фактических client areas.
* `checks-first.log`: 6/7, реальный mismatch native/style Alignment у Wrapped; исправлен production guard, затем geometry PASS.
* Первый final-validation: 49/50, описанный выше ошибочный lifetime expectation. Сохранён. При коррекции теста был отдельный compile error отсутствующего using ViewModels; лог сохранён, затем исправлен.
* Первый полный Broad в normal environment: **71/72**, GridDefinitions. Затем native-reference эксперимент отдельно подтвердил вертикальное центрирование исходного Button и выявил font gap 38×29 против 41×30. Исправлены typography measurement root и старое предположение теста; зафиксирован отдельный native PASS перед новым full run.
* `pending-red.log`: 0/2, потеря common state у новых Canvas controls до ACK; после минимального исправления 2/2.
* Baseline restore с параллельными MSBuild nodes завершился exit 1 без полноценной диагностики; `--disable-parallel -m:1` использовал тот же package cache успешно. NuGet/TFM для этого не менялись.
* Нет отдельного ручного VS acceptance, live PostgreSQL, Linux/macOS, screen-reader/multi-monitor/DPI матрицы.

## 15. Изменённые файлы

* DesignerSystem/AxamlRoundTrip/{AxamlControlMetadata,AxamlImportService,AxamlImportStructureReport,AxamlLayoutProjection,AxamlPhase4Projection,AxamlRoundTripModels,AxamlSyntax}.cs.
* Services/LayoutDropProfile.cs — opt-in stage GC counters.
* Views/MainWindow.axaml.cs; Views/PreviewWindow.{Consistency,Lifecycle}.cs; Views/PreviewWindow.axaml.cs.
* smoke-tests/FormDesigner.ExportSmokeTests/Program.cs, Program.AxamlLayout.cs, Program.Universal4A.cs, новые Program.Universal4B.cs и Program.Universal4BInteractions.cs.
* Samples/RoundTrip/Phase4B/{Login,Nested}.axaml.
* smoke-tests/UniversalMetadata/README.md; настоящий отчёт.

В ignored artifacts/universal-phase4b находятся before archive, raw profiles, summary.json, первые красные логи, capture manifests, isolated SQL и runtime outputs. Они не добавляются в Git.

## 16. Оставшиеся ограничения и следующий этап

1. Imported Preview не является sandbox для полного исполнения приложения: source styles/resources/templates, Binding/ViewModel/Commands, ContextMenu attachments и DataGrid source adapter остаются частично поддержанными/opaque. Root Window styling и composition Window/явно ограниченного Canvas тоже частичны; весь viewport/background/composition не прошёл pixel-perfect проверку. Исходный текст сохранён и ограничения показаны.
2. Preview fidelity повышена ценой измеренного роста CPU/allocations. До массового применения imported Preview нужен отдельный узкий profiling templates/layout/snapshot; автоматически такие оптимизации не начаты.
3. Полный patch/rebase и initialization Designer остаются дорогими; 3 rebuild на initialization не менялись. Большой MainWindow source import ускорен, но это не обещание мгновенного редактирования всего большого окна.
4. Structural editing imported collections остаётся read-only. Не вводился небезопасный XML node reorder.
5. UIA native AV/внешний client-dependent Window retention не закрыты; полный crash pipeline готов, исходные старые dumps не восстановлены.
6. Ручная Visual Studio: большой/повторно открытый AXAML, switching/close/restart VsHost, stale checksum conflict, Apply/ACK и Save/Undo в настоящем text buffer. Отдельно проверить реальные проекты, DPI и внешние UIA clients.

Рекомендуемый Phase 4C: сначала измерить рост стоимости native imported Preview и определить безопасный ограниченный scope Resources/ContextMenu/DataGrid adapters. Не начинать универсальное структурное редактирование до source-preserving алгоритма. Phase 4C в текущей задаче не начиналась.

## 17. Итоговая оценка

**AXAML PERFORMANCE & PREVIEW: READY WITH CONDITIONS.**

Обоснование: сопоставимый large import ускорен с 99,35 до 49,45 мс с сохранением capabilities/source text; native layout и проверенная Preview fidelity улучшены; Reset/Undo/Redo, source patches, IPC и compiled runtime проходят. Новых подтверждённых P0/P1 после исправления pending Canvas state не осталось в проверенной матрице.

Условия: imported Preview остаётся безопасной частичной проекцией, а не runtime произвольного source приложения; рост стоимости Preview и P95 Undo сохраняются как P2. Исходный UIA AV unresolved, live PostgreSQL и установленный VSIX требуют отдельной ручной/серверной приёмки. Не заявляется полное соответствие всех source resources/templates/Window scopes. Phase 4C не начиналась.
