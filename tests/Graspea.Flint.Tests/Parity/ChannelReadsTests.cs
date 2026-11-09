using System.Text.Json.Nodes;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The channels the validator treats as read, against what the generator's read probe saw the assemblers of the pinned
/// flint-chart read (tests/fixtures/flint-chart@&lt;pin&gt;/probes.json <c>channelReads</c>; generator/src/extract/channel-reads.ts).
/// Under every base a template assembles from, with each catalog channel bound to the probe's text field unless the base
/// binds it: a declared channel is the template's word and is never <c>unsupported_channel</c>, read or not; an undeclared
/// channel the probe saw ignored is <c>unsupported_channel</c>. On the native backends a channel the probe saw read is
/// never <c>unsupported_channel</c> either (a bound <c>column</c> or <c>row</c>, on which the backend throws, is
/// <c>&lt;id&gt;_unsupported</c>); on the four layout backends, whose shared layout reacts to some undeclared channels,
/// that direction is not asserted (see channel-reads.ts).
/// </summary>
public sealed class ChannelReadsTests
{
    private static JsonNode Record => CommittedIr.Probes["channelReads"]!;

    /// <summary>Undeclared channels the probe saw ignored under one base that the validator still treats as read, because
    /// whether the read changes the chart depends on the row values, which a Warning about the spec does not weigh.</summary>
    private static readonly Dictionary<(string Backend, string ChartType, string Base, string Channel), string> IgnoredHereOnly = new()
    {
        // source: src/excel/assemble.ts:320-337 (each series is sorted by the order field): every city of Semantic
        // Context|#6 is its own color series with a single row, so the sort moves nothing
        [("excel", "Scatter Plot", "Semantic Context|#6", "order")] = "one row per series",
    };

    private static JsonNode Template(string backend, string chartType) =>
        CommittedIr.Catalog["chartTypes"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == chartType)!["backends"]![backend]!;

    public static IEnumerable<TestCaseData> LayoutBases() =>
        from b in Record["layoutReads"]!.AsObject()
        from t in b.Value!.AsObject()
        from s in t.Value!.AsObject()
        select new TestCaseData(b.Key, t.Key, s.Key).SetName($"ProbedLayoutReads_MatchTheValidator({b.Key}, {t.Key}, {s.Key})");

    public static IEnumerable<TestCaseData> Bases() =>
        from b in Record["reads"]!.AsObject()
        from t in b.Value!.AsObject()
        from s in t.Value!.AsObject()
        select new TestCaseData(b.Key, t.Key, s.Key).SetName($"ProbedReads_MatchTheValidator({b.Key}, {t.Key}, {s.Key})");

    /// <summary>The probe's document: the base (a fixture case, or synthetic encodings over the record's rows) with the
    /// probe fields added to every row, row i taking entry i % length, and the channel bound to the text field unless the
    /// base binds it.</summary>
    private static ChartAssemblyInput Document(string chartType, string baseName, string channel)
    {
        var source = Record["bases"]![baseName]!;
        var doc = source["case"] is { } id
            ? CommittedIr.FixtureInput(id.GetValue<string>())
            : new JsonObject
            {
                ["data"] = new JsonObject { ["values"] = Record["rows"]!.DeepClone() },
                ["chart_spec"] = new JsonObject { ["chartType"] = chartType, ["encodings"] = source["encodings"]!.DeepClone() },
            };
        var rows = doc["data"]!["values"]!.AsArray();
        foreach (var field in new[] { "probe_text", "probe_number" })
        {
            var values = Record["probe"]![field]!.AsArray();
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i]![field] = values[i % values.Count]!.DeepClone();
            }
        }

        var encodings = doc["chart_spec"]!["encodings"]!.AsObject();
        if (!encodings.ContainsKey(channel))
        {
            encodings[channel] = new JsonObject { ["field"] = "probe_text" };
        }

        return FlintJson.Deserialize(doc.ToJsonString());
    }

    // source: probes.json channelReads (every template of the two native backends in ir/flint-ir.json assembles from a
    // base; a bound column or row throws, image-charts/assemble.ts:192-194, excel/assemble.ts:213-215; every declared
    // channel is read under some base) and IgnoredHereOnly (each entry is still needed)
    [Test]
    public void ProbeRecord_CoversTheNativeTemplates()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Record["reads"]!.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "excel", "image-charts" }));
            foreach (var (backend, types) in Record["reads"]!.AsObject())
            {
                var withTemplate = CommittedIr.Catalog["chartTypes"]!.AsArray()
                    .Where(t => t!["backends"]![backend] is not null).Select(t => t!["name"]!.GetValue<string>());
                Assert.That(types!.AsObject().Select(p => p.Key), Is.EquivalentTo(withTemplate), backend);
                foreach (var (chartType, bases) in types.AsObject())
                {
                    Assert.That(bases!.AsObject(), Is.Not.Empty, $"{backend}, {chartType}");
                    Assert.That(CommittedIr.Reads(backend, chartType), Is.SupersetOf(CommittedIr.Strings(Template(backend, chartType)["channels"]).Concat(["column", "row"])),
                        $"{backend}, {chartType}: every declared channel is read, and a bound facet throws");
                }
            }

            foreach (var ((backend, chartType, baseName, channel), _) in IgnoredHereOnly)
            {
                Assert.That(CommittedIr.Strings(Record["reads"]![backend]?[chartType]?[baseName]), Does.Not.Contain(channel), $"{backend}, {chartType}, {baseName}: {channel} is still ignored there");
            }
        }
    }

    // source: probes.json channelReads.layoutReads (every template of the four layout backends in ir/flint-ir.json
    // assembles from a base; only the channels a template does not declare are probed)
    [Test]
    public void ProbeRecord_CoversTheLayoutTemplates()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Record["layoutReads"]!.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "chartjs", "echarts", "plotly", "vegalite" }));
            foreach (var (backend, types) in Record["layoutReads"]!.AsObject())
            {
                var withTemplate = CommittedIr.Catalog["chartTypes"]!.AsArray()
                    .Where(t => t!["backends"]![backend] is not null).Select(t => t!["name"]!.GetValue<string>());
                Assert.That(types!.AsObject().Select(p => p.Key), Is.EquivalentTo(withTemplate), backend);
                foreach (var (chartType, bases) in types.AsObject())
                {
                    Assert.That(bases!.AsObject(), Is.Not.Empty, $"{backend}, {chartType}");
                    Assert.That(bases.AsObject().SelectMany(b => CommittedIr.Strings(b.Value)).Intersect(CommittedIr.Strings(Template(backend, chartType)["channels"])), Is.Empty,
                        $"{backend}, {chartType}: a declared channel is not probed");
                }
            }
        }
    }

    // source: probes.json channelReads.layoutReads[backend][chartType][base] (the undeclared channels the layout assembler
    // reads under that base; every other undeclared channel it ignores there) against the validator's unsupported_channel
    // (ValidationRun.UnsupportedChannel: on a layout backend the template's declared channels, ir/flint-ir.json) and
    // retired_member (generator/retired.json)
    [TestCaseSource(nameof(LayoutBases))]
    public void ProbedLayoutReads_MatchTheValidator(string backend, string chartType, string baseName)
    {
        var read = CommittedIr.Strings(Record["layoutReads"]![backend]![chartType]![baseName]);
        var declared = CommittedIr.Strings(Template(backend, chartType)["channels"]);
        using (Assert.EnterMultipleScope())
        {
            foreach (var channel in CommittedIr.Strings(CommittedIr.Catalog["channels"]))
            {
                if (!declared.Contains(channel) && read.Contains(channel))
                {
                    continue;
                }

                var issues = ChartValidator.Validate(Document(chartType, baseName, channel), CommittedIr.Backend(backend)).Issues;
                bool Has(string code) => issues.Any(i => i.Code == code && i.Channel?.Name == channel);
                if (declared.Contains(channel))
                {
                    Assert.That(Has("unsupported_channel"), Is.False, $"{channel} is declared");
                }
                else if (CommittedIr.RetiredIn($"catalog.chartTypes[{chartType}].backends.{backend}.channels[{channel}]") is not null)
                {
                    Assert.That(Has("retired_member"), Is.True, $"{channel} is retired and ignored here, so it is retired_member");
                }
                else
                {
                    Assert.That(Has("unsupported_channel"), Is.True, $"{channel} is ignored here, so it is unsupported_channel");
                }
            }
        }
    }

    // source: probes.json channelReads.reads[backend][chartType][base] against the validator's unsupported_channel,
    // Excel.ReadsUndeclared and ImageCharts.ReadsUndeclared (flint-chart 1.0.1 src/excel/assemble.ts, src/image-charts/assemble.ts)
    [TestCaseSource(nameof(Bases))]
    public void ProbedReads_MatchTheValidator(string backend, string chartType, string baseName)
    {
        var read = CommittedIr.Strings(Record["reads"]![backend]![chartType]![baseName]);
        var declared = CommittedIr.Strings(Template(backend, chartType)["channels"]);
        var target = backend == "excel" ? FlintBackend.Excel : FlintBackend.ImageCharts;
        var throwCode = backend == "excel" ? "excel_unsupported" : "image_charts_unsupported";
        using (Assert.EnterMultipleScope())
        {
            foreach (var channel in CommittedIr.Strings(CommittedIr.Catalog["channels"]))
            {
                var issues = ChartValidator.Validate(Document(chartType, baseName, channel), target).Issues;
                bool Has(string code) => issues.Any(i => i.Code == code && i.Channel?.Name == channel);
                if (declared.Contains(channel))
                {
                    Assert.That(Has("unsupported_channel"), Is.False, $"{channel} is declared");
                    continue;
                }

                if (channel is "column" or "row" && read.Contains(channel))
                {
                    Assert.That(Has(throwCode), Is.True, $"a bound {channel} throws");
                }

                if (!read.Contains(channel) && IgnoredHereOnly.TryGetValue((backend, chartType, baseName, channel), out var why))
                {
                    Assert.That(Has("unsupported_channel"), Is.False, $"{channel} is read by the validator though this base shows no change ({why})");
                    continue;
                }

                Assert.That(Has("unsupported_channel"), Is.EqualTo(!read.Contains(channel)),
                    read.Contains(channel) ? $"{channel} is read here, so it is not unsupported_channel" : $"{channel} is ignored here, so it is unsupported_channel");
            }
        }
    }
}
