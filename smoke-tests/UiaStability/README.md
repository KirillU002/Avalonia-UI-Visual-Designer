# UI Automation stability diagnostics — Phases 3B.1 / 3B.2

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

## Full process-local capture — Phase 3B.2

**Preserve original evidence before any experiment.** Copy the explicitly relevant
WER dumps into a separate ignored local evidence directory and record SHA-256.
Do not rely on WER keeping older `dotnet.exe` files: automatic rotation can replace
them after unrelated managed controller failures. Never publish dumps; they may
contain private application data. This phase lost the original WER files during
a faulty helper-development series; see the report for the evidence limits.

Build these additional diagnostic projects (same pinned Avalonia 11.1.1):

```powershell
dotnet build smoke-tests/UiaStability/NativeCapture -c Release
dotnet build smoke-tests/UiaStability/MinimalAvalonia -c Release
```

First run one short real target and inspect both its result and debugger log:

```powershell
./smoke-tests/UiaStability/RunCaptured.ps1 -Output artifacts/uia-new-sanity -Cycles 2 -Client
```

The output directory must be fresh. The driver stops on the first nonzero exit,
missing result, or incomplete cycle count. Longer cases are separate observations,
not automatic retries. Example isolated Menu/ContextMenu/Flyout/etc. editor cases:

```powershell
./smoke-tests/UiaStability/RunCaptured.ps1 -Output artifacts/uia-editors `
  -Scenario editor -Controls Menu,ContextMenu,Flyout,TreeView,TabControl,ComboBox,ListBox `
  -Cycles 50 -Client `
  -Target smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll
```

Minimal controls support `-Close normal` and `-Close root-disconnect`. The second
is an isolated comparison of the existing root-only lifecycle, not a descendant
disconnect fix. `-Scenario differential` executes the same `Common/PlainWindowProbe.cs`
in a smoke runner or minimal application through `--uia-differential`; archive
comparison must use identical source, cycles, runtime, client, and build conditions.
Minimal normal mode uses ClassicDesktopLifetime, differential mode uses the same
nested Dispatcher pump as the archived runners. Keep those results separate.

For Menu/ContextMenu/Flyout, `-Scope popup` queries the actual native PopupRoot
HWND, verified to belong to the target PID. Owner traversal alone does not cover
every separate popup. `UIA_TREE_DEPTH` defaults to 8, with a hard cap of 32 and
128 nodes. Depth 8 did not reach the TextBox inside the Flyout template. The
separate depth-24 case used `UIA_PROBE_REQUIRE_VALUE=1` and asserted at least one
real ValuePattern provider before Close (30 nodes / 1 provider in this run):

```powershell
try {
  $env:UIA_TREE_DEPTH='24'
  $env:UIA_PROBE_REQUIRE_VALUE='1'
  ./smoke-tests/UiaStability/RunCaptured.ps1 -Output artifacts/uia-popup-value `
    -Scope popup -Controls Flyout -Cycles 50 -Client
} finally {
  Remove-Item Env:UIA_TREE_DEPTH,Env:UIA_PROBE_REQUIRE_VALUE -ErrorAction SilentlyContinue
}
```

`NativeCapture` launches only its own x64 target; no arbitrary PID attach or WER
registry changes. It captures first-chance AV in UIAutomationCore or any second-
chance AV with full readable user-mode memory, handles, modules, thread/memory
information. Exceptions remain unhandled; the target exit code is propagated.
`UIA_CAPTURE_TIMEOUT_SECONDS` defaults to 300; use an explicit larger bound for
an entire build-heavy regression suite. A timeout is not a native crash/pass.

Raw launch syntax, including a deliberate isolated capture self-test:

```powershell
$capture=(Resolve-Path smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll).Path
$dotnet=(Get-Command dotnet).Source
dotnet $capture artifacts/uia-capture-selftest $dotnet -- $capture --fault
```

The self-test intentionally returns `0xC0000005`; it is not a Designer failure.
Check dump header FullMemory/Memory64, exception context, and result.json. Capture
directory contains native journals, modules.json, and result.json. No recovery,
blanket accessibility changes, manual GCHandle changes, or COM AddRef/Release
instrumentation is installed in Designer. Test-only GC is used in weak-reference
probes; no production GC is added.

**Classify a dump, not just its exit code.** A JIT null dereference/managed NRE can
appear as `C0000005` under a native debugger. In Phase 3B.2 a rejected test owner
returned a null editor session; SOS `pe`/`ip2md` proved this was a harness NRE,
not the historical `ProviderCallouts::GetPatternProvider` AV. Use both native
and managed stacks and compare module RVA. Preserve first red observations.

Collection benchmarks opt in to additional operations without changing the old
default order: `COLLECTION3B_PROFILE_COUNTS=10,50,100`,
`COLLECTION3B_PROFILE_FULL_OPERATIONS=1`, `COLLECTION3B_PROFILE_OUTPUT=<fresh path>`,
then run the smoke runner with `--collections3b-profile`. Three warm-ups and 30
measured observations per operation; no CI timing thresholds. Do not run during
restore/build/stress jobs. Compositor callback is not physical first-pixel time.
