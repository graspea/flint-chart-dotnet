using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint;

namespace Graspea.Flint.Samples.Generators;

/// <summary>Mode A: the Row record is inferred from Charts/revenue.flint.json.</summary>
[FlintChart("Charts/revenue.flint.json")]
public static partial class RevenueChart;

// Mode B: the spec's fields bind to the members of Sale, written through SalesJson. The region is the README's
// generator example (tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs).
#region readme:generator-declaration
[FlintChart<Sale>("Charts/units.flint.json", JsonContext = typeof(SalesJson))]
public static partial class UnitsChart;
#endregion

/// <summary>A sales region.</summary>
public enum SalesRegion
{
    North,
    South,
}

/// <summary>A row of sales; keys are camelCase (JsonSerializerDefaults.Web).</summary>
/// <param name="Region">The region.</param>
/// <param name="Units">The units sold.</param>
public sealed record Sale(SalesRegion Region, int Units);

/// <summary>Source-generated metadata for <see cref="Sale"/>: camelCase keys, enums as strings, nulls written.</summary>
[JsonSerializable(typeof(Sale))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
public sealed partial class SalesJson : JsonSerializerContext;
