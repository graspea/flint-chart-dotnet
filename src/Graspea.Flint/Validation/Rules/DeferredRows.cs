namespace Graspea.Flint.Validation.Rules;

/// <summary>Rows a validation does not see (design §13.6): streamed data, or a template's rows that are bound later.</summary>
/// <param name="Keys">The keys every row writes, from the row metadata; null for a dynamic shape or without metadata, and
/// the key rules skip.</param>
/// <param name="Template">A template: binding supplies the data, so <c>data_missing</c> is not reported.</param>
internal sealed record DeferredRows(IReadOnlyList<string>? Keys, bool Template);
