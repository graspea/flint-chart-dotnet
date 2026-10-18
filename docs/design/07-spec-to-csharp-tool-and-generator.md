## Tooling design: spec-to-C# tool and source generator

Status: design, not implemented. Owner decisions of 2026-10-18 (index, decisions 13 and 14) are binding; everything else is proposed, with the high-impact choices collected in §12.12. Section numbers continue the SDK design (§1-§11 in parts 1-3); part 8 is §13.

### 12. Spec-to-C# conversion

#### 12.1 Purpose and scope

A Flint `ChartAssemblyInput` is plain JSON, and JSON is what AI agents and the upstream tooling produce: an agent drafts a chart with `flint-chart-mcp` (`list_chart_types`, the `flint://agent-skill` resource, `validate_chart`, `render_chart` for a preview), and the result is a document such as `Charts/revenue.flint.json`. Two products turn that document into C# that builds the same document with `Graspea.Flint`:

1. **`flint-cs`**, a .NET tool (`Graspea.Flint.Tool`): a one-shot conversion `flint-cs to-csharp revenue.flint.json` that prints readable fluent or model C#. The C# is then the source and the JSON is discarded. Typical use: an agent drafts and validates JSON through MCP, converts it, and commits idiomatic C#.
2. **`Graspea.Flint.Generators`**, a Roslyn incremental source generator: the JSON stays in the repository as the source, `[FlintChart("Charts/revenue.flint.json")]` on a `static partial class` generates a typed builder at compile time, and the compiler reports a spec that does not match the catalog or the bound row type. Typical use: the agent keeps editing the JSON with MCP tools, and the C# build checks every edit.

Both run the same conversion core (§12.3) and so produce the same code for the same input. Neither renders or calls upstream. Decision 13 replaces the closing sentence of part 5 §4, which left "give me the C# for this spec" unscheduled; that sentence now points here.

**Non-goals**: converting backend output (Vega-Lite, ECharts, ...) back to Flint; conversion of C# to JSON (that is `FlintJson`); fetching `data.url`; an MCP server (decision 9 stands; the tool is a CLI that an agent calls like any other).

**Agent flow, documented in the README "For AI agents" section when the tool ships:**

```
1. Draft:     flint-chart-mcp list_chart_types / flint://agent-skill  ->  Charts/revenue.flint.json
2. Check:     flint-chart-mcp validate_chart (vegalite, echarts, chartjs) and render_chart for a preview
3a. Convert:  dnx Graspea.Flint.Tool@<version> to-csharp Charts/revenue.flint.json --data parameter --out RevenueChart.cs
3b. Or bind:  [FlintChart<Sale>("Charts/revenue.flint.json")] public static partial class RevenueChart;  (build reports FLINT1xx)
4. Verify:    builder.Validate(backend) in a test, as for hand-written charts
```

`dotnet dnx` (.NET 10 SDK) runs a tool package without installing it (checked locally: `dotnet dnx --help` on SDK 10.0.401); `dotnet tool install` and a local tool manifest work on SDK 8 and 9.

#### 12.2 Package layout

| Package | Kind | Target | Contents | Depends on |
|---|---|---|---|---|
| `Graspea.Flint` | library (unchanged) | `net8.0` | model, fluent, validator | BCL only |
| `Graspea.Flint.Tool` | .NET tool (`PackAsTool`, `PackageType` `DotnetTool`, `ToolCommandName` `flint-cs`) | `net8.0`, `RollForward=Major` | the CLI, the conversion core compiled in, `Graspea.Flint.dll` of the same version | `Graspea.Flint` only (project reference, packed inside the tool, as every tool carries its dependencies) |
| `Graspea.Flint.Generators` | analyzer package | generator assembly `netstandard2.0` | `analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll` with the conversion core compiled in; `build/Graspea.Flint.Generators.targets` | nuspec dependency `Graspea.Flint` `[X]` (exact, §12.6.1); compiles against `Microsoft.CodeAnalysis.CSharp` 4.14 with `PrivateAssets=all` (nothing packed) |

These ids and the command name are defaults (decision 13); the owner may change them before the first release of the tool or the generator. A tool package cannot be referenced by a project (NU1212), so the tool and the generator cannot share one package. The generator also stays out of the library package, so the library's users do not get a generator loaded into every compilation or a compiler floor (§12.6.1) they did not ask for (§12.12, D7.1).

**Source layout**

```
src/Graspea.Flint.CSharp/              shared source, no project of its own; namespace Graspea.Flint.CSharp; every type internal
  Json/JsonReader.cs                   strict RFC 8259 reader with positions (§12.2.2)
  Spec/SpecReader.cs                   JSON DOM -> SpecDocument (keeps unknown keys, positions)
  Spec/CatalogCheck.cs                 compile-time catalog rules (§12.6.7)
  Rows/RowInference.cs                 typed row inference (§12.4.3, mode A)
  Rows/RowBinding.cs                   field -> member binding and type compatibility (mode B)
  Writing/FluentWriter.cs, ModelWriter.cs, CSharpText.cs (identifiers, literals, layout)
  Generated/NameTables.g.cs, Generated/Catalog.g.cs    written by the csharp-names emitter (§12.7); never edited
  Polyfills/                           IsExternalInit and nullable attributes under #if NETSTANDARD2_0
src/Graspea.Flint.Tool/                net8.0 exe; <Compile Include="../Graspea.Flint.CSharp/**/*.cs" />
src/Graspea.Flint.Generators/          netstandard2.0; the same include; the IIncrementalGenerator and the Roslyn adapters
tests/Graspea.Flint.CSharp.Tests/      net10.0;net8.0; the same include, so the core is tested directly without InternalsVisibleTo
samples/Graspea.Flint.Samples.Generators/   consumes the generator (mode A and mode B), built and run in the samples job
```

##### 12.2.1 Shared source, not a shared assembly

The core is a folder of shared source that is compiled into the tool, the generator and the test project. It is not a `netstandard2.0` assembly packed next to them (D7.2):
- An analyzer's dependencies must sit beside it in `analyzers/dotnet/cs` and are loaded by the compiler host. On the .NET Framework host (Visual Studio's in-process build) they share one load context, so two projects that use two generator versions in one Visual Studio process would see whichever `Graspea.Flint.CSharp.dll` was loaded first. Compiled-in source has no second assembly to conflict.
- Tests rule 4 (§10.1, no `InternalsVisibleTo`) holds: the test project compiles the same sources.
- The cost is that the code must compile for `netstandard2.0` as well as `net8.0`. It uses only `string`, `StringBuilder`, `List<>`, `Dictionary<,>` and LINQ, with no `Span` APIs, no `System.Text.Json`, no `FrozenDictionary`, no `Index`/`Range`, no `HashCode` and no `required` members. Polyfills cover `init` and the nullable attributes. The generator project's build is the check that it compiles for `netstandard2.0`.

**Language versions.** `Directory.Build.props` sets no `LangVersion`, so each project gets its target's default: C# 12 for `net8.0` and C# 7.3 for `netstandard2.0`. Neither is enough, so the projects set it explicitly:
- `src/Graspea.Flint/Graspea.Flint.csproj`: `<LangVersion>13</LangVersion>`. Applying `[OverloadResolutionPriority]` (part 8, §13.5) needs C# 13 on the declaring side; under C# 12 it is error CS9202. The library uses no C# 13 feature that needs runtime support net8 lacks; the attribute is declared internally (§13.5).
- `src/Graspea.Flint.Tool`, `src/Graspea.Flint.Generators`, `tests/Graspea.Flint.CSharp.Tests` and `tests/Graspea.Flint.Tests`: the same `<LangVersion>13</LangVersion>`. Under C# 7.3 the shared core (records, `init`, switch expressions, raw strings, nullable annotations from `Nullable=enable`) does not compile. One value for every project keeps the core's language identical in all three compilations; the compiler that implements it is pinned by `global.json`.
- The samples keep their target's default (C# 12 on `net8.0`), so their build checks the consumer floor of §12.6.2.
- A compile probe (§13.13 item 8) builds the declaring side of the overload pairs at the pinned `LangVersion`, so a lower value fails a test and not only a build.

##### 12.2.2 JSON inside the compiler host

Facts:
- A source generator must target `netstandard2.0` (the compiler loads it on .NET Framework in Visual Studio and on .NET in the SDK). `System.Text.Json` is not part of `netstandard2.0`.
- On the .NET host, `System.Text.Json` is a shared-framework assembly, and the host's version wins. Checked locally: the SDK 10.0.401 compiler (`sdk/10.0.401/Roslyn/bincore/csc.deps.json`) lists no `System.Text.Json`, so the generator would get the runtime's 10.0 copy under SDK 10 and 9.0 under SDK 9.0.312. A generator compiled against a newer package than the host runtime fails at run time with a missing member.
- On the .NET Framework host, `System.Text.Json` is not in the framework. Shipping the NuGet package means packing `System.Text.Json.dll` and its dependencies (`System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `Microsoft.Bcl.AsyncInterfaces`, `System.Text.Encodings.Web`, ...) into `analyzers/`, where they can collide with copies the compiler or another analyzer loaded. Whether the Visual Studio compiler directory carries its own copy, and which version, is **UNVERIFIED** (no Windows host was available) and is not a documented contract either way.

Decision (default, D7.3): **an embedded reader** in the core, about 300 lines, with no dependency. It follows RFC 8259 strictly, as `FlintJson.Deserialize` does: no comments, no trailing commas, maximum depth 64 (the STJ default), invalid escapes rejected, and a lone surrogate escape (`\uD800` not followed by a low surrogate, or a lone low surrogate) rejected in strings and keys. STJ throws `JsonException` for the same input (probe on net10: `JsonSerializer.Deserialize<string>("\"\\uD800\"")`), so the generator (FLINT102) and the tool (exit 1) refuse exactly what the library refuses. It keeps object members in input order, keeps every number's raw text (so `1200.50` and `1e3` are re-emitted exactly), rejects duplicate keys with a positioned error (an agent-written duplicate is a bug; STJ would silently keep the last one), and records the line and column of every node so diagnostics point into the JSON file. The tool additionally runs `FlintJson.Deserialize` on the input (§12.5.4), so it never emits C# for a document the library cannot read.

#### 12.3 The conversion core

```
text --JsonReader--> JSON DOM (ordered, raw numbers, positions)
     --SpecReader--> SpecDocument: the six top-level members, typed where the library is typed, unknown keys kept per level
     --CatalogCheck--> SpecIssue[] (code, severity, JSON pointer, position)          (tool: stderr; generator: FLINT1xx)
     --RowInference (mode A, tool rows) / RowBinding (mode B)--> RowPlan
     --FluentWriter | ModelWriter--> C# text
```

`SpecDocument` mirrors §4.1 without depending on the library: `Data` (values as DOM, url, extensions, absent), `SemanticTypes`, `ChartSpec` (chartType, title, subtitle, encodings with the shorthand, object and array forms, baseSize, canvasSize, chartProperties, extensions), `ThemeSpec` (preset string or object), `Options`, `FieldDisplayNames` and `Extensions`. The writers use the name tables (§12.7) to find the C# spelling of every catalog name. They never guess: a name missing from the tables goes through the raw form (`Encode`, `Property`, `new ChartType("...")`).

**The fidelity rule.** For every input that `FlintJson.Deserialize` accepts, the emitted C# compiles without warnings and builds a document that is `JsonNode.DeepEquals` to the canonicalised input (the canonicaliser of the contract tests, §10.3). The writers choose the most idiomatic call whose output is exactly the input value and fall back otherwise. A typed facade method is used only when the value it writes, compared as JSON, equals the input value. Anything the typed surface cannot hold goes into the owning object's `Extensions` under its wire name, with the typed member left null; the converters write extensions verbatim and after the known members, so the key appears exactly once. The compile round-trip (§12.10) proves the rule over every fixture. `DeepEquals` ignores member order, but the key order of a row is upstream-relevant (`Object.keys(data[0])`, `FirstRowKeys`, static series), so the round-trip also asserts the exact key order of every `data.values` row.

#### 12.4 JSON to C# mapping

##### 12.4.1 Fluent style (default)

Calls follow input order (encodings, then chart properties, then title, subtitle, sizes, annotations not folded into a channel call, display names, theme, options, extensions). The comparison is `DeepEquals`, which is insensitive to member order, so the output is not byte-identical to the input but is semantically identical.

| JSON | Fluent C# | Notes |
|---|---|---|
| `chart_spec.chartType` in the catalog | `Chart.{Entry}(…)`, such as `Chart.Line(rows)` | entry name from the tables (`Line Chart` -> `Line`, `KPI Card` -> `KpiCard`) |
| `chartType` not in the catalog | `Chart.Of(new ChartType("Image Chart"), …)` | FLINT110 (§12.6.7) is reported; the code still compiles |
| `data.values`, typed rows possible (§12.4.3) | `Chart.Line(Rows)` with a nested `Row` record; selectors `r => r.Month` | `--data inline` (default) |
| `data.values`, typed rows not possible | `Chart.Line(ChartData.FromJson(Values))` with `Values` parsed from a raw JSON literal; fields bound by name | rows that are not all objects with one key set in one order, or a row that is not an object |
| `data.values` with `--data external` | `Build(JsonArray values)` -> `Chart.Line(ChartData.FromJson(values))`; the values are written to a sidecar file | §12.5.2 |
| `data.values` with `--data parameter` | `Build(IEnumerable<Row> rows)`; `data.values` only drives the type inference | |
| `data.url` | `Chart.Line(ChartData.FromUrl("…"))` | |
| `data` absent | the deferred entry `Chart.Line<Row>()` of part 8 (`Build()` then has no data), with `Row` from the hints (§12.4.3) | every mode; needs part 8's optional `Data`, which lands before the tool (§12.10, M4 before M3) |
| other `data` keys, or `values` and `url` together | `ChartData.FromJson(…)` with `Extensions = { ["url"] = … }` | fidelity rule |
| `encodings.ch = "f"` or `{"field":"f"}`, channel on the facade | `.X("f")`; with typed rows `.X(r => r.F)` | shorthand is written as `{"field":…}`, as canonical output does |
| encoding object with more members | `.X(new ChartEncoding { Field = "f", Type = EncodingType.Temporal, Aggregate = Aggregate.Sum, SortOrder = SortOrder.Descending, SortBy = "x", Scheme = "blues" })` | constants from the tables; an unknown literal is `new Aggregate("median")` |
| encoding without a field (`{"aggregate":"count"}`) | `.Y(new ChartEncoding { Aggregate = Aggregate.Count })` | |
| unknown keys inside an encoding | `new ChartEncoding { …, Extensions = new() { ["k"] = Json("…") } }` | `Json` is a private helper in the emitted class (§12.5.3); generated code names it `__flint_Json` (§12.6.3) |
| channel not on the facade (undeclared, or `Chart.Of`) | `.Encode(Channel.StrokeDash, "f")`; unknown channel `.Encode(new Channel("text"), "f")` | FLINT111 when undeclared on every backend |
| array of at least two plain, distinct fields on `x` or `y` | `.Series(Channel.Y, r => r.Revenue, r => r.Cost)` or `.Series(Channel.Y, "revenue", "cost")` | `Series` throws on fewer than two, duplicates or other channels, so those go to the next row |
| any other array | `.Encode(Channel.Color, EncodingValue.Series(new ChartEncoding { Field = "a" }, …))` | a one-entry array stays an array (`IsSeries`) |
| `encodings.ch = null` | `.Configure(d => d.ChartSpec.Encodings["x"] = null!)` | FLINT115-class error for the four layout backends is reported as the validator would |
| `chartProperties.k`, binary property, boolean value | `.ShowPoints(true)` | method name from the tables, including the collision suffixes (`OpacityValue`) |
| discrete property, value equal to an enum member's wire value | `.Interpolate(LineChartInterpolate.Monotone)` | reverse table keyed by the canonical JSON of the wire value; `"default"` maps to `DefaultLiteral` |
| discrete property, value is a label, an array (`projectionCenter`) or not in the option set | `.Property("projectionCenter", Json("[10,50]"))` | an enum member writes the label for array options (`docs/generator.md`), so the enum would change the value |
| continuous property, number | `.CornerRadius(4)` | |
| `JsonNode`-mapped property (Radar Chart `filled`) | `.Filled(true)` | `JsonNode` has implicit conversions from scalars |
| any other key (undeclared, host-state, wrong JSON kind) | `.Property("k", "v")`, `.Property("k", true)`, `.Property("k", 12.5)`, `.Property("k", Json("…"))` | |
| property value `null` | `.Configure(d => (d.ChartSpec.ChartProperties ??= new())["k"] = null)` | `Property(k, null)` removes the key, so it cannot write a null |
| `title`, `subtitle` | `.Title("…")`, `.Subtitle("…")` | |
| `baseSize`, `canvasSize` `{width,height}` numbers | `.BaseSize(800, 400)`, `.CanvasSize(…)` | anything else: `Configure` with `ChartSize` and extensions |
| `semantic_types.f = "YearMonth"`, `f` bound by a channel call | folded: `.X(r => r.Month, SemanticType.YearMonth)` | the first channel call that binds `f` takes it |
| other trivial annotations | `.Annotate("f", SemanticType.YearMonth)` (selector with typed rows) | `Name` is `SemanticType.NameType`; unregistered: `new SemanticType("Revenue")` |
| annotation object | `.Annotate("f", new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD", IntrinsicDomain = [0, 100], DivergingMidpoint = 0, SortOrder = ["low", "high"] })` | unknown keys into `Extensions` |
| `field_display_names` | `.DisplayName(r => r.Gdp, "GDP per capita")` or `.DisplayName("f", "…")` | |
| `theme_spec` preset | `.Theme(ThemePreset.Economist)`; unknown `.Theme(new ThemePreset("x"))` | FLINT114 for an unknown preset |
| `theme_spec` object | `.Theme(new Theming.ThemeSpec { Ink = new() { … }, … })` | class and property names from the theming tables; maps as collection initialisers; unknown keys into `Extensions` |
| `options` | `.Options(o => { o.MaxStretch = 1.5; o.ContinuousMarkCrossSection = ContinuousMarkCrossSection.Axes(1, 2, seriesCountAxis: SeriesCountAxis.Auto); })` | `background` and other undeclared keys into `o.Extensions` |
| unknown top-level or `chart_spec` keys (`interaction_spec`) | `.Configure(d => (d.Extensions ??= new())["interaction_spec"] = Json("…"))` | FLINT140 (Info) names each key |

##### 12.4.2 Model style (`--style model`)

One object initialiser of `ChartAssemblyInput`, in the shape of the §4.1 example: `Data = ChartData.FromRows(Rows)` (or `FromJson`, `FromUrl`, the `rows` parameter), `SemanticTypes = new() { … }`, `ChartSpec = new ChartSpec { ChartType = ChartType.LineChart, Encodings = { ["x"] = "month", … }, ChartProperties = new() { ["interpolate"] = "monotone" } }`, `ThemeSpec`, `Options = new AssembleOptions { … }`, `FieldDisplayNames`, `Extensions`. Encodings use the string shorthand when only `field` is set, a `ChartEncoding` initialiser otherwise and `EncodingValue.Series(…)` for arrays; chart properties are written as their JSON values (`"monotone"`, `true`, `12.5`, `Json("…")`). The model style expresses every case directly, so it needs neither `Configure` nor the typed facade methods. It is the better target for documents with many undeclared keys.

##### 12.4.3 Typed row inference (tool rows, generator mode A)

One function in the core, used by the tool's `inline` and `parameter` modes and by generator mode A, so both infer the same `Row`. The inference must be lossless for the data in the spec: `ChartData.FromRows(rows)` over the inferred rows writes the same values.

- **Fields**, depending on who supplies the rows:
  - The rows are replayed from the spec (tool `inline` mode) and `data.values` exists: exactly the keys of the first row, in its order. An encoding field, annotation key or display-name key missing from the data stays a string field name in the chain (`.X("f")`), and the catalog check reports it as the validator does (`unknown_field`, Warning). A union here would add a member that `FromRows` writes as `"f": null` into every row: the document would change (the fidelity rule fails), and upstream would see an all-null nominal column instead of a missing field.
  - The user supplies the rows (tool `parameter` mode, generator mode A) or the spec has no `data.values`: the union, in first-seen order, of the keys of `data.values` (first row first), the encoding fields (series entries included), the `semantic_types` keys and the `field_display_names` keys. The data keys come first, so a row of the inferred type writes them in the input's order.
- **Shape gate**: typed rows are inferred only when every row is an object with the same keys in the same order (that is what `FromRows` writes). Otherwise the tool falls back to raw JSON (§12.4.1) and the generator reports FLINT131 and types every field as `JsonNode?`.
- **Type per field**, first rule that holds over all non-null values:

| Values in `data.values` | Spec hints | .NET type |
|---|---|---|
| all booleans | — | `bool` |
| all numbers, each fits `decimal` | semantic `Amount`, `Price` or `Profit` | `decimal` (raw text kept, `1200.50m`) |
| all numbers, integral, within ±2^53 | otherwise | `long`; integral is lexical: the raw text has no `.` and no exponent (`1.0` and `1e3` are not integral, STJ cannot read them into a `long`) |
| all numbers | otherwise | `double` |
| all strings, each exactly `yyyy-MM-dd` and a valid date | encoding `temporal`, or a temporal semantic type, or no type hint | `DateOnly` (written back as the same string) |
| all strings | — | `string` |
| mixed kinds, nested objects or arrays | — | `JsonNode?` (written verbatim) |
| no values (no `data.values`, `parameter` mode, or every value `null`) | from the hints: quantitative -> `double` (`Count`, `Rank`, `Year`, `Decade` -> `int`; `Amount`, `Price`, `Profit` -> `decimal`); temporal -> `DateOnly` (`DateTime`, `Timestamp` -> `DateTimeOffset`); nominal/ordinal -> `string`; `Boolean` -> `bool` | FLINT130 (Info) and `string` when there is no hint |

A field with at least one `null` gets the nullable form (`long?`, `string?`). Other date and time strings stay `string`, because STJ would write `DateTimeOffset` back in a different form (`+00:00` instead of `Z`), which breaks the fidelity rule. Inference never reads the field name (upstream's `year` token rule, §2, is left to upstream).
- **Member names**: `Pascal(field)` with rule 1 of §7.6 (shared test vectors with `naming.test.ts`): split on non-alphanumeric runs and case changes, acronyms capitalised, a leading digit gets `_`. An empty result becomes `Field{position}`. A name equal to `Row` or to a record member (`EqualityContract`, `Equals`, `GetHashCode`, `ToString`, `Deconstruct`, `PrintMembers`, `GetType`, `Clone`) gets the suffix `Field`. Duplicates get `2`, `3`, ... in first-seen order. Every member carries `[property: JsonPropertyName("<field>")]`, so the wire name never depends on `RowOptions` and field names such as `GDP per capita` (65 fixture fields contain spaces or parentheses) round-trip.

```csharp
public sealed record Row(
    [property: JsonPropertyName("month")] DateOnly Month,
    [property: JsonPropertyName("revenue")] decimal Revenue,
    [property: JsonPropertyName("GDP per capita")] double? GdpPerCapita);
```

#### 12.5 The tool

##### 12.5.1 Synopsis

```
flint-cs to-csharp <file.json | -> [--style fluent|model] [--data inline|external|parameter]
                                   [--namespace <N>] [--class <C>] [--out <file.cs>]
flint-cs --version
flint-cs --help | to-csharp --help
```

| Option | Default | Meaning |
|---|---|---|
| `<file.json>` or `-` | required | input document; `-` reads standard input; UTF-8 with or without BOM; invalid UTF-8 is an input error |
| `--style` | `fluent` | §12.4.1 or §12.4.2 |
| `--data` | `inline` | §12.5.2 |
| `--namespace` | none (global namespace) | a dotted C# identifier; emitted as a file-scoped namespace |
| `--class` | `Pascal(stem)` plus `Chart` unless it already ends in `Chart`, where the stem is the file name without a trailing `.flint.json`, else without `.json` (`revenue.flint.json` -> `RevenueChart`, `sales.json` -> `SalesChart`), with `Flint` before a name the output uses as a type or namespace (`chart.json` -> `FlintChart`); `FlintChart` for standard input | a C# identifier that is neither a member name of the class nor a type or namespace name the output writes |
| `--out` | standard output | the output file, overwritten; required by `--data external` |

These names are the decided defaults (decision 13) and become a public contract with the tool's first release (§12.8).

##### 12.5.2 Data modes

| Mode | Emitted members | Rows | Use |
|---|---|---|---|
| `inline` | `Row` record (or raw JSON), `static readonly Row[] Rows = [ new(…), … ];`, `static ChartAssemblyInput Build()` | embedded | a self-contained replay of the document; the tool prints an Info above 1000 rows suggesting `external` |
| `external` | `static ChartAssemblyInput Build(JsonArray values)`; `data.values` written byte-for-byte to `<out stem>.data.json` | read by the caller (`JsonNode.Parse(File.ReadAllText(…))!.AsArray()`) | large or opaque data; keys verbatim; no inference |
| `parameter` | `Row` record, `static ChartAssemblyInput Build(IEnumerable<Row> rows)`, and with part 8 `Template`, `BuildAsync`, `WriteAsync` (the members of §12.6.3) | supplied later | the production shape: the spec is code and the rows come from a query |

A `data.url` document emits `ChartData.FromUrl` in every mode. A document without `data` emits the deferred entry in every mode (§12.4.1), and `external` then writes no sidecar file and `Build()` takes no parameter. With `--data external` and no `--out`, the tool exits with 2.

##### 12.5.3 Literals, identifiers, determinism

- **Strings**: regular C# literals. `\"`, `\\`, `\0 \a \b \f \n \r \t \v`, the other C0 and C1 controls, every character of the Unicode categories `Cf` (format: bidirectional overrides U+202A-U+202E and isolates U+2066-U+2069, zero-width characters, U+FEFF), `Zl` and `Zp` (U+2028, U+2029), `Co` (private use) and `Cn` (unassigned), and U+FFFD are written as escapes (`\uXXXX`, or `\UXXXXXXXX` outside the BMP); every other character is written as itself (UTF-8 output). Specs are written by agents, and an unescaped bidirectional or invisible character would make the committed C# read differently from what compiles (the "Trojan Source" pattern, CVE-2021-42574). Lone surrogates never reach the writer: the reader rejects them (§12.2.2). The categories come from the Unicode data of the running .NET (`char.GetUnicodeCategory`), so test vectors pin U+202E and U+200B and avoid characters whose category changed between Unicode versions.
- **JSON fragments** (arrays and objects only): single-line raw string literals with one more `"` than the longest quote run in the content (at least three), holding compact JSON with the input's raw number text. They are passed to `JsonNode.Parse` for `JsonNode` targets and to a private helper `static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }` for `Extensions`. The helper is emitted only when used. `JsonElement.Parse` exists only from net10 (checked against the 9.0 and 10.0 reference packs), and the output must compile on net8.
- **Numbers**: the raw input text, which is always a valid literal of the inferred type: `long` is inferred only for lexically integral text (§12.4.3), and every JSON number is a valid C# real literal. `long` gets `L` outside the `int` range, `decimal` gets `m`, and `double` gets `d` when the text has neither `.` nor an exponent. A number that `double` cannot represent (`1e400`) is written as a JSON fragment. No number is reformatted, so the output does not depend on `double.ToString("R")`, which is not shortest-round-trip on the .NET Framework compiler host.
- **Identifiers**: the `Row` member rules of §12.4.3. `--class` and `--namespace` are validated with the C# identifier grammar (Unicode categories as the language specification defines them); an invalid value is a usage error.
- **Determinism**: for the same input bytes, options and tool version the output is byte-identical. It has LF line endings, four-space indentation, a final newline, usings sorted with `System` first, one chained call per line, and no timestamps or absolute paths. The header line is `// Generated by flint-cs <version> from <file name>; requires Graspea.Flint <version>.` (the two versions are equal, §12.8). The tool's output is code the user owns, so it carries no `<auto-generated/>` marker. A test runs the tool twice and compares the bytes.
- **Trimming**: the `Rows`-based members use the reflection row path, so the emitted methods carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` with the library's message (on a property, on its `get` accessor: both attributes target only classes, constructors and methods, so on the property itself they are error CS0592). `external` mode and url data need neither.
- **Language version**: the output uses collection expressions (`[ … ]`), so it needs C# 12, the default of `net8.0`. The README states the floor.

Example (`fluent`, `inline`):

```csharp
// Generated by flint-cs 0.5.1.1 from revenue.flint.json; requires Graspea.Flint 0.5.1.1.
#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Graspea.Flint;

namespace Charts;

public static class RevenueChart
{
    public sealed record Row(
        [property: JsonPropertyName("month")] DateOnly Month,
        [property: JsonPropertyName("revenue")] decimal Revenue,
        [property: JsonPropertyName("region")] string Region);

    public static readonly Row[] Rows =
    [
        new(new DateOnly(2024, 1, 1), 1200.50m, "EU"),
    ];

    [RequiresUnreferencedCode("Row types are serialised by reflection; use the JsonTypeInfo overload for trimmed or AOT hosts.")]
    [RequiresDynamicCode("Row types are serialised by reflection; use the JsonTypeInfo overload for trimmed or AOT hosts.")]
    public static ChartAssemblyInput Build() =>
        Chart.Line(Rows)
            .X(r => r.Month, SemanticType.YearMonth)
            .Y(r => r.Revenue, SemanticType.Amount)
            .Color(r => r.Region)
            .Interpolate(LineChartInterpolate.Monotone)
            .Title("Revenue by region")
            .BaseSize(800, 400)
            .Theme(ThemePreset.Economist)
            .Build();
}
```

##### 12.5.4 Errors, diagnostics, exit codes

The tool reads the input twice: once with the core reader (for positions and raw numbers) and once with `FlintJson.Deserialize`. Both must succeed. A document without `data` passes both once part 8's optional `Data` exists, which is why that lands first (§12.10). The catalog checks of §12.6.7 run as in the generator and are printed to standard error in the MSBuild canonical form, always with the category `warning`, whatever their severity; the code and the validator severity are in the message (`revenue.flint.json(12,18): warning FLINT110: unknown_chart_type (Error): …`). A canonical `error` line would fail an MSBuild `Exec` task (its default `IgnoreStandardErrorWarningFormat=false` turns it into a build error) and most CI annotators, so the same run would pass or fail depending on the caller. Catalog findings never change the exit code, because the C# is still produced and compiles; whether the chart is valid for a backend is decided by `Validate(backend)`, as for any hand-written chart. D7.12 lists the alternative of an exit code 3.

Until the tool's contract is declared stable (§12.8, D7.11), every run prints one notice line to standard error: `flint-cs is in preview; its options and output may change in any release.` It is not in the canonical form, so build tools ignore it.

| Exit code | Meaning |
|---|---|
| 0 | C# written (catalog diagnostics may have been printed) |
| 1 | input error: unreadable file, invalid UTF-8, invalid JSON, duplicate key, or a document `FlintJson.Deserialize` rejects (its `JsonException` message is printed) |
| 2 | usage error: unknown verb or option, invalid `--class`/`--namespace`, `--data external` without `--out` |

Output is written only when the exit code is 0. `--out` is written to a temporary file in the same directory, which then replaces the target, so a failed run never leaves a half-written file.

#### 12.6 The source generator

##### 12.6.1 Package and versions

- `Graspea.Flint.Generators.dll` (`netstandard2.0`, `IsRoslynComponent`, `EnforceExtendedAnalyzerRules`, already on in `Directory.Build.props`) is compiled against `Microsoft.CodeAnalysis.CSharp` **4.14**. That is the compiler of .NET SDK 9.0.300 and Visual Studio 17.14, and the first version with `IncrementalGeneratorPostInitializationContext.AddEmbeddedAttributeDefinition()`. Checked locally: SDK 9.0.312 ships `Microsoft.CodeAnalysis` 4.14.0.0 with that API, and SDK 10.0.401 ships 5.9.0.0. The assembly is packed under `analyzers/dotnet/roslyn4.14/cs/`, the folder the SDK selects by compiler version. `build/Graspea.Flint.Generators.targets` stops an older compiler with error FLINT109 instead of letting it skip the generator with CS8032 and a cascade of missing-member errors. The check reads `$(CompilerApiVersion)`, which the compiler's own targets set (`roslyn4.14` in `sdk/9.0.312/Roslyn/Microsoft.Managed.Core.CurrentVersions.targets`, `roslyn5.9` in SDK 10.0.401, checked locally) and which the SDK also uses to pick the `roslynX.Y` folder: FLINT109 when it is empty or its `X.Y` is lower than 4.14. `$(MSBuildVersion)` is not used, because it only approximates the compiler version (a `Microsoft.Net.Compilers.Toolset` override, or a Visual Studio and SDK mismatch).
- The nuspec depends on `Graspea.Flint` with the exact range `[X]` (decision 14: the same version). The package is not a development dependency. A development dependency would apply `IncludeAssets` without `compile` to its dependency graph and hide `Graspea.Flint` from the consuming project, and the generated code is public API built on `Graspea.Flint`, so the library has to flow (D7.6). A direct reference to another `Graspea.Flint` version wins NuGet's resolution and produces NU1608. The generator then reports FLINT100 (Error), comparing its own informational version with the `AssemblyInformationalVersionAttribute` of the referenced `Graspea.Flint` assembly (build metadata after `+` ignored).
- Packed: `analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll` only (no `Microsoft.CodeAnalysis*`, no `System.Text.Json`), `lib/net8.0/_._` so that NuGet does not report NU5128 for the dependency group, and `build/Graspea.Flint.Generators.targets`. The targets file is in `build/`, not `buildTransitive/`, so FLINT109 and the `**/*.flint.json` glob apply only to projects that reference the generator directly, never to a downstream project (which may build on an older SDK and has no specs). With the default `PrivateAssets` (`contentfiles;analyzers;build`), neither the analyzer nor `build/` flows to a project that references a generator user; `Graspea.Flint` (the `[X]` dependency, compile and runtime assets) does. The packaging test asserts this flow (§12.10).

##### 12.6.2 Attributes

```csharp
namespace Graspea.Flint;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("FLINT_GENERATOR_ATTRIBUTES")]
[Microsoft.CodeAnalysis.Embedded]
internal sealed class FlintChartAttribute : Attribute                 // mode A: the generator infers Row
{
    public FlintChartAttribute(string path) { Path = path; }
    public string Path { get; }                                        // project-relative, '/' or '\'
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("FLINT_GENERATOR_ATTRIBUTES")]
[Microsoft.CodeAnalysis.Embedded]
internal sealed class FlintChartAttribute<TRow> : Attribute           // mode B: binds an existing row type
{
    public FlintChartAttribute(string path) { Path = path; }
    public string Path { get; }
    public FlintRowNaming RowNaming { get; set; }                      // CamelCase (default, = RowOptions.Default) or Verbatim
    public Type? JsonContext { get; set; }                             // a JsonSerializerContext with metadata for TRow: AOT-safe path
}

[Microsoft.CodeAnalysis.Embedded]
internal enum FlintRowNaming { CamelCase = 0, Verbatim = 1 }
```

Usage: `[FlintChart("Charts/revenue.flint.json")] public static partial class RevenueChart;` and `[FlintChart<Sale>("Charts/revenue.flint.json", JsonContext = typeof(SalesJson))] public static partial class RevenueChart;`.

- **Delivery** (D7.5): the generator adds the attributes in `RegisterPostInitializationOutput`, together with `AddEmbeddedAttributeDefinition()`. They are `internal` to each compilation and marked `[Embedded]`, so a project that sees another project's internals (`InternalsVisibleTo`) does not get CS0436. `[Conditional]` keeps them out of the compiled metadata, so nothing reaches run time. Verified in a spike against Roslyn 4.14: the embedded definition is emitted, the generic and the non-generic attribute are told apart by `ForAttributeWithMetadataName("Graspea.Flint.FlintChartAttribute`1")` and `("Graspea.Flint.FlintChartAttribute")`, and the compilation has no diagnostics.
- **Language version**: the floor is C# 12, the default of `net8.0`, which every consumer has unless it lowers `LangVersion`. Generic attributes need C# 11, and the generated code uses collection expressions (C# 12). The spike compiled the generic usage with C# 12 parse options. The generator reads `ParseOptionsProvider`; below C# 12 it reports one FLINT107 (Error, "C# 12 or later is required") per target and emits no members, so the user sees one diagnostic and not a cascade of C# errors in files they cannot edit. A generic attribute below C# 11 also gets CS8936 at the usage site. A `typeof` form (`[FlintChart("…", RowType = typeof(Sale))]`) is the alternative in D7.4.
- **Stability marker** (proposed, D7.11): while the generator's contract is in preview (§12.8), both attribute classes and every generated member carry `[global::System.Diagnostics.CodeAnalysis.Experimental("FLINTEXP001")]` (in the BCL since net8). A user opts in once with `<NoWarn>$(NoWarn);FLINTEXP001</NoWarn>`. The release that declares the contract stable stops emitting the marker and says so in its CHANGELOG entry.
- Exactly one `FlintChart` attribute per class (FLINT105 otherwise). The target must be a non-generic `static partial class`, and every containing type must be `partial` (FLINT104). It may be in any namespace and have any accessibility. The generated part repeats the namespace and every containing type declaration (with their type parameters) and omits the accessibility modifiers, so it takes the user's.

##### 12.6.3 What is generated

One file per target, hint name `<Namespace>.<Containing>.<Class>.flint.g.cs`, built from the metadata names with every character outside `[A-Za-z0-9_.]` replaced by `_` (a generic containing type `Outer<T>` would otherwise put `<`, `>` and `,` into the hint name, which `AddSource` rejects) and a numeric suffix when two targets map to the same name. The file is headed `// <auto-generated/> Graspea.Flint.Generators <version> from <project-relative path>`, with `#nullable enable`. The chain inside is the fluent style of §12.4.1 with `--data parameter`, ending in `ToTemplate()` (part 8, §13.3). The generator therefore ships after part 8's API.

**Name binding in generated code.** The file is emitted into the user's namespace and class, so a simple name could bind to a user type (a `Charts` namespace is a likely home for a type called `Chart`, and `SemanticType` or `Json` are plausible user names) and silently change meaning or fail with C# errors in a file the user cannot edit. So the generated file has no `using` directives, and every type it names is written `global::`-qualified: `global::Graspea.Flint.Chart`, `global::Graspea.Flint.SemanticType`, `global::System.Text.Json.Serialization.JsonPropertyNameAttribute`, `global::System.DateOnly`, `global::System.Threading.LazyInitializer`, the user's `TRow` as `global::Ns.Sale`, and so on. Members the generator adds only for its own use have reserved names: the backing field `__flint_template` and the helper `__flint_Json` (§12.5.3). The nested `Row` record is the one generated type referred to by its simple name, which binds to the nested type before any namespace type. The tool's output is code the user owns and keeps its `using` directives for readability; a clash there is visible and fixed by the user, who can edit the file, and the round-trip pins the list of names that can clash (§12.10).

**Accessibility.** The generated members are emitted with the lesser of the class's and `TRow`'s effective accessibility (for mode A, the class's). A `public` class bound to an `internal` `TRow` therefore gets `internal` members, instead of CS0050/CS0051 in generated code. `JsonContext` appears only inside member bodies, so its accessibility does not matter.

| Member | Mode A | Mode B |
|---|---|---|
| `public sealed record Row(…)` | yes (§12.4.3) | no; `TRow` is the user's type |
| `public static ChartTemplate<TRow> Template { get; }` | built on first use and published once (`LazyInitializer.EnsureInitialized` on `__flint_template`, thread-safe), reflection row path | built once; through `JsonContext` when given (AOT-safe), else `RowOptions` from `RowNaming`. The metadata is `(global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRow>?)SalesJson.Default.GetTypeInfo(typeof(TRow)) ?? throw new global::System.InvalidOperationException(…)` (the message names the context and `TRow`, FLINT106): AOT-safe, and independent of the property name STJ's generator chose (`TypeInfoPropertyName` and STJ's collision rules), which this generator cannot see |
| `public static ChartAssemblyInput Build(IEnumerable<TRow> rows)` | `Template.Bind(rows)` | same |
| `public static ValueTask<ChartAssemblyInput> BuildAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default)` | `Template.BindAsync(rows, cancellationToken)` | same |
| `public static Task WriteAsync(Stream utf8Json, IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default)` | `FlintJson.SerializeAsync(utf8Json, Template.BindStream(rows), cancellationToken)` (streamed, §13.5) | same |
| `public static ValidationResult Validate(FlintBackend? backend = null)` | `Template.Validate(backend)` | same |
| `[RequiresUnreferencedCode]`, `[RequiresDynamicCode]` on the members above | yes (D7.8) | only without `JsonContext` |
| `[Experimental("FLINTEXP001")]` on every member above | while the contract is in preview (D7.11) | same |

The two trimming attributes target only classes, constructors and methods (`AttributeUsage` checked on net10), so on `Template` they sit on the `get` accessor, and the factory lambda is created inside that accessor so that the trim analyzer attributes it to an annotated method (a field initialiser would put it in the static constructor, which cannot carry the attributes):

```csharp
public static global::Graspea.Flint.ChartTemplate<Row> Template
{
    [global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(…)]
    [global::System.Diagnostics.CodeAnalysis.RequiresDynamicCode(…)]
    get => global::System.Threading.LazyInitializer.EnsureInitialized(ref __flint_template, () => …);
}
```

Under a first-use race the factory may run more than once; one result is published and the others are discarded, which is harmless because a template is immutable. An AOT-analyzer build of the generator sample (§12.10) checks the annotations.

The data in the spec is never embedded: `data.values` only drives mode A inference and the mode B checks. `data.url` is ignored with FLINT133 (Info), which points to `Template.Bind(ChartData.FromUrl(…))`.

Mode A selectors are `r => r.Member` over the generated `Row`. Mode B selectors are `r => r.Member` over the matched member of `TRow`. In both modes `semantic_types` and `field_display_names` keys that name a bound field use selectors, so the document stays consistent under any row naming the template is created with.

##### 12.6.4 AdditionalFiles conventions and path resolution

- `build/Graspea.Flint.Generators.targets` adds `<AdditionalFiles Include="**/*.flint.json" Exclude="@(AdditionalFiles);$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)" />` unless `FlintIncludeSpecFiles` is `false`, and adds `<CompilerVisibleProperty Include="ProjectDir" />`. Excluding `@(AdditionalFiles)` keeps a spec that the project already lists from being added twice. A spec with another name is added by hand (`<AdditionalFiles Include="charts/sales.json" />`). MSBuild keeps duplicate items, so the generator does not rely on the glob alone: the matcher deduplicates `AdditionalText`s by normalised path (the first wins), and a driver test passes a duplicated `AdditionalText`.
- The attribute path is relative to the project directory (`build_property.ProjectDir`), may use `/` or `\`, and may leave the project with `..`. Paths are normalised (separators, `.` and `..` segments) and compared ordinally. A miss is FLINT101; when a candidate differs only in case, the message names it.
- The generator never touches the file system (RS1035 is an error under `EnforceExtendedAnalyzerRules`); it reads only `AdditionalText`. A file that is missing from AdditionalFiles is therefore FLINT101 even when it exists on disk.

##### 12.6.5 Mode B: binding and naming

The candidate members of `TRow` are the ones STJ serialises, inherited members included: an instance property with a public getter; any property or field with `[JsonInclude]`, whatever its accessibility; and public instance fields only when the `JsonContext` sets `IncludeFields = true` (never on the reflection path, whose `RowOptions` does not set it). Members with `[JsonIgnore]` that is never written (unconditional, `Always` or .NET 9's `WhenWriting`) are excluded, `Never` and `WhenReading` keep the member; the other conditions are FLINT122 below. A plain public field without `[JsonInclude]` is never a candidate: STJ does not write it, so a selector over it would be rejected at run time ("not written as a key"). Test vectors compare the candidate list, for each test row type, with `JsonTypeInfo.Properties` of the same type at run time, on the reflection path and through a source-generated context. A spec field `f` binds to the one candidate `m` for which:
1. `[JsonPropertyName]` on `m` equals `f`, else
2. `RowNaming == CamelCase` and `JsonNamingPolicy.CamelCase` applied to `m.Name` equals `f` (the core implements STJ's camelCase algorithm; shared test vectors compare it with `JsonNamingPolicy.CamelCase` on net8 and net10), else
3. `RowNaming == Verbatim` and `m.Name == f`.

No member: FLINT120, a Warning, as the validator reports a field missing from the rows (`unknown_field`, Warning, because upstream assembles); it is an Error only when the field is a static-series entry, mirroring `series_field_missing` (D7.7). Two members: FLINT123 (Error). With `JsonContext`, the generator also reads the context's `[JsonSerializable]` attributes; when none names `TRow`, directly, as an array element or as a generic type argument, it reports FLINT106 (Warning): `GetTypeInfo(typeof(TRow))` may return null at run time, and the generated `Template` then throws `InvalidOperationException` naming the context and `TRow`. It is not an Error because STJ also generates metadata for types reachable through the members of a registered type, which the generator does not trace; `.editorconfig` can raise it. With `JsonContext`, the naming policy and the `[JsonSourceGenerationOptions]` arguments are read from the context's attributes in source (`PropertyNamingPolicy`, `DefaultIgnoreCondition`, `UseStringEnumConverter`, `NumberHandling`); `RowNaming` must then be left at its default (FLINT105 otherwise). The row guarantees of §5.3 are checked at compile time where they are decidable: `DefaultIgnoreCondition` other than `Never`, a member with `[JsonIgnore(Condition = WhenWritingNull | WhenWritingDefault)]` or a condition value the generator does not know, or `WriteAsString` number handling is FLINT122 (Error), because `FromRows` throws `ArgumentException` for each of these at run time.

Every bound field is checked against what the spec says about it, in this order: the encoding's explicit `type`; otherwise the registered semantic type's first `visEncodings` entry (`Year`, `Decade`, `YearMonth`, `YearQuarter`, `YearWeek` count as temporal); otherwise, when the spec has `data.values`, the upstream inference rule of §2 applied to those values; otherwise the field is not checked. An `aggregate` of `sum`, `average` or `mean`, and every static-series entry, also require a number.

| The spec says | Accepted | FLINT121 (Warning) | FLINT124 (Info, not checked) |
|---|---|---|---|
| quantitative (also `Latitude`, `Longitude`, every `Measure` semantic type, `Score`, `Duration`) | `byte`…`ulong`, `Int128`, `UInt128`, `Half`, `float`, `double`, `decimal`, and their `Nullable<T>` | `string`, `char`, `bool`, enum, date and time types, `Guid`, `Uri`, collections, nested objects; `TimeSpan` (written as `"1.02:03:04"`, which upstream reads as temporal, §5.3) | `object`, `JsonNode`, `JsonElement`, a member or type with a `[JsonConverter]` |
| temporal (`Date`, `DateTime`, `Time`, `Timestamp`, and the temporal granules above) | `DateOnly`, `DateTime`, `DateTimeOffset`, `string`; integers for `Year` and `Decade` | `TimeOnly` (upstream reads `"10:00:00"` as nominal, probed), `TimeSpan`, `bool`, `Guid`, collections, nested objects | other numbers (epoch values), opaque types |
| ordinal (`Day`, `Hour`, `Month`, `Quarter`, `Week`, `Range`, `Rank`, `Direction`) | `string`, `char`, enum, integers, `float`, `double`, `decimal`, `bool`, `DateOnly` | collections, nested objects | opaque types |
| nominal (the remaining registered types, `Unknown`) | `string`, `char`, enum, `bool`, `Guid`, `Uri`, integers | collections, nested objects (`nested_value`); `float`, `double`, `decimal` (upstream infers a number as quantitative unless the encoding type says otherwise) | opaque types |
| enum member under a `JsonContext` without `UseStringEnumConverter`, bound to a nominal or ordinal field | — | FLINT121: written as a number, which upstream infers as quantitative | — |

##### 12.6.6 Mode A specifics

The `Row` record and the member names are those of §12.4.3. Without `data.values`, types come from the hints. Mode A always uses the reflection row path: a source-generated `JsonSerializerContext` cannot see a type that another generator produced, because generators do not see each other's output. AOT users therefore choose mode B with `JsonContext` (D7.8). FLINT131 (Warning) is reported when the shape gate of §12.4.3 fails.

##### 12.6.7 Diagnostics (`FLINT1xx`)

IDs are never reused. `FLINT0001` and `FLINT0002` stay the `[Obsolete]` ids of §8.4, and `FLINTEXP001` is the experimental marker of D7.11. Severities are defaults that `.editorconfig` can change (`dotnet_diagnostic.FLINT112.severity = error`) for a finding at a C# location, which is reported in that file's syntax tree so that `#pragma warning` applies too; a finding in a JSON spec has an external location, which only a `.globalconfig` or `NoWarn`/`WarningsAsErrors` reach. The catalog checks report the validator code in the message (`… (invalid_option_value)`) and use the validator's severity for `backend == null` (§6) per case: each is created with `Diagnostic.Create(descriptor, location, effectiveSeverity, …)`, so one id can carry different severities (`unknown_theme_preset` is an Error only for a template that Vega-Lite or Plotly has, and an Info otherwise). The table shows the descriptor's default severity only.

The ids are recorded with analyzer release tracking: `AnalyzerReleases.Unshipped.md` and `AnalyzerReleases.Shipped.md` in `src/Graspea.Flint.Generators`, moved Unshipped to Shipped in the release pull request, as with the `PublicAPI` files. That is the contract record for the FLINT ids (§12.8), and it is required anyway: the generator builds with `TreatWarningsAsErrors` and the `Microsoft.CodeAnalysis.Analyzers` rules, and RS2008 fires on every `DiagnosticDescriptor` without it. They are the subset of the validator rules that need no data, so a spec that passes the catalog checks can still fail `Validate` on its data. An Error never stops generation of the members in §12.6.3 (except FLINT107, where the generated code could not compile); an unresolved binding falls back to the field name as a string. The build then fails on the one diagnostic and not on a cascade of CS0117 errors in user code.

| Id | Severity | Condition | Location |
|---|---|---|---|
| FLINT100 | Error | `Graspea.Flint` not referenced, or its version differs from the generator's | the attribute |
| FLINT101 | Error | the attribute path matches no AdditionalFiles item | the attribute argument |
| FLINT102 | Error | the spec is not valid JSON (invalid syntax, invalid escape, depth over 64, duplicate key) | the JSON file, line and column |
| FLINT103 | Error | the JSON is not a `ChartAssemblyInput` (no `chart_spec` or `chartType`, `chartType` empty or not a string, `encodings` not an object, an encoding not a string, object or array, an annotation not a string or object, …): the cases where `FlintJson.Deserialize` throws | the JSON file |
| FLINT104 | Error | the target is not a non-generic `static partial class`, a containing type is not `partial`, or the class already declares a generated member name (`Row`, `Template`, `Build`, `BuildAsync`, `WriteAsync`, `Validate`, `__flint_template`, `__flint_Json`) | the class identifier |
| FLINT105 | Error | invalid attribute use: path not a non-empty constant, more than one `FlintChart` attribute, `RowNaming` together with `JsonContext`, or `JsonContext` that is not a `JsonSerializerContext`; a row type below internal (private, protected, private protected or file-local), for which nothing is generated | the attribute, or the type argument |
| FLINT106 | Warning | mode B: the `JsonContext` has no `[JsonSerializable]` that names `TRow` (§12.6.5) | the attribute's `JsonContext` argument |
| FLINT107 | Error | the language version is below C# 12 (§12.6.2); no members are emitted | the class identifier |
| FLINT109 | Error (MSBuild) | `$(CompilerApiVersion)` empty or below `roslyn4.14` (§12.6.1) | the project |
| FLINT110 | Error | `unknown_chart_type` | the `chartType` value |
| FLINT111 | Warning | `unsupported_channel`: the channel is declared by no backend of the template | the encoding key |
| FLINT112 | Warning | `invalid_option_value`: a discrete value outside the union of the template's options | the property value |
| FLINT113 | Warning | `unknown_semantic_type` | the annotation value |
| FLINT114 | Error | `unknown_theme_preset` (Vega-Lite and Plotly throw; Info for a template neither has) | the `theme_spec` value |
| FLINT115 | Error | a throw-class rule that needs no data: `missing_encodings`, `channel_null`, `reserved_semantic_type` for a bound field, `series_multiple_channels`, `series_channel_not_xy`, `series_too_few`, `series_duplicate_field`, `series_color_bound`, `series_nominal_entry` (explicit type or semantic type) | the offending node |
| FLINT116 | Info | the Info codes that need no data: `unknown_chart_property`, `continuous_out_of_range`, `invalid_literal`, `semantic_type_without_field`, `canvas_too_large`, `required_channel_missing` once the IR carries the heuristic | the node |
| FLINT117 | Warning | `retired_member` | the node |
| FLINT120 | Warning | mode B: no member of `TRow` binds to a spec field (Error when the field is a static-series entry, as `series_field_missing`) | the attribute's type argument; the message names the field and its JSON position |
| FLINT121 | Warning | mode B: the member's type is incompatible with the field (§12.6.5) | the type argument; the message names the member, its type and the reason |
| FLINT122 | Error | mode B: `TRow` metadata may omit a null member or writes numbers as strings (§5.3) | the member, or the context |
| FLINT123 | Error | mode B: two members bind to one field | the type argument |
| FLINT124 | Info | mode B: a member type the generator does not check | the type argument |
| FLINT130 | Info | mode A: a field type could not be inferred and is `string` | the JSON field |
| FLINT131 | Warning | mode A: rows are not uniform objects; every field is `JsonNode?` | the `data.values` node |
| FLINT133 | Info | `data.url` in the spec is not used by the generated members | the `url` value |
| FLINT140 | Info | unknown keys are passed through `Extensions` | each key |

Locations in a JSON file are created with `Location.Create(path, textSpan, lineSpan)`, so the error list and `dotnet build` show `revenue.flint.json(12,18)`. How each IDE presents a location in an additional file (navigation, squiggles in the JSON editor) is **UNVERIFIED** beyond `dotnet build` output and is checked on Visual Studio, Rider and VS Code with C# Dev Kit before the first generator release.

##### 12.6.8 Incremental pipeline and caching

```
post-init        AddEmbeddedAttributeDefinition(); FlintChartAttribute, FlintChartAttribute<TRow>, FlintRowNaming
targets          ForAttributeWithMetadataName("Graspea.Flint.FlintChartAttribute")      -> TargetModel     ("FlintTargets")
targetsB         ForAttributeWithMetadataName("Graspea.Flint.FlintChartAttribute`1")    -> TargetModel     ("FlintGenericTargets")
paths            (targets + targetsB).Select(normalised attribute path).Collect()           -> EquatableArray<string> ("FlintPaths")
specs            AdditionalTextsProvider.Combine(options, paths)
                   .Where(path ends with .flint.json, or is in paths).Select(read + parse) -> SpecModel     ("FlintSpecs")
options          AnalyzerConfigOptionsProvider.Select(ProjectDir)                         -> string
language         ParseOptionsProvider.Select(C# language version >= 12)                   -> bool          ("FlintLanguage")
reference        CompilationProvider.Select(Graspea.Flint informational version)          -> string?       ("FlintReference")
rowShapes        targetsB.Combine(CompilationProvider).Select(extract TRow shape and the JsonContext's attributes) -> RowShapeModel ("FlintRowShapes")
models           (targets + targetsB with rowShapes).Combine(specs.Collect(), options, language, reference).Select(match, check, write) -> EmitModel ("FlintModels")
output           RegisterSourceOutput(models): AddSource + ReportDiagnostic
```

- Every model is a `record` of strings, numbers and `EquatableArray<T>` (a small hand-written wrapper with value equality). No `ISymbol`, `SyntaxNode`, `Compilation` or `Location` is held across steps. Positions are stored as path, `TextSpan` and `LinePositionSpan`, and the `Location` is created in the output step.
- A spec is parsed once per file content: the `specs` step is per file, so editing one JSON file re-parses only that file. Only `*.flint.json` files and paths named by an attribute are parsed; other `.json` AdditionalFiles (settings files, large data files) are skipped by a cheap path check before any read. The `paths` step changes only when an attribute's path argument changes, so it does not re-run the parse on ordinary edits.
- `rowShapes` reads the `JsonContext`'s attributes (`[JsonSourceGenerationOptions]`, the `[JsonSerializable]` list for FLINT106) from the `CompilationProvider`, for the same reason as `TRow`'s shape below.
- `TRow`'s shape is extracted from the `CompilationProvider` and not inside the `ForAttributeWithMetadataName` transform. That transform is not guaranteed to run again when `TRow` changes in another file, so the shape could go stale. The extraction step runs on every compilation change but is cheap (the members of one type), and its value-equal result stops the change from going further when nothing relevant changed.
- `specs.Collect()` changes whenever any JSON file changes. The `models` step then runs again for every target, but it only matches paths and compares inputs; the expensive writing is skipped when `EmitModel` is equal, which the caching test asserts.
- Cancellation: every step checks `CancellationToken`; the reader checks it every 4096 tokens.

##### 12.6.9 IDE behaviour

- Generated files appear under Dependencies > Analyzers > Graspea.Flint.Generators; `EmitCompilerGeneratedFiles=true` writes them to `obj/` for review.
- Editing a `*.flint.json` file updates the generated members and diagnostics while typing. Visual Studio, Rider and VS Code feed open AdditionalFiles text to the generator; this is **UNVERIFIED** per IDE and is checked together with the locations above.
- The generator does no I/O and no network access. The time it adds is measured by the caching test (re-parsing only the changed spec) and by `dotnet build -bl` on the generator sample, with a target of under 50 ms per chart on a cold build of a 20-chart project.

#### 12.7 The csharp-names emitter

A new emitter `generator/src/emit/csharp-names.ts` reads the IR and the shared fluent model (`src/emit/fluent.ts`, the one the builders, enums and entries emitters use), so its names cannot differ from the compiled facades. It writes two files into `src/Graspea.Flint.CSharp/Generated/`, which are covered by the drift check (§7.7) and never edited by hand:

- `NameTables.g.cs`: chart type -> constant, entry, builder; channel -> `Channel` constant; per template: channel -> facade method, property key -> method name, kind (`bool`, `double`, enum, `JsonNode`) and, for an enum, the type name and the map from the canonical JSON of each wire value to its member; semantic type -> constant (`Name` -> `NameType`); preset -> constant; literal structs -> constants; `AssembleOptions` and every `Theming` class: wire name -> C# property, value kind (string, number, bool, array, map, nested class name, `ChartSize`, `ContinuousMarkCrossSection`, `JsonNode`). Renames from `overrides.json` are applied, because the fluent model applies them.
- `Catalog.g.cs`: what the catalog checks need: templates per backend with channels and properties (kind, options, ranges), semantic types with `visEncodings`, presets, `backendsApplyingTheme`, host-state keys, literal sets and `retired.json`.

Both are plain arrays and `switch` expressions that compile on `netstandard2.0`, built into dictionaries on first use. The library gains no public type and no `InternalsVisibleTo`. Tests: `generator/test/emit-csharp-names.test.ts` (a synthetic IR produces the inline expected C#), and a .NET cross-check that loads `Graspea.Flint` by reflection and asserts that every table entry exists with the stated signature (method and parameter type, enum member, constant), so a table that disagrees with the library fails the build of that release.

#### 12.8 Versioning and stability

- **One version for all three packages.** `Graspea.Flint`, `Graspea.Flint.Tool` and `Graspea.Flint.Generators` are packed from the same tag with the same `-p:Version`. Each carries the catalog of that release. The tool runs with the `Graspea.Flint.dll` packed inside it, and its output targets the library version in its header. The README tells users to install the tool and the generator at the version of the library they reference (`dotnet tool install Graspea.Flint.Tool --version <X>`, or `dnx Graspea.Flint.Tool@<X>`).
- **Public contracts** (decisions 13 and 14): the tool's command name, verb, options, data modes, use of standard input and output, and exit codes; the generator's attributes (names, namespace, constructor, properties, `FlintRowNaming` members); the diagnostic ids and their meaning (recorded in `AnalyzerReleases.*.md`, §12.6.7); the MSBuild property `FlintIncludeSpecFiles` and the `*.flint.json` convention.
- **Their stability does not follow the version label** (proposed, D7.11). One version for all three packages means that once the library is Stable, no tag can produce a preview tool or generator; under the plain §8.2 rules their contracts would then freeze in their first release, with no preview period. So these contracts carry their own marker: the generator emits `[Experimental("FLINTEXP001")]` on its attributes and generated members (§12.6.2), and the tool prints a preview notice (§12.5.4). While the marker is present, the contract may change in any release, with a CHANGELOG line per change. A later release declares the tool's or the generator's contract stable in its CHANGELOG entry and drops the marker; from then on the hand-written rules of §8.2 apply to it (additions in any release, removals or renames only in a breaking window). The two contracts are declared stable independently. `docs/versioning.md` and the §8.2 surfaces table gain these rows in the implementation pull request of M3 (the table row is added now).
- **Not contracts**: the text of the tool's output and the body of generated code (call style, helper names, file and hint names, ordering) may change in any release, but every change keeps the fidelity rule and the C# 12 floor. Users who need a fixed text pin the tool version. Proposed refinement (D7.9): the generated member *signatures* that user code calls (`Row` with its member names, `Template`, `Build`, `BuildAsync`, `WriteAsync`, `Validate`) follow the contract rules, because changing them breaks user code exactly like a library change. The owner decides whether this refinement holds.
- **Diagnostics**: a new Warning or Info id may ship in any release. A new Error id, or the promotion of an id to Error, follows the validator's §8.3 rule (only when it mirrors an upstream throw, or in an upstream-version release), because an Error fails builds.
- `PublicAPI.*.txt` and package validation stay on the library. The tool and the generator expose no library API (`EnablePackageValidation=false` for both).

#### 12.9 Release

Changes to `release.yml` and the maintainer guide, made in the implementation pull request:

1. The publish job packs the three projects with the tag's version, the library first. It then runs `scripts/check-nupkg.sh` per package kind:
   - library: unchanged (the library's `LangVersion` 13 changes nothing in the package);
   - tool: the nuspec has `<packageType name="DotnetTool" />`; `tools/net8.0/any/DotnetToolSettings.xml` names the command `flint-cs`; `flint-cs.runtimeconfig.json` has `"rollForward": "Major"`; the packed `Graspea.Flint.dll` has the same informational version as the library package; no other non-BCL assembly; README, icon, license and `THIRD-PARTY-NOTICES.md`;
   - generators: `analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll` and nothing else under `analyzers/`; no `Microsoft.CodeAnalysis*` or `System.Text.Json*` file anywhere; `build/Graspea.Flint.Generators.targets` and no `buildTransitive/` folder; `lib/net8.0/_._`; a dependency `Graspea.Flint` with the version range `[<version>]`; `developmentDependency` absent; README, icon, license and notices.
2. `actions/attest-build-provenance` covers every `.nupkg` and `.snupkg` of the three packages. The GitHub release attaches all of them, and `SECURITY.md` "Verifying a release" lists the three ids.
3. Push order: library, tool, generators, each with `--skip-duplicate`, so a rerun after a partial failure completes the set.
4. Trusted Publishing: the nuget.org policy (owner account, `graspea/flint-chart-dotnet`, `release.yml`, environment `nuget`) is scoped to the owner account and should cover every package that account owns. Whether the first push of a *new* package id is accepted through a Trusted Publishing token is **UNVERIFIED**. The maintainer checks it on nuget.org before the first release that includes the new ids, and `docs/maintainers.md` records the result. The `Graspea.` prefix reservation covers the new ids once it is granted.
5. One CHANGELOG entry per release covers all packages; the GitHub release title stays `Flint.NET <version> (flint-chart X.Y.Z)`.
6. CI: the `dotnet` job builds the three projects with `-warnaserror`. The `samples` job also builds `samples/Graspea.Flint.Samples.Generators` against the packed generator from a local folder feed (so the packaging is tested, not only the project reference), with `IsAotCompatible=true` and warnings as errors, so the trimming annotations of §12.6.3 are checked by the analyzers (the sample's mode A calls sit in methods that carry the same annotations, and its mode B calls in methods that carry none); and it runs the packed tool on the README spec.

#### 12.10 Tests

All tests follow §10.1: red first, a `// source:` comment per expected value, NUnit constraints, and no `InternalsVisibleTo` (the core sources are compiled into `tests/Graspea.Flint.CSharp.Tests`). `Microsoft.CodeAnalysis.CSharp` 4.14 is a test-only package version in `Directory.Packages.props`. The tests pin the compiler floor, so the generator cannot use an API above 4.14 unnoticed.

| Layer | Asserts | Expected values from |
|---|---|---|
| Reader | RFC 8259 conformance vectors (valid and invalid), positions, raw numbers, duplicate keys, depth 64, lone surrogate escapes in strings and keys rejected | RFC 8259; `FlintJson.Deserialize` behaviour for the same inputs (`// source: FlintJson.Deserialize behaviour`) |
| Mapping (unit) | one inline JSON -> inline expected C# per row of §12.4.1, §12.4.2 and §12.4.3, per escaping rule of §12.5.3 (including U+202E and U+200B), the `--class` stem rule (`revenue.flint.json`, `sales.json`, `-`), and the lexical integral rule (`1`, `1.0`, `1e3`) | this design's tables |
| Name tables cross-check | every table entry exists in `Graspea.Flint` with the stated signature | the compiled library |
| Catalog checks | per FLINT11x code a minimal spec yields exactly that diagnostic; over the 934 fixtures and 37 probe cases, the core's codes equal `ChartValidator.Validate(input)`'s data-independent codes with the same severity | §6, the validator |
| Compile round-trip (contract, `Category=Contract`) | for every fixture, probe case, sample output (`samples/out/*.json`) and the README quick-start JSON, for each style (fluent, model) and data mode (inline, external, parameter): the core writes C#; all classes of one style and mode compile in one `CSharpCompilation` with `LanguageVersion.CSharp12` against the `net8.0` reference pack (`Microsoft.NETCore.App.Ref` 8 from the SDK's `packs` folder) and `Graspea.Flint`, on both test legs, with no warnings; the assembly loads in a collectible `AssemblyLoadContext`; `Build` is invoked (`external`: with the values; `parameter`: with rows deserialised from `data.values` into the generated `Row`); `FlintJson.SerializeToNode(result)` is `DeepEquals` to the canonicalised input, and the key order of every `data.values` row equals the input's. In `parameter` mode the expected document is the canonicalised input with each union-only field (§12.4.3) added as `null` at the end of every row, which is what rows of the inferred type write. Once more, the inline fluent classes are compiled inside a namespace that declares `Chart`, `Row`, `SemanticType` and `Json` types: a namespace member wins over a `using` import, so the tool's output cannot be immune, and the test asserts that every error is at one of the simple names the README lists under "Name clashes" with the fix (another `--namespace`, or qualifying the name by hand), so a new clash cannot appear unnoticed. Six compilations of about a thousand classes each | the fixtures (§7.9) and the canonicaliser |
| Tool CLI | process-level: every exit code; `-` for standard input; `--out` atomic replace; external sidecar; two runs byte-identical; `--help`/`--version` | §12.5 |
| Generator driver | `CSharpGeneratorDriver` with in-memory AdditionalTexts, C# 12 parse options and the `net8.0` reference pack: mode A and mode B output compiles, and invoking `Build` on rows from `data.values` is `DeepEquals` to the canonicalised spec (with union-only fields as `null`, as above) for every fixture (mode A); output compiles in a user namespace that declares `Chart`, `Row`, `SemanticType` and `Json` types, and inside a generic containing type; mode B binding equals `JsonTypeInfo.Properties` for the test row types (§12.6.5); a public class over an internal `TRow` gets internal members; a duplicated `AdditionalText` yields one spec; every FLINT1xx id with its severity and location (line and column in the JSON), including per-case severities of one id; `trackIncrementalGeneratorSteps: true` with an unrelated edit leaves every named step `Cached` or `Unchanged`, editing one spec marks only its `FlintSpecs` and `FlintModels` entries `Modified`, and editing `TRow` marks only its `FlintRowShapes` entry | §12.6 |
| Packaging | §12.9 step 1 checks; the generator sample restored from the packed generator; a second project that references the sample project gets `Graspea.Flint` but neither the analyzer nor the `build/` targets (verifies the `PrivateAssets` flow of §12.6.1) | §12.9 |

First failing tests, in order: `naming` vectors for `Pascal(field)` shared with the TS tests; `JsonReaderTests` (positions, raw numbers, duplicates); `FluentWriterTests.MinimalBarChart` (Semantic Context #0 -> the expected chain); `generator/test/emit-csharp-names.test.ts`; `NameTablesCrossCheckTests`; the compile round-trip over the fixtures with `known-failures` support as in §10.3; the tool's exit-code tests; then the generator tests once part 8's template API exists.

**Milestones**, in this order: M4 is part 8's data API, including the optional `Data` (today `ChartAssemblyInput.Data` is `required`, so `FlintJson.Deserialize` throws on a spec without `data`, and the tool's double read would exit 1). It must land before the stable tag because it changes a hand-written signature (§13.12). M3 is the conversion core, the csharp-names emitter and the tool; it builds on M4 (the deferred entry for a spec without `data`, and `Template`, `BuildAsync` and `WriteAsync` in `parameter` mode). M5 is the generator. M3 and M5 do not gate the Stable exit of §8.1. The tool and the generator ship with a library release, at the library's version and label; their contracts carry their own preview marker until declared stable (D7.11), so a stable library release can carry a preview tool or generator.

#### 12.11 Risks

| Risk | Mitigation |
|---|---|
| A second reader and a second catalog copy could drift from the library | the fixture-wide comparison of catalog codes with `ChartValidator`; the name tables come from the same fluent model; the round-trip compiles against the real library |
| The compiler floor 4.14 excludes SDK 8 users of the generator | FLINT109 says so in plain words; the tool has no floor beyond .NET 8; .NET 8 support ends in November 2026 |
| The experimental marker (D7.11) adds a `NoWarn` step for every generator user | one line, documented in the README; it ends with the stability declaration |
| Generated code for 100 000-row inline data compiles slowly | the Info at 1000 rows; `external` and `parameter` modes |
| IDE presentation of diagnostics in JSON files | `dotnet build` output is the contract; the IDE checks in §12.6.7 |

#### 12.12 High-impact decision points for the owner

| # | Decision | Options | Recommendation | Trade-offs |
|---|---|---|---|---|
| D7.1 | Package split | (a) three packages: library, tool, generators; (b) generator inside the `Graspea.Flint` package (`analyzers/`); (c) (a) plus a public `Graspea.Flint.CSharp` conversion library | **(a)** | (b) gives one install, but every library user loads the generator and inherits the 4.14 compiler floor; (c) adds a public API that must freeze for a use case nobody asked for (YAGNI); (a) costs two more package ids, but they share the version and the pipeline |
| D7.2 | Shared core form | (a) shared source compiled into tool, generator and tests; (b) an internal `netstandard2.0` assembly packed beside the tool and the analyzer; (c) a public package | **(a)** | (b) builds the core once and keeps it as one unit, but brings analyzer dependency loading (version conflicts on the .NET Framework host) and needs `InternalsVisibleTo` or public types for tests; (a) compiles the core three times (seconds) and limits it to APIs common to `netstandard2.0` and `net8.0` |
| D7.3 | JSON parsing in the generator | (a) embedded reader; (b) `System.Text.Json` package packed into `analyzers/`; (c) rely on the host's `System.Text.Json` | **(a)** | (b) and (c) depend on what the host has loaded (verified: none in the SDK compiler, the runtime's copy on .NET; unknown on .NET Framework) and fail at run time when versions differ; (a) costs about 300 lines plus conformance tests and gives exact positions and raw numbers, which (b) would need extra work for |
| D7.4 | Mode B attribute shape | (a) generic `FlintChart<TRow>` plus non-generic `FlintChart`; (b) one attribute with `RowType = typeof(Sale)`; (c) both | **(a)** | (a) needs C# 11, below the C# 12 floor the generated code needs anyway (§12.6.2), which every net8+ consumer has by default (spike: works with C# 12 and Roslyn 4.14), and reads best; (b) avoids the generic attribute, which gains nothing under the C# 12 floor, and `typeof` can name an open or unrelated type, which needs extra diagnostics; (c) doubles the contract |
| D7.5 | Attribute delivery | (a) generated per compilation, `internal`, `[Embedded]`, `[Conditional]` (compiler floor 4.14); (b) public types in `Graspea.Flint`; (c) a separate attributes assembly in the generators package's `lib/` | **(a)** | (b) freezes generator API into the library at Stable and couples the two release cadences in the other direction; (c) puts a run-time reference into every consumer; (a) has no run-time footprint and no CS0436, at the cost of the 4.14 floor (SDK 9.0.300, Visual Studio 17.14) |
| D7.6 | How the generator requires the same library version | (a) nuspec dependency `[X]`, not a development dependency, plus FLINT100; (b) no dependency, FLINT100 only; (c) generator inside the library package (= D7.1 b) | **(a)** | (a) installs everything with one reference and flows `Graspea.Flint` to dependent projects (the generated API needs it); the analyzer and `build/` do not flow, because `PrivateAssets` defaults to `contentfiles;analyzers;build` (asserted by the packaging test). (b) needs two references and relies on users to keep them equal |
| D7.7 | Mode B severities | (a) missing member Warning (Error for a static-series entry), ambiguous member Error, incompatible type Warning, unchecked Info; (b) missing member always Error; (c) incompatible type Error too | **(a)** | (a) follows the §6 rule: upstream assembles with a field missing from the rows (`unknown_field`, Warning) and throws only for a missing static-series field (`series_field_missing`); it does not throw on a type mismatch either, it silently charts nonsense. The generated code still compiles, falling back to the string field name. (b) and (c) catch mistakes earlier but depart from the validator's severity rule for compile-time binding; `.editorconfig` gives the same result per project (`dotnet_diagnostic.FLINT120.severity = error`). An ambiguous member is an Error because the generator cannot pick a selector |
| D7.8 | Mode A and trimming/AOT | (a) reflection row path with `RequiresUnreferencedCode`; AOT users choose mode B with `JsonContext`; (b) generate a `JsonTypeInfo<Row>` with `JsonMetadataServices` | **(a)** for the first release, (b) additive later | (b) gives AOT-safe mode A but re-implements part of STJ's source generator and has to match `RowOptions` behaviour (enum names, non-finite doubles become `null`), a large surface to test; mode B already has an AOT path |
| D7.9 | Contract for generated members | (a) attributes and diagnostics are contracts, generated code is not (decisions 13 and 14 as written); (b) additionally the generated member signatures (`Row` members, `Template`, `Build`, `BuildAsync`, `WriteAsync`, `Validate`) follow the §8.2 rules, while bodies stay free | **(b)** | under (a), renaming `BuildAsync` or a `Row` member in a minor release would break user code with no warning period; (b) keeps the bodies free and costs a reflection snapshot test over the generator sample |
| D7.10 | Tool defaults | (a) `--style fluent`, `--data inline`; (b) `--data parameter` by default | **(a)** as decided, revisit after the first preview | `inline` replays the agent's document exactly, which is what a first conversion is checked against; `parameter` is the production shape and is one flag away |
| D7.11 | Stability of the tool and generator contracts under one version | (a) own marker: `[Experimental("FLINTEXP001")]` on the generator's attributes and generated members, a preview notice from the tool, the freeze declared in a later CHANGELOG entry; (b) M3 and M5 must ship while the library line is still in preview, and freeze with it at Stable; (c) separate version lines for the tool and the generator | **(a)** | (a) keeps one version and one pipeline and gives each new contract a real preview period whenever it ships, at the cost of a `NoWarn` line for generator users until the freeze; (b) needs no marker but makes the tool and the generator gate (or race) the library's Stable exit, which §12.10 says they must not; (c) breaks decision 14's "same version" in spirit, needs a version map between the packages and a second tag scheme |
| D7.12 | Tool exit code when the catalog check finds an Error | (a) exit 0, every finding printed with the canonical `warning` category and the validator severity in the text; (b) exit 3, "C# written, catalog Errors reported", with `error` lines; (c) exit 0 with `error` lines (as first drafted) | **(a)** | (a) behaves the same under every caller and matches "the C# is still produced and compiles"; an agent reads the severity from the text. (b) lets scripts stop on a broken spec without parsing text, at the cost of a fourth exit code to freeze and a non-zero exit with output written; (c) fails an MSBuild `Exec` task or a CI annotator but passes a plain shell, so the result depends on the caller |

#### 12.13 Defaults applied (say if you object)

- Package ids `Graspea.Flint.Tool` and `Graspea.Flint.Generators`; command `flint-cs`; verb `to-csharp`; tool on `net8.0` with `RollForward=Major` (decision 13, changeable before the first release).
- Spec files are `*.flint.json`, included automatically; paths are relative to the project directory.
- The generated `Row` type is a nested `sealed record` named `Row`.
- Catalog diagnostics in the tool never change the exit code and are printed with the `warning` category (D7.12).
- Exit codes 0, 1 and 2 only.
- `LangVersion` 13 in the library, the tool, the generator and the test projects; C# 12 as the consumer floor (FLINT107).
- The FLINT ids are tracked in `AnalyzerReleases.Shipped.md`/`Unshipped.md`.

UNVERIFIED items to settle before the first tool or generator release: `System.Text.Json` on the .NET Framework compiler host (it only supports D7.3 further, it does not change it); IDE presentation of JSON-file locations and live AdditionalFiles edits; the first push of a new package id through Trusted Publishing.
