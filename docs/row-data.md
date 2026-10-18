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

Rows are written to JSON when the `ChartData` is created, so later changes to the .NET objects do not affect the document. Streamed data (`ChartData.Stream`, below) is the exception: its rows are read when the document is written.

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

## Late-bound, asynchronous and streamed rows

The design is in [part 4 §13](design/08-async-and-batched-data.md).

**A template** holds a chart without rows plus the row metadata. Build it once, keep it in a static field and bind rows per request, from any thread. Start the chain with a deferred entry, which takes the metadata instead of rows, and end it with `ToTemplate()`:

```csharp
static readonly ChartTemplate<Sale> Revenue =
    Chart.Line(SalesJson.Default.Sale)              // or Chart.Line<Sale>() by reflection
        .X(r => r.Month, SemanticType.YearMonth)
        .Y(r => r.Revenue, SemanticType.Amount)
        .Color(r => r.Region)
        .ToTemplate();

ChartAssemblyInput a = Revenue.Bind(salesList);                                    // materialised
ChartAssemblyInput b = await Revenue.BindAsync(db.Sales.AsAsyncEnumerable(), ct);  // materialised, asynchronous
await FlintJson.SerializeAsync(stream, Revenue.BindStream(db.Sales.AsAsyncEnumerable()), ct);   // streamed
```

A spec read at run time, such as a JSON file without `data`, becomes a template with `ChartTemplate.Create(FlintJson.Deserialize(json), SalesJson.Default.Sale)`. Its field names must be the keys the metadata writes. `Build()` on a deferred builder returns a document without `data`, which `Validate` reports as `data_missing`. Each bind returns a new document that shares no object with another.

| Member | Rows | Memory | Use |
|---|---|---|---|
| `Bind(rows)`, `ChartData.FromRows` | written to JSON now | the rows as JSON | the default; any serialiser, full validation, the document can be written many times |
| `BindAsync(rows, ct)`, `ChartData.FromRowsAsync` | an `IAsyncEnumerable<T>`, read once before the task completes | the rows as JSON | asynchronous sources |
| `BindStream(rows)`, `ChartData.Stream` | read during the write, never held | bounded: a 16 KiB buffer plus one row | large row sets; single use |

**Batches and pages.** `RowSource.FromBatches(batches)` turns an `IAsyncEnumerable<IReadOnlyList<T>>` into one row sequence and forwards the cancellation token to it. A page loader is a short iterator:

```csharp
static async IAsyncEnumerable<IReadOnlyList<Sale>> Pages(SalesApi api, [EnumeratorCancellation] CancellationToken ct = default)
{
    string? next = null;
    do
    {
        var page = await api.GetSalesAsync(next, ct);
        yield return page.Items;
        next = page.Next;
    }
    while (next is not null);
}

var input = await Revenue.BindAsync(RowSource.FromBatches(Pages(api)), ct);
```

**Streamed data** is written by `FlintJson.SerializeAsync(stream, input, ct)` (or `FlintJson.Serialize(stream, input)` for a synchronous source) with the same bytes as the materialised document. The writer never calls the stream's synchronous `Write` or `Flush` and flushes the stream once, at the end. Rows are written as they are, with no checks: a `null` row is written as `null`, as `FromRows` writes it. Rules of streamed data:

- It is single use. A second write throws `InvalidOperationException`; bind a new source for each write.
- An asynchronous source is written only by `FlintJson.SerializeAsync`. `FlintJson.Serialize` (string, stream or writer), `SerializeToUtf8Bytes`, `SerializeToNode` and `JsonSerializer` with any options throw `InvalidOperationException`. A synchronous source can be written by all of them; the in-memory ones hold the whole document.
- A builder does not take streamed data (`Chart.Line(ChartData.Stream(…))` throws `ArgumentException`); use `ToTemplate().BindStream(rows)`.
- Cancellation, a source exception or a stream exception leaves a truncated document in the stream, which is never valid JSON.

**End a database query with `.AsAsyncEnumerable()`** before binding it. An `IQueryable<T>` is an `IEnumerable<T>`, so `BindStream(db.Sales.Where(…))` binds the synchronous overload. `FlintJson.SerializeAsync` still reads such a source asynchronously when its run-time object implements `IAsyncEnumerable<T>`, as an Entity Framework Core query does; any other `IEnumerable<T>` runs with synchronous I/O inside the asynchronous write, and `FlintJson.Serialize(stream, …)` always enumerates synchronously. A type that implements both interfaces, such as a `DbSet<T>`, binds the asynchronous overload under C# 13 or later; under C# 12 the call is ambiguous (CS0121) until you add `.AsAsyncEnumerable()` or `.AsEnumerable()`.

**ASP.NET Core.** Write a streamed document to the response body directly:

```csharp
app.MapGet("/charts/revenue", async (HttpContext ctx, SalesDb db) =>
{
    ctx.Response.ContentType = "application/json; charset=utf-8";
    await FlintJson.SerializeAsync(ctx.Response.Body, Revenue.BindStream(db.Sales.AsAsyncEnumerable()), ctx.RequestAborted);
    return Results.Empty;
});
```

Returning the document instead (`Results.Json(input)`, `TypedResults.Ok(input)`, an MVC result or `WriteAsJsonAsync`) goes through `JsonSerializer.SerializeAsync` with the host's options. With an asynchronous source it throws `InvalidOperationException`, which the host turns into a 500 response. With a synchronous source it writes the same bytes, but the whole document is built in memory first, so the memory bound is lost.

**Validation without rows.** `ChartValidator.Validate` on streamed data and `template.Validate(backend)` do not read a row. The spec rules run. Fields are checked against the keys the row type writes (`FirstRowKeys` of streamed data lists them; it is empty for dictionary, `JsonNode`, `object` or polymorphic rows, and those checks are skipped). A static-series field the row type does not write is a Warning, because upstream throws only once there are rows. The row-content rules are skipped, and `rows_not_inspected` (Info) says so. Validate a real sample with every rule, in tests or at start-up:

```csharp
ChartValidator.Validate(Revenue.Bind(sampleRows), FlintBackend.VegaLite).ThrowIfInvalid();
```
