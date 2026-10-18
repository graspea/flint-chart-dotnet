namespace Graspea.Flint.CSharp;

/// <summary>What a row member's .NET type is, as far as the type checks of design §12.6.5 care.</summary>
internal enum RowMemberKind
{
    Integer,
    Real,
    String,
    Char,
    Bool,
    Enum,
    DateOnly,
    DateTime,
    DateTimeOffset,
    TimeOnly,
    TimeSpan,
    Guid,
    Uri,
    Collection,
    Object,

    /// <summary><c>object</c>, <c>JsonNode</c>, <c>JsonElement</c>, or a member or type with a <c>[JsonConverter]</c>: not
    /// checked (FLINT124).</summary>
    Opaque,
}

/// <summary>
/// A member System.Text.Json writes for the row type (design §12.6.5): its C# name, its <c>[JsonPropertyName]</c>, its type
/// for messages and checks, whether the generated class can name it in a selector, its conditional
/// <c>[JsonIgnore]</c> (<c>WhenWritingNull</c> or <c>WhenWritingDefault</c>), and whether it writes numbers as strings.
/// </summary>
internal sealed record RowMember(string Name, string? JsonName, string TypeDisplay, RowMemberKind Kind, bool Accessible, string? IgnoreCondition, bool WritesNumbersAsStrings);

/// <summary>A finding of the binding: the FLINT id, the severity, the message and the member it is about (an index into
/// the members), or null for the attribute's type argument.</summary>
internal sealed record RowBindingIssue(string Id, SpecSeverity Severity, string Message, int? Member);

/// <summary>
/// Mode B (design §12.6.5): binds every field of the spec to the one member of the user's row type that writes it, and
/// checks the member's type against what the spec says about the field. A field that no member writes, or that two do,
/// stays a field name in the chain.
/// </summary>
internal sealed class RowBinding
{
    private static readonly HashSet<string> Summing = new(StringComparer.Ordinal) { "sum", "average", "mean" };

    private RowBinding(IReadOnlyDictionary<string, string> members, IReadOnlyList<RowBindingIssue> issues)
    {
        Members = members;
        Issues = issues;
    }

    /// <summary>Each bound field and the member name a selector names, as written in C#.</summary>
    public IReadOnlyDictionary<string, string> Members { get; }

    public IReadOnlyList<RowBindingIssue> Issues { get; }

    /// <param name="spec">The spec.</param>
    /// <param name="members">The members System.Text.Json writes, in declaration order.</param>
    /// <param name="policy">The naming policy of the row metadata.</param>
    /// <param name="enumsAsNumbers">Whether enums are written as numbers (a context without <c>UseStringEnumConverter</c>).</param>
    /// <param name="rowName">The row type's name for messages.</param>
    /// <param name="specPath">The spec's path for messages.</param>
    public static RowBinding Bind(SpecDocument spec, IReadOnlyList<RowMember> members, RowNamingPolicy policy, bool enumsAsNumbers, string rowName, string specPath)
    {
        var issues = new List<RowBindingIssue>();
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        var wires = members.Select(m => m.JsonName ?? RowNaming.Convert(policy, m.Name)).ToList();

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member.IgnoreCondition is { } condition)
            {
                issues.Add(new("FLINT122", SpecSeverity.Error,
                    $"{rowName}.{member.Name} has [JsonIgnore(Condition = {condition})]: the row metadata may omit a null member, and ChartData.FromRows throws ArgumentException.", i));
            }

            if (member.WritesNumbersAsStrings)
            {
                issues.Add(new("FLINT122", SpecSeverity.Error,
                    $"{rowName}.{member.Name} writes numbers as strings (JsonNumberHandling.WriteAsString), and ChartData.FromRows throws ArgumentException.", i));
            }
        }

        foreach (var (field, node, series) in Fields(spec))
        {
            var matches = Enumerable.Range(0, members.Count).Where(i => wires[i] == field).ToList();
            var at = $"\"{field}\" ({specPath}({node.Start.Line},{node.Start.Column}))";
            switch (matches.Count)
            {
                case 0 when node.Kind != JsonItemKind.Object || series:
                    issues.Add(series
                        ? new("FLINT120", SpecSeverity.Error, $"{at} is bound by no member of {rowName}; the static series throws without it (series_field_missing).", null)
                        : new("FLINT120", SpecSeverity.Warning, $"{at} is bound by no member of {rowName}; the encoding resolves to nothing (unknown_field).", null));
                    continue;
                case 0:
                    continue;
                case > 1:
                    issues.Add(new("FLINT123", SpecSeverity.Error,
                        $"{at} is bound by more than one member of {rowName}: {string.Join(", ", matches.Select(i => members[i].Name))}.", null));
                    continue;
            }

            var index = matches[0];
            if (members[index].Accessible)
            {
                bound[field] = CSharpText.Escape(members[index].Name);
            }

            if (Check(spec, field, members[index], enumsAsNumbers, rowName) is { } issue)
            {
                issues.Add(issue with { Member = index });
            }
        }

        foreach (var annotation in spec.SemanticTypes?.Members ?? [])
        {
            if (!wires.Contains(annotation.Key))
            {
                issues.Add(new("FLINT116", SpecSeverity.Info,
                    $"\"{annotation.Key}\" ({specPath}({annotation.KeyPosition.Line},{annotation.KeyPosition.Column})) is not a key {rowName} writes; upstream never reads this annotation. (semantic_type_without_field)", null));
            }
        }

        return new RowBinding(bound, issues);
    }

    /// <summary>The fields a selector may name, first-seen: encoding fields (the node, and whether a static-series entry),
    /// then annotation and display-name keys (their parent object as the node, so that a missing one is not reported).</summary>
    private static IEnumerable<(string Field, JsonItem Node, bool Series)> Fields(SpecDocument spec)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in spec.Encodings?.Members ?? [])
        {
            var value = member.Value;
            foreach (var entry in value.IsArray ? value.Items : [value])
            {
                var node = entry.IsString ? entry : entry.IsObject ? entry.Get("field") : null;
                if (node is { IsString: true } && seen.Add(node.Value!))
                {
                    yield return (node.Value!, node, value.IsArray);
                }
            }
        }

        foreach (var map in new[] { spec.SemanticTypes, spec.FieldDisplayNames })
        {
            foreach (var member in map?.Members ?? [])
            {
                if (seen.Add(member.Key))
                {
                    yield return (member.Key, map!, false);
                }
            }
        }
    }

    private static RowBindingIssue? Check(SpecDocument spec, string field, RowMember member, bool enumsAsNumbers, string rowName)
    {
        var encodings = EncodingsOf(spec, field).ToList();
        var category = encodings.Select(e => e.Get("type")).FirstOrDefault(t => t is { IsString: true })?.Value
            ?? (spec.SemanticTypeOf(field) is { } name ? NameTables.SemanticType(name)?.Encoding : null)
            ?? FromData(spec, field);
        var needsNumber = encodings.Any(e => e.Get("aggregate") is { IsString: true } a && Summing.Contains(a.Value!))
            || spec.Encodings?.Members.Any(m => m.Value.IsArray && m.Value.Items.Any(e => FieldOf(e) == field)) == true;
        var kind = member.Kind;
        var subject = $"{rowName}.{member.Name} ({member.TypeDisplay})";

        if (kind == RowMemberKind.Opaque)
        {
            return category is null && !needsNumber
                ? null
                : new("FLINT124", SpecSeverity.Info, $"{subject} is not checked against \"{field}\": its JSON depends on a converter or is any value.", null);
        }

        var numeric = kind is RowMemberKind.Integer or RowMemberKind.Real;
        if (needsNumber && !numeric)
        {
            return Warning($"{subject} is bound to \"{field}\", which the spec sums or plots as a series value; it needs a number.");
        }

        var yearLike = spec.SemanticTypeOf(field) is "Year" or "Decade";
        var verdict = category switch
        {
            "quantitative" => numeric ? Verdict.Accepted : Verdict.Incompatible,
            "temporal" => kind switch
            {
                RowMemberKind.DateOnly or RowMemberKind.DateTime or RowMemberKind.DateTimeOffset or RowMemberKind.String => Verdict.Accepted,
                RowMemberKind.Integer when yearLike => Verdict.Accepted,
                RowMemberKind.TimeOnly or RowMemberKind.TimeSpan or RowMemberKind.Bool or RowMemberKind.Guid or RowMemberKind.Collection or RowMemberKind.Object => Verdict.Incompatible,
                _ => Verdict.Unchecked,
            },
            "ordinal" => kind switch
            {
                RowMemberKind.Collection or RowMemberKind.Object => Verdict.Incompatible,
                RowMemberKind.String or RowMemberKind.Char or RowMemberKind.Enum or RowMemberKind.Integer or RowMemberKind.Real or RowMemberKind.Bool or RowMemberKind.DateOnly => Verdict.Accepted,
                _ => Verdict.Unchecked,
            },
            null => Verdict.Accepted,
            _ => kind switch
            {
                RowMemberKind.Collection or RowMemberKind.Object or RowMemberKind.Real => Verdict.Incompatible,
                RowMemberKind.String or RowMemberKind.Char or RowMemberKind.Enum or RowMemberKind.Bool or RowMemberKind.Guid or RowMemberKind.Uri or RowMemberKind.Integer => Verdict.Accepted,
                _ => Verdict.Unchecked,
            },
        };

        var nominalOrOrdinal = category is not (null or "quantitative" or "temporal");
        if (verdict == Verdict.Accepted && kind == RowMemberKind.Enum && enumsAsNumbers && nominalOrOrdinal)
        {
            return Warning($"{subject} is bound to \"{field}\", which the spec treats as {category}; the row metadata writes the enum as a number, which upstream infers as quantitative. Set UseStringEnumConverter = true on the context.");
        }

        return verdict switch
        {
            Verdict.Incompatible => Warning($"{subject} is bound to \"{field}\", which the spec treats as {Category(category!)}; {Reason(kind, category!)}."),
            Verdict.Unchecked => new("FLINT124", SpecSeverity.Info, $"{subject} is not checked against \"{field}\", which the spec treats as {Category(category!)}.", null),
            _ => null,
        };

        RowBindingIssue Warning(string message) => new("FLINT121", SpecSeverity.Warning, message, null);
    }

    private enum Verdict
    {
        Accepted,
        Incompatible,
        Unchecked,
    }

    private static string Category(string category) => category is "quantitative" or "temporal" or "ordinal" ? category : "nominal";

    private static string Reason(RowMemberKind kind, string category) => (kind, category) switch
    {
        (RowMemberKind.Collection, _) => "a collection is written as a nested array, which upstream reads as a nominal value (nested_value)",
        (RowMemberKind.Object, _) => "a nested object is read as a nominal value (nested_value)",
        (RowMemberKind.TimeSpan, _) => "a TimeSpan is written as \"1.02:03:04\", which upstream reads as temporal",
        (RowMemberKind.TimeOnly, _) => "a TimeOnly is written as \"10:00:00\", which upstream reads as nominal",
        (RowMemberKind.Real, _) => "upstream infers a number as quantitative unless the encoding type says otherwise",
        (_, "quantitative") => $"upstream reads {Article(kind)} as a category, not a number",
        _ => $"upstream does not read {Article(kind)} as {category}",
    };

    private static string Article(RowMemberKind kind) => kind switch
    {
        RowMemberKind.String => "a string",
        RowMemberKind.Char => "a char",
        RowMemberKind.Bool => "a boolean",
        RowMemberKind.Enum => "an enum",
        RowMemberKind.Guid => "a Guid",
        RowMemberKind.Uri => "a Uri",
        _ => $"a {kind}",
    };

    /// <summary>The upstream inference over <c>data.values</c>, narrowed to what it decides without <c>Date.parse</c>: all
    /// numbers infer as quantitative, all booleans as nominal; anything else is not checked.</summary>
    private static string? FromData(SpecDocument spec, string field)
    {
        var values = (spec.Values?.Items ?? []).Where(r => r.IsObject).Select(r => r.Get(field)).Where(v => v is { IsNull: false }).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return values.All(v => v!.IsNumber) ? "quantitative" : values.All(v => v!.IsBoolean) ? "nominal" : null;
    }

    private static IEnumerable<JsonItem> EncodingsOf(SpecDocument spec, string field) =>
        (spec.Encodings?.Members ?? []).SelectMany(m => m.Value.IsArray ? m.Value.Items : [m.Value]).Where(e => e.IsObject && FieldOf(e) == field);

    private static string? FieldOf(JsonItem entry) =>
        entry.IsString ? entry.Value : entry.IsObject && entry.Get("field") is { IsString: true } field ? field.Value : null;
}
