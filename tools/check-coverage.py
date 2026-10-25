"""Enforce independent 80% line and branch gates per measured production file."""
import json
from pathlib import Path
import re
import sys
from defusedxml import ElementTree as ET


def read_cobertura(path):
    """Combine nested classes by file and deduplicate source lines."""
    files = {}
    for cls in ET.parse(path).findall(".//class"):
        lines = files.setdefault(cls.attrib["filename"], {})
        for line in cls.findall("./lines/line"):
            covered, total = 0, 0
            if line.get("branch") == "true":
                match = re.fullmatch(r"[\d.]+% \((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if not match:
                    raise ValueError("Missing or invalid branch counts in Cobertura report")
                covered, total = map(int, match.groups())
            previous = lines.get(line.attrib["number"], (False, 0, 0))
            lines[line.attrib["number"]] = (
                previous[0] or int(line.attrib["hits"]) > 0,
                max(previous[1], covered), max(previous[2], total),
            )
    return {"files": {name: {"summary": {
        "covered_lines": sum(line[0] for line in lines.values()),
        "num_statements": len(lines),
        "covered_branches": sum(line[1] for line in lines.values()),
        "num_branches": sum(line[2] for line in lines.values()),
    }} for name, lines in files.items()}}


def validate(report):
    failures = []
    if not report.get("files"):
        return ["Coverage report contains no files"]
    for name, data in report["files"].items():
        summary = data["summary"]
        for label, covered, total in (
            ("lines", summary["covered_lines"], summary["num_statements"]),
            ("branches", summary["covered_branches"], summary["num_branches"]),
        ):
            if total and covered / total < 0.8:
                failures.append(f"{name}: {label} {covered}/{total} is below 80%")
    return failures


def main(path="Logs/python-coverage.json"):
    path = Path(path)
    report = read_cobertura(path) if path.suffix == ".xml" else json.loads(path.read_text(encoding="utf-8"))
    failures = validate(report)
    for failure in failures:
        print(failure, file=sys.stderr)
    return int(bool(failures))


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]) if len(sys.argv) > 1 else main())
