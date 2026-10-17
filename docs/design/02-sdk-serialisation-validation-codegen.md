## SDK design, part 2: serialisation, validation, code generation

### 5. Serialisation and the row-data contract

#### 5.1 Setup and the host-options guarantee

```csharp
namespace Graspea.Flint.Serialization;

public static class FlintJson
{
    public static JsonSerializerOptions Options { get; }                 // read-only after construction
    public static string Serialize(ChartAssemblyInput input, bool indented = false);
    public static byte[] SerializeToUtf8Bytes(ChartAssemblyInput input);
    public static void Serialize(Utf8JsonWriter writer, ChartAssemblyInput input);
    public static JsonNode SerializeToNode(ChartAssemblyInput input);
    public static ChartAssemblyInput Deserialize(string json);            // throws System.Text.Json.JsonException, unwrapped
    public static ChartAssemblyInput Deserialize(ReadOnlySpan<byte> utf8);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(ChartAssemblyInput))]
[JsonSerializable(typeof(SemanticAnnotationDto))]   // one entry per converter-owned DTO
internal sealed partial class FlintJsonContext : JsonSerializerContext { }
```

`Options`: `PropertyNamingPolicy = null`, `DictionaryKeyPolicy = null`, default encoder (`<` escapes are harmless to a JSON consumer), `TypeInfoResolver = FlintJsonContext.Default`. The spec part is reflection-free; source generation on net8 supports everything this needs (type-level and property-level converters, `Nullable<struct>` with a type-level converter, `required` members; spike). `Deserialize` throws STJ's `JsonException` on malformed input, on a missing `required` member (`chart_spec`, `chartType`) and on a string-backed struct read from `""` or a non-string (the struct's `ArgumentException` is wrapped so the contract holds); nothing else is wrapped.

**Host-options guarantee.** Every wire rule is attached to the types, never to options: `[JsonPropertyName]`; per-property `WhenWritingNull`; `[JsonNumberHandling(Strict)]` per type; closed-generic `VerbatimKeyDictionaryConverter<T>` on every map property including generated `Record<string, T>` members and `chartDefaults`; `[JsonConverter]` on every union type; rows as `JsonArray` written with `JsonNode.WriteTo`. Therefore `JsonSerializer.Serialize(input, hostOptions)` with a snake_case property **and** dictionary-key policy, nulls written, `IgnoreReadOnlyProperties = true`, `NumberHandling = WriteAsString`, `WriteIndented` or a host source-generated `TypeInfoResolver` yields the same document as `FlintJson.Serialize` (spike: byte-identical in every listed case). A contract test asserts this with keys that a naming policy would alter (`strokeDash`, `fiscalYear`, `logScale_x`). Out of scope: `ReferenceHandler.Preserve` (injects `$id`) and custom host converters registered for `string` or `double`.

**Converter-attributed types own the wire shape.** For `ChartData`, `EncodingValue`, `SemanticAnnotation`, `ChartTheme`, `ContinuousMarkCrossSection` and the string-backed structs the `[JsonConverter]` on the type is the implementation: wire names, null omission and `Extensions` handling happen inside the converter, which serialises the object form through an internal attribute-driven DTO (to avoid recursion). Every converter serialises its DTO through `FlintJsonContext.Default.<Dto>` and ignores the caller's `options`: a host that serialises through its own source-generated context has no metadata for the SDK's internal DTOs and would get `NotSupportedException` (spike). Attributes on the public members of those classes are documentation only. A generated `JsonNode` member (an override target) carries `[JsonConverter(typeof(JsonNodePassThroughConverter))]`, which writes with `WriteTo` and no caller options; without it a host `NumberHandling = WriteAsString` quotes a number held in a `JsonNode` (found by the first generator run). The converters are public infrastructure (`[EditorBrowsable(Never)]`): a host source-generated context instantiates every `[JsonConverter]` type it meets and reports SYSLIB1220 for an internal one. `VerbatimKeyDictionaryConverter<T>` resolves `T` from `FlintJsonContext` (hand-written map members) or from the generated `FlintMapJsonContext` (`Generated/Json/FlintMapJsonContext.g.cs`), into which the options emitter writes one `[JsonSerializable]` per map value type it emits, so a new upstream `Record<string, X>` member is registered by `npm run generate` rather than failing at the consumer's first serialisation; it is a context of its own because System.Text.Json reads `[JsonSerializable]` from one partial declaration only.

| Wire | C# | Write | Read |
|---|---|---|---|
| `string \| SemanticAnnotation` | `SemanticAnnotation` | string iff only `Type` is set and `Extensions` is empty; else object | both |
| `ChartEncoding \| string \| (…)[]` | `EncodingValue` | object, or array when `IsSeries` (even with one entry) | all three, including arrays with mixed string and object items |
| `{values} \| {url}` | `ChartData` | whichever is set; both if both present (validator flags) | both |
| `ThemeSpec \| string` | `ChartTheme` | string or object | both |
| `number \| {x,y,…}` | `ContinuousMarkCrossSection` | number or object | both |
| `chartType`, `semanticType`, preset id, encoding literals | string-backed structs | string | string, verbatim; `{"type":null}` reads as null |
| `chartProperties`, `chartDefaults` values | `Dictionary<string, JsonNode?>` | pass-through (`WriteTo`, no caller options) | pass-through |

#### 5.2 Nulls, forward compatibility, canonical output

- **Spec part**: every optional property is omitted when null. Upstream throws on a `null` channel value (Vega-Lite, ECharts, Chart.js, Plotly) and on `theme_spec: null` (Vega-Lite, Plotly) but tolerates absence; "omitted" and "null" are one state in C# and no upstream field distinguishes them. The SDK never invents defaults.
- **Row part**: nulls are always written (§5.3).
- **Forward compatibility**: `[JsonExtensionData] Dictionary<string, JsonElement>? Extensions` on every class, hand-written and generated. A document from a newer upstream (`interaction_spec`, a new theme path, a new option) round-trips byte-identical through reflection and through `FlintJsonContext` (spike); extensions are written last. Upstream ignores unknown keys everywhere, so emitting them is harmless. Unknown literals in `EncodingType`, `Aggregate`, `SortOrder`, `SeriesCountAxis` round-trip verbatim because they are string-backed.
- **Canonical output**: hand-written classes in fixed declaration order; generated DTOs in IR order, which is sorted by wire name (so an upstream reordering of a d.ts interface never changes output inside a line); extensions last. Maps in insertion order (`Dictionary<,>` without removals; `Property(key, null)` is the only removal and the result stays deterministic for a given call sequence; deliberate simplification, upgrade path is an internal ordered map once the properties are frozen as `IDictionary<,>` during the M2 previews, §8.3). Numbers: STJ shortest round-trip; `decimal` as-is; integers without `.0`. An encoding string shorthand is read but written as `{"field":…}`; a trivial annotation is written as the string form. The contract canonicaliser applies the same two rules to fixtures before `DeepEquals`. No timestamps or version stamps in the JSON.

#### 5.3 The row-data contract

**One name for three places.** A field has exactly one wire name, used as a `data.values` key, a `semantic_types` key, a `field_display_names` key and an `encodings.*.field` value. Upstream matches by exact string comparison and infers from the key itself.

```csharp
public sealed class RowOptions
{
    public static RowOptions Default { get; }                                    // camelCase, enums as names, nulls written
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; } = JsonNamingPolicy.CamelCase;
}
```

Decided: camelCase by default (`RowOptions.Default`), verbatim names through `new RowOptions { PropertyNamingPolicy = null }`, `[JsonPropertyName]` honoured on every path.

Resolution:

1. **Lambda selectors** (`r => r.Month`): a `MemberExpression` on a property or field of `TRow`, optionally under a boxing `Convert`; anything else (`r.Region.Length`, `r.Value + 1`) throws `ArgumentException`. The name comes from STJ metadata: `typeInfo.Properties` matched by `JsonPropertyInfo.AttributeProvider` as `MemberInfo` (Module + MetadataToken) → `JsonPropertyInfo.Name`. This honours `[JsonPropertyName("GDP per capita")]`, a positional record's `[property:]` attribute, inherited members and the naming policy exactly as the rows are written. On net8 a source-generated `JsonTypeInfo<TRow>` leaves `AttributeProvider` null (spike; the net9 and net10 generators populate it); the fallback reads `JsonPropertyNameAttribute` from the expression's own `MemberInfo`, otherwise applies `typeInfo.Options.PropertyNamingPolicy`, then checks the result against `typeInfo.Properties` and throws `ArgumentException` for a member that is not serialised. `[JsonPropertyName]` is therefore honoured on both paths.
2. **String field names** are literal.
3. **Dictionary rows**: keys literal, `DictionaryKeyPolicy = null`.
4. The `JsonTypeInfo<TRow>` is captured once at the entry point and used by every typed overload of that builder. Mixing `X("Month")` with typed rows is allowed; `unknown_field` catches a mismatch because it runs on `FirstRowKeys`.

Default camelCase matches Flint's own examples and fixtures (`city`, `revenue`) and keeps `FiscalYear` → `fiscalYear` on the same inference path as a JS author's key.

**Why rows are materialised.** `FromRows` writes every row to a `JsonNode` immediately: `FirstRowKeys` is exactly what upstream sees, the validator needs no reflection, output is fixed before `Build()`, and the null guarantee survives hostile host options because `JsonNode` content ignores `WhenWritingNull`. Memory is ~2x at chart-data sizes; a lazy `IEnumerable<TRow>` source written straight to the `Utf8JsonWriter` is an additive upgrade.

**Null guarantee on both paths.** `FromRows(rows, JsonTypeInfo<TRow>)` and `FromRows(rows, RowOptions)` throw `ArgumentException` when the effective `DefaultIgnoreCondition != Never` or any `JsonPropertyInfo` has `ShouldSerialize` or an ignore condition set (STJ sets `ShouldSerialize` for `[JsonIgnore(WhenWritingNull)]` members in both paths, spike), because a key dropped from `data[0]` silently breaks key detection and static series. A predicate can also depend on the value, so each written row is checked to contain every member the metadata writes. Metadata that writes numbers as strings (`JsonNumberHandling.WriteAsString`, on the options, the type or a member) is refused too, because upstream would read those numbers as text. The `JsonTypeInfo` path otherwise writes values exactly as the caller's metadata does: the library's enum-name and non-finite converters belong to the `RowOptions` path.

**.NET type → JSON value.** Row serialisation uses a separate `JsonSerializerOptions`: `DefaultIgnoreCondition = Never`, `JsonStringEnumConverter` (no naming policy), the user's naming policy, one converter for non-finite doubles.

| .NET | JSON | Upstream inference | Notes |
|---|---|---|---|
| `bool` | `true`/`false` | nominal | |
| `byte`…`long`, `uint`…`ulong` | number | quantitative | beyond 2^53 loses precision in JS; Info `large_integer` on the first row |
| `float`, `double` | number | quantitative | `NaN`/`±Infinity` → `null` (STJ would throw; a string would be nominal) |
| `decimal` | number | quantitative | written exactly; JS reads a double |
| `DateOnly` | `"2024-01-15"` | temporal, UTC midnight | recommended for dates; the zone semantics of JS `Date.parse` are **unverified** here (spec behaviour, not probed) |
| `DateTimeOffset`, `DateTime` Utc/Local | ISO-8601 with `Z`/offset | temporal, absolute instant | STJ default |
| `DateTime` Unspecified | `"2024-01-15T10:00:00"` | temporal, interpreted in the local time zone of the process that runs flint-chart | written as-is; Info `unspecified_datetime` |
| `TimeOnly` | `"10:00:00"` | nominal (probed) | pass a number (`TotalSeconds`) for an axis |
| `TimeSpan` | `"1.02:03:04"` | **temporal** (probed: digit-leading, V8 `Date.parse` accepts it; `"2.00:00:00"` too) | write `TotalSeconds` as a number; Info `timespan_as_string` |
| enum | member name | nominal | integer enums would be mis-inferred as quantitative |
| `string` | string | numeric (including `''`/whitespace) → quantitative, date-like → temporal, else nominal | upstream behaviour |
| `Guid`, `Uri` | string | nominal (probed, digit-leading and letter-leading GUIDs alike) | |
| `null`, `Nullable<T>` null | `null` | — (an all-null column is nominal) | **always written**; Info `null_in_temporal_field` when the column is annotated temporal |
| nested object | nested JSON | nominal (probed) | passed through; Info `nested_value`; rendering effect **unverified** |
| collection | JSON array | a single-number array (`[1]`) is quantitative (probed), otherwise nominal | as above |
| `JsonElement`, `JsonNode` | verbatim | — | escape hatch |

Integer years stay quantitative unless annotated `Year`/`Decade`, or the name split on camelCase and non-alphanumerics contains the token `year` and all values are integers 1500–2200 with at least two distinct values (§2); documented, not diagnosed.

### 6. Validation

```csharp
namespace Graspea.Flint.Validation;

public static class ChartValidator
{
    public static ValidationResult Validate(ChartAssemblyInput input, FlintBackend? backend = null);   // null = any backend with the template
}
public sealed class ValidationResult
{
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsValid { get; }                                   // no Error
    public void ThrowIfInvalid();                                  // throws FlintValidationException when !IsValid
}
public sealed class FlintValidationException : Exception
{
    public IReadOnlyList<ValidationIssue> Issues { get; }          // every issue of the result, not only the Errors
}
public sealed class ValidationIssue                               // init-only, internal constructor; fields can be added later
{
    public IssueSeverity Severity { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }
    public string Path { get; init; }
    public Channel? Channel { get; init; }
    public string? Field { get; init; }
}
public enum IssueSeverity { Info, Warning, Error }
```

Decided: `Build()` and `ToJson()` never validate; `Validate(backend)` is explicit. The engine is ~300 hand-written lines over generated tables (chart types x backends, channels per template and backend, discrete option values, continuous ranges, semantic types, presets, `backendsApplyingTheme`, host-state keys, Excel facet support). Nothing else is modelled: no defaults, no `check()` applicability, no data inference (a rule that needs a field's type reads the encoding's explicit `type` or the registered semantic type and skips a field that has neither).

**Severity rule** (decided). *Error*: an assembler of the pinned `flint-chart` version throws for the target backend (for `backend == null`: any backend that has the template throws, which keeps `IsValid ⇒ every backend that has the template assembles`, §8.7). *Warning*: upstream silently drops or substitutes. *Info*: everything else, including what only the MCP front door rejects. `IsValid` means "no Error", so an Error code changes consumer control flow and is tied to upstream throw behaviour only. Codes are snake_case.

**Error**

| Code | Upstream fact |
|---|---|
| `unknown_chart_type`, `chart_type_not_on_backend` | throws on every backend / on that backend |
| `missing_encodings` | `encodings` absent throws on all five |
| `encodings_empty` (Excel target only) | `encodings: {}` throws on Excel ("requires one quantitative measure axis"); Info elsewhere |
| `channel_null` (Vega-Lite, ECharts, Chart.js, Plotly targets) | `encodings.x = null` throws there; Excel accepts it (Info on an Excel target) |
| `data_missing` | `data: null` throws on all five |
| `unknown_theme_preset` | throws on Vega-Lite and Plotly; Info on other backends. A member name of `Object.prototype` (`constructor`, `toString`, …) is found by the plain-object lookup and does not throw (probe `prototypeKeys`) |
| `row_not_object` | a `null` row throws on all five (keys are read from every row); a number, string or boolean row throws on all five when a key of the first row is temporal (`convertTemporalData` writes it back into every row); Info otherwise (probes `row null`, `row number with temporal x`) |
| `reserved_semantic_type` | a semantic type named like a member of `Object.prototype` passes `semanticType in TYPE_REGISTRY` but has no `visEncodings`; it throws on all five when the field is bound or a key of the first row (probe `prototypeKeys`) |
| `series_multiple_channels`, `series_channel_not_xy`, `series_too_few`, `series_duplicate_field`, `series_field_missing` (only when `Values` has rows), `series_color_bound`, `series_nominal_entry` (explicit `type` nominal/ordinal, or a registered semantic type that resolves nominal/ordinal; the data-inferred case is not covered) | each throws on the four layout backends |
| `data_empty` (Excel target only) | Excel throws with no `values`; Info elsewhere |
| `data_url_only` (Excel target only) | Excel throws on url-only data; Info `data_url_not_fetched` elsewhere |
| `excel_unsupported` (Excel target only) | Excel throws on: facets (`column`/`row` bound; `supportsFacets` from the IR); a static-series (array) encoding where the template needs that channel, because Excel reads the array as an unbound channel; a category/measure field that cannot be resolved (`field: null`, no quantitative measure); and the `validate()` conditions recorded from the 0.5.1 corpus: continuous colour on scatter/bubble, stacked bar and line; continuous grouping; a `shape` encoding; unlegended detail series; colour-grouped box-whisker; waterfall total placement; negative pyramid values; categorical radar x; quantitative x/y/measure required. 16 of 18 Excel templates have a `validate()`; its body is not extracted, so these are hand-written rules in `Rules/Excel.cs`, each pinned to a recorded outcome. 219 of 695 applicable fixtures throw on Excel (130 facets, 46 non-quantitative XY, 43 other), so this code carries the bulk of the Excel contract |

**Warning**

| Code | Fact |
|---|---|
| `unsupported_channel` | not declared by the template (any backend, or the target backend); ignored on Vega-Lite, ECharts, Chart.js and Plotly; on an Excel target the facet and `validate()` cases are `excel_unsupported` Errors instead |
| `unknown_field` | encoding field not in `FirstRowKeys` (only when `Values` present; exempt for `aggregate: count` without a field); upstream accepts it silently and the encoding resolves to nothing |
| `invalid_option_value` | discrete value outside the template's set for the backend; upstream `invalid-option-value` drops it |
| `unknown_semantic_type` | unregistered name kept verbatim; the registry lookup falls back to `UNKNOWN_ENTRY` and the type is inferred from the data, so the annotation has no effect |
| `retired_member` | key, option or channel from `retired.json`, keyed by template and backend; a retired key or channel is ignored upstream; a retired option value yields upstream `invalid-option-value` |
| `sort_by_invalid` | not `x`/`y`/`color` and not a JSON array string; Vega-Lite warns and ignores, the other backends ignore silently (`size` and `group`, honoured by some ECharts templates, are Info on an ECharts target) |

**Info**: `required_channel_missing` (x/y heuristic, KPI Card `metric`/`value`; the MCP front door rejects, the assemblers accept), `data_empty` and `encodings_empty` on non-Excel targets, `data_values_and_url`, `too_many_rows` (> 100 000), `canvas_too_large` (> 4000 px), `channel_null` on an Excel target, `semantic_type_without_field`, `unknown_chart_property` (declared properties, encoding-action and host-state keys are exempt), `continuous_out_of_range`, `theme_ignored_by_backend` (ECharts, Chart.js, Excel), `option_overridden_by_backend` (the five overwritten options; every option on an Excel target), `data_url_not_fetched`, `unknown_theme_preset` on non-theme backends, `invalid_literal` (`type`/`aggregate`/`sortOrder`/`seriesCountAxis` outside the IR literal set; no recorded fact shows upstream dropping the value, so it is Info under the severity rule; a probe that shows a drop promotes it to Warning, which §8.3 allows in any release), `inconsistent_row_keys` (a row's key set differs from `FirstRowKeys`; runs on the `JsonArray`; the MCP front door checks fields against the union of row keys, the assemblers do not), `row_not_object` for a non-object row that does not throw (an array, or a primitive when no key of the first row is temporal; the MCP front door rejects it; `FirstRowKeys` and key detection assume an object; the throwing cases are Errors above), and the row infos of §5.3.

**Relation to upstream validation.** For 0.5.1 there is no public upstream validator. The SDK validator is a static pre-flight: the assemblers' throw conditions plus the MCP `prepareInput` checks as Infos plus catalog checks upstream never performs; it cannot predict `overflow`. The contract enforced on the fixture corpus (§10): *assembler throws ⇒ an Error is reported*; *no Error ⇒ every supported assembler assembles without throwing*; *upstream `invalid-option-value` ⇒ `invalid_option_value`*. Agents validate SDK output with upstream `flint-chart-mcp` `validate_chart` (vegalite, echarts, chartjs); see the README "Validate against flint-chart" section (companion). When upstream ships `flint-chart/validate`, the snake_case vocabulary already overlaps and the mapping becomes a table in `docs/validation-codes.md`.

### 7. Code generation from TypeScript/JSON definitions

#### 7.1 Inputs

| Input | Used for | Why |
|---|---|---|
| `flint-chart` d.ts (`package.json` `types` → `dist/index.d.ts` → re-exported `types-BWc1W3v4.d.ts`, followed by the TypeScript checker, never globbed), via the TypeScript compiler API | `AssembleOptions`, `ThemeSpec` subtree (emitted); `ChartAssemblyInput`, `ChartData`, `ChartSpec`, `ChartEncoding`, `SemanticAnnotation`, `ChartSize`, `ContinuousMarkCrossSection` (extracted for the parity test, not emitted) | the only precise source; MCP schemas type these as `any`; no `any`/`unknown` in `ThemeSpec` or `AssembleOptions` on 0.5.1 |
| runtime registries via `import 'flint-chart'` (`*AllTemplateDefs`, `channels`, `SemanticTypes` + `getRegistryEntry`, `THEME_PRESETS`, `listThemePresets`, `EXCEL_TYPE_MAP`, Excel template `channels`) | templates x backends, channels, `ChartPropertyDef`s, option values, semantic types, presets, Excel support | `chartType` is `string` in the d.ts; only the registries know the 48 names and 296 properties |
| `flint-chart/test-data` `TEST_GENERATORS` + the five assemblers | fixtures and expectations | |
| MCP `list_chart_types` / zod schemas | not used | subset of the registry, untyped; accepted as an alternative intermediate format only through the IR |

Pinning: `generator/package.json` pins `"flint-chart": "0.5.1"` exactly, integrity hash in the lock file, installed with `--ignore-scripts`; never a checkout of `main`. A fork or an unpublished build of flint-chart: point `generator/package.json` at a tarball.

#### 7.2 Pipeline

```
generator/
  package.json            flint-chart pinned exact; typescript (exact), tsx; node:test
  src/extract/dts.ts      TypeScript checker -> IR.types
  src/extract/registry.ts import('flint-chart') -> IR.catalog
  src/extract/fixtures.ts TZ=UTC, Park-Miller seed 42 -> fixtures + expectations
  src/extract/crosscheck.ts  one-off, not in CI: compares mapped fixtures with upstream's shared/test-data input.json files (§7.9)
  src/probes.ts           runtime probes behind documented facts (null handling, theme backends, value inference); outputs recorded in verify/
  src/diff.ts             ir-diff: two IR files -> markdown table (added / removed / changed, with IR paths); feeds the sync PR body
  src/ir.ts               IR types + canonical writer (sorted keys, LF, 2 spaces, trailing newline)
  src/emit/*.ts           constants, options, theming, tables, builders, enums, entries, public-api, docs
  src/naming.ts           naming and collision rules (§7.6)
  scripts/                install-upstream.mjs (gate), verify-provenance.mjs, pick-upstream-version.mjs, upstream-report.mjs (bot helpers, companion)
  overrides.json          the only hand-maintained input; validated against overrides.schema.json
  overrides.schema.json   keys: unions, flatten, renames, dedup, docs, backendsApplyingTheme, hostStateKeys, ignoredOptions, keepObsolete
  retired.json            tombstone ledger, auto-appended, hand-reviewed
  mcp-pin.json            written by install-upstream.mjs: the flint-chart-mcp version the README mcp-commands section pins (companion, "AI and MCP readiness" §5)
  ir/flint-ir.json        committed; the single catalog source
npm run extract -> npm run emit -> npm run fixtures     (npm run generate = all three)
```

Installing is a separate, gated step, never part of `generate`: `npm run install-upstream -- 0.6.0` runs `npm install --save-exact --ignore-scripts flint-chart@0.6.0`, then `npm audit signatures`, then `node scripts/verify-provenance.mjs 0.6.0` (repository `microsoft/flint-chart`, workflow `npm-publish.yml`, `gitCommit` equal to the packument's `gitHead`; the provenance `ref` is `refs/heads/main`, so no tag ref is checked), and last resolves the `flint-chart-mcp` version for the README `mcp-commands` section (`npm view flint-chart-mcp@0.6.0 version`; when that version does not exist, the newest `flint-chart-mcp` version not above the pin) into `generator/mcp-pin.json`, so that `generate` stays offline. `npm run generate` always uses the installed pin: extract → write IR → diff against the committed IR and append removals to `retired.json` → emit (C#, PublicAPI, the marked README sections, `docs/package/AGENTS.md`, `catalog.json`) → fixtures and expectations → update `FlintUpstream.Version`. `npm run generate -- --check` writes to a temp dir and diffs (§7.7). Each step is idempotent. `emit` needs only the IR, so anyone can emit from a JSON definition without Node access to flint-chart.

#### 7.3 IR (committed)

```jsonc
{
  "upstream": { "package": "flint-chart", "version": "0.5.1", "integrity": "sha512-…" },
  "types": {
    "ChartAssemblyInput": { "kind": "object", "props": [ { "name": "data", "optional": false, "type": { "kind": "union", "members": [ … ] }, "doc": "…" }, … ] },
    "ChartEncoding": { "props": [ { "name": "aggregate", "optional": true, "type": { "kind": "literalUnion", "values": ["count","sum","average","mean"] } }, … ] },
    "AssembleOptions": { … }, "ThemeSpec": { … }, "ContinuousMarkCrossSection": { "kind": "union", … }
  },
  "catalog": {
    "backends": ["vegalite","echarts","chartjs","plotly","excel"],
    "backendsApplyingTheme": ["vegalite","plotly"],            // overrides.json, backed by the recorded probe (§7.5)
    "hostStateKeys": ["facetColumns","pivot","sort","colorScheme","arrange","chartType","xAxisType","yAxisType","showValueLabels","showTextLabels"],   // overrides.json; not provably closed
    "channels": ["x","y",…,"goal","theta"],                    // union of the core constant (26) and all declared template channels: 27 on 0.5.1; "text" excluded (declared by none)
    "chartTypes": [ { "name": "Line Chart",
        "backends": { "vegalite": { "category": "Lines & Areas", "channels": ["x","y","color",…],
            "properties": [ { "key": "interpolate", "label": "Curve", "type": "discrete",
                "options": [ { "label": "Default", "omitted": true }, { "label": "Monotone", "value": "monotone" }, … ] }, … ] },
          "excel": { "channels": ["x","y","color","strokeDash"], "typeMapping": { … }, "supportsFacets": false, "hasValidate": true } } }, … ],
    "semanticTypes": [ { "name": "YearMonth", "t0": "Temporal", "t1": "…", "doc": "…" }, … ],
    "themePresets": [ { "id": "nyt", "label": "…", "description": "…" }, … ]
  }
}
```

`category` is stored per backend because upstream's categories differ between backends (`Lines & Areas` on vegalite, `Line & Area` on chartjs); Excel has none and is the only backend with `typeMapping`, `supportsFacets` and `hasValidate`. The required-channel heuristic (`requiredHeuristic`) behind `required_channel_missing` is not in the M1 IR; the M2 builders and validator work adds it. Sorted: chart types by name, backends in the fixed order, properties by key, options in declaration order, object props by wire name. Function members and `check` probes are excluded. ~500 KB. Committed so the sync PR shows a semantic diff, retirement detection diffs IRs, the parity and cross-check tests load it, the docs emitter and `catalog.json` derive from it, and emission never depends on the extractor working today.

#### 7.4 Emitters and type mapping

| Emitter | Output |
|---|---|
| constants | partial files adding generated members to the hand-written structs; `FlintUpstream.Version` |
| options / theming | one `.g.cs` per IR object type, properties in IR order |
| tables | `internal static class CatalogTables` with `FrozenDictionary`s |
| builders / enums / entries | per chart type: channel methods from the channel union across backends; property methods from the property union (binary → `bool`, discrete → enum over the union of option values, continuous → `double`); enum + internal value table per distinct template-and-key discrete pair (46 on 0.5.1); `Chart.<Name>(…)` entries including the error-marked `(string)` overload |
| public-api | reads `PublicAPI.Shipped.txt`, writes only generated symbols not yet shipped to `PublicAPI.Unshipped.txt` (both files start with `#nullable enable`); a rename override of a shipped symbol is refused unless the override also sets `keepObsolete: true`, which emits the old name as `[Obsolete] FLINT0002` delegating to the new one |
| docs | from the IR only: the marked README sections (`chart-catalog`, `semantic-types`, `upstream-version` with the `flint-chart` badge version, the "tracks" sentence and the skill link, `mcp-commands` with the `npx -y flint-chart-mcp@<pin>` snippet from `generator/mcp-pin.json`, `agent-identifiers`); the NuGet badge kind (`v`/`vpre`) and the `--prerelease` install line lie outside every marker and are hand-edited in the release PR (§9.6), the whole `docs/package/AGENTS.md` (README "For AI agents" section verbatim plus the catalog table, headed "generated; edit README.md"), and `src/Graspea.Flint/Generated/catalog.json` (projection of IR `catalog`, packed as `catalog/catalog.json`) |

Type mapping: `string`→`string?`, `number`→`double?`, `boolean`→`bool?`, string-literal union in a generated leaf→`string?` (literals in the doc comment; an enum per literal union in the theme tree would add dozens of public types that change with every theme release), `T[]`→`T[]?`, `[number, number]`→`double[]?`, `Record<string, T>`→`Dictionary<string, T>?` with the closed-generic verbatim-key converter, nested object literal→nested class `<Parent><Prop>`, intersections flattened, `Partial<X>`→`X`, structurally identical `{width,height}`→`ChartSize` via `overrides.json`. Every generated class gets `[JsonNumberHandling(Strict)]` and a trailing `[JsonExtensionData] Dictionary<string, JsonElement>? Extensions`. **Every other construct fails generation** with the IR path (unions, `any`/`unknown`, conditional or mapped types, generics) unless `overrides.json` maps it (`"AssembleOptions.continuousMarkCrossSection": "Graspea.Flint.ContinuousMarkCrossSection"`, or `"JsonNode"` for an explicitly accepted opaque member). A silently shipped `JsonNode?` would be a public-API commitment fixable only in a breaking window; a failed generation costs one reviewed line. `ThemeSpec.compileDefaults` (`Partial<AssembleOptions> & {baseSize?, canvasSize?}`) is accepted as the flattened 29-property class `ThemeCompileDefaults` (the upstream interface name); the duplicate is deliberate (zero hand-written code). The theme tree has 56 distinct object types (53 generated classes on 0.5.1, because three `{width,height}` objects map to `ChartSize`), 27 of them named upstream (`ThemeVariant`, `ThemeGuard`, `NumericGuard`, …); `ThemeVariant.when: ThemeGuard` and any union inside the theme tree are decided by the first extractor run and listed in `docs/generator.md`.

Property kind rule: the same key with the boolean-only kind on every backend → `bool`; the same key with different kinds across backends of one template (binary vs discrete, discrete vs continuous; on 0.5.1 only Radar Chart `filled`) → **generation fails** with both IR paths; `overrides.json` either maps the key to a raw `JsonNode?` setter or splits the names.

Every emitted file starts with `// <auto-generated/> flint-chart 0.5.1`, LF, no timestamps; files in alphabetical order.

#### 7.5 Doc comments and research-derived annotations

From d.ts JSDoc, plus `overrides.json` `docs` entries for facts the d.ts gets wrong: `maxStretch` real default 1.5; theme applied by Vega-Lite and Plotly (`T:3013` says Vega-Lite only; resolved by the probe `probes.ts#themeBackends`: assemble with an unknown preset on each backend, a throw proves the backend reads the theme; the recorded output is cited next to the override); options overwritten by backends; Excel ignoring `options` and `theme_spec`.

#### 7.6 Naming rules (deterministic, total, in `naming.ts`)

1. `Pascal(s)`: split on non-alphanumeric runs and case changes; an all-upper token of length ≥ 2 is capitalised (`KPI`→`Kpi`, `ECDF`→`Ecdf`, `LR`→`Lr`); leading digit gets `_`.
2. Chart type constant `ChartType.{Pascal(name)}`; builder `{Pascal(name)}Builder<TRow>`; entry `Pascal(name)` with one trailing `Chart`/`Plot` removed (`Line`, `Boxplot`, `SankeyDiagram`, `Ecdf`, `RangedDot`).
3. Channel method `Pascal(channel)`; property method `Pascal(key)` (`LogScaleX`, `ShowValueLabels`); enum `{Pascal(chartType)}{Pascal(key)}`. An enum name whose `Pascal(key)` starts with `Pascal(chartType)` repeats the template name (Regression `regressionMethod` → `RegressionRegressionMethod`) and fails generation until `"renames": { "<Template>/enum/<key>": "<NewName>" }` names it; on 0.5.1 that is `Regression/enum/regressionMethod` → `RegressionMethod`.
4. Enum member: from the raw value when it is a string (`step-after`→`StepAfter`, `catmull-rom`→`CatmullRom`, `LR`→`Lr`); from the label when it is not (`[10,50]` "Europe"→`Europe`; `0`→its label, else `N0`); `omitted`→`Default`; a literal value `"default"`→`DefaultLiteral`. On 0.5.1, `overrides.json` renames `minmax`→`MinMax` and the Tree `orient` values `LR`/`TB`→`LeftToRight`/`TopToBottom` (`<Template>/enum/<key>/<value>`).
5. Semantic type constants verbatim; theme presets `Pascal(id)`, except `powerbi`→`PowerBi` and `powerbi-light`→`PowerBiLight` (renamed in `overrides.json` through `catalog.themePresets[<id>]`); `FlintBackend` hand-written.
6. Collision resolution, in order: channel vs property on one template → property gets `Value`; channel or property vs a reserved core name → `Channel`/`Value` suffix; two entries equal after stripping, two enum members equal, or any remaining collision → **generation fails** with the IR paths; the fix is `"renames": { "<Template>/property/<key>": "<NewName>" }` (or `<Template>/channel/<channel>`, `<Template>/enum/<key>`) in `overrides.json` (a hypothetical path; on the 0.5.1 IR no residual collision is expected, §4.2).

Names depend only on the IR.

#### 7.7 Running against another version

```
cd generator
npm ci --ignore-scripts
npm run install-upstream -- 0.6.0      # install exact, npm audit signatures, verify-provenance
npm run generate
cd .. && dotnet build && dotnet test
```

CI drift check (`verify-generated` job, §10.2): `npm run generate -- --check`, which regenerates into a temp dir and diffs `generator/ir`, `src/Graspea.Flint/Generated` (including `catalog.json`), `tests/fixtures`, `src/Graspea.Flint/PublicAPI.Unshipped.txt`, the marked sections of `README.md` (`chart-catalog`, `semantic-types`, `upstream-version`, `mcp-commands`, `agent-identifiers`) and `docs/package/AGENTS.md`; a missing or duplicated marker is an error. The NuGet badge kind and the `--prerelease` install line lie outside the markers and are not covered: they are set in the release PR (§9.6). The check also keeps the `generator/package.json` pin equal to `FlintUpstream.Version`.

#### 7.8 Retirement ledger

`extract` diffs the new IR against the committed one; every vanished chart type, channel, property, option value, semantic type or preset is appended to `retired.json` with the version in which it vanished, keyed by template and backend (all 7 historical removals were per-template; the same key still exists elsewhere in 0.5.1). Emitters keep emitting retired members with `[Obsolete(…, DiagnosticId = "FLINT0001")]`; the validator reports `retired_member`. Entries are purged, and members removed, only in a breaking window (§8.4). Safe because a removed key or channel is silently ignored at runtime (a removed channel is covered by `unsupported_channel`); a removed option value yields upstream `invalid-option-value`, which the Warning mirrors.

#### 7.9 Fixture extraction

`fixtures.ts` maps each `TestCase` as: `data = {values}`; `semantic_types[f] = semanticAnnotations?.[f] ?? metadata[f].semanticType`; `encodings[ch] = {field: fields.find(id === fieldID).name, type: dtype, aggregate, sortOrder, sortBy, scheme}` for entries with a `fieldID`, empty values dropped (on 0.5.1 the `encodingMap` entries carry only `fieldID` and `dtype`, so the other four are mapped when present); `chartProperties`, `options = assembleOptions`; `title`, `description`, `tags`, `metadata.type`, `metadata.levels` dropped. Upstream's own converters are not used (they write an unread `semantic_annotations` key). This mapping assembles without a throw on every non-Excel backend that has the template; Excel throws on 219 of its 695 applicable cases, all recorded as expectations (`excel_unsupported`, §6). Semantic equivalence with upstream's own inputs is unverified. `crosscheck.ts` (one-off, not in CI) compares the mapped fixtures with the 705 `shared/test-data/*/input.json` files at the upstream 0.5.1 git tag by `chartType` and data shape and records the result in `docs/fixtures.md`; `levels` handling is decided from that evidence before the preview tag.

Expectations come from running the five assemblers on each fixture for every backend that has the template: throw/no-throw plus the warning codes (`_warnings` on the four layout backends, the top-level `warnings` array on Excel, same shape).
