namespace Graspea.Flint.Validation.Rules;

/// <summary>A static-series violation: the code, its message and the offending channel.</summary>
internal sealed record SeriesViolation(string Code, string Message, string Channel, string? Field = null);

/// <summary>
/// The throw conditions of <c>normalizeStaticSeries</c> (src/core/static-series.ts of the pinned package), which the
/// four layout backends run (design §6 <c>series_*</c>). The checks run in upstream's order and the first that holds is
/// reported, because upstream throws there. Excel does not run them: it reads an array encoding as an unbound channel, so its
/// template rules (<see cref="Excel"/>) decide.
/// </summary>
internal static class StaticSeries
{
    internal static List<SeriesViolation> Evaluate(ChartAssemblyInput input, FieldTypes types)
    {
        var violations = new List<SeriesViolation>();
        var encodings = input.ChartSpec.Encodings;
        var series = encodings.Where(e => e.Value is { IsSeries: true }).ToArray();
        if (series.Length == 0)
        {
            return violations;
        }

        if (series.Length > 1)
        {
            violations.Add(new(Codes.SeriesMultipleChannels, $"Only one channel may use a static series (array encoding); found {string.Join(", ", series.Select(s => s.Key))}.", series[1].Key));
            return violations;
        }

        var (channel, value) = series[0];
        if (channel is not ("x" or "y"))
        {
            violations.Add(new(Codes.SeriesChannelNotXy, $"A static series is only allowed on x or y, not \"{channel}\".", channel));
            return violations;
        }

        var entries = value!.Encodings;
        if (entries.Count < 2)
        {
            violations.Add(new(Codes.SeriesTooFew, $"A static series needs at least 2 fields; \"{channel}\" has {entries.Count}.", channel));
            return violations;
        }

        if (entries.FirstOrDefault(e => string.IsNullOrEmpty(e.Field)) is not null)
        {
            violations.Add(new(Codes.SeriesFieldMissing, "Each static series entry must have a field.", channel));
            return violations;
        }

        var fields = entries.Select(e => e.Field!).ToArray();
        if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Length)
        {
            violations.Add(new(Codes.SeriesDuplicateField, "A static series contains duplicate fields; each field must be unique.", channel));
            return violations;
        }

        var rows = types.Rows;
        if (rows.Count > 0)
        {
            var columns = input.Data.FirstRowKeys;
            if (fields.FirstOrDefault(f => !columns.Contains(f)) is { } missing)
            {
                violations.Add(new(Codes.SeriesFieldMissing, $"Static series field \"{missing}\" is not a key of the first row.", channel, missing));
                return violations;
            }
        }

        foreach (var entry in entries)
        {
            var explicitType = entry.Type?.Name;
            if (explicitType is "nominal" or "ordinal")
            {
                violations.Add(new(Codes.SeriesNominalEntry, $"Static series field \"{entry.Field}\" has type \"{explicitType}\"; only quantitative or temporal fields are allowed.", channel, entry.Field));
                return violations;
            }

            if (explicitType is null && rows.Count > 0)
            {
                var semanticType = types.SuppliedSemanticType(entry.Field!);
                // getVisCategory: visEncodings[0] ?? null, then inference from the data.
                var category = semanticType.Length > 0 && CatalogTables.SemanticTypes.TryGetValue(semanticType, out var candidates) && candidates.Length > 0
                    ? candidates[0]
                    : FieldTypes.InferVisCategory(types.Values(entry.Field!));
                if (category is "nominal" or "ordinal")
                {
                    violations.Add(new(Codes.SeriesNominalEntry, $"Static series field \"{entry.Field}\" resolves as \"{category}\"; only quantitative or temporal fields are allowed.", channel, entry.Field));
                    return violations;
                }
            }
        }

        if (encodings.TryGetValue("color", out var color) && color is { IsSeries: false } && !string.IsNullOrEmpty(color.Encodings[0].Field))
        {
            violations.Add(new(Codes.SeriesColorBound, $"A static series on \"{channel}\" uses the color channel, which is bound to \"{color.Encodings[0].Field}\".", "color", color.Encodings[0].Field));
        }

        return violations;
    }
}
