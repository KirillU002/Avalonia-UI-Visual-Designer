# Standalone Startup Input Fix

## Root Cause

The unavailable workspace was a trigger, not a dispatcher deadlock or a busy
command state. `MainWindow.TryRestoreLastSessionDocumentAsync` used to set
`IsStartScreenVisible = true` both for a missing file and for a load exception.
The root start-screen Border spans all three shell rows and has a non-null
background, so Avalonia includes it in pointer hit testing. Its inner panel,
including the Close/New/Open buttons, was hard-coded `IsVisible="False"`.
This left a full-window input-blocking backdrop without a dismissible dialog.

Before the fix, an isolated native Debug window with a missing `Phase4.axaml`
produced:

```text
STARTUP_REPRO busy=False; startScreen=True; newEnabled=True; hit=Border
Startup overlay intercepts pointer hit-test over New Project.
```

After the fix the same test reports:

```text
STARTUP_REPRO busy=False; startScreen=False; newEnabled=True; hit=AccessText
ancestors=ContentPresenter/Button/Grid/Border/Grid/.../MainWindow
```

The real dispatcher continues processing work, and the native window resizes.
New/Open are enabled. The toast is separately right/top aligned with a 76px
top margin; its X receives pointer events and its bounds do not cover the menu.
There was no evidence requiring a dispatcher rewrite, a global busy reset,
or removing notifications. The only `.Result` in MainWindow belongs to a
completed color-flyout continuation, not startup restore.

## Regression History

- `3e1afdc` introduced workspace restore and the start-screen backdrop.
- `c0904311e2e060bd606ed6496ccc97138236e859`, **2026-05-28**, "Сделан UX cleanup",
  added `IsVisible="False"` to the inner start-screen Border but left the outer
  binding and hit testing intact. `git blame` identifies the exact offending
  line. This predates the AXAML Phase 4/5 work.
- `3170d90` introduced the shared-surface smoke helper that constructed
  `new MainWindow { DataContext = context.ViewModel }` with production services.
- `b38d9ef` added the Phase 4 smoke document label `Phase4.axaml`.

## Why Phase4.axaml

The actual `%LOCALAPPDATA%/FormDesigner/app-settings.json` was inspected and
contained `Session.LastProjectPath = "Phase4.axaml"`, with startup reopen enabled.
No such workspace exists at that relative path. Restore prefers LastProjectPath
over LastDocumentPath and therefore repeatedly attempted that missing file.

`Program.AxamlPhase4.Phase4Document` uses that exact label as its default source
path. Previously, its default shared-surface helper created a standalone window
with real user-data paths. DataContext/session/viewport changes schedule the
one-second settings-save timer; `CaptureSessionState` copies the test source
path into LastProjectPath. Thus smoke execution could pollute production
settings even when the window was never shown. There is no historical writer
audit for the particular settings file, but the saved value, source label and
write path match this mechanism.

The fix does **not** delete or reset user settings, recent files, pinned files,
documents or drafts to suppress the warning. Existing missing paths can still
produce the informational warning; they no longer disable the application.

## Changes

- `Views/MainWindow.axaml`: restore visibility of the explicit start-screen
  panel; name the start-screen, command-palette, busy and toast hosts for
  diagnostics. No theme or global styles changed.
- `Views/MainWindow.axaml.cs`: unavailable/corrupt last workspaces produce
  nonmodal toasts instead of opening the start screen. Restore has paired
  begin/end diagnostics. Open/New operations log their asynchronous completion
  and failures. All window timers stop when closing, including hidden test hosts.
- `Views/MainWindow.Startup.cs`: bounded startup lifecycle with exception
  diagnostics/finalization, a post-startup dispatcher callback, optional
  dispatcher ticks and pointer/key target tracing. HostBuffer retains its
  existing rule: no standalone disk reopen/recovery.
- `ViewModels/MainWindowViewModel.cs`: command dispatch diagnostics. A dispatch
  completion is labeled `scope=dispatch`; asynchronous New/Open completion is
  separately labeled `scope=operation`, so they are not confused.
- Smoke runner: isolated host services for view models and standalone windows,
  tracked window cleanup, and startup/input tests. No real user settings are
  used by the shared-surface helper anymore. Ordinary smoke VMs still use the
  real Avalonia UI scheduler. Isolated window preferences are seeded from the
  configured fixture rather than overwriting it with empty defaults.

Startup restore never acquired the global busy state, so it must not reset
another operation's busy flag indiscriminately. Existing busy-operation error
cleanup is tested using an injected exception after BeginBusy.

## Diagnostics

Always available in the Diagnostics Output:

```text
APP_STARTUP_BEGIN / APP_STARTUP_END / APP_STARTUP_FAILED
WORKSPACE_RESTORE_BEGIN / WORKSPACE_RESTORE_FAILED / WORKSPACE_RESTORE_END
UI_DISPATCHER_HEALTH
BUSY_STATE_CHANGED
OVERLAY_SHOWN / OVERLAY_HIDDEN
COMMAND_EXECUTION_STARTED / COMMAND_EXECUTION_FINISHED / COMMAND_EXECUTION_FAILED
```

Events include startup stage, restore path, busy and overlay states. Load errors
include exception type/message/stack via the exception's full representation.
The missing-file branch records `reason=FileNotFound` and whether the path is
fully qualified; there is no exception to fabricate a stack trace for.

For pointer targets, capture, focus and keyboard delivery, enable
`FORMDESIGNER_INPUT_DIAGNOSTICS=1` before launch, or the existing trace-diagnostics
setting. This adds `MAINWINDOW_INPUT_RECEIVED` and two-second dispatcher health
intervals. Mouse movement and typed text are not logged. Disable the environment
flag after investigation to return to ordinary logging.

## Regression Tests

`Program.Startup.cs` adds 16 scenarios covering no workspace, valid JSON/AXAML,
missing AXAML, corrupt JSON/AXAML, New/Open after failure, toast dismissal and
menu hit testing, explicit start-screen dismissal, dispatcher/input diagnostics,
busy error cleanup, repeat launch, deferred interrupted-session recovery,
smoke settings isolation, keyboard dispatch and workspace mode switching.

Native Avalonia windows are shown. New/Open/toast/start-screen tests send raw
pointer down/up through the actual platform input hook, which performs native
Avalonia hit testing and button command dispatch. They do not call ICommand
directly as a substitute for pointer delivery. Reflection against internal
Avalonia raw-input APIs is test-only. The keyboard test exercises the routed
Ctrl+N path; it is not an OS keyboard-injection test.

The real-user settings isolation test compares file bytes before/after saving
the test AXAML session. Corrupt document tests verify that source files remain.
Deferred recovery verifies that the draft remains available after the modal
recovery choice is dismissed with OpenNormally.

An intermediate isolation attempt exposed an existing test dependency:
MainWindow applies loaded preferences to its ViewModel. Empty temporary
preferences changed the Eremex fixture's `IncludeSampleData=True` to `False`,
leaving its exported runtime preview without rows. The final helper captures
fixture preferences into the temporary settings directory before creating the
window and asserts that sample-data mode is unchanged. Eremex production code
was not modified; its five scenarios passed after correcting the harness.

Screenshot: `artifacts/diagnostics/startup/missing-workspace-interactive.png`.
Before/after proof: `artifacts/startup-before.log`, `artifacts/startup-after.log`.

## Validation

- Startup suite: **16/16 passed**.
- Solution Debug build: **0 errors**, existing VSSDK `NU1603` warning only.
- Smoke runner build: **0 warnings/errors**.
- XML audit: all 12 tracked project/props/targets files have unchanged NuGet
  declarations and TargetFramework values.
- VSIX archive: 250 VsHost runtime files; required exe/dll/deps/runtimeconfig
  present; packaged FormDesigner/VsHost DLL hashes match the Debug build.
  No Avalonia/Eremex/FormDesigner visual assemblies at the VSIX root.
- Final regression: **189 unique scenarios, 201 successful executions, 0 failures**
  across 15 suite filters. Overlapping filters do not count as new scenarios.
  Only `artifacts/startup-final-*.log` are included in these totals; the earlier
  reproducer and intermediate failed harness runs are excluded.

| Final Suite | Passed |
| --- | ---: |
| Startup/input/restore | 16/16 |
| AXAML (including two overlapping startup AXAML cases) | 107/107 |
| DesignerSurface | 14/14 |
| VSIX bridge/packaging | 5/5 |
| VsHost/IPC | 10/10 |
| Multi Form | 13/13 |
| New Project | 7/7 |
| PropertyGrid | 7/7 |
| Inspector | 12/12 |
| Eremex vertical slices | 5/5 |
| SimpleFormExport | 1/1 |
| GridLayoutExport | 1/1 |
| RuntimePreviewCanLoadSimpleWindow | 1/1 |
| Net8RuntimeBaseline | 1/1 |
| ExportToProjectBuildValidation | 1/1 |

The genuine `Views/MainWindow.axaml` stress import still reports 1470/1473,
795 partial, 3 opaque, 3 skipped boundaries; the Phase 5 native-controls check
reports 31/31 present and 31/31 with nonzero bounds. The importer, source map,
patch writer, JSON model and IPC architecture were not changed.

VSIX version remains **0.1.17**. The rebuilt package is
`AvaloniaDesigner.VSIX/bin/Debug/net472/AvaloniaDesigner.VSIX.vsix`.

## Changed Files

- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`
- `Views/MainWindow.Startup.cs`
- `ViewModels/MainWindowViewModel.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.cs`
- `smoke-tests/FormDesigner.ExportSmokeTests/Program.Startup.cs`
- `Docs/StandaloneStartupInputFix.md`

## Manual Check And Limitations

Run the rebuilt Debug standalone executable or `dotnet run --no-build --project
FormDesigner.csproj`. With the existing missing workspace setting still present:

1. Close the warning using X and click New Project.
2. Open an existing JSON project, switch modes, select/edit a control.
3. Add a second form, launch Preview and export.
4. Resize the window and repeat the launch.

The Windows capture helper timed out while capturing the independently launched
Debug application. Native shown-window tests and an Avalonia-rendered screenshot
were used instead. This is not a claim of a complete manual mouse/keyboard run
or of an installed extension being manually verified inside Visual Studio.
Arbitrary large/network workspace startup performance is outside this fix.
