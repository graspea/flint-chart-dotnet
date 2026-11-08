namespace Graspea.Flint.Serialization;

/// <summary>The nesting limit FlintJson applies to a document, which rows written outside a document must respect.</summary>
internal static class JsonDepth
{
    /// <summary>The depth System.Text.Json applies when the options set none.</summary>
    internal const int Default = 64;

    internal static int Effective => FlintJson.Options.MaxDepth == 0 ? Default : FlintJson.Options.MaxDepth;
}
