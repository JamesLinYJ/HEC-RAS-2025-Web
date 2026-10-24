using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.IO.Vectors;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>Reads a bounded, exact GeoJSON source bound to one persisted map vector node.</summary>
internal static class MapGeoJSONSourceService
{
    private const long MaximumSourceBytes = 128L * 1024 * 1024;
    private const int MaximumFeatureCount = 250_000;
    private const int MaximumPositionCount = 250_000;
    private const int MaximumJsonDepth = 64;

    private sealed class BoundsAccumulator
    {
        private double _minX = double.PositiveInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _maxY = double.NegativeInfinity;

        public void Add(double x, double y)
        {
            _minX = Math.Min(_minX, x);
            _minY = Math.Min(_minY, y);
            _maxX = Math.Max(_maxX, x);
            _maxY = Math.Max(_maxY, y);
        }

        public MapVectorSourceExtent? ToExtent() => double.IsFinite(_minX) && double.IsFinite(_minY) &&
            double.IsFinite(_maxX) && double.IsFinite(_maxY)
                ? new(_minX, _minY, _maxX, _maxY)
                : null;
    }

    private sealed record Position(double X, double Y, double? Z, double? M);
    private sealed record PartPlan(int PartOrdinal, int[] PositionIndices, bool HasZ, bool HasM,
        int? PolygonIndex, bool? IsHole);
    private sealed record AttributeValue(string Type, string? Value, bool? BooleanValue);
    private sealed record FeaturePlan(int SourceOrdinal, string? SourceID, PartPlan[] Parts,
        Dictionary<string, AttributeValue> Properties);
    private sealed class AttributeColumn(string name, string valueType)
    {
        public string Name { get; } = name;
        public string ValueType { get; set; } = valueType;
    }

    public static MapSourceFamilyCapture CaptureFamily(
        MapVectorSourceLoadRequest request,
        MapSourceNodeLocation location)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(location);
        string presentationPath = location.PresentationPath ?? location.SourcePath;
        if (!GeoJsonReader.HasJsonFileExtension(presentationPath))
            throw new SourceFileChangedException();

        string sourcePath = Path.GetFullPath(location.SourcePath);
        if (request.ExpectedFamily is not { } expected)
            return MapSourceIdentity.CaptureFamily("geojson", sourcePath, []);

        if (!string.Equals(expected.Family, "geojson", StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(expected.SourcePath, sourcePath) ||
            expected.Files is null || expected.Files.Length != 1 ||
            !expected.Files[0].Required || !expected.Files[0].Exists || expected.Files[0].Revision is null ||
            !string.Equals(expected.Files[0].Role, "source", StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(expected.Files[0].Path, sourcePath) ||
            !SourceFilePaths.SameSourceFile(expected.Files[0].InputPath, sourcePath))
            throw new SourceFileChangedException();

        MapSourceIdentity.VerifyFamily(expected);
        return expected;
    }

    public static MapVectorSourceData ReadData(string sourcePath, MapSourceFamilyCapture family,
        Project project, string? mapType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(project);
        string shapeType = mapType switch
        {
            "PointsVector" => "Point",
            "PolylinesVector" => "Polyline",
            "PolygonsVector" => "Polygon",
            _ => throw new NotSupportedException("The persisted map node is not a supported vector layer.")
        };

        ValidateSource(sourcePath, family);
        MapSourceIdentity.VerifyFamily(family);
        byte[] bytes = ReadBoundedFile(sourcePath);
        MapSourceFileRevision sourceFile = SourceFile(family);
        string contentSHA = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(contentSHA, sourceFile.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(bytes);
        if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        JsonElement[] rawFeatures = ReadRawFeatures(document.RootElement);
        if (rawFeatures.Length > MaximumFeatureCount)
            throw new NotSupportedException("The GeoJSON source contains too many features for map rendering.");

        GeoJsonFeatureCollection collection = DeserializeRetainedAst(json, document.RootElement,
            rawFeatures.Length);

        var positions = new List<Position>();
        var features = new List<FeaturePlan>();
        bool localProject = project.ProjectionDefinition.Type == ProjectionType.Local;
        for (int sourceOrdinal = 0; sourceOrdinal < collection.Features.Count; sourceOrdinal++)
        {
            GeoJsonFeature feature = collection.Features[sourceOrdinal]
                ?? throw new InvalidDataException("The GeoJSON feature collection contains an empty feature entry.");
            if (!GeoJsonTypes.Matches(feature.Type, GeoJsonTypes.Feature))
                throw new InvalidDataException("The GeoJSON feature collection contains an invalid feature type.");
            string? sourceID = ReadFeatureID(rawFeatures[sourceOrdinal]);
            if (feature.Geometry is null)
                continue; // RFC 7946 allows an unlocated feature with null geometry.

            string geometryType = feature.Geometry.Type;
            string geometryFamily = GeometryFamily(geometryType);
            if (!string.Equals(geometryFamily, shapeType, StringComparison.Ordinal))
                continue;

            PartPlan[] parts = ReadGeometry(feature.Geometry, positions, localProject);
            if (parts.Length == 0)
                throw new InvalidDataException("A located GeoJSON feature has no drawable geometry parts.");
            features.Add(new(sourceOrdinal, sourceID, parts, ReadProperties(feature.Properties)));
        }

        if (features.Count == 0)
            throw new SourceFileChangedException();

        Dictionary<string, int> columnOrdinals = BuildAttributeSchema(features, out AttributeColumn[] columns);
        MapGeoJSONProjectionPoint[] projectionInput = positions.Select((point, index) =>
            new MapGeoJSONProjectionPoint(index.ToString(CultureInfo.InvariantCulture), point.X, point.Y)).ToArray();
        MapGeoJSONProjectedCoordinates projected = MapGeoJSONProjectionService.ProjectToMap(project, projectionInput);
        if (projected.Points.Length != positions.Count)
            throw new InvalidDataException("The GeoJSON projection returned an incomplete coordinate array.");

        var sourceBounds = new BoundsAccumulator();
        var projectBounds = new BoundsAccumulator();
        var projectedVertices = new MapVectorSourceVertex[positions.Count];
        for (int index = 0; index < positions.Count; index++)
        {
            Position source = positions[index];
            MapGeoJSONProjectionPoint target = projected.Points[index];
            if (!int.TryParse(target.ID, NumberStyles.None, CultureInfo.InvariantCulture, out int targetIndex) ||
                targetIndex != index || !double.IsFinite(target.X) || !double.IsFinite(target.Y))
                throw new InvalidDataException("The GeoJSON projection returned invalid coordinate identities.");
            sourceBounds.Add(source.X, source.Y);
            projectBounds.Add(target.X, target.Y);
            projectedVertices[index] = new(target.X, target.Y, source.X, source.Y, source.Z, source.M);
        }

        MapVectorSourceFeature[] outputFeatures = features.Select(feature => new MapVectorSourceFeature(
            feature.SourceOrdinal,
            feature.SourceID,
            shapeType,
            feature.Parts.Select(part => new MapVectorSourcePart(part.PartOrdinal,
                part.PositionIndices.Select(index => projectedVertices[index]).ToArray(),
                part.HasZ, part.HasM, part.PolygonIndex, part.IsHole)).ToArray(),
            BuildAttributes(feature, columns, columnOrdinals))).ToArray();

        MapSourceIdentity.VerifyFamily(family);
        return new(shapeType, 0, outputFeatures.Length, sourceBounds.ToExtent(), projectBounds.ToExtent(),
            projected.ProjectionStatus, projected.SourceProjectionWKT, projected.ProjectProjectionWKT,
            outputFeatures,
            columns.Select((column, ordinal) => new MapVectorAttributeField(
                ordinal, column.Name, "GeoJSON", 0, 0, column.ValueType)).ToArray());
    }

    private static void ValidateSource(string sourcePath, MapSourceFamilyCapture family)
    {
        if (!Path.IsPathFullyQualified(sourcePath) ||
            !string.Equals(family.Family, "geojson", StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(family.SourcePath, sourcePath))
            throw new SourceFileChangedException();
        _ = SourceFile(family);
    }

    private static MapSourceFileRevision SourceFile(MapSourceFamilyCapture family)
    {
        MapSourceFileRevision[] sources = family.Files.Where(file => file.Required && file.Exists &&
            string.Equals(file.Role, "source", StringComparison.Ordinal)).ToArray();
        if (family.Files.Length != 1 || sources.Length != 1 || sources[0].Revision is null ||
            !SourceFilePaths.SameSourceFile(sources[0].Path, family.SourcePath))
            throw new SourceFileChangedException();
        return sources[0];
    }

    private static byte[] ReadBoundedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        long length = stream.Length;
        if (length <= 0)
            throw new InvalidDataException("The GeoJSON source file is empty.");
        if (length > MaximumSourceBytes || length > int.MaxValue)
            throw new NotSupportedException("The GeoJSON source exceeds the map vector size limit.");
        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new NotSupportedException("The GeoJSON source grew beyond the map vector size limit while it was read.");
        return bytes;
    }

    private static JsonElement[] ReadRawFeatures(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out string type))
            throw new InvalidDataException("The GeoJSON root must be a FeatureCollection or Feature object.");
        if (GeoJsonTypes.Matches(type, GeoJsonTypes.FeatureCollection))
        {
            if (!root.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The GeoJSON FeatureCollection must declare a features array.");
            if (features.GetArrayLength() > MaximumFeatureCount)
                throw new NotSupportedException("The GeoJSON source contains too many features for map rendering.");
            return features.EnumerateArray().ToArray();
        }
        if (GeoJsonTypes.Matches(type, GeoJsonTypes.Feature)) return [root];
        throw new NotSupportedException("Only GeoJSON Feature and FeatureCollection roots are supported for map layers.");
    }

    private static GeoJsonFeatureCollection DeserializeRetainedAst(string json, JsonElement root, int featureCount)
    {
        GeoJsonFeatureCollection? retained = null;
        try
        {
            retained = GeoJsonReader.Deserialize(json);
        }
        catch (JsonException)
        {
            // The retained normalizer discovers a root "type" by token scan;
            // valid documents with "features" before root "type" can expose a
            // nested geometry type first. Fall back to its same typed AST below.
        }

        if (retained?.Features is not null && retained.Features.Count == featureCount &&
            retained.Features.All(feature => feature is not null && GeoJsonTypes.Matches(feature.Type, GeoJsonTypes.Feature)))
            return retained;

        if (!TryGetString(root, "type", out string rootType))
            throw new InvalidDataException("The GeoJSON root is missing its type declaration.");
        if (GeoJsonTypes.Matches(rootType, GeoJsonTypes.FeatureCollection))
            return JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json)
                ?? throw new InvalidDataException("The retained GeoJSON feature collection AST is empty.");
        GeoJsonFeature feature = JsonSerializer.Deserialize<GeoJsonFeature>(json)
            ?? throw new InvalidDataException("The retained GeoJSON feature AST is empty.");
        return new GeoJsonFeatureCollection { Type = GeoJsonTypes.FeatureCollection, Features = [feature] };
    }

    private static string? ReadFeatureID(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The GeoJSON feature collection contains a non-object entry.");
        if (!feature.TryGetProperty("id", out JsonElement id)) return null;
        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => throw new InvalidDataException("A GeoJSON feature id must be a string or number.")
        };
    }

    private static string GeometryFamily(string? geometryType)
    {
        if (GeoJsonTypes.IsPointType(geometryType ?? "")) return "Point";
        if (GeoJsonTypes.IsLineType(geometryType ?? "")) return "Polyline";
        if (GeoJsonTypes.IsPolygonType(geometryType ?? "")) return "Polygon";
        throw new NotSupportedException($"The GeoJSON geometry type '{geometryType ?? "(missing)"}' is not supported by persisted map vector layers.");
    }

    private static PartPlan[] ReadGeometry(GeoJsonGeometry geometry, List<Position> positions,
        bool preserveSourceCoordinates)
    {
        if (geometry.Coordinates.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"GeoJSON {geometry.Type} coordinates must be an array.");
        var parts = new List<PartPlan>();
        switch (geometry.Type?.ToLowerInvariant())
        {
            case "point":
                parts.Add(ReadPositionPart(geometry.Coordinates, positions, 0, preserveSourceCoordinates));
                break;
            case "multipoint":
            {
                JsonElement[] points = RequireItems(geometry.Coordinates, "MultiPoint", 1);
                for (int index = 0; index < points.Length; index++)
                    parts.Add(ReadPositionPart(points[index], positions, index, preserveSourceCoordinates));
                break;
            }
            case "linestring":
                parts.Add(ReadLinePart(geometry.Coordinates, positions, 0, preserveSourceCoordinates));
                break;
            case "multilinestring":
            {
                JsonElement[] lines = RequireItems(geometry.Coordinates, "MultiLineString", 1);
                for (int index = 0; index < lines.Length; index++)
                    parts.Add(ReadLinePart(lines[index], positions, index, preserveSourceCoordinates));
                break;
            }
            case "polygon":
                AddPolygonParts(geometry.Coordinates, positions, 0, parts, preserveSourceCoordinates);
                break;
            case "multipolygon":
            {
                JsonElement[] polygons = RequireItems(geometry.Coordinates, "MultiPolygon", 1);
                for (int polygonIndex = 0; polygonIndex < polygons.Length; polygonIndex++)
                    AddPolygonParts(polygons[polygonIndex], positions, polygonIndex, parts, preserveSourceCoordinates);
                break;
            }
            default:
                throw new NotSupportedException($"The GeoJSON geometry type '{geometry.Type}' is not supported by persisted map vector layers.");
        }
        return parts.ToArray();
    }

    private static PartPlan ReadPositionPart(JsonElement position, List<Position> positions, int partOrdinal,
        bool preserveSourceCoordinates)
    {
        int index = AddPosition(position, positions, preserveSourceCoordinates);
        Position parsed = positions[index];
        return new(partOrdinal, [index], parsed.Z.HasValue || HasOrdinate(position, 2),
            parsed.M.HasValue || HasOrdinate(position, 3), null, null);
    }

    private static PartPlan ReadLinePart(JsonElement line, List<Position> positions, int partOrdinal,
        bool preserveSourceCoordinates)
    {
        JsonElement[] coordinates = RequireItems(line, "LineString", 2);
        int[] indices = coordinates.Select(position => AddPosition(position, positions, preserveSourceCoordinates))
            .ToArray();
        return new(partOrdinal, indices, coordinates.Any(position => HasOrdinate(position, 2)),
            coordinates.Any(position => HasOrdinate(position, 3)), null, null);
    }

    private static void AddPolygonParts(JsonElement polygon, List<Position> positions, int polygonIndex,
        List<PartPlan> parts, bool preserveSourceCoordinates)
    {
        JsonElement[] rings = RequireItems(polygon, "Polygon", 1);
        for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
        {
            JsonElement[] coordinates = RequireItems(rings[ringIndex], "Polygon ring", 4);
            int[] indices = coordinates.Select(position => AddPosition(position, positions, preserveSourceCoordinates))
                .ToArray();
            Position first = positions[indices[0]];
            Position last = positions[indices[^1]];
            if (first.X != last.X || first.Y != last.Y)
                throw new InvalidDataException("A GeoJSON polygon ring must be closed with matching first and last XY coordinates.");
            parts.Add(new(parts.Count, indices,
                coordinates.Any(position => HasOrdinate(position, 2)),
                coordinates.Any(position => HasOrdinate(position, 3)),
                polygonIndex, ringIndex > 0));
        }
    }

    private static JsonElement[] RequireItems(JsonElement array, string geometryName, int minimumCount)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"GeoJSON {geometryName} coordinates have an invalid array nesting level.");
        if (array.GetArrayLength() > MaximumPositionCount)
            throw new NotSupportedException("The GeoJSON source contains too many coordinates or geometry parts for map rendering.");
        JsonElement[] items = array.EnumerateArray().ToArray();
        if (items.Length < minimumCount)
            throw new InvalidDataException($"GeoJSON {geometryName} coordinates contain too few values.");
        return items;
    }

    private static int AddPosition(JsonElement element, List<Position> positions, bool preserveSourceCoordinates)
    {
        if (positions.Count >= MaximumPositionCount)
            throw new NotSupportedException("The GeoJSON source contains too many coordinates for map rendering.");
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() is < 2 or > 4)
            throw new InvalidDataException("A GeoJSON position must contain 2 to 4 ordinates (X, Y, optional Z and M).");
        JsonElement xElement = element[0];
        JsonElement yElement = element[1];
        if (!xElement.TryGetDouble(out double x) || !yElement.TryGetDouble(out double y) ||
            !double.IsFinite(x) || !double.IsFinite(y))
            throw new InvalidDataException("A GeoJSON XY coordinate is missing, nonnumeric, or non-finite.");
        if (!preserveSourceCoordinates && (x is < -180 or > 180 || y is < -90 or > 90))
            throw new InvalidDataException("RFC 7946 GeoJSON coordinates must use longitude [-180, 180] and latitude [-90, 90].");
        double? z = element.GetArrayLength() >= 3 ? ReadOptionalOrdinate(element[2], "Z") : null;
        double? m = element.GetArrayLength() >= 4 ? ReadOptionalOrdinate(element[3], "M") : null;
        positions.Add(new(x, y, z, m));
        return positions.Count - 1;
    }

    private static double? ReadOptionalOrdinate(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetDouble(out double ordinate) || !double.IsFinite(ordinate))
            throw new InvalidDataException($"A GeoJSON {name} ordinate must be finite or null.");
        return ordinate;
    }

    private static bool HasOrdinate(JsonElement position, int index) =>
        position.ValueKind == JsonValueKind.Array && position.GetArrayLength() > index;

    private static Dictionary<string, AttributeValue> ReadProperties(JsonElement properties)
    {
        var values = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        if (properties.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return values;
        if (properties.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GeoJSON feature properties must be an object or null.");
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, ReadAttributeValue(property.Value)))
                throw new InvalidDataException($"GeoJSON feature properties contain the duplicate field '{property.Name}'.");
        }
        return values;
    }

    private static AttributeValue ReadAttributeValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => new("null", null, null),
        JsonValueKind.String => new("string", value.GetString(), null),
        JsonValueKind.Number => new(value.TryGetInt64(out _) ? "integer" : "number", value.GetRawText(), null),
        JsonValueKind.True => new("boolean", null, true),
        JsonValueKind.False => new("boolean", null, false),
        JsonValueKind.Object or JsonValueKind.Array => new("string", value.GetRawText(), null),
        _ => throw new InvalidDataException("GeoJSON properties contain an unsupported JSON value.")
    };

    private static Dictionary<string, int> BuildAttributeSchema(IReadOnlyList<FeaturePlan> features,
        out AttributeColumn[] columns)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var builtColumns = new List<AttributeColumn>();
        foreach (FeaturePlan feature in features)
        {
            foreach ((string name, AttributeValue value) in feature.Properties)
            {
                if (!ordinals.TryGetValue(name, out int ordinal))
                {
                    ordinals.Add(name, ordinal = builtColumns.Count);
                    builtColumns.Add(new(name, value.Type));
                }
                else
                {
                    builtColumns[ordinal].ValueType = MergeValueTypes(builtColumns[ordinal].ValueType, value.Type);
                }
            }
        }
        columns = builtColumns.ToArray();
        return ordinals;
    }

    private static string MergeValueTypes(string left, string right)
    {
        if (left == "null") return right;
        if (right == "null" || left == right) return left;
        if ((left == "integer" && right == "number") || (left == "number" && right == "integer"))
            return "number";
        return "string";
    }

    private static MapVectorSourceAttribute[] BuildAttributes(FeaturePlan feature, AttributeColumn[] columns,
        IReadOnlyDictionary<string, int> columnOrdinals)
    {
        var valuesByOrdinal = new AttributeValue?[columns.Length];
        foreach ((string name, AttributeValue value) in feature.Properties)
            valuesByOrdinal[columnOrdinals[name]] = value;
        var attributes = new MapVectorSourceAttribute[columns.Length];
        for (int ordinal = 0; ordinal < columns.Length; ordinal++)
        {
            AttributeValue? value = valuesByOrdinal[ordinal];
            attributes[ordinal] = new(ordinal, columns[ordinal].Name,
                value?.Type ?? columns[ordinal].ValueType,
                value?.Value, value?.BooleanValue);
        }
        return attributes;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
            return false;
        string? propertyValue = property.GetString();
        if (string.IsNullOrWhiteSpace(propertyValue)) return false;
        value = propertyValue;
        return true;
    }
}
