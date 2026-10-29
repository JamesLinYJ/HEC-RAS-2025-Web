using System.Text;
using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal sealed record ResultGroupLiveAcceptanceReport(string State, string ManifestPath,
    string SourceManifestPath, string ProtectedResultPath, string ProtectedResultRevision,
    int AnchorProfileIndex, int LiveProfileIndex, bool ProtectedSourceOmitted,
    bool PublishedMetadataMapped, bool ForgedRunRejected, bool ProtectedStoredAnchorRejected,
    bool VisualizationAndHdfBytesUnchanged);

internal static partial class ServerAcceptance
{
    public static async Task<ResultGroupLiveAcceptanceReport> RunResultGroupLiveAsync(
        Uri baseAddress, string sourceManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Result-group live acceptance requires a loopback server.", nameof(baseAddress));
        string inputPath = Path.GetFullPath(sourceManifestPath);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(inputPath));
        JsonElement root = document.RootElement;
        RequireResultGroup(StringProperty(root, "schema") == "hecras-result-group-http/v1",
            "A completed Result-group manifest is required.");
        ResultGroupAcceptanceReport source = DeserializeResultGroup<ResultGroupAcceptanceReport>(Property(root, "report"));
        MapTreeRequest map = DeserializeResultGroup<MapTreeRequest>(Property(root, "mapRequest"));
        MapResultSelectionOverride[] selections = DeserializeResultGroup<MapResultSelectionOverride[]>(Property(root, "selections"));
        MapResultSourcesResult previousSources = DeserializeResultGroup<MapResultSourcesResult>(Property(root, "sources"));
        MapResultRenderSharedKeyframeAnchor anchor = DeserializeResultGroup<MapResultRenderSharedKeyframeAnchor>(Property(root, "sharedAnchor"));
        RasterMapViewExtent viewport = DeserializeResultGroup<RasterMapViewExtent>(Property(root, "viewport"));
        string sourceRoot = Path.GetDirectoryName(source.ProjectPath)!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory) ||
            IsPathWithin(outputDirectory, sourceRoot) || IsPathWithin(sourceRoot, outputDirectory))
            throw new IOException("The live acceptance output directory must be new and outside the source fixture.");
        Directory.CreateDirectory(outputDirectory);
        string manifestPath = Path.Combine(outputDirectory, "result-group-live-http-manifest.json");
        string resultPath = source.SecondResultPath;
        string resultRevision = FileSha256(resultPath);
        string visualizationRevision = FileSha256(source.VisualizationPath);
        RequireResultGroup(resultRevision == previousSources.ActiveNodes
            .Single(node => node.Selections.Any(selection =>
                selection.SelectedResult is { } choice && PathEquals(choice.ResultPath, resultPath)))
            .Selections.Single().SelectedResult!.Revision && visualizationRevision == source.FinalRevision,
            "The Result-group source fixture no longer matches its receipt.");
        string[] hdfs = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase)).ToArray();
        var hdfHashes = hdfs.ToDictionary(path => path, FileSha256, StringComparer.Ordinal);
        MapResultActiveNode targetNode = previousSources.ActiveNodes.Single(node => node.NodeKind == "MultiResult" &&
            node.Selections.Any(selection => selection.SelectedResult is { } choice && PathEquals(choice.ResultPath, resultPath)));
        string sessionID = map.ProjectSessionID ?? throw new InvalidDataException("The map has no session identity.");
        string runID = Guid.NewGuid().ToString("D");
        var lease = new MapResultComputeReadLease(sessionID, runID, source.ProjectPath, [resultPath], resultPath);
        MapTreeRequest liveMap = map with
        {
            ComputeReadLease = lease,
            ResultSelectionOverrides = (map.ResultSelectionOverrides ?? [])
                .Where(selection => !PathEquals(selection.SelectedResultPath, resultPath)).ToArray()
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        MapResultRenderResult targetTimeline = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            map, selections, previousSources.ContextToken, targetNode.Node, 0, viewport), cancellation);
        var timeline = new MapResultRenderSharedLiveProfileTimeline(targetNode.Node, 0, runID, resultPath,
            "Base", targetTimeline.Steps.Length, targetTimeline.Steps
                .Select(step => new MapResultRenderSharedKeyframeProfile(step.Id, step.Label, step.Time)).ToArray());
        MapResultSourcesResult liveSources = DeserializeResultGroup<MapResultSourcesResult>(
            (await SubmitOperationAsync(client, "map.resultSources", new
            {
                map = liveMap, selectionOverrides = selections
            }, cancellation)).Result);
        MapResultSelectionState slot = liveSources.ActiveNodes.Single(node => node.Name == targetNode.Name)
            .Selections.Single();
        RequireResultGroup(slot.SelectedResult is null && slot.Choices.All(choice => !PathEquals(choice.ResultPath, resultPath)),
            "The protected Result was exposed as a readable stored source.");
        MapResultRenderRequest liveRequest = new(liveMap, selections, liveSources.ContextToken,
            targetNode.Node, anchor.ProfileIndex, viewport, BlockedSelectionIndices: [0],
            SharedKeyframeAnchor: anchor, SharedLiveProfileTimelines: [timeline]);
        MapResultRenderResult live = await RenderResultGroupAsync(client, liveRequest, cancellation);
        RequireResultGroup(live.SharedLiveProfileMatches?.Single().ProfileIndex == 0 &&
            live.Layers.Length == 0 && live.Sources.Length == 0,
            "The protected timeline did not map A1 to B0 through published metadata.");
        string badRunFailure = await ResultGroupRejectedAsync(client, liveRequest with
        {
            SharedLiveProfileTimelines = [timeline with { RunID = Guid.NewGuid().ToString("D") }]
        }, cancellation, "map.resultRender");
        RequireResultGroup(badRunFailure.Contains("sourceChanged", StringComparison.Ordinal),
            "A forged live run ID was not rejected.");
        MapResultRenderStep first = targetTimeline.Steps.Single(step => step.Id == 0);
        var forgedStored = new MapResultRenderSharedKeyframeAnchor(targetNode.Node, 0, resultPath,
            resultRevision, null, null, targetTimeline.Steps.Length, 0, first.Label, first.Time,
            timeline.Profiles);
        string badStoredFailure = await ResultGroupRejectedAsync(client, liveRequest with
        {
            ProfileIndex = 0, SharedKeyframeAnchor = forgedStored, SharedLiveProfileTimelines = []
        }, cancellation, "map.resultRender");
        RequireResultGroup(badStoredFailure.Contains("sourceChanged", StringComparison.Ordinal),
            "A protected HDF was accepted as a stored keyframe anchor.");
        RequireResultGroup(FileSha256(source.VisualizationPath) == visualizationRevision &&
            hdfHashes.All(item => FileSha256(item.Key) == item.Value), "A read-only live check changed source bytes.");
        var report = new ResultGroupLiveAcceptanceReport("completed", manifestPath, inputPath,
            resultPath, resultRevision, anchor.ProfileIndex, 0, true, true, true, true, true);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schema = "hecras-result-group-live-http/v1", report, runID, liveMap, selections,
            liveSources, sharedAnchor = anchor, timeline, liveMatch = live.SharedLiveProfileMatches,
            badRunFailure, badStoredFailure, visualizationRevision, hdfHashes,
            scope = "actual existing HDF marked protected with synthetic callback metadata; no concurrent writer"
        }, JsonOptions), new UTF8Encoding(false), cancellation);
        return report;
    }
}
