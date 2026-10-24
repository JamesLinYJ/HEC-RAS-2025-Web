using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Observables;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private sealed record GeometryMeshExportOperation(string Fingerprint,
        TaskCompletionSource<GeometryMeshShapefileExportResult> Completion);
    private sealed record CompletedGeometryMeshExport(string Fingerprint,
        GeometryMeshShapefileExportResult Result);

    private static readonly object GeometryMeshExportGate = new();
    private static readonly Dictionary<string, CompletedGeometryMeshExport> CompletedGeometryMeshExports =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, GeometryMeshExportOperation> PendingGeometryMeshExports =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<GeometryMeshElementFindResult> FindGeometryMeshElementAsync(
        GeometryMeshElementFindRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGeometryMeshToolRequest(request.Model, request.Revision, request.DraftState,
            request.TopologyDraftState, request.MeshDraftSHA256, request.TopologyDraftSHA256);
        if (request.Index < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Index), "The mesh element index cannot be negative.");

        string revision = Revision(request.Model.AssetPath);
        if (!revision.Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        ApplyGeometryTopologyDraft(context, request.Model.AssetPath, revision, request.TopologyDraftState);
        string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
        GeometryMeshDraftState meshState = ReadGeometryMeshDraft(request.DraftState,
            request.Model.AssetPath, revision);
        EnsureTopologyFingerprint(meshState, topologyFingerprint);
        _ = await ApplyGeometryMeshMutationsAsync(context, request.Model.AssetPath, revision,
            meshState, replayPropertyTableComputations: false).ConfigureAwait(false);

        var (geometry, _) = GeometryMeshTopologyFor(context);
        var (flowArea, mesh) = ResolveFlowArea(geometry,
            request.FlowAreaID, request.ExpectedFlowAreaName);
        if (mesh.CellCount <= 0)
            throw new InvalidOperationException("The selected flow area has no mesh to search.");

        Point center;
        double viewportSize;
        GeometryTopologyCoordinate[] coordinates;
        switch (request.ElementKind)
        {
            case "Cell":
            {
                if (request.Index >= mesh.CellCount)
                    throw new ArgumentOutOfRangeException(nameof(request.Index), "The cell index is outside the current mesh.");
                center = mesh.CellCenters[request.Index];
                double area = mesh.CellPolygonArea(request.Index);
                if (!double.IsFinite(area) || area <= 0)
                    throw new InvalidDataException("The selected mesh cell has no finite positive area.");
                viewportSize = Math.Sqrt(area) * 2.0;
                Point[] points = mesh.CellPolygon(request.Index).Points.ToArray();
                coordinates = Coordinates(points, "cell polygon", minimumCount: 3);
                break;
            }
            case "Face":
            {
                if (request.Index >= mesh.FaceCount)
                    throw new ArgumentOutOfRangeException(nameof(request.Index), "The face index is outside the current mesh.");
                center = mesh.FaceCentroidPoint(request.Index);
                viewportSize = mesh.FacePolylineLength(request.Index) * 2.0;
                Polyline line = mesh.FacePolyline(request.Index);
                coordinates = Coordinates(PolylinePoints(line), "face polyline", minimumCount: 2);
                break;
            }
            case "Node":
            {
                if (request.Index >= mesh.NodeCount || request.Index >= mesh.FacePoints.Length)
                    throw new ArgumentOutOfRangeException(nameof(request.Index), "The node index is outside the current mesh.");
                center = mesh.FacePoints[request.Index].Point;
                viewportSize = mesh.FaceSimpleLength(0) * 2.0;
                coordinates = Coordinates([center], "mesh node", minimumCount: 1);
                break;
            }
            default:
                throw new ArgumentException("Choose Cell, Face, or Node as the mesh element kind.", nameof(request.ElementKind));
        }

        if (!Finite(center) || !double.IsFinite(viewportSize) || viewportSize < 0)
            throw new InvalidDataException("The selected mesh element has invalid source coordinates or viewport dimensions.");
        if (!Revision(request.Model.AssetPath).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        ValidateCurrentProjectCoreReferences(request.Model);

        return new GeometryMeshElementFindResult(
            SourceFilePaths.Canonical(request.Model.AssetPath), revision,
            request.FlowAreaID, flowArea.Name, request.ElementKind, request.Index,
            request.MeshDraftSHA256, request.TopologyDraftSHA256,
            center.X, center.Y,
            FindViewport(center, viewportSize),
            coordinates);
    }

    internal static Task<GeometryMeshShapefileExportResult> ExportGeometryMeshShapefileAsync(
        GeometryMeshShapefileExportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGeometryMeshToolRequest(request.Model, request.Revision, request.DraftState,
            request.TopologyDraftState, request.MeshDraftSHA256, request.TopologyDraftSHA256);
        if (!Guid.TryParse(request.OperationID, out Guid operationID))
            throw new ArgumentException("The mesh Shapefile export operation identity is invalid.", nameof(request.OperationID));
        if (request.ExportKind is not ("CellCenters" or "PerimeterFaces"))
            throw new NotSupportedException("Choose CellCenters or PerimeterFaces for the mesh Shapefile export.");
        string destination = ExportOutputCommit.ValidateOutputPath(request.OutputPath, ".shp");
        string operationKey = operationID.ToString("N");
        string fingerprint = GeometryMeshExportFingerprint(request, destination);

        TaskCompletionSource<GeometryMeshShapefileExportResult> completion;
        lock (GeometryMeshExportGate)
        {
            if (CompletedGeometryMeshExports.TryGetValue(operationKey, out CompletedGeometryMeshExport? cached))
            {
                if (!cached.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
                    throw new ArgumentException("This mesh export operation ID was already used for a different request.");
                return Task.FromResult(cached.Result);
            }
            if (PendingGeometryMeshExports.TryGetValue(operationKey, out GeometryMeshExportOperation? pending))
            {
                if (!pending.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
                    throw new ArgumentException("This mesh export operation ID is already running for a different request.");
                return pending.Completion.Task;
            }

            completion = new TaskCompletionSource<GeometryMeshShapefileExportResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PendingGeometryMeshExports.Add(operationKey, new(fingerprint, completion));
        }

        return RunGeometryMeshExportAsync(request, destination, operationKey, fingerprint,
            completion, report, duplexControls);
    }

    private static async Task<GeometryMeshShapefileExportResult> RunGeometryMeshExportAsync(
        GeometryMeshShapefileExportRequest request, string destination, string operationKey,
        string fingerprint, TaskCompletionSource<GeometryMeshShapefileExportResult> completion,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader? duplexControls)
    {
        var reporter = new ProgressReporter();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));
        using var stopControls = new CancellationTokenSource();
        Task? controlPump = duplexControls is null ? null : Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControls.Token)
                               .ConfigureAwait(false))
            {
                if (!frame.Control.TryGetProperty("type", out JsonElement type) ||
                    !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal)) continue;
                reporter.RequestCancel();
            }
        }, stopControls.Token);

        try
        {
            GeometryMeshShapefileExportResult result = await Task.Run(() =>
                ExportGeometryMeshShapefileInWorkerAsync(request, destination, reporter, report),
                CancellationToken.None).ConfigureAwait(false);
            lock (GeometryMeshExportGate)
            {
                PendingGeometryMeshExports.Remove(operationKey);
                if (result.State == "completed")
                    CompletedGeometryMeshExports[operationKey] = new(fingerprint, result);
            }
            completion.TrySetResult(result);
            return result;
        }
        catch (Exception error)
        {
            lock (GeometryMeshExportGate) PendingGeometryMeshExports.Remove(operationKey);
            completion.TrySetException(error);
            // Observe the shared task fault when this request has no duplicate
            // waiter. Existing duplicate waiters can still await the same task
            // and receive the original exception.
            _ = completion.Task.Exception;
            throw;
        }
        finally
        {
            await ExportOutputCommit.StopAndObserveAsync(stopControls, controlPump,
                "Geometry mesh export control pump").ConfigureAwait(false);
        }
    }

    private static async Task<GeometryMeshShapefileExportResult> ExportGeometryMeshShapefileInWorkerAsync(
        GeometryMeshShapefileExportRequest request, string destination, ProgressReporter reporter,
        Action<BackendOperationProgress>? report)
    {
        string revision = Revision(request.Model.AssetPath);
        if (!revision.Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        ModelContext context = new(ToModelRequest(request.Model));
        bool contextDisposed = false;
        try
        {
            ApplyGeometryTopologyDraft(context, request.Model.AssetPath, revision, request.TopologyDraftState);
            string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
            GeometryMeshDraftState meshState = ReadGeometryMeshDraft(request.DraftState,
                request.Model.AssetPath, revision);
            EnsureTopologyFingerprint(meshState, topologyFingerprint);
            _ = await ApplyGeometryMeshMutationsAsync(context, request.Model.AssetPath, revision,
                meshState, replayPropertyTableComputations: false).ConfigureAwait(false);

            var (geometry, _) = GeometryMeshTopologyFor(context);
            (Observable2DFlowArea flowArea, Mesh mesh) = ResolveFlowArea(geometry,
                request.FlowAreaID, request.ExpectedFlowAreaName);
            if (mesh.CellCount <= 0)
                throw new InvalidOperationException("The selected flow area has no mesh to export.");
            Projection? projection = ResolveGeometryMeshExportProjection(context.Project);
            string? projectionWKT = projection?.GetWKT();
            if (projection is not null && string.IsNullOrWhiteSpace(projectionWKT))
                throw new InvalidDataException("The project projection did not provide a usable WKT definition.");

            string? temporaryDirectory = null;
            try
            {
                if (reporter.CancelRequested)
                    return GeometryMeshExportCancelled(request, flowArea.Name, projectionWKT);
                ExportOutputCommit.EnsureNoClobber(destination);

                int featureCount;
                if (request.ExportKind == "CellCenters")
                {
                    report?.Invoke(new(TaskName: "Collecting mesh cell centers"));
                    Memory<Point> sourceCenters = mesh.CellCenters;
                    Point[] centers = sourceCenters.ToArray();
                    if (centers.Length != mesh.CellCount)
                        throw new InvalidDataException("The source mesh returned an incomplete cell-center collection.");
                    for (int index = 0; index < centers.Length; index++)
                    {
                        if ((index & 0x3ff) == 0 && reporter.CancelRequested)
                            return GeometryMeshExportCancelled(request, flowArea.Name, projectionWKT);
                        if (!Finite(centers[index]))
                            throw new InvalidDataException("The source mesh contains a non-finite cell center.");
                    }
                    featureCount = centers.Length;
                    temporaryDirectory = ExportOutputCommit.CreateTemporaryDirectory(destination);
                    string stagedShp = Path.Combine(temporaryDirectory, Path.GetFileName(destination));
                    report?.Invoke(new(TaskName: "Writing mesh cell centers"));
                    ShapefileIO.Write_GDAL(stagedShp, "MeshCellCenters", centers.AsMemory(), projection);
                    ValidateStagedShapefile(stagedShp, "PointsVector", "Point", featureCount, projection);
                    string flowAreaName = flowArea.Name;
                    context.Dispose();
                    contextDisposed = true;
                    return CommitGeometryMeshShapefile(request, destination, stagedShp, flowAreaName,
                        featureCount, projectionWKT, reporter);
                }

                report?.Invoke(new(TaskName: "Collecting mesh perimeter faces"));
                var lines = new List<Polyline>();
                var attributes = new Table();
                attributes.AddColumn("Face", typeof(int));
                attributes.AddColumn("CellA", typeof(int));
                attributes.AddColumn("CellB", typeof(int));
                attributes.AddColumn("Length", typeof(double));
                for (int index = 0; index < mesh.Faces.Length; index++)
                {
                    if ((index & 0x3ff) == 0 && reporter.CancelRequested)
                        return GeometryMeshExportCancelled(request, flowArea.Name, projectionWKT);
                    Face face = mesh.Faces[index];
                    if (face.cellA >= 0 && face.cellB >= 0) continue;
                    Polyline line = mesh.FacePolyline(index);
                    _ = Coordinates(PolylinePoints(line), "perimeter face polyline", minimumCount: 2);
                    lines.Add(line);
                    attributes.AddNewRow([index, face.cellA, face.cellB, line.Length]);
                }
                featureCount = lines.Count;
                if (reporter.CancelRequested)
                    return GeometryMeshExportCancelled(request, flowArea.Name, projectionWKT);
                temporaryDirectory = ExportOutputCommit.CreateTemporaryDirectory(destination);
                string stagedPerimeterShp = Path.Combine(temporaryDirectory, Path.GetFileName(destination));
                report?.Invoke(new(TaskName: "Writing mesh perimeter faces"));
                OperationResult write = ShapefileIO.Write(stagedPerimeterShp, lines, attributes, projection);
                if (!write.Result)
                    throw new IOException("The retained Shapefile writer failed: " +
                        string.Join(" ", write.Messages.Select(message => message.EvaluatedMessage)));
                ValidateStagedShapefile(stagedPerimeterShp, "PolylinesVector", "Polyline", featureCount, projection);
                ValidatePerimeterAttributes(stagedPerimeterShp, featureCount);
                string perimeterFlowAreaName = flowArea.Name;
                context.Dispose();
                contextDisposed = true;
                return CommitGeometryMeshShapefile(request, destination, stagedPerimeterShp, perimeterFlowAreaName,
                    featureCount, projectionWKT, reporter);
            }
            finally
            {
                if (temporaryDirectory is not null)
                    ExportOutputCommit.TryCleanup(() => ExportOutputCommit.DeleteDirectoryIfPresent(temporaryDirectory),
                        "temporary Geometry mesh Shapefile directory");
            }
        }
        finally
        {
            if (!contextDisposed)
                ExportOutputCommit.TryCleanup(context.Dispose,
                    "temporary Geometry mesh model context");
        }
    }

    private static GeometryMeshShapefileExportResult CommitGeometryMeshShapefile(
        GeometryMeshShapefileExportRequest request, string destination, string stagedShp,
        string flowAreaName, int featureCount, string? projectionWKT, ProgressReporter reporter)
    {
        ExportOutputCommit.PreparedShapefileFamily prepared =
            ExportOutputCommit.PrepareShapefileFamily(stagedShp, destination);
        ExportOutputCommit.VerifyPreparedShapefileFamily(prepared);
        var successResult = new GeometryMeshShapefileExportResult(
            request.OperationID, "completed", SourceFilePaths.Canonical(request.Model.AssetPath),
            request.Revision, request.FlowAreaID, flowAreaName, request.ExportKind,
            request.MeshDraftSHA256, request.TopologyDraftSHA256, destination,
            featureCount, projectionWKT, prepared.Receipts);
        if (!Revision(request.Model.AssetPath).Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        ValidateCurrentProjectCoreReferences(request.Model);
        if (reporter.CancelRequested)
            return GeometryMeshExportCancelled(request, flowAreaName, projectionWKT);

        ExportDestinationLease lease = ExportDestinationLease.Acquire(destination);
        bool published = false;
        try
        {
            ExportOutputCommit.EnsureNoClobber(destination);
            ExportOutputCommit.VerifyPreparedShapefileFamily(prepared);
            if (!Revision(request.Model.AssetPath).Equals(request.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            ValidateCurrentProjectCoreReferences(request.Model);
            if (reporter.CancelRequested)
                return GeometryMeshExportCancelled(request, flowAreaName, projectionWKT);

            _ = ExportOutputCommit.CommitPreparedShapefileFamily(prepared);
            published = true;
        }
        finally
        {
            if (published)
                ExportOutputCommit.TryCleanup(lease.Dispose, "mesh Shapefile destination lease");
            else
                lease.Dispose();
        }

        return successResult;
    }

    private static GeometryMeshShapefileExportResult GeometryMeshExportCancelled(
        GeometryMeshShapefileExportRequest request, string flowAreaName, string? projectionWKT) =>
        new(request.OperationID, "cancelled", SourceFilePaths.Canonical(request.Model.AssetPath),
            request.Revision, request.FlowAreaID, flowAreaName, request.ExportKind,
            request.MeshDraftSHA256, request.TopologyDraftSHA256, null, 0, projectionWKT, []);

    private static void ValidateGeometryMeshToolRequest(GeometryMeshModelRequest model, string revision,
        string draftState, string? topologyDraftState, string meshDraftSHA256, string topologyDraftSHA256)
    {
        ArgumentNullException.ThrowIfNull(model);
        VerifyGeometryMeshModel(model);
        if (string.IsNullOrWhiteSpace(revision) || revision.Length != 64 || !revision.All(Uri.IsHexDigit))
            throw new ArgumentException("The captured Geometry source revision is invalid.", nameof(revision));
        if (string.IsNullOrEmpty(draftState))
            throw new ArgumentException("The captured Geometry mesh draft is required.", nameof(draftState));
        VerifyDraftDigest(draftState, meshDraftSHA256, "mesh");
        VerifyDraftDigest(topologyDraftState ?? "", topologyDraftSHA256, "topology");
    }

    private static (Observable2DFlowArea FlowArea, Mesh Mesh) ResolveFlowArea(
        ObservableGeometry geometry, string flowAreaID, string expectedFlowAreaName)
    {
        const string prefix = "flow-area-row:";
        if (string.IsNullOrWhiteSpace(flowAreaID) || !flowAreaID.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(flowAreaID.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out int meshIndex) || meshIndex < 0 || meshIndex >= geometry.FlowAreaLayer.Features.Count ||
            !string.Equals(FlowAreaID(meshIndex), flowAreaID, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        Observable2DFlowArea flowArea = geometry.FlowAreaLayer.Features[meshIndex];
        if (!string.Equals(flowArea.Name, expectedFlowAreaName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        Mesh mesh = flowArea.Mesh ?? throw new InvalidOperationException("The selected flow area has no mesh.");
        return (flowArea, mesh);
    }

    private static GeometryTopologyCoordinate[] Coordinates(Point[] points, string description, int minimumCount)
    {
        if (points.Length < minimumCount || points.Any(point => !Finite(point)))
            throw new InvalidDataException("The source " + description + " has too few or non-finite coordinates.");
        return points.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();
    }

    private static Point[] PolylinePoints(Polyline line)
    {
        Point[] points = new Point[line.Count];
        for (int index = 0; index < points.Length; index++) points[index] = line[index];
        return points;
    }

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static GeometryMeshExtent FindViewport(Point center, double size)
    {
        if (!Finite(center) || !double.IsFinite(size) || size < 0)
            throw new InvalidDataException("The selected mesh element has invalid source coordinates or viewport dimensions.");
        Extent extent = Extent.CenteredOn(center, size).EnforceMinDimensions(1.0, 1.0);
        if (!extent.IsFiniteAndValid())
            throw new InvalidDataException("The selected mesh element produced an invalid source viewport.");
        return new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
    }

    private static Projection? ResolveGeometryMeshExportProjection(Project project)
    {
        ProjectionDefinition definition = project.ProjectionDefinition;
        OperationResult evaluation = definition.Evaluate(out Projection? projection, out _);
        if (!evaluation.Result)
            throw new InvalidDataException("The project projection is invalid; fix it before exporting a mesh Shapefile.");
        if (definition.Type == ProjectionType.Local)
        {
            if (projection is not null)
                throw new InvalidDataException("The local-coordinate project unexpectedly resolved a projected CRS.");
            return null;
        }
        if (projection is null)
            throw new InvalidDataException("The project projection could not be written to the mesh Shapefile.");
        return projection;
    }

    private static void ValidateStagedShapefile(string path, string expectedMapType,
        string expectedShapeType, int expectedFeatureCount, Projection? projection)
    {
        MapShapefileSourceReadResult source = MapShapefileImportService.Read(path);
        if (source.MapType != expectedMapType || source.ShapeType != expectedShapeType ||
            source.FeatureCount != expectedFeatureCount)
            throw new InvalidDataException("The retained Shapefile reader did not verify the exported mesh geometry.");
        foreach (string member in new[] { path, ShapefileIO.SHXFilename(path), ShapefileIO.DBFFilename(path) })
            if (!File.Exists(member) || new FileInfo(member).Length == 0)
                throw new InvalidDataException("The mesh Shapefile writer did not create a complete required family.");

        string projectionPath = ShapefileIO.PRJFilename(path);
        if (projection is null)
        {
            if (File.Exists(projectionPath))
                throw new InvalidDataException("A Local-coordinate mesh Shapefile must not claim an undefined projection.");
            return;
        }
        if (!File.Exists(projectionPath) || new FileInfo(projectionPath).Length == 0)
            throw new InvalidDataException("The projected mesh Shapefile writer did not create a .prj sidecar.");
        var writtenProjection = new ESRIProjection(projectionPath);
        if (!writtenProjection.IsEqual(projection))
            throw new InvalidDataException("The mesh Shapefile projection sidecar does not match the project CRS.");
    }

    private static void ValidatePerimeterAttributes(string path, int expectedRows)
    {
        OperationResult read = DbfIO.TryRead(ShapefileIO.DBFFilename(path), out Table table);
        if (!read.Result)
            throw new InvalidDataException("The mesh perimeter DBF attributes could not be read back.");
        string[] names = table.Columns.Select(column => column.Name).ToArray();
        Type[] types = table.Columns.Select(column => column.Type).ToArray();
        if (!names.SequenceEqual(["Face", "CellA", "CellB", "Length"], StringComparer.Ordinal) ||
            !types.SequenceEqual([typeof(int), typeof(int), typeof(int), typeof(double)]) ||
            table.Rows.Count != expectedRows)
            throw new InvalidDataException("The mesh perimeter DBF does not match the retained Face/CellA/CellB/Length schema.");
    }

    private static void ValidateCurrentProjectCoreReferences(GeometryMeshModelRequest model)
    {
        if (model.CoreLayerContext is null) return;
        if (string.IsNullOrWhiteSpace(model.ProjectPath))
            throw new SourceFileChangedException();
        CoreLayerImportService.ValidateContext(model.CoreLayerContext, model.ProjectPath);
    }

    private static string GeometryMeshExportFingerprint(GeometryMeshShapefileExportRequest request,
        string destination)
    {
        string model = JsonSerializer.Serialize(request.Model, BackendJson.Options);
        string material = string.Join("\n", SourceFilePaths.Canonical(request.Model.AssetPath),
            model, request.Revision.ToUpperInvariant(), request.MeshDraftSHA256.ToLowerInvariant(),
            request.TopologyDraftSHA256.ToLowerInvariant(), request.FlowAreaID,
            request.ExpectedFlowAreaName, request.ExportKind, Path.GetFullPath(destination));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

}
