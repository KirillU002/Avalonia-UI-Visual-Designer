# UIA provider lifetime — minimal reproducer and evidence

This is a diagnostic observation, **not a claim that Avalonia or Windows is defective**. The observed managed Window survives while an external UI Automation client retains provider references and becomes collectible after that client ends. The historical native `ProviderCallouts::GetPatternProvider` AV is a separate unresolved issue.

Environment: Avalonia 11.1.1.0, .NET 8.0.32 x64, Windows 10 build 19045. The probe has only Avalonia.Desktop/Fluent dependencies. It contains no DesignerSurface, PropertyGrid, SQL, Export or plugins. No accessibility disable, replacement provider or foreign COM release is used.

Build and run the existing portable test client/probe:

```powershell
dotnet build smoke-tests/UiaStability/MinimalAvalonia -c Release
dotnet build smoke-tests/UiaStability/UiaTreeClient -c Release
pwsh -File smoke-tests/UiaStability/RunCaptured.ps1 `
  -Output artifacts/<fresh-directory> -Scenario differential -Cycles 20 -Client `
  -ClientDll smoke-tests/UiaStability/UiaTreeClient/bin/Release/net8.0-windows/UiaTreeClient.dll
```

`ConnectedAlive` is sampled while the owned UIA process is still alive; `ReleasedAlive` after STOP, process exit, dispatcher drain and the original full GC. A successful process exit means the observation completed, not that `ConnectedAlive` must be zero. Inspect the counts. Ambient external clients are uncontrolled; “without our client” does not mean UIA disabled.

For a forensic pause, set `UIA_PROBE_FORENSIC_HOLD=1`. The probe publishes `hold-ready.json` after its original checkpoint. Publish `sample.request` or `release.txt` through `WriteProbeSignal.ps1`; requests use atomic rename, without overwriting an unconsumed request. Never write a request in place while the probe can read/delete it.

For a single read-only external snapshot, set `UIA_PROBE_WAIT_CLOSE=1` and cycles=1. The probe exposes a test Window with title `UIA plain lifetime probe`, waits for `close.request`, then closes it and enters the forensic hold. The interactive variant sets ShowInTaskbar only to make this diagnostic window selectable. Production/normal probe styling is unchanged.

Observed in this investigation:

1. Existing diagnostic computer-use client remained alive after an earlier UIA snapshot. A clean 20-window probe, without Designer code or an owned probe client, had **18/20** retained. In the same still-running probe, ending our own diagnostic REPL/client and requesting GC produced **0/20**.
2. A newly started helper that only enumerated windows produced **0/20**. Merely loading UIAutomationCore or finding a client process is insufficient evidence of ownership.
3. A real accessibility tree snapshot of one test Window produced **1/1** retained, including a second GC checkpoint while the client remained alive. The heap contains two native ref-counted roots: RootAutomationNode → WindowAutomationPeer → Window; and AutomationNode → TextBoxAutomationPeer → cached child peers → visual/composition target → Window.
4. Repeating the snapshot/close experiment with the atomic request protocol produced **1/1 → 0/1** after ending that same owned client. The after heap contains no `Avalonia.Controls.Window` instance; the server process remained alive and exited normally when released.

The client was ended through its owning REPL lifecycle API, not by killing unrelated desktop applications. Windows accessibility remained enabled. No experiment releases another component's COM pointers. Server heap CCW counts establish strong references but do not identify every remote owner; the client-lifetime comparison supplies that evidence for the controlled experiment. These data do not establish the precise owner of every historical failure or a permanent production leak.

Evidence remains local in ignored `artifacts/universal-phase4e`: original-dump-quality.json, old-ccw.log, own-client-snapshot/gcroot.log, atomic-snapshot/TextBox/hold-samples.jsonl and after-heap-stats.log; unique dumps, SHA-256 and per-process metadata. Dumps are not committed or published. This technical note has not been sent upstream.

After any interactive diagnostic UIA session, complete that session's owned client lifecycle before a post-close GC gate. Do not silently terminate arbitrary accessibility clients or change GC/assertions to accommodate them. Keep a separate connected-client observation and label external retention explicitly.
