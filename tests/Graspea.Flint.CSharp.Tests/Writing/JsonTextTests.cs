using Graspea.Flint.CSharp;

namespace Graspea.Flint.CSharp.Tests.Writing;

public sealed class JsonTextTests
{
    // source: design §12.5.3 (JSON fragments hold compact JSON with the input's raw number text) and RFC 8259 §4 (members
    // in input order)
    [Test]
    public void Compact_KeepsOrderAndRawNumbers()
    {
        var item = JsonReader.Parse("{ \"b\" : [ 1.50 , 1e3, -0 ],\n \"a\": { \"x\": null, \"y\": true, \"z\": \"s\" } }");

        Assert.That(JsonText.Compact(item), Is.EqualTo("{\"b\":[1.50,1e3,-0],\"a\":{\"x\":null,\"y\":true,\"z\":\"s\"}}"));
    }

    // source: RFC 8259 §7 (quote, backslash and controls are escaped) and design §12.5.3 (a fragment sits in a raw string
    // literal, so the characters a C# string literal escapes, such as U+202E, are written as JSON \u escapes and never reach
    // the source as themselves)
    [Test]
    public void Compact_EscapesQuotesControlsAndInvisibleCharacters()
    {
        var item = JsonReader.Parse("[\"q\\\"b\\\\n\\n\\u0001\\u202e\\u00e9\\ud83d\\ude00\\udb40\\udc01\"]");

        Assert.That(JsonText.Compact(item), Is.EqualTo("[\"q\\\"b\\\\n\\n\\u0001\\u202Eé\U0001F600\\uDB40\\uDC01\"]"));
    }
}
