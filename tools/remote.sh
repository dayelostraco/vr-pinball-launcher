#!/usr/bin/env bash
# Drive your trusted Windows SSH host from Mac/Linux. Unity, the tests, builds and the
# media script only run there; this pushes the current branch and runs them against it.
#
#   tools/remote.sh status              show uncommitted changes in the PC working copy
#   tools/remote.sh sync                push this branch and fast-forward the PC working copy to it
#   tools/remote.sh test [filter]       sync, then run the Unity EditMode tests
#   tools/remote.sh pytest              sync, then run the fetch_media tests
#   tools/remote.sh batch <Method>      sync, then run a static editor method in batchmode
#   tools/remote.sh build               sync, then build the player (no installer)
#   tools/remote.sh deploy              sync, then build and copy it over the installed launcher
#   tools/remote.sh media [args]        sync, then run tools/fetch_media.py on the PC
#   tools/remote.sh pull <path> <dest>  copy a file from the PC working copy to the Mac
#   tools/remote.sh pushback "<msg>"    commit the PC's changes, push them, and pull them here
set -euo pipefail

HOST=${VRL_REMOTE_HOST:?Set VRL_REMOTE_HOST to your Windows SSH alias}
WIN_REPO=${VRL_REMOTE_REPO:?Set VRL_REMOTE_REPO to your Windows checkout path}
SCP_REPO=${VRL_REMOTE_SCP_REPO:?Set VRL_REMOTE_SCP_REPO to its forward-slash SCP path}
SSH_URL=${VRL_REMOTE_GIT_URL:-$(git remote get-url origin)}
BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [[ "$BRANCH" == HEAD ]]; then
    echo 'Check out a named branch before using the remote helper.' >&2
    exit 1
fi

# PowerShell literals escape apostrophes by doubling them. Transport the entire
# script as UTF-16LE base64 so the SSH host's default shell cannot reinterpret it.
ps_literal() {
    local value=${1//\'/\'\'}
    printf "'%s'" "$value"
}

on_pc() {
    local script encoded
    script="\$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath $(ps_literal "$WIN_REPO"); $1"
    encoded=$(printf '%s' "$script" | iconv -f UTF-8 -t UTF-16LE | base64 | tr -d '\r\n')
    ssh "$HOST" "powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded"
}

sync() {
    local dirty
    dirty=$(on_pc 'git status --porcelain; exit $LASTEXITCODE')
    if [ -n "$dirty" ]; then
        echo "The PC working copy has uncommitted changes:" >&2
        echo "$dirty" >&2
        echo "Review them, then run: tools/remote.sh pushback \"<message>\"" >&2
        exit 1
    fi
    git push -q origin "$BRANCH"
    on_pc "git fetch -q -- $(ps_literal "$SSH_URL") $(ps_literal "$BRANCH"); if (\$LASTEXITCODE) { exit \$LASTEXITCODE }; git show-ref --verify --quiet $(ps_literal "refs/heads/$BRANCH"); if (\$LASTEXITCODE -eq 0) { git checkout -q $(ps_literal "$BRANCH") } elseif (\$LASTEXITCODE -eq 1) { git checkout -q -b $(ps_literal "$BRANCH") FETCH_HEAD } else { exit \$LASTEXITCODE }; if (\$LASTEXITCODE) { exit \$LASTEXITCODE }; git merge -q --ff-only FETCH_HEAD; exit \$LASTEXITCODE"
    local here there
    here=$(git rev-parse HEAD)
    there=$(on_pc 'git rev-parse HEAD; exit $LASTEXITCODE' | tr -d '\r')
    if [ "$here" != "$there" ]; then
        echo "PC is at $there but this branch is at $here" >&2
        exit 1
    fi
}

cmd=${1:-}
shift || true
case "$cmd" in
    status)  on_pc 'git status --short; exit $LASTEXITCODE' ;;
    sync)    sync ;;
    test)    sync; on_pc "& ./test.ps1 ${1:+-Filter $(ps_literal "$1")}; exit \$LASTEXITCODE" ;;
    pytest)  sync; on_pc "python -m pytest tools/tests -q; exit \$LASTEXITCODE" ;;
    batch)   sync; on_pc "& ./tools/unity-batch.ps1 -Method $(ps_literal "${1:?Specify an editor method}"); exit \$LASTEXITCODE" ;;
    build)   sync; on_pc "powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstaller; exit \$LASTEXITCODE" ;;
    deploy)  sync; on_pc "powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstaller -Deploy; exit \$LASTEXITCODE" ;;
    media)
        sync
        args=""
        for arg in "$@"; do
            args="$args $(ps_literal "$arg")"
        done
        on_pc "uv run tools/fetch_media.py$args; exit \$LASTEXITCODE"
        ;;
    pull)    scp -q "$HOST:$SCP_REPO/$1" "$2" ;;
    pushback)
        on_pc "\$branch = git rev-parse --abbrev-ref HEAD; if (\$LASTEXITCODE) { exit \$LASTEXITCODE }; if (\$branch -cne $(ps_literal "$BRANCH")) { throw 'Remote branch differs; refusing to commit' }; git add -A; if (\$LASTEXITCODE) { exit \$LASTEXITCODE }; git commit -q -m $(ps_literal "${1:?Specify a commit message}"); if (\$LASTEXITCODE) { exit \$LASTEXITCODE }; git push -q -- $(ps_literal "$SSH_URL") $(ps_literal "$BRANCH"); exit \$LASTEXITCODE"
        git pull -q --ff-only origin "$BRANCH" ;;
    *) sed -n '2,17p' "$0"; exit 2 ;;
esac
