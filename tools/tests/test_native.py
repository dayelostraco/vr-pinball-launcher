import hashlib
import importlib.util
import json
from pathlib import Path
import struct

import pytest

spec = importlib.util.spec_from_file_location("check_native", Path(__file__).parents[1] / "check-native.py")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def fixture(root):
    data = bytearray(400)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 68, 0x8664)
    struct.pack_into("<H", data, 70, 1)
    struct.pack_into("<H", data, 84, 240)
    struct.pack_into("<H", data, 86, 0x2000)
    struct.pack_into("<H", data, 88, 0x20b)
    struct.pack_into("<H", data, 158, 0x160)
    struct.pack_into("<I", data, 196, 16)
    struct.pack_into("<II", data, 240, 0x1000, 16)
    struct.pack_into("<II", data, 344, 16, 368)
    struct.pack_into("<I", data, 364, 0x60000020)
    (root / "ControllerBridge").mkdir()
    path = root / "ControllerBridge/test.dll"
    path.write_bytes(data)
    lock = {"formatVersion": 1, "files": [{
        "path": "ControllerBridge/test.dll", "machine": "amd64",
        "sha256": hashlib.sha256(data).hexdigest(),
        "source": "https://raw.githubusercontent.com/owner/repo/" + "a" * 40 + "/test.dll",
    }]}
    return path, lock


def test_valid_inventory_and_cli(tmp_path, capsys):
    _, lock = fixture(tmp_path)
    assert native.check(tmp_path, lock) == 1
    (tmp_path / "tools").mkdir()
    (tmp_path / "tools/native-lock.json").write_text(json.dumps(lock))
    assert native.main(tmp_path) == 0
    assert "no libraries loaded" in capsys.readouterr().out


@pytest.mark.parametrize("field,value", [
    ("path", "../outside.dll"), ("path", "ControllerBridge/sub/file.dll"),
    ("path", "ControllerBridge/test.exe"), ("path", "ControllerBridge/C:test.dll"),
    ("path", "ControllerBridge/a\\test.dll"), ("sha256", "invalid"),
    ("sha256", "0" * 64), ("source", "http://example.com/binary.dll"),
    ("source", "https://raw.githubusercontent.com/owner/repo/master/file.dll"),
    ("source", "https://raw.githubusercontent.com/owner/repo/" + "a" * 40 + "/file.dll?mutable"),
    ("machine", "x86"),
])
def test_manifest_rejections(tmp_path, field, value):
    _, lock = fixture(tmp_path)
    lock["files"][0][field] = value
    with pytest.raises(ValueError):
        native.check(tmp_path, lock)


@pytest.mark.parametrize("change", ["short", "dos", "offset", "signature", "x86", "exe"])
def test_even_a_pinned_hash_must_describe_an_amd64_dll(tmp_path, change):
    path, lock = fixture(tmp_path)
    data = bytearray(path.read_bytes())
    if change == "short":
        data = data[:10]
    elif change == "dos":
        data[:2] = b"xx"
    elif change == "offset":
        struct.pack_into("<I", data, 60, 2**32 - 1)
    elif change == "signature":
        data[64:68] = b"xxxx"
    elif change == "x86":
        struct.pack_into("<H", data, 68, 0x14c)
    else:
        struct.pack_into("<H", data, 86, 0)
    path.write_bytes(data)
    lock["files"][0]["sha256"] = hashlib.sha256(data).hexdigest()
    with pytest.raises(ValueError):
        native.check(tmp_path, lock)


def test_extra_missing_duplicate_and_empty_inventory(tmp_path):
    path, lock = fixture(tmp_path)
    extra = path.with_name("extra.DLL")
    extra.write_bytes(b"unreviewed")
    with pytest.raises(ValueError, match="Unreviewed"):
        native.check(tmp_path, lock)
    extra.unlink()
    lock["files"].append(dict(lock["files"][0]))
    with pytest.raises(ValueError, match="duplicate"):
        native.check(tmp_path, lock)
    lock["files"] = []
    with pytest.raises(ValueError, match="empty"):
        native.check(tmp_path, lock)
    assert native.main(tmp_path) == 1
    (tmp_path / "tools").mkdir()
    (tmp_path / "tools/native-lock.json").write_text("{")
    assert native.main(tmp_path) == 1


@pytest.mark.parametrize("offset,format_code,value", [
    (84, "H", 159), (84, "H", 500), (88, "H", 0x10b),
    (158, "H", 0x140), (158, "H", 0x120), (158, "H", 0x60),
    (196, "I", 5), (240, "I", 0), (244, "I", 0),
    (70, "H", 0), (70, "H", 97), (70, "H", 2),
    (348, "I", 1), (348, "I", 399), (364, "I", 0xe0000020),
])
def test_hardening_rejects_malformed_or_weakened_pinned_images(tmp_path, offset, format_code, value):
    path, lock = fixture(tmp_path)
    data = bytearray(path.read_bytes())
    struct.pack_into("<" + format_code, data, offset, value)
    path.write_bytes(data)
    lock["files"][0]["sha256"] = hashlib.sha256(data).hexdigest()
    with pytest.raises(ValueError):
        native.check(tmp_path, lock)


def test_uninitialized_data_section_has_no_disk_payload(tmp_path):
    path, lock = fixture(tmp_path)
    data = bytearray(path.read_bytes())
    struct.pack_into("<II", data, 344, 0, 0)
    struct.pack_into("<I", data, 364, 0xc0000080)
    path.write_bytes(data)
    lock["files"][0]["sha256"] = hashlib.sha256(data).hexdigest()
    assert native.check(tmp_path, lock) == 1
