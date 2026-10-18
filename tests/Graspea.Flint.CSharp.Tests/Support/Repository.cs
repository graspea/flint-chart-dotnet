namespace Graspea.Flint.CSharp.Tests.Support;

/// <summary>Paths in the repository, found from the test output folder.</summary>
public static class Repository
{
    private static readonly Lazy<string> Root = new(() =>
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Graspea.Flint.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the repository root (Graspea.Flint.slnx).");
    });

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root.Value, .. parts]);
}
