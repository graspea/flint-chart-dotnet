## SDK design, part 5: chart data delivery to the browser

Status: implemented on the development branch and unreleased (`CHANGELOG.md`, section `[Unreleased]`), on top of `v1.0.1.1-preview.1`; the owner accepted ADR 16 on 2026-11-08. The implementation follows the recommendation of every decision point of §14.10, which the owner accepted with ADR 16, and the owner's outcome of D9.6 (streamed parts in v1) and D9.11 (no npm client, README guidance only), settled earlier that day. M0 (§14.8.1) is done: M0a, the `scheme` union, and M0b, the Image-Charts backend, on the owner's decisions of 2026-11-08. It builds on owner decision 15 (index), which part 4 designs and `0.5.1.1-preview.1` ships (`CHANGELOG.md`, section `[0.5.1.1-preview.1]`), and it is owner decision 16 in the index's dated decision table (`00-index.md:32-38`): "fixed chart definitions reach the browser as one cached manifest of data-free specs, the rows of a chart group follow in one response per group, both written by a `Graspea.Flint.Delivery` namespace in the core package under a wire contract v1 that is frozen at its first release". The requirements are in the PRD (part 10, [10-prd-chart-data-delivery.md](10-prd-chart-data-delivery.md): host needs N1-N11, FR-1 to FR-14, NFR-1 to NFR-9) and the decisions in the ADR (part 11, [11-adr-chart-data-delivery.md](11-adr-chart-data-delivery.md): ADR-16.1 to ADR-16.7). The decision points are collected in §14.10 (D9.1-D9.12): D9.6 and D9.11 carry the owner's outcome, the others a recommendation. This text replaces the uncommitted 0.5.1 draft of this part, which left the response shape to the host, added only `ChartTemplate<TRow>.ToJson()` and rejected `WriteRows` members and an SDK-owned envelope; its D9 numbering is replaced by §14.10. Section numbers continue the SDK design; part 8 is §13.

Paths: paths under `src/Graspea.`, `tests/`, `samples/`, `docs/`, `generator/` and `.github/`, and root files such as `README.md` and `CHANGELOG.md`, are repository paths; a repository path that starts with a folder of `src/Graspea.Flint/` (`Fluent/`, `Generated/`, `Model/`, `Validation/`) is short for that path. Every other `src/…` path, and `dist/…` and `package.json`, is an upstream path relative to the root of the `flint-chart` 1.0.1 npm package. The repository pins 1.0.1 since `v1.0.1.1-preview.1` (`generator/package-lock.json`, installed at `generator/node_modules/flint-chart/`); 1.0.0 and 1.0.1 differ only in three Plotly files (`src/plotly/templates/area.ts`, `src/plotly/templates/line.ts`, `src/plotly/theme.ts`), so every upstream fact below holds for both. A repository line cited in a file that this design adds (for example `src/Graspea.Flint/Delivery/`, `Model/ColorScheme.cs`, `docs/delivery.md`, `tests/Graspea.Flint.Tests/Delivery/`, `tests/contract-node/delivery*`), or in a sentence that describes the implementation, refers to the implementation; every other repository line refers to `v1.0.1.1-preview.1`. A path written "0.5.1 `src/…`" is relative to the 0.5.1 package. Figures labelled "ad-hoc run" come from short Node 24 scripts run against the `dist/index.js` of 0.5.1 and of an installed 1.0.1 whose `dist/` is identical to the published package, or, for a .NET member, from short programs against `src/Graspea.Flint`: the `scheme` and `sortBy` failures on `v1.0.1.1-preview.1`, the others before the 1.0.1 sync, which left `Fluent/ChartTemplate.cs`, `FlintJson.cs`, `RowWriter.cs`, `RowMaterializer.cs`, `StreamedRows.cs`, `StreamedDocument.cs` and `PooledBufferWriter.cs` unchanged. The scripts are not committed. The figures support the documented drawing policy and limits (§14.2) and are not guarantees, so no test pins them; a figure measured on 0.5.1 only says so, and "by source read" marks a 1.0.1 fact that no run checked. Anything not confirmed by source, a committed fixture or such a run is marked **UNVERIFIED**.

### 14. Fixed charts in the browser: specs first, rows per group

#### 14.1 Assumptions, goals, constraints, starting point

**Assumptions** (PRD §2):

- **Fixed definitions.** Charts are declared in code or in spec files at build time or start-up, are immutable at run time, and are the same for every request of the users who see them. Their specs are computed once, cached and content-hashed, never composed per request. A host whose users see different charts (for example by role) builds one manifest per variant at start-up.
- **Baseline `v1.0.1.1-preview.1`.** The work is implemented on the SDK release that tracks `flint-chart` 1.0.1 and covers 1.0.0 (`CHANGELOG.md`, section `[1.0.1.1-preview.1]`). Every upstream fact below is re-based on 1.0.1, and every repository path and line on that release.
- **M0a lands first** (§14.8.1): the one 1.0.1 gap of `v1.0.1.1-preview.1` that blocks delivery. `encoding.scheme` is `string | string[]` in 1.0.1 (`src/core/types.ts:62`), `ChartEncoding.Scheme` is a `string` on that release (`Model/ChartEncoding.cs:39`), and there `FlintJson.Deserialize` throws `JsonException` ("The JSON value could not be converted to System.String. Path: $.scheme") on a spec with an array `scheme` (ad-hoc run), so such a spec file cannot be read at start-up. M0a is done as `ChartEncoding.ColorScheme`; M0b, the Image-Charts backend, followed it, outside delivery, which does not depend on it.
- **Rows** are final values aggregated on the server, small enough to assemble in the browser: about 10,000 per chart, the measured guidance of §14.2.3; the host enforces its own cap and error code.
- **Wire contract v1** (§14.5) is frozen at its first release: the `[Unreleased]` entry of `CHANGELOG.md` declares it stable, additive changes only. The C# surface follows the library's rules: the preview rule now (`docs/versioning.md:38`), and before the first stable release the preview exit gate of D9.4 (`docs/versioning.md`, "The delivery contract").

**Goals** (PRD §5):

1. One cacheable response carries the data-free specs of every chart of a page, identical for every request of the users who see them (N1).
2. The rows of a chart group are requested only when the group is shown; one response per group carries every chart of the group, each as rows or withheld under a host-defined code, with no counts (N2, N3, N7).
3. The output embeds as one raw JSON value, or as UTF-8 bytes, in any transport: HTTP, or a WebSocket RPC with the host's own envelope (N6).
4. A definition that depends on the rows present is detectable at start-up (N10).
5. Paging is reserved in the contract and additive (N5).
6. The rows of a chart are byte-identical to the `data.values` elements of the streamed document, and of `Bind` for rows whose converters write JSON tokens (§14.4.2, "Byte identity"), and every member is trimming- and AOT-safe.
7. The browser side is a documented, tested snippet on 1.0.1, and the upstream changes that would remove today's workarounds are named (N8, N9, N11).

**Constraints**: zero runtime dependencies and `net8.0` (`00-index.md:20`), `IsAotCompatible` (`src/Graspea.Flint/Graspea.Flint.csproj:8`); `Build()`/`ToJson()` never validate (`00-index.md:12`), and every new writer inherits the rule; the row contract (§5.3) and the byte identity of §13.1 and §13.5 step 9 unchanged; PublicAPI analyzers with RS0016 as an error (`.editorconfig:30`; part 3 §8.6); the library grants no `InternalsVisibleTo` (decision 14 chose this for the generator, `00-index.md:29`; the same rule is assumed for any companion package, so a separate package sees only public core members); every package of a release ships at one four-part version mirroring `flint-chart` (`docs/versioning.md:42-49`); the package emits JSON only (`Graspea.Flint.csproj:6`, `Description`) and ships no JavaScript; HTTP helpers belong to the later `Graspea.Flint.AspNetCore` package (part 1, `docs/design/01-sdk-overview-baseline-architecture-api.md:132`); a new hand-written member and a new Info code are allowed in any release (`docs/versioning.md:30`, `:33`); English only; no AI attribution anywhere.

**Today** (§13): a deferred entry plus `ToTemplate()` gives a `ChartTemplate<TRow>` that holds the document without `data`, serialised once to UTF-8, and the row metadata, both private (`Fluent/ChartTemplate.cs:32-34`, `:40`). Rows are bound per call with `Bind`, `BindAsync` or `BindStream` (`:58-118`). What a host that sends specs first and rows later needs is missing:

- **The spec bytes are private.** The public routes are a deferred builder's `ToJson()` (`Fluent/ChartBuilder.cs:414`) or `Bind(ChartData.FromUrl("x"))` with `Data = null` and a second write, one parse and one write per call (`ChartTemplate.cs:68-74`).
- **One chart's rows as an array** are `template.Bind(rows).Data!.Values!`: one spec parse and one `JsonArray` per call (`ChartTemplate.cs:58-62`, `:68-73`). An ad-hoc run (the 0.5.1 draft's evidence) showed its elements byte-identical to those of `Bind(allRows)` for one page, one-row pages, uneven pages with an empty page, an empty source, a `null` row, escaped strings and both metadata kinds. `RowWriter<TRow>` is internal (`Model/Json/RowWriter.cs:12`); `StreamedRows<TRow>.WriteRows` and `WriteRowsAsync` write the elements only, the brackets are the caller's (`Model/Json/StreamedRows.cs:77-109`); the only streamed writer writes a whole document (`Model/Json/StreamedDocument.cs:30-36`, `:58-70`).
- **No envelope**: nothing writes several charts in one response, a withheld chart, a code-only error, a version or a content hash, and nothing checks a definition for row-dependent decisions.

**Upstream facts on 1.0.1** that shape the design:

- Six assemblers read `input.data.values ?? []` and nothing else from `data`: Vega-Lite through `analyzeChart` (`src/core/analyze.ts:62`, called at `src/vegalite/assemble.ts:933`), `src/echarts/assemble.ts:147`, `src/chartjs/assemble.ts:107`, `src/plotly/assemble.ts:144`, `src/excel/assemble.ts:194` and the new Image-Charts backend, `src/image-charts/assemble.ts:186`. Only the opt-in `validateChartInput` reads `data.url`, to reject it (`src/validate/index.ts:151-161`). `data` is still required, with the same union and comment (`src/core/types.ts:1291-1302`), and `flint-chart/validate` says hosts resolve `data.url` themselves (`src/validate/index.ts:10`).
- A document without `data`, or with `data: null`, throws a raw `TypeError` on all six backends (ad-hoc run; for every backend the SDK targets also `tests/fixtures/flint-chart@1.0.1/probe-expectations.jsonl:38`, `Probe|#37`, and `:11`, `Probe|#10`, which the validator reports as `data_missing`, `docs/validation-codes.md:38`). The new `assembleForBackend`, for Vega-Lite, ECharts, Chart.js and Plotly only, throws "input.data is required (provide { values: [...] })" instead, and also rejects URL-only `data`, empty `values` and more than 100,000 rows by default (`src/validate/index.ts:44`, `:54`, `:151-169`, `:317-332`).
- `ChartAssemblyInput` keeps `data` as a required sibling of `chart_spec` (`src/core/types.ts:1302`) and adds `interaction_spec` (`:1412`) and `chart_spec.label`, `source` and `chrome` (`:1336-1349`). A spec may carry them and the shallow merge of §14.6 keeps them. No upstream API assembles from a spec and rows given apart. `v1.0.1.1-preview.1` types those members (`Model/ChartAssemblyInput.cs:52`, `Model/ChartSpec.cs:30`, `:57`, `:62`).
- `dist/index.js` carries no version string (`grep -c '1\.0\.1' dist/index.js` gives 0, as does every `dist/*/index.js`), no module exports a version, and the `exports` map of `package.json` has no `./package.json` entry. A browser bundle learns its `flint-chart` version only at build time, by reading `node_modules/flint-chart/package.json`, as the contract harness does at run time (`tests/contract-node/flint.js:14-17`).
- Assembled ECharts and Chart.js options hold functions, and the 1.0.1 Vega-Lite output holds two under a private key (§14.2.1).

#### 14.2 Why the browser assembles, and when it draws

##### 14.2.1 Why assembly stays in the browser

- **Function-valued options.** ECharts and Chart.js outputs are not JSON-safe. `grep -rE --include='*.ts' '(formatter|callback|renderItem|label) *[:=] *\(' src/<backend> | wc -l` gives 28 lines under `src/echarts` and 5 under `src/chartjs`, for example an axis-label formatter (`src/echarts/instantiate-spec.ts:880`), a tooltip formatter (`:1466`) and a tick callback (`src/chartjs/templates/gantt.ts:115`); the pattern misses method shorthand such as `callback(v) {` (`src/chartjs/templates/line.ts:104`), so the counts are a floor. Its 5 hits under `src/vegalite` are interaction-runtime types and a local helper (`src/vegalite/templates/ridgeline.ts:786`), not emitted options, and `src/plotly` gives 0; Plotly figures are documented as "pure JSON — no callback functions anywhere" (`src/plotly/assemble.ts:20-21`). The 1.0.1 Vega-Lite output nevertheless carries two functions, `_interactionSemantics.resolve` and `.presentUpdate`, and a copy of the rows in `data.values` as `_interactionSemantics.sourceRecords`, under a private top-level key that the newly exported `stripPrivateKeys` removes (`src/vegalite/assemble.ts:761-772`, `src/validate/index.ts:464-471`; ad-hoc run, 0 functions on 0.5.1). `JSON.stringify` drops functions silently and `structuredClone` throws on them (as JavaScript specifies; not probed here), so an assembled option can be neither produced on the server and sent, nor stored in IndexedDB or a cache.
- **Inputs only the browser knows.** The layout reads `chart_spec.baseSize` and `canvasSize` (`src/core/compute-layout.ts:495-505`, `:579-589`). Upstream now takes the container size itself: `withCanvasSize` (`:519-546`) turns `mountChart`'s `availableSize` into `canvasSize` (`src/interactive/index.ts:212-218`). The host re-themes after assembly.
- **Assembly is cheap.** Ad-hoc run, median of five: about 1-3.6 ms for 1,000 rows and 7-30 ms for 10,000 rows for a Line, a Bar and a Heatmap on Vega-Lite, ECharts, Chart.js (no Heatmap template) and Plotly on 0.5.1; on 1.0.1 about 1-10 ms and 2-48 ms, the Vega-Lite and Plotly Lines the slowest. Browser timings and the renderer's own cost are **UNVERIFIED**.
- **Inputs are not mutated.** An ad-hoc run assembled a Line and a Bar from 40 rows on all five 0.5.1 backends and a Heatmap on Vega-Lite, ECharts and Plotly (Chart.js and Excel have no Heatmap template). `JSON.stringify(input)` was identical before and after in all 13 cases, and a deep-frozen input assembled without a throw in each; on 1.0.1 the same, plus a Line and a Bar on Image-Charts. Other templates are **UNVERIFIED**. The contract harness still clones defensively (`tests/contract-node/flint.js:36`). The merge of §14.6 does not clone; a host that wants certainty for other templates deep-freezes the input in a development build, where a write to a frozen object throws in strict-mode ESM.
- **Server-side SVG stays outside this design.** `flint-chart/render` adds `renderSvg(input, { backend })` in 1.0.1: a static SVG without a DOM for Vega-Lite and ECharts (ECharts through `ssr: true`), meant for export, images and server rendering (`src/render/index.ts:6-39`). `vega`, `vega-lite` and `echarts` are optional peers (`package.json:143-177`). The ECharts functions then run where the SVG is made and only SVG travels, but the result is static, sized from `_width` or `baseSize` (`src/render/index.ts:27-28`), and it needs a JavaScript runtime on the server, which this SDK does not assume.

##### 14.2.2 Draw on complete, not per page

Upstream derives types, categories, sizes and colours from the rows that are present (1.0.1 lines; the code is unchanged from 0.5.1 unless a bullet says otherwise):

- **Encoding type.** A registry quantitative field with no numeric value falls back to inference (`src/core/decisions.ts:442-453`), and an empty column infers as nominal (`src/core/semantic-types.ts:274`). A temporal field needs two distinct values (`src/core/decisions.ts:197-198`). An explicit `encoding.type` wins (`src/core/resolve-semantics.ts:549-550`).
- **Size.** The discrete step and plot size follow the distinct values present (`src/core/compute-layout.ts:737-745`). The ECharts outer size is plot plus margins (`src/echarts/instantiate-spec.ts:647-648`), so it can exceed `canvasSize`. Continuous marks grow with the point count (`src/core/compute-layout.ts:939-1142`). For a continuous Scatter, the caller's `options.continuousMarkCrossSection` holds the size. Templates that declare that parameter in `paramOverrides` (for example Line, `src/echarts/templates/line.ts:89`, `src/vegalite/templates/line.ts:180`; Vega-Lite Ridgeline joins them in 1.0.1, `src/vegalite/templates/ridgeline.ts:524`) override the caller's value, because template parameters spread after the caller's `options` (`src/echarts/assemble.ts:243-244`, `src/chartjs/assemble.ts:199-200`, `src/plotly/assemble.ts:231-232`; Vega-Lite `src/core/plan.ts:1126-1130`). Ad-hoc run from 30 to 3,000 points with the option set, identical on 0.5.1 and 1.0.1: the Scatter stays 372 (Vega-Lite) and 496 (ECharts) wide, while the Line grows from 372 to 540 and from 496 to 655.
- **Series and categories.** ECharts Line creates one series per colour value in first-appearance order (`src/echarts/templates/line.ts:313-327`). ECharts Heatmap takes its categories from the inferred order only, not from `sortBy` (`src/echarts/templates/heatmap.ts:76-77`).
- **Colours.** The ECharts palette switches from 10 to 20 colours above 10 categories (`src/echarts/instantiate-spec.ts:926-931`), and colours follow the series index (`:1196-1240`). The 20 start with the 10 (`src/echarts/colormap.ts:39-63`, byte-identical to 0.5.1) and Line series follow encounter order, so in an ad-hoc run a Line's present series kept their colours across every prefix, while Vega-Lite moved from `tableau10` to `tableau20`, whose order differs, and recoloured them (both versions). Other ECharts templates are **UNVERIFIED**.
- **Axis pins.** Vega-Lite reads `xAxisType`/`yAxisType`, `includeZero_*` and `logScale_*` in `analyzeChart` (`src/core/analyze.ts:100`, `:185`, `:210`), which only the Vega-Lite assembler calls. Plotly reads `includeZero_*` and `logScale_*` (`src/plotly/instantiate-spec.ts:156-180`, called at `src/plotly/assemble.ts:460`, `:466`), not the axis type. Nothing under `src/echarts` or `src/chartjs` reads any of them, so on ECharts numeric domains and zero baselines follow the rows present.
- **Transforms and aggregates.** A chosen transform reverts silently when the data stops passing its gate (`src/core/pivot.ts:964`; `:980` for `arrange`), with no warning code. Aggregates over a prefix are partial sums (`applyAggregation`, `src/core/aggregate.ts:104`). New in 1.0.1: a Heatmap whose `color` measure has no `aggregate` and whose cells hold two or more rows gets `sum` or `average` and an `implicit-aggregate` warning, while one row per cell gets none (`src/core/aggregate.ts:34-90`; called at `src/core/analyze.ts:134`, `src/echarts/assemble.ts:184`, `src/chartjs/assemble.ts:144`, `src/plotly/assemble.ts:179`; by source read), so a prefix can draw unaggregated values that the complete rows aggregate.
- **Sorts.** On Vega-Lite a `sortBy` that names a field is checked against the keys of the rows present and, with no rows, dropped with an `invalid_sort` warning (`src/core/resolve-semantics.ts:137`, `:192`, `:199`; `src/core/analyze.ts:63`); ECharts, Chart.js and Plotly reject a field-name `sortBy` whatever the rows (`src/core/resolve-semantics.ts:138-143`; `src/echarts/assemble.ts:148`, `src/chartjs/assemble.ts:108`, `src/plotly/assemble.ts:145`). A channel `sortBy` (`x`, `y` or `color`) orders the categories by the values of that channel (`src/core/resolve-semantics.ts:122-134`). Only a JSON-array string survives independent of the rows.
- **Measured.** Ad-hoc run with sales for 8 regions × 5 months, first page of 10 rows covering 2 regions: ECharts Line goes from 2 to 8 series, `legend.data` from 2 to 8 entries and `grid.right` from 112 to 126, and the ECharts Bar outer size goes from 390×417 to 524×437.4, identical on 0.5.1 and 1.0.1. Even with every region in the first page (rows month by month), the ECharts Heatmap `_height` moves from 223 to 319 as the months arrive (on 1.0.1: 231.4 to 327.4).
- **Annotations do not hold ECharts.** In 1.0.1 a field annotation with a `sortOrder` that lists every category and a `colors` map changes none of this on ECharts in an ad-hoc run: Line series follow encounter order, and Heatmap categories and sizes follow the values present. Without a `theme_spec` the map applies on Vega-Lite only (`src/vegalite/value-colors.ts:45-70`), where it keeps each value's colour across pages.
- **Swapping rows into a mounted chart.** In 1.0.1 the Vega-Lite interactive surface accepts an undocumented `set-data` update (`src/core/interaction-contracts.ts:260-263`) through `applyUpdate` or `setUpdates`. It puts the rows into the compiled Vega data source without re-assembly (`src/vegalite/interactions/runtime.ts:1150-1152`, `:1217-1220`, `:1331-1339`). The compiled spec and its types stay those of the input the chart was mounted with (`src/vegalite/interactive.ts:299`); only a filter control in filter mode re-lays the steps for the new rows (`:338-339`, `:424-430`). It is not in `src/interactive/README.md`, other backends ignore updates (`src/validate/index.ts:388-396`), and the internal re-plan (`src/vegalite/assemble.ts:938-975`) is not exported. It keeps the prefix problem and changes nothing below.

**Documented default.** The browser collects every page of a group and assembles and draws each chart once, after the last page. Until then it shows a fixed-size placeholder that the page layout owns. It never assembles a spec with `values: []` as a placeholder: the types and sizes above differ from the real chart's, the opt-in `validateChartInput` rejects an empty `values` (`src/validate/index.ts:167-169`), and on Vega-Lite a field-name `sortBy` is dropped without rows.

**Progressive drawing** (re-assembling after each page) is not documented. A prefix of the rows gives different types, sizes, series and categories, and on Vega-Lite different colours (above); on ECharts the pins that would hold them are missing or ignored. A host that draws per page owns that risk. §14.7 lets a host check at start-up that its definitions pin what can be pinned in a spec.

##### 14.2.3 Limits and exposure

- **Rows per chart.** Keep the merged rows of one chart at or below about 10,000 (N4), where the ad-hoc timings of §14.2.1 stay in tens of milliseconds. `MaxRows` is a handle setting that caps one part, the rows of one `Rows`, `RowsAsync` or `Stream` call, not a chart across a paging sequence: a host that pages caps the whole chart across its pages itself, and a host that caps a whole page sums its parts. Above that, aggregate on the server; more pages do not help. In an ad-hoc run (Node 24, default stack) every assembler threw `RangeError: Maximum call stack size exceeded` at 200,000 rows, and already from about 125,000 (124,000 rows passed and 125,000 threw for a Line and a Scatter on ECharts and Vega-Lite), from spread calls such as `Math.min(...filtered)` (`src/core/field-semantics.ts:623`; 0.5.1 `:553`); on 1.0.1 the same onset, Image-Charts included. The new `assembleForBackend` rejects more than 100,000 rows by default (`src/validate/index.ts:53-54`, `:162-166`). The exact limit depends on the engine's stack size; browsers are **UNVERIFIED**. The merge of §14.6 appends with `concat`, never with a spread. `MaxRows` (§14.4.2) lets the server enforce the cap per part.
- **Exposure.** Every row sent to the browser can be read there: send only the rows and fields the chart needs, aggregated on the server. The Image-Charts backend writes the values into an `https://image-charts.com` URL, so fetching its image sends them to that service (`src/image-charts/assemble.ts:7-16`, `:47`); never use it for delivered rows. It and Excel are outside this flow.
- **Withheld codes and errors are data**, never HTML: the client writes them with `textContent` or maps them to its own messages.

#### 14.3 Where the data API lives

Everything a separate package would need is private today: the template's spec bytes and `JsonTypeInfo` (`Fluent/ChartTemplate.cs:32-34`), `RowWriter<TRow>`, `StreamedRows`, `PooledBufferWriter`, and the constructors of `ValidationIssue` and `ValidationResult` (`Validation/ValidationIssue.cs:6`, `Validation/ValidationResult.cs:6`). The library grants no `InternalsVisibleTo` (§14.1).

| Option | Buys | Costs |
|---|---|---|
| **A. Core only**: namespace `Graspea.Flint.Delivery` in `Graspea.Flint`; wire v1 becomes the third documented contract of `docs/versioning.md`, next to the tool's and the generator's, whose stability does not follow the version label (`docs/versioning.md:47-49`) | internal access: no spec parse and no `JsonArray` per chart, no public row writer, `ValidationIssue` constructible, streamed parts (D9.6) without a public primitive; one release process, one AOT publish, no new package in any pinned list | about 9 types and 40 members in core; the C# surface freezes with core at stable (`docs/versioning.md:40`) unless marked `[Experimental]` |
| **B. Public primitives in core + `Graspea.Flint.Delivery` package** (zero dependencies), `Graspea.Flint.AspNetCore` later | a package boundary around the frozen contract; an independent stability declaration for the delivery C# surface after core goes stable | public `WriteRows`/`WriteRowsAsync` and `ToJson()` in core; an own result type for the profile codes; a nuspec or pack target for the exact `[X]` dependency (`FlintUpstream.Version` is a `const`, `Generated/Constants/FlintUpstream.g.cs:10`, so it is inlined into the package that writes the envelope); a new package kind in `scripts/check-nupkg.sh`; four `release.yml` edits, the workflow test, `docs/maintainers.md`, `SECURITY.md`, the README and the AOT sample; identical rebuilds on most upstream releases; the package id's availability and its first Trusted Publishing push **UNVERIFIED** |
| C. Everything in `Graspea.Flint.AspNetCore` | minimal-API convenience | fails N6 (transport-neutral, no ASP.NET Core in the data API) |
| D. An npm client from this repository | a typed, tested client | the four-part version is not npm semver; npm trusted publishing of a new package id **UNVERIFIED**; a second lockfile and sync-bot path |

**Recommendation: A now** (D9.1, ADR-16.1). B's one genuine advantage, independent stability after core goes stable, is covered while the 1.0.x line is in preview (a new backend and the `scheme` union keep it there, `docs/versioning.md:21`) and by `[Experimental("FLINTEXP002")]` on every public type of `Graspea.Flint.Delivery` and on `ChartTemplate<TRow>.ToJson()` as the fallback (D9.4; the attribute has no namespace target). **Split trigger for B:** a wire v2 is needed while core is stable, or a host needs these members on another release cadence. The split then makes `WriteRows`/`WriteRowsAsync` public next to `ToJson()`, moves the profile codes to an own result type and keeps the wire shape. Streamed parts in v1 (D9.6) add a reason for A: in a separate package they would also need a public streaming primitive with an after-row hook, the writer-target trap of §14.4.7. D is not planned (owner decision D9.11).

#### 14.4 Public API

##### 14.4.1 Signatures

```csharp
namespace Graspea.Flint;

public sealed class ChartTemplate<TRow>
{
    // existing: Bind (2), BindAsync, BindStream (2), Validate
    /// <summary>The document without data as compact JSON text: the bytes stored at creation, decoded. Never validates.</summary>
    public string ToJson();
}

namespace Graspea.Flint.Delivery;

/// <summary>A fixed chart under a stable id; exists only as ChartHandle&lt;TRow&gt;. Immutable.</summary>
public abstract class ChartHandle
{
    /// <summary>Unique within a manifest; matches [A-Za-z0-9_.-]+.</summary>
    public string Id { get; }
    /// <summary>The most rows one part may hold: one Rows, RowsAsync or Stream call, not a chart across a paging
    /// sequence. Above it Rows and RowsAsync throw ChartRowLimitException, and a streamed part's DataPage.WriteAsync
    /// does (§14.4.6); null means no limit.</summary>
    public int? MaxRows { get; }
    /// <summary>This chart withheld on a page with a non-empty host-defined code, written as given.</summary>
    public ChartPart Withheld(string code);
}

public sealed class ChartHandle<TRow> : ChartHandle
{
    /// <summary>Binds an id to an immutable template; throws ArgumentException for a bad id, a template without row
    /// metadata or a template whose document has a top-level data member, and ArgumentOutOfRangeException for a
    /// maxRows below 1. Keep it in a static field.</summary>
    public ChartHandle(string id, ChartTemplate<TRow> template, int? maxRows = null);
    public ChartTemplate<TRow> Template { get; }
    /// <summary>The rows written to UTF-8 now with the template's row contract: this chart's part of a page. Enumerated
    /// synchronously; for a database query use RowsAsync with .AsAsyncEnumerable(). The bytes equal the data.values
    /// elements of the streamed document, and of Bind(rows) for rows whose converters write JSON tokens.</summary>
    public ChartPart Rows(IEnumerable<TRow> rows);
    /// <summary>The rows of an asynchronous source, enumerated once before the task completes; end a database query
    /// with .AsAsyncEnumerable().</summary>
    public ValueTask<ChartPart> RowsAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default);
    /// <summary>A single-use part whose rows are written straight from the source when the page is written with
    /// DataPage.WriteAsync, with memory that does not grow with the row count. A failure after the page's first byte
    /// leaves truncated JSON (§14.4.6).</summary>
    public ChartPart Stream(IAsyncEnumerable<TRow> rows);
}

/// <summary>One chart's part of a page: its rows as UTF-8, a streamed source, or a withheld code. Created by a handle
/// only; immutable unless streamed.</summary>
public sealed class ChartPart
{
    public ChartHandle Chart { get; }
    /// <summary>The code when withheld; null when the part carries rows.</summary>
    public string? WithheldCode { get; }
    /// <summary>True for a part from Stream: written once, by DataPage.WriteAsync only.</summary>
    public bool IsStreamed { get; }
}

/// <summary>Charts shown together: the unit a client requests and a page answers. Immutable, thread-safe.</summary>
public sealed class ChartGroup
{
    /// <summary>A name matching [A-Za-z0-9_.-]+ and at least one chart; ids unique within the group; the array is
    /// copied.</summary>
    public ChartGroup(string name, params ChartHandle[] charts);
    public string Name { get; }
    /// <summary>A read-only snapshot, in the order given.</summary>
    public IReadOnlyList<ChartHandle> Charts { get; }
    /// <summary>Each template's Validate(backend) plus the fixed_* Info codes (§14.7); paths are manifest paths,
    /// charts.&lt;id&gt;.input.… Never implicit.</summary>
    public ValidationResult Validate(FlintBackend? backend = null);
}

/// <summary>The data-free documents of one or more groups, serialised once: cache it, hash it, send it on mount.</summary>
public sealed class ChartManifest
{
    /// <summary>The only factory. At least one group, copied; charts in first-appearance order; a handle may be in
    /// several groups; two handles with one id or two groups with one name throw.</summary>
    public static ChartManifest Create(params ChartGroup[] groups);
    /// <summary>A read-only snapshot, in the order given.</summary>
    public IReadOnlyList<ChartGroup> Groups { get; }
    /// <summary>The manifest bytes, the same for every request: the stored array, shared, so it must not be modified,
    /// because Hash and every write use it.</summary>
    public ReadOnlyMemory<byte> Utf8Bytes { get; }
    /// <summary>Lowercase hex SHA-256 of Utf8Bytes: the ETag. Deterministic for the same definitions under the same SDK
    /// version and runtime serialisation.</summary>
    public string Hash { get; }
    public string ToJson();
    /// <summary>Writes the stored bytes as one raw JSON value.</summary>
    public void WriteTo(Utf8JsonWriter writer);
}

/// <summary>One response for a group: every chart of the group exactly once, rows or withheld. Immutable and written any
/// number of times, unless a part is streamed: then written once, by WriteAsync.</summary>
public sealed class DataPage
{
    /// <summary>Throws ArgumentException when a part's chart is not in the group, a chart is missing or repeated, or next
    /// is empty; the list is copied.</summary>
    public DataPage(ChartGroup group, IReadOnlyList<ChartPart> charts, string? next = null);
    public ChartGroup Group { get; }
    /// <summary>In group order; a read-only snapshot.</summary>
    public IReadOnlyList<ChartPart> Charts { get; }
    /// <summary>The opaque cursor of the next page; null on the last page.</summary>
    public string? Next { get; }
    /// <summary>Writes the page as one JSON value into the caller's writer, which must sit over a buffer, never over a
    /// response stream. Throws InvalidOperationException when a part is streamed.</summary>
    public void WriteTo(Utf8JsonWriter writer);
    /// <summary>Throws InvalidOperationException when a part is streamed.</summary>
    public byte[] ToUtf8Bytes();
    /// <summary>Throws InvalidOperationException when a part is streamed.</summary>
    public string ToJson();
    /// <summary>Writes the page with WriteAsync and FlushAsync only, never the stream's synchronous Write or Flush: in
    /// one write without streamed parts, otherwise drained whenever the buffer holds at least 16 KiB, after each
    /// streamed row and after each materialised part. A failure of the stream propagates unchanged, at any time, and
    /// nothing is written after it.</summary>
    public Task WriteAsync(Stream utf8Json, CancellationToken cancellationToken = default);
}

/// <summary>A code-only error document: {"contract":1,"error":{"code":"…"}}.</summary>
public sealed class DeliveryError
{
    /// <summary>A non-empty host-defined code.</summary>
    public DeliveryError(string code);
    public string Code { get; }
    public void WriteTo(Utf8JsonWriter writer);
    public byte[] ToUtf8Bytes();
    public string ToJson();
}

/// <summary>Thrown when the source of one part yields row MaxRows + 1, before that row is serialised. From Rows and
/// RowsAsync that is before any byte of the page; nothing was kept. From a streamed part it comes out of WriteAsync,
/// possibly after the page's first byte: the stream then holds a prefix of the page, empty when less than 16 KiB had
/// been drained. The host maps it to its own code.</summary>
public sealed class ChartRowLimitException : InvalidOperationException
{
    public ChartHandle Chart { get; }
    public int Limit { get; }
}

/// <summary>The wire contract version every document carries.</summary>
public static class DeliveryContract { public const int Version = 1; }
```

`ChartHandle`, `ChartPart` and `ChartRowLimitException` have internal constructors, and `ChartManifest` a private one behind `Create`, so no public type can be derived from (`docs/versioning.md:40`). `ChartGroup`, `ChartManifest` and `DataPage` copy the arrays and lists they are given and expose `ReadOnlyCollection<T>` views, which throw on a write and cannot be cast back to the copied array (`Delivery/ChartGroup.cs:40`, `Delivery/ChartManifest.cs:102`, `Delivery/DataPage.cs:66`). The handle refuses a template whose stored document has a top-level `data` member, found by a scan of the top-level property names (`Delivery/ChartHandleOfTRow.cs:30-35`, `:111-123`): a `Configure` action that writes `Extensions["data"]` produces one, because `WithoutData()` does not strip it (`Model/ChartAssemblyInput.Copy.cs:7-12`), and the manifest would then carry rows. `Rows`/`RowsAsync`/`Stream` are distinct names, so the overload-priority issue of D8.6 does not arise; `Stream` takes an asynchronous source only, because a synchronous one has no I/O to overlap (an in-memory collection uses `Rows`), and the name mirrors `ChartData.Stream`. `params ChartHandle[]`, not a span: C# 12 consumers cannot call a span `params`. A template without row metadata (`TRow = object`, entered with `ChartData`) is refused at the handle; a spec file gets rows through `ChartTemplate.Create(spec, typeInfo)` (`ChartTemplate.cs:148-152`). A source-generated chart passes its `Template` (§14.4.3, "Name").

##### 14.4.2 Rules

| Rule | Statement |
|---|---|
| Never validates | no writer validates; `ChartGroup.Validate` is explicit (§14.7) |
| Implementation | `Rows*` create `new StreamedRows<TRow>(rows, typeInfo)` per call (`StreamedRows.cs:57-71`) and write `WriteStartArray`, the elements through `WriteRows(writer, null)` or `WriteRowsAsync(writer, afterRow, ct)` with a no-op `afterRow` (`:77-109`: elements only), and `WriteEndArray` into a `PooledBufferWriter` (`Model/Json/PooledBufferWriter.cs:7`), then copy to an exact `byte[]` (`Delivery/ChartHandleOfTRow.cs:54-64`, `:81-91`). `MaxRows` is enforced by an internal counting wrapper around the source, a synchronous and an asynchronous iterator that throw `ChartRowLimitException` when the source yields row `MaxRows + 1`, before that row reaches the writer, so a bad excess row never wins over the limit; the count stops at the limit and cannot overflow, and without `MaxRows` the source passes through unwrapped (`Delivery/RowLimit.cs:8-59`). `ToJson()` decodes the private `spec` bytes (`ChartTemplate.cs:132`). A manifest writes each `input` and a page each materialised part with `WriteRawValue(bytes, skipInputValidation: true)`. A streamed part holds `new StreamedRows<TRow>(rows, typeInfo)` over the same wrapper (`StreamedRows.cs:65-71`), claimed by the first write (`:26-33`); `WriteAsync` writes the page with an internal `DrainingJsonWriter`, one `Utf8JsonWriter` over a pooled buffer, extracted from `StreamedDocument` with no change of behaviour (`Model/Json/DrainingJsonWriter.cs:9-52`), and the streamed rows through `WriteRowsAsync(writer, afterRow, ct)`, whose `afterRow` drains the buffer with `Stream.WriteAsync` whenever it holds at least 16 KiB, as `FlintJson.SerializeAsync` does (`Delivery/DataPage.cs:192-199`, `:209`; `Model/Json/FlintJson.cs:170-186`); the page drains again after each part (`Delivery/DataPage.cs:219`) |
| Byte identity | the elements of a chart's `rows` equal byte for byte the elements of `data.values` in the streamed document, `FlintJson.SerializeAsync` or `FlintJson.SerializeToUtf8Bytes` over `handle.Template.BindStream(sameRows)`: both write each row through the direct `RowWriter<TRow>.Write` path. They equal those of `Bind(sameRows)` for rows whose converters write JSON tokens. A row converter that writes raw JSON (`Utf8JsonWriter.WriteRawValue`) can differ from `Bind` in insignificant whitespace and is then JSON-equal (`JsonNode.DeepEquals`), the existing relation between streamed and materialised documents (`Model/Json/RowWriter.cs:39-40`; §13.13 step 5). All of it holds with the default writer options, which `FlintJson.Options` does not change (`Model/Json/FlintJsonContext.cs:11` names no encoder). `charts.<id>.input` equals the template's stored bytes and the UTF-8 of `ToJson()`. Under `WriteTo(Utf8JsonWriter)` with indentation or another encoder the raw values stay compact: JSON-equal, not byte-equal |
| Errors without streamed parts | every row, limit, invariant and cancellation failure happens before the page's first byte. `ArgumentException` from the `ChartHandle<TRow>` constructor (bad id, no row metadata, a `data` member), `ChartGroup`, `ChartManifest.Create` and `DataPage` (invariants, an empty `next`), and `Withheld` (empty code); `ArgumentNullException` for a null argument or code; `ArgumentOutOfRangeException` for a `maxRows` below 1. From `Rows`/`RowsAsync`: `ArgumentException` at the row for a value-dependent predicate (`Model/Json/RowMaterializer.cs:87-101`) or a non-finite `double` under `JsonTypeInfo` metadata (written `null` under `RowOptions`, as `Bind` does today; `docs/row-data.md:58`); `ChartRowLimitException` when the source yields row `MaxRows + 1`, before that row is serialised; `OperationCanceledException` (the token reaches the source and is checked before each row, `StreamedRows.cs:95`, `:103-105`); source exceptions unwrapped. The part's buffer returns to the pool; nothing escapes. Transport failures, the stream's `WriteAsync` or `FlushAsync` throwing, can happen at any time, including after the whole page was written, at the final `FlushAsync`; they propagate unchanged. The token passed to `WriteAsync` reaches only the stream, so a cancellation observed there is a transport failure and can come at any time, including at the final flush. The SDK never writes anything after a failure, and a host never appends an error document after its output has started. `WriteTo` throws only what the writer throws |
| Errors of streamed parts | the same row errors, `ChartRowLimitException`, `OperationCanceledException` and source exceptions come out of `WriteAsync` while the rows are read, possibly after the page's first byte: the stream holds a prefix of the page, empty when less than 16 KiB had been drained and otherwise invalid JSON with no withheld code; the host aborts a started response (in ASP.NET Core `Response.HasStarted` tells) and may still answer an error document otherwise. Transport failures behave as without streamed parts. `WriteTo`, `ToUtf8Bytes` and `ToJson` throw `InvalidOperationException` before writing and claim nothing, as `FlintJson`'s synchronous paths refuse an asynchronous source (`StreamedRows.cs:16-17`). A second `WriteAsync` of the page throws `InvalidOperationException` from the page's own flag, and a part already written in another page from its claim, both before the first byte, each with a message of its own (`Delivery/DataPage.cs:14-16`, `:174-182`) |
| Threads | handles, groups, manifests, materialised parts and pages without streamed parts are immutable; a streamed part and its page are single-use, and a concurrent second write throws (the page's flag, `Delivery/DataPage.cs:174`, and the part's claim, `StreamedRows.cs:26-33`); one `RowWriter<TRow>` per `Rows*` or `Stream` call, never cached on a handle (it caches runtime metadata and is not thread-safe, `RowWriter.cs:10-14`) |
| AOT | no annotation anywhere: the metadata was resolved when the template was created (for the `RowOptions` forms that creation is the annotated step: the deferred entries, for example `Generated/Entries/Chart.Line.g.cs:33-35`, and `ChartTemplate.cs:162-168`); `Utf8JsonWriter`, `SHA256.HashData` and `Convert.ToHexString` carry none (on `net8.0` it returns uppercase, so `Hash` is `Convert.ToHexString(…).ToLowerInvariant()`; `ToHexStringLower` arrives in .NET 9); the AOT sample runs every member (§14.8.5) |
| Memory | per materialised chart the rows' UTF-8 once (no `JsonArray`), per page the output once, bounded per part by `MaxRows` or by the host's cap; a streamed page holds a pooled buffer of about 32 KiB (`Model/Json/DrainingJsonWriter.cs:12-14`) whatever its row count, drained whenever it holds at least 16 KiB, after each streamed row and after each materialised part; a materialised part of the same page passes through that buffer once (`WriteRawValue` needs its bytes contiguous), so a streamed page's memory is bounded by its largest single row or materialised part plus about 32 KiB of buffer |
| Flushing | `JsonSerializer.Serialize(writer, row, typeInfo)` flushes the writer after every row (§13.5 step 5); here it flushes into the pooled buffer, inside `Rows*` and inside `WriteAsync` for a streamed part. `WriteTo` writes raw values only. `WriteAsync` is tested against a stream whose synchronous `Write`/`Flush` throw, as Kestrel's response body does |
| Depth | one internal `JsonDepth.Effective` gives the document's depth limit, `FlintJson.Options.MaxDepth` or 64 when it sets none (`Model/Json/JsonDepth.cs:9`). `Rows*` write into their buffer with `MaxDepth` at that limit minus two, because a row there sits two levels higher than under `data.values`, so they accept exactly the rows whose bound document `FlintJson` writes (`Delivery/ChartHandleOfTRow.cs:58`); the bytes then enter the page as raw values, inside which the writer tracks no depth. A streamed part's rows go through the page writer one level deeper than under `data.values`, so the page writer's `MaxDepth` is the limit plus one (`Delivery/DataPage.cs:189`), and every row that core accepts is accepted there too. `StreamedDocument` keeps the limit minus one (`Model/Json/StreamedDocument.cs:17-18`) |
| Hash | the lowercase hex SHA-256 of `Utf8Bytes`, the stored manifest bytes, computed once (`Delivery/ChartManifest.cs:20`). It is deterministic for the same definitions under the same SDK version and runtime serialisation: two manifests from fresh templates are byte- and hash-equal (test-pinned). No canonical hash across runtimes or across semantically equal documents is promised. `flint` is in the bytes, so the bytes and `Hash` change with every upstream version; a new SDK line may also move spec bytes (`v1.0.1.1-preview.1` typed `label`, `source`, `chrome` and `interaction_spec`, which `0.5.1.1-preview.1` wrote from `Extensions` after the known members; wire rule 1 holds within a line only, `docs/versioning.md:99-101`), and the hash changes by design |

N3 and N7 require each chart to be rows or withheld, decided before that chart's first byte, and N4 bounds the rows. Materialising per chart, the default, makes "nothing is sent until the page is complete" true by construction; only the transport can still fail after that. A streamed part (§14.4.6) trades that for flat memory: a predicate, value or source failure lands in the middle of the envelope, where the only option left is to abort the response.

##### 14.4.3 `ChartTemplate<TRow>.ToJson()`

- **Content.** The stored bytes decoded as UTF-8 (`ChartTemplate.cs:32`, `:40`). Encoding the string as UTF-8 gives those bytes back exactly, because they were read back as valid JSON when the template was created (`:41`). There is no `data` key (`Model/ChartAssemblyInput.cs:18-20`) unless a `Configure` action puts one into `Extensions`, which `WithoutData()` does not strip (`Model/ChartAssemblyInput.Copy.cs:7-12`). `ToJson()` returns such a document as it is; a `ChartHandle<TRow>` refuses the template (§14.4.1).
- **Equalities, pinned by §14.8.5 step 1.** It equals `FlintJson.Serialize(d)`, where `d = template.Bind(ChartData.FromUrl("x"))` with `d.Data = null`: both are written by `FlintJsonContext` (`Model/Json/FlintJson.cs:29`; `ChartTemplate.cs:40`). For a template from a deferred builder whose `Configure` actions set no `Data`, it equals that builder's `ToJson()` (same writer, `ChartBuilder.cs:414`); ad-hoc run: equal bytes for a source-generated and a reflection deferred builder, with and without a `Configure` action that writes `Extensions`. `ToJson()` reruns the `Options` and `Configure` actions on each call and `ToTemplate()` ran them once (`:414`, `:422`), so the equality assumes deterministic actions and no later change to the builder. A `Configure` action that sets `Data` makes the builder write it, and the template drops it; one that puts a `data` key into `Extensions` is written by both, and the first equality then fails, because the read-back spec takes that key as `Data`.
- **Errors.** None; it never validates. **Cost and threads.** One decode per call into a new string; the template is immutable, so concurrent calls are safe. The manifest writes the bytes themselves and never decodes them. **AOT.** No annotations; no reflection.
- **Name.** It mirrors `ChartBuilder.ToJson()`. The template is not a generated builder, so no channel or property name can collide with it (part 1 §4.2). A source-generated type already exposes `Template` (`src/Graspea.Flint.CSharp/Writing/GeneratedChartWriter.cs:32`), so `Template.ToJson()` and `new ChartHandle<Row>(id, Template)` reach it with no new generated member and no new FLINT104 collision (`Template` is emitted at `:85-90` behind `FLINTEXP001` like every generated member, `:34`, `:87`; `docs/versioning.md:79-81`).
- **Uses outside a manifest**: a host that keeps its own envelope, a spec file written at build time, or a check with `flint-chart-mcp`'s `validate_chart` (D9.9).

##### 14.4.4 Declaring once; synchronous and buffered-asynchronous use

```csharp
// Charts.cs
public sealed record Sale(DateOnly Month, string Region, decimal Revenue);
public sealed record Visit(string Source, long Visits);
public sealed record Stock(string Warehouse, int Units);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Sale))]
[JsonSerializable(typeof(Visit))]
[JsonSerializable(typeof(Stock))]
public sealed partial class ShopJson : JsonSerializerContext;

public static class Charts
{
    public static readonly ChartHandle<Sale> Revenue = new("revenue-by-month",
        Chart.Line(ShopJson.Default.Sale)
            .X(r => r.Month, SemanticType.YearMonth).Y(r => r.Revenue, SemanticType.Amount).Color(r => r.Region)
            .Encoding(Channel.X, e => e.Type = EncodingType.Temporal)
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .Encoding(Channel.Color, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder("North", "South"); })
            .ToTemplate(),
        maxRows: 10_000);

    public static readonly ChartHandle<Visit> Traffic = new("traffic-by-source",
        Chart.Bar(ShopJson.Default.Visit)
            .X(r => r.Source).Y(r => r.Visits)
            .Encoding(Channel.X, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder("search", "direct", "social"); })
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .ToTemplate(),
        maxRows: 10_000);

    public static readonly ChartHandle<Stock> StockLevels = new("stock-by-warehouse", StockTemplate, maxRows: 10_000);

    public static readonly ChartGroup Overview = new("overview", Revenue, Traffic);
    public static readonly ChartGroup Inventory = new("inventory", StockLevels);
    public static readonly ChartManifest Manifest = ChartManifest.Create(Overview, Inventory);   // one per variant
}

// Program.cs, at start-up
foreach (var group in Charts.Manifest.Groups)
{
    var check = group.Validate(FlintBackend.ECharts);
    check.ThrowIfInvalid();
    if (check.Issues.Any(i => i.Code.StartsWith("fixed_", StringComparison.Ordinal)))
        throw new InvalidOperationException(string.Join("; ", check.Issues));
}

// per request, after the host authorized the caller for this group
var page = new DataPage(Charts.Overview, [Charts.Revenue.Rows(sales), Charts.Traffic.Withheld("restricted")]);
byte[] body = page.ToUtf8Bytes();                                            // or page.ToJson(), page.WriteTo(writer)

// asynchronous source: each part awaited; the page exists only when every part does
var page2 = new DataPage(Charts.Overview, [
    await Charts.Revenue.RowsAsync(db.Sales.AsAsyncEnumerable(), ct),
    Charts.Traffic.Withheld("restricted"),
], next: cursor);
await page2.WriteAsync(response.Body, ct);

// streamed: flat memory, WriteAsync only, single use; a failure after the first byte truncates the body
var page3 = new DataPage(Charts.Overview, [
    Charts.Revenue.Stream(db.Sales.AsAsyncEnumerable()),
    Charts.Traffic.Withheld("restricted"),
]);
await page3.WriteAsync(response.Body, ct);
```

The code blocks of `docs/delivery.md` are labelled and not compiled by a test, as the endpoint sample of `docs/row-data.md:121` is; as one file, a block that mixes type declarations and top-level statements is CS8803, so every sample marks its `// Charts.cs` and `// Program.cs` parts. `StockTemplate`, `sales` (an in-memory collection), `db` and `cursor` stand for the host's own members.

##### 14.4.5 Host embedding

`Graspea.Flint.Delivery` references nothing of ASP.NET Core. Both samples build the body in memory (`ToUtf8Bytes`, `WriteTo` over a buffer), so no synchronous write reaches the response stream, and a part that throws fails the request before any byte is sent; a streamed page is the exception described below. The samples are those of `docs/delivery.md`, section "Host embedding".

The HTTP routes keep the group, the manifest tag and the cursor in query parameters, never in a path segment, because URL parsing collapses the legal group names `.` and `..`: the manifest is at `/charts/manifest`, pages at `/charts/page?group=<name>&manifest=<tag>`, plus `&next=<cursor>` when set. The tag is the manifest's `ETag`, which the transport of §14.6 sends back with every page request.

```csharp
// Program.cs, after var app = builder.Build(); an uncompiled docs sample
app.MapGet("/charts/manifest", (HttpContext ctx) =>
{
    var m = Charts.Manifest;                                          // the caller's variant
    ctx.Response.Headers.ETag = $"\"{m.Hash}\"";
    ctx.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
    return ctx.Request.Headers.IfNoneMatch == $"\"{m.Hash}\"" ? Results.StatusCode(304)
        : Results.Bytes(m.Utf8Bytes.ToArray(), "application/json; charset=utf-8");
}).RequireAuthorization();

app.MapGet("/charts/page", async (HttpContext ctx, SalesStore store, string group, string manifest, string? next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (manifest != $"\"{Charts.Manifest.Hash}\"")                     // the browser holds an old manifest: restart
        return Results.Text(new DeliveryError("stale").ToJson(), "application/json; charset=utf-8", statusCode: 409);
    if (group != "overview" || !store.MayRead(ctx.User))               // authorize first, decode the cursor after
        return Results.Text(new DeliveryError("forbidden").ToJson(), "application/json; charset=utf-8", statusCode: 403);
    try
    {
        var result = await store.ReadAsync(ctx.User, next, ctx.RequestAborted);   // one consistent result per sequence
        var page = new DataPage(Charts.Overview,
            [await Charts.Revenue.RowsAsync(result.Sales, ctx.RequestAborted), Charts.Traffic.Withheld("restricted")],
            result.Next);
        return Results.Bytes(page.ToUtf8Bytes(), "application/json; charset=utf-8");
    }
    catch (ChartRowLimitException)                                    // the host's own code; it aggregates more
    {
        return Results.Text(new DeliveryError("row_limit").ToJson(), "application/json; charset=utf-8", statusCode: 422);
    }
}).RequireAuthorization();
```

```csharp
// WebSocket RPC: the host's own envelope, written with a Utf8JsonWriter over an IBufferWriter<byte>.
// The request carries { id, method: "manifest" | "page", group, manifest, next }; the host compares
// request.manifest with the bare Charts.Manifest.Hash and answers "stale" on a mismatch; the reply envelope carries the bare Hash.
var buffer = new ArrayBufferWriter<byte>();
using (var writer = new Utf8JsonWriter(buffer))
{
    writer.WriteStartObject();
    writer.WriteNumber("id", requestId);
    writer.WritePropertyName("result");
    page.WriteTo(writer);                                             // or Charts.Manifest.WriteTo(writer), error.WriteTo(writer)
    writer.WriteEndObject();
}
await socket.SendAsync(buffer.WrittenMemory, WebSocketMessageType.Text, endOfMessage: true, ct);
```

`SalesStore` is an example store that checks the caller's access, decodes the cursor only after that check, and answers a stale cursor with its own exception, which the host maps to an error document. A streamed page goes to the response with `await page.WriteAsync(ctx.Response.Body, ctx.RequestAborted)`. On an exception the host checks `ctx.Response.HasStarted`: false (nothing drained yet) still allows an error document; true means the body is a truncated prefix, so the host calls `ctx.Abort()` and the client restarts (§14.6). The same check applies to a transport failure of any page, which can come after the whole page was written. An RPC host that sends whole messages uses materialised parts.

**Manifest and page consistency across deploys.** A browser can keep an old manifest in memory across a server deploy, so pages written by new definitions could be merged with old specs. The pages do not carry the manifest hash, so the wire stays as §14.5 shows; the reference transport carries the `ETag` of the manifest the caller holds in every page request instead, the host compares it with the `ETag` of its current manifest (`"\"" + manifest.Hash + "\""`) and answers an error document with its own code when they differ (the samples use `stale`), and the client restarts from the manifest on any error. A WebSocket host carries the hash in its own RPC envelope.

##### 14.4.6 Streamed-asynchronous rows (v1, owner decision D9.6)

```csharp
public ChartPart Stream(IAsyncEnumerable<TRow> rows);   // on ChartHandle<TRow>: a single-use part
```

- **Write.** A page holding a streamed part is written only by `DataPage.WriteAsync`, with one `Utf8JsonWriter` over a pooled buffer that is drained with `Stream.WriteAsync` whenever it holds at least 16 KiB, after each streamed row and after each materialised part, as `FlintJson.SerializeAsync` drains (§13.5 step 6). Materialised parts of the same page go in as raw values; several streamed parts are read one after the other, in group order, through the same writer. `WriteAsync` first claims the page with a flag of its own, then every streamed part in group order (`StreamedRows.Claim`, `StreamedRows.cs:26-33`), before it writes anything, as `FlintJson` claims before writing (`Model/Json/FlintJson.cs:131`), so a second `WriteAsync`, a concurrent one, or a part already written in another page throws before the first byte (`Delivery/DataPage.cs:174-182`). A claim cannot be undone: when a later part was already written elsewhere, the parts claimed before it stay unwritten and are spent, while nothing reaches the stream. The synchronous finishers throw `InvalidOperationException` without claiming anything, as `FlintJson` does for an asynchronous streamed source (`StreamedRows.cs:16-17`).
- **Memory.** The largest single row or materialised part plus about 32 KiB of buffer, whatever the row count.
- **Failure.** A row, source, cap or cancellation failure comes out of `WriteAsync` with a prefix of the page in the stream: empty before the first 16 KiB drain, when the host can still answer an error document, otherwise truncated, invalid JSON with no withheld code (`docs/row-data.md:114`), when the host aborts the response, the client's JSON parse fails and it restarts from the first page (§14.6). A transport failure propagates unchanged at any time, as for every page (§14.4.2). The SDK writes nothing after a failure. Everything that can be checked early still is: the bad id, missing metadata and a `data` member at the handle, the page invariants at `DataPage`. A host decides `Withheld` before it writes.
- **When.** Use it for charts whose source is large or slow and whose rows cannot fail on a value (no value-dependent predicates, finite doubles under `JsonTypeInfo` metadata), over HTTP or another byte stream. Keep materialised parts where a clean error document matters or where the transport sends whole messages.
- **Not added.** A public after-row hook, a synchronous `Stream(IEnumerable<TRow>)`, and an option to turn a mid-stream failure into a withheld code.

##### 14.4.7 What is not added, and why

| Not added | Why |
|---|---|
| Public `WriteRows(Utf8JsonWriter, …)`/`WriteRowsAsync` on the template (D9.12) | needed only for the split of §14.3 or a host whose serializer writes the envelope; a writer-shaped member adds rules for the writer's position and options, a synchronous flush per row on a `Stream`-backed writer (§13.5 step 5), and an array left open when a row throws |
| `Rows(JsonArray)` | a host whose query returns JSON; additive later |
| Mapping a source failure to a withheld code per chart | a host that cannot pre-check its sources; today the host catches and calls `Withheld` |
| A page that answers several groups | a transport that batches group requests |
| The manifest hash inside pages | fixed definitions change only with a deploy, and a deploy under a live browser is caught on the request side: the reference transport sends the manifest's `ETag` with every page request and the host answers its own error code on a mismatch (§14.4.5, §14.6), with no wire change |
| A page-vs-template key check | redundant for typed rows (§14.7, "Keys") |
| SDK-defined withheld or error codes | host policy (wire rule 4) |
| HTTP helpers (`IResult` for manifest, page, error) | `Graspea.Flint.AspNetCore`, M4 |
| A published JavaScript client | owner decision D9.11: README guidance only; the same code is tested in `tests/contract-node` (§14.8.5) |
| `RowSource.FromPages` | D8.5 (c) stays deferred until the first host that pages its sources |
| `ValidationResult.ThrowIfAny(params string[] codes)` | until the start-up LINQ of §14.4.4 proves error-prone |
| Generated delivery members on source-generated types | `Template` covers them (§14.4.3); a new generated name would extend the reserved member names and cause FLINT104 for user types that declare it |
| SDK mirrors of the upstream proposals of §14.9 | only after upstream ships them |

#### 14.5 Wire contract v1

Three documents. Members are single lowercase words; the per-chart `input` is an unchanged data-free `ChartAssemblyInput`; the SDK writes these documents and never reads them.

```json
// manifest: the same bytes for every request of the users who see these charts; ETag = the quoted Hash
{ "contract": 1, "flint": "1.0.1",
  "charts": { "revenue-by-month": { "input": { "semantic_types": { }, "chart_spec": { } } },
              "traffic-by-source": { "input": { "chart_spec": { } } },
              "stock-by-warehouse": { "input": { "chart_spec": { } } } },
  "groups": { "overview": ["revenue-by-month", "traffic-by-source"], "inventory": ["stock-by-warehouse"] } }

// page: one group; every chart of the group exactly once; next only when another page follows
{ "contract": 1, "group": "overview",
  "charts": { "revenue-by-month": { "rows": [ { "month": "2024-01-01", "region": "North", "revenue": 1200.50 } ] },
              "traffic-by-source": { "withheld": "restricted" } },
  "next": "p2" }

// last page of that sequence: a finished chart carries "rows": []
{ "contract": 1, "group": "overview",
  "charts": { "revenue-by-month": { "rows": [ { "month": "2024-02-01", "region": "North", "revenue": 980.00 } ] },
              "traffic-by-source": { "withheld": "restricted" } } }

// error
{ "contract": 1, "error": { "code": "forbidden" } }
```

1. **`contract`** is in every document. It stays 1 while members are only added; a removal, a rename or a change of meaning bumps it. Clients ignore unknown members at every level.
2. **`flint`** is `FlintUpstream.Version`, in the manifest only. The browser compares it with the `flint-chart` version the host injects into its bundle at build time (no runtime version in 1.0.1, §14.1; the reference `fetchManifest` takes it as an argument, §14.6) and refuses to draw on a mismatch of the first three parts (`docs/versioning.md:115`); a host that deploys server and client from one commit may skip the check (N8).
3. **Ids and group names** match `[A-Za-z0-9_.-]+`; chart ids are host-chosen and unique per manifest; `charts` holds each chart once, even when it sits in several groups; `groups` lists ids in group order. Clients use ids as object keys, so `constructor` or `__proto__` are legal ids: the reference merge keys prototype-free objects (§14.6).
4. **A chart member of a page** has exactly one of `rows` (an array, possibly empty, whose elements are the template's `data.values` elements) or `withheld` (a non-empty host-defined code; the SDK defines none). Every page names every chart of its group; a finished chart carries `"rows":[]` (11 bytes per idle chart, D9.5); a chart withheld on any page is withheld, and the client keeps the first code.
5. **`next`** is present only when another page follows and is opaque to the client; it is never empty (`DataPage` throws `ArgumentException`), because a client that builds no `next` parameter for `""` would request the first page again forever. The host authorizes every page request, then decodes the cursor, reads one consistent result per sequence, and answers a stale cursor, or a page request made under an older manifest (§14.4.5), with an error document; the client restarts and never merges pages of different sequences. The client concatenates `rows` per chart in arrival order and draws once after the last page, because types, sizes and categories follow the rows present (§14.2.2).
6. **Empty `rows`** means nothing to draw: the client shows an empty state and never assembles `values: []` (§14.2.2).
7. **Deliberately absent**: counts, totals, more-pages flags, messages, timestamps, sizes, backend names, the SDK version and per-chart hashes. The merge needs only `rows`, `withheld` and `next`, so the contract stays small and additive and the manifest stays byte-stable for caching. Nothing depends on the host's transport.
8. **`Hash`** is outside the document and is the manifest's strong validator (the `ETag`): the lowercase hex SHA-256 of the stored manifest bytes. It is deterministic for the same definitions under the same SDK version and runtime serialisation; no canonical hash across runtimes or across semantically equal documents is promised, and `flint`, so the bytes and `Hash`, changes with every upstream version. `docs/versioning.md` gains "The delivery contract" (`docs/versioning.md:85-95`): what is frozen from the first release (the member names, shapes and meanings and the rule that readers ignore unknown members; changes are additive only), when `contract` bumps, this `Hash` rule, the CHANGELOG line that declares v1 stable, and the preview exit gate of the C# surface (D9.4).

The SDK writes members in the order shown; `next` is the only optional member and is written only when set. Clients must not depend on member order.

#### 14.6 Browser flow on 1.0.1

```js
// The README's browser guidance ("Charts in the browser"); the README block equals this file.
// transport(request, signal) returns a parsed document. request: { kind: 'manifest' } | { kind: 'page', manifest, group, next }
// A WebSocket host sends the manifest's hash in its own envelope.
export const httpTransport = (base) => {
  const tags = new WeakMap();                           // manifest object -> its ETag; pages carry it as `manifest`
  return async (req, signal) => {
    let url;
    if (req.kind === 'manifest') url = `${base}/manifest`;
    else {
      url = `${base}/page?group=${encodeURIComponent(req.group)}`;
      const tag = tags.get(req.manifest);
      if (tag) url += `&manifest=${encodeURIComponent(tag)}`;
      if (req.next) url += `&next=${encodeURIComponent(req.next)}`;
    }
    const res = await fetch(url, { signal, cache: req.kind === 'manifest' ? 'default' : 'no-store' });
    const doc = await res.json();                       // an error document arrives with its status
    if (req.kind === 'manifest' && doc !== null && typeof doc === 'object') tags.set(doc, res.headers.get('etag') ?? undefined);
    return doc;
  };                                                    // an aborted streamed page fails to parse: restart
};

// flint: the flint-chart version the client was built with; undefined skips the check
export async function fetchManifest(transport, signal, flint) {
  const m = await transport({ kind: 'manifest' }, signal);
  if (m.error) throw Object.assign(new Error('delivery'), { code: m.error.code });
  if (m.contract !== 1) throw new Error(`delivery contract ${m.contract}`);
  if (flint !== undefined && m.flint !== flint) throw new Error(`charts for flint-chart ${m.flint}, client built with ${flint}`);
  return m;
}

export async function collectGroup(transport, manifest, group, signal) {
  const rows = Object.create(null), withheld = Object.create(null);   // ids are host-chosen keys
  for (let next; ; ) {
    const page = await transport({ kind: 'page', manifest, group, next }, signal);
    if (page.error) throw Object.assign(new Error('delivery'), { code: page.error.code });   // restart from the manifest
    if (page.contract !== 1 || page.group !== group) throw new Error('unexpected page');
    for (const [id, part] of Object.entries(page.charts)) {
      if (part.withheld !== undefined) { withheld[id] ??= part.withheld; delete rows[id]; }
      else if (!(id in withheld)) rows[id] = (rows[id] ?? []).concat(part.rows);   // in order; never a spread
    }
    next = page.next;
    if (next == null) break;
  }
  return { rows, withheld };                            // memory only; clear on sign-out
}

export const inputFor = (manifest, id, rows) => ({ ...manifest.charts[id].input, data: { values: rows } });
```

The block is the README's browser guidance, byte-equal to `tests/contract-node/delivery-merge.mjs`; `delivery.test.js` pins the equality (§14.8.5 step 12). It holds no `flint-chart` version literal, which would go stale outside the README's generated sections: the host injects the installed version at build time, for example as a bundler define read from its own `package.json` or lockfile, because `flint-chart` exports no version and no `./package.json` (§14.1, U4), and passes it as `fetchManifest`'s third argument; `undefined` skips the check (rule 2). `httpTransport` keeps the group, the manifest tag and the cursor in query parameters (§14.4.5) and keeps each manifest's `ETag` header, sending the tag of the manifest the caller passes to `collectGroup` as the `manifest` parameter of every page request (a second, overlapping manifest load cannot change it); the caller restarts from `fetchManifest` on any error code, `stale` included. A WebSocket transport returns the payload of its RPC reply and carries the manifest hash in its own envelope.

| Step | 1.0.1 guidance |
|---|---|
| Draw once | after the last page: `withheld[id] \|\| !rows[id]?.length` shows the empty state; otherwise `mountChart(el, inputFor(m, id, rows[id]), { backend, availableSize: { width, height } })` from `flint-chart/interactive` (`src/interactive/index.ts:207-218` turns `availableSize` into `canvasSize`), or `<FlintChart>` from `flint-chart/react`, which remounts whenever `data.values` changes by reference (`src/react/index.ts:178-183`): pass the merged array once. The surface hands back no rows (`src/interactive/README.md:125-127`), so the client keeps them. Static or post-processed output: `assembleForBackend(backend, input)` from `flint-chart/validate` (four backends, clear errors, a 100,000-row cap; also re-exported from the root, `src/index.ts:70`), then the renderer |
| Not progressive | a prefix of the rows changes types, sizes, series and categories on 1.0.1 (§14.2.2); `set-data` is Vega-Lite only and keeps the mount input's analysis |
| Post-processing (N11) | ECharts: replace `tooltip.formatter` with one that escapes HTML, or set `tooltip.renderMode: 'richText'` with a plain-text formatter (an ECharts option, not probed here); upstream interpolates raw values (`src/echarts/templates/gantt.ts:98-99`, `src/echarts/instantiate-spec.ts:1466-1472`; the encoding tooltip is joined with `<br/>` at `:1388`, `:1427`), and a grep for `escape\|sanitize\|encodeHTML\|textContent\|renderMode\|richText` under `src/echarts` gives 0. Vega-Lite: `stripPrivateKeys(spec)` before `JSON.stringify`, `structuredClone` or `postMessage` (§14.2.1). Colours from computed CSS after assembly; a theme change re-assembles from the kept rows. `encoding.colors` is honoured by Vega-Lite, by Plotly only with a `theme_spec` (`src/plotly/assemble.ts:524-538`), never by ECharts or Chart.js |
| Order | ECharts Line series and Heatmap nominal categories follow row order, not `sortBy` (`src/echarts/templates/line.ts:313-327`, `src/echarts/templates/heatmap.ts:76-77`): the server writes rows in the declared order (§14.9, U1.1). Any `sortBy` also switches off the inferred order of month, day and quarter sequences and of dates drawn as categories (`src/core/resolve-semantics.ts:739`), so an explicit order lists every category |
| Keys | upstream takes the field keys from the first row of the merged array (`src/core/resolve-semantics.ts:434`; static series: `src/core/static-series.ts:179`); the sort check and `flint-chart/validate` read the keys of every row (`src/core/resolve-semantics.ts:192`, `src/validate/index.ts:198`). With typed rows every non-null row has every key (§14.7), so page order does not change key detection |
| Limits | §14.2.3 |
| Exposure | §14.2.3; withheld codes are data, never HTML |
| Caching | `ETag`/`If-None-Match` and a private cache on the manifest; `Cache-Control: no-store` on pages and errors |

#### 14.7 Start-up validation profile (N10)

`ChartGroup.Validate(backend)` runs `Template.Validate(backend)` per chart, with the existing codes (`unknown_field`, `series_field_missing` as a Warning, `sort_by_invalid`, `rows_not_inspected` and every spec rule; `ChartTemplate.cs:120-127`), re-paths them to `charts.<id>.input.…`, and adds five **Info** codes; each chart's Info issues follow its template issues, in group order (`Delivery/ChartGroup.cs:54-67`). They are reported from this entry point only, never from `ChartValidator.Validate` or `template.Validate`, so existing consumers see no new output. Info is right by the severity rule (`00-index.md:12`): upstream honours `aggregate`, `baseSize` and the override keys and drops nothing; the profile is host policy on top of the upstream mirror. `IsValid` is unchanged; a host fails start-up on the `fixed_` prefix (§14.4.4).

| Code (Info) | Condition | Upstream fact (1.0.1) |
|---|---|---|
| `fixed_type_missing` | a bound encoding entry (a non-blank `field`, or `aggregate` `count`) without `type` | the type is inferred from the rows present (`src/core/decisions.ts:442-453`, `src/core/semantic-types.ts:274`); an explicit `type` wins (`src/core/resolve-semantics.ts:549-550`) |
| `fixed_order_missing` | a `nominal` or `ordinal` encoding entry whose `sortBy` is not a JSON array holding at least one category value (a string, a boolean or a finite number), including `x`, `y` and `color` | only array strings and mapped channels survive; a field name is rejected off Vega-Lite and row-dependent on it, and a rejected `sortBy` drops its `sortOrder` (`src/core/resolve-semantics.ts:111`, `:122-165`); a channel sort is a value sort. Known false positive: an ordinal field whose semantic type has a built-in sequence, or a date drawn as a category, is ordered without `sortBy` (`:738-747`); no exemption ships, so the Info stays and the fix is an explicit order that lists every category (`docs/validation-codes.md`, "Known false positive"). Conversely, any `sortBy` switches that inferred order off (`:739`): once a channel carries an explicit order, ECharts templates that read only `ordinalSortOrder` (Heatmap, `src/echarts/templates/heatmap.ts:76-77`) follow row order, so the explicit order lists every category and the server writes rows in that order |
| `fixed_aggregate` | any `aggregate` | rows are final; aggregation follows the rows present (`src/core/aggregate.ts:104`). Not checkable: a Heatmap cell with two or more rows gets an implicit `sum` or `average` (`:34-90`); one row per cell is a documented host rule |
| `fixed_host_override` | `chartProperties.chartType`, `arrange` or `pivot` with a non-null value | host-state keys, exempt from `unknown_chart_property` (`Generated/Tables/CatalogTables.g.cs:14`; `Validation/Rules/ValidationRun.cs:536`); they revert silently when the gate fails (`src/core/pivot.ts:964`) |
| `fixed_size` | `baseSize` or `canvasSize` present | the layout follows the container: `mountChart` derives `canvasSize` from `availableSize` (`src/core/compute-layout.ts:495-505`, `:519-546`) |

The inputs are public model members: `ChartSpec.Encodings` with `EncodingValue.Encodings` (`Model/ChartSpec.cs:36`, `Model/EncodingValue.cs:20`), `ChartEncoding.Type`, `Aggregate` and `SortBy` (`Model/ChartEncoding.cs:19`, `:24`, `:34`), and `ChartSpec.BaseSize`, `CanvasSize` and `ChartProperties` (`Model/ChartSpec.cs:41`, `:46`, `:52`). Template-dependent facts (declared channels, band axes) stay in the internal catalog and are not checked. The codes join `docs/validation-codes.md` under a heading "Fixed definitions (ChartGroup.Validate only)", each row citing its fact; `Validation/Codes.cs` gains the constants. The checks live in the internal `Validation/Rules/FixedDefinitionChecks.cs`, which reads the document back from the template's stored bytes (`Delivery/ChartGroup.cs:59`) and shares the per-entry bound test and the category-value test with `ValidationRun`, made internal there with no change of behaviour.

**Keys.** For typed rows the metadata writes every member of every row (`RowMaterializer.cs:51-58`, `:87-101`), so every page of a static row shape has the keys `RowMaterializer.StaticKeys` lists (`:175-188`), and `template.Validate(backend)` already checks the spec's fields against those keys (`unknown_field`, `ValidationRun.cs:145-146`, `:259-260`). A `null` row is written as `null` (`docs/row-data.md:109`) and is `row_not_object`, an Error (`docs/validation-codes.md:40`): the host does not produce one. Dynamic shapes (dictionary, `JsonNode`, `object` or polymorphic rows) have no static keys (`RowMaterializer.cs:179-185`), so the key rules are skipped and `rows_not_inspected` says so; validate a real sample in tests with `ChartValidator.Validate(handle.Template.Bind(sample), backend)` and assert on the key codes, because `unknown_field` is a Warning and `inconsistent_row_keys` an Info (`docs/validation-codes.md:47-52`, `:69`) and `ThrowIfInvalid` throws only on an Error (`Validation/ValidationResult.cs:12`, `:16-22`).

#### 14.8 Integration plan

##### 14.8.1 M0: the 1.0.1 gaps of `v1.0.1.1-preview.1`

`v1.0.1.1-preview.1` shipped most of the 1.0.x sync (`CHANGELOG.md`, section `[1.0.1.1-preview.1]`): `FlintUpstream.Version = "1.0.1"` and the regenerated catalog; typed `reverse`, `mid`, `domain`, `bins`, `colors` and `title` on the encoding (`src/core/types.ts:62-77`), `chart_spec.label`, `source` and `chrome`, and `interaction_spec`; and `sort_by_invalid` aligned with upstream's `invalid_sort` (`src/core/resolve-semantics.ts:122-165`), where a field name is valid on Vega-Lite only. M0 is what it left out, in two parts.

**M0a, the `scheme` union: done** (blocking, §14.1). On `v1.0.1.1-preview.1` `ChartEncoding.Scheme` is a `string` (`Model/ChartEncoding.cs:39`) in the model, its converters and the spec-to-C# core. A property cannot carry two types, and the preview rule keeps the old shape as `[Obsolete]` for one release where the old and the new shape can coexist (`docs/versioning.md:38`), so the union arrives as a new member next to the old one:

- `ColorScheme` (`Model/ColorScheme.cs:13-40`), a sealed union class in the pattern of `LabelOverflow` and `ChannelTitle`: an internal constructor, `Name` and `Stops` (upstream calls the array "stops"), implicit conversions from `string` and from `string[]`, `ToString()`, and no `Equals` override. Its `ColorSchemeConverter` (`Model/Json/Converters/CoreUnionConverters.cs:107-154`) reads a string or an array of strings and throws `JsonException` on any other token; nothing is registered in `FlintJsonContext`.
- `ChartEncoding.ColorScheme` owns the `scheme` wire member. `ChartEncoding.Scheme` stays as a `[JsonIgnore]` bridge marked `[Obsolete]` under `FLINT0002`: its getter reads `ColorScheme?.Name`, null when the scheme is a list of stops, and its setter assigns `ColorScheme` (`Model/ChartEncoding.cs:36-49`). `docs/deprecations.md` lists it, deprecated in `1.0.1.1-preview.2` and deleted in the release after it; its `PublicAPI.Shipped.txt` lines stay, and no line is removed.
- `FlintJson` reads an array `scheme`, which threw `JsonException`. The spec-to-C# core accepts it (`src/Graspea.Flint.CSharp/Spec/SpecShape.cs:171-175`) and writes it typed as `ColorScheme = new string[] { … }`, never a collection expression (`src/Graspea.Flint.CSharp/Writing/ValueWriter.cs:338-340`); `flint-cs` and `Graspea.Flint.Generators` reported it as `FLINT103` before.
- Tests: the converter, the members and the parity of the IR's `ChartEncoding.scheme` union (exactly the string and string-array members) in `tests/Graspea.Flint.Tests`, the spec shape, the fluent writer and one case of `tests/Graspea.Flint.CSharp.Tests/Contract/edge-cases.jsonl`; one bridge test runs under `#pragma warning disable FLINT0002`.

**M0b, the Image-Charts backend: done.** Delivery does not depend on it: the browser flow assembles on the four layout backends (§14.2.3, §14.8.5 step 12), and Image-Charts sends the rows to an external service, so it is outside this flow (§14.2.3, "Exposure"). The owner decided on 2026-11-08: the member `FlintBackend.ImageCharts`, appended last, with the wire and IR id `image-charts`; the Error code `image_charts_unsupported`; Image-Charts joins the default target set, as Excel does; the channel list is synthetic, with the ten chart types of `IMAGE_CHARTS_TYPE_MAP` (`src/image-charts/chart-types.ts:33-44`) and per type the channels `assembleImageCharts` reads intersected with the union the other five backends declare, curated in `generator/overrides.json` (`imageChartsChannels`), so no builder gains a member (`docs/versioning.md:31`); no new topics or tags. It holds:

- `FlintBackend.ImageCharts` with its `CatalogTables.Backends` and `ValidationRun.Id` rows; six generator backends, with a synthetic Image-Charts registry, because Image-Charts has no `*AllTemplateDefs` export; the regenerated `flint-chart@1.0.1` fixtures with 23 crafted cases for the Image-Charts rules.
- The validator port, `Validation/Rules/ImageCharts.cs` and the native-backend branches of `ValidationRun`, which explains every recorded Image-Charts throw (`docs/validation-codes.md`).
- `tests/contract-node/flint.js` `BACKENDS` with six entries. The Image-Charts template lookup is an own key of `IMAGE_CHARTS_TYPE_MAP`, because `isImageChartsSupported` uses `in` and accepts `Object.prototype` names (`chart-types.ts:47-49`).

It landed as three commits: the generator, the IR and the fixtures, with the `FlintBackend` member that the generated tables name; the validator; the harness and the docs. On the fixtures, adding Image-Charts to the null target changes `IsValid` only for a Sparkline with a facet or without `y`, which has no Excel template (`docs/fixtures.md`).

##### 14.8.2 Changes by area

| Area | Change |
|---|---|
| `Fluent/ChartTemplate.cs` | `ToJson()`; two `internal` accessors (spec bytes, `JsonTypeInfo<TRow>?`) for the handle |
| `src/Graspea.Flint/Delivery/*.cs` (new, M1) | the nine types of §14.4.1, plus the internal `RowLimit` (the counting source wrapper) and `DeliveryNames` (the id and group-name check, an ordinal character loop with no `Regex`); reuses `StreamedRows`, `PooledBufferWriter` and `FlintJsonContext` |
| `Model/Json/DrainingJsonWriter.cs`, `Model/Json/JsonDepth.cs` (new, internal), `Model/Json/StreamedDocument.cs`, `Model/Json/StreamedRows.cs`, `Validation/ValidationIssue.cs`, `Validation/Rules/ValidationRun.cs` (M1) | the drain buffer and the depth limit, which `StreamedDocument` now composes with no change of behaviour; `StreamedRows.Claim` takes the message of its refusal; an internal `ValidationIssue.WithPath` for the manifest paths; `ValidationRun`'s per-entry bound test and category-value test made internal for §14.7 |
| `Model/ChartEncoding.cs`, `Model/ColorScheme.cs` (new), `Model/Json/Converters/CoreUnionConverters.cs`, `src/Graspea.Flint.CSharp/Spec/SpecShape.cs`, `src/Graspea.Flint.CSharp/Writing/ValueWriter.cs`, `docs/deprecations.md` (M0a) | the `scheme` union and the `FLINT0002` bridge of §14.8.1 |
| `Validation/Codes.cs`, `Validation/Rules/FixedDefinitionChecks.cs` (new), `docs/validation-codes.md` | the five Info codes (§14.7) |
| `src/Graspea.Flint/PublicAPI.Unshipped.txt` | 60 lines, 46 for `Graspea.Flint.Delivery`, one for `ChartTemplate<TRow>.ToJson()`, 12 for the `scheme` union of M0a and one for `FlintBackend.ImageCharts` (M0b), with no `*REMOVED*` line, in a file that `v1.0.1.1-preview.1` emptied into `PublicAPI.Shipped.txt`; `npm run generate` keeps and sorts hand-written lines (`generator/src/emit/public-api.ts:36-44`), and the drift check pins the file (`.github/workflows/ci.yml:43-44`) |
| `Graspea.Flint.csproj` | the `Description` (`:6`) gains ", and write the envelope that delivers fixed charts to a browser" before "; there are no other dependencies"; "This package emits JSON only" stays true |
| `Model/FlintBackend.cs`, `Validation/Codes.cs`, `Validation/Rules/ImageCharts.cs` (new), `Validation/Rules/ValidationRun.cs`, `Validation/Rules/Excel.cs`, `src/Graspea.Flint.CSharp/Spec/CatalogCheck.cs`, `generator/src/**`, `generator/overrides.json`, `tests/fixtures/flint-chart@1.0.1/*`, `tests/contract-node/flint.js`, `docs/validation-codes.md`, `docs/fixtures.md`, `docs/generator.md` (M0b) | the Image-Charts backend of §14.8.1 |
| `docs/delivery.md` (new), `docs/README.md`, `docs/row-data.md`, `docs/versioning.md` | contract v1, both host samples (uncompiled, labelled as `docs/row-data.md:121` is), a link to the README's browser code instead of a third, untested copy, limits and security notes; the section "The delivery contract" in `docs/versioning.md`, after "The source generator" |
| `README.md` section "Charts in the browser" | after "Late-bound, asynchronous and streamed rows": one short C# block, the region `readme:delivery` of `tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs`; the browser guidance of §14.6, the JavaScript of §14.6 and its step table, as the only client the SDK documents (D9.11); a link to `docs/delivery.md` for the contract |
| `README.md` rule 9, regenerated `docs/package/AGENTS.md`, `CHANGELOG.md` | rule 9 after rule 8 (`README.md:377`): "Declare charts once as `ChartHandle`s in a `ChartGroup`; serve the `ChartManifest` on mount and one `DataPage` per group; the browser merges `data.values` and draws once after the last page". `npm run generate` copies the section into the packed `docs/package/AGENTS.md` (`generator/src/cli.ts:146`; `Directory.Build.props:44`), which the drift check pins (`ci.yml:43-44`) |
| `tests/Graspea.Flint.Tests/Delivery/*` (M1) | §14.8.5 steps 2-10; step 1 joins `tests/Graspea.Flint.Tests/Templates/ChartTemplateTests.cs` |
| `samples/Graspea.Flint.Samples`, `samples/Graspea.Flint.Samples.Aot/Program.cs` (M1, M2) | the Samples project also writes `samples/out/delivery/manifest.json`, `overview.json`, `overview-2.json` (a continuation with `"rows":[]`), `overview-withheld.json`, and one `Bind(all)` document per chart; the subfolder keeps the exact top-level list of `tests/contract-node/samples.test.js` (`:42`, `:57-59`); the AOT sample adds a handle, a group, a manifest and a page under the existing Native AOT publish (`ci.yml:89-90`) |
| `tests/contract-node/delivery.test.js`, `delivery-merge.mjs` (M2) | §14.8.5 step 12; `delivery-merge.mjs` is the code of §14.6. It reverses the 0.5.1 draft's choice of no JavaScript delivery test, because the SDK now owns the envelope |
| `docs/design/00-index.md` | the Contents entries of parts 9-11 with this design; the decision 16 row |
| Untouched | `release.yml`, the `ci.yml` jobs, `scripts/check-nupkg.sh`, the nuspecs, `Directory.Packages.props`, `SECURITY.md`, `docs/maintainers.md`, the sync bot (its `SYNC_PATHS`, `.github/workflows/upstream-sync.yml:151`, covers `src/**`) |

##### 14.8.3 Milestones

| Milestone | Content | Effort (one maintainer) | Status |
|---|---|---|---|
| M0a | §14.8.1: the `scheme` union as `ChartEncoding.ColorScheme`, with the `FLINT0002` bridge | — | done, unreleased |
| M0b | §14.8.1: the Image-Charts backend, the six-backend harness | 3-5 days | done, unreleased |
| M1 | `ToJson()`, the Delivery namespace with the streamed part (§14.4.6), the codes, the .NET tests, the docs, the AOT sample | 7-9 days | done, unreleased |
| M2 | the samples subfolder, the contract-node test, README rule 9 and the browser section, the CHANGELOG entry | 1-2 days | done, unreleased |
| M4 (deferred) | `Graspea.Flint.AspNetCore`: `IResult` for the manifest (ETag/304), the page (`no-store`) and the error; the package plumbing of option B in §14.3; its own AOT sample | 2 days of code, 2 days of plumbing | deferred |
| — | no npm client: the browser side is README guidance only (owner decision D9.11) | — | — |

M3 (streaming) moved into M1 by D9.6 and M5 (the npm client) was dropped by D9.11; the numbers stay so that cross-references hold. The first estimate of 2-4 days for the whole of M0 was too low: M0b alone takes 3-5 days (§14.8.1).

##### 14.8.4 Public API changes and compatibility

- **Added**: `ChartTemplate<TRow>.ToJson()`, the `Graspea.Flint.Delivery` types, for M0a `ChartEncoding.ColorScheme`, `ColorScheme` and `ColorSchemeConverter`, and for M0b `FlintBackend.ImageCharts`, in `PublicAPI.Unshipped.txt`; for M0b also the Error code `image_charts_unsupported`. RS0016 is an error (part 3 §8.6, `docs/design/03-sdk-versioning-runbook-tests-risks.md:58`; `.editorconfig:30`). Under the preview rule no experimental id is needed (D9.4); before the first stable release the preview exit gate of `docs/versioning.md` ("The delivery contract") decides whether the Delivery C# surface becomes stable API or gets `[Experimental("FLINTEXP002")]`.
- **Changed**: the `scheme` member (M0a, §14.8.1). `FlintJson` now reads an array `scheme`, which threw `JsonException`, and `flint-cs` and `Graspea.Flint.Generators` write it typed, which was `FLINT103`. `ChartEncoding.Scheme` is deprecated (`FLINT0002`, `docs/deprecations.md`): it keeps its signature and its `PublicAPI.Shipped.txt` lines, reads the scheme name and returns null when the scheme is a list of colour stops. The `CHANGELOG.md` `[Unreleased]` entry records `ChartEncoding.ColorScheme` under "Added" and `ChartEncoding.Scheme` under "Deprecated". For M0b, validation without a backend also covers Image-Charts for its ten chart types: messages name it, it adds Infos and its Errors, and a Sparkline with a facet or without a field on `x` or `y` becomes invalid (§14.8.1). Nothing else: no other existing signature, and no wire byte of `FlintJson` for a document it could read before.
- **New contract**: wire v1 (§14.5), documented in `docs/versioning.md` as a contract of its own ("The delivery contract"); its first release declares v1 stable in the CHANGELOG, from then on additive only. The `[Unreleased]` entry carries that line.
- **Unchanged**: the row contract, the byte identity of streamed and materialised documents, `Build()`/`ToJson()` never validating, the generator's reserved member names, every existing entry point.

##### 14.8.5 Tests, in TDD order

1. **`ChartTemplateTests.ToJson_*`** (`tests/Graspea.Flint.Tests/Templates/ChartTemplateTests.cs`): the UTF-8 of `ToJson()` equals `FlintJson.SerializeToUtf8Bytes(d)` with `d = template.Bind(ChartData.FromUrl("x"))` and `d.Data = null`; for a deferred builder without a `Data`-setting `Configure` action it equals `builder.ToJson()`, which pins an equality no test covers today.
2. **`ChartHandleTests`**: ids inside and outside `[A-Za-z0-9_.-]+`, `__proto__` among the legal ones; a template without row metadata throws `ArgumentException` at construction, before any row, and so does a template from a deferred builder whose `Configure` action adds `Extensions["data"]`; a `maxRows` below 1 throws `ArgumentOutOfRangeException`; `Withheld("")` throws.
3. **`ChartPartTests.Rows_*`**, modelled on the byte-identity family of `tests/Graspea.Flint.Tests/Serialization/FlintJsonStreamingTests.cs` (`:84-123`): the elements of `rows` equal the `data.values` elements of `FlintJson.SerializeToUtf8Bytes(template.Bind(all))` for a `null` row, escaped strings, decimals, an empty source and both metadata kinds; a row converter that writes raw JSON gives `rows` equal to the streamed document's bytes and JSON-equal (`JsonNode.DeepEquals`) to `Bind`'s values; rows nested to the depth limit are accepted exactly when the bound document is written; a value-dependent predicate throws `ArgumentException` at its row; a non-finite `double` throws under `JsonTypeInfo` metadata and is written `null` under `RowOptions`.
4. **`ChartPartAsyncTests.RowsAsync_*`**: the same over an asynchronous source; the token reaches the source; cancellation in the middle throws `OperationCanceledException` and returns no part, and the next call on the same handle succeeds.
5. **`MaxRows`**: `MaxRows` rows give a part; `MaxRows + 1` throw `ChartRowLimitException` with `Chart` and `Limit`, synchronous and asynchronous, and the excess row is never serialised, so a bad excess row still gives the limit; `MaxRows = int.MaxValue` does not overflow the count.
6. **`ChartGroupTests`** and **`ChartManifestTests`**: name rules, at least one chart, unique ids, duplicate ids and group names across groups throw; charts in first-appearance order; a handle in two groups appears once in `charts` and in both `groups`; each `input` equals the template's stored bytes and `ToJson()`; `Groups` and `Charts` are read-only snapshots of the arrays given. The bytes are pinned against the full expected manifest text with `FlintUpstream.Version` spliced in for `flint`, built from minimal fixed specs: `Utf8Bytes` equals its UTF-8; `Hash` equals the lowercase hex SHA-256 of those expected bytes, computed in the test, and matches `^[0-9a-f]{64}$`; two manifests built from fresh, independently created templates are byte-equal and hash-equal (`tests/Graspea.Flint.Tests/Delivery/ChartManifestTests.cs:25-78`). No frozen hash literal is pinned: the sync bot may change only its `SYNC_PATHS`, which hold `src/**` and `tests/fixtures/**` but not `tests/**` (`.github/workflows/upstream-sync.yml:151-160`), and the manifest carries `flint`, so a frozen hash would fail every sync pull request. Splicing the version keeps the check of cross-machine determinism without that drift; a sync that changes how the minimal test specs serialise still fails the test, which is intended drift detection that a maintainer fixes by hand (§14.4.2, "Hash").
7. **`DataPageTests`**: a foreign part, a missing chart, a repeated chart and an empty `next` throw; `Charts` is a read-only snapshot; parts given out of order are written in group order; `next` is written only when set; `ToUtf8Bytes`, `ToJson` and `WriteTo` agree and may repeat; `WriteTo` under an indented writer is JSON-equal; uneven pages, concatenated per chart, equal `Bind(all)`'s `data.values` elements.
8. **`DataPageWriteAsyncTests`**: on a stream whose synchronous `Write` and `Flush` throw, as Kestrel's does, the bytes equal `ToUtf8Bytes()` and `FlushAsync` is called exactly once; a stream whose `WriteAsync` throws after taking some bytes, and one whose `FlushAsync` throws after the whole page, each give the stream's exception unchanged, and nothing more is written.
8a. **`DataPageStreamedTests`** and **`DeliveryStreamingMemoryTests`** (§14.4.6): a page with a streamed part, written by `WriteAsync` to that stream, equals byte for byte the page with the same rows from `RowsAsync`, also with a materialised part before and after it, with large materialised parts around it and with two streamed parts; more than 16 KiB of rows reach the stream in several `WriteAsync` calls and memory stays flat over a million streamed rows (the pattern of §13.13's streaming tests); rows nested to the depth limit are accepted as in `Bind`; `WriteTo`, `ToUtf8Bytes` and `ToJson` throw `InvalidOperationException` before writing; a second `WriteAsync` throws; a value-dependent predicate, a non-finite `double` under `JsonTypeInfo` metadata, a source exception, `MaxRows + 1` rows and cancellation in the middle each propagate out of `WriteAsync` and leave the stream with a proper prefix of the page: empty when the failure comes before the first 16 KiB drain, otherwise non-empty and not valid JSON; one case of each kind also fails after more than 16 KiB of rows, so a drained, non-empty prefix is covered; claiming happens before the first byte, so a second or concurrent `WriteAsync`, or a part already written in another page, throws with the stream untouched.
9. **`DeliveryErrorTests`**: an empty code throws; the document is `{"contract":1,"error":{"code":"…"}}` with the code escaped as any JSON string.
10. **`ChartGroupValidateTests`**: each of the five codes with a positive and a negative spec, at its manifest path; the template codes re-pathed; `ChartValidator.Validate` and `template.Validate` report none of the five.
11. **Docs and AOT**: the C# block of the README section "Charts in the browser" is the region `readme:delivery` in `tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs` itself (`:283-308`), which reuses that file's `Sale` type and asserts on the page it writes, so `RegionFiles` (`:335-340`) gains no entry; rule 9 is prose. The AOT sample runs every public Delivery member through `JsonTypeInfo` entries only and returns 1 when the forms of the manifest, the page or the error differ, under the CI publish with trimming and AOT warnings as errors (`.github/workflows/ci.yml:89-90`); the `PublicAPI.Unshipped.txt` drift check.
12. **`tests/contract-node/delivery.test.js`** (M2), reading `samples/out/delivery/` through the samples artifact (`ci.yml:111-129`): the exact file list; `contract === 1` and `flint` equal to the version `flint.js` reports (pattern of `samples.test.js:52-55`); pages served by an in-memory transport keyed by `next` and merged with `delivery-merge.mjs`; the merged input deep-equals the `Bind(all)` document; it assembles each chart with rows on the four layout backends (`vegalite`, `echarts`, `chartjs`, `plotly`) intersected with the backends that have its template, never Excel or a later backend (§14.2.3), asserts that this set is non-empty, and compares warnings with `[]`, not output (Vega-Lite output carries functions); withheld and empty charts assert "not assembled". With in-memory transports and synthetic pages it also covers: the ids `__proto__`, `constructor` and a numeric-looking id; the group names `.` and `..` through `httpTransport`'s URL building, which keeps them in the query; the manifest tag taken from the manifest response only, bound to the manifest object and sent with every page request of the caller that passes it (two overlapping manifest loads keep their own tags), and no tag for a manifest the transport did not fetch; a chart withheld after earlier rows (the rows dropped, the first code kept, later rows discarded); unknown additive members ignored at every level; a `flint` mismatch rejected and `undefined` as the opt-out; a wrong contract and a wrong group rejected; an error document after an earlier page rejected with its code; a transport rejection and a JSON parse failure propagated; nothing assembled before `collectGroup` resolves. Finally it pins the README's js block of "Charts in the browser" as equal to `delivery-merge.mjs`, after LF normalisation.

##### 14.8.6 Release criteria (M1 and M2)

Every test above green on `net8.0` and `net10.0` (`tests/Graspea.Flint.Tests/Graspea.Flint.Tests.csproj:5`); the AOT publish with 0 trimming and AOT warnings; `delivery.test.js` green on 1.0.1; the `PublicAPI.Unshipped.txt` drift check green; `docs/delivery.md`, the versioning section, the validation-code rows and README rule 9 regenerated; one CHANGELOG entry; no AI attribution anywhere.

#### 14.9 Upstream proposals for flint-chart

Each proposal can be filed upstream as a feature request. None blocks this design: the workaround column is what the docs tell hosts. The status column is 1.0.1.

**U1. Order, pins, colours and transforms**

| # | Proposal | Status and blocker in 1.0.1 | Workaround |
|---|---|---|---|
| U1.1 | A declared category domain that every template honours: wire `SemanticAnnotation.sortOrder` (documented as "Explicit ordinal ordering", `src/core/field-semantics.ts:84-85`) into channel ordering, category counts, ECharts Heatmap categories and colour-series order | Still blocked. `sortOrder` becomes `canonicalOrder` (`src/core/field-semantics.ts:871-883`, `:1163`), which nothing reads as an order (the theme grounding reads it only as a sign that a series is ordered, `src/core/theme/ground.ts:2440-2442`); `ordinalSortOrder` comes only from the type's known sequences or a date order (`src/core/resolve-semantics.ts:738-747`). The new `categoryOrder` (`src/core/channel-decisions.ts:67-121`) serves Vega-Lite only (`src/core/plan.ts:587`), and the new `set-order` update reorders only a drag-reorder category axis on the Vega-Lite surface (`src/vegalite/interactions/runtime.ts:215-220`) | `encoding.sortBy` as a JSON-encoded array string (`SortBy.ExplicitOrder(...)`, for example `"[\"North\",\"South\"]"`), which orders the ECharts Bar axis (`src/echarts/templates/utils.ts:19-23`), and rows in that order, which is what orders ECharts Line series and the nominal categories of the ECharts Heatmap (ad-hoc run, 0.5.1 and 1.0.1). A literal array now draws an `invalid_sort` warning on every backend instead of being ignored (`src/core/resolve-semantics.ts:122-123`, `:199`), `validateChartInput` throws on it (`src/validate/index.ts:202`), and the SDK cannot read it on `v1.0.1.1-preview.1`, where `ChartEncoding.SortBy` is a `string` (`Model/ChartEncoding.cs:34`; ad-hoc run); `fixed_order_missing` checks the declaration (§14.7) |
| U1.3 | ECharts and Chart.js honour the pins that Vega-Lite and Plotly honour (`includeZero_*`, `logScale_*`); the axis-type override on every backend; caller `options` beat template parameters; ECharts accepts the shared scheme ids | Unchanged: `src/core/analyze.ts:100`, `:185`, `:210` (Vega-Lite only), `src/plotly/instantiate-spec.ts:156-180`, `src/echarts/assemble.ts:243-244`; `src/echarts/colormap.ts` is byte-identical. The new encoding `scheme` (`src/core/types.ts:58-62`) reaches ECharts only on a continuous `visualMap`, and a scheme name ECharts lacks is ignored (`src/echarts/color-controls.ts:5-15`) | Post-process the ECharts option (axis `min`/`scale`, colours) |
| U1.4 | A stable value-to-colour map | Partly shipped. `SemanticAnnotation.colors` binds colours to values (`src/core/field-semantics.ts:87-92`), and a per-chart `encoding.colors` is folded into it (`src/core/types.ts:71`, `src/core/color-controls.ts:30-44`). Vega-Lite honours it with or without a theme (`src/vegalite/assemble.ts:611`, `src/vegalite/value-colors.ts:45-70`, `src/core/theme/ground.ts:2295-2296`); Plotly only with a `theme_spec` (`src/plotly/assemble.ts:524-538`, `src/plotly/theme.ts:1779`). ECharts and Chart.js ignore it: colour by series index in encounter order (`src/echarts/instantiate-spec.ts:1196-1240`), palette switch above 10 categories (`:926-931`) | `encoding.colors` for Vega-Lite and themed Plotly; post-process ECharts series colours by series name |
| U1.5 | A sticky transform override, or a warning when it reverts | Unchanged: `src/core/pivot.ts:964` (`:980` for `arrange`), no warning code | No transform overrides in fixed definitions (`fixed_host_override`, §14.7) |

U1.2 (schema-only assembly) and U1.6 (data statistics as input, data by reference, a two-phase compile) stay withdrawn: they matter only for drawing before the last page, which §14.2.2 does not document.

**U2. JSON-safe output**

| # | Proposal | Status and blocker in 1.0.1 | Workaround |
|---|---|---|---|
| U2.1 | An option for JSON-safe output: formatters and callbacks expressed as declarative descriptors (format strings, label maps) that the host or a small runtime turns into functions, as Plotly output already is (`src/plotly/assemble.ts:20-21`) | No such option (`grep -rniE 'jsonSafe\|json-safe\|pureJson\|noFunctions' src` is empty). 28 function sites under `src/echarts` and 5 under `src/chartjs` (§14.2.1); the Vega-Lite output carries two functions under `_interactionSemantics`. `renderSvg` avoids shipping functions only by sending static SVG | Assemble in the browser; keep the spec and rows, never the output (§14.2.1) |
| U2.2 | Escape HTML in ECharts formatters | Unchanged: raw interpolation at `src/echarts/templates/gantt.ts:98-99` and `src/echarts/instantiate-spec.ts:1466-1472`, the encoding tooltip joined with `<br/>` at `:1388`, `:1427`; no escape helper under `src/echarts` | Replace `tooltip.formatter` after assembly with an escaping formatter, or use `tooltip.renderMode: 'richText'` with a plain-text formatter (§14.6) |

**U3. `url` data**

| # | Proposal | Status and blocker in 1.0.1 | Workaround |
|---|---|---|---|
| U3.1 | Resolve `data.url` through a host-supplied loader, or throw a clear error for a URL-only `data` and for a missing `data` | Partly shipped, opt-in: no loader, but `flint-chart/validate` gives the clear errors on Vega-Lite, ECharts, Chart.js and Plotly (`src/validate/index.ts:44-51`): "input.data is required (provide { values: [...] })" for a missing `data` and "input.data must provide inline values" for a URL-only one (`:151-158`, `:326`); `validateChart` returns them as an `assembly_failed` error (`:448-456`). `assemble*` still read `input.data.values ?? []` unguarded (§14.1) | The client merges `data: {values}` before assembly (§14.6). The SDK reports `data_url_not_fetched` (Info) for a URL-only document (`ValidationRun.cs:288-294`) |

**Other**

| # | Proposal | Status and blocker in 1.0.1 | Workaround |
|---|---|---|---|
| U4 | A runtime version export | Unchanged: no version string in any `dist` bundle and no version export (§14.1) | The version the host injects at build time, for example as a bundler define read from its own `package.json` or lockfile, passed to `fetchManifest` (§14.6); `flint` in the manifest (§14.5) |
| U5 | Large-data safety: loops instead of `Math.min(...x)` | `RangeError` from about 125,000 rows (`src/core/field-semantics.ts:623`); 85 lines with a spread `Math.min`/`Math.max` in `src/core` and the ECharts and Vega-Lite assemblers and 269 under `src` (0.5.1: 39 and 172), by the line count of `Math\.(min\|max)\(\.\.\.` | Keep a chart at or below about 10,000 rows and aggregate on the server (§14.2.3); `MaxRows` (§14.4.2) |

Every proposal matters to a host that draws on complete data: U2.2 for safety; U1.1, U1.3, U1.4 and U1.5 for the order, pins, colours and transforms of a complete result; U2.1, U3.1, U4 and U5 for transport and robustness.

#### 14.10 Decision points for the owner

| # | Decision | Options | Recommendation | Trade-offs |
|---|---|---|---|---|
| D9.1 | Where the data API lives | (a) a `Graspea.Flint.Delivery` namespace in core; (b) a `Graspea.Flint.Delivery` package plus public primitives in core; (c) `Graspea.Flint.AspNetCore` | **(a)** | (b) isolates only C# churn after core goes stable, at the plumbing cost of §14.3; (c) fails N6 |
| D9.2 | Group noun | (a) `ChartGroup`/`groups`/`group`; (b) `ChartPanel`/`panels` | **(a)** | neutral; "panel" presumes a UI |
| D9.3 | Home of the N10 codes | (a) five Infos from `ChartGroup.Validate`; (b) an own result type; (c) host-owned | **(a)** | (b) adds a second result type; (c) makes every host re-derive the 1.0.1 `sortBy` rules |
| D9.4 | Stability marker for the delivery C# surface | (a) the preview rule (`docs/versioning.md:38`); (b) `[Experimental("FLINTEXP002")]` on every public type of `Graspea.Flint.Delivery` and on `ChartTemplate<TRow>.ToJson()` | **(a)**; (b) if core goes stable before a host has run this, decided at the preview exit gate of `docs/versioning.md` ("The delivery contract") | (b) costs a `NoWarn` in every consumer; `[Experimental]` marks only the generator contract today (`docs/versioning.md:79-81`) |
| D9.5 | Page invariant | (a) every chart of the group once on every page; (b) free parts | **(a)** | (a) makes `charts[id]` reliable at 11 bytes per idle chart; (b) pushes the check to every client |
| D9.6 | Streaming in v1 | (a) defer, reserving the shape of §14.4.6; (b) ship §14.4.6 in v1 | **Owner, 2026-11-08: (b)**, as an opt-in `Stream` part (§14.4.6); materialised parts stay the default | (b) adds a second failure model (truncated JSON after the first byte) for the parts that use it; (a) would have left the memory of a large asynchronous source unbounded |
| D9.7 | JavaScript contract test | (a) `delivery.test.js` in M2; (b) none | **(a)** | cheap; pins the snippet against real 1.0.1 output |
| D9.8 | Cap option | (a) `MaxRows` optional, null by default; (b) none; (c) default 10,000 | **(a)** | (c) throws in production by default; (b) leaves the memory of an asynchronous source unbounded |
| D9.9 | `ChartTemplate<TRow>.ToJson()` | (a) now; (b) defer | **(a)** | tiny, designed in the 0.5.1 draft, useful outside a manifest (for example `flint-chart-mcp`'s `validate_chart`) |
| D9.10 | Per-chart wrapper | (a) `{ "input": … }`; (b) the document directly | **(a)** | one more level for the client; room for additive per-chart members |
| D9.11 | npm client | (a) now; (b) later; (c) no package, README guidance only | **Owner, 2026-11-08: (c)** | the browser side is the README's guidance (§14.6), kept honest by the contract-node test of D9.7; no npm id, lockfile or publish path |
| D9.12 | Public `WriteRows`/`WriteRowsAsync` | (a) defer; (b) add now | **(a)** | needed only for the split or a host with its own serializer; carries the writer-target trap (§14.4.7) |

The implementation follows the recommendation of every open point, and the owner's outcome of D9.6 and D9.11; the owner accepted the open points with ADR 16 on 2026-11-08. The ADR records these as ADR-16.1 (D9.1, D9.4, D9.12), ADR-16.2 (D9.6), ADR-16.3 (D9.2, D9.5, D9.10), ADR-16.4 (D9.3), ADR-16.5 (D9.8), ADR-16.6 (D9.7, D9.11) and ADR-16.7 (D9.9).

#### 14.11 Deferrals and defaults

**Deferrals with triggers**

| Deferred | Trigger |
|---|---|
| Public `WriteRows*` and the package split (§14.3) | wire v2 needed while core is stable, or a host on another cadence (D9.12) |
| `Graspea.Flint.AspNetCore` (M4) | the first stable release and a host on minimal APIs |
| `RowSource.FromPages` (D8.5 (c)) | the first host that pages its sources |
| Per-chart mapping of a source failure to a withheld code | a host that cannot pre-check its sources |
| A page for several groups | a transport that batches group requests |
| The manifest hash inside pages | definitions redeployed under live pages that the request-side manifest tag of §14.6 cannot catch |
| `ValidationResult.ThrowIfAny(params string[] codes)` | the start-up LINQ proves error-prone |
| `Rows(JsonArray)` | a host whose query returns JSON |
| Template-dependent profile facts | a public catalog API |
| `fixed_order_missing` exemptions for built-in sequences (month, day, quarter, direction) and for dates drawn as categories | a generator-emitted sequence table, or a hand port pinned by a 1.0.1 test |

**Defaults applied (say if you object)**

- Ids and group names match `[A-Za-z0-9_.-]+` and are checked; `charts` order is first appearance, `groups` order is group order, a page's order is group order.
- `next` is written only when present and is never empty; the error document carries `code` only; a null withheld or error code throws `ArgumentNullException`, an empty code or an empty cursor `ArgumentException`.
- `Hash` is lowercase hex SHA-256 of `Utf8Bytes`; the manifest's `WriteTo` is a raw write of the stored bytes.
- A `DataPage` holds its parts' bytes and may be written any number of times, unless a part is streamed (§14.4.6).
- `maxRows`, when set, is at least 1 (`ArgumentOutOfRangeException`, an `ArgumentException`) and caps one part; the count cannot overflow.
- The 10,000-row figure is documentation unless a host configures `MaxRows`.
- `ToJson()` decodes on each call and caches nothing; the manifest holds the bytes.
- The reference HTTP transport puts the group, the manifest tag and the cursor in query parameters, never in a path segment (§14.4.5, §14.6).
