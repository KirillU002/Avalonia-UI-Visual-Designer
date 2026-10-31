# Layout Designer Phase 3 — Visual Grid Definition Editor

Дата: 31 октября 2026 года. Основа: [LayoutDesignerArchitecture.md](LayoutDesignerArchitecture.md), [LayoutDesignerPhase2.md](LayoutDesignerPhase2.md). Исходный commit Phase 2 — `cece503`. Phase 4 не начата.

## 1. Результат и переиспользованный механизм

Расширен существующий Grid Editor: `LayoutDefinitionEditorItem`, `LayoutGridRows/Columns`, selected items, команды Add/Delete/Move, `MutateGridDefinitions`, `ApplyActiveGridDefinitions` и существующие session snapshots. Второй редактор или новая Document Model не создавались.

Добавлены реальные направляющие tracks, выбор строки/колонки, вставка до/после, безопасное удаление с подтверждением переноса, корректная обработка spans и resize с сохранением GridLength. Редактируются существующие definitions и attached properties, которые уже используют JSON, Preview и Export.

Нативные направляющие и pointer resize предназначены для JSON-форм с `LayoutSemanticsVersion=2`, введённых в Phase 2. Старые Canvas/v1 документы сохраняют прежний renderer; автоматической миграции нет. Их существующие команды definitions также используют безопасную структурную обработку. Импортированный AXAML в визуальном редакторе доступен только для чтения.

## 2. Выявленные проблемы существующего редактора

Анализ и UI-тесты выявили следующие причины неполного прежнего workflow:

- Блок definitions находился на вкладке «Свойства», тогда как кнопка Edit rows/columns переключала в Layout. Блок перенесён на существующую вкладку Layout, с сохранением bindings и editor items.
- `RebuildLayoutDefinitionEditor` всегда выбирал первый track. Теперь выбор сохраняется в пределах одной пары document session/container и сбрасывается при смене контекста.
- Удаление занятого track сразу меняло индексы/обрезало spans. Теперь требуется явный выбор пользователя.
- Перестановка учитывала только начальный индекс, но могла разрывать span. Такая перестановка теперь блокируется с объяснением.
- Общий `HistoryGroupingWindow` объединял даже завершённую Grid-команду с предыдущей правкой. Для явных Grid-команд введены границы поверх существующего undo batch; глобальная политика истории не переписывалась.
- Правка definitions через editor items не обновляла текстовую строку root Inspector. Теперь используется существующий `RebuildPropertyGrid` и его механизм deferred refresh.
- Смена формы с `refreshEditorSurfaces=false` не проходила общий reset Inspector. Временное состояние Grid сбрасывается также в `ClearActiveDocumentTransientState`.
- Визуальная проверка обнаружила старые равномерные layout guides поверх новых native guides. Для v2 в старом `GridOverlayCanvas` оставлена только фоновая координатная сетка; tracks рисуются по native measurement.

## 3. Визуальные направляющие

Существующий `AxamlLayoutProjection.JsonLayoutTree` дополнен transient metadata: `GridPanels` и `MeasuredGrids`. После настоящих Avalonia Measure/Arrange считываются `RowDefinition.ActualHeight` и `ColumnDefinition.ActualWidth`, а положение вложенного panel переводится относительно root. Padding, border, margin и размещение родительских контейнеров уже учтены Avalonia.

Снимок содержит bounds панели и фактический размер/definition каждого track. View только суммирует фактические интервалы для положения линий; распределение Auto/Star/Pixel не рассчитывается отдельным layout engine. Учтён конечный край fixed-only tracks, который может не совпадать с краем растянутого Grid. Пиксельное округление Avalonia сохраняется.

В MainWindow добавлен один служебный `GridDefinitionOverlay` Canvas. Линии, контур и полупрозрачное выделение имеют `IsHitTestVisible=false`. Интерактивные линейки находятся справа и снизу **за пределами пользовательской формы**, в том числе для nested Grid. Это позволяет выбирать tracks и тянуть разделители без перекрытия дочерних controls. Служебный viewport расширяется на место линеек только для native Designer; размер пользовательского Grid не изменяется.

Линейки показывают индекс, definition и tooltip с фактическим размером в DIP. Выбранный track подсвечивается. Очень короткие/нулевые tracks надёжнее выбирать в списке definitions: в линейке места для подписи может не быть. Grid overlay toggle управляет transient guides и не меняет авторские definitions.

## 4. Добавление и вставка

Существующие Add Row/Add Column добавляют `*` в конец через общий путь вставки. В панели доступны «Вставить до» и «Вставить после» для выбранной строки или колонки.

При вставке по индексу `i`:

- начало элемента `>= i` сдвигается на 1;
- если вставка попадает строго внутрь span, span увеличивается на 1;
- вставка непосредственно после конца span не расширяет его;
- ParentId, порядок controls и properties других осей сохраняются.

Операция затрагивает только непосредственных детей активного Grid. Вложенный Grid сохраняет собственные внутренние индексы; изменение его родительского placement перемещает весь контейнер естественным layout.

## 5. Удаление без потери controls

Пустой track удаляется сразу. Если track пересекает хотя бы один control/span, editor показывает предупреждение и кнопки «Перенести и удалить» / «Отмена». До подтверждения Document Model не меняется.

После явного подтверждения:

| Ситуация | Изменение |
| --- | --- |
| Control начинается после удаляемого track | Start уменьшается на 1 |
| Control с span>1 пересекает удаляемый track | Span уменьшается на 1 |
| Control целиком находится в удаляемом track | Перенос в следующую оставшуюся ячейку; для последнего track — в предыдущую |
| Начальный индекс оказался за новым концом | Ограничивается последним допустимым индексом |
| Span вышел за доступные tracks | Ограничивается оставшимся диапазоном, минимум 1 |

Controls из коллекции не удаляются. Удаление и перенос всех затронутых элементов — один шаг Undo. Последний track удалить нельзя: сохраняется ячейка 0/0. Пустая строка definitions, введённая через native Inspector, трактуется как один `*`; если это исключило бы занятые ячейки, изменение отклоняется.

Pending-операция привязана к session, Grid Id, исходным definitions и placement детей. Изменение контекста отменяет её; устаревшее подтверждение не применяется. Сокращение definitions/count через Inspector, затрагивающее controls/spans, отклоняется с предложением использовать безопасное удаление через Grid Editor.

## 6. GridLength и синхронизация Inspector

Существующий item хранит `SizeKind` и `Value`. UI показывает Auto / Star / Pixel; Pixel отображается через accessor к прежнему `Fixed`, без изменения сериализованного словаря режимов. Для Auto числовой редактор скрыт. Для Star вводится вес, для Pixel — размер в DIP.

Правки сразу обновляют общие definitions и Designer. Нулевые и дробные native GridLength из Phase 2 сохраняются без нового округления. Выбор track в линейке синхронизирован с существующим списком в Layout. Выбор control для Inspector при этом сохраняется.

Inspector и визуальный редактор используют один источник истины: `SurfaceGrid*Definitions` для root и `Grid*Definitions` control model для nested host. Счётчики обновляются вместе со строками definitions. После изменения вызывается существующий refresh Inspector; категории, Favorites и tooltip infrastructure сохранены. Прямое редактирование root и nested definitions проходит Avalonia parser и проверку безопасного сокращения.

## 7. Resize и pointer state

Resize доступен на внутренних разделителях активного Grid. Политика:

| Пара / режим перед разделителем | Поведение |
| --- | --- |
| Pixel | Меняется объявленный размер первого track; минимум 0 DIP |
| Star / Star с положительными весами | Меняются два веса с сохранением их суммы; масштаб delta берётся из фактического суммарного размера пары |
| Auto | Drag не включается; предлагается числовой/режимный editor |
| Star / Pixel, Star / Auto, нулевые Star | Неоднозначный drag блокируется; используется editor |
| Единственный или последний track без внутреннего разделителя | Размер меняется через editor |

Star resize сохраняет исходную пропорцию при нулевом delta. Доля каждого соседнего Star ограничена диапазоном 0.1–99.9% суммы пары, чтобы случайным drag не схлопнуть track. Нулевой вес остаётся доступен в числовом editor. Intrinsic размеры детей могут ограничивать видимое движение границы; окончательные bounds всегда определяет Avalonia.

Использована существующая interaction state machine. Добавлены Select Grid Row/Column и Resize Grid Row/Column; одновременно Marquee/Move/Control Resize не запускаются. Capture принадлежит стабильному overlay Canvas, поэтому пересоздание измеренных handles не теряет capture.

Во время движения изменяется только transient preview payload и native projection, без записи промежуточных значений в JSON. Release фиксирует итог через общий mutation/undo path. Escape, CaptureLost, смена формы и закрытие окна отменяют payload. При закрытии отмена начинается на `Closing`, до удаления captured visual, чтобы не оставлять capture на закрывающемся MainWindow. Состояние/definitions проверяются повторно перед commit.

Существующая структурированная pointer diagnostics сохраняет mode, control/Grid Id, position, capture и причины завершения. Логи на каждый PointerMoved не добавлялись.

## 8. Undo/Redo

Add/Insert/Delete/Move, изменение GridLength и commit resize используют прежние snapshots и undo batches. Перед явной Grid-командой завершается grouping с предыдущим изменением; после команды следующая правка также получает отдельную границу. Это локальная политика Grid Editor.

Отменённый resize не создаёт запись истории: модель не менялась. Подтверждённое удаление со сдвигом нескольких controls или spans восстанавливается одним Undo и повторяется одним Redo. Последовательные быстрые Insert-команды не сливаются друг с другом. История остаётся в текущей document session.

## 9. JSON Save/Load

Формат Phase 2 не изменён: версия 2.4, layout semantics 2, definitions/counts, ParentId, placement/spans и Auto/Fixed sizes. Новых persisted полей и миграций нет.

Guides, selected track, pending confirmation, measured bounds и resize payload в JSON не записываются. Проверена запись workspace в файл и повторная загрузка с definitions, режимами, порядком controls и изменённым placement. Для старого Canvas остаётся semantics 1 и прежний renderer.

## 10. Preview, Export и runtime

Legacy Preview и AXAML Preview получают уже существующие definitions из Document Model. Production Preview и Export pipeline не переписывались. В обоих режимах проверены actual DataGrid bounds после правок tracks и увеличения окна.

Export сохраняет настоящий Grid, attached placement и GridLength. Ни ruler controls, ни overlay lines, ни pending UI не являются частью документного дерева, поэтому не попадают в сгенерированный AXAML/C#. Старый Responsive export по-прежнему не преобразует native Grid.

Новый generated-project сценарий `GridEditorPhase3Export` выполняет реальные операции editor: Pixel track, Star weights, insert и удаление пустой строки. Дополнительно собираются прежние native Grid, Canvas+Grid и Grid+Eremex сценарии. Для runtime проверки используется существующий [ProbeGeneratedLayout.ps1](../smoke-tests/LayoutGrid/ProbeGeneratedLayout.ps1): создаётся сгенерированный MainWindow на установленном .NET runtime, показывается offscreen окно, меняются Width/Height и проверяется рост DataGrid. Временный test-only entry point восстанавливается, обычный generated binary пересобирается.

Финальный runtime результат и логи приведены в таблице ниже.

## 11. AXAML Import / Round-trip

Визуальные структурные операции для source-backed AXAML выключены через `CanEditGridDefinitions`. Коллекции editor остаются источником отображения, но Add/Insert/Delete/Move/size mutation не меняют исходник. Панель содержит явное русскоязычное read-only объяснение. Открытие панели не переключает layout и не регенерирует AXAML.

Существующие source-preserving property patches и AXAML import не изменялись. Проверяется отсутствие изменений source patch после попыток визуального редактирования. Отдельные regression scenarios покрывают comments, unknown attributes, raw content, styles и bindings. Полноценная source-preserving структурная правка RowDefinitions property elements, ресурсов или bindings в Phase 3 не реализовывалась.

## 12. Проверки и доказательства

Логи и снимок UI: `artifacts/layout-phase3/`.

| Проверка | Результат |
| --- | --- |
| Grid Editor contracts/native pointer/UI | 45/45 |
| Native Grid Phase 2 | 28/28 |
| Canvas pointer/marquee | 26/26 |
| DataGrid offline contracts | 18/18; live SQL Server — SKIP |
| PostgreSQL offline contracts | 20/20; live PostgreSQL — SKIP |
| Generated projects restore/build | 4/4 |
| DesignerSurface/Inspector/Favorites/Multi Form/AXAML/Eremex regression | 32/32, каждый с generated project build |
| Full solution, VsHost/VSIX | 0 ошибок; 1 прежнее предупреждение NU1603 |
| Generated Grid runtime resize | PASS: .NET 6.0.36, DataGrid 570×298 → 810×418 |
| `git diff --check` | PASS |

Native pointer tests используют реальный Avalonia raw input dispatcher и `InputHitTest`, не прямые вызовы pointer handlers. Проверены выбор track без потери selection control, Pixel/Star resize, отказ Auto, Release вне handle, Escape, CaptureLost, смена формы, закрытие окна и zoom 75%/150%. Зафиксирован текущий Windows `RenderScaling=1`; другая физическая DPI-конфигурация не эмулировалась и не объявляется проверенной.

Во время визуальной QA используется RenderTargetBitmap настоящего MainWindow; результат осмотрен. На снимке проверены расположение панели, modes/value editors, линейки и отсутствие дублирующих равномерных guides. Скриншот служит проверкой текущего Windows UI, а не матрицы тем/платформ.

### Команды воспроизведения

```powershell
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'Codex-AvaloniaReview-dotnet'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build .\FormDesigner.sln --no-restore -m:1
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --grid-editor-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --native-grid-checks
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --designer-pointer-checks
$env:SMOKE_SCENARIO_FILTER = 'GridEditorPhase3Export'
$env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS = '1'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\layout-phase3\export
$run = Get-Content .\artifacts\layout-phase3\export\latest-run.txt
& .\smoke-tests\LayoutGrid\ProbeGeneratedLayout.ps1 -ProjectPath (Join-Path $run 'GridEditorPhase3Export')
```

В массовых generated-project smoke запусках `NuGetAudit=false` задаётся только окружением процесса для исключения ожидания недоступного vulnerability feed. Файлы NuGet/project manifest и версии пакетов не менялись. Generated projects сохраняют существующий net6 target; предупреждения NETSDK1138 не исправлялись обновлением TFM.

## 13. Изменённые файлы

| Файл | Назначение |
| --- | --- |
| `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.Json.cs` | Actual track metadata после native layout |
| новый `ViewModels/MainWindowViewModel.GridEditor.cs` | Insert/remove/span/reorder safety, confirmation, transient resize, Grid undo boundaries |
| `ViewModels/MainWindowViewModel.cs` | Переиспользованные команды, сохранение track selection, Inspector refresh, reset per form |
| `ViewModels/MainWindowViewModel.NativeLayout.cs` | Безопасная правка definitions/counts root и nested host |
| `ViewModels/LayoutDefinitionEditorItem.cs` | UI mapping Pixel↔Fixed и уведомления modes |
| новый `Views/MainWindow.GridEditor.cs` | Native guides, внешние линейки, pointer routing/capture |
| `Views/MainWindow.NativeLayout.cs` | Measurement snapshot и transient resize projection |
| `Views/MainWindow.axaml` | Перенос существующего editor в Layout, selection, insert/confirmation UI, Auto numeric visibility |
| `Views/MainWindow.axaml.cs` | Rendering hookup, native gutter, исключение старых равномерных guides |
| `Views/MainWindow.DesignerInteraction.cs`, `Models/DesignerInteraction.cs` | Новые modes, cancel/close handling поверх прежней state machine |
| `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новый `Program.GridEditor.cs` | Grid Editor native/UI/serialization/preview/export tests |
| новый `Docs/LayoutDesignerPhase3.md` | Этот отчёт |

NuGet, TFM, global styles, SQL Server/PostgreSQL services, Eremex descriptors, Toolbox, Document Model и VSIX/IPC production-код не изменены.

## 14. Оставшиеся ограничения

1. Полный cell Drag & Drop не реализован. Новые компоненты по-прежнему добавляются в 0/0, placement меняется через Inspector. Phase 4 не запущена.
2. Направляющие/resize включены для native v2 JSON. Старые v1 Canvas/experimental layouts не конвертируются; их геометрия остаётся прежней.
3. В source AXAML visual structural editor read-only. Сложные minimal structural patches требуют отдельного этапа.
4. Удаление может привести к размещению нескольких controls в соседней ячейке. Это явная подтверждённая политика переноса с сохранением всех controls; автоматического поиска свободной ячейки нет.
5. Перестановка, которая сделала бы span несмежным, блокируется. Auto, смешанные Star-пары, нулевые Star и край без внутреннего separator редактируются через поля, без автоматического перевода в Pixel.
6. При нулевых/очень коротких tracks подписи могут не помещаться в линейке; используются список definitions и tooltips. SharedSizeGroup, Min/Max definition constraints и произвольные resource bindings не добавлены в JSON.
7. Resize вызывает повторное native measurement и отрисовку Designer. Stress/performance matrix на сотнях тяжёлых plugin controls не проводилась.

## 15. Ручная проверка

- Физическая мышь, частые движения, двойные клики, PointerReleased за пределами окна, переключение приложений во время resize.
- Несколько мониторов с Windows DPI 125/150/200%, перенос окна между ними, zoom 25–200% и scrolling к nested Grid.
- Читаемость длинных definitions и маленьких/нулевых tracks, узкий правый dock, светлая/тёмная темы.
- Большие nested trees и тяжёлые Eremex controls на конкретных пользовательских формах.
- Интерактивный host внутри настоящего Visual Studio. Сборка VsHost/VSIX не заменяет такую проверку.
- Live SQL Server/PostgreSQL не запускались; offline contracts и DLL fixture не выдаются за live database integration.

Phase 3 ограничена редактором структуры Grid. Дальнейшая разработка размещения по ячейкам автоматически не начинается.
