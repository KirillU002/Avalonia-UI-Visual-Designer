# Avalonia UI Visual Designer — архитектура `DesignerDocumentSession`

## Назначение

`DesignerDocumentSession` — это runtime-граница состояния одной открытой формы Designer. Она выделена из `MainWindowViewModel` как первый безопасный шаг к будущей схеме с двумя host:

```text
Standalone application
Visual Studio Host
        ↓
DesignerDocumentSession
```

На данном этапе standalone-приложение сохраняет существующий UX, Canvas, Property Inspector, Toolbox, Preview, Export Pipeline и plugin system. `DesignerDocumentSession` не является `DesignerSurface`, не содержит host API и не выполняет IPC-задачи.

## Ответственность `DesignerDocumentSession`

Одна session владеет состоянием одного `DesignerFormDocument`:

| Состояние | Владелец | Примечание |
| --- | --- | --- |
| Идентификатор и модель формы | `DesignerDocumentSession` | `DocumentId`, `FormDocument`, `Document` |
| Runtime-элементы формы | `DesignerDocumentSession.Controls` | Единственная runtime-коллекция активной формы |
| Selection | `SelectedControl`, `SelectedControlIds` | Один authoritative source для primary и multi-selection |
| Undo/Redo | `UndoSnapshots`, `RedoSnapshots` | История изолирована по документам |
| Snapshot и dirty state | `CurrentSnapshot`, `SavedSnapshot`, `IsDirty` | Project-level dirty по-прежнему вычисляется существующим workspace flow |
| Revision | `Revision` | Изменяется при обновлении модели, истории и snapshot |

Session публикует только минимальные уведомления:

```text
SelectionChanged
DocumentChanged
HistoryChanged
DirtyStateChanged
```

Она не обращается к `Application.Current`, `Window`, `StorageProvider`, dialogs, clipboard, Settings, SQL configuration, NuGet, global logs или plugin registry.

## Что осталось в `MainWindowViewModel`

`MainWindowViewModel` остаётся standalone coordinator и compatibility facade. На этом этапе в нём остаются:

* Canvas и его Avalonia-specific lifecycle;
* Property Inspector UI, BindingSources и Interactions;
* Toolbox, commands, dialogs и standalone navigation;
* Preview, AXAML Preview и Export Pipeline orchestration;
* global Settings, SQL/NuGet configuration, diagnostics и workspace UI;
* `ApplyDocument` и существующая сериализация формы.

Старые публичные свойства намеренно сохранены для существующих AXAML bindings и команд:

```csharp
public ObservableCollection<DesignControlModel> Controls => ActiveSession.Controls;
public ObservableCollection<string> SelectedControlIds => ActiveSession.SelectedControlIds;
public DesignControlModel? SelectedControl
{
    get => ActiveSession.SelectedControl;
    set => ActiveSession.SetSelectedControl(value);
}
```

Следовательно, `MainWindowViewModel` больше не имеет самостоятельных конкурирующих коллекций controls, selection или undo/redo. Он делегирует их `ActiveSession`.

## Lifecycle

```text
Создание workspace
    ↓
Create `DesignerDocumentSession` для Form1
    ↓
Activate session
    ↓
ApplyDocument hydrates runtime Controls

Add Form
    ↓
Persist active session
    ↓
Create session для новой формы
    ↓
Activate session

Switch Form
    ↓
Persist outgoing session
    ↓
Deactivate runtime controls текущей session
    ↓
Activate target session
    ↓
ApplyDocument hydrates Controls target form

Delete Form
    ↓
Activate remaining form
    ↓
Dispose удалённой session

New Project / Open Project
    ↓
Dispose all form sessions
    ↓
Create and activate sessions нового workspace
```

Inactive sessions сохраняют документ, history и dirty state. Их runtime `Controls` намеренно очищаются при деактивации: это повторяет прежний `ApplyDocument` flow и не позволяет старым form visuals, event subscriptions и model references удерживаться в памяти. При повторной активации controls детерминированно восстанавливаются из `CurrentSnapshot`.

`_bootstrapSession` существует только во время начальной инициализации `MainWindowViewModel`, до появления реального `Workspace`. Она transient и не попадает в JSON.

## Selection и Property Inspector

`ActiveSession.SelectedControl` — единственный источник primary selection. `SelectedControlIds` принадлежат той же session и синхронизируются с `Controls`.

Поток выглядит так:

```text
Canvas / Structure Tree / Property Inspector command
        ↓
ActiveSession.SetSelection(...)
        ↓
SelectionChanged
        ↓
MainWindowViewModel compatibility handler
        ↓
Property Inspector / Structure Tree refresh
```

При переключении формы сохраняется текущий UX: transient Property Inspector state и selection очищаются. При этом selection не может попасть в другую форму, поскольку каждая session владеет своей коллекцией IDs и runtime controls.

## Undo/Redo и dirty state

Undo/Redo stacks принадлежат `ActiveSession`. Поэтому команда Undo на Form2 не изменяет history Form1. `MainWindowViewModel` по-прежнему использует существующий snapshot format и существующий `RestoreFromSnapshot`/`ApplyDocument` путь, чтобы не вернуть исторические ошибки selection и Property Inspector.

`PersistActiveFormDocumentState` сохраняет в `DesignerFormDocument` только данные active session:

```text
Document
CurrentSnapshot
SavedSnapshot
UndoSnapshots
RedoSnapshots
IsDirty
```

## Invariants

После этапа должны соблюдаться следующие правила:

1. `Controls` active form имеют одного владельца: `ActiveSession.Controls`.
2. `SelectedControl` и `SelectedControlIds` принадлежат одной active session.
3. `MainWindowViewModel.SelectedControl` не хранит отдельный backing state.
4. Undo/Redo stacks изолированы на уровне `DesignerDocumentSession`.
5. `IsDirty` основывается на snapshot одной session.
6. Session удалённой формы вызывается `Dispose` и исключается из `DocumentSessions`.
7. `New Project` и `ApplyWorkspace` не оставляют старые sessions активными или зарегистрированными.
8. `ApplyDocument` не создаёт альтернативную коллекцию controls: он гидратирует `ActiveSession.Controls` через compatibility facade.

При нарушении active session ownership пишется `DOCUMENT_SESSION_STATE_MISMATCH`.

## Diagnostics

Добавлены структурированные события:

| Event | Когда пишется |
| --- | --- |
| `DOCUMENT_SESSION_CREATED` | Создана session для формы |
| `DOCUMENT_SESSION_ACTIVATED` | Активирована target session |
| `DOCUMENT_SESSION_DEACTIVATED` | Деактивирована предыдущая session |
| `DOCUMENT_SESSION_SELECTION_CHANGED` | Изменилась selection active session |
| `DOCUMENT_SESSION_HISTORY_PUSHED` | Создан либо сгруппирован history snapshot |
| `DOCUMENT_SESSION_UNDO` | Выполнен Undo активной формы |
| `DOCUMENT_SESSION_REDO` | Выполнен Redo активной формы |
| `DOCUMENT_SESSION_DISPOSED` | Удалена session формы или export-only ViewModel |
| `DOCUMENT_SESSION_STATE_MISMATCH` | Event пришёл не от active session |

## Regression coverage

В `smoke-tests/FormDesigner.ExportSmokeTests` добавлены сценарии:

* `DesignerDocumentSessionCreatedForOpenedForm`;
* `SelectionLivesInActiveDocumentSession`;
* `SwitchingFormsSwitchesDocumentSession`;
* `AddFormDoesNotMutateExistingSession`;
* `DeletingFormDisposesItsSession`;
* `NewProjectDisposesAllOldDocumentSessions`;
* `PropertyInspectorFollowsActiveSessionSelection`;
* `UndoRedoIsIsolatedPerDocumentSession`;
* `ApplyDocumentPreservesSelectionThroughSession`.

Они дополняют уже существующие Multi Form, Property Inspector, New Project, Preview, Export и Eremex smoke scenarios. Новый набор не заменяет ручную проверку Canvas, drag/resize и real visual Preview.

## Граница текущего этапа

В этом этапе намеренно не выполнялись:

* выделение `DesignerSurface`;
* создание `IDesignerHost`, VSIX, `AvaloniaDesigner.VsHost.exe` или IPC;
* перенос Canvas UI, Toolbox или Property Inspector;
* AXAML importer / round-trip editing;
* изменение Export Pipeline или plugin contract;
* обновление .NET, Avalonia или Eremex.

## Следующий архитектурный шаг

После устойчивой эксплуатации `DesignerDocumentSession` возможен отдельный этап выделения `DesignerSurface`: она должна получать session как входное состояние и не создавать ещё один document owner. До этого шага standalone host остаётся единственным UI host и продолжает использовать `MainWindowViewModel` как coordinator.
