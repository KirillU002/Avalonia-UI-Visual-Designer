# DemoDesignerPlugin

Extended plugin sample for FormDesigner SDK scenarios.

It demonstrates:

- multiple descriptors in one plugin package
- custom properties
- richer preview controls
- XAML export with plugin XML namespace
- toolbox categories
- fallback-friendly document metadata through `PluginId` and `PluginVersion`

Controls:

- `Demo.DevButton`
- `Demo.GridControl`
- `Demo.TreeList`

Build:

```powershell
dotnet build .\Plugins\DemoDesignerPlugin\DemoDesignerPlugin.csproj
```

The output is copied to:

```text
bin\<Configuration>\net7.0\Plugins\DemoDesignerPlugin\
```

