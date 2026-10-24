namespace Ras.Backend;

public sealed record GeometryMeshElementFindRequest(
    GeometryMeshModelRequest Model,
    string Revision,
    string DraftState,
    string? TopologyDraftState,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string FlowAreaID,
    string ExpectedFlowAreaName,
    string ElementKind,
    int Index);

public sealed record GeometryMeshElementFindResult(
    string CanonicalAssetPath,
    string SourceRevision,
    string FlowAreaID,
    string FlowAreaName,
    string ElementKind,
    int Index,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    double X,
    double Y,
    GeometryMeshExtent Viewport,
    GeometryTopologyCoordinate[] Coordinates);

public sealed record GeometryMeshShapefileExportRequest(
    string OperationID,
    GeometryMeshModelRequest Model,
    string Revision,
    string DraftState,
    string? TopologyDraftState,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string FlowAreaID,
    string ExpectedFlowAreaName,
    string ExportKind,
    string OutputPath);

public sealed record GeometryMeshShapefileExportResult(
    string OperationID,
    string State,
    string CanonicalAssetPath,
    string SourceRevision,
    string FlowAreaID,
    string FlowAreaName,
    string ExportKind,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string? OutputPath,
    int FeatureCount,
    string? ProjectionWKT,
    ExportArtifactReceipt[] Artifacts);
