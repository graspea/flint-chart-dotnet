using Graspea.Flint.Tests.Support;
using Microsoft.CodeAnalysis.CSharp;

namespace Graspea.Flint.Tests.Probes;

public sealed class OverloadPriorityProbeTests
{
    private const string Pair = """
        using System.Collections.Generic;
        namespace Probe;
        public static class Pair
        {
            public static int Write<T>(IEnumerable<T> rows) => 0;
            [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
            public static int Write<T>(IAsyncEnumerable<T> rows) => 1;
        }
        """;

    /// <summary>The library's own declaration on net8; on later runtimes the framework declares the attribute, and a
    /// second declaration in source would only add a conflict warning.</summary>
    private static string[] DeclaringSources()
    {
        if (typeof(object).Assembly.GetType("System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute") is not null)
        {
            return [Pair];
        }

        var attribute = Path.Combine(CommittedIr.RepositoryRoot(), "src", "Graspea.Flint", "Polyfills", "OverloadResolutionPriorityAttribute.cs");
        return [Pair, File.ReadAllText(attribute)];
    }

    // source: design §13.5 and §13.13 item 8 (the overload pair with the internal attribute compiles without
    // diagnostics at the library's pinned LangVersion, 13)
    [Test]
    public void OverloadPair_AtLibraryLanguageVersion_CompilesCleanly()
    {
        Assert.That(CompileProbe.LibraryLanguageVersion(), Is.EqualTo(LanguageVersion.CSharp13));
        Assert.That(CompileProbe.Diagnostics(CompileProbe.LibraryLanguageVersion(), DeclaringSources()).Select(d => d.ToString()), Is.Empty);
    }

    // source: design §13.5 (compiled under C# 12 the pair is CS9202, "Feature 'overload resolution priority' is not
    // available in C# 12.0", Roslyn 4.14), so the LangVersion setting cannot be dropped unnoticed
    [Test]
    public void OverloadPair_AtCSharp12_IsCS9202()
    {
        Assert.That(CompileProbe.Diagnostics(LanguageVersion.CSharp12, DeclaringSources()).Select(d => d.Id), Has.Member("CS9202"));
    }
}

public sealed class StreamOverloadProbeTests
{
    /// <summary>A source that implements both interfaces, as Entity Framework Core's DbSet does.</summary>
    private const string Consumer = """
        using System.Collections;
        using System.Collections.Generic;
        using System.Text.Json.Serialization.Metadata;
        using System.Threading;
        using Graspea.Flint;
        namespace Probe;
        public sealed class Both : IEnumerable<int>, IAsyncEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => throw null!;
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => throw null!;
        }
        public static class Use
        {
            public static ChartData WithMetadata(Both rows, JsonTypeInfo<int> typeInfo) => ChartData.Stream(rows, typeInfo);
        }
        """;

    // source: design §13.5 (spike, Roslyn 4.14: for a type implementing both interfaces C# 13 picks the prioritised
    // IAsyncEnumerable overload) and §13.13 item 8
    [Test]
    public void Stream_SourceWithBothInterfaces_AtCSharp13_BindsTheAsyncOverload()
    {
        var compilation = CompileProbe.Compile(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp13, Consumer);
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()), Is.Empty);
        Assert.That(CompileProbe.BoundFirstParameters(compilation, "Stream"), Is.EqualTo(new[] { "System.Collections.Generic.IAsyncEnumerable<TRow>" }));
    }

    // source: design §13.5 (C# 12 reports CS0121; a net8 consumer on the default language version writes
    // .AsAsyncEnumerable() or .AsEnumerable(), as the XML docs say)
    [Test]
    public void Stream_SourceWithBothInterfaces_AtCSharp12_IsAmbiguous()
    {
        Assert.That(CompileProbe.Diagnostics(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12, Consumer).Select(d => d.Id), Has.Member("CS0121"));
    }
}
