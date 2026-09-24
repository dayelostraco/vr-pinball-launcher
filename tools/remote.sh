#!/usr/bin/env bash
# Drive the Windows PC (ssh host sigilark-gpu) from the Mac. Unity, the tests, builds and the
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
#                                       (no apostrophes in <msg>)
set -euo pipefail

HOST=sigilark-gpu
WIN_REPO='C:\Users\dayel\Development\GitHub\vr-pinball-launcher'
SCP_REPO='C:/Users/dayel/Development/GitHub/vr-pinball-launcher'
SSH_URL='git@github.com:dayelostraco/vr-pinball-launcher.git'
BRANCH=$(git rev-parse --abbrev-ref HEAD)

on_pc() {
    ssh "$HOST" "cd '$WIN_REPO'; $1"
}

sync() {
    local dirty
    dirty=$(on_pc 'git status --porcelain')
    if [ -n "$dirty" ]; then
        echo "The PC working copy has uncommitted changes:" >&2
        echo "$dirty" >&2
        echo "Review them, then run: tools/remote.sh pushback \"<message>\"" >&2
        exit 1
    fi
    git push -q origin "$BRANCH"
    on_pc "git fetch -q $SSH_URL $BRANCH; if (\$LASTEXITCODE) { exit 1 }; git checkout -q $BRANCH; git merge -q --ff-only FETCH_HEAD; exit \$LASTEXITCODE"
    local here there
    here=$(git rev-parse HEAD)
    there=$(on_pc 'git rev-parse HEAD' | tr -d '\r')
    if [ "$here" != "$there" ]; then
        echo "PC is at $there but this branch is at $here" >&2
        exit 1
    fi
}

cmd=${1:-}
shift || true
case "$cmd" in
    status)  on_pc 'git status --short' ;;
    sync)    sync ;;
    test)    sync; on_pc "powershell -ExecutionPolicy Bypass -File test.ps1 ${1:+-Filter $1}; exit \$LASTEXITCODE" ;;
    pytest)  sync; on_pc "uv run --with pytest pytest tools/tests -q; exit \$LASTEXITCODE" ;;
    batch)   sync; on_pc "powershell -ExecutionPolicy Bypass -File tools\\unity-batch.ps1 -Method $1; exit \$LASTEXITCODE" ;;
    build)   sync; on_pc "powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstaller; exit \$LASTEXITCODE" ;;
    deploy)  sync; on_pc "powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstaller -Deploy; exit \$LASTEXITCODE" ;;
    media)
        sync
        args=""
        for arg in "$@"; do
            escaped=${arg//\'/\'\'}
            args="$args '$escaped'"
        done
        on_pc "uv run tools/fetch_media.py$args; exit \$LASTEXITCODE"
        ;;
    pull)    scp -q "$HOST:$SCP_REPO/$1" "$2" ;;
    pushback)
        on_pc "git add -A; git commit -q -m '$1'; git push -q $SSH_URL $BRANCH; exit \$LASTEXITCODE"
        git pull -q --ff-only origin "$BRANCH" ;;
    *) sed -n '2,17p' "$0"; exit 2 ;;
esac
