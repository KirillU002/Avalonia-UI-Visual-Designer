"""Summarize raw Release measurements; never assert timing thresholds."""
import json
import math
from collections import defaultdict
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
rows = []


def q(values, percentile):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * percentile) - 1)]


groups = defaultdict(list)
for row in json.loads((root / "operations.json").read_text(encoding="utf-8-sig")):
    if row["phase"] == "Steady":
        groups[(row["count"], row["operation"])].append(row)
for (count, operation), samples in sorted(groups.items()):
    rows.append(dict(count=count, operation=operation, samples=len(samples),
                     median=q([s["visibleMs"] for s in samples], .5),
                     p95=q([s["visibleMs"] for s in samples], .95),
                     actionMedian=q([s["actionMs"] for s in samples], .5),
                     allocatedMiB=q([s["allocatedBytes"] for s in samples], .5) / 1048576, allocationScope="Process",
                     gc=[sum(s["gc"][i] for s in samples) for i in range(3)]))
for path in sorted(root.glob("beta-*.json")):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    steady = [s for s in data["Observations"] if s["Phase"] == "Steady"]
    rows.append(dict(count=data["count"], operation=data["scenario"], samples=len(steady),
                     median=data["Median"], p95=data["P95"],
                     actionMedian=q([s["HandlerMs"] for s in steady], .5),
                     allocatedMiB=data["MedianAllocated"] / 1048576, allocationScope="UI thread",
                     gc=[data["Gc0"], data["Gc1"], data["Gc2"]]))
rows.sort(key=lambda r: (r["operation"], r["count"]))
(root / "summary-data.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
table = ["| Операция | N | Samples | Median, ms | P95, ms | Action median, ms | MiB | GC 0/1/2 |",
         "|---|---:|---:|---:|---:|---:|---:|---|"]
for r in rows:
    table.append(f'| {r["operation"]} | {r["count"]} | {r["samples"]} | {r["median"]:.2f} | '
                 f'{r["p95"]:.2f} | {r["actionMedian"]:.2f} | {r["allocatedMiB"]:.2f} | '
                 f'{"/".join(map(str, r["gc"]))} |')
(root / "summary.md").write_text("\n".join(table) + "\n", encoding="utf-8")
print(f"Summarized {len(rows)} scenarios / {sum(r['samples'] for r in rows)} steady-state samples")
