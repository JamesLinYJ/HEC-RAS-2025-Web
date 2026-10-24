using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers;
using Ras.Observables;
using Utility.Logging;

namespace Ras.Backend;

/// <summary>
/// Resolves the selected Result contexts from one retained source map tree and
/// samples their Result flow series. Result selection itself is session state;
/// the source .vis file is read but never changed by these operations.
/// </summary>
public static class MapResultChartService
{
    private const int MaximumOverrides = 256;
    private const int MaximumSelectionSlots = 32;
    private const int MaximumSampleLinePoints = 10_000;
    private const long MaximumProjectXmlBytes = 8L * 1024 * 1024;
    private const string CoordinateHashDomain = "HECRAS-MAP-LINE-v1\0";
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static MapResultSourcesResult Sources(MapResultSourcesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using ResolvedMapResultContext context = ResolveContext(request.Map, request.SelectionOverrides);
        context.EnsureUnchanged();
        return context.Sources;
    }

    public static MapResultSourcesResult Selection(MapResultSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExpectedContextToken))
            throw new ArgumentException("Refresh the selected map results before changing a selection.", nameof(request));

        using (ResolvedMapResultContext current = ResolveContext(request.Map, request.CurrentSelectionOverrides,
                   request.ExpectedContextToken))
            current.EnsureUnchanged();

        using ResolvedMapResultContext updated = ResolveContext(request.Map, request.SelectionOverrides);
        updated.EnsureUnchanged();
        return updated.Sources;
    }

    public static async Task<MapResultSampleLineResult> SampleLine(MapResultSampleLineRequest request,
        Action<BackendOperationProgress>? report = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Feature);
        string mode = request.Mode switch
        {
            "Flow" => "Flow",
            "CumulativeVolume" => "CumulativeVolume",
            _ => throw new ArgumentException("Choose Flow or Cumulative Volume for the map sample-line chart.", nameof(request.Mode))
        };
        GeometryTopologyCoordinate[] coordinates = ValidateSampleLine(request.SampleLine);

        using ResolvedMapResultContext context = ResolveContext(request.Map, request.SelectionOverrides,
            request.ContextToken);
        string vectorPath = ValidateFeatureIdentity(context, request.Feature, coordinates);
        context.EnsureUnchanged();

        var line = new Polyline();
        foreach (GeometryTopologyCoordinate coordinate in coordinates)
            line.Add(new Point(coordinate.X, coordinate.Y));

        var series = new List<MapResultSampleLineSeries>(context.ResultContexts.Count);
        foreach (ResolvedMapResultNode resultContext in context.ResultContexts)
        {
            EnsureUnchanged(resultContext.ResultPath, resultContext.Revision);
            ResultProfileSampleLineSeries computed = await ResultProfileSampleLineService.ComputeOneAsync(
                resultContext.Result, resultContext.ResultPath, resultContext.Revision, line, mode, report);
            EnsureUnchanged(resultContext.ResultPath, resultContext.Revision);
            series.Add(new MapResultSampleLineSeries(resultContext.Selections,
                resultContext.SelectedMapType, resultContext.ProfileIndex, computed));
        }

        context.EnsureUnchanged();
        EnsureUnchanged(vectorPath, request.Feature.VectorRevision);
        string featureToken = FeatureContextToken(context.Sources.ContextToken, vectorPath,
            request.Feature.FeatureID, request.Feature.VectorRevision, request.Feature.CoordinateSHA, mode);
        MapResultSourcesResult sources = context.Sources;
        return new MapResultSampleLineResult(sources.ProjectPath, sources.ProjectRevision,
            sources.ProjectXmlSHA, sources.VisualizationRevision, sources.MapID, sources.MapIndex,
            sources.MapName, sources.MapType, sources.ContextToken, featureToken,
            request.Feature with { VectorPath = vectorPath, VectorRevision = request.Feature.VectorRevision.ToUpperInvariant(),
                CoordinateSHA = request.Feature.CoordinateSHA.ToUpperInvariant() }, mode, coordinates,
            series.ToArray(), BuildSideEffects(context.ResultContexts));
    }

    /// <summary>
    /// Shared same-assembly resolver for render/catalog services. The returned
    /// context owns its Project and Result resources and must be disposed.
    /// </summary>
    internal static ResolvedMapResultContext ResolveContext(MapTreeRequest map,
        MapResultSelectionOverride[]? selectionOverrides, string? expectedContextToken = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        string projectPath = ValidateMapRequest(map);
        string projectRevision = Revision(projectPath);
        string projectXmlSHA = ValidateProjectXml(map);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath))
            throw new FileNotFoundException("The selected map visualization file was not found.", visualizationPath);
        string visualizationRevision = Revision(visualizationPath);
        if (!string.Equals(map.ExpectedVisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(map, projectPath);
        MapTreeRequest sourceMap = map with
        {
            ProjectPath = projectPath,
            ComputeReadLease = readLease?.Lease
        };
        // Project loading opens Result HDFs before SavedProjectResults can
        // inspect them. Capture identities first, excluding exact files held
        // by the active compute writer, so persisted summary and diagnostic
        // data bind only to files the Project was allowed to load.
        ResultSourceReadContext resultSourceReads = ResultSourceReadContext.Capture(projectPath,
            readLease?.ProtectedSourcePaths, map.CoreLayerContext?.SessionHdfReferences);
        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectPath,
            ParseProjectXml(map.ProjectXml!), map.CoreLayerContext,
            sourcePath => readLease?.IsProtected(sourcePath) != true);
        bool keepProject = false;
        try
        {
            MapTreeSnapshot tree = MapTreeService.Load(sourceMap);
            if (!string.Equals(tree.VisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            var planResultLookup = new ProjectPlanResultLookup(project, map.CoreLayerContext);
            StringComparer sourcePathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> protectedSourcePaths = (readLease?.ProtectedSourcePaths ?? [])
                .ToHashSet(sourcePathComparer);
            MapTreeResultSelectionOverride[] selectionTreeOverrides = ResolveTreeSelectionOverrides(
                planResultLookup, tree, selectionOverrides, resultSourceReads, protectedSourcePaths);
            MapTreeResultSelectionOverride[] mergedTreeOverrides = MergeTreeSelectionOverrides(
                sourceMap.ResultSelectionOverrides, selectionTreeOverrides);
            if (!SameTreeSelectionOverrides(sourceMap.ResultSelectionOverrides, mergedTreeOverrides))
            {
                sourceMap = sourceMap with { ResultSelectionOverrides = mergedTreeOverrides };
                tree = MapTreeService.Load(sourceMap);
                if (!string.Equals(tree.VisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
            }

            MapResultSelectionOverride[] effectiveSelectionOverrides = IncludeTreeSelectionsInChart(
                tree, mergedTreeOverrides, selectionOverrides);
            XElement serializedMap = LoadSerializedMap(visualizationPath, map.MapIndex, map.MapName, map.MapType);
            MapResultActiveNode[] activeNodes = BuildActiveNodes(project, planResultLookup, tree.Roots, serializedMap,
                effectiveSelectionOverrides, resultSourceReads, protectedSourcePaths,
                out List<ResolvedMapResultNode> resultContexts);
            string contextToken = ContextToken(projectPath, projectRevision, projectXmlSHA,
                visualizationRevision, sourceMap, activeNodes);
            var sources = new MapResultSourcesResult(projectPath, projectRevision, projectXmlSHA,
                visualizationRevision, map.MapID, map.MapIndex, map.MapName, map.MapType,
                contextToken, activeNodes, tree.Roots, sourceMap.ResultSelectionOverrides ?? []);
            var resolved = new ResolvedMapResultContext(sourceMap, projectPath, projectRevision,
                projectXmlSHA, visualizationRevision, visualizationPath, tree, project, sources,
                resultContexts);
            resolved.EnsureUnchanged();
            if (expectedContextToken is not null &&
                !string.Equals(expectedContextToken, contextToken, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            keepProject = true;
            return resolved;
        }
        finally
        {
            if (!keepProject) DisposeProject(project);
        }
    }

    private static MapTreeResultSelectionOverride[] ResolveTreeSelectionOverrides(ProjectPlanResultLookup planResultLookup,
        MapTreeSnapshot tree, MapResultSelectionOverride[]? selectionOverrides,
        ResultSourceReadContext resultSourceReads, IReadOnlySet<string> protectedSourcePaths)
    {
        var resolved = new List<MapTreeResultSelectionOverride>();
        foreach (IGrouping<string, MapResultSelectionOverride> group in (selectionOverrides ?? [])
                     .Where(item => item.Node.NodeType == "MultiResult" && item.SelectionIndex == 0)
                     .GroupBy(item => LocatorKey(item.Node), StringComparer.Ordinal))
        {
            MapResultSelectionOverride requested = group.Single();
            CatalogTreeNode[] matches = Flatten(tree.Roots).Where(node => node.Locator is { } locator &&
                SameLocator(locator, requested.Node)).ToArray();
            if (matches.Length != 1 || matches[0].Locator is not { } nodeLocator ||
                nodeLocator.NodeType != "MultiResult" ||
                matches[0].Checked is not ("checked" or "unchecked"))
                throw new SourceFileChangedException();
            if (requested.Mode == "Explicit" && requested.ResultPath is { } protectedPath &&
                protectedSourcePaths.Contains(SourceFilePaths.Canonical(protectedPath)))
                continue;
            string planAssetID;
            HashSet<Result> matchedResults;
            SessionHdfResultOwner? sessionOwner = nodeLocator.SessionHdfResultOwner;
            if (sessionOwner is not null)
            {
                if (nodeLocator.AssetID is not null || matches[0].AssetID is not null ||
                    !string.Equals(nodeLocator.NodeName, sessionOwner.EmbeddedPlanName,
                        StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                planResultLookup.ValidateSessionHdfResultOwner(sessionOwner);
                planAssetID = "";
                matchedResults = planResultLookup.MatchedResultsForSessionHdfResultGroup(sessionOwner);
            }
            else
            {
                planAssetID = nodeLocator.AssetID ?? matches[0].AssetID
                    ?? throw new SourceFileChangedException();
                matchedResults = planResultLookup.MatchedResultsForProjectPlan(planAssetID,
                    nodeLocator.NodeName);
            }
            ResultChoice[] candidates = SavedProjectResults(planResultLookup, matchedResults, resultSourceReads,
                new HashSet<string>(StringComparer.Ordinal));
            ResultChoice? selected = ResolveSelection(candidates, requested.Mode,
                requested.ResultPath, requested.ResultRevision, out string normalizedMode);
            if (selected is null) continue;
            if (sessionOwner is not null && normalizedMode == "Latest")
            {
                // Latest is a logical selection for a session-owned external
                // Result group. Do not turn its current choice into a pinned
                // tree selection: a subsequent import must follow the group's
                // actual ResultEndTime ordering.
                resolved.Add(new MapTreeResultSelectionOverride("", nodeLocator.NodeName,
                    "", "", sessionOwner, FollowLatest: true));
                continue;
            }
            resolved.Add(new MapTreeResultSelectionOverride(planAssetID, nodeLocator.NodeName,
                selected.Choice.ResultPath, selected.Choice.Revision, sessionOwner));
        }
        return resolved.ToArray();
    }

    private static MapTreeResultSelectionOverride[] MergeTreeSelectionOverrides(
        MapTreeResultSelectionOverride[]? existing,
        MapTreeResultSelectionOverride[] fromChart)
    {
        var merged = new Dictionary<string, MapTreeResultSelectionOverride>(StringComparer.Ordinal);
        foreach (MapTreeResultSelectionOverride selection in existing ?? [])
        {
            if (!IsValidTreeResultSelection(selection) ||
                !merged.TryAdd(TreeResultKey(selection), selection))
                throw new SourceFileChangedException();
        }
        foreach (MapTreeResultSelectionOverride selection in fromChart)
        {
            if (!IsValidTreeResultSelection(selection))
                throw new SourceFileChangedException();
            string key = TreeResultKey(selection);
            if (merged.TryGetValue(key, out MapTreeResultSelectionOverride? previous))
            {
                if (!SessionHdfResultOwnerEquals(previous.SessionHdfResultOwner,
                        selection.SessionHdfResultOwner))
                    throw new SourceFileChangedException();

                bool logicalLatestTransition = previous.FollowLatest || selection.FollowLatest;
                if (logicalLatestTransition)
                {
                    // Only the same validated external Result owner can change
                    // between the logical Latest state and one exact selected
                    // file. Project-owned selections retain their old exact
                    // conflict guard.
                    if (previous.SessionHdfResultOwner is null ||
                        selection.SessionHdfResultOwner is null)
                        throw new SourceFileChangedException();
                }
                else if (!SameExactTreeResultSelection(previous, selection))
                {
                    // Two different exact selections for the same owner are a
                    // stale/conflicting capture, not a last-writer-wins merge.
                    throw new SourceFileChangedException();
                }
            }
            merged[key] = selection;
        }
        return merged.Values.OrderBy(TreeResultKey,
            StringComparer.Ordinal).ToArray();
    }

    private static bool SameTreeSelectionOverrides(MapTreeResultSelectionOverride[]? left,
        MapTreeResultSelectionOverride[]? right)
    {
        MapTreeResultSelectionOverride[] a = left ?? [];
        MapTreeResultSelectionOverride[] b = right ?? [];
        return a.Length == b.Length && a.All(item => b.Any(other =>
            string.Equals(TreeResultKey(item), TreeResultKey(other), StringComparison.Ordinal) &&
            SessionHdfResultOwnerEquals(item.SessionHdfResultOwner, other.SessionHdfResultOwner) &&
            item.FollowLatest == other.FollowLatest &&
            (item.FollowLatest
                ? IsFollowLatestTreeResultSelection(item) && IsFollowLatestTreeResultSelection(other)
                : SameExactTreeResultSelection(item, other))));
    }

    private static bool IsValidTreeResultSelection(MapTreeResultSelectionOverride selection) =>
        selection.FollowLatest
            ? IsFollowLatestTreeResultSelection(selection)
            : HasExactTreeResultSelection(selection);

    private static bool IsFollowLatestTreeResultSelection(MapTreeResultSelectionOverride selection) =>
        selection.SessionHdfResultOwner is not null &&
        string.IsNullOrEmpty(selection.SelectedResultPath) &&
        string.IsNullOrEmpty(selection.SelectedResultRevision);

    private static bool HasExactTreeResultSelection(MapTreeResultSelectionOverride selection) =>
        !string.IsNullOrWhiteSpace(selection.SelectedResultPath) &&
        !string.IsNullOrWhiteSpace(selection.SelectedResultRevision);

    private static bool SameExactTreeResultSelection(MapTreeResultSelectionOverride left,
        MapTreeResultSelectionOverride right) =>
        HasExactTreeResultSelection(left) && HasExactTreeResultSelection(right) &&
        SourceFilePaths.SameSourceFile(left.SelectedResultPath, right.SelectedResultPath) &&
        string.Equals(left.SelectedResultRevision, right.SelectedResultRevision,
            StringComparison.OrdinalIgnoreCase);

    private static MapResultSelectionOverride[] IncludeTreeSelectionsInChart(
        MapTreeSnapshot tree, MapTreeResultSelectionOverride[] treeSelections,
        MapResultSelectionOverride[]? chartSelections)
    {
        var result = (chartSelections ?? []).ToList();
        foreach (MapTreeResultSelectionOverride selection in treeSelections)
        {
            CatalogTreeNode[] matches = Flatten(tree.Roots).Where(node =>
                node.Locator is { NodeType: "MultiResult" } locator &&
                SameTreeSelectionOwner(locator, selection) &&
                string.Equals(node.Name, selection.PlanName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || matches[0].Locator is not { } locator ||
                matches[0].Checked is not ("checked" or "unchecked"))
                throw new SourceFileChangedException();
            // Keep hidden source-tree Result selections in the tree request so
            // the exact owner and revision remain resolvable. They are not
            // chart selections and must never enter the renderable ActiveNodes.
            MapResultSelectionOverride[] existingSelections = result.Where(item =>
                item.SelectionIndex == 0 && SameLocator(item.Node, locator)).ToArray();
            if (matches[0].Checked == "unchecked")
            {
                if (existingSelections.Length > 1)
                    throw new SourceFileChangedException();
                if (existingSelections.FirstOrDefault() is { } hiddenSelection)
                {
                    bool exactExplicitSelection = !selection.FollowLatest &&
                        hiddenSelection.Mode == "Explicit" &&
                        hiddenSelection.ResultPath is { Length: > 0 } resultPath &&
                        HasExactTreeResultSelection(selection) &&
                        SourceFilePaths.SameSourceFile(resultPath, selection.SelectedResultPath) &&
                        string.Equals(hiddenSelection.ResultRevision, selection.SelectedResultRevision,
                            StringComparison.OrdinalIgnoreCase);
                    bool resolvedLogicalSelection = hiddenSelection.Mode is "Latest" or "Previous" &&
                        hiddenSelection.ResultPath is null && hiddenSelection.ResultRevision is null;
                    bool validSessionSelection = selection.SessionHdfResultOwner is not null &&
                        ChartSelectionMatchesTreeSelection(hiddenSelection, selection, matches[0]);
                    if (selection.SessionHdfResultOwner is not null
                        ? !validSessionSelection
                        : !exactExplicitSelection && !resolvedLogicalSelection)
                        throw new SourceFileChangedException();
                    // A hidden selection is represented by the exact source-tree
                    // override above, never by the chart's active-node overrides.
                    result.Remove(hiddenSelection);
                }
                continue;
            }
            MapResultSelectionOverride? existing = existingSelections.FirstOrDefault();
            if (existing is null)
            {
                result.Add(selection.FollowLatest
                    ? new MapResultSelectionOverride(locator, 0, "Latest")
                    : new MapResultSelectionOverride(locator, 0, "Explicit",
                        selection.SelectedResultPath, selection.SelectedResultRevision));
                continue;
            }
            if (selection.SessionHdfResultOwner is not null)
            {
                if (!ChartSelectionMatchesTreeSelection(existing, selection, matches[0]))
                    throw new SourceFileChangedException();
                if (selection.FollowLatest && existing.Mode == "Explicit")
                {
                    // An Explicit request that resolved to the current latest
                    // option has already been normalized by ResolveSelection.
                    // Carry that logical Latest forward without pinning its path.
                    result.Remove(existing);
                    result.Add(new MapResultSelectionOverride(locator, 0, "Latest"));
                }
            }
        }
        return result.ToArray();
    }

    private static bool ChartSelectionMatchesTreeSelection(MapResultSelectionOverride chartSelection,
        MapTreeResultSelectionOverride treeSelection, CatalogTreeNode treeNode)
    {
        if (treeSelection.FollowLatest)
        {
            if (chartSelection.Mode == "Latest" && chartSelection.ResultPath is null &&
                chartSelection.ResultRevision is null)
                return true;
            return chartSelection.Mode == "Explicit" &&
                chartSelection.ResultPath is { Length: > 0 } explicitPath &&
                chartSelection.ResultRevision is { Length: > 0 } explicitRevision &&
                treeNode.SelectedResultAssetID is { Length: > 0 } latestPath &&
                treeNode.SelectedResultRevision is { Length: > 0 } latestRevision &&
                SourceFilePaths.SameSourceFile(explicitPath, latestPath) &&
                string.Equals(explicitRevision, latestRevision, StringComparison.OrdinalIgnoreCase);
        }

        if (!HasExactTreeResultSelection(treeSelection)) return false;
        if (chartSelection.Mode == "Explicit" && chartSelection.ResultPath is { Length: > 0 } exactPath &&
            chartSelection.ResultRevision is { Length: > 0 } exactRevision)
            return SourceFilePaths.SameSourceFile(exactPath, treeSelection.SelectedResultPath) &&
                string.Equals(exactRevision, treeSelection.SelectedResultRevision,
                    StringComparison.OrdinalIgnoreCase);
        if (chartSelection.Mode is "Latest" or "Previous" && chartSelection.ResultPath is null &&
            chartSelection.ResultRevision is null)
            return treeNode.SelectedResultAssetID is { Length: > 0 } selectedPath &&
                treeNode.SelectedResultRevision is { Length: > 0 } selectedRevision &&
                SourceFilePaths.SameSourceFile(selectedPath, treeSelection.SelectedResultPath) &&
                string.Equals(selectedRevision, treeSelection.SelectedResultRevision,
                    StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool SameTreeSelectionOwner(CatalogTreeNodeLocator locator,
        MapTreeResultSelectionOverride selection)
    {
        if (selection.SessionHdfResultOwner is { } owner)
            return locator.AssetID is null &&
                SessionHdfResultOwnerEquals(locator.SessionHdfResultOwner, owner);
        return selection.PlanAssetID is { Length: > 0 } planAsset &&
            locator.AssetID is { Length: > 0 } locatorPlan &&
            SourceFilePaths.SameSourceFile(locatorPlan, planAsset) &&
            locator.SessionHdfResultOwner is null;
    }

    private static bool SessionHdfResultOwnerEquals(SessionHdfResultOwner? left,
        SessionHdfResultOwner? right) => left is null ? right is null : right is not null &&
        string.Equals(left.ReferenceID, right.ReferenceID, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ProjectSessionID, right.ProjectSessionID, StringComparison.Ordinal) &&
        SourceFilePaths.SameSourceFile(left.ProjectPath, right.ProjectPath) &&
        SourceFilePaths.SameSourceFile(left.ResultPath, right.ResultPath) &&
        string.Equals(left.ResultRevision, right.ResultRevision, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.EmbeddedPlanName, right.EmbeddedPlanName, StringComparison.Ordinal) &&
        string.Equals(left.CoreLayerContextIdentityToken, right.CoreLayerContextIdentityToken,
            StringComparison.OrdinalIgnoreCase);

    private static string TreeResultKey(MapTreeResultSelectionOverride selection) =>
        selection.SessionHdfResultOwner is { } owner
            ? "session-hdf-result\n" + owner.ReferenceID + "\n" + owner.EmbeddedPlanName
            : TreeResultKey(selection.PlanAssetID, selection.PlanName);

    private static string TreeResultKey(string planAssetID, string planName)
    {
        string canonical = SourceFilePaths.Canonical(planAssetID);
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return canonical + "\n" + planName;
    }

    private static string ProjectPlanAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return SourceFilePaths.Canonical(path);
    }

    internal static void EnsureUnchanged(string path, string revision)
    {
        if (!File.Exists(path) || !string.Equals(Revision(path), revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    internal static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private static string ValidateMapRequest(MapTreeRequest map)
    {
        if (string.IsNullOrWhiteSpace(map.ProjectPath) || !Path.IsPathFullyQualified(map.ProjectPath))
            throw new ArgumentException("Open a saved project before reading map results.", nameof(map.ProjectPath));
        if (string.IsNullOrWhiteSpace(map.MapID) || map.MapIndex < 0 ||
            string.IsNullOrWhiteSpace(map.MapName) || string.IsNullOrWhiteSpace(map.MapType))
            throw new ArgumentException("The selected map identity is incomplete.", nameof(map));
        if (string.IsNullOrWhiteSpace(map.ExpectedVisualizationRevision))
            throw new ArgumentException("Refresh the map tree to obtain its visualization revision.", nameof(map));
        string sourcePath = Path.GetFullPath(map.ProjectPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected project file was not found.", sourcePath);
        return sourcePath;
    }

    private static string ValidateProjectXml(MapTreeRequest map)
    {
        if (string.IsNullOrWhiteSpace(map.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.", nameof(map.ProjectXml));
        if (Encoding.UTF8.GetByteCount(map.ProjectXml) > MaximumProjectXmlBytes)
            throw new ArgumentException("The project XML context is too large.", nameof(map.ProjectXml));
        string revision = Hash(Encoding.UTF8.GetBytes(map.ProjectXml));
        if (string.IsNullOrWhiteSpace(map.ProjectXmlSHA) ||
            !string.Equals(map.ProjectXmlSHA, revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return revision;
    }

    private static XElement ParseProjectXml(string projectXml)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(projectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumProjectXmlBytes
        });
        return XElement.Load(reader, LoadOptions.None);
    }

    private static XElement LoadSerializedMap(string visualizationPath, int mapIndex,
        string mapName, string mapType)
    {
        using XmlReader reader = XmlReader.Create(visualizationPath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 32L * 1024 * 1024
        });
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        XElement[] maps = document.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (mapIndex >= maps.Length) throw new SourceFileChangedException();
        XElement selected = maps[mapIndex];
        if ((string?)selected.Attribute("Name") != mapName || (string?)selected.Attribute("Type") != mapType)
            throw new SourceFileChangedException();
        return selected;
    }

    private static MapResultActiveNode[] BuildActiveNodes(Project project, ProjectPlanResultLookup planResultLookup,
        CatalogTreeNode[] roots,
        XElement serializedMap, MapResultSelectionOverride[]? requestedOverrides,
        ResultSourceReadContext resultSourceReads,
        IReadOnlySet<string> protectedSourcePaths,
        out List<ResolvedMapResultNode> resolvedResults)
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        List<(CatalogTreeNode Node, XElement SourceNode)> active = [];
        var protectedResultMarkers = new HashSet<string>(pathComparer);
        Visit(roots);
        var byLocator = active.ToDictionary(item => LocatorKey(item.Node.Locator!), item => item,
            StringComparer.Ordinal);
        MapResultSelectionOverride[] overrides = ValidateOverrides(requestedOverrides, byLocator);
        var activePlanResults = new Dictionary<string, HashSet<Result>>(StringComparer.Ordinal);
        foreach ((CatalogTreeNode node, _) in active.Where(item => item.Node.Locator?.NodeType == "MultiResult"))
        {
            CatalogTreeNodeLocator locator = node.Locator!;
            if (locator.SessionHdfResultOwner is { } sessionOwner)
            {
                if (locator.AssetID is not null || node.AssetID is not null)
                    throw new SourceFileChangedException();
                planResultLookup.ValidateSessionHdfResultOwner(sessionOwner);
                activePlanResults.Add(LocatorKey(locator),
                    planResultLookup.MatchedResultsForSessionHdfResultGroup(sessionOwner));
            }
            else
            {
                string planAssetID = locator.AssetID ?? node.AssetID ?? throw new SourceFileChangedException();
                activePlanResults.Add(LocatorKey(locator),
                    planResultLookup.MatchedResultsForProjectPlan(planAssetID, locator.NodeName));
            }
        }
        var matchedPlanResults = new HashSet<Result>(ReferenceEqualityComparer.Instance);
        foreach (HashSet<Result> planResults in activePlanResults.Values)
            matchedPlanResults.UnionWith(planResults);
        HashSet<string> standaloneResultPaths = active
            .Where(item => item.Node.Locator?.NodeType == "Result")
            .Select(item => item.Node.Locator?.AssetID ?? item.Node.AssetID)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => SourceFilePaths.Canonical(path!))
            .ToHashSet(pathComparer);
        var resultCandidates = SavedProjectResults(planResultLookup, matchedPlanResults,
            resultSourceReads, standaloneResultPaths);
        var activeNodes = new List<MapResultActiveNode>(active.Count);
        resolvedResults = [];

        foreach ((CatalogTreeNode node, XElement sourceNode) in active)
        {
            CatalogTreeNodeLocator locator = node.Locator!;
            string kind = locator.NodeType;
            string? selectedMapType = (string?)sourceNode.Attribute("SelectedMapType") ?? "Depth";
            int? profileIndex = int.TryParse((string?)sourceNode.Attribute("ProfileIndex"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedProfile) ? parsedProfile : 0;

            if (kind == "MultiResult")
            {
                if (!activePlanResults.TryGetValue(LocatorKey(locator), out HashSet<Result>? planResults))
                    throw new SourceFileChangedException();
                ResultChoice[] candidates = resultCandidates
                    .Where(item => planResults.Contains(item.Result))
                    .OrderByDescending(item => item.EndTime)
                    .ToArray();
                MapResultSourceChoice[] choices = candidates.Select(item => item.Choice).ToArray();
                MapResultSelectionOverride[] nodeOverrides = overrides
                    .Where(item => SameLocator(item.Node, locator))
                    .OrderBy(item => item.SelectionIndex).ToArray();
                int slotCount = nodeOverrides.Length == 0 ? 1 : nodeOverrides[^1].SelectionIndex + 1;
                var states = new MapResultSelectionState[slotCount];
                var selectedPerSlot = new (ResultChoice? Choice, string Mode)[slotCount];
                for (int index = 0; index < slotCount; index++)
                {
                    MapResultSelectionOverride? choiceOverride = nodeOverrides.FirstOrDefault(item => item.SelectionIndex == index);
                    string requestedMode = choiceOverride?.Mode ?? "Latest";
                    ResultChoice? selected;
                    string normalizedMode;
                    if (requestedMode == "Explicit" && choiceOverride?.ResultPath is { } requestedPath &&
                        protectedSourcePaths.Contains(SourceFilePaths.Canonical(requestedPath)))
                    {
                        // Keep this logical slot present, but do not resolve,
                        // hash or open the callback-owned file while its writer
                        // lease is active.
                        selected = null;
                        normalizedMode = requestedMode;
                    }
                    else
                    {
                        selected = ResolveSelection(candidates, requestedMode,
                            choiceOverride?.ResultPath, choiceOverride?.ResultRevision, out normalizedMode);
                    }
                    states[index] = new MapResultSelectionState(index, normalizedMode,
                        selected?.Choice, choices);
                    selectedPerSlot[index] = (selected, normalizedMode);
                }

                var activeNode = new MapResultActiveNode(locator, kind, node.Name, states,
                    selectedMapType, profileIndex);
                activeNodes.Add(activeNode);
                for (int index = 0; index < selectedPerSlot.Length; index++)
                {
                    if (selectedPerSlot[index].Choice is not { } selected) continue;
                    resolvedResults.Add(new ResolvedMapResultNode(activeNode, index, [index], selected.Result,
                        selected.Choice.ResultPath, selected.Choice.Revision, selectedMapType,
                        profileIndex, sourceNode, [new MapResultSelectionSource(locator, index)]));
                }
            }
            else
            {
                ResultChoice selected = FindStandaloneResult(resultCandidates, node);
                MapResultSelectionOverride? choiceOverride = overrides.FirstOrDefault(item =>
                    SameLocator(item.Node, locator));
                if (choiceOverride is { SelectionIndex: not 0 })
                    throw new ArgumentException("A standalone Result has one source selection slot.", nameof(requestedOverrides));
                if (choiceOverride is not null && (choiceOverride.Mode != "Explicit" ||
                    !MatchesResult(choiceOverride.ResultPath, choiceOverride.ResultRevision, selected)))
                    throw new ArgumentException("A standalone Result selection must retain its exact source file.", nameof(requestedOverrides));

                MapResultSourceChoice[] choices = [selected.Choice];
                var state = new MapResultSelectionState(0, "Explicit", selected.Choice, choices);
                var activeNode = new MapResultActiveNode(locator, kind, node.Name, [state],
                    selectedMapType, profileIndex);
                activeNodes.Add(activeNode);
                resolvedResults.Add(new ResolvedMapResultNode(activeNode, 0, [0], selected.Result,
                    selected.Choice.ResultPath, selected.Choice.Revision, selectedMapType,
                    profileIndex, sourceNode, [new MapResultSelectionSource(locator, 0)]));
            }
        }

        // MapResultsChart stores datasets in a HashSet<(VisualContext, MapTypes)>.
        // A ResultVisualContext is shared by a MultiResult option and an
        // individual Result tree node on the same map, so deduplicate by the
        // same underlying Result object and the selected map type while keeping
        // all source node/slot addresses attached to the row.
        resolvedResults = resolvedResults
            .GroupBy(item => (item.Result, item.SelectedMapType), ResultMapTypeComparer.Instance)
            .Select(group =>
            {
                ResolvedMapResultNode first = group.First();
                MapResultSelectionSource[] selections = group.SelectMany(item => item.Selections)
                    .DistinctBy(item => (LocatorKey(item.Node), item.SelectionIndex))
                    .ToArray();
                int[] selectionIndices = selections.Where(item => SameLocator(item.Node, first.ActiveNode.Node))
                    .Select(item => item.SelectionIndex).Distinct().Order().ToArray();
                return first with { SelectionIndices = selectionIndices, Selections = selections };
            }).ToList();

        foreach (ResultChoice candidate in resultCandidates)
            EnsureUnchanged(candidate.Choice.ResultPath, candidate.Choice.Revision);
        return activeNodes.ToArray();

        void Visit(IEnumerable<CatalogTreeNode> nodes)
        {
            foreach (CatalogTreeNode item in nodes)
            {
                bool checkable = item.Checked is not null;
                bool visible = !checkable || item.Checked == "checked";
                if (checkable && !visible) continue;
                if (item.ProtectedResultSourcePath is { } protectedPath)
                {
                    if (item.Locator is not { NodeType: "Result" } protectedLocator ||
                        item.AssetID is not null ||
                        protectedLocator.AssetID is not null ||
                        !Path.IsPathFullyQualified(protectedPath) ||
                        !protectedSourcePaths.Contains(SourceFilePaths.Canonical(protectedPath)) ||
                        !string.Equals(Path.GetFileNameWithoutExtension(protectedPath), item.Name,
                            StringComparison.Ordinal) ||
                        !protectedResultMarkers.Add(SourceFilePaths.Canonical(protectedPath)))
                        throw new SourceFileChangedException();
                    XElement raw = ResolveSerializedNode(serializedMap, protectedLocator)
                        ?? throw new SourceFileChangedException();
                    if ((string?)raw.Attribute("Type") != "Result" ||
                        !string.Equals((string?)raw.Attribute("Name"), item.Name, StringComparison.Ordinal) ||
                        !bool.TryParse((string?)raw.Attribute("IsChecked"), out bool isChecked) || !isChecked)
                        throw new SourceFileChangedException();
                    // Keep the checked .vis row intact, but do not expose it as
                    // an active readable Result while its exact callback path
                    // is held by the compute writer. Its children share that
                    // unavailable owner and are omitted too.
                    continue;
                }
                if (item.Locator is { NodeType: "Result" or "MultiResult" } locator &&
                    item.Checked == "checked")
                {
                    XElement raw = ResolveSerializedNode(serializedMap, locator)
                        ?? throw new SourceFileChangedException();
                    active.Add((item, raw));
                }
                if (item.Children is { Length: > 0 } children)
                    Visit(children);
            }
        }
    }

    private static MapResultSelectionOverride[] ValidateOverrides(
        MapResultSelectionOverride[]? requestedOverrides,
        IReadOnlyDictionary<string, (CatalogTreeNode Node, XElement SourceNode)> activeByLocator)
    {
        MapResultSelectionOverride[] overrides = requestedOverrides ?? [];
        if (overrides.Length > MaximumOverrides)
            throw new ArgumentException("The map contains too many result selection overrides.", nameof(requestedOverrides));
        var keys = new HashSet<(string Locator, int Index)>();
        foreach (MapResultSelectionOverride? selection in overrides)
        {
            if (selection is null || selection.Node is null)
                throw new ArgumentException("A result selection override is incomplete.", nameof(requestedOverrides));
            if (selection.SelectionIndex is < 0 or >= MaximumSelectionSlots)
                throw new ArgumentOutOfRangeException(nameof(requestedOverrides), "A Result selection index is outside the supported source range.");
            if (selection.Mode is not ("Latest" or "Previous" or "Explicit"))
                throw new ArgumentException("Choose Latest, Previous or Explicit for a Result selection.", nameof(requestedOverrides));
            string key = LocatorKey(selection.Node);
            if (!keys.Add((key, selection.SelectionIndex)))
                throw new ArgumentException("A Result selection slot appears more than once.", nameof(requestedOverrides));
            if (!activeByLocator.TryGetValue(key, out var active) || !SameLocator(active.Node.Locator!, selection.Node))
                throw new ArgumentException("The Result selection does not belong to an active node in the selected map.", nameof(requestedOverrides));
            if (active.Node.Locator!.NodeType == "Result" && selection.SelectionIndex != 0)
                throw new ArgumentException("A standalone Result has one source selection slot.", nameof(requestedOverrides));
            if (selection.Mode == "Explicit")
            {
                if (string.IsNullOrWhiteSpace(selection.ResultPath) || string.IsNullOrWhiteSpace(selection.ResultRevision))
                    throw new ArgumentException("An explicit Result selection requires its path and revision.", nameof(requestedOverrides));
            }
            else if (selection.ResultPath is not null || selection.ResultRevision is not null)
                throw new ArgumentException("Latest and Previous selections do not accept a pinned Result path.", nameof(requestedOverrides));
        }

        foreach (IGrouping<string, MapResultSelectionOverride> group in overrides.GroupBy(
                     item => LocatorKey(item.Node), StringComparer.Ordinal))
        {
            int[] indices = group.Select(item => item.SelectionIndex).Order().ToArray();
            if (indices.Where((index, ordinal) => index != ordinal).Any())
                throw new ArgumentException("MultiResult selection slots must be contiguous from zero.", nameof(requestedOverrides));
        }
        return overrides;
    }

    private static ResultChoice? ResolveSelection(ResultChoice[] candidates, string mode,
        string? resultPath, string? resultRevision, out string normalizedMode)
    {
        normalizedMode = mode;
        if (mode == "Latest")
        {
            if (resultPath is not null || resultRevision is not null)
                throw new ArgumentException("Latest selection does not accept a pinned Result path.");
            return candidates.FirstOrDefault();
        }
        if (mode == "Previous")
        {
            if (resultPath is not null || resultRevision is not null)
                throw new ArgumentException("Previous selection does not accept a pinned Result path.");
            return candidates.Skip(1).FirstOrDefault();
        }
        if (mode != "Explicit" || string.IsNullOrWhiteSpace(resultPath) || string.IsNullOrWhiteSpace(resultRevision))
            throw new ArgumentException("An explicit Result selection requires its path and revision.");
        ResultChoice selected = candidates.FirstOrDefault(candidate => MatchesResult(resultPath, resultRevision, candidate))
            ?? throw new ArgumentException("The selected Result is not a current option for this map node.");
        if (candidates.FirstOrDefault() is { } latest && SameResult(latest, selected))
            normalizedMode = "Latest";
        else if (candidates.Skip(1).FirstOrDefault() is { } previous && SameResult(previous, selected))
            normalizedMode = "Previous";
        return selected;
    }

    private static ResultChoice FindStandaloneResult(IReadOnlyList<ResultChoice> candidates, CatalogTreeNode node)
    {
        string? assetID = node.Locator?.AssetID ?? node.AssetID;
        if (string.IsNullOrWhiteSpace(assetID))
            throw new ArgumentException("The active Result node has no exact source file identity.");
        string canonical = SourceFilePaths.Canonical(assetID);
        ResultChoice[] matches = candidates.Where(candidate =>
            SourceFilePaths.SameSourceFile(candidate.Choice.ResultPath, canonical)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new SourceFileChangedException(),
            _ => throw new ArgumentException("The active Result node has an ambiguous source file identity.")
        };
    }

    private sealed class ProjectPlanResultLookup
    {
        private readonly HashSet<Result> projectResults;
        private readonly HashSet<string> sessionHdfResultPaths;
        private ObservableProject? observableProject;
        private readonly CoreLayerRequestContext? coreLayerContext;

        public ProjectPlanResultLookup(Project project, CoreLayerRequestContext? coreLayerContext = null)
        {
            Project = project;
            this.coreLayerContext = coreLayerContext;
            projectResults = new HashSet<Result>(project.Results, ReferenceEqualityComparer.Instance);
            sessionHdfResultPaths = (coreLayerContext?.SessionHdfReferences ?? [])
                .Where(reference => reference.Kind == "result")
                .Select(reference => SourceFilePaths.Canonical(reference.SourcePath))
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        public Project Project { get; }

        public string? FriendlyFailureReason(Result result)
        {
            observableProject ??= new ObservableProject(Project, Utility.SynchronizationContext.None);
            return observableProject.TryFindObservableResult(result)?.FriendlyFailureReason;
        }

        public HashSet<Result> MatchedResultsForProjectPlan(string planAssetID, string planName)
        {
            Plan[] namedPlans = Project.Plans.Where(plan =>
                string.Equals(plan.Name, planName, StringComparison.Ordinal)).ToArray();
            if (namedPlans.Length != 1 || string.IsNullOrWhiteSpace(planAssetID))
                throw new SourceFileChangedException();

            Plan[] matchingPlans = namedPlans.Where(plan => !string.IsNullOrWhiteSpace(plan.Filename) &&
                SourceFilePaths.SameSourceFile(ProjectPlanAssetPath(Project, plan.Filename), planAssetID)).ToArray();
            if (matchingPlans.Length != 1)
                throw new SourceFileChangedException();

            observableProject ??= new ObservableProject(Project, Utility.SynchronizationContext.None);
            ObservablePlan[] observablePlans = observableProject.Plans.Where(candidate =>
                ReferenceEquals(candidate.Plan, matchingPlans[0])).ToArray();
            if (observablePlans.Length != 1 ||
                !string.Equals(observablePlans[0].Name, planName, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            var matchedResults = new HashSet<Result>(ReferenceEqualityComparer.Instance);
            foreach (ObservableResult observableResult in observablePlans[0].NameMatchedResults)
            {
                Result result = observableResult.Result;
                if (!projectResults.Contains(result))
                    throw new SourceFileChangedException();
                if (!string.IsNullOrWhiteSpace(result.Filename) &&
                    sessionHdfResultPaths.Contains(ProjectPlanAssetPath(Project, result.Filename)))
                    continue;
                matchedResults.Add(result);
            }
            return matchedResults;
        }

        public HashSet<Result> MatchedResultsForSessionHdfResultGroup(SessionHdfResultOwner owner)
        {
            ValidateSessionHdfResultOwner(owner);
            var matches = new HashSet<Result>(ReferenceEqualityComparer.Instance);
            foreach (Result result in Project.Results)
            {
                if (result.IsInMemory || string.IsNullOrWhiteSpace(result.Filename) ||
                    !sessionHdfResultPaths.Contains(ProjectPlanAssetPath(Project, result.Filename)) ||
                    !string.Equals(result.Plan?.Name, owner.EmbeddedPlanName, StringComparison.Ordinal))
                    continue;
                if (!projectResults.Contains(result)) throw new SourceFileChangedException();
                matches.Add(result);
            }
            if (matches.Count == 0 || !matches.Any(result => !string.IsNullOrWhiteSpace(result.Filename) &&
                    SourceFilePaths.SameSourceFile(ProjectPlanAssetPath(Project, result.Filename),
                        owner.ResultPath)))
                throw new SourceFileChangedException();
            return matches;
        }

        public void ValidateSessionHdfResultOwner(SessionHdfResultOwner owner)
        {
            CoreLayerRequestContext context = coreLayerContext ?? throw new SourceFileChangedException();
            if (!string.Equals(owner.CoreLayerContextIdentityToken, context.IdentityToken,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(owner.ProjectSessionID, context.ProjectSessionID, StringComparison.Ordinal) ||
                !SourceFilePaths.SameSourceFile(owner.ProjectPath, context.ProjectPath) ||
                !sessionHdfResultPaths.Contains(SourceFilePaths.Canonical(owner.ResultPath)))
                throw new SourceFileChangedException();
            SessionHdfReferenceCapture[] references = context.SessionHdfReferences.Where(reference =>
                string.Equals(reference.ReferenceID, owner.ReferenceID, StringComparison.OrdinalIgnoreCase) &&
                reference.Kind == "result" &&
                SourceFilePaths.SameSourceFile(reference.SourcePath, owner.ResultPath) &&
                reference.SourceRevision.Equals(owner.ResultRevision, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reference.ResultPlanName, owner.EmbeddedPlanName, StringComparison.Ordinal) &&
                string.Equals(reference.ProjectSessionID, owner.ProjectSessionID, StringComparison.Ordinal) &&
                SourceFilePaths.SameSourceFile(reference.ProjectPath, owner.ProjectPath)).ToArray();
            if (references.Length != 1 || !Project.Results.Any(result => !result.IsInMemory &&
                    !string.IsNullOrWhiteSpace(result.Filename) &&
                    SourceFilePaths.SameSourceFile(ProjectPlanAssetPath(Project, result.Filename), owner.ResultPath) &&
                    string.Equals(result.Name, references[0].Name, StringComparison.Ordinal) &&
                    string.Equals(result.Plan?.Name, owner.EmbeddedPlanName, StringComparison.Ordinal)))
                throw new SourceFileChangedException();
        }
    }

    /// <summary>
    /// Resolves the exact saved Result selected by a source-tree FlowArea
    /// inspector. Unlike ResolveContext/BuildActiveNodes this intentionally
    /// does not require the Result or any ancestor to be checked: the caller
    /// has already captured the exact serialized owner locator and slot-zero
    /// selection from the map source tree. Rendering and chart discovery keep
    /// their visibility-gated active-node semantics.
    /// </summary>
    internal static Result ResolveSourceTreeMeshErrorResult(Project project,
        CatalogTreeNodeLocator resultNode, string selectedResultPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(resultNode);
        if (string.IsNullOrWhiteSpace(selectedResultPath) ||
            !Path.IsPathFullyQualified(selectedResultPath))
            throw new SourceFileChangedException();

        IEnumerable<Result> eligible;
        if (resultNode.NodeType == "MultiResult")
        {
            if (resultNode.AssetID is not { Length: > 0 } planAsset ||
                !Path.IsPathFullyQualified(planAsset) || string.IsNullOrWhiteSpace(resultNode.NodeName))
                throw new SourceFileChangedException();
            eligible = new ProjectPlanResultLookup(project)
                .MatchedResultsForProjectPlan(planAsset, resultNode.NodeName);
        }
        else if (resultNode.NodeType == "Result")
        {
            if (resultNode.AssetID is not { Length: > 0 } resultAsset ||
                !SourceFilePaths.SameSourceFile(resultAsset, selectedResultPath))
                throw new SourceFileChangedException();
            // A serialized Result row can refer to a result that belongs to a
            // Plan. Resolve it by its exact saved HDF identity and row name;
            // the direct Result tree node itself is the owner, regardless of
            // whether the Result also has a Plan association.
            eligible = project.Results.Where(result =>
                string.Equals(result.Name, resultNode.NodeName, StringComparison.Ordinal));
        }
        else
        {
            throw new SourceFileChangedException();
        }

        Result[] exact = eligible.Where(result => !result.IsInMemory &&
            !string.IsNullOrWhiteSpace(result.Filename) &&
            SourceFilePaths.SameSourceFile(ProjectPlanAssetPath(project, result.Filename),
                selectedResultPath)).ToArray();
        if (exact.Length != 1) throw new SourceFileChangedException();
        return exact[0];
    }

    private static ResultChoice[] SavedProjectResults(ProjectPlanResultLookup planResultLookup,
        IReadOnlySet<Result> matchedPlanResults, ResultSourceReadContext resultSourceReads,
        IReadOnlySet<string> standaloneResultPaths)
    {
        Project project = planResultLookup.Project;
        var matches = new List<(Result Result, string Path, int Order)>();
        int order = 0;
        foreach (Result result in project.Results)
        {
            if (result.IsInMemory || string.IsNullOrWhiteSpace(result.Filename))
            {
                order++;
                continue;
            }
            string path = Path.IsPathRooted(result.Filename)
                ? result.Filename
                : Path.Combine(project.ProjectDirectory, result.Filename);
            string canonical = SourceFilePaths.Canonical(path);
            bool belongsToActivePlan = matchedPlanResults.Contains(result);
            if (!belongsToActivePlan && !standaloneResultPaths.Contains(canonical))
            {
                order++;
                continue;
            }
            if (File.Exists(canonical)) matches.Add((result, canonical, order));
            order++;
        }

        ResultChoice[] choices = matches.Select(item =>
        {
            PersistedResultMetadata metadata = resultSourceReads.Read(item.Result, item.Path,
                planResultLookup);
            var choice = new MapResultSourceChoice(item.Path, metadata.Revision, item.Result.Name,
                item.Result.Plan?.Name,
                metadata.EndTime == DateTime.MinValue ? null : metadata.EndTime.ToString("O", CultureInfo.InvariantCulture),
                IsComputing: false,
                ComputeState: metadata.ComputeState,
                ComputeFailureReason: metadata.ComputeFailureReason,
                DiagnosticStatus: metadata.DiagnosticStatus,
                LayerErrors: metadata.LayerErrors,
                DiagnosticReadError: metadata.DiagnosticReadError);
            return new ResultChoice(item.Result, choice, metadata.EndTime, item.Order);
        }).OrderByDescending(item => item.EndTime)
            .ThenBy(item => item.Order)
            .ToArray();

        if (choices.Select(item => item.Choice.ResultPath).Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new ArgumentException("The current project lists the same Result source more than once.");
        return choices;
    }

    private static PersistedResultMetadata ReadPersistedResultMetadata(Result result, string canonicalPath,
        string capturedRevision, ProjectPlanResultLookup planResultLookup)
    {
        string? computeState = null;
        string? computeFailureReason = null;
        DateTime endTime = DateTime.MinValue;
        try
        {
            // The Result object may expose a default ComputeSummary even when no
            // summary was persisted. Only the actual HDF group establishes state.
            using var reader = new H5Reader(canonicalPath);
            if (reader.ExistsAsGroup("/Results/" + Result.ComputeSummaryGroupName))
            {
                var summary = result.ComputeSummary;
                if (summary is not null)
                {
                    endTime = summary.EndTime;
                    computeState = summary.Success ? "completed" : "failed";
                    if (!summary.Success)
                        computeFailureReason = planResultLookup.FriendlyFailureReason(result);
                }
            }
        }
        catch (Exception)
        {
            // An unreadable or malformed summary cannot prove either success or
            // failure; keep the persisted compute status unknown.
            computeState = null;
            computeFailureReason = null;
            endTime = DateTime.MinValue;
        }

        CatalogTreeLayerError[]? layerErrors = null;
        string diagnosticStatus;
        string? diagnosticReadError = null;
        try
        {
            layerErrors = MaterializeLayerErrors(result.LayerErrors);
            diagnosticStatus = "loaded";
        }
        catch (Exception error)
        {
            diagnosticStatus = "readFailed";
            diagnosticReadError = error.Message;
        }

        (string? canonicalAfter, string? revisionAfter) = CatalogTreeService.TryReadStableRevision(canonicalPath);
        if (canonicalAfter is null || revisionAfter is null ||
            !SourceFilePaths.SameSourceFile(canonicalAfter, canonicalPath) ||
            !string.Equals(capturedRevision, revisionAfter, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        return new PersistedResultMetadata(capturedRevision, computeState, computeFailureReason,
            diagnosticStatus, layerErrors, diagnosticReadError, endTime);
    }

    private static CatalogTreeLayerError[] MaterializeLayerErrors(IReadOnlyList<Message>? messages) =>
        (messages ?? []).Select(message => new CatalogTreeLayerError(
            message.MessageType?.Code ?? string.Empty,
            message.MessageType?.DefaultSeverity.ToString() ?? "Unknown",
            message.EvaluatedMessage ?? string.Empty,
            message.LogData.Select(pair => new CatalogTreeLogDatum(pair.Key,
                pair.Value switch
                {
                    null => null,
                    IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                    _ => pair.Value.ToString()
                })).ToArray()))
        .ToArray();

    private static XElement? ResolveSerializedNode(XElement map, CatalogTreeNodeLocator locator)
    {
        if (locator.NodePath is not { Length: > 0 } path) return null;
        XElement[] siblings = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        XElement? found = null;
        foreach (int index in path)
        {
            if (index < 0 || index >= siblings.Length) return null;
            found = siblings[index];
            siblings = found.Elements("Node").ToArray();
        }
        return (string?)found?.Attribute("Type") == locator.NodeType &&
               (string?)found?.Attribute("Name") == locator.NodeName ? found : null;
    }

    private static string ValidateFeatureIdentity(ResolvedMapResultContext context,
        MapResultFeatureIdentity feature, GeometryTopologyCoordinate[] coordinates)
    {
        if (string.IsNullOrWhiteSpace(feature.VectorPath) || !Path.IsPathFullyQualified(feature.VectorPath))
            throw new ArgumentException("Choose a saved vector source from this map.", nameof(feature));
        if (string.IsNullOrWhiteSpace(feature.VectorRevision) || feature.VectorRevision.Length != 64 ||
            string.IsNullOrWhiteSpace(feature.FeatureID) || feature.FeatureID.Length > 1024 ||
            feature.FeatureID.Any(char.IsControl))
            throw new ArgumentException("The selected map feature identity is incomplete.", nameof(feature));
        string vectorPath = SourceFilePaths.Canonical(feature.VectorPath);
        if (!File.Exists(vectorPath)) throw new FileNotFoundException("The map vector source was not found.", vectorPath);
        EnsureUnchanged(vectorPath, feature.VectorRevision);
        string coordinateSHA = HashCoordinates(coordinates);
        if (string.IsNullOrWhiteSpace(feature.CoordinateSHA) ||
            !string.Equals(feature.CoordinateSHA, coordinateSHA, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The sample-line coordinates do not match the selected feature identity.", nameof(feature));

        bool member = Flatten(context.Tree.Roots).Any(node =>
            node.Locator is { NodeType: "MapLayer" } locator &&
            locator.MapType == "PolylinesVector" &&
            !string.IsNullOrWhiteSpace(node.AssetID) &&
            SourceFilePaths.SameSourceFile(node.AssetID, vectorPath));
        if (!member)
            throw new ArgumentException("The vector source is not a polyline layer in the selected map.", nameof(feature));
        EnsureUnchanged(vectorPath, feature.VectorRevision);
        return vectorPath;
    }

    private static GeometryTopologyCoordinate[] ValidateSampleLine(GeometryTopologyCoordinate[]? sampleLine)
    {
        if (sampleLine is null || sampleLine.Length is < 2 or > MaximumSampleLinePoints)
            throw new ArgumentException("Draw a sample line with two or more points.", nameof(sampleLine));
        if (sampleLine.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Sample-line coordinates must be finite.", nameof(sampleLine));
        return sampleLine;
    }

    private static MapResultSideEffect[] BuildSideEffects(IEnumerable<ResolvedMapResultNode> results) => results
        .Select(item => item.Result.ExpectedPostProcessFilename())
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.Ordinal)
        .Select(path => new MapResultSideEffect("ResultPostProcessCacheMayBeCreatedOrUpdated", path))
        .ToArray();

    private static string ContextToken(string projectPath, string projectRevision, string projectXmlSHA,
        string visualizationRevision, MapTreeRequest map, MapResultActiveNode[] activeNodes)
    {
        var payload = new
        {
            projectPath,
            projectRevision,
            projectXmlSHA,
            visualizationRevision,
            coreLayerContext = map.CoreLayerContext?.IdentityToken,
            map.MapID,
            map.MapIndex,
            map.MapName,
            map.MapType,
            computeReadLease = map.ComputeReadLease is { } lease ? new
            {
                lease.ProjectSessionID,
                lease.RunID,
                lease.ProjectPath,
                protectedSourcePaths = lease.ProtectedSourcePaths,
                lease.CallbackResultPath
            } : null,
            activeNodes = activeNodes.Select(node => new
            {
                locator = LocatorKey(node.Node),
                node.Node.NodeType,
                node.NodeKind,
                node.Name,
                node.SelectedMapType,
                node.ProfileIndex,
                selections = node.Selections.Select(selection => new
                {
                    selection.SelectionIndex,
                    selection.SelectionMode,
                    selected = selection.SelectedResult is { } selected
                        ? new { selected.ResultPath, selected.Revision, selected.ComputeState,
                            selected.ComputeFailureReason, selected.DiagnosticStatus, selected.LayerErrors,
                            selected.DiagnosticReadError }
                        : null,
                    choices = selection.Choices.Select(choice => new
                    {
                        choice.ResultPath,
                        choice.Revision,
                        choice.ComputeState,
                        choice.ComputeFailureReason,
                        choice.DiagnosticStatus,
                        choice.LayerErrors,
                        choice.DiagnosticReadError
                    }).ToArray()
                }).ToArray()
            }).ToArray()
        };
        return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, FingerprintJsonOptions)));
    }

    private static string FeatureContextToken(string contextToken, string vectorPath,
        string featureID, string vectorRevision, string coordinateSHA, string mode) =>
        Hash(Encoding.UTF8.GetBytes(string.Join('\0', "HECRAS-MAP-SAMPLE-v1", contextToken,
            vectorPath, featureID, vectorRevision.ToUpperInvariant(), coordinateSHA.ToUpperInvariant(), mode)));

    private static string HashCoordinates(IEnumerable<GeometryTopologyCoordinate> coordinates)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(CoordinateHashDomain));
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        foreach (GeometryTopologyCoordinate coordinate in coordinates)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes, unchecked((ulong)BitConverter.DoubleToInt64Bits(coordinate.X)));
            hash.AppendData(bytes);
            BinaryPrimitives.WriteUInt64BigEndian(bytes, unchecked((ulong)BitConverter.DoubleToInt64Bits(coordinate.Y)));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
            {
                foreach (CatalogTreeNode descendant in Flatten(children)) yield return descendant;
            }
        }
    }

    private static bool MatchesResult(string? resultPath, string? revision, ResultChoice choice) =>
        !string.IsNullOrWhiteSpace(resultPath) && !string.IsNullOrWhiteSpace(revision) &&
        SourceFilePaths.SameSourceFile(resultPath, choice.Choice.ResultPath) &&
        string.Equals(revision, choice.Choice.Revision, StringComparison.OrdinalIgnoreCase);

    private static bool SameResult(ResultChoice left, ResultChoice right) =>
        ReferenceEquals(left.Result, right.Result);

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(LocatorKey(left), LocatorKey(right), StringComparison.Ordinal);

    private static string LocatorKey(CatalogTreeNodeLocator locator) => string.Join('\u001f',
        locator.RootName, locator.NodeType, locator.NodeName,
        locator.NodePath is null ? string.Empty : string.Join('.', locator.NodePath),
        locator.AssetID ?? string.Empty, locator.MapType ?? string.Empty,
        locator.Filename ?? string.Empty, locator.FilenameRelative ?? string.Empty,
        locator.ParentGeometryAssetID ?? string.Empty,
        locator.ParentGeometryNodePath is null ? string.Empty : string.Join('.', locator.ParentGeometryNodePath),
        locator.ParentGeometryNodeName ?? string.Empty,
        locator.AssociationKind ?? string.Empty, locator.AssociationAssetID ?? string.Empty,
        locator.ParentPlanAssetID ?? string.Empty,
        locator.ParentPlanNodePath is null ? string.Empty : string.Join('.', locator.ParentPlanNodePath),
        locator.ParentPlanNodeName ?? string.Empty,
        locator.ParentResultNodeType ?? string.Empty,
        locator.ParentResultAssetID ?? string.Empty,
        locator.ParentResultNodePath is null ? string.Empty : string.Join('.', locator.ParentResultNodePath),
        locator.ParentResultNodeName ?? string.Empty,
        locator.ParentResultRevision ?? string.Empty,
        locator.SessionHdfResultOwner?.ReferenceID ?? string.Empty,
        locator.SessionHdfResultOwner?.ProjectSessionID ?? string.Empty,
        locator.SessionHdfResultOwner?.ProjectPath ?? string.Empty,
        locator.SessionHdfResultOwner?.ResultPath ?? string.Empty,
        locator.SessionHdfResultOwner?.ResultRevision ?? string.Empty,
        locator.SessionHdfResultOwner?.EmbeddedPlanName ?? string.Empty,
        locator.SessionHdfResultOwner?.CoreLayerContextIdentityToken ?? string.Empty);

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private sealed class ResultSourceReadContext
    {
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private readonly Dictionary<string, string?> capturedRevisions;
        private readonly HashSet<string> protectedPaths;
        private readonly Dictionary<string, (Result Result, PersistedResultMetadata Metadata)> metadata =
            new(PathComparer);

        private ResultSourceReadContext(Dictionary<string, string?> capturedRevisions,
            IReadOnlySet<string>? protectedPaths = null)
        {
            this.capturedRevisions = capturedRevisions;
            this.protectedPaths = protectedPaths is null
                ? new HashSet<string>(PathComparer)
                : new HashSet<string>(protectedPaths, PathComparer);
        }

        internal static ResultSourceReadContext Capture(string projectPath, string[]? protectedPaths = null,
            SessionHdfReferenceCapture[]? sessionHdfReferences = null)
        {
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
                ?? throw new ArgumentException("The project path must have a containing directory.", nameof(projectPath));
            string resultsDirectory = Project.GetDefaultResultDirectory(projectDirectory);
            var captured = new Dictionary<string, string?>(PathComparer);
            HashSet<string> protectedSet = (protectedPaths ?? [])
                .Select(SourceFilePaths.Canonical).ToHashSet(PathComparer);
            string[] paths;
            try
            {
                paths = Directory.Exists(resultsDirectory)
                    ? Directory.GetFiles(resultsDirectory, "*.h5", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(resultsDirectory, "*.hdf", SearchOption.TopDirectoryOnly))
                        .ToArray()
                    : [];
            }
            catch (IOException) { return new ResultSourceReadContext(captured, protectedSet); }
            catch (UnauthorizedAccessException) { return new ResultSourceReadContext(captured, protectedSet); }
            catch (ArgumentException) { return new ResultSourceReadContext(captured, protectedSet); }

            foreach (string path in paths)
            {
                string canonical;
                try { canonical = SourceFilePaths.Canonical(path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                catch (ArgumentException) { continue; }
                if (protectedSet.Contains(canonical)) continue;

                (string? stablePath, string? revision) = CatalogTreeService.TryReadStableRevision(path);
                captured[canonical] = stablePath is not null && revision is not null &&
                    SourceFilePaths.SameSourceFile(stablePath, canonical) ? revision : null;
            }

            foreach (SessionHdfReferenceCapture reference in sessionHdfReferences ?? [])
            {
                if (reference.Kind != "result") continue;
                string canonical;
                try { canonical = SourceFilePaths.Canonical(reference.SourcePath); }
                catch (IOException) { throw new SourceFileChangedException(); }
                catch (UnauthorizedAccessException) { throw new SourceFileChangedException(); }
                catch (ArgumentException) { throw new SourceFileChangedException(); }
                if (!SourceFilePaths.SameSourceFile(reference.CanonicalSourcePath, canonical))
                    throw new SourceFileChangedException();
                if (protectedSet.Contains(canonical)) continue;

                (string? stablePath, string? revision) = CatalogTreeService.TryReadStableRevision(canonical);
                if (stablePath is null || revision is null ||
                    !SourceFilePaths.SameSourceFile(stablePath, canonical) ||
                    !string.Equals(revision, reference.SourceRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
                if (captured.TryGetValue(canonical, out string? existing) &&
                    existing is not null && !string.Equals(existing, revision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
                captured[canonical] = revision;
            }
            return new ResultSourceReadContext(captured, protectedSet);
        }

        internal PersistedResultMetadata Read(Result result, string path,
            ProjectPlanResultLookup planResultLookup)
        {
            string canonical = SourceFilePaths.Canonical(path);
            if (protectedPaths.Contains(canonical)) throw new SourceFileChangedException();
            if (!capturedRevisions.TryGetValue(canonical, out string? capturedRevision) ||
                string.IsNullOrWhiteSpace(capturedRevision))
                throw new SourceFileChangedException();

            if (metadata.TryGetValue(canonical, out (Result Result, PersistedResultMetadata Metadata) cached))
            {
                if (!ReferenceEquals(cached.Result, result))
                    throw new ArgumentException("The current project lists the same Result source more than once.");
                return cached.Metadata;
            }

            PersistedResultMetadata value = ReadPersistedResultMetadata(result, canonical,
                capturedRevision, planResultLookup);
            metadata.Add(canonical, (result, value));
            return value;
        }
    }

    private sealed record PersistedResultMetadata(string Revision, string? ComputeState,
        string? ComputeFailureReason, string DiagnosticStatus, CatalogTreeLayerError[]? LayerErrors,
        string? DiagnosticReadError, DateTime EndTime);

    private sealed record ResultChoice(Result Result, MapResultSourceChoice Choice, DateTime EndTime, int Order);

    private sealed class ResultMapTypeComparer : IEqualityComparer<(Result Result, string? SelectedMapType)>
    {
        internal static ResultMapTypeComparer Instance { get; } = new();

        public bool Equals((Result Result, string? SelectedMapType) x,
            (Result Result, string? SelectedMapType) y) =>
            ReferenceEquals(x.Result, y.Result) && string.Equals(x.SelectedMapType, y.SelectedMapType, StringComparison.Ordinal);

        public int GetHashCode((Result Result, string? SelectedMapType) value) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Result),
                value.SelectedMapType is null ? 0 : StringComparer.Ordinal.GetHashCode(value.SelectedMapType));
    }
}
