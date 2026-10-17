using System.Text.Json.Nodes;

namespace Graspea.Flint.Samples;

/// <summary>Samples built with the low-level model: <see cref="ChartAssemblyInput"/> and its parts, set directly.</summary>
internal static class ModelSamples
{
    internal static IEnumerable<(string Name, ChartAssemblyInput Input)> All()
    {
        yield return ("model-waterfall", new ChartAssemblyInput
        {
            Data = ChartData.FromJson(
            [
                new JsonObject { ["step"] = "Opening balance", ["amount"] = 1200 },
                new JsonObject { ["step"] = "Product sales", ["amount"] = 860 },
                new JsonObject { ["step"] = "Services", ["amount"] = 240 },
                new JsonObject { ["step"] = "Salaries", ["amount"] = -710 },
                new JsonObject { ["step"] = "Rent", ["amount"] = -180 },
                new JsonObject { ["step"] = "Marketing", ["amount"] = -150 },
            ]),
            SemanticTypes = new() { ["step"] = SemanticType.Category, ["amount"] = new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD" } },
            FieldDisplayNames = new() { ["amount"] = "Amount (USD)" },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.WaterfallChart,
                Title = "Quarterly cash flow",
                Encodings = { ["x"] = "step", ["y"] = "amount" },
            },
        });

        yield return ("model-treemap", new ChartAssemblyInput
        {
            Data = ChartData.FromJson(
            [
                new JsonObject { ["category"] = "Groceries", ["spend"] = 420 },
                new JsonObject { ["category"] = "Housing", ["spend"] = 1150 },
                new JsonObject { ["category"] = "Transport", ["spend"] = 260 },
                new JsonObject { ["category"] = "Leisure", ["spend"] = 190 },
                new JsonObject { ["category"] = "Utilities", ["spend"] = 210 },
            ]),
            SemanticTypes = new() { ["category"] = SemanticType.Category, ["spend"] = SemanticType.Amount },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.Treemap,
                Title = "Household spending by category",
                Encodings = { ["color"] = "category", ["size"] = "spend" },
            },
        });

        yield return ("model-candlestick", new ChartAssemblyInput
        {
            Data = ChartData.FromJson(Candles()),
            SemanticTypes = new()
            {
                ["date"] = SemanticType.Date,
                ["open"] = SemanticType.Price,
                ["high"] = SemanticType.Price,
                ["low"] = SemanticType.Price,
                ["close"] = SemanticType.Price,
            },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.CandlestickChart,
                Title = "Daily index level",
                Encodings = { ["x"] = "date", ["open"] = "open", ["high"] = "high", ["low"] = "low", ["close"] = "close" },
            },
        });

        yield return ("model-radar", new ChartAssemblyInput
        {
            Data = ChartData.FromJson(Radar()),
            SemanticTypes = new() { ["product"] = SemanticType.Category, ["criterion"] = SemanticType.Category, ["score"] = SemanticType.Score },
            ChartSpec = new ChartSpec
            {
                ChartType = ChartType.RadarChart,
                Title = "Product review scores",
                Encodings = { ["x"] = "criterion", ["y"] = "score", ["color"] = "product" },
                // Outlined series: Excel cannot fill a radar series translucently and warns unless filling is off.
                ChartProperties = new() { ["filled"] = false },
            },
        });
    }

    private static JsonArray Candles()
    {
        var rows = new JsonArray();
        var close = 100.0;
        for (var d = 0; d < 20; d++)
        {
            var open = close;
            close = Math.Round(open + 3 * Math.Sin(d * 0.9) + 0.8 * Math.Cos(d * 2.3), 2);
            rows.Add(new JsonObject
            {
                ["date"] = new DateOnly(2024, 3, 1).AddDays(d).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["open"] = open,
                ["high"] = Math.Round(Math.Max(open, close) + 1.1 + Math.Abs(Math.Sin(d)), 2),
                ["low"] = Math.Round(Math.Min(open, close) - 0.9 - Math.Abs(Math.Cos(d)), 2),
                ["close"] = close,
            });
        }

        return rows;
    }

    private static JsonArray Radar()
    {
        string[] criteria = ["Quality", "Price", "Design", "Support", "Durability"];
        (string Product, int[] Scores)[] products = [("Model A", [90, 62, 78, 70, 85]), ("Model B", [72, 88, 69, 81, 66])];
        var rows = new JsonArray();
        foreach (var (product, scores) in products)
        {
            for (var i = 0; i < criteria.Length; i++)
            {
                rows.Add(new JsonObject { ["product"] = product, ["criterion"] = criteria[i], ["score"] = scores[i] });
            }
        }

        return rows;
    }
}
