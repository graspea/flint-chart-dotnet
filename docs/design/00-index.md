## Flint.NET (Graspea.Flint) - design

Flint.NET is an unofficial, community-maintained .NET SDK for Flint (`flint-chart`, npm, MIT), not affiliated with or endorsed by Microsoft. It builds a backend-neutral Flint `ChartAssemblyInput` in C# and emits it as JSON that the pinned `flint-chart` version accepts on every backend (Vega-Lite, ECharts, Chart.js, Plotly, Excel); it never renders. The design is a hand-written core model with converters plus code generated from a committed intermediate representation of the pinned npm package (options, theming, constants, catalog tables and, from M2, per-template fluent builders), an explicit validator whose severities mirror what the pinned upstream throws or silently drops, a four-part version that mirrors the upstream version, and a daily GitHub App bot that turns every upstream npm release into a reviewable sync pull request. Parts 1-3 are the SDK design; parts 4-6 are the companion document (bot, AI and MCP readiness, discoverability). Section references such as §9.6 point into the SDK design (parts 1-3); "companion" refers to parts 4-6.

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
| Fixed earlier | Package and namespace `Graspea.Flint`; repository `graspea/flint-dotnet`; MIT, Copyright (c) 2026 GRASPEA s.r.o., Authors GRASPEA; `net8.0` only, zero runtime dependencies; four-part version mirroring `flint-chart` with the fourth part starting at 1; first release `0.5.1.1-preview.1` from `flint-chart@0.5.1` (npm only); every upstream release gets an SDK release; NUnit and `node:test`, TDD; English only; neutral public audience; no AI attribution anywhere |

### Contents

1. [SDK design, part 1: overview, upstream baseline, architecture, public API](01-sdk-overview-baseline-architecture-api.md)
2. [SDK design, part 2: serialisation, validation, code generation](02-sdk-serialisation-validation-codegen.md)
3. [SDK design, part 3: versioning, runbook, tests, risks](03-sdk-versioning-runbook-tests-risks.md)
4. [Companion, part 1: upstream watch bot](04-companion-upstream-watch-bot.md)
5. [Companion, part 2: AI and MCP readiness](05-companion-ai-and-mcp-readiness.md)
6. [Companion, part 3: discoverability (topics, tags, metadata)](06-companion-discoverability.md)
