"""Summarize retained Phase 5G samples; never substitutes a failed sample/run.

Usage: python Summarize5GPerformance.py ARTIFACT_DIRECTORY
Five warm-ups and 30 measured samples are defined by the existing C# harness.
"""
import csv
import json
import math
import statistics
import sys
from pathlib import Path

root = Path(sys.argv[1])


def read(name):
    return json.loads((root / name).read_text(encoding="utf-8-sig"))


def median(rows, key):
    return statistics.median(r[key] for r in rows)


def p95(values):
    values = sorted(values)
    return values[math.ceil(len(values) * .95) - 1]


def save(name, rows):
    with (root / name).open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


results, stages = [], []
sources = [("Before", "before-controls.json"), ("After", "after-controls.json"),
           ("SameTypeDiagnostic", "same-type-controls.json"), ("NewControls", "new-controls-profile.json")]
if (root / "late-baseline-controls.json").exists():
    sources.append(("LateBaseline", "late-baseline-controls.json"))
for phase, file in sources:
    samples = read(file)
    for typ, operation in dict.fromkeys((r["Type"], r["Operation"]) for r in samples):
        rows = [r for r in samples if (r["Type"], r["Operation"]) == (typ, operation)]
        assert len(rows) == 30, (phase, typ, operation, len(rows))
        results.append(dict(Phase=phase, Type=typ, Operation=operation, Samples=len(rows),
                            MedianMs=median(rows, "Ms"), P95Ms=p95(r["Ms"] for r in rows),
                            StandardDeviationMs=statistics.pstdev(r["Ms"] for r in rows),
                            VarianceMs2=statistics.pvariance(r["Ms"] for r in rows),
                            AllocationMB=median(rows, "Bytes") / 1e6,
                            SelectionMedianMs=median(rows, "SelectionMs"),
                            LayoutMedianMs=median(rows, "LayoutMs"),
                            CpuMedianMs=median(rows, "CpuMs"),
                            GC0=sum(r["GC"][0] for r in rows), GC1=sum(r["GC"][1] for r in rows),
                            GC2=sum(r["GC"][2] for r in rows),
                            PeakWorkingSetMB=max(r["PeakWorkingSet"] for r in rows) / 1e6,
                            MetadataBuilds=sum(r["MetadataBuilds"] for r in rows)))
        if operation == "Inspector":
            for stage in dict.fromkeys(s["Stage"] for r in rows for s in r["Stages"]):
                ss = [next((s for s in r["Stages"] if s["Stage"] == stage), {}) for r in rows]
                stages.append(dict(Phase=phase, Type=typ, Stage=stage,
                                   MedianMs=statistics.median(s.get("ElapsedMs", 0) for s in ss),
                                   P95Ms=p95(s.get("ElapsedMs", 0) for s in ss),
                                   AllocationMB=statistics.median(s.get("AllocatedBytes", 0) for s in ss) / 1e6,
                                   Calls=statistics.median(s.get("Calls", 0) for s in ss),
                                   Created=statistics.median(s.get("CreatedTrackedObjects", 0) for s in ss)))
save("Phase5GPerformance.csv", results)
save("Phase5GInspectorStages.csv", stages)
imports, import_stages = [], []
import_sources = [("After", "import-profile.json")]
if (root / "late-baseline-import.json").exists():
    import_sources.append(("LateBaseline", "late-baseline-import.json"))
for phase, file in import_sources:
    samples = read(file)
    for scene in dict.fromkeys(r["Scene"] for r in samples):
        rows = [r for r in samples if r["Scene"] == scene]
        assert len(rows) == 30
        for name in dict.fromkeys(s["Stage"] for r in rows for s in r["Stages"]):
            ss = [next((s for s in r["Stages"] if s["Stage"] == name), {}) for r in rows]
            import_stages.append(dict(Phase=phase, Scene=scene, Stage=name,
                                      MedianMs=statistics.median(s.get("ElapsedMs", 0) for s in ss),
                                      P95Ms=p95(s.get("ElapsedMs", 0) for s in ss),
                                      AllocationMB=statistics.median(s.get("AllocatedBytes", 0) for s in ss) / 1e6,
                                      Calls=statistics.median(s.get("Calls", 0) for s in ss)))
        for operation in ["Import", "Patch"]:
            imports.append(dict(Phase=phase, Scene=scene, Operation=operation, Samples=len(rows),
                            MedianMs=median(rows, operation + "Ms"),
                            P95Ms=p95(r[operation + "Ms"] for r in rows),
                            StandardDeviationMs=statistics.pstdev(r[operation + "Ms"] for r in rows),
                            VarianceMs2=statistics.pvariance(r[operation + "Ms"] for r in rows),
                            AllocationMB=median(rows, operation + "Bytes") / 1e6,
                            GC0=sum(r["GC"][0] for r in rows), GC1=sum(r["GC"][1] for r in rows),
                            GC2=sum(r["GC"][2] for r in rows),
                            MetadataBuilds=sum(r["MetadataBuilds"] for r in rows)))
save("Phase5GImportPerformance.csv", imports)
save("Phase5GImportStages.csv", import_stages)
print("PHASE5G_PERFORMANCE_SUMMARY PASS", len(results), "operation groups;", len(stages), "inspector stages")
