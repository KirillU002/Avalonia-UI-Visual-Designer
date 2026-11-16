# Phase 5E — Property Coverage Expansion & ContextMenu Stabilization

Дата: 16.11.2026. Исходный commit: `296890194372efe9368ac2a18e6d97cfb98e72a4`, ветка `main`, исходное рабочее дерево чистое. Продукт Beta 0.1, Avalonia 11.1.1, Designer net8.0; Windows 10 x64 build 19045, runtime .NET 8.0.32, SDK 10.0.402. Изучены Phase 1, 3A, 3B, 3B.3, 4A, 4B, 5C и 5D. NuGet, TargetFramework, тема, JSON contract, VSIX/IPC и набор Toolbox не изменены. Коммит/tag/release не создавались.

## 1. ContextMenu: подтверждённый root cause

Исходный Layout7 natural run этой фазы: **15/15**. Исторический результат Phase 5D **14/15** не скрывается и не заменяется утверждением, что intermittent failure никогда не существовал. Проверка `LAYOUT7_DeferredContextMenu` исследована отдельно.

Обнаружены две managed lifecycle ошибки:

1. Right-button selection планирует coalesced `RenderDesigner` через DispatcherTimer. Меню успевает открыться на текущем wrapper. Следующий render очищает visual tree, отсоединяет owner и закрывает Popup. Наблюдённая цепочка: `RenderDesigner → DesignerCanvas.Children.Clear → ContextMenu.ControlDetachedFromVisualTree → Popup.OnDetachedFromLogicalTree → Close`. Проверка актуального wrapper непосредственно перед Open не защищала от уже запланированного последующего render.
2. После Load того же JSON `DocumentId` и `ControlId` сохраняются, но `DocumentSessionId` меняется. Старый wrapper мог инициировать запрос в новой сессии: проверок document/control ID недостаточно.

Добавлены два детерминированных regression tests. Первый явно управляет порядком timer tick и открытия, второй посылает ContextRequested со старого wrapper после reload. До исправления **0/2**, после **2/2**. Продолжительность timer меняется только в тесте для фиксации порядка; production delay/retry отсутствуют.

Исправление минимальное: перед открытием завершить уже запланированный render текущей сессии, заново разрешить owner; захватывать и проверять session ID для routed и deferred pointer requests. Старый двухаргументный helper сохранён. Accessibility, Popup API и Collection Editor не переписаны. Доказательств связи этого сбоя с историческим native UIA AV нет.

Evidence вне Git: `artifacts/universal-phase5e/context-red.log`, `context-red-capture/`, `universal5e-context-fixed.log`, `regression/layout7a-checks.log`. Финальный trace `context-order.json` показывает меню, оставшееся открытым после timer tick. Первые неуспешные assertions сохранены.

## 2. Расширение свойств через существующий Metadata Engine

Используется прежний `StandardControlProperties`, `AvaloniaMetadataCatalog`, набор typed editors и тот же PropertyGrid. Partial-файл содержит reviewed capabilities для существующих descriptors; он не регистрирует новые controls. Наличие свойства, writable native registration и допустимый codec проверяются по установленным assemblies. Не вызываются произвольные пользовательские getters.

| Группа | Новые или доведённые до общей цепочки свойства |
|---|---|
| Behavior/layout, только где реально применимы | `IsEnabled`, `IsHitTestVisible`, `Focusable`, `IsTabStop`, `TabIndex`, `UseLayoutRounding`, `ClipToBounds` |
| Typography | `FontStyle`, `FontStretch` |
| Associated value | `ToolTip.Tip`: literal string и null; сложный ToolTip/Binding/Resource не заменяется строкой |
| TextBox | `IsReadOnly`, `MaxLength`, `AcceptsReturn`, `AcceptsTab`, `TextWrapping`, `TextAlignment` |
| TextBlock | `TextAlignment`, `TextWrapping`, `TextTrimming`, `LineHeight`, `LetterSpacing`, `MaxLines` |
| Border | `BoxShadow` |

Существующие Width/Height/Min/Max/Margin/Padding/alignment, brushes, thickness, radius, ranges, selection и Geometry adapters сохранены. PropertyGrid использует прежние категории, поиск, Favorites, русские hints, Value Source, Reset и Undo/Redo. Дублирующий key при объединении source scalar metadata устранён через `Distinct`.

`TextTrimming` в Avalonia 11.1.1 — объект singleton, а не enum. Добавлен явный адаптер только для `None`, `CharacterEllipsis`, `WordEllipsis` с inline ComboBox. Прочие экземпляры не выдаются за поддерживаемый enum.

`BoxShadows` редактируется проверяемым literal: максимум 4096 символов, 16 shadows, конечные координаты/blur/spread в установленном диапазоне, blur неотрицательный. Проверены multiple/inset shadows и обратимое текстовое представление именно известного `BoxShadows`; произвольные CLR objects через ToString не сериализуются. Визуальный shadow designer не добавлялся.

`MaxLength`/`MaxLines` проверяются на отрицательные значения. `LineHeight=NaN` сохраняет нативный auto sentinel. Null tooltip остаётся JSON null и `{x:Null}` при AXAML export.

## 3. Property Coverage: определения и сравнение

Знаменатель Phase 5D сохранён: **72 применимых core-типа, 4935 пар «тип–native property», 332 глобальные native identities**. До схлопывания aliases — 5207 пар; **272 alias duplicates исключены**. Наследование учитывается в применимых парах, но не создаёт новую глобальную identity. Attached properties учитываются только в reviewed context. Read-only diagnostic state и несвязанные attached registrations исключены исходной policy; authorable getter collections остаются применимыми, даже когда сама коллекционная property read-only. `ActualThemeVariant` и другие runtime-state признаки отдельно аннотированы, без скрытого изменения baseline.

Property Coverage здесь — conservative eligibility `Editable ∩ Exportable` для проверенных literal/collection/content/asset adapters. Она не доказывает поддержку произвольных значений, expressions или шаблонов. JSON/Import/SourcePatch имеют отдельные capabilities. Assembly Avalonia.Controls.DataGrid, ColorPicker и ItemsRepeater не смешиваются с core.

| Метрика core | Before | After |
|---|---|---|
| Discovered | 4935 / 100,00% | 4935 / 100,00% |
| Displayable | 2486 / 50,37% | 3049 / 61,78% |
| Editable Coverage | 1637 / 33,17% | 2025 / 41,03% |
| JSON Coverage | 2043 / 41,40% | 2264 / 45,88% |
| AXAML Importable | 2020 / 40,93% | 2233 / 45,25% |
| Source Patchable | 1999 / 40,51% | 2212 / 44,82% |
| Export Coverage | 1637 / 33,17% | 2025 / 41,03% |
| Editable ∩ Exportable | 1637 / 33,17% | 2025 / 41,03% |
| Unsupported authoring pairs | 3298 / 66,83% | 2910 / 58,97% |
| Уникальные безопасные native identities | 156 | 167 |

Editable ∩ Exportable: **33,17% → 41,03%**, +388 пар, +7,86 процентного пункта; unique identities 156 → 167. Denominator не увеличивался за счёт aliases.

Полные результаты: [по типам](Phase5EPropertyCoverage.csv), [canonical свойства](Phase5EPropertyCoverageDetails.csv), [summary](Phase5EPropertyCoverageSummary.json), [рейтинг отсутствующих свойств](Phase5EPropertyPriority.csv). В details отдельно указаны inherited/attached/read-only signature, runtime-only, aliases, Preview eligibility и фактическая сертификация.

**Preview/E2E — проверенная нижняя граница**, а не оценка всего остального как неподдерживаемого. Историческая Phase 5D не имела сопоставимого pair manifest: Before обозначается `Not measured`, не 0%. Пересечение полного Inspector proof и manifest обоих generated runtime используется для E2E. Reflection и один успешный build в это пересечение не входят.

| Native type | Preview certified pairs | E2E certified pairs |
|---|---|---|
| Border | 9 | 9 |
| Button | 10 | 10 |
| Calendar | 10 | 10 |
| ListBox | 10 | 10 |
| NumericUpDown | 10 | 10 |
| Slider | 10 | 10 |
| TextBlock | 16 | 16 |
| TextBox | 16 | 16 |

Preview verified lower bound: **91/4935 = 1,84%**; E2E verified lower bound: **91/4935 = 1,84%**. Before для обеих метрик — Not measured. В каждом из двух generated приложений проверены все 91 native значения (182 runtime assertions), а не только наличие XML attributes. Статическая JSON/Export eligibility не переименована в E2E.

Control Coverage не увеличивалось: **57/72 = 79,17% частичной поддержки; strict Fully Supported — 0/72**. Это остаётся честной оценкой literal subsets при отсутствии универсального authoring Styles/Resources/Templates. Полная текущая таблица: [StandardLibraryCoverage.csv](StandardLibraryCoverage.csv); assemblies и роли разделены в [summary](StandardLibraryCoverageSummary.json). Каталог выгружен без массового runtime-override: подтверждённые 91 пары учитываются отдельным proof, а не переносом исторического verified-флага на весь тип. Новых Toolbox items нет.

Оставшиеся 15 применимых core-типов без JSON Designer adapter: AutoCompleteBox, ExperimentalAcrylicBorder, LayoutTransformControl, PathIcon, HeaderedItemsControl, RefreshContainer, RelativePanel, ReversibleStackPanel, SelectableTextBlock, Arc, Sector, SplitView, ThemeVariantScope, ToggleSplitButton, TransitioningContentControl. Их обнаружение не является поддержкой полного pipeline.

## 4. Приоритеты оставшихся свойств и сложные значения

| Canonical identity | Пробелы Before / 72 | Из них на поддерживаемых / 57 | Осталось на поддерживаемых |
|---|---|---|---|
| Avalonia.Controls.ToolTip.Tip | 72 | 57 | 6 |
| Avalonia.Input.InputElement.Focusable | 72 | 57 | 6 |
| Avalonia.Input.InputElement.IsHitTestVisible | 72 | 57 | 6 |
| Avalonia.Input.KeyboardNavigation.IsTabStop | 72 | 57 | 6 |
| Avalonia.Input.KeyboardNavigation.TabIndex | 72 | 57 | 6 |
| Avalonia.Layout.Layoutable.UseLayoutRounding | 72 | 57 | 6 |
| Avalonia.Animation.Animatable.Transitions | 72 | 57 | 57 |
| Avalonia.Controls.Control.ContextMenu | 72 | 57 | 57 |
| Avalonia.Input.InputElement.Cursor | 72 | 57 | 57 |
| Avalonia.Visual.RenderTransform | 72 | 57 | 57 |

Это baseline ranking с колонкой оставшихся пробелов. Assembly-группы не объединены; шесть child-only типов продолжают использовать специализированные collection adapters, поэтому common properties на них не объявлены поддержанными автоматически.

| Категория | Текущее безопасное поведение |
|---|---|
| bool/numeric/enum/nullable/Thickness/CornerRadius/GridLength/Brush/Font | Существующие typed editors и shared codecs; новые reviewed keys используют тот же путь |
| BoxShadow, TextTrimming, literal ToolTip | Небольшие специализированные adapters этой фазы |
| Items/Children/Columns, ContextMenu/Flyout | Существующие специализированные collection editors, только предусмотренные contracts |
| Points/Data/StrokeDashArray | Прежний ограниченный shape literal codec; object Geometry не превращается в строку |
| Content/Header | Literal или прежняя иерархическая модель; произвольный object сохраняет partial/read-only capability |
| Transitions/Styles/Resources/Templates | Требуют специализированного authoring; read-only/opaque, не обычный TextBox |
| RenderTransform/Transform/Clip/OpacityMask | Сложный объект без reviewed authoring adapter: read-only; объектный JSON/expressions не теряются при экспорте молча |
| Commands/Bindings/SelectedItem/ItemsSource | Сохраняются expressions/специализированные workflows; не выполняются ViewModels, не подменяются локальными literals |
| Cursor | Пока нет bounded authoring/export adapter; read-only |

Перечень приоритетов показывает количество применимых компонентов и canonical identity. Runtime-only state не выдвигается как обязательное редактируемое свойство. BoxShadow реализован только на Border, где адаптер подтверждён; Transitions и Transform не объявлены поддержанными заодно с ним.

## 5. JSON, Designer, Preview и AXAML round-trip

Тестовые native types: Button, TextBox, TextBlock, Border, Slider, NumericUpDown, ListBox, Calendar. Значения меняются через существующие metadata rows, сохраняются на диск, загружаются в новую VM, проверяются через native Avalonia GetValue в Grid и Canvas, AXAML/Legacy Preview и отдельно Designer.

Legacy TextBlock обёрнут в Border: раньше дополнительное свойство попадало в decorator. Найден actual native target и свойство применяется к нему. Container backdrop Border также получает BoxShadow. Designer продолжает маршрутизировать pointer selection через свои wrappers; runtime hit-test semantics проверяется в Preview/generated control. IsEnabled применяется к actual native descendant и его runtime wrapper.

Проверены Multi Form, nullable tooltip, Reset/Undo/Redo и 20+20 Preview close/GC cycles. JSON contract не расширен: используются существующие CustomProperties.

Для импортированного AXAML проверены FontStyle, LineHeight, BoxShadow, MaxLength и ToolTip.Tip: одно минимальное изменение, Reset удаляет local attribute, Undo восстанавливает точный source. Дополнительные восемь сценариев проверяют те же native значения всех 91 пары в обоих imported Preview modes и неизменность source. Unknown attributes, namespaces, quoting, comments, bindings и resources дополнительно покрыты Phase 4A/4B и PropertyGrid regressions. Binding/StaticResource/DynamicResource на новых object-typed свойствах остаются read-only. Полная регенерация импортированного AXAML не выполняется; структурное редактирование imported collections не добавлялось.

## 6. Export safety

Сохранён Phase 5D preflight guard против unsupported ItemsSource. Дополнительно перед IO проверяются reviewed scalar codecs, collection contracts и распознанные complex properties без безопасного адаптера — в активной и неактивных формах. ListBox со старым специализированным Items/ItemsSource workflow не блокируется общей проверкой. Plugins/DataGrid продолжают использовать свои validators.

Новые negative tests: invalid enum, invalid Binding, unsupported RenderTransform object, invalid Geometry и incomplete collection configuration. Для Folder и ZIP операция отклоняется с диагностикой, sentinel предыдущего output остаётся неизменным. Это ограниченная preflight policy, а не утверждение о валидации любого неизвестного пользовательского property.

Отдельно обнаружена подтверждённая ошибка сохранности **P0**: `ExportZipAsync` удалял существующий ZIP до проверки CancellationToken. Regression до исправления — **0/1**, прежний файл утрачивался. Теперь архив создаётся в уникальном staging-файле в том же каталоге, закрывается полностью, проверяется cancellation и только затем заменяет target. Finally удаляет только собственный staging. Проверка отменённого экспорта с прежним файлом проходит.

**Ограничение:** Folder Export не является транзакционной заменой всего каталога. Подтверждённые model validation failures останавливаются до записи; произвольный disk-full/IO/cancellation во время folder write не покрыт гарантией сохранения каталога целиком. Не заявляется универсальная атомарность всех export modes.

## 7. PropertyGrid: profiling и локальная оптимизация

Baseline получен с сохранённого полного runner bin commit `2968901` до production изменений. After — финальная Release-сборка. Одни и те же Button/NumericUpDown/MaskedTextBox, пять warm-up и 30 измерений на operation; UI suites, build и SQL не запускаются параллельно benchmark. P95 — nearest rank, allocations — текущий UI thread, GC counts — process-wide во время timed sections. Selection + UpdateLayout измеряются синхронно; физический first compositor frame не измерен.

Root cause подтверждён counters: старое positional reuse оставляло совместимые editors на других позициях и создавало **34 новых view на выбор NumericUpDown, 39 на MaskedTextBox**. Metadata lookup занимал доли миллисекунды, повторного reflection discovery после warm-up нет. Существенную часть стоимости составляли создание editor controls/bindings и их native layout.

Проверен локальный эксперимент: выбирать активные slots по Key+EditorKind и переставлять их в коллекции. Первый After оказался хуже baseline: Button 63,40/121,84 мс (median/P95), NumericUpDown 128,43/189,99 мс, MaskedTextBox 124,37/192,75 мс; allocations 5,15/18,71/19,60 MB. Новых views стало 8/46/50. Это красный performance result, он сохранён в `artifacts/universal-phase5e/performance/`, включая исходные samples и rejected summary/stages.

Эксперимент **отклонён**: перестановки active slots приводили к дополнительному visual projection/recreation, а совместимость editor kind не гарантировала reuse после перемещения. Восстановлены точные baseline bytes `DesignerPropertyInspector.Presentation.cs`; production-оптимизация Inspector в финальный diff не входит. Повторно проверены PropertyGrid 30/30, Context 2/2, Layout7 15/15 и новый полный suite. Ни cache ViewModel/control/плагинных Type, ни скрытый editor pool не добавлен. Следующий benchmark использует расширенные свойства с исходным механизмом UI.

| Фаза | Control | Median ms | P95 ms | MB allocated / выбор | Rows created | Views created | GC0/1/2 суммарно / 30 |
|---|---|---|---|---|---|---|---|
| Before | Button | 40,34 | 66,86 | 1,62 | 40 | 1 | 4/4/0 |
| Before | NumericUpDown | 98,55 | 134,61 | 10,71 | 51 | 34 | 20/20/1 |
| Before | MaskedTextBox | 102,88 | 152,47 | 11,98 | 59 | 39 | 24/24/2 |
| After | Button | 46,07 | 101,33 | 2,27 | 56 | 1 | 5/5/0 |
| After | NumericUpDown | 95,24 | 127,84 | 10,24 | 63 | 29 | 21/21/2 |
| After | MaskedTextBox | 91,45 | 124,27 | 12,18 | 71 | 37 | 22/22/1 |

Подробные все шесть операций (ToolboxCreate, Inspector, SaveLoad, ExportAxaml, оба Preview): [Phase5EPerformance.csv](Phase5EPerformance.csv). Отдельные стадии/calls/allocations: [Phase5EInspectorStages.csv](Phase5EInspectorStages.csv). Stages вложены друг в друга: их времена и allocations нельзя суммировать как независимые этапы.

Финальный результат неоднороден. NumericUpDown median −3,36%, P95 −5,03%, allocations −4,46%; MaskedTextBox median −11,11%, P95 −18,49%, allocations +1,64%. Для Button median +14,20%, P95 +51,57%, allocations +39,70%: строк стало 56 вместо 40. Это новая измеренная P2 стоимость расширенного Inspector, а не обещанное ускорение. Большой дополнительный cost отклонённого Move-эксперимента снят; оставшийся Button tail не объявляется устранённым. Времена измерены на общей рабочей машине, не на изолированном performance host; фоновые процессы не завершались. Изменение числа строк подтверждено counters, но весь P95 не приписывается только этому фактору.

После warm-up metadata builds = 0. Final samples: `artifacts/universal-phase5e/performance-accepted/after-controls.json`; baseline: `before/controls.json`; отвергнутый After: `performance/after-controls.json`. Широкая цель ускорения каждого типа не достигнута. Оптимизация самого PropertyGrid не оставлена без подтверждённого выигрыша; P95/allocations Button остаются условием готовности.

## 8. Build, regressions и первые красные прогоны

| Проверка | Результат |
|---|---|
| Build solution, accepted build | PASS, 0 errors / 1 historical warning |
| Phase 5E, final after native helper fixes | 58/58 |
| Deterministic ContextMenu, final | 2/2 |
| PropertyGrid, final after presentation rollback | 30/30 |
| Layout7, final after presentation rollback | 15/15 |
| Phase 5D | 49/49 |
| Phase 5C | 55/55 |
| Metadata | 45/45 |
| Collections | 30/30 |
| Phase 4A / Round-trip | 38/38 |
| Phase 4B / Preview fidelity | 52/52 |
| Pointer | 26/26 |
| Grid Drop / nested / optional Eremex | 40/40 |
| Broad, normal permissions | 72/72 |
| PostgreSQL offline | 20/20; live SKIP |
| SQL Server LocalDB | 47/47 |
| Settings/schema/BindingSource, live both Preview | PASS; real distinct rows |
| Preview Memory, live SQL | 36/36 |
| Generated SQL application | PASS; Customers=3, nested Orders=2, independent forms, resize |
| New generated Grid / Canvas | 2/2 applications; 91 pairs each; restore/build/run PASS |
| VSIX/VsHost/IPC | Broad scenarios + new property-element Apply/ACK 1/1 |
| Eremex isolation | PASS in Broad and Grid Drop |
| Performance | Before + rejected After + accepted After; 5 warm-up + 30 samples/type/operation |

Основная full regression выполнена на metadata/export/Preview изменениях до отклонения Inspector experiment. После восстановления исходного presentation отдельно повторены затронутые PropertyGrid/Context/Layout и расширенный Phase 5E suite. Exported AXAML и runtime не включают Designer presentation и не менялись при rollback; их первый успешный запуск не заменён фиктивным повторным PASS.

Сборка решения: 0 errors; один существующий NU1603 — fallback Microsoft.VSSDK.Analyzers 17.7.20 → 17.7.22. Runner: 0 errors/0 warnings. Пакеты не обновлялись. Build logs и capture results сохранены в `artifacts/universal-phase5e/`.

Первые красные результаты не скрыты:

* Context repro **0/2**, затем **2/2** после двух lifecycle fixes.
* Первый расширенный suite **1/41**: duplicate scalar key породил каскад 40 failures; устранено объединение без Distinct. Это не 40 независимых production defects.
* Далее **32/41**: ошибочная предпосылка fixture о native enum TextTrimming и именах legacy Canvas controls. Исправлены реальный codec и native lookup теста.
* **40/41**: свойство legacy TextBlock применялось к decorator; исправлен native target. Затем **41/41**.
* Cancelled ZIP **0/1**: подтверждённая потеря предыдущего файла, исправлена staging publication.
* Промежуточные новые проверки **50/50**, включая отдельные native Designer assertions и cancelled ZIP.
* Дополнительный imported Preview suite сначала **54/58**: четыре imported source types используют внутреннюю projection-модель `Group`; test helper ошибочно определял ожидаемый native type из неё. Следующая попытка **53/58** дополнительно приняла Canvas wrapper Border за native Border. Helper теперь использует явный source type и предпочитает native descendant wrapper-у, сохраняя возможность tracked native root. Production Preview не менялся из-за этих ошибок harness.
* Финальный полный новый suite **58/58**, PropertyGrid **30/30**, Context **2/2**, Layout7 **15/15** после отклонения performance experiment. Результаты предыдущих полных regressions по неизменённым metadata/export/Preview участкам сохранены в таблице.

Не выполнялись retries для маскировки native crash. Ошибки первоначальной компиляции нового test harness (shadowed Control symbol) исправлены до execution и не объявляются runtime regression.

## 9. Memory, UIA и ограничения среды

PropertyGrid regression проверяет освобождение старых rows, replaced session, stale row после Undo/form switch и input recycling. Новые native previews проверены 20 Legacy + 20 AXAML cycles; полная Preview Memory matrix выполнена отдельно с live SQL. Metadata cache по-прежнему ограничен trusted shipped types; глобальных strong plugin Type/Assembly references не добавлено.

Исторический `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Исправление ContextMenu объясняет конкретные managed события, но не происхождение native null provider. Accessibility не отключалась, глобальные WER settings не менялись. Все UI suites/generated runtimes запускались через существующий process-local NativeCapture с сохранением evidence. Старые dumps не удалялись и не включены в Git.

В 42 сохранённых process-local capture results этой фазы: **0 новых native captures, 0 CaptureFailed, 0 timeout**. Exit codes: 35 × 0, 7 × 1 (сохранённые red assertion runs); access violation exit не зафиксирован. Это результат данной матрицы, не доказательство исправления исходного UIA AV. Известное удержание обычных Window не пересматривалось как устранённое; эти GC roots не смешиваются с Preview Memory assertions. Evidence index: `artifacts/universal-phase5e/capture-summary.json`.

Named Pipes/LocalDB требуют normal permissions. Broad normal run отделён от прежних sandbox permission failures; IPC production code ради sandbox не менялся. В Broad встречается `ARTIFACT_CLEANUP_FAILED` для занятого generated DLL: assertion result не скрыт, временные ignored файлы оставлены на месте, принудительное удаление не выполнялось.

Не завершены ручная проверка установленного VSIX в interactive Visual Studio, реальный DPI matrix, PostgreSQL live и произвольный imported AXAML visual fidelity. Автоматический IPC/assembly isolation smoke не выдаётся за ручной acceptance установленного расширения. Известные ограничения Geometry, больших AXAML, Inspector tail latency и UIA сохраняются в соответствующих разделах.

## 10. Изменённые файлы и дальнейшие шаги

Production: `DesignerSystem/Metadata/StandardControlProperties.cs`, новый `StandardControlProperties.Extensions.cs`, `AvaloniaMetadataCatalog.cs`; `BuiltIn/BuiltInControlRegistrar.cs`; `AxamlRoundTrip/AxamlControlMetadata.cs`, `AxamlLayoutProjection.Json.cs`; `Views/MainWindow.axaml.cs`, `MainWindow.NativeLayout.cs`, `PreviewWindow.axaml.cs`; `ViewModels/MainWindowViewModel.cs`, `MainWindowViewModel.Collections.cs`; `Services/ExportPipelineService.cs`. `DesignerPropertyInspector.Presentation.cs` восстановлен после отклонённого эксперимента и не входит в финальный diff.

Tests/reporting: `Program.cs`, новые `Program.Universal5E.cs` и `Program.Universal5EContext.cs`; `Phase5ERuntimeProbe.cs.txt`, `Summarize5EPropertyCoverage.py`, `Summarize5EPerformance.py`, `smoke-tests/UniversalMetadata/README.md`; обновлённые Universal/StandardLibrary CSV и summaries; новые Phase5E coverage/priority/performance/stage CSV, summary и этот отчёт. `bin/obj/artifacts`, базы, secrets и dumps в diff не входят.

Рекомендации: расширять reviewed shared adapters по canonical priority report; отдельно проектировать Transitions/Transform и Templates authoring, не открывая complex object как строку. Продолжить targeted Inspector allocation work только по stage measurements. Провести installed-VSIX/DPI acceptance; при новом native AV использовать готовый full capture. Транзакционный folder export рассматривать отдельно, не смешивая с устранённой потерей ZIP.

**PROPERTY COVERAGE & STABILITY: READY WITH CONDITIONS.**

Подтверждённые ContextMenu lifecycle defects и потеря существующего ZIP при cancellation исправлены; основные regressions и bounded native/runtime матрица проходят. Условия: unresolved native UIA risk, P2 Button Inspector tail/allocations, ограниченная authoring support complex properties, отсутствие полной folder transaction и незавершённый installed-VSIX/DPI/live-PostgreSQL acceptance. Новых подтверждённых открытых P0/P1 по этой матрице нет. Полная поддержка всей стандартной библиотеки и общее ускорение Inspector не заявляются.

Phase 4C и следующая фаза не начинались.
