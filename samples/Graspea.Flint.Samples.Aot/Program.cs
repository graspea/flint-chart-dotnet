// AOT smoke test (design part 3 §10.2, part 4 §13.8): builds documents through the JsonTypeInfo overloads only, with a
// source-generated row context, validates them and prints each as one JSON line; then binds rows late through a template,
// from asynchronous batches, materialised and streamed. CI publishes this project with PublishAot and runs the native
// binary; trimming or AOT warnings fail the publish (warnings are errors).
using System.Text;
using System.Text.Json.Serialization;
using Graspea.Flint;
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
return 0;

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
