# Avalonia UI Visual Designer

> Статус: **0.2.0-alpha**. Desktop designer для визуальной сборки Avalonia UI форм, настройки данных/interactions и экспорта в обычный Avalonia-проект.

![C#](https://img.shields.io/badge/C%23-.NET_6-blue?style=for-the-badge&logo=csharp)
![Avalonia](https://img.shields.io/badge/Avalonia-11.1.1-purple?style=for-the-badge)
![Status](https://img.shields.io/badge/status-0.2.0--alpha-orange?style=for-the-badge)

## Что это

Avalonia UI Visual Designer — это IDE-like конструктор форм для Avalonia:

- несколько форм в одном designer-project;
- canvas editor с drag/drop, selection, undo/redo;
- BindingSource и DataGrid с реальными полями;
- Logic/interactions без ручного C#;
- preview runtime;
- Export Pipeline с generated files, NuGet-зависимостями и build validation.

Alpha 0.2 сфокусирована на end-to-end workflow: создать multi-form проект, настроить DataGrid/BindingSource/Logic, открыть preview, экспортировать Avalonia-проект и собрать его.

## Требования

- **.NET 6 SDK**
- Avalonia NuGet-пакеты **11.1.1**
- Windows для desktop-сценариев разработки

Проверить SDK:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

Если .NET 6 SDK нет, установите его:  
<https://dotnet.microsoft.com/en-us/download/dotnet/6.0>

## Запуск

```powershell
dotnet restore .\FormDesigner.sln
dotnet build .\FormDesigner.sln
dotnet run --project .\FormDesigner.csproj
```

## Quick Start

1. Создайте новый project.
2. В Project Explorer добавьте вторую форму: `+ Form`.
3. На `Form1` перетащите `Button`, `DataGrid`, `TextBox`, `CheckBox`.
4. Во вкладке `Data` создайте `BindingSource`, добавьте поля `Id`, `Name`, `Email`, `Status`.
5. Выберите `DataGrid`, назначьте BindingSource и создайте колонки из полей.
6. Во вкладке `Logic` добавьте interactions:
   - `Button.Click -> OpenForm -> Form2`;
   - `DataGrid.SelectionChanged -> TextBox.Text = Name`;
   - `CheckBox.Checked/Unchecked -> Show/Hide block`.
7. Откройте preview и проверьте поведение.
8. Перейдите в `Code / Export`, нажмите `Refresh`, затем `Validate build`.
9. При Real DataGrid export установите пакет:

```powershell
dotnet add package Avalonia.Controls.DataGrid --version 11.1.1
```

## Export Pipeline

Export Pipeline показывает:

- дерево generated files: `MainWindow.axaml`, `MainWindow.axaml.cs`, дополнительные формы;
- required packages;
- export diagnostics;
- build validation status;
- code preview выбранного файла.

Generated projects таргетятся в `net6.0` и используют Avalonia `11.1.1`.

## Smoke tests

Запуск:

```powershell
.\smoke-tests\run-smoke-tests.ps1
```

Smoke tests создают временные Avalonia-проекты в `artifacts/smoke-tests`, вставляют generated XAML/C# и запускают `dotnet build`.

Покрытые Alpha-сценарии:

- simple form export;
- Real DataGrid export;
- interactions export;
- multi-form OpenForm export;
- Alpha end-to-end project export;
- DataGrid + BindingSource workflow;
- save/load multi-form project;
- export build validation;
- plugin fallback;
- Grid/StackPanel/layout export.

## Ограничения Alpha 0.2

- UI preview проверяется вручную; smoke tests проверяют model/export/build path.
- Layout System v2 ещё считается experimental.
- Real DataGrid требует `Avalonia.Controls.DataGrid 11.1.1`.
- Plugin runtime export пока лучше использовать через fallback, если plugin DLL не входит в target project.
- Export to existing project требует внимательной проверки overwrite-сценариев.

## Документация

- [Alpha 0.2 manual checklist](Docs/ALPHA_0_2_MANUAL_TEST_CHECKLIST.md)
- [Plugin guide](Docs/PluginGuide.md)
- [Undo/Redo smoke checklist](Docs/UndoRedoSmokeTest.md)