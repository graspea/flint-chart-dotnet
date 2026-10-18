using System.Reflection;
using System.Text;
using System.Text.Json;
using Graspea.Flint.CSharp;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.Tool;

/// <summary>
/// <c>flint-cs to-csharp</c> (design §12.5): reads a <c>ChartAssemblyInput</c> JSON document, checks it with the core
/// reader and with <c>FlintJson.Deserialize</c>, prints the catalog findings, and writes C# that builds the same document.
/// Exit codes: 0 written, 1 input error, 2 usage error. Output is written only on exit code 0.
/// </summary>
internal static class ToolCommand
{
    /// <summary>Printed to standard error on every run while the tool's contract is in preview (design §12.8, D7.11).</summary>
    internal const string PreviewNotice = "flint-cs is in preview; its options and output may change in any release.";

    internal const int Success = 0;
    internal const int InputError = 1;
    internal const int UsageError = 2;

    internal const string Usage = """
        Usage:
          flint-cs to-csharp <file.json | -> [--style fluent|model] [--data inline|external|parameter]
                                             [--namespace <N>] [--class <C>] [--out <file.cs>]
          flint-cs --version
          flint-cs --help | to-csharp --help

        Converts a Flint ChartAssemblyInput JSON document into C# that builds the same document with Graspea.Flint.

        Options:
          <file.json> | -   The input document; - reads standard input (UTF-8, with or without a BOM).
          --style           fluent (default): a chain over the generated builders; model: one ChartAssemblyInput initialiser.
          --data            inline (default): the rows are embedded; external: Build(JsonArray values), the rows are written
                            to <out stem>.data.json (needs --out); parameter: a Row record, a template and
                            Build(IEnumerable<Row>), BuildAsync, WriteAsync and Validate.
          --namespace       A file-scoped namespace for the class (default: the global namespace).
          --class           The class name (default: from the file name, such as RevenueChart for revenue.flint.json).
          --out             The output file, replaced atomically (default: standard output).

        Exit codes: 0 C# written (catalog findings may have been printed), 1 input error, 2 usage error.
        """;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The tool's informational version without build metadata; it is also the Graspea.Flint version it targets.</summary>
    public static string InformationalVersion()
    {
        var version = typeof(ToolCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    public static int Run(string[] args, Stream standardInput, Stream standardOutput, TextWriter standardError, string version)
    {
        standardError.WriteLine(PreviewNotice);
        if (args is ["--version"])
        {
            Write(standardOutput, version + "\n");
            return Success;
        }

        if (args is ["--help"] or ["-h"] or ["to-csharp", "--help"] or ["to-csharp", "-h"])
        {
            Write(standardOutput, Usage.Replace("\r\n", "\n") + "\n");
            return Success;
        }

        if (args is not ["to-csharp", ..])
        {
            return Fail(standardError, UsageError, args.Length == 0 ? "a verb is required." : $"unknown verb or option \"{args[0]}\".");
        }

        if (Parse(args[1..]) is not { } parsed)
        {
            return Fail(standardError, UsageError, ParseError(args[1..]));
        }

        if (Validate(parsed) is { } usage)
        {
            return Fail(standardError, UsageError, usage);
        }

        var display = parsed.Input == "-" ? "standard input" : parsed.Input;
        string json;
        try
        {
            var bytes = parsed.Input == "-" ? ReadAll(standardInput) : File.ReadAllBytes(parsed.Input);
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            json = Utf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Fail(standardError, InputError, $"{display}: the input is not valid UTF-8.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(standardError, InputError, $"cannot read {display}: {e.Message}");
        }

        JsonItem root;
        try
        {
            root = JsonReader.Parse(json);
        }
        catch (JsonReadException e)
        {
            return Fail(standardError, InputError, $"{display}({e.Position.Line},{e.Position.Column}): {e.Message}");
        }

        try
        {
            // The double read of design §12.5.4: the tool never emits C# for a document the library cannot read.
            _ = FlintJson.Deserialize(json);
        }
        catch (JsonException e)
        {
            return Fail(standardError, InputError, $"{display}: not a ChartAssemblyInput: {e.Message}");
        }

        var options = new ConversionOptions(parsed.ClassName ?? CSharpText.ClassNameFor(parsed.Input))
        {
            Style = parsed.Style,
            Data = parsed.Data,
            Namespace = parsed.Namespace,
            SourceName = parsed.Input == "-" ? "standard input" : Path.GetFileName(parsed.Input),
            Version = version,
        };
        var result = SpecConverter.Convert(root, json, options);

        foreach (var finding in CatalogFindings.Of(root, parsed.Input == "-" ? "stdin" : parsed.Input))
        {
            standardError.WriteLine(finding);
        }

        foreach (var note in result.Notes)
        {
            standardError.WriteLine($"flint-cs: info: {note}");
        }

        if (parsed.Out is null)
        {
            Write(standardOutput, result.Code);
            return Success;
        }

        try
        {
            if (result.Sidecar is { } sidecar)
            {
                WriteAtomically(SidecarPath(parsed.Out), sidecar);
            }

            WriteAtomically(parsed.Out, result.Code);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(standardError, InputError, $"cannot write {parsed.Out}: {e.Message}");
        }

        return Success;
    }

    /// <summary><c>&lt;out stem&gt;.data.json</c> beside the output file (design §12.5.2).</summary>
    internal static string SidecarPath(string output)
    {
        var stem = output.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? output[..^3] : output;
        return stem + ".data.json";
    }

    private sealed record Arguments(string Input, CodeStyle Style, DataMode Data, string? Namespace, string? ClassName, string? Out);

    private static Arguments? Parse(string[] args)
    {
        string? input = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--style" or "--data" or "--namespace" or "--class" or "--out")
            {
                if (i + 1 >= args.Length || values.ContainsKey(arg))
                {
                    return null;
                }

                values[arg] = args[++i];
            }
            else if (arg == "-" || !arg.StartsWith('-'))
            {
                if (input is not null)
                {
                    return null;
                }

                input = arg;
            }
            else
            {
                return null;
            }
        }

        if (input is null)
        {
            return null;
        }

        CodeStyle? style = values.GetValueOrDefault("--style", "fluent") switch { "fluent" => CodeStyle.Fluent, "model" => CodeStyle.Model, _ => null };
        DataMode? data = values.GetValueOrDefault("--data", "inline") switch { "inline" => DataMode.Inline, "external" => DataMode.External, "parameter" => DataMode.Parameter, _ => null };
        if (style is null || data is null)
        {
            return null;
        }

        return new Arguments(input, style.Value, data.Value, values.GetValueOrDefault("--namespace"), values.GetValueOrDefault("--class"), values.GetValueOrDefault("--out"));
    }

    /// <summary>Why <see cref="Parse"/> refused the arguments.</summary>
    private static string ParseError(string[] args)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inputs = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--style" or "--data" or "--namespace" or "--class" or "--out" when i + 1 >= args.Length:
                    return $"{arg} needs a value.";
                case "--style" or "--data" or "--namespace" or "--class" or "--out" when !seen.Add(arg):
                    return $"{arg} is given more than once.";
                case "--style" when args[i + 1] is not ("fluent" or "model"):
                    return $"--style must be fluent or model, not \"{args[i + 1]}\".";
                case "--data" when args[i + 1] is not ("inline" or "external" or "parameter"):
                    return $"--data must be inline, external or parameter, not \"{args[i + 1]}\".";
                case "--style" or "--data" or "--namespace" or "--class" or "--out":
                    i++;
                    break;
                default:
                    if (arg != "-" && arg.StartsWith('-'))
                    {
                        return $"unknown option \"{arg}\".";
                    }

                    if (++inputs > 1)
                    {
                        return $"only one input is allowed; \"{arg}\" is a second one.";
                    }

                    break;
            }
        }

        return "an input file, or - for standard input, is required.";
    }

    private static string? Validate(Arguments parsed)
    {
        if (parsed.ClassName is { } name)
        {
            if (!CSharpText.IsIdentifier(name))
            {
                return $"--class \"{name}\" is not a C# identifier.";
            }

            if (Array.IndexOf(SpecConverter.MemberNames, name) >= 0)
            {
                return $"--class \"{name}\" is the name of a member the class declares; choose another name.";
            }

            if (CSharpText.ReservedClassNames.Contains(name))
            {
                return $"--class \"{name}\" is the name of a type the output uses; choose another name.";
            }
        }

        if (parsed.Namespace is { } ns && !CSharpText.IsNamespace(ns))
        {
            return $"--namespace \"{ns}\" is not a dotted C# identifier.";
        }

        return parsed.Data == DataMode.External && parsed.Out is null ? "--data external writes a sidecar file next to the output and needs --out." : null;
    }

    private static int Fail(TextWriter standardError, int code, string message)
    {
        standardError.WriteLine($"flint-cs: error: {message}");
        if (code == UsageError)
        {
            standardError.WriteLine("Run flint-cs --help for the usage.");
        }

        return code;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Write(Stream stream, string text)
    {
        var bytes = Utf8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>Writes to a temporary file in the same folder, which then replaces the target, so a failed run never leaves
    /// a half-written file (design §12.5.4).</summary>
    private static void WriteAtomically(string path, string text)
    {
        var full = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, Utf8.GetBytes(text));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
