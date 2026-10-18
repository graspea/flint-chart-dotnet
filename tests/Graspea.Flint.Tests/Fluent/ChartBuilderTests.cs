using System.Reflection;
using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Rows;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Fluent;

/// <summary>The hand-written core <c>ChartBuilder&lt;TSelf,TRow&gt;</c>, reached through <c>Chart.Of</c> (design §4.2, §10.3 unit: fluent).</summary>
public sealed class ChartBuilderTests
{
    private static readonly DerivedRow[] Derived = [new() { Region = "EU", Gdp = 1.5, Value = 2 }];

    private static JsonNode Spec(ChartAssemblyInput input) => FlintJson.SerializeToNode(input)["chart_spec"]!;

    // source: design §5.3 rule 1 and §10.4 ("the first M2 test"): a member selector resolves to the wire name of the rows,
    // through reflection metadata (RowOptions captured at the entry point) and through a source-generated JsonTypeInfo
    [Test]
    public void Encode_MemberExpression_ResolvesWireNameFromRowMetadata()
    {
        var reflection = Chart.Of(ChartType.BarChart, Derived)
            .Encode(Channel.X, r => r.Gdp)
            .Encode(Channel.Y, r => r.Value)
            .Encode(Channel.Color, r => r.Region)
            .Build();
        JsonAssert.Equal("""{"x":{"field":"GDP per capita"},"y":{"field":"value"},"color":{"field":"region"}}""", Spec(reflection)["encodings"]);

        var verbatim = Chart.Of(ChartType.BarChart, Derived, new RowOptions { PropertyNamingPolicy = null }).Encode(Channel.Y, r => r.Value).Build();
        JsonAssert.Equal("""{"y":{"field":"Value"}}""", Spec(verbatim)["encodings"]);

        var sourceGenerated = Chart.Of(ChartType.BarChart, Derived, RowContext.Default.DerivedRow)
            .Encode(Channel.X, r => r.Gdp)
            .Encode(Channel.Color, r => r.Region)
            .Build();
        JsonAssert.Equal("""{"x":{"field":"GDP per capita"},"color":{"field":"region"}}""", Spec(sourceGenerated)["encodings"]);
        Assert.That(sourceGenerated.Data!.FirstRowKeys, Does.Contain("GDP per capita"));
    }

    // source: design §5.3 rule 1 (anything but a member of the row throws ArgumentException) and §4.2 (data entered as
    // ChartData has no row metadata, so a selector cannot be resolved)
    [Test]
    public void Encode_SelectorThatIsNotARowMember_Throws()
    {
        var builder = Chart.Of(ChartType.BarChart, Derived);
        Assert.That((Action)(() => builder.Encode(Channel.X, r => r.Value + 1)), Throws.ArgumentException);
        Assert.That((Action)(() => builder.Encode(Channel.X, r => r.Hidden)), Throws.ArgumentException);
        Assert.That((Action)(() => Chart.Of(ChartType.BarChart, ChartData.FromJson([])).Encode(Channel.X, r => r.ToString())), Throws.InvalidOperationException);
    }

    // source: design §4.2 (Encode with a field writes {"field":…}; the semantic type goes to semantic_types under the
    // field; a ChartEncoding or EncodingValue is stored as given; the last call per channel wins)
    [Test]
    public void Encode_Overloads_WriteOneEncodingPerChannel()
    {
        var input = Chart.Of(ChartType.BarChart, ChartData.FromUrl("data.json"))
            .Encode(Channel.X, "month", SemanticType.YearMonth)
            .Encode(Channel.Y, new ChartEncoding { Field = "revenue", Aggregate = Aggregate.Sum })
            .Encode(Channel.Color, (EncodingValue)"region")
            .Encode(new Channel("radius"), "size")
            .Encode(Channel.Color, "segment")
            .Build();
        JsonAssert.Equal(
            """
            {"data":{"url":"data.json"},"semantic_types":{"month":"YearMonth"},
             "chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"month"},"y":{"field":"revenue","aggregate":"sum"},
               "color":{"field":"segment"},"radius":{"field":"size"}}}}
            """,
            FlintJson.SerializeToNode(input));
    }

    // source: spike (a) ("X(null) and Encode(ch, null) bind to the ChartEncoding overload and throw ArgumentNullException")
    // and design §4.2 (default(ChartType) and default(Channel) are not values)
    [Test]
    public void Encode_NullOrDefaultArguments_Throw()
    {
        var builder = Chart.Of(ChartType.BarChart, Derived);
        Assert.That((Action)(() => builder.Encode(Channel.X, null!)), Throws.ArgumentNullException);
        Assert.That((Action)(() => builder.Encode(default, "month")), Throws.ArgumentException);
        Assert.That((Action)(() => Chart.Of(default, Derived)), Throws.ArgumentException);
        Assert.That((Action)(() => Chart.Of(default, ChartData.FromUrl("data.json"))), Throws.ArgumentException);
    }

    // source: design §4.2 ("Build(): never validates; fresh document each call"): two calls give two documents that share
    // no mutable state, so changing one leaves the next Build() untouched
    [Test]
    public void Build_ReturnsAFreshDocumentEachCall()
    {
        var builder = Chart.Of(ChartType.LineChart, Derived)
            .Encode(Channel.X, new ChartEncoding { Field = "region" })
            .Annotate("value", new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD" })
            .DisplayName("value", "Value")
            .Property("interpolate", "monotone")
            .Property("bands", new JsonArray(1, 2))
            .Theme(new Theming.ThemeSpec { Id = "house" })
            .Options(o => o.MaxStretch = 2);
        var expected = FlintJson.SerializeToNode(builder.Build()).ToJsonString();

        var first = builder.Build();
        Assert.That(builder.Build(), Is.Not.SameAs(first));
        first.ChartSpec.Encodings["x"].Encodings[0].Field = "changed";
        first.ChartSpec.Encodings["y"] = "added";
        first.ChartSpec.ChartProperties!["interpolate"] = "step";
        first.ChartSpec.ChartProperties["bands"]!.AsArray().Add(3);
        first.SemanticTypes!["value"].Unit = "EUR";
        first.FieldDisplayNames!["value"] = "Changed";
        first.ThemeSpec!.Spec!.Id = "changed";
        first.Options!.MaxStretch = 3;
        first.Data!.Values!.Add(new JsonObject());

        JsonAssert.Equal(expected, FlintJson.SerializeToNode(builder.Build()));
    }

    // source: design §4.2 and §6 ("Build()/ToJson() never validate"): a document every backend rejects still builds and
    // serialises; only Validate reports it
    [Test]
    public void BuildAndToJson_NeverValidate()
    {
        var builder = Chart.Of(new ChartType("Image Chart"), ChartData.FromJson([])).Series(Channel.Y, "a", "b").Series(Channel.X, "c", "d");
        Assert.That((Action)(() => builder.Build()), Throws.Nothing);
        Assert.That((Action)(() => builder.ToJson()), Throws.Nothing);
        Assert.That(builder.Validate().IsValid, Is.False);
    }

    // source: design §4.2 ("Validate: ChartValidator over Build(); the only validation entry") and ir/flint-ir.json
    // (Doughnut Chart exists on chartjs only)
    [Test]
    public void Validate_RunsTheValidatorForTheBackend()
    {
        var builder = Chart.Of(ChartType.DoughnutChart, Derived).Encode(Channel.Color, r => r.Region).Encode(Channel.Y, r => r.Value);
        Assert.That(builder.Validate(FlintBackend.VegaLite).Issues, Has.Exactly(1).Matches<ValidationIssue>(i => i.Code == "chart_type_not_on_backend"));
        Assert.That(builder.Validate(FlintBackend.ChartJs).Issues, Has.None.Matches<ValidationIssue>(i => i.Code == "chart_type_not_on_backend"));
    }

    // source: design §4.2 (ToJson() is FlintJson.Serialize(Build()))
    [Test]
    public void ToJson_IsTheSerialisedBuild()
    {
        var builder = Chart.Of(ChartType.BarChart, Derived).Encode(Channel.X, r => r.Region).Title("GDP");
        Assert.That(builder.ToJson(), Is.EqualTo(FlintJson.Serialize(builder.Build())));
    }

    // source: spike (a) change 2 ("Property(key, null) binds to Property(string, string?); remove the key on null") and
    // design §4.2 (Property(string, JsonNode?): null removes the key); the other keys keep their order
    [Test]
    public void Property_Null_RemovesTheKey()
    {
        var builder = Chart.Of(ChartType.LineChart, Derived)
            .Property("interpolate", "monotone")
            .Property("showPoints", true)
            .Property("strokeWidth", 1.5)
            .Property("bands", JsonNode.Parse("[1,2]"));
        JsonAssert.Equal("""{"interpolate":"monotone","showPoints":true,"strokeWidth":1.5,"bands":[1,2]}""", Spec(builder.Build())["chartProperties"]);

        builder.Property("interpolate", null).Property("bands", (JsonNode?)null).Property("absent", (string?)null).Property("tail", "x");
        Assert.That(Spec(builder.Build())["chartProperties"]!.ToJsonString(), Is.EqualTo("""{"showPoints":true,"strokeWidth":1.5,"tail":"x"}"""));

        builder.Property("showPoints", null).Property("strokeWidth", null).Property("tail", null);
        Assert.That(builder.Build().ChartSpec.ChartProperties, Is.Null);
    }

    // source: design §4.2 (Series: static series, x or y only; y: [{field:"revenue"},{field:"cost"}]) and §6 series_*
    // (fewer than two fields, a duplicate field or an entry without a field throws on the four layout backends, so no
    // document with these can assemble there); upstream normalizeStaticSeries tests
    // !entry.field, so an empty field is missing too, and whitespace is not (review R3-2)
    [Test]
    public void Series_WritesAnArrayOnXOrYOnly()
    {
        var byName = Chart.Of(ChartType.LineChart, Derived).Series(Channel.Y, "revenue", "cost").Build();
        JsonAssert.Equal("""{"y":[{"field":"revenue"},{"field":"cost"}]}""", Spec(byName)["encodings"]);
        Assert.That(byName.ChartSpec.Encodings["y"].IsSeries, Is.True);

        var bySelector = Chart.Of(ChartType.LineChart, Derived).Series(Channel.X, r => r.Gdp, r => r.Value).Build();
        JsonAssert.Equal("""{"x":[{"field":"GDP per capita"},{"field":"value"}]}""", Spec(bySelector)["encodings"]);

        var builder = Chart.Of(ChartType.LineChart, Derived);
        Assert.That((Action)(() => builder.Series(Channel.Color, "a", "b")), Throws.ArgumentException);
        Assert.That((Action)(() => builder.Series(Channel.Y, "a")), Throws.ArgumentException);
        Assert.That((Action)(() => builder.Series(Channel.Y, "a", "a")), Throws.ArgumentException);
        Assert.That((Action)(() => builder.Series(Channel.Y, "a", null!)), Throws.ArgumentException);
        Assert.That((Action)(() => builder.Series(Channel.Y, "", "cost")), Throws.ArgumentException);
        Assert.That(Chart.Of(ChartType.LineChart, Derived).Series(Channel.Y, " ", "cost").Build().ChartSpec.Encodings["y"].IsSeries, Is.True);
        Assert.That((Action)(() => builder.Series(Channel.Y, (string[])null!)), Throws.ArgumentNullException);
        Assert.That(builder.Build().ChartSpec.Encodings, Is.Empty);
    }

    // source: design §4.2 (Series takes params string[] or params Expression[]; a call with no fields or a literal null
    // second argument binds to neither and does not compile, CS0121 in the review probe, so the overload set documents
    // it) and Line_NullSecondArgument_IsAmbiguousAndDocumented (the same check for the entry points)
    [Test]
    public void Series_NoFieldsOrNull_IsAmbiguousAndDocumented()
    {
        var series = typeof(ChartBuilder<,>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Series")
            .ToArray();
        Assert.That(series, Has.Length.EqualTo(2));
        foreach (var method in series)
        {
            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(2));
            Assert.That(parameters[1].IsDefined(typeof(ParamArrayAttribute)), Is.True, method.ToString());
            Assert.That(parameters[1].ParameterType.GetElementType()!.IsValueType, Is.False, method.ToString());
        }

        var source = File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "src", "Graspea.Flint", "Fluent", "ChartBuilder.cs"));
        Assert.That(source, Does.Contain("A call with no fields or a literal <c>null</c> is ambiguous between the two overloads and does not compile"));
    }

    // source: design §4.2 usage (Encoding(Channel.X, e => e.SortBy = SortBy.ExplicitOrder("Jan", "Feb")) configures the
    // bound encoding; aggregate, sort and scheme are set this way, also on a channel without a field)
    [Test]
    public void Encoding_ConfiguresTheChannelsEncoding()
    {
        var input = Chart.Of(ChartType.BarChart, Derived)
            .Encode(Channel.X, "month")
            .Encoding(Channel.X, e => e.SortBy = SortBy.ExplicitOrder("Jan", "Feb"))
            .Encoding(Channel.Y, e => e.Aggregate = Aggregate.Count)
            .Build();
        JsonAssert.Equal("""{"x":{"field":"month","sortBy":"[\"Jan\",\"Feb\"]"},"y":{"aggregate":"count"}}""", Spec(input)["encodings"]);
    }

    // source: design §4.2 (an encoding passed in is not shared with the builder: configuring the channel later does not
    // change the caller's object)
    [Test]
    public void Encoding_DoesNotChangeTheCallersEncoding()
    {
        var mine = new ChartEncoding { Field = "month" };
        Chart.Of(ChartType.BarChart, Derived).Encode(Channel.X, mine).Encoding(Channel.X, e => e.Field = "other").Build();
        Assert.That(mine.Field, Is.EqualTo("month"));
    }

    // source: design §4.1 (wire names title, subtitle, baseSize, canvasSize, semantic_types, field_display_names,
    // theme_spec, options) and §4.2 (each core method is one model mutation; Configure runs at Build(), last)
    [Test]
    public void CoreSetters_EachWriteTheirMember()
    {
        var input = Chart.Of(ChartType.BarChart, Derived)
            .Encode(Channel.X, r => r.Region)
            .Title("GDP").Subtitle("2024")
            .BaseSize(800, 400).CanvasSize(1000, 500)
            .Annotate(r => r.Gdp, SemanticType.Amount)
            .Annotate("value", new SemanticAnnotation { Type = SemanticType.Percentage, IntrinsicDomain = [0, 100] })
            .DisplayName(r => r.Gdp, "GDP")
            .DisplayName("value", "Share")
            .Theme(ThemePreset.Economist)
            .Options(o => o.MaxStretch = 2)
            .Options(o => o.FacetColumns = 3)
            .Configure(i => i.ChartSpec.Title += " (draft)")
            .Build();
        JsonAssert.Equal(
            """
            {"data":{"values":[{"GDP per capita":1.5,"value":2,"region":"EU"}]},
             "semantic_types":{"GDP per capita":"Amount","value":{"semanticType":"Percentage","intrinsicDomain":[0,100]}},
             "chart_spec":{"chartType":"Bar Chart","title":"GDP (draft)","subtitle":"2024","encodings":{"x":{"field":"region"}},
               "baseSize":{"width":800,"height":400},"canvasSize":{"width":1000,"height":500}},
             "theme_spec":"economist","options":{"maxStretch":2,"facetColumns":3},
             "field_display_names":{"GDP per capita":"GDP","value":"Share"}}
            """,
            FlintJson.SerializeToNode(input));

        var cleared = Chart.Of(ChartType.BarChart, Derived).Title("GDP").Title(null).Subtitle("x").Subtitle(null).Build();
        Assert.That((cleared.ChartSpec.Title, cleared.ChartSpec.Subtitle), Is.EqualTo(((string?)null, (string?)null)));
    }

    // source: design §4.2 entry points (Of(ChartType, ChartData) for pre-built values or {url}) and spike (a) change 1 (the
    // string overload is error-marked, so Chart.Of(type, "https://…") does not compile: CS0619)
    [Test]
    public void Of_EntryPoints()
    {
        var input = Chart.Of(new ChartType("Image Chart"), ChartData.FromUrl("https://example.com/d.json")).Build();
        JsonAssert.Equal("""{"data":{"url":"https://example.com/d.json"},"chart_spec":{"chartType":"Image Chart","encodings":{}}}""", FlintJson.SerializeToNode(input));

        var url = typeof(Chart).GetMethod(nameof(Chart.Of), [typeof(ChartType), typeof(string)])!;
        var obsolete = url.GetCustomAttribute<ObsoleteAttribute>();
        Assert.That(obsolete, Is.Not.Null);
        Assert.That(obsolete!.IsError, Is.True);
        Assert.That(obsolete.Message, Does.Contain("ChartData.FromUrl"));
        Assert.That(url.ReturnType, Is.EqualTo(typeof(GenericChartBuilder<object>)));
    }

    // source: design §4.2 extension points (a consumer extension method over the CRTP base infers TSelf and TRow and keeps
    // the concrete builder type)
    [Test]
    public void ExtensionMethodOverTheCore_KeepsTheBuilderType()
    {
        GenericChartBuilder<DerivedRow> builder = Chart.Of(ChartType.BarChart, Derived).WithHouseStyle();
        Assert.That(builder.Build().ThemeSpec!.Preset, Is.EqualTo(ThemePreset.Economist));
    }

    // source: owner decision 2026-10-17 (BoxplotWhiskerMethod.MinMax, renamed through overrides.json, writes the
    // unchanged wire value "minmax")
    [Test]
    public void RenamedEnumMember_WritesTheOriginalWireValue()
    {
        var builder = Chart.Boxplot(Derived).WhiskerMethod(BoxplotWhiskerMethod.MinMax);
        JsonAssert.Equal("""{"whiskerMethod":"minmax"}""", FlintJson.SerializeToNode(builder.Build())["chart_spec"]!["chartProperties"]);
        Assert.That(Enum.GetNames<BoxplotWhiskerMethod>(), Is.EqualTo(new[] { "Default", "Iqr", "MinMax" }));
    }
}

internal static class HouseStyle
{
    public static TSelf WithHouseStyle<TSelf, TRow>(this ChartBuilder<TSelf, TRow> builder)
        where TSelf : ChartBuilder<TSelf, TRow> =>
        builder.Theme(ThemePreset.Economist).BaseSize(800, 400);
}
