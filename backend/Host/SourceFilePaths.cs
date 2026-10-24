namespace Ras.Backend;

/// <summary>Compares source paths by filesystem identity while leaving persisted paths untouched.</summary>
internal static class SourceFilePaths
{
    private static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static bool SameSourceFile(string left, string right) =>
        Comparer.Equals(Canonical(left), Canonical(right));

    internal static string Canonical(string path) => Canonical(Path.GetFullPath(path), depth: 0);

    private static string Canonical(string fullPath, int depth)
    {
        if (depth > 64) throw new IOException("The source path contains too many symbolic links.");

        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The source path must be absolute.", nameof(fullPath));
        string current = root;
        string remainder = fullPath[root.Length..];
        foreach (string segment in remainder.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            // Missing raster dependencies are tracked as "missing" by their
            // editors. Resolve existing parent aliases without requiring the
            // leaf (or every trailing directory) to exist.
            if (info.LinkTarget is null) continue;
            FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null) continue;

            string targetPath = Path.GetFullPath(target.FullName);
            if (!Comparer.Equals(targetPath, current)) current = Canonical(targetPath, depth + 1);
        }
        return current;
    }
}
