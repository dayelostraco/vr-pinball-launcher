# Configuration reference

Use F2 or the Start menu Setup shortcut for normal setup. `launcher-config.json`
is next to `vr-launch.exe`; relative paths and `%ENVIRONMENT_VARIABLES%` resolve
against that launcher directory. Absolute paths are recommended when inspecting
a configuration shared between machines. The repository example is a template.

This normalization also applies to the media downloader and controller executable/
working-directory fields. A leading `.ahk` script argument is resolved the same way;
other command-line arguments are passed through literally and are not path fields.

| Field | Meaning |
|---|---|
| `vpinballExecutable` | Default program to launch: native VPX or an intentional helper |
| `realVpinballExecutable` | Actual VPX target when the default program is a helper; keep it consistent with the helper's target sidecar |
| `tablesDirectory` | Root containing `.vpx` files |
| `searchSubdirectories` | Include nested tables; defaults to true |
| `wheelDirectory` | Flat wheel art directory; defaults to `Media\Wheel`; nested wheel directories are not scanned |
| `tableMediaDirectory` | Per-table artwork/previews; defaults to `Media\Tables` |
| `showDebugConsole` | Legacy setting retained for configuration compatibility; currently unused. Use `Player.log` for diagnostics. |
| `vpxProfiles`, `tableProfiles` | Alternate executables, INIs and per-table assignments, described below |
| `enableControllerBridge` | Start the separately installed experimental Touch add-on with the launcher; defaults to false. The standard installer does not include it. |
| `controllerBridgePath` | Bridge executable, such as AutoHotkey 1.1 |
| `controllerBridgeArgs` | Bridge arguments, normally its quoted AHK script path |
| `controllerBridgeWorkingDir` | Folder containing the script and native bridge dependencies |

Older `menuDistance`, `menuHeight` and `menuScale` values no longer control the
arcade room. They were removed from templates and runtime fields in 1.1.0;
setup preserves unrelated JSON properties when saving. Room geometry currently
lives in `ArcLayout`, `CabinetView` and `ArcRoom` source constants.

## Alternate VPX profiles

Example fields to add to the configuration object:

```json
{
  "vpxProfiles": [
    {
      "name": "Legacy",
      "executable": "D:\\VPX Legacy\\VPinballX_GL64.exe",
      "iniFile": "D:\\VPX Legacy\\legacy.ini",
      "workingDirectory": "table"
    }
  ],
  "tableProfiles": [
    {"table": "Example (Original 2020)", "profile": "Legacy"}
  ]
}
```

The table key is its filename without `.vpx`; matching ignores case. An
undefined profile falls back to the default program. An empty working directory
uses the executable's folder, `table` uses the table's folder, and any other
value specifies a directory. `iniFile` is optional and passed as `-Ini`.
Relative executable/INI/working paths use the launcher directory consistently
in setup tests and actual launches. Keep paths and profile names unambiguous.

Profiles can point to their own helpers. The default helper does not wrap
profiles automatically; see [helper configuration](vpx-background-input.md).
Preserve existing shutdown/cleanup hosts unless you understand their behavior.

## Persistence and restore

Favorites, session counts and launch results use relative table paths in
`state.json`. Renaming a table or moving it relative to the tables root changes
its history key; it also requires updating profile assignments and companion
files. Back up before reorganizing a collection.

The [setup guide](setup.md) describes backup contents, review, replacement versus
merge behavior, preserving this PC's paths and profiles, and undo procedures.
Installed configuration remains authoritative across upgrades.
