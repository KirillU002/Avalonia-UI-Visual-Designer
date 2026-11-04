"""Compare actual applied/template header values captured by four native UI paths."""
import json
import sys
from pathlib import Path

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2] / 'artifacts' / 'preview-phase2'
captures = {}
active = None
for line in (root / 'prereq-final.log').read_text(encoding='utf-8-sig').splitlines():
    if line.startswith('HEADERS '):
        value = json.loads(line[len('HEADERS '):])
        active = 'HeadersGrid' if value['native'] else 'HeadersCanvas'
        captures[active] = {'Legacy': value['Legacy'], 'AXAML': value['Axaml']}
    elif line.startswith('DESIGNER_HEADER ') and active:
        captures[active]['Designer'] = json.loads(line[len('DESIGNER_HEADER '):])

results = []
for scene in ('HeadersCanvas', 'HeadersGrid'):
    modes = captures[scene]
    runtime = json.loads((root / (scene + '-header-styles.json')).read_text(encoding='utf-8-sig'))
    assert runtime, 'Runtime contains no native column headers: ' + scene
    expected = modes['AXAML']
    differences = [mode for mode, actual in modes.items() if actual != expected]
    differences += ['Runtime:' + row['Header'] for row in runtime if row['Appearance'] != expected]
    results.append(dict(Scene=scene, Captures=modes, Runtime=runtime, Differences=differences))
    print(scene + ': applied header properties in Designer/Legacy/AXAML/Runtime; differences=' + str(differences))
(root / 'header-style-comparison.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
assert not any(result['Differences'] for result in results), 'Applied header styles differ'
