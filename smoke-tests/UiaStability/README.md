# UI Automation stability diagnostics — Phase 3B.1

Windows-only diagnostic projects; production NuGet/TFM are unchanged. Build the
Release smoke runner, `UiaClient` (net8.0) and `UiaTreeClient` (net8.0-windows;
requires the installed Windows Desktop 8 runtime). The latter is an external MTA
UIAutomation client, not a WPF application window or a Designer dependency.

Run from the repository root, sequentially after builds:

```powershell
dotnet build smoke-tests/UiaStability/UiaClient -c Release
dotnet build smoke-tests/UiaStability/UiaTreeClient -c Release
dotnet build smoke-tests/FormDesigner.ExportSmokeTests -c Release -m:1
pwsh -NoProfile -File smoke-tests/UiaStability/RunStress.ps1 -Scenarios plain -Cycles 100
pwsh -NoProfile -File smoke-tests/UiaStability/RunStress.ps1 -Scenarios preview-axaml -Cycles 100 -Client
```

For descendant queries use `-ClientDll
smoke-tests/UiaStability/UiaTreeClient/bin/Release/net8.0-windows/UiaTreeClient.dll`
with `-Client`. Scenarios: `plain`, `preview-legacy`, `preview-axaml`, `editor`,
`popup`, `menu`, `mutations`, `undo`, `forms`, `project`. Each is a fresh process,
100 cycles by default, with JSON checkpoints at 25/50/75/100, memory/handles/
threads, exit code and a bounded native journal. `Runs` counts independent
observations; it does not retry or replace failures. A timeout or missing final
assertion/result is a failure, even if earlier checkpoints passed.

No-client means **ambient UIA**, not “UI Automation disabled”. Both clients verify
the HWND's owning PID, query only the test window, and do not read user text or
invoke actions. Root client intentionally uses deprecated node APIs to compare
with historical diagnostics. Tree client queries up to 128 real descendants
at depth 8 and ValuePattern availability while the owner closes.

`UIA_STRESS_RELEASE_CLIENT_FIRST=1` ends the **owned client lease** before the final
server GC assertion. This is important: retaining AutomationElements in an active
client can retain closed server controls. Preserve both connected-client and
released-client observations; don't rename a former retention failure a pass.
Collection session and active-context assertions stay enabled. Plain/collection
Window retention is recorded separately from Preview collectibility. Test-only
GC does not run in production.

`UIA_STRESS_HOLD_MEMORY=1` writes `dump-ready.json` and pumps for at most 60 seconds
until `release.txt`. The existing smoke dump-on-retained opt-in is also supported.
Collect only the owned PID. Dumps are ignored artifacts and must not be committed.

## Native dump analysis

`DumpAnalyzer` is a minimal read-only host of installed Microsoft DbgEng. Its
vtable slots follow the official `dbgeng.h`. Build with net8.0, then pass dump,
output log and a command text file, one command per line. Example:

```text
.sympath <directory containing the exact matching Microsoft PDB>
.reload /f uiautomationcore.dll
.ecxr
kv
lmv m uiautomationcore
~*k 4
```

When the OS debugger cannot load symbols, use its local copy with installed SDK
`dbghelp.dll`/`symsrv.dll` next to the diagnostic executable. Never replace system
DLLs. Match PDB GUID/age to the dump module; nearest export names are insufficient.
Sources: [DebugCreate](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-debugcreate),
[OpenDumpFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugclient-opendumpfile),
[WaitForEvent](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugcontrol-waitforevent).

## Rejected disconnect experiment — opt-in only

`Experiments/PreviewAutomationLifetime.children.cs.txt` records the **rejected**
attempt to disconnect each existing descendant provider in WM_DESTROY before
the root. It caused a native AV and an E_FAIL/NULL-provider error under incoming
UIA queries. It is not compiled, a proposed workaround, or an ordinary CI test.

`ReproduceRejectedExperiment.ps1 -AllowKnownCrash` builds a fresh `git archive`
under ignored artifacts, copies the diagnostic harness and this one experimental
adapter into that isolated copy, and runs only 3 Preview cycles with a Tree client.
It never patches the checkout. Timing-dependent outcomes can differ; keep every
exit/checkpoint, don't retry until green. Existing WER configuration may capture
the failing process; the script changes no registry/debugger policy.

The source experiment was exercised during the audit. The isolated convenience
script is supplied separately; see the report for exactly which invocation was
verified. Native signatures differ: historical `+0x47593` is not automatically
the experimental `+0x5d615`.
