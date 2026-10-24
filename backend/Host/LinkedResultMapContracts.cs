namespace Ras.Backend;

public sealed record LinkedResultMapRequest(string ProjectPath, string ProjectXml,
    string ProjectRevision, ResultPlanCandidate Anchor, ResultPlanCandidateGroup[] SelectedGroups,
    int ProfileIndex, string MapType, string FrameMode = "Profile",
    RasterMapViewExtent? Viewport = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record LinkedResultMapSourceStatus(string ResultPath, string Revision,
    string ResultName, string? TerrainPath, string? TerrainRevision, bool Available,
    int? ProfileIndex, string? ProfileName, string? Time, string? UnavailableReason);

public sealed record LinkedResultMapLayer(string Id, string ResultPath, string Revision,
    string ResultName, string MapType, string TerrainPath, string TerrainRevision,
    int ProfileIndex, string ProfileName, string Time, string Units, int Width, int Height,
    byte[] Rgba, double MinX, double MinY, double MaxX, double MaxY,
    MapResultRenderLegendStop[] LegendStops);

public sealed record LinkedResultMapSnapshot(string ProjectPath, string ProjectRevision,
    string AnchorPath, int ProfileIndex, string FrameMode, string MapType,
    RasterMapViewExtent Viewport, bool MinimumAvailable, bool MaximumAvailable,
    LinkedResultMapSourceStatus[] Sources, LinkedResultMapLayer[] Layers,
    string? UnavailableReason);
