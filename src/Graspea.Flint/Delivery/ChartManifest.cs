using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Delivery;

/// <summary>The manifest a browser fetches once: <c>{"contract":1,"flint":...,"charts":{id:{"input":...}},"groups":{name:[ids]}}</c>.
/// Each chart's <c>input</c> is its template's document without data. Creating a manifest never validates; call
/// <see cref="ChartGroup.Validate"/> for that.</summary>
public sealed class ChartManifest
{
    private readonly byte[] bytes;

    private ChartManifest(IReadOnlyList<ChartGroup> groups, byte[] bytes)
    {
        this.bytes = bytes;
        Groups = groups;
        Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Creates a manifest.</summary>
    /// <param name="groups">At least one group, none null, no two with one name; the array is copied. A chart handle may be in
    /// several groups and is written once; two different handles must not share an id.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groups"/> is null.</exception>
    /// <exception cref="ArgumentException">There is no group, a group is null, a name repeats, or two different handles share an id.</exception>
    public static ChartManifest Create(params ChartGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Length == 0)
        {
            throw new ArgumentException("A manifest needs at least one group.", nameof(groups));
        }

        var copy = (ChartGroup[])groups.Clone();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var handles = new List<ChartHandle>();
        var byId = new Dictionary<string, ChartHandle>(StringComparer.Ordinal);
        foreach (var group in copy)
        {
            if (group is null)
            {
                throw new ArgumentException("A manifest must not hold a null group.", nameof(groups));
            }

            if (!names.Add(group.Name))
            {
                throw new ArgumentException($"The group name '{group.Name}' occurs twice.", nameof(groups));
            }

            foreach (var handle in group.Charts)
            {
                if (byId.TryGetValue(handle.Id, out var known))
                {
                    if (!ReferenceEquals(known, handle))
                    {
                        throw new ArgumentException($"Two different charts share the id '{handle.Id}'.", nameof(groups));
                    }

                    continue;
                }

                byId.Add(handle.Id, handle);
                handles.Add(handle);
            }
        }

        using var buffer = new PooledBufferWriter(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contract", DeliveryContract.Version);
            writer.WriteString("flint", FlintUpstream.Version);
            writer.WriteStartObject("charts");
            foreach (var handle in handles)
            {
                writer.WriteStartObject(handle.Id);
                writer.WritePropertyName("input");
                writer.WriteRawValue(handle.SpecUtf8.Span, skipInputValidation: true);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartObject("groups");
            foreach (var group in copy)
            {
                writer.WriteStartArray(group.Name);
                foreach (var handle in group.Charts)
                {
                    writer.WriteStringValue(handle.Id);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return new ChartManifest(new ReadOnlyCollection<ChartGroup>(copy), buffer.WrittenMemory.ToArray());
    }

    /// <summary>The groups in the order given; a read-only snapshot.</summary>
    public IReadOnlyList<ChartGroup> Groups { get; }

    /// <summary>The manifest as compact UTF-8 JSON. The memory is shared and must not be modified, because
    /// <see cref="Hash"/> and every write use it.</summary>
    public ReadOnlyMemory<byte> Utf8Bytes => bytes;

    /// <summary>The lowercase hex SHA-256 of <see cref="Utf8Bytes"/>. It is deterministic for the same definitions under the
    /// same SDK version and runtime serialisation; no hash is promised across runtimes or across semantically equal documents.</summary>
    public string Hash { get; }

    /// <summary>The manifest as compact JSON text.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => Encoding.UTF8.GetString(bytes);

    /// <summary>Writes the stored bytes to <paramref name="writer"/> as one raw JSON value.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteRawValue(bytes, skipInputValidation: true);
    }
}
