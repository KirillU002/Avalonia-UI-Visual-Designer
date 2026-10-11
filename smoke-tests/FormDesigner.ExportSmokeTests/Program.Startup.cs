using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.Views;
using FormDesigner.ViewModels;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindow CreateSmokeWindow(FormDesigner.ViewModels.MainWindowViewModel viewModel)
    {
        var host = new TestDesignerHostServices(useUiScheduler: true);
        // A real window applies preferences to its VM. Seed isolated preferences
        // from the fixture so neither user settings nor defaults rewrite it.
        var settings = new AppSettingsModel
        {
            General = viewModel.CaptureGeneralSettings(),
            CanvasEditor = viewModel.CaptureCanvasEditorSettings(),
            Interface = viewModel.CaptureInterfaceSettings(),
            UiDensity = viewModel.CaptureUiDensitySettings(),
            Preview = viewModel.CapturePreviewSettings(),
            BuildAndLogs = viewModel.CaptureBuildAndLogsSettings(),
            SqlServer = viewModel.CaptureSqlServerSettings(),
            Advanced = viewModel.CaptureAdvancedSettings(),
            ExportCache = viewModel.CaptureExportCache(),
            PropertyGrid = viewModel.CapturePropertyGridSettings(),
            PropertyGridFavorites = viewModel.CapturePropertyGridFavorites(),
            PropertyGridCollapsedCategories = viewModel.CapturePropertyGridCollapsedCategories()
        };
        settings.Session.WorkspaceMode = viewModel.WorkspaceMode;
        settings.Session.EditorShell = viewModel.CaptureEditorShellLayoutState();
        settings.Session.ReopenLastWorkspaceOnStartup = false;
        Pump(new AppSettingsService(host.Paths.UserDataDirectory).SaveAsync(settings));
        var window = new MainWindow(host) { DataContext = viewModel };
        if (viewModel.IncludeSampleData != settings.ExportCache.IncludeSampleData)
            throw new InvalidOperationException("Smoke window preferences changed the fixture's sample data mode.");
        SmokeWindows.Add(window);
        return window;
    }

    private static void AssertStartupMissingWorkspaceInput(SmokeContext context)
    {
        var host = new TestDesignerHostServices();
        var settings = new AppSettingsModel();
        settings.Session.LastProjectPath = "Phase4.axaml";
        Pump(new AppSettingsService(host.Paths.UserDataDirectory).SaveAsync(settings));
        var window = new MainWindow(host) { DataContext = context.ViewModel, Width = 1200, Height = 800 };
        try
        {
            window.Show();
            using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            Dispatcher.UIThread.MainLoop(wait.Token);
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.Command, context.ViewModel.NewEditorCommand));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Console.WriteLine($"STARTUP_REPRO busy={context.ViewModel.IsBusy}; startScreen={context.ViewModel.IsStartScreenVisible}; newEnabled={button.IsEffectivelyEnabled}; hit={hit?.GetType().Name}; ancestors={string.Join('/', hit?.GetVisualAncestors().OfType<Control>().Select(c => c.GetType().Name + ':' + c.Name) ?? [])}");
            if (hit != button && hit?.GetVisualAncestors().Contains(button) != true)
                throw new InvalidOperationException("Startup overlay intercepts pointer hit-test over New Project.");
        }
        finally { window.CloseForExternalHost(); }
    }

    private static void AssertStartupScenario(SmokeContext context)
    {
        var name = context.Scenario.Name;
        var host = new TestDesignerHostServices();
        var settingsService = new AppSettingsService(host.Paths.UserDataDirectory);
        var settings = new AppSettingsModel();
        Directory.CreateDirectory(host.Paths.TempDirectory);
        var sourcePath = Path.Combine(host.Paths.TempDirectory, "workspace.formdesigner.json");
        var json = context.ViewModel.ExportDocumentJson();
        var axaml = "<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Canvas><Button x:Name=\"Restored\" Content=\"Hello\" Width=\"100\" Height=\"40\"/></Canvas></Window>";
        var missing = name is not ("StartupNoWorkspace" or "StartupExistingJsonWorkspace" or "StartupExistingAxamlWorkspace");
        settings.Session.LastProjectPath = missing ? "Phase4.axaml" : name == "StartupNoWorkspace" ? "" : sourcePath;
        if (name == "StartupExistingJsonWorkspace") File.WriteAllText(sourcePath, json);
        if (name == "StartupExistingAxamlWorkspace")
        {
            sourcePath = Path.ChangeExtension(sourcePath, ".axaml");
            File.WriteAllText(sourcePath, axaml);
            settings.Session.LastProjectPath = sourcePath;
        }
        if (name.Contains("Corrupt"))
        {
            sourcePath = name.Contains("Axaml") ? Path.ChangeExtension(sourcePath, ".axaml") : sourcePath;
            File.WriteAllText(sourcePath, name.Contains("Axaml") ? "<Window><" : "{corrupt");
            settings.Session.LastProjectPath = sourcePath;
        }
        settings.RecentFiles.Add(new RecentFileModel { FilePath = settings.Session.LastProjectPath, IsPinned = true });
        Pump(settingsService.SaveAsync(settings));
        var previousDiagnosticFlag = Environment.GetEnvironmentVariable("FORMDESIGNER_INPUT_DIAGNOSTICS");
        if (name.Contains("Diagnostics")) Environment.SetEnvironmentVariable("FORMDESIGNER_INPUT_DIAGNOSTICS", "1");
        var initialVm = CreateViewModel(name);
        initialVm.NewDocumentCommand.Execute(null);
        var window = new MainWindow(host) { DataContext = initialVm, Width = 1200, Height = 800 };
        var vm = (MainWindowViewModel)window.DataContext!;
        try
        {
            window.Show();
            Pump((Task)typeof(MainWindow).GetField("_startupTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
            SettleStartupLayout();
            if (vm.IsBusy || vm.IsStartScreenVisible) throw new InvalidOperationException("Restore left a blocking state.");
            var newButton = StartupButton(window, vm.NewEditorCommand);
            AssertStartupHitTarget(window, newButton);
            if (name == "StartupExistingJsonWorkspace") RequireEqual(context.ViewModel.Controls.Count, vm.Controls.Count, "JSON restore lost controls.");
            if (name == "StartupExistingAxamlWorkspace")
            {
                if (!vm.IsAxamlRoundTripDocument || vm.Controls.Single().Name != "Restored") throw new InvalidOperationException("AXAML used the wrong restore lifecycle.");
                RequireEqual(axaml, vm.CreateActiveAxamlPatch().PatchedText, "No-edit AXAML restore changed the source.");
            }
            if (name.Contains("Corrupt"))
            {
                if (!vm.WorkspaceToasts.Any(t => t.Title == "Reopen failed")) throw new InvalidOperationException("Corruption was not reported.");
                if (!vm.OutputEntries.Any(e => e.Message == "WORKSPACE_RESTORE_FAILED" && e.Details.Contains("Exception"))) throw new InvalidOperationException("Missing restore exception diagnostics.");
                if (!File.Exists(sourcePath)) throw new InvalidOperationException("Corrupt document was deleted.");
            }
            if (name == "StartupNewAfterRestoreFailure")
            {
                var session = vm.DocumentSessionId;
                StartupClick(window, newButton);
                SettleStartupLayout();
                if (session == vm.DocumentSessionId || vm.Controls.Count != 0) throw new InvalidOperationException("Actual pointer click did not create New Project.");
            }
            if (name == "StartupOpenAfterRestoreFailure")
            {
                host.FilePicker.OpenFiles.Enqueue(new TestDesignerHostFile("Opened.formdesigner.json", sourcePath, json));
                StartupClick(window, StartupButton(window, vm.OpenEditorCommand));
                SettleStartupLayout();
                RequireEqual(1, host.FilePicker.OpenRequests.Count, "Actual pointer click did not open the picker.");
                RequireEqual(context.ViewModel.Controls.Count, vm.Controls.Count, "Open Project did not load picked JSON.");
            }
            if (name == "StartupToastDismissAndMenuHitTest")
            {
                var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "startup");
                Directory.CreateDirectory(output);
                using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1200, 800)))
                {
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, "missing-workspace-interactive.png"));
                }
                var toast = vm.WorkspaceToasts.Single();
                var dismiss = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.CommandParameter, toast));
                AssertStartupHitTarget(window, dismiss);
                StartupClick(window, dismiss);
                if (vm.WorkspaceToasts.Count != 0) throw new InvalidOperationException("Toast X did not receive pointer input.");
                AssertStartupHitTarget(window, newButton);
            }
            if (name == "StartupKeyboardAndModeInput")
            {
                var session = vm.DocumentSessionId;
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Control });
                SettleStartupLayout();
                if (session == vm.DocumentSessionId) throw new InvalidOperationException("Ctrl+N did not dispatch New Project.");
                vm.WorkspaceMode = "Плагины";
                SettleStartupLayout();
                vm.WorkspaceMode = "Дизайн";
                SettleStartupLayout();
                AssertStartupHitTarget(window, StartupButton(window, vm.OpenEditorCommand));
                if (window.FindControl<DesignerSurface>("DesignerSurface")?.IsEffectivelyVisible != true)
                    throw new InvalidOperationException("Mode switching detached the shared surface.");
            }
            if (name == "StartupStartScreenHasDismissAction")
            {
                vm.IsStartScreenVisible = true;
                SettleStartupLayout();
                var dismiss = StartupButton(window, vm.CloseStartScreenCommand);
                AssertStartupHitTarget(window, dismiss);
                StartupClick(window, dismiss);
                if (vm.IsStartScreenVisible) throw new InvalidOperationException("Start screen has an undismissable backdrop.");
                AssertStartupHitTarget(window, newButton);
            }
            if (name == "StartupDispatcherAndInputDiagnostics")
            {
                StartupClick(window, newButton);
                SettleStartupLayout();
                foreach (var evt in new[] { "APP_STARTUP_BEGIN", "APP_STARTUP_END", "WORKSPACE_RESTORE_BEGIN", "WORKSPACE_RESTORE_FAILED", "WORKSPACE_RESTORE_END", "UI_DISPATCHER_HEALTH", "MAINWINDOW_INPUT_RECEIVED", "COMMAND_EXECUTION_STARTED", "COMMAND_EXECUTION_FINISHED" })
                    if (!vm.OutputEntries.Any(e => e.Message == evt)) throw new InvalidOperationException("Missing diagnostic: " + evt);
                var dispatched = false;
                Dispatcher.UIThread.Post(() => dispatched = true, DispatcherPriority.Background);
                SettleStartupLayout();
                if (!dispatched) throw new InvalidOperationException("Dispatcher stopped processing work.");
                window.Width += 100;
                SettleStartupLayout();
                RequireEqual(1300d, window.Bounds.Width, "Native window did not resize.");
            }
            if (name == "StartupBusyResetAfterException")
            {
                var injected = false;
                vm.PropertyChanged += (_, e) =>
                {
                    if (!injected && e.PropertyName == nameof(MainWindowViewModel.IsBusy) && vm.IsBusy)
                    {
                        injected = true;
                        throw new InvalidOperationException("Injected startup diagnostic operation failure.");
                    }
                };
                host.FilePicker.OpenFiles.Enqueue(new TestDesignerHostFile("missing.dll", Path.Combine(context.ProjectPath, "missing.dll")));
                typeof(MainWindow).GetMethod("ImportBindingSourcesFromAssemblyButton_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [window, new Avalonia.Interactivity.RoutedEventArgs()]);
                SettleStartupLayout();
                if (!injected || vm.IsBusy) throw new InvalidOperationException("Failed DLL import left busy overlay active.");
                AssertStartupHitTarget(window, newButton);
            }
            if (name == "StartupRepeatAfterRestoreFailure")
            {
                window.CloseForExternalHost();
                var second = new MainWindow(host) { DataContext = CreateViewModel(name), Width = 1200, Height = 800 };
                try
                {
                    second.Show();
                    Pump((Task)typeof(MainWindow).GetField("_startupTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(second)!);
                    SettleStartupLayout();
                    AssertStartupHitTarget(second, StartupButton(second, ((MainWindowViewModel)second.DataContext!).NewEditorCommand));
                    RequireEqual("Phase4.axaml", settingsService.Load().Session.LastProjectPath, "Missing workspace setting was removed.");
                }
                finally { second.CloseForExternalHost(); }
            }
            if (name == "StartupInterruptedSessionRecovery")
            {
                var recovery = new AutosaveRecoveryService(host.Paths.RecoveryDirectory);
                Pump(recovery.SaveDraftAsync(new RecoveryDraftFileModel { DocumentJson = json }));
                var task = (Task<bool>)typeof(MainWindow).GetMethod("CheckRecoveryOnStartupAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
                SettleStartupLayout();
                var dialog = window.OwnedWindows.OfType<RecoveryWindow>().Single();
                dialog.Close(RecoveryDialogResult.OpenNormally);
                Pump(task);
                SettleStartupLayout();
                if (!File.Exists(recovery.RecoveryFilePath)) throw new InvalidOperationException("Deferred recovery draft was deleted.");
                AssertStartupHitTarget(window, newButton);
            }
            if (name == "StartupSmokeSettingsIsolation")
            {
                var actualSettings = new AppSettingsService().SettingsFilePath;
                var before = File.Exists(actualSettings) ? File.ReadAllBytes(actualSettings) : null;
                var surface = CreateMainWindowDesignerSurface(context);
                context.ViewModel.LoadAxamlImportedDocument(new FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportService().Import(axaml, "Phase4.axaml"), "Phase4.axaml");
                var smokeWindow = surface.GetLogicalAncestors().OfType<MainWindow>().First();
                Pump((Task)typeof(MainWindow).GetMethod("SaveAppSettingsNowAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(smokeWindow, null)!);
                if (before is null ? File.Exists(actualSettings) : !before.SequenceEqual(File.ReadAllBytes(actualSettings))) throw new InvalidOperationException("Smoke test changed real user settings.");
            }
        }
        finally
        {
            window.CloseForExternalHost();
            Environment.SetEnvironmentVariable("FORMDESIGNER_INPUT_DIAGNOSTICS", previousDiagnosticFlag);
        }
    }

    private static void SettleStartupLayout()
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
        Dispatcher.UIThread.MainLoop(wait.Token);
    }

    private static Button StartupButton(MainWindow window, System.Windows.Input.ICommand? command) =>
        window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, command));

    private static Point StartupButtonPoint(MainWindow window, Button button) =>
        button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

    private static void AssertStartupHitTarget(MainWindow window, Button button)
    {
        var hit = window.InputHitTest(StartupButtonPoint(window, button)) as Visual;
        if (hit != button && hit?.GetVisualAncestors().Contains(button) != true)
            throw new InvalidOperationException($"Pointer target for {button.Command} was {hit?.GetType().Name}:{(hit as Control)?.Name}.");
    }

    private static void StartupClick(MainWindow window, Button button)
    {
        AssertStartupHitTarget(window, button);
        // Avalonia's native input hook is internal API; keep reflection test-only.
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        var mouse = (IInputDevice)Activator.CreateInstance(typeof(MouseDevice), flags, null, new object[] { pointer }, null)!;
        var point = StartupButtonPoint(window, button);
        var callback = (Action<RawInputEventArgs>)typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input")!.GetValue(window.PlatformImpl)!;
        foreach (var type in new[] { RawPointerEventType.LeftButtonDown, RawPointerEventType.LeftButtonUp })
        {
            var raw = (RawInputEventArgs)Activator.CreateInstance(typeof(RawPointerEventArgs), flags, null, [mouse, 1UL, window, type, point, RawInputModifiers.None], null)!;
            callback(raw);
        }
    }
}
