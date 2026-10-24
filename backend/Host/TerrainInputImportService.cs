using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility.Logging;
using Utility.Progress;
using Visual.Observables;

namespace Ras.Backend;

public sealed record TerrainInputImportInspectRequest(ModelSaveRequest Model, string[] FilePaths);
public sealed record TerrainInputImportSource(
    string Id,
    string Path,
    string Name,
    string Revision,
    bool IsValid,
    string? Error,
    int? Width,
    int? Height,
    int? BandCount,
    double? CellSize,
    string? Projection,
    double? Minimum,
    double? Maximum,
    double? DefaultScaleFactor,
    int? OverviewCount = null,
    string[]? Overviews = null);
public sealed record TerrainInputImportInspectData(
    string TargetProjection,
    TerrainInputImportSource[] Sources,
    string[] RoundingOptions,
    string[] UnitConversionOptions,
    bool StitchTilesDefault);
public sealed record TerrainInputImportSelection(
    string Path,
    string SourceRevision,
    string Rounding,
    string UnitConversion,
    double? CustomScaleFactor = null,
    string? SourceId = null,
    double? ActualScaleFactor = null);
public sealed record TerrainInputAppendRequest(
    ModelSaveRequest Model,
    TerrainInputImportSelection[] Files,
    bool StitchTiles,
    string DraftToken,
    string BatchToken);
public sealed record TerrainInputOperationResult(
    string OperationID,
    int Order,
    string[] SourceIDs,
    string[] SourcePaths,
    string[] SourceRevisions,
    string Status,
    string? OutputInputID = null,
    string? OutputName = null,
    string? Diagnostic = null,
    double? Progress = null);
public sealed record TerrainInputAppendResult(
    ModelSnapshot Snapshot,
    string DraftToken,
    string BatchToken,
    string Mode,
    string BatchStatus,
    TerrainInputOperationResult[] Operations);
public sealed record TerrainInputImportStatusRequest(TerrainInputAppendRequest Request);
public sealed record TerrainInputImportStatusData(
    string State,
    TerrainInputAppendResult? Result,
    TerrainInputOperationResult[] Operations,
    string? ResultUnavailableCode = null,
    string? Diagnostic = null);
public sealed record TerrainInputImportCancelRequest(
    ModelRequest Model,
    string DraftToken,
    string BatchToken,
    string? OperationID = null);
public sealed record TerrainInputImportCancelResult(bool Accepted, string State, string? OperationID = null);
public sealed record TerrainInputImportDiscardRequest(ModelRequest Model, string DraftToken);
public sealed record TerrainInputImportDiscardBatchRequest(ModelRequest Model, string DraftToken, string BatchToken);

internal sealed record TerrainPendingImport(
    string Id,
    string BatchToken,
    string FileName,
    string Name,
    string Revision);

internal sealed record TerrainStagedImport(
    string OperationID,
    string InputID,
    int Order,
    string[] SourceIDs,
    string[] SourcePaths,
    string[] SourceRevisions,
    string FileName,
    string RequestedName,
    string Revision,
    string? OutputName);

internal sealed record TerrainImportOperationJournal(
    string OperationID,
    int Order,
    string[] SourceIDs,
    string[] SourcePaths,
    string[] SourceRevisions,
    string Status,
    string? OutputInputID = null,
    string? OutputName = null,
    string? FileName = null,
    string? RequestedName = null,
    string? OutputRevision = null,
    string? Diagnostic = null,
    double? Progress = null)
{
    public TerrainInputOperationResult ToResult() => new(OperationID, Order, SourceIDs, SourcePaths,
        SourceRevisions, Status, OutputInputID, OutputName, Diagnostic, Progress);
}

internal sealed record TerrainImportBatchJournal(
    int Version,
    string Asset,
    string DraftToken,
    string BatchToken,
    string RequestDigest,
    string Mode,
    string State,
    string OwnerNonce,
    TerrainImportOperationJournal[] Operations,
    TerrainInputAppendResult? Result = null,
    string? Diagnostic = null);

internal static class TerrainInputImportService
{
    internal static readonly string[] RoundingOptions = Enum.GetNames<RoundTo>();
    internal static readonly string[] UnitConversionOptions = ["Default", "None", "FeetToMeters", "MetersToFeet", "Custom"];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> JournalGates =
        new(StringComparer.Ordinal);

    internal static TerrainInputImportInspectData Inspect(string[] paths, Projection? targetProjection)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Length == 0) throw new ArgumentException("Choose at least one terrain raster or Terrain file.");

        List<(string Path, string? Error)> expanded = [];
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                expanded.Add((path ?? string.Empty, "The selected file path is empty."));
                continue;
            }

            string fullPath = Path.GetFullPath(path);
            if (!Path.GetExtension(fullPath).Equals(".h5", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add((fullPath, null));
                continue;
            }

            try
            {
                var valid = Terrain.IsValidTerrainFile(fullPath);
                if (!valid)
                {
                    expanded.Add((fullPath, valid.GetConcatenatedMessages()));
                    continue;
                }

                using var sourceTerrain = new Terrain(fullPath);
                RasterTerrainInput[] rasters = sourceTerrain.AllRasterInputs().ToArray();
                if (rasters.Length == 0)
                {
                    expanded.Add((fullPath, "The selected Terrain contains no raster inputs."));
                    continue;
                }
                expanded.AddRange(rasters.Select(raster => (raster.Filename, (string?)null)));
            }
            catch (Exception error)
            {
                expanded.Add((fullPath, error.Message));
            }
        }

        // A Terrain container and a directly selected TIFF can refer to the
        // same input. Keep its first position and one path-owned settings row.
        expanded = expanded.DistinctBy(item => string.IsNullOrWhiteSpace(item.Path) ? item.Path : Path.GetFullPath(item.Path),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
        var sources = new TerrainInputImportSource[expanded.Count];
        for (int index = 0; index < expanded.Count; index++)
        {
            (string path, string? expansionError) = expanded[index];
            sources[index] = InspectRaster(path, targetProjection, expansionError);
        }

        return new TerrainInputImportInspectData(
            targetProjection?.GetPrettyWKT() ?? string.Empty,
            sources,
            RoundingOptions,
            UnitConversionOptions,
            StitchTilesDefault: true);
    }

    private static TerrainInputImportSource InspectRaster(string path, Projection? targetProjection, string? priorError)
    {
        try { path = Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { priorError ??= error.Message; }
        string name = Path.GetFileNameWithoutExtension(path);
        string revision = "";
        int? width = null, height = null, bandCount = null;
        int? overviewCount = null;
        string[]? overviews = null;
        double? cellSize = null, minimum = null, maximum = null, scale = null;
        string? projectionText = null, errorText = priorError;

        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Terrain raster file not found.", path);
            revision = HashSource(path);
            using var raster = new GDALRaster(path);
            width = raster.PixelsWide;
            height = raster.PixelsTall;
            bandCount = raster.GetBandCount();
            if (bandCount > 0)
            {
                var levels = raster.GetPyramidLevels(1);
                // RasterInfoHelpers reports GetPyramidLevels(1).Length and iterates
                // every entry, including the native-resolution level at index 0.
                overviews = levels.Select(level => $"{level.Width} × {level.Height}").ToArray();
                overviewCount = overviews.Length;
            }

            if (errorText == null && (width <= 0 || height <= 0)) errorText = "Raster dimensions must be positive.";
            else if (errorText == null && bandCount == 0) errorText = "Raster has no valid bands.";
            else if (errorText == null && bandCount != 1) errorText = "Not a valid terrain DEM. Raster has multiple bands.";
            else if (errorText == null && raster.GetDataType() != typeof(float))
                errorText = "Terrain DEM rasters must contain one float32 band.";

            Projection? sourceProjection = raster.GetProjection();
            projectionText = sourceProjection?.GetPrettyWKT();
            if (errorText == null)
            {
                double observedCellSize = raster.GetCellSizeX();
                if (!double.IsFinite(observedCellSize) || observedCellSize <= 0)
                    errorText = "Raster cell size must be a finite positive value.";
                else cellSize = observedCellSize;
                try
                {
                    (minimum, maximum) = ReadCachedStatistics(raster, 1);
                }
                catch
                {
                    // Statistics are optional inspection metadata. A valid
                    // NoData-only DEM can have no finite range at all.
                    minimum = null;
                    maximum = null;
                }
                double defaultScale = sourceProjection == null || targetProjection == null
                    ? 1.0
                    : Projection.GetLengthScaleFactor(sourceProjection, targetProjection, 1.0);
                if (!double.IsFinite(defaultScale))
                    errorText ??= "The default terrain unit conversion is not finite.";
                else scale = defaultScale;
            }
        }
        catch (Exception error)
        {
            errorText ??= error.Message;
        }

        string id = SourceID(path, revision);
        return new TerrainInputImportSource(id, path, name, revision, errorText == null, errorText,
            width, height, bandCount, cellSize, projectionText, minimum, maximum, scale, overviewCount, overviews);
    }

    private static (double? Minimum, double? Maximum) ReadCachedStatistics(GDALRaster raster, int bandIndex)
    {
        Dictionary<string, string> metadata = raster.GetMetadataDictionary(bandIndex);
        return (Read("STATISTICS_MINIMUM"), Read("STATISTICS_MAXIMUM"));

        double? Read(string key) => metadata.TryGetValue(key, out string? raw) &&
            double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) &&
            double.IsFinite(value) ? value : null;
    }

    internal static async Task<TerrainImportBatchJournal> ExecuteBatchAsync(TerrainInputAppendRequest request,
        Projection destinationProjection)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Files == null || request.Files.Length == 0)
            throw new ArgumentException("Choose at least one terrain raster.");
        string assetPath = Path.GetFullPath(request.Model.AssetPath);
        string draftToken = NormalizeToken(request.DraftToken, "terrain import draft");
        string batchToken = NormalizeToken(request.BatchToken, "terrain import batch");
        string batchDirectory = BatchDirectory(assetPath, draftToken, batchToken);
        Directory.CreateDirectory(batchDirectory);
        string digest = RequestDigest(request, assetPath, draftToken, batchToken);
        await using FileStream batchLock = await AcquireBatchLockAsync(batchDirectory).ConfigureAwait(false);

        string journalPath = JournalPath(batchDirectory);
        TerrainImportBatchJournal? journal = ReadJournal(journalPath);
        if (journal != null)
        {
            ValidateJournalIdentity(journal, assetPath, draftToken, batchToken, digest);
            if (journal.Result != null || journal.State != "Running") return journal;
            // Acquiring the exclusive lock proves the previous owner exited.
            // Retain every recorded success and clean only this batch's work.
            journal = RecoverInterruptedBatch(journal, batchDirectory);
            WriteJournal(journalPath, journal);
        }
        else
        {
            TerrainImportOperationJournal[] planned = request.Files.Select((selection, index) =>
            {
                string sourceID = SourceID(selection);
                return new TerrainImportOperationJournal(OperationID("source", index, [sourceID]), index,
                    [sourceID], [SafePath(selection?.Path)], [selection?.SourceRevision ?? string.Empty], "Queued");
            }).ToArray();
            journal = new TerrainImportBatchJournal(1, assetPath, draftToken, batchToken, digest,
                request.StitchTiles ? "Stitched" : "Individual", "Running", batchToken, planned);
            WriteJournal(journalPath, journal);
        }

        if (HasValidCancelMarker(batchDirectory, BatchCancelFile, batchToken))
        {
            journal = FinalizeJournal(journal with
            {
                Operations = journal.Operations.Select(operation => IsPending(operation.Status)
                    ? operation with { Status = "Cancelled", Diagnostic = "The terrain import was cancelled before this operation started." }
                    : operation).ToArray()
            });
            WriteJournal(journalPath, journal);
            return journal;
        }

        CheckedSelection?[] checkedByIndex = new CheckedSelection?[request.Files.Length];
        for (int index = 0; index < request.Files.Length; index++)
        {
            TerrainInputImportSelection selection = request.Files[index];
            try { checkedByIndex[index] = ValidateSelection(selection, destinationProjection, index); }
            catch (Exception error) when (IsItemFailure(error))
            {
                journal = UpdateOperation(journal, OperationID("source", index, [SourceID(selection)]), operation =>
                    operation with { Status = "Failed", Diagnostic = error.Message });
                WriteJournal(journalPath, journal);
            }
        }

        if (request.StitchTiles)
            journal = await ExecuteStitchedAsync(request, destinationProjection, checkedByIndex, journal,
                batchDirectory, journalPath).ConfigureAwait(false);
        else
            journal = await ExecuteIndividualAsync(request, destinationProjection, checkedByIndex, journal,
                batchDirectory, journalPath).ConfigureAwait(false);

        journal = FinalizeJournal(journal);
        WriteJournal(journalPath, journal);
        return journal;
    }

    internal static TerrainStagedImport[] StagedImports(TerrainImportBatchJournal journal)
    {
        return journal.Operations.Where(operation => operation.Status == "Staged"
                && operation.FileName != null && operation.RequestedName != null
                && operation.OutputRevision != null && operation.OutputInputID != null)
            .OrderBy(operation => operation.Order)
            .Select(operation => new TerrainStagedImport(operation.OperationID, operation.OutputInputID!,
                operation.Order, operation.SourceIDs, operation.SourcePaths, operation.SourceRevisions,
                operation.FileName!, operation.RequestedName!, operation.OutputRevision!, operation.OutputName))
            .ToArray();
    }

    internal static TerrainImportBatchJournal UpdateAttachedOperations(TerrainImportBatchJournal journal,
        IReadOnlyDictionary<string, string> outputNames)
    {
        string directory = BatchDirectory(journal.Asset, journal.DraftToken, journal.BatchToken);
        using FileStream batchLock = AcquireBatchLockAsync(directory).GetAwaiter().GetResult();
        string path = JournalPath(directory);
        TerrainImportBatchJournal current = ReadJournal(path) ?? journal;
        ValidateJournalIdentity(current, journal.Asset, journal.DraftToken, journal.BatchToken, journal.RequestDigest);
        if (current.Result != null) return current;
        TerrainImportOperationJournal[] updated = current.Operations.Select(operation =>
            operation.Status == "Staged" && outputNames.TryGetValue(operation.OperationID, out string? name)
                ? operation with { OutputName = name }
                : operation).ToArray();
        current = FinalizeJournal(current with { Operations = updated });
        WriteJournal(path, current);
        return current;
    }

    internal static TerrainImportBatchJournal FailStagedImport(TerrainImportBatchJournal journal,
        string operationID, string diagnostic)
    {
        string directory = BatchDirectory(journal.Asset, journal.DraftToken, journal.BatchToken);
        using FileStream batchLock = AcquireBatchLockAsync(directory).GetAwaiter().GetResult();
        string path = JournalPath(directory);
        TerrainImportBatchJournal current = ReadJournal(path) ?? journal;
        ValidateJournalIdentity(current, journal.Asset, journal.DraftToken, journal.BatchToken, journal.RequestDigest);
        if (current.Result != null) return current;
        TerrainImportOperationJournal? operation = current.Operations.SingleOrDefault(item => item.OperationID == operationID);
        if (operation?.Status != "Staged") return current;
        string? fileName = operation.FileName;
        current = FinalizeJournal(UpdateOperation(current, operationID, item => item with
        {
            Status = "Failed", OutputInputID = null, OutputName = null, FileName = null,
            OutputRevision = null, Diagnostic = diagnostic, Progress = 1
        }));
        WriteJournal(path, current);
        if (fileName != null && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            try { File.Delete(Path.Combine(directory, fileName)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                current = UpdateOperation(current, operationID, item => item with
                { Diagnostic = diagnostic + " Temporary output cleanup failed: " + error.Message });
                WriteJournal(path, current);
            }
        }
        return current;
    }

    internal static TerrainInputAppendResult CommitResult(TerrainImportBatchJournal journal,
        TerrainInputAppendResult result)
    {
        string directory = BatchDirectory(journal.Asset, journal.DraftToken, journal.BatchToken);
        using FileStream batchLock = AcquireBatchLockAsync(directory).GetAwaiter().GetResult();
        string path = JournalPath(directory);
        TerrainImportBatchJournal current = ReadJournal(path) ?? journal;
        ValidateJournalIdentity(current, journal.Asset, journal.DraftToken, journal.BatchToken, journal.RequestDigest);
        if (current.Result != null) return current.Result;
        current = current with { State = result.BatchStatus, Operations = journal.Operations, Result = result };
        WriteJournal(path, current);
        return result;
    }

    internal static TerrainInputImportStatusData ReadStatus(TerrainInputAppendRequest request)
    {
        string asset = Path.GetFullPath(request.Model.AssetPath);
        string draft = NormalizeToken(request.DraftToken, "terrain import draft");
        string batch = NormalizeToken(request.BatchToken, "terrain import batch");
        string directory = BatchDirectory(asset, draft, batch);
        TerrainImportBatchJournal? journal = ReadJournal(JournalPath(directory));
        if (journal == null)
        {
            string state = Directory.Exists(directory) && HasValidCancelMarker(directory, BatchCancelFile, batch)
                ? "Running" : "NotFound";
            return new(state, null, []);
        }
        ValidateJournalIdentity(journal, asset, draft, batch, RequestDigest(request, asset, draft, batch));
        if (journal.State == "Running" && TryRecoverInterruptedBatch(directory, journal, out TerrainImportBatchJournal? recovered))
            journal = recovered!;
        return new(journal.Result?.BatchStatus ?? journal.State, journal.Result,
            journal.Operations.Select(operation => operation.ToResult()).OrderBy(operation => operation.Order).ToArray());
    }

    internal static async Task<TerrainInputImportCancelResult> RequestCancelAsync(TerrainInputImportCancelRequest request)
    {
        string draft = NormalizeToken(request.DraftToken, "terrain import draft");
        string batch = NormalizeToken(request.BatchToken, "terrain import batch");
        string directory = BatchDirectory(Path.GetFullPath(request.Model.AssetPath), draft, batch);
        Directory.CreateDirectory(directory);
        string fileName = string.IsNullOrWhiteSpace(request.OperationID)
            ? BatchCancelFile : OperationCancelFile(request.OperationID);
        TerrainImportBatchJournal? journal = ReadJournal(JournalPath(directory));
        if (request.OperationID != null)
        {
            if (journal == null) return new(false, "NotFound", request.OperationID);
            if (journal.Result != null || journal.State != "Running")
                return new(false, journal.Result?.BatchStatus ?? journal.State, request.OperationID);
            TerrainImportOperationJournal? operation = journal.Operations.SingleOrDefault(item => item.OperationID == request.OperationID);
            if (operation == null || !IsPending(operation.Status)) return new(false, journal.State, request.OperationID);
        }
        else if (journal?.Result != null || journal is { State: not "Running" })
            return new(false, journal?.Result?.BatchStatus ?? journal?.State ?? "NotFound");

        WriteCancelMarker(directory, fileName, batch);
        await Task.CompletedTask.ConfigureAwait(false);
        return new(true, "Running", request.OperationID);
    }

    private sealed record CheckedSelection(int Index, string SourceID, string Path, string SourceRevision,
        RoundTo Rounding, float Scale);

    private static async Task<TerrainImportBatchJournal> ExecuteIndividualAsync(TerrainInputAppendRequest request,
        Projection destinationProjection, CheckedSelection?[] checkedByIndex, TerrainImportBatchJournal journal,
        string batchDirectory, string journalPath)
    {
        List<Task<TerrainInputOperationResult>> tasks = [];
        for (int index = 0; index < checkedByIndex.Length; index++)
        {
            if (checkedByIndex[index] is not { } selection) continue;
            TerrainImportOperationJournal operation = journal.Operations.Single(item => item.Order == index);
            journal = UpdateOperation(journal, operation.OperationID, item => item with { Status = "Running", Diagnostic = null });
        }
        WriteJournal(journalPath, journal);
        for (int index = 0; index < checkedByIndex.Length; index++)
        {
            if (checkedByIndex[index] is not { } selection) continue;
            TerrainImportOperationJournal operation = journal.Operations.Single(item => item.Order == index);
            tasks.Add(RunOperationAsync(selection.Path, selection.Rounding, selection.Scale,
                [selection.SourceID], [selection.Path], [selection.SourceRevision],
                operation.OperationID, index, Path.GetFileNameWithoutExtension(selection.Path),
                request.Model.AssetPath, request.Model.Revision, request.DraftToken, request.BatchToken,
                destinationProjection, journalPath, batchDirectory));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return ReadJournal(journalPath) ?? journal;
    }

    private static async Task<TerrainImportBatchJournal> ExecuteStitchedAsync(TerrainInputAppendRequest request,
        Projection destinationProjection, CheckedSelection?[] checkedByIndex, TerrainImportBatchJournal journal,
        string batchDirectory, string journalPath)
    {
        CheckedSelection[] valid = checkedByIndex.Where(item => item != null).Select(item => item!).ToArray();
        if (valid.Length == 0) return journal;
        foreach (CheckedSelection item in valid)
        {
            string operationID = OperationID("source", item.Index, [item.SourceID]);
            journal = UpdateOperation(journal, operationID, operation => operation with { Status = "Running", Diagnostic = null });
        }
        WriteJournal(journalPath, journal);

        List<(string Filename, List<int> Indices)> runs;
        string mergeDirectory = Path.Combine(batchDirectory, "work-merge");
        TryDeleteDirectory(mergeDirectory);
        Directory.CreateDirectory(mergeDirectory);
        var mergeReporter = new ProgressReporter();
        var mergeJob = new BatchJob(mergeReporter, "Terrain tile merge", uiThreadSyncContext: Utility.SynchronizationContext.None);
        using var monitorStop = new CancellationTokenSource();
        Task monitor = MonitorCancellationAsync(batchDirectory, request.BatchToken, null, mergeJob, monitorStop.Token);
        Exception? mergeError = null;
        try
        {
            if (HasCancellation(batchDirectory, request.BatchToken, null)) mergeJob.RequestCancel();
            if (valid.Length > 1)
            {
                string[] sourcePaths = valid.Select(item => item.Path).ToArray();
                List<(string Filename, List<int> Indices)> localRuns = await Task.Run(() =>
                    TiffExportEngine.SmartMerge(sourcePaths, mergeDirectory, mergeReporter)).ConfigureAwait(false);
                runs = localRuns.Select(run => (run.Filename,
                    run.Indices.Select(index => index >= 0 && index < valid.Length ? valid[index].Index : -1).ToList())).ToList();
            }
            else runs = [(valid[0].Path, [valid[0].Index])];
        }
        catch (Exception error) { runs = []; mergeError = error; }
        finally
        {
            monitorStop.Cancel();
            try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { mergeJob.MarkComplete(mergeError == null && !mergeJob.Reporter.CancelRequested); } catch { }
        }

        if (mergeError != null || mergeJob.Reporter.CancelRequested)
        {
            bool cancelled = mergeJob.Reporter.CancelRequested || HasCancellation(batchDirectory, request.BatchToken, null);
            string diagnostic = cancelled ? "Terrain tile merging was cancelled." : mergeError!.Message;
            foreach (CheckedSelection item in valid)
            {
                string operationID = OperationID("source", item.Index, [item.SourceID]);
                journal = UpdateOperation(journal, operationID, operation => operation with
                {
                    Status = cancelled ? "Cancelled" : "Failed", Diagnostic = diagnostic
                });
            }
            TryDeleteDirectory(mergeDirectory);
            WriteJournal(journalPath, journal);
            return journal;
        }
        if (runs.Count == 0)
        {
            foreach (CheckedSelection item in valid)
            {
                string operationID = OperationID("source", item.Index, [item.SourceID]);
                journal = UpdateOperation(journal, operationID, operation => operation with
                {
                    Status = "Failed", Diagnostic = "The selected terrain rasters did not produce an importable raster."
                });
            }
            TryDeleteDirectory(mergeDirectory);
            WriteJournal(journalPath, journal);
            return journal;
        }

        List<TerrainImportOperationJournal> runOperations = [];
        HashSet<int> assignedSources = [];
        foreach (var run in runs)
        {
            List<int> indices = run.Indices;
            string filename = run.Filename;
            int[] identifiableIndices = indices.Where(index => index >= 0 && index < checkedByIndex.Length
                && checkedByIndex[index] != null).Distinct().ToArray();
            bool invalidMembership = indices.Count == 0 || indices.Distinct().Count() != indices.Count
                || indices.Any(index => index < 0 || index >= checkedByIndex.Length || checkedByIndex[index] == null)
                || identifiableIndices.Any(index => assignedSources.Contains(index));
            if (invalidMembership)
            {
                // A source already accounted for by an earlier valid output
                // must not gain a second, contradictory Failed row merely
                // because a later malformed run repeats it. Attribute the
                // merge failure only to identifiable sources that were not
                // already accepted by a valid run.
                foreach (int index in identifiableIndices.Where(index => !assignedSources.Contains(index)))
                {
                    CheckedSelection member = checkedByIndex[index]!;
                    string sourceOperationID = OperationID("source", index, [member.SourceID]);
                    journal = UpdateOperation(journal, sourceOperationID, operation => operation with
                    {
                        Status = "Failed", Diagnostic = "The raster merge returned invalid source membership."
                    });
                }
                continue;
            }
            foreach (int index in identifiableIndices) assignedSources.Add(index);
            CheckedSelection first = checkedByIndex[indices[0]]!;
            CheckedSelection[] members = indices.Select(index => checkedByIndex[index]!).ToArray();
            string[] sourceIDs = members.Select(item => item.SourceID).ToArray();
            string operationID = OperationID("run", indices.Min(), sourceIDs);
            string requestedName;
            try
            {
                if (members.Length > 1)
                {
                    using var mergedRaster = new GDALRaster(filename);
                    requestedName = "Merged-" + mergedRaster.GetCellSizeLabel();
                }
                else requestedName = Path.GetFileNameWithoutExtension(first.Path);
            }
            catch (Exception error)
            {
                runOperations.Add(new TerrainImportOperationJournal(operationID, indices.Min(), sourceIDs,
                    members.Select(item => item.Path).ToArray(), members.Select(item => item.SourceRevision).ToArray(),
                    "Failed", Diagnostic: error.Message));
                continue;
            }
            runOperations.Add(new TerrainImportOperationJournal(operationID, indices.Min(), sourceIDs,
                members.Select(item => item.Path).ToArray(), members.Select(item => item.SourceRevision).ToArray(),
                "Queued", RequestedName: requestedName));
        }
        foreach (CheckedSelection item in valid.Where(item => !assignedSources.Contains(item.Index)))
        {
            string sourceOperationID = OperationID("source", item.Index, [item.SourceID]);
            journal = UpdateOperation(journal, sourceOperationID, operation => operation with
            {
                Status = operation.Status == "Running" ? "Failed" : operation.Status,
                Diagnostic = operation.Diagnostic ?? "The raster merge did not return an output for this selected source."
            });
        }
        TerrainImportOperationJournal[] retainedSourceResults = journal.Operations
            .Where(operation => operation.Status is "Failed" or "Cancelled").ToArray();
        journal = journal with { Operations = retainedSourceResults.Concat(runOperations).OrderBy(operation => operation.Order).ToArray() };
        WriteJournal(journalPath, journal);

        foreach (var run in runs)
        {
            string filename = run.Filename;
            List<int> indices = run.Indices;
            if (HasCancellation(batchDirectory, request.BatchToken, null))
            {
                journal = journal with { Operations = journal.Operations.Select(operation =>
                    operation.Status == "Queued" ? operation with { Status = "Cancelled", Diagnostic = "The stitched terrain import was cancelled before this run started." } : operation).ToArray() };
                break;
            }
            if (indices.Count == 0 || indices.Any(index => index < 0 || index >= checkedByIndex.Length || checkedByIndex[index] == null)) continue;
            CheckedSelection first = checkedByIndex[indices[0]]!;
            string[] sourceIDs = indices.Select(index => checkedByIndex[index]!.SourceID).ToArray();
            string operationID = OperationID("run", indices.Min(), sourceIDs);
            TerrainImportOperationJournal op = journal.Operations.Single(item => item.OperationID == operationID);
            if (op.Status != "Queued") continue;
            journal = UpdateOperation(journal, operationID, item => item with { Status = "Running" });
            WriteJournal(journalPath, journal);
            await RunOperationAsync(filename, first.Rounding, first.Scale,
                sourceIDs, indices.Select(index => checkedByIndex[index]!.Path).ToArray(),
                indices.Select(index => checkedByIndex[index]!.SourceRevision).ToArray(), operationID,
                indices.Min(), op.RequestedName ?? Path.GetFileNameWithoutExtension(filename), request.Model.AssetPath,
                request.Model.Revision, request.DraftToken, request.BatchToken, destinationProjection,
                journalPath, batchDirectory).ConfigureAwait(false);
            journal = ReadJournal(journalPath) ?? journal;
        }
        TryDeleteDirectory(mergeDirectory);
        return journal;
    }

    private static async Task<TerrainInputOperationResult> RunOperationAsync(string inputPath,
        RoundTo rounding, float scale, string[] sourceIDs, string[] sourcePaths,
        string[] sourceRevisions, string operationID, int order, string requestedName, string assetPath,
        string modelRevision, string draftToken, string batchToken, Projection destinationProjection,
        string journalPath, string batchDirectory)
    {
        string workDirectory = Path.Combine(batchDirectory, "work-" + operationID);
        TryDeleteDirectory(workDirectory);
        Directory.CreateDirectory(workDirectory);
        string workOutput = Path.Combine(workDirectory, Guid.NewGuid().ToString("N") + ".tif");
        var reporter = new ProgressReporter();
        var job = new BatchJob(reporter, "Terrain raster conversion", uiThreadSyncContext: Utility.SynchronizationContext.None);
        reporter.ProgressReported += (_, progress) =>
        {
            PersistOperationUpdate(journalPath, operationID, old => IsPending(old.Status)
                ? old with { Progress = Math.Clamp(progress / 100.0, 0, 1) } : old);
        };
        using var monitorStop = new CancellationTokenSource();
        Task monitor = MonitorCancellationAsync(batchDirectory, batchToken, operationID, job, monitorStop.Token);
        string? diagnostic = null, fileName = null, outputRevision = null, outputInputID = null;
        string status = "Failed";
        try
        {
            if (HasCancellation(batchDirectory, batchToken, operationID)) job.RequestCancel();
            InterpolatedRasterFileConverterConfig<float> config = InterpolatedRasterFileConverterConfig<float>.Default();
            config.DestinationProjection = destinationProjection;
            config.UseRounding(rounding);
            config.ConvertScaleFactor = scale;
            config.DestinationNoData = -9999f;
            OperationResult conversion = await Task.Run(() =>
                TiffExportEngine.ConvertFile(inputPath, workOutput, config, reporter)).ConfigureAwait(false);
            if (job.Reporter.CancelRequested || HasCancellation(batchDirectory, batchToken, operationID))
            {
                status = "Cancelled";
                diagnostic = "The terrain raster conversion was cancelled.";
            }
            else if (!conversion) diagnostic = conversion.GetConcatenatedMessages();
            else if (!File.Exists(workOutput))
            {
                status = HasCancellation(batchDirectory, batchToken, operationID) ? "Cancelled" : "Failed";
                diagnostic = status == "Cancelled" ? "The terrain raster conversion was cancelled."
                    : "The terrain raster conversion returned success without an output file.";
            }
            else
            {
                VerifyOutputRaster(workOutput);
                for (int index = 0; index < sourcePaths.Length; index++)
                    if (HashSource(sourcePaths[index]) != sourceRevisions[index]) throw new SourceFileChangedException();
                if (HasCancellation(batchDirectory, batchToken, operationID))
                {
                    status = "Cancelled";
                    diagnostic = "The terrain raster conversion was cancelled.";
                }
                else
                {
                    fileName = Guid.NewGuid().ToString("N") + ".tif";
                    string stagedPath = Path.Combine(batchDirectory, fileName);
                    File.Move(workOutput, stagedPath, overwrite: false);
                    VerifyOutputRaster(stagedPath);
                    outputRevision = HashFileContents(stagedPath);
                    outputInputID = ImportInputID(assetPath, modelRevision, draftToken, batchToken, operationID);
                    status = "Staged";
                }
            }
        }
        catch (Exception error)
        {
            bool cancelled = job.Reporter.CancelRequested || HasCancellation(batchDirectory, batchToken, operationID);
            status = cancelled ? "Cancelled" : "Failed";
            diagnostic = cancelled ? "The terrain raster conversion was cancelled." : error.Message;
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { job.MarkComplete(status == "Staged"); } catch { }
            TryDeleteDirectory(workDirectory);
        }
        if (status != "Staged" && fileName != null) TryDeleteFile(Path.Combine(batchDirectory, fileName));
        PersistOperationUpdate(journalPath, operationID, old => old with
        {
            Status = status, OutputInputID = outputInputID, FileName = fileName,
            RequestedName = status == "Staged" ? requestedName : null,
            OutputRevision = outputRevision, Diagnostic = diagnostic,
            Progress = job.Progress / 100.0
        });
        return new(operationID, order, sourceIDs, sourcePaths, sourceRevisions, status,
            outputInputID, null, diagnostic, job.Progress / 100.0);
    }

    private static async Task MonitorCancellationAsync(string batchDirectory, string batchToken,
        string? operationID, BatchJob job, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (HasCancellation(batchDirectory, batchToken, operationID)) { job.RequestCancel(); return; }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasCancellation(string directory, string batchToken, string? operationID) =>
        HasValidCancelMarker(directory, BatchCancelFile, batchToken)
        || operationID != null && HasValidCancelMarker(directory, OperationCancelFile(operationID), batchToken);

    private static CheckedSelection ValidateSelection(TerrainInputImportSelection selection,
        Projection? destinationProjection, int index)
    {
        if (selection == null) throw new ArgumentException("The terrain import contains an empty file selection.");
        if (string.IsNullOrWhiteSpace(selection.Path) || !File.Exists(selection.Path))
            throw new FileNotFoundException("Terrain raster file not found.", selection.Path);
        string path = Path.GetFullPath(selection.Path);
        var inspected = InspectRaster(path, destinationProjection, null);
        if (!string.Equals(inspected.Revision, selection.SourceRevision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        if (!inspected.IsValid) throw new ArgumentException(inspected.Error ?? "The selected file is not a valid terrain DEM.");
        if (!string.IsNullOrWhiteSpace(selection.SourceId)
            && !string.Equals(selection.SourceId, inspected.Id, StringComparison.Ordinal))
            throw new ArgumentException("The terrain import source identity no longer matches the inspected file.");
        if (!Enum.TryParse(selection.Rounding, ignoreCase: false, out RoundTo rounding) || !Enum.IsDefined(rounding))
            throw new ArgumentException("Choose a supported terrain raster rounding value.");
        if (selection.UnitConversion is not ("Default" or "None" or "FeetToMeters" or "MetersToFeet" or "Custom"))
            throw new ArgumentException("Choose a supported terrain unit conversion.");
        float scale = selection.ActualScaleFactor is { } forcedScale ? ToFloat(forcedScale) : selection.UnitConversion switch
        {
            "Default" => inspected.DefaultScaleFactor is { } value ? ToFloat(value) : 1f,
            "None" => 1f, "FeetToMeters" => 0.3048f, "MetersToFeet" => 3.28084f,
            "Custom" => selection.CustomScaleFactor is { } custom ? ToFloat(custom) : throw new ArgumentException("Enter a custom terrain vertical scale factor."),
            _ => throw new ArgumentException("Choose a supported terrain unit conversion.")
        };
        return new(index, SourceID(selection), path, inspected.Revision, rounding, scale);
    }

    private static bool IsItemFailure(Exception error) => error is ArgumentException or IOException
        or UnauthorizedAccessException or InvalidDataException or NotSupportedException;
    internal static string SourceID(TerrainInputImportSelection? selection) =>
        SourceID(SafePath(selection?.Path), selection?.SourceRevision ?? string.Empty);
    private static string SourceID(string path, string revision) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + revision)));
    private static string OperationID(string prefix, int order, string[] sourceIDs) =>
        prefix + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(order + "\n" + string.Join("\n", sourceIDs)))).ToLowerInvariant()[..32];
    private static string ImportInputID(string asset, string revision, string draftToken, string batchToken, string operationID) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(asset) + "\n" + revision + "\nterrain-import:" + draftToken + ":" + batchToken + ":" + operationID)));
    private static string SafePath(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

    private static void VerifyOutputRaster(string filename)
    {
        if (!File.Exists(filename) || new FileInfo(filename).Length == 0)
            throw new InvalidDataException("Terrain import conversion did not produce a readable raster file.");
        using var output = new GDALRaster(filename);
        if (output.PixelsWide <= 0 || output.PixelsTall <= 0 || output.GetBandCount() != 1 || output.GetDataType() != typeof(float))
            throw new InvalidDataException("Terrain import conversion did not produce one valid float32 band.");
    }

    private const string BatchCancelFile = ".cancel";
    private static string OperationCancelFile(string operationID)
    {
        if (operationID.Length is < 3 or > 80 || operationID.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Invalid terrain import operation identity.");
        return ".cancel-" + operationID;
    }
    private static string BatchDirectory(string assetPath, string draftToken, string batchToken) =>
        Path.Combine(DraftDirectory(assetPath, draftToken), "batches", batchToken);
    private static string JournalPath(string batchDirectory) => Path.Combine(batchDirectory, "journal.json");
    private static string LockPath(string batchDirectory) => Path.Combine(batchDirectory, ".lock");

    private static async Task<FileStream> AcquireBatchLockAsync(string batchDirectory)
    {
        Directory.CreateDirectory(batchDirectory);
        DateTime started = DateTime.UtcNow;
        while (true)
        {
            try { return new FileStream(LockPath(batchDirectory), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow - started < TimeSpan.FromMinutes(10))
            { await Task.Delay(100).ConfigureAwait(false); }
        }
    }
    private static TerrainImportBatchJournal? ReadJournal(string path)
    {
        if (!File.Exists(path)) return null;
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<TerrainImportBatchJournal>(stream, BackendJson.Options)
            ?? throw new InvalidDataException("The terrain import batch journal is empty.");
    }
    private static void WriteJournal(string path, TerrainImportBatchJournal journal)
    {
        object gate = JournalGates.GetOrAdd(path, _ => new object());
        lock (gate)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, journal, BackendJson.Options); stream.Flush(flushToDisk: true); }
                File.Move(temporary, path, overwrite: true);
            }
            finally { TryDeleteFile(temporary); }
        }
    }
    private static string RequestDigest(TerrainInputAppendRequest request, string asset, string draftToken, string batchToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string? value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        void AddBytes(byte[]? bytes)
        {
            bytes ??= [];
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        void AddFields(Dictionary<string, string>? values)
        {
            if (values == null) { Add("fields:null"); return; }
            Add("fields:" + values.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { Add(pair.Key); Add(pair.Value); }
        }
        void AddRows(Dictionary<string, ModelRow[]>? values)
        {
            if (values == null) { Add("rows:null"); return; }
            Add("rows:" + values.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Add(pair.Key); Add(pair.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (ModelRow row in pair.Value)
                {
                    Add(row.Id);
                    Add(row.Values.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    foreach (var cell in row.Values.OrderBy(cell => cell.Key, StringComparer.Ordinal)) { Add(cell.Key); Add(cell.Value); }
                }
            }
        }
        void AddSamples(Dictionary<string, ModelSample[]>? values)
        {
            if (values == null) { Add("samples:null"); return; }
            Add("samples:" + values.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Add(pair.Key); Add(pair.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (ModelSample sample in pair.Value)
                {
                    Add(sample.Id);
                    Add(sample.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Add(sample.Y?.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Add(sample.Series); Add(sample.DatasetId);
                }
            }
        }
        Add(asset); Add(request.Model.Kind); Add(request.Model.Revision); Add(request.Model.ProjectPath);
        Add(request.Model.ProjectXml); Add(request.Model.SourceState); AddFields(request.Model.Fields);
        if (request.Model.Images == null) Add("images:null");
        else
        {
            Add("images:" + request.Model.Images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var pair in request.Model.Images.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Add(pair.Key); Add(pair.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (ImageData image in pair.Value) { Add(image.Id); Add(image.Name); AddBytes(image.Data); }
            }
        }
        AddRows(request.Model.Rows); AddSamples(request.Model.Samples);
        if (request.Model.PlanTimeWindowEdits == null) Add("timeWindowEdits:null");
        else
        {
            Add("timeWindowEdits:" + request.Model.PlanTimeWindowEdits.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (PlanTimeWindowEdit edit in request.Model.PlanTimeWindowEdits) { Add(edit.Id); Add(edit.FieldID); Add(edit.Value); }
        }
        Add(request.Model.GeometryTopologyState); Add(request.Model.GeometryMeshState); Add(request.Model.GeometryCulvertState);
        Add(draftToken); Add(batchToken); Add(request.StitchTiles.ToString());
        Add(request.Files.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (TerrainInputImportSelection selection in request.Files)
        {
            Add(SafePath(selection?.Path)); Add(selection?.SourceRevision); Add(selection?.Rounding);
            Add(selection?.UnitConversion); Add(selection?.CustomScaleFactor?.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (selection?.ActualScaleFactor is { } actualScale)
            {
                Add("actualScaleFactor");
                Add(actualScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            Add(selection?.SourceId);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void ValidateJournalIdentity(TerrainImportBatchJournal journal, string asset,
        string draftToken, string batchToken, string digest)
    {
        if (journal.Version != 1 || journal.Asset != Path.GetFullPath(asset) || journal.DraftToken != draftToken || journal.BatchToken != batchToken)
            throw new ArgumentException("The terrain import batch identity does not match its journal.");
        if (!string.Equals(journal.RequestDigest, digest, StringComparison.Ordinal))
            throw new ArgumentException("This terrain import batch token was already used for a different request.");
    }
    private static TerrainImportBatchJournal RecoverInterruptedBatch(TerrainImportBatchJournal journal, string batchDirectory)
    {
        bool cancelled = HasValidCancelMarker(batchDirectory, BatchCancelFile, journal.BatchToken);
        TerrainImportOperationJournal[] operations = journal.Operations.Select(operation =>
        {
            if (!IsPending(operation.Status)) return operation;
            bool opCancelled = cancelled || HasValidCancelMarker(batchDirectory, OperationCancelFile(operation.OperationID), journal.BatchToken);
            return operation with
            {
                Status = opCancelled ? "Cancelled" : "Failed",
                Diagnostic = opCancelled ? "The terrain import was cancelled before this operation completed."
                    : "The terrain import process ended before this operation completed."
            };
        }).ToArray();
        foreach (string path in Directory.EnumerateDirectories(batchDirectory, "work-*", SearchOption.TopDirectoryOnly)) TryDeleteDirectory(path);
        var retained = operations.Where(operation => operation.Status == "Staged" && operation.FileName != null)
            .Select(operation => operation.FileName!).ToHashSet(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(batchDirectory, "*.tif", SearchOption.TopDirectoryOnly))
            if (!retained.Contains(Path.GetFileName(path))) TryDeleteFile(path);
        return FinalizeJournal(journal with { Operations = operations });
    }
    private static bool TryRecoverInterruptedBatch(string directory, TerrainImportBatchJournal journal,
        out TerrainImportBatchJournal? recovered)
    {
        recovered = null;
        FileStream batchLock;
        try { batchLock = new FileStream(LockPath(directory), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return false; }
        using (batchLock)
        {
            TerrainImportBatchJournal? current = ReadJournal(JournalPath(directory));
            if (current == null || current.State != "Running") { recovered = current; return false; }
            recovered = RecoverInterruptedBatch(current, directory);
            WriteJournal(JournalPath(directory), recovered);
            return true;
        }
    }
    private static TerrainImportBatchJournal FinalizeJournal(TerrainImportBatchJournal journal)
    {
        bool staged = journal.Operations.Any(operation => operation.Status == "Staged");
        bool failed = journal.Operations.Any(operation => operation.Status == "Failed");
        bool cancelled = journal.Operations.Any(operation => operation.Status == "Cancelled");
        string state = staged ? cancelled ? "CancelledPartial" : failed ? "Partial" : "Completed"
            : cancelled ? "Cancelled" : "Failed";
        return journal with { State = state };
    }
    private static TerrainImportBatchJournal UpdateOperation(TerrainImportBatchJournal journal, string operationID,
        Func<TerrainImportOperationJournal, TerrainImportOperationJournal> update) =>
        journal with { Operations = journal.Operations.Select(operation => operation.OperationID == operationID ? update(operation) : operation).ToArray() };
    private static void PersistOperationUpdate(string journalPath, string operationID,
        Func<TerrainImportOperationJournal, TerrainImportOperationJournal> update)
    {
        object gate = JournalGates.GetOrAdd(journalPath, _ => new object());
        lock (gate)
        {
            TerrainImportBatchJournal? current = ReadJournal(journalPath);
            if (current == null || !current.Operations.Any(operation => operation.OperationID == operationID)) return;
            WriteJournal(journalPath, UpdateOperation(current, operationID, update));
        }
    }
    private static bool IsPending(string status) => status is "Queued" or "Running";
    private static bool HasValidCancelMarker(string directory, string fileName, string nonce)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || new FileInfo(path).LinkTarget != null) return false;
        try
        {
            if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite)) return false;
            return string.Equals(File.ReadAllText(path, Encoding.UTF8), nonce, StringComparison.Ordinal);
        }
        catch { return false; }
    }
    private static void WriteCancelMarker(string directory, string fileName, string nonce)
    {
        string path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            if (HasValidCancelMarker(directory, fileName, nonce)) return;
            throw new IOException("A terrain import cancellation marker is invalid.");
        }
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        byte[] bytes = Encoding.UTF8.GetBytes(nonce);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }

    internal static bool Discard(string assetPath, string draftToken)
    {
        string directory = DraftDirectory(assetPath, NormalizeToken(draftToken, "terrain import draft"));
        TryDeleteDirectory(directory);
        return !Directory.Exists(directory);
    }

    internal static bool DiscardBatch(string assetPath, string draftToken, string batchToken)
    {
        string draftDirectory = DraftDirectory(assetPath, NormalizeToken(draftToken, "terrain import draft"));
        string normalizedBatchToken = NormalizeToken(batchToken, "terrain import batch");
        TryDeleteDirectory(Path.Combine(draftDirectory, "batches", normalizedBatchToken));
        TryDeleteDirectory(Path.Combine(draftDirectory, ".work-" + normalizedBatchToken));
        return !Directory.Exists(Path.Combine(draftDirectory, "batches", normalizedBatchToken))
            && !Directory.Exists(Path.Combine(draftDirectory, ".work-" + normalizedBatchToken));
    }

    internal static string DraftDirectory(string assetPath, string draftToken)
    {
        string canonicalAsset = Path.GetFullPath(assetPath);
        string assetKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalAsset))).ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "HECRAS-TerrainInputDrafts", assetKey, draftToken);
    }

    internal static string NormalizeToken(string token, string description)
    {
        if (!Guid.TryParseExact(token, "N", out Guid guid)) throw new ArgumentException("Invalid " + description + " token.");
        return guid.ToString("N");
    }

    internal static string HashSource(string filename)
    {
        filename = Path.GetFullPath(filename);
        if (!File.Exists(filename)) return "missing";
        bool isVirtualRaster = Path.GetExtension(filename).Equals(".vrt", StringComparison.OrdinalIgnoreCase);
        string[] dependencies = isVirtualRaster
            ? (GDALRaster.GetVRTSources(filename) ?? []).Select(source => Path.IsPathRooted(source)
                ? Path.GetFullPath(source)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filename)!, source)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : [];

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (isVirtualRaster)
        {
            foreach (string path in dependencies)
            {
                byte[] name = Encoding.UTF8.GetBytes(path);
                hash.AppendData(BitConverter.GetBytes(name.Length));
                hash.AppendData(name);
                AppendFile(hash, path);
            }
        }
        AppendFile(hash, filename);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string HashFileContents(string filename)
    {
        using Stream input = File.OpenRead(filename);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        if (!File.Exists(path))
        {
            hash.AppendData(BitConverter.GetBytes(-1L));
            return;
        }
        using Stream input = File.OpenRead(path);
        hash.AppendData(BitConverter.GetBytes(input.Length));
        byte[] buffer = new byte[65536];
        int count;
        while ((count = input.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, count));
    }

    private static float ToFloat(double value)
    {
        float converted = (float)value;
        if (!double.IsFinite(value) || !float.IsFinite(converted)) throw new ArgumentException("Terrain vertical scale factor must be a finite float value.");
        return converted;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
