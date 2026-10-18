using System.Diagnostics;
using System.Text;
using Graspea.Flint.CSharp.Tests.Support;
using Graspea.Flint.Tool;

namespace Graspea.Flint.CSharp.Tests.Tool;

public sealed class ToolCommandTests
{
    private const string Spec = "{\"data\":{\"values\":[{\"month\":\"2024-01-01\",\"revenue\":1200.50}]},\"chart_spec\":{\"chartType\":\"Line Chart\",\"encodings\":{\"x\":\"month\",\"y\":\"revenue\"}}}";

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "flint-cs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    private sealed record Run(int ExitCode, string Output, string Error);

    private static Run InvokeWith(byte[]? input, params string[] args)
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();
        var code = ToolCommand.Run(args, new MemoryStream(input ?? []), output, error, "1.2.3.4");
        return new Run(code, Encoding.UTF8.GetString(output.ToArray()), error.ToString().Replace("\r\n", "\n"));
    }

    private static Run Invoke(params string[] args) => InvokeWith(null, args);

    private string File(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    // source: design §12.5.1 (flint-cs --version) and §12.5.4 (every run prints the preview notice to standard error until
    // the contract is declared stable, D7.11)
    [Test]
    public void Version_PrintsTheVersionAndThePreviewNotice()
    {
        var run = Invoke("--version");

        Assert.That(run, Is.EqualTo(new Run(0, "1.2.3.4\n", "flint-cs is in preview; its options and output may change in any release.\n")));
    }

    // source: design §12.5.1 (flint-cs --help | to-csharp --help)
    [TestCase("--help")]
    [TestCase("to-csharp", "--help")]
    public void Help_PrintsTheUsage(params string[] args)
    {
        var run = Invoke(args);

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(run.Output, Does.StartWith("Usage:\n  flint-cs to-csharp <file.json | -> [--style fluent|model] [--data inline|external|parameter]\n"));
    }

    // source: design §12.5.4 (exit code 2: unknown verb or option, invalid --class or --namespace, --data external without
    // --out; nothing is written)
    [TestCase(new string[0], "a verb is required.")]
    [TestCase(new[] { "convert", "a.json" }, "unknown verb or option \"convert\".")]
    [TestCase(new[] { "to-csharp", "a.json", "--verbose" }, "unknown option \"--verbose\".")]
    [TestCase(new[] { "to-csharp", "a.json", "--style" }, "--style needs a value.")]
    [TestCase(new[] { "to-csharp", "a.json", "--style", "pretty" }, "--style must be fluent or model, not \"pretty\".")]
    [TestCase(new[] { "to-csharp", "a.json", "--data", "lazy" }, "--data must be inline, external or parameter, not \"lazy\".")]
    [TestCase(new[] { "to-csharp", "a.json", "--class", "A", "--class", "B" }, "--class is given more than once.")]
    [TestCase(new[] { "to-csharp", "a.json", "b.json" }, "only one input is allowed; \"b.json\" is a second one.")]
    [TestCase(new[] { "to-csharp" }, "an input file, or - for standard input, is required.")]
    [TestCase(new[] { "to-csharp", "a.json", "--class", "1a" }, "--class \"1a\" is not a C# identifier.")]
    [TestCase(new[] { "to-csharp", "a.json", "--class", "Row" }, "--class \"Row\" is the name of a member the class declares; choose another name.")]
    [TestCase(new[] { "to-csharp", "a.json", "--class", "Chart" }, "--class \"Chart\" is the name of a type the output uses; choose another name.")]
    [TestCase(new[] { "to-csharp", "a.json", "--class", "FlintJson" }, "--class \"FlintJson\" is the name of a type the output uses; choose another name.")]
    [TestCase(new[] { "to-csharp", "a.json", "--namespace", "A..B" }, "--namespace \"A..B\" is not a dotted C# identifier.")]
    [TestCase(new[] { "to-csharp", "a.json", "--data", "external" }, "--data external writes a sidecar file next to the output and needs --out.")]
    public void UsageError_ExitsWith2(string[] args, string message)
    {
        var run = Invoke(args);

        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain($"flint-cs: error: {message}\nRun flint-cs --help for the usage.\n"));
    }

    // source: design §12.5.4 (exit code 1: unreadable file, invalid UTF-8, invalid JSON, duplicate key, or a document
    // FlintJson.Deserialize rejects, whose message is printed; output is written only on exit code 0)
    [Test]
    public void InputErrors_ExitWith1AndWriteNothing()
    {
        var output = Path.Combine(_folder, "Out.cs");
        Assert.Multiple(() =>
        {
            Assert.That(Invoke("to-csharp", Path.Combine(_folder, "missing.json")).ExitCode, Is.EqualTo(1));
            Assert.That(InvokeWith([0x7B, 0xFF, 0x7D], "to-csharp", "-").Error, Does.Contain("flint-cs: error: standard input: the input is not valid UTF-8."));
            Assert.That(Convert(File("bad.json", "{\n  \"a\": tru\n}"), "--out", output).ExitCode, Is.EqualTo(1));
            Assert.That(Invoke("to-csharp", File("dup.json", "{\"a\":1,\n\"a\":2}")).Error, Does.Contain("dup.json(2,1): Duplicate key \"a\"."));
            Assert.That(Invoke("to-csharp", File("shape.json", "{\"chart_spec\":1}")).Error, Does.Contain("shape.json: not a ChartAssemblyInput: "));
            Assert.That(Invoke("to-csharp", File("shape.json", "{\"chart_spec\":1}")).ExitCode, Is.EqualTo(1));
        });
        Assert.That(System.IO.File.Exists(output), Is.False);
    }

    private static Run Convert(string path, params string[] args) => InvokeWith(null, ["to-csharp", path, .. args]);

    // source: design §12.5.1 (- reads standard input; UTF-8 with or without a BOM; the class is FlintChart for standard input)
    [Test]
    public void StandardInput_WithABom_IsRead()
    {
        var run = InvokeWith([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Spec)], "to-csharp", "-");

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(run.Output, Does.StartWith("// Generated by flint-cs 1.2.3.4 from standard input; requires Graspea.Flint 1.2.3.4.\n"));
        Assert.That(run.Output, Does.Contain("public static class FlintChart\n"));
    }

    // source: design §12.5.1 (--class default from the file name) and §12.5.3 (determinism: two runs give the same bytes)
    [Test]
    public void File_TwoRuns_AreByteIdentical()
    {
        var path = File("revenue.flint.json", Spec);

        var first = Convert(path);
        Assert.That(first.ExitCode, Is.EqualTo(0));
        Assert.That(first.Output, Does.Contain("public static class RevenueChart\n"));
        Assert.That(Convert(path).Output, Is.EqualTo(first.Output));
    }

    // source: design §12.5.1 (Flint before a reserved class name) and docs/versioning.md (the output compiles with C# 12):
    // chart.flint.json would give the class Chart, which hides Graspea.Flint.Chart inside it
    [TestCase("fluent")]
    [TestCase("model")]
    public void File_NamedChart_GivesAClassThatCompiles(string style)
    {
        var run = Convert(File("chart.flint.json", Spec), "--style", style);

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(run.Output, Does.Contain("public static class FlintChart\n"));
        Assert.That(Net8Compiler.Problems(Net8Compiler.Compile("ChartFile", [("chart.cs", run.Output)])), Is.Empty);
    }

    // source: design §12.5.4 (--out goes to a temporary file in the same folder that then replaces the target)
    [Test]
    public void Out_ReplacesTheTargetAndLeavesNoTemporaryFile()
    {
        var output = File("RevenueChart.cs", "old");

        var run = Convert(File("revenue.flint.json", Spec), "--out", output, "--namespace", "Charts");

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(run.Output, Is.Empty);
        Assert.That(System.IO.File.ReadAllText(output), Does.Contain("namespace Charts;\n"));
        Assert.That(Directory.GetFiles(_folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "RevenueChart.cs", "revenue.flint.json" }));
    }

    // source: design §12.5.2 (external: data.values written byte for byte to <out stem>.data.json)
    [Test]
    public void External_WritesTheSidecar()
    {
        var spec = "{\"data\":{\"values\":  [ {\"a\" : 1.50} ]},\"chart_spec\":{\"chartType\":\"Bar Chart\",\"encodings\":{\"x\":\"a\"}}}";

        var run = Convert(File("bars.json", spec), "--data", "external", "--out", Path.Combine(_folder, "BarsChart.cs"));

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_folder, "BarsChart.data.json")), Is.EqualTo("[ {\"a\" : 1.50} ]"));
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_folder, "BarsChart.cs")), Does.Contain("public static ChartAssemblyInput Build(JsonArray values) =>"));
    }

    // source: design §12.5.4 (catalog findings in the MSBuild canonical form with the category warning, the code and the
    // validator severity in the message; they never change the exit code; the catalog checks of §12.6.7 run as in the
    // generator) and §12.6.7 (FLINT110 unknown_chart_type at the chartType value, FLINT140 for an unknown key)
    [Test]
    public void CatalogFindings_AreWarningsAndExitWith0()
    {
        var path = File("image.flint.json", "{\"chart_spec\":{\"chartType\":\"Image Chart\",\n\"encodings\":{\"x\":\"a\"}},\n\"zz\":1}");

        var run = Convert(path);

        Assert.That(run.ExitCode, Is.EqualTo(0));
        Assert.That(run.Error, Does.Contain($"{path}(1,28): warning FLINT110: unknown_chart_type (Error): \"Image Chart\" is not a chart type of the pinned flint-chart; every backend throws.{Environment.NewLine}"));
        Assert.That(run.Error, Does.Contain($"{path}(3,1): warning FLINT140: unknown_key (Info): \"zz\" is not modelled by Graspea.Flint and is passed through Extensions.{Environment.NewLine}"));
        Assert.That(run.Error, Does.Contain("flint-cs: info: The field \"a\" has no values and no type hint; its Row member is a string."));
    }

    // source: design §12.10 Tool CLI (process level: the packed command runs, reads standard input and exits with the
    // documented codes)
    [Test]
    public void Process_RunsTheBuiltTool()
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(TestContext.CurrentContext.TestDirectory)!);
        var tool = Repository.Path("src", "Graspea.Flint.Tool", "bin", configuration, "net8.0", "flint-cs.dll");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { tool, "to-csharp", "-", "--style", "model" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Write(Spec);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), error);
        Assert.That(error, Does.StartWith("flint-cs is in preview;"));
        Assert.That(output, Does.Contain("            Data = ChartData.FromRows(Rows),\n"));
    }
}
