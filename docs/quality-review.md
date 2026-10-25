# Quality review — 24 October 2026

This review covers the maintained source, documentation, build and setup paths
after the reliability and testing pass. Grades are maintainer judgments, not an
independent audit or a hardware certification. **Source maintainability and
documentation are A− overall. Standard-package release assurance is A− for the
documented tested configuration; the separate experimental Touch add-on remains B+.**
Its inherited native provenance and hardware validation are still incomplete;
separating it changes the standard payload, not the add-on's grade.
Clean-account silent installation passed on hosted Windows Server 2025; a manual
Windows 11 first-user walkthrough remains unverified. The user confirmed the deployed PC's
gameplay, PinOne recentering and repeated launcher/table handoff.

An A means clear responsibilities, useful failure handling, meaningful regression
evidence and usable documentation. A− allows bounded platform or measurement
limitations that are stated explicitly. B+ means an important confidence gap
remains. Coverage is assessed separately: a grade does not waive the 80% line
and branch baseline or turn an unsupported metric into a pass.

## Review by section

| Section | Grade | Evidence and remaining improvement |
|---|---|---|
| Core catalog, names, filters, profiles and paths | A | Portable production code, shared installation contracts, boundary and malformed-input tests. Keep path rules synchronized with setup. |
| History, favorites, play counts and launch results | A | Atomic writes, legacy JSON compatibility, corruption handling, separate failure history and backup validation. Add migration fixtures whenever the schema changes. |
| Launch, process ownership and recovery | A− | Process identity includes creation time; emergency stop checks ancestry; deferred coroutine failures and cleanup are tested. Copied-player tests verify exit, retry and child cleanup. The user confirmed real XR handoff and repeated launches on the existing PC configuration. |
| Menu and native input | A− | Neutral rearming, setup suppression, foreground ownership and key-release tests. Native INPUT layout is checked. Physical mappings and focus behavior remain hardware-dependent. |
| Arcade room, cabinets, details and media cache | A− | Presentation assembly has 91.7% line coverage, async cache failure tests and explicit texture/material ownership. Keep accessibility and different headset readability on the roadmap. |
| Configuration and bootstrap | A− | Missing/invalid paths leave setup accessible; startup scheduling, cancellation and input-action cleanup have regression coverage. Test a new user account before declaring first-run certification. |
| Desktop setup and health | A− | Separate path, transaction and UI responsibilities; fixtures cover profile selection, path review, failures and logs. Health findings deliberately do not promise playable tables. |
| Backup and restore | A | Archive validation, size/path/integrity checks, reviewed restore and rollback fixtures. Backups remain private executable configuration, not authenticated packages. |
| Optional bridge and background-input helper | A− maintained code and tests; native hardware unverified | The complete helper meets line/branch gates (90.55%/87.50%). Bridge parsing and 69 assertions exercise neutral rearming, hysteresis and owned-key cleanup. AHK has no instrumented coverage; headset polling and native focus still need physical verification. |
| Legacy component installer | A− | The complete production script is instrumented: 91.4% lines. Isolated tests cover configuration/INI preservation, download results, registration failure, library paths and absent-add-on dispatch. Unexecuted menu lines remain in the denominator; Pester does not report branches. |
| Python media/preview tools and remote helper | A− | Per-file line/branch gates; mocked network and SSH tests; quoted remote commands. Actual recording requires VPX/FFmpeg; Bash has behavioral tests but no coverage instrumentation. |
| Editor utilities, scene/assets and packaging | A− | Pinned Unity packages, complete named-assembly deployment, source checks, real player build and isolated installer tests. Editor generation helpers have build/preview evidence, not unit coverage. Generated output stays out of source. |
| CI, dependency controls and security policy | A− standard-package controls; B+ experimental add-on assurance | Pinned actions/downloads, restricted permissions, source/dependency scans, mitigation gates and separate package inventories. Standard installation tests reject Touch bridge payloads. Scanner results exclude comprehensive Unity/native advisory coverage; see below. |
| Comments and public contracts | A− | Comments explain ownership, cancellation, path bases, quoting, trust boundaries and platform workarounds. No comment-count quota; future changes must keep these explanations accurate. |
| End-user documentation | A− | Prominent upstream thanks, install/setup steps, controls, troubleshooting, backup and compatibility limits. A first-time user's clean-account walkthrough remains the strongest next check. |
| Contributor documentation and contribution process | A− | Build prerequisites, architecture map, test commands, PR template, security policy and merge requirements. Default-branch protection requires PRs, four CI checks and resolved conversations, including for admins. |

## Measured verification

These are separate, overlapping test scopes; do not sum their counts as unique tests.

| Scope | Result | Line coverage | Branch coverage |
|---|---|---|---|
| Unity EditMode, three production assemblies | 294 passed | 91.9% overall; every measured class ≥80% | Unsupported by this backend |
| .NET Core plus standalone Windows helper, actual production source | 203 passed | 98.66% | 94.13% |
| Python tools | 85 passed | 97.25% | 93.42% |
| PowerShell, fourteen production scripts | 55 Pester tests passed locally | Each measured file ≥80%; legacy installer 91.4%, standard packager 96.3%, add-on packager 95.7% | Unsupported by Pester |
| Optional AutoHotkey bridge | Parse plus 69 input assertions passed | Unmeasured | Unmeasured |
| Standalone VPX input helper (subset of .NET above) | 21 tests plus real dummy-child integration | 90.55% | 87.50% |

Portable C# and Python enforce 80% lines and branches **per measured file**.
Unity enforces 80% lines per measured production class. PowerShell enforces
80% lines per measured file; its 88.12% console aggregate measures commands,
not lines. The initial broad Unity measurement was 60.2% lines; the final report
is 91.9%. Refactoring changed the measured source, so this is progress evidence,
not a fixed-source comparison. The .NET scope expanded during this pass: its
previous 99.78%/94.69% measured Core alone; adding the formerly unmeasured helper
changes the aggregate without lowering any file's coverage gate.

See [testing](testing.md#coverage-scope) for exclusions. Unsupported and unmeasured
areas have **not** demonstrated the requested 80% branch baseline. In particular,
passing integration checks do not supply missing AHK, Bash or third-party native
library coverage. The helper and legacy installer are now measured rather than
excluded; unexecuted lines in those files remain visible in their reports.

The final Windows player builds with Unity 6000.6.2f1. Copied-player checks passed
setup opening, nonzero exit/retry, emergency wrapper/child cleanup, invalid and
missing executables, and error-history persistence without false play counts.
Original player state was restored byte-for-byte. Isolated installer tests passed
fresh install, complete payload/notices, upgrade configuration preservation and
packaged setup reporting. Source syntax, local documentation links and setup
formatting passed; PSScriptAnalyzer reported zero Error-severity findings.

The live installation was backed up before deployment. Deployment verified all
three production assemblies and preserved the authoritative configuration hash.
Media and the existing optional controller bridge were preserved; the standalone
VPX-side helper was not replaced by this deployment.

## Security findings and limits

- Semgrep 1.182.1 ran 226 rules against 424 source targets: zero findings.
  Six partial-parsing warnings came from two workflow rules interpreting three
  PowerShell snippets as Bash. The scripts also passed PowerShell parsing and
  were reviewed directly. One file over 1 MB and default ignored files were
  skipped. No blanket rule suppression was added.
- Trivy 0.76.1 recognized Python requirements and the NuGet lock: zero reported
  medium/high/critical vulnerabilities, secrets or misconfiguration findings
  in the scanned snapshot. No supported infrastructure config was detected.
  This is not a whole-product CVE clearance.
- pip-audit 2.10.1 reported no known vulnerabilities in the seven pinned Python
  dependencies. Hosted security jobs retain the machine-readable reports.
- The standard release inventory records 144 DLL/EXE hashes, file versions, signatures
  and resolved Unity packages. UnityPlayer's signature validated locally.
  Experimental add-on DLLs are separately inventoried and unsigned; their provenance and hashes are recorded
  in [third-party notices](../THIRD-PARTY-NOTICES.md).
- [Unity's CVE-2025-59489 advisory](https://unity.com/security/sept-2025-01)
  and the [official CVE record](https://cveawg.mitre.org/api/cve/CVE-2025-59489)
  identify affected branches and fixed versions. The record's default status is
  unaffected; our 6000.6.2f1 build lies outside its affected ranges. This resolves
  that specific advisory's applicability, not comprehensive native dependency assurance.
  Review [vendor advisories](https://unity.com/security) for each engine update.

No actionable source/dependency finding remains from the tools above. The
remaining experimental add-on assurance gap is explicitly rated B+, rather than claiming
that unsigned inherited binaries or every Unity package were certified safe.

The [native follow-up](native-assurance.md) records PE mitigation checks, static
imports, a source-build feasibility probe and remaining signing/provenance work.
The native checker now has 98.72% line and 97.37% branch coverage. Its gates do
not change the inherited binaries' assurance grade or certify hardware support.

The native follow-up independently downloaded both bridge DLLs from immutable
upstream commits and verified identical SHA-256 hashes. `tools/native-lock.json`
records those sources; CI checks hashes, inventory and AMD64 PE/DLL headers without
loading libraries. Installer compilation rechecks integrity before packaging.
The AHK loader now uses
[restricted Windows DLL-search flags](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw),
removing its current-directory/PATH fallback. This strengthens provenance and
loading controls; reproducible source builds, signatures and comprehensive native
advisory coverage remain separate limitations. The live bridge/helper were not
replaced during this follow-up, so ongoing gameplay uses the existing installation.

## Remaining work and recommendations

1. Complete a manual first-user walkthrough and any newly deployed optional component
   checks from the [physical release checklist](testing.md#physical-release-checklist).
   The user confirmed VR display, coins/start/flippers/plunger, physical PinOne X
   recentering, normal return to working launcher navigation and a second launch
   on 24 October 2026. This supplements the earlier unavailable-XR recovery test.
   The confirmed setup used its existing PC-specific helper, with the optional
   Touch bridge disabled; it does not certify the new distributable helper or
   that bridge. Preserve this distinction in release compatibility claims.
   The rebuilt installer passed the automated clean-account check and visual
   review of setup pages; see the [recorded evidence](testing.md#compatibility-evidence).
2. Keep the new helper and legacy coverage gates; extend AHK/native-library
   measurement where practical without reducing thresholds or hiding unexecuted
   source. Seek reproducible source builds and signing for
   inherited bridge components before broadening their compatibility claims.
3. Retain the configured PR/check requirements in repository settings as well as
   documentation. Recheck them after any repository recreation; they do not travel
   with Git source. The solo-maintainer configuration requires a PR but not a
   second account's approval; maintainer review remains a documented obligation.
   Preserve upstream credit, notices and archived history.
4. Next usability work: a first-run walkthrough with screenshots and a readable
   controller diagram. Measure its value with a fresh user and different headset
   before adding more menu features.

The standard package now meets this review's A− target for its stated scope.
That is not an A− claim for every file or possible hardware combination: the
separate experimental Touch add-on still has B+ assurance, and unsupported
coverage metrics remain unmeasured. Signing, broader hardware testing and a
first-user walkthrough remain useful improvements, not completed certifications.
