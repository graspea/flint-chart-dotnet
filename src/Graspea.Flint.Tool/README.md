# Graspea.Flint.Tool (flint-cs)

`flint-cs` converts a Flint `ChartAssemblyInput` JSON document into readable C# that builds the same document with
[Graspea.Flint](https://www.nuget.org/packages/Graspea.Flint), the unofficial .NET SDK for Flint (`flint-chart`). The
JSON is typically what an AI agent drafted and checked with `flint-chart-mcp`; after the conversion the C# is the
source. The tool does not render charts and is not affiliated with or endorsed by Microsoft.

The tool is in preview: every run prints `flint-cs is in preview; its options and output may change in any release.`
to standard error, until a release declares its contract stable in the changelog.

## Install

Install the tool at the version of the `Graspea.Flint` package your project references; the output names that version
in its first line.

```
dotnet tool install --global Graspea.Flint.Tool --version <version>
dnx Graspea.Flint.Tool@<version> to-csharp revenue.flint.json      # .NET 10 SDK: run without installing
```

The tool runs on .NET 8 or later. The C# it writes needs C# 12, the default of `net8.0`.

## Usage

```
flint-cs to-csharp <file.json | -> [--style fluent|model] [--data inline|external|parameter]
                                   [--namespace <N>] [--class <C>] [--out <file.cs>]
flint-cs --version
flint-cs --help
```

| Option | Default | Meaning |
|---|---|---|
| `<file.json>` or `-` | required | The input document; `-` reads standard input. UTF-8, with or without a BOM. |
| `--style` | `fluent` | `fluent`: a chain over the generated builders (`Chart.Line(Rows).X(r => r.Month)…`); `model`: one `ChartAssemblyInput` initialiser. |
| `--data` | `inline` | `inline`: the rows are embedded as a `Row` record array; `external`: `Build(JsonArray values)`, with `data.values` written byte for byte to `<out stem>.data.json` (needs `--out`); `parameter`: a `Row` record, a reusable `Template`, `Build(IEnumerable<Row>)`, `BuildAsync`, `WriteAsync` (streamed) and `Validate`. |
| `--namespace` | none | A file-scoped namespace for the class. |
| `--class` | from the file name | `revenue.flint.json` gives `RevenueChart`, `sales.json` gives `SalesChart`, standard input gives `FlintChart`; `Flint` goes before a reserved name, so `chart.json` gives `FlintChart`. |
| `--out` | standard output | The output file, replaced atomically. |

```
flint-cs to-csharp Charts/revenue.flint.json --data parameter --namespace Charts --out RevenueChart.cs
```

A document without `data` uses the deferred entry (`Chart.Line<Row>()`), with `Row` typed from the encodings and the
semantic types; a `data.url` document uses `ChartData.FromUrl`.

## What the output guarantees

For every document that `FlintJson.Deserialize` accepts, the C# compiles without warnings and builds a document equal
to the input as JSON, with the key order of every row kept. A value that no typed call writes exactly goes into the
owning object's `Extensions` under its wire name. The same input, options and tool version always give the same bytes.
Characters that could make the source read differently from what compiles (bidirectional controls, zero-width and
other invisible characters) are written as escapes.

The `Row` members use `[JsonPropertyName]`, so field names such as `GDP per capita` keep their spelling. The members
that use reflection to write rows carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`; for trimmed or AOT
hosts, use `--data external` or bind the rows through a `JsonTypeInfo<TRow>` yourself.

The output uses `using` directives for readability. If the namespace of the class declares a type named `Chart`,
`ChartData` or `SemanticType`, that type wins over the import and the output does not compile; choose another
`--namespace` or qualify the name by hand (`Graspea.Flint.Chart`). The class's own `Row` and `Json` members win over
types of those names.

The class itself must not have the name of a type or namespace that the output writes by its simple name, because
inside the class its own name wins (a class `Chart` would turn `Chart.Bar(Rows)` into a call on itself). `--class`
refuses these names with exit code 2, and the default name avoids them: `Graspea`, `System`, the Graspea.Flint types
(`Chart`, `ChartAssemblyInput`, `ChartData`, `ChartEncoding`, `ChartSpec`, `ChartTemplate`, `ChartType`, `Channel`,
`SemanticType`, `SemanticAnnotation`, `EncodingType`, `EncodingValue`, `Aggregate`, `SortOrder`, `SeriesCountAxis`,
`ThemePreset`, `FlintBackend`, `FlintJson`, `RowOptions`, `ValidationResult`), the per-template enums (such as
`LineChartInterpolate`) and the option and theme classes with their member types (such as `AssembleOptions`,
`ChartSize`, `ThemeInk`), and the .NET types the output names (`JsonArray`, `JsonDocument`, `JsonElement`, `JsonNode`,
`JsonPropertyName`, `JsonTypeInfo`, `RequiresDynamicCode`, `RequiresUnreferencedCode`, `LazyInitializer`,
`CancellationToken`, `DateOnly`, `DateTime`, `DateTimeOffset`, `IAsyncEnumerable`, `IEnumerable`, `Stream`, `Task`,
`ValueTask`). The member names `Row`, `Rows`, `Values`, `Template`, `Build`, `BuildAsync`, `WriteAsync`, `Validate`,
`Json` and `WithData` are refused as well.

## Diagnostics and exit codes

The tool runs the catalog checks that the source generator (Graspea.Flint.Generators) runs: the rules of the
Graspea.Flint validator that need no data (unknown chart type, channel or preset, an option value outside the
template's options, and so on) and the unknown keys passed through `Extensions`. It prints them to standard error in
the MSBuild canonical form, always with the category `warning`; the validator's code and severity are in the message:

```
revenue.flint.json(5,18): warning FLINT110: unknown_chart_type (Error): …
```

They never change the exit code; whether the chart is valid for a backend is decided by `Validate(backend)` in your
code, as for any hand-written chart.

| Exit code | Meaning |
|---|---|
| 0 | C# written (findings may have been printed) |
| 1 | Input error: unreadable file, invalid UTF-8, invalid JSON (including a duplicate key), or a document `FlintJson.Deserialize` rejects |
| 2 | Usage error: unknown verb or option, an invalid or reserved `--class`, an invalid `--namespace`, `--data external` without `--out` |

Output is written only when the exit code is 0.

## Verifying the package

Every release of the tool is built by the repository's release workflow from a maintainer-signed tag, in the same run
and at the same version as `Graspea.Flint`, and carries Sigstore build provenance. Verify the `.nupkg` attached to the
[GitHub release](https://github.com/graspea/flint-chart-dotnet/releases):

```
gh attestation verify Graspea.Flint.Tool.<version>.nupkg --repo graspea/flint-chart-dotnet \
  --signer-workflow graspea/flint-chart-dotnet/.github/workflows/release.yml \
  --source-ref refs/tags/v<version> --deny-self-hosted-runners
```

nuget.org adds its repository signature on upload, so a copy restored from nuget.org is checked with `dotnet nuget verify`
instead ([SECURITY.md](https://github.com/graspea/flint-chart-dotnet/blob/main/SECURITY.md#verifying-a-release)).

## License

MIT. Copyright (c) 2026 GRASPEA s.r.o. The catalog the tool uses derives from `flint-chart` (MIT); see
`THIRD-PARTY-NOTICES.md` in the package.
