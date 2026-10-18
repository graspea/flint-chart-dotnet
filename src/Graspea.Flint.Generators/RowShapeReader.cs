using Graspea.Flint.CSharp;
using Microsoft.CodeAnalysis;

namespace Graspea.Flint.Generators;

/// <summary>
/// Reads mode B's row type and context from the current compilation (design §12.6.5, §12.6.8): the members System.Text.Json
/// writes, by its rules, and the context's options from its attributes in source. It runs on every compilation change, so
/// it reads one type and returns a value-equal model.
/// </summary>
internal static class RowShapeReader
{
    private const string JsonNamespace = "System.Text.Json.Serialization";
    private const int WriteAsString = 2;

    public static RowShapeModel? Read(TargetModel target, Compilation compilation, CancellationToken cancellationToken)
    {
        if (target.RowTypeId is null || DocumentationCommentId.GetFirstSymbolForReferenceId(target.RowTypeId, compilation) is not ITypeSymbol rowType)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var typeName = rowType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
        var displayName = rowType.ToDisplayString();
        var context = target.JsonContextId is not null && DocumentationCommentId.GetFirstSymbolForReferenceId(target.JsonContextId, compilation) is INamedTypeSymbol contextType
            ? Context(contextType, rowType)
            : null;
        var includeFields = context is { IsContext: true } && IncludeFields(DocumentationCommentId.GetFirstSymbolForReferenceId(target.JsonContextId!, compilation) as INamedTypeSymbol);
        var members = Members(rowType, compilation, includeFields).ToEquatableArray();
        var numbersAsStrings = NumberHandling(rowType.GetAttributes()) is { } handling && (handling & WriteAsString) != 0;
        return new RowShapeModel(true, typeName, displayName, (int)TargetReader.EffectiveAccessibility(rowType), IsFileLocal(rowType), numbersAsStrings, members, context);
    }

    /// <summary>Whether the type or a containing type is file-local, so the generated file cannot name it.</summary>
    private static bool IsFileLocal(ITypeSymbol type)
    {
        for (ISymbol? current = type; current is INamedTypeSymbol named; current = named.ContainingType)
        {
            if (named.IsFileLocal)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The candidates of design §12.6.5, inherited ones included and a derived member first: instance properties
    /// with a public getter, any property or field with <c>[JsonInclude]</c>, and public fields when the context includes
    /// fields; without <c>[JsonIgnore]</c> (unconditional) and without <c>[JsonExtensionData]</c>.</summary>
    private static IEnumerable<RowMemberModel> Members(ITypeSymbol rowType, Compilation compilation, bool includeFields)
    {
        var types = new List<ITypeSymbol>();
        if (rowType.TypeKind == TypeKind.Interface)
        {
            types.Add(rowType);
            types.AddRange(rowType.AllInterfaces);
        }
        else
        {
            for (var type = rowType; type is not null && type.SpecialType != SpecialType.System_Object && type.SpecialType != SpecialType.System_ValueType; type = type.BaseType)
            {
                types.Add(type);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.IsStatic || member.IsImplicitlyDeclared && member is IFieldSymbol)
                {
                    continue;
                }

                var attributes = member.GetAttributes();
                var include = Has(attributes, "JsonIncludeAttribute");
                ITypeSymbol memberType;
                switch (member)
                {
                    case IPropertySymbol { IsIndexer: false, GetMethod: not null } property
                        when include || (property.DeclaredAccessibility == Accessibility.Public && property.GetMethod.DeclaredAccessibility == Accessibility.Public):
                        memberType = property.Type;
                        break;
                    case IFieldSymbol { IsConst: false } field when include || (includeFields && field.DeclaredAccessibility == Accessibility.Public):
                        memberType = field.Type;
                        break;
                    default:
                        continue;
                }

                if (!seen.Add(member.Name) || Has(attributes, "JsonExtensionDataAttribute"))
                {
                    continue;
                }

                // JsonIgnoreCondition: Never = 0 and WhenReading = 5 write the member; Always = 1 and WhenWriting = 4 never do;
                // WhenWritingDefault = 2, WhenWritingNull = 3 and any value this version does not know may omit it.
                var ignore = Find(attributes, "JsonIgnoreAttribute");
                var condition = ignore?.NamedArguments.FirstOrDefault(a => a.Key == "Condition").Value.Value as int?;
                if (ignore is not null && condition is null or 1 or 4)
                {
                    continue;
                }

                var jsonName = Find(attributes, "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;
                var opaque = Has(attributes, "JsonConverterAttribute");
                var kind = opaque ? RowMemberKind.Opaque : Kind(memberType);
                var numbersAsStrings = kind is RowMemberKind.Integer or RowMemberKind.Real && NumberHandling(attributes) is { } handling && (handling & WriteAsString) != 0;
                yield return new RowMemberModel(
                    new RowMember(
                        member.Name,
                        jsonName,
                        memberType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                        kind,
                        compilation.IsSymbolAccessibleWithin(member, compilation.Assembly),
                        ignore is null ? null : condition switch { 0 or 5 => null, 2 => "WhenWritingDefault", 3 => "WhenWritingNull", _ => $"(JsonIgnoreCondition){condition}" },
                        numbersAsStrings),
                    LocationInfo.From(member.Locations.FirstOrDefault(l => l.IsInSource)));
            }
        }
    }

    /// <summary>What the type checks of design §12.6.5 see in a member's type.</summary>
    private static RowMemberKind Kind(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (Has(type.GetAttributes(), "JsonConverterAttribute"))
        {
            return RowMemberKind.Opaque;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64:
                return RowMemberKind.Integer;
            case SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal:
                return RowMemberKind.Real;
            case SpecialType.System_String:
                return RowMemberKind.String;
            case SpecialType.System_Char:
                return RowMemberKind.Char;
            case SpecialType.System_Boolean:
                return RowMemberKind.Bool;
            case SpecialType.System_DateTime:
                return RowMemberKind.DateTime;
            case SpecialType.System_Object:
                return RowMemberKind.Opaque;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return RowMemberKind.Enum;
        }

        switch (type.ToDisplayString())
        {
            case "System.Int128" or "System.UInt128":
                return RowMemberKind.Integer;
            case "System.Half":
                return RowMemberKind.Real;
            case "System.DateOnly":
                return RowMemberKind.DateOnly;
            case "System.DateTimeOffset":
                return RowMemberKind.DateTimeOffset;
            case "System.TimeOnly":
                return RowMemberKind.TimeOnly;
            case "System.TimeSpan":
                return RowMemberKind.TimeSpan;
            case "System.Guid":
                return RowMemberKind.Guid;
            case "System.Uri":
                return RowMemberKind.Uri;
            case "System.Text.Json.JsonElement" or "System.Text.Json.JsonDocument":
                return RowMemberKind.Opaque;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Text.Json.Nodes.JsonNode")
            {
                return RowMemberKind.Opaque;
            }
        }

        if (type is IArrayTypeSymbol)
        {
            return RowMemberKind.Collection;
        }

        var interfaces = type.AllInterfaces.Select(i => i.OriginalDefinition.ToDisplayString()).ToList();
        if (interfaces.Contains("System.Collections.Generic.IDictionary<TKey, TValue>") || interfaces.Contains("System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"))
        {
            return RowMemberKind.Object;
        }

        return interfaces.Contains("System.Collections.IEnumerable") ? RowMemberKind.Collection : RowMemberKind.Object;
    }

    /// <summary>The context's options from <c>[JsonSourceGenerationOptions]</c> and whether a <c>[JsonSerializable]</c> names
    /// the row type, directly, as an array element or as a generic type argument.</summary>
    private static ContextModel Context(INamedTypeSymbol contextType, ITypeSymbol rowType)
    {
        var isContext = false;
        for (var type = contextType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString() == $"{JsonNamespace}.JsonSerializerContext")
            {
                isContext = true;
                break;
            }
        }

        var attributes = contextType.GetAttributes();
        var names = attributes.Where(a => IsJson(a, "JsonSerializableAttribute"))
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol)
            .Any(t => t is not null && Names(t, rowType));
        var options = Find(attributes, "JsonSourceGenerationOptionsAttribute");
        var policy = options?.ConstructorArguments.FirstOrDefault().Value is 1 ? RowNamingPolicy.CamelCase : RowNamingPolicy.Verbatim;
        var useStringEnums = false;
        string? ignore = null;
        var numbersAsStrings = false;
        foreach (var named in options?.NamedArguments ?? [])
        {
            switch (named.Key)
            {
                case "PropertyNamingPolicy" when named.Value.Value is int known:
                    policy = known switch
                    {
                        1 => RowNamingPolicy.CamelCase,
                        2 => RowNamingPolicy.SnakeCaseLower,
                        3 => RowNamingPolicy.SnakeCaseUpper,
                        4 => RowNamingPolicy.KebabCaseLower,
                        5 => RowNamingPolicy.KebabCaseUpper,
                        _ => RowNamingPolicy.Verbatim,
                    };
                    break;
                case "UseStringEnumConverter":
                    useStringEnums = named.Value.Value is true;
                    break;
                case "DefaultIgnoreCondition" when named.Value.Value is int condition and not 0:
                    ignore = condition switch { 2 => "WhenWritingDefault", 3 => "WhenWritingNull", _ => "Always" };
                    break;
                case "NumberHandling" when named.Value.Value is int handling:
                    numbersAsStrings = (handling & WriteAsString) != 0;
                    break;
            }
        }

        return new ContextModel(
            isContext,
            contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            contextType.ToDisplayString(),
            names,
            policy,
            useStringEnums,
            ignore,
            numbersAsStrings);
    }

    private static bool IncludeFields(INamedTypeSymbol? contextType) =>
        Find(contextType?.GetAttributes() ?? [], "JsonSourceGenerationOptionsAttribute")?.NamedArguments.Any(a => a.Key == "IncludeFields" && a.Value.Value is true) == true;

    private static bool Names(ITypeSymbol type, ITypeSymbol rowType) =>
        SymbolEqualityComparer.Default.Equals(type, rowType)
        || type is IArrayTypeSymbol array && Names(array.ElementType, rowType)
        || type is INamedTypeSymbol named && named.TypeArguments.Any(t => Names(t, rowType));

    private static int? NumberHandling(IEnumerable<AttributeData> attributes) =>
        Find(attributes, "JsonNumberHandlingAttribute")?.ConstructorArguments.FirstOrDefault().Value as int?;

    private static bool Has(IEnumerable<AttributeData> attributes, string name) => Find(attributes, name) is not null;

    private static AttributeData? Find(IEnumerable<AttributeData> attributes, string name) => attributes.FirstOrDefault(a => IsJson(a, name));

    private static bool IsJson(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } type && type.Name == name && type.ContainingNamespace.ToDisplayString() == JsonNamespace;
}
