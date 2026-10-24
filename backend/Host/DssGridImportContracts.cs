namespace Ras.Backend;

public sealed record DssGridFrame(string Time, string RecordPath);

public sealed record DssGridSeriesInfo(
    string Id,
    string GroupPath,
    string Title,
    string[] Times,
    string[] RecordPaths,
    string Units,
    string DataType,
    string Projection,
    int Rows,
    int Columns,
    double CellSize,
    double Left,
    double Bottom,
    double Right,
    double Top,
    float NoDataValue,
    float SourceNullValue,
    float SourceUndefinedValue,
    string GridIdentity);

public sealed record DssGridImportComponent(string Component, string SeriesId,
    string[] Times, string[] RecordPaths, string GridIdentity);

public sealed record DssGridImportSelection(
    string SourcePath,
    string Revision,
    string DraftIdentity,
    string OperationId,
    DssGridImportComponent[] Components);

public sealed record DssGridInspection(
    string SourcePath,
    string Revision,
    string DraftIdentity,
    DssGridSeriesInfo[] Sources,
    SpatialGridPreview? Preview);

public sealed record DssGridImportDuplexResult(string State, ModelSnapshot? Snapshot,
    string? ErrorMessage = null, string? RefreshWarning = null);
