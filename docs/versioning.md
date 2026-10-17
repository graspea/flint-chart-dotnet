# Versioning and compatibility with flint-chart

The package version mirrors the version of `flint-chart` (npm) that the release was generated from and tested against. The design is in [part 3 §8](design/03-sdk-versioning-runbook-tests-risks.md).

## Version grammar

`FLINT.MAJOR.FLINT.MINOR.FLINT.PATCH.OURS[-preview.N]`; the git tag is the version with a `v` prefix.

| Part | Meaning |
|---|---|
| First three | The `flint-chart` version. `Graspea.Flint 0.5.1.x` is generated from and tested against `flint-chart@0.5.1`. The release workflow refuses a tag whose first three parts differ from `FlintUpstream.Version`. |
| Fourth | This project's release counter for that upstream version, starting at 1 (NuGet drops a trailing `.0`). |
| `-preview.N` | A preview of that four-part version. Previews count up on the same four-part version: `0.5.1.1-preview.1`, `0.5.1.1-preview.2`, then `0.5.1.1`. There are no `beta` or `rc` labels. |

Every flint-chart release on npm gets a release of this package, even when nothing changes; the only exception is a version the maintainers skip on purpose, which the README then names. Releases follow npm only: an upstream git tag or GitHub release without an npm version gets nothing.

`FlintUpstream.Version` holds the pinned flint-chart version at runtime.

## Preview and stable

The package stays in preview until the fluent builder milestone is complete and two consecutive upstream releases have been synced without a change to the generator. The next release after that is the first stable one. From then on each new upstream version ships as stable `X.Y.Z.1`, unless the sync shows a breaking upstream change, in which case that line restarts at `X.Y.Z.1-preview.1`. The maintainers may force either label when they tag a release.

While only previews exist, `dotnet add package` needs `--prerelease`, and `dotnet package search` and the NuGet CLI hide the package unless asked for prereleases; nuget.org shows it.

## What a release may change

| Change | Next release of the same upstream version (`.D+1`, or `-preview.N+1`) | New upstream patch | New upstream minor before 1.0, or major |
|---|---|---|---|
| Bug fix in hand-written code | yes | yes | yes |
| New hand-written member | yes | yes | yes |
| Regenerated members (new templates, channels, properties, semantic types, presets) | no | yes | yes |
| `[Obsolete]` on a member that upstream removed | no | yes | yes |
| New Warning or Info validation code | yes | yes | yes |
| New Error validation code | only to report a throw of the pinned version that was missed | yes, when the new upstream throws | yes |
| Different JSON for the same object graph | no | no | yes, documented in the changelog |
| Removal of an `[Obsolete]` member, signature change | no (see the preview rule below) | no | yes |

**Preview rule.** Until the first stable release, the hand-written surface may still change between previews; where the old and the new shape can coexist, the old one is kept as `[Obsolete]` for one release first. Generated members are never removed inside a line: a member that upstream removes stays, marked `[Obsolete]` ([deprecations.md](deprecations.md)).

**After stable.** The hand-written surface is frozen: additions in any release, removals and signature changes only in a breaking window (an upstream minor before 1.0, an upstream major after it). No public type can be derived from, so additions do not break consumers.

## Wire format

Within one line (`X.Y.Z.*`):

1. The same object graph serialises to the same bytes in every release.
2. Every document an earlier release wrote can be read by every later release.
3. Every document the pinned flint-chart accepts can be read and written back without loss; keys the model does not know are kept in `Extensions`.
4. The package never writes a key that the pinned flint-chart rejects with an exception.

## Rendering with another flint-chart version

| flint-chart that renders the JSON | What to expect |
|---|---|
| The same version as the first three parts | Everything above. A document without a validation `Error` assembles on every backend that has its template. |
| A newer patch | Expected to work: no upstream release so far has removed anything that makes assembly throw. Members added upstream are unknown to the package: `Validate` reports a new template as `unknown_chart_type`, but `Build()`, `ToJson()` and `Chart.Of` still write it. |
| A newer minor | The same expectation, weaker: before 1.0 upstream may break things in a minor. |
| An older version | Documents that use only members the older version has assemble; keys it does not know are ignored; a template or preset added later throws there. Not tested. |

Match the first three parts of the package version to the installed `flint-chart` (or `flint-chart-mcp`) version.

## Older lines

Only the newest line receives fixes by default. Every release can be rebuilt from its tag.
