# Phase 4D — ручная приёмка установленного VSIX

Статус: **НЕ ВЫПОЛНЕНА**. Visual Studio Community 2022 17.14.37806.7 установлена. При попытке интерактивной проверки инструмент получил `FrameArrived timed out`, затем `coordinate input geometry is unavailable`. Чтение accessibility tree подтвердило открытый sample AXAML, но не выполнение операций. Открытая пользовательская Visual Studio не закрывалась. Установка текущего VSIX, DPI и физический Drag & Drop автоматизированными IPC-тестами не подтверждаются.

Проверку провести на копии `Samples/VisualStudioPoC/SimpleAvaloniaApp`, сохранив исходный AXAML и его SHA-256. Использовать собранный текущий VSIX для VS 2022; не считать наличие более старого установленного extension проверкой текущей сборки. Записать VS/VSIX/Windows versions, DPI монитора, commit и working-tree diff.

| Шаг | Действие | Проверяемый результат |
|---|---|---|
| 1 | Установить VSIX; запустить VS; открыть AXAML через Designer | VsHost — отдельный процесс; импорт и дерево отображаются; в devenv нет загруженных Avalonia assemblies |
| 2 | Вставить Button/TextBox/TextBlock/CheckBox и вложенный StackPanel/Grid/Canvas/Border | Правильный parent, selection и Explorer; в Grid отсутствуют Canvas.Left/Top |
| 3 | Apply, затем Ctrl+S | VS buffer получает минимальные edits; ACK снимает dirty только для отправленного snapshot; файл совпадает с buffer |
| 4 | Remove subtree; Reorder в StackPanel/DockPanel; Reparent Grid → Canvas → StackPanel | Правильные порядок и attached properties; внутренний XML, Binding и неизвестные атрибуты сохраняются |
| 5 | Undo/Redo до и после ACK, несколько операций подряд | Исходный текст восстанавливается; один history entry на действие; актуальные IDs/selection/PropertyGrid |
| 6 | Изменить исходный AXAML вручную между подготовкой и Apply patch | Stale version/checksum отклоняется; новые изменения пользователя не перезаписываются |
| 7 | Изменить Designer после отправки patch, до ACK | ACK сохраняет отправленный snapshot; более новые изменения остаются dirty и доступны следующему Apply |
| 8 | Оборвать соединение после применения patch в VS до ACK | Нет автоматического повторного Insert; Apply заблокирован до явного Reload; Reload берёт фактический buffer |
| 9 | Закрыть документ с pending patch; открыть другой; перезапустить VsHost/VS | Старый ACK не применяется к новой сессии; нет автоматической потери несохранённых Designer edits |
| 10 | Открыть большой `Views/MainWindow.axaml`, выполнить Insert/Remove/Reorder/Reparent и Preview | UI показывает busy, нет повторного конфликтующего command; геометрия и порядок native projection соответствуют поддерживаемому subset |
| 11 | Повторить на DPI 100%, 125%, 150%, при scroll/zoom и смене монитора | Hit testing, drop coordinate, resize handles, selection и menus совпадают с визуальными границами |
| 12 | Оставить accessibility включённой; открыть/закрыть Preview и ContextMenu | Нет подтверждённых новых сбоев; при AV сохранить full dump, metadata, stacks и SHA-256 существующим capture pipeline |

Для каждого шага записать PASS/FAIL, фактический AXAML до/после, логи VSIX/VsHost и screenshot при доступном захвате. Не заменять FAIL повторным запуском; повтор с изменёнными условиями фиксировать отдельно. DPI editor zoom не равен DPI Windows. Исторический UI Automation native crash остаётся UNRESOLVED независимо от результатов этой проверки.
