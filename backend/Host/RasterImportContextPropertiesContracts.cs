namespace Ras.Backend;

/// <summary>Read-only inspection request for the source raster property contexts.</summary>
public sealed record RasterImportContextPropertiesRequest(
    string[] FilePaths,
    string? CompareProjectionWKT = null);

public sealed record RasterImportContextInfo(
    string? FileName,
    string? Directory,
    string? RasterProjection,
    string? ProjectionFriendlyText,
    double? OverviewsCount,
    string[]? Overviews,
    string? CellSize,
    string? Size,
    string? UncompressedSize,
    string? CompressionRatio,
    string? Driver,
    int? BandCount,
    string? UnitSystem);

/// <summary>
/// Statistic values are strings to preserve the source control's current-culture
/// `Double.ToString()` presentation, including source NaN/Infinity values.
/// </summary>
public sealed record RasterImportContextStats(
    string Min,
    string Max,
    string Mean,
    string StandardDeviation);

public sealed record RasterImportContextSource(
    string Path,
    string Revision,
    RasterImportContextInfo? Info,
    RasterImportContextStats? Stats,
    string? Error);

public sealed record RasterImportContextPropertiesSnapshot(
    RasterImportContextSource[] Sources);
