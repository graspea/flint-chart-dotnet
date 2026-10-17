## SDK design, part 1: overview, upstream baseline, architecture, public API

### 1. Overview, goals, non-goals

Flint.NET is an unofficial, community-maintained .NET SDK for Flint (`flint-chart`, by Microsoft; npm package `flint-chart`, MIT). It is not affiliated with or endorsed by Microsoft; the name "Microsoft" appears only as nominative use together with that disclaimer, never as a topic, tag or logo (companion document, "Discoverability"). The SDK does one thing: it builds a backend-neutral Flint `ChartAssemblyInput` and emits it as JSON that the pinned `flint-chart` version accepts on every backend.

| Item | Decision |
|---|---|
| Package / root namespace | `Graspea.Flint` (later `Graspea.Flint.AspNetCore`) |
| Target | `net8.0`, zero runtime dependencies (`System.Text.Json` from the BCL) |
| First release | `0.5.1.1-preview.1`, generated from and tested against `flint-chart@0.5.1` (npm `latest`) |
| Architecture (decided 2026-10-17) | hybrid: hand-written core model and converters; generated `AssembleOptions`, `Theming.*`, constants, catalog tables and fluent facades; the committed IR (`generator/ir/flint-ir.json`) is the single source; generation fails on unmapped constructs, fixed through `overrides.json` |
| API levels | (1) typed model of the whole `ChartAssemblyInput`, primary, ships in M1; (2) fluent builder with generated per-template builders, sugar over (1), never the only way, ships in M2 |
| Generation | a Node/TS generator reads the npm package's `.d.ts` and runtime registries, writes a committed IR, emits C#; CI regenerates and fails on drift |
| Tests | NUnit constraint model, inline expected JSON compared with `JsonNode.DeepEquals`; upstream fixtures round-tripped; recorded assembler outcomes; emitted samples assembled by Node |

**Goals**

1. Emit a `ChartAssemblyInput` document with Flint-exact wire names regardless of the host's `JsonSerializerOptions`.
2. Represent the whole 0.5.1 input model losslessly, including keys the SDK does not know yet.
3. A fluent surface that survives every upstream release without source-breaking changes, so it can be frozen at Stable.
4. Catch before the JSON leaves the process what upstream reports late or never: unknown chart type (throws), undeclared channel (silently ignored on four backends, Excel throws for some), field missing from the data (silently ignored unless it is a static-series field), unregistered semantic type (silently kept; the encoding type is then inferred from the data as if the field were unannotated), invalid discrete option (dropped with a warning), static-series violations (throws).
5. One command per upstream release (`npm run generate`); a human reads the IR diff and tags. Manual work only when CI names a collision, an unmapped construct or a core member.
6. Trimming- and AOT-friendly for the spec part; the reflection row path is annotated.

**Non-goals**

- Rendering, compiling, previewing, caching, any renderer or browser concern. The SDK never imports a renderer.
- Template defaults and applicability (108 of 296 properties have no static default; 140 depend on a `check()` that needs resolved data).
- Required channels as Errors: templates do not declare them.
- Reproducing upstream data inference (temporal detection, the `year` name token); the SDK documents it and makes explicit annotation easy.
- Aliasing chart types across backends (`Donut Chart` vs Chart.js `Doughnut Chart`).
- Fetching `data.url` (upstream never does either).
- An MCP server of its own (decided): agents validate the SDK's JSON with upstream `flint-chart-mcp` `validate_chart` (companion, "AI and MCP readiness").

### 2. Upstream baseline (flint-chart 0.5.1)

Facts below are from the npm tarball of `flint-chart@0.5.1` (which ships `src/`), from runtime probes against it, and from `flint-chart-mcp@0.5.1`. `T:` refers to `dist/types-BWc1W3v4.d.ts`: `package.json` `types` points at `dist/index.d.ts`, nine lines of re-exports from that hashed file; a second hashed file (`types-2j8bDxoT.d.ts`, 88 lines) exists, so the generator follows the exports with the TypeScript checker and never globs `types-*.d.ts`. Unreleased commits on upstream `main` are noted only where they explain a design choice; the SDK follows npm releases only.

**Input shape** (`T:2916-3027`, `src/core/types.ts:1062-1166`). Six top-level keys, nothing else:

| Key | Required | Type |
|---|---|---|
| `data` | yes | `{values: any[]} \| {url: string}`; XOR only in the type; every assembler reads `values ?? []`; `url` is never fetched (empty chart on four backends, throw on Excel) |
| `semantic_types` | no | `Record<string, string \| SemanticAnnotation>` |
| `chart_spec` | yes | `chartType`, `title?`, `subtitle?`, `encodings`, `baseSize?`, `canvasSize?`, `chartProperties?` |
| `theme_spec` | no | `ThemeSpec \| string` (preset id); applied by Vega-Lite and Plotly (probe: output changes with a preset or a custom object, and an unknown preset throws, on those two only; `T:3013` saying "Vega-Lite only" is out of date). Excel ignores `theme_spec` and `options` entirely |
| `options` | no | `AssembleOptions`, 27 optional members |
| `field_display_names` | no | `Record<string, string>` |

`interaction_spec` exists only on unreleased `main`; it is not part of the 0.5.1 model.

**SemanticAnnotation** (`T:612-644`): `semanticType` (required; the only key read, camelCase; `semantic_type` is read nowhere), `intrinsicDomain?: [number, number]`, `unit?`, `divergingMidpoint?`, `sortOrder?: string[]`. Names are matched case-sensitively against a 44-entry registry (`SemanticTypes`, `getRegistryEntry = TYPE_REGISTRY[t] ?? UNKNOWN_ENTRY`). An unregistered name is kept verbatim; its registry lookup falls back to `UNKNOWN_ENTRY` and the encoding type is inferred from the data, so the output matches an unannotated field (not an explicit `"Unknown"`, which forces nominal). Silent either way. A field is an implicit Year when its name, split on camelCase and non-alphanumerics, contains the token `year`, and all non-empty values are integers 1500–2200 with at least two distinct values (`resolve-semantics.ts:68-83`; `fiscalYear [2020,2021]` is a Year, `yearly`, `years`, `year [1,2]` and `Year [2020,2020]` are not).

**chart_spec**. `chartType` is the exact display name (`"Line Chart"`, case-sensitive); unknown names throw on all five backends. `encodings` missing throws on all five; `encodings: {}` assembles on Vega-Lite, ECharts, Chart.js and Plotly and throws on Excel (no measure field). `ChartEncoding` (`T:1994-2001`): `field?`, `type?` (`quantitative|nominal|ordinal|temporal`), `aggregate?` (`count|sum|average|mean`), `sortOrder?` (`ascending|descending`), `sortBy?` (`x|y|color` or a JSON-encoded array string; Vega-Lite `console.warn`s and ignores anything else, the other backends ignore it silently, and some ECharts templates also honour `size` and `group`), `scheme?` (`default` = none). Raw encoding value: `ChartEncoding | string | (ChartEncoding|string)[]`; a string becomes `{field}`. Static-series arrays (`static-series.ts:114-206`) throw unless: exactly one array channel, on `x` or `y`; at least two entries, each with a unique `field`; when data has rows, each field in `Object.keys(data[0])`; no entry typed nominal/ordinal and no untyped entry whose semantic type or data infers nominal/ordinal; `color` not bound to a field. Excel reads an array encoding as an unbound channel, so a static series throws there when the template needs that channel (`y` of a Line Chart) and assembles otherwise (`color`; `probe-expectations.jsonl`). `chartProperties`: only declared discrete properties are checked (`invalid-option-value` warning drops the value; `coerced-option-label` info); binary and continuous values are not checked; undeclared keys pass. Host-state and encoding-action keys read even where undeclared: `facetColumns`, `pivot`, `sort`, `colorScheme`, `arrange`, `chartType`, `xAxisType`, `yAxisType`, `showValueLabels`, `showTextLabels` (each member confirmed in source; the list is not provably closed; `xAxisType`, `yAxisType`, `showValueLabels`, `showTextLabels` and `sort` are also declared properties on some templates). `baseSize` default 400x320 clamped to `canvasSize` on Vega-Lite, ECharts, Chart.js and Plotly; on Vega-Lite and Plotly a theme preset's `compileDefaults.baseSize` replaces the default (`powerbi` 480x280); Excel defaults to 480x320 and does not clamp.

**Channels**. The core `channels` constant has 26 names (`x y x2 y2 id color opacity size shape strokeDash column row latitude longitude radius detail group open high low close angle order metric value goal`). The union of the constant and every template's declared channels has 27 (adds `theta`, declared by Excel Pie and Donut); 28 only if `channelGroups` (`text`) is also counted. `radius` and `angle` are in the constant but declared by no template; `text` is declared by none. The count is derived at extraction time, never hard-coded. Encoding keys are untyped; unknown or undeclared channels are ignored on Vega-Lite, ECharts, Chart.js and Plotly; Excel throws on `column`/`row` (faceting) and on channels its template `validate()` rejects (for example `shape`).

**Registries** (runtime, via `import 'flint-chart'`): 48 chart types across 5 backends (Vega-Lite 36, ECharts 37, Chart.js 22, Plotly 38, Excel 18); 11 on all five; 18 of the 40 multi-backend types have different channel sets per backend; 296 `ChartPropertyDef`s under 64 keys (`discrete` with `options[{value,label}]` 83, `binary` 135, `continuous` with `min/max/step` 78); 46 distinct (template, key) discrete pairs. Within one template, option sets differ across backends only for `stackMode` (Stacked Bar Chart, Area Chart) and `filled` (Radar Chart, binary on Vega-Lite and discrete `[true,false]` on ECharts, Chart.js and Plotly); `interpolate` (Range Area vs others), `layout` (KPI Card vs Network Graph) and `orient` (Funnel, Tree, Sankey) differ between templates, not backends. Discrete values are strings (289), `undefined` meaning "Default, omit the key" (22), booleans (10), one number (`labelRotate: 0`, Sunburst@echarts), and `[lon,lat]` arrays (38, `projectionCenter`). Excel templates have no properties, carry `typeMapping`, and 16 of 18 (all except Pie and Donut) have a `validate(ctx)` that returns a message on unsupported input. Ten theme presets (`nyt economist swiss nature mckinsey datawrapper powerbi powerbi-light pop cartoon`), all pure JSON.

**AssembleOptions** (`T:3032-3300`): 27 optional members; `continuousMarkCrossSection` is `number | {x, y, elasticity?, maxStretch?, seriesCountAxis?: 'x'|'y'|'auto'}`; `facetFixedPadding: {width, height}`. Real `maxStretch` default is 1.5 (`compute-layout.ts:178`), the doc says 2. `maxStretchX/Y`, `bandStepFitCapacityX/Y`, `facetColumns` are overwritten by the four layout backends; Excel reads no `input.options` at all. Merge order `{backend defaults, ...options, ...template.paramOverrides}`. The undeclared `options.background` (string) is read via `as any` as the theme host surface (`hostSurface?: string`) by Vega-Lite and Plotly only.

**Null handling** (probes): unknown keys at every level are accepted silently. Throws on all five: `data: null`, missing `encodings`, unknown `chartType`. Backend-specific throws: `encodings.<ch> = null` on Vega-Lite, ECharts, Chart.js and Plotly (Excel accepts it); `theme_spec: null` and an unknown preset on Vega-Lite and Plotly only; static-series violations on the four layout backends; Excel with no `values`, Excel-unsupported types or facets, and an Excel category/measure field that cannot be resolved (`field: null`). Null tolerated as absent everywhere else (`semantic_types`, `options`, `field_display_names`, `chartProperties`, `title`, `baseSize`, `canvasSize`, an annotation value, `encoding.type`). Implication: omit nulls in the spec part.

**Row data**: static series and key detection use `Object.keys(data[0])`; a key missing from `data[0]` throws only when it is a static-series field (and only when data has rows); a normal encoding whose field is absent from `data[0]` assembles on all five; an explicit `null` passes; `null` in a temporal column becomes the string `"null"`. Inference (`src/core/semantic-types.ts:255-273`): booleans nominal; anything numeric under `+v` (including numeric strings, `''`/whitespace strings and single-number arrays) quantitative; strings starting with a digit or month name that `Date.parse` accepts temporal (`"1.02:03:04"` included); else nominal; nested objects nominal; an all-null column nominal. JS numbers are doubles.

**Warnings**: the four layout assemblers return `_warnings: {severity, code, message, channel?, field?}[]`; `assembleExcel` returns a top-level `warnings` array of the same shape (for example `excel-radar-fill-unsupported`). Six codes exist: `overflow`, `invalid-option-value`, `coerced-option-label`, `negative-values-pyramid`, `too-many-groups-pyramid`, `excel-radar-fill-unsupported`. Severity `error` is never emitted; failures are thrown `Error`s without a code.

**Validation**: `flint-chart@0.5.1` has no validator. `validateChart` lives only inside `flint-chart-mcp@0.5.1` (`dist/server.js`, internal, not exported; reachable as the MCP tool `validate_chart` for vegalite, echarts and chartjs, and as `flint-chart-mcp/render`'s `validateInput`, which runs no channel checks; its `valid` reduces to "did not throw"; 187 packages including native modules). The MCP `prepareInput` additionally enforces: every row an object, `chartType` a string, `encodings` an object, ≥ 1 row, ≤ 100 000 rows, non-empty `encodings`, fields in the union of row keys, sizes ≤ 4000 px, and, only when a backend is given, declared channels, the x/y heuristic and KPI Card `metric`+`value`. Upstream `main` adds a public `flint-chart/validate` with snake_case codes (`assembly_failed`, `interactions_ignored`, `invalid_interaction_spec`, `unknown_semantic_type`); unreleased.

**Fixtures**: `flint-chart/test-data` exports `TEST_GENERATORS` producing 934 `TestCase`s (128 groups, 48 chart types; append-only across 0.4.0 → 0.4.1 → 0.5.0 → 0.5.1 by `group|index|title`; `main` unverified). Deterministic only with `TZ=UTC` and `Math.random` replaced by a seeded Park-Miller generator. Values are ints, floats and strings only; field names include spaces and parentheses (65 such names). The `TestCase → ChartAssemblyInput` mapping of §7.9 assembles without a throw on every non-Excel backend that has the template (vegalite 886, echarts 866, chartjs 743, plotly 894 cases); Excel throws on 219 of its 695 applicable cases (130 facets, 46 non-quantitative XY, 43 other `validate()` reasons). The 0.5.1 `encodingMap` entries carry only `fieldID` and `dtype`. Semantic equivalence with upstream's own inputs is **unverified** (the upstream git tag has 705 `shared/test-data/*/input.json` files not in the tarball; see §7.9). `metadata.levels → sortOrder` is a design choice still open (2453 field metadata entries carry `levels`).

**Change history 0.1.0 → 0.5.1** (12 npm versions, 11 transitions, no prereleases): 0 breaking changes to the JSON shape; every d.ts addition optional; the six-key core changed in 2 transitions (`aggregate` gained `mean` in 0.1.3; `title`/`subtitle`, `theme_spec` and `SemanticAnnotation.divergingMidpoint` in 0.5.0); `AssembleOptions` grew 18 → 22 (0.4.0) → 24 (0.5.0) → 27 (0.5.1). Registry removals in 3 transitions, 7 items, none makes assembly throw: `Rose Chart@vegalite innerRadius` (0.2.2, a patch), `Bar Chart@vegalite` channel `group`, `Stacked Bar@vegalite+echarts stackMode=layered` and `Sparkline@vegalite independentYAxis` (0.3.0, a minor), `Waterfall@vegalite` and `Heatmap@vegalite showTextLabels` (0.5.0). All 7 are per-template: `innerRadius`, `group`, `independentYAxis`, `showTextLabels` and `stackMode: layered` still exist on other templates in 0.5.1, so retirement is keyed by template and backend. A removed discrete option value is not silent: upstream emits `invalid-option-value`. No template, preset or semantic type was ever removed. The CHANGELOG is unreliable; git tags cover 6 of 12 versions (`0.1.1, 0.2.1, 0.3.0, 0.4.0, 0.5, 0.5.1`); npm releases drive the sync.

### 3. Architecture

**One assembly, four layers.** Dependencies point downward only.

```
Graspea.Flint.dll (net8.0)

Fluent        Chart (partial), ChartBuilder<TSelf,TRow>, GenericChartBuilder<TRow>   [hand-written]
              48 facades, per-template enums, Chart.<Name>() entries                [generated]
     |
Validation    ChartValidator, ValidationResult, ValidationIssue, IssueSeverity,
              FlintValidationException (carries Issues)                           [hand-written engine]
     |
Model+Json    ChartAssemblyInput, ChartData, ChartSpec, ChartEncoding, EncodingValue,
              SemanticAnnotation, ChartTheme, ChartSize, ContinuousMarkCrossSection,
              RowOptions, SortBy, string-backed structs, FlintJson, converters,
              RowMaterializer                                                        [hand-written]
              AssembleOptions (+nested), Theming.* (56 object types), struct constants [generated]
     |
Tables        internal CatalogTables (FrozenDictionary), FlintUpstream, FlintBackend [generated + hand-written]
```

Model and serialisation are one layer: model types carry `[JsonConverter]` attributes and `ChartData.FromRows` uses the row materialiser. Generated code never contains logic: a constant, a DTO property with attributes, a table row, or a one-line delegation to a hand-written core method.

**Source layout**

```
README.md           also the NuGet README (companion, "AI and MCP readiness" §1 owns the outline)
AGENTS.md           hand-written, for contributors
CONTRIBUTING.md, LICENSE, THIRD-PARTY-NOTICES.md, icon.png
.github/workflows/  ci.yml, upstream-sync.yml, release.yml
src/Graspea.Flint/
  Model/            core types, RowOptions.cs, SortBy.cs, Json/ (FlintJson.cs, FlintJsonContext.cs, Converters/*.cs, RowMaterializer.cs)
  Validation/       ChartValidator.cs, Rules/*.cs, ValidationResult.cs, FlintValidationException.cs, Codes.cs
  Fluent/           Chart.cs, ChartBuilder.cs, GenericChartBuilder.cs
  Generated/        never edited; one folder per emitter: Constants/ Options/ Theming/ Json/ (FlintMapJsonContext, written by the options emitter) Tables/ Builders/ Enums/ Entries/; catalog.json
  PublicAPI.Shipped.txt, PublicAPI.Unshipped.txt   (every public symbol: hand-written, including FlintValidationException, and generated)
generator/          Node/TS, flint-chart pinned exactly (see §7); ir/flint-ir.json; src/emit/docs.ts
tests/              Graspea.Flint.Tests, Graspea.Flint.ContractTests, fixtures/flint-chart@<ver>/, contract-node/
samples/            Graspea.Flint.Samples (console; `dotnet run` writes samples/out/*.json); Graspea.Flint.Samples.Aot for the AOT smoke publish
docs/               row-data.md, validation-codes.md, versioning.md, generator.md, fixtures.md, deprecations.md
docs/package/AGENTS.md   generated in full, packed at the nupkg root
docs/assets/social-preview.png
Directory.Build.props, Directory.Packages.props, nuget.config, global.json, .gitattributes, .nvmrc
```

**Namespaces**

| Namespace | Contents |
|---|---|
| `Graspea.Flint` | core model, `AssembleOptions`, string-backed structs and their constants, `Chart`, all builders, per-template enums, `RowOptions`, `SortBy`, `FlintUpstream`, `FlintBackend` |
| `Graspea.Flint.Theming` | generated `ThemeSpec` tree; `ChartTheme` and `ThemePreset` stay in the root |
| `Graspea.Flint.Serialization` | `FlintJson` (public); the context and the converter-owned DTOs are `internal`; the converters are public infrastructure marked `[EditorBrowsable(Never)]`, because a host source-generated `JsonSerializerContext` must instantiate every `[JsonConverter]` type it meets and reports SYSLIB1220 for an inaccessible one (found by the first generator run) |
| `Graspea.Flint.Validation` | validator and result types |

No type is named after a namespace segment (CA1724). Per-template enums (46 on the 0.5.1 IR, one per distinct template-and-key discrete pair) live in the root with the template prefix (`LineChartInterpolate`); nesting them in the builder would collide with the method of the same name. There is no public catalog API in M1: `CatalogTables` is internal, used by the validator and `SemanticType.IsRegistered`; a public read API is an additive change for when a consumer asks. The catalog is delivered as the struct constants and as the loose `catalog/catalog.json` in the nupkg, an emitted projection of the IR `catalog` node.

**Package boundary.** `Graspea.Flint` is one assembly; the fluent layer adds no dependency, so splitting it would only add a package to version-lock. `Graspea.Flint.AspNetCore` (after the first stable release, not a gate for it) depends on `Graspea.Flint` only and adds `IResult`/`ActionResult` helpers that serialise with `FlintJson.Options`.

**Repository files**

| File | Content |
|---|---|
| `global.json` | `{"sdk": {"version": "10.0.100", "rollForward": "latestFeature"}}`; the library targets `net8.0`, the test projects target `net10.0;net8.0` |
| `Directory.Build.props` | build half: `TargetFramework=net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, `ContinuousIntegrationBuild` when `CI` is set, `GenerateDocumentationFile=true`, `EnablePackageValidation=true`, `EnforceExtendedAnalyzerRules=true`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true` (SourceLink comes with the .NET SDK); `IsAotCompatible=true` is set in `src/Graspea.Flint/Graspea.Flint.csproj` only, because the trim analyzers would fail the reflection-based test and sample code; a target that strips the generated-section markers from `README.md` into `$(IntermediateOutputPath)README.pack.md` before pack, so packing needs no Node (it uses `File.ReadAllText` and `Regex.Replace` property functions, not `ReadLinesFromFile`, which drops blank lines and leading whitespace). Package metadata: see the companion, Discoverability → "NuGet package metadata" (owner); `PackageId`, `Title` and `Description` are set per `.csproj` |
| `Directory.Packages.props` | `ManagePackageVersionsCentrally=true`; pins for NUnit 4, NUnit3TestAdapter, Microsoft.NET.Test.Sdk, Microsoft.CodeAnalysis.PublicApiAnalyzers (test/analyzer only; the shipped package references nothing) |
| `nuget.config` | `<clear/>` then nuget.org only |
| `.gitattributes` | `*.g.cs text eol=lf`, `*.json text eol=lf`, `*.jsonl text eol=lf`, `tests/fixtures/** linguist-generated=true`, `generator/ir/*.json linguist-generated=true` (generated `.cs` files stay in language statistics; reason in the companion, Discoverability) |
| `.nvmrc`, `generator/package.json` `engines` | `.nvmrc` holds the exact Node 24 LTS version and every workflow uses `node-version-file: .nvmrc`; `engines` holds the matching major; a generator `node:test` asserts that the two agree |

**docs/**: `row-data.md` (§5), `validation-codes.md` (§6 tables), `versioning.md` (§8), `generator.md` (§7.7, §9; the regeneration runbook that the README "Regenerate from a new flint-chart release" section and `CONTRIBUTING.md` link to), `fixtures.md` (provenance, mapping, cross-check result), `deprecations.md` (one anchor per retired member, the `UrlFormat` target). There is no `docs/README.md`: the version-matching rule and the two API levels with their examples live in the root README (companion §1). Agent-facing docs (decided): the README "For AI agents" section, XML docs, and `docs/package/AGENTS.md` generated in full and packed at the nupkg root; the upstream `flint-chart-author` skill is linked at the pinned tag (or at the `gitHead` commit when no tag exists), never vendored.

### 4. Public API

#### 4.1 Low-level model (primary)

Every model class: `sealed partial`, init-friendly mutable properties, `[JsonNumberHandling(JsonNumberHandling.Strict)]` on the type (a host `NumberHandling = WriteAsString` would otherwise quote every number, spike), `[JsonPropertyName]` on every member, `[JsonIgnore(Condition = WhenWritingNull)]` on every optional member, `[JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; set; }` last. `JsonObject` as extension data writes invalid JSON (`{"A":1,{"b":2}}`, reproduced on runtimes 8, 9 and 10 with reflection and with source generation) and `Dictionary<string, JsonNode?>` is rejected by the source generator (SYSLIB1036), so `Dictionary<string, JsonElement>` is the type everywhere, hand-written and generated. Every map property carries a closed-generic `[JsonConverter(typeof(VerbatimKeyDictionaryConverter<T>))]` (`<string>`, `<SemanticAnnotation>`, `<EncodingValue>`, `<JsonNode?>`), never an open factory, so no `MakeGenericType` and no IL3050 suppression; keys are written as-is under any `DictionaryKeyPolicy`. Union-typed members carry `[JsonConverter]` on the **type**; for those types the converter owns the wire shape (§5.1).

```csharp
namespace Graspea.Flint;

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartAssemblyInput
{
    [JsonPropertyName("data")]                public required ChartData Data { get; set; }
    [JsonPropertyName("semantic_types"), JsonConverter(typeof(VerbatimKeyDictionaryConverter<SemanticAnnotation>))]
                                              public Dictionary<string, SemanticAnnotation>? SemanticTypes { get; set; }
    [JsonPropertyName("chart_spec")]          public required ChartSpec ChartSpec { get; set; }
    [JsonPropertyName("theme_spec")]          public ChartTheme? ThemeSpec { get; set; }
    [JsonPropertyName("options")]             public AssembleOptions? Options { get; set; }
    [JsonPropertyName("field_display_names"), JsonConverter(typeof(VerbatimKeyDictionaryConverter<string>))]
                                              public Dictionary<string, string>? FieldDisplayNames { get; set; }
    [JsonExtensionData]                       public Dictionary<string, JsonElement>? Extensions { get; set; }
}

[JsonConverter(typeof(ChartDataConverter))]
public sealed partial class ChartData
{
    public JsonArray? Values { get; }                       // materialised rows, nulls explicit
    public string? Url { get; }
    public IReadOnlyList<string> FirstRowKeys { get; }      // what Object.keys(data[0]) sees; empty when no rows
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    [RequiresUnreferencedCode("Row types are serialised by reflection; use the JsonTypeInfo overload for trimmed or AOT hosts.")]
    [RequiresDynamicCode("…")]
    public static ChartData FromRows<TRow>(IEnumerable<TRow> rows, RowOptions? options = null);   // throws ArgumentException when a row property may drop nulls (§5.3)
    public static ChartData FromRows<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo);   // same check
    public static ChartData FromJson(JsonArray values);
    public static ChartData FromUrl(string url);
}

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartSpec
{
    [JsonPropertyName("chartType")]       public required ChartType ChartType { get; set; }
    [JsonPropertyName("title")]           public string? Title { get; set; }
    [JsonPropertyName("subtitle")]        public string? Subtitle { get; set; }
    [JsonPropertyName("encodings"), JsonConverter(typeof(VerbatimKeyDictionaryConverter<EncodingValue>))]
                                          public Dictionary<string, EncodingValue> Encodings { get; set; } = new();
    [JsonPropertyName("baseSize")]        public ChartSize? BaseSize { get; set; }
    [JsonPropertyName("canvasSize")]      public ChartSize? CanvasSize { get; set; }
    [JsonPropertyName("chartProperties"), JsonConverter(typeof(VerbatimKeyDictionaryConverter<JsonNode?>))]
                                          public Dictionary<string, JsonNode?>? ChartProperties { get; set; }   // open namespace
    [JsonExtensionData]                   public Dictionary<string, JsonElement>? Extensions { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed partial class ChartEncoding
{
    [JsonPropertyName("field")]     public string? Field { get; set; }
    [JsonPropertyName("type")]      public EncodingType? Type { get; set; }
    [JsonPropertyName("aggregate")] public Aggregate? Aggregate { get; set; }
    [JsonPropertyName("sortOrder")] public SortOrder? SortOrder { get; set; }
    [JsonPropertyName("sortBy")]    public string? SortBy { get; set; }
    [JsonPropertyName("scheme")]    public string? Scheme { get; set; }
    [JsonExtensionData]             public Dictionary<string, JsonElement>? Extensions { get; set; }
}

[JsonConverter(typeof(EncodingValueConverter))]
public sealed partial class EncodingValue
{
    public IReadOnlyList<ChartEncoding> Encodings { get; }
    public bool IsSeries { get; }                                   // true <=> wire form is an array
    public static EncodingValue Single(ChartEncoding e);
    public static EncodingValue Series(params ChartEncoding[] e);   // strings are not accepted here; the builder has Series(Channel, params string[])
    public static implicit operator EncodingValue(string field);    // Single(new() { Field = field })
    public static implicit operator EncodingValue(ChartEncoding e);
}

[JsonConverter(typeof(SemanticAnnotationConverter))]
public sealed partial class SemanticAnnotation
{
    public required SemanticType Type { get; set; }                 // wire key "semanticType"
    public double[]? IntrinsicDomain { get; set; }
    public string? Unit { get; set; }
    public double? DivergingMidpoint { get; set; }
    public string[]? SortOrder { get; set; }
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.Strict)]
public readonly record struct ChartSize(
    [property: JsonPropertyName("width")]  double Width,
    [property: JsonPropertyName("height")] double Height);

[JsonConverter(typeof(ChartThemeConverter))]
public sealed partial class ChartTheme
{
    public ThemePreset? Preset { get; }
    public Theming.ThemeSpec? Spec { get; }
    public static implicit operator ChartTheme(ThemePreset preset);
    public static implicit operator ChartTheme(Theming.ThemeSpec spec);
}

[JsonConverter(typeof(ContinuousMarkCrossSectionConverter))]
public sealed partial class ContinuousMarkCrossSection
{
    public double? Uniform { get; }
    public double? X { get; } public double? Y { get; } public double? Elasticity { get; } public double? MaxStretch { get; }
    public SeriesCountAxis? SeriesCountAxis { get; }
    public static implicit operator ContinuousMarkCrossSection(double value);
    public static ContinuousMarkCrossSection Axes(double x, double y, double? elasticity = null, double? maxStretch = null, SeriesCountAxis? seriesCountAxis = null);
}

public static class SortBy
{
    public static string ExplicitOrder(params string[] order);     // JSON-encoded array string
}
```

**String-backed structs.** Every upstream string vocabulary is open at runtime, so none is a closed C# enum. Each is a `readonly partial record struct` over `string` with an explicit constructor and a get-only property, not a positional record: the positional form let `ChartType.LineChart with { Name = null! }` bypass the argument check (spike); with a get-only property `with` fails to compile (CS0200). The constructor throws `ArgumentException` on a null or empty string; `default(ChartType)` cannot be serialised (the converter throws `JsonException`); reading `""` or a non-string throws `JsonException` (§5.1). Generated constants and a converter write the string verbatim; an unknown value round-trips untouched and the validator reports it.

```csharp
[JsonConverter(typeof(ChartTypeConverter))]
public readonly partial record struct ChartType
{
    public ChartType(string name) { … }     // ArgumentException on null/empty
    public string Name { get; }
    public override string ToString() => Name;
    public static readonly ChartType LineChart = new("Line Chart");   // generated, 48 of these
}
```

| Struct | Constants (generated from) | Validator code for an unknown value |
|---|---|---|
| `ChartType(string name)` | 48 chart types (`LineChart`, `KpiCard`, `DonutChart`, `DoughnutChart`, `Map`) | `unknown_chart_type` (Error) |
| `Channel(string name)` | union of the core `channels` constant and every template's declared channels on every backend: 27 names on 0.5.1 (includes `Theta`; `Text` is declared by no template and is not emitted); implicit operator to `string` | `unsupported_channel` (Warning) |
| `SemanticType(string name)` | 44 registry names verbatim (`YearMonth`, `ID`, `ZipCode`), except `Name`, which would collide with the struct's `Name` property and is emitted as `NameType` (rename in `overrides.json`); `IsRegistered`; implicit operator to `SemanticAnnotation` | `unknown_semantic_type` (Warning) |
| `ThemePreset(string id)` | 10 presets (`Nyt`, …, `PowerBiLight`) | `unknown_theme_preset` |
| `EncodingType`, `Aggregate`, `SortOrder`, `SeriesCountAxis` | IR literal unions (`EncodingType.Temporal`, `Aggregate.Mean`, `SortOrder.Descending`, `SeriesCountAxis.Auto`) | `invalid_literal` (Info) |

`FlintBackend { VegaLite, ECharts, ChartJs, Plotly, Excel }` is a hand-written enum; an IR cross-check test fails when the backend list changes. `FlintUpstream.Version` is a generated constant (`"0.5.1"`).

`AssembleOptions` (27 generated properties) references the hand-written `ContinuousMarkCrossSection` through `overrides.json`. Ignored options (`maxStretchX/Y`, `bandStepFitCapacityX/Y`, `facetColumns`; on Excel every option) are generated as declared with a doc note. `options.background` stays reachable through `Extensions`.

```csharp
var input = new ChartAssemblyInput
{
    Data = ChartData.FromRows(sales),
    SemanticTypes = new()
    {
        ["month"]   = SemanticType.YearMonth,
        ["revenue"] = new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD" },
    },
    ChartSpec = new ChartSpec
    {
        ChartType = ChartType.LineChart,
        Encodings = { ["x"] = "month", ["y"] = "revenue", ["color"] = new ChartEncoding { Field = "region" } },
        BaseSize = new ChartSize(800, 400),
        ChartProperties = new() { ["interpolate"] = "monotone" },
    },
    ThemeSpec = ThemePreset.Economist,
};
string json = FlintJson.Serialize(input);
```

#### 4.2 Fluent API (convenience over the model)

Decided: generated per-template builders (`Chart.Line(rows).X(...).Y(...)...`), channels as the union across backends with `Validate(backend)` reporting backend gaps, data at the entry point, `BaseSize`/`CanvasSize` instead of `Size`, per-template enums for discrete properties, a hand-written CRTP core `ChartBuilder<TSelf,TRow>` plus `GenericChartBuilder` through `Chart.Of`. The fluent API is designed now because it freezes at Stable; it ships in M2. Two deliberate deviations from the original sketch, both forced by C#: data enters at the entry point (`Chart.Line(rows)`, not `Chart.Line().Data(rows)`), because a chain cannot change its row type midway without a second class per template; and the size setters are `BaseSize`/`CanvasSize`, not `Size`, because `size` is a channel on Scatter, Bubble and others and the channel keeps its natural name.

**Entry point**

```csharp
public static partial class Chart
{
    // generated, one quadruple per template (48)
    [RequiresUnreferencedCode, RequiresDynamicCode]
    public static LineChartBuilder<TRow>   Line<TRow>(IEnumerable<TRow> rows, RowOptions? options = null);
    public static LineChartBuilder<TRow>   Line<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo);
    public static LineChartBuilder<object> Line(ChartData data);                 // pre-built values or {url}
    [Obsolete("A string is IEnumerable<char>; use Chart.Line(ChartData.FromUrl(url)).", error: true)]
    public static LineChartBuilder<object> Line(string url);                     // wins overload resolution, CS0619

    // hand-written escape hatch for templates the catalog does not know
    [RequiresUnreferencedCode, RequiresDynamicCode]
    public static GenericChartBuilder<TRow>   Of<TRow>(ChartType chartType, IEnumerable<TRow> rows, RowOptions? options = null);
    public static GenericChartBuilder<TRow>   Of<TRow>(ChartType chartType, IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo);
    public static GenericChartBuilder<object> Of(ChartType chartType, ChartData data);
    [Obsolete("…", error: true)]
    public static GenericChartBuilder<object> Of(ChartType chartType, string url);
}
```

A literal `null` or `default` second argument (`Chart.Line(rows, null)`) is ambiguous between `RowOptions?` and `JsonTypeInfo<TRow>` and does not compile (CS0121, spike); omit the argument or pass `RowOptions.Default`. This is documented and accepted: `null` is never a meaningful argument. The same holds for `Series(Channel.Y)` with no fields and `Series(Channel.Y, null)`: both are ambiguous between the `params string[]` and `params Expression<…>[]` overloads and do not compile (CS0121, review probe); a series needs at least two fields anyway, and the `Series` remarks say so. Without the error-marked overload, `Chart.Line("https://…")` compiles silently as `LineChartBuilder<char>` and writes `"values":["h","t","t","p",…]` (spike), hence the `[Obsolete(error: true)] Line(string)` per entry point. Dictionary rows are `TRow = IReadOnlyDictionary<string, object?>`; lambda overloads are then unused. `Chart.Of(default, …)` and `new ChartType(null!)` throw `ArgumentException` at the call site, so `Build()` can never produce a document without a chart type.

**Hand-written core**

```csharp
public abstract class ChartBuilder<TSelf, TRow> where TSelf : ChartBuilder<TSelf, TRow>
{
    internal ChartBuilder(ChartType chartType, ChartData data, JsonTypeInfo<TRow>? rowTypeInfo) { … }

    // channels — raw form; facades delegate here; undeclared channels are reachable here
    public TSelf Encode(Channel channel, string field, SemanticType? semanticType = null);
    public TSelf Encode(Channel channel, Expression<Func<TRow, object?>> selector, SemanticType? semanticType = null);
    public TSelf Encode(Channel channel, ChartEncoding encoding);                           // X(null) binds here and throws ArgumentNullException
    public TSelf Encode(Channel channel, EncodingValue value);
    public TSelf Series(Channel channel, params string[] fields);                        // static series, x or y only
    public TSelf Series(Channel channel, params Expression<Func<TRow, object?>>[] selectors);
    public TSelf Encoding(Channel channel, Action<ChartEncoding> configure);              // aggregate, sort, scheme

    // chartProperties — raw form
    public TSelf Property(string key, JsonNode? value);                                   // null removes the key
    public TSelf Property(string key, string? value);                                     // Property(key, null) binds here (spike); null removes the key
    public TSelf Property(string key, bool value);
    public TSelf Property(string key, double value);

    public TSelf Title(string? title);
    public TSelf Subtitle(string? subtitle);
    public TSelf BaseSize(double width, double height);
    public TSelf CanvasSize(double width, double height);
    public TSelf Annotate(string field, SemanticAnnotation annotation);
    public TSelf Annotate(Expression<Func<TRow, object?>> selector, SemanticAnnotation annotation);
    public TSelf DisplayName(string field, string displayName);
    public TSelf DisplayName(Expression<Func<TRow, object?>> selector, string displayName);
    public TSelf Theme(ChartTheme theme);
    public TSelf Options(Action<AssembleOptions> configure);
    public TSelf Configure(Action<ChartAssemblyInput> configure);                         // applied at Build()

    public ChartAssemblyInput Build();                                                    // never validates; builder state copied per call, action-assigned objects as given
    public ValidationResult Validate(FlintBackend? backend = null);                       // ChartValidator over Build(); the only validation entry
    public string ToJson();                                                               // FlintJson.Serialize(Build()); never validates
}
```

Core method names are reserved: `Encode Series Encoding Property Title Subtitle BaseSize CanvasSize Annotate DisplayName Theme Options Configure Build Validate ToJson`. `Encode`/`Annotate` rather than `Channel`/`SemanticType` so that `Channel.X` and `SemanticType.YearMonth` resolve to the structs inside the builder and in consumer extension methods. Rules that make the core freezable: internal constructor, no interfaces, no virtuals, every facade method a one-liner over a core method, no optional parameters outside the fixed overload set above (RS0026 is therefore not enabled, see §8.6). Builders are mutable and not thread-safe. The spike compiled this shape on net8.0: the chain below produces the expected JSON, `X(new() { Field = "month" })` binds to `ChartEncoding`, and the extension method `WithHouseStyle<TSelf,TRow>` infers its types and returns `LineChartBuilder<Sale>`.

**Generated facades**

```csharp
public sealed partial class LineChartBuilder<TRow> : ChartBuilder<LineChartBuilder<TRow>, TRow>
{
    internal LineChartBuilder(ChartData data, JsonTypeInfo<TRow>? rowTypeInfo) : base(ChartType.LineChart, data, rowTypeInfo) { }

    // channels: union over the backends that have this template
    public LineChartBuilder<TRow> X(string field, SemanticType? semanticType = null)                             => Encode(Channel.X, field, semanticType);
    public LineChartBuilder<TRow> X(Expression<Func<TRow, object?>> selector, SemanticType? semanticType = null) => Encode(Channel.X, selector, semanticType);
    public LineChartBuilder<TRow> X(ChartEncoding encoding)                                                     => Encode(Channel.X, encoding);
    public LineChartBuilder<TRow> Y(…) …   Color(…) …   Opacity(…) …   Column(…) …   Row(…) …
    public LineChartBuilder<TRow> StrokeDash(…) …    // vegalite, plotly, excel; Validate(backend) says so elsewhere
    public LineChartBuilder<TRow> Detail(…) …        // vegalite only

    // discrete -> per-template enum; binary -> bool; continuous -> double
    // Line Chart properties on 0.5.1: interpolate, showPoints, independentYAxis, logScale_x/y, includeZero_x/y, xAxisType, yAxisType
    public LineChartBuilder<TRow> Interpolate(LineChartInterpolate value) => Property("interpolate", LineChartInterpolateValues.ToJson(value));
    public LineChartBuilder<TRow> ShowPoints(bool value)                  => Property("showPoints", value);
    public LineChartBuilder<TRow> LogScaleX(bool value)                   => Property("logScale_x", value);
    public LineChartBuilder<TRow> IncludeZeroY(bool value)                => Property("includeZero_y", value);
    // a continuous example lives on another template: RadarChartBuilder<TRow>.StrokeWidth(double) (vegalite)
}

public enum LineChartInterpolate { Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom }   // identical on the 4 backends
internal static class LineChartInterpolateValues { internal static JsonNode? ToJson(LineChartInterpolate v) { … } }   // Default -> null (key removed)
```

Template-only channels hold by construction (`Goal()` exists on `BulletChartBuilder` and `KpiCardBuilder` only). When a key has different option sets per backend, the enum is the union and `Validate(backend)` reports `invalid_option_value` for a member that backend lacks. Encoding actions (`sort`, `colorScheme`) and host-state keys are not emitted as methods; they are set with `Property(key, …)`.

**Collision rule and the 0.5.1 names** (deterministic, in `naming.ts`, §7.6): when a channel and a property share a key on one template, the channel keeps `Pascal(key)` and the property becomes `Pascal(key) + "Value"`; a channel or property that maps to a reserved core name gets the suffix `Channel`/`Value`; only residual collisions fail generation. Expected results on the 0.5.1 IR from the registry facts: `Opacity(…)` channel plus `OpacityValue(double)` on Scatter Plot, Area Chart and Bubble Chart, the only templates where a channel and a property share a key (`shape` is a property only on Radar Chart, which declares no `shape` channel, so there is no `ShapeValue`); Funnel Chart's declared `sort` property (ECharts) gets `Sort(...)`, and the `sort` encoding action is not emitted, so there is no collision. The complete list is produced by the first generator run and checked into `docs/generator.md` before the preview tag; it is listed as an open item in §11, since the rules were not executed against the IR while writing this document.

**Usage**

```csharp
record Sale(DateOnly Month, decimal Revenue, decimal Cost, string Region);

var input = Chart.Line(sales)
    .X(r => r.Month,   SemanticType.YearMonth)      // field "month" via RowOptions.Default (camelCase)
    .Y(r => r.Revenue, SemanticType.Amount)
    .Color(r => r.Region)
    .Interpolate(LineChartInterpolate.Monotone)
    .Title("Revenue by region")
    .BaseSize(800, 400)
    .Theme(ThemePreset.Economist)
    .Build();

Chart.Line(sales)
    .X(r => r.Month, SemanticType.YearMonth)
    .Series(Channel.Y, r => r.Revenue, r => r.Cost)                       // y: [{field:"revenue"},{field:"cost"}]
    .Encoding(Channel.X, e => e.SortBy = SortBy.ExplicitOrder("Jan", "Feb"))
    .Validate(FlintBackend.VegaLite).ThrowIfInvalid();
```

Output of the first chain:

```json
{"data":{"values":[{"month":"2024-01-01","revenue":1200.50,"cost":800.00,"region":"EU"}]},
 "semantic_types":{"month":"YearMonth","revenue":"Amount"},
 "chart_spec":{"chartType":"Line Chart","title":"Revenue by region",
   "encodings":{"x":{"field":"month"},"y":{"field":"revenue"},"color":{"field":"region"}},
   "baseSize":{"width":800,"height":400},
   "chartProperties":{"interpolate":"monotone"}},
 "theme_spec":"economist"}
```

**Extension points**: the raw core setters; consumer extension methods over the CRTP base (`static TSelf WithHouseStyle<TSelf,TRow>(this ChartBuilder<TSelf,TRow> b) where TSelf : ChartBuilder<TSelf,TRow>`); `Chart.Of(new ChartType("Image Chart"), rows)` for unknown templates; every generated type is `partial`.
