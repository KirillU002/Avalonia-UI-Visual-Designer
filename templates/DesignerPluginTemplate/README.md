# DesignerPluginTemplate

Copy this folder when you want to create a new FormDesigner control plugin.

## Quick start

1. Copy `templates/DesignerPluginTemplate` to a new folder, for example `Plugins/MyCompanyPlugin`.
2. Rename the project file and namespaces.
3. Change `PluginIdValue`, `PluginVersionValue`, and `TypeKeyValue`.
4. Implement your descriptor properties, preview and XAML export.
5. Build the project:

```powershell
dotnet build .\Plugins\MyCompanyPlugin\MyCompanyPlugin.csproj
```

6. Put the output DLL and its dependencies into:

```text
bin\<Configuration>\net7.0\Plugins\MyCompanyPlugin\
```

7. Restart the editor or use **Plugins -> Reload plugins**.

## Required pieces

- `PluginPackage.cs`: plugin entry point, marked with `[assembly: FormDesignerPlugin(...)]`.
- `MyControlDescriptor.cs`: toolbox metadata, default model, preview and XAML export.
- `DesignerPluginTemplate.csproj`: references `FormDesigner.PluginContracts` and Avalonia.

## Diagnostics

Open the **Plugins** workspace in the editor. It shows:

- discovered DLL count
- loaded packages
- warnings/errors
- registered controls
- dependency or duplicate `TypeKey` problems

