using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graspea.Flint.Tests.Delivery;

/// <summary>A monthly sales row.</summary>
public sealed record Sale(DateOnly Month, string Region, decimal Revenue);

/// <summary>A traffic row.</summary>
public sealed record Visit(string Source, long Visits);

/// <summary>A sensor row with a nullable value.</summary>
public sealed record Reading(string Station, double? Value);

/// <summary>A row that writes its member as raw JSON with insignificant whitespace.</summary>
[JsonConverter(typeof(RawRowConverter))]
public sealed record RawRow(int Value);

/// <summary>Writes <see cref="RawRow"/> through WriteRawValue.</summary>
public sealed class RawRowConverter : JsonConverter<RawRow>
{
    public override RawRow Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(System.Text.Json.Utf8JsonWriter writer, RawRow value, JsonSerializerOptions options) =>
        writer.WriteRawValue($"{{ \"value\" : {value.Value} }}");
}

/// <summary>A row holding a nested node.</summary>
public sealed record Nested(System.Text.Json.Nodes.JsonNode? Value);

/// <summary>A row with a value-dependent member.</summary>
public sealed record Note(string City, string? Text);

/// <summary>A row that counts how often its member is read.</summary>
public sealed class Counted
{
    public static int Reads;

    public string City
    {
        get
        {
            Reads++;
            return "Oslo";
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Sale))]
[JsonSerializable(typeof(Visit))]
[JsonSerializable(typeof(Reading))]
[JsonSerializable(typeof(RawRow))]
[JsonSerializable(typeof(Nested))]
[JsonSerializable(typeof(Counted))]
[JsonSerializable(typeof(object))]
internal sealed partial class DeliveryJson : JsonSerializerContext;
