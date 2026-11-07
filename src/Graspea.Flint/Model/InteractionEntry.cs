using System.Text.Json.Nodes;

namespace Graspea.Flint;

/// <summary>The hand-written part of the generated <see cref="InteractionEntry"/>: the helper of the preset factories.</summary>
public sealed partial class InteractionEntry
{
    /// <summary>An entry of <paramref name="type"/> whose <see cref="Options"/> are the members of <paramref name="options"/>;
    /// options without a member set write none.</summary>
    private static InteractionEntry Preset(string type, string? id, JsonNode? options)
    {
        Dictionary<string, JsonNode?>? map = null;
        if (options is JsonObject { Count: > 0 } members)
        {
            map = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var (key, value) in members.ToArray())
            {
                members.Remove(key);
                map[key] = value;
            }
        }

        return new InteractionEntry { Type = type, Id = id, Options = map };
    }
}
