using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// The Vega-Lite throws of the pinned package (flint-chart 1.1.0+) on the annotation options a chart is drawn with from the
/// start: <c>notes</c>, <c>regions</c> and <c>enclosures</c>, of <c>options</c> or, where <c>options</c> leaves the member
/// undefined, of <c>theme_spec.compileDefaults</c> (<c>resolveCompileDefaults</c>, src/core/theme/ground.ts:195-215 of
/// flint-chart 1.1.1). Every Vega-Lite compile calls <c>vlReserveNoteRoom</c> and <c>vlReserveEnclosureRoom</c>
/// (src/vegalite/assemble.ts:666-667), which read the entries without a guard (src/vegalite/note-room.ts):
/// <list type="bullet">
/// <item>the regions filter reads <c>region.text</c> of every entry, on every chart (:177), and keeps a captioned stretch
/// of x at the top: a truthy <c>text</c>, an <c>x</c> with a truthy <c>length</c>, a <c>y</c> without one, and
/// <c>placement</c> undefined or <c>'top'</c>; <c>stretchOf</c> then maps over its <c>x</c> (:128-153);</item>
/// <item>the notes loop reads <c>Object.entries(note.key)</c> (:247) and, once a key field names a mark, measures
/// <c>note.text</c> (:255, src/core/annotation/room.ts:25-35);</item>
/// <item>the enclosures read <c>(enclosure.targets ?? []).flatMap</c>, test <c>'select' in</c> every truthy target and
/// compile its selector (:335-336): <c>'where' in select</c> and <c>Object.entries</c> of its <c>where</c> or
/// <c>key</c> (src/core/target-selector.ts:12-16), then <c>'in' in</c> each where condition and
/// <c>condition.in.map</c> or <c>condition.range[0]</c> (:59-67).</item>
/// </list>
/// Only Vega-Lite reads these options (src/vegalite/assemble.ts and interactive.ts). The rule reads the option values by
/// upstream's declared type (<c>AssembleOptions</c>, src/core/types.ts:1597-1618), not the chart or the rows: Vega-Lite
/// reaches the notes and regions loops only for a single view whose <c>y</c> draws a quantitative measure of bars, points,
/// lines or ticks with rows, the enclosures only for dots or lines, a note's text only when a key field names a row and a
/// where condition only when every where field is a data key (note-room.ts:180-228, :320-329; target-selector.ts:60). So
/// an entry is reported for every chart, also one whose compiled spec never reads it.
/// </summary>
internal static class AnnotationOptions
{
    /// <summary>One entry upstream throws on: where (<paramref name="Path"/>), the option it is in, what is wrong with it,
    /// and what Vega-Lite does with it.</summary>
    internal sealed record Violation(string Path, string Option, string Fact, string Reason);

    private static readonly string[] Members = ["notes", "regions", "enclosures"];

    /// <summary>The entries of <paramref name="options"/>, and of <paramref name="compileDefaults"/> for each member the
    /// options leave undefined, that Vega-Lite throws on reading; the first fault of each entry, in the order upstream
    /// reads it. Both are the JSON the document writes.</summary>
    internal static IEnumerable<Violation> Evaluate(JsonObject? options, JsonObject? compileDefaults)
    {
        foreach (var member in Members)
        {
            var (root, value) = options is not null && options.TryGetPropertyValue(member, out var stated) ? ("options", stated)
                : compileDefaults is not null && compileDefaults.TryGetPropertyValue(member, out var house) ? ("theme_spec.compileDefaults", house)
                : (null, null);
            if (value is not JsonArray entries)
            {
                continue;
            }

            var option = $"{root}.{member}";
            for (var i = 0; i < entries.Count; i++)
            {
                var at = $"{option}[{i}]";
                var fault = member switch
                {
                    "notes" => Note(at, entries[i]),
                    "regions" => Region(at, entries[i]),
                    _ => Enclosure(at, entries[i]),
                };
                if (fault is { } f)
                {
                    yield return new Violation(f.Path, option, f.Fact, f.Reason);
                }
            }
        }
    }

    private static (string Path, string Fact, string Reason)? Note(string at, JsonNode? note)
    {
        if (note is null)
        {
            return (at, $"{at} is null", "Vega-Lite reads the key of every note");
        }

        if (Js.Get(note, "key", out _) is not { } key)
        {
            return ($"{at}.key", $"{at} has no key", "Vega-Lite reads the fields of every note's key");
        }

        // A key without fields names no mark, and the text is never read.
        return Entries(key).Count > 0 && Js.Get(note, "text", out _) is null
            ? ($"{at}.text", $"{at} has a key but no text", "Vega-Lite measures the text of a note whose key names a mark")
            : null;
    }

    private static (string Path, string Fact, string Reason)? Region(string at, JsonNode? region)
    {
        if (region is null)
        {
            return (at, $"{at} is null", "Vega-Lite reads the text of every region");
        }

        var x = Js.Get(region, "x", out _);
        var placement = Js.Get(region, "placement", out var placed);
        var captionedAtTop = Truthy(Js.Get(region, "text", out _)) && TruthyLength(x) && !TruthyLength(Js.Get(region, "y", out _))
            && (!placed || (Js.Kind(placement) == JsonValueKind.String && placement!.GetValue<string>() == "top"));
        return captionedAtTop && x is not JsonArray
            ? ($"{at}.x", $"{at}.x is {x!.ToJsonString()}, not an array", "Vega-Lite maps the stretch of a region captioned at the top")
            : null;
    }

    private static (string Path, string Fact, string Reason)? Enclosure(string at, JsonNode? enclosure)
    {
        if (enclosure is null)
        {
            return (at, $"{at} is null", "Vega-Lite reads the targets of every enclosure");
        }

        var targets = Js.Get(enclosure, "targets", out _);
        if (targets is null)
        {
            return null;
        }

        if (targets is not JsonArray list)
        {
            return ($"{at}.targets", $"{at}.targets is {targets.ToJsonString()}, not an array", "Vega-Lite reads the targets of every enclosure as an array");
        }

        for (var j = 0; j < list.Count; j++)
        {
            if (Target($"{at}.targets[{j}]", list[j]) is { } fault)
            {
                return fault;
            }
        }

        return null;
    }

    private static (string Path, string Fact, string Reason)? Target(string at, JsonNode? target)
    {
        if (!Truthy(target))
        {
            return null;
        }

        if (!IsObject(target))
        {
            return (at, $"{at} is {target!.ToJsonString()}", "Vega-Lite looks for a select in every target but null, false, 0 and \"\"");
        }

        if (target is not JsonObject t || !t.TryGetPropertyValue("select", out var select))
        {
            return null;
        }

        var sat = $"{at}.select";
        const string selector = "Vega-Lite reads the where or the key of a target's select";
        if (!IsObject(select))
        {
            return (sat, $"{sat} is {Json(select)}", selector);
        }

        if (select is not JsonObject s || !s.TryGetPropertyValue("where", out var where))
        {
            return Js.Get(select, "key", out _) is null ? (sat, $"{sat} has neither a where nor a key", selector) : null;
        }

        if (where is null)
        {
            return ($"{sat}.where", $"{sat}.where is null", "Vega-Lite reads the fields of a select's where");
        }

        const string condition = "Vega-Lite reads the in or the range of each where condition";
        foreach (var (field, value) in Entries(where))
        {
            var cat = $"{sat}.where.{field}";
            if (!IsObject(value))
            {
                return (cat, $"{cat} is {Json(value)}", condition);
            }

            if (value is JsonObject c && c.TryGetPropertyValue("in", out var members))
            {
                if (members is not JsonArray)
                {
                    return ($"{cat}.in", $"{cat}.in is {Json(members)}, not an array", "Vega-Lite maps the in of a where condition");
                }
            }
            else if (Js.Get(value, "range", out _) is null)
            {
                return (cat, $"{cat} has neither an in nor a range", condition);
            }
        }

        return null;
    }

    /// <summary><c>Object.entries(v)</c> of a JSON value: an object's members, an array's or a string's index and element,
    /// nothing for a number or a boolean (null throws, and is handled by the caller).</summary>
    private static List<(string Key, JsonNode? Value)> Entries(JsonNode node) => node switch
    {
        JsonObject o => [.. o.Select(m => (m.Key, m.Value))],
        JsonArray a => [.. a.Select((v, i) => (i.ToString(System.Globalization.CultureInfo.InvariantCulture), v))],
        _ when Js.Kind(node) == JsonValueKind.String =>
            [.. node.GetValue<string>().Select((ch, i) => (i.ToString(System.Globalization.CultureInfo.InvariantCulture), (JsonNode?)JsonValue.Create(ch.ToString())))],
        _ => [],
    };

    /// <summary>A JSON value as JavaScript's <c>Boolean(v)</c> reads it.</summary>
    private static bool Truthy(JsonNode? node) => Js.Kind(node) switch
    {
        JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.Number => Js.NumberOf(node!) != 0,
        JsonValueKind.String => node!.GetValue<string>().Length > 0,
        _ => true,
    };

    /// <summary><c>v?.length</c> read as a boolean: a string's or an array's length, an object's <c>length</c> member;
    /// a number or a boolean has none.</summary>
    private static bool TruthyLength(JsonNode? node) => node switch
    {
        JsonArray a => a.Count > 0,
        JsonObject o => Truthy(Js.Get(o, "length", out _)),
        _ => Js.Kind(node) == JsonValueKind.String && node!.GetValue<string>().Length > 0,
    };

    /// <summary>The right-hand side the <c>in</c> operator accepts: an object or an array.</summary>
    private static bool IsObject(JsonNode? node) => Js.Kind(node) is JsonValueKind.Object or JsonValueKind.Array;

    private static string Json(JsonNode? node) => node?.ToJsonString() ?? "null";
}
