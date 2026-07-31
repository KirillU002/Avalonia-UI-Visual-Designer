using Avalonia;
using Avalonia.Styling;
using System;
using System.Diagnostics;
using System.Linq;

namespace EremexDesignerPlugin.Services;

internal static class EremexPreviewTheme
{
    public static void EnsureInstalled(StyledElement scopeRoot, string scope)
    {
        if (scopeRoot is null)
            throw new ArgumentNullException(nameof(scopeRoot));

        Debug.WriteLine(
            "EREMEX_THEME_SCOPE_APPLY_START " +
            $"scope={scope}; targetScope={scopeRoot.GetType().FullName}; targetName={scopeRoot.Name ?? "-"}");

        var application = Application.Current;
        var globalStylesBefore = application?.Styles.Count ?? -1;
        Debug.WriteLine(
            "DESIGNER_GLOBAL_STYLE_SNAPSHOT " +
            $"phase=before-eremex-scope; stylesCount={globalStylesBefore}");

        if (application is not null)
        {
            Debug.WriteLine(
                "EREMEX_THEME_GLOBAL_APPLICATION_MUTATION_BLOCKED " +
                "reason=DeltaDesignTheme is scoped to an Eremex preview subtree, never Application.Current.Styles");
        }

        try
        {
            if (scopeRoot.Styles.OfType<EremexPreviewThemeResource>().Any())
            {
                Debug.WriteLine(
                    "EREMEX_THEME_SCOPE_APPLIED " +
                    $"scope={scope}; affectedRootType={scopeRoot.GetType().FullName}; alreadyApplied=true");
                return;
            }

            scopeRoot.Styles.Add(new EremexPreviewThemeResource());
            Debug.WriteLine(
                "EREMEX_THEME_SCOPE_APPLIED " +
                $"scope={scope}; affectedRootType={scopeRoot.GetType().FullName}; theme=DeltaDesignTheme; alreadyApplied=false");
            Debug.WriteLine(
                "DESIGNER_GLOBAL_STYLE_SNAPSHOT " +
                $"phase=after-eremex-scope; stylesCount={application?.Styles.Count ?? -1}; unchanged={(application?.Styles.Count ?? -1) == globalStylesBefore}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                "EREMEX_THEME_SCOPE_APPLY_FAILED " +
                $"scope={scope}; exception={ex.GetType().Name}; reason={ex.Message}");
            throw;
        }
    }
}
