# Granular AXAML capabilities, VSIX 0.1.12

## Причины прежнего поведения

`AxamlCapabilityReport.Add` повышал единый document-level `Level` до максимального
уровня любой записи. `AxamlImportService.CreateControl` добавлял запись
`PartiallyEditable` для `custom:Custom.Unknown` у TextBox в SimpleAvaloniaApp.
Комментарий и само объявление `xmlns:custom` не были причиной ограничения.
SimpleAvaloniaApp уже разрешал patch (`CanSafelyPatch == true`), но
`VsHostBridge.OpenDocumentAsync` и `MainWindowViewModel.LoadAxamlImportedDocument`
показывали одинаковый текст «ограниченный режим» для всех состояний, кроме FullyEditable.

Для настоящего `Views/MainWindow.axaml` действовало другое условие в
`AxamlImportService.Import`: `canvas is null || canvas.IsSelfClosing` приводило к
`report.Add(..., ReadOnly, "Phase 1 requires a non-empty direct Canvas child.")`.
Корневой Grid не попадал в projection, после чего `CanSafelyPatch == false` превращался
в `DocumentOpenedPayload.CanEdit == false`. VSIX показывал предупреждение read-only.
Это смешивало отсутствие visual projection с невозможностью безопасного source patch.

## Модель

Используется существующий `AxamlCapabilityReport`, не второй document model.

| Уровень | Состояния | Правило |
| --- | --- | --- |
| Document | FullyEditable, PartiallyEditable, ReadOnly, UnsafeToSave | Opaque syntax повышает максимум до Partial, не до ReadOnly. |
| Element | Editable, PartiallyEditable, Opaque | Неизвестный sibling не влияет на возможности соседей. |
| Property | Editable, Preserved, Unsupported | Markup extension, неизвестный атрибут и неподдерживаемый literal сохраняются. |

`AxamlElementCapability` содержит source element и список `AxamlPropertyCapability`.
`AxamlSourceReference.Capability` ссылается на тот же объект, который находится в
report. Inspector, gesture guards и patch writer используют `CanEditProperty`.
Старый `EditableProperties` теперь вычисляется из этого же источника, а не хранится отдельно.

`DocumentReadOnly` означает запрет безопасного patch. Реальные причины: повреждённая
разметка, неоднозначный source map (например повторный атрибут), явная safety-ошибка.
`UnsafeToSave` остаётся совместимым состоянием fatal parse failure; такой результат
не заменяет открытую сессию. Внешнее изменение checksum блокирует конкретный patch
через прежний `ExternalChangeDetected`, не превращает unsupported syntax в fatal issue.

## Иерархия и операции

- Window/UserControl и единственный прямой Canvas остаются текущей visual projection.
- Button/TextBox/TextBlock/Border/CheckBox без вложенного содержимого импортируются.
- Unknown attributes сохраняются, известные свойства того же элемента редактируются.
- Unknown controls отсутствуют в editable projection. Удалить/переместить их через Canvas нельзя.
- Потомки opaque container получают `OpaqueAncestor:<parent>`; supported Button под
  unknown container не переносится искусственно на корневой Canvas.
- Styles/resources/templates за пределами Canvas сохраняются без блокировки его controls.
- Grid/StackPanel, неоднозначный выбор нескольких Canvas и неизвестный root остаются opaque.
  Это Partial с нулевой projection, не глобальный ReadOnly.
- Отсутствие безопасного insertion target хранится отдельно: `SourceMap.CanvasElement`
  может быть null; `CanInsertControls` запрещает добавление, но не identity-save.
- Self-closing Canvas сейчас не расширяется автоматически для вставки controls.

Root/container metadata сохраняются, но редакторов их свойств на этом этапе нет.
Счётчик editable elements включает распознанные root/container; счётчик editable
properties показывает фактически доступные свойства leaf controls.

## Property Inspector и gestures

`MainWindowViewModel.ApplyAxamlPropertyCapability` оставляет обычные rows для
поддерживаемых свойств. Остальные rows становятся существующим ReadOnly editor,
для binding показывается исходное значение вместо случайного preview default.
Неизвестные атрибуты пока не добавляются как отдельные rows; они остаются в source map.
При multi-selection свойство разрешено только если разрешено у всех выбранных controls.
JSON workflow проходит через тот же метод без ограничений.

Canvas drag и keyboard move проверяют доступность X/Y; resize проверяет Width/Height;
inline text editor проверяет соответствующее Text/Watermark. Связанный Width блокирует
resize, но не Text. Selection остаётся доступной. Это guards существующих handlers,
не замена drag engine или DesignerSurface.

Patch writer повторно проверяет свойства независимо от UI. Попытка изменить opaque
property или hierarchy напрямую через model даёт `AXAML_PATCH_BLOCKED`, а не молчаливое
применение соседних изменений. Добавление без Canvas или unsupported control отклоняется.

## Source preservation

Изменяются только mapped attribute value spans. Opaque elements не имеют редактируемых
source references, их диапазоны не удаляются. Comments/trivia сохраняются исходным текстом.
Identity patch возвращает исходник без edits, включая полностью opaque layout.
Single-quoted attributes экранируют apostrophe через `&apos;`; вставка в inline Canvas
производится внутри Canvas, а не в начале содержащей его строки.
XML attribute lookup регистрозависим; неизвестный `width` не подменяет Avalonia `Width`.

Checksum/version, VS buffer ownership, транспорт и ACK lifecycle не изменены.

## Статусы и диагностика

- Editable: обычный статус «AXAML открыт для редактирования», без warning.
- Partial с editable properties: «Частичное редактирование: поддерживаемые элементы
  доступны, остальные элементы и свойства AXAML будут сохранены без изменений».
- Partial с пустой projection: явно указано отсутствие редактируемых элементов;
  это не ошибка разбора и не обещание полного Grid designer.
- ReadOnly: конкретная safety-причина. Ошибочный parse по-прежнему не открывается поверх
  текущего документа.

Полный текст bridge-статуса доступен в tooltip без изменения размеров/цветов панели.
`AXAML_DOCUMENT_CAPABILITY` содержит editable/partial/opaque counts, property counts,
fatalIssues, documentReadOnly и reason. `AXAML_ELEMENT_CAPABILITY` содержит имя/тип,
mode, reason и списки editable/opaque properties. Исходные значения AXAML не логируются.

## Проверки

Новые сценарии `Program.AxamlCapabilities.cs`:

1. AxamlCapabilitySimpleEditable
2. AxamlCapabilityCommentPreserved
3. AxamlCapabilityUnknownAttribute
4. AxamlCapabilityOpaqueSiblings
5. AxamlCapabilityOpaqueAncestor
6. AxamlCapabilityStylesAndBindings
7. AxamlCapabilityMinimalDragResize
8. AxamlCapabilityInspectorAndOperations
9. AxamlCapabilityNoCanvasSafeIdentity
10. AxamlCapabilityUnsafeSourceBlocked
11. AxamlCapabilityRealDocuments
12. AxamlCapabilityQuoteAndInlineInsertion

`AxamlHardeningIpcEditReloadLifecycle` продолжает проверять настоящий pipe,
partial-документ с Styles/Bindings/custom controls, общий DesignerSurface, unsaved
buffer, patch/ACK/reload и конфликт. Проверка настоящего MainWindow обновлена с
ReadOnly на Partial. `AxamlHardeningRealDocumentExternalProcess` дополнительно
проверяет статус и CanEdit для Simple/Complex/MainWindow через отдельный VsHost.exe.

Это автоматизация реального процесса, но не ручной тест установленного VSIX в devenv.exe.
Логи прогонов: `artifacts/diagnostics/axaml-capabilities/`.

### Результаты 19.09.2026

`dotnet build FormDesigner.sln --no-restore -v:minimal -m:1`: успешно, 0 ошибок.
Осталось прежнее NU1603 по VSSDK SDK.Analyzers 17.7.20 -> 17.7.22; версии packages не менялись.

| Группа | Пройдено |
| --- | --- |
| AxamlCapability | 12/12 |
| AxamlHardening | 8/8 |
| AxamlRoundTrip | 9/9 |
| DesignerSurface | 14/14 |
| Vsix | 5/5 |
| VsHost | 10/10 |
| SaveLoadMultiFormProject | 1/1 |
| MultiFormToolboxDropPropertyEdit | 1/1 |
| NewProject | 7/7 |
| EremexTextEditorVerticalSlice | 1/1 |
| EremexDataGridControlVerticalSlice | 1/1 |

69 pass executions, 66 уникальных сценариев (фильтры пересекаются). После финального
уточнения informational diagnostics повторены capability/hardening/VSIX на свежих DLL.
Есть нефатальные cleanup-сообщения о заблокированных временных каталогах; bin/obj удалены.

| Документ | Capability | Editable properties | Opaque elements | Fatal | DocumentReadOnly |
| --- | --- | --- | --- | --- | --- |
| SimpleAvaloniaApp/MainWindow.axaml | PartiallyEditable | 39 | 0 | 0 | False |
| Views/MainWindow.axaml | PartiallyEditable | 0 | 2506 | 0 | False |

Root/container metadata учитываются отдельно от редактируемых leaf properties.
У Simple есть 5 preserved properties, включая metadata root и custom attribute.
У настоящего MainWindow все descendants корневого неподдерживаемого layout остаются opaque;
изменять их через текущий Canvas нельзя. Документ не получает ложного global safety failure.
Оба документа проверены также через внешний VsHost.exe, затем повторно в обратном порядке.

Итоговый `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`: версия 0.1.12,
130 entries. Hashes VsHost/FormDesigner совпадают с build output, visual assemblies в корне
VSIX отсутствуют. Для ручного теста закрыть VS, установить этот пакет и вызвать
`Tools / Средства -> Open in Avalonia UI Visual Designer` на активном AXAML.
Проверить редактирование Content/Width/X/Y в Simple и Apply в dirty VS buffer, а затем
открытие настоящего MainWindow без read-only dialog. Полный Grid UI при этом не ожидается.

## Изменённые файлы

- DesignerSystem/AxamlRoundTrip: AxamlRoundTripModels.cs, AxamlImportService.cs,
  AxamlPatchWriter.cs, AxamlSyntax.cs.
- ViewModels/MainWindowViewModel.cs: capability facade, Inspector rows, move/drop guards.
- Views/MainWindow.axaml.cs: guards существующих drag/resize/inline edit handlers.
- AvaloniaDesigner.VsHost: VsHostBridge.cs, VsHostWindow.cs (статус/tooltip).
- AvaloniaDesigner.VSIX: только version в manifest и InstalledProductRegistration.
- Smoke: Program.cs, Program.AxamlHardening.cs, новый Program.AxamlCapabilities.cs.
- Документация AXAML/VS PoC и этот отчёт.

VSCT, IPC protocol, DesignerSurface UI, темы, Eremex, JSON serialization, Export и
Avalonia baseline не менялись. Пользовательские AXAML fixtures не перезаписывались.
