# P0: реальные строки БД в DataGrid Preview

Дата: 30 октября 2026 года. Базовый commit: `dd65531`.

Исправление подтверждено на настоящем изолированном SQL Server LocalDB в Legacy Preview, AXAML Preview и отдельном процессе экспортированного приложения. Для PostgreSQL проверены общие контракты и offline-сценарии; доступного PostgreSQL нет. Полностью закрытой live-интеграцию обоих provider считать нельзя.

## Воспроизведение и точный root cause

Обнаружены несколько независимых дефектов.

| Дефект | Где теряются данные | Проверка до исправления |
| --- | --- | --- |
| Runtime-копия поля не сохраняла `DbType`, `IsNullable`, `IsPrimaryKey` | Эти свойства входят в `DataSourceIdentity.BuildKey`, но отсутствовали в `CreateRuntimeBindingFieldModel`. Preload сохранял реальные строки под runtime-ключом, а DataGrid искал их по другому ключу file model. | На SQL Server запрос возвращал 3 строки, Legacy Preview с заполненными metadata показывал 0. |
| Несовместимый контракт строки в AXAML Preview | `BuildExportedAxamlDataGridView` возвращал `DataView`/`DataRowView`, а экспортированные Binding обращались к обычным свойствам generated DTO. Такой объект не давал Avalonia нужных значений ячеек. | `ItemsSource.Count == 3`, но все ячейки пустые. |
| Сопоставление таблиц по позиции | Список bindings содержит только связанные таблицы, а список native DataGrid — также таблицы без источника. `ElementAtOrDefault(bindingIndex)` назначал строки другому контролу. | Первая таблица без источника получала 3 строки, настоящая связанная таблица — 0. |
| SQL Server error не попадала в общий banner | Loader возвращал результат с ошибкой, но banner обрабатывался только для PostgreSQL. | Неверный SELECT оставлял пустую таблицу без сообщения. |
| SQL cancellation не доходила до драйвера, ранний render | SQL Server ветка не передавала token в Open/ExecuteReader/Read; resize timer мог отрисовать таблицу до загрузки и после закрытия. | Добавлены отдельные проверки pending load, закрытия, повторного открытия и настоящего заблокированного запроса. |
| SQL aliases с пробелами в Legacy | Путь вида `[Customer Name]` передавался в parser Binding и вызывал ошибку синтаксиса. | Реальная выборка с aliases не доходила до визуального DataGrid. |

Первый baseline на реальном SQL Server: **3/8**, включая пустые AXAML-ячейки, неправильное сопоставление и скрытую SQL-ошибку. Дополнительное воспроизведение metadata: **0 строк** в Legacy при успешном запросе с тремя строками. Данные и provider не подменялись mock-провайдером в этих live-проверках.

## История Git

Сравнение `git log -S` и diff показало:

- `95bf5b1` (17.06.2026) добавил `DbType`/PK/nullable в identity hash; runtime-копирование этих значений не было добавлено. Так сформировалось несовпадение ключей для источников с настоящими metadata.
- `e1cbbcd` (11.07.2026) ввёл AXAML Preview с адаптером на `DataView`; привязки к DTO и реальный тип preview-строки оказались несовместимы.
- `a22d541` (13.07.2026) заменил поиск native DataGrid по имени на сопоставление по индексу, что нарушало сценарии с несвязанной таблицей перед связанной.
- Последний `dd65531` объединил PostgreSQL и Toolbox-изменения, но перечисленные механизмы существовали раньше. Доказательств, что причиной является сам PostgreSQL adapter, нет.

Это локализация изменений по исходному коду и воспроизведению. Полный runtime `git bisect` не выполнялся: подтверждённого live working snapshot с такой БД не было. Destructive reset и откат рабочих функций не применялись.

Прежние SQL-тесты преимущественно проверяли `MainWindowViewModel.LoadPreviewRowsForBindingSourceAsync`, загруженные строки и строки generated AXAML/C#. Проверка AXAML Preview также проверяла наличие binding и методов в исходном файле. Они не подтверждали текст native ячеек, совпадение cache keys при настоящих metadata и правильное назначение нескольких таблиц. PostgreSQL real-server checks ранее были честно отмечены SKIP.

## Что изменено

Сохранена существующая цепочка Source Settings → provider-specific driver → общий rows loader → BindingSource → Preview. Новая система BindingSource не создавалась.

Runtime-поле теперь копирует metadata полностью. Ключи file model и runtime model совпадают, в том числе после JSON Save/Load. Provider определяется существующим `SourceKind`; SQL Server продолжает использовать `Microsoft.Data.SqlClient`, PostgreSQL — свой adapter и Npgsql. SQL schema discovery и диалекты запросов не менялись.

Для AXAML Preview сохраняются runtime-имена DataGrid. Контролы сопоставляются по export name, связанному с ControlId; порядковый fallback удалён. General field map переводит исходные SQL aliases и PostgreSQL identifiers в те же имена, которые использованы в generated DTO. Колонки шаблонов также получают реальные значения; заполнение по позиции колонок больше не требуется.

Standard DataGrid обоих Preview использует общий dictionary rows contract. Только в in-memory AXAML привязки ячеек преобразуются в индексаторы. Экспортируемый AXAML, generated DTO и исходники импортированного AXAML не переписываются. `DataView` сохранён для прежнего plugin adapter; Eremex-код не менялся.

Каждый DataGrid получает собственный `DataGridPreviewContext.Rows` и настоящий ItemsSource binding. Rows остаются UI-bound ObservableCollection/collection view; изменение фильтра выполняется на UI thread. Сортировка переиспользует существующий dictionary comparer. Для сложных имён в Legacy используется существующий cell template, который читает значение по точному ключу.

Native DataGrid без источника пуст. Для настроенных БД demo/sample fallback отключён в копии источника, не меняя документ Designer. Пустой результат остаётся пустым с колонками. Неверный запрос, недоступный сервер и timeout показывают русскоязычный banner. Статические данные Manual source по-прежнему доступны явно.

## Async и lifecycle

- SQL Server получает CancellationToken в OpenAsync, ExecuteReaderAsync и ReadAsync. Сохраняются существующие лимит строк и command timeout 30 секунд.
- Окно ждёт async preload и затем строит DataGrid на UI thread. Синхронного ожидания SQL в Preview не добавлено.
- Generation guard не позволяет устаревшему запросу заменить кэш/содержимое после нового reload.
- Closed отменяет запрос и останавливает render timer. Resize timer пропускается во время loading и не запускает Legacy render внутри AXAML Preview.
- Reset/Reload сохраняют выбранный Preview mode. Ошибки кэша сохраняют сообщение при повторной попытке; Reload очищает кэш, повторное открытие создаёт новый runtime state.

## Diagnostics и секреты

Добавлены все события:

```text
DATAGRID_PREVIEW_START
DATAGRID_SOURCE_RESOLVED
DATAGRID_PROVIDER_SELECTED
DATAGRID_QUERY_START
DATAGRID_QUERY_SUCCESS
DATAGRID_QUERY_FAILED
DATAGRID_ROWS_LOADED
DATAGRID_DATACONTEXT_ASSIGNED
DATAGRID_ITEMSSOURCE_ASSIGNED
DATAGRID_PREVIEW_RENDERED
DATAGRID_PREVIEW_FAILED
```

Запись содержит FormId, ControlId, BindingSourceId, Provider, QueryType, LoadedRowsCount, ItemsSourceCount, DataContextType, PreviewMode и Duration в миллисекундах с начала Preview. SchemaOnly не выдаётся за выполненный запрос. События доступны в `PreviewWindow.DataGridDiagnostics` и существующем Workspace Output; открытия дочерних форм передают события владельцу.

В событиях нет query text, connection strings и паролей. SQL errors сохраняют тип exception, SQL error number, тип inner exception и stack без серверного Message, который может содержать литералы. UI получает безопасное русское сообщение. Проверена ошибка с контрольным секретом в exception: в diagnostics его нет.

## Реальная тестовая среда

Найден уже установленный LocalDB; новое системное ПО не устанавливалось. Создан отдельный private test instance `FormDesignerPreviewP0_<timestamp>`, версия SQL Server LocalDB **17.0.4025.3**. Пользовательский `MSSQLLocalDB` не запускался и не менялся. Подключение через Windows authentication, без пароля.

После проверок созданный instance остановлен. Тестовая БД сохранена; текущий Windows-auth профиль находится в ignored `artifacts/datagrid-preview-p0/localdb.json` для локального воспроизведения.

Таблицы: Customers (101/Анна SQL, 202/Борис SQL, 303/Вера SQL, один NULL Email), Orders (две разные строки с Amount 19.50 и 720.25), EmptyTable (0 строк). Fixture включён в `smoke-tests/SqlServer/DataGridPreviewFixture.sql`.

Дополнительно пройден настоящий ViewModel workflow: глобальный SQL profile → `RefreshSelectedBindingSourceFromDatabaseAsync` → обнаруженные колонки и типы → сохранённый BindingSourceId → оба Preview с тремя различающимися строками. Это API, используемый Designer; ручной click-through Settings/Data Source dialogs не выполнялся.

Окна Preview создавались на настоящем Avalonia Win32 UI thread. Проверены ItemsSource, DataContext и TextBlock реальных native ячеек; изображения обоих режимов просмотрены. Простое успешное выполнение запроса не использовалось как критерий прохождения.

## Результаты

| Набор | Результат |
| --- | --- |
| Полный FormDesigner.sln, включая VSIX packaging | 0 ошибок; 1 прежнее NU1603 у VSSDK analyzer |
| Smoke runner / Designer / VsHost build | 0 ошибок, 0 предупреждений |
| Новый DataGrid Preview набор | 43/43: 18 deterministic contracts и 25 live SQL Server checks |
| Финальная контрактная проверка после guard пустой схемы | 18/18; источник без колонок не выполняет запрос и не показывает ложную ошибку |
| Реальные Settings/schema/BindingSource/Preview | 1/1, оба режима |
| Export → restore → build → отдельный процесс | 1/1, реальные Customers=3 и Orders=2 в двух generated windows |
| Существующие выбранные smoke regressions | 30/30 |
| PostgreSQL offline/contract checks | 20/20 |
| PostgreSQL live integration | SKIP: тестового сервера и Docker нет |

Live SQL checks покрывают обычные поля и metadata, пустую таблицу, недоступное подключение, неверный SELECT, aliases с пробелами/точкой, NULL, template columns, фильтр, несколько DataGrid, JSON Save/Load, Multi Form, reopen и cancellation. Настоящий command timeout проверен в обоих Preview при удержании блокировки в отдельной тестовой транзакции; UI продолжает отвечать, после timeout остаются колонки с нулём строк и сообщением. Транзакция откатывается в finally.

Contract checks отдельно покрывают provider selection после JSON, cache identity, точный регистр и dotted identifiers PostgreSQL, оба native Preview с двумя различающимися строками, DataContext/ItemsSource, schema-only без запроса, unconfigured SQL без demo, static Manual source, delayed async completion, закрытие и смешанные SQL Server/PostgreSQL/без источника формы. Seeded cache и provider hook обозначены CONTRACT; они не являются реальным PostgreSQL integration test.

Existing regression selection покрывает SQL provider/cache/aliases, DataGrid, DLL fallback, Legacy/AXAML Preview, Multi Form, export builds, Standalone startup и ранее добавленные Toolbox controls. Полный smoke-набор репозитория не запускался.

Export pipeline не менялся. SQL generated runtime проверен с существующим opt-in экспортом Windows-auth connection string, в которой нет пароля. Generated ViewModel самостоятельно загрузил данные настоящей БД. Это отдельный процесс, не подстановка объектов в collection внутри Designer.

## Повторение

На Windows с установленными LocalDB и sqlcmd:

```powershell
.\smoke-tests\run-datagrid-preview-regression.ps1 -LocalDb -IncludeExport
```

Скрипт создаёт private instance с GUID, отдельную БД и fixture, выполняет проверки, затем останавливает и удаляет только созданный им instance. Без `-LocalDb` выполняются contracts и явно настроенные live checks из `FORMDESIGNER_TEST_SQLSERVER`. Для такого подключения используйте отдельную тестовую БД с fixture: lifecycle/timeout тесты временно удерживают табличную блокировку. Пароль задаётся через локальную environment configuration и не передаётся в CLI arguments.

Только контракты без сервера:

```powershell
dotnet run --project .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -- --datagrid-preview-checks
```

Логи текущего запуска в ignored `artifacts/datagrid-preview-p0/`: `baseline-before-fix.log`, `preliminary-before-cache-fix.log`, `baseline.log` (полный прогон 43/43), `final-contracts.log` (финальные 18/18 после guard пустой схемы), `source-workflow.log`, `export-runtime.log`, `generated-runtime.txt`, `regression.log`, `postgres-offline.log`, `solution-build.log`, `build.log`. Structured examples: `legacy-diagnostics.json`, `axaml-diagnostics.json`; изображения: `legacy-real-sql.png`, `axaml-real-sql.png`.

## Файлы

- `DesignerSystem/Binding/PreviewRowsLoader.cs`, `SqlPreviewDataLoader.cs`
- `Models/DataGridPreviewDiagnostic.cs`, `Models/ExportedAxamlPreviewModel.cs`
- `Services/SqlPreviewErrors.cs`
- `ViewModels/MainWindowViewModel.cs` — только Preview snapshot name/field maps
- `Views/PreviewWindow.axaml.cs`, `Views/PreviewWindow.DataGrid.cs`
- `Views/MainWindow.axaml.cs` — передача diagnostics в существующий Output
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
- `Program.DataGridPreviewRegression.cs`, `Program.DataGridPreviewContracts.cs`, `Program.DataGridPreviewExport.cs`, `Program.DataGridPreviewSource.cs` в том же каталоге
- `smoke-tests/SqlServer/DataGridPreviewFixture.sql`, `smoke-tests/run-datagrid-preview-regression.ps1`
- `Docs/DataGridPreviewRegression.md`, `README.md`, `smoke-tests/README.md`

NuGet versions, TargetFramework, Avalonia/Eremex versions, VSIX/IPC, AXAML Round-trip, Toolbox, Inspector, global styles и SQL metadata queries не менялись.

## Что осталось непроверенным

- Успешное live-подключение PostgreSQL, его реальные строки/типы/null/quoted identifiers в обоих Preview и PostgreSQL generated runtime.
- Смешанный live SQL Server + PostgreSQL проект: проверена только deterministic изоляция такого состава форм и реальные SQL Server формы.
- SQL Server authentication с логином/паролем, другие версии/удалённые серверы SQL Server и конкретный пользовательский проект. Live-проверка выполнена на Windows-auth LocalDB.
- Ручной mouse/keyboard сценарий всех Settings/Data Source dialogs, Linux/macOS и полный набор smoke tests.

Таким образом, SQL Server путь подтверждён live от настроек/схемы до visual DataGrid и отдельного generated application. Для PostgreSQL устранён общий дефект cache/binding contract, но live acceptance остаётся открытым.
