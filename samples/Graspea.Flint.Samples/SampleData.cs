namespace Graspea.Flint.Samples;

// Synthetic, deterministic data: no randomness, so samples/out is identical on every run and machine.

public sealed record MonthlyTemperature(DateOnly Month, string City, double TemperatureC);

public sealed record RegionSales(string Region, decimal Revenue);

public sealed record QuarterUnits(string Quarter, string Product, int Units);

public sealed record CountryPopulation(string Country, string Census, double PopulationMillions);

public sealed record EnergyOutput(DateOnly Month, string Source, double Gwh);

public sealed record CityStats(string City, string Continent, double AreaKm2, double PopulationMillions, double DensityPerKm2);

public sealed record ChannelSales(string Channel, decimal Sales);

public sealed record DeviceSessions(string Device, int Sessions);

public sealed record HourlyVisits(string Weekday, int Hour, int Visits);

public sealed record DailyRainfall(DateOnly Date, double RainfallMm);

public sealed record StationReading(string Station, string Month, double TemperatureC);

public sealed record FunnelStage(string Stage, int Visitors);

public sealed record SalesTarget(string Product, decimal Sales, decimal Target);

public sealed record Kpi(string Metric, double Value, double Goal);

public sealed record EnergyMix(DateOnly Month, double Solar, double Wind, double Hydro);

internal static class SampleData
{
    internal static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static DateOnly MonthStart(int index) => new DateOnly(2024, 1, 1).AddMonths(index);

    /// <summary>A smooth seasonal curve: <paramref name="mean"/> plus <paramref name="amplitude"/> peaking in July.</summary>
    private static double Seasonal(int month, double mean, double amplitude) =>
        Math.Round(mean + amplitude * Math.Cos((month - 6) * Math.PI / 6), 1);

    /// <summary>A repeatable wobble in [-1, 1].</summary>
    private static double Wobble(int i) => Math.Sin(i * 12.9898) * 0.5 + Math.Sin(i * 4.1414) * 0.5;

    internal static MonthlyTemperature[] Temperatures() =>
    [
        .. Enumerable.Range(0, 12).SelectMany(m => new[]
        {
            new MonthlyTemperature(MonthStart(m), "Oslo", Seasonal(m, 6.5, 10.5)),
            new MonthlyTemperature(MonthStart(m), "Madrid", Seasonal(m, 15.0, 9.5)),
            new MonthlyTemperature(MonthStart(m), "Cairo", Seasonal(m, 22.5, 6.5)),
        }),
    ];

    internal static RegionSales[] RegionRevenue() =>
    [
        new("North", 412_300.00m),
        new("South", 389_950.50m),
        new("East", 275_120.25m),
        new("West", 501_479.00m),
        new("Central", 198_640.75m),
    ];

    internal static QuarterUnits[] QuarterlyUnits()
    {
        string[] products = ["Laptops", "Tablets", "Phones"];
        return
        [
            .. Enumerable.Range(1, 4).SelectMany(q => products.Select((p, i) =>
                new QuarterUnits($"Q{q} 2024", p, 1200 + 150 * q + 400 * i + (int)(90 * Wobble(q * 3 + i))))),
        ];
    }

    internal static CountryPopulation[] Populations() =>
    [
        new("Brazil", "2000", 175.9), new("Brazil", "2010", 196.4), new("Brazil", "2020", 213.2),
        new("Japan", "2000", 126.8), new("Japan", "2010", 128.1), new("Japan", "2020", 126.3),
        new("Mexico", "2000", 98.9), new("Mexico", "2010", 114.1), new("Mexico", "2020", 125.9),
        new("Nigeria", "2000", 122.9), new("Nigeria", "2010", 160.9), new("Nigeria", "2020", 208.3),
    ];

    internal static EnergyOutput[] Energy() =>
    [
        .. Enumerable.Range(0, 12).SelectMany(m => new[]
        {
            new EnergyOutput(MonthStart(m), "Solar", Seasonal(m, 310, 190)),
            new EnergyOutput(MonthStart(m), "Wind", Seasonal(m, 520, -140)),
            new EnergyOutput(MonthStart(m), "Hydro", Seasonal(m, 430, 60)),
        }),
    ];

    internal static EnergyMix[] EnergyWide() =>
    [
        .. Enumerable.Range(0, 12).Select(m => new EnergyMix(MonthStart(m), Seasonal(m, 310, 190), Seasonal(m, 520, -140), Seasonal(m, 430, 60))),
    ];

    internal static CityStats[] Cities()
    {
        (string City, string Continent, double Area, double Population)[] rows =
        [
            ("Lagos", "Africa", 1171, 15.4), ("Cairo", "Africa", 3085, 10.1), ("Nairobi", "Africa", 696, 4.4),
            ("Tokyo", "Asia", 2194, 14.0), ("Delhi", "Asia", 1484, 16.8), ("Jakarta", "Asia", 662, 10.6),
            ("Paris", "Europe", 105, 2.1), ("Berlin", "Europe", 891, 3.7), ("Madrid", "Europe", 604, 3.3),
            ("Lima", "South America", 2672, 10.0), ("Bogota", "South America", 1587, 7.9), ("Santiago", "South America", 641, 6.3),
            ("Toronto", "North America", 630, 2.8), ("Chicago", "North America", 606, 2.7), ("Houston", "North America", 1651, 2.3),
        ];
        return [.. rows.Select(r => new CityStats(r.City, r.Continent, r.Area, r.Population, Math.Round(r.Population * 1_000_000 / r.Area)))];
    }

    internal static ChannelSales[] Channels() =>
    [
        new("Online", 48_200m), new("Retail stores", 31_750m), new("Wholesale", 12_400m), new("Marketplace", 7_650m),
    ];

    internal static DeviceSessions[] Devices() =>
    [
        new("Mobile", 61_250), new("Desktop", 29_870), new("Tablet", 6_480), new("Other", 1_120),
    ];

    internal static HourlyVisits[] Visits()
    {
        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        return
        [
            .. days.SelectMany((d, di) => Enumerable.Range(8, 12).Select(h =>
                new HourlyVisits(d, h, (int)(120 + 80 * Math.Sin((h - 8) * Math.PI / 11) + (di >= 5 ? -40 : 15) + 10 * Wobble(di * 24 + h))))),
        ];
    }

    internal static DailyRainfall[] Rainfall()
    {
        var start = new DateOnly(2024, 1, 1);
        return
        [
            .. Enumerable.Range(0, 366).Select(d =>
            {
                var w = Wobble(d);
                return new DailyRainfall(start.AddDays(d), w > 0.2 ? Math.Round(w * 18 + 2 * Math.Cos(d * Math.PI / 183), 1) : 0);
            }),
        ];
    }

    internal static StationReading[] Readings()
    {
        string[] stations = ["Coastal", "Inland"];
        return
        [
            .. stations.SelectMany((s, si) => Enumerable.Range(0, 12).SelectMany(m => Enumerable.Range(0, 8).Select(k =>
                new StationReading(s, MonthNames[m], Math.Round(Seasonal(m, si == 0 ? 14 : 11, si == 0 ? 6 : 11) + 3.5 * Wobble(si * 1000 + m * 31 + k), 1))))),
        ];
    }

    internal static FunnelStage[] Checkout() =>
    [
        new("Visited product page", 18_400), new("Added to cart", 6_920), new("Started checkout", 3_310),
        new("Entered payment", 2_480), new("Completed order", 2_105),
    ];

    internal static SalesTarget[] Targets() =>
    [
        new("Coffee", 284m, 300m), new("Tea", 192m, 180m), new("Juice", 141m, 160m), new("Water", 233m, 220m),
    ];

    internal static Kpi[] Kpis() =>
    [
        new("Monthly revenue (k)", 1284, 1200),
    ];
}
