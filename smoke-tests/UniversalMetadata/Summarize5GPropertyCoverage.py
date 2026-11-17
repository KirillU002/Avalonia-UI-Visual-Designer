"""Phase 5G metrics over the frozen Phase 5D denominator and native pair evidence.
Usage: BEFORE AFTER INSPECTOR GRID_RUNTIME CANVAS_RUNTIME OUTPUT
"""
import sys,runpy,json,csv
from pathlib import Path
args=sys.argv[1:];out=Path(args[-1])
sys.argv=[str(Path(__file__).with_name('Summarize5EPropertyCoverage.py')),*args]
runpy.run_path(sys.argv[0],run_name='__main__')
for p in list(out.glob('Phase5E*')):
 p.replace(out/p.name.replace('Phase5E','Phase5G'))
summary_path=out/'Phase5GPropertyCoverageSummary.json'
s=json.loads(summary_path.read_text(encoding='utf-8'))
before=Path(args[0])
def read_json(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def pairs(path,flags):
 rows=read_json(path);assert rows and all(all(r.get(f)is True for f in flags) for r in rows)
 return {(r['Type'],r['Key']) for r in rows}
baseline_local=pairs(before/'inspector-proof.json',['Inspector','Json','Designer','AxamlPreview','LegacyPreview','ImportedPreview'])
baseline_runtime=pairs(before/'Grid.properties.json',['Runtime']) & pairs(before/'Canvas.properties.json',['Runtime'])
ids={(r['Control'],r['Key']):r['Identity'] for r in read_json(before/'PropertyCoverageInventory.json')}
baseline_verified={(t,ids[t,k]) for t,k in baseline_local & baseline_runtime}
baseline_preview={(t,ids[t,k]) for t,k in baseline_local}
s['BaselineVerifiedPairCount']=len(baseline_verified)
s['BaselineProof']='Archived Phase 5F Inspector and two native generated runtime manifests in BEFORE_DIR; a historical certified lower bound, not a deduction from editable flags.'
for f in s['Families']:
 if f['Phase']=='Before' and f['Assembly']=='Avalonia.Controls':
  f['PreviewCoverageVerifiedLowerBound']=100*len(baseline_preview)/4935
  f['EndToEndCoverageVerifiedLowerBound']=100*len(baseline_verified)/4935
s['ProofDefinition']='Current Inspector edit + JSON file save/read/open + native Designer/Legacy/AXAML/imported Preview + two built/running generated applications. Unique canonical pairs. Verified lower bound for the explicit literal matrix, not arbitrary expression support.'
summary_path.write_text(json.dumps(s,ensure_ascii=False,indent=2),encoding='utf-8')
def rewrite_csv(path,update):
 with path.open(encoding='utf-8',newline='') as f: rows=list(csv.DictReader(f)); fields=rows[0].keys()
 for r in rows: update(r)
 with path.open('w',encoding='utf-8',newline='') as f:
  w=csv.DictWriter(f,fieldnames=fields,lineterminator='\n');w.writeheader();w.writerows(rows)
def detail(r):
 if r['Phase']=='Before':
  r['PreviewSupported']=(r['Control'],r['Identity']) in baseline_preview
  r['EndToEnd']=(r['Control'],r['Identity']) in baseline_verified
def pertype(r):
 if r['Phase']=='Before':
  r['PreviewSupported']=sum(t==r['Control'] for t,i in baseline_preview)
  r['EndToEnd']=sum(t==r['Control'] for t,i in baseline_verified)
rewrite_csv(out/'Phase5GPropertyCoverageDetails.csv',detail)
rewrite_csv(out/'Phase5GPropertyCoverage.csv',pertype)
print('PHASE5G_COVERAGE_COMPLETE')
