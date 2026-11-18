# Advanced Property System — Phase 6A

Дата: 17.11.2026. Baseline HEAD `a290aff077817e1840c69bf023207c808a1efa10`, исходное рабочее дерево clean. Avalonia 11.1.1 / host net8.0 / runtime 8.0.32. NuGet, TargetFramework, Toolbox, тема, Document Model, VSIX protocol/architecture и accessibility не менялись.

**ADVANCED PROPERTY SYSTEM: READY WITH CONDITIONS.** Условная поддержка ограниченного grammar и известных targets; универсальный Binding/Template Designer не реализован.

## 1. Архитектура сложных значений

Прочитаны Phase 3A/3B/4A/4B/4C/5E/5F/5G; изучены общий MainWindowViewModel PropertyGrid, Metadata Engine, typed editors, Source Map, patches, Preview, export, legacy BindingSource/TextBindingPath, DataGrid, generated ViewModels и Logic interactions. Используются прежние механизмы; новый XML parser, второй PropertyGrid, binding evaluator или engine подключения к БД не создавались.

`PropertyValueKind` различает Literal, Binding, StaticResource, DynamicResource, TemplateBinding, ComplexObject, Collection, Opaque и Unset. `PropertyExpressionValue(Kind, Text, Version)` — authoring data, не runtime Binding/Converter/ViewModel. Для неподдержанного source выражения сохраняется исходный XML; capability блокирует запись. Установка expression target не делает scalar editor редактируемым.

Аудит 15 разных expression/property families: [Phase6AComplexPropertyAudit.csv](Phase6AComplexPropertyAudit.csv). MultiBinding, Templates, Styles, Resources dictionaries, Transitions и произвольные TransformGroup не объединены с простыми references.

## 2. Binding capabilities и фактический API

Поддержаны пустой Path / `.`, identifier/dotted property paths и числовой indexer, BindingMode из установленного enum, ElementName, RelativeSource Self/TemplatedParent, literal FallbackValue/TargetNullValue, StringFormat и ссылка Converter через StaticResource. Path ≤512 символов, expression ≤4096; resource key ≤128. Не поддержаны вызовы методов, arbitrary Source, неизвестные параметры, nested fallback/resources, ConverterParameter и AncestorType/type resolution. Дублирующиеся аргументы и конфликт ElementName/RelativeSource отвергаются. Scalar parameters с backslash/неподдержанным quoting не нормализуются молча.

Важное обнаружение: `BindingBase.UpdateSourceTrigger` присутствует в runtime API Avalonia 11.1.1, но установленный XAML `ReflectionBindingExtension` не предоставляет его. Доверенный runtime test получил XamlTransformException на этом параметре. Поэтому допускается только Default; selector отключён при отсутствии свойства у markup extension. Неподдержанный импорт остаётся opaque; значение не игнорируется и не удаляется автоматически. Default TwoWay behavior проверен реальными source updates.

Справочный синтаксис: [Avalonia v11 Binding](https://v11.docs.avaloniaui.net/docs/basics/data/data-binding/data-binding-syntax/). Текущая документация API не заменяет проверку реально установленной assembly и XAML compiler.

## 3. Binding Editor

В existing Inspector добавлена одна общая кнопка `fx`: выбрать строку свойства → fx → выбрать режим → Apply. Window/editor controls создаются только после явного открытия, не на каждой строке и не при обычном Inspector refresh. Context, категории, Favorites, поиск, Reset и текущая тема используются прежние. Поддержанный expression отображается в summary и через Value Source / ValueKind; tooltip объясняет safe Preview limitation.

Edit/Cancel не исполняют Binding. Cancel не изменяет модель. Apply проверяет native target/grammar, документ, выбранный control, revision, busy state и source capability; устаревший editor не может изменить другой control/form. Одно действие — одна существующая Undo transaction. Это native automated acceptance общего Inspector, не физическая mouse/DPI приёмка.

## 4. Resource Reference Editor

Отдельно выбираются StaticResource и DynamicResource. Сохраняются kind и key, не вычисленный цвет/значение. Из исходного scope читаются только доступные XML key declarations; XAML namespace aliases учитываются. Для JSON предлагаются constants существующего generated theme. Нет создания ResourceDictionary, converter или выполнения getters ради списка ключей. Manual key допустим в поддержанном формате; наличие и совместимость значения не угадываются.

Необъявленный generated StaticResource/Converter key блокирует distribution export до публикации файлов. Известный Brush resource с несовместимым target также отклоняется. DynamicResource может оставаться unresolved; editor/Preview показывают ограничение. Source reference на внешний application resource сохраняется; его runtime resolution здесь не утверждается.

## 5. CompiledBinding и TemplateBinding

CompiledBinding/MultiBinding/TemplateBinding и unknown arguments остаются read-only/opaque. Нет автоматической конверсии в compiled binding, исполнения ViewModel или угадывания x:DataType. Явный inherited x:CompileBindings=True, включая другой prefix той же XAML namespace, запрещает создание непроверенного обычного Binding. Project-level defaults, не переданные host, не объявляются известными; compiled authoring требует отдельного контекста типов.

Объектные property elements и Templates/Styles/Resources ancestors не доступны expression patch editor. Исходные expressions/namespace declarations сохраняются.

## 6. Command support

Поддержана ссылка Binding на существующий ICommand у reviewed native targets; отдельное authoring data и CommandParameter. Generated code не создаёт выдуманный SaveCommand/ViewModel property или Click handler. Logic interactions и generated event/CRUD actions остаются своим существующим workflow; общего каталога всех ICommand из Logic Editor нет. В первой версии не добавлен command editor для произвольных Collection Editor item payloads.

Actual Button.Command/CommandParameter и явный вызов caller-owned ICommand проверены runtime probe; import/editor команду не выполняют.

## 7. BindingSource integration

Список Paths строится из существующих BindingSources.Path и Fields.Path, включая простой `[0]` access. Это schema-only операция: без SQL queries, ReflectionBindingMetadataProvider.DiscoverSources, загрузки DLL или credentials. SQL Server/PostgreSQL/DLL используют прежние import/connection/isolation механизмы. Metadata selector не открывает соединение.

Для однозначного source path на основном Text/Content сохраняется прежний BindingSourceId. Явная замена legacy TextBindingPath очищает этот прежний field binding в той же Undo transaction; competing legacy selector отключён, пока active expression overlay существует. Undo восстанавливает legacy binding. ItemsSource/SelectedItem/DataContext не включены в generic fx; прежние DataGrid/ListBox data adapters остаются ответственными за их ownership. Live PostgreSQL не заявляется проверенным при отсутствии сервера.

## 8. PropertyGrid и typed editors

Literal-only rows используют прежние templates/recycling/native setters. Добавленная toolbar состоит из одной кнопки и label; per-row binding editor tree отсутствует. Binding/resource row защищена от scalar commit; смена режима производится только через fx. После явной конверсии Literal в expression слой сохраняется обычное backing literal, не runtime object. Literal mode внутри fx использует существующий typed codec; для снятия JSON overlay Reset возвращает прежний backing literal и обычный editor. Imported Reset удаляет локальный attribute через Unset.

Runtime-only/read-only properties, collection/data ownership и placement attached properties не стали принудительно Editable. Geometry/transform literals сохранили прежние adapters; object graphs не сериализуются через произвольный ToString.

## 9. JSON и migration

Расширена только extensible CustomProperties representation: key `@Avalonia.Expression:<canonical native property>`, JSON с Kind/Text/Version=1. Старые scalar contracts, IDs, ParentId и project schema version не переписаны. Старые valid проекты продолжают открываться; legacy TextBindingPath проходит отдельную replacement/Undo проверку.

Unknown version/shape, duplicate fields или повреждённый payload не превращается в scalar: данные сохраняются, authoring блокируется, distribution export отклоняется. New JSON Binding/Resource прошли фактическую запись файла/read/reopen и восстановление kind/text. Multi Form проверен отдельно. Сериализуемые suggestions не содержат connection strings/credentials.

## 10. AXAML Import / source-preserving Round-trip

Ограниченный безопасный expression импортируется в ту же authoring representation, scalar capability остаётся protected. SourceReference хранит только expression snapshot data, без native control/VM/COM refs. Это позволяет восстановить literal → Binding → Undo после ACK и вернуть исходный Binding после его редактирования.

Использованы прежние AxamlSyntaxDocument spans / AxamlPatchWriter, без полной регенерации source. Replacements меняют attribute ValueSpan, Reset — FullSpan, добавление — opening tag. Несвязанные comments, whitespace, prefixes, unknown attributes, Styles/Templates/Resources не переписываются. Property aliases/ambiguous mapping и объектные property elements блокируются. XML validation с DTD prohibited не выполняет user code. Composition со structural reorder проверена; обычные structural операции не расширялись.

До ACK Undo восстанавливает точный исходный текст проверенных fixtures. После ACK проверены semantic restoration literal/binding и Redo, версия/checksum, conflict detection; manual source overwrite не производится. Вставка ранее удалённого attribute после ACK следует существующему минимальному insertion contract, не обещает побайтовую реставрацию произвольного исходного formatting.

## 11. Preview

Сохранение expression, экспорт и runtime resolution разделены. Safe Designer/Legacy/imported projection не исполняет user DataContext, converter, command или DynamicResource и не показывает raw Binding как фиктивный Text. Новый literal применяется native setter. JSON AXAML Preview sanitizes только собственные expression attributes в preview copy; экспортный AXAML сохраняет их. Полная source regeneration отсутствует.

Existing Preview status/tooltip показывает ограничение (до шести distinct warnings), не десятки одинаковых сообщений. Unsupported converters/keys не исполняются ради красивого Preview. Реальное разрешение с корректным caller-owned DataContext подтверждается отдельно в доверенных generated applications.

## 12. Export / Generated Runtime и безопасность

Оба generated projects **Grid и Canvas** прошли JSON file save/read/reopen → export → restore → build → actual native runtime. В каждом **10 authored expression assignments на 5 unique native type-property pairs**: TextBox.Text, TextBlock.Text, Border.Background, Button.Command, Button.CommandParameter.

Проверены начальное значение/OneWay update, TwoWay/default source trigger, ElementName, RelativeSource Self, FallbackValue, TargetNullValue, StringFormat, StaticResource и DynamicResource при замене ресурса, CommandParameter и явный Execute. Caller-owned trusted ViewModel предоставлен probe; production generated code не содержит выдуманных Name/NullableName/SaveCommand. Это доказывает Binding semantics при корректном контексте, не resolution произвольного Path без DataContext.

Export generator и pipeline остаются прежними. Native expressions накладываются на результат existing generator существующими syntax spans; original imported AXAML меняется только через patch writer. Preflight отвергает malformed/unknown expression payload и неподдержанный target, unknown generated static/converter key, incompatible known Brush reference. Tests сохраняют sentinel прежнего output при отказе. Unsupported ItemsSource/invalid enum/geometry/complex objects и atomic ZIP guards повторно покрыты Phase 5E/Broad.

Не исполняется пользовательский AXAML при импорте, неизвестные converters не создаются, ViewModels не запускаются редактором, Commands не вызываются им. Literal/ref switch — явный Apply.

## 13. Property Coverage Before/After

Исторический core scope: **64/72 Partial**, strict Fully Supported=0; Toolbox controls не добавлялись. Literal Editable ∩ Exportable остаётся **2579/4935 = 52,26%**; этот показатель не переименован в поддержку всех expressions.

Generic fx authoring: **0 → 2241/4935 (45.41%) conditional visible pairs**, 152 unique native identities. Legacy TextBindingPath/DataGrid bindings уже существовали и не объявляются отсутствовавшими. Additional pairs beyond literal policy: **239**. Literal-or-conditional-fx potential: **2818/4935 (57.10%)**, при grammar/source/context guards, не безусловный runtime coverage.

Actual expression proof: по **10 assignments / 5 unique type-property pairs** в каждом Grid/Canvas, с caller-owned DataContext. Прежние 195 literal pairs/16 native types сохраняются отдельной матрицей Phase 5G. JSON/import/patch/export eligibility expression зависит от вида значения и scope; для arbitrary graph не назначались фиктивные flags. Таблицы: [Phase6APropertyCoverageSummary.json](Phase6APropertyCoverageSummary.json), [Phase6AExpressionTargets.csv](Phase6AExpressionTargets.csv).

Literal eligibility, conditional fx targets и фактический native proof не смешиваются. Aliases схлопнуты по canonical identity, уникальные свойства и type-property pairs разделены. Это расширение authoring modes, не утверждение универсальной поддержки всех значений одной AP.

## 14. Performance

[Полные 30-sample измерения](Phase6APerformance.csv), [Inspector stages](Phase6AInspectorStages.csv). MB — decimal allocated bytes текущего UI thread, GC — сумма за 30 samples; process peak — high-water mark, не footprint операции.

| Inspector | Before Median/P95, мс | After Median/P95, мс | Before/After MB | After GC0/1/2 |
|---|---:|---:|---:|---:|
| Button | 46.87/98.82 | 60.13/118.83 | 2.32/2.35 | 5/5/0 |
| TextBlock | 67.59/112.16 | 71.63/108.19 | 4.67/4.70 | 15/15/0 |
| TextBox | 50.57/98.19 | 59.62/101.06 | 1.82/1.85 | 4/4/1 |
| DataGrid | 75.14/105.23 | 66.66/117.67 | 5.46/5.49 | 11/11/0 |
| MaskedTextBox | 101.91/133.37 | 105.09/156.90 | 12.53/12.59 | 22/22/0 |

| Lazy editor / switch | Median/P95, мс | MB |
|---|---:|---:|
| OpenEditor | 28.31/44.60 | 2.09 |
| LiteralToBinding | 33.97/50.78 | 1.62 |
| BindingToResource | 32.70/43.36 | 1.57 |
| ResourceToLiteral | 31.46/44.34 | 1.55 |


After/Before allocations обычного Inspector изменились примерно на +0,4–2,2%; добавлены value-kind/capability descriptors и одна общая toolbar, eager dialog/editor tree на строках отсутствует. Button median/P95 выросли 46,87/98,82→60,13/118,83 мс, DataGrid median снизился 75,14→66,66 мс, MaskedTextBox P95 вырос 133,37→156,90 мс. Это смешанные измеренные результаты: весь latency рост не списывается на окружение, причинное ускорение или устранение P2 не заявляется. Для отделения framework layout/binding variance от добавленного capability bookkeeping нужна отдельная сопоставимая paired series; основной known tail остаётся у active editor binding/layout.

Измерения: existing common harness, 5 warm-up + минимум 30 samples, последовательные процессы, UI-thread allocations и GC. Editor opening / mode switching — отдельная серия. Не принята speculative оптимизация и не объявлен устранённым P95/allocations P2. Большие AXAML и imported Preview сохраняют прежние ограничения.

## 15. Memory / UI Automation

**Memory Gate 36/36 × 3** в независимых SQL-enabled процессах, по 44 census checkpoints; tracked Window/Context/Items/Grid survivors=0. Дополнительный actual expression editor Show/Close weak-reference test проходит (5 окон), прежние waits/GC/criteria не ослаблялись. Native capture records: 45, Captured/CaptureFailed/TimedOut=0. Exit 1 от managed assertion failures сохранены отдельно; отсутствие native crash не доказывает его исправление.

Native UIA crash `uiautomationcore.dll → ProviderCallouts::GetPatternProvider` остаётся **UNRESOLVED**. Его связь с expression authoring не установлена. Accessibility, COM/GCHandle/AutomationPeer lifetime не менялись; чужие UIA clients не завершались. Existing forensic capture, metadata/SHA-256/archives используются; dumps не включаются в Git и не удаляются.

## 16. Build / regression и первые отказы

| Gate | Результат |
|---|---|
| Phase 6A | 63/63 |
| Phase 5G | 85/85 |
| Metadata | 45/45 |
| PropertyGrid | 30/30 |
| Collections | 30/30 |
| Layout7 | 15/15 |
| Phase 4A | 38/38 |
| Phase 4B | 52/52 |
| Phase 4C / Structural | 85/85 |
| Phase 4D | 14/14 |
| Phase 4F | 15/15 |
| Phase 5F | 73/73 |
| Phase 5E | 58/58 |
| Pointer | 26/26 |
| Grid Drop | 40/40 |
| Broad normal environment | 72/72 |
| IPC normal environment | 3/3 |
| Preview Memory | 36/36 × 3 |
| SQL Server LocalDB | 47/47 |
| Live source/settings/schema | PASS |
| Generated expression Grid/Canvas | PASS, по 10 assignments |
| Generated SQL runtime | PASS, real Customers/Orders |
| Solution build / VSIX payload audit | PASS |
| Installed VSIX / physical DPI/DnD | NOT VERIFIED |
Solution build: 0 errors, прежний NU1603 analyzer fallback. Generated projects сохраняют net6.0 и прежний NETSDK1138 EOL warning. TFM/пакеты не менялись ради build. Live PostgreSQL SKIP — тестового сервера нет. Это не PASS его live connectivity.

VSIX version 0.1.18 не изменена; dependency closure/relative paths/bridge isolation PASS. Current VSIX SHA-256 `5a90e070926f13d6740534b6bedc4531ee9d1797c1eeff70fde03eab9d71c347`, production FormDesigner DLL `ab07131a7cd76d032006d87353e538ae58380ef23a71c2cbd45b11e2ad0c96ba`.

Первые failures сохранены: prototype compile errors (type inference/local shadowing), harness missing imports/неверные прежние API names; первый functional run 56/57 (xmlns declaration внутри runtime fixture), затем 62/63 (подтверждённый markup UpdateSourceTrigger mismatch). Namespace fixture исправлен, markup capability ограничена по установленному API; успешный повтор без изменения кода не выдаётся за исправление. Нативного AV эти managed/compiler ошибки не означают. Broad cleanup сохранил прежний warning UnauthorizedAccessException для locked ToolboxStandardItemsExport.dll после 72/72; assertion failure или native crash этим не подменяются, принудительного удаления не было.

Дополнительный первый Metadata run **44/45** обнаружил scalar mutation под active expression: изменение backing value молча игнорировалось вместо отказа. Guard теперь отклоняет изменение; expression backing snapshot после ACK/reimport rebased по acknowledged model, чтобы не запрещать допустимый Undo. Новый Metadata run **45/45**, повторные 6A/4C/4D/PropertyGrid проходят. Исходный красный log сохранён.

PropertyGrid regression SourceBindingResourceAndTemplateProtection обновлена под явную Phase 6A capability: scalar writes остаются blocked, fx/reset разрешены отдельно; проверка сохранности expression при scalar commit сохранена. Ни один testcase не удалён. Installed VSIX и physical DPI/DnD остаются NOT VERIFIED; automated tests их не подменяют.

## 17. Изменённые файлы

* `DesignerSystem/AxamlRoundTrip/AxamlImportService.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlLayoutProjection.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.Expressions.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.Structure.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlPatchWriter.cs`
* `DesignerSystem/AxamlRoundTrip/AxamlRoundTripModels.cs`
* `DesignerSystem/Metadata/PropertyExpressionProjection.cs`
* `DesignerSystem/Metadata/PropertyExpressionValue.cs`
* `DesignerSystem/Metadata/StandardControlProperties.Extensions.cs`
* `DesignerSystem/Metadata/StandardControlProperties.cs`
* `Docs/Phase6AComplexPropertyAudit.csv`
* `Docs/Phase6AEvidenceSummary.json`
* `Docs/Phase6AExpressionTargets.csv`
* `Docs/Phase6AInspectorStages.csv`
* `Docs/Phase6APerformance.csv`
* `Docs/Phase6APropertyCoverageSummary.json`
* `Docs/UniversalControlsAndPropertiesPhase6A.md`
* `ViewModels/MainWindowViewModel.AxamlStructure.cs`
* `ViewModels/MainWindowViewModel.Collections.cs`
* `ViewModels/MainWindowViewModel.Expressions.cs`
* `ViewModels/MainWindowViewModel.PropertyMetadata.cs`
* `ViewModels/MainWindowViewModel.cs`
* `ViewModels/PropertyGridRowViewModel.cs`
* `Views/DesignerPropertyInspector.Expressions.cs`
* `Views/DesignerPropertyInspector.Presentation.cs`
* `Views/DesignerPropertyInspector.axaml`
* `Views/DesignerPropertyInspector.axaml.cs`
* `Views/PreviewWindow.Mode.cs`
* `Views/PreviewWindow.axaml.cs`
* `Views/PropertyExpressionEditorWindow.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.PropertyGrid3A.Checks.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal6A.Generated.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.Universal6A.cs`
* `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
* `smoke-tests/UniversalMetadata/Phase6ARuntimeProbe.cs.txt`
* `smoke-tests/UniversalMetadata/Summarize6ACoverage.py`
* `smoke-tests/UniversalMetadata/Summarize6APerformance.py`

Raw artifacts / first failure logs / capture records: ignored `artifacts/universal-phase6a`. Ни Git tag, ни Release, commit/push не создавались. Пользовательские данные и предыдущие dumps не удалялись.

## 18. Ограничения

* Только reviewed native targets и ограниченный Binding grammar; arbitrary Source/AncestorType/MultiBinding/CompiledBinding/TemplateBinding/object graph authoring отсутствует.
* Markup UpdateSourceTrigger только Default для установленной Avalonia 11.1.1; runtime class API не равен markup extension API.
* Нет универсального converter/ICommand/type catalog, Template Designer или editor ResourceDictionary. Converter reference сохраняется без исполнения, distribution export требует reviewed declared resources.
* Safe Preview не равен runtime resolution. Для bindings нужен настоящий DataContext; его свойства не генерируются из текста Path.
* PropertyGrid P95/allocations, large AXAML, installed VSIX, physical DPI/DnD и historical native UIA AV остаются открытыми рисками.

## 19. Рекомендации для Phase 6B

Следующий конкретный шаг — type/scope-aware validation существующего ViewModel/Resource metadata, явный ICommand catalog там, где он уже предоставляется проектом, расширение grammar только с native/runtime доказательствами. Отдельно принять current VSIX payload и физический DPI/DnD по прежнему checklist. Не расширять template/object graph authoring формально и не менять lifetime COM ради зелёных тестов.

**ADVANCED PROPERTY SYSTEM: READY WITH CONDITIONS.** Phase 6B автоматически не начинается.
