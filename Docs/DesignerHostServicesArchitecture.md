# Avalonia UI Visual Designer — архитектура Host Services

## Назначение

Этот этап отделяет потребности Designer от конкретного standalone host. `DesignerSurface`, `DesignerDocumentSession` и document-scoped logic знают, **какая** операция нужна: доступ к clipboard, выбор файла, confirmation, notification, путь к служебной папке или запуск внешнего ресурса. Они не знают, **как** standalone Avalonia-приложение выполняет эту операцию через `TopLevel`, `StorageProvider`, `Window`, `Process.Start` или `LocalApplicationData`.

Новая граница:

```text
DesignerSurface / DesignerDocumentSession / Designer logic
                         ↓
                IDesignerHostServices
                         ↓
          StandaloneDesignerHostServices
                         ↓
TopLevel / StorageProvider / Avalonia dialogs / Process / host paths
```

Будущий `VisualStudioDesignerHostServices` сможет реализовать тот же контракт, не создавая второй Canvas, Toolbox, Property Inspector или plugin system. На этом этапе не создавались VSIX, IPC, `AvaloniaDesigner.VsHost.exe`, AXAML import/round-trip или Visual Studio-specific classes.

## Граница ответственности

### Host-neutral слой

| Компонент | Ответственность | Запрещённые зависимости |
| --- | --- | --- |
| `DesignerDocumentSession` | Document state, controls, selection, history, dirty state и snapshot | `Window`, dialogs, clipboard, `StorageProvider`, settings, paths, plugin registry |
| `DesignerSurface` | Avalonia Canvas, selection visualization, UI events и привязка session | concrete `MainWindow`, `StorageProvider`, standalone dialogs, `Process.Start` |
| `DesignerSurfaceViewModel` | Ссылка на active session, temporary binding context и явно переданные host services | static current host, concrete standalone ViewModel |
| `DesignerSystem.Hosting` | Нейтральные contracts и DTO | Avalonia, Visual Studio, filesystem/UI implementation details |
| `ReflectionBindingMetadataProvider` | Import metadata DLL; application base directory передаётся извне | `AppContext.BaseDirectory` как скрытый путь host |

### Standalone host слой

| Компонент | Ответственность |
| --- | --- |
| `App.axaml.cs` | Composition root: создаёт `StandaloneDesignerHostServices`, регистрирует binding provider и plugins с host paths, создаёт `MainWindow` и `MainWindowViewModel` |
| `MainWindow` | Standalone window chrome, Avalonia event bridge, экспортные/навигационные окна и выбор host actions |
| `StandaloneDesignerHostServices` | Реализации host contracts через Avalonia `TopLevel`, `StorageProvider`, standalone dialogs, `Dispatcher.UIThread`, local paths и `Process.Start` |
| `MainWindowViewModel` | Существующий standalone coordinator и compatibility facade; получает paths/scheduler из injected host services |

`MainWindow` остаётся Avalonia presentation host. Его прямой focus management, окна Settings/Help/Preview, Export shell и smoke-test process являются host UI обязанностями и не передаются в `DesignerDocumentSession` или `DesignerSurface`.

## Карта зависимостей

| Location | Текущая зависимость | Причина использования | Ответственность | Target abstraction |
| --- | --- | --- | --- | --- |
| `Views/DesignerSurface.axaml.cs` | Нет concrete host API; принимает host services как explicit property | Surface должна быть создаваться в test host и будущем VsHost | Designer | `IDesignerHostServices` property |
| `DesignerSystem/DesignerDocumentSession.cs` | Нет UI/host dependency | Документ, selection, undo/redo и dirty state | Designer | Не требуется |
| `Views/MainWindow.axaml.cs` | `TopLevel.Clipboard` ранее использовался напрямую | Копирование generated text и build logs | Host | `IDesignerClipboard` |
| `Views/MainWindow.axaml.cs` | `StorageProvider` ранее использовался напрямую | Open/Save project, image, DLL, plugins, ZIP и folder selection | Host | `IDesignerFilePickerService` и `IDesignerHostFile` |
| `Views/MainWindow.axaml.cs` | `UnsavedChangesWindow` и host dialogs | Confirmation перед заменой несохранённого документа | Host | `IDesignerDialogService` |
| `Views/MainWindow.axaml.cs` | `Process.Start` ранее использовался при открытии folders | Logs, validation и build artifacts | Host | `IDesignerExternalLauncher` |
| `ViewModels/MainWindowViewModel.cs` | `Dispatcher.UIThread` и local log/plugin paths | Deferred UI refresh и standalone storage configuration | Host | `IDesignerScheduler`, `IDesignerPathService` |
| `App.axaml.cs` | `AppContext.BaseDirectory` для plugins | Startup plugin discovery и Reflection metadata resolver | Host composition | `IDesignerPathService.ApplicationBaseDirectory`, `PluginDirectory` |
| `AutosaveRecoveryService`, `AppSettingsService`, `DocumentBackupService`, `ReusableTemplateStorageService` | `LocalApplicationData` fallback | Existing standalone persistence locations | Host internal persistence | Явные paths передаются standalone composition; fallback сохранен для legacy direct construction |
| `DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs` | .NET Framework reference discovery | Metadata fallback для пользовательских legacy DLL | Binding engine | System reference discovery; это не UI host service |
| `Views/MainWindow.axaml.cs` | `TopLevel.GetTopLevel(this)?.FocusManager` | Focus management в standalone window input flow | Standalone presentation | Остаётся в host presentation code |
| `Views/PreviewWindow.axaml.cs` и Preview launch | Avalonia `Window` | Existing legacy/Runtime AXAML Preview presentation | Host presentation | Следующий этап: `IPreviewPresenter` или `IDesignerHostCommandService` |

## Контракты

`DesignerSystem/Hosting/DesignerHostServices.cs` определяет небольшой aggregate `IDesignerHostServices`:

| Contract | Назначение |
| --- | --- |
| `IDesignerClipboard` | Асинхронные `GetTextAsync` и `SetTextAsync`; контент clipboard не попадает в diagnostics |
| `IDesignerDialogService` | Intent-level `DesignerDialogRequest` и typed `DesignerDialogResult`, включая unsaved changes |
| `IDesignerFilePickerService` | Open/Save/SelectFolder через host-neutral options и filters |
| `IDesignerHostFile` | Host-owned file handle с optional `LocalPath` и stream API |
| `IDesignerNotificationService` | Structured notification: severity, title, message, details и persistence |
| `IDesignerPathService` | Application base, user data, logs, temp, plugins, recovery и artifacts |
| `IDesignerFileSystem` | Минимальные internal-file операции: existence, read/write, atomic write, delete |
| `IDesignerScheduler` | Минимальный bridge для posted/deferred UI work без `Dispatcher.UIThread` в shared flows |
| `IDesignerExternalLauncher` | Open file, folder или URI без direct `Process.Start` в Designer logic |
| `IDesignerHostCommandService` | Небольшой semantic command channel (`OpenSettings`, `OpenHelp`, `OpenPreview`, `OpenExport`) для последующего этапа |

Это не service locator: экземпляр создаётся в `App.axaml.cs`, передаётся конструктором в `MainWindow` и `MainWindowViewModel`, а затем явно назначается `DesignerSurface.HostServices`. Static `Current` и `Application.Current.Services` не используются.

## Standalone composition

```text
App.axaml.cs
    ├─ new StandaloneDesignerHostServices()
    ├─ ConfigureDesignerSystem(hostServices)
    │      ├─ ReflectionBindingMetadataProvider(host.Paths.ApplicationBaseDirectory)
    │      └─ PluginLoader.LoadFromFolder(host.Paths.PluginDirectory)
    ├─ new MainWindow(hostServices)
    └─ new MainWindowViewModel(registry, hostServices)
             └─ DesignerSurface.HostServices = hostServices
```

`StandaloneDesignerHostServices.AttachTopLevel` привязывает host adapter к созданному `MainWindow`. Только adapter затем использует Avalonia `Clipboard`, `StorageProvider`, `IStorageFile`, `Window`, `Dispatcher.UIThread` и `Process.Start`.

## File ownership

Разделяются два класса операций:

| Тип | Примеры | Владение |
| --- | --- | --- |
| Internal Designer files | recovery draft, app settings, templates, logs, plugin folder, artifacts | `IDesignerPathService` + `IDesignerFileSystem` |
| Project documents | `.formdesigner.json`, будущие `.axaml`, `.csproj`, source files | Текущий standalone save/open flow; будущий Visual Studio host будет владеть lifecycle документа |

Контракты не навязывают будущему Visual Studio host прямую запись в открытый AXAML-документ. Его document save lifecycle будет отдельным этапом.

## Preview, commands и notifications

На этом этапе Preview Window не переносился: он остаётся standalone Avalonia presentation feature. Shared document/session logic не создаёт `PreviewWindow` напрямую. Для будущего host уже существует `IDesignerHostCommandService`; extraction `IPreviewPresenter` будет отдельным, узким шагом после стабилизации текущего bridge.

`IDesignerNotificationService` передаёт structured notification. Standalone adapter публикует событие, а test host сохраняет сообщения для assertions. Existing status/toast presentation остаётся в `MainWindowViewModel`, поэтому текущий UX не был переработан.

## Diagnostics

Host boundary публикует существующие structured workspace events:

| Event | Смысл |
| --- | --- |
| `HOST_SERVICE_CALL` | Выполнена host operation, например external launcher |
| `HOST_FILE_PICKER_REQUEST` / `HOST_FILE_PICKER_RESULT` | Запрошен и завершён file/folder picker без вывода выбранных секретных данных |
| `HOST_DIALOG_REQUEST` / `HOST_DIALOG_RESULT` | Запрошен и завершён confirmation/dialog |
| `HOST_CLIPBOARD_READ` / `HOST_CLIPBOARD_WRITE` | Выполнена clipboard operation без content |
| `HOST_PATH_RESOLVED` | Использован logical host path без sensitive details |
| `HOST_COMMAND_REQUESTED` | Запрошена semantic host command |

## Проверки и архитектурные guard

В `smoke-tests/FormDesigner.ExportSmokeTests` добавлены:

| Scenario | Проверяемый invariant |
| --- | --- |
| `DesignerSurfaceWorksWithFakeHostServices` | `DesignerSurface` создаётся без `MainWindow` и получает explicit fake host |
| `DesignerHostServicesRouteClipboardFilePickerDialogAndNotification` | Canvas-related host interactions используют clipboard, picker, dialog, notification и paths contracts |
| `DesignerHostArchitectureGuard` | Shared Surface/session sources не содержат direct `MainWindow`, `StorageProvider`, `TopLevel.GetTopLevel`, `AppContext.BaseDirectory` или `Process.Start` dependencies |

Fakes реализуют весь `IDesignerHostServices`; тесты не требуют file picker, clipboard или dialogs реального desktop host.

## Будущее соответствие Visual Studio Host

```text
Сегодня
DesignerSurface -> IDesignerHostServices -> StandaloneDesignerHostServices

Будущий этап
DesignerSurface -> IDesignerHostServices -> VisualStudioDesignerHostServices
```

Будущий host сможет сопоставить picker с Visual Studio project/document services, notification с Output/InfoBar/Error List, scheduler с Visual Studio threading model и launcher с IDE command services. Реализация пока намеренно отсутствует.

## Ограничения текущего этапа

* Canvas pointer/drag/resize handlers всё ещё живут в `MainWindow.axaml.cs` как compatibility bridge после extraction `DesignerSurface`.
* Existing `MainWindowViewModel` остаётся большим standalone coordinator; он пока не превращался в host-neutral application layer.
* Settings, Help, Export shell и Preview Window пока standalone-specific.
* No VSIX, IPC, `AvaloniaDesigner.VsHost.exe`, Visual Studio API, AXAML importer/round-trip или target-framework migration не добавлялись.

Следующий этап может точечно заменить remaining preview/settings/help commands на `IDesignerHostCommandService` или отдельный `IPreviewPresenter`, но только после ручной проверки этого standalone bridge.
