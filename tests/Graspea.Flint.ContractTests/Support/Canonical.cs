using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graspea.Flint.ContractTests.Support;

/// <summary>
/// The contract canonicaliser (design §5.2, §10.1 rule 3): an encoding string shorthand becomes <c>{"field":…}</c>, an
/// annotation with only <c>semanticType</c> becomes its string form, and every number is re-emitted through
/// <see cref="double"/>, because the fixtures come from JavaScript. Applied to both sides before <c>DeepEquals</c>.
/// </summary>
public static class Canonical
{
    public static JsonNode? Of(JsonNode? document)
    {
        var copy = Numbers(document?.DeepClone());
        if (copy?["chart_spec"]?["encodings"] is JsonObject encodings)
        {
            foreach (var channel in encodings.Select(p => p.Key).ToArray())
            {
                encodings[channel] = encodings[channel] switch
                {
                    JsonArray series => new JsonArray(series.Select(e => Shorthand(e?.DeepClone())).ToArray()),
                    var single => Shorthand(single?.DeepClone()),
                };
            }
        }

        if (copy?["semantic_types"] is JsonObject semanticTypes)
        {
            foreach (var field in semanticTypes.Select(p => p.Key).ToArray())
            {
                if (semanticTypes[field] is JsonObject { Count: 1 } annotation && annotation["semanticType"] is JsonValue type && type.GetValueKind() == JsonValueKind.String)
                {
                    semanticTypes[field] = type.GetValue<string>();
                }
            }
        }

        return copy;
    }

    private static JsonNode? Shorthand(JsonNode? encoding) =>
        encoding is JsonValue v && v.GetValueKind() == JsonValueKind.String ? new JsonObject { ["field"] = v.GetValue<string>() } : encoding;

    private static JsonNode? Numbers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToArray())
                {
                    o[key] = Numbers(o[key]?.DeepClone());
                }

                return o;
            case JsonArray a:
                return new JsonArray(a.Select(e => Numbers(e?.DeepClone())).ToArray());
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(v.GetValue<double>());
            default:
                return node;
        }
    }
}
