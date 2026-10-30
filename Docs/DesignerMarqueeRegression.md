# Designer Canvas — ложное Selection Marquee

Проверка выполнена 30 октября 2026 года поверх `99396e6`. Исправление касается взаимодействия с Canvas. Версии пакетов, TargetFramework, Preview, Export, AXAML Import, БД, Eremex и VSIX/IPC не изменены.

## Причина

`DesignerSurface` получает события Canvas через `AddHandler(..., Bubble, handledEventsToo: true)` и пересылает их с `sender == DesignerSurface`. Старый `DesignerCanvas_PointerPressed` не проверял `e.Handled`, левую кнопку и попадание именно в пустой Canvas. Поэтому уже обработанные Ctrl-клики, клики по заблокированным компонентам, контекстные и повторные клики могли установить `_isMarqueeSelecting`, когда перемещение компонента не запускалось.

Затем `e.Pointer.Capture(sender)` захватывал родительский `DesignerSurface`, а Move/Release были подключены к дочернему `DesignerCanvas`. Последующие события обходили Canvas; состояние оставалось незавершённым. Отсутствовали отмена по потере capture и проверка удержания левой кнопки в Move. Смена формы очищала флаг, но оставляла capture. Внешний вид на скриншоте соответствует Marquee, однако точная последовательность действий со скриншота неизвестна; воспроизведены перечисленные ошибочные пути маршрутизации.

Relay с `sender == DesignerSurface` присутствует уже в коммите `3170d90` (выделение DesignerSurface). Проверка `git blame` показывает, что обработчик Canvas с безусловным включением Marquee старше этого выделения. Бисекция конкретного пользовательского сценария не выполнялась.

Дополнительно найдены два дефекта в той же цепочке: Ctrl-флаг обнулялся до применения выделения; при смене формы во время Move оставалась глубина undo batch, поскольку загрузка формы использует `refreshEditorSurfaces: false`.

## Исправление

Использована существующая обработка selection, геометрии, Inspector и undo. Добавлен небольшой координатор временной операции: `None`, `Selecting`, `MarqueeSelecting`, `MovingControl`, `ResizingControl`, `ResizingSurface`, `ToolboxDragDrop`. Существующие флаги сохранены для совместимости с рендером и таймерами.

- Canvas принимает только необработанный левый press, у которого и `Source`, и фактический hit-test указывают на пустой `DesignerCanvas`.
- Press только подготавливает выбор. Marquee рисуется после существующего порога 4 DIP. Move без удерживаемой левой кнопки отменяет операцию.
- Владелец capture задаётся явно: Canvas для Marquee, wrapper для Move, соответствующий grip для resize компонента/формы. Ссылка на пересылающий Surface не используется как владелец.
- Release завершает операцию даже за пределами Canvas. Обработчик CaptureLost отслеживает именно текущего владельца; при отмене чужой новый capture не освобождается. Перед штатным освобождением собственный обработчик потери capture отписывается.
- Escape, потеря capture, смена документа и закрытие окна очищают временные флаги и overlays. Завершение Move/Resize освобождает live gesture/Inspector и undo batch в текущей сессии. Смена формы очищает оставшийся batch в существующем `ClearActiveDocumentTransientState`, без записи операции в историю новой формы.
- Toolbox drag исключает Marquee; Drop и выход из Canvas завершают этот режим, а `finally` после `DoDragDrop` обрабатывает завершение/отмену системного drag.
- Ctrl/Shift сохраняются до применения Marquee selection. Обычный клик на пустом Canvas очищает выбор.

Исправлены обработчики Canvas Press/Move/Release, Control Press/Move/Release, обоих resize grips, Toolbox Press/DragOver/Drop, Escape и сброса состояния документа. API `DesignerSurface` и его архитектура не менялись.

## Диагностика

Добавлены `DESIGNER_POINTER_PRESSED`, `DESIGNER_POINTER_RELEASED`, `DESIGNER_POINTER_CAPTURE_LOST`, `DESIGNER_INTERACTION_STARTED`, `DESIGNER_INTERACTION_COMPLETED`, `MARQUEE_STARTED`, `MARQUEE_CANCELLED`, `MARQUEE_COMPLETED`.

Запись содержит ControlId, координаты в Canvas, HitTestTarget, InteractionMode, состояние левой кнопки, наличие capture, DocumentId и причину завершения. Для событий без PointerEventArgs используются последние известные координаты/состояние кнопки. Записи направляются в существующую диагностику документов; в памяти сохраняется не более 512 последних записей. Каждый PointerMoved не логируется.

## Проверки

Новый запуск:

```powershell
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --designer-pointer-checks
```

Проверки создают настоящий MainWindow с DesignerSurface. Raw mouse events передаются в `ITopLevelImpl.Input` настоящему Avalonia MouseDevice; Avalonia выполняет hit-test, маршрутизацию и capture. Проверяется попадание в реальные wrapper/resize grip. Перед hit-test завершается отложенная перерисовка и commit композиции. Это не вызовы приватных pointer handlers и не тесты только ViewModel. Toolbox DragOver/Drop/DragLeave передаются routed events через настоящий Canvas; системный цикл `DoDragDrop` этой автоматизацией не воспроизводится.

На исходном коде стартовый набор дал **2/11**; после исправления расширенный набор дал **26/26**:

1. Клик по выбранному компоненту.
2. Клик по невыбранному компоненту и обновление selection.
3. Уже обработанный Ctrl-клик.
4. Клик по заблокированному компоненту.
5. Правая кнопка.
6. Пустой Canvas, движение ниже порога и намеренный Marquee.
7. Потеря capture.
8. Release вне Canvas.
9. Escape.
10. Move после отпускания левой кнопки.
11. Смена формы во время Marquee.
12. Move компонента: геометрия, отсутствие Marquee, session selection.
13. Resize компонента: реальные grip и размеры.
14. Resize формы: правильный владелец capture.
15. Toolbox Drop после Marquee и последующий клик по новому компоненту.
16. Выбор двух компонентов Marquee и Ctrl-кликом.
17. Ctrl Marquee toggle.
18. Shift Marquee additive.
19. Повторный клик.
20. Потеря capture во время Move без освобождения чужого capture.
21. Escape во время resize.
22. Потеря capture во время resize.
23. Смена формы во время Move без утечки undo batch/Inspector/capture.
24. Toolbox DragLeave и последующий клик.
25. Клик по пустому Canvas очищает selection.
26. Структурированная диагностика без потока записей на каждый Move.

Логи: `artifacts/designer-pointer/baseline.log`, `checks.log`, `build.log`.

Полная `FormDesigner.sln`: **0 ошибок**, одно прежнее предупреждение VSIX `NU1603` о версии Microsoft.VisualStudio.SDK.Analyzers. Сборка smoke runner: **0 ошибок / 0 предупреждений**. Упаковка VSIX в песочнице завершилась `IsolatedStorageException`; повторная разрешённая сборка вне песочницы создала `.vsix`. Изменений исходников VSIX для этого не требовалось. Логи: `solution-build.log`, `solution-build-sandbox.log`.

Существующий набор DesignerSurface / selection / Inspector / resize / multi-form / AXAML pointer selection: **22/22**. Для каждого сценария успешно выполнены проверки и сборка сгенерированного Avalonia-проекта. Проверены session selection, избранные свойства, изоляция Undo/Redo, переключение сессий, drag после добавления формы, Toolbox Drop в нескольких формах и Inspector/Logic после добавления пустой формы. Лог: `artifacts/designer-pointer/regression.log`; список сценариев: `artifacts/designer-pointer/regression-scenarios.txt`.

## Изменённые файлы

- `Models/DesignerInteraction.cs` — режимы и запись диагностики.
- `Views/MainWindow.DesignerInteraction.cs` — координатор операции, capture, отмена и диагностика.
- `Views/MainWindow.axaml.cs` — проверки и подключение координатора к существующим handlers.
- `ViewModels/MainWindowViewModel.cs` — сброс двух полей undo batch при смене формы.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs` — отдельный запуск pointer checks.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.DesignerPointer.cs` — 26 regression checks и native input fixture.
- `Docs/DesignerMarqueeRegression.md` — этот отчёт.

## Ручная проверка

Остаётся проверить аппаратной мышью системный Toolbox drag (добавление, отмена Escape, отпускание вне окна), выход указателя за окно при захвате, Alt+Tab/деактивацию, масштаб Canvas и разные DPI/мониторы. Повторить клик, Move, resize, Ctrl/Shift selection и смену формы на пользовательском проекте. Native input checks подтверждают обработку событий внутри Avalonia; они не заменяют проверку доставки событий оконной системой и аппаратного ввода.
