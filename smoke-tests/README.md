# FormDesigner Export Smoke Tests

## PostgreSQL

Phase 2: [статус, SQL fixture и ручная приёмка](../Docs/PostgreSqlIntegrationPhase2.md).
Проверка среды: `.\smoke-tests\run-postgres-integration.ps1 -ProbeOnly`.
Изолированный Docker: `-UseDocker`; явно заданная тестовая БД через env:
`-UseExistingTestDatabase`. Скрипт не устанавливает ПО и не выводит пароль.
Real Eremex suite запускается отдельно с `--postgres-eremex-checks`, до других
AXAML Preview в этом процессе. При отсутствии тестовой БД выводится SKIP.

Offline/contract проверки, включая защищённые настройки, JSON, query policy,
ошибочное подключение без synthetic fallback и оба Preview:

```powershell
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --postgres-checks
```

Четыре export-сценария собирают настоящие net6.0 проекты с Npgsql:

```powershell
$env:SMOKE_SCENARIO_FILTER = 'Postgres'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\postgres-export-smoke
```

Это PostgreSQL-only, mixed Multi Form (2 PostgreSQL + 1 SQL Server), case-sensitive
quoted columns и Eremex DataGrid. Основной PostgreSQL export также проверяет
compiled generated ViewModel/ItemsSource при недоступном сервере.

Реальные integration checks запускаются только при заданной
`FORMDESIGNER_TEST_POSTGRES` (Npgsql connection string тестовой БД) и ключе
`--postgres-checks`. Создаётся отдельная schema `Designer.Test_<GUID>` с тестовыми
tables/view; она удаляется в finally. Нужны права создавать schema/table/view.
Проверяются metadata, реальные/пустые rows, оба Preview, скомпилированный export,
invalid password, timeout и cancellation. Без этой переменной выводится SKIP;
offline checks не считаются успешным подключением к серверу.

Для полного набора убрать `SMOKE_SCENARIO_FILTER`. Для изолированных пакетов
`SMOKE_SCENARIO_NAMES_FILE` может указывать UTF-8 файл с точными именами сценариев
(по одному на строку). У параллельных пакетов должны быть отдельные TEMP/TMP и
artifacts root. Результаты пакетного прогона не доказывают отсутствие известных
Eremex late-load проблем одного долгоживущего процесса.

## Общий runner

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

- `ToolboxStandard*`: DropDownButton, SplitButton и ListBox во всех основных путях Designer. Включает настоящий отдельный процесс экспортированного приложения, оба Preview, JSON, минимальный AXAML patch, Multi Form, реальные и пустые DLL rows. PostgreSQL здесь проверяется на отказ без подставных данных. Описание и ограничения: [StandardToolboxControls](../Docs/StandardToolboxControls.md).

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
