using System.Text;
using System.Text.Json;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Delivery;

/// <summary>The error document a host answers instead of a page: <c>{"contract":1,"error":{"code":...}}</c>.</summary>
public sealed class DeliveryError
{
    /// <summary>Creates an error document.</summary>
    /// <param name="code">A non-empty, host-defined code, written as given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
    public DeliveryError(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length == 0)
        {
            throw new ArgumentException("An error code must not be empty.", nameof(code));
        }

        Code = code;
    }

    /// <summary>The error code.</summary>
    public string Code { get; }

    /// <summary>Writes the document to <paramref name="writer"/>.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteNumber("contract", DeliveryContract.Version);
        writer.WriteStartObject("error");
        writer.WriteString("code", Code);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>The document as compact UTF-8 JSON.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToUtf8Bytes()
    {
        using var buffer = new PooledBufferWriter(64);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTo(writer);
        }

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>The document as compact JSON text.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => Encoding.UTF8.GetString(ToUtf8Bytes());
}
