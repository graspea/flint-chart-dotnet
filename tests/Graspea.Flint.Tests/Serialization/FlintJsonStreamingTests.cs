using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Rows;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Serialization;

public sealed record Reading(DateOnly Day, string Station, double? Value, [property: JsonPropertyOrder(-1)] int Id);

public sealed class StationReading
{
    public string Station { get; set; } = "";

    [JsonPropertyName("10")]
    public int Ten { get; set; }

    [JsonPropertyName("2")]
    public int Two { get; set; }
}

public sealed record NestedValue(System.Text.Json.Nodes.JsonNode? Value);

/// <summary>A query-like source: synchronous and asynchronous at run time, as an Entity Framework Core query is, with a
/// synchronous enumerator that throws.</summary>
public sealed class QueryLikeRows<T>(IEnumerable<T> rows) : IEnumerable<T>, IAsyncEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("synchronous enumeration");

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        AsyncRows.Iterate(rows, cancellationToken).GetAsyncEnumerator(cancellationToken);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Reading))]
[JsonSerializable(typeof(StationReading))]
[JsonSerializable(typeof(NestedValue))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(object))]
internal sealed partial class StreamingContext : JsonSerializerContext;

public sealed class FlintJsonStreamingTests
{
    private static Reading[] Readings(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Reading(new DateOnly(2024, 1, 1).AddDays(i % 300), i % 3 == 0 ? "Coast <é+>" : "Inland", i % 7 == 0 ? null : i * 0.5, i))];

    private static readonly JsonTypeInfo<Reading> TypeInfo = StreamingContext.Default.Reading;

    /// <summary>A document with every top-level member and data extensions around <paramref name="data"/>.</summary>
    private static ChartAssemblyInput Document(ChartData data, bool everyMember = true)
    {
        var input = new ChartAssemblyInput
        {
            Data = data,
            ChartSpec = new ChartSpec { ChartType = ChartType.LineChart, Title = "Readings", Encodings = { ["x"] = "day", ["y"] = "value" } },
        };
        if (everyMember)
        {
            data.Extensions = new() { ["format"] = JsonDocument.Parse("""{"type":"json"}""").RootElement.Clone() };
            input.SemanticTypes = new() { ["day"] = SemanticType.Date };
            input.ThemeSpec = ThemePreset.Economist;
            input.Options = new AssembleOptions { AddTooltips = true };
            input.FieldDisplayNames = new() { ["value"] = "Value" };
            input.Extensions = new() { ["x-note"] = JsonDocument.Parse("\"kept\"").RootElement.Clone() };
        }

        return input;
    }

    private static string Materialised(Reading?[] rows, bool everyMember = true) =>
        FlintJson.Serialize(Document(ChartData.FromRows(rows, TypeInfo!), everyMember));

    private static async Task<string> StreamedAsync(ChartData data, bool everyMember = true)
    {
        var stream = new RecordingStream();
        await FlintJson.SerializeAsync(stream, Document(data, everyMember));
        return stream.Text;
    }

    // source: design §13.5 step 9 and §13.13 item 5 (a streamed document is byte-identical to FlintJson.Serialize of the
    // same document with the rows materialised by FromRows with the same metadata), for a synchronous source
    [Test]
    public async Task SerializeAsync_Streamed_SyncSource_IsByteIdenticalToMaterialised()
    {
        var rows = Readings(50);
        Assert.That(await StreamedAsync(ChartData.Stream(rows, TypeInfo)), Is.EqualTo(Materialised(rows)));
    }

    // source: design §13.5 step 9 and §13.13 item 5, for an asynchronous source and the reflection metadata
    [Test]
    public async Task SerializeAsync_Streamed_AsyncSource_IsByteIdenticalToMaterialised()
    {
        var rows = Readings(50);
        Assert.That(await StreamedAsync(ChartData.Stream(AsyncRows.Iterate(rows), TypeInfo)), Is.EqualTo(Materialised(rows)));
        var reflection = FlintJson.Serialize(Document(ChartData.FromRows(rows)));
        Assert.That(await StreamedAsync(ChartData.Stream(AsyncRows.Iterate(rows))), Is.EqualTo(reflection));
    }

    // source: design §13.5 step 4 (data first, the remainder after it) and §13.13 item 5 (with only the required members)
    [Test]
    public async Task SerializeAsync_Streamed_MinimalDocument_IsByteIdenticalToMaterialised()
    {
        var rows = Readings(3);
        Assert.That(await StreamedAsync(ChartData.Stream(rows, TypeInfo), everyMember: false), Is.EqualTo(Materialised(rows, everyMember: false)));
    }

    // source: design §13.5 step 6 (the buffer drains at 16 KiB; the output across the boundary is unchanged) and step 7
    // (one stream flush, at the end)
    [Test]
    public async Task SerializeAsync_Streamed_AcrossTheDrainBoundary_IsByteIdenticalAndFlushesOnce()
    {
        var rows = Readings(5000);
        var stream = new RecordingStream();
        await FlintJson.SerializeAsync(stream, Document(ChartData.Stream(AsyncRows.Iterate(rows), TypeInfo)));
        Assert.That(stream.Text, Is.EqualTo(Materialised(rows)));
        Assert.That(stream.Received, Is.GreaterThan(4 * 16384));
        Assert.That(stream.Writes, Is.GreaterThan(4));
        Assert.That(stream.Flushes, Is.EqualTo(1));
    }

    // source: design §13.6 "Checks while streaming" (D8.8 (a)) and §13.10 (a null row is written as null by every path,
    // byte-identical to the materialised output)
    [Test]
    public async Task SerializeAsync_Streamed_NullRow_IsWrittenAsNull()
    {
        Reading?[] rows = [.. Readings(2), null, .. Readings(1)];
        var streamed = await StreamedAsync(ChartData.Stream(AsyncRows.Iterate(rows), TypeInfo!));
        Assert.That(streamed, Is.EqualTo(Materialised(rows)));
        Assert.That(streamed, Does.Contain(",null,"));
    }

    // source: design §13.5 step 7 and §13.13 item 5 (SerializeAsync to a stream whose synchronous Write and Flush throw,
    // as Kestrel's response body does, succeeds and calls FlushAsync exactly once)
    [Test]
    public async Task SerializeAsync_ToAsyncOnlyStream_NeverWritesSynchronously()
    {
        var rows = Readings(3000);
        var streamed = new RecordingStream(asyncOnly: true);
        await FlintJson.SerializeAsync(streamed, Document(ChartData.Stream(AsyncRows.Iterate(rows), TypeInfo)));
        Assert.That(streamed.Text, Is.EqualTo(Materialised(rows)));
        Assert.That(streamed.Flushes, Is.EqualTo(1));

        var materialised = new RecordingStream(asyncOnly: true);
        await FlintJson.SerializeAsync(materialised, Document(ChartData.FromRows(rows, TypeInfo)));
        Assert.That(materialised.Text, Is.EqualTo(Materialised(rows)));
    }

    // source: design §13.5 "Hosting" (no synchronous I/O on the asynchronous path): an IQueryable binds the IEnumerable
    // overload by its static type, but its run-time object also implements IAsyncEnumerable, which SerializeAsync reads;
    // the synchronous paths still accept the source and enumerate it synchronously
    [Test]
    public async Task SerializeAsync_SourceThatIsAlsoAsynchronous_IsReadAsynchronously()
    {
        var rows = Readings(40);
        IEnumerable<Reading> query = new QueryLikeRows<Reading>(rows);

        Assert.That(await StreamedAsync(ChartData.Stream(query, TypeInfo)), Is.EqualTo(Materialised(rows)));
        Assert.That(() => FlintJson.Serialize(new RecordingStream(), Document(ChartData.Stream(query, TypeInfo))),
            Throws.InvalidOperationException.With.Message.EqualTo("synchronous enumeration"));
    }

    // source: design §13.5 step 9 (the streamed bytes are those of the materialised write) and System.Text.Json's default
    // MaxDepth of 64 that FlintJson.Options applies: a row nested to the limit is written by both paths, one level deeper
    // makes both throw JsonException, so the streamed path never writes a document FlintJson.Deserialize refuses
    [TestCase(60)]
    [TestCase(61)]
    [TestCase(70)]
    public async Task SerializeAsync_Streamed_NestedRow_WritesOrThrowsAsTheMaterialisedWrite(int depth)
    {
        System.Text.Json.Nodes.JsonNode Nested()
        {
            System.Text.Json.Nodes.JsonNode node = 1;
            for (var i = 0; i < depth; i++)
            {
                node = new System.Text.Json.Nodes.JsonArray(node);
            }

            return node;
        }

        ChartAssemblyInput Input(ChartData data) => new() { Data = data, ChartSpec = new ChartSpec { ChartType = ChartType.BarChart } };
        string? materialised = null;
        try
        {
            materialised = FlintJson.Serialize(Input(ChartData.FromRows([new NestedValue(Nested())], StreamingContext.Default.NestedValue)));
        }
        catch (JsonException)
        {
        }

        var stream = new RecordingStream();
        var streamed = Input(ChartData.Stream(AsyncRows.Iterate(new[] { new NestedValue(Nested()) }), StreamingContext.Default.NestedValue));
        if (materialised is null)
        {
            Assert.That(async () => await FlintJson.SerializeAsync(stream, streamed), Throws.InstanceOf<JsonException>());
        }
        else
        {
            await FlintJson.SerializeAsync(stream, streamed);
            Assert.That(stream.Text, Is.EqualTo(materialised));
            Assert.That(() => FlintJson.Deserialize(stream.Text), Throws.Nothing);
        }

        Assert.That(materialised is null, Is.EqualTo(depth > 60));
    }

    // source: design §13.5 step 1 (a document whose data is not streamed is written as FlintJson.Serialize writes it)
    [Test]
    public void Serialize_ToStream_NotStreamed_WritesTheSameBytes()
    {
        var rows = Readings(10);
        var stream = new RecordingStream();
        FlintJson.Serialize(stream, Document(ChartData.FromRows(rows, TypeInfo)));
        Assert.That(stream.Text, Is.EqualTo(Materialised(rows)));
    }

    // source: design §13.5 table (Serialize(Stream) with a synchronous source: streamed through the library's buffer,
    // synchronous writes, byte-identical)
    [Test]
    public void Serialize_ToStream_StreamedSyncSource_IsByteIdenticalAndDrains()
    {
        var rows = Readings(3000);
        var stream = new RecordingStream();
        FlintJson.Serialize(stream, Document(ChartData.Stream(rows, TypeInfo)));
        Assert.That(stream.Text, Is.EqualTo(Materialised(rows)));
        Assert.That(stream.Writes, Is.GreaterThan(2));
        Assert.That(stream.Flushes, Is.EqualTo(1));
    }

    // source: design §13.5 step 2 and §13.9 (single use: the first write claims the data; a second write throws)
    [Test]
    public async Task SerializeAsync_StreamedTwice_Throws()
    {
        var input = Document(ChartData.Stream(Readings(2), TypeInfo));
        await FlintJson.SerializeAsync(new RecordingStream(), input);
        Assert.That(async () => await FlintJson.SerializeAsync(new RecordingStream(), input),
            Throws.InvalidOperationException.With.Message.Contains("already written"));
        Assert.That(() => FlintJson.Serialize(input), Throws.InvalidOperationException.With.Message.Contains("already written"));
    }

    // source: design §13.5 "Other serialisation paths" (an asynchronous source through each synchronous path throws
    // InvalidOperationException naming SerializeAsync; no sync-over-async), §13.9 (the data stays unclaimed)
    [Test]
    public async Task SynchronousPaths_AsyncSource_ThrowAndLeaveTheDataUnclaimed()
    {
        var input = Document(ChartData.Stream(AsyncRows.Iterate(Readings(2)), TypeInfo));
        var paths = new Dictionary<string, Action>
        {
            ["Serialize"] = () => FlintJson.Serialize(input),
            ["Serialize indented"] = () => FlintJson.Serialize(input, indented: true),
            ["SerializeToUtf8Bytes"] = () => FlintJson.SerializeToUtf8Bytes(input),
            ["SerializeToNode"] = () => FlintJson.SerializeToNode(input),
            ["Serialize(Stream)"] = () => FlintJson.Serialize(new RecordingStream(), input),
            ["Serialize(Utf8JsonWriter)"] = () => FlintJson.Serialize(new Utf8JsonWriter(new MemoryStream()), input),
            ["host options"] = () => JsonSerializer.Serialize(input, HostileOptions.Create()),
        };
        foreach (var (name, path) in paths)
        {
            Assert.That(() => path(), Throws.InvalidOperationException.With.Message.Contains("SerializeAsync"), name);
        }

        Assert.That(async () => await JsonSerializer.SerializeAsync(new MemoryStream(), input, HostileOptions.Create()),
            Throws.InvalidOperationException.With.Message.Contains("SerializeAsync"));
        Assert.That(await StreamedAsync(input.Data!), Is.Not.Empty);
    }

    // source: design §13.5 table (string, bytes and node paths with a synchronous source write the rows into the result)
    [Test]
    public void InMemoryPaths_SyncSource_WriteTheSameDocument()
    {
        var rows = Readings(20);
        var expected = Materialised(rows);
        Assert.That(FlintJson.Serialize(Document(ChartData.Stream(rows, TypeInfo))), Is.EqualTo(expected));
        Assert.That(Encoding.UTF8.GetString(FlintJson.SerializeToUtf8Bytes(Document(ChartData.Stream(rows, TypeInfo)))), Is.EqualTo(expected));
        Assert.That(FlintJson.SerializeToNode(Document(ChartData.Stream(rows, TypeInfo))).ToJsonString(), Is.EqualTo(JsonSerializer.Serialize(JsonDocument.Parse(expected).RootElement)));
        Assert.That(FlintJson.Serialize(Document(ChartData.Stream(rows, TypeInfo)), indented: true),
            Is.EqualTo(FlintJson.Serialize(Document(ChartData.FromRows(rows, TypeInfo)), indented: true)));
    }

    // source: design §13.5 "The caller's writer" (the caller's options apply: an indented writer keeps its formatting)
    [Test]
    public void Serialize_ToIndentedCallerWriter_KeepsTheCallersFormatting()
    {
        var rows = Readings(5);
        string Write(ChartData data)
        {
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                FlintJson.Serialize(writer, Document(data));
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        var streamed = Write(ChartData.Stream(rows, TypeInfo));
        Assert.That(streamed, Is.EqualTo(Write(ChartData.FromRows(rows, TypeInfo))));
        Assert.That(streamed, Does.Contain("\n  \"data\": {"));
    }

    // source: design §5.1 (host options give the same document) and §13.5 table (host JsonSerializer.Serialize and
    // SerializeAsync with a synchronous source: same bytes, fully buffered)
    [Test]
    public async Task HostSerializer_SyncSource_WritesTheSameBytes()
    {
        var rows = Readings(20);
        var expected = JsonSerializer.Serialize(Document(ChartData.FromRows(rows, TypeInfo)), HostileOptions.Create());
        Assert.That(JsonSerializer.Serialize(Document(ChartData.Stream(rows, TypeInfo)), HostileOptions.Create()), Is.EqualTo(expected));
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, Document(ChartData.Stream(rows, TypeInfo)), HostileOptions.Create());
        Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Is.EqualTo(expected));
    }

    // source: design §13.5 "Creating" (Values and Url are null; the source is not touched) and §13.2 (IsStreamed)
    [Test]
    public void Stream_DoesNotReadTheSource()
    {
        var source = AsyncRows.Of(Readings(3));
        var data = ChartData.Stream(source, TypeInfo);
        Assert.That(data.IsStreamed, Is.True);
        Assert.That(data.Values, Is.Null);
        Assert.That(data.Url, Is.Null);
        Assert.That(source.Enumerations, Is.Zero);
        Assert.That(ChartData.FromRows(Readings(1), TypeInfo).IsStreamed, Is.False);
    }

    // source: design §13.5 "Creating" and §13.10 (the §5.3 metadata refusals when Stream is called, before any row is read)
    [Test]
    public void Stream_MetadataThatDropsNulls_Throws()
    {
        Assert.That(() => ChartData.Stream([new GdpRow(1, "CZ")], NullDroppingContext.Default.GdpRow), Throws.ArgumentException);
        Assert.That(() => ChartData.Stream(AsyncRows.Iterate(new[] { new NullDroppingRow(null, 1) })), Throws.ArgumentException);
        Assert.That(() => ChartData.Stream((IEnumerable<Reading>)null!, TypeInfo), Throws.ArgumentNullException);
    }

    // source: design §13.6 (static shape: the keys every row will have, from the metadata, in metadata order rearranged
    // by the Object.keys rule, equal to the materialised FirstRowKeys)
    [Test]
    public void FirstRowKeys_StaticShape_EqualsTheMaterialisedKeys()
    {
        Assert.That(ChartData.Stream(AsyncRows.Iterate(Array.Empty<Reading>()), TypeInfo).FirstRowKeys,
            Is.EqualTo(ChartData.FromRows(Readings(1), TypeInfo).FirstRowKeys));
        Assert.That(ChartData.Stream(Array.Empty<Reading>(), TypeInfo).FirstRowKeys, Is.EqualTo(new[] { "id", "day", "station", "value" }));
        var stations = new[] { new StationReading() };
        Assert.That(ChartData.Stream(stations, StreamingContext.Default.StationReading).FirstRowKeys,
            Is.EqualTo(ChartData.FromRows(stations, StreamingContext.Default.StationReading).FirstRowKeys));
        Assert.That(ChartData.Stream(stations).FirstRowKeys, Is.EqualTo(new[] { "2", "10", "station" }));
    }

    // source: design §13.6 (a dictionary or object row has a dynamic shape: the streamed FirstRowKeys is empty)
    [Test]
    public void FirstRowKeys_DynamicShape_IsEmpty()
    {
        Assert.That(ChartData.Stream([new Dictionary<string, object> { ["a"] = 1 }], StreamingContext.Default.DictionaryStringObject).FirstRowKeys, Is.Empty);
        Assert.That(ChartData.Stream(new object[] { new Reading(default, "", null, 1) }, StreamingContext.Default.Object).FirstRowKeys, Is.Empty);
    }

    // source: design §13.5 table (builder entry with streamed data: ArgumentException; a builder's documents are reusable)
    [Test]
    public void BuilderEntry_StreamedData_Throws()
    {
        var data = ChartData.Stream(Readings(1), TypeInfo);
        Assert.That(() => Chart.Line(data), Throws.ArgumentException.With.Message.Contains("BindStream"));
        Assert.That(() => Chart.Of(ChartType.LineChart, data), Throws.ArgumentException.With.Message.Contains("BindStream"));
    }

    // source: design §13.10 (cancellation: the token reaches the source and every WriteAsync and is checked before each
    // row; OperationCanceledException; the enumerator is disposed)
    [Test]
    public void SerializeAsync_CancelledMidSequence_ThrowsAndDisposesTheSource()
    {
        using var cancellation = new CancellationTokenSource();
        var source = AsyncRows.Of(Readings(10_000), i =>
        {
            if (i == 2000)
            {
                cancellation.Cancel();
            }
        });
        var stream = new RecordingStream();

        Assert.That(async () => await FlintJson.SerializeAsync(stream, Document(ChartData.Stream(source, TypeInfo)), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Token, Is.EqualTo(cancellation.Token));
        Assert.That(source.Yielded, Is.EqualTo(2001));
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(() => JsonDocument.Parse(stream.Bytes), Throws.InstanceOf<JsonException>());
    }

    // source: design §13.10 (source exceptions propagate unchanged; the data stays claimed)
    [Test]
    public void SerializeAsync_SourceThrows_PropagatesAndStaysClaimed()
    {
        var failure = new IOException("page 3 failed");
        var input = Document(ChartData.Stream(AsyncRows.Of(Readings(5), i =>
        {
            if (i == 3)
            {
                throw failure;
            }
        }), TypeInfo));

        Assert.That(async () => await FlintJson.SerializeAsync(new RecordingStream(), input), Throws.Exception.SameAs(failure));
        Assert.That(async () => await FlintJson.SerializeAsync(new RecordingStream(), input), Throws.InvalidOperationException);
    }

    // source: design §13.10 (value-dependent omission throws ArgumentException at the offending row during the write)
    [Test]
    public void SerializeAsync_ValueDependentShouldSerialize_ThrowsAtTheRow()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Name == "Note"))
            {
                property.ShouldSerialize = (_, value) => value is null;
            }
        });
        var typeInfo = (JsonTypeInfo<NoteRow>)new JsonSerializerOptions { TypeInfoResolver = resolver }.GetTypeInfo(typeof(NoteRow));
        NoteRow[] good = [new("A", null), new("B", null)];
        Assert.That(FlintJson.Serialize(Document(ChartData.Stream(good, typeInfo))), Is.EqualTo(FlintJson.Serialize(Document(ChartData.FromRows(good, typeInfo)))));

        var input = Document(ChartData.Stream(AsyncRows.Iterate(new[] { new NoteRow("A", null), new NoteRow("A", "x") }), typeInfo));
        Assert.That(async () => await FlintJson.SerializeAsync(new RecordingStream(), input), Throws.ArgumentException.With.Message.Contains("'Note'"));
    }

    // source: design §13.5 (argument checks)
    [Test]
    public void SerializeAsync_NullArguments_Throw()
    {
        Assert.That(() => FlintJson.SerializeAsync(null!, Document(ChartData.FromRows(Readings(1), TypeInfo))), Throws.ArgumentNullException);
        Assert.That(() => FlintJson.SerializeAsync(new MemoryStream(), null!), Throws.ArgumentNullException);
        Assert.That(() => FlintJson.Serialize((Stream)null!, Document(ChartData.FromRows(Readings(1), TypeInfo))), Throws.ArgumentNullException);
    }
}
