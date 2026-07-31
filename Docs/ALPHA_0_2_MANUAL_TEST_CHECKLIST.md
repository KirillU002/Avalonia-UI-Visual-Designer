# Alpha 0.2 Manual Test Checklist

Цель проверки: пройти основной пользовательский workflow без ручного исправления generated code.

## 1. Запуск

- [ ] Установлен .NET 6 SDK.
- [ ] `dotnet restore .\FormDesigner.sln` проходит.
- [ ] `dotnet build .\FormDesigner.sln` проходит.
- [ ] Приложение запускается.

## 2. Новый проект и формы

- [ ] Создать новый project.
- [ ] В Project Explorer видны `Forms`, `Assets`, `Export`.
- [ ] `Add Form` создаёт `Form2`.
- [ ] `Form1` и `Form2` открываются во вкладках.
- [ ] Переключение форм не показывает элементы другой формы.

## 3. Controls

- [ ] На `Form1` добавить `Button`, `DataGrid`, `TextBox`, `CheckBox`.
- [ ] Элементы выделяются на canvas и в structure tree.
- [ ] Property Inspector показывает свойства выбранного элемента.
- [ ] Drag/resize не создаёт фантомные элементы после переключения форм.

## 4. BindingSource и DataGrid

- [ ] Во вкладке `Data` создать `BindingSource`.
- [ ] Добавить поля `Id`, `Name`, `Email`, `Status`.
- [ ] Назначить BindingSource на `DataGrid`.
- [ ] Создать/обновить колонки DataGrid из BindingSource.
- [ ] В Export checklist указано `Real Avalonia DataGrid`, если выбран real mode.
- [ ] Required NuGet показывает `Avalonia.Controls.DataGrid 11.1.5`.

## 5. Logic / Interactions

- [ ] Для кнопки добавить `Button.Click -> OpenForm -> Form2`.
- [ ] Для DataGrid добавить `SelectionChanged -> TextBox.Text = Name`.
- [ ] Для CheckBox добавить `Checked/Unchecked -> Show/Hide block`.
- [ ] Logic показывает interactions только выбранной/активной формы.
- [ ] При удалённой target form diagnostics показывает понятную ошибку.

## 6. Preview

- [ ] Preview открывается.
- [ ] Кнопка на `Form1` открывает preview `Form2`.
- [ ] DataGrid selection заполняет TextBox.
- [ ] CheckBox показывает/скрывает блок.
- [ ] Ошибки preview не роняют редактор и видны в diagnostics/output.

## 7. Export Pipeline

- [ ] Открыть `Code / Export`.
- [ ] Нажать `Refresh`.
- [ ] Generated files содержат `MainWindow.axaml`, `MainWindow.axaml.cs`, `Form2.axaml`, `Form2.axaml.cs`.
- [ ] Required packages корректны.
- [ ] Export diagnostics не содержит blocker errors.
- [ ] `Validate build` проходит.
- [ ] Output содержит лог build validation.

## 8. Export to project

- [ ] Выполнить export в отдельную папку.
- [ ] Проверить, что файлы записаны без неожиданного overwrite.
- [ ] В target project установлен `Avalonia.Controls.DataGrid 11.1.5`, если используется Real DataGrid.
- [ ] `dotnet build` target project проходит.
- [ ] Exported `MainWindow` открывает `Form2`.

## 9. Save/load

- [ ] Сохранить project.
- [ ] Закрыть и открыть project заново.
- [ ] Формы, controls, BindingSource, columns и interactions восстановлены.
- [ ] Dirty marker корректно появляется и исчезает после save.

## 10. Known limitations

- Layout System v2 остаётся experimental.
- Smoke tests не заменяют ручную проверку preview и canvas drag/drop.
- Plugin controls без runtime DLL экспортируются как placeholder.
- Export overwrite workflow требует ручного подтверждения и проверки.
