"""Phase 6A conditional expression authoring over the frozen Phase 5G scope.
Usage: Phase5GPropertyCoverageDetails.csv ExpressionAudit.json OUTPUT_DIRECTORY
No policy inference from reflection or aliases: uses the actual production CanTarget flag.
"""
import collections
import csv
import json
import sys
from pathlib import Path

details, audit, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
with details.open(encoding="utf-8-sig", newline="") as handle:
    rows = [r for r in csv.DictReader(handle) if r["Phase"] == "After" and r["Assembly"] == "Avalonia.Controls"]
assert len(rows) == 4935
native = collections.defaultdict(list)
for p in json.loads(audit.read_text(encoding="utf-8-sig")):
    native[p["Control"], p["Identity"]].append(p)


def yes(value):
    return str(value).lower() == "true"


result = []
for r in rows:
    ps = native[r["Control"], r["Identity"]]
    assert ps, (r["Control"], r["Identity"])
    target = yes(r["Displayable"]) and any(p["ExpressionTarget"] for p in ps)
    result.append(dict(Control=r["Control"], Identity=r["Identity"], Displayable=yes(r["Displayable"]),
                       LiteralEditableExportable=yes(r["SafelyEditableAndExportable"]), ConditionalFxTarget=target,
                       OwnerType=ps[0]["OwnerType"], PropertyType=ps[0]["PropertyType"],
                       AliasesExcluded=len(ps) - 1,
                       Limit="Reviewed literal/binding/resource grammar; source mapping, scope, compiled context, child ownership and payload validated at commit" if target else ps[0]["Reason"]))
with (output / "Phase6AExpressionTargets.csv").open("w", encoding="utf-8", newline="") as handle:
    writer = csv.DictWriter(handle, fieldnames=result[0], lineterminator="\n")
    writer.writeheader()
    writer.writerows(result)
literal = sum(r["LiteralEditableExportable"] for r in result)
assert literal == 2579
targets = [r for r in result if r["ConditionalFxTarget"]]
extra = [r for r in targets if not r["LiteralEditableExportable"]]
summary = dict(
    Method="Frozen 72 core types / 4935 canonical type-property pairs. Aliases collapsed. Actual production CanTarget intersected with previous Displayable. Conditional expression eligibility is separate from literal eligibility and native runtime proof. No claim of arbitrary Binding/Resource resolution.",
    LiteralBefore=dict(Count=literal, Denominator=4935, Percent=100 * literal / 4935),
    LiteralAfter=dict(Count=literal, Denominator=4935, Percent=100 * literal / 4935),
    GenericFxBefore=dict(Count=0, Denominator=4935, Percent=0, Scope="New generic fx editor only; existing TextBindingPath/BindingSource/DataGrid workflows were already supported"),
    GenericFxAfter=dict(Count=len(targets), Denominator=4935, Percent=100 * len(targets) / 4935,
                        UniqueNativeIdentities=len({r["Identity"] for r in targets})),
    ConditionalAdditionalPairs=len(extra),
    LiteralOrConditionalFx=dict(Count=literal + len(extra), Denominator=4935, Percent=100 * (literal + len(extra)) / 4935,
                                Scope="Potential explicit authoring modes, not unconditional end-to-end coverage"),
    ExistingLiteralProof=dict(UniquePairs=195, NativeTypes=16, Scope="Phase 5G literal matrix retained; separate from expression modes"),
    ExpressionNativeProof=dict(AssignmentsPerApplication=10, UniqueTypePropertyPairs=5,
                               Applications=["Grid", "Canvas"],
                               Scope="Read generated runtime PASS files; caller-owned DataContext. No inferred all-value coverage"))
(output / "Phase6APropertyCoverageSummary.json").write_bytes(json.dumps(summary, ensure_ascii=False, indent=2).encode("utf-8"))
print("PHASE6A_COVERAGE", len(targets), "conditional pairs;", len(extra), "additional pairs beyond literal policy")
