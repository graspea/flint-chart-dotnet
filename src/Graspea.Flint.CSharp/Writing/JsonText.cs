using System.Text;

namespace Graspea.Flint.CSharp;

/// <summary>Compact JSON text of a <see cref="JsonItem"/>: members in input order, numbers as their raw text.</summary>
internal static class JsonText
{
    /// <summary>
    /// The value as compact JSON. Strings escape the quote, the backslash and controls, and every character a C# string
    /// literal escapes (<see cref="CSharpText.MustEscape"/>), so the text can sit in a raw string literal without putting an
    /// invisible or bidirectional character into the source.
    /// </summary>
    public static string Compact(JsonItem item)
    {
        var builder = new StringBuilder();
        Write(builder, item);
        return builder.ToString();
    }

    /// <summary>A JSON string literal of <paramref name="value"/>, escaped as <see cref="Compact"/> escapes strings.</summary>
    public static string String(string value)
    {
        var builder = new StringBuilder();
        WriteString(builder, value);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonItem item)
    {
        switch (item.Kind)
        {
            case JsonItemKind.Object:
                builder.Append('{');
                for (var i = 0; i < item.Members.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    WriteString(builder, item.Members[i].Key);
                    builder.Append(':');
                    Write(builder, item.Members[i].Value);
                }

                builder.Append('}');
                break;
            case JsonItemKind.Array:
                builder.Append('[');
                for (var i = 0; i < item.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    Write(builder, item.Items[i]);
                }

                builder.Append(']');
                break;
            case JsonItemKind.String:
                WriteString(builder, item.Value!);
                break;
            case JsonItemKind.Number:
                builder.Append(item.Value);
                break;
            case JsonItemKind.True:
                builder.Append("true");
                break;
            case JsonItemKind.False:
                builder.Append("false");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); continue;
                case '\\': builder.Append("\\\\"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case '\t': builder.Append("\\t"); continue;
                case '\b': builder.Append("\\b"); continue;
                case '\f': builder.Append("\\f"); continue;
            }

            if (CSharpText.MustEscape(value, i))
            {
                builder.Append($"\\u{(int)c:X4}");
                if (char.IsSurrogatePair(value, i))
                {
                    builder.Append($"\\u{(int)value[++i]:X4}");
                }
            }
            else
            {
                builder.Append(c);
                if (char.IsSurrogatePair(value, i))
                {
                    builder.Append(value[++i]);
                }
            }
        }

        builder.Append('"');
    }
}
