using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Themes.Fluent;
using Eremex.AvaloniaUI.Controls.Editors;
using Eremex.AvaloniaUI.Themes.DeltaDesign;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace EremexDesignerPlugin.Services;

internal sealed record EremexCompatibilityResult(bool IsCompatible, string Reason);

internal static class EremexCompatibilityValidator
{
    private const string RequiredMethodName = "ProvideValue";

    public static EremexCompatibilityResult Validate()
    {
        Debug.WriteLine($"EREMEX_COMPATIBILITY_CHECK_START plugin={EremexPlugin.PluginIdValue}; packageVersion={EremexPlugin.PackageVersion}");

        var assemblies = GetDiagnosticAssemblies();
        foreach (var assembly in assemblies)
            LogAssembly(assembly);

        var markupAssembly = typeof(ReflectionBindingExtension).Assembly;
        var requiredMethod = typeof(ReflectionBindingExtension).GetMethod(
            RequiredMethodName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(IServiceProvider) },
            modifiers: null);
        var requiredMethodFound = requiredMethod?.ReturnType == typeof(Binding);
        var actualSignature = requiredMethod is null
            ? "-"
            : $"{requiredMethod.ReturnType.FullName} {requiredMethod.Name}({string.Join(", ", requiredMethod.GetParameters().Select(parameter => parameter.ParameterType.FullName))})";
        Debug.WriteLine(
            $"EREMEX_REQUIRED_METHOD_CHECK declaringType={typeof(ReflectionBindingExtension).FullName}; " +
            $"method={RequiredMethodName}; expectedSignature={typeof(Binding).FullName} {RequiredMethodName}({typeof(IServiceProvider).FullName}); " +
            $"found={requiredMethodFound}; actualSignature={actualSignature}; loadedAssemblyVersion={markupAssembly.GetName().Version}; " +
            $"loadedAssemblyLocation={SafeLocation(markupAssembly)}");

        var hostAvaloniaVersion = typeof(AvaloniaObject).Assembly.GetName().Version;
        var referenceVersions = GetEremexAvaloniaReferences();
        var familyMatches = hostAvaloniaVersion is not null
            && referenceVersions.Count > 0
            && referenceVersions.All(version => version.Major == hostAvaloniaVersion.Major && version.Minor == hostAvaloniaVersion.Minor);
        var duplicateCoreAssembly = HasDuplicateCoreAvaloniaAssembly();

        if (!requiredMethodFound)
        {
            return Fail(
                $"Eremex {EremexPlugin.PackageVersion} is incompatible with Avalonia {hostAvaloniaVersion}: " +
                "ReflectionBindingExtension.ProvideValue(IServiceProvider) returning Avalonia.Data.Binding is unavailable.");
        }

        if (!familyMatches)
        {
            var expectedVersions = string.Join(", ", referenceVersions.Select(version => version.ToString()).Distinct(StringComparer.Ordinal));
            return Fail(
                $"Eremex {EremexPlugin.PackageVersion} targets Avalonia {expectedVersions}, " +
                $"but the Designer host uses Avalonia {hostAvaloniaVersion}. A matching Avalonia major/minor line is required.");
        }

        if (duplicateCoreAssembly)
        {
            return Fail("More than one AssemblyLoadContext contains a core Avalonia assembly. Eremex controls cannot be inserted into the host visual tree safely.");
        }

        Debug.WriteLine(
            $"EREMEX_COMPATIBILITY_CHECK_SUCCESS plugin={EremexPlugin.PluginIdValue}; hostAvalonia={hostAvaloniaVersion}; " +
            $"referencedAvalonia={string.Join(",", referenceVersions.Select(version => version.ToString()).Distinct(StringComparer.Ordinal))}");
        return new EremexCompatibilityResult(true, string.Empty);
    }

    private static IReadOnlyList<Assembly> GetDiagnosticAssemblies()
    {
        var assemblies = new List<Assembly>
        {
            typeof(AvaloniaObject).Assembly,
            typeof(Control).Assembly,
            typeof(ReflectionBindingExtension).Assembly,
            typeof(FluentTheme).Assembly,
            typeof(TextEditor).Assembly,
            typeof(DeltaDesignTheme).Assembly
        };

        return assemblies.Distinct().ToList();
    }

    private static void LogAssembly(Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) == true)
            .Select(reference => reference.FullName)
            .ToList();
        Debug.WriteLine(
            $"EREMEX_RUNTIME_COMPATIBILITY_CHECK assembly={assembly.GetName().Name}; fullName={assembly.FullName}; " +
            $"version={assembly.GetName().Version}; location={SafeLocation(assembly)}; " +
            $"assemblyLoadContext={AssemblyLoadContext.GetLoadContext(assembly)?.Name ?? "-"}; " +
            $"mvid={assembly.ManifestModule.ModuleVersionId}; referencedAvalonia={string.Join("|", references)}");
    }

    private static IReadOnlyList<Version> GetEremexAvaloniaReferences()
    {
        return new[] { typeof(TextEditor).Assembly, typeof(DeltaDesignTheme).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Where(reference => reference.Name is "Avalonia.Base" or "Avalonia.Controls" or "Avalonia.Markup.Xaml")
            .Select(reference => reference.Version)
            .Where(version => version is not null)
            .Cast<Version>()
            .Distinct()
            .ToList();
    }

    private static bool HasDuplicateCoreAvaloniaAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name is "Avalonia.Base" or "Avalonia.Controls" or "Avalonia.Markup.Xaml")
            .GroupBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .Any(group => group.Select(assembly => AssemblyLoadContext.GetLoadContext(assembly)).Distinct().Count() > 1);
    }

    private static EremexCompatibilityResult Fail(string reason)
    {
        Debug.WriteLine($"EREMEX_COMPATIBILITY_CHECK_FAILED plugin={EremexPlugin.PluginIdValue}; reason={reason}");
        return new EremexCompatibilityResult(false, reason);
    }

    private static string SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch (NotSupportedException)
        {
            return "-";
        }
    }
}
