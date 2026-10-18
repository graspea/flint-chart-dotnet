namespace Graspea.Flint.CSharp;

/// <summary>
/// The model style (design §12.4.2): one object initialiser of <c>ChartAssemblyInput</c>. Every member that the typed
/// model cannot write exactly goes into the owning object's <c>Extensions</c> under its wire name.
/// </summary>
internal sealed class ModelWriter(SpecDocument spec, DataSource source, ValueWriter values)
{
    private static readonly string[] Known = ["data", "semantic_types", "chart_spec", "theme_spec", "options", "field_display_names"];

    public WrittenBody Write()
    {
        var members = new List<string>();
        var extensions = new List<JsonMember>();
        switch (source.Kind)
        {
            case DataKind.Rows:
                members.Add($"Data = {values.Flint("ChartData")}.FromRows(Rows)");
                break;
            case DataKind.Raw:
                members.Add($"Data = {values.Flint("ChartData")}.FromJson(Values())");
                break;
            case DataKind.External:
                members.Add($"Data = {values.Flint("ChartData")}.FromJson(values)");
                break;
            case DataKind.Url:
                members.Add($"Data = {values.Flint("ChartData")}.FromUrl({CSharpText.StringLiteral(source.Url!)})");
                break;
        }

        if (spec.Root.Get("semantic_types") is { } semanticTypes)
        {
            if (SemanticTypes(semanticTypes) is { } typed)
            {
                members.Add($"SemanticTypes = {typed}");
            }
            else
            {
                extensions.Add(Member("semantic_types"));
            }
        }

        members.Add($"ChartSpec = {ChartSpec(spec.ChartSpec!)}");

        if (spec.Root.Get("theme_spec") is { } theme)
        {
            if (theme.IsString)
            {
                members.Add($"ThemeSpec = {values.Constant("ThemePreset", theme.Value!, NameTables.ThemePreset(theme.Value!))}");
            }
            else if (theme.IsObject && NameTables.Class("Graspea.Flint.Theming.ThemeSpec") is { } names)
            {
                members.Add($"ThemeSpec = {values.Object(names, theme)}");
            }
            else
            {
                extensions.Add(Member("theme_spec"));
            }
        }

        if (spec.Root.Get("options") is { } options)
        {
            if (options.IsObject && NameTables.Class("Graspea.Flint.AssembleOptions") is { } names)
            {
                members.Add($"Options = {values.Object(names, options)}");
            }
            else
            {
                extensions.Add(Member("options"));
            }
        }

        if (spec.Root.Get("field_display_names") is { } displayNames)
        {
            if (displayNames.IsObject && displayNames.Members.All(m => m.Value.IsString || m.Value.IsNull))
            {
                members.Add($"FieldDisplayNames = {Map(displayNames.Members.Select(m => Entry(m.Key, m.Value.IsNull ? "null!" : CSharpText.StringLiteral(m.Value.Value!))))}");
            }
            else
            {
                extensions.Add(Member("field_display_names"));
            }
        }

        extensions.AddRange(spec.Root.Members.Where(m => Array.IndexOf(Known, m.Key) < 0));
        if (source.AsExtension is { } data)
        {
            extensions.Add(new JsonMember("data", default, data));
        }

        if (extensions.Count > 0)
        {
            members.Add($"Extensions = {Map(extensions.Select(m => Entry(m.Key, values.Element(m.Value))))}");
        }

        var input = Block($"new {values.Flint("ChartAssemblyInput")}", members);
        var statements = source.Extensions.Count > 0 ? new[] { $"input.Data!.Extensions = {values.Extensions(source.Extensions)};" } : [];
        var expression = source.Kind == DataKind.Template ? $"{values.Flint("ChartTemplate")}.Create<Row>({input})" : input;
        return new WrittenBody(expression, statements, source.Kind is DataKind.Rows or DataKind.Template);
    }

    private JsonMember Member(string key) => spec.Root.Members.First(m => m.Key == key);

    private string ChartSpec(JsonItem chartSpec)
    {
        var members = new List<string>();
        var extensions = new List<JsonMember>();
        foreach (var member in chartSpec.Members)
        {
            var value = member.Value;
            string? assignment = member.Key switch
            {
                "chartType" => $"ChartType = {values.Constant("ChartType", value.Value!, NameTables.Template(value.Value!)?.Constant)}",
                "title" when value.IsString => $"Title = {CSharpText.StringLiteral(value.Value!)}",
                "subtitle" when value.IsString => $"Subtitle = {CSharpText.StringLiteral(value.Value!)}",
                "encodings" when value.IsNull => "Encodings = null!",
                "encodings" when value.IsObject => value.Members.Count == 0
                    ? string.Empty
                    : $"Encodings =\n{Block(string.Empty, value.Members.Select(m => Entry(m.Key, Encoding(m.Value))))}",
                "baseSize" when values.Typed(value, "Graspea.Flint.ChartSize") is { } size => $"BaseSize = {size}",
                "canvasSize" when values.Typed(value, "Graspea.Flint.ChartSize") is { } size => $"CanvasSize = {size}",
                "chartProperties" when value.IsObject => $"ChartProperties = {Map(value.Members.Select(m => Entry(m.Key, values.Node(m.Value))))}",
                _ => null,
            };
            if (assignment is null)
            {
                extensions.Add(member);
            }
            else if (assignment.Length > 0)
            {
                members.Add(assignment);
            }
        }

        if (extensions.Count > 0)
        {
            members.Add($"Extensions = {Map(extensions.Select(m => Entry(m.Key, values.Element(m.Value))))}");
        }

        return Block($"new {values.Flint("ChartSpec")}", members);
    }

    private string Encoding(JsonItem value)
    {
        if (value.IsNull)
        {
            return "null!";
        }

        if (ValueWriter.PlainField(value) is { } field)
        {
            return CSharpText.StringLiteral(field);
        }

        if (value.IsArray)
        {
            var entries = value.Items.Select(e => e.IsString ? $"new {values.Flint("ChartEncoding")} {{ Field = {CSharpText.StringLiteral(e.Value!)} }}" : values.Encoding(e));
            return $"{values.Flint("EncodingValue")}.Series({string.Join(", ", entries)})";
        }

        return values.Encoding(value);
    }

    private string? SemanticTypes(JsonItem types)
    {
        if (!types.IsObject)
        {
            return null;
        }

        var entries = new List<string>();
        foreach (var member in types.Members)
        {
            var annotation = member.Value.IsNull ? "null!" : values.Annotation(member.Value);
            if (annotation is null)
            {
                return null;
            }

            entries.Add(Entry(member.Key, annotation));
        }

        return Map(entries);
    }

    private static string Entry(string key, string value) => $"[{CSharpText.StringLiteral(key)}] = {value}";

    /// <summary><c>new()</c> with one entry per line, or <c>new()</c> alone.</summary>
    private static string Map(IEnumerable<string> entries) => Block("new()", entries.ToList());

    /// <summary>An initialiser with one member per line, indented by four spaces.</summary>
    private static string Block(string head, IEnumerable<string> members)
    {
        var list = members.ToList();
        if (list.Count == 0)
        {
            return head == "new()" ? head : head + "()";
        }

        var body = $"{{\n{string.Join("\n", list.Select(m => SpecConverter.Indent(m, 4) + ","))}\n}}";
        return head.Length == 0 ? body : $"{head}\n{body}";
    }
}
