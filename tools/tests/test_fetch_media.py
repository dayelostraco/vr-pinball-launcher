import importlib.util
import json
import pathlib

import pytest

_spec = importlib.util.spec_from_file_location(
    "fetch_media", pathlib.Path(__file__).resolve().parents[1] / "fetch_media.py")
fm = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fm)

DB = [
    {"id": "afm1", "name": "Attack from Mars", "manufacturer": "Bally", "year": 1995},
    {"id": "t2", "name": "Terminator 2: Judgment Day", "manufacturer": "Williams", "year": 1991},
    {"id": "im", "name": "Iron Man (Pro Vault Edition)", "manufacturer": "Stern", "year": 2014},
]
AFM = "Attack from Mars (Bally 1995)"
T2 = "Terminator 2 (Williams 1991)"


class FakeGet:
    """Stands in for http_get: serves the VPS db and writes a stub for every media file."""

    def __init__(self, missing=(), db_error=None):
        self.urls = []
        self.missing = set(missing)
        self.db_error = db_error

    def __call__(self, url, dest=None):
        self.urls.append(url)
        if url == fm.VPSDB_URL:
            if self.db_error:
                raise self.db_error
            return json.dumps(DB).encode()
        if any(url.endswith("/" + m) for m in self.missing):
            raise OSError("HTTP Error 404: Not Found")
        pathlib.Path(dest).write_bytes(b"new")
        return None


@pytest.fixture
def setup(tmp_path):
    tables = tmp_path / "Tables"
    tables.mkdir()
    for stem in (AFM, T2, "Pinball Training Lab"):
        (tables / f"{stem}.vpx").write_bytes(b"")
    launcher = tmp_path / "Launcher"
    launcher.mkdir()
    config = launcher / "launcher-config.json"
    config.write_text(json.dumps({
        "tablesDirectory": str(tables),
        "searchSubdirectories": True,
        "tableMediaDirectory": "Media\\Tables",
    }), encoding="utf-8")
    overrides = tmp_path / "overrides.json"
    overrides.write_text(json.dumps({"_comment": "x", T2: "t2"}), encoding="utf-8")
    media = launcher / "Media" / "Tables"
    return {"config": str(config), "overrides": str(overrides), "media": media}


def run(setup, *extra, get=None):
    get = get or FakeGet()
    code = fm.main(["--config", setup["config"], "--overrides", setup["overrides"], *extra], get=get)
    return code, get


def test_match_by_name_manufacturer_and_year():
    assert fm.match(DB, AFM)["id"] == "afm1"


def test_match_title_with_parentheses():
    assert fm.match(DB, "Iron Man (Pro Vault Edition) (Stern 2014)")["id"] == "im"


def test_match_ignores_text_after_year_group():
    assert fm.match(DB, "Attack from Mars (Bally 1995) VPW 2.0")["id"] == "afm1"


def test_match_uses_override():
    assert fm.match(DB, T2, {T2: "t2"})["id"] == "t2"


def test_match_needs_a_year_group():
    assert fm.match(DB, "Pinball Training Lab") is None


def test_downloads_all_media_into_table_folders(setup):
    code, _ = run(setup)
    assert code == 0
    for stem in (AFM, T2):
        for name in ("wheel.png", "table.png", "bg.png", "table.mp4"):
            assert (setup["media"] / stem / name).read_bytes() == b"new"


def test_existing_files_are_never_overwritten(setup):
    folder = setup["media"] / AFM
    folder.mkdir(parents=True)
    (folder / "wheel.png").write_bytes(b"mine")
    code, get = run(setup)
    assert code == 0
    assert (folder / "wheel.png").read_bytes() == b"mine"
    assert not any(u.endswith("afm1/wheel.png") for u in get.urls)


def test_force_refetches_only_the_named_table(setup):
    for stem in (AFM, T2):
        folder = setup["media"] / stem
        folder.mkdir(parents=True)
        (folder / "wheel.png").write_bytes(b"mine")
    run(setup, "--force", AFM)
    assert (setup["media"] / AFM / "wheel.png").read_bytes() == b"new"
    assert (setup["media"] / T2 / "wheel.png").read_bytes() == b"mine"


def test_missing_media_is_reported_and_the_run_continues(setup, capsys):
    code, _ = run(setup, get=FakeGet(missing=("table.mp4",)))
    out = capsys.readouterr().out
    assert code == 0
    assert "table.mp4 not available" in out
    assert (setup["media"] / AFM / "bg.png").exists()
    assert not (setup["media"] / AFM / "table.mp4").exists()


def test_unmatched_tables_are_listed(setup, capsys):
    run(setup)
    assert "Pinball Training Lab" in capsys.readouterr().out


def test_vps_database_failure_exits_1(setup):
    code, _ = run(setup, get=FakeGet(db_error=OSError("offline")))
    assert code == 1


def test_dry_run_writes_nothing(setup):
    code, get = run(setup, "--dry-run")
    assert code == 0
    assert not setup["media"].exists()
    assert get.urls == [fm.VPSDB_URL]


def test_missing_tables_directory_exits_2(setup, tmp_path):
    cfg = json.loads(pathlib.Path(setup["config"]).read_text(encoding="utf-8"))
    cfg["tablesDirectory"] = str(tmp_path / "nope")
    pathlib.Path(setup["config"]).write_text(json.dumps(cfg), encoding="utf-8")
    code, _ = run(setup)
    assert code == 2
