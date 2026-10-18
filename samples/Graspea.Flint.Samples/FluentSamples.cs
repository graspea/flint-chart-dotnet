namespace Graspea.Flint.Samples;

/// <summary>Samples built with the fluent API: one per chart category, plus a static series, a theme and assemble options.</summary>
internal static class FluentSamples
{
    internal static IEnumerable<(string Name, ChartAssemblyInput Input)> All()
    {
        yield return ("line-temperature", Chart.Line(SampleData.Temperatures())
            .X(r => r.Month, SemanticType.YearMonth)
            .Y(r => r.TemperatureC)
            .Color(r => r.City, SemanticType.City)
            .Annotate(r => r.TemperatureC, new SemanticAnnotation { Type = SemanticType.Temperature, Unit = "°C" })
            .DisplayName(r => r.TemperatureC, "Mean temperature")
            .Interpolate(LineChartInterpolate.Monotone)
            .ShowPoints(true)
            .Title("Monthly mean temperature")
            .Subtitle("Three cities, 2024")
            .Build());

        yield return ("bar-sales-by-region", Chart.Bar(SampleData.RegionRevenue())
            .X(r => r.Region, SemanticType.Region)
            .Y(r => r.Revenue)
            .Annotate(r => r.Revenue, new SemanticAnnotation { Type = SemanticType.Amount, Unit = "USD" })
            .Encoding(Channel.X, e => e.SortBy = "y")
            .Encoding(Channel.X, e => e.SortOrder = SortOrder.Descending)
            .Title("Revenue by region")
            .Build());

        yield return ("stacked-bar-sales-by-quarter", Chart.StackedBar(SampleData.QuarterlyUnits())
            .X(r => r.Quarter, SemanticType.YearQuarter)
            .Y(r => r.Units, SemanticType.Quantity)
            .Color(r => r.Product, SemanticType.Category)
            .Title("Units sold per quarter")
            .Build());

        yield return ("grouped-bar-population", Chart.GroupedBar(SampleData.Populations())
            .X(r => r.Country, SemanticType.Country)
            .Y(r => r.PopulationMillions, SemanticType.Quantity)
            .Group(r => r.Census, SemanticType.Category)
            .DisplayName(r => r.PopulationMillions, "Population (millions)")
            .Title("Population by census year")
            .Build());

        yield return ("area-energy-mix", Chart.Area(SampleData.Energy())
            .X(r => r.Month, SemanticType.YearMonth)
            .Y(r => r.Gwh, SemanticType.Quantity)
            .Color(r => r.Source, SemanticType.Category)
            .DisplayName(r => r.Gwh, "Generation (GWh)")
            .Title("Electricity generation by source")
            .Build());

        yield return ("scatter-cities", Chart.Scatter(SampleData.Cities())
            .X(r => r.AreaKm2, SemanticType.Quantity)
            .Y(r => r.PopulationMillions, SemanticType.Quantity)
            .Color(r => r.Continent, SemanticType.Category)
            .DisplayName(r => r.AreaKm2, "Area (km²)")
            .DisplayName(r => r.PopulationMillions, "Population (millions)")
            .Title("City area and population")
            .Build());

        yield return ("bubble-cities", Chart.Bubble(SampleData.Cities())
            .X(r => r.AreaKm2, SemanticType.Quantity)
            .Y(r => r.DensityPerKm2, SemanticType.Quantity)
            .Size(r => r.PopulationMillions, SemanticType.Quantity)
            .Color(r => r.Continent, SemanticType.Category)
            .Title("City density by area, sized by population")
            .Build());

        yield return ("pie-sales-channels", Chart.Pie(SampleData.Channels())
            .Size(r => r.Sales, SemanticType.Amount)
            .Color(r => r.Channel, SemanticType.Category)
            .SortSlices(PieChartSortSlices.Descending)
            .Title("Sales by channel")
            .Build());

        yield return ("donut-device-share", Chart.Donut(SampleData.Devices())
            .Size(r => r.Sessions, SemanticType.Count)
            .Color(r => r.Device, SemanticType.Category)
            .InnerRadius(50)
            .Title("Sessions by device")
            .Build());

        yield return ("heatmap-visits", Chart.Heatmap(SampleData.Visits())
            .X(r => r.Hour, SemanticType.Hour)
            .Y(r => r.Weekday, SemanticType.Day)
            .Color(r => r.Visits, SemanticType.Count)
            .Title("Store visits by weekday and hour")
            .Build());

        yield return ("calendar-rainfall", Chart.CalendarHeatmap(SampleData.Rainfall())
            .X(r => r.Date, SemanticType.Date)
            .Color(r => r.RainfallMm, SemanticType.Quantity)
            .DisplayName(r => r.RainfallMm, "Rainfall (mm)")
            .Title("Daily rainfall, 2024")
            .Build());

        yield return ("histogram-temperatures", Chart.Histogram(SampleData.Readings())
            .X(r => r.TemperatureC, SemanticType.Temperature)
            .BinCount(20)
            .Title("Distribution of station temperatures")
            .Build());

        yield return ("boxplot-temperatures", Chart.Boxplot(SampleData.Readings())
            .X(r => r.Month, SemanticType.Month)
            .Y(r => r.TemperatureC, SemanticType.Temperature)
            .WhiskerMethod(BoxplotWhiskerMethod.Iqr)
            .Title("Temperature spread by month")
            .Build());

        yield return ("density-temperatures", Chart.Density(SampleData.Readings())
            .X(r => r.TemperatureC, SemanticType.Temperature)
            .Color(r => r.Station, SemanticType.Category)
            .Title("Temperature density by station")
            .Build());

        yield return ("funnel-checkout", Chart.Funnel(SampleData.Checkout())
            .Y(r => r.Stage, SemanticType.Category)
            .Size(r => r.Visitors, SemanticType.Count)
            .Title("Checkout funnel")
            .Build());

        yield return ("bullet-sales-targets", Chart.Bullet(SampleData.Targets())
            .Y(r => r.Product, SemanticType.Category)
            .X(r => r.Sales, SemanticType.Quantity)
            .Goal(r => r.Target, SemanticType.Quantity)
            .Title("Sales against target")
            .Build());

        yield return ("kpi-monthly-revenue", Chart.KpiCard(SampleData.Kpis())
            .Metric(r => r.Metric, SemanticType.Category)
            .Value(r => r.Value, SemanticType.Quantity)
            .Goal(r => r.Goal, SemanticType.Quantity)
            .Build());

        // A static series binds several fields to one channel; flint-chart folds them into series. Excel throws on
        // every static series, so this sample uses a template that has no Excel backend.
        yield return ("static-series-energy", Chart.Streamgraph(SampleData.EnergyWide())
            .X(r => r.Month, SemanticType.YearMonth)
            .Series(Channel.Y, r => r.Solar, r => r.Wind, r => r.Hydro)
            .Annotate(r => r.Solar, SemanticType.Quantity)
            .Annotate(r => r.Wind, SemanticType.Quantity)
            .Annotate(r => r.Hydro, SemanticType.Quantity)
            .Title("Renewable generation (GWh)")
            .Build());

        yield return ("themed-line", Chart.Line(SampleData.Temperatures())
            .X(r => r.Month, SemanticType.YearMonth)
            .Y(r => r.TemperatureC, SemanticType.Temperature)
            .Color(r => r.City, SemanticType.City)
            .Theme(ThemePreset.Economist)
            .Title("Monthly mean temperature")
            .BaseSize(640, 360)
            .Build());

        yield return ("options-bar", Chart.Bar(SampleData.RegionRevenue())
            .X(r => r.Region, SemanticType.Region)
            .Y(r => r.Revenue, SemanticType.Amount)
            .Options(o =>
            {
                o.AddTooltips = true;
                o.MaxStretch = 1.5;
                o.BaseTitleFontSize = 16;
                o.BaseLabelFontSize = 11;
            })
            .CanvasSize(720, 420)
            .Title("Revenue by region")
            .Build());
    }
}
