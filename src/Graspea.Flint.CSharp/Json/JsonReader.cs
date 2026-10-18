using System.Text;
using System.Threading;

namespace Graspea.Flint.CSharp;

/// <summary>
/// A strict RFC 8259 reader with positions (design §12.2.2). It refuses what <c>FlintJson.Deserialize</c> refuses: comments,
/// trailing commas, leading zeros, invalid escapes, lone surrogates and nesting deeper than 64. It also refuses duplicate
/// keys, which System.Text.Json would silently resolve to the last one. Object members keep their input order and numbers
/// their raw text.
/// </summary>
internal sealed class JsonReader
{
    /// <summary>The System.Text.Json default, which <c>FlintJson.Deserialize</c> reads with.</summary>
    internal const int MaxDepth = 64;

    private const int CancellationInterval = 4096;

    private readonly string _text;
    private readonly CancellationToken _cancellationToken;
    private int _position;
    private int _line = 1;
    private int _lineStart;
    private int _tokens;

    private JsonReader(string text, CancellationToken cancellationToken)
    {
        _text = text;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Reads one JSON value that fills the whole text, surrounded by optional whitespace.</summary>
    /// <exception cref="JsonReadException">The text is not valid JSON.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled; it is checked every 4096 values.</exception>
    public static JsonItem Parse(string text, CancellationToken cancellationToken = default)
    {
        var reader = new JsonReader(text, cancellationToken);
        reader.SkipWhitespace();
        var root = reader.ReadValue(0);
        reader.SkipWhitespace();
        if (reader._position < text.Length)
        {
            throw reader.Error("Unexpected text after the JSON value.");
        }

        return root;
    }

    private TextPosition Here => new(_position, _line, _position - _lineStart + 1);

    private JsonReadException Error(string message) => new(message, Here);

    private JsonReadException Error(string message, TextPosition at) => new(message, at);

    private void SkipWhitespace()
    {
        while (_position < _text.Length)
        {
            switch (_text[_position])
            {
                case ' ':
                case '\t':
                case '\r':
                    _position++;
                    break;
                case '\n':
                    _position++;
                    _line++;
                    _lineStart = _position;
                    break;
                default:
                    return;
            }
        }
    }

    private JsonItem ReadValue(int depth)
    {
        if (++_tokens % CancellationInterval == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }

        if (_position >= _text.Length)
        {
            throw Error("Unexpected end of the JSON text.");
        }

        var start = Here;
        switch (_text[_position])
        {
            case '{':
                return ReadObject(depth + 1, start);
            case '[':
                return ReadArray(depth + 1, start);
            case '"':
                var value = ReadString();
                return JsonItem.Scalar(JsonItemKind.String, value, start, _position);
            case 't':
                return ReadLiteral("true", JsonItemKind.True, start);
            case 'f':
                return ReadLiteral("false", JsonItemKind.False, start);
            case 'n':
                return ReadLiteral("null", JsonItemKind.Null, start);
            default:
                if (_text[_position] == '-' || IsDigit(_text[_position]))
                {
                    return ReadNumber(start);
                }

                throw Error($"Unexpected character '{Describe(_text[_position])}'.");
        }
    }

    private JsonItem ReadObject(int depth, TextPosition start)
    {
        if (depth > MaxDepth)
        {
            throw Error($"The JSON is nested deeper than {MaxDepth} levels.");
        }

        _position++;
        var members = new List<JsonMember>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        SkipWhitespace();
        if (Peek() == '}')
        {
            _position++;
            return JsonItem.Object(members, start, _position);
        }

        while (true)
        {
            SkipWhitespace();
            if (Peek() != '"')
            {
                throw Error("Expected a string key.");
            }

            var keyPosition = Here;
            var key = ReadString();
            var keyEnd = _position;
            if (!keys.Add(key))
            {
                throw Error($"Duplicate key \"{key}\".", keyPosition);
            }

            SkipWhitespace();
            if (Peek() != ':')
            {
                throw Error("Expected ':' after a key.");
            }

            _position++;
            SkipWhitespace();
            members.Add(new JsonMember(key, keyPosition, keyEnd, ReadValue(depth)));
            SkipWhitespace();
            switch (Peek())
            {
                case ',':
                    _position++;
                    continue;
                case '}':
                    _position++;
                    return JsonItem.Object(members, start, _position);
                default:
                    throw Error("Expected ',' or '}'.");
            }
        }
    }

    private JsonItem ReadArray(int depth, TextPosition start)
    {
        if (depth > MaxDepth)
        {
            throw Error($"The JSON is nested deeper than {MaxDepth} levels.");
        }

        _position++;
        var items = new List<JsonItem>();
        SkipWhitespace();
        if (Peek() == ']')
        {
            _position++;
            return JsonItem.Array(items, start, _position);
        }

        while (true)
        {
            SkipWhitespace();
            items.Add(ReadValue(depth));
            SkipWhitespace();
            switch (Peek())
            {
                case ',':
                    _position++;
                    continue;
                case ']':
                    _position++;
                    return JsonItem.Array(items, start, _position);
                default:
                    throw Error("Expected ',' or ']'.");
            }
        }
    }

    private JsonItem ReadLiteral(string literal, JsonItemKind kind, TextPosition start)
    {
        if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
        {
            throw Error("Invalid literal.", start);
        }

        _position += literal.Length;
        return JsonItem.Scalar(kind, null, start, _position);
    }

    private JsonItem ReadNumber(TextPosition start)
    {
        var begin = _position;
        if (Peek() == '-')
        {
            _position++;
        }

        if (Peek() == '0')
        {
            _position++;
        }
        else if (IsDigit(Peek()))
        {
            SkipDigits();
        }
        else
        {
            throw Error("Invalid number.");
        }

        if (Peek() == '.')
        {
            _position++;
            if (!IsDigit(Peek()))
            {
                throw Error("Invalid number: a digit must follow the decimal point.");
            }

            SkipDigits();
        }

        if (Peek() is 'e' or 'E')
        {
            _position++;
            if (Peek() is '+' or '-')
            {
                _position++;
            }

            if (!IsDigit(Peek()))
            {
                throw Error("Invalid number: a digit must follow the exponent.");
            }

            SkipDigits();
        }

        return JsonItem.Scalar(JsonItemKind.Number, _text.Substring(begin, _position - begin), start, _position);
    }

    private string ReadString()
    {
        _position++;
        var builder = new StringBuilder();
        while (true)
        {
            if (_position >= _text.Length)
            {
                throw Error("Unterminated string.");
            }

            var c = _text[_position];
            if (c == '"')
            {
                _position++;
                return builder.ToString();
            }

            if (c == '\\')
            {
                ReadEscape(builder);
                continue;
            }

            if (c < 0x20)
            {
                throw Error("A control character must be escaped in a string.");
            }

            if (char.IsHighSurrogate(c) && _position + 1 < _text.Length && char.IsLowSurrogate(_text[_position + 1]))
            {
                builder.Append(c).Append(_text[_position + 1]);
                _position += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw Error("A string holds a lone surrogate.");
            }

            builder.Append(c);
            _position++;
        }
    }

    private void ReadEscape(StringBuilder builder)
    {
        var at = Here;
        _position++;
        switch (Peek())
        {
            case '"': builder.Append('"'); break;
            case '\\': builder.Append('\\'); break;
            case '/': builder.Append('/'); break;
            case 'b': builder.Append('\b'); break;
            case 'f': builder.Append('\f'); break;
            case 'n': builder.Append('\n'); break;
            case 'r': builder.Append('\r'); break;
            case 't': builder.Append('\t'); break;
            case 'u':
                _position++;
                var unit = ReadHex4(at);
                if (char.IsHighSurrogate(unit))
                {
                    if (Peek() != '\\' || _position + 1 >= _text.Length || _text[_position + 1] != 'u')
                    {
                        throw Error("A \\u escape holds a lone surrogate.", at);
                    }

                    _position += 2;
                    var low = ReadHex4(at);
                    if (!char.IsLowSurrogate(low))
                    {
                        throw Error("A \\u escape holds a lone surrogate.", at);
                    }

                    builder.Append(unit).Append(low);
                }
                else if (char.IsLowSurrogate(unit))
                {
                    throw Error("A \\u escape holds a lone surrogate.", at);
                }
                else
                {
                    builder.Append(unit);
                }

                return;
            default:
                throw Error("Invalid escape sequence.", at);
        }

        _position++;
    }

    private char ReadHex4(TextPosition at)
    {
        if (_position + 4 > _text.Length)
        {
            throw Error("Invalid \\u escape.", at);
        }

        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = _text[_position + i];
            var digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            if (digit < 0)
            {
                throw Error("Invalid \\u escape.", at);
            }

            value = (value * 16) + digit;
        }

        _position += 4;
        return (char)value;
    }

    private void SkipDigits()
    {
        while (IsDigit(Peek()))
        {
            _position++;
        }
    }

    private char Peek() => _position < _text.Length ? _text[_position] : '\0';

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static string Describe(char c) => c < 0x20 || c > 0x7e ? $"\\u{(int)c:X4}" : c.ToString();
}
