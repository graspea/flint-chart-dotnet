using System.Text;
using Graspea.Flint.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graspea.Flint.Generators;

/// <summary>
/// Reads a <c>[FlintChart]</c> class in the <c>ForAttributeWithMetadataName</c> transform (design §12.6.8): only strings,
/// numbers and locations leave it, never a symbol or a syntax node.
/// </summary>
internal static class TargetReader
{
    public static TargetModel Read(GeneratorAttributeSyntaxContext context, bool generic, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];
        var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
        cancellationToken.ThrowIfCancellationRequested();

        var path = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        var flintAttributes = symbol.GetAttributes().Where(a => IsFlintChart(a.AttributeClass)).ToList();

        string? rowTypeId = null;
        LocationInfo? rowTypeLocation = null;
        string? contextId = null;
        LocationInfo? contextLocation = null;
        var rowNamingSet = false;
        var verbatim = false;
        if (generic && attribute.AttributeClass is { TypeArguments.Length: 1 } attributeClass)
        {
            var rowType = attributeClass.TypeArguments[0];
            rowTypeId = rowType.TypeKind == TypeKind.Error ? null : DocumentationCommentId.CreateReferenceId(rowType);
            rowTypeLocation = LocationInfo.From(TypeArgument(attributeSyntax)) ?? LocationInfo.From(attributeSyntax);
            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "RowNaming":
                        rowNamingSet = true;
                        verbatim = named.Value.Value is 1;
                        break;
                    case "JsonContext" when named.Value.Value is ITypeSymbol contextType:
                        contextId = DocumentationCommentId.CreateReferenceId(contextType);
                        contextLocation = LocationInfo.From(NamedArgument(attributeSyntax, "JsonContext")) ?? LocationInfo.From(attributeSyntax);
                        break;
                }
            }
        }

        return new TargetModel(
            generic,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintBase(symbol),
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            ContainingTypes(symbol).ToEquatableArray(),
            symbol.Name,
            (int)EffectiveAccessibility(symbol),
            InvalidTarget(symbol, syntax, generic),
            path,
            flintAttributes.Count,
            flintAttributes.Any(a => a.AttributeClass!.TypeArguments.IsEmpty),
            LocationInfo.From(attributeSyntax),
            LocationInfo.From(attributeSyntax?.ArgumentList?.Arguments.FirstOrDefault()) ?? LocationInfo.From(attributeSyntax),
            LocationInfo.From(syntax.Identifier.GetLocation()),
            rowTypeId,
            rowTypeLocation,
            rowNamingSet,
            verbatim,
            contextId,
            contextLocation);
    }

    /// <summary>Whether the attribute class is one of the generator's (by name, since they are internal to each
    /// compilation).</summary>
    private static bool IsFlintChart(INamedTypeSymbol? type) =>
        type is { Name: "FlintChartAttribute", ContainingNamespace: { Name: "Flint", ContainingNamespace: { Name: "Graspea", ContainingNamespace.IsGlobalNamespace: true } } };

    private static SyntaxNode? TypeArgument(AttributeSyntax? attribute) => attribute?.Name switch
    {
        GenericNameSyntax generic => generic.TypeArgumentList.Arguments.FirstOrDefault(),
        QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic.TypeArgumentList.Arguments.FirstOrDefault(),
        AliasQualifiedNameSyntax { Name: GenericNameSyntax generic } => generic.TypeArgumentList.Arguments.FirstOrDefault(),
        _ => null,
    };

    private static SyntaxNode? NamedArgument(AttributeSyntax? attribute, string name) =>
        attribute?.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name);

    /// <summary>Why the class cannot carry the generated members, or null (FLINT104).</summary>
    private static string? InvalidTarget(INamedTypeSymbol symbol, TypeDeclarationSyntax syntax, bool generic)
    {
        if (symbol.TypeKind != TypeKind.Class || syntax is RecordDeclarationSyntax || !symbol.IsStatic || !IsPartial(symbol))
        {
            return $"{symbol.Name} must be a static partial class.";
        }

        if (symbol.TypeParameters.Length > 0)
        {
            return $"{symbol.Name} must not be generic.";
        }

        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (!IsPartial(containing))
            {
                return $"{symbol.Name} is nested in {containing.Name}, which must be partial.";
            }
        }

        var names = generic ? GeneratedChartWriter.MemberNames.Where(n => n != "Row") : GeneratedChartWriter.MemberNames;
        var declared = names.Where(n => !symbol.GetMembers(n).IsEmpty).ToList();
        return declared.Count == 0
            ? null
            : $"{symbol.Name} declares {string.Join(", ", declared)}, which the generator adds.";
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    /// <summary>The containing types, outermost first, as partial declarations without accessibility.</summary>
    private static IEnumerable<string> ContainingTypes(INamedTypeSymbol symbol)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            chain.Insert(0, containing);
        }

        foreach (var type in chain)
        {
            var keyword = type switch
            {
                { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
                { IsRecord: true } => "record",
                { TypeKind: TypeKind.Struct } => "struct",
                { TypeKind: TypeKind.Interface } => "interface",
                _ => "class",
            };
            var parameters = type.TypeParameters.Length == 0 ? string.Empty : $"<{string.Join(", ", type.TypeParameters.Select(p => p.Name))}>";
            yield return $"partial {keyword} {type.Name}{parameters}";
        }
    }

    /// <summary>The hint name of design §12.6.3: the metadata names with every character outside <c>[A-Za-z0-9_.]</c>
    /// replaced by <c>_</c>.</summary>
    private static string HintBase(INamedTypeSymbol symbol)
    {
        var parts = new List<string> { symbol.MetadataName };
        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            parts.Insert(0, containing.MetadataName);
        }

        if (!symbol.ContainingNamespace.IsGlobalNamespace)
        {
            parts.Insert(0, symbol.ContainingNamespace.ToDisplayString());
        }

        var builder = new StringBuilder();
        foreach (var c in string.Join(".", parts))
        {
            builder.Append(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' ? c : '_');
        }

        return builder.ToString();
    }

    /// <summary>The accessibility a member of the type has at most: the least accessible of the type and its containers.</summary>
    public static Accessibility EffectiveAccessibility(ITypeSymbol type)
    {
        var result = Accessibility.Public;
        for (ISymbol? current = type; current is INamedTypeSymbol named; current = named.ContainingType)
        {
            if (named.DeclaredAccessibility < result)
            {
                result = named.DeclaredAccessibility;
            }
        }

        return result;
    }
}
