using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Geospatial;
using Geospatial.Core.Rasters;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Mapping;
using Ras.Output;
using Ras.Rendering;
using Utility.Logging;
using Utility.Progress;
using static Ras.Backend.MapResultRenderService;

namespace Ras.Backend;

public static class DepthShapefileExportService
{
    private const int ContourTileSize = 256;
    private const double DefaultCellSize = 100.0;
    private const double DefaultSimplifyDistance = 0.05;

    public static DepthShapefileExportInspection Inspect(DepthShapefileExportInspectRequest request)
    {
        ValidateInspectRequest(request);
        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.SelectionOverrides, request.ContextToken);
        var owners = new List<DepthShapefileExportOwner>();
        foreach (MapResultActiveNode activeNode in context.Sources.ActiveNodes)
        {
            if (activeNode.NodeKind is not ("Result" or "MultiResult") ||
                activeNode.Node.NodeType is not ("Result" or "MultiResult")) continue;
            MapResultSelectionState? slotZero = activeNode.Selections.SingleOrDefault(slot => slot.SelectionIndex == 0);
            if (slotZero?.SelectedResult is not { } choice) continue;
            ResolvedMapResultNode? selected = FindSelectedResult(context, activeNode.Node, 0,
                choice.ResultPath, choice.Revision);
            if (selected is null) continue;
            if (selected.Result.BaseOutputBlock is not IHorizontalOutputBlock outputBlock ||
                outputBlock.ProfileCount <= 0) continue;
            ValidateDepthInputs(selected.Result);
            DepthShapefileExportProfile[] profiles = Enumerable.Range(0, outputBlock.ProfileCount)
                .Select(index => new DepthShapefileExportProfile(index, outputBlock.ProfileNames[index],
                    FormatTime(outputBlock.Times[index]))).ToArray();
            owners.Add(new(activeNode.Node, activeNode.NodeKind, activeNode.Name, 0,
                selected.ResultPath, selected.Revision, selected.Result.Name,
                activeNode.ProfileIndex, profiles));
        }
        context.EnsureUnchanged();
        return new(context.Sources.MapID, context.Sources.ContextToken,
            DefaultCellSize, DefaultSimplifyDistance, owners.ToArray());
    }

    public static DepthShapefileExportPreview Preview(DepthShapefileExportPreviewRequest request)
    {
        ValidatePreviewRequest(request);
        using ResolvedMapResultContext context = Resolve(request.Map, request.SelectionOverrides,
            request.ContextToken, request.ResultNode, request.SelectionIndex,
            request.ResultPath, request.ResultRevision, request.ProfileIndex,
            out ResolvedMapResultNode selected, out IHorizontalOutputBlock outputBlock,
            out Terrain terrain, out MeshPropertyTables[] tables);
        TerrainSourceSnapshot terrainSources = MapResultRenderService.CaptureTerrainSources(terrain);
        var depthLayer = new ResultMapRenderLayer(outputBlock, MapTypes.Depth, terrain, tables);
        var definition = PreviewRasterDefinition(request.Viewport, request.CellSize);
        EnsureExportInputsCurrent(context, selected, terrainSources);

        MapResultRenderLayerPayload? resultRaster = MapResultRenderService.RenderStoredResult(
            context.Project, selected.Result, selected.ResultPath, selected.Revision,
            context.Sources.MapID, SwiftNodeIdentity(request.ResultNode),
            "0", MapTypes.Depth, request.ProfileIndex, request.Viewport, terrainSources);

        var mapper = Mapper.CreateMapper(MapTypes.Depth, outputBlock, tables);
        mapper.RenderOptions.AllowNegativeDepths = true;
        var generationResampler = new ResultMapResampler(mapper, depthLayer.TerrainResampler);
        ContourShapeOperations.GetUnclippedShapes(
            generationResampler.AsSingleProfile(request.ProfileIndex).AsRaster(definition),
            0f, out PolygonCollection polygons, out PolylineCollection polylines,
            request.SimplifyDistance);

        EnsureExportInputsCurrent(context, selected, terrainSources);
        DepthShapefileExportProfile profile = ProfileAt(outputBlock, request.ProfileIndex);
        return new(context.Sources.MapID, context.Sources.ContextToken,
            request.ResultNode, request.SelectionIndex, selected.ResultPath,
            selected.Revision, request.ProfileIndex, profile.Name, profile.Time,
            request.Viewport, request.CellSize, request.SimplifyDistance, resultRaster,
            polygons.Select(ToCoordinates).ToArray(), polylines.Select(ToCoordinates).ToArray());
    }

    internal static Task<DepthShapefileExportExecuteResult> ExportAsync(
        DepthShapefileExportExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls) => ExportCoreAsync(request, report, duplexControls);

    private static async Task<DepthShapefileExportExecuteResult> ExportCoreAsync(
        DepthShapefileExportExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ValidateExecuteRequest(request);
        string destination = ExportOutputCommit.ValidateOutputPath(request.OutputPath, ".shp");
        ExportOutputCommit.EnsureNoClobber(destination);
        string tempDirectory = ExportOutputCommit.CreateTemporaryDirectory(destination);
        string stagedShp = Path.Combine(tempDirectory, Path.GetFileName(destination));

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
            return await Task.Run(() =>
            {
                using ResolvedMapResultContext context = Resolve(request.Map,
                    request.SelectionOverrides, request.ContextToken, request.ResultNode,
                    request.SelectionIndex, request.ResultPath, request.ResultRevision,
                    request.ProfileIndex, out ResolvedMapResultNode selected,
                    out IHorizontalOutputBlock outputBlock, out Terrain terrain,
                    out MeshPropertyTables[] tables);
                TerrainSourceSnapshot terrainSources = MapResultRenderService.CaptureTerrainSources(terrain);
                EnsureExportInputsCurrent(context, selected, terrainSources);
                if (reporter.CancelRequested) return Cancelled(request, selected, outputBlock);

                report?.Invoke(new(TaskName: "Generating depth contours"));
                var depthLayer = new ResultMapRenderLayer(outputBlock, MapTypes.Depth, terrain, tables);
                var mapper = Mapper.CreateMapper(MapTypes.Depth, outputBlock, tables);
                mapper.RenderOptions.AllowNegativeDepths = true;
                var generationResampler = new ResultMapResampler(mapper, depthLayer.TerrainResampler);
                IRaster<float> depthRaster = generationResampler.AsSingleProfile(request.ProfileIndex)
                    .AsRaster(request.CellSize);
                ContourShapeOperations.GetContourShapes(depthRaster, ContourTileSize, 0f,
                    out ComplexPolygonCollection polygons, out _, request.SimplifyDistance);
                EnsureExportInputsCurrent(context, selected, terrainSources);
                if (reporter.CancelRequested) return Cancelled(request, selected, outputBlock);

                report?.Invoke(new(TaskName: "Writing depth shapefile"));
                ExportOutputCommit.EnsureNoClobber(destination);
                OperationResult write = ShapefileIO.Write(stagedShp, polygons.ToArray(),
                    projection: context.Project.Projection);
                if (!write.Result)
                    throw new IOException("The retained shapefile writer failed: " +
                        string.Join(" ", write.Messages.Select(message => message.EvaluatedMessage)));
                EnsureExportInputsCurrent(context, selected, terrainSources);
                if (reporter.CancelRequested) return Cancelled(request, selected, outputBlock);

                DepthShapefileExportProfile profile = ProfileAt(outputBlock, request.ProfileIndex);
                ExportOutputCommit.PreparedShapefileFamily prepared =
                    ExportOutputCommit.PrepareShapefileFamily(stagedShp, destination);
                using (ExportDestinationLease.Acquire(destination))
                {
                    ExportOutputCommit.VerifyPreparedShapefileFamily(prepared);
                    EnsureExportInputsCurrent(context, selected, terrainSources);
                    if (reporter.CancelRequested) return Cancelled(request, selected, outputBlock);

                    ExportArtifactReceipt[] artifacts =
                        ExportOutputCommit.CommitPreparedShapefileFamily(prepared);
                    return new DepthShapefileExportExecuteResult("completed", destination,
                        context.Sources.MapID, context.Sources.ContextToken, request.ResultNode,
                        request.SelectionIndex, selected.ResultPath, selected.Revision,
                        request.ProfileIndex, profile.Name, profile.Time,
                        request.CellSize, request.SimplifyDistance, polygons.Count, artifacts);
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ExportOutputCommit.TryCleanup(() => ExportOutputCommit.DeleteDirectoryIfPresent(tempDirectory),
                "temporary Shapefile export directory");
            await ExportOutputCommit.StopAndObserveAsync(stopControls, controlPump, "depth export control pump")
                .ConfigureAwait(false);
        }
    }

    private static DepthShapefileExportExecuteResult Cancelled(
        DepthShapefileExportExecuteRequest request, ResolvedMapResultNode selected,
        IHorizontalOutputBlock outputBlock)
    {
        DepthShapefileExportProfile profile = ProfileAt(outputBlock, request.ProfileIndex);
        return new("cancelled", null, request.Map.MapID, request.ContextToken,
            request.ResultNode, request.SelectionIndex, selected.ResultPath,
            selected.Revision, request.ProfileIndex, profile.Name, profile.Time,
            request.CellSize, request.SimplifyDistance, 0, []);
    }

    private static ResolvedMapResultContext Resolve(MapTreeRequest map,
        MapResultSelectionOverride[] selectionOverrides, string contextToken,
        CatalogTreeNodeLocator resultNode, int selectionIndex, string resultPath,
        string resultRevision, int profileIndex, out ResolvedMapResultNode selected,
        out IHorizontalOutputBlock outputBlock, out Terrain terrain,
        out MeshPropertyTables[] tables)
    {
        if (selectionIndex != 0)
            throw new ArgumentException("Depth export is tied to Result selection slot zero.", nameof(selectionIndex));
        if (resultNode.NodeType is not ("Result" or "MultiResult"))
            throw new ArgumentException("Choose an active Result or MultiResult owner.", nameof(resultNode));
        ResolvedMapResultContext context = MapResultChartService.ResolveContext(map,
            selectionOverrides, contextToken);
        try
        {
            MapResultActiveNode active = context.Sources.ActiveNodes.SingleOrDefault(node =>
                SameNode(node.Node, resultNode) && (node.NodeKind is "Result" or "MultiResult"))
                ?? throw new SourceFileChangedException();
            MapResultSelectionState slot = active.Selections.SingleOrDefault(item => item.SelectionIndex == 0)
                ?? throw new SourceFileChangedException();
            if (slot.SelectedResult is not { } selectedChoice ||
                !SameFile(selectedChoice.ResultPath, resultPath) ||
                !selectedChoice.Revision.Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            selected = FindSelectedResult(context, resultNode, selectionIndex, resultPath, resultRevision)
                ?? throw new SourceFileChangedException();
            outputBlock = selected.Result.BaseOutputBlock as IHorizontalOutputBlock
                ?? throw new InvalidDataException("The selected Result has no horizontal output profiles.");
            if (outputBlock.ProfileCount <= 0 || profileIndex < 0 || profileIndex >= outputBlock.ProfileCount)
                throw new ArgumentOutOfRangeException(nameof(profileIndex),
                    "Choose a stored profile from the selected Result.");
            terrain = selected.Result.AssociatedTerrain
                ?? throw new InvalidDataException("The selected Result has no current-project associated Terrain.");
            tables = selected.Result.Geometry?.FlowAreaLayer?.AllPropertyTables()
                ?? throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
            if (tables.Length == 0)
                throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static ResolvedMapResultNode? FindSelectedResult(ResolvedMapResultContext context,
        CatalogTreeNodeLocator node, int selectionIndex, string resultPath, string resultRevision)
    {
        ResolvedMapResultNode[] matches = context.ResultContexts.Where(candidate =>
            candidate.SelectionIndices.Contains(selectionIndex) &&
            candidate.Selections.Any(selection => SameNode(selection.Node, node) &&
                selection.SelectionIndex == selectionIndex) &&
            SameFile(candidate.ResultPath, resultPath) &&
            candidate.Revision.Equals(resultRevision, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void ValidateDepthInputs(Result result)
    {
        if (result.AssociatedTerrain is null)
            throw new InvalidDataException("The selected Result has no current-project associated Terrain.");
        MeshPropertyTables[] tables = result.Geometry?.FlowAreaLayer?.AllPropertyTables()
            ?? throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
        if (tables.Length == 0)
            throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
    }

    private static void EnsureExportInputsCurrent(ResolvedMapResultContext context,
        ResolvedMapResultNode selected, TerrainSourceSnapshot terrainSources)
    {
        context.EnsureUnchanged();
        MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision);
        MapResultRenderService.VerifyTerrainSources(terrainSources);
    }

    private static RasterDefinition PreviewRasterDefinition(RasterMapViewExtent viewport,
        double exportCellSize)
    {
        ValidateCellSize(exportCellSize);
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MaxX <= viewport.MinX || viewport.MaxY <= viewport.MinY ||
            viewport.PixelWidth < 1 || viewport.PixelHeight < 1)
            throw new ArgumentException("The current map view is not a finite ordered extent.", nameof(viewport));
        double liveCellSize = Math.Max((viewport.MaxX - viewport.MinX) / viewport.PixelWidth,
            (viewport.MaxY - viewport.MinY) / viewport.PixelHeight);
        double cellSize = Math.Max(liveCellSize, exportCellSize);
        double minX = Math.Truncate(viewport.MinX / cellSize) * cellSize - 2 * cellSize;
        double maxY = Math.Truncate(viewport.MaxY / cellSize) * cellSize + 2 * cellSize;
        double maxX = Math.Truncate(viewport.MaxX / cellSize) * cellSize + 2 * cellSize;
        double minY = Math.Truncate(viewport.MinY / cellSize) * cellSize - 2 * cellSize;
        var definition = new RasterDefinition(new Extent(minX, maxX, minY, maxY), cellSize);
        if (!definition.IsValid() || definition.CellCount <= 0 || definition.CellCountLong > 4_194_304)
            throw new ArgumentException("The current view is too large for a contour preview.", nameof(viewport));
        return definition;
    }

    private static GeometryTopologyCoordinate[] ToCoordinates(Polygon polygon) => polygon.Points
        .Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();

    private static GeometryTopologyCoordinate[] ToCoordinates(Polyline polyline) => polyline.Points
        .Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();

    private static DepthShapefileExportProfile ProfileAt(IHorizontalOutputBlock block, int index) =>
        new(index, block.ProfileNames[index], FormatTime(block.Times[index]));

    private static string FormatTime(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static void ValidateCellSize(double cellSize)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive and finite.");
    }

    private static string SwiftNodeIdentity(CatalogTreeNodeLocator node)
    {
        static string PathIdentity(int[]? path) => path is null ? string.Empty : string.Join('.', path);
        string[] parts = [node.RootName, node.NodeType, node.NodeName, PathIdentity(node.NodePath),
            node.AssetID ?? string.Empty, node.MapType ?? string.Empty, node.Filename ?? string.Empty,
            node.FilenameRelative ?? string.Empty, node.ParentGeometryAssetID ?? string.Empty,
            PathIdentity(node.ParentGeometryNodePath), node.ParentGeometryNodeName ?? string.Empty,
            node.ParentPlanAssetID ?? string.Empty, PathIdentity(node.ParentPlanNodePath),
            node.ParentPlanNodeName ?? string.Empty, node.ParentResultNodeType ?? string.Empty,
            node.ParentResultAssetID ?? string.Empty, PathIdentity(node.ParentResultNodePath),
            node.ParentResultNodeName ?? string.Empty, node.ParentResultRevision ?? string.Empty,
            node.AssociationAssetID ?? string.Empty, node.AssociationKind ?? string.Empty];
        return string.Join('|', parts.Select(part =>
            Encoding.UTF8.GetByteCount(part).ToString(CultureInfo.InvariantCulture) + ":" + part));
    }

    private static bool SameNode(CatalogTreeNodeLocator first, CatalogTreeNodeLocator second) =>
        first.RootName == second.RootName && first.NodeType == second.NodeType &&
        first.NodeName == second.NodeName && SamePath(first.NodePath, second.NodePath) &&
        first.AssetID == second.AssetID && first.MapType == second.MapType &&
        first.Filename == second.Filename && first.FilenameRelative == second.FilenameRelative &&
        first.ParentGeometryAssetID == second.ParentGeometryAssetID &&
        SamePath(first.ParentGeometryNodePath, second.ParentGeometryNodePath) &&
        first.ParentGeometryNodeName == second.ParentGeometryNodeName &&
        first.AssociationAssetID == second.AssociationAssetID && first.AssociationKind == second.AssociationKind &&
        first.ParentPlanAssetID == second.ParentPlanAssetID &&
        SamePath(first.ParentPlanNodePath, second.ParentPlanNodePath) &&
        first.ParentPlanNodeName == second.ParentPlanNodeName &&
        first.ParentResultNodeType == second.ParentResultNodeType &&
        first.ParentResultAssetID == second.ParentResultAssetID &&
        SamePath(first.ParentResultNodePath, second.ParentResultNodePath) &&
        first.ParentResultNodeName == second.ParentResultNodeName &&
        first.ParentResultRevision == second.ParentResultRevision &&
        first.ParentBoundaryConditionAssetID == second.ParentBoundaryConditionAssetID &&
        SamePath(first.ParentBoundaryConditionNodePath, second.ParentBoundaryConditionNodePath) &&
        first.ParentBoundaryConditionNodeName == second.ParentBoundaryConditionNodeName;

    private static bool SamePath(int[]? first, int[]? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    private static bool SameFile(string first, string second) =>
        SourceFilePaths.SameSourceFile(first, second);

    private static void ValidateInspectRequest(DepthShapefileExportInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        if (string.IsNullOrWhiteSpace(request.ContextToken) || request.SelectionOverrides is null)
            throw new ArgumentException("Refresh the map Result source selection before exporting depth.");
    }

    private static void ValidatePreviewRequest(DepthShapefileExportPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDepthRequest(request.Map, request.SelectionOverrides, request.ContextToken,
            request.ResultNode, request.SelectionIndex, request.ResultPath,
            request.ResultRevision, request.ProfileIndex, request.CellSize, request.SimplifyDistance);
        if (request.Viewport is null) throw new ArgumentException("A current map view is required for preview.");
    }

    private static void ValidateExecuteRequest(DepthShapefileExportExecuteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDepthRequest(request.Map, request.SelectionOverrides, request.ContextToken,
            request.ResultNode, request.SelectionIndex, request.ResultPath,
            request.ResultRevision, request.ProfileIndex, request.CellSize, request.SimplifyDistance);
        _ = ExportOutputCommit.ValidateOutputPath(request.OutputPath, ".shp");
    }

    private static void ValidateDepthRequest(MapTreeRequest map,
        MapResultSelectionOverride[] selectionOverrides, string contextToken,
        CatalogTreeNodeLocator resultNode, int selectionIndex, string resultPath,
        string resultRevision, int profileIndex, double cellSize, double simplifyDistance)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(resultNode);
        if (selectionOverrides is null || string.IsNullOrWhiteSpace(contextToken) ||
            string.IsNullOrWhiteSpace(resultPath) || string.IsNullOrWhiteSpace(resultRevision))
            throw new ArgumentException("The exact map, Result source and selection context are required.");
        if (selectionIndex != 0) throw new ArgumentException("Depth export is tied to Result selection slot zero.");
        if (resultNode.NodeType is not ("Result" or "MultiResult"))
            throw new ArgumentException("Choose an active Result or MultiResult owner.");
        if (profileIndex < 0) throw new ArgumentOutOfRangeException(nameof(profileIndex));
        ValidateCellSize(cellSize);
        if (!double.IsFinite(simplifyDistance) || simplifyDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(simplifyDistance), "Simplification distance must be finite and nonnegative.");
    }
}
