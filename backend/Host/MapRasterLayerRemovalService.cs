using Geospatial.GDALAssist;

namespace Ras.Backend;

/// <summary>Removal validation for source-backed GDAL raster map nodes.</summary>
public static partial class MapTreeService
{
    private static string ResolveRasterMapLayerSource(OperationContext context,
        string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The raster source path must be absolute.");
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The raster source revision is required.");

        string path = Path.GetFullPath(sourcePath);
        string canonical = CanonicalPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The raster source was not found.", path);

        string projectDirectory = CanonicalPath(Path.GetDirectoryName(context.ProjectAliasPath)!);
        if (!IsWithinDirectory(canonical, projectDirectory) && !IsExternalConnectionFile(context, canonical))
            throw new ArgumentException("The raster source is outside the current project connections.");

        bool matchesManifest;
        try
        {
            matchesManifest = MapRasterLayerSourceManifest.Matches(canonical, sourceRevision);
        }
        catch (Exception exception) when (IsLegacyTiff(path) &&
            exception is IOException or NotSupportedException or ArgumentException)
        {
            // Older TIFF-only callers supply the root-file SHA. Keep that
            // compatibility path while new map layers use the full GDAL file
            // list manifest, including VRT dependencies.
            matchesManifest = false;
        }

        if (!matchesManifest && IsLegacyTiff(path))
            matchesManifest = RevisionFile(canonical).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase);
        if (!matchesManifest) throw new SourceFileChangedException();
        return canonical;
    }

    private static bool IsLegacyTiff(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }
}
