# VR Pinball Launcher

A Steam VR launcher for Visual Pinball tables that provides an immersive VR menu interface for browsing and launching your pinball table collection.

## Features

- **VR Menu Interface**: Browse your table collection in VR
- **Automatic Table Detection**: Scans directories for .vpx files
- **Easy Table Launching**: Select and play tables with VPinballX_GL64.exe -play
- **Seamless Return**: Menu reappears when table exits
- **Configurable**: JSON configuration file for easy customization

## Requirements

- **Unity 6000.6.2f1** (the version in `ProjectSettings/ProjectVersion.txt`; only needed to build from source)
- **SteamVR** installed and running
- **Visual Pinball X** — the BGFX builds (`VPinballX_BGFX64.exe`) and the older GL builds (`VPinballX_GL64.exe`) both work
- **VR Headset** compatible with SteamVR

## Installation

### Option 1: Build from Source

1. Clone or download this repository
2. Open the project in Unity Hub
3. Install required packages:
   - Go to **Window > Package Manager**
   - Install **XR Plugin Management**
   - Install **OpenXR Plugin** or **SteamVR Plugin**
4. Configure XR settings:
   - Go to **Edit > Project Settings > XR Plugin Management**
   - Enable **OpenXR** or **SteamVR** for your platform
   - **IMPORTANT**: Set **SteamVR** as the default OpenXR runtime in SteamVR settings
   - **For Meta Quest**: Also enable the **Oculus/Meta** plugin in XR Plugin Management
5. Open the main scene: `Assets/Scenes/VRLauncher.unity`
6. Build the project: **File > Build Settings > Build**

### Option 2: Use the Installer (recommended)

1. Download **`VRPinballLauncher-Setup.exe`** from the releases page.
2. Run it. The wizard lets you choose the install location (per-user, no admin, or
   all-users in Program Files), creates Start Menu / Desktop shortcuts, and offers
   optional checkboxes to **set up VPinballX for VR** (downloads the VR build if you
   don't have it), **set up VPinMAME** (the ROM emulator most tables need — this
   registers a COM DLL, so it triggers one admin prompt on a per-user install), and
   the **controller bridge**.
3. Launch from the Desktop / Start Menu shortcut. Uninstall any time from
   **Settings → Apps** or the Start Menu.

When the VPinballX VR step runs, it also drops **"VR Pinball Tables"** and
**"VR Pinball ROMs"** shortcuts on your Desktop (pointing at the configured tables
folder and VPinMAME's ROM folder), so you can drag new `.vpx` tables and ROM `.zip`
files straight in.

The optional setup steps just run the bundled `install.ps1`; you can re-run it any
time from the install folder to reconfigure, and you can still edit
`launcher-config.json` by hand. See [In-Game Controls](#in-game-controls-while-a-table-is-running).

#### Building the installer (maintainers)

After building the Unity project to `Build/`, run `build-installer.ps1` (requires
[Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File build-installer.ps1 -Version 1.0.0
```

This compiles `installer/vr-pinball-launcher.iss` into
`dist/VRPinballLauncher-Setup.exe`, bundling the build, `install.ps1`, and the
`ControllerBridge` folder.

## Configuration

Edit `launcher-config.json` in the same folder as the executable:

```json
{
  "vpinballExecutable": "C:\\Visual Pinball\\VPinballX_GL64.exe",
  "tablesDirectory": "C:\\Visual Pinball\\Tables",
  "searchSubdirectories": true,
  "wheelDirectory": "C:\\Visual Pinball\\Media\\Wheel",
  "menuDistance": 2.0,
  "menuHeight": 1.5,
  "menuScale": 0.01,
  "showDebugConsole": false,
  "enableControllerBridge": false,
  "controllerBridgePath": "",
  "controllerBridgeArgs": "",
  "controllerBridgeWorkingDir": ""
}
```

### Configuration Options

- **vpinballExecutable**: Full path to VPinballX_GL64.exe
- **tablesDirectory**: Directory containing your .vpx table files
- **searchSubdirectories**: Whether to search subdirectories for tables
- **wheelDirectory**: Directory containing wheel images for tables (supports both absolute paths like `C:\\Visual Pinball\\Media\\Wheel` or relative paths like `Media\\Wheel`). Only the top level is scanned — `searchSubdirectories` applies to tables, not wheel art. Supported formats: PNG, JPG, JPEG. See [Table and Media Naming](#table-and-media-naming) for how images are matched to tables.
- **menuDistance**: Distance (in meters) to position menu from camera
- **menuHeight**: Height offset (in meters) for menu positioning
- **menuScale**: Scale factor for the menu UI
- **showDebugConsole**: Show on-screen VR controller/button debug overlays (useful for troubleshooting). Defaults to `false`.
- **enableControllerBridge**: Launch an external VR-controller input bridge when the launcher starts and stop it when the launcher exits. See [In-Game Controls](#in-game-controls-while-a-table-is-running). Defaults to `false`.
- **controllerBridgePath**: Full path to the bridge executable to launch (e.g. the AutoHotkey executable, or a compiled bridge). Leave empty to disable.
- **controllerBridgeArgs**: Command-line arguments for the bridge (e.g. the quoted full path to the AutoHotkey script).
- **controllerBridgeWorkingDir**: Working directory for the bridge process. Leave empty to use the executable's folder. For the AutoHotkey script, set this to the folder containing `auto_oculus_touch.dll` so the script can load it.

## Table and Media Naming

Wheel art is matched to tables by filename. The launcher is tolerant of the
decoration that table files usually carry, but naming tables to the community
standard gives the most reliable results and keeps other pinball frontends
happy at the same time.

### The standard

Name every table, and its media, as:

```
Title (Manufacturer Year).ext
```

For example:

```
Attack from Mars (Bally 1995).vpx
Attack from Mars (Bally 1995).ini            <- VPX per-table settings
Media\Wheel\Attack from Mars (Bally 1995).png
```

This is the same convention used by PinballX, PinUP Popper and PinballY, and
by the community wheel packs, so a table named this way picks up its art
automatically everywhere.

Rules:

- **Title** as it appears on the playfield, no leading article juggling.
- **Manufacturer** and four-digit **Year** in a single parenthesised group.
- **Drop author, version and VR-room decoration** — no `VPW v1.0.1`, no
  `g5k VR 1.3.1`, no leading `VR ROOM`.
- Use **spaces, not underscores**.

### Renaming an existing table

Three things share a table's basename, and all must be renamed together:

| What | Example |
|---|---|
| The table | `Attack from Mars (Bally 1995).vpx` |
| VPX per-table settings | `Attack from Mars (Bally 1995).ini` |
| VPX texture cache | `Tables\cache\Attack from Mars (Bally 1995)\` |

Renaming the `.vpx` alone orphans the other two. The cache folder is
regenerated automatically, but the `.ini` holds your per-table settings and is
not.

High scores are safe either way: VPX stores them in `Tables\user\VPReg.stg`
keyed by the table's internal script name, not by its filename.

The trade-off is that the version is no longer visible in the filename. If you
track which build of a table you have, record it somewhere else — the per-table
`.ini` is a convenient place.

### How matching actually works

`TableScanner.LoadWheelImages()` tries three tiers in order and stops at the
first hit, so a correctly named table matches on tier 1:

1. **exact** — basenames equal, ignoring case.
2. **normalized** — underscores become spaces, parentheses are dropped,
   whitespace is collapsed, and a leading `VR ROOM` is removed.
3. **title+year** — everything after the `(Manufacturer Year)` group is
   discarded. Where a name has no parentheses, the last four-digit year token
   is used as the cut point instead, so titles that *begin* with a year (such
   as `2001 (Gottlieb 1971)`) survive intact.

Where several images reduce to the same key, the least decorated filename
wins, so `Twilight Zone (Bally 1993).png` is preferred over
`Twilight Zone (Bally 1993) BW.png`.

Tiers 2 and 3 mean an unrenamed table still finds its art — a table called
`Star_Trek_The_Next_Generation_Williams_1993_VPW_Mod_v1.1.vpx` matches
`Star Trek The Next Generation (Williams 1993).png`. Renaming is a
convenience, not a requirement.

### When art doesn't appear

A table with no match falls back to a solid placeholder colour. Every miss is
logged with the keys that were tried, so check the player log:

```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\vr-launch\Player.log
```

Look for `Matched wheel image for ...` on success, or
`No wheel image for '<table>' (tried exact, normalized '...', title+year '...')`
on failure. Compare the reported keys against your actual filenames.

## Setup in Unity

### Scene Setup

1. Create a new scene or open `Assets/Scenes/VRLauncher.unity`
2. Add the **VRLauncherManager** prefab to the scene
3. Configure the Canvas UI:

#### Create Menu Canvas

1. **Create Canvas**:
   - Right-click in Hierarchy > UI > Canvas
   - Set **Render Mode** to **World Space**
   - Set **Event Camera** to Main Camera

2. **Create List Container**:
   - Right-click Canvas > Create Empty
   - Name it "ListContainer"
   - Add **Vertical Layout Group** component
   - Add **Content Size Fitter** component

3. **Create Table Item Prefab**:
   - Create UI > Button
   - Add Text child for table name
   - Save as prefab: `Assets/Prefabs/TableItem.prefab`

4. **Create Status Text**:
   - Create UI > Text
   - Position at top of canvas
   - Name it "StatusText"

5. **Configure VRMenuController**:
   - Select the VRLauncherManager object
   - Drag Canvas to **Menu Canvas** field
   - Drag ListContainer to **List Container** field
   - Drag TableItem prefab to **Table Item Prefab** field
   - Drag StatusText to **Status Text** field

### Script Components

The project includes these main scripts:

- **VRLauncherManager.cs**: Main manager (attach to root GameObject)
- **VRMenuController.cs**: Handles VR menu UI
- **TableScanner.cs**: Scans for .vpx files
- **TableLauncher.cs**: Launches Visual Pinball tables
- **LauncherConfig.cs**: Configuration management
- **UnityMainThreadDispatcher.cs**: Utility for thread-safe callbacks

## Usage

1. **Start SteamVR** if not already running
2. **Put on your VR headset**
3. **Launch the application**
4. The menu will appear in front of you showing available tables
5. **Point and click** on a table to launch it
6. The menu will hide while the table is running
7. When you exit the table, the menu reappears
8. Press **Escape** to quit (desktop mode only)

## Controls

### Menu Navigation (Carousel)

- **VR Controllers**:
  - **Left Trigger** — Previous table
  - **Right Trigger** — Next table
  - **A Button** (right controller) — Launch selected table
  - **Y Button** (left controller), held 2 seconds — Quit the launcher
- **Keyboard**:
  - Left/Right Shift or Left/Right Arrow — Browse tables
  - Enter/Space — Launch table
  - Esc — Quit application

**Y** is deliberately the same button that exits a table, so the rule is
uniform: *Y exits whatever you are in* — table back to the launcher, launcher
back to the desktop. It requires a hold rather than a tap so that a reflexive
press on returning from a table doesn't close the launcher outright. The
remaining time counts down in the menu while you hold, and releasing early
cancels.

Quitting is ignored while a table is running: there, Y belongs to VPX and
exits the table. The launcher hands its XR session to VPinballX during play
and cannot read the controllers at all, so the two uses can never overlap.

Menu input is read through the XR Input System (OpenXR), so it works reliably with Quest/Touch and other OpenXR controllers.

### In-Game Controls (While a Table is Running)

Visual Pinball's VR build reads keyboard and DirectInput, **not** VR motion controllers, and Unity has released its XR session to VPinballX while a table is running. So in-game controls are provided by an **external controller bridge** that reads the VR controllers and sends the keystrokes VPinballX expects. The launcher starts this bridge on startup and stops it on exit, controlled by the `enableControllerBridge` configuration options above.

A ready-to-use bridge is bundled in the [`ControllerBridge/`](ControllerBridge/) folder and is set up for you if you tick **"Set up the VR controller bridge"** in the installer. To set it up later (or outside the installer), run `install.ps1` from the install folder — it shows a menu where you can choose **VPinballX (VR)**, **Controller bridge**, or **Both**:

- **VPinballX (VR)** installs/VR-enables Visual Pinball X (downloading the latest VR-capable `VPinballX_GL` Windows build if you don't already have one), turns on VR in `VPinballX.ini`, and points `launcher-config.json` at the exe and your tables folder.
- **Controller bridge** installs AutoHotkey 1.1 if needed and wires the bridge into your config automatically (no vJoy, no kernel driver, no security changes).

See [`ControllerBridge/README.md`](ControllerBridge/README.md) for details and non-interactive flags.

The bundled bridge ([`auto_oculus_touch`](https://github.com/rajetic/auto_oculus_touch/) driving an AutoHotkey script) uses these mappings, which match VPinballX's default keys:

| Control | Action | Key sent |
|---|---|---|
| **Left Trigger** | Left flipper | Left Shift |
| **Right Trigger** | Right flipper | Right Shift |
| **X Button** (left) | Start game | `1` |
| **B Button** (right) | Insert coin | `5` |
| **Y Button** (left) | Exit table | `Esc` |
| **Right Thumbstick** (pull back) | Plunger | Enter |

**Notes**:
- The bridge only sends keys while the **VPinballX window is focused**. This is why the triggers act as carousel navigation in the menu and as flippers in-game — there's no conflict.
- Mappings assume VPinballX's default key bindings. If yours differ, check **VPX > Preferences > Configure Keys** and adjust the bridge script accordingly.
- The bridge cannot run inside Unity (Unity hands its XR session to VPinballX during play); it must be a separate process, which is why the launcher spawns/kills it.

## Troubleshooting

### No Tables Appear

- Check that `tablesDirectory` in config points to correct folder
- Verify .vpx files exist in that directory
- Check Unity console for error messages
- Ensure `searchSubdirectories` is true if tables are in subfolders

### Table Won't Launch

- Verify `vpinballExecutable` path is correct
- Make sure VPinballX_GL64.exe exists at that location
- Check that the .vpx file is not corrupted
- Look for error messages in Unity console

### VR Not Working

- Ensure SteamVR is running before launching
- **Verify SteamVR is set as the default OpenXR runtime**:
  - Open SteamVR settings
  - Go to **OpenXR** section
  - Click **Set SteamVR as OpenXR Runtime**
- Check Project Settings > XR Plugin Management
- Verify OpenXR or SteamVR plugin is enabled
- **For Meta Quest users**: Enable both OpenXR and Oculus/Meta plugins
- Test that your headset works in other SteamVR apps

### Menu Not Visible

- Check that Canvas is set to World Space render mode
- Verify menuDistance and menuHeight values in config
- Try adjusting menuScale (default 0.01)
- Make sure camera has proper tracking

### Menu Doesn't Return After Exiting Table

- Check Unity console for process exit errors
- Verify the table process actually exited
- Try relaunching the application

### Controller Input Not Working in VPinballX

**Understanding the Input System**:
- Menu navigation reads the controllers directly via the XR Input System (OpenXR)
- In-game controls are provided by the external controller bridge (see [In-Game Controls](#in-game-controls-while-a-table-is-running)), which simulates keyboard input
- VR controllers don't appear in Windows joy.cpl and VPinballX can't read them as joysticks (this is normal — hence the bridge)

**Troubleshooting Steps**:
- Confirm the bridge is enabled and configured (`enableControllerBridge`, `controllerBridgePath`, `controllerBridgeArgs`, `controllerBridgeWorkingDir`) and that it actually launched with the app
- The bridge only sends keys while the **VPinballX window is focused** — make sure no other window (terminal, launcher menu, etc.) has stolen focus
- Verify your VPinballX key bindings match the bridge mappings (Shift for flippers, `1` = start, `5` = coin, Enter = plunger) under **VPX > Preferences > Configure Keys**
- Some tables require inserting a coin (B) before Start (X) does anything
- Confirm the VR runtime is running (e.g. Quest Link/Air Link) so the bridge can read the controllers
- Temporarily set `showDebugConsole` to `true` to see the `ControllerBridge: started …` log line and confirm the bridge launched

## Development

### Project Structure

```
vr-launch/
├── Assets/
│   ├── Scenes/
│   │   └── VRLauncher.unity
│   ├── Scripts/
│   │   ├── VRLauncherManager.cs
│   │   ├── VRMenuController.cs
│   │   ├── TableScanner.cs
│   │   ├── TableLauncher.cs
│   │   ├── LauncherConfig.cs
│   │   └── UnityMainThreadDispatcher.cs
│   └── Prefabs/
│       ├── VRLauncherManager.prefab
│       └── TableItem.prefab
└── launcher-config.json
```

### Building

`build.ps1` builds the player, compiles the installer and optionally deploys,
in one command:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1.0
```

| Flag | Effect |
|---|---|
| `-SkipInstaller` | Build the player only; skip Inno Setup |
| `-Deploy` | Copy the player over an existing install, preserving `launcher-config.json`, `ControllerBridge\` and `Media\` |
| `-UnityExe <path>` | Use a specific editor instead of the one in `ProjectVersion.txt` |

Output lands in `Build\`, the installer in `dist\`, and the Unity log in
`Logs\unity-build.log`. The log is deliberately kept out of `Build\`, because
the installer ships everything under that folder.

Notes on the build environment, all handled by the script but worth knowing:

- `-nographics` is **not** used. It requires the `com.unity.editor.headless`
  entitlement, and a Personal licence exits with code 198.
- Unity Hub is started if it isn't running: a Personal licence is validated by
  the Hub's licensing client, and batchmode otherwise fails with
  `No valid Unity Editor license found`.
- `Unity.exe` relaunches itself, so its exit code is meaningless for
  automation. The script waits for the real editor process and verifies the
  log and the output file instead.

To build by hand instead: File > Build Profiles, Windows platform, Intel
64-bit, then Build — and copy `launcher-config.json` into the output folder.

### Extending

To add new features:

- **Custom table sorting**: Modify `TableScanner.cs`
- **Table metadata**: Extend `TableInfo` class
- **Additional launch parameters**: Modify `TableLauncher.cs`
- **Better UI**: Enhance the Canvas prefab
- **Controller input**: Add input handling in `VRMenuController.cs`

## Credits

Created for the Visual Pinball VR community.

## License

MIT License - Feel free to use and modify for your own purposes.

## Support

For issues and feature requests, please use the GitHub issues page.

## Tips

- **Performance**: If menu lags, reduce number of visible tables or optimize prefab
- **Positioning**: Adjust menuDistance/Height in config for comfort
- **Large Collections**: Enable subdirectory search and organize tables in folders
- **Quick Access**: Create shortcuts to favorite tables by organizing in subfolders
