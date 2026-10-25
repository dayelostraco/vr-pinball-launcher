# Release procedure

Releases belong to `dayelostraco/vr-pinball-launcher`. Installer support links
point here; retain keithbphillips's original authorship and third-party notices.

1. Work from a clean reviewed commit. Update the changelog, build script defaults,
   Inno default version and `BuildScript` fallback together. The build command
   passes its explicit version into both the Unity player and installer.
2. Run all checks in [CONTRIBUTING](../CONTRIBUTING.md), including Unity EditMode
   and the desktop GUI smoke test. Run `tools/format-setup.ps1 -Check` if you
   changed setup scripts. Inspect license notices when updating dependencies.
   Run `python tools/check-native.py`; verify changed lock entries against their
   immutable upstream URLs before accepting new binary hashes. Add-on builds
   independently recheck those hashes and reject additional bridge DLLs. Standard
   installer builds reject stale Touch bridge files in the player output.
3. Run `build.ps1 -Version <version>`. Use the pinned Unity editor, a valid license
   and Inno Setup 6. Inspect Unity/installer logs, not only process exit codes.
4. Run `tools/test-runtime.ps1` against the built player, followed by
   `tools/test-installer.ps1`. The latter recompiles the same payload with a unique
   test AppId and group, installs in a temporary directory with optional tasks
   disabled, verifies fresh defaults/payload/notices and upgrade preservation,
   then uninstalls only the test instance. It leaves diagnostic artifacts. Do not
   silently install the real AppId to a temporary directory on a live PC:
   Inno's uninstall registration could affect the existing launcher. Also follow
   the actual installer wizard in a clean account/VM when available.
5. Back up the installed runtime and export launcher settings before
   `build.ps1 -Deploy -Version <version>`. It preserves installed JSON, Media and
   ControllerBridge; deploy the complete matching player. Confirm all three
   assemblies match `Build/vr-launch_Data/Managed`: `VRLauncher.Runtime.dll`,
   `VRLauncher.Arcade.dll` and `VRLauncher.Core.dll`.
6. Start the installed app, inspect `Player.log`, check the catalogue and setup
   shortcut/F2. Follow the [manual checklist](testing.md) and record unavailable
   hardware checks rather than implying broad headset certification.
7. Prepare a draft release at tag `v<version>` targeting the exact tested commit.
   Run `tools/write-release-inventory.ps1` to record the built binary hashes,
   signature status and resolved Unity packages in `dist/release-inventory.json`.
   Review vendor advisories separately; inventory is not a clean CVE result.
   Attach the installer, inventory, `SHA256SUMS.txt`, release validation notes, and optional
   screenshots/demo. Publish after assets and checks are complete.

The standard installer excludes the experimental Touch bridge. When publishing
that optional package, build it separately with
`tools/build-controller-bridge.ps1`. Review and attach
`dist/VRPinballLauncher-TouchBridge-Experimental.zip` with its own entry in
`SHA256SUMS.txt`; label it experimental in the release notes. The ZIP contains
one `ControllerBridge` directory, its instructions, MIT notices and native lock.
It neither installs nor enables itself. Keep inherited binary and unverified
hardware limitations visible. The standard `release-inventory.json` excludes
these two DLLs; their identity belongs to the add-on's native lock.

Example checksum generation:

```powershell
$file = 'dist\VRPinballLauncher-Setup.exe'
((Get-FileHash $file -Algorithm SHA256).Hash.ToLower() + '  VRPinballLauncher-Setup.exe') |
    Set-Content -Encoding ASCII dist\SHA256SUMS.txt
```

Users can compare `Get-FileHash .\VRPinballLauncher-Setup.exe -Algorithm SHA256`
with the release file. A checksum checks integrity; the current installer is
unsigned. Describe any signing changes in release notes.

## Demonstration assets

`tools/unity-batch.ps1 -Method VRLauncher.EditorTools.ReleasePreview.Render`
creates synthetic-library stills under `Logs/release-preview` without reading
live tables or history. An FFmpeg slideshow can join the five stills into a
short silent walkthrough. Review all images and the encoded video before
uploading. Do not commit generated capture frames or videos. Public screenshots
must not imply table/ROM/artwork redistribution rights.

## Automated checks

For a real-AppId clean-account check, upload the candidate installer to an
unpublished `validation-*` draft release and dispatch **Installer validation
(trusted candidate)** with that tag and its independently recorded SHA-256.
Only the repository owner can run it. It verifies defaults, per-user registration,
shortcuts, exclusion of experimental bridge files, rendered setup pages and upgrade preservation on a fresh
GitHub-hosted Windows account, then uninstalls the test package. Review its setup
page artifacts. It does not launch a real table or certify headset compatibility.
The script refuses to run on a live PC or an account with launcher state already
present. This is automated silent-installer evidence, not a manual wizard walkthrough.
Draft retrieval runs separately with the push access GitHub requires for unpublished
releases. The installation job uses a fresh runner, read-only repository permissions
and no persisted checkout credentials; both jobs verify the supplied package digest.

Hosted `Checks` CI requires no Unity license or headset. The manually dispatched
Unity workflow needs a trusted Windows runner with the pinned editor, Hub,
license and `unity` label. Do not register a live personal PC to execute arbitrary
pull-request code. Hosted green checks do not replace player or physical tests.
