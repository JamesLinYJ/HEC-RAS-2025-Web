using Geospatial.GDALAssist;
using Geospatial.Features;
using Geospatial.IO;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Backend;

public sealed record MapShapefilePolylineDraftRecord(
    int? AttributeSourceFeatureIndex,
    double[][] Coordinates,
    string?[]? ZValues = null,
    string?[]? MValues = null);

public sealed record MapShapefilePolylineDraftSaveRequest(
    string SourcePath,
    string ProjectionStatus,
    string? SourceProjectionWKT,
    string? TargetProjectionWKT,
    MapSourceFamilyCapture ExpectedFamily,
    MapShapefilePolylineDraftRecord[] Records);

public sealed record MapShapefilePolylineDraftSavedRecord(
    int RecordIndex,
    double[][] SourceCoordinates,
    string?[]? ZValues,
    string?[]? MValues);

public sealed record MapShapefilePolylineDraftSaveResponse(
    MapSourceFamilyCapture Family,
    MapShapefilePolylineDraftSavedRecord[] Records);

/// <summary>
/// Persists the native line editor's flattened Shapefile draft. It applies the
/// exact family snapshot, writes a complete sibling family in a staging folder,
/// preserves the source PRJ bytes, and only then replaces the four recognized
/// family members with rollback if any replacement or readback fails.
/// </summary>
public static class MapShapefilePolylineDraftService
{
    private const int MaximumRecords = 1_000_000;
    private const int MaximumVerticesPerRecord = 10_000;
    private const long MaximumTotalVertices = 20_000_000;

    public static MapShapefilePolylineDraftSaveResponse Save(MapShapefilePolylineDraftSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string sourcePath = MapShapefileImportService.ValidateSourcePath(request.SourcePath);
        MapShapefileImportService.VerifyExpectedFamily(request.ExpectedFamily, sourcePath);
        ValidateProjectionContract(request);

        OperationResult headerResult = ShapefileIO.TryParseShapefileHeader(sourcePath, out ShapefileHeader originalHeader);
        if (!headerResult.Result || originalHeader is null)
            throw new InvalidDataException(ReadFailure(sourcePath, headerResult));
        OperationResult readResult = ShapefileIO.TryRead(sourcePath, out PolylineCollectionFeatureCollection source);
        if (!readResult.Result || source is null)
            throw new InvalidDataException(ReadFailure(sourcePath, readResult));
        Table sourceTable = source.AttributeTable;
        if (sourceTable.Rows.Count != source.Count)
            throw new InvalidDataException("The Shapefile geometry and attribute rows are not aligned.");

        MapShapefilePolylineDraftRecord[] records = request.Records ?? [];
        if (records.Length > MaximumRecords)
            throw new InvalidDataException("The line draft contains too many records.");
        if (originalHeader.ShpTypeCode is not (3 or 13 or 23))
            throw new NotSupportedException($"The Shapefile shape type {originalHeader.ShpTypeCode} is not a supported polyline dimension type.");
        long totalVertices = 0;
        var lines = new List<Polyline>(records.Length);
        var savedRecords = new MapShapefilePolylineDraftSavedRecord[records.Length];
        using ReprojectionTransformer? transformer = CreateTargetToSourceTransformer(request, sourcePath);
        for (int index = 0; index < records.Length; index++)
        {
            MapShapefilePolylineDraftRecord record = records[index] ??
                throw new InvalidDataException("The line draft contains a missing record.");
            double[][] coordinates = record.Coordinates ?? [];
            if (coordinates.Length != 0 && (coordinates.Length < 2 || coordinates.Length > MaximumVerticesPerRecord))
                throw new InvalidDataException("Each edited line must contain a supported number of vertices.");
            if (coordinates.Any(point => point is null || point.Length != 2 ||
                    !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
                throw new InvalidDataException("The line draft contains invalid map coordinates.");
            totalVertices += coordinates.Length;
            if (totalVertices > MaximumTotalVertices)
                throw new InvalidDataException("The line draft exceeds the supported vertex count.");
            ValidateOrdinates(record.ZValues, coordinates.Length, "Z");
            ValidateOrdinates(record.MValues, coordinates.Length, "M");
            bool dimensionsMatch = originalHeader.ShpTypeCode switch
            {
                3 => record.ZValues is null && record.MValues is null,
                13 => record.ZValues is not null,
                23 => record.ZValues is null && record.MValues is not null,
                _ => false
            };
            if (coordinates.Length > 0 && !dimensionsMatch)
                throw new InvalidDataException("The edited line dimensions do not match the source Shapefile shape type.");
            if (record.AttributeSourceFeatureIndex is int row && (uint)row >= (uint)sourceTable.Rows.Count)
                throw new InvalidDataException("A line draft refers to an attribute row outside the captured source table.");

            var line = new Polyline(coordinates.Length);
            foreach (double[] coordinate in coordinates)
                line.Add(new Point(coordinate[0], coordinate[1]));
            if (record.ZValues is not null) line.TrackZ(ParseOrdinates(record.ZValues));
            if (record.MValues is not null) line.TrackM(ParseOrdinates(record.MValues));

            if (transformer is not null && coordinates.Length > 0)
            {
                Memory<Point> points = line.GetPointBufferUnsafe();
                transformer.Transform(points, points);
            }

            double[][] sourceCoordinates = new double[line.Count][];
            for (int vertex = 0; vertex < line.Count; vertex++)
            {
                Point point = line[vertex];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    throw new InvalidDataException("The edited line could not be transformed into its source projection.");
                sourceCoordinates[vertex] = [point.X, point.Y];
            }
            savedRecords[index] = new(index, sourceCoordinates, record.ZValues, record.MValues);
            lines.Add(line);
        }

        Table outputTable = sourceTable.CloneStructure();
        foreach (MapShapefilePolylineDraftRecord record in records)
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
                // Source edit-stack collection appends a new row for each
                // generated line. DBF character fields have no distinct null
                // representation: the writer stores null as blank and the
                // reader returns an empty string. Put that on-disk default in
                // the expected table up front so readback verification remains
                // exact for every persisted value and keeps typed nulls in
                // numeric and logical columns.
                TableRow row = outputTable.AddNewRow();
                for (int column = 0; column < outputTable.Columns.Count; column++)
                    if (outputTable.Columns[column].Type == typeof(string))
                        row[column] = string.Empty;
            }
        }

        string sourceDirectory = Path.GetDirectoryName(sourcePath) ??
            throw new InvalidDataException("The Shapefile has no parent directory.");
        string stagingDirectory = Path.Combine(sourceDirectory, ".polyline-edit-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(sourceDirectory, ".polyline-edit-backup-" + Guid.NewGuid().ToString("N"));
        bool preserveBackup = false;
        Directory.CreateDirectory(stagingDirectory);
        string stagedPath = Path.Combine(stagingDirectory, Path.GetFileName(sourcePath));
        string? originalPrj = request.ExpectedFamily.Files.Single(file => file.Role == "prj").Revision is null
            ? null : ShapefileIO.PRJFilename(sourcePath);
        try
        {
            OperationResult writeResult = ShapefileIO.Write(stagedPath, lines, outputTable);
            if (!writeResult.Result)
                throw new InvalidDataException(ReadFailure(stagedPath, writeResult));
            MapShapefileAttributeTableDraftWriter.Write(ShapefileIO.DBFFilename(sourcePath),
                ShapefileIO.DBFFilename(stagedPath), outputTable,
                records.Select(record => record.AttributeSourceFeatureIndex).ToArray(), sourceTable);
            if (originalPrj is not null)
                File.WriteAllBytes(ShapefileIO.PRJFilename(stagedPath), File.ReadAllBytes(originalPrj));

            RequireCompleteOutput(stagedPath, records.Length, originalHeader.ShpTypeCode,
                request.ExpectedFamily.Files.Single(file => file.Role == "prj").Exists);
            VerifyOutputContents(stagedPath, lines, outputTable);
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
                RequireCompleteOutput(sourcePath, records.Length, originalHeader.ShpTypeCode,
                    request.ExpectedFamily.Files.Single(file => file.Role == "prj").Exists);
                VerifyOutputContents(sourcePath, lines, outputTable);
                MapShapefileImportService.VerifyExpectedFamily(savedFamily, sourcePath);
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
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
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
                        $"The Shapefile save failed and rollback was incomplete. Recovery files remain at '{backupDirectory}'.",
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

    private static void ValidateProjectionContract(MapShapefilePolylineDraftSaveRequest request)
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
        MapShapefilePolylineDraftSaveRequest request, string sourcePath)
    {
        if (request.ProjectionStatus == "localProjectSourceCoordinates") return null;
        string prjPath = ShapefileIO.PRJFilename(sourcePath);
        if (!File.Exists(prjPath))
            throw new InvalidDataException("The selected Shapefile source projection file is missing.");
        var sourceProjection = new ESRIProjection(prjPath);
        if (!string.Equals(sourceProjection.GetWKT(), request.SourceProjectionWKT, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        var targetProjection = new WKTProjection(request.TargetProjectionWKT!);
        return new ReprojectionTransformer(targetProjection, sourceProjection);
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
        OperationResult readResult = ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection collection);
        if (!readResult.Result || collection is null || collection.Count != expectedCount ||
            collection.AttributeTable.Rows.Count != expectedCount)
            throw new InvalidDataException("The saved Shapefile geometry and attribute rows could not be read back in order.");
    }

    private static void VerifyOutputContents(string path, IReadOnlyList<Polyline> expectedLines, Table expectedTable)
    {
        OperationResult readResult = ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection collection);
        if (!readResult.Result || collection is null)
            throw new InvalidDataException(ReadFailure(path, readResult));
        Polyline[] actualLines = collection.ToExplodedPolylineCollection().ToArray();
        Polyline[] nonEmptyExpected = expectedLines.Where(line => line.Count > 0).ToArray();
        if (actualLines.Length != nonEmptyExpected.Length)
            throw new InvalidDataException("The saved Shapefile line parts do not match the edited draft.");
        for (int lineIndex = 0; lineIndex < actualLines.Length; lineIndex++)
        {
            Polyline actual = actualLines[lineIndex];
            Polyline expected = nonEmptyExpected[lineIndex];
            if (actual.Count != expected.Count || actual.HasZ != expected.HasZ || actual.HasM != expected.HasM)
                throw new InvalidDataException("The saved Shapefile line dimensions do not match the edited draft.");
            for (int vertex = 0; vertex < actual.Count; vertex++)
            {
                Point actualPoint = actual[vertex];
                Point expectedPoint = expected[vertex];
                if (actualPoint.X != expectedPoint.X || actualPoint.Y != expectedPoint.Y ||
                    (actual.HasZ && !SameOrdinate(actual.Z[vertex], expected.Z[vertex])) ||
                    (actual.HasM && !SameOrdinate(actual.M[vertex], expected.M[vertex])))
                    throw new InvalidDataException("The saved Shapefile coordinates do not match the edited draft.");
            }
        }

        Table actualTable = collection.AttributeTable;
        if (actualTable.Rows.Count != expectedTable.Rows.Count ||
            actualTable.Columns.Count != expectedTable.Columns.Count)
            throw new InvalidDataException("The saved Shapefile attribute table does not match the edited draft.");
        for (int column = 0; column < actualTable.Columns.Count; column++)
        {
            if (actualTable.Columns[column].Name != expectedTable.Columns[column].Name ||
                actualTable.Columns[column].Type != expectedTable.Columns[column].Type)
                throw new InvalidDataException("The saved Shapefile attribute schema does not match the source.");
        }
        for (int row = 0; row < actualTable.Rows.Count; row++)
        for (int column = 0; column < actualTable.Columns.Count; column++)
        {
            if (!SameTableValue(actualTable.Rows[row][column], expectedTable.Rows[row][column]))
                throw new InvalidDataException("The saved Shapefile attribute values do not match the edited draft.");
        }
    }

    private static bool SameTableValue(object? left, object? right)
    {
        if (left is null or DBNull) return right is null or DBNull;
        if (right is null or DBNull) return false;
        if (left.Equals(right)) return true;
        return string.Equals(Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool SameOrdinate(double left, double right) =>
        left == right || double.IsNaN(left) && double.IsNaN(right);

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ValidateOrdinates(string?[]? values, int coordinateCount, string dimension)
    {
        if (values is null) return;
        if (values.Length != coordinateCount || values.Any(value => value is not null &&
                (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))))
            throw new InvalidDataException("The edited " + dimension + " dimensions do not match the line vertices.");
    }

    private static double[] ParseOrdinates(string?[] values) => values.Select(value => value is null
        ? double.NaN
        : double.Parse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static string ReadFailure(string path, OperationResult result)
    {
        string detail = result.GetConcatenatedMessages();
        return string.IsNullOrWhiteSpace(detail)
            ? $"The Shapefile writer or reader could not process '{Path.GetFileName(path)}'."
            : detail;
    }
}
