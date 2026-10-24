namespace Ras.Backend;

/// <summary>
/// Exact source capture for the native counterparts of the original model
/// vector hover callbacks. The renderer kind is a closed source-layer choice;
/// it is never inferred from a visible name.
/// </summary>
public sealed record MapHoverModelVectorCapture(
    CatalogTreeNodeLocator Node,
    string RendererKind,
    string SourcePath,
    string SourceRevision,
    string OwnerMode,
    CatalogTreeNodeLocator? GeometryNode = null,
    string? GeometryContextToken = null,
    CatalogTreeNodeLocator[]? PlanNodes = null,
    MapResultGeometryOwner? ResultOwner = null,
    ModelRequest? Model = null,
    string? CulvertDraftState = null,
    string? CulvertDraftSHA256 = null,
    string? BoundarySourceState = null,
    string? BoundaryContextToken = null,
    MapHoverModelVectorLine[]? BoundaryLines = null);

/// <summary>
/// The current line geometry painted by the Boundary map. Sending this with
/// the editor state makes the backend prove that its re-opened source geometry
/// is the geometry associated with the visible line capture.
/// </summary>
public sealed record MapHoverModelVectorLine(
    string ID,
    string Name,
    int SourceOrdinal,
    MapHoverCoordinate[] Coordinates);
