## SDK design, part 3: versioning, runbook, tests, risks

### 8. Versioning, stability and cross-version guarantees

#### 8.1 Version grammar (decided 2026-10-17)

`FLINT.MAJOR.FLINT.MINOR.FLINT.PATCH.OURS[-preview.N]`, tag `v…` (prefix held in one `env: TAG_PREFIX` line of `release.yml`).

- First three parts = the `flint-chart` version the release is generated from and tested against; `release.yml` fails unless they equal `FlintUpstream.Version`.
- Fourth part is ours, starts at 1 (NuGet strips a trailing `.0`: `0.5.1.0-preview.1` packs as `0.5.1-preview.1`), increments per upstream-independent release.
- `-preview.N` increments on the same four-part version for every shipped preview: `0.5.1.1-preview.1` → `0.5.1.1-preview.2` → `0.5.1.1`. After a stable `X.Y.Z.n`, the next hand-written release is `X.Y.Z.n+1`. There is no `rc` step. `beta` is never used: it sorts below `preview`, so it could not follow a preview (prerelease labels compare alphabetically, numeric dot parts numerically).
- **Stable exit**: once M2 (the fluent builder) is done and two consecutive upstream releases have synced with no generator fix (no change to `generator/src/**` or `generator/overrides.json` needed to make the sync PR green), the next release is stable. After that, each new upstream version ships as stable `X.Y.Z.1` unless its sync report has BREAKING rows; that line then restarts at `-preview.1`. **Maintainer override**: the maintainer may force either label at tag time; the sync report's suggested tag is a suggestion only, and `release.yml` accepts any grammar-valid label. A line restarted at `-preview.1` goes stable at the same four-part version (`X.Y.Z.1-preview.N` → `X.Y.Z.1`) by the maintainer's tag-time decision; the sync report suggests that stable tag once the restarted line's sync PR is merged with no further generator fix.
- **Stable** = the first tag without a prerelease label. From then on the hand-written surface is frozen.
- Every upstream release gets an SDK release, even without a diff; the only exception is the owner's `upstream-skip` (§9.4). Version comes from the tag (`dotnet pack -p:Version=${tag#v}`).

#### 8.2 Two surfaces, two rule sets

| Surface | Contents | Before Stable | After Stable |
|---|---|---|---|
| Hand-written | core model, `FlintJson`, `ChartValidator`, `ValidationResult`, `ValidationIssue`, `FlintValidationException`, `ChartBuilder` core, `GenericChartBuilder`, `Chart.Of`, `RowOptions`, `SortBy`, string-backed structs, `FlintBackend` | may change in any preview; a removal or signature change ships one release as `[Obsolete]` first when the old shape can coexist | frozen; additions in any release; removals and signature changes only in a breaking window |
| Generated | constants, `AssembleOptions`, `Theming.*`, facades, per-template enums, `Chart.<Name>` entries, `FlintUpstream` | additive per upstream release; a vanished member becomes `[Obsolete]`, never disappears | same |

Additions never break consumers because no public type is derivable (`sealed` classes, `internal` builder constructors, no interfaces). The only addition that can break source is an overload that makes an existing call ambiguous; the facade emitter emits the fixed overload set of §4.2 and nothing else, and the reflection sweep over all facades fails on any other shape. `ValidationIssue` has init-only members and no positional constructor so fields can be added.

#### 8.3 What each release kind may contain

| Change | `X.Y.Z.n → X.Y.Z.n+1` (and `-preview.N → N+1` while in preview) | upstream patch (`X.Y.Z+1.1`) | upstream minor (pre-1.0) or major = breaking window |
|---|---|---|---|
| Bug fix in hand-written code | yes | yes | yes |
| Additive hand-written member | yes | yes | yes |
| Regenerated members | no | yes | yes |
| New `[Obsolete]` on a retired upstream member | no | yes | yes |
| New Warning or Info code | yes | yes | yes |
| New Error code | only when it fixes a missed throw of the pinned version | yes, when the new upstream throws | yes |
| Change of canonical output | no | no | yes, documented |
| Removal of an `[Obsolete]` member, signature change | no (before Stable: allowed under the §8.2 pre-Stable rule) | no | yes |
| `IDictionary<,>` freeze of the map properties, PublicAPI tidy-up | done during the M2 previews under the §8.2 pre-Stable rule, so the stable tag only drops the label | never | yes |
| Package-validation baseline | previous `X.Y.Z.n` (the preview-to-preview baseline is **UNVERIFIED**: the NuGet docs describe the baseline as a previously released stable version; pre-Stable breaking changes need `CompatibilitySuppressions.xml` entries either way) | previous release of the same `X.Y` line | none; new baseline |

#### 8.4 Deprecation policy

- `FLINT0001`: upstream retired the member. Text generated from `retired.json`: `[Obsolete("Removed in flint-chart 0.2.2; ignored upstream. Deleted in the next breaking window.", DiagnosticId = "FLINT0001", UrlFormat = "https://github.com/graspea/flint-dotnet/blob/main/docs/deprecations.md#{0}")]`; a `renamedTo` ledger entry names the replacement.
- `FLINT0002`: the SDK deprecates a hand-written member or a renamed generated symbol; the message names the replacement.
- Both are warnings, never errors. A deprecated member keeps working; a retired key is still emitted when set; the validator reports `retired_member` (Warning) for channels, properties, option values and semantic types, and the throw-class Error for a retired template or preset.
- Lifetime: at least until the next breaking window and never shorter than one upstream minor line. Breaking windows: pre-1.0 upstream minors (where upstream reserves its own breaking changes), post-1.0 upstream majors. The purge is one commit `breaking: purge members retired before <version>`, reviewed against `docs/deprecations.md`, with `CompatibilitySuppressions.xml` entries per intended break (generated with `dotnet pack /p:GenerateCompatibilitySuppressionFile=true`).

#### 8.5 Wire-format stability

1. For a given object graph, `FlintJson.Serialize` is byte-identical across all releases of a line (pinned by `CanonicalOutputTests`, the one string-comparison test).
2. Any document a previous release of the line could write is readable by every later release (fixture round-trip over every stored set; `ExtensionsTests`).
3. Any document the pinned upstream accepts is readable and re-emits `DeepEquals`-equal after canonicalisation (fixture round-trip).
4. The SDK never writes a key the pinned upstream would reject with a throw (`contract-node` job).

#### 8.6 Public API tooling

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` with RS0016/RS0017 as errors; the analyzer also tracks generated code, so `.g.cs` members are in the PublicAPI files, which start with `#nullable enable` (RS0037). RS0026 (overloads with optional parameters) is **disabled** because the fixed overload set of §4.2 uses them deliberately; RS0027 ("API with optional parameters should have the most parameters amongst its public overloads") is the companion optional-parameter rule and may fire on the same shapes; it is verified on the real facade before the first tag and disabled too if it does. The reflection sweep guards the overload shape instead.
- `EnablePackageValidation=true`, `PackageValidationBaselineVersion` = last published package of the same `X.Y` line; a new line has none (preview baseline caveat in §8.3).
- The release PR moves Unshipped to Shipped; the tag points at that merge commit (§9.6).

#### 8.7 Cross-version behaviour

SDK `X.Y.Z.n`, consumer renders with `flint-chart@R`.

| `R` vs `X.Y.Z` | Statement | Not covered | Checked by |
|---|---|---|---|
| Same | everything in §8.5; `IsValid ⇒` every backend that has the template assembles without a throw; samples assemble with zero warnings | rendering details | full CI, gate |
| Newer patch | **expected** (0 counter-examples in 11 transitions: no release ever removed anything that makes assembly throw; the one patch removal, `innerRadius` in 0.2.2, is silently ignored): emitted documents assemble; documents from `R` round-trip through `Extensions` | catalog completeness: a template, channel, option or semantic type added in `R` is unknown; `Validate` reports `unknown_chart_type` (Error) for a new template, Warnings/Infos for the rest; `Build()`/`ToJson()` never validate, so `Chart.Of` and raw setters still produce it | the bot's forward-compat step (companion, "End-to-end flow", `generate` job) |
| Newer minor | same expectation for the JSON shape (0 breaking d.ts changes so far), weaker because upstream reserves breaking changes for minors pre-1.0 | anything upstream breaks | the same step |
| Older | documents using only members present in `R` assemble; keys unknown to `R` are ignored | a template or preset added after `R` throws on `R`; the SDK cannot tell which members `R` lacks | not tested; README rule: match the first three parts to the installed `flint-chart` |

Forward compatibility is proved empirically by the forward-compat step of `upstream-sync.yml` (companion, "End-to-end flow") before regeneration: the old model round-trips the new version's fixtures.

What each upstream change kind does:

| Upstream change | Generated code | Validator | Consumer |
|---|---|---|---|
| Template / channel / property / option / preset / semantic type added | new members | knows it | source-compatible |
| Template or preset removed (never happened) | kept, `[Obsolete] FLINT0001` | `unknown_chart_type` / `unknown_theme_preset` Error naming the removing version | warning at compile time; `Validate` fails, matching the throw |
| Template renamed | old facade `[Obsolete]` with `renamedTo`; new facade generated | as above | one-line rename |
| Channel, property, option value, semantic type removed | kept, `[Obsolete]`; enum value table still emits the raw value | `unsupported_channel` / `retired_member` / `unknown_semantic_type` Warning | warning only |
| Property renamed with alias kept (`showTextLabels`) | both methods exist | none | none |
| Core member added (`interaction_spec`) | nothing; parity test red | — | property added by hand (§9.3) |
| Core member removed or narrowed (never happened) | parity test red | — | kept `[Obsolete]`; converter keeps reading the old form |
| Default or meaning changed (every release) | nothing (not in the IR) | nothing | release notes when the CHANGELOG or a probe reveals it |

### 9. Adding a new upstream version (runbook)

#### 9.1 Automated path (`upstream-sync.yml`, daily)

`upstream-sync.yml` (daily, the only bot) is specified in the companion document, "Upstream watch bot", which owns version selection (lowest stable version above the pin, skipped ones excluded), the branch `upstream/flint-chart-<ver>`, the labels, the identity (a GitHub App token minted with `actions/create-github-app-token` in the `open-pr` job only; no PAT, no `GITHUB_TOKEN` approval flow) and the PR contents, including the forward-compat probe, the IR diff, the `retired.json` additions and the suggested tag computed under §8.1. It never tags or publishes. The maintainer reviews the PR (§9.5), merges, opens the release PR and pushes the tag by hand (§9.6).

#### 9.2 Manual path

```
git switch -c upstream/flint-chart-0.6.0                      # the same branch name as the bot's
cd generator && npm ci --ignore-scripts
npm run install-upstream -- 0.6.0                             # the gate: install exact, npm audit signatures, verify-provenance, resolve flint-chart-mcp
npm run fixtures                                              # forward-compat probe on the old model; writes ../tests/fixtures/flint-chart@0.6.0 from the installed pin
dotnet test ../tests/Graspea.Flint.ContractTests --filter Category=RoundTrip
npm run generate
cd .. && dotnet build -warnaserror && dotnet test
npm --prefix generator run ir-diff -- <(git show main:generator/ir/flint-ir.json) generator/ir/flint-ir.json
```

A fork or an unpublished build: `"flint-chart": "file:../flint-chart-0.6.0.tgz"` in `generator/package.json` (the provenance gate is skipped for a file dependency, by hand only).

#### 9.3 What turns red, why, what to do (TDD order)

| Red | Cause | Action |
|---|---|---|
| Drift job | tree not regenerated | expected on the branch; commit the generated output |
| `generate` fails: collision | two members map to one C# name after the rules of §7.6 | `renames` in `overrides.json`; regenerate; update the sample; note in release notes |
| `generate` fails: unmapped construct | a new union or unsupported TS construct | decide the C# shape (hand-written type with converter, `string?` for a literal union, or explicit `JsonNode`); write the converter unit tests first; add the mapping |
| `generate` fails: kind differs across backends | binary vs discrete etc. | map to a raw `JsonNode?` setter or split names in `overrides.json` |
| `CoreParityTests` | a core type gained, lost or retyped a member | serialisation unit test with the inline expected document first; add the property with `[JsonPropertyName]` and `WhenWritingNull`; a lost member stays `[Obsolete] FLINT0001` |
| `CatalogCrossCheckTests` | emitter bug (both sides come from the IR) | golden emitter test first, then fix |
| `FixtureRoundTripTests` on the new set | a wire shape the model cannot represent | reduce one fixture to an inline unit test; fix model or converter |
| `FixtureExpectationTests` on the new set | upstream throws where no Error is reported, or stopped throwing | add or demote the rule, unit test first; a new Error is allowed in an upstream-version release |
| RS0017 | an emitter dropped a retired member | emitter bug; never inside a line |
| Package validation | a breaking change against the baseline | only in a breaking window with a suppression entry |
| A unit test pinned to a probe fact | upstream behaviour changed | re-run `probes.ts`, update the `// source:` comment and the expectation in one commit |
| `samples/` build | a facade renamed by an override | update the sample; if the old name shipped in a previous line, the override must set `keepObsolete: true` |
| `contract-node` pinned | a sample now warns (e.g. a retired option) | change the sample to a current option |
| Generator `import 'flint-chart'` fails | the release statically imports an optional peer (`vega`, unverified risk) | add the peer as a generator devDependency; nothing changes for consumers |

Generator fixes (`overrides.json`, `generator/src/**`) are never committed to the bot's branch: they land on `main` in their own PR, then the bot is dispatched and force-updates its branch. A fix of this kind resets the clean-sync streak of §8.1.

#### 9.4 Deciding the release kind

The release kind (REGENERATE, BREAKING, held back) is decided here; the label (preview or stable) is decided by §8.1 and is independent of it.

```
generation succeeded, parity green, package validation green?
  yes -> retired.json unchanged, or only channel/property/option/semantic-type entries?
           yes -> REGENERATE release NEW.1 (the expected case)
           no (template or preset removed) -> still a regenerate release; member [Obsolete], validator Error; release notes call it out
  no  -> is the fix additive (override, new core property, new rule)?
           yes -> regenerate release after the fix
           no (removal, signature change, canonical-output change needed)?
                  NEW is a pre-1.0 minor or a major -> BREAKING release NEW.1 with suppressions, purge commit, migration notes
                  NEW is a patch -> HELD BACK: the owner applies the `upstream-skip` label to the bot's PR (companion), the bot
                                    moves on, an issue labelled `upstream`, `needs-attention` tracks the change for the next
                                    upstream minor, and the README states the latest supported version
```

Upstream prereleases (none published so far): the bot ignores them (companion, "Prerelease and backport handling"). A maintainer may run the manual path against `flint-chart@0.6.0-beta.1` on a branch; the SDK is then a prerelease, the numeric check compares the first three parts, `FlintUpstream.Version` holds the full string, the branch is never merged, its fixture folder is not kept.

#### 9.5 Reviewing a sync PR

1. `retired.json` additions: confirm the class (Warning vs throw), add `renamedTo` when the IR diff shows a matching addition, read the Obsolete text.
2. `PublicAPI.Unshipped.txt`: only additions; any `*REMOVED*` line is an emitter bug; names that read badly (`N0`, `_3d`, a wrongly split acronym) are renamed now through `overrides.json`, because once shipped they stay.
3. `expectations.jsonl` diff: new `throws: true` entries need an Error rule.
4. The IR diff table; spot-check the research-derived annotations in `overrides.json` when the affected nodes changed.
5. `Generated/**`, the README sections and `docs/package/AGENTS.md`: shape only; the drift job proves they are the emitter's output. The NuGet badge kind and the install line are not part of a sync PR; they belong to the release PR (§9.6 step 2).
6. `tests/fixtures/flint-chart@NEW/manifest.json`: counts plausible (append-only growth), seed and TZ as expected; `known-failures.txt` empty or justified.
7. The "skill changed upstream" line: re-read the hand-written agent rules in the README when the upstream skill changed.

#### 9.6 Release and tag

1. Merge the sync PR into `main` (CI green, non-draft, one approving review). A sync PR is never edited by hand; generator fixes go to `main` in their own PR and the bot regenerates (§9.3).
2. Release PR `release: <tag>`, with the tag taken from the sync report's suggestion or from the maintainer's override (§8.1): move Unshipped to Shipped; set `PackageValidationBaselineVersion`; add the CHANGELOG entry (IR diff table plus hand-written notes on defaults and behaviour); set by hand the two README lines that depend on the label and that no generator writes: the NuGet badge (`vpre` while the newest line is a preview, `v` once it is stable) and the install line (`dotnet add package Graspea.Flint --prerelease` while only previews exist, without the flag once a stable release exists, both lines while the newest line is a preview after a stable release exists). `verify-generated` does not cover these two lines. Release PR checklist: Shipped moved, baseline set, CHANGELOG entry, NuGet badge kind, install line. Merge.
3. The maintainer tags the release PR's merge commit by hand and pushes the tag (`git tag v0.6.0.1 <merge sha> && git push origin v0.6.0.1`). A tag ruleset on `v*` lets only maintainers create tags; the bot never tags.
4. `release.yml` (`push: tags: v*`):
   - checks the grammar `^v\d+\.\d+\.\d+\.\d+(-preview\.\d+)?$`; any grammar-valid label is accepted (maintainer override);
   - strips `TAG_PREFIX`; fails unless the first three parts equal `FlintUpstream.Version` (the drift check keeps the `generator/package.json` pin equal to it);
   - fails unless `npm view flint-chart@X.Y.Z version` succeeds (unpublish guard; npm never reuses a version number);
   - runs the full suite including `contract-node`; `dotnet pack -p:Version=${tag#v}` with package validation, then asserts that the nupkg contains `AGENTS.md` and `catalog/catalog.json` (`Directory.Build.props` packs `AGENTS.md` only when it exists, so a missing file would otherwise pass);
   - publish job: `environment: nuget` (required reviewer), `permissions: id-token: write`, `NuGet/login@8d196754b4036150537f80ac539e15c2f1028841` (v1.2.0) with `user: ${{ vars.NUGET_USER }}`; its `NUGET_API_KEY` output (valid for one hour) feeds `dotnet nuget push`; the Trusted Publishing policy on nuget.org names the owner, the repository `graspea/flint-dotnet`, the workflow file `release.yml` and the environment `nuget`; no API-key secret;
   - a final job with `contents: write` runs `gh release create "$GITHUB_REF_NAME" --title "Flint.NET <ver> (flint-chart X.Y.Z)"` with a body that is the line "Tracks flint-chart X.Y.Z (npm): <link>" followed by the CHANGELOG entry (no `--generate-notes`), and `--prerelease` when the tag has a label. Title and body wording per the companion, "Discoverability".
5. A fix inside a line: branch from the line's latest tag (or `main` while it is still on that line), TDD the fix, tag `vX.Y.Z.n+1` (or `-preview.N+1` while that four-part version is in preview).

Older lines: only the newest line receives fixes by default; every release is reproducible from its tag. On request a maintenance branch `release/X.Y` takes hand-written bug fixes only, cherry-picked from `main` with their failing test, and is deleted when the next line ships. A validator overload that knows which members an older upstream lacks (`since` per catalog entry) is an additive upgrade built only when a consumer asks.

### 10. Tests (TDD)

#### 10.1 Rules for every layer

1. **Red first.** Every behaviour change starts with a test that fails for the stated reason: an assertion failure or a compile error naming the missing production member. Green is the smallest change that passes. A test that passes on first run is redundant or wrong.
2. **Expected values never come from the code under test.** Each test states its source in a one-line comment: `// source: d.ts T:2949 (wire name)`, `// source: probe (null channel throws on vegalite)`, `// source: design §5.2 (canonical form)`, `// source: ir/flint-ir.json`. Admissible sources: the upstream package (d.ts, registries, recorded assembler behaviour), the committed IR, a design decision quoted from this document.
3. **Assertions.** NUnit 4 constraint model only. JSON through one helper:

   ```csharp
   static class JsonAssert
   {
       public static void Equal(string expectedJson, JsonNode? actual) =>
           Assert.That(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), Is.True,
               () => $"expected:\n{expectedJson}\nactual:\n{actual?.ToJsonString()}");
   }
   ```

   Expected JSON is an inline raw string literal. The contract canonicaliser re-emits every number through `double` before comparing, because fixtures come from JavaScript.
4. **Public surface only.** No `InternalsVisibleTo`. Converters through `FlintJson`, tables through the validator and the structs, rules through `ChartValidator`.
5. **Naming.** Class = production type + `Tests`; method = `Member_Condition_Expectation`; `[TestCase]` with `TestName`; fixture tests named `<group>|<title>`; Node tests `describe(module) / it("does X when Y")`.
6. **Categories** `Contract`, `RoundTrip`, `Expectations`, `Parity`; CI runs all; `dotnet test --filter Category!=Contract` runs in seconds.

#### 10.2 Projects and CI

```
tests/Graspea.Flint.Tests/            NUnit unit: Model/ Serialization/ Rows/ Validation/ Parity/ Fluent/ (M2)
tests/Graspea.Flint.ContractTests/    NUnit fixture round-trip + recorded outcomes; reads tests/fixtures/; offline, no Node
tests/fixtures/flint-chart@0.5.1/     cases.jsonl (uncompressed; git packs it), expectations.jsonl, probe-cases.jsonl, probe-expectations.jsonl, manifest.json, known-failures.txt
                                      (the only fixture location; generator/ir/flint-ir.json is the only catalog source, catalog.json an emitted projection)
tests/contract-node/                  node:test, CI only: assembles samples/out/*.json with the pinned flint-chart
generator/test/                       node:test: naming, IR, extractors, emitters (inline golden strings), docs emitter, overrides schema, fixture determinism, `.nvmrc`/`engines` agreement
samples/                              Graspea.Flint.Samples writes samples/out/*.json; Graspea.Flint.Samples.Aot publishes with PublishAot using JsonTypeInfo overloads only
```

`ci.yml` (ubuntu-latest; .NET SDK from `global.json`; Node from `.nvmrc`):

| Job | Steps | Needs | Gate |
|---|---|---|---|
| `verify-generated` | `npm ci --ignore-scripts`, `node --test`, `npm run generate -- --check` (drift over IR, `Generated/`, fixtures, `PublicAPI.Unshipped.txt`, README sections, `docs/package/AGENTS.md`) | — | yes |
| `dotnet` | `dotnet build -warnaserror`, `dotnet test` (unit + contract, offline), `dotnet pack` with package validation | — | yes |
| `samples` | `dotnet run --project samples/Graspea.Flint.Samples` → `samples/out`; `dotnet publish samples/Graspea.Flint.Samples.Aot -p:PublishAot=true` and run the binary | `dotnet` | yes |
| `contract-node` | `node --test tests/contract-node` against the pinned package | `samples` | yes |

There is no `contract-node-latest` job: the newer-upstream probe is the bot's forward-compat step (§8.7), so `upstream-sync.yml` stays the only automated upstream watcher. `release.yml` is tag-triggered (§9.6); `upstream-sync.yml` is daily (§9.1). No PAT. `upstream-sync.yml` uses a GitHub App token (`actions/create-github-app-token`, `client-id` input): the client ID is a repository variable and the private key is a secret of the `upstream-sync` environment (deployment branch `main`). PR CI runs with a read-only `GITHUB_TOKEN` and no secrets. Publishing uses OIDC through the `nuget` environment; the nuget.org profile name for `NuGet/login` is a repository variable.

#### 10.3 Layers

| Layer | Asserts | Red-first procedure | Expected values from |
|---|---|---|---|
| Unit: model + serialisation | each type and union converter writes the Flint wire shape and reads every wire form; nulls omitted in the spec part; extensions round-trip, written last; map keys verbatim and output identical under hostile host options, including `WriteAsString` and a host source-generated context | write the inline JSON from the d.ts line, call the missing type, let the compile error name it | d.ts wire names and literal unions; probe facts |
| Unit: row contract | `FromRows` key naming, `FirstRowKeys`, every row of the §5.3 table, non-finite doubles, nulls always written, null-dropping `JsonTypeInfo` and `RowOptions` rejected, lambda resolution with reflection and with a source-generated context (net8 fallback) | one `[TestCase]` per table row | §5.3 table (STJ defaults + upstream inference) |
| Unit: fluent (M2) | each core method is one model mutation; a chain and a hand-built model are `DeepEquals`; lambda resolution; `Build()` returns a fresh document; `Build()`/`ToJson()` never validate; a reflection sweep over all facades checks every catalog channel and property has the predicted method and no other overload shape exists | write the chain and the inline expected document first | §4.2 examples; IR tables for the sweep |
| Unit: validation | for every code a minimal document yields exactly that code with the documented severity; a clean document yields nothing; backend-scoped checks differ per backend (Excel throws; `channel_null` is Info on Excel) | `Has.Exactly(1).Matches<ValidationIssue>(i => i.Code == "…" && i.Severity == …)` | §6 tables with the cited fact per code |
| Unit: catalog cross-check + core parity | compiled tables equal the IR (counts derived from the IR, not hard-coded); every IR member of `ChartAssemblyInput`, `ChartData`, `ChartSpec`, `ChartEncoding`, `SemanticAnnotation`, `ChartSize`, `ContinuousMarkCrossSection` has a hand-written property with the same wire name and optionality; struct constants equal the IR literal lists; `FlintBackend` equals the backend list | written once; red whenever the IR gains a core member | the committed IR |
| Contract: fixture round-trip | per fixture of every stored set: `Deserialize` → `Serialize` → canonicalise → `DeepEquals` | red from day one of M1; ids in `known-failures.txt` are `Assert.Inconclusive`; CI fails when a listed id passes; the preview tag requires the file to be empty | `TEST_GENERATORS` via the §7.9 mapping |
| Contract: recorded outcomes | per fixture x backend: `throws ⇒ ≥ 1 Error`; `!throws ⇒ no throw-class Error`; `"invalid-option-value" ∈ warnings ⇒ invalid_option_value ∈ issues`. Throw-class codes: `unknown_chart_type`, `chart_type_not_on_backend`, `missing_encodings`, `channel_null` (four layout backends), `data_missing`, `unknown_theme_preset` (vegalite, plotly), `series_*`, `data_empty`/`data_url_only`/`encodings_empty`/`excel_unsupported` (excel). The 219 recorded Excel throws are the hard part of this contract. Gate only for the set matching `FlintUpstream.Version` | as above | assemblers run by `fixtures.ts` |
| Contract-node: emitted samples | every `samples/out/*.json` assembles on every backend that has its template with `_warnings.length === 0` (Excel: no throw and empty `warnings`; samples use no facets or static series) | add the sample chain first (fails to compile until the facade exists) | the pinned package |
| Generator | naming and collision rules; IR writer idempotent; registry extractor counts against the installed package; each emitter turns a synthetic IR into an inline expected C# string; the docs emitter renders the README section snapshot and every chart type constant appears in the README table and vice versa; `catalog.json` equals the IR `catalog` projection; unmapped construct, collision and kind-mismatch fail with the IR path; `overrides.json` validates against its schema; two fixture runs byte-identical | synthetic IR and expected `.cs` text first | §7.4–§7.6; registry facts |
| Drift, Public API, package, samples build | §7.7 check; RS0016/RS0017; package validation; `samples` with warnings as errors; AOT publish | not written, enforced by CI | the committed tree |

**Fixture formats.** `cases.jsonl`: `{"id":"Semantic Context|#0","chartType":"Bar Chart","defaultBackend":"vegalite","input":{…}}` per line. `expectations.jsonl`: `{"id":"…","backends":{"vegalite":{"throws":false,"warnings":[]},"echarts":{"throws":false,"warnings":["overflow"]},"excel":{"throws":true}}}` (Excel entries carry `warnings` from the top-level `warnings` array when they do not throw). `probe-cases.jsonl` and `probe-expectations.jsonl`: the same formats for the crafted cases of `generator/src/extract/probe-cases.ts`, one per validator rule the corpus never exercises. `manifest.json`: `{"upstream":"0.5.1","integrity":"sha512-…","cases":934,"probeCases":37,"tz":"UTC","seed":42,"excludedNondeterministic":[]}`. Folders are append-only, one per upstream version; compression is revisited only if the repository passes 50 MB.

**Generator determinism.** `flint-chart` exact with integrity, `typescript` exact, Node major via `engines` (asserted equal to the `.nvmrc` major by a generator test), `TZ=UTC` set by the scripts, Park-Miller seed 42 by replacing `Math.random` before importing `flint-chart/test-data`, `--ignore-scripts`; sorted IR; sorted file lists; no timestamps, hostnames or absolute paths; LF via `.gitattributes`. Tested twice: `fixtures.test.ts` runs the extractor twice and compares hashes; the drift job regenerates everything.

#### 10.4 The first failing tests for M1, in order

Generator first, because the .NET tables and constants are generated.

1. `generator/test/naming.test.ts`: `KPI Card → KpiCard`, `ECDF → Ecdf`, `step-after → StepAfter`, `catmull-rom → CatmullRom`, `LR → Lr`, `logScale_x → LogScaleX`, `3d → _3d`; entries `Line Chart → Line`, `Boxplot → Boxplot`, `Sankey Diagram → SankeyDiagram`, `Ranged Dot Plot → RangedDot`; collision rule: channel `opacity` + property `opacity` on one template → `Opacity` / `OpacityValue`; property `size` vs reserved core name → unaffected (core uses `BaseSize`); two entries equal after stripping → throws with both IR paths. Drives `naming.ts`.
2. `generator/test/ir.test.ts`: canonical writer sorts keys, two-space indent, LF, trailing newline, idempotent.
3. `generator/test/extract-registry.test.ts` against the installed `flint-chart@0.5.1`: 5 backends, 48 chart types, 44 semantic types, 10 presets, 296 properties, 46 distinct discrete pairs; the channel union has 27 names, contains `theta` and not `text` (the count is derived from the IR, not assumed); `Line Chart` has `strokeDash` on vegalite, plotly and excel and `detail` on vegalite only; `goal` appears only on `Bullet Chart` and `KPI Card`; `Doughnut Chart` is chartjs-only; Excel `hasValidate` is false for Pie and Donut only. Drives `registry.ts`.
4. `generator/test/emit-tables.test.ts`: a synthetic IR with one chart type on two backends emits the inline expected `CatalogTables.g.cs` (header, `FrozenDictionary` rows, no timestamp).
5. `generator/test/emit-options.test.ts`: (a) `continuousMarkCrossSection` without an override fails with the IR path; (b) with the override the property is `Graspea.Flint.ContinuousMarkCrossSection?` with `[JsonPropertyName]`, `WhenWritingNull`, a `Record<string,T>` member gets the closed-generic `VerbatimKeyDictionaryConverter<T>`, the class carries `[JsonNumberHandling(Strict)]` and a trailing `[JsonExtensionData] Dictionary<string, JsonElement>? Extensions`; (c) two members colliding after `Pascal` fail with both paths; (d) an `any` member without an override fails; (e) a key that is binary on one backend and discrete on another fails with both paths. Drives `options.ts`, overrides loading, `overrides.schema.json`.

   After these are green, `npm run generate` produces `Constants`, `Options`, `Theming`, `Tables`, `FlintUpstream.Version`, the README sections, `docs/package/AGENTS.md` and `catalog.json`. The .NET side starts.

6. `FlintJsonTests.Serialize_MinimalInput_EmitsFlintWireNamesAndOmitsAbsentMembers`: Semantic Context #0 without annotations → `{"data":{"values":[{"city":"Seattle","revenue":501479}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"city"},"y":{"field":"revenue"}}}}`. Drives `ChartAssemblyInput`, `ChartSpec`, `ChartData.FromJson`, `ChartEncoding`, `EncodingValue`, `ChartType`, `FlintJson`, `FlintJsonContext`.
7. `FlintJsonTests.Serialize_UnderHostileHostOptions_IsIdenticalToFlintJson`: a document with `encodings["strokeDash"]`, `semantic_types["fiscalYear"]`, `chartProperties["logScale_x"]`, `field_display_names["GDP per capita"]` through `new JsonSerializerOptions { PropertyNamingPolicy = SnakeCaseLower, DictionaryKeyPolicy = SnakeCaseLower, DefaultIgnoreCondition = Never, IgnoreReadOnlyProperties = true, NumberHandling = WriteAsString }` and through a host `JsonSerializerContext` equals `FlintJson.Serialize`. Drives `VerbatimKeyDictionaryConverter<T>`, `[JsonNumberHandling(Strict)]`, the converters' use of `FlintJsonContext.Default` and the attributes on types.
8. `EncodingValueConverterTests` (TestCases): read `"month"` → `Single`, `Field == "month"`; read `{"field":"m","aggregate":"sum","sortOrder":"descending"}`; read `[{"field":"a"},"b"]` → `IsSeries`, two encodings; write a one-entry `Series` → `[{"field":"a"}]`; read `aggregate:"mean"` → `Aggregate.Mean`; read `aggregate:"median"` → `Aggregate("median")` round-trips verbatim. Drives the converter and the four literal structs.
9. `SemanticAnnotationConverterTests` (TestCases): `SemanticType.YearMonth` as annotation → `"YearMonth"`; `{Type = Amount, Unit = "USD"}` → `{"semanticType":"Amount","unit":"USD"}`; read `"Revenue"` → `IsRegistered == false`; read `{"semanticType":"Percentage","intrinsicDomain":[0,100]}`. Pins the camelCase wire key. `ChartTypeTests`: `new ChartType("")` throws `ArgumentException`; `default(ChartType)` fails to serialise with `JsonException`; reading `""` and `42` throws `JsonException`; `"Image Chart"` round-trips.
10. `ChartDataTests.FromRows_Record_WritesCamelCaseKeysAndExplicitNulls`: `record Sale(DateOnly Month, decimal Revenue, string? Region)` with a null region → `[{"month":"2024-01-01","revenue":1200.50,"region":null}]`, `FirstRowKeys == ["month","revenue","region"]`.
11. `ChartDataTests.FromRows_ValueTypes_WriteDocumentedJson` (one TestCase per row of §5.3): `DateTimeOffset`, `DateTime` Unspecified, `double.NaN` → `null`, `float.PositiveInfinity` → `null`, enum → name, `Guid` → string, `TimeOnly` → `"10:00:00"`, `TimeSpan` → `"1.02:03:04"`, `long` 2^53+1 exact, `bool`.
12. `ChartDataTests.FromRows_JsonPropertyNameOnRowType_IsUsedAsKey`, `FromRows_SourceGeneratedTypeInfo_ResolvesLambdaName` (with a `JsonSerializerContext` declared in the test project; on net8 it exercises the `MemberInfo` fallback of §5.3, including `[JsonPropertyName]` and an inherited member), `FromRows_TypeInfoThatDropsNulls_Throws` and `FromRows_RowOptionsWithNullDroppingRow_Throws`.
13. `ExtensionsTests.Deserialize_UnknownKeys_RoundTripAtEveryLevel`: `interaction_spec` at the top level, unknown keys in `chart_spec`, an encoding, `options`, a `semantic_types` object and `theme_spec` round-trip byte-identical and are written after the known keys; an empty extension map writes nothing. Proves the emitter adds `Extensions` to generated classes.
14. `ChartThemeConverterTests`, `ContinuousMarkCrossSectionConverterTests`, `ChartSizeTests`: `ThemePreset.Economist` → `"economist"`; a `ThemeSpec { Ink = … }` → object; `12.5 ↔ 12.5`; `{x:1,y:2,seriesCountAxis:"auto"}` ↔ `SeriesCountAxis.Auto`; `new ChartSize(800,400)` → `{"width":800,"height":400}` under `PropertyNamingPolicy = null`.
15. `CoreParityTests.HandWrittenCore_MatchesIr` and `CatalogCrossCheckTests.CompiledTables_EqualIr` (including `FlintBackend` vs the backend list and the channel union containing `theta`).
16. `ChartValidatorTests.Validate_UnknownChartType_IsError`, `Validate_ChartTypeMissingOnBackend_IsError` (`Doughnut Chart` on `VegaLite` → `chart_type_not_on_backend`), `Validate_EmptyData_IsErrorOnExcelOnly`, `Validate_EmptyEncodings_IsErrorOnExcelOnly`, `Validate_NullChannel_IsErrorExceptOnExcel`, `Validate_UrlOnlyData_IsErrorOnExcelOnly`, `Validate_FacetOnExcel_IsExcelUnsupported`, `Validate_StaticSeriesOnExcel_IsExcelUnsupported`, `Validate_CanvasTooLarge_IsInfo`.
17. `ChartValidatorTests.StaticSeries_UpstreamRule_IsError` (seven TestCases); `Warnings_*` for `unsupported_channel` (per backend and any), `unknown_field` (exempt for `aggregate: count`), `unknown_semantic_type`; `Infos_*` for `invalid_literal`, `inconsistent_row_keys`, `row_not_object`; `Validate_CleanDocument_HasNoIssues`.
18. `FixtureRoundTripTests.RoundTrip_Fixture_IsCanonicallyEqual` and `FixtureExpectationTests.Validator_AgreesWithRecordedOutcome` over `flint-chart@0.5.1`, with `known-failures.txt` consumed; each listed fixture is reduced to a unit test in the pattern of tests 6–14 until the file is empty.

The first M2 test is `ChartBuilderTests.Encode_MemberExpression_ResolvesWireNameFromRowMetadata`, followed by `LineChartBuilderTests.Chain_EqualsHandBuiltModel`, `Chart_Line_StringArgument_DoesNotCompile` (a compile probe like the spike's `Spike.Ambiguity`) and the facade reflection sweep.

**Milestones.** M1: §4.1 model, §5, string-backed structs, generator for `constants`/`options`/`theming`/`tables`/`docs`/`fixtures`, validator with every Error code plus the Warnings `unsupported_channel`, `unknown_field`, `unknown_semantic_type` and the Infos `invalid_literal`, `inconsistent_row_keys`, `row_not_object`, contract and parity tests, drift check, public API files; the README Quick start and the package description use the typed model (fluent wording arrives with M2). M2: `builders`/`enums`/`entries` emitters, remaining Warning/Info codes, emitted-sample assembly job, the `IDictionary<,>` freeze and PublicAPI tidy-up (§8.3). Stable: first tag without a label once the §8.1 exit rule holds (or the maintainer forces it); no API change in that release. `Graspea.Flint.AspNetCore` follows the first stable release and is not a gate for it.

### 11. Risks, deliberate simplifications, open questions

| # | Decision | Risk / cost | Mitigation or upgrade path |
|---|---|---|---|
| 1 | Hand-written core model | an upstream core addition needs a human | parity test names the member; the core changed in 2 of 11 transitions, both additive |
| 2 | Facades expose the union of channels and options across backends | a method can be a no-op on the target backend (18 of 40 multi-backend types differ) | `Validate(backend)` reports it; per-backend facades would be an additive generation from the same IR |
| 3 | 46 enums and 48 facades in the root namespace | large surface; every upstream property addition is a public API addition | generated; PublicAPI files regenerated; removals never deleted inside a line |
| 4 | Data at the entry point; `BaseSize`/`CanvasSize` instead of `Size` | deviates from the sketch | stated in docs; decided |
| 5 | Rows materialised to `JsonArray` | ~2x memory | lazy row source, additive |
| 6 | Reflection for row types in `FromRows(rows, RowOptions)` | not AOT-safe | annotated with `RequiresUnreferencedCode`/`RequiresDynamicCode`; the `JsonTypeInfo` overload is. A Roslyn source generator for row types (`[FlintRow]` producing the `JsonTypeInfo<TRow>` and a constant per field name, plugging into the existing overloads) is a possible later addition, built only when a consumer asks |
| 7 | Map ordering via `Dictionary` insertion order | non-contractual runtime behaviour | internal ordered map after freezing as `IDictionary<,>` in the M2 previews |
| 8 | `required_channel_missing` is Info | a KPI Card without `metric` passes `IsValid` yet fails in MCP `validate_chart` | promote once upstream declares required channels; agents run `validate_chart` anyway |
| 9 | `DateTime` Unspecified written without a zone; `TimeSpan` strings infer temporal; `decimal`/`long` precision | upstream realities | Info issues and docs |
| 10 | Canonical output rewrites encoding shorthand and collapses trivial annotations | not a byte-for-byte echo of arbitrary input | semantics identical upstream; canonicaliser shared with contract tests |
| 11 | Excel `validate()` conditions are hand-written rules from recorded outcomes, not extracted | a new Excel condition in an upstream release shows up only as a red expectation | `FixtureExpectationTests` names the fixture; add the rule (§9.3) |
| 12 | Generation fails on collisions, unmapped constructs and kind mismatches | a sync PR needs a human line | only on failure; a shipped `JsonNode?` would be a frozen mistake |
| 13 | `import 'flint-chart'` in the generator | a future release may statically import the `vega` peer (unverified) | pinned tarballs; add `vega` as a generator-only devDependency if needed |
| 14 | Fixture determinism relies on replacing `Math.random` before importing `flint-chart/test-data` | an upstream switch to `crypto.getRandomValues` or a change in module evaluation order reintroduces nondeterminism silently | two-run hash test; fallback: exclude the affected ids via `manifest.json` `excludedNondeterministic` |
| 15 | `compileDefaults` emitted as a flattened 29-property duplicate of `AssembleOptions` | duplicate surface in `Theming` | explicit; a hand-written wrapper with a flattening converter is an additive replacement in a breaking window |
| 16 | No defaults, no applicability, no data inference modelled | users cannot ask what a property will do; rules that need a field's type skip untyped fields | out of scope by design |
| 17 | Upstream CHANGELOG unreliable | cannot drive anything | the IR diff is the changelog |
| 18 | `interaction_spec` and `validate` arrive only with the next upstream release | consumers wanting interactions wait | `Extensions` carries it today; typed automatically once generated from that release |
| 19 | Newer-patch compatibility is an expectation, not a guarantee | the bot's forward-compat step records counts, it does not gate | 0 counter-examples in 11 transitions; a failed round-trip is visible in the sync PR body; the next SDK line follows within a day of the sync PR |
| 20 | Package-validation baseline between two previews | the NuGet docs describe a stable baseline (**UNVERIFIED** for previews) | suppressions per intended pre-Stable break; re-check on the first `-preview.2` |

**Open questions, to settle before tagging `0.5.1.1-preview.1`**

1. The full collision list and resulting method names from running `naming.ts` on the 0.5.1 IR (expected from the registry facts: `Opacity`/`OpacityValue` on Scatter Plot, Area Chart and Bubble Chart and nothing else; unverified until the run), checked into `docs/generator.md`.
2. Every `overrides.json` entry for the 0.5.1 d.ts: union mappings, `ChartSize` dedup, `ThemeGuard` shape, and the backend-id renames if `FlintBackend` ever stops being hand-written.
3. `metadata.levels → sortOrder` and the fixture cross-check against upstream's 705 `input.json` files (`crosscheck.ts`, result in `docs/fixtures.md`).
4. Whether RS0027 fires on the §4.2 overload set (§8.6).
5. `ChartData.FirstRowKeys` is the first row (what the library sees), not the union of rows (what MCP checks); `inconsistent_row_keys` covers the difference. Confirmed as the design choice; listed here because the MCP front door disagrees.

Settled since the draft (2026-10-17): the channel count (27, `text` excluded); the Excel `validate()` conditions and Excel warning channel (§6, §7.9); `AttributeProvider` on source-generated contexts (null on net8, fallback specified in §5.3); the `Guid`, `Uri`, `TimeSpan` and theme-backend probes (§5.3, §2).
