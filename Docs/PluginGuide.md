# Plugin SDK Guide

Этот документ описывает минимальный путь для разработчика, который хочет добавить свой control plugin в FormDesigner.

## Что такое plugin control

Plugin control - это отдельная DLL, которая подключается к редактору через `FormDesigner.PluginContracts`.

Плагин регистрирует один или несколько `IControlDescriptor`. Каждый descriptor отвечает за:

- карточку в toolbox
- начальные свойства control
- дополнительные custom properties
- preview внутри дизайнера
- XAML export
- metadata `PluginId` / `PluginVersion` для восстановления документа

## Быстрый старт

1. Создайте class library project на `net7.0`.
2. Добавьте ссылку на `FormDesigner.PluginContracts`.
3. Реализуйте `IFormDesignerPlugin`.
4. Добавьте `[assembly: FormDesignerPlugin(typeof(...))]`.
5. Зарегистрируйте descriptor через `registry.RegisterControl(...)`.
6. В descriptor реализуйте `CreateDefaultDefinition`, `BuildPreview`, `AppendXaml`.
7. Соберите DLL.
8. Положите DLL и зависимости в `bin\<Configuration>\net7.0\Plugins\<PluginName>\`.
9. Перезапустите редактор или нажмите **Plugins -> Reload plugins**.
10. Проверьте control в toolbox и вкладке **Plugins**.

## Минимальный пример

В репозитории есть готовый minimal SDK example:

- `Plugins/MinimalDesignerPlugin/MinimalDesignerPlugin.csproj`
- `Plugins/MinimalDesignerPlugin/MinimalPluginPackage.cs`
- `Plugins/MinimalDesignerPlugin/Descriptors/HelloCardDescriptor.cs`
- `Plugins/MinimalDesignerPlugin/Controls/HelloCard.cs`

Сборка:

```powershell
dotnet build .\Plugins\MinimalDesignerPlugin\MinimalDesignerPlugin.csproj
```

После сборки редактор найдёт control `Minimal.HelloCard` в папке `Plugins`.

## Template для нового plugin

Шаблон лежит здесь:

```text
templates/DesignerPluginTemplate
```

Как использовать:

1. Скопируйте папку в `Plugins\MyCompanyPlugin`.
2. Переименуйте `.csproj`, namespace и class names.
3. Замените `PluginIdValue`, `PluginVersionValue`, `TypeKeyValue`.
4. Настройте preview и export.
5. Соберите проект.

## Plugin package

```csharp
using FormDesigner.PluginContracts;

[assembly: FormDesignerPlugin(typeof(MyCompanyPlugin.PluginPackage))]

namespace MyCompanyPlugin;

public sealed class PluginPackage : IFormDesignerPlugin
{
    public string Id => "MyCompany.Controls";
    public string Title => "My Company Controls";
    public Version ApiVersion => new(1, 0, 0);

    public void Register(IDesignerRegistry registry)
    {
        registry.RegisterControl(new MyControlDescriptor(Id, "1.0.0"));
    }
}
```

`ApiVersion` проверяется loader-ом. Сейчас поддерживается major version `1`.

## Descriptor

```csharp
public sealed class MyControlDescriptor : IControlDescriptor
{
    public string TypeKey => "MyCompany.InfoPanel";
    public string Title => "Info Panel";
    public string Category => "My Company";
    public string Description => "Simple plugin panel.";
    public bool IsContainer => false;
    public bool CanHostChildren => false;
    public string ChildLayoutMode => "Absolute";

    public IReadOnlyList<DesignPropertyDescriptor> Properties => PropertySchema;

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = TypeKey,
            DescriptorId = TypeKey,
            PluginId = "MyCompany.Controls",
            PluginVersion = "1.0.0"
        };

        definition.BuiltInProperties["Width"] = 240d;
        definition.BuiltInProperties["Height"] = 96d;
        definition.CustomProperties["Caption"] = JsonSerializer.Serialize("Hello plugin");
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        return new Border
        {
            Width = control.GetDouble("Width", 240),
            Height = control.GetDouble("Height", 96),
            Child = new TextBlock
            {
                Text = control.GetCustomValue("Caption", "Hello plugin")
            }
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        writer.WriteLine(indentLevel, "<Border Width=\"240\" Height=\"96\" />");
    }
}
```

## Custom properties

Descriptor-driven свойства описываются через `DesignPropertyDescriptor`.

```csharp
private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
{
    new DesignPropertyDescriptor
    {
        Key = "Caption",
        Title = "Caption",
        Category = "Content",
        Editor = PropertyEditorKind.Text,
        DefaultValueJson = JsonSerializer.Serialize("Hello plugin")
    },
    new DesignPropertyDescriptor
    {
        Key = "AccentBrush",
        Title = "Accent",
        Category = "Appearance",
        Editor = PropertyEditorKind.Color,
        DefaultValueJson = JsonSerializer.Serialize("#2563EB")
    }
};
```

Custom properties сохраняются в `.formdesigner.json` как JSON-строки. Если plugin DLL временно отсутствует, значения не теряются.

## Sample plugin

Для расширенного примера смотрите:

- `Plugins/DemoDesignerPlugin/DemoPluginPackage.cs`
- `Plugins/DemoDesignerPlugin/Descriptors/DemoDevButtonDescriptor.cs`
- `Plugins/DemoDesignerPlugin/Descriptors/DemoGridControlDescriptor.cs`
- `Plugins/DemoDesignerPlugin/Descriptors/DemoTreeListDescriptor.cs`

Он показывает несколько controls, custom properties, preview provider, export provider, категории toolbox и fallback-friendly metadata.

## Диагностика загрузки

Откройте вкладку **Plugins**. Там показывается:

- сколько DLL найдено
- сколько plugin packages загружено
- сколько warning/error
- какие controls зарегистрированы
- путь к DLL
- SDK API version
- duplicate `TypeKey`
- ошибки `Register(...)`
- dependency loading errors

Если control не появился в toolbox:

1. Проверьте, что DLL лежит в `Plugins\<PluginName>\`.
2. Проверьте `[assembly: FormDesignerPlugin(...)]`.
3. Проверьте, что class реализует `IFormDesignerPlugin`.
4. Проверьте `ApiVersion`.
5. Проверьте duplicate `TypeKey`.
6. Проверьте зависимости рядом с DLL.
7. Нажмите **Reload plugins** или перезапустите редактор.

## Fallback behavior

Если документ содержит plugin control, а DLL отсутствует:

- control не удаляется из документа
- custom properties сохраняются
- designer показывает missing-plugin placeholder
- export пишет безопасный placeholder/warning
- diagnostics показывает missing plugin descriptor

Это позволяет открыть и сохранить документ без потери данных.

