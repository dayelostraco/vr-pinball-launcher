# Native dependency review

Reviewed 24 October 2026. This records evidence and unresolved release decisions;
it is not a whole-product vulnerability clearance or a source-build attestation.

## Engine advisory disposition

The [CVE-2025-59489 record](https://cveawg.mitre.org/api/cve/CVE-2025-59489)
sets the default status to unaffected and lists affected Unity branches through
6000.3, with explicit fixed-version boundaries. The release uses Unity 6000.6.2f1,
outside those affected ranges. Disposition: **not affected according to the
published CVE record**. This replaces the earlier inference based only on version
ordering. Review the [vendor advisory](https://unity.com/security/sept-2025-01)
and [exact editor release](https://unity.com/releases/editor/whats-new/6000.6.2f1)
again whenever the engine changes. It does not dispose of unrelated advisories.

## Existing bridge binaries

The two files retain the exact identities in `tools/native-lock.json`. Neither
was replaced or executed during this review. Microsoft `dumpbin` 14.44.35229.0
reported PE32+ AMD64 images with high-entropy ASLR, NX compatibility, base
relocation directories and no writable executable sections. Neither declares
Control Flow Guard. Both lack Authenticode signatures.

`tools/check-native.py` now checks those minimum mitigation declarations and
section data bounds, in addition to hashes, architecture and exact inventory.
Malformed and weakened-image fixtures exercise failure paths. This is structural
validation using the [Microsoft PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format),
not a loader emulator, memory-safety proof or runtime exploit test.

| Binary | Static imports inspected | Remaining provenance limitation |
|---|---|---|
| auto_oculus_touch.dll | vJoyInterface, Windows kernel/user libraries, VC runtime and Universal CRT APIs | Inherited launcher binary; exact source/toolchain correspondence has not been established |
| vJoyInterface.dll | Windows SetupAPI, HID, kernel/user and Advapi32 libraries | Inherited upstream binary; no source-build attestation |

These are static imports, not a complete graph of runtime-loaded libraries.
The Oculus runtime is installed separately. A matching hash confirms the reviewed
bytes; it does not establish that those bytes are safe.

## Source-build feasibility

An isolated checkout of [AutoOculusTouch commit 2ff09c7](https://github.com/rajetic/auto_oculus_touch/tree/2ff09c79533f3b99cb742a013ba55c07f37c1642)
built locally with MSVC v143 (14.44.35207) and Windows SDK 10.0.26100.0, overriding
the upstream project's older toolset/SDK settings. The build emitted numeric
conversion and output-path warnings. Its DLL hash differs from the inherited
binary; it has not been packaged, deployed or tested on a headset.

The project links **prebuilt** `sdk/Lib/LibOVR.lib` and `vJoyInterface.lib`. Therefore
rebuilding this wrapper alone would not establish an entirely source-rebuilt
dependency chain. The pinned inputs have SHA-256 values:

- LibOVR.lib: `b7941cb6810954066b3f00004b04b27a1a20264ee7128d5adec483472ff68f14`
- vJoyInterface.lib: `59b3d529e7ec21be14ee048a153523f427651669f18a1dfcfd42347352954ac3`

Do not substitute the probe output merely to label the package source-built.
A replacement needs reviewed SDK provenance and terms, repeatable build inputs,
API/failure-path tests, mitigation checks and community hardware validation.

## Signing and release decision

No code-signing certificate was available in the current user's or local machine's
personal certificate stores during this review. A trusted signing service or
certificate requires maintainer identity provisioning; a self-signed certificate
would not establish public publisher trust. Continue identifying the installer
as unsigned until a genuine signing route is configured.

The maintainer chose a separate experimental add-on. The standard installer
excludes its scripts and both DLLs; the optional ZIP retains their source-related
files, license notices and hash lock. Build and installation checks enforce the
separation. Existing installations keep their user-owned bridge files and settings.
This removes the inherited bridge binaries from the standard release's payload;
it does not improve the add-on's grade or excuse missing evidence. Its source-build
and hardware-validation work remains open for community contributors.
