using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Logging;

namespace Ras.Backend;

/// <summary>Loads real persisted point, polyline, and polygon map sources for the native renderer.</summary>
public static class MapVectorSourceService
{
    private sealed record DbfField(string Name, char SourceType, int Width, int DecimalCount, string Type);

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

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;
        public void Dispose() => DisposeProject(Project);
    }

    /// <summary>Reads the exact Shapefile or GeoJSON family persisted in the requested .vis map node.</summary>
    public static MapVectorSourceLoadResult Load(MapVectorSourceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Node);

        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedNode(request.Map, request.Node);
        string presentationPath = location.PresentationPath ?? location.SourcePath;
        string extension = Path.GetExtension(presentationPath);
        bool isGeoJson = extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
        string sourcePath;
        MapSourceFamilyCapture family;
        if (isGeoJson)
        {
            sourcePath = Path.GetFullPath(location.SourcePath);
            family = MapGeoJSONSourceService.CaptureFamily(request, location);
        }
        else if (extension.Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            sourcePath = MapShapefileImportService.ValidateSourcePath(location.SourcePath);
            if (request.ExpectedFamily is null)
            {
                family = MapShapefileImportService.CaptureFamily(sourcePath);
            }
            else
            {
                MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);
                family = request.ExpectedFamily;
            }
        }
        else
        {
            throw new NotSupportedException("Only persisted Shapefile and GeoJSON map layers can be loaded as vectors.");
        }

        MapSourceNodeCapture capture = MapSourceIdentity.ResolvePersistedNode(request.Map, request.Node, family);
        MapSourceIdentity.VerifyPersistedNode(request.Map, capture);

        using ProjectScope projectScope = OpenProjectScope(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.CoreLayerContext);
        MapVectorSourceData data = isGeoJson
            ? MapGeoJSONSourceService.ReadData(sourcePath, family, projectScope.Project, request.Node.MapType)
            : ReadData(sourcePath, projectScope.Project);
        string expectedMapType = data.ShapeType switch
        {
            "Point" => "PointsVector",
            "Polyline" => "PolylinesVector",
            "Polygon" => "PolygonsVector",
            _ => throw new InvalidDataException("The vector reader returned an unsupported geometry type.")
        };
        if (!string.Equals(request.Node.MapType, expectedMapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        MapSourceIdentity.VerifyPersistedNode(request.Map, capture);
        return new(capture, data);
    }

    private static MapVectorSourceData ReadData(string sourcePath, Project project)
    {
        OperationResult headerResult = ShapefileIO.TryParseShapefileHeader(sourcePath, out ShapefileHeader header);
        if (!headerResult.Result || header is null)
            throw ReadFailure(sourcePath, headerResult);

        Projection? sourceProjection = File.Exists(ShapefileIO.PRJFilename(sourcePath))
            ? new ESRIProjection(ShapefileIO.PRJFilename(sourcePath))
            : null;
        Projection? targetProjection = project.Projection;
        ReprojectionTransformer? transformer = null;
        string projectionStatus;
        if (targetProjection is not null)
        {
            if (sourceProjection is null)
                throw new InvalidOperationException("The Shapefile has no source projection; it cannot be loaded into this projected map.");
            transformer = new ReprojectionTransformer(sourceProjection, targetProjection);
            projectionStatus = "projected";
        }
        else if (IsValidLocalProject(project))
        {
            projectionStatus = "localProjectSourceCoordinates";
        }
        else
        {
            throw new InvalidOperationException("The project has no valid target projection for this Shapefile source.");
        }
        using (transformer)
        {
            return ReadProjectedData(sourcePath, header, sourceProjection, targetProjection,
                projectionStatus, transformer);
        }
    }

    private static MapVectorSourceData ReadProjectedData(string sourcePath, ShapefileHeader header,
        Projection? sourceProjection, Projection? targetProjection, string projectionStatus,
        ReprojectionTransformer? transformer)
    {

        string? sourceWkt = NonEmptyWkt(sourceProjection);
        string? targetWkt = NonEmptyWkt(targetProjection);
        if (projectionStatus == "projected" && (sourceWkt is null || targetWkt is null))
            throw new InvalidDataException("The source or project projection did not provide a usable WKT definition.");
        string shapeType;
        MapVectorSourceFeature[] features;
        var sourceBounds = new BoundsAccumulator();
        var targetBounds = new BoundsAccumulator();
        DbfField[] dbfFields;
        object?[][] attributeRows;
        ReadDbf(ShapefileIO.DBFFilename(sourcePath), out dbfFields, out attributeRows);

        switch (header.ShpTypeCode)
        {
            case 1:
            case 11:
            case 21:
                if (!ShapefileIO.IsPointShapefile(sourcePath))
                    throw UnsupportedShapeType(header.ShpTypeCode);
                shapeType = "Point";
                OperationResult pointResult = ShapefileIO.TryRead(sourcePath, out PointFeatureCollection? points);
                if (!pointResult.Result || points is null) throw ReadFailure(sourcePath, pointResult);
                features = ReadPointFeatures(points, dbfFields, attributeRows, transformer, sourceBounds, targetBounds);
                break;
            case 3:
            case 13:
            case 23:
                if (!ShapefileIO.IsPolylineShapefile(sourcePath))
                    throw UnsupportedShapeType(header.ShpTypeCode);
                shapeType = "Polyline";
                OperationResult lineResult = ShapefileIO.TryRead(sourcePath, out PolylineCollectionFeatureCollection? lines);
                if (!lineResult.Result || lines is null) throw ReadFailure(sourcePath, lineResult);
                features = ReadPolylineFeatures(lines, dbfFields, attributeRows, transformer, sourceBounds, targetBounds);
                break;
            case 5:
            case 15:
            case 25:
                if (!ShapefileIO.IsPolygonShapefile(sourcePath))
                    throw UnsupportedShapeType(header.ShpTypeCode);
                shapeType = "Polygon";
                OperationResult polygonResult = ShapefileIO.TryRead(sourcePath, out PolygonCollectionFeatureCollection? polygons);
                if (!polygonResult.Result || polygons is null) throw ReadFailure(sourcePath, polygonResult);
                features = ReadPolygonFeatures(polygons, dbfFields, attributeRows, transformer, sourceBounds, targetBounds);
                break;
            default:
                throw UnsupportedShapeType(header.ShpTypeCode);
        }

        if (features.Length != attributeRows.Length)
            throw new InvalidDataException($"The Shapefile reader returned {features.Length} records but the DBF contains {attributeRows.Length}.");

        return new(shapeType, header.ShpTypeCode, features.Length,
            sourceBounds.ToExtent(), targetBounds.ToExtent(), projectionStatus, sourceWkt, targetWkt, features,
            dbfFields.Select((field, ordinal) => new MapVectorAttributeField(
                ordinal, field.Name, field.SourceType.ToString(), field.Width, field.DecimalCount, field.Type))
                .ToArray());
    }

    private static bool IsValidLocalProject(Project project) =>
        project.ProjectionDefinition.Type == ProjectionType.Local &&
        project.ProjectionDefinition.Evaluate(out Projection? evaluatedProjection, out _).Result &&
        evaluatedProjection is null;

    private static string? NonEmptyWkt(Projection? projection)
    {
        if (projection is null) return null;
        string wkt = projection.GetWKT();
        return string.IsNullOrWhiteSpace(wkt) ? null : wkt;
    }

    private static MapVectorSourceFeature[] ReadPointFeatures(PointFeatureCollection source,
        DbfField[] fields, object?[][] rows, ReprojectionTransformer? transformer,
        BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds)
    {
        PointCollection points = source.Features as PointCollection ?? source.AsPointCollection();
        EnsureRecordCount(points.Count, rows.Length);
        var features = new MapVectorSourceFeature[points.Count];
        for (int record = 0; record < points.Count; record++)
        {
            Point point = points[record];
            MapVectorSourcePart[] parts = point.IsNull()
                ? []
                : [new(0, ReadPart(points, new[] { point }, record, transformer, sourceBounds, targetBounds),
                    points.HasZ, points.HasM)];
            features[record] = new(record, null, "Point", parts, CreateAttributes(fields, rows[record]));
        }
        return features;
    }

    private static MapVectorSourceFeature[] ReadPolylineFeatures(PolylineCollectionFeatureCollection source,
        DbfField[] fields, object?[][] rows, ReprojectionTransformer? transformer,
        BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds)
    {
        EnsureRecordCount(source.Count, rows.Length);
        var features = new MapVectorSourceFeature[source.Count];
        for (int record = 0; record < source.Count; record++)
        {
            PolylineCollection paths = source.Features[record];
            var parts = new List<MapVectorSourcePart>(paths.Count);
            for (int part = 0; part < paths.Count; part++)
            {
                if (paths[part].Count == 0) continue;
                parts.Add(new(part, ReadPart(paths[part], paths[part], 0, transformer,
                    sourceBounds, targetBounds), paths[part].HasZ, paths[part].HasM));
            }
            features[record] = new(record, null, "Polyline", parts.ToArray(), CreateAttributes(fields, rows[record]));
        }
        return features;
    }

    private static MapVectorSourceFeature[] ReadPolygonFeatures(PolygonCollectionFeatureCollection source,
        DbfField[] fields, object?[][] rows, ReprojectionTransformer? transformer,
        BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds)
    {
        EnsureRecordCount(source.Count, rows.Length);
        var features = new MapVectorSourceFeature[source.Count];
        for (int record = 0; record < source.Count; record++)
        {
            PolygonCollection rings = source.Features[record];
            var parts = new List<MapVectorSourcePart>(rings.Count);
            for (int part = 0; part < rings.Count; part++)
            {
                if (rings[part].Count == 0) continue;
                parts.Add(new(part, ReadPart(rings[part], rings[part], 0, transformer,
                    sourceBounds, targetBounds), rings[part].HasZ, rings[part].HasM));
            }
            features[record] = new(record, null, "Polygon", parts.ToArray(), CreateAttributes(fields, rows[record]));
        }
        return features;
    }

    private static MapVectorSourceVertex[] ReadPart(PointCollection geometry, IReadOnlyList<Point> points,
        int start, ReprojectionTransformer? transformer, BoundsAccumulator sourceBounds,
        BoundsAccumulator targetBounds)
    {
        if (points.Count == 0) throw new InvalidDataException("A Shapefile feature contains an empty geometry part.");
        var sourcePoints = new Point[points.Count];
        var targetPoints = new Point[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            Point point = points[index];
            EnsureFinite(point.X, point.Y, "source");
            sourcePoints[index] = point;
            sourceBounds.Add(point.X, point.Y);
        }

        if (transformer is null)
            Array.Copy(sourcePoints, targetPoints, sourcePoints.Length);
        else
            transformer.Transform(sourcePoints.AsMemory(), targetPoints.AsMemory());

        var vertices = new MapVectorSourceVertex[points.Count];
        for (int index = 0; index < targetPoints.Length; index++)
        {
            Point target = targetPoints[index];
            EnsureFinite(target.X, target.Y, "projected");
            targetBounds.Add(target.X, target.Y);
            double? z = geometry.HasZ ? FiniteOrNull(geometry.Z[start + index]) : null;
            double? m = geometry.HasM ? FiniteOrNull(geometry.M[start + index]) : null;
            Point source = sourcePoints[index];
            vertices[index] = new(target.X, target.Y, source.X, source.Y, z, m);
        }
        return vertices;
    }

    private static MapVectorSourceAttribute[] CreateAttributes(DbfField[] fields, object?[] values)
    {
        if (fields.Length != values.Length)
            throw new InvalidDataException("The DBF record did not contain the declared ordered columns.");
        var attributes = new MapVectorSourceAttribute[fields.Length];
        for (int column = 0; column < fields.Length; column++)
        {
            DbfField field = fields[column];
            if (string.IsNullOrEmpty(field.Name))
                throw new InvalidDataException($"DBF column {column} has no usable field name.");
            object? value = values[column];
            (string? text, bool? boolean) = value switch
            {
                null => ((string?)null, (bool?)null),
                string stringValue => (stringValue, (bool?)null),
                int integerValue => (integerValue.ToString(CultureInfo.InvariantCulture), (bool?)null),
                double numberValue when double.IsFinite(numberValue) => (numberValue.ToString("R", CultureInfo.InvariantCulture), (bool?)null),
                bool booleanValue => ((string?)null, booleanValue),
                _ => throw new InvalidDataException($"DBF column {column} has an unsupported decoded value type.")
            };
            attributes[column] = new(column, field.Name, field.Type, text, boolean);
        }
        return attributes;
    }

    /// <summary>
    /// Reads DBF descriptors by ordinal. DbfIO's Table reader resolves duplicate
    /// field names to the first column, so it cannot be used for this wire snapshot.
    /// Value decoding follows its retained field-type and null conventions.
    /// </summary>
    private static void ReadDbf(string path, out DbfField[] fields, out object?[][] rows)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 33) throw new InvalidDataException("The DBF header is truncated.");
        int rowCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2));
        if (rowCount < 0 || headerLength < 33 || headerLength > bytes.Length || recordLength < 1 ||
            bytes[headerLength - 1] != 13)
            throw new InvalidDataException("The DBF header has invalid record or field metadata.");

        var parsedFields = new List<DbfField>();
        for (int offset = 32; offset + 32 <= headerLength - 1; offset += 32)
        {
            ReadOnlySpan<byte> descriptor = bytes.AsSpan(offset, 32);
            string name = Encoding.UTF8.GetString(descriptor[..11]).Trim().Trim('\0');
            if (name.Length == 0) throw new InvalidDataException("The DBF contains an unnamed field.");
            char sourceType = (char)descriptor[11];
            int width = descriptor[16];
            int decimalCount = descriptor[17];
            string type = sourceType switch
            {
                'I' => "integer",
                'N' when decimalCount > 0 => "number",
                'N' => "integer",
                'F' => "number",
                'L' => "boolean",
                _ => "string"
            };
            if (width <= 0) throw new InvalidDataException($"DBF field '{name}' declares an empty value width.");
            parsedFields.Add(new(name, sourceType, width, decimalCount, type));
        }

        fields = parsedFields.ToArray();
        int fieldBytes = fields.Sum(field => field.Width);
        if (fieldBytes + 1 > recordLength || (long)headerLength + (long)rowCount * recordLength > bytes.LongLength)
            throw new InvalidDataException("The DBF record layout is truncated or inconsistent.");

        rows = new object?[rowCount][];
        for (int record = 0; record < rowCount; record++)
        {
            int cursor = checked(headerLength + record * recordLength + 1);
            var values = new object?[fields.Length];
            for (int column = 0; column < fields.Length; column++)
            {
                DbfField field = fields[column];
                ReadOnlySpan<byte> raw = bytes.AsSpan(cursor, field.Width);
                values[column] = DecodeDbfValue(field, raw);
                cursor += field.Width;
            }
            rows[record] = values;
        }
    }

    private static object? DecodeDbfValue(DbfField field, ReadOnlySpan<byte> raw)
    {
        if ((field.Type is "integer" or "number") && raw.IndexOfAnyExcept((byte)' ', (byte)0) < 0)
            return null;

        if (field.Type == "boolean")
        {
            if (raw.IsEmpty) return null;
            return raw[0] switch
            {
                (byte)'T' or (byte)'t' or (byte)'Y' or (byte)'y' => true,
                (byte)'F' or (byte)'f' or (byte)'N' or (byte)'n' => false,
                (byte)'?' or (byte)' ' or 0 => null,
                _ => throw new InvalidDataException($"DBF field '{field.Name}' contains an invalid logical value.")
            };
        }

        if (field.Type == "integer")
        {
            // Match DbfIO's Int32 output and ASCII integer parser for I/N fields.
            string digits = Encoding.ASCII.GetString(raw).Trim();
            if (digits.Length == 0) return null;
            if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed))
                return -1;
            return unchecked((int)parsed);
        }

        if (field.Type == "number")
        {
            string digits = Encoding.ASCII.GetString(raw).Trim();
            if (digits.Length == 0) return null;
            if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                !double.IsFinite(parsed))
                throw new InvalidDataException($"DBF field '{field.Name}' contains a non-finite or invalid numeric value.");
            return parsed;
        }

        int firstNonSpace = 0;
        while (firstNonSpace < raw.Length && raw[firstNonSpace] == (byte)' ') firstNonSpace++;
        return Encoding.UTF8.GetString(raw[firstNonSpace..]);
    }

    private static void EnsureRecordCount(int geometryCount, int dbfCount)
    {
        if (geometryCount != dbfCount)
            throw new InvalidDataException($"The SHP reader returned {geometryCount} records but the DBF declares {dbfCount}.");
    }

    private static void EnsureFinite(double x, double y, string coordinateKind)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new InvalidDataException($"A Shapefile {coordinateKind} coordinate is not finite.");
    }

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static NotSupportedException UnsupportedShapeType(int code) =>
        new($"Shapefile shape type {code} is not supported by persisted map vector layers.");

    private static InvalidDataException ReadFailure(string path, OperationResult operation)
    {
        string detail = operation.GetConcatenatedMessages();
        return new InvalidDataException(string.IsNullOrWhiteSpace(detail)
            ? $"The Shapefile source reader could not read '{Path.GetFileName(path)}'."
            : detail);
    }

    private static ProjectScope OpenProjectScope(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext) =>
        new(CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml, coreLayerContext));

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }
}
