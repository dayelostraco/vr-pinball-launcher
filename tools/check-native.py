"""Verify bundled bridge DLL identity and architecture without loading any DLL."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct
import sys
from urllib.parse import urlparse


def check_hardening(data, offset, name):
    """Inspect PE32+ mitigation declarations, never map or execute the image.

    These are minimum build properties, not proof of runtime memory safety.
    Layout: Microsoft PE/COFF specification, optional and section headers.
    """
    size = struct.unpack_from("<H", data, offset + 20)[0]
    optional = offset + 24
    if size < 160 or optional + size > len(data):
        raise ValueError(f"{name}: truncated PE32+ optional header")
    if struct.unpack_from("<H", data, optional)[0] != 0x20b:
        raise ValueError(f"{name}: expected PE32+ optional header")
    flags = struct.unpack_from("<H", data, optional + 70)[0]
    # HIGH_ENTROPY_VA, DYNAMIC_BASE and NX_COMPAT must all remain enabled.
    if flags & 0x160 != 0x160:
        raise ValueError(f"{name}: missing ASLR/high-entropy/NX declarations")
    directories = struct.unpack_from("<I", data, optional + 108)[0]
    reloc_rva, reloc_size = struct.unpack_from("<II", data, optional + 152)
    if directories < 6 or not reloc_rva or not reloc_size:
        raise ValueError(f"{name}: missing base relocation directory")
    count = struct.unpack_from("<H", data, offset + 6)[0]
    sections = optional + size
    if not 1 <= count <= 96 or sections + count * 40 > len(data):
        raise ValueError(f"{name}: invalid section table")
    for index in range(count):
        header = sections + index * 40
        raw_size, raw_offset = struct.unpack_from("<II", data, header + 16)
        if raw_size and (raw_offset < sections + count * 40 or raw_offset + raw_size > len(data)):
            raise ValueError(f"{name}: invalid section data bounds")
        flags = struct.unpack_from("<I", data, header + 36)[0]
        if flags & 0xa0000000 == 0xa0000000:
            raise ValueError(f"{name}: writable executable section")


def check(root, manifest):
    """The reviewed lock is the trust anchor; do not fetch replacement hashes."""
    if manifest.get("formatVersion") != 1 or not manifest.get("files"):
        raise ValueError("Unsupported or empty native dependency lock")
    expected = set()
    for entry in manifest["files"]:
        name = entry["path"]
        path = PurePosixPath(name)
        if (len(path.parts) != 2 or path.parts[0] != "ControllerBridge"
                or path.suffix != ".dll" or "\\" in name or ":" in name
                or name in expected):
            raise ValueError("Invalid or duplicate native dependency path")
        expected.add(name)
        source = urlparse(entry.get("source", ""))
        if (source.scheme != "https" or source.netloc != "raw.githubusercontent.com"
                or not re.fullmatch(r"/[^/]+/[^/]+/[0-9a-f]{40}/.+", source.path)
                or source.query or source.fragment):
            raise ValueError(f"{name}: provenance must pin an upstream commit")
        digest = entry.get("sha256", "")
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError(f"{name}: invalid SHA-256")
        data = (root / name).read_bytes()
        if hashlib.sha256(data).hexdigest() != digest:
            raise ValueError(f"{name}: SHA-256 differs from reviewed binary")
        if len(data) < 64 or data[:2] != b"MZ":
            raise ValueError(f"{name}: invalid DOS header")
        offset = struct.unpack_from("<I", data, 60)[0]
        if offset < 64 or offset + 24 > len(data) or data[offset:offset + 4] != b"PE\0\0":
            raise ValueError(f"{name}: invalid PE header")
        machine = struct.unpack_from("<H", data, offset + 4)[0]
        flags = struct.unpack_from("<H", data, offset + 22)[0]
        if entry.get("machine") != "amd64" or machine != 0x8664 or not flags & 0x2000:
            raise ValueError(f"{name}: expected an AMD64 DLL")
        check_hardening(data, offset, name)
    actual = {p.relative_to(root).as_posix() for p in (root / "ControllerBridge").rglob("*")
              if p.suffix.lower() == ".dll"}
    if actual != expected:
        raise ValueError("Unreviewed or missing bridge DLL: " + ", ".join(sorted(actual ^ expected)))
    return len(expected)


def main(root=None):
    root = Path(root) if root is not None else Path(__file__).resolve().parents[1]
    try:
        manifest = json.loads((root / "tools/native-lock.json").read_text(encoding="utf-8"))
        count = check(root, manifest)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Native dependency check failed: {error}", file=sys.stderr)
        return 1
    print(f"Native dependency check: {count} reviewed AMD64 DLLs with ASLR/NX and no W+X sections; no libraries loaded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
