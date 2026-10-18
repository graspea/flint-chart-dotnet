using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.ContractTests.Support;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.ContractTests;

/// <summary>
/// Every stored fixture survives Deserialize, Serialize and canonicalisation unchanged (design §10.3 contract: fixture
/// round-trip). An id in known-failures.txt is inconclusive while it fails and fails once it passes, so the file can only
/// shrink; the preview tag requires it to be empty.
/// </summary>
[Category("Contract")]
[Category("RoundTrip")]
public sealed class FixtureRoundTripTests
{
    public static IEnumerable<TestCaseData> AllFixtures() =>
        from version in Fixtures.Versions()
        from c in Fixtures.Cases(version).Values
        select new TestCaseData(version, c.Id).SetName($"RoundTrip_Fixture_IsCanonicallyEqual({version}: {c.Name})");

    // source: TEST_GENERATORS of each pinned flint-chart via the design §7.9 mapping (tests/fixtures/flint-chart@<version>/cases.jsonl)
    [TestCaseSource(nameof(AllFixtures))]
    public void RoundTrip_Fixture_IsCanonicallyEqual(string version, string id)
    {
        var fixture = Fixtures.Cases(version)[id];
        var (equal, detail) = RoundTrip(fixture.InputJson);

        if (Fixtures.KnownFailures(version).Contains(id))
        {
            Assert.That(equal, Is.False, $"{id} is listed in known-failures.txt but round-trips; remove it from the list.");
            Assert.Inconclusive($"known failure: {detail}");
        }

        Assert.That(equal, Is.True, detail);
    }

    private static (bool Equal, string Detail) RoundTrip(string inputJson)
    {
        try
        {
            var expected = Canonical.Of(JsonNode.Parse(inputJson));
            var actual = Canonical.Of(JsonNode.Parse(FlintJson.Serialize(FlintJson.Deserialize(inputJson))));
            return JsonNode.DeepEquals(expected, actual)
                ? (true, string.Empty)
                : (false, $"expected:\n{expected!.ToJsonString()}\nactual:\n{actual!.ToJsonString()}");
        }
        catch (JsonException e)
        {
            return (false, $"{e.GetType().Name}: {e.Message}");
        }
    }
}
