using Geospatial.IO;

namespace Ras.Backend;

public static partial class MapSourceIdentity
{
    /// <summary>
    /// Rebinds a captured staged Shapefile family to its final sibling paths
    /// without rereading files. The existing family-revision algorithm remains
    /// the single source of truth for the receipt identity.
    /// </summary>
    internal static MapSourceFamilyCapture RebaseStagedShapefileFamily(
        MapSourceFamilyCapture staged, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!string.Equals(staged.Family, "shapefile", StringComparison.Ordinal) ||
            staged.Files is null || staged.Files.Length != 4)
            throw new SourceFileChangedException();

        string source = Path.GetFullPath(destinationPath);
        var destinationByRole = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = source,
            ["shx"] = ShapefileIO.SHXFilename(source),
            ["dbf"] = ShapefileIO.DBFFilename(source),
            ["prj"] = ShapefileIO.PRJFilename(source)
        };
        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        var rebased = new List<MapSourceFileRevision>(staged.Files.Length);
        foreach (MapSourceFileRevision file in staged.Files)
        {
            if (file is null || !destinationByRole.TryGetValue(file.Role, out string? target) || target is null ||
                !seenRoles.Add(file.Role))
                throw new SourceFileChangedException();

            bool required = file.Role != "prj";
            if (file.Required != required || file.Exists != (file.Revision is not null) ||
                (required && !file.Exists) ||
                (file.Revision is not null && !IsSHA256Revision(file.Revision)))
                throw new SourceFileChangedException();

            string fullTarget = Path.GetFullPath(target);
            rebased.Add(new(fullTarget, SourceFilePaths.Canonical(fullTarget), file.Role,
                required, file.Exists, file.Revision));
        }

        if (seenRoles.Count != destinationByRole.Count)
            throw new SourceFileChangedException();

        MapSourceFileRevision[] files = rebased
            .OrderBy(file => file.Path, PathComparer)
            .ThenBy(file => file.InputPath, PathComparer)
            .ThenBy(file => file.Role, StringComparer.Ordinal)
            .ToArray();
        string canonicalSource = SourceFilePaths.Canonical(source);
        string revision = ComputeFamilyRevision(staged.Family, canonicalSource, files);
        return new(staged.Family, canonicalSource, revision, files);
    }

    private static bool IsSHA256Revision(string value) => value.Length == 64 &&
        value.All(character => Uri.IsHexDigit(character));
}
