using System.Text.Json;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.Network.USGS;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility.Progress;
using Utility.Units;
using Utility.Web;

namespace Ras.Backend;

public sealed record TerrainAIEnhanceRequest(ModelSaveRequest Model, TerrainInputExtent MapExtent,
    string MapID, string MapWorkflowIdentity);

/// <summary>
/// A generated, source-projected TIFF remains in the USGS cleanup-owned
/// directory until the normal terrain-import transaction has copied it into
/// its staging journal and completed.
/// </summary>
public sealed record TerrainAIEnhanceData(string State, string? OutputPath, string? CleanupToken,
    string AssetPath, string ProjectPath, string SourceRevision, string? InputId,
    TerrainInputExtent Extent, string MapID, string MapWorkflowIdentity, string? OutputRevision = null,
    string? Diagnostic = null);

public static partial class ModelEditorAdapter
{
    private sealed record TerrainAIEnhanceCandidate(USGSItem Item, Extent GeographicExtent,
        Polygon ProjectBoundary, double ProjectedArea);

    private sealed record TerrainAIEnhanceDownloaded(TerrainAIEnhanceCandidate Candidate, string ConvertedPath);

    /// <summary>
    /// Replays the retained TerrainBehaviors.AIEnhance route with the actual
    /// USGS elevation query/download helpers, its 100-cell coverage selection,
    /// prioritized resampling, and the overview TIFF writer. The final raster
    /// is only returned as a cleanup-owned staged file; the caller must append
    /// it through the normal terrain-input draft transaction.
    /// </summary>
    internal static async Task<TerrainAIEnhanceData> EnhanceTerrainFromUSGSAsync(
        TerrainAIEnhanceRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing terrain enhancement model.");
        if (string.IsNullOrWhiteSpace(model.ProjectPath))
            throw new ArgumentException("Terrain enhancement requires the owning saved project.");
        if (duplexControls == null)
            throw new ArgumentException("Terrain enhancement requires a cancellable operation channel.");
        if (string.IsNullOrWhiteSpace(request.MapID) || !Guid.TryParse(request.MapID, out _)
            || string.IsNullOrWhiteSpace(request.MapWorkflowIdentity))
            throw new ArgumentException("The captured map identity is incomplete.");
        if (!IsFiniteTerrainExtent(request.MapExtent))
            throw new ArgumentException("Choose a finite map extent before enhancing terrain.");

        using TerrainUSGSScope scope = OpenTerrainUSGSScope(model, requestedInputId: null,
            hintExtent: request.MapExtent);
        var reporter = new ProgressReporter();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        using var stopControls = new CancellationTokenSource();
        using var requestCancellation = new CancellationTokenSource();
        Task? controlPump = StartTerrainAIControlPump(duplexControls, reporter,
            requestCancellation, stopControls.Token);
        string? operationDirectory = null;
        string cleanupToken = string.Empty;
        bool transferredOwnership = false;
        var rasterInputs = new List<RasterTerrainInput>();
        try
        {
            VerifyEnhancementSource(model, scope);
            Extent extent = scope.Extent;
            Projection projection = scope.Projection;
            Polygon mapPolygon = Polygon.FromExtent(extent);

            report?.Invoke(new(TaskName: "Querying USGS elevation products"));
            USGSProductQuery query = await USGSRequests.GetProductsFromExtent(
                extent, projection, USGSDataType.ElevationModel,
                reporter.SubTask("Querying USGS elevation products", 0, 1)).ConfigureAwait(false);
            VerifyEnhancementSource(model, scope);
            ThrowIfUSGSQueryFailed(query);
            if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);

            USGSItem[] downloadable = (query?.Items ?? []).Where(IsDownloadableProduct).ToArray();
            report?.Invoke(new(TaskName: "Checking USGS elevation product URLs"));
            bool[] validURLs = await Task.WhenAll(downloadable.Select(item =>
                IsEnhancementUrlValidAsync(item, requestCancellation.Token))).ConfigureAwait(false);
            VerifyEnhancementSource(model, scope);
            if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);

            List<TerrainAIEnhanceCandidate> candidates = [];
            for (int productIndex = 0; productIndex < downloadable.Length; productIndex++)
            {
                if (!validURLs[productIndex]) continue;
                USGSItem item = downloadable[productIndex];
                if (item.Resolution.Category is not (USGSDataResolution.Original
                    or USGSDataResolution.OneMeter or USGSDataResolution.Unknown)) continue;
                TerrainAIEnhanceCandidate? candidate = MakeEnhancementCandidate(item, projection);
                if (candidate != null && mapPolygon.ContainsOrIntersects(candidate.ProjectBoundary)) candidates.Add(candidate);
            }
            candidates = candidates
                .OrderBy(candidate => candidate.GeographicExtent.Area)
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("USGS returned no overlapping Original, 1 Meter, or Unknown elevation products for this map extent.");

            RasterDefinition coverageRaster = RasterDefinition.ComputeBoundingRaster(extent, 100);
            if (coverageRaster.CellCount <= 0)
                throw new InvalidOperationException("The captured map extent cannot be rasterized for USGS coverage selection.");
            report?.Invoke(new(TaskName: "Selecting USGS products by map coverage"));
            bool[] coverage = new bool[coverageRaster.CellCount];
            var selected = new List<TerrainAIEnhanceCandidate>();
            foreach (TerrainAIEnhanceCandidate candidate in candidates)
            {
                if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);
                Point[] edgePoints = candidate.GeographicExtent.GetEdgeDivisorPoints(6);
                ReprojectionTransformer.GetOrMake(WellKnownProjections.WGS84, projection)
                    .Transform(edgePoints.AsSpan(), edgePoints.AsSpan());
                var boundary = new Polygon(edgePoints);
                new PolygonRasterizer(coverageRaster).Rasterize(boundary, index => coverage[index] = true);
                selected.Add(candidate);
                if (coverage.All(value => value)) break;
            }

            operationDirectory = CreateTerrainUSGSDirectory(out string token);
            cleanupToken = token;
            var downloadTasks = new List<Task<TerrainAIEnhanceDownloaded?>>();
            for (int index = 0; index < selected.Count; index++)
            {
                if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);
                TerrainAIEnhanceCandidate candidate = selected[index];
                string convertedPath = Path.Combine(operationDirectory,
                    "converted-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tif");
                ProgressReporter downloadProgress = reporter.SubTask("Downloading selected USGS elevation products", 0, 1);
                downloadTasks.Add(DownloadEnhancementCandidateAsync(candidate, convertedPath,
                    operationDirectory, projection, downloadProgress));
            }

            TerrainAIEnhanceDownloaded?[] downloadedResults = await Task.WhenAll(downloadTasks).ConfigureAwait(false);
            VerifyEnhancementSource(model, scope);
            if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);

            TerrainAIEnhanceDownloaded[] downloaded = downloadedResults.Where(item => item != null)
                .Select(item => item!).OrderByDescending(item => Math.Abs(item.Candidate.ProjectedArea)).ToArray();
            if (downloaded.Length == 0)
                throw new IOException("No eligible USGS elevation product could be downloaded and prepared.");

            var prioritized = new PrioritizedResampleCollection();
            double minimumCellSize = double.MaxValue;
            foreach (TerrainAIEnhanceDownloaded item in downloaded)
            {
                RasterTerrainInput input = RasterTerrainInput.CreateAndScan((Terrain)scope.Context.Layer,
                    item.ConvertedPath);
                rasterInputs.Add(input);
                IRasterPyramid<float>? resampler = input.GetResampler();
                if (resampler == null)
                    throw new InvalidDataException("A converted USGS elevation product could not be opened as a terrain raster.");
                double cellSize = resampler.RasterDefinition.CellSize;
                if (!double.IsFinite(cellSize) || cellSize <= 0)
                    throw new InvalidDataException("A converted USGS elevation product has an invalid cell size.");
                minimumCellSize = Math.Min(minimumCellSize, cellSize);
                prioritized.Add(resampler);
            }

            Extent outputExtent = prioritized.ValidExtent;
            if (!outputExtent.IsValidAndFinite() || !double.IsFinite(minimumCellSize)
                || minimumCellSize <= 0)
                throw new InvalidDataException("The prioritized USGS elevation products do not define a valid composite raster.");

            string outputPath = Path.Combine(operationDirectory, "AI Enhancement.tif");
            ProgressReporter exportProgress = reporter.SubTask("Creating terrain composite", 0, 1);
            await Task.Run(() => TiffExportEngine.ExportWithOverviews(outputPath, prioritized,
                new RasterDefinition(outputExtent, minimumCellSize), projection, exportProgress)).ConfigureAwait(false);
            VerifyEnhancementSource(model, scope);
            if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new IOException("The terrain enhancement completed without creating its composite raster.");

            TerrainInputImportSource inspected = TerrainInputImportService.Inspect([outputPath], projection)
                .Sources.SingleOrDefault() ?? throw new InvalidDataException("The terrain enhancement output is not a single raster.");
            if (!inspected.IsValid)
                throw new InvalidDataException(inspected.Error ?? "The terrain enhancement output is not a valid terrain DEM.");

            var output = new TerrainUSGSDownloadedFile("terrain-ai-enhancement", "AI Enhancement",
                outputPath, inspected.Revision, 1.0, "None");
            var manifest = new TerrainUSGSImportManifest(1, cleanupToken,
                SourceFilePaths.Canonical(model.AssetPath),
                SourceFilePaths.Canonical(model.ProjectPath ?? throw new ArgumentException("The terrain project is missing.")),
                model.Revision, scope.InputId, [output]);
            await File.WriteAllTextAsync(Path.Combine(operationDirectory, TerrainUSGSManifestName),
                System.Text.Json.JsonSerializer.Serialize(manifest, BackendJson.Options),
                System.Text.Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);
            VerifyEnhancementSource(model, scope);
            if (reporter.CancelRequested) return CancelledEnhancement(request, model, scope);

            transferredOwnership = true;
            report?.Invoke(new(Fraction: 1, TaskName: "Terrain composite is ready for the terrain draft."));
            return new("completed", outputPath, cleanupToken,
                SourceFilePaths.Canonical(model.AssetPath),
                SourceFilePaths.Canonical(model.ProjectPath!), model.Revision, scope.InputId,
                ToContractExtent(extent), request.MapID, request.MapWorkflowIdentity, inspected.Revision);
        }
        catch (Exception error) when (reporter.CancelRequested)
        {
            report?.Invoke(new(TaskName: "Terrain enhancement cancelled."));
            return CancelledEnhancement(request, model, scope, error.Message);
        }
        finally
        {
            foreach (RasterTerrainInput input in rasterInputs) input.Dispose();
            if (!transferredOwnership && operationDirectory != null)
                DeleteOwnedTerrainUSGSDirectory(operationDirectory);
            stopControls.Cancel();
            if (controlPump != null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            }
        }
    }

    private static TerrainAIEnhanceCandidate? MakeEnhancementCandidate(USGSItem item, Projection projection)
    {
        if (item.BoundingBox?.MinX is not { } minX || item.BoundingBox.MaxX is not { } maxX
            || item.BoundingBox.MinY is not { } minY || item.BoundingBox.MaxY is not { } maxY)
            return null;
        var geographic = new Extent(minX, maxX, minY, maxY);
        if (!geographic.IsFiniteAndValid()) return null;
        Point[] points = geographic.GetEdgeDivisorPoints(4);
        ReprojectionTransformer.GetOrMake(WellKnownProjections.WGS84, projection)
            .Transform(points.AsSpan(), points.AsSpan());
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
        var projectedBoundary = new Polygon(points);
        double area = projectedBoundary.Area;
        return double.IsFinite(area) ? new TerrainAIEnhanceCandidate(item, geographic,
            projectedBoundary, area) : null;
    }

    private static async Task<TerrainAIEnhanceDownloaded?> DownloadEnhancementCandidateAsync(
        TerrainAIEnhanceCandidate candidate, string convertedPath, string operationDirectory,
        Projection projection, ProgressReporter reporter)
    {
        USGSImportConfig config = USGSImportConfig.Preserve(operationDirectory);
        config.RasterImportOptions.DestinationProjection = projection;
        config.RasterImportOptions.UseRounding(RoundTo.None);
        USGSDownloadResult download = await USGSHelpers.TryDownloadAndParseUSGSTileAsync(
            candidate.Item.DownloadURL, convertedPath, config, reporter).ConfigureAwait(false);
        if (reporter.CancelRequested) return null;
        if (download?.Result is not { Result: true } || !File.Exists(convertedPath)) return null;
        return new(candidate, convertedPath);
    }

    private static async Task<bool> IsEnhancementUrlValidAsync(USGSItem item, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, item.DownloadURL);
            using HttpResponseMessage response = await SharedWebResourceManager.Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Task StartTerrainAIControlPump(BackendDuplexControlReader controls,
        ProgressReporter reporter, CancellationTokenSource requestCancellation,
        CancellationToken stopToken) =>
        Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stopToken)
                               .ConfigureAwait(false))
            {
                if (frame.Control.TryGetProperty("type", out JsonElement type)
                    && string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                {
                    reporter.RequestCancel();
                    requestCancellation.Cancel();
                }
            }
        }, stopToken);

    private static void VerifyEnhancementSource(ModelSaveRequest model, TerrainUSGSScope scope)
    {
        scope.Editor.VerifyDependencies();
        CheckTerrain(model);
    }

    private static TerrainAIEnhanceData CancelledEnhancement(TerrainAIEnhanceRequest request,
        ModelSaveRequest model, TerrainUSGSScope scope, string? diagnostic = null) =>
        new("cancelled", null, null, SourceFilePaths.Canonical(model.AssetPath),
            SourceFilePaths.Canonical(model.ProjectPath ?? string.Empty), model.Revision, scope.InputId,
            ToContractExtent(scope.Extent), request.MapID, request.MapWorkflowIdentity,
            OutputRevision: null, Diagnostic: diagnostic);
}
