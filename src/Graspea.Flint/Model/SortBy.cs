using System.Text.Encodings.Web;
using System.Text.Json;

namespace Graspea.Flint;

/// <summary>Helpers for <see cref="ChartEncoding.SortBy"/>.</summary>
public static class SortBy
{
    /// <summary>An explicit category order, written as the JSON-encoded array string that <c>sortBy</c> accepts.</summary>
    /// <param name="order">The categories in display order.</param>
    /// <returns>A compact JSON array, such as <c>["Jan","Feb"]</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is null.</exception>
    public static string ExplicitOrder(params string[] order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var item in order)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
