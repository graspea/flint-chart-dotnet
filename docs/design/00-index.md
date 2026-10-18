## Flint.NET (Graspea.Flint) - design

Flint.NET is an unofficial, community-maintained .NET SDK for Flint (`flint-chart`, npm, MIT), not affiliated with or endorsed by Microsoft. It builds a backend-neutral Flint `ChartAssemblyInput` in C# and emits it as JSON that the pinned `flint-chart` version accepts on every backend (Vega-Lite, ECharts, Chart.js, Plotly, Excel); it never renders. The design is a hand-written core model with converters plus code generated from a committed intermediate representation of the pinned npm package (options, theming, constants, catalog tables and, from M2, per-template fluent builders), an explicit validator whose severities mirror what the pinned upstream throws or silently drops, a four-part version that mirrors the upstream version, and a daily GitHub App bot that turns every upstream npm release into a reviewable sync pull request. Parts 1-3 are the SDK design; parts 4-6 are the companion document (bot, AI and MCP readiness, discoverability); parts 7 and 8 (designs, not implemented) add a spec-to-C# tool, a source generator and late-bound, asynchronous and streamed row data, and continue the SDK section numbering with §12 and §13. Section references such as §9.6 point into the SDK design (parts 1-3, 7, 8); "companion" refers to parts 4-6.

### Owner decisions (2026-10-17)

| Decision | Choice |
|---|---|
| Architecture | Hybrid: hand-written core model (`ChartAssemblyInput`, `ChartData`, `ChartSpec`, `ChartEncoding`, `EncodingValue`, `SemanticAnnotation`, `ChartTheme`, converters); generated `AssembleOptions`, `Theming.*`, constants, catalog tables and fluent facades; the committed IR `generator/ir/flint-ir.json` is the single source; generation fails on unmapped constructs, fixed through `overrides.json` |
| Fluent API | Generated per-template builders (`Chart.Line(rows).X(...).Y(...)`); channels are the union across backends and `Validate(backend)` reports backend gaps; data at the entry point; `BaseSize`/`CanvasSize` instead of `Size`; per-template enums for discrete properties; hand-written CRTP core `ChartBuilder<TSelf,TRow>` plus `GenericChartBuilder` through `Chart.Of` |
| Row-data naming | camelCase by default (`RowOptions.Default`); verbatim through `RowOptions { PropertyNamingPolicy = null }`; `[JsonPropertyName]` honoured |
| Validation | `Build()`/`ToJson()` never validate; `Validate(backend)` is explicit; Error only where the pinned upstream assembler throws, Warning where upstream silently drops, Info otherwise |
| Preview numbering | `-preview.N` on the same four-part version: `0.5.1.1-preview.1` -> `0.5.1.1-preview.2` -> `0.5.1.1` |
| Stable exit | After M2 is done and two consecutive upstream releases synced without generator fixes, the next release is stable; afterwards each upstream version ships as stable `X.Y.Z.1` unless the sync report shows BREAKING rows (that line restarts at `-preview.1`); the maintainer may force preview or stable at tag time |
| Bot identity | GitHub App token (`actions/create-github-app-token`); the maintainer pushes the release tag by hand; no `GITHUB_TOKEN` approval flow, no PAT; one workflow, `upstream-sync.yml` |
| Agent docs | README with a "For AI agents" section, XML docs, generated `AGENTS.md` packed at the nupkg root; the upstream `flint-chart-author` skill linked at the pinned tag, never vendored; a contributor `AGENTS.md` at the repository root |
| MCP | No own MCP server; document upstream `flint-chart-mcp` (`npx -y flint-chart-mcp` and the hosted endpoint) and tell agents to validate the SDK's JSON with `validate_chart`; no `mcp` topics or tags |
| Microsoft naming | Nominative use only, with "unofficial" and "not affiliated with or endorsed by Microsoft"; never a `microsoft` topic, tag or logo; a legal sanity check of the wording is advisable |
| Topics and tags | The recommended 20 GitHub topics (including the backends) and the recommended 23 NuGet tags |
| Fixed earlier | Package and namespace `Graspea.Flint`; repository `graspea/flint-chart-dotnet`; MIT, Copyright (c) 2026 GRASPEA s.r.o., Authors GRASPEA; `net8.0` only, zero runtime dependencies; four-part version mirroring `flint-chart` with the fourth part starting at 1; first release `0.5.1.1-preview.1` from `flint-chart@0.5.1` (npm only); every upstream release gets an SDK release; NUnit and `node:test`, TDD; English only; neutral public audience; no AI attribution anywhere |

### Owner decisions (2026-10-18)

Numbered after the 2026-10-17 table (decisions 1-12, in row order). Parts 7 and 8 list the open high-impact choices with recommendations (D7.1-D7.12, D8.1-D8.9).

| # | Decision | Choice |
|---|---|---|
| 13 | Spec-to-C# tool | A separate .NET tool converts a `ChartAssemblyInput` JSON document into C# that builds the same document with `Graspea.Flint`. Defaults the owner may change before its first release: package `Graspea.Flint.Tool`, command `flint-cs`, `flint-cs to-csharp <file.json\|-> [--style fluent\|model] [--data inline\|external\|parameter] [--namespace N] [--class C] [--out file.cs]`, `net8.0` with `RollForward=Major`, no third-party dependency, depends only on `Graspea.Flint` |
| 14 | Source generator | A Roslyn incremental generator (default id `Graspea.Flint.Generators`) reads a spec through an attribute and AdditionalFiles: `[FlintChart(path)]` infers a row record, `[FlintChart<TRow>(path)]` binds an existing row type with `FLINT1xx` diagnostics; the spec is checked against the catalog at compile time; the conversion core is shared with the tool and compiles for `netstandard2.0` and `net8.0`; C# name tables come from a new generator emitter, so the library gains no public surface and no `InternalsVisibleTo`; the generator requires the exact same `Graspea.Flint` version |
| 15 | Batched, asynchronous, streamed and late-bound data | `IAsyncEnumerable<TRow>`, batch and page sources and streaming serialisation with cancellation; a spec built without data and rows bound later or streamed straight to the output; affects `ChartData`, the entry points, `FlintJson`, the validator and the AOT paths |

### Contents

1. [SDK design, part 1: overview, upstream baseline, architecture, public API](01-sdk-overview-baseline-architecture-api.md)
2. [SDK design, part 2: serialisation, validation, code generation](02-sdk-serialisation-validation-codegen.md)
3. [SDK design, part 3: versioning, runbook, tests, risks](03-sdk-versioning-runbook-tests-risks.md)
4. [Companion, part 1: upstream watch bot](04-companion-upstream-watch-bot.md)
5. [Companion, part 2: AI and MCP readiness](05-companion-ai-and-mcp-readiness.md)
6. [Companion, part 3: discoverability (topics, tags, metadata)](06-companion-discoverability.md)
7. [Tooling design: spec-to-C# tool and source generator](07-spec-to-csharp-tool-and-generator.md)
8. [SDK design, part 4: async, batched, streamed and late-bound data](08-async-and-batched-data.md)
