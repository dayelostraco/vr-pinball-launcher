# Testing and compatibility

Automated tests cover isolated behavior. Runtime smoke tests exercise the built
player and dummy processes. Physical headset/controller/gameplay tests remain
separate; a green CI result does not establish them.

## Automated checks

See [CONTRIBUTING](../CONTRIBUTING.md) for commands. Source checks parse every
tracked PowerShell script, validate JSON and local documentation links. Setup
tests use temporary directories, including failure/rollback cases. Contract
tests verify the same profile/path cases in PowerShell and C#. Python media
tests mock network responses and do not download artwork.

Unity EditMode tests run through `test.ps1`; results are `Logs/editmode-results.xml`
and `Logs/unity-tests.log`. These tests do not cover a real headset or VPX.

## Coverage scope

The merge baseline is 80% lines and 80% branches, measured separately. See
[contribution requirements](../CONTRIBUTING.md#requirements-before-merge) for
reporting and review rules. Passing tests alone do not demonstrate coverage.

| Target | Measurement and limitation |
|---|---|
| Python tools | `coverage.py`, including branches; per-file gates use `Logs/python-coverage.json`. Tests are excluded from production totals. |
| .NET C# | Coverlet runs actual linked Core and standalone VPX helper source under .NET 10 on Windows; `Logs/portable-coverage.xml` reports lines and branches. The project file lists the Unity-dependent files not included in this target. |
| Unity EditMode | Exercises Unity-dependent and portable behavior under the pinned editor. Run `test.ps1 -Coverage` with the pinned coverage package to produce line coverage and HTML under a unique `Logs/unity-coverage-*` folder. Unity does not report branches. A normal `test.ps1` run produces test results, not coverage. |
| PowerShell | `tools/test-powershell.ps1` uses Pester 5.9.1 to run setup, backup, UI, download and build fixtures. It writes `Logs/powershell-coverage.xml` and enforces 80% lines per measured file. Pester supplies no branch metric; its console aggregate counts commands, not lines. |
| Native/VR integration | Copied-player, installer and physical checks provide integration evidence. They do not establish source coverage or broad headset compatibility. |

Menu input decisions run in `MenuInputState`, independently of hardware polling.
Regression tests cover setup suppression, simultaneous actions, trigger/stick
navigation and controls held during a session transition. Bridge argument and
dependency validation similarly runs in portable production code. Keep platform
adapters thin so new decisions can be tested without sending real keystrokes or
launching VPX.

`test.ps1 -Coverage` also enforces 80% lines for each measured production class.
PowerShell coverage includes the thirteen scripts listed in `tools/test-powershell.ps1`,
including the complete legacy `install.ps1`. Its fixtures dot-source the production
functions without entering the menu and mock registration, downloads and shortcuts.
Unexecuted menu lines remain in the measured denominator. Preview/development helpers
and test runners are not part of that measurement. This is a scope limitation, not evidence that
unmeasured scripts meet the baseline. The remote Bash helper has mocked SSH/Git
regression tests in the Python suite; it has no instrumented coverage report.

Build orchestration tests use disposable output/log directories and a script
that emulates compiler results. Deployment tests redirect `LOCALAPPDATA` to a
fixture directory and restore it afterward; they do not update the live launcher.
Real player and installer smoke tests remain separate release requirements.

`tools/test-controller-bridge.ps1 -AutoHotkeyExe <trusted-1.1-interpreter>` parses
the complete AHK bridge with `/iLib`, then uses `/include` to run decision tests
and exit before the bridge's auto-execute section. No native library, headset
polling or synthetic key output is executed. Hosted Windows CI extracts the
SHA-256-verified pinned installer to a temporary folder for this check; it does
not install AutoHotkey. The 69 assertions cover rearming, threshold boundaries,
hysteresis, repeated polling, all five held keys and idempotent owned-key cleanup.
An injected output sink records key actions without sending them. These tests do not provide AHK line/branch coverage or
certify Meta runtime compatibility. See the [official command-line behavior](https://www.autohotkey.com/docs/v1/Scripts.htm#cmd).

`tools/test-vpx-helper.ps1` compiles the standalone background-input helper and
a hidden dummy child. It checks exact argument forwarding, inherited working
directory and SDL hint, child exit codes, invalid executable handling and missing
or recursive target rejection. No real table is launched. Its Win32 focus path
still needs physical/runtime validation. Separately, the .NET target instruments
the complete helper file, including unexecuted native-adapter lines. It tests the
real launch path with hidden children and the focus policy with a simulated clock
and window callbacks; per-file line and branch gates apply to that source too.

`python tools/check-native.py` verifies every bundled bridge DLL against
`tools/native-lock.json`, checks AMD64 PE/DLL headers, and rejects unexpected DLLs.
No library is loaded and no upstream network request occurs in the check. The lock
pins both SHA-256 and the exact upstream source commit; changes need provenance
review. Installer compilation also refuses changed or extra bridge DLLs before
invoking Inno Setup. Hash agreement is integrity evidence, not CVE certification.

## Built-player smoke test

Build first with `build.ps1 -SkipInstaller`, close the installed launcher and
VPX, then run `tools/test-runtime.ps1` on an interactive Windows desktop.
It copies the complete runtime to a unique temporary directory, creates a dummy
table and program, and checks nonzero exit recovery, retry, process-start errors,
missing executable handling, launch-result persistence and unchanged play counts.
It also checks that F2 opens/closes desktop setup without changing configuration.
Emergency recovery launches a temporary dummy wrapper and child, sends Backspace
to the focused test launcher, and verifies both owned processes exit.

The player uses a shared persistent-data location, so the harness snapshots the
existing state, temporarily uses an isolated test state, closes its own player
and restores original bytes in `finally`. Do not launch another player during
the check. On interruption by machine shutdown, restore `state-before.json`
from the printed test directory to the player-state location below. If no
original existed, remove the test-created state instead. No real table or ROM
is launched; test artifacts remain available for diagnosis.

## Physical release checklist

- Existing install: export a backup, upgrade, verify paths/profiles/history.
- New install on another user account or PC: follow the quick start without
  relying on a maintainer's existing configuration.
- Headset: navigation, tracking, recentering, view changes, details, handoff to
  VPX, normal return and emergency recovery.
- Inputs: keyboard, physical cabinet controls and any enabled motion-controller
  bridge. Verify the focused window; physical PinOne buttons are distinct from
  the controller's X button and the keyboard X key.
- Gameplay: coins, start, flippers, plunger, scoring, comfortable audio and DMD
  artwork. A `Startup done` line is only initialization evidence.
- Restore: review a backup in a temporary installation before replacing live
  state; test a cross-PC path review.

## Compatibility evidence

On 24 October 2026, the user confirmed VR startup/display quality, table launch,
coins/start/flippers/plunger, physical PinOne X recentering, normal exit back to
working launcher navigation, and a second table launch on the existing PC setup.
This is user-reported gameplay evidence for the deployed launcher and existing
PC-specific background-input helper. The optional Meta/Oculus bridge was disabled;
the subsequently refactored distributable helper and bridge were not substituted
during that play session.

The rebuilt 1.2.0 candidate passed the [clean-account installer run](https://github.com/dayelostraco/vr-pinball-launcher-development-archive/actions/runs/40139289238)
on 24 October 2026 using GitHub's Windows Server 2025 hosted image. This verified
the real AppId and default per-user installation, default configuration, shortcuts,
native hashes, all five setup-page renders, configuration preservation on upgrade
and removal of uninstall registration. The setup screenshots were reviewed; the
blank-executable review page now gives selection guidance. Package SHA-256:
`214f08fb785491c262c496200c4fce8221d744e450164ed4c235b9888c03b54e`.
This silent-install check does not replace a manual wizard walkthrough, a fresh
Windows 11 user's usability session or physical testing of optional components.

After separating Touch controls into an experimental add-on, the final standard
candidate passed [clean-account validation again](https://github.com/dayelostraco/vr-pinball-launcher-development-archive/actions/runs/40142551176)
on the same hosted Windows image. It explicitly checks that no `ControllerBridge`
directory or either inherited bridge DLL is installed. The local isolated
upgrade test also confirmed existing user-owned bridge files and configuration
are preserved. Standard installer SHA-256:
`4a260aa71dcf395131ababe336f108a24e952c63da05aedae9f0f6d3ba5a9a67`.
The separate experimental ZIP has SHA-256
`db3ba51a2d1c729eb2f46f9693443bdfb809b075638e4360af94e420be449266`;
its packaging tests verify the explicit file inventory, DLL/script hashes and
license notices without running the bridge or enabling it. It remains unverified
on Quest hardware. These package hashes supersede the earlier bundled candidate.

| Environment | Evidence / limits |
|---|---|
| Windows 11 x64 development PC, Unity 6000.6.2f1 | Player builds, EditMode, setup/backup and copied-player recovery checks run here |
| VPX BGFX 10.8.1 rev 5436 | Existing-install field use; PinOne background-input/focus helper tested with Medieval Madness. Recheck each VPX update. |
| OpenXR headset/runtime | Required for VR. This PC has field use, but no comprehensive per-headset/runtime certification matrix; unattended smoke tests can run while the headset is unavailable. |
| Meta/Oculus Touch external bridge | Optional Oculus-runtime-specific bridge; upstream and local mappings documented. Not a generic bridge for every OpenXR controller. |
| Other Windows/VPX/headset combinations | Unverified until someone reports the exact versions and manual checks |
| Ally/Linux | VPX table installation work is separate; this Windows Unity launcher/setup release is not an Ally/Linux port |

The Windows version row records this development machine, not a promise that
all Windows 11 systems have been tested. Add reproducible reports to the matrix
as evidence arrives, rather than broadening support claims from automated counts.

## Logs and state

| File | Location |
|---|---|
| Launcher log | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\vr-launch\Player.log` |
| Favorites/history/results | Same directory, `state.json` |
| Default monitored VPX log | `%APPDATA%\VPinballX\10.8\vpinball.log` |
| Optional focus helper log | `VpxBackgroundInput.log` beside that helper |
| Setup safety backups | `SetupBackups` beside `vr-launch.exe` |

Alternate VPX log locations remain unconfirmed by default monitoring. Include
version details, profile and log excerpts when reporting a failure; do not attach
ROMs, tables, credentials or personal backup archives to public issues.
