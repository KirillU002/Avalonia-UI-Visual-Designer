using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private const string CapabilityButton = "<Button x:Name=\"Button1\" Content=\"Old\" Width=\"160\" Height=\"40\" Canvas.Left=\"100\" Canvas.Top=\"100\" />";
    private const string CapabilityTextBox = "<TextBox x:Name=\"TextBox1\" Text=\"Hello\" Width=\"220\" Height=\"40\" Canvas.Left=\"100\" Canvas.Top=\"160\" />";
    private static string CapabilitySource(string children, string rootExtra = "") =>
        "<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:custom=\"using:Unknown\">" +
        rootExtra + "<Canvas>" + children + "</Canvas></Window>";

    private static AxamlElementCapability CapabilityFor(AxamlImportResult result, string name) =>
        result.CapabilityReport.Elements.Single(e => e.Name == name);

    private static void RequireCapability(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireIdentity(AxamlImportResult result)
    {
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, result.RoundTripDocument.OriginalText);
        RequireCapability(patch.CanApply && !patch.HasChanges && patch.PatchedText == result.RoundTripDocument.OriginalText,
            "No-edit patch must preserve the entire original text.");
    }

    private static void AssertCapabilitySimpleEditable(SmokeContext context)
    {
        var result = new AxamlImportService().Import(CapabilitySource(CapabilityButton + CapabilityTextBox));
        RequireCapability(result.CapabilityReport.Level == AxamlCapabilityLevel.FullyEditable, "Simple Canvas must be editable; xmlns declarations are not restrictions.");
        RequireCapability(result.Document.Controls.Count == 2 && CapabilityFor(result, "Button1").Mode == AxamlElementCapabilityMode.Editable, "Simple controls must be editable.");
        RequireIdentity(result);
    }

    private static void AssertCapabilityCommentPreserved(SmokeContext context)
    {
        var source = CapabilitySource("<!-- keep me -->" + CapabilityButton);
        var result = new AxamlImportService().Import(source);
        RequireCapability(result.CapabilityReport.Level == AxamlCapabilityLevel.FullyEditable, "Comments must not downgrade capability.");
        result.Document.Controls.Single().Text = "New";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.PatchedText == source.Replace("Content=\"Old\"", "Content=\"New\""), "Comment or unrelated source changed.");
    }

    private static void AssertCapabilityUnknownAttribute(SmokeContext context)
    {
        var source = CapabilitySource(CapabilityButton.Replace(" />", " custom:Something.Unknown=\"KEEP\" />"));
        var result = new AxamlImportService().Import(source);
        var capability = CapabilityFor(result, "Button1");
        RequireCapability(result.CapabilityReport.Level == AxamlCapabilityLevel.PartiallyEditable && !result.CapabilityReport.DocumentReadOnly, "Unknown attribute must not make the document read-only.");
        RequireCapability(capability.Mode == AxamlElementCapabilityMode.PartiallyEditable && capability.CanEditProperty("Text") && capability.CanEditProperty("Width"), "Unknown attribute blocked supported properties.");
        RequireCapability(capability.Properties.Single(p => p.SourceName == "custom:Something.Unknown").Mode == AxamlPropertyCapabilityMode.Preserved, "Unknown attribute must be explicitly preserved.");
        RequireCapability(result.Diagnostics.Single(d => d.Code == "AXAML_CAPABILITY_REPORT").Severity == AxamlDiagnosticSeverity.Information,
            "Partial support must be informational, not a document safety warning.");
        result.Document.Controls.Single().Text = "New";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == source.Replace("Content=\"Old\"", "Content=\"New\""), "Edit must replace only the Content value.");
    }

    private static void AssertCapabilityOpaqueSiblings(SmokeContext context)
    {
        const string opaque = "<custom:UnknownControl Foo=\"123\" Bar=\"ABC\" />";
        var source = CapabilitySource(CapabilityButton + opaque + CapabilityTextBox);
        var result = new AxamlImportService().Import(source);
        RequireCapability(result.Document.Controls.Count == 2 && CapabilityFor(result, "TextBox1").CanEditProperty("Text"), "Opaque sibling blocked supported controls.");
        RequireCapability(result.CapabilityReport.Elements.Single(e => e.Element.Name == "custom:UnknownControl").Mode == AxamlElementCapabilityMode.Opaque, "Unknown control must not enter editable projection.");
        result.Document.Controls.Single(c => c.Name == "TextBox1").Text = "Changed";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.PatchedText == source.Replace("Text=\"Hello\"", "Text=\"Changed\""), "Opaque sibling changed during patch.");
        result.Document.Controls.RemoveAll(c => c.Name == "Button1");
        patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.PatchedText.Contains(opaque), "Deleting owned control removed opaque sibling.");
    }

    private static void AssertCapabilityOpaqueAncestor(SmokeContext context)
    {
        var result = new AxamlImportService().Import(CapabilitySource("<custom:Container>" + CapabilityButton + "</custom:Container>" + CapabilityTextBox));
        RequireCapability(result.Document.Controls.Count == 1 && result.Document.Controls[0].Name == "TextBox1", "Children of unknown containers must not be flattened into Canvas.");
        var child = CapabilityFor(result, "Button1");
        RequireCapability(child.Mode == AxamlElementCapabilityMode.Opaque && child.Reason.StartsWith("OpaqueAncestor:"), "Opaque parent restriction was not propagated.");
        RequireCapability(!child.CanEditProperty("Text") && !result.CapabilityReport.DocumentReadOnly, "Ancestor restriction must be local, not global.");
        RequireIdentity(result);
    }

    private static void AssertCapabilityStylesAndBindings(SmokeContext context)
    {
        var boundRoot = new AxamlImportService().Import(CapabilitySource(CapabilityButton).Replace("<Window ", "<Window Title=\"{Binding Title}\" "));
        RequireCapability(boundRoot.CapabilityReport.Level == AxamlCapabilityLevel.PartiallyEditable
            && CapabilityFor(boundRoot, "Button1").CanEditProperty("Text"), "Root binding must stay opaque without disabling editable children.");
        const string styles = "<Window.Styles><Style Selector=\"Button\"><Setter Property=\"Opacity\" Value=\"0.5\" /></Style></Window.Styles>";
        var source = CapabilitySource(CapabilityButton.Replace(" />", " Background=\"{Binding Accent}\" Padding=\"1,2,3,4\" />"), styles);
        var result = new AxamlImportService().Import(source);
        var button = CapabilityFor(result, "Button1");
        RequireCapability(button.CanEditProperty("Text") && !button.CanEditProperty("Background") && !button.CanEditProperty("Padding"), "Binding or unsupported literal needs a property-level restriction.");
        RequireIdentity(result);
        result.Document.Controls.Single().Text = "New";
        var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.PatchedText == source.Replace("Content=\"Old\"", "Content=\"New\""), "Styles or binding changed during a supported edit.");
        result.Document.Controls.Single().Background = "Red";
        RequireCapability(!new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source).CanApply, "Patch writer must reject bypassing an opaque property restriction.");
    }

    private static void AssertCapabilityMinimalDragResize(SmokeContext context)
    {
        var source = CapabilitySource("<!-- keep -->" + CapabilityButton + "<custom:Unknown />");
        var result = new AxamlImportService().Import(source);
        var button = result.Document.Controls.Single();
        button.X = 180;
        button.Y = 120;
        var writer = new AxamlPatchWriter();
        var patch = writer.CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.Edits.Count == 2 && patch.PatchedText == source.Replace("Canvas.Left=\"100\"", "Canvas.Left=\"180\"").Replace("Canvas.Top=\"100\"", "Canvas.Top=\"120\""), "Drag changed more than X/Y.");
        button.X = button.Y = 100;
        button.Width = 220;
        button.Height = 60;
        patch = writer.CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.Edits.Count == 2 && patch.PatchedText == source.Replace("Width=\"160\"", "Width=\"220\"").Replace("Height=\"40\"", "Height=\"60\""), "Resize changed more than Width/Height.");
        RequireCapability(writer.CreatePatch(result.RoundTripDocument, result.Document, source + " ").ExternalChangeDetected, "Conflict protection was weakened.");
    }

    private static void AssertCapabilityInspectorAndOperations(SmokeContext context)
    {
        var source = CapabilitySource(CapabilityButton.Replace(" />", " Background=\"{Binding Accent}\" custom:Unknown=\"KEEP\" />"));
        var vm = context.ViewModel;
        vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source), "partial.axaml");
        var button = vm.Controls.Single();
        vm.SelectSingleControl(button);
        var rows = vm.PropertyGridCategories.SelectMany(c => c.Rows).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        RequireCapability(!rows["Text"].IsReadOnly && !rows["Width"].IsReadOnly && rows["Background"].IsReadOnly && rows["Background"].Value == "{Binding Accent}", "Inspector does not follow property capabilities.");
        rows["Text"].Value = "Inspector edit";
        rows["Text"].CommitValue();
        rows["Background"].Value = "Red";
        rows["Background"].CommitValue();
        RequireCapability(button.Text == "Inspector edit" && button.Background != "Red", "Inspector applied an opaque property.");
        RequireCapability(vm.CanMoveAxamlControl(button) && vm.CanResizeAxamlControl(button), "Opaque attribute blocked geometry operations.");
        vm.MoveSelectedControl(10, 10);
        RequireCapability(button.X == 110 && button.Y == 110, "Move operation failed on partial element.");
        RequireCapability(vm.CreateActiveAxamlPatch(source).CanApply, "Inspector/drag changes could not be patched.");

        var bindingSource = CapabilitySource(CapabilityButton.Replace("Canvas.Left=\"100\"", "Canvas.Left=\"{Binding Left}\"").Replace("Width=\"160\"", "Width=\"{Binding Width}\""));
        vm.LoadAxamlImportedDocument(new AxamlImportService().Import(bindingSource), "bound.axaml");
        button = vm.Controls.Single();
        vm.SelectSingleControl(button);
        RequireCapability(!vm.CanMoveAxamlControl(button) && !vm.CanResizeAxamlControl(button) && vm.CanEditAxamlProperty(button, "Text"), "Geometry bindings must block geometry gestures, not text editing.");
        var x = button.X;
        vm.MoveSelectedControl(20, 20);
        RequireCapability(button.X == x && !vm.CreateActiveAxamlPatch(bindingSource).HasChanges, "Move mutated bound geometry.");
    }

    private static void AssertCapabilityNoCanvasSafeIdentity(SmokeContext context)
    {
        foreach (var source in new[] { "<Window><TabControl><Button Content=\"Keep\" /></TabControl></Window>", "<custom:Root xmlns:custom=\"using:X\"><Canvas><Button /></Canvas></custom:Root>" })
        {
            var result = new AxamlImportService().Import(source);
            RequireCapability(result.CapabilityReport.Level == AxamlCapabilityLevel.PartiallyEditable && result.Document.Controls.Count == 0, "Unsupported layout is opaque, not an unsafe document.");
            RequireIdentity(result);
            result.Document.Controls.Add(new DesignerControlFileModel { Id = "new", Type = DesignerControlTypes.Button });
            var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document, source);
            RequireCapability(!patch.CanApply && patch.PatchedText == source, "No Canvas must not fall back to inserting into root.");
        }
    }

    private static void AssertCapabilityUnsafeSourceBlocked(SmokeContext context)
    {
        foreach (var source in new[] { "<Window><Canvas></Window>", CapabilitySource("<Button Width=\"10\" Width=\"20\" />") })
        {
            var result = new AxamlImportService().Import(source);
            RequireCapability(result.CapabilityReport.DocumentReadOnly && !result.CapabilityReport.CanOpen && result.CapabilityReport.ReadOnlyReason.Length > 0,
                "Malformed/ambiguous syntax must have a concrete document-level safety failure.");
            RequireCapability(!new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document).CanApply, "Unsafe source must not be writable.");
        }
    }

    private static void AssertCapabilityRealDocuments(SmokeContext context)
    {
        foreach (var path in new[] { Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"), Path.Combine(FindRepositoryRoot(), "Samples", "VisualStudioPoC", "SimpleAvaloniaApp", "MainWindow.axaml") })
        {
            var result = new AxamlImportService().Import(File.ReadAllText(path), path);
            RequireCapability(result.CapabilityReport.Level == AxamlCapabilityLevel.PartiallyEditable && !result.CapabilityReport.DocumentReadOnly, "Real document was globally made read-only by unsupported syntax.");
            RequireCapability(result.Diagnostics.Any(d => d.Code == "AXAML_DOCUMENT_CAPABILITY") && result.Diagnostics.Any(d => d.Code == "AXAML_ELEMENT_CAPABILITY"), "Granular diagnostics missing.");
            RequireIdentity(result);
            Console.WriteLine(result.Diagnostics.Single(d => d.Code == "AXAML_DOCUMENT_CAPABILITY").Details);
        }
    }

    private static void AssertCapabilityQuoteAndInlineInsertion(SmokeContext context)
    {
        var source = CapabilitySource("<Button x:Name='Button1' Content='Old' custom:Unknown='KEEP' />");
        var result = new AxamlImportService().Import(source);
        result.Document.Controls.Single().Text = "It's new";
        var writer = new AxamlPatchWriter();
        var patch = writer.CreatePatch(result.RoundTripDocument, result.Document, source);
        RequireCapability(patch.CanApply && patch.PatchedText == source.Replace("Content='Old'", "Content='It&apos;s new'"), "Single-quoted source was corrupted.");
        result.Document.Controls.Add(new DesignerControlFileModel { Id = "new", Name = "Button2", Type = DesignerControlTypes.Button });
        patch = writer.CreatePatch(result.RoundTripDocument, result.Document, source);
        var reopened = new AxamlImportService().Import(patch.PatchedText);
        RequireCapability(patch.CanApply && reopened.Document.Controls.Count == 2 && reopened.CapabilityReport.CanSafelyPatch, "New control was inserted outside inline Canvas.");
    }
}
