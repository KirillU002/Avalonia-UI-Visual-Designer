"""Phase 5E adapter coverage and independently verified native property pairs.
Usage: BEFORE_DIR AFTER_AUDIT_DIR INSPECTOR_PROOF GRID_PROOF CANVAS_PROOF OUTPUT_DIR
"""
import csv,json,sys,runpy,collections
from pathlib import Path
before,after,inspector,grid,canvas,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
intermediate=after/'canonical';saved=sys.argv;sys.argv=['SummarizePropertyCoverage.py',str(before),str(after),str(intermediate)]
runpy.run_path(str(Path(__file__).with_name('SummarizePropertyCoverage.py')),run_name='__main__');sys.argv=saved

def read(p):
 with p.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
def write(p,rows):
 with p.open('w',encoding='utf-8',newline='') as f:w=csv.DictWriter(f,fieldnames=rows[0].keys(),lineterminator='\n');w.writeheader();w.writerows(rows)
def proof(p,flags):
 data=json.loads(p.read_text(encoding='utf-8-sig'));assert data and all(all(r.get(f)is True for f in flags)for r in data),str(p)+' has uncertified rows'
 result={(r['Type'],r['Key'])for r in data};assert len(result)==len(data),str(p)+' has duplicate pairs'
 return result
local=proof(inspector,['Inspector','Json','Designer','AxamlPreview','LegacyPreview','ImportedPreview']);g=proof(grid,['Runtime']);c=proof(canvas,['Runtime']);verified=local&g&c
inventory=json.loads((after/'PropertyCoverageInventory.json').read_text(encoding='utf-8-sig'));identities={(r['Control'],r['Key']):r['Identity'] for r in inventory}
signature_groups=collections.defaultdict(list)
for r in inventory:signature_groups[r['Control'],r['Identity']].append(r)
verified_ids={(t,identities[(t,k)])for t,k in verified};preview_ids={(t,identities[(t,k)])for t,k in local}
runtime_only={'ActualThemeVariant','IsFocused','IsKeyboardFocusWithin','IsPointerOver','Bounds','DesiredSize','IsEffectivelyEnabled','IsEffectivelyVisible','IsArrangeValid','IsMeasureValid'}
rows=read(intermediate/'Phase5DPropertyCoverageDetails.csv')
for r in rows:
 key=(r['Control'],r['Identity']);r['RuntimeOnly']=r['Identity'].rsplit('.',1)[-1]in runtime_only
 signatures=signature_groups[key]
 r['Attached']=any(p['Attached']for p in signatures)
 r['ReadOnlySignature']=all(p['ReadOnly']for p in signatures)
 r['InheritedFromBase']=any(p['Owner']!=r['Control']and not p['Attached']for p in signatures)
 r['PropertyKinds']=';'.join(sorted({p['Kind']for p in signatures}))
 r['PreviewAdapterEligible']=r['Exportable']=='True' and r['Editable']=='True'
 r['PreviewSupported']=key in preview_ids if r['Phase']=='After'else 'Not measured'
 r['EndToEnd']=key in verified_ids if r['Phase']=='After'else 'Not measured'
write(out/'Phase5EPropertyCoverageDetails.csv',rows)
pertype=[]
for phase in ['Before','After']:
 groups=collections.defaultdict(list)
 for r in rows:
  if r['Phase']==phase:groups[(r['Control'],r['Assembly'])].append(r)
 for (type,assembly),rs in groups.items():
  record=dict(Phase=phase,Control=type,Assembly=assembly,TotalApplicable=len(rs),AliasesExcluded=sum(int(r['AliasCount'])-1 for r in rs))
  for k in ['Discovered','Displayable','Editable','Serializable','Importable','SourcePatchable','Exportable','Unsupported','RuntimeOnly','PreviewAdapterEligible']:
   record[k]=sum(str(r[k]).lower()=='true' for r in rs)
  record['PreviewSupported']=sum(r['PreviewSupported']is True for r in rs)if phase=='After'else 'Not measured'
  record['EndToEnd']=sum(r['EndToEnd']is True for r in rs)if phase=='After'else 'Not measured'
  pertype.append(record)
write(out/'Phase5EPropertyCoverage.csv',pertype)
summary=json.loads((intermediate/'Phase5DPropertyCoverageSummary.json').read_text(encoding='utf-8-sig'))
for f in summary['Families']:
 rs=[r for r in rows if r['Phase']==f['Phase']and r['Assembly']==f['Assembly']];n=len(rs)
 f['EditableCoverage']=100*f['Editable']/n if n else 0;f['JsonCoverage']=100*f['Serializable']/n if n else 0;f['ExportCoverage']=100*f['Exportable']/n if n else 0
 f['PreviewCoverageVerifiedLowerBound']=100*sum(r['PreviewSupported']is True for r in rs)/n if f['Phase']=='After'else None
 f['EndToEndCoverageVerifiedLowerBound']=100*sum(r['EndToEnd']is True for r in rs)/n if f['Phase']=='After'else None
 f['RuntimeOnlyAnnotated']=sum(r['RuntimeOnly']for r in rs)
summary['VerifiedPairCount']=len(verified_ids);summary['ProofDefinition']='Actual Inspector edit + JSON disk round-trip + native Designer/Legacy/AXAML Preview + two built/running generated applications. Counts are certified lower bounds for this matrix; untested pairs are not promoted from discovery. Historical E2E was not measured with a pair manifest.'
summary['DenominatorDefinition']='Same frozen Phase 5D signature scope: core 72 types, 4935 applicable type-property pairs, 332 unique identities, 272 alias duplicates excluded. Runtime-only/read-only state separately annotated and not made editable; excluding additional runtime state is an optional metric, not a silent baseline change.'
(out/'Phase5EPropertyCoverageSummary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
ranking=collections.defaultdict(list);remaining=collections.defaultdict(list)
supported={r['Type']for r in read(after/'StandardLibraryCoverage.csv')if r['Status']in ('Fully Supported','Partially Supported')}
for r in rows:
 if r['Phase']=='Before'and r['Unsupported']=='True':ranking[r['Assembly'],r['Identity']].append(r)
 if r['Phase']=='After'and r['Unsupported']=='True':remaining[r['Assembly'],r['Identity']].append(r)
rank=[dict(Assembly=k[0],Identity=k[1],Components=len(v),SupportedComponents=sum(r['Control'].rsplit('.',1)[-1]in supported for r in v),RuntimeOnly=v[0]['RuntimeOnly'],Candidate='Read-only runtime state'if v[0]['RuntimeOnly']else'Capability review required')for k,v in ranking.items()]
for r in rank:
 rs=remaining[r['Assembly'],r['Identity']];r['RemainingComponents']=len(rs);r['RemainingSupportedComponents']=sum(p['Control'].rsplit('.',1)[-1]in supported for p in rs)
rank.sort(key=lambda r:(r['Assembly'],r['RuntimeOnly'],-r['SupportedComponents'],-r['Components'],r['Identity']));write(out/'Phase5EPropertyPriority.csv',rank)
assert next(f['ApplicableTypePropertyPairs']for f in summary['Families']if f['Assembly']=='Avalonia.Controls'and f['Phase']=='After')==4935
assert len(verified_ids)==len(verified)
print('PHASE5E_COVERAGE_INTEGRITY PASS; certified pairs='+str(len(verified_ids)))
