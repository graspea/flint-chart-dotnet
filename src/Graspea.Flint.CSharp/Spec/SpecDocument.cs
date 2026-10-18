namespace Graspea.Flint.CSharp;

/// <summary>A field an encoding binds: the channel, the field and the encoding's explicit <c>type</c>, if any.</summary>
internal sealed record EncodingField(string Channel, string Field, string? Type);

/// <summary>
/// A <c>ChartAssemblyInput</c> document as JSON (design §12.3): typed access to the members the conversion reads, with
/// the JSON values kept as they are, so unknown keys and raw numbers survive.
/// </summary>
internal sealed class SpecDocument(JsonItem root)
{
    public JsonItem Root { get; } = root;

    public JsonItem? Data => Root.Get("data");

    /// <summary><c>data.values</c> when it is an array.</summary>
    public JsonItem? Values => Data is { IsObject: true } data && data.Get("values") is { IsArray: true } values ? values : null;

    public JsonItem? ChartSpec => Root.Get("chart_spec");

    public string? ChartType => ChartSpec?.Get("chartType") is { IsString: true } type ? type.Value : null;

    public JsonItem? Encodings => ChartSpec is { IsObject: true } spec && spec.Get("encodings") is { IsObject: true } encodings ? encodings : null;

    public JsonItem? SemanticTypes => Root.Get("semantic_types") is { IsObject: true } types ? types : null;

    public JsonItem? FieldDisplayNames => Root.Get("field_display_names") is { IsObject: true } names ? names : null;

    /// <summary>Every field an encoding binds, series entries included, in input order.</summary>
    public IEnumerable<EncodingField> EncodingFields()
    {
        foreach (var member in Encodings?.Members ?? [])
        {
            var entries = member.Value.IsArray ? member.Value.Items : [member.Value];
            foreach (var entry in entries)
            {
                if (entry.IsString)
                {
                    yield return new EncodingField(member.Key, entry.Value!, null);
                }
                else if (entry.IsObject && entry.Get("field") is { IsString: true } field)
                {
                    yield return new EncodingField(member.Key, field.Value!, entry.Get("type") is { IsString: true } type ? type.Value : null);
                }
            }
        }
    }

    /// <summary>The semantic type name annotated on a field (the string form or the object's <c>semanticType</c>).</summary>
    public string? SemanticTypeOf(string field) =>
        SemanticTypes?.Get(field) switch
        {
            { IsString: true } name => name.Value,
            { IsObject: true } annotation when annotation.Get("semanticType") is { IsString: true } name => name.Value,
            _ => null,
        };
}
