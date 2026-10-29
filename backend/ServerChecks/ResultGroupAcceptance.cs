using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record ResultGroupAcceptanceReport(string State, string ManifestPath,
    string ProjectPath, string VisualizationPath, string InitialRevision, string FinalRevision,
    string FirstResultPath, string SecondResultPath, int AnchorProfileIndex, int TargetProfileIndex,
    bool DynamicFocusedPlans, bool AtomicGroupUpdate, bool IndependentAndHiddenPreserved,
    bool StaleRevisionRejected, bool EmptyGroupsRejected, bool StoredTimeMappingVerified,
    bool ProtectedLiveMetadataVerified, bool SourceHdfBytesUnchanged);

/// <summary>
/// Copies a completed real Result-hover fixture, computes a second Plan with
/// an offset timeline, and exercises the public HTTP map operations. The
/// synthetic live timeline tests lease handling without claiming a live solver.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<ResultGroupAcceptanceReport> RunResultGroupAsync(
        Uri baseAddress, string sourceManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Result-group acceptance requires a loopback server.", nameof(baseAddress));
        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(Path.GetFullPath(sourceManifestPath)), JsonOptions)
            ?? throw new InvalidDataException("The Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || source.Sources.Length < 2 ||
            !File.Exists(source.ProjectPath) || !File.Exists(source.VisualizationPath))
            throw new InvalidDataException("A completed real Result-hover fixture is required.");
        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source.ProjectPath))!;
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory) ||
            IsPathWithin(outputDirectory, sourceRoot) || IsPathWithin(sourceRoot, outputDirectory))
            throw new IOException("The Result-group output must be new and outside the source fixture.");
        Directory.CreateDirectory(outputDirectory);
        string outputRoot = Path.Combine(outputDirectory, "project");
        CopyDirectoryNoOverwrite(sourceRoot, outputRoot);
        string Rebase(string path) => RebaseFixturePath(path, sourceRoot, outputRoot);
        string projectPath = Rebase(source.ProjectPath);
        string visualizationPath = Rebase(source.VisualizationPath);
        string firstResult = Rebase(source.Sources[0].ResultPath);
        string firstRevision = source.Sources[0].Revision;
        RequireResultGroup(FileSha256(firstResult) == firstRevision, "Copied first Result revision differs.");
        string[] sourceHdfs = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourceHashes = sourceHdfs.ToDictionary(path => path, FileSha256, StringComparer.Ordinal);

        const string secondPlanName = "Result group B";
        const string blockedPlanName = "Result group hidden by ancestor";
        const string uncheckedPlanName = "Result group unchecked";
        RasRuntimeBootstrap.Configure();
        string secondPlanPath;
        var project = new Project(projectPath);
        try
        {
            Plan firstPlan = project.Plans.Single(plan => plan.Name == Path.GetFileNameWithoutExtension(source.PlanPath));
            Geometry geometry = project.Geometries.Single();
            BoundaryCondition boundary = project.BoundaryConditions.Single();
            Plan AddPlan(string name)
            {
                Plan plan = project.AddNewPlan(name, geometry, boundary);
                plan.TimeWindowMode = PlanTimeWindowMode.Plan;
                plan.InternalStartTime = firstPlan.InternalStartTime.AddSeconds(1);
                plan.InternalComputeDurationSeconds = firstPlan.InternalComputeDurationSeconds;
                plan.ComputeTimestep = firstPlan.ComputeTimestep;
                plan.BaseOutputInterval = firstPlan.BaseOutputInterval;
                plan.SolverType = SolverType.CPU;
                plan.Save();
                return plan;
            }
            secondPlanPath = Path.GetFullPath(AddPlan(secondPlanName).Filename);
            _ = AddPlan(blockedPlanName);
            _ = AddPlan(uncheckedPlanName);
            project.Save();
        }
        finally { MapResultChartService.DisposeProject(project); }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        RequireResultGroup(StringProperty(health.RootElement, "status") == "ready", "Server is not ready.");
        JsonElement opened = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        string projectXml = StringProperty(opened, "projectXml");
        string projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));
        JsonElement planSnapshot = (await SubmitOperationAsync(client, "model.load", new
        {
            projectPath, projectXml, assetPath = secondPlanPath, kind = "Plan"
        }, cancellation)).Result;
        object model = new
        {
            projectPath, projectXml, assetPath = secondPlanPath, kind = "Plan",
            revision = StringProperty(planSnapshot, "revision"),
            fields = new Dictionary<string, string>(), images = new Dictionary<string, ImageData[]>(),
            sourceState = OptionalString(planSnapshot, "sourceState")
        };
        JsonElement computed = await ComputePlanAsync(client, model, cancellation);
        string secondResult = Path.GetFullPath(StringProperty(computed, "resultPath"));
        string secondRevision = StringProperty(computed, "resultRevision");
        RequireResultGroup(FileSha256(secondResult) == secondRevision, "Second computed Result revision differs.");
        string[] copiedHdfs = Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase)).ToArray();
        var copiedHashes = copiedHdfs.ToDictionary(path => path, FileSha256, StringComparer.Ordinal);
        // Compute may update project metadata; all following requests share its final readback.
        opened = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        projectXml = StringProperty(opened, "projectXml");
        projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string sessionID = Guid.NewGuid().ToString("D");
        string mapID = Guid.NewGuid().ToString("D");
        string currentRevision = FileSha256(visualizationPath);
        MapTreeResultSelectionOverride[] treeSelections =
        [
            new(Rebase(source.PlanPath), Path.GetFileNameWithoutExtension(source.PlanPath), firstResult, firstRevision),
            new(secondPlanPath, secondPlanName, secondResult, secondRevision)
        ];
        MapTreeRequest Map(string revision) => new(projectPath, projectXml, projectXmlSHA, mapID,
            source.Map.MapIndex, source.Map.MapName, source.Map.MapType, revision, treeSelections,
            ProjectSessionID: sessionID);

        JsonElement focus = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "focusPlan", sourcePath = secondPlanPath,
            sourceRevision = FileSha256(secondPlanPath)
        }, cancellation)).Result;
        currentRevision = StringProperty(focus, "visualizationRevision");
        CatalogTreeNode[] focusRoots = DeserializeResultGroupRoots(focus);
        CatalogTreeNode secondGroup = SingleResultGroup(focusRoots, secondPlanName);
        RequireResultGroup(secondGroup.Locator is { RootName: "Focused Plans", NodePath.Length: > 0 },
            "Focused Plan did not produce its dynamic MultiResult row.");
        JsonElement visible = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "visibility", node = secondGroup.Locator, @checked = true
        }, cancellation)).Result;
        currentRevision = StringProperty(visible, "visualizationRevision");

        // The two additional rows are controlled tree-state fixtures: one
        // unchecked group and one checked group below an unchecked Geometry.
        // Their Plans are genuine project assets; neither has a Result HDF.
        XDocument setup = XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace);
        XElement selectedMap = setup.Root!.Element("Maps")!.Elements("Map").ElementAt(source.Map.MapIndex);
        XElement layers = selectedMap.Element("Tree")!.Element("Nodes")!.Elements("Node")
            .Single(node => (string?)node.Attribute("Name") == "Layers");
        XElement standalone = layers.Elements("Node").Single(node => (string?)node.Attribute("Type") == "Result");
        standalone.SetAttributeValue("SelectedMapType", "D*V");
        layers.Add(new XElement("Node", new XAttribute("Type", "Geometry"),
            new XAttribute("Name", projectGeometryName(projectPath)),
            new XElement("Node", new XAttribute("Type", "MultiResult"),
                new XAttribute("Name", blockedPlanName), new XAttribute("IsChecked", true),
                new XAttribute("SelectedMapType", "Water Surface"))));
        layers.Add(new XElement("Node", new XAttribute("Type", "MultiResult"),
            new XAttribute("Name", uncheckedPlanName), new XAttribute("SelectedMapType", "Water Surface")));
        setup.Root.Add(new XElement("ResultGroupAcceptanceUnknown", "preserve"));
        setup.Save(visualizationPath, SaveOptions.DisableFormatting);
        currentRevision = FileSha256(visualizationPath);
        string initialRevision = currentRevision;
        CatalogTreeNode[] roots = await LoadResultGroupTreeAsync(client, Map(currentRevision), cancellation);
        CatalogTreeNode firstGroup = SingleResultGroup(roots, Path.GetFileNameWithoutExtension(source.PlanPath));
        secondGroup = SingleResultGroup(roots, secondPlanName);
        CatalogTreeNode blocked = SingleResultGroup(roots, blockedPlanName);
        CatalogTreeNode uncheckedGroup = SingleResultGroup(roots, uncheckedPlanName);
        CatalogTreeNode ordinary = FlattenMapNodes(roots).Single(node => node.Locator?.NodeType == "Result");
        RequireResultGroup(firstGroup.Checked == "checked" && secondGroup.Checked == "checked" &&
            blocked.Checked == "checked" && uncheckedGroup.Checked == "unchecked" && ordinary.Checked == "checked",
            "The mixed checked/unchecked Result tree did not round-trip.");
        RequireResultGroup(!MapTreeService.IsEffectivelyVisible(roots, blocked),
            "The checked group under an unchecked ancestor is unexpectedly active.");

        XDocument before = XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace);
        XDocument expected = new(before);
        GroupElement(expected, firstGroup.Name).SetAttributeValue("SelectedMapType", "Velocity");
        GroupElement(expected, secondGroup.Name).SetAttributeValue("SelectedMapType", "Velocity");
        JsonElement receipt = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "setResultMapType", name = "Velocity"
        }, cancellation)).Result;
        string velocityRevision = StringProperty(receipt, "visualizationRevision");
        RequireResultGroup(StringProperty(receipt, "previousVisualizationRevision") == currentRevision &&
            velocityRevision != currentRevision && FileSha256(visualizationPath) == velocityRevision &&
            XNode.DeepEquals(expected, XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace)),
            "The group change did not produce one complete, exact .vis transaction/receipt.");
        CatalogTreeNode[] returnedRoots = DeserializeResultGroupRoots(receipt);
        foreach (string name in new[] { firstGroup.Name, secondGroup.Name })
            RequireResultGroup(SingleResultGroup(returnedRoots, name).Properties?.Any(property =>
                property.Id == "Source/SelectedMapType" && property.Value == "Velocity") == true,
                "The mutation receipt omitted a group's selected map type.");
        currentRevision = velocityRevision;
        JsonElement noOp = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "setResultMapType", name = "Velocity"
        }, cancellation)).Result;
        RequireResultGroup(StringProperty(noOp, "visualizationRevision") == currentRevision &&
            StringProperty(noOp, "previousVisualizationRevision") == currentRevision,
            "An unchanged selection should retain its revision.");
        string staleFailure = await ResultGroupRejectedAsync(client, new
        {
            map = Map(initialRevision), action = "setResultMapType", name = "Depth"
        }, cancellation);
        RequireResultGroup(staleFailure.Contains("sourceChanged", StringComparison.Ordinal) &&
            FileSha256(visualizationPath) == currentRevision,
            "A stale revision changed the visualization file.");

        roots = await LoadResultGroupTreeAsync(client, Map(currentRevision), cancellation);
        secondGroup = SingleResultGroup(roots, secondPlanName);
        currentRevision = await SetResultGroupVisibilityAsync(client, Map(currentRevision),
            secondGroup.Locator!, false, cancellation);
        receipt = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "setResultMapType", name = "DepthTimesVelocitySquared"
        }, cancellation)).Result;
        currentRevision = StringProperty(receipt, "visualizationRevision");
        XDocument afterHidden = XDocument.Load(visualizationPath);
        RequireResultGroup((string?)GroupElement(afterHidden, firstGroup.Name).Attribute("SelectedMapType") == "D*V^2" &&
            (string?)GroupElement(afterHidden, secondPlanName).Attribute("SelectedMapType") == "Velocity" &&
            (string?)GroupElement(afterHidden, blockedPlanName).Attribute("SelectedMapType") == "Water Surface" &&
            (string?)GroupElement(afterHidden, uncheckedPlanName).Attribute("SelectedMapType") == "Water Surface" &&
            (string?)afterHidden.Descendants("Node").Single(node => (string?)node.Attribute("Type") == "Result")
                .Attribute("SelectedMapType") == "D*V", "Independent or hidden rows changed.");
        roots = await LoadResultGroupTreeAsync(client, Map(currentRevision), cancellation);
        firstGroup = SingleResultGroup(roots, firstGroup.Name);
        currentRevision = await SetResultGroupVisibilityAsync(client, Map(currentRevision), firstGroup.Locator!, false, cancellation);
        string emptyFailure = await ResultGroupRejectedAsync(client, new
        {
            map = Map(currentRevision), action = "setResultMapType", name = "Depth"
        }, cancellation);
        RequireResultGroup(emptyFailure.Contains("No active MultiResult group", StringComparison.Ordinal) &&
            FileSha256(visualizationPath) == currentRevision, "Empty group rejection or file preservation failed.");

        roots = await LoadResultGroupTreeAsync(client, Map(currentRevision), cancellation);
        firstGroup = SingleResultGroup(roots, firstGroup.Name);
        currentRevision = await SetResultGroupVisibilityAsync(client, Map(currentRevision), firstGroup.Locator!, true, cancellation);
        roots = await LoadResultGroupTreeAsync(client, Map(currentRevision), cancellation);
        secondGroup = SingleResultGroup(roots, secondPlanName);
        currentRevision = await SetResultGroupVisibilityAsync(client, Map(currentRevision), secondGroup.Locator!, true, cancellation);
        receipt = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = Map(currentRevision), action = "setResultMapType", name = "Depth"
        }, cancellation)).Result;
        currentRevision = StringProperty(receipt, "visualizationRevision");
        roots = DeserializeResultGroupRoots(receipt);
        firstGroup = SingleResultGroup(roots, firstGroup.Name);
        secondGroup = SingleResultGroup(roots, secondPlanName);
        ordinary = FlattenMapNodes(roots).Single(node => node.Locator?.NodeType == "Result");
        MapResultSelectionOverride[] selections =
        [
            new(firstGroup.Locator!, 0, "Explicit", firstResult, firstRevision),
            new(secondGroup.Locator!, 0, "Explicit", secondResult, secondRevision),
            new(ordinary.Locator!, 0, "Explicit", firstResult, firstRevision)
        ];
        MapResultSourcesResult sources = DeserializeResultGroup<MapResultSourcesResult>(
            (await SubmitOperationAsync(client, "map.resultSources", new
            {
                map = Map(currentRevision), selectionOverrides = selections
            }, cancellation)).Result);
        RequireResultGroup(sources.ActiveNodes.Count(node => node.NodeKind == "MultiResult") == 2 &&
            sources.ActiveNodes.Single(node => node.NodeKind == "Result").SelectedMapType == "D*V" &&
            sources.ActiveNodes.Where(node => node.NodeKind == "MultiResult").All(node => node.SelectedMapType == "Depth"),
            "Result-source readback did not preserve the active groups and independent Result.");
        var viewport = new RasterMapViewExtent(0, 0, 4, 4, 8, 8);
        MapResultRenderResult anchorRender = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            Map(currentRevision), selections, sources.ContextToken, firstGroup.Locator!, 1, viewport), cancellation);
        RequireResultGroup(anchorRender.Steps.Length >= 2 && anchorRender.Sources.Single().ProfileIndex == 1,
            "The first real HDF anchor did not render profile 1.");
        MapResultRenderSharedKeyframeProfile[] profiles = anchorRender.Steps
            .Select(step => new MapResultRenderSharedKeyframeProfile(step.Id, step.Label, step.Time)).ToArray();
        MapResultRenderStep selected = anchorRender.Steps.Single(step => step.Id == 1);
        var anchor = new MapResultRenderSharedKeyframeAnchor(firstGroup.Locator!, 0, firstResult,
            firstRevision, null, null, profiles.Length, 1, selected.Label, selected.Time, profiles);
        MapResultRenderResult target = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            Map(currentRevision), selections, sources.ContextToken, secondGroup.Locator!, 1, viewport,
            SharedKeyframeAnchor: anchor), cancellation);
        RequireResultGroup(target.SharedKeyframeAnchor?.ProfileIndex == 1 &&
            target.Sources.Single().ProfileIndex == 0 && target.Sources.Single().Available &&
            target.Layers.Single().ProfileIndex == 0 && target.Layers.Single().Time == selected.Time,
            "The second group's real HDF did not map anchor A1 to B0 by time.");
        selected = anchorRender.Steps.Single(step => step.Id == 0);
        anchor = anchor with { ProfileIndex = 0, ProfileName = selected.Label, Time = selected.Time };
        MapResultRenderResult missing = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            Map(currentRevision), selections, sources.ContextToken, secondGroup.Locator!, 0, viewport,
            SharedKeyframeAnchor: anchor), cancellation);
        RequireResultGroup(missing.Sources.Single().ProfileIndex is null && !missing.Sources.Single().Available,
            "An unmatched timestamp must not fall back to the target ordinal.");

        // Exercise the live boundary by marking an actual Result HDF protected.
        // The timeline represents published callback metadata, not a running
        // solver; no claim of concurrent-writer acceptance is made.
        MapResultRenderResult secondTimeline = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            Map(currentRevision), selections, sources.ContextToken, secondGroup.Locator!, 0, viewport), cancellation);
        string callbackPath = secondResult;
        string runID = Guid.NewGuid().ToString("D");
        var lease = new MapResultComputeReadLease(sessionID, runID, projectPath, [callbackPath], callbackPath);
        MapTreeRequest liveMap = Map(currentRevision) with
        {
            ComputeReadLease = lease,
            ResultSelectionOverrides = [treeSelections[0]]
        };
        MapResultSelectionOverride[] liveSelections =
        [
            selections[0],
            new(secondGroup.Locator!, 0, "Explicit", callbackPath, secondRevision),
            selections[2]
        ];
        MapResultSourcesResult liveSources = DeserializeResultGroup<MapResultSourcesResult>(
            (await SubmitOperationAsync(client, "map.resultSources", new
            {
                map = liveMap, selectionOverrides = liveSelections
            }, cancellation)).Result);
        MapResultSelectionState liveSlot = liveSources.ActiveNodes.Single(node => node.Name == secondPlanName)
            .Selections.Single();
        RequireResultGroup(liveSlot.SelectedResult is null &&
            liveSlot.Choices.All(choice => !PathEquals(choice.ResultPath, callbackPath)),
            "Protected callback Result became a readable stored source.");
        selected = anchorRender.Steps.Single(step => step.Id == 1);
        anchor = anchor with { ProfileIndex = 1, ProfileName = selected.Label, Time = selected.Time };
        var timeline = new MapResultRenderSharedLiveProfileTimeline(secondGroup.Locator!, 0, runID,
            callbackPath, "Base", secondTimeline.Steps.Length, secondTimeline.Steps
                .Select(step => new MapResultRenderSharedKeyframeProfile(step.Id, step.Label, step.Time)).ToArray());
        MapResultRenderResult live = await RenderResultGroupAsync(client, new MapResultRenderRequest(
            liveMap, liveSelections, liveSources.ContextToken, secondGroup.Locator!, 1, viewport,
            BlockedSelectionIndices: [0], SharedKeyframeAnchor: anchor,
            SharedLiveProfileTimelines: [timeline]), cancellation);
        RequireResultGroup(live.SharedLiveProfileMatches?.Single().ProfileIndex == 0 && live.Layers.Length == 0 &&
            live.Sources.Length == 0 && File.Exists(callbackPath), "Protected live metadata mapping failed.");
        string invalidLiveFailure = await ResultGroupRejectedAsync(client, new MapResultRenderRequest(
            liveMap, liveSelections, liveSources.ContextToken, secondGroup.Locator!, 1, viewport,
            BlockedSelectionIndices: [0], SharedKeyframeAnchor: anchor,
            SharedLiveProfileTimelines: [timeline with { RunID = Guid.NewGuid().ToString("D") }]),
            cancellation, "map.resultRender");
        RequireResultGroup(invalidLiveFailure.Contains("sourceChanged", StringComparison.Ordinal),
            "A mismatched live run identity was not rejected.");

        RequireResultGroup(FileSha256(firstResult) == firstRevision && FileSha256(secondResult) == secondRevision &&
            sourceHashes.All(item => FileSha256(item.Key) == item.Value) &&
            copiedHashes.All(item => FileSha256(item.Key) == item.Value), "A source HDF changed.");
        string manifestPath = Path.Combine(outputDirectory, "result-group-http-manifest.json");
        var report = new ResultGroupAcceptanceReport("completed", manifestPath, projectPath, visualizationPath,
            initialRevision, currentRevision, firstResult, secondResult, 1, 0, true, true, true, true,
            true, true, true, true);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schema = "hecras-result-group-http/v1", report, mapRequest = Map(currentRevision),
            selections, sources, viewport, sharedAnchor = anchor, targetProfileIndex = 0,
            staleFailure, emptyFailure, invalidLiveFailure, sourceHashes, copiedHashes,
            liveBoundary = "actual HDF marked protected with synthetic callback metadata; no concurrent writer was started"
        }, JsonOptions), new UTF8Encoding(false), cancellation);
        return report;

        static string projectGeometryName(string path)
        {
            var copiedProject = new Project(path);
            try { return copiedProject.Geometries.Single().Name; }
            finally { MapResultChartService.DisposeProject(copiedProject); }
        }
    }

    private static async Task<CatalogTreeNode[]> LoadResultGroupTreeAsync(HttpClient client,
        MapTreeRequest map, CancellationToken cancellation) => DeserializeResultGroupRoots(
            (await SubmitOperationAsync(client, "map.tree.load", map, cancellation)).Result);

    private static CatalogTreeNode[] DeserializeResultGroupRoots(JsonElement value) =>
        DeserializeResultGroup<CatalogTreeNode[]>(Property(value, "roots"));

    private static T DeserializeResultGroup<T>(JsonElement value) =>
        JsonSerializer.Deserialize<T>(value.GetRawText(), JsonOptions)
        ?? throw new InvalidDataException("A Result-group HTTP payload was empty.");

    private static CatalogTreeNode SingleResultGroup(CatalogTreeNode[] roots, string name) =>
        FlattenMapNodes(roots).Single(node => node.Locator?.NodeType == "MultiResult" && node.Name == name);

    private static XElement GroupElement(XDocument document, string name) =>
        document.Descendants("Node").Single(node => (string?)node.Attribute("Type") == "MultiResult" &&
            (string?)node.Attribute("Name") == name);

    private static async Task<string> SetResultGroupVisibilityAsync(HttpClient client, MapTreeRequest map,
        CatalogTreeNodeLocator node, bool isChecked, CancellationToken cancellation) => StringProperty(
            (await SubmitOperationAsync(client, "map.tree.perform", new
            {
                map, action = "visibility", node, @checked = isChecked
            }, cancellation)).Result, "visualizationRevision");

    private static async Task<string> ResultGroupRejectedAsync(HttpClient client, object parameters,
        CancellationToken cancellation, string method = "map.tree.perform")
    {
        try { _ = await SubmitOperationAsync(client, method, parameters, cancellation); }
        catch (InvalidOperationException error) { return error.Message; }
        throw new InvalidDataException("A Result-group operation that should fail was accepted.");
    }

    private static async Task<MapResultRenderResult> RenderResultGroupAsync(HttpClient client,
        MapResultRenderRequest request, CancellationToken cancellation) => DeserializeResultGroup<MapResultRenderResult>(
            (await SubmitOperationAsync(client, "map.resultRender", request, cancellation)).Result);

    private static void RequireResultGroup(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
