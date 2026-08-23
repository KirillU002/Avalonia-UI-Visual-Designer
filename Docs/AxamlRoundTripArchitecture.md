# Avalonia UI Visual Designer — архитектура AXAML Import / Round-trip

## Назначение

`AXAML Import — Experimental` добавляет безопасный обратный поток для существующего Avalonia-кода:

```text
existing .axaml
    -> AxamlSyntaxDocument
    -> AxamlImportService
    -> DesignerDocumentFileModel / DesignerDocumentSession
    -> DesignerSurface
    -> AxamlPatchWriter
    -> minimal AxamlTextEdit[]
```

Это **не** замена текущего Export Pipeline. Export по-прежнему создаёт generated project из JSON-модели; round-trip изменяет только открытый исходный `.axaml` и только через точечный patch.

## Принцип сохранности исходника

Исходный текст AXAML является источником истины. `AxamlImportService` создаёт только designer projection, а вместе с ней — in-memory `AxamlSourceMap`. Для каждого импортированного control карта хранит `AxamlElementSyntax`, attribute spans, исходные значения поддерживаемых свойств и набор свойств, которые безопасно редактировать.

`AxamlPatchWriter` никогда не вызывает `GenerateXaml()` и не пересериализует дерево XML. Он возвращает host-neutral результат:

```csharp
public sealed record AxamlTextEdit(int Start, int Length, string NewText);
public sealed class AxamlPatchResult
{
    public bool CanApply { get; }
    public bool ExternalChangeDetected { get; }
    public IReadOnlyList<AxamlTextEdit> Edits { get; }
    public string PatchedText { get; }
}
```

Standalone применяет `PatchedText` через `IDesignerFileSystem`. Будущий Visual Studio Host будет применять те же `AxamlTextEdit` к document buffer, а не обязан напрямую записывать файл на диск.

## Выбор parser

`XDocument` не используется как source of truth: он не сохраняет исходные offsets, комментарии и форматирование, нужные для минимальных изменений. Phase 1 использует собственный lightweight XML tokenizer `AxamlSyntaxDocument`:

| Возможность | Причина |
|---|---|
| `AxamlElementSyntax` | Сохраняет span opening tag, closing tag и целого элемента. |
| `AxamlAttributeSyntax` | Сохраняет отдельный `ValueSpan`, поэтому `Width="160" -> "200"` меняет только `160`. |
| comments / unknown nodes | Не становятся моделью, но остаются в исходном тексте и поэтому сохраняются. |
| semantic projection | Выполняется отдельно в `AxamlImportService`; tokenizer не пытается быть полным Avalonia parser. |

Полный XML validation и Runtime AXAML loading не являются задачей этого tokenizer. Ошибочный синтаксис переводит файл в `UnsafeToSave`: Designer не выполняет destructive save.

## Поддерживаемый subset: Phase 1

| Уровень | Поддержка |
|---|---|
| Root | `Window`, `UserControl` |
| Container | один прямой `Canvas` root child |
| Controls | `Button`, `TextBox`, `TextBlock`, `Border`, `CheckBox` без вложенного visual subtree и без raw inner content; Phase 1 читает attribute syntax. |
| Common properties | `x:Name`/`Name`, `Width`, `Height`, `Opacity`, `IsVisible`, `Margin`, `HorizontalAlignment`, `VerticalAlignment`, `Canvas.Left`, `Canvas.Top` |
| Layers | `Canvas.ZIndex` проецируется в существующий порядок controls и сохраняется в исходнике. Отдельного editable Z-order field в Phase 1 нет: его добавление должно опираться на общий layer editor, а не на round-trip metadata. |
| Appearance | `Background`, `Foreground`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding`, `FontSize`, `FontWeight` |
| Button / CheckBox | `Content` |
| TextBox | `Text`, `Watermark` |
| TextBlock | `Text` |

Поддерживаемые controls становятся существующими `DesignControlModel`/`DesignerControlFileModel`, поэтому отображаются тем же `DesignerSurface`, редактируются тем же Property Inspector и используют тот же `DesignerDocumentSession`.

## Unsupported syntax policy

Неизвестная разметка не является ошибкой сама по себе.

| Синтаксис | Действие Phase 1 |
|---|---|
| Unknown attribute | Сохраняется без изменений; добавляется `AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED`. |
| Unknown direct child of `Canvas` | Не редактируется, сохраняется как opaque source; документ становится `PartiallyEditable`. |
| Ручной inner content, например `<Button>Text</Button>` | Сохраняется как opaque subtree; не заменяется attribute-версией control. |
| `Style`, `ResourceDictionary`, `Grid`, `StackPanel`, bindings, templates, `DataGrid`, Eremex | Не импортируются в visual projection Phase 1, но не удаляются и не регенерируются. |
| Markup extension в поддерживаемом свойстве, например `{Binding Name}` | Сохраняется как read-only для этого свойства в Phase 1. |
| Unsupported root / отсутствие `Canvas` | `ReadOnly`; normal save блокируется. |
| Invalid XML-like syntax | `UnsafeToSave`; patch не создаётся. |

Следствие: Designer не должен молча уничтожать AXAML, который не понимает. При неопределённости действует правило `preserve -> warn -> read-only`, а не full regeneration.

## Capability report

Importer возвращает `AxamlCapabilityReport` с уровнями:

```text
FullyEditable
PartiallyEditable
ReadOnly
UnsafeToSave
```

Пример частично редактируемого файла:

```text
Window:                       Supported
Canvas:                       Supported
Button1:                      Supported
TextBox1:                     Supported
custom:HandWrittenControl:    Unsupported but preserved
```

`PartiallyEditable` можно сохранить, пока операция затрагивает только imported supported controls. `ReadOnly` и `UnsafeToSave` не дают `AxamlPatchWriter` применить изменения.

## Patch model

| Операция | Изменение в исходнике |
|---|---|
| Изменить `Width`, `Content`, `Canvas.Left` | Замена только `Attribute.ValueSpan`. |
| Добавить ранее отсутствующее поддерживаемое свойство | Вставка одного attribute перед `/>` или `>`. |
| Добавить control | Генерация только нового control fragment и вставка перед `</Canvas>`. Соседние элементы не сериализуются заново. |
| Удалить imported control | Удаляется только `ElementSpan`; соседний комментарий намеренно остаётся. |
| Переименовать | Меняется только `x:Name`/`Name`. Code-behind, bindings, event handlers и ссылки Phase 1 не переименовываются автоматически. |

Новые fragments используют newline style и indent unit текущего документа. Существующие строки и unknown syntax не форматируются повторно.

## Conflict detection

`AxamlRoundTripDocument` фиксирует исходный SHA-256 checksum. Перед standalone save `MainWindow` перечитывает файл через `IDesignerFileSystem` и передаёт текст в `CreateActiveAxamlPatch`.

```text
checksum совпадает     -> patch можно применить
checksum отличается    -> AXAML_EXTERNAL_CHANGE_DETECTED, save отменяется
```

На Phase 1 нет автоматического merge или встроенного Compare UI. Standalone отменяет save, показывает понятное предупреждение, а пользователь может повторно открыть файл и сравнить изменения вместо молчаливого перезаписывания внешнего редактирования. Будущий host сможет предоставить собственные Reload / Compare / Cancel действия поверх того же `AxamlPatchResult`.

## Standalone workflow

В меню `More` добавлена команда `Открыть AXAML (Experimental)`.

```text
Open AXAML
    -> AxamlImportService.Import
    -> MainWindowViewModel.LoadAxamlImportedDocument
    -> DesignerSurface

Save
    -> MainWindowViewModel.CreateActiveAxamlPatch
    -> conflict check
    -> IDesignerFileSystem.WriteAllTextAtomicallyAsync
    -> MainWindowViewModel.MarkAxamlRoundTripSaved
```

После успешного save source map пересоздаётся из patched text с сохранением `DesignControlModel.Id` по имени control. Повторное сохранение без пользовательских изменений возвращает zero edits.

`DesignerDocumentSession` остаётся владельцем selection/history/document state. `AxamlRoundTripDocument` не сериализуется в `.formdesigner.json`, не принадлежит Canvas и не попадает в export.

## Diagnostics

Новый subsystem формирует структурированные diagnostics:

```text
AXAML_IMPORT_START
AXAML_IMPORT_ROOT_RESOLVED
AXAML_IMPORT_CONTROL
AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED
AXAML_IMPORT_UNKNOWN_NODE_PRESERVED
AXAML_CAPABILITY_REPORT
AXAML_PATCH_CREATED
AXAML_PATCH_APPLIED
AXAML_EXTERNAL_CHANGE_DETECTED
```

Содержимое AXAML полностью в logs не записывается: diagnostics содержат path, type, element, attribute, capability и число edits.

## Regression coverage

Fixture: [Samples/RoundTrip/SimpleCanvas/MainWindow.axaml](../Samples/RoundTrip/SimpleCanvas/MainWindow.axaml).

Smoke suite `FormDesigner.ExportSmokeTests` покрывает:

| Test | Проверка |
|---|---|
| `AxamlImportCanvasButtonTextBox` | Проекция `Canvas`, `Button`, `TextBox` в `DesignerSurface`; повторный save не создаёт лишний patch. |
| `AxamlRoundTripUpdatesButtonProperties` | `Content`, `Width`, `Canvas.Left`, `Canvas.Top` изменяются минимальными edits. |
| `AxamlRoundTripPreservesComment` | Пользовательский comment сохраняется. |
| `AxamlRoundTripPreservesUnknownAttribute` | `Custom.Unknown` остаётся в исходнике. |
| `AxamlRoundTripPreservesUnknownControl` | Unknown control остаётся и включает partial mode. |
| `AxamlRoundTripCanInsertNewButtonIntoCanvas` | Новый fragment вставляется внутрь `Canvas`. |
| `AxamlRoundTripDeletesOnlyOwnedElement` | Удаляется только imported element, не соседний comment. |
| `AxamlRoundTripDoesNotReformatWholeDocument` | Одно свойство создаёт одну value-only edit. |
| `AxamlRoundTripDetectsExternalChange` | Save блокируется при несовпадении checksum. |

## Границы и roadmap

Phase 1 намеренно не поддерживает full AXAML editing. Дальнейшее расширение идёт только после сохранения этих invariants:

1. Phase 2: `Grid`, `StackPanel`, nested `Border`, `Image`, `CheckBox` расширенного вида.
2. Phase 3: `DataContext`, bindings, `x:DataType`, compiled/non-compiled bindings.
3. Phase 4: standard `DataGrid`, columns, `ItemsSource`.
4. Phase 5: descriptor-driven plugin import для Eremex `TextEditor`, `DataGridControl` и custom controls.
5. Phase 6: `Styles`, `Resources`, `ResourceDictionary`, templates и advanced markup.

Eremex не имеет hardcode в Phase 1. Будущий importer должен спрашивать plugin descriptor о namespace, supported attributes и source-preserving adapter, так же как текущий Designer использует registry для preview/export.

## Связь с будущим Visual Studio Host

`AxamlPatchResult` отделяет semantic edit от physical save:

```text
DesignerSurface / DesignerDocumentSession
                |
                v
       AxamlPatchWriter -> AxamlTextEdit[]
                |
      +---------+---------+
      |                   |
Standalone file system   Visual Studio text buffer
```

Поэтому будущий `AvaloniaDesigner.VsHost.exe` и VSIX bridge смогут использовать тот же importer, capability report и patch writer без второй реализации AXAML serializer и без нарушения document ownership Visual Studio.
