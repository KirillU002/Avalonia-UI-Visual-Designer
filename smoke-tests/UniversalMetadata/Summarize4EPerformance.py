"""Compare the unchanged 4C workflow used for the 4D baseline and 4E acceptance."""
import collections
import csv
import json
import math
from pathlib import Path
import statistics
import sys


def read(path):
    groups = collections.defaultdict(list)
    for sample in json.loads(path.read_text(encoding="utf-8-sig")):
        groups[sample["Scene"], sample["Operation"]].append(sample)
    assert len(groups) == 24
    for values in groups.values():
        assert len(values) == 30 and {v["Iteration"] for v in values} == set(range(30))
    return groups


def stats(values):
    times = sorted(v["Ms"] for v in values)
    return dict(MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times)) - 1],
                StdDevMs=statistics.pstdev(times), MedianBytes=statistics.median(v["Bytes"] for v in values),
                GC0=sum(v["GC"][0] for v in values), GC1=sum(v["GC"][1] for v in values),
                GC2=sum(v["GC"][2] for v in values), PeakWorkingSet=max(v["PeakWorkingSet"] for v in values))


baseline_path, current_path, output_path = map(Path, sys.argv[1:])
baseline, current = read(baseline_path), read(current_path)
assert baseline.keys() == current.keys()
rows = [dict(Scene=key[0], Operation=key[1], Samples=30,
             **{"Phase4D" + k: v for k, v in stats(baseline[key]).items()},
             **{"Phase4E" + k: v for k, v in stats(current[key]).items()}) for key in baseline]
with output_path.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=rows[0].keys(), lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
print("PERFORMANCE4E_INTEGRITY PASS; 720+720 samples; no trimming/retries")
for row in rows:
    if row["Scene"] == "Large":
        print(f"{row['Operation']}: {row['Phase4DMedianMs']:.2f}/{row['Phase4DP95Ms']:.2f} -> "
              f"{row['Phase4EMedianMs']:.2f}/{row['Phase4EP95Ms']:.2f} ms; "
              f"{row['Phase4DMedianBytes']/1e6:.2f} -> {row['Phase4EMedianBytes']/1e6:.2f} MB")
