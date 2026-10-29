using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Runtime;
using Utility.Memory;

namespace Ras.Backend.ServerChecks;

/// <summary>
/// Exercises the actual owned-stage reader, retained GeoJSON writer/exporter and
/// worker dispatch with real files. This is not evidence of an online provider query.
/// </summary>
internal static class FeatureQueryPreviewAcceptance
{
    private sealed record LayerInput(string MapType, string GeoJson, int FeatureCount);
    private sealed record Stage(string Id, string Directory, string[] Paths, MapSourceMapCapture Map);
    private sealed record DuplexWorkerResult(int ExitCode, JsonElement Terminal, bool CancelSent);

    public static async Task<object> RunAsync(string requestedDirectory)
    {
        string root = Path.GetFullPath(requestedDirectory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("Feature query acceptance requires a new directory.");
        Directory.CreateDirectory(root);
        RasRuntimeBootstrap.Configure();
        string projectPath = Path.Combine(root, "Query Preview.ras");
        Project project = Project.CreateNew(projectPath, ProjectionDefinition.FromEPSG(3857));
        project.Save();
        string visPath = Path.ChangeExtension(projectPath, ".vis");
        await File.WriteAllTextAsync(visPath,
            "<Ras><Maps><Map Name=\"查询预览验收\" Type=\"Project\" /></Maps></Ras>");
        string projectHash = Hash(projectPath);
        string visHash = Hash(visPath);
        var map = new MapTreeRequest(projectPath, null, null, "query-preview-acceptance", 0,
            "查询预览验收", "Project", visHash);
        MapSourceMapCapture capture = MapSourceIdentity.CaptureMap(map);
        var stages = new List<Stage>();
        var checks = new Dictionary<string, bool>();

        Stage Create(LayerInput[] layers, DateTime? created = null)
        {
            Stage stage = CreateStage(capture, layers, created ?? DateTime.UtcNow);
            stages.Add(stage);
            return stage;
        }
        FeatureQueryPreviewResponse Preview(Stage stage) =>
            FeatureQueryService.Preview(new(map, stage.Id, capture));
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidDataException("Feature query acceptance failed: " + name);
            checks.Add(name, true);
        }

        try
        {
            var table = new Table();
            table.AddColumn("名称", typeof(string));
            table.AddColumn("reading", typeof(double));
            table.AddColumn("active", typeof(bool));
            table.AddNewRow(["真实工程坐标甲", 12.5, true]);
            table.AddNewRow(["真实工程坐标乙", 7.25, false]);
            string writerPath = Path.Combine(root, "retained-writer.geojson");
            var write = GeoJsonWriter.Write(writerPath,
                new PointCollection(new[] { new Point(12_345, 23_456), new Point(34_567, 45_678) }), null, table);
            if (!write.Result) throw new InvalidOperationException(write.GetConcatenatedMessages());
            Stage small = Create([new("PointsVector", File.ReadAllText(writerPath), 2),
                new("PolygonsVector", Collection(Feature(new
                {
                    type = "MultiPolygon",
                    coordinates = new[]
                    {
                        new[]
                        {
                            new[] { new[] { 0d, 0d }, new[] { 4d, 0d }, new[] { 4d, 4d }, new[] { 0d, 0d } },
                            new[] { new[] { 1d, 1d }, new[] { 2d, 1d }, new[] { 1d, 2d }, new[] { 1d, 1d } }
                        },
                        new[] { new[] { new[] { 5d, 5d }, new[] { 6d, 5d }, new[] { 5d, 6d }, new[] { 5d, 5d } } }
                    }
                }, new { name = "保留多面与洞", value = 12 })), 1)]);
            Dictionary<string, string> stagedHashes = small.Paths.ToDictionary(path => path, Hash);
            FeatureQueryPreviewResponse preview = Preview(small);
            JsonElement first = preview.Layers[0].FeatureCollection.Features[0];
            Check("actual_project_world_coordinates_and_typed_properties", preview.CoordinateSpace == "ProjectWorld" &&
                first.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble() == 12_345 &&
                first.GetProperty("properties").GetProperty("名称").GetString() == "真实工程坐标甲" &&
                first.GetProperty("properties").GetProperty("reading").GetDouble() == 12.5 &&
                first.GetProperty("properties").GetProperty("active").GetBoolean());
            Check("multipolygon_holes_and_counts_preserved", !preview.Truncated && preview.TotalFeatureCount == 3 &&
                preview.TotalCoordinateCount == 14 && preview.ReturnedCoordinateCount == 14 &&
                preview.Layers[1].FeatureCollection.Features[0].GetProperty("geometry")
                    .GetProperty("coordinates")[0].GetArrayLength() == 2);
            Check("preview_preserves_all_source_files", stagedHashes.All(entry => Hash(entry.Key) == entry.Value) &&
                Hash(projectPath) == projectHash && Hash(visPath) == visHash);

            ExpectFailure(() => FeatureQueryService.Preview(new(map, "../arbitrary", capture)));
            ExpectFailure(() => FeatureQueryService.Preview(new(map, small.Id, capture with { MapID = "other" })));
            ExpectFailure(() => FeatureQueryService.Discard(new(small.Id, capture with { MapID = "other" })));
            Check("result_id_and_original_map_binding_reject_mismatch", Directory.Exists(small.Directory));

            string onePoint = Feature(new { type = "Point", coordinates = new[] { 12_345d, 23_456d } },
                new { name = "保留属性", value = true });
            Stage featureBudget = Create([new("PointsVector", Collection(Enumerable.Repeat(onePoint, 1_200)), 1_200),
                new("PointsVector", Collection(Enumerable.Repeat(onePoint, 1_200)), 1_200)]);
            FeatureQueryPreviewResponse limited = Preview(featureBudget);
            Check("feature_budget_is_shared_across_layers", limited.TotalFeatureCount == 2_400 &&
                limited.ReturnedFeatureCount == limited.Budget.MaxFeatures && limited.Truncated &&
                limited.Layers[1].ReturnedFeatureCount == 800 && limited.Warnings.Length > 0);

            double[][] longLine = Enumerable.Range(0, 100_001).Select(index => new[] { (double)index, 2d }).ToArray();
            Stage coordinateBudget = Create([new("PolylinesVector",
                Collection(Feature(new { type = "LineString", coordinates = longLine }, new { name = "完整长线" })), 1),
                new("PointsVector", Collection(onePoint), 1)]);
            FeatureQueryPreviewResponse coordinateLimited = Preview(coordinateBudget);
            Check("whole_features_not_partially_cut_by_coordinate_budget", coordinateLimited.TotalCoordinateCount == 100_002 &&
                coordinateLimited.ReturnedCoordinateCount == 1 && coordinateLimited.Layers[0].ReturnedFeatureCount == 0 &&
                coordinateLimited.Layers[1].ReturnedFeatureCount == 1 && coordinateLimited.Truncated);

            string largePoint = Feature(new { type = "Point", coordinates = new[] { 10d, 20d } },
                new { text = new string('汉', 60_000) + "\"\\\n" });
            Stage byteBudget = Create([new("PointsVector", Collection(Enumerable.Repeat(largePoint, 24)), 24)]);
            FeatureQueryPreviewResponse bytesLimited = Preview(byteBudget);
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(bytesLimited, BackendJson.Options);
            Check("complete_serialized_response_budget_includes_escaping", bytesLimited.Truncated &&
                bytesLimited.ReturnedFeatureCount > 0 && serialized.Length <= bytesLimited.Budget.MaxPayloadBytes &&
                serialized.Length < 8 * 1024 * 1024);
            await File.WriteAllBytesAsync(Path.Combine(root, "preview-byte-budget.json"), serialized);

            Stage expired = Create([new("PointsVector", Collection(onePoint), 1)], DateTime.UtcNow.AddHours(-25));
            ExpectFailure(() => Preview(expired));
            Check("expired_results_rejected_but_can_be_discarded",
                FeatureQueryService.Discard(new(expired.Id, capture)).Discarded && !Directory.Exists(expired.Directory));

            Stage changed = Create([new("PointsVector", Collection(onePoint), 1)]);
            await File.AppendAllTextAsync(changed.Paths[0], " ");
            ExpectFailure(() => Preview(changed));
            Check("changed_source_sha_rejected", Directory.Exists(changed.Directory));
            FeatureQueryService.Discard(new(changed.Id, capture));

            Stage oversized = Create([new("PointsVector", Collection(onePoint), 1)]);
            using (FileStream sparse = File.OpenWrite(oversized.Paths[0])) sparse.SetLength(128L * 1024 * 1024 + 1);
            ExpectFailure(() => Preview(oversized));
            Check("source_input_budget_rejects_before_parse", Directory.Exists(oversized.Directory));
            FeatureQueryService.Discard(new(oversized.Id, capture));

            Stage linked = Create([new("PointsVector", Collection(onePoint), 1)]);
            string guardPath = Path.Combine(root, "external-owner-guard.txt");
            await File.WriteAllTextAsync(guardPath, linked.Id);
            File.Delete(Path.Combine(linked.Directory, ".owner"));
            File.CreateSymbolicLink(Path.Combine(linked.Directory, ".owner"), guardPath);
            ExpectFailure(() => FeatureQueryService.Discard(new(linked.Id, capture)));
            Check("symbolic_link_owner_not_followed", File.ReadAllText(guardPath) == linked.Id && Directory.Exists(linked.Directory));
            File.Delete(Path.Combine(linked.Directory, ".owner"));
            await File.WriteAllTextAsync(Path.Combine(linked.Directory, ".owner"), linked.Id);

            using (var lease = new FileStream(Path.Combine(Path.GetDirectoryName(small.Directory)!, ".lease-" + small.Id),
                       FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                ExpectFailure(() => FeatureQueryService.Discard(new(small.Id, capture)));
                Check("active_stage_lease_prevents_discard", Directory.Exists(small.Directory));
                JsonElement worker = await RunWorkerAsync("featureQuery.discard", new FeatureQueryDiscardRequest(small.Id, capture));
                Check("stage_lease_excludes_other_worker_process", worker.TryGetProperty("error", out JsonElement lockError) &&
                    lockError.GetProperty("message").GetString()!.Contains("正在预览", StringComparison.Ordinal) &&
                    Directory.Exists(small.Directory));
            }

            JsonElement dispatched = await RunWorkerAsync("featureQuery.preview", new FeatureQueryPreviewRequest(map, small.Id, capture));
            Check("preview_rpc_dispatch_returns_real_geometry", !dispatched.TryGetProperty("error", out _) &&
                dispatched.GetProperty("result").GetProperty("totalFeatureCount").GetInt32() == 3);

            string exportDirectory = Path.Combine(root, "export");
            Directory.CreateDirectory(exportDirectory);
            FeatureQuerySaveResponse saved = await FeatureQueryService.SaveAsync(new(map, featureBudget.Id, "GeoJson", exportDirectory));
            int exportedCount = 0;
            foreach (FeatureQueryExportedSource source in saved.Sources)
            {
                using JsonDocument exported = JsonDocument.Parse(await File.ReadAllBytesAsync(source.Path));
                exportedCount += exported.RootElement.GetProperty("features").GetArrayLength();
            }
            Check("save_exports_all_features_after_preview_truncation", exportedCount == 2_400 && saved.Sources.Length == 2);

            Stage saveChanged = Create([new("PointsVector", Collection(onePoint), 1)]);
            string rollbackDirectory = Path.Combine(root, "rollback");
            Directory.CreateDirectory(rollbackDirectory);
            await ExpectFailureAsync(() => FeatureQueryService.SaveAsync(new(map, saveChanged.Id, "GeoJson", rollbackDirectory),
                progress =>
                {
                    if (progress.Fraction >= 0.9) File.AppendAllText(saveChanged.Paths[0], " ");
                }));
            Check("post_export_source_verification_rolls_back_written_files", !Directory.EnumerateFiles(rollbackDirectory).Any() &&
                Directory.Exists(saveChanged.Directory));

            // The control is sent after the first completed export while the
            // worker still has a substantial second layer to read and write.
            // This observes the real duplex protocol, not a direct service call.
            string manyPoints = Collection(Enumerable.Repeat(onePoint, 100_000));
            Stage cancelSave = Create([new("PointsVector", Collection(onePoint), 1),
                new("PointsVector", manyPoints, 100_000)]);
            string cancelDirectory = Path.Combine(root, "cancel-export");
            Directory.CreateDirectory(cancelDirectory);
            DuplexWorkerResult cancelResult = await RunCancelWorkerAsync("featureQuery.save",
                new FeatureQuerySaveRequest(map, cancelSave.Id, "GeoJson", cancelDirectory), cancelImmediately: false);
            Check("save_worker_cancellation_cleans_written_output", cancelResult.ExitCode == 0 && cancelResult.CancelSent &&
                cancelResult.Terminal.GetProperty("result").GetProperty("state").GetString() == "canceled" &&
                cancelResult.Terminal.GetProperty("result").GetProperty("cleanupComplete").GetBoolean() &&
                !Directory.EnumerateFileSystemEntries(cancelDirectory).Any() && Directory.Exists(cancelSave.Directory));

            if (!OperatingSystem.IsWindows())
            {
                Stage blockedSave = Create([new("PointsVector", Collection(onePoint), 1),
                    new("PointsVector", manyPoints, 100_000)]);
                string blockedDirectory = Path.Combine(root, "cancel-cleanup-blocked");
                Directory.CreateDirectory(blockedDirectory);
                DuplexWorkerResult blockedResult;
                try
                {
                    blockedResult = await RunCancelWorkerAsync("featureQuery.save",
                        new FeatureQuerySaveRequest(map, blockedSave.Id, "GeoJson", blockedDirectory),
                        cancelImmediately: false, beforeCancel: () =>
                        {
                            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(blockedDirectory,
                                UnixFileMode.UserRead | UnixFileMode.UserExecute);
                        });
                }
                finally
                {
                    File.SetUnixFileMode(blockedDirectory,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                Check("save_worker_reports_incomplete_cancellation_cleanup", blockedResult.ExitCode != 0 &&
                    blockedResult.CancelSent && blockedResult.Terminal.TryGetProperty("error", out JsonElement cleanupError) &&
                    cleanupError.GetProperty("message").GetString()!.Contains(blockedDirectory, StringComparison.Ordinal) &&
                    Directory.EnumerateFiles(blockedDirectory).Any() && Directory.Exists(blockedSave.Directory));
            }

            string stageRoot = Path.Combine(Path.GetTempPath(), "HECRAS-FeatureQueryResults");
            var beforeQuery = Directory.EnumerateDirectories(stageRoot).ToHashSet(StringComparer.Ordinal);
            DuplexWorkerResult cancelQuery = await RunCancelWorkerAsync("featureQuery.query",
                new FeatureQueryQueryRequest(map, "OSM", new FeatureQueryExtent(0, 0, 100, 100), null,
                    false, null, "[out:json][timeout:5];node[\"amenity\"]({{bbox}});out geom;", "cancel-test"),
                cancelImmediately: true);
            Check("query_worker_cancellation_cleans_owned_stage", cancelQuery.ExitCode == 0 && cancelQuery.CancelSent &&
                cancelQuery.Terminal.GetProperty("result").GetProperty("state").GetString() == "canceled" &&
                cancelQuery.Terminal.GetProperty("result").GetProperty("cleanupComplete").GetBoolean() &&
                beforeQuery.SetEquals(Directory.EnumerateDirectories(stageRoot)));

            var suggestClock = Stopwatch.StartNew();
            DuplexWorkerResult cancelSuggest = await RunCancelWorkerAsync("featureQuery.suggest",
                new FeatureQuerySuggestionRequest("", "building", true), cancelImmediately: true);
            suggestClock.Stop();
            Check("suggest_worker_control_cancellation_is_bounded", cancelSuggest.ExitCode == 0 && cancelSuggest.CancelSent &&
                cancelSuggest.Terminal.GetProperty("result").GetProperty("state").GetString() == "canceled" &&
                suggestClock.Elapsed < TimeSpan.FromSeconds(15));
            JsonElement noCancelSuggest = await RunWorkerAsync("featureQuery.suggest",
                new FeatureQuerySuggestionRequest("", "", false));
            Check("suggest_without_control_remains_a_normal_result", noCancelSuggest.GetProperty("result")
                .GetProperty("suggestions").GetArrayLength() == 0);

            await File.AppendAllTextAsync(projectPath, "\n");
            ExpectFailure(() => Preview(small));
            FeatureQueryDiscardResponse discarded = FeatureQueryService.Discard(new(small.Id, capture));
            FeatureQueryDiscardResponse again = FeatureQueryService.Discard(new(small.Id, capture));
            Check("discard_uses_original_map_after_source_change_and_is_idempotent", discarded.Discarded &&
                again.AlreadyMissing && !Directory.Exists(small.Directory));
            Check("discard_preserves_other_results", Directory.Exists(linked.Directory) && Directory.Exists(byteBudget.Directory));

            var report = new
            {
                state = "passed", scope = "real-stage-files-and-local-worker; online-provider-query-not-exercised",
                checks, completeResponseBytes = serialized.Length, byteBudget = bytesLimited.Budget,
                previewTotalFeatures = limited.TotalFeatureCount, previewReturnedFeatures = limited.ReturnedFeatureCount,
                exportedFeatureCount = exportedCount
            };
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(report, BackendJson.Options));
            return report;
        }
        finally
        {
            foreach (Stage stage in stages)
                if (Directory.Exists(stage.Directory)) FeatureQueryService.Discard(new(stage.Id, capture));
        }
    }

    private static Stage CreateStage(MapSourceMapCapture map, LayerInput[] layers, DateTime created)
    {
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), "HECRAS-FeatureQueryResults", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".owner"), id);
        var paths = new List<string>();
        var manifestLayers = new List<object>();
        for (int index = 0; index < layers.Length; index++)
        {
            string fileName = $"feature-{index:D2}.geojson";
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, layers[index].GeoJson);
            paths.Add(path);
            manifestLayers.Add(new { fileName, revision = Hash(path), name = "验收图层" + index,
                mapType = layers[index].MapType, featureCount = layers[index].FeatureCount,
                lineColor = "#0080FFFF", fillColor = "#0080FF40", lineWidth = 2 });
        }
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            version = 1, resultId = id, createdUtc = created, map, kind = "OSM",
            optionsFingerprint = new string('a', 64), queryLabel = "acceptance", layers = manifestLayers
        }, BackendJson.Options));
        return new(id, directory, paths.ToArray(), map);
    }

    private static string Feature(object geometry, object properties) =>
        JsonSerializer.Serialize(new { type = "Feature", geometry, properties }, BackendJson.Options);
    private static string Collection(params string[] features) => Collection((IEnumerable<string>)features);
    private static string Collection(IEnumerable<string> features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(',', features) + "]}";
    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or
                                         System.ComponentModel.DataAnnotations.ValidationException) { return; }
        throw new InvalidDataException("Expected a query ownership, revision, lease or budget rejection.");
    }
    private static async Task ExpectFailureAsync(Func<Task<FeatureQuerySaveResponse>> action)
    {
        try { await action(); }
        catch (IOException) { return; }
        throw new InvalidDataException("Expected post-export source verification to reject the changed stage.");
    }

    private static async Task<JsonElement> RunWorkerAsync<T>(string method, T parameters)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing runtime host path.");
        bool isDotnet = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string worker = typeof(BackendWorker).Assembly.Location;
        var start = new ProcessStartInfo(isDotnet ? executable : Path.ChangeExtension(worker, OperatingSystem.IsWindows() ? ".exe" : null))
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        if (isDotnet) start.ArgumentList.Add(worker);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start backend worker.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"), method, parameters
        }, BackendJson.Options));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        string stdout = await output;
        string stderr = await errors;
        if (string.IsNullOrWhiteSpace(stdout)) throw new InvalidOperationException("Worker returned no protocol output: " + stderr);
        using JsonDocument result = JsonDocument.Parse(stdout);
        return result.RootElement.Clone();
    }

    private static async Task<DuplexWorkerResult> RunCancelWorkerAsync<T>(string method, T parameters,
        bool cancelImmediately, Action? beforeCancel = null)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing runtime host path.");
        bool isDotnet = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string worker = typeof(BackendWorker).Assembly.Location;
        var start = new ProcessStartInfo(isDotnet ? executable : Path.ChangeExtension(worker, OperatingSystem.IsWindows() ? ".exe" : null))
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        if (isDotnet) start.ArgumentList.Add(worker);
        start.ArgumentList.Add("--duplex");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start duplex backend worker.");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string id = Guid.NewGuid().ToString("N");
        bool cancelSent = false;
        async Task SendCancelAsync()
        {
            beforeCancel?.Invoke();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                id, controlSequence = 1, control = new { type = "cancel" }
            }, BackendJson.Options));
            await process.StandardInput.FlushAsync();
            cancelSent = true;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                id, method, parameters, duplex = true, streamProgress = true
            }, BackendJson.Options));
            await process.StandardInput.FlushAsync();
            if (cancelImmediately) await SendCancelAsync();
            JsonElement terminal = default;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement frame = document.RootElement;
                if (frame.TryGetProperty("progress", out JsonElement progress))
                {
                    if (!cancelSent && progress.TryGetProperty("fraction", out JsonElement fraction) &&
                        fraction.ValueKind == JsonValueKind.Number && fraction.GetDouble() >= 0.45)
                        await SendCancelAsync();
                    continue;
                }
                terminal = frame.Clone();
                break;
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            string stderr = await errors;
            if (terminal.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("Duplex worker returned no terminal response: " + stderr);
            return new(process.ExitCode, terminal, cancelSent);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
