using System.Globalization;

namespace Graspea.Flint.CSharp;

/// <summary>
/// C# expressions for JSON values, shared by the fluent and the model writers. Every typed expression is used only when
/// the value it writes, compared as JSON, equals the input value (design §12.3, the fidelity rule); the methods return
/// null otherwise, and the caller falls back to an <c>Extensions</c> entry or a raw JSON value.
/// </summary>
internal sealed class ValueWriter
{
    private readonly SortedSet<string> _usings;

    public ValueWriter(SortedSet<string> usings, bool qualified = false)
    {
        _usings = usings;
        Qualified = qualified;
    }

    /// <summary>Whether every type is written <c>global::</c>-qualified, for generated code that has no using directives
    /// (design §12.6.3); otherwise types are written by their simple names and their namespaces are collected as usings.</summary>
    public bool Qualified { get; }

    /// <summary>Whether a <c>Json</c> helper call was written, so the class needs the helper.</summary>
    public bool UsesJsonHelper { get; private set; }

    /// <summary>The private helper that parses an <c>Extensions</c> value: <c>Json</c>, or the reserved <c>__flint_Json</c>
    /// in generated code (design §12.6.3).</summary>
    public string JsonHelper => Qualified ? "__flint_Json" : "Json";

    public void Using(string ns) => _usings.Add(ns);

    /// <summary>A type of <paramref name="ns"/>: its simple name, with the namespace added to the usings, or
    /// <c>global::</c>-qualified.</summary>
    public string Type(string ns, string name)
    {
        if (Qualified)
        {
            return $"global::{ns}.{name}";
        }

        Using(ns);
        return name;
    }

    /// <summary>A type of <c>Graspea.Flint</c>, such as <c>Chart</c> or <c>LineChartInterpolate</c>.</summary>
    public string Flint(string name) => Type("Graspea.Flint", name);

    /// <summary>A <c>JsonElement</c> for an <c>Extensions</c> entry: <c>Json("…")</c>.</summary>
    public string Element(JsonItem item)
    {
        UsesJsonHelper = true;
        if (!Qualified)
        {
            Using("System.Text.Json");
        }

        return $"{JsonHelper}({Fragment(item)})";
    }

    /// <summary>A <c>JsonNode?</c> value: scalars through the implicit conversions, the rest parsed.</summary>
    public string Node(JsonItem item)
    {
        switch (item.Kind)
        {
            case JsonItemKind.String:
                return CSharpText.StringLiteral(item.Value!);
            case JsonItemKind.Number when CSharpText.FitsDouble(item.Value!):
                return CSharpText.NumberLiteral(item.Value!);
            case JsonItemKind.True:
                return "true";
            case JsonItemKind.False:
                return "false";
            case JsonItemKind.Null:
                return "null";
            default:
                return $"{Type("System.Text.Json.Nodes", "JsonNode")}.Parse({Fragment(item)})";
        }
    }

    /// <summary>A C# literal holding the compact JSON of the value: a raw string for an object or an array, a regular
    /// string otherwise.</summary>
    public static string Fragment(JsonItem item)
    {
        var json = JsonText.Compact(item);
        return item.IsObject || item.IsArray ? CSharpText.RawStringLiteral(json) : CSharpText.StringLiteral(json);
    }

    /// <summary>A number as a <c>double</c> argument, or null when <c>double</c> cannot hold it.</summary>
    public static string? Number(JsonItem item) =>
        item.IsNumber && CSharpText.FitsDouble(item.Value!) ? CSharpText.NumberLiteral(item.Value!) : null;

    /// <summary>A string-backed constant: <c>Type.Member</c> when registered, <c>new Type("…")</c> otherwise.</summary>
    public string Constant(string type, string value, string? member) =>
        member is null ? $"new {Flint(type)}({CSharpText.StringLiteral(value)})" : $"{Flint(type)}.{member}";

    /// <summary>
    /// A value of a generated options or theming member type (<see cref="MemberName.Type"/>), or null when the type cannot
    /// write exactly this value.
    /// </summary>
    public string? Typed(JsonItem item, string type)
    {
        switch (type)
        {
            case "string":
                return item.IsString ? CSharpText.StringLiteral(item.Value!) : null;
            case "double":
                return Number(item);
            case "bool":
                return item.IsBoolean ? (item.Kind == JsonItemKind.True ? "true" : "false") : null;
            case "JsonNode":
            case "JsonNode?":
                return item.IsNull ? null : Node(item);
            case "Graspea.Flint.ChartSize":
                return Size(item);
            case "Graspea.Flint.ContinuousMarkCrossSection":
                return CrossSection(item);
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            if (!item.IsArray)
            {
                return null;
            }

            var element = type.Substring(0, type.Length - 2);
            var items = new List<string>();
            foreach (var entry in item.Items)
            {
                var value = entry.IsNull && IsReference(element) ? "null!" : Typed(entry, element);
                if (value is null)
                {
                    return null;
                }

                items.Add(value);
            }

            return $"[{string.Join(", ", items)}]";
        }

        const string map = "Dictionary<string, ";
        if (type.StartsWith(map, StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
        {
            if (!item.IsObject)
            {
                return null;
            }

            var valueType = type.Substring(map.Length, type.Length - map.Length - 1);
            var entries = new List<string>();
            foreach (var member in item.Members)
            {
                var value = member.Value.IsNull ? (valueType == "JsonNode?" ? "null" : IsReference(valueType) ? "null!" : null) : Typed(member.Value, valueType);
                if (value is null)
                {
                    return null;
                }

                entries.Add($"[{CSharpText.StringLiteral(member.Key)}] = {value}");
            }

            return entries.Count == 0 ? "new()" : $"new() {{ {string.Join(", ", entries)} }}";
        }

        return NameTables.Class(type) is { } names && item.IsObject ? Object(names, item) : null;
    }

    /// <summary><c>new T { … }</c> for a generated class: typed members where they write the value, the rest (unknown keys,
    /// nulls, values of another kind) in <c>Extensions</c>.</summary>
    public string Object(ClassNames names, JsonItem item)
    {
        var (assignments, extensions) = Members(names, item);
        if (extensions.Count > 0)
        {
            assignments.Add($"Extensions = {Extensions(extensions)}");
        }

        var type = TypeName(names.Name);
        return assignments.Count == 0 ? $"new {type}()" : $"new {type} {{ {string.Join(", ", assignments)} }}";
    }

    /// <summary>The typed assignments (<c>Name = value</c>) and the members left for <c>Extensions</c>.</summary>
    public (List<string> Assignments, List<JsonMember> Extensions) Members(ClassNames names, JsonItem item)
    {
        var assignments = new List<string>();
        var extensions = new List<JsonMember>();
        foreach (var member in item.Members)
        {
            var declared = names.Members.FirstOrDefault(m => m.Wire == member.Key);
            var value = declared is null ? null : Typed(member.Value, declared.Type);
            if (value is null)
            {
                extensions.Add(member);
            }
            else
            {
                assignments.Add($"{declared!.Name} = {value}");
            }
        }

        return (assignments, extensions);
    }

    /// <summary><c>new() { ["k"] = Json("…"), … }</c> for an <c>Extensions</c> dictionary.</summary>
    public string Extensions(IEnumerable<JsonMember> members) =>
        $"new() {{ {string.Join(", ", members.Select(m => $"[{CSharpText.StringLiteral(m.Key)}] = {Element(m.Value)}"))} }}";

    /// <summary>The simple name of a generated class, adding the using of its namespace.</summary>
    public string TypeName(string qualified) => Type(NameTables.Namespace(qualified), NameTables.SimpleName(qualified));

    private string? Size(JsonItem item)
    {
        if (!item.IsObject || item.Members.Count != 2 || item.Get("width") is not { } width || item.Get("height") is not { } height)
        {
            return null;
        }

        return Number(width) is { } w && Number(height) is { } h ? $"new {Flint("ChartSize")}({w}, {h})" : null;
    }

    private string? CrossSection(JsonItem item)
    {
        if (Number(item) is { } uniform)
        {
            return uniform;
        }

        if (!item.IsObject || item.Get("x") is not { } xItem || item.Get("y") is not { } yItem || Number(xItem) is not { } x || Number(yItem) is not { } y)
        {
            return null;
        }

        var arguments = new List<string> { x, y };
        foreach (var member in item.Members)
        {
            switch (member.Key)
            {
                case "x":
                case "y":
                    continue;
                case "elasticity" or "maxStretch" when Number(member.Value) is { } number:
                    arguments.Add($"{member.Key}: {number}");
                    continue;
                case "seriesCountAxis" when member.Value.IsString:
                    arguments.Add($"seriesCountAxis: {Constant("SeriesCountAxis", member.Value.Value!, NameTables.SeriesCountAxis(member.Value.Value!))}");
                    continue;
                default:
                    return null;
            }
        }

        return $"{Flint("ContinuousMarkCrossSection")}.Axes({string.Join(", ", arguments)})";
    }

    /// <summary><c>new ChartEncoding { … }</c>: field, type, aggregate, sortOrder, sortBy and scheme typed, the rest in
    /// <c>Extensions</c>.</summary>
    public string Encoding(JsonItem item)
    {
        var assignments = new List<string>();
        var extensions = new List<JsonMember>();
        foreach (var member in item.Members)
        {
            var value = member.Value.IsString ? member.Value.Value! : null;
            var assignment = (member.Key, value) switch
            {
                ("field", { } v) => $"Field = {CSharpText.StringLiteral(v)}",
                ("type", { } v) => $"Type = {Constant("EncodingType", v, NameTables.EncodingType(v))}",
                ("aggregate", { } v) => $"Aggregate = {Constant("Aggregate", v, NameTables.Aggregate(v))}",
                ("sortOrder", { } v) => $"SortOrder = {Constant("SortOrder", v, NameTables.SortOrder(v))}",
                ("sortBy", { } v) => $"SortBy = {CSharpText.StringLiteral(v)}",
                ("scheme", { } v) => $"Scheme = {CSharpText.StringLiteral(v)}",
                _ => null,
            };
            if (assignment is null)
            {
                extensions.Add(member);
            }
            else
            {
                assignments.Add(assignment);
            }
        }

        if (extensions.Count > 0)
        {
            assignments.Add($"Extensions = {Extensions(extensions)}");
        }

        var type = Flint("ChartEncoding");
        return assignments.Count == 0 ? $"new {type}()" : $"new {type} {{ {string.Join(", ", assignments)} }}";
    }

    /// <summary>The field of a plain field binding: a string, or an object whose only member is a string <c>field</c>.</summary>
    public static string? PlainField(JsonItem item) =>
        item.IsString ? item.Value
        : item.IsObject && item.Members.Count == 1 && item.Get("field") is { IsString: true } field ? field.Value
        : null;

    /// <summary><c>SemanticType.X</c> or <c>new SemanticType("…")</c>.</summary>
    public string SemanticType(string name) => Constant("SemanticType", name, NameTables.SemanticType(name)?.Member);

    /// <summary>The semantic type of an annotation that writes as its string form: a string, or an object whose only
    /// member is a string <c>semanticType</c> (the canonical form, design §5.2).</summary>
    public static string? TrivialAnnotation(JsonItem item) =>
        item.IsString ? item.Value
        : item.IsObject && item.Members.Count == 1 && item.Get("semanticType") is { IsString: true } type ? type.Value
        : null;

    /// <summary>An annotation: <c>SemanticType.X</c> for the string form, otherwise <c>new SemanticAnnotation { … }</c>
    /// with unknown keys and nulls in <c>Extensions</c>; null when the value is neither (the reader of the library refuses
    /// it, so it does not happen after the double read).</summary>
    public string? Annotation(JsonItem item)
    {
        if (TrivialAnnotation(item) is { } trivial)
        {
            return SemanticType(trivial);
        }

        if (!item.IsObject || item.Get("semanticType") is not { IsString: true } type)
        {
            return null;
        }

        var assignments = new List<string> { $"Type = {SemanticType(type.Value!)}" };
        var extensions = new List<JsonMember>();
        foreach (var member in item.Members)
        {
            var assignment = member.Key switch
            {
                "semanticType" => string.Empty,
                "intrinsicDomain" => Typed(member.Value, "double[]") is { } domain ? $"IntrinsicDomain = {domain}" : null,
                "unit" => Typed(member.Value, "string") is { } unit ? $"Unit = {unit}" : null,
                "divergingMidpoint" => Number(member.Value) is { } midpoint ? $"DivergingMidpoint = {midpoint}" : null,
                "sortOrder" => Typed(member.Value, "string[]") is { } order ? $"SortOrder = {order}" : null,
                _ => null,
            };
            if (assignment is null)
            {
                extensions.Add(member);
            }
            else if (assignment.Length > 0)
            {
                assignments.Add(assignment);
            }
        }

        if (extensions.Count > 0)
        {
            assignments.Add($"Extensions = {Extensions(extensions)}");
        }

        return $"new {Flint("SemanticAnnotation")} {{ {string.Join(", ", assignments)} }}";
    }

    private static bool IsReference(string type) =>
        type is "string" || type.EndsWith("[]", StringComparison.Ordinal) || type.StartsWith("Dictionary<", StringComparison.Ordinal) || NameTables.Class(type) is not null;

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
