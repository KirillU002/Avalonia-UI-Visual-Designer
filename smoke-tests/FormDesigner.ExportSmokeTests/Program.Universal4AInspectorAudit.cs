using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.ViewModels;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4AInspectorAudit()
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var imported = new AxamlImportService().Import(File.ReadAllText(path), path);
        var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(imported, path);
        var observations = new List<object>();
        foreach (var reference in vm.ActiveAxamlSourceDocument!.SourceMap.Controls)
        {
            vm.SelectSingleControl(vm.GetControl(reference.ControlId)!);
            var editable = vm.PropertyGridCategories.SelectMany(c => c.Rows).Where(r => !r.IsReadOnly)
                .Select(r => r.Key switch { "X" => "Canvas.Left", "Y" => "Canvas.Top", _ => r.Key }).ToHashSet();
            var present = reference.Capability.Properties.Where(p => reference.Element.FindAttribute(p.SourceName) is not null).ToArray();
            observations.Add(new { Type = reference.Element.LocalName, PresentMapped = present.Length,
                PatchablePresent = present.Count(p => p.Mode == AxamlPropertyCapabilityMode.Editable),
                InspectorEditablePresent = present.Count(p => p.Mode == AxamlPropertyCapabilityMode.Editable && editable.Contains(p.Key)),
                InspectorEditableSlots = editable.Count, MissingInspector = present.Where(p => p.Mode == AxamlPropertyCapabilityMode.Editable && !editable.Contains(p.Key)).Select(p => p.SourceName).ToArray() });
        }
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Selection during coverage audit modified source.");
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4A_OUTPUT")!, JsonSerializer.Serialize(observations));
        Console.WriteLine("INSPECTOR4A controls=" + observations.Count); return 0;
    }
}
