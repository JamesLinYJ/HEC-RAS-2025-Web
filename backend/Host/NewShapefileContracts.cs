namespace Ras.Backend;

/// <summary>
/// Creates one empty Shapefile for the exact map the user selected. The map
/// context and projection choice are captured before opening the native form;
/// no server-side default can silently redirect the new layer.
/// </summary>
public sealed record NewShapefileCreateRequest(
    MapDocumentRequest Map,
    string ExpectedProjectRevision,
    string ProjectSessionID,
    string MapID,
    string OperationID,
    string DestinationPath,
    string ShapeType,
    string ProjectionChoice);

/// <summary>
/// A durable-in-session receipt for the four-file source family. Import can be
/// retried from this receipt without running the writer a second time.
/// </summary>
public sealed record NewShapefileCreateReceipt(
    string OperationID,
    string ProjectSessionID,
    string MapID,
    string ProjectPath,
    string ProjectRevision,
    string? VisualizationRevision,
    int? MapIndex,
    string? MapName,
    string MapType,
    string DestinationPath,
    string ShapeType,
    string ProjectionChoice,
    string? ProjectionWKT,
    MapSourceFamilyCapture Family,
    string CreatedUtc);
