namespace Ras.Backend;

public sealed record TerrainTopologyRequest(ModelSaveRequest Model, string InputId, string Operation,
    string[]? FeatureIDs = null, TerrainMapCoordinate[]? Points = null, double? MapCellSize = null,
    string? ExpectedDraftIdentity = null, string? SplitMode = null, int? VertexIndex = null,
    string? MetadataKind = null, string? MetadataColumn = null, string? MetadataValue = null,
    TerrainInputMapContextRequest? MapContext = null, TerrainPolygonEditSettings? PolygonSettings = null);

public sealed record TerrainPolygonEditSettings(string Shape, double Width, double Height,
    double Rotation, float Value, string ModificationType);

public sealed record TerrainTopologyMetadataField(string Id, string Title, string SourceType,
    string? Value, bool ReadOnly, string[]? Options = null);

public sealed record TerrainTopologyFeature(string Id, string Kind, TerrainMapCoordinate[] Points,
    string? ParentId = null, int? VertexIndex = null, int? ControlPointIndex = null,
    bool? IsMask = null, TerrainTopologyMetadataField[]? Metadata = null,
    TerrainMapCoordinate[][]? Rings = null);
public sealed record TerrainTopologyData(string InputId, string SourceRevision, string DraftIdentity,
    TerrainTopologyFeature[] Features, string[] SelectedFeatureIDs, bool CanUndo, bool CanRedo,
    int? InputIndex = null, TerrainPolygonEditSettings? PolygonSettings = null);
public sealed record TerrainTopologyResponse(ModelSnapshot Snapshot, TerrainTopologyData Topology);

/// <summary>One source graph command, retained in the parent terrain draft for deterministic replay.</summary>
public sealed record TerrainImportedChannelProfileSnapshot(string? Name,
    TerrainMapCoordinate[] Points, bool HasZ, bool HasM, string[]? Z, string[]? M,
    string[] Stations, string[] Elevations);

public sealed record TerrainTopologyMutation(string Operation, string[] FeatureIDs,
    TerrainMapCoordinate[]? Points = null, double? MapCellSize = null, string? SplitMode = null,
    int? VertexIndex = null, string? MetadataKind = null, string? MetadataColumn = null,
    string? MetadataValue = null, string? ImportPayloadID = null, string? ImportSourceSHA256 = null,
    TerrainImportedChannelProfileSnapshot[]? ImportedChannelProfiles = null);

/// <summary>Commands after the saved terrain baseline. Redo commands are stored in pop order.</summary>
public sealed record TerrainTopologyDraft(TerrainTopologyMutation[] Mutations)
{
    public TerrainTopologyMutation[] RedoMutations { get; init; } = [];
}

/// <summary>Parent-Terrain journal for source-backed polygon raster modifications.</summary>
public sealed record TerrainPolygonTopologyMutation(string Operation, string[] FeatureIDs,
    TerrainMapCoordinate[]? Points = null, TerrainPolygonEditSettings? Settings = null);
public sealed record TerrainPolygonTopologyDraft(TerrainPolygonTopologyMutation[] Mutations)
{
    public TerrainPolygonTopologyMutation[] RedoMutations { get; init; } = [];
    public string? InputNodeID { get; init; }
    public int? InputIndex { get; init; }
}
