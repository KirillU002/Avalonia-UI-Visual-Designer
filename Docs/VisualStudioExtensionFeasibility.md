# Avalonia UI Visual Designer — исследование интеграции с Visual Studio

**Статус:** только архитектурное исследование. Этот документ не меняет production-код и target framework.  
**Дата исследования:** 2026-08-23.  
**Целевой baseline расширения:** собирать VSIX против стабильного Visual Studio 2022 API 17.x (минимум 17.0; проверить на актуальной 17.14) и тестировать на Visual Studio 2026. Microsoft документирует, что совместимые VSIX для Visual Studio 2022 могут загружаться в Visual Studio 2026 через модель совместимости версий API. Не использовать preview-only VS API как production-зависимость.  
**Решение:** **Да, при соблюдении условий.** Продукт может стать единой платформой Designer с самостоятельным host и host, связанным с Visual Studio. Рекомендуемая реализация первого этапа — **отдельный Avalonia Designer Host process и минимальный VSIX bridge**. Прямое встраивание текущей поверхности Avalonia/Eremex в `devenv.exe` не является безопасной первой реализацией.

> Один Avalonia UI Visual Designer может существовать одновременно как самостоятельное приложение и как расширение Visual Studio. Рекомендуемая архитектура первого этапа — минимальный VSIX bridge + IPC + отдельный AvaloniaDesigner.VsHost.exe, использующий общий DesignerSurface и общее ядро со standalone-приложением.

## 1. Краткий итог

В репозитории уже есть сильный переиспользуемый доменный слой: сохраняемые документы Designer, multi-form workspace, registry/descriptors, plugin contracts, binding-модели, diagnostics, generated AXAML/C#, export contributions и smoke coverage. В нём пока нет переиспользуемой editor surface, независимой от host. Большая часть orchestration сосредоточена в `MainWindowViewModel` (24 257 строк), а большая часть Canvas interaction находится в `MainWindow.axaml.cs` (10 477 строк).

Желаемый продукт с двумя host достижим без двух Designer, если явно выделить границы:

```text
                     AvaloniaDesigner.Domain
                               |
                     AvaloniaDesigner.Engine
             (registry, plugins, export, diagnostics)
                               |
                 AvaloniaDesigner.AvaloniaSurface
        (Canvas, Toolbox, Property Inspector, selection)
                    /                         \
     AvaloniaDesigner.Standalone        AvaloniaDesigner.VsHost.exe
                                              |
                                       authenticated IPC
                                              |
                              AvaloniaDesigner.VSIX bridge
                     (commands, document/version bridge, project integration)
```

VSIX намеренно является bridge, а не вторым Canvas или Property Inspector. `VsHost.exe` использует ту же Avalonia surface, что и самостоятельное приложение, и владеет Avalonia, Eremex, plugin loading и Runtime AXAML preview. Это сохраняет текущую изоляцию темы Eremex и не загружает сторонние Avalonia controls в `devenv.exe`.

Главный продуктовый блокер — не оболочка VSIX. Это **безопасное AXAML round-trip editing**. Текущий продукт сохраняет `.formdesigner.json` и генерирует AXAML; он не импортирует произвольный `.axaml` в `DesignerDocumentFileModel` и не может при сохранении гарантированно сохранить вручную написанные XML, styles, comments и unknown controls. Это необходимо реализовать до заявления о том, что Designer способен открывать и сохранять произвольные существующие Avalonia views из Visual Studio.

## 2. Прямые ответы

| Вопрос | Ответ |
| --- | --- |
| Может ли standalone оставаться полноценным продуктом? | Да. Он становится одним из host для общих domain/engine/surface проектов; его окна, settings и dialogs остаются standalone-специфичными. |
| Можно ли переиспользовать логику, Canvas, Toolbox, Property Inspector, plugins, Eremex и export? | Да, между Avalonia host. Их нельзя безопасно поместить прямо в процесс VS без отдельной высокорисковой интеграции. |
| Может ли VSIX открыть выбранный `.axaml` и запустить Designer? | Да. Небольшой VSIX может добавить context command и передать текст/путь документа внешнему Avalonia host через IPC. |
| Может ли текущий продукт безопасно редактировать произвольный существующий AXAML? | Нет, пока нет. Необходимы importer, syntax-preserving writer и conflict model. |
| Нужно ли в первом релизе использовать встроенные VS Toolbox и Properties window? | Нет. Следует переиспользовать существующие Designer Toolbox и Property Inspector внутри Avalonia host. |
| Рекомендуется ли сейчас прямое встраивание Avalonia/Eremex в Visual Studio? | Нет. Оно сочетает неподтверждённый/экспериментальный hosting с рисками .NET runtime, theming, assembly и crash isolation. |

## 3. Фактические данные текущего репозитория

### 3.1. Структура solution и runtime topology

Корневое приложение — `FormDesigner.csproj`, desktop-приложение Avalonia 11.1.5 с target framework `net6.0`. `App.axaml.cs` создаёт `DesignerRegistry`, регистрирует built-ins, загружает plugins из `AppContext.BaseDirectory/Plugins`, затем создаёт `MainWindow` и `MainWindowViewModel`. `PluginContracts` — отдельный проект `net6.0`, но он намеренно ссылается на Avalonia, поскольку `IControlDescriptor.BuildPreview` возвращает `Avalonia.Controls.Control`.

Solution содержит корневое приложение, contracts и demo/minimal plugins. Eremex plugin подключён приложением как build dependency и копируется в plugin output folder. Его controls являются реальными Avalonia controls и зависят от identity Avalonia assemblies host-процесса.

### 3.2. Уже имеющиеся переиспользуемые части

| Подсистема | Фактические данные | Оценка переиспользования в VS host |
| --- | --- | --- |
| Project/document models | `Models/ProjectModels.cs`, `DesignerDocumentFileModel.cs`, `DesignControlModel.cs`, binding и interaction models | Высокая; вынести без зависимостей от Avalonia UI. |
| JSON workspace persistence | `ProjectWorkspaceService` сериализует/загружает workspace models | Высокая; владение путями должно стать host abstraction. |
| Plugin registry/contracts | `PluginContracts/DesignerContracts.cs`, `DesignerRegistry`, `PluginLoader` | Высокая для внешнего Avalonia host; не используется напрямую .NET Framework VSIX. |
| Standard descriptors | `BuiltInControlRegistrar`, descriptor contexts | Высокая после переноса ниже standalone window. |
| Eremex adapter | `Plugins/EremexDesignerPlugin`, preview/export contributions, scoped DeltaDesign theme | Высокая во внешнем Avalonia host; намеренно небезопасна в `devenv.exe`. |
| Binding/SQL/DLL models | `DesignerSystem/Binding`, metadata providers, preview loaders | Средне-высокая; credentials, solution paths и package resolution должны быть host services. |
| Diagnostics/logging | `DocumentDiagnosticsService`, `WorkspaceLogService`, structured models | Высокая; VS будет отображать записи в Output/Error List, а не заменять internal diagnostics. |
| Export/build services | `ExportPipelineService`, generated file models и plugin contributions | Средне-высокая; часть generation code orchestrated из `MainWindowViewModel`. |
| Undo/redo command intent | `EditorCommands`, persisted snapshot history в `MainWindowViewModel` | Средняя; нужен отдельный document command/history service. |
| Runtime AXAML preview | `RuntimeAxamlPreviewLoader`, runtime preview contributions | Средне-высокая во внешнем Avalonia process; нужна policy для project dependency resolution. |
| Canvas и selection | `MainWindow.axaml.cs` управляет rendering, pointer input, adorner state, drag/drop и resize | Низкая в текущем виде; извлечь Avalonia `DesignerSurface` control. |
| Property Inspector | View models переиспользуемы концептуально, но rows/rebuild lifecycle находятся в `MainWindowViewModel`, а custom editor code обращается к `MainWindow` | Средняя после извлечения. |
| Toolbox | Список основан на registry, но layout/filtering находятся в main VM/XAML | Средне-высокая после извлечения. |
| Settings/dialogs/file picking | `SettingsWindow`, `StorageProvider`, `TopLevel`, `ShowDialog` и AppData services | Специфичны для host. |

### 3.3. Текущая связанность со standalone window

| Подсистема | Текущая связанность | Переиспользование напрямую в VSIX? | Необходимый рефакторинг | Риск |
| --- | --- | --- | --- | --- |
| Canvas | `MainWindow.axaml.cs` создаёт preview controls, обрабатывает pointer/drag/drop/resize и вызывает `RenderDesigner` | Нет | Извлечь `DesignerSurface` и surface interaction controller; передавать host services через interfaces | Высокий |
| Main state/orchestration | `MainWindowViewModel` владеет project state, selection, inspector, undo stacks, export generation, timers и workspace UI state | Нет | Разделить document session, selection, toolbox, inspector, export и workspace coordinators | Высокий |
| File commands | VM поднимает `ExternalEditorCommandRequested`; `MainWindow` сопоставляет его с `StorageProvider`, dialogs и direct paths | Частично | Формализовать этот существующий seam как `IDesignerHostServices` | Средний |
| Clipboard | Canvas/window получает `TopLevel.GetTopLevel(this).Clipboard` | Нет | `IDesignerClipboard`, реализованный standalone и IPC bridge | Низкий |
| Dialogs | Прямой `ShowDialog` для unsaved changes, recovery, settings, help и column editor | Нет | `IDesignerDialogService`; окна останутся в standalone/VsHost | Средний |
| Timers/threading | VM напрямую создаёт `Avalonia.Threading.DispatcherTimer` и post-ит в `Dispatcher.UIThread` | Нет | Scheduler abstraction или сохранение в Avalonia surface/session layer | Средний |
| Preview | `PreviewWindow` — Avalonia Window с generated-AXAML loader path | Нет | Preview presenter contract; фактический visual runtime остаётся в Avalonia host | Средний |
| Settings storage | Прямые `%LocalAppData%/FormDesigner` services | Нет | Разделить designer preferences от standalone и VS preferences | Низкий |
| Export | VM содержит generation orchestration и использует export services | Нет | Вынести request/snapshot construction в engine service; folder/zip/process UI оставить host | Высокий |

Существующее событие `ExternalEditorCommandRequested` является хорошим началом: оно доказывает, что file commands уже dispatch-ятся из VM в UI host. Его недостаточно для второго host, поскольку оно передаёт только `EditorCommandId`, а не document version, project context, consent или structured results.

## 4. Модель расширений Visual Studio: актуальные факты

В Visual Studio сейчас релевантны две модели.

1. **VisualStudio.Extensibility** современна, асинхронна и обычно выполняется out-of-process. Она предоставляет commands, document snapshots, text edits, Project Query, settings и tool windows. Microsoft документирует, что её Remote UI основан на WPF, создаётся в процессе Visual Studio, не имеет code-behind и не может ссылаться на собственные custom controls расширения. Следовательно, она не может host-ить существующий Avalonia Canvas, Eremex `DataGridControl`, custom PropertyGrid editors или plugin visuals. Она всё ещё полезна для lightweight commands, project/package queries и status UI.
2. **VSSDK / legacy editor APIs / MEF** остаются необходимыми для классического custom document designer с `IVsEditorFactory`, Running Document Table lifecycle, `IVsPersistDocData2`, file change coordination, native VS Toolbox interop и интеграции с VS Properties window. Совместимое с VSSDK in-process расширение для VS 2022 нацелено на .NET Framework 4.7.2. Эта граница framework не позволяет напрямую сослаться из него на текущий host Designer/Eremex с `net6.0`.

Следствия:

* Современное out-of-process расширение не может превратить текущий Avalonia UI в Remote UI control.
* Классический in-process document editor может дать наиболее нативную интеграцию tabs/split, но не должен загружать текущий Avalonia/Eremex/plugin stack в `devenv.exe` без отдельного compatibility и hosting proof.
* Небольшой in-process VSSDK bridge может сосуществовать с внешним Avalonia host `net6.0`. Bridge должен содержать только VS APIs и IPC, без Designer visual assemblies.

## 5. Avalonia внутри Visual Studio: анализ вариантов

### Вариант A: in-process Avalonia control в VS document/tool window

**Реализуемость:** не подтверждена и высокорискова для текущего codebase. Visual Studio tool windows host-ятся в WPF. Текущие Eremex controls требуют единой identity Avalonia 11.1.5, DeltaDesign resources и работающий Avalonia dispatcher. Текущая plugin model также загружает сторонние assemblies через `AssemblyLoadContext`.

**Преимущества:** наиболее близкий вид к нативной document tab `[AXAML] [Designer] [Split]`; возможны прямые keyboard и document commands.

**Риски:**

* in-process VSSDK extension для VS 2022 использует .NET Framework 4.7.2, тогда как Designer и Eremex host нацелены на `net6.0`;
* в этом репозитории нет проверенного supported host path для размещения Avalonia 11/Eremex surface внутри WPF VS document frame;
* assembly/theme conflicts и сбой plugin могут дестабилизировать `devenv.exe`;
* focus, OLE drag/drop, DPI, keyboard routing, high contrast и VS theme integration потребуют custom work;
* кроме shared Designer code понадобится custom editor factory/RDT persistence implementation.

**Рекомендация:** не выбирать для первого VS release. Выполнить отдельный disposable technical spike только после работы внешнего host.

### Вариант B: native child HWND или external editor, встроенный в VS frame

**Реализуемость:** возможна только как экспериментальный Win32/VS editor-hosting route, а не как подтверждённый путь переиспользования текущего приложения. Cross-process child HWND создаёт риски focus, modal dialog, DPI, accessibility, ownership, resize, lifetime и crash recovery. Нельзя опираться на `SetParent` как на production-архитектуру.

**Рекомендация:** отклонить для первого release. Считать это более поздним experiment, а не baseline для продуктового обещания.

### Вариант C: отдельный Avalonia Designer Host process плюс VSIX bridge

**Реализуемость:** **рекомендуемый и практичный вариант.** `AvaloniaDesigner.VsHost.exe` использует те же `DesignerSurface`, registry, plugin loader, Eremex controls, локальную тему DeltaDesign и AXAML preview, что и standalone. VSIX остаётся небольшим и только запускает/координирует host через versioned IPC protocol.

**Преимущества:**

* один фактический Avalonia Canvas/Toolbox/Property Inspector implementation;
* Eremex и third-party plugin crashes не могут напрямую завершить `devenv.exe`;
* нет двух версий Avalonia внутри одного process;
* будущий host может использовать ту же версию .NET/Avalonia, что и standalone;
* plugin discovery, license и theme behavior остаются идентичными уже протестированному standalone route;
* lazy-load только тогда, когда пользователь явно открывает Designer.

**Компромиссы:** это connected Designer window, а не полностью embedded document tab. Первый release должен говорить об этом прямо. Bridge обязан дать сильный workflow: context command, document ownership/version notification, Save integration, focus activation и diagnostics в VS Output.

### Вариант D: VisualStudio.Extensibility Remote UI/out-of-process UI

**Реализуемость как Canvas host:** нет. Remote UI основан на WPF и не может ссылаться на custom controls, что исключает Avalonia и Eremex visual trees.

**Подходящее применение:** lightweight Settings/Status tool window, commands, Project Query и document text operations. Позже он может дополнить VSSDK bridge, но не может заменить `VsHost.exe`.

### Матрица решения

| Критерий | A: in-process Avalonia | B: child HWND | C: external Avalonia host | D: Remote UI |
| --- | --- | --- | --- | --- |
| Переиспользует фактический Canvas | Потенциально, не подтверждено | Потенциально, хрупко | Да | Нет |
| Совместимость Eremex/plugins | Высокий риск | Высокий риск | Высокая | Нет visual support |
| Crash isolation от VS | Низкая | Средняя | Высокая | Высокая |
| Нативная VS document tab | Высокая после реализации | Средняя | Низкая | Средняя для WPF-only UI |
| Риск реализации | Очень высокий | Очень высокий | Средний | Низкий для bridge, невозможен для Canvas |
| Рекомендация для первого release | Нет | Нет | **Да** | Только bridge/status |

## 6. Рекомендуемая граница host и services

### 6.1. Host services

Следующий contract уместен, но он должен быть async, version-aware и возвращать structured results:

```csharp
public interface IDesignerHostServices
{
    Task<DesignerDocumentEnvelope> OpenDocumentAsync(DesignerDocumentReference reference, CancellationToken ct);
    Task<DesignerSaveResult> SaveDocumentAsync(DesignerSaveRequest request, CancellationToken ct);

    IDesignerClipboard Clipboard { get; }
    IDesignerDialogService Dialogs { get; }
    IDesignerNotificationService Notifications { get; }
    IDesignerFileSystem FileSystem { get; }
    IDesignerScheduler Scheduler { get; }
}
```

Для VS-connected host `SaveDocumentAsync` должен возвращать source version/checksum и набор text edits, а не записывать `.axaml` напрямую. VS bridge применяет edits к running document/text buffer, владеет dirty state и позволяет обычным VS Save/Save All сохранить файл. Direct host file writes допустимы только для files, которыми не владеет открытый VS document, и требуют явной policy.

### 6.2. IPC protocol

Использовать named pipe или local socket с versioned JSON-RPC-like envelope. Protocol должен проверять принадлежность пользователю/session и никогда не слушать network interface. Основные сообщения:

```text
Hello(protocolVersion, hostVersion, solutionId)
OpenDocument(path, text, version, projectSnapshot)
DocumentChanged(path, text, version, origin)
ApplyDesignerEdits(path, baseVersion, edits, sidecarChanges)
SaveRequested(path, baseVersion)
DocumentSaved(path, version)
ProjectChanged(projectSnapshot)
ShowDiagnostics(entries)
CloseDocument(path)
```

Host обязан отклонить patch, если `baseVersion` не совпадает. При conflict он показывает Reload / Keep Designer Changes / Compare; он никогда не должен молча перезаписывать source text.

## 7. Интеграция AXAML document и безопасность round-trip

### Текущее состояние

В репозитории нет AXAML importer. Поиск обнаружил `XDocument.Parse` только в `GeneratedAxamlService` для generated-AXAML preview transformation; `PreviewWindow` разбирает отдельные generated binding attributes. Текущий persistence path — JSON (`DesignerDocumentFileModel`, `WorkspaceModel`), а текущий generator создаёт AXAML из этой модели. Следовательно, generator не является безопасным writer для произвольного user-authored `.axaml` file.

### Необходимый design

Нужно добавить `AxamlDocumentAdapter` ниже обоих host:

```text
AXAML text + syntax/trivia
  -> AxamlSyntaxDocument (namespaces, comments, unknown nodes, spans)
  -> Import capability report
  -> DesignerDocument projection
  -> Designer operations
  -> minimal text edits against original AXAML
```

Adapter должен сохранять unknown attributes/elements, comments, namespace declarations, binding markup, resources, styles, `x:DataType`, custom controls и formatting во всех случаях, когда изменение не владеет соответствующим syntax. Обычный `XDocument` serializer недостаточен, потому что он теряет trivia и formatting.

### Поддерживаемые режимы сохранения

| Режим | Поведение первого release |
| --- | --- |
| Designer-owned view | Полное designer editing. Использовать companion `.formdesigner.json` mapping/metadata file и явную ownership marker/region policy; регенерировать только owned structure. |
| Imported simple AXAML | Поддержать документированный subset: Window/UserControl, Canvas/Grid/StackPanel, standard properties, declared bindings и зарегистрированные plugin controls. Записывать minimal source edits. |
| Unsupported/complex AXAML | Открывать read-only или partial-designer mode с видимым capability report. Не удалять unsupported syntax при Save. |
| External source edit, пока Designer dirty | Требовать reload/compare/merge. Никогда не выполнять auto-rewrite. |

Styles, resource dictionaries, custom templates, merged dictionaries, code-behind event handlers, attached properties и произвольные markup extensions нуждаются в отдельных import/round-trip tests. Eremex обрабатывается через descriptor namespaces и project package metadata, а не через hard-coded XML cases.

## 8. Editor UX в Visual Studio

### Первый release

* Context command и `Open With -> Avalonia UI Visual Designer` вызывают внешний host для выбранного `.axaml`.
* Обычный AXAML editor остаётся доступным и никогда не заменяется глобально.
* VSIX предоставляет небольшую status/output integration; фактические Toolbox, Canvas и Property Inspector являются shared Avalonia surface.
* Save в host отправляет text edits в VS; Ctrl+S/Save All остаются операциями Visual Studio.

### Нативный `[AXAML] [Designer] [Split]`

Это последующая функция, а не обещание первого release. Microsoft документирует, что source/designer multi-view требует отдельных document data и document view objects через legacy editor infrastructure. Ей также нужны Running Document Table, file-change и persistence handling. Настоящий embedded designer дополнительно зависит от решения проблемы Avalonia-in-WPF host. Нельзя строить его копированием существующего Canvas в WPF VS editor.

## 9. Toolbox и Property Inspector

### Toolbox

В первом release использовать существующий registry-backed Toolbox как часть `DesignerSurface`:

```text
Standard Avalonia
Eremex
User plugins
```

Он уже понимает descriptor categories, provider metadata и Eremex descriptors. Native Visual Studio Toolbox требует legacy `IVsToolboxUser`, OLE data objects и editor-specific registration. Его ранняя интеграция продублирует descriptor selection, drag/drop и plugin behavior; по сравнению с shared Toolbox это даёт мало продуктовой ценности.

### Property Inspector

Использовать текущий descriptor-aware Property Inspector в `DesignerSurface`. Он уже поддерживает enum editors, binding selectors, custom Eremex properties и filtering внутренней metadata. Native VS Properties window требует selection-container/COM integration и не сможет автоматически выразить существующие custom editors или plugin metadata. В будущем возможен read-only VS Properties adapter, но он должен потреблять те же property descriptors, а не заменять Property Inspector.

## 10. Eremex, plugins и dependencies

### Политика host

Все third-party visual assemblies загружаются в `AvaloniaDesigner.VsHost.exe`, но не в процесс VSIX/devenv. Это сохраняет смысл текущей plugin `AssemblyLoadContext` policy: Eremex controls должны использовать единую identity Avalonia assembly host, а Eremex dependencies остаются в plugin context. Scoped `DeltaDesignTheme` contribution остаётся прикреплённой только к Eremex preview subtree; она никогда не должна глобально изменять `Application.Current.Styles`.

### Обнаружение packages существующего проекта

Когда VS document открывается, bridge получает project snapshot: target framework, Avalonia version, package references, project directory, restore state и релевантные `App.axaml` styles. Host разрешает доступность Eremex/plugins на основе этого snapshot и trusted package/restore output location.

Если пользователь добавляет Eremex control, а package отсутствует:

1. показать точный package/version и предлагаемое theme change;
2. запросить явное consent;
3. дать VS bridge применить изменение `.csproj`/`App.axaml` через VS project/document APIs;
4. запросить restore и проверить compatibility;
5. в противном случае оставить Missing Control placeholder без потери descriptor properties.

Никогда не auto-load-ить произвольные DLL из открытого solution. Нужны allow-list/trust prompt, package identity/version validation и diagnostics. Не логировать licence keys или connection strings. Eremex licensing остаётся Bring Your Own Package / License.

## 11. NuGet, project system, settings и data sources

VisualStudio.Extensibility Project Query API может получать project files и NuGet references, а также изменять project properties. Поэтому он подходит для package proposal workflow bridge, хотя точный package-install UX нужно подтвердить в выбранной target version VS.

Settings должны быть разделены:

| Scope | Примеры |
| --- | --- |
| Shared Designer preferences | grid/snap, preview mode, Property Inspector, trusted plugin policy, Eremex behavior |
| Standalone host settings | window bounds, recent standalone workspaces, standalone export folder |
| VS integration settings | command/editor association, host launch behavior, VS Output routing, auto-reload policy |
| Project settings | form metadata, bindings, resources, dependencies, необходимые проекту с разрабатываемым UI |
| Secret/user settings | SQL credentials и персональные connection values; не коммитить по умолчанию |

Текущая JSON model включает `SourceConnectionString`; VS edition не должна автоматически копировать private connection strings в source-controlled AXAML или shared sidecars. Предпочтительны существующие global settings/secure-storage policy и явный project-level export choice.

## 12. Undo/Redo и document lifecycle

Текущий Designer snapshot undo/redo остаётся первоначальной history editor surface. В первом release не следует пытаться интегрироваться в global Undo Visual Studio. Каждое Designer edit всё ещё должно быть осмысленной document command и batch-ить pointer/property gestures, как это уже происходит.

Для будущего native custom editor VS может интегрироваться через `IOleUndoManager`/`IOleUndoUnit`; это VSSDK-specific work, которое должно адаптировать host-independent `IDesignerUndoHistory`, а не заменять его.

Lifecycle requirements для external route:

* VS владеет open document state, dirty flag, Save/Save All и external file notifications.
* Host работает из versioned snapshot и возвращает edits, а не прямые file writes.
* Solution close, document close, rename, reload и project unload явно уведомляют host.
* Один host process первоначально может обслуживать несколько documents, но у каждого есть изолированные `DesignerDocumentSession` и plugin/project context.
* Bridge не должен стартовать host или загружать plugins при обычном запуске VS; activation выполняется лениво по команде пользователя.

## 13. Производительность, threading и crash isolation

### Производительность

* Lazy-start `VsHost.exe` только после открытия пользователем Designer.
* Не загружать Eremex/plugin assemblies, пока они не нужны document.
* Первоначально использовать один host process на VS solution; освобождать document preview trees при закрытии tabs.
* Кешировать project/package snapshots с invalidation при изменениях `.csproj`, `packages.lock.json`, restore и `App.axaml`.
* Выполнять высоконагруженные preview/SQL операции в host, а не в VS UI thread.

### Threading

* VS bridge marshals все VSSDK/COM/document UI operations через требуемый механизм threading Visual Studio.
* IPC receive loops работают вне UI thread.
* Avalonia host владеет всеми visual mutations через свой Avalonia dispatcher.
* Ни один callback `MainWindowViewModel` не должен напрямую вызывать VS object; границу должны пересекать только host services/protocol messages.

### Crash isolation

Внешний host даёт наиболее безопасное поведение для Runtime AXAML loader, Eremex templates, DLL reflection и third-party plugin faults. Если он завершается, VS остаётся живой, показывает diagnostic с Restart Designer и сохраняет source text без изменений. Protocol должен сохранять неотправленные changes в локальном recovery record, но не применять их после restart без user review.

## 14. Карта необходимого рефакторинга и оценки повторного использования

Оценки основаны на текущих file boundaries и coupling, а не на выполненной migration.

| Область | Оценка переиспользуемого кода | Причина / цель извлечения |
| --- | ---: | --- |
| Models и JSON serialization | 85-90% | Перенести models и workspace serialization в Domain; отделить standalone-only session fields. |
| Plugin contracts и registry | 75-85% | Переиспользовать в Avalonia host; сторона VSIX использует только IPC contracts. |
| Built-in/Eremex descriptors | 80-90% | Уже descriptor-driven; сохранить Avalonia dependency ниже Surface/Engine. |
| Binding, SQL/DLL metadata | 70-80% | Заменить direct settings/path access на host services и security policy. |
| Diagnostics и logs | 80% | Добавить sinks для VS Output/Error List. |
| Export pipeline service | 70% | Переиспользовать pure build/artifact portions; вынести generation orchestration из VM. |
| AXAML generation | 65-75% | Переиспользовать generator для designer-owned views; добавить import/round-trip adapter для реального AXAML. |
| Undo/redo semantics | 50-60% | Извлечь snapshot/command history из VM и предоставить host-independent events. |
| `MainWindowViewModel` | 30-40% в текущем виде | 24k-line orchestrator смешивает editor domain, Avalonia dispatcher, workspace UI и export state. Разделить по ответственности. |
| Canvas + code-behind | 20-30% в текущем виде; 65-75% после извлечения `DesignerSurface` | Его behavior можно сохранить только внутри Avalonia host. |
| Toolbox/Inspector view models | 50-65% | Извлечь selection/session services и удалить предположения о `MainWindow`. |
| Standalone windows/dialogs | 10-20% | Сохранить как standalone host presentation. |
| Existing smoke tests | 55-65% | Core/export portions можно перенести; window-specific smoke останется host coverage. |

Core refactoring существенен: примерно **40-55% текущего orchestration/presentation layer**, но это не переписывание project models, descriptors, Eremex plugin или export services.

## 15. Предлагаемая будущая структура solution

```text
src/
  AvaloniaDesigner.Domain/             # models, document commands, serialization contracts
  AvaloniaDesigner.Engine/             # registry, plugin orchestration, bindings, diagnostics, export requests
  AvaloniaDesigner.PluginContracts/    # versioned descriptor and contribution contracts
  AvaloniaDesigner.AvaloniaSurface/    # Canvas, Toolbox, Inspector, DesignerDocumentSession VM
  AvaloniaDesigner.Export/             # AXAML/C# generation and build validation
  AvaloniaDesigner.AxamlRoundTrip/     # parser, capability report, source edit writer
  AvaloniaDesigner.Host.Protocol/      # versioned IPC DTOs, no Avalonia or VS refs

hosts/
  AvaloniaDesigner.Standalone/         # current app behavior and standalone dialogs/settings
  AvaloniaDesigner.VsHost/             # Avalonia process hosting the shared surface
  AvaloniaDesigner.VSIX/               # minimal VSSDK/VS bridge, commands, RDT/document/project bridge

plugins/
  AvaloniaDesigner.Plugins.Eremex/
  AvaloniaDesigner.Plugins.Demo/

tests/
  AvaloniaDesigner.Domain.Tests/
  AvaloniaDesigner.Export.Tests/
  AvaloniaDesigner.AxamlRoundTrip.Tests/
  AvaloniaDesigner.AvaloniaSurface.SmokeTests/
  AvaloniaDesigner.VsHost.Protocol.Tests/
  AvaloniaDesigner.VSIX.IntegrationTests/
```

Это target structure. Её нужно вводить vertical slices, сохраняя текущий standalone project рабочим host, пока каждый извлечённый service не покрыт tests.

## 16. Последовательность миграции

1. **Baseline и boundaries:** задокументировать текущие behaviors; добавить tests вокруг document session, export snapshot, plugin registry и command dispatch. Пока не создавать VSIX.
2. **Domain/engine extraction:** перенести models, workspace serialization, command history contracts, registry и diagnostics за interfaces. Текущий `MainWindow` продолжает их использовать.
3. **Surface extraction:** превратить Canvas + Toolbox + Inspector в Avalonia `DesignerSurface`, размещённый текущим MainWindow. Сохранить standalone UX и smoke coverage.
4. **Host services:** заменить предположения о `StorageProvider`, `TopLevel`, clipboard, dialogs, AppData paths и dispatcher на standalone implementations формальных interfaces.
5. **AXAML round-trip foundation:** реализовать parser, supported-subset capability report, source spans, minimal patch writer и conflict handling. Начать с controlled Button/TextBox fixture.
6. **VS bridge PoC:** создать изолированный VSIX project вне production solution path; запустить `VsHost.exe`, обменять document snapshot и применить versioned text patch через VS.
7. **Vertical integration:** selected `.axaml` -> command -> external designer -> source edit -> VS Save. Добавить project/package proposal flow.
8. **Hardening:** external edits, multi-document, plugin/Eremex discovery, SQL secret policy, recovery, shutdown, performance и accessibility.
9. **Только затем оценить native document tabs:** на основе Avalonia-in-VS spike решить, стоит ли независимого риска настоящий embedded `Designer`/`Split` tab.

## 17. Риски и меры снижения

| Риск | Влияние | Мера снижения |
| --- | --- | --- |
| Полная AXAML regeneration перезаписывает handwritten source | Критическое | Syntax-aware adapter, owned-region policy, capability report, versioned minimal patches и tests. |
| Plugin/Eremex crash дестабилизирует VS | Критическое для in-process route | Внешний Avalonia host; никогда не загружать visual plugins в VSIX. |
| Несовпадение версий Avalonia/Eremex | Высокое | Host разрешает одну совместимую runtime; package manifest и compatibility gate; использовать текущие Eremex diagnostics. |
| Недоверенная DLL из solution | Высокое | Явное trust, allow-list, без автоматического выполнения arbitrary DLL, isolation в host process. |
| Рассинхронизация source/designer edits | Высокое | Text versions/checksums, conflict UI, VS-owned Save lifecycle. |
| Несовпадение темы Visual Studio | Среднее | Сохранить VS chrome native; применять тему только внешнему host. Не применять Eremex theme глобально. |
| Native VS tab становится большой второй UI | Высокое | Отложить; не портировать Canvas в WPF. |
| SQL credentials попадают в source control/logs | Высокое | User-level secure settings, masking, explicit export policy, отсутствие default project persistence. |
| Регрессия startup/memory | Среднее | Lazy host/plugin loading, один host на solution, dispose preview trees. |

## 18. Оценка сложности

| Рабочая область | Сложность | Основная причина |
| --- | --- | --- |
| Извлечение Domain/Engine seams | Высокая | `MainWindowViewModel` сейчас объединяет несколько продуктовых подсистем. |
| Извлечение shared Avalonia surface | Высокая | Canvas input/selection/adorner behavior находится в большом code-behind. |
| External host + IPC bridge | Средняя | Ограниченный protocol и process lifecycle; нет переноса visual framework. |
| VS command/context menu/project discovery | Средняя | Работа с VSSDK project/document lifecycle. |
| AXAML import и safe round-trip | Очень высокая | Syntax preservation и compatibility scope определяют доверие пользователя. |
| Native VS custom editor/document tabs | Очень высокая | Legacy VSSDK/RDT + проблема WPF/Avalonia hosting. |
| Native VS Toolbox/Properties integration | Высокая | Legacy COM/OLE/selection integration и потеря текущих custom editors. |
| Eremex/plugin support через external host | Средняя | Переиспользует текущий adapter, но нуждается в trusted dependency resolution. |

## 19. Минимальный PoC: пока не реализовывать

Наиболее честный PoC — **не embedded control experiment**. Он проверяет critical path рекомендованной архитектуры:

```text
VSIX command on a known MainWindow.axaml fixture
  -> VS obtains current text/version and project metadata
  -> launches AvaloniaDesigner.VsHost.exe
  -> host imports supported Button/TextBox subset
  -> shared DesignerSurface displays the document
  -> change Button.Text and Width
  -> host returns versioned minimal AXAML text edits
  -> VS applies edits to its buffer and marks document dirty
  -> normal VS Save writes the file
```

Критерии приёмки PoC:

1. VS остаётся стабильной при завершении host.
2. Host использует тот же `DesignerSurface`/registry path, что и standalone, а не скопированный UI.
3. Вручную открытая AXAML text tab получает patch без потери несохранённого текста.
4. Добавления/изменения Button/TextBox проходят round-trip, а несвязанный comment и unknown attribute сохраняются.
5. Reload/conflict виден, когда source изменён извне.
6. Plugin discovery остаётся в host; VSIX не загружает Eremex/Avalonia visual assembly.
7. Запуск host ленивый, а повторные открытия используют тот же process.

Только после успеха этого PoC отдельный spike должен проверить, способен ли native VS document frame безопасно host-ить ту же Avalonia surface. Test обязан включать Eremex `TextEditor`, Eremex `DataGridControl`, DPI changes, keyboard focus, drag/drop, document split и принудительное plugin exception. Его результат не должен блокировать продуктовый путь external host.

## 20. Использованные официальные источники

* [VisualStudio.Extensibility overview](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility?view=visualstudio) — актуальные commands, documents, tool windows и Project Query surface.
* [Remote UI](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/inside-the-sdk/remote-ui?view=visualstudio) — ограничение WPF Remote UI: нет extension custom controls/code-behind.
* [VSSDK-compatible VisualStudio.Extensibility extensions](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions?view=visualstudio) — target framework in-process VS 2022 и hosting requirement.
* [Choosing an extensibility model](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models?view=visualstudio) — trade-offs isolation/runtime между VSSDK и новой моделью.
* [Creating custom editors and designers](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-custom-editors-and-designers?view=visualstudio) — custom editor, source/designer multi-view и decision points external editor.
* [Walkthrough: adding features to a custom editor](https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-adding-features-to-a-custom-editor?view=visualstudio) — editor factory, document persistence, Toolbox, Properties и Undo integration points.
* [Document windows](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/document-windows?view=visualstudio) — lifecycle editor factory и Running Document Table.
* [Project Query API](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/project/project?view=visualstudio) — возможность query и modification project files/package references/properties.
* [Saving a custom document](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/saving-a-custom-document?view=vs-2022) — обязанности dirty-state и save lifecycle.
* [Extension compatibility model for Visual Studio](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility?view=visualstudio) — совместимость VSIX между Visual Studio 2022/2026 через API-version.
