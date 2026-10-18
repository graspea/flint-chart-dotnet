using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Graspea.Flint.Validation.Rules;

/// <summary>The encoding type upstream resolves for a channel, or <see cref="Unknown"/> where deciding it needs a JavaScript engine.</summary>
internal enum FieldType
{
    /// <summary>No resolvable binding: the channel is unbound, null, a static series, or has no field and no <c>count</c>.</summary>
    Absent,
    Quantitative,
    Nominal,
    Ordinal,
    Temporal,

    /// <summary>Temporal or ordinal: which one depends on V8's <c>Date.parse</c>; never quantitative.</summary>
    TemporalOrOrdinal,

    /// <summary>An explicit <c>type</c> outside the four literals.</summary>
    Other,
    Unknown,
}

/// <summary>
/// The type decision of upstream's <c>resolveChannelSemantics</c> (src/core/field-semantics.ts and decisions.ts of the
/// pinned package): explicit <c>type</c>, then <c>aggregate</c>, the registered or implicit (<c>Year</c>) semantic type
/// with its data guards, and value inference. Every step that would need <c>Date.parse</c> beyond what <see cref="Js.DateParses"/>
/// decides yields <see cref="FieldType.Unknown"/> (or <see cref="FieldType.TemporalOrOrdinal"/>), and a rule that reads
/// such a type skips (design §6: no rule guesses).
/// </summary>
internal sealed partial class FieldTypes
{
    private readonly ChartAssemblyInput input;
    private readonly IReadOnlyList<JsonNode?> rows;
    private readonly Dictionary<string, FieldType> cache = new(StringComparer.Ordinal);

    internal FieldTypes(ChartAssemblyInput input)
    {
        this.input = input;
        rows = input.Data?.Values is { } values ? [.. values] : [];
    }

    internal IReadOnlyList<JsonNode?> Rows => rows;

    /// <summary>The single encoding bound to <paramref name="channel"/>, or null when unbound, null or a static series.</summary>
    internal ChartEncoding? Encoding(string channel) =>
        input.ChartSpec?.Encodings is { } encodings && encodings.TryGetValue(channel, out var value) && value is { IsSeries: false }
            ? value.Encodings[0]
            : null;

    /// <summary><c>encodings[ch]?.field</c> as the Excel assembler reads it (null and empty are both "no field").</summary>
    internal string? FieldOf(string channel) => Encoding(channel)?.Field is { Length: > 0 } field ? field : null;

    /// <summary><c>sem[ch]?.type</c>.</summary>
    internal FieldType Of(string channel)
    {
        if (!cache.TryGetValue(channel, out var type))
        {
            type = Resolve(channel);
            cache[channel] = type;
        }

        return type;
    }

    internal (JsonNode? Value, bool Present)[] Values(string field) =>
        rows.Select(r => (Js.Get(r, field, out var present), present)).ToArray();

    /// <summary>The supplied semantic type name of a field (<c>""</c> when none).</summary>
    internal string SuppliedSemanticType(string field) =>
        input.SemanticTypes is { } types && types.TryGetValue(field, out var annotation) && annotation?.Type.Name is { } name ? name : string.Empty;

    private FieldType Resolve(string channel)
    {
        if (Encoding(channel) is not { } e)
        {
            return FieldType.Absent;
        }

        if (string.IsNullOrEmpty(e.Field))
        {
            return e.Aggregate?.Name == "count" ? FieldType.Quantitative : FieldType.Absent;
        }

        var field = e.Field;
        var values = Values(field);
        var supplied = SuppliedSemanticType(field);
        var semanticType = supplied.Length > 0 ? supplied : ImplicitYear(field, values);
        var resolved = ResolveEncodingType(semanticType, values, channel, field);

        if (e.Type?.Name is { Length: > 0 } explicitType)
        {
            resolved = explicitType switch
            {
                "quantitative" => FieldType.Quantitative,
                "nominal" => FieldType.Nominal,
                "ordinal" => FieldType.Ordinal,
                "temporal" => FieldType.Temporal,
                _ => FieldType.Other,
            };
        }
        else if (channel is "column" or "row" && resolved is not (FieldType.Nominal or FieldType.Ordinal))
        {
            // Upstream keeps nominal and ordinal and makes everything else nominal; both are categorical, which is all a
            // rule distinguishes, so an unknown type is categorical here too.
            resolved = FieldType.Nominal;
        }

        if (resolved == FieldType.Quantitative && LooksLikeIsoDateTimes(field))
        {
            resolved = FieldType.Temporal;
        }

        if (e.Aggregate is { Name.Length: > 0 })
        {
            resolved = FieldType.Quantitative;
        }

        return resolved;
    }

    /// <summary>The first 15 rows' defined values all look like ISO date-times (upstream turns quantitative into temporal).</summary>
    private bool LooksLikeIsoDateTimes(string field)
    {
        var sample = rows.Take(15).Select(r => Js.Get(r, field, out var present) is var v && !Js.IsNullish(v, present) ? Js.ToJsString(v) : null)
            .Where(v => v is not null).ToArray();
        return sample.Length > 0 && sample.All(v => IsoDateTime().IsMatch(Js.Trim(v!)));
    }

    private FieldType ResolveEncodingType(string semanticType, (JsonNode? Value, bool Present)[] values, string channel, string field)
    {
        // A registered type without candidates has no registry category, as upstream's visEncodings[0] ?? null.
        if (semanticType.Length > 0 && CatalogTables.SemanticTypes.TryGetValue(semanticType, out var candidates) && candidates.Length > 0)
        {
            if (candidates.Length > 1)
            {
                return Disambiguate(candidates, channel, field);
            }

            switch (candidates[0])
            {
                case "quantitative":
                    var nonNull = values.Where(v => !Js.IsNullish(v.Value, v.Present)).ToArray();
                    var allNumeric = nonNull.Length > 0 && nonNull.All(v => Js.Kind(v.Value) == JsonValueKind.Number
                        || (Js.Kind(v.Value) == JsonValueKind.String && !double.IsNaN(Js.ToNumber(v.Value)) && Js.Trim(v.Value!.GetValue<string>()).Length > 0));
                    return allNumeric ? FieldType.Quantitative : FromCategory(InferVisCategory(values));
                case "temporal":
                    return TemporalEncoding(channel, field, fromRegistry: true);
                case "ordinal":
                    return OrdinalGuards(channel, values, fromRegistry: true);
                default:
                    return FromCategory(candidates[0]);
            }
        }

        return InferVisCategory(values) switch
        {
            "temporal" => TemporalEncoding(channel, field, fromRegistry: false),
            "quantitative" => FieldType.Quantitative,
            "nominal" => FieldType.Nominal,
            _ => FieldType.Unknown,
        };
    }

    /// <summary><c>visCategoryToVLType</c>; null (undecided) is unknown.</summary>
    private static FieldType FromCategory(string? category) => category switch
    {
        "quantitative" or "geographic" => FieldType.Quantitative,
        "ordinal" => FieldType.Ordinal,
        "temporal" => FieldType.Temporal,
        null => FieldType.Unknown,
        _ => FieldType.Nominal,
    };

    private FieldType Disambiguate(string[] candidates, string channel, string field)
    {
        bool Has(string c) => candidates.Contains(c);
        if (Has("temporal") && Has("ordinal"))
        {
            return TemporalEncoding(channel, field, fromRegistry: true);
        }

        if (Has("quantitative") && Has("ordinal"))
        {
            return channel switch
            {
                "color" or "group" => DistinctCount(field) <= 12 ? FieldType.Ordinal : FieldType.Quantitative,
                "column" or "row" => FieldType.Ordinal,
                _ => FieldType.Quantitative,
            };
        }

        if (Has("quantitative") && Has("geographic"))
        {
            return FieldType.Quantitative;
        }

        if (Has("ordinal") && Has("nominal"))
        {
            return channel is "color" or "group" ? FieldType.Nominal : FieldType.Ordinal;
        }

        return FromCategory(candidates[0]);
    }

    /// <summary>
    /// Whether <c>convertTemporalData</c> finds a temporal key among <paramref name="keys"/> (the keys of the first row): a
    /// supplied or implicit semantic type whose registry category is temporal, <c>Decade</c>, or values that infer as
    /// temporal. Null when only <c>Date.parse</c> could decide.
    /// </summary>
    internal bool? HasTemporalKey(IEnumerable<string> keys)
    {
        var undecided = false;
        foreach (var key in keys)
        {
            var values = Values(key);
            var supplied = SuppliedSemanticType(key);
            var semanticType = supplied.Length > 0 ? supplied : ImplicitYear(key, values);
            if (semanticType == "Decade"
                || (CatalogTables.SemanticTypes.TryGetValue(semanticType, out var candidates) && candidates is ["temporal", ..]))
            {
                return true;
            }

            switch (InferVisCategory(values))
            {
                case "temporal":
                    return true;
                case null:
                    undecided = true;
                    break;
            }
        }

        return undecided ? null : false;
    }

    /// <summary><c>new Set(data.map(r => r[field])).size</c>.</summary>
    internal int DistinctCount(string field) => rows.Select(r => Js.SetKey(Js.Get(r, field, out var present), present)).Distinct().Count();

    private FieldType TemporalEncoding(string channel, string field, bool fromRegistry)
    {
        if (channel is "size" or "column" or "row")
        {
            return FieldType.Ordinal;
        }

        if (channel == "color" && DistinctCount(field) <= 12)
        {
            return FieldType.Ordinal;
        }

        return ValidateTemporalParsing(field, fromRegistry) switch
        {
            true => FieldType.Temporal,
            false => FieldType.Ordinal,
            null => FieldType.TemporalOrOrdinal,
        };
    }

    /// <summary><c>validateTemporalParsing</c>: up to 15 distinct defined values, enough of which look temporal.</summary>
    private bool? ValidateTemporalParsing(string field, bool fromRegistry)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sample = new List<JsonNode?>();
        foreach (var row in rows)
        {
            var value = Js.Get(row, field, out var present);
            if (Js.IsNullish(value, present))
            {
                continue;
            }

            var key = (Js.Kind(value) switch
            {
                JsonValueKind.Number => "number:",
                JsonValueKind.String => "string:",
                JsonValueKind.True or JsonValueKind.False => "boolean:",
                _ => "object:",
            }) + Js.ToJsString(value);
            if (!seen.Add(key))
            {
                continue;
            }

            sample.Add(value);
            if (sample.Count >= 15)
            {
                break;
            }
        }

        if (sample.Count <= 1)
        {
            return false;
        }

        var looks = sample.Select(LooksTemporal).ToArray();
        var minimum = fromRegistry ? 0.3 : 0.5;
        var sure = looks.Count(l => l == true);
        var possible = sure + looks.Count(l => l is null);
        if ((double)sure / looks.Length >= minimum)
        {
            return true;
        }

        return (double)possible / looks.Length < minimum ? false : null;
    }

    private static bool? LooksTemporal(JsonNode? value)
    {
        switch (Js.Kind(value))
        {
            case JsonValueKind.Number:
                var n = Js.NumberOf(value!);
                return (n is >= 1500 and <= 2200 && n % 1 == 0) || n is > 864e5 and < 42e11;
            case JsonValueKind.String:
                var s = Js.Trim(value!.GetValue<string>());
                if (s.Length == 0)
                {
                    return false;
                }

                return FourDigits().IsMatch(s) ? true : Js.DateParses(s);
            default:
                return false;
        }
    }

    private FieldType OrdinalGuards(string channel, (JsonNode? Value, bool Present)[] values, bool fromRegistry)
    {
        var numeric = values.Where(v => !Js.IsNullish(v.Value, v.Present) && !double.IsNaN(Js.ToNumber(v.Value))).Select(v => Js.ToNumber(v.Value)).ToArray();
        if (numeric.Length > 0)
        {
            var unique = numeric.Select(d => d == 0 ? 0d : d).Distinct().Count();
            var hasFractions = numeric.Any(d => d % 1 != 0);
            if (!fromRegistry && hasFractions && unique > 20)
            {
                return FieldType.Quantitative;
            }

            if (!hasFractions && unique > 12 && channel is "color" or "group" or "x" or "y")
            {
                return FieldType.Quantitative;
            }
        }

        return FieldType.Ordinal;
    }

    /// <summary>
    /// <c>inferVisCategory</c>: <c>"nominal"</c>, <c>"quantitative"</c> or <c>"temporal"</c>, or null when only
    /// <c>Date.parse</c> could decide between temporal and nominal.
    /// </summary>
    internal static string? InferVisCategory((JsonNode? Value, bool Present)[] values)
    {
        if (values.Length == 0)
        {
            return "nominal";
        }

        var nonNull = values.Where(v => !Js.IsNullish(v.Value, v.Present)).Select(v => v.Value).ToArray();
        if (nonNull.Length == 0 || nonNull.All(v => Js.Kind(v) is JsonValueKind.True or JsonValueKind.False))
        {
            return "nominal";
        }

        if (nonNull.All(v => !double.IsNaN(Js.ToNumber(v))))
        {
            return "quantitative";
        }

        var dates = nonNull.Select(IsDate).ToArray();
        if (dates.Any(d => d == false))
        {
            return "nominal";
        }

        return dates.All(d => d == true) ? "temporal" : null;
    }

    private static bool? IsDate(JsonNode? value)
    {
        if (Js.Kind(value) == JsonValueKind.String)
        {
            var s = value!.GetValue<string>();
            return LooksLikeDate().IsMatch(Js.Trim(s)) ? Js.DateParses(s) : false;
        }

        // Date.parse(String(v)): V8 reads many bare numbers as dates, so a number is undecided.
        return Js.Kind(value) == JsonValueKind.Number ? null : Js.DateParses(Js.ToJsString(value));
    }

    /// <summary><c>inferImplicitSemanticType</c>: a field named with the token <c>year</c> whose values are distinct integer years.</summary>
    internal static string ImplicitYear(string field, (JsonNode? Value, bool Present)[] values)
    {
        var tokens = NonAlphanumeric().Split(CamelBoundary().Replace(field, "$1 $2").ToLowerInvariant()).Where(t => t.Length > 0);
        if (!tokens.Contains("year"))
        {
            return string.Empty;
        }

        var observed = values.Where(v => !Js.IsNullish(v.Value, v.Present) && !(Js.Kind(v.Value) == JsonValueKind.String && v.Value!.GetValue<string>().Length == 0)).ToArray();
        if (observed.Select(v => Js.ToJsString(v.Value)).Distinct(StringComparer.Ordinal).Count() <= 1)
        {
            return string.Empty;
        }

        return observed.All(v => Js.ToNumber(v.Value) is var n && n % 1 == 0 && n is >= 1500 and <= 2200) ? "Year" : string.Empty;
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateTime();

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FourDigits();

    [GeneratedRegex("^[0-9]|^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeDate();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();
}
