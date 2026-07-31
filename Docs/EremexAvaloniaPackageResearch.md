# Проверка Eremex NuGet package для Avalonia

Дата проверки: 14 июля 2026 года.

## Краткий вывод

Конкретный основной пакет называется **`Eremex.Avalonia.Controls`**.

- Текущая проверенная версия пакета: **`1.4.34`**.
- Она собрана для **`.NET 8`** и требует **Avalonia `12.0.2`**.
- Текущий Avalonia UI Visual Designer собран для **`.NET 6` / Avalonia `11.1.5`**, поэтому `1.4.34` нельзя загружать в него как обычный plugin dependency без обновления host application до Avalonia 12.
- Для текущего Designer подтверждена версия **`1.0.98`**: `.NET 6`, assembly references Avalonia `11.1.1`, а реальный visual render с host Avalonia `11.1.5` проходит после загрузки Eremex runtime assemblies в Default context.
- `1.0.43` не следует использовать: она ссылается на Avalonia `11.0.10` и не проходит compatibility gate текущего host.
- Полноценный self-contained Export пока блокирует не Eremex API, а текущий plugin contract: он не умеет объявлять NuGet dependencies, App styles/resources и startup/build requirements.

Официальные страницы:

- [`Eremex.Avalonia.Controls 1.4.34`](https://www.nuget.org/packages/Eremex.Avalonia.Controls/1.4.34)
- [`Eremex.Avalonia.Controls 1.0.98`](https://www.nuget.org/packages/Eremex.Avalonia.Controls/1.0.98)
- [Getting Started with Eremex Controls](https://eremexcontrols.net/get-started/get-started-with-emx-controls/)
- [Eremex themes](https://eremexcontrols.net/controls/themes/)
- [Eremex licensing](https://eremexcontrols.net/licensing/)
- [Eremex 1.4 version history](https://eremexcontrols.net/version-history/version-1.4/)

## 1. Идентификация пакетов

### Текущая линия

| Назначение | PackageId | Версия | Assembly target | Avalonia dependency |
|---|---|---:|---|---:|
| Основные controls | `Eremex.Avalonia.Controls` | `1.4.34` | `net8.0` | `12.0.2` |
| Тема | `Eremex.Avalonia.Themes.DeltaDesign` | `1.4.34` | DLL `net8.0` | `12.0.2` |

Фактический `.nupkg` `Eremex.Avalonia.Controls 1.4.34` содержит:

- `Eremex.Avalonia.Controls.dll`;
- `Eremex.Avalonia.Charts.dll`;
- `Eremex.Avalonia.Icons.dll`;
- локализованные resource assemblies;
- `Eremex.Avalonia.Controls.targets`;
- build tasks для лицензии и usage collection;
- `eula.txt`.

Прямые dependencies из `.nuspec` версии `1.4.34`:

- `Avalonia 12.0.2`;
- `Avalonia.Fonts.Inter 12.0.2`;
- `Svg.Controls.Skia.Avalonia 12.0.0.5`;
- `DynamicData 8.1.1`;
- `Microsoft.CodeAnalysis.CSharp.Scripting 4.3.0`;
- `SkiaSharp 3.119.4-preview.1.1`;
- `System.Resources.Extensions 6.0.0`;
- `Eremex.DocumentProcessing 1.4.34`;
- `Eremex.Common.Contracts 1.4.34`.

Примечание по package metadata: у theme package `1.4.34` dependency group в `.nuspec` обозначен как `net6.0`, но сама theme DLL и основной controls package собраны для `net8.0`. Для выбора host target нужно ориентироваться на реальные assemblies и основной package: это `.NET 8`.

### Совместимая линия для текущего Designer

`Eremex.Avalonia.Controls 1.0.98` содержит `net6.0` assemblies и ссылается на Avalonia `11.1.1`. Версия проверена реальным `ApplyTemplate`/layout-прогоном со следующими ссылками:

```xml
<PackageReference Include="Avalonia" Version="11.1.5" />
<PackageReference Include="Avalonia.Desktop" Version="11.1.5" />
<PackageReference Include="Eremex.Avalonia.Controls" Version="1.0.98" />
<PackageReference Include="Eremex.Avalonia.Themes.DeltaDesign" Version="1.0.98" />
```

Результат: restore, build, theme construction, attach to visual tree, `ApplyTemplate` и layout завершились успешно.

## 2. Avalonia/.NET compatibility

| Eremex | TFM | Минимальная Avalonia из `.nuspec` | Совместимость с Designer сейчас |
|---:|---|---:|---|
| `1.4.34` | `net8.0` | `12.0.2` | Нет: Designer использует `net6.0` / Avalonia `11.1.5` |
| `1.0.43` | `net6.0` | `11.0.10` | Нет: несовместимая minor line для host `11.1.5`; registration blocked by compatibility gate |
| `1.0.98` | `net6.0` | `11.1.1` | Да: real visual render проверен с Avalonia `11.1.5` |

В `PluginLoadContext` host assemblies переиспользуются по простому assembly name. Поэтому plugin, собранный с Avalonia 12, нельзя считать изолированным от Avalonia 11 host: это не поддерживаемый side-by-side сценарий.

## 3. Реальный список public controls

Для `1.4.34` DLL были загружены в отдельном `.NET 8 / Avalonia 12.0.2` probe. Критерий сканирования:

```text
type.IsPublic
&& !type.IsAbstract
&& typeof(Avalonia.Controls.Control).IsAssignableFrom(type)
```

Результат:

| Assembly | Public concrete Control types |
|---|---:|
| `Eremex.Avalonia.Controls.dll` | 238 |
| `Eremex.Avalonia.Charts.dll` | 29 |
| `Eremex.Avalonia.Icons.dll` | 0 |
| `Eremex.Avalonia.Themes.DeltaDesign.dll` | 0 |

Полный механический реестр с base type и наличием public parameterless constructor находится в [EremexAvaloniaPublicControls-1.4.34.csv](EremexAvaloniaPublicControls-1.4.34.csv).

Число 267 не равно числу Toolbox-компонентов. DLL публично экспортирует также template parts и служебные controls из namespaces `Internal`, `Visuals`, `Native`, `Customization` и `TrialWatermark`. Их нельзя автоматически добавлять в Toolbox только по признаку `public`.

### Public product-surface types

Ниже приведены public concrete `Control`-типы из продуктовых namespaces. Служебные namespaces `Internal`, `Visuals`, `Native`, `Customization` и `TrialWatermark` исключены, но некоторые helper types внутри продуктовых namespaces всё равно требуют adapter-фильтрации.

**DataGrid**

- `Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl`

**TreeList / TreeView**

- `TreeListControl`
- `TreeViewControl`

**ListView**

- `ListViewControl`
- `ListViewGroupControl`
- `ListViewItemControl`

**PropertyGrid**

- `PropertyGridControl`
- `PropertyGridCategoryRow`
- `PropertyGridRow`
- `PropertyGridTabRow`
- `PropertyGridTabRowItem`

**Editors and layout controls**

- `BaseEditor`
- `ButtonEditor`
- `ButtonEditorProperties`
- `ButtonSettings`
- `CalendarControl`
- `CheckEditor`
- `CheckEditorProperties`
- `ColorEditor`
- `ComboBoxEditor`
- `ComboBoxEditorProperties`
- `DateEditor`
- `DateEditorProperties`
- `GroupBox`
- `HyperlinkEditor`
- `HyperlinkEditorProperties`
- `MemoEditor`
- `MemoEditorProperties`
- `PopupColorEditor`
- `PopupColorEditorProperties`
- `PopupEditor`
- `PopupEditorProperties`
- `SegmentedEditor`
- `SegmentedEditorProperties`
- `SpinEditor`
- `SpinEditorProperties`
- `SplitContainerControl`
- `TextEditor`
- `TextEditorProperties`

**Common / tabs**

- `CircleProgressIndicator`
- `ProgressIndicator`
- `MxSplitButton`
- `MxVirtualizingControl`
- `MxWindow`
- `ResizeablePopup`
- `MxTabControl`
- `MxTabItem`
- `MxTabStrip`
- `MxTabStripItem`
- `ShadowControl`

**Bars**

- `PopupContainer`
- `PopupMenu`
- `Toolbar`
- `ToolbarButtonBaseItem`
- `ToolbarButtonItem`
- `ToolbarCheckItem`
- `ToolbarCheckItemGroup`
- `ToolbarContainerControl`
- `ToolbarContainerLayoutPanel`
- `ToolbarControlDragThumb`
- `ToolbarEditorItem`
- `ToolbarItem`
- `ToolbarItemDragWindow`
- `ToolbarItemGroup`
- `ToolbarItemImage`
- `ToolbarLayoutPanel`
- `ToolbarManager`
- `ToolbarMenuItem`
- `ToolbarSeparatorItem`
- `ToolbarTextItem`

**Docking**

- `AutoHideGroup`
- `DockGroup`
- `DockManager`
- `DockPane`
- `DocumentGroup`
- `DocumentPane`
- `FloatGroup`
- `TabbedGroup`

**Ribbon**

- `RibbonControl`
- `RibbonGalleryItem`
- `RibbonPage`
- `RibbonPageGroup`
- `RibbonPopupGallery`

**Charts**

- `CartesianChart`, `CartesianSeries`
- `PolarChart`, `PolarSeries`
- `SmithChart`, `SmithSeries`
- `Heatmap`
- `AxisX`, `AxisY`
- `HeatmapAxisX`, `HeatmapAxisY`
- `PolarAxisX`, `PolarAxisY`
- `SmithAxisX`, `SmithAxisY`
- `ConstantLine`
- `CrosshairOptions`
- `CrosshairAllSeriesLabelControl`
- `CrosshairSingleSeriesLabelControl`
- `Strip`

## 4. XML namespaces

Следующие mappings получены из реальных `XmlnsDefinitionAttribute` / `XmlnsPrefixAttribute` в assemblies `1.4.34`:

| Prefix | XML namespace | CLR namespace |
|---|---|---|
| `mx` | `https://schemas.eremexcontrols.net/avalonia` | `Eremex.AvaloniaUI.Controls`, `.Common` |
| `mxb` | `https://schemas.eremexcontrols.net/avalonia/bars` | `.Controls.Bars` |
| `mxdg` | `https://schemas.eremexcontrols.net/avalonia/datagrid` | `.Controls.DataGrid` |
| `mxd` | `https://schemas.eremexcontrols.net/avalonia/docking` | `.Controls.Docking` |
| `mxe` | `https://schemas.eremexcontrols.net/avalonia/editors` | `.Controls.Editors` |
| `mxlv` | `https://schemas.eremexcontrols.net/avalonia/listview` | `.Controls.ListView` |
| `mxpg` | `https://schemas.eremexcontrols.net/avalonia/propertygrid` | `.Controls.PropertyGrid` |
| `mxr` | `https://schemas.eremexcontrols.net/avalonia/ribbon` | `.Controls.Ribbon` |
| `mxtl` | `https://schemas.eremexcontrols.net/avalonia/treelist` | `.Controls.TreeList` |
| `mxc` | `https://schemas.eremexcontrols.net/avalonia/charts` | `Eremex.AvaloniaUI.Charts` |
| `mxi` | `https://schemas.eremexcontrols.net/avalonia/icons` | `Eremex.AvaloniaUI.Icons`, `.Other` |

Пример реального DataGrid tag из официального API:

```xml
<mxdg:DataGridControl
    ItemsSource="{Binding Employees}"
    AutoGenerateColumns="True" />
```

Документация: [DataGrid data binding](https://eremexcontrols.net/controls/datagrid/data-binding/).

## 5. Themes and resources

Theme package обязателен для нормального визуального результата:

```xml
<Application
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:theme="clr-namespace:Eremex.AvaloniaUI.Themes.DeltaDesign;assembly=Eremex.Avalonia.Themes.DeltaDesign">
  <Application.Styles>
    <theme:DeltaDesignTheme />
  </Application.Styles>
</Application>
```

Официальная документация предупреждает, что без Eremex theme controls могут отображаться пустыми. Тот же `DeltaDesignTheme` присутствует в официальном template package.

Проверенные ресурсы:

- `Eremex.Avalonia.Controls.dll`: embedded `!AvaloniaResources` и localization resources;
- `Eremex.Avalonia.Icons.dll`: embedded `!AvaloniaResources`;
- `Eremex.Avalonia.Themes.DeltaDesign.dll`: embedded `!AvaloniaResources` и palette resources.

Reflection показал, что `DeltaDesignTheme` имеет public constructor:

```text
DeltaDesignTheme(IServiceProvider)
```

Поэтому AXAML loader создаёт theme через service provider. Preview factory, подключающий theme программно, должен учитывать этот constructor; простой `Activator.CreateInstance(themeType)` без arguments завершится `MissingMethodException`.

## 6. Startup initialization

В официальном template и Getting Started нет отдельного `UseEremex()`, global static initializer или обязательного startup method. Фактическая startup-конфигурация состоит из:

1. NuGet references на controls и theme packages.
2. `DeltaDesignTheme` в `Application.Styles`.
3. По желанию/шаблону Eremex `MxWindow` как root window для полной поддержки визуальных возможностей Eremex window theme.
4. Автоматического MSBuild target из controls package для license data.

Это не означает, что любой сложный control не требует собственной model/configuration. Например, DataGrid columns, Ribbon pages и Docking groups являются отдельным adapter scope.

## 7. Licensing requirements

Проверено по package metadata, build target, EULA внутри `.nupkg` и [официальной licensing documentation](https://eremexcontrols.net/licensing/):

- `Eremex.Avalonia.Controls` имеет `requireLicenseAcceptance=true`.
- Лицензия предоставляется на разработчика.
- Trial действует 60 календарных дней и показывает trial messages.
- Package MSBuild target запускает `LicenseDataWriterTask` до `CoreCompile`.
- Официальная документация описывает генерацию `emxLicense.cs` с runtime key.
- Runtime key зависит от assembly name и major version Eremex; переименование assembly или смена major требует нового ключа.
- В `1.4.34` build target дополнительно запускает `CollectEMXUsage` вне design-time build.
- Обход license checks не исследовался и не допускается.

Практическое следствие для Designer:

- Designer не должен автоматически распространять Eremex package/DLL как собственный компонент.
- Пользователь должен сам принять EULA и иметь trial/действующую лицензию.
- Generated project должен получать обычный `PackageReference`, чтобы vendor build targets выполнились штатно.
- Право на распространение готового приложения и binaries нужно проверять по актуальной EULA/договору пользователя; этот отчёт не даёт юридического разрешения на redistribution.

## 8. Проверка простого adapter plugin

Финальная проверка выполнена для `Eremex.Avalonia.Controls 1.0.98`, потому что это совместимая линия с текущим Avalonia 11.1 host.

### Runtime activation probe

В инициализированном Avalonia `11.1.5` process выполнено:

```text
Activator.CreateInstance(Eremex.AvaloniaUI.Controls.Editors.TextEditor)
```

Результат:

```text
PROBE_SUCCESS
type=Eremex.AvaloniaUI.Controls.Editors.TextEditor
Width=240
Height=36
Watermark=Name
EditorValue=Alice
```

Для `TextEditor 1.0.98` adapter использует только проверенный subset Eremex-specific properties:

- `EditorValue`;
- `Watermark`;
- `ReadOnly`;
- `Mask`, `MaskType`, `MaskCulture`;
- `DisplayFormatString`;
- `EditorMode`;
- `TextWrapping`;
- `ValidateOnInput`;
- `ErrorText`, `ErrorShowMode`;
- `SelectionStart`, `SelectionEnd`.

### Реальный plugin contract probe

Отдельный временный `net6.0` class library:

- ссылается на `PluginContracts/FormDesigner.PluginContracts.csproj`;
- реализует `IFormDesignerPlugin`;
- регистрирует `IControlDescriptor` с category `Eremex`;
- описывает `Width`, `Height`, `EditorValue`, `Watermark`, `ReadOnly`, `Mask`;
- возвращает реальный `TextEditor` из `BuildPreview`;
- регистрирует `mxe` и пишет `<mxe:TextEditor>` в `AppendXaml`;
- ссылается на controls/theme packages `1.0.98`.
- включает `CopyLocalLockFileAssemblies=true`, чтобы Eremex managed/native dependencies находились рядом с plugin output и разрешались `AssemblyDependencyResolver`.

Результат build:

```text
Build succeeded.
Warnings: 0
Errors: 0
```

После build plugin folder был загружен реальным `FormDesigner.DesignerSystem.Infrastructure.PluginLoader`. Результат:

```text
PLUGIN_STATUS|Ok|errors=0|warnings=0
DESCRIPTOR|Eremex.TextEditor|Eremex|properties=6
PREVIEW_CONTROL|Eremex.AvaloniaUI.Controls.Editors.TextEditor|width=240|height=36
```

Без `CopyLocalLockFileAssemblies=true` обычная class-library сборка оставляла Eremex assemblies только в NuGet cache/deps graph и не копировала их рядом с plugin DLL. Для текущего folder-based plugin installer это недостаточно: package layout должен включать vendor dependencies и native runtime assets.

### Что это доказывает

Простой Eremex control можно загрузить текущим PluginLoader, зарегистрировать в category `Eremex`, описать для Property Inspector, создать через `BuildPreview` и записать в AXAML через существующий adapter plugin.

### Что это пока не доказывает

- Host export не добавит Eremex PackageReference автоматически: plugin API v1 не содержит dependency manifest.
- Host `App.axaml` не добавит `DeltaDesignTheme` автоматически: plugin API v1 не содержит resource/style contributions.
- Runtime AXAML Preview требует явного preload/resolution Eremex assemblies.
- Полный visual-tree render с коммерческим ключом не проверялся; host probe подтвердил создание control instance, но не является проверкой лицензированного production render.
- `DataGridControl`, columns, Ribbon, Docking и другие object graphs требуют специализированных descriptors/editors.

## 9. Итоговое решение

### Можно ли создать простой control через adapter plugin?

**Да, для текущего Designer на Avalonia 11.1 это подтверждено с `Eremex.Avalonia.Controls 1.0.98`.** Исходный код Eremex для этого не требуется: достаточно public NuGet API, theme package и законной лицензии/trial.

### Можно ли прямо сейчас использовать последнюю `1.4.34`?

**Нет, не в текущем Avalonia 11 host.** Сначала нужен переход Designer и PluginContracts на `.NET 8 / Avalonia 12.0.2+`, после чего adapter нужно пересобрать и повторить host-level Preview/Export tests.

### Минимальный безопасный PoC

1. Зафиксировать package line: `1.0.98` для текущего host или сначала обновить host до Avalonia 12.
2. Использовать `TextEditor`, не `DataGridControl`.
3. Добавить descriptor с 6 проверенными properties.
4. Подключить `DeltaDesignTheme` в preview host.
5. Расширить plugin contract декларациями `PackageReference` и `Application.Styles`.
6. Проверить generated project restore/build/run с лицензией пользователя.
7. Только после этого переходить к `DataGridControl` и collection properties.

## 10. Воспроизводимость исследования

Исследование выполнялось на реальных `.nupkg`, без decompilation и обращения к private API:

- inspection `.nuspec`, package files и MSBuild `.targets`;
- reflection public metadata;
- отдельный restore/build и visual render для `.NET 6 / Avalonia 11.1.5 / Eremex 1.0.98`;
- отдельный metadata probe для `.NET 8 / Avalonia 12.0.2 / Eremex 1.4.34`;
- process-level `Activator` test;
- compile test против реального `FormDesigner.PluginContracts` API;
- загрузка folder package через реальный `PluginLoader`, регистрация descriptor и вызов `BuildPreview`.

В репозиторий не добавлены Eremex binaries, package references или runtime implementation.
