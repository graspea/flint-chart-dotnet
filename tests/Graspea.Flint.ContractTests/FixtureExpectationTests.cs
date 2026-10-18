using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graspea.Flint.ContractTests.Support;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.ContractTests;

/// <summary>
/// The validator agrees with what the pinned assemblers did on every fixture, crafted case and backend (design §6
/// contract, §10.3 recorded outcomes): a throw is reported as an Error whose code explains the recorded message, no throw
/// means no throw-class Error, upstream's invalid-option-value warning is reported as invalid_option_value and the other
/// way round, and validating for every backend is valid exactly when no backend with the template throws. Gate for the
/// set of <see cref="FlintUpstream.Version"/>.
/// </summary>
[Category("Contract")]
[Category("Expectations")]
public sealed class FixtureExpectationTests
{
    private static readonly string[] BackendIds = ["vegalite", "echarts", "chartjs", "plotly", "excel"];

    /// <summary>The codes that stand for an assembler throw (design §10.3).</summary>
    private static readonly HashSet<string> ThrowClass = new(StringComparer.Ordinal)
    {
        "unknown_chart_type", "chart_type_not_on_backend", "missing_encodings", "channel_null", "data_missing", "unknown_theme_preset",
        "series_multiple_channels", "series_channel_not_xy", "series_too_few", "series_duplicate_field", "series_field_missing",
        "series_color_bound", "series_nominal_entry", "data_empty", "data_url_only", "encodings_empty", "excel_unsupported",
        "row_not_object", "reserved_semantic_type",
    };

    private static readonly string[] SeriesCodes =
        ["series_multiple_channels", "series_channel_not_xy", "series_too_few", "series_duplicate_field", "series_field_missing", "series_color_bound", "series_nominal_entry"];

    /// <summary>
    /// The codes that explain a recorded upstream error message, first match wins. An error that matches nothing fails
    /// the test, so a new upstream message is mapped before the gate passes.
    /// </summary>
    private static readonly (Regex Message, string[] Codes)[] ErrorCodes =
    [
        (new("^(Static series|Each static series entry|Cannot use static series)"), SeriesCodes),
        (new(@"^Unknown (ECharts |Chart\.js |Plotly )?chart type|^Excel backend does not support chart type"), ["unknown_chart_type", "chart_type_not_on_backend"]),
        (new("^Unknown theme"), ["unknown_theme_preset"]),
        (new(@"^Cannot read properties of (null|undefined) \(reading 'values'\)"), ["data_missing"]),
        (new(@"^Cannot read properties of null \(reading 'field'\)"), ["channel_null"]),
        (new(@"^Cannot read properties of null \(reading |^Cannot create property "), ["row_not_object"]),
        (new("^Cannot convert undefined or null to object"), ["missing_encodings", "row_not_object"]),
        (new(@"^Cannot read properties of undefined \(reading '0'\)"), ["reserved_semantic_type"]),
        (new("^Excel backend "), ["excel_unsupported", "data_empty", "data_url_only", "encodings_empty", "missing_encodings"]),
    ];

    public static IEnumerable<TestCaseData> CurrentFixtures() =>
        Fixtures.Cases(FlintUpstream.Version).Values.Select(c => new TestCaseData(false, c.Id).SetName($"Validator_AgreesWithRecordedOutcome({c.Name})"))
            .Concat(Fixtures.ProbeCases(FlintUpstream.Version).Values.Select(c => new TestCaseData(true, c.Id).SetName($"Validator_AgreesWithRecordedOutcome({c.Name})")));

    // source: tests/fixtures/flint-chart@<FlintUpstream.Version>/expectations.jsonl and probe-expectations.jsonl (the five
    // assemblers run by fixtures.ts on the corpus and on the crafted cases of probe-cases.ts)
    [TestCaseSource(nameof(CurrentFixtures))]
    public void Validator_AgreesWithRecordedOutcome(bool crafted, string id)
    {
        var version = FlintUpstream.Version;
        var input = FlintJson.Deserialize((crafted ? Fixtures.ProbeCases(version) : Fixtures.Cases(version))[id].InputJson);
        var outcomes = (crafted ? Fixtures.ProbeExpectations(version) : Fixtures.Expectations(version))[id].AsObject();
        var anyThrows = false;

        using (Assert.EnterMultipleScope())
        {
            foreach (var (backendId, outcome) in outcomes)
            {
                var backend = (FlintBackend)Array.IndexOf(BackendIds, backendId);
                var issues = ChartValidator.Validate(input, backend).Issues;
                var errors = issues.Where(i => i.Severity == IssueSeverity.Error && ThrowClass.Contains(i.Code)).ToArray();
                var hasTemplate = issues.All(i => i.Code != "chart_type_not_on_backend");

                if (outcome!["throws"]!.GetValue<bool>())
                {
                    anyThrows |= hasTemplate;
                    var message = outcome["error"]!.GetValue<string>();
                    var codes = ErrorCodes.FirstOrDefault(e => e.Message.IsMatch(message)).Codes;
                    Assert.That(codes, Is.Not.Null, $"{backendId} throws ({message}), which no code is mapped to");
                    Assert.That(errors.Select(e => e.Code), Has.Some.AnyOf(codes ?? []), $"{backendId} throws ({message}) but no Error explains it");
                }
                else
                {
                    Assert.That(errors.Select(e => e.ToString()), Is.Empty, $"{backendId} assembles but Errors are reported");
                    var dropped = outcome["warnings"]!.AsArray().Any(w => w!.GetValue<string>() == "invalid-option-value");
                    Assert.That(issues.Any(i => i.Code == "invalid_option_value"), Is.EqualTo(dropped),
                        $"{backendId}: upstream {(dropped ? "drops" : "keeps")} the option values");
                }
            }

            Assert.That(ChartValidator.Validate(input, null).IsValid, Is.EqualTo(!anyThrows), "validation for every backend");
        }
    }

    // source: tests/fixtures/flint-chart@<FlintUpstream.Version>/manifest.json (written with the files by fixtures.ts; a
    // missing or truncated file would otherwise leave fewer test cases and still pass)
    [Test]
    public void FixtureSet_MatchesItsManifest()
    {
        var version = FlintUpstream.Version;
        var manifest = Fixtures.Manifest(version);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest["upstream"]!.GetValue<string>(), Is.EqualTo(version));
            Assert.That(Fixtures.Cases(version), Has.Count.EqualTo(manifest["cases"]!.GetValue<int>()));
            Assert.That(Fixtures.Expectations(version).Keys, Is.EquivalentTo(Fixtures.Cases(version).Keys));
            Assert.That(Fixtures.ProbeCases(version), Has.Count.EqualTo(manifest["probeCases"]!.GetValue<int>()));
            Assert.That(Fixtures.ProbeExpectations(version).Keys, Is.EquivalentTo(Fixtures.ProbeCases(version).Keys));
        }
    }
}
