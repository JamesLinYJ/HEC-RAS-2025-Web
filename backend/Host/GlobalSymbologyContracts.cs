namespace Ras.Backend;

public sealed record GlobalSymbologyValues(
    string PenColor,
    double PenWidth,
    string FillColor,
    int PointSize,
    string HaloColor,
    double HaloThickness,
    int TextEmphasis);

public sealed record GlobalSymbologyDefinitionSnapshot(
    string DefinitionID,
    string Name,
    string LayerName,
    string VectorType,
    string[] EditableProperties,
    GlobalSymbologyValues DefaultValues,
    GlobalSymbologyValues Values,
    bool IsCustomized);

public sealed record GlobalSymbologyGroupSnapshot(
    string LayerName,
    GlobalSymbologyDefinitionSnapshot[] Options);

public sealed record GlobalSymbologyInspectResponse(
    string Revision,
    GlobalSymbologyGroupSnapshot[] Groups);

public sealed record GlobalSymbologyStyleInput(
    string DefinitionID,
    GlobalSymbologyValues Values);

public sealed record GlobalSymbologySaveRequest(
    string ExpectedRevision,
    GlobalSymbologyStyleInput[] Styles);

public sealed record GlobalSymbologySaveResponse(
    string Revision,
    GlobalSymbologyGroupSnapshot[] Groups);

public sealed class GlobalSymbologyRevisionConflictException(string message) : InvalidOperationException(message);
