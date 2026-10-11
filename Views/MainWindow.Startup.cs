using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private Task _startupTask = Task.CompletedTask;
    private readonly DispatcherTimer _dispatcherHealthTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private long _lastDispatcherTick;
    private string _startupStage = "WindowCreated";
    private bool InputDiagnosticsEnabled => Environment.GetEnvironmentVariable("FORMDESIGNER_INPUT_DIAGNOSTICS") == "1"
        || _appSettings.Advanced.EnableTraceDiagnostics;

    private async Task InitializeWorkspaceOnStartupAsync()
    {
        LogStartupEvent("APP_STARTUP_BEGIN");
        try
        {
            _startupStage = "WindowState";
            ApplySessionWindowState(_appSettings.Session);
            if (UsesHostDocumentBuffer)
            {
                VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryDiagnostics,
                    "HOST_BUFFER_LIFECYCLE", "Startup recovery and disk reopen disabled; awaiting host snapshot.");
                return;
            }
            _startupStage = "Recovery";
            var recoveryHandled = await CheckRecoveryOnStartupAsync();
            _startupStage = "LastWorkspace";
            if (!recoveryHandled)
                await TryRestoreLastSessionDocumentAsync();
            _startupStage = "Viewport";
            ApplySessionViewportState(_appSettings.Session);
        }
        catch (Exception ex)
        {
            LogStartupEvent("APP_STARTUP_FAILED", $"exception={ex}", WorkspaceLogLevel.Error);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Startup restore failed", ex.Message, isPersistent: true);
        }
        finally
        {
            if (!UsesHostDocumentBuffer && !_isCloseConfirmed) _autosaveTimer.Start();
            _startupStage = "Interactive";
            LogStartupEvent("APP_STARTUP_END");
            // This callback proves that the dispatcher processes work after restore.
            Dispatcher.UIThread.Post(() => LogStartupEvent("UI_DISPATCHER_HEALTH", "state=Responsive; source=StartupCallback"), DispatcherPriority.Background);
            if (InputDiagnosticsEnabled && !_isCloseConfirmed)
            {
                _lastDispatcherTick = Stopwatch.GetTimestamp();
                _dispatcherHealthTimer.Start();
            }
        }
    }

    private void InitializeInputDiagnostics()
    {
        AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!InputDiagnosticsEnabled) return;
            var target = e.Source as Control;
            LogStartupEvent("MAINWINDOW_INPUT_RECEIVED",
                $"event=PointerPressed; target={target?.GetType().Name}:{target?.Name}; position={e.GetPosition(this)}; handled={e.Handled}; capture={e.Pointer.Captured}; focus={GetFocusedElementDebugName()}");
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (InputDiagnosticsEnabled) LogStartupEvent("MAINWINDOW_INPUT_RECEIVED", $"event=KeyDown; modifiers={e.KeyModifiers}; target={e.Source?.GetType().Name}");
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        foreach (var name in new[] { "StartScreenOverlay", "CommandPaletteOverlay", "BusyOverlay" })
        {
            var overlay = this.FindControl<Border>(name)!;
            overlay.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                    LogStartupEvent(overlay.IsVisible ? "OVERLAY_SHOWN" : "OVERLAY_HIDDEN", $"overlay={name}");
            };
        }
        _dispatcherHealthTimer.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            LogStartupEvent("UI_DISPATCHER_HEALTH", $"state=Responsive; intervalMs={Stopwatch.GetElapsedTime(_lastDispatcherTick, now).TotalMilliseconds:F0}");
            _lastDispatcherTick = now;
        };
    }

    private void LogStartupEvent(string eventName, string details = "", WorkspaceLogLevel level = WorkspaceLogLevel.Info)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var overlays = $"startScreen={this.FindControl<Border>("StartScreenOverlay")?.IsVisible}; palette={vm.IsCommandPaletteOpen}; busyOverlay={this.FindControl<Border>("BusyOverlay")?.IsVisible}";
        vm.LogWorkspace(level, MainWindowViewModel.OutputCategoryDiagnostics, eventName,
            $"stage={_startupStage}; path={_appSettings.Session.LastProjectPath}; busy={vm.IsBusy}; {overlays}; {details}");
    }

    private void StopWindowTimers()
    {
        _autosaveTimer.Stop();
        _previewFilterRefreshTimer.Stop();
        _settingsSaveTimer.Stop();
        _designerRenderTimer.Stop();
        _dispatcherHealthTimer.Stop();
    }
}
