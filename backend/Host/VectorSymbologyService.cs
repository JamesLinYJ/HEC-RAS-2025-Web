using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial.IO.Vectors;
using Geospatial.Rendering;
using Utility.Memory;
using Visual.Mapping.Observables;

namespace Ras.Backend;

/// <summary>
/// Reads a GeoJSON source through the retained vector reader and evaluates the
/// retained typed attribute table using the source condition predicate rules.
/// This service never writes the vector file.
/// </summary>
public static class VectorSymbologyService
{
    private const long MaximumSourceBytes = 128L * 1024 * 1024;
    private const int MaximumSourceFeatures = 500_000;
    private const int MaximumTableColumns = 512;
    private const long MaximumTableCells = 5_000_000;
    private const int MaximumRequestedIndices = 500_000;
    private const int MaximumPropertyGroups = 64;
    private const int MaximumConditionsPerProperty = 256;
    private const int MaximumOverrideResults = 5_000_000;

    public static VectorSymbologyInspection Inspect(VectorSymbologyInspectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = ReadSource(request.VectorPath, request.Revision, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new VectorSymbologyInspection(snapshot.Revision, snapshot.SourceFeatureCount,
            snapshot.Buckets.Select(ToInspectionBucket).ToArray());
    }

    public static VectorSymbologyEvaluation Evaluate(VectorSymbologyEvaluateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = ReadSource(request.VectorPath, request.Revision, cancellationToken);
        if (request.SourceFeatureIndices is null || request.SourceFeatureIndices.Length == 0)
            throw new ArgumentException("Select at least one source feature for conditional-style evaluation.");
        if (request.SourceFeatureIndices.Length > MaximumRequestedIndices)
            throw new ArgumentException("The source-feature selection exceeds conditional-style limits.");
        if (request.Properties is null || request.Properties.Length > MaximumPropertyGroups)
            throw new ArgumentException("The conditional-style property selection exceeds current limits.");

        var bucket = snapshot.Buckets.SingleOrDefault(candidate =>
            string.Equals(candidate.GeometryType, request.GeometryType, StringComparison.Ordinal));
        if (bucket is null || bucket.SourceFeatureIndices.Length == 0)
            throw new NotSupportedException("The requested geometry family has no retained source features.");
        if (!bucket.OrdinalBindingProven)
            throw new InvalidDataException("The retained feature table cannot be bound to original source-feature indices.");
        if (!bucket.HasAttributeTable)
            throw new NotSupportedException("The selected vector bucket has no usable source attribute table.");

        var tableRowBySourceIndex = bucket.SourceFeatureIndices
            .Select((sourceIndex, rowIndex) => (sourceIndex, rowIndex))
            .ToDictionary(pair => pair.sourceIndex, pair => pair.rowIndex);
        var sourceIndices = new List<int>(request.SourceFeatureIndices.Length);
        var seenIndices = new HashSet<int>();
        foreach (int sourceIndex in request.SourceFeatureIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tableRowBySourceIndex.ContainsKey(sourceIndex))
                throw new ArgumentException("A selected source-feature index is not present in the requested geometry family.");
            if (seenIndices.Add(sourceIndex)) sourceIndices.Add(sourceIndex);
        }

        var definitionCapabilities = DefinitionsFor(bucket.GeometryType)
            .ToDictionary(item => item.DefinitionID, StringComparer.Ordinal);
        var seenProperties = new HashSet<(string DefinitionID, string Property)>();
        var compiledProperties = new List<CompiledProperty>(request.Properties.Length);
        foreach (var property in request.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property is null || property.Conditions is null || property.Conditions.Length > MaximumConditionsPerProperty)
                throw new ArgumentException("A conditional-style property group is invalid or exceeds current limits.");
            if (string.IsNullOrWhiteSpace(property.DefinitionID) || string.IsNullOrWhiteSpace(property.Property))
                throw new ArgumentException("A conditional-style property identity is missing.");
            if (!definitionCapabilities.TryGetValue(property.DefinitionID, out var definition) ||
                !definition.Properties.Contains(property.Property))
                throw new NotSupportedException("The selected source definition does not support this conditional property.");
            if (!seenProperties.Add((property.DefinitionID, property.Property)))
                throw new ArgumentException("A conditional property was requested more than once.");

            compiledProperties.Add(new CompiledProperty(property.DefinitionID, property.Property,
                CompileConditions(property, bucket.Table)));
        }

        long overrideCount = (long)sourceIndices.Count * compiledProperties.Count;
        if (overrideCount > MaximumOverrideResults)
            throw new ArgumentException("The conditional-style result exceeds current limits.");
        var overrides = new List<VectorSymbologyOverride>((int)overrideCount);
        foreach (int sourceIndex in sourceIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rowIndex = tableRowBySourceIndex[sourceIndex];
            var row = bucket.Table.Rows[rowIndex];
            foreach (var property in compiledProperties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                overrides.Add(EvaluateProperty(sourceIndex, property, row));
            }
        }

        EnsureCurrentRevision(snapshot.Path, snapshot.Revision);
        cancellationToken.ThrowIfCancellationRequested();
        return new VectorSymbologyEvaluation(snapshot.Revision, overrides.ToArray());
    }

    private static SourceSnapshot ReadSource(string vectorPath, string revision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(vectorPath)) throw new ArgumentException("The vector source path is missing.");
        if (!IsRevision(revision)) throw new ArgumentException("The expected vector source revision is invalid.");

        string path = Path.GetFullPath(vectorPath);
        if (!GeoJsonReader.HasJsonFileExtension(path))
            throw new NotSupportedException("Conditional symbology currently requires a GeoJSON vector source.");

        byte[] bytes = ReadBoundedFile(path);
        string actualRevision = Convert.ToHexString(SHA256.HashData(bytes));
        if (!actualRevision.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        cancellationToken.ThrowIfCancellationRequested();
        string json;
        using (var content = new MemoryStream(bytes, writable: false))
        using (var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            json = reader.ReadToEnd();

        GeoJsonFeatureCollection collection;
        try { collection = GeoJsonReader.Deserialize(json); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The retained source reader could not parse the GeoJSON vector file.", exception);
        }
        if (collection?.Features is null)
            throw new InvalidDataException("The retained source reader did not return a GeoJSON feature collection.");
        if (collection.Features.Count > MaximumSourceFeatures)
            throw new NotSupportedException("The GeoJSON source exceeds conditional-style feature limits.");

        int[][] indicesByFamily = BuildOriginalIndexBuckets(collection.Features, cancellationToken);
        var sorted = GeoJsonReader.TrySort(collection, out var sourceBuckets);
        if (!sorted.Result || sourceBuckets is null)
            throw new InvalidDataException("The retained source reader could not build typed GeoJSON feature tables.");

        var buckets = new[]
        {
            BindBucket("point", sourceBuckets.Points.Count, sourceBuckets.Points.AttributeTable, indicesByFamily[0]),
            BindBucket("line", sourceBuckets.Polylines.Count, sourceBuckets.Polylines.AttributeTable, indicesByFamily[1]),
            BindBucket("polygon", sourceBuckets.Polygons.Count, sourceBuckets.Polygons.AttributeTable, indicesByFamily[2])
        };
        if (!buckets.Any(item => item.RenderFeatureCount > 0))
            throw new NotSupportedException("The GeoJSON source has no located point, line, or polygon features.");
        if (buckets.Any(item => item.Columns.Length > MaximumTableColumns ||
                                (long)item.TableRowCount * item.Columns.Length > MaximumTableCells))
            throw new NotSupportedException("The source attribute table exceeds conditional-style limits.");

        EnsureCurrentRevision(path, actualRevision);
        cancellationToken.ThrowIfCancellationRequested();
        return new SourceSnapshot(path, actualRevision, collection.Features.Count, buckets);
    }

    private static byte[] ReadBoundedFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The GeoJSON source file could not be found.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < 0 || stream.Length > MaximumSourceBytes)
            throw new NotSupportedException("The GeoJSON source exceeds conditional-style file-size limits.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        try { stream.ReadExactly(bytes); }
        catch (EndOfStreamException) { throw new SourceFileChangedException(); }
        if (stream.ReadByte() != -1)
            throw new SourceFileChangedException();
        return bytes;
    }

    private static int[][] BuildOriginalIndexBuckets(IReadOnlyList<GeoJsonFeature> features, CancellationToken cancellationToken)
    {
        var point = new List<int>();
        var line = new List<int>();
        var polygon = new List<int>();
        for (int index = 0; index < features.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feature = features[index];
            if (feature is null)
                throw new InvalidDataException("The GeoJSON contains an empty feature entry.");
            var geometry = feature.Geometry;
            if (geometry is null) continue;
            if (GeoJsonTypes.IsPointType(geometry.Type)) point.Add(index);
            else if (GeoJsonTypes.IsLineType(geometry.Type)) line.Add(index);
            else if (GeoJsonTypes.IsPolygonType(geometry.Type)) polygon.Add(index);
            else throw new NotSupportedException("The GeoJSON contains a geometry family that cannot be bound to retained vector-table rows.");
        }
        return [point.ToArray(), line.ToArray(), polygon.ToArray()];
    }

    private static BoundBucket BindBucket(string geometryType, int renderFeatureCount, Table? table, int[] sourceFeatureIndices)
    {
        if (table is null) throw new InvalidDataException("The retained vector reader did not provide a source attribute table.");
        bool rowBindingProven = renderFeatureCount == sourceFeatureIndices.Length && table.Rows.Count == sourceFeatureIndices.Length;
        var columns = table.Columns.Select((column, ordinal) => new VectorSymbologyColumn(
            "column/" + ordinal.ToString(CultureInfo.InvariantCulture), column.Name, GetColumnType(column.Type), ordinal)).ToArray();
        return new BoundBucket(geometryType, renderFeatureCount, table.Rows.Count, rowBindingProven,
            sourceFeatureIndices, DefinitionsFor(geometryType).Select(item => item.DefinitionID).ToArray(),
            columns, columns.Length > 0 && table.Rows.Count > 0, table);
    }

    private static VectorSymbologyBucket ToInspectionBucket(BoundBucket bucket) => new(
        bucket.GeometryType, bucket.RenderFeatureCount, bucket.TableRowCount, bucket.OrdinalBindingProven,
        bucket.SourceFeatureIndices, bucket.SupportedDefinitionIDs, bucket.Columns, bucket.HasAttributeTable);

    private static string GetColumnType(Type type) => type == typeof(string) ? "string"
        : type == typeof(int) ? "int32"
        : type == typeof(long) ? "int64"
        : type == typeof(float) ? "float32"
        : type == typeof(double) ? "float64"
        : "unsupported";

    private static DefinitionCapability[] DefinitionsFor(string geometryType)
    {
        VectorSymbologyDefinition[] definitions = geometryType switch
        {
            "point" => PointMapLayerVisualContext.DefaultSymbologyDefinitions,
            "line" => PolylineMapLayerVisualContext.DefaultSymbologyDefinitions,
            "polygon" => PolygonMapLayerVisualContext.DefaultSymbologyDefinitions,
            _ => throw new NotSupportedException("The requested geometry family is not supported.")
        };
        return definitions.Select(definition => new DefinitionCapability(definition.Id,
            definition.GetPropertyLabels().Select(label => label.PropertyName).ToHashSet(StringComparer.Ordinal))).ToArray();
    }

    private static CompiledCondition[] CompileConditions(VectorSymbologyPropertyConditions property, Table table)
    {
        var compiled = new List<CompiledCondition>(property.Conditions.Length);
        foreach (var condition in property.Conditions)
        {
            if (condition is null) throw new ArgumentException("A conditional-style condition is missing.");
            // The source ignores incomplete editor rows until the user fills them in.
            if (string.IsNullOrEmpty(condition.Column) || string.IsNullOrEmpty(condition.Operator) ||
                string.IsNullOrEmpty(condition.Threshold) || string.IsNullOrEmpty(condition.ResultValue)) continue;
            if (!table.TryGetColumnIndex(condition.Column, out int columnIndex))
                throw new ArgumentException("The selected condition column is not in the current source table.");

            Type columnType = table.Columns[columnIndex].Type;
            if (GetColumnType(columnType) == "unsupported")
                throw new NotSupportedException("The selected source column has a type unsupported by conditional styling.");
            if (!ConditionOperatorMap.Map.TryGetValue(condition.Operator, out var op))
                throw new ArgumentException("The selected condition operator is not supported by the retained renderer.");
            bool stringOperator = ConditionPredicates.IsStringOperator(op);
            if (stringOperator != (columnType == typeof(string)))
                throw new ArgumentException("The condition operator does not match the selected source column type.");

            object result = ParseResult(property.Property, condition.ResultValue);
            Func<object, bool> predicate;
            try { predicate = ConditionPredicates.Build(op, condition.Threshold, columnType); }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            {
                throw new ArgumentException("The condition threshold is invalid for the selected source column type.", exception);
            }
            compiled.Add(new CompiledCondition(columnIndex, predicate, result));
        }
        return compiled.ToArray();
    }

    private static object ParseResult(string property, string value)
    {
        switch (property)
        {
            case "PenColor":
            case "FillColor":
                try
                {
                    Color color = ColorTranslator.FromHtml(value);
                    return "#" + unchecked((uint)color.ToArgb()).ToString("X8", CultureInfo.InvariantCulture);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException)
                {
                    throw new ArgumentException("The conditional color is not valid for the retained renderer.", exception);
                }
            case "PenWidth":
                if (double.TryParse(value, out double width) && double.IsFinite(width)) return width;
                throw new ArgumentException("The conditional line width must be a finite number.");
            case "PointSize":
                if (int.TryParse(value, out int size)) return size;
                throw new ArgumentException("The conditional point size must be a 32-bit integer.");
            default:
                throw new NotSupportedException("The selected conditional property is not supported.");
        }
    }

    private static VectorSymbologyOverride EvaluateProperty(int sourceIndex, CompiledProperty property, TableRow row)
    {
        foreach (var condition in property.Conditions)
        {
            object value = row[condition.ColumnIndex];
            if (!condition.Predicate(value)) continue;
            return property.Property switch
            {
                "PenColor" or "FillColor" => new(sourceIndex, property.DefinitionID, property.Property, true,
                    (string)condition.ResultValue, null, null),
                "PenWidth" => new(sourceIndex, property.DefinitionID, property.Property, true,
                    null, (double)condition.ResultValue, null),
                "PointSize" => new(sourceIndex, property.DefinitionID, property.Property, true,
                    null, null, (int)condition.ResultValue),
                _ => throw new NotSupportedException("The selected conditional property is not supported.")
            };
        }
        return new VectorSymbologyOverride(sourceIndex, property.DefinitionID, property.Property,
            false, null, null, null);
    }

    private static void EnsureCurrentRevision(string path, string expectedRevision)
    {
        byte[] currentBytes = ReadBoundedFile(path);
        string currentRevision = Convert.ToHexString(SHA256.HashData(currentBytes));
        if (!currentRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static bool IsRevision(string? revision) => revision is { Length: 64 } &&
        revision.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private sealed record SourceSnapshot(string Path, string Revision, int SourceFeatureCount, BoundBucket[] Buckets);
    private sealed record BoundBucket(string GeometryType, int RenderFeatureCount, int TableRowCount,
        bool OrdinalBindingProven, int[] SourceFeatureIndices, string[] SupportedDefinitionIDs,
        VectorSymbologyColumn[] Columns, bool HasAttributeTable, Table Table);
    private sealed record DefinitionCapability(string DefinitionID, HashSet<string> Properties);
    private sealed record CompiledProperty(string DefinitionID, string Property, CompiledCondition[] Conditions);
    private sealed record CompiledCondition(int ColumnIndex, Func<object, bool> Predicate, object ResultValue);
}
