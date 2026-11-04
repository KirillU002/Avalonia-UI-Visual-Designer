# Beta 0.1 Release Candidate — финальная проверка и упаковка

> Исторический отчёт подготовки RC до финального release commit. Указанные ниже dirty tree, RC hashes и отсутствие tag относятся к этому этапу. Официальная Beta описана в [Release Notes](Releases/Beta-0.1.md); её пакеты пересобираются из финального commit отдельно.

Дата: 04.11.2026. Репозиторий: [Avalonia UI Visual Designer](https://github.com/KirillU002/Avalonia-UI-Visual-Designer).

## 1. Исходное состояние и версии

Ветка `main`, HEAD `a28f238bfc95fd0df6ebe49caa3d289c5ba5524c`. Удалённый main совпал с HEAD при `git ls-remote`. Tag `v0.1.0-beta.1` отсутствует и локально, и в удалённых refs. Существующие `v0.3.0-alpha`, `v0.4.0-alpha.1` и `alpha-3.0-pre-host-refactor` не изменялись.

Рабочее дерево **не было чистым**: в нём оставались незакоммиченные исправления/тесты предыдущего Beta Readiness Audit. Они сохранены и входят в RC. Baseline status и binary diff сохранены в evidence; другие пользовательские изменения не удалялись. RC описывает **HEAD + проверенное рабочее дерево**, а не якобы чистый release commit. Перед созданием tag нужно зафиксировать согласованный состав и пересобрать/сверить артефакты.

| Компонент | До | RC |
|---|---|---|
| Продукт | UI Alpha 4.0; assembly version не задана явно | **Beta 0.1 RC**, Version `0.1.0-beta.1` |
| Standalone / VsHost assembly и file version | SDK default | `0.1.0.0` |
| InformationalVersion | SDK default | `0.1.0-beta.1` + SDK source revision suffix |
| VSIX | `0.1.17` | **0.1.18**, прежний extension Identity Id |
| Планируемый Git tag | — | `v0.1.0-beta.1`, **не создан** |

Новая Beta-нумерация продукта отделена от Alpha tags и версии расширения. JSON format, TFM, NuGet versions, глобальная тема, Layout/Preview default не менялись.

## 2. Изменения подготовки RC

- Явные version metadata Standalone/VsHost и VSIX, Beta title в AXAML и runtime UpdateWindowTitle. Первый apphost probe обнаружил, что runtime title затирал заголовок AXAML; исправлена только подпись.
- VSIX получает отдельный `VsHostPackageDirectory`: обычная developer-сборка использует bin output, RC — self-contained publish.
- Воспроизведён packaging blocker: evaluated Content DLLs self-contained .NET 8 попадали в CandidateAssemblyFiles net472 bridge и вызывали CS0731/CS1069 и MSB3277. Payload теперь добавляется **после ResolveReferences**, напрямую как VSIXSourceItem перед GetVsixSourceItems; fresh host build выполнен до упаковки.
- Отдельная проверка обнаружила flattening culture directories. Используется отдельный `_VsHostPayload` с RecursiveDir и VSIXSubPath; все publish files должны побайтно совпадать с извлечённым VsHost. Не отключались проверки dependencies и не добавлялись Avalonia/Eremex references в bridge.
- Release symbols/log/tmp не пакуются. Eremex/runtime plugin copies исключены только из собственного стандартного staging, поддержка плагина в source сохранена.
- Новый package verifier проверяет PE architecture/version, self-contained runtimeconfig, native/runtime/resource closure, отсутствие private/vendor/debug files, bridge AssemblyReferences и exact published host bytes.
- Узкое исключение в `.gitignore` сохраняет `Docs/Releases/Beta-0.1.md`: общий шаблон Visual Studio `[Rr]eleases/` иначе скрывал новый запрошенный документ от Git. Другие release/build каталоги остаются ignored.

Первый packaging build с self-contained payload упал; первый package verifier дал **8/11**, затем **10/11** на промежуточном варианте путей. Оба результата сохранены. После исправления упаковки **11/11**. Два resource-closure failures первого verifier включали ошибку самого harness: не учитывалось перемещение satellite asset из NuGet lib path в locale folder. Проверка расположения исправлена по locale metadata, а необходимость и содержимое каждого файла сохранены.

## 3. Standalone

Подготовлен Windows x64 **self-contained**, без trimming и single-file: runtime AXAML/reflection используют обычные assemblies. Реальный `FormDesigner.exe` из publish запущен в native Windows-сессии с пустыми DOTNET_ROOT/DOTNET_ROOT_X64 и PATH без SDK/VS. Native Beta window создан, coreclr.dll загружен из publish, Eremex/Microsoft.VisualStudio modules не загружены.

Probe не редактировал проекты; реальные настройки/recovery пользователя сохранялись в памяти и восстанавливались после завершения процесса. **Это не объявляется first-clean-profile проверкой самого apphost.** Cold/no-settings/startup-error workflows отдельно проверяются native MainWindow tests с изолированными host paths. Для окончательной ручной проверки нового Windows-профиля оставлен checklist.

Standalone не требует Visual Studio, SDK или Eremex **для запуска**. SDK по-прежнему нужен Build Validation и generated project restore/build. Default generated projects остаются net6.0, для их запуска нужен соответствующий runtime; NETSDK1138 не скрывается.

Linux/macOS/Windows ARM64 runtime не проверялись и не поставляются. Кроссплатформенность Avalonia не используется как доказательство поддержки.

## 4. VSIX / VsHost

RC extension `0.1.18` сохраняет прежний Id для обновления `0.1.17`. В `VsHost/` упакован полный self-contained Windows x64 runtime: executable, deps/runtimeconfig, CoreCLR/hostpolicy/hostfxr, Avalonia/DataGrid/SQL/Npgsql/PluginContracts/Host.Protocol и native dependencies.

RC package PE verification: bridge не ссылается на Avalonia.*, Eremex, FormDesigner или System.Private.CoreLib. Modern runtime размещён только в VsHost, не в bridge root. Это static/process boundary proof; **проверка реально установленного VSIX и модулей живого devenv.exe не выполнялась**.

Реальные process/Named Pipes regression запускаются с executable **из распакованного RC VSIX**, а не только developer bin. Проверяются handshake/OpenDocument/ApplyPatch/ACK/checksum/version conflicts/disconnect/reload. Installed VS command/IDE workflow остаётся manual acceptance; упаковка не считается установкой расширения.

## 5. Финальная matrix

Baseline Release solution: 0 errors, прежнее NU1603 SDK.Analyzers. Final Debug/Release solution также собраны; NU1603 выводится дважды при повторном build внешнего host. Runner: 0 errors/0 warnings. Packaging failures перечислены выше отдельно, а не скрыты успешным последующим build.

| Проверка | Первый RC прогон |
|---|---|
| Full broad, actual packaged VsHost | **71/72**, SplitButton Logic/Legacy (см. ниже) |
| Дополнительный standalone startup/no-settings/error/recovery set | **8/8** |
| Exact RC package verification после исправления packaging | **11/11** |
| Beta safety regression | **16/16** |
| Четырёхформенный native Save/Open/Undo/Redo/Preview/Export workflow | PASS |
| Native Grid Phase 2 | 28/28 |
| Grid Editor | 45/45 |
| Grid Drag & Drop | 40/40 |
| Nested Layout | 59/59 |
| Pointer/Marquee/capture | 26/26 |
| Layout validation/Zoom/properties | 55/55 |
| Phase 7A.2 / Phase 7A | 13/13; 15/15 |
| Undo history cache | 25/25 |
| Grid-first / Canvas compatibility | 26/26 |
| Preview Phase 2 | 28/28 |
| Preview Consistency | 32/32 |
| PostgreSQL offline | 20/20, live не проводился |
| Stabilization | **24/25**, ordinary Window/UIA negative control; Legacy/AXAML Preview GC checks PASS |
| Optional plugin cold/no-vendor startup, missing runtime, missing theme | Три отдельных процесса PASS |
| SQL DataGrid contract + live LocalDB | **47/47** |
| SQL Settings/schema/BindingSource workflow | PASS |
| Standalone exported SQL runtime с Multi Form/Grid resize | PASS |
| Preview Memory, включая live SQL и закрытие во время загрузки | **36/36**, по 20 циклов Legacy и AXAML |
| Generated folder runtime / navigation / resize | PASS: 4 формы, 6 переходов, независимые DataContext |
| RegistryNative / NestedNative / RegistryCanvas runtime | PASS во всех трёх отдельных приложениях |
| Geometry comparison с AXAML Preview как эталоном | **27 групп, 492 наблюдения, 0 mismatches** |

First broad failure сохранён в `broad-first.log`. Helper `RequireStandardPreview` после Window.Show повторно вызывал private preload/render/load, пока настоящий async Opened pipeline мог ещё завершать инициализацию. Такой второй render может сбросить Logic state после клика. Helper исправлен: ожидает завершение реального startup activity/loading overlay и проверяет те же native primary/secondary/Flyout/Logic assertions. Production Preview не менялся. Три отдельных targeted repeat — **3/3 PASS** (`standard-items-repeat-results.json`); первый 71/72 не переименовывается в 72/72.

Полный повтор с corrected helper и actual final packaged VsHost: **72/72** (`broad-repeat.log`). Проверка final VSIX, уже с third-party notices: **11/11** (`rc-package-checks-distribution.log`). Отдельный свежий restore/build/run **из ZIP Export** также PASS: 4 формы Grid/Grid/Canvas/Grid, 6 переходов, независимые DataContext, реальные fixture rows, resize и неизменные Canvas coordinates (`generated-zip/generated-runtime.txt`). Первый broad 71/72 и Stabilization 24/25 остаются результатами своих прогонов.

Все последующие повторные результаты сохраняются в отдельных логах; повторы одного сценария не складываются с независимыми tests для увеличения pass count.

## 6. Данные, Preview и Layout

Проверяются Grid-first и explicit Canvas, historical JSON fallback, Grid definitions, nested Grid/StackPanel/DockPanel/WrapPanel/Border, routed Drop/Reparent, resize, selection/Marquee/capture, Undo/Redo, Inspector/Favorites, Multi Form и source-preserving AXAML.

Четырёхформенный native workflow сравнивает полные сохранённые DTO: ControlId, ParentId/order, placement, definitions/properties, bindings/Logic/DataGrid. Save → New/dispose прежней workspace → Open; оба Preview, folder/ZIP export и standalone generated runtime. New здесь заменяет/закрывает прежнюю рабочую сессию, а не имитирует недоступный отдельный UI wizard.

AXAML Preview остаётся default только для JSON. Явный Legacy selection сохраняется. Imported AXAML workflow не получает новый default и не регенерируется полностью. Registry-derived standard control completeness и Bounds сравниваются в одинаковом client viewport, не только по AXAML names.

SQL Server проверяется только в новой изолированной LocalDB; production/user DB не используются. PostgreSQL — offline/experimental до live acceptance настоящего test server. Eremex optional/experimental: metadata/loading/failure isolation и существующие vertical slices проверяются отдельно, vendor binaries не поставляются в стандартном RC.

Visual consistency: native controls берутся из DesignerRegistry, проверяются в visual tree, не только в JSON/AXAML. Три сцены измерены при client viewport 800×600, 1200×800, 1600×900. Designer/Legacy/exported Runtime сравниваются с AXAML Preview: 492 наблюдения, максимальное измеренное расхождение **0.000 DIP**, допуск 1.1 DIP. Это bounds/layout proof контрольных сцен; оно не утверждает pixel-perfect одинаковость шрифтов/OS chrome/любой пользовательской темы. Designer DataGrid placeholder не считается проверкой SQL cell values: для этого отдельно выполнен live suite и SQL runtime.

После завершения live suites собственные fixture database/instance удалены. Проверены physical paths обоих файлов внутри owned evidence до DROP; MDF/LDF не осталось. Списки остальных LocalDB instances до/после совпали (`localdb/cleanup.json`). Production/user databases не изменялись.

## 7. Memory / Performance и известные проблемы

Preview memory: по 20 Open/Close для Legacy и AXAML, Window/DataContext/ItemsSource WeakReference и animation/UIA lifecycle. Отдельно сохраняется известный P2 ordinary Avalonia Window retention через COM UI Automation provider; успешный Preview memory test не отменяет этот negative control.

Новый RC benchmark: Release, .NET 8.0.32, без debugger, реальный Windows/layout/compositor, document viewport 1200×800 DIP, Zoom/RenderScaling 1.0. Для каждого N — четыре warm-up (включая FirstDrop), затем 30 steady samples; всего **120 steady +16 warm-up**. Замеры выполнены последовательно после restore/build и остальных UI suites, без параллельных тяжёлых сборок. Время — до первого compositor commit после Drop; Ready измерено отдельно, allocations — UI thread, GC — сумма по 30 steady операциям.

| Controls | Median / P95, ms | Min / Max, ms | Median Ready, ms | Median UI allocations, MiB | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|
| 50 | 127.20 / 169.91 | 97.48 / 183.03 | 133.89 | 23.86 | 59/59/18 |
| 100 | 123.12 / 148.96 | 109.00 / 166.31 | 133.31 | 29.83 | 50/34/2 |
| 300 | 170.09 / 198.32 | 146.14 / 201.83 | 175.17 | 54.77 | 81/56/5 |
| 500 | 255.74 / 310.15 | 211.84 / 324.03 | 264.42 | 79.85 | 118/95/16 |

Raw JSON сохраняет cold/warm observations отдельно от median/P95. Например, 500 FirstDrop — 614.21 ms, он **не включён** в steady statistics. Исторические Grid Drop median/P95 из [Beta Readiness](BetaReadinessAudit.md): 139.8/189.3, 140.9/183.3, 199.6/272.5, 293.8/763.1 ms. Новая выборка не доказывает причинного ускорения: performance-код не менялся, не проводился controlled A/B. На больших формах latency/allocations остаются P2; timing thresholds в CI не вводились и крупная оптимизация ради RC не выполнялась.

Stabilization negative control в этом RC: **6/6 обычных Window удержаны**, suite **24/25**. Ранее снятый GC root: ref-counted COM handle → Avalonia.Win32.Automation.RootAutomationNode → WindowAutomationPeer → Window. Он воспроизводился без Designer/DataGrid/Preview; предыдущий forensic dump удалён, text-root evidence сохранён. Исправление Win32/UIA глобально ради RC не предпринимается. Preview-specific memory **36/36** не означает устранения этого framework/backend ограничения.

## 8. Release artifacts

Поставляются только проверенные Windows x64 RC: Standalone ZIP и VSIX. Точный состав, размеры и SHA-256 фиксируются при окончательной упаковке. Ни user settings/recovery, ни transient evidence/logs/dumps, ни developer absolute paths в поставку не входят. Vendor-provided third-party notices и package metadata собираются из текущих NuGet packages; версии зависимостей не обновляются.

Исходники упаковки: `tools/Prepare-Beta01RC.ps1`, `Collect-ReleaseNotices.ps1`. Script требует свежий OutputRoot внутри artifacts и не удаляет существующий RC. Он не создаёт tag/release и не заменяет последующую validation/acceptance. Distribution files находятся в `artifacts/beta01-rc/dist`.

| Артефакт | Bytes | SHA-256 |
|---|---:|---|
| `Avalonia-UI-Visual-Designer-Beta-0.1-RC-win-x64.zip` | 50 982 476 | `9274c5efcf5da92acbcbc01f095c2f8f12a1215fa5b5bced5b9ca069a706c996` |
| `Avalonia-UI-Visual-Designer-Beta-0.1-RC.vsix` | 49 655 384 | `893775b80cb8cf1c66a95f2898f6798af4687a22d8544f76424b9fc55e5cad8d` |

Рядом находятся `SHA256SUMS.txt`, `SOURCE-MANIFEST.json` и `RC-MANIFEST.json`: версия, base HEAD, признак dirty working tree, relative-path source hashes, runtime/RID и artifact hashes. Git release commit ещё не существует. Source ledger включает сохранённые audit fixes и новые RC files, но не ignored artifacts, user profile или содержимое настроек. Поставляемые packages **не подписаны**; checksum подтверждает конкретные bytes, не доверенного издателя.

Publish outputs сохранены отдельно в `staging/Standalone` и `staging/VsHost`. Стандартные пакеты не содержат Plugins/Eremex. Self-contained runtime — **.NET 8.0.32**, Avalonia **11.1.1**, win-x64; NuGet/TFM не обновлялись. `ThirdPartyNotices` содержит metadata **56 packages**, включая runtime pack, и файлы лицензий/notices, предоставленные соответствующими vendors.

Сам `Prepare-Beta01RC.ps1` выполнен end-to-end в новом reproduction directory: publish обоих приложений → notices → build VSIX → ZIP → checksums. **530 runtime DLL/EXE/deps/runtimeconfig файлов** совпали с основным RC, mismatches 0. Это проверка runtime bytes; ZIP timestamps/metadata и порядок полей notices не объявляются bit-for-bit reproducible archive format.

**Итоговый ZIP**, а не только staging, распакован заново. Все **297 файлов** побайтно совпали с publish; forbidden/private files 0. Из этой распаковки выполнен настоящий `FormDesigner.exe`: native Beta window, CoreCLR из пакета, нет SDK на PATH и нет Eremex/VisualStudio modules. Оригинальные настройки/recovery восстановлены probe в finally; fresh OS/profile acceptance остаётся ручной.

После завершения процессов удалены только проверенные owned temporary outputs: **21 каталог, 0 failures, 1137.19 MiB**; evidence осталось **6.77 MiB**. Удалены generated bin/obj, plugin fault fixtures, повторные распаковки и reproduction copy. Основные staging/dist, generated source/ZIP, JSON/CSV, результаты и логи сохранены. Автоматические cleanup warnings о locked ToolboxStandardItemsExport.dll внутри ещё работающего runner не скрыты: окончательная cleanup после process exit успешна. Для повторного package verifier нужно снова извлечь dist VSIX в `evidence/extracted-vsix`.

Воспроизведение упаковки, без создания tag/release (нужен **свежий** каталог):

```powershell
pwsh -NoProfile -File tools/Prepare-Beta01RC.ps1 -OutputRoot artifacts/beta01-rc-new
```

Release build log scripts не включаются в бинарные distribution packages. Runtime probes временно добавляют test entry hooks только в generated apps, восстанавливают source и пересобирают без hooks в finally. Они не являются частью Standalone/VSIX.

## 9. README / Release Notes / links

README сохраняет прежнюю структуру, badges и AXAML coverage/архитектуру, обновляет Beta status, Grid-first, Preview default, optional/experimental boundaries и установку. Существующие разделы Alpha 3.0 и Alpha 2.0 сохранены; Alpha 4.0 оставлена отдельной существующей ссылкой. Для нового tag/release не создаётся несуществующий download link.

[Release Notes Beta 0.1](Releases/Beta-0.1.md) написаны на русском. GitHub repo, Alpha 4/3 tree links, Alpha 2 commit и Beta badge проверены HTTP 200; authoritative tag conflict check выполнен git ls-remote. Документация RC не содержит локальных developer paths.

## 10. Изменённые файлы

Текущий RC:

- Versions/package: `FormDesigner.csproj`, `AvaloniaDesigner.VsHost/AvaloniaDesigner.VsHost.csproj`, `AvaloniaDesigner.VSIX/AvaloniaDesigner.VSIX.csproj`, `source.extension.vsixmanifest`, `AvaloniaDesignerVsixPackage.cs` в каталоге VSIX.
- Beta window label: `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`; остальная audit logic последнего файла сохранена.
- Документация: `README.md`, этот отчёт, `Docs/Releases/Beta-0.1.md`, соответствующее исключение `.gitignore`.
- Packaging tools: `tools/Prepare-Beta01RC.ps1`, `tools/Collect-ReleaseNotices.ps1`.
- Tests: новый `smoke-tests/FormDesigner.ExportSmokeTests/Program.ReleaseCandidate.cs`, dispatch `Program.cs`, только output override в `Program.BetaReadiness.cs`, actual-startup synchronization в `Program.StandardItems.cs`, новый `smoke-tests/ReleaseCandidate/ProbeStandalone.ps1`.

Незакоммиченные production fixes и regression files **предыдущего** аудита сохранены: AutosaveRecoveryService, DocumentPersistence, MainWindowViewModel, PostgreSql partial и recovery guard в MainWindow; полный перечень/причины — [Beta Readiness Audit](BetaReadinessAudit.md). Они входят в source manifest RC. Ни Undo storage/Document Model, ни Layout/Preview/Export architecture не переписывались.

Evidence index: `artifacts/beta01-rc/evidence/matrix.json`, `final-repeat-results.json`, `distribution-verification.json`, `packaging-reproduction.json`, `workflow/workflow-result.json`, `generated-zip/generated-runtime.txt`, `geometry-comparison.log`, performance raw JSON, `localdb/cleanup.json`, `cleanup.json`. Сохраняются baseline и failed/intermediate logs, а не только последняя зелёная попытка.

## 11. Ручная acceptance и решение

Перед публикацией:

1. Зафиксировать согласованный рабочий source tree, пересобрать/сверить source/artifact hashes. Не тегировать родительский HEAD без незакоммиченных исправлений.
2. Чистый Windows x64 профиль/VM без SDK/VS: распаковка ZIP, first launch/no settings, New Grid/explicit Canvas, Settings/Help/Diagnostics, Save/Open/Preview/Export. Проверить platform prerequisites на чистой OS; проверка bundled CoreCLR на текущей машине не равна такой приёмке.
3. Реальный Toolbox system DragDrop, Escape/Alt+Tab/release вне окна, DPI 100/125/150/200%, zoom/scroll/multi-monitor, selection/handles после Undo/Reparent.
4. Установить VSIX в Visual Studio 2022 Community x64 experimental hive; команда Tools, несколько buffers, Apply/ACK/external conflict, закрытие/reopen host; проверить отсутствие визуальных assemblies в devenv.
5. Live PostgreSQL acceptance или явно сохранить feature experimental/unverified. Произвольный AXAML Preview и Eremex не обещают безусловной поддержки.
6. Принять/отдельно исследовать ordinary Window/UIA P2, performance tails больших форм и generated net6.0 support warning.

**Итог: READY WITH CONDITIONS** для предварительной Windows x64 Beta 0.1 RC со стандартными Avalonia controls и описанными ограничениями. Standalone/VSIX подготовлены к передаче на manual acceptance; это не заявление о прохождении установленного VSIX, физической мыши/DPI или live PostgreSQL.

Новых открытых подтверждённых P0/P1 после минимальных packaging fixes не осталось в выполненной matrix. Есть известный P2 ordinary Window/UIA retention, большие allocations/latency на 300–500 controls и перечисленные verification gaps. First broad 71/72, repeated 72/72, Stabilization 24/25 и Preview Memory 36/36 приводятся раздельно; **все тесты зелёными не объявляются**.

До публичного выпуска нужны source commit/rebuild, ручная Windows/VS acceptance и согласие пользователя на публикацию. Git tag, GitHub Release, force push, удаление старых releases, Git commit и изменение Git history **не выполнялись**. Следующий Layout phase не начинался.
