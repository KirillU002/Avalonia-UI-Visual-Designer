using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class ExportPipelineService
{
    private const string AvaloniaVersion = "11.1.1";
    private const string AvaloniaDesktopVersion = "11.1.1";
    private const int ValidationRunsToKeep = 5;

    public ExportResult CreateResult(
        ExportProfile profile,
        IEnumerable<GeneratedFileModel> generatedFiles,
        IEnumerable<RequiredPackageModel> requiredPackages,
        IEnumerable<ExportDiagnosticModel> diagnostics,
        ExportBuildValidationResult? buildValidation = null)
    {
        return new ExportResult
        {
            Profile = profile,
            GeneratedFiles = generatedFiles.ToList(),
            RequiredPackages = requiredPackages.ToList(),
            Diagnostics = DeduplicateDiagnostics(diagnostics).ToList(),
            BuildValidation = buildValidation ?? new ExportBuildValidationResult(),
            GeneratedUtc = DateTime.UtcNow
        };
    }

    public async Task<ExportBuildValidationResult> ValidateBuildAsync(
        ExportResult result,
        string artifactsRoot,
        Func<string, Task>? logAsync = null,
        CancellationToken cancellationToken = default)
    {
        var runRoot = Path.Combine(artifactsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runRoot);
        await WriteValidationProjectAsync(result, runRoot, cancellationToken).ConfigureAwait(false);
        await LogAsync(logAsync, $"Validation project: {runRoot}").ConfigureAwait(false);

        var projectFile = Directory.GetFiles(runRoot, "*.csproj").Single();
        var process = await RunProcessAsync("dotnet", $"build \"{projectFile}\"", runRoot, logAsync, cancellationToken).ConfigureAwait(false);
        PruneValidationRuns(artifactsRoot, runRoot);
        return new ExportBuildValidationResult
        {
            Status = process.ExitCode == 0 ? ExportBuildValidationStatus.Passed : ExportBuildValidationStatus.Failed,
            ProjectPath = runRoot,
            ExitCode = process.ExitCode,
            Output = DeduplicateBuildOutput(process.Output),
            CompletedUtc = DateTime.UtcNow
        };
    }

    public async Task ExportToProjectAsync(
        ExportResult result,
        string targetFolder,
        Func<string, Task>? logAsync = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);

        foreach (var file in result.GeneratedFiles.Where(file => !IsGeneratedReadme(file.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(targetFolder, file.Path.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, file.Content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            await LogAsync(logAsync, $"Wrote {file.Path}").ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(targetFolder, "README.generated.md"), BuildReadme(result), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "required-packages.txt"), BuildPackagesText(result), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "export-diagnostics.txt"), BuildDiagnosticsText(result), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportZipAsync(
        ExportResult result,
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        await using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in result.GeneratedFiles.Where(file => !IsGeneratedReadme(file.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(file.Path.Replace('\\', '/'));
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            await writer.WriteAsync(file.Content).ConfigureAwait(false);
        }

        await WriteZipTextAsync(archive, "README.generated.md", BuildReadme(result), cancellationToken).ConfigureAwait(false);
        await WriteZipTextAsync(archive, "required-packages.txt", BuildPackagesText(result), cancellationToken).ConfigureAwait(false);
        await WriteZipTextAsync(archive, "export-diagnostics.txt", BuildDiagnosticsText(result), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteValidationProjectAsync(ExportResult result, string projectPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectPath);
        foreach (var file in result.GeneratedFiles.Where(file => file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                     || file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var targetPath = Path.Combine(projectPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(targetPath, file.Content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(projectPath, "App.axaml"), BuildAppXaml(result.Profile.ProjectNamespace), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "App.axaml.cs"), BuildAppCode(result.Profile.ProjectNamespace), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "Program.cs"), BuildProgramCode(result.Profile.ProjectNamespace), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "ExportValidation.csproj"), BuildProjectFile(result.RequiredPackages), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "NuGet.config"), BuildNuGetConfig(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildNuGetConfig()
    {
        return BuildNuGetConfigForSources(Array.Empty<NuGetPackageSource>());
    }

    public static string BuildNuGetConfigForSources(IEnumerable<NuGetPackageSource> additionalSources)
    {
        var sources = new List<NuGetPackageSource>
        {
            new("nuget.org", "https://api.nuget.org/v3/index.json")
        };

        foreach (var source in additionalSources)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Value))
                continue;

            if (sources.Any(item => string.Equals(item.Name, source.Name, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(item.Value, source.Value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            sources.Add(source);
        }

        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        sb.AppendLine("<configuration>");
        sb.AppendLine("  <packageSources>");
        sb.AppendLine("    <clear />");
        foreach (var source in sources)
        {
            var protocol = source.Value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? " protocolVersion=\"3\"" : "";
            var allowInsecure = source.Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? " allowInsecureConnections=\"true\""
                : "";
            sb.AppendLine($"    <add key=\"{EscapeXml(source.Name)}\" value=\"{EscapeXml(source.Value)}\"{protocol}{allowInsecure} />");
        }
        sb.AppendLine("  </packageSources>");
        sb.AppendLine("  <packageSourceMapping>");
        sb.AppendLine("    <clear />");
        sb.AppendLine("  </packageSourceMapping>");
        sb.AppendLine("</configuration>");
        return sb.ToString();
    }

    private static string BuildProjectFile(IEnumerable<RequiredPackageModel> requiredPackages)
    {
        var packageLines = new StringBuilder();
        foreach (var package in requiredPackages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var version = string.IsNullOrWhiteSpace(package.Version) ? AvaloniaVersion : package.Version;
            packageLines.AppendLine($"    <PackageReference Include=\"{EscapeXml(package.Id)}\" Version=\"{EscapeXml(version)}\" />");
        }

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Avalonia"" Version=""{AvaloniaVersion}"" />
    <PackageReference Include=""Avalonia.Desktop"" Version=""{AvaloniaDesktopVersion}"" />
    <PackageReference Include=""Avalonia.Themes.Fluent"" Version=""{AvaloniaVersion}"" />
    <PackageReference Include=""Avalonia.Fonts.Inter"" Version=""{AvaloniaDesktopVersion}"" />
{packageLines.ToString().TrimEnd()}
  </ItemGroup>
</Project>
";
    }

    private static string BuildAppXaml(string ns)
    {
        return $@"<Application xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
             x:Class=""{ns}.App""
             RequestedThemeVariant=""Default"">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
";
    }

    private static string BuildAppCode(string ns)
    {
        return @"using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace {Namespace};

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
".Replace("{Namespace}", ns);
    }

    private static string BuildProgramCode(string ns)
    {
        return @"using Avalonia;
using System;

namespace {Namespace};

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
".Replace("{Namespace}", ns);
    }

    private static string BuildReadme(ExportResult result)
    {
        return $@"# Generated Avalonia Export

Generated: {result.GeneratedUtc:u}
Target: {result.Profile.TargetMode}
Namespace: {result.Profile.ProjectNamespace}
DataGrid: {result.Profile.DataGridExportMode}
Layout: {result.Profile.LayoutExportMode}

## Quick start
1. Open this folder or solution in Visual Studio/Rider.
2. Run `dotnet restore`.
3. Run `dotnet build`.

The generated project targets `net6.0` and uses Avalonia `11.1.1`.

## NuGet notes
- Real DataGrid export requires `Avalonia.Controls.DataGrid 11.1.1`.
- Generated bindings are runtime bindings unless a real exported ViewModel type exists.
- If your NuGet source is HTTP/intranet-only, the generated `NuGet.config` must mark that source with `allowInsecureConnections=""true""`.

## Files
{string.Join(Environment.NewLine, result.GeneratedFiles.Select(file => $"- {file.Path} ({file.StatusText})"))}

## Required packages
{BuildPackagesText(result)}

## Diagnostics
{BuildDiagnosticsText(result)}
";
    }

    public static string DeduplicateBuildOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;

        var ordered = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var key = NormalizeBuildOutputLine(line);
            if (!counts.ContainsKey(key))
            {
                ordered.Add(line);
                counts[key] = 0;
            }

            counts[key]++;
        }

        var sb = new StringBuilder();
        foreach (var line in ordered)
        {
            var key = NormalizeBuildOutputLine(line);
            var count = counts[key];
            sb.AppendLine(count > 1 ? $"[x{count}] {line}" : line);
        }

        return sb.ToString();
    }

    private static string NormalizeBuildOutputLine(string line)
    {
        return line
            .Trim()
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);
    }

    private static string BuildPackagesText(ExportResult result)
    {
        return result.RequiredPackages.Count == 0
            ? "No additional packages required."
            : string.Join(Environment.NewLine, result.RequiredPackages.Select(package => $"{package.Id} {package.Version} - {package.Reason}{Environment.NewLine}{package.InstallCommand}"));
    }

    private static string BuildDiagnosticsText(ExportResult result)
    {
        return result.Diagnostics.Count == 0
            ? "No export diagnostics."
            : string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.SeverityText}: {diagnostic.Source} - {diagnostic.Message} {diagnostic.Details}".Trim()));
    }

    private static IEnumerable<ExportDiagnosticModel> DeduplicateDiagnostics(IEnumerable<ExportDiagnosticModel> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var diagnostic in diagnostics)
        {
            var key = $"{diagnostic.Severity}|{diagnostic.Source}|{diagnostic.Message}|{diagnostic.Details}";
            if (seen.Add(key))
                yield return diagnostic;
        }
    }

    private static async Task WriteZipTextAsync(ZipArchive archive, string path, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(text).ConfigureAwait(false);
    }

    private static bool IsGeneratedReadme(string path)
    {
        return string.Equals(path.Replace('\\', '/'), "README.generated.md", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Func<string, Task>? logAsync,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            output.AppendLine(args.Data);
            _ = LogAsync(logAsync, args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            output.AppendLine(args.Data);
            _ = LogAsync(logAsync, args.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start {fileName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output.ToString());
    }

    private static Task LogAsync(Func<string, Task>? logAsync, string message)
    {
        return logAsync?.Invoke(message) ?? Task.CompletedTask;
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static void PruneValidationRuns(string artifactsRoot, string currentRunRoot)
    {
        var root = new DirectoryInfo(Path.GetFullPath(artifactsRoot));
        if (!root.Exists)
            return;

        var current = Path.GetFullPath(currentRunRoot);
        foreach (var staleRun in root.GetDirectories()
                     .OrderByDescending(directory => directory.LastWriteTimeUtc)
                     .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                     .Skip(ValidationRunsToKeep))
        {
            var fullPath = Path.GetFullPath(staleRun.FullName);
            if (string.Equals(fullPath, current, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fullPath.StartsWith(root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteDirectory(staleRun);
        }
    }

    private static void TryDeleteDirectory(DirectoryInfo directory)
    {
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}

public sealed record NuGetPackageSource(string Name, string Value);

