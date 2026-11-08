// AOT smoke test (design part 3 §10.2, part 4 §13.8): builds documents through the JsonTypeInfo overloads only, with a
// source-generated row context, validates them and prints each as one JSON line; then binds rows late through a template,
// from asynchronous batches, materialised and streamed. CI publishes this project with PublishAot and runs the native
// binary; trimming or AOT warnings fail the publish (warnings are errors).
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint;
using Graspea.Flint.Delivery;
using Graspea.Flint.Samples.Aot;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

Reading[] readings =
[
    new(new DateOnly(2024, 6, 1), "Coastal", 17.2, 0.0),
    new(new DateOnly(2024, 6, 2), "Coastal", 18.9, 2.4),
    new(new DateOnly(2024, 6, 3), "Coastal", 16.4, 11.0),
    new(new DateOnly(2024, 6, 1), "Inland", 22.8, null),
    new(new DateOnly(2024, 6, 2), "Inland", 25.1, 0.0),
    new(new DateOnly(2024, 6, 3), "Inland", 23.6, 4.2),
];
var typeInfo = RowContext.Default.Reading;

ChartAssemblyInput[] documents =
[
    Chart.Line(readings, typeInfo)
        .X(r => r.Day, SemanticType.Date)
        .Y(r => r.TemperatureC, SemanticType.Temperature)
        .Color(r => r.Station, SemanticType.Category)
        .Interpolate(LineChartInterpolate.Monotone)
        .Title("Daily temperature")
        .Build(),

    // The member is written as "rain mm" ([JsonPropertyName]); the selector resolves to that key.
    Chart.Of(ChartType.BarChart, readings, typeInfo)
        .Encode(Channel.X, r => r.Station, SemanticType.Category)
        .Encode(Channel.Y, r => r.RainMm, SemanticType.Quantity)
        .Encoding(Channel.Y, e => e.Aggregate = Aggregate.Sum)
        .Title("Rainfall by station")
        .Build(),

    new ChartAssemblyInput
    {
        Data = ChartData.FromRows(readings, typeInfo),
        SemanticTypes = new() { ["temperatureC"] = SemanticType.Temperature },
        ChartSpec = new ChartSpec { ChartType = ChartType.Histogram, Encodings = { ["x"] = "temperatureC" } },
    },
];

foreach (var document in documents)
{
    ChartValidator.Validate(document).ThrowIfInvalid();
    var json = FlintJson.Serialize(document);
    if (FlintJson.Serialize(FlintJson.Deserialize(json)) != json)
    {
        Console.Error.WriteLine($"Round trip changed the document: {json}");
        return 1;
    }

    Console.WriteLine(json);
}

// Late-bound rows: a deferred entry, a template, a batched asynchronous source, and a streamed write that must give the
// bytes of the materialised document.
var template = Chart.Line(typeInfo)
    .X(r => r.Day, SemanticType.Date)
    .Y(r => r.TemperatureC, SemanticType.Temperature)
    .Color(r => r.Station, SemanticType.Category)
    .Title("Daily temperature, bound late")
    .ToTemplate();
template.Validate().ThrowIfInvalid();

var bound = await template.BindAsync(RowSource.FromBatches(Batches(readings)));
ChartValidator.Validate(bound).ThrowIfInvalid();
var materialised = FlintJson.Serialize(bound);

using var streamed = new MemoryStream();
await FlintJson.SerializeAsync(streamed, template.BindStream(RowSource.FromBatches(Batches(readings))));
if (Encoding.UTF8.GetString(streamed.ToArray()) != materialised)
{
    Console.Error.WriteLine($"The streamed document differs from the materialised one: {materialised}");
    return 1;
}

Console.WriteLine(materialised);

// Browser data delivery: every public member of the delivery types, through JsonTypeInfo entries only.
var revenue = new ChartHandle<Reading>("revenue", template, maxRows: 10_000);
var other = new ChartHandle<Reading>("traffic", template);
var group = new ChartGroup("overview", revenue, other);
var manifest = ChartManifest.Create(group);

var manifestBuffer = new ArrayBufferWriter<byte>();
using (var manifestWriter = new Utf8JsonWriter(manifestBuffer))
{
    manifest.WriteTo(manifestWriter);
}

Console.WriteLine($"manifest hash {manifest.Hash}");
Console.WriteLine($"manifest bytes {manifest.Utf8Bytes.Length}, chars {manifest.ToJson().Length}, written {manifestBuffer.WrittenCount}");
Console.WriteLine($"group {group.Name} charts {group.Charts.Count}, manifest groups {manifest.Groups.Count}, issues {group.Validate(FlintBackend.ECharts).Issues.Count}");
Console.WriteLine($"template json {template.ToJson().Length}, contract {DeliveryContract.Version}");

var error = new DeliveryError("forbidden");
var errorBuffer = new ArrayBufferWriter<byte>();
using (var errorWriter = new Utf8JsonWriter(errorBuffer))
{
    error.WriteTo(errorWriter);
}

if (error.Code != "forbidden" || error.ToJson() != Encoding.UTF8.GetString(error.ToUtf8Bytes())
    || !errorBuffer.WrittenSpan.SequenceEqual(error.ToUtf8Bytes()))
{
    Console.Error.WriteLine("The delivery error forms differ.");
    return 1;
}

Console.WriteLine(error.ToJson());

var page = new DataPage(group, [revenue.Rows(readings), other.Withheld("restricted")], next: "p2");
var pageBuffer = new ArrayBufferWriter<byte>();
using (var pageWriter = new Utf8JsonWriter(pageBuffer))
{
    page.WriteTo(pageWriter);
}

var pageBytes = page.ToUtf8Bytes();
var pageJson = page.ToJson();
using var pageStream = new MemoryStream();
await page.WriteAsync(pageStream);

var asyncPage = new DataPage(group, [await revenue.RowsAsync(Flatten(Batches(readings))), other.Withheld("restricted")], next: "p2");
using var streamedPageStream = new MemoryStream();
await new DataPage(group, [revenue.Stream(Rows(readings)), other.Withheld("restricted")], next: "p2").WriteAsync(streamedPageStream);

Console.WriteLine(pageJson);
Console.WriteLine($"page next {page.Next}, group {page.Group.Name}, charts {page.Charts.Count}, maxRows {revenue.MaxRows}");
foreach (var part in page.Charts)
{
    Console.WriteLine($"part {part.Chart.Id} streamed {part.IsStreamed} withheld {part.WithheldCode}");
}

if (!pageBuffer.WrittenSpan.SequenceEqual(pageBytes)
    || Encoding.UTF8.GetString(pageBytes) != pageJson
    || !pageStream.ToArray().AsSpan().SequenceEqual(pageBytes)
    || !asyncPage.ToUtf8Bytes().AsSpan().SequenceEqual(pageBytes)
    || !streamedPageStream.ToArray().AsSpan().SequenceEqual(pageBytes))
{
    Console.Error.WriteLine($"The page forms differ: {pageJson}");
    return 1;
}

if (manifest.Hash != Convert.ToHexString(SHA256.HashData(manifest.Utf8Bytes.Span)).ToLowerInvariant()
    || !manifestBuffer.WrittenSpan.SequenceEqual(manifest.Utf8Bytes.Span)
    || manifest.ToJson() != Encoding.UTF8.GetString(manifest.Utf8Bytes.Span))
{
    Console.Error.WriteLine("The manifest forms or hash differ.");
    return 1;
}

using (var manifestDocument = JsonDocument.Parse(manifest.Utf8Bytes))
{
    var input = manifestDocument.RootElement.GetProperty("charts").GetProperty("revenue").GetProperty("input").GetRawText();
    if (input != revenue.Template.ToJson())
    {
        Console.Error.WriteLine("The manifest input differs from the template document.");
        return 1;
    }
}

try
{
    var tight = new ChartHandle<Reading>("tight", template, maxRows: 1);
    tight.Rows(readings[..2]);
    Console.Error.WriteLine("The row limit did not trigger.");
    return 1;
}
catch (ChartRowLimitException limit)
{
    Console.WriteLine($"limit {limit.Limit} chart {limit.Chart.Id}");
}

return 0;

static async IAsyncEnumerable<Reading> Flatten(IAsyncEnumerable<IReadOnlyList<Reading>> batches)
{
    await foreach (var batch in batches)
    {
        foreach (var row in batch)
        {
            yield return row;
        }
    }
}

static async IAsyncEnumerable<Reading> Rows(Reading[] rows)
{
    foreach (var row in rows)
    {
        await Task.Yield();
        yield return row;
    }
}

static async IAsyncEnumerable<IReadOnlyList<Reading>> Batches(Reading[] rows)
{
    foreach (var batch in rows.Chunk(4))
    {
        await Task.Yield();
        yield return batch;
    }
}

namespace Graspea.Flint.Samples.Aot
{
    internal sealed record Reading(DateOnly Day, string Station, double TemperatureC, [property: JsonPropertyName("rain mm")] double? RainMm);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Reading))]
    internal sealed partial class RowContext : JsonSerializerContext;
}
