using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The Image-Charts channels the validator treats as read, against what the generator's read probe saw
/// <c>assembleImageCharts</c> of the pinned flint-chart read (tests/fixtures/flint-chart@&lt;pin&gt;/probes.json
/// <c>imageChartsReads</c>; generator/src/extract/image-charts-reads.ts). Under every base a chart type assembles from, a
/// probed read is declared by the template, a bound <c>column</c> or <c>row</c> that throws, or exempted from
/// <c>unsupported_channel</c> (<c>ImageCharts.ReadsUndeclared</c>); an undeclared channel the probe saw ignored there is
/// reported.
/// </summary>
public sealed class ImageChartsReadsTests
{
    private static JsonNode Record => CommittedIr.Probes["imageChartsReads"]!;

    private static JsonNode Template(string chartType) =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == chartType)!["backends"]!["image-charts"]!;

    public static IEnumerable<TestCaseData> Bases() =>
        from t in Record["reads"]!.AsObject()
        from b in t.Value!.AsObject()
        select new TestCaseData(t.Key, b.Key).SetName($"ProbedReads_MatchTheValidator({t.Key}, {b.Key})");

    /// <summary>The base's encodings as the probe recorded them, and the channel bound to a field of its own unless the
    /// base binds it; the rows carry the fields of the probe's bases.</summary>
    private static ChartAssemblyInput Document(string chartType, string baseName, string channel)
    {
        var spec = new ChartSpec { ChartType = new ChartType(chartType) };
        foreach (var (c, e) in Record["bases"]![baseName]!.AsObject())
        {
            spec.Encodings[c] = e!["field"]!.GetValue<string>();
        }

        if (!spec.Encodings.ContainsKey(channel))
        {
            spec.Encodings[channel] = "probe";
        }

        return new ChartAssemblyInput
        {
            Data = ChartData.FromJson([
                new JsonObject { ["category"] = "A", ["measure"] = 3, ["measure2"] = 30, ["probe"] = "k1" },
                new JsonObject { ["category"] = "B", ["measure"] = 1, ["measure2"] = 10, ["probe"] = "k2" },
            ]),
            ChartSpec = spec,
        };
    }

    // source: probes.json imageChartsReads (each chart type of IMAGE_CHARTS_TYPE_MAP assembles from a base, and a bound
    // column or row throws under each, assemble.ts:192-194) and ir/flint-ir.json (a curated channel is read,
    // generator/overrides.json imageChartsChannels)
    [Test]
    public void ProbeRecord_CoversTheImageChartsTemplates()
    {
        var withTemplate = CommittedIr.Catalog["chartTypes"]!.AsArray()
            .Where(t => t!["backends"]!["image-charts"] is not null).Select(t => t!["name"]!.GetValue<string>());
        Assert.That(Record["reads"]!.AsObject().Select(p => p.Key), Is.EquivalentTo(withTemplate));
        using (Assert.EnterMultipleScope())
        {
            foreach (var (chartType, bases) in Record["reads"]!.AsObject())
            {
                Assert.That(bases!.AsObject(), Is.Not.Empty, chartType);
                Assert.That(CommittedIr.ImageChartsReads(chartType), Is.SupersetOf(CommittedIr.Strings(Template(chartType)["channels"])),
                    $"{chartType}: every declared channel is read");
                foreach (var (name, read) in bases.AsObject())
                {
                    Assert.That(CommittedIr.Strings(read), Is.SupersetOf(new[] { "column", "row" }), $"{chartType}, {name}: a bound facet throws");
                }
            }
        }
    }

    // source: probes.json imageChartsReads.reads[chartType][base] (assemble.ts:205-210 the series, :265-266 the slice
    // and value fallbacks) against the validator's unsupported_channel and ImageCharts.ReadsUndeclared
    [TestCaseSource(nameof(Bases))]
    public void ProbedReads_MatchTheValidator(string chartType, string baseName)
    {
        var read = CommittedIr.Strings(Record["reads"]![chartType]![baseName]);
        var declared = CommittedIr.Strings(Template(chartType)["channels"]);
        using (Assert.EnterMultipleScope())
        {
            foreach (var channel in CommittedIr.Strings(CommittedIr.Catalog["channels"]))
            {
                var issues = ChartValidator.Validate(Document(chartType, baseName, channel), FlintBackend.ImageCharts).Issues;
                bool Has(string code) => issues.Any(i => i.Code == code && i.Channel?.Name == channel);
                if (channel is "column" or "row")
                {
                    Assert.That(Has("image_charts_unsupported"), Is.True, $"{channel}: a bound facet throws");
                }
                else if (declared.Contains(channel))
                {
                    Assert.That(Has("unsupported_channel"), Is.False, $"{channel} is declared");
                }
                else
                {
                    Assert.That(Has("unsupported_channel"), Is.EqualTo(!read.Contains(channel)),
                        read.Contains(channel) ? $"{channel} is read here, so it is not unsupported_channel" : $"{channel} is ignored here, so it is unsupported_channel");
                }
            }
        }
    }
}
