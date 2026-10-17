using System.Text.Json.Nodes;

namespace Graspea.Flint.Tests.Support;

/// <summary>JSON equality through JsonNode.DeepEquals (design §10.1 rule 3).</summary>
public static class JsonAssert
{
    public static void Equal(string expectedJson, JsonNode? actual) =>
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), Is.True,
            () => $"expected:\n{expectedJson}\nactual:\n{actual?.ToJsonString()}");

    public static void Equal(string expectedJson, string actualJson) => Equal(expectedJson, JsonNode.Parse(actualJson));
}
