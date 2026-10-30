# PostgreSQL integration

## Архитектурный анализ до изменений

SQL Server использует `SqlServerSettingsModel` → `SqlConnectionStringBuilderService`
→ `BindingSourceModel.SourceKind=SqlServer`. Глобальные настройки передаются через
`MainWindowViewModel`, Settings Center и `AppSettingsService`. Чтение схемы находится
в `CreateBindingSourceFromDatabaseAsync`: `SqlConnection`, SQL Server `TOP` и
`DbColumn`. `SqlPreviewDataLoader` читает строки, а общий `PreviewRowsLoader`
управляет cache/status/sort/limit. Оба Preview используют этот же loader.

Универсальны: BindingSource/BindingField, document DTO/JSON, session/multi-form,
registry/descriptors, standard/Eremex binding, preview snapshots и export manifest.
SQL Server-specific: connection builder/certificate retry, quoting `[identifier]`,
`TOP`, generated SqlConnection/SqlDataReader и package Microsoft.Data.SqlClient.
Generated C# строится в MainWindowViewModel, включая отдельные secondary-form
generation contexts. ExportPipelineService записывает готовые файлы, manifest,
README и выполняет restore/build. Нельзя исправить только главную форму.

Решение: использовать существующий SourceKind (PostgreSql), общий ADO.NET adapter
contract/metadata reader и отдельные dialect adapters. Существующий SQL Server
pipeline сохраняется. PostgreSQL подключается к общим Preview и export contexts,
а не создаёт отдельный DataGrid/BindingSource/сериализатор.

Npgsql 8.0.9 совместим с net8.0 Designer и net6.0 generated projects
([NuGet package](https://www.nuget.org/packages/Npgsql/8.0.9)). Existing package versions/TFMs не меняются.
VSIX не ссылается на Npgsql; runtime dependency находится только во внешнем VsHost.

## Реализованный путь

1. Settings Center → PostgreSQL: Host, Port (5432), Database, Username, Password,
   SSL Mode, connection timeout, command timeout, default schema (public), preview
   limit (100). Существующие Save/Apply/Cancel применяются к рабочей копии настроек;
   отдельные команды проверяют подключение, отменяют проверку и сбрасывают PostgreSQL.
2. Data → PostgreSQL: источник создаётся в существующей BindingSources активной
   формы. Можно применить текущие глобальные настройки, проверить подключение,
   получить схемы, затем таблицы/views, выбрать объект и получить колонки. Можно
   ввести один SELECT/WITH-запрос. Запрос имеет приоритет над таблицей.
3. Полученные BindingField содержат исходные case-sensitive имена, типы, nullable
   и PK. Schema discovery не копирует первую запись в SampleValue. Пустая таблица
   всё равно даёт схему колонок. Источник привязывается обычным выбором BindingSource
   у standard/Eremex DataGrid, без отдельного PostgreSQL grid.
4. Designer, обычный Preview и AXAML Preview используют прежние snapshots, cache,
   PreviewRowsLoader и data binding. PostgreSQL-ветка читает реальные строки через
   общий ADO.NET reader. Ошибка даёт пустой результат и русское сообщение, Preview
   показывает error banner. Demo/sample fallback для PostgreSQL запрещён даже при
   включённом глобальном demo-флаге. Закрытие Preview отменяет выполняющееся чтение.
5. Экспорт использует обычные изолированные contexts всех форм. PostgreSQL source
   получает DTO, ObservableCollection, ItemsSource и async loader; каждая форма
   запускает собственные loaders на Opened и отменяет их на Closed. SQL Server
   остаётся на прежнем пути. В одном проекте могут присутствовать оба provider.

## Provider-specific и общие services

- `IDatabaseProviderAdapter`: DbConnection, quoting и provider-specific SQL для
  схем, таблиц/views, колонок и SELECT.
- `SqlServerProviderAdapter`: SqlConnection, квадратные скобки, TOP и SQL Server
  metadata. Существующие SQL Server certificate retry/import/loader не переписаны.
- `PostgreSqlProviderAdapter`: NpgsqlConnection, double-quoted identifiers, LIMIT,
  information_schema и pg_catalog. Имена схем/таблиц при metadata discovery передаются
  как parameters; для SELECT каждый identifier quoted целиком, без split по точке.
- `DatabaseReadService`: async DbCommand/DbDataReader, metadata, schema-only query
  description и bounded reads. PostgreSQL выполняет команды в READ ONLY транзакции,
  которая откатывается при освобождении. Есть timeout и CancellationToken.
- `PostgreSqlReadQuery`: консервативная проверка одного SELECT/WITH; учитывает
  comments, quoted identifiers, string/dollar literals, блокирует несколько
  statements, DML CTE, SELECT INTO и изменяющие команды. Диалект SQL Server не
  переписывается. Это не sandbox для произвольных серверных функций: для Preview
  следует использовать роль с минимальными правами чтения.
- `PostgreSqlExportNames`: отдельные C# properties для name/Name, пробелов, точек,
  цифровых префиксов и конфликтов с Clone/CopyFrom; SQL reader всегда обращается
  к исходному имени колонки. Nullable value types сохраняются в generated DTO.
- `IRuntimeBindingExportPathProvider.GetFieldPath`: совместимое расширение с default
  implementation. Eremex export получает те же property names, что generated DTO;
  остальные plugins сохраняют прежний field.Path.

## Секреты и конфигурация экспортированного приложения

Password в PostgreSqlSettingsModel имеет JsonIgnore. В локальном app-settings.json
сохраняется только DPAPI ciphertext, привязанный к текущему пользователю Windows.
Флаг RememberPassword=false удаляет сохранённый ciphertext. Если DPAPI недоступен,
пароль остаётся только в памяти; при следующем запуске его нужно ввести в Settings.
Кэш сессионных credentials не сериализуется. Профиль BindingSource хранит только
Host/Port/Database/Username/SSL/timeouts; Password/Passfile/Options исключаются.
Защита DTO действует также при обратном порядке свойств JSON.

Generated C#/AXAML/README не получают пароль или ciphertext Designer. Runtime
сначала читает ключ конкретной формы/источника `FORMDESIGNER_POSTGRES_<HASH>`
(16 hex-символов SHA256 от FormId и SourceId). Затем проверяется совместимый alias
`FORMDESIGNER_POSTGRES_<SOURCE_ID>`, затем общий `FORMDESIGNER_POSTGRES_CONNECTION_STRING`.
Точные scoped keys для каждой формы/источника напечатаны в README.generated.md;
alias SourceId нормализуется генератором в C# identifier и верхний регистр.
Копии форм с одинаковым SourceId получают разные scoped keys. Значение — строка
Npgsql, заданная вне репозитория. Подключения форм настраиваются независимо.
В generated project пароль не требуется на этапе restore/build.

Npgsql package включается только при использовании PostgreSQL-bound DataGrid.
Loader async, выполняется в READ ONLY, обрабатывает ошибки на русском языке,
не подставляет demo rows и сохраняет пустой результат пустым. CRUD-кнопки
существующего генератора меняют коллекции в памяти, не выполняют database writes.

В diagnostics остаются exception type, SQLSTATE, inner exception type и stack.
Исходный Message/Detail/Where может включать SQL literals, поэтому он не логируется.
Full connection strings и пароль не записываются в events. Добавлены все события
POSTGRES_CONNECTION_*, POSTGRES_SCHEMA_LOADED, POSTGRES_TABLES_LOADED,
POSTGRES_COLUMNS_LOADED, POSTGRES_PREVIEW_START, POSTGRES_PREVIEW_ROWS_LOADED,
POSTGRES_EXPORT_GENERATED и POSTGRES_RUNTIME_BINDING_CHECK.

## Совместимость

Нового обязательного поля DatabaseProvider нет: используется прежний SourceKind.
SQL Server JSON с SourceKind=SqlServer и без DatabaseProvider читается по старым
правилам; default dbo для него сохранён. PostgreSql сохраняется как новое значение
SourceKind; для него default schema public. BindingSource/FileModel, history,
copy/snapshots и per-form JSON сохраняют provider и metadata. Версия формата не
увеличивалась. SQL Server password/export policy намеренно не менялась.

Прямо добавлен только Npgsql 8.0.9 в FormDesigner.csproj. Необходимые транзитивные
dependencies разрешаются NuGet. Версии Avalonia 11.1.1, Eremex 1.0.98,
CommunityToolkit 8.2.1, SqlClient 5.2.2 и TargetFrameworks не менялись.
В VSIX/Host.Protocol нет ссылки на Npgsql; Npgsql входит в dependency closure
внешнего net8.0 VsHost. В devenv.exe provider не загружается.

## Проверки

Дата: 30.10.2026. Реального тестового PostgreSQL нет (подтверждено пользователем).

- Designer и smoke runner/VsHost: Debug build, 0 errors / 0 warnings.
- PostgreSQL offline/contract checks: 20/20, включая DPAPI save/load, masking,
  source serialization/clone, form switching, Settings apply/reset, legacy SQL Server
  JSON/modes, query policy, quoting, cancellation до подключения, настоящую ошибку
  подключения к закрытому локальному порту и оба native Preview в error path.
- PostgreSQL export smoke: 4/4 с restore/build настоящих net6.0 проектов:
  PostgreSQL-only, PostgreSQL+SQL Server в трёх формах, quoted/case-distinct columns,
  Eremex DataGrid. Для обычного export также загружен compiled generated ViewModel:
  ItemsSource остаётся привязанным и пустым при недоступном сервере; пароль не попадает
  в сообщение ошибки. Проверены экспортируемые файлы и README.generated.md.
- Объединённый regression набор: 407/409. Учтены первые 67 сценариев последовательного
  прогона и остальные 342 в четырёх пакетах. Восемь IPC-сценариев сначала получили
  AccessDenied в sandbox и повторно прошли вне sandbox (8/8). Два оставшихся сбоя
  уже описаны в Alpha4ReleaseNotes.md: SqlDataGridPreviewUsesDemoOnlyWhenExplicitlyEnabled
  (ожидался DemoData, получен SampleRows) и DllTablePreviewClearlyMarksSampleRows (пустой sample fallback).
  Это не один финальный долгоживущий прогон. Последние изменения PostgreSQL отдельно
  проверены 20/20 offline и 4/4 export, SQL Server settings/global profile — 4/4.
  Сводка с результатом и логом каждого сценария: artifacts/postgres-validation-summary.json.
- Solution build имеет прежнюю отдельную ошибку CS0023 в VSIX/VsTextPatch.cs:52
  (`ordered.Reverse().ToArray()`) и прежнее NU1603. VSIX source не менялся; новая
  упакованная версия расширения не считается проверенной установленной в VS.
- MainWindow fixture получил 17 standard visuals: теперь 1487/1490 imported,
  XML elements 2524. Ожидаемые counts трёх coverage tests обновлены; проверки
  трёх opaque/skipped custom controls сохранены. Это метрика одного документа.
- Settings PostgreSQL визуально проверен через реальный Avalonia layout/render
  изолированного окна при 980×720 и минимальном размере 900×640. Исправлены ширина
  карточек и прокрутка нижних полей/кнопок; screenshots находятся в artifacts/postgres-ui.
  Проверка UI не подтверждает получение данных с настоящего PostgreSQL.

### Повторный запуск

```powershell
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --postgres-checks

$env:SMOKE_SCENARIO_FILTER = 'Postgres'
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll .\artifacts\postgres-export-smoke
```

Для полного набора убрать SMOKE_SCENARIO_FILTER. SMOKE_SCENARIO_NAMES_FILE позволяет
выбрать точные имена сценариев для изолированных пакетов; TEMP/TMP и artifacts root
у пакетов должны быть разными. Один долгоживущий процесс и отдельные пакеты могут
по-разному проявлять ранее известный Eremex late-load lifecycle defect.

### Невыполненные реальные integration checks

Успешное подключение и отказ по неверному паролю; schemas/tables/views/columns/PK/
nullable с сервера; реальные/пустые выборки; отмена выполняющегося запроса; timeout;
обе Preview с настоящими строками; загрузка этих строк скомпилированным экспортом.
Все эти проверки включаются через FORMDESIGNER_TEST_POSTGRES и --postgres-checks.
Тест создаёт schema Designer.Test_<GUID> только в явно заданной тестовой БД и удаляет
её в finally; нужен тестовый пользователь с правами создавать schema/table/view.
Пароль не передавать в чат или source control. Integration fixtures не выдаются за
выполненный реальный end-to-end сценарий. SQL Server smoke также не заменяет
подключения к настоящему SQL Server.

## Изменённые файлы

Всего 42 файла: 12 новых и 30 изменённых. Временные логи, render-helper и результаты
проверок в ignored artifacts в этот список не входят.

Новые:

- DesignerSystem/Binding/DatabaseProviderAdapters.cs
- DesignerSystem/Binding/DatabaseReadService.cs
- Models/PostgreSqlSettingsModel.cs
- Services/PostgreSqlConnectionService.cs
- Services/PostgreSqlExportNames.cs
- Services/WindowsSecretProtection.cs
- ViewModels/MainWindowViewModel.PostgreSql.cs
- ViewModels/MainWindowViewModel.PostgreSqlExport.cs
- ViewModels/SettingsWindowViewModel.PostgreSql.cs
- Views/MainWindow.PostgreSql.cs
- smoke-tests/FormDesigner.ExportSmokeTests/Program.PostgreSql.cs
- Docs/PostgreSqlIntegration.md

Расширены:

- FormDesigner.csproj
- Models/AppSettingsModel.cs, BindingSourceModel.cs, BindingFieldModel.cs,
  DesignerDocumentFileModel.cs, ExportedAxamlPreviewModel.cs
- Localization/SettingsTextCatalog.cs
- Services/AppSettingsService.cs, ExportPipelineService.cs
- DesignerSystem/Binding/DataSourceIdentity.cs, DataGridRuntimeDataModeResolver.cs,
  SqlPreviewDataLoader.cs, PreviewRowsLoader.cs, BindingPreviewItemsBuilder.cs
- ViewModels/MainWindowViewModel.cs, SettingsWindowViewModel.cs
- Views/MainWindow.axaml, MainWindow.axaml.cs, PreviewWindow.axaml,
  PreviewWindow.axaml.cs, SettingsWindow.axaml, SettingsWindow.axaml.cs
- PluginContracts/DesignerContracts.cs
- Plugins/EremexDesignerPlugin/Descriptors/EremexDataGridControlDescriptor.cs
- smoke-tests/FormDesigner.ExportSmokeTests/Program.cs, Program.AxamlCoverage.cs,
  Program.AxamlPhase4.cs, Program.AxamlPhase5.cs
- README.md, smoke-tests/README.md
