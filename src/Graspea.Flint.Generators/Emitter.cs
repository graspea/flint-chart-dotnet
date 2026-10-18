using System.Collections.Immutable;
using Graspea.Flint.CSharp;
using Microsoft.CodeAnalysis;

namespace Graspea.Flint.Generators;

/// <summary>What the models step of design §12.6.8 sees besides the target: every spec, the project directory, whether the
/// language version is C# 12 or later, the referenced Graspea.Flint version (null when it is not referenced), and the
/// generator's own version.</summary>
internal sealed record EmitInputs(ImmutableArray<SpecModel> Specs, string ProjectDirectory, bool LanguageSupported, string? Reference, string Version);

/// <summary>
/// The models step (design §12.6.8): matches a target with its spec, runs the checks and writes the source. An Error never
/// stops the members from being generated, except where the generated code could not compile (FLINT100 without the library,
/// FLINT104, FLINT105 for two attributes or a row type below internal, FLINT107); without a spec the members are written as
/// throwing stubs.
/// </summary>
internal static class Emitter
{
    public static EmitModel Emit(TargetWithShape item, EmitInputs inputs, CancellationToken cancellationToken)
    {
        var target = item.Target;
        var diagnostics = new List<DiagnosticInfo>();
        EmitModel Result(string? source) => new(target.ClassKey, target.HintBase, source, diagnostics.ToEquatableArray());
        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, string message, DiagnosticSeverity? severity = null) =>
            diagnostics.Add(new DiagnosticInfo(descriptor.Id, severity ?? descriptor.DefaultSeverity, message, location));

        if (target.InvalidTarget is { } invalid)
        {
            Report(Descriptors.InvalidTarget, target.IdentifierLocation, invalid);
            return Result(null);
        }

        if (target.AttributeCount > 1)
        {
            // Reported once: by the plain attribute's target when the class has one.
            if (!target.Generic || !target.HasPlainAttribute)
            {
                Report(Descriptors.InvalidAttribute, target.AttributeLocation, $"{target.ClassName} has more than one FlintChart attribute; apply exactly one.");
            }

            return Result(null);
        }

        if (!inputs.LanguageSupported)
        {
            Report(Descriptors.LanguageVersion, target.IdentifierLocation, $"C# 12 or later is required to generate {target.ClassName}; the project sets a lower LangVersion.");
            return Result(null);
        }

        if (inputs.Reference is null)
        {
            Report(Descriptors.ReferenceMismatch, target.AttributeLocation, $"Graspea.Flint is not referenced; add Graspea.Flint {inputs.Version}, the generator's version.");
            return Result(null);
        }

        if (inputs.Reference != inputs.Version)
        {
            Report(Descriptors.ReferenceMismatch, target.AttributeLocation,
                $"Graspea.Flint {inputs.Reference} is referenced, but the generator is {inputs.Version}; reference Graspea.Flint {inputs.Version}, or the generator at {inputs.Reference}.");
        }

        var shape = item.Shape;
        if (target.Generic && shape is null)
        {
            // The row type does not resolve; the compiler reports it.
            return Result(null);
        }

        if (shape is not null && (shape.FileLocal || shape.Accessibility < (int)Microsoft.CodeAnalysis.Accessibility.Internal))
        {
            // The generated members are public or internal; a less accessible row type in their signatures would not compile.
            var kind = shape.FileLocal ? "file-local" : (Microsoft.CodeAnalysis.Accessibility)shape.Accessibility switch
            {
                Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
                Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
                _ => "private",
            };
            Report(Descriptors.InvalidAttribute, target.RowTypeLocation, $"The row type {shape.DisplayName} is {kind}; the generated members need a row type that is at least internal.");
            return Result(null);
        }

        var accessibility = Accessibility(Math.Min(target.Accessibility, shape?.Accessibility ?? int.MaxValue));
        var rowType = shape?.TypeName;
        if (string.IsNullOrEmpty(target.Path))
        {
            Report(Descriptors.InvalidAttribute, target.PathLocation, "The FlintChart path must be a non-empty string.");
            return Result(GeneratedChartWriter.WriteUnavailable(Target(target, accessibility, inputs, "-"), rowType, $"The FlintChart attribute of {target.ClassName} has no path (FLINT105)."));
        }

        var relative = SpecPaths.Normalize(target.Path!);
        var generated = Target(target, accessibility, inputs, relative);
        var resolved = SpecPaths.Resolve(inputs.ProjectDirectory, target.Path!);
        var spec = inputs.Specs.FirstOrDefault(s => s.NormalizedPath == resolved) ?? inputs.Specs.FirstOrDefault(s => SpecPaths.Same(s.NormalizedPath, resolved));
        if (spec is null)
        {
            var candidate = inputs.Specs.FirstOrDefault(s => string.Equals(s.NormalizedPath, resolved, StringComparison.OrdinalIgnoreCase));
            var hint = candidate is null ? string.Empty : $" {candidate.FilePath} differs only in case.";
            Report(Descriptors.SpecNotFound, target.PathLocation,
                $"\"{target.Path}\" matches no AdditionalFiles item (resolved against the project directory to {resolved}); name the file *.flint.json or add it with <AdditionalFiles Include=\"...\" />.{hint}");
            return Result(GeneratedChartWriter.WriteUnavailable(generated, rowType, $"The spec {relative} is not in AdditionalFiles (FLINT101)."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (spec.Parsed.Error is { } error)
        {
            var offset = Math.Min(error.Position.Offset, spec.Text.Length);
            Report(Descriptors.InvalidJson, spec.Location(offset, Math.Min(offset + 1, spec.Text.Length)), $"{relative} is not valid JSON: {error.Message}");
            return Result(GeneratedChartWriter.WriteUnavailable(generated, rowType, $"The spec {relative} is not valid JSON (FLINT102)."));
        }

        var root = spec.Parsed.Root!;
        if (SpecShape.Check(root) is { } shapeIssue)
        {
            Report(Descriptors.NotAChart, spec.Location(shapeIssue.Start.Offset, shapeIssue.End), $"{relative}: {shapeIssue.Message}");
            return Result(GeneratedChartWriter.WriteUnavailable(generated, rowType, $"The spec {relative} is not a ChartAssemblyInput (FLINT103)."));
        }

        var document = new SpecDocument(root);
        foreach (var issue in CatalogCheck.Run(document, cancellationToken))
        {
            Report(Descriptors.Get(issue.Id), spec.Location(issue.Start.Offset, issue.End), issue.Message, Severity(issue.Severity));
        }

        if (document.Data is { IsObject: true } data && data.Get("url") is { IsString: true } url)
        {
            Report(Descriptors.UrlNotUsed, spec.Location(url.Start.Offset, url.End),
                $"data.url is not used by the generated members, which take rows; bind it at run time with {target.ClassName}.Template.Bind(ChartData.FromUrl(...)).");
        }

        return Result(shape is null ? ModeA(document, generated, spec, relative, Report) : ModeB(document, generated, target, shape, relative, Report));
    }

    private static string ModeA(SpecDocument document, GeneratedTarget generated, SpecModel spec, string relative, Action<DiagnosticDescriptor, LocationInfo?, string, DiagnosticSeverity?> report)
    {
        var plan = RowInference.Infer(document, replay: false);
        if (!plan.Typed && document.Values is { } values)
        {
            report(Descriptors.NonUniformRows, spec.Location(values.Start.Offset, values.End),
                $"The rows of {relative} are not objects with the same keys in the same order; every Row member is JsonNode?.", null);
        }

        foreach (var field in plan.UnhintedFields)
        {
            var node = FieldNode(document, field);
            report(Descriptors.UninferredField, node is null ? null : spec.Location(node.Value.Start, node.Value.End),
                $"The field \"{field}\" has no values and no type hint in {relative}; its Row member is a string.", null);
        }

        return GeneratedChartWriter.Write(document, generated, plan);
    }

    private static string ModeB(SpecDocument document, GeneratedTarget generated, TargetModel target, RowShapeModel shape, string relative, Action<DiagnosticDescriptor, LocationInfo?, string, DiagnosticSeverity?> report)
    {
        var context = shape.Context;
        if (context is { IsContext: false })
        {
            report(Descriptors.InvalidAttribute, target.JsonContextLocation, $"JsonContext {context.DisplayName} is not a JsonSerializerContext.", null);
            context = null;
        }

        if (context is not null && target.RowNamingSet)
        {
            report(Descriptors.InvalidAttribute, target.AttributeLocation, $"RowNaming has no effect with JsonContext, whose options name the keys; remove RowNaming.", null);
        }

        if (context is not null)
        {
            if (!context.NamesRowType)
            {
                report(Descriptors.ContextWithoutRowType, target.JsonContextLocation,
                    $"{context.DisplayName} has no [JsonSerializable] that names {shape.DisplayName}; GetTypeInfo may return null at run time, and Template then throws. Add [JsonSerializable(typeof({shape.DisplayName}))].", null);
            }

            if (context.DefaultIgnoreCondition is { } condition)
            {
                report(Descriptors.RowMetadata, target.JsonContextLocation,
                    $"{context.DisplayName} sets DefaultIgnoreCondition = {condition}: the row metadata may omit a null member, and ChartData.FromRows throws ArgumentException.", null);
            }

            if (context.WritesNumbersAsStrings)
            {
                report(Descriptors.RowMetadata, target.JsonContextLocation,
                    $"{context.DisplayName} writes numbers as strings (JsonNumberHandling.WriteAsString), and ChartData.FromRows throws ArgumentException.", null);
            }
        }

        if (shape.WritesNumbersAsStrings)
        {
            report(Descriptors.RowMetadata, target.RowTypeLocation,
                $"{shape.DisplayName} writes numbers as strings (JsonNumberHandling.WriteAsString), and ChartData.FromRows throws ArgumentException.", null);
        }

        var policy = context?.Policy ?? (target.Verbatim ? RowNamingPolicy.Verbatim : RowNamingPolicy.CamelCase);
        var enumsAsNumbers = context is not null && !context.UseStringEnumConverter;
        var binding = RowBinding.Bind(document, shape.Members.Select(m => m.Member).ToList(), policy, enumsAsNumbers, shape.DisplayName, relative);
        foreach (var issue in binding.Issues)
        {
            // FLINT122 is at the member; the binding findings are at the type argument and name the member (design §12.6.7).
            var location = issue.Id == "FLINT122" && issue.Member is { } index ? shape.Members[index].Location ?? target.RowTypeLocation : target.RowTypeLocation;
            report(Descriptors.Get(issue.Id), location, issue.Message, Severity(issue.Severity));
        }

        var row = new BoundRowType(shape.TypeName, shape.DisplayName, binding.Members, context?.TypeName, context?.DisplayName, target.Verbatim && context is null);
        return GeneratedChartWriter.Write(document, generated, row);
    }

    /// <summary>Where a field first appears: an encoding field, an annotation key or a display-name key.</summary>
    private static (int Start, int End)? FieldNode(SpecDocument document, string field)
    {
        foreach (var member in document.Encodings?.Members ?? [])
        {
            foreach (var entry in member.Value.IsArray ? member.Value.Items : [member.Value])
            {
                var node = entry.IsString ? entry : entry.IsObject ? entry.Get("field") : null;
                if (node is { IsString: true } && node.Value == field)
                {
                    return (node.Start.Offset, node.End);
                }
            }
        }

        foreach (var map in new[] { document.SemanticTypes, document.FieldDisplayNames })
        {
            if (map?.Members.FirstOrDefault(m => m.Key == field) is { } key)
            {
                return (key.KeyPosition.Offset, key.KeyEnd);
            }
        }

        return null;
    }

    private static GeneratedTarget Target(TargetModel target, string accessibility, EmitInputs inputs, string path) =>
        new(target.Namespace, target.ContainingTypes.ToList(), target.ClassName, accessibility, path, inputs.Version);

    /// <summary>The generated members' accessibility (design §12.6.3): <c>public</c> when the class and the row type are
    /// public, otherwise <c>internal</c>, so that a public class over an internal row type compiles.</summary>
    private static string Accessibility(int accessibility) =>
        (Microsoft.CodeAnalysis.Accessibility)accessibility == Microsoft.CodeAnalysis.Accessibility.Public ? "public" : "internal";

    private static DiagnosticSeverity Severity(SpecSeverity severity) => severity switch
    {
        SpecSeverity.Error => DiagnosticSeverity.Error,
        SpecSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info,
    };
}
