using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed record CityRow(string City, long Revenue, int Cost);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CityRow))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal sealed partial class CityContext : JsonSerializerContext;

public sealed partial class ChartValidatorTests
{
    private static CityRow[] CityRows() => [new("Seattle", 501479, 10), new("Boston", 300000, 20)];

    /// <summary>The probes' Bar Chart with its rows streamed through source-generated metadata.</summary>
    private static ChartAssemblyInput StreamedBar(Action<ChartAssemblyInput>? change = null, IAsyncEnumerable<CityRow>? rows = null)
    {
        var input = Bar(change);
        input.Data = ChartData.Stream(rows ?? AsyncRows.Iterate(CityRows()), CityContext.Default.CityRow);
        return input;
    }

    private static ChartAssemblyInput StreamedLine(Action<ChartAssemblyInput>? change = null, IAsyncEnumerable<CityRow>? rows = null)
    {
        var input = StreamedBar(rows: rows);
        input.ChartSpec.ChartType = ChartType.LineChart;
        change?.Invoke(input);
        return input;
    }

    // source: design §13.6 (key rules run on the metadata keys of a streamed document with a static shape: unknown_field
    // as for a materialised one)
    [Test]
    public void Streamed_MetadataKeysDriveUnknownField()
    {
        AssertOne(StreamedBar(i => i.ChartSpec.Encodings["y"] = "profit"), FlintBackend.VegaLite, "unknown_field", IssueSeverity.Warning);
        AssertNone(StreamedBar(), FlintBackend.VegaLite, "unknown_field");
    }

    // source: design §13.6 (series_field_missing from the metadata keys is a Warning on a streamed document: upstream throws
    // only when data has rows, and a streamed source may yield none)
    [Test]
    public void Streamed_StaticSeriesFieldMissing_IsAWarning()
    {
        var input = StreamedLine(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series([new ChartEncoding { Field = "revenue" }, new ChartEncoding { Field = "profit" }]));
        var issue = Issues(input, FlintBackend.VegaLite, "series_field_missing").Single();
        Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
        Assert.That(issue.Message, Does.Contain("an Error once rows exist"));
        Assert.That(ChartValidator.Validate(input, FlintBackend.VegaLite).IsValid, Is.True);
        AssertNone(StreamedLine(i => i.ChartSpec.Encodings["y"] = EncodingValue.Series([new ChartEncoding { Field = "revenue" }, new ChartEncoding { Field = "cost" }])),
            FlintBackend.VegaLite, "series_field_missing");
    }

    // source: design §13.6 test (an empty asynchronous source with a static-series field missing from the metadata: one
    // series_field_missing Warning, FirstRowKeys equal to the metadata keys, and an output of "values":[])
    [Test]
    public async Task Streamed_EmptySource_HasMetadataKeysAndAWarning()
    {
        var input = StreamedLine(
            i => i.ChartSpec.Encodings["y"] = EncodingValue.Series([new ChartEncoding { Field = "revenue" }, new ChartEncoding { Field = "profit" }]),
            AsyncRows.Iterate(Array.Empty<CityRow>()));
        Assert.That(input.Data!.FirstRowKeys, Is.EqualTo(new[] { "city", "revenue", "cost" }));
        Assert.That(Issues(input, null, "series_field_missing").Select(i => i.Severity), Is.EqualTo(new[] { IssueSeverity.Warning }));

        var stream = new RecordingStream();
        await FlintJson.SerializeAsync(stream, input);
        Assert.That(stream.Text, Does.StartWith("""{"data":{"values":[]},"""));
    }

    // source: design §13.6 (a dynamic row shape skips the key rules)
    [Test]
    public void Streamed_DynamicShape_SkipsTheKeyRules()
    {
        var input = Line(i =>
        {
            i.ChartSpec.Encodings["x"] = "nope";
            i.ChartSpec.Encodings["y"] = EncodingValue.Series([new ChartEncoding { Field = "revenue" }, new ChartEncoding { Field = "profit" }]);
            i.SemanticTypes = new() { ["zz"] = SemanticType.Amount };
        });
        input.Data = ChartData.Stream(AsyncRows.Iterate(new[] { new Dictionary<string, object> { ["city"] = "A" } }), CityContext.Default.DictionaryStringObject);
        var codes = ChartValidator.Validate(input, FlintBackend.VegaLite).Issues.Select(i => i.Code).ToArray();
        Assert.That(codes, Has.No.Member("unknown_field").And.No.Member("series_field_missing").And.No.Member("semantic_type_without_field"));
        Assert.That(codes, Has.Member("rows_not_inspected"));
    }

    // source: design §13.6 (rows_not_inspected, an Info at path data naming the skipped families; it never changes IsValid)
    [Test]
    public void Streamed_ReportsRowsNotInspected()
    {
        var result = ChartValidator.Validate(StreamedBar(), FlintBackend.VegaLite);
        var issue = result.Issues.Single(i => i.Code == "rows_not_inspected");
        Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Info));
        Assert.That(issue.Path, Is.EqualTo("data"));
        Assert.That(issue.Message, Does.Contain("row_not_object").And.Contain("ChartValidator.Validate(template.Bind("));
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Select(i => i.Code), Is.EqualTo(new[] { "rows_not_inspected" }));
    }

    // source: design §13.6 (row-content rules are skipped: data_empty, too_many_rows and the data-inferred field types used
    // by the Excel rules; the spec rules, Excel facets among them, still run)
    [Test]
    public void Streamed_SkipsRowContentRulesAndKeepsSpecRules()
    {
        var excel = ChartValidator.Validate(StreamedBar(), FlintBackend.Excel).Issues.Select(i => i.Code).ToArray();
        Assert.That(excel, Has.No.Member("data_empty").And.No.Member("excel_unsupported").And.No.Member("data_url_only"));

        AssertOne(StreamedBar(i => i.ChartSpec.Encodings["column"] = "city"), FlintBackend.Excel, "excel_unsupported", IssueSeverity.Error);
        AssertOne(StreamedBar(i => i.ChartSpec.ChartType = new ChartType("Nope Chart")), null, "unknown_chart_type", IssueSeverity.Error);
        AssertOne(StreamedBar(i => i.SemanticTypes = new() { ["revenue"] = new SemanticAnnotation { Type = new SemanticType("constructor") } }),
            FlintBackend.VegaLite, "reserved_semantic_type", IssueSeverity.Error);
    }

    // source: design §13.6 (validation decides what it can without the rows; the source is not read and the data stays
    // writable)
    [Test]
    public async Task Streamed_ValidationDoesNotReadTheSource()
    {
        var source = AsyncRows.Of(CityRows());
        var input = StreamedBar(rows: source);
        ChartValidator.Validate(input);
        Assert.That(source.Enumerations, Is.Zero);
        var stream = new RecordingStream();
        await FlintJson.SerializeAsync(stream, input);
        Assert.That(source.Enumerations, Is.EqualTo(1));
    }
}
