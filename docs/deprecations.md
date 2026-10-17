# Deprecations

Deprecated members compile with a warning and keep working. Each warning has a diagnostic id whose help link points to a section of this page: `[Obsolete(..., DiagnosticId = "FLINT0001", UrlFormat = "https://github.com/graspea/flint-dotnet/blob/main/docs/deprecations.md#{0}")]` opens `deprecations.md#FLINT0001`. The policy is in [versioning.md](versioning.md) and [part 3 §8.4](design/03-sdk-versioning-runbook-tests-risks.md).

Anchor scheme:
- one section per diagnostic id, with the id as its heading and an explicit `<a id>` of the same id, so `#FLINT0001` and `#flint0001` both resolve;
- one table row per deprecated member, keyed by its C# name and, for generated members, the IR path from `generator/retired.json` or `generator/overrides.json`.

Both diagnostics are warnings, never errors. To silence one for a member you still need, use `#pragma warning disable FLINT0001` around the call or `<NoWarn>FLINT0001</NoWarn>` in the project.

<a id="FLINT0001"></a>

## FLINT0001

**The upstream flint-chart release removed the member.** flint-chart ignores the key, channel or value from that release on, and the validator warns about it ([validation-codes.md](validation-codes.md)); a removed template or theme preset makes the assembler throw, which the validator reports as an Error. The member stays until the next breaking window (an upstream minor before 1.0, an upstream major after it) and never less than one upstream minor line. The message names the version that removed it and, where upstream renamed it, the replacement.

| Member | IR path | Removed in flint-chart | Replacement | Deleted in |
|---|---|---|---|---|

No member is deprecated under FLINT0001 in the flint-chart 0.5.1 line.

<a id="FLINT0002"></a>

## FLINT0002

**This package deprecated the member**: a hand-written member replaced by a better shape, or a generated name changed through `generator/overrides.json` after it shipped (the old name is kept when the rename sets `keepObsolete`). The message names the replacement.

| Member | Replacement | Deprecated in | Deleted in |
|---|---|---|---|

No member is deprecated under FLINT0002 yet.
