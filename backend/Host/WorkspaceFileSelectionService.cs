using System.Security.Cryptography;

namespace Ras.Backend;

public sealed record WorkspaceFileSelectionRequest(string SourcePath);
public sealed record WorkspaceFileSelection(string SourcePath, string CanonicalPath, string Revision, long Length);

/// <summary>Captures the file explicitly selected in a server-side path input before an import inspection.</summary>
public static class WorkspaceFileSelectionService
{
    public static WorkspaceFileSelection Inspect(WorkspaceFileSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath))
            throw new ArgumentException("Choose an absolute path visible to the C# server.");
        string path = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The selected source file was not found.", path);
        string canonical = SourceFilePaths.Canonical(path);
        var before = new FileInfo(path);
        long length = before.Length;
        DateTime modified = before.LastWriteTimeUtc;
        string revision;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                   1024 * 1024, FileOptions.SequentialScan))
        {
            revision = Convert.ToHexString(SHA256.HashData(file));
        }
        var after = new FileInfo(path);
        if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != modified ||
            !SourceFilePaths.Canonical(path).Equals(canonical,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return new(path, canonical, revision, length);
    }
}
