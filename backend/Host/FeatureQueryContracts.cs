namespace Ras.Backend;

/// <summary>Loads query polygon choices for one exact saved project map.</summary>
public sealed record FeatureQuerySnapshotRequest(MapTreeRequest Map);

public sealed record FeatureQueryPolygonSource(string Id, string Name);

public sealed record FeatureQuerySnapshotResponse(
    MapSourceMapCapture Map,
    FeatureQueryPolygonSource[] PolygonSources,
    string? SelectedPolygonId);

/// <summary>Project-world bounds and source selector fields used by the original queriers.</summary>
public sealed record FeatureQueryExtent(double MinX, double MinY, double MaxX, double MaxY);

public sealed record FeatureQueryQueryRequest(
    MapTreeRequest Map,
    string Kind,
    FeatureQueryExtent? Extent,
    string? NldLayerId,
    bool UseBoundaryPolygon,
    string? PolygonSourceId,
    string? RawQl,
    string? QueryLabel);

/// <summary>A real query layer represented by a persistent GeoJSON preview source.</summary>
public sealed record FeatureQueryPreviewLayer(
    string Name,
    string SourcePath,
    string SourceRevision,
    string MapType,
    string LineColor,
    string FillColor,
    double LineWidth,
    string CoordinateSpace = "ProjectWorld");

public sealed record FeatureQueryQueryResponse(
    MapSourceMapCapture Map,
    string Kind,
    string ResultId,
    bool HasFeatures,
    string Info,
    FeatureQueryPreviewLayer[] Layers);

/// <summary>
/// The native file picker supplies OutputDirectory. ResultId resolves only to a
/// backend-owned staging manifest tied to the same captured map and real query.
/// </summary>
public sealed record FeatureQuerySaveRequest(
    MapTreeRequest Map,
    string ResultId,
    string FormatId,
    string OutputDirectory);

/// <summary>A vector file exported by the retained querier workflow.</summary>
public sealed record FeatureQueryExportedSource(
    string Path,
    string Revision,
    string Name,
    string MapType,
    int FeatureCount,
    string LineColor,
    string FillColor,
    double LineWidth,
    MapSourceFamilyCapture? Family = null);

public sealed record FeatureQuerySaveResponse(
    MapSourceMapCapture Map,
    FeatureQueryExportedSource[] Sources,
    string Info,
    string[]? Warnings = null);

public sealed record FeatureQuerySuggestionRequest(string Key, string Value, bool IsKey);

public sealed record FeatureQuerySuggestionResponse(string[] Suggestions);
