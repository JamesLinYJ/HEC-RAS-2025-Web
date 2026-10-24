namespace Ras.Backend;

/// <summary>Exact saved map-tree node and HDF identity for one source raster style.</summary>
public sealed record AssociatedRasterStyleRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string SourcePath,
    string SourceRevision,
    string? ContextToken = null,
    CatalogTreeNodeLocator[]? GeometryNodes = null,
    CatalogTreeNodeLocator[]? PlanNodes = null,
    MapResultGeometryOwner? ResultOwner = null);

public sealed record AssociatedRasterStyleColorStop(int Index, string Color, double Value);

/// <summary>
/// Editable values backed by one retained RasterVisualContext. The JSON names
/// mirror the serialized .vis attributes and the retained renderer properties.
/// </summary>
public sealed record AssociatedRasterStyleDraft(
    string? PaletteID,
    string Name,
    AssociatedRasterStyleColorStop[] Stops,
    string ValueSource,
    bool IsEditing,
    bool IsSurfaceFillEnabled,
    int Alpha,
    string InterpolationMode,
    double SmoothingRadius,
    double ResampleCellSizeRatio,
    bool HillshadeEnabled,
    double HillshadeZFactor,
    double HillshadeAzimuth,
    double HillshadeZenith,
    bool ContoursEnabled,
    bool ContoursAuto,
    double ContourInterval,
    int ContourIndexEvery,
    int ContourColorArgb,
    int ContourOpacity,
    bool ContourShowLabels,
    double ContourDensity);

public sealed record AssociatedRasterStylePalette(
    string Id,
    string Name,
    string[] Colors,
    double[]? Values = null,
    bool IsCustom = false,
    string ValueSource = "Manual");

/// <summary>
/// Source files whose bytes affect the current style's derived ranges. The
/// source HDF is also returned here; Terrain input rasters and an NValue's
/// exact Classification HDF/input rasters are included when applicable.
/// </summary>
public sealed record AssociatedRasterStyleDependency(string Path, string Revision, string Role);

public sealed record AssociatedRasterStyleSnapshot(
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ProjectCanonicalPath,
    string ProjectXmlSHA,
    CatalogTreeNodeLocator Node,
    CatalogTreeNodeLocator[] GeometryNodes,
    CatalogTreeNodeLocator[] PlanNodes,
    MapResultGeometryOwner? ResultOwner,
    string SourcePath,
    string ContextToken,
    string SourceKind,
    string SourceRevision,
    string VisualizationRevision,
    AssociatedRasterStyleDraft Draft,
    AssociatedRasterStyleDraft ResetDraft,
    AssociatedRasterStylePalette[] Palettes,
    bool CanComputeHistogram,
    double[]? SourceHistogramValues,
    AssociatedRasterStyleDependency[] Dependencies);

/// <summary>
/// A palette edit is applied in the same .vis replacement as the selected
/// node style. Actions are "saveCustom" or "deleteCustom"; default palettes
/// are never mutable.
/// </summary>
public sealed record AssociatedRasterStylePaletteMutation(
    string Action,
    string? PaletteID = null,
    string? Name = null);

public sealed record AssociatedRasterStyleApplyRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string SourcePath,
    string SourceRevision,
    string ExpectedContextToken,
    AssociatedRasterStyleDraft Draft,
    AssociatedRasterStylePaletteMutation? PaletteMutation = null,
    CatalogTreeNodeLocator[]? GeometryNodes = null,
    CatalogTreeNodeLocator[]? PlanNodes = null,
    MapResultGeometryOwner? ResultOwner = null);

public sealed record AssociatedRasterStyleApplyResult(
    string PreviousVisualizationRevision,
    string VisualizationRevision,
    string SourceRevision);
