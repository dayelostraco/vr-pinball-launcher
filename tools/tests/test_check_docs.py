import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("check_docs", Path(__file__).parents[1] / "check-docs.py")
docs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(docs)


def test_links_ignore_code_external_and_vendor_files(tmp_path, monkeypatch, capsys):
    (tmp_path / "README.md").write_text('[file](present.txt) [web](https://example.com) [anchor](#ok)\n```\n[example](missing.txt)\n```')
    (tmp_path / "present.txt").touch()
    monkeypatch.setattr(docs.subprocess, "check_output", lambda *a, **k: 'README.md\npresent.txt\nlicenses/vendor.md\nabsent.md\n')
    assert docs.main(tmp_path) == 0
    assert "1 local targets passed" in capsys.readouterr().out


def test_missing_local_link_fails(tmp_path, monkeypatch, capsys):
    (tmp_path / "README.md").write_text('[missing](<missing file.txt>)')
    monkeypatch.setattr(docs.subprocess, "check_output", lambda *a, **k: 'README.md')
    assert docs.main(tmp_path) == 1
    assert "missing link target" in capsys.readouterr().err
