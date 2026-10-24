namespace Ras.Backend;

/// <summary>
/// A run-scoped list of exact HDF files currently owned by plan compute.
/// The native caller creates this only after validating the compute owner and
/// callback Result identity. It lets map-source readers omit those files
/// before any Project loader or revision reader opens them.
/// </summary>
public sealed record MapResultComputeReadLease(
    string ProjectSessionID,
    string RunID,
    string ProjectPath,
    string[] ProtectedSourcePaths,
    string? CallbackResultPath = null);

internal sealed class MapResultComputeReadLeaseScope
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly HashSet<string> protectedPaths;

    private MapResultComputeReadLeaseScope(MapResultComputeReadLease lease,
        string[] canonicalProtectedPaths)
    {
        Lease = lease with { ProjectPath = SourceFilePaths.Canonical(lease.ProjectPath),
            ProtectedSourcePaths = canonicalProtectedPaths };
        protectedPaths = canonicalProtectedPaths.ToHashSet(PathComparer);
    }

    internal MapResultComputeReadLease Lease { get; }
    internal string[] ProtectedSourcePaths => Lease.ProtectedSourcePaths;

    internal bool IsProtected(string path) => protectedPaths.Contains(SourceFilePaths.Canonical(path));

    internal static MapResultComputeReadLeaseScope? Resolve(MapTreeRequest map,
        string canonicalProjectPath)
    {
        if (map.ComputeReadLease is not { } lease) return null;
        if (!Guid.TryParse(lease.ProjectSessionID, out Guid projectSessionID) || projectSessionID == Guid.Empty ||
            !Guid.TryParse(lease.RunID, out Guid runID) || runID == Guid.Empty ||
            string.IsNullOrWhiteSpace(lease.ProjectPath) || !Path.IsPathFullyQualified(lease.ProjectPath) ||
            !SourceFilePaths.SameSourceFile(lease.ProjectPath, canonicalProjectPath))
            throw new SourceFileChangedException();

        string[] rawPaths = lease.ProtectedSourcePaths ?? [];
        if (rawPaths.Length is 0 or > 64) throw new SourceFileChangedException();
        var canonicalPaths = new HashSet<string>(PathComparer);
        foreach (string? rawPath in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathFullyQualified(rawPath) ||
                !IsSupportedHdfPath(rawPath))
                throw new SourceFileChangedException();
            string canonical = SourceFilePaths.Canonical(rawPath);
            if (!canonicalPaths.Add(canonical)) throw new SourceFileChangedException();
        }

        string? callbackResultPath = null;
        if (!string.IsNullOrWhiteSpace(lease.CallbackResultPath))
        {
            if (!Path.IsPathFullyQualified(lease.CallbackResultPath) ||
                !IsSupportedHdfPath(lease.CallbackResultPath))
                throw new SourceFileChangedException();
            callbackResultPath = SourceFilePaths.Canonical(lease.CallbackResultPath);
            if (!canonicalPaths.Contains(callbackResultPath)) throw new SourceFileChangedException();
        }

        string[] ordered = canonicalPaths.OrderBy(path => path, PathComparer).ToArray();
        var canonicalLease = lease with
        {
            ProjectSessionID = projectSessionID.ToString("D"),
            RunID = runID.ToString("D"),
            ProjectPath = SourceFilePaths.Canonical(lease.ProjectPath),
            ProtectedSourcePaths = ordered,
            CallbackResultPath = callbackResultPath
        };
        return new MapResultComputeReadLeaseScope(canonicalLease, ordered);
    }

    private static bool IsSupportedHdfPath(string path) =>
        Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase);
}
