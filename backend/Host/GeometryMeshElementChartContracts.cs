namespace Ras.Backend;

/// <summary>
/// Reads one original FlowAreaLayer property table from an exact Geometry
/// child draft. If that draft contains a successful ComputePropertyTables
/// intent, the source algorithm is replayed in a temporary ModelContext so
/// the result can be plotted before the parent Geometry is saved.
/// </summary>
public sealed record GeometryMeshElementPropertyChartRequest(
    string OperationID,
    string ProjectSessionID,
    string AssetID,
    GeometryMeshModelRequest Model,
    string Revision,
    string DraftState,
    string? TopologyDraftState,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string FlowAreaID,
    string ExpectedFlowAreaName,
    string ElementKind,
    int ElementIndex);

public sealed record GeometryMeshElementPropertyChartPoint(double X, double Y);

public sealed record GeometryMeshElementPropertyChartResult(
    string OperationID,
    string State,
    string? Message,
    string ProjectSessionID,
    string AssetID,
    string CanonicalAssetPath,
    string SourceRevision,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string FlowAreaID,
    string FlowAreaName,
    int FlowAreaIndex,
    int MeshHash,
    string ElementKind,
    int ElementIndex,
    string XQuantity,
    string XUnits,
    string YQuantity,
    string YUnits,
    GeometryMeshElementPropertyChartPoint[] Points);
