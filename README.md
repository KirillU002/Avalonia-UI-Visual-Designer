# Avalonia UI Visual Designer

**Текущая версия:** Alpha 3.0, development-ветка `main` · **Visual Studio Extension:** 0.1.16

Avalonia UI Visual Designer - визуальный дизайнер форм для Avalonia UI. Он позволяет создавать многоформенные проекты, редактировать интерфейс, настраивать источники данных, просматривать результат и экспортировать Avalonia-проект с AXAML и C#.

Доступны два сценария: **standalone Designer** с JSON-проектами и **Visual Studio Extension** для source-preserving редактирования существующих `.axaml`. Оба используют общий `DesignerSurface`; расширение запускает Designer в отдельном процессе.

![C#](https://img.shields.io/badge/C%23-.NET_8-blue?style=for-the-badge&logo=csharp)
![Avalonia](https://img.shields.io/badge/Avalonia-11.1.1-purple?style=for-the-badge)
![Status](https://img.shields.io/badge/status-Alpha_3.0-orange?style=for-the-badge)

## Статус проекта

Проект находится на стадии **Alpha** и активно развивается. Standalone-сценарии, Visual Studio bridge и AXAML Round-trip имеют smoke/regression tests, но проект пока не считается production-ready.

Текущая `main` включает миграцию на .NET 8 и AXAML Import Phase 2/3/4. Она новее зафиксированного standalone-релиза `v0.3.0-alpha`; нового продуктового тега для этого состояния пока нет. Visual Studio integration остаётся экспериментальной: это отдельное окно Designer, а не встроенный редактор Visual Studio.

## Что изменилось в Alpha 3.0

- Сохранены улучшения New Project, Multi Form, Property Inspector, DataGrid, DLL Import и соответствия Preview/Export.
- Export Pipeline изолирован от editor state; доступны Build Validation, diagnostics и smoke tests.
- Выделены общий `DesignerSurface`, document sessions и host services для standalone и Visual Studio.
- Добавлен VSIX bridge: активный AXAML из text buffer передаётся во внешний VsHost через Named Pipes.
- Реализованы AXAML Import, granular capabilities и minimal patches с сохранением исходной разметки.
- Phase 2/3 добавили вложенные layout containers, статические вкладки и отчёт о partial import.
- Phase 4 добавила native Expander и ComboBox: Content hierarchy, static items, безопасные Inspector edits и minimal patches.
- Designer, VsHost, plugins, sample и smoke runner переведены на .NET 8 без изменения версий NuGet-пакетов. VSIX сохраняет совместимый `net472`.

## Основные возможности

| Область | Что уже доступно |
| --- | --- |
| Visual Designer | Canvas, Toolbox, selection, Drag & Drop, перемещение, resize, Property Inspector, Undo/Redo |
| Проекты | New/Open/Save, JSON-формат `.formdesigner.json`, Multi Form, вкладки и Project Explorer |
| Preview и Export | Preview, генерация AXAML/C#, Export Pipeline, экспорт Avalonia-проекта и Build Validation через restore/build |
| Данные | DataGrid Designer и Column Editor, Binding Source, SQL Data Source, DLL Import метаданных источников данных |
| Расширение Designer | Plugin contracts, demo/minimal plugins, собственные properties, preview и export plugin controls |
| Eremex | Отдельные vertical slices TextEditor и DataGridControl, не весь каталог controls |
| Visual Studio | Открытие активного `.axaml`, внешний VsHost, Apply в VS buffer, ACK, version/checksum conflict protection |
| AXAML Round-trip | Частичное редактирование поддерживаемых элементов, сохранение opaque syntax, source map и minimal text edits |
| Диагностика | Problems/Output/Trace, AXAML Import Report, capability/blocker analysis и smoke/regression suites |

Возможности standalone Toolbox/Export и AXAML Import различаются. Например, наличие DataGrid Designer не означает поддержку импорта произвольного DataGrid из AXAML.

## Архитектура

Standalone:

```text
Designer UI
    ↓
shared DesignerSurface
    ↓
ViewModels / Services
    ↓
Project Model (JSON)
    ↓
Export Pipeline
    ↓
Generated Avalonia Project (AXAML + C#)
```

Visual Studio:

```text
Visual Studio: active Text Buffer
    ↓
VSIX bridge (.NET Framework 4.7.2)
    ↓ IPC / Named Pipes
AvaloniaDesigner.VsHost.exe (.NET 8)
    ↓
shared DesignerSurface / DesignerDocumentSession
    ↓
AXAML Round-trip
    ↓ minimal patch + version/checksum validation
Visual Studio Text Buffer (modified, без автоматического сохранения)
```

VSIX получает текст открытого документа, включая несохранённые изменения. Avalonia, Eremex и визуальные assemblies Designer работают **в VsHost, не в `devenv.exe`**. Общий [DesignerSurface](Docs/DesignerSurfaceArchitecture.md) и [document session](Docs/DesignerDocumentSessionArchitecture.md) используются обоими hosts; JSON-проекты и AXAML Round-trip имеют разные lifecycle.

## AXAML Import / Round-trip

Открытый в Visual Studio `.axaml` импортируется в editable projection. Исходный текст и source map сохраняются; Apply формирует **minimal text edits**, а не полную XML-регенерацию. Комментарии, unknown attributes/elements, Styles, Resources и Bindings остаются в исходной разметке. Apply без изменений сохраняет текст идентичным входному.

Текущая поддержка:

| AXAML | Поддержка |
| --- | --- |
| Window / UserControl | Корень документа и поддерживаемые свойства формы |
| Canvas; Button, TextBox, TextBlock, CheckBox, Border | Editable projection; Canvas-сценарий включает добавление, move/resize и редактирование поддерживаемых properties |
| Grid, StackPanel, DockPanel | Вложенное дерево, native layout projection; Grid definitions/row/column/spans, StackPanel orientation/spacing, Dock semantics |
| WrapPanel, ScrollViewer | Native wrapping, orientation; один Content child и scrollbar visibility |
| TabControl / TabItem | Статические вкладки, Header и SelectedIndex; содержимое всех страниц сохраняется, отображается выбранная |
| Expander | Native Header/Content, IsExpanded, ExpandDirection; закрытое дочернее дерево сохраняется в модели |
| ComboBox / ComboBoxItem | Статические items, SelectedIndex, PlaceholderText и literal item Content; bindings остаются preserved, без mock DataContext |
| Styles, Resources, Templates, Bindings, неизвестная syntax | Source-preserved / opaque; без визуального редактора и без выполнения пользовательского DataContext |

Capability model разделяет **document → element → property**: supported части доступны для редактирования, unsupported сохраняются. Unknown attribute не блокирует остальные свойства элемента; unknown sibling не блокирует поддерживаемых соседей. Поддерево неизвестного контейнера остаётся opaque, когда его semantics нельзя безопасно спроецировать. Read-only применяется при невозможности безопасного разбора/patch, а не просто из-за partial support. В режиме частичной поддержки кнопка **«Подробнее»** открывает AXAML Import Report с counts, paths, причинами и blockers.

На настоящем [`Views/MainWindow.axaml`](Views/MainWindow.axaml) Phase 4 импортирует **1439 / 1473 visual candidates (97.69%)**: 2507 XML elements, 34 opaque/skipped visual candidates, 34 skipped subtree boundaries. Это **coverage модели/source map одного stress-test документа**, не процент поддержки всей Avalonia, не поддержка всех properties и не полная visual equivalence. Native Expander/ComboBox проверены на shared surface; данные подтверждены `AxamlPhase4RealMainWindow`. Подробнее: [Phase 4 report](Docs/AxamlCoveragePhase4.md), [Phase 3 baseline](Docs/AxamlCoveragePhase3.md) и [проверка после .NET 8 migration](Docs/Net8Migration.md).

Перед применением patch проверяются version/checksum текущего VS buffer. При внешнем изменении source patch отклоняется; после успешного Apply документ остаётся dirty и сохраняется обычным `Ctrl+S`. ACK обновляет snapshot для следующего редактирования.

## Eremex integration

Plugin реализует vertical slices **TextEditor** и **DataGridControl**: реальные controls в preview, Property Inspector, JSON persistence и export. Используются `Eremex.Avalonia.Controls` и `Eremex.Avalonia.Themes.DeltaDesign` **1.0.98** с Avalonia **11.1.1**; нужна соответствующая лицензия/trial Eremex.

Поддержка ограничена реализованными API. Advanced DataGrid features и остальной Eremex catalog не заявляются; импорт Eremex controls из существующего AXAML пока opaque. Подробнее: [DataGridControl Phase 1](Docs/EremexDataGridControlPhase1.md) и [список properties](Docs/EremexDataGridControlProperties-1.0.98.md). TFM и результаты проверки текущего net8 host приведены отдельно в [Net8Migration](Docs/Net8Migration.md).

## Версии

Текущая разработка: **Alpha 3.0 (`main`)**, VSIX **0.1.16**. Ниже сохранены ссылки на **две предыдущие зафиксированные версии**, чтобы можно было посмотреть проект до текущих изменений.

## Alpha 3.0

[Alpha 3.0 / `v0.3.0-alpha`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.3.0-alpha) - предыдущий standalone-релиз со стабилизацией конструктора, Export, DataGrid и DLL Import. Это исторический release, не сборка текущей `main` с .NET 8 и новым AXAML Import.

## Alpha 2.0

Alpha 2.0 (`0.2.0-alpha`) - историческое состояние до стабилизационного цикла Alpha 3.0. Отдельного тега для этой ссылки нет; версия доступна по прежнему коммиту:

[`29c5864908912c10713498b30def81af00ee4355`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/commit/29c5864908912c10713498b30def81af00ee4355)

## Требования

- .NET 8 SDK или более новый совместимый SDK; для запуска Designer/VsHost требуется .NET 8 runtime.
- Текущая проверенная desktop/VSIX-конфигурация - Windows. Avalonia-проекты используют `net8.0`, без обязательного `-windows`; другие ОС не заявляются проверенными.
- Для Visual Studio Extension - Visual Studio 2022 Community x64. Для сборки VSIX нужен .NET Framework 4.7.2 targeting/developer pack; bridge использует текущий VSSDK.
- Зависимости восстанавливаются через NuGet: Avalonia **11.1.1**, CommunityToolkit.Mvvm **8.2.1**, Eremex **1.0.98** для plugin. Обновление их версий не требуется для .NET 8.

| Проекты / output | Target Framework |
| --- | --- |
| FormDesigner, VsHost, PluginContracts, plugins, smoke runner, sample и plugin template | `net8.0` |
| AvaloniaDesigner.Host.Protocol | `netstandard2.0;net8.0`: совместимая сторона для VSIX и modern сторона для VsHost |
| AvaloniaDesigner.VSIX | `net472`: in-process AsyncPackage/VSSDK внутри Visual Studio |
| Generated export projects по текущему default | `net6.0`, Avalonia `11.1.1`; не мигрируются автоматически вместе с Designer |

Для запуска generated net6 exports нужен соответствующий runtime. Подробнее о совместимости и результатах миграции: [Net8Migration](Docs/Net8Migration.md).

Проверить SDK:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

## Быстрый запуск

```powershell
git clone https://github.com/KirillU002/Avalonia-UI-Visual-Designer.git
cd Avalonia-UI-Visual-Designer
dotnet restore .\FormDesigner.sln
dotnet build .\FormDesigner.sln
dotnet run --no-build --project .\FormDesigner.csproj
```

Если нужен только standalone Designer без VSIX tooling:

```powershell
dotnet build .\FormDesigner.csproj
dotnet run --no-build --project .\FormDesigner.csproj
```

### Visual Studio Extension

После Debug-сборки solution пакет **0.1.16** находится в:

```text
AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix
```

1. Закройте Visual Studio и установите `.vsix` в нужный instance Visual Studio 2022 Community.
2. Перезапустите Visual Studio, откройте `.axaml` и выберите **Tools → Open in Avalonia UI Visual Designer** (Tools / «Средства»).
3. Измените поддерживаемые properties в отдельном VsHost, нажмите **«Применить изменения»**, проверьте VS buffer и сохраните документ через `Ctrl+S`.

VsHost и его runtime dependencies входят в `VsHost/` внутри VSIX; на машине запуска нужен .NET 8 runtime. Для первой проверки есть [SimpleAvaloniaApp](Samples/VisualStudioPoC/README.md). Интеграция развивается, поэтому после обновления пакета рекомендуется повторить этот ручной сценарий; автоматические IPC/packaging tests не заменяют проверку загрузки в живом Visual Studio.

### Smoke tests

Полный runner через существующий скрипт (Release):

```powershell
.\smoke-tests\run-smoke-tests.ps1
```

Целевой AXAML-набор (Debug):

```powershell
$env:SMOKE_SCENARIO_FILTER = "Axaml"
dotnet run -c Debug --project .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj
Remove-Item Env:SMOKE_SCENARIO_FILTER
```

Runner содержит AXAML/layout/source-preservation, DesignerSurface, VSIX/VsHost/IPC, JSON/Multi Form, Preview/Export и Eremex suites. Для generated-project build scenarios требуется NuGet restore. Результаты последней целевой проверки и её границы, включая непроверенный полный Release-прогон, перечислены в [Net8Migration](Docs/Net8Migration.md).

## Документация

- [DeveloperArchitecture](Docs/DeveloperArchitecture.md) - техническая документация для разработчиков.
- [DesignerSurface](Docs/DesignerSurfaceArchitecture.md), [document sessions](Docs/DesignerDocumentSessionArchitecture.md) и [host services](Docs/DesignerHostServicesArchitecture.md).
- [AXAML Round-trip architecture](Docs/AxamlRoundTripArchitecture.md), [granular capabilities](Docs/AxamlGranularCapabilities.md), [Phase 3 coverage](Docs/AxamlCoveragePhase3.md) и [Phase 4: Expander / ComboBox](Docs/AxamlCoveragePhase4.md).
- [Visual Studio fixture / ручная проверка](Samples/VisualStudioPoC/README.md).
- [Alpha 0.2 manual checklist](Docs/ALPHA_0_2_MANUAL_TEST_CHECKLIST.md) - исторический checklist standalone-сценариев.
- [Plugin guide](Docs/PluginGuide.md)
- [Контролируемая миграция .NET 8](Docs/Net8Migration.md)
- [Undo/Redo smoke checklist](Docs/UndoRedoSmokeTest.md)

## Ограничения и roadmap

- AXAML Import не воспроизводит весь Avalonia API. ItemsControl, ListBox, TreeView, DataGrid и custom/Eremex controls пока не входят в поддержанный import subset. ComboBox поддерживает простой static Items slice, не полный Selector/ItemsControl framework.
- Bindings, Styles, Resources и Templates сохраняются, но не вычисляются и не редактируются специальными визуальными редакторами. Arbitrary assemblies/plugins открытого VS solution не загружаются.
- Изменение hierarchy, insertion и destructive operations ограничиваются безопасными source-mapped сценариями. Partial import не гарантирует runtime-equivalent внешний вид.
- Следующее расширение import coverage выбирается по blocker impact реальных документов; после Phase 4 среди оставшихся типов текущего MainWindow наиболее часто встречается ItemsControl. Дальнейшие направления: стандартные Items/Selector controls, расширение безопасных patches и hardening Visual Studio lifecycle.

## Примечания

Архитектура, ключевые классы, flows, diagnostics и правила разработки описаны в [DeveloperArchitecture](Docs/DeveloperArchitecture.md).

JSON project → Export и existing AXAML → minimal patch - разные workflows. Экспортируемые проекты сохраняют текущий default `net6.0` / Avalonia `11.1.1`; переход самого Designer на .NET 8 не меняет уже созданные пользовательские проекты. Проект может содержать Alpha-баги; перед редактированием важных AXAML используйте version control.
