namespace Ras.Backend;

/// <summary>
/// Source-backed Flow/Stage chart request for exact lines in the selected
/// Result's embedded BoundaryCondition. It intentionally does not accept a
/// drawn/sample line: the retained Result methods consume the source BCL.
/// </summary>
public sealed record MapBoundaryLineTimeSeriesRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator BoundaryNode,
    string ExpectedBoundaryRevision,
    string BoundarySourceState,
    string ExpectedBoundaryContextToken,
    MapResultGeometryOwner ResultOwner,
    string[] LineIDs,
    string Variable);

public sealed record MapBoundaryLineTimeSeriesPoint(string Time, double? Value);

public sealed record MapBoundaryLineTimeSeriesDataset(
    string ID,
    string Name,
    string Kind,
    int SourceOrdinal,
    string ColorRGBA,
    double StrokeWidth,
    MapBoundaryLineTimeSeriesPoint[] Points);

public sealed record MapBoundaryLineTimeSeriesResult(
    string ProjectCanonicalPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ResultContextToken,
    string BoundaryContextToken,
    CatalogTreeNodeLocator BoundaryNode,
    string BoundaryPath,
    string BoundaryRevision,
    MapResultOwnerIdentity ResultOwner,
    string Variable,
    MapBoundaryLineTimeSeriesDataset[] Datasets);
