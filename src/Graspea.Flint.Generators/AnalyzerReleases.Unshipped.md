; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FLINT100 | Flint | Error | Graspea.Flint is not referenced, or its version differs from the generator's
FLINT101 | Flint | Error | The attribute path matches no AdditionalFiles item
FLINT102 | Flint | Error | The spec is not valid JSON
FLINT103 | Flint | Error | The JSON is not a ChartAssemblyInput
FLINT104 | Flint | Error | The target is not a non-generic static partial class with partial containing types, or declares a generated member name
FLINT105 | Flint | Error | Invalid attribute use
FLINT106 | Flint | Warning | Mode B: the JsonContext has no [JsonSerializable] that names the row type
FLINT107 | Flint | Error | The language version is below C# 12
FLINT110 | Flint | Error | unknown_chart_type
FLINT111 | Flint | Warning | unsupported_channel
FLINT112 | Flint | Warning | invalid_option_value
FLINT113 | Flint | Warning | unknown_semantic_type
FLINT114 | Flint | Error | unknown_theme_preset (Info for a template on no backend that applies themes)
FLINT115 | Flint | Error | A throw-class rule that needs no data
FLINT116 | Flint | Info | The Info codes that need no data
FLINT120 | Flint | Warning | Mode B: no member binds a spec field (Error for a static-series entry)
FLINT121 | Flint | Warning | Mode B: the member's type does not fit the field
FLINT122 | Flint | Error | Mode B: the row metadata may omit a null member or writes numbers as strings
FLINT123 | Flint | Error | Mode B: two members bind one field
FLINT124 | Flint | Info | Mode B: a member type the generator does not check
FLINT130 | Flint | Info | Mode A: a field type could not be inferred and is string
FLINT131 | Flint | Warning | Mode A: rows are not uniform objects; every field is JsonNode?
FLINT133 | Flint | Info | data.url in the spec is not used by the generated members
FLINT140 | Flint | Info | Unknown keys are passed through Extensions
