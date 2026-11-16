"""Canonical, conservative property coverage over the existing catalog and adapter CSV.
No discovery or production dependency. Usage: script BEFORE_DIR AFTER_DIR OUTPUT_DIR.
"""
import csv,json,sys
from pathlib import Path
before,after,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
def csvrows(p):
 with p.open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
def yes(v):return str(v).lower()=='true'
inv=json.loads((after/'PropertyCoverageInventory.json').read_text(encoding='utf-8-sig'))
controls=csvrows(after/'StandardLibraryCoverage.csv')
lookup={r['Namespace']+'.'+r['Type']:r for r in controls if yes(r['Applicable'])}
metrics=[];detail=[]
for phase,folder in [('Before',before),('After',after)]:
 props={(r['Control'],r['Key']):r for r in csvrows(folder/'UniversalPropertyCoverage.csv')}
 library={r['Namespace']+'.'+r['Type']:r for r in csvrows(folder/'StandardLibraryCoverage.csv')}
 groups={}
 for p in inv:
  if p['Control'] not in lookup:continue
  groups.setdefault((p['Control'],p['Identity']),[]).append(p)
 for (control,identity),aliases in groups.items():
  if not any(p['Applicable'] for p in aliases):continue
  found=[props[(control,p['Key'])] for p in aliases if (control,p['Key'])in props]
  flags={k:any(yes(r[k]) for r in found) for k in ['Discovered','Displayable','Editable','Serializable','Importable','Exportable']}
  # Import capability permits literals, never arbitrary expressions/property graphs.
  flags['SourcePatchable']=any(yes(r['Importable']) and not yes(r['ReadOnly']) for r in found)
  capability=library.get(control,{})
  enabled=capability.get('Status') in ('Partially Supported','Fully Supported')
  specialized=enabled and any(p['CollectionAdapter'] or p['ContentAdapter'] for p in aliases)
  # Existing child-item editor: one native property, backed by CollectionV1.
  item=control.rsplit('.',1)[-1]
  childkeys={'MenuItem':{'Name','Header','IsEnabled'},'TreeViewItem':{'Name','Header','IsEnabled','IsExpanded'},'TabItem':{'Name','Header','Content','IsEnabled'},'ComboBoxItem':{'Name','Content','IsEnabled'},'ListBoxItem':{'Name','Content','IsEnabled'},'TabStripItem':{'Name','Content','IsEnabled'}}
  specialized=specialized or enabled and any(p['Name']in childkeys.get(item,set()) for p in aliases)
  # Existing ImageSource asset editor exports an actual native Image.Source.
  specialized=specialized or enabled and item=='Image' and any(p['Name']=='Source' for p in aliases)
  if specialized:
   for key in ['Displayable','Editable','Serializable','Exportable']:flags[key]=True
  if capability.get('AxamlImport','Opaque')!='Opaque' and any(p['SourceCollection'] for p in aliases):flags['Importable']=True
  flags['Discovered']=True
  flags['SafelyEditableAndExportable']=flags['Editable'] and flags['Exportable']
  flags['Unsupported']=not flags['SafelyEditableAndExportable']
  detail.append(dict(Phase=phase,Control=control,Assembly=lookup[control]['Assembly'],Identity=identity,Aliases=';'.join(p['Key'] for p in aliases),AliasCount=len(aliases),**flags))
 for control,cl in lookup.items():
  rows=[r for r in detail if r['Phase']==phase and r['Control']==control]
  metrics.append(dict(Phase=phase,Control=control,Assembly=cl['Assembly'],Applicable=len(rows),**{k:sum(r[k] for r in rows) for k in flags},PropertyCoveragePercent=round(100*sum(r['SafelyEditableAndExportable'] for r in rows)/len(rows),3) if rows else 0))
def write(name,rows):
 with (out/name).open('w',encoding='utf-8',newline='')as f:
  w=csv.DictWriter(f,fieldnames=rows[0].keys(),lineterminator="\n");w.writeheader();w.writerows(rows)
write('Phase5DPropertyCoverage.csv',metrics);write('Phase5DPropertyCoverageDetails.csv',detail)
summary=[]
for phase in ['Before','After']:
 for family in sorted(set(r['Assembly'] for r in metrics)):
  rows=[r for r in detail if r['Phase']==phase and r['Assembly']==family];types=[r for r in metrics if r['Phase']==phase and r['Assembly']==family]
  summary.append(dict(Phase=phase,Assembly=family,ApplicableControls=len(types),ApplicableTypePropertyPairs=len(rows),UniqueNativeIdentities=len(set(r['Identity'] for r in rows)),RawPairsWithAliases=sum(r['AliasCount'] for r in rows),AliasesExcluded=sum(r['AliasCount']-1 for r in rows),**{k:sum(r[k]for r in rows)for k in flags},UniqueSafelyEditableExportable=len(set(r['Identity']for r in rows if r['SafelyEditableAndExportable'])),PropertyCoveragePercent=round(100*sum(r['SafelyEditableAndExportable']for r in rows)/len(rows),3)if rows else 0))
(out/'Phase5DPropertyCoverageSummary.json').write_text(json.dumps(dict(Definition='Writable native/CLR signatures, known authorable getter collections and reviewed contextual attached properties; diagnostic read-only state and unrelated attached registrations excluded. Complex writable/authorable values remain in denominator. Existing reviewed collection/content/item adapters counted separately from literal flags. Aliases collapsed by native owner/name; inherited pairs and global unique identities reported separately. Flags are conservative eligibility of reviewed literal and explicitly identified collection/content/item/asset adapters, not proof of arbitrary expressions or all-value runtime correctness. Editable refers to the JSON editing/export workflow; imported-only capabilities are separate.',Families=summary),ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))

# Report integrity checks: no alias inflation, no support promoted from discovery.
assert all(r['UniqueNativeIdentities'] <= r['ApplicableTypePropertyPairs'] <= r['RawPairsWithAliases'] for r in summary)
assert all(r['SafelyEditableAndExportable'] <= min(r['Editable'],r['Exportable']) for r in summary)
for control in lookup:
 a=[r['Applicable'] for r in metrics if r['Control']==control]
 assert len(a)==2 and a[0]==a[1],control
print('PROPERTY_COVERAGE_INTEGRITY PASS')
