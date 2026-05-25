# FormDesigner Export Smoke Tests

Smoke tests проверяют, что XAML/C# export из текущего конструктора реально собирается в новом Avalonia Desktop project.

Запуск:

```powershell
.\smoke-tests\run-smoke-tests.ps1
```

Runner:

1. Создаёт документ через текущий `MainWindowViewModel`.
2. Вызывает `GenerateXaml()`.
3. Создаёт временный Avalonia project.
4. Копирует generated files (`MainWindow.axaml`, `MainWindow.axaml.cs`, дополнительные form windows).
5. Добавляет нужные NuGet-пакеты.
6. Запускает `dotnet build`.
7. Печатает `PASS/FAIL` по каждому сценарию.

Артефакты создаются в:

```text
artifacts/smoke-tests/<timestamp>/
```

Последний запуск записывается в:

```text
artifacts/smoke-tests/latest-run.txt
```

## Сценарии

- `SimpleFormExport`  
  Button, TextBox, CheckBox, TextBlock, Border. Canvas layout, Clean UI, без DataGrid NuGet и plugins.

- `RealDataGridExport`  
  BindingSource с полями `Title`, `Price`, `Count`; Real Avalonia DataGrid; проверяет `dataGrid:DataGrid` и NuGet `Avalonia.Controls.DataGrid`.

- `InteractionsExport`  
  `Button.Click -> ShowMessage`, `CheckBox.Checked/Unchecked -> Show/Hide`, `DataGrid.SelectionChanged -> TextBox.Text`.

- `MultiFormOpenFormExport`
  Designer project with `Form1` and `Form2`; `Button.Click -> OpenForm -> Form2`; generated app includes both windows and builds.

- `PluginFallbackExport`  
  Plugin control при выключенных runtime references экспортируется как безопасный placeholder без plugin DLL.

- `ResponsiveLayoutExport_StackPanel`  
  Простая вертикальная форма в `Responsive layout experimental` экспортируется как `StackPanel`.

- `ResponsiveLayoutExport_CanvasFallback`  
  Пересекающиеся элементы в responsive mode fallback-ятся на `Canvas`.

## NuGet

Base generated projects use:

- `Avalonia`
- `Avalonia.Desktop`
- `Avalonia.Themes.Fluent`
- `Avalonia.Fonts.Inter`

DataGrid scenarios additionally use:

- `Avalonia.Controls.DataGrid`

## Notes

Smoke tests intentionally live outside the main solution build. `FormDesigner.csproj` excludes `smoke-tests/**`, so the runner does not add extra entry points or generated files to the editor application.
