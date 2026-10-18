using System.Text;

namespace Graspea.Flint.CSharp;

/// <summary>The C# style of the output (design §12.4).</summary>
internal enum CodeStyle
{
    /// <summary>A fluent chain over the generated builders (§12.4.1).</summary>
    Fluent,

    /// <summary>One object initialiser of <c>ChartAssemblyInput</c> (§12.4.2).</summary>
    Model,
}

/// <summary>Where the rows of <c>data.values</c> go (design §12.5.2).</summary>
internal enum DataMode
{
    /// <summary>Embedded in the class: a replay of the document.</summary>
    Inline,

    /// <summary>Read by the caller from a sidecar file and passed to <c>Build(JsonArray)</c>.</summary>
    External,

    /// <summary>Supplied later: a template and <c>Build(IEnumerable&lt;Row&gt;)</c>, <c>BuildAsync</c>, <c>WriteAsync</c>.</summary>
    Parameter,
}

/// <summary>What to write.</summary>
internal sealed class ConversionOptions(string className)
{
    /// <summary>The class name, a C# identifier.</summary>
    public string ClassName { get; } = className;

    public CodeStyle Style { get; set; } = CodeStyle.Fluent;

    public DataMode Data { get; set; } = DataMode.Inline;

    /// <summary>A dotted C# identifier for a file-scoped namespace; null for the global namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>The input's file name, for the header line (no directories, so the output has no absolute path).</summary>
    public string SourceName { get; set; } = "-";

    /// <summary>The tool version, which is also the <c>Graspea.Flint</c> version the output targets.</summary>
    public string Version { get; set; } = "0.0.0";
}

/// <summary>The C# text, the sidecar JSON of <see cref="DataMode.External"/>, and notes for the user.</summary>
internal sealed class ConversionResult(string code, string? sidecar, IReadOnlyList<string> notes)
{
    public string Code { get; } = code;

    /// <summary>The text of <c>data.values</c>, exactly as in the input; null unless the mode is external and the document has
    /// values.</summary>
    public string? Sidecar { get; } = sidecar;

    public IReadOnlyList<string> Notes { get; } = notes;
}

/// <summary>
/// Converts a <c>ChartAssemblyInput</c> JSON document into C# that builds the same document with <c>Graspea.Flint</c>
/// (design §12.3): the reader, the row inference and the fluent or model writer.
/// </summary>
internal static class SpecConverter
{
    /// <summary>The library's message for the reflection row path (<c>RowMaterializer.ReflectionMessage</c>).</summary>
    internal const string ReflectionMessage = "Row types are serialised by reflection; use the JsonTypeInfo overload for trimmed or AOT hosts.";

    /// <summary>Above this many inline rows the result suggests <c>--data external</c> (design §12.5.2).</summary>
    internal const int InlineRowNotice = 1000;

    /// <summary>The member names the class itself declares; <c>--class</c> must differ from them.</summary>
    internal static readonly string[] MemberNames = ["Row", "Rows", "Values", "Template", "Build", "BuildAsync", "WriteAsync", "Validate", "Json", "WithData"];

    /// <exception cref="JsonReadException">The text is not valid JSON.</exception>
    public static ConversionResult Convert(string json, ConversionOptions options) => Convert(JsonReader.Parse(json), json, options);

    public static ConversionResult Convert(JsonItem root, string json, ConversionOptions options)
    {
        var spec = new SpecDocument(root);
        var source = DataSource.Of(spec, options.Data);
        var usings = new SortedSet<string>(StringComparer.Ordinal) { "Graspea.Flint" };
        var values = new ValueWriter(usings);
        var notes = new List<string>();

        // A Row type: for replayed rows, for rows supplied later, and for the fluent deferred entry (Chart.Line<Row>()).
        var needsRow = source.Kind is DataKind.Rows or DataKind.Template || (source.Kind == DataKind.Deferred && options.Style == CodeStyle.Fluent);
        var plan = needsRow ? RowInference.Infer(spec, replay: source.Kind == DataKind.Rows) : null;
        if (source.Kind == DataKind.Rows && plan is { Typed: false })
        {
            source = source.AsRaw();
            plan = null;
        }

        if (source.Kind is DataKind.Rows or DataKind.Raw && source.Values!.Items.Count > InlineRowNotice)
        {
            notes.Add($"{source.Values.Items.Count} rows are written inline; --data external keeps them in a sidecar file.");
        }

        foreach (var field in plan?.UnhintedFields ?? [])
        {
            notes.Add($"The field \"{field}\" has no values and no type hint; its Row member is a string.");
        }

        var body = options.Style == CodeStyle.Fluent
            ? new FluentWriter(spec, source, plan?.Fields.ToDictionary(f => f.Wire, f => f.Member, StringComparer.Ordinal), values).Write()
            : new ModelWriter(spec, source, values).Write();

        var members = new List<string>();
        if (plan is not null)
        {
            members.Add(RowRecord(plan, values));
        }

        if (source.Kind == DataKind.Rows)
        {
            members.Add(RowsArray(source.Values!, plan!, values));
        }
        else if (source.Kind == DataKind.Raw)
        {
            values.Using("System.Text.Json.Nodes");
            members.Add($"    public static JsonArray Values() => JsonNode.Parse({ValueWriter.Fragment(source.Values!)})!.AsArray();\n");
        }

        members.AddRange(BuildMembers(source, body, values));
        if (values.UsesJsonHelper)
        {
            members.Add(
                "    private static JsonElement Json(string json)\n    {\n        using var document = JsonDocument.Parse(json);\n" +
                "        return document.RootElement.Clone();\n    }\n");
        }

        var code = new StringBuilder()
            .Append($"// Generated by flint-cs {options.Version} from {options.SourceName}; requires Graspea.Flint {options.Version}.\n")
            .Append("#nullable enable\n");
        foreach (var ns in usings.Where(IsSystem).Concat(usings.Where(u => !IsSystem(u))))
        {
            code.Append($"using {ns};\n");
        }

        code.Append('\n');
        if (options.Namespace is { } name)
        {
            code.Append($"namespace {name};\n\n");
        }

        code.Append($"public static class {options.ClassName}\n{{\n").Append(string.Join("\n", members)).Append("}\n");
        var sidecar = source.Kind == DataKind.External ? json.Substring(source.Values!.Start.Offset, source.Values.End - source.Values.Start.Offset) : null;
        return new ConversionResult(code.ToString(), sidecar, notes);
    }

    private static bool IsSystem(string ns) => ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);

    private static string RowRecord(RowPlan plan, ValueWriter values)
    {
        if (plan.Fields.Count == 0)
        {
            return "    public sealed record Row();\n";
        }

        values.Using("System.Text.Json.Serialization");
        foreach (var field in plan.Fields)
        {
            values.Using(field.Kind switch
            {
                RowFieldKind.DateOnly or RowFieldKind.DateTimeOffset => "System",
                RowFieldKind.Json => "System.Text.Json.Nodes",
                _ => "Graspea.Flint",
            });
        }

        var parameters = plan.Fields.Select(f => $"        [property: JsonPropertyName({CSharpText.StringLiteral(f.Wire)})] {f.TypeText} {f.Member}");
        return $"    public sealed record Row(\n{string.Join(",\n", parameters)});\n";
    }

    private static string RowsArray(JsonItem rows, RowPlan plan, ValueWriter values)
    {
        if (rows.Items.Count == 0)
        {
            return "    public static readonly Row[] Rows = [];\n";
        }

        var builder = new StringBuilder("    public static readonly Row[] Rows =\n    [\n");
        foreach (var row in rows.Items)
        {
            var arguments = plan.Fields.Select(f => RowValue(row.Get(f.Wire)!, f, values));
            builder.Append($"        new({string.Join(", ", arguments)}),\n");
        }

        return builder.Append("    ];\n").ToString();
    }

    private static string RowValue(JsonItem value, RowField field, ValueWriter values)
    {
        if (value.IsNull)
        {
            return "null";
        }

        return field.Kind switch
        {
            RowFieldKind.Bool => value.Kind == JsonItemKind.True ? "true" : "false",
            RowFieldKind.Long or RowFieldKind.Int => CSharpText.LongLiteral(value.Value!),
            RowFieldKind.Double => CSharpText.DoubleLiteral(value.Value!),
            RowFieldKind.Decimal => CSharpText.DecimalLiteral(value.Value!),
            RowFieldKind.DateOnly => $"new DateOnly({int.Parse(value.Value!.Substring(0, 4), System.Globalization.CultureInfo.InvariantCulture)}, " +
                $"{int.Parse(value.Value.Substring(5, 2), System.Globalization.CultureInfo.InvariantCulture)}, " +
                $"{int.Parse(value.Value.Substring(8, 2), System.Globalization.CultureInfo.InvariantCulture)})",
            RowFieldKind.String => CSharpText.StringLiteral(value.Value!),
            _ => values.Node(value),
        };
    }

    private static IEnumerable<string> BuildMembers(DataSource source, WrittenBody body, ValueWriter values)
    {
        var annotations = $"    [RequiresUnreferencedCode({CSharpText.StringLiteral(ReflectionMessage)})]\n    [RequiresDynamicCode({CSharpText.StringLiteral(ReflectionMessage)})]\n";
        var reflection = source.Kind is DataKind.Rows or DataKind.Template || (source.Kind == DataKind.Deferred && body.Reflection);
        if (reflection)
        {
            values.Using("System.Diagnostics.CodeAnalysis");
        }

        if (source.Kind != DataKind.Template)
        {
            var parameter = source.Kind == DataKind.External ? "JsonArray values" : string.Empty;
            if (source.Kind == DataKind.External)
            {
                values.Using("System.Text.Json.Nodes");
            }

            var header = $"{(reflection ? annotations : string.Empty)}    public static ChartAssemblyInput Build({parameter})";
            yield return body.Statements.Count == 0
                ? $"{header} =>\n{Indent(body.Expression, 8)};\n"
                : $"{header}\n    {{\n        var input =\n{Indent(body.Expression, 12)};\n{string.Concat(body.Statements.Select(s => $"        {s}\n"))}        return input;\n    }}\n";
            yield break;
        }

        values.Using("System.Threading");
        values.Using("System.Threading.Tasks");
        values.Using("System.Collections.Generic");
        values.Using("System.IO");
        values.Using("Graspea.Flint.Serialization");
        values.Using("Graspea.Flint.Validation");
        var getterAnnotations = annotations.Replace("    [", "        [");
        yield return "    private static ChartTemplate<Row>? template;\n";
        yield return
            "    public static ChartTemplate<Row> Template\n    {\n" + getterAnnotations +
            $"        get => LazyInitializer.EnsureInitialized(ref template, static () =>\n{Indent(body.Expression, 12)});\n    }}\n";

        var withData = body.Statements.Count > 0;
        string Wrap(string call) => withData ? $"WithData({call})" : call;
        yield return $"{annotations}    public static ChartAssemblyInput Build(IEnumerable<Row> rows) => {Wrap("Template.Bind(rows)")};\n";
        yield return withData
            ? $"{annotations}    public static async ValueTask<ChartAssemblyInput> BuildAsync(IAsyncEnumerable<Row> rows, CancellationToken cancellationToken = default) =>\n" +
              "        WithData(await Template.BindAsync(rows, cancellationToken).ConfigureAwait(false));\n"
            : $"{annotations}    public static ValueTask<ChartAssemblyInput> BuildAsync(IAsyncEnumerable<Row> rows, CancellationToken cancellationToken = default) =>\n" +
              "        Template.BindAsync(rows, cancellationToken);\n";
        yield return $"{annotations}    public static Task WriteAsync(Stream utf8Json, IAsyncEnumerable<Row> rows, CancellationToken cancellationToken = default) =>\n" +
            $"        FlintJson.SerializeAsync(utf8Json, {Wrap("Template.BindStream(rows)")}, cancellationToken);\n";
        yield return $"{annotations}    public static ValidationResult Validate(FlintBackend? backend = null) => Template.Validate(backend);\n";
        if (withData)
        {
            yield return "    private static ChartAssemblyInput WithData(ChartAssemblyInput input)\n    {\n" +
                string.Concat(body.Statements.Select(s => $"        {s}\n")) + "        return input;\n    }\n";
        }
    }

    /// <summary>Indents every line of a multi-line expression by <paramref name="spaces"/>.</summary>
    internal static string Indent(string text, int spaces)
    {
        var pad = new string(' ', spaces);
        return string.Join("\n", text.Split('\n').Select(line => line.Length == 0 ? line : pad + line));
    }
}

/// <summary>What a writer produced: the expression (a chain or an initialiser, unindented), statements that complete the
/// document (<c>input.Data!.Extensions = …</c>), and whether the expression uses the reflection row path.</summary>
internal sealed class WrittenBody(string expression, IReadOnlyList<string> statements, bool reflection)
{
    public string Expression { get; } = expression;

    public IReadOnlyList<string> Statements { get; } = statements;

    public bool Reflection { get; } = reflection;
}

/// <summary>Where the document's rows come from in the emitted code.</summary>
internal enum DataKind
{
    /// <summary>Typed rows replayed from the spec: <c>Rows</c>.</summary>
    Rows,

    /// <summary>Rows that are not uniform objects, replayed as raw JSON: <c>Values()</c>.</summary>
    Raw,

    /// <summary>The caller's <c>JsonArray</c>: <c>Build(JsonArray values)</c>.</summary>
    External,

    /// <summary><c>ChartData.FromUrl</c>.</summary>
    Url,

    /// <summary>No data: the deferred entry (<c>Chart.Line&lt;Row&gt;()</c>) or a document without <c>data</c>.</summary>
    Deferred,

    /// <summary>Rows supplied later through a template.</summary>
    Template,
}

/// <summary>How the document's <c>data</c> member is written.</summary>
internal sealed class DataSource
{
    private DataSource(DataKind kind, JsonItem? values, string? url, IReadOnlyList<JsonMember> extensions, JsonItem? asExtension)
    {
        Kind = kind;
        Values = values;
        Url = url;
        Extensions = extensions;
        AsExtension = asExtension;
    }

    public DataKind Kind { get; }

    /// <summary><c>data.values</c>, for the row kinds.</summary>
    public JsonItem? Values { get; }

    public string? Url { get; }

    /// <summary>Members of <c>data</c> the typed <c>ChartData</c> does not hold (<c>url</c> beside <c>values</c>, unknown
    /// keys); they go into <c>Data.Extensions</c>.</summary>
    public IReadOnlyList<JsonMember> Extensions { get; }

    /// <summary>A <c>data</c> value that no <c>ChartData</c> writes (null, or an object with neither values nor a URL); it
    /// goes into the document's <c>Extensions</c> as it is.</summary>
    public JsonItem? AsExtension { get; }

    public static DataSource Of(SpecDocument spec, DataMode mode)
    {
        var data = spec.Data;
        var parameter = mode == DataMode.Parameter;
        var deferred = parameter ? DataKind.Template : DataKind.Deferred;
        if (data is null)
        {
            return new(deferred, null, null, [], null);
        }

        if (data.IsObject && data.Get("values") is { IsArray: true } values)
        {
            var extensions = data.Members.Where(m => m.Key != "values").ToList();
            var kind = mode switch
            {
                DataMode.External => DataKind.External,
                DataMode.Parameter => DataKind.Template,
                _ => DataKind.Rows,
            };
            return new(kind, values, null, extensions, null);
        }

        if (data.IsObject && data.Get("url") is { IsString: true } url)
        {
            return new(DataKind.Url, null, url.Value, data.Members.Where(m => m.Key != "url").ToList(), null);
        }

        return new(deferred, null, null, [], parameter ? null : data);
    }

    public DataSource AsRaw() => new(DataKind.Raw, Values, Url, Extensions, AsExtension);

    /// <summary>Rows supplied later through a template, with nothing of the spec's <c>data</c> written: the generator never
    /// embeds data (design §12.6.3).</summary>
    public static DataSource Template() => new(DataKind.Template, null, null, [], null);
}
