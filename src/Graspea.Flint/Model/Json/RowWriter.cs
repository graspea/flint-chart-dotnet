using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Graspea.Flint.Serialization;

/// <summary>
/// Writes the rows of one source through <see cref="JsonTypeInfo{T}"/> (design §5.3, §13.5): the metadata is checked once,
/// at creation, before any row is read; a row is checked for every member only where a <c>ShouldSerialize</c> predicate
/// could omit one. One instance per source: it caches the metadata of runtime row types and is not thread-safe.
/// </summary>
internal sealed class RowWriter<TRow>
{
    private readonly Dictionary<Type, (JsonTypeInfo Info, bool Check)>? runtimeInfos;
    private readonly bool check;

    internal RowWriter(JsonTypeInfo<TRow> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        RowMaterializer.EnsureRowMetadata(typeInfo);
        TypeInfo = typeInfo;
        check = HasPredicate(typeInfo);

        // Rows typed as object are written by their runtime type, so the guarantees are checked on that type.
        runtimeInfos = typeof(TRow) == typeof(object) ? [] : null;
    }

    internal JsonTypeInfo<TRow> TypeInfo { get; }

    /// <summary>The row as a node, as <see cref="ChartData.FromRows{TRow}(IEnumerable{TRow}, JsonTypeInfo{TRow})"/> holds it.</summary>
    internal JsonNode? ToNode(TRow row)
    {
        var (rowInfo, _) = RowInfo(row);
        var node = JsonSerializer.SerializeToNode(row, TypeInfo);
        RowMaterializer.EnsureEveryMemberWritten(node, rowInfo);
        return node;
    }

    /// <summary>Writes the row to <paramref name="writer"/>, with the bytes <see cref="ToNode"/> would be written as. Without a
    /// predicate the row goes straight to the writer; otherwise through its node and the member check.</summary>
    internal void Write(Utf8JsonWriter writer, TRow row)
    {
        var (rowInfo, checkRow) = RowInfo(row);
        if (!checkRow)
        {
            JsonSerializer.Serialize(writer, row, TypeInfo);
            return;
        }

        var node = JsonSerializer.SerializeToNode(row, TypeInfo);
        RowMaterializer.EnsureEveryMemberWritten(node, rowInfo);
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            node.WriteTo(writer);
        }
    }

    private (JsonTypeInfo Info, bool Check) RowInfo(TRow row)
    {
        if (runtimeInfos is null || row is null)
        {
            return (TypeInfo, check);
        }

        var type = row.GetType();
        if (!runtimeInfos.TryGetValue(type, out var info))
        {
            var rowInfo = TypeInfo.Options.GetTypeInfo(type);
            RowMaterializer.EnsureRowMetadata(rowInfo);
            info = runtimeInfos[type] = (rowInfo, HasPredicate(rowInfo));
        }

        return info;
    }

    private static bool HasPredicate(JsonTypeInfo typeInfo) =>
        typeInfo.Kind == JsonTypeInfoKind.Object && typeInfo.Properties.Any(p => p is { Get: not null, ShouldSerialize: not null });
}
