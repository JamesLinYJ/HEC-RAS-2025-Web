using Geospatial.Vectors;

namespace Ras.Backend;

/// <summary>
/// Exact, saved-map FlowAreaLayer properties request. SourcePath/SourceRevision
/// identify the Geometry HDF or selected Result HDF whose embedded Geometry
/// owns the mesh; they are never resolved by display name.
/// </summary>
public sealed record MapTreeMeshErrorRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator FlowAreaNode,
    string ProjectSessionID,
    string MapWorkflowIdentity,
    string ContextToken,
    string SourcePath,
    string SourceRevision,
    MapResultGeometryOwner? ResultOwner,
    string Action,
    bool ReadSettingsOnly = false,
    bool? CheckMeshErrors = null,
    double? MaxFacesPerNodeEntry = null,
    bool SourceTreeCapture = false,
    CatalogTreeNodeLocator? SourceTreeGeometryNode = null,
    string? SourceTreeResultSourcesContextToken = null);

public sealed record MapTreeMeshErrorCoordinate(double X, double Y);

public sealed record MapTreeMeshErrorCell(int CellIndex, MapTreeMeshErrorCoordinate[] Coordinates);

public sealed record MapTreeMeshErrorIssue(
    string ID,
    int MeshIndex,
    int NodeIndex,
    int FaceCount,
    MapTreeMeshErrorCoordinate Node,
    double? CellsMinX,
    double? CellsMinY,
    double? CellsMaxX,
    double? CellsMaxY,
    MapTreeMeshErrorCell[] Cells);

public sealed record MapTreeMeshErrorSnapshot(
    string ProjectSessionID,
    string MapWorkflowIdentity,
    string ContextToken,
    string ProjectCanonicalPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    CatalogTreeNodeLocator FlowAreaNode,
    string SourcePath,
    string SourceRevision,
    bool CheckMeshErrors,
    int MaxFacesPerNode,
    bool? IsChecked,
    bool? IsEffectivelyVisible,
    string Status,
    string? Diagnostic,
    MapTreeMeshErrorIssue[] Issues,
    int TotalIssueCount,
    int OmittedIssueCount,
    string? PreviousVisualizationRevision = null);
