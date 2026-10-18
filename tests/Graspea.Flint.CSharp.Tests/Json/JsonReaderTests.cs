using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.CSharp.Tests.Json;

public sealed class JsonReaderTests
{
    // source: RFC 8259 §4 (an object is an unordered collection, kept here in input order for diagnostics and row keys) and
    // design §12.2.2 (positions of every node; line and column are 1-based)
    [Test]
    public void Parse_Object_KeepsMembersInInputOrderWithPositions()
    {
        var root = JsonReader.Parse("{\n  \"b\": 1,\n  \"a\": [true, null]\n}");

        Assert.That(root.Kind, Is.EqualTo(JsonItemKind.Object));
        Assert.That(root.Members.Select(m => m.Key), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(root.Members[1].KeyPosition, Is.EqualTo(new TextPosition(14, 3, 3)));
        Assert.That(root.Members[1].Value.Start, Is.EqualTo(new TextPosition(19, 3, 8)));
        Assert.That(root.Members[1].Value.Items.Select(i => i.Kind), Is.EqualTo(new[] { JsonItemKind.True, JsonItemKind.Null }));
        Assert.That(root.Get("a")!.End, Is.EqualTo(31));
    }

    // source: design §12.6.7 (a diagnostic at a key, FLINT140, spans the key with its quotes); the key "a\u0062" is 9
    // characters of text from offset 1
    [Test]
    public void Parse_Object_RecordsWhereEachKeyEnds()
    {
        var root = JsonReader.Parse("{\"a\\u0062\": 1}");

        Assert.That(root.Members[0].Key, Is.EqualTo("ab"));
        Assert.That(root.Members[0].KeyPosition.Offset, Is.EqualTo(1));
        Assert.That(root.Members[0].KeyEnd, Is.EqualTo(10));
    }

    // source: design §12.2.2 (every number's raw text is kept, so 1200.50 and 1e3 are re-emitted exactly)
    [TestCase("1200.50")]
    [TestCase("1e3")]
    [TestCase("-0.5E+10")]
    [TestCase("-0")]
    [TestCase("123456789012345678901234567890")]
    public void Parse_Number_KeepsTheRawText(string number)
    {
        var item = JsonReader.Parse($"[{number}]").Items[0];

        Assert.That(item.Kind, Is.EqualTo(JsonItemKind.Number));
        Assert.That(item.Value, Is.EqualTo(number));
    }

    // source: RFC 8259 §7 (the two-character escapes and \uXXXX, a surrogate pair as two escapes)
    [Test]
    public void Parse_String_DecodesEscapes()
    {
        var item = JsonReader.Parse("\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\\u00e9\\ud83d\\ude00\"");

        Assert.That(item.Value, Is.EqualTo("a\"b\\c/d\b\f\n\r\té\U0001F600"));
    }

    // source: design §12.2.2 (a duplicate key is a positioned error; System.Text.Json would keep the last one)
    [Test]
    public void Parse_DuplicateKey_ThrowsAtTheSecondKey()
    {
        var e = Assert.Throws<JsonReadException>(() => JsonReader.Parse("{\"a\": 1,\n \"a\": 2}"));

        Assert.That(e!.Position, Is.EqualTo(new TextPosition(10, 2, 2)));
        Assert.That(e.Message, Does.Contain("Duplicate key \"a\""));
    }

    // source: design §12.2.2 and FlintJson.Deserialize behaviour (System.Text.Json rejects a lone surrogate escape when it
    // reads the string, as JsonSerializer.Deserialize<string>("\"\\uD800\"") does), in strings and in keys
    [TestCase("[\"\\uD800\"]")]
    [TestCase("[\"\\uDC00\"]")]
    [TestCase("[\"\\uD800x\"]")]
    [TestCase("[\"\\uD800\\u0041\"]")]
    public void Parse_LoneSurrogateEscapeInAString_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string[]>(json));
        Assert.Throws<JsonReadException>(() => JsonReader.Parse(json));
    }

    // source: as above, for a key (FlintJson reads keys as strings: field_display_names, semantic_types, encodings)
    [Test]
    public void Parse_LoneSurrogateEscapeInAKey_Throws()
    {
        const string json = "{\"\\uD800\": 1}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<string, int>>(json));
        Assert.Throws<JsonReadException>(() => JsonReader.Parse(json));
    }

    public static IEnumerable<string> Vectors() =>
    [
        "{}", "[]", "\"\"", "0", "-0", "1E+2", "1.5e-3", "[1,[2,{\"a\":null}]]", " \t\r\n{} ", "true", "false", "null",
        "", " ", "{", "}", "[1,]", "{\"a\":1,}", "// c\n{}", "/* c */ {}", "{'a':1}", "[01]", "[1.]", "[.5]", "[+1]", "[1e]",
        "[1e+]", "[-]", "\"\\x\"", "\"a\tb\"", "\"a\nb\"", "[tru]", "[True]", "{} {}", "[NaN]", "[Infinity]", "[\"\\u12\"]",
        "{\"a\" 1}", "[1 2]", "{\"a\":}", "{1:2}", "[\"a\"", "\"abc", "\u00a0{}", "[1]x", "{\"a\":1 \"b\":2}",
    ];

    // source: RFC 8259 and FlintJson.Deserialize behaviour (System.Text.Json with its defaults, as FlintJson reads:
    // no comments, no trailing commas, no leading zeros, strict literals and escapes)
    [TestCaseSource(nameof(Vectors))]
    public void Parse_AcceptsExactlyWhatSystemTextJsonAccepts(string json)
    {
        bool stjAccepts;
        try
        {
            using var _ = JsonDocument.Parse(json);
            stjAccepts = true;
        }
        catch (JsonException)
        {
            stjAccepts = false;
        }

        bool accepts;
        try
        {
            JsonReader.Parse(json);
            accepts = true;
        }
        catch (JsonReadException)
        {
            accepts = false;
        }

        Assert.That(accepts, Is.EqualTo(stjAccepts));
    }

    // source: design §12.2.2 (maximum depth 64, the System.Text.Json default FlintJson.Deserialize reads with)
    [TestCase(63, true)]
    [TestCase(64, false)]
    public void Parse_Depth_MatchesSystemTextJson(int depth, bool accepted)
    {
        var json = new string('[', depth) + new string(']', depth);
        var stjAccepts = true;
        try
        {
            FlintJson.Deserialize("{\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{}},\"x\":" + json + "}");
        }
        catch (JsonException)
        {
            stjAccepts = false;
        }

        var root = "{\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{}},\"x\":" + json + "}";
        Assert.That(stjAccepts, Is.EqualTo(accepted));
        if (stjAccepts)
        {
            Assert.DoesNotThrow(() => JsonReader.Parse(root));
        }
        else
        {
            Assert.Throws<JsonReadException>(() => JsonReader.Parse(root));
        }
    }

    // source: design §12.2.2 (errors carry the line and column of the offending character)
    [Test]
    public void Parse_InvalidToken_ReportsItsPosition()
    {
        var e = Assert.Throws<JsonReadException>(() => JsonReader.Parse("{\n  \"a\": tru\n}"));

        Assert.That(e!.Position.Line, Is.EqualTo(2));
        Assert.That(e.Position.Column, Is.EqualTo(8));
    }
}
