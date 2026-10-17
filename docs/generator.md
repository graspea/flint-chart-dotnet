# Generator

The generator in `generator/` reads the pinned `flint-chart` package and writes four things: the committed IR (`generator/ir/flint-ir.json`), the C# under `src/Graspea.Flint/Generated/`, the documentation that depends on the pinned version (the marked sections of `README.md` and the whole `docs/package/AGENTS.md`), and the fixture corpus under `tests/fixtures/` ([fixtures.md](fixtures.md)). The design is in [part 2 §7](design/02-sdk-serialisation-validation-codegen.md). This page lists the decisions the first run against `flint-chart@0.5.1` produced.

## Commands

```
cd generator
npm ci --ignore-scripts
npm test                 # node:test via tsx
npm run typecheck        # tsc, no emit
npm run generate         # extract, emit, fixtures
npm run generate -- --check   # drift check: writes nothing, lists every file a fresh run would change, exits 1
npm run fixtures         # the fixture step alone
npm run probes           # re-record verify/probes.json (not part of generate)
npm run crosscheck -- <flint-chart checkout>    # one-off, not in CI (fixtures.md)
npm run ir-diff -- <before.json> [after.json]   # markdown table; after defaults to ir/flint-ir.json
cd .. && dotnet build -warnaserror && dotnet test -f net10.0
```

| Step | What it does |
|---|---|
| `extract` | Sets `TZ=UTC` and loads `overrides.json`. Builds `IR.types` with the TypeScript checker, starting from `package.json` `types`, and `IR.catalog` from the runtime registries. Diffs the result against the committed IR and appends each removal to `retired.json`. Writes `ir/flint-ir.json` canonically: sorted keys, two-space indent, LF, trailing newline. |
| `emit` | Reads only the IR and `overrides.json`. Checks property kinds and the entry, builder, enum, channel and property names (design §7.6), then writes `Generated/Constants`, `Generated/Options`, `Generated/Theming`, `Generated/Json`, `Generated/Tables`, the fluent facades `Generated/Builders`, `Generated/Enums` and `Generated/Entries`, and `Generated/catalog.json` (the IR `catalog` node, canonical JSON, packed as `catalog/catalog.json`). Removes stale `*.g.cs` files and rewrites `PublicAPI.Unshipped.txt` (hand-written lines are kept; generated lines are owned by the generator). Rewrites the marked sections of `README.md` and writes `docs/package/AGENTS.md` (see [Documentation](#documentation)). |
| `fixtures` | Runs in a process of its own (the seed and `TZ` are process-wide): `TZ=UTC`, Park-Miller seed 42 in place of `Math.random`, then `flint-chart/test-data`. Writes `cases.jsonl`, `expectations.jsonl` and `manifest.json` to `tests/fixtures/flint-chart@<version>/`, and an empty `known-failures.txt` when there is none ([fixtures.md](fixtures.md)). |

`npm run generate` is deterministic: a second run, or a run from an empty `Generated/` and `ir/`, leaves `git status` clean. `generate -- --check` runs the same steps without writing: it fails when the IR, `retired.json`, a generated file (including a stale `*.g.cs`), `PublicAPI.Unshipped.txt`, a marked `README.md` section, `docs/package/AGENTS.md` or a fixture file would change, or when the `generator/package.json` pin differs from the installed version.

`npm run probes` records the runtime facts the design cites as probed (null handling, the backends that apply the theme, the inference of the values .NET writes, and the `Date.parse` results the validator relies on) in `generator/verify/probes.json`; `test/probes.test.ts` keeps the record equal to a fresh run.

Not implemented yet: the `[Obsolete]` emission of retired members.

## A new flint-chart version

The scheduled workflow `upstream-sync.yml` does this ([part 4](design/04-companion-upstream-watch-bot.md)); by hand it is the same steps on a branch with the bot's name:

```
git switch -c upstream/flint-chart-0.6.0
cd generator && npm ci --ignore-scripts
npm run install-upstream -- 0.6.0   # the gate, see below
npm run fixtures                    # forward-compat probe: the old model reads the new fixtures
dotnet test ../tests/Graspea.Flint.ContractTests -f net10.0 --filter Category=RoundTrip
npm run generate
cd .. && dotnet build -warnaserror && dotnet test
npm --prefix generator run ir-diff -- <(git show main:generator/ir/flint-ir.json)
```

`npm run install-upstream -- <version>` (`scripts/install-upstream.mjs`) stops at the first failure: `npm install --save-exact --ignore-scripts flint-chart@<version>` (no package code runs), `npm audit signatures` (registry signature and Sigstore provenance), then `scripts/verify-provenance.mjs`, which requires the provenance to name the package and tarball digest, the repository `microsoft/flint-chart`, the workflow `.github/workflows/npm-publish.yml` and the packument's `gitHead` as the commit. Last it writes `generator/mcp-pin.json`. Versions without provenance (0.1.0) fail the gate.

The scripts in `generator/scripts/` are plain Node modules with `node:test` tests in `generator/test/`, run by `npm test`:

| Script | Used by | Does |
|---|---|---|
| `install-upstream.mjs`, `verify-provenance.mjs` | the bot's `generate` job and the manual path | the gate above |
| `pick-upstream-version.mjs` | the bot's `detect` job | the lowest stable version above the pin that is not skipped, the queued, stale and backport lists, and the inputs of the suggested tag |
| `upstream-report.mjs` (`npm run upstream-report`) | the bot's `generate` job | the pull request body and `result.json`: IR diff rows with a computed severity, labels, the suggested tag |
| `release-check.mjs` | `release.yml` | the tag grammar, the first three parts against `FlintUpstream.Version` and the pin, the CHANGELOG entry, the release title and body |
| `versions.mjs`, `gha.mjs` | the scripts above | version grammar; GitHub Actions outputs and process helpers |

## Documentation

`src/emit/docs.ts` writes the documentation that depends on the pinned version from the same IR as the C#, so the names in it cannot drift from the compiled surface.

`README.md` is hand-written except for the lines between `<!-- flint:generated:<id> start -->` and `<!-- flint:generated:<id> end -->`. Every id below appears exactly once; a missing, duplicated, unknown or reversed marker fails generation.

| Section id | Content |
|---|---|
| `upstream-badge` | the flint-chart badge (version from the IR, linked to the upstream release) and the CI badge |
| `upstream-version` | the "tracks" sentence under the install line |
| `chart-catalog` | one row per chart type: `ChartType` constant, `Chart` entry, backends, channels, properties |
| `semantic-types` | the semantic types grouped by category, with the constant name where it differs (`NameType`) |
| `mcp-commands` | the `mcp.json` snippet for `npx -y flint-chart-mcp@<version>` and the hosted endpoint |
| `agent-identifiers` | the real `FlintBackend`, `ChartType`, `Channel`, `SemanticType` and `ThemePreset` names, the `flint-chart-mcp` command and the upstream `flint-chart-author` skill at the pinned tag |

The NuGet badge (`vpre` or `v`) and the two `dotnet add package` lines are outside the markers; the release pull request sets them ([maintainers.md](maintainers.md)).

`docs/package/AGENTS.md` is generated whole: a header, the README `## For AI agents` section verbatim (marker lines removed), then the chart catalog and semantic types tables. `Directory.Build.props` packs it at the package root, next to `catalog/catalog.json`; `scripts/check-nupkg.sh` fails CI when either is missing from the package. `Directory.Build.props` also strips the marker lines from the packed copy of `README.md`.

`generator/mcp-pin.json` holds the `flint-chart-mcp` version for `mcp-commands` and the upstream git ref for links: `{"flint-chart": "<pin>", "flint-chart-mcp": "<version>", "upstreamRef": "<tag or commit>"}`. The gate script that installs a new upstream version (`npm run install-upstream -- <version>`) resolves both: `npm view flint-chart-mcp@<pin> version`, or the newest version below the pin; and the upstream tag equal to the pin, or the version's `gitHead` commit when upstream did not tag it (upstream tagged 6 of its first 12 versions). `generate` stays offline. The badge links the upstream release at the tag, or the commit tree; the skill link uses the same ref. Generation fails when `flint-chart` in the file differs from the IR version; when the two versions differ, the section says which version it uses and why.

Every C# block of `README.md` is a `#region readme:<name>` of `tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs`, which compiles and runs it; a block without a region fails that test. `tests/contract-node/readme.test.js` assembles the quick-start JSON with the pinned `flint-chart`.

## Failure policy

Generation fails, naming the IR path, on:
- a construct the type mapping cannot map;
- a member-name or class-name collision;
- a property whose kind differs across the backends of one template;
- an `overrides.json` entry that matches nothing.
- two generated type names that coincide (a builder, an enum or its value table), or an entry that takes the name of a hand-written `Chart` member (`Of`);
- two option values of one discrete property that give the same enum member;
- an enum name whose key repeats the template name (`RegressionRegressionMethod`), until `renames["<Template>/enum/<key>"]` names it;
- a `PublicAPI.Shipped.txt` that ships an enum's `Default` as anything but 0, or two members of one enum with the same number.

The fix is always one reviewed line in `generator/overrides.json`, which is validated against `overrides.schema.json`.

IR paths:
- `Type.member.member` for object members;
- `[]` for an array element;
- `{*}` for a record value;
- `catalog.chartTypes[<name>].backends.<backend>.properties[<key>]` for the catalog;
- `<Template>/property/<key>`, `<Template>/channel/<channel>` and `<Template>/enum/<key>` for template-level overrides (the last renames the enum of a discrete property);
- `<Template>/enum/<key>/<value>` renames one enum member; `<value>` is the raw string, or the JSON of another value (`[10,50]`). The wire value does not change.
- `catalog.chartTypes[<name>]`, `catalog.channels[<channel>]`, `catalog.semanticTypes[<name>]` and `catalog.themePresets[<id>]` rename one catalog constant (`ChartType`, `Channel`, `SemanticType`, `ThemePreset`); the wire value does not change.

`category` lives on each backend entry (`catalog.chartTypes[<name>].backends.<backend>.category`), not on the chart type, because upstream's categories differ between backends (`Lines & Areas` on vegalite, `Line & Area` on the others).

## Channel count: 26, 27 or 28

All three numbers appear in upstream material. The generator takes the union, derives it at extraction time and never hard-codes it.

| Set | Count | Difference |
|---|---|---|
| Core `channels` constant (`T:1988`) | 26 | `radius` and `angle` are in the constant, but no template declares them |
| Channels declared by templates (all backends) | 25 | adds `theta` (Excel Pie and Donut) |
| Constant plus declared channels: **the IR `catalog.channels`** | **27** | the 26 plus `theta` |
| ...plus the members of `channelGroups` | 28 | adds `text`; no template declares it, so it is not emitted |

`Channel` therefore has 27 constants on 0.5.1, including `Theta` and excluding `Text`. The unit test `GeneratedConstantsTests.Channel_ConstantsEqualTheIrChannelUnion` and the generator test `extract-registry` pin these counts.

## Type mapping of the 0.5.1 d.ts

Emission starts at two roots. `AssembleOptions` goes to `Graspea.Flint` (`Generated/Options`). `ThemeSpec` goes to `Graspea.Flint.Theming` (`Generated/Theming`, 53 classes). `ChartAssemblyInput`, `ChartEncoding` and `SemanticAnnotation` are extracted for the parity test only. So are the anonymous core types, stored under their SDK names: `ChartData`, `ChartSpec`, `ChartSize` and `ContinuousMarkCrossSection`.

| Construct found | Where | Decision |
|---|---|---|
| `number \| {x, y, elasticity?, maxStretch?, seriesCountAxis?}` | `AssembleOptions.continuousMarkCrossSection`, and the same member flattened into `ThemeCompileDefaults` | override → hand-written `Graspea.Flint.ContinuousMarkCrossSection` (both paths) |
| `{width: number; height: number}` | `AssembleOptions.facetFixedPadding`, `ThemeCompileDefaults.baseSize/canvasSize/facetFixedPadding` | `dedup` → `Graspea.Flint.ChartSize`; the emitter checks the shape (two required numbers) |
| `SizeToken = string \| number` | `TypeRole.size` | override → `JsonNode` (a token such as `text.100` or a pixel number); written through `JsonNodePassThroughConverter` |
| `Record<string, unknown>` inside the index signature of `ThemeChartDefaults` | `ThemeChartDefaults{*}{*}` | override → `JsonNode`, giving `Dictionary<string, Dictionary<string, JsonNode?>>` with `VerbatimKeyDictionaryConverter<Dictionary<string, JsonNode?>>` |
| `Partial<Omit<ThemeSpec, 'extends' \| 'id' \| 'label' \| 'ink' \| 'type' \| 'variants'>>` (mapped type) | `ThemeVariant.then` | `flatten` → generated class `ThemeVariantThen` with the members the checker resolves (the 13 policy blocks); see the note below |
| `interface ThemeCompileDefaults extends Partial<AssembleOptions>` | `ThemeSpec.compileDefaults` | flattened by the checker into the 29-member class `ThemeCompileDefaults`; no override needed (design §7.4) |
| `ThemeGuard`, `NumericGuard` | `ThemeVariant.when` | plain interfaces with optional members; mapped without an override |
| `ThemeChartDefaults { [chartType: string]: … }` | `ThemeSpec.chartDefaults` | an interface with only a string index signature is a record → `Dictionary<string, …>` |
| String-literal unions, including aliases such as `Presence`, `Frequency` and `LegendPlacement` | throughout the theme tree | aliases are inlined; → `string?`, with the literals listed in `<remarks>` |
| A single string literal (`swatch?: 'auto'`) | `ThemeLegend.swatch` | → `string?` with the value in `<remarks>` |
| `[number, number]` | `ThemeMarks.sizeRange` | → `double[]?` |
| `T[]` / `Array<T>` | `furniture`, `placement`, `redundantChannels`, `dash`, … | → `T[]?` |
| Nested object literal | for example `ThemeInk.series.status` | nested class `<Parent><Prop>` (`ThemeInkSeriesStatus`) |
| `any`, `never`, unions of objects | `ChartAssemblyInput.data`, `chartProperties`, `encodings` | parity-only types, never emitted, so no override |

Named interfaces are keyed by their name in the IR. When two different interfaces with one name are reachable (for example from two declaration files), extraction fails and names both files, rather than mapping the second one onto the first one's shape.

Note on `ThemeVariant.then`: the `interaction` member it inherits from `ThemeSpec` is an inline object, so it becomes a second class, `ThemeVariantThenInteraction`, structurally equal to `ThemeSpecInteraction`. This is accepted. Reusing the class across a mapped type would need declaration-identity tracking in the emitter.

Every generated class has these properties:
- `sealed partial`, with `[JsonNumberHandling(Strict)]`;
- every member nullable, with `[JsonPropertyName]` and `WhenWritingNull`;
- `VerbatimKeyDictionaryConverter<T>` on map members and `JsonNodePassThroughConverter` on `JsonNode` members;
- a trailing `[JsonExtensionData] Dictionary<string, JsonElement>? Extensions`.

Every map value type (other than `JsonNode`) gets a `[JsonSerializable]` line in the generated `FlintMapJsonContext`, from which `VerbatimKeyDictionaryConverter<T>` resolves it; on 0.5.1 that is `Dictionary<string, JsonNode?>` (the `chartDefaults` values) and `string`, which is always listed.

Required upstream members are nullable too, because "omitted" and "null" are one state in C# (design §5.2).

## `overrides.json` entries for 0.5.1

| Entry | Reason |
|---|---|
| `unions["AssembleOptions.continuousMarkCrossSection"]`, `unions["ThemeCompileDefaults.continuousMarkCrossSection"]` = `Graspea.Flint.ContinuousMarkCrossSection` | union of number and object; the hand-written class and its converter own the wire shape |
| `unions["TypeRole.size"]` = `JsonNode` | `string \| number` with different meanings (a token reference or a pixel size); writing a number as a string would change it |
| `unions["ThemeChartDefaults{*}{*}"]` = `JsonNode` | `unknown`: chart-property values are an open namespace, as `chartProperties` is |
| `unions["Radar Chart/property/filled"]` = `JsonNode` | `filled` is `binary` on vegalite and `discrete [true, false]` on echarts, chartjs and plotly; the only kind mismatch on 0.5.1 (design §7.4). It affects the builder method only: `RadarChartBuilder<TRow>.Filled(JsonNode? value)`, and no enum |
| `flatten` = `ThemeVariant.then` | mapped type, accepted as the generated class `ThemeVariantThen` |
| `dedup["Graspea.Flint.ChartSize"]` = the four `{width, height}` paths | one public size type instead of four structurally identical classes |
| `renames["Regression/enum/regressionMethod"]` = `RegressionMethod` | rule 3 gives `RegressionRegressionMethod`, which repeats the template name; generation fails on such a name until a rename names the enum (design §7.6 rule 3) |
| `renames["Tree/enum/orient/LR"]` = `LeftToRight`, `renames["Tree/enum/orient/TB"]` = `TopToBottom` | rule 4 gives `Lr` and `Tb`, which do not say what they mean; the wire values stay `"LR"` and `"TB"` |
| `renames["Boxplot/enum/whiskerMethod/minmax"]` = `MinMax` | rule 4 gives `Minmax`, which hides the two words of "Min–Max"; the wire value stays `"minmax"` (owner decision, 2026-10-17) |
| `renames["catalog.semanticTypes[Name]"]` = `NameType` | the semantic type `Name` would collide with the `Name` property of the `SemanticType` struct (design §7.6 rule 6) |
| `renames["catalog.themePresets[powerbi]"]` = `PowerBi`, `renames["catalog.themePresets[powerbi-light]"]` = `PowerBiLight` | rule 5 gives `Powerbi` and `PowerbiLight`, which do not spell the product name; the wire ids stay `"powerbi"` and `"powerbi-light"` (owner decision, 2026-10-17) |
| `docs["AssembleOptions.maxStretch"]`, `docs["ThemeCompileDefaults.maxStretch"]` | the real default is 1.5 (`src/core/compute-layout.ts:178`); the d.ts says 2 |
| `docs["AssembleOptions"]` | Excel reads no `options` |
| `docs["ThemeSpec"]` | the theme is applied by Vega-Lite and Plotly only (recorded probe); `T:3013` says Vega-Lite only |
| `backendsApplyingTheme` = vegalite, plotly | recorded probe: an unknown preset throws only there, and output changes only there |
| `hostStateKeys` | the ten keys read even where undeclared (design §2); the list is not provably closed |
| `ignoredOptions` | `maxStretchX/Y`, `bandStepFitCapacityX/Y` and `facetColumns` are overwritten by the four layout backends. The member docs of `AssembleOptions` and `ThemeCompileDefaults` say so, and the IR carries the list for the validator |

## Naming results on the 0.5.1 IR

- **Entry names:** the 48 entry names (rule 2) are unique. Examples: `Line`, `KpiCard`, `Ecdf`, `RangedDot`, `SankeyDiagram`, `Boxplot`, `Donut` and `Doughnut`.
- **Channel and property collisions:** the only ones are `opacity` on Area Chart, Bubble Chart and Scatter Plot. In each case the channel is `Opacity` and the property is `OpacityValue`, as the design expected. No channel or property maps to a reserved core name. Range Area Chart, Regression and Strip Plot declare an `opacity` property but no `opacity` channel, so their property stays `Opacity(double)`; Funnel Chart's `sort` property is `Sort(FunnelChartSort)`.
- **Type names:** the 48 builders, 45 enums and 45 value tables have distinct names; no entry takes `Of`. One enum name repeats its template name (`RegressionRegressionMethod`) and is renamed to `RegressionMethod`.
- **Enum members:** no two option values of one property give the same member. The members not named from a string value are listed below.
- **Constants:** the only collision is the semantic type `Name` (see `renames` above). Two theme presets are renamed for readability, `powerbi` -> `PowerBi` and `powerbi-light` -> `PowerBiLight`; their wire ids do not change.
- **Template renames:** one enum rename, `Regression/enum/regressionMethod` -> `RegressionMethod`, and enum member renames on two enums, `Tree/enum/orient/LR` -> `LeftToRight`, `Tree/enum/orient/TB` -> `TopToBottom` and `Boxplot/enum/whiskerMethod/minmax` -> `MinMax` (see `renames` above); no channel or property method is renamed.

`npm run emit` computes these names on every run, so a collision in a future IR fails generation; `generator/test/naming-ir.test.ts` pins these results against the committed IR, and the .NET `FacadeSweepTests` re-derive every builder method, entry and enum from the IR and compare them with the compiled assembly.

## Fluent facades

The builders, enums and entries emitters (design §4.2, §7.4) share one model (`src/emit/fluent.ts`), so they cannot disagree on a name. Per chart type:

- `Builders/{Pascal(name)}Builder.g.cs`: `public sealed partial class {Name}Builder<TRow> : ChartBuilder<{Name}Builder<TRow>, TRow>` with an internal constructor. Channels are the union over the template's backends, in declaration order (first backend first); each has three overloads, `(string field, SemanticType? semanticType = null)`, `(Expression<Func<TRow, object?>> selector, SemanticType? semanticType = null)` and `(ChartEncoding encoding)`, each delegating to `Encode`. Properties are the union by key, in key order: discrete -> the per-template enum, binary -> `bool`, continuous -> `double`, a kind that differs across backends -> `JsonNode?` (only through `overrides.json`); each delegates to `Property`. The XML docs carry the upstream label, the backends that declare the channel or property, and the continuous ranges per backend. Each builder class has an `<example>`: two anonymous rows with one numeric member per channel, bound through the entry to the first two channels that are not `column` or `row` (`Chart.Line(rows).X(r => r.X).Y(r => r.Y).ToJson()`), so the snippet compiles for every template.
- `Enums/{Pascal(chartType)}{Pascal(key)}.g.cs` (or the `<Template>/enum/<key>` rename): the public enum and an internal `…Values.ToJson` table. `Default` (= 0) omits the key; the other members follow the union of option values (backends in IR order, options in declaration order). A value a backend does not offer is noted in its doc; `Validate(backend)` reports it as `invalid_option_value`. Every member carries an explicit number (`Monotone = 1,`), and the numbers are stable across upstream releases (design §8.2): the enums emitter reads the `Graspea.Flint.<Enum>.<Member> = N` lines of `PublicAPI.Shipped.txt`, keeps each shipped number, and gives a new member the next number above every number the enum has shipped, wherever upstream inserts its value; a retired member's number is never reused. `Default` is always 0. Generation fails when `PublicAPI.Shipped.txt` ships `Default` as anything but 0 or gives two members of one enum the same number. Before the first release nothing is shipped, so the numbers follow the union order.
- `Entries/Chart.{Entry}.g.cs`: `{Entry}<TRow>(IEnumerable<TRow>, RowOptions? = null)` with `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`, `{Entry}<TRow>(IEnumerable<TRow>, JsonTypeInfo<TRow>)`, `{Entry}(ChartData)` returning the `object` builder, and `[Obsolete(…, error: true)] {Entry}(string)`, so `Chart.Line("https://…")` is CS0619 instead of a `LineChartBuilder<char>`. A literal `null` second argument is ambiguous (CS0121) between the first two; the `options` docs say to omit it.

The public-api emitter owns the builder and enum types and the `Chart.{Entry}` overloads (`EmitResult.ownedMembers`); the hand-written core (`ChartBuilder`, `GenericChartBuilder`, `Chart.Of`) keeps its own lines. On 0.5.1 the facades add 1400 generated API lines: 192 entries, 944 builder lines (48 types, 896 methods) and 264 enum lines (45 types, 219 members).

Enum member names (design §7.6 rule 4) come from the raw value when it is a string (`step-after` -> `StepAfter`, `LR` -> `Lr` before the `TreeOrient` rename; the literal `default` -> `DefaultLiteral`, because `Default` means "omit the key"). A non-string value is named from its label. The 0.5.1 labels repeat the value in brackets (`Europe [10, 50]`) or mark the default (`Show (default)`); one trailing `[…]` and one trailing `(default)` are dropped before `Pascal`, which gives the design's `Europe`. Without a usable label a non-negative integer is `N<value>`; anything else fails generation.

| Enum member | Value | Label |
|---|---|---|
| `GaugeChartShowProgress.Show`, `.Hide` | `true`, `false` | "Show (default)", "Hide" |
| `TreemapBreadcrumb.Show`, `.Hide` | `true`, `false` | "Show (default)", "Hide" |
| `MapProjection.DefaultLiteral` | `"default"` | "Default" |
| `MapProjectionCenter.WorldAtlantic`, `.WorldPacific`, `.China`, `.Usa`, `.Europe`, `.Japan`, `.India`, `.Brazil`, `.Australia`, `.Russia`, `.Africa`, `.MiddleEast`, `.SoutheastAsia`, `.SouthAmerica`, `.NorthAmerica`, `.Uk`, `.Germany`, `.France`, `.Korea` | `[lon, lat]` pairs, written as the label | "World (Atlantic) [0, 0]", …, "Korea [128, 36]" |
| `SunburstChartLabelRotate.Horizontal` | `0` | "Horizontal" |

An array value is written as its label, not as the array: flint-chart accepts an array option only when it is the same object as the registry's (`o.value === value` in `normalize-properties.ts`), which a parsed JSON array never is, so the array would be dropped with `invalid-option-value`. The label is coerced to the option (Info `coerced-option-label`). `test/enum-wire-values.test.ts` sends every emitted member through the installed flint-chart on each backend that offers it.

`GaugeChartShowProgress` and `TreemapBreadcrumb` are enums although their values are `true` and `false`: the IR declares both properties `discrete`, and the kind decides the type (discrete -> enum, binary -> `bool`), so `Gauge(...).ShowProgress(GaugeChartShowProgress.Hide)` sits next to `Line(...).ShowPoints(false)`. The enum keeps what a `bool` would lose: `Default` omits the key, and `Validate(backend)` checks the value against the declared options like every other discrete value. A caller who prefers a boolean writes `Property("showProgress", false)`.

Radar Chart `filled` is binary on vegalite and discrete `[true, false]` on echarts, chartjs and plotly; `overrides.json` maps it to `JsonNode`, so it is `RadarChartBuilder<TRow>.Filled(JsonNode? value)` and has no enum. That is why the 46 discrete template-and-key pairs of the design give 45 enums.

### Method names per template (0.5.1)

| Template | Entry | Builder | Channel methods | Property methods |
|---|---|---|---|---|
| Area Chart | `Chart.Area` | `AreaChartBuilder<TRow>` | X, Y, Color, Opacity, Column, Row | IndependentYAxis(bool), Interpolate(AreaChartInterpolate), OpacityValue(double), StackMode(AreaChartStackMode), XAxisType(AreaChartXAxisType), YAxisType(AreaChartYAxisType) |
| Bar Chart | `Chart.Bar` | `BarChartBuilder<TRow>` | X, Y, Color, Opacity, Column, Row | CornerRadius(double), IndependentYAxis(bool), ShowValueLabels(bool), XAxisType(BarChartXAxisType), YAxisType(BarChartYAxisType) |
| Bar Table | `Chart.BarTable` | `BarTableBuilder<TRow>` | Y, X, Color, Column, Row | IndependentYAxis(bool), MaxRows(double), ShowPercent(bool) |
| Boxplot | `Chart.Boxplot` | `BoxplotBuilder<TRow>` | X, Y, Color, Opacity, Column, Row | Dodge(BoxplotDodge), IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), ShowOutliers(bool), ShowPoints(bool), WhiskerMethod(BoxplotWhiskerMethod) |
| Bubble Chart | `Chart.Bubble` | `BubbleChartBuilder<TRow>` | X, Y, Size, Color, Opacity, Column, Row | OpacityValue(double) |
| Bullet Chart | `Chart.Bullet` | `BulletChartBuilder<TRow>` | Y, X, Goal, Color, Column, Row | IndependentYAxis(bool) |
| Bump Chart | `Chart.Bump` | `BumpChartBuilder<TRow>` | X, Y, Color, Detail, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), Interpolate(BumpChartInterpolate), LogScaleX(bool), LogScaleY(bool) |
| Calendar Heatmap | `Chart.CalendarHeatmap` | `CalendarHeatmapBuilder<TRow>` | X, Color | CornerRadius(double) |
| Candlestick Chart | `Chart.Candlestick` | `CandlestickChartBuilder<TRow>` | X, Open, High, Low, Close, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), MaWindow(double), ShowMa(bool) |
| Choropleth | `Chart.Choropleth` | `ChoroplethBuilder<TRow>` | Id, Color, Detail | Region(ChoroplethRegion) |
| Combo Chart | `Chart.Combo` | `ComboChartBuilder<TRow>` | X, Y, Column, Row | CornerRadius(double) |
| Connected Scatter Plot | `Chart.ConnectedScatter` | `ConnectedScatterPlotBuilder<TRow>` | X, Y, Order, Color, Detail, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool) |
| Density Contour | `Chart.DensityContour` | `DensityContourBuilder<TRow>` | X, Y, Column, Row | BinCount(double), ShowPoints(bool) |
| Density Plot | `Chart.Density` | `DensityPlotBuilder<TRow>` | X, Color, Column, Row | Bandwidth(double), IndependentYAxis(bool) |
| Donut Chart | `Chart.Donut` | `DonutChartBuilder<TRow>` | Size, Color, Column, Row, Theta | IndependentYAxis(bool), InnerRadius(double), LabelType(DonutChartLabelType), ShowValueLabels(bool), SortSlices(DonutChartSortSlices) |
| Doughnut Chart | `Chart.Doughnut` | `DoughnutChartBuilder<TRow>` | Size, Color, Column, Row | InnerRadius(double), SortSlices(DoughnutChartSortSlices) |
| ECDF Plot | `Chart.Ecdf` | `EcdfPlotBuilder<TRow>` | X, Color, Detail, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), ShowPoints(bool) |
| Funnel Chart | `Chart.Funnel` | `FunnelChartBuilder<TRow>` | Y, Size | Gap(double), Orient(FunnelChartOrient), Sort(FunnelChartSort) |
| Gantt Chart | `Chart.Gantt` | `GanttChartBuilder<TRow>` | Y, X, X2, Color, Detail, Column, Row | CornerRadius(double), IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), IntervalLabels(bool), LogScaleX(bool), LogScaleY(bool), TaskHeight(double) |
| Gauge Chart | `Chart.Gauge` | `GaugeChartBuilder<TRow>` | Size, Column | Max(double), Min(double), ShowProgress(GaugeChartShowProgress) |
| Grouped Bar Chart | `Chart.GroupedBar` | `GroupedBarChartBuilder<TRow>` | X, Y, Group, Column, Row, Color | Dodge(GroupedBarChartDodge), IndependentYAxis(bool), ShowValueLabels(bool) |
| Heatmap | `Chart.Heatmap` | `HeatmapBuilder<TRow>` | X, Y, Color, Column, Row | IndependentYAxis(bool), ShowValueLabels(bool), XAxisType(HeatmapXAxisType), YAxisType(HeatmapYAxisType) |
| Histogram | `Chart.Histogram` | `HistogramBuilder<TRow>` | X, Color, Column, Row | BinCount(double), IndependentYAxis(bool) |
| KPI Card | `Chart.KpiCard` | `KpiCardBuilder<TRow>` | Metric, Value, Goal | BehindThreshold(double), IncludeZeroX(bool), IncludeZeroY(bool), Layout(KpiCardLayout), LogScaleX(bool), LogScaleY(bool), Style(bool) |
| Line Chart | `Chart.Line` | `LineChartBuilder<TRow>` | X, Y, Color, StrokeDash, Detail, Opacity, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), Interpolate(LineChartInterpolate), LogScaleX(bool), LogScaleY(bool), ShowPoints(bool), XAxisType(LineChartXAxisType), YAxisType(LineChartYAxisType) |
| Lollipop Chart | `Chart.Lollipop` | `LollipopChartBuilder<TRow>` | X, Y, Color, Column, Row | DotSize(double), IndependentYAxis(bool), ShowValueLabels(bool), XAxisType(LollipopChartXAxisType), YAxisType(LollipopChartYAxisType) |
| Map | `Chart.Map` | `MapBuilder<TRow>` | Longitude, Latitude, Color, Size, Opacity | IncludeZeroX(bool), IncludeZeroY(bool), LogScaleX(bool), LogScaleY(bool), Projection(MapProjection), ProjectionCenter(MapProjectionCenter), Region(MapRegion) |
| Network Graph | `Chart.NetworkGraph` | `NetworkGraphBuilder<TRow>` | X, Y, Size | Layout(NetworkGraphLayout) |
| Parallel Coordinates | `Chart.ParallelCoordinates` | `ParallelCoordinatesBuilder<TRow>` | Color, Detail | (none) |
| Pie Chart | `Chart.Pie` | `PieChartBuilder<TRow>` | Size, Color, Column, Row, Theta | CornerRadius(double), IndependentYAxis(bool), InnerRadius(double), LabelType(PieChartLabelType), ShowValueLabels(bool), SortSlices(PieChartSortSlices) |
| Pyramid Chart | `Chart.Pyramid` | `PyramidChartBuilder<TRow>` | X, Y, Color | ShowValueLabels(bool) |
| Radar Chart | `Chart.Radar` | `RadarChartBuilder<TRow>` | X, Y, Color, Column, Row | FillOpacity(double), Filled(JsonNode?), IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), Shape(RadarChartShape), StrokeWidth(double) |
| Range Area Chart | `Chart.RangeArea` | `RangeAreaChartBuilder<TRow>` | X, Y, Y2, Color, Column, Row | IndependentYAxis(bool), Interpolate(RangeAreaChartInterpolate), Opacity(double) |
| Ranged Dot Plot | `Chart.RangedDot` | `RangedDotPlotBuilder<TRow>` | X, Y, Color | IncludeZeroX(bool), IncludeZeroY(bool), LogScaleX(bool), LogScaleY(bool) |
| Regression | `Chart.Regression` | `RegressionBuilder<TRow>` | X, Y, Size, Color, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), Opacity(double), PolyOrder(double), RegressionMethod(RegressionMethod) |
| Rose Chart | `Chart.Rose` | `RoseChartBuilder<TRow>` | X, Y, Color, Column, Row | Alignment(RoseChartAlignment), IndependentYAxis(bool), PadAngle(double), ShowValueLabels(bool), SortSlices(RoseChartSortSlices) |
| Sankey Diagram | `Chart.SankeyDiagram` | `SankeyDiagramBuilder<TRow>` | X, Y, Size | NodeGap(double), NodeWidth(double), Orient(SankeyDiagramOrient) |
| Scatter Plot | `Chart.Scatter` | `ScatterPlotBuilder<TRow>` | X, Y, Color, Size, Shape, Opacity, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), OpacityValue(double) |
| Slope Chart | `Chart.Slope` | `SlopeChartBuilder<TRow>` | X, Y, Color, Detail, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), ShowSeriesInLabel(bool), ShowText(bool) |
| Sparkline | `Chart.Sparkline` | `SparklineBuilder<TRow>` | X, Y, Color, Detail, Row, Column | Baseline(SparklineBaseline), IncludeZeroX(bool), IncludeZeroY(bool), Interpolate(SparklineInterpolate), LogScaleX(bool), LogScaleY(bool), TrendWidth(double) |
| Stacked Bar Chart | `Chart.StackedBar` | `StackedBarChartBuilder<TRow>` | X, Y, Color, Column, Row | IndependentYAxis(bool), ShowValueLabels(bool), StackMode(StackedBarChartStackMode) |
| Streamgraph | `Chart.Streamgraph` | `StreamgraphBuilder<TRow>` | X, Y, Color, Column, Row | IndependentYAxis(bool), Interpolate(StreamgraphInterpolate) |
| Strip Plot | `Chart.Strip` | `StripPlotBuilder<TRow>` | X, Y, Color, Size, Column, Row | IncludeZeroX(bool), IncludeZeroY(bool), IndependentYAxis(bool), LogScaleX(bool), LogScaleY(bool), Opacity(double), PointSize(double), StepWidth(double) |
| Sunburst Chart | `Chart.Sunburst` | `SunburstChartBuilder<TRow>` | Color, Size, Detail, Group | InnerRadius(double), LabelRotate(SunburstChartLabelRotate) |
| Tree | `Chart.Tree` | `TreeBuilder<TRow>` | Color, Detail, Size | Orient(TreeOrient) |
| Treemap | `Chart.Treemap` | `TreemapBuilder<TRow>` | Color, Size, Detail | Breadcrumb(TreemapBreadcrumb) |
| Violin Plot | `Chart.Violin` | `ViolinPlotBuilder<TRow>` | X, Y, Color, Row, Column | Bandwidth(double), IndependentYAxis(bool), MedianWidth(double), ShowBox(bool), ShowContour(bool), ShowMedian(bool), ShowPoints(bool) |
| Waterfall Chart | `Chart.Waterfall` | `WaterfallChartBuilder<TRow>` | X, Y, Color, Column, Row | CornerRadius(double), IndependentYAxis(bool), ShowTextLabels(bool), ShowValueLabels(bool), Totals(WaterfallChartTotals) |

### Enums (0.5.1)

| Enum | Members |
|---|---|
| `AreaChartInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom |
| `AreaChartStackMode` | Default, Normalize, Center, Layered |
| `AreaChartXAxisType` | Default, Temporal, Nominal |
| `AreaChartYAxisType` | Default, Temporal, Nominal |
| `BarChartXAxisType` | Default, Temporal, Nominal |
| `BarChartYAxisType` | Default, Temporal, Nominal |
| `BoxplotDodge` | Default, Auto, Local, Global |
| `BoxplotWhiskerMethod` | Default, Iqr, MinMax (renamed from rule 4's `Minmax`) |
| `BumpChartInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom |
| `ChoroplethRegion` | Default, Auto, Us, World |
| `DonutChartLabelType` | Default, CategoryPercent, Category, Value, Percent, None |
| `DonutChartSortSlices` | Default, None, Descending, Ascending |
| `DoughnutChartSortSlices` | Default, None, Descending, Ascending |
| `FunnelChartOrient` | Default, Vertical, Horizontal |
| `FunnelChartSort` | Default, Descending, Ascending, None |
| `GaugeChartShowProgress` | Default, Show, Hide |
| `GroupedBarChartDodge` | Default, Auto, Local, Global |
| `HeatmapXAxisType` | Default, Temporal, Nominal |
| `HeatmapYAxisType` | Default, Temporal, Nominal |
| `KpiCardLayout` | Default, Horizontal, Vertical, Grid |
| `LineChartInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom |
| `LineChartXAxisType` | Default, Temporal, Nominal |
| `LineChartYAxisType` | Default, Temporal, Nominal |
| `LollipopChartXAxisType` | Default, Temporal, Nominal |
| `LollipopChartYAxisType` | Default, Temporal, Nominal |
| `MapProjection` | Default, DefaultLiteral, Mercator, EqualEarth, Orthographic, Stereographic, ConicEqualArea, ConicEquidistant, AzimuthalEquidistant, Mollweide |
| `MapProjectionCenter` | Default, WorldAtlantic, WorldPacific, China, Usa, Europe, Japan, India, Brazil, Australia, Russia, Africa, MiddleEast, SoutheastAsia, SouthAmerica, NorthAmerica, Uk, Germany, France, Korea |
| `MapRegion` | Default, Auto, Us, World |
| `NetworkGraphLayout` | Default, Circular, Force |
| `PieChartLabelType` | Default, CategoryPercent, Category, Value, Percent, None |
| `PieChartSortSlices` | Default, None, Descending, Ascending |
| `RadarChartShape` | Default, Circle |
| `RangeAreaChartInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis |
| `RegressionMethod` | Default, Linear, Log, Exp, Pow, Quad, Poly |
| `RoseChartAlignment` | Default, Left, Center |
| `RoseChartSortSlices` | Default, None, Descending, Ascending |
| `SankeyDiagramOrient` | Default, Horizontal, Vertical |
| `SparklineBaseline` | Default, Mean, Zero, Median, None |
| `SparklineInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom |
| `StackedBarChartStackMode` | Default, Normalize, Center |
| `StreamgraphInterpolate` | Default, Linear, Monotone, Step, StepBefore, StepAfter, Basis, Cardinal, CatmullRom |
| `SunburstChartLabelRotate` | Default, Radial, Tangential, Horizontal |
| `TreeOrient` | Default, LeftToRight, TopToBottom (renamed from rule 4's `Lr`, `Tb`) |
| `TreemapBreadcrumb` | Default, Show, Hide |
| `WaterfallChartTotals` | Default, Auto, None, First, Last, Both |

## Catalog tables

`Generated/Tables/CatalogTables.g.cs` holds the following, all as `FrozenSet`s and `FrozenDictionary`s:
- the backends;
- `BackendsApplyingTheme`, `HostStateKeys` and `IgnoredOptions`;
- the channel union, the semantic types and the presets;
- for every template on every backend: its channels, its declared properties, and for Excel `supportsFacets` and `hasValidate`;
- for every semantic type, its candidate encodings (`visEncodings`, from `getRegistryEntry`), from which the validator resolves an annotated field's type the way upstream does.

Each property records its kind, its discrete option values as compact JSON (`null` = the omitted default) with their labels (upstream accepts a label in place of a value), and its continuous `min/max/step`. The constants emitter also gives each literal-union struct (`EncodingType`, `Aggregate`, `SortOrder`, `SeriesCountAxis`) an internal `Literals` set, against which the validator reports `invalid_literal`. The row types and the set/map helpers are hand-written in `src/Graspea.Flint/Model/CatalogTables.cs`.

`supportsFacets` is derived from the template's declared `column`/`row` channels. It is false for all 18 Excel templates, because the Excel assembler throws on a bound facet.
