using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Tests.Support;

/// <summary>Host options that would change every wire rule attached to options instead of types (design §5.1).</summary>
public static class HostileOptions
{
    public static JsonSerializerOptions Create() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IgnoreReadOnlyProperties = true,
        NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,
    };
}
