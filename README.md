# Flint.NET: unofficial .NET SDK for Flint (flint-chart)

[![NuGet](https://img.shields.io/nuget/vpre/Graspea.Flint)](https://www.nuget.org/packages/Graspea.Flint)
<!-- flint:generated:upstream-badge start -->
[![flint-chart](https://img.shields.io/badge/flint--chart-0.5.1-blue)](https://github.com/microsoft/flint-chart/releases/tag/0.5.1)
[![CI](https://github.com/graspea/flint-chart-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/graspea/flint-chart-dotnet/actions/workflows/ci.yml)
<!-- flint:generated:upstream-badge end -->

Type-safe C# model and fluent builder for [Flint](https://github.com/microsoft/flint-chart) (`flint-chart`),
Microsoft's visualization language for AI agents. Flint.NET builds semantic chart specs in .NET and
emits Flint `ChartAssemblyInput` JSON; `flint-chart` then compiles that JSON to Vega-Lite, ECharts,
Chart.js, Plotly or Excel charts. This package emits JSON only and does not render charts. It is an
independent project, not affiliated with or endorsed by Microsoft.

    dotnet add package Graspea.Flint --prerelease

<!-- flint:generated:upstream-version start -->
Flint.NET 0.5.1.x tracks flint-chart 0.5.1; the first three version parts always equal the pinned upstream release.
<!-- flint:generated:upstream-version end -->

## What it is / what it is not

It is:

- a typed C# model of the Flint `ChartAssemblyInput` document, plus one generated fluent builder per flint-chart template (`Chart.Line(rows).X(...).Y(...)`);
- a JSON writer and reader on System.Text.Json with no other dependencies, usable with trimming and Native AOT through the `JsonTypeInfo` overloads;
- an explicit validator that reports what the pinned flint-chart version throws on (`Error`), silently drops (`Warning`) or merely flags (`Info`).

It is not:

- a renderer: it does not render charts and produces no image, SVG or HTML;
- a port of flint-chart: it computes no layout and writes no Vega-Lite, ECharts, Chart.js, Plotly or Excel spec, which flint-chart does from the JSON;
- a Microsoft product: it is an unofficial, independent project.

## Install

```
dotnet add package Graspea.Flint --prerelease
```

Only preview versions exist so far, so the `--prerelease` flag is needed. Versions and release notes: [releases](https://github.com/graspea/flint-chart-dotnet/releases). The package targets `net8.0` and runs on .NET 8 and later.

## Quick start

A row type:

```csharp
public sealed record Sale(DateOnly Month, string Region, decimal Revenue);
```

Rows in, Flint JSON out:

```csharp
using Graspea.Flint;

Sale[] sales =
[
    new(new DateOnly(2024, 1, 1), "North", 1200.50m),
    new(new DateOnly(2024, 2, 1), "North", 1340.00m),
    new(new DateOnly(2024, 1, 1), "South", 980.25m),
    new(new DateOnly(2024, 2, 1), "South", 1105.75m),
];

var chart = Chart.Line(sales)
    .X(s => s.Month, SemanticType.YearMonth)
    .Y(s => s.Revenue, SemanticType.Amount)
    .Color(s => s.Region)
    .Title("Monthly revenue");

chart.Validate(FlintBackend.VegaLite).ThrowIfInvalid();
string json = chart.ToJson();
```

`json` holds this document (indented here):

```json
{
  "data": {
    "values": [
      { "month": "2024-01-01", "region": "North", "revenue": 1200.50 },
      { "month": "2024-02-01", "region": "North", "revenue": 1340.00 },
      { "month": "2024-01-01", "region": "South", "revenue": 980.25 },
      { "month": "2024-02-01", "region": "South", "revenue": 1105.75 }
    ]
  },
  "semantic_types": { "month": "YearMonth", "revenue": "Amount" },
  "chart_spec": {
    "chartType": "Line Chart",
    "title": "Monthly revenue",
    "encodings": {
      "x": { "field": "month" },
      "y": { "field": "revenue" },
      "color": { "field": "region" }
    }
  }
}
```

Pass it to `flint-chart` (`assembleVegaLite(input)`, `assembleECharts(input)` and so on in JavaScript) or to the `flint-chart-mcp` server, which compile it for the chosen backend.

### Row field names

`ChartData.FromRows` and the fluent entries write camelCase keys by default (`RowOptions.Default`). `RowOptions { PropertyNamingPolicy = null }` keeps member names verbatim, and `[JsonPropertyName]` on a row member wins over both. A lambda selector (`r => r.StationId`) resolves to the same key the rows are written with; a string field name (`"station_id"`) is used as it is.

```csharp
using System.Text.Json.Serialization;

public sealed record Reading([property: JsonPropertyName("station_id")] string StationId, double TemperatureC);
```

```csharp
Reading[] readings = [new("ST-01", 21.5), new("ST-02", 19.0)];

var camel = ChartData.FromRows(readings);                                                      // station_id, temperatureC
var verbatim = ChartData.FromRows(readings, new RowOptions { PropertyNamingPolicy = null });   // station_id, TemperatureC
var chart = Chart.Bar(readings).X(r => r.StationId).Y(r => r.TemperatureC).Build();            // fields station_id, temperatureC
```

How each .NET type is written (dates, `decimal`, `TimeSpan`, enums, `NaN`): [docs/row-data.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/row-data.md).

## Late-bound, asynchronous and streamed rows

A deferred entry takes the row type instead of rows (`Chart.Line<Sale>()`, or `Chart.Line(SalesJson.Default.Sale)` for
trimming and Native AOT), and `ToTemplate()` ends the chain with an immutable, thread-safe `ChartTemplate<Sale>`. Build it
once, keep it in a static field and bind rows per call:

```csharp
ChartTemplate<Sale> revenue = Chart.Line<Sale>()
    .X(s => s.Month, SemanticType.YearMonth)
    .Y(s => s.Revenue, SemanticType.Amount)
    .Color(s => s.Region)
    .Title("Monthly revenue")
    .ToTemplate();

ChartAssemblyInput now = revenue.Bind(sales);                                       // rows written now
ChartAssemblyInput later = await revenue.BindAsync(LoadSalesAsync(ct), ct);         // an IAsyncEnumerable<Sale>
await FlintJson.SerializeAsync(stream, revenue.BindStream(LoadSalesAsync(ct)), ct); // rows streamed to the output

ValidationResult checks = revenue.Validate(FlintBackend.VegaLite);                  // no rows read: rows_not_inspected (Info)
```

All three write the quick-start document. `RowSource.FromBatches` turns pages of rows into one asynchronous sequence. A
streamed document holds one row at a time, is written once, and only by `FlintJson.SerializeAsync` (or
`FlintJson.Serialize(stream, …)` for a synchronous source). End a database query with `.AsAsyncEnumerable()` before
binding it, and validate a real sample with every rule, `ChartValidator.Validate(revenue.Bind(sample), backend)`, in
tests or at start-up. A spec without `data`, such as a JSON file, is read by `FlintJson.Deserialize` and becomes a template
with `ChartTemplate.Create(spec, typeInfo)`. Memory bounds, cancellation, ASP.NET Core and the failure modes:
[docs/row-data.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/row-data.md#late-bound-asynchronous-and-streamed-rows).

## Two API levels

### Fluent builder

`Chart.<Template>(rows)` returns a builder with one method per channel (`X`, `Y`, `Color`, ...) and per chart property (`Interpolate(LineChartInterpolate.Monotone)`, `ShowPoints(true)`) of that template, generated from flint-chart. The channels and properties are the union over the backends that have the template; `Validate(backend)` reports the ones a backend lacks. Common methods: `Title`, `Subtitle`, `Annotate`, `DisplayName`, `Theme`, `Options`, `BaseSize`, `CanvasSize`, `Encode`, `Encoding`, `Series`, `Property`, and `Build()`, `ToJson()`, `Validate(backend)`. `Chart.Of(chartType, rows)` builds any template, including one the constants do not list.

### Typed model (ChartAssemblyInput)

The builder fills a `ChartAssemblyInput`, which can also be written directly. This writes the same document as the quick start:

```csharp
using Graspea.Flint;
using Graspea.Flint.Serialization;

var input = new ChartAssemblyInput
{
    Data = ChartData.FromRows(sales),
    SemanticTypes = new() { ["month"] = SemanticType.YearMonth, ["revenue"] = SemanticType.Amount },
    ChartSpec = new ChartSpec
    {
        ChartType = ChartType.LineChart,
        Title = "Monthly revenue",
        Encodings = { [Channel.X] = "month", [Channel.Y] = "revenue", [Channel.Color] = "region" },
    },
};

string json = FlintJson.Serialize(input);
```

`FlintJson.Deserialize(json)` reads a document back; keys the model does not know are kept in `Extensions` and written again.

### From a JSON spec (flint-cs)

The `flint-cs` tool (package `Graspea.Flint.Tool`, in preview) turns a `ChartAssemblyInput` JSON document into C# that
builds the same document, in the fluent style or as a typed model, with the rows inline, read from a sidecar file, or
supplied later through a template:

```
dotnet tool install --global Graspea.Flint.Tool --version <the Graspea.Flint version>
flint-cs to-csharp Charts/revenue.flint.json --data parameter --namespace Charts --out RevenueChart.cs
```

A typical agent flow drafts the JSON with `flint-chart-mcp`, checks it there, converts it, and then validates the
C# like any hand-written chart:

```
1. Draft:    flint-chart-mcp list_chart_types / flint://agent-skill  ->  Charts/revenue.flint.json
2. Check:    flint-chart-mcp validate_chart (vegalite, echarts, chartjs) and render_chart for a preview
3. Convert:  dnx Graspea.Flint.Tool@<version> to-csharp Charts/revenue.flint.json --data parameter --out RevenueChart.cs
3b. Or bind: [FlintChart<Sale>("Charts/revenue.flint.json")] public static partial class RevenueChart;  (build reports FLINT1xx)
4. Verify:   RevenueChart.Validate(backend) in a test
```

The output needs C# 12, the default of `net8.0`. Options, data modes, diagnostics and exit codes:
[src/Graspea.Flint.Tool/README.md](https://github.com/graspea/flint-chart-dotnet/blob/main/src/Graspea.Flint.Tool/README.md).

### From a JSON spec at compile time (source generator)

The `Graspea.Flint.Generators` package (in preview) keeps the JSON in the project as the source. On a
`static partial class`, `[FlintChart("Charts/revenue.flint.json")]` generates a `Row` record inferred from the spec,
`Template`, `Build`, `BuildAsync`, `WriteAsync` (streamed) and `Validate`; `[FlintChart<Sale>(…)]` binds the spec's
fields to an existing row type instead, through a `JsonSerializerContext` when one is given. The build reports a spec
that does not match the catalog or the row type as FLINT1xx diagnostics, at their line and column in the JSON file.

```
dotnet add package Graspea.Flint.Generators --version <the Graspea.Flint version>
```

With the generator sample's `Sale(SalesRegion Region, int Units)` and its `JsonSerializerContext` `SalesJson`:

```csharp
[FlintChart<Sale>("Charts/units.flint.json", JsonContext = typeof(SalesJson))]
public static partial class UnitsChart;
```

```csharp
Sale[] rows = [new(SalesRegion.North, 120), new(SalesRegion.South, 80), new(SalesRegion.North, 45)];
ChartAssemblyInput input = UnitsChart.Build(rows);
ValidationResult checks = UnitsChart.Validate(FlintBackend.VegaLite);
```

Every `*.flint.json` file of the project is a spec. The generator needs the compiler of .NET SDK 9.0.300 or Visual
Studio 17.14 (Roslyn 4.14) and C# 12, and its attributes and generated members carry `[Experimental("FLINTEXP001")]`
until a release declares its contract stable: add `<NoWarn>$(NoWarn);FLINTEXP001</NoWarn>` to opt in. Modes, members
and diagnostics:
[src/Graspea.Flint.Generators/README.md](https://github.com/graspea/flint-chart-dotnet/blob/main/src/Graspea.Flint.Generators/README.md).

## Chart catalog

<!-- flint:generated:chart-catalog start -->
flint-chart 0.5.1 declares 48 chart types. Backend ids map to `FlintBackend`: `vegalite` = `FlintBackend.VegaLite`, `echarts` = `FlintBackend.ECharts`, `chartjs` = `FlintBackend.ChartJs`, `plotly` = `FlintBackend.Plotly`, `excel` = `FlintBackend.Excel`. Channels and properties are wire names, the union over the backends of the template; `Validate(backend)` reports one that the target backend does not declare (`unsupported_channel`, `unknown_chart_property`).

| Chart type | Constant and entry | Backends | Channels | Properties |
|---|---|---|---|---|
| Area Chart | `ChartType.AreaChart`, `Chart.Area` | vegalite, echarts, chartjs, plotly, excel | x, y, color, opacity, column, row | independentYAxis, interpolate, opacity, stackMode, xAxisType, yAxisType |
| Bar Chart | `ChartType.BarChart`, `Chart.Bar` | vegalite, echarts, chartjs, plotly, excel | x, y, color, opacity, column, row | cornerRadius, independentYAxis, showValueLabels, xAxisType, yAxisType |
| Bar Table | `ChartType.BarTable`, `Chart.BarTable` | vegalite, plotly | y, x, color, column, row | independentYAxis, maxRows, showPercent |
| Boxplot | `ChartType.Boxplot`, `Chart.Boxplot` | vegalite, echarts, plotly, excel | x, y, color, opacity, column, row | dodge, includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, showOutliers, showPoints, whiskerMethod |
| Bubble Chart | `ChartType.BubbleChart`, `Chart.Bubble` | chartjs | x, y, size, color, opacity, column, row | opacity |
| Bullet Chart | `ChartType.BulletChart`, `Chart.Bullet` | vegalite, echarts, plotly | y, x, goal, color, column, row | independentYAxis |
| Bump Chart | `ChartType.BumpChart`, `Chart.Bump` | vegalite, echarts, chartjs, plotly | x, y, color, detail, column, row | includeZero_x, includeZero_y, independentYAxis, interpolate, logScale_x, logScale_y |
| Calendar Heatmap | `ChartType.CalendarHeatmap`, `Chart.CalendarHeatmap` | vegalite, echarts | x, color | cornerRadius |
| Candlestick Chart | `ChartType.CandlestickChart`, `Chart.Candlestick` | vegalite, echarts, plotly, excel | x, open, high, low, close, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, maWindow, showMA |
| Choropleth | `ChartType.Choropleth`, `Chart.Choropleth` | vegalite, plotly | id, color, detail | region |
| Combo Chart | `ChartType.ComboChart`, `Chart.Combo` | chartjs | x, y, column, row | cornerRadius |
| Connected Scatter Plot | `ChartType.ConnectedScatterPlot`, `Chart.ConnectedScatter` | vegalite, echarts, chartjs, plotly, excel | x, y, order, color, detail, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y |
| Density Contour | `ChartType.DensityContour`, `Chart.DensityContour` | plotly | x, y, column, row | binCount, showPoints |
| Density Plot | `ChartType.DensityPlot`, `Chart.Density` | vegalite, echarts, plotly | x, color, column, row | bandwidth, independentYAxis |
| Donut Chart | `ChartType.DonutChart`, `Chart.Donut` | vegalite, plotly, excel | size, color, column, row, theta | independentYAxis, innerRadius, labelType, showValueLabels, sortSlices |
| Doughnut Chart | `ChartType.DoughnutChart`, `Chart.Doughnut` | chartjs | size, color, column, row | innerRadius, sortSlices |
| ECDF Plot | `ChartType.EcdfPlot`, `Chart.Ecdf` | vegalite, echarts, chartjs, plotly | x, color, detail, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, showPoints |
| Funnel Chart | `ChartType.FunnelChart`, `Chart.Funnel` | echarts, plotly, excel | y, size | gap, orient, sort |
| Gantt Chart | `ChartType.GanttChart`, `Chart.Gantt` | vegalite, echarts, chartjs, plotly | y, x, x2, color, detail, column, row | cornerRadius, includeZero_x, includeZero_y, independentYAxis, intervalLabels, logScale_x, logScale_y, taskHeight |
| Gauge Chart | `ChartType.GaugeChart`, `Chart.Gauge` | echarts, plotly | size, column | max, min, showProgress |
| Grouped Bar Chart | `ChartType.GroupedBarChart`, `Chart.GroupedBar` | vegalite, echarts, chartjs, plotly, excel | x, y, group, column, row, color | dodge, independentYAxis, showValueLabels |
| Heatmap | `ChartType.Heatmap`, `Chart.Heatmap` | vegalite, echarts, plotly | x, y, color, column, row | independentYAxis, showValueLabels, xAxisType, yAxisType |
| Histogram | `ChartType.Histogram`, `Chart.Histogram` | vegalite, echarts, chartjs, plotly, excel | x, color, column, row | binCount, independentYAxis |
| KPI Card | `ChartType.KpiCard`, `Chart.KpiCard` | vegalite, plotly | metric, value, goal | behindThreshold, includeZero_x, includeZero_y, layout, logScale_x, logScale_y, style |
| Line Chart | `ChartType.LineChart`, `Chart.Line` | vegalite, echarts, chartjs, plotly, excel | x, y, color, strokeDash, detail, opacity, column, row | includeZero_x, includeZero_y, independentYAxis, interpolate, logScale_x, logScale_y, showPoints, xAxisType, yAxisType |
| Lollipop Chart | `ChartType.LollipopChart`, `Chart.Lollipop` | vegalite, echarts, chartjs, plotly | x, y, color, column, row | dotSize, independentYAxis, showValueLabels, xAxisType, yAxisType |
| Map | `ChartType.Map`, `Chart.Map` | vegalite, plotly | longitude, latitude, color, size, opacity | includeZero_x, includeZero_y, logScale_x, logScale_y, projection, projectionCenter, region |
| Network Graph | `ChartType.NetworkGraph`, `Chart.NetworkGraph` | echarts | x, y, size | layout |
| Parallel Coordinates | `ChartType.ParallelCoordinates`, `Chart.ParallelCoordinates` | echarts | color, detail | (none) |
| Pie Chart | `ChartType.PieChart`, `Chart.Pie` | vegalite, echarts, chartjs, plotly, excel | size, color, column, row, theta | cornerRadius, independentYAxis, innerRadius, labelType, showValueLabels, sortSlices |
| Pyramid Chart | `ChartType.PyramidChart`, `Chart.Pyramid` | vegalite, echarts, plotly, excel | x, y, color | showValueLabels |
| Radar Chart | `ChartType.RadarChart`, `Chart.Radar` | vegalite, echarts, chartjs, plotly, excel | x, y, color, column, row | fillOpacity, filled, includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, shape, strokeWidth |
| Range Area Chart | `ChartType.RangeAreaChart`, `Chart.RangeArea` | vegalite, echarts, chartjs, plotly | x, y, y2, color, column, row | independentYAxis, interpolate, opacity |
| Ranged Dot Plot | `ChartType.RangedDotPlot`, `Chart.RangedDot` | vegalite, echarts, plotly | x, y, color | includeZero_x, includeZero_y, logScale_x, logScale_y |
| Regression | `ChartType.Regression`, `Chart.Regression` | vegalite, echarts, plotly | x, y, size, color, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, opacity, polyOrder, regressionMethod |
| Rose Chart | `ChartType.RoseChart`, `Chart.Rose` | vegalite, echarts, chartjs, plotly | x, y, color, column, row | alignment, independentYAxis, padAngle, showValueLabels, sortSlices |
| Sankey Diagram | `ChartType.SankeyDiagram`, `Chart.SankeyDiagram` | echarts | x, y, size | nodeGap, nodeWidth, orient |
| Scatter Plot | `ChartType.ScatterPlot`, `Chart.Scatter` | vegalite, echarts, chartjs, plotly, excel | x, y, color, size, shape, opacity, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, opacity |
| Slope Chart | `ChartType.SlopeChart`, `Chart.Slope` | vegalite, echarts, chartjs, plotly | x, y, color, detail, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, showSeriesInLabel, showText |
| Sparkline | `ChartType.Sparkline`, `Chart.Sparkline` | vegalite, plotly | x, y, color, detail, row, column | baseline, includeZero_x, includeZero_y, interpolate, logScale_x, logScale_y, trendWidth |
| Stacked Bar Chart | `ChartType.StackedBarChart`, `Chart.StackedBar` | vegalite, echarts, chartjs, plotly, excel | x, y, color, column, row | independentYAxis, showValueLabels, stackMode |
| Streamgraph | `ChartType.Streamgraph`, `Chart.Streamgraph` | vegalite, echarts, plotly | x, y, color, column, row | independentYAxis, interpolate |
| Strip Plot | `ChartType.StripPlot`, `Chart.Strip` | vegalite, echarts, chartjs, plotly | x, y, color, size, column, row | includeZero_x, includeZero_y, independentYAxis, logScale_x, logScale_y, opacity, pointSize, stepWidth |
| Sunburst Chart | `ChartType.SunburstChart`, `Chart.Sunburst` | echarts, excel | color, size, detail, group | innerRadius, labelRotate |
| Tree | `ChartType.Tree`, `Chart.Tree` | echarts | color, detail, size | orient |
| Treemap | `ChartType.Treemap`, `Chart.Treemap` | echarts, excel | color, size, detail | breadcrumb |
| Violin Plot | `ChartType.ViolinPlot`, `Chart.Violin` | vegalite, plotly | x, y, color, row, column | bandwidth, independentYAxis, medianWidth, showBox, showContour, showMedian, showPoints |
| Waterfall Chart | `ChartType.WaterfallChart`, `Chart.Waterfall` | vegalite, echarts, chartjs, plotly, excel | x, y, color, column, row | cornerRadius, independentYAxis, showTextLabels, showValueLabels, totals |
<!-- flint:generated:chart-catalog end -->

## Semantic types

<!-- flint:generated:semantic-types start -->
flint-chart 0.5.1 registers 44 semantic types. Annotate a field with the `SemanticType` constant of the same name (`SemanticType.YearMonth`); a different constant name is given in brackets.

| Group | Semantic types |
|---|---|
| Categorical / Binned | Range |
| Categorical / Coded | Boolean, Direction, Status |
| Categorical / Entity | Category, Name (`NameType`), Unknown |
| Discrete / Rank | Rank |
| Discrete / Score | Score |
| Geographic / GeoCoordinate | Latitude, Longitude |
| Geographic / GeoPlace | Address, City, Country, Region, State, ZipCode |
| Identifier / ID | ID |
| Measure / Amount | Amount, Price |
| Measure / GenericMeasure | Count, Number |
| Measure / Physical | Quantity, Temperature |
| Measure / Proportion | Percentage |
| Measure / SignedMeasure | Correlation, PercentageChange, Profit, Sentiment |
| Temporal / DateGranule | Day, Decade, Hour, Month, Quarter, Week, Year, YearMonth, YearQuarter, YearWeek |
| Temporal / DateTime | Date, DateTime, Time, Timestamp |
| Temporal / Duration | Duration |
<!-- flint:generated:semantic-types end -->

## Validate against flint-chart

### In-process pre-flight

`Validate(backend)` on a builder, or `ChartValidator.Validate(input, backend)` on a document, checks the document against the pinned flint-chart version without running it. Nothing validates implicitly: `Build()` and `ToJson()` never do.

```csharp
using Graspea.Flint.Validation;

var doughnut = Chart.Doughnut(sales).Size(s => s.Revenue).Color(s => s.Region);

ValidationResult result = doughnut.Validate(FlintBackend.VegaLite);
foreach (ValidationIssue issue in result.Issues)
{
    Console.WriteLine($"{issue.Severity} {issue.Code} at {issue.Path}");
}
// Error chart_type_not_on_backend at chart_spec.chartType

Console.WriteLine(doughnut.Validate(FlintBackend.ChartJs).IsValid); // True
```

`Error` means that the flint-chart assembler for that backend throws, `Warning` that it silently drops or replaces part of the document, and `Info` anything else worth a look. `IsValid` means "no `Error`"; `ThrowIfInvalid()` throws a `FlintValidationException` that carries every issue. Without a backend, the document is checked for every backend that has its template. Every code and the upstream fact behind it: [docs/validation-codes.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/validation-codes.md).

### With the upstream MCP server

The authoritative check is upstream's `flint-chart-mcp` server. Its `validate_chart` tool checks the JSON this package writes (on vegalite, echarts and chartjs), and `render_chart` renders it. This package has no MCP server of its own. Run the server at the version that matches the package, for example from a workspace-root `.mcp.json` (`mcpServers` format; VS Code's `.vscode/mcp.json` uses a top-level `servers` key instead):

<!-- flint:generated:mcp-commands start -->
```json
{
  "mcpServers": {
    "flint-chart": {
      "command": "npx",
      "args": ["-y", "flint-chart-mcp@0.5.1"]
    }
  }
}
```

The hosted endpoint is `https://flint.data-formulator.ai/mcp`. It runs the version upstream has deployed, which can differ from flint-chart 0.5.1.
<!-- flint:generated:mcp-commands end -->

## For AI agents

This section is also packed as `AGENTS.md` at the root of the NuGet package, next to `catalog/catalog.json`.

### Rules

1. Graspea.Flint produces Flint `ChartAssemblyInput` JSON and never renders. There is no Render, Draw or ToSvg method; pass the JSON to `flint-chart`, which compiles it for Vega-Lite, ECharts, Chart.js, Plotly or Excel, or to the `flint-chart-mcp` server, which handles Vega-Lite, ECharts and Chart.js.
2. Transform and aggregate the data in C# before building the chart; style the chart after Flint assembles it.
3. Use the constants (`ChartType.LineChart`, `SemanticType.YearMonth`, `Channel.X`, `ThemePreset.Economist`) and the generated enums (`LineChartInterpolate.Monotone`) for known members, never free-form strings.
4. A template that the constants do not list is not unsupported: use `Chart.Of(new ChartType("..."), rows)` or set `ChartSpec.ChartType` directly; `Validate` reports `unknown_chart_type` for the pinned flint-chart version.
5. `Build()` and `ToJson()` never validate. Call `Validate(backend)` explicitly and read the result.
6. The package version `A.B.C.D` mirrors flint-chart `A.B.C`. Validate and render with that flint-chart version.
7. Build a template once and bind rows per request: `Chart.Line(typeInfo)…ToTemplate()`, then `Bind`, `BindAsync` or `BindStream`. Stream large row sets with `BindStream` and `FlintJson.SerializeAsync`, and end database queries with `.AsAsyncEnumerable()` ([docs/row-data.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/row-data.md)).
8. A `ChartAssemblyInput` JSON document, such as one drafted and checked with `flint-chart-mcp`, becomes C# with the `flint-cs` tool (package `Graspea.Flint.Tool`, at the version of `Graspea.Flint`): `flint-cs to-csharp revenue.flint.json --data parameter --out RevenueChart.cs`. The output builds the same document; do not retype the JSON by hand. The tool is in preview ([src/Graspea.Flint.Tool/README.md](https://github.com/graspea/flint-chart-dotnet/blob/main/src/Graspea.Flint.Tool/README.md)). To keep editing the JSON instead, reference `Graspea.Flint.Generators` and mark a `static partial class` with `[FlintChart("revenue.flint.json")]`: the builder is generated at compile time and the build reports FLINT1xx for a spec that does not fit the catalog ([src/Graspea.Flint.Generators/README.md](https://github.com/graspea/flint-chart-dotnet/blob/main/src/Graspea.Flint.Generators/README.md)).

### Canonical patterns

In the patterns below, `sales` is an `IEnumerable<Sale>` with `record Sale(DateOnly Month, string Region, decimal Revenue)`.

Inline rows, typed model (member names become camelCase keys: `city`, `revenue`):

```csharp
using Graspea.Flint;
using Graspea.Flint.Serialization;

var input = new ChartAssemblyInput
{
    Data = ChartData.FromRows(new[]
    {
        new { City = "Seattle", Revenue = 501479 },
        new { City = "Boston", Revenue = 423117 },
    }),
    ChartSpec = new ChartSpec
    {
        ChartType = ChartType.BarChart,
        Encodings = { [Channel.X] = "city", [Channel.Y] = "revenue" },
    },
};
string json = FlintJson.Serialize(input);
```

Rows from an existing `IEnumerable<T>`, aggregated in C# first, then bound with lambdas (keys are camelCase: `region`, `revenue`) and validated:

```csharp
var byRegion = sales
    .GroupBy(s => s.Region)
    .Select(g => new { Region = g.Key, Revenue = g.Sum(s => s.Revenue) })
    .ToList();

var chart = Chart.Bar(byRegion)
    .X(r => r.Region)
    .Y(r => r.Revenue, SemanticType.Amount);

chart.Validate(FlintBackend.VegaLite).ThrowIfInvalid();
string json = chart.ToJson();
```

A template built once (in a static field) and bound per request, with a real sample validated in a test or at start-up, and rows streamed from an `IAsyncEnumerable<Sale>` to the response:

```csharp
var template = Chart.Bar<Sale>()
    .X(s => s.Region)
    .Y(s => s.Revenue, SemanticType.Amount)
    .ToTemplate();

ChartValidator.Validate(template.Bind(sales), FlintBackend.VegaLite).ThrowIfInvalid();
await FlintJson.SerializeAsync(response, template.BindStream(LoadSalesAsync(ct)), ct);
```

Editing an existing document (keys the model does not know survive in `Extensions`):

```csharp
ChartAssemblyInput spec = FlintJson.Deserialize(json);
spec.ChartSpec.Title = "Revenue by region";
string edited = FlintJson.Serialize(spec);
```

A template that the constants do not list:

```csharp
using Graspea.Flint.Validation;

ChartAssemblyInput input = Chart.Of(new ChartType("Image Chart"), sales)
    .Encode(Channel.X, s => s.Region)
    .Build();
ValidationResult result = ChartValidator.Validate(input); // unknown_chart_type (Error) for the pinned flint-chart
```

### Anti-patterns

- Building Vega-Lite or ECharts options by hand "because Flint is just JSON".
- Expecting `theme_spec` to apply on ECharts, Chart.js or Excel; only Vega-Lite and Plotly apply it (`theme_ignored_by_backend`).
- Aggregating in the chart spec what can be aggregated in C# first.
- Pinning `Graspea.Flint` to a version whose first three parts differ from the flint-chart version that renders the JSON.
- Reading `IsValid` as "the chart looks right": the validator checks what the pinned assemblers throw on or drop, not the layout.
- Retyping a `ChartAssemblyInput` JSON document as C# by hand: convert it with `flint-cs`, or keep it as the source with `[FlintChart]`.
- Returning a streamed document through `Results.Json`, `WriteAsJsonAsync` or `JsonSerializer`: an asynchronous source throws there. Write it to the response body with `FlintJson.SerializeAsync`.
- Referencing `Graspea.Flint.Tool` or `Graspea.Flint.Generators` at a version other than the `Graspea.Flint` version (the generator reports FLINT100).

### How to validate

1. In-process pre-flight: `builder.Validate(backend)` or `ChartValidator.Validate(input, backend)`. `Error` = the pinned upstream assembler throws, `Warning` = it silently drops part of the input, `Info` = worth a look. Every code: https://github.com/graspea/flint-chart-dotnet/blob/main/docs/validation-codes.md
2. Authoritative check: the upstream MCP tool `validate_chart` on the JSON (vegalite, echarts and chartjs); `render_chart` on the same server shows the result. Setup: https://github.com/graspea/flint-chart-dotnet#with-the-upstream-mcp-server

<!-- flint:generated:agent-identifiers start -->
### Identifiers (flint-chart 0.5.1)

- Backends: `FlintBackend.VegaLite`, `FlintBackend.ECharts`, `FlintBackend.ChartJs`, `FlintBackend.Plotly`, `FlintBackend.Excel`.
- Chart types: 48 `ChartType` constants, each with a fluent entry (`ChartType.LineChart` and `Chart.Line(rows)`); see the chart catalog.
- Channels: `Channel.X`, `Channel.Y`, `Channel.X2`, `Channel.Y2`, `Channel.Id`, `Channel.Color`, `Channel.Opacity`, `Channel.Size`, `Channel.Shape`, `Channel.StrokeDash`, `Channel.Column`, `Channel.Row`, `Channel.Latitude`, `Channel.Longitude`, `Channel.Radius`, `Channel.Detail`, `Channel.Group`, `Channel.Open`, `Channel.High`, `Channel.Low`, `Channel.Close`, `Channel.Angle`, `Channel.Order`, `Channel.Metric`, `Channel.Value`, `Channel.Goal`, `Channel.Theta`.
- Semantic types: 44 `SemanticType` constants (`SemanticType.YearMonth`); see the semantic types.
- Theme presets: `ThemePreset.Cartoon`, `ThemePreset.Datawrapper`, `ThemePreset.Economist`, `ThemePreset.Mckinsey`, `ThemePreset.Nature`, `ThemePreset.Nyt`, `ThemePreset.Pop`, `ThemePreset.PowerBi`, `ThemePreset.PowerBiLight`, `ThemePreset.Swiss`.
- Upstream MCP server: `npx -y flint-chart-mcp@0.5.1`, tool `validate_chart` (vegalite, echarts and chartjs).
- Upstream skill for the JSON format: https://github.com/microsoft/flint-chart/blob/0.5.1/agent-skills/flint-chart-author/SKILL.md
- Machine-readable catalog: `catalog/catalog.json` in the package, generated from the same data.
<!-- flint:generated:agent-identifiers end -->

## Version mapping to flint-chart

- The version `A.B.C.D` mirrors flint-chart: `A.B.C` is the flint-chart version the package was generated from and tested against, and `D` counts this project's releases for it, starting at 1.
- Previews carry `-preview.N` on the same four-part version: `0.5.1.1-preview.1`, `0.5.1.1-preview.2`, then `0.5.1.1`.
- The SDK follows flint-chart releases on npm only. Render with the flint-chart version whose number matches the first three parts.

Details, including what changes between releases: [docs/versioning.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/versioning.md).

## Regenerate from a new flint-chart release

The constants, options, theme classes, catalog tables, fluent builders, the generated README sections and the package `AGENTS.md` are generated from a committed description of the pinned flint-chart package, so a new upstream release is a regeneration and a test run, not a rewrite. A scheduled workflow opens a pull request for every new flint-chart release on npm; a maintainer reviews it and tags the release. The generator, its inputs and the manual steps: [docs/generator.md](https://github.com/graspea/flint-chart-dotnet/blob/main/docs/generator.md).

## About Flint

- Upstream repository: [microsoft/flint-chart](https://github.com/microsoft/flint-chart)
- Paper: [arXiv 2607.20775](https://arxiv.org/abs/2607.20775)
- Project site: [microsoft.github.io/flint-chart](https://microsoft.github.io/flint-chart/)
- Microsoft Research post: [Flint: A visualization language for the AI era](https://www.microsoft.com/en-us/research/blog/flint-a-visualization-language-for-the-ai-era/)

## Contributing

Issues and pull requests are welcome. Read [CONTRIBUTING.md](https://github.com/graspea/flint-chart-dotnet/blob/main/CONTRIBUTING.md) first; coding agents also read [AGENTS.md](https://github.com/graspea/flint-chart-dotnet/blob/main/AGENTS.md).

## Security

Report a vulnerability privately, as described in [SECURITY.md](https://github.com/graspea/flint-chart-dotnet/blob/main/SECURITY.md); do not open a public issue.

Every release is built by the release workflow from a maintainer-signed tag and carries Sigstore build provenance for each of its packages (`Graspea.Flint`, `Graspea.Flint.Tool`, `Graspea.Flint.Generators`). Verify the `.nupkg` attached to the [GitHub release](https://github.com/graspea/flint-chart-dotnet/releases), and the nuget.org repository signature of the package you restore (shown for `Graspea.Flint`; the same commands work with the other ids):

```
gh attestation verify Graspea.Flint.<version>.nupkg --repo graspea/flint-chart-dotnet \
  --signer-workflow graspea/flint-chart-dotnet/.github/workflows/release.yml \
  --source-ref refs/tags/v<version> --deny-self-hosted-runners
dotnet nuget verify --all --certificate-fingerprint <nuget.org repository certificate SHA-256> Graspea.Flint.<version>.nupkg
```

nuget.org adds its repository signature on upload, which changes the file, so the attestation matches the file on the GitHub release, not the copy downloaded from nuget.org. [SECURITY.md](https://github.com/graspea/flint-chart-dotnet/blob/main/SECURITY.md#verifying-a-release) shows where the nuget.org certificate fingerprints come from.

## Attribution and license

Flint.NET is licensed under the [MIT License](https://github.com/graspea/flint-chart-dotnet/blob/main/LICENSE), Copyright (c) 2026 GRASPEA s.r.o. flint-chart is licensed under the MIT License, Copyright (c) Microsoft Corporation; the generated code, documentation and test fixtures derive from it ([THIRD-PARTY-NOTICES.md](https://github.com/graspea/flint-chart-dotnet/blob/main/THIRD-PARTY-NOTICES.md)).

Flint.NET is an unofficial, independent project. It is not affiliated with or endorsed by Microsoft. Microsoft is named only to identify the upstream project this package works with.
