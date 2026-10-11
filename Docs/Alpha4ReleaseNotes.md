# Avalonia UI Visual Designer — Alpha 4.0

Предварительный релиз **Alpha 4.0**, Git tag **`v0.4.0-alpha.1`**.
Visual Studio Extension имеет отдельную версию **0.1.17**.

## Основные изменения

- **Visual Studio integration:** VSIX получает активный AXAML из text buffer,
  включая несохранённые изменения, и запускает отдельный VsHost через Named Pipes.
  Avalonia/Eremex не загружаются в `devenv.exe`; standalone и VsHost используют
  общий DesignerSurface.
- **AXAML Import / Round-trip:** source map, granular document/element/property
  capabilities и minimal text patches вместо полной XML-регенерации. Comments,
  unknown syntax, namespaces, bindings, styles, resources и templates сохраняются.
  Version/checksum validation и ACK защищают buffer от конфликтующего Apply.
- **Стандартные controls:** Canvas, Grid, StackPanel, DockPanel, Border,
  WrapPanel, ScrollViewer, TabControl/TabItem, Expander, ComboBox/ComboBoxItem,
  ItemsControl, ListBox/ListBoxItem, TreeView/TreeViewItem, ProgressBar и базовые
  Button/TextBox/TextBlock/CheckBox. Вложенная hierarchy и native layout semantics
  сохраняются; Items controls поддерживают статические данные.
- **Inspector и diagnostics:** безопасное редактирование поддерживаемых свойств,
  сохранение selection, AXAML Import Report с counts и причинами partial import.
- **Standalone hardening:** неудачное восстановление последнего workspace больше
  не оставляет невидимый блокирующий backdrop. Проверены New/Open, toast dismissal,
  dispatcher/input и повторный startup; smoke hosts не используют реальные настройки.
- **Существующий standalone workflow:** JSON-проекты, Multi Form, Toolbox,
  Property Inspector, Preview, AXAML/C# Export Pipeline и Build Validation,
  DataGrid Designer, SQL/DLL Data Sources, plugins и Eremex TextEditor/DataGridControl.
- **.NET 8:** Designer и VsHost работают на `net8.0`; VSIX остаётся `net472`,
  Host.Protocol — `netstandard2.0;net8.0`. NuGet versions при подготовке релиза
  не изменялись.

## AXAML Coverage

Настоящий `Views/MainWindow.axaml`: **1470 / 1473 visual candidates (99,80%)**,
**3 opaque custom controls** — DesignerToolbox, DesignerSurface и
DesignerPropertyInspector из namespace `views:`.

Это coverage импортированной модели/source map **одного stress-test документа**,
не 99,80% Avalonia API, не поддержка всех свойств и не полная visual equivalence.
Bound collections могут отображаться пустыми: пользовательские bindings и
ViewModels при импорте не исполняются.

Подробности: [Phase 5](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/blob/v0.4.0-alpha.1/Docs/AxamlCoveragePhase5.md)
и [startup/input fix](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/blob/v0.4.0-alpha.1/Docs/StandaloneStartupInputFix.md).

## Установка

Нужны Windows и .NET 8 SDK/runtime. Для расширения — Visual Studio 2022 Community
x64; для сборки VSIX — .NET Framework 4.7.2 targeting pack и используемый VSSDK.
Eremex vertical slices требуют соответствующей лицензии/trial.

```powershell
git clone --branch v0.4.0-alpha.1 https://github.com/KirillU002/Avalonia-UI-Visual-Designer.git
cd Avalonia-UI-Visual-Designer
dotnet restore .\FormDesigner.sln
dotnet build .\FormDesigner.sln
dotnet run --no-build --project .\FormDesigner.csproj
```

После сборки установите `AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`
с закрытой Visual Studio. Откройте `.axaml`, выберите **Tools → Open in Avalonia UI
Visual Designer**, отредактируйте поддерживаемые свойства и нажмите **«Применить
изменения»**. Apply меняет VS buffer; сохранение выполняется обычным `Ctrl+S`.
VsHost runtime входит в `VsHost/` внутри VSIX; это framework-dependent output.

## Известные ограничения

- Visual Studio Extension экспериментальная, Designer открывается отдельным окном.
- Сложный AXAML может отличаться визуально от runtime приложения.
- Произвольные custom controls, DataGrid/Eremex AXAML import пока не поддержаны;
  unknown controls и небезопасные subtrees остаются opaque.
- Bindings, templates, resources и styles сохраняются, но не исполняются/редактируются
  специальными визуальными редакторами. Пользовательские assemblies не загружаются.
- Hierarchy edits, insertion, deletion и item reorder ограничены безопасными
  source-mapped сценариями. Неподдерживаемое свойство не делает весь документ read-only.
- Generated standalone exports сохраняют default `net6.0` / Avalonia `11.1.1`;
  миграция самого Designer не меняет пользовательские проекты.
- Некоторые тексты встроенной справки ещё относятся к Alpha 3.0; её переработка
  не входит в эту релизную подготовку.
- В SQL demo preview строки создаются, но `PreviewRowsDataKind` возвращает
  `SampleRows` вместо ожидаемой тестом метки `DemoData`. Это ранее известное
  отклонение `SqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled`, не новая
  регрессия Alpha 4.0; в этом релизе оно не исправлялось.
- Для DLL без доступных real rows включённый sample fallback может вернуть
  пустую коллекцию вместо ожидаемых demo rows
  (`DllTablePreviewClearlyMarksSampleRows`). Это также отклонение из прежнего
  baseline; импорт метаданных DLL и загрузка доступных real rows проверяются отдельно.
- Поздняя загрузка Eremex plugins после других runtime AXAML Preview в одном
  долгоживущем процессе может приводить к ошибке разрешения XML namespace
  TextEditor/DataGridControl. В чистом процессе Eremex suite проходит 5/5;
  обычный startup загружает plugins до первого Preview. Lifecycle поздней
  загрузки требует отдельного исправления и не заявляется полностью поддержанным.

## Проверка релиза

- Debug-сборка solution: **0 ошибок**, одно существующее предупреждение `NU1603`
  о выборе VSSDK analyzer dependency. Версии NuGet и Target Framework не менялись.
- Полный Debug smoke runner: **400 / 405 passed**. Два отклонения SQL/DLL fallback
  описаны выше; два Eremex Preview failures воспроизводятся при поздней загрузке.
  Пятый failure был гонкой проверки DLL trace с очередью UI Dispatcher:
  smoke harness исправлен, повторный сценарий прошёл **1/1**.
- Отдельные повторные прогоны: Eremex **5/5**, Export Validation **10/10**.
  Полный runner после последнего test-only исправления повторно не запускался;
  эти результаты не означают, что общий долгоживущий процесс полностью green.
- В полном прогоне прошли AXAML **107/107**, startup/input **16/16**,
  DesignerSurface **14/14**, VsHost **10/10**, VSIX **5/5**, Multi Form **13/13**
  и New Project **7/7**. MainWindow coverage повторно подтверждён: **1470/1473**;
  31 восстановленный Phase 5 control присутствует в native visual tree с bounds.
- VSIX **0.1.17** собран: полный VsHost output, включая exe/dll/deps/runtimeconfig
  и dependencies, находится в `VsHost/`; Avalonia/Eremex visual assemblies в root
  пакета отсутствуют. Runtime target VsHost — `net8.0`.
- Ручная интеграция VSIX подтверждена пользователем до релизной подготовки.
  Новая ручная проверка установленного расширения и полный Release-конфигурационный
  smoke-прогон в рамках этой подготовки не выполнялись.

Перед редактированием важных документов используйте version control.
