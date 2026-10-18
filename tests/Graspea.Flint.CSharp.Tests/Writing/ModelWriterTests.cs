using Graspea.Flint.CSharp;
using static Graspea.Flint.CSharp.Tests.Writing.FluentWriterTests;

namespace Graspea.Flint.CSharp.Tests.Writing;

public sealed class ModelWriterTests
{
    private static string Model(string json, DataMode mode = DataMode.Inline) => Convert(json, mode, CodeStyle.Model);

    // source: design §12.4.2 (one object initialiser of ChartAssemblyInput in the shape of the §4.1 example: Data from the
    // rows, SemanticTypes, ChartSpec with ChartType, Encodings with the string shorthand, a ChartEncoding initialiser and
    // EncodingValue.Series, ChartProperties as JSON values, ThemeSpec, Options, FieldDisplayNames, Extensions)
    [Test]
    public void Convert_WritesOneInitialiser()
    {
        var code = Model(Doc(
            "Line Chart",
            "{\"x\":\"a\",\"y\":{\"field\":\"b\",\"aggregate\":\"sum\"},\"color\":[\"a\",\"b\"]}",
            extra: "\"semantic_types\":{\"a\":\"Category\"},\"theme_spec\":\"economist\",\"options\":{\"maxStretch\":1.5},\"field_display_names\":{\"a\":\"A\"},\"zz\":[1],",
            specExtra: "\"chartProperties\":{\"interpolate\":\"monotone\",\"n\":4,\"o\":{\"k\":null},\"x\":null},"));

        Assert.That(code, Does.Contain("""
                public static ChartAssemblyInput Build() =>
                    new ChartAssemblyInput
                    {
                        Data = ChartData.FromRows(Rows),
                        SemanticTypes = new()
                        {
                            ["a"] = SemanticType.Category,
                        },
                        ChartSpec = new ChartSpec
                        {
                            ChartType = ChartType.LineChart,
                            ChartProperties = new()
                            {
                                ["interpolate"] = "monotone",
                                ["n"] = 4,
                                ["o"] = JsonNode.Parse(""\"{"k":null}""\"),
                                ["x"] = null,
                            },
                            Encodings =
                            {
                                ["x"] = "a",
                                ["y"] = new ChartEncoding { Field = "b", Aggregate = Aggregate.Sum },
                                ["color"] = EncodingValue.Series(new ChartEncoding { Field = "a" }, new ChartEncoding { Field = "b" }),
                            },
                        },
                        ThemeSpec = ThemePreset.Economist,
                        Options = new AssembleOptions { MaxStretch = 1.5 },
                        FieldDisplayNames = new()
                        {
                            ["a"] = "A",
                        },
                        Extensions = new()
                        {
                            ["zz"] = Json(""\"[1]""\"),
                        },
                    };
            """.Replace("\"\"\\\"", "\"\"\"").Replace("\r\n", "\n")));
    }

    // source: design §12.4.2 (the model style expresses every case directly: a null channel is null!, a null member goes into
    // the owning object's Extensions)
    [Test]
    public void Convert_Nulls()
    {
        var code = Model(Doc("Bar Chart", "{\"x\":{\"field\":null},\"color\":null}", specExtra: "\"title\":null,"));

        Assert.That(code, Does.Contain("[\"x\"] = new ChartEncoding { Extensions = new() { [\"field\"] = Json(\"null\") } },"));
        Assert.That(code, Does.Contain("[\"color\"] = null!,"));
        Assert.That(code, Does.Contain("[\"title\"] = Json(\"null\"),"));
    }

    // source: design §12.5.2 and §13.3.3 (parameter: the template from the model, ChartTemplate.Create<Row>(spec))
    [Test]
    public void Convert_Parameter_CreatesTheTemplateFromTheModel()
    {
        var code = Model(Doc("Bar Chart", "{\"x\":\"a\"}"), DataMode.Parameter);

        Assert.That(code, Does.Contain("get => LazyInitializer.EnsureInitialized(ref template, static () =>\n            ChartTemplate.Create<Row>(new ChartAssemblyInput\n            {\n                ChartSpec = new ChartSpec\n"));
        Assert.That(code, Does.Not.Contain("Data ="));
    }

    // source: design §12.3 fidelity rule (data members ChartData does not model are set on the built document's data)
    [Test]
    public void Convert_DataExtensions_CompleteTheDocument()
    {
        var json = Doc("Bar Chart", "{\"x\":\"a\"}").Replace("\"data\":{\"values\":[{\"a\":\"x\",\"b\":1}]}", "\"data\":{\"values\":[{\"a\":\"x\",\"b\":1}],\"format\":\"json\"}");

        Assert.That(Model(json), Does.Contain("    public static ChartAssemblyInput Build()\n    {\n        var input =\n            new ChartAssemblyInput\n"));
        Assert.That(Model(json), Does.Contain("        };\n        input.Data!.Extensions = new() { [\"format\"] = Json(\"\\\"json\\\"\") };\n        return input;\n    }\n"));
        Assert.That(Model(json, DataMode.Parameter), Does.Contain("public static ChartAssemblyInput Build(IEnumerable<Row> rows) => WithData(Template.Bind(rows));"));
        Assert.That(Model(json, DataMode.Parameter), Does.Contain("    private static ChartAssemblyInput WithData(ChartAssemblyInput input)\n    {\n        input.Data!.Extensions = new() { [\"format\"] = Json(\"\\\"json\\\"\") };\n        return input;\n    }\n"));
    }
}
