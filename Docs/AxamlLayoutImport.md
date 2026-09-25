# AXAML layout import, VSIX 0.1.13

## Причина пустой поверхности

Анализ выполнен на настоящем `Views/MainWindow.axaml`, не на копии.
Исходник не изменялся: 462480 UTF-16 code units, SHA-256 UTF-8 text:
`245A2291637F3FF7798651BC74386D050BB34702C66057EF952EB2C13CC32476`.

В `AxamlImportService.Import` версия 0.1.12 выбирала единственный прямой
`Canvas` под `Window`/`UserControl`. Проекция строилась только в цикле
`foreach (var element in canvas?.Children ...)`. Остальные root children
передавались в `AddOpaqueSubtree`, а controls с дочерними элементами также
считались opaque. У настоящего файла root `Window`, top-level property element
`Window.Styles`, visual root **`Window/Grid`**. Canvas на этом уровне нет.
Первый blocker был `Window/Grid`, reason `UnsupportedContainerOrSubtree`.
Всё поддерево получало `OpaqueAncestor`, создавалось **0 controls**.

`Window.Styles` не принимался за visual root: проверка точки уже существовала.
Но раздельного структурного отчёта не было. `MainWindow.RenderDesigner` при
`Controls.Count == 0` показывал обычную карточку новой пустой формы, скрывая
причину отсутствия проекции. Это была отдельная UX-проблема, не сбой IPC.

## Структурный отчёт настоящего файла

| Метрика | Значение |
| --- | ---: |
| Все XML elements | 2507 |
| Визуальные кандидаты | 1473 |
| Старый importer: designer controls | 0 |
| Новый importer: designer controls | 225 |
| Из них partial | 154 |
| Из них без partial restriction | 71 |
| Opaque/skipped visual candidates | 1248 |
| Границы пропущенных визуальных поддеревьев | 25 |
| Binding attributes / Binding nodes | 1255 / 4 |
| Style / Setter | 74 / 312 |
| Custom namespace elements, включая templates | 7 |

Визуальные кандидаты считаются структурно, а не как экземпляры runtime controls:
без Window, property-element wrappers и содержимого Styles/Resources/templates.
Дети unsupported controls учитываются, но не проецируются через неизвестного
родителя. Поэтому число не равно количеству видимых controls работающего приложения.

| Тип | Все XML | Visual candidates | Импортировано |
| --- | ---: | ---: | ---: |
| Grid | 188 | 147 | 33 |
| StackPanel | 352 | 274 | 25 |
| DockPanel | 9 | 0 | 0 |
| Border | 201 | 146 | 47 |
| Canvas | 7 | 7 | 1 |
| Button | 259 | 208 | 51 |
| TextBox | 130 | 100 | 2 |
| TextBlock | 577 | 399 | 65 |
| CheckBox | 62 | 50 | 1 |
| TabControl | 2 | 2 | 0 |
| TabItem | 12 | 12 | 0 |
| ComboBox | 66 | 45 | 0 |
| ItemsControl | 32 | 25 | 0 |
| WrapPanel | 25 | 20 | 0 |
| ScrollViewer | 19 | 19 | 0 |
| DataGrid | 0 | 0 | 0 |

DockPanel этого конкретного файла находится внутри невизуальных для этой
проекции property/template веток. Поддержка DockPanel проверяется отдельным
fixture, а не заявляется как импорт этих девяти экземпляров.

Namespaces: default Avalonia; x; d; mc; vm; models; controls; views; commands;
contracts. Полные URI, все типы, пути, namespace и причины находятся в отчёте
`artifacts/diagnostics/axaml-layout/MainWindow-import-report.txt`.

Первый оставшийся blocker:
`Window/Grid/Border[1]/Grid/Border[2]/StackPanel/ComboBox`,
reason `UnsupportedControl`. Его соседние поддерживаемые элементы импортируются.

## Реализация

`AxamlImportService` рекурсивно импортирует Grid, StackPanel, DockPanel, Canvas,
Border и существующие leaf types. Используется существующий `ParentId`.
Grid и StackPanel используют существующие типы и поля JSON-модели. DockPanel и
вложенный Canvas используют универсальный Group как projection container;
**исходный тип остаётся в source map**, Group никогда не сериализуется вместо
DockPanel/Canvas. Новые Group нельзя вставлять в AXAML через этот alias.

`AxamlLayoutProjection` строит временное дерево стандартных Avalonia panels,
вызывает `Measure/Arrange` и возвращает local bounds. Logical styling root
позволяет измерять native Auto controls с обычными темами host, без второго окна
и без исполнения пользовательского AXAML/Styles/Bindings. Эти bounds использует
существующий `AddControlsToCanvas` и тот же shared DesignerSurface. Они не
записываются в модель или исходник как Canvas.Left/Top.

- Grid: RowDefinitions/ColumnDefinitions в attributes и property elements;
  Auto/star/fixed; Grid.Row/Column/RowSpan/ColumnSpan; Margin и alignments.
  Attributes definitions редактируются минимальным patch. Definition elements
  интерпретируются для layout, но пока редактируются только в исходнике.
- StackPanel: Orientation, Spacing, Margin, alignments; исходный порядок детей.
- DockPanel: DockPanel.Dock, LastChildFill, Margin и дети участвуют в native
  layout. Dock/LastChildFill пока сохраняются и не имеют отдельного Inspector
  editor. Это не полный DockPanel designer.
- Canvas: исходные координаты, включая вложенный Canvas. Рабочее добавление
  leaf controls в прямой Canvas сохранено; вставка в новые layout containers,
  reparent и изменение порядка source siblings пока блокируются patch writer.
- Property elements не становятся controls. Styles/Resources остаются opaque.
  Неизвестные Content/Items wrappers пока также являются границей проекции.
- Binding ограничивает только соответствующее свойство. Custom/unsupported
  container оставляет opaque всё своё поддерево, но не соседей.

Дополнительно тесты выявили старые геометрические ограничения standalone:
`ClampControlToSurface` и минимумы 40x24 в `DesignControlModel`. Они меняли
вложенные AXAML размеры при загрузке. Для AXAML-backed модели добавлен
не сериализуемый `UsesSourceLayout`; JSON workflow сохраняет прежнее поведение.

## Безопасность patch и ACK

Patch writer сравнивает исходного родителя, layout kind и порядок siblings
в упорядоченных StackPanel/DockPanel. Поле StackOrder не используется для
проверки Canvas/Grid: там оно не определяет layout, а исходный XML-порядок
writer не переставляет. Новый regression поймал ложную блокировку второго
Apply после добавления Button в Canvas; это исправлено без ослабления проверки
перестановки StackPanel.
Неприменимые Canvas properties у Grid/Stack/Dock children не редактируются.
Удаление контейнера с opaque descendants блокируется; перекрывающиеся удаления
известных дочерних элементов объединяются через удаление родителя. Нельзя
удалить родителя, сохранив его ребёнка в модели.

No-edit round-trip остаётся text-for-text identical, включая комментарии,
unknown attributes, Styles и bindings. Изменение Content/Width/Grid.Row и т.п.
патчит только соответствующий attribute span. Полной регенерации нет.

После ACK source identities без x:Name восстанавливаются по смещённым spans
подтверждённого patch, а не по неуникальным именам Grid/StackPanel или устаревшим
индексам siblings. Учитывается подтверждённый Designer snapshot, поэтому edits,
сделанные во время ожидания ACK, не становятся ошибочно сохранёнными.
Checksum/version protection, Named Pipes и владение VS buffer не менялись.

Вставка новых Canvas controls теперь сразу записывает поддерживаемые значения
их шаблона (alignment, цвета, padding и т.п.), а не только текст/геометрию.
Ранее эти значения оставались в модели и появлялись как лишний patch после
первого ACK. Regression проверяет отсутствие этого phantom patch и следующий
одиночный Canvas.Left edit. Это генерация только нового узла, не существующего файла.

## Диагностика и UX

Новый `AxamlImportStructureReport`: root, visual root, namespaces, XML/visual
counts, imported/partial/opaque counts, skipped subtree boundaries, bindings,
Styles/Setters, first blocker, полный список element path/type/namespace/reason.

События: AXAML_ROOT_DETECTED, AXAML_VISUAL_ROOT_DETECTED,
AXAML_ELEMENT_DISCOVERED, AXAML_ELEMENT_IMPORTED, AXAML_ELEMENT_OPAQUE,
AXAML_SUBTREE_SKIPPED и AXAML_IMPORT_SUMMARY; старые capability diagnostics
остались. Полный report доступен даже при ограничении длины Output history.

VsHost banner содержит числа и кнопку **Подробнее**, открывающую read-only
окно **AXAML Import Report**. Если visual candidates есть, но projection пуста,
поверхность объясняет blocker и предлагает тот же report вместо карточки
новой пустой формы. Пустой поддерживаемый Canvas не считается ошибкой импорта.
Вложенные AXAML name badges показываются при selection, чтобы не перекрывать
текст десятками меток. Standalone и прямой Canvas сохраняют прежний вид.

## Fixtures и проверки

Добавлены десять файлов `Samples/RoundTrip/Layout`: GridButton, GridDefinitions,
GridStackPanel, DockPanel, Styles, Bindings, UnknownSibling, UnknownParent,
DeepNested, Preservation.

`Program.AxamlLayout.cs` проверяет native bounds, Auto height, минимальные
patches, property-element definitions, styles/bindings preservation, opaque
ancestor, объяснение пустой проекции, nested Canvas, deletion protection,
реальный MainWindow и rebase anonymous siblings. Отдельный green baseline
использует настоящий SimpleAvaloniaApp: toolbox add, move/resize/text, Apply,
ACK и ещё один patch. Старые проверки, ожидавшие opaque Grid/Border/StackPanel,
обновлены под расширенный subset; custom parent остаётся opaque.

Результаты финального прогона записываются в
`artifacts/diagnostics/axaml-layout/final-*.log`.
`MainWindow-projection.png` является снимком partial projection, не обещанием
визуальной идентичности работающему приложению.

### Финальный прогон 25.09.2026

`dotnet build FormDesigner.sln --no-restore -v:minimal -m:1`: успешно, 0 ошибок.
Сборка smoke-tests: 0 ошибок, 0 предупреждений.

| Набор | Результат |
| --- | ---: |
| AXAML round-trip / hardening / capability / layout | 53/53 |
| DesignerSurface | 14/14 |
| VsHost / IPC | 10/10 |
| VSIX bridge / упаковка | 5/5 |
| JSON SaveLoadMultiFormProject | 1/1 |
| NewProject | 7/7 |
| Eremex TextEditor vertical slice | 1/1 |
| Eremex DataGridControl vertical slice | 1/1 |

Всего 92 успешных запуска, 88 уникальных сценариев (наборы пересекаются),
0 падений. В AXAML-набор входят все 11 новых layout scenarios, включая
SimpleAvaloniaApp add/edit/ACK/second patch и настоящий MainWindow. External
process test открывает SimpleAvaloniaApp, Complex fixture и настоящий
MainWindow через Named Pipes в отдельном VsHost, затем в обратном порядке.
Это не автоматизация установленного расширения внутри Visual Studio.

Осталось существующее предупреждение NU1603: Microsoft.VisualStudio.SDK
17.9.37000 запрашивает SDK.Analyzers >=17.7.20; недоступный 17.7.20 разрешён
как 17.7.22. Зависимости и подавление предупреждений не изменялись.

Проверен итоговый ZIP-контейнер VSIX 0.1.13: 130 entries, все visual assemblies
под `VsHost/`, в корне их 0. Упакованные FormDesigner.dll и VsHost.dll
совпадают по SHA-256 с текущей сборкой. EXE, deps.json и runtimeconfig.json
присутствуют. SHA-256 установочного файла:
`05B19BE6A62C8CF5FBA6205D05B74C7BB6A4650205CE2500D0633E9E51C98BD8`.

Временные bin/obj smoke workspaces очищены существующим cleanup lifecycle.
В четырёх наборах cleanup временно получил IOException на занятые каталоги;
retention затем убрал два из них. Два оставшихся каталога пустые (0 байт файлов),
это не падение тестов и не оставленные runtime-копии. Report/PNG и логи сохранены.

## Изменённые файлы

- DesignerSystem/AxamlRoundTrip: AxamlImportService.cs, AxamlPatchWriter.cs,
  AxamlRoundTripModels.cs; новые AxamlImportStructureReport.cs и AxamlLayoutProjection.cs.
- Models/DesignControlModel.cs: source-backed dimensions, Clone.
- ViewModels/MainWindowViewModel.cs: layout bounds, AXAML clamp isolation, ACK rebase.
- Views/MainWindow.axaml.cs: bounds adapter, report/empty state, source container preview.
- AvaloniaDesigner.VsHost/VsHostWindow.cs: кнопка отчёта.
- AvaloniaDesigner.VSIX: только номер 0.1.13 в manifest/product metadata.
- Smoke: Program.cs, Program.AxamlCapabilities.cs, Program.AxamlHardening.cs,
  новый Program.AxamlLayout.cs; десять fixtures и этот документ.
- Docs/AxamlRoundTripArchitecture.md и Docs/VisualStudioPoCArchitecture.md:
  ссылки на текущий subset и этот отчёт.

VSCT, команда меню, IPC, Avalonia baseline, Eremex API, Export generator,
JSON file schema и глобальные темы не изменялись.

## Ручная проверка

Закрыть Visual Studio, установить
`AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix` версии 0.1.13.
Вызвать `Tools / Средства -> Open in Avalonia UI Visual Designer`.

1. Повторить green baseline на SimpleAvaloniaApp, включая добавление и второй Apply.
2. Открыть настоящий Views/MainWindow.axaml. Ожидать partial, не полное совпадение UI.
3. Нажать Подробнее: 2507 XML / 1473 visual / 225 imported; первый blocker ComboBox.
4. Изменить доступное literal property; проверить минимальный diff в dirty VS buffer.
5. Открыть UnknownParent.axaml: объяснение пустой проекции вместо пустого Canvas.

Ручная проверка установленного VSIX в devenv.exe должна быть выполнена отдельно.
Автоматические проверки процесса VsHost не заменяют её.
