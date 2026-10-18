namespace Graspea.Flint.CSharp;

/// <summary>A position in the JSON text: the character offset and the 1-based line and column.</summary>
internal readonly record struct TextPosition(int Offset, int Line, int Column);

/// <summary>The kind of a JSON value.</summary>
internal enum JsonItemKind
{
    Object,
    Array,
    String,
    Number,
    True,
    False,
    Null,
}

/// <summary>
/// A JSON value read by <see cref="JsonReader"/>: object members in input order, the raw text of every number and the
/// position of every value (design §12.2.2).
/// </summary>
internal sealed class JsonItem
{
    private static readonly JsonMember[] NoMembers = [];
    private static readonly JsonItem[] NoItems = [];

    private JsonItem(JsonItemKind kind, string? value, IReadOnlyList<JsonMember> members, IReadOnlyList<JsonItem> items, TextPosition start, int end)
    {
        Kind = kind;
        Value = value;
        Members = members;
        Items = items;
        Start = start;
        End = end;
    }

    public JsonItemKind Kind { get; }

    /// <summary>The decoded string, or the raw text of a number; null for the other kinds.</summary>
    public string? Value { get; }

    /// <summary>The members of an object, in input order; empty otherwise.</summary>
    public IReadOnlyList<JsonMember> Members { get; }

    /// <summary>The elements of an array; empty otherwise.</summary>
    public IReadOnlyList<JsonItem> Items { get; }

    /// <summary>Where the value starts.</summary>
    public TextPosition Start { get; }

    /// <summary>The offset just after the value.</summary>
    public int End { get; }

    public bool IsObject => Kind == JsonItemKind.Object;

    public bool IsArray => Kind == JsonItemKind.Array;

    public bool IsString => Kind == JsonItemKind.String;

    public bool IsNumber => Kind == JsonItemKind.Number;

    public bool IsBoolean => Kind is JsonItemKind.True or JsonItemKind.False;

    public bool IsNull => Kind == JsonItemKind.Null;

    /// <summary>The value of an object member; null when the object has no such key or this is not an object.</summary>
    public JsonItem? Get(string key)
    {
        foreach (var member in Members)
        {
            if (member.Key == key)
            {
                return member.Value;
            }
        }

        return null;
    }

    internal static JsonItem Scalar(JsonItemKind kind, string? value, TextPosition start, int end) => new(kind, value, NoMembers, NoItems, start, end);

    internal static JsonItem Object(List<JsonMember> members, TextPosition start, int end) => new(JsonItemKind.Object, null, members, NoItems, start, end);

    internal static JsonItem Array(List<JsonItem> items, TextPosition start, int end) => new(JsonItemKind.Array, null, NoMembers, items, start, end);
}

/// <summary>An object member: the decoded key, where the key starts and ends (after its closing quote), and the value.</summary>
internal sealed class JsonMember(string key, TextPosition keyPosition, int keyEnd, JsonItem value)
{
    public JsonMember(string key, TextPosition keyPosition, JsonItem value)
        : this(key, keyPosition, keyPosition.Offset, value)
    {
    }

    public string Key { get; } = key;

    public TextPosition KeyPosition { get; } = keyPosition;

    /// <summary>The offset just after the key's closing quote.</summary>
    public int KeyEnd { get; } = keyEnd;

    public JsonItem Value { get; } = value;
}

/// <summary>Invalid JSON, with the position of the offending character.</summary>
internal sealed class JsonReadException(string message, TextPosition position) : Exception(message)
{
    public TextPosition Position { get; } = position;
}
