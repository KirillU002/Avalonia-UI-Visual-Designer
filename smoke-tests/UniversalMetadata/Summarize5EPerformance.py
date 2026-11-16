"""Summarize identical Phase 5A workflow samples for Phase 5E (nearest-rank P95)."""
import collections
import csv
import json
import math
import statistics
import sys
from pathlib import Path

before, after, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
rows, stages = [], []
for phase, path in [('Before', before), ('After', after)]:
    samples = json.loads(path.read_text(encoding='utf-8-sig'))
    groups = collections.defaultdict(list)
    for sample in samples:
        groups[sample['Type'], sample['Operation']].append(sample)
    for (control, operation), values in groups.items():
        assert len(values) == 30 and {v['Iteration'] for v in values} == set(range(30))
        times = sorted(v['Ms'] for v in values)
        row = dict(Phase=phase, Control=control, Operation=operation, Samples=len(values),
                   MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95 * len(times))-1],
                   StdDevMs=statistics.pstdev(times), MinMs=min(times), MaxMs=max(times),
                   MedianBytes=statistics.median(v['Bytes'] for v in values),
                   GC0=sum(v['GC'][0] for v in values), GC1=sum(v['GC'][1] for v in values),
                   GC2=sum(v['GC'][2] for v in values),
                   MetadataBuilds=sum(v['MetadataBuilds'] for v in values),
                   PeakWorkingSet=max(v['PeakWorkingSet'] for v in values))
        for name in ['SelectionMs', 'LayoutMs']:
            row[name] = statistics.median(v[name] for v in values)
        for name in ['InspectorRowsCreated', 'InspectorViewsCreated', 'InspectorViewsReused']:
            row[name] = statistics.median((v.get('Counters') or {}).get(name, 0) for v in values)
        rows.append(row)
        if operation == 'Inspector':
            names = sorted({s['Stage'] for v in values for s in v.get('Stages', [])})
            for name in names:
                data = [next((s for s in v['Stages'] if s['Stage'] == name), {}) for v in values]
                stages.append(dict(Phase=phase, Control=control, Stage=name,
                                   MedianMs=statistics.median(s.get('ElapsedMs', 0) for s in data),
                                   MedianBytes=statistics.median(s.get('AllocatedBytes', 0) for s in data),
                                   MedianCalls=statistics.median(s.get('Calls', 0) for s in data)))
for name, data in [('Phase5EPerformance.csv', rows), ('Phase5EInspectorStages.csv', stages)]:
    with (output/name).open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=data[0].keys(), lineterminator='\n')
        writer.writeheader()
        writer.writerows(data)
for row in rows:
    if row['Operation'] == 'Inspector':
        print(f"{row['Phase']} {row['Control']}: median={row['MedianMs']:.2f} ms; "
              f"P95={row['P95Ms']:.2f} ms; bytes={row['MedianBytes']:.0f}; "
              f"views={row['InspectorViewsCreated']:.0f}; GC={row['GC0']}/{row['GC1']}/{row['GC2']}")
