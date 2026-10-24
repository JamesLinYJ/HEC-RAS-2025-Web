namespace Ras.Backend;

/// <summary>Exact project, saved-map, layer, and source capture for vector style inspection.</summary>
public sealed record MapVectorSymbologyRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string LayerID,
    string ExpectedProjectRevision,
    MapSourceFamilyCapture? ExpectedFamily,
    string SourceRevision);

/// <summary>
/// Saves editable base values for the exact inspected source. Conditional rules are read-only in this
/// operation and remain in the existing .vis XML unchanged.
/// </summary>
public sealed record MapVectorSymbologySaveRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string LayerID,
    string ExpectedProjectRevision,
    MapSourceFamilyCapture? ExpectedFamily,
    string SourceRevision,
    string ExpectedStyleSHA256,
    string ExpectedGlobalSymbologyRevision,
    MapVectorPlotStyle[] Styles);

public sealed record MapVectorSymbologyCondition(
    string Column,
    string Operator,
    string Threshold,
    string ResultValue);

public sealed record MapVectorSymbologyConditionGroup(
    string DefinitionID,
    string Property,
    MapVectorSymbologyCondition[] Conditions);

public sealed record MapVectorSymbologyDefinition(
    string DefinitionID,
    string Name,
    string LayerName,
    string VectorType,
    string[] EditableProperties,
    GlobalSymbologyValues DefaultValues,
    GlobalSymbologyValues GlobalValues);

public sealed record MapVectorSymbologyInspection(
    string MapID,
    string LayerID,
    CatalogTreeNodeLocator Node,
    string ProjectPath,
    string? ProjectXmlSHA,
    string ProjectRevision,
    string? ProjectSessionID,
    string SourcePath,
    string SourceRevision,
    string VisualizationRevision,
    string StyleSHA256,
    string GlobalSymbologyRevision,
    MapVectorSymbologyDefinition[] Definitions,
    MapVectorPlotStyle[] Styles,
    MapVectorPlotOptionDescriptor[] PlotOptions,
    MapVectorSymbologyConditionGroup[] ProjectConditions,
    bool HasUnsupportedProjectConditions,
    bool HasUnknownProjectSymbologyContent);
