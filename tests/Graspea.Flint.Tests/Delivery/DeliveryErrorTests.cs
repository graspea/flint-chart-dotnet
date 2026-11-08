using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Delivery;

namespace Graspea.Flint.Tests.Delivery;

public sealed class DeliveryErrorTests
{
    // source: §14.5 error document, §14.8.5 step 9
    [Test]
    public void ToJson_IsTheErrorDocument() =>
        Assert.That(new DeliveryError("forbidden").ToJson(), Is.EqualTo("""{"contract":1,"error":{"code":"forbidden"}}"""));

    // source: §14.5 (codes are written as JSON strings with the default escaping)
    [TestCase("a\"b")]
    [TestCase("a\\b")]
    [TestCase("café")]
    public void Code_IsEscapedLikeAnyJsonString(string code)
    {
        var expected = "{\"contract\":1,\"error\":{\"code\":" + JsonSerializer.Serialize(code) + "}}";
        Assert.That(new DeliveryError(code).ToJson(), Is.EqualTo(expected));
        Assert.That(new DeliveryError(code).Code, Is.EqualTo(code));
    }

    // source: §14.8.5 step 9
    [Test]
    public void Utf8AndWriter_AgreeWithToJson()
    {
        var error = new DeliveryError("stale");
        Assert.That(error.ToUtf8Bytes(), Is.EqualTo(Encoding.UTF8.GetBytes(error.ToJson())));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            error.WriteTo(writer);
        }

        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(stream.ToArray()), JsonNode.Parse(error.ToJson())), Is.True);
    }

    // source: §14.4.1 (non-empty code)
    [Test]
    public void Code_EmptyOrNull_Throws()
    {
        Assert.That(() => new DeliveryError(""), Throws.ArgumentException);
        Assert.That(() => new DeliveryError(null!), Throws.ArgumentNullException);
    }

    // source: §14.4.1 DeliveryContract
    [Test]
    public void ContractVersion_IsOne() => Assert.That(DeliveryContract.Version, Is.EqualTo(1));
}
