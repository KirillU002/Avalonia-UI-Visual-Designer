"""Read-only VSIX payload audit; does not install or change a VS instance."""
import hashlib
import json
import sys
import zipfile
from pathlib import Path, PurePosixPath
import xml.etree.ElementTree as ET

package, output = map(Path, sys.argv[1:3])
with zipfile.ZipFile(package) as archive:
    names = set(archive.namelist())
    required = ["AvaloniaDesigner.VSIX.dll", "AvaloniaDesigner.Host.Protocol.dll",
                "VsHost/AvaloniaDesigner.VsHost.exe", "VsHost/AvaloniaDesigner.VsHost.runtimeconfig.json",
                "VsHost/AvaloniaDesigner.VsHost.deps.json", "VsHost/FormDesigner.dll", "VsHost/AvaloniaDesigner.VsHost.dll"]
    assert all(name in names for name in required), "Incomplete host payload"
    assert all(not PurePosixPath(name).is_absolute() and ".." not in PurePosixPath(name).parts
               and ":" not in name for name in names), "Unsafe archive path"
    forbidden = [name for name in names if name.lower().endswith((".dmp", ".mdf", ".ldf", ".suo", ".user", ".log"))
                 or PurePosixPath(name).name.lower() in {"secrets.json", "user.config", "lastsession.json"}]
    assert not forbidden, forbidden
    assert not [name for name in names if "/" not in name and name.endswith(".dll")
                and (name.startswith("Avalonia.") or "Eremex" in name)], "Visual dependencies in VS bridge root"
    config = json.loads(archive.read(required[3]))
    assert config["runtimeOptions"]["tfm"] == "net8.0"
    deps = json.loads(archive.read(required[4]))
    target = deps["targets"][deps["runtimeTarget"]["name"]]
    missing = []
    for library in target.values():
        for group in ("runtime", "native", "runtimeTargets"):
            for asset in library.get(group, {}):
                if asset.endswith("/_._"):
                    continue
                # SDK runtime assets may be flattened; RID assets retain their path.
                if "VsHost/" + asset not in names and "VsHost/" + PurePosixPath(asset).name not in names:
                    missing.append(asset)
    assert not missing, missing
    manifest = ET.fromstring(archive.read("extension.vsixmanifest"))
    identity = next(node for node in manifest.iter() if node.tag.endswith("}Identity"))
    results = {"Entries": len(names), "Bytes": package.stat().st_size,
               "SHA256": hashlib.sha256(package.read_bytes()).hexdigest(),
               "Version": identity.attrib["Version"], "Identity": identity.attrib["Id"],
               "RuntimeDependencyClosure": "PASS", "Forbidden": forbidden,
               "Symbols": sorted(name for name in names if name.endswith(".pdb")),
               "RequiredSHA256": {name: hashlib.sha256(archive.read(name)).hexdigest() for name in required}}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(results, indent=2), encoding="utf-8")
print("PHASE4F_PACKAGE PASS", len(names), "entries; dependency closure; relative paths; bridge root isolation")
