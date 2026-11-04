# Avalonia UI Visual Designer — Beta 0.1

**Предварительный выпуск (GitHub Pre-release).** Версия продукта `0.1.0-beta.1`, аннотированный Git tag `v0.1.0-beta.1`. Версия Visual Studio Extension — **0.1.18**, независимо от версии Standalone. Предыдущие Alpha tags/releases не изменяются.

Это предварительная Windows Beta, не обещание production-ready стабильности. Фактические результаты и условия приёмки: [Beta 0.1 RC](../Beta01ReleaseCandidate.md), [Beta Readiness Audit](../BetaReadinessAudit.md).

## Что изменилось относительно Alpha

- **Grid-first:** новые проекты и формы создаются с настоящим Grid Root. Canvas остаётся доступным для свободного позиционирования. Старые Canvas JSON и JSON без новых layout metadata открываются с исторической семантикой, без автоматической миграции.
- **Layout Designer:** визуальные Grid definitions Auto/Star/Pixel, Grid cell placement, nested Grid/StackPanel/DockPanel/WrapPanel/Canvas, Drop/Reparent, selection, resize и Undo/Redo.
- **Preview:** AXAML Preview по умолчанию для JSON-проектов, Legacy Preview доступен в Settings. Стандартные controls, вложенная hierarchy, layout, DataGrid и Export сравниваются по native visual tree/Bounds. Ошибки отображения диагностируются, а не молча скрываются.
- **Multi Form / Logic / Export:** независимые формы, настройки и источники; AXAML/C#/ViewModel, folder/ZIP export, restore/build и работа generated окон с навигацией.
- **AXAML Import:** расширен поддерживаемый subset вложенных layouts/items, сохраняются comments, styles, bindings и unknown syntax; произвольный AXAML остаётся частично поддерживаемым.
- **Visual Studio:** внешний VsHost, Named Pipes, открытый AXAML text buffer, minimal patches, Apply/ACK и защита version/checksum. Avalonia/Eremex не являются in-process зависимостями bridge в devenv.
- **BindingSource / DLL Import:** настройка источников данных и импорт доступных метаданных DLL; секреты PostgreSQL Designer не переносятся в generated проект.
- **DataGrid:** static/empty sources и реальные SQL Server строки, columns/bindings, несколько grids/forms и адаптивный resize.
- **Производительность:** повторное использование visual wrappers и контекстных меню, сокращение лишних UI updates, session-scoped cache описаний Undo History. Snapshot storage и native Avalonia layout semantics сохранены; большие формы по-прежнему требуют дальнейшей оптимизации.

## Исправления сохранности данных и состояния

Предыдущий аудит воспроизвёл и исправил **3 P0 + 3 P1**:

1. Временная ошибка чтения больше не удаляет recovery.
2. Повторяющиеся ControlId, включая различия только регистра, отклоняются до замены текущей сессии.
3. Отложенный recovery защищён от открытия/сохранения другого документа. До Restore/Delete autosave новой работы приостановлен с сообщением; обычный Save JSON доступен.
4. Удаление контейнера с детьми — атомарный Undo шаг, включая Delete сразу после переключения формы.
5. Refresh списка режимов BindingSource больше не создаёт ложную правку и не сбрасывает Redo.
6. Полностью записанный autosave публикуется атомарно: существующие readers сохраняют прежний snapshot, новые получают новый.

Preview lifecycle ранее исправлен для animation clock и native UI Automation provider. Общий backend negative control обычных Window всё ещё может воспроизводить retention; это ограничение не скрывается успешными Preview tests.

## Установка Standalone

1. На [странице Beta 0.1](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1) скачайте `Avalonia-UI-Visual-Designer-Beta-0.1-win-x64.zip` и `SHA256SUMS.txt`.
2. Сверьте SHA-256 с `SHA256SUMS.txt`, распакуйте архив целиком в отдельную папку.
3. Запустите `FormDesigner.exe`. Windows x64 self-contained пакет содержит .NET 8 runtime; Visual Studio, SDK и Eremex для запуска не нужны.
4. Для Build Validation/restore/build экспортированных проектов нужен SDK. Default generated projects сохраняют `net6.0` и Avalonia 11.1.1: соответствующий runtime нужен для их запуска. TFM в этом этапе не обновлялся.
5. Не заменяйте отдельные DLL из другого RC. Сохраните резервную копию важных пользовательских проектов перед использованием предварительной версии.

Linux/macOS и Windows ARM64 runtime не проверены и в эту Beta-поставку не входят. Avalonia cross-platform API сам по себе не является подтверждением поддержки платформы.

## Visual Studio Extension

1. Скачайте `Avalonia-UI-Visual-Designer-Beta-0.1.vsix` со страницы релиза, закройте Visual Studio 2022 Community x64 и установите расширение версии 0.1.18.
2. Перезапустите VS, откройте `.axaml`, выберите **Tools → Open in Avalonia UI Visual Designer**.
3. Измените поддерживаемое свойство во внешнем окне, нажмите Apply/«Применить изменения» и проверьте VS buffer. Сохранение файла выполняется обычным `Ctrl+S`.
4. VSIX Beta содержит полный self-contained Windows x64 VsHost в отдельном каталоге. Старый идентификатор расширения сохранён для обновления с 0.1.17.

Packaging/реальные process/IPC tests не заменяют ручную проверку установленного VSIX в живом IDE. Нужно проверить несколько документов, повторный запуск, внешний конфликт, ACK и закрытие host. Произвольные assemblies открытого VS solution в devenv/Designer не загружаются автоматически.

## SQL Server, PostgreSQL и plugins

- **SQL Server:** LocalDB acceptance выполняется на изолированном fixture: Settings/schema discovery, BindingSource, оба Preview, Multi Form и exported runtime с реальными cell values. Пустые/ошибочные запросы не заменяются demo rows.
- **PostgreSQL — experimental:** offline contracts/regressions выполнены; без настоящего сервера live integration не считается проверенной. Перед полноценным заявлением поддержки требуется отдельная live acceptance. Сервер автоматически не устанавливается.
- **Eremex — optional experimental:** TextEditor/DataGridControl integration сохранена, но vendor DLL/themes не включаются в стандартные Beta-пакеты. Для сборки и использования плагина нужна соответствующая лицензия/trial. Ошибка/отсутствие плагина не должны мешать стандартным controls; исходные JSON descriptors сохраняются.

## Известные ограничения

- Импортированный AXAML — отдельный source-preserving workflow. Opaque styles/resources/bindings сохраняются; произвольный AXAML не обещает полностью эквивалентный Preview приложения. Структурные операции ограничены безопасными minimal patches.
- Сохраняются latency/allocations и P95 tails больших форм на 300–500 controls. Масштабный renderer/Undo refactoring ради RC не выполнялся.
- Intermittent retention обычных Avalonia Window через UI Automation подтверждён отдельно; PreviewWindow regression проверяется отдельно и не маскирует этот negative control.
- Непарсируемый recovery сохраняется для ручного восстановления; пока причина не устранена/файл не удалён явно, autosave новой работы приостановлен.
- Physical Drag & Drop, DPI/перенос между мониторами и интерактивный Visual Studio требуют ручной acceptance. Числа автоматизированных tests не означают прохождение этих проверок.
- Пакеты предварительные и не подписаны цифровой подписью. Hash-проверка идентифицирует bytes конкретного RC, а не заменяет подпись доверенного издателя.

## Предыдущие версии

- [Alpha 4.0 — существующий tag v0.4.0-alpha.1](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/tree/v0.4.0-alpha.1), [release notes](../Alpha4ReleaseNotes.md).
- [Alpha 3.0 — существующий tag v0.3.0-alpha](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/tree/v0.3.0-alpha).
- [Alpha 2.0 — исторический commit](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/commit/29c5864908912c10713498b30def81af00ee4355), без придуманного отдельного тега.

Пакеты собираются из commit, на который указывает tag `v0.1.0-beta.1`; контрольные суммы опубликованы отдельным `SHA256SUMS.txt`. Старые Alpha tags и исторические README сохранены. Предварительный выпуск не означает завершение всех ручных acceptance-проверок.
