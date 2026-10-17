namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// Three-valued conditions: true and false where the outcome is decided, null where it depends on a type the validator
/// cannot resolve. A rule reports an Error only for a condition that is true.
/// </summary>
internal static class Tri
{
    internal static bool? Or(params bool?[] conditions) =>
        conditions.Any(c => c == true) ? true : conditions.Any(c => c is null) ? null : false;

    internal static bool? And(params bool?[] conditions) =>
        conditions.Any(c => c == false) ? false : conditions.Any(c => c is null) ? null : true;

    internal static bool? IsQuantitative(FieldType t) => t == FieldType.Unknown ? null : t == FieldType.Quantitative;

    internal static bool? NotQuantitative(FieldType t) => t == FieldType.Unknown ? null : t != FieldType.Quantitative;

    internal static bool? IsTemporal(FieldType t) => t is FieldType.Unknown or FieldType.TemporalOrOrdinal ? null : t == FieldType.Temporal;

    /// <summary>Quantitative or temporal: the "continuous" of the Excel templates.</summary>
    internal static bool? IsContinuous(FieldType t) =>
        t is FieldType.Unknown or FieldType.TemporalOrOrdinal ? null : t is FieldType.Quantitative or FieldType.Temporal;
}
