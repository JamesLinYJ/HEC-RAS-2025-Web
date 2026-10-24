using System.Buffers.Binary;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Backend;

public sealed record MapShapefilePolygonDraftRing(
    double[][] Coordinates,
    string?[]? ZValues = null,
    string?[]? MValues = null,
    bool? HasZ = null,
    bool? HasM = null);

public sealed record MapShapefilePolygonDraftRecord(
    int? AttributeSourceFeatureIndex,
    MapShapefilePolygonDraftRing[] Rings);

public sealed record MapShapefilePolygonDraftSaveRequest(
    string SourcePath,
    string ProjectionStatus,
    string? SourceProjectionWKT,
    string? TargetProjectionWKT,
    MapSourceFamilyCapture ExpectedFamily,
    MapShapefilePolygonDraftRecord[] Records);

public sealed record MapShapefilePolygonDraftSavedRing(
    double[][] SourceCoordinates,
    string?[]? ZValues,
    string?[]? MValues,
    bool HasZ,
    bool HasM);

public sealed record MapShapefilePolygonDraftAttribute(
    int ColumnOrdinal,
    string ColumnName,
    string Type,
    string? Value,
    bool? BooleanValue,
    bool IsSet,
    bool IsNull);

public sealed record MapShapefilePolygonDraftSavedRecord(
    int RecordIndex,
    MapShapefilePolygonDraftSavedRing[] Rings,
    MapShapefilePolygonDraftAttribute[] Attributes);

public sealed record MapShapefilePolygonDraftSaveResponse(
    MapSourceFamilyCapture Family,
    MapShapefilePolygonDraftSavedRecord[] Records);

/// <summary>
/// Saves ordinary polygon Shapefile drafts using the first-party polygon and
/// attribute readers/writers. A complete staged family is reopened and
/// compared before the original family is replaced.
/// </summary>
public static class MapShapefilePolygonDraftService
{
    private const int MaximumRecords = 1_000_000;
    private const int MaximumRingsPerRecord = 100_000;
    private const int MaximumVerticesPerRing = 1_000_000;
    private const long MaximumTotalVertices = 20_000_000;

    public static MapShapefilePolygonDraftSaveResponse Save(MapShapefilePolygonDraftSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string sourcePath = MapShapefileImportService.ValidateSourcePath(request.SourcePath);
        MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);
        ValidateProjectionContract(request);

        OperationResult headerResult = ShapefileIO.TryParseShapefileHeader(sourcePath, out ShapefileHeader originalHeader);
        if (!headerResult.Result || originalHeader is null)
            throw new InvalidDataException(ReadFailure(sourcePath, headerResult));
        if (originalHeader.ShpTypeCode is not (5 or 15 or 25))
            throw new NotSupportedException("Only Polygon, PolygonZ, and PolygonM Shapefiles can be edited here.");
        OperationResult readResult = ShapefileIO.TryRead(sourcePath, out PolygonCollectionFeatureCollection source);
        if (!readResult.Result || source is null)
            throw new InvalidDataException(ReadFailure(sourcePath, readResult));
        Table sourceTable = source.AttributeTable;
        IReadOnlyList<MapShapefileDbfField> sourceDbfSchema =
            MapShapefileAttributeTableDraftWriter.ReadSchema(ShapefileIO.DBFFilename(sourcePath));
        if (sourceTable.Rows.Count != source.Count)
            throw new InvalidDataException("The Shapefile polygon records and attribute rows are not aligned.");
        if (sourceDbfSchema.Count != sourceTable.Columns.Count)
            throw new InvalidDataException("The source DBF descriptor order does not match the decoded attribute table.");

        MapShapefilePolygonDraftRecord[] records = request.Records ?? [];
        if (records.Length > MaximumRecords)
            throw new InvalidDataException("A saved polygon Shapefile must contain a supported number of records.");
        long totalVertices = 0;
        var polygons = new List<ComplexPolygon>(records.Length);
        var savedRecords = new MapShapefilePolygonDraftSavedRecord[records.Length];
        using ReprojectionTransformer? transformer = CreateTargetToSourceTransformer(request, sourcePath);
        bool outputHasZ = originalHeader.ShpTypeCode == 15;

        for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
        {
            MapShapefilePolygonDraftRecord record = records[recordIndex] ??
                throw new InvalidDataException("The polygon draft contains a missing record.");
            MapShapefilePolygonDraftRing[] rings = record.Rings ?? [];
            if (rings.Length == 0 || rings.Length > MaximumRingsPerRecord)
                throw new InvalidDataException("Each polygon record must retain at least one supported ring.");
            if (record.AttributeSourceFeatureIndex is int sourceIndex &&
                (uint)sourceIndex >= (uint)sourceTable.Rows.Count)
                throw new InvalidDataException("A polygon draft refers to an attribute row outside the captured source table.");

            ComplexPolygon? polygon = null;
            var savedRings = new MapShapefilePolygonDraftSavedRing[rings.Length];
            bool? recordHasZ = null;
            bool? recordHasM = null;
            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                MapShapefilePolygonDraftRing ring = rings[ringIndex] ??
                    throw new InvalidDataException("The polygon draft contains a missing ring.");
                double[][] coordinates = ring.Coordinates ?? [];
                if (coordinates.Length < 3 || coordinates.Length > MaximumVerticesPerRing ||
                    coordinates.Any(point => point is null || point.Length != 2 ||
                        !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
                    throw new InvalidDataException("Each polygon ring needs at least three finite XY coordinates.");
                totalVertices += coordinates.Length;
                if (totalVertices > MaximumTotalVertices)
                    throw new InvalidDataException("The polygon draft exceeds the supported vertex count.");

                string?[]? zValues = ring.ZValues;
                string?[]? mValues = ring.MValues;
                bool hasZ = ring.HasZ ?? zValues is not null;
                bool hasM = ring.HasM ?? mValues is not null;
                ValidateOrdinates(zValues, coordinates.Length, "Z", hasZ);
                ValidateOrdinates(mValues, coordinates.Length, "M", hasM);
                if (hasZ && originalHeader.ShpTypeCode != 15)
                    throw new InvalidDataException("The edited polygon adds Z values to a source without Z coordinates.");
                if (hasM && originalHeader.ShpTypeCode is not (15 or 25))
                    throw new InvalidDataException("The edited polygon adds M values to a source without optional M coordinates.");
                if (outputHasZ && !hasZ)
                {
                    if (record.AttributeSourceFeatureIndex is not null)
                        throw new InvalidDataException("An existing PolygonZ record has source Z values that cannot be preserved safely.");
                    zValues = DefaultOrdinates(coordinates.Length);
                    hasZ = true;
                }
                if (!outputHasZ && hasZ)
                    throw new InvalidDataException("The edited polygon adds Z values to a source without Z coordinates.");
                if (originalHeader.ShpTypeCode == 25 && !hasM)
                {
                    if (record.AttributeSourceFeatureIndex is not null)
                        throw new InvalidDataException("An existing PolygonM record has source M values that cannot be preserved safely.");
                    mValues = DefaultOrdinates(coordinates.Length);
                    hasM = true;
                }
                if (!hasM && mValues is not null)
                    throw new InvalidDataException("The edited polygon has M ordinates without an M dimension.");
                if (hasM && mValues is null)
                    throw new InvalidDataException("The edited polygon marks an M dimension without one value slot per vertex.");
                if (!hasM && originalHeader.ShpTypeCode == 25)
                    throw new InvalidDataException("A PolygonM record must retain its M dimension.");
                if (recordHasZ is bool priorZ && priorZ != hasZ || recordHasM is bool priorM && priorM != hasM)
                    throw new InvalidDataException("All rings in one polygon record must share its Z/M dimensions.");
                recordHasZ = hasZ;
                recordHasM = hasM;
                if (mValues is not null && originalHeader.ShpTypeCode is not (15 or 25))
                    throw new InvalidDataException("The edited polygon adds M values to a source without M coordinates.");

                var points = coordinates.Select(point => new Point(point[0], point[1])).ToArray();
                if (transformer is not null && points.Length > 0)
                    transformer.Transform(points.AsMemory(), points.AsMemory());
                if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                    throw new InvalidDataException("The edited polygon could not be transformed into its source projection.");
                var sourceCoordinates = points.Select(point => new[] { point.X, point.Y }).ToArray();
                var sourceRing = new Polygon(points);
                if (zValues is not null) sourceRing.TrackZ(ParseOrdinates(zValues));
                if (mValues is not null) sourceRing.TrackM(ParseOrdinates(mValues));
                if (polygon is null)
                    polygon = ComplexPolygon.FromOuterRing(sourceRing);
                else
                    // Keep the original ring order. The source record's first
                    // ring remains the outer ring; holes are not reclassified.
                    polygon.AddWithoutValidation(sourceRing);
                savedRings[ringIndex] = new(sourceCoordinates, zValues, mValues, hasZ, hasM);
            }
            polygons.Add(polygon ?? throw new InvalidDataException("The polygon draft has no outer ring."));
            // Attributes are populated from the reopened output table below,
            // after the DBF writer has normalized the persisted cell values.
            savedRecords[recordIndex] = new(recordIndex, savedRings, []);
        }

        Table outputTable = sourceTable.CloneStructure();
        foreach (MapShapefilePolygonDraftRecord record in records)
        {
            if (record.AttributeSourceFeatureIndex is int sourceIndex)
            {
                TableRow sourceRow = sourceTable.Rows[sourceIndex];
                var values = new object[sourceTable.Columns.Count];
                for (int column = 0; column < values.Length; column++) values[column] = sourceRow[column];
                outputTable.AddNewRow(values);
            }
            else
            {
                TableRow row = outputTable.AddNewRow();
                for (int column = 0; column < outputTable.Columns.Count; column++)
                    if (outputTable.Columns[column].Type == typeof(string)) row[column] = string.Empty;
            }
        }

        string sourceDirectory = Path.GetDirectoryName(sourcePath) ??
            throw new InvalidDataException("The Shapefile has no parent directory.");
        string stagingDirectory = Path.Combine(sourceDirectory, ".polygon-edit-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(sourceDirectory, ".polygon-edit-backup-" + Guid.NewGuid().ToString("N"));
        bool preserveBackup = false;
        Directory.CreateDirectory(stagingDirectory);
        string stagedPath = Path.Combine(stagingDirectory, Path.GetFileName(sourcePath));
        bool expectedProjectionFile = request.ExpectedFamily.Files.Single(file => file.Role == "prj").Exists;
        string? originalPrj = expectedProjectionFile ? ShapefileIO.PRJFilename(sourcePath) : null;
        try
        {
            OperationResult writeResult = records.Length == 0
                ? WriteEmptyPolygonShapefile(stagedPath, originalHeader.ShpTypeCode)
                : ShapefileIO.Write(stagedPath, polygons, outputTable);
            if (!writeResult.Result) throw new InvalidDataException(ReadFailure(stagedPath, writeResult));
            MapShapefileAttributeTableDraftWriter.Write(ShapefileIO.DBFFilename(sourcePath),
                ShapefileIO.DBFFilename(stagedPath), outputTable,
                records.Select(record => record.AttributeSourceFeatureIndex).ToArray(), sourceTable);
            if (originalPrj is not null)
                File.WriteAllBytes(ShapefileIO.PRJFilename(stagedPath), File.ReadAllBytes(originalPrj));

            RequireCompleteOutput(stagedPath, records.Length, originalHeader.ShpTypeCode, expectedProjectionFile);
            VerifyOutputContents(stagedPath, polygons, outputTable);
            MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);

            Directory.CreateDirectory(backupDirectory);
            string[] extensions = [".shp", ".shx", ".dbf", ".prj"];
            var backedUp = new List<string>();
            var installed = new List<string>();
            try
            {
                foreach (string extension in extensions)
                {
                    string target = Path.ChangeExtension(sourcePath, extension);
                    if (!File.Exists(target)) continue;
                    File.Move(target, Path.Combine(backupDirectory, Path.GetFileName(target)));
                    backedUp.Add(target);
                }
                foreach (string extension in extensions)
                {
                    string staged = Path.ChangeExtension(stagedPath, extension);
                    if (!File.Exists(staged)) continue;
                    string target = Path.ChangeExtension(sourcePath, extension);
                    File.Move(staged, target);
                    installed.Add(target);
                }

                MapSourceFamilyCapture savedFamily = MapShapefileImportService.CaptureFamily(sourcePath);
                RequireCompleteOutput(sourcePath, records.Length, originalHeader.ShpTypeCode, expectedProjectionFile);
                VerifyOutputContents(sourcePath, polygons, outputTable);
                OperationResult committedReadResult = ShapefileIO.TryRead(sourcePath, out PolygonCollectionFeatureCollection committed);
                if (!committedReadResult.Result || committed is null)
                    throw new InvalidDataException(ReadFailure(sourcePath, committedReadResult));
                if (committed.AttributeTable.Rows.Count != records.Length)
                    throw new InvalidDataException("The saved polygon DBF rows could not be read back in order.");
                for (int row = 0; row < savedRecords.Length; row++)
                {
                    if (committed.AttributeTable.Columns.Count != outputTable.Columns.Count)
                        throw new InvalidDataException("The saved polygon DBF schema changed during commit.");
                    var attributes = new MapShapefilePolygonDraftAttribute[committed.AttributeTable.Columns.Count];
                    for (int column = 0; column < attributes.Length; column++)
                    {
                        if (!string.Equals(committed.AttributeTable.Columns[column].Name, outputTable.Columns[column].Name, StringComparison.Ordinal) ||
                            committed.AttributeTable.Columns[column].Type != outputTable.Columns[column].Type)
                            throw new InvalidDataException("The saved polygon DBF schema changed during commit.");
                        attributes[column] = ToWireAttribute(sourceDbfSchema[column],
                            committed.AttributeTable.Rows[row][column]);
                    }
                    savedRecords[row] = savedRecords[row] with { Attributes = attributes };
                }
                if (savedFamily.Family != request.ExpectedFamily.Family ||
                    !SourceFilePaths.SameSourceFile(savedFamily.SourcePath, sourcePath))
                    throw new InvalidDataException("The saved Shapefile family does not match the selected source.");
                return new(savedFamily, savedRecords);
            }
            catch (Exception commitError)
            {
                var rollbackErrors = new List<Exception>();
                foreach (string path in installed.AsEnumerable().Reverse())
                {
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch (Exception error) { rollbackErrors.Add(error); }
                }
                foreach (string path in backedUp.AsEnumerable().Reverse())
                {
                    string backup = Path.Combine(backupDirectory, Path.GetFileName(path));
                    try
                    {
                        if (!File.Exists(backup)) continue;
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(backup, path);
                    }
                    catch (Exception error) { rollbackErrors.Add(error); }
                }
                if (rollbackErrors.Count > 0)
                {
                    preserveBackup = true;
                    throw new IOException(
                        $"The polygon Shapefile save failed and rollback was incomplete. Recovery files remain at '{backupDirectory}'.",
                        new AggregateException(new[] { commitError }.Concat(rollbackErrors)));
                }
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            if (!preserveBackup) TryDeleteDirectory(backupDirectory);
        }
    }

    private static void ValidateProjectionContract(MapShapefilePolygonDraftSaveRequest request)
    {
        switch (request.ProjectionStatus)
        {
            case "projected":
                if (string.IsNullOrWhiteSpace(request.SourceProjectionWKT) ||
                    string.IsNullOrWhiteSpace(request.TargetProjectionWKT))
                    throw new InvalidDataException("The selected Shapefile has no complete source-to-map projection contract.");
                break;
            case "localProjectSourceCoordinates":
                if (!string.IsNullOrWhiteSpace(request.TargetProjectionWKT))
                    throw new InvalidDataException("A local-coordinate Shapefile cannot be saved with a projected map target.");
                break;
            default:
                throw new InvalidDataException("The selected Shapefile projection mode is unsupported.");
        }
    }

    private static ReprojectionTransformer? CreateTargetToSourceTransformer(
        MapShapefilePolygonDraftSaveRequest request, string sourcePath)
    {
        if (request.ProjectionStatus == "localProjectSourceCoordinates") return null;
        string prjPath = ShapefileIO.PRJFilename(sourcePath);
        if (!File.Exists(prjPath)) throw new InvalidDataException("The selected Shapefile source projection file is missing.");
        string expectedSourceWKT = request.SourceProjectionWKT ??
            throw new InvalidDataException("The selected Shapefile source projection is missing.");
        string targetWKT = request.TargetProjectionWKT ??
            throw new InvalidDataException("The captured map projection is missing.");
        var sourceProjection = new ESRIProjection(prjPath);
        if (!string.Equals(sourceProjection.GetWKT(), expectedSourceWKT, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return new ReprojectionTransformer(new WKTProjection(targetWKT), sourceProjection);
    }

    private static void RequireCompleteOutput(string path, int expectedCount, int expectedShapeTypeCode,
        bool expectedProjectionFile)
    {
        foreach (string extension in new[] { ".shp", ".shx", ".dbf" })
            if (!File.Exists(Path.ChangeExtension(path, extension)))
                throw new InvalidDataException("The Shapefile writer did not produce the complete required file family.");
        if (File.Exists(ShapefileIO.PRJFilename(path)) != expectedProjectionFile)
            throw new InvalidDataException("The Shapefile writer changed the source projection sidecar state.");
        OperationResult headerResult = ShapefileIO.TryParseShapefileHeader(path, out ShapefileHeader header);
        if (!headerResult.Result || header is null || header.ShpTypeCode != expectedShapeTypeCode)
            throw new InvalidDataException("The edited Shapefile does not preserve its original geometry dimensions.");
        OperationResult readResult = ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection collection);
        if (!readResult.Result || collection is null || collection.Count != expectedCount ||
            collection.AttributeTable.Rows.Count != expectedCount)
            throw new InvalidDataException("The saved polygon geometry and attribute rows could not be read back in order.");
    }

    private static OperationResult WriteEmptyPolygonShapefile(string path, int shapeTypeCode)
    {
        if (shapeTypeCode is not (5 or 15 or 25))
            throw new InvalidDataException("An empty polygon Shapefile must retain a supported source shape type.");

        // ShapefileIO.Write infers a 2D shape type from its records. With no
        // records, write only the standard 100-byte SHP/SHX headers here so an
        // empty edit preserves the source Polygon/PolygonZ/PolygonM type.
        byte[] header = new byte[100];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 9994);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24, 4), 50);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28, 4), 1000);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32, 4), shapeTypeCode);
        File.WriteAllBytes(path, header);
        File.WriteAllBytes(ShapefileIO.SHXFilename(path), header);

        return OperationResult.Success();
    }

    private static void VerifyOutputContents(string path, IReadOnlyList<ComplexPolygon> expectedPolygons, Table expectedTable)
    {
        OperationResult readResult = ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection collection);
        if (!readResult.Result || collection is null) throw new InvalidDataException(ReadFailure(path, readResult));
        if (collection.Count != expectedPolygons.Count)
            throw new InvalidDataException("The saved polygon record count does not match the draft.");
        for (int record = 0; record < collection.Count; record++)
        {
            PolygonCollection actualRings = collection.Features[record];
            ComplexPolygon expected = expectedPolygons[record];
            if (actualRings.Count != expected.RingCount)
                throw new InvalidDataException("The saved polygon ring grouping does not match the draft.");
            for (int ring = 0; ring < actualRings.Count; ring++)
            {
                Polygon actual = actualRings[ring];
                Polygon expectedRing = expected.Rings[ring];
                if (actual.Count != expectedRing.Count || actual.HasZ != expectedRing.HasZ || actual.HasM != expectedRing.HasM)
                    throw new InvalidDataException("The saved polygon ring dimensions do not match the draft.");
                for (int vertex = 0; vertex < actual.Count; vertex++)
                {
                    if (actual[vertex].X != expectedRing[vertex].X || actual[vertex].Y != expectedRing[vertex].Y ||
                        (actual.HasZ && !SameOrdinate(actual.Z[vertex], expectedRing.Z[vertex])) ||
                        (actual.HasM && !SameOrdinate(actual.M[vertex], expectedRing.M[vertex])))
                        throw new InvalidDataException("The saved polygon coordinates do not match the draft.");
                }
            }
        }

        Table actualTable = collection.AttributeTable;
        if (actualTable.Rows.Count != expectedTable.Rows.Count || actualTable.Columns.Count != expectedTable.Columns.Count)
            throw new InvalidDataException("The saved polygon attribute table does not match the draft.");
        for (int column = 0; column < actualTable.Columns.Count; column++)
        {
            if (actualTable.Columns[column].Name != expectedTable.Columns[column].Name ||
                actualTable.Columns[column].Type != expectedTable.Columns[column].Type)
                throw new InvalidDataException("The saved polygon attribute schema does not match the source.");
        }
        for (int row = 0; row < actualTable.Rows.Count; row++)
        for (int column = 0; column < actualTable.Columns.Count; column++)
            if (!SameTableValue(actualTable.Rows[row][column], expectedTable.Rows[row][column]))
                throw new InvalidDataException("The saved polygon attribute values do not match the draft.");
    }

    private static bool SameTableValue(object? left, object? right)
    {
        if (left is null or DBNull) return right is null or DBNull;
        if (right is null or DBNull) return false;
        if (left.Equals(right)) return true;
        return string.Equals(Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static MapShapefilePolygonDraftAttribute ToWireAttribute(MapShapefileDbfField field, object? value)
    {
        string type = field.ValueType;
        bool isNull = value is null or DBNull;
        if (isNull) return new(field.Ordinal, field.Name, type, null, null, false, true);
        return value switch
        {
            string text when type == "string" => new(field.Ordinal, field.Name, type, text, null, true, false),
            DateTime date when type == "string" => new(field.Ordinal, field.Name, type,
                date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), null, true, false),
            int integer when type == "integer" => new(field.Ordinal, field.Name, type,
                integer.ToString(System.Globalization.CultureInfo.InvariantCulture), null, true, false),
            double number when type == "number" && double.IsFinite(number) => new(field.Ordinal, field.Name, type,
                number.ToString("R", System.Globalization.CultureInfo.InvariantCulture), null, true, false),
            float number when type == "number" && float.IsFinite(number) => new(field.Ordinal, field.Name, type,
                number.ToString("R", System.Globalization.CultureInfo.InvariantCulture), null, true, false),
            bool boolean when type == "boolean" => new(field.Ordinal, field.Name, type, null, boolean, true, false),
            _ => throw new NotSupportedException($"The DBF field '{field.Name}' returned value type '{value?.GetType().Name ?? "null"}' inconsistent with source descriptor '{field.SourceType}'.")
        };
    }

    private static bool SameOrdinate(double left, double right) =>
        left.Equals(right) || double.IsNaN(left) && double.IsNaN(right);

    private static string[] DefaultOrdinates(int count) => Enumerable.Repeat("0", count).ToArray();

    private static void ValidateOrdinates(string?[]? values, int count, string dimension, bool hasDimension)
    {
        if (!hasDimension)
        {
            if (values is not null)
                throw new InvalidDataException("The edited " + dimension + " ordinates have values without a declared dimension.");
            return;
        }
        if (values is null || values.Length != count || values.Any(value =>
                value is not null && (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))))
            throw new InvalidDataException("The edited " + dimension + " coordinates do not match the polygon vertices.");
    }

    private static double[] ParseOrdinates(string?[] values) => values.Select(value => value is null
        ? double.NaN
        : double.Parse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static string ReadFailure(string path, OperationResult result)
    {
        string detail = result.GetConcatenatedMessages();
        return string.IsNullOrWhiteSpace(detail)
            ? $"The polygon Shapefile writer or reader could not process '{Path.GetFileName(path)}'."
            : detail;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
