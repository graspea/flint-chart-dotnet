using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Rows;
using Graspea.Flint.Tests.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Templates;

public sealed class ChartTemplateTests
{
    private static readonly Reading[] Rows =
    [
        new(new DateOnly(2024, 1, 1), "Coastal", 17.5, 1),
        new(new DateOnly(2024, 1, 2), "Inland", null, 2),
    ];

    private static readonly Reading[] Other = [new(new DateOnly(2023, 5, 5), "Other", 1, 9)];

    /// <summary>The chain every test compares with, on the given rows.</summary>
    private static LineChartBuilder<Reading> Chain(IEnumerable<Reading> rows) =>
        Chart.Line(rows, StreamingContext.Default.Reading)
            .X(r => r.Day, SemanticType.Date)
            .Y(r => r.Value)
            .Color(r => r.Station)
            .Title("Readings")
            .Options(o => o.AddTooltips = true);

    private static JsonNode Node(ChartAssemblyInput input) => JsonNode.Parse(FlintJson.Serialize(input))!;

    // source: design §13.3.3 and §13.13 item 3 (Bind(rows) equals the equivalent immediate chain; the builder's own data
    // is not carried)
    [Test]
    public void Bind_Rows_EqualsTheImmediateChain()
    {
        var template = Chain(Other).ToTemplate();
        Assert.That(JsonNode.DeepEquals(Node(template.Bind(Rows)), Node(Chain(Rows).Build())), Is.True);
    }

    // source: design §13.3.3 (Options and Configure actions run once, when the template is created, not on every bind;
    // later changes to the builder do not reach the template)
    [Test]
    public void ToTemplate_RunsOptionsAndConfigureOnce()
    {
        var options = 0;
        var configure = 0;
        var builder = Chain(Other).Options(_ => options++).Configure(_ => configure++);
        var template = builder.ToTemplate();
        builder.Title("Changed later");
        template.Bind(Rows);
        template.Bind(Rows);
        Assert.That((options, configure), Is.EqualTo((1, 1)));
        Assert.That(template.Bind(Rows).ChartSpec.Title, Is.EqualTo("Readings"));
    }

    // source: design §13.3.3 and §13.9 (immutable and thread-safe: eight threads binding concurrently get equal documents)
    [Test]
    public void Bind_ConcurrentThreads_GetEqualDocuments()
    {
        var template = Chain(Other).ToTemplate();
        var expected = FlintJson.Serialize(Chain(Rows).Build());
        var results = new ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (var i = 0; i < 50; i++)
            {
                results.Add(FlintJson.Serialize(template.Bind(Rows)));
            }
        });
        Assert.That(results, Has.Count.EqualTo(400).And.All.EqualTo(expected));
    }

    // source: design §13.3.3 (Bind reads the stored bytes into a new document, so documents never share an object)
    [Test]
    public void Bind_Twice_SharesNoObject()
    {
        var template = Chain(Other).ToTemplate();
        var first = template.Bind(Rows);
        var second = template.Bind(Rows);
        Assert.That(second.ChartSpec, Is.Not.SameAs(first.ChartSpec));
        Assert.That(second.Data, Is.Not.SameAs(first.Data));
        first.ChartSpec.Title = "Changed";
        first.ChartSpec.Encodings["x"].Encodings[0].Field = "changed";
        first.SemanticTypes!.Clear();
        first.Data!.Values!.Clear();
        Assert.That(FlintJson.Serialize(second), Is.EqualTo(FlintJson.Serialize(template.Bind(Rows))));
    }

    // source: design §13.3.3 (Bind, BindAsync and BindStream throw InvalidOperationException without row metadata, as
    // for a template from Chart.Line(ChartData); Bind(ChartData) always works)
    [Test]
    public void Bind_WithoutMetadata_ThrowsAndBindDataWorks()
    {
        var template = Chart.Line(ChartData.FromUrl("https://example.org/rows.json")).X("day").ToTemplate();
        object[] rows = [new Reading(default, "", null, 1)];
        Assert.That(() => template.Bind(rows), Throws.InvalidOperationException.With.Message.Contains("no row metadata"));
        Assert.That(() => template.BindAsync(AsyncRows.Iterate(rows)), Throws.InvalidOperationException);
        Assert.That(() => template.BindStream(rows), Throws.InvalidOperationException);
        Assert.That(() => template.BindStream(AsyncRows.Iterate(rows)), Throws.InvalidOperationException);
        var bound = template.Bind(ChartData.FromUrl("https://example.org/other.json"));
        Assert.That(bound.Data!.Url, Is.EqualTo("https://example.org/other.json"));
    }

    public static IEnumerable<TestCaseData> SpecData()
    {
        yield return new TestCaseData((Func<ChartData>)(() => ChartData.FromRows(Other, StreamingContext.Default.Reading))).SetArgDisplayNames("materialised");
        yield return new TestCaseData((Func<ChartData>)(() => ChartData.FromUrl("https://example.org/rows.json"))).SetArgDisplayNames("url");
        yield return new TestCaseData((Func<ChartData>)(() => ChartData.Stream(AsyncRows.Iterate(Other), StreamingContext.Default.Reading))).SetArgDisplayNames("streamed");
    }

    // source: design §13.3.3 and §13.13 item 3 (Create ignores spec.Data in any state, leaving streamed data unclaimed,
    // and does not mutate spec)
    [TestCaseSource(nameof(SpecData))]
    public async Task Create_IgnoresSpecDataAndLeavesSpecUnchanged(Func<ChartData> createData)
    {
        var data = createData();
        var spec = Chain(Other).Build();
        spec.Data = data;
        var before = spec.Data.IsStreamed ? null : FlintJson.Serialize(spec);

        var template = ChartTemplate.Create(spec, StreamingContext.Default.Reading);

        Assert.That(spec.Data, Is.SameAs(data));
        if (before is not null)
        {
            Assert.That(FlintJson.Serialize(spec), Is.EqualTo(before));
        }
        else
        {
            await FlintJson.SerializeAsync(new RecordingStream(), spec);
        }

        Assert.That(FlintJson.Serialize(template.Bind(Rows)), Is.EqualTo(FlintJson.Serialize(Chain(Rows).Build())));
    }

    // source: design §13.3.3 (a spec read from a JSON file at run time: ChartTemplate.Create(FlintJson.Deserialize(json), typeInfo))
    [Test]
    public void Create_FromASpecWithoutData_BindsRows()
    {
        var json = """{"chart_spec":{"chartType":"Line Chart","encodings":{"x":{"field":"day"},"y":{"field":"value"}}}}""";
        var bound = ChartTemplate.Create(FlintJson.Deserialize(json), StreamingContext.Default.Reading).Bind(Rows);
        Assert.That(bound.Data!.FirstRowKeys, Is.EqualTo(new[] { "id", "day", "station", "value" }));
        Assert.That(FlintJson.Serialize(bound), Does.StartWith("""{"data":{"values":[{"id":1,"day":"2024-01-01","""));
        var reflection = ChartTemplate.Create<Reading>(FlintJson.Deserialize(json)).Bind(Rows);
        Assert.That(reflection.Data!.FirstRowKeys, Is.EqualTo(ChartData.FromRows(Rows).FirstRowKeys));
    }

    // source: design §13.10 (metadata errors are ArgumentException when ChartTemplate.Create is called)
    [Test]
    public void Create_MetadataThatDropsNulls_Throws()
    {
        var spec = FlintJson.Deserialize("""{"chart_spec":{"chartType":"Bar Chart","encodings":{}}}""");
        Assert.That(() => ChartTemplate.Create(spec, NullDroppingContext.Default.GdpRow), Throws.ArgumentException);
        Assert.That(() => ChartTemplate.Create<NullDroppingRow>(spec), Throws.ArgumentException);
        Assert.That(() => ChartTemplate.Create(null!, StreamingContext.Default.Reading), Throws.ArgumentNullException);
    }

    // source: design §13.3.3 and §13.7 (BindAsync materialises; BindStream plus SerializeAsync streams; every path gives
    // the document of Bind)
    [Test]
    public async Task BindAsyncAndBindStream_WriteTheDocumentOfBind()
    {
        var template = Chain(Other).ToTemplate();
        var expected = FlintJson.Serialize(template.Bind(Rows));

        var materialised = await template.BindAsync(AsyncRows.Iterate(Rows));
        Assert.That(materialised.Data!.IsStreamed, Is.False);
        Assert.That(FlintJson.Serialize(materialised), Is.EqualTo(expected));

        var asyncStream = new RecordingStream(asyncOnly: true);
        await FlintJson.SerializeAsync(asyncStream, template.BindStream(AsyncRows.Iterate(Rows)));
        Assert.That(asyncStream.Text, Is.EqualTo(expected));

        var syncStream = new RecordingStream();
        await FlintJson.SerializeAsync(syncStream, template.BindStream(Rows));
        Assert.That(syncStream.Text, Is.EqualTo(expected));
        Assert.That(template.BindStream(Rows).Data!.IsStreamed, Is.True);
    }

    // source: design §13.10 (cancellation of BindAsync throws OperationCanceledException and returns nothing)
    [Test]
    public void BindAsync_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var template = Chain(Other).ToTemplate();
        Assert.That(async () => await template.BindAsync(AsyncRows.Of(Rows), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    // source: design §13.11 (Bind: no second enumeration; a counting source is enumerated once) and §13.5 (BindStream
    // does not touch the source)
    [Test]
    public async Task Bind_EnumeratesOnceAndBindStreamNotAtAll()
    {
        var template = Chain(Other).ToTemplate();
        var source = AsyncRows.Of(Rows);
        await template.BindAsync(source);
        Assert.That(source.Enumerations, Is.EqualTo(1));
        var streamed = AsyncRows.Of(Rows);
        template.BindStream(streamed);
        Assert.That(streamed.Enumerations, Is.Zero);
    }

    // source: design §13.6 (template validation: spec rules run, key rules on the metadata keys, no data_missing,
    // rows_not_inspected reported)
    [Test]
    public void Validate_UsesTheMetadataKeysAndReportsNoDataMissing()
    {
        var template = Chain(Other).Encode(Channel.Size, "nope").ToTemplate();
        var codes = template.Validate(FlintBackend.VegaLite).Issues.Select(i => (i.Code, i.Severity)).ToArray();
        Assert.That(codes, Has.Member(("unknown_field", IssueSeverity.Warning)));
        Assert.That(codes, Has.Member(("rows_not_inspected", IssueSeverity.Info)));
        Assert.That(codes.Select(c => c.Code), Has.No.Member("data_missing"));
        Assert.That(Chain(Other).ToTemplate().Validate().IsValid, Is.True);

        var series = Chain(Other).Series(Channel.Y, "value", "profit").Encode(Channel.Color, (ChartEncoding)new()).ToTemplate();
        Assert.That(series.Validate(FlintBackend.VegaLite).Issues.Single(i => i.Code == "series_field_missing").Severity, Is.EqualTo(IssueSeverity.Warning));
    }

    // source: design §13.6 (without metadata, the key rules are skipped)
    [Test]
    public void Validate_WithoutMetadata_SkipsTheKeyRules()
    {
        var template = Chart.Line(ChartData.FromUrl("https://example.org/rows.json")).X("nope").Y("value").ToTemplate();
        var codes = template.Validate(FlintBackend.VegaLite).Issues.Select(i => i.Code).ToArray();
        Assert.That(codes, Has.No.Member("unknown_field").And.No.Member("data_missing").And.No.Member("data_url_not_fetched"));
        Assert.That(codes, Has.Member("rows_not_inspected"));
    }

    // source: design §13.6 and docs/row-data.md (a sample is validated with every rule through Bind)
    [Test]
    public void ValidateASample_ThroughBind_RunsTheRowRules()
    {
        var template = Chain(Other).ToTemplate();
        Reading?[] sample = [Rows[0], null];
        var result = ChartValidator.Validate(template.Bind(sample!), FlintBackend.VegaLite);
        Assert.That(result.Issues.Select(i => i.Code), Has.Member("row_not_object"));
    }
}
