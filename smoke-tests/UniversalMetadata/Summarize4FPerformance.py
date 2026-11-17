"""Compare matching core workflows and keep debugger/process metrics separate."""
import collections
import csv
import json
import math
from pathlib import Path
import statistics
import sys


def stats(values, time_key="Ms", bytes_key="Bytes"):
    times = sorted(v[time_key] for v in values)
    result = dict(MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times)) - 1],
                  StdDevMs=statistics.pstdev(times))
    if bytes_key:
        result["MedianBytes"] = statistics.median(v[bytes_key] for v in values)
    return result


def groups(path):
    result = collections.defaultdict(list)
    for sample in json.loads(path.read_text(encoding="utf-8-sig")):
        result[sample["Scene"], sample["Operation"]].append(sample)
    assert len(result) == 24
    for values in result.values():
        assert len(values) == 30 and {v["Iteration"] for v in values} == set(range(30))
    return result


baseline_path, current_path, host_path, output_path = map(Path, sys.argv[1:])
baseline, current = groups(baseline_path), groups(current_path)
assert baseline.keys() == current.keys()
rows = []
for key in baseline:
    row = dict(Scene=key[0], Operation=key[1], Samples=30)
    for label, values in (("Phase4E", baseline[key]), ("Phase4F", current[key])):
        row.update({label + k: v for k, v in stats(values).items()})
        row.update({label + "GC" + str(i): sum(v["GC"][i] for v in values) for i in range(3)})
        row[label + "PeakWorkingSet"] = max(v["PeakWorkingSet"] for v in values)
    rows.append(row)
with output_path.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=rows[0].keys(), lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)

hosts = json.loads(host_path.read_text(encoding="utf-8-sig"))
assert len(hosts) == 55 and len({v["Pid"] for v in hosts}) == 55 and all(v["ExitCode"] == 0 for v in hosts)
measured = [v for v in hosts if v["Iteration"] >= 0]
assert len(measured) == 50
summary = {"Boundary": "NativeCapture launch + real VsHost pipe handshake; not devenv command/DTE latency",
           "Samples": 50, "Warmups": 5, "Operations": {}}
for operation in measured[0]["Samples"]:
    summary["Operations"][operation] = stats([{"Ms": v["Samples"][operation]} for v in measured], bytes_key=None)
for key in ("PrivateBytes", "WorkingSet", "Handles", "Threads"):
    summary[key] = {"Min": min(v[key] for v in measured), "Max": max(v[key] for v in measured),
                    "Median": statistics.median(v[key] for v in measured)}
output_path.with_suffix(".host.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print("PHASE4F_MEASUREMENT_INTEGRITY PASS 720+720 core samples; 55 independent host processes")
for row in rows:
    if row["Scene"] == "Large":
        print(f"{row['Operation']}: {row['Phase4EMedianMs']:.2f}/{row['Phase4EP95Ms']:.2f} -> "
              f"{row['Phase4FMedianMs']:.2f}/{row['Phase4FP95Ms']:.2f} ms; "
              f"{row['Phase4EMedianBytes']/1e6:.2f} -> {row['Phase4FMedianBytes']/1e6:.2f} MB")
