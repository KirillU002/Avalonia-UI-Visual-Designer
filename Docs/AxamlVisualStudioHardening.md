# AXAML lifecycle hardening, VSIX 0.1.11

## Подтверждённые причины

### AXAML попадал в JSON reopen

VsHost использует существующий `MainWindow` как compatibility host общего
`DesignerSurface`. Вместе с ним наследовался `MainWindow_Opened`:

```text
MainWindow_Opened
  -> TryRestoreLastSessionDocumentAsync
  -> File.ReadAllTextAsync(LastProjectPath)
  -> MainWindowViewModel.LoadDocumentJson
  -> ProjectWorkspaceService.TryDeserializeWorkspace (ловит JsonException)
  -> JsonSerializer.Deserialize<DesignerDocumentFileModel> (исключение наружу)
```

В `%LOCALAPPDATA%/FormDesigner/VsHost/logs/designer-20260917.log` в 15:10:20Z
зафиксирован именно этот reopen файла `Samples/VisualStudioPoC/SimpleAvaloniaApp/MainWindow.axaml`.
Настройки VsHost содержали его в `Session.LastProjectPath` при включённом
`ReopenLastWorkspaceOnStartup`. Старый catch записывал только Message, без stack trace.

До исправления regression воспроизвёл стек:

```text
System.Text.Json.JsonException: '<' is an invalid start of a value.
  -> System.Text.Json.JsonReaderException
  -> System.Text.Json.JsonSerializer.Deserialize<TValue>
  -> MainWindowViewModel.LoadDocumentJson (старая строка 9298)
  -> AssertJsonFormatGuard
```

Это отдельный startup-сбой, а не ошибка разбора сложного XML и не проблема VSCT.

### XML-транспорт изменял snapshot

`NamedPipeProtocolConnection.Serialize` использовал `XmlSerializer.Serialize(TextWriter, ...)`.
При чтении literal CR/CRLF нормализовались XML reader. Сначала сериализуется payload,
затем содержащий его envelope: сохранять CR необходимо на обоих уровнях.

До исправления тест показал: исходная строка 35 символов / 2 CR превращалась в
33 символа / 0 CR. Переданный checksum оставался прежним. SHA-256 вычислялся правильно;
изменялся именно текст. `VsHostBridge.OpenDocumentAsync` обнаруживал реальное несоответствие.

Проверенный реальный `Views/MainWindow.axaml`: 462480 UTF-16 code units, 3831 CR,
8321 LF. SimpleAvaloniaApp: 750 code units, 0 CR, 28 LF. Поэтому простой fixture
не обнаруживал дефект. UTF-8 SHA-256 не нормализует строки и не зависит от encoding файла
на диске: VS передаёт уже декодированный buffer, не файл. BOM-символ, если он есть в строке,
сохраняется. Encoding/BOM файла при сохранении по-прежнему принадлежат Visual Studio.

### Read-only считался невозможностью открыть

Importer корректно определял отсутствие прямого Canvas как `ReadOnly`, но
`LoadAxamlImportedDocument` требовал `CanSafelyPatch`, а bridge отвечал до подключения
сессии к surface. Теперь `CanOpen` отделён от права редактировать.

## Исправленный lifecycle

* `DesignerDocumentKind.ProjectJson / AxamlRoundTrip` явно различает форматы.
  `DocumentKind` вычисляется из существующего authoritative AXAML context, не дублирует его.
* `LoadDocumentJson` проверяет формат и путь до сброса текущего context. Ошибочный вызов
  возвращает `DOCUMENT_FORMAT_MISMATCH expected=... actual=... caller=...`.
* `DesignerDocumentPersistence.HostBuffer` передаётся в конструктор окна VsHost.
  Startup recovery/reopen с диска и JSON autosave в этом режиме не запускаются.
* Standalone reopen/recent различают JSON и AXAML по расширению. JSON workflow остаётся
  прежним; legacy JSON recovery с AXAML destination отвергается до изменения сессии.
  AXAML не автосохраняется как JSON без source map.
* Save/Save As в host-buffer режиме направляются на Apply, Open/reopen на Reload.
  Эти команды не читают/пишут исходный AXAML на диске. Закрытие не подтверждается
  синхронно при ещё не подтверждённом Apply.
* XML writer использует `NewLineHandling.Entitize` для payload и envelope. Формат protocol
  и SHA-256 не заменены, checksum-проверки не ослаблены.
* Open/Reload проверяют checksum и версию до замены сессии. Snapshot не меняется при ошибке.
* Patch и ACK коррелируются по RequestId, DocumentId и версии. Второй Apply/reload во время
  ожидания ACK блокируется. ACK проверяется против фактически отправленного patched text.
* При ACK source map перебазируется на подтверждённый текст. SavedSnapshot берётся из
  отправленной ревизии, поэтому правки после нажатия Apply остаются dirty.
* Reload захватывает конкретный открытый документ, а не случайно активную другую вкладку.
  Patch не имеет fallback на другой active document.

## Применение в VS buffer

Перед первым изменением проверяются все диапазоны и пересечения edits.
UTF-16 offsets переводятся в DTE line/column: DTE absolute offsets считают CRLF одним
символом ([Microsoft](https://learn.microsoft.com/en-us/dotnet/api/envdte.editpoint.absolutecharoffset?view=visualstudiosdk-2022)).
Автоформатирование `ReplaceText` отключено. Используется локальный undo context операции;
это не реализация синхронизации глобальной VS history. Buffer становится dirty, Save не вызывается.

## Неподдерживаемый AXAML

* Прямой Canvas с поддерживаемыми leaf controls остаётся редактируемым.
* Styles, Resources, bindings, custom namespaces/controls, вложенные layouts сохраняются opaque.
* Тип контрола определяется с учётом xmlns: `custom:Button` не становится обычным Button.
* Window/UserControl с корневым Grid, включая настоящий MainWindow, открывается read-only.
  Его сложный layout не визуализируется: projection может быть пустой.
* No-edit patch для read-only projection является identity operation: исходный текст возвращается
  без edits. Попытка добавить controls в такую projection блокируется, исходник не регенерируется.
* Повреждённый AXAML не заменяет текущий документ; bridge возвращает конкретную ошибку разбора.

## Диагностика

VSIX: `VSIX_DOCUMENT_CAPTURE`, `IPC_OPEN_DOCUMENT_SEND`, `IPC_RELOAD_DOCUMENT_SEND`,
`VSIX_PATCH_RECEIVED`, `PATCH_APPLIED_TO_VS_BUFFER`.
VsHost: `HOST_BUFFER_LIFECYCLE`, `VSHOST_OPEN_DOCUMENT_RECEIVED`, `AXAML_SESSION_CREATED`,
`AXAML_SESSION_REOPEN`, `AXAML_IMPORT_CAPABILITY`, `VSHOST_PATCH_*`.
Логи содержат document id, version, length, переданный/вычисленный checksum, но не исходный AXAML.
VsHost пишет их в существующий workspace log, а не только `Trace`.

## Regression coverage

В `Program.AxamlHardening.cs`:

* `AxamlHardeningProtocolPreservesSnapshot`: CRLF/LF/CR, BOM, Unicode, реальный MainWindow;
  OpenDocument, PatchApplied, DocumentChanged, вставляемый NewText.
* `AxamlHardeningJsonFormatGuard`: ошибка формата вместо raw JsonException.
* `AxamlHardeningRealMainWindow`: реальный исходник без изменений, не специальная модель его UI.
* `AxamlHardeningComplexSourcePreservation`: Styles, Bindings, unknown xmlns/attributes/controls,
  comments; ровно четыре value edits, external-change conflict.
* `AxamlHardeningJsonWorkflowAndReopen`: JSON продолжает загружаться; неудачный import не
  уничтожает текущую AXAML session; standalone reopen выбирает правильный формат.
* `AxamlHardeningVsBufferCoordinates`: CRLF offsets и overlap validation.
* `AxamlHardeningIpcEditReloadLifecycle`: реальный pipe, общий surface, unsaved input,
  Apply/ACK/reload, правки во время ACK, stale reload, неверный checksum, реальный read-only документ.
* `AxamlHardeningRealDocumentExternalProcess`: отдельный VsHost.exe, SimpleAvaloniaApp,
  generic complex fixture, настоящий MainWindow и повторное открытие в обратном порядке.

Smoke runner инициализирует Avalonia до создания view models, чтобы асинхронные IPC-тесты
использовали настоящий UI dispatcher, а не созданный раньше fallback dispatcher.
Отдельно проверен XML transport из netstandard2.0 assembly в CLR 4.0.30319.42000:
реальный MainWindow прошёл text/checksum equality, как и в .NET 6.

Полный ручной цикл установленного расширения в devenv.exe после переустановки 0.1.11 остаётся
отдельной проверкой. Автотесты реального внешнего процесса не подменяются утверждением о
ручной проверке Visual Studio. Отчёты прогонов: `artifacts/diagnostics/axaml-hardening/`.

## Результаты проверки 18.09.2026

`dotnet build FormDesigner.sln --no-restore -v:minimal -m:1`: успешно, 0 ошибок.
Сохранилось прежнее NU1603: VSSDK выбирает SDK.Analyzers 17.7.22 вместо отсутствующей
17.7.20. Зависимости в рамках исправления не обновлялись.

| Группа | Результат |
| --- | --- |
| AxamlHardening | 8/8 |
| AxamlRoundTrip | 9/9 |
| DesignerSurface | 14/14 |
| Vsix | 5/5 |
| VsHost | 10/10 |
| EremexTextEditorVerticalSlice | 1/1 |
| EremexDataGridControlVerticalSlice | 1/1 |
| SaveLoadMultiFormProject | 1/1 |
| MultiFormToolboxDropPropertyEdit | 1/1 |
| DesignerPropertyInspectorFavoritesRoundTrip | 1/1 |
| NewProject | 7/7 |

Всего 58 успешных выполнений, 55 уникальных сценариев: некоторые фильтры пересекаются.
После последнего уточнения статусов повторены AxamlHardening и Vsix, также без failures.
Есть нефатальные сообщения cleanup об оставшихся заблокированных каталогах smoke workspace;
это не ошибка импорта или сборки. Крупные bin/obj этих прогонов удалены.

Проверен итоговый ZIP `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`:
версия 0.1.11, 130 entries. SHA-256 Protocol для netstandard2.0 и net6.0, VsHost и
FormDesigner совпадают с актуальной сборкой. Avalonia/FormDesigner/Eremex assemblies
в корне VSIX отсутствуют; визуальный runtime остаётся в `VsHost/`.

### Ручная проверка установленного расширения

1. Закрыть Visual Studio, установить VSIX 0.1.11, снова открыть Visual Studio.
2. Открыть настоящий `Views/MainWindow.axaml`, вызвать
   `Tools / Средства -> Open in Avalonia UI Visual Designer`.
3. Ожидать ограниченный/read-only режим без JSON exception и checksum error.
   Пустая визуальная projection сложного Grid пока допустима; это не полный Grid designer.
4. Открыть SimpleAvaloniaApp или ComplexAxamlVisualStudioRoundTrip. Изменить текст в VS
   без сохранения, затем повторно открыть Designer: должен использоваться текст буфера.
5. Изменить Button.Content, Width, X/Y, нажать Apply. Проверить dirty buffer, минимальный
   diff, сохранённые comments и unknown attributes. Автоматического сохранения быть не должно.
6. Изменить VS buffer после открытия Designer и попытаться применить устаревший patch:
   ожидается conflict без перезаписи. Reload должен получать актуальный буфер.

## Файлы

Protocol: `NamedPipeProtocolConnection.cs`.
VSIX: `VsDocumentBuffer.cs`, `VsTextPatch.cs`, `VsHostBridgeClient.cs`,
`OpenInAvaloniaDesignerCommand.cs`; версия в manifest и package attribute.
VsHost: `VsHostBridge.cs`, `VsHostBridge.DocumentChanged.cs`, `VsHostWindow.cs`.
Designer: `DesignerDocumentKind.cs`, `Hosting/DesignerDocumentPersistence.cs`,
четыре файла `AxamlRoundTrip`, `MainWindowViewModel.cs`, `MainWindow.axaml.cs`.
Tests: partial runner, registrations/csproj, `ComplexAxamlVisualStudioRoundTrip/MainWindow.axaml`.

DesignerSurface, исходный MainWindow.axaml, Eremex, версии Avalonia, VSCT и Export Pipeline
не изменены. Старые незакоммиченные изменения cleanup сохранены отдельно от этой задачи.
