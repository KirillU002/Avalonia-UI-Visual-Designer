"""Paired 4C workflow samples; nested stages are not additive. No outlier removal."""
import collections
import csv
import json
import math
from pathlib import Path
import statistics
import sys


def groups(path):
    result = collections.defaultdict(list)
    for item in json.loads(path.read_text(encoding="utf-8-sig")):
        result[item["Scene"], item.get("Operation", "ACK")].append(item)
    for values in result.values():
        assert len(values) == 30 and {v["Iteration"] for v in values} == set(range(30))
    return result


def summarize(values):
    times = sorted(v["Ms"] for v in values)
    return dict(MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times)) - 1],
                StdDevMs=statistics.pstdev(times), MedianBytes=statistics.median(v["Bytes"] for v in values),
                GC0=sum(v["GC"][0] for v in values), GC1=sum(v["GC"][1] for v in values),
                GC2=sum(v["GC"][2] for v in values), PeakWorkingSet=max(v["PeakWorkingSet"] for v in values))


def stage_rows(key, phase, values):
    rows = []
    for name in sorted({s["Stage"] for v in values for s in v["Stages"]}):
        data = [next((s for s in v["Stages"] if s["Stage"] == name), {}) for v in values]
        times = sorted(s.get("ElapsedMs", 0) for s in data)
        rows.append(dict(Scene=key[0], Operation=key[1], Phase=phase, Stage=name,
                         MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times)) - 1],
                         StdDevMs=statistics.pstdev(times),
                         MedianBytes=statistics.median(s.get("AllocatedBytes", 0) for s in data),
                         MedianCalls=statistics.median(s.get("Calls", 0) for s in data),
                         TotalCalls=sum(s.get("Calls", 0) for s in data),
                         GC0=sum(s.get("GC0", 0) for s in data), GC1=sum(s.get("GC1", 0) for s in data),
                         GC2=sum(s.get("GC2", 0) for s in data)))
    return rows


before_path, after_path, ack_path, output = map(Path, sys.argv[1:])
before, after, ack = groups(before_path), groups(after_path), groups(ack_path)
assert before.keys() == after.keys() and len(before) == 24 and len(ack) == 3
output.mkdir(parents=True, exist_ok=True)
rows, stages = [], []
for key in before:
    rows.append(dict(Scene=key[0], Operation=key[1], Samples=30,
                     **{"Before" + k: v for k, v in summarize(before[key]).items()},
                     **{"After" + k: v for k, v in summarize(after[key]).items()}))
    for phase, values in (("Before", before[key]), ("After", after[key])):
        stages.extend(stage_rows(key, phase, values))
ack_rows = [dict(Scene=key[0], Samples=30, **summarize(values)) for key, values in ack.items()]
for key, values in ack.items():
    assert all(any(s["Stage"] == "AXAML.AcknowledgeReuse" for s in v["Stages"]) for v in values)
    assert not any(s["Stage"] in ("RefreshPropertyInspector", "AXAML.StructuralSourceMapUpdate")
                   for v in values for s in v["Stages"]), "Exact ACK rebuilt Inspector or source map"
    stages.extend(stage_rows(key, "After", values))
for name, records in (("AxamlStructuralPhase4DPerformance.csv", rows),
                      ("AxamlStructuralPhase4DStages.csv", stages), ("AxamlStructuralPhase4DAck.csv", ack_rows)):
    with (output / name).open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys(), lineterminator="\n")
        writer.writeheader()
        writer.writerows(records)
print("PERFORMANCE4D_INTEGRITY PASS; paired=720+720; ACK=90; no discarded samples")
for row in rows:
    if row["Scene"] == "Large":
        print(f"{row['Operation']}: {row['BeforeMedianMs']:.2f}/{row['BeforeP95Ms']:.2f} -> "
              f"{row['AfterMedianMs']:.2f}/{row['AfterP95Ms']:.2f} ms; "
              f"{row['BeforeMedianBytes']/1e6:.2f} -> {row['AfterMedianBytes']/1e6:.2f} MB")
