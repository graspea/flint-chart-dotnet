# Row data

Flint reads the rows of a chart from `data.values`, a JSON array of objects. This page describes how `ChartData.FromRows` and the fluent entries (`Chart.Line(rows)` and the others) turn .NET rows into that array, and how field names are chosen. The design is in [part 2 §5.3](design/02-sdk-serialisation-validation-codegen.md).

## One name in four places

A field has one wire name. It is the key in every row of `data.values`, the key in `semantic_types` and `field_display_names`, and the `field` of an encoding. flint-chart matches these by exact string comparison and also infers the field's type from the name, so the name the rows are written with is the name every other part of the document must use.

## Choosing the names

| Source | Name |
|---|---|
| A row member, by default (`RowOptions.Default`) | camelCase: `FiscalYear` becomes `fiscalYear` |
| A row member with `new RowOptions { PropertyNamingPolicy = null }` | the member name, verbatim |
| A row member with `[JsonPropertyName("GDP per capita")]` (on a positional record: `[property: JsonPropertyName(...)]`) | the attribute value, under every policy |
| A lambda selector in the fluent API (`r => r.Month`) | the name the rows are written with, resolved from the same metadata |
| A string field name (`X("month")`, `Encodings = { [Channel.X] = "month" }`) | the string, as it is |
| A key of a dictionary row | the key, as it is |

A lambda selector must be a plain member access (`r => r.Month`). Anything else (`r => r.Region.Length`, `r => r.Value + 1`) throws `ArgumentException`, and so does a member that is not serialised (`[JsonIgnore]`).

`ChartData.FirstRowKeys` lists the keys of the first row, which is what flint-chart sees. The validator checks every encoding field against it (`unknown_field`).

## Overloads

| Call | Use |
|---|---|
| `ChartData.FromRows(rows)`, `ChartData.FromRows(rows, rowOptions)` | reflection over the row type; not trimming- or AOT-safe (annotated with `RequiresUnreferencedCode` and `RequiresDynamicCode`) |
| `ChartData.FromRows(rows, jsonTypeInfo)` | a `JsonTypeInfo<TRow>` from a source-generated `JsonSerializerContext`; trimming- and AOT-safe |
| `ChartData.FromJson(jsonArray)` | rows that are already JSON |
| `ChartData.FromUrl(url)` | a URL; flint-chart never fetches it, so the validator reports `data_url_not_fetched` (Info), and `data_url_only` on Excel |

The fluent entries have the same overloads: `Chart.Line(rows)`, `Chart.Line(rows, rowOptions)`, `Chart.Line(rows, jsonTypeInfo)` and `Chart.Line(chartData)`. A `JsonTypeInfo` (or `RowOptions`) that would drop `null` members, through `DefaultIgnoreCondition`, `[JsonIgnore(Condition = WhenWritingNull)]` or a custom `ShouldSerialize` predicate, is rejected with `ArgumentException`: a key missing from the first row breaks field detection upstream. So is metadata that writes numbers as strings (`JsonNumberHandling.WriteAsString`).

Rows are written to JSON when the `ChartData` is created, so later changes to the .NET objects do not affect the document.

## How each .NET type is written

| .NET | JSON | flint-chart infers | Notes |
|---|---|---|---|
| `bool` | `true` / `false` | nominal | |
| `byte` to `long`, `uint` to `ulong` | number | quantitative | beyond 2^53 JavaScript loses precision; Info `large_integer` |
| `float`, `double` | number | quantitative | `NaN` and `±Infinity` are written as `null` |
| `decimal` | number, digits as stored (`1200.50`) | quantitative | JavaScript reads a double |
| `DateOnly` | `"2024-01-15"` | temporal | the recommended type for dates |
| `DateTimeOffset`, `DateTime` (Utc or Local) | ISO 8601 with `Z` or an offset | temporal | an absolute instant |
| `DateTime` (Unspecified) | `"2024-01-15T10:00:00"` | temporal | read in the time zone of the process that runs flint-chart; Info `unspecified_datetime` |
| `TimeOnly` | `"10:00:00"` | nominal | pass a number (for example `TotalSeconds`) for an axis |
| `TimeSpan` | `"1.02:03:04"` | temporal when a day or longer | write `TotalSeconds` as a number instead; Info `timespan_as_string` |
| enum | member name | nominal | a number would be read as quantitative |
| `string` | string | quantitative when numeric, temporal when date-like, otherwise nominal | flint-chart's own inference |
| `Guid`, `Uri` | string | nominal | |
| `null`, a null `Nullable<T>` | `null` | (an all-null column is nominal) | always written; Info `null_in_temporal_field` in a temporal column |
| nested object | JSON object | nominal | Info `nested_value` |
| collection | JSON array | nominal (a single-number array is quantitative) | Info `nested_value` |
| `JsonElement`, `JsonNode` | as given | | |

The table describes the reflection overloads (`RowOptions`). The `JsonTypeInfo` overloads write values exactly as the given metadata does: enums as numbers unless the context sets `UseStringEnumConverter = true`, and a `NaN` or infinite `double` throws unless the row maps it to `null`. For source-generated rows, use `[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]`.

Integer years stay quantitative unless the field is annotated `SemanticType.Year` or `SemanticType.Decade`, or its name contains the token `year` and every value is an integer between 1500 and 2200 with at least two distinct values.

A semantic type (`SemanticType.YearMonth`, `SemanticType.Amount`) tells flint-chart what a field means and usually decides its encoding type better than inference does. Annotate fields through the fluent channel methods (`X(r => r.Month, SemanticType.YearMonth)`), `Annotate`, or `ChartAssemblyInput.SemanticTypes`.
