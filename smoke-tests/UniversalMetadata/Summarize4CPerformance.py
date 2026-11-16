"""Structural workflow benchmark; nested stages are not additive."""
import collections
import csv
import json
import math
from pathlib import Path
import statistics
import sys

source, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
samples = json.loads(source.read_text(encoding='utf-8-sig'))
groups = collections.defaultdict(list)
for sample in samples:
    groups[sample['Scene'], sample['Operation']].append(sample)
rows, stages = [], []
for (scene, operation), values in groups.items():
    assert len(values) == 30 and {v['Iteration'] for v in values} == set(range(30))
    times = sorted(v['Ms'] for v in values)
    rows.append(dict(Scene=scene, Operation=operation, Samples=30,
        MedianMs=statistics.median(times), P95Ms=times[math.ceil(.95*len(times))-1],
        StdDevMs=statistics.pstdev(times), MedianBytes=statistics.median(v['Bytes'] for v in values),
        GC0=sum(v['GC'][0] for v in values), GC1=sum(v['GC'][1] for v in values), GC2=sum(v['GC'][2] for v in values),
        PeakWorkingSet=max(v['PeakWorkingSet'] for v in values)))
    for name in sorted({s['Stage'] for v in values for s in v['Stages']}):
        data=[next((s for s in v['Stages'] if s['Stage']==name),{}) for v in values]
        stages.append(dict(Scene=scene, Operation=operation, Stage=name,
            MedianMs=statistics.median(s.get('ElapsedMs',0) for s in data),
            MedianBytes=statistics.median(s.get('AllocatedBytes',0) for s in data),
            MedianCalls=statistics.median(s.get('Calls',0) for s in data)))
assert len(groups)==24, 'Expected three scenes and eight operations'
for filename, records in [('AxamlStructuralPhase4CPerformance.csv',rows),('AxamlStructuralPhase4CStages.csv',stages)]:
    with (output/filename).open('w',encoding='utf-8',newline='') as stream:
        writer=csv.DictWriter(stream,fieldnames=records[0].keys(),lineterminator='\n');writer.writeheader();writer.writerows(records)
print('PERFORMANCE_INTEGRITY PASS; samples='+str(len(samples))+'; groups='+str(len(groups)))
for row in rows:
    if row['Scene']=='Large':print(row['Operation']+': '+str(round(row['MedianMs'],2))+'/'+str(round(row['P95Ms'],2))+' ms; '+str(round(row['MedianBytes']/1e6,2))+' MB')
