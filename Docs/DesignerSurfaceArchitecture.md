# Avalonia UI Visual Designer — архитектура DesignerSurface

## Назначение

`DesignerSurface` — переиспользуемая Avalonia `UserControl` для рабочей области конструктора. Она создаётся независимо от `Window`, получает текущую `DesignerDocumentSession` и отображает document-scoped Canvas: форму, сетку, направляющие, selection overlay, resize handle, zoom и навигацию по документу.

Целевая цепочка для двух host выглядит так:

```text
Standalone MainWindow / будущий AvaloniaDesigner.VsHost.exe
    -> DesignerSurface
    -> DesignerDocumentSession
```

`DesignerDocumentSession` остаётся единственным владельцем `Controls`, selection, history и dirty state. `DesignerSurface` не копирует эти данные и не создаёт отдельную selection model.

## Состав

В текущей реализации reusable UI разбит на три компонента одной поверхности:

| Компонент | Ответственность | Текущее расположение в standalone layout |
| --- | --- | --- |
| `Views/DesignerSurface.axaml` | Canvas, документные вкладки, zoom, overlays, selection adorners и document interaction events | центральная рабочая область |
| `Views/DesignerToolbox.axaml` | поиск и provider/category Toolbox (`Стандартные`, `Eremex`, plugins) | вкладка `Компоненты` левой dock-панели |
| `Views/DesignerPropertyInspector.axaml` | таблица Property Inspector и inline editors | вкладка `Свойства` правой dock-панели |

Такое разделение сохраняет текущие размеры dock-панелей, Explorer, Data и Export UI без визуального redesign. Будущий host может расположить те же три controls в собственном chrome, не создавая вторую реализацию Canvas, Toolbox или Property Inspector.

## Связь с DesignerDocumentSession

`DesignerSurface.Session` — typed Avalonia property типа `DesignerDocumentSession`.

При её изменении компонент:

1. Отписывается от предыдущей session.
2. Подписывается на `SelectionChanged` новой session.
3. Публикует `DESIGNER_SURFACE_SESSION_ATTACHED` или `DESIGNER_SURFACE_SESSION_DETACHED`.
4. Не изменяет `Controls`, `SelectedControlIds`, undo/redo или dirty state напрямую.

Selection flows в обе стороны через session:

```text
Canvas interaction -> MainWindowViewModel facade -> ActiveSession.SetSelection(...)
Explorer -> ActiveSession.SetSelection(...)
ActiveSession.SelectionChanged -> DesignerSurface / Property Inspector refresh
```

Поэтому состояние не может расходиться между Canvas и Property Inspector: `MainWindowViewModel.SelectedControl` остаётся compatibility facade над `ActiveSession.SelectedControl`.

## Host-neutral contract

`DesignerSurface` не ссылается на `MainWindow`, `MainWindowViewModel`, `Application.Current`, `StorageProvider` или dialogs. Публичный минимальный контракт:

```csharp
public object? Context { get; set; }
public DesignerDocumentSession? Session { get; set; }
public event EventHandler<DesignerSurfaceDiagnosticEventArgs>? DiagnosticReported;
```

`Context` является переходным binding facade для уже существующих команд и presentation properties. Его concrete type не является частью API `DesignerSurface`; будущий host сможет передать другой context, реализующий нужные bindings. Это избегает массовой замены проверенных AXAML bindings в одном изменении.

## Canvas и interaction lifecycle

Рабочий Canvas visual tree (`DesignerViewportScrollViewer`, `DesignSurfaceHost`, `DesignerCanvas`, overlays, resize handle, minimap) принадлежит `DesignerSurface`. `MainWindow` использует его только через временные accessors. Скрытая legacy-разметка остаётся в AXAML исключительно как переходный compatibility template и не участвует в render path.

`DesignerSurface` публикует события pointer, drag/drop, resize и zoom. На данном переходном этапе `MainWindow` подписывает существующие, проверенные handlers на эти события. Это intentional compatibility bridge: drag engine, smart guides, resize, inline editing и preview factory сохраняют прежнее поведение и не получают второй реализации.

Следующее безопасное выделение после введения host service abstractions — перенести этот controller из `MainWindow.axaml.cs` в отдельный surface interaction controller, сохранив тот же event contract. До этого этапа нельзя удалять текущие handlers или переписывать coordinate system.

## Toolbox и Property Inspector

`DesignerToolbox` использует существующий `ToolboxGroups` registry, поэтому не имеет собственного каталога control descriptors. Provider/category grouping остаётся единым:

```text
Стандартные
Eremex
Пользовательские плагины
```

`DesignerPropertyInspector` использует существующие `PropertyGridCategories` и row view models. Цветовые, action и reset requests поднимаются событийно в host; сами изменения идут через existing commands и session-backed property model.

Explorer пока остаётся в `MainWindow`: он относится к project shell, а не к одной designer surface. Он взаимодействует с surface только через `ActiveSession.SetSelection(...)`; прямого поиска визуала Canvas из Explorer нет.

## Plugins и Eremex

Preview creation остаётся в существующей descriptor/registry infrastructure. `DesignerSurface` не содержит условий по TypeKey, Eremex или отдельным Avalonia controls. Это сохраняет текущую plugin isolation и DeltaDesign theme scope:

- standard controls используют текущий preview renderer;
- plugin controls создаются descriptor factory;
- Eremex TextEditor и Eremex DataGridControl проходят тот же Canvas path;
- DeltaDesignTheme не добавляется в `Application.Current.Styles` и не меняет Toolbox/Property Inspector/host chrome.

## Diagnostics

Surface публикует следующие структурированные события через `DiagnosticReported`:

| Событие | Когда публикуется |
| --- | --- |
| `DESIGNER_SURFACE_CREATED` | visual attached |
| `DESIGNER_SURFACE_DISPOSED` | visual detached |
| `DESIGNER_SURFACE_CONTEXT_ATTACHED` | назначен host context |
| `DESIGNER_SURFACE_SESSION_ATTACHED` | назначена session |
| `DESIGNER_SURFACE_SESSION_DETACHED` | старая session отписана |
| `DESIGNER_SURFACE_SELECTION_SYNC` | selection изменилась в active session |
| `DESIGNER_SURFACE_RENDER_START` / `DESIGNER_SURFACE_RENDER_END` | началась / закончилась отрисовка Canvas |
| `DESIGNER_SURFACE_DROP` | Toolbox или внешний источник dropped на Canvas |
| `DESIGNER_SURFACE_RESIZE` | началось изменение размера формы |

Standalone bridge записывает эти события в существующий workspace log. Surface не пишет в global log самостоятельно.

## Lifecycle и память

При смене формы standalone не создаёт новый `DesignerSurface`. У существующего компонента обновляется `Session`; старая подписка `SelectionChanged` снимается до подключения новой. При `New Project`, удалении формы или закрытии visual tree session больше не удерживается surface.

`DesignerSurface` не имеет `IDisposable`: у него нет собственных таймеров, файловых handles или внешних resources. Отписка выполняется в `DetachedFromVisualTree` и при изменении `Session`.

## Проверки

Добавлены smoke scenarios:

- `DesignerSurfaceCanBeCreatedWithoutMainWindow`;
- `DesignerSurfaceCanAttachDocumentSession`;
- `DesignerSurfaceCanSwitchDocumentSessions`;
- `DesignerSurfaceRendersStandardControl`;
- `DesignerSurfaceToolboxUsesSharedRegistry`;
- `DesignerSurfaceSelectionUpdatesSession`;
- `SessionSelectionUpdatesDesignerSurface`;
- `DesignerSurfaceInspectorUsesSessionSelection`;
- `MainWindowSwitchFormRebindsSameDesignerSurface`;
- `DesignerSurfaceDoesNotRequireMainWindowConcreteType`;
- `DesignerSurfaceRendersEremexTextEditor`;
- `DesignerSurfaceRendersEremexDataGrid`.

Они проходят через реальный `MainWindow -> DesignerSurface` Canvas path, а не только через `Activator.CreateInstance` descriptor controls.

## Оставшиеся зависимости и следующий этап

На этом этапе намеренно остаются:

| Зависимость | Причина | План |
| --- | --- | --- |
| Canvas interaction handlers в `MainWindow.axaml.cs` | compatibility bridge предотвращает регрессию drag/resize/smart guides | перенести в отдельный controller после ввода host services |
| Presentation bindings через `Context` | migration facade для большого существующего AXAML | заменить на host-neutral surface context постепенно |
| Explorer, Data, Logic и Export UI в `MainWindow` | это project/application shell, не document surface | оставить за пределами DesignerSurface |
| dialogs, clipboard, storage, settings | зависят от standalone host | вынести в будущий host service contract |

Не выделены VSIX, IPC, `AvaloniaDesigner.VsHost.exe`, AXAML import/round-trip и глобальные host services. Они остаются следующими независимыми этапами.
