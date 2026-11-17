"""Reporting over existing canonical metadata; never a registration/capability engine."""
import csv,json,sys,collections
from pathlib import Path
before,after,coverage,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
def read(p):
 with p.open(encoding='utf-8-sig',newline='')as f:return list(csv.DictReader(f))
def yes(v):return str(v).lower()=='true'
def save(name,rows):
 with (out/name).open('w',encoding='utf-8',newline='')as f:
  w=csv.DictWriter(f,fieldnames=rows[0],lineterminator='\n');w.writeheader();w.writerows(rows)
signatures=json.loads((after/'NativePropertySignatures.json').read_text(encoding='utf-8-sig'))
lookup={(p['Control'],p['Key']):p for p in signatures}
inv=json.loads((after/'PropertyCoverageInventory.json').read_text(encoding='utf-8-sig'))
ig=collections.defaultdict(list)
for p in inv:ig[p['Control'],p['Identity']].append(p)
rows=read(coverage/'Phase5GPropertyCoverageDetails.csv')
base='Avalonia.Controls.Primitives.HeaderedItemsControl'
runtime={'ActualThemeVariant','CaretIndex','SelectedText','Bounds','DesiredSize','IsFocused','IsKeyboardFocusWithin','IsPointerOver','IsEffectivelyEnabled','IsEffectivelyVisible','IsArrangeValid','IsMeasureValid'}
corrected=[]
for phase in ['Before','After']:
 rs=[r for r in rows if r['Phase']==phase and r['Assembly']=='Avalonia.Controls']
 included=[r for r in rs if r['Control']!=base and r['Identity'].rsplit('.',1)[-1]not in runtime]
 n=len(included)
 corrected.append(dict(Phase=phase,HistoricalPairs=len(rs),ApplicableControls=71,CorrectedAuthorablePairs=n,ExcludedArchitecturalBase=sum(r['Control']==base for r in rs),ExcludedRuntimeState=sum(r['Control']!=base and r['Identity'].rsplit('.',1)[-1]in runtime for r in rs),Editable=sum(yes(r['Editable'])for r in included),Serializable=sum(yes(r['Serializable'])for r in included),SourcePatchable=sum(yes(r['SourcePatchable'])for r in included),Exportable=sum(yes(r['Exportable'])for r in included),EditableAndExportable=sum(yes(r['SafelyEditableAndExportable'])for r in included),Percent=100*sum(yes(r['SafelyEditableAndExportable'])for r in included)/n))
missing=collections.defaultdict(list)
for r in rows:
 if r['Phase']=='After' and r['Assembly']=='Avalonia.Controls' and not yes(r['SafelyEditableAndExportable']):missing[r['Identity']].append(r)
result=[]
for identity,rs in missing.items():
 aliases=ig[rs[0]['Control'],identity]
 p=next((lookup[(rs[0]['Control'],a['Key'])]for a in aliases if (rs[0]['Control'],a['Key'])in lookup),None)
 assert p is not None,(identity,rs[0]['Control'])
 key=identity.rsplit('.',1)[-1]
 reason='Needs a reviewed literal/object/collection adapter'
 if key in runtime:reason='Runtime-only state; not required visual authoring'
 elif all(yes(r['ReadOnlySignature'])for r in rs) and not any(a['CollectionAdapter']or a['ContentAdapter']for a in aliases):reason='Getter collection may be authorable; read-only diagnostics excluded by original inventory'
 elif key in {'Transitions','PageTransition'}:reason='Typed animation/transition object model and ownership required; not a string/CollectionV1 adapter'
 elif key in {'Resources','Styles','Template','DataTemplates','ItemTemplate','ContentTemplate'}:reason='Source-preserved object/template/resource graph; specialized architecture required'
 elif key in {'Command','CommandParameter','DataContext','ItemsSource','SelectedItem'}:reason='Expression/data object; no arbitrary user code or object deserialization'
 elif key=='Cursor':reason='Platform cursor lifetime and native/runtime acceptance required'
 result.append(dict(Property=key,OwnerType=p['OwnerType'],PropertyType=p['PropertyType'],Identity=identity,ApplicableComponents=len(rs),InheritedPairs=sum(yes(r['InheritedFromBase'])for r in rs),AttachedPairs=sum(yes(r['Attached'])for r in rs),ReadOnlySignature=all(yes(r['ReadOnlySignature'])for r in rs),RuntimeOnly=key in runtime,AliasesExcluded=sum(int(r['AliasCount'])-1 for r in rs),Displayable=sum(yes(r['Displayable'])for r in rs),Editable=sum(yes(r['Editable'])for r in rs),JSONSerializable=sum(yes(r['Serializable'])for r in rs),AXAMLImportable=sum(yes(r['Importable'])for r in rs),SourcePatchable=sum(yes(r['SourcePatchable'])for r in rs),PreviewVerified=sum(yes(r['PreviewSupported'])for r in rs),Exportable=sum(yes(r['Exportable'])for r in rs),Reason=reason))
result.sort(key=lambda r:(r['RuntimeOnly'],-r['ApplicableComponents'],r['Identity']))
save('Phase5GPropertyGaps.csv',result)
(out/'Phase5GAdjustedCoverage.json').write_bytes(json.dumps(dict(Definition='Historical 72/4935 retained separately. Corrected control scope excludes architectural HeaderedItemsControl only; associated/infrastructure/root/presenter types remain excluded as before. Corrected properties also exclude explicitly listed runtime diagnostic/caret state. Writable complex properties and authorable getter collections remain in denominator, including Templates/Styles/Resources/Transitions. Initial SelectionStart/SelectionEnd remain authorable where explicitly supported. Not a claim of arbitrary expression support.',RuntimeStateExcluded=sorted(runtime),PropertyCoverage=corrected),ensure_ascii=False,indent=2).encode('utf-8'))
print('ADJUSTED_AND_GAPS_REPORT PASS',corrected)
