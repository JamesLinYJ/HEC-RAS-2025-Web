using Geospatial.Rasters;

namespace Ras.Backend;

public sealed record RasterCalculatorSourceFile(string Path, string Role, bool Exists, string? Revision);

public sealed record RasterCalculatorSourceChoice(string Id, string Kind, string Name, string AssetPath,
    string Revision, RasterCalculatorSourceFile[] Files, bool IsAvailable, string? UnavailableReason,
    string[] MapTypes, int ProfileCount, string[] ProfileTimes, double? NoDataValue,
    double MinX, double MinY, double MaxX, double MaxY);

public sealed record RasterCalculatorInspectRequest(MapTreeRequest Map);

public sealed record RasterCalculatorInspectResult(string ProjectPath, string ProjectRevision,
    string ProjectXmlSHA, string VisualizationRevision, string MapID, int MapIndex,
    string MapName, string MapType, string ContextToken, string DefaultNoDataBehavior,
    string[] NoDataBehaviors, string[] ReservedConstants, RasterCalculatorSourceChoice[] Sources);

public sealed record RasterCalculatorInput(string VariableName, string SourceID, string SourceRevision,
    string Kind, string? MapType = null, bool ForceHorizontal = false, bool IsKeyFrame = false);

public sealed record RasterCalculatorConstant(string Name, string Value);

public sealed record RasterCalculatorRecipe(string Expression, string NoDataBehavior,
    string? KeyFrameVariableName, RasterCalculatorInput[] Inputs,
    RasterCalculatorConstant[] Constants);

public sealed record RasterCalculatorEvaluateRequest(MapTreeRequest Map, string ExpectedContextToken,
    RasterCalculatorRecipe Recipe, string LayerID, string LayerName, int ProfileIndex,
    RasterMapViewExtent Viewport);

public sealed record RasterCalculatorProfile(int Index, string Time);

public sealed record RasterCalculatorLayerPayload(string ID, string MapID, string Name, int Width,
    int Height, byte[] RGBA, double MinX, double MinY, double MaxX, double MaxY);

public sealed record RasterCalculatorEvaluateResult(string ProjectPath, string ProjectRevision,
    string ProjectXmlSHA, string VisualizationRevision, string MapID, int MapIndex,
    string MapName, string MapType, string ContextToken, string LayerID, string LayerName,
    RasterCalculatorRecipe Recipe, bool IsDynamic, int ProfileCount,
    int SelectedProfileIndex, string? SelectedProfileTime, RasterCalculatorProfile[] Profiles,
    double MinX, double MinY, double MaxX, double MaxY,
    RasterCalculatorLayerPayload Layer, RasterCalculatorSourceChoice[] Sources);
