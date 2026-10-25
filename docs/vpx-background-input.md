# VPX background input and startup focus

`tools/VpxBackgroundInput.cs` is an optional Windows helper for launching VPX.
It enables `SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` for the VPX child process,
so SDL joystick axes (including PinOne nudge and plunger input) can work when
the desktop window lacks focus. It also hands keyboard focus to the child's
play window during startup. PinOne keyboard buttons still need window focus;
the SDL hint only addresses joystick input.

When the launcher starts a helper, its own focus routine tracks the helper
process, which has no game window. This helper finds the visible `VPPlayer`
window or a window titled `Visual Pinball Player` belonging to its own VPX
child. After that window has existed for two seconds, it attempts foreground
activation and checks the result. It retries for up to 120 seconds, then stops.
After success it does not take focus back if you switch applications. Windows
can deny foreground activation; this is a startup handoff, not a guarantee.

## Build and install

Requires Windows and the .NET Framework 4 C# compiler (normally installed with
Windows). No Unity rebuild or firmware changes are required. Close VPX before
updating an installed helper. From the repository root in PowerShell:

```powershell
.\tools\install-vpx-background-input.ps1 -VpxExecutable 'D:\Visual Pinball\VPinballX_BGFX64.exe'
```

The script compiles a windowless executable into the VPX installation's `Tools`
folder and writes `VpxBackgroundInput.vpx-path.txt` beside it. The text file
contains the absolute path to the real VPX executable. Both files must stay
together. Use `-DestinationDirectory 'C:\Your\HelperFolder'` for another
location. Existing helper/config files receive timestamped backups.

Edit **only** `vpinballExecutable` in your installed `launcher-config.json`:

```json
"vpinballExecutable": "D:\\Visual Pinball\\Tools\\VpxBackgroundInput.exe"
```

The usual per-user launcher folder is
`%LOCALAPPDATA%\Programs\VR Pinball Launcher`. Preserve all other settings and
profiles; do not replace the installed JSON with the repository example.
Restart the launcher after changing its configuration.

The helper forwards all VPX arguments, inherits the launcher's working
directory (including a profile's `workingDirectory: "table"`), waits for VPX to
exit, and returns its exit code. The environment hint is process-local; it
does not change Windows environment variables or VPX input/VR settings.

Profiles with their own `executable` bypass the default helper. To use it for
another VPX install, install a separate copy with its own destination and path
file, then point that profile's `executable` at that copy. Existing shutdown
watchdog hosts are separate programs: do not replace or chain them without
accounting for their cleanup behavior.

## Verify and troubleshoot

The install script runs `--self-test`, which verifies child-process inheritance
of the SDL hint. It does **not** verify focus or physical controls. Launch a
table through the launcher and check `VpxBackgroundInput.log` beside the helper
for `Started VPX` and `Focused VPX player`. Then test keyboard buttons and
physical axes in VR. A timeout means the helper could not find/focus the player
in the startup window. Check the configured VPX path, table loading time,
foreground restrictions, and VPX runtime log.

The original installed version was tested with VPX 10.8.1 rev 5436: a desktop
Medieval Madness run confirmed foreground handoff, and the SDL background hint
previously resolved PinOne VR nudge input. Recheck behavior on other builds.

To undo, restore `vpinballExecutable` to the real VPX executable and restart the
launcher. Keep the helper files until any running table has exited.
