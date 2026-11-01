# Layout Designer Phase 7A — производительность Drop и UX

Дата: 31.10.2026. Baseline commit: `326110d8d52277d6d9cd36326d957072d344a48b`. Рабочее дерево до начала было чистым. Изучены Phase 4–6, StabilizationAudit и PreviewMemoryLeakFix. Существующие пользовательские файлы и Git history не откатывались.

**Canvas остаётся вариантом по умолчанию. Phase 7B не начиналась.** NuGet, TargetFramework, глобальные стили, Document Model, database services и VSIX/IPC не менялись.

Подтверждённые причины/дефекты этого прохода:

| Приоритет | Проблема | Состояние |
| --- | --- | --- |
| P1 | Полное пересоздание Designer visuals/menus при placement Drop | Ускорен безопасный subset; общая latency Toolbox ещё выше цели |
| P1 | TreeView сбрасывал selection во время замены roots и повторно строил Inspector | Исправлено |
| P1 | Right release терялся после detach нажатого wrapper | Исправлено, capture перенесён на стабильный Canvas |
| P1 | Generated ObservableProperty для имени формы с цифрой не компилировался | Исправлено и проверено build/runtime |
| P1 | Generated Button SetProperty ссылался на отсутствующий value helper | Исправлено и проверено build/runtime |
| P2 | Сотни control name badges перекрывали содержимое/друг друга | Одна primary-selection подпись, tooltip для остальных |

Воспроизведённых P0 этого этапа нет. Native memory access violation первого тестового запуска остаётся неатрибутированным риском; он не смешивается с указанными managed/performance root causes.

## 1. Root cause и границы оптимизации

Повторён сценарий Phase 6: Grid → горизонтальный WrapPanel, 50/100/300/500 controls, перенос последнего Button в начало. Размер формы 1200×800 DIP, у buttons фиксированные 75×28. Ghost/target вычисляются до начала замера commit, как в Phase 6. Измеряются и фактические ожидания UI после изменения модели.

Главный источник задержки — полный render Designer: пересоздание wrappers, содержимого и контекстных меню всех controls, затем layout присоединённых visuals. На 500 controls baseline дал 998 preview factory calls, 500 новых wrappers, 500 новых context menus и 27 строк Inspector. ApplyDocument при этой операции **не вызывался**. Undo сериализация и Measure/Arrange значимы, но не были основной причиной исходной задержки.

Подробный повтор на 500 controls: `CreateDesignerWrappers` — 303.8 ms / 128.7 MB, в том числе `CreateContextMenus` — 135.8 ms / 42.9 MB. Native projection build — 7.9 ms, Measure/Arrange с извлечением measurements — 108.9 ms. Общий renderer — 472.0 ms. Первый compositor commit — 1046 ms от начала Drop в этом повторе. Основная before matrix дала 834 ms; разброс не скрывается.

Это inclusive timings: вложенные stages нельзя складывать. `WaitVisualSnapshot` включает работу renderer, а общий frame latency включает Dispatcher/layout/прочие отложенные UI задачи. Это не только время вызова SQL/model/VM method.

## 2. Профилирование

Добавлен opt-in `Services/LayoutDropProfile.cs`. События: `LAYOUT_DROP_PROFILE_START`, `LAYOUT_DROP_PROFILE_STAGE`, `LAYOUT_DROP_PROFILE_END`. Включение для native routed Drop: `FORMDESIGNER_LAYOUT_DROP_PROFILE=1`. На PointerMoved обычного Designer профилирование не запускается. Stages агрегируются, а не пишутся на каждую итерацию.

Stage records содержат время, allocated bytes текущего UI thread, число вызовов, tracked object creation и обновления wrappers. Учитываются созданные factory roots preview controls/wrappers/menus и строки Inspector. **Это не полный счёт всех CLR объектов и template descendants**: EventPipe allocation type census не снимался. Allocated bytes измерены через `GC.GetAllocatedBytesForCurrentThread`, не через working set. Stages и ambient scope освобождаются в Dispose; после завершения не хранят ссылки на Window, DataContext или model.

Инструментированы Resolve/Validate, model update, Undo snapshots/commit, ApplyDocument, selection, Inspector rows/visual refresh, native projection, Measure/Arrange, wrappers/context menus и renderer. Smoke profile дополнительно измеряет layout completion, первый compositor commit и готовность следующего Input-priority callback. Production scope синхронного handler заканчивается при его завершении; его END не выдаётся за физическую презентацию frame на мониторе. `routed-drop-profile.json` отдельно подтверждает реальный PointerReleased путь.

Первый compositor commit — наблюдаемый compositor fence, а не измерение времени сканирования экрана. Искусственный idle после Drop в latency не добавляется; Dispatcher обрабатывается до фактического completion с bounded timeout. UI ожидания не прячутся за новым Dispatcher.Post в production.

Профиль move на 500 controls, detailed before → итоговый after. Время/MB inclusive, не сумма независимых интервалов:

| Stage | Before calls / ms / MB | After calls / ms / MB | After tracked created / updated |
| --- | --- | --- | --- |
| Resolve Drop Target | 1 / 0.62 / 0.03 | 1 / 0.61 / 0.03 | 0 / 0 |
| Validate Drop | 1 / 0.85 / 0.03 | 1 / 0.62 / 0.03 | 0 / 0 |
| Update Document Model | 1 / 19.58 / 0.22 | 1 / 5.30 / 0.13 | 0 / 0 |
| Create Undo Snapshot | 2 / 52.97 / 29.96 | 2 / 71.37 / 29.94 | 0 / 0 |
| ApplyDocument | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 |
| Build Native Projection | 1 / 7.95 / 5.36 | 1 / 6.57 / 5.36 | 499 / 0 |
| Measure/Arrange + extraction | 1 / 108.93 / 24.07 | 1 / 49.56 / 21.89 | 0 / 0 |
| Create wrappers | 500 / 303.81 / 128.71 | 0 / 0 / 0 | 0 / 0 |
| Create context menus (внутри wrappers) | 500 / 135.81 / 42.88 | 0 / 0 / 0 | 0 / 0 |
| Update existing wrappers | 0 / 0 / 0 | 1 / 1.20 / 0.17 | 0 / 500 |
| Rebuild/Update Visual Tree, всего | 1 / 471.97 / 145.41 | 1 / 58.12 / 27.45 | 499 / 500 |
| Refresh Selection | 1 / 0.18 / <0.01 | 1 / 0.10 / <0.01 | 0 / 0 |
| Refresh Property Inspector | 1 / 34.37 / 2.75 | 0 / 0 / 0 | 0 / 0 |
| UI layout fence | 1 / 0.18 / <0.01 | 1 / 0.12 / <0.01 | 0 / 0 |
| Ожидание первого compositor commit после layout | 1 / 63.02 / 3.20 | 1 / 13.92 / 0.03 | 0 / 0 |

В baseline per-stage object counters ещё не были добавлены: известны агрегированные 998 preview roots / 500 wrappers / 500 menus / 27 Inspector rows; несуществующая детализация не восстановлена догадками. В отдельном real-routed regression `PointerReleased` занимает 34.45 ms на маленькой форме; этот handler включает commit/renderer, но не compositor presentation. Это не 500-control benchmark и не основание заменить им end-to-end matrix.

## 3. Исправления Drop

1. Контекстное меню строится при запросе/правом клике. Нераскрытые меню не создают тысячи MenuItem при render. Команды и их актуальная доступность сохраняются; повторное открытие не накапливает items/handlers.
2. Native same-parent placement Drop передаёт существующему DesignerChanged value hint. Он действителен только для той же session, точного fingerprint и того же selection. Любая последующая обычная авторская правка отменяет hint.
3. Avalonia заново измеряет настоящую layout tree. Для plain unbound Button/TextBox/TextBlock внутри контейнеров, при неизменных размерах и иерархии старых элементов, существующие wrappers получают новые Canvas overlay positions и sibling paint order. При Toolbox add создаётся ровно одна новая wrapper, у прежней selection обновляются рамки/grips. Это служебные координаты Designer, не Canvas.Left/Top пользовательского Grid/Stack/Wrap. Идентичность model controls, bindings и Logic сохраняется.
4. Перед reuse проверяются object identity, parent chain, размеры, positive/zero bounds и наличие wrappers. Для add допускается ровно один новый Id; несколько неотрисованных добавлений безопасно переходят на полный render. DataGrid/plugin/items/data-bound previews сохраняют полный refresh: их содержимое может обновляться независимо от placement, например при завершении async row loader. Reparent, изменение старых размеров и прочие неподходящие случаи также используют прежний полный renderer. Ни второй layout engine, ни общая архитектура Surface не создавались.
5. Синхронный RenderDesigner погашает уже запланированный render timer. Один Drop не выполняет ещё один полный render по старому отложенному запросу.
6. Для same-parent move убран принудительный полный RebuildPropertyGrid. Существующие property notifications обновляют placement rows; смена parent/контекста сохраняет полный rebuild. Selection/Inspector не очищаются.
7. Дополнительный Toolbox profile выявил три Inspector rebuild: явный повтор и временную очистку selection через TreeView.SelectedItem=null при StructureTreeItems.Clear. Замена tree roots защищена существующим selection-update флагом с вложенным восстановлением; убран повтор GridDropCreate. В итоговом профиле остаётся один Inspector rebuild (27 rows вместо 63), StructureTree rebuild сократился с 66.7 до 1.3 ms на 500 controls. Проверка не ослаблена: routed test требует ровно один Inspector refresh и одну новую wrapper.

До и после операции остаются прежние Undo boundaries и snapshots. Оптимизация не заменяет полноценную историю частичным patch. ControlId, ParentId, bindings, styling, Logic и DataGrid metadata не меняют identity. Undo/Redo и subsequent authored edit проверены реальным routed input.

## 4. Performance before / after

Основная сопоставимая matrix; ms от начала commit, decimal MB allocated на UI thread до завершения profile:

| Controls | Before model | Before first commit | After model | After visual snapshot | After layout | After first commit | After next input | Before MB | After MB |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 50 | 89.9 | 540.4 | 33.0 | 87.5 | 87.7 | 102.7 | 111.6 | 45.79 | 6.63 |
| 100 | 32.6 | 358.0 | 29.0 | 116.3 | 116.4 | 138.4 | 155.1 | 65.69 | 12.57 |
| 300 | 39.0 | 566.7 | 52.1 | 135.2 | 135.3 | 156.6 | 173.2 | 146.56 | 38.30 |
| 500 | 94.4 | 834.3 | 81.8 | 197.4 | 197.5 | 211.5 | 228.4 | 228.76 | 64.21 |

В последнем проходе на 500 controls first commit ускорен примерно в 4 раза, allocations уменьшены примерно на 72%. После move создаются 499 offscreen leaf controls для native measurement, **0 wrappers и 0 context menus**; обновляются 500 существующих wrappers. Остались два history snapshot calls (~71.4 ms, 29.9 MB) и native measure/extraction (~49.6 ms, 21.9 MB). В предыдущем after-проходе first commit был 166.3 ms; в отчёте не выбрано только более выгодное значение. Простая перестановка на 50–100 controls достигла 100–150 ms, но это не гарантия для всех операций/controls.

Замеры — отдельные проходы одного checkout на Windows, а не статистический benchmark. Первый размер включает cold paths, GC/JIT и фоновые работы вносят разброс. Порог pass/fail искусственно не подгонялся. Исходники и JSON evidence: `drop-before.json`, `drop-before-detailed.json`, `drop-after-reuse.json`, `drop-final-move.json`.

Toolbox сравнение отдельно: before здесь — уже с lazy menus, до исправлений tree/Inspector и incremental add. С исходным HEAD Toolbox не измерялся, эти числа не подменяют основной before сценарий.

| Controls до add | Before first commit ms | After first commit ms | After model ms | After visual ms | After layout ms | After next input ms | Before MB | After MB |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 100 | 638.0 | 409.2 | 128.1 | 393.3 | 393.6 | 421.7 | 56.84 | 36.51 |
| 500 | 663.6 | 350.6 | 103.1 | 313.7 | 313.8 | 363.0 | 175.80 | 89.41 |

Дополнительный финальный изолированный Toolbox проход на всех четырёх размерах (`drop-toolbox-matrix.json`), ms от commit:

| Controls до add | Model | Visual snapshot | Layout | First commit | Next input | MB |
| --- | --- | --- | --- | --- | --- | --- |
| 50 | 110.5 | 337.7 | 337.9 | 357.3 | 359.0 | 30.17 |
| 100 | 110.4 | 352.9 | 353.0 | 369.0 | 379.9 | 36.18 |
| 300 | 51.1 | 185.4 | 185.5 | 216.9 | 221.3 | 61.84 |
| 500 | 65.6 | 226.5 | 226.5 | 248.9 | 253.2 | 89.68 |

Это ещё один проход, а не percentile/median. Предыдущие 409/351 ms не удалены ради более выгодных новых 369/249 ms. Наблюдается значимый разброс, но и этот проход подтверждает оставшуюся задержку Toolbox на небольших формах. Полный final solution/runner build и новый UX runner 15/15 выполнены до него; параллельных export builds во время этой matrix не было.

На 500 controls Toolbox создаёт одну wrapper вместо 501, 501 preview factory roots вместо 1000, один Inspector rebuild вместо трёх. Однако цель 100–150 ms для **Toolbox** не достигнута. В отдельном уточняющем профиле на 100 controls: выбор нового control 95.1 ms (включая Inspector 45.3 ms), renderer 58.2 ms, ожидание visual snapshot 253.0 ms. Из последнего около 194.8 ms находятся вне renderer: это работа/ожидание очереди UI и attached layout/templates; точная CPU-stack атрибуция этой части не установлена. Она не выдана за ускорение и не скрыта новым Dispatcher.Post.

Следующий узкий performance проход должен исследовать retarget/recycling UI Inspector и очередь attached layout со stack sampling. Это не делалось крупным неподтверждённым рефакторингом в Phase 7A. Reparent, data/plugin/items previews и изменение размеров старых controls сохраняют полный render. Общий performance acceptance выполнен **частично**, поэтому переход default пока не готов.

## 5. Бейджи имён и right-click lifecycle

Вместо постоянно видимых подписей всех controls отображается одна подпись primary selection. Имена остальных доступны через hover tooltip, Inspector и structure tree. Подпись compact, с max width/ellipsis, non-hit-testable, находится в designer-only SelectionOverlay и не входит в JSON/AXAML/Preview.

Бейдж расположен над рамкой, либо под ней с зазором от выступающего resize grip, либо сбоку. При отсутствии свободного места/невидимом selected frame он не рисуется поверх control; tooltip и Inspector остаются. Несколько выбранных элементов сохраняют прежние outlines/toolbar, приоритет подписи — у primary selection.

Проверка реального снимка выявила опасность использования TranslatePoint до Arrange нового wrapper: оно ещё возвращало (0,0). Anchor использует существующий native measured snapshot/абсолютную layout-позицию. Бейдж измеряется в присоединённой style/font scope. Нет screenshot-specific координат или повторного DPI multiplier.

Right click regression установил ещё один lifecycle конфликт: selection render удалял нажатый wrapper, capture переходил к Canvas, и release не вызывал Control_PointerReleased. Теперь правый press захватывает стабильный Canvas; release направляется в текущий wrapper по ControlId. CaptureLost, Escape, deactivate, смена формы и удаление target сбрасывают pending state. Открытое меню проверяется настоящим raw right-button input, а не только вызовом factory.

## 6. Zoom / DPI и Drag & Drop

Автоматизированы реальные routed/raw pointer events при 50/75/100/125/150/200%. Проверяются фактический zoom, nested reparent, badge anchor/resize exclusion, selection, scroll reset и compositor scene. Существующие Grid/Nested suites проверяют deep target, clipping, invalid drop, spans, ordering, Escape, release outside и CaptureLost.

В проверенной Windows-сессии RenderScaling=1. Нет заявлений о физическом DPI 125/150/200% или переносе между мониторами. Координаты остаются DIP, существующие transforms учитывают zoom/scroll.

Для desktop проверки подготовлен `--layout7a-desktop`: настоящий MainWindow/Toolbox с Grid/Stack/Dock/Wrap/Canvas nested targets; изолированный test host сохраняет model state/drag diagnostics в artifacts. Computer-use нашёл единственное целевое окно, но дважды не получил screenshot: `window capture timed out: timed out waiting on channel` (повтор с новым window selection и screenshot-only). Timer/model snapshot продолжал обновляться. **Drag input не отправлялся; физический DnD не проверен.** Fixture закрыт через собственный stop-file с штатным test cleanup.

Использован [computer-use SKILL.md](C:/Users/Kirill/.codex/plugins/cache/openai-bundled/computer-use/26.1027.30824/skills/computer-use/SKILL.md). Recovery в [guidance.md](C:/Users/Kirill/.codex/plugins/cache/openai-bundled/computer-use/26.1027.30824/docs/guidance.md) требует: “If state capture or window activation fails, stop using prior coordinates or element indexes.” После повторного timeout inputs остановлены. Это ограничение проверки, не установленный дефект Designer. Доказательство: `desktop-result.txt`, исходное состояние `Controls=10`, `Mode=None`, pointer log пуст.

## 7. Multi Form export / ZIP / Logic

Fixture содержит Canvas home, Grid, nested Stack/Wrap с DropDownButton/SplitButton/ListBox и DataGrid + явный локальный Manual BindingSource. Секретов и внешних БД нет. Все формы используют production Export Pipeline и ZIP; данные разрешены fixture явно, а не подменяют ошибку SQL.

Найдены два реальных generated-build дефекта:

- Заголовок формы `7A Grid` даёт допустимый C# class `_7A_Grid`, но старый camel-case backing field начинался `_7A_...`. Toolkit удалял префикс `_`, ломая generated property/partial callbacks. Для ожидаемого property identifier с `_` используется поддержанный `m_` backing field prefix. Обычные имена сохраняются. [Документация ObservableProperty](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty) описывает conventions; поведение закреплённой 8.2.1 проверяется actual build, версия не обновляется.
- В ветке с ViewModel кнопочные handlers генерировались отдельно, а value helper выводился только по non-button interactions/ShowMessage. Button SetProperty ссылался на отсутствующий ResolveInteractionValue. Теперь существующий AppendGeneratedInteractionHandlers анализирует весь набор, пропуская уже сгенерированные Button handlers. Helper выводится, дублирующий handler не создаётся.

Заголовки `7A ...` и Button Logic сохранены в regression fixture; ошибку не обошли переименованием форм. ProbeMultiFormLayout7 временно инструментирует только generated artifact, запускает compiled project из ZIP на настоящем Avalonia Windows backend, вызывает generated routed Click handlers и возвращает исходные bytes/обычную сборку в finally. Окна создаются offscreen; entrypoint на время probe использует SetupWithoutStarting и bounded UI pump. Это runtime проверка реальных controls/handlers/layout, но не физическая мышь и не полная интерактивная desktop lifetime с обычным entrypoint.

Production export/build: 1/1. ZIP содержит App.axaml и четыре form AXAML. Запущено настоящее Avalonia-приложение из распакованного ZIP: **4 формы, 6 переходов PASS**, Button SetProperty PASS, разные ViewModel instances PASS. На каждой форме DataGrid привязан к явно разрешённым локальным Manual rows; DataGrid основной data-формы содержит 6 строк и растягивается с 1200×672 до 1440×812 DIP после увеличения окна на 240×140. Nested Stack/Wrap, три статических ListBox items и открытие настоящих Flyout обоих menu buttons проверены runtime.

Первоначальный fixture ошибочно ожидал sample BindingSource rows в ListBox: существующий ResolveItemsRuntimeMode намеренно отключает demo для ListBox-only sources. Эта политика не изменена. Fixture теперь проверяет локальные строки через DataGrid и статический ListBox отдельно; data/navigation assertions не сняты. Это не SQL integration. Тестовые заголовки с цифрой, независимость DataContext и кнопочная Logic сохранены.

После probe восстановлены исходные generated bytes, временный C# probe удалён, обычная сборка повторно успешна. Нормальный generated build: 0 errors, 5 warnings (NETSDK1138 для существующего export target net6.0 и четыре CS8618 в row DTO). TargetFramework и nullable codegen вне двух воспроизведённых compile defects не менялись. Логи: `multiform-runtime-export.log`, `multiform-runtime-accepted.log`, `zip-runtime-accepted/navigation.stdout.log`.

## 8. Canvas, Memory и regression

Canvas factory/default/JSON semantics не переключались. Legacy Canvas move/resize/marquee/multi-selection, save/load и preview/export проверяются прежними suites. Native Canvas inside Grid использует прежний DIP placement. Структурный AXAML round-trip остаётся read-only там, где нет безопасного patch; writer не изменён.

Production GC.Collect не добавлялся. Profiler не создаёт постоянные callbacks/статические коллекции окон. Основной cache wrappers не расширяется: reuse работает с уже принадлежащими окну controls. Ghost cache, binding loaders, Preview lifecycle/automation/animation cleanup не менялись. Memory runner проверяет 20 Open/Close каждого режима через WeakReference и возврат clock subscriptions.

Solution build: **0 errors, 1 прежний NU1603** (VS SDK analyzer version resolution). Smoke runner build: **0 errors, 0 warnings**. Оба проекта собраны без изменения зависимостей.

| Целевой runner | Итоговый результат |
| --- | --- |
| Новый Phase 7A UX / routed input | 15/15 |
| Nested Layout Phase 5 | 59/59 |
| Grid Drop Phase 4 | 40/40 |
| Native Grid Phase 2 | 28/28 |
| Grid Editor Phase 3 | 45/45 |
| Pointer/Marquee | 26/26 |
| Layout Phase 6 validation / consistency | 55/55 |
| Stabilization audit | 25/25 |
| DataGrid offline contracts | 18/18 |
| PostgreSQL offline contracts | 20/20 |
| Preview Memory | 28/28 |
| Новый Multi Form export/build | 1/1 + runtime PASS |
| Общая regression selection Phase 6 (AXAML, Inspector, DLL/SQL fallback, Eremex, Multi Form, VSIX/VsHost/IPC) | 72/72 |
| Дополнительные Native Grid / Grid Editor / Nested / Phase 6 exports | 13/13 |

Memory: два изолированных завершённых повторных прогона — 28/28. В каждом Legacy и AXAML выполняют 20 последовательных Open/Close; checkpoints 5/10/15/20 показывают aliveWindows=0 и clockObservers=0. Проверяются WeakReference на окна/DataContext/старые ItemsSource, reopen, multiple grids/forms и close during async contract load. Проверки не ослаблялись.

В самом первом memory запуске был native exit **0xC0000005** после AXAML MultipleGrids, до Reopen. Dump/stack для этого падения нет; последующие два запуска его не повторили. Причина не установлена, связь с изменениями Phase 7A не доказана и не исключена. Это оставшийся риск, а не успешно пройденный первый прогон. Evidence: `preview-memory-native-crash.log`, `preview-memory-repeat.log`, `accepted-preview-memory.log`.

Общий 72-scenario smoke/export/AXAML/IPC прогон завершился с exit 0 и 72/72 PASS. Cleanup в живом runner встретил два загруженных generated DLL и не смог удалить часть bin до выхода процесса; это отдельно от результатов сценариев. Повторная очистка после выхода runner успешна: удалены только принадлежащие Phase 7A generated bin/obj (18 directories, ~679 MB), исходники/ZIP/JSON/логи сохранены. VSIX packaging/metadata и запуск/patch/conflict/disconnect VsHost проверены автоматикой; ручная работа в Visual Studio не заявляется.

Live SQL/PostgreSQL в Phase 7A не запускались: FORMDESIGNER_TEST_SQLSERVER и FORMDESIGNER_TEST_POSTGRES не заданы. Локальные Manual rows не называются live DB integration.

После финальных 13 export builds удалены их 26 bin/obj directories (~1.56 GB), когда runner уже завершился. Вся папка artifacts/layout-phase7a занимает около 2.8 MB и содержит исходники экспортов, ZIP, JSON, снимки и текстовые evidence. Новые большие временные artifacts не оставлены.

## 9. Изменённые файлы

- Новый `Services/LayoutDropProfile.cs` — value-based opt-in timings/counters.
- `ViewModels/MainWindowViewModel.GridDrop.cs`, `.NestedDrop.cs`, `.NativeLayout.cs`, `.cs` — stages, native Drop hint, Inspector reuse, два минимальных codegen fixes.
- `Views/MainWindow.axaml.cs`, `.GridDrop.cs`, `.DesignerInteraction.cs`, новый `.LayoutUx.cs` — guarded placement-only reuse, render coalescing, lazy menus, pending right-click capture, name badge.
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`, новые `.DropPerformance.cs`, `.Layout7.cs` — dispatch, four-size performance, routed/UX tests, desktop fixture, production Multi Form/ZIP export.
- Новый `smoke-tests/LayoutGrid/ProbeMultiFormLayout7.ps1` — actual generated runtime navigation/Logic/BindingSource/resize.
- `Docs/LayoutDesignerPhase7A.md` — этот отчёт.

## 10. Ручной checklist / оставшиеся границы

1. Аппаратная мышь: все Toolbox containers, маленькие/перекрытые targets, длинные движения, удержание/отпускание вне окна, Escape и Alt+Tab во время удержания.
2. DPI 125/150/200%, mixed-DPI мониторы, перенос окна при активном gesture, zoom и реальные scroll offsets.
3. Вложенные Auto-size/plugin containers, тяжёлые DataGrid datasets, resize окна во время drag. Reparent/Toolbox большие формы могут требовать полного render.
4. Бейджи длинных имён, full-surface container, multi-selection, scroll/zoom около границ; правильный tooltip и отсутствие перекрытия resize.
5. Дополнительные окна generated приложения, BackHome, повторное открытие, независимые данные/Logic, закрытие всех окон.
6. Native Windows UIA roots обычного Window вне Preview остаются известным ограничением версии backend; Preview-specific fix не расширялся на весь продукт. Interactive Visual Studio, live PostgreSQL, Linux/macOS и многочасовой soak не заявляются автоматически проверенными.

### Воспроизведение автоматизированных проверок

Из корня repository после build smoke runner (PowerShell):

```powershell
dotnet build .\FormDesigner.sln --no-restore -m:1
dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj --no-restore -m:1
$runner = '.\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll'
dotnet $runner --layout7a-checks
dotnet $runner --layout-drop-performance-checks
$env:LAYOUT_PROFILE_OPERATION = 'toolbox'
$env:LAYOUT_PROFILE_LABEL = 'toolbox-repeat'
dotnet $runner --layout-drop-performance-checks
Remove-Item Env:LAYOUT_PROFILE_OPERATION
Remove-Item Env:LAYOUT_PROFILE_LABEL
dotnet $runner --preview-memory-checks
$env:SMOKE_SCENARIO_FILTER = 'LayoutPhase7AMultiForm'
$env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS = '1'
dotnet $runner .\artifacts\layout-phase7a\manual-export
Remove-Item Env:SMOKE_SCENARIO_FILTER
Remove-Item Env:FORMDESIGNER_KEEP_SMOKE_ARTIFACTS
# Распаковать полученный MultiForm.zip в новую папку, затем:
# .\smoke-tests\LayoutGrid\ProbeMultiFormLayout7.ps1 -ProjectPath <папка>
```

Для повторного baseline нужен исходный commit в отдельном checkout и только instrumentation/harness: текущий optimized code не выдаётся за before. Отдельные новые profiler tests не изменяют thresholds ради pass. Обычный Designer не включает профилирование без env flag.

## 11. Готовность к Grid-first

Простые move и Toolbox add ускорены, устранены лишние allocations/rebuilds и пересечение имён, но Toolbox на 100 controls всё ещё заметно превышает желаемые 150 ms. Физические gestures/DPI не подтверждены, первый native memory crash требует наблюдения и атрибуции при повторении. Объявлять Grid-first готовым на этих данных нельзя. Canvas остаётся вариантом по умолчанию; Phase 7B не запускается.

NOT READY
