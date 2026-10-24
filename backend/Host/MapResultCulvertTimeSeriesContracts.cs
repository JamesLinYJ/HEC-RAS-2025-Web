namespace Ras.Backend;

/// <summary>
/// Reads exact stored culvert output from one Result/MultiResult slot-zero
/// owner. Barrel IDs are source-ordinal identities, never display names.
/// </summary>
public sealed record MapResultCulvertTimeSeriesRequest(
    MapTreeRequest Map,
    MapResultGeometryOwner ResultOwner,
    string[] BarrelIDs,
    string Variable);

public sealed record MapResultCulvertTimeSeriesDataset(
    string ID,
    string Name,
    int SourceOrdinal,
    string ColorRGBA,
    double StrokeWidth,
    double[] Values);

public sealed record MapResultCulvertTimeSeriesResult(
    string ProjectCanonicalPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string CoreLayerContextIdentity,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    CatalogTreeNodeLocator ResultNode,
    int SelectionIndex,
    string SelectedResultPath,
    string SelectedResultRevision,
    string Variable,
    string Units,
    string[] Times,
    double NoDataValue,
    MapResultCulvertTimeSeriesDataset[] Datasets);
