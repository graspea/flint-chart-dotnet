using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Graspea.Flint;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Readme;

#region readme:sale
public sealed record Sale(DateOnly Month, string Region, decimal Revenue);
#endregion

#region readme:reading
public sealed record Reading([property: JsonPropertyName("station_id")] string StationId, double TemperatureC);
#endregion

/// <summary>
/// Every C# block of README.md (and so of the package AGENTS.md, which copies the "For AI agents" section) is a
/// <c>#region readme:&lt;name&gt;</c> of this file, so CI compiles and runs it. A README block without a region, or a region
/// without a README block, fails <see cref="ReadmeCSharpBlocks_AreTheRegionsOfThisFile"/>.
/// </summary>
public sealed partial class ReadmeExamplesTests
{
    private static readonly Sale[] Sales =
    [
        new(new DateOnly(2024, 1, 1), "North", 1200.50m),
        new(new DateOnly(2024, 2, 1), "North", 1340.00m),
        new(new DateOnly(2024, 1, 1), "South", 980.25m),
        new(new DateOnly(2024, 2, 1), "South", 1105.75m),
    ];

    // source: README.md (the README shows the code and its output; this test keeps the two equal)
    [Test]
    public void QuickStart_WritesTheJsonShownInTheReadme()
    {
        #region readme:quick-start
        Sale[] sales =
        [
            new(new DateOnly(2024, 1, 1), "North", 1200.50m),
            new(new DateOnly(2024, 2, 1), "North", 1340.00m),
            new(new DateOnly(2024, 1, 1), "South", 980.25m),
            new(new DateOnly(2024, 2, 1), "South", 1105.75m),
        ];

        var chart = Chart.Line(sales)
            .X(s => s.Month, SemanticType.YearMonth)
            .Y(s => s.Revenue, SemanticType.Amount)
            .Color(s => s.Region)
            .Title("Monthly revenue");

        chart.Validate(FlintBackend.VegaLite).ThrowIfInvalid();
        string json = chart.ToJson();
        #endregion

        JsonAssert.Equal(BlockAfter("quick-start", "json"), json);
    }

    // source: README.md "Typed model" (the same document as the quick start, written with the model)
    [Test]
    public void TypedModel_WritesTheQuickStartDocument()
    {
        var sales = Sales;

        #region readme:typed-model
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromRows(sales),
            SemanticTypes = new() { ["month"] = SemanticType.YearMonth, ["revenue"] = SemanticType.Amount },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.LineChart,
                Title = "Monthly revenue",
                Encodings = { [Channel.X] = "month", [Channel.Y] = "revenue", [Channel.Color] = "region" },
            },
        };

        string json = FlintJson.Serialize(input);
        #endregion

        JsonAssert.Equal(BlockAfter("quick-start", "json"), json);
    }

    // source: README.md "Row field names" and design §5.3 (camelCase by default, null policy verbatim, [JsonPropertyName]
    // wins, lambdas resolve to the same names)
    [Test]
    public void RowFieldNames_AreTheOnesTheReadmeStates()
    {
        #region readme:row-field-names
        Reading[] readings = [new("ST-01", 21.5), new("ST-02", 19.0)];

        var camel = ChartData.FromRows(readings);                                                      // station_id, temperatureC
        var verbatim = ChartData.FromRows(readings, new RowOptions { PropertyNamingPolicy = null });   // station_id, TemperatureC
        var chart = Chart.Bar(readings).X(r => r.StationId).Y(r => r.TemperatureC).Build();            // fields station_id, temperatureC
        #endregion

        Assert.That(camel.FirstRowKeys, Is.EqualTo(new[] { "station_id", "temperatureC" }));
        Assert.That(verbatim.FirstRowKeys, Is.EqualTo(new[] { "station_id", "TemperatureC" }));
        Assert.That(chart.ChartSpec.Encodings["x"].Encodings[0].Field, Is.EqualTo("station_id"));
        Assert.That(chart.ChartSpec.Encodings["y"].Encodings[0].Field, Is.EqualTo("temperatureC"));
    }

    // source: README.md "In-process pre-flight" and ir/flint-ir.json (Doughnut Chart exists on chartjs only)
    [Test]
    public void Validation_ReportsWhatTheReadmeShows()
    {
        var sales = Sales;
        var console = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            #region readme:validate
            var doughnut = Chart.Doughnut(sales).Size(s => s.Revenue).Color(s => s.Region);

            ValidationResult result = doughnut.Validate(FlintBackend.VegaLite);
            foreach (ValidationIssue issue in result.Issues)
            {
                Console.WriteLine($"{issue.Severity} {issue.Code} at {issue.Path}");
            }
            // Error chart_type_not_on_backend at chart_spec.chartType

            Console.WriteLine(doughnut.Validate(FlintBackend.ChartJs).IsValid); // True
            #endregion
        }
        finally
        {
            Console.SetOut(console);
        }

        Assert.That(writer.ToString().ReplaceLineEndings("\n"), Is.EqualTo("Error chart_type_not_on_backend at chart_spec.chartType\nTrue\n"));
    }

    // source: README.md "For AI agents" (canonical pattern: inline rows, typed model) and design §10.4 test 6 (the minimal
    // Semantic Context document)
    [Test]
    public void AgentsInlineRows_WritesTheStatedJson()
    {
        #region readme:agents-inline-rows
        var input = new ChartAssemblyInput
        {
            Data = ChartData.FromRows(new[]
            {
                new { City = "Seattle", Revenue = 501479 },
                new { City = "Boston", Revenue = 423117 },
            }),
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.BarChart,
                Encodings = { [Channel.X] = "city", [Channel.Y] = "revenue" },
            },
        };
        string json = FlintJson.Serialize(input);
        #endregion

        JsonAssert.Equal(
            """{"data":{"values":[{"city":"Seattle","revenue":501479},{"city":"Boston","revenue":423117}]},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"city"},"y":{"field":"revenue"}}}}""",
            json);
    }

    // source: README.md "For AI agents" (canonical patterns: aggregate in C# first, then bind; round-trip; validate)
    [Test]
    public void AgentsAggregateFirst_RoundTripsAndValidates()
    {
        var sales = Sales;

        #region readme:agents-aggregate-first
        var byRegion = sales
            .GroupBy(s => s.Region)
            .Select(g => new { Region = g.Key, Revenue = g.Sum(s => s.Revenue) })
            .ToList();

        var chart = Chart.Bar(byRegion)
            .X(r => r.Region)
            .Y(r => r.Revenue, SemanticType.Amount);

        chart.Validate(FlintBackend.VegaLite).ThrowIfInvalid();
        string json = chart.ToJson();
        #endregion

        #region readme:agents-round-trip
        ChartAssemblyInput spec = FlintJson.Deserialize(json);
        spec.ChartSpec.Title = "Revenue by region";
        string edited = FlintJson.Serialize(spec);
        #endregion

        JsonAssert.Equal(
            """{"data":{"values":[{"region":"North","revenue":2540.50},{"region":"South","revenue":2086.00}]},"semantic_types":{"revenue":"Amount"},"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"region"},"y":{"field":"revenue"}},"title":"Revenue by region"}}""",
            edited);
    }

    // source: README.md "For AI agents" rule 4 and docs/validation-codes.md (unknown_chart_type is an Error)
    [Test]
    public void AgentsUnknownTemplate_BuildsAndValidationNamesIt()
    {
        var sales = Sales;

        #region readme:agents-unknown-template
        ChartAssemblyInput input = Chart.Of(new ChartType("Image Chart"), sales)
            .Encode(Channel.X, s => s.Region)
            .Build();
        ValidationResult result = ChartValidator.Validate(input); // unknown_chart_type (Error) for the pinned flint-chart
        #endregion

        Assert.That(input.ChartSpec.ChartType.Name, Is.EqualTo("Image Chart"));
        Assert.That(result.Issues, Has.Exactly(1).Matches<ValidationIssue>(i => i.Code == "unknown_chart_type" && i.Severity == IssueSeverity.Error));
    }

    // source: this file's contract (every README C# block is a region here, and every region is in the README)
    [Test]
    public void ReadmeCSharpBlocks_AreTheRegionsOfThisFile()
    {
        var source = File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "tests", "Graspea.Flint.Tests", "Readme", "ReadmeExamplesTests.cs")).ReplaceLineEndings("\n");
        var regions = Region().Matches(source).ToDictionary(m => m.Groups["name"].Value, m => Dedent(m.Groups["body"].Value));
        var fileUsings = Using().Matches(source).Select(m => m.Value).ToHashSet();

        var readmeBlocks = Blocks().Where(b => b.Language == "csharp").Select(b => b.Text).ToList();
        foreach (var block in readmeBlocks)
        {
            var lines = block.Split('\n');
            var usings = lines.TakeWhile(l => l.StartsWith("using ", StringComparison.Ordinal)).ToArray();
            Assert.That(usings, Is.SubsetOf(fileUsings), $"README block uses a namespace this file does not:\n{block}");
            var body = string.Join('\n', lines.Skip(usings.Length)).Trim('\n');
            Assert.That(regions.Values, Has.Member(body), $"README block without a region in this file:\n{body}");
        }

        Assert.That(regions.Values.Where(r => !readmeBlocks.Any(b => b.EndsWith(r, StringComparison.Ordinal))), Is.Empty, "regions missing from README.md");
    }

    private static string Readme() => File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "README.md")).ReplaceLineEndings("\n");

    private static List<(string Language, string Text)> Blocks() =>
        Fence().Matches(Readme()).Select(m => (m.Groups["lang"].Value, m.Groups["body"].Value.TrimEnd('\n'))).ToList();

    /// <summary>The first fenced block of the given language after the README block equal to the named region.</summary>
    private static string BlockAfter(string region, string language)
    {
        var source = File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "tests", "Graspea.Flint.Tests", "Readme", "ReadmeExamplesTests.cs")).ReplaceLineEndings("\n");
        var body = Dedent(Region().Matches(source).Single(m => m.Groups["name"].Value == region).Groups["body"].Value);
        var blocks = Blocks();
        var index = blocks.FindIndex(b => b.Language == "csharp" && b.Text.EndsWith(body, StringComparison.Ordinal));
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"README.md has no block for region {region}.");
        return blocks.Skip(index + 1).First(b => b.Language == language).Text;
    }

    private static string Dedent(string text)
    {
        var lines = text.Trim('\n').Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()).Select(l => l.TrimEnd()));
    }

    [GeneratedRegex(@"^[ \t]*#region readme:(?<name>[a-z-]+)\n(?<body>.*?)\n[ \t]*#endregion", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Region();

    [GeneratedRegex(@"^using [A-Za-z.]+;$", RegexOptions.Multiline)]
    private static partial Regex Using();

    [GeneratedRegex(@"^```(?<lang>[a-z]*)\n(?<body>.*?)^```$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Fence();
}
