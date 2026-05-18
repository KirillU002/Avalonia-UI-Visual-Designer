# Undo/Redo smoke test

Короткий сценарий для проверки, что история редактора не ломает документ, preview, diagnostics и export.

1. Создать новый документ.
2. Добавить `Button`, `TextBox`, `DataGrid`.
3. Создать `BindingSource`, добавить несколько полей и привязать его к `DataGrid`.
4. Добавить interaction `DataGrid.SelectionChanged -> TextBox.Text`.
5. Сгруппировать `TextBox` и `DataGrid`.
6. Переместить группу мышью и изменить размер `DataGrid`.
7. Вставить template `Форма клиентов`.
8. Удалить часть вставленных элементов.
9. Выполнить несколько `Undo`.
10. Выполнить несколько `Redo`.
11. Открыть `Предпросмотр запуска` и проверить, что preview state не меняет документ.
12. Проверить diagnostics и export checklist: XAML/C# пересобраны, DataGrid mode и interactions отображаются актуально.

Ожидаемый результат:

- одно mouse drag/resize действие дает один undo step;
- группы, BindingSource, DataGrid columns и interactions восстанавливаются вместе с документом;
- после Undo/Redo selection, inspector, structure tree, diagnostics и export обновлены;
- runtime preview state не попадает в undo history.
