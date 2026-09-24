# VR Arcade Room: Design

Date: 2026-09-24
Branch: `feature/vr-arcade-room` (fork `dayelostraco/vr-pinball-launcher`, based on `feature/app-icon`, which includes the PR #2 work)
Status: approved in brainstorming, awaiting spec review

## Goal

Replace the launcher's flat, one-wheel-at-a-time panel with a VR arcade room that makes choosing a table feel like standing in front of real machines, in the spirit of ES-DE on the handheld. The launch and return flow into VPinballX BGFX 10.8.1 must keep working exactly as it does today.

Scope is the user's own setup (fork only): Windows PC, SteamVR/OpenXR, `D:\Visual Pinball\VPinballX_BGFX64.exe`, about 42 tables named `Title (Manufacturer Year).vpx`. Upstream compatibility is not a goal.

### Success criteria

1. In the headset, tables appear as cabinets on an arc with backglass, playfield (video on the centered one) and wheel marquee.
2. Every table shows a finished-looking cabinet, even with missing media.
3. Favorites and recently played views work and persist across launcher restarts and reinstalls.
4. Launching fades out, VPX starts in VR, and on exit the room fades back in on the same table.
5. Browsing holds the headset frame rate with no visible hitch when scrolling quickly.

### Out of scope

- A walkable room or camera movement (rejected for comfort).
- Imported photoreal cabinet models (possible later by replacing one prefab).
- Backglass videos, DMD media, audio previews, search, sort by manufacturer.
- Upstreaming to `keithbphillips/vr-pinball-launcher`.

## 1. Room and controls

**Room.** A dark arcade room: floor, back wall, soft spotlights. The centered cabinet gets a brighter key light; side cabinets are dimmer. The room is anchored to the head position at startup and re-centered each time the launcher returns from a table, so it works seated or standing.

**Arc.** 7 cabinet slots on an arc of radius about 2.2 m, spaced about 22 degrees apart, center slot straight ahead. Cabinets are life size (about 0.7 m wide, backbox top about 1.9 m). The playfield is tilted about 35 degrees toward the viewer, much steeper than a real machine, so its video is readable from the front. Browsing rotates the arc one slot in about 0.25 s with ease in/out. Holding the thumbstick repeats with acceleration. The list wraps around.

**Info plate** under the centered cabinet: title; `Manufacturer · Year`; a star if favorite; "Last played <relative time>" if played; the current view name and position (for example `All · 12 / 42`).

**Controls.**

| Input | Action |
|---|---|
| Left / right trigger, thumbstick left / right | Previous / next table |
| A | Launch |
| B | Toggle favorite |
| X | Cycle view: All, Favorites, Recent |
| Hold Y 2 s | Quit (with countdown on the info plate) |
| Keyboard Left Shift / Right Shift / Enter / Space / Esc | Unchanged from today |

When the current view is empty (for example no favorites yet), the room shows one placeholder cabinet reading "No favorites yet" or "Nothing played yet", and X still cycles.

**Launch and return.** On A: the centered cabinet's lights pulse, the view fades to black with "Loading <Title>…", then XR stops and VPX starts exactly as today. When VPX exits: XR restarts, the room re-centers and fades in on the same table, and the play is recorded (so the table is first in Recent).

## 2. Media pipeline and table data

### `tools/fetch-media.py`

A Windows port of the Ally's `fetch-media.py`, run with `uv run tools/fetch-media.py`.

- Reads `tablesDirectory` from `launcher-config.json` (path given with `--config`, default is the installed launcher's config).
- Matches each table to a VPS id using `vpsdb.json` from the Virtual Pinball Spreadsheet, with `tools/media-overrides.json` for names that do not match (`{"Table Name (Manufacturer Year)": "<VPS id>"}`).
- Downloads from VPinMediaDB into `<launcher>\Media\Tables\<Table Name>\`:

  | VPinMediaDB path | Saved as |
  |---|---|
  | `wheel.png` | `wheel.png` |
  | `1k/table.png` | `table.png` |
  | `1k/bg.png` | `bg.png` |
  | `1k/table.mp4` | `table.mp4` |

- Never overwrites an existing file. `--force "<Table Name>"` re-downloads one table.
- Continues past per-file failures, then prints a per-table summary of fetched, already present and missing media, plus tables with no VPS match.
- Fetches media only. Never downloads ROMs.

### Media resolution in the launcher

For each table and each media kind, the launcher checks `Media\Tables\<Table Name>\` first. For the wheel only, it falls back to the existing `wheelDirectory` pack using today's exact, normalized and title-plus-year matching (moved unchanged into `TableCatalog`).

### Fallbacks

| Missing | Shown instead |
|---|---|
| `table.mp4` | `table.png` |
| `table.png` | Dark playfield with the wheel as a centered decal |
| `bg.png` | Wheel, enlarged on a dark backbox |
| Wheel | Title text on the marquee |

### Video

Unity `VideoPlayer` rendering to a `RenderTexture`, active only on the centered cabinet, looping and muted. A decode or prepare error falls back to `table.png` and logs the table and error.

### Table metadata and state

- Manufacturer and year are parsed from the `(Manufacturer Year)` group in the file name. Names without one show the title only.
- `state.json` in `Application.persistentDataPath` holds favorites and play history. Keys are table paths relative to `tablesDirectory`. Renaming a table drops its history.
- Shape: `{"version": 1, "favorites": ["<rel path>", ...], "plays": {"<rel path>": {"last": "<ISO 8601 UTC>", "count": <int>}}}`.
- Recent lists only played tables, newest first.

## 3. Code structure

`TableCarousel.cs` (526 lines, mixing input, display and launch) is replaced.

### Plain C# (no MonoBehaviour, EditMode-testable)

- **`TableCatalog`** (replaces `TableScanner`): scans `tablesDirectory` (respecting `searchSubdirectories`), builds `TableEntry { RelativePath, FullPath, Title, Manufacturer, Year, Media { Wheel, Playfield, Backglass, Video } }`, applying the resolution order above. File system access goes through a small interface so tests can use an in-memory tree.
- **`LauncherState`**: load, toggle favorite, record play, save. Corrupt JSON is renamed to `state.json.bad` and replaced with an empty state.
- **`TableListView`**: the current view's ordered list, selected index, wrap-around next and previous, and view cycling that keeps the same table selected when it exists in the new view.

### MonoBehaviours

- **`LauncherInput`**: reads keyboard and both XR controllers and raises `Previous`, `Next`, `Launch`, `ToggleFavorite`, `CycleView` and quit-hold progress. Owns edge detection, trigger hysteresis (0.7 press / 0.4 release, as today) and stick repeat. Disabled while a table is running. Existing `VRControllerInput`, `VRJoystickMapper` and `VRMenuController` are folded in or deleted where they overlap; anything the controller bridge still needs is kept.
- **`ArcRoom`**: builds the room, owns a pool of 7 `CabinetView`s, animates rotation, assigns entries to slots, and re-centers on the head.
- **`CabinetView`** (prefab): cabinet body, legs, backbox, tilted playfield panel, marquee, built from primitives with runtime textures. `SetEntry(entry)`, `SetFocused(bool)` (key light, video on or off), `Pulse()`.
- **`MediaCache`**: async texture loading, keeping the most recent 15 tables' textures (LRU) and destroying evicted textures.
- **`ScreenFader`**: head-locked fade quad with a status line.
- **`TableLauncher`** and **`ControllerBridge`**: unchanged except that launch is triggered after the fade-out, and `OnTableExited` drives fade-in and play recording.

## 4. Error handling

- Missing media: fallbacks in section 2.
- Corrupt `state.json`: renamed to `state.json.bad`, start fresh, log it.
- Video failure: still image, log it.
- No tables found: one cabinet reading "No tables found" and the configured path.
- `fetch-media.py`: per-file failures logged, run continues, non-zero exit only if `vpsdb.json` cannot be loaded.

## 5. Testing

- **Unity EditMode tests** (Unity Test Framework): name parsing (including `Simpsons Pinball Party, The (Stern 2003)` and names without a year group), media resolution order and each fallback, state round-trip and corrupt-file recovery, view filtering, wrap-around and view cycling.
- **pytest** for `fetch-media.py`: VPS matching, overrides, no-overwrite and `--force`, using a local fake media source.
- **PC smoke test** (run over SSH): `build.ps1` succeeds; the built launcher starts, logs 42 tables with resolved media, and logs frame timing while browsing.
- **Headset check** (user): look, comfort, tilt angle, controls, and launch and return on several tables.
