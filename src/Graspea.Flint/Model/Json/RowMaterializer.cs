using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>Writes rows to JSON once, at the entry point, so the document is fixed before it is serialised (design §5.3).</summary>
internal static class RowMaterializer
{
    internal const string ReflectionMessage = "Row types are serialised by reflection; use the JsonTypeInfo overload for trimmed or AOT hosts.";

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    internal static JsonTypeInfo<TRow> TypeInfo<TRow>(RowOptions? options) =>
        (JsonTypeInfo<TRow>)(options ?? RowOptions.Default).SerializerOptions().GetTypeInfo(typeof(TRow));

    internal static JsonArray Materialize<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(typeInfo);
        EnsureRowMetadata(typeInfo);
        var runtimeInfos = typeof(TRow) == typeof(object) ? new Dictionary<Type, JsonTypeInfo>() : null;
        var values = new JsonArray();
        foreach (var row in rows)
        {
            // Rows typed as object are written by their runtime type, so the guarantees are checked on that type.
            JsonTypeInfo rowInfo = typeInfo;
            if (runtimeInfos is not null && row is not null && !runtimeInfos.TryGetValue(row.GetType(), out rowInfo!))
            {
                rowInfo = typeInfo.Options.GetTypeInfo(row.GetType());
                EnsureRowMetadata(rowInfo);
                runtimeInfos.Add(row.GetType(), rowInfo);
            }

            var node = JsonSerializer.SerializeToNode(row, typeInfo);
            EnsureEveryMemberWritten(node, rowInfo);
            values.Add(node);
        }

        return values;
    }

    /// <summary>A key dropped from <c>data[0]</c> silently breaks key detection and static series, so metadata that may
    /// omit a null member is refused (design §5.3, null guarantee). So is metadata that writes numbers as strings, which
    /// flint-chart reads as text instead of quantities.</summary>
    internal static void EnsureRowMetadata(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Options.DefaultIgnoreCondition != JsonIgnoreCondition.Never)
        {
            throw new ArgumentException(
                $"Row metadata for {typeInfo.Type} uses DefaultIgnoreCondition.{typeInfo.Options.DefaultIgnoreCondition}; rows must write nulls (Never).",
                nameof(typeInfo));
        }

        if (WritesNumbersAsStrings(typeInfo.Options.NumberHandling) || WritesNumbersAsStrings(typeInfo.NumberHandling))
        {
            throw new ArgumentException(
                $"Row metadata for {typeInfo.Type} uses JsonNumberHandling.WriteAsString; rows must write numbers as JSON numbers.",
                nameof(typeInfo));
        }

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        if (typeInfo.Properties.FirstOrDefault(p => p.Get is not null && WritesNumbersAsStrings(p.NumberHandling)) is { } asString)
        {
            throw new ArgumentException(
                $"Row member '{asString.Name}' of {typeInfo.Type} uses JsonNumberHandling.WriteAsString; rows must write numbers as JSON numbers.",
                nameof(typeInfo));
        }

        if (typeInfo.Properties.FirstOrDefault(IsConditional) is { } conditional)
        {
            throw Conditional(conditional.Name, typeInfo.Type);
        }
    }

    /// <summary>A predicate can depend on the value as well as on null, which no probe call can rule out, so every written
    /// row is checked for every member the metadata writes.</summary>
    private static void EnsureEveryMemberWritten(JsonNode? node, JsonTypeInfo typeInfo)
    {
        if (node is not JsonObject row || typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property is { Get: not null, IsExtensionData: false } && !row.ContainsKey(property.Name))
            {
                throw Conditional(property.Name, typeInfo.Type);
            }
        }
    }

    private static ArgumentException Conditional(string member, Type type) =>
        new($"Row member '{member}' of {type} is written conditionally; rows must write every member, nulls included.", "typeInfo");

    private static bool WritesNumbersAsStrings(JsonNumberHandling? handling) =>
        handling is { } h && (h & JsonNumberHandling.WriteAsString) != 0;

    /// <summary>
    /// True when the member may be written for some rows and omitted for others. A member ignored always
    /// (<c>[JsonIgnore]</c>: no getter in the metadata) is never a key, and one marked <c>Never</c> is always written;
    /// both are safe. Every other <see cref="JsonPropertyInfo.ShouldSerialize"/> predicate is conditional.
    /// </summary>
    private static bool IsConditional(JsonPropertyInfo property) =>
        property is { ShouldSerialize: { } shouldSerialize, Get: not null } && !WritesNull(shouldSerialize);

    private static bool WritesNull(Func<object, object?, bool> shouldSerialize)
    {
        try
        {
            return shouldSerialize(null!, null);
        }
        catch (Exception e) when (e is NullReferenceException or InvalidCastException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The wire name of a selected row member, exactly as the rows are written (design §5.3 rule 1): matched through
    /// <see cref="JsonPropertyInfo.AttributeProvider"/>; where that is null (net8 source generation), through the
    /// member's own <see cref="JsonPropertyNameAttribute"/> or the naming policy, checked against the serialised members.
    /// </summary>
    internal static string FieldName(JsonTypeInfo typeInfo, LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(selector);
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? convert.Operand : selector.Body;
        if (selector.Parameters.Count != 1
            || body is not MemberExpression { Member: PropertyInfo or FieldInfo } access
            || access.Expression != selector.Parameters[0])
        {
            throw new ArgumentException($"The selector must read a property or field of the row, such as r => r.Month; got '{selector}'.", nameof(selector));
        }

        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            var member = access.Member;
            var written = typeInfo.Properties.Where(p => p.Get is not null).ToList();
            foreach (var property in written)
            {
                if (property.AttributeProvider is MemberInfo info && info.Module == member.Module && info.MetadataToken == member.MetadataToken)
                {
                    return property.Name;
                }
            }

            var candidate = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? typeInfo.Options.PropertyNamingPolicy?.ConvertName(member.Name)
                ?? member.Name;
            if (written.Any(p => p.AttributeProvider is null && p.Name == candidate))
            {
                return candidate;
            }
        }

        throw new ArgumentException($"Member '{access.Member.Name}' is not written as a key of {typeInfo.Type}.", nameof(selector));
    }

    /// <summary>The keys of the first row in the order JavaScript's <c>Object.keys</c> lists them: array-index keys
    /// ascending, then the other keys in insertion order (ECMA-262 OrdinaryOwnPropertyKeys).</summary>
    internal static IReadOnlyList<string> FirstRowKeys(JsonArray? values)
    {
        if (values is not [JsonObject first, ..])
        {
            return [];
        }

        var keys = first.Select(p => p.Key).ToList();
        var indices = keys.Where(IsArrayIndex).OrderBy(k => uint.Parse(k, System.Globalization.CultureInfo.InvariantCulture));
        return [.. indices, .. keys.Where(k => !IsArrayIndex(k))];
    }

    private static bool IsArrayIndex(string key) =>
        uint.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
        && index != uint.MaxValue
        && index.ToString(System.Globalization.CultureInfo.InvariantCulture) == key;
}
