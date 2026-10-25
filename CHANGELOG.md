# Changelog

## 1.2.0 — 2026-10-24

First release from the clean single-commit repository. Earlier development,
reviews and validation records are retained in the
[development archive](https://github.com/dayelostraco/vr-pinball-launcher-development-archive).

- Separate Quest Touch controls into an explicitly experimental add-on ZIP.
  Standard installs contain no Touch bridge scripts/DLLs; upgrades preserve
  existing add-ons and mappings. Retain upstream credits and license notices.
- Check native image mitigation declarations and section bounds, and record
  the specific Unity advisory disposition and remaining add-on provenance limits.
- Guide first-time setup users to select VPX and tables when paths are blank,
  instead of exposing a PowerShell parameter error on the review page.
- Validate candidate installers on disposable hosted Windows accounts, including
  defaults, shortcuts, setup rendering, upgrade preservation and uninstall.
- Measure the complete background-input helper and legacy installer against
  per-file coverage gates; add deterministic focus and isolated component tests.
- Verify pinned native bridge binaries and AMD64 headers in CI and recheck
  integrity before packaging; restrict DLL loading to explicit Windows directories.
- Exercise bridge hysteresis and owned-key cleanup without sending OS input.
- Recheck VPX foreground ownership before each bridge key-down, including actions
  after an exit tap, and release tap keys even if waiting is interrupted.
- Report failed VPinMAME registration accurately and select one valid ROM library
  for desktop shortcuts when the registry contains several locations.

- Suppress menu shortcuts while desktop setup is open; require held controls to
  return to neutral before menu input resumes.
- Release synthetic keys when VPX loses foreground ownership and clear temporary
  topmost status after focus attempts. Recognize GL and BGFX player windows in
  the optional controller bridge.
- Resolve configured bridge/media paths relative to the launcher/configuration
  directory; validate AutoHotkey dependencies without rejecting compiled bridges.
- Save launcher configuration atomically and reject invalid backup history
  records before restore.
- Verify optional VPX, VPinMAME and AutoHotkey downloads against reviewed,
  versioned SHA-256 digests before installation.
- Add regression tests, per-file line/branch coverage gates for portable C# and
  Python tools, dependency/static security checks, and required PR documentation.
- Dispose generated room/fader materials when their components are destroyed.
- Release preview textures and per-cabinet materials in the editor, and clear
  failed image pin reservations so later loads remain eligible for eviction.
- Validate process ancestry and creation times before treating a window as part
  of VPX or stopping its descendants during emergency recovery.
- Preserve launch ownership if window-focus scheduling fails, and report XR
  restart failures while still completing the return-to-menu flow.
- Cancel an unfinished XR restart before handing the runtime to another table;
  prevent an earlier window-focus retry from targeting the next table.
- Isolate artwork subscriber failures so other waiting cabinet views still update.
- Quote remote-development arguments safely and refuse detached or mismatched
  branches before committing changes on a Windows development host.
- Enforce per-class Unity line coverage and per-file PowerShell line coverage;
  retain explicit limitations for unsupported branch and native measurements.
- Keep setup reachable when a configured catalog path is invalid, reject incomplete
  deployment payloads before changing installed files, and inventory release binaries.
- Require neutral controls when the optional Touch bridge regains focus and
  release only its owned keys on shutdown; validate its syntax and input decisions.
- Log background-input helper startup failures and return a controlled error code;
  verify its argument/environment forwarding with disposable native fixtures.
- Preserve existing VPinMAME registration and configured ROM/sample libraries in
  legacy component setup; resolve helper paths to the actual VPX installation.

## 1.1.0 — 2026-10-24

First packaged release of this maintained fork. Includes work developed before
this release; earlier fork commits were distributed as local builds.

- Arcade-room selection with favorites, recent sessions, play counts, remembered
  browsing, letter jumps, Surprise Me and grouped control hints.
- Table details with launch-profile/file checks, detectable requirements and
  persisted launch results. Initialization remains distinct from gameplay checks.
- Desktop setup from F2 or Start menu: actual VPX/helper paths, health report,
  table tests, reviewed changes, safety backups and rollback.
- ZIP export/restore for settings and history, with integrity checks, destination
  review and an option to retain this PC's paths/profiles.
- Launch recovery, process-tree rescue and the optional background-input/focus
  helper. Existing installed configuration is preserved.
- Matching setup/runtime relative-path and ROM-library rules, checked against
  shared installation-contract cases. Unknown helpers remain unverified.
- Per-user installer, current OpenXR quick start, contributor/architecture/test
  guides, CI, portable runtime harness, license and dependency notices.

Known limits: Windows x64 release; no launcher port for Ally/Linux; embedded or
dynamic table-script requirements are not automatically scanned. Other headset,
runtime and VPX combinations need physical validation. Installer is unsigned.

## Earlier development

Original launcher by keithbphillips, starting in 2025. See Git history for the
original project and subsequent fork development.
