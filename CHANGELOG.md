# Changelog

## v0.4.0-alpha.1 / Alpha 4.0

- Добавлен Visual Studio VSIX bridge: Named Pipes, отдельный VsHost и общий DesignerSurface со standalone.
- Реализован AXAML Import Phase 2–5: вложенные layout containers и стандартные controls, granular capabilities и source-preserving minimal patches.
- Для настоящего `Views/MainWindow.axaml` импортируется 1470 из 1473 visual candidates (99,80%); три custom controls остаются opaque. Это метрика одного stress fixture, не всей Avalonia.
- Улучшены Property Inspector, selection, partial-import diagnostics и ACK/version/checksum protection.
- Исправлена блокировка standalone startup overlay при недоступном последнем workspace; smoke hosts изолированы от пользовательских настроек.
- Сохранены JSON/Multi Form, Toolbox, Preview/Export, DataGrid, SQL/DLL sources, plugins и Eremex TextEditor/DataGridControl vertical slices.
- Designer/VsHost работают на .NET 8, VSIX остаётся net472, Host.Protocol использует netstandard2.0;net8.0. Версии зависимостей при подготовке релиза не изменены.
- Visual Studio Extension остаётся экспериментальной. Bindings/templates сохраняются, но пользовательские ViewModels не исполняются; visual equivalence сложного AXAML не гарантируется.

Подробнее: [Alpha 4.0 Release Notes](Docs/Alpha4ReleaseNotes.md).

## 0.2.0-alpha

- Добавлен multi-form workflow: несколько форм в designer-project, document tabs и project explorer.
- Добавлен `OpenForm` interaction для сценария `Button.Click -> OpenForm -> Form2`.
- Расширен DataGrid + BindingSource workflow с real fields и Real Avalonia DataGrid export.
- Добавлен Export Pipeline: generated files, required packages, export diagnostics и build validation.
- Добавлены Layout System v2 сценарии: Canvas, Grid, StackPanel и layout-aware export.
- Добавлен PropertyGrid v2 с компактным inspector workflow.
- Добавлена workspace/project foundation и сохранение multi-form project model.
- Добавлены smoke tests для Alpha 0.2 end-to-end export/build сценариев.
- Проект переведён на `net6.0` и Avalonia packages `11.1.1`.

