namespace Ras.Backend;

public sealed record GeometryMeshModelRequest(string AssetPath, string Kind, string? ProjectPath,
    string? ProjectXml = null, CoreLayerRequestContext? CoreLayerContext = null);
public sealed record GeometryMeshLoadRequest(GeometryMeshModelRequest Model, string? Revision = null,
    string? DraftState = null, string? TopologyDraftState = null, int[]? SelectedRegionIDs = null);
public sealed record GeometryMeshEditRequest(GeometryMeshModelRequest Model, string Revision, string DraftState,
    string? TopologyDraftState, int[]? SelectedRegionIDs, GeometryMeshMutation Mutation);
public sealed record GeometryMeshDegreeScanRequest(GeometryMeshModelRequest Model, string Revision,
    string DraftState, string? TopologyDraftState, int MaxFacesPerNode,
    bool IncludeIncidentCells = false);
public sealed record GeometryMeshPropertyTablesRequest(GeometryMeshModelRequest Model, string Revision,
    string DraftState, string? TopologyDraftState, int[]? SelectedRegionIDs);
public sealed record GeometryMeshPropertyTablesResult(bool Succeeded, bool Cancelled,
    string[] Messages, GeometryMeshSnapshot Snapshot);
public sealed record GeometryMeshGenerateRequest(GeometryMeshModelRequest Model, string Revision,
    string DraftState, string? TopologyDraftState, int[]? SelectedRegionIDs, string OperationID);
public sealed record GeometryMeshGenerateResult(string OperationID, string Revision, bool Cancelled,
    GeometryMeshSnapshot Snapshot);

/// <summary>
/// Operations are replayed against a fresh in-memory source model. Flow-area
/// row identities are HDF feature-row ordinals scoped to the source revision;
/// region identities are the source topology control-point FIDs.
/// </summary>
public sealed record GeometryMeshMutation(string Operation, string[]? FlowAreaIDs = null,
    string[]? ExpectedFlowAreaNames = null, int[]? FeatureIDs = null, string? FieldID = null,
    string? Value = null, string? Axis = null, string? ExpectedMeshFingerprint = null,
    string? GeneratedMeshPayload = null, GeometryMeshGenerationReport? GeneratedReport = null,
    string? OperationID = null);

public sealed record GeometryMeshSnapshot(string Revision, string DraftState, string TopologyFingerprint,
    bool CanEdit, bool IsDirty, GeometryMeshFlowAreaRow[] FlowAreas,
    GeometryTopologyFeatureTable Regions, int[] SelectedRegionIDs,
    GeometryMeshPatchSizing? PatchSizing, GeometryMeshGenerationReport? GenerationReport,
    GeometryMeshPrepareSummary? PrepareSummary, GeometryMeshMapData? Map = null, bool PreparePending = false,
    GeometryMeshDiagnosticHistoryEvent? DiagnosticHistoryEvent = null);

public sealed record GeometryMeshFlowAreaRow(string ID, int RowIndex, string Name, double? TargetedCellSpacing,
    bool HasMesh, int CellCount, int FaceCount, int NodeCount, GeometryMeshExtent? Extent,
    bool HasMeshErrors, bool HasPropertyTables, bool PropertyTablesQueuedForParentSave,
    bool PropertyTablesPinned, GeometryMeshMetadataItem[] Metadata);

public sealed record GeometryMeshMetadataItem(string ID, string Title, string Value);

public sealed record GeometryMeshExtent(double MinX, double MinY, double MaxX, double MaxY);
/// <summary>
/// A bounded source-coordinate preview. FlowAreaID is the source flow-area
/// row ordinal scoped to Revision; CellIndex is the original mesh-local cell
/// ordinal. These values are not HDF FIDs. OmittedCellCount includes cells
/// outside the preview budget and sampled candidates rejected for invalid
/// geometry; InvalidCandidateCount reports the latter among examined samples.
/// </summary>
public sealed record GeometryMeshMapData(GeometryTopologyExtent? Extent, GeometryTopologyMap Topology,
    GeometryMeshMapCell[] Cells, int TotalCellCount, int RenderedCellCount, int OmittedCellCount,
    int InvalidCandidateCount, int CellLimit, int VertexLimit);
public sealed record GeometryMeshMapCell(string ID, string FlowAreaID, string FlowAreaName,
    int MeshIndex, int CellIndex, GeometryTopologyCoordinate[] Coordinates);
public sealed record GeometryMeshDegreeScan(int MaxFacesPerNode, GeometryMeshDegreeIssue[] Issues);
public sealed record GeometryMeshDegreeIssue(string ID, string FlowAreaID, string FlowAreaName,
    int MeshIndex, int NodeIndex, int FaceCount, double? X, double? Y, GeometryMeshExtent? CellsExtent,
    GeometryMeshDegreeCell[] Cells);
public sealed record GeometryMeshDegreeCell(int CellIndex, GeometryTopologyCoordinate[] Coordinates);

public sealed record GeometryMeshPatchSizing(string State, string? Kind, string Message,
    int[] SelectedRegionIDs, GeometryMeshPatchDimension[] Dimensions);
public sealed record GeometryMeshPatchDimension(string ID, string Title, int ArcCount,
    string? SizeFunction, bool SizeFunctionHasMismatch, double? LiveValue, bool LiveValueHasMismatch,
    bool CanResize, bool CanApply, bool CanChangeMode, GeometryMeshPatchArcSelection? SideArcSelection = null,
    GeometryMeshPatchSideArcIssue[]? SideArcIssues = null);
/// <summary>
/// Exact source-arc membership for one Quad side dimension. Geometry is not
/// duplicated here: callers join these source FIDs to the arc paths already
/// present in the same revision's GeometryMeshMapData topology snapshot.
/// </summary>
public sealed record GeometryMeshPatchArcSelection(string SourceRevision, string TopologyFingerprint,
    int[] SelectedRegionIDs, string Axis, int[] ArcFeatureIDs);
/// <summary>
/// A source-side derivation failure for one selected control-point region.
/// The issue travels with the same mesh snapshot so the UI cannot present a
/// partial arc set as a complete side or enable a mutation against it.
/// </summary>
public sealed record GeometryMeshPatchSideArcIssue(int RegionFID, string Axis, string Code, string Message);

public sealed record GeometryMeshGenerationReport(bool Succeeded, bool Partial, bool MeshWasReplaced,
    int? CellCount, string[] Messages);

/// <summary>
/// A transient source OperationResult from one explicit BuildConceptualMesh
/// invocation. This is distinct from the current mesh-generation summary and
/// is never serialized into the geometry mesh draft or HDF.
/// </summary>
public sealed record GeometryMeshDiagnosticHistoryEvent(string OperationID, bool Result,
    GeometryMeshDiagnosticMessage[] Messages, GeometryTopologyMap Topology);
public sealed record GeometryMeshDiagnosticMessage(string ID, string Code, string Severity,
    string EvaluatedMessage, GeometryMeshDiagnosticValue[] Values);
public sealed record GeometryMeshDiagnosticValue(string ID, string Key, string Value,
    string[] SelectionIDs, string? TargetFeatureID, GeometryTopologyCoordinate? Coordinate);

public sealed record GeometryMeshPrepareSummary(bool? Succeeded, string? FailureReason,
    string? SoftwareVersion, string? StartTime, double? TotalDurationSeconds, string? PrepareLog,
    string? EndTime = null, double? PropertyTableDurationSeconds = null);

/// <summary>
/// Opaque child draft sent as part of the parent ModelSaveRequest. The topology
/// fingerprint binds patch edits and generated meshes to the exact in-memory
/// topology against which they were previewed.
/// </summary>
public sealed record GeometryMeshDraftState(string AssetPath, string Revision,
    string TopologyFingerprint, GeometryMeshMutation[] Mutations);
