namespace System.Runtime.CompilerServices;

/// <summary>The C# 13 overload priority attribute, which net8 lacks; the compiler matches it by name. It ranks the
/// <c>IAsyncEnumerable</c> overload above the <c>IEnumerable</c> one for a source that implements both (design §13.5).
/// Compile-time only.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
{
    /// <summary>The priority; higher wins.</summary>
    public int Priority { get; } = priority;
}
