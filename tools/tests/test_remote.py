"""Exercise remote command construction without a network or Git mutation."""
import base64
import os
from pathlib import Path
import shutil
import subprocess

import pytest

ROOT = Path(__file__).resolve().parents[2]
BASH = shutil.which("bash") or (
    "C:/Program Files/Git/bin/bash.exe"
    if Path("C:/Program Files/Git/bin/bash.exe").exists() else None
)
pytestmark = pytest.mark.skipif(BASH is None, reason="Bash is required for the remote helper")

MOCKS = r'''
git() {
    case "$*" in
        'rev-parse --abbrev-ref HEAD') printf '%s\n' "$FIXTURE_BRANCH" ;;
        'rev-parse HEAD') echo fixture-sha ;;
        *) return 0 ;;
    esac
}
ssh() {
    printf '%s\n' "$2" >> "$FIXTURE_LOG"
    local decoded
    decoded=$(printf '%s' "${2##* }" | base64 -d | iconv -f UTF-16LE -t UTF-8)
    if [[ "$decoded" == *'git status --porcelain'* ]]; then
        printf '%s' "${FIXTURE_DIRTY:-}"
    elif [[ "$decoded" == *'git rev-parse HEAD;'* ]]; then
        echo fixture-sha
    fi
}
export -f git ssh
exec bash "$@"
'''


def run_helper(tmp_path, *args, branch="feature/author's-fix", dirty=""):
    log = tmp_path / "commands.txt"
    env = dict(os.environ, VRL_REMOTE_HOST="fixture", VRL_REMOTE_REPO="C:/author's checkout",
               VRL_REMOTE_SCP_REPO="C:/fixture", VRL_REMOTE_GIT_URL="ssh://host/author's-repo",
               FIXTURE_LOG=str(log), FIXTURE_BRANCH=branch, FIXTURE_DIRTY=dirty)
    result = subprocess.run([BASH, "-c", MOCKS, "fixture", str(ROOT / "tools/remote.sh"), *args],
                            env=env, capture_output=True, text=True, timeout=15)
    commands = []
    if log.exists():
        for line in log.read_text().splitlines():
            assert line.startswith("powershell.exe -NoProfile -NonInteractive -EncodedCommand ")
            commands.append(base64.b64decode(line.rsplit(" ", 1)[1]).decode("utf-16le"))
    return result, commands


def test_status_quotes_path_and_propagates_git_failure(tmp_path):
    result, commands = run_helper(tmp_path, "status")
    assert result.returncode == 0, result.stderr
    assert "Set-Location -LiteralPath 'C:/author''s checkout'" in commands[0]
    assert commands[0].endswith("git status --short; exit $LASTEXITCODE")


@pytest.mark.parametrize("command,argument", [
    ("test", "Owner's test; $(throw 'injection')"),
    ("batch", "Editor.Run; $(throw 'injection')"),
    ("media", "--name=Author's table; $(throw 'injection')"),
    ("pushback", "Author's fix; $(throw 'injection')"),
])
def test_user_arguments_remain_powershell_literals(tmp_path, command, argument):
    result, commands = run_helper(tmp_path, command, argument)
    assert result.returncode == 0, result.stderr
    assert "'" + argument.replace("'", "''") + "'" in commands[-1]
    if command == "pushback":
        assert "Remote branch differs; refusing to commit" in commands[-1]
        assert "git commit" in commands[-1]
    else:
        sync = next(c for c in commands if "git fetch" in c)
        assert "'feature/author''s-fix'" in sync
        assert "'ssh://host/author''s-repo'" in sync
        assert "--ff-only FETCH_HEAD" in sync
        assert "if ($LASTEXITCODE) { exit $LASTEXITCODE }; git merge" in sync


def test_dirty_remote_prevents_sync(tmp_path):
    result, commands = run_helper(tmp_path, "sync", dirty=" M file.cs")
    assert result.returncode == 1
    assert "uncommitted changes" in result.stderr
    assert len(commands) == 1


def test_detached_checkout_cannot_pushback(tmp_path):
    result, commands = run_helper(tmp_path, "pushback", "message", branch="HEAD")
    assert result.returncode == 1
    assert "named branch" in result.stderr
    assert not commands
