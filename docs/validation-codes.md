# Validation codes

`ChartValidator.Validate(input, backend)` checks a `ChartAssemblyInput` against the pinned `flint-chart` version without running it. The design is in [part 2 §6](design/02-sdk-serialisation-validation-codegen.md). This page lists every code with its severity and the upstream fact behind it.

```csharp
var result = ChartValidator.Validate(input, FlintBackend.Excel);
if (!result.IsValid) { /* an Excel assembler of flint-chart 0.5.1 would throw */ }
result.ThrowIfInvalid();   // FlintValidationException carrying every issue
```

## Severity rule

| Severity | Meaning |
|---|---|
| `Error` | An assembler of the pinned version throws for the target backend. |
| `Warning` | Upstream assembles the document but silently drops or substitutes part of it. |
| `Info` | Everything else, including checks that only the `flint-chart-mcp` front door makes. |

`IsValid` means "no `Error`". `Build()` and `ToJson()` never validate.

**Target backends.** With a backend, the backend-scoped checks run for that backend only. With `backend: null`, they run for every backend that has the template, and each issue is reported once, at the highest severity among them. The message names the backends the severity applies to. So `IsValid` with `null` means that every backend that has the template assembles the document.

**Paths** are wire paths: `chart_spec.encodings.x`, `chart_spec.encodings.y[1].type` (an entry of a static series), `semantic_types.revenue`, `data.values[3]`, `chart_spec.chartProperties.interpolate`. Keys are written verbatim.

## Error

| Code | Upstream fact | Source |
|---|---|---|
| `unknown_chart_type` | An unknown `chartType` throws on all five backends. A `null` `chart_spec` is reported under this code too, because every backend throws reading its `chartType`. | probe `chartType unknown` |
| `chart_type_not_on_backend` | The target backend has no template for the chart type and throws. | `ir/flint-ir.json` |
| `missing_encodings` | `chart_spec.encodings` absent or `null` throws on all five backends. The SDK sees only an explicit `null`: `FlintJson.Deserialize` reads an absent member as `{}` (the `ChartSpec.Encodings` default) and writes it back as `{}`, which `encodings_empty` then covers. | probe `encodings missing` |
| `encodings_empty` | `encodings: {}` throws on Excel, which needs a measure. Info on the other backends. | probe `encodings empty` |
| `channel_null` | `encodings.<ch> = null` throws on Vega-Lite, ECharts, Chart.js and Plotly. Info on Excel, which ignores it. | probe `channel null` |
| `data_missing` | `data: null` throws on all five backends. | probe `data null` |
| `unknown_theme_preset` | A preset id outside the registry throws on Vega-Lite and Plotly. A custom theme whose `extends` names an id outside the registry resolves through the same lookup and throws there too; the issue is then reported at `theme_spec.extends`. Info on ECharts, Chart.js and Excel, which do not apply themes. The lookup is a plain JavaScript object, so a member name of `Object.prototype` (`constructor`, `toString`, `__proto__`, …) is found and does not throw. | probes `themeBackends` (`unknownPresetThrows`, `unknownExtendsThrows`), `prototypeKeys` |
| `row_not_object` | A `null` row throws on all five backends, at any index: upstream reads the keys of every row. A number, string or boolean row throws on all five when a key of the first row is temporal (a temporal semantic type, `Decade`, or values that infer as temporal), because `convertTemporalData` writes that key back into every row. Info otherwise: the MCP front door rejects it, the assemblers do not. The issue names the first such row and the count. | probes `row null`, `row null first`, `row number with temporal x`, `row number with an unbound temporal key`, `row number, no temporal key` |
| `reserved_semantic_type` | A semantic type named like a member of `Object.prototype` (`constructor`, `toString`, `valueOf`, …) passes upstream's `semanticType in TYPE_REGISTRY` but has no registry entry, so every backend throws when the field is bound or a key of the first row. | probes `prototypeKeys` |
| `series_multiple_channels`, `series_channel_not_xy`, `series_too_few`, `series_duplicate_field`, `series_field_missing`, `series_color_bound`, `series_nominal_entry` | The static-series rules of `src/core/static-series.ts` throw on the four layout backends. They are checked in upstream's order, and the first that holds is reported. `series_field_missing` covers an entry without a `field`, and a field that is not a key of the first row (checked only when there are rows). | part 1 §2, `static-series.ts` |
| `data_empty` (Excel) | With no rows, Excel throws when its template needs a type that untyped fields cannot have without rows; for example, a Bar Chart needs a quantitative measure. With explicit encoding types it assembles. This code is therefore an `Error` only when the Excel rules fail without rows, and `Info` otherwise. On the other backends it is always `Info`. | probes `data values empty` and `data values empty, typed encodings` |
| `data_url_only` (Excel) | Excel reads only `data.values`, so a URL-only document has no rows. The severity rule is the same as for `data_empty`. On the other backends the code is `data_url_not_fetched` (Info). | probe `data url only` |
| `excel_unsupported` (Excel) | Excel throws on: faceting (`column`/`row` bound; `supportsFacets` from the IR); each template's `validate()` condition; a category or measure field that the shared path cannot resolve; a histogram without finite values; and a radar chart with fewer than three metrics. Excel reads a static series (an array encoding) as an unbound channel, so it throws when the template needs that channel (`y` of a Line Chart) and assembles otherwise (`color`). The rules are hand-written in `Validation/Rules/Excel.cs` from `src/excel` of the pinned package, and pinned to the 219 recorded Excel throws and the crafted cases. | `expectations.jsonl`, `probe-expectations.jsonl` |

## Warning

| Code | Upstream fact | Source |
|---|---|---|
| `unsupported_channel` | A channel that the template does not declare for the target backend (with `null`: for any backend) is ignored. On an Excel target, a channel that the Excel rules reject (a facet, `shape`, `detail` and so on) is reported as `excel_unsupported` instead. | `ir/flint-ir.json` channels |
| `unknown_field` | An encoding field that is not a key of the first row is accepted, and the encoding resolves to nothing. It is checked only when the first row is an object; `aggregate: count` without a field is exempt. | probe p5 (part 1 §2) |
| `invalid_option_value` | A discrete `chartProperties` value outside the template's options for the backend is dropped, and upstream emits `invalid-option-value`. A value equal (`===`) to an option value is accepted. A string equal to an option label is accepted too, ignoring case and surrounding whitespace: upstream coerces it and emits only `coerced-option-label`. An array value never equals an option, so upstream always drops it. `null` is ignored. | `src/core/normalize-properties.ts` |
| `unknown_semantic_type` | An unregistered semantic type is kept verbatim. The registry lookup falls back to `UNKNOWN_ENTRY` and the type is inferred from the data, so the annotation has no effect. | probe p16 (part 1 §2) |
| `sort_by_invalid` | A `sortBy` that is not `x`, `y`, `color` or a JSON array string of values is ignored: Vega-Lite logs `sort error` to the console, the other assemblers catch the failed `JSON.parse` silently. `size` and `group` are honoured by some ECharts templates, so they are `Info` on an ECharts target. | `src/vegalite/assemble.ts`, `src/echarts/assemble.ts`, `src/echarts/templates/utils.ts`, `src/core/filter-overflow.ts` |

## Info

The `flint-chart-mcp@0.5.1` line numbers below are those of the sources embedded in that package's source map (`dist/server.js.map`), the version that matches the `flint-chart` pin.

| Code | Fact |
|---|---|
| `encodings_empty`, `channel_null`, `data_empty`, `data_url_only`, `unknown_theme_preset`, `row_not_object` | The non-throwing side of the Error codes above. |
| `data_url_not_fetched` | `flint-chart` never fetches `data.url`; the four layout backends assemble an empty chart. |
| `canvas_too_large` | `baseSize` or `canvasSize` above 4000 px. Only the MCP front door rejects it; the assemblers accept it. Source: `flint-chart-mcp@0.5.1` `src/render/assemble.ts:26` (`MAX_CANVAS_DIM = 4000`), checked at lines 105-109. |
| `invalid_literal` | `type`, `aggregate`, `sortOrder` or `seriesCountAxis` outside the IR literal set. No recorded fact shows upstream dropping the value. A probe that shows a drop would promote this code to Warning. |
| `inconsistent_row_keys` | A row's key set differs from the first row's. The MCP front door checks fields against the union of row keys; the assemblers do not. The issue names the first such row and the count. |
| `required_channel_missing` | `x` or `y` is not bound on a template that declares both, or `metric` or `value` on a KPI Card. A binding is a non-blank `field` or `aggregate: count`. The MCP front door rejects it; the assemblers accept it. Source: `flint-chart-mcp@0.5.1` `src/render/assemble.ts:158` (`requiredChannels`: `x` and `y` when the template declares both, `metric` and `value` on KPI Card), applied at line 141. |
| `data_values_and_url` | `data` has both `values` and `url`. The assemblers read `values` only. |
| `too_many_rows` | More than 100 000 rows. The MCP front door rejects them; the assemblers accept them. Source: `flint-chart-mcp@0.5.1` `src/render/assemble.ts:23` (`MAX_DATA_ROWS = 100_000`), checked at line 88. |
| `semantic_type_without_field` | A `semantic_types` key that is not a key of the first row. Upstream never reads the annotation. |
| `unknown_chart_property` | A `chartProperties` key that the template does not declare for the target backend (with `null`: for any backend). Host-state keys (`catalog.hostStateKeys`, such as `sort` and `colorScheme`) are exempt. The value is passed through and may have no effect. |
| `continuous_out_of_range` | A number outside the declared `min`..`max` of a continuous property. `normalizeChartProperties` checks discrete values only, so upstream passes it through. |
| `theme_ignored_by_backend` | A theme on a backend that does not apply themes (ECharts, Chart.js, Excel; `catalog.backendsApplyingTheme`). An unknown preset is `unknown_theme_preset` instead. |
| `option_overridden_by_backend` | An option that the backend overwrites (`catalog.ignoredOptions`: `bandStepFitCapacityX/Y`, `facetColumns`, `maxStretchX/Y` on the four layout backends), or any option on Excel, which reads none. |

The row-value infos of [part 2 §5.3](design/02-sdk-serialisation-validation-codegen.md) are reported once per field, at the first row that has the value:

| Code | Value |
|---|---|
| `large_integer` | An integer beyond 2^53, which JavaScript reads as the nearest double. |
| `unspecified_datetime` | An ISO date and time without `Z` or an offset, as a `DateTime` of kind Unspecified is written. It is read in the local time zone of the process that runs flint-chart. |
| `timespan_as_string` | A `TimeSpan` of a day or more (`"1.02:03:04"`), which upstream infers as temporal. A shorter one looks like a time of day (`"10:00:00"`), which infers nominal, and is not reported. |
| `null_in_temporal_field` | A `null` in a field annotated with a semantic type whose encodings include `temporal`. |
| `nested_value` | A nested object or array, which upstream reads as a nominal value. |

## How a rule reads a field's type

The Excel rules, and `series_nominal_entry`, need the encoding type that upstream resolves for a channel. The validator ports that decision from `resolveChannelSemantics` in the pinned package:

1. An explicit `type` wins.
2. Otherwise the registered semantic type (or the implicit `Year` rule of part 1 §2) decides, with upstream's data guards: distinct counts, numeric checks, and the temporal sampling of `validateTemporalParsing`.
3. Otherwise the type is inferred from the values.
4. A quantitative column of ISO date-times becomes temporal, and any `aggregate` makes the type quantitative.

The candidate encodings of each semantic type come from the IR (`catalog.semanticTypes[].visEncodings`).

JavaScript semantics such as `Number(v)`, `String(v)` and set membership are reproduced exactly. `Date.parse` is decided only in three cases: the ISO 8601 forms parse, and a string without a digit never parses (both recorded in `generator/verify/probes.json`, `dateParse`); for numbers V8's legacy parser decides, so they are undecided. Anything else, such as `"Stage 1"`, which V8 also parses, leaves the type undecided. A condition that depends on an undecided type is skipped, so the validator never reports an Error that upstream might not throw.

On the 0.5.1 fixture corpus this resolves every Excel decision. All 219 recorded Excel throws are reported as Errors, and no non-throwing fixture gets one (`FixtureExpectationTests`).

## Not implemented yet

`retired_member` (Warning): the retirement ledger `generator/retired.json` is empty for 0.5.1, so there is nothing to report. The rule arrives with the first sync that retires a member, together with the emitter support of design §7.8. Until then a retired key or channel is reported as `unknown_chart_property` or `unsupported_channel`.

## Relation to upstream validation

`flint-chart@0.5.1` has no public validator. Upstream `main` adds `flint-chart/validate`, with the snake_case codes `assembly_failed`, `interactions_ignored`, `invalid_interaction_spec` and `unknown_semantic_type`. When it ships, the mapping is:

| Upstream | SDK |
|---|---|
| `assembly_failed` | any `Error` |
| `unknown_semantic_type` | `unknown_semantic_type` |
| `interactions_ignored`, `invalid_interaction_spec` | none yet (`interaction_spec` round-trips through `Extensions`) |
