using System.Text.Json;
using Geospatial;
using Geospatial.IO;
using Geospatial.Rasters;
using Ras.Layers;
using Utility.Files;
using Utility.Progress;

namespace Ras.Backend;

public sealed record TerrainRasterExportRequest(ModelSaveRequest Model, double CellSize);
public sealed record TerrainRasterExportResult(string State, string? OutputPath, string SourcePath,
    string SourceRevision, string? CleanupWarning = null);

public static partial class ModelEditorAdapter
{
    /// <summary>
    /// Exports the selected source Terrain using the same scalar resampler,
    /// valid extent, projection, and overview writer as TerrainBehaviors.ExportFullRaster.
    /// The duplex control stream only requests cancellation from the source
    /// ProgressReporter; it does not claim to interrupt an individual tile.
    /// </summary>
    internal static async Task<TerrainRasterExportResult> ExportTerrainRasterAsync(
        TerrainRasterExportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing terrain export source.");
        CheckTerrain(model);
        if (!double.IsFinite(request.CellSize) || request.CellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Cell size must be a positive finite number.");
        if (model.Samples?.Any(pair => pair.Value.Length > 0) == true)
            throw new NotSupportedException("Terrain series edits are not connected to this source editor.");

        string sourcePath = SourceFilePaths.Canonical(model.AssetPath);
        string sourceRevision = Revision(sourcePath);
        if (!string.Equals(sourceRevision, model.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        using var context = new ModelContext(model);
        if (context.Layer is not Terrain terrain)
            throw new ArgumentException("The selected model is not a terrain.");
        var editor = TerrainInputs(context, sourcePath, sourceRevision, model.SourceState)
            ?? throw new ArgumentException("The selected source is not a Terrain.");
        editor.CaptureSelectedInput(model.Fields);
        editor.ApplySelections(model.TableSelections);
        ApplyFields(context, null, model.Fields);
        editor.Apply(model.Fields);
        editor.ApplyRows(model.Rows, model.ChangedCells);
        editor.VerifyDependencies();

        var extent = terrain.ValidExtent;
        if (!extent.IsValidAndFinite())
            throw new InvalidOperationException("The terrain has no valid finite raster extent to export.");
        var definition = new RasterDefinition(extent, request.CellSize);
        var projection = terrain.TryGetProjection();
        string exportDirectory = context.Project.EnsureDefaultExportDirectory();
        string destination = FileHelpers.UniqueFilename(Path.Combine(exportDirectory,
            terrain.Name + " (Exported).tif"));

        var reporter = new ProgressReporter();
        int cancelReceived = 0;
        using var stopControls = new CancellationTokenSource();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        Task? controlPump = duplexControls is null ? null : Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControls.Token)
                               .ConfigureAwait(false))
            {
                JsonElement control = frame.Control;
                if (!control.TryGetProperty("type", out JsonElement typeValue) ||
                    !string.Equals(typeValue.GetString(), "cancel", StringComparison.Ordinal)) continue;
                if (Interlocked.Exchange(ref cancelReceived, 1) == 0) reporter.RequestCancel();
            }
        }, stopControls.Token);

        try
        {
            await Task.Run(() => TiffExportEngine.ExportWithOverviews(
                destination, terrain, definition, projection, reporter)).ConfigureAwait(false);
            editor.VerifyDependencies();
            CheckTerrain(model);
            if (!string.Equals(Revision(sourcePath), sourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            if (reporter.CancelRequested)
            {
                string? cleanupWarning = File.Exists(destination)
                    ? "The export was cancelled, but a partial TIFF remains at " + destination
                    : null;
                return new("cancelled", File.Exists(destination) ? destination : null,
                    sourcePath, sourceRevision, cleanupWarning);
            }

            if (!File.Exists(destination))
                throw new IOException("The terrain TIFF export completed without creating its output file.");
            return new("completed", destination, sourcePath, sourceRevision);
        }
        catch (Exception error)
        {
            if (File.Exists(destination))
            {
                try { File.Delete(destination); }
                catch (Exception cleanupError)
                {
                    throw new IOException(error.Message + " The incomplete TIFF could not be removed: " +
                        destination + " (" + cleanupError.Message + ")", error);
                }
            }
            throw;
        }
        finally
        {
            stopControls.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            }
        }
    }
}
