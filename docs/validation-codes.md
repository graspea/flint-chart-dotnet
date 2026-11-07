# Validation codes

`ChartValidator.Validate(input, backend)` checks a `ChartAssemblyInput` against the pinned `flint-chart` version without running it. The design is in [part 2 §6](design/02-sdk-serialisation-validation-codegen.md). This page lists every code with its severity and the upstream fact behind it.

```csharp
var result = ChartValidator.Validate(input, FlintBackend.Excel);
if (!result.IsValid) { /* an Excel assembler of flint-chart 1.0.0 would throw */ }
result.ThrowIfInvalid();   // FlintValidationException carrying every issue
```

## Severity rule

| Severity | Meaning |
|---|---|
| `Error` | An assembler of the pinned version throws for the target backend. |
| `Warning` | Upstream assembles the document but silently drops or substitutes part of it. |
| `Info` | Everything else, including checks that only `flint-chart/validate` and the `flint-chart-mcp` front door make. |

`IsValid` means "no `Error`". `Build()` and `ToJson()` never validate.

**Target backends.** With a backend, the backend-scoped checks run for that backend only. With `backend: null`, they run for every backend that has the template, and each issue is reported once, at the highest severity among them. The message names the backends the severity applies to. So `IsValid` with `null` means that every backend that has the template assembles the document.

**Rows not yet seen.** A document with streamed data (`ChartData.Stream`, `ChartTemplate.BindStream`) and a template (`ChartTemplate.Validate`) are validated without reading a row. The spec rules run. The key rules (`unknown_field`, `series_field_missing`, `semantic_type_without_field` and the row-key part of `reserved_semantic_type`) run on the keys the row type writes when its shape is static, and are skipped for dictionary, `JsonNode`, `object` or polymorphic rows. `series_field_missing` is then a Warning, because upstream throws only once `data` has rows. The row-content rules and the field types inferred from values are skipped, which `rows_not_inspected` reports. A template is not reported as `data_missing`. So on streamed data or a template, `IsValid` means "no Error that can be decided without the rows". Validate a sample with every rule: `ChartValidator.Validate(template.Bind(sampleRows), backend)`.

**Paths** are wire paths: `chart_spec.encodings.x`, `chart_spec.encodings.y[1].type` (an entry of a static series), `semantic_types.revenue`, `data.values[3]`, `chart_spec.chartProperties.interpolate`. Keys are written verbatim.

**Upstream version.** The upstream facts below, and the probes they cite, describe flint-chart 1.0.0: a probe is the record `tests/fixtures/flint-chart@1.0.0/probes.json` ([generator.md](generator.md#probes)). A later upstream version that changes one is listed in its sync pull request; the validator follows it only when this page does.

## Error

| Code | Upstream fact | Source |
|---|---|---|
| `unknown_chart_type` | An unknown `chartType` throws on all five backends. A `null` `chart_spec` is reported under this code too, because every backend throws reading its `chartType`. | probe `chartType unknown` |
| `chart_type_not_on_backend` | The target backend has no template for the chart type and throws. | `ir/flint-ir.json` |
| `missing_encodings` | `chart_spec.encodings` absent or `null` throws on all five backends. The SDK sees only an explicit `null`: `FlintJson.Deserialize` reads an absent member as `{}` (the `ChartSpec.Encodings` default) and writes it back as `{}`, which `encodings_empty` then covers. | probe `encodings missing` |
| `encodings_empty` | `encodings: {}` throws on Excel, which needs a measure. Info on the other backends. | probe `encodings empty` |
| `channel_null` | `encodings.<ch> = null` throws on Vega-Lite, ECharts, Chart.js and Plotly. Info on Excel, which ignores it. | probe `channel null` |
| `data_missing` | `data` absent or `null` throws on all five backends. `ChartAssemblyInput.Data` is null in both cases, and the document is written without `data`. | probes `data null`, `data absent` |
| `unknown_theme_preset` | A preset id outside the registry throws on Vega-Lite and Plotly. A custom theme whose `extends` names an id outside the registry resolves through the same lookup and throws there too; the issue is then reported at `theme_spec.extends`. Info on ECharts, Chart.js and Excel, which do not apply themes. The lookup is a plain JavaScript object, so a member name of `Object.prototype` (`constructor`, `toString`, `__proto__`, …) is found and does not throw. | probes `themeBackends` (`unknownPresetThrows`, `unknownExtendsThrows`), `prototypeKeys` |
| `row_not_object` | A `null` row throws on all five backends, at any index: upstream reads the keys of every row. A number, string or boolean row throws on all five when a key of the first row is temporal (a temporal semantic type, `Decade`, or values that infer as temporal), because `convertTemporalData` writes that key back into every row. Info otherwise: the MCP front door rejects it, the assemblers do not. The issue names the first such row and the count. | probes `row null`, `row null first`, `row number with temporal x`, `row number with an unbound temporal key`, `row number, no temporal key` |
| `reserved_semantic_type` | A semantic type named like a member of `Object.prototype` (`constructor`, `toString`, `valueOf`, …) passes upstream's `semanticType in TYPE_REGISTRY` but has no registry entry, so every backend throws when the field is bound or a key of the first row. On 1.0.0 Vega-Lite meets the missing entry first in the temporal parser lookup (`parsers is not iterable`); the other backends throw reading its `visEncodings`. | probes `prototypeKeys` |
| `series_multiple_channels`, `series_channel_not_xy`, `series_too_few`, `series_duplicate_field`, `series_field_missing`, `series_color_bound`, `series_nominal_entry` | The static-series rules of `src/core/static-series.ts` throw on the four layout backends. They are checked in upstream's order, and the first that holds is reported. `series_field_missing` covers an entry without a `field`, and a field that is not a key of the first row (checked only when there are rows). On streamed data and templates a field that the row type does not write is a Warning ("an Error once rows exist"). | part 1 §2, `static-series.ts`, part 4 §13.6 |
| `data_empty` (Excel) | With no rows, Excel throws when its template needs a type that untyped fields cannot have without rows; for example, a Bar Chart needs a quantitative measure. With explicit encoding types it assembles. This code is therefore an `Error` only when the Excel rules fail without rows, and `Info` otherwise. On the other backends it is always `Info`. | probes `data values empty` and `data values empty, typed encodings` |
| `data_url_only` (Excel) | Excel reads only `data.values`, so a URL-only document has no rows. The severity rule is the same as for `data_empty`. On the other backends the code is `data_url_not_fetched` (Info). | probe `data url only` |
| `excel_unsupported` (Excel) | Excel throws on: faceting (`column`/`row` bound; `supportsFacets` from the IR); each template's `validate()` condition; a category or measure field that the shared path cannot resolve; a histogram without finite values; and a radar chart with fewer than three metrics. Excel reads a static series (an array encoding) as an unbound channel, so it throws when the template needs that channel (`y` of a Line Chart) and assembles otherwise (`color`). The rules are hand-written in `Validation/Rules/Excel.cs` from `src/excel` of the pinned package, and pinned to the 218 recorded Excel throws of the 1.0.0 corpus and the crafted cases. | `expectations.jsonl`, `probe-expectations.jsonl` |

## Warning

| Code | Upstream fact | Source |
|---|---|---|
| `unsupported_channel` | A channel that the template does not declare for the target backend (with `null`: for any backend) is ignored. On an Excel target, a channel that the Excel rules reject (a facet, `shape`, `detail` and so on) is reported as `excel_unsupported` instead. | `ir/flint-ir.json` channels |
| `unknown_field` | An encoding field that is not a key of the first row is accepted, and the encoding resolves to nothing. It is checked only when the first row is an object, or, on streamed data and templates, against the keys the row type writes; `aggregate: count` without a field is exempt. | probe p5 (part 1 §2) |
| `series_field_missing` | The Warning form on streamed data and templates; see the Error table. | part 4 §13.6 |
| `invalid_option_value` | A discrete `chartProperties` value outside the template's options for the backend is dropped, and upstream emits `invalid-option-value`. A value equal (`===`) to an option value is accepted, and from 1.0.0 so is an array of the same length as an array option whose entries are each `===` (`sameOptionValue`; on 0.5.1 an array never matched). A string equal to an option label is accepted too, ignoring case and surrounding whitespace: upstream coerces it and emits only `coerced-option-label`. `null` is ignored. | `src/core/normalize-properties.ts`, probe `option array value` |
| `unknown_semantic_type` | An unregistered semantic type is kept verbatim. The registry lookup falls back to `UNKNOWN_ENTRY` and the type is inferred from the data, so the annotation has no effect. | probe p16 (part 1 §2) |
| `retired_member` | A channel, chart property, option value or semantic type that the pinned flint-chart removed (`generator/retired.json`, which names the version) is ignored; a removed option value is dropped with `invalid-option-value`. It replaces `unsupported_channel`, `unknown_chart_property`, `invalid_option_value` or `unknown_semantic_type` for that member, on the backends the ledger lists (with `null`: when no target declares the member). A host-state key is exempt, as for `unknown_chart_property`. On 1.0.0: `includeZero_x/y` and `logScale_x/y` of KPI Card and Map on Vega-Lite. The catalog checks report it as FLINT117. | `generator/retired.json`, design part 3 §8.4 |
| `sort_by_invalid` | The four layout backends first run `repairEncodingSorts`, which drops each sort hint `inspectEncodingSort` rejects with the warning `invalid_sort`, and the chart keeps its default order. A `sortBy` is kept when it is `x`, `y` or `color` and that channel is bound (a `field`, or `aggregate: count`), a key of any row (Vega-Lite only: ECharts, Chart.js and Plotly reject sorting by a data field), or a JSON array whose entries are strings, booleans or finite numbers; an array with some other entries keeps the valid ones and still warns. A blank `sortBy`, an unbound `x`/`y`/`color` (`size` and `group` too), a JSON object or number, or an array with no valid entry is dropped, and so is its `sortOrder`. A `sortOrder` other than `ascending` or `descending` is dropped (path `….sortOrder`). Excel reads no sort hint and is never reported. On rows not yet seen without a static row shape, a `sortBy` that may name a field is skipped on Vega-Lite. | `src/core/resolve-semantics.ts` (`inspectEncodingSort`, `repairEncodingSorts`); probe cases `sortBy …` and `sortOrder …` |

## Info

From 1.0.0 the front-door checks live in `flint-chart/validate` (`validateChartInput`, `src/validate/index.ts` of the pinned package); `flint-chart-mcp@1.0.0`, the version that matches the pin, calls it before it assembles (`src/render/assemble.ts:42`) and in `validate_chart` with the backend. The line numbers below are those of the sources embedded in the source maps.

| Code | Fact |
|---|---|
| `encodings_empty`, `channel_null`, `data_empty`, `data_url_only`, `unknown_theme_preset`, `row_not_object` | The non-throwing side of the Error codes above. |
| `data_url_not_fetched` | `flint-chart` never fetches `data.url`; the four layout backends assemble an empty chart. |
| `canvas_too_large` | `baseSize` or `canvasSize` above 4000 px. `flint-chart/validate` and so the MCP front door reject it; the assemblers accept it. Source: `src/validate/index.ts:57` (`DEFAULT_MAX_CANVAS_DIM = 4000`), checked in `validateCanvasCaps` (line 240). |
| `invalid_literal` | `type`, `aggregate`, `sortOrder` or `seriesCountAxis` outside the IR literal set. No recorded fact shows upstream dropping a `type`, `aggregate` or `seriesCountAxis`; a probe that shows a drop would promote this code to Warning. A `sortOrder` outside the set is also `sort_by_invalid` (Warning), because the layout backends drop it (probe case `sortOrder invalid`). |
| `inconsistent_row_keys` | A row's key set differs from the first row's. `flint-chart/validate` checks fields against the union of row keys (`src/validate/index.ts:198`); the assemblers do not. The issue names the first such row and the count. |
| `required_channel_missing` | `x` or `y` is not bound on a template that declares both, or `metric` or `value` on a KPI Card. A binding is a non-blank `field` or `aggregate: count`. `flint-chart/validate` rejects it when it is given a backend, as `validate_chart` does; the assemblers accept it. Source: `src/validate/index.ts:256` (`requiredChannels`: `x` and `y` when the template declares both, `metric` and `value` on KPI Card), applied at line 233. |
| `data_values_and_url` | `data` has both `values` and `url`. The assemblers read `values` only; `flint-chart/validate` rejects it (`src/validate/index.ts:160`). |
| `too_many_rows` | More than 100 000 rows. `flint-chart/validate` and so the MCP front door reject them; the assemblers accept them. Source: `src/validate/index.ts:54` (`DEFAULT_MAX_DATA_ROWS = 100_000`), checked at line 164. |
| `semantic_type_without_field` | A `semantic_types` key that is not a key of the first row. Upstream never reads the annotation. |
| `unknown_chart_property` | A `chartProperties` key that the template does not declare for the target backend (with `null`: for any backend). Host-state keys (`catalog.hostStateKeys`, such as `sort` and `colorScheme`) are exempt. The value is passed through and may have no effect. |
| `continuous_out_of_range` | A number outside the declared `min`..`max` of a continuous property. `normalizeChartProperties` checks discrete values only, so upstream passes it through. |
| `theme_ignored_by_backend` | A theme on a backend that does not apply themes (ECharts, Chart.js, Excel; `catalog.backendsApplyingTheme`). An unknown preset is `unknown_theme_preset` instead. |
| `option_overridden_by_backend` | An option that the backend overwrites (`catalog.ignoredOptions`: `bandStepFitCapacityX/Y`, `facetColumns`, `maxStretchX/Y` on the four layout backends), or any option on Excel, which reads none. |
| `rows_not_inspected` | Streamed data or a template: the rows were not read, so the row-content rules (`row_not_object`, `inconsistent_row_keys`, `too_many_rows`, `data_empty`, the row-value infos below, and the field types inferred from values that the Excel rules and `series_nominal_entry` read) were skipped. Path `data`. Never changes `IsValid`. |

The row-value infos of [part 2 §5.3](design/02-sdk-serialisation-validation-codegen.md) are reported once per field, at the first row that has the value:

| Code | Value |
|---|---|
| `large_integer` | An integer beyond 2^53, which JavaScript reads as the nearest double. |
| `unspecified_datetime` | An ISO date and time without `Z` or an offset, as a `DateTime` of kind Unspecified is written. It is read in the local time zone of the process that runs flint-chart. |
| `timespan_as_string` | A `TimeSpan` of a day or more (`"1.02:03:04"`), which flint-chart 1.0.0 infers as nominal (probe `valueInference`), so each value is a category rather than a duration; 0.5.1 inferred it as temporal. A shorter one looks like a time of day (`"10:00:00"`), which may be one, and is not reported. |
| `null_in_temporal_field` | A `null` in a field annotated with a semantic type whose encodings include `temporal`. |
| `nested_value` | A nested object or array, which upstream reads as a nominal value. |

## How a rule reads a field's type

The Excel rules, and `series_nominal_entry`, need the encoding type that upstream resolves for a channel. The validator ports that decision from `resolveChannelSemantics` in the pinned package:

1. An explicit `type` wins.
2. Otherwise the registered semantic type (or the implicit `Year` rule of part 1 §2) decides, with upstream's data guards: distinct counts (a temporal field on `color` or, from 1.0.0, `group` with at most 12 distinct values is ordinal), numeric checks, and the temporal sampling of `validateTemporalParsing`. From 1.0.0 the values of a `YearMonth`, `Date` or `DateTime` field are first rewritten as upstream holds them (`parseTemporalByType`: `202501`, `2025M01`, `Jan 2025`, `03/2025` become `2025-01`; `20250115`, `2025/01/15`, `Jan 15, 2025`, `15 January 2025` become `2025-01-15`); its regular expressions are ported, so this needs no `Date.parse`. A temporal `x` or `y` on a banded chart type (`BANDED_CHART_TYPES`: bars, Heatmap, Boxplot, …) is ordinal when its dates read as a few snapshots (`readsAsSnapshots`: two to eight distinct dates, or nine to fifty midnight dates that fill under half the days or months between the first and the last at their finest step). The validator reads a date's time from a number (an integer from 1000 to 2999 is a year), four digits, an ISO date, or an ISO date-time with `Z` or an offset. A date-time without an offset is local time and other strings need V8's legacy parser; when such values could change the answer, the type stays undecided between temporal and ordinal.
3. Otherwise the type is inferred from the values (`inferVisCategory`). From 1.0.0 a string is a date only when `isUnambiguousDateString` holds: an ISO date or time (`ISO_DATE`, which also takes a space for `T`, an offset without a colon and a longer fraction) or a US slash date `M/D/YYYY` that `Date.parse` reads, or a full date that `parseTemporalByType` reads (`Jan 15, 2025`, `2025/01/15`). So `Jan 2025` and `Stage 1` are nominal.
4. A quantitative column of ISO date-times becomes temporal, and any `aggregate` makes the type quantitative.

The candidate encodings of each semantic type come from the IR (`catalog.semanticTypes[].visEncodings`).

JavaScript semantics such as `Number(v)`, `String(v)` and set membership are reproduced exactly. `Date.parse` is decided only where `tests/fixtures/flint-chart@1.0.0/probes.json` (`dateParse`) records it: the ISO 8601 forms and the wider `ISO_DATE` forms parse when month, day and time of day are in range (`T24:00` too; a day past the month's end rolls over) and do not when one is out of range (`2024-00`, `2024-01-32`, `T25:00`, `10:60`); a US slash date likewise (`2/30/2025` parses, `13/1/2025` and `1/0/2025` do not); a string without a digit never parses. For numbers V8's legacy parser decides, so they are undecided, and so is anything else V8 may read, such as `"Stage 1"` where `Date.parse` itself is called (`validateTemporalParsing`). A condition that depends on an undecided type is skipped, so the validator never reports an Error that upstream might not throw. The record's `channelTypes` pins the 1.0.0 decisions the validator ports (snapshots, the rewritten spellings, the inferred date strings).

On the 1.0.0 fixture corpus all 218 recorded Excel throws are reported as Errors, and no non-throwing fixture gets one (`FixtureExpectationTests`); the crafted `boxplot x of …` cases pin the snapshot and `YearMonth` decisions on Excel's Boxplot rule. Upstream's `convertTemporalData`, which rewrites the rows the chart draws, is not ported: no rule reads its output.

## Relation to upstream validation

`flint-chart@1.0.0` ships `flint-chart/validate`: `validateChart(input, backend)` for Vega-Lite, ECharts, Chart.js and Plotly runs `validateChartInput` (the front-door checks above) and then assembles, and reports the assembler's warnings with its own snake_case codes. `ChartValidator` needs no JavaScript engine and covers Excel too; where the two meet, the mapping is:

| Upstream | SDK |
|---|---|
| `assembly_failed` (a thrown assembly or `validateChartInput` failure) | any `Error`; the front-door failures are the Infos `too_many_rows`, `canvas_too_large`, `required_channel_missing`, `data_values_and_url` and `inconsistent_row_keys` |
| `invalid-option-value`, `coerced-option-label` | `invalid_option_value`; none for a coerced label |
| `unknown_semantic_type` | `unknown_semantic_type` |
| `invalid_sort` | `sort_by_invalid` (Warning) on the layout backends, each the other way round too (`FixtureExpectationTests`). `flint-chart/validate` and so the MCP front door reject the same hints (`resolveEncodingSort`) as `assembly_failed`. Upstream's sort ignores a valid JSON array on Chart.js and Plotly; the SDK does not report that |
| `interactions_ignored`, `invalid_interaction_spec`, `conflicting_interactions` | none: `interaction_spec` is typed (`InteractionSpec`) but not validated |
| `invalid_updates`, `update_not_applied`, `updates_ignored` and the layout notes (`overflow`, `canvas-fit`, …) | none |
