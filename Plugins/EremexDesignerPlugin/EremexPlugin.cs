using EremexDesignerPlugin.Descriptors;
using FormDesigner.PluginContracts;
using System;
using System.Diagnostics;

[assembly: FormDesignerPlugin(typeof(EremexDesignerPlugin.EremexPlugin))]

namespace EremexDesignerPlugin;

public sealed class EremexPlugin : IFormDesignerPlugin, IDesignerExportContributionProvider, IDesignerRuntimePreviewContributionProvider
{
    public const string PluginIdValue = "Eremex.DesignerPlugin";
    public const string PluginVersionValue = "1.0.98";
    public const string ControlsPackageId = "Eremex.Avalonia.Controls";
    public const string ThemePackageId = "Eremex.Avalonia.Themes.DeltaDesign";
    public const string PackageVersion = "1.0.98";

    public string Id => PluginIdValue;

    public string Title => "Eremex Designer Plugin";

    public Version ApiVersion => new(1, 0, 0);

    public string ProviderId => "Eremex";

    public void Register(IDesignerRegistry registry)
    {
        Debug.WriteLine($"EREMEX_PLUGIN_LOAD_START plugin={PluginIdValue}; packageVersion={PackageVersion}");
        try
        {
            var compatibility = Services.EremexCompatibilityValidator.Validate();
            if (!compatibility.IsCompatible)
            {
                Debug.WriteLine($"EREMEX_CONTROL_REGISTRATION_SKIPPED plugin={PluginIdValue}; reason={compatibility.Reason}");
                throw new InvalidOperationException(compatibility.Reason);
            }

            var descriptors = new IControlDescriptor[]
            {
                new EremexTextEditorDescriptor(PluginIdValue, PluginVersionValue),
                new EremexDataGridControlDescriptor(PluginIdValue, PluginVersionValue)
            };

            foreach (var descriptor in descriptors)
            {
                registry.RegisterControl(descriptor);
                Debug.WriteLine($"EREMEX_CONTROL_REGISTERED descriptorId={descriptor.TypeKey}; provider={ProviderId}");
            }

            Debug.WriteLine(
                $"EREMEX_DATAGRID_DESCRIPTOR_REGISTERED descriptorId={EremexDataGridControlDescriptor.TypeKeyValue}; " +
                "clrType=Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl");
            Debug.WriteLine($"EREMEX_PLUGIN_LOAD_SUCCESS plugin={PluginIdValue}; packageVersion={PackageVersion}; controlsRegistered={descriptors.Length}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EREMEX_PLUGIN_LOAD_FAILED plugin={PluginIdValue}; exception={ex.GetType().Name}; reason={ex.Message}");
            throw;
        }
    }

    public DesignerExportContributions GetExportContributions(DesignerExportContributionContext context)
    {
        if (!UsesEremexControl(context))
            return new DesignerExportContributions();

        Debug.WriteLine(
            $"EREMEX_EXPORT_CONTRIBUTION_ADDED packages={ControlsPackageId},{ThemePackageId}; styles=1");

        return new DesignerExportContributions
        {
            Packages = new[]
            {
                new DesignerPackageReference(
                    ControlsPackageId,
                    PackageVersion,
                    "Требуется Eremex TextEditor и штатные Eremex MSBuild targets для trial/license data."),
                new DesignerPackageReference(
                    ThemePackageId,
                    PackageVersion,
                    "Тема DeltaDesign для отображения Eremex controls.")
            },
            ApplicationStyles = new[]
            {
                new DesignerApplicationStyleContribution(
                    "theme",
                    "clr-namespace:Eremex.AvaloniaUI.Themes.DeltaDesign;assembly=Eremex.Avalonia.Themes.DeltaDesign",
                    "<theme:DeltaDesignTheme />",
                    "Eremex DeltaDesign theme")
            }
        };
    }

    public DesignerRuntimePreviewContribution? GetRuntimePreviewContribution(DesignerRuntimePreviewContributionContext context)
    {
        if (!UsesEremexControl(context))
            return null;

        return new DesignerRuntimePreviewContribution
        {
            ProviderId = ProviderId,
            Assemblies = new[]
            {
                typeof(Eremex.AvaloniaUI.Controls.Editors.TextEditor).Assembly,
                typeof(Eremex.AvaloniaUI.Themes.DeltaDesign.DeltaDesignTheme).Assembly
            },
            ApplyToPreviewRoot = previewRoot =>
                Services.EremexPreviewTheme.EnsureInstalled(previewRoot, "RuntimeAxamlPreview")
        };
    }

    private static bool UsesEremexControl(DesignerExportContributionContext context)
    {
        return context.UsesControl(EremexTextEditorDescriptor.TypeKeyValue)
            || context.UsesControl(EremexDataGridControlDescriptor.TypeKeyValue);
    }

    private static bool UsesEremexControl(DesignerRuntimePreviewContributionContext context)
    {
        return context.UsesControl(EremexTextEditorDescriptor.TypeKeyValue)
            || context.UsesControl(EremexDataGridControlDescriptor.TypeKeyValue);
    }
}
