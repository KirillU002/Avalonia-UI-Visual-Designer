# Avalonia UI Visual Designer

**Текущая версия:** Alpha 3.0

Avalonia UI Visual Designer - визуальный дизайнер форм для Avalonia UI. Alpha 3.0 - стабилизационный релиз после Alpha 2.0: основной фокус сделан на стабильность Multi Form, Property Inspector, Export, DataGrid, DLL Import и соответствие Preview/Export.

![C#](https://img.shields.io/badge/C%23-.NET_6-blue?style=for-the-badge&logo=csharp)
![Avalonia](https://img.shields.io/badge/Avalonia-11.1.5-purple?style=for-the-badge)
![Status](https://img.shields.io/badge/status-Alpha_3.0-orange?style=for-the-badge)

## Статус проекта

Проект находится на стадии Alpha. Основные функции уже реализованы.

## Что изменилось в Alpha 3.0

- Исправлена очистка состояния при New Project.
- Улучшена стабильность Add Form / Multi Form.
- Исправлены проблемы Property Inspector.
- Export pipeline изолирован от editor state.
- Улучшено соответствие Preview и Export.
- Исправлен DataGrid export.
- Добавлен/улучшен C# binding для DataGrid.
- Улучшен Column Editor.
- Переработан Data mode.
- Улучшен DLL Import UX.
- Добавлено удаление DLL.
- Улучшены ошибки загрузки DLL.
- Улучшены logs/settings/validate build.
- Добавлена документация для разработчиков.

## Версии

- Alpha 3.0 - текущая версия, стабилизация конструктора, Export, DataGrid, DLL Import.
- Alpha 2.0 - предыдущая версия до большого стабилизационного цикла.

## Alpha 3.0

- [Alpha 3.0](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.3.0-alpha) — текущая версия, стабилизация конструктора, Export, DataGrid, DLL Import.
  
## Alpha 2.0

Предыдущую версию можно посмотреть по коммиту:

[`29c5864908912c10713498b30def81af00ee4355`](https://github.com/KirillU002/Avalonia-UI-Visual-Designer/commit/29c5864908912c10713498b30def81af00ee4355)

## Требования

- .NET 6 SDK
- Avalonia NuGet packages 11.1.5
- Windows для desktop-сценариев разработки

Проверить SDK:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

## Быстрый запуск

```powershell
dotnet restore .\FormDesigner.sln
dotnet build .\FormDesigner.sln
dotnet run --project .\FormDesigner.csproj
```

Smoke tests:

```powershell
.\smoke-tests\run-smoke-tests.ps1
```

## Документация

- [DeveloperArchitecture](Docs/DeveloperArchitecture.md) - техническая документация для разработчиков.
- [Alpha 0.2 manual checklist](Docs/ALPHA_0_2_MANUAL_TEST_CHECKLIST.md)
- [Plugin guide](Docs/PluginGuide.md)
- [Undo/Redo smoke checklist](Docs/UndoRedoSmokeTest.md)

## Примечания

Архитектура, ключевые классы, flows, diagnostics и правила разработки описаны в [DeveloperArchitecture](Docs/DeveloperArchitecture.md).

Generated projects сейчас ориентированы на `net6.0` и Avalonia `11.1.5`. Проект не считается production-ready и может содержать Alpha-баги.
