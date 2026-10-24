namespace Ras.Backend;

/// <summary>
/// The origin for a mesh-element time series is either an exact Geometry
/// child draft or an exact Result slot. Both are session-scoped and are
/// resolved only against the same retained map-source snapshot.
/// </summary>
public sealed record MapResultMeshGeometryOrigin(
    string ProjectSessionID,
    string AssetID,
    GeometryMeshModelRequest Model,
    string Revision,
    string DraftState,
    string? TopologyDraftState,
    string MeshDraftSHA256,
    string TopologyDraftSHA256);

public sealed record MapResultMeshResultOrigin(
    CatalogTreeNodeLocator ResultNode,
    int SelectionIndex,
    string SelectedResultPath,
    string SelectedResultRevision);

public sealed record MapResultMeshTimeSeriesOwner(
    MapTreeRequest Map,
    string ProjectSessionID,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    MapResultMeshGeometryOrigin? GeometryOrigin,
    MapResultMeshResultOrigin? ResultOrigin);

public sealed record MapResultMeshTimeSeriesInspectRequest(MapResultMeshTimeSeriesOwner Owner);

public sealed record MapResultMeshTimeSeriesExecuteRequest(
    string OperationID,
    MapResultMeshTimeSeriesOwner Owner,
    int FlowAreaIndex,
    string FlowAreaID,
    string ExpectedFlowAreaName,
    string ElementKind,
    int ElementIndex);

public sealed record MapResultMeshFlowAreaChoice(
    int FlowAreaIndex,
    string FlowAreaID,
    string Name,
    int MeshHash,
    int CellCount,
    int FaceCount,
    bool HasMesh);

public sealed record MapResultMeshTimeSeriesInspectResult(
    string State,
    string? Message,
    string ProjectSessionID,
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
    string OriginKind,
    string OriginPath,
    string OriginRevision,
    string OriginMeshDraftSHA256,
    string OriginTopologyDraftSHA256,
    CatalogTreeNodeLocator? OriginResultNode,
    int OriginSelectionIndex,
    string[] MatchingResultNames,
    MapResultMeshFlowAreaChoice[] FlowAreas,
    int SkippedComputingResultCount);

public sealed record MapResultMeshTimeSeriesSeries(
    CatalogTreeNodeLocator[] Owners,
    int[] SelectionIndices,
    string ResultPath,
    string ResultRevision,
    string ResultName,
    int FlowAreaIndex,
    string FlowAreaName,
    int MeshHash,
    string Variable,
    string[] Times,
    double[] Values);

public sealed record MapResultMeshTimeSeriesResult(
    string OperationID,
    string State,
    string? Message,
    string ProjectSessionID,
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
    string OriginKind,
    string OriginPath,
    string OriginRevision,
    string OriginMeshDraftSHA256,
    string OriginTopologyDraftSHA256,
    CatalogTreeNodeLocator? OriginResultNode,
    int OriginSelectionIndex,
    int FlowAreaIndex,
    string FlowAreaID,
    string FlowAreaName,
    int MeshHash,
    string ElementKind,
    int ElementIndex,
    string Variable,
    string Units,
    MapResultMeshTimeSeriesSeries[] Series,
    int SkippedComputingResultCount);
