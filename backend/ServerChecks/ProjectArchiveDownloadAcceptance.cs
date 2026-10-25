using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record ProjectArchiveDownloadAcceptanceReport(
    string State, string ProjectPath, string SourceProjectSHA256,
    ProjectArchiveDownloadEvidence[] Downloads, bool SourceUnchanged,
    bool InvalidAndMissingDenied, bool PendingAndSymlinkDenied,
    bool CleanupIdempotent, bool CancellationRemovedArchive, string CancellationOperationID);

internal sealed record ProjectArchiveDownloadEvidence(
    bool IncludeResults, string CleanupToken, string DownloadPath,
    string ArchiveSHA256, string ProjectSHA256, string InventorySHA256, int EntryCount);

internal static partial class ServerAcceptance
{
    public static async Task<ProjectArchiveDownloadAcceptanceReport> RunProjectArchiveDownloadAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Archive acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));
        string output = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("Archive acceptance requires a fresh external output directory.");
        string fixture = Path.Combine(output, "fixture");
        Directory.CreateDirectory(fixture);
        RasRuntimeBootstrap.Configure();
        string projectPath = Path.Combine(fixture, "Archive HTTP.ras");
        Project project = Project.CreateNew(projectPath, UnitSystem.SI);
        string resultPath = Path.Combine(project.ResultsDirectory, "result-payload.txt");
        Directory.CreateDirectory(project.ResultsDirectory);
        File.WriteAllText(resultPath, "Results content preserved by the source archive method.\n");
        File.WriteAllText(Path.Combine(fixture, "支持文件.txt"), "UTF-8 supporting content\n");
        File.WriteAllText(Path.Combine(fixture, ".lock"), "Excluded from archive, preserved in source.");
        string sourceRevision = ArchiveCheckSHA(projectPath);
        Dictionary<string, string> before = ArchiveCheckInventory(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        var downloads = new List<ProjectArchiveDownloadEvidence>();
        string? archiveRoot = null;
        foreach (bool includeResults in new[] { false, true })
        {
            JsonElement result = (await SubmitOperationAsync(client, "developer.projectArchive.prepare",
                new { projectPath, includeResults, expectedProjectSHA256 = sourceRevision }, cancellation)).Result;
            DeveloperProjectArchiveResult receipt = result.Deserialize<DeveloperProjectArchiveResult>(JsonOptions)
                ?? throw new InvalidDataException("Archive prepare omitted its receipt.");
            ArchiveRequire(receipt.State == "completed" && receipt.ProjectPath == projectPath &&
                receipt.SourceProjectSHA256 == sourceRevision && receipt.IncludeResults == includeResults,
                "Prepare receipt does not match the captured source request.");
            archiveRoot = Path.GetDirectoryName(receipt.ArchivePath)!;
            try
            {
                ArchiveRequire(!Directory.Exists(Path.Combine(archiveRoot, ".stage-" + receipt.CleanupToken)) &&
                    !File.Exists(Path.Combine(archiveRoot, ".pending-" + receipt.CleanupToken + ".zip")),
                    "Prepare returned before its private staging/pending files were removed.");
                using HttpResponseMessage response = await client.GetAsync("api/project-archives/" + receipt.CleanupToken,
                    HttpCompletionOption.ResponseHeadersRead, cancellation);
                ArchiveRequire(response.StatusCode == HttpStatusCode.OK &&
                    response.Content.Headers.ContentType?.MediaType == "application/zip" &&
                    response.Content.Headers.ContentDisposition?.DispositionType == "attachment" &&
                    response.Headers.CacheControl?.NoStore == true,
                    "Download did not return a non-cacheable ZIP attachment.");
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
                string downloadPath = Path.Combine(output, includeResults ? "with-results.zip" : "without-results.zip");
                await File.WriteAllBytesAsync(downloadPath, bytes, cancellation);
                ArchiveRequire(ArchiveCheckSHA(downloadPath) == ArchiveCheckSHA(receipt.ArchivePath),
                    "HTTP ZIP bytes differ from the already prepared archive.");
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var entries = new Dictionary<string, (long Length, string SHA)>(StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    using Stream stream = entry.Open();
                    ArchiveRequire(entries.TryAdd(entry.FullName,
                        (entry.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant())),
                        "Downloaded ZIP contains duplicate names.");
                }
                string prefix = Path.GetFileName(fixture) + "/";
                string projectEntry = prefix + Path.GetFileName(projectPath);
                ArchiveRequire(entries.Count == receipt.EntryCount && entries[projectEntry].SHA == receipt.ProjectSHA256,
                    "Downloaded ZIP project SHA or entry count differs from its receipt.");
                string inventoryText = string.Concat(entries.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => $"{item.Key}\0{item.Value.Length}\0{item.Value.SHA}\n"));
                string inventorySHA = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventoryText))).ToLowerInvariant();
                ArchiveRequire(inventorySHA == receipt.InventorySHA256,
                    "Downloaded ZIP full entry hashes differ from its inventory receipt.");
                string resultEntry = prefix + Path.GetRelativePath(fixture, resultPath).Replace('\\', '/');
                ArchiveRequire(entries.ContainsKey(resultEntry) == includeResults &&
                    !entries.Keys.Any(name => name.EndsWith("/.lock", StringComparison.Ordinal)),
                    "includeResults or .lock filtering differs from the retained source semantics.");
                foreach (var source in before)
                {
                    string name = prefix + Path.GetRelativePath(fixture, source.Key).Replace('\\', '/');
                    if (name == projectEntry || Path.GetFileName(source.Key) == ".lock" ||
                        (!includeResults && source.Key.StartsWith(project.ResultsDirectory, StringComparison.Ordinal))) continue;
                    ArchiveRequire(entries.TryGetValue(name, out var entry) && entry.SHA == source.Value,
                        "Downloaded ZIP changed a supporting source file: " + name);
                }
                await ArchiveExpectMissing(client, receipt.CleanupToken.ToUpperInvariant(), cancellation);
                downloads.Add(new(includeResults, receipt.CleanupToken, downloadPath, ArchiveCheckSHA(downloadPath),
                    receipt.ProjectSHA256, inventorySHA, entries.Count));
            }
            finally
            {
                JsonElement cleaned = (await SubmitOperationAsync(client, "developer.projectArchive.cleanup",
                    new { cleanupToken = receipt.CleanupToken }, cancellation)).Result;
                ArchiveRequire(BooleanProperty(cleaned, "cleaned"), "Archive cleanup failed.");
            }
            await ArchiveExpectMissing(client, receipt.CleanupToken, cancellation);
            JsonElement secondCleanup = (await SubmitOperationAsync(client, "developer.projectArchive.cleanup",
                new { cleanupToken = receipt.CleanupToken }, cancellation)).Result;
            ArchiveRequire(BooleanProperty(secondCleanup, "cleaned"), "Archive cleanup is not idempotent.");
            ArchiveRequire(ArchiveCheckInventoryMatches(before, ArchiveCheckInventory(fixture)),
                "Preparing/downloading/cleaning an archive changed the source project.");
        }
        foreach (string invalid in new[] { "invalid-token", Guid.NewGuid().ToString("D"),
                     downloads[0].CleanupToken.ToUpperInvariant(), downloads[0].CleanupToken.Replace("-", ""), "%2e%2e%2fsource.ras" })
            await ArchiveExpectMissing(client, invalid, cancellation);
        JsonElement invalidCleanup = (await SubmitOperationAsync(client, "developer.projectArchive.cleanup",
            new { cleanupToken = projectPath }, cancellation)).Result;
        ArchiveRequire(!BooleanProperty(invalidCleanup, "cleaned") && ArchiveCheckSHA(projectPath) == sourceRevision,
            "Cleanup accepted a caller-supplied source path.");
        string pendingToken = Guid.NewGuid().ToString("D");
        string pendingPath = Path.Combine(archiveRoot!, ".pending-" + pendingToken + ".zip");
        string linkedToken = Guid.NewGuid().ToString("D");
        string linkedPath = Path.Combine(archiveRoot!, linkedToken + ".zip");
        try
        {
            await File.WriteAllTextAsync(pendingPath, "unfinished", cancellation);
            await ArchiveExpectMissing(client, pendingToken, cancellation);
            File.CreateSymbolicLink(linkedPath, projectPath);
            await ArchiveExpectMissing(client, linkedToken, cancellation);
            ArchiveRequire(!DeveloperProjectArchiveService.Cleanup(new(linkedToken)).Cleaned,
                "Cleanup must reject archive symbolic links.");
        }
        finally
        {
            File.Delete(pendingPath);
            File.Delete(linkedPath);
        }
        // Queue a real duplex control immediately after HTTP submission. The large
        // file leaves a deterministic processing window even when the worker is warm.
        string cancellationPayload = Path.Combine(fixture, "cancel-payload.bin");
        using (var payload = File.Create(cancellationPayload)) payload.SetLength(64 * 1024 * 1024);
        Dictionary<string, string> beforeCancellation = ArchiveCheckInventory(fixture);
        using HttpResponseMessage submitted = await client.PostAsJsonAsync("api/operations", new
        {
            method = "developer.projectArchive.prepare",
            parameters = new { projectPath, includeResults = true, expectedProjectSHA256 = sourceRevision },
        }, JsonOptions, cancellation);
        ArchiveRequire(submitted.StatusCode == HttpStatusCode.Accepted, "Cancellation fixture was not accepted.");
        using JsonDocument accepted = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync(cancellation));
        string operationID = StringProperty(accepted.RootElement, "id");
        using HttpResponseMessage cancel = await client.PostAsJsonAsync($"api/operations/{operationID}/controls",
            new { control = new { type = "cancel" } }, JsonOptions, cancellation);
        ArchiveRequire(cancel.StatusCode == HttpStatusCode.Accepted, "HTTP duplex cancellation was not accepted.");
        DeveloperProjectArchiveResult cancelled;
        while (true)
        {
            using JsonDocument polled = await GetJsonAsync(client, $"api/operations/{operationID}", cancellation);
            string state = StringProperty(polled.RootElement, "state");
            if (IsTerminal(state))
            {
                ArchiveRequire(state == "canceled", "Archive cancellation did not reach canceled state: " + polled.RootElement);
                cancelled = Property(polled.RootElement, "result").Deserialize<DeveloperProjectArchiveResult>(JsonOptions)
                    ?? throw new InvalidDataException("Cancellation omitted its archive receipt.");
                break;
            }
            await Task.Delay(50, cancellation);
        }
        ArchiveRequire(cancelled.State == "cancelled" && cancelled.EntryCount == 0 &&
            !File.Exists(cancelled.ArchivePath) &&
            !File.Exists(Path.Combine(archiveRoot!, ".pending-" + cancelled.CleanupToken + ".zip")) &&
            !Directory.Exists(Path.Combine(archiveRoot!, ".stage-" + cancelled.CleanupToken)),
            "Cancelled archive left staging, pending or published ZIP content.");
        await ArchiveExpectMissing(client, cancelled.CleanupToken, cancellation);
        ArchiveRequire(ArchiveCheckInventoryMatches(beforeCancellation, ArchiveCheckInventory(fixture)),
            "Cancellation changed the source project.");
        return new("passed", projectPath, sourceRevision, downloads.ToArray(), true, true, true, true, true, operationID);
    }

    private static async Task ArchiveExpectMissing(HttpClient client, string token, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.GetAsync("api/project-archives/" + token, cancellation);
        ArchiveRequire(response.StatusCode == HttpStatusCode.NotFound, "Unsafe or missing archive token was not denied: " + token);
    }

    private static string ArchiveCheckSHA(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static Dictionary<string, string> ArchiveCheckInventory(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, ArchiveCheckSHA, StringComparer.Ordinal);

    private static bool ArchiveCheckInventoryMatches(Dictionary<string, string> expected, Dictionary<string, string> actual) =>
        expected.Count == actual.Count && expected.All(item => actual.TryGetValue(item.Key, out string? hash) && hash == item.Value);

    private static void ArchiveRequire(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
