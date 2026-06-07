using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static class Program
{
    private const string AvaloniaVersion = "11.1.1";
    private const string AvaloniaDesktopVersion = "11.1.1";
    private const int SmokeRunsToKeep = 5;

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
            new("CanvasBorderBackgroundZOrderExport", ConfigureCanvasBorderBackgroundZOrderExport, AssertCanvasBorderBackgroundZOrderExport),
            new("RealDataGridExport", ConfigureRealDataGridExport, AssertRealDataGridExport, RequiresRealDataGrid: true),
            new("InteractionsExport", ConfigureInteractionsExport, AssertInteractionsExport, RequiresRealDataGrid: true),
            new("MultiFormOpenFormExport", ConfigureMultiFormOpenFormExport, AssertMultiFormOpenFormExport),
            new("MultiFormDocumentStateIsolation", ConfigureMultiFormDocumentStateIsolation, AssertMultiFormDocumentStateIsolation),
            new("MultiFormToolboxDropPropertyEdit", ConfigureMultiFormToolboxDropPropertyEdit, AssertMultiFormToolboxDropPropertyEdit, RequiresRealDataGrid: true),
            new("MultiFormSameControlNamesPropertyGridEdit", ConfigureMultiFormSameControlNamesPropertyGridEdit, AssertMultiFormSameControlNamesPropertyGridEdit, RequiresRealDataGrid: true),
            new("AddFormSimpleIsolation", ConfigureAddFormSimpleIsolation, AssertAddFormSimpleIsolation),
            new("SwitchFormsClearsInspector", ConfigureSwitchFormsClearsInspector, AssertSwitchFormsClearsInspector),
            new("PropertyGridTextEditUsesLocalBuffer", ConfigurePropertyGridTextEditUsesLocalBuffer, AssertPropertyGridTextEditUsesLocalBuffer),
            new("EditAfterAddFormWorks", ConfigureEditAfterAddFormWorks, AssertEditAfterAddFormWorks),
            new("EditTextAfterAddFormStillWorks", ConfigureEditTextAfterAddFormStillWorks, AssertEditTextAfterAddFormStillWorks),
            new("ChangeForegroundAfterAddFormWorks", ConfigureChangeForegroundAfterAddFormWorks, AssertChangeForegroundAfterAddFormWorks),
            new("ChangeBackgroundAfterAddFormWorks", ConfigureChangeBackgroundAfterAddFormWorks, AssertChangeBackgroundAfterAddFormWorks),
            new("ForegroundPersistsInModel", ConfigureForegroundPersistsInModel, AssertForegroundPersistsInModel),
            new("ForegroundAppearsInExportedXaml", ConfigureForegroundAppearsInExportedXaml, AssertForegroundAppearsInExportedXaml),
            new("SameSelectionDoesNotRebuildPropertyGrid", ConfigureSameSelectionDoesNotRebuildPropertyGrid, AssertSameSelectionDoesNotRebuildPropertyGrid),
            new("PropertiesPanelVisibleAfterAddFormAndSelectControl", ConfigurePropertiesPanelVisibleAfterAddFormAndSelectControl, AssertPropertiesPanelVisibleAfterAddFormAndSelectControl),
            new("InspectorShowsActiveFormPropertiesAfterSwitch", ConfigureInspectorShowsActiveFormPropertiesAfterSwitch, AssertInspectorShowsActiveFormPropertiesAfterSwitch),
            new("RenameFormDoesNotDuplicatePropertyRows", ConfigureRenameFormDoesNotDuplicatePropertyRows, AssertRenameFormDoesNotDuplicatePropertyRows),
            new("WidthHeightEditDoesNotLockInspector", ConfigureWidthHeightEditDoesNotLockInspector, AssertWidthHeightEditDoesNotLockInspector),
            new("EmptyButtonTextIsPreserved", ConfigureEmptyButtonTextIsPreserved, AssertEmptyButtonTextIsPreserved),
            new("PropertyEditBlockedForStaleInspectorTarget", ConfigurePropertyEditBlockedForStaleInspectorTarget, AssertPropertyEditBlockedForStaleInspectorTarget),
            new("AddFormRegressionStillWorks", ConfigureAddFormRegressionStillWorks, AssertAddFormRegressionStillWorks),
            new("NewProjectClearsPreviousFormsAndState", ConfigureNewProjectClearsPreviousFormsAndState, AssertNewProjectClearsPreviousFormsAndState),
            new("NewProjectAfterSavedMultiFormProjectClearsAllOldForms", ConfigureNewProjectAfterSavedMultiFormProjectClearsAllOldForms, AssertNewProjectAfterSavedMultiFormProjectClearsAllOldForms),
            new("NewProjectDoesNotReuseOldFormNamesOrIds", ConfigureNewProjectDoesNotReuseOldFormNamesOrIds, AssertNewProjectDoesNotReuseOldFormNamesOrIds),
            new("NewProjectReleasesOldFormsAndControls", ConfigureNewProjectReleasesOldFormsAndControls, AssertNewProjectReleasesOldFormsAndControls),
            new("NewProjectClearsDataDllExportCaches", ConfigureNewProjectClearsDataDllExportCaches, AssertNewProjectClearsDataDllExportCaches),
            new("PropertyEditDoesNotTriggerExportPipeline", ConfigurePropertyEditDoesNotTriggerExportPipeline, AssertPropertyEditDoesNotTriggerExportPipeline),
            new("ResizeDoesNotRebuildPropertyGridOnEveryMove", ConfigureResizeDoesNotRebuildPropertyGridOnEveryMove, AssertResizeDoesNotRebuildPropertyGridOnEveryMove),
            new("DragAfterAddFormWorks", ConfigureDragAfterAddFormWorks, AssertDragAfterAddFormWorks),
            new("AddEmptySecondFormDoesNotBreakFirstFormPropertyGrid", ConfigureAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid, AssertAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid, RequiresRealDataGrid: true),
            new("AddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic", ConfigureAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic, AssertAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic, RequiresRealDataGrid: true),
            new("AlphaEndToEndProjectExport", ConfigureAlphaEndToEndProjectExport, AssertAlphaEndToEndProjectExport, RequiresRealDataGrid: true),
            new("DataGridBindingSourceWorkflow", ConfigureDataGridBindingSourceWorkflow, AssertDataGridBindingSourceWorkflow, RequiresRealDataGrid: true),
            new("OpenFormPreviewAndExport", ConfigureOpenFormPreviewAndExport, AssertOpenFormPreviewAndExport),
            new("PropertyGridDataGridSetup", ConfigurePropertyGridDataGridSetup, AssertPropertyGridDataGridSetup, RequiresRealDataGrid: true),
            new("SaveLoadMultiFormProject", ConfigureSaveLoadMultiFormProject, AssertSaveLoadMultiFormProject, RequiresRealDataGrid: true),
            new("ExportToProjectBuildValidation", ConfigureExportToProjectBuildValidation, AssertExportToProjectBuildValidation, RequiresRealDataGrid: true),
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

        PruneSmokeRuns(artifactsRoot, runRoot);
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

    private static void ConfigureCanvasBorderBackgroundZOrderExport(MainWindowViewModel vm)
    {
        vm.SurfaceLayoutMode = DesignerLayoutModes.Absolute;
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "CardTitle", 54, 46, 220, 30, text: "Customer"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "CustomerNameTextBox", 54, 92, 260, 38, placeholder: "Name"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "BackgroundPanel", 24, 28, 520, 180, background: "#EFF6FF", border: "#93C5FD", radius: 16));
    }

    private static void AssertCanvasBorderBackgroundZOrderExport(SmokeContext context)
    {
        var borderIndex = context.Xaml.IndexOf("x:Name=\"BackgroundPanel\"", StringComparison.Ordinal);
        var textBlockIndex = context.Xaml.IndexOf("x:Name=\"CardTitle\"", StringComparison.Ordinal);
        var textBoxIndex = context.Xaml.IndexOf("x:Name=\"CustomerNameTextBox\"", StringComparison.Ordinal);
        if (borderIndex < 0 || textBlockIndex < 0 || textBoxIndex < 0)
            throw new InvalidOperationException("Z-order smoke XAML should contain Border, TextBlock and TextBox.");

        if (borderIndex > textBlockIndex || borderIndex > textBoxIndex)
            throw new InvalidOperationException("Background Border should be exported before foreground text controls.");

        RequireContains(context.Xaml, "x:Name=\"BackgroundPanel\" Width=\"520\" Height=\"180\" Canvas.Left=\"24\" Canvas.Top=\"28\" ZIndex=\"0\"", "Background Border should export ZIndex=0.");
        RequireContains(context.Xaml, "x:Name=\"CardTitle\" Text=\"Customer\" Width=\"220\" Height=\"30\" Canvas.Left=\"54\" Canvas.Top=\"46\" ZIndex=\"1\"", "TextBlock should export above background Border.");
        RequireContains(context.Xaml, "x:Name=\"CustomerNameTextBox\"", "TextBox should be exported.");
        if (context.Xaml.Contains("<Canvas>\r\n      </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\n      </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\r\n        </Canvas>", StringComparison.Ordinal)
            || context.Xaml.Contains("<Canvas>\n        </Canvas>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Export should not generate an empty inner Canvas for a background Border.");
        }
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

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Title", "OpenForm2Button");
        RequireNoActiveControls(vm, "Form2Title", "Form3TextBox");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2Title");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form3TextBox");

        SwitchToForm(vm, form3.Id);
        RequireActiveControls(vm, "Form3TextBox");
        RequireNoActiveControls(vm, "Form1Title", "OpenForm2Button", "Form2Title");

        SwitchToForm(vm, form1.Id);
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

    private static void ConfigureMultiFormDocumentStateIsolation(MainWindowViewModel vm)
    {
        vm.Controls.Add(Control(DesignerControlTypes.Button, "Form1Button", 24, 42, 160, 38, text: "Form1 original"));
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form2TextBox", 24, 48, 240, 36, placeholder: "Form2 original"));

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button");
        RequireNoActiveControls(vm, "Form2TextBox");
        var form1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        form1Button.Text = "Form1 edited";
        form1Button.Width = 210;

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2TextBox");
        RequireNoActiveControls(vm, "Form1Button");
        var form2TextBox = vm.Controls.Single(control => control.Name == "Form2TextBox");
        form2TextBox.PlaceholderText = "Form2 edited";
        form2TextBox.Width = 280;

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button");
        RequireNoActiveControls(vm, "Form2TextBox");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        if (restoredForm1Button.Text != "Form1 edited" || Math.Abs(restoredForm1Button.Width - 210) > 0.001)
            throw new InvalidOperationException("Form1 edited properties were not preserved after switching documents.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2TextBox");
        RequireNoActiveControls(vm, "Form1Button");
        var restoredForm2TextBox = vm.Controls.Single(control => control.Name == "Form2TextBox");
        if (restoredForm2TextBox.PlaceholderText != "Form2 edited" || Math.Abs(restoredForm2TextBox.Width - 280) > 0.001)
            throw new InvalidOperationException("Form2 edited properties were not preserved after switching documents.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormDocumentStateIsolation(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Form1 edited", "Form1 exported state should include edited button text.");
        RequireNotContains(context.Xaml, "Form2 edited", "Active Form1 export should not contain Form2 control state.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Form2 edited", "Form2 exported state should include edited placeholder.");
        RequireNotContains(form2File.Content, "Form1 edited", "Form2 export should not contain Form1 control state.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Valid multi-form switching should not produce isolation diagnostics.");
    }

    private static void ConfigureMultiFormToolboxDropPropertyEdit(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created through toolbox drop path.");
        var form1Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 40, 100, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created through toolbox drop path.");
        var form1Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created through toolbox drop path.");

        form1Button.Name = "Form1Button";
        form1Border.Name = "Form1Border";
        form1Grid.Name = "Form1Grid";

        vm.SelectSingleControl(form1Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Form1 inspector text");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "188");
        RequirePropertyGridContext(vm, form1.Id, form1Button.Id);

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";

        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 54, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Button was not created through toolbox drop path.");
        var form2Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 60, 116, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Border was not created through toolbox drop path.");
        var form2Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 270, 54, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 DataGrid was not created through toolbox drop path.");

        form2Button.Name = "Form2Button";
        form2Border.Name = "Form2Border";
        form2Grid.Name = "Form2Grid";

        var rejected = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 10, 10, null, false, form1.Id);
        if (rejected is not null)
            throw new InvalidOperationException("Toolbox drop should be rejected when drag source document differs from active form.");

        vm.SelectSingleControl(form2Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Form2 inspector text");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "204");
        RequirePropertyGridContext(vm, form2.Id, form2Button.Id);

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Form1Button", "Form1Border", "Form1Grid");
        RequireNoActiveControls(vm, "Form2Button", "Form2Border", "Form2Grid");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Form1Button");
        if (restoredForm1Button.Text != "Form1 inspector text" || Math.Abs(restoredForm1Button.Width - 188) > 0.001)
            throw new InvalidOperationException("Form1 PropertyGrid edits were not preserved after creating/editing Form2.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Form2Button", "Form2Border", "Form2Grid");
        RequireNoActiveControls(vm, "Form1Button", "Form1Border", "Form1Grid");
        var restoredForm2Button = vm.Controls.Single(control => control.Name == "Form2Button");
        if (restoredForm2Button.Text != "Form2 inspector text" || Math.Abs(restoredForm2Button.Width - 204) > 0.001)
            throw new InvalidOperationException("Form2 PropertyGrid edits were not preserved after switching documents.");

        var form1Ids = form1.Document.Controls.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var form2Ids = form2.Document.Controls.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (form1Ids.Overlaps(form2Ids))
            throw new InvalidOperationException("Form documents share control ids after toolbox drop workflow.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormToolboxDropPropertyEdit(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Form1 inspector text", "Form1 edited Button text should be exported.");
        RequireNotContains(context.Xaml, "Form2 inspector text", "Active Form1 export should not contain Form2 Button text.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Form2 inspector text", "Form2 edited Button text should be exported.");
        RequireNotContains(form2File.Content, "Form1 inspector text", "Form2 export should not contain Form1 Button text.");
    }

    private static void ConfigureMultiFormSameControlNamesPropertyGridEdit(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 24, 36, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button1 was not created.");
        var form1Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 24, 96, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border1 was not created.");
        var form1Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 36, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid1 was not created.");

        RequireSameDefaultNames(form1Button, form1Border, form1Grid);

        vm.SelectSingleControl(form1Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button Form1");
        RequirePropertyGridContext(vm, form1.Id, form1Button.Id);

        vm.SelectSingleControl(form1Border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "300");
        RequirePropertyGridContext(vm, form1.Id, form1Border.Id);

        vm.SelectSingleControl(form1Grid);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "400");
        RequirePropertyGridContext(vm, form1.Id, form1Grid.Id);

        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";

        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 24, 36, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Button1 was not created.");
        var form2Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 24, 96, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 Border1 was not created.");
        var form2Grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 36, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 DataGrid1 was not created.");

        RequireSameDefaultNames(form2Button, form2Border, form2Grid);

        vm.SelectSingleControl(form2Button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button Form2");
        RequirePropertyGridContext(vm, form2.Id, form2Button.Id);

        vm.SelectSingleControl(form2Border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "500");
        RequirePropertyGridContext(vm, form2.Id, form2Border.Id);

        vm.SelectSingleControl(form2Grid);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "600");
        RequirePropertyGridContext(vm, form2.Id, form2Grid.Id);

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredForm1Button = vm.Controls.Single(control => control.Name == "Button1");
        var restoredForm1Border = vm.Controls.Single(control => control.Name == "Border1");
        var restoredForm1Grid = vm.Controls.Single(control => control.Name == "DataGrid1");
        if (restoredForm1Button.Text != "Button Form1")
            throw new InvalidOperationException("Form1.Button1 text was overwritten by Form2.Button1.");
        if (Math.Abs(restoredForm1Border.Width - 300) > 0.001)
            throw new InvalidOperationException("Form1.Border1 width was overwritten by Form2.Border1.");
        if (Math.Abs(restoredForm1Grid.Width - 400) > 0.001)
            throw new InvalidOperationException("Form1.DataGrid1 width was overwritten by Form2.DataGrid1.");

        SwitchToForm(vm, form2.Id);
        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredForm2Button = vm.Controls.Single(control => control.Name == "Button1");
        var restoredForm2Border = vm.Controls.Single(control => control.Name == "Border1");
        var restoredForm2Grid = vm.Controls.Single(control => control.Name == "DataGrid1");
        if (restoredForm2Button.Text != "Button Form2")
            throw new InvalidOperationException("Form2.Button1 text was overwritten by Form1.Button1.");
        if (Math.Abs(restoredForm2Border.Width - 500) > 0.001)
            throw new InvalidOperationException("Form2.Border1 width was overwritten by Form1.Border1.");
        if (Math.Abs(restoredForm2Grid.Width - 600) > 0.001)
            throw new InvalidOperationException("Form2.DataGrid1 width was overwritten by Form1.DataGrid1.");

        if (ReferenceEquals(restoredForm1Button, restoredForm2Button)
            || ReferenceEquals(restoredForm1Border, restoredForm2Border)
            || ReferenceEquals(restoredForm1Grid, restoredForm2Grid))
            throw new InvalidOperationException("Forms share runtime control instances.");

        SwitchToForm(vm, form1.Id);
    }

    private static void AssertMultiFormSameControlNamesPropertyGridEdit(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Button Form1", "Form1.Button1 edit should be exported from active form.");
        RequireNotContains(context.Xaml, "Button Form2", "Form1 export should not contain Form2.Button1 edit.");
        var form2File = context.GeneratedFiles.First(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        RequireContains(form2File.Content, "Button Form2", "Form2.Button1 edit should be exported.");
        RequireNotContains(form2File.Content, "Button Form1", "Form2 export should not contain Form1.Button1 edit.");
        RequireNotContains(context.DiagnosticsText, "дублирующееся имя control", "Same names across forms should be allowed.");
    }

    private static void RequireSameDefaultNames(DesignControlModel button, DesignControlModel border, DesignControlModel grid)
    {
        if (button.Name != "Button1" || border.Name != "Border1" || grid.Name != "DataGrid1")
            throw new InvalidOperationException($"Expected default per-form names Button1/Border1/DataGrid1, got {button.Name}/{border.Name}/{grid.Name}.");
    }

    private static void ConfigureAddFormSimpleIsolation(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "111";
        vm.SelectSingleControl(button);
        vm.MoveSelectedControl(12, 8);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (string.Equals(form2.Id, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AddForm did not activate a new document.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 must be empty.");
        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("AddForm must clear selected control.");
        RequirePropertyGridContext(vm, form2.Id, "");

        vm.SetActiveForm(form1.Id, "AddFormSimpleIsolation");
        if (!string.Equals(vm.ActiveDocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SetActiveForm did not return to Form1.");
        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("Switching forms must not restore stale selection.");

        var restored = vm.Controls.Single(control => control.Id == button.Id);
        if (!string.Equals(restored.Text, "111", StringComparison.Ordinal))
            throw new InvalidOperationException("Form1 Button text was lost after AddForm.");
        if (Math.Abs(restored.X - 52) > 0.001 || Math.Abs(restored.Y - 48) > 0.001)
            throw new InvalidOperationException("Form1 Button position was lost after AddForm.");
    }

    private static void AssertAddFormSimpleIsolation(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"111\"", "Form1 Button text should survive AddForm.");
        RequireContains(context.Xaml, "Canvas.Left=\"52\"", "Form1 Button X should survive AddForm.");
    }

    private static void ConfigureSwitchFormsClearsInspector(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        RequirePropertyGridContext(vm, form1.Id, button.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        RequirePropertyGridContext(vm, form2.Id, "");
        if (vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => !string.IsNullOrWhiteSpace(row.ContextControlId)))
            throw new InvalidOperationException("Form-level inspector should not keep control rows after AddForm.");

        vm.SetActiveForm(form1.Id, "SwitchFormsClearsInspector");
        RequirePropertyGridContext(vm, form1.Id, "");
        if (vm.SelectedControl is not null || vm.SelectedInteraction is not null)
            throw new InvalidOperationException("Switching forms must clear selected control and logic selection.");
    }

    private static void AssertSwitchFormsClearsInspector(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireNotContains(context.DiagnosticsText, "Stale PropertyGrid", "SwitchFormsClearsInspector should not produce stale PropertyGrid diagnostics.");
    }

    private static void ConfigurePropertyGridTextEditUsesLocalBuffer(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "Initial";
        vm.SelectSingleControl(button);

        var row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text));
        vm.BeginPropertyGridTextEdit(row, row.Value);
        vm.UpdatePropertyGridTextEdit(row, "1");
        vm.UpdatePropertyGridTextEdit(row, "12");
        vm.UpdatePropertyGridTextEdit(row, "123456");

        if (!string.Equals(row.Value, "Initial", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid row.Value changed during live typing before commit.");
        if (!string.Equals(button.Text, "Initial", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text changed during live typing before commit.");

        vm.CommitPropertyGridTextEdit(row, "123456");
        if (!string.Equals(button.Text, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text was not committed after local-buffer edit.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        button = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(button);
        row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text));
        vm.BeginPropertyGridTextEdit(row, row.Value);
        vm.UpdatePropertyGridTextEdit(row, "222222");

        if (!string.Equals(row.Value, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid row.Value changed during live typing after AddForm.");
        if (!string.Equals(button.Text, "123456", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text changed during live typing after AddForm.");

        vm.CommitPropertyGridTextEdit(row, "222222");
        if (!string.Equals(button.Text, "222222", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text was not committed after AddForm local-buffer edit.");
    }

    private static void AssertPropertyGridTextEditUsesLocalBuffer(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222222\"", "PropertyGrid local-buffer edit should commit final Button text.");
    }

    private static void ConfigureEditAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Text = "111";

        vm.AddFormEditorCommand?.Execute(null);
        vm.SetActiveForm(form1.Id, "EditAfterAddFormWorks");
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "222");

        if (!string.Equals(restored.Text, "222", StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyGrid edit after AddForm did not apply.");

        vm.SetActiveForm(vm.CurrentProject.Forms.Single(form => !string.Equals(form.Id, form1.Id, StringComparison.OrdinalIgnoreCase)).Id, "EditAfterAddFormWorks verify Form2");
        if (vm.Controls.Any())
            throw new InvalidOperationException("Form2 should stay empty after editing Form1.");
        vm.SetActiveForm(form1.Id, "EditAfterAddFormWorks export Form1");
    }

    private static void AssertEditAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222\"", "Edited Form1 Button text should be exported.");
        var form2File = context.GeneratedFiles.FirstOrDefault(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        if (form2File is not null)
            RequireNotContains(form2File.Content, "222", "Form2 should not contain Form1 edited Button text.");
    }

    private static void ConfigureEditTextAfterAddFormStillWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "222222");

        if (!string.Equals(restored.Text, "222222", StringComparison.Ordinal))
            throw new InvalidOperationException("Text edit after AddForm was not preserved in model.");
    }

    private static void AssertEditTextAfterAddFormStillWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"222222\"", "Text edit after AddForm should be exported.");
    }

    private static void ConfigureChangeForegroundAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Foreground), "#00AA44");

        if (!string.Equals(restored.Foreground, "#00AA44", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Foreground after AddForm was not applied. Got {restored.Foreground}.");
    }

    private static void AssertChangeForegroundAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#00AA44\"", "Foreground after AddForm should be exported.");
    }

    private static void ConfigureChangeBackgroundAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Background), "#CC99EB");

        if (!string.Equals(restored.Background, "#CC99EB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Background after AddForm was not applied. Got {restored.Background}.");
    }

    private static void AssertChangeBackgroundAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Background=\"#CC99EB\"", "Background after AddForm should be exported.");
    }

    private static void ConfigureForegroundPersistsInModel(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Foreground), "#123456");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);

        if (!string.Equals(restored.Foreground, "#123456", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Foreground was lost after switching forms. Got {restored.Foreground}.");
    }

    private static void AssertForegroundPersistsInModel(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#123456\"", "Persisted Foreground should be exported.");
    }

    private static void ConfigureForegroundAppearsInExportedXaml(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        button.Foreground = "#AA2244";
        vm.SelectSingleControl(button);
    }

    private static void AssertForegroundAppearsInExportedXaml(SmokeContext context)
    {
        RequireContains(context.Xaml, "Foreground=\"#AA2244\"", "Button Foreground should appear in exported XAML.");
    }

    private static void ConfigureSameSelectionDoesNotRebuildPropertyGrid(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        var sameIdentity = new DesignControlModel
        {
            Id = button.Id,
            Name = button.Name,
            Type = button.Type
        };
        vm.SelectedControl = sameIdentity;

        if (!vm.InteractionTraceEntries.Any(entry => entry.EventName == "SELECTED_CONTROL_SAME_SUPPRESSED"))
            throw new InvalidOperationException("Same selection was not suppressed.");

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "RebuildPropertyGrid"
            && entry.Details.Contains("SelectedControlChanged", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Same selection still triggered PropertyGrid rebuild.");
        }
    }

    private static void AssertSameSelectionDoesNotRebuildPropertyGrid(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Same-selection scenario should keep the selected Button exportable.");
    }

    private static void ConfigurePropertiesPanelVisibleAfterAddFormAndSelectControl(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 90, 70, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);

        if (vm.SelectedControl is null)
            throw new InvalidOperationException("Button selection was not restored before Properties check.");

        if (vm.RightInspectorSelectedIndex != 0)
            throw new InvalidOperationException($"Properties tab is not active. Active index: {vm.RightInspectorSelectedIndex}.");

        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Name));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Text));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Width));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Height));

        if (vm.PropertiesTabRefreshVersion <= 0)
            throw new InvalidOperationException("Properties tab refresh version was not raised after selecting the Button.");

        vm.InteractionTraceEntries.Clear();
        vm.GenerateXaml();

        if (vm.SelectedControl is null || !string.Equals(vm.SelectedControl.Id, button.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export refresh cleared the selected Button.");

        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Name));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Text));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Width));
        RequirePropertyGridRow(vm, nameof(DesignControlModel.Height));

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "ApplyDocument"
            || entry.EventName == "APPLY_DOCUMENT_START"
            || entry.EventName == "APPLY_DOCUMENT_END"))
        {
            var applyTrace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName.Contains("APPLY_DOCUMENT", StringComparison.OrdinalIgnoreCase))
                .Select(entry => $"{entry.EventName}:{entry.Details}"));
            throw new InvalidOperationException($"Export refresh still applied editor documents: {applyTrace}");
        }
    }

    private static void AssertPropertiesPanelVisibleAfterAddFormAndSelectControl(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Properties visibility scenario should keep Button exportable.");
    }

    private static void ConfigureInspectorShowsActiveFormPropertiesAfterSwitch(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        EditPropertyGridTextLikeUi(vm, "FormTitle", "Form_A");
        RequirePropertyGridContext(vm, form1.Id, "");
        RequireNoDuplicatePropertyRows(vm);

        var form2 = vm.CreateNewForm();
        EditPropertyGridTextLikeUi(vm, "FormTitle", "Form_B");
        RequirePropertyGridContext(vm, form2.Id, "");
        RequireNoDuplicatePropertyRows(vm);

        SwitchToForm(vm, form1.Id);
        RequirePropertyGridContext(vm, form1.Id, "");
        var form1TitleRow = GetPropertyGridRow(vm, "FormTitle");
        if (!string.Equals(form1TitleRow.Value, "Form_A", StringComparison.Ordinal))
            throw new InvalidOperationException($"Inspector showed stale form title for Form1. Got {form1TitleRow.Value}.");

        SwitchToForm(vm, form2.Id);
        RequirePropertyGridContext(vm, form2.Id, "");
        var form2TitleRow = GetPropertyGridRow(vm, "FormTitle");
        if (!string.Equals(form2TitleRow.Value, "Form_B", StringComparison.Ordinal))
            throw new InvalidOperationException($"Inspector showed stale form title for Form2. Got {form2TitleRow.Value}.");
    }

    private static void AssertInspectorShowsActiveFormPropertiesAfterSwitch(SmokeContext context)
    {
        RequireContains(context.Xaml, "x:Class", "Inspector active-form scenario should still export.");
    }

    private static void ConfigureRenameFormDoesNotDuplicatePropertyRows(MainWindowViewModel vm)
    {
        EditPropertyGridTextLikeUi(vm, "FormTitle", "RenamedForm");
        RequireNoDuplicatePropertyRows(vm);

        EditPropertyGridTextLikeUi(vm, "FormTitle", "RenamedFormAgain");
        RequireNoDuplicatePropertyRows(vm);

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_GRID_DUPLICATE_ROW_DETECTED"))
        {
            var trace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName == "PROPERTY_GRID_DUPLICATE_ROW_DETECTED")
                .Select(entry => entry.Details));
            throw new InvalidOperationException($"Duplicate PropertyGrid rows were detected after form rename: {trace}");
        }
    }

    private static void AssertRenameFormDoesNotDuplicatePropertyRows(SmokeContext context)
    {
        RequireContains(context.Xaml, "RenamedFormAgain", "Renamed form title should be exported.");
    }

    private static void ConfigureWidthHeightEditDoesNotLockInspector(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);

        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Width), "222");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Height), "64");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "After size edit");
        SetPropertyGridValue(vm, nameof(DesignControlModel.Background), "#334455");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Name), "ButtonAfterSize");

        if (!string.Equals(button.Name, "ButtonAfterSize", StringComparison.Ordinal)
            || !string.Equals(button.Text, "After size edit", StringComparison.Ordinal)
            || Math.Abs(button.Width - 222) > 0.001
            || Math.Abs(button.Height - 64) > 0.001
            || !string.Equals(button.Background, "#334455", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Width/Height edit left the inspector unable to edit later properties.");
        }

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_EDIT_STATE_STUCK"))
            throw new InvalidOperationException("Width/Height edit left a stuck property edit state.");
    }

    private static void AssertWidthHeightEditDoesNotLockInspector(SmokeContext context)
    {
        RequireContains(context.Xaml, "ButtonAfterSize", "Width/Height lock scenario should export renamed Button.");
        RequireContains(context.Xaml, "Content=\"After size edit\"", "Width/Height lock scenario should export later text edit.");
    }

    private static void ConfigureEmptyButtonTextIsPreserved(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "");

        if (!string.Equals(button.Text, "", StringComparison.Ordinal))
            throw new InvalidOperationException($"Empty Button text was not preserved in model. Got '{button.Text}'.");
    }

    private static void AssertEmptyButtonTextIsPreserved(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"\"", "Empty Button text should be exported as an explicit empty Content.");
        RequireNotContains(context.Xaml, "Content=\"Кнопка\"", "Empty Button text should not fall back to the default Russian caption.");
    }

    private static void ConfigurePropertyEditBlockedForStaleInspectorTarget(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form2 = vm.CreateNewForm();
        SwitchToForm(vm, form1.Id);

        var staleRow = new PropertyGridRowViewModel(
            "FormTitle",
            "Title",
            "Common",
            PropertyGridEditorKind.Text,
            "Form_B",
            "Stale form row.",
            (_, value) => vm.FormTitle = value,
            contextDocumentId: form2.Id,
            contextControlType: "Form");

        vm.CommitPropertyGridEdit(staleRow, "ShouldNotApply");

        if (string.Equals(vm.FormTitle, "ShouldNotApply", StringComparison.Ordinal))
            throw new InvalidOperationException("Stale inspector form edit was applied to the active form.");

        if (!vm.InteractionTraceEntries.Any(entry => entry.EventName == "PROPERTY_EDIT_BLOCKED_STALE_INSPECTOR_TARGET"))
            throw new InvalidOperationException("Stale inspector edit was not logged as blocked.");
    }

    private static void AssertPropertyEditBlockedForStaleInspectorTarget(SmokeContext context)
    {
        RequireNotContains(context.Xaml, "ShouldNotApply", "Stale inspector edit should not appear in exported XAML.");
    }

    private static void ConfigureAddFormRegressionStillWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");

        vm.CreateNewForm();
        SwitchToForm(vm, form1.Id);
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        RequirePropertyGridRowsContext(vm, form1.Id, restored.Id);
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "Regression still works");

        if (!string.Equals(restored.Text, "Regression still works", StringComparison.Ordinal))
            throw new InvalidOperationException("AddForm regression text edit failed.");

        if (vm.InteractionTraceEntries.Any(entry =>
            entry.EventName == "APPLY_DOCUMENT_START"
            && entry.Details.Contains("Property", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Property edit after AddForm triggered ApplyDocument.");
        }
    }

    private static void AssertAddFormRegressionStillWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Content=\"Regression still works\"", "AddForm regression should export edited Button text.");
    }

    private static void ConfigureNewProjectClearsPreviousFormsAndState(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        form1Button.Text = "Old project Button";
        vm.SelectSingleControl(form1Button);
        RequirePropertyGridRowsContext(vm, form1.Id, form1Button.Id);

        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.ImportedDllCatalog.Add(new ImportedDllInfoModel
        {
            FileName = "OldProject.dll",
            AssemblyPath = @"C:\Temp\OldProject.dll",
            SourceCount = 1,
            SourceNames = source.Name,
            TypeNames = source.ItemTypeName,
            Summary = "Fake smoke metadata"
        });
        vm.FilteredImportedDllCatalog.Add(vm.ImportedDllCatalog[0]);

        vm.GenerateXaml();
        var form2 = vm.CreateNewForm();
        var form2TextBox = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.TextBox, 70, 80, null, false, form2.Id)
            ?? throw new InvalidOperationException("Form2 TextBox was not created.");
        form2TextBox.PlaceholderText = "Old Form2";
        vm.SelectSingleControl(form2TextBox);

        var form3 = vm.CreateNewForm();
        var form3Border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 80, 90, null, false, form3.Id)
            ?? throw new InvalidOperationException("Form3 Border was not created.");
        vm.SelectSingleControl(form3Border);

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (oldFormIds.Count < 3 || oldControlIds.Count < 3)
            throw new InvalidOperationException("NewProject reset smoke did not prepare old multi-form state.");

        vm.NewDocumentCommand.Execute(null);

        AssertCleanNewProjectViewModelState(vm, oldFormIds, oldControlIds);
    }

    private static void AssertCleanNewProjectViewModelState(
        MainWindowViewModel vm,
        IReadOnlyList<string> oldFormIds,
        IReadOnlyList<string> oldControlIds)
    {
        if (vm.CurrentProject.Forms.Count != 1 || vm.Forms.Count != 1)
            throw new InvalidOperationException($"New Project should leave one form. Project={vm.CurrentProject.Forms.Count}, VM={vm.Forms.Count}.");

        var newForm = vm.ActiveFormDocument ?? throw new InvalidOperationException("New Project did not create an active Form1.");
        if (!string.Equals(newForm.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"New Project active form should be Form1. Got {newForm.DisplayName}.");

        if (oldFormIds.Any(oldId => string.Equals(newForm.Id, oldId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("New Project reused an old Form id.");

        if (newForm.Document.Controls.Count != 0)
            throw new InvalidOperationException($"New Project Form1 document should be empty. Controls={newForm.Document.Controls.Count}.");

        if (vm.Controls.Count != 0)
            throw new InvalidOperationException($"New Project canvas should be empty. Controls={vm.Controls.Count}.");

        if (vm.SelectedControl is not null || vm.SelectedControlIds.Count != 0)
            throw new InvalidOperationException("New Project should clear selected control.");

        if (vm.DocumentTabs.Count != 1 || !string.Equals(vm.DocumentTabs[0].DocumentId, newForm.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("New Project should leave exactly one tab for the new Form1.");

        var explorerFormIds = GetProjectExplorerFormIds(vm).ToList();
        if (explorerFormIds.Count != 1 || !string.Equals(explorerFormIds[0], newForm.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Project Explorer should show exactly the new Form1. Explorer forms: {string.Join(", ", explorerFormIds)}.");

        var formsFolder = FlattenExplorerItems(vm.ProjectExplorerItems).FirstOrDefault(item => string.Equals(item.TargetId, "Forms", StringComparison.OrdinalIgnoreCase));
        if (formsFolder is null || formsFolder.Count != 1)
            throw new InvalidOperationException($"Project Explorer Forms folder should show count 1. Got {formsFolder?.Count.ToString() ?? "missing"}.");

        if (vm.BindingSources.Count != 0 || vm.ImportedDllCatalog.Count != 0 || vm.FilteredImportedDllCatalog.Count != 0)
            throw new InvalidOperationException("New Project should clear data sources and imported DLL metadata.");

        if (vm.HasUndo || vm.HasRedo)
            throw new InvalidOperationException("New Project should clear undo/redo state.");

        if (vm.UndoRedoHistoryItems.Any(item =>
            oldFormIds.Any(oldId => item.Snapshot.Contains(oldId, StringComparison.OrdinalIgnoreCase))
            || oldControlIds.Any(oldId => item.Snapshot.Contains(oldId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("New Project history snapshots still contain old form/control ids.");
        }

        if (vm.GeneratedFiles.Count != 0 || vm.RequiredPackages.Count != 0 || vm.ExportDiagnostics.Count != 0)
            throw new InvalidOperationException("New Project should clear generated export pipeline state.");

        var leakedFormIds = oldFormIds.Where(oldId =>
            vm.CurrentProject.Forms.Any(form => string.Equals(form.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.Forms.Any(form => string.Equals(form.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.DocumentTabs.Any(tab => string.Equals(tab.DocumentId, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.Workspace.Session.OpenDocumentIds.Any(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(vm.Workspace.Session.ActiveDocumentId, oldId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (leakedFormIds.Count > 0)
            throw new InvalidOperationException($"Old form ids leaked after New Project: {string.Join(", ", leakedFormIds)}.");

        var leakedControlIds = oldControlIds.Where(oldId =>
            vm.Controls.Any(control => string.Equals(control.Id, oldId, StringComparison.OrdinalIgnoreCase))
            || vm.SelectedControlIds.Any(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(vm.SelectedControl?.Id, oldId, StringComparison.OrdinalIgnoreCase)
            || vm.PropertyGridCategories.SelectMany(category => category.Rows).Any(row => string.Equals(row.ContextControlId, oldId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (leakedControlIds.Count > 0)
            throw new InvalidOperationException($"Old control ids leaked after New Project: {string.Join(", ", leakedControlIds)}.");

        if (vm.InteractionTraceEntries.Any(entry => entry.EventName == "NEW_PROJECT_LEAKED_OLD_STATE"))
        {
            var leakTrace = string.Join(" | ", vm.InteractionTraceEntries
                .Where(entry => entry.EventName == "NEW_PROJECT_LEAKED_OLD_STATE")
                .Select(entry => entry.Details));
            throw new InvalidOperationException($"New Project leak trace was emitted: {leakTrace}");
        }
    }

    private static void AssertNewProjectClearsPreviousFormsAndState(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "New Project should still export a clean empty Form1 canvas.");
        RequireNotContains(context.Xaml, "Old project Button", "Old project controls must not appear in new project export.");
        RequireNotContains(context.Xaml, "Old Form2", "Old Form2 controls must not appear in new project export.");
        if (context.ViewModel.CurrentProject.Forms.Count != 1 || context.ViewModel.Controls.Count != 0)
            throw new InvalidOperationException("NewProjectClearsPreviousFormsAndState should finish with one empty Form1.");
    }

    private static void ConfigureNewProjectAfterSavedMultiFormProjectClearsAllOldForms(MainWindowViewModel vm)
    {
        var state = PrepareSavedTwoFormProjectForNewCommand(vm);
        vm.NewDocumentCommand.Execute(null);
        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);
    }

    private static void AssertNewProjectAfterSavedMultiFormProjectClearsAllOldForms(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "New Project after saved multi-form project should export clean Form1.");
        RequireNotContains(context.Xaml, "OldForm1Button", "Old Form1 Button must not be exported after New.");
        RequireNotContains(context.Xaml, "OldForm2Button", "Old Form2 Button must not be exported after New.");
        if (GetProjectExplorerFormIds(context.ViewModel).Count() != 1)
            throw new InvalidOperationException("Project Explorer should contain one form node after New.");
    }

    private static void ConfigureNewProjectDoesNotReuseOldFormNamesOrIds(MainWindowViewModel vm)
    {
        var state = PrepareSavedTwoFormProjectForNewCommand(vm);
        vm.NewDocumentCommand.Execute(null);

        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);

        var forms = vm.CurrentProject.Forms.ToList();
        if (forms.Count(form => string.Equals(form.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidOperationException($"New Project should have exactly one Form1. Forms: {string.Join(", ", forms.Select(form => $"{form.DisplayName}:{form.Id}"))}");

        var newForm = forms[0];
        if (state.OldFormIds.Any(oldId => string.Equals(newForm.Id, oldId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("New Project reused an old form id.");
        if (state.OldControlIds.Any(oldId => newForm.Document.Controls.Any(control => string.Equals(control.Id, oldId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("New Project reused old controls inside the new Form1 document.");
    }

    private static void AssertNewProjectDoesNotReuseOldFormNamesOrIds(SmokeContext context)
    {
        if (context.ViewModel.CurrentProject.Forms.Count != 1)
            throw new InvalidOperationException("NewProjectDoesNotReuseOldFormNamesOrIds should finish with one form.");
        RequireNotContains(context.Xaml, "OldForm1Button", "Old Form1 Button should not remain after New.");
        RequireNotContains(context.Xaml, "OldForm2Button", "Old Form2 Button should not remain after New.");
    }

    private static void ConfigureNewProjectReleasesOldFormsAndControls(MainWindowViewModel vm)
    {
        var state = PrepareWeakReferenceProjectAndRunNew(vm);
        ForceFullGc();

        var alive = state.WeakReferences
            .Where(item => item.Reference.IsAlive)
            .Select(item => $"{item.Kind}:{item.Id}")
            .ToList();
        if (alive.Count > 0)
            throw new InvalidOperationException($"Old project objects are still alive after New Project: {string.Join(", ", alive.Take(12))}");

        AssertCleanNewProjectViewModelState(vm, state.OldFormIds, state.OldControlIds);
    }

    private static void AssertNewProjectReleasesOldFormsAndControls(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Canvas", "NewProjectReleasesOldFormsAndControls should export clean Form1.");
        RequireNotContains(context.Xaml, "WeakOldForm", "Old weak-reference controls must not be exported after New.");
    }

    private static NewProjectWeakState PrepareWeakReferenceProjectAndRunNew(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 44, 44, null, false, form1.Id)
            ?? throw new InvalidOperationException("Old Form1 Button was not created.");
        form1Button.Name = "WeakOldForm1Button";
        vm.SelectSingleControl(form1Button);

        var form2 = vm.CreateNewForm();
        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 84, 84, null, false, form2.Id)
            ?? throw new InvalidOperationException("Old Form2 Button was not created.");
        form2Button.Name = "WeakOldForm2Button";

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var weakReferences = new List<NamedWeakReference>
        {
            new("Form", form1.Id, new WeakReference(form1)),
            new("Form", form2.Id, new WeakReference(form2)),
            new("RuntimeControl", form1Button.Id, new WeakReference(form1Button)),
            new("RuntimeControl", form2Button.Id, new WeakReference(form2Button))
        };
        weakReferences.AddRange(vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .Take(8)
            .Select(row => new NamedWeakReference("PropertyRow", $"{row.ContextDocumentId}/{row.ContextControlId}/{row.Key}", new WeakReference(row))));

        vm.NewDocumentCommand.Execute(null);
        return new NewProjectWeakState(oldFormIds, oldControlIds, weakReferences);
    }

    private static void ConfigureNewProjectClearsDataDllExportCaches(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.ImportedDllCatalog.Add(new ImportedDllInfoModel
        {
            FileName = "HeavyMetadata.dll",
            AssemblyPath = @"C:\Temp\HeavyMetadata.dll",
            SourceCount = 1,
            SourceNames = source.Name,
            TypeNames = source.ItemTypeName,
            Summary = "Fake heavy metadata"
        });
        vm.FilteredImportedDllCatalog.Add(vm.ImportedDllCatalog[0]);
        vm.Controls.Add(DataGrid("OldDataGrid", source.Id, 40, 40, 520, 260));
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.GenerateXaml();

        if (vm.BindingSources.Count == 0 || vm.ImportedDllCatalog.Count == 0 || vm.GeneratedFiles.Count == 0)
            throw new InvalidOperationException("Data/DLL/export cache smoke setup did not create cache state.");

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).ToList();
        var oldControlIds = vm.Controls.Select(control => control.Id).ToList();
        vm.NewDocumentCommand.Execute(null);
        AssertCleanNewProjectViewModelState(vm, oldFormIds, oldControlIds);
    }

    private static void AssertNewProjectClearsDataDllExportCaches(SmokeContext context)
    {
        if (context.ViewModel.BindingSources.Count != 0
            || context.ViewModel.ImportedDllCatalog.Count != 0
            || context.ViewModel.FilteredImportedDllCatalog.Count != 0)
        {
            throw new InvalidOperationException("New Project should clear Data/DLL caches.");
        }

        RequireNotContains(context.Xaml, "OldDataGrid", "Old DataGrid should not remain after New Project.");
    }

    private static void ConfigurePropertyEditDoesNotTriggerExportPipeline(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 60, 60, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "No export storm");

        var forbidden = vm.InteractionTraceEntries
            .Where(entry => entry.EventName is "RefreshExportPipelineResult" or "APPLY_DOCUMENT_START" or "APPLY_DOCUMENT_END"
                || entry.EventName.Contains("BuildSecondaryFormGeneratedFiles", StringComparison.OrdinalIgnoreCase)
                || entry.Details.Contains("BuildSecondaryFormGeneratedFiles", StringComparison.OrdinalIgnoreCase)
                || entry.Details.Contains("RefreshExportPipelineResult", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.EventName}:{entry.Details}")
            .ToList();
        if (forbidden.Count > 0)
            throw new InvalidOperationException($"Property edit triggered heavy export/apply pipeline: {string.Join(" | ", forbidden)}");
    }

    private static void AssertPropertyEditDoesNotTriggerExportPipeline(SmokeContext context)
    {
        RequireContains(context.Xaml, "No export storm", "Property edit should still persist before final explicit export.");
    }

    private static void ConfigureResizeDoesNotRebuildPropertyGridOnEveryMove(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 80, 80, null, false, form1.Id)
            ?? throw new InvalidOperationException("Button was not created.");
        vm.SelectSingleControl(button);
        vm.InteractionTraceEntries.Clear();

        for (var index = 0; index < 30; index++)
            vm.MoveSelectedControl(1, 1);

        var rebuilds = vm.InteractionTraceEntries.Count(entry => entry.EventName == "RebuildPropertyGrid");
        if (rebuilds > 2)
            throw new InvalidOperationException($"Resize/move should not rebuild PropertyGrid on every move. Rebuilds={rebuilds}.");
    }

    private static void AssertResizeDoesNotRebuildPropertyGridOnEveryMove(SmokeContext context)
    {
        RequireContains(context.Xaml, "Button1", "Resize/move storm scenario should keep the Button exportable.");
    }

    private static NewProjectOldState PrepareSavedTwoFormProjectForNewCommand(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var form1Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 42, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Old Form1 Button was not created.");
        form1Button.Name = "OldForm1Button";
        form1Button.Text = "Old Form1 Button";

        var form2 = vm.CreateNewForm();
        var form2Button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 82, 88, null, false, form2.Id)
            ?? throw new InvalidOperationException("Old Form2 Button was not created.");
        form2Button.Name = "OldForm2Button";
        form2Button.Text = "Old Form2 Button";

        var oldJson = vm.ExportWorkspaceJson();
        if (!oldJson.Contains("OldForm1Button", StringComparison.Ordinal) || !oldJson.Contains("OldForm2Button", StringComparison.Ordinal))
            throw new InvalidOperationException("Saved multi-form smoke workspace did not contain both old buttons.");
        vm.MarkDocumentSaved(Path.Combine(Path.GetTempPath(), $"old-project-{Guid.NewGuid():N}.formdesigner.json"));

        var oldFormIds = vm.CurrentProject.Forms.Select(form => form.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var oldControlIds = vm.CurrentProject.Forms
            .SelectMany(form => form.Document.Controls)
            .Select(control => control.Id)
            .Concat(vm.Controls.Select(control => control.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (oldFormIds.Count != 2 || oldControlIds.Count < 2)
            throw new InvalidOperationException("Saved multi-form smoke setup did not prepare two old forms and controls.");

        return new NewProjectOldState(oldFormIds, oldControlIds);
    }

    private static void ConfigureDragAfterAddFormWorks(MainWindowViewModel vm)
    {
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");
        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 40, 40, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");

        vm.AddFormEditorCommand?.Execute(null);
        vm.SetActiveForm(form1.Id, "DragAfterAddFormWorks");
        var restored = vm.Controls.Single(control => control.Id == button.Id);
        vm.SelectSingleControl(restored);
        vm.MoveSelectedControl(25, 15);

        if (Math.Abs(restored.X - 65) > 0.001 || Math.Abs(restored.Y - 55) > 0.001)
            throw new InvalidOperationException($"Move after AddForm did not apply. Got {restored.X}/{restored.Y}.");

        vm.SetActiveForm(vm.CurrentProject.Forms.Single(form => !string.Equals(form.Id, form1.Id, StringComparison.OrdinalIgnoreCase)).Id, "DragAfterAddFormWorks verify Form2");
        vm.SetActiveForm(form1.Id, "DragAfterAddFormWorks verify Form1");
        restored = vm.Controls.Single(control => control.Id == button.Id);
        if (Math.Abs(restored.X - 65) > 0.001 || Math.Abs(restored.Y - 55) > 0.001)
            throw new InvalidOperationException("Moved position was lost after switching forms.");
    }

    private static void AssertDragAfterAddFormWorks(SmokeContext context)
    {
        RequireContains(context.Xaml, "Canvas.Left=\"65\"", "Moved Button X should be exported after AddForm.");
        RequireContains(context.Xaml, "Canvas.Top=\"55\"", "Moved Button Y should be exported after AddForm.");
    }

    private static void ConfigureAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 32, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        var border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 32, 104, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created.");
        var grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created.");

        button.Name = "Button1";
        border.Name = "Border1";
        grid.Name = "DataGrid1";

        vm.SelectSingleControl(border);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "300");
        RequirePropertyGridContext(vm, form1.Id, border.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (form2.Id == form1.Id)
            throw new InvalidOperationException("AddForm did not switch to a new form.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 should be empty in this regression scenario.");

        SwitchToForm(vm, form1.Id);
        if (vm.ActiveDocumentId != form1.Id)
            throw new InvalidOperationException("ActiveDocumentId did not return to Form1.");

        RequireActiveControls(vm, "Button1", "Border1", "DataGrid1");
        var restoredBorder = vm.Controls.Single(control => control.Name == "Border1");
        vm.SelectSingleControl(restoredBorder);
        RequirePropertyGridContext(vm, form1.Id, restoredBorder.Id);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Width), "500");
        if (Math.Abs(restoredBorder.Width - 500) > 0.001)
            throw new InvalidOperationException("Form1.Border1 Width edit did not apply after adding empty Form2.");

        var restoredButton = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(restoredButton);
        RequirePropertyGridContext(vm, form1.Id, restoredButton.Id);
        SetPropertyGridValue(vm, nameof(DesignControlModel.Text), "Button after empty Form2");
        if (restoredButton.Text != "Button after empty Form2")
            throw new InvalidOperationException("Form1.Button1 Text edit did not apply after adding empty Form2.");

        if (vm.OutputEntries.Any(entry => entry.Message.Contains("Rejected stale PropertyGrid edit", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A stale PropertyGrid edit was rejected during the empty Form2 regression.");
    }

    private static void AssertAddEmptySecondFormDoesNotBreakFirstFormPropertyGrid(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireContains(context.Xaml, "Button after empty Form2", "Form1 Button edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Width=\"500\"", "Form1 Border width edit after empty Form2 should be exported.");
        var form2File = context.GeneratedFiles.FirstOrDefault(file => string.Equals(file.Path, "Form2.axaml", StringComparison.OrdinalIgnoreCase));
        if (form2File is not null)
            RequireNotContains(form2File.Content, "Button after empty Form2", "Empty Form2 export should not contain Form1 Button.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Empty Form2 workflow should not produce document isolation diagnostics.");
    }

    private static void ConfigureAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic(MainWindowViewModel vm)
    {
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form1 missing.");

        var button = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Button, 32, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Button was not created.");
        var border = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.Border, 32, 104, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 Border was not created.");
        var grid = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.DataGrid, 240, 42, null, false, form1.Id)
            ?? throw new InvalidOperationException("Form1 DataGrid was not created.");

        button.Name = "Button1";
        button.Text = "Before second form";
        border.Name = "Border1";
        grid.Name = "DataGrid1";

        vm.SelectSingleControl(button);
        RequirePropertyGridContext(vm, form1.Id, button.Id);
        RequirePropertyGridRowsContext(vm, form1.Id, button.Id);

        vm.AddFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Form2 missing.");
        if (form2.Id == form1.Id)
            throw new InvalidOperationException("AddForm did not switch to a new form.");
        if (vm.Controls.Count != 0)
            throw new InvalidOperationException("New Form2 should be empty.");

        SwitchToForm(vm, form1.Id);
        if (!string.Equals(vm.ActiveDocumentId, form1.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ActiveDocumentId did not return to Form1.");

        var restoredButton = vm.Controls.Single(control => control.Name == "Button1");
        vm.SelectSingleControl(restoredButton);
        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "Button1");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Name), "OpenDetailsButton");
        if (!string.Equals(restoredButton.Name, "OpenDetailsButton", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Name edit did not apply to Form1 selected control.");

        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "OpenDetailsButton");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Text), "Open details");
        if (!string.Equals(restoredButton.Text, "Open details", StringComparison.Ordinal))
            throw new InvalidOperationException("Button.Text edit did not apply to Form1 selected control.");

        var restoredBorder = vm.Controls.Single(control => control.Name == "Border1");
        vm.SelectSingleControl(restoredBorder);
        RequireInspectorSelection(vm, form1.Id, restoredBorder.Id, "Border1");
        EditPropertyGridTextLikeUi(vm, nameof(DesignControlModel.Width), "444");
        if (Math.Abs(restoredBorder.Width - 444) > 0.001)
            throw new InvalidOperationException("Border.Width edit did not apply to Form1 selected control.");

        vm.SelectSingleControl(restoredButton);
        RequireInspectorSelection(vm, form1.Id, restoredButton.Id, "OpenDetailsButton");
        vm.AddShowMessageInteractionForSelectedCommand.Execute(null);
        if (vm.SelectedInteraction is null)
            throw new InvalidOperationException("Button Logic quick action did not create an interaction.");
        if (!string.Equals(vm.WorkspaceMode, MainWindowViewModel.WorkspaceModeLogic, StringComparison.Ordinal))
            throw new InvalidOperationException("Button Logic quick action should keep the Logic workspace active.");
        if (!string.Equals(vm.SelectedInteraction.SourceControlName, restoredButton.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(vm.SelectedInteraction.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(vm.SelectedInteraction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Button Logic interaction was created with an unexpected source/event/action.");
        }

        if (vm.OutputEntries.Any(entry => entry.Message.Contains("Rejected stale PropertyGrid edit", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A stale PropertyGrid edit was rejected during the inspector/logic regression.");
    }

    private static void AssertAddEmptySecondFormDoesNotBreakFirstFormInspectorAndLogic(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireContains(context.Xaml, "x:Name=\"OpenDetailsButton\"", "Button.Name edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Content=\"Open details\"", "Button.Text edit after empty Form2 should be exported.");
        RequireContains(context.Xaml, "Click=\"OpenDetailsButtonClick\"", "Button.Click logic should be exported.");
        RequireContains(context.CSharp, "private async void OpenDetailsButtonClick", "Button.Click ShowMessage handler should be generated.");
        RequireContains(context.Xaml, "Width=\"444\"", "Border.Width edit after empty Form2 should be exported.");
        RequireNotContains(context.DiagnosticsText, "Document isolation", "Inspector/Logic regression should not produce document isolation diagnostics.");
    }

    private static void ConfigureAlphaEndToEndProjectExport(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);
    }

    private static void AssertAlphaEndToEndProjectExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "Alpha OpenForm handler should be wired in XAML.");
        RequireContains(context.Xaml, "SelectionChanged=\"CustomersGrid_SelectionChanged\"", "Alpha DataGrid.SelectionChanged handler should be wired in XAML.");
        RequireContains(context.Xaml, "Checked=\"DetailsCheckBox_Checked\"", "Alpha CheckBox.Checked handler should be wired in XAML.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "Alpha OpenForm handler should instantiate Form2.");
        RequireContains(context.CSharp, "SelectedNameTextBox.Text = ResolveInteractionValue(selectedItem, @\"Name\", @\"\");", "Alpha DataGrid selection should fill TextBox from Name.");
        RequireContains(context.ChecklistText, "Forms exported: 2/2", "Alpha export should include both forms.");
        RequireContains(context.ChecklistText, "Interactions exported: 4/4", "Alpha export should include all configured interactions.");
        RequireContains(context.ChecklistText, "OpenForm interactions: 1", "Alpha checklist should report OpenForm.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Alpha Real DataGrid export should require DataGrid NuGet.");
    }

    private static void ConfigureDataGridBindingSourceWorkflow(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        var grid = DataGrid("CustomersGrid", source.Id, 34, 48, 650, 300);
        grid.ShowFilterRow = true;
        grid.ShowGroupPanel = true;
        grid.AllowGrouping = true;
        vm.Controls.Add(grid);
    }

    private static void AssertDataGridBindingSourceWorkflow(SmokeContext context)
    {
        RequireContains(context.Xaml, "<dataGrid:DataGrid", "BindingSource workflow should export a real DataGrid.");
        RequireContains(context.Xaml, "Binding Path=\"Id\"", "DataGrid should include BindingSource field Id.");
        RequireContains(context.Xaml, "Binding Path=\"Name\"", "DataGrid should include BindingSource field Name.");
        RequireContains(context.Xaml, "Binding Path=\"Email\"", "DataGrid should include BindingSource field Email.");
        RequireContains(context.Xaml, "Binding Path=\"Status\"", "DataGrid should include BindingSource field Status.");
        RequireContains(context.ChecklistText, "DataGrid: Real Avalonia DataGrid", "Checklist should keep Real DataGrid mode.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "Checklist should report DataGrid package.");
        RequireNotContains(context.DiagnosticsText, "DataGrid without fields", "DataGrid with real BindingSource fields must not warn about missing fields.");
    }

    private static void ConfigureOpenFormPreviewAndExport(MainWindowViewModel vm)
    {
        ConfigureTwoFormOpenFormProject(vm);
    }

    private static void AssertOpenFormPreviewAndExport(SmokeContext context)
    {
        RequireGeneratedFile(context, "MainWindow.axaml");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireGeneratedFile(context, "Form2.axaml");
        RequireGeneratedFile(context, "Form2.axaml.cs");
        RequireContains(context.Xaml, "Click=\"OpenForm2ButtonClick\"", "OpenForm preview/export smoke should wire Button.Click.");
        RequireContains(context.CSharp, "var windowForm2 = new Form2();", "OpenForm export should create Form2.");
        RequireContains(context.CSharp, "windowForm2.Show();", "OpenForm export should call Show.");
        RequireNotContains(context.DiagnosticsText, "OpenForm target form not found", "OpenForm target should be valid.");
        RequireNotContains(context.DiagnosticsText, "OpenForm target form не выбран", "OpenForm target should be selected.");
    }

    private static void ConfigurePropertyGridDataGridSetup(MainWindowViewModel vm)
    {
        ConfigureDataGridBindingSourceWorkflow(vm);
        var grid = vm.Controls.First(control => string.Equals(control.Name, "CustomersGrid", StringComparison.OrdinalIgnoreCase));
        vm.SelectControls(new[] { grid }, grid);
        vm.WorkspaceMode = MainWindowViewModel.WorkspaceModeData;

        RequirePropertyGridRow(vm, "Width");
        RequirePropertyGridRow(vm, "Height");
        RequirePropertyGridRow(vm, "HeaderBackground");
        if (vm.SelectedBindingSourceForControl is null || vm.SelectedBindingSourceForControl.Fields.Count != 4)
            throw new InvalidOperationException("DataGrid setup should expose the selected BindingSource with four fields.");
        if (!vm.SelectedGridColumnCompactSummary.Contains("4", StringComparison.Ordinal))
            throw new InvalidOperationException("DataGrid column summary should report four generated fields.");
    }

    private static void AssertPropertyGridDataGridSetup(SmokeContext context)
    {
        RequireContains(context.Xaml, "CustomersGrid", "PropertyGrid setup scenario should keep DataGrid in export.");
        RequireContains(context.ChecklistText, "Avalonia.Controls.DataGrid", "PropertyGrid setup should preserve required DataGrid package.");
        RequireNotContains(context.DiagnosticsText, "BindingSource не выбран", "PropertyGrid setup should keep DataGrid BindingSource assigned.");
    }

    private static void ConfigureSaveLoadMultiFormProject(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);

        var form2Document = vm.CurrentProject.Forms.First(form => string.Equals(form.DisplayName, "Form2", StringComparison.OrdinalIgnoreCase));
        SwitchToForm(vm, form2Document.Id);
        var form1Document = vm.CurrentProject.Forms.First(form => form.DisplayName.StartsWith("Form1", StringComparison.OrdinalIgnoreCase));
        SwitchToForm(vm, form1Document.Id);

        var json = JsonSerializer.Serialize(vm.Workspace, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<WorkspaceModel>(json) ?? throw new InvalidOperationException("Workspace round-trip returned null.");
        if (restored.Project.Forms.Count != 2)
            throw new InvalidOperationException("Workspace round-trip should preserve two forms.");

        var form1 = restored.Project.Forms.FirstOrDefault(form => string.Equals(form.DisplayName, "Form1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Workspace round-trip should preserve Form1.");
        var form2 = restored.Project.Forms.FirstOrDefault(form => string.Equals(form.DisplayName, "Form2", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Workspace round-trip should preserve Form2.");
        if (form1.Document.Controls.All(control => !string.Equals(control.Name, "CustomersGrid", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Workspace round-trip should preserve Form1 controls.");
        if (form1.Document.BindingSources.Count != 1 || form1.Document.BindingSources[0].Fields.Count != 4)
            throw new InvalidOperationException("Workspace round-trip should preserve BindingSource fields.");
        if (form1.Document.Interactions.Count != 4)
            throw new InvalidOperationException("Workspace round-trip should preserve Form1 interactions.");
        if (form2.Document.Controls.All(control => !string.Equals(control.Name, "Form2Title", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Workspace round-trip should preserve Form2 controls.");
    }

    private static void AssertSaveLoadMultiFormProject(SmokeContext context)
    {
        RequireGeneratedFile(context, "Form2.axaml");
        RequireContains(context.ChecklistText, "Forms exported: 2/2", "Save/load smoke should still export both forms.");
        RequireContains(context.CSharp, "new Form2()", "Save/load smoke should preserve OpenForm interaction.");
    }

    private static void ConfigureExportToProjectBuildValidation(MainWindowViewModel vm)
    {
        ConfigureAlphaProject(vm);
    }

    private static void AssertExportToProjectBuildValidation(SmokeContext context)
    {
        var scenarioRoot = Directory.GetParent(context.ProjectPath)?.FullName ?? context.ProjectPath;
        var validationRoot = Path.Combine(scenarioRoot, $"{context.Scenario.Name}-validation");
        var result = context.ViewModel.ValidateCurrentExportBuildAsync(validationRoot).GetAwaiter().GetResult();
        if (result.Status != ExportBuildValidationStatus.Passed)
            throw new InvalidOperationException($"Export pipeline build validation failed.{Environment.NewLine}{result.Output}");

        RequireContains(result.ProjectPath, "-validation", "Build validation should create a temporary validation project.");
        RequireContains(File.ReadAllText(Path.Combine(result.ProjectPath, "ExportValidation.csproj"), Encoding.UTF8), "<TargetFramework>net6.0</TargetFramework>", "Validation project should target net6.0.");
        RequireContains(File.ReadAllText(Path.Combine(result.ProjectPath, "ExportValidation.csproj"), Encoding.UTF8), "Avalonia.Controls.DataGrid", "Validation project should include DataGrid package.");
    }

    private static void ConfigureAlphaProject(MainWindowViewModel vm)
    {
        var source = CustomersSource();
        vm.BindingSources.Add(source);
        vm.SelectedBindingSource = source;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;

        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form1Title", 30, 24, 360, 32, text: "Customers"));
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 30, 74, 190, 42, text: "Открыть Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 8));
        vm.Controls.Add(DataGrid("CustomersGrid", source.Id, 30, 136, 620, 280));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "SelectedNameTextBox", 690, 136, 250, 38, placeholder: "Selected Name"));
        vm.Controls.Add(Control(DesignerControlTypes.CheckBox, "DetailsCheckBox", 690, 196, 180, 34, text: "Show details"));
        vm.Controls.Add(Control(DesignerControlTypes.Border, "DetailsPanel", 690, 250, 250, 118, background: "#EFF6FF", border: "#BFDBFE", radius: 10));

        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        form1.Name = "Form1";
        form1.Document.FormTitle = "Form1";
        vm.FormTitle = "Form1";
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 40, 40, 360, 32, text: "Form2"));
        vm.Controls.Add(Control(DesignerControlTypes.TextBox, "Form2Input", 40, 96, 280, 40, placeholder: "Form2 input"));

        SwitchToForm(vm, form1.Id);
        RequireActiveControls(vm, "OpenForm2Button", "CustomersGrid", "SelectedNameTextBox", "DetailsCheckBox", "DetailsPanel");
        RequireNoActiveControls(vm, "Form2Title", "Form2Input");

        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "OpenForm2Button",
            EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionOpenForm,
            TargetFormId = form2.Id,
            TargetFormName = form2.DisplayName,
            OpenMode = InteractionModel.OpenModeShow
        });
        vm.Interactions.Add(new InteractionModel
        {
            SourceControlName = "CustomersGrid",
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = "SelectedNameTextBox",
            TargetProperty = InteractionModel.TargetPropertyText,
            SourcePath = "Name"
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
    }

    private static void ConfigureTwoFormOpenFormProject(MainWindowViewModel vm)
    {
        vm.ExportTarget = MainWindowViewModel.ExportTargetMainWindow;
        vm.Controls.Add(Control(DesignerControlTypes.Button, "OpenForm2Button", 36, 92, 180, 42, text: "Open Form2", background: "#2563EB", foreground: "#FFFFFF", border: "#1D4ED8", radius: 10));
        var form1 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form1 document missing.");
        vm.NewFormEditorCommand?.Execute(null);
        var form2 = vm.ActiveFormDocument ?? throw new InvalidOperationException("Active Form2 document missing.");
        form2.Name = "Form2";
        form2.Document.FormTitle = "Form2";
        vm.FormTitle = "Form2";
        vm.Controls.Add(Control(DesignerControlTypes.TextBlock, "Form2Title", 36, 34, 340, 30, text: "Form2 window"));
        SwitchToForm(vm, form1.Id);
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

    private static BindingSourceModel CustomersSource()
    {
        var source = new BindingSourceModel
        {
            Id = "customers-source",
            Name = "CustomersSource",
            Path = "Customers",
            ItemTypeName = "CustomerRow",
            Description = "Alpha 0.2 customers source."
        };
        source.Fields.Add(Field("Id", "Id", "1", "int", "80"));
        source.Fields.Add(Field("Name", "Name", "Ada Lovelace", "string", "2*"));
        source.Fields.Add(Field("Email", "Email", "ada@example.com", "string", "2*"));
        source.Fields.Add(Field("Status", "Status", "Active", "string", "*"));
        return source;
    }

    private static void SwitchToForm(MainWindowViewModel vm, string formId)
    {
        if (!vm.SetActiveForm(formId, "SmokeSwitchToForm"))
            throw new InvalidOperationException($"Could not switch to form {formId}.");

        if (!string.Equals(vm.ActiveDocumentId, formId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ActiveDocumentId mismatch after switch. Expected {formId}, got {vm.ActiveDocumentId}.");
    }

    private static void RequirePropertyGridRow(MainWindowViewModel vm, string label)
    {
        var hasRow = vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .Any(row => string.Equals(row.Label, label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Key, label, StringComparison.OrdinalIgnoreCase));
        if (!hasRow)
            throw new InvalidOperationException($"PropertyGrid row missing: {label}");
    }

    private static void SetPropertyGridValue(MainWindowViewModel vm, string key, string value)
    {
        var row = GetPropertyGridRow(vm, key);
        row.Value = value;
        row.CommitValue();
    }

    private static void EditPropertyGridTextLikeUi(MainWindowViewModel vm, string key, string value)
    {
        var row = GetPropertyGridRow(vm, key);

        vm.BeginPropertyGridTextEdit(row, row.Value);
        var text = "";
        foreach (var character in value)
        {
            text += character;
            vm.UpdatePropertyGridTextEdit(row, text);
        }

        vm.CommitPropertyGridTextEdit(row, value);
    }

    private static PropertyGridRowViewModel GetPropertyGridRow(MainWindowViewModel vm, string key)
    {
        return vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PropertyGrid row missing: {key}");
    }

    private static void RequirePropertyGridContext(MainWindowViewModel vm, string documentId, string controlId)
    {
        if (!string.Equals(vm.PropertyGridContextDocumentId, documentId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PropertyGrid context document mismatch. Expected {documentId}, got {vm.PropertyGridContextDocumentId}.");

        if (!string.Equals(vm.PropertyGridContextControlId, controlId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PropertyGrid context control mismatch. Expected {controlId}, got {vm.PropertyGridContextControlId}.");
    }

    private static void RequireInspectorSelection(MainWindowViewModel vm, string documentId, string controlId, string expectedTitlePrefix)
    {
        RequirePropertyGridContext(vm, documentId, controlId);
        RequirePropertyGridRowsContext(vm, documentId, controlId);

        if (vm.SelectedControl is null || !string.Equals(vm.SelectedControl.Id, controlId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SelectedControl does not match the expected inspector control.");

        if (!vm.PropertyGridSelectionTitle.StartsWith(expectedTitlePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Inspector header mismatch. Expected prefix '{expectedTitlePrefix}', got '{vm.PropertyGridSelectionTitle}'.");
    }

    private static void RequirePropertyGridRowsContext(MainWindowViewModel vm, string documentId, string controlId)
    {
        var rows = vm.PropertyGridCategories.SelectMany(category => category.Rows).ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("PropertyGrid has no rows.");

        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.ContextControlId)))
        {
            if (!string.Equals(row.ContextDocumentId, documentId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.ContextControlId, controlId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PropertyGrid row context mismatch for {row.Key}. Expected {documentId}/{controlId}, got {row.ContextDocumentId}/{row.ContextControlId}.");
            }
        }
    }

    private static void RequireNoDuplicatePropertyRows(MainWindowViewModel vm)
    {
        var duplicate = vm.PropertyGridCategories
            .SelectMany(category => category.Rows)
            .GroupBy(row => $"{row.ContextDocumentId}|{row.ContextControlId}|{row.Key}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            var rows = string.Join(", ", duplicate.Select(row => $"{row.Category}:{row.Label}"));
            throw new InvalidOperationException($"Duplicate PropertyGrid rows detected for {duplicate.Key}: {rows}");
        }
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

    private static IEnumerable<ProjectExplorerItemModel> FlattenExplorerItems(IEnumerable<ProjectExplorerItemModel> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenExplorerItems(item.Children))
                yield return child;
        }
    }

    private static IEnumerable<string> GetProjectExplorerFormIds(MainWindowViewModel vm)
    {
        return FlattenExplorerItems(vm.ProjectExplorerItems)
            .Where(item => string.Equals(item.ItemType, "Form", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.TargetId)
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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
        File.WriteAllText(Path.Combine(context.ProjectPath, "NuGet.config"), BuildNuGetConfig(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(context.ProjectPath, "smoke-summary.txt"), BuildSummary(context), Encoding.UTF8);
    }

    private static string BuildNuGetConfig()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
  <packageSourceMapping>
    <clear />
  </packageSourceMapping>
</configuration>
";
    }

    private static string BuildProjectFile(SmokeScenario scenario)
    {
        var dataGridPackage = scenario.RequiresRealDataGrid
            ? $@"    <PackageReference Include=""Avalonia.Controls.DataGrid"" Version=""{AvaloniaVersion}"" />"
            : "";

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
{dataGridPackage}
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

    private static string BuildSummary(SmokeContext context)
    {
        return $@"Scenario: {context.Scenario.Name}
Project: {context.ProjectPath}

Checklist:
{context.ChecklistText}

Diagnostics:
{context.DiagnosticsText}
";
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

    private static void PruneSmokeRuns(string artifactsRoot, string currentRunRoot)
    {
        var root = new DirectoryInfo(Path.GetFullPath(artifactsRoot));
        if (!root.Exists)
            return;

        var current = Path.GetFullPath(currentRunRoot);
        foreach (var staleRun in root.GetDirectories()
                     .OrderByDescending(directory => directory.LastWriteTimeUtc)
                     .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                     .Skip(SmokeRunsToKeep))
        {
            var fullPath = Path.GetFullPath(staleRun.FullName);
            if (string.Equals(fullPath, current, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fullPath.StartsWith(root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            DeleteWithRetry(() => staleRun.Delete(recursive: true), staleRun.FullName);
        }
    }

    private sealed record NewProjectOldState(
        IReadOnlyList<string> OldFormIds,
        IReadOnlyList<string> OldControlIds);

    private sealed record NamedWeakReference(
        string Kind,
        string Id,
        WeakReference Reference);

    private sealed record NewProjectWeakState(
        IReadOnlyList<string> OldFormIds,
        IReadOnlyList<string> OldControlIds,
        IReadOnlyList<NamedWeakReference> WeakReferences);

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

