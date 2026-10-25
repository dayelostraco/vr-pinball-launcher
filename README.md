# VR Pinball Launcher

> **Thank you, [keithbphillips](https://github.com/keithbphillips)!**
> This project builds on his [original VR Pinball Launcher](https://github.com/keithbphillips/vr-pinball-launcher).
> His work made this continuation possible, and we are grateful that he shared it
> with the pinball community. We retain his authorship and license attribution;
> the [original history](https://github.com/dayelostraco/vr-pinball-launcher-upstream-fork)
> remains available in the previous development fork.

Browse your VPX collection in an OpenXR arcade room, launch a table, and return
to the same selection when it closes. Includes favorites, recently played,
play counts, letter navigation, Surprise Me, table details, desktop setup and
reviewed backup/restore.

This is an independently maintained continuation, hosted in a standalone repository.
Please report issues and contributions here; keithbphillips is not responsible for
supporting these changes. The [previous fork](https://github.com/dayelostraco/vr-pinball-launcher-upstream-fork)
preserves earlier pull-request and CI history.

This repository starts from a reviewed source snapshot. The
[development archive](https://github.com/dayelostraco/vr-pinball-launcher-development-archive)
preserves the subsequent quality pass, reviews, test evidence and earlier releases.

![Launcher with a synthetic sample library; artwork is not bundled](https://github.com/dayelostraco/vr-pinball-launcher/releases/download/v1.2.0/launcher-demo.png)

[Watch the navigation demonstration](https://github.com/dayelostraco/vr-pinball-launcher/releases/download/v1.2.0/launcher-navigation.mp4).
It uses sample titles and original procedural demo artwork. Your artwork and
previews populate real cabinets; tables, ROMs and artwork are not bundled.

## Install and play

You need **Windows x64**, an existing **Visual Pinball X** installation with
working tables, and an **active OpenXR runtime compatible with your headset**.
BGFX and older GL VPX executables can be selected. SteamVR is one runtime option;
use the runtime that already works with your headset and VPX. Unity is needed
only to build the source. See [tested combinations and limits](docs/testing.md).

1. Download **VRPinballLauncher-Setup.exe** from the
   [latest release](https://github.com/dayelostraco/vr-pinball-launcher/releases/latest).
   Assets include `SHA256SUMS.txt` for download verification.
2. Run the installer. It installs for your Windows user under
   `%LOCALAPPDATA%\Programs\VR Pinball Launcher`, with Start menu shortcuts.
   Keep **Review an existing VPX installation** selected. Legacy VPX/VPinMAME
   tasks are optional and unchecked. Existing configuration is preserved.
3. In Setup, select the **actual VPX executable** and **tables folder**. Keep an
   existing helper unless you deliberately want to replace it. Review Health,
   then Review and apply. Installed builds do not need Unity project edits.
4. Activate your headset/runtime and open **VR Pinball Launcher**. Browse, press
   A to play, and exit VPX normally to return to the room.
5. Open **table details with left-stick click**, or **desktop setup with F2**.
   Close the launcher before applying settings, restoring backups or testing
   through setup so the headset and files are available.

If VPX cannot run a table independently, first resolve its VPX/ROM/DMD requirements.
The health report does not install every dependency. Full instructions:
[Setup](docs/setup.md). This release supports per-user installation;
Program Files/all-users deployment is not supported by the setup flow.

## Controls while browsing

| Action | Motion controller | Keyboard |
|---|---|---|
| Previous / next table | Left stick sideways; triggers also work | Left / right arrow or Shift |
| All / Favorites / Recent | Left stick up/down, or X to cycle | V |
| Previous / next initial letter | Right stick up/down | Use arrows to browse |
| Play selected table | A | Enter or Space |
| Toggle favorite | B | F |
| Surprise Me in current view | Right-stick click | R |
| Details and full control hints | Left-stick click | I |
| Quit launcher | Hold Y for two seconds | Escape |
| Desktop setup | Use PC keyboard | F2 |

Return vertical sticks to center between view/letter changes. The active view
is highlighted above the title. Play counts reflect counted sessions; readiness
separates initialization from manual controls, audio and scoring verification.

## Controls during a table

VPX owns input and its XR session during play. Keyboard and physical cabinet
controls use VPX's mappings. The optional
[background-input/focus helper](docs/vpx-background-input.md) helps physical SDL
controls and startup focus; it does not change firmware or button mappings.

For Meta/Oculus Touch motion controllers, a separate **experimental add-on**
[controller bridge](ControllerBridge/README.md) uses the Oculus runtime and
AutoHotkey 1.1. It is not a universal bridge for every OpenXR headset. Defaults:
triggers for flippers, X for start, B for coin, Y for exit, right stick for
plunger and left-stick click for VPX recenter. Physical PinOne buttons have
separate mappings; controller X and keyboard X are different inputs.

The standard installer does not include the bridge scripts or DLLs and does not
enable it. Community testers can use `VRPinballLauncher-TouchBridge-Experimental.zip`
with the [add-on instructions](ControllerBridge/README.md). Standard upgrades
preserve an existing bridge and its configuration. PinOne controls do not need it.

**Community testing help wanted:** the current Quest + Touch bridge still needs
end-to-end hardware validation through Quest Link / Air Link. If you already use
that connection, see the [bridge setup and controls](ControllerBridge/README.md)
and share your headset/runtime/VPX versions and results through
[an issue](https://github.com/dayelostraco/vr-pinball-launcher/issues/new/choose).
Check flippers, coin/start, plunger, recenter, exit and return to launcher browsing.
This is a request to validate existing code, not a claim of verified compatibility.
Virtual Desktop users do not need to switch connections to help: reports of their
current setup and contributions to a compatible input path are welcome for the
future work below.

**Backspace** is the emergency process-tree stop while the launcher is running.
Prefer the table's normal exit so it can save cleanly.

## Settings, backups and media

- [Setup, health, table tests and backup/restore](docs/setup.md)
- [Configuration fields and VPX profiles](docs/configuration.md)
- [Artwork naming and downloading](docs/media.md)
- [Recording missing previews](docs/recording-previews.md)

Settings live in `launcher-config.json` beside the application. Player state is
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\vr-launch\state.json`.
Setup's fifth page exports both as a ZIP and reviews restores with safety copies.
It can retain this PC's paths/profiles. Tables, ROMs, media and VPX INIs are separate.
Never replace a working installed configuration with the repository sample.

## Troubleshooting

| Symptom | First steps |
|---|---|
| No tables | F2 → Locations: check the tables folder, subfolder option and `.vpx` files. |
| Table fails to start | Open details for its result/profile; F2 → Health; test directly in VPX. Check the executable and profile INI. |
| Headset shows a static image | Verify the active OpenXR runtime/tracking in another app; inspect `Player.log`. |
| Controls do not respond | Check VPX focus and mappings. Check the bridge only if enabled. Physical and motion controls have different input paths. |
| Wrong or missing DMD | Check that table's ROM/plugin/Serum/PuP requirements in VPX. Plugin presence does not establish correct artwork/routing. |
| Room does not return | Check whether VPX/helper is still running. Use emergency rescue if necessary and retain the launch log. |

Launcher log: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\vr-launch\Player.log`.
Default monitored VPX log: `%APPDATA%\VPinballX\10.8\vpinball.log`.
Helper log: `VpxBackgroundInput.log` beside the helper.
[Full logs and compatibility limits](docs/testing.md).
Use the [bug-report form](https://github.com/dayelostraco/vr-pinball-launcher/issues/new/choose)
with versions and log excerpts; omit tables, ROMs and personal backup archives.

## Build or contribute

Start with [CONTRIBUTING](CONTRIBUTING.md) and [architecture](docs/architecture.md).
Use **Unity 6000.6.2f1**, a valid local license and Hub. Open the project to
restore pinned packages and retain its OpenXR settings:

```powershell
.\test.ps1
.\build.ps1 -SkipInstaller
```

To package, install Inno Setup 6 and run `build.ps1 -Version 1.2.0`.
Output: `Build/` and `dist/`; logs: `Logs/`. Maintainers back up and deploy the
complete player, then run [runtime checks](docs/testing.md).
[Release procedure](docs/releasing.md) · [Changelog](CHANGELOG.md).

See the [quality review](docs/quality-review.md) for section grades, measured
coverage, security scan results and remaining hardware-validation gaps.

## Help shape the launcher

Have an idea, found a bug, or spotted something that could be easier to use?
[Open an issue](https://github.com/dayelostraco/vr-pinball-launcher/issues/new/choose).
Feature requests, fixes, accessibility suggestions, documentation improvements,
and reports from different headsets and controllers are welcome. You do not need
to write code to contribute.

Describe what you want to accomplish and what gets in the way. For bugs, include
your launcher/VPX versions, headset/runtime, and steps to reproduce. Please keep
ROMs, table files, credentials, and personal backups out of public reports.
Developers can start with [CONTRIBUTING](CONTRIBUTING.md).

## Roadmap and future ideas

Current work focuses on input/focus reliability, setup consistency, security checks,
and stronger automated test coverage. Ideas for later consideration include:

- A clearer first-run walkthrough and more visual setup guidance.
- Easier media discovery and preview management.
- More flexible controller mappings and broader tested hardware compatibility.
- Quest + Touch controller support through Virtual Desktop (VDXR/SteamVR):
  investigate a compatible input path and validate table controls and launcher
  handoff. This is deferred; the current optional bridge uses the Meta/Oculus
  runtime through Quest Link / Air Link, not a generic OpenXR controller reader.
  Help designing, implementing and testing a Quest 3 + Virtual Desktop path is
  welcome; there is no requirement to adopt Link / Air Link for this future work.
- Better library diagnostics and tools for larger collections.
- Further selection-room polish and accessibility improvements.

These are discussion topics, not promised features or release dates. Check
[open issues](https://github.com/dayelostraco/vr-pinball-launcher/issues) for current
work, suggest an improvement, or explain which idea would help your setup most.

## License and credits

Original launcher by **keithbphillips**, maintained in this fork by **dayelostraco**
and contributors. Original source is [MIT licensed](LICENSE).
[Third-party components](THIRD-PARTY-NOTICES.md) retain their own notices and terms.
Community authors retain their rights; tables and artwork are not bundled.
