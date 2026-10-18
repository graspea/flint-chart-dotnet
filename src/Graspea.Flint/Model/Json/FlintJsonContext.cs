using System.Text.Json.Serialization;

namespace Graspea.Flint.Serialization;

/// <summary>
/// Source-generated metadata the converters serialise through, independent of the caller's options (design §5.1).
/// Lists the converter-owned DTOs and the map value types of hand-written members. The map value types of generated
/// members are registered by the generator in <see cref="FlintMapJsonContext"/>, because System.Text.Json reads
/// <c>[JsonSerializable]</c> from one partial declaration only.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(ChartAssemblyInput))]
[JsonSerializable(typeof(ChartDataDto))]
[JsonSerializable(typeof(ChartEncoding))]
[JsonSerializable(typeof(EncodingValue))]
[JsonSerializable(typeof(ContinuousMarkCrossSectionDto))]
[JsonSerializable(typeof(SemanticAnnotation))]
[JsonSerializable(typeof(SemanticAnnotationDto))]
[JsonSerializable(typeof(Theming.ThemeSpec))]
internal sealed partial class FlintJsonContext : JsonSerializerContext;
