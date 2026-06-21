# Avalonia UI Visual Designer: техническая документация для разработчиков

Документ описывает текущую архитектуру проекта **Avalonia UI Visual Designer Alpha 0.2** по состоянию репозитория на момент написания. Цель документа - помочь новому разработчику быстро понять, где находится код, какие подсистемы за что отвечают, какие flows являются критичными и какие правила нельзя нарушать при доработке.

Термины `Avalonia`, `MVVM`, `ViewModel`, `Service`, `Model`, `DataGrid`, `Canvas`, `Preview`, `Export`, `Build`, `NuGet`, `DLL`, `Binding`, `AXAML`, `C#`, `ApplyDocument`, `PropertyGrid`, `Inspector` в документе оставлены без перевода. Имена классов, методов, свойств, файлов и папок также не переводятся.

## 1. Общий обзор проекта

Avalonia UI Visual Designer - визуальный конструктор Avalonia UI, который позволяет создавать проект с несколькими формами, размещать controls на дизайнерском Canvas, редактировать свойства через Property Inspector, настраивать DataGrid и data binding, импортировать metadata из DLL, задавать простую Logic между controls, просматривать результат в Preview и экспортировать готовый Avalonia project.

Alpha 0.2 - это рабочая, но активно развивающаяся версия. Внутри уже есть много подсистем, но часть архитектуры пока сконцентрирована в `MainWindowViewModel`, а часть UI-сценариев находится в `MainWindow.axaml.cs`. При доработках особенно важно не возвращать старые регрессии: Add Form, New Project, Property Inspector, Export isolation, DataGrid export и DLL import уже имеют защитные правила и diagnostics.

Основные подсистемы:

- **Project management** - создание, сохранение, загрузка workspace/project JSON.
- **Multi Form** - несколько `DesignerFormDocument`, активная форма, opened tabs, Project Explorer.
- **Designer Canvas** - визуальное размещение controls, drag/drop, resize, selection, wrappers/adorners.
- **Toolbox** - список доступных controls и drag/drop на Canvas.
- **Property Inspector / PropertyGrid** - редактирование свойств формы или выбранного control.
- **Project Explorer** - дерево форм, assets, export-секции.
- **Preview** - runtime-предпросмотр формы без мутации editor state.
- **Export pipeline** - генерация AXAML/C#/csproj/nuget.config/README и Validate Build.
- **Validate Build** - restore/build generated project и сбор structured diagnostics.
- **DataGrid Designer** - schema, columns, preview rows, sorting/grouping/wrapping/resizing settings.
- **DLL Import** - metadata extraction из assemblies, LINQ to SQL tables/columns, search index.
- **Data mode** - просмотр source/schema/sample data для selected DataGrid.
- **Column Editor** - редактирование колонок DataGrid через working copy и Apply/Cancel.
- **Logic Editor** - события/actions, включая DataGrid selected row -> TextBox/TextBlock.
- **Settings** - настройки preview/build/logs/artifacts/layout feature flags.
- **Logs/Diagnostics** - видимая diagnostic trace panel и structured logs.

Общий flow:

```text
User action
 -> Command or event handler
 -> MainWindowViewModel / Service method
 -> Model update
 -> Canvas/Inspector/Data/Export refresh, if allowed
 -> Diagnostics trace/log entry
```

## 2. Архитектура приложения

Проект построен вокруг MVVM, но исторически `MainWindowViewModel` стал центральным orchestration layer. Он управляет project state, active form, selection, PropertyGrid, export generation, DataGrid/DataSource state, Logic Editor, diagnostics и settings bindings. `MainWindow.axaml.cs` содержит существенную часть real UI behavior: canvas rendering, pointer handling, drag/drop, dialogs, preview opening, clipboard, file dialogs и некоторые визуальные repair-операции.

Ключевой принцип:

```text
Persisted project state
 != Editor transient state
 != Preview runtime state
 != Export generated state
```

Source of truth для пользовательского проекта - `WorkspaceModel.Project` и формы внутри `DesignerProjectModel.Forms`. UI не должен становиться отдельной "правдой". Если данные отображаются в Canvas, Inspector, Data mode или Export panel, они должны быть производными от model/snapshot, а не независимым состоянием, которое потом случайно перезаписывает model.

Критическое разделение:

- **Editor state**: `ActiveFormDocument`, selected control, opened tabs, Canvas wrappers, PropertyRows, inspector target, current editing flags.
- **Preview state**: изолированный runtime visual tree, который должен соответствовать generated AXAML, но не должен менять editor state.
- **Export state**: generated files, build logs, artifacts, diagnostics. Export читает project model/snapshot и не трогает editor UI.

Правило: Preview/Export/Validate Build не должны вызывать editor-only операции вроде `ApplyDocument`, `SetActiveForm`, `ClearSelection`, `RenderCanvas`, `RefreshSimpleInspector`, `RebuildPropertyGrid`, если это меняет текущий editor surface.

## 3. Структура проекта и папок

Основные реальные папки solution:

### `Views`

Содержит Avalonia views/windows.

Ключевые файлы:

- `Views/MainWindow.axaml` - основной layout приложения.
- `Views/MainWindow.axaml.cs` - code-behind главного окна: canvas rendering, drag/drop, pointer events, dialogs, preview opening, build log buttons, clipboard.
- `Views/PreviewWindow.axaml` и `Views/PreviewWindow.axaml.cs` - runtime preview window.
- `Views/DataGridColumnEditorWindow.axaml` и `.cs` - Column Editor для DataGrid.
- `Views/HelpWindow.*`, `BackupRestoreWindow.*`, `RecoveryWindow.*`, `RecentFileUnavailableWindow.*`, `UnsavedChangesWindow.*` - вспомогательные dialogs/windows.

Новый UI-код добавлять сюда, если он является View/window/control interaction. Логику model/service лучше не держать в code-behind, кроме прямых UI event handlers.

### `ViewModels`

Содержит ViewModel layer.

Ключевые файлы:

- `ViewModels/MainWindowViewModel.cs` - центральный orchestrator проекта.
- `ViewModels/PropertyGridRowViewModel.cs` - строка PropertyGrid.
- `ViewModels/PropertyGridCategoryViewModel.cs` - категория PropertyGrid.
- `ViewModels/PropertyGridOptionViewModel.cs` - option item для enum/dropdown editors.
- `ViewModels/DescriptorPropertyEditorViewModel.cs` - descriptor-based property editor.
- `ViewModels/LayoutDefinitionEditorItem.cs` - вспомогательный item для layout definitions.
- `ViewModels/ViewModelBase.cs` - базовый observable ViewModel.

### `Models`

Persisted и transient models проекта.

Ключевые файлы:

- `Models/WorkspaceModel.cs` - workspace root.
- `Models/DesignerProjectModel.cs` - project root.
- `Models/DesignerFormDocument.cs` - form-level document/session wrapper.
- `Models/DesignerDocumentFileModel.cs` - persisted form document.
- `Models/DesignControlModel.cs` - model одного visual control.
- `Models/BindingSourceModel.cs`, `BindingFieldModel.cs` - data source/schema/column metadata.
- `Models/ImportedDllInfoModel.cs`, `ImportedDllTypeInfoModel.cs`, `ImportedDllTableInfoModel.cs`, `ImportedDllColumnInfoModel.cs` - DLL metadata DTO.
- `Models/InteractionModel.cs` - Logic actions/events.
- `Models/AppSettingsModel.cs` - app settings.
- `Models/GeneratedFileModel.cs`, `ExportValidationResult.cs`, `ExportedProjectFile.cs` - export/build results.

Важно: в коде не найдено отдельных классов с именами `DataGridModel`, `DataGridColumnModel`, `DataSourceModel`, `LogicActionModel`, `SettingsModel`. Их роли сейчас выполняют реальные классы:

- `DataGridModel` -> `DesignControlModel` с `Type == "DataGrid"` и DataGrid-specific properties.
- `DataGridColumnModel` -> чаще всего `BindingFieldModel`.
- `DataSourceModel` -> `BindingSourceModel`.
- `LogicActionModel` -> `InteractionModel`.
- `SettingsModel` -> `AppSettingsModel` и вложенные settings records/classes.

### `Services`

Содержит application services и helper services.

Ключевые файлы:

- `AppSettingsService.cs` - load/save settings.
- `ProjectWorkspaceService.cs` - save/load workspace JSON.
- `ProjectDocumentService.cs` - clone/import document snapshots.
- `DocumentDiagnosticsService.cs` - validation diagnostics для documents/controls/properties.
- `ExportPipelineService.cs` - workspace export, Validate Build, NuGet config, README, build log aggregation.
- `ExportWorkspaceService.cs` - export workspace/artifacts helpers.
- `ArtifactCleanupService.cs` - cleanup policy для artifacts.
- `PreviewRuntimeService.cs` - runtime preview interactions.
- `ProjectAssetService.cs`, `ProjectResourceService.cs` - assets/resources.
- `ReusableTemplateCatalog.cs`, `ReusableTemplateStorageService.cs` - reusable templates.
- `DocumentBackupService.cs`, `AutosaveRecoveryService.cs` - backups/autosave/recovery.
- `WorkspaceLogService.cs`, `WorkspaceNotificationService.cs`, `WorkspaceTaskService.cs` - logs/notifications/tasks.

### `DesignerSystem`

Designer infrastructure: descriptors, registry, plugin loading, binding metadata, preview data.

Ключевые области:

- `DesignerSystem/BuiltInControlRegistrar.cs` - built-in control descriptors and property descriptors.
- `DesignerSystem/DesignerRegistry.cs` - registry of descriptors.
- `DesignerSystem/DescriptorContexts.cs` - descriptor context classes.
- `DesignerSystem/DesignControlNodeAdapters.cs` - adapter layer between model and descriptor nodes.
- `DesignerSystem/PluginLoading/*` - plugin loading infrastructure.
- `DesignerSystem/Binding/DataSourceIdentity.cs` - stable identity/key generation for data sources.
- `DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs` - reflection-based metadata.
- `DesignerSystem/Binding/SqlPreviewDataLoader.cs` - SQL preview data.
- `DesignerSystem/Binding/BindingPreviewItemsBuilder.cs` - preview items.

### `Controls`

Custom controls/visual helpers. Добавлять сюда reusable UI controls, если они не являются отдельным window/view.

### `Styles`

Avalonia styles/themes. UI polish и shared visual resources лучше добавлять сюда, если это стиль, а не behavior.

### `Localization`

Централизованные UI strings/localization helpers. Частичная русификация должна идти через этот слой, а не хаотично через hardcoded strings.

### `PluginContracts` и `Plugins`

Contracts and plugin-related code. Использовать для расширяемости, но не смешивать plugin-specific code с core editor state.

### `EditorCommands`

Editor command abstractions/helpers.

### `Assets`

Static assets приложения.

### `templates`

Templates для generation/export или reusable content.

### `smoke-tests`

Smoke/regression tests. Это важная часть процесса разработки: многие regression scenarios уже были найдены через ручные и smoke checks.

### `Docs`

Документация проекта. Этот файл находится здесь. В репозитории также есть `ALPHA_0_2_MANUAL_TEST_CHECKLIST.md`, `PluginGuide.md`, `UndoRedoSmokeTest.md`.

### Не найдено как отдельные папки

На момент написания не найдены отдельные top-level папки `Converters`, `Helpers`, `Resources`. Если они появятся, документацию нужно обновить. Сейчас converters/helpers/resources распределены по существующим папкам или отсутствуют как отдельные директории.

## 4. MainWindowViewModel

`MainWindowViewModel` - главный orchestration layer приложения. Он слишком большой и отвечает за несколько подсистем сразу. Это технический долг, но на текущем этапе важно понимать его границы и side effects.

Он отвечает за:

- current workspace/project;
- active form and multi-form state;
- opened form tabs;
- project explorer items;
- selected control;
- inspector target and PropertyRows;
- Simple Inspector / PropertyGrid editing;
- canvas-related model operations;
- command set для New/Open/Save/Add Form/Delete/Rename;
- export generation and validate build orchestration;
- DataGrid source/schema/column state;
- DLL import metadata and source detach;
- Logic actions/editor state;
- diagnostics trace entries;
- settings bindings and feature flags.

Ключевые коллекции и state:

- `CurrentWorkspace` / `CurrentProject` - project source of truth.
- `Forms` внутри `CurrentProject.Forms` - persisted list of forms.
- `OpenedFormTabs` - открытые tabs editor session.
- `ActiveFormDocument` / active document id - текущая форма.
- `SelectedControl` - выбранный `DesignControlModel`.
- `PropertyRows`, category collections/maps - текущие rows PropertyGrid.
- `CurrentInspectorDocumentId`, `CurrentInspectorControlId` - target inspector.
- `ImportedDlls`, binding sources, schema/search/preview caches - data binding state.
- `GeneratedFiles`, build/validation output, export diagnostics - export state.
- flags: `_isApplyingDocument`, `_isExportPipelineRunning`, `_isPreviewGenerationRunning`, property editing flags, drag/resize flags.

### MainWindowViewModel: важные методы

Ниже перечислены реальные методы, найденные в `ViewModels/MainWindowViewModel.cs`. Номера строк могут меняться, поэтому искать лучше по имени метода.

#### `SetActiveForm(string? documentId, string reason, bool persistCurrent = true)`

Назначение: переключает активную форму.

Когда вызывается: при открытии формы из Project Explorer/tab, Add Form, New Project, load workspace, open form document.

Основные действия:

- optionally вызывает `SaveActiveFormState`;
- вызывает `LoadActiveFormState`;
- синхронизирует active tab/form;
- rebuild Project Explorer/Inspector as needed;
- emits diagnostics with reason.

Side effects:

- может менять `ActiveFormDocument`;
- может менять selection;
- может rebuild PropertyGrid;
- может trigger canvas render через view binding/event.

Что нельзя делать внутри:

- нельзя запускать export/build;
- нельзя вызывать при обычном property edit;
- нельзя destructive refresh во время active inspector text edit.

#### `CreateNewForm()`

Назначение: создает новую `DesignerFormDocument`.

Flow:

```text
SaveActiveFormState("CreateNewForm")
 -> create DesignerFormDocument with new id/name
 -> add to CurrentProject.Forms
 -> add/open tab
 -> LoadActiveFormState(newForm.Id, "CreateNewForm")
 -> RebuildProjectExplorer
```

Правила:

- новая форма должна получать новый unique id;
- нельзя переиспользовать ids старых форм;
- Add Form не должен сбрасывать property edit на другой форме;
- после Add Form inspector должен соответствовать active form.

#### `SaveActiveFormState(string reason)`

Назначение: сохраняет текущий live editor state активной формы в snapshot внутри `DesignerFormDocument`.

Сохраняет:

- document file model;
- selected control id;
- zoom/viewport;
- dirty/session state.

Риски:

- если вызвать во время property edit или stale snapshot, можно записать старые значения поверх live model;
- нельзя использовать как универсальный refresh method.

Diagnostics: `SaveActiveFormState` trace with reason.

#### `LoadActiveFormState(string? documentId, string reason)`

Назначение: загружает form snapshot/document в active editor surface.

Обычно вызывает `ApplyDocument`.

Риски:

- это editor operation, не export operation;
- нельзя использовать внутри generation/build;
- должен сохранять/восстанавливать selection только если selected control реально существует в target document.

Diagnostics: `LoadActiveFormState`.

#### `ApplyDocument(DesignerDocumentFileModel document, string sourcePath, bool markAsSaved, bool resetDocumentSession, string caller = ...)`

Назначение: применяет `DesignerDocumentFileModel` к текущему editor surface.

Это **editor-only** метод.

Side effects:

- обновляет active document surface;
- может rebuild Canvas;
- может rebuild Inspector;
- может менять selection;
- может менять document/session state.

Критические правила:

- `ApplyDocument` нельзя вызывать из Export/Build/Validate.
- `ApplyDocument` нельзя вызывать при обычном property edit.
- `ApplyDocument` нельзя вызывать при typing в PropertyGrid/Simple Inspector/Logic Template.
- Если `_isExportPipelineRunning == true`, editor state mutation должна логироваться и блокироваться.
- Если выбранный control существует в новом document, selection нужно preserve.
- Если selected control тот же (`Id` и document id совпадают), нельзя запускать same-selection rebuild loop.

Diagnostics:

- `APPLY_DOCUMENT_START`
- `APPLY_DOCUMENT_END`
- `EXPORT_PIPELINE_APPLY_DOCUMENT_CALL_DETECTED`
- `EDITOR_STATE_MUTATION_DURING_EXPORT_BLOCKED`

#### `ResetApplicationStateForNewProject(...)`

Назначение: полный reset runtime/editor state перед созданием нового проекта.

Должен очищать:

- forms/project state;
- active document/current document;
- opened form tabs;
- Project Explorer items;
- selected control and selection history;
- canvas wrappers/adorners/transient state;
- PropertyRows and inspector target;
- Data/DLL/source/schema/preview/search caches;
- Logic editor transient state;
- export/build generated state;
- undo/redo/history;
- autosave/backup references where relevant.

После reset New Project создает новый `WorkspaceModel`, новый `DesignerProjectModel` и ровно одну новую пустую `Form1` с новым id.

Diagnostics:

- `NEW_PROJECT_START`
- `NEW_PROJECT_CLEAR_FORMS`
- `NEW_PROJECT_CLEAR_PROJECT_EXPLORER`
- `NEW_PROJECT_CLEAR_DOCUMENT_SNAPSHOTS`
- `NEW_PROJECT_CLEAR_OPENED_TABS`
- `NEW_PROJECT_CREATE_NEW_FORM`
- `NEW_PROJECT_END`
- `NEW_PROJECT_LEAK_CHECK`
- `NEW_PROJECT_LEAKED_OLD_FORM_STATE`

#### `AssertCleanNewProjectState(...)`

Debug/smoke guard после New Project.

Проверяет:

- ровно одна новая Form1;
- нет старых form ids/control ids;
- canvas empty;
- selected control null;
- inspector empty/new target;
- opened tabs не содержат старые формы;
- caches cleaned.

#### `SelectSingleControl(DesignControlModel? control)` и `ClearSelection()`

Назначение: selection management.

Правила:

- selection должен ссылаться на control из `ActiveFormDocument.Controls`, а не stale instance;
- same selection (`old.Id == new.Id` и тот же document) не должен вызывать `SelectedControlChanged`;
- `ClearSelection` нельзя вызывать из pointer events, пришедших из Inspector;
- во время `_isApplyingDocument` нельзя очищать selection из-за временного `Controls.Clear()`.

Diagnostics:

- `SelectedControlChanging`
- `SELECTED_CONTROL_SAME_SUPPRESSED`
- `ClearSelection`

#### `RebuildProjectExplorer()`

Назначение: пересобрать tree Project Explorer из `CurrentProject`.

Правила:

- Project Explorer должен быть производным от current project model;
- после New Project он должен показывать `Forms (1)`;
- нельзя оставлять старые form nodes;
- rename form должен обновлять Explorer, tab header и Inspector.

#### `RebuildPropertyGrid(string reason = "General")`

Назначение: пересобрать rows PropertyGrid для текущего inspector target.

Flow:

```text
Resolve target (selected control or active form)
 -> clear/replace categories and rows
 -> BuildPropertyGridRows()
 -> remove duplicate rows
 -> EnsurePropertiesTabContent(reason)
 -> diagnostics
```

Правила:

- метод должен быть idempotent;
- перед rebuild rows нужно очищать старые rows, нельзя append поверх старых;
- один target + одно property name = одна row;
- stale inspector target должен быть detected/blocked;
- во время active text edit destructive refresh должен suppress/queue;
- property edit не должен вызывать full `ApplyDocument`.

Diagnostics:

- `RebuildPropertyGrid`
- `PROPERTY_ROWS_REBUILT`
- `PROPERTY_GRID_DUPLICATE_ROW_DETECTED`
- `PROPERTIES_TAB_REFRESH_REQUESTED`
- `PROPERTIES_TAB_CONTENT_REBUILT`
- `PROPERTIES_TAB_EMPTY_AFTER_REBUILD`

#### `BuildPropertyGridRows()`

Назначение: формирует список properties для active form или selected control.

Важно:

- layout properties (`X`, `Y`, `Canvas.Left`, `Canvas.Top`, `Margin`, `Padding`, alignments) доступны в Properties;
- отдельная Layout tab может быть скрыта feature flag;
- properties должны использовать реальные technical names, не русифицированные labels.

#### `EnsurePropertiesTabContent(string reason)`

Назначение: repair active Properties tab, если rows построены, но visual content остался пустым.

Этот метод нужен из-за прошлой регрессии: после Add Form/select control `PropertyRows` уже содержали rows, но Properties tab оставалась визуально пустой до переключения вкладок.

Правило: repair должен обновлять только Properties content, не вызывать `ApplyDocument`, не менять selection и не сбрасывать edit state.

#### `CommitPropertyGridTextEdit(...)`, `CommitPropertyGridEdit(...)`, `EndPropertyGridTextEdit(...)`

Назначение: commit changes from Inspector text editors.

Правила:

- TextBox local text должен применяться к actual target по id в active document;
- stale target edit должен blocked;
- empty string для Button `Text/Content` валиден;
- после commit/cancel flags `isEditingProperty` и `isInspectorInteractionActive` должны сбрасываться;
- edit не должен вызывать `ApplyDocument`.

Diagnostics:

- `PROPERTY_EDIT_BEGIN`
- `PROPERTY_EDIT_COMMIT`
- `PROPERTY_EDIT_CANCEL`
- `PROPERTY_EDIT_BLOCKED_STALE_INSPECTOR_TARGET`
- `TEXT_PROPERTY_EMPTY_VALUE_APPLIED`
- `TEXT_PROPERTY_DEFAULT_FALLBACK_SUPPRESSED`

#### `GenerateXaml()`

Назначение: сгенерировать current AXAML/C#/binding guide для export panel.

Side effects:

- обновляет generated text fields;
- обновляет diagnostics/checklist/export cache;
- не должен менять editor active form/selection/inspector.

#### `RefreshExportPipelineResult()`

Назначение: обновить export pipeline result in UI.

Должен:

- take editor state snapshot before;
- build generated files from model;
- compare state after;
- log mutation if any.

Не должен:

- вызывать `ApplyDocument`;
- менять current editor surface.

Diagnostics:

- `EXPORT_PIPELINE_START`
- `EXPORT_EDITOR_STATE_SNAPSHOT_BEFORE`
- `EXPORT_EDITOR_STATE_SNAPSHOT_AFTER`
- `EXPORT_MUTATED_EDITOR_STATE`
- `EXPORT_PIPELINE_END`

#### `BuildGeneratedFiles()`

Назначение: build list of generated files for export/validate.

Генерирует:

- main window/form AXAML;
- C# code-behind/ViewModel;
- secondary forms;
- csproj;
- nuget.config;
- README.generated.md, если включено/нужно;
- export diagnostics files.

Правило: pure/read-only relative to editor state.

#### `BuildSecondaryFormGeneratedFiles(DesignerFormDocument form, string className)`

Назначение: generation для secondary forms.

Ранее опасная зона: secondary export вызывал `ApplyDocument` и ломал editor state. Текущий правильный подход: использовать isolated export context/view model or pure generation, не применять form к текущему editor surface.

Правило: никакого `ApplyDocument` на active `MainWindowViewModel`.

Diagnostics:

- `BUILD_SECONDARY_FORM_GENERATION_PURE`
- `EXPORT_PIPELINE_APPLY_DOCUMENT_CALL_DETECTED`, если правило нарушено.

#### `AppendDataGridXaml(...)`

Назначение: generation of DataGrid AXAML.

Правила:

- генерировать `<DataGridTextColumn>`, `<DataGridCheckBoxColumn>` или `<DataGridTemplateColumn>`, но не `<dataGrid:DataGridTextColumn>`;
- не генерировать `{Binding }`;
- не генерировать `Path=""`;
- `SortMemberPath` должен соответствовать binding path;
- wrapping/trimming может требовать `DataGridTemplateColumn`;
- scroll/sort/resize settings должны совпадать в preview/export.

#### `BuildGeneratedCSharp()`

Назначение: generation of C# code including ViewModel-like data and logic handlers.

Отвечает за:

- row DTOs / sample collections для DataGrid;
- interaction handlers;
- DataGrid selection logic;
- code-behind wiring.

#### `ImportBindingSourcesFromAssembly(string assemblyPath)`

Назначение: import DLL metadata and create binding sources.

Должен:

- detect LINQ to SQL `TableAttribute`/`ColumnAttribute`;
- build stable DataSourceKey;
- populate `ImportedDllInfoModel` and binding sources;
- not load real table data automatically;
- not freeze UI;
- log errors/partial success.

Diagnostics:

- `DLL_IMPORT_START`
- `DLL_IMPORT_END`
- `DLL_IMPORT_FAILED`
- `LINQ_TO_SQL_TABLE_DETECTED`
- `LINQ_TO_SQL_COLUMN_DETECTED`

#### `RemoveDll(ImportedDllInfoModel? dll)` и `ReloadDll(ImportedDllInfoModel? dll)`

Назначение: remove/reload imported DLL metadata.

Remove должен:

- remove DLL from UI list;
- clear metadata/schema/preview/search caches;
- detach DataGrid binding sources;
- leave generated columns as manual if source is removed;
- mark source missing/detached, not crash.

Diagnostics:

- `DLL_REMOVE_REQUESTED`
- `DLL_REMOVE_STARTED`
- `DLL_REMOVE_DATAGRID_SOURCE_DETACHED`
- `DLL_REMOVE_CACHE_CLEARED`
- `DLL_REMOVE_COMPLETED`

#### `AddInteraction()` и Logic template methods

Назначение: управление `InteractionModel`.

Logic editor должен работать через working copy/draft для template text. Typing не должен запускать heavy operations.

Diagnostics:

- `LOGIC_TEMPLATE_EDIT_BEGIN`
- `LOGIC_TEMPLATE_TEXT_CHANGED`
- `LOGIC_TEMPLATE_PREVIEW_UPDATED`
- `LOGIC_TEMPLATE_VALIDATION_UPDATED`
- `LOGIC_TEMPLATE_EDIT_APPLY`
- `LOGIC_TEMPLATE_EDIT_CANCEL`

## 5. Project model / Form model / Control model

### `WorkspaceModel`

Root workspace JSON model.

Основные поля:

- `Version`
- `WorkspaceId`
- `Project`
- `Session`

Persisted state: весь workspace file. Transient editor state не должен попадать сюда, если он не нужен для восстановления workspace session.

### `DesignerProjectModel`

Project root.

Основные поля:

- `Id`
- `Name`
- `RootPath`
- `DefaultNamespace`
- `TargetFramework`
- `AvaloniaVersion`
- `Forms`
- `ViewModels`
- `Assets`
- `Resources`
- `ExportProfiles`
- `Settings`

`Forms` - основной source of truth для multi-form project. New Project обязан заменить проект новым экземпляром и не оставлять старые forms/tabs/snapshots.

### `DesignerFormDocument`

Wrapper/session model для одной формы.

Содержит:

- `Id`, `Name`, display/tab state;
- `Document` / current file model;
- `IsDirty`;
- `CurrentSnapshot`, `SavedSnapshot`;
- undo/redo snapshots;
- `Zoom`, viewport offsets;
- `SelectedControlId`.

Persisted: form document content and project-level metadata. Transient/editor-only: zoom/viewport/selected id may be session state, not runtime export state.

### `DesignerDocumentFileModel`

Persisted file model формы.

Содержит:

- form title/name/size/theme/layout;
- list of `DesignControlModel`;
- binding sources;
- interactions;
- resources/settings relevant to form.

Это модель, из которой строятся Canvas, Preview и Export.

### `DesignControlModel`

Model одного control.

Основные поля:

- identity: `Id`, `Type`, `Name`, `ParentId`;
- content: `Text`, `PlaceholderText`, `ImageSource`;
- appearance: `Background`, `Foreground`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Opacity`, `FontFamily`, `FontSize`, `FontWeight`;
- layout: `X`, `Y`, `Width`, `Height`, `Margin`, `Padding`, alignments, min/max values, z-order/layout fields;
- state: `IsVisible`, `IsLocked`;
- DataGrid-related fields: binding source id, auto-generate columns, scroll/sort/resize/group settings, display settings;
- descriptor/custom property bag.

Важно:

- default `Text` для new Button можно задать при создании control;
- после явного user edit empty string должен оставаться empty string;
- opacity применяется к inner rendered control, а designer outline/adorner должен оставаться visible.

### `BindingSourceModel`

Реальный аналог requested `DataSourceModel`.

Содержит:

- `Id`
- `Name`
- `Path`
- `ItemTypeName`
- `Description`
- `SourceKind`
- source-specific fields: assembly path/type/table, connection string/schema/query;
- `Fields` collection of `BindingFieldModel`.

Используется для SQL source, DLL source and manual/sample data source. Не должен идентифицироваться только по display name.

### `BindingFieldModel`

Реальный аналог requested `DataGridColumnModel`.

Содержит:

- `Header`
- `Path`
- `SampleValue`
- `TypeName`
- `DbType`
- primary/nullability/read/write flags;
- visibility/sort/filter/resize flags;
- width/min/max;
- wrapping/trimming/max lines;
- alignment/format/null text;
- order/group/sort state.

Column Editor работает с working copy `BindingFieldModel`, Apply переносит изменения в model.

### `ImportedDllInfoModel` и related DLL metadata models

DTO metadata after DLL import:

- `ImportedDllInfoModel` - DLL-level summary/status/counts/errors.
- `ImportedDllTypeInfoModel` - type metadata.
- `ImportedDllTableInfoModel` - table/source metadata.
- `ImportedDllColumnInfoModel` - column metadata.

Эти models должны хранить compact metadata, а не тяжелые reflection objects.

### `InteractionModel`

Реальный аналог requested `LogicActionModel`.

Содержит:

- source control name/event;
- action type;
- target control/property;
- text template/source path;
- missing/no-selection behavior;
- open form options;
- message/action metadata.

Используется как persisted logic model и source for generated code.

### `AppSettingsModel`

Application settings.

Основные области:

- Canvas settings.
- PropertyGrid settings.
- Recent files/session/shell layout.
- Export cache/artifacts.
- Preview settings: runtime badge, experimental Layout tab.
- Build/logs settings: validate after export, verbose logs, log file policy.
- Autosave/recovery.

Settings are app-level, not project model, unless a setting is explicitly project-specific.

## 6. Multi Form

Multi Form основан на `DesignerProjectModel.Forms`, где каждый элемент - `DesignerFormDocument`.

Основные concepts:

- **Forms collection** - source of truth.
- **Active form** - текущая форма на editor surface.
- **OpenedFormTabs** - UI session list of open forms.
- **Project Explorer** - tree generated from current project forms/assets/export sections.
- **Selection** - selected control belongs to active form.
- **Snapshots** - сохраненное состояние формы при переключениях.

Typical form switch flow:

```text
User selects form in Explorer/tab
 -> SetActiveForm(formId, reason)
 -> SaveActiveFormState(current)
 -> LoadActiveFormState(target)
 -> ApplyDocument(target.Document)
 -> RebuildProjectExplorer
 -> RebuildPropertyGrid for active form/selected control
 -> RenderCanvas
```

Add Form flow:

```text
CreateNewForm()
 -> SaveActiveFormState("CreateNewForm")
 -> new DesignerFormDocument(new id, "FormN")
 -> CurrentProject.Forms.Add(form)
 -> OpenedFormTabs.Add(form)
 -> LoadActiveFormState(form.Id, "CreateNewForm")
 -> RebuildProjectExplorer
```

Delete Form rules:

- remove form from `CurrentProject.Forms`;
- remove opened tab;
- clear selection if selected control belonged to deleted form;
- clear snapshots/caches for deleted form;
- activate another form or create new if needed;
- rebuild Explorer and Inspector.

Rename Form rules:

- update `DesignerFormDocument.Name`;
- update underlying document title/name;
- update Project Explorer node;
- update tab header;
- update Inspector rows without duplicates;
- no stale rows from old form.

Known past bugs and rules:

- Properties of old form were shown after switch. Guard: inspector target document id must match `ActiveFormDocument.Id`.
- Add Form caused `SelectedControl` to become null or same selection rebuild loops. Guard: preserve selection by id and suppress same-selection updates.
- `ApplyDocument` cleared state during temporary controls clear. Guard: `_isApplyingDocument` blocks transient null selection refresh.
- New Project left old forms in Explorer/canvas. Guard: full reset and leak check.

## 7. Property Inspector

Property Inspector displays editable properties for either selected control or active form.

Target resolution:

```text
if SelectedControl != null and belongs to ActiveFormDocument:
    target = selected control
else:
    target = active form
```

Important state:

- `SelectedControl`
- `CurrentInspectorDocumentId`
- `CurrentInspectorControlId`
- `PropertyRows`
- `PropertyGridCategoryViewModel`
- property edit flags (`isEditingProperty`, inspector interaction flags)

Stale target prevention:

- property edit checks active document id;
- property edit checks selected/inspector control id;
- if mismatch, edit is blocked and diagnostic is written.

PropertyRows lifecycle:

```text
RebuildPropertyGrid(reason)
 -> resolve target
 -> clear previous rows/categories
 -> BuildPropertyGridRows()
 -> RemoveDuplicatePropertyGridRows()
 -> update inspector document/control ids
 -> EnsurePropertiesTabContent(reason)
```

Important rules:

- `RebuildPropertyGrid` must be idempotent.
- Do not append rows over old rows.
- Do not show stale properties from previous form.
- During text edit, do not destructive refresh.
- Property edit must not call `ApplyDocument`.
- Empty `Text/Content` is valid and must not fall back to `"Кнопка"`.
- Width/Height commit/cancel must reset edit flags; numeric validation must not lock whole inspector.

Color properties:

- `Background`, `Foreground`, `BorderBrush` are model properties.
- Color apply flow:

```text
Color button click
 -> COLOR_DIALOG_OPENED
 -> user selects color
 -> COLOR_DIALOG_APPLY_ATTEMPT
 -> update selected control model property
 -> invalidate/update preview/canvas visual
 -> update inspector value
 -> export uses same model property
```

Foreground-specific rule: ensure renderer and exporter apply `Foreground` for Button/TextBox/TextBlock, not only Inspector label.

Diagnostics commonly used:

- `INSPECTOR_TARGET_CHANGED`
- `INSPECTOR_TARGET_STALE_DETECTED`
- `PROPERTY_EDIT_BLOCKED_STALE_INSPECTOR_TARGET`
- `PROPERTY_ROWS_REBUILT`
- `PROPERTY_GRID_DUPLICATE_ROW_DETECTED`
- `PROPERTY_EDIT_BEGIN`
- `PROPERTY_EDIT_COMMIT`
- `PROPERTY_EDIT_CANCEL`
- `PROPERTY_EDIT_STATE_STUCK`
- `TEXT_PROPERTY_DEFAULT_FALLBACK_SUPPRESSED`

## 8. Designer Canvas

Designer Canvas is editor-only visual representation. It is not the same as Preview runtime tree.

Core responsibilities:

- render controls from `DesignControlModel`;
- provide drag/drop from Toolbox;
- provide selection and resize adorners;
- support move/resize operations;
- show design-time overlays such as selection frame and invisible control outline;
- maintain hit-testing independent from runtime opacity;
- support zoom/viewport.

`MainWindow.axaml.cs` contains a lot of rendering and pointer logic. Important areas include:

- rendering host/canvas children;
- `AddRenderedControl(...)` for model -> visual;
- drag/drop pointer handlers;
- resize handle logic;
- opacity=0 outline/selection behavior;
- runtime preview opening.

Canvas model mapping:

- `DesignControlModel.X` -> `Canvas.Left`
- `DesignControlModel.Y` -> `Canvas.Top`
- `Width`, `Height` -> control bounds
- collection order / z-index -> visual order

Resize rules:

- resizing a control can update that control's width/height and possibly x/y depending on resize handle;
- resizing the form changes only form/canvas size;
- resizing preview window does not change model coordinates;
- no proportional movement of canvas children unless a future explicit layout/anchor mode is enabled.

Opacity=0 design rule:

- runtime Preview/Export: real `Opacity=0`, object invisible.
- Designer Canvas: inner visual may be transparent, but outer wrapper/adorner/outline must stay visible/selectable.

Diagnostics:

- `FORM_RESIZE_START`
- `FORM_RESIZE_END`
- `CONTROL_POSITION_CHANGED_DURING_FORM_RESIZE`
- `DESIGNER_INVISIBLE_CONTROL_OUTLINE_SHOWN`
- `DESIGNER_HIT_TEST_INVISIBLE_CONTROL`

## 9. Preview

Preview is runtime-like view of a form. It should match exported AXAML as closely as possible.

Key principle:

```text
DocumentModel
 -> same generation/intermediate rules
 -> Preview visual tree

DocumentModel
 -> same generation/intermediate rules
 -> Exported AXAML
```

Preview may have technical differences:

- isolated host window;
- preview-only sample data;
- optional compact runtime badge overlay;
- debug diagnostics.

Preview-only elements must not:

- affect layout;
- change control bounds;
- be exported;
- mutate editor state.

Runtime badge:

- controlled by Preview settings;
- should be off by default or compact overlay;
- must not participate in layout;
- must not reduce form content area.

Preview generation must not:

- change `ActiveFormDocument`;
- change `SelectedControl`;
- change `CurrentInspectorDocumentId` / `CurrentInspectorControlId`;
- call `ApplyDocument` for editor surface;
- clear canvas;
- rebuild PropertyGrid;
- touch Project Explorer.

Diagnostics:

- `PREVIEW_GENERATION_START`
- `PREVIEW_GENERATION_END`
- `PREVIEW_CONTROL_ORDER`
- `PREVIEW_LAYOUT_SNAPSHOT`
- `PREVIEW_CONTROL_PROPERTIES`
- `PREVIEW_MUTATED_EDITOR_STATE`
- `PREVIEW_RUNTIME_BADGE_SHOWN`
- `PREVIEW_RUNTIME_BADGE_HIDDEN`

Preview/export compare diagnostics:

- `PREVIEW_EXPORT_COMPARE_START`
- `PREVIEW_EXPORT_COMPARE_END`
- `PREVIEW_EXPORT_ORDER_MISMATCH`
- `PREVIEW_EXPORT_LAYOUT_MISMATCH`
- `PREVIEW_EXPORT_PROPERTY_MISMATCH`

## 10. Export pipeline architecture

Export pipeline generates a standalone Avalonia project or generated files from current project model. It must be read-only relative to editor state.

Relevant classes:

- `MainWindowViewModel` - currently orchestrates generation and prepares `GeneratedFileModel`.
- `ExportPipelineService` - writes files, validates restore/build, aggregates messages/logs.
- `ExportWorkspaceService` - workspace/artifacts helpers.
- `ArtifactCleanupService` - cleanup policy.
- `GeneratedFileModel`, `ExportValidationResult`, `ExportedProjectFile` - generated results.

Export flow:

```text
ProjectModel snapshot
 -> BuildGeneratedFiles()
 -> AXAML generator
 -> C# generator
 -> csproj / nuget.config / README generator
 -> ExportPipelineService
 -> write export workspace
 -> optional Validate Build
 -> collect diagnostics
 -> cleanup artifacts according to settings
```

Detailed flow:

1. Take project snapshot or use current project model read-only.
2. Generate AXAML from `DesignerDocumentFileModel`.
3. Generate C# code-behind/ViewModel/data DTOs.
4. Generate secondary form files.
5. Generate `.csproj`, `nuget.config`, package references.
6. Generate README.generated.md if useful/enabled.
7. Write generated files to export workspace/artifacts.
8. Optionally run restore/build.
9. Deduplicate warnings/errors.
10. Write detailed logs.
11. Cleanup artifacts according to settings.

Export must not:

- call `ApplyDocument` on active VM;
- change active form;
- change selected control;
- clear inspector;
- rebuild canvas;
- change opened tabs;
- mutate Project Explorer;
- reset property edit fields.

`BuildSecondaryFormGeneratedFiles` is a critical method. It must generate secondary forms in an isolated/pure context and never temporarily apply secondary documents to the editor.

Validate Build:

`ExportPipelineService.ValidateBuildAsync` runs staged validation:

```text
Preparing export workspace
 -> Generating project files
 -> Restoring NuGet packages
 -> Building project
 -> Collecting warnings/errors
 -> Cleaning temporary artifacts, if enabled
 -> Done/Failed
```

Build output should expose:

- current step;
- elapsed time;
- command line if verbose;
- exit code;
- stdout/stderr;
- deduplicated warnings/errors;
- generated project path;
- detailed log path.

NuGet rules:

- HTTP package sources require `allowInsecureConnections=true` in generated/merged `nuget.config`.
- Do not remove user package sources.
- Do not include designer-only packages in generated runtime project.
- Keep Avalonia package versions consistent.

Artifacts rules:

- artifacts/export must not grow forever;
- cleanup must respect current active export;
- cleanup policy should be configurable;
- log freed size/errors.

Export diagnostics:

- `EXPORT_PIPELINE_START`
- `EXPORT_PIPELINE_END`
- `EXPORT_EDITOR_STATE_SNAPSHOT_BEFORE`
- `EXPORT_EDITOR_STATE_SNAPSHOT_AFTER`
- `EXPORT_MUTATED_EDITOR_STATE`
- `EXPORT_PIPELINE_APPLY_DOCUMENT_CALL_DETECTED`
- `EDITOR_STATE_MUTATION_DURING_EXPORT_BLOCKED`
- `BUILD_SECONDARY_FORM_GENERATION_PURE`
- `NUGET_HTTP_SOURCE_DETECTED`
- `NUGET_ALLOW_INSECURE_CONNECTIONS_APPLIED`
- `ARTIFACTS_CLEANUP_START`
- `ARTIFACTS_CLEANUP_END`

## 11. DataGrid

DataGrid subsystem currently uses:

- `DesignControlModel` for the DataGrid control itself.
- `BindingSourceModel` for data source/schema.
- `BindingFieldModel` for columns/fields.
- `DataSourceIdentity` for stable source keys.
- `DataGridColumnEditorWindow` and its ViewModel for column editing.
- export generation methods in `MainWindowViewModel`.

DataGrid flow:

```text
DataSourceKey
 -> BindingSourceModel schema
 -> BindingFieldModel columns
 -> DataGrid preview rows
 -> AXAML DataGrid.Columns
 -> generated ViewModel ItemsSource
```

Supported source kinds:

- Manual columns / sample data.
- SQL query/source.
- DLL table / LINQ to SQL metadata.
- Fake/sample preview data.

### DataGrid model behavior

The DataGrid visual control is a `DesignControlModel` with DataGrid-specific fields. The columns are usually represented by `BindingFieldModel` inside the selected `BindingSourceModel`.

Important fields/settings:

- `ItemsSource` binding source id/path.
- `AutoGenerateColumns`.
- `CanUserSortColumns`.
- `CanUserResizeColumns`.
- `HorizontalScrollBarVisibility`.
- `VerticalScrollBarVisibility`.
- grouping settings.
- column width/min/max.
- `TextWrapping`, `TextTrimming`, `RowHeightMode`.
- `SortMemberPath`.

### Column Editor

`DataGridColumnEditorWindow` uses a working copy. Correct flow:

```text
Open editor
 -> copy current columns/schema to working copy
 -> user edits working copy
 -> Apply: update model
 -> Cancel: discard working copy
```

Column Editor must not:

- call `ApplyDocument`;
- reset selected control;
- rebuild whole PropertyGrid unnecessarily;
- mutate model while user is typing unless architecture explicitly supports live apply.

### Data mode

Data mode should show selected DataGrid source/schema/sample data. It should not duplicate Column Editor editing.

Data mode should display:

- form name;
- grid name;
- source kind;
- source display name;
- source key;
- schema columns count;
- sample rows count;
- errors/warnings;
- buttons: Open Column Editor, Refresh schema, Preview sample data.

It must not show first random table from global list.

### DataGrid export

AXAML generation rules:

```xml
<DataGrid x:Name="OrdersGrid"
          ItemsSource="{Binding Orders}"
          AutoGenerateColumns="False"
          CanUserSortColumns="True"
          CanUserResizeColumns="True"
          HorizontalScrollBarVisibility="Auto"
          VerticalScrollBarVisibility="Auto">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Id"
                            Binding="{Binding Id}"
                            SortMemberPath="Id" />
        <DataGridCheckBoxColumn Header="IsActive"
                                Binding="{Binding IsActive}"
                                SortMemberPath="IsActive" />
    </DataGrid.Columns>
</DataGrid>
```

Do not generate:

- `<dataGrid:DataGridTextColumn ...>`
- `{Binding }`
- `Path=""`
- invalid `x:DataType=""`
- unnecessary `HeaderTemplate`

Column type rules:

- bool -> `DataGridCheckBoxColumn`;
- simple text/numeric/date -> `DataGridTextColumn`;
- wrapping/trimming/template requirements -> `DataGridTemplateColumn` with `TextBlock`;
- unknown/dynamic source -> safe classic binding without invalid compiled binding.

C# generation rules:

- create `ObservableCollection<SomeRow> SomeItems`.
- create DTO row class with properties matching columns/schema.
- add sample data or placeholder load method.
- do not export secrets such as SQL connection strings.
- for DLL source, either reference real type if safe or generate DTO + comment/hint.

DataGrid export diagnostics:

- `EXPORT_DATAGRID_BINDING_GENERATED`
- `EXPORT_DATAGRID_VIEWMODEL_PROPERTY_GENERATED`
- `EXPORT_DATAGRID_ROW_DTO_GENERATED`
- `EXPORT_DATAGRID_COLUMN`
- `EXPORT_DATAGRID_INVALID_BINDING_BLOCKED`
- `EXPORT_DATAGRID_INVALID_COLUMN_TAG_DETECTED`

Sorting/grouping/resizing rules:

- `CanUserSortColumns="True"` and `SortMemberPath` per column.
- `CanUserResizeColumns="True"` and valid width/min/max.
- horizontal/vertical scrollbars should be `Auto` for many columns.
- grouping must use adapter/helper or warn clearly if unsupported.
- preview/export settings must match.

## 12. DLL Import / Data binding

DLL Import extracts metadata from assemblies and creates stable data sources for DataGrid binding.

DLL flow:

```text
DLL path
 -> metadata extraction
 -> ImportedDllInfoModel / table/type/column DTOs
 -> DataSourceKey
 -> search index
 -> DataGrid binding source
```

LINQ to SQL metadata:

The importer should detect:

- `System.Data.Linq.Mapping.TableAttribute`
- `System.Data.Linq.Mapping.ColumnAttribute`
- `System.Data.Linq.Mapping.AssociationAttribute`
- namespace/type/table name;
- column name/property name;
- primary key;
- nullable;
- db type;
- CLR type.

Data metadata models:

- `ImportedDllInfoModel` - DLL summary/status.
- `ImportedDllTypeInfoModel` - type metadata.
- `ImportedDllTableInfoModel` - table metadata with `SourceKey`.
- `ImportedDllColumnInfoModel` - column metadata.

Stable source identity:

DataGrid must not bind only by `TableName`. Use full key via `DataSourceIdentity`, conceptually:

```text
SourceKind
DllId
AssemblyName
AssemblyPathHash
Namespace
TypeName
TableName
SchemaHash
```

For SQL source include query hash/schema hash. For manual source include source id/schema hash.

Duplicate table names:

- if table name is unique, display can be short;
- if duplicate, qualify display name by DLL and namespace/type;
- identity remains key, not display name.

Remove DLL:

```text
RemoveDll
 -> remove ImportedDllInfoModel
 -> remove metadata/search/schema/preview caches
 -> detach DataGrid sources bound to DLL
 -> mark source Missing/Detached
 -> keep existing columns as manual
 -> update Data mode/Column Editor
```

Error handling:

- failed DLL import must create visible UI status `Failed` or `Partial`;
- details must include exception type/message and loader exceptions;
- app must not crash;
- failed DLL can be removed.

Performance rules:

- do not load real table rows during import;
- extract compact metadata DTOs, not hold `Type`/`PropertyInfo` forever;
- use async/cancellation/progress for heavy import;
- use batch updates for UI collections;
- search index should be compact strings/keys;
- preview rows loaded only on demand and limited.

Diagnostics:

- `DLL_IMPORT_START`
- `DLL_IMPORT_END`
- `DLL_IMPORT_FAILED`
- `DLL_LOAD_FAILED`
- `DLL_LOAD_ERROR_REPORTED_TO_UI`
- `DLL_LOAD_PARTIAL_SUCCESS`
- `LINQ_TO_SQL_TABLE_DETECTED`
- `LINQ_TO_SQL_COLUMN_DETECTED`
- `DLL_DUPLICATE_TABLE_NAME_DETECTED`
- `DLL_TABLE_DISPLAY_NAME_QUALIFIED`
- `DATASOURCE_KEY_CREATED`
- `DATASOURCE_KEY_COLLISION_DETECTED`
- `DLL_REMOVE_COMPLETED`
- `DLL_METADATA_CACHE_STATS`

## 13. Logic Editor

Logic Editor stores user-defined interactions in `InteractionModel`.

Core concepts:

- source control;
- event;
- action type;
- target control;
- target property;
- validation state;
- generated code.

Important supported scenario:

```text
DataGrid1.SelectionChanged
 -> selected row
 -> template text with placeholders
 -> TextBox1.Text or TextBlock1.Text
```

Example template:

```text
ID: {Id}
Name: {Name}
Price: {Price}
```

The model should capture:

- source DataGrid id/name;
- event name, normalized to DataGrid selection event;
- target control id/name;
- target property `Text`;
- `TemplateText`;
- missing value behavior;
- no selection behavior/text;
- placeholder list/validation.

Target controls:

- `TextBox`
- `TextBlock`
- future compatible text-bearing controls.

Invalid targets should produce validation warning, not crash.

Template editor rules:

- edit in working copy/draft;
- typing updates local text only;
- preview result and placeholder validation are debounced;
- Apply writes model;
- Cancel discards draft.

Typing in template editor must not:

- call `ApplyDocument`;
- rebuild PropertyGrid;
- run export pipeline;
- rebuild entire Logic tree;
- freeze UI.

Export logic:

Generated C# should wire `SelectionChanged`, cast selected row to generated/known row DTO type, format template, and set target TextBox/TextBlock text. Invalid placeholders should produce export warning/error instead of broken code.

Diagnostics:

- `LOGIC_TEMPLATE_EDIT_BEGIN`
- `LOGIC_TEMPLATE_TEXT_CHANGED`
- `LOGIC_TEMPLATE_PREVIEW_UPDATED`
- `LOGIC_TEMPLATE_VALIDATION_UPDATED`
- `LOGIC_TEMPLATE_HEAVY_OPERATION_BLOCKED`
- `LOGIC_TEMPLATE_EDIT_APPLY`
- `LOGIC_TEMPLATE_EDIT_CANCEL`
- `EXPORT_LOGIC_DATAGRID_SELECTION_GENERATED`
- `EXPORT_LOGIC_TEMPLATE_INVALID_PLACEHOLDER`

## 14. Settings / Logs / Diagnostics

Settings are stored via `AppSettingsService` and represented by `AppSettingsModel`.

Important settings areas:

- Preview:
  - show runtime badge;
  - compact/auto-hide badge if enabled;
  - experimental Layout tab flag.
- Export/Build:
  - validate after export;
  - validate on demand;
  - allow insecure HTTP NuGet sources;
  - verbose build logs;
  - keep successful artifacts;
  - automatic cleanup;
  - max artifact size/runs.
- Logs:
  - log level;
  - save logs to file;
  - max log size/count;
  - open/clear logs.

Logs/Diagnostics should be structured:

```text
timestamp
event/category
severity
active form
selected control
focused element, if relevant
flags, if relevant
reason/details
```

Important diagnostics categories:

- Project/New Project.
- Inspector/PropertyGrid.
- Canvas/Pointer/Focus.
- Preview.
- Export/Build.
- DataGrid.
- DLL/Data binding.
- Logic.
- Performance/Memory.

Logs panel should support:

- severity filter;
- category filter;
- copy selected/all;
- clear;
- open logs folder.

Useful diagnostics patterns:

- Use start/end pairs for heavy operations.
- Include reason/caller.
- Include ids, not only names.
- Include counts for collections/caches.
- Log suppression reasons, not just operation success.

Examples:

- `PERF_OPERATION_START` / `PERF_OPERATION_END`
- `REFRESH_STORM_DETECTED`
- `HEAVY_OPERATION_ON_UI_THREAD`
- `MEMORY_SNAPSHOT`
- `OLD_PROJECT_OBJECTS_STILL_ALIVE`
- `LOCALIZATION_MISSING_KEY`

## 15. Performance and memory rules

Performance problems usually come from refresh storms, heavy reflection state, unbounded caches, event subscription leaks, or UI-thread operations.

Rules:

1. Do not keep heavy reflection objects in long-lived state. Store compact DTO metadata.
2. Do not load all real table rows in designer.
3. Use limited sample rows for SQL/DLL preview.
4. DLL metadata should be lazy and cancellable.
5. Use debounce/throttle for typing, resize, search, validation, export refresh.
6. Batch update `ObservableCollection` where possible.
7. Do not full rebuild Canvas/PropertyGrid on every pixel/character.
8. Drag/resize should update lightweight overlay/model fields and commit heavy changes on release.
9. Export/build should not run on every ordinary property edit.
10. New Project and Remove DLL must clear caches and detach event subscriptions.
11. Every new heavy operation should have diagnostics.

Danger zones:

- event subscriptions (`PropertyChanged`, `CollectionChanged`);
- canvas wrappers/adorners;
- preview windows and runtime controls;
- DLL metadata/search index/preview rows;
- export generated files/artifacts;
- undo/redo snapshots;
- autosave/recovery references;
- timers/debounce callbacks;
- async tasks/cancellation tokens.

Memory diagnostics should include:

```text
MEMORY_SNAPSHOT
 - reason
 - GC.GetTotalMemory(false)
 - forms count
 - controls count
 - wrappers count
 - property rows count
 - loaded dll count
 - data sources count
 - schema cache count
 - preview rows count
 - export cache count
 - undo/redo count
```

After New Project/debug cleanup, weak references to old project/forms/controls/DLL metadata can be checked. If alive, log `OLD_PROJECT_OBJECTS_STILL_ALIVE`.

## 16. Save/Load project

`ProjectWorkspaceService` saves/loads workspace JSON using `System.Text.Json`.

Save flow:

```text
Save command
 -> SaveActiveFormState(reason)
 -> WorkspaceModel serialized to JSON
 -> write workspace file
 -> update dirty/saved state
 -> diagnostics/logs
```

Load flow:

```text
Open project
 -> deserialize WorkspaceModel
 -> migrate/default missing settings if needed
 -> ResetApplicationStateForLoadedProject
 -> set CurrentWorkspace/Project
 -> rebuild Project Explorer
 -> open/set active Form1 or saved active form
 -> LoadActiveFormState
 -> rebuild Canvas/Inspector
```

Persisted:

- project metadata;
- forms/documents;
- controls;
- binding sources/fields;
- interactions;
- resources/assets references;
- project settings/profiles.

Transient/editor-only:

- current UI focus;
- active TextBox edit text before commit;
- runtime preview visual tree;
- export validation workspace;
- canvas wrappers/adorners;
- loaded reflection objects;
- build stdout cache unless explicitly stored as logs.

Load errors:

- should show user-readable message;
- detailed logs should include exception type/message/path;
- old version/migration issues should not crash without details.

## 17. Smoke tests / regression tests

Smoke tests live in `smoke-tests`. The exact test runner/scripts should be checked in that folder before running. There are also manual checklists in `Docs`.

Important regression scenarios:

- Add Form -> return to Form1 -> select Button -> Properties visible.
- Add Form -> edit Text -> Enter -> text does not reset.
- Add Form -> color Foreground/Background -> canvas and export update.
- Properties tab active after selecting control, not empty.
- Form switch -> Inspector shows active form/control, no stale rows.
- Rename form -> no duplicate property rows.
- Width/Height edit -> inspector remains editable.
- Empty Button Text/Content remains empty.
- New Project -> old forms/controls/caches removed.
- Export does not mutate editor state.
- BuildSecondaryFormGeneratedFiles does not call ApplyDocument on editor VM.
- Preview/export order/bounds/properties match.
- DataGrid export builds without invalid tags/bindings.
- DLL import duplicate table names use stable DataSourceKey.
- Remove DLL detaches DataGrid source without crash.
- Column Editor Apply/Cancel works.
- Logic template typing does not call ApplyDocument/RebuildPropertyGrid/export.
- Validate Build logs restore/build steps and deduplicates messages.

Before commit, at minimum run smoke scenarios covering the subsystem touched. For docs-only changes, build is not necessary, but if docs mention changed behavior, run relevant smoke tests.

## 18. Development rules / Do not break

1. Do not call `ApplyDocument` from export/build/validate.
2. Do not mutate editor state from Preview/Export.
3. Do not keep stale `SelectedControl` references; resolve by id from active document.
4. Property edit must not call full `ApplyDocument`.
5. `RebuildPropertyGrid` must be idempotent.
6. New Project must clear all old state and create one new empty Form1 with a new id.
7. DataGrid source must use full `DataSourceKey`, not `TableName`.
8. DLL import must be async/lazy/limited.
9. Do not load real large data into designer by default.
10. Any new heavy operation must have diagnostics.
11. Any new UI feature should have a smoke/regression test.
12. Do not translate property names/control class names during localization.
13. Generated project must build without manual fixes.
14. Exported AXAML must not contain invalid DataGrid tags or empty bindings.
15. Preview must match exported AXAML behavior for order, bounds, key properties.
16. Runtime badge/diagnostics overlays must not affect layout.
17. Resize form must not move Canvas children unless explicit future layout mode is enabled.
18. Empty user values are valid values; do not silently restore defaults after edit.
19. Logs should include reason/caller for suppression and heavy operations.
20. If a method has broad side effects, document them before expanding it.

## 19. Recommended future refactoring

This section is recommendation, not a statement of current implementation.

The largest technical debt is `MainWindowViewModel`. It currently owns too many subsystems. A safer future direction:

```text
MainWindowViewModel
 -> ProjectSessionService
 -> FormNavigationService
 -> InspectorService
 -> CanvasEditorService
 -> ExportGenerationService
 -> DataGridDesignerService
 -> DllImportService
 -> LogicDesignerService
 -> DiagnosticsService
```

Do this gradually. Do not rewrite everything at once. Extract only when:

- there is a clear ownership boundary;
- behavior is covered by smoke/regression tests;
- editor/preview/export state separation remains explicit.

## 20. Quick search map

Use these names to find relevant code quickly:

- Active form and snapshots: `SetActiveForm`, `SaveActiveFormState`, `LoadActiveFormState`, `ApplyDocument`.
- New Project cleanup: `NewDocument`, `ResetApplicationStateForNewProject`, `AssertCleanNewProjectState`.
- Selection: `SelectSingleControl`, `ClearSelection`, `SelectedControlChanging`.
- Property Inspector: `RebuildPropertyGrid`, `BuildPropertyGridRows`, `EnsurePropertiesTabContent`, `CommitPropertyGridEdit`.
- Canvas rendering: `MainWindow.axaml.cs`, `AddRenderedControl`, pointer handlers, resize handlers.
- Preview: `PreviewWindow`, `PreviewRuntimeService`.
- Export: `GenerateXaml`, `RefreshExportPipelineResult`, `BuildGeneratedFiles`, `BuildSecondaryFormGeneratedFiles`, `ExportPipelineService`.
- DataGrid AXAML: `AppendDataGridXaml`.
- Generated C#: `BuildGeneratedCSharp`.
- DLL import: `ImportBindingSourcesFromAssembly`, `RemoveDll`, `ReloadDll`, `DesignerAssemblyLoadContext`.
- DataSourceKey: `DataSourceIdentity`.
- Column Editor: `DataGridColumnEditorWindow`, `DataGridColumnEditorViewModel`.
- Logic: `InteractionModel`, `AddInteraction`, Logic template methods, generated interaction handlers.
- Settings: `AppSettingsModel`, `AppSettingsService`.
- Build logs: `ExportPipelineService.ValidateBuildAsync`.

