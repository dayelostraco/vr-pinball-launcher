import importlib.util
import json
from pathlib import Path
import pytest

spec = importlib.util.spec_from_file_location("gate", Path(__file__).parents[1] / "check-coverage.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def report(lines=8, branches=8, total=10):
    return {"files": {"production.py": {"summary": {
        "covered_lines": lines, "num_statements": total,
        "covered_branches": branches, "num_branches": total}}}}


def test_independent_thresholds_and_empty_report():
    assert gate.validate(report()) == []
    assert gate.validate(report(10, 7))
    assert gate.validate(report(7, 10))
    assert gate.validate({})
    assert gate.validate(report(0, 0, 0)) == []


def test_cli_reports_failure_and_success(tmp_path, capsys):
    path = tmp_path / "coverage.json"
    path.write_text(json.dumps(report(9, 7)))
    assert gate.main(path) == 1
    assert "branches" in capsys.readouterr().err
    path.write_text(json.dumps(report()))
    assert gate.main(path) == 0


def test_cobertura_combines_classes_without_double_counting_lines(tmp_path):
    path = tmp_path / "coverage.xml"
    path.write_text('''<coverage><classes>
      <class filename="Production.cs"><lines>
        <line number="1" hits="1" branch="true" condition-coverage="50% (1/2)"/>
        <line number="2" hits="0"/>
      </lines></class>
      <class filename="Production.cs"><lines>
        <line number="1" hits="1" branch="true" condition-coverage="100% (2/2)"/>
        <line number="2" hits="1"/>
      </lines></class>
    </classes></coverage>''')
    summary = gate.read_cobertura(path)["files"]["Production.cs"]["summary"]
    assert summary == dict(covered_lines=2, num_statements=2, covered_branches=2, num_branches=2)
    assert gate.main(path) == 0


def test_cobertura_missing_branch_measurement_fails(tmp_path):
    path = tmp_path / "coverage.xml"
    path.write_text('<coverage><class filename="A.cs"><lines><line number="1" hits="1" branch="true"/></lines></class></coverage>')
    with pytest.raises(ValueError, match="branch counts"):
        gate.read_cobertura(path)
