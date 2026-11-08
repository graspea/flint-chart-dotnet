// Writes one Flint document per sample to samples/out (or to the directory given as the first argument). Every sample
// must validate without an Error or a Warning on every backend that has its template; the contract-node tests then
// assemble each file with the pinned flint-chart (design part 3 §10.2).
using Graspea.Flint.Delivery;
using System.Text;
using Graspea.Flint.Samples;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

var outDir = args.Length > 0 ? args[0] : Path.Combine(RepositoryRoot(), "samples", "out");
Directory.CreateDirectory(outDir);
foreach (var stale in Directory.GetFiles(outDir, "*.json"))
{
    File.Delete(stale);
}

var failures = 0;
foreach (var (name, input) in FluentSamples.All().Concat(ModelSamples.All()))
{
    var blocking = ChartValidator.Validate(input).Issues.Where(i => i.Severity != IssueSeverity.Info).ToArray();
    foreach (var issue in blocking)
    {
        Console.Error.WriteLine($"{name}: {issue.Severity} {issue.Code} at {issue.Path}: {issue.Message}");
    }

    failures += blocking.Length;
    File.WriteAllText(Path.Combine(outDir, name + ".json"), FlintJson.Serialize(input, indented: true) + "\n");
    Console.WriteLine($"{name}.json  {input.ChartSpec.ChartType}");
}

failures += WriteDelivery(Path.Combine(outDir, "delivery"));

return failures == 0 ? 0 : 1;

// Browser data delivery (docs/delivery.md): the exact wire bytes of a manifest, two pages of one sequence, a withheld
// page, and the document each chart gets once all rows are bound.
static int WriteDelivery(string dir)
{
    Directory.CreateDirectory(dir);
    foreach (var stale in Directory.GetFiles(dir, "*.json"))
    {
        File.Delete(stale);
    }

    var failed = 0;
    void Report(string where, IEnumerable<ValidationIssue> issues)
    {
        foreach (var issue in issues.Where(i => i.Severity != IssueSeverity.Info || i.Code.StartsWith("fixed_", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"{where}: {issue.Severity} {issue.Code} at {issue.Path}: {issue.Message}");
            failed++;
        }
    }

    void Write(string file, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(dir, file), bytes);
        Console.WriteLine($"delivery/{file}");
    }

    var manifest = DeliverySamples.Manifest;
    foreach (var group in manifest.Groups)
    {
        Report("group " + group.Name, group.Validate().Issues);
    }

    var sales = DeliverySamples.Sales();
    var visits = DeliverySamples.Visits();
    var stocks = DeliverySamples.Stocks();
    void Bind(string id, Graspea.Flint.ChartAssemblyInput input)
    {
        Report("bind-" + id, ChartValidator.Validate(input).Issues);
        Write($"bind-{id}.json", Encoding.UTF8.GetBytes(FlintJson.Serialize(input, indented: true) + "\n"));
    }

    Bind(DeliverySamples.Revenue.Id, DeliverySamples.Revenue.Template.Bind(sales));
    Bind(DeliverySamples.Traffic.Id, DeliverySamples.Traffic.Template.Bind(visits));
    Bind(DeliverySamples.StockLevels.Id, DeliverySamples.StockLevels.Template.Bind(stocks));

    Write("manifest.json", manifest.Utf8Bytes.ToArray());
    var overview = DeliverySamples.Overview;
    var half = sales.Length / 2;
    Write("overview.json", new DataPage(overview, [DeliverySamples.Revenue.Rows(sales[..half]), DeliverySamples.Traffic.Rows(visits)], next: "p2").ToUtf8Bytes());
    Write("overview-2.json", new DataPage(overview, [DeliverySamples.Revenue.Rows(sales[half..]), DeliverySamples.Traffic.Rows([])]).ToUtf8Bytes());
    Write("overview-withheld.json", new DataPage(overview, [DeliverySamples.Revenue.Rows(sales), DeliverySamples.Traffic.Withheld("restricted")]).ToUtf8Bytes());
    return failed;
}

static string RepositoryRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Graspea.Flint.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run from inside the repository, or pass the output directory as the first argument.");
}
