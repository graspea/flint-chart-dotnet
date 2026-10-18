using System.Globalization;
using System.Text.RegularExpressions;

namespace Graspea.Flint.CSharp;

/// <summary>The .NET type of an inferred row member.</summary>
internal enum RowFieldKind
{
    Bool,
    Int,
    Long,
    Double,
    Decimal,
    DateOnly,
    DateTimeOffset,
    String,
    Json,
}

/// <summary>A member of the inferred <c>Row</c> record: its wire name, C# name, type and nullability.</summary>
internal sealed record RowField(string Wire, string Member, RowFieldKind Kind, bool Nullable)
{
    /// <summary>The C# type as declared, such as <c>long?</c> or <c>JsonNode?</c>.</summary>
    public string TypeText => Kind switch
    {
        RowFieldKind.Bool => "bool",
        RowFieldKind.Int => "int",
        RowFieldKind.Long => "long",
        RowFieldKind.Double => "double",
        RowFieldKind.Decimal => "decimal",
        RowFieldKind.DateOnly => "DateOnly",
        RowFieldKind.DateTimeOffset => "DateTimeOffset",
        RowFieldKind.String => "string",
        _ => "JsonNode",
    } + (Nullable || Kind == RowFieldKind.Json ? "?" : string.Empty);
}

/// <summary>The inferred row type (design §12.4.3).</summary>
internal sealed class RowPlan(bool typed, IReadOnlyList<RowField> fields, IReadOnlyList<string> unhintedFields)
{
    /// <summary>Whether every row is an object with the same keys in the same order (the shape gate).</summary>
    public bool Typed { get; } = typed;

    public IReadOnlyList<RowField> Fields { get; } = fields;

    /// <summary>Fields without values and without a type hint, typed <c>string</c> (the generator's FLINT130).</summary>
    public IReadOnlyList<string> UnhintedFields { get; } = unhintedFields;

    public RowField? Field(string wire) => Fields.FirstOrDefault(f => f.Wire == wire);
}

/// <summary>
/// Typed row inference, shared by the tool and the generator so both infer the same <c>Row</c> (design §12.4.3). It is
/// lossless for the data in the spec: rows of the inferred type write the same values.
/// </summary>
internal static class RowInference
{
    private const long MaxSafeInteger = 9007199254740992L;

    private static readonly Regex IsoDate = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> RecordMembers = new(StringComparer.Ordinal)
    {
        "Row", "EqualityContract", "Equals", "GetHashCode", "ToString", "Deconstruct", "PrintMembers", "GetType", "Clone",
    };

    /// <summary>
    /// Infers the row type. With <paramref name="replay"/> (the rows are written from the spec, the tool's inline mode) and
    /// <c>data.values</c> present, the fields are exactly the keys of the first row; otherwise they are the union of the
    /// data keys, the encoding fields, the <c>semantic_types</c> keys and the <c>field_display_names</c> keys.
    /// </summary>
    public static RowPlan Infer(SpecDocument spec, bool replay)
    {
        var values = spec.Values;
        var rows = values?.Items ?? [];
        var typed = IsUniform(rows);
        var wires = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string wire)
        {
            if (seen.Add(wire))
            {
                wires.Add(wire);
            }
        }

        foreach (var row in rows.Where(r => r.IsObject))
        {
            foreach (var member in row.Members)
            {
                Add(member.Key);
            }

            if (typed)
            {
                break;
            }
        }

        if (!replay || values is null)
        {
            foreach (var field in spec.EncodingFields())
            {
                Add(field.Field);
            }

            foreach (var member in spec.SemanticTypes?.Members ?? [])
            {
                Add(member.Key);
            }

            foreach (var member in spec.FieldDisplayNames?.Members ?? [])
            {
                Add(member.Key);
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<RowField>();
        var unhinted = new List<string>();
        for (var i = 0; i < wires.Count; i++)
        {
            var wire = wires[i];
            var member = MemberName(wire, i + 1, names);
            if (!typed && values is not null)
            {
                fields.Add(new RowField(wire, member, RowFieldKind.Json, true));
                continue;
            }

            var present = rows.Select(r => r.Get(wire)).ToList();
            var nonNull = present.Where(v => v is { IsNull: false }).Select(v => v!).ToList();
            var nullable = present.Any(v => v is null or { IsNull: true });
            var kind = nonNull.Count > 0 ? FromValues(nonNull, spec, wire) : FromHints(spec, wire);
            if (kind is null)
            {
                unhinted.Add(wire);
                kind = RowFieldKind.String;
            }

            fields.Add(new RowField(wire, member, kind.Value, nullable));
        }

        return new RowPlan(typed, fields, unhinted);
    }

    private static bool IsUniform(IReadOnlyList<JsonItem> rows)
    {
        if (rows.Count == 0)
        {
            return true;
        }

        if (rows.Any(r => !r.IsObject))
        {
            return false;
        }

        var keys = rows[0].Members.Select(m => m.Key).ToList();
        return rows.All(r => r.Members.Select(m => m.Key).SequenceEqual(keys, StringComparer.Ordinal));
    }

    private static string MemberName(string wire, int position, HashSet<string> names)
    {
        var name = CSharpText.Pascal(wire);
        if (name.Length == 0)
        {
            name = $"Field{position}";
        }
        else if (RecordMembers.Contains(name))
        {
            name += "Field";
        }

        var unique = name;
        for (var n = 2; !names.Add(unique); n++)
        {
            unique = name + n.ToString(CultureInfo.InvariantCulture);
        }

        return unique;
    }

    private static RowFieldKind FromValues(List<JsonItem> values, SpecDocument spec, string wire)
    {
        if (values.All(v => v.IsBoolean))
        {
            return RowFieldKind.Bool;
        }

        if (values.All(v => v.IsNumber))
        {
            if (values.Any(v => !CSharpText.FitsDouble(v.Value!)))
            {
                return RowFieldKind.Json;
            }

            if (spec.SemanticTypeOf(wire) is "Amount" or "Price" or "Profit" && values.All(v => FitsDecimal(v.Value!)))
            {
                return RowFieldKind.Decimal;
            }

            return values.All(v => IsSafeInteger(v.Value!)) ? RowFieldKind.Long : RowFieldKind.Double;
        }

        if (values.All(v => v.IsString))
        {
            var hint = HintKind(spec, wire);
            return (hint is null or "temporal") && values.All(v => IsDate(v.Value!)) ? RowFieldKind.DateOnly : RowFieldKind.String;
        }

        return RowFieldKind.Json;
    }

    private static RowFieldKind? FromHints(SpecDocument spec, string wire)
    {
        var explicitType = spec.EncodingFields().FirstOrDefault(f => f.Field == wire && f.Type is not null)?.Type;
        var semanticType = spec.SemanticTypeOf(wire);
        if (explicitType is null && semanticType is not null && NameTables.SemanticType(semanticType) is not null)
        {
            switch (semanticType)
            {
                case "Count" or "Rank" or "Year" or "Decade":
                    return RowFieldKind.Int;
                case "Amount" or "Price" or "Profit":
                    return RowFieldKind.Decimal;
                case "DateTime" or "Timestamp":
                    return RowFieldKind.DateTimeOffset;
                case "Boolean":
                    return RowFieldKind.Bool;
            }
        }

        return HintKind(spec, wire) switch
        {
            "quantitative" => RowFieldKind.Double,
            "temporal" => RowFieldKind.DateOnly,
            null => null,
            _ => RowFieldKind.String,
        };
    }

    /// <summary>The field's type hint: an encoding's explicit <c>type</c>, else the registered semantic type's first
    /// <c>visEncodings</c> entry; null when there is none.</summary>
    private static string? HintKind(SpecDocument spec, string wire) =>
        spec.EncodingFields().FirstOrDefault(f => f.Field == wire && f.Type is not null)?.Type
        ?? (spec.SemanticTypeOf(wire) is { } name ? NameTables.SemanticType(name)?.Encoding : null);

    private static bool IsSafeInteger(string raw) =>
        CSharpText.IsIntegralText(raw)
        && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
        && value >= -MaxSafeInteger && value <= MaxSafeInteger;

    /// <summary>Fits <c>decimal</c> as a literal: no exponent (System.Text.Json reads a decimal without one) and in range.</summary>
    private static bool FitsDecimal(string raw) =>
        raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0
        && decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    private static bool IsDate(string text) =>
        IsoDate.IsMatch(text)
        && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
