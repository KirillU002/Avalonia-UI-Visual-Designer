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
    public const string DefaultNuGetSourceUrl = "https://api.nuget.org/v3/index.json";
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
        return await ValidateBuildAsync(result, artifactsRoot, Array.Empty<NuGetPackageSource>(), logAsync, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExportBuildValidationResult> ValidateBuildAsync(
        ExportResult result,
        string artifactsRoot,
        IEnumerable<NuGetPackageSource>? packageSources,
        Func<string, Task>? logAsync = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var detailedLog = new StringBuilder();
        var stepSummaries = new List<string>();
        var validationSources = NormalizePackageSources(packageSources).ToList();
        var hasCustomSources = validationSources.Count > 0;
        var selectedSourceText = hasCustomSources
            ? string.Join(", ", validationSources.Select(source => $"{source.Name}={source.Value}"))
            : $"nuget.org={DefaultNuGetSourceUrl}";
        var allowInsecureText = hasCustomSources
            ? string.Join(", ", validationSources.Select(source => $"{source.Name}:{source.AllowInsecureConnections}"))
            : "false";
        var runRoot = Path.Combine(artifactsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        await LogValidationAsync("VALIDATE_BUILD_START", $"projectPath={runRoot}; reason=on-demand").ConfigureAwait(false);
        await LogValidationAsync(
            "VALIDATE_BUILD_NUGET_SOURCE_SELECTED",
            $"source={selectedSourceText}; sourceKind={(hasCustomSources ? string.Join(",", validationSources.Select(source => GetNuGetSourceKind(source.Value))) : "https")}; custom={hasCustomSources}; allowInsecure={allowInsecureText}").ConfigureAwait(false);

        async Task RunStepAsync(string stepName, Func<Task> action)
        {
            var stopwatch = Stopwatch.StartNew();
            await LogValidationAsync("VALIDATE_BUILD_STEP_START", $"step={stepName}").ConfigureAwait(false);
            try
            {
                await action().ConfigureAwait(false);
                stopwatch.Stop();
                stepSummaries.Add($"{stepName}: OK ({stopwatch.ElapsedMilliseconds} ms)");
                await LogValidationAsync("VALIDATE_BUILD_STEP_END", $"step={stepName}; elapsedMs={stopwatch.ElapsedMilliseconds}; success=true").ConfigureAwait(false);
            }
            catch
            {
                stopwatch.Stop();
                stepSummaries.Add($"{stepName}: Failed ({stopwatch.ElapsedMilliseconds} ms)");
                await LogValidationAsync("VALIDATE_BUILD_STEP_END", $"step={stepName}; elapsedMs={stopwatch.ElapsedMilliseconds}; success=false").ConfigureAwait(false);
                throw;
            }
        }

        await RunStepAsync("Preparing export workspace", () =>
        {
            Directory.CreateDirectory(runRoot);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        await RunStepAsync("Generating project files", () => WriteValidationProjectAsync(result, runRoot, validationSources, !hasCustomSources, cancellationToken)).ConfigureAwait(false);
        await LogValidationAsync("VALIDATE_BUILD_PROJECT", $"path={runRoot}").ConfigureAwait(false);
        var nugetConfigPath = Path.Combine(runRoot, "NuGet.config");
        await LogValidationAsync(
            "NUGET_CONFIG_GENERATED_FOR_VALIDATE_BUILD",
            $"path={nugetConfigPath}; source={selectedSourceText}; allowInsecure={allowInsecureText}").ConfigureAwait(false);
        stepSummaries.Add($"NuGet source: {selectedSourceText}; custom={hasCustomSources}; allowInsecure={allowInsecureText}");
        stepSummaries.Add($"NuGet.config: {nugetConfigPath}");

        var projectFile = Directory.GetFiles(runRoot, "*.csproj").Single();
        ProcessResult restoreProcess = new(0, "");
        ProcessResult buildProcess = new(0, "");

        try
        {
            await RunStepAsync("Restoring NuGet packages", async () =>
            {
                var restoreArgs = $"restore \"{projectFile}\" --configfile \"{nugetConfigPath}\"";
                await LogValidationAsync("VALIDATE_BUILD_COMMAND", $"command=dotnet {restoreArgs}; workingDirectory={runRoot}").ConfigureAwait(false);
                restoreProcess = await RunProcessAsync("dotnet", restoreArgs, runRoot, LogValidationProcessLineAsync, cancellationToken).ConfigureAwait(false);
                if (restoreProcess.ExitCode != 0)
                    throw new InvalidOperationException($"dotnet restore failed with exit code {restoreProcess.ExitCode}.");
            }).ConfigureAwait(false);
        }
        catch
        {
            // The failure is represented in the returned validation result below.
        }

        if (restoreProcess.ExitCode == 0)
        {
            try
            {
                await RunStepAsync("Building project", async () =>
                {
                    await LogValidationAsync("VALIDATE_BUILD_COMMAND", $"command=dotnet build \"{projectFile}\" --no-restore; workingDirectory={runRoot}").ConfigureAwait(false);
                    buildProcess = await RunProcessAsync("dotnet", $"build \"{projectFile}\" --no-restore", runRoot, LogValidationProcessLineAsync, cancellationToken).ConfigureAwait(false);
                    if (buildProcess.ExitCode != 0)
                        throw new InvalidOperationException($"dotnet build failed with exit code {buildProcess.ExitCode}.");
                }).ConfigureAwait(false);
            }
            catch
            {
                // The failure is represented in the returned validation result below.
            }
        }

        var rawOutput = restoreProcess.Output + buildProcess.Output;
        var dedupedOutput = DeduplicateBuildOutput(rawOutput);
        await RunStepAsync("Collecting warnings/errors", () =>
        {
            detailedLog.AppendLine();
            detailedLog.AppendLine("Deduplicated output:");
            detailedLog.AppendLine(dedupedOutput);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        await RunStepAsync("Cleaning temporary artifacts", () =>
        {
            PruneValidationRuns(artifactsRoot, runRoot);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        totalStopwatch.Stop();
        var logPath = Path.Combine(runRoot, "validate-build.log");
        detailedLog.AppendLine();
        detailedLog.AppendLine($"VALIDATE_BUILD_END success={restoreProcess.ExitCode == 0 && buildProcess.ExitCode == 0}; elapsedMs={totalStopwatch.ElapsedMilliseconds}");
        await File.WriteAllTextAsync(logPath, detailedLog.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        var finalExitCode = restoreProcess.ExitCode != 0 ? restoreProcess.ExitCode : buildProcess.ExitCode;
        await LogValidationAsync(
            "VALIDATE_BUILD_END",
            $"success={finalExitCode == 0}; warnings={CountBuildOutputLines(dedupedOutput, "warning")}; errors={CountBuildOutputLines(dedupedOutput, "error")}; elapsedMs={totalStopwatch.ElapsedMilliseconds}; log={logPath}").ConfigureAwait(false);

        return new ExportBuildValidationResult
        {
            Status = finalExitCode == 0 ? ExportBuildValidationStatus.Passed : ExportBuildValidationStatus.Failed,
            ProjectPath = runRoot,
            ExitCode = finalExitCode,
            Output = dedupedOutput,
            DetailedLogPath = logPath,
            StepSummary = string.Join(Environment.NewLine, stepSummaries),
            CompletedUtc = DateTime.UtcNow
        };

        async Task LogValidationAsync(string eventName, string details)
        {
            var line = $"{eventName} {details}";
            detailedLog.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            await LogAsync(logAsync, line).ConfigureAwait(false);
        }

        async Task LogValidationProcessLineAsync(string line)
        {
            detailedLog.AppendLine(line);
            await LogAsync(logAsync, $"VALIDATE_BUILD_OUTPUT severity={ClassifyBuildOutputSeverity(line)}; {line}").ConfigureAwait(false);
        }
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

    private static async Task WriteValidationProjectAsync(
        ExportResult result,
        string projectPath,
        IEnumerable<NuGetPackageSource> packageSources,
        bool includeDefaultNugetSource,
        CancellationToken cancellationToken)
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
        await File.WriteAllTextAsync(Path.Combine(projectPath, "NuGet.config"), BuildNuGetConfigForSources(packageSources, includeDefaultNugetSource), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildNuGetConfig()
    {
        return BuildNuGetConfigForSources(Array.Empty<NuGetPackageSource>());
    }

    public static string BuildNuGetConfigForSources(IEnumerable<NuGetPackageSource> additionalSources)
    {
        return BuildNuGetConfigForSources(additionalSources, includeDefaultNugetSource: true);
    }

    public static string BuildNuGetConfigForSources(IEnumerable<NuGetPackageSource> additionalSources, bool includeDefaultNugetSource)
    {
        var sources = new List<NuGetPackageSource>
        {
        };

        if (includeDefaultNugetSource)
            sources.Add(new NuGetPackageSource("nuget.org", DefaultNuGetSourceUrl));

        foreach (var source in NormalizePackageSources(additionalSources))
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
            var allowInsecure = source.Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && source.AllowInsecureConnections
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

    private static IEnumerable<NuGetPackageSource> NormalizePackageSources(IEnumerable<NuGetPackageSource>? sources)
    {
        if (sources is null)
            yield break;

        var index = 1;
        foreach (var source in sources)
        {
            var value = source.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var name = string.IsNullOrWhiteSpace(source.Name)
                ? $"CustomSource{index}"
                : source.Name.Trim();
            yield return source with
            {
                Name = name,
                Value = value
            };
            index++;
        }
    }

    public static string GetNuGetSourceKind(string source)
    {
        if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "https";
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "http";
        if (source.StartsWith(@"\\", StringComparison.Ordinal))
            return "network-share";
        if (Path.IsPathRooted(source))
            return "local-folder";
        return "custom";
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

    private static string ClassifyBuildOutputSeverity(string line)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || line.Contains("NU", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return "Info";
    }

    private static int CountBuildOutputLines(string output, string token)
    {
        if (string.IsNullOrWhiteSpace(output))
            return 0;

        return output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(token, StringComparison.OrdinalIgnoreCase));
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

public sealed record NuGetPackageSource(string Name, string Value, bool AllowInsecureConnections = true);

