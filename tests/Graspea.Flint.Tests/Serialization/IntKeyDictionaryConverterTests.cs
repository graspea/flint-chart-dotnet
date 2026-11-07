using System.Text.Json;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Tests.Serialization;

public sealed class IntKeyDictionaryConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new IntKeyDictionaryConverter<string>() },
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseUpper,
    };

    // source: flint-chart 1.0.0 d.ts ThemeInk.series.categoricalByCount (`Record<number, …>`, "`{ 2: [...], 3: [...] }`");
    // JSON object keys are strings (RFC 8259 §4), written in the integer's invariant form
    [Test]
    public void Write_KeysAsInvariantIntegerStrings()
    {
        var map = new Dictionary<int, string> { [2] = "a", [10] = "b", [-1] = "c" };
        Assert.That(JsonSerializer.Serialize(map, Options), Is.EqualTo("""{"2":"a","10":"b","-1":"c"}"""));
    }

    // source: as above; the reader is the inverse of the writer
    [Test]
    public void Read_IntegerKeys()
    {
        var map = JsonSerializer.Deserialize<Dictionary<int, string>>("""{"3":"x","-4":"y"}""", Options)!;
        Assert.That(map, Is.EqualTo(new Dictionary<int, string> { [3] = "x", [-4] = "y" }));
    }

    // source: design §12.3 fidelity rule: a key that does not write back as itself would change the wire form, so it is refused
    [TestCase("""{"02":"x"}""")]
    [TestCase("""{"+2":"x"}""")]
    [TestCase("""{"2.5":"x"}""")]
    [TestCase("""{"two":"x"}""")]
    [TestCase("""{"":"x"}""")]
    [TestCase("""{"99999999999":"x"}""")]
    [TestCase("""["x"]""")]
    public void Read_OtherKeysOrShapes_Throw(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<int, string>>(json, Options));
    }
}
