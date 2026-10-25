# Existing-installation setup

Open **VR Pinball Launcher Setup** from the Start menu, or press **F2 on the PC
keyboard** while browsing the launcher. A short VR notice appears if the
configured launch program or tables folder is missing, or the config cannot
be read. Setup opens as a separate Windows desktop window.

You can also run it from the launcher folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -STA -File .\tools\setup.ps1
```

## Locations

Choose the **actual VPX executable** and **tables folder**. BGFX and GL builds
are detected in common installation locations, including C: and D:. The actual
VPX path is separate from the program used to launch it:

| Launch choice | Behavior |
|---|---|
| Keep current launch program | Preserves a verified helper and its target. Native VPX paths follow the selected executable. |
| Launch VPX directly | Changes only the default launch program to the selected VPX executable. |
| Use background-input helper | Compiles the bundled helper, writes its target sidecar, and sets the default launcher path to that helper. |

Helper configuration is read from `VpxBackgroundInput.vpx-path.txt`. For older
installed helpers, the adjacent source's hardcoded target can be detected;
that is evidence to review, not proof of what a binary will execute. Unknown
helpers need an explicit target or a deliberate Direct/Helper choice. Setup
does not guess a helper's root directory from the helper's own executable path.

Existing per-table profiles and assignments are preserved. They may use another
VPX version or a shutdown-cleanup host and override the default launch choice.
Media paths, controller bridge settings, and unknown JSON properties survive
setup saves. `realVpinballExecutable` records the actual target independently
of `vpinballExecutable`, which remains the launched program.

## Health

The report covers the launch program, actual VPX version, tables and artwork directories,
candidate/active INI, VR mode, ROM library, native PinMAME/Serum/ScoreView/
FlexDMD/PuP/B2S plugins, legacy VPinMAME/FlexDMD files, external DMD configuration,
and existing launch profiles.

- **Found** means a file or location was detected. It does not prove a table works.
- **Missing** identifies a required launch-program, VPX, or tables-location problem.
- **Needs testing** covers uncertain INI selection, optional components, and
  hardware or table behavior that cannot be inferred from files.

Native plugins and legacy COM add-ons are separate. An installed native FlexDMD
plugin does not establish that legacy FlexDMD COM registration works. ROM ZIP
contents, table requirements, missing fonts, and custom display wiring are not
automatically audited. Global plugin disable flags are shown; per-table INIs
may intentionally override them.

INI detection considers portable settings and both versioned
`%APPDATA%\VPinballX\10.8` and older settings locations. Only the latest VPX run
whose **application directory matches the selected executable** confirms an
active INI. An old log for a different install cannot authorize an INI repair.

**Save report** exports the current checks, selected-table test result, and
manual verification checkboxes as JSON. Reports contain local installation paths.
Unchecked manual items mean unverified, not necessarily broken. **Open
configuration** opens the existing JSON in Notepad for advanced troubleshooting.
Malformed configuration is never silently replaced by the setup window.

## Test a table

Close the launcher and other VPX sessions before testing, so the headset is
available. Browse to an existing `.vpx` file and start the test. It uses the
proposed locations and respects that table's existing executable/INI/working-
directory profile. Applying setup is separate; a helper must be installed
before it can be tested.

Fresh lines for the selected table in the default versioned VPX log can confirm
initialization or report a script error. Other log locations may remain
unconfirmed. Initialization does not prove the ROM finished booting, the DMD
uses the correct artwork, or the game can start. Verify controls, audio, scoring,
DMD artwork and VR view yourself, then record those checks. A real table test
can update its normal credits, settings, and NVRAM just like ordinary play.

**Close test table** asks only the test process and its descendants to close
normally; it does not forcibly kill them. A blocking VPX dialog may need manual
attention. Closing setup itself leaves a running table independent.

## Review, apply and undo

The Review page lists the exact proposed changes. Apply is explicit and requires
the launcher and VPX to be closed. No downloads or repairs run on opening setup.

The optional **always-on VR** repair changes only `[PlayerVR] AskToTurnOn` in a
log-confirmed active INI. It is unchecked by default. It preserves INI encoding,
line endings, unrelated settings and input mappings. It will not create or guess
an active settings file when the runtime evidence is missing.

Original JSON/INI files and a change summary are saved under
`SetupBackups/<timestamp>` beside the launcher. Config replacement is atomic.
If a later step fails, original config/INI and existing helper files are
restored. Helper rebuilds additionally keep their own timestamped file backups.
If the config changes externally while the window is open, apply stops instead
of overwriting those edits; reopen setup to refresh it.

To undo, close the launcher and copy the original JSON/INI from the backup to
the paths listed in the change summary. If replacing a helper, restore its
matching executable and target sidecar together. Restart the launcher afterward.
Location setup does not rewrite table scripts, ROMs, artwork, per-table overrides,
play history or controller firmware. Existing table-local VPX settings remain intact.

## Backup and restore

The fifth page exports a ZIP containing saved `launcher-config.json` and, when
present, the player's `state.json`. This includes favorites, play counts, recent
history, browsing selection, table launch results, profiles and assignments.
Unsaved edits on other setup pages are not exported. Close the launcher and VPX
first so the files represent a settled state.

Choose a backup, select settings and/or history, then **Review restore**. By
default, restoring preferences keeps this PC's executable, tables, media and
controller launch paths, profiles and table assignments. Uncheck that option to
restore the complete configuration, including profiles, when moving to another
PC; review all paths before launching. Restoring history replaces it, rather
than merging it. Missing history is reported before changes can be applied.

**Restore reviewed backup** shows the proposed changes for confirmation, saves
the current files under `SetupBackups/restore-<timestamp>`, then restores the
selected files. Setup closes afterward so reopening reloads the restored values.
If a destination changes after review, restore stops. A failed restore rolls
back files already written. To undo, close the launcher and copy the saved
originals back to the paths in `restore-destinations.json`; an originally absent
file can be removed instead. Restart the launcher afterward.

The archive has a versioned manifest and SHA-256 integrity checks. Unexpected,
duplicate, oversized or damaged entries are rejected without extracting files.
Only launcher JSON files are restored. Tables, ROMs, artwork, VPX/table INIs,
helper binaries/sidecars and external controller scripts are not included.
Backups contain personal play history and local paths; keep them out of the
public source repository.

## Table readiness in VR

Click the left stick to open table details. The panel shows the resolved launch
profile/program, file checks, detectable ROM/plugin requirements, last launch
time and result, alongside play count and last played date. New launch results
are recorded independently of successful-session counts. Existing history is
preserved; earlier attempts have no recorded result.

**Needs attention** identifies missing launch files or a reported launch problem.
**Not yet tested** means no startup confirmation has been recorded. **Startup
confirmed - gameplay unverified** means VPX logged initialization; it does not
prove that coins, controls, audio, DMD artwork or scoring work. A last-played
session is not labelled as verified gameplay.

A single literal `cGameName`, `GameName` or `Controller.GameName` assignment in
an adjacent `.vbs` can identify a ROM. Dynamic/ambiguous assignments and embedded
scripts remain unknown. ROM checks look for ZIP presence in common table-local
and VPX libraries plus absolute registry-configured libraries; custom table INI
locations and ZIP contents require verification.
Native plugin presence does not verify enablement, per-table INIs or legacy COM.

For tables whose requirements are known, an optional adjacent
`Table Name.vpx.readiness.json` can declare them without changing the table:

```json
{"rom":"afm_113b","plugins":["pinmame","serum","scoreview"]}
```

Supported native plugin names are `pinmame`, `serum`, `scoreview`, `flexdmd`,
`pup` and `b2s`. A manifest takes precedence over script detection. These
sidecars stay with tables and are not part of the launcher backup.

This first version focuses on connecting and diagnosing **existing Windows
installations**. It does not install every display system or automatically
reconfigure DMDs. Optional legacy emulator/GL installation tasks remain unchecked
options in the installer. Touch controls are a separate experimental add-on ZIP;
the standard installer includes neither its payload nor a bridge setup task.

## Development checks

```powershell
.\tools\test-setup.ps1
.\tools\test-setup-ui.ps1
.\tools\test-backup.ps1
.\tools\setup.ps1 -LauncherDirectory "$env:LOCALAPPDATA\Programs\VR Pinball Launcher" -ReportOnly
.\tools\setup.ps1 -LauncherDirectory "$env:LOCALAPPDATA\Programs\VR Pinball Launcher" -PreviewDirectory .\Logs\setup-preview
```

The core tests use generated temporary files, exercise config preservation,
rollback, helper compilation and Windows argument round-trips. Close the
launcher/VPX first. The UI test uses a guarded dummy executable that exits 42;
it does not launch VPX, change live configuration or mark startup successful.
Preview mode renders all five pages and closes without applying changes or
running tables. Unity EditMode tests and complete-player startup checks remain
part of the normal deployment workflow.
