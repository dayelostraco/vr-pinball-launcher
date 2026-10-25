#!/usr/bin/env python3
"""Record silent VPX attract previews and optionally install launcher/ES-DE videos."""
import argparse
import configparser
import csv
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time


def vpx_running():
    if os.name == "nt":
        rows = csv.reader(io.StringIO(subprocess.check_output(
            ["tasklist", "/FO", "CSV", "/NH"], text=True)))
        return any(row and row[0].lower().startswith("vpinball") for row in rows)
    rows = subprocess.check_output(["ps", "-eo", "comm="], text=True).splitlines()
    return any(row.strip().lower().startswith("vpinball") for row in rows)


def desktop_ini(source, dest):
    """Use an isolated configuration; keep the user's live VR settings intact."""
    c = configparser.ConfigParser(interpolation=None, strict=False)
    c.optionxform = str
    c.read(source, encoding="utf-8-sig")
    if not c.has_section("PlayerVR"):
        c.add_section("PlayerVR")
    c.set("PlayerVR", "AskToTurnOn", "2")
    with dest.open("w", encoding="utf-8") as stream:
        c.write(stream)


def install_video(source, targets, force):
    for target in targets:
        if target.exists() and not force:
            print(f"Keeping existing {target}")
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_name(target.name + ".recording.tmp")
        shutil.copy2(source, tmp)
        os.replace(tmp, target)
        print(f"Installed {target}")


def record(args, table):
    table = table.resolve(strict=True)
    if table.suffix.lower() != ".vpx":
        raise ValueError(f"Expected a VPX table: {table}")
    targets = []
    if args.launcher_media_root:
        targets.append(args.launcher_media_root / table.stem / "table.mp4")
    if args.esde_video_dir:
        targets.append(args.esde_video_dir / (table.stem + ".mp4"))
    if targets and all(p.exists() for p in targets) and not args.force:
        print(f"Skipping {table.stem}: all requested videos exist")
        return
    folder = args.work_dir / table.stem
    if args.dry_run:
        print(f"Would capture {args.frames} frames: {table}")
        print(f"Would encode {folder / 'preview.mp4'}")
        for target in targets:
            print(f"Would install {target}")
        return
    if vpx_running():
        raise RuntimeError("VPX is running. Exit the table before recording previews.")
    folder.mkdir(parents=True, exist_ok=True)
    ini = folder / "capture.ini"
    desktop_ini(args.ini, ini)
    command = [str(args.vpx_executable), "-Ini", str(ini), "-CaptureAttract",
               str(args.frames), "1", str(table), "noloop"]
    print(f"Recording {table.stem}", flush=True)
    started = time.time()
    with (folder / "capture-console.log").open("w", encoding="utf-8") as output:
        process = subprocess.Popen(command, cwd=table.parent, stdout=output,
                                   stderr=subprocess.STDOUT)
        try:
            code = process.wait(timeout=args.timeout)
        except subprocess.TimeoutExpired:
            # Only terminate the process created by this invocation.
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            raise RuntimeError(f"Capture timed out: {table.stem}") from None
    if code:
        raise RuntimeError(f"VPX exited with status {code}: {table.stem}")
    capture = table.parent / "Capture"
    frames = [capture / f"Playfield_{i:05d}_tmp.qoi"
              for i in range(1, args.frames + 1)]
    if any(not p.exists() or p.stat().st_mtime < started - 1 for p in frames):
        raise RuntimeError(f"Fresh capture frames are incomplete: {table.stem}")
    video = folder / "preview.mp4"
    temp_video = folder / "preview.recording.mp4"
    # FFmpeg decodes QOI directly. Pillow's Python QOI decoder is much slower
    # for VPX's 5120x2160 captures; avoid a PNG intermediate sequence.
    subprocess.run([str(args.ffmpeg), "-hide_banner", "-loglevel", "error", "-y",
                    "-framerate", str(args.fps), "-i",
                    str(capture / "Playfield_%05d_tmp.qoi"), "-frames:v",
                    str(args.frames), "-vf", args.video_filter or f"scale={args.width}:-2",
                    "-c:v", "libx264", "-crf", "22", "-pix_fmt", "yuv420p",
                    "-movflags", "+faststart", str(temp_video)], check=True)
    os.replace(temp_video, video)
    install_video(video, targets, args.force)
    print(f"Saved {video}", flush=True)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("tables", nargs="+", type=Path, help="Explicit installed .vpx paths")
    ap.add_argument("--vpx-executable", required=True, type=Path)
    ap.add_argument("--ini", required=True, type=Path, help="Existing global VPX INI to copy")
    ap.add_argument("--ffmpeg", type=Path, default=Path(shutil.which("ffmpeg") or "ffmpeg"))
    ap.add_argument("--work-dir", type=Path)
    ap.add_argument("--launcher-media-root", type=Path)
    ap.add_argument("--esde-video-dir", type=Path)
    ap.add_argument("--frames", type=int, default=120)
    ap.add_argument("--fps", type=int, default=10)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--video-filter", help="Explicit FFmpeg filter instead of default scaling; crop native QOI frames before resizing")
    ap.add_argument("--timeout", type=int, default=180)
    ap.add_argument("--force", action="store_true", help="Replace explicitly requested video files")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)
    if min(args.frames, args.fps, args.width, args.timeout) <= 0 or args.width % 2:
        ap.error("Frames, FPS and timeout must be positive; width must be positive and even")
    try:
        args.vpx_executable = args.vpx_executable.resolve(strict=True)
        args.ini = args.ini.resolve(strict=True)
        args.work_dir = (args.work_dir or Path(tempfile.gettempdir()) / "vpx-previews").resolve()
        for table in args.tables:
            record(args, table)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
        print(f"Preview recording failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
