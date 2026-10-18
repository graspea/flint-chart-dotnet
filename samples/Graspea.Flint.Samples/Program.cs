// Writes one Flint document per sample to samples/out (or to the directory given as the first argument). Every sample
// must validate without an Error or a Warning on every backend that has its template; the contract-node tests then
// assemble each file with the pinned flint-chart (design part 3 §10.2).
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

return failures == 0 ? 0 : 1;

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
