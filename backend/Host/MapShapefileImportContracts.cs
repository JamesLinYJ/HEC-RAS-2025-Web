namespace Ras.Backend;

/// <summary>Read-only source selection used to capture an exact Shapefile family.</summary>
public sealed record MapShapefileFamilyCaptureRequest(string SourcePath);

/// <summary>Persists one selected-format Shapefile in the exact owning map tree.</summary>
public sealed record MapShapefileVectorImportRequest(
    MapDocumentRequest Map,
    string SourcePath,
    string ExpectedProjectRevision,
    MapSourceFamilyCapture ExpectedFamily);

/// <summary>Result of validating a Shapefile with its retained typed source reader.</summary>
internal sealed record MapShapefileSourceReadResult(
    string MapType,
    string ShapeType,
    int ShapeTypeCode,
    int FeatureCount);
