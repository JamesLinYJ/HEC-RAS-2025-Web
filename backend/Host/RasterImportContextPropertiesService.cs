using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Geospatial.GDALAssist;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Read-only equivalent of RasterInfoHelpers.GetRasterInfo and
/// RasterStatsHelpers.GetRasterStats for the native import property contexts.
/// It returns DTO data only; it never mutates the selected source files.
/// </summary>
internal static class RasterImportContextPropertiesService
{
    private const int MaximumSources = 256;
    private const int MaximumProjectionWKTLength = 1_000_000;

    internal static RasterImportContextPropertiesSnapshot Inspect(
        RasterImportContextPropertiesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FilePaths is null || request.FilePaths.Length is < 1 or > MaximumSources)
            throw new ArgumentException("Choose between one and 256 raster files.");
        if (request.CompareProjectionWKT?.Length > MaximumProjectionWKTLength)
            throw new ArgumentOutOfRangeException(nameof(request.CompareProjectionWKT),
                "The comparison projection is too large.");

        Projection? comparisonProjection = string.IsNullOrWhiteSpace(request.CompareProjectionWKT)
            ? null : new WKTProjection(request.CompareProjectionWKT);
        string[] canonicalPaths = request.FilePaths.Select(path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("Every raster file path must be absolute.");
            return SourceFilePaths.Canonical(path);
        }).ToArray();
        if (canonicalPaths.Distinct(PathComparer).Count() != canonicalPaths.Length)
            throw new ArgumentException("The selected raster files contain duplicate source paths.");

        return new RasterImportContextPropertiesSnapshot(request.FilePaths.Select((path, index) =>
            InspectOne(canonicalPaths[index], comparisonProjection)).ToArray());
    }

    private static RasterImportContextSource InspectOne(string path, Projection? comparisonProjection)
    {
        if (!File.Exists(path))
            return new RasterImportContextSource(path, "missing", null, null,
                "The selected raster file was not found.");

        string revision = HashSource(path);
        RasterImportContextInfo? info = null;
        RasterImportContextStats? stats = null;
        string? error = null;
        try
        {
            using var raster = new GDALRaster(path);
            long uncompressedBytes = UncompressedByteCount(raster);
            long fileLength = new FileInfo(path).Length;
            if (fileLength <= 0) throw new InvalidDataException("The selected raster file is empty.");
            List<string> overviews = raster.GetPyramidLevels(1)
                .Select(level => level.Width + "x" + level.Height).ToList();
            Projection? projection = raster.GetProjection();
            string friendlyProjection = projection is null
                ? "None"
                : comparisonProjection is null
                    ? "Has Projection"
                    : projection.IsEqual(comparisonProjection) ? "Matches Local" : "Differs from Local";
            string? unitSystem = projection is not null && projection.TryParseUnitSystem(out var units)
                ? units.ToString() : null;
            info = new RasterImportContextInfo(
                Path.GetFileName(path), Path.GetDirectoryName(path),
                projection?.GetPrettyWKT() ?? "(No Projection)", friendlyProjection,
                overviews.Count, overviews.ToArray(),
                raster.GetCellSizeX().ToString("F2", CultureInfo.CurrentCulture),
                FileHelpers.PrettyByteCount(fileLength), FileHelpers.PrettyByteCount(uncompressedBytes),
                Math.Round((double)uncompressedBytes / fileLength, 1).ToString(CultureInfo.CurrentCulture) + "x",
                raster.DriverLongName, raster.GetBandCount(), unitSystem);

            raster.ReadStats(1, out double min, out double max, out double mean, out double standardDeviation);
            stats = new RasterImportContextStats(
                min.ToString(CultureInfo.CurrentCulture), max.ToString(CultureInfo.CurrentCulture),
                mean.ToString(CultureInfo.CurrentCulture), standardDeviation.ToString(CultureInfo.CurrentCulture));
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        string finalRevision = HashSource(path);
        if (!finalRevision.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return new RasterImportContextSource(path, revision, info, stats, error);
    }

    private static long UncompressedByteCount(GDALRaster raster)
    {
        Type? dataType = raster.GetDataType();
        int itemSize = dataType is null ? 4 : Marshal.SizeOf(dataType);
        return checked(checked((long)raster.PixelsTall * raster.PixelsWide) * itemSize);
    }

    private static string HashSource(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
