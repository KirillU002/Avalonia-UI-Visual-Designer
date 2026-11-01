using Avalonia.Controls;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Avalonia.Threading;
using FormDesigner.Services;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
    private const string KnownGcRoots = "SOS verified: (1) MediaContextClock -> loading indicator animation -> PreviewWindow; (2) native UIA COM handle -> RootAutomationNode -> WindowAutomationPeer -> PreviewWindow. Stop owned loading animation and disconnect only the closing window provider.";
    private static int _activePreviewWindows, _rootDiagnosticLogged;
    private readonly string _previewId = Guid.NewGuid().ToString("N");
    private readonly string _previewLifecycleMode;
    private readonly List<PreviewLifecycleDiagnostic> _previewLifecycleDiagnostics = new();
    private bool _previewWasOpened, _previewLifecycleDisposed;
    private bool _automationCloseQueued;
    private bool _automationDisconnecting;
    private int _previewActivities;

    public static int ActivePreviewWindows => Volatile.Read(ref _activePreviewWindows);
    public string PreviewId => _previewId;
    public IReadOnlyList<PreviewLifecycleDiagnostic> LifecycleDiagnostics => _previewLifecycleDiagnostics;
    public event EventHandler<PreviewLifecycleDiagnostic>? LifecycleDiagnosticEmitted;

    private void TracePreviewLifecycle(string code, string reason = "")
    {
        var record = new PreviewLifecycleDiagnostic(code, _previewId, _previewLifecycleMode,
            ActivePreviewWindows, _previewActivities, reason);
        if (_previewLifecycleDiagnostics.Count == 64) _previewLifecycleDiagnostics.RemoveAt(0);
        _previewLifecycleDiagnostics.Add(record);
        NativeCrashDiagnostics.Write(code,_previewId,_previewLifecycleMode,ActivePreviewWindows,reason);
        if(code=="PREVIEW_LIFECYCLE_OPENED")NativeCrashDiagnostics.CaptureModules("first-preview-opened");
        Debug.WriteLine(code + " " + JsonSerializer.Serialize(record));
        LifecycleDiagnosticEmitted?.Invoke(this, record);
    }

    private void InitializePreviewLifecycle()
    {
        NativeCrashDiagnostics.InitializeFromEnvironment();
        Closed += PreviewWindow_Closed;
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, PreviewNativeLifetimeHook);
        TracePreviewLifecycle("PREVIEW_LIFECYCLE_CREATED");
        // A one-time description of the offline SOS finding, not runtime heap profiling.
        if (Interlocked.Exchange(ref _rootDiagnosticLogged, 1) == 0)
            TracePreviewLifecycle("PREVIEW_GC_ROOT_IDENTIFIED", KnownGcRoots);
    }

    private void PreviewLifecycleOpened()
    {
        if (_previewWasOpened) return;
        _previewWasOpened = true;
        Interlocked.Increment(ref _activePreviewWindows);
        TracePreviewLifecycle("PREVIEW_LIFECYCLE_OPENED");
    }

    private void PreviewWindow_Closed(object? sender, EventArgs e)
    {
        if (_databasePreviewClosed) return;
        _databasePreviewClosed = true;
        // Hiding an ancestor doesn't end the indeterminate template's infinite clock subscription.
        LoadingProgress.IsIndeterminate = false;
        _databasePreviewCancellation?.Cancel();
        _renderDocumentTimer.Stop();
        _isRenderDocumentScheduled = false;
        _previewBuildGeneration++;
        _builtPreviewControls.Clear();
        if (_previewWasOpened) Interlocked.Decrement(ref _activePreviewWindows);
        TracePreviewLifecycle("PREVIEW_LIFECYCLE_CLOSED");
        CompletePreviewLifetimeIfIdle();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Defer cross-thread SendMessage before raising cancellable Closing. Native UIA
        // disconnection cannot make outbound COM calls in that synchronous message context.
        if (!_databasePreviewClosed && PreviewAutomationLifetime.MustDeferClose(this))
        {
            e.Cancel = true;
            if (!_automationCloseQueued)
            {
                _automationCloseQueued = true;
                _previewActivities++;
                Dispatcher.UIThread.Post(() =>
                {
                    _automationCloseQueued = false;
                    try { Close(); }
                    finally { FinishPreviewActivity(); }
                });
            }
            return;
        }
        base.OnClosing(e);
        if (e.Cancel || _databasePreviewClosed) return;
        TracePreviewLifecycle("PREVIEW_LIFECYCLE_CLOSING");
    }

    private IntPtr PreviewNativeLifetimeHook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_automationDisconnecting && message == 0x003D) handled = true; // WM_GETOBJECT
        if (message != 0x0002) return IntPtr.Zero; // WM_DESTROY: closing really committed, HWND still exists.
        try
        {
            if (PreviewAutomationLifetime.CaptureDisconnect(this) is not { } disconnect) return IntPtr.Zero;
            // In 11.1.1 the provider needs the live HWND to resolve its host/runtime identity.
            // Closed is too late (E_FAIL). A native destroy hook also avoids disconnecting
            // an owned preview when its owner's later Closing handler cancels the operation.
            _automationDisconnecting = true;
            disconnect();
            TracePreviewLifecycle("PREVIEW_AUTOMATION_RELEASED");
        }
        catch (Exception ex) { TracePreviewLifecycle("PREVIEW_AUTOMATION_RELEASE_FAILED", $"{ex.GetType().Name}:0x{ex.HResult:X8}"); }
        return IntPtr.Zero;
    }

    private void FinishPreviewActivity()
    {
        _previewActivities--;
        CompletePreviewLifetimeIfIdle();
    }

    private void CompletePreviewLifetimeIfIdle()
    {
        if (!_databasePreviewClosed || _previewActivities != 0 || _previewLifecycleDisposed) return;
        _previewLifecycleDisposed = true;
        TracePreviewLifecycle("PREVIEW_LIFECYCLE_DISPOSED");
        BuildDiagnosticEmitted = null;
    }
}
