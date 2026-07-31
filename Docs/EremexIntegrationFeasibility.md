# Исследование возможности интеграции Eremex

> Vendor-specific проверка реального NuGet package завершена 14 июля 2026 года. Точные версии, compatibility matrix, public controls, namespaces, theme, licensing и adapter probe вынесены в [EremexAvaloniaPackageResearch.md](EremexAvaloniaPackageResearch.md).

## 1. Executive summary

**Краткий вывод: поддержка Eremex технически возможна, но на текущем состоянии архитектуры только частично готова.**

Простой Eremex-контрол можно подключить через отдельный designer-плагин без изменения стандартных Avalonia-контролов. В проекте уже есть рабочие точки расширения для регистрации компонента, отдельной категории Toolbox, Drag & Drop, базовых свойств, legacy Preview и генерации AXAML-тега.

Полноценная интеграция сложных Eremex-компонентов, особенно GridControl/TreeList, пока не является подключением «только DLL». Для неё не хватает:

- автоматического discovery именно UI-контролов и их Avalonia properties;
- typed metadata для CLR, StyledProperty, DirectProperty и attached properties;
- специализированных редакторов коллекций, колонок, bands, summaries и templates;
- декларации NuGet packages, assembly references, themes/resources и startup code в plugin API;
- гарантированного разрешения сторонних assemblies в Preview из экспортируемого AXAML;
- project-level dependency manifest для сохранения и повторного подключения стороннего пакета;
- подтверждённых сведений о составе, runtime API и лицензировании Eremex.

Рекомендуемый подход: **гибрид «reflection discovery + EremexDesignerPlugin adapter»**. Reflection годится для первичного каталога типов и простых свойств. Adapter должен курировать UX, сложные свойства, Preview, AXAML, зависимости, themes/resources и диагностику.

### Итоговая оценка готовности

| Сценарий | Готовность текущей архитектуры | Вывод |
|---|---:|---|
| Отдельная группа Eremex в Toolbox | 80% | Категории и plugin descriptors уже есть; требуется adapter и убрать специальные исключения категорий |
| Drag & Drop простого контрола | 85% | Общий descriptor flow уже работает |
| 5–10 простых свойств | 60% | Работает после ручного описания schema; автоматического UI-property discovery нет |
| Legacy Preview простого контрола | 60% | Возможен при совместимых DLL, теме и лицензии |
| AXAML Preview простого контрола | 35% | Нужны assembly resolver и resources для runtime XAML loader |
| Export AXAML-тега/xmlns | 60% | Descriptor умеет писать тег и namespace |
| Самодостаточный generated project | 25% | Plugin API не сообщает packages/resources/startup и export не копирует runtime DLL |
| Сложный GridControl | 15–25% | Нужны typed collection editors и Eremex-specific adapter |
| Полная поддержка Eremex family | 20–30% | Реалистична как отдельная подсистема/плагин, не как набор `if` в ядре |

Проценты отражают покрытие текущей архитектурой, а не объём Eremex API. Для текущего `.NET 6 / Avalonia 11.1.5` host visual render подтверждён для `1.0.98`; `1.0.43` блокируется compatibility gate, а `1.4.34` требует `.NET 8 / Avalonia 12.0.2+`.

## 2. Граница исследования

Исследованы solution, локальный plugin SDK, примеры плагинов, Property Inspector, оба Preview path, export pipeline, project persistence и локальные package locations.

Проверены:

- корень solution `FormDesigner.sln`;
- `FormDesigner.csproj`;
- `PluginContracts/DesignerContracts.cs`;
- `DesignerSystem/Infrastructure`;
- `DesignerSystem/BuiltIn`;
- `DesignerSystem/Binding`;
- `Plugins/MinimalDesignerPlugin`;
- `Plugins/DemoDesignerPlugin`;
- `ViewModels/MainWindowViewModel.cs`;
- `Views/MainWindow.axaml.cs`;
- `Views/PreviewWindow.axaml.cs`;
- `Services/GeneratedAxamlService.cs`;
- `Services/RuntimeAxamlPreviewLoader.cs`;
- `Services/ExportPipelineService.cs`;
- модели project/document/settings.

### Проверенные Eremex artifacts

В основной проект Eremex references по-прежнему не добавлены. Для изолированного исследования получены и проверены официальные NuGet packages:

- `Eremex.Avalonia.Controls 1.4.34` и `Eremex.Avalonia.Themes.DeltaDesign 1.4.34` для `.NET 8 / Avalonia 12.0.2`;
- `Eremex.Avalonia.Controls 1.0.98` и `Eremex.Avalonia.Themes.DeltaDesign 1.0.98` для `.NET 6 / Avalonia 11.1`.

Текущий Designer использует `.NET 6 / Avalonia 11.1.5`. Для него подтверждена версия Eremex `1.0.98`: restore/build, `TextEditor`, DeltaDesign theme, attach to visual tree, `ApplyTemplate`, layout, PluginLoader и AXAML Preview прошли в одном процессе. Последняя версия `1.4.34` требует обновления host до Avalonia 12.

Полные результаты и ограничения: [EremexAvaloniaPackageResearch.md](EremexAvaloniaPackageResearch.md).

### Матрица vendor-specific проверки

| Вопрос | Статус | Результат |
|---|---|---|
| Какие packages поддерживают Avalonia | Проверено | `Eremex.Avalonia.Controls` + `Eremex.Avalonia.Themes.DeltaDesign` |
| Какие public controls доступны | Проверено metadata scan | `1.4.34`: 238 controls + 29 charts; полный CSV приложен |
| Базовые типы controls | Проверено | Avalonia `Control` hierarchy извлечена reflection probe |
| Работает ли visual render | Проверено | `TextEditor 1.0.98` проходит attach, `ApplyTemplate` и layout в Avalonia 11.1 process |
| Нужен ли startup/init code | Проверено | Theme registration + package MSBuild license target; отдельного `UseEremex()` нет в vendor template |
| Нужны ли themes/styles | Да | `DeltaDesignTheme`; без Eremex theme controls могут быть пустыми |
| Есть ли runtime/design license checks | Да | Trial/license key flow документирован vendor; build target генерирует license data |
| Обязательные assemblies/native assets | Проверено package inventory | Managed assemblies, build tasks и vendor licensing components находятся в package |
| Обычные CLR properties | Проверено | Public instance properties доступны reflection |
| StyledProperty/DirectProperty | Частично проверено | Public Avalonia property fields обнаружены; семантическая классификация Direct/Styled требует adapter metadata |
| Attached properties | Не завершено | Требуется отдельный API catalog для каждого product family |
| Columns/bands/editors/templates/summaries | Не завершено | Это следующий GridControl-specific этап, не часть простого TextEditor PoC |
| Сериализуемость обычным AXAML | Частично проверено | Официальные XML namespaces есть; host-level generated build/run ещё не выполнен |
| Design-time restrictions | Частично проверено | Activation и descriptor compile прошли; лицензированный render-time host test ещё нужен |
| Достаточно ли public DLL/NuGet API | Да для простого control | `TextEditor` adapter загружен host PluginLoader и не требует исходного кода Eremex |

## 3. Что уже готово в архитектуре

### 3.1 Descriptor/plugin API

`PluginContracts/DesignerContracts.cs` уже содержит:

- `IFormDesignerPlugin` для регистрации пакета;
- `IDesignerRegistry` для регистрации controls и binding providers;
- `IControlDescriptor` для Toolbox metadata, defaults, Preview и AXAML export;
- `DesignPropertyDescriptor` для schema Property Inspector;
- `DesignerControlDefinition` для built-in/custom values;
- `IPreviewContext` и `IXamlExportContext`;
- `RegisterXmlNamespace(...)` для стороннего AXAML namespace.

Это правильная основа для `EremexDesignerPlugin`. Стандартные Avalonia controls регистрируются в том же registry и не требуют замены.

### 3.2 Доказательство на sample plugins

В solution есть рабочие примеры:

- `MinimalDesignerPlugin/HelloCardDescriptor`;
- `DemoDesignerPlugin/DemoGridControlDescriptor`;
- `DemoDesignerPlugin/DemoTreeListDescriptor`.

Они подтверждают, что внешний plugin может:

- зарегистрировать свой TypeKey и Category;
- создать default model;
- показать custom properties;
- построить Control для Preview;
- зарегистрировать XML namespace;
- сгенерировать custom AXAML tag.

Это важное доказательство жизнеспособности adapter-подхода, но demo controls находятся под контролем самого solution. Они не проверяют сторонний package, license/runtime initialization или сложные collections vendor-компонента.

### 3.3 Изоляция загрузки плагинов

`DesignerSystem/Infrastructure/PluginLoading.cs` использует collectible `AssemblyLoadContext` и `AssemblyDependencyResolver`. Managed и unmanaged dependencies могут разрешаться из plugin package folder.

Ограничения текущего loader:

- он ищет `IFormDesignerPlugin`, а не произвольные subclasses `Avalonia.Controls.Control`;
- `Activator.CreateInstance` применяется к plugin class, но не автоматически к зарегистрированным controls;
- loader рекурсивно сканирует каждую DLL, поэтому vendor package с большим количеством assemblies будет загружаться/инспектироваться избыточно;
- assemblies из default context переиспользуются по простому имени, что создаёт риск version conflict;
- manifest с entry assembly, dependency list и compatibility constraints отсутствует.

Host, PluginContracts и sample plugins используют Avalonia `11.1.5`. Eremex-плагины должны проходить compatibility gate до регистрации controls: они не могут приносить отдельную несовместимую копию core Avalonia assemblies.

Eremex DLL сама по себе не появится в Toolbox. Нужна adapter assembly, реализующая `IFormDesignerPlugin`.

## 4. Карта подсистем

| Подсистема | Текущее состояние | Можно переиспользовать | Что требуется для Eremex |
|---|---|---|---|
| Toolbox registration | Descriptor-driven registry | `IControlDescriptor`, Category, TypeKey | Adapter descriptors, icons/default metadata, нормальная category grouping |
| Drag & Drop | Передаёт TypeKey и вызывает descriptor defaults | Общий drop path | Проверка availability/license до создания |
| Design model | Общие поля + JSON custom properties | Position/size/common values | Third-party identity, typed values, dependency reference, collection nodes |
| Property Inspector | Ручная descriptor schema | Text/bool/number/enum/color | Reflection catalog, validation, attached/direct/styled properties, custom editors |
| DLL Import | Metadata provider для data entities | Safe metadata loading patterns | Отдельный control discovery service; текущий importer не подходит напрямую |
| Legacy Preview | `descriptor.BuildPreview(...)` | Adapter factory | Theme/init/license/error boundary |
| AXAML Preview | Runtime loader from string | Общий export AXAML generator | Assembly resolver, local assembly context, resource/theme contribution |
| AXAML export | Descriptor writes XML and namespace | `AppendXaml`, `RegisterXmlNamespace` | Structured export metadata, collections/templates, validation |
| C# export | Hardcoded host generation | Общий project generation | Startup/license contributions, event adapters, code-behind hooks |
| NuGet generation | Hardcoded known packages | `RequiredPackageModel` pipeline | Plugin-contributed PackageReference with exact versions |
| App.axaml | Fluent/DataGrid hardcoded includes | Existing app generation | Plugin-contributed styles/resources in deterministic order |
| Save/load JSON | PluginId/version/custom JSON preserved | Missing-control placeholder | Package/assembly/type identity and dependency manifest |
| Plugin system | API v1, collectible ALC | Strong base for adapter | API v2 capabilities and compatibility checks |

## 5. Toolbox integration

### Текущий flow

1. `DesignerRegistry.GetControls()` возвращает descriptors.
2. `MainWindowViewModel.RefreshToolboxItemsFromRegistry()` создаёт `ToolboxItem` из Title, TypeKey, Category и Description.
3. Drag payload содержит TypeKey.
4. Drop вызывает `TryCreateControlFromToolboxDrop(...)`.
5. `CreateDefaultControl(...)` вызывает `descriptor.CreateDefaultDefinition(...)`.

Этот flow универсален и не зависит от конкретного Avalonia control type.

### Важный hardcode

`ShouldShowInMainToolbox(...)` специально помещает в основной Toolbox только два demo TypeKey. Остальные external descriptors идут в `PluginToolboxItems`. Это не блокирует Eremex, но grouping сейчас основан не только на Category и содержит sample-specific исключения.

### Рекомендуемый descriptor

Для полноценной сторонней поддержки descriptor должен иметь как минимум:

```text
ControlId / TypeKey
DisplayName
Category
ProviderId
ClrTypeName
AssemblyName
AssemblyPath or PackageReferenceId
XmlNamespace
ExportTag
DefaultWidth / DefaultHeight
Icon resource
Preview factory
Required resources
Compatibility constraints
```

Текущий `IControlDescriptor` покрывает TypeKey, Title, Category, Preview и AXAML, но не содержит CLR/package/resource metadata.

### Вывод

Отдельная группа `Eremex` реализуема без риска для стандартных controls. Лучше строить группы по `Category`/`ProviderId`, а не добавлять Eremex-specific `if` в `MainWindowViewModel`.

## 6. Создание control instance

Designer Canvas не обязан хранить реальный Eremex object. Он хранит `DesignControlModel`, а visual instance создаёт descriptor в `BuildPreview(...)`. Это хорошая изоляция.

Варианты factory:

1. Adapter компилируется с Eremex reference и вызывает `new VendorControl()`.
2. Adapter получает `Type` и вызывает `Activator.CreateInstance(type)`.
3. Adapter использует vendor factory/startup API.

На реальной assembly `1.0.98` `TextEditor` имеет public parameterless constructor и проходит visual render в Avalonia 11.1 process. Для каждого следующего Toolbox-кандидата constructor, mandatory parent/model objects и visual template необходимо валидировать отдельно.

Для PoC предпочтителен strongly typed adapter: ошибки типов, properties и breaking changes обнаруживаются на build, а не во время Drag & Drop.

## 7. Property Inspector

### Фактическая модель

Property Inspector **не строит UI control properties по Reflection**.

Он использует два источника:

1. Общие свойства `DesignControlModel`, вручную сформированные в `BuildPropertyGridRows()`.
2. Custom schema из `IControlDescriptor.Properties`.

`DesignPropertyDescriptor` поддерживает editor kinds:

- Text;
- Bool;
- Number;
- Enum;
- Color;
- Binding;
- Collection.

Но `Collection` сейчас попадает в текстовый редактор и хранится как serialized JSON/string. Это не полноценный collection editor.

### Ответы на ключевые вопросы

| Возможность | Сейчас | Комментарий |
|---|---|---|
| Reflection CLR properties | Нет для UI controls | Reflection importer обслуживает data entities/DLL tables |
| Avalonia StyledProperty | Нет автоматического каталога | Можно использовать в будущем через AvaloniaPropertyRegistry/field discovery |
| Avalonia DirectProperty | Нет автоматического каталога | Нужна отдельная metadata strategy |
| Inherited properties | Только вручную описанные общие | Reflection provider должен учитывать inheritance и deduplication |
| Readonly filtering | Нет общего механизма | Descriptor author просто не добавляет property |
| bool/enum/string/number | Да | Подходят для PoC |
| Brush/color | Частично | Color editor рассчитан на строковое/HEX значение, не на все Brush types |
| Thickness/CornerRadius | Нет универсального typed editor | В built-ins часто сведены к одному number/string |
| Bindings | Маркер и text-compatible editor | Нет полноценного binding expression designer для plugin property |
| Collections | Только serialized text | Недостаточно для Grid columns/bands/summaries |
| Templates/DataTemplates | Нет | Нужен template/resource editor и безопасное хранение AXAML |
| Commands/events | Нет plugin metadata | `IControlDescriptor` не объявляет события/commands |
| Attached properties | Нет | Нужны owner type, target constraints и export syntax |

### Reflection-only подход

Полностью автоматический Reflection полезен для первого каталога, но опасен как конечный UX:

- сотни inherited properties;
- readonly/service/runtime properties;
- properties с побочными эффектами;
- неподдерживаемые object graphs;
- internal collections и owner-specific semantics;
- vendor API может требовать BeginInit/EndInit или специальный order.

### Рекомендуемый hybrid

1. `ControlMetadataDiscoveryService` находит public non-abstract Avalonia controls.
2. `AvaloniaPropertyMetadataProvider` собирает CLR + StyledProperty + DirectProperty metadata.
3. Safety filter скрывает indexers, readonly runtime services, delegates, handles и unsafe object graphs.
4. Generic editors дают базовую поддержку простых значений.
5. `EremexDesignerPlugin` заменяет metadata/editors для сложных properties.

Для GridControl `Columns`, bands, grouping, summaries, editors и templates должны иметь специализированные adapter-owned editors.

## 8. Reflection / DLL Import

`DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs` предназначен для data source metadata:

- LINQ-to-SQL table/column attributes;
- public scalar entity properties;
- `MetadataLoadContext`, portable metadata и runtime fallback.

Он не ищет Avalonia controls и не анализирует StyledProperty/DirectProperty. Переиспользовать можно:

- безопасное чтение metadata без запуска static constructors;
- dependency probing;
- tolerant handling `ReflectionTypeLoadException`;
- diagnostics.

Нельзя просто направить Eremex DLL в существующий DLL Import и ожидать Toolbox controls. Нужен отдельный control discovery path.

## 9. Preview integration

### 9.1 Legacy Preview

Legacy Preview вызывает `descriptor.BuildPreview(...)` и принимает готовый `Avalonia.Controls.Control`. Это наиболее реалистичный первый путь.

Adapter может:

- создать реальный Eremex instance;
- применить curated properties;
- назначить ItemsSource;
- вернуть placeholder с понятной ошибкой при missing dependency/license.

Перед PoC нужно проверить:

- совместимость Eremex с Avalonia 11.1.x и `net6.0`;
- обязательную тему;
- startup initialization;
- возможность design-time instance;
- license behavior;
- native dependencies.

Сейчас exception в `BuildPreview` проглатывается и заменяется generic missing preview. Для коммерческого контрола нужна структурированная ошибка с причиной.

### 9.2 Preview из экспортируемого AXAML

Runtime AXAML загружается из строки через `AvaloniaRuntimeXamlLoader` с synthetic base URI. `RuntimeAxamlPreviewLoader` заранее удерживает только известные host assemblies (`Binding`, `ItemsRepeater`, `DataGrid`).

Риски для Eremex:

- `LocalAssembly = null`;
- отсутствует plugin-aware assembly resolver;
- Eremex types могут жить в collectible plugin `AssemblyLoadContext`, невидимом runtime XAML compiler;
- Eremex resource URI/theme assembly может не разрешиться;
- version mismatch Avalonia assemblies может привести к type identity/load failures;
- license initialization может потребоваться до XAML load.

До подтверждения отдельным тестом нельзя считать AXAML Preview готовым к Eremex.

Рекомендуемое расширение:

```csharp
public interface IPreviewRuntimeContribution
{
    IReadOnlyList<Assembly> Assemblies { get; }
    IReadOnlyList<Uri> StyleIncludes { get; }
    void Initialize(PreviewRuntimeContext context);
}
```

Также нужен deterministic load context policy: либо все visual control assemblies доступны default context, либо runtime loader явно получает resolver/local assembly set. Смешивать одинаковый vendor assembly в нескольких ALC нельзя.

## 10. AXAML и C# export

### Что уже работает

`IControlDescriptor.AppendXaml(...)` может сгенерировать:

```xml
<eremex:SomeControl ... />
```

`IXamlExportContext.RegisterXmlNamespace(...)` добавляет `xmlns:eremex` в root Window.

### Что захардкожено

`GetRequiredExportNuGetPackages()` и project-wide dependency scan знают только:

- `Avalonia.Controls.DataGrid`;
- `CommunityToolkit.Mvvm`;
- `Microsoft.Data.SqlClient`.

Recommended versions и reasons также заданы switch-ами. `ExportPipelineService.BuildAppXaml(...)` умеет специальный include только для Avalonia DataGrid theme. Startup code генерируется без plugin contributions.

Флаг `IncludePluginRuntimeReferences` разрешает descriptor output вместо placeholder, но не делает generated project самодостаточным:

- Eremex NuGet package не добавляется автоматически;
- runtime DLL не копируется в exported project;
- `<Reference HintPath=...>` не создаётся;
- vendor styles/resources не добавляются;
- startup/license code не генерируется.

### Требуемый export contract

```csharp
public sealed class DesignerExportContribution
{
    public IReadOnlyList<PackageReferenceDescriptor> Packages { get; init; }
    public IReadOnlyList<AssemblyReferenceDescriptor> Assemblies { get; init; }
    public IReadOnlyList<XmlNamespaceDescriptor> XmlNamespaces { get; init; }
    public IReadOnlyList<ResourceIncludeDescriptor> AppResources { get; init; }
    public IReadOnlyList<string> StartupStatements { get; init; }
    public IReadOnlyList<string> RequiredUsings { get; init; }
    public IReadOnlyList<ExportDiagnostic> Diagnostics { get; init; }
}
```

Contribution должна собираться project-wide по всем формам, дедуплицироваться и валидироваться до записи файлов.

### Complex properties

Простые scalar properties можно писать attributes. Коллекции требуют property-element syntax, например концептуально:

```xml
<mxdg:DataGridControl ItemsSource="{Binding Employees}">
  <mxdg:DataGridControl.Columns>
    <!-- vendor-specific column objects -->
  </mxdg:DataGridControl.Columns>
</mxdg:DataGridControl>
```

`DataGridControl`, `ItemsSource` и namespace `https://schemas.eremexcontrols.net/avalonia/datagrid` подтверждены реальной assembly и официальной документацией. Конкретная модель columns/bands/templates остаётся отдельным Grid adapter scope.

## 11. Save/load integration

### Что сохраняется сейчас

`DesignerControlFileModel` сохраняет:

- Type;
- DescriptorId;
- PluginId;
- PluginVersion;
- common built-in fields;
- `CustomProperties` как `Key + ValueJson`.

`MissingPluginDescriptor` позволяет открыть документ без установленного plugin и показать placeholder. Это полезная база для graceful degradation.

### Чего не хватает

Не сохраняются:

- provider identity как отдельная модель;
- CLR full type name;
- assembly name/path/hash;
- PackageId/version/source;
- XML namespace/export tag;
- required resources/themes;
- version range/compatibility;
- structured collection object graph;
- migration version property schema.

Project model также не имеет списка plugin/package dependencies. Личные absolute paths нельзя считать переносимым dependency mechanism.

### Рекомендуемая модель

```json
{
  "type": "ThirdParty",
  "provider": "Eremex",
  "descriptorId": "Eremex.SomeControl",
  "clrTypeName": "Vendor.Namespace.SomeControl",
  "assemblyName": "Vendor.Assembly",
  "packageId": "<verified package id>",
  "packageVersion": "<verified version>",
  "propertySchemaVersion": 1,
  "properties": {},
  "collections": {}
}
```

Package/path credentials и license secrets не должны храниться внутри control JSON.

При missing dependency проект должен:

- сохранить исходные property JSON без потерь;
- показать Missing Control;
- сообщить точный PackageId/assembly/version;
- предложить relink/install;
- не выполнять export реального тега без dependency validation.

## 12. Themes, resources и startup

Текущий host `App.axaml` содержит Fluent, ColorPicker и DataGrid styles. Generated `App.axaml` содержит Fluent и опциональный DataGrid include.

Для Eremex подтверждён отдельный package `Eremex.Avalonia.Themes.DeltaDesign`. Официальный setup добавляет `<theme:DeltaDesignTheme />` в `Application.Styles`; без Eremex theme controls могут отображаться пустыми. Theme assembly содержит embedded `!AvaloniaResources` и palette resources, а `DeltaDesignTheme` создаётся через constructor с `IServiceProvider`.

Открытыми остаются порядок совместного подключения с host Fluent theme, unload resources из collectible ALC и полная проверка Light/Dark variants внутри Designer Preview.

Plugin API v1 не предоставляет способа объявить эти contributions. Добавлять их условными строками в `ExportPipelineService` не рекомендуется.

## 13. Events, attached properties и bindings

Текущая plugin schema не описывает события. Встроенная interaction system ориентирована на известные host events/actions. Поэтому Eremex-specific events нельзя автоматически подключить только через `IControlDescriptor`.

Нужны отдельные descriptors:

```text
DesignerEventDescriptor
DesignerAttachedPropertyDescriptor
DesignerBindingDescriptor
DesignerCollectionDescriptor
DesignerCommandDescriptor
```

Для каждого нужны type, owner, read/write semantics, editor, serialization strategy и export syntax.

`ItemsSource` можно поддержать рано, потому что binding sources уже доступны через `IPreviewContext`/`IXamlExportContext`. Vendor-specific collection view, grouping/filtering API должны находиться в adapter.

## 14. Лицензирование

Лицензия Eremex локально не найдена и не исследована. Поэтому нельзя подтверждать:

- право распространять DLL внутри Designer;
- право копировать DLL в generated project;
- право загружать control в designer process;
- наличие design-time/runtime license checks;
- право генерировать source/AXAML, использующий Eremex API;
- допустимость CI/Validate Build без интерактивной активации.

Текущий installer копирует все `.dll`, `.json`, `.pdb`, `.xml` из выбранной plugin folder. Для коммерческих vendor packages это может нарушить redistribution terms. До legal review этот механизм нельзя автоматически применять к Eremex package.

Рекомендуемая продуктовая модель до подтверждения EULA:

```text
Bring Your Own License / Bring Your Own Package

Designer не распространяет Eremex.
Пользователь устанавливает vendor package и имеет действующую лицензию.
Adapter хранит только metadata и генерирует dependency declaration.
Generated project восстанавливает package из источника пользователя.
```

Это рекомендация, а не подтверждённое условие лицензии.

## 15. Performance и security risks

### Performance

- Reflection по большой vendor assembly может быть дорогим.
- GridControl может создавать тяжёлое visual tree на Designer Canvas.
- Runtime XAML compilation для каждого изменения может быть заметной.
- Большие property collections нельзя пересобирать целиком на каждое изменение.
- Plugin scanner сейчас инспектирует все dependency DLL как потенциальные plugins.

Меры:

- metadata cache по assembly MVID/version/hash;
- lazy control discovery;
- curated descriptors вместо показа всех properties;
- deferred Preview creation;
- virtualization для collection editors;
- debounce AXAML Preview;
- plugin manifest с одной entry assembly.

### Security

Загрузка designer plugin выполняет чужой managed code в процессе Designer. `Activator.CreateInstance` и `Register(...)` не sandboxed. Eremex adapter и любые пользовательские plugins должны считаться trusted code.

Дополнительные риски:

- static constructors при runtime reflection/instance creation;
- native DLL loading;
- malicious resource URI;
- secrets в custom properties/export code;
- arbitrary startup code contribution;
- dependency confusion через NuGet source.

Metadata-only discovery безопаснее runtime loading и должен использоваться до активации plugin.

## 16. Рекомендуемая архитектура

### Решение

Не добавлять Eremex в ядро через `if (type == ...)`. Создать plugin API v2 и отдельный `EremexDesignerPlugin`.

### Слои

```text
Designer core
  ├─ Control metadata discovery
  ├─ Generic property metadata/editors
  ├─ Preview/export contribution contracts
  ├─ Dependency/resource manifest
  └─ Missing control/relink support

EremexDesignerPlugin
  ├─ Eremex control catalog
  ├─ Curated property descriptors
  ├─ Specialized collection editors
  ├─ Legacy Preview factories
  ├─ Runtime AXAML resolver contribution
  ├─ AXAML exporters
  └─ Package/theme/startup/license diagnostics
```

### Reflection и adapter

Reflection отвечает за:

- assembly inventory;
- public candidate controls;
- inheritance;
- simple CLR/Avalonia property metadata;
- enum options;
- availability diagnostics.

Adapter отвечает за:

- allowlist controls;
- display names/categories/icons/defaults;
- hiding unsafe/useless properties;
- complex editors;
- Preview factory;
- AXAML/C# generation;
- package/resource/startup contributions;
- vendor compatibility and license messages.

## 17. Минимальный PoC

### Выбор контрола

Выбрать **самый простой public Eremex Avalonia control**, который:

- не требует columns/items/template collections;
- имеет parameterless constructor или документированную factory;
- имеет 2–3 scalar vendor-specific properties;
- может отображаться без external data source;
- имеет документированный theme setup.

Конкретное имя нельзя выбрать без package/docs.

### Scope PoC

1. Получить и зафиксировать package/version/license assumptions.
2. Создать `EremexDesignerPlugin` вне core.
3. Зарегистрировать один descriptor в Category `Eremex`.
4. Проверить Drag & Drop и default size.
5. Показать Name, Width, Height, IsVisible и 2–6 vendor properties.
6. Создать реальный instance в legacy Preview.
7. Сгенерировать AXAML tag и xmlns.
8. Добавить package/theme contribution через минимальный API v2 draft.
9. Сгенерировать isolated project.
10. Выполнить `dotnet restore`, `dotnet build` и ручной runtime launch.
11. Отдельно проверить AXAML Preview from string.
12. Зафиксировать license/runtime diagnostics.

### Критерии успеха PoC

- стандартные Avalonia controls работают без изменений;
- Eremex отображается отдельной группой;
- control создаётся и переживает save/reload;
- 5–10 свойств редактируются и экспортируются;
- legacy Preview показывает реальный control;
- exported project содержит verified PackageReference/resources;
- generated project build/run проходит;
- missing package приводит к placeholder, а не crash;
- license error показана пользователю без обхода защиты.

### Что не включать в первый PoC

- GridControl columns/bands;
- nested templates;
- event designer;
- auto-discovery всех Eremex controls;
- redistribution vendor binaries;
- full AXAML Preview parity для всего family.

## 18. Оценка сложности

| Работа | Сложность | Подсистемы | Основные риски |
|---|---|---:|---|
| Простой control в Toolbox + DnD | Low–Medium | 3–4 | package compatibility, creation API |
| 5–10 simple properties | Medium | 3–5 | metadata mapping, validation, defaults |
| Legacy Preview | Medium | 3–4 | themes, init, license, ALC |
| AXAML Preview | High | 4–6 | runtime compiler assembly/resource resolution |
| AXAML export одного simple control | Medium | 4–5 | xmlns, property syntax, package metadata |
| Self-contained build/run export | High | 6–8 | PackageReference, resources, startup, licensing |
| GridControl basic columns/items | High | 7–10 | collection editors, bindings, serialization |
| GridControl bands/templates/summaries | High | 9–12 | complex object graph and vendor semantics |
| Полный Eremex plugin | High | 10+ | API coverage, version matrix, support burden |

Оценка в календарном времени не дана до host-level PoC и выбора поддерживаемой package line. Package/docs и `TextEditor` compile/activation probe уже получены, но self-contained Export и лицензированный visual render ещё не проверены.

## 19. Порядок реализации

1. Выполнено: получить official packages, EULA и licensing documentation.
2. Выполнено: составить compatibility matrix для `1.0.43`, `1.0.98` и `1.4.34`.
3. Выполнено: metadata probe controls/base types/properties/constructors/resources.
4. Выполнено частично: `TextEditor` adapter загружен реальным PluginLoader и `BuildPreview` создал control; визуальный host UI намеренно не изменялся.
5. Добавить plugin API v2 для dependencies/resources/startup/diagnostics.
6. Добавить project-level third-party dependency manifest и relink flow.
7. Добавить generic reflection property catalog с safety filter.
8. Подтвердить лицензированный visual render в legacy Preview.
9. Доработать runtime AXAML assembly/resource resolution.
10. Подтвердить self-contained export build/run.
11. Только после этого проектировать `DataGridControl` collection editors.
12. Расширять catalog контролов постепенно, с regression tests на стандартные Avalonia controls.

## 20. Финальный ответ на главный вопрос

**Да, Eremex можно добавить отдельной группой и не сломать стандартные Avalonia controls, но не как автоматически импортированную обычную DLL.**

Уже сейчас достаточно архитектуры для ограниченного PoC через вручную написанный adapter descriptor. Для полноценного продукта нужны расширения plugin contract, Property Inspector, dependency/resource export, runtime AXAML resolution и project dependency persistence.

Для простого control исходный код Eremex не нужен: это подтверждено `TextEditor` adapter compile/activation probe. Достаточно публичного NuGet API, theme package и действующей лицензии/trial. Для сложных collections нужно использовать только документированный public API либо официальный vendor adapter, без доступа к internals.

Первое практическое решение: **один `TextEditor`, один adapter plugin, один generated build/run test**. `DataGridControl` не должен быть первым host-level PoC.

## Приложение A. Проверенные точки в исходном коде

| Область | Файл / symbol | Наблюдение |
|---|---|---|
| Plugin contracts | `PluginContracts/DesignerContracts.cs` | API v1: descriptors, basic property schema, Preview/XAML hooks |
| Registry | `DesignerSystem/Infrastructure/DesignerRegistry.cs` | TypeKey registry и MissingPlugin fallback |
| Plugin loading | `DesignerSystem/Infrastructure/PluginLoading.cs` | Collectible ALC, dependency resolver, discovery только `IFormDesignerPlugin` |
| Built-in registration | `DesignerSystem/BuiltIn/BuiltInControlRegistrar.cs` | Built-ins зарегистрированы descriptors, но preview/export делегированы host bridges |
| Toolbox | `MainWindowViewModel.RefreshToolboxItemsFromRegistry()` | Toolbox строится из descriptors |
| Toolbox grouping exception | `MainWindowViewModel.ShouldShowInMainToolbox()` | Sample-specific TypeKey hardcode |
| Drag payload | `MainWindow.ToolboxItem_PointerPressed()` | Передаётся TypeKey |
| Drop | `MainWindow.DesignerCanvas_Drop()` | Общий drop flow |
| Model creation | `MainWindowViewModel.CreateDefaultControl()` | Defaults приходят из descriptor |
| Runtime model | `Models/DesignControlModel.cs` | Общие поля + JSON custom properties |
| File model | `Models/DesignerDocumentFileModel.cs` | PluginId/version/custom JSON сохраняются, package manifest отсутствует |
| Missing control | `DesignerSystem/Infrastructure/MissingPluginDescriptor.cs` | Graceful placeholder для отсутствующего descriptor |
| Inspector build | `MainWindowViewModel.BuildPropertyGridRows()` | Общие свойства собраны вручную |
| Plugin properties | `MainWindowViewModel.CreateDescriptorPropertyRow()` | Basic editor mapping; Collection сводится к text path |
| Inspector editor VM | `ViewModels/DescriptorPropertyEditorViewModel.cs` | Text/bool/number/enum/color UI paths |
| DLL data import | `DesignerSystem/Binding/ReflectionBindingMetadataProvider.cs` | Entity/table metadata, не UI-control metadata |
| Legacy designer preview | `MainWindow.CreatePreviewControl()` | `descriptor.BuildPreview(...)` |
| Legacy Preview Window | `PreviewWindow.CreatePreviewControl()` | Тот же descriptor hook для snapshot |
| Runtime AXAML load | `Services/RuntimeAxamlPreviewLoader.cs` | In-memory runtime XAML; только известные host assemblies preloaded |
| AXAML normalization | `Services/GeneratedAxamlService.cs` | Export/RuntimePreview root transformation |
| AXAML namespaces | `XamlExportContext.RegisterXmlNamespace()` | Сторонний prefix поддержан |
| AXAML export | `MainWindowViewModel.TryAppendControlXamlViaDescriptor()` | Descriptor output или placeholder в зависимости от runtime flag |
| Package discovery | `MainWindowViewModel.GetRequiredExportNuGetPackages()` | Список пакетов захардкожен под известные built-ins/data runtime |
| Project file | `ExportPipelineService.BuildProjectFile()` | Генерирует PackageReference только из host result |
| App resources | `ExportPipelineService.BuildAppXaml()` | Fluent + специальный DataGrid StyleInclude |
| Plugin install | `MainWindow.InstallPluginPackage()` | Копирует соседние DLL/JSON/PDB/XML; требует license-aware redesign |
