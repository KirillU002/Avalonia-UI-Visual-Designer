# Avalonia UI Visual Designer — Beta 0.1

**Предварительная Beta-версия в активной разработке.** Продукт `0.1.0-beta.1` · Visual Studio Extension `0.1.18`.

[Скачать Beta 0.1](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1) · [Что нового](Docs/Releases/Beta-0.1.md)

Avalonia UI Visual Designer — визуальный конструктор пользовательских интерфейсов для Avalonia на платформе .NET. Он помогает проектировать формы без необходимости вручную писать всю AXAML-разметку: размещать компоненты, настраивать свойства, данные и поведение, просматривать результат и экспортировать готовый Avalonia-проект.

Проект ориентирован на разработчиков .NET и предлагает привычный подход к визуальному проектированию, похожий на Windows Forms Designer. Цель — упростить создание кроссплатформенных интерфейсов; готовые дистрибутивы этой Beta проверяются и поставляются **только для Windows x64**.

Доступны **Standalone Designer** для JSON-проектов и **Visual Studio Extension** для частичного source-preserving редактирования существующих `.axaml`. Оба используют общий DesignerSurface.

![C#](https://img.shields.io/badge/C%23-.NET_8-blue?style=for-the-badge&logo=csharp)
![Avalonia](https://img.shields.io/badge/Avalonia-11.1.1-purple?style=for-the-badge)
![Status](https://img.shields.io/badge/status-Beta_0.1-orange?style=for-the-badge)

## Статус проекта

Beta 0.1 — предварительный выпуск для знакомства с продуктом и обратной связи. Grid/Canvas, Multi Form, Preview и Export доступны; ограничения PostgreSQL, AXAML Import, плагинов и больших форм перечислены ниже. Перед работой с важными проектами сохраняйте резервную копию или используйте систему контроля версий.

[Release Notes](Docs/Releases/Beta-0.1.md) описывают возможности и известные проблемы. Подробные результаты автоматизированных проверок сохранены в [Beta Readiness Audit](Docs/BetaReadinessAudit.md) и [историческом отчёте RC](Docs/Beta01ReleaseCandidate.md).

JSON Designer и AXAML Import — разные workflows. Для JSON основным Preview является AXAML Preview; Legacy сохраняется. Visual Studio integration остаётся экспериментальной и открывает отдельное окно VsHost.

## Что изменилось в Beta 0.1

- **Grid-first:** новые проекты и формы используют настоящий Grid; Canvas доступен явно. Старые JSON без layout metadata сохраняют Canvas-семантику, без автоматической миграции.
- **Layout Designer:** Grid Definition Editor (Auto/Star/Pixel), ячейки Grid, nested StackPanel/DockPanel/WrapPanel/Canvas, визуальный Drop/Reparent, Undo/Redo.
- **Preview:** AXAML Preview по умолчанию для JSON, Legacy в Settings, согласованность layout/стандартных controls с экспортом и диагностика отсутствующих компонентов.
- **Сохранность данных:** preflight повреждённых документов/ControlId, защита async Save и Multi Form, recovery при I/O errors, отложенное восстановление и атомарная публикация autosave.
- **Undo/Redo:** атомарное удаление поддерева и отсутствие ложных правок BindingSource при refresh Inspector; кэш описаний истории.
- **Multi Form / DataGrid / Logic / Export:** проверены Save/Open, реальные строки SQL Server LocalDB, folder/ZIP export и запуск generated приложения с навигацией.
- **VSIX:** source-preserving AXAML patches, ACK/checksum/version protection, внешний VsHost с отдельной версией расширения.
- **PostgreSQL — experimental**, live acceptance не завершена. **Eremex — optional experimental plugin**, стандартные пакеты его не включают.
- Сохранены .NET 8 Designer/VsHost, `net472` bridge и текущие NuGet versions. Изменения предыдущей Alpha: [Alpha 4.0 Release Notes](Docs/Alpha4ReleaseNotes.md).

## Основные возможности

| Область | Что уже доступно |
| --- | --- |
| Visual Designer | Grid-first / Canvas, вложенные layouts, Grid Definition Editor, Toolbox Drag & Drop/Reparent, selection/resize, Property Inspector с Favorites, Undo/Redo |
| Проекты | New/Open/Save, JSON-формат `.formdesigner.json`, Multi Form, вкладки и Project Explorer, Autosave/Recovery |
| Поведение | Logic Editor, события компонентов и реализованные действия, включая открытие других форм |
| Preview и Export | AXAML Preview default для JSON, Legacy Preview, AXAML/C#/ViewModel, Multi Form, folder/ZIP, restore/build/runtime |
| Данные | DataGrid Designer и Column Editor, Binding Source, SQL Server / PostgreSQL Data Source, DLL Import метаданных источников данных |
| Расширение Designer | Plugin contracts, demo/minimal plugins, собственные properties, preview и export plugin controls |
| Eremex | Optional experimental TextEditor/DataGridControl plugin; отсутствует в стандартных пакетах |
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
| ItemsControl; ListBox / ListBoxItem | Статические visual items, ListBox SelectedIndex/SelectionMode и item Content; ItemsSource/SelectedItem bindings сохраняются |
| TreeView / TreeViewItem | Статическая вложенная hierarchy, Header/IsExpanded и SelectionMode; collapsed nodes остаются в модели |
| ProgressBar | Native Minimum/Maximum/Value, IsIndeterminate и Orientation; bound Value остаётся read-only на уровне свойства |
| Styles, Resources, Templates, Bindings, неизвестная syntax | Source-preserved / opaque; без визуального редактора и без выполнения пользовательского DataContext |

Capability model разделяет **document → element → property**: supported части доступны для редактирования, unsupported сохраняются. Unknown attribute не блокирует остальные свойства элемента; unknown sibling не блокирует поддерживаемых соседей. Поддерево неизвестного контейнера остаётся opaque, когда его semantics нельзя безопасно спроецировать. Read-only применяется при невозможности безопасного разбора/patch, а не просто из-за partial support. В режиме частичной поддержки кнопка **«Подробнее»** открывает AXAML Import Report с counts, paths, причинами и blockers.

Границы editable/opaque элементов и результаты source-preservation проверок описаны в [AXAML coverage](Docs/AxamlCoveragePhase5.md). Частичный импорт не означает выполнение пользовательского DataContext или полную визуальную эквивалентность исходному приложению.

Перед применением patch проверяются version/checksum текущего VS buffer. При внешнем изменении source patch отклоняется; после успешного Apply документ остаётся dirty и сохраняется обычным `Ctrl+S`. ACK обновляет snapshot для следующего редактирования.

## PostgreSQL — experimental

Реализованы настройки подключения, schema discovery, BindingSource, Preview и export. Полноценное live-тестирование на настоящем сервере ещё требуется; текущие результаты и acceptance checklist: [PostgreSQL Integration](Docs/PostgreSqlIntegrationPhase2.md).

Settings → PostgreSQL → Host/Port/Database/Username/Password → проверить и применить.
Затем Data → PostgreSQL → получить схемы → таблицы → колонки → выбрать BindingSource
у DataGrid. Поддерживаются обычный и AXAML Preview, SELECT/WITH queries, отмена и
лимит строк. Ошибки и пустые таблицы не подменяются демонстрационными данными.

Экспорт получает Npgsql только при использовании PostgreSQL. Каждая форма запускает
свой async loader; подключение задаётся через `FORMDESIGNER_POSTGRES_CONNECTION_STRING`
или отдельную переменную источника, указанную в README.generated.md. Пароль Designer
в экспорт не переносится. Локальное сохранение использует Windows DPAPI.

Реальный тестовый PostgreSQL отсутствует: успешный end-to-end с сервером пока не
подтверждён. Архитектура, безопасность, результаты проверок и перечень невыполненных
integration checks: [PostgreSQL Integration](Docs/PostgreSqlIntegration.md).

## Eremex — optional experimental plugin

Стандартный Designer работает без Eremex assemblies/themes. Плагин можно собрать отдельно при наличии лицензии/trial; поддержка не удалена.

Plugin реализует vertical slices **TextEditor** и **DataGridControl**: реальные controls в preview, Property Inspector, JSON persistence и export. Используются `Eremex.Avalonia.Controls` и `Eremex.Avalonia.Themes.DeltaDesign` **1.0.98** с Avalonia **11.1.1**; нужна соответствующая лицензия/trial Eremex.

Поддержка ограничена реализованными API. Advanced DataGrid features и остальной Eremex catalog не заявляются; импорт Eremex controls из существующего AXAML пока opaque. Подробнее: [DataGridControl Phase 1](Docs/EremexDataGridControlPhase1.md) и [список properties](Docs/EremexDataGridControlProperties-1.0.98.md). TFM и результаты проверки текущего net8 host приведены отдельно в [Net8Migration](Docs/Net8Migration.md).

## Версии

Текущая версия — **[Beta 0.1 / `v0.1.0-beta.1`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1)**. Это новая Beta-схема нумерации продукта; версия VSIX развивается отдельно — **0.1.18**.

Две предыдущие версии доступны по существующим Git tags. Их исторические README и исходники сохранены:

## Alpha 4.0

[Alpha 4.0 / `v0.4.0-alpha.1`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/tree/v0.4.0-alpha.1) — предыдущая Alpha с внешним VsHost и Visual Studio integration. [Release notes](Docs/Alpha4ReleaseNotes.md).

## Alpha 3.0

[Alpha 3.0 / `v0.3.0-alpha`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/tree/v0.3.0-alpha) — более ранняя standalone-версия. Ссылки ведут на реальные теги; отдельные GitHub Releases для этих Alpha не создаются заново.

## Требования

- Для сборки нужен .NET 8 SDK или более новый совместимый SDK. Подготовленные Windows x64 self-contained пакеты содержат .NET 8 runtime; SDK нужен для Build Validation и сборки generated проектов.
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
git checkout v0.1.0-beta.1
dotnet restore .\FormDesigner.csproj -p:BuildExperimentalEremexPlugin=false
dotnet run -c Release --project .\FormDesigner.csproj -p:BuildExperimentalEremexPlugin=false
```

Для сборки всей solution, включая VSIX, нужны перечисленные выше VSIX build tools:

```powershell
dotnet restore .\FormDesigner.sln
dotnet build .\FormDesigner.sln -c Release
```

### Установка Standalone

1. Откройте [Beta 0.1 Release](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1).
2. Скачайте `Avalonia-UI-Visual-Designer-Beta-0.1-win-x64.zip` и `SHA256SUMS.txt`.
3. Сверьте SHA-256 (`Get-FileHash -Algorithm SHA256 <файл.zip>`), распакуйте архив **целиком** и запустите `FormDesigner.exe`.

Пакет Windows x64 self-contained содержит .NET 8 runtime. Visual Studio, SDK и Eremex для запуска не нужны. Для restore/build экспортированных проектов нужен SDK, для запуска default generated `net6.0` приложений — соответствующий runtime. Linux/macOS и Windows ARM64 runtime в эту поставку не входят. Пакеты не подписаны цифровой подписью; не смешивайте DLL разных сборок.

### Visual Studio Extension

Скачайте `Avalonia-UI-Visual-Designer-Beta-0.1.vsix` из [Beta 0.1 Release](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1). Версия расширения — **0.1.18**. При сборке из исходников Debug-пакет находится в:

```text
AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix
```

1. Закройте Visual Studio и установите `.vsix` в нужный instance Visual Studio 2022 Community.
2. Перезапустите Visual Studio, откройте `.axaml` и выберите **Tools → Open in Avalonia UI Visual Designer** (Tools / «Средства»).
3. Измените поддерживаемые properties в отдельном VsHost, нажмите **«Применить изменения»**, проверьте VS buffer и сохраните документ через `Ctrl+S`.

Поставляемый VSIX содержит self-contained Windows x64 VsHost в `VsHost/`; runtime входит в пакет, Eremex не включён. Обычный developer Debug VSIX остаётся framework-dependent и требует .NET 8 runtime. Для первой проверки есть [SimpleAvaloniaApp](Samples/VisualStudioPoC/README.md). Интеграция развивается, поэтому после обновления пакета рекомендуется повторить этот ручной сценарий; автоматические IPC/packaging tests не заменяют проверку загрузки в живом Visual Studio.

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

Runner содержит AXAML/layout/source-preservation, startup/input, DesignerSurface, VSIX/VsHost/IPC, JSON/Multi Form, Preview/Export и Eremex suites. Для generated-project build scenarios требуется NuGet restore. Проверки Alpha 4.0 описаны в [Release Notes](Docs/Alpha4ReleaseNotes.md); предыдущие результаты миграции сохранены в [Net8Migration](Docs/Net8Migration.md).

## Документация

- [Beta 0.1 Release Notes](Docs/Releases/Beta-0.1.md), [RC validation](Docs/Beta01ReleaseCandidate.md) и [Beta Readiness Audit](Docs/BetaReadinessAudit.md).
- [P0 DataGrid Preview regression: root cause, live SQL Server и результаты проверок](Docs/DataGridPreviewRegression.md).
- [DropDownButton, SplitButton и ListBox: Toolbox, Preview, Export и проверки](Docs/StandardToolboxControls.md).
- [DeveloperArchitecture](Docs/DeveloperArchitecture.md) - техническая документация для разработчиков.
- [DesignerSurface](Docs/DesignerSurfaceArchitecture.md), [document sessions](Docs/DesignerDocumentSessionArchitecture.md) и [host services](Docs/DesignerHostServicesArchitecture.md).
- [AXAML Round-trip architecture](Docs/AxamlRoundTripArchitecture.md), [granular capabilities](Docs/AxamlGranularCapabilities.md), [Phase 4: Expander / ComboBox](Docs/AxamlCoveragePhase4.md) и [Phase 5: standard controls](Docs/AxamlCoveragePhase5.md).
- [Visual Studio fixture / ручная проверка](Samples/VisualStudioPoC/README.md).
- [Alpha 0.2 manual checklist](Docs/ALPHA_0_2_MANUAL_TEST_CHECKLIST.md) - исторический checklist standalone-сценариев.
- [Plugin guide](Docs/PluginGuide.md)
- [Контролируемая миграция .NET 8](Docs/Net8Migration.md)
- [Alpha 4.0 Release Notes](Docs/Alpha4ReleaseNotes.md)
- [Standalone startup/input fix](Docs/StandaloneStartupInputFix.md)
- [Undo/Redo smoke checklist](Docs/UndoRedoSmokeTest.md)

## Ограничения и roadmap

- AXAML Import не воспроизводит весь Avalonia API. DataGrid и custom/Eremex controls пока не входят в поддержанный import subset. Items/Selector controls поддерживают статические коллекции, не runtime Binding Engine или пользовательские templates/ItemsPanel.
- Bindings, Styles, Resources и Templates сохраняются, но не вычисляются и не редактируются специальными визуальными редакторами. Arbitrary assemblies/plugins открытого VS solution не загружаются.
- Изменение hierarchy, insertion и destructive operations ограничиваются безопасными source-mapped сценариями. Partial import не гарантирует runtime-equivalent внешний вид.
- Известны intermittent native UI Automation retention обычных окон и задержки больших форм (300–500 controls). Preview-specific memory regression проверяется отдельно. Полные результаты, включая красные/flaky прогоны: [Beta Readiness Audit](Docs/BetaReadinessAudit.md) и [Beta 0.1 RC](Docs/Beta01ReleaseCandidate.md).
- Physical Drag & Drop/DPI/мультимониторный UX, установленный VSIX в живом Visual Studio и live PostgreSQL требуют отдельной acceptance. Не заявляется полная поддержка произвольного AXAML Preview.
- Следующее расширение import coverage выбирается по blocker impact реальных документов; после Phase 5 в текущем MainWindow остаются три custom controls. Дальнейшие направления: безопасная custom-control metadata, расширение patches и hardening Visual Studio lifecycle.

## Примечания

Архитектура, ключевые классы, flows, diagnostics и правила разработки описаны в [DeveloperArchitecture](Docs/DeveloperArchitecture.md).

JSON project → Export и existing AXAML → minimal patch - разные workflows. Экспортируемые проекты сохраняют текущий default `net6.0` / Avalonia `11.1.1`; переход самого Designer на .NET 8 не меняет уже созданные пользовательские проекты. Beta может содержать ошибки; перед редактированием важных AXAML используйте version control.
