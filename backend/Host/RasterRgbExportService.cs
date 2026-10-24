using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Backend;

public static partial class RasterSymbologyService
{
    public static RasterRgbExportInspection InspectRgbExport(RasterRgbExportInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WithContext<RasterRgbExportInspection>(request.Source, request.ProjectXml, context =>
        {
            Extent full = context.Raster.ValidExtent;
            if (!full.IsFiniteAndValid())
                throw new NotSupportedException("The selected raster has no finite export extent.");
            RasterMapViewExtent? current = request.CurrentView;
            double? suggestedCellSize = null;
            if (current is not null)
            {
                ValidateExtent(current);
                Extent visible = full.Intersection(new Extent(current.MinX, current.MaxX,
                    current.MinY, current.MaxY));
                if (!visible.IsFiniteAndValid()) current = null;
                else
                {
                    current = current with
                    {
                        MinX = visible.MinX, MinY = visible.MinY,
                        MaxX = visible.MaxX, MaxY = visible.MaxY
                    };
                    suggestedCellSize = Math.Max(
                        (current.MaxX - current.MinX) / current.PixelWidth,
                        (current.MaxY - current.MinY) / current.PixelHeight);
                    if (!double.IsFinite(suggestedCellSize.Value) || suggestedCellSize <= 0)
                        suggestedCellSize = null;
                }
            }
            RasterSymbology symbology = ReadSymbology(context.SourceNode.Element("RasterSymbology"),
                context.Raster, context.SourceRoot, out _);
            RasterPlotOptionDescriptor[] plotOptions = DescribeRasterPlotOptions(context.Raster, symbology);
            VerifySourceRevision(context);
            VerifyVisualizationRevision(context);
            return new(request.Source.Id, request.Source.NodeName, context.SourceRevision,
                context.VisualizationRevision, full.MinX, full.MinY, full.MaxX, full.MaxY,
                current, suggestedCellSize, plotOptions);
        });
    }

    public static RasterRgbExportPreview PreviewRgbExport(RasterRgbExportPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCellSize(request.CellSize);
        return WithContext<RasterRgbExportPreview>(request.Source, request.ProjectXml, context =>
        {
            Extent requested = SelectExportExtent(request.Region, request.CurrentView, context.Raster.ValidExtent);
            var outputDefinition = new RasterDefinition(requested, request.CellSize);
            if (!outputDefinition.IsValid() || outputDefinition.CellCount <= 0 ||
                outputDefinition.CellCountLong > int.MaxValue)
                throw new ArgumentException("The selected cell size produces unsupported raster dimensions.");
            RasterSymbology symbology = ReadSymbology(context.SourceNode.Element("RasterSymbology"),
                context.Raster, context.SourceRoot, out _);
            RasterMapViewExtent? view = request.Region == "CurrentView" ? request.CurrentView : null;
            RasterMapLayerPayload raster = Render(context, symbology, view,
                request.PlotOptions ?? []);
            VerifySourceRevision(context);
            VerifyVisualizationRevision(context);
            return new(request.Source.Id, context.SourceRevision, context.VisualizationRevision,
                request.CellSize, requested.MinX, requested.MinY, requested.MaxX, requested.MaxY,
                outputDefinition.Cols, outputDefinition.Rows,
                RgbTiffExporter.EstimateFileSize(outputDefinition), raster);
        });
    }

    internal static Task<RasterRgbExportExecuteResult> ExportRgbAsync(
        RasterRgbExportExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls) => ExportRgbCoreAsync(request, report, duplexControls);

    private static async Task<RasterRgbExportExecuteResult> ExportRgbCoreAsync(
        RasterRgbExportExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCellSize(request.CellSize);
        string destination = ExportOutputCommit.ValidateOutputPath(request.OutputPath, ".tif");
        if (request.Source.GdbSource is { } requestedGdb &&
            ExportOutputCommit.IsSameOrWithin(destination, requestedGdb.Source.SourcePath))
            throw new ArgumentException("The TIFF export cannot be written inside its source File Geodatabase.");
        ExportOutputCommit.EnsureNoClobber(destination);
        string stagedPath = ExportOutputCommit.CreateTemporaryFilePath(destination);

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
            RasterRgbExportExecuteResult result = await Task.Run(() =>
                WithContext(request.Source, request.ProjectXml, context =>
                {
                    Extent exportExtent = SelectExportExtent(request.Region, request.CurrentView,
                        context.Raster.ValidExtent);
                    var definition = new RasterDefinition(exportExtent, request.CellSize);
                    if (!definition.IsValid() || definition.CellCount <= 0 ||
                        definition.CellCountLong > int.MaxValue)
                        throw new ArgumentException("The selected cell size produces an unsupported raster dimensions.");
                    ExportOutputCommit.EnsureNoClobber(destination);
                    if (reporter.CancelRequested)
                        return CancelledRgb(request, context);

                    RasterSymbology symbology = ReadSymbology(context.SourceNode.Element("RasterSymbology"),
                        context.Raster, context.SourceRoot, out _);
                    var renderLayer = new RasterRenderLayer(context.Raster, symbology);
                    Dictionary<string, (Geospatial.Rendering.Layers.Attributes.PlotOption Option,
                        System.Reflection.PropertyInfo Property)> options =
                        renderLayer.GetPlotOptions().ToDictionary(pair => pair.Key.Name,
                            pair => (pair.Key, pair.Value), StringComparer.Ordinal);
                    ApplyRasterPlotOptions(renderLayer, options, request.PlotOptions ?? []);
                    Projection? projection = ResolveExportProjection(context.Source.ProjectPath, request.ProjectXml);
                    report?.Invoke(new(TaskName: "Exporting RGBA TIFF"));
                    RgbTiffExporter.ExportRgba(stagedPath, renderLayer, definition, projection, reporter);
                    if (reporter.CancelRequested)
                    {
                        ExportOutputCommit.DeleteFileIfPresent(stagedPath);
                        return CancelledRgb(request, context);
                    }
                    if (!File.Exists(stagedPath))
                        throw new IOException("The retained RGBA TIFF writer did not create its staged output.");

                    VerifySourceRevision(context);
                    VerifyVisualizationRevision(context);
                    if (reporter.CancelRequested)
                    {
                        ExportOutputCommit.DeleteFileIfPresent(stagedPath);
                        return CancelledRgb(request, context);
                    }
                    if (context.GdbSource is { } gdb &&
                        ExportOutputCommit.IsSameOrWithin(destination, gdb.Source.SourcePath))
                        throw new ArgumentException("The TIFF export cannot be written inside its source File Geodatabase.");
                    ExportOutputCommit.PreparedSingleFile prepared =
                        ExportOutputCommit.PrepareSingleFile(stagedPath, destination);
                    ExportArtifactReceipt[] artifacts;
                    using (ExportDestinationLease.Acquire(destination))
                    {
                        ExportOutputCommit.VerifyPreparedSingleFile(prepared);
                        VerifySourceRevision(context);
                        VerifyVisualizationRevision(context);
                        if (reporter.CancelRequested)
                        {
                            ExportOutputCommit.DeleteFileIfPresent(stagedPath);
                            return CancelledRgb(request, context);
                        }
                        artifacts = ExportOutputCommit.CommitSingleFile(prepared);
                    }
                    return new("completed", destination, request.Source.Id,
                        context.SourceRevision, context.VisualizationRevision, request.CellSize,
                        definition.Cols, definition.Rows, definition.MinX, definition.MinY,
                        definition.MaxX, definition.MaxY, artifacts);
                }), CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        finally
        {
            ExportOutputCommit.TryCleanup(() => ExportOutputCommit.DeleteFileIfPresent(stagedPath),
                "temporary TIFF export file");
            await ExportOutputCommit.StopAndObserveAsync(stopControls, controlPump, "TIFF export control pump")
                .ConfigureAwait(false);
        }
    }

    private static RasterRgbExportExecuteResult CancelledRgb(RasterRgbExportExecuteRequest request,
        OperationContext context) => new("cancelled", null, request.Source.Id,
        context.SourceRevision, context.VisualizationRevision, request.CellSize,
        0, 0, 0, 0, 0, 0, []);

    private static Extent SelectExportExtent(string region, RasterMapViewExtent? currentView,
        Extent fullExtent)
    {
        if (!fullExtent.IsFiniteAndValid())
            throw new NotSupportedException("The selected raster has no finite export extent.");
        if (region == "FullLayer") return fullExtent;
        if (region != "CurrentView")
            throw new ArgumentException("Choose Current View or Full Layer Extent.", nameof(region));
        if (currentView is null)
            throw new ArgumentException("A current map view is required for this export region.", nameof(currentView));
        ValidateExtent(currentView);
        Extent visible = fullExtent.Intersection(new Extent(currentView.MinX, currentView.MaxX,
            currentView.MinY, currentView.MaxY));
        if (!visible.IsFiniteAndValid())
            throw new ArgumentException("The current map view does not overlap the selected raster.", nameof(currentView));
        return visible;
    }

    private static Projection? ResolveExportProjection(string projectPath, string? projectXml)
    {
        XDocument projectDocument = ReadProjectDocument(projectPath, projectXml);
        XElement? projectionNode = projectDocument.Root?.Element("Projection");
        if (projectionNode is null) return null;
        Ras.Layers.ProjectionDefinition definition = Ras.Layers.ProjectionDefinition.Deserialize(projectionNode);
        var result = definition.Evaluate(out Projection? projection, out _);
        if (!result.Result)
            throw new ArgumentException("The current project projection could not be evaluated.");
        return projection;
    }

    private static void VerifyVisualizationRevision(OperationContext context)
    {
        if (!RevisionFile(context.VisualizationPath).Equals(
                context.PreviousVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void ValidateCellSize(double cellSize)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive and finite.");
    }
}
