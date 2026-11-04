using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunReleaseCandidatePackageChecks()
    {
        var passed = 0; var failed = 0;
        var root = Environment.GetEnvironmentVariable("FORMDESIGNER_RC_ROOT")
            ?? Path.Combine(FindRepositoryRoot(), "artifacts", "beta01-rc");
        void Check(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS RC_" + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL RC_" + name + ": " + error); }
        }
        foreach (var name in new[] { "Standalone", "VsHost" })
        {
            var folder = Path.Combine(root, "staging", name);
            var entry = name == "Standalone" ? "FormDesigner" : "AvaloniaDesigner.VsHost";
            Check(name + "_SelfContainedX64", () =>
            {
                foreach (var file in new[] { entry + ".exe", entry + ".dll", entry + ".deps.json", entry + ".runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "Avalonia.Controls.dll", "Avalonia.Controls.DataGrid.dll" })
                    RequireCapability(File.Exists(Path.Combine(folder, file)), "Missing payload: " + file);
                using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, entry + ".runtimeconfig.json")));
                RequireCapability(config.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _), "Framework-dependent runtimeconfig in self-contained RC.");
                using var stream = File.OpenRead(Path.Combine(folder, entry + ".exe")); using var pe = new PEReader(stream);
                RequireCapability(pe.PEHeaders.CoffHeader.Machine == Machine.Amd64, "Unexpected apphost architecture.");
            });
            Check(name + "_Version", () => RequireCapability(FileVersionInfo.GetVersionInfo(Path.Combine(folder, "FormDesigner.dll")).ProductVersion!.StartsWith("0.1.0-beta.1", StringComparison.Ordinal), "Wrong product version."));
            Check(name + "_NoExperimentalOrPrivateFiles", () =>
            {
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                RequireCapability(files.All(p => !Path.GetFileName(p).StartsWith("Eremex", StringComparison.OrdinalIgnoreCase)
                    && !new[] { ".pdb", ".log", ".dmp", ".tmp" }.Contains(Path.GetExtension(p))
                    && Path.GetFileName(p) != "app-settings.json" && !p.Contains("recovery.json", StringComparison.OrdinalIgnoreCase)), "Vendor, debug or private file in standard payload.");
            });
            Check(name + "_RuntimeDependencyClosure", () =>
            {
                using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, entry + ".deps.json")));
                var target = deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
                foreach (var library in deps.RootElement.GetProperty("targets").GetProperty(target).EnumerateObject())
                foreach (var kind in new[] { "runtime", "native", "resources" })
                {
                    if (!library.Value.TryGetProperty(kind, out var assets)) continue;
                    foreach (var asset in assets.EnumerateObject())
                    {
                        if (asset.Name.EndsWith("/_._", StringComparison.Ordinal)) continue;
                        var direct = Path.Combine(folder, asset.Name.Replace('/', Path.DirectorySeparatorChar));
                        var flat = Path.Combine(folder, Path.GetFileName(asset.Name));
                        var localized = kind == "resources" && asset.Value.TryGetProperty("locale", out var locale)
                            ? Path.Combine(folder, locale.GetString()!, Path.GetFileName(asset.Name)) : direct;
                        RequireCapability(File.Exists(direct) || File.Exists(flat) || File.Exists(localized), "Unresolved runtime asset: " + asset.Name);
                    }
                }
            });
        }
        var unpacked = Path.Combine(root, "evidence", "extracted-vsix");
        Check("VsixIdentityAndVersion", () =>
        {
            var xml = XDocument.Load(Path.Combine(unpacked, "extension.vsixmanifest"));
            var identity = xml.Descendants().Single(e => e.Name.LocalName == "Identity");
            RequireCapability((string?)identity.Attribute("Id") == "AvaloniaDesigner.VSIX" && (string?)identity.Attribute("Version") == "0.1.18", "VSIX identity/version changed incorrectly.");
        });
        Check("BridgeDoesNotReferenceDesignerOrModernRuntime", () =>
        {
            using var stream = File.OpenRead(Path.Combine(unpacked, "AvaloniaDesigner.VSIX.dll")); using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var references = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray();
            RequireCapability(references.All(n => !n.StartsWith("Avalonia.", StringComparison.Ordinal) && !n.StartsWith("Eremex", StringComparison.Ordinal) && n != "FormDesigner" && n != "System.Private.CoreLib"), "Modern visual runtime leaked into devenv bridge references.");
            RequireCapability(!File.Exists(Path.Combine(unpacked, "coreclr.dll")) && !File.Exists(Path.Combine(unpacked, "Avalonia.Controls.dll")), "Runtime DLL at VSIX bridge root.");
        });
        Check("VsixContainsExactPublishedHost", () =>
        {
            var published = Path.Combine(root, "staging", "VsHost");
            var files = Directory.GetFiles(published, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(published, file); var packaged = Path.Combine(unpacked, "VsHost", relative);
                RequireCapability(File.Exists(packaged) && File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(packaged)), "VSIX runtime differs from verified publish: " + relative);
            }
        });
        Console.WriteLine($"RC package checks: {passed}/{passed + failed}");
        return failed == 0 ? 0 : 1;
    }
}
