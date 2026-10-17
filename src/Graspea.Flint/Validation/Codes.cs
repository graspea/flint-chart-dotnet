namespace Graspea.Flint.Validation;

/// <summary>The issue codes (design §6; docs/validation-codes.md).</summary>
internal static class Codes
{
    // Error where the pinned assemblers throw.
    internal const string UnknownChartType = "unknown_chart_type";
    internal const string ChartTypeNotOnBackend = "chart_type_not_on_backend";
    internal const string MissingEncodings = "missing_encodings";
    internal const string EncodingsEmpty = "encodings_empty";
    internal const string ChannelNull = "channel_null";
    internal const string DataMissing = "data_missing";
    internal const string UnknownThemePreset = "unknown_theme_preset";
    internal const string SeriesMultipleChannels = "series_multiple_channels";
    internal const string SeriesChannelNotXy = "series_channel_not_xy";
    internal const string SeriesTooFew = "series_too_few";
    internal const string SeriesDuplicateField = "series_duplicate_field";
    internal const string SeriesFieldMissing = "series_field_missing";
    internal const string SeriesColorBound = "series_color_bound";
    internal const string SeriesNominalEntry = "series_nominal_entry";
    internal const string DataEmpty = "data_empty";
    internal const string DataUrlOnly = "data_url_only";
    internal const string ExcelUnsupported = "excel_unsupported";
    internal const string RowNotObject = "row_not_object";
    internal const string ReservedSemanticType = "reserved_semantic_type";

    // Warning where upstream silently drops.
    internal const string UnsupportedChannel = "unsupported_channel";
    internal const string UnknownField = "unknown_field";
    internal const string InvalidOptionValue = "invalid_option_value";
    internal const string UnknownSemanticType = "unknown_semantic_type";
    internal const string SortByInvalid = "sort_by_invalid";

    // Info.
    internal const string DataUrlNotFetched = "data_url_not_fetched";
    internal const string CanvasTooLarge = "canvas_too_large";
    internal const string InvalidLiteral = "invalid_literal";
    internal const string InconsistentRowKeys = "inconsistent_row_keys";
    internal const string RequiredChannelMissing = "required_channel_missing";
    internal const string DataValuesAndUrl = "data_values_and_url";
    internal const string TooManyRows = "too_many_rows";
    internal const string SemanticTypeWithoutField = "semantic_type_without_field";
    internal const string UnknownChartProperty = "unknown_chart_property";
    internal const string ContinuousOutOfRange = "continuous_out_of_range";
    internal const string ThemeIgnoredByBackend = "theme_ignored_by_backend";
    internal const string OptionOverriddenByBackend = "option_overridden_by_backend";
    internal const string LargeInteger = "large_integer";
    internal const string UnspecifiedDatetime = "unspecified_datetime";
    internal const string TimespanAsString = "timespan_as_string";
    internal const string NullInTemporalField = "null_in_temporal_field";
    internal const string NestedValue = "nested_value";
}
