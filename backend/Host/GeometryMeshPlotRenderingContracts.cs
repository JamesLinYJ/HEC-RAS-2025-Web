namespace Ras.Backend;

/// <summary>
/// A source-render request tied to one loaded project-map capture. Parent
/// editor mode carries exact Geometry drafts; saved-source mode carries the
/// canonical empty-draft identity and renders only the saved HDF model.
/// </summary>
public sealed record GeometryMeshPlotRenderRequest(
    GeometryMeshModelRequest Model,
    string SourceIdentity,
    string MapID,
    string MapContextToken,
    string GeometryNodeIdentity,
    string ProjectRevision,
    string VisualizationRevision,
    string SourceRevision,
    string MeshDraftState,
    string MeshDraftSHA256,
    string? TopologyDraftState,
    string TopologyDraftSHA256,
    string OwnerMode,
    GeometryMeshPlotViewport Viewport,
    int PixelWidth,
    int PixelHeight,
    double RenderScale,
    double PixelSamplingScale,
    string StyleScope,
    string StyleRevision,
    ulong GlobalStyleRevision,
    string StylePacketSHA256,
    string StyleIdentity,
    Dictionary<string, MapVectorPlotStyleValues> Styles,
    string RendererFamily,
    Dictionary<string, string> Options);

public sealed record GeometryMeshPlotViewport(double MinX, double MinY, double MaxX, double MaxY);
public sealed record GeometryMeshPlotCoordinate(double X, double Y);
public sealed record GeometryMeshPlotLabel(string Text, GeometryMeshPlotCoordinate Point,
    GeometryMeshPlotCoordinate[] Polyline);
public sealed record GeometryMeshPlotEnumChoice(string Value, string Label);
public sealed record GeometryMeshPlotOptionDescriptor(string PropertyName, string Name, string Label,
    string Kind, string Value, GeometryMeshPlotEnumChoice[] EnumValues, bool IsEnabled,
    string? EnabledIf, bool HasDrawingImplementation, string? ImplementationNote);

/// <summary>
/// Actual pixels and labels emitted by the retained CPU renderer. RGBA bytes
/// are premultiplied for direct use by CoreGraphics; labels preserve the
/// renderer's source world coordinates.
/// </summary>
public sealed record GeometryMeshPlotRenderResponse(
    string SourceIdentity,
    string OwnerIdentity,
    string MapID,
    string MapContextToken,
    string GeometryNodeIdentity,
    string ProjectRevision,
    string VisualizationRevision,
    string SourceRevision,
    string MeshDraftSHA256,
    string TopologyDraftSHA256,
    string OwnerMode,
    GeometryMeshPlotViewport Viewport,
    int PixelWidth,
    int PixelHeight,
    double RenderScale,
    double PixelSamplingScale,
    double SourceRenderScale,
    string StylePacketSHA256,
    string StyleIdentity,
    Dictionary<string, MapVectorPlotStyleValues> AppliedStyles,
    GeometryMeshPlotViewport RasterExtent,
    string RendererFamily,
    string Status,
    bool PaintedPixels,
    string PixelsRGBA,
    GeometryMeshPlotLabel[] Labels,
    GeometryMeshPlotOptionDescriptor[] Options);
