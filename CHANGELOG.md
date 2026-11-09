# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow the four-part scheme of [docs/versioning.md](docs/versioning.md): the first three parts are the `flint-chart` version the release tracks.

## [Unreleased]

### Added

- `ChartTemplate<TRow>.ToJson()`, the template's stored document (no bound rows) as compact JSON text.
- The `Graspea.Flint.Delivery` namespace for sending fixed charts to a browser: `ChartHandle`, `ChartPart` (materialised and streamed parts), `ChartGroup`, `ChartManifest`, `DataPage`, `DeliveryError`, `ChartRowLimitException` and `DeliveryContract`.
- Five `fixed_*` Info codes that flag what a fixed definition leaves to the rows present, reported by `ChartGroup.Validate` only.
- The README section "Charts in the browser" and [docs/delivery.md](docs/delivery.md); the SDK ships no npm package.
- Delivery contract v1 ([docs/versioning.md](docs/versioning.md#the-delivery-contract)) is stable from this release: additive changes only.
- `ChartEncoding.ColorScheme`, a `ColorScheme`: a scheme name or a list of colour stops, low end first (`ColorScheme = "viridis"` or `ColorScheme = new[] { "#c0392b", "#f2f2f2", "#2e75b6" }`). `FlintJson` now reads an array `scheme`, which threw `JsonException`; `flint-cs to-csharp` and `Graspea.Flint.Generators` write an array scheme typed, which was reported as `FLINT103`.
- `FlintBackend.ImageCharts` (`image-charts`), the Image-Charts backend of `flint-chart` (`assembleImageCharts`), which returns an `https://image-charts.com` image URL instead of a spec. It has ten chart types: Bar, Grouped Bar, Stacked Bar, Line, Area, Pie, Donut and Radar Chart, Sparkline and Scatter Plot. Each declares the channels the assembler reads that the other backends of the template declare, so no builder gains a member. Validation does not report the channels the assembler reads that its template leaves out (`group`, and `x` and `y` on Pie and Donut Chart) as `unsupported_channel`. Loading the image sends the chart's values to that service; the browser delivery flow does not use it ([docs/delivery.md](docs/delivery.md#limits-and-exposure)).
- The Error code `image_charts_unsupported` for what makes the Image-Charts assembler of `flint-chart` 1.0.1 throw: a bound `column` or `row`; no field on `x` or `y` of a Bar, Grouped Bar, Stacked Bar, Line, Area or Radar Chart or a Sparkline; a Pie or Donut Chart without a slice field (`color`, else `x`) or a value field (`size`, else `theta`, else `y`); and a Scatter Plot whose `x` or `y` is missing or not quantitative ([docs/validation-codes.md](docs/validation-codes.md)).

### Changed

- Validation without a backend (`ChartValidator.Validate(input)`, `Validate()` on a builder or a template, and `ChartGroup.Validate()`) now covers Image-Charts for its ten chart types, as it covers Excel: messages name `image-charts` among the backends, Infos such as `theme_ignored_by_backend`, `option_overridden_by_backend` and `data_empty` also report it, and an Image-Charts throw is an Error. A Sparkline, which has no Excel template, is now invalid without a backend when it binds `column` or `row`, or has no field on `x` or `y`: Image-Charts throws there, while Vega-Lite and Plotly assemble it. No other document of the `flint-chart` 1.0.1 fixtures changes its `IsValid`. Pass the backend you render with to check only that one.
- The catalog lists `image-charts`: `catalog/catalog.json` in the package, the README chart catalog and the packed `AGENTS.md`; the generated builder docs say where a channel or property is not declared for it, and name `excel_unsupported` or `image_charts_unsupported` for a `column` or `row` that those backends throw on. The `flint-chart@1.0.1` fixtures record the Image-Charts outcomes and 26 new crafted cases.
- Excel validation now reports every Excel throw that the spec decides on its own also on a template (`ChartTemplate.Validate`, `ChartGroup.Validate`), on streamed rows and without rows. Released versions skipped a rule that reads a field's type until rows were present, and with an empty `data.values` reported it as a `data_empty` Error that blamed the missing rows. The spec decides a channel's type when the channel has an explicit `type` other than `quantitative`, an `aggregate` (quantitative) or no field; for a rule that rejects a continuous `color`, `group` or radar `x`, an explicit `quantitative` `type` decides too; and a Waterfall Chart's final total is decided by an explicit `chartProperties.totals`. So a Line Chart template with a `nominal` `y` or a `temporal` `color`, or a Funnel Chart template without `size`, is now invalid for Excel, as it is once rows are bound. This changes the validation outcome of released versions: a template or a streamed document that passed for Excel, or for every backend, can now fail. Rules that read row values are still skipped without rows. With rows, a Scatter Plot or Connected Scatter Plot now reports one `excel_unsupported` per axis that is not quantitative, on `chart_spec.encodings.x` or `chart_spec.encodings.y`, instead of one on `chart_spec`. Without rows, the `data_empty` Info of Excel and Image-Charts now says that the backend throws whatever the rows when a spec rule fails, where it said that the chart assembles empty.

### Deprecated

- `ChartEncoding.Scheme` (`FLINT0002`, [docs/deprecations.md](docs/deprecations.md#FLINT0002)): use `ChartEncoding.ColorScheme`. It reads the scheme name and returns null when the scheme is a list of colour stops.

### Fixed

- `reserved_semantic_type` is no longer an Error on an Excel target for a field that only a static-series entry binds and that is not a key of the first row: Excel reads an array encoding as an unbound channel and never looks the annotation up. Image-Charts is validated the same way.
- Excel validation of a Candlestick Chart that binds a field to `color`, `group` or `detail` now reports one `excel_unsupported` Error per bound channel, on `chart_spec.encodings.<channel>`, instead of one on `chart_spec`, and on an Excel target no longer also an `unsupported_channel` Warning that said Excel ignores the channel: `flint-chart` throws on any of them.
- Excel validation of a Grouped Bar Chart now reads `color` as the group when no `group` is bound, as `flint-chart` does: a continuous `color` is an `excel_unsupported` Error (reported on `chart_spec.encodings.color`), and the `color` is no longer reported as `unsupported_channel` on an Excel target. With a `group` bound, Excel still ignores `color` and it is still reported.
- Excel validation of a Histogram whose `x` has an empty field (`""`) beside a `y` field now reports the throw of `flint-chart` ("Excel histogram requires finite numeric values.") as an `excel_unsupported` Error on `chart_spec.encodings.x`. `flint-chart` skips an empty `x` field when it picks the channel to check, and checks `y`, but it then bins the empty `x` field, which reads the rows' `""` key, so it finds no finite value. On a template or streamed rows the rule reads row values and is skipped, as before.
- `group` is no longer reported as `unsupported_channel` on an Excel target on an Area, Bar, Stacked Bar or Line Chart or a Scatter or Connected Scatter Plot when it has a field, or when a `color` with a field that is not quantitative is bound too: Excel reads it as the series, ahead of `color`, as on a Grouped Bar Chart (a quantitative or field-less `color` is no series candidate, so the `group` is still reported). Without a backend, and as `FLINT111` of `flint-cs to-csharp` and `Graspea.Flint.Generators`, a `group` on a Connected Scatter Plot, and a `group` without a field beside a `color` with a field, are no longer reported either. A Pie or Donut Chart still ignores `group`.
- `strokeDash` with a field is no longer reported as `unsupported_channel` on an Excel target, nor without a backend or as `FLINT111`, on an Area, Bar, Grouped Bar, Stacked Bar, Pie or Donut Chart, where Excel splits the series by it, and on a Scatter or Connected Scatter Plot without a series field (`group`, else `color`), where it shows the legend.
- `size` with a field on a Connected Scatter Plot and `order` with a field on a Scatter Plot are no longer reported as `unsupported_channel` on an Excel target, nor without a backend or as `FLINT111`: Excel reads `size` as the bubble size and sorts each series by `order`. On a Line Chart it reads both when `x` and `y` are quantitative, so they are reported only when the spec or the rows make `x` or `y` another type; on a template or streamed rows an `x` or `y` whose type the rows decide counts as quantitative.
- On an Excel target, `x` and `y` of a Pie or Donut Chart are no longer reported as `unsupported_channel` while Excel reads them as the slice (`x`, with no `color` bound) and the value (`y`, with neither `size` nor `theta` bound). `y` of a Histogram without a field on `x` is no longer reported on an Excel target, without a backend or as `FLINT111`: Excel reads it as the value.

## [1.0.1.1-preview.1] - 2026-11-08

Tracks `flint-chart` 1.0.1 (npm). `flint-chart` 1.0.0 is an upstream major and so a breaking window ([docs/versioning.md](docs/versioning.md)); this preview covers 1.0.0 and 1.0.1, and no release tracks 1.0.0 alone. 1.0.1 changes no catalog, option or theme member (no IR changes besides the version); its fixes (the Plotly line curve, the MCP server) are in the upstream renderers, not in the document the SDK writes.

<details>
<summary>IR diff, flint-chart 0.5.1 → 1.0.0 (from the sync pull request; BREAKING 28 · behavioural 2 · additive 155 · cosmetic 31)</summary>

**Chart types**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| added | `catalog.chartTypes[Marginal Plot]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Ridgeline Plot]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Table]` |  | `{…}` | additive |

**Channels**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| added | `catalog.chartTypes[Grouped Bar Chart].backends.vegalite.channels[color]` |  | `"color"` | additive |
| added | `catalog.chartTypes[Line Chart].backends.echarts.channels[strokeDash]` |  | `"strokeDash"` | additive |
| added | `catalog.chartTypes[Scatter Plot].backends.vegalite.channels[detail]` |  | `"detail"` | additive |

**Properties**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| added | `catalog.chartTypes[Choropleth].backends.vegalite.properties[level]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Choropleth].backends.vegalite.properties[projectionCenter]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Choropleth].backends.vegalite.properties[projection]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Density Plot].backends.vegalite.properties[opacity]` |  | `{…}` | additive |
| added | `catalog.chartTypes[Density Plot].backends.vegalite.properties[stackMode]` |  | `{…}` | additive |
| removed | `catalog.chartTypes[KPI Card].backends.vegalite.properties[includeZero_x]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[KPI Card].backends.vegalite.properties[includeZero_y]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[KPI Card].backends.vegalite.properties[logScale_x]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[KPI Card].backends.vegalite.properties[logScale_y]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[Map].backends.vegalite.properties[includeZero_x]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[Map].backends.vegalite.properties[includeZero_y]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[Map].backends.vegalite.properties[logScale_x]` | `{…}` |  | BREAKING |
| removed | `catalog.chartTypes[Map].backends.vegalite.properties[logScale_y]` | `{…}` |  | BREAKING |
| added | `catalog.chartTypes[Violin Plot].backends.vegalite.properties[widths]` |  | `{…}` | additive |

**Theme presets**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| added | `catalog.themePresets[apple]` |  | `{…}` | additive |
| added | `catalog.themePresets[bbc]` |  | `{…}` | additive |
| changed | `catalog.themePresets[datawrapper].description` | `"Embedded web chart: narrow column, plain headline and deck, a rule under the footer."` | `"Embedded web chart: Roboto, a plain headline and description, values on bars and no value axis."` | cosmetic |
| added | `catalog.themePresets[de-stijl]` |  | `{…}` | additive |
| added | `catalog.themePresets[material]` |  | `{…}` | additive |

**Input types**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| added | `types.ArcGeometry.props[hole]` |  | `{…}` | additive |
| added | `types.ArcGeometry.props[order]` |  | `{…}` | additive |
| added | `types.ArcGeometry.props[startAngle]` |  | `{…}` | additive |
| added | `types.AreaGeometry.props[fill]` |  | `{…}` | additive |
| removed | `types.AssembleOptions.doc` | `"Options for the chart assembly function.\nIncludes layout tuning parameters — all have sensible def…` |  | cosmetic |
| added | `types.AssembleOptions.props[aspectTolerance]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[bandBalance]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[canvasFit]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[canvasRefit]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[canvasTreatment]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[cellGrowth]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[facetGapColumn]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[facetGapRow]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[labelStep]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[liveLayoutSignal]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[minLabelFontSize]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[rowNamesAbove]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[targetAspect]` |  | `{…}` | additive |
| added | `types.AssembleOptions.props[themeDecisions]` |  | `{…}` | additive |
| added | `types.AssistedTargetingOptions` |  | `{…}` | additive |
| added | `types.AxisRole.props[color]` |  | `{…}` | additive |
| changed | `types.AxisRole.props[placement].doc` | `` "`opposite` = the far side of the plot (top for x, right for y)." `` | `` "Which side of the plot the axis sits on (AX1):\n- `opposite` — the far side, whichever way the axis… `` | cosmetic |
| added | `types.AxisRole.props[placement].type.values[bottom]` |  | `"bottom"` | additive |
| added | `types.AxisRole.props[placement].type.values[leading]` |  | `"leading"` | additive |
| added | `types.AxisRole.props[placement].type.values[left]` |  | `"left"` | additive |
| added | `types.AxisRole.props[placement].type.values[right]` |  | `"right"` | additive |
| added | `types.AxisRole.props[placement].type.values[top]` |  | `"top"` | additive |
| added | `types.AxisRole.props[placement].type.values[trailing]` |  | `"trailing"` | additive |
| added | `types.BandGeometry.props[group]` |  | `{…}` | additive |
| added | `types.BandGeometry.props[minLength]` |  | `{…}` | additive |
| added | `types.BandGeometry.props[outlineInk]` |  | `{…}` | additive |
| added | `types.CanvasTreatment` |  | `{…}` | additive |
| added | `types.ChartAssemblyInput.props[chart_spec].type.props[chrome]` |  | `{…}` | additive |
| added | `types.ChartAssemblyInput.props[chart_spec].type.props[label]` |  | `{…}` | additive |
| added | `types.ChartAssemblyInput.props[chart_spec].type.props[source]` |  | `{…}` | additive |
| changed | `types.ChartAssemblyInput.props[chart_spec].type.props[title].doc` | `"The headline — what this chart says, in words.\n\nNot decoration. A chart of bare numbers names not…` | `"The headline — what this chart says, in words.\n\nThemes that omit required axis titles append thei…` | cosmetic |
| added | `types.ChartAssemblyInput.props[interaction_spec]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[bins]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[colors]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[domain]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[mid]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[reverse]` |  | `{…}` | additive |
| added | `types.ChartEncoding.props[scheme].doc` |  | `` "Colour channel: a Vega scheme name (`viridis`, `redblue`), or the stops a ramp or palette runs\nthr… `` | cosmetic |
| changed | `types.ChartEncoding.props[scheme].type.kind` | `"string"` | `"union"` | BREAKING |
| added | `types.ChartEncoding.props[scheme].type.members` |  | `[…]` | additive |
| added | `types.ChartEncoding.props[title]` |  | `{…}` | additive |
| added | `types.ChartSpec.props[chrome]` |  | `{…}` | additive |
| added | `types.ChartSpec.props[label]` |  | `{…}` | additive |
| added | `types.ChartSpec.props[source]` |  | `{…}` | additive |
| changed | `types.ChartSpec.props[title].doc` | `"The headline — what this chart says, in words.\n\nNot decoration. A chart of bare numbers names not…` | `"The headline — what this chart says, in words.\n\nThemes that omit required axis titles append thei…` | cosmetic |
| added | `types.InteractionEntry` |  | `{…}` | additive |
| added | `types.InteractionSpec` |  | `{…}` | additive |
| added | `types.LineGeometry.props[washOpacity]` |  | `{…}` | additive |
| added | `types.LineGeometry.props[wash]` |  | `{…}` | additive |
| added | `types.PointGeometry.props[density]` |  | `{…}` | additive |
| added | `types.PointGeometry.props[halo]` |  | `{…}` | additive |
| added | `types.PointGeometry.props[ink]` |  | `{…}` | additive |
| added | `types.PointGeometry.props[opacity]` |  | `{…}` | additive |
| added | `types.PointGeometry.props[shape]` |  | `{…}` | additive |
| added | `types.SemanticAnnotation.props[colors]` |  | `{…}` | additive |
| added | `types.SemanticAnnotation.props[polarity]` |  | `{…}` | additive |
| added | `types.TargetDetailsOptions` |  | `{…}` | additive |
| changed | `types.ThemeAnnotation.props[axisTitles].doc` | `` "`whenAmbiguous` asks the same question of each axis: `Jan Feb Mar` names\nits own kind and needs no… `` | `` "`whenAmbiguous` asks the same question of each axis: `Jan Feb Mar` names\nits own kind and needs no… `` | cosmetic |
| added | `types.ThemeAnnotation.props[numberFormat].type.props[compact]` |  | `{…}` | additive |
| changed | `types.ThemeChartDefaults.doc` | `"The one block that is allowed to name a chart type.\n\nEverything else at level 1 is a policy the c…` | `"The one block that is allowed to name a chart type.\n\nEverything else at level 1 is a policy the c…` | cosmetic |
| added | `types.ThemeCompileDefaults.props[aspectTolerance]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[bandBalance]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[canvasFit]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[canvasRefit]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[canvasTreatment]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[cellGrowth]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[facetGapColumn]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[facetGapRow]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[labelStep]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[liveLayoutSignal]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[minLabelFontSize]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[rowNamesAbove]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[targetAspect]` |  | `{…}` | additive |
| added | `types.ThemeCompileDefaults.props[themeDecisions]` |  | `{…}` | additive |
| added | `types.ThemeDataLabels.props[anchor]` |  | `{…}` | additive |
| added | `types.ThemeDataLabels.props[fallback]` |  | `{…}` | additive |
| added | `types.ThemeDataLabels.props[minContrast]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[align]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[alt]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[anchor].doc` |  | `` "The earlier corner placement, read by `mastheadTab`, `headerRule` and `footerRule`." `` | cosmetic |
| added | `types.ThemeFurniture.props[at]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[frame]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[height].doc` |  | `"px. A rule's weight, a band's or tab's or image's height."` | cosmetic |
| added | `types.ThemeFurniture.props[href]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[kind].doc` |  | `` "`rule` — a line across its frame; `band` — a filled strip across its frame; `tab` — a short\nblock … `` | cosmetic |
| added | `types.ThemeFurniture.props[kind].type.values[band]` |  | `"band"` | additive |
| added | `types.ThemeFurniture.props[kind].type.values[image]` |  | `"image"` | additive |
| added | `types.ThemeFurniture.props[kind].type.values[rule]` |  | `"rule"` | additive |
| added | `types.ThemeFurniture.props[kind].type.values[tab]` |  | `"tab"` | additive |
| added | `types.ThemeFurniture.props[kind].type.values[text]` |  | `"text"` | additive |
| added | `types.ThemeFurniture.props[src]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[svg]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[text]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[type]` |  | `{…}` | additive |
| added | `types.ThemeFurniture.props[width].doc` |  | `"px. A tab's or image's width; a rule or band spans its frame unless given one."` | cosmetic |
| added | `types.ThemeInk.props[series].type.props[byMark]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[categoricalByCount]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[categoricalText]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[minContrast]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[ordinal]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[repeat]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[roles]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[selection].type.props[ordered]` |  | `{…}` | additive |
| added | `types.ThemeInk.props[series].type.props[selection].type.props[signed].doc` |  | `"How a signed series is coloured. Applies only when the coloured field itself carries\nthe sign: a c…` | cosmetic |
| added | `types.ThemeInteraction` |  | `{…}` | additive |
| added | `types.ThemeLabelFit` |  | `{…}` | additive |
| removed | `types.ThemeLabels.props[angle]` | `{…}` |  | BREAKING |
| added | `types.ThemeLabels.props[fit]` |  | `{…}` | additive |
| added | `types.ThemeLabels.props[overflow]` |  | `{…}` | additive |
| added | `types.ThemeLabels.props[rowNames]` |  | `{…}` | additive |
| changed | `types.ThemeLayout.props[bandStepFit].doc` | `"How strongly category pitch follows the available discrete-axis span.\n  On sparse one-banded axes,…` | `"How strongly category pitch follows the available discrete-axis span.\n  On sparse one-banded axes,…` | cosmetic |
| changed | `types.ThemeLayout.props[density].doc` | `` "Air around the plot. Not the ruler's gradations — those are `tickDensity`." `` | `"Air around the plot: the canvas padding, and a key's gap to the plot (0.8, 1 or 1.4 em of its\nlabe…` | cosmetic |
| added | `types.ThemeLayout.props[titleBlock].type.props[frame]` |  | `{…}` | additive |
| changed | `types.ThemeLayout.props[titleBlock].type.props[gap].doc` | `"The vertical gap between the title block and the chart below it — a\nhouse's whitespace personality…` | `"The vertical gap between the title block and the chart below it — a\nhouse's whitespace personality…` | cosmetic |
| added | `types.ThemeLegend.props[align]` |  | `{…}` | additive |
| added | `types.ThemeLegend.props[arrangement]` |  | `{…}` | additive |
| added | `types.ThemeLegend.props[endValue]` |  | `{…}` | additive |
| added | `types.ThemeLegend.props[gaps]` |  | `{…}` | additive |
| changed | `types.ThemeLegend.props[maxSwatches].doc` | `"The most entries a key to *values* may spend. A legend that names ten\nbubble sizes is a table, not…` | `"The most entries a key to *values* may spend. A legend that names ten\nbubble sizes is a table, not…` | cosmetic |
| added | `types.ThemeLegend.props[swatch].doc` |  | `` "The symbol beside each name in a key to categories: `auto` (default) takes it from the mark —\na sq… `` | cosmetic |
| changed | `types.ThemeLegend.props[swatch].type.kind` | `"literal"` | `"literalUnion"` | BREAKING |
| removed | `types.ThemeLegend.props[swatch].type.value` | `"auto"` |  | BREAKING |
| added | `types.ThemeLegend.props[swatch].type.values` |  | `[…]` | additive |
| added | `types.ThemeLegend.props[titlePosition]` |  | `{…}` | additive |
| changed | `types.ThemeLegend.props[title].doc` | `` "`whenAmbiguous` asks whether the key's labels say what they are: a list\nof names (`Chrome`, `Cairo… `` | `` "Whether a key carries its field's name. `always`; or `whenAmbiguous` (the default), which\nleaves i… `` | cosmetic |
| added | `types.ThemeMarks.props[arc]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[areaEdge]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[barMinLength]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[barOpacity]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[gradient]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[group]` |  | `{…}` | additive |
| changed | `types.ThemeMarks.props[outline].doc` | `"A stroke drawn around every filled mark — a bar, wedge, or point: the\n\"sticker\" / flat-illustrat…` | `` "`series` draws each mark's border in its own series ink, at full strength." `` | cosmetic |
| added | `types.ThemeMarks.props[outline].type.props[source].type.values[series]` |  | `"series"` | additive |
| added | `types.ThemeMarks.props[point].type.props[density]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[point].type.props[halo].doc` |  | `"A ring in the plot's surface colour around each dot, so dots that overlap, or sit on a line,\nstay …` | cosmetic |
| added | `types.ThemeMarks.props[point].type.props[ink]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[point].type.props[opacity]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[point].type.props[shape]` |  | `{…}` | additive |
| added | `types.ThemeMarks.props[texture]` |  | `{…}` | additive |
| added | `types.ThemeSource` |  | `{…}` | additive |
| changed | `types.ThemeSpec.props[interaction].type.kind` | `"object"` | `"ref"` | BREAKING |
| added | `types.ThemeSpec.props[interaction].type.name` |  | `"ThemeInteraction"` | additive |
| removed | `types.ThemeSpec.props[interaction].type.props` | `[…]` |  | BREAKING |
| added | `types.ThemeSpec.props[source]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[axis].type.props[categorical].doc` |  | `"The axis the chart is indexed by, wherever it is drawn."` | cosmetic |
| added | `types.ThemeStructure.props[axis].type.props[floating]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[axis].type.props[measure].doc` |  | `"The axis values are read off, wherever it is drawn."` | cosmetic |
| added | `types.ThemeStructure.props[axis].type.props[x]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[axis].type.props[y]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[baseline].doc` |  | `"The rule the marks stand on (A7): a presence, or one with its own weight, dash and ink."` | cosmetic |
| changed | `types.ThemeStructure.props[baseline].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.ThemeStructure.props[baseline].type.members` |  | `[…]` | additive |
| removed | `types.ThemeStructure.props[baseline].type.values` | `[…]` |  | BREAKING |
| added | `types.ThemeStructure.props[frame].doc` |  | `"The rule round the plot: a presence, or one with its own weight, dash and ink, so a house\nwhose fr…` | cosmetic |
| changed | `types.ThemeStructure.props[frame].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.ThemeStructure.props[frame].type.members` |  | `[…]` | additive |
| removed | `types.ThemeStructure.props[frame].type.values` | `[…]` |  | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[category].doc` |  | `` "The grid across the axis the chart is indexed by; styled on its own, as `measure`." `` | cosmetic |
| changed | `types.ThemeStructure.props[grid].type.props[category].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[category].type.members` |  | `[…]` | additive |
| removed | `types.ThemeStructure.props[grid].type.props[category].type.values` | `[…]` |  | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[layer]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[grid].type.props[measure].doc` |  | `"The grid across the value axis: a presence, or the presence with its own style, dash, weight and in…` | cosmetic |
| changed | `types.ThemeStructure.props[grid].type.props[measure].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[measure].type.members` |  | `[…]` | additive |
| removed | `types.ThemeStructure.props[grid].type.props[measure].type.values` | `[…]` |  | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[minor]` |  | `{…}` | additive |
| added | `types.ThemeStructure.props[grid].type.props[style].doc` |  | `"The style both grids take unless one states its own."` | cosmetic |
| changed | `types.ThemeStructure.props[grid].type.props[zero].doc` | `` "A separate rule where the value axis crosses zero. `omit` leaves\nzero as an ordinary gridline; any… `` | `` "A separate rule where the value axis crosses zero. `omit` leaves\nzero as an ordinary gridline; any… `` | cosmetic |
| changed | `types.ThemeStructure.props[grid].type.props[zero].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.ThemeStructure.props[grid].type.props[zero].type.members` |  | `[…]` | additive |
| removed | `types.ThemeStructure.props[grid].type.props[zero].type.values` | `[…]` |  | BREAKING |
| added | `types.ThemeType.props[keyTitle]` |  | `{…}` | additive |
| added | `types.ThemeType.props[kicker]` |  | `{…}` | additive |
| changed | `types.ThemeVariant.props[then].type.props[interaction].type.kind` | `"object"` | `"ref"` | BREAKING |
| added | `types.ThemeVariant.props[then].type.props[interaction].type.name` |  | `"ThemeInteraction"` | additive |
| removed | `types.ThemeVariant.props[then].type.props[interaction].type.props` | `[…]` |  | BREAKING |
| added | `types.ThemeVariant.props[then].type.props[source]` |  | `{…}` | additive |
| added | `types.TypeRole.props[case].doc` |  | `` "`upper`, `lower`, `title` (each word capitalised, short joining words kept lower-case) or\n`sentenc… `` | cosmetic |
| added | `types.TypeRole.props[case].type.values[sentence]` |  | `"sentence"` | additive |
| added | `types.TypeRole.props[weight].doc` |  | `` "The full CSS scale: `thin` (100) … `regular` (400) … `black` (900), or the number itself." `` | cosmetic |
| changed | `types.TypeRole.props[weight].type.kind` | `"literalUnion"` | `"union"` | BREAKING |
| added | `types.TypeRole.props[weight].type.members` |  | `[…]` | additive |
| removed | `types.TypeRole.props[weight].type.values` | `[…]` |  | BREAKING |

**Behaviour probes**
| Change | IR path | Before | After | Severity |
|---|---|---|---|---|
| changed | `probes.valueInference.TimeSpan "1.02:03:04"` | `"temporal"` | `"nominal"` | behavioural |
| changed | `probes.valueInference.TimeSpan "2.00:00:00"` | `"temporal"` | `"nominal"` | behavioural |

**Retired (retired.json additions)**
| Entry | Vanished in | Last seen in |
|---|---|---|
| `catalog.chartTypes[KPI Card].backends.vegalite.properties[includeZero_x]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[KPI Card].backends.vegalite.properties[includeZero_y]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[KPI Card].backends.vegalite.properties[logScale_x]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[KPI Card].backends.vegalite.properties[logScale_y]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[Map].backends.vegalite.properties[includeZero_x]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[Map].backends.vegalite.properties[includeZero_y]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[Map].backends.vegalite.properties[logScale_x]` | 1.0.0 | 0.5.1 |
| `catalog.chartTypes[Map].backends.vegalite.properties[logScale_y]` | 1.0.0 | 0.5.1 |

</details>

### Added

- The `flint-chart` 1.0.0 catalog: the chart types Marginal Plot, Ridgeline Plot and Table (`Chart.Marginal`, `Chart.Ridgeline`, `Chart.Table`), and the channels, chart properties, options and theme members the sync pull request lists.
- `ChartAssemblyInput.InteractionSpec` (`interaction_spec`), read by the Vega-Lite interactive surface: the generated `InteractionSpec` and `InteractionEntry`, the hand-written `AssistedTargeting` and `TargetDetails` (each `true`/`false` or an object), and `ChartBuilder.Interaction(...)`.
- Typed options for the 23 interaction presets of `flint-chart/interactive` (`SelectOptions`, `NavigateOptions`, `InspectOptions`, …) and one factory per preset, `InteractionEntry.Navigate(new NavigateOptions { Axes = "x", Pan = false })`; `InteractionEntry.Options` stays the generic map for anything else. `flint-cs to-csharp` and `Graspea.Flint.Generators` write a known preset through its factory.
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
- `FlintUpstream.Version` is `1.0.1`.

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
