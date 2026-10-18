# Graspea.Flint.Generators

A C# source generator for [Graspea.Flint](https://www.nuget.org/packages/Graspea.Flint), the unofficial .NET SDK for
Flint (`flint-chart`). The Flint `ChartAssemblyInput` JSON stays in the project as the source, typically a file an AI
agent keeps editing with `flint-chart-mcp`; `[FlintChart("Charts/revenue.flint.json")]` on a `static partial class`
turns it into a typed chart builder at compile time, and the build reports a spec that does not match the catalog or
the bound row type. It does not render charts and is not affiliated with or endorsed by Microsoft.

The generator is in preview: its attributes and every generated member carry
`[Experimental("FLINTEXP001")]`, and its contract may change in any release until a release declares it stable in the
changelog. Opt in once per project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);FLINTEXP001</NoWarn>
</PropertyGroup>
```

## Install

Reference the generator at the version of `Graspea.Flint` it requires; the package depends on exactly that version,
so one reference brings both.

```
dotnet add package Graspea.Flint.Generators --version <version>
```

It needs the C# compiler of .NET SDK 9.0.300 or Visual Studio 17.14 or later (Roslyn 4.14); an older compiler stops the
build with FLINT109. The project needs C# 12 or later, the default of `net8.0` (FLINT107 otherwise).

A project that references a project using the generator gets `Graspea.Flint`, and the SDK passes it the analyzer too,
but not the package's `build/` targets: it has no spec files, so the generator only adds its internal attributes there.

## Usage

Every `*.flint.json` file in the project is passed to the generator; set `<FlintIncludeSpecFiles>false</FlintIncludeSpecFiles>`
to list them yourself, and add a spec with another name with `<AdditionalFiles Include="charts/sales.json" />`. The
attribute path is relative to the project directory.

**Mode A**, a row type inferred from the spec:

```csharp
[FlintChart("Charts/revenue.flint.json")]
public static partial class RevenueChart;

ChartAssemblyInput input = RevenueChart.Build(rows);          // IEnumerable<RevenueChart.Row>
await RevenueChart.WriteAsync(response.Body, query.AsAsyncEnumerable(), ct);
```

The nested `Row` record has one member per field of the spec (`data.values` keys first, then the encoding fields and the
annotated fields), each with `[JsonPropertyName]`, typed from the values and the spec's hints. Mode A writes rows by
reflection, so its members carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`.

**Mode B**, an existing row type:

```csharp
[FlintChart<Sale>("Charts/revenue.flint.json", JsonContext = typeof(SalesJson))]
public static partial class RevenueChart;
```

Every field of the spec binds to the member of `Sale` that System.Text.Json writes under that name: its
`[JsonPropertyName]`, else its name under the naming policy (camelCase by default, `RowNaming = FlintRowNaming.Verbatim`
for the names as written, or the policy of the `JsonContext`). With `JsonContext`, rows are written through the context's
metadata, which is trimming- and AOT-safe, and the members carry no trimming attributes.

Both modes generate:

| Member | |
|---|---|
| `Template` | the chart without rows, built once on first use (`ChartTemplate<TRow>`) |
| `Build(IEnumerable<TRow> rows)` | a document with the rows written now |
| `BuildAsync(IAsyncEnumerable<TRow> rows, CancellationToken)` | the same from an asynchronous source |
| `WriteAsync(Stream utf8Json, IAsyncEnumerable<TRow> rows, CancellationToken)` | the document written with the rows streamed from their source |
| `Validate(FlintBackend? backend = null)` | the spec's issues that need no rows |

The data in the spec is never embedded: `data.values` only shapes the row type and the checks.

## Diagnostics

| Id | Severity | Condition |
|---|---|---|
| FLINT100 | Error | `Graspea.Flint` is not referenced, or its version differs from the generator's |
| FLINT101 | Error | the attribute path matches no AdditionalFiles item |
| FLINT102 | Error | the spec is not valid JSON (syntax, escape, depth over 64, duplicate key) |
| FLINT103 | Error | the JSON is not a `ChartAssemblyInput` (where `FlintJson.Deserialize` throws) |
| FLINT104 | Error | the target is not a non-generic `static partial class` in partial containing types, or declares a generated member name |
| FLINT105 | Error | invalid attribute use: an empty path, more than one `FlintChart` attribute, `RowNaming` with `JsonContext`, a `JsonContext` that is not a `JsonSerializerContext`, a row type that is private, protected or file-local (nothing is generated) |
| FLINT106 | Warning | mode B: the `JsonContext` has no `[JsonSerializable]` that names the row type |
| FLINT107 | Error | the language version is below C# 12 |
| FLINT109 | Error (MSBuild) | the compiler is older than Roslyn 4.14 |
| FLINT110 | Error | `unknown_chart_type` |
| FLINT111 | Warning | `unsupported_channel` |
| FLINT112 | Warning | `invalid_option_value` |
| FLINT113 | Warning | `unknown_semantic_type` |
| FLINT114 | Error | `unknown_theme_preset` (an Info for a template on no backend that applies themes) |
| FLINT115 | Error | a rule that makes the assemblers throw and needs no data: `missing_encodings`, `channel_null`, `reserved_semantic_type`, the `series_*` rules |
| FLINT116 | Info | `unknown_chart_property`, `continuous_out_of_range`, `invalid_literal`, `semantic_type_without_field`, `canvas_too_large`, `required_channel_missing` |
| FLINT120 | Warning | mode B: no member binds a spec field (an Error for a static-series entry) |
| FLINT121 | Warning | mode B: the member's type does not fit what the spec says about the field |
| FLINT122 | Error | mode B: the row metadata may omit a null member or writes numbers as strings |
| FLINT123 | Error | mode B: two members bind one field |
| FLINT124 | Info | mode B: a member type that is not checked (`object`, `JsonNode`, a converter) |
| FLINT130 | Info | mode A: a field without values or type hints is a `string` |
| FLINT131 | Warning | mode A: the rows are not uniform objects; every member is `JsonNode?` |
| FLINT133 | Info | `data.url` is not used by the generated members |
| FLINT140 | Info | an unknown key is passed through `Extensions` |

The catalog checks (FLINT110 to FLINT116) are the Graspea.Flint validator's rules that need no data, at the validator's
severity for every backend that has the template, with the validator's code in the message; a spec that passes them can
still fail `Validate` on its data. A finding in a spec is reported at its line and column in the JSON file.

A finding at a C# location (the attribute, its arguments, the class or a row type member, such as FLINT120 and FLINT121
at the type argument) is an ordinary compiler diagnostic of that file: `#pragma warning disable FLINT120` around the
attribute suppresses it, and `.editorconfig` changes its severity (`[*.cs]` with
`dotnet_diagnostic.FLINT120.severity = error`). A finding in a JSON spec is outside every C# file, so only project-wide
settings reach it: a `.globalconfig` (`is_global = true`, then `dotnet_diagnostic.FLINT112.severity = none`), or
`NoWarn` and `WarningsAsErrors` in the project file.

## Verifying the package

Every release of the generator is built by the repository's release workflow from a maintainer-signed tag, in the same run
and at the same version as `Graspea.Flint`, and carries Sigstore build provenance. Verify the `.nupkg` attached to the
[GitHub release](https://github.com/graspea/flint-chart-dotnet/releases):

```
gh attestation verify Graspea.Flint.Generators.<version>.nupkg --repo graspea/flint-chart-dotnet \
  --signer-workflow graspea/flint-chart-dotnet/.github/workflows/release.yml \
  --source-ref refs/tags/v<version> --deny-self-hosted-runners
```

nuget.org adds its repository signature on upload, so a copy restored from nuget.org is checked with `dotnet nuget verify`
instead ([SECURITY.md](https://github.com/graspea/flint-chart-dotnet/blob/main/SECURITY.md#verifying-a-release)).

## License

MIT. Copyright (c) 2026 GRASPEA s.r.o. The catalog the generator uses derives from `flint-chart` (MIT); see
`THIRD-PARTY-NOTICES.md` in the package.
