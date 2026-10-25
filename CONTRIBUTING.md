# Contributing

Thanks for helping improve VR Pinball Launcher. This independent continuation accepts
issues and pull requests at [dayelostraco/vr-pinball-launcher](https://github.com/dayelostraco/vr-pinball-launcher).
Original upstream work is credited to keithbphillips; fixes for this version should
be reported here so they match the current implementation.

## Get a working checkout

1. Fork and clone the repository on Windows x64. Use a branch for your change.
2. Install Unity Hub and the exact editor in `ProjectSettings/ProjectVersion.txt`
   (currently **6000.6.2f1**), including Windows build support. Activate your own
   Unity license through Hub. Open the project once to restore its pinned packages.
3. The project already configures OpenXR and input packages. Keep the checked-in
   XR settings; do not add the Oculus plugin or switch runtimes just to build.
4. The root `launcher-config.json` is a sample. Copy it to your own build and
   select your paths through setup. Never deploy it over an existing user's file.
5. Run checks below, then build with `build.ps1 -SkipInstaller`. Inno Setup 6 is
   required only when packaging an installer. Hardware is needed for gameplay
   validation, but not for the isolated tests.

## Checks

From Windows PowerShell 5.1 in the repository root, with the launcher/VPX closed:

```powershell
.\tools\check-source.ps1
.\tools\test-installation-contract.ps1
.\tools\test-setup.ps1
.\tools\test-backup.ps1
.\tools\test-downloads.ps1
.\tools\test-setup-ui.ps1
.\test.ps1
Install-Module Pester -RequiredVersion 5.9.1 -Scope CurrentUser
.\tools\test-powershell.ps1
python -m pip install -r requirements-dev.txt
python -m coverage run -m pytest tools/tests -q -p no:cacheprovider
python -m coverage json
python tools/check-coverage.py
python tools/check-docs.py
python tools/check-native.py
```

`test.ps1 -Filter TableReadinessTests` runs a Unity subset. Core and backup tests
use temporary fixtures. The GUI test needs an interactive Windows desktop and
closes its own form. The optional real-player check, after building, is:

```powershell
.\tools\test-runtime.ps1
```

Read [the runtime test procedure](docs/testing.md) first: it launches a copied
player and a dummy process, temporarily isolates shared player history, and
restores it afterward. It never launches real tables. Headset/VPX tests remain a
separate manual step. See [architecture](docs/architecture.md) to find the right
component to change.

Hosted CI runs source checks, setup/backup/contract tests and Python tests. It
does not certify VR compatibility. Unity tests can be dispatched manually on a
trusted, licensed Windows runner labelled `unity`; this repository does not
automatically execute pull-request code on a maintainer's PC.

Install the .NET 10 SDK to run the portable production-code coverage checks:

```powershell
dotnet restore tests/dotnet/Tests/Tests.csproj --locked-mode
dotnet test tests/dotnet/Tests/Tests.csproj --no-restore /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=../../../Logs/portable-coverage.xml /p:Threshold=80 /p:ThresholdType=line%2cbranch
python tools/check-coverage.py Logs/portable-coverage.xml
```

Python coverage enforces at least 80% lines and branches for each measured tool.
The .NET target enforces both thresholds per file across linked Core and standalone
VPX helper source. Its helper tests require Windows and use hidden fixture processes;
they do not focus VPX or exercise a headset. Unity presentation remains a separate scope.
Unity EditMode results alone are not a coverage report. See [testing](docs/testing.md)
for scope and the remaining hardware checks.

## Style and scope

- C#: four-space indentation, focused classes, descriptive names. Keep domain
  behavior in Core and Windows/Unity integration in the main assembly.
- PowerShell: Windows PowerShell 5.1 compatible; one operation per line in
  transaction and UI code. Backup, path, core setup and UI files have distinct
  responsibilities. Optional formatter: install PSScriptAnalyzer **1.25.0** and
  run `tools/format-setup.ps1`; `-Check` verifies formatting.
- If changing launch/path rules, update the shared contract fixture and test
  both implementations. Add tests for observable behavior and meaningful failure
  paths, rather than implementation details.
- Never commit tables, ROMs, personal configuration, backups, community media,
  generated capture frames/videos, credentials or Unity-generated build folders.
  Keep `.meta` files alongside Unity source/assets.
- Keep support docs and control hints current with behavior. Describe remaining
  manual checks honestly; initialization is not verified gameplay.

## Pull requests

All contributions must go through a pull request, including documentation,
dependency updates and maintainer changes. Do not push changes directly to the
default branch. Use a focused branch and complete every section of the
[PR template](.github/PULL_REQUEST_TEMPLATE.md). Draft PRs are welcome while
work is in progress; they are not ready to merge.

### Requirements before merge

A maintainer must review the final diff and validation evidence. All of the
following requirements must be met before a PR is merged:

- **Complete PR description:** explain the problem, resulting behavior, related
  issue (when one exists), validation, coverage, and remaining limitations.
  Mark a section `Not applicable` only with a reason. Include screenshots for
  visible changes and hardware/runtime versions for physical tests.
- **Passing checks:** all applicable CI jobs and local checks must pass on the
  final revision. Resolve review comments and merge conflicts. Unity changes
  require EditMode results; player/installer changes also require the relevant
  build and smoke-test evidence. A skipped, unavailable or failing required
  check is not a pass.
- **Coverage baseline:** require at least **80% line coverage and 80% branch
  coverage**, reported separately, for production code in each supported
  coverage scope. Changed production modules must meet the same baseline;
  strong results elsewhere cannot hide an untested change. Do not reduce
  existing coverage or lower thresholds to make a PR pass. Provide the report
  or CI artifact, measured files, and before/after percentages. Documentation-only
  changes may mark coverage not applicable with a reason.
- **Meaningful tests:** cover changed behavior, boundary cases, error handling
  and regressions. A bug fix needs a regression test that would fail before the
  fix where the behavior is automatable. Do not inflate coverage with assertions
  that only repeat the implementation, exclude difficult production code, or
  count test fixtures as production coverage.
- **Honest measurement:** identify unmeasured files and unsupported metrics.
  Unity's coverage backend does not report branch coverage, and Pester does not
  provide a branch metric. Never report these as 100% or infer branch coverage
  from passing tests. Put changed decision logic in a testable component where
  both metrics can be measured when practical. For integration that cannot be
  measured by the available tools, provide automated integration evidence and
  record the measurement limitation explicitly for maintainer review; this
  does not establish that the 80% baseline has been reached for that scope.
- **Readable code and comments:** use focused functions and clear names. Explain
  non-obvious decisions, lifecycle/ownership rules, platform workarounds, trust
  boundaries and failure recovery. Document public contracts and significant
  side effects, including path bases and units when ambiguous. Keep comments
  accurate; do not narrate obvious statements or add filler to meet a comment
  quota. Remove obsolete code and comments.
- **Current documentation:** update user instructions, configuration examples,
  control hints and developer documentation when behavior changes. Document
  compatibility or migration effects and add a changelog entry for user-visible
  changes. Local documentation links must pass validation.
- **Security and dependencies:** resolve actionable security findings introduced
  by the change. Existing findings need documented triage and disposition; do
  not silence scanners broadly. Preserve dependency locks, reviewed download
  hashes, provenance and required copyright/license notices. Never commit
  secrets or redistribute table/ROM content.

These are merge requirements, not a claim that every part of the existing
codebase already meets them. CI currently enforces coverage for Python tools
and the portable C# target, plus PowerShell line coverage; reviewers must examine the scope and integration
evidence as well as the green checks. The default branch is protected: pull
requests, passing portable/Python/Windows/security checks and resolved review
conversations are required, including for administrators. Force pushes and branch
deletion are disabled. GitHub does not require a second account's approval for
this solo-maintainer project; maintainer review of the diff, PR template and
local evidence remains mandatory. Reapply these settings after recreating a repo.

### Preparing a contribution

The optional Mac/Linux `tools/remote.sh` helper needs `VRL_REMOTE_HOST`,
`VRL_REMOTE_REPO` (Windows path) and `VRL_REMOTE_SCP_REPO` (forward-slash path).
`VRL_REMOTE_GIT_URL` defaults to your checkout's origin. It synchronizes a branch
to your own trusted Windows SSH host; local Windows checks need none of these.

Explain the concrete problem, resulting behavior, and checks performed. Include
screenshots for visible changes and list hardware/runtime versions for physical
tests. Separate unrelated cleanup from behavioral changes where practical. You
do not need a headset to contribute parser, test, documentation or setup fixes.

For bugs, use the issue form and attach relevant log excerpts. Remove personal
paths if desired; never attach ROMs or commercial table content. Small fixes can
go straight to a pull request; discuss larger changes in an issue first.

Contributions are made under the project's [MIT license](LICENSE). Retain
existing copyright and third-party notices. See [release instructions](docs/releasing.md)
for packaging and validation; direct deployment is a maintainer operation.
