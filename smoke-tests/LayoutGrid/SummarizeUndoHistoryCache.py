"""Validate the cache experiment matrix; keep timing out of functional CI gates."""
import csv
import json
import math
import sys
from pathlib import Path

root = Path(sys.argv[1] if len(sys.argv) > 1 else 'artifacts/undo-history-cache-poc')

def q(values, fraction=.5):
    a = sorted(values)
    return a[math.ceil(len(a)*fraction)-1]

def aggregate(records, ms, allocated, gc_fields):
    assert len(records) == 30, f'Expected 30 steady samples, got {len(records)}'
    return dict(median=q([r[ms] for r in records]), p95=q([r[ms] for r in records], .95),
                minimum=min(r[ms] for r in records), maximum=max(r[ms] for r in records),
                allocated=q([r[allocated] for r in records]),
                gc=[sum(r[g] for r in records) for g in gc_fields])

rows = []
captions = []
for file in sorted([*(root/'vm').glob('vm-*.json'), *(root/'captions').glob('captions-*.json')]):
    data = json.loads(file.read_text(encoding='utf-8-sig'))
    assert data['Samples']==30 and data['Build']=='Release' and not data['Debugger'], file
    for operation in sorted({r['Operation'] for r in data['Records'] if r['Phase']=='Steady'}):
        records=[r for r in data['Records'] if r['Operation']==operation and r['Phase']=='Steady']
        exclusive=file.parent.name=='captions'
        row=dict(family='Captions' if exclusive else 'VM',operation=operation,n=data['N'],h=data['History'],enabled=data['CacheEnabled'],
                 **aggregate(records,'Ms','AllocatedBytes',('Gc0','Gc1','Gc2')),
                 hits=sum(r['Hits'] for r in records), misses=sum(r['Misses'] for r in records),
                 cache_max=max(r['CacheCount'] for r in records))
        (captions if exclusive else rows).append(row)
        assert row['cache_max'] <= data['History']+1, file
        if not data['CacheEnabled']: assert row['cache_max']==0, file

ui=[]
for file in sorted((root/'ui').glob('cache-*.json')):
    data=json.loads(file.read_text(encoding='utf-8-sig'))
    assert data['samples']==30 and data['Build']=='Release' and not data['Debugger'], file
    assert data['actualZoom']==1 and data['RenderScaling']==1 and data['ablation']=='None', file
    records=[r for r in data['Observations'] if r['Phase']=='Steady']
    assert all(r['UndoEntryDelta']==1 for r in data['Observations']), file
    assert all(r['HistoryCacheCount']<=data['preparedHistory']+1 for r in data['Observations']), file
    if 'cache-off' in data['label']: assert all(r['HistoryCacheCount']==0 for r in data['Observations']), file
    ui.append(dict(family='UI',operation=data['scenario'],n=data['count'],h=data['preparedHistory'],enabled='cache-on' in data['label'],
        **aggregate(records,'FirstCommitMs','UiAllocatedBytes',('Gen0','Gen1','Gen2')),
        handler=q([r['HandlerMs'] for r in records]), visual=q([r['VisualMs'] for r in records]),
        layout=q([r['LayoutMs'] for r in records]), ready=q([r['ReadyMs'] for r in records]),
        hits=sum(r['HistoryCacheHits'] for r in records),misses=sum(r['HistoryCacheMisses'] for r in records),
        cache_max=max(r['HistoryCacheCount'] for r in records)))

if '--complete' in sys.argv:
    assert len(rows)==240 and len(ui)==150 and len(captions)==30, (len(rows),len(ui),len(captions))
    expected={(op,n,h,enabled) for op in ('Add','Move','Reparent','Resize','Property','Undo','Redo','HistoryRefresh')
              for n in (20,50,100,300,500) for h in (0,10,30) for enabled in (False,True)}
    assert {(r['operation'],r['n'],r['h'],r['enabled']) for r in rows}==expected
    expected={(op,n,h,enabled) for op in ('ToolboxCanvas','ToolboxGrid','ToolboxDockPanel','Move','Reparent')
              for n in (20,50,100,300,500) for h in (0,10,30) for enabled in (False,True)}
    assert {(r['operation'],r['n'],r['h'],r['enabled']) for r in ui}==expected

for family, values in [('vm',rows),('ui',ui),('captions',captions)]:
    if not values: continue
    with (root/(family+'-summary.csv')).open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=values[0].keys());writer.writeheader();writer.writerows(values)
    print(f'{family}: {len(values)} arms, {len(values)*30} steady measurements')
json.dump(dict(vm=rows,ui=ui,captions=captions), (root/'summary.json').open('w',encoding='utf-8'),ensure_ascii=False,indent=2)

def paired(values):
    pairs={}
    for r in values: pairs.setdefault((r['operation'],r['n'],r['h']),{})[r['enabled']]=r
    return [(k,v[False],v[True]) for k,v in sorted(pairs.items()) if len(v)==2]

with (root/'tables.md').open('w',encoding='utf-8') as f:
    for title,values in [('Caption pairs — без UI rows',captions),('VM / CPU — команды без Window',rows),('UI — Drop до compositor commit',ui)]:
        if not values: continue
        f.write(f'### {title}\n\n| Operation | N | H | Off P50/P95 ms | On P50/P95 ms | Off/On MiB | Off GC0/1/2 | On GC0/1/2 | On hit/miss |\n|---|---:|---:|---:|---:|---:|---|---|---|\n')
        for (op,n,h),a,b in paired(values):
            f.write(f"| {op} | {n} | {h} | {a['median']:.2f}/{a['p95']:.2f} | {b['median']:.2f}/{b['p95']:.2f} | {a['allocated']/1048576:.3f}/{b['allocated']/1048576:.3f} | {'/'.join(map(str,a['gc']))} | {'/'.join(map(str,b['gc']))} | {b['hits']}/{b['misses']} |\n")
        f.write('\n')

for (op,n,h),a,b in paired(ui):
    if n==100 and h==30:
        print(f'{op} N100/H30: {a["median"]:.2f}/{a["p95"]:.2f} -> {b["median"]:.2f}/{b["p95"]:.2f}; MiB {a["allocated"]/1048576:.2f}->{b["allocated"]/1048576:.2f}')
