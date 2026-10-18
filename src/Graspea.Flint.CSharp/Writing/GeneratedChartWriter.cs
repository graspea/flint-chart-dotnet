using System.Text;

namespace Graspea.Flint.CSharp;

/// <summary>
/// Where the generator writes a chart (design §12.6.2, §12.6.3): the namespace (null for the global one), the containing
/// type declarations as the user wrote them without accessibility (<c>partial class Outer&lt;T&gt;</c>), the class name,
/// the accessibility of the generated members, the project-relative spec path and the generator version for the header.
/// </summary>
internal sealed record GeneratedTarget(string? Namespace, IReadOnlyList<string> ContainingTypes, string ClassName, string Accessibility, string SpecPath, string Version);

/// <summary>
/// Mode B (design §12.6.5): the user's row type, <c>global::</c>-qualified; the spec field each member binds, to the member
/// name as written in C#; the <c>JsonSerializerContext</c> that holds the row metadata, if any; and whether the reflection
/// row path writes member names verbatim (<c>FlintRowNaming.Verbatim</c>) instead of camelCase.
/// </summary>
internal sealed record BoundRowType(string TypeName, string DisplayName, IReadOnlyDictionary<string, string> Members, string? JsonContext, string? JsonContextDisplayName, bool Verbatim);

/// <summary>
/// The source the generator adds for one <c>[FlintChart]</c> class (design §12.6.3): the fluent chain of the parameter data
/// mode ending in <c>ToTemplate()</c>, and <c>Template</c>, <c>Build</c>, <c>BuildAsync</c>, <c>WriteAsync</c> and
/// <c>Validate</c>, plus the <c>Row</c> record in mode A. The file has no using directive: every type is
/// <c>global::</c>-qualified, the members the generator adds for itself have reserved names, and every generated member
/// carries the experimental marker <c>FLINTEXP001</c> while the generator's contract is in preview.
/// </summary>
internal static class GeneratedChartWriter
{
    /// <summary>The id of the experimental marker (design D7.11).</summary>
    internal const string ExperimentalId = "FLINTEXP001";

    /// <summary>The names the generated part declares; a class that declares one of them is FLINT104.</summary>
    internal static readonly string[] MemberNames = ["Row", "Template", "Build", "BuildAsync", "WriteAsync", "Validate", "__flint_template", "__flint_Json"];

    private const string Experimental = "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"" + ExperimentalId + "\")]";

    /// <summary>Mode A: the rows are of the <c>Row</c> record inferred from the spec (design §12.6.6).</summary>
    public static string Write(SpecDocument spec, GeneratedTarget target, RowPlan plan) =>
        Write(spec, target, "Row", plan.Fields.ToDictionary(f => f.Wire, f => f.Member, StringComparer.Ordinal), new DeferredEntry("Row"), reflection: true, RowRecord(plan, target));

    /// <summary>Mode B: the rows are of the user's type, written through the context's metadata or by reflection.</summary>
    public static string Write(SpecDocument spec, GeneratedTarget target, BoundRowType row)
    {
        var entry = row.JsonContext is { } context
            ? new DeferredEntry(row.TypeName, TypeInfo(row, context), TypeInfo: true)
            : new DeferredEntry(row.TypeName, row.Verbatim ? "new global::Graspea.Flint.RowOptions { PropertyNamingPolicy = null }" : null);
        return Write(spec, target, row.TypeName, row.Members, entry, reflection: row.JsonContext is null, row: null);
    }

    /// <summary>
    /// The members without a spec (FLINT101 to FLINT103): the same signatures, each throwing, so that user code compiles and
    /// the build fails on the one diagnostic. Mode A gets an empty <c>Row</c>.
    /// </summary>
    public static string WriteUnavailable(GeneratedTarget target, string? rowType, string reason)
    {
        var message = CSharpText.StringLiteral(reason);
        var members = new List<string>();
        if (rowType is null)
        {
            members.Add($"    /// <summary>A row of the chart; the spec could not be read.</summary>\n    {Experimental}\n    {target.Accessibility} sealed record Row();\n");
        }

        var type = rowType ?? "Row";
        var body = $"throw new global::System.InvalidOperationException({message})";
        members.Add($"    {Experimental}\n    {target.Accessibility} static global::Graspea.Flint.ChartTemplate<{type}> Template => {body};\n");
        members.AddRange(Operations(target, type, annotations: string.Empty, body: _ => body));
        return File(target, members);
    }

    private static string Write(SpecDocument spec, GeneratedTarget target, string type, IReadOnlyDictionary<string, string> members, DeferredEntry entry, bool reflection, string? row)
    {
        var values = new ValueWriter(new SortedSet<string>(StringComparer.Ordinal), qualified: true);
        var body = new FluentWriter(spec, DataSource.Template(), members, values, entry).Write();
        var annotations = reflection
            ? $"[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute({CSharpText.StringLiteral(SpecConverter.ReflectionMessage)})]\n" +
              $"[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute({CSharpText.StringLiteral(SpecConverter.ReflectionMessage)})]\n"
            : string.Empty;

        var output = new List<string>();
        if (row is not null)
        {
            output.Add(row);
        }

        output.Add($"    {Experimental}\n    private static global::Graspea.Flint.ChartTemplate<{type}>? __flint_template;\n");
        output.Add(
            $"    /// <summary>The chart of {Xml(target.SpecPath)} without rows, built on first use; bind rows per call.</summary>\n" +
            $"    {Experimental}\n" +
            $"    {target.Accessibility} static global::Graspea.Flint.ChartTemplate<{type}> Template\n    {{\n" +
            Indent(annotations, 8) +
            $"        get => global::System.Threading.LazyInitializer.EnsureInitialized(ref __flint_template, static () =>\n{SpecConverter.Indent(body.Expression, 12)});\n    }}\n");
        output.AddRange(Operations(target, type, annotations, body: member => member switch
        {
            "Build" => "Template.Bind(rows)",
            "BuildAsync" => "Template.BindAsync(rows, cancellationToken)",
            "WriteAsync" => "global::Graspea.Flint.Serialization.FlintJson.SerializeAsync(utf8Json, Template.BindStream(rows), cancellationToken)",
            _ => "Template.Validate(backend)",
        }));
        if (values.UsesJsonHelper)
        {
            output.Add(
                "    private static global::System.Text.Json.JsonElement __flint_Json(string json)\n    {\n" +
                "        using var document = global::System.Text.Json.JsonDocument.Parse(json);\n" +
                "        return document.RootElement.Clone();\n    }\n");
        }

        return File(target, output);
    }

    /// <summary><c>Build</c>, <c>BuildAsync</c>, <c>WriteAsync</c> and <c>Validate</c>.</summary>
    private static IEnumerable<string> Operations(GeneratedTarget target, string type, string annotations, Func<string, string> body)
    {
        var head = $"    {Experimental}\n{Indent(annotations, 4)}    {target.Accessibility} static ";
        var rows = $"global::System.Collections.Generic.IEnumerable<{type}> rows";
        var asyncRows = $"global::System.Collections.Generic.IAsyncEnumerable<{type}> rows";
        const string token = "global::System.Threading.CancellationToken cancellationToken = default";
        yield return
            "    /// <summary>A new document with <paramref name=\"rows\"/> written to JSON now.</summary>\n" +
            "    /// <param name=\"rows\">The rows.</param>\n    /// <returns>The document.</returns>\n" +
            $"{head}global::Graspea.Flint.ChartAssemblyInput Build({rows}) => {body("Build")};\n";
        yield return
            "    /// <summary>A new document with the rows of an asynchronous source written to JSON.</summary>\n" +
            "    /// <param name=\"rows\">The rows; end a database query with <c>.AsAsyncEnumerable()</c>.</param>\n" +
            "    /// <param name=\"cancellationToken\">Passed to the source and checked before each row.</param>\n    /// <returns>The document.</returns>\n" +
            $"{head}global::System.Threading.Tasks.ValueTask<global::Graspea.Flint.ChartAssemblyInput> BuildAsync({asyncRows}, {token}) =>\n        {body("BuildAsync")};\n";
        yield return
            "    /// <summary>Writes the document to <paramref name=\"utf8Json\"/> with the rows streamed from their source.</summary>\n" +
            "    /// <param name=\"utf8Json\">The stream.</param>\n    /// <param name=\"rows\">The rows.</param>\n" +
            "    /// <param name=\"cancellationToken\">Passed to the source and to every write.</param>\n    /// <returns>The write.</returns>\n" +
            $"{head}global::System.Threading.Tasks.Task WriteAsync(global::System.IO.Stream utf8Json, {asyncRows}, {token}) =>\n        {body("WriteAsync")};\n";
        yield return
            "    /// <summary>Validates the spec and the row metadata without rows.</summary>\n" +
            "    /// <param name=\"backend\">The target backend; null means every backend that has the template.</param>\n    /// <returns>The issues.</returns>\n" +
            $"{head}global::Graspea.Flint.Validation.ValidationResult Validate(global::Graspea.Flint.FlintBackend? backend = null) => {body("Validate")};\n";
    }

    private static string RowRecord(RowPlan plan, GeneratedTarget target)
    {
        var header = $"    /// <summary>A row of the chart, inferred from {Xml(target.SpecPath)}.</summary>\n" +
            string.Concat(plan.Fields.Select(f => $"    /// <param name=\"{f.Member}\">The <c>{Xml(f.Wire)}</c> field.</param>\n")) +
            $"    {Experimental}\n    {target.Accessibility} sealed record Row(";
        if (plan.Fields.Count == 0)
        {
            return header + ");\n";
        }

        var parameters = plan.Fields.Select(f =>
            $"        [property: global::System.Text.Json.Serialization.JsonPropertyNameAttribute({CSharpText.StringLiteral(f.Wire)})] {Qualified(f)} {f.Member}");
        return $"{header}\n{string.Join(",\n", parameters)});\n";
    }

    private static string Qualified(RowField field) => field.Kind switch
    {
        RowFieldKind.DateOnly => "global::System.DateOnly" + (field.Nullable ? "?" : string.Empty),
        RowFieldKind.DateTimeOffset => "global::System.DateTimeOffset" + (field.Nullable ? "?" : string.Empty),
        RowFieldKind.Json => "global::System.Text.Json.Nodes.JsonNode?",
        _ => field.TypeText,
    };

    /// <summary>The metadata of the context for the row type, without reflection: <c>GetTypeInfo</c> does not depend on the
    /// property name System.Text.Json's generator chose, which this generator cannot see (design §12.6.3).</summary>
    private static string TypeInfo(BoundRowType row, string context)
    {
        var message = $"{row.JsonContextDisplayName} has no metadata for {row.DisplayName}; add [JsonSerializable(typeof({row.DisplayName}))] to it (FLINT106).";
        return $"(global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{row.TypeName}>?){context}.Default.GetTypeInfo(typeof({row.TypeName}))\n" +
            $"    ?? throw new global::System.InvalidOperationException({CSharpText.StringLiteral(message)})";
    }

    private static string File(GeneratedTarget target, IEnumerable<string> members)
    {
        var builder = new StringBuilder()
            .Append($"// <auto-generated/> Graspea.Flint.Generators {target.Version} from {target.SpecPath}\n")
            .Append("#nullable enable\n\n");
        if (target.Namespace is { } ns)
        {
            builder.Append($"namespace {ns};\n\n");
        }

        var declarations = target.ContainingTypes.Append($"static partial class {target.ClassName}").ToList();
        for (var i = 0; i < declarations.Count; i++)
        {
            var pad = new string(' ', 4 * i);
            builder.Append($"{pad}{declarations[i]}\n{pad}{{\n");
        }

        builder.Append(Indent(string.Join("\n", members), 4 * (declarations.Count - 1)));
        for (var i = declarations.Count - 1; i >= 0; i--)
        {
            builder.Append($"{new string(' ', 4 * i)}}}\n");
        }

        return builder.ToString();
    }

    /// <summary>Indents every non-empty line by <paramref name="spaces"/>, keeping the final line break.</summary>
    private static string Indent(string text, int spaces) =>
        spaces == 0 || text.Length == 0 ? text : string.Join("\n", text.Split('\n').Select(line => line.Length == 0 ? line : new string(' ', spaces) + line));

    private static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
