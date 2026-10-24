using Geospatial.Features;
using Geospatial.IO;
using Utility.Logging;

namespace Ras.Backend;

/// <summary>
/// Reads and captures the exact file family retained by a Shapefile map layer.
/// Geometry and attributes remain in their source files; this service never
/// rewrites, reprojects, or flattens them while adding a map-tree row.
/// </summary>
internal static class MapShapefileImportService
{
    private const string FamilyName = "shapefile";

    public static MapSourceFamilyCapture CaptureFamily(string sourcePath)
    {
        string path = ValidateSourcePath(sourcePath);
        MapSourceFamilyCapture capture = MapSourceIdentity.CaptureFamily(FamilyName, path,
        [
            new(ShapefileIO.SHXFilename(path), "shx"),
            new(ShapefileIO.DBFFilename(path), "dbf"),
            new(ShapefileIO.PRJFilename(path), "prj", Required: false)
        ]);
        ValidateExpectedFamily(capture, path);
        MapSourceIdentity.VerifyFamily(capture);
        _ = ReadHeader(path);
        MapSourceIdentity.VerifyFamily(capture);
        return capture;
    }

    public static string ValidateSourcePath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected Shapefile must use an absolute path.", nameof(sourcePath));
        string path = Path.GetFullPath(sourcePath);
        if (!string.Equals(Path.GetExtension(path), ".shp", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Choose a Shapefile (.shp) source file.");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected Shapefile was not found.", path);
        return path;
    }

    public static void VerifyExpectedFamily(MapSourceFamilyCapture expected, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(expected);
        string path = ValidateSourcePath(sourcePath);
        ValidateExpectedFamily(expected, path);
        MapSourceIdentity.VerifyFamily(expected);
    }

    public static MapShapefileSourceReadResult Read(string sourcePath)
    {
        string path = ValidateSourcePath(sourcePath);
        ShapefileHeader header = ReadHeader(path);

        // These retained classifiers define the exact source geometry families
        // supported by the original map layer importer. Multipoint, multipatch,
        // null, and unknown types are rejected instead of being coerced.
        if (ShapefileIO.IsPointShapefile(path))
        {
            OperationResult operation = ShapefileIO.TryRead(path, out PointFeatureCollection collection);
            if (!operation.Result || collection is null)
                throw ReadFailure(path, operation);
            return new("PointsVector", "Point", header.ShpTypeCode, collection.Count);
        }
        if (ShapefileIO.IsPolylineShapefile(path))
        {
            OperationResult operation = ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection collection);
            if (!operation.Result || collection is null)
                throw ReadFailure(path, operation);
            return new("PolylinesVector", "Polyline", header.ShpTypeCode, collection.Count);
        }
        if (ShapefileIO.IsPolygonShapefile(path))
        {
            OperationResult operation = ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection collection);
            if (!operation.Result || collection is null)
                throw ReadFailure(path, operation);
            return new("PolygonsVector", "Polygon", header.ShpTypeCode, collection.Count);
        }

        throw new NotSupportedException($"The Shapefile shape type {header.ShpTypeCode} is not supported by map layers.");
    }

    private static ShapefileHeader ReadHeader(string path)
    {
        OperationResult operation = ShapefileIO.TryParseShapefileHeader(path, out ShapefileHeader header);
        if (!operation.Result || header is null)
            throw ReadFailure(path, operation);
        bool point = (header.ShpTypeCode is 1 or 11 or 21) && ShapefileIO.IsPointShapefile(path);
        bool polyline = (header.ShpTypeCode is 3 or 13 or 23) && ShapefileIO.IsPolylineShapefile(path);
        bool polygon = (header.ShpTypeCode is 5 or 15 or 25) && ShapefileIO.IsPolygonShapefile(path);
        if (!point && !polyline && !polygon)
            throw new NotSupportedException($"The Shapefile shape type {header.ShpTypeCode} is not supported by map layers.");
        return header;
    }

    private static InvalidDataException ReadFailure(string path, OperationResult operation)
    {
        string detail = operation.GetConcatenatedMessages();
        return new InvalidDataException(string.IsNullOrWhiteSpace(detail)
            ? $"The Shapefile source reader could not read '{Path.GetFileName(path)}'."
            : detail);
    }

    private static void ValidateExpectedFamily(MapSourceFamilyCapture capture, string sourcePath)
    {
        if (!string.Equals(capture.Family, FamilyName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(capture.SourcePath) || !IsSHA256(capture.Revision) ||
            capture.Files is null || capture.Files.Length != 4 || capture.Files.Any(file =>
                file is null || string.IsNullOrWhiteSpace(file.InputPath) ||
                string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Role)) ||
            !SourceFilePaths.SameSourceFile(capture.SourcePath, sourcePath))
            throw new SourceFileChangedException();

        var expectedPaths = new Dictionary<string, (string Path, bool Required)>(StringComparer.Ordinal)
        {
            ["source"] = (sourcePath, true),
            ["shx"] = (ShapefileIO.SHXFilename(sourcePath), true),
            ["dbf"] = (ShapefileIO.DBFFilename(sourcePath), true),
            ["prj"] = (ShapefileIO.PRJFilename(sourcePath), false)
        };
        if (capture.Files.Select(file => file.Role).Distinct(StringComparer.Ordinal).Count() != expectedPaths.Count)
            throw new SourceFileChangedException();

        foreach (MapSourceFileRevision file in capture.Files)
        {
            if (!expectedPaths.TryGetValue(file.Role, out var expected) ||
                !SourceFilePaths.SameSourceFile(file.InputPath, expected.Path) ||
                !SourceFilePaths.SameSourceFile(file.Path, expected.Path) ||
                file.Required != expected.Required || file.Exists != (file.Revision is not null) ||
                (file.Required && !file.Exists) ||
                (file.Revision is not null && !IsSHA256(file.Revision)))
                throw new SourceFileChangedException();
        }
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
}
