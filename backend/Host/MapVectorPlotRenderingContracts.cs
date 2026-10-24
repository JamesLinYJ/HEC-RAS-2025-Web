namespace Ras.Backend;

public sealed record MapVectorPlotOptionChoice(string Value, string Label);
public sealed record MapVectorPlotOptionDescriptor(string PropertyName, string Name, string Label,
    string Description, string Kind, string Value, MapVectorPlotOptionChoice[] EnumValues,
    bool IsEnabled, string? EnabledIf);

public sealed record MapVectorPlotOptionsRequest(MapTreeRequest Map, CatalogTreeNodeLocator Node,
    string LayerID, MapSourceFamilyCapture? ExpectedFamily, string SourceRevision,
    Dictionary<string, string>? PlotOptions = null);
public sealed record MapVectorPlotOptionsResponse(string MapID, string LayerID, string SourcePath,
    string SourceRevision, string VisualizationRevision, MapVectorPlotOptionDescriptor[] PlotOptions);

public sealed record MapVectorPlotStyleValues(string PenColor, double PenWidth, string FillColor,
    int PointSize, string HaloColor, double HaloThickness, int TextEmphasis);
public sealed record MapVectorPlotStyleOverride(int SourceFeatureIndex, MapVectorPlotStyleValues Values);
public sealed record MapVectorPlotStyle(string DefinitionID, MapVectorPlotStyleValues Base,
    MapVectorPlotStyleOverride[] SourceOverrides, bool? IsProjectOverride = null);
public sealed record MapVectorPlotCoordinate(double X, double Y);
public sealed record MapVectorPlotLabel(string Text, MapVectorPlotCoordinate Point,
    MapVectorPlotCoordinate[] Polyline);

public sealed record MapVectorPlotRequest(MapTreeRequest Map, CatalogTreeNodeLocator Node,
    string LayerID, MapSourceFamilyCapture? ExpectedFamily, string SourceRevision, RasterMapViewExtent Viewport,
    Dictionary<string, string> PlotOptions, MapVectorPlotStyle[] Styles, double RenderScale = 1.0);
public sealed record MapVectorPlotRenderResponse(string MapID, string LayerID, string SourcePath,
    string SourceRevision, string VisualizationRevision, RasterMapViewExtent Viewport,
    int PixelWidth, int PixelHeight, byte[] Rgba, MapVectorPlotLabel[] Labels,
    MapVectorPlotOptionDescriptor[] PlotOptions, double RenderScale = 1.0);
