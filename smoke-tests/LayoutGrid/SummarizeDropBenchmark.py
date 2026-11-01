"""Render the saved 30-sample matrices; no timing thresholds or removed outliers."""
import json
import math
import pathlib
import sys

folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/layout-phase7a2")
scenarios = ["ToolboxGrid", "ToolboxStackPanel", "ToolboxDockPanel", "ToolboxWrapPanel", "ToolboxCanvas", "Move", "Reparent"]


def read(label, scenario, count):
    data = json.loads((folder / f"{label}-{scenario}-{count}.json").read_text(encoding="utf-8-sig"))
    steady = [o for o in data["Observations"] if o["Phase"] == "Steady"]
    assert data["Build"] == "Release" and not data["Debugger"] and len(steady) >= 30
    assert data["Viewport"] == "1200x800 DIP" and data["RenderScaling"] == 1
    return data, steady


def q(values, quantile=.5):
    values = sorted(values)
    return values[max(0, math.ceil(len(values) * quantile) - 1)]


print("### First compositor commit, ms (30 steady samples per cell)\n")
print("| Scenario | N | Before median / P95 | Before min / max | After median / P95 | After min / max |")
print("|---|---:|---:|---:|---:|---:|")
for s in scenarios:
    for n in (50, 100, 300, 500):
        b, _ = read("before", s, n)
        a, _ = read("after", s, n)
        print(f"| {s} | {n} | {b['Median']:.1f} / {b['P95']:.1f} | {b['Min']:.1f} / {b['Max']:.1f} | {a['Median']:.1f} / {a['P95']:.1f} | {a['Min']:.1f} / {a['Max']:.1f} |")
print("\n### Allocations, MiB; GC collections across 30 measured operations\n")
print("| Scenario | N | UI before / after | Process before / after | GC 0/1/2 before | GC 0/1/2 after |")
print("|---|---:|---:|---:|---:|---:|")
for s in scenarios:
    for n in (50, 100, 300, 500):
        b, bo = read("before", s, n)
        a, ao = read("after", s, n)
        print(f"| {s} | {n} | {b['MedianAllocated']/2**20:.2f} / {a['MedianAllocated']/2**20:.2f} | {q(o['ProcessAllocatedBytes'] for o in bo)/2**20:.2f} / {q(o['ProcessAllocatedBytes'] for o in ao)/2**20:.2f} | {b['Gc0']}/{b['Gc1']}/{b['Gc2']} | {a['Gc0']}/{a['Gc1']}/{a['Gc2']} |")
print("\n### First Drop versus steady state, N=500\n")
print("| Scenario | First before / after, ms | First JIT before / after | Steady JIT median before / after |")
print("|---|---:|---:|---:|")
for s in scenarios:
    b, bo = read("before", s, 500)
    a, ao = read("after", s, 500)
    bc, ac = b["Observations"][0], a["Observations"][0]
    print(f"| {s} | {bc['FirstCommitMs']:.1f} / {ac['FirstCommitMs']:.1f} | {bc['JittedMethods']} / {ac['JittedMethods']} | {q(o['JittedMethods'] for o in bo)} / {q(o['JittedMethods'] for o in ao)} |")
print("\n### After timeline, cumulative ms from Drop, N=500 (median)\n")
print("| Scenario | Handler | Visual ready | Layout complete | Commit | Next Input | Hover outside clock |")
print("|---|---:|---:|---:|---:|---:|---:|")
for s in scenarios:
    _, obs = read("after", s, 500)
    print("| " + s + " | " + " | ".join(f"{q(o[k] for o in obs):.1f}" for k in ("HandlerMs", "VisualMs", "LayoutMs", "FirstCommitMs", "ReadyMs", "HoverMs")) + " |")
