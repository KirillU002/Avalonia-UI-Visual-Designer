"""Read only exception/module metadata. No heap strings or secrets are exported."""
import datetime
import json
import mmap
import pathlib
import struct
import sys


def inspect(path):
    with path.open("rb") as source, mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
        def u32(offset): return struct.unpack_from("<I", data, offset)[0]
        def u64(offset): return struct.unpack_from("<Q", data, offset)[0]
        if data[:4] != b"MDMP": raise ValueError("Not a Windows minidump")
        streams = {u32(u32(12) + i * 12): u32(u32(12) + i * 12 + 8) for i in range(u32(8))}
        modules = []
        if 4 in streams:
            start = streams[4]
            for i in range(u32(start)):
                record = start + 4 + i * 108
                name = u32(record + 20)
                modules.append(dict(Name=pathlib.PureWindowsPath(data[name+4:name+4+u32(name)].decode("utf-16-le")).name,
                                    Base=u64(record), Size=u32(record+8)))
        result = dict(Dump=path.name, TimestampUtc=datetime.datetime.fromtimestamp(u32(20), datetime.timezone.utc).isoformat(),
                      Modules=modules, Streams=sorted(streams))
        if 15 in streams and u32(streams[15]+4) & 1: result["ProcessId"] = u32(streams[15]+8)
        if 6 in streams:
            record = streams[6]
            address = u64(record+24)
            module = next((m for m in modules if m["Base"] <= address < m["Base"]+m["Size"]), None)
            result["Exception"] = dict(ThreadId=u32(record), Code=hex(u32(record+8)), Address=hex(address),
                                       Module=module["Name"] if module else None,
                                       Offset=hex(address-module["Base"]) if module else None,
                                       Parameters=[hex(u64(record+40+i*8)) for i in range(min(15,u32(record+32)))])
        return result


if __name__ == "__main__":
    print(json.dumps([inspect(pathlib.Path(p)) for p in sys.argv[1:]], indent=2))
