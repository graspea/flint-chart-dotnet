# Delivering fixed charts to a browser

This page covers the `Graspea.Flint.Delivery` namespace of the `Graspea.Flint` package: how a server sends a fixed set of chart definitions and their rows to a browser, which then assembles and draws them with flint-chart. The SDK writes the JSON documents; it does not read them, render anything or depend on a web framework. The contract is declared in [versioning.md](versioning.md#the-delivery-contract).

## Overview

A dashboard shows charts whose definitions are fixed at build time (the encodings, sort orders and options) and whose rows change per request. The flow has three steps:

1. **Specs first.** The browser fetches one cached document, the manifest, which holds every chart's data-free spec.
2. **Rows per group.** When a group of charts is shown, the browser requests its rows. The server answers with one or more pages; each page names every chart of the group once, with its rows or a withheld code.
3. **Draw once.** The browser concatenates the rows per chart across all pages and assembles and draws each chart once, after the last page. A prefix of the rows can change types, sizes, series and categories, so drawing per page is not supported.

Specs are cached and identical for every caller; rows are authorized and sent per request.

## Wire contract v1

Three documents. Members are single lowercase words. The per-chart `input` is an unchanged data-free `ChartAssemblyInput`.

```json
// manifest: the same bytes for every request; ETag = the quoted Hash
{ "contract": 1, "flint": "1.0.1",
  "charts": { "revenue-by-month": { "input": { "semantic_types": { }, "chart_spec": { } } },
              "traffic-by-source": { "input": { "chart_spec": { } } },
              "stock-by-warehouse": { "input": { "chart_spec": { } } } },
  "groups": { "overview": ["revenue-by-month", "traffic-by-source"], "inventory": ["stock-by-warehouse"] } }

// page: one group; every chart of the group exactly once; next only when another page follows
{ "contract": 1, "group": "overview",
  "charts": { "revenue-by-month": { "rows": [ { "month": "2024-01-01", "region": "North", "revenue": 1200.50 } ] },
              "traffic-by-source": { "withheld": "restricted" } },
  "next": "p2" }

// last page of that sequence: a finished chart carries "rows": []
{ "contract": 1, "group": "overview",
  "charts": { "revenue-by-month": { "rows": [ { "month": "2024-02-01", "region": "North", "revenue": 980.00 } ] },
              "traffic-by-source": { "withheld": "restricted" } } }

// error
{ "contract": 1, "error": { "code": "forbidden" } }
```

Rules:

1. **`contract`** is in every document. It stays 1 while members are only added; a removal, a rename or a change of meaning bumps it. Clients ignore unknown members at every level.
2. **`flint`** is `FlintUpstream.Version`, in the manifest only. The browser compares it with its build-time constant and refuses to draw on a mismatch of the first three parts; a host that deploys server and client from one commit may skip the check.
3. **Ids and group names** match `[A-Za-z0-9_.-]+`. Chart ids are host-chosen and unique per manifest; `charts` holds each chart once, even when it sits in several groups; `groups` lists ids in group order. Clients use ids as object keys, so `constructor` and `__proto__` are legal ids: the reference client keys prototype-free objects.
4. **A chart member of a page** has exactly one of `rows` (an array, possibly empty, whose elements are the template's `data.values` elements) or `withheld` (a non-empty host-defined code; the SDK defines none). Every page names every chart of its group; a finished chart carries `"rows":[]`. A chart withheld on any page is withheld, and the client keeps the first code.
5. **`next`** is present only when another page follows and is opaque to the client. The host authorizes every page request, then decodes the cursor, reads one consistent result per sequence, and answers a stale cursor with an error document. The client restarts from the first page and never merges pages of different sequences. It concatenates `rows` per chart in arrival order and draws once after the last page.
6. **Empty `rows`** means nothing to draw: the client shows an empty state and never assembles `values: []`.
7. **Deliberately absent:** counts, totals, more-pages flags, messages, timestamps, sizes, backend names, the SDK version and per-chart hashes. The contract stays small and additive and the manifest stays byte-stable for caching.
8. **`Hash`** is outside the document: the lowercase hex SHA-256 of the manifest's stored bytes, used as the strong validator (the `ETag`).

The SDK writes members in the order shown; `next` is the only optional member and is written only when set. Clients must not depend on member order.

## Declaring charts

Declare each chart once, in a static field, as a `ChartHandle<TRow>` that binds a stable id to an immutable template. Charts shown together form a `ChartGroup`; the groups form a `ChartManifest`. The blocks on this page are labelled by file and are not compiled by a test: a block that mixes type declarations and top-level statements would not compile as one file. `StockTemplate`, `sales`, `db` and `cursor` stand for the host's own members.

```csharp
// Charts.cs
public sealed record Sale(DateOnly Month, string Region, decimal Revenue);
public sealed record Visit(string Source, long Visits);
public sealed record Stock(string Warehouse, int Units);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Sale))]
[JsonSerializable(typeof(Visit))]
[JsonSerializable(typeof(Stock))]
public sealed partial class ShopJson : JsonSerializerContext;

public static class Charts
{
    public static readonly ChartHandle<Sale> Revenue = new("revenue-by-month",
        Chart.Line(ShopJson.Default.Sale)
            .X(r => r.Month, SemanticType.YearMonth).Y(r => r.Revenue, SemanticType.Amount).Color(r => r.Region)
            .Encoding(Channel.X, e => e.Type = EncodingType.Temporal)
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .Encoding(Channel.Color, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder("North", "South"); })
            .ToTemplate(),
        maxRows: 10_000);

    public static readonly ChartHandle<Visit> Traffic = new("traffic-by-source",
        Chart.Bar(ShopJson.Default.Visit)
            .X(r => r.Source).Y(r => r.Visits)
            .Encoding(Channel.X, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder("search", "direct", "social"); })
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .ToTemplate(),
        maxRows: 10_000);

    public static readonly ChartHandle<Stock> StockLevels = new("stock-by-warehouse", StockTemplate, maxRows: 10_000);

    public static readonly ChartGroup Overview = new("overview", Revenue, Traffic);
    public static readonly ChartGroup Inventory = new("inventory", StockLevels);
    public static readonly ChartManifest Manifest = ChartManifest.Create(Overview, Inventory);   // one per variant
}

// Program.cs, at start-up
foreach (var group in Charts.Manifest.Groups)
{
    var check = group.Validate(FlintBackend.ECharts);
    check.ThrowIfInvalid();
    if (check.Issues.Any(i => i.Code.StartsWith("fixed_", StringComparison.Ordinal)))
        throw new InvalidOperationException(string.Join("; ", check.Issues));
}

// per request, after the host authorized the caller for this group
var page = new DataPage(Charts.Overview, [Charts.Revenue.Rows(sales), Charts.Traffic.Withheld("restricted")]);
byte[] body = page.ToUtf8Bytes();                                            // or page.ToJson(), page.WriteTo(writer)

// asynchronous source: each part awaited; the page exists only when every part does
var page2 = new DataPage(Charts.Overview, [
    await Charts.Revenue.RowsAsync(db.Sales.AsAsyncEnumerable(), ct),
    Charts.Traffic.Withheld("restricted"),
], next: cursor);
await page2.WriteAsync(response.Body, ct);

// streamed: flat memory, WriteAsync only, single use; a failure after the first byte truncates the body
var page3 = new DataPage(Charts.Overview, [
    Charts.Revenue.Stream(db.Sales.AsAsyncEnumerable()),
    Charts.Traffic.Withheld("restricted"),
]);
await page3.WriteAsync(response.Body, ct);
```

A handle refuses a template without row metadata (one whose builder was entered with `ChartData`; a spec file gets metadata through `ChartTemplate.Create(spec, typeInfo)`) and a template whose document already has a top-level `data` member, which a `Configure` action that writes `Extensions["data"]` can produce: a handle needs a data-free template. Nothing in the delivery API validates by itself; validation is explicit (see [Start-up validation](#start-up-validation)).

Groups, manifests and pages copy the arrays and lists they are given and expose read-only views. `ChartManifest.Utf8Bytes` is the stored array as `ReadOnlyMemory<byte>`: the memory is shared and must not be modified, because `Hash` and every write use it.

### Byte identity

The elements of a chart's `rows` equal, byte for byte, the elements of `data.values` of the streamed document (`FlintJson.SerializeAsync` or `SerializeToUtf8Bytes` over `BindStream` with the same rows), and those of `Bind(rows)` for rows whose converters write JSON tokens. Both paths share one row writer. A row converter that writes raw JSON (`Utf8JsonWriter.WriteRawValue`) can differ from `Bind` in insignificant whitespace; the result is then JSON-equal, not byte-equal. Under `WriteTo(Utf8JsonWriter)` with indentation or another encoder the raw values stay compact.

## Host embedding

The delivery namespace references nothing of ASP.NET Core. Both samples build the body in memory (`ToUtf8Bytes`, `WriteTo` over a buffer), so no synchronous write reaches the response stream, and a part that throws fails the request before any byte is sent.

The HTTP routes keep the group, the manifest tag and the cursor in query parameters, never in a path segment: the legal group names `.` and `..` are collapsed by URL parsing. The manifest is at `/charts/manifest`; pages are at `/charts/page?group=<name>&manifest=<tag>`, plus `&next=<cursor>` when set.

```csharp
// Program.cs, after var app = builder.Build(); an uncompiled docs sample
app.MapGet("/charts/manifest", (HttpContext ctx) =>
{
    var m = Charts.Manifest;                                          // the caller's variant
    ctx.Response.Headers.ETag = $"\"{m.Hash}\"";
    ctx.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
    return ctx.Request.Headers.IfNoneMatch == $"\"{m.Hash}\"" ? Results.StatusCode(304)
        : Results.Bytes(m.Utf8Bytes.ToArray(), "application/json; charset=utf-8");
}).RequireAuthorization();

app.MapGet("/charts/page", async (HttpContext ctx, SalesStore store, string group, string? manifest, string? next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (manifest != $"\"{Charts.Manifest.Hash}\"")                     // the browser holds an old manifest: restart
        return Results.Text(new DeliveryError("stale").ToJson(), "application/json; charset=utf-8", statusCode: 409);
    if (group != "overview" || !store.MayRead(ctx.User))               // authorize first, decode the cursor after
        return Results.Text(new DeliveryError("forbidden").ToJson(), "application/json; charset=utf-8", statusCode: 403);
    try
    {
        var result = await store.ReadAsync(ctx.User, next, ctx.RequestAborted);   // one consistent result per sequence
        var page = new DataPage(Charts.Overview,
            [await Charts.Revenue.RowsAsync(result.Sales, ctx.RequestAborted), Charts.Traffic.Withheld("restricted")],
            result.Next);
        return Results.Bytes(page.ToUtf8Bytes(), "application/json; charset=utf-8");
    }
    catch (ChartRowLimitException)                                    // the host's own code; it aggregates more
    {
        return Results.Text(new DeliveryError("row_limit").ToJson(), "application/json; charset=utf-8", statusCode: 422);
    }
}).RequireAuthorization();
```

```csharp
// WebSocket RPC: the host's own envelope, written with a Utf8JsonWriter over an IBufferWriter<byte>.
// The request carries { id, method: "manifest" | "page", group, manifest, next }; the host compares
// request.manifest with the bare Charts.Manifest.Hash and answers "stale" on a mismatch; the reply envelope carries the bare Hash.
var buffer = new ArrayBufferWriter<byte>();
using (var writer = new Utf8JsonWriter(buffer))
{
    writer.WriteStartObject();
    writer.WriteNumber("id", requestId);
    writer.WritePropertyName("result");
    page.WriteTo(writer);                                             // or Charts.Manifest.WriteTo(writer), error.WriteTo(writer)
    writer.WriteEndObject();
}
await socket.SendAsync(buffer.WrittenMemory, WebSocketMessageType.Text, endOfMessage: true, ct);
```

`SalesStore` is an example store that checks the caller's access, decodes the cursor only after that check, and answers a stale cursor with its own exception, which the host maps to an error document.

### Manifest and page consistency across deploys

A browser can keep an old manifest in memory across a server deploy, so pages written by the new definitions could be merged with specs of the old ones. The pages therefore do not carry the manifest hash (the wire stays small); instead the reference client keeps each manifest's `ETag` header and sends the tag of the manifest the caller passes to `collectGroup` with every page request as the `manifest` query parameter, so two overlapping manifest loads cannot mix their tags. The host compares it with the `ETag` of its current manifest (`"\"" + manifest.Hash + "\""`) and answers an error document with its own code when they differ (the samples use `stale`). The client restarts from the manifest on any error. A host that serves the charts cross-origin sends `Access-Control-Expose-Headers: ETag` on the manifest response, or the page cannot read the tag. A WebSocket host carries the hash in its own RPC envelope.

### HasStarted and Abort

For a page without streamed parts, every row, limit, invariant and cancellation failure happens before the first byte, so the host can answer an error document. Transport failures (the stream's `WriteAsync` or `FlushAsync` throwing) can happen at any time, including after the whole page was written, at the final flush. The token passed to `WriteAsync` reaches only the stream, so a cancellation observed there is a transport failure and can come at any time, including at the final flush. The SDK never writes anything after a failure, and a host never appends an error document after its output has started. In ASP.NET Core `ctx.Response.HasStarted` tells: when it is false, an error document is still possible; when it is true, the body is a truncated prefix, so the host calls `ctx.Abort()` and the client restarts.

## Streamed parts

```csharp
public ChartPart Stream(IAsyncEnumerable<TRow> rows);   // on ChartHandle<TRow>: a single-use part
```

- **Write.** A page holding a streamed part is written only by `DataPage.WriteAsync`, with one `Utf8JsonWriter` over a pooled buffer. The buffer is drained with `Stream.WriteAsync` whenever it holds at least 16 KiB, after each streamed row and after each materialised part. Materialised parts of the same page go in as raw values; several streamed parts are read one after the other, in group order. `WriteAsync` claims every streamed part before it writes anything, so a second or concurrent `WriteAsync`, or a part already written in another page, throws before the first byte. A claim cannot be undone: when a later streamed part was already written elsewhere, the page and the parts claimed before it are spent, although nothing reached the stream; build a new page with new parts. `WriteTo`, `ToUtf8Bytes` and `ToJson` throw `InvalidOperationException` for a page with a streamed part.
- **Memory.** The memory bound is the largest single row or materialised part plus about 32 KiB of buffer, whatever the row count.
- **Failure model.** A row, source, limit or cancellation failure comes out of `WriteAsync` with a prefix of the page in the stream. The prefix is empty when less than 16 KiB had been drained, so the host can still answer an error document. Otherwise the body is truncated, invalid JSON that carries no withheld code (the same rule as [row-data.md](row-data.md#late-bound-asynchronous-and-streamed-rows)); the host aborts the response, the client's JSON parse fails and it restarts from the first page. Everything that can be checked early still is: the bad id, missing metadata and a data member at the handle, the page invariants at `DataPage`. A host decides `Withheld` before it writes.
- **When to use.** For charts whose source is large or slow and whose rows cannot fail on a value (no value-dependent predicates, finite doubles under `JsonTypeInfo` metadata), over HTTP or another byte stream. Keep materialised parts where a clean error document matters or where the transport sends whole messages (such as a WebSocket RPC).

## MaxRows and ChartRowLimitException

`maxRows` on the handle caps one part: the rows of one `Rows`, `RowsAsync` or `Stream` call, that is, one source enumeration. It does not cap a chart's rows across a paging sequence. The limit fires when the source yields row `MaxRows + 1`, before that row is written; for a materialised part this is before any byte of the page, for a streamed part it comes out of `WriteAsync` and bytes may already be in the stream. It throws `ChartRowLimitException` (properties `Chart` and `Limit`); the host maps it to its own code. `maxRows` below 1 throws `ArgumentOutOfRangeException`; `null` means no limit.

A host that pages a chart caps the whole chart across pages itself, for example with a counter in the cursor state:

```csharp
// two pages of at most 6,000 rows each, with a chart-wide budget of 10,000 rows
var budget = 10_000 - state.RowsSent;                          // read from the host's own cursor state
var take = Math.Min(6_000, budget);
if (take <= 0) return new DeliveryError("row_limit");           // the host's own code
var part = Charts.Revenue.Rows(source.Skip(state.RowsSent).Take(take));   // MaxRows still guards this one part
state.RowsSent += take;
```

On page one the budget is 10,000 and 6,000 rows go out; on page two the budget is 4,000 and only 4,000 rows go out (or the host answers its error code when it wants to refuse instead of truncate). The SDK cannot do this itself because it never sees the cursor.

## Start-up validation

`ChartGroup.Validate(backend)` runs `Template.Validate(backend)` for each chart with the existing codes, re-pathed to manifest paths (`charts.<id>.input...`), and adds five Info codes with the prefix `fixed_` that flag what a fixed definition leaves to the rows present: a missing type, a missing explicit order, an aggregate, a host override and a fixed size. They are reported from this entry point only, never from `ChartValidator.Validate`. `IsValid` is unchanged; a host fails start-up on the `fixed_` prefix as in the sample above. Each code, its condition and the upstream fact behind it are in [validation-codes.md](validation-codes.md#fixed-definitions-chartgroupvalidate-only).

## Browser

The browser side (fetch the manifest, collect a group's pages, draw once) is plain JavaScript on top of flint-chart; the SDK ships no npm package. The reference code is in the README section [Charts in the browser](https://github.com/graspea/flint-chart-dotnet#charts-in-the-browser). Its transport asks for `/manifest` and `/page?group=&manifest=&next=` as above.

| Topic | Guidance |
|---|---|
| Post-processing | ECharts: replace `tooltip.formatter` with one that escapes HTML, or set `tooltip.renderMode: 'richText'` with a plain-text formatter; upstream interpolates raw values into tooltips. Vega-Lite: call `stripPrivateKeys(spec)` before `JSON.stringify`, `structuredClone` or `postMessage`. Take colours from computed CSS after assembly; a theme change re-assembles from the kept rows |
| Order | ECharts Line series and Heatmap nominal categories follow row order, not `sortBy`: the server writes rows in the declared order. Any `sortBy` also switches off the inferred order of the built-in ordinal sequences (month, day, quarter, direction), so an explicit order lists every category |
| Keys | upstream takes the field keys from the first row of the merged array; with typed rows every non-null row has every key, so page order does not change key detection |
| Caching | `ETag` / `If-None-Match` and a private cache on the manifest; `Cache-Control: no-store` on pages and errors |

## Limits and exposure

- **Rows per chart.** Keep the merged rows of one chart at or below about 10,000, where assembly stays in tens of milliseconds. Above that, aggregate on the server; more pages do not help, and some engines overflow the call stack around 125,000 rows. The 10,000 guidance is per chart, across all its pages; `MaxRows` caps one part, so a host that pages caps the chart across its pages itself (see MaxRows above), and a host that caps a whole page sums its parts.
- **Backends.** Image-Charts and Excel are outside the browser flow.
- **Exposure.** Rows and withheld codes are data, never HTML: a client puts them into the page as text. Keep rows in memory only and clear them on sign-out; never keep the assembled option. Authorize every page request before reading the cursor.

## See also

- [row-data.md](row-data.md): how rows become JSON values.
- [validation-codes.md](validation-codes.md): the `fixed_` codes.
- [versioning.md](versioning.md#the-delivery-contract): what the contract freezes.
