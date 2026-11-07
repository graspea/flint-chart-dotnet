# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow the four-part scheme of [docs/versioning.md](docs/versioning.md): the first three parts are the `flint-chart` version the release tracks.

## [Unreleased]

Tracks `flint-chart` 1.0.0 (npm), an upstream major and so a breaking window ([docs/versioning.md](docs/versioning.md)). The release pull request sets the version heading and adds the IR diff table of the sync pull request.

### Added

- The `flint-chart` 1.0.0 catalog: the chart types Marginal Plot, Ridgeline Plot and Table (`Chart.Marginal`, `Chart.Ridgeline`, `Chart.Table`), and the channels, chart properties, options and theme members the sync pull request lists.
- `ChartAssemblyInput.InteractionSpec` (`interaction_spec`), read by the Vega-Lite interactive surface: the generated `InteractionSpec` and `InteractionEntry`, the hand-written `AssistedTargeting` and `TargetDetails` (each `true`/`false` or an object), and `ChartBuilder.Interaction(...)`.
- `ChartSpec.Label`, `ChartSpec.Source` (`ChartSource`: a string, or text with a link) and `ChartSpec.Chrome` (`ChartChrome`: `ChartChrome.None` or the parts to turn off), with `Label`, `Source` and `Chrome` on the builders.
- `ChartEncoding.Title` (`ChannelTitle`: a name, or `ChannelTitle.None`, written as `null`, for no title), `Reverse`, `Mid`, `Domain`, `Bins` and `Colors`.
- `SemanticAnnotation.Colors` and `SemanticAnnotation.Polarity` (`Polarity.HigherIsBetter`, `Polarity.LowerIsBetter`).
- The validator code `retired_member` (Warning; FLINT117 in the catalog checks of `flint-cs` and `Graspea.Flint.Generators`) for a channel, chart property, option value or semantic type that `flint-chart` removed, naming the version.
- From the next upstream retirement on, a member that `flint-chart` removes is kept under `[Obsolete]` with the diagnostic id FLINT0001 until the next breaking window ([docs/deprecations.md](docs/deprecations.md)).
- `flint-cs to-csharp` and `Graspea.Flint.Generators` write these members typed; `interaction_spec` and `chart_spec.label`, `source` and `chrome` are no longer reported as unknown keys (FLINT140).

### Changed

- Theme members that `flint-chart` 1.0.0 widens from a string to a union now have a typed union: `ThemeStructure.Frame`, `ThemeStructure.Baseline`, `ThemeStructureGrid.Category`, `Measure` and `Zero` are `RuleSpec`, and `TypeRole.Weight` is `TypeWeight`. Both convert implicitly from a string, so `Frame = "quiet"` and `Weight = "bold"` still compile; code that reads the member as a `string` uses `RuleSpec.Presence` or `TypeWeight.Keyword` instead.
- `ThemeSpec.Interaction` and `ThemeVariantThen.Interaction` are `ThemeInteraction` (`TooltipFormat`, and the new `SelectionBoundary`); it replaces `ThemeSpecInteraction` and `ThemeVariantThenInteraction`.
- The validator follows `flint-chart` 1.0.0 ([docs/validation-codes.md](docs/validation-codes.md)): an array value equal entry by entry to an array option is no longer `invalid_option_value`; on Excel, a temporal `group` with at most 12 distinct values is ordinal, so a Grouped Bar Chart grouped by a few dates is no longer `excel_unsupported`; a temporal `x` or `y` on a banded chart type that reads as a few snapshots is ordinal, so Excel's Boxplot rule no longer reports such a date `x`; `timespan_as_string` now says that upstream reads a duration string as a nominal category.
- The validator ports more of `flint-chart` 1.0.0 ([docs/validation-codes.md](docs/validation-codes.md)): `sort_by_invalid` follows the sort repair of the layout backends (`invalid_sort`): an unbound `x`, `y` or `color`, a data field outside Vega-Lite, an invalid JSON array entry and an invalid `sortOrder` are reported, `size` and `group` are Warnings on ECharts too, and Excel is never reported; `YearMonth`, `Date` and `DateTime` values are read in the spellings upstream rewrites (`202501`, `Jan 2025`, `2025/01/15`); dates on a banded `x` or `y` are decided as snapshots or a time scale where their times are known; and a date string is inferred as upstream 1.0.0 infers it (`Jan 2025` and `Stage 1` are nominal).
- `FlintUpstream.Version` is `1.0.0`.

### Removed

- `KpiCardBuilder<TRow>` and `MapBuilder<TRow>`: `IncludeZeroX`, `IncludeZeroY`, `LogScaleX` and `LogScaleY`. `flint-chart` 1.0.0 dropped `includeZero_x/y` and `logScale_x/y` from both templates and ignores the keys; the methods are deleted in this breaking window rather than kept as FLINT0001 ([docs/deprecations.md](docs/deprecations.md)). `Property("includeZero_x", true)` still writes the key, which the validator reports as `retired_member`.
- `ThemeLabels.Angle`, which `flint-chart` 1.0.0 removed.
- `ThemeSpecInteraction` and `ThemeVariantThenInteraction`; see `ThemeInteraction` above.

## [0.5.1.1-preview.1] - 2026-10-18

The first preview, `0.5.1.1-preview.1`, tracking `flint-chart` 0.5.1 (npm).

### Added

- Typed model of the Flint `ChartAssemblyInput` document: `ChartAssemblyInput`, `ChartData`, `ChartSpec`, `ChartEncoding`, `EncodingValue`, `SemanticAnnotation`, `ChartTheme`, `ContinuousMarkCrossSection`, `ChartSize`, and the string-backed constants `ChartType`, `Channel`, `SemanticType`, `ThemePreset`, `EncodingType`, `Aggregate`, `SortOrder` and `SeriesCountAxis`.
- `FlintJson` to write and read documents with System.Text.Json; unknown keys round-trip through `Extensions`, and the output does not depend on the caller's serializer options.
- `ChartData.FromRows` with camelCase keys by default, `RowOptions` and source-generated `JsonTypeInfo` overloads for trimming and Native AOT.
- Generated `AssembleOptions`, the theme classes under `Graspea.Flint.Theming`, and `FlintUpstream.Version`.
- Fluent builders for the 48 chart types of `flint-chart` 0.5.1 (`Chart.Line(rows).X(...).Y(...)`), with per-template enums for discrete chart properties, and `Chart.Of` for any template.
- `ChartValidator` and `Validate(backend)`: Errors where the pinned assemblers throw, Warnings where they silently drop input, Infos otherwise ([docs/validation-codes.md](docs/validation-codes.md)).
- `AGENTS.md` and `catalog/catalog.json` in the package, for AI coding agents.
- Late-bound rows: `ChartAssemblyInput.Data` is optional, so a spec without rows can be built, written and read (`FlintJson.Deserialize` accepts a document without `data`); deferred entries without rows (`Chart.Line<Sale>()`, `Chart.Line(typeInfo)`, `Chart.Of<TRow>(chartType)`); and `ChartTemplate<TRow>` from `ToTemplate()` or `ChartTemplate.Create`, an immutable, thread-safe spec that binds rows per call with `Bind`, `BindAsync` and `BindStream`, and validates without rows.
- Asynchronous and batched rows: `ChartData.FromRowsAsync` and `RowSource.FromBatches`, with cancellation.
- Streamed rows: `ChartData.Stream` and `FlintJson.SerializeAsync`/`FlintJson.Serialize(Stream, …)` write rows from their source to the output through a pooled buffer, with memory that does not grow with the row count and the bytes of the materialised document; `SerializeAsync` never writes synchronously, and it reads a source passed as `IEnumerable<T>` asynchronously when the object also implements `IAsyncEnumerable<T>`, as an Entity Framework Core query does.
- `rows_not_inspected` (Info) on streamed data and templates, where `series_field_missing` is a Warning ([docs/validation-codes.md](docs/validation-codes.md)).
- `Graspea.Flint.Tool`, a .NET tool (`flint-cs to-csharp <file.json | ->`) that converts a `ChartAssemblyInput` JSON document into C# building the same document: the fluent or the model style, with the rows inline, in a sidecar file read by the caller, or supplied later through a template with `Build`, `BuildAsync`, `WriteAsync` and `Validate`. It ships at the library's version and is in preview: its options and output may change in any release until a release declares its contract stable here ([src/Graspea.Flint.Tool/README.md](src/Graspea.Flint.Tool/README.md)).
- `Graspea.Flint.Generators`, a C# source generator: `[FlintChart("spec.flint.json")]` on a `static partial class` generates a `Row` record inferred from a `ChartAssemblyInput` JSON file of the project, `Template`, `Build`, `BuildAsync`, `WriteAsync` and `Validate`; `[FlintChart<TRow>(…)]` binds the spec's fields to an existing row type, by reflection or through a `JsonSerializerContext`. The build reports a spec that does not match the catalog or the row type as FLINT1xx diagnostics at their position in the JSON file. It needs Roslyn 4.14 (.NET SDK 9.0.300, Visual Studio 17.14) and C# 12, ships at the library's version and depends on exactly that `Graspea.Flint`, and is in preview: its attributes and generated members carry `[Experimental("FLINTEXP001")]` until a release declares its contract stable here ([src/Graspea.Flint.Generators/README.md](src/Graspea.Flint.Generators/README.md)).
- Each release publishes `Graspea.Flint`, `Graspea.Flint.Tool` and `Graspea.Flint.Generators` at one version from the same approved release job, with Sigstore build provenance for every package file attached to the GitHub release ([SECURITY.md](SECURITY.md#verifying-a-release)).
