# VR Arcade Room Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the launcher's flat one-wheel carousel with a VR arcade room: 7 life-size cabinets on an arc, playfield video on the centred cabinet, favorites and recently played views, fades around launching VPX, plus a script that fetches the media.

**Architecture:** Pure C# logic (naming, catalog, state, list view, input edge logic, arc maths, LRU, file URIs) lives in a `VRLauncher.Core` assembly with EditMode tests. Unity components that draw and read devices (`MediaCache`, `CabinetView`, `ArcRoom`, `ScreenFader`, `LauncherInput`) live in a `VRLauncher.Arcade` assembly, also testable. `LauncherBootstrap` in the default assembly wires them to the existing `TableLauncher` and replaces `TableCarousel`. Everything visual is built in code at runtime, so the scene only needs a camera and one bootstrap object.

**Tech Stack:** Unity 6000.6.2f1 (built-in render pipeline, OpenXR 1.18, TextMeshPro via ugui 2.6.0, Unity Test Framework 1.8.0 / NUnit), C# 9, PowerShell 5.1 on the Windows PC, Python 3.12 with `uv` and pytest for the media script.

**Spec:** `docs/superpowers/specs/2026-09-24-vr-arcade-room-design.md`

## Global Constraints

- **Where things run.** Edit code only in the Mac working copy `~/Development/GitHub/vr-pinball-launcher` (branch `feature/vr-arcade-room`). Unity, tests, builds and the media script run only on the Windows PC `sigilark-gpu`, working copy `C:\Users\dayel\Development\GitHub\vr-pinball-launcher`, driven through `tools/remote.sh` (Task 1). Never edit the PC working copy by hand; changes Unity makes there come back with `tools/remote.sh pushback`.
- **Do not disturb the user.** Never launch `vr-launch.exe` or VPinballX on the PC, and never start a build or test while the Unity Editor is open there. `build.ps1` refuses to run while `vr-launch.exe` is running; if it does, stop and ask.
- **Unity .meta files.** Every new file or folder under `Assets/` gets a `.meta` written by `tools/new-meta.sh` on the Mac and committed with it. Never let Unity generate one on the PC (it makes the PC working copy diverge).
- **Assemblies.** `Assets/Scripts/Core` = `VRLauncher.Core` (no MonoBehaviours, no references). `Assets/Scripts/Arcade` = `VRLauncher.Arcade` (references Core and `Unity.TextMeshPro` only; must not use types from the default assembly such as `LauncherConfig` or `TableLauncher`). Everything else stays in the default `Assembly-CSharp`. All C# uses `namespace VRLauncher` (tests: `VRLauncher.Tests`).
- **Code style.** Match the existing scripts: 4-space indent, braces on their own lines, `///` summaries on public types and non-obvious members, LF line endings.
- **Spec values.** Arc radius 2.2 m, 7 slots, 22 degrees apart; playfield tilted 35 degrees; scroll settles in about 0.25 s; trigger press 0.7 / release 0.4; quit hold 2 s; texture cache 15 tables (45 textures); state file `state.json` in `Application.persistentDataPath`; media in `<launcher>\Media\Tables\<Table Name>\{wheel.png,table.png,bg.png,table.mp4}`.
- **Media.** VPinMediaDB `table.png` and `table.mp4` are 1920x1080 landscape with the flippers on the right edge; `bg.png` is 1920x1080; `wheel.png` is 500x500 RGBA. Videos are H.264.
- **Scripts never download ROMs**, and `fetch_media.py` never overwrites an existing file unless `--force` names that table.
- **Writing.** No em-dashes in commit messages, README text or UI strings. Commit messages are short imperative sentences in the repo's style ("Add ...", "Move ..."), with no `Co-Authored-By` trailer.
- **Recorded deviations from the spec** (approved when the plan is approved): play history is stored as a list of records because Unity's `JsonUtility` cannot serialise dictionaries; the media script is `tools/fetch_media.py` (underscore, so tests can import it); keyboard `F` and `V` are added for favorite and view so everything can be exercised without a headset. The spec's automated "PC smoke test" of the running launcher becomes the user's headset run plus a read of `Player.log` (Task 12), because starting the VR app over SSH would take over the headset; the build, the tests and the preview renders stay automated.

## Review Focus

1. **Table names with apostrophes, commas, ampersands, `%` or `#`** (for example `Bram Stoker's Dracula`, `Simpsons Pinball Party, The`) must still load their media. Pinned by the `FileUri` round-trip tests in Task 7.
2. **An empty or truncated `state.json`** (power loss mid-write) must be set aside as `state.json.bad` and not crash the launcher. Pinned by `Load_EmptyFile_IsSetAside` in Task 3.
3. **Unfavoriting the last table while in the Favorites view** must leave a valid selection and then show the "No favorites yet" placeholder. Pinned by `Favorites_UnfavoriteLastRemaining_BecomesEmpty` in Task 4 and `EmptyView_ShowsPlaceholder` in Task 9.
4. **A table renamed or deleted after it was favorited or played** must simply drop out of Favorites and Recent. Pinned by `Recent_And_Favorites_IgnoreMissingTables` in Task 4.
5. **Mashing next faster than the animation** must never push cabinets off the arc or desynchronise the centre cabinet from the selection. Pinned by `RapidNext_ClampsScrollAndKeepsCentreInSync` in Task 9.

---

## Task 1: Tooling, test harness and table naming

**Files:**
- Create: `tools/UnityCommon.ps1`, `test.ps1`, `tools/unity-batch.ps1`, `tools/remote.sh`, `tools/new-meta.sh`
- Modify: `build.ps1` (use `tools/UnityCommon.ps1`), `Packages/manifest.json`
- Create: `Assets/Scripts/Core/` (+ `.meta`), `Assets/Scripts/Core/VRLauncher.Core.asmdef`, `Assets/Scripts/Core/TableNaming.cs`
- Create: `Assets/Tests/`, `Assets/Tests/EditMode/` (+ `.meta`), `Assets/Tests/EditMode/VRLauncher.Tests.EditMode.asmdef`, `Assets/Tests/EditMode/TableNamingTests.cs`

**Interfaces:**
- Produces: `tools/remote.sh {status|sync|test [filter]|pytest|batch <Method>|build|deploy|media [args]|pull <path> <dest>|pushback "<msg>"}`; `tools/new-meta.sh <paths...>`.
- Produces: `static class TableNaming { string Normalize(string); string TitleYearSignature(string); ParsedName Parse(string stem); }` and `readonly struct ParsedName { string Title; string Manufacturer; int Year; }` (Manufacturer null and Year 0 when the name has no `(Manufacturer Year)` group).

- [ ] **Step 1: Extract the shared PowerShell helpers**

Create `tools/UnityCommon.ps1` by moving `Find-UnityExe`, `Wait-UnityExit` and `Ensure-UnityHub` out of `build.ps1` unchanged, except that `Find-UnityExe` takes the project path as a parameter, and add `Assert-NoOpenEditor`:

```powershell
# Shared helpers for build.ps1, test.ps1 and tools\unity-batch.ps1. Dot-source this file.

# Prefer the version pinned in ProjectVersion.txt; fall back to the newest
# installed editor, warning about the mismatch rather than failing silently.
function Find-UnityExe {
    param([string]$ProjectPath, [string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Unity.exe not found at '$Explicit'." }
        return $Explicit
    }

    $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    $pinned = $null
    if (Test-Path $versionFile) {
        $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$'
        if ($line) { $pinned = $line.Matches[0].Groups[1].Value.Trim() }
    }

    $root = "C:\Program Files\Unity\Hub\Editor"
    if (-not (Test-Path $root)) { throw "No Unity editors found under '$root'. Install one via Unity Hub." }

    if ($pinned) {
        $exact = Join-Path $root "$pinned\Editor\Unity.exe"
        if (Test-Path $exact) { return $exact }
        Write-Warning "Pinned editor $pinned is not installed; falling back to the newest available."
    }

    $candidate = Get-ChildItem $root -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) { throw "No Unity.exe found under '$root'." }
    return $candidate
}

# Copy Wait-UnityExit and Ensure-UnityHub here verbatim from build.ps1,
# including their comments.

# Batchmode refuses to open a project the editor already has open, and says so only in the log.
function Assert-NoOpenEditor {
    param([string]$LogFile)
    if ((Test-Path $LogFile) -and
        (Select-String -Path $LogFile -Pattern 'another Unity instance is running with this project open' -Quiet)) {
        throw "The project is open in the Unity editor on this PC. Close it and retry."
    }
}
```

(The comment "Copy ... verbatim" is an instruction to you, not file content: paste the two real functions from `build.ps1` in its place.)

In `build.ps1`, delete the three function definitions, and just after `$ErrorActionPreference = "Stop"` add:

```powershell
. (Join-Path $PSScriptRoot "tools\UnityCommon.ps1")
```

Change the call to `$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe`, and after `Wait-UnityExit ...` add `Assert-NoOpenEditor -LogFile $logFile`.

- [ ] **Step 2: Write `test.ps1`**

```powershell
<#
  test.ps1 -- run the Unity EditMode tests headlessly and summarise the results.

  Usage:
      powershell -ExecutionPolicy Bypass -File test.ps1
      powershell -ExecutionPolicy Bypass -File test.ps1 -Filter TableNamingTests
#>

param(
    [string]$UnityExe = "",
    [string]$Filter   = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "tools\UnityCommon.ps1")

$projectPath = $PSScriptRoot
$logDir      = Join-Path $projectPath "Logs"
$logFile     = Join-Path $logDir "unity-tests.log"
$results     = Join-Path $logDir "editmode-results.xml"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
foreach ($file in ($logFile, $results)) {
    if (Test-Path $file) { Remove-Item $file -Force }
}

$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
Ensure-UnityHub

$unityArgs = ('-batchmode', '-projectPath', $projectPath,
               '-runTests', '-testPlatform', 'EditMode',
               '-testResults', $results, '-logFile', $logFile)
if ($Filter) { $unityArgs += ('-testFilter', $Filter) }

$started = Get-Date
& $unity unityArgs | Out-Null
Wait-UnityExit -ExePath $unity -Since $started
Assert-NoOpenEditor -LogFile $logFile

$compileErrors = Select-String -Path $logFile -Pattern 'error CS\d+' -ErrorAction SilentlyContinue
if ($compileErrors) {
    $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "Compilation failed -- see $logFile"
}

if (-not (Test-Path $results)) { throw "No test results were written -- see $logFile" }

[xml]$xml = Get-Content $results -Raw
$run = $xml.'test-run'
Write-Host "EditMode tests: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped (total $($run.total))"

foreach ($case in $xml.SelectNodes('//test-case[result="Failed"]')) {
    Write-Host "FAIL $($case.fullname)" -ForegroundColor Red
    $message = $case.SelectSingleNode('failure/message')
    if ($message) { Write-Host "     $($message.InnerText.Trim())" }
}

if ($run.result -notlike 'Passed*') { exit 1 }
exit 0
```

- [ ] **Step 3: Write `tools/unity-batch.ps1`**

```powershell
<#
  unity-batch.ps1 -- run a static editor method in batchmode (scene setup, preview renders).

  The method must log "[unity-batch] OK" when it succeeds and call EditorApplication.Exit.

  Usage: powershell -ExecutionPolicy Bypass -File tools\unity-batch.ps1 -Method VRLauncher.EditorTools.ArcadePreview.RenderCabinets
#>

param(
    [Parameter(Mandatory = $true)][string]$Method,
    [string]$UnityExe = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "UnityCommon.ps1")

$projectPath = Split-Path $PSScriptRoot -Parent
$logDir      = Join-Path $projectPath "Logs"
$logFile     = Join-Path $logDir "unity-batch.log"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if (Test-Path $logFile) { Remove-Item $logFile -Force }

$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
Ensure-UnityHub

$started = Get-Date
& $unity -quit -batchmode -projectPath $projectPath -executeMethod $Method -logFile $logFile | Out-Null
Wait-UnityExit -ExePath $unity -Since $started
Assert-NoOpenEditor -LogFile $logFile

$compileErrors = Select-String -Path $logFile -Pattern 'error CS\d+' -ErrorAction SilentlyContinue
if ($compileErrors) {
    $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "Compilation failed -- see $logFile"
}

if (-not (Select-String -Path $logFile -Pattern '\[unity-batch\] OK' -Quiet)) {
    Select-String -Path $logFile -Pattern 'Exception|Error' | Select-Object -First 15 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "$Method did not report success -- see $logFile"
}

Write-Host "$Method OK" -ForegroundColor Green
```

- [ ] **Step 4: Write `tools/remote.sh` and `tools/new-meta.sh`**

`tools/remote.sh`:

```bash
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
SSH_URL='gitgithub.com:dayelostraco/vr-pinball-launcher.git'
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
    media)   sync; on_pc "uv run tools/fetch_media.py $*; exit \$LASTEXITCODE" ;;
    pull)    scp -q "$HOST:$SCP_REPO/$1" "$2" ;;
    pushback)
        on_pc "git add -A; git commit -q -m '$1'; git push -q $SSH_URL $BRANCH; exit \$LASTEXITCODE"
        git pull -q --ff-only origin "$BRANCH" ;;
    *) sed -n '2,17p' "$0"; exit 2 ;;
esac
```

`tools/new-meta.sh`:

```bash
#!/usr/bin/env bash
# Write a Unity .meta file with a fresh GUID for each new asset path, so Unity never has to
# generate one on the PC. Create the file or folder first.
#   tools/new-meta.sh Assets/Scripts/Arcade Assets/Scripts/Arcade/Foo.cs
set -euo pipefail
for path in "$"; do
    meta="$path.meta"
    if [ -e "$meta" ]; then echo "exists: $meta"; continue; fi
    guid=$(uuidgen | tr -d '-' | tr 'A-F' 'a-f')
    if [ -d "$path" ]; then
        printf 'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta"
    else
        case "$path" in
            *.cs) printf 'fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta" ;;
            *.asmdef) printf 'fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta" ;;
            *) echo "unsupported asset type: $path" >&2; exit 1 ;;
        esac
    fi
    echo "wrote $meta"
done
```

Run: `chmod +x tools/remote.sh tools/new-meta.sh`

- [ ] **Step 5: Add the test framework and the two assemblies**

In `Packages/manifest.json` add `"com.unity.test-framework": "1.8.0",` to `dependencies` (keep alphabetical order, after `com.unity.multiplayer.center`).

`Assets/Scripts/Core/VRLauncher.Core.asmdef`:

```json
{
    "name": "VRLauncher.Core",
    "rootNamespace": "VRLauncher",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/Tests/EditMode/VRLauncher.Tests.EditMode.asmdef`:

```json
{
    "name": "VRLauncher.Tests.EditMode",
    "rootNamespace": "VRLauncher.Tests",
    "references": [
        "VRLauncher.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 6: Write the failing naming tests**

`Assets/Tests/EditMode/TableNamingTests.cs`:

```csharp
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableNamingTests
    {
        [TestCase("Attack from Mars (Bally 1995)", "Attack from Mars", "Bally", 1995)]
        [TestCase("Big Bang Bar (Capcom 1996) VPW 2.1", "Big Bang Bar", "Capcom", 1996)]
        [TestCase("Star Wars (Data East 1992)", "Star Wars", "Data East", 1992)]
        [TestCase("Simpsons Pinball Party, The (Stern 2003)", "The Simpsons Pinball Party", "Stern", 2003)]
        [TestCase("Spider-Man (Vault Edition) (Stern 2016)", "Spider-Man (Vault Edition)", "Stern", 2016)]
        [TestCase("Attack_from_Mars_(Bally_1995)", "Attack from Mars", "Bally", 1995)]
        [TestCase("2001 (Gottlieb 1971)", "2001", "Gottlieb", 1971)]
        public void Parse_ReadsTitleManufacturerAndYear(string stem, string title, string maker, int year)
        {
            ParsedName name = TableNaming.Parse(stem);
            Assert.AreEqual(title, name.Title);
            Assert.AreEqual(maker, name.Manufacturer);
            Assert.AreEqual(year, name.Year);
        }

        [Test]
        public void Parse_WithoutYearGroup_KeepsWholeNameAsTitle()
        {
            ParsedName name = TableNaming.Parse("Pinball Training Lab");
            Assert.AreEqual("Pinball Training Lab", name.Title);
            Assert.IsNull(name.Manufacturer);
            Assert.AreEqual(0, name.Year);
        }

        [Test]
        public void Normalize_StripsPunctuationCaseAndVrRoomPrefix()
        {
            Assert.AreEqual("medieval madness williams 1997",
                TableNaming.Normalize("VR ROOM Medieval_Madness (Williams  1997)"));
        }

        [TestCase("Big Bang Bar (Capcom 1996) VPW 2.1", "big bang bar capcom 1996")]
        [TestCase("Attack_from_Mars_Bally_1995_VPW", "attack from mars bally 1995")]
        [TestCase("2001 (Gottlieb 1971) Mod", "2001 gottlieb 1971")]
        public void TitleYearSignature_DropsDecoration(string name, string expected)
        {
            Assert.AreEqual(expected, TableNaming.TitleYearSignature(name));
        }
    }
}
```

Create the metas for everything new under `Assets/`:

```bash
tools/new-meta.sh Assets/Scripts/Core Assets/Scripts/Core/VRLauncher.Core.asmdef \
  Assets/Tests Assets/Tests/EditMode Assets/Tests/EditMode/VRLauncher.Tests.EditMode.asmdef \
  Assets/Tests/EditMode/TableNamingTests.cs
```

- [ ] **Step 7: Run the tests to see them fail**

Commit the tooling first so the PC can fetch it:

```bash
git add tools build.ps1 test.ps1 Packages/manifest.json Assets/Scripts/Core Assets/Scripts/Core.meta Assets/Tests Assets/Tests.meta
git commit -m "Add a headless test harness and remote tooling for the PC"
tools/remote.sh test
```

Expected: `Compilation failed` with `error CS0103: The name 'TableNaming' does not exist` (or CS0246 for `ParsedName`).

Unity rewrites `Packages/packages-lock.json` the first time it resolves the new package. If `tools/remote.sh status` shows it (or other `ProjectSettings/` files) modified, inspect them with `ssh sigilark-gpu "cd 'C:\Users\dayel\Development\GitHub\vr-pinball-launcher'; git diff --stat"`, and if they are only Unity's package resolution, run `tools/remote.sh pushback "Record the test framework in the package lock"`.

- [ ] **Step 8: Implement `TableNaming`**

`Assets/Scripts/Core/TableNaming.cs` (the `Normalize` and `TitleYearSignature` bodies are moved from `TableScanner.cs`, which Task 11 deletes):

```csharp
using System.Text.RegularExpressions;

namespace VRLauncher
{
    /// <summary>
    /// Parses and normalises table file names of the community form
    /// "Title (Manufacturer Year) optional decoration".
    /// </summary>
    public static class TableNaming
    {
        // A "(Manufacturer Year)" group, e.g. "(Bally 1995)".
        private static readonly Regex YearGroupRegex =
            new Regex("\([^)]*\b(?:19|20)\d{2}\b[^)]*\)", RegexOptions.Compiled);

        private static readonly Regex YearTokenRegex =
            new Regex("\b(?:19|20)\d{2}\b", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex =
            new Regex("\s+", RegexOptions.Compiled);

        // VPX VR-room conversions prefix the title; wheel art never does.
        private static readonly Regex VrRoomPrefixRegex =
            new Regex("^vr\s*room\s+", RegexOptions.Compiled);

        // Lazy title, then the first parenthesised "Maker Year" group that actually ends in a year,
        // so "Spider-Man (Vault Edition) (Stern 2016)" keeps "(Vault Edition)" in the title.
        private static readonly Regex TitleMakerYearRegex =
            new Regex("^(?<title>.*?)\s*\((?<maker>[^()]*?)\s+(?<year>(?:19|20)\d{2})\)", RegexOptions.Compiled);

        // "Simpsons Pinball Party, The" -> "The Simpsons Pinball Party".
        private static readonly Regex TrailingArticleRegex =
            new Regex("^(?<rest>.+),\s*(?<article>The|A|An)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Splits a table file stem into a display title, manufacturer and year.
        /// </summary>
        public static ParsedName Parse(string stem)
        {
            string cleaned = WhitespaceRegex.Replace((stem ?? string.Empty).Replace('_', ' '), " ").Trim();
            Match match = TitleMakerYearRegex.Match(cleaned);
            if (!match.Success || match.Groups["title"].Value.Trim().Length == 0)
            {
                return new ParsedName(DisplayTitle(cleaned), null, 0);
            }

            return new ParsedName(
                DisplayTitle(match.Groups["title"].Value.Trim()),
                match.Groups["maker"].Value.Trim(),
                int.Parse(match.Groups["year"].Value));
        }

        /// <summary>
        /// Case/punctuation-insensitive form: underscores become spaces,
        /// parentheses are dropped, whitespace is collapsed, and a leading
        /// "VR ROOM" marker is removed.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string result = name.Replace('_', ' ').Replace("(", "").Replace(")", "");
            result = WhitespaceRegex.Replace(result, " ").Trim().ToLowerInvariant();
            result = VrRoomPrefixRegex.Replace(result, "");

            return result.Trim();
        }

        /// <summary>
        /// Reduces a name to "title manufacturer year", discarding the author
        /// and version decoration that follows it.
        /// </summary>
        public static string TitleYearSignature(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // Preferred form: truncate after the "(Manufacturer Year)" group.
            Match group = YearGroupRegex.Match(name);
            if (group.Success)
            {
                return Normalize(name.Substring(0, group.Index + group.Length));
            }

            // Underscore-separated names have no parentheses to anchor on, so
            // fall back to the last year token. Using the last one keeps titles
            // that begin with a year (e.g. "2001 (Gottlieb 1971)") intact.
            string normalized = Normalize(name);
            MatchCollection years = YearTokenRegex.Matches(normalized);
            if (years.Count > 0)
            {
                Match last = years[years.Count - 1];
                return normalized.Substring(0, last.Index + last.Length).Trim();
            }

            return normalized;
        }

        private static string DisplayTitle(string title)
        {
            Match match = TrailingArticleRegex.Match(title);
            return match.Success ? $"{match.Groups["article"].Value} {match.Groups["rest"].Value}" : title;
        }
    }

    /// <summary>
    /// A table name split into its parts. Manufacturer is null and Year is 0 when the
    /// name carries no "(Manufacturer Year)" group.
    /// </summary>
    public readonly struct ParsedName
    {
        public readonly string Title;
        public readonly string Manufacturer;
        public readonly int Year;

        public ParsedName(string title, string manufacturer, int year)
        {
            Title = title;
            Manufacturer = manufacturer;
            Year = year;
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/TableNaming.cs
```

- [ ] **Step 9: Run the tests to see them pass**

```bash
git add Assets/Scripts/Core
git commit -m "Add table name parsing shared by the catalog and wheel matching"
tools/remote.sh test
```

Expected: `EditMode tests: 12 passed, 0 failed, 0 skipped (total 12)`.

---

## Task 2: Table catalog and media resolution

**Files:**
- Create: `Assets/Scripts/Core/FileSystem.cs`, `Assets/Scripts/Core/TableEntry.cs`, `Assets/Scripts/Core/WheelIndex.cs`, `Assets/Scripts/Core/TableCatalog.cs`
- Create: `Assets/Scripts/LauncherPaths.cs`
- Modify: `Assets/Scripts/LauncherConfig.cs` (new `tableMediaDirectory` field), `launcher-config.json`
- Test: `Assets/Tests/EditMode/FakeFileSystem.cs`, `Assets/Tests/EditMode/TableCatalogTests.cs`

**Interfaces:**
- Consumes: `TableNaming.Parse`, `TableNaming.Normalize`, `TableNaming.TitleYearSignature` (Task 1).
- Produces:
  - `interface IFileSystem { bool DirectoryExists(string); bool FileExists(string); IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions); }` and `sealed class DiskFileSystem : IFileSystem`.
  - `sealed class MediaSet { string Wheel, Playfield, Backglass, Video; }` (absolute paths or null).
  - `sealed class TableEntry { string RelativePath; string FullPath; string Stem; string Title; string Manufacturer; int Year; MediaSet Media; string Subtitle; }`.
  - `sealed class WheelIndex { WheelIndex(IEnumerable<string>); string Find(string tableStem, out string strategy); }`.
  - `sealed class CatalogSettings { string TablesDirectory; bool SearchSubdirectories; string TableMediaDirectory; string WheelDirectory; }` and `static class TableCatalog { const string WheelFile, PlayfieldFile, BackglassFile, VideoFile; List<TableEntry> Scan(CatalogSettings, IFileSystem); }`.
  - `static class LauncherPaths { string AppDirectory; string Resolve(string); CatalogSettings CatalogSettingsFrom(LauncherConfig); }` (default assembly).

- [ ] **Step 1: Write the test file system and the failing catalog tests**

`Assets/Tests/EditMode/FakeFileSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VRLauncher.Tests
{
    /// <summary>In-memory file tree for catalog tests. Directories exist when they hold a file.</summary>
    public sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystem Add(params string[] paths)
        {
            foreach (string path in paths)
            {
                files.Add(Path.GetFullPath(path));
            }
            return this;
        }

        public bool FileExists(string path) => files.Contains(Path.GetFullPath(path));

        public bool DirectoryExists(string path)
        {
            string prefix = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return files.Any(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions)
        {
            string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            return files
                .Where(f => recursive
                    ? f.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase))
                .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
```

`Assets/Tests/EditMode/TableCatalogTests.cs`:

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableCatalogTests
    {
        private const string Tables = "C:\fake\Tables";
        private const string Media = "C:\fake\Launcher\Media\Tables";
        private const string Wheels = "C:\fake\Launcher\Media\Wheel";

        private static CatalogSettings Settings(bool recursive = true) => new CatalogSettings
        {
            TablesDirectory = Tables,
            SearchSubdirectories = recursive,
            TableMediaDirectory = Media,
            WheelDirectory = Wheels
        };

        private static string T(string name) => Path.Combine(Tables, name);
        private static string M(string table, string file) => Path.Combine(Media, table, file);
        private static string W(string file) => Path.Combine(Wheels, file);

        [Test]
        public void Scan_MissingTablesDirectory_ReturnsEmpty()
        {
            Assert.IsEmpty(TableCatalog.Scan(Settings(), new FakeFileSystem()));
        }

        [Test]
        public void Scan_FindsTablesRecursivelyAndSortsByStem()
        {
            var fs = new FakeFileSystem().Add(
                T("White Water (Williams 1993).vpx"),
                T("Sub\Attack from Mars (Bally 1995).vpx"),
                T("notes.txt"));

            var tables = TableCatalog.Scan(Settings(), fs);

            CollectionAssert.AreEqual(
                new[] { "Sub\Attack from Mars (Bally 1995).vpx", "White Water (Williams 1993).vpx" },
                tables.Select(t => t.RelativePath).ToArray());
            Assert.AreEqual(T("Sub\Attack from Mars (Bally 1995).vpx"), tables[0].FullPath);
            Assert.AreEqual("Attack from Mars (Bally 1995)", tables[0].Stem);
        }

        [Test]
        public void Scan_NotRecursive_IgnoresSubfolders()
        {
            var fs = new FakeFileSystem().Add(T("A (Bally 1990).vpx"), T("Sub\B (Bally 1991).vpx"));
            Assert.AreEqual(1, TableCatalog.Scan(Settings(recursive: false), fs).Count);
        }

        [Test]
        public void Scan_ParsesDisplayTitleManufacturerAndYear()
        {
            var fs = new FakeFileSystem().Add(T("Simpsons Pinball Party, The (Stern 2003).vpx"));
            TableEntry entry = TableCatalog.Scan(Settings(), fs).Single();
            Assert.AreEqual("The Simpsons Pinball Party", entry.Title);
            Assert.AreEqual("Stern", entry.Manufacturer);
            Assert.AreEqual(2003, entry.Year);
            Assert.AreEqual("Stern · 2003", entry.Subtitle);
        }

        [Test]
        public void Subtitle_IsEmptyWithoutManufacturer()
        {
            var fs = new FakeFileSystem().Add(T("Pinball Training Lab.vpx"));
            Assert.AreEqual(string.Empty, TableCatalog.Scan(Settings(), fs).Single().Subtitle);
        }

        [Test]
        public void Media_PerTableFolderIsUsedAndWinsOverWheelPack()
        {
            const string name = "Attack from Mars (Bally 1995)";
            var fs = new FakeFileSystem().Add(
                T(name + ".vpx"),
                M(name, TableCatalog.WheelFile), M(name, TableCatalog.PlayfieldFile),
                M(name, TableCatalog.BackglassFile), M(name, TableCatalog.VideoFile),
                W(name + ".png"));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(M(name, "wheel.png"), media.Wheel);
            Assert.AreEqual(M(name, "table.png"), media.Playfield);
            Assert.AreEqual(M(name, "bg.png"), media.Backglass);
            Assert.AreEqual(M(name, "table.mp4"), media.Video);
        }

        [Test]
        public void Media_WheelFallsBackToPackUsingTitleYearMatch()
        {
            var fs = new FakeFileSystem().Add(
                T("Big Bang Bar (Capcom 1996) VPW 2.1.vpx"),
                W("Big Bang Bar (Capcom 1996).png"));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(W("Big Bang Bar (Capcom 1996).png"), media.Wheel);
            Assert.IsNull(media.Playfield);
        }

        [Test]
        public void Media_PartialFolderLeavesOthersNull()
        {
            const string name = "Congo (Williams 1995)";
            var fs = new FakeFileSystem().Add(T(name + ".vpx"), M(name, TableCatalog.VideoFile));

            MediaSet media = TableCatalog.Scan(Settings(), fs).Single().Media;

            Assert.AreEqual(M(name, "table.mp4"), media.Video);
            Assert.IsNull(media.Playfield);
            Assert.IsNull(media.Backglass);
            Assert.IsNull(media.Wheel);
        }

        [Test]
        public void Media_NoMediaOrWheelDirectories_AllNull()
        {
            var fs = new FakeFileSystem().Add(T("Congo (Williams 1995).vpx"));
            var settings = Settings();
            settings.TableMediaDirectory = null;
            settings.WheelDirectory = null;

            MediaSet media = TableCatalog.Scan(settings, fs).Single().Media;

            Assert.IsNull(media.Wheel);
            Assert.IsNull(media.Video);
        }

        [Test]
        public void WheelIndex_PrefersLeastDecoratedName()
        {
            var index = new WheelIndex(new[] { W("X-Men (Stern 2012) Alt.png"), W("X-Men (Stern 2012).png") });
            Assert.AreEqual(W("X-Men (Stern 2012).png"), index.Find("X-Men (Stern 2012) VPW", out string strategy));
            Assert.AreEqual("title+year", strategy);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/FakeFileSystem.cs Assets/Tests/EditMode/TableCatalogTests.cs
git add Assets/Tests && git commit -m "Add catalog tests"
tools/remote.sh test
```

Expected: compilation fails (`IFileSystem`, `TableCatalog`, `CatalogSettings` not found).

- [ ] **Step 2: Implement the Core types**

`Assets/Scripts/Core/FileSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VRLauncher
{
    /// <summary>The file-system calls the catalog needs, so tests can use an in-memory tree.</summary>
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);

        /// <summary>
        /// Files in <paramref name="directory"/> (and below it when <paramref name="recursive"/>)
        /// whose extension, including the dot, matches one of <paramref name="extensions"/>, ignoring case.
        /// </summary>
        IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions);
    }

    /// <summary>The real disk.</summary>
    public sealed class DiskFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, bool recursive, params string[] extensions)
        {
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(directory, "*", option)
                .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
```

`Assets/Scripts/Core/TableEntry.cs`:

```csharp
namespace VRLauncher
{
    /// <summary>Absolute paths to a table's media; null where the file does not exist.</summary>
    public sealed class MediaSet
    {
        public string Wheel;
        public string Playfield;
        public string Backglass;
        public string Video;
    }

    /// <summary>One .vpx table, its display name and its media.</summary>
    public sealed class TableEntry
    {
        /// <summary>Path relative to the tables directory; the key for favorites and play history.</summary>
        public string RelativePath { get; }
        public string FullPath { get; }
        /// <summary>File name without extension, e.g. "Attack from Mars (Bally 1995)".</summary>
        public string Stem { get; }
        public string Title { get; }
        /// <summary>Null when the file name has no "(Manufacturer Year)" group.</summary>
        public string Manufacturer { get; }
        /// <summary>0 when unknown.</summary>
        public int Year { get; }
        public MediaSet Media { get; }

        /// <summary>"Bally · 1995", or empty when the manufacturer is unknown.</summary>
        public string Subtitle => Manufacturer == null ? string.Empty : $"{Manufacturer} · {Year}";

        public TableEntry(string relativePath, string fullPath, string stem, string title,
                          string manufacturer, int year, MediaSet media)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Stem = stem;
            Title = title;
            Manufacturer = manufacturer;
            Year = year;
            Media = media ?? new MediaSet();
        }
    }
}
```

`Assets/Scripts/Core/WheelIndex.cs` (the matching moved from `TableScanner.LoadWheelImages`):

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace VRLauncher
{
    /// <summary>
    /// Looks up wheel art for a table in a flat wheel pack, loosening the match only as far
    /// as needed: exact file name, then normalised name, then title plus manufacturer and year.
    /// Table files carry author/version decoration ("... VPW v1.0.1") that wheel art does not.
    /// </summary>
    public sealed class WheelIndex
    {
        private readonly Dictionary<string, string> byExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> byNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> byTitleYear = new Dictionary<string, string>(StringComparer.Ordinal);

        public WheelIndex(IEnumerable<string> imagePaths)
        {
            foreach (string image in imagePaths)
            {
                string name = Path.GetFileNameWithoutExtension(image);
                AddCandidate(byExact, name, image);
                AddCandidate(byNormalized, TableNaming.Normalize(name), image);
                AddCandidate(byTitleYear, TableNaming.TitleYearSignature(name), image);
            }
        }

        /// <summary>The best wheel image for a table stem, or null. Strategy is "exact", "normalized" or "title+year".</summary>
        public string Find(string tableStem, out string strategy)
        {
            if (byExact.TryGetValue(tableStem, out string image)) { strategy = "exact"; return image; }
            if (byNormalized.TryGetValue(TableNaming.Normalize(tableStem), out image)) { strategy = "normalized"; return image; }
            if (byTitleYear.TryGetValue(TableNaming.TitleYearSignature(tableStem), out image)) { strategy = "title+year"; return image; }
            strategy = null;
            return null;
        }

        /// <summary>
        /// Records a lookup key, preferring the least decorated filename when
        /// several images share it, so "Table (Maker 1995).png" wins over
        /// "Table (Maker 1995) BW Mod.png".
        /// </summary>
        private static void AddCandidate(Dictionary<string, string> map, string key, string path)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (map.TryGetValue(key, out string existing) &&
                Path.GetFileNameWithoutExtension(existing).Length <= Path.GetFileNameWithoutExtension(path).Length)
            {
                return;
            }

            map[key] = path;
        }
    }
}
```

`Assets/Scripts/Core/TableCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace VRLauncher
{
    /// <summary>Absolute directories the catalog scans. Media and wheel directories may be null.</summary>
    public sealed class CatalogSettings
    {
        public string TablesDirectory;
        public bool SearchSubdirectories = true;
        public string TableMediaDirectory;
        public string WheelDirectory;
    }

    /// <summary>
    /// Finds every .vpx table and resolves its media: first from
    /// &lt;TableMediaDirectory&gt;\&lt;stem&gt;\ (fetched by tools/fetch_media.py), then, for the
    /// wheel only, from the flat wheel pack.
    /// </summary>
    public static class TableCatalog
    {
        public const string WheelFile = "wheel.png";
        public const string PlayfieldFile = "table.png";
        public const string BackglassFile = "bg.png";
        public const string VideoFile = "table.mp4";

        public static List<TableEntry> Scan(CatalogSettings settings, IFileSystem fs)
        {
            var tables = new List<TableEntry>();
            if (string.IsNullOrEmpty(settings.TablesDirectory) || !fs.DirectoryExists(settings.TablesDirectory))
            {
                return tables;
            }

            WheelIndex wheels = !string.IsNullOrEmpty(settings.WheelDirectory) && fs.DirectoryExists(settings.WheelDirectory)
                ? new WheelIndex(fs.GetFiles(settings.WheelDirectory, false, ".png", ".jpg", ".jpeg"))
                : null;

            foreach (string path in fs.GetFiles(settings.TablesDirectory, settings.SearchSubdirectories, ".vpx"))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                ParsedName name = TableNaming.Parse(stem);
                tables.Add(new TableEntry(
                    Path.GetRelativePath(settings.TablesDirectory, path),
                    path,
                    stem,
                    name.Title,
                    name.Manufacturer,
                    name.Year,
                    ResolveMedia(stem, settings.TableMediaDirectory, wheels, fs)));
            }

            tables.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Stem, b.Stem));
            return tables;
        }

        private static MediaSet ResolveMedia(string stem, string mediaRoot, WheelIndex wheels, IFileSystem fs)
        {
            string folder = string.IsNullOrEmpty(mediaRoot) ? null : Path.Combine(mediaRoot, stem);

            string Existing(string file)
            {
                if (folder == null)
                {
                    return null;
                }
                string candidate = Path.Combine(folder, file);
                return fs.FileExists(candidate) ? candidate : null;
            }

            var media = new MediaSet
            {
                Wheel = Existing(WheelFile),
                Playfield = Existing(PlayfieldFile),
                Backglass = Existing(BackglassFile),
                Video = Existing(VideoFile)
            };

            if (media.Wheel == null && wheels != null)
            {
                media.Wheel = wheels.Find(stem, out _);
            }

            return media;
        }
    }
}
```

`Assets/Scripts/LauncherPaths.cs` (default assembly, because it reads `LauncherConfig`):

```csharp
using System.IO;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Resolves configured directories. Relative paths are relative to the launcher's own
    /// folder (next to vr-launch.exe), or to the project root in the editor.
    /// </summary>
    public static class LauncherPaths
    {
        public static string AppDirectory => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            : Path.GetDirectoryName(Application.dataPath);

        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppDirectory, path));
        }

        public static CatalogSettings CatalogSettingsFrom(LauncherConfig config) => new CatalogSettings
        {
            TablesDirectory = config.tablesDirectory,
            SearchSubdirectories = config.searchSubdirectories,
            TableMediaDirectory = Resolve(config.tableMediaDirectory),
            WheelDirectory = Resolve(config.wheelDirectory)
        };
    }
}
```

In `LauncherConfig.cs`, directly after the `wheelDirectory` field add:

```csharp
        [Tooltip("Folder of per-table media fetched by tools/fetch_media.py, one subfolder per table (supports both relative and absolute paths)")]
        public string tableMediaDirectory = "Media\Tables";
```

In `launcher-config.json`, after the `wheelDirectory` line add `"tableMediaDirectory": "Media\\Tables",`.

```bash
tools/new-meta.sh Assets/Scripts/Core/FileSystem.cs Assets/Scripts/Core/TableEntry.cs \
  Assets/Scripts/Core/WheelIndex.cs Assets/Scripts/Core/TableCatalog.cs Assets/Scripts/LauncherPaths.cs
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add Assets launcher-config.json
git commit -m "Add a table catalog that resolves per-table media with a wheel-pack fallback"
tools/remote.sh test
```

Expected: all tests pass (12 from Task 1 plus 10 new = 22).

---

## Task 3: Favorites and play history

**Files:**
- Create: `Assets/Scripts/Core/LauncherState.cs`, `Assets/Scripts/Core/RelativeTime.cs`
- Test: `Assets/Tests/EditMode/LauncherStateTests.cs`, `Assets/Tests/EditMode/RelativeTimeTests.cs`

**Interfaces:**
- Produces:
  - `sealed class LauncherState { static LauncherState Load(string filePath); string FilePath; string LoadWarning; bool IsFavorite(string); bool ToggleFavorite(string) /* returns new state */; void RecordPlay(string, DateTime utcNow); DateTime? LastPlayed(string); int PlayCount(string); IReadOnlyList<string> RecentPaths(); void Save(); }`.
  - `static class RelativeTime { string Format(DateTime lastUtc, DateTime nowUtc); }`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/LauncherStateTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class LauncherStateTests
    {
        private string dir;
        private string file;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-state-" + Guid.NewGuid().ToString("N"));
            file = Path.Combine(dir, "state.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static readonly DateTime Noon = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Load_MissingFile_IsEmpty()
        {
            LauncherState state = LauncherState.Load(file);
            Assert.IsFalse(state.IsFavorite("a.vpx"));
            Assert.IsEmpty(state.RecentPaths());
            Assert.IsNull(state.LoadWarning);
        }

        [Test]
        public void Favorites_SurviveSaveAndLoad()
        {
            LauncherState state = LauncherState.Load(file);
            Assert.IsTrue(state.ToggleFavorite("a.vpx"));
            state.Save();

            LauncherState reloaded = LauncherState.Load(file);
            Assert.IsTrue(reloaded.IsFavorite("a.vpx"));
            Assert.IsFalse(reloaded.ToggleFavorite("a.vpx"));
            Assert.IsFalse(reloaded.IsFavorite("a.vpx"));
        }

        [Test]
        public void RecordPlay_TracksCountAndLastPlayed()
        {
            LauncherState state = LauncherState.Load(file);
            state.RecordPlay("a.vpx", Noon);
            state.RecordPlay("a.vpx", Noon.AddHours(1));
            state.Save();

            LauncherState reloaded = LauncherState.Load(file);
            Assert.AreEqual(2, reloaded.PlayCount("a.vpx"));
            Assert.AreEqual(Noon.AddHours(1), reloaded.LastPlayed("a.vpx"));
            Assert.IsNull(reloaded.LastPlayed("b.vpx"));
        }

        [Test]
        public void RecentPaths_NewestFirst()
        {
            LauncherState state = LauncherState.Load(file);
            state.RecordPlay("old.vpx", Noon);
            state.RecordPlay("new.vpx", Noon.AddDays(1));
            state.RecordPlay("mid.vpx", Noon.AddHours(5));
            CollectionAssert.AreEqual(new[] { "new.vpx", "mid.vpx", "old.vpx" }, state.RecentPaths());
        }

        [Test]
        public void Load_Corrupt_IsSetAsideAndStartsEmpty()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{ not json");

            LauncherState state = LauncherState.Load(file);

            Assert.IsFalse(File.Exists(file));
            Assert.AreEqual("{ not json", File.ReadAllText(file + ".bad"));
            Assert.IsNotNull(state.LoadWarning);
            Assert.IsEmpty(state.RecentPaths());
        }

        [Test]
        public void Load_EmptyFile_IsSetAside()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "");

            LauncherState state = LauncherState.Load(file);

            Assert.IsTrue(File.Exists(file + ".bad"));
            Assert.IsNotNull(state.LoadWarning);
        }

        [Test]
        public void Load_Corrupt_ReplacesAnOlderBadFile()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file + ".bad", "older");
            File.WriteAllText(file, "newer garbage");

            LauncherState.Load(file);

            Assert.AreEqual("newer garbage", File.ReadAllText(file + ".bad"));
        }

        [Test]
        public void Load_UnknownVersion_IsSetAside()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "{\"version\": 99, \"favorites\": [], \"plays\": []}");

            LauncherState state = LauncherState.Load(file);

            Assert.IsNotNull(state.LoadWarning);
            Assert.IsTrue(File.Exists(file + ".bad"));
        }

        [Test]
        public void Save_CreatesTheDirectory()
        {
            LauncherState state = LauncherState.Load(file);
            state.ToggleFavorite("a.vpx");
            state.Save();
            Assert.IsTrue(File.Exists(file));
            Assert.IsFalse(File.Exists(file + ".tmp"));
        }
    }
}
```

`Assets/Tests/EditMode/RelativeTimeTests.cs`:

```csharp
using System;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class RelativeTimeTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [TestCase(0, "just now")]
        [TestCase(-30, "just now")]            // clock skew: last play in the future
        [TestCase(59, "just now")]
        [TestCase(60, "1 minute ago")]
        [TestCase(45 * 60, "45 minutes ago")]
        [TestCase(60 * 60, "1 hour ago")]
        [TestCase(23 * 3600, "23 hours ago")]
        [TestCase(30 * 3600, "yesterday")]
        [TestCase(3 * 86400, "3 days ago")]
        [TestCase(13 * 86400, "13 days ago")]
        public void Format_ReadsNaturally(int secondsAgo, string expected)
        {
            Assert.AreEqual(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));
        }

        [Test]
        public void Format_OlderThanTwoWeeks_ShowsTheDate()
        {
            Assert.AreEqual("on 1 Aug 2026", RelativeTime.Format(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc), Now));
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/LauncherStateTests.cs Assets/Tests/EditMode/RelativeTimeTests.cs
git add Assets/Tests && git commit -m "Add state and relative time tests"
tools/remote.sh test
```

Expected: compilation fails (`LauncherState`, `RelativeTime` not found).

- [ ] **Step 2: Implement**

`Assets/Scripts/Core/LauncherState.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Favorites and play history, persisted as JSON. Keys are table paths relative to the
    /// tables directory, so renaming a table drops its history. An unreadable file is moved
    /// aside to "state.json.bad" and the launcher starts fresh rather than failing.
    /// </summary>
    public sealed class LauncherState
    {
        public const int CurrentVersion = 1;

        [Serializable]
        private sealed class PlayRecord
        {
            public string path;
            public string last;   // ISO 8601, UTC
            public int count;
        }

        // JsonUtility cannot serialise dictionaries, so plays are a list of records.
        [Serializable]
        private sealed class StateData
        {
            public int version = CurrentVersion;
            public List<string> favorites = new List<string>();
            public List<PlayRecord> plays = new List<PlayRecord>();
        }

        private readonly StateData data;

        public string FilePath { get; }

        /// <summary>Set when an unreadable state file was moved aside during Load.</summary>
        public string LoadWarning { get; private set; }

        private LauncherState(string filePath, StateData data)
        {
            FilePath = filePath;
            this.data = data;
        }

        public static LauncherState Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new LauncherState(filePath, new StateData());
            }

            string json = File.ReadAllText(filePath);
            StateData loaded = null;
            string problem = null;
            try
            {
                loaded = JsonUtility.FromJson<StateData>(json);
            }
            catch (ArgumentException ex)
            {
                problem = ex.Message;
            }

            if (problem == null && (loaded == null || loaded.version != CurrentVersion || loaded.favorites == null || loaded.plays == null))
            {
                problem = "unrecognised contents";
            }

            if (problem == null)
            {
                return new LauncherState(filePath, loaded);
            }

            string bad = filePath + ".bad";
            if (File.Exists(bad))
            {
                File.Delete(bad);
            }
            File.Move(filePath, bad);
            return new LauncherState(filePath, new StateData())
            {
                LoadWarning = $"State file was unreadable ({problem}); moved it to {bad} and started fresh."
            };
        }

        public bool IsFavorite(string relativePath) => data.favorites.Contains(relativePath);

        /// <summary>Flips the favorite flag and returns the new value.</summary>
        public bool ToggleFavorite(string relativePath)
        {
            if (data.favorites.Remove(relativePath))
            {
                return false;
            }
            data.favorites.Add(relativePath);
            return true;
        }

        public void RecordPlay(string relativePath, DateTime utcNow)
        {
            PlayRecord record = data.plays.FirstOrDefault(p => p.path == relativePath);
            if (record == null)
            {
                record = new PlayRecord { path = relativePath };
                data.plays.Add(record);
            }
            record.last = utcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            record.count++;
        }

        public DateTime? LastPlayed(string relativePath)
        {
            PlayRecord record = data.plays.FirstOrDefault(p => p.path == relativePath);
            return record == null ? null : ParseTime(record.last);
        }

        public int PlayCount(string relativePath) =>
            data.plays.FirstOrDefault(p => p.path == relativePath)?.count ?? 0;

        /// <summary>Played tables, most recent first.</summary>
        public IReadOnlyList<string> RecentPaths() => data.plays
            .Select(p => (p.path, when: ParseTime(p.last)))
            .Where(p => p.when.HasValue)
            .OrderByDescending(p => p.when.Value)
            .ThenBy(p => p.path, StringComparer.Ordinal)
            .Select(p => p.path)
            .ToList();

        /// <summary>Writes atomically: a temp file, then a replace, so a crash never leaves half a file.</summary>
        public void Save()
        {
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }

        private static DateTime? ParseTime(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed.ToUniversalTime()
                : (DateTime?)null;
    }
}
```

`Assets/Scripts/Core/RelativeTime.cs`:

```csharp
using System;
using System.Globalization;

namespace VRLauncher
{
    /// <summary>"3 days ago" style wording for the info plate.</summary>
    public static class RelativeTime
    {
        public static string Format(DateTime lastUtc, DateTime nowUtc)
        {
            TimeSpan ago = nowUtc - lastUtc;
            if (ago < TimeSpan.FromMinutes(1)) return "just now";
            if (ago < TimeSpan.FromHours(1)) return Plural((int)ago.TotalMinutes, "minute");
            if (ago < TimeSpan.FromHours(24)) return Plural((int)ago.TotalHours, "hour");
            if (ago < TimeSpan.FromHours(48)) return "yesterday";
            if (ago < TimeSpan.FromDays(14)) return Plural((int)ago.TotalDays, "day");
            return "on " + lastUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/LauncherState.cs Assets/Scripts/Core/RelativeTime.cs
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add Assets/Scripts/Core
git commit -m "Persist favorites and play history, and word play times naturally"
tools/remote.sh test
```

Expected: all tests pass.

---

## Task 4: The table list view

**Files:**
- Create: `Assets/Scripts/Core/TableListView.cs`
- Test: `Assets/Tests/EditMode/TestEntries.cs`, `Assets/Tests/EditMode/TableListViewTests.cs`

**Interfaces:**
- Consumes: `TableEntry` (Task 2), `LauncherState` (Task 3).
- Produces: `enum ViewKind { All, Favorites, Recent }`; `sealed class TableListView { TableListView(IReadOnlyList<TableEntry> all, LauncherState state); ViewKind Kind; IReadOnlyList<TableEntry> Items; int SelectedIndex; TableEntry Selected; void Next(); void Previous(); TableEntry EntryAt(int offset); void CycleView(); void Refresh(); bool Select(string relativePath); string PositionLabel; string EmptyMessage; }`; test helper `TestEntries.Make(int count)` / `TestEntries.Named(params string[] stems)`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/TestEntries.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace VRLauncher.Tests
{
    /// <summary>Catalog entries without media, for list and room tests.</summary>
    public static class TestEntries
    {
        public static List<TableEntry> Make(int count) =>
            Named(Enumerable.Range(0, count).Select(i => $"Table {i:00} (Bally {1990 + i})").ToArray());

        public static List<TableEntry> Named(params string[] stems) => stems
            .Select(s =>
            {
                ParsedName name = TableNaming.Parse(s);
                return new TableEntry(s + ".vpx", "C:\fake\Tables\" + s + ".vpx", s, name.Title, name.Manufacturer, name.Year, new MediaSet());
            })
            .ToList();
    }
}
```

`Assets/Tests/EditMode/TableListViewTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class TableListViewTests
    {
        private string dir;
        private LauncherState state;
        private static readonly DateTime Noon = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-view-" + Guid.NewGuid().ToString("N"));
            state = LauncherState.Load(Path.Combine(dir, "state.json"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void All_ListsEveryTableAndStartsAtTheFirst()
        {
            var view = new TableListView(TestEntries.Make(5), state);
            Assert.AreEqual(ViewKind.All, view.Kind);
            Assert.AreEqual(5, view.Items.Count);
            Assert.AreEqual(0, view.SelectedIndex);
        }

        [Test]
        public void NextAndPrevious_WrapAround()
        {
            var view = new TableListView(TestEntries.Make(3), state);
            view.Previous();
            Assert.AreEqual(2, view.SelectedIndex);
            view.Next();
            view.Next();
            Assert.AreEqual(1, view.SelectedIndex);
        }

        [Test]
        public void EntryAt_WrapsBothWays()
        {
            List<TableEntry> all = TestEntries.Make(4);
            var view = new TableListView(all, state);
            Assert.AreSame(all[3], view.EntryAt(-1));
            Assert.AreSame(all[1], view.EntryAt(5));
        }

        [Test]
        public void Empty_HasNoSelectionAndNavigationIsSafe()
        {
            var view = new TableListView(new List<TableEntry>(), state);
            view.Next();
            view.Previous();
            Assert.IsNull(view.Selected);
            Assert.IsNull(view.EntryAt(2));
            Assert.AreEqual("No tables found", view.EmptyMessage);
        }

        [Test]
        public void CycleView_GoesAllFavoritesRecentAll()
        {
            var view = new TableListView(TestEntries.Make(2), state);
            view.CycleView();
            Assert.AreEqual(ViewKind.Favorites, view.Kind);
            view.CycleView();
            Assert.AreEqual(ViewKind.Recent, view.Kind);
            view.CycleView();
            Assert.AreEqual(ViewKind.All, view.Kind);
        }

        [Test]
        public void CycleView_KeepsTheSelectedTableWhenItIsInTheNextView()
        {
            List<TableEntry> all = TestEntries.Make(5);
            state.ToggleFavorite(all[1].RelativePath);
            state.ToggleFavorite(all[3].RelativePath);
            var view = new TableListView(all, state);
            view.Select(all[3].RelativePath);

            view.CycleView();

            Assert.AreSame(all[3], view.Selected);
            Assert.AreEqual(1, view.SelectedIndex);
        }

        [Test]
        public void CycleView_StartsAtTheTopWhenTheTableIsNotInTheNextView()
        {
            List<TableEntry> all = TestEntries.Make(5);
            state.ToggleFavorite(all[1].RelativePath);
            var view = new TableListView(all, state);
            view.Select(all[4].RelativePath);

            view.CycleView();

            Assert.AreSame(all[1], view.Selected);
        }

        [Test]
        public void Favorites_UnfavoriteSelected_MovesToTheNextOne()
        {
            List<TableEntry> all = TestEntries.Make(5);
            foreach (int i in new[] { 0, 2, 4 }) state.ToggleFavorite(all[i].RelativePath);
            var view = new TableListView(all, state);
            view.CycleView();
            view.Select(all[2].RelativePath);

            state.ToggleFavorite(all[2].RelativePath);
            view.Refresh();

            Assert.AreEqual(2, view.Items.Count);
            Assert.AreSame(all[4], view.Selected);
        }

        [Test]
        public void Favorites_UnfavoriteLastRemaining_BecomesEmpty()
        {
            List<TableEntry> all = TestEntries.Make(3);
            state.ToggleFavorite(all[2].RelativePath);
            var view = new TableListView(all, state);
            view.CycleView();

            state.ToggleFavorite(all[2].RelativePath);
            view.Refresh();

            Assert.IsNull(view.Selected);
            Assert.AreEqual(0, view.SelectedIndex);
            Assert.AreEqual("No favorites yet", view.EmptyMessage);
        }

        [Test]
        public void Recent_IsNewestFirstAndFollowsNewPlays()
        {
            List<TableEntry> all = TestEntries.Make(4);
            state.RecordPlay(all[0].RelativePath, Noon);
            state.RecordPlay(all[2].RelativePath, Noon.AddHours(1));
            var view = new TableListView(all, state);
            view.CycleView();
            view.CycleView();
            CollectionAssert.AreEqual(new[] { all[2], all[0] }, view.Items);

            view.Select(all[0].RelativePath);
            state.RecordPlay(all[0].RelativePath, Noon.AddHours(2));
            view.Refresh();

            CollectionAssert.AreEqual(new[] { all[0], all[2] }, view.Items);
            Assert.AreSame(all[0], view.Selected);
        }

        [Test]
        public void Recent_And_Favorites_IgnoreMissingTables()
        {
            List<TableEntry> all = TestEntries.Make(2);
            state.RecordPlay("Renamed Table (Bally 1999).vpx", Noon);
            state.ToggleFavorite("Deleted Table (Bally 1998).vpx");
            var view = new TableListView(all, state);

            view.CycleView();
            Assert.AreEqual(0, view.Items.Count);
            view.CycleView();
            Assert.AreEqual(0, view.Items.Count);
            Assert.AreEqual("Nothing played yet", view.EmptyMessage);
        }

        [Test]
        public void PositionLabel_ShowsViewAndPosition()
        {
            var view = new TableListView(TestEntries.Make(42), state);
            view.Next();
            Assert.AreEqual("All · 2 / 42", view.PositionLabel);
            view.CycleView();
            Assert.AreEqual("Favorites", view.PositionLabel);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/TestEntries.cs Assets/Tests/EditMode/TableListViewTests.cs
git add Assets/Tests && git commit -m "Add list view tests"
tools/remote.sh test
```

Expected: compilation fails (`TableListView`, `ViewKind` not found).

- [ ] **Step 2: Implement**

`Assets/Scripts/Core/TableListView.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace VRLauncher
{
    public enum ViewKind
    {
        All,
        Favorites,
        Recent
    }

    /// <summary>
    /// The list the arc shows (all tables, favorites, or recently played) and the selection
    /// within it. Navigation wraps. Switching views or refreshing keeps the same table
    /// selected whenever it is still in the list.
    /// </summary>
    public sealed class TableListView
    {
        private readonly IReadOnlyList<TableEntry> all;
        private readonly Dictionary<string, TableEntry> byPath;
        private readonly LauncherState state;
        private List<TableEntry> items = new List<TableEntry>();

        public ViewKind Kind { get; private set; } = ViewKind.All;
        public IReadOnlyList<TableEntry> Items => items;
        public int SelectedIndex { get; private set; }
        public TableEntry Selected => items.Count == 0 ? null : items[SelectedIndex];

        public TableListView(IReadOnlyList<TableEntry> all, LauncherState state)
        {
            this.all = all;
            this.state = state;
            byPath = all.GroupBy(e => e.RelativePath).ToDictionary(g => g.Key, g => g.First());
            Rebuild(null, 0);
        }

        public void Next()
        {
            if (items.Count > 0) SelectedIndex = (SelectedIndex + 1) % items.Count;
        }

        public void Previous()
        {
            if (items.Count > 0) SelectedIndex = (SelectedIndex - 1 + items.Count) % items.Count;
        }

        /// <summary>The entry <paramref name="offset"/> places from the selection, wrapping; null when empty.</summary>
        public TableEntry EntryAt(int offset)
        {
            int n = items.Count;
            if (n == 0) return null;
            return items[((SelectedIndex + offset) % n + n) % n];
        }

        public void CycleView()
        {
            TableEntry keep = Selected;
            Kind = (ViewKind)(((int)Kind + 1) % 3);
            Rebuild(keep, 0);
        }

        /// <summary>Rebuilds the list after favorites or play history changed.</summary>
        public void Refresh() => Rebuild(Selected, SelectedIndex);

        public bool Select(string relativePath)
        {
            int index = items.FindIndex(e => e.RelativePath == relativePath);
            if (index < 0) return false;
            SelectedIndex = index;
            return true;
        }

        /// <summary>"All · 12 / 42", or just the view name when it is empty.</summary>
        public string PositionLabel => items.Count == 0 ? Kind.ToString() : $"{Kind} · {SelectedIndex + 1} / {items.Count}";

        public string EmptyMessage
        {
            get
            {
                switch (Kind)
                {
                    case ViewKind.Favorites: return "No favorites yet";
                    case ViewKind.Recent: return "Nothing played yet";
                    default: return "No tables found";
                }
            }
        }

        private void Rebuild(TableEntry keep, int fallbackIndex)
        {
            switch (Kind)
            {
                case ViewKind.Favorites:
                    items = all.Where(e => state.IsFavorite(e.RelativePath)).ToList();
                    break;
                case ViewKind.Recent:
                    items = state.RecentPaths()
                        .Select(p => byPath.TryGetValue(p, out TableEntry e) ? e : null)
                        .Where(e => e != null)
                        .ToList();
                    break;
                default:
                    items = all.ToList();
                    break;
            }

            int kept = keep == null ? -1 : items.IndexOf(keep);
            SelectedIndex = kept >= 0 ? kept : (items.Count == 0 ? 0 : Math.Min(fallbackIndex, items.Count - 1));
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/TableListView.cs
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add Assets/Scripts/Core
git commit -m "Add the All, Favorites and Recent table views"
tools/remote.sh test
```

Expected: all tests pass.

---

## Task 5: Controller and keyboard input

**Files:**
- Create: `Assets/Scripts/Core/InputLogic.cs`
- Create: `Assets/Scripts/Arcade/` (+ `.meta`), `Assets/Scripts/Arcade/VRLauncher.Arcade.asmdef`, `Assets/Scripts/Arcade/LauncherInput.cs`
- Test: `Assets/Tests/EditMode/InputLogicTests.cs`

**Interfaces:**
- Produces (Core): `sealed class TriggerLatch { const float PressThreshold = 0.7f, ReleaseThreshold = 0.4f; bool Update(float value); }`, `sealed class ButtonEdge { bool Update(bool down); }`, `sealed class StickRepeat { int Update(float x, float deltaSeconds); }`, `sealed class HoldTimer { HoldTimer(float seconds); float Progress; bool IsHeld; bool Update(bool down, float deltaSeconds); }`.
- Produces (Arcade): `sealed class LauncherInput : MonoBehaviour { event Action Previous, Next, Launch, ToggleFavorite, CycleView, Quit; bool InputEnabled; float QuitHoldProgress; bool QuitHeld; const float QuitHoldSeconds = 2f; }`.

Controls (spec section 1, plus keyboard F and V): left trigger / Left Shift / Left Arrow = Previous; right trigger / Right Shift / Right Arrow = Next; either thumbstick left/right = Previous/Next with repeat; A / Enter / Space = Launch; B / F = ToggleFavorite; X / V = CycleView; hold Y 2 s = Quit; Esc = Quit.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/InputLogicTests.cs`:

```csharp
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class InputLogicTests
    {
        [Test]
        public void TriggerLatch_FiresOncePerSqueezeWithHysteresis()
        {
            var latch = new TriggerLatch();
            Assert.IsFalse(latch.Update(0.5f));
            Assert.IsTrue(latch.Update(0.8f));
            Assert.IsFalse(latch.Update(0.9f));
            Assert.IsFalse(latch.Update(0.5f));   // still above release: no re-arm
            Assert.IsFalse(latch.Update(0.8f));
            Assert.IsFalse(latch.Update(0.3f));   // re-armed
            Assert.IsTrue(latch.Update(0.75f));
        }

        [Test]
        public void ButtonEdge_FiresOnPressOnly()
        {
            var edge = new ButtonEdge();
            Assert.IsTrue(edge.Update(true));
            Assert.IsFalse(edge.Update(true));
            Assert.IsFalse(edge.Update(false));
            Assert.IsTrue(edge.Update(true));
        }

        [Test]
        public void StickRepeat_StepsImmediatelyThenRepeatsAndAccelerates()
        {
            var stick = new StickRepeat();
            Assert.AreEqual(1, stick.Update(0.9f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.9f, 0.3f));                 // inside the initial delay
            Assert.AreEqual(1, stick.Update(0.9f, 0.2f));                 // 0.5 s held: first repeat
            Assert.AreEqual(0, stick.Update(0.9f, 0.1f));
            Assert.AreEqual(1, stick.Update(0.9f, 0.15f));                // next repeat after about 0.22 s
        }

        [Test]
        public void StickRepeat_ReleaseAndReverse()
        {
            var stick = new StickRepeat();
            Assert.AreEqual(-1, stick.Update(-0.8f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.8f, 0.016f));               // reversal releases first
            Assert.AreEqual(1, stick.Update(0.8f, 0.016f));
            Assert.AreEqual(0, stick.Update(0.2f, 0.016f));               // released
            Assert.AreEqual(0, stick.Update(0.5f, 0.016f));               // below press threshold
        }

        [Test]
        public void HoldTimer_FiresOnceAfterTheDurationAndResetsOnRelease()
        {
            var hold = new HoldTimer(2f);
            Assert.IsFalse(hold.Update(true, 1f));
            Assert.AreEqual(0.5f, hold.Progress, 1e-5f);
            Assert.IsTrue(hold.Update(true, 1f));
            Assert.IsFalse(hold.Update(true, 1f));
            Assert.IsFalse(hold.Update(false, 0.016f));
            Assert.IsFalse(hold.IsHeld);
            Assert.AreEqual(0f, hold.Progress);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/InputLogicTests.cs
git add Assets/Tests && git commit -m "Add input logic tests"
tools/remote.sh test
```

Expected: compilation fails (`TriggerLatch` and friends not found).

- [ ] **Step 2: Implement the Core input logic**

`Assets/Scripts/Core/InputLogic.cs`:

```csharp
using System;

namespace VRLauncher
{
    /// <summary>
    /// Turns an analog trigger into single presses: fires when it rises above the press
    /// threshold and re-arms only after it drops below the release threshold.
    /// </summary>
    public sealed class TriggerLatch
    {
        public const float PressThreshold = 0.7f;
        public const float ReleaseThreshold = 0.4f;
        private bool held;

        public bool Update(float value)
        {
            if (!held && value > PressThreshold)
            {
                held = true;
                return true;
            }
            if (held && value < ReleaseThreshold)
            {
                held = false;
            }
            return false;
        }
    }

    /// <summary>Fires on the frame a button goes down.</summary>
    public sealed class ButtonEdge
    {
        private bool was;

        public bool Update(bool down)
        {
            bool fired = down && !was;
            was = down;
            return fired;
        }
    }

    /// <summary>
    /// Thumbstick browsing: one step when the stick is pushed, then repeats while held,
    /// getting faster the longer it is held. Returns -1, 0 or +1 for this frame.
    /// </summary>
    public sealed class StickRepeat
    {
        public const float PressThreshold = 0.6f;
        public const float ReleaseThreshold = 0.3f;
        public const float InitialDelay = 0.45f;
        public const float StartInterval = 0.22f;
        public const float MinInterval = 0.07f;
        public const float Acceleration = 0.85f;

        private int direction;
        private float timer;
        private float interval;

        public int Update(float x, float deltaSeconds)
        {
            if (direction == 0)
            {
                if (x > PressThreshold) direction = 1;
                else if (x < -PressThreshold) direction = -1;
                else return 0;

                timer = InitialDelay;
                interval = StartInterval;
                return direction;
            }

            if (Math.Abs(x) < ReleaseThreshold || Math.Sign(x) != direction)
            {
                direction = 0;
                return 0;
            }

            timer -= deltaSeconds;
            if (timer > 0f) return 0;

            timer += interval;
            interval = Math.Max(MinInterval, interval * Acceleration);
            return direction;
        }
    }

    /// <summary>Fires once when a button has been held for the given time.</summary>
    public sealed class HoldTimer
    {
        private readonly float seconds;
        private float held;
        private bool fired;

        public HoldTimer(float seconds)
        {
            this.seconds = seconds;
        }

        public float Progress => Math.Min(held / seconds, 1f);
        public bool IsHeld => held > 0f;

        public bool Update(bool down, float deltaSeconds)
        {
            if (!down)
            {
                held = 0f;
                fired = false;
                return false;
            }

            held += deltaSeconds;
            if (!fired && held >= seconds)
            {
                fired = true;
                return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 3: Create the Arcade assembly and `LauncherInput`**

`Assets/Scripts/Arcade/VRLauncher.Arcade.asmdef`:

```json
{
    "name": "VRLauncher.Arcade",
    "rootNamespace": "VRLauncher",
    "references": [
        "VRLauncher.Core",
        "Unity.TextMeshPro"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/Scripts/Arcade/LauncherInput.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace VRLauncher
{
    /// <summary>
    /// Reads the keyboard and both XR controllers and raises menu actions. Keys are read with
    /// GetAsyncKeyState because the launcher window is often not focused in VR. While
    /// <see cref="InputEnabled"/> is false nothing fires; when it is re-enabled, buttons that are
    /// already down (for example a VPX key still held on return) are ignored until released.
    /// </summary>
    public sealed class LauncherInput : MonoBehaviour
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_RETURN = 0x0D;
        private const int VK_SPACE = 0x20;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_F = 0x46;
        private const int VK_V = 0x56;

        public const float QuitHoldSeconds = 2f;

        public event Action Previous;
        public event Action Next;
        public event Action Launch;
        public event Action ToggleFavorite;
        public event Action CycleView;
        public event Action Quit;

        public bool InputEnabled { get; set; } = true;
        public float QuitHoldProgress => quitHold.Progress;
        public bool QuitHeld => quitHold.IsHeld;

        private readonly Dictionary<int, ButtonEdge> keys = new Dictionary<int, ButtonEdge>();
        private readonly TriggerLatch leftTrigger = new TriggerLatch();
        private readonly TriggerLatch rightTrigger = new TriggerLatch();
        private readonly ButtonEdge aButton = new ButtonEdge();
        private readonly ButtonEdge bButton = new ButtonEdge();
        private readonly ButtonEdge xButton = new ButtonEdge();
        private readonly StickRepeat stick = new StickRepeat();
        private readonly HoldTimer quitHold = new HoldTimer(QuitHoldSeconds);
        private XRInputDevice left;
        private XRInputDevice right;
        private bool wasEnabled;

        private void Update()
        {
            if (!InputEnabled)
            {
                wasEnabled = false;
                quitHold.Update(false, 0f);
                return;
            }

            // First frame after re-enabling: record what is already held without acting on it.
            Poll(raise: wasEnabled);
            wasEnabled = true;
        }

        private void Poll(bool raise)
        {
            EnsureControllers();
            float dt = Time.unscaledDeltaTime;
            int nav = 0;

            // Non-short-circuit '|' so every detector sees every frame.
            if (KeyEdge(VK_LSHIFT) | KeyEdge(VK_LEFT)) nav -= 1;
            if (KeyEdge(VK_RSHIFT) | KeyEdge(VK_RIGHT)) nav += 1;
            bool launch = KeyEdge(VK_RETURN) | KeyEdge(VK_SPACE);
            bool favorite = KeyEdge(VK_F);
            bool view = KeyEdge(VK_V);
            bool quit = KeyEdge(VK_ESCAPE);

            float stickX = 0f;
            bool yHeld = false;

            if (left.isValid)
            {
                if (left.TryGetFeatureValue(XRCommonUsages.trigger, out float lt) && leftTrigger.Update(lt)) nav -= 1;
                if (left.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool x) && xButton.Update(x)) view = true;
                yHeld = left.TryGetFeatureValue(XRCommonUsages.secondaryButton, out bool y) && y;
                if (left.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 ls)) stickX = ls.x;
            }

            if (right.isValid)
            {
                if (right.TryGetFeatureValue(XRCommonUsages.trigger, out float rt) && rightTrigger.Update(rt)) nav += 1;
                if (right.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool a) && aButton.Update(a)) launch = true;
                if (right.TryGetFeatureValue(XRCommonUsages.secondaryButton, out bool b) && bButton.Update(b)) favorite = true;
                if (right.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 rs) && Mathf.Abs(rs.x) > Mathf.Abs(stickX)) stickX = rs.x;
            }

            nav += stick.Update(stickX, dt);
            if (quitHold.Update(yHeld, dt)) quit = true;

            if (!raise) return;

            if (nav < 0) Previous?.Invoke();
            else if (nav > 0) Next?.Invoke();
            if (favorite) ToggleFavorite?.Invoke();
            if (view) CycleView?.Invoke();
            if (launch) Launch?.Invoke();
            if (quit) Quit?.Invoke();
        }

        private bool KeyEdge(int vk)
        {
            if (!keys.TryGetValue(vk, out ButtonEdge edge))
            {
                edge = new ButtonEdge();
                keys[vk] = edge;
            }
            return edge.Update((GetAsyncKeyState(vk) & 0x8000) != 0);
        }

        /// <summary>(Re)acquires the controllers; they can connect late or drop out.</summary>
        private void EnsureControllers()
        {
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/InputLogic.cs Assets/Scripts/Arcade \
  Assets/Scripts/Arcade/VRLauncher.Arcade.asmdef Assets/Scripts/Arcade/LauncherInput.cs
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
git add Assets/Scripts
git commit -m "Add launcher input: triggers, sticks with repeat, favorites, views and hold to quit"
tools/remote.sh test
```

Expected: all tests pass and no compile errors (this also proves the Arcade assembly and its TextMeshPro reference resolve).

---

## Task 6: The media fetch script

**Files:**
- Create: `tools/fetch_media.py`, `tools/media-overrides.json`, `tools/tests/test_fetch_media.py`

**Interfaces:**
- Produces: `uv run tools/fetch_media.py [--config PATH] [--force "<Table Name>"]... [--dry-run] [--overrides PATH]`. Writes `<media root>/<stem>/{wheel.png,table.png,bg.png,table.mp4}`, where the media root is `tableMediaDirectory` from the config (default `Media\Tables`) resolved relative to the config file's folder. This is the same folder `LauncherPaths.Resolve` gives the launcher, because the installed config sits next to `vr-launch.exe`. Python functions `norm`, `match`, `media_root`, `list_tables`, `main(argv=None, get=http_get)`.

- [ ] **Step 1: Write the failing tests**

`tools/tests/test_fetch_media.py`:

```python
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


pytest.fixture
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
```

```bash
git add tools/tests && git commit -m "Add media fetch script tests"
tools/remote.sh pytest
```

Expected: FAIL, `FileNotFoundError` / `No such file` for `fetch_media.py`.

- [ ] **Step 2: Implement the script**

`tools/fetch_media.py` (`http_get`, `norm` and `match` are copied from the Ally's `fetch-media.py`, which already works against these services):

```python
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
```

`tools/media-overrides.json`: copy the Ally's list verbatim (from `~/Development/GitHub/vpx/ally-tools/ally-install/bin/media-overrides.json` on the Mac):

```json
{
  "_comment": "Table file name (without .vpx) -> Virtual Pinball Spreadsheet id, for names that don't match VPS automatically. Look ids up at https://virtualpinballspreadsheet.github.io",
  "Back To The Future - The Pinball (Data East 1990)": "43ma3WQK",
  "Batman The Dark Knight (Stern 2008)": "LX-K2F9k",
  "No Good Gofers - Rawds Edition (Williams 1997)": "AV8W-O2R",
  "Simpsons Pinball Party, The (Stern 2003)": "qfbl4Ee0",
  "Star Trek LE (Stern 2013)": "XYh-gw4S",
  "Terminator 2 (Williams 1991)": "BunvWvh9",
  "Tron Legacy (Stern 2011)": "akYNyhNO",
  "X-Men LE (Stern 2012)": "FTxECos3",
  "No Fear (Williams 1995)": "mtKGKnBh",
  "Red and Ted's Road Show (Williams 1994)": "LMK5EL-k",
  "Spider-Man Classic (Stern 2007)": "Uzvrn_qq"
}
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add tools
git commit -m "Add a media fetch script for wheel, playfield, backglass and video"
tools/remote.sh pytest
```

Expected: `11 passed`.

- [ ] **Step 4: Fetch the real media on the PC**

This only adds files under the installed launcher's `Media\Tables\`. It does not touch tables, ROMs or the running app.

```bash
tools/remote.sh media --dry-run | tail -20
tools/remote.sh media | tail -25
```

Expected: `media folder: C:\Users\dayel\AppData\Local\Programs\VR Pinball Launcher\Media\Tables`, about 160 files for about 41 matched tables, `Pinball Training Lab` under "no VPS match". Record the summary lines (fetched count, missing list, unmatched list) in the task report. Running it a second time should print `fetched 0 file(s)`.

---

## Task 7: Texture cache and file URIs

**Files:**
- Create: `Assets/Scripts/Core/LruCache.cs`, `Assets/Scripts/Core/FileUri.cs`, `Assets/Scripts/Arcade/MediaCache.cs`
- Modify: `Assets/Tests/EditMode/VRLauncher.Tests.EditMode.asmdef` (add the Arcade reference)
- Test: `Assets/Tests/EditMode/LruCacheTests.cs`, `Assets/Tests/EditMode/FileUriTests.cs`, `Assets/Tests/EditMode/MediaCacheTests.cs`

**Interfaces:**
- Produces (Core): `sealed class LruCache<TKey, TValue> { LruCache(int capacity, Action<TKey, TValue> onEvict = null, IEqualityComparer<TKey> comparer = null); int Count; bool TryGet(TKey, out TValue); void Add(TKey, TValue); void Clear(); }`; `static class FileUri { string FromPath(string fullPath); }`.
- Produces (Arcade): `sealed class MediaCache : MonoBehaviour { const int Capacity = 45; void Request(string path, Action<Texture2D> onLoaded); }`. The callback gets null when the file is missing or unreadable. In edit mode (tests, preview renders) images load synchronously.

- [ ] **Step 1: Write the failing tests**

Add `"VRLauncher.Arcade"` to the `references` array of `Assets/Tests/EditMode/VRLauncher.Tests.EditMode.asmdef` (after `"VRLauncher.Core"`).

`Assets/Tests/EditMode/LruCacheTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class LruCacheTests
    {
        [Test]
        public void Add_BeyondCapacity_EvictsLeastRecentlyUsed()
        {
            var evicted = new List<string>();
            var cache = new LruCache<string, int>(2, (k, _) => evicted.Add(k));
            cache.Add("a", 1);
            cache.Add("b", 2);
            cache.TryGet("a", out _);   // "a" is now more recent than "b"
            cache.Add("c", 3);

            CollectionAssert.AreEqual(new[] { "b" }, evicted);
            Assert.IsTrue(cache.TryGet("a", out int a));
            Assert.AreEqual(1, a);
            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void Add_ExistingKey_ReplacesAndEvictsTheOldValue()
        {
            var evicted = new List<int>();
            var cache = new LruCache<string, int>(2, (_, v) => evicted.Add(v));
            cache.Add("a", 1);
            cache.Add("a", 2);
            CollectionAssert.AreEqual(new[] { 1 }, evicted);
            Assert.AreEqual(1, cache.Count);
        }

        [Test]
        public void Clear_EvictsEverything()
        {
            int evictions = 0;
            var cache = new LruCache<string, int>(3, (_, __) => evictions++);
            cache.Add("a", 1);
            cache.Add("b", 2);
            cache.Clear();
            Assert.AreEqual(2, evictions);
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void Comparer_IsHonoured()
        {
            var cache = new LruCache<string, int>(2, null, StringComparer.OrdinalIgnoreCase);
            cache.Add("A.png", 1);
            Assert.IsTrue(cache.TryGet("a.PNG", out _));
        }

        [Test]
        public void Capacity_MustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        }
    }
}
```

`Assets/Tests/EditMode/FileUriTests.cs`:

```csharp
using System;
using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class FileUriTests
    {
        [TestCase("C:\Media\Tables\Attack from Mars (Bally 1995)\wheel.png")]
        [TestCase("C:\Media\Tables\Bram Stoker's Dracula (Williams 1993)\bg.png")]
        [TestCase("C:\Media\Tables\Simpsons Pinball Party, The (Stern 2003)\table.png")]
        [TestCase("C:\Media\Tables\Rock & Roll (Bally 1990)\table.png")]
        [TestCase("C:\Media\Tables\100% Pinball (Foo 1990)\table.png")]
        [TestCase("C:\Media\Tables\Pin #1 (Foo 1990)\wheel.png")]
        public void FromPath_RoundTripsThroughUri(string path)
        {
            string uri = FileUri.FromPath(path);
            StringAssert.StartsWith("file:///C:/Media/Tables/", uri);
            StringAssert.DoesNotContain(" ", uri);
            StringAssert.DoesNotContain("#", uri);
            Assert.AreEqual(path, new Uri(uri).LocalPath);
        }
    }
}
```

`Assets/Tests/EditMode/MediaCacheTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class MediaCacheTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            host = new GameObject("MediaCacheTest");
            cache = host.AddComponent<MediaCache>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            Directory.Delete(dir, true);
        }

        private string WritePng(string name, int width, int height)
        {
            var tex = new Texture2D(width, height);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }

        [Test]
        public void Request_LoadsTheImageAndCachesIt()
        {
            string path = WritePng("wheel.png", 8, 4);
            Texture2D first = null, second = null;

            cache.Request(path, t => first = t);
            cache.Request(path, t => second = t);

            Assert.IsNotNull(first);
            Assert.AreEqual(8, first.width);
            Assert.AreSame(first, second);
        }

        [Test]
        public void Request_MissingFile_GivesNull()
        {
            bool called = false;
            Texture2D result = new Texture2D(1, 1);
            cache.Request(Path.Combine(dir, "nope.png"), t => { called = true; result = t; });
            Assert.IsTrue(called);
            Assert.IsNull(result);
        }

        [Test]
        public void Request_NullPath_GivesNull()
        {
            bool called = false;
            cache.Request(null, t => { called = true; Assert.IsNull(t); });
            Assert.IsTrue(called);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/LruCacheTests.cs Assets/Tests/EditMode/FileUriTests.cs Assets/Tests/EditMode/MediaCacheTests.cs
git add Assets/Tests && git commit -m "Add cache and file URI tests"
tools/remote.sh test
```

Expected: compilation fails (`LruCache`, `FileUri`, `MediaCache` not found).

- [ ] **Step 2: Implement**

`Assets/Scripts/Core/LruCache.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace VRLauncher
{
    /// <summary>
    /// A fixed-size cache that drops the least recently used entry, calling
    /// <c>onEvict</c> for every value it drops (so textures can be destroyed).
    /// </summary>
    public sealed class LruCache<TKey, TValue>
    {
        private readonly int capacity;
        private readonly Action<TKey, TValue> onEvict;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> order = new LinkedList<KeyValuePair<TKey, TValue>>();

        public LruCache(int capacity, Action<TKey, TValue> onEvict = null, IEqualityComparer<TKey> comparer = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            this.onEvict = onEvict;
            map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => map.Count;

        public bool TryGet(TKey key, out TValue value)
        {
            if (map.TryGetValue(key, out var node))
            {
                order.Remove(node);
                order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            if (map.TryGetValue(key, out var existing))
            {
                order.Remove(existing);
                map.Remove(key);
                onEvict?.Invoke(existing.Value.Key, existing.Value.Value);
            }

            map[key] = order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));

            while (map.Count > capacity)
            {
                var last = order.Last;
                order.RemoveLast();
                map.Remove(last.Value.Key);
                onEvict?.Invoke(last.Value.Key, last.Value.Value);
            }
        }

        public void Clear()
        {
            foreach (var pair in order)
            {
                onEvict?.Invoke(pair.Key, pair.Value);
            }
            order.Clear();
            map.Clear();
        }
    }
}
```

`Assets/Scripts/Core/FileUri.cs`:

```csharp
using System;
using System.Linq;

namespace VRLauncher
{
    /// <summary>
    /// Builds a file:// URI from an absolute path, escaping each segment so characters such as
    /// '#', '%', spaces and apostrophes in table names cannot change the URI's meaning.
    /// (new Uri(path) would treat '#' as the start of a fragment.)
    /// </summary>
    public static class FileUri
    {
        public static string FromPath(string fullPath)
        {
            string forward = fullPath.Replace('\\', '/');
            bool posix = forward.StartsWith("/", StringComparison.Ordinal);
            var segments = forward.Split('/')
                .Select((segment, i) => !posix && i == 0 ? segment : Uri.EscapeDataString(segment));
            return (posix ? "file://" : "file:///") + string.Join("/", segments);
        }
    }
}
```

`Assets/Scripts/Arcade/MediaCache.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace VRLauncher
{
    /// <summary>
    /// Loads table art off the main thread and keeps the most recently used textures
    /// (15 tables x 3 images), destroying the rest. Several requests for the same file
    /// share one load. In edit mode (tests, preview renders) loads are synchronous.
    /// </summary>
    public sealed class MediaCache : MonoBehaviour
    {
        public const int Capacity = 45;

        private LruCache<string, Texture2D> cache;
        private readonly Dictionary<string, List<Action<Texture2D>>> pending =
            new Dictionary<string, List<Action<Texture2D>>>(StringComparer.OrdinalIgnoreCase);

        // Created lazily: Awake does not run for components added in edit mode.
        private LruCache<string, Texture2D> Cache =>
            cache ?? (cache = new LruCache<string, Texture2D>(Capacity, (_, tex) => DestroyTexture(tex), StringComparer.OrdinalIgnoreCase));

        public void Request(string path, Action<Texture2D> onLoaded)
        {
            if (string.IsNullOrEmpty(path))
            {
                onLoaded(null);
                return;
            }

            if (Cache.TryGet(path, out Texture2D cached))
            {
                onLoaded(cached);
                return;
            }

            if (!Application.isPlaying)
            {
                Texture2D loaded = LoadImmediate(path);
                if (loaded != null) Cache.Add(path, loaded);
                onLoaded(loaded);
                return;
            }

            if (pending.TryGetValue(path, out var waiting))
            {
                waiting.Add(onLoaded);
                return;
            }

            pending[path] = new List<Action<Texture2D>> { onLoaded };
            StartCoroutine(Load(path));
        }

        private IEnumerator Load(string path)
        {
            Texture2D texture = null;
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(FileUri.FromPath(path), true))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    texture = DownloadHandlerTexture.GetContent(request);
                    texture.name = Path.GetFileName(path);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    Cache.Add(path, texture);
                }
                else
                {
                    Debug.LogWarning($"MediaCache: could not load {path}: {request.error}");
                }
            }

            List<Action<Texture2D>> callbacks = pending[path];
            pending.Remove(path);
            foreach (Action<Texture2D> callback in callbacks)
            {
                callback(texture);
            }
        }

        private static Texture2D LoadImmediate(string path)
        {
            if (!File.Exists(path)) return null;

            var texture = new Texture2D(2, 2) { name = Path.GetFileName(path), wrapMode = TextureWrapMode.Clamp };
            if (texture.LoadImage(File.ReadAllBytes(path)))
            {
                return texture;
            }

            DestroyTexture(texture);
            return null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        private void OnDestroy()
        {
            cache?.Clear();
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/LruCache.cs Assets/Scripts/Core/FileUri.cs Assets/Scripts/Arcade/MediaCache.cs
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add Assets
git commit -m "Add a texture cache that loads table art off the main thread"
tools/remote.sh test
```

Expected: all tests pass.

---

## Task 8: The cabinet

**Files:**
- Create: `Assets/Scripts/Arcade/CabinetView.cs`, `Assets/Editor/ArcadePreview.cs`
- Test: `Assets/Tests/EditMode/CabinetViewTests.cs`

**Interfaces:**
- Consumes: `MediaCache.Request` (Task 7), `TableEntry`/`MediaSet` (Task 2).
- Produces: `sealed class CabinetView : MonoBehaviour` with
  - `static readonly Vector3 PlayfieldCenter` (0, 1.31, 0.56), `static readonly Quaternion PlayfieldRotation`, `const float DimTint = 0.55f`, `static Texture2D DarkTexture`;
  - `static CabinetView Create(Transform parent, MediaCache cache)`;
  - `TableEntry Entry`; `void SetEntry(TableEntry entry)` (null clears the art); `void SetPlaceholder(string message)`; `void SetFocused(bool focused)`; `void ShowVideo(Texture texture)`; `void ShowStill()`; `void Pulse()`;
  - read-only test hooks `Texture PlayfieldTexture`, `Texture BackglassTexture`, `bool WheelDecalVisible`, `bool ApronWheelVisible`, `string MarqueeText`, `string PlaceholderText` (the last two are null when hidden).
- Produces: editor method `VRLauncher.EditorTools.ArcadePreview.RenderCabinets` (writes `Logs/preview-cabinets.png`) and helper `ArcadePreview.LoadInstalledCatalog()`.

**Geometry.** Local frame: origin on the floor at the front centre, +Z away from the player, +Y up. Unity's Quad faces -Z (seen from the player). The playfield quad is scaled (1.25, 0.70) with the landscape image's long side along its local X, then rotated `Euler(55, 0, -90)`: Z -90 puts the image's right edge (flippers) at the bottom with the image's top to the player's right, and X +55 lays it back so it rises away from the player at 35 degrees from horizontal. Its flipper edge lands at about (0, 0.95, 0.05) and its top edge at about (0, 1.67, 1.07). These numbers are starting values; Step 5's render is how you check them.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/CabinetViewTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class CabinetViewTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;
        private CabinetView cabinet;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-cab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            host = new GameObject("CabinetTest");
            cache = host.AddComponent<MediaCache>();
            cabinet = CabinetView.Create(host.transform, cache);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            Directory.Delete(dir, true);
        }

        private string Png(string name)
        {
            var tex = new Texture2D(4, 4);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }

        private static TableEntry Entry(MediaSet media) =>
            new TableEntry("Congo (Williams 1995).vpx", "C:\t\Congo (Williams 1995).vpx", "Congo (Williams 1995)", "Congo", "Williams", 1995, media);

        [Test]
        public void FullMedia_ShowsPlayfieldBackglassAndApronWheel()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Png("w.png"), Playfield = Png("p.png"), Backglass = Png("b.png") }));

            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.PlayfieldTexture);
            Assert.AreNotSame(CabinetView.DarkTexture, cabinet.BackglassTexture);
            Assert.IsTrue(cabinet.ApronWheelVisible);
            Assert.IsFalse(cabinet.WheelDecalVisible);
            Assert.IsNull(cabinet.MarqueeText);
        }

        [Test]
        public void NoPlayfield_ShowsDarkPlayfieldWithWheelDecal()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Png("w.png"), Backglass = Png("b.png") }));
            Assert.AreSame(CabinetView.DarkTexture, cabinet.PlayfieldTexture);
            Assert.IsTrue(cabinet.WheelDecalVisible);
        }

        [Test]
        public void NoBackglass_ShowsTheWheelOnTheBackbox()
        {
            string wheel = Png("w.png");
            cabinet.SetEntry(Entry(new MediaSet { Wheel = wheel, Playfield = Png("p.png") }));
            Texture2D wheelTexture = null;
            cache.Request(wheel, t => wheelTexture = t);
            Assert.AreSame(wheelTexture, cabinet.BackglassTexture);
        }

        [Test]
        public void NoWheel_ShowsTheTitleOnTheMarquee()
        {
            cabinet.SetEntry(Entry(new MediaSet { Playfield = Png("p.png") }));
            Assert.AreEqual("Congo", cabinet.MarqueeText);
            Assert.IsFalse(cabinet.ApronWheelVisible);
        }

        [Test]
        public void WheelFileMissingOnDisk_FallsBackToTitle()
        {
            cabinet.SetEntry(Entry(new MediaSet { Wheel = Path.Combine(dir, "gone.png") }));
            Assert.AreEqual("Congo", cabinet.MarqueeText);
        }

        [Test]
        public void Video_ReplacesAndRestoresTheStill()
        {
            cabinet.SetEntry(Entry(new MediaSet { Playfield = Png("p.png") }));
            Texture still = cabinet.PlayfieldTexture;
            var videoTexture = new RenderTexture(4, 4, 0);

            cabinet.ShowVideo(videoTexture);
            Assert.AreSame(videoTexture, cabinet.PlayfieldTexture);
            cabinet.ShowStill();
            Assert.AreSame(still, cabinet.PlayfieldTexture);

            UnityEngine.Object.DestroyImmediate(videoTexture);
        }

        [Test]
        public void Placeholder_ShowsTheMessageAndClearsTheEntry()
        {
            cabinet.SetEntry(Entry(new MediaSet()));
            cabinet.SetPlaceholder("No favorites yet");
            Assert.IsNull(cabinet.Entry);
            Assert.AreEqual("No favorites yet", cabinet.PlaceholderText);

            cabinet.SetEntry(Entry(new MediaSet()));
            Assert.IsNull(cabinet.PlaceholderText);
        }

        [Test]
        public void PlayfieldFaceLooksUpAndTowardThePlayer()
        {
            Vector3 normal = CabinetView.PlayfieldRotation * Vector3.back;
            Assert.Greater(normal.y, 0.5f);
            Assert.Less(normal.z, -0.3f);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/CabinetViewTests.cs
git add Assets/Tests && git commit -m "Add cabinet tests"
tools/remote.sh test
```

Expected: compilation fails (`CabinetView` not found).

- [ ] **Step 2: Implement `CabinetView`**

`Assets/Scripts/Arcade/CabinetView.cs`:

```csharp
using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// One arcade cabinet built from primitives: legs, body, tilted playfield, backbox with
    /// backglass, and the wheel on the front apron. Local frame: origin on the floor at the
    /// front centre, +Z away from the player, +Y up. Art is unlit (screens), the body is lit.
    /// Missing media falls back so every cabinet still looks finished: no playfield shows a
    /// dark playfield with the wheel as a decal, no backglass shows the wheel on the backbox,
    /// and no wheel shows the title on the apron.
    /// </summary>
    public sealed class CabinetView : MonoBehaviour
    {
        public static readonly Vector3 PlayfieldCenter = new Vector3(0f, 1.31f, 0.56f);
        public static readonly Quaternion PlayfieldRotation = Quaternion.Euler(55f, 0f, -90f);
        private static readonly Vector3 PlayfieldSize = new Vector3(1.25f, 0.70f, 1f);
        private static readonly Vector3 BackglassCenter = new Vector3(0f, 1.95f, 1.075f);
        private static readonly Vector3 BackglassSize = new Vector3(0.68f, 0.3825f, 1f);
        private static readonly Vector3 BackglassWheelSize = new Vector3(0.38f, 0.38f, 1f);
        private static readonly Vector3 ApronCenter = new Vector3(0f, 0.66f, -0.005f);
        private const float ApronWheelSize = 0.30f;
        private const float DecalSize = 0.40f;
        private const float PulseSeconds = 0.4f;
        public const float DimTint = 0.55f;

        private static Material bodyMaterial;
        private static Texture2D darkTexture;

        private MediaCache cache;
        private Material playfieldMaterial;
        private Material backglassMaterial;
        private Material wheelMaterial;
        private Material decalMaterial;
        private Transform backglass;
        private GameObject apronWheel;
        private GameObject decal;
        private TextMeshPro marquee;
        private TextMeshPro placeholder;
        private Texture stillPlayfield;
        private bool showingVideo;
        private bool focused = true;
        private int version;
        private Coroutine pulse;

        public TableEntry Entry { get; private set; }

        public Texture PlayfieldTexture => playfieldMaterial.mainTexture;
        public Texture BackglassTexture => backglassMaterial.mainTexture;
        public bool WheelDecalVisible => decal.activeSelf;
        public bool ApronWheelVisible => apronWheel.activeSelf;
        public string MarqueeText => marquee.gameObject.activeSelf ? marquee.text : null;
        public string PlaceholderText => placeholder.gameObject.activeSelf ? placeholder.text : null;

        /// <summary>A 1x1 near-black texture for blank screens (Sprites/Default renders white without one).</summary>
        public static Texture2D DarkTexture
        {
            get
            {
                if (darkTexture == null)
                {
                    darkTexture = new Texture2D(1, 1) { name = "CabinetDark" };
                    darkTexture.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.07f));
                    darkTexture.Apply();
                }
                return darkTexture;
            }
        }

        private static Material BodyMaterial
        {
            get
            {
                if (bodyMaterial == null)
                {
                    bodyMaterial = new Material(Shader.Find("Standard")) { name = "CabinetBody", color = new Color(0.09f, 0.09f, 0.11f) };
                    bodyMaterial.SetFloat("_Metallic", 0.4f);
                    bodyMaterial.SetFloat("_Glossiness", 0.55f);
                }
                return bodyMaterial;
            }
        }

        public static CabinetView Create(Transform parent, MediaCache cache)
        {
            var go = new GameObject("Cabinet");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CabinetView>();
            view.cache = cache;
            view.Build();
            view.SetEntry(null);
            return view;
        }

        private void Build()
        {
            Material body = BodyMaterial;
            foreach (float x in new[] { -0.32f, 0.32f })
            {
                foreach (float z in new[] { 0.06f, 1.04f })
                {
                    Box("Leg", new Vector3(x, 0.18f, z), new Vector3(0.06f, 0.36f, 0.06f), body);
                }
            }
            Box("Body", new Vector3(0f, 0.66f, 0.55f), new Vector3(0.72f, 0.60f, 1.10f), body);
            Box("HeadSupport", new Vector3(0f, 1.295f, 1.0f), new Vector3(0.72f, 0.67f, 0.20f), body);
            Box("LockdownBar", new Vector3(0f, 0.97f, 0.02f), new Vector3(0.72f, 0.05f, 0.08f), body);
            Box("Backbox", new Vector3(0f, 1.95f, 1.20f), new Vector3(0.74f, 0.64f, 0.24f), body);

            playfieldMaterial = ArtMaterial("Playfield");
            Quad("Playfield", PlayfieldCenter, PlayfieldRotation, PlayfieldSize, playfieldMaterial);

            decalMaterial = ArtMaterial("WheelDecal");
            decalMaterial.renderQueue += 1;   // always drawn over the playfield it sits on
            Vector3 normal = PlayfieldRotation * Vector3.back;
            decal = Quad("WheelDecal", PlayfieldCenter + normal * 0.005f, Quaternion.Euler(55f, 0f, 0f),
                         new Vector3(DecalSize, DecalSize, 1f), decalMaterial).gameObject;

            backglassMaterial = ArtMaterial("Backglass");
            backglass = Quad("Backglass", BackglassCenter, Quaternion.identity, BackglassSize, backglassMaterial);

            wheelMaterial = ArtMaterial("ApronWheel");
            apronWheel = Quad("ApronWheel", ApronCenter, Quaternion.identity,
                              new Vector3(ApronWheelSize, ApronWheelSize, 1f), wheelMaterial).gameObject;

            marquee = Label("Marquee", ApronCenter + new Vector3(0f, 0f, -0.005f), new Vector2(0.66f, 0.28f));
            placeholder = Label("Placeholder", BackglassCenter + new Vector3(0f, 0f, -0.01f), new Vector2(0.66f, 0.36f));
        }

        /// <summary>Shows a table's art, or clears the cabinet when <paramref name="entry"/> is null.</summary>
        public void SetEntry(TableEntry entry)
        {
            version++;
            Entry = entry;
            showingVideo = false;
            stillPlayfield = DarkTexture;
            playfieldMaterial.mainTexture = DarkTexture;
            backglassMaterial.mainTexture = DarkTexture;
            backglass.localScale = BackglassSize;
            decal.SetActive(false);
            apronWheel.SetActive(false);
            marquee.gameObject.SetActive(false);
            placeholder.gameObject.SetActive(false);

            if (entry == null) return;

            MediaSet media = entry.Media;
            int requested = version;

            if (media.Wheel == null)
            {
                ShowTitle(entry.Title);
            }
            else
            {
                Load(media.Wheel, requested, wheel =>
                {
                    if (wheel == null)
                    {
                        ShowTitle(entry.Title);
                        return;
                    }
                    wheelMaterial.mainTexture = wheel;
                    apronWheel.SetActive(true);
                    if (media.Playfield == null)
                    {
                        decalMaterial.mainTexture = wheel;
                        decal.SetActive(true);
                    }
                    if (media.Backglass == null)
                    {
                        backglassMaterial.mainTexture = wheel;
                        backglass.localScale = BackglassWheelSize;
                    }
                });
            }

            if (media.Playfield != null)
            {
                Load(media.Playfield, requested, still =>
                {
                    if (still == null) return;
                    stillPlayfield = still;
                    if (!showingVideo) playfieldMaterial.mainTexture = still;
                });
            }

            if (media.Backglass != null)
            {
                Load(media.Backglass, requested, art =>
                {
                    if (art != null) backglassMaterial.mainTexture = art;
                });
            }
        }

        /// <summary>An empty cabinet with a message on the backbox, e.g. "No favorites yet".</summary>
        public void SetPlaceholder(string message)
        {
            SetEntry(null);
            placeholder.text = message;
            placeholder.gameObject.SetActive(true);
        }

        public void SetFocused(bool isFocused)
        {
            focused = isFocused;
            ApplyTint(focused ? 1f : DimTint);
        }

        public void ShowVideo(Texture texture)
        {
            showingVideo = true;
            playfieldMaterial.mainTexture = texture;
        }

        public void ShowStill()
        {
            showingVideo = false;
            playfieldMaterial.mainTexture = stillPlayfield;
        }

        /// <summary>A brief brightening, used when the table is launched.</summary>
        public void Pulse()
        {
            if (!Application.isPlaying) return;
            if (pulse != null) StopCoroutine(pulse);
            pulse = StartCoroutine(PulseRoutine());
        }

        private IEnumerator PulseRoutine()
        {
            for (float t = 0f; t < PulseSeconds; t += Time.unscaledDeltaTime)
            {
                ApplyTint(1f + 0.6f * Mathf.Sin(Mathf.PI * t / PulseSeconds));
                yield return null;
            }
            ApplyTint(focused ? 1f : DimTint);
            pulse = null;
        }

        private void ShowTitle(string title)
        {
            marquee.text = title;
            marquee.gameObject.SetActive(true);
        }

        private void Load(string path, int requested, Action<Texture2D> apply)
        {
            cache.Request(path, texture =>
            {
                // Ignore loads that finish after the cabinet moved on to another table.
                if (this != null && requested == version) apply(texture);
            });
        }

        private void ApplyTint(float brightness)
        {
            var tint = new Color(brightness, brightness, brightness, 1f);
            playfieldMaterial.color = tint;
            backglassMaterial.color = tint;
            wheelMaterial.color = tint;
            decalMaterial.color = tint;
        }

        private static Material ArtMaterial(string name) =>
            new Material(Shader.Find("Sprites/Default")) { name = name, mainTexture = DarkTexture };

        private Transform Box(string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            return Place(go, name, position, Quaternion.identity, size, material);
        }

        private Transform Quad(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            return Place(go, name, position, rotation, size, material);
        }

        private Transform Place(GameObject go, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            go.name = name;
            RemoveCollider(go);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private TextMeshPro Label(string name, Vector3 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.5f;
            text.fontSizeMax = 6f;
            text.color = Color.white;
            go.SetActive(false);
            return text;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        private void OnDestroy()
        {
            foreach (Material material in new[] { playfieldMaterial, backglassMaterial, wheelMaterial, decalMaterial })
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Arcade/CabinetView.cs
```

- [ ] **Step 3: Run the tests to see them pass**

```bash
git add Assets/Scripts/Arcade
git commit -m "Add the arcade cabinet with media fallbacks"
tools/remote.sh test
```

Expected: all tests pass.

- [ ] **Step 4: Write the preview renderer**

`Assets/Editor/ArcadePreview.cs` renders headless screenshots so the look can be checked without a headset. It reads the *installed* launcher's config (the one with real tables and media), not the repo's sample config.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// Batchmode preview renders of the arcade room, written to Logs/, so geometry and
    /// fallbacks can be checked without a headset. Run through tools/unity-batch.ps1.
    /// </summary>
    public static class ArcadePreview
    {
        private static string InstalledDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VR Pinball Launcher");

        /// <summary>The installed launcher's tables with media resolved exactly as the launcher does.</summary>
        public static List<TableEntry> LoadInstalledCatalog()
        {
            string configPath = Path.Combine(InstalledDirectory, "launcher-config.json");
            var config = JsonUtility.FromJson<LauncherConfig>(File.ReadAllText(configPath));
            string Resolve(string p) => string.IsNullOrEmpty(p) ? null : Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(InstalledDirectory, p));
            var settings = new CatalogSettings
            {
                TablesDirectory = config.tablesDirectory,
                SearchSubdirectories = config.searchSubdirectories,
                TableMediaDirectory = Resolve(config.tableMediaDirectory),
                WheelDirectory = Resolve(config.wheelDirectory)
            };
            return TableCatalog.Scan(settings, new DiskFileSystem());
        }

        /// <summary>Three cabinets side by side: full media, no playfield image, no media at all.</summary>
        public static void RenderCabinets()
        {
            Run("preview-cabinets.png", () =>
            {
                List<TableEntry> tables = LoadInstalledCatalog();
                TableEntry full = tables.First(t => t.Media.Playfield != null && t.Media.Backglass != null && t.Media.Wheel != null);
                TableEntry noPlayfield = new TableEntry(full.RelativePath, full.FullPath, full.Stem, full.Title, full.Manufacturer, full.Year,
                    new MediaSet { Wheel = full.Media.Wheel, Backglass = full.Media.Backglass });
                TableEntry bare = new TableEntry("Pinball Training Lab.vpx", "", "Pinball Training Lab", "Pinball Training Lab", null, 0, new MediaSet());

                var host = new GameObject("Preview");
                MediaCache cache = host.AddComponent<MediaCache>();
                TableEntry[] entries = { noPlayfield, full, bare };
                for (int i = 0; i < entries.Length; i++)
                {
                    CabinetView cabinet = CabinetView.Create(host.transform, cache);
                    cabinet.transform.localPosition = new Vector3((i - 1) * 1.0f, 0f, 0f);
                    cabinet.SetEntry(entries[i]);
                    cabinet.SetFocused(i == 1);
                }

                AddLights();
                return MakeCamera(new Vector3(0f, 1.66f, -2.2f), Quaternion.Euler(12f, 0f, 0f), 70f);
            });
        }

        /// <summary>Opens an empty scene, lets <paramref name="build"/> populate it and return a camera, renders, saves Logs/<paramref name="fileName"/>.</summary>
        public static void Run(string fileName, Func<Camera> build)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Camera camera = build();
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", fileName));
                Render(camera, path);
                Debug.Log($"[unity-batch] OK wrote {path}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[unity-batch] FAILED {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void AddLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.28f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.8f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        public static Camera MakeCamera(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            return camera;
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1600, height = 900;
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Editor/ArcadePreview.cs
git add Assets/Editor && git commit -m "Add headless preview renders of the arcade cabinet"
```

- [ ] **Step 5: Render and inspect the cabinets**

```bash
tools/remote.sh batch VRLauncher.EditorTools.ArcadePreview.RenderCabinets
tools/remote.sh pull Logs/preview-cabinets.png /tmp/preview-cabinets.png
```

Open `/tmp/preview-cabinets.png` with the Read tool and check, in order:
1. Centre cabinet: the playfield image lies on the slope with its **flippers at the bottom, nearest the viewer**, and it is not mirrored (text on the apron cards reads left to right). The backglass is upright and not mirrored. The wheel is on the front apron.
2. Left cabinet: dark playfield with the wheel upright on it.
3. Right cabinet: dark playfield and backglass, "Pinball Training Lab" readable on the apron.
4. The side cabinets are dimmer than the centre one.

If the playfield is rotated or mirrored wrongly, fix `PlayfieldRotation` (for example `Euler(55, 0, 90)` flips it end to end; a negative X scale mirrors it) and re-render. If the text is far too small or large, adjust `fontSizeMin`/`fontSizeMax` in `Label`. Include the final PNG path and a one-line description of each check in the task report. Commit any fix:

```bash
git commit -am "Fix cabinet art orientation from the preview render"
```

---

## Task 9: The arc room

**Files:**
- Create: `Assets/Scripts/Core/ArcLayout.cs`, `Assets/Scripts/Arcade/ArcRoom.cs`
- Modify: `Assets/Editor/ArcadePreview.cs` (add `RenderArc`)
- Test: `Assets/Tests/EditMode/ArcLayoutTests.cs`, `Assets/Tests/EditMode/ArcRoomTests.cs`

**Interfaces:**
- Consumes: `TableListView` (Task 4), `LauncherState`, `RelativeTime` (Task 3), `CabinetView` (Task 8), `MediaCache` (Task 7).
- Produces (Core): `static class ArcLayout { const int MaxOffset = 3; const float Radius = 2.2f, SpacingDegrees = 22f; Vector3 SlotPosition(float offset); Quaternion SlotRotation(float offset); IReadOnlyList<int> VisibleOffsets(int itemCount); }`.
- Produces (Arcade): `sealed class ArcRoom : MonoBehaviour { const float HeadAbovePlayfield = 0.35f; const float MaxScroll = 2f; static ArcRoom Create(TableListView view, LauncherState state, MediaCache cache); CabinetView CabinetAt(int offset); float ScrollOffset; string InfoTitle, InfoDetail, InfoStatus; void Next(); void Previous(); void CycleView(); void Refresh(); void Recenter(Vector3 headPosition, float yawDegrees); void PulseCenter(); void ShowNotice(string); void ClearNotice(); void Suspend(); void Resume(); }`.

**Behaviour.** Cabinet `k` (offset -3..3) always shows `view.EntryAt(k)`; the pool never re-parents. Navigation shifts the entries and sets `scroll` to ±1 (clamped to ±2), so every cabinet starts one slot over and glides back with `SmoothDamp` (about 0.25 s). Only offsets in `VisibleOffsets(count)` are shown, so 2 favorites show 2 cabinets, not 7 repeats. `Refresh()` calls `view.Refresh()` then reassigns without animation. The centred cabinet's video starts 0.3 s after the selection settles, only in play mode.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/ArcLayoutTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ArcLayoutTests
    {
        [Test]
        public void CentreSlotIsStraightAhead()
        {
            Vector3 centre = ArcLayout.SlotPosition(0f);
            Assert.AreEqual(0f, centre.x, 1e-5f);
            Assert.AreEqual(ArcLayout.Radius, centre.z, 1e-5f);
        }

        [Test]
        public void PositiveOffsetsAreToTheRightAndOnTheArc()
        {
            for (float k = -3f; k <= 3f; k += 0.5f)
            {
                Assert.AreEqual(ArcLayout.Radius, ArcLayout.SlotPosition(k).magnitude, 1e-4f);
            }
            Assert.Greater(ArcLayout.SlotPosition(1f).x, 0f);
        }

        [Test]
        public void CabinetsFaceTheCentre()
        {
            // A cabinet's +Z points away from the player, i.e. along the radius.
            Vector3 outward = ArcLayout.SlotRotation(2f) * Vector3.forward;
            Vector3 radial = ArcLayout.SlotPosition(2f).normalized;
            Assert.Greater(Vector3.Dot(outward, radial), 0.9999f);
        }

        [TestCase(0, new int[0])]
        [TestCase(1, new[] { 0 })]
        [TestCase(2, new[] { 0, 1 })]
        [TestCase(4, new[] { -1, 0, 1, 2 })]
        [TestCase(7, new[] { -3, -2, -1, 0, 1, 2, 3 })]
        [TestCase(42, new[] { -3, -2, -1, 0, 1, 2, 3 })]
        public void VisibleOffsets_NeverRepeatATable(int count, int[] expected)
        {
            CollectionAssert.AreEqual(expected, ArcLayout.VisibleOffsets(count));
        }
    }
}
```

`Assets/Tests/EditMode/ArcRoomTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ArcRoomTests
    {
        private string dir;
        private GameObject host;
        private MediaCache cache;
        private LauncherState state;
        private ArcRoom room;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vrl-room-" + Guid.NewGuid().ToString("N"));
            state = LauncherState.Load(Path.Combine(dir, "state.json"));
            host = new GameObject("RoomTest");
            cache = host.AddComponent<MediaCache>();
        }

        [TearDown]
        public void TearDown()
        {
            if (room != null) UnityEngine.Object.DestroyImmediate(room.gameObject);
            UnityEngine.Object.DestroyImmediate(host);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private (TableListView view, List<TableEntry> all) Build(int count)
        {
            List<TableEntry> all = TestEntries.Make(count);
            var view = new TableListView(all, state);
            room = ArcRoom.Create(view, state, cache);
            return (view, all);
        }

        private int ActiveCabinets() =>
            Enumerable.Range(-3, 7).Count(k => room.CabinetAt(k).gameObject.activeSelf);

        [Test]
        public void ShowsSevenCabinetsCentredOnTheSelection()
        {
            var (view, all) = Build(10);
            Assert.AreEqual(7, ActiveCabinets());
            Assert.AreSame(all[0], room.CabinetAt(0).Entry);
            Assert.AreSame(all[9], room.CabinetAt(-1).Entry);
            Assert.AreEqual(all[0].Title, room.InfoTitle);
            Assert.AreEqual("All · 1 / 10", room.InfoStatus);
        }

        [Test]
        public void Next_ShiftsEntriesAndStartsTheGlide()
        {
            var (_, all) = Build(10);
            room.Next();
            Assert.AreSame(all[1], room.CabinetAt(0).Entry);
            Assert.AreSame(all[0], room.CabinetAt(-1).Entry);
            Assert.AreEqual(1f, room.ScrollOffset, 1e-5f);
        }

        [Test]
        public void RapidNext_ClampsScrollAndKeepsCentreInSync()
        {
            var (view, all) = Build(10);
            for (int i = 0; i < 5; i++) room.Next();
            Assert.AreEqual(ArcRoom.MaxScroll, room.ScrollOffset, 1e-5f);
            Assert.AreSame(view.Selected, room.CabinetAt(0).Entry);
            Assert.AreSame(all[5], room.CabinetAt(0).Entry);
        }

        [Test]
        public void FewTables_OnlyShowThatManyCabinets()
        {
            Build(2);
            Assert.AreEqual(2, ActiveCabinets());
            Assert.IsTrue(room.CabinetAt(1).gameObject.activeSelf);
            Assert.IsFalse(room.CabinetAt(-1).gameObject.activeSelf);
        }

        [Test]
        public void EmptyView_ShowsPlaceholder()
        {
            Build(3);
            room.CycleView();   // Favorites: none yet
            Assert.AreEqual(1, ActiveCabinets());
            Assert.AreEqual("No favorites yet", room.CabinetAt(0).PlaceholderText);
            Assert.AreEqual("No favorites yet", room.InfoTitle);
        }

        [Test]
        public void Refresh_ShowsFavoriteAndLastPlayed()
        {
            var (_, all) = Build(3);
            state.ToggleFavorite(all[0].RelativePath);
            state.RecordPlay(all[0].RelativePath, DateTime.UtcNow.AddDays(-3));
            room.Refresh();
            StringAssert.Contains("Favorite", room.InfoDetail);
            StringAssert.Contains("Last played 3 days ago", room.InfoDetail);
            StringAssert.StartsWith("Bally · 1990", room.InfoDetail);
        }

        [Test]
        public void Notice_OverridesStatusUntilCleared()
        {
            Build(3);
            room.ShowNotice("Keep holding Y to quit... 1.2s");
            Assert.AreEqual("Keep holding Y to quit... 1.2s", room.InfoStatus);
            room.ClearNotice();
            Assert.AreEqual("All · 1 / 3", room.InfoStatus);
        }

        [Test]
        public void Recenter_PutsThePlayfieldAheadAndBelowTheEyes()
        {
            Build(3);
            var head = new Vector3(1f, 1.2f, -2f);   // seated
            room.Recenter(head, 90f);                 // looking along +X

            Vector3 playfield = room.CabinetAt(0).transform.TransformPoint(CabinetView.PlayfieldCenter);
            Assert.AreEqual(head.y - ArcRoom.HeadAbovePlayfield, playfield.y, 1e-4f);
            Assert.AreEqual(head.x + ArcLayout.Radius + CabinetView.PlayfieldCenter.z, playfield.x, 1e-4f);
            Assert.AreEqual(head.z, playfield.z, 1e-4f);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/ArcLayoutTests.cs Assets/Tests/EditMode/ArcRoomTests.cs
git add Assets/Tests && git commit -m "Add arc layout and room tests"
tools/remote.sh test
```

Expected: compilation fails (`ArcLayout`, `ArcRoom` not found).

- [ ] **Step 2: Implement `ArcLayout`**

`Assets/Scripts/Core/ArcLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Where the cabinets stand: on an arc around the player (at the origin, looking along +Z),
    /// one slot per offset, positive offsets to the right. Offsets may be fractional while the
    /// arc is gliding between tables.
    /// </summary>
    public static class ArcLayout
    {
        public const int MaxOffset = 3;
        public const float Radius = 2.2f;
        public const float SpacingDegrees = 22f;

        public static Vector3 SlotPosition(float offset)
        {
            float angle = offset * SpacingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * Radius, 0f, Mathf.Cos(angle) * Radius);
        }

        /// <summary>Turns a cabinet so its front (-Z) faces the player at the origin.</summary>
        public static Quaternion SlotRotation(float offset) => Quaternion.Euler(0f, offset * SpacingDegrees, 0f);

        /// <summary>
        /// The offsets to show for a list of <paramref name="itemCount"/> tables: up to 3 each
        /// side, never so many that the wrap-around shows a table twice.
        /// </summary>
        public static IReadOnlyList<int> VisibleOffsets(int itemCount)
        {
            if (itemCount <= 0) return Array.Empty<int>();
            int left = Math.Min(MaxOffset, (itemCount - 1) / 2);
            int right = Math.Min(MaxOffset, itemCount - 1 - left);
            return Enumerable.Range(-left, left + right + 1).ToList();
        }
    }
}
```

- [ ] **Step 3: Implement `ArcRoom`**

`Assets/Scripts/Arcade/ArcRoom.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace VRLauncher
{
    /// <summary>
    /// The arcade room: floor, back wall, lights, 7 cabinets on an arc, and the info plate
    /// under the centred cabinet. Cabinet k always shows view.EntryAt(k); navigating shifts
    /// the entries and lets the arc glide back into place. Only the centred cabinet plays video.
    /// </summary>
    public sealed class ArcRoom : MonoBehaviour
    {
        /// <summary>The centred playfield sits this far below eye height, seated or standing.</summary>
        public const float HeadAbovePlayfield = 0.35f;
        public const float MaxScroll = 2f;
        private const float ScrollSmoothTime = 0.08f;   // settles in about 0.25 s
        private const float VideoDelay = 0.3f;
        private static readonly Vector3 InfoPlateOffset = new Vector3(0f, 0.45f, -0.40f);

        private readonly Dictionary<int, CabinetView> cabinets = new Dictionary<int, CabinetView>();
        private TableListView view;
        private LauncherState state;
        private MediaCache cache;
        private TextMeshPro infoTitle;
        private TextMeshPro infoDetail;
        private TextMeshPro infoStatus;
        private string notice;
        private float scroll;
        private float scrollVelocity;
        private VideoPlayer video;
        private RenderTexture videoTexture;
        private CabinetView videoCabinet;
        private string videoPath;
        private float videoCountdown = -1f;
        private bool suspended;

        public float ScrollOffset => scroll;
        public string InfoTitle => infoTitle.text;
        public string InfoDetail => infoDetail.text;
        public string InfoStatus => infoStatus.text;

        public CabinetView CabinetAt(int offset) => cabinets[offset];

        public static ArcRoom Create(TableListView view, LauncherState state, MediaCache cache)
        {
            var room = new GameObject("ArcRoom").AddComponent<ArcRoom>();
            room.view = view;
            room.state = state;
            room.cache = cache;
            room.Build();
            room.Assign();
            return room;
        }

        private void Build()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.10f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.06f;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);

            Surface("Floor", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(40f, 40f, 1f), new Color(0.03f, 0.03f, 0.04f), 0.8f);
            Surface("BackWall", new Vector3(0f, 3f, 6.5f), Quaternion.identity, new Vector3(30f, 8f, 1f), new Color(0.04f, 0.04f, 0.06f), 0.2f);

            // The centre slot never moves, so one spotlight on it is the "key light".
            Vector3 target = ArcLayout.SlotPosition(0f) + CabinetView.PlayfieldCenter;
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 45f;
            key.range = 6f;
            key.intensity = 3f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(0f, 3.2f, 0.9f);
            key.transform.localRotation = Quaternion.LookRotation(target - key.transform.localPosition);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.3f;
            fill.color = new Color(0.75f, 0.8f, 1f);
            fill.transform.SetParent(transform, false);
            fill.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);

            for (int k = -ArcLayout.MaxOffset; k <= ArcLayout.MaxOffset; k++)
            {
                cabinets[k] = CabinetView.Create(transform, cache);
            }

            var plate = new GameObject("InfoPlate").transform;
            plate.SetParent(transform, false);
            plate.localPosition = ArcLayout.SlotPosition(0f) + InfoPlateOffset;
            plate.localRotation = Quaternion.Euler(35f, 0f, 0f);
            infoTitle = PlateLine(plate, "Title", 0.07f, new Vector2(0.9f, 0.08f));
            infoDetail = PlateLine(plate, "Detail", 0f, new Vector2(0.9f, 0.05f));
            infoStatus = PlateLine(plate, "Status", -0.06f, new Vector2(0.9f, 0.045f));
            infoStatus.color = new Color(0.7f, 0.7f, 0.75f);

            videoTexture = new RenderTexture(1920, 1080, 0) { name = "PlayfieldVideo" };
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.isLooping = true;
            video.skipOnDrop = true;
            video.source = VideoSource.Url;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = videoTexture;
            video.prepareCompleted += OnVideoPrepared;
            video.errorReceived += OnVideoError;
        }

        public void Next()
        {
            if (view.Items.Count == 0) return;
            view.Next();
            scroll = Mathf.Clamp(scroll + 1f, -MaxScroll, MaxScroll);
            Assign();
        }

        public void Previous()
        {
            if (view.Items.Count == 0) return;
            view.Previous();
            scroll = Mathf.Clamp(scroll - 1f, -MaxScroll, MaxScroll);
            Assign();
        }

        public void CycleView()
        {
            view.CycleView();
            scroll = 0f;
            scrollVelocity = 0f;
            Assign();
        }

        /// <summary>Re-reads favorites and play history (for example after returning from a table).</summary>
        public void Refresh()
        {
            view.Refresh();
            Assign();
        }

        /// <summary>Places the room so the centred playfield is ahead of and just below the head.</summary>
        public void Recenter(Vector3 headPosition, float yawDegrees)
        {
            float floor = headPosition.y - (CabinetView.PlayfieldCenter.y + HeadAbovePlayfield);
            transform.SetPositionAndRotation(new Vector3(headPosition.x, floor, headPosition.z), Quaternion.Euler(0f, yawDegrees, 0f));
        }

        public void PulseCenter() => CabinetAt(0).Pulse();

        public void ShowNotice(string message)
        {
            notice = message;
            UpdateInfo();
        }

        public void ClearNotice()
        {
            notice = null;
            UpdateInfo();
        }

        /// <summary>Stops video while a table is running.</summary>
        public void Suspend()
        {
            suspended = true;
            StopVideo();
        }

        public void Resume()
        {
            suspended = false;
            videoCountdown = VideoDelay;
        }

        private void Assign()
        {
            int count = view.Items.Count;
            IReadOnlyList<int> visible = ArcLayout.VisibleOffsets(count);
            TableEntry oldCentre = CabinetAt(0).Entry;

            foreach (KeyValuePair<int, CabinetView> pair in cabinets)
            {
                TableEntry entry = visible.Contains(pair.Key) ? view.EntryAt(pair.Key) : null;
                if (!ReferenceEquals(pair.Value.Entry, entry))
                {
                    pair.Value.SetEntry(entry);
                }
                pair.Value.SetFocused(pair.Key == 0);
            }

            if (count == 0)
            {
                CabinetAt(0).SetPlaceholder(view.EmptyMessage);
            }

            if (!ReferenceEquals(oldCentre, CabinetAt(0).Entry))
            {
                StopVideo();
                videoCountdown = VideoDelay;
            }

            UpdateInfo();
            Layout();
        }

        private void Layout()
        {
            // An empty view still shows its placeholder cabinet.
            IReadOnlyList<int> visible = ArcLayout.VisibleOffsets(Math.Max(view.Items.Count, 1));
            foreach (KeyValuePair<int, CabinetView> pair in cabinets)
            {
                float position = pair.Key + scroll;
                bool show = visible.Contains(pair.Key) && Mathf.Abs(position) <= ArcLayout.MaxOffset + 0.5f;
                pair.Value.gameObject.SetActive(show);
                if (show)
                {
                    pair.Value.transform.localPosition = ArcLayout.SlotPosition(position);
                    pair.Value.transform.localRotation = ArcLayout.SlotRotation(position);
                }
            }
        }

        private void UpdateInfo()
        {
            TableEntry entry = view.Selected;
            infoTitle.text = entry?.Title ?? view.EmptyMessage;
            infoDetail.text = entry == null ? string.Empty : DetailLine(entry);
            infoStatus.text = notice ?? view.PositionLabel;
        }

        private string DetailLine(TableEntry entry)
        {
            var parts = new List<string>();
            if (entry.Subtitle.Length > 0) parts.Add(entry.Subtitle);
            if (state.IsFavorite(entry.RelativePath)) parts.Add("<color=#FFC940>Favorite</color>");
            DateTime? last = state.LastPlayed(entry.RelativePath);
            if (last.HasValue) parts.Add("Last played " + RelativeTime.Format(last.Value, DateTime.UtcNow));
            return string.Join(" · ", parts);
        }

        private void Update()
        {
            if (scroll != 0f)
            {
                scroll = Mathf.SmoothDamp(scroll, 0f, ref scrollVelocity, ScrollSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                if (Mathf.Abs(scroll) < 0.0005f)
                {
                    scroll = 0f;
                    scrollVelocity = 0f;
                }
                Layout();
            }

            if (videoCountdown >= 0f && scroll == 0f)
            {
                videoCountdown -= Time.unscaledDeltaTime;
                if (videoCountdown < 0f) StartVideo();
            }
        }

        private void StartVideo()
        {
            if (!Application.isPlaying || suspended) return;
            CabinetView centre = CabinetAt(0);
            string path = centre.Entry?.Media.Video;
            if (path == null || (path == videoPath && video.isPlaying)) return;

            video.Stop();
            videoCabinet = centre;
            videoPath = path;
            video.url = path;
            video.Prepare();
        }

        private void StopVideo()
        {
            if (video != null) video.Stop();
            if (videoCabinet != null) videoCabinet.ShowStill();
            videoCabinet = null;
            videoPath = null;
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (suspended || videoCabinet == null || source.url != videoPath) return;
            source.Play();
            videoCabinet.ShowVideo(videoTexture);
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"ArcRoom: video failed for {videoPath}: {message}");
            if (videoCabinet != null) videoCabinet.ShowStill();
            videoCabinet = null;
            videoPath = null;
        }

        private void Surface(string name, Vector3 position, Quaternion rotation, Vector3 scale, Color color, float gloss)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", gloss);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static TextMeshPro PlateLine(Transform plate, string name, float y, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(plate, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = 4f;
            text.color = Color.white;
            return text;
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                if (Application.isPlaying) Destroy(videoTexture); else DestroyImmediate(videoTexture);
            }
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Core/ArcLayout.cs Assets/Scripts/Arcade/ArcRoom.cs
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
git add Assets/Scripts
git commit -m "Add the arc room with gliding navigation, info plate and centre video"
tools/remote.sh test
```

Expected: all tests pass.

- [ ] **Step 5: Render and inspect the arc**

Add to `ArcadePreview`:

```csharp
        /// <summary>The whole room from a standing player's eyes, headset-like field of view.</summary>
        public static void RenderArc()
        {
            Run("preview-arc.png", () =>
            {
                List<TableEntry> tables = LoadInstalledCatalog();
                string stateFile = Path.Combine(Path.GetTempPath(), "vrl-preview-state.json");
                if (File.Exists(stateFile)) File.Delete(stateFile);
                LauncherState state = LauncherState.Load(stateFile);
                var view = new TableListView(tables, state);
                view.Select(tables.First(t => t.Media.Playfield != null).RelativePath);

                var host = new GameObject("Preview");
                ArcRoom room = ArcRoom.Create(view, state, host.AddComponent<MediaCache>());
                var head = new Vector3(0f, 1.66f, 0f);
                room.Recenter(head, 0f);
                return MakeCamera(head, Quaternion.Euler(15f, 0f, 0f), 100f);
            });
        }
```

```bash
git commit -am "Add a preview render of the whole arc"
tools/remote.sh batch VRLauncher.EditorTools.ArcadePreview.RenderArc
tools/remote.sh pull Logs/preview-arc.png /tmp/preview-arc.png
```

Open `/tmp/preview-arc.png` with the Read tool and check: 7 cabinets on a curve, the centre one brightest and lit by the key light, the info plate readable below the centre cabinet without covering its apron wheel, nothing clipping into the floor. If the info plate overlaps the wheel or sits off-screen, adjust `InfoPlateOffset`; if the key light misses, adjust its position. Commit any tweak with a short message and describe the render in the task report.

---

## Task 10: Cabinet polish (added 2026-09-24 at the user's request)

**Files:**
- Create: `Assets/Scripts/Arcade/CabinetMesh.cs`
- Modify: `Assets/Scripts/Arcade/CabinetView.cs`, `Assets/Tests/EditMode/CabinetViewTests.cs`
- Modify only if they mention the apron wheel: comments in `Assets/Scripts/Arcade/ArcRoom.cs`

**Interfaces:**
- Consumes: `CabinetView` (Task 8), `ArcadePreview.RenderCabinets` / `RenderArc` (Tasks 8 and 9).
- Produces: `static class CabinetMesh { Mesh Wedge(float width, float zStart, float zEnd, float yBottom, float yTop); }`. `CabinetView` loses `ApronWheelVisible` and gains `TopperVisible`; every other member is unchanged.

**What changes, and why.** The first preview render showed three things:
- **The gap.** The 35 degree playfield leaves an open triangular gap on each side between the base and the playfield, so the cabinet looks hollow from any angle but straight on.
- **The front.** It is a blank box. The user asked for a coin door there, and chose to move the wheel up to a **topper** above the backbox.
- **The rails.** Thin side rails along the playfield's long edges frame it and hide the join.

The fixes:
- A generated wedge mesh (`PlayfieldBase`) fills the volume under the playfield. It replaces the old `HeadSupport` box.
- The two playfield side rails are added.
- The coin door is built from primitives: a chrome trim plate, a dark metal door, and two red-lit coin slots.
- The wheel moves from the apron to a topper above the backbox, and the no-wheel title text moves with it.

- [ ] **Step 1: Write the failing tests**

In `Assets/Tests/EditMode/CabinetViewTests.cs`:
- Replace every `cabinet.ApronWheelVisible` with `cabinet.TopperVisible`. That is one `IsTrue`, in `FullMedia_ShowsPlayfieldBackglassAndApronWheel`: rename that test to `FullMedia_ShowsPlayfieldBackglassAndTopper`. It is also one `IsFalse`, in `NoWheel_ShowsTheTitleOnTheMarquee`.
- Add these tests to the class:

```csharp
        [Test]
        public void Wedge_FacesPointOutward()
        {
            Mesh mesh = CabinetMesh.Wedge(0.72f, 0.078f, 1.08f, 0.96f, 1.66f);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3 inside = Vector3.zero;
            foreach (Vector3 vertex in vertices) inside += vertex;
            inside /= vertices.Length;

            Assert.AreEqual(8, triangles.Length / 3);   // 2 side triangles + 3 quads
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Assert.Greater(Vector3.Dot(normal, (a + b + c) / 3f - inside), 0f, $"triangle {i / 3} faces inward");
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void PlayfieldBase_FillsTheGapJustUnderThePlayfield()
        {
            Transform wedge = cabinet.transform.Find("PlayfieldBase");
            Assert.IsNotNull(wedge);
            Assert.IsNull(cabinet.transform.Find("HeadSupport"));

            Vector3 normal = CabinetView.PlayfieldRotation * Vector3.back;
            float highest = float.MinValue;
            foreach (Vector3 vertex in wedge.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                float above = Vector3.Dot(vertex - CabinetView.PlayfieldCenter, normal);
                Assert.LessOrEqual(above, 0.001f, "wedge pokes through the playfield");
                highest = Mathf.Max(highest, above);
            }
            Assert.Greater(highest, -0.02f, "wedge leaves a visible gap under the playfield");
        }

        [Test]
        public void Playfield_HasSideRails()
        {
            Assert.IsNotNull(cabinet.transform.Find("RailLeft"));
            Assert.IsNotNull(cabinet.transform.Find("RailRight"));
        }

        [Test]
        public void Front_HasACoinDoorWithTwoLitSlots()
        {
            Assert.IsNotNull(cabinet.transform.Find("CoinDoor"));
            Assert.IsNotNull(cabinet.transform.Find("CoinDoorTrim"));
            int slots = 0;
            foreach (Transform child in cabinet.transform)
            {
                if (child.name == "CoinSlot") slots++;
            }
            Assert.AreEqual(2, slots);
        }
```

```bash
git add Assets/Tests && git commit -m "Add cabinet polish tests"
tools/remote.sh test CabinetViewTests
```

Expected: compilation fails (`CabinetMesh` not found, `TopperVisible` not found).

- [ ] **Step 2: Add the wedge mesh**

`Assets/Scripts/Arcade/CabinetMesh.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>Meshes for cabinet parts that Unity's primitives cannot make.</summary>
    public static class CabinetMesh
    {
        /// <summary>
        /// A solid wedge <paramref name="width"/> wide, centred on X, whose side profile is the right
        /// triangle (zStart, yBottom), (zEnd, yTop), (zEnd, yBottom): a flat bottom, a vertical back
        /// and a rising slope. Faces do not share vertices, so each is flat-shaded, and every
        /// triangle is wound to face outward.
        /// </summary>
        public static Mesh Wedge(float width, float zStart, float zEnd, float yBottom, float yTop)
        {
            float half = width / 2f;
            var leftFront = new Vector3(-half, yBottom, zStart);
            var leftTop = new Vector3(-half, yTop, zEnd);
            var leftBack = new Vector3(-half, yBottom, zEnd);
            var rightFront = new Vector3(half, yBottom, zStart);
            var rightTop = new Vector3(half, yTop, zEnd);
            var rightBack = new Vector3(half, yBottom, zEnd);

            // Any point with positive weight on every corner is strictly inside the solid.
            Vector3 inside = (leftFront + leftTop + leftBack + rightFront + rightTop + rightBack) / 6f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Corners go round the face's perimeter; winding is fixed per triangle below.
            void Face(params Vector3[] corners)
            {
                int start = vertices.Count;
                vertices.AddRange(corners);
                for (int i = 1; i < corners.Length - 1; i++)
                {
                    int a = start, b = start + i, c = start + i + 1;
                    Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                    if (Vector3.Dot(normal, centroid - inside) < 0f)
                    {
                        (b, c) = (c, b);
                    }
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }

            Face(leftFront, leftTop, leftBack);
            Face(rightFront, rightTop, rightBack);
            Face(leftFront, leftBack, rightBack, rightFront);   // bottom
            Face(leftBack, leftTop, rightTop, rightBack);       // back
            Face(leftFront, rightFront, rightTop, leftTop);     // slope

            var mesh = new Mesh { name = "CabinetWedge" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Arcade/CabinetMesh.cs
```

- [ ] **Step 3: Update `CabinetView`**

Make these edits to `Assets/Scripts/Arcade/CabinetView.cs`.

1. **Constants.** Replace the `ApronCenter` and `ApronWheelSize` constants with:

```csharp
        private static readonly Vector3 TopperCenter = new Vector3(0f, 2.49f, 1.07f);
        private const float TopperSize = 0.40f;
        private static readonly Vector3 CoinDoorCenter = new Vector3(0f, 0.62f, 0f);
```

2. **Fields.** Rename the field `apronWheel` to `topper`, and the property `ApronWheelVisible` to `TopperVisible` (`public bool TopperVisible => topper.activeSelf;`). Add these static fields next to `bodyMaterial`:

```csharp
        private static Material doorMaterial;
        private static Material trimMaterial;
        private static Material coinLightMaterial;
        private static Mesh wedgeMesh;
```

   Add these static properties next to `BodyMaterial`, in the same lazy style:

```csharp
        private static Material DoorMaterial => doorMaterial != null ? doorMaterial : (doorMaterial = Metal("CoinDoor", new Color(0.13f, 0.13f, 0.14f), 0.8f, 0.6f));
        private static Material TrimMaterial => trimMaterial != null ? trimMaterial : (trimMaterial = Metal("CoinDoorTrim", new Color(0.75f, 0.75f, 0.78f), 0.9f, 0.85f));
        private static Material CoinLightMaterial => coinLightMaterial != null ? coinLightMaterial
            : (coinLightMaterial = new Material(Shader.Find("Sprites/Default")) { name = "CoinLight", color = new Color(1f, 0.18f, 0.12f) });
        private static Mesh WedgeMesh => wedgeMesh != null ? wedgeMesh : (wedgeMesh = CabinetMesh.Wedge(0.72f, 0.078f, 1.08f, 0.96f, 1.66f));

        private static Material Metal(string name, Color color, float metallic, float gloss)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", gloss);
            return material;
        }
```

   The wedge's slope runs from (z 0.078, y 0.96) to (z 1.08, y 1.66). That puts it 8 to 14 mm under the playfield plane along its whole length, and the test in Step 1 pins it.

3. **`Build()`.**
   - **Remove** the `Box("HeadSupport", ...)` line.
   - **Add after the LockdownBar line:**

```csharp
            // Fills the space under the steeply tilted playfield so the cabinet is solid from the side.
            AddMesh("PlayfieldBase", WedgeMesh, body);

            Vector3 lift = PlayfieldRotation * Vector3.back * 0.02f;
            foreach (float side in new[] { -1f, 1f })
            {
                Transform rail = Box(side < 0f ? "RailLeft" : "RailRight",
                                     PlayfieldCenter + lift + new Vector3(side * 0.365f, 0f, 0f),
                                     new Vector3(0.03f, 0.05f, 1.27f), body);
                rail.localRotation = Quaternion.Euler(-35f, 0f, 0f);   // along the playfield slope
            }

            BuildCoinDoor();
```

   - **Replace** the apron wheel and marquee lines:

```csharp
            wheelMaterial = ArtMaterial("TopperWheel");
            topper = Quad("TopperWheel", TopperCenter, Quaternion.identity,
                          new Vector3(TopperSize, TopperSize, 1f), wheelMaterial).gameObject;

            marquee = Label("Marquee", TopperCenter + new Vector3(0f, -0.08f, -0.005f), new Vector2(0.66f, 0.26f));
```

   (`placeholder` stays on the backglass.)

4. **`SetEntry`.** Replace `apronWheel.SetActive(false)` with `topper.SetActive(false)`, and `apronWheel.SetActive(true)` with `topper.SetActive(true)`.

5. **New helpers,** next to `Box` and `Quad`:

```csharp
        private void BuildCoinDoor()
        {
            // The body's front face is z = 0: the trim sits almost flush, the door stands proud of it,
            // and the lit coin slots sit on the door.
            Box("CoinDoorTrim", CoinDoorCenter + new Vector3(0f, 0f, -0.004f), new Vector3(0.33f, 0.43f, 0.012f), TrimMaterial);
            Box("CoinDoor", CoinDoorCenter + new Vector3(0f, 0f, -0.010f), new Vector3(0.30f, 0.40f, 0.02f), DoorMaterial);
            foreach (float x in new[] { -0.065f, 0.065f })
            {
                Quad("CoinSlot", CoinDoorCenter + new Vector3(x, 0.08f, -0.021f), Quaternion.identity,
                     new Vector3(0.045f, 0.06f, 1f), CoinLightMaterial);
            }
        }

        private Transform AddMesh(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
```

6. **Class summary.** Update it to say the wheel is on a topper above the backbox and the front carries a coin door, and change "no wheel shows the title on the apron" to "on the topper". `OnDestroy` stays as it is. The new materials and the wedge mesh are shared statics, like `bodyMaterial`, and must not be destroyed per cabinet.

7. **ArcRoom.** If `ArcRoom.cs` comments mention the apron wheel (for example beside `InfoPlateOffset`), change them to "coin door". Do not change any values.

- [ ] **Step 4: Run the tests to see them pass**

```bash
git add Assets
git commit -m "Close the cabinet under the playfield, add rails and a coin door, and move the wheel to a topper"
tools/remote.sh test
```

Expected: all tests pass (Task 9's total plus the 4 new tests).

- [ ] **Step 5: Re-render and inspect**

```bash
tools/remote.sh batch VRLauncher.EditorTools.ArcadePreview.RenderCabinets
tools/remote.sh pull Logs/preview-cabinets.png .superpowers/sdd/2026-09-24-vr-arcade-room/preview-cabinets-polish.png
tools/remote.sh batch VRLauncher.EditorTools.ArcadePreview.RenderArc
tools/remote.sh pull Logs/preview-arc.png .superpowers/sdd/2026-09-24-vr-arcade-room/preview-arc-polish.png
```

Open both with the Read tool and check:
1. The side cabinets in the cabinet render show a **solid side** under the playfield, with no triangular gap. If the wedge is invisible, its faces are inside-out. Check `Wedge_FacesPointOutward`.
2. The rails run along both long edges of the playfield without floating above it or sinking into it.
3. The coin door is centred on the front, with two red slots near its top.
4. The wheel sits above each backbox as a topper. On Pinball Training Lab the title text sits there instead.
5. In the arc render, the info plate does not cover the coin door, and the toppers are not cut off at the top of the frame.

Fix any constant that is visibly off and re-render. Describe each check in the report.

---

## Task 11: Wire it up and retire the carousel

**Files:**
- Create: `Assets/Scripts/Arcade/ScreenFader.cs`, `Assets/Scripts/LauncherBootstrap.cs`, `Assets/Editor/ArcadeSceneSetup.cs`
- Modify: `Assets/Scripts/SceneDiagnostic.cs`, `Assets/Scenes/VRLauncher.unity` (via the editor method), `ProjectSettings/GraphicsSettings.asset` (via the editor method)
- Delete: `Assets/Scripts/TableCarousel.cs`, `TableScanner.cs`, `VRMenuController.cs`, `VRLauncherManager.cs`, `VRJoystickMapper.cs` (each with its `.meta`), `Assets/Prefabs/TableItem.prefab` (+ `.meta`)
- Test: `Assets/Tests/EditMode/ScreenFaderTests.cs`

**Interfaces:**
- Consumes: everything above; `TableLauncher.LaunchTable(string)`, `TableLauncher.IsTableRunning()`, `TableLauncher.OnTableExited` (raised on the main thread right after XR restart is requested), `LauncherConfig.Instance`, `LauncherPaths.CatalogSettingsFrom`.
- Produces: `sealed class ScreenFader : MonoBehaviour { static ScreenFader Create(Camera camera); float Alpha; string Text; void SetImmediate(float alpha, string text = null); IEnumerator Fade(float to, float seconds, string text = null); }`; `LauncherBootstrap` (scene entry point); editor method `VRLauncher.EditorTools.ArcadeSceneSetup.Run`.

`VRControllerInput` (maps controller buttons to keys while a table runs) and `ControllerBridge` stay; the bootstrap adds them exactly as `TableCarousel` did.

- [ ] **Step 1: Write the failing fader test**

`Assets/Tests/EditMode/ScreenFaderTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ScreenFaderTests
    {
        private GameObject cameraObject;

        [SetUp]
        public void SetUp() => cameraObject = new GameObject("FaderCamera", typeof(Camera));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(cameraObject);

        [Test]
        public void SetImmediate_ShowsAndHidesTheOverlay()
        {
            ScreenFader fader = ScreenFader.Create(cameraObject.GetComponent<Camera>());
            fader.SetImmediate(1f, "Loading Congo…");
            Assert.AreEqual(1f, fader.Alpha);
            Assert.AreEqual("Loading Congo…", fader.Text);
            Assert.IsTrue(fader.GetComponentInChildren<MeshRenderer>().enabled);

            fader.SetImmediate(0f);
            Assert.IsFalse(fader.GetComponentInChildren<MeshRenderer>().enabled);
        }

        [Test]
        public void Overlay_SitsInsideTheNearClipAndInFrontOfTheCamera()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            ScreenFader fader = ScreenFader.Create(camera);
            float distance = fader.transform.localPosition.z;
            Assert.Greater(distance, camera.nearClipPlane);
            Assert.Less(distance, 1f);
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Tests/EditMode/ScreenFaderTests.cs
git add Assets/Tests && git commit -m "Add screen fader tests"
tools/remote.sh test
```

Expected: compilation fails (`ScreenFader` not found).

- [ ] **Step 2: Implement `ScreenFader`**

`Assets/Scripts/Arcade/ScreenFader.cs`:

```csharp
using System.Collections;
using TMPro;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// A head-locked black overlay with one line of text, used to fade out before a table
    /// launches ("Loading ...") and to fade the room back in afterwards.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        private const float Distance = 0.35f;
        private Material material;
        private MeshRenderer overlay;
        private TextMeshPro label;

        public float Alpha { get; private set; }
        public string Text => label.text;

        public static ScreenFader Create(Camera camera)
        {
            camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f);

            var go = new GameObject("ScreenFader");
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, Distance);
            var fader = go.AddComponent<ScreenFader>();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Overlay";
            Collider collider = quad.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(3f, 3f, 1f);   // covers a 110 degree view at this distance
            fader.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 5000 };
            fader.overlay = quad.GetComponent<MeshRenderer>();
            fader.overlay.sharedMaterial = fader.material;

            var textObject = new GameObject("Label");
            textObject.transform.SetParent(go.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            fader.label = textObject.AddComponent<TextMeshPro>();
            fader.label.rectTransform.sizeDelta = new Vector2(0.4f, 0.08f);
            fader.label.alignment = TextAlignmentOptions.Center;
            fader.label.enableAutoSizing = true;
            fader.label.fontSizeMin = 0.1f;
            fader.label.fontSizeMax = 2f;
            fader.label.fontMaterial.renderQueue = 5001;   // a per-object copy, so other text is unaffected

            fader.SetImmediate(0f);
            return fader;
        }

        public void SetImmediate(float alpha, string text = null)
        {
            if (text != null) label.text = text;
            Apply(alpha);
        }

        public IEnumerator Fade(float to, float seconds, string text = null)
        {
            if (text != null) label.text = text;
            float from = Alpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            Apply(to);
        }

        private void Apply(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
            material.color = new Color(0f, 0f, 0f, Alpha);
            label.alpha = Alpha;
            overlay.enabled = Alpha > 0.001f;
            label.enabled = Alpha > 0.001f;
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/Arcade/ScreenFader.cs
git add Assets/Scripts/Arcade && git commit -m "Add a head-locked screen fader"
tools/remote.sh test
```

Expected: all tests pass.

- [ ] **Step 3: Write `LauncherBootstrap`**

`Assets/Scripts/LauncherBootstrap.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRLauncher
{
    /// <summary>
    /// Scene entry point. Builds the catalog, state, list view, arcade room, fader and input,
    /// and runs the launch and return flow around TableLauncher: fade out with "Loading ...",
    /// launch, and when VPX exits, record the play, wait for XR, recentre and fade back in.
    /// </summary>
    public sealed class LauncherBootstrap : MonoBehaviour
    {
        private const float FadeSeconds = 0.4f;
        private const float XrReadyTimeoutSeconds = 5f;

        private TableLauncher launcher;
        private LauncherInput input;
        private ArcRoom room;
        private ScreenFader fader;
        private LauncherState state;
        private TableListView view;
        private bool launching;
        private bool quitNoticeShown;

        private void Start()
        {
            Application.runInBackground = true;
            Camera head = Camera.main;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color(0.02f, 0.02f, 0.03f);

            LauncherConfig config = LauncherConfig.Instance;
            launcher = GetOrAdd<TableLauncher>();
            GetOrAdd<VRControllerInput>();
            GetOrAdd<ControllerBridge>();
            _ = UnityMainThreadDispatcher.Instance;
            MediaCache cache = gameObject.AddComponent<MediaCache>();

            List<TableEntry> tables = TableCatalog.Scan(LauncherPaths.CatalogSettingsFrom(config), new DiskFileSystem());
            LogCatalog(tables, config);

            state = LauncherState.Load(Path.Combine(Application.persistentDataPath, "state.json"));
            if (state.LoadWarning != null) Debug.LogWarning(state.LoadWarning);

            view = new TableListView(tables, state);
            room = ArcRoom.Create(view, state, cache);
            if (tables.Count == 0) room.ShowNotice($"Tables folder: {config.tablesDirectory}");

            fader = ScreenFader.Create(head);
            fader.SetImmediate(1f);

            input = gameObject.AddComponent<LauncherInput>();
            input.InputEnabled = false;
            input.Previous += room.Previous;
            input.Next += room.Next;
            input.CycleView += room.CycleView;
            input.ToggleFavorite += ToggleFavorite;
            input.Launch += () => StartCoroutine(LaunchSelected());
            input.Quit += Quit;
            launcher.OnTableExited += OnTableExited;

            StartCoroutine(Arrive());
        }

        private void Update()
        {
            if (input == null) return;
            if (input.QuitHeld)
            {
                float remaining = (1f - input.QuitHoldProgress) * LauncherInput.QuitHoldSeconds;
                room.ShowNotice($"Keep holding Y to quit... {remaining:0.0}s");
                quitNoticeShown = true;
            }
            else if (quitNoticeShown)
            {
                room.ClearNotice();
                quitNoticeShown = false;
            }
        }

        /// <summary>Waits for the headset, places the room in front of it, and fades in.</summary>
        private IEnumerator Arrive()
        {
            float waited = 0f;
            while (waited < XrReadyTimeoutSeconds && !HeadsetTracked())
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (waited >= XrReadyTimeoutSeconds) Debug.LogWarning("Headset not tracked after 5 s; placing the room from the current camera pose.");

            yield return null;   // one more frame so the camera has the tracked pose
            Transform head = Camera.main.transform;
            room.Recenter(head.position, head.eulerAngles.y);
            room.Resume();
            yield return fader.Fade(0f, FadeSeconds);
            input.InputEnabled = true;
        }

        private static bool HeadsetTracked()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete || manager.activeLoader == null) return false;
            InputDevice headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return headset.isValid && headset.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private IEnumerator LaunchSelected()
        {
            TableEntry entry = view.Selected;
            if (launching || entry == null || launcher.IsTableRunning()) yield break;

            launching = true;
            input.InputEnabled = false;
            room.PulseCenter();
            yield return fader.Fade(1f, FadeSeconds, $"Loading {entry.Title}…");
            room.Suspend();

            if (!launcher.LaunchTable(entry.FullPath))
            {
                room.Resume();
                room.ShowNotice($"Could not start {entry.Title}. See the log.");
                yield return fader.Fade(0f, FadeSeconds);
                input.InputEnabled = true;
                launching = false;
            }
        }

        private void OnTableExited()
        {
            TableEntry entry = view.Selected;
            if (entry != null)
            {
                state.RecordPlay(entry.RelativePath, DateTime.UtcNow);
                SaveState();
            }
            room.ClearNotice();
            room.Refresh();
            fader.SetImmediate(1f, string.Empty);
            launching = false;
            StartCoroutine(Arrive());
        }

        private void ToggleFavorite()
        {
            TableEntry entry = view.Selected;
            if (entry == null) return;
            state.ToggleFavorite(entry.RelativePath);
            SaveState();
            room.Refresh();
        }

        private void SaveState()
        {
            try
            {
                state.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Could not save {state.FilePath}: {ex.Message}");
            }
        }

        private void Quit()
        {
            if (launcher.IsTableRunning()) return;
            Debug.Log("Quitting launcher");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private T GetOrAdd<T>() where T : Component
        {
            T existing = FindFirstObjectByType<T>();
            return existing != null ? existing : gameObject.AddComponent<T>();
        }

        private static void LogCatalog(List<TableEntry> tables, LauncherConfig config)
        {
            Debug.Log($"Catalog: {tables.Count} table(s) in {config.tablesDirectory}; " +
                      $"missing wheel {tables.Count(t => t.Media.Wheel == null)}, " +
                      $"playfield {tables.Count(t => t.Media.Playfield == null)}, " +
                      $"backglass {tables.Count(t => t.Media.Backglass == null)}, " +
                      $"video {tables.Count(t => t.Media.Video == null)}");
            foreach (TableEntry table in tables)
            {
                var missing = new List<string>();
                if (table.Media.Wheel == null) missing.Add("wheel");
                if (table.Media.Playfield == null) missing.Add("playfield");
                if (table.Media.Backglass == null) missing.Add("backglass");
                if (table.Media.Video == null) missing.Add("video");
                if (missing.Count > 0) Debug.Log($"  {table.Stem}: missing {string.Join(", ", missing)}");
            }
        }
    }
}
```

Update `Assets/Scripts/SceneDiagnostic.cs`: replace the `TableCarousel` lookup (lines 29 to 39) with the same check for `LauncherBootstrap`, keeping its log style (`✓ LauncherBootstrap found on: ...` / `✗ No LauncherBootstrap found in scene!`).

- [ ] **Step 4: Write the scene setup method**

`Assets/Editor/ArcadeSceneSetup.cs`:

```csharp
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// One-off migration of VRLauncher.unity from the flat carousel to the arcade room: removes
    /// the carousel canvas and the directional light (the room brings its own lights), swaps
    /// TableCarousel for LauncherBootstrap, and makes sure the shaders the room creates at
    /// runtime are included in builds. Safe to run more than once.
    /// </summary>
    public static class ArcadeSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/VRLauncher.unity";

        [MenuItem("VR Launcher/Set Up Arcade Scene")]
        public static void Run()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == "Canvas" || root.name == "Directional Light")
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }

                GameObject host = scene.GetRootGameObjects().First(g => g.name == "VRLauncherManager");
                foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().Name == "TableCarousel")
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }
                if (host.GetComponent<LauncherBootstrap>() == null)
                {
                    host.AddComponent<LauncherBootstrap>();
                }

                EditorSceneManager.SaveScene(scene);

                AlwaysInclude("Standard");
                AlwaysInclude("Sprites/Default");
                AssetDatabase.SaveAssets();

                Debug.Log("[unity-batch] OK scene migrated");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[unity-batch] FAILED {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void AlwaysInclude(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' not found");

            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var serialized = new SerializedObject(settings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            serialized.ApplyModifiedProperties();
        }
    }
}
```

```bash
tools/new-meta.sh Assets/Scripts/LauncherBootstrap.cs Assets/Editor/ArcadeSceneSetup.cs
git add Assets && git commit -m "Add the launcher bootstrap and the arcade scene migration"
tools/remote.sh test
```

Expected: all tests pass (this also compiles the bootstrap and the editor script).

- [ ] **Step 5: Migrate the scene on the PC and bring it back**

```bash
tools/remote.sh batch VRLauncher.EditorTools.ArcadeSceneSetup.Run
tools/remote.sh status
```

Expected: `ArcadeSceneSetup.Run OK`, and status shows `M Assets/Scenes/VRLauncher.unity` and `M ProjectSettings/GraphicsSettings.asset` only. Check the scene diff mentions no `TableCarousel` guid (`f3e82b4a9d2e4e84fa8b1c9d7e6f3a12`) and contains the `LauncherBootstrap.cs.meta` guid:

```bash
ssh sigilark-gpu "cd 'C:\Users\dayel\Development\GitHub\vr-pinball-launcher'; git diff --stat; Select-String -Path Assets\Scenes\VRLauncher.unity -Pattern f3e82b4a9d2e4e84fa8b1c9d7e6f3a12 | Measure-Object | % Count"
grep guid Assets/Scripts/LauncherBootstrap.cs.meta
```

Expected: count `0`, and the bootstrap guid appears in the scene. Then:

```bash
tools/remote.sh pushback "Migrate the launcher scene to the arcade room"
```

- [ ] **Step 6: Delete the retired scripts and build**

```bash
git rm Assets/Scripts/TableCarousel.cs Assets/Scripts/TableCarousel.cs.meta \
  Assets/Scripts/TableScanner.cs Assets/Scripts/TableScanner.cs.meta \
  Assets/Scripts/VRMenuController.cs Assets/Scripts/VRMenuController.cs.meta \
  Assets/Scripts/VRLauncherManager.cs Assets/Scripts/VRLauncherManager.cs.meta \
  Assets/Scripts/VRJoystickMapper.cs Assets/Scripts/VRJoystickMapper.cs.meta \
  Assets/Prefabs/TableItem.prefab Assets/Prefabs/TableItem.prefab.meta
grep -rn "TableCarousel\|TableScanner\|VRMenuController\|VRLauncherManager\|VRJoystickMapper" Assets --include=*.cs
```

Expected: the grep prints nothing. If `Assets/Prefabs` is now empty, also `git rm Assets/Prefabs.meta` and remove the folder.

```bash
git commit -m "Retire the flat carousel and its unused menu scripts"
tools/remote.sh test
tools/remote.sh build
```

Expected: all tests pass; `Build OK: ...\Build\vr-launch.exe`. If `build.ps1` reports that `vr-launch.exe` is running, stop and ask the user to close it.

---

## Task 12: Deploy, document and hand over

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document the arcade room**

In `README.md`:
- In **Configuration Options**, add `tableMediaDirectory` after `wheelDirectory`: "Folder of per-table media fetched by `tools/fetch_media.py`, one subfolder per table (`wheel.png`, `table.png`, `bg.png`, `table.mp4`). Relative paths are relative to the launcher folder. Defaults to `Media\Tables`."
- Add a section **Arcade Room** after **Table and Media Naming** with: the controls table from the spec (including keyboard F and V), how favorites and Recent work, where `state.json` lives (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\vr-launch\state.json`) and that a damaged one is renamed to `state.json.bad`, and the fallback table from the spec.
- Add a section **Fetching Table Media**: `uv run tools/fetch_media.py` (reads the installed launcher's config by default; `--config` for another), `--dry-run`, `--force "<Table Name>"`, `tools/media-overrides.json` for names that do not match, and that it never overwrites existing files or downloads ROMs.
- In the development notes, add `test.ps1` (EditMode tests), `tools/unity-batch.ps1` with the two `ArcadePreview` methods, and `tools/remote.sh` for driving the PC from a Mac.

Keep the README's existing tone; no em-dashes.

```bash
git commit -am "Document the arcade room, media fetching and the test tooling"
```

- [ ] **Step 2: Confirm the media is complete and deploy**

```bash
tools/remote.sh media | tail -8
```

Expected: `fetched 0 file(s)` (everything already present from Task 6).

Ask the user whether the PC is free (launcher closed, nobody in the headset) before deploying. Then:

```bash
tools/remote.sh deploy
```

Expected: `Deployed (launcher-config.json, ControllerBridge\, Media\ preserved)`.

- [ ] **Step 3: Hand over the headset check**

Ask the user to start **VR Pinball Launcher** in the headset and try, reporting anything off:
1. The room appears in front of them with the centred playfield just below eye level; the centred cabinet's video starts after a moment.
2. Triggers and thumbsticks glide the arc; holding a stick speeds up.
3. B marks a favorite (info plate says Favorite); X cycles All, Favorites, Recent; empty views show the placeholder.
4. A fades to "Loading ..." and the table starts in VR; exiting the table fades back into the room on the same table, now first in Recent.
5. Holding Y shows the countdown and quits.
6. Comfort: tilt angle, distance and text size. These are all constants (`CabinetView.PlayfieldRotation`, `ArcLayout.Radius`, `ArcRoom.InfoPlateOffset`) and easy to tune.

After they have used it, read the player log for problems:

```bash
ssh sigilark-gpu "Select-String -Path \"\$env:USERPROFILE\AppData\LocalLow\DefaultCompany\vr-launch\Player.log\" -Pattern 'Catalog:|missing|ArcRoom|MediaCache|Exception|State file' | Select -First 60 | % Line"
```

Fix anything that shows up (with a test where the cause is in Core or Arcade code), then redeploy.

- [ ] **Step 4: Finish the branch**

Use superpowers:finishing-a-development-branch. Confirm the PR base with the user first (the fork's `master` tracks upstream and does not yet contain the PR #2 work this branch is built on). Reference issue #1 in the PR ("Closes #1"), and when it merges, move issue #1 to Done in GitHub Project #7.
