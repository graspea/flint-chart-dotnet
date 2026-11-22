using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Validation;

public sealed partial class ChartValidatorTests
{
    private const string Code = "invalid_annotation_option";

    /// <summary>The probes' baseline Bar Chart (or another chart type over the same rows) read from JSON with the given
    /// <c>options</c> and <c>theme_spec</c>, as a document a host would send.</summary>
    private static ChartAssemblyInput Annotated(string? options, string chartType = "Bar Chart", string? theme = null, string encodings = "{\"x\":\"city\",\"y\":\"revenue\"}") =>
        FlintJson.Deserialize(
            "{\"data\":{\"values\":[{\"city\":\"Seattle\",\"revenue\":501479,\"cost\":10},{\"city\":\"Boston\",\"revenue\":300000,\"cost\":20}]},"
            + $"\"chart_spec\":{{\"chartType\":\"{chartType}\",\"encodings\":{encodings}}}"
            + (options is null ? string.Empty : $",\"options\":{options}")
            + (theme is null ? string.Empty : $",\"theme_spec\":{theme}")
            + "}");

    // source: flint-chart 1.1.1 src/vegalite/assemble.ts:666-667 (vlReserveNoteRoom and vlReserveEnclosureRoom run on every
    // Vega-Lite compile) and src/vegalite/note-room.ts: the regions filter reads region.text of every entry (:177) and keeps a
    // captioned stretch of x at the top (text, x?.length, no y?.length, placement undefined or 'top'), whose x stretchOf
    // maps (:128-153); the notes loop reads Object.entries(note.key) (:247) and measures note.text once a key field names a
    // mark (:255, src/core/annotation/room.ts:25-35); the enclosures read (enclosure.targets ?? []).flatMap, test
    // 'select' in every truthy target (:335-336) and compile its selector (src/core/target-selector.ts:12-16: 'where' in
    // select, Object.entries of where or key; :59-67: 'in' in each where condition, condition.in.map or
    // condition.range[0]); the declared types of AssembleOptions notes, regions and enclosures (src/core/types.ts:1597-1618);
    // probe cases "bar with a null note", "bar with a note without a key", "bar with a note on a row without text", "bar
    // with a null region", "bar with a region whose x is a string", "scatter with a null enclosure" and "scatter with an
    // enclosure whose targets are a string" of tests/fixtures/flint-chart@1.1.1 (each throws on vegalite only). The rule
    // reads the option values, not the chart or the rows, so the path is reported for a Bar Chart whatever reaches the code.
    [TestCase("{\"notes\":[null]}", "options.notes[0]")]
    [TestCase("{\"notes\":[{\"text\":\"Peak\"}]}", "options.notes[0].key")]
    [TestCase("{\"notes\":[{\"key\":null,\"text\":\"Peak\"}]}", "options.notes[0].key")]
    [TestCase("{\"notes\":[{\"key\":{\"city\":\"Seattle\"}}]}", "options.notes[0].text")]
    [TestCase("{\"notes\":[{\"key\":{\"city\":\"Seattle\"},\"text\":null}]}", "options.notes[0].text")]
    [TestCase("{\"notes\":[{\"key\":{}}]}", null)]
    [TestCase("{\"notes\":[{\"key\":{\"city\":\"Seattle\"},\"text\":\"Peak\"}]}", null)]
    [TestCase("{\"notes\":[{\"key\":{\"city\":\"Seattle\"},\"text\":\"\"}]}", null)]
    [TestCase("{\"notes\":[]}", null)]
    [TestCase("{\"notes\":[{\"key\":{\"city\":\"Seattle\"},\"text\":\"Peak\"},null]}", "options.notes[1]")]
    [TestCase("{\"regions\":[null]}", "options.regions[0]")]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\"}]}", "options.regions[0].x")]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\",\"placement\":\"top\"}]}", "options.regions[0].x")]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":{\"length\":2}}]}", "options.regions[0].x")]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\",\"y\":null}]}", "options.regions[0].x")]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\",\"placement\":\"bottom\"}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\",\"y\":[0,100]}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"Seattle\",\"y\":\"b\"}]}", null)]
    [TestCase("{\"regions\":[{\"x\":\"Seattle\"}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"\",\"x\":\"Seattle\"}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":[\"Seattle\",\"Boston\"]}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":5}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":true}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":{}}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":{\"length\":0}}]}", null)]
    [TestCase("{\"regions\":[{\"text\":\"Launch\",\"x\":\"\"}]}", null)]
    [TestCase("{\"enclosures\":[null]}", "options.enclosures[0]")]
    [TestCase("{\"enclosures\":[{\"targets\":\"Seattle\"}]}", "options.enclosures[0].targets")]
    [TestCase("{\"enclosures\":[{\"targets\":5}]}", "options.enclosures[0].targets")]
    [TestCase("{\"enclosures\":[{\"targets\":true}]}", "options.enclosures[0].targets")]
    [TestCase("{\"enclosures\":[{\"targets\":{}}]}", "options.enclosures[0].targets")]
    [TestCase("{\"enclosures\":[{}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":null}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[\"Seattle\"]}]}", "options.enclosures[0].targets[0]")]
    [TestCase("{\"enclosures\":[{\"targets\":[5]}]}", "options.enclosures[0].targets[0]")]
    [TestCase("{\"enclosures\":[{\"targets\":[null,true]}]}", "options.enclosures[0].targets[1]")]
    [TestCase("{\"enclosures\":[{\"targets\":[null,0,\"\",false]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{},[],{\"visual\":{\"kind\":\"bar\"}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":null}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":\"Seattle\"}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":0}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":[]}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{}}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"key\":null}}]}]}", "options.enclosures[0].targets[0].select")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"key\":\"Seattle\"}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"key\":{}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"key\":{\"city\":\"Seattle\"}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":null}}]}]}", "options.enclosures[0].targets[0].select.where")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":{\"in\":[\"Seattle\"]}}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"revenue\":{\"range\":[0,400000]}}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"revenue\":{\"range\":\"ab\"}}}}]}]}", null)]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":null}}}]}]}", "options.enclosures[0].targets[0].select.where.city")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":\"Seattle\"}}}]}]}", "options.enclosures[0].targets[0].select.where.city")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":[]}}}]}]}", "options.enclosures[0].targets[0].select.where.city")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":{}}}}]}]}", "options.enclosures[0].targets[0].select.where.city")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"revenue\":{\"range\":null}}}}]}]}", "options.enclosures[0].targets[0].select.where.revenue")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":{\"in\":\"Seattle\"}}}}]}]}", "options.enclosures[0].targets[0].select.where.city.in")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":{\"city\":{\"in\":null,\"range\":[0,1]}}}}]}]}", "options.enclosures[0].targets[0].select.where.city.in")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":\"ab\"}}]}]}", "options.enclosures[0].targets[0].select.where.0")]
    [TestCase("{\"enclosures\":[{\"targets\":[{\"select\":{\"where\":5}}]}]}", null)]
    public void Errors_InvalidAnnotationOption_WhereVegaLiteThrowsReadingTheEntry(string options, string? path)
    {
        var input = Annotated(options);
        var expected = path is null ? Array.Empty<string>() : [$"Error {path}"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(input, FlintBackend.VegaLite, Code).Select(i => $"{i.Severity} {i.Path}"), Is.EqualTo(expected), "vegalite");
            Assert.That(Issues(input, null, Code).Select(i => $"{i.Severity} {i.Path}"), Is.EqualTo(expected), "no backend");
            Assert.That(ChartValidator.Validate(input, null).IsValid, Is.EqualTo(path is null), "IsValid");
        }
    }

    // source: flint-chart 1.1.1: only src/vegalite/assemble.ts and src/vegalite/interactive.ts read options.notes, regions
    // and enclosures (the ECharts, Chart.js, Plotly, Excel and Image-Charts assemblers do not), and the probe cases "bar
    // with a null note" and the others of the matrix above assemble on those five; validation-codes.md severity rule (Info
    // for everything else)
    [Test]
    public void Infos_InvalidAnnotationOption_OnTheBackendsThatDoNotReadIt()
    {
        var input = Annotated("{\"notes\":[null]}");
        using (Assert.EnterMultipleScope())
        {
            foreach (var backend in new[] { FlintBackend.ECharts, FlintBackend.ChartJs, FlintBackend.Plotly, FlintBackend.Excel, FlintBackend.ImageCharts })
            {
                var issue = Issues(input, backend, Code).Single();
                Assert.That($"{issue.Severity} {issue.Path}", Is.EqualTo("Info options.notes[0]"), backend.ToString());
                Assert.That(issue.Message, Is.EqualTo($"options.notes[0] is null; {ValidationIds[backend]} does not read options.notes, but Vega-Lite throws on it."), backend.ToString());
                Assert.That(ChartValidator.Validate(input, backend).IsValid, Is.True, backend.ToString());
            }

            Assert.That(Issues(input, FlintBackend.VegaLite, Code).Single().Message,
                Is.EqualTo("options.notes[0] is null; Vega-Lite reads the key of every note. Throws on vegalite."));
            Assert.That(Issues(input, null, Code).Single().Message,
                Is.EqualTo("options.notes[0] is null; Vega-Lite reads the key of every note. Throws on vegalite."));

            // A chart type without a Vega-Lite template: the Info, for every backend that has the template.
            var combo = Annotated("{\"notes\":[null]}", "Combo Chart");
            Assert.That(Issues(combo, null, Code).Select(i => $"{i.Severity} {i.Path} {i.Message}"),
                Is.EqualTo(new[] { "Info options.notes[0] options.notes[0] is null; chartjs does not read options.notes, but Vega-Lite throws on it." }));
            Assert.That(ChartValidator.Validate(combo, null).IsValid, Is.True);
        }
    }

    private static readonly Dictionary<FlintBackend, string> ValidationIds = new()
    {
        [FlintBackend.VegaLite] = "vegalite", [FlintBackend.ECharts] = "echarts", [FlintBackend.ChartJs] = "chartjs",
        [FlintBackend.Plotly] = "plotly", [FlintBackend.Excel] = "excel", [FlintBackend.ImageCharts] = "image-charts",
    };

    // source: flint-chart 1.1.1 src/vegalite/assemble.ts:276 (the options are resolveCompileDefaults of the theme and
    // input.options) and src/core/theme/ground.ts:195-215 (a theme's compileDefaults member applies only where the chart
    // leaves the option undefined; the chart's own stands, even an empty array); probe case "bar with a theme whose compile
    // defaults hold a null region" of tests/fixtures/flint-chart@1.1.1 (throws on vegalite)
    [Test]
    public void Errors_InvalidAnnotationOption_UnderTheThemesCompileDefaults()
    {
        const string theme = "{\"compileDefaults\":{\"regions\":[null],\"notes\":[{\"text\":\"Peak\"}]}}";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(Annotated(null, theme: theme), FlintBackend.VegaLite, Code).Select(i => $"{i.Severity} {i.Path}"),
                Is.EqualTo(new[] { "Error theme_spec.compileDefaults.notes[0].key", "Error theme_spec.compileDefaults.regions[0]" }));
            Assert.That(Issues(Annotated("{\"regions\":[],\"notes\":[{\"key\":{\"city\":\"Seattle\"},\"text\":\"Peak\"}]}", theme: theme), FlintBackend.VegaLite, Code), Is.Empty,
                "the chart's own options stand");
            Assert.That(Issues(Annotated("{\"regions\":[]}", theme: theme), FlintBackend.VegaLite, Code).Select(i => i.Path),
                Is.EqualTo(new[] { "theme_spec.compileDefaults.notes[0].key" }), "the theme fills what the chart leaves out");
            Assert.That(Issues(Annotated(null, theme: "{\"extends\":\"economist\",\"compileDefaults\":{\"enclosures\":[null]}}"), FlintBackend.VegaLite, Code).Select(i => i.Path),
                Is.EqualTo(new[] { "theme_spec.compileDefaults.enclosures[0]" }), "a theme that extends a preset");
            Assert.That(Issues(Annotated(null, theme: "\"economist\""), FlintBackend.VegaLite, Code), Is.Empty, "a preset states no annotation options");
            Assert.That(Issues(Annotated(null, theme: theme), FlintBackend.ECharts, Code).Select(i => $"{i.Severity} {i.Message}"), Is.EqualTo(new[]
            {
                "Info theme_spec.compileDefaults.notes[0] has no key; echarts does not read theme_spec.compileDefaults.notes, but Vega-Lite throws on it.",
                "Info theme_spec.compileDefaults.regions[0] is null; echarts does not read theme_spec.compileDefaults.regions, but Vega-Lite throws on it.",
            }));
        }
    }

    // source: docs/validation-codes.md invalid_annotation_option (the rule reads the option values by upstream's declared
    // type, not the chart or the rows: Vega-Lite reaches the code only for some charts, flint-chart 1.1.1
    // src/vegalite/note-room.ts:180-202 and :320-328, so an entry Vega-Lite would throw on is an Error also on a chart
    // whose compiled spec never reads it) and the typed model (AssembleOptionsNotes.Key and Text, AssembleOptionsRegions.X,
    // AssembleOptionsEnclosures.Targets) writing each form from C#
    [Test]
    public void Errors_InvalidAnnotationOption_ByDeclaredTypeWhateverTheChart()
    {
        var pie = Annotated("{\"notes\":[null]}", "Pie Chart", encodings: "{\"color\":\"city\",\"size\":\"revenue\"}");
        var typed = Bar(i => i.Options = new AssembleOptions
        {
            Notes = [new AssembleOptionsNotes { Text = "Peak" }],
            Regions = [new AssembleOptionsRegions { Text = "Launch", X = "Seattle" }],
            Enclosures = [new AssembleOptionsEnclosures { Targets = "Seattle" }],
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Issues(pie, FlintBackend.VegaLite, Code).Select(i => $"{i.Severity} {i.Path}"), Is.EqualTo(new[] { "Error options.notes[0]" }));
            Assert.That(Issues(typed, FlintBackend.VegaLite, Code).Select(i => $"{i.Severity} {i.Path} {i.Message}"), Is.EqualTo(new[]
            {
                "Error options.notes[0].key options.notes[0] has no key; Vega-Lite reads the fields of every note's key. Throws on vegalite.",
                "Error options.regions[0].x options.regions[0].x is \"Seattle\", not an array; Vega-Lite maps the stretch of a region captioned at the top. Throws on vegalite.",
                "Error options.enclosures[0].targets options.enclosures[0].targets is \"Seattle\", not an array; Vega-Lite reads the targets of every enclosure as an array. Throws on vegalite.",
            }));
        }
    }
}
