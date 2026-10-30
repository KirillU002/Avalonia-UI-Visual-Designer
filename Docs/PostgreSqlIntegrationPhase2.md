# PostgreSQL Phase 2 — integration and stabilization

Дата проверки: 30.10.2026. **Acceptance с реальным PostgreSQL не подтверждён.**
На этой машине не найдены Docker CLI/служба/процессы и PostgreSQL CLI/служба/процессы
в PATH и стандартных местах установки. Порт 127.0.0.1:5432 закрыт,
FORMDESIGNER_TEST_POSTGRES не задан. Системное ПО не устанавливалось, к внешним
или пользовательским базам подключений не выполнялось.

## 1. Root cause CS0023

В `AvaloniaDesigner.VSIX/VsTextPatch.cs:52` массив `TextEditPayload[]` вызывал
`ordered.Reverse().ToArray()`. VSIX использует LangVersion=latest; установленный
SDK 10.0.402 применяет C# 14 span conversions. Extension resolution выбирает
`System.MemoryExtensions.Reverse(Span<T>)`, возвращающий void, вместо LINQ
`Enumerable.Reverse(IEnumerable<T>)`. Поэтому следующий `.ToArray()` даёт CS0023.
Это [известное изменение компилятора](https://github.com/dotnet/roslyn/issues/80259),
а не ошибка PostgreSQL или протокола bridge.

Минимальное исправление: `Enumerable.Reverse(ordered).ToArray()`. TFM, LangVersion,
версии NuGet и архитектура VSIX не менялись. Regression assertion дополнительно
проверяет descending order edits и отсутствие изменения массива вызывающей стороны;
прежние CRLF/overlap проверки сохранены.

## 2. Сборка solution

`dotnet build .\FormDesigner.sln --no-restore -m:1 --verbosity minimal`:
**0 errors, 1 warning NU1603** — прежняя подстановка VSSDK analyzer 17.7.22 вместо
отсутствующей 17.7.20. Пакеты ради предупреждения не обновлялись.

После исправления компиляции ограниченная среда завершила VsixUtil.exe с MSB6006.
Повторная сборка вне sandbox прошла, включая упаковку
`AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`.
Лог: `artifacts/postgres-phase2/solution-build.log`.
Содержимое нового VSIX проверено: Npgsql.dll находится только в VsHost/;
в корне пакета нет Npgsql/Avalonia/Eremex runtime assemblies. Результат:
`artifacts/postgres-phase2/vsix-isolation.json`.
Установка расширения в пользовательский Visual Studio не выполнялась.

## 3. Воспроизводимый PostgreSQL fixture

Добавлен `smoke-tests/run-postgres-integration.ps1` (PowerShell 7):

```powershell
.\smoke-tests\run-postgres-integration.ps1 -ProbeOnly
.\smoke-tests\run-postgres-integration.ps1 -UseDocker
```

Docker-вариант использует официальный `postgres:16-bookworm`, новый случайный
container name, tmpfs без host volumes и случайный порт только на 127.0.0.1.
Пароль генерируется в памяти и передаётся Docker через имя переменной окружения,
не через значение в argv. Runner ждёт pg_isready, выполняет проверки и удаляет
только созданный им контейнер в finally. Он восстанавливает прежние env values.
Полный docker inspect/Config.Env и пароль не выводятся. В логе остаётся image ID;
для строгой CI-воспроизводимости можно передать `-Image postgres@sha256:<digest>`.
Используется [официальный PostgreSQL image](https://hub.docker.com/_/postgres/).
Дополнительных NuGet-зависимостей Testcontainers не потребовалось.

На текущей машине Docker-ветка **не исполнялась**. Проверены синтаксис скрипта,
ProbeOnly и безопасный отказ при отсутствии Docker/конфигурации. Сам скрипт ничего
не устанавливает и не запускает неизвестные локальные базы.

Для явно разрешённой отдельной тестовой БД:

```powershell
# Использовать только выделенную тестовую БД с password authentication.
# Сервер и учётную запись создаёт владелец среды; эти команды не создают БД.
$testCredential = Get-Credential -UserName designer_test
$testConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$testConnection['Host'] = '127.0.0.1'
$testConnection['Port'] = 5432
$testConnection['Database'] = 'designer_integration'
$testConnection['Username'] = $testCredential.UserName
$testConnection['Password'] = $testCredential.GetNetworkCredential().Password
$testConnection['Ssl Mode'] = 'Prefer'
$previousTestConnection = $env:FORMDESIGNER_TEST_POSTGRES
try {
    $env:FORMDESIGNER_TEST_POSTGRES = $testConnection.ConnectionString
    .\smoke-tests\run-postgres-integration.ps1 -UseExistingTestDatabase
} finally {
    $env:FORMDESIGNER_TEST_POSTGRES = $previousTestConnection
    $testConnection.Clear()
}
```

Не выводить `$testConnection` или env value в консоль. Нужны права создать schema,
table и view в этой тестовой БД. Automated fixture создаёт только случайную схему
`Designer.Test_<GUID>` и удаляет только её; Database/role не удаляются.

`smoke-tests/PostgreSql/fixture.sql` содержит:

| Объект | Проверяемые данные |
|---|---|
| Customers | Id, Name, Email, City; 3 строки, PK, NOT NULL и NULL |
| Orders | Id, CustomerId, OrderDate, Amount; 3 строки, FK, timestamp, numeric(12,2), nullable Amount |
| EmptyTable | Id, Name; 0 строк |
| CustomersView | Безопасное view над Customers |
| Order.Items | Имя с точкой, разные name/Name, колонка display.name |

Customers: 101/Alice North/Kazan, 205/Boris River/Perm, 309/Clara Pine/NULL.
Orders: 1001/1250.50, 1002/42.75, 1003/NULL. Assertions проверяют конкретные
значения и все три строки, включая NULL; одного совпавшего row count недостаточно.

## 4. Реальное подключение и Settings

**Не выполнено: PostgreSQL отсутствует.** Проверка закрытого локального порта
и offline settings save/load не являются успешным подключением к серверу.

Ручная приёмка после предоставления тестовой БД:

1. Для доступного psql загрузить fixture в выделенную БД: передать пароль через
   временный PGPASSWORD, `-v ON_ERROR_STOP=1 -f smoke-tests/PostgreSql/fixture.sql`.
   Восстановить прежний PGPASSWORD в finally. Файл создаёт схему Designer.Phase2;
   повторный запуск при существующей схеме останавливается, не перезаписывая её.
2. Запустить настоящий Designer. Settings → PostgreSQL: Host/Port/Database/Username,
   пароль в masked поле, Default Schema = `Designer.Phase2`.
3. Проверить подключение, сохранить, закрыть процесс Designer и открыть заново.
4. Проверить поля и повторное подключение. На Windows при RememberPassword=true
   пароль восстанавливается через DPAPI текущего пользователя; при false его нужно
   ввести снова. Не снимать пароль в открытом виде и не публиковать app settings.

## 5. Schema discovery

**Реальные результаты: SKIP.** Gated suite подготовлен для схем, tables/views,
Id primary key, nullable Email/Amount, NOT NULL Name/OrderDate, integer/text/numeric/
timestamp, quoted names Order.Items, name/Name/display.name. SQL adapters используют
metadata parameters и quoting целого identifier; SQL Server SQL не переписывается.
Root cause возможных серверных metadata defects пока не установлен: воспроизведения
на сервере нет, исправления на основании предположений не вносились.

## 6–7. Standard/Eremex DataGrid, Preview и AXAML Preview

**Реальные значения в обоих Preview: SKIP.** Gated standard suite проверяет
реальные Customers и конкретные имена в ItemsSource обоих Preview. Отдельный
`--postgres-eremex-checks` выполняется свежим процессом с ранней загрузкой plugin,
как обычный startup; проверяет те же значения для Eremex DataGridControl.

Ручная приёмка: Data → PostgreSQL → схемы → Designer.Phase2 → Customers → колонки;
привязать источник к стандартному DataGrid, открыть оба Preview, проверить 3 разные
записи. Повторить с EmptyTable (0 записей) и Eremex DataGridControl с доступной
лицензией/trial. Проверить отсутствие синтетических строк при ошибке. Отмена
длительного запроса должна оставлять UI отзывчивым; после ошибки повторный запрос
должен загружать данные. Эта ручная проверка не выполнена.

## 8. Runtime экспортированного приложения

**Настоящий PostgreSQL → отдельный процесс exported app: НЕ ПРОВЕРЕНО.**
Restore/build проектов без сервера проверяется offline smoke suite. Gated live
suite также вызывает loaders настоящих скомпилированных generated ViewModel с
реальным Npgsql и сверяет значения; это не заменяет запуск всего приложения.

Для приёмки экспортировать проект из предыдущего сценария, затем в каталоге export:

```powershell
dotnet restore .\ExportedProject.csproj
dotnet build .\ExportedProject.csproj --no-restore -m:1
$runtimeCredential = Get-Credential -UserName designer_test
$runtimeConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$runtimeConnection['Host'] = '127.0.0.1'; $runtimeConnection['Port'] = 5432
$runtimeConnection['Database'] = 'designer_integration'
$runtimeConnection['Username'] = $runtimeCredential.UserName
$runtimeConnection['Password'] = $runtimeCredential.GetNetworkCredential().Password
$runtimeConnection['Ssl Mode'] = 'Prefer'
$previousRuntimeConnection = $env:FORMDESIGNER_POSTGRES_CONNECTION_STRING
try {
    $env:FORMDESIGNER_POSTGRES_CONNECTION_STRING = $runtimeConnection.ConnectionString
    dotnet .\bin\Debug\net6.0\ExportedProject.dll
} finally {
    $env:FORMDESIGNER_POSTGRES_CONNECTION_STRING = $previousRuntimeConnection
    $runtimeConnection.Clear()
}
```

Имена csproj/dll заменить фактическими. Для разных источников использовать scoped
ключи форм/источников из README.generated.md; они приоритетнее общей переменной.
Перед запуском проверить, что действующие scoped keys и aliases также указывают
на эту тестовую БД; для приёмки предпочтительны явно заданные scoped keys.
Проверить реальные Customers в открытом окне, точные значения и 3 разные записи,
затем EmptyTable. До передачи env просканировать generated C#/AXAML/README/config
на отсутствие пароля. Экспорт не должен копировать пароль Designer.
На текущей машине .NET runtime 6.0.36 и 8.0.32 имеются; TFM не менялся.

## 9. Multi Form

Offline экспорт 2 PostgreSQL + 1 SQL Server сохраняется в smoke suite. Gated live
suite подготовлен для двух PostgreSQL форм Customers и Orders с независимыми
коллекциями и loaders скомпилированных ViewModel. Реальные результаты **SKIP**.

При ручной приёмке создать Form1/Customers, Form2/Orders и кнопку OpenForm для Form2;
переключить формы в Designer, проверить оба Preview и открыть Form2 в exported app.
Должны отображаться соответственно Customers IDs 101/205/309 и Orders IDs
1001/1002/1003. Form3/SQL Server добавлять только при доступном разрешённом тестовом
SQL Server. Такого сервера не предоставлено: mixed runtime scenario не проверен.

## 10. Секреты: текущий статус и минимальная архитектура

Текущая реализация WindowsSecretProtection — только current-user Windows DPAPI.
Она не универсальна для Linux/macOS. На других ОС или при невозможности расшифровать
секрет возвращается пустая строка; открытый пароль не сохраняется. Пользователь
вводит пароль через Settings для текущей сессии. Нативные Linux/macOS backends и
автоматический password prompt при подключении в Phase 2 не реализованы.
Изолированная проверка на этой Windows подтвердила доступность DPAPI, round-trip
пароля без plaintext в JSON, удаление при RememberPassword=false и безопасный
отказ при повреждённом ciphertext. Лог: artifacts/postgres-phase2/secret-probe.log.

Минимальное предлагаемое расширение: один `ISecretStore` с async Read/Write/Delete
и capability/result Available/Unavailable/Locked/NotFound, внедряемый в существующий
AppSettingsService. В локальном settings хранить provider id и opaque secret reference;
project JSON, экспорт и diagnostics не получают ни пароль, ни ссылку хранилища.

| Платформа | Backend | Безопасный отказ |
|---|---|---|
| Windows | Существующий DPAPI текущего пользователя, совместимый read старого ciphertext | Секрет только в памяти, повторный masked prompt |
| Linux | [Secret Service](https://specifications.freedesktop.org/secret-service/latest/) через libsecret/D-Bus при доступном desktop service | При отсутствии/locked service ничего не писать на диск |
| macOS | [Keychain Services](https://developer.apple.com/documentation/security/using-the-keychain-to-manage-user-secrets), SecItemAdd/CopyMatching/Update/Delete | При отказе доступа ничего не писать на диск |

Собственной криптографии, plaintext-файлов или shell argv с паролем не требуется.
При выключении RememberPassword удалять сохранённый secret reference/backend item;
перед повторным подключением использовать память или запросить пароль. Реальные
native-store проверки на Linux/macOS требуют этих ОС и в данной среде не выполнены.
Предложенная архитектура описана отдельно от фактически реализованных backends.

## 11. Regression и два прежних fallback failures

SQL failure: общий early return для Demo возвращал SampleRows для SQL Server,
не достигая старой нижней ветки с DemoData. Исправлена метка SQL demo; DLL/manual
SampleRows и PostgreSQL real-only policy сохранены.

DLL failure имеет две причины: исходный тест называл fallback включённым, но не
выставлял AllowPreviewSampleFallback; loader также игнорировал этот отдельный флаг.
В setup теста включён ровно этот флаг. Assertions SampleRows/status/rows не менялись.
Loader разрешает DLL fallback только после недоступности real data при явном opt-in;
SchemaOnly не создаёт samples, доступные real rows имеют приоритет, cancellation
не превращается в samples. Добавлены проверки opt-in, real-row priority и cancellation.
Default DLL sources не начинают показывать фиктивные строки автоматически.

Результат: **66/66 уникальных выбранных smoke scenarios**, плюс **20/20 PostgreSQL
offline/contract checks**. По группам:

| Группа | Результат | Лог в artifacts/postgres-phase2 |
|---|---|---|
| SQL/DLL/PostgreSQL/BindingSource/DataGrid/Preview/AXAML/Multi Form/JSON/Standalone/Export/Build Validation | 51/51 | core.log |
| Eremex controls/theme | 3/3 | eremex.log |
| VSIX/VsHost/IPC/AXAML patches | 10/10 вне sandbox | ipc-rerun.log |
| Финальная проверка двух старых failures, DLL opt-in/real rows/cancel, descending text patches/CRLF | 6/6 (4 повторных, 2 дополнительных) | final.log |
| PostgreSQL settings/security/query policy/error paths | 20/20 offline | postgres-checks.log |
| PostgreSQL-only/mixed/quoted/Eremex export | 4/4 с restore/build, входят в 51/51 | core.log |
| Настоящий PostgreSQL, standard и Eremex suites | SKIP | PostgreSQL отсутствует |

Первый IPC запуск в sandbox дал AccessDenied в 8/10 сценариях, повторный вне sandbox
прошёл 10/10. Smoke runner собирается с 0 errors / 0 warnings. Сводка каждого
уникального сценария: artifacts/postgres-phase2/validation-summary.json.
Это выбранный набор по затронутым paths, не полный долгоживущий runner всех сценариев. Ранее
известный Eremex late-load дефект одного долгоживущего процесса не объявляется
исправленным: Eremex проверяется отдельным процессом с ранней загрузкой plugins.

## 12. Что осталось непроверенным

- Любой положительный runtime с настоящим PostgreSQL и весь главный acceptance path.
- Реальный Settings test connection/save/process restart/reconnect.
- Серверные schemas/tables/views/columns/PK/nullable/data types/quoted names.
- Standard/Eremex DataGrid и оба Preview с настоящими строками и пустой таблицей.
- Ошибочный пароль, отсутствующая БД/таблица, SQL syntax, выполняющийся timeout/cancel,
  отзывчивость UI и повторное подключение после этих ошибок на PostgreSQL-сервере.
- Запуск exported app отдельным процессом с реальными данными и две реальные формы.
- Mixed SQL Server/PostgreSQL runtime, реальный SQL Server и установка VSIX в VS.
- Linux/macOS native secret stores и UI на этих ОС.

Integration fixture не считается выполненным тестом. Docker script подготовлен,
но запуск контейнера не проверен: Docker отсутствует. Факт успешного build не
подменяет PostgreSQL runtime acceptance.

## Файлы Phase 2

- AvaloniaDesigner.VSIX/VsTextPatch.cs
- DesignerSystem/Binding/PreviewRowsLoader.cs
- smoke-tests/FormDesigner.ExportSmokeTests/Program.cs
- smoke-tests/FormDesigner.ExportSmokeTests/Program.DataFallback.cs (новый)
- smoke-tests/FormDesigner.ExportSmokeTests/Program.AxamlHardening.cs
- smoke-tests/FormDesigner.ExportSmokeTests/Program.PostgreSql.cs
- smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj
- smoke-tests/PostgreSql/fixture.sql (новый)
- smoke-tests/run-postgres-integration.ps1 (новый)
- Docs/PostgreSqlIntegrationPhase2.md (новый)
- README.md, smoke-tests/README.md

Новые NuGet-пакеты Phase 2 не добавлялись. Avalonia/Eremex/Toolkit/Npgsql/TFM и
dependency isolation VSIX/VsHost сохранены.
