# Avalonia UI Visual Designer

> Desktop low-code/no-code конструктор пользовательских интерфейсов для **Avalonia UI** на платформе **.NET**.

![C#](https://img.shields.io/badge/C%23-.NET-blue?style=for-the-badge&logo=csharp)
![Avalonia UI](https://img.shields.io/badge/Avalonia_UI-Desktop_UI-purple?style=for-the-badge)
![Status](https://img.shields.io/badge/status-in_development-orange?style=for-the-badge)
![Architecture](https://img.shields.io/badge/architecture-MVVM-green?style=for-the-badge)

---

## О проекте

**Avalonia UI Visual Designer** — это desktop-приложение для визуального проектирования пользовательских интерфейсов на базе **Avalonia UI**.

Проект представляет собой визуальный редактор форм, похожий по концепции на:

- WinForms Designer
- WPF Designer
- DevExpress Form Designer

Но в отличие от них, данный инструмент ориентирован именно на **Avalonia UI** и генерацию чистого **Avalonia XAML/C# кода**, который можно использовать в обычном Avalonia-проекте.

Главная идея проекта — дать разработчику удобный визуальный инструмент для создания интерфейсов без необходимости вручную писать весь XAML-код.

---

## Основная цель

Проект позволяет:

- визуально проектировать интерфейсы;
- настраивать свойства контролов;
- работать с деревом элементов;
- связывать данные;
- задавать пользовательские interactions;
- валидировать проектируемый интерфейс;
- экспортировать готовый Avalonia XAML и C# код.

---

## Технологический стек

Проект написан с использованием:

- **C#**
- **.NET**
- **Avalonia UI**
- **MVVM**
- **Plugin-based architecture**
- **Descriptor-based architecture**

---

## Основные возможности

### Визуальный дизайнер

- Drag & Drop конструктор форм
- Canvas-based рабочая область
- Zoom и Pan
- Inline editing
- Context menu
- Lock / Unlock элементов
- Grouping / Ungrouping
- Undo / Redo
- Autosave / Recovery
- Diagnostics / Validation

### Работа с элементами

- Дерево элементов
- Descriptor-driven property panel
- Настройка свойств контролов
- Reusable templates/components
- Preview/render separation
- Runtime-safe preview execution

### Экспорт

- Экспорт в Avalonia XAML
- Экспорт C# кода
- Export checklist
- Diagnostics перед экспортом
- Portable export без лишних зависимостей
- Clean UI export
- Plugin placeholders
- Responsive layout groundwork

---

## Поддерживаемые контролы

На текущем этапе поддерживаются следующие элементы:

- Button
- TextBox
- TextBlock
- CheckBox
- Border
- Container controls
- DataGrid
- Plugin controls через descriptor/plugin architecture

---

## Descriptor-based архитектура

В проекте используется descriptor-based подход, который позволяет избежать больших `switch-case` конструкций и упростить расширение системы.

Вместо жёсткой логики для каждого контрола используются:

- Control descriptors
- Preview providers
- XAML export providers
- Binding metadata providers
- Designer registry
- Plugin contracts

Такой подход делает архитектуру гибкой, расширяемой и удобной для поддержки.

---

## Plugin Architecture

Проект поддерживает подключение внешних плагинов.

Плагины могут добавлять новые контролы и расширять функциональность дизайнера без изменения основного кода приложения.

Поддерживается:

- загрузка внешних DLL-плагинов;
- регистрация пользовательских контролов;
- preview/render/export логика;
- custom properties;
- fallback descriptors при отсутствии plugin DLL;
- plugin placeholders при экспорте.

---

## Data System

В проекте реализована система работы с данными, которая позволяет проектировать интерфейсы с учётом будущих привязок.

Возможности data system:

- BindingSource model
- Импорт моделей из DLL
- Reflection metadata providers
- Design-time schema
- Real fields vs preview sample fields
- DataGrid binding
- Binding metadata providers

Это позволяет работать не только с визуальной частью интерфейса, но и с будущими источниками данных.

---

## Interaction System

Редактор поддерживает настройку простых interactions между элементами интерфейса.

Примеры поддерживаемых сценариев:

- `Button.Click` → `ShowMessage`
- `CheckBox.Checked` / `CheckBox.Unchecked` → `Show` / `Hide`
- `DataGrid.SelectionChanged` → заполнение `TextBox`

При экспорте interactions преобразуются в C# handlers, которые можно использовать в Avalonia-проекте.

---

## Export System

Система экспорта отвечает за генерацию готового кода из визуально собранного интерфейса.

Поддерживается:

- экспорт XAML;
- экспорт C#;
- диагностика перед экспортом;
- export checklist;
- portable export;
- clean UI export;
- real Avalonia DataGrid export;
- plugin placeholders;
- responsive layout groundwork.

Экспортируемый код ориентирован на использование в обычном Avalonia-проекте.

---

## DataGrid Features

Для `DataGrid` реализован отдельный набор возможностей:

- настройка колонок;
- сортировка;
- изменение ширины колонок;
- стилизация;
- фильтры;
- grouping panel;
- preview/export modes;
- portable visual table export;
- real DataGrid export через `Avalonia.Controls.DataGrid`.

---

## UI/UX возможности

Проект включает в себя расширенные возможности интерфейса редактора:

- Descriptor-driven property panel
- Compact export UI
- Checklist diagnostics
- Responsive export panel
- Code viewer
- Advanced export settings
- Удобная работа с рабочей областью
- Быстрая настройка элементов

---

## Архитектура проекта

Проект построен вокруг следующих архитектурных принципов:

- MVVM
- Plugin contracts
- Designer registry
- Descriptor-based control system
- Preview/export separation
- Document serialization
- Runtime-safe preview execution
- Export strategies

Архитектура ориентирована на расширяемость, безопасность выполнения preview-логики и чистое разделение ответственности между частями системы.

---

## Пример рабочего процесса

1. Пользователь создаёт новый документ.
2. Добавляет элементы на Canvas через Drag & Drop.
3. Настраивает свойства элементов через Property Panel.
4. При необходимости задаёт bindings и interactions.
5. Проверяет проект через diagnostics/checklist.
6. Просматривает сгенерированный XAML/C# код.
7. Экспортирует результат в Avalonia-проект.

---

## Для кого этот проект

Проект может быть полезен:

- разработчикам Avalonia UI;
- .NET-разработчикам;
- разработчикам desktop-приложений;
- тем, кто хочет быстрее проектировать интерфейсы;
- командам, которым нужен визуальный UI designer;
- разработчикам, которым не хватает аналога WinForms/WPF Designer для Avalonia.

---

## Позиционирование проекта

**Avalonia UI Visual Designer** позиционируется как:

- визуальный редактор форм для Avalonia UI;
- low-code UI designer;
- инструмент проектирования интерфейсов;
- аналог WinForms/WPF Designer для Avalonia;
- расширяемая платформа для визуального создания UI.

---

## Текущий статус

Проект находится в активной разработке.

Основной фокус сейчас:

- развитие designer canvas;
- улучшение descriptor-based архитектуры;
- расширение plugin system;
- улучшение export system;
- развитие DataGrid features;
- повышение стабильности preview/export сценариев.

---

## Возможные направления развития

В будущем проект может быть расширен следующими возможностями:

- визуальный редактор layouts;
- расширенный binding editor;
- генерация ViewModel;
- поддержка themes/styles;
- импорт существующего XAML;
- visual state editor;
- расширенная система шаблонов;
- marketplace для плагинов;
- AI-assisted UI generation.

---

## Project Vision

Цель проекта — создать удобный, расширяемый и современный визуальный дизайнер интерфейсов для Avalonia UI, который позволит ускорить разработку desktop-приложений и сделать процесс создания UI более наглядным.

---

## License

Лицензия проекта будет указана позже.

---

## Author

Разработчик: **Кирилл Уколов**

Проект создан как инструмент для визуального проектирования Avalonia UI интерфейсов и генерации чистого XAML/C# кода.
