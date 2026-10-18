namespace Graspea.Flint.CSharp;

/// <summary>
/// The deferred entry of a template or a document without data: the row type argument and the argument, such as
/// <c>Chart.Line&lt;Row&gt;()</c>, <c>Chart.Line&lt;Sale&gt;(new RowOptions { … })</c> or, when
/// <paramref name="TypeInfo"/> is set, <c>Chart.Line(typeInfo)</c>, which needs no reflection.
/// </summary>
internal sealed record DeferredEntry(string RowType, string? Argument = null, bool TypeInfo = false);

/// <summary>
/// The fluent style (design §12.4.1): an entry of <c>Chart</c>, one call per line in input order (encodings, chart
/// properties, title, subtitle, sizes, the annotations not folded into a channel call, display names, theme, options),
/// and <c>Configure</c> calls last for what no typed call writes exactly. <paramref name="members"/> maps a field to the
/// row member a selector names (<c>r =&gt; r.Month</c>); a field without one is written by name.
/// </summary>
internal sealed class FluentWriter(SpecDocument spec, DataSource source, IReadOnlyDictionary<string, string>? members, ValueWriter values, DeferredEntry? deferred = null)
{
    private readonly List<string> _calls = [];
    private readonly List<string> _configure = [];
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);
    private TemplateNames? _template;

    public WrittenBody Write()
    {
        var chartSpec = spec.ChartSpec!;
        var chartType = spec.ChartType!;
        _template = NameTables.Template(chartType);
        var (entry, reflection) = Entry(chartType);

        // chart_spec first, so a channel call takes its field's annotation before the remaining annotations are written.
        ChartSpec(chartSpec);
        foreach (var key in new[] { "semantic_types", "field_display_names", "theme_spec", "options" })
        {
            if (spec.Root.Members.FirstOrDefault(m => m.Key == key) is not { } member)
            {
                continue;
            }

            switch (key)
            {
                case "semantic_types":
                    SemanticTypes(member);
                    break;
                case "field_display_names":
                    DisplayNames(member);
                    break;
                case "theme_spec":
                    Theme(member);
                    break;
                default:
                    Options(member);
                    break;
            }
        }

        foreach (var member in spec.Root.Members.Where(m => m.Key is not ("data" or "chart_spec" or "semantic_types" or "field_display_names" or "theme_spec" or "options")))
        {
            TopExtension(member);
        }

        if (source.AsExtension is { } data)
        {
            TopExtension(new JsonMember("data", default, data));
        }

        foreach (var member in source.Kind == DataKind.Template ? [] : source.Extensions)
        {
            _configure.Add($".Configure(d => (d.Data!.Extensions ??= new())[{CSharpText.StringLiteral(member.Key)}] = {values.Element(member.Value)})");
        }

        var statements = source.Kind == DataKind.Template && source.Extensions.Count > 0
            ? new[] { $"input.Data!.Extensions = {values.Extensions(source.Extensions)};" }
            : [];
        var terminal = source.Kind == DataKind.Template ? ".ToTemplate()" : ".Build()";
        var lines = _calls.Concat(_configure).Append(terminal).Select(c => "    " + c);
        return new WrittenBody(entry + "\n" + string.Join("\n", lines), statements, reflection);
    }

    private (string Entry, bool Reflection) Entry(string chartType)
    {
        var data = values.Flint("ChartData");
        var argument = source.Kind switch
        {
            DataKind.Rows => "Rows",
            DataKind.Raw => $"{data}.FromJson(Values())",
            DataKind.External => $"{data}.FromJson(values)",
            DataKind.Url => $"{data}.FromUrl({CSharpText.StringLiteral(source.Url!)})",
            _ => null,
        };
        var chart = values.Flint("Chart");
        var type = $"new {values.Flint("ChartType")}({CSharpText.StringLiteral(chartType)})";
        if (argument is null)
        {
            var entry = deferred ?? new DeferredEntry("Row");
            var typeArgument = entry.TypeInfo ? string.Empty : $"<{entry.RowType}>";
            var arguments = string.Join(", ", new[] { _template is null ? type : null, entry.Argument }.Where(a => a is not null));
            return ($"{chart}.{(_template is null ? "Of" : _template.Entry)}{typeArgument}({arguments})", !entry.TypeInfo);
        }

        var call = _template is null ? $"{chart}.Of({type}, {argument})" : $"{chart}.{_template.Entry}({argument})";
        return (call, source.Kind == DataKind.Rows);
    }

    private bool HasMember(string field) => members?.ContainsKey(field) == true;

    /// <summary>A selector over the row type when the field is a member of it, otherwise the field name.</summary>
    private string Field(string field) =>
        members is not null && members.TryGetValue(field, out var member) ? $"r => r.{member}" : CSharpText.StringLiteral(field);

    private string ChannelConstant(string channel) => values.Constant("Channel", channel, NameTables.Channel(channel));

    private void ChartSpec(JsonItem chartSpec)
    {
        string[] order = ["encodings", "chartProperties", "title", "subtitle", "baseSize", "canvasSize"];
        var members = order.Select(key => chartSpec.Members.FirstOrDefault(m => m.Key == key)).Where(m => m is not null)
            .Concat(chartSpec.Members.Where(m => m.Key != "chartType" && Array.IndexOf(order, m.Key) < 0));
        foreach (var member in members)
        {
            switch (member!.Key)
            {
                case "encodings":
                    Encodings(member.Value);
                    break;
                case "chartProperties":
                    Properties(member);
                    break;
                case "title" or "subtitle" when member.Value.IsString:
                    _calls.Add($".{(member.Key == "title" ? "Title" : "Subtitle")}({CSharpText.StringLiteral(member.Value.Value!)})");
                    break;
                case "baseSize" or "canvasSize" when Size(member.Value) is { } size:
                    _calls.Add($".{(member.Key == "baseSize" ? "BaseSize" : "CanvasSize")}({size})");
                    break;
                default:
                    _configure.Add($".Configure(d => (d.ChartSpec.Extensions ??= new())[{CSharpText.StringLiteral(member.Key)}] = {values.Element(member.Value)})");
                    break;
            }
        }
    }

    private static string? Size(JsonItem item) =>
        item.IsObject && item.Members.Count == 2 && item.Get("width") is { } width && item.Get("height") is { } height
        && ValueWriter.Number(width) is { } w && ValueWriter.Number(height) is { } h
            ? $"{w}, {h}"
            : null;

    private void Encodings(JsonItem encodings)
    {
        if (encodings.IsNull)
        {
            _configure.Add(".Configure(d => d.ChartSpec.Encodings = null!)");
            return;
        }

        foreach (var member in encodings.Members)
        {
            var channel = member.Key;
            var key = CSharpText.StringLiteral(channel);
            var value = member.Value;
            var method = _template?.Channels.FirstOrDefault(c => c.Wire == channel)?.Member;
            // The builder copies an encoding through JSON, which reads a member set to null back as absent; such a value
            // is assigned in Configure, which keeps it as given.
            if (value.IsNull || channel.Length == 0 || HoldsNull(value))
            {
                var expression = value.IsNull ? "null!" : EncodingValue(value);
                _configure.Add($".Configure(d => d.ChartSpec.Encodings[{key}] = {expression})");
                continue;
            }

            var target = method is null ? $".Encode({ChannelConstant(channel)}, " : $".{method}(";
            if (ValueWriter.PlainField(value) is { } field)
            {
                _calls.Add($"{target}{Field(field)}{Fold(field)})");
            }
            else if (value.IsObject)
            {
                _calls.Add($"{target}{values.Encoding(value)})");
            }
            else if (Series(channel, value) is { } series)
            {
                _calls.Add(series);
            }
            else
            {
                _calls.Add($".Encode({ChannelConstant(channel)}, {EncodingValue(value)})");
            }
        }
    }

    /// <summary>The semantic type argument of the first channel call that binds a field with a trivial annotation.</summary>
    private string Fold(string field)
    {
        if (AnnotationsTyped() && spec.SemanticTypes?.Get(field) is { } annotation && ValueWriter.TrivialAnnotation(annotation) is { } type && _folded.Add(field))
        {
            return $", {values.SemanticType(type)}";
        }

        return string.Empty;
    }

    /// <summary><c>.Series(Channel.Y, …)</c> for two or more distinct plain fields on x or y, which <c>Series</c> accepts.</summary>
    private string? Series(string channel, JsonItem value)
    {
        if (channel is not ("x" or "y") || value.Items.Count < 2)
        {
            return null;
        }

        var fields = value.Items.Select(ValueWriter.PlainField).ToList();
        if (fields.Any(f => string.IsNullOrEmpty(f)) || fields.Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            return null;
        }

        var arguments = fields.All(f => HasMember(f!)) ? fields.Select(f => Field(f!)) : fields.Select(f => CSharpText.StringLiteral(f!));
        return $".Series({ChannelConstant(channel)}, {string.Join(", ", arguments)})";
    }

    private string EncodingValue(JsonItem value) =>
        value.IsArray
            ? $"{values.Flint("EncodingValue")}.Series({string.Join(", ", value.Items.Select(e => e.IsString ? $"new {values.Flint("ChartEncoding")} {{ Field = {CSharpText.StringLiteral(e.Value!)} }}" : values.Encoding(e)))})"
            : ValueWriter.PlainField(value) is { } field ? CSharpText.StringLiteral(field) : values.Encoding(value);

    private void Properties(JsonMember member)
    {
        var properties = member.Value;
        if (!properties.IsObject)
        {
            _configure.Add($".Configure(d => (d.ChartSpec.Extensions ??= new())[\"chartProperties\"] = {values.Element(properties)})");
            return;
        }

        if (properties.Members.Count == 0)
        {
            _configure.Add(".Configure(d => d.ChartSpec.ChartProperties = new())");
            return;
        }

        foreach (var property in properties.Members)
        {
            var key = CSharpText.StringLiteral(property.Key);
            var value = property.Value;
            if (value.IsNull)
            {
                _configure.Add($".Configure(d => (d.ChartSpec.ChartProperties ??= new())[{key}] = null)");
                continue;
            }

            var declared = _template?.Properties.FirstOrDefault(p => p.Key == property.Key);
            if (declared is not null && Facade(declared, value) is { } argument)
            {
                _calls.Add($".{declared.Method}({argument})");
                continue;
            }

            var generic = value.Kind switch
            {
                JsonItemKind.String => CSharpText.StringLiteral(value.Value!),
                JsonItemKind.True => "true",
                JsonItemKind.False => "false",
                JsonItemKind.Number when ValueWriter.Number(value) is { } number => number,
                _ => values.Node(value),
            };
            _calls.Add($".Property({key}, {generic})");
        }
    }

    /// <summary>The argument of a template's property method when it writes exactly this value.</summary>
    private string? Facade(PropertyName property, JsonItem value) =>
        property.Kind switch
        {
            PropertyKind.Bool when value.IsBoolean => value.Kind == JsonItemKind.True ? "true" : "false",
            PropertyKind.Double => ValueWriter.Number(value),
            PropertyKind.Enum => property.Members.FirstOrDefault(m => Writes(m, value)) is { } member ? $"{values.Flint(property.EnumType!)}.{member.Member}" : null,
            PropertyKind.Json => values.Node(value),
            _ => null,
        };

    /// <summary>Whether the enum member writes this JSON value.</summary>
    internal static bool Writes(EnumMemberName member, JsonItem value) =>
        member.Kind switch
        {
            WireKind.String => value.IsString && value.Value == member.Text,
            WireKind.Boolean => value.IsBoolean && (value.Kind == JsonItemKind.True ? "true" : "false") == member.Text,
            _ => value.IsNumber && CSharpText.FitsDouble(value.Value!)
                && double.Parse(value.Value!, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
                    == double.Parse(member.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
        };

    /// <summary>Whether <c>semantic_types</c> is written through typed calls (otherwise it goes into <c>Extensions</c> whole).</summary>
    private bool AnnotationsTyped() =>
        spec.Root.Get("semantic_types") is { IsObject: true } types && types.Members.All(m => m.Value.IsNull || ValueWriter.TrivialAnnotation(m.Value) is not null || (m.Value.IsObject && m.Value.Get("semanticType") is { IsString: true }));

    private void SemanticTypes(JsonMember member)
    {
        var types = member.Value;
        if (!AnnotationsTyped())
        {
            TopExtension(member);
            return;
        }

        if (types.Members.Count == 0)
        {
            _configure.Add(".Configure(d => d.SemanticTypes = new())");
            return;
        }

        foreach (var annotation in types.Members)
        {
            if (annotation.Value.IsNull)
            {
                _configure.Add($".Configure(d => (d.SemanticTypes ??= new())[{CSharpText.StringLiteral(annotation.Key)}] = null!)");
            }
            else if (HoldsNull(annotation.Value))
            {
                _configure.Add($".Configure(d => (d.SemanticTypes ??= new())[{CSharpText.StringLiteral(annotation.Key)}] = {values.Annotation(annotation.Value)})");
            }
            else if (!_folded.Contains(annotation.Key))
            {
                _calls.Add($".Annotate({Field(annotation.Key)}, {values.Annotation(annotation.Value)})");
            }
        }
    }

    private void DisplayNames(JsonMember member)
    {
        var names = member.Value;
        if (!names.IsObject || names.Members.Any(m => !m.Value.IsString && !m.Value.IsNull))
        {
            TopExtension(member);
            return;
        }

        if (names.Members.Count == 0)
        {
            _configure.Add(".Configure(d => d.FieldDisplayNames = new())");
            return;
        }

        foreach (var name in names.Members)
        {
            if (name.Value.IsNull)
            {
                _configure.Add($".Configure(d => (d.FieldDisplayNames ??= new())[{CSharpText.StringLiteral(name.Key)}] = null!)");
            }
            else
            {
                _calls.Add($".DisplayName({Field(name.Key)}, {CSharpText.StringLiteral(name.Value.Value!)})");
            }
        }
    }

    private void Theme(JsonMember member)
    {
        var theme = member.Value;
        if (theme.IsString)
        {
            _calls.Add($".Theme({values.Constant("ThemePreset", theme.Value!, NameTables.ThemePreset(theme.Value!))})");
        }
        else if (theme.IsObject && NameTables.Class("Graspea.Flint.Theming.ThemeSpec") is { } names)
        {
            var expression = values.Object(names, theme);
            if (HoldsNull(theme))
            {
                _configure.Add($".Configure(d => d.ThemeSpec = {expression})");
            }
            else
            {
                _calls.Add($".Theme({expression})");
            }
        }
        else
        {
            TopExtension(member);
        }
    }

    private void Options(JsonMember member)
    {
        var options = member.Value;
        if (!options.IsObject || NameTables.Class("Graspea.Flint.AssembleOptions") is not { } names)
        {
            TopExtension(member);
            return;
        }

        var (assignments, extensions) = values.Members(names, options);
        var statements = assignments.Select(a => $"o.{a};").ToList();
        if (extensions.Count > 0)
        {
            statements.Add($"o.Extensions = {values.Extensions(extensions)};");
        }

        _calls.Add(statements.Count == 0 ? ".Options(o => { })" : $".Options(o => {{ {string.Join(" ", statements)} }})");
    }

    /// <summary>Whether a value holds a null anywhere: the builder's JSON copy would drop a known member set to null.</summary>
    private static bool HoldsNull(JsonItem item) =>
        item.IsNull || item.Members.Any(m => HoldsNull(m.Value)) || item.Items.Any(HoldsNull);

    private void TopExtension(JsonMember member) =>
        _configure.Add($".Configure(d => (d.Extensions ??= new())[{CSharpText.StringLiteral(member.Key)}] = {values.Element(member.Value)})");
}
