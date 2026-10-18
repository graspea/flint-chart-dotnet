using System.Text.Json;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint.Tests.Model;

public sealed class ChartAssemblyInputTests
{
    private const string WithoutData = """{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"city"}}}}""";

    private static ChartAssemblyInput Spec() => new()
    {
        ChartSpec = new ChartSpec { ChartType = ChartType.BarChart, Encodings = { ["x"] = "city" } },
    };

    // source: design §13.3.1 (Data is optional and written only when set: WhenWritingNull)
    [Test]
    public void Serialize_WithoutData_OmitsTheDataMember()
    {
        Assert.That(FlintJson.Serialize(Spec()), Is.EqualTo(WithoutData));
    }

    // source: design §13.3.1 and §5.1 (the wire rule is on the type, so a host context with DefaultIgnoreCondition.Never
    // writes the same document)
    [Test]
    public void Serialize_WithoutData_HostOptionsOmitTheDataMemberToo()
    {
        Assert.That(JsonSerializer.Serialize(Spec(), HostileHostContext.Default.ChartAssemblyInput), Is.EqualTo(WithoutData));
    }

    // source: design §13.3.1 (FlintJson.Deserialize accepts a document without data)
    [Test]
    public void Deserialize_WithoutData_ReadsNullData()
    {
        var input = FlintJson.Deserialize(WithoutData);
        Assert.That(input.Data, Is.Null);
        Assert.That(FlintJson.Serialize(input), Is.EqualTo(WithoutData));
    }

    // source: design §13.3.1 ("data": null reads as null and is written back as absent; omitted and null are one state)
    [Test]
    public void Deserialize_NullData_IsWrittenBackWithoutData()
    {
        var input = FlintJson.Deserialize("""{"data":null,"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"city"}}}}""");
        Assert.That(input.Data, Is.Null);
        Assert.That(FlintJson.Serialize(input), Is.EqualTo(WithoutData));
    }

    // source: probe "data absent" (tests/fixtures/flint-chart@0.5.1/probe-expectations.jsonl: every backend throws
    // "Cannot read properties of undefined (reading 'values')"), design §13.3.1 (data_missing fires when Data is null)
    [Test]
    public void Validate_WithoutData_ReportsDataMissing()
    {
        var issues = ChartValidator.Validate(FlintJson.Deserialize(WithoutData), FlintBackend.VegaLite).Issues;
        Assert.That(issues.Where(i => i.Code == "data_missing").Select(i => i.Severity), Is.EqualTo(new[] { IssueSeverity.Error }));
    }
}
