"""Retained Phase 6A before/after and lazy editor measurements. ARTIFACT_DIR OUT_DIR"""
import csv
import json
import math
import statistics
import sys
from pathlib import Path

root, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
results = []
stages = []
for phase, filename in [("Before", "before-controls.json"), ("After", "after-controls.json"), ("LazyEditor", "editor-profile.json")]:
    samples = json.loads((root / filename).read_text(encoding="utf-8-sig"))
    for typ, operation in dict.fromkeys((r.get("Type", "TextBlock"), r["Operation"]) for r in samples):
        rows = [r for r in samples if (r.get("Type", "TextBlock"), r["Operation"]) == (typ, operation)]
        assert len(rows) == 30, (phase, typ, operation, len(rows))
        times = sorted(r["Ms"] for r in rows)
        results.append(dict(Phase=phase, Type=typ, Operation=operation, Samples=30,
                            MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times)) - 1],
                            AllocationMB=statistics.median(r["Bytes"] for r in rows) / 1e6,
                            VarianceMs2=statistics.pvariance(times),
                            GC0=sum(r["GC"][0] for r in rows), GC1=sum(r["GC"][1] for r in rows), GC2=sum(r["GC"][2] for r in rows),
                            SelectionMedianMs=statistics.median(r.get("SelectionMs", 0) for r in rows),
                            LayoutMedianMs=statistics.median(r.get("LayoutMs", 0) for r in rows),
                            PeakWorkingSetMB=max(r.get("PeakWorkingSet", 0) for r in rows) / 1e6,
                            MetadataBuilds=sum(r.get("MetadataBuilds", 0) for r in rows)))
        if operation == "Inspector":
            for name in dict.fromkeys(s["Stage"] for r in rows for s in r.get("Stages", [])):
                ss = [next((s for s in r["Stages"] if s["Stage"] == name), {}) for r in rows]
                stages.append(dict(Phase=phase, Type=typ, Stage=name,
                                   MedianMs=statistics.median(s.get("ElapsedMs", 0) for s in ss),
                                   AllocationMB=statistics.median(s.get("AllocatedBytes", 0) for s in ss) / 1e6,
                                   Calls=statistics.median(s.get("Calls", 0) for s in ss)))
for name, rows in [("Phase6APerformance.csv", results), ("Phase6AInspectorStages.csv", stages)]:
    with (output / name).open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
print("PHASE6A_PERFORMANCE_SUMMARY", len(results), "groups")
