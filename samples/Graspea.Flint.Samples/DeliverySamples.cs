using System.Text.Json.Serialization;
using Graspea.Flint.Delivery;

namespace Graspea.Flint.Samples;

public sealed record Sale(DateOnly Month, string Region, decimal Revenue);

public sealed record Visit(string Source, long Visits);

public sealed record Stock(string Warehouse, int Units);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Sale))]
[JsonSerializable(typeof(Visit))]
[JsonSerializable(typeof(Stock))]
internal sealed partial class DeliveryJson : JsonSerializerContext;

/// <summary>Charts for the browser data delivery samples: three charts, two groups, one manifest.</summary>
internal static class DeliverySamples
{
    internal static readonly string[] Regions = ["North", "South", "East"];
    internal static readonly string[] Warehouses = ["Alpha", "Beta", "Gamma"];
    internal static readonly string[] Sources = ["search", "direct", "social"];

    internal static readonly ChartHandle<Sale> Revenue = new("revenue-by-month",
        Chart.Line(DeliveryJson.Default.Sale)
            .X(r => r.Month, SemanticType.YearMonth).Y(r => r.Revenue, SemanticType.Amount).Color(r => r.Region)
            .Encoding(Channel.X, e => e.Type = EncodingType.Temporal)
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .Encoding(Channel.Color, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder(Regions); })
            .ToTemplate(),
        maxRows: 10_000);

    internal static readonly ChartHandle<Visit> Traffic = new("traffic-by-source",
        Chart.Bar(DeliveryJson.Default.Visit)
            .X(r => r.Source).Y(r => r.Visits)
            .Encoding(Channel.X, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder(Sources); })
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .ToTemplate(),
        maxRows: 10_000);

    internal static readonly ChartHandle<Stock> StockLevels = new("stock-by-warehouse",
        Chart.Bar(DeliveryJson.Default.Stock)
            .X(r => r.Warehouse).Y(r => r.Units)
            .Encoding(Channel.X, e => { e.Type = EncodingType.Nominal; e.SortBy = SortBy.ExplicitOrder(Warehouses); })
            .Encoding(Channel.Y, e => e.Type = EncodingType.Quantitative)
            .ToTemplate());

    internal static readonly ChartGroup Overview = new("overview", Revenue, Traffic);
    internal static readonly ChartGroup Inventory = new("inventory", StockLevels);
    internal static readonly ChartManifest Manifest = ChartManifest.Create(Overview, Inventory);

    internal static Sale[] Sales() =>
    [
        .. Enumerable.Range(0, 6).SelectMany(m => Regions.Select((r, i) =>
            new Sale(new DateOnly(2024, 1, 1).AddMonths(m), r, 1000m + 150m * m + 220m * i))),
    ];

    internal static Visit[] Visits() => [new("search", 5200), new("direct", 3100), new("social", 1800)];

    internal static Stock[] Stocks() => [new("Alpha", 420), new("Beta", 310), new("Gamma", 95)];
}
