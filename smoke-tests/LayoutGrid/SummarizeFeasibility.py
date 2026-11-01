"""Research report tables, nearest-rank P50/P95; retains every steady observation."""
import json
import math
import pathlib
import sys

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/layout-phase7a3")
counts = (20, 50, 100, 300, 500)
scenarios = ("ToolboxGrid", "ToolboxStackPanel", "ToolboxDockPanel", "ToolboxWrapPanel", "ToolboxCanvas", "Move", "Reparent")


def read(name):
    d = json.loads((root / (name + ".json")).read_text(encoding="utf-8-sig"))
    o = [x for x in d["Observations"] if x["Phase"] == "Steady"]
    assert len(o) == 30 and not d["Debugger"] and d["Build"] == "Release"
    return d, o


def q(values, p=.5):
    v = sorted(values)
    return v[math.ceil(len(v) * p) - 1]


def stage(o, name, key):
    return sum(s[key] for s in o["Stages"] if s["Stage"] == name)


def gc(obs, prefix="Gen"):
    return "/".join(str(sum(o[prefix + str(i)] for o in obs)) for i in range(3))


print("### Основная UI-матрица: 1050 measured samples\n")
print("Все latency — ms; allocations — MiB; GC — сумма collections0/1/2 внутри30 операций.\n")
print("| Scenario | N | P50 | P95 | Min–max | UI MiB | GC0/1/2 | First operation ms |")
print("|---|---:|---:|---:|---:|---:|---:|---:|")
for s in scenarios:
    for n in counts:
        d, obs = read(f"current-{s}-{n}")
        print(f"| {s} | {n} | {d['Median']:.1f} | {d['P95']:.1f} | {d['Min']:.1f}–{d['Max']:.1f} | {d['MedianAllocated']/2**20:.2f} | {gc(obs)} | {d['Observations'][0]['FirstCommitMs']:.1f} |")

operations = {"Add": "current-ToolboxGrid", "Move (Wrap)": "current-Move", "Reparent (Stack)": "current-Reparent", "Resize": "undo-Resize", "Property (Text)": "undo-Property"}
print("\n### Undo capture: 750 measured samples (450 уже входят в основную UI-матрицу)\n")
print("CreateUndoSnapshot — inclusive RegisterHistorySnapshot scopes, включая persist/captions, а не только Serialize. Payload — UTF-16 строки текущего snapshot, без CLR overhead.\n")
print("| Operation | N | Capture calls | Capture P50 / P95 ms | Capture MiB | Snapshot KiB |")
print("|---|---:|---:|---:|---:|---:|")
for name, file in operations.items():
    for n in counts:
        _, obs = read(f"{file}-{n}")
        calls = sorted(set(stage(o, "CreateUndoSnapshot", "Calls") for o in obs))
        durations = [stage(o, "CreateUndoSnapshot", "ElapsedMs") for o in obs]
        allocated = q(stage(o, "CreateUndoSnapshot", "AllocatedBytes") for o in obs)
        print(f"| {name} | {n} | {','.join(map(str,calls))} | {q(durations):.2f} / {q(durations,.95):.2f} | {allocated/2**20:.2f} | {q(o['SnapshotCharacters'] for o in obs)*2/1024:.1f} |")

print("\n### Forward UI, atomicity и хранение snapshots\n")
print("Retained strings — distinct references Current/Saved/Undo/Redo, не heap census; history короткая, fixture reset после каждого шага. Поля UndoCommitMs исходной матрицы включают legacy helper waits260ms и НЕ являются latency продукта; они исключены из этой таблицы.\n")
print("| Operation | N | Forward P50/P95 ms | Logical entry delta | Retained strings MiB |")
print("|---|---:|---:|---:|---:|")
for name, file in operations.items():
    for n in counts:
        d, obs = read(f"{file}-{n}")
        assert all(o["UndoEntryDelta"] == 1 for o in obs)
        print(f"| {name} | {n} | {d['Median']:.1f}/{d['P95']:.1f} | 1 | {q(o['RetainedHistoryCharacters'] for o in obs)*2/2**20:.2f} |")

print("\nResize/Property: дополнительные forward allocations, GC и cold operation (остальные три operations уже в основной матрице).\n")
print("| Scenario | N | UI MiB | GC0/1/2 | First operation ms |")
print("|---|---:|---:|---:|---:|")
for s in ("Resize", "Property"):
    for n in counts:
        d, obs = read(f"undo-{s}-{n}")
        print(f"| {s} | {n} | {d['MedianAllocated']/2**20:.2f} | {gc(obs)} | {d['Observations'][0]['FirstCommitMs']:.1f} |")

print("\n### Прямые Undo/Redo, отдельная UI-серия Property edit\n")
print("По30 операций на N: production UndoCommand/RedoCommand (клавиатурный document path) напрямую, готовая native geometry/model references + compositor fence; нет GridEditorUndo/фиксированных ожиданий внутри clock.\n")
print("| N | Undo P50/P95 ms | Undo UI MiB | Redo P50/P95 ms | Redo UI MiB |")
print("|---:|---:|---:|---:|---:|")
for n in counts:
    d, obs = read(f"direct-Property-{n}")
    assert d["directRestore"] and all(o["RedoCommitMs"] > 0 for o in obs)
    print(f"| {n} | {q(o['UndoCommitMs'] for o in obs):.1f}/{q((o['UndoCommitMs'] for o in obs),.95):.1f} | {q(o['UndoUiAllocatedBytes'] for o in obs)/2**20:.2f} | {q(o['RedoCommitMs'] for o in obs):.1f}/{q((o['RedoCommitMs'] for o in obs),.95):.1f} | {q(o['RedoUiAllocatedBytes'] for o in obs)/2**20:.2f} |")

print("\n### CPU и временные границы UI (P50, ms)\n")
print("CPU GetThreadTimes грубый, для всего Ready interval; его нельзя вычитать из одной вложенной стадии. Layout/Commit/Ready — накопленные отметки от начала операции.\n")
print("| Scenario | N | Handler | Layout | Commit | Ready | UI CPU |")
print("|---|---:|---:|---:|---:|---:|---:|")
for s, prefix in [("Grid", "current-ToolboxGrid"), ("Move", "current-Move"), ("Property", "undo-Property")]:
    for n in (20, 100, 500):
        _, obs = read(f"{prefix}-{n}")
        print("| " + s + " | " + str(n) + " | " + " | ".join(f"{q(o[k] for o in obs):.1f}" for k in ("HandlerMs", "LayoutMs", "FirstCommitMs", "ReadyMs", "UiCpuMs")) + " |")

print("\n### VM-only: captions и commit при растущей истории\n")
print("34 вызова/процесс: 1 first + 3 warm-ups + 30 samples. Нет Window/compositor; Commit — existing CommitUndoBatch с независящим от UI текстовым изменением и завершённой предыдущей history group.\n")
print("| N | H | Captions P50/P95 ms | Captions MiB / GC0/1/2 | Commit P50/P95 ms | Commit MiB / GC0/1/2 | History strings MiB |")
print("|---|---:|---:|---:|---:|---:|---:|")
for n in (20, 100, 500):
    for h in (1, 10, 30):
        c, co = read(f"history-Captions-{n}-{h}")
        d, do = read(f"history-Commit-{n}-{h}")
        print(f"| {n} | {h} | {c['Median']:.2f}/{c['P95']:.2f} | {c['MedianAllocated']/2**20:.2f} / {gc(co,'Gc')} | {d['Median']:.2f}/{d['P95']:.2f} | {d['MedianAllocated']/2**20:.2f} / {gc(do,'Gc')} | {d['RetainedHistoryCharacters']*2/2**20:.2f} |")

print("\n### Парная UI-проверка влияния истории H0/H30\n")
print("По30samples/arm; одинаковая подготовка30 VM edits, конечные authored values и viewport. GC — сумма внутри30 forward clocks.\n")
print("| Scenario | N | H0 P50/P95 ms | H30 P50/P95 ms | UI MiB H0/H30 | Snapshot-stage P50 H0/H30 ms | Native build+measure P50 H0/H30 ms | GC H0 / H30 |")
print("|---|---:|---:|---:|---:|---:|---:|---:|")
for s in ("ToolboxGrid", "Move", "ToolboxCanvas"):
    for n in (20, 100):
        b, bo = read(f"history-ui/paired-H0-{s}-{n}")
        a, ao = read(f"history-ui/paired-H30-{s}-{n}")
        native = lambda obs: q(stage(o,"BuildNativeProjection","ElapsedMs")+stage(o,"MeasureArrange","ElapsedMs") for o in obs)
        print(f"| {s} | {n} | {b['Median']:.1f}/{b['P95']:.1f} | {a['Median']:.1f}/{a['P95']:.1f} | {b['MedianAllocated']/2**20:.2f}/{a['MedianAllocated']/2**20:.2f} | {q(stage(o,'CreateUndoSnapshot','ElapsedMs') for o in bo):.1f}/{q(stage(o,'CreateUndoSnapshot','ElapsedMs') for o in ao):.1f} | {native(bo):.1f}/{native(ao):.1f} | {gc(bo)} / {gc(ao)} |")
print("\n### VM-only: DTO materialization и Serialize существующего DTO\n")
print("| N | DTO P50/P95 ms | DTO MiB | Serialize P50/P95 ms | Serialize MiB |")
print("|---:|---:|---:|---:|---:|")
for n in counts:
    d, _ = read(f"history-Dto-{n}-1")
    s, _ = read(f"history-Serialize-{n}-1")
    print(f"| {n} | {d['Median']:.2f}/{d['P95']:.2f} | {d['MedianAllocated']/2**20:.2f} | {s['Median']:.2f}/{s['P95']:.2f} | {s['MedianAllocated']/2**20:.2f} |")

print("\n### Property path: budget для будущего PoC (не прогноз after)\n")
print("Build и Measure — отдельные вложенные части общего render. В Measure входят geometry enumeration/keys, которые PoC не удаляет.\n")
print("| N | Build P50 ms | Measure P50 ms | Full render P50 ms | Wrappers created | Factory roots | Forward UI MiB |")
print("|---:|---:|---:|---:|---:|---:|---:|")
for n in counts:
    _, obs = read(f"undo-Property-{n}")
    print(f"| {n} | {q(stage(o,'BuildNativeProjection','ElapsedMs') for o in obs):.2f} | {q(stage(o,'MeasureArrange','ElapsedMs') for o in obs):.2f} | {q(stage(o,'RebuildVisualTree','ElapsedMs') for o in obs):.1f} | {q(o['Counters'].get('DesignerWrappersCreated',0) for o in obs)} | {q(o['Counters'].get('PreviewControlsCreated',0) for o in obs)} | {q(o['UiAllocatedBytes'] for o in obs)/2**20:.2f} |")


def ranks(v):
    order = sorted(range(len(v)), key=v.__getitem__)
    result = [0.] * len(v)
    start = 0
    while start < len(v):
        end = start + 1
        while end < len(v) and v[order[end]] == v[order[start]]:
            end += 1
        for i in range(start, end):
            result[order[i]] = (start + 1 + end) / 2
        start = end
    return result


def spearman(x, y):
    x, y = ranks(x), ranks(y)
    a, b = sum(x)/len(x), sum(y)/len(y)
    denominator = math.sqrt(sum((t-a)**2 for t in x) * sum((t-b)**2 for t in y))
    return "—" if denominator == 0 else f"{sum((u-a)*(v-b) for u,v in zip(x,y))/denominator:.2f}"


print("\n### Exploratory Spearman ρ с ReadyMs, по30samples; не причинная атрибуция\n")
print("GC/JIT counters process-wide; CPU coarse; stage duration — часть того же интервала. Без confidence/repeated runs эти коэффициенты не являются release thresholds.\n")
print("| Scenario | N | GC2 | JIT count | UI CPU | Native MeasureArrange wall |")
print("|---|---:|---:|---:|---:|---:|")
for label, prefix, sizes in [("Grid", "current-ToolboxGrid", counts), ("Move", "current-Move", (100,)), ("Property", "undo-Property", (100,))]:
    for n in sizes:
        _, obs = read(f"{prefix}-{n}")
        wall = [o["ReadyMs"] for o in obs]
        values = [[o[key] for o in obs] for key in ("Gen2", "JittedMethods", "UiCpuMs")]
        values.append([stage(o,"MeasureArrange","ElapsedMs") for o in obs])
        print("| " + label + " | " + str(n) + " | " + " | ".join(spearman(wall, v) for v in values) + " |")
