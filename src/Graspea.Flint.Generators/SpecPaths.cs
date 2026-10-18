namespace Graspea.Flint.Generators;

/// <summary>
/// Path matching of design §12.6.4: separators unified to <c>/</c>, <c>.</c> and <c>..</c> segments resolved, and compared
/// ordinally. The generator never touches the file system.
/// </summary>
internal static class SpecPaths
{
    /// <summary>The normalised form of an absolute or relative path.</summary>
    public static string Normalize(string path)
    {
        var unified = path.Replace('\\', '/');
        var rooted = unified.StartsWith("/", StringComparison.Ordinal);
        var segments = new List<string>();
        foreach (var segment in unified.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[segments.Count - 1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return (rooted ? "/" : string.Empty) + string.Join("/", segments);
    }

    /// <summary>The attribute path resolved against the project directory, unless it is already absolute.</summary>
    public static string Resolve(string projectDirectory, string path)
    {
        var unified = path.Replace('\\', '/');
        var absolute = unified.StartsWith("/", StringComparison.Ordinal) || (unified.Length > 1 && unified[1] == ':');
        return Normalize(absolute || projectDirectory.Length == 0 ? unified : projectDirectory.Replace('\\', '/').TrimEnd('/') + "/" + unified);
    }

    /// <summary>
    /// Whether two normalised absolute paths name the same file: equal, or equal after a prefix the compiler or MSBuild
    /// added by resolving a symbolic link at the root (macOS reports <c>/var/…</c> as <c>/private/var/…</c>), which the
    /// generator cannot resolve without touching the file system.
    /// </summary>
    public static bool Same(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        var (longer, shorter) = a.Length > b.Length ? (a, b) : (b, a);
        return shorter.StartsWith("/", StringComparison.Ordinal) && longer.EndsWith(shorter, StringComparison.Ordinal);
    }

    /// <summary>Whether a file is a spec by convention (<c>*.flint.json</c>).</summary>
    public static bool IsSpecFile(string path) => path.EndsWith(".flint.json", StringComparison.Ordinal);
}
