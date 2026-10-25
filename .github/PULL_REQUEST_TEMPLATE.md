## Problem and resulting behavior

Describe the concrete problem and what changes for the user or developer.
Link the related issue, if one exists.

## Validation

List commands/checks and results for the final revision, with CI or artifact
links. Include Unity/build/installer checks where applicable. For physical tests,
record Windows, VPX, headset/controller and runtime versions. Attach screenshots
for visible changes. Never attach tables, ROMs, personal backups or credentials.

## Coverage

Provide the report/artifact and production scope, with before/after line and
branch percentages reported separately. The baseline is at least 80% for both;
changed modules must meet it and existing coverage must not decrease.
Identify unmeasured files and unsupported metrics explicitly. Test counts are
not coverage. For documentation-only changes, explain why this is not applicable.

## Documentation, comments and compatibility

List documentation/control-hint/changelog updates, non-obvious behavior explained
in code comments, and any configuration, migration or compatibility impact.
Use `Not applicable` with a reason where appropriate.

## Limitations and remaining checks

Identify unavailable hardware checks, measurement limitations and security
findings with their disposition. State `None` if there are none.

## Merge checklist

- [ ] I completed every section and followed [CONTRIBUTING.md](../CONTRIBUTING.md).
- [ ] Applicable checks pass on the final revision; evidence is linked above.
- [ ] Tests cover changed behavior, failure paths and regressions.
- [ ] Coverage meets the documented baseline without reduced thresholds or artificial exclusions; any unsupported measurement is disclosed above.
- [ ] Code is readable; comments explain non-obvious decisions and public contracts accurately.
- [ ] User/developer documentation, control hints and changelog are current where applicable.
- [ ] Security findings are addressed or explicitly triaged; dependency locks, integrity checks and license notices are preserved.
- [ ] No credentials, personal configuration, tables, ROMs or generated media are included.

A maintainer must review the final diff, evidence and resolved review comments
before merging. Leave this PR as a draft while required work is incomplete.
