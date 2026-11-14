# Universal Control & Property Support — Phase 1

Дата: 14.11.2026. Основа: `cb3784eae92292a00aaf75377da84adcd49a25f7` (`main`, Beta 0.1). До начала работы дерево Git было чистым. Версии продукта, VSIX, Avalonia, Eremex, NuGet и TargetFramework не изменены. Коммиты, теги и публикация релиза в этом этапе не выполнялись.

## 1. Результат и границы

Добавлен общий каталог типов/свойств Avalonia с кэшем по Type, консервативная политика допуска и типизированный scalar codec. Существующие descriptor, Inspector, JSON property bag, native layout, Preview, export bridge и source-preserving patch writer расширены, а не заменены.

Первая полная цепочка на общей metadata реализована для **Slider, ProgressBar, ToggleSwitch**. Обнаружение остальных типов не означает их автоматическую регистрацию или разрешение редактирования каждого найденного свойства.

Полные таблицы, полученные из установленной Avalonia **11.1.1.0**, а не из заранее составленного списка:

* [Control Coverage Report — CSV](UniversalControlCoverage.csv): все 140 публичных типов, производных от Control, в используемых стандартных сборках. Включены базовые/абстрактные типы для аудита; они не включаются в Toolbox.
* [Property Coverage Report — CSV](UniversalPropertyCoverage.csv): 5957 пар «тип контрола — свойство» для 30 типов с существующим descriptor/import/root workflow. Наследуемые свойства и unqualified/attached aliases повторяются для каждого типа; это не 5957 уникальных AvaloniaProperty registrations.
* [Машиночитаемая сводка](UniversalCoverageSummary.json).

Это аудит подключённых `Avalonia.Controls`, базовой Avalonia и стандартного DataGrid. Он не объявляет поддержку controls из отсутствующих дополнительных пакетов и не сканирует произвольные сторонние DLL.

## 2. Существующая архитектура и дублирование

| Участок | Существующий механизм | Решение Phase 1 |
|---|---|---|
| Toolbox / DesignerRegistry | IControlDescriptor, TypeKey, defaults, BuildPreview, AppendXaml; регистрация built-in и plugin descriptors | Сохранён. Три reviewed descriptors добавлены через существующий DelegatingControlDescriptor |
| Property schema | Вручную заданные DesignPropertyDescriptor и поля DesignControlModel; отдельные специализированные строки MainWindowViewModel | Сохранены. Новые scalar descriptors и строки строятся из одной metadata |
| JSON / Undo | BuiltInProperties + CustomProperties.ValueJson, существующие snapshots и document sessions | Формат и Undo storage не менялись |
| Designer / Legacy | Native layout projection и PluginPreviewControlBuilder, старые built-in builders | Новые descriptors используют один StandardControlProperties.Create |
| AXAML Preview | Runtime loader сгенерированного export AXAML | Получает те же атрибуты, что экспортированный проект |
| Export | BuiltInXamlBridge + специализированные handlers/DataGrid styles | Добавлена небольшая ветка для трёх типов; атрибуты берутся из общего codec |
| AXAML Import | AxamlControlMetadata, LiteralProperties, AxamlRoundTripPropertyMap, SourceMap/capability policy | LiteralProperties трёх типов строятся из общей metadata; старая source policy остаётся обязательной |
| Plugins | Существующий PluginLoader/descriptor contract, lazy Eremex runtime | Не заменён. Новый каталог не загружает plugin assemblies самостоятельно |

До этапа имелись несколько параллельных описаний одних свойств: registrar, ручные Inspector rows/defaults, source LiteralProperties, source property map, preview builders и exporter. Reflection уже использовалась в plugin/DLL integration, редакторах и Avalonia runtime; она не давала общей проверенной таблицы разрешённых свойств.

Полностью объединять старую schema в этом этапе небезопасно: например, исторические Padding/BorderThickness/CornerRadius представлены scalar double, а native Avalonia использует Thickness/CornerRadius; Text модели может соответствовать Text или Content. Старые файлы не мигрируются. Эти адаптеры отмечены как частичная поддержка, а не как универсальная запись любого native значения.

Конкретное устранённое дублирование: common source properties и новые literal Padding/CornerRadius могли образовать два редактора/patch accessor для одного ключа. Теперь metadata literal перекрывает одноимённый common accessor; no-op patch сохраняет исходный текст побайтно.

## 3. Call path

```text
BuiltInControlRegistrar.Register
  -> RegisterMetadataControls
  -> StandardControlProperties.PropertiesFor
  -> AvaloniaMetadataCatalog.Get(Type) [ConditionalWeakTable + Lazy]
  -> DesignPropertyDescriptor

Select control -> existing RebuildPropertyGrid
  -> CreateDescriptorPropertyRow / source literal rows
  -> CreateMetadataPropertyRow -> existing CreateRow/editor templates
  -> TryEncode + AvaloniaProperty.IsValidValue
  -> existing CustomProperties/NotifyDesignerStateChanged
  -> DesignerDocumentSession / existing Undo / DesignerSurface.ApplyDocument

Native layout -> descriptor.BuildPreview -> StandardControlProperties.Create
Export -> BuiltInXamlBridge -> StandardControlProperties.Attributes
  -> generated AXAML -> AXAML Preview loader / generated runtime

AXAML Import -> AxamlControlMetadata.LiteralProperties
  -> AxamlRoundTripPropertyMap -> source capabilities
  -> typed CustomProperties -> AxamlPhase4Projection.Create
  -> same native factory
Edit -> AxamlPatchWriter -> minimal attribute edit, or unsafe/original source
```

ControlId остаётся идентификатором документа; Name не становится ключом кэша native instances. Кэш содержит описания типов, не controls, DataContext, ItemsSource, sessions или ViewModels. Full/incremental layout projection в этом этапе не менялась.

## 4. Metadata engine и безопасность

`AvaloniaMetadataCatalog` объединяет registered StyledProperty, DirectProperty и AttachedProperty с публичными CLR signatures. Avalonia properties имеют приоритет над одноимённым CLR wrapper, attached ключ содержит owner (`Grid.Row`, `Canvas.Left`, `DockPanel.Dock`).

Учитывается AddOwner: например, FontFamily/FontSize/Foreground у TemplatedControl имеют attached-origin native property, но доступны как обычные unqualified CLR members. Исходная проверка `IsAttached` отбрасывала эти aliases и теряла AvaloniaProperty identity; тест воспроизвёл неприменённые FontSize=23 и Foreground. Теперь Key различает normal CLR alias и qualified attached usage, а Kind/Attached сохраняют native origin. В CSV Key позволяет отличить такие строки.

Attached index один раз обнаруживает публичные static AttachedProperty declarations в доверенных поставляемых сборках и инициализирует их native owners. Затем Registry опрашивается по **host type/ancestors**, с фильтром этого индекса. Это исправляет зависимость от случайного порядка открытия controls. В normal metadata допускаются только публичный CLR member либо публичная AvaloniaProperty declaration: динамические `__AvaloniaReserved::Classes::*` и private SavedAdornerLayer не являются пользовательской schema. До фильтра warm/cold CSV различались; после исправления оба CSV побайтно совпадают между fresh process и полным native suite.

Контракт API сверялся с XML-документацией именно установленной 11.1.1; публичное описание — [AvaloniaPropertyRegistry](https://docs.avaloniaui.net/api/avalonia/avaloniapropertyregistry) и [RegisterAttached с THost](https://api-docs.avaloniaui.net/docs/M_Avalonia_AvaloniaProperty_RegisterAttached__3). OwnerType свойства и тип host не взаимозаменяемы.

Получаются Name, OwnerType, PropertyType, registration default/наличие default, ReadOnly, CLR-nullability, наследование объявления, Avalonia Inherits, Attached, возможность быть binding target и подсказка редактора. `InheritedFromBase` и `InheritsValue` — разные поля. Для DirectProperty default означает metadata UnsetValue, а не результат вызова getter.

Getters/constructors пользовательских компонентов не вызываются. CLR default без безопасной registration metadata помечен неизвестным. Reference-type Nullable означает представимость null на уровне CLR; это не обещание NRT/семантической допустимости null. Перед записью дополнительно проверяется AvaloniaProperty.IsValidValue.

Static initialization выполняется только для явно поставляемых стандартных типов, необходимая для Avalonia property registration. Для другого Type каталог читает CLR signatures и свойства trusted base classes, не запускает его static initializer и не включает его в Toolbox. Новые native instances создаются только для трёх явно разрешённых стандартных типов.

Кэш — ConditionalWeakTable<Type, Lazy<...>>. Значение может ссылаться на свой Type, но ephemeron semantics не удерживают collectible key. Статический support policy содержит только три поставляемых non-collectible Type. Тест с RunAndCollect assembly подтверждает освобождение типа после metadata lookup. Это не означает, что существующий PluginLoader уже умеет выгружать любые сторонние плагины; его прежняя политика retained contexts не менялась.

## 5. Control Coverage и выбор первой итерации

В реальном built-in registry теперь 17 descriptors: Button, CheckBox, TextBox, DataGrid, TextBlock, Border, WrapPanel, Grid, StackPanel, Image, DropDownButton, ListBox, SplitButton, Group и три новых типа. Group — существующий model/Toolbox контейнер, а не CLR Control с таким именем. Canvas/DockPanel доступны через root/container layout workflow; CSV не выдаёт их за отдельные одноимённые Toolbox entries.

Window и UserControl рассматриваются как root workflow. ContentControl, ItemsControl, Panel, Decorator, HeaderedContentControl и TemplatedControl выделены как базовые классы/контракт Content/Items/Children/Child, а не автоматически добавляемые обычные controls. Abstract classes, presenters, template infrastructure, DataGridRow/Cell/ColumnHeader и подобные служебные типы отфильтрованы из кандидатов. ContextMenu/ToolTip требуют attachment workflow, а не обычного размещения на поверхности.

Из 140 обнаруженных типов: 8 abstract, 57 infrastructure, 9 root/base/constructor-policy, 2 popup attachment workflow и 64 предварительных кандидата. В Toolbox 16 одноимённых native entries плюс Group; остальные кандидаты не активированы автоматически.

Невизуальный RowDefinition проверен отдельно: доступна настоящая Height/GridLength metadata, но IsVisual/ToolboxCandidate=False. Наличие parameterless constructor само по себе не разрешает Toolbox registration.

| Тип | Designer | Inspector | AXAML Import | Legacy / AXAML Preview | Export runtime |
|---|---|---|---|---|---|
| Slider | Native Slider | Общие + reviewed metadata literals | Literal editing + capability guard | Native в обоих | Native, Value/Padding/Grid placement/resize проверены |
| ProgressBar | Native ProgressBar | Общие + reviewed metadata literals | Существующее распознавание расширено общей metadata | Native в обоих, включая indeterminate lifecycle | Native, Value/Padding/Grid placement/resize проверены |
| ToggleSwitch | Native ToggleSwitch | Общие + bool? / Thickness / CornerRadius | Literal editing + capability guard | Native в обоих | Native, IsChecked/Padding/Grid placement/resize проверены |
| Прежние built-in descriptors | Прежние специализированные adapters | Сохранены | Частично, согласно source capability | Registry-derived native tree regression | Broad generated-project regression |
| Обнаруженный тип без adapter | Не включён автоматически | Не включён автоматически | Opaque или прежний reviewed source adapter | Не объявляется поддержанным по одному Reflection | Не объявляется поддержанным |

Приоритет недостающих Toolbox controls: RadioButton, Separator, NumericUpDown, ScrollViewer, Expander, затем ComboBox и TabControl. Это backlog, не автоматическая регистрация. ScrollViewer/Expander требуют single-content/header semantics; ComboBox/TabControl/TreeView — item/selection/template editors; Calendar/DatePicker/TimePicker — проверки culture и nullable values; Shapes — отдельная политика геометрии. Полный список отсутствующих entries содержится в CSV (`Toolbox=False`), включая причины непригодности служебных типов.

## 6. Property Coverage и Inspector

В CSV имеются все запрошенные поля и SupportState. DesignerEditable/ImportSupported/ExportSupported означают наличие reviewed scalar/legacy adapter, а не сохранение любого возможного значения свойства. Legacy flags — консервативная сверка descriptor keys/source attribute map; контекстные layout rows могут быть доступны только для подходящего parent. Нельзя трактовать процент этих флагов как процент полной поддержки Avalonia.

| Native kind | Пар «тип — property/alias» |
|---|---:|
| StyledProperty | 1360 |
| DirectProperty | 381 |
| AttachedProperty, включая AddOwner aliases | 3775 |
| CLR signature без AvaloniaProperty | 441 |
| **Всего** | **5957** |

По policy: 24 shared scalar literals, 599 partial legacy/source adapters, 744 read-only, 4590 не выставленных/opaque свойств. 252 строки имеют Designer adapter, 587 — source attribute adapter. Root Window/UserControl, динамические parent-context rows и специализированные schema editors требуют отдельной интерпретации и не превращаются в общие scalar adapters.

| Control | Найдено | Designer adapter | ReadOnly | Source adapter | Export adapter |
|---|---:|---:|---:|---:|---:|
| Border | 182 | 12 | 22 | 19 | 12 |
| Button | 201 | 17 | 23 | 23 | 17 |
| Canvas | 176 | 7 | 23 | 15 | 7 |
| CheckBox | 203 | 12 | 23 | 23 | 12 |
| ComboBox | 214 | 0 | 28 | 24 | 0 |
| ComboBoxItem | 193 | 0 | 22 | 23 | 0 |
| DataGrid | 226 | 15 | 25 | 0 | 15 |
| DockPanel | 177 | 7 | 23 | 15 | 7 |
| DropDownButton | 201 | 17 | 23 | 24 | 17 |
| Expander | 198 | 0 | 23 | 26 | 0 |
| Grid | 179 | 9 | 23 | 17 | 9 |
| Image | 176 | 7 | 21 | 0 | 7 |
| ItemsControl | 198 | 0 | 27 | 22 | 0 |
| ListBox | 210 | 23 | 28 | 26 | 24 |
| ListBoxItem | 193 | 0 | 22 | 23 | 0 |
| ProgressBar | 198 | 20 | 23 | 29 | 20 |
| ScrollViewer | 211 | 0 | 29 | 26 | 0 |
| Slider | 198 | 23 | 21 | 32 | 23 |
| SplitButton | 196 | 17 | 22 | 24 | 17 |
| StackPanel | 180 | 10 | 23 | 17 | 10 |
| TabControl | 212 | 0 | 29 | 23 | 0 |
| TabItem | 197 | 0 | 24 | 23 | 0 |
| TextBlock | 194 | 12 | 22 | 20 | 12 |
| TextBox | 222 | 17 | 26 | 24 | 17 |
| ToggleSwitch | 210 | 18 | 25 | 26 | 18 |
| TreeView | 202 | 0 | 27 | 23 | 0 |
| TreeViewItem | 204 | 0 | 29 | 24 | 0 |
| UserControl | 192 | 0 | 22 | 0 | 0 |
| Window | 235 | 0 | 43 | 0 | 0 |
| WrapPanel | 179 | 9 | 23 | 16 | 9 |

Например, DataGrid/Image имеют descriptor/export workflow, но отсутствуют в reviewed source type map: source adapter=0 не означает отсутствие JSON Preview. И наоборот, ComboBox/TreeView имеют partial import workflow без обычного Toolbox descriptor. Это разные оси поддержки.

24 пары «новый тип — property» используют общий scalar policy:

* Slider: Minimum, Maximum, Value, SmallChange, LargeChange, Orientation, IsSnapToTickEnabled, TickFrequency, TickPlacement, IsEnabled, Padding.
* ProgressBar: Minimum, Maximum, Value, IsIndeterminate, Orientation, ShowProgressText, IsEnabled, Padding.
* ToggleSwitch: IsChecked, IsThreeState, IsEnabled, Padding, CornerRadius.

Существующие общие Name/Text/Width/Height/Opacity/Visibility/colors/font/placement продолжают проходить через старые model adapters. Возможность записать произвольный binding не следует из BindableTarget=True: это свойство native Avalonia property, а не обещание готового binding authoring UI для каждого ключа.

| Тип значения | Metadata/editor path | Ограничение |
|---|---|---|
| string, bool, enum, int/double | Существующие Text/CheckBox/Enum/Number templates + validation | Bindings не принимаются как обычный литерал; finite numeric values |
| bool? | Enum с «Не задано (null)», False, True | JSON null сохраняется как null; не строка `"null"` |
| Color / Brush | Существующий Color editor, bounded codec | Scalar codec поддерживает Color/solid brush; gradient/resource требуют отдельного представления |
| Thickness / CornerRadius / GridLength / FontFamily | Типизированные editor kinds, parser и validation | Для новых kinds используется существующий текстовый template. Новые составные четыре-поля/picker UI здесь не реализованы |
| Collections | Metadata classification + прежние специализированные collection editors | Нет общего небезопасного преобразования collection в строку |
| Complex objects | ReadOnly hint / специализированный прежний workflow | Нет произвольного ToString/JSON serialization CLR object |

Существующие Grid definition editor, DataGrid columns/schema, items/flyout editors, font/color editors, Favorites, поиск, русские описания, stale-row/context guards и Undo transactions сохранены. Новые строки используют тот же CreateRow; их категории Layout/Behavior и подсказки получаются из общей policy. Недопустимое значение показывает ошибку и не меняет содержимое документа.

Дополнительный scalar safety тест воспроизвёл принятие `{Binding Font}` конструктором FontFamily и NaN/Infinity parsers Thickness/CornerRadius. Теперь весь scalar codec отвергает markup (кроме явно разрешённого nullable null) и нечисловые компоненты layout structs. Неизвестный CLR object никогда не сериализуется через fallback ToString.

К наиболее заметным gaps относятся полные типизированные Thickness/CornerRadius в старой scalar schema; невыставленные native свойства вроде IsEnabled на части прежних descriptors; validation/culture для оставшихся значений; templates/resources/styles/commands. Полный перечень найденных, но не включённых свойств — строки `Opaque/preserved or not exposed` CSV. Прямые read-only свойства Bounds/DesiredSize не превращаются в фиктивные редактируемые поля.

## 7. Сложные свойства

ReadOnly в CSV означает отсутствие записи самого свойства; содержимое read-only коллекции может изменяться существующим специализированным редактором. Это не обещание immutable collection и не запрет прежнего DataGrid Columns/items editor.

| Группа | Существующее поведение | Phase 1 / дальнейшая граница |
|---|---|---|
| Content, Header | Text aliases, source child metadata, single-content guards | Скалярный текст и child controls различаются; произвольный object не сериализуется ToString |
| Items, ItemsSource | Items/flyout collection editors; DataGrid/ListBox BindingSource adapters | Не универсальный collection serializer, SQL/DLL loaders не менялись |
| ItemTemplate, ControlTemplate, DataTemplate | Source preservation / opaque capabilities | Read-only без специализированного template editor |
| Styles, Resources, Static/DynamicResource | Сохранение source syntax; существующий preview/resource workflow | Не заменяются scalar strings и не регенерируются |
| Bindings, Commands | Прежний Logic/BindingSource и source capability guards | Известное свойство с Binding может быть PartiallyEditable/ReadOnly |
| Flyout, ContextMenu | Существующие menu/flyout editors и source nodes | Attachment/collection workflow; не обычное строковое свойство |

## 8. JSON, Preview, Export и AXAML safety

Typed literals хранятся в прежнем CustomProperties.ValueJson: JSON numbers/bools/null и строки только для известных scalar representations. Unknown custom JSON сохраняется без преобразования. ControlId, ParentId, Grid placement, порядок/иерархия, Logic и DataGrid configuration не меняли формат.

При экспорте новых типов исправлен найденный дополнительный manual built-in whitelist: без этого новый зарегистрированный descriptor классифицировался как plugin и чистый UI export выводил placeholder. Теперь whitelist использует ту же reviewed support policy. Native factory и XAML attributes используют один parser/type metadata. Attached placement остаётся в существующем layout exporter; Grid children не получают Canvas.Left/Top.

Source editing различает Editable literals, PartiallyEditable source controls, ReadOnly binding/template/resource values и Opaque unknown syntax. Импортированные bindings/comments/unknown namespaces/attributes остаются в оригинальном тексте. Структурная вставка новых типов в imported AXAML не включена автоматически; безопасное редактирование literal не даёт разрешения reparent/reorder/regenerate.

Raw pointer тест после Drop → Undo → Redo дополнительно выявил потерю selection Slider/ToggleSwitch: native range/toggle обрабатывал PointerPressed до designer wrapper. Новый descriptor не учитывал существующий DesignerPreviewMode.Designer, тогда как старые Button/TextBox builders отключают native hit-testing именно в Designer. Теперь это правило применяется к трём новым controls; source host передаёт тот же design-only контекст. Preview и export остаются интерактивными, маска не попадает в model/JSON/AXAML. DesignerSurface и state machine не переписывались. Для ProgressBar тест использует центр реальных bounds (Auto height 4 DIP), а не фиксированную точку, пригодную только для Button.

Новый null требует корректного xmlns:x в scope. Если его нет или он перекрыт другим URI, patch writer возвращает оригинальный source и объяснение NullNamespaceUnavailable. Он не добавляет namespace вслепую. Существующее `{x:Null}` распознаётся только для подходящего nullable scalar; общий запрет подмены bindings сохранён.

## 9. Подтверждённая lifecycle проблема ProgressBar

При добавлении indeterminate ProgressBar воспроизведено удержание закрытых Legacy и AXAML Preview. Строгие WeakReference tests сначала падали. SOS `dotnet-dump gcroot` подтвердил цепочку:

```text
DispatcherTimer -> MediaContext -> MediaContextClock
 -> observer -> AnimationInstance<Double> -> DoubleAnimator -> AnimatorKeyFrame
 -> ValueStore -> BindingExpression -> LogicalAncestorElementNode
 -> ControlTracker -> Border -> composition visuals -> ServerCompositionTarget
 -> TopLevel callback closure -> PreviewWindow
```

Ранее lifecycle останавливал только собственный LoadingProgress. Теперь при подтверждённом Closed останавливаются indeterminate ProgressBar только в runtime visual tree этого Preview. Document/snapshot, пользовательские источники данных и другие открытые Preview не изменяются. Production GC.Collect не добавлялся.

Промежуточный вариант в Closing прошёл обычные memory tests, но новый тест отменённого owner close воспроизвёл остановку анимации живого дочернего окна. Исправление перенесено в Closed. Проверены как отмена собственного закрытия, так и отмена owner close в обоих режимах. Тесты сравнивают clock observers после завершения предыдущего fixture; они не требуют немедленного возврата памяти процесса ОС.

## 10. Проверки и измерения

Windows, Release, .NET 8 host, существующая Avalonia 11.1.1; native suites выполнялись последовательно. Подробные результаты — игнорируемая папка `artifacts/universal-phase1`: `verified-results.json`, `verified-*.log`, `live-results.json`, `coverage-consistency.json`, `metadata-profile.json`, `consistency/geometry-comparison.json`.

| Проверка | Финальный результат |
|---|---|
| Release solution, включая VSIX/VsHost | 0 errors, 1 существующее NU1603 warning |
| Release smoke runner | 0 errors / 0 warnings |
| Новые metadata/native/Inspector/source/JSON/cache/memory checks | **45/45**, включая raw pointer click после Drop → Undo → Redo |
| Cold/warm coverage | Оба CSV совпадают SHA-256 после полного UI suite и в fresh process |
| Grid-first / Grid Editor / Grid Drop | **26/26; 45/45; 40/40** |
| Nested Layout / Pointer-Marquee | **59/59; 26/26** |
| Undo history cache / Beta save-open-state regression | **25/25; 16/16** |
| Preview consistency / Preview Phase 2 | **32/32; 28/28**, registry scene расширена до всех 17 descriptors |
| Stabilization / Layout 7A.2 wrapper reuse | **25/25; 13/13** |
| Eremex cold isolation / missing theme / missing runtime | **3/3** отдельных процессов; standard controls сохраняются; повторная загрузка не увеличивает retained contexts |
| PostgreSQL offline/contract | **20 passed, 0 failed**, live server отсутствует |
| Broad export/round-trip/VSIX/VsHost | **72/72**, process exit 0; cleanup warning указан ниже |
| DataGrid Preview CONTRACT + live LocalDB | **47/47**; реальные строки и видимые значения ячеек |
| SQL Settings → schema → BindingSource → оба Preview | **PASS**, stable source id и реальные 3 строки |
| Preview Memory с LocalDB | **36/36**, включая 20 циклов каждого режима, DataContext/ItemsSource, UIA и close during query |
| Новые content ProgressBar lifecycle checks | Legacy/AXAML: закрытые окна собраны, clock observers **0 → 0**; cancelled close/owner close оставляют live animation |
| Exported metadata runtime | Restore/build/run **PASS**: три native controls, Value=37, IsChecked=True, Padding=2,3,4,5, Grid placement, реальный resize |
| Exported SQL runtime | **PASS**: Customers=3, Orders=2, независимые ViewModels/окна и resize |
| RegistryNative, RegistryCanvas, NestedNative exported runtimes | Все три приложения реально запущены, exit 0 |
| Геометрия четырёх pipelines | 800×600, 1200×800, 1600×900; 141 runtime component/viewport rows; **423 comparisons**, ParentId совпал, максимальное фактическое отклонение **0 DIP** в этих fixtures; допустимый порог 1,1 DIP |

В broad проверены запускаемый VsHost, shared DesignerSurface, minimal patch, stale-version rejection, disconnect resilience, Tools command registration, VSIX dependency closure/полный host runtime и отсутствие visual Avalonia references у VSIX. Это автоматизация процесса/пакета/IPC, не ручная установка расширения в Visual Studio.

**Cleanup warning не скрыт:** после функциональных 72/72 runner не смог удалить собственную загруженную ToolboxStandardItemsExport.dll (UnauthorizedAccessException). Это прежний cleanup дефект runner, не PASS удаления файлов и не функциональный fail. Собственные временные binaries очищены после завершения процессов: 18 путей, 0 ошибок, task artifacts 481,66 → 1,91 MiB; ранее удалены dump и изолированная diagnostic tool installation. Логи/JSON/generated sources оставлены; для повторного runtime probe нужен обычный restore/build. Первая попытка isolated SQL fixture столкнулась с существующим MDF; он не изменялся и не удалялся. Затем созданы отдельные data/log files в workspace, выполнены все live проверки, собственные БД/instances удалены. PostgreSQL live не объявляется проверенным.

### Производительность и память metadata

Изолированный от restore/build fresh-process запуск `--universal-metadata-profile`: одна Grid-форма с тремя новыми controls; 3 warm-up, 30 переключений selection/Inspector с синхронным Window.UpdateLayout, Release без debugger. Это измерение UI thread, не time-to-first-compositor/физический frame latency. Медиана — среднее двух центральных наблюдений; P95 — nearest rank. Отдельно измерен cold catalog lookup после bootstrap Avalonia.

| Метрика | Результат |
|---|---:|
| Cold metadata lookup + attached-index initialization | **62,35 мс; 3,95 MiB** |
| 1000 warm catalog lookups | **0,018 мс; 40 bytes; 0 rebuilds** |
| Первый измеренный выбор Inspector, включая layout, до warm-up | **255,66 мс; 21,86 MiB** |
| Steady selection/Inspector + layout, median / P95 | **166,33 / 253,30 мс** |
| Selection stage, median / P95 | **62,56 / 100,98 мс** |
| Window.UpdateLayout stage, median / P95 | **99,96 / 185,01 мс** |
| Allocations per switch, median / P95 | **19,57 / 21,43 MiB** |
| Metadata rebuilds за переключения | **0** |
| GC0 / GC1 / GC2, включая 3 warm-up | **62 / 61 / 15** |

Медианы отдельных стадий не складываются в медиану total. Managed memory перед/после 33 наблюдений: 47,80 → 64,44 MiB без принудительного GC; это не критерий утечки. Отсутствие дополнительного удержания проверялось отдельно WeakReference/collectible-type и Preview lifecycle tests.

**Оставшийся P2:** полное обновление существующего Inspector/layout всё ещё дорого даже в этой небольшой сцене. Каталог metadata не перестраивается, но кэш не устраняет остальную UI work/allocations. Более глубокий root cause внутри selection/layout и сравнение с равнозначной старой сценой отдельно не профилировались; ускорение всего Inspector не заявляется. Дальнейший performance refactoring автоматически не начинался.

Первый baseline build --no-restore не прошёл: obj/assets ссылались на отсутствовавший временный NuGet cache (236 ошибок resolution). После restore с существующим пользовательским package cache, без изменения package versions/TFM, baseline Release solution собрался и broad прошёл 72/72. Исходное NU1603 VS SDK analyzers 17.7.20 → доступный 17.7.22 осталось предупреждением.

Промежуточные красные результаты сохранены: исходные vertical-slice тесты выявили plugin whitelist и duplicated source defaults; расширение выявило nullable markup guard, animation root, AddOwner и private runtime schema, scalar safety, native pointer interception. Ошибка первого exported runtime probe CS0165 находилась в тестовой инъекции после unbraced if; исправлены скобки harness, production startup не менялся. Source Inspector тест сначала читал старое view при `_isDesignerRenderScheduled=True`: теперь ждёт bounded render-completion condition и проверяет actual native value, без изменения production scheduling. Эти прогоны не выдаются за зелёный первый запуск.

## 11. Изменённые файлы

* `Controls/PropertyGridEditorTemplates.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlControlMetadata.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPhase4Projection.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.Metadata.cs`
* `DesignerSystem/BuiltIn/BuiltInControlRegistrar.cs`
* `DesignerSystem/Metadata/AvaloniaMetadataCatalog.cs`
* `DesignerSystem/Metadata/MetadataCoverageReport.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `Docs/UniversalControlCoverage.csv`
* `Docs/UniversalControlsAndPropertiesPhase1.md`
* `Docs/UniversalCoverageSummary.json`
* `Docs/UniversalPropertyCoverage.csv`
* `ViewModels/MainWindowViewModel.Metadata.cs`
* `ViewModels/MainWindowViewModel.cs`
* `ViewModels/PropertyGridRowViewModel.cs`
* `Views/MainWindow.axaml.cs`
* `Views/PreviewWindow.Lifecycle.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PreviewConsistency.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.UniversalMetadata.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/MetadataRuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/ProbeRuntime.ps1`
* `smoke-tests/UniversalMetadata/README.md`

## 12. Ограничения и ручная приёмка

* Это metadata foundation и три законченных reviewed controls, а не универсальный visual editor всех 140 типов/всех найденных CLR properties.
* Остальные ручные property adapters остаются. Их следует переводить небольшими проверяемыми группами, сохраняя старый JSON и специализированные editors.
* Сложные values/bindings/resources/templates не проходят через scalar codec. Произвольный импортированный AXAML по-прежнему имеет source-preserving ограничения; Preview не гарантирует воспроизведение всего чужого приложения.
* Новый каталог не гарантирует выгрузку старых plugin contexts существующего PluginLoader; подтверждена лишь отсутствие дополнительного удержания Type новым кэшем.
* Scalar policy не заменяет все Avalonia TypeConverters: сложные/неканонические source values могут оставаться read-only/opaque. Полная поддержка markup extensions не заявляется.
* Memory proof ограничен Type cache и выполненными Preview lifecycle suites. Общий lifecycle анимированных Designer/secondary runtime windows и произвольных пользовательских animation templates отдельно не профилировался. Исторические generic Window/external UIA риски не закрываются одним зелёным negative-control прогоном.
* Физическая мышь/system DragDrop, screen reader/external UIA client, физический DPI/мультимониторный перенос, interactive установленный VSIX в devenv.exe, Linux/macOS runtime в этом этапе не проверены. Native Windows routed events/visual tree и автоматизированный VsHost/IPC — отдельные, явно измеряемые проверки.
* PostgreSQL live не проверен: сервер не устанавливался. Offline contract tests не называются live integration.
* Ручная приёмка: три новых controls в Grid/Canvas/nested layout; nullable ToggleSwitch; Padding/CornerRadius с несколькими значениями; Favorites/search; source с Binding/Styles/unknown attrs; VSIX ApplyPatch/conflict; отмена закрытия Preview с анимированным ProgressBar; реальные DPI и клавиатурная навигация Slider/ToggleSwitch.

## 13. План последующего этапа

1. Согласовать typed representation для общих legacy Thickness/CornerRadius без потери старого JSON.
2. Расширять reviewed policy для существующих controls, начиная с простых отсутствующих scalar свойств; сравнивать native Designer/Legacy/AXAML/export runtime, не только AXAML strings.
3. Разработать удобные составные редакторы известных struct values; сохранить текущие специализированные editors.
4. Отдельно рассмотреть Items/Content/Header и шаблоны; discovery не даёт автоматического права source patch.
5. Затем выбирать следующую небольшую группу Toolbox controls по coverage report, с отдельной оценкой child/content semantics и plugin lifecycle.

Phase 1 завершается на этом. Реализация Phase 2 автоматически не начинается.
