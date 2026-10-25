import importlib.util
import os
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

spec = importlib.util.spec_from_file_location("record_previews", Path(__file__).parents[1] / "record_previews.py")
rp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rp)


@pytest.fixture
def capture(tmp_path, monkeypatch):
    table = tmp_path / "Example.vpx"
    table.touch()
    ini = tmp_path / "live.ini"
    ini.write_text("[PlayerVR]\nAskToTurnOn=0\n[Player]\nVolume=12\n")
    args = SimpleNamespace(launcher_media_root=tmp_path / "media", esde_video_dir=tmp_path / "esde",
                           force=False, work_dir=tmp_path / "work", dry_run=False, frames=2,
                           ini=ini, vpx_executable=tmp_path / "VPX.exe", timeout=10, fps=10,
                           ffmpeg=Path("ffmpeg"), video_filter=None, width=1280)
    monkeypatch.setattr(rp, "vpx_running", lambda: False)
    process = Mock()
    process.wait.return_value = 0
    monkeypatch.setattr(rp.subprocess, "Popen", Mock(return_value=process))
    def encode(command, **kwargs):
        Path(command[-1]).write_bytes(b"encoded video")
    monkeypatch.setattr(rp.subprocess, "run", Mock(side_effect=encode))
    frames = tmp_path / "Capture"
    frames.mkdir()
    for i in (1, 2):
        (frames / f"Playfield_{i:05d}_tmp.qoi").touch()
    return args, table, process


def test_record_preserves_live_ini_and_installs_video(capture):
    args, table, process = capture
    before = args.ini.read_bytes()
    rp.record(args, table)
    assert args.ini.read_bytes() == before
    assert "AskToTurnOn = 2" in (args.work_dir / table.stem / "capture.ini").read_text()
    assert (args.launcher_media_root / table.stem / "table.mp4").read_bytes() == b"encoded video"
    assert (args.esde_video_dir / "Example.mp4").exists()
    process.wait.assert_called_once_with(timeout=10)


def test_existing_media_and_force(capture):
    args, table, process = capture
    rp.record(args, table)
    process.wait.reset_mock()
    rp.record(args, table)
    process.wait.assert_not_called()
    args.force = True
    args.video_filter = "transpose=2"
    rp.record(args, table)
    assert "transpose=2" in rp.subprocess.run.call_args.args[0]


def test_install_video_keeps_existing(tmp_path):
    source, target = tmp_path / "source", tmp_path / "target"
    source.write_bytes(b"new")
    target.write_bytes(b"old")
    rp.install_video(source, [target], False)
    assert target.read_bytes() == b"old"
    rp.install_video(source, [target], True)
    assert target.read_bytes() == b"new"


def test_dry_run_does_not_launch_or_write(capture):
    args, table, process = capture
    args.dry_run = True
    rp.record(args, table)
    assert not args.work_dir.exists()
    process.wait.assert_not_called()


@pytest.mark.parametrize("failure", ["running", "exit", "missing", "stale", "extension", "timeout", "kill"])
def test_capture_failures_do_not_install(capture, monkeypatch, failure):
    args, table, process = capture
    if failure == "running":
        monkeypatch.setattr(rp, "vpx_running", lambda: True)
    elif failure == "exit":
        process.wait.return_value = 42
    elif failure == "missing":
        (table.parent / "Capture/Playfield_00001_tmp.qoi").unlink()
    elif failure == "stale":
        os.utime(table.parent / "Capture/Playfield_00001_tmp.qoi", (1, 1))
    elif failure == "extension":
        table = table.with_suffix(".txt")
        table.touch()
    else:
        timeout = rp.subprocess.TimeoutExpired("owned-vpx", 10)
        process.wait.side_effect = [timeout, 0] if failure == "timeout" else [timeout, timeout, 0]
    with pytest.raises((RuntimeError, ValueError)):
        rp.record(args, table)
    assert not args.launcher_media_root.exists()
    if failure in ("timeout", "kill"):
        process.terminate.assert_called_once()
        assert process.kill.call_count == int(failure == "kill")


def test_desktop_ini_adds_missing_section(tmp_path):
    source, target = tmp_path / "source.ini", tmp_path / "target.ini"
    source.write_text("[Player]\nVolume=20\n")
    rp.desktop_ini(source, target)
    assert "AskToTurnOn = 2" in target.read_text()


@pytest.mark.parametrize("running", [True, False])
def test_process_detection(monkeypatch, running):
    output = ('"VPinballX_BGFX64.exe","123"' if running else '"other.exe","123"') if os.name == "nt" else ("VPinballX" if running else "other")
    monkeypatch.setattr(rp.subprocess, "check_output", lambda *a, **k: output)
    assert rp.vpx_running() is running


def test_cli_validation_and_missing_inputs(tmp_path):
    base = [str(tmp_path / "A.vpx"), "--vpx-executable", str(tmp_path / "VPX.exe"), "--ini", str(tmp_path / "settings.ini")]
    assert rp.main(base) == 1
    with pytest.raises(SystemExit) as exc:
        rp.main(base + ["--width", "1279"])
    assert exc.value.code == 2
    for name in ("A.vpx", "VPX.exe", "settings.ini"):
        (tmp_path / name).touch()
    assert rp.main(base + ["--dry-run"]) == 0
