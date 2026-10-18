## SDK design, part 4: async, batched, streamed and late-bound data

Status: design, not implemented. Owner decision of 2026-10-18 (index, decision 15) is binding. The high-impact choices are collected in §13.14, each with a recommendation. Section numbers continue the SDK design; part 7 is §12.

### 13. Data that arrives later, in batches or as a stream

#### 13.1 Goals, constraints, starting point

**Today** (§4.2, §5.3): rows enter at the entry point (`Chart.Line(rows)`, `ChartData.FromRows`) and are written to a `JsonArray` at once. The builder holds that `ChartData`, `Build()` copies it per document, and `ToJson()` serialises it. Memory is about twice the rows. §5.3 names "a lazy `IEnumerable<TRow>` source written straight to the `Utf8JsonWriter`" as an additive upgrade, and this part designs it together with the rest of decision 15.

**Goals**

1. Build a chart without rows and bind rows later: a spec built once and reused with different row sources, from any thread, including a spec read from a JSON file (part 7) or written by an agent.
2. Accept `IAsyncEnumerable<TRow>` and batched or paged sources, with cancellation.
3. Stream rows straight to the output with memory that does not grow with the row count, without materialising the rows twice.
4. Define what `FirstRowKeys` and `ChartValidator` mean when the rows have not been seen.
5. Keep every row path trimming- and AOT-safe through `JsonTypeInfo<TRow>`.
6. Give the generated classes of part 7 `BuildAsync` and a streamed write.

**Constraints**: zero runtime dependencies and `net8.0`; the §5.3 row contract (key naming, nulls always written, numbers as numbers) and the host-options guarantee (§5.1) unchanged; `Build()`/`ToJson()` never validate; a streamed document is byte-identical to the same document with materialised rows; existing signatures keep working, except the one change in §13.3.1, which must land before the stable tag (§13.12).

#### 13.2 Data states

| State | Created by | `Values` | `Url` | `IsStreamed` | `FirstRowKeys` | Serialised by | Reusable |
|---|---|---|---|---|---|---|---|
| materialised | `FromRows`, `FromJson`, `FromRowsAsync` (new), `ChartTemplate.Bind`/`BindAsync` (new) | `JsonArray` | null | false | the first row (unchanged) | every API, any number of times | yes |
| url | `FromUrl` | null | the URL | false | empty | every API | yes |
| streamed (new) | `ChartData.Stream`, `ChartTemplate.BindStream` | null | null | true | the keys every row will have, from the row metadata, when the row shape is static (§13.6), even if the source turns out to yield no row; empty otherwise | once; an asynchronous source only through `FlintJson.SerializeAsync` | no |
| absent (new) | `ChartAssemblyInput.Data == null`: a deferred builder's `Build()`, a spec file without `data` | — | — | — | — | every API; no `data` key is written | yes |

#### 13.3 Late-bound data

##### 13.3.1 `Data` becomes optional

```csharp
public sealed partial class ChartAssemblyInput
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChartData? Data { get; set; }          // was: required ChartData, JsonIgnore Never
    …
}
```

- A document without rows can now be represented, written and read. That is the shape an agent drafts before data exists, and the shape a part 7 spec file usually has. `FlintJson.Deserialize` accepts a document without `data`. `"data": null` reads as null and is written back as absent; omitted and null are one state in C#, as everywhere else in the spec part (§5.2).
- The validator is unchanged: `data_missing` (Error) fires when `Data` is null. Upstream throws on `data: null` on all five backends (§2). That it also throws on an *absent* `data` is expected from the source (`input.data.values` on `undefined`) but **UNVERIFIED**. A probe case `data absent` is added to `probe-cases.jsonl` before implementation, and the rule's `// source:` comment cites it.
- Consumer code that dereferences `input.Data` gets a nullable warning (CS8602). That is a change of a hand-written signature, allowed before Stable (§8.2). D8.1 lists the alternatives.

##### 13.3.2 Deferred builders

Two more generated entries per template, and two more `Chart.Of` overloads, create a builder that has row metadata but no rows:

```csharp
public static partial class Chart
{
    // generated, per template (96 more entries on the 0.5.1 IR)
    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage), RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    public static LineChartBuilder<TRow> Line<TRow>(RowOptions? options = null);
    public static LineChartBuilder<TRow> Line<TRow>(JsonTypeInfo<TRow> typeInfo);

    // hand-written
    [RequiresUnreferencedCode(…), RequiresDynamicCode(…)]
    public static GenericChartBuilder<TRow> Of<TRow>(ChartType chartType, RowOptions? options = null);
    public static GenericChartBuilder<TRow> Of<TRow>(ChartType chartType, JsonTypeInfo<TRow> typeInfo);
}
```

Overload resolution, to be pinned by compile probes (like the §4.2 `Spike.Ambiguity`): `Chart.Line<Sale>()` binds to the deferred `RowOptions` entry. `Chart.Line(SalesJson.Default.Sale)` binds to the deferred `JsonTypeInfo` entry and infers `TRow`. `Chart.Line(rows)`, `Chart.Line(rows, typeInfo)` and `Chart.Line(data)` bind as today, because a `List<Sale>` is neither a `RowOptions` nor a `JsonTypeInfo<Sale>`. A literal `null` stays ambiguous (CS0121), as documented in §4.2.

A deferred builder behaves like any other. Selectors resolve through the captured metadata. `Build()` returns a document with `Data == null`. `ToJson()` writes it without `data`. `Validate(backend)` validates that document and so reports `data_missing`. Validating a spec as a template is `ToTemplate().Validate(backend)` (§13.6).

##### 13.3.3 Templates

```csharp
namespace Graspea.Flint;

public abstract class ChartBuilder<TSelf, TRow> …
{
    public ChartTemplate<TRow> ToTemplate();          // new reserved core name; the builder's own data, if any, is not carried
}

public sealed class ChartTemplate<TRow>              // immutable, thread-safe; internal constructor
{
    public ChartAssemblyInput Bind(IEnumerable<TRow> rows);                                    // materialised now
    public ChartAssemblyInput Bind(ChartData data);                                            // any state: values, url, streamed
    public ValueTask<ChartAssemblyInput> BindAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken = default);   // materialised
    public ChartAssemblyInput BindStream(IEnumerable<TRow> rows);                              // streamed, single use (§13.5)
    [OverloadResolutionPriority(1)]
    public ChartAssemblyInput BindStream(IAsyncEnumerable<TRow> rows);                         // streamed, single use
    public ValidationResult Validate(FlintBackend? backend = null);                            // spec + row metadata (§13.6)
}

public static class ChartTemplate
{
    public static ChartTemplate<TRow> Create<TRow>(ChartAssemblyInput spec, JsonTypeInfo<TRow> typeInfo);
    [RequiresUnreferencedCode(…), RequiresDynamicCode(…)]
    public static ChartTemplate<TRow> Create<TRow>(ChartAssemblyInput spec, RowOptions? options = null);
}
```

- **What a template holds**: the document without `data`, serialised once to UTF-8 with `FlintJsonContext` at `ToTemplate()`/`Create`. `Create` ignores `spec.Data` (any state, including streamed data, which it does not claim) and does not mutate `spec`; its test asserts both. A sample is validated with the public members, `ChartValidator.Validate(template.Bind(sampleRows), backend)`, which `docs/row-data.md` shows; there is no `ValidateSample` member (D8.7).
- **Metadata**: the `JsonTypeInfo<TRow>` captured by the entry point (or resolved from `RowOptions` once). `Options` and `Configure` actions run once, when the template is created, not on every bind. Later changes to the builder or to the `spec` object do not reach the template.
- **Bind** reads the stored bytes into a new document (so documents never share an object, the rule `Build()` already follows) and sets `Data`. Its cost is proportional to the spec's size plus the row writing. `Bind(IEnumerable<TRow>)`, `BindAsync` and `BindStream` throw `InvalidOperationException` when the template has no row metadata (a template from `Chart.Line(ChartData)`, whose `TRow` is `object`), with the message the selectors already use. `Bind(ChartData)` always works.
- **Reuse**: a template is the unit to store in a static field and to share across requests and threads.
- **From a spec file at run time**, without the generator: `ChartTemplate.Create(FlintJson.Deserialize(json), SalesJson.Default.Sale)`. The spec's field names must equal the keys the metadata writes; `template.Validate()` reports `unknown_field` from those keys (§13.6).

```csharp
static readonly ChartTemplate<Sale> Revenue =
    Chart.Line(SalesJson.Default.Sale)
        .X(r => r.Month, SemanticType.YearMonth)
        .Y(r => r.Revenue, SemanticType.Amount)
        .Color(r => r.Region)
        .Title("Revenue by region")
        .ToTemplate();

ChartAssemblyInput a = Revenue.Bind(salesList);                                          // materialised
ChartAssemblyInput b = await Revenue.BindAsync(db.Sales.AsAsyncEnumerable(), ct);        // materialised, asynchronous
await FlintJson.SerializeAsync(response.Body, Revenue.BindStream(db.Sales.AsAsyncEnumerable()), ct);   // streamed
```

#### 13.4 Asynchronous and batched sources

```csharp
public sealed partial class ChartData
{
    public static ValueTask<ChartData> FromRowsAsync<TRow>(IAsyncEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo,
        CancellationToken cancellationToken = default);
    [RequiresUnreferencedCode(…), RequiresDynamicCode(…)]
    public static ValueTask<ChartData> FromRowsAsync<TRow>(IAsyncEnumerable<TRow> rows, RowOptions? options = null,
        CancellationToken cancellationToken = default);
}

public static class RowSource
{
    public static IAsyncEnumerable<TRow> FromBatches<TRow>(IAsyncEnumerable<IReadOnlyList<TRow>> batches);
}
```

- `FromRowsAsync` is `FromRows` over an asynchronous source. It applies the same metadata checks before the first row, the same per-row check, and gives the same `FirstRowKeys` and the same JSON. The rows are enumerated once, before the task completes. As with `JsonSerializer.SerializeAsync(stream, value, options, cancellationToken)`, a token after the rows needs a named argument (`FromRowsAsync(rows, cancellationToken: ct)`); `ChartTemplate.BindAsync(rows, ct)` has no such gap and is the primary asynchronous path.
- `RowSource.FromBatches` flattens batches in order (empty batches allowed) and forwards the consumer's cancellation token to the batch source (`[EnumeratorCancellation]`). It returns an `IAsyncEnumerable<TRow>`, so it composes with every API above without multiplying overloads. A page loader becomes a batch source with a short iterator, which the docs show. A dedicated page-loader adapter (cursor type, end condition) is D8.5:

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

#### 13.5 Streaming

```csharp
public sealed partial class ChartData
{
    public static ChartData Stream<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo);
    [OverloadResolutionPriority(1)]
    public static ChartData Stream<TRow>(IAsyncEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo);
    [RequiresUnreferencedCode(…), RequiresDynamicCode(…)]
    public static ChartData Stream<TRow>(IEnumerable<TRow> rows, RowOptions? options = null);
    [RequiresUnreferencedCode(…), RequiresDynamicCode(…), OverloadResolutionPriority(1)]
    public static ChartData Stream<TRow>(IAsyncEnumerable<TRow> rows, RowOptions? options = null);
    public bool IsStreamed { get; }
}

public static class FlintJson
{
    public static Task SerializeAsync(Stream utf8Json, ChartAssemblyInput input, CancellationToken cancellationToken = default);
    public static void Serialize(Stream utf8Json, ChartAssemblyInput input);
    // existing members unchanged
}
```

**Creating** a streamed `ChartData` checks the row metadata (the §5.3 refusals: `DefaultIgnoreCondition` not `Never`, conditional members, `WriteAsString`) and computes the static key list (§13.6). It does not touch the source. `Values` and `Url` are null; `Extensions` may be set as for any `ChartData`.

**Writing** (`SerializeAsync`, `Serialize(Stream)`):
1. A document whose data is not streamed is written by `JsonSerializer.SerializeAsync(utf8Json, input, FlintJsonContext.Default.ChartAssemblyInput, cancellationToken)` (or its synchronous form), exactly as `Serialize` writes it today.
2. A streamed document is claimed atomically. A second or concurrent write throws `InvalidOperationException` ("the streamed rows were already written").
3. **The remainder.** The document without `data` is serialised once with `FlintJsonContext` to UTF-8 bytes. It is an object, `{}` when no other member is set.
4. **The buffer.** The library owns a pooled `IBufferWriter<byte>` (an `ArrayPool<byte>`-backed buffer, returned at the end) and a `Utf8JsonWriter` over that buffer, never over the stream, with the `JsonWriterOptions` of the non-indented `FlintJson.Serialize` (default encoder, not indented), so the bytes equal the materialised output. Into the buffer go, in order: the raw bytes `{"data":` (written to the buffer directly); then, through the writer, the data object `{"values":[`, every row, `]`, the data `Extensions` (`WritePropertyName` and `JsonElement.WriteTo`, after the known members, as `ChartDataConverter` writes them) and `}`; then, after `writer.Flush()`, the raw tail: `}` when the remainder is `{}`, otherwise `,` followed by the remainder bytes without their leading `{`. Members keep their canonical order: `data` comes first, as `FlintJson.Serialize` writes it. The writer only ever writes one complete JSON value (the data object), so its state and comma tracking are never bypassed; the raw bytes go around it, into the buffer, at points where the writer is flushed and finished.
5. **Rows** are written through the captured `JsonTypeInfo<TRow>`. When no serialised member has a `ShouldSerialize` predicate, a row is written with `JsonSerializer.Serialize(writer, row, typeInfo)`. That call always ends with `writer.Flush()` (probe on SDK 10: three rows into a writer over a counting stream gave six synchronous `Stream.Write` calls, three `Stream.Flush` calls, and `BytesPending == 0` after each row), which is why the writer targets the buffer: the flush only advances the in-memory buffer. Otherwise a row goes through `SerializeToNode` and the §5.3 per-row member check, then `WriteTo`, as `FromRows` does today.
6. **Draining.** After each row, when the buffer holds 16 KiB or more, its written bytes go to the stream (`await utf8Json.WriteAsync(buffer.WrittenMemory, cancellationToken)`, or `Write` in `Serialize(Stream)`), the buffer is reset and `writer.Reset(buffer)` is called. At the end the rest of the buffer is written and the stream is flushed once (`FlushAsync` or `Flush`). `Utf8JsonWriter.FlushAsync` is never used: it calls `Stream.FlushAsync` on every chunk (probe: one stream flush per call), which costs compression ratio on `GZipStream` and `BrotliStream` response bodies.
7. `SerializeAsync` never calls `Stream.Write` or `Stream.Flush`. ASP.NET Core response bodies have `AllowSynchronousIO = false` and throw `InvalidOperationException` on synchronous I/O, which is the main use case (`SerializeAsync(response.Body, template.BindStream(rows))`). A test writes to a stream that throws on every synchronous `Write` and `Flush`, as Kestrel's does (§13.13).
8. Every `await` in the library uses `ConfigureAwait(false)`, so a caller that blocks on the task in a single-threaded synchronisation context does not deadlock.
9. The output is byte-identical to `FlintJson.Serialize` of the same document with the rows materialised by `FromRows` with the same metadata. This is a contract test (§13.13).

**The caller's writer** (`FlintJson.Serialize(Utf8JsonWriter, …)`, synchronous sources only) writes into the caller's writer, so the caller's options (indentation, encoder) apply: `data` is written as above through the caller's writer, and the remainder is parsed once into a `JsonDocument`, whose properties are written with `JsonProperty.WriteTo` in order. Rows written with `JsonSerializer.Serialize(writer, row, typeInfo)` flush the caller's writer after each row (the STJ behaviour of step 5); the caller owns the writer's target and its buffering.

**Other serialisation paths** with streamed data:

| Path | Synchronous source | Asynchronous source |
|---|---|---|
| `FlintJson.SerializeAsync(Stream, …)` | streamed, bounded | streamed, bounded |
| `FlintJson.Serialize(Stream, …)` | streamed through the library's buffer, synchronous writes | `InvalidOperationException` naming `SerializeAsync` (no sync-over-async) |
| `FlintJson.Serialize(Utf8JsonWriter, …)` | streamed into the caller's writer, which STJ flushes after each row | `InvalidOperationException` naming `SerializeAsync` |
| `FlintJson.Serialize` (string), `SerializeToUtf8Bytes`, `SerializeToNode` | written into the result (the caller asked for the whole document in memory) | `InvalidOperationException` |
| a host's `JsonSerializer.Serialize(input, hostOptions)` (the §5.1 guarantee) | written by `ChartDataConverter`; same bytes; memory is the host's buffer | `InvalidOperationException` from the converter: converters are synchronous |
| host asynchronous serialisation: an ASP.NET Core endpoint returning the document (`Results.Json`, `TypedResults.Ok`, MVC's `SystemTextJsonOutputFormatter`, `WriteAsJsonAsync`), all of which call `JsonSerializer.SerializeAsync` with the host's options | written by `ChartDataConverter`, but **fully buffered**: STJ cannot suspend a custom converter, so the whole document is built in memory before the first byte is sent and streaming is silently lost | `InvalidOperationException` from the converter, which the host turns into a 500 response at run time |
| builder entry `Chart.Line(ChartData)` | `ArgumentException`: a builder's documents are reusable and streamed data is not; use `ToTemplate().BindStream(…)` | same |

**Hosting.** The documented pattern for a streamed response is to write it directly: set `ctx.Response.ContentType = "application/json; charset=utf-8"` and `await FlintJson.SerializeAsync(ctx.Response.Body, template.BindStream(rows), ctx.RequestAborted)`, then return `Results.Empty`. `docs/row-data.md` shows it next to the two failure modes above. An `IResult` that wraps this belongs in `Graspea.Flint.AspNetCore` (part 1 §3) and is out of scope here.

**`IQueryable` sources.** `template.BindStream(db.Sales.Where(…))` binds the `IEnumerable` overload, because the static type `IQueryable<Sale>` implements `IEnumerable<Sale>` but not `IAsyncEnumerable<Sale>`; `FlintJson.SerializeAsync` therefore reads a synchronous source asynchronously when its run-time object also implements `IAsyncEnumerable<T>` (an Entity Framework Core query does), and only a source that does not runs with synchronous I/O inside the asynchronous write. The rule, in the XML docs and `docs/row-data.md`: end a query with `.AsAsyncEnumerable()` before binding it.

`System.Text.Json`'s own resumable serialisation of `IAsyncEnumerable<T>` was considered (D8.3). It works only with the built-in converters, so `ChartData` would lose the converter that owns its wire shape and the host-options guarantee, and every row type would need a closed generic DTO registered in a source-generated context.

**`OverloadResolutionPriority`**: a source type that implements both `IEnumerable<T>` and `IAsyncEnumerable<T>` (Entity Framework Core's `DbSet<T>` does) would make every overload pair ambiguous. The library declares `System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute` internally (net8 does not have it), and the compiler matches it by name. Applying it needs C# 13 on the declaring side: compiled under C# 12 (the `net8.0` default) the overload pair is error CS9202 ("Feature 'overload resolution priority' is not available in C# 12.0", Roslyn 4.14). So `src/Graspea.Flint/Graspea.Flint.csproj` sets `<LangVersion>13</LangVersion>` (part 7, §12.2.1); the library uses no C# 13 feature that needs runtime support net8 lacks. Spike, Roslyn 4.14, a type implementing both interfaces: C# 13 picks the prioritised `IAsyncEnumerable` overload, and C# 12 reports CS0121. Consumers on net9 or later (default C# 13 or later) get the asynchronous overload. A net8 consumer on the default C# 12 writes `.AsAsyncEnumerable()` or `.AsEnumerable()`, which the XML docs say. D8.6 lists the alternative of distinct method names.

#### 13.6 `FirstRowKeys` and validation of rows not yet seen

**Static row shape.** The null guarantee of §5.3 means every row of a given object type writes every serialised member. So when the metadata fixes the members, the keys of every row, and therefore of `data[0]`, are known without enumerating anything. The shape is static when `JsonTypeInfo.Kind` is `Object`, `TRow` is not `object`, no polymorphism options are set, no member is `[JsonExtensionData]`, and no type-level converter is set (which would make the kind `None`). The keys are the names of the members with a getter, in metadata order, rearranged by the `Object.keys` rule that `FirstRowKeys` already applies (array-index keys ascending, then the rest in insertion order). A dictionary, `JsonNode`, `JsonElement` or polymorphic row has a dynamic shape, and its streamed `FirstRowKeys` is empty.

This changes what the public property means for streamed data, so its XML doc changes with it (part 1 §4.1): "The keys `Object.keys(data[0])` sees. For materialised data, the keys of the first row, empty when there are no rows. For streamed data with a static row shape, the keys every row will have, taken from the row metadata; the source is not read, so `data[0]` may not exist. Empty for url data and for streamed data with a dynamic shape." 

**Validation without the rows** (`ChartValidator.Validate` on a streamed document, and `ChartTemplate.Validate`; a builder never holds streamed data):

| Rule family | Streamed, static shape | Streamed, dynamic shape | Template (`ChartTemplate.Validate`) |
|---|---|---|---|
| spec rules (chart type, channels, options, presets, semantic types, static-series structure, Excel facets) | run | run | run |
| key rules (`unknown_field`, `series_field_missing`, `reserved_semantic_type` from row keys) | run on the metadata keys; `series_field_missing` is reported as a Warning ("an Error once rows exist") | skipped | run on the metadata keys, with the same `series_field_missing` Warning (without metadata: skipped) |
| `data_missing` | n/a | n/a | not reported: binding supplies the data |
| row-content rules (`row_not_object`, `inconsistent_row_keys`, `too_many_rows`, `data_empty` on Excel, `null_in_temporal_field`, `large_integer`, `unspecified_datetime`, `timespan_as_string`, `nested_value`, data-inferred field types used by the Excel rules and `series_nominal_entry`) | skipped | skipped | skipped |
| new Info `rows_not_inspected` (path `data`) naming the skipped families | reported | reported | reported |

`series_field_missing` is an Error because upstream throws, but only when `data` has rows. A streamed source may yield none, and a template has none yet, so reporting an Error there would break the §6 rule that an Error means upstream throws; the Warning names the condition instead. A test validates a streamed document over an empty asynchronous source with a static-series field missing from the metadata: one `series_field_missing` Warning, `FirstRowKeys` equal to the metadata keys, and an output of `"values":[]`.

Consequence: on a streamed or template validation, `IsValid` means "no Error that can be decided without the rows", and the §8.7 statement `IsValid ⇒ every backend that has the template assembles` holds only for materialised data. `ChartValidator.Validate(template.Bind(firstPage), backend)` validates a real sample with every rule. The docs recommend it in tests and at start-up.

**Checks while streaming** (D8.8, recommended (a)). The writer writes every row as the materialised path would, and checks nothing about row content: `Build()`/`ToJson()` never validate (decided 2026-10-17), and `FlintJson` inherits that rule, and a streamed document is byte-identical to the materialised one (§13.1). So a null row (reference `TRow`) is written as `null`, as `FromRows` writes it; the validator reports it as `row_not_object` when a sample is validated (`ChartValidator.Validate(template.Bind(sample), backend)`). The one throw during the write is the one `FromRows` also has: a member omitted by a value-dependent predicate throws `ArgumentException` at the offending row (§5.3), because the row contract cannot be met; the materialised path throws at the same row, so byte identity is not at stake.

#### 13.7 Fluent and generated entry points

- Synchronous rows: unchanged (`Chart.Line(rows)…Build()`), or a template's `Bind`.
- Asynchronous rows: `Chart.Line(typeInfo)…ToTemplate()`, then `BindAsync` (materialised) or `BindStream` plus `FlintJson.SerializeAsync` (streamed). There are no asynchronous entry points per template (`Chart.LineAsync`). They would add 96 more generated members, and an awaited builder reads badly in a chain (`(await Chart.LineAsync(rows, ct)).X(…)`) (D8.4).
- Model level: `ChartData.FromRowsAsync`, `ChartData.Stream`, `ChartTemplate.Create`.
- Generated classes (part 7, §12.6.3): `Template`, `Build(rows)` = `Template.Bind`, `BuildAsync(rows, ct)` = `Template.BindAsync`, `WriteAsync(stream, rows, ct)` = `FlintJson.SerializeAsync(stream, Template.BindStream(rows), ct)`, `Validate(backend)` = `Template.Validate`. The tool's `--data parameter` output (§12.5.2) has the same members.

#### 13.8 Trimming and AOT

- Every new member that writes rows has a `JsonTypeInfo<TRow>` form without annotations. The `RowOptions` forms (`FromRowsAsync`, `Stream`, `ChartTemplate.Create`, the deferred entries) carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` with `RowMaterializer.ReflectionMessage`.
- `ChartTemplate<TRow>` has no annotations: its metadata is resolved when it is created, so `Bind`, `BindAsync` and `BindStream` are AOT-safe whichever way the template was created. The reflection cost is paid, and annotated, at creation.
- The template's stored spec, the streamed remainder and the async writer use `FlintJsonContext` and no reflection.
- `samples/Graspea.Flint.Samples.Aot` gains a deferred entry with `JsonTypeInfo`, `ToTemplate`, `BindAsync`, `RowSource.FromBatches` and a streamed `SerializeAsync`. The existing AOT publish step fails on any trimming or AOT warning.
- The internal `OverloadResolutionPriorityAttribute` is compile-time only.

#### 13.9 Thread-safety and enumeration

| Object | Thread-safety | Enumeration of the source |
|---|---|---|
| `ChartTemplate<TRow>` | immutable; any number of concurrent `Bind*` and `Validate` calls | none of its own |
| builders | mutable, not thread-safe (unchanged) | entry rows once, at the entry (unchanged) |
| materialised `ChartData` | not synchronised (a `JsonArray`); do not change it while another thread serialises it; share templates, not documents | once, at creation (`FromRowsAsync`: before its task completes) |
| streamed `ChartData` | single use: the first write claims it atomically; a second or concurrent write throws `InvalidOperationException` | exactly once, during the write; never at creation |
| `RowSource.FromBatches` result | each enumeration is independent | each enumeration enumerates the batch source again; nothing is cached |

#### 13.10 Errors and cancellation

- **Cancellation**: the token is passed to the source (`WithCancellation`, which reaches `[EnumeratorCancellation]` parameters) and to every `Stream.WriteAsync`, and checked before each row. Every library `await` uses `ConfigureAwait(false)`. Cancellation throws `OperationCanceledException`, and the enumerator is disposed (`await using`). `FromRowsAsync` and `BindAsync` return nothing. A streamed write leaves a truncated document in the stream with its closing brackets missing, so it is never valid JSON. Callers that must not send partial output buffer it or abort the response.
- **Source exceptions** propagate unchanged, not wrapped.
- **Metadata errors** (§5.3): `ArgumentException` when `FromRows*`, `Stream`, `ChartTemplate.Create` or a deferred entry is called, before any row is read. Value-dependent omission: `ArgumentException` at the offending row, during materialisation or the write.
- **Null rows**: written as `null` by every path, materialised and streamed (unchanged; the validator reports `row_not_object` on a materialised document or a bound sample, §13.6).
- **Stream errors** (`IOException`, a closed stream) propagate. The streamed data stays claimed and cannot be retried; the caller binds a new source.

#### 13.11 Memory and performance targets

| Path | Managed memory beyond the caller's own | Target and check |
|---|---|---|
| materialised (`FromRows`, `FromRowsAsync`, `Bind`, `BindAsync`) | the rows as a `JsonArray` (about twice the row objects, §5.3) plus the output | unchanged |
| streamed (`BindStream` or `Stream`, then `SerializeAsync` or `Serialize(Stream)`) | the pooled buffer (16 KiB plus at most one row), one row (and its `JsonNode` on the checked path) and the serialised spec remainder, independent of the row count | test (`Category=Memory`): 1 000 000 rows from an async iterator to a counting stream; `GC.GetTotalMemory(true)` sampled every 100 000 rows stays within the first sample plus 1 MiB |
| streamed throughput | at most 2x the time of `JsonSerializer.SerializeAsync(stream, rows, typeInfo)` for the same rows, where `typeInfo` is a `JsonTypeInfo<IAsyncEnumerable<TRow>>` registered in the test project's `JsonSerializerContext` (`[JsonSerializable(typeof(IAsyncEnumerable<Sale>))]`) | measured by a non-gating script (release build, `Stopwatch`), result recorded in the implementing pull request; the direct-write path of §13.5 step 5 is the lever |
| `Bind` | work proportional to the spec size plus the row writing; no second enumeration | unit test with a counting source |

#### 13.12 Public API changes and compatibility

- **Changed**: `ChartAssemblyInput.Data` from `required ChartData` (`JsonIgnore Never`) to `ChartData?` (`WhenWritingNull`). `FlintJson.Deserialize` accepts a document without `data`. This is allowed before Stable (§8.2) and must land before the stable tag. A `CompatibilitySuppressions.xml` entry is added if package validation flags it against the preview baseline (§8.3).
- **Added** (`ChartTemplate<TRow>` has six members, D8.7): `ChartData.FromRowsAsync` (2), `ChartData.Stream` (4), `ChartData.IsStreamed`; `FlintJson.SerializeAsync`, `FlintJson.Serialize(Stream, …)`; `ChartTemplate<TRow>` and `ChartTemplate.Create` (2); `RowSource.FromBatches`; `ChartBuilder.ToTemplate`; per template two deferred entries (96 on 0.5.1, written by the entries emitter) and `Chart.Of` (2); the Info code `rows_not_inspected`. `ToTemplate` joins the reserved core names of §4.2 (no template has a channel or property of that name; the facade sweep checks it).
- **Build settings**: `<LangVersion>13</LangVersion>` in `src/Graspea.Flint/Graspea.Flint.csproj`, required to apply `OverloadResolutionPriority` (§13.5); the tool, generator and test projects use the same value (part 7, §12.2.1). Consumers need nothing above their default.
- **Changed documentation**: the XML doc of `ChartData.FirstRowKeys` covers streamed data (§13.6).
- **Unchanged**: the row contract and its refusals, the host-options guarantee, canonical output (a streamed write is byte-identical), `Build()`/`ToJson()` never validating, and every existing entry point and overload.
- **Docs**: `docs/row-data.md` gains "Late-bound, asynchronous and streamed rows", with validating a sample through `ChartValidator.Validate(template.Bind(sample), backend)`, the ASP.NET Core pattern and its two failure modes (§13.5, "Hosting"), and the `.AsAsyncEnumerable()` rule for `IQueryable` sources; `docs/validation-codes.md` gains `rows_not_inspected` and the Warning form of `series_field_missing` on streamed and template validation; the README "For AI agents" rules gain "build a template once, bind rows per request; stream large row sets with `BindStream` and `FlintJson.SerializeAsync`".

#### 13.13 Tests, in TDD order

1. `ChartAssemblyInputTests`: a document without data is written without `data`; one without `data` reads with `Data == null`; `data_missing` for an absent `data` (`// source: probe "data absent"`, added first).
2. Compile probes for the deferred entries (`Chart.Line<Sale>()`, `Chart.Line(SalesJson.Default.Sale)`; `Chart.Line(rows)` and `Chart.Line(rows, typeInfo)` unchanged; `Chart.Line(rows, null)` still CS0121) and the facade sweep extended with the two shapes.
3. `ChartTemplateTests`: `Bind(rows)` is `DeepEquals` to the equivalent immediate chain; `Configure` and `Options` run once; eight threads binding concurrently get equal documents; documents from two binds share no object; `Bind` on a template without metadata throws; `Create` ignores `spec.Data` (materialised, url and streamed, the streamed one left unclaimed) and leaves `spec` unchanged.
4. `ChartDataTests.FromRowsAsync_*`: the §5.3 test cases of `FromRows` repeated over an async source (keys, nulls, refusals), plus cancellation in the middle of the sequence.
5. `FlintJsonTests.SerializeAsync_Streamed_*`: byte-identical to the materialised document for sync and async sources, with data `Extensions`, with an empty remainder (`{}`) and with every top-level member, and across the 16 KiB drain boundary; a null row is written as `null`, byte-identical to the materialised output; second write throws; an async source through each synchronous path throws; `SerializeAsync` to a stream whose synchronous `Write` and `Flush` throw (as Kestrel's response body does) succeeds and calls `FlushAsync` exactly once; `Serialize(Utf8JsonWriter)` with an indented caller writer keeps the caller's formatting; the host-options path with `JsonSerializer.Serialize` and with `JsonSerializer.SerializeAsync` (asynchronous source: `InvalidOperationException`; synchronous source: same bytes, fully buffered).
6. `ChartValidatorTests.Streamed_*`: metadata keys drive `unknown_field` and `series_field_missing` (a Warning); a dynamic shape skips them; `rows_not_inspected` is reported; `template.Validate` has no `data_missing`; an empty asynchronous source gives the metadata keys as `FirstRowKeys` and a `series_field_missing` Warning, not an Error.
7. `RowSourceTests.FromBatches_*`: order, empty batches, the token reaching the batch source.
8. Overload probes for a type implementing both interfaces: the async overload under C# 13, CS0121 under C# 12 (pins the documented behaviour). A declaring-side probe compiles the overload pair, with the internal attribute, at the library's pinned `LangVersion` (13) without diagnostics and at C# 12 with CS9202, so the `LangVersion` setting cannot be dropped unnoticed.
9. The memory test of §13.11 and the AOT sample of §13.8.

#### 13.14 High-impact decision points for the owner

| # | Decision | Options | Recommendation | Trade-offs |
|---|---|---|---|---|
| D8.1 | How a spec without rows is represented | (a) `ChartAssemblyInput.Data` optional, plus `ChartTemplate<TRow>`; (b) keep `Data` required and add a `ChartData.Deferred` sentinel; (c) templates only, model unchanged | **(a)** | (a) changes one hand-written signature before Stable (CS8602 for code that dereferences `Data`) but models what agents and spec files produce, and lets `FlintJson` read them. (b) keeps the signature but adds a fourth `ChartData` state that every API has to reject or special-case, and a sentinel can still reach the wire. (c) leaves documents without `data` unreadable, which part 7's runtime path and agents need |
| D8.2 | Default for asynchronous sources | (a) materialise by default (`FromRowsAsync`, `BindAsync`), stream only through explicitly named members (`Stream`, `BindStream`); (b) stream by default; (c) streaming only | **(a)** | materialised data keeps every guarantee (exact `FirstRowKeys`, full validation, reusable documents, any serialiser) at the known 2x memory; streaming is single use, validates only what the metadata decides, and needs `SerializeAsync`. Making the bounded but weaker path explicit avoids surprises; (b) and (c) would make `Validate` weaker by default |
| D8.3 | Streaming mechanism | (a) a streamed `ChartData` state plus `FlintJson.SerializeAsync(Stream)` and `Serialize(Stream)`; (b) a template-only `WriteAsync(Stream, rows)`, model unchanged; (c) `System.Text.Json`'s built-in `IAsyncEnumerable` serialisation | **(a)** | (a) works at the model level and from templates and keeps one writer; (b) is smaller, but streaming would then be impossible for documents built with the model or read from JSON; (c) drops the `ChartData` converter and so the host-options guarantee, and needs a DTO per row type |
| D8.4 | Fluent entry for late-bound and asynchronous rows | (a) deferred entries `Chart.Line<TRow>()` / `Chart.Line(typeInfo)` plus `ToTemplate()`; (b) asynchronous entries `Chart.LineAsync(rows, …)`; (c) `BuildAsync(rows)` on the builder core | **(a)** | (a) adds 96 generated entries but one concept (a template) serves reuse, async, streaming and the generator; (b) adds as many entries, gives awkward chains and no reuse; (c) mixes rows-at-entry and rows-at-build in one mutable, non-thread-safe object |
| D8.5 | Batch and page loaders | (a) no adapter, a documented iterator; (b) `RowSource.FromBatches` only, page loaders through a documented iterator; (c) (b) plus `RowSource.FromPages` with a cursor type (`RowPage<TRow, TCursor>`); (d) batch overloads on every API | **(b)** | (b) costs one method; a cursor abstraction that fits offset, keyset and continuation-token paging is a type whose shape freezes at Stable for a six-line iterator (YAGNI); (c) is additive later; (d) multiplies overloads on frozen types |
| D8.6 | `IEnumerable` and `IAsyncEnumerable` overload pairs | (a) overload pairs with an internal `OverloadResolutionPriority` on the async one (C# 13+ resolves; C# 12 needs `.AsAsyncEnumerable()`); (b) asynchronous members take only `IAsyncEnumerable`, synchronous ones only `IEnumerable` (no `Stream`/`BindStream` for synchronous sources); (c) distinct names (`StreamRows`/`StreamRowsAsync`-style) | **(a)** | (a) keeps one name per concept; the spike shows the ambiguity remains only for net8 consumers on the default C# 12 with a type implementing both interfaces, and .NET 8 support ends in November 2026. (b) loses bounded streaming of large synchronous sources (unbuffered data readers). (c) is unambiguous everywhere but doubles the names users must learn |
| D8.7 | Reusable spec type | (a) a separate immutable `ChartTemplate<TRow>`; (b) make builders immutable (copy on write) so any builder is reusable; (c) a delegate `Func<IEnumerable<TRow>, ChartAssemblyInput>` | **(a)** | (b) changes the core that freezes at Stable and adds a copy per call to every chain; (c) cannot validate a spec or carry metadata; (a) is one class with six members (no `ValidateSample`: it would be `Validate(Bind(rows))`, a frozen name for a one-line composition) |
| D8.8 | Row checks during a streamed write | (a) write every row as given (exact mirror of the materialised output; a null row is found by validating a sample, or at rendering); (b) throw on the conditions that make every backend throw (a null row), truncating the output; (c) collect row Infos and Warnings into a result available after the write | **(a)** | (a) keeps the decided rule that `Build()`/`ToJson()` and so `FlintJson` never validate, and the §13.1 constraint that a streamed document is byte-identical to the materialised one; a broken row reaches the client and fails there. (b) surfaces the bug at the server for a document no backend could assemble anyway, but it carves an exception out of the decided "Validation" row of 2026-10-17 and out of the byte-identity constraint (the materialised path writes `null`), so choosing it needs the owner to confirm both exceptions, and §13.1 and §13.10 would then name them; (c) is useful for logging only and adds a result type to the writer's API (additive later) |
| D8.9 | `FirstRowKeys` for streamed data | (a) from the row metadata when the shape is static, empty otherwise, with `rows_not_inspected`; (b) peek the first row before validating (async validation API, the first row buffered and replayed); (c) always empty | **(a)** | (a) is exact for typed rows thanks to the null guarantee and needs no new async validator API; (b) also covers dynamic shapes, but forces `ValidateAsync` and changes enumeration semantics (the source would be partly read before the write); (c) drops `unknown_field` for the main use case |

#### 13.15 Defaults applied (say if you object)

- The drain threshold is 16 KiB, internal and not configurable.
- The template stores the spec as UTF-8 bytes and reads it per bind.
- No `ValidateSample`; a sample is validated with `ChartValidator.Validate(template.Bind(sample), backend)`.
- The streamed writer drains a library-owned buffer to the stream and flushes the stream once, at the end.
- On streamed and template validation, `series_field_missing` is a Warning.
- `rows_not_inspected` is an Info, so it never changes `IsValid`.
