using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO.Network.USGS;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility.Progress;
using Utility.Units;

namespace Ras.Backend;

public sealed record TerrainUSGSQueryRequest(ModelSaveRequest Model, string? InputId = null,
    TerrainInputExtent? HintExtent = null);
public sealed record TerrainUSGSMapPoint(double X, double Y);
public sealed record TerrainUSGSProduct(string Id, string Title, string? PublicationDate,
    long? SizeInBytes, string Resolution, double? CellSize, string? MetadataUrl,
    TerrainInputExtent? BoundingBox, TerrainUSGSMapPoint[]? ProjectedBoundary = null,
    string? DownloadUrl = null);
public sealed record TerrainUSGSQueryData(string State, string? InputId, TerrainInputExtent Extent,
    string TargetProjection, int Total, TerrainUSGSProduct[] Products, string? Diagnostic = null);
public sealed record TerrainUSGSDownloadRequest(ModelSaveRequest Model, string[] ProductIds,
    string? InputId = null, TerrainInputExtent? HintExtent = null);
public sealed record TerrainUSGSDownloadedFile(string ProductId, string Title, string FilePath,
    string SourceRevision, double DefaultScaleFactor, string DefaultRounding,
    string? DownloadUrl = null, string? MetadataUrl = null,
    string? PreviewFilePath = null, string? PreviewSourceRevision = null, string? InputId = null);
public sealed record TerrainUSGSDownloadData(string State, string? CleanupToken,
    TerrainUSGSDownloadedFile[] Files, string AssetPath, string ProjectPath, string SourceRevision,
    string? InputId, TerrainInputExtent Extent, bool StitchTiles, string? Diagnostic = null);
public sealed record TerrainUSGSCleanupRequest(ModelRequest Model, string CleanupToken);
public sealed record TerrainUSGSCleanupData(bool Removed, string? Warning = null);

internal sealed record TerrainUSGSImportManifest(int Version, string Token, string AssetPath,
    string ProjectPath, string SourceRevision, string? InputId, TerrainUSGSDownloadedFile[] Files);

public static partial class ModelEditorAdapter
{
    private const string TerrainUSGSImportDirectoryName = "HECRAS-TerrainUSGSImports";
    internal const string TerrainUSGSManifestName = "manifest.json";
    private const string TerrainUSGSDefaultRounding = "ThirtySeconds";

    public static async Task<TerrainUSGSQueryData> QueryTerrainUSGSAsync(TerrainUSGSQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing terrain query model.");
        using TerrainUSGSScope scope = OpenTerrainUSGSScope(model, request.InputId, request.HintExtent);
        USGSProductQuery products = await USGSRequests.GetProductsFromExtent(
            scope.Extent, scope.Projection, USGSDataType.ElevationModel).ConfigureAwait(false);
        scope.Editor.VerifyDependencies();
        CheckTerrain(model);

        if (products?.OperationResult is { Result: false } failed)
            throw new InvalidOperationException(USGSQueryDiagnostic(products, failed));

        USGSItem[] items = (products?.Items ?? []).Where(IsDownloadableProduct)
            .GroupBy(ProductId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        TerrainUSGSProduct[] result = items.Select(item => {
            TerrainInputExtent? boundingBox = ValidBoundingBox(item.BoundingBox);
            return new TerrainUSGSProduct(ProductId(item), item.Title ?? string.Empty,
                item.PublicationDate, item.SizeInBytesNullable, item.Resolution.ToDisplayString(),
                item.Resolution.Meters > 0 ? item.Resolution.Meters : null,
                item.MetaURL, boundingBox, ProjectedBoundary(boundingBox, scope.Projection), item.DownloadURL);
        }).ToArray();
        return new("completed", scope.InputId, ToContractExtent(scope.Extent),
            scope.Projection.GetPrettyWKT(), products?.Total ?? result.Length, result,
            string.IsNullOrWhiteSpace(products?.ErrorMessage) ? null : products.ErrorMessage);
    }

    internal static async Task<TerrainUSGSDownloadData> DownloadTerrainUSGSAsync(
        TerrainUSGSDownloadRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing terrain download model.");
        string[] productIds = request.ProductIds ?? throw new ArgumentException("Choose at least one USGS terrain product.");
        if (productIds.Length == 0 || productIds.Any(string.IsNullOrWhiteSpace)
            || productIds.Distinct(StringComparer.Ordinal).Count() != productIds.Length)
            throw new ArgumentException("Choose one or more distinct USGS terrain products.");
        RejectUnsupportedDraftData(model);

        using TerrainUSGSScope scope = OpenTerrainUSGSScope(model, request.InputId, request.HintExtent);
        var reporter = new ProgressReporter();
        using var stopControls = new CancellationTokenSource();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: "Downloading and validating terrain products…"));
        Task? controlPump = StartTerrainUSGSControlPump(duplexControls, reporter, stopControls.Token);

        string? operationDirectory = null;
        string cleanupToken = string.Empty;
        bool transferredOwnership = false;
        try
        {
            USGSProductQuery query = await USGSRequests.GetProductsFromExtent(
                scope.Extent, scope.Projection, USGSDataType.ElevationModel,
                reporter.WithProgressModifier(0, 0.05f)).ConfigureAwait(false);
            scope.Editor.VerifyDependencies();
            CheckTerrain(model);
            ThrowIfUSGSQueryFailed(query);
            if (reporter.CancelRequested)
                return CancelledUSGSDownload(model, scope.InputId, scope.Extent);

            Dictionary<string, USGSItem> available = (query?.Items ?? []).Where(IsDownloadableProduct)
                .GroupBy(ProductId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            USGSItem[] selected = productIds.Select(id => available.TryGetValue(id, out USGSItem? item)
                ? item : throw new ArgumentException("A selected USGS terrain product is no longer available. Refresh the query and try again.")).ToArray();

            operationDirectory = CreateTerrainUSGSDirectory(out cleanupToken);
            List<TerrainUSGSDownloadedFile> files = [];
            for (int index = 0; index < selected.Length; index++)
            {
                if (reporter.CancelRequested)
                    return CancelledUSGSDownload(model, scope.InputId, scope.Extent);
                USGSItem item = selected[index];
                string outputPath = Path.Combine(operationDirectory, "converted-" + index.ToString(CultureInfo.InvariantCulture) + ".tif");
                USGSImportConfig config = USGSImportConfig.Preserve(operationDirectory);
                config.RasterImportOptions.DestinationProjection = scope.Projection;
                config.RasterImportOptions.UseRounding(RoundTo.None);
                config.RasterImportOptions.DestinationNoData = -9999f;

                int operationOffset = 5 + 90 * index / selected.Length;
                float operationScale = 0.90f / selected.Length;
                USGSDownloadResult download = await USGSHelpers.TryDownloadAndParseUSGSTileAsync(
                    item.DownloadURL, outputPath, config,
                    reporter.WithProgressModifier(operationOffset, operationScale)).ConfigureAwait(false);
                if (reporter.CancelRequested)
                    return CancelledUSGSDownload(model, scope.InputId, scope.Extent);
                if (download?.Result is not { Result: true })
                {
                    string diagnostic = download?.Result?.GetConcatenatedMessages() ?? string.Empty;
                    throw new IOException(string.IsNullOrWhiteSpace(diagnostic)
                        ? "The USGS terrain product could not be downloaded and parsed."
                        : diagnostic);
                }
                string rawPath = OwnedFilePath(operationDirectory, download.DownloadedFile,
                    "The USGS downloader did not preserve its original source TIFF.");
                string previewPath = OwnedFilePath(operationDirectory, download.ConvertedFile,
                    "The USGS downloader did not produce its source preview conversion.");
                string previewRevision = MapRasterLayerSourceManifest.CaptureRevision(previewPath);
                if (!double.IsFinite(download.ScaleFactorUsed) || download.ScaleFactorUsed <= 0
                    || !float.IsFinite((float)download.ScaleFactorUsed) || (float)download.ScaleFactorUsed <= 0)
                    throw new InvalidDataException("The USGS source-unit conversion is not a finite positive float scale factor.");

                TerrainInputImportSource inspected = TerrainInputImportService.Inspect([rawPath], scope.Projection)
                    .Sources.SingleOrDefault() ?? throw new InvalidDataException("The downloaded USGS source is not a single terrain raster.");
                if (!inspected.IsValid)
                    throw new InvalidDataException(inspected.Error ?? "The downloaded USGS source is not a valid terrain DEM.");
                string sourceRevision = TerrainInputImportService.HashSource(rawPath);
                files.Add(new(ProductId(item), item.Title ?? string.Empty, rawPath, sourceRevision,
                    download.ScaleFactorUsed, TerrainUSGSDefaultRounding, item.DownloadURL, item.MetaURL,
                    previewPath, previewRevision, scope.InputId));
                scope.Editor.VerifyDependencies();
                CheckTerrain(model);
                report?.Invoke(new(Fraction: 0.05 + 0.90 * (index + 1.0) / selected.Length,
                    TaskName: "Preparing USGS terrain files…"));
            }

            scope.Editor.VerifyDependencies();
            CheckTerrain(model);
            if (reporter.CancelRequested)
                return CancelledUSGSDownload(model, scope.InputId, scope.Extent);

            string token = cleanupToken;
            string assetPath = SourceFilePaths.Canonical(model.AssetPath);
            string projectPath = SourceFilePaths.Canonical(model.ProjectPath
                ?? throw new ArgumentException("USGS terrain import requires the owning project."));
            var manifest = new TerrainUSGSImportManifest(1, token, assetPath, projectPath,
                model.Revision, scope.InputId, files.ToArray());
            string manifestPath = Path.Combine(operationDirectory!, TerrainUSGSManifestName);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, BackendJson.Options),
                Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);
            transferredOwnership = true;
            report?.Invoke(new(Fraction: 1, Message: "USGS terrain files are ready for import."));
            return new("completed", token, files.ToArray(), assetPath, projectPath,
                model.Revision, scope.InputId, ToContractExtent(scope.Extent), StitchTiles: true);
        }
        catch
        {
            if (reporter.CancelRequested)
                return CancelledUSGSDownload(model, scope.InputId, scope.Extent);
            throw;
        }
        finally
        {
            if (!transferredOwnership && operationDirectory != null)
                DeleteOwnedTerrainUSGSDirectory(operationDirectory);
            stopControls.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            }
        }
    }

    internal static Task<TerrainUSGSCleanupData> CleanupTerrainUSGSAsync(TerrainUSGSCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelRequest model = request.Model ?? throw new ArgumentException("Missing terrain cleanup owner.");
        if (!string.Equals(model.Kind, "Terrain", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(model.ProjectPath))
            throw new ArgumentException("USGS import cleanup requires its owning Terrain project.");
        string token = TerrainInputImportService.NormalizeToken(request.CleanupToken, "USGS terrain cleanup");
        string directory = TerrainUSGSDirectory(token);
        string manifestPath = Path.Combine(directory, TerrainUSGSManifestName);
        if (!File.Exists(manifestPath)) return Task.FromResult(new TerrainUSGSCleanupData(true));

        TerrainUSGSImportManifest manifest = JsonSerializer.Deserialize<TerrainUSGSImportManifest>(
            File.ReadAllText(manifestPath), BackendJson.Options)
            ?? throw new InvalidDataException("The USGS terrain cleanup manifest is invalid.");
        if (manifest.Version != 1 || !string.Equals(manifest.Token, token, StringComparison.Ordinal)
            || !PathEquals(manifest.AssetPath, model.AssetPath)
            || !PathEquals(manifest.ProjectPath, model.ProjectPath))
            throw new ArgumentException("The USGS terrain cleanup token belongs to another model or project.");

        try
        {
            Directory.Delete(directory, recursive: true);
            return Task.FromResult(new TerrainUSGSCleanupData(true));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new TerrainUSGSCleanupData(false,
                "Could not remove the owned USGS temporary files: " + error.Message));
        }
    }

    private static TerrainUSGSScope OpenTerrainUSGSScope(ModelSaveRequest model, string? requestedInputId,
        TerrainInputExtent? hintExtent)
    {
        CheckTerrain(model);
        RejectUnsupportedDraftData(model);
        var context = new ModelContext(model);
        try
        {
            if (context.Layer is not Terrain terrain)
                throw new ArgumentException("The selected model is not a Terrain.");
            TerrainEditor editor = TerrainInputs(context, model.AssetPath, model.Revision, model.SourceState)
                ?? throw new ArgumentException("The selected model is not a Terrain.");

            editor.CaptureSelectedInput(model.Fields);
            editor.ApplySelections(model.TableSelections);
            ApplyFields(context, null, model.Fields);
            editor.Apply(model.Fields);
            editor.ApplyRows(model.Rows, model.ChangedCells);
            editor.VerifyDependencies();

            TerrainEditorData snapshot = editor.Snapshot();
            string? inputId = string.IsNullOrWhiteSpace(requestedInputId) ? snapshot.SelectedId : requestedInputId;
            TerrainInputData? selectedInput = inputId == null ? null
                : snapshot.Inputs.FirstOrDefault(input => input.Id == inputId);
            if (requestedInputId != null && selectedInput == null)
                throw new ArgumentException("The selected terrain input is no longer available.");

            TerrainInputExtent terrainExtent = ToContractExtent(terrain.ValidExtent);
            Extent extent = ToExtent(ResolveTerrainUSGSQueryExtent(
                hintExtent, selectedInput?.ValidExtent, terrainExtent));

            Projection projection = context.Project.Projection
                ?? throw new InvalidOperationException("No projection is set for this project. Set a projection before querying USGS terrain.");
            return new TerrainUSGSScope(context, editor, extent, projection, inputId);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static void RejectUnsupportedDraftData(ModelSaveRequest model)
    {
        if (model.Samples?.Any(pair => pair.Value.Length > 0) == true)
            throw new NotSupportedException("Terrain series edits are not connected to USGS terrain import.");
        if (model.PlanTimeWindowEdits is { Length: > 0 })
            throw new NotSupportedException("Plan time-window edits are not connected to USGS terrain import.");
        if (model.GeometryTopologyState != null || model.GeometryMeshState != null || model.GeometryCulvertState != null)
            throw new NotSupportedException("Geometry editor state is not connected to USGS terrain import.");
    }

    internal static bool IsFiniteTerrainExtent(TerrainInputExtent extent) =>
        ToExtent(extent).IsValidAndFinite();

    internal static TerrainInputExtent ResolveTerrainUSGSQueryExtent(
        TerrainInputExtent? hintExtent, TerrainInputExtent? selectedInputExtent,
        TerrainInputExtent terrainExtent)
    {
        if (hintExtent is { } hint && IsFiniteTerrainExtent(hint)) return hint;
        if (selectedInputExtent is { } selected && IsFiniteTerrainExtent(selected)) return selected;
        if (IsFiniteTerrainExtent(terrainExtent)) return terrainExtent;
        throw new InvalidOperationException(
            "Choose a finite terrain viewport or a terrain input with a valid extent before querying USGS.");
    }

    private static Extent ToExtent(TerrainInputExtent extent) =>
        new(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY);

    private static TerrainInputExtent ToContractExtent(Extent extent) =>
        new(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY);

    private static TerrainInputExtent? ValidBoundingBox(BoundingBoxJson? boundingBox)
    {
        if (boundingBox?.MinX is not { } minX || boundingBox.MaxX is not { } maxX
            || boundingBox.MinY is not { } minY || boundingBox.MaxY is not { } maxY)
            return null;
        var extent = new TerrainInputExtent(minX, maxX, minY, maxY);
        return IsFiniteTerrainExtent(extent) ? extent : null;
    }

    /// <summary>
    /// Projects the actual USGS geographic bounding box through the same
    /// destination projection used by the terrain importer. Sampling each edge
    /// keeps curved projected boundaries faithful enough for map drawing and
    /// point-in-polygon hit testing instead of treating projected corners as a
    /// straight rectangle.
    /// </summary>
    private static TerrainUSGSMapPoint[]? ProjectedBoundary(TerrainInputExtent? extent, Projection targetProjection)
    {
        if (extent is not { } sourceExtent || !IsFiniteTerrainExtent(sourceExtent)) return null;
        const int edgeSegments = 4;
        (double X, double Y)[] corners =
        [
            (sourceExtent.MinX, sourceExtent.MinY),
            (sourceExtent.MaxX, sourceExtent.MinY),
            (sourceExtent.MaxX, sourceExtent.MaxY),
            (sourceExtent.MinX, sourceExtent.MaxY)
        ];
        var boundary = new List<TerrainUSGSMapPoint>(edgeSegments * corners.Length);
        try
        {
            for (int edge = 0; edge < corners.Length; edge++)
            {
                (double X, double Y) start = corners[edge];
                (double X, double Y) end = corners[(edge + 1) % corners.Length];
                for (int sample = 0; sample < edgeSegments; sample++)
                {
                    double fraction = (double)sample / edgeSegments;
                    var geographic = new Point(start.X + (end.X - start.X) * fraction,
                        start.Y + (end.Y - start.Y) * fraction);
                    Point projected = Projector.TransformPoint(WellKnownProjections.WGS84,
                        targetProjection, geographic);
                    if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y)) return null;
                    boundary.Add(new TerrainUSGSMapPoint(projected.X, projected.Y));
                }
            }
        }
        catch
        {
            return null;
        }
        return boundary.Count >= 4 ? boundary.ToArray() : null;
    }

    internal static bool IsDownloadableProduct(USGSItem item) => item != null
        && !string.IsNullOrWhiteSpace(item.Title)
        && Uri.TryCreate(item.DownloadURL, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https";

    internal static string ProductId(USGSItem item)
    {
        string[] fields = [item.Title ?? string.Empty, item.DownloadURL ?? string.Empty,
            item.MetaURL ?? string.Empty, item.PublicationDate ?? string.Empty,
            item.SizeInBytesNullable?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.BoundingBox?.MinX?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            item.BoundingBox?.MaxX?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            item.BoundingBox?.MinY?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            item.BoundingBox?.MaxY?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty];
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(fields, BackendJson.Options);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static string USGSQueryDiagnostic(USGSProductQuery? query, Utility.Logging.OperationResult result)
    {
        string messages = result.GetConcatenatedMessages();
        if (!string.IsNullOrWhiteSpace(messages)) return messages;
        return string.IsNullOrWhiteSpace(query?.ErrorMessage)
            ? "The USGS terrain query did not complete successfully."
            : query.ErrorMessage;
    }

    private static void ThrowIfUSGSQueryFailed(USGSProductQuery? query)
    {
        if (query?.OperationResult is { Result: false } failed)
            throw new InvalidOperationException(USGSQueryDiagnostic(query, failed));
    }

    internal static string CreateTerrainUSGSDirectory(out string token)
    {
        string root = TerrainUSGSRootDirectory();
        Directory.CreateDirectory(root);
        token = Guid.NewGuid().ToString("N");
        string path = Path.Combine(root, token);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string TerrainUSGSDirectory(string token) =>
        Path.GetFullPath(Path.Combine(TerrainUSGSRootDirectory(), token));

    private static string TerrainUSGSRootDirectory() => Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), TerrainUSGSImportDirectoryName));

    private static string OwnedFilePath(string directory, string? filePath, string error)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new IOException(error);
        string fullPath = Path.GetFullPath(filePath);
        string relative = Path.GetRelativePath(directory, fullPath);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(fullPath))
            throw new IOException(error);
        return fullPath;
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right), StringComparison.Ordinal);

    private static TerrainUSGSDownloadData CancelledUSGSDownload(ModelSaveRequest model, string? inputId,
        Extent extent) =>
        new("cancelled", null, [], SourceFilePaths.Canonical(model.AssetPath),
            SourceFilePaths.Canonical(model.ProjectPath ?? string.Empty), model.Revision, inputId,
            ToContractExtent(extent),
            StitchTiles: true, Diagnostic: "The USGS terrain download was cancelled.");

    private static Task? StartTerrainUSGSControlPump(BackendDuplexControlReader? controls,
        ProgressReporter reporter, CancellationToken cancellationToken)
    {
        if (controls == null) return null;
        return Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (frame.Control.TryGetProperty("type", out JsonElement type)
                    && string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    reporter.RequestCancel();
            }
        }, cancellationToken);
    }

    private static void DeleteOwnedTerrainUSGSDirectory(string directory)
    {
        string root = TerrainUSGSRootDirectory();
        string fullPath = Path.GetFullPath(directory);
        string relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a directory outside the USGS import temporary root.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }

    private sealed record TerrainUSGSScope(ModelContext Context, TerrainEditor Editor,
        Extent Extent, Projection Projection, string? InputId) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }
}
