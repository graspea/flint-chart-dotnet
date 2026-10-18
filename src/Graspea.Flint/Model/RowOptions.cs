using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>
/// How <see cref="ChartData.FromRows{TRow}(IEnumerable{TRow}, RowOptions?)"/> names row keys. Enums are written as
/// member names and nulls are always written; <c>[JsonPropertyName]</c> on a row member wins over the policy.
/// </summary>
public sealed class RowOptions
{
    private JsonSerializerOptions? _serializerOptions;

    /// <summary>camelCase keys (<c>FiscalYear</c> becomes <c>fiscalYear</c>).</summary>
    public static RowOptions Default { get; } = new();

    /// <summary>The key naming policy; null writes member names verbatim. Default: <see cref="JsonNamingPolicy.CamelCase"/>.</summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; } = JsonNamingPolicy.CamelCase;

    [RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]
    [RequiresDynamicCode(RowMaterializer.ReflectionMessage)]
    internal JsonSerializerOptions SerializerOptions() => _serializerOptions ??= new JsonSerializerOptions
    {
        PropertyNamingPolicy = PropertyNamingPolicy,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new NonFiniteAsNullConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
