# Experimental Touch Controller Add-on

Optional in-game Touch controls, distributed separately from the standard launcher.
**Hardware compatibility is unverified; community testing help is wanted.**
This add-on uses Quest Link / Air Link, not Virtual Desktop's VDXR/SteamVR path.
Its inherited DLLs are unsigned and lack reproducible source-build attestations.
Separating this package does not resolve those limitations. PinOne users do not
need this add-on.

## Why this exists

Visual Pinball's VR build reads **keyboard / DirectInput**, not VR motion
controllers, and Unity hands its XR session over to VPinballX while a table is
running. So the controls can't live inside the launcher — they're provided by a
small **external bridge** that reads the Touch controllers and sends the
keystrokes VPinballX expects. The launcher starts this bridge on startup and
kills it on exit (see `enableControllerBridge` in `launcher-config.json`).

## Contents

| File | Purpose |
|------|---------|
| `vpx_vr_controls.ahk` | The control mapping (this project) |
| `auto_oculus_touch.ahk` / `auto_oculus_touch.dll` | Reads the Quest/Touch controllers via the Oculus runtime — by Kojack (rajetic), MIT (see `auto_oculus_touch_README.txt`) |
| `vJoyInterface.dll` | **Required.** `auto_oculus_touch.dll` is built with vJoy support, so it has a load-time dependency on this file even though we don't use vJoy. It must sit beside `auto_oculus_touch.dll` or the DLL won't load ("auto_oculus_touch.dll file is missing"). The vJoy *driver* itself is **not** required. |

Setup for both VPinballX (VR) **and** this controller bridge is handled by
**`install.ps1`**, which lives in the launcher root folder (next to `vr-launch.exe`),
one level up from here.

## Install

1. Install the standard launcher and close it. Back up any existing `ControllerBridge`
   folder before changing add-on files; do not overwrite customized mappings blindly.
2. Obtain `VRPinballLauncher-TouchBridge-Experimental.zip` from the same reviewed
   release, verify its published SHA-256, and extract it into the launcher install
   folder. It contains one `ControllerBridge` folder that must sit **next to
   `vr-launch.exe`**. Extraction does not enable it or replace `launcher-config.json`.
3. Connect Quest Link / Air Link and wake the controllers before starting the launcher.
4. Right-click **`install.ps1`** (in the launcher root folder) → **Run with PowerShell**
   (or `powershell -ExecutionPolicy Bypass -File install.ps1`).

`install.ps1` opens a menu:

| Option | What it does |
|---|---|
| **1. VPinballX (VR)** | Finds an existing BGFX/GL VPX installation or offers the pinned Windows build. Enables VR and preserves an existing configured helper and table folder. |
| **2. VPinMAME** | Installs/configures the ROM emulator when needed, preserving an existing registration and configured ROM/sample paths. ROM ZIPs are not supplied. |
| **3. Controller bridge** | Ensures **AutoHotkey 1.1** is present and writes the bridge settings into `launcher-config.json` so the launcher manages it automatically. |
| **4. Everything** | Runs all three component setup steps. Existing installations should select only the component they need. |

Choose **3** for the add-on. The standard installer has no bridge checkbox.
Without an extracted add-on, option 3 explains what to install; option 4 skips
the absent add-on. To disable it later, close the launcher and set
`enableControllerBridge` to `false` in its configuration. Remove the add-on folder
only after backing up mappings you want to retain. Standard launcher upgrades
preserve an existing add-on; removing it is a separate user action.

Optional downloads use reviewed versions and SHA-256 digests from
[`tools/download-lock.json`](https://github.com/dayelostraco/vr-pinball-launcher/blob/master/tools/download-lock.json). They do not automatically
track the latest upstream release. For ordinary path/configuration review, use
[desktop setup](https://github.com/dayelostraco/vr-pinball-launcher/blob/master/docs/setup.md) instead of reinstalling components.

No vJoy, no kernel driver, and no Windows security changes required.

### Non-interactive use

```powershell
# everything, no prompts where avoidable
powershell -ExecutionPolicy Bypass -File install.ps1 -Setup both

# only VPinballX VR, into an existing/target folder, with a specific tables dir
powershell -ExecutionPolicy Bypass -File install.ps1 -Setup vpx -VpxDir "C:\Visual Pinball" -TablesDir "C:\Visual Pinball\Tablesvr"

# only the controller bridge
powershell -ExecutionPolicy Bypass -File install.ps1 -Setup bridge
```

Use the VR runtime that works with your VPX installation. This bridge additionally
requires the **Meta/Oculus runtime through Quest Link / Air Link**; it is not a
generic controller reader for every OpenXR runtime. The first time VPX starts in
VR it may still prompt 2D-vs-VR until a headset is detected.

## Controls (in-game)

The bridge only sends keys while the **VPinballX window is focused**, so the
triggers double as carousel navigation in the menu and flippers in-game.
After focus changes, release the mapped buttons/triggers and center the sticks
before using them again. This prevents held controls from becoming new presses
in the next table. The bridge releases its own held keys when focus leaves.

| Control | Action | Key (VPX default) |
|---|---|---|
| Left trigger | Left flipper | Left Shift |
| Right trigger | Right flipper | Right Shift |
| X (left) | Start game | `1` |
| B (right) | Insert coin | `5` |
| Y (left) | Exit table | `Esc` |
| Right thumbstick (pull back) | Plunger | Enter |
| Left-stick click | Recenter VR player | Numpad 5 |
| Left stick up / down | Move VR player up / down | Numpad 8 / Numpad 2 |

The bridge recognizes GL, BGFX, and VPinballX64 executable names and sends input
only to an identified player window, never its editor/DMD/backglass. Unknown
window classes/titles fail closed. Renamed executables require updating the
`VPXPlayers` group in the script. Validate each headset/runtime/VPX combination;
matching an executable name does not certify its input behavior.

Mappings assume VPinballX's default key bindings. If yours differ, check
**VPX → Preferences → Configure Keys** and edit the `Send` lines in
`vpx_vr_controls.ahk` (then restart the launcher so the bridge reloads).

## Requirements

- Oculus/Meta runtime running (Quest **Link / Air Link**) — the reader uses the
  Oculus SDK, so pure Virtual-Desktop / OpenXR-only streaming is not supported by
  this reader.
- AutoHotkey 1.1 (the installer handles this).

## Manual configuration

If you'd rather not run the installer, set these in `launcher-config.json`
(next to `vr-launch.exe`), adjusting paths to where you put this folder:

```json
"enableControllerBridge": true,
"controllerBridgePath": "C:\\Program Files\\AutoHotkey\\AutoHotkeyU64.exe",
"controllerBridgeArgs": "\"C:\\...\\ControllerBridge\\vpx_vr_controls.ahk\"",
"controllerBridgeWorkingDir": "C:\\...\\ControllerBridge"
```

Use this folder for `controllerBridgeWorkingDir`. Library resolution uses the
script's own directory, independently of the selected table's working directory.

## Dependency integrity and validation

The add-on DLLs are pinned in the repository's `tools/native-lock.json`, included
as `ControllerBridge/native-lock.json` in the ZIP. CI and add-on
packaging reject unreviewed replacements. The loader uses explicit Windows search
directories and does not fall back to the current table folder or PATH.

The bridge's parser and 69 input assertions run with a disposable AutoHotkey 1.1
interpreter; they record requested key events without loading native libraries or
sending keys. They cover neutral rearming, hysteresis and owned-key cleanup, but
do not certify headset polling or supply AHK line/branch coverage. DLLs remain
unsigned inherited components; see [third-party notices](https://github.com/dayelostraco/vr-pinball-launcher/blob/master/THIRD-PARTY-NOTICES.md).
The ZIP retains the launcher MIT license, upstream source headers and MIT texts
for AutoOculusTouch and vJoy under its `licenses` directory. Meta's SDK/runtime
retains its separate terms and is not installed by this ZIP.
