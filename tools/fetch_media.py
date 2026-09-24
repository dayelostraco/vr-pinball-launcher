#!/usr/bin/env python3
"""Download VR arcade room media for every table the launcher knows about.

Reads tablesDirectory and tableMediaDirectory from the launcher's launcher-config.json, matches
each "Title (Manufacturer Year).vpx" to the Virtual Pinball Spreadsheet, and saves the wheel,
playfield, backglass and playfield video from VPinMediaDB into <media root>/<table name>/.
Existing files are never overwritten, so hand-picked art is safe; --force re-downloads one table.
Only media is downloaded, never ROMs.

Usage: uv run tools/fetch_media.py [--config PATH] [--force "Table Name (Maker 1995)"] [--dry-run]
"""
import argparse
import json
import os
import sys
import re
import urllib.parse
import urllib.request

DEFAULT_CONFIG = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs", "VR Pinball Launcher",
                              "launcher-config.json")
# {"Table Name (Manufacturer Year)": "<VPS id>"} for tables whose name differs from the VPS entry
DEFAULT_OVERRIDES = os.path.join(os.path.dirname(os.path.realpath(__file__)), "media-overrides.json")
VPSDB_URL = "https://virtualpinballspreadsheet.github.io/vps-db/db/vpsdb.json"
MEDIADB_URL = "https://raw.githubusercontent.com/superhac/vpinmediadb/main/{vps_id}/{path}"

# VPinMediaDB file -> file name the launcher looks for (TableCatalog constants)
MEDIA_MAP = [
    ("wheel.png", "wheel.png"),
    ("1k/table.png", "table.png"),
    ("1k/bg.png", "bg.png"),
    ("1k/table.mp4", "table.mp4"),
]


def http_get(url, dest=None):
    req = urllib.request.Request(url, headers={"User-Agent": "vr-pinball-launcher"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        data = resp.read()
    if dest is None:
        return data
    tmp = dest + ".part"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, dest)
    return None


def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower().replace("the ", ""))


def match(db, stem, overrides=None):
    if overrides and stem in overrides:
        return next((g for g in db if g.get("id") == overrides[stem]), None)
    m = re.match(r"^(.*?)\s*\((.+?)\s+(\d{4})\)", stem)
    if not m:
        return None
    name, manufacturer, year = norm(m.group(1)), m.group(2).lower(), int(m.group(3))
    for game in db:
        if game.get("year") == year and norm(game.get("name", "")) == name \
                and game.get("manufacturer", "").lower().startswith(manufacturer.split()[0]):
            return game
    return None


def to_native(path):
    return path.replace("\\", os.sep).replace("/", os.sep)


def media_root(config_path, config):
    """tableMediaDirectory, resolved against the folder holding launcher-config.json."""
    rel = to_native(config.get("tableMediaDirectory") or os.path.join("Media", "Tables"))
    if os.path.isabs(rel):
        return rel
    return os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(config_path)), rel))


def list_tables(tables_dir, recursive):
    stems = set()
    for folder, _, files in os.walk(tables_dir):
        stems.update(os.path.splitext(f)[0] for f in files if f.lower().endswith(".vpx"))
        if not recursive:
            break
    return sorted(stems, key=str.lower)


def load_overrides(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8-sig") as f:
        return {k: v for k, v in json.load(f).items() if not k.startswith("_")}


def main(argv=None, get=http_get):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--overrides", default=DEFAULT_OVERRIDES)
    ap.add_argument("--force", action="append", default=[], metavar="TABLE",
                    help="re-download this table's media (repeatable)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)

    try:
        # utf-8-sig: PowerShell-edited configs often start with a BOM.
        with open(args.config, encoding="utf-8-sig") as f:
            config = json.load(f)
    except (OSError, ValueError) as exc:
        print(f"could not read {args.config}: {exc}")
        return 2

    tables_dir = to_native(config.get("tablesDirectory", ""))
    if not os.path.isdir(tables_dir):
        print(f"tables directory not found: {tables_dir}")
        return 2

    root = media_root(args.config, config)
    try:
        db = json.loads(get(VPSDB_URL))
    except Exception as exc:
        print(f"could not load the VPS database: {exc}")
        return 1

    overrides = load_overrides(args.overrides)
    stems = list_tables(tables_dir, config.get("searchSubdirectories", True))
    for name in args.force:
        if name not in stems:
            print(f"--force: no table named '{name}'")

    fetched, missing, unmatched = 0, {}, []
    for stem in stems:
        game = match(db, stem, overrides)
        if not game:
            unmatched.append(stem)
            continue
        folder = os.path.join(root, stem)
        force = stem in args.force
        for src, name in MEDIA_MAP:
            dest = os.path.join(folder, name)
            if os.path.exists(dest) and not force:
                continue
            url = MEDIADB_URL.format(vps_id=urllib.parse.quote(game["id"]), path=src)
            if args.dry_run:
                print(f"{stem}: would fetch {name}")
                continue
            os.makedirs(folder, exist_ok=True)
            try:
                get(url, dest)
                fetched += 1
                print(f"{stem}: {name}")
            except Exception as exc:  # missing media for a table is normal
                missing.setdefault(stem, []).append(name)
                print(f"{stem}: {name} not available ({exc})")

    print(f"\nmedia folder: {root}")
    print(f"fetched {fetched} file(s) for {len(stems) - len(unmatched)} matched table(s)")
    for stem, names in sorted(missing.items()):
        print(f"  missing for {stem}: {', '.join(names)}")
    if unmatched:
        print("no VPS match (name it 'Title (Manufacturer Year)' or add it to tools/media-overrides.json):")
        for stem in unmatched:
            print(f"  {stem}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
