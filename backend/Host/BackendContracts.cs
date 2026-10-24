using System.Text.Json;

namespace Ras.Backend;

public static class BackendJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}

public sealed record BackendRequest(string Id, string Method, JsonElement Parameters,
    bool StreamProgress = false, bool Duplex = false);
public record ModelRequest(string AssetPath, string Kind, string? ProjectPath, string? ProjectXml = null,
    string? SourceState = null, CoreLayerRequestContext? CoreLayerContext = null);
public sealed record ModelCatalogRequest(string[] AssetPaths);
public sealed record SurfaceCatalogEntry(string Path, string? Category, string? Error);
public sealed record ModelSaveRequest(string AssetPath, string Kind, string? ProjectPath,
    string Revision, Dictionary<string, string> Fields, Dictionary<string, ImageData[]> Images, string? ProjectXml = null,
    string? SourceState = null, Dictionary<string, ModelRow[]>? Rows = null, Dictionary<string, ModelSample[]>? Samples = null,
    PlanTimeWindowEdit[]? PlanTimeWindowEdits = null, string? GeometryTopologyState = null,
    string? GeometryMeshState = null, string? GeometryCulvertState = null,
    Dictionary<string, string[]>? TableSelections = null,
    Dictionary<string, ChangedCell[]>? ChangedCells = null,
    CoreLayerRequestContext? CoreLayerContext = null)
    : ModelRequest(AssetPath, Kind, ProjectPath, ProjectXml, SourceState, CoreLayerContext);
public sealed record ModelContextRefreshResult(ModelSnapshot Baseline, ModelSnapshot Draft);
public sealed record PlanTimeWindowFieldIDs(string Mode, string Start, string End)
{
    public bool Contains(string fieldID) => fieldID == Mode || fieldID == Start || fieldID == End;
}
public sealed record PlanTimeWindowEdit(string Id, string FieldID, string Value);
public sealed record SourceEditorChoice(string Id, string Title, bool? DebugOnly = null,
    bool? TitleIsUserData = null, string? SourceIcon = null, string? Description = null);
public sealed record ChangedCell(string RowID, string ColumnID);
public sealed record ModelRow(string Id, Dictionary<string, string> Values);
public sealed record ModelSample(string Id, double X, double? Y, string Series, string? DatasetId = null);
public sealed record SeriesTransformRequest(ModelSaveRequest Model, string PageId, string Operation);
public sealed record BoundaryTransformRequest(ModelSaveRequest Model, string LineId, string Operation, string? PayloadId, Dictionary<string, string> Fields);
public sealed record ManningTransformRequest(ModelSaveRequest Model, string Operation);
public sealed record SpatialTransformRequest(ModelSaveRequest Model, string LayerId, string Operation, string? ItemId);
public sealed record ImageData(string Id, string Name, byte[] Data);
public sealed record ModelSnapshot(string Revision, Dictionary<string, string> Fields,
    Dictionary<string, ImageData[]> Images, bool CanEdit,
    Dictionary<string, string[]> FieldGroups, string[] AvailableOperations, BoundaryEditorData? Boundary = null,
    string? SourceState = null, string[]? ReadOnlyFields = null, Dictionary<string, SpatialGridData>? Grids = null, ManningEditorData? Manning = null, ClassificationEditorData? Classification = null, TerrainEditorData? Terrain = null,
    PlanTimeWindowFieldIDs? PlanTimeWindowFieldIDs = null,
    Dictionary<string, SourceEditorChoice[]>? FieldChoices = null,
    Dictionary<string, string>? FieldTitles = null);
public sealed class SourceFileChangedException() : IOException("The model file changed outside this editor. Reload it before saving.");
