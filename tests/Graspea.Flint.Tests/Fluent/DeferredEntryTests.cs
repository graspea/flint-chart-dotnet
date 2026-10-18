using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Rows;
using Graspea.Flint.Tests.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.Tests.Fluent;

public sealed class DeferredEntryTests
{
    private const string Calls = """
        using System.Collections.Generic;
        using System.Text.Json.Serialization.Metadata;
        using Graspea.Flint;
        namespace Probe;
        public sealed record Sale(int Month);
        public static class Use
        {
            public static object Deferred() => Chart.Line<Sale>();
            public static object DeferredTyped(JsonTypeInfo<Sale> typeInfo) => Chart.Line(typeInfo);
            public static object Rows(List<Sale> rows) => Chart.Line(rows);
            public static object RowsTyped(List<Sale> rows, JsonTypeInfo<Sale> typeInfo) => Chart.Line(rows, typeInfo);
            public static object Data(ChartData data) => Chart.Line(data);
        }
        """;

    private static readonly Reading[] Rows =
    [
        new(new DateOnly(2024, 1, 1), "Coastal", 17.5, 1),
        new(new DateOnly(2024, 1, 2), "Inland", null, 2),
    ];

    // source: design §13.3.2 overload resolution (Chart.Line<Sale>() binds to the deferred RowOptions entry,
    // Chart.Line(typeInfo) to the deferred JsonTypeInfo entry and infers TRow; rows, rows + typeInfo and data bind as before)
    [Test]
    public void Entries_BindAsDesigned()
    {
        var compilation = CompileProbe.Compile(LanguageVersion.CSharp12, Calls);
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()), Is.Empty);
        Assert.That(CompileProbe.BoundParameterLists(compilation, "Line"), Is.EqualTo(new[]
        {
            "RowOptions", "JsonTypeInfo", "IEnumerable,RowOptions", "IEnumerable,JsonTypeInfo", "ChartData",
        }));
    }

    // source: design §13.3.2 and §4.2 (a literal null stays ambiguous, CS0121, after the rows and without them)
    [TestCase("Chart.Line(new List<Sale>(), null)")]
    [TestCase("Chart.Line<Sale>(null)")]
    public void LiteralNull_IsStillAmbiguous(string call)
    {
        var source = Calls.Replace("public static object Data(ChartData data) => Chart.Line(data);", $"public static object Null() => {call};");
        Assert.That(CompileProbe.Diagnostics(LanguageVersion.CSharp12, source).Select(d => d.Id), Has.Member("CS0121"));
    }

    // source: design §13.3.2 (a deferred builder: selectors resolve through the captured metadata; Build() has no data;
    // ToJson() writes no data member; Validate reports data_missing)
    [Test]
    public void DeferredBuilder_BuildsADocumentWithoutData()
    {
        var builder = Chart.Line(StreamingContext.Default.Reading).X(r => r.Day).Y(r => r.Value);
        var built = builder.Build();
        Assert.That(built.Data, Is.Null);
        Assert.That(built.ChartSpec.Encodings["x"].Encodings[0].Field, Is.EqualTo("day"));
        Assert.That(builder.ToJson(), Is.EqualTo("""{"chart_spec":{"chartType":"Line Chart","encodings":{"x":{"field":"day"},"y":{"field":"value"}}}}"""));
        Assert.That(builder.Validate(FlintBackend.VegaLite).Issues.Select(i => i.Code), Has.Member("data_missing"));
        Assert.That(builder.ToTemplate().Validate(FlintBackend.VegaLite).Issues.Select(i => i.Code), Has.No.Member("data_missing"));
    }

    // source: design §13.3.3 (the deferred chain plus ToTemplate().Bind(rows) equals the immediate chain)
    [Test]
    public void DeferredBuilder_TemplateBindEqualsTheImmediateChain()
    {
        var bound = Chart.Line(StreamingContext.Default.Reading).X(r => r.Day).Y(r => r.Value).Title("T").ToTemplate().Bind(Rows);
        var immediate = Chart.Line(Rows, StreamingContext.Default.Reading).X(r => r.Day).Y(r => r.Value).Title("T").Build();
        Assert.That(JsonNode.DeepEquals(FlintJson.SerializeToNode(bound), FlintJson.SerializeToNode(immediate)), Is.True);

        var reflection = Chart.Line<Reading>().X(r => r.Day).Y(r => r.Value).Title("T").ToTemplate().Bind(Rows);
        Assert.That(FlintJson.Serialize(reflection), Is.EqualTo(Chart.Line(Rows).X(r => r.Day).Y(r => r.Value).Title("T").ToJson()));
    }

    // source: design §13.3.2 (Chart.Of gains the two deferred overloads)
    [Test]
    public void Of_Deferred_BuildsWithoutData()
    {
        var typed = Chart.Of(ChartType.BarChart, StreamingContext.Default.Reading).Encode(Channel.X, r => r.Station).Build();
        Assert.That(typed.Data, Is.Null);
        Assert.That(typed.ChartSpec.Encodings["x"].Encodings[0].Field, Is.EqualTo("station"));
        var reflection = Chart.Of<Reading>(ChartType.BarChart).Encode(Channel.X, r => r.Station).Build();
        Assert.That(reflection.ChartSpec.Encodings["x"].Encodings[0].Field, Is.EqualTo("station"));
        Assert.That(() => Chart.Of<Reading>(default), Throws.ArgumentException);
    }

    // source: design §13.10 (metadata errors are ArgumentException when a deferred entry is called)
    [Test]
    public void DeferredEntries_MetadataThatDropsNulls_Throws()
    {
        Assert.That(() => Chart.Line(NullDroppingContext.Default.GdpRow), Throws.ArgumentException);
        Assert.That(() => Chart.Line<NullDroppingRow>(), Throws.ArgumentException);
        Assert.That(() => Chart.Of(ChartType.BarChart, NullDroppingContext.Default.GdpRow), Throws.ArgumentException);
        Assert.That(() => Chart.Line((System.Text.Json.Serialization.Metadata.JsonTypeInfo<Reading>)null!), Throws.ArgumentNullException);
    }

    // source: design §13.3.3 (a template from a deferred builder streams with BindStream and FlintJson.SerializeAsync)
    [Test]
    public async Task DeferredBuilder_StreamsThroughTheTemplate()
    {
        var template = Chart.Line(StreamingContext.Default.Reading).X(r => r.Day).Y(r => r.Value).ToTemplate();
        var stream = new RecordingStream(asyncOnly: true);
        await FlintJson.SerializeAsync(stream, template.BindStream(AsyncRows.Iterate(Rows)));
        Assert.That(stream.Text, Is.EqualTo(FlintJson.Serialize(template.Bind(Rows))));
        Assert.That(ChartValidator.Validate(template.Bind(Rows), FlintBackend.VegaLite).IsValid, Is.True);
    }
}
