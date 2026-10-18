# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow the four-part scheme of [docs/versioning.md](docs/versioning.md): the first three parts are the `flint-chart` version the release tracks.

## [Unreleased]

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
