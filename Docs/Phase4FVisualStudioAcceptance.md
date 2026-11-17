# Phase 4F — приёмка установленного VSIX

Это checklist физической приёмки, а не список автоматически выполненных проверок. Итоговые наблюдения и hashes — в `UniversalControlsAndPropertiesPhase4F.md`. Автоматические IPC/geometry tests не подтверждают работу мыши на разных DPI.

## Подготовка

1. Использовать копию проекта `Samples/VisualStudioPoC/SimpleAvaloniaApp`, а также копию большого `Views/MainWindow.axaml`. Сохранить исходные байты и SHA-256.
2. Записать commit/diff, VS/Windows/.NET/Avalonia/VSIX versions, SHA-256 VSIX и DLL установленного расширения. Одинаковый номер версии не гарантирует одинаковую сборку.
3. Установить проверяемый VSIX в отдельный экспериментальный RootSuffix. Не обновлять рабочую установку. Первый вход/настройку VS выполнить вручную.
   Запускать отдельное решение без `/Edit`: этот ключ может передать файл уже работающему обычному экземпляру VS. Перед действиями сверить PID/CommandLine с RootSuffix и фактический `VSIX_EXTENSION_ROOT_RESOLVED` в журнале.
4. Проверить путь активного AXAML по VSIX-журналу до редактирования. Одинаковое имя MainWindow.axaml не подтверждает, что открыта копия.
5. Оставить accessibility включённой. Native dump capture должен использовать отдельный каталог и protected evidence vault. Чужие UIA clients не завершать.

## Матрица

Для каждой строки записать PASS/FAIL/NOT VERIFIED, timestamp, документ, version/checksum, логи и фактический source diff. При ошибке сохранить первый результат; повтор разрешён как отдельный эксперимент с описанным изменением условий.

| № | Сценарий | Ожидаемый результат |
|---|---|---|
| 1 | Установка в тестовый RootSuffix, запуск VS | Команда в меню Средства, корректная загрузка package; версия и hash совпадают с проверяемым VSIX |
| 2 | Открытие малого и большого AXAML | Отдельный VsHost из установленного каталога; общий DesignerSurface; честные предупреждения о неподдерживаемом source |
| 3 | Выбор, PropertyGrid, изменение Content/Margin | Правильный control/типизированный editor; Apply изменяет только целевой source span; ACK подтверждает новую версию |
| 4 | Ctrl+S, закрытие и повторное открытие | Файл соответствует VS buffer; модель соответствует сохранённому AXAML |
| 5 | Toolbox Drop в Grid/Canvas/StackPanel/DockPanel/WrapPanel | ParentId, child order и selection корректны; Grid не получает Canvas.Left/Top |
| 6 | Remove subtree, Reorder, Reparent | Внутренний XML, неизвестные attributes, bindings и внешние comments сохранены; attached properties соответствуют новому parent |
| 7 | Undo/Redo до и после Apply | Одна запись на действие; source/model/map согласованы; точное восстановление текста в пределах контракта |
| 8 | Binding/StaticResource/DynamicResource/Styles/Templates | Не изменяются посторонние выражения/opaque nodes; небезопасные операции отклоняются |
| 9 | Ручная правка VS перед Apply | Конфликт checksum/version; новая правка пользователя не перезаписывается; явный Reload восстанавливает фактический buffer |
| 10 | Delayed/duplicate ACK | Более новые изменения Designer остаются dirty; один patch не применяется дважды |
| 11 | Disconnect после применения source, до ACK | Нет автоматического повторного Insert; требуется проверка фактического buffer и явный Reload |
| 12 | Два файла, переключение с dirty Designer | Нет молчаливой потери edits: Apply к исходному документу или осознанный Reload; старый ACK не применяется к другому файлу |
| 13 | Закрытие source-документа во время операции | Нет записи в другой buffer; ошибка/неопределённый результат явно отображаются |
| 14 | Закрытие и повторный запуск VsHost | Соединение восстанавливается; старый callback не отключает новое; source baseline проверяется |
| 15 | Закрытие VS при чистом Designer | Штатный HostShutdown; нет оставшегося чистого дочернего процесса |
| 16 | Закрытие VS при dirty Designer/pending ACK | Неприменённые edits не уничтожаются; оставшееся окно явно сообщает причину; принудительное завершение не используется |
| 17 | Preview open/resize/close/reopen | Native controls соответствуют поддерживаемому subset; source не изменяется |
| 18 | 50 open/close AXAML, Preview, VsHost | Записаны exit codes, retained objects, memory/handles/threads; собственные UIA clients завершены перед post-client GC |

## DPI и физический Drag & Drop

Отдельно повторить строки 3–7 и 17 при Windows scale **100%, 125%, 150%**. Для каждого scale проверить zoom Designer 50/100/150%, scroll, nested layout, тонкие controls, resize handles, Grid cell placement, Canvas coordinates и перенос между контейнерами. Editor zoom не равен Windows DPI. При нескольких мониторах добавить перенос окна между мониторами. Изменения системного масштаба выполняет пользователь; автоматическая смена глобальных настроек не требуется.

Заполнить отдельную таблицу:

| Windows scale | Select/hit test | Toolbox Drop | Resize | Reparent | Scroll/zoom | Доказательство |
|---|---|---|---|---|---|---|
| 100% | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | — |
| 125% | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | — |
| 150% | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | — |

При AV остановить серию и сохранить full dump, native/managed stacks, modules, metadata и SHA-256 существующим forensic pipeline. Отсутствие сбоя и успешный Memory Gate не означают устранение исторического `ProviderCallouts::GetPatternProvider` crash.
