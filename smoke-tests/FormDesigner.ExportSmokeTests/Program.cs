using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text;

namespace FormDesigner.ExportSmokeTests;

internal static class Program
{
    private const string AvaloniaVersion = "11.3.12";
    private const string AvaloniaDesktopVersion = "11.3.11";

    public static int Main(string[] args)
    {
        var artifactsRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "smoke-tests"));

        Directory.CreateDirectory(artifactsRoot);
        var runRoot = Path.Combine(artifactsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runRoot);
        File.WriteAllText(Path.Combine(artifactsRoot, "latest-run.txt"), runRoot, Encoding.UTF8);
        Console.WriteLine($"Run artifacts: {runRoot}");

        var scenarios = new SmokeScenario[]
        {
            new("SimpleFormExport", ConfigureSimpleFormExport, AssertSimpleFormExport),
            new("RealDataGridExport", ConfigureRealDataGridExport, AssertRealDataGridExport, RequiresRealDataGrid: true),
            new("InteractionsExport", ConfigureInteractionsExport, AssertInteractionsExport, RequiresRealDataGrid: true),
            new("MultiFormOpenFormExport", ConfigureMultiFormOpenFormExport, AssertMultiFormOpenFormExport),
            new("PluginFallbackExport", ConfigurePluginFallbackExport, AssertPluginFallbackExport),
            new("GridLayoutExport", ConfigureGridLayoutExport, AssertGridLayoutExport),
            new("StackPanelLayoutExport", ConfigureStackPanelLayoutExport, AssertStackPanelLayoutExport),
            new("LayoutContainerExport", ConfigureLayoutContainerExport, AssertLayoutContainerExport),
            new("LayoutConversionExport", ConfigureLayoutConversionExport, AssertLayoutConversionExport),
            new("ResponsiveLayoutExport_StackPanel", ConfigureResponsiveStackPanelExport, AssertResponsiveStackPanelExport),
            new("ResponsiveLayoutExport_CanvasFallback", ConfigureResponsiveCanvasFallbackExport, AssertResponsiveCanvasFallbackExport)
        };

        var failed = 0;
        foreach (var scenario in scenarios)
        {
            try
            {
                RunScenario(runRoot, scenario);
                Console.WriteLine($"PASS {scenario.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {scenario.Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"Smoke tests passed: {scenarios.Length}/{scenarios.Length}"
            : $"Smoke tests failed: {failed}/{scenarios.Length}");

        return failed == 0 ? 0 : 1;
    }

    private static void RunScenario(string artifactsRoot, SmokeScenario scenario)
    {
        var projectPath = Path.Combine(artifactsRoot, scenario.Name);
        Directory.CreateDirectory(projectPath);

        var viewModel = CreateViewModel(scenario.Name);
        scenario.Configure(viewModel);
        viewModel.RefreshDiagnostics();
        viewModel.GenerateXaml();

        var context = new SmokeContext(
            Scenario: scenario,
            ViewModel: viewModel,
            ProjectPath: projectPath,
            Xaml: viewModel.GeneratedXaml,
            CSharp: viewModel.GeneratedCSharp,
            GeneratedFiles: viewModel.GeneratedFiles.ToList(),
            ChecklistText: string.Join(Environment.NewLine, viewModel.ExportChecklistItems.Select(item => $"{item.Title}: {item.Value} {item.Details}")),
            DiagnosticsText: string.Join(Environment.NewLine, viewModel.Diagnostics.Select(item => $"{item.Category}: {item.Message} {item.Recommendation}")));

        WriteAvaloniaProject(context);
        scenario.Assert(context);
        DotnetBuild(projectPath);
    }

    private static MainWindowViewModel CreateViewModel(string scenarioName)
    {
        var registry = new DesignerRegistry();
        BuiltInControlRegistrar.Register(registry);
        registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider());

        var viewModel = new MainWindowViewModel(registry)
        {
            ExportProjectNamespace = $"SmokeGenerated.{scenarioName.Replace("-", "").Replace("_", "")}",
            ExportTarget = MainWindowViewModel.ExportTargetMainWindow,
            XamlVerbosity = MainWindowViewModel.XamlVerbosityCompact,
            LayoutExportMode = MainWindowViewModel.LayoutExportModeCanvas,
            DataGridExportMode = MainWindowViewModel.DataGridExportModeVisual,
            IncludeExportComments = true,
            IncludeSampleData = false,
            IncludeCrudSkeleton = false,
            IncludeCommunityToolkitAttributes = false,
            IncludePluginRuntimeReferences = false,
            DesignWidth = 900,
            DesignHeight = 620,
            FormTitle = scenarioName
        };

        viewModel.Controls.Clear();
        viewModel.BindingSources.Clear();
        viewModel.Interactions.Clear();
        return viewModel;
    }

    private static void ConfigureSimpleFormExport(MainWindowViewModel vm)
    {
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "TitleText", 36, 30, 360, 32, text: "Simple export smoke"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "NameTextBox", 36, 90, 260, 40, placeholder: "Enter name"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "EnabledCheckBox", 36, 148, 180, 32, text: "Enabled"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "ContentCard", 340, 86, 260, 110, background: "#F8FAFC", border: "#CBD5E1", radius: 12));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SaveButton", 36, 220, 150, 42, text: "Save", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
    }

    private static void AssertSimpleFormExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Button", "XAML should contain Button.");
        RequireContains(context.Xaml, "<TextBox", "XAML should contain TextBox.");
        RequireContains(context.Xaml, "<CheckBox", "XAML should contain CheckBox.");
        RequireContains(context.Xaml, "<TextBlock", "XAML should contain TextBlock.");
        RequireContains(context.Xaml, "<Border", "XAML should contain Border.");
        RequireNotContains(context.Xaml, "Avalonia.Controls.DataGrid", "Simple export must not require DataGrid package.");
        RequireContains(context.ChecklistText, "Plugins: none", "Simple export checklist should not require plugins.");
    }

    private static void ConfigureRealDataGridExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 42, 720, 360));
    }

    private static void AssertRealDataGridExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "xmlns:dataGrid=\"clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls.DataGrid\"", "Real DataGrid XML namespace missing.");
        RequireContains(context.Xaml, "<dataGrid:DataGrid", "Generated XAML should use real Avalonia DataGrid.");
        RequireContains(context.Xaml, "Title", "Generated XAML should contain BindingSource field Title.");
        RequireContains(context.Xaml, "Price", "Generated XAML should contain BindingSource field Price.");
        RequireContains(context.Xaml, "Count", "Generated XAML should contain BindingSource field Count.");
        RequireContains(context.ChecklistText, "DataGrid: Real Avalonia DataGrid", "Checklist should report real DataGrid mode.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Checklist should mention required DataGrid NuGet.");
        RequireNotContains(context.CSharp, "ObservableCollection", "Clean real DataGrid export should not generate fake demo models.");
        RequireNotContains(context.CSharp, "ProductRow", "Clean real DataGrid export should not generate demo row classes.");
    }

    private static void ConfigureInteractionsExport(MainWindowViewModel vm)
    {
        var source = ProductsSource();
        vm.BindingSources.Add(source);
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        vm.Controls.Add(DataGrid("ProductsGrid", source.Id, 32, 36, 520, 250));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "SelectedTitleTextBox", 590, 44, 240, 38, placeholder: "Selected title"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "DetailsCheckBox", 590, 100, 180, 34, text: "Show details"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "DetailsPanel", 590, 150, 240, 90, background: "#EFF6FF", border: "#BFDBFE", radius: 12));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "MessageButton", 590, 270, 180, 42, text: "Show message", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));

        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "MessageButton",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionShowMessage,
            TextTemplate = "Smoke message",
            MessageTitle = "Smoke"
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxChecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "DetailsCheckBox",
            EventName = InteractionModel.EventCheckBoxUnchecked,
            ActionType = InteractionModel.ActionToggleVisibility,
            TargetControlName = "DetailsPanel",
            TargetProperty = InteractionModel.TargetPropertyIsVisible
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "ProductsGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedTitleTextBox",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Title"
        });
    }

    private static void AssertInteractionsExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "Click=\"MessageButtonClick\"", "Button click handler missing.");
        RequireContains(context.Xaml, "Checked=\"DetailsCheckBox_Checked\"", "CheckBox checked handler missing.");
        RequireContains(context.Xaml, "Unchecked=\"DetailsCheckBox_Unchecked\"", "CheckBox unchecked handler missing.");
        RequireContains(context.Xaml, "SelectionChanged=\"ProductsGrid_SelectionChanged\"", "DataGrid selection handler missing.");
        RequireContains(context.CSharp, "private async void MessageButtonClick", "ShowMessage handler missing.");
        RequireContains(context.CSharp, "private void DetailsCheckBox_Checked", "Checked handler missing.");
        RequireContains(context.CSharp, "private void DetailsCheckBox_Unchecked", "Unchecked handler missing.");
        RequireContains(context.CSharp, "private void ProductsGrid_SelectionChanged", "SelectionChanged handler missing.");
        RequireContains(context.CSharp, "ShowMessageAsync", "ShowMessage helper missing.");
        RequireNotContains(context.CSharp, "ObservableCollection", "Interactions clean export should not generate demo classes.");
    }

    private static void ConfigureMultiFormOpenFormExport(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeVisual;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form1Title", 36, 34, 340, 30, text: "Form1"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 36, 92, 180, 42, text: "Open Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));

        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 36, 34, 340, 30, text: "Form2 window"));

        vm.NewFormEditorCommand?.Execute(null);
        var form3 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form3 document missing.");
        form3.Name = "Form3";
        form3.Document.FormTitle = "Form3";
        vm.FormTitle = "Form3";
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form3TextBox", 36, 84, 260, 40, placeholder: "Form3 text"));

        var form1Tab = vm.DocumentTabs.First(tab => tab.DocumentId == form1.Id);
        vm.SelectedDocumentTab = form1Tab;
        RequireActiveControls(vm, "Form1Title", "OpenForm2Button");
        RequireNoActiveControls(vm, "Form2Title", "Form3TextBox");

        vm.SelectedDocumentTab = vm.DocumentTabs.First(tab => tab.DocumentId == form2.Id);
        RequireActiveControls(vm, "Form2Title");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form3TextBox");

        vm.SelectedDocumentTab = vm.DocumentTabs.First(tab => tab.DocumentId == form3.Id);
        RequireActiveControls(vm, "Form3TextBox");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form2Title");

        vm.SelectedDocumentTab = form1Tab;
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "OpenForm2Button",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionOpenForm,
            TargetFormId = form2.Id,
            TargetFormName = form2.DisplayName,
            OpenMode = InteractionModel.OpenModeShow
        });
    }

    private static void AssertMultiFormOpenFormExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireGeneratedFile(context, "Form3.axaml");
        RequireGeneratedFile(context, "Form3.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "OpenForm Button.Click handler missing in Form1 XAML.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "OpenForm handler should create Form2.");
        RequireContains(context.CSharp, "windowForm2.Show();", "OpenForm handler should show Form2.");
        RequireContains(context.ChecklistText, "Forms exported: 3/3", "Export checklist should report all forms.");
        RequireContains(context.ChecklistText, "OpenForm interactions: 1", "Export checklist should report OpenForm interaction.");
    }

    private static void RequireActiveControls(MainWindowViewModel vm, params string[] names)
    {
        foreach (var name in names)
        {
            if (!vm.Controls.Any(control => string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Expected active form to contain control '{name}'.");
        }
    }

    private static void RequireNoActiveControls(MainWindowViewModel vm, params string[] names)
    {
        foreach (var name in names)
        {
            if (vm.Controls.Any(control => string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Phantom control leaked into active form: '{name}'.");
        }
    }

    private static void ConfigurePluginFallbackExport(MainWindowViewModel vm)
    {
        vm.IncludePluginRuntimeReferences = false;
        vm.Controls.Add(Control("Minimal.HelloCard", "HelloCard1", 42, 42, 280, 118, text: "Hello plugin", pluginId: "Samples.MinimalDesignerPlugin", pluginVersion: "1.0.0"));
    }

    private static void AssertPluginFallbackExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "Plugin control 'Minimal.HelloCard'", "Plugin placeholder comment missing.");
        RequireContains(context.Xaml, "<Border", "Plugin fallback should export a safe Border placeholder.");
        RequireNotContains(context.Xaml, "minimal:HelloCard", "Clean fallback export must not require plugin runtime namespace.");
        RequireContains(context.DiagnosticsText, "placeholder", "Diagnostics should warn about plugin placeholder export.");
        RequireContains(context.ChecklistText, "Plugins: none", "Checklist should report no runtime plugin DLLs in fallback mode.");
    }

    private static void ConfigureGridLayoutExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Grid;
        vm.SurfaceLayoutColumns = 2;
        vm.SurfaceLayoutRows = 2;
        vm.SurfaceGridColumnDefinitions = "Auto,*";
        vm.SurfaceGridRowDefinitions = "Auto,*";
        var title = Control(DesignerControlTypes.TextBlock, "GridTitle", 0, 0, 220, 30, text: "Grid layout");
        title.GridRow = 0;
        title.GridColumn = 0;
        title.GridColumnSpan = 2;
        title.HorizontalAlignment = DesignerLayoutModes.AlignStretch;
        vm.Controls.Add(title);

        var input = Control(DesignerControlTypes.TextBox, "GridInput", 0, 0, 240, 38, placeholder: "Input");
        input.GridRow = 1;
        input.GridColumn = 0;
        input.Margin = "0,12,12,0";
        vm.Controls.Add(input);

        var submit = Control(DesignerControlTypes.Button, "GridSubmit", 0, 0, 140, 40, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        submit.GridRow = 1;
        submit.GridColumn = 1;
        submit.HorizontalAlignment = DesignerLayoutModes.AlignRight;
        submit.Margin = "0,12,0,0";
        vm.Controls.Add(submit);
    }

    private static void AssertGridLayoutExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Grid x:Name=\"RootLayout\"", "Grid layout export should use Avalonia Grid as the root.");
        RequireContains(context.Xaml, "ColumnDefinitions=\"Auto,*\"", "Grid column definitions should be exported.");
        RequireContains(context.Xaml, "RowDefinitions=\"Auto,*\"", "Grid row definitions should be exported.");
        RequireContains(context.Xaml, "Grid.Row=\"1\" Grid.Column=\"1\"", "Child Grid.Row/Grid.Column placement should be exported.");
        RequireNotContains(context.Xaml, "Canvas.Left", "Grid layout children should not export Canvas.Left.");
        RequireNotContains(context.Xaml, "primitives:UniformGrid", "Grid layout export should not use UniformGrid.");
    }

    private static void ConfigureStackPanelLayoutExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Stack;
        vm.SurfaceLayoutOrientation = DesignerLayoutModes.Vertical;
        vm.SurfaceLayoutSpacing = 8;

        var input = Control(DesignerControlTypes.TextBox, "StackInput", 0, 0, 260, 38, placeholder: "Email");
        input.StackOrder = 0;
        vm.Controls.Add(input);

        var submit = Control(DesignerControlTypes.Button, "StackSubmit", 0, 0, 140, 40, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        submit.StackOrder = 1;
        submit.Margin = "0,4,0,0";
        vm.Controls.Add(submit);
    }

    private static void AssertStackPanelLayoutExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<StackPanel x:Name=\"RootLayout\" Orientation=\"Vertical\" Spacing=\"8\"", "StackPanel layout should export root StackPanel with orientation and spacing.");
        RequireContains(context.Xaml, "StackInput", "First StackPanel child should be exported.");
        RequireContains(context.Xaml, "StackSubmit", "Second StackPanel child should be exported.");
        RequireNotContains(context.Xaml, "Canvas.Left", "StackPanel layout children should not export Canvas.Left.");
        RequireNotContains(context.Xaml, "Canvas.Top", "StackPanel layout children should not export Canvas.Top.");
    }

    private static void ConfigureLayoutContainerExport(MainWindowViewModel vm)
    {
        var border = Control(DesignerControlTypes.Border, "DetailsPanel", 40, 40, 360, 160, background: "Transparent", border: "#CBD5E1", radius: 8);
        border.ChildLayoutMode = DesignerLayoutModes.Stack;
        border.LayoutOrientation = DesignerLayoutModes.Vertical;
        border.LayoutSpacing = 6;
        border.Padding = 12;
        vm.Controls.Add(border);

        var title = Control(DesignerControlTypes.TextBlock, "DetailsTitle", 0, 0, 240, 28, text: "Details");
        title.ParentId = border.Id;
        title.StackOrder = 0;
        vm.Controls.Add(title);

        var input = Control(DesignerControlTypes.TextBox, "DetailsInput", 0, 0, 260, 38, placeholder: "Name");
        input.ParentId = border.Id;
        input.StackOrder = 1;
        vm.Controls.Add(input);
    }

    private static void AssertLayoutContainerExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "DetailsPanel", "Layout container should be exported.");
        RequireContains(context.Xaml, "<StackPanel Orientation=\"Vertical\" Spacing=\"6\"", "Border layout container should export inner StackPanel.");
        RequireContains(context.Xaml, "DetailsTitle", "Container child TextBlock should be exported.");
        RequireContains(context.Xaml, "DetailsInput", "Container child TextBox should be exported.");
    }

    private static void ConfigureLayoutConversionExport(MainWindowViewModel vm)
    {
        var first = Control(DesignerControlTypes.TextBox, "ConvertedInput", 40, 40, 260, 38, placeholder: "Converted");
        var second = Control(DesignerControlTypes.Button, "ConvertedButton", 40, 96, 160, 40, text: "Save", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10);
        vm.Controls.Add(first);
        vm.Controls.Add(second);
        vm.SelectControls(new[] { first, second }, second);
        vm.ConvertSelectionToStackPanelEditorCommand?.Execute(null);
    }

    private static void AssertLayoutConversionExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "StackPanel", "Converted selection should export as a StackPanel container.");
        RequireContains(context.Xaml, "ConvertedInput", "Converted TextBox should be preserved.");
        RequireContains(context.Xaml, "ConvertedButton", "Converted Button should be preserved.");
        RequireNotContains(context.DiagnosticsText, "outside the parent grid", "Conversion should not create invalid Grid placement.");
    }

    private static void ConfigureResponsiveStackPanelExport(MainWindowViewModel vm)
    {
        vm.LayoutExportMode = MainWindowViewModel.LayoutExportModeResponsive;
        vm.SurfaceLayoutSpacing = 14;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "HeaderText", 32, 32, 360, 30, text: "Responsive form"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "EmailTextBox", 32, 84, 320, 38, placeholder: "Email"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "SubmitButton", 32, 142, 160, 42, text: "Submit", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
    }

    private static void AssertResponsiveStackPanelExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<StackPanel", "Responsive simple form should export a StackPanel.");
        RequireContains(context.ChecklistText, "Layout export mode: Responsive StackPanel", "Checklist should report Responsive StackPanel.");
    }

    private static void ConfigureResponsiveCanvasFallbackExport(MainWindowViewModel vm)
    {
        vm.LayoutExportMode = MainWindowViewModel.LayoutExportModeResponsive;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "FirstText", 32, 32, 220, 80, text: "First"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "OverlappingTextBox", 42, 70, 260, 38, placeholder: "Overlap"));
    }

    private static void AssertResponsiveCanvasFallbackExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "Overlapping responsive form should fallback to Canvas.");
        RequireContains(context.ChecklistText, "Layout export mode: Canvas fallback", "Checklist should report Canvas fallback.");
        RequireContains(context.ChecklistText, "пересекаются", "Checklist should explain overlap fallback.");
    }

    private static BindingSourceModel ProductsSource()
    {
        var source = new BindingSourceModel
        {
            Id = "products-source",
            Name = "ProductsSource",
            Path = "Products",
            ItemTypeName = "ProductRow",
            Description = "Smoke test products source."
        };
        source.Fields.Add(Field("Title", "Title", "Keyboard", "string", "2*"));
        source.Fields.Add(Field("Price", "Price", "149.90", "decimal", "*"));
        source.Fields.Add(Field("Count", "Count", "3", "int", "*"));
        return source;
    }

    private static BindingFieldModel Field(string header, string path, string sample, string typeName, string width)
    {
        return new BindingFieldModel
        {
            Header = header,
            Path = path,
            SampleValue = sample,
            TypeName = typeName,
            Width = width,
            IsVisible = true,
            AllowSort = true,
            AllowFilter = true
        };
    }

    private static DesignControlModel DataGrid(string name, string bindingSourceId, double x, double y, double width, double height)
    {
        return new DesignControlModel
        {
            Type = DesignerControlTypes.DataGrid,
            DescriptorId = DesignerControlTypes.DataGrid,
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            BindingSourceId = bindingSourceId,
            Text = "DataGrid",
            Background = "#FFFFFF",
            BorderBrush = "#CBD5E1",
            BorderThickness = 1,
            AutoGenerateColumns = false,
            DataGridShowHeader = true,
            DataGridShowRowLines = true,
            DataGridShowColumnLines = true,
            DataGridShowAlternatingRows = true,
            ShowFilterRow = false,
            ShowGroupPanel = false,
            ShowFooter = false
        };
    }

    private static DesignControlModel Control(
        string type,
        string name,
        double x,
        double y,
        double width,
        double height,
        string text = "",
        string placeholder = "",
        string background = "#FFFFFF",
        string foreground = "#0F172A",
        string border = "#94A3B8",
        double radius = 6,
        string pluginId = "",
        string pluginVersion = "")
    {
        return new DesignControlModel
        {
            Type = type,
            DescriptorId = type,
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Text = text,
            PlaceholderText = placeholder,
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            CornerRadius = radius,
            PluginId = pluginId,
            PluginVersion = pluginVersion
        };
    }

    private static void WriteAvaloniaProject(SmokeContext context)
    {
        Directory.CreateDirectory(context.ProjectPath);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.xaml.txt"), context.Xaml, Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.cs.txt"), context.CSharp, Encoding.UTF8);
        foreach (var generatedFile in context.GeneratedFiles.Where(file =>
                     file.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                     || file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var targetPath = Path.Combine(context.ProjectPath, generatedFile.Path.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(targetPath, generatedFile.Content, Encoding.UTF8);
        }

        if (!File.Exists(Path.Combine(context.ProjectPath, "MainWindow.axaml")))
            File.WriteAllText(Path.Combine(context.ProjectPath, "MainWindow.axaml"), context.Xaml, Encoding.UTF8);
        if (!File.Exists(Path.Combine(context.ProjectPath, "MainWindow.axaml.cs")))
            File.WriteAllText(Path.Combine(context.ProjectPath, "MainWindow.axaml.cs"), context.CSharp, Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "App.axaml"), BuildAppXaml(context.ViewModel.ExportProjectNamespace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "App.axaml.cs"), BuildAppCode(context.ViewModel.ExportProjectNamespace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "Program.cs"), BuildProgramCode(context.ViewModel.ExportProjectNamespace), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, $"{context.Scenario.Name}.csproj"), BuildProjectFile(context.Scenario), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "smoke-summary.txt"), BuildSummary(context), Encoding.UTF8);
    }

    private static string BuildProjectFile(SmokeScenario scenario)
    {
        var dataGridPackage = scenario.RequiresRealDataGrid
            ? $"""
                <PackageReference Include="Avalonia.Controls.DataGrid" Version="{AvaloniaVersion}" />
            """
            : "";

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="{AvaloniaVersion}" />
    <PackageReference Include="Avalonia.Desktop" Version="{AvaloniaDesktopVersion}" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="{AvaloniaVersion}" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="{AvaloniaDesktopVersion}" />
{dataGridPackage}
  </ItemGroup>
</Project>
""";
    }

    private static string BuildAppXaml(string ns)
    {
        return $"""
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="{ns}.App"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
""";
    }

    private static string BuildAppCode(string ns)
    {
        return $$"""
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace {{ns}};

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
""";
    }

    private static string BuildProgramCode(string ns)
    {
        return $$"""
using Avalonia;
using System;

namespace {{ns}};

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
""";
    }

    private static string BuildSummary(SmokeContext context)
    {
        return $"""
Scenario: {context.Scenario.Name}
Project: {context.ProjectPath}

Checklist:
{context.ChecklistText}

Diagnostics:
{context.DiagnosticsText}
""";
    }

    private static void DotnetBuild(string projectPath)
    {
        var projectFile = Directory.GetFiles(projectPath, "*.csproj").Single();
        var result = RunProcess("dotnet", $"build \"{projectFile}\"", projectPath);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet build failed.{Environment.NewLine}{result.Output}");
    }

    private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
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
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.ToString());
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string unexpected, string message)
    {
        if (text.Contains(unexpected, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void RequireGeneratedFile(SmokeContext context, string path)
    {
        if (!context.GeneratedFiles.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Generated file missing: {path}");
    }

    private static void SafeCleanDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            return;

        if (!fullPath.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}smoke-tests", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clean unexpected directory: {fullPath}");

        foreach (var childDirectory in directory.GetDirectories())
            DeleteWithRetry(() => childDirectory.Delete(recursive: true), childDirectory.FullName);

        foreach (var file in directory.GetFiles())
            DeleteWithRetry(file.Delete, file.FullName);
    }

    private static void DeleteWithRetry(Action delete, string path)
    {
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(250 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(250 * attempt);
            }
        }

        throw new IOException($"Could not clean smoke-test artifact path: {path}");
    }

    private sealed record SmokeScenario(
        string Name,
        Action<MainWindowViewModel> Configure,
        Action<SmokeContext> Assert,
        bool RequiresRealDataGrid = false);

    private sealed record SmokeContext(
        SmokeScenario Scenario,
        MainWindowViewModel ViewModel,
        string ProjectPath,
        string Xaml,
        string CSharp,
        IReadOnlyList<GeneratedFileModel> GeneratedFiles,
        string ChecklistText,
        string DiagnosticsText);

    private sealed record ProcessResult(int ExitCode, string Output);
}
