using System.Collections;
using System.Collections.Immutable;
using Graspea.Flint.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Graspea.Flint.Generators;

/// <summary>An immutable array with value equality, so a pipeline step whose output is unchanged stops there (design
/// §12.6.8).</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public int Count => _items.IsDefault ? 0 : _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other) => this.SequenceEqual(other);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in this)
        {
            hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => (_items.IsDefault ? ImmutableArray<T>.Empty : _items).AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Extension helpers for <see cref="EquatableArray{T}"/>.</summary>
internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new(items.ToImmutableArray());
}

/// <summary>A location held across pipeline steps without a syntax tree: the file, the span and the line span.</summary>
internal sealed record LocationInfo(string FilePath, int Start, int Length, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
{
    public static LocationInfo? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        var span = location.GetLineSpan().Span;
        return new(location.SourceTree.FilePath, location.SourceSpan.Start, location.SourceSpan.Length, span.Start.Line, span.Start.Character, span.End.Line, span.End.Character);
    }

    public static LocationInfo? From(SyntaxNode? node) => node is null ? null : From(node.GetLocation());

    /// <summary>The location in <paramref name="trees"/>' syntax tree of the file, when it has one and the span fits; an
    /// external location otherwise, as for a JSON spec.</summary>
    public Location ToLocation(IReadOnlyDictionary<string, SyntaxTree>? trees)
    {
        var span = new TextSpan(Start, Length);
        if (trees is not null && trees.TryGetValue(FilePath, out var tree) && span.End <= tree.Length)
        {
            return Location.Create(tree, span);
        }

        return Location.Create(FilePath, span, new LinePositionSpan(new LinePosition(StartLine, StartCharacter), new LinePosition(EndLine, EndCharacter)));
    }
}

/// <summary>
/// A <c>[FlintChart]</c> class (design §12.6.8, the targets steps): where to write it, the attribute's arguments and their
/// locations, and why the class cannot be extended, if it cannot. Mode B adds the row type and the context, by documentation
/// comment id, which the row shapes step resolves against the current compilation.
/// </summary>
internal sealed record TargetModel(
    bool Generic,
    string ClassKey,
    string HintBase,
    string? Namespace,
    EquatableArray<string> ContainingTypes,
    string ClassName,
    int Accessibility,
    string? InvalidTarget,
    string? Path,
    int AttributeCount,
    bool HasPlainAttribute,
    LocationInfo? AttributeLocation,
    LocationInfo? PathLocation,
    LocationInfo? IdentifierLocation,
    string? RowTypeId,
    LocationInfo? RowTypeLocation,
    bool RowNamingSet,
    bool Verbatim,
    string? JsonContextId,
    LocationInfo? JsonContextLocation);

/// <summary>A member of the row type and where it is declared (null outside the compilation's sources).</summary>
internal sealed record RowMemberModel(RowMember Member, LocationInfo? Location);

/// <summary>
/// The <c>JsonSerializerContext</c> of mode B as its attributes in source say (design §12.6.5): whether it is one, its
/// names, whether a <c>[JsonSerializable]</c> names the row type, and the options the row guarantees depend on.
/// </summary>
internal sealed record ContextModel(
    bool IsContext,
    string TypeName,
    string DisplayName,
    bool NamesRowType,
    RowNamingPolicy Policy,
    bool UseStringEnumConverter,
    string? DefaultIgnoreCondition,
    bool WritesNumbersAsStrings);

/// <summary>Mode B's row type as System.Text.Json writes it (design §12.6.5), read from the current compilation.</summary>
internal sealed record RowShapeModel(
    bool Resolved,
    string TypeName,
    string DisplayName,
    int Accessibility,
    bool FileLocal,
    bool WritesNumbersAsStrings,
    EquatableArray<RowMemberModel> Members,
    ContextModel? Context);

/// <summary>A target and, in mode B, its row shape.</summary>
internal sealed record TargetWithShape(TargetModel Target, RowShapeModel? Shape);

/// <summary>
/// A spec file (design §12.6.8, the specs step): its normalised path, the path as the compiler knows it, and its text,
/// parsed once per file content. Equality is by path and text; the parsed document and the line starts are carried along.
/// </summary>
internal sealed class SpecModel(string normalizedPath, string filePath, string text, CancellationToken cancellationToken) : IEquatable<SpecModel>
{
    public string NormalizedPath { get; } = normalizedPath;

    public string FilePath { get; } = filePath;

    public string Text { get; } = text;

    /// <summary>The document, or the JSON error.</summary>
    public ParsedSpec Parsed { get; } = ParsedSpec.Parse(text, cancellationToken);

    public bool Equals(SpecModel? other) => other is not null && NormalizedPath == other.NormalizedPath && FilePath == other.FilePath && Text == other.Text;

    public override bool Equals(object? obj) => Equals(obj as SpecModel);

    public override int GetHashCode() => unchecked((NormalizedPath.GetHashCode() * 31) + Text.Length);

    /// <summary>The location of <c>[start, end)</c> in the file.</summary>
    public LocationInfo Location(int start, int end)
    {
        var (startLine, startCharacter) = Parsed.Position(start);
        var (endLine, endCharacter) = Parsed.Position(end);
        return new(FilePath, start, end - start, startLine, startCharacter, endLine, endCharacter);
    }
}

/// <summary>A parsed spec: the document or the reader's error, and the line starts for positions.</summary>
internal sealed class ParsedSpec
{
    private readonly List<int> _lineStarts;

    private ParsedSpec(JsonItem? root, JsonReadException? error, List<int> lineStarts)
    {
        Root = root;
        Error = error;
        _lineStarts = lineStarts;
    }

    public JsonItem? Root { get; }

    public JsonReadException? Error { get; }

    public static ParsedSpec Parse(string text, CancellationToken cancellationToken)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        try
        {
            return new ParsedSpec(JsonReader.Parse(text, cancellationToken), null, lineStarts);
        }
        catch (JsonReadException e)
        {
            return new ParsedSpec(null, e, lineStarts);
        }
    }

    /// <summary>The 0-based line and character of an offset.</summary>
    public (int Line, int Character) Position(int offset)
    {
        var index = _lineStarts.BinarySearch(offset);
        var line = index >= 0 ? index : ~index - 1;
        return (line, offset - _lineStarts[line]);
    }
}

/// <summary>A diagnostic held across steps: the id, the effective severity, the message and the location.</summary>
internal sealed record DiagnosticInfo(string Id, DiagnosticSeverity Severity, string Message, LocationInfo? Location)
{
    public Diagnostic ToDiagnostic(IReadOnlyDictionary<string, SyntaxTree>? trees) =>
        Diagnostic.Create(Descriptors.Get(Id), Location?.ToLocation(trees) ?? Microsoft.CodeAnalysis.Location.None, Severity, additionalLocations: null, properties: null, Message);
}

/// <summary>The diagnostics of one class, for the reporting step.</summary>
internal sealed record KeyedDiagnostics(string ClassKey, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>What the output step adds for one class: the source (null when none is generated) and the diagnostics.</summary>
internal sealed record EmitModel(string ClassKey, string HintBase, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);
