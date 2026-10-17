using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;

namespace Graspea.Flint;

/// <summary>A semantic type name from the flint-chart registry, such as <c>YearMonth</c> or <c>Amount</c>. Any non-empty string is accepted and written verbatim, so values newer than this
/// library round-trip; the validator reports unknown ones.</summary>
[JsonConverter(typeof(SemanticTypeConverter))]
public readonly partial record struct SemanticType
{
    /// <summary>Creates a value from its wire string.</summary>
    /// <param name="name">The registry name, case-sensitive.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public SemanticType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The wire string; null only for <c>default(SemanticType)</c>, which cannot be serialised.</summary>
    public string Name { get; }

    /// <summary>Whether the pinned flint-chart registry knows this name; an unregistered name has no effect upstream.</summary>
    public bool IsRegistered => Name is not null && CatalogTables.SemanticTypes.ContainsKey(Name);

    /// <summary>An annotation that sets only the semantic type; written as the string form.</summary>
    /// <param name="type">The semantic type.</param>
    public static implicit operator SemanticAnnotation(SemanticType type) => new() { Type = type };

    /// <summary>The wire string.</summary>
    /// <returns><see cref="Name"/>, or an empty string for <c>default(SemanticType)</c>.</returns>
    public override string ToString() => Name ?? string.Empty;
}
