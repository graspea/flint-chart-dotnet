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
