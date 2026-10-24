namespace Ras.Backend;

public sealed record VectorSymbologyInspectRequest(string VectorPath, string Revision);

public sealed record VectorSymbologyInspection(
    string Revision,
    int SourceFeatureCount,
    VectorSymbologyBucket[] Buckets);

public sealed record VectorSymbologyBucket(
    string GeometryType,
    int RenderFeatureCount,
    int TableRowCount,
    bool OrdinalBindingProven,
    int[] SourceFeatureIndices,
    string[] SupportedDefinitionIDs,
    VectorSymbologyColumn[] Columns,
    bool HasAttributeTable);

public sealed record VectorSymbologyColumn(string ID, string Name, string Type, int Ordinal);

public sealed record VectorSymbologyEvaluateRequest(
    string VectorPath,
    string Revision,
    string GeometryType,
    int[] SourceFeatureIndices,
    VectorSymbologyPropertyConditions[] Properties);

public sealed record VectorSymbologyPropertyConditions(
    string DefinitionID,
    string Property,
    VectorSymbologyConditionInput[] Conditions);

public sealed record VectorSymbologyConditionInput(
    string Column,
    string Operator,
    string Threshold,
    string ResultValue);

public sealed record VectorSymbologyEvaluation(string Revision, VectorSymbologyOverride[] Overrides);

public sealed record VectorSymbologyOverride(
    int SourceFeatureIndex,
    string DefinitionID,
    string Property,
    bool HasMatch,
    string? ColorValue,
    double? NumberValue,
    int? IntegerValue);
