using System.Security.Cryptography;
using System.Text.Json;
using Ras.Layers.Classic;
using Ras.Layers.Terrains;
using Ras.Migration;

namespace Ras.Backend;

public sealed record ChannelV6GeometryImportRequest(ModelSaveRequest Model, string InputId,
    string SourcePath, string SourceSHA256, string ExpectedDraftIdentity);

public sealed record ChannelV6GeometryImportResponse(string State,
    ModelSnapshot? Snapshot = null, TerrainTopologyData? Topology = null,
    string? SourceRevision = null, string? ErrorMessage = null);

/// <summary>
/// Self-contained draft payload for replaying an imported topology. No temporary
/// absolute path is retained in the terrain draft or sent back to the client.
/// </summary>
internal sealed record TerrainChannelGeometryPayload(string Id, string FileName, string SourceSHA256,
    string MigratedSHA256);

internal static class TerrainChannelGeometryPayloadCodec
{
    internal static TerrainChannelGeometryPayload Stage(string assetPath, string draftToken,
        string migratedPath, string sourceSHA256)
    {
        string token = TerrainInputImportService.NormalizeToken(draftToken, "terrain import draft");
        string id = Guid.NewGuid().ToString("N");
        string fileName = id + ".hdf";
        string path = PathFor(assetPath, token, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (FileStream source = new(migratedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        }
        string migratedSHA = HashFile(path);
        return new TerrainChannelGeometryPayload(id, fileName, sourceSHA256, migratedSHA);
    }

    internal static T WithClassicGeometry<T>(string assetPath, string draftToken,
        TerrainChannelGeometryPayload payload,
        Func<ClassicGeometry, T> operation)
    {
        Validate(payload);
        string path = VerifyFile(assetPath, draftToken, payload);
        using ClassicGeometry geometry = new(path);
        return operation(geometry);
    }

    internal static string VerifyFile(string assetPath, string draftToken, TerrainChannelGeometryPayload payload)
    {
        Validate(payload);
        string path = PathFor(assetPath, draftToken, payload.FileName);
        if (!File.Exists(path) || !StringComparer.Ordinal.Equals(HashFile(path), payload.MigratedSHA256))
            throw new InvalidDataException("The migrated Channel geometry draft payload failed its SHA-256 check.");
        return path;
    }

    internal static void Validate(TerrainChannelGeometryPayload payload)
    {
        if (payload is null || payload.Id is not { Length: 32 } || !Guid.TryParseExact(payload.Id, "N", out _)
            || !StringComparer.Ordinal.Equals(payload.FileName, payload.Id + ".hdf")
            || !IsSHA256(payload.SourceSHA256) || !IsSHA256(payload.MigratedSHA256)
            )
            throw new ArgumentException("The imported Channel geometry draft payload is invalid.");
    }

    internal static string PathFor(string assetPath, string draftToken, string fileName)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _)
            || !StringComparer.Ordinal.Equals(fileName, Path.GetFileName(fileName))
            || !fileName.EndsWith(".hdf", StringComparison.Ordinal))
            throw new ArgumentException("The imported Channel geometry file name is invalid.");
        string token = TerrainInputImportService.NormalizeToken(draftToken, "terrain import draft");
        return Path.Combine(TerrainInputImportService.DraftDirectory(assetPath, token), "channel-geometry", fileName);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsSHA256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}

public static partial class ModelEditorAdapter
{
    internal static async Task<ChannelV6GeometryImportResponse> ImportChannelV6GeometryDuplexAsync(
        ChannelV6GeometryImportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader controls)
    {
        using CancellationTokenSource cancellation = new();
        using CancellationTokenSource watcherStop = new();
        Task watcher = WatchCancellationAsync(controls, cancellation, watcherStop.Token);
        string? workingDirectory = null;
        string? stagedPayloadPath = null;
        string? stagedDraftToken = null;
        bool createdDraftToken = false;
        bool keepDraftPayload = false;
        try
        {
            CheckTerrain(request.Model);
            string sourcePath = SourceFilePaths.Canonical(request.SourcePath);
            if (!File.Exists(sourcePath) || !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(sourcePath), ".hdf")
                || !IsSHA256(request.SourceSHA256) || string.IsNullOrWhiteSpace(request.InputId)
                || string.IsNullOrWhiteSpace(request.ExpectedDraftIdentity))
                throw new ArgumentException("Choose a Version 6 HDF geometry and load the selected Channel topology before importing.");
            string requestDraftIdentity = DraftIdentity(request.InputId, request.Model.Revision, request.Model.SourceState);
            if (!StringComparer.Ordinal.Equals(requestDraftIdentity, request.ExpectedDraftIdentity))
                throw new SourceFileChangedException();

            using ModelContext context = new(request.Model);
            TerrainEditor editor = TerrainInputs(context, request.Model.AssetPath, request.Model.Revision,
                request.Model.SourceState) ?? throw new ArgumentException("This model is not a terrain.");
            editor.CaptureSelectedInput(request.Model.Fields);
            editor.ApplySelections(request.Model.TableSelections);
            ApplyFields(context, null, request.Model.Fields);
            editor.Apply(request.Model.Fields);
            editor.ApplyRows(request.Model.Rows, request.Model.ChangedCells);
            editor.VerifyDependencies();
            TerrainTopologyData current = editor.LoadChannelTopology(request.InputId);
            string? existingDraftToken = editor.ImportDraftToken;

            cancellation.Token.ThrowIfCancellationRequested();
            report?.Invoke(new BackendOperationProgress(Fraction: 0.05, TaskName: "Checking Version 6 geometry"));
            string actualSourceRevision = await HashFileAsync(sourcePath, cancellation.Token).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(actualSourceRevision, request.SourceSHA256))
                throw new SourceFileChangedException();

            // The retained converter may generate and save missing 1D helper
            // layers in its input HDF. Give it an exact private copy so import
            // never changes the source selected by the user.
            workingDirectory = Path.Combine(Path.GetTempPath(), "hecras-channel-v6-" + Guid.NewGuid().ToString("N"));
            string inputDirectory = Path.Combine(workingDirectory, "input");
            Directory.CreateDirectory(inputDirectory);
            string inputPath = Path.Combine(inputDirectory, Path.GetFileName(sourcePath));
            await using (FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream copy = new(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(copy, cancellation.Token).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(await HashFileAsync(inputPath, cancellation.Token).ConfigureAwait(false), request.SourceSHA256)
                || !StringComparer.Ordinal.Equals(await HashFileAsync(sourcePath, cancellation.Token).ConfigureAwait(false), request.SourceSHA256))
                throw new SourceFileChangedException();
            string outputPath = Path.Combine(workingDirectory, "migrated.hdf");
            report?.Invoke(new BackendOperationProgress(Fraction: 0.12, TaskName: "Converting Version 6 geometry"));
            bool migrated = await Task.Run(() => ModelConverter.MigrateGeometry(inputPath, outputPath,
                new MigrationParams { AsClassic = true, Generate1DLines = Generate1DLinesMode.IfNeeded })).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!migrated || !File.Exists(outputPath))
                throw new InvalidDataException("Version 6 geometry conversion did not produce a Classic geometry file.");
            if (!StringComparer.Ordinal.Equals(await HashFileAsync(sourcePath, cancellation.Token).ConfigureAwait(false), request.SourceSHA256))
                throw new SourceFileChangedException();

            cancellation.Token.ThrowIfCancellationRequested();
            report?.Invoke(new BackendOperationProgress(Fraction: 0.72, TaskName: "Preparing imported Channel geometry"));
            TerrainTopologyData topology = editor.ImportChannelV6Geometry(request.InputId, outputPath,
                request.SourceSHA256, current.DraftIdentity, out string stagedPath);
            stagedPayloadPath = stagedPath;
            stagedDraftToken = editor.ImportDraftToken;
            createdDraftToken = existingDraftToken == null;
            editor.VerifyDependencies();
            ModelSnapshot snapshot = await Snapshot(context, request.Model.Revision, true, null, terrain: editor);
            editor.VerifyDependencies();
            CheckTerrain(request.Model);
            report?.Invoke(new BackendOperationProgress(Fraction: 0.86, TaskName: "Verifying imported geometry source"));
            await Task.Yield();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(await HashFileAsync(sourcePath, cancellation.Token).ConfigureAwait(false), request.SourceSHA256))
                throw new SourceFileChangedException();
            cancellation.Token.ThrowIfCancellationRequested();
            report?.Invoke(new BackendOperationProgress(Fraction: 1, TaskName: "Channel geometry is ready to save"));
            keepDraftPayload = true;
            return new("completed", snapshot, topology, request.SourceSHA256);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new("cancelled", ErrorMessage: "The import was cancelled before the terrain draft changed.");
        }
        finally
        {
            watcherStop.Cancel();
            try { await watcher.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            if (workingDirectory is not null && Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
            if (!keepDraftPayload && stagedPayloadPath is not null && File.Exists(stagedPayloadPath))
            {
                if (createdDraftToken && stagedDraftToken is not null)
                    _ = TerrainInputImportService.Discard(request.Model.AssetPath, stagedDraftToken);
                else File.Delete(stagedPayloadPath);
            }
        }
    }

    private static async Task WatchCancellationAsync(BackendDuplexControlReader controls,
        CancellationTokenSource cancellation, CancellationToken stop)
    {
        await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stop).ConfigureAwait(false))
        {
            if (!frame.Control.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String || type.GetString() != "cancel")
                throw new InvalidDataException("The Channel geometry import received an unknown duplex control.");
            cancellation.Cancel();
            return;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            hash.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool IsSHA256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    internal static string DraftIdentity(string inputID, string revision, string? sourceState)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(inputID + "\n" + revision + "\n" + (sourceState ?? ""));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
