namespace Graspea.Flint.Validation;

/// <summary>How a <see cref="ValidationIssue"/> affects assembly by the pinned flint-chart version.</summary>
public enum IssueSeverity
{
    /// <summary>Upstream assembles the document; the issue is advice, or something only the MCP front door rejects.</summary>
    Info,

    /// <summary>Upstream assembles the document but silently drops or substitutes part of it.</summary>
    Warning,

    /// <summary>An assembler of the pinned flint-chart version throws for the target backend.</summary>
    Error,
}
