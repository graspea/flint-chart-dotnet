## Companion, part 2: AI and MCP readiness

### AI and MCP readiness

Goal: an AI agent (Copilot, Claude Code, Cursor, a Visual Studio chat session) that is asked to "draw a chart with Graspea.Flint" should find the package, learn the rules that matter, copy a canonical pattern, and validate its output, without reading the source. Items marked UNVERIFIED were not confirmed.

Scope reminder: the SDK emits backend-neutral `ChartAssemblyInput` JSON and never renders. Every agent-facing text repeats that sentence, because the main confusion risk on nuget.org is `Flint.AI` (an unrelated agent-orchestration SDK) and because upstream's README trademark section requires that use of Microsoft trademarks "must not cause confusion or imply Microsoft sponsorship". The wording "unofficial" and "not affiliated with or endorsed by Microsoft" is this project's own choice (decision 10), not an upstream requirement.

Package `Description`, `PackageTags`, GitHub topics, the README title line and first paragraph are owned by the Discoverability section; this section only adds the agent-facing content below them.

#### 1. README structure (repo root = NuGet README)

One `README.md` serves both GitHub and nuget.org; there is no second README under `docs/`. nuget.org renders Markdown through Markdig, has a 1 MB limit, and does **not** index the README for search (only id, title, tags, author, description, summary, owner). Which image hosts it allows (img.shields.io and raw.githubusercontent.com are commonly cited) is UNVERIFIED, as is whether it renders relative links and raw HTML, so use absolute `https://github.com/graspea/flint-chart-dotnet/...` links and badge images from img.shields.io only. Consequence of the indexing rule: keywords go into `PackageTags` and `Description` (Discoverability section); the README carries the explanations.

Outline (H2 = `##`, H3 = `###`):

```
# <title line, badges and first paragraph per the Discoverability section>

## What it is / what it is not          (3 bullets each; "does not render" is bullet 1)
## Install                               (dotnet add package Graspea.Flint --prerelease; link to releases page;
                                          the --prerelease flag and the NuGet badge kind are set in the release PR,
                                          SDK design §9.6 step 2, not by the generator)
## Quick start                           (one typed-model example: rows -> ChartAssemblyInput -> JSON string;
                                          "Row field names" line: ChartData.FromRows writes camelCase keys by default
                                          (RowOptions.Default), string field names are literal,
                                          RowOptions { PropertyNamingPolicy = null } keeps names verbatim,
                                          [JsonPropertyName] is honoured; with M2 the line adds that lambda
                                          selectors resolve to the same names)
## Two API levels
### Typed model (ChartAssemblyInput)     (primary)
### Fluent builder                       (appears with M2; absent from the first release)
## Chart catalog                         (generated section, see §5)
   (table: chartType | backends | channels | notable chartProperties)
## Semantic types                        (generated section)
## Validate against flint-chart
### In-process pre-flight                (builder.Validate(backend) / ChartValidator.Validate; never implicit)
### With the upstream MCP server         (generated mcp-commands section: mcp.json for npx -y flint-chart-mcp@<pin>,
                                          <pin> = the flint-chart-mcp version resolved by the gate script (§5),
                                          and the hosted endpoint;
                                          validate_chart covers vegalite, echarts and chartjs only; the hosted
                                          endpoint runs upstream's deployed version, which may differ from the pin)
## For AI agents                         (see §1a; also packed as AGENTS.md in the nupkg, decision 8)
## Version mapping to flint-chart        (three lines: four-part mirror, -preview.N, "SDK follows npm releases only";
                                          link to docs/versioning.md, owned by SDK design §8)
## Regenerate from a new flint-chart release (one paragraph; links to docs/generator.md)
## About Flint                           (one link each to the upstream repo, arXiv 2607.20775, the Microsoft Research post)
## Contributing                          (link to CONTRIBUTING.md and repo-root AGENTS.md)
## Attribution and license               (MIT, GRASPEA s.r.o.; flint-chart MIT Microsoft; THIRD-PARTY-NOTICES)
```

##### 1a. The "For AI agents" section (skeleton)

Short, deterministic, no prose paragraphs; agents copy from it. Target under 120 lines so it also fits inside the package `AGENTS.md`.

The identifiers are the real ones from SDK design §3 to §6. The generator writes the catalog-dependent parts of this section from the IR that also emits the C#, so the names in the file cannot drift from the compiled surface. The fluent lines appear with M2.

```
## For AI agents

### Rules
1. This SDK produces Flint `ChartAssemblyInput` JSON. It never renders. Do not look for
   a Render/Draw/ToSvg method; pass the JSON to flint-chart (vegalite, echarts, chartjs, ...).
2. Transform data BEFORE building the chart; style AFTER Flint assembles it (upstream contract).
3. Use the struct constants `ChartType.LineChart`, `SemanticType.YearMonth`, `Channel.X`,
   `ThemePreset.Economist`; never free-form strings for known members.
4. A template that is not in the constants is not unsupported: use
   `Chart.Of(new ChartType("..."), rows)` or set `ChartSpec.ChartType` directly; `Validate` will
   report `unknown_chart_type` for the pinned upstream version.
5. `Build()` and `ToJson()` never validate. Call `Validate(backend)` explicitly and read the result.
6. The package version `A.B.C.D` mirrors flint-chart `A.B.C`. Validate and render with the same
   upstream version.

### Canonical patterns
- Inline rows -> bar chart (typed model, 10 lines)
- Field binding from an existing `IEnumerable<T>` (`ChartData.FromRows(rows)`, camelCase keys)
- Serialize: `FlintJson.Serialize(input)` (M2: `builder.ToJson()`)
- Round-trip: `FlintJson.Deserialize(json)` for editing an existing spec; unknown keys survive
- Validate: `ChartValidator.Validate(input, FlintBackend.VegaLite)`
  (M2: `builder.Validate(FlintBackend.VegaLite).ThrowIfInvalid()`)

### Anti-patterns
- Building Vega-Lite / ECharts options by hand "because Flint is just JSON"
- Expecting `theme_spec` on ECharts, Chart.js or Excel (only Vega-Lite and Plotly apply it;
  `theme_ignored_by_backend`)
- Aggregating in the chart spec what should be aggregated in C# first
- Pinning `Graspea.Flint` to a version whose first three parts differ from the flint-chart you render with

### How to validate
1. In-process pre-flight: `ChartValidator.Validate(input, backend)`; Error = the pinned upstream
   assembler throws, Warning = upstream silently drops, Info = worth a look.
2. Authoritative check: upstream MCP `validate_chart` on the SDK's JSON, from `npx -y flint-chart-mcp@<pin>`
   or `https://flint.data-formulator.ai/mcp` (vegalite, echarts, chartjs only; the hosted endpoint
   runs upstream's deployed version). `render_chart` from the same server shows the result.

### Where the catalog is
- Runtime: the struct constants `ChartType.*`, `SemanticType.*`, `Channel.*`, `ThemePreset.*`
- File: `catalog/catalog.json` in the nupkg, generated from `generator/ir/flint-ir.json`
- Docs: README "Chart catalog" section (generated from the same IR)
- Upstream skill: https://github.com/microsoft/flint-chart/blob/0.5.1/agent-skills/flint-chart-author/SKILL.md
```

flint-chart@0.5.1 has no `bin` entry in its package manifest (checked against the registry today), so there is no `npx flint-chart validate` command; any CLI claim stays out of the docs until a bin exists upstream (UNVERIFIED for future versions). `flint-chart@0.5.1` also exports no validator; the only reachable upstream validation is the MCP tool `validate_chart` (and `flint-chart-mcp/render`'s `validateInput`, which performs no channel checks). The Node snippet returns to the README only when upstream releases `flint-chart/validate`, which exists on unreleased `main`. `<pin>` in the `mcp-commands` section is the `flint-chart-mcp` version the gate script resolved (§5): `flint-chart-mcp@<flint-chart version>` when it exists, otherwise the newest `flint-chart-mcp` version not above the pin, with the sync PR labelled `needs-attention`.

#### 2. What ships in the NuGet package for agents

| Artifact | Mechanism | Why | Status |
|---|---|---|---|
| `README.md` | `<PackageReadmeFile>` | nuget.org page, NuGet MCP fallback when `AGENTS.md` is absent | tested, 1 MB limit |
| `Graspea.Flint.xml` | `GenerateDocumentationFile=true` -> `lib/net8.0/Graspea.Flint.xml` | IntelliSense + agents reading `~/.nuget/packages`; `<example><code>` survives verbatim | tested locally |
| `AGENTS.md` at nupkg root | `<None Include="docs/package/AGENTS.md" Pack="true" PackagePath="/" />` | `NuGet.Mcp.Server` 1.4.16 `get_package_context` "returns the AGENTS.md context for a NuGet package when available; otherwise returns the package README" (its own README); it reads `AGENTS.md` from the global packages folder, which matches a nupkg-root file; built into Visual Studio 2026 | tested locally; no official author guidance on placement ([agentsmd issue #106](https://github.com/agentsmd/agents.md/issues/106) open); Learn docs do not mention AGENTS.md |
| `catalog/catalog.json` | `<None Include="src/Graspea.Flint/Generated/catalog.json" Pack="true" PackagePath="catalog/" />` | agents read the full catalog without compiling; not copied into consumer projects | emitted projection of IR `catalog`; default applied |
| Runtime catalog API | none in M1 (`CatalogTables` is internal, SDK design §3) | possible additive API later when a consumer asks | not built |

XML doc rules for the generator and hand-written code:
- Every public type and member gets `<summary>`; builders get `<example><code>` with a complete, compilable snippet (agents scrape these; IDE hovers show only `<summary>/<param>/<returns>`; whether any hover shows `<example>` is UNVERIFIED).
- Generated model members copy the upstream JSDoc into `<summary>` when present and fall back to a template "`Flint channel 'x' (optional) for chart type 'y'`". How many upstream `.d.ts` members have JSDoc is UNVERIFIED; the generator reports the count so the gap is visible.
- `<inheritdoc/>` is written explicitly where used; automatic inheritance does not reach the XML file.
- CI treats CS1591 (missing XML comment) as an error for the public surface.

Generated-section markers: the generator splices tables between `<!-- flint:generated:<id> start/end -->` comments. Because raw-HTML rendering on nuget.org is UNVERIFIED, an MSBuild target strips the markers before pack (`File.ReadAllText` and `Regex.Replace` property functions writing `$(IntermediateOutputPath)README.pack.md`; `ReadLinesFromFile` is not used because it drops blank lines and leading whitespace), so `dotnet pack` in `release.yml` needs no Node and the nuget.org page never shows the comments even if Markdig escapes them. GitHub renders the committed file with the markers hidden as usual.

#### 3. llms.txt and AGENTS.md

Two different files with the same name; they must never be one source:

| File | Audience | Location | Content |
|---|---|---|---|
| `AGENTS.md` (repo root) | agents working **on** the repo | `/AGENTS.md`, committed, hand-written | build/test commands (`dotnet test`, `npm test` in `generator/`), "never edit `src/Graspea.Flint/Generated/**` by hand, run `npm run generate`", contract-test invocation, version-tag rule, PR conventions (English, imperative commit subjects, no co-author trailers). |
| `AGENTS.md` (package) | agents working **with** the library | `docs/package/AGENTS.md`, packed to nupkg root | generated in full by `generator/src/emit/docs.ts` (decision 8): a header "generated; edit README.md", the README "For AI agents" section copied verbatim, then the generated catalog table. No hand-written text, no markers. |
| `CLAUDE.md`, `.cursor/rules` | not shipped | none | skip; `AGENTS.md` is the vendor-neutral file (agents.md, Linux Foundation / Agentic AI Foundation) and the nearest-file rule covers it |

llms.txt: a proposal, not a standard (llmstxt.org; the date of its v2 page is UNVERIFIED); it has to be served from a website, GitHub cannot serve it, and upstream has none. Default applied: skip until a docs site exists. If the owner later enables GitHub Pages for the repo, `docs/llms.txt` is a 20-line H1 + blockquote + three H2 link lists (README, catalog JSON on raw.githubusercontent.com, upstream skill) and gets generated by the same emitter as §5.

Agent Skills (`SKILL.md`, agentskills.io): upstream's `flint-chart-author/SKILL.md` is 34,034 bytes / 669 lines at tag 0.5.1 (730 lines on `main`), over the recommended 500 lines, MIT with Microsoft copyright, and it changes with each release (git blob SHA af1a890f at 0.5.1, dc8b0761 on main). About 90 % is language-neutral and applies to our JSON one-to-one. Decided: **linked at the pinned tag, not vendored** (decision 8). The README and package `AGENTS.md` point to the SKILL.md at the pinned upstream tag (or the `gitHead` commit when the tag is missing), substituted by the generator from `generator/package.json`; the sync PR reports when the upstream blob SHA changes. Auto-copying a skill into consumer repos at build time (community pattern) is not done: it writes into user repos from a package restore.

#### 4. MCP options and milestone placement

Decided: **no own MCP server** (decision 9). The README "Validate against flint-chart" section documents upstream `flint-chart-mcp` with an `mcp.json` snippet for `npx -y flint-chart-mcp@<pin>` (the `flint-chart-mcp` version resolved by the gate script, §5) and the hosted endpoint `https://flint.data-formulator.ai/mcp`, and the package `AGENTS.md` tells agents to call `validate_chart` on the SDK's JSON. No `mcp*` topic or tag is used.

Facts behind the decision: upstream already ships `flint-chart-mcp` with the tools `validate_chart`, `render_chart`, `compile_chart`, `list_chart_types`, `list_themes`, `create_chart_view` and the resources `flint://agent-skill`, `flint://theme-skill`, `flint://chart-types` (live `initialize` on 2026-10-17 reported `flint-chart-mcp 0.5.1`); `validate_chart` covers vegalite, echarts and chartjs. An own server would have to be its own NuGet package (`PackageType=McpServer` + `PackAsTool`; a library referencing it fails with NU1212, DotnetTool package type), need the .NET 10 SDK to pack, a `.mcp/server.json`, a second release pipeline and a registry listing, and it would duplicate `flint-chart-mcp` for validate and render. The one tool upstream cannot offer, "give me the C# for this spec", is scheduled by owner decision 13 (2026-10-18) as a .NET tool and a source generator, designed in [part 7](07-spec-to-csharp-tool-and-generator.md); it is a CLI, not an MCP server.

Placement: the first release (`0.5.1.1-preview.1`) ships the README, the XML docs, the package `AGENTS.md` and `catalog/catalog.json`; GitHub topics are set at repository creation and the `Graspea.` prefix request goes out right after the first publish (both per the Discoverability section). Indexer submission (Context7, DeepWiki, §5) happens after the first publish, independent of the preview label.

#### 5. Keeping docs in sync with generated code

Principle: the generator is the single writer for every table that depends on the pinned flint-chart version; CI fails on drift exactly as it does for `Generated/**.cs`.

Generated targets, all produced by `npm run generate` in `generator/` from the committed IR `generator/ir/flint-ir.json` (decision 1):
1. `src/Graspea.Flint/Generated/**/*.g.cs` (constants, options, theming, internal `CatalogTables`, and with M2 builders, enums and entries).
2. `src/Graspea.Flint/Generated/catalog.json`, the projection of IR `catalog`, packed as `catalog/catalog.json`.
3. Marked sections in `README.md`, section ids `chart-catalog`, `semantic-types`, `upstream-version` (the pinned version string, the `flint-chart` badge version, the "tracks" sentence and the skill link), `mcp-commands` (the `npx -y flint-chart-mcp@<pin>` snippet; `<pin>` comes from `generator/mcp-pin.json`, written by the gate script `install-upstream.mjs` from `npm view flint-chart-mcp@<ver> version`, falling back to the newest `flint-chart-mcp` version not above the flint-chart pin, in which case the sync PR carries `needs-attention` and the report prints the row), `agent-identifiers` (the "For AI agents" skeleton with real names). Text outside the markers is hand-written and never touched; in particular the NuGet badge kind (`v`/`vpre`) and the `--prerelease` install line are set in the release PR (SDK design §9.6 step 2), not by the generator. `docs/package/AGENTS.md` is generated in full (§3).
4. XML doc `<summary>` text for generated members, inside the `.g.cs` files, so there is no separate doc pipeline.

Mechanics:
- `generator/src/emit/docs.ts` renders the tables from the IR (the same object the C# emitters use), splices them between the markers in `README.md`, writes `docs/package/AGENTS.md` whole and writes `catalog.json`; a missing or duplicated marker is an error, not a silent skip.
- `npm run generate -- --check` rewrites to a temp dir and diffs `generator/ir`, `src/Graspea.Flint/Generated`, `tests/fixtures`, `PublicAPI.Unshipped.txt`, `README.md` and `docs/package/AGENTS.md`; the CI job `verify-generated` runs it on every PR, including the bot's, so the sync PR carries README and `AGENTS.md` changes together with the C# changes, reviewed in one place.
- Generator tests (node:test): snapshot of the rendered README section for a synthetic IR; a test that every chart type constant appears in the README table and vice versa; a test that `catalog.json` equals the IR `catalog` projection.
- Upstream skill link: the `upstream-version` section writes the tag from `generator/package.json`; the sync report carries the "skill changed upstream" line so the owner can re-check the hand-written rules (manual, by design).
- nuget.org README is frozen per version; a doc typo means the next release: `-preview.N+1` while in preview, `.D+1` once stable (SDK design §8.1).

Indexers after the first publish (default applied): submit `graspea/flint-chart-dotnet` at context7.com/add-library with a `context7.json` (`projectTitle`, `description`, `rules` = the six rules from §1a, `excludeFolders: ["tests/fixtures","generator/ir"]`, `previousVersions` = one entry per release tag, max 20) and to DeepWiki with `.devin/wiki.json` (`repo_notes` repeating "emits JSON, does not render", pages: Overview, Quick start, Catalog, Validation, Versioning). Both are free for public repos. UNVERIFIED: the exact Context7 and DeepWiki config keys, the DeepWiki badge markup and auto-refresh, and Context7's refresh GitHub Action name; check each against the current docs at submission time and refresh manually until confirmed. The DeepWiki badge host is not known to be on nuget.org's image allow-list, so it goes on GitHub only.

#### Defaults applied in this section (say if you object)

- Catalog delivery: loose `catalog/catalog.json` in the nupkg (not an embedded resource, not contentFiles), emitted from the IR; no public runtime catalog API in M1.
- llms.txt: skipped until a docs website exists.
- Indexers: Context7 and DeepWiki with config files, after the first publish.
- Generated-section markers stripped from the pack copy of the README by an MSBuild target.
