# MinimalDesignerPlugin

Small SDK example for FormDesigner plugin authors.

It registers one control:

- `Minimal.HelloCard`
- toolbox metadata
- default model values
- designer/runtime preview
- custom properties (`Message`, `AccentBrush`)
- XAML export to `<minimal:HelloCard />`

Build:

```powershell
dotnet build .\Plugins\MinimalDesignerPlugin\MinimalDesignerPlugin.csproj
```

The project writes its output to:

```text
bin\<Configuration>\net7.0\Plugins\MinimalDesignerPlugin\
```

After build, start the editor and open the **Plugins** workspace. The control appears as **Hello Card** in the plugin toolbox.

