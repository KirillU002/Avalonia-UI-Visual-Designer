import json, csv
from pathlib import Path
root=Path(__file__).resolve().parents[2]/'artifacts'/'preview-consistency-audit'
rows=json.loads((root/'geometry.json').read_text(encoding='utf-8-sig'))
for file in root.glob('*-runtime.json'):
    rows.extend(json.loads(file.read_text(encoding='utf-8-sig')))
keys=('Scene','Mode','ViewportWidth','ViewportHeight','Id','Name','ParentId','Type','NativeType','NativePresent','Attached','Visible','X','Y','Width','Height')
with (root/'visual-tree.csv').open('w',encoding='utf-8-sig',newline='') as stream:
    writer=csv.DictWriter(stream,fieldnames=keys);writer.writeheader();writer.writerows({k:r[k] for k in keys} for r in rows)
report=[]
for scene in sorted({r['Scene'] for r in rows}):
    for width in (800,1200,1600):
        subset=[r for r in rows if r['Scene']==scene and r['ViewportWidth']==width]
        expected={r['Id']:r for r in subset if r['Mode']=='AXAML'}
        for mode in ('Designer','Legacy','Runtime'):
            actual=[r for r in subset if r['Mode']==mode]
            assert len(actual)==len(expected),(scene,width,mode,len(actual),len(expected))
            diffs=[]
            for a in actual:
                e=expected[a['Id']]
                delta=max(abs(a[key]-e[key]) for key in ('X','Y','Width','Height'))
                if delta>1.1 or a['ParentId']!=e['ParentId']:diffs.append(dict(Name=a['Name'],Delta=delta,Expected=e,Actual=a))
                if mode!='Designer':assert a['NativePresent'] and a['Attached'],a
            result=dict(Scene=scene,Width=width,Mode=mode,Count=len(actual),Mismatches=len(diffs),MaxDelta=max([d['Delta'] for d in diffs],default=0),Differences=diffs)
            report.append(result)
            print(f'{scene} {width} {mode}: {len(actual)} controls; mismatches {len(diffs)}; max {result["MaxDelta"]:.3f} DIP')
            if scene!='RegistryCanvas':assert not diffs,result
json.dump(report,(root/'geometry-comparison.json').open('w',encoding='utf-8'),ensure_ascii=False,indent=2)
