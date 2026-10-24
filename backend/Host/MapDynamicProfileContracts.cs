namespace Ras.Backend;

/// <summary>
/// Samples the active terrain and Result contexts for arbitrary source map
/// polylines using the retained DynamicProfilePlot sampling behavior.
/// </summary>
public sealed record MapDynamicProfileRequest(
    MapTreeRequest Map,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    MapDynamicProfileLine[] Lines,
    string LinesToken,
    int TargetSampleCount = 256,
    bool PlotTerrains = true,
    bool PlotResults = true,
    bool TerrainRequiresElevationResult = true,
    string? MapType = null,
    MapDynamicProfileSelection[]? ProfileSelections = null,
    LinkedResultProfilesRequest? LinkedResults = null,
    string LinkedFrameMode = "Profile");

/// <summary>A source polyline supplied by the active geometry/map selection.</summary>
public sealed record MapDynamicProfileLine(
    string Id,
    GeometryTopologyCoordinate[] Points,
    MapResultFeatureIdentity? Feature = null);

/// <summary>
/// The exact native profile index currently selected for an active map node.
/// MultiResult child Results are keyed to selection slot zero by source time.
/// </summary>
public sealed record MapDynamicProfileSelection(
    CatalogTreeNodeLocator Node,
    int ProfileIndex);

public sealed record MapDynamicProfileSeries(
    string Id,
    string LineId,
    string SourceKind,
    string SourcePath,
    string SourceRevision,
    string Name,
    string? MapType,
    int? ProfileIndex,
    string? Time,
    string Units,
    double?[] Samples,
    string? TerrainPath = null,
    string? TerrainRevision = null,
    string? UnavailableReason = null);

public sealed record MapDynamicProfileLineResult(
    string Id,
    GeometryTopologyCoordinate[] Points,
    GeometryTopologyCoordinate[] SamplePoints,
    double[] Distances,
    MapDynamicProfileSeries[] Series);

/// <summary>
/// Read-only dynamic profile response tied to one exact project/map/result
/// context and the exact UI polyline snapshot identified by LinesToken.
/// </summary>
public sealed record MapDynamicProfileResult(
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string DistanceUnits,
    string ContextToken,
    string LinesToken,
    int TargetSampleCount,
    MapDynamicProfileLineResult[] Lines);
