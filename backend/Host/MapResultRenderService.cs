using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Mapping;
using Ras.Output;
using Ras.Rendering;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Renders the selected Result map contexts for one exact current-map node.
/// Stored values, embedded Result geometry, current-project terrain
/// association, source map type and KeyedProfileState timestamp matching all
/// come from the retained HEC-RAS project and Result implementations.
/// </summary>
public static class MapResultRenderService
{
    private const int MaximumRasterSide = 1536;
    internal const long MaximumAggregateRasterPixels = 8L * 1024 * 1024;
    private const long MultiResultTimestampToleranceTicks = TimeSpan.TicksPerMillisecond * 10;
    internal const float NoDataValue = -9999f;

    public static MapResultRenderResult Render(MapResultRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Node);
        if (string.IsNullOrWhiteSpace(request.ContextToken))
            throw new ArgumentException("Refresh the selected map result sources before rendering.", nameof(request));

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.SelectionOverrides, request.ContextToken);
        MapResultActiveNode? activeNode = context.Sources.ActiveNodes
            .SingleOrDefault(node => SameLocator(node.Node, request.Node));
        if (activeNode is null)
            throw new SourceFileChangedException();
        if (request.ForceHorizontalRendering && activeNode.NodeKind != "MultiResult")
            throw new SourceFileChangedException();

        string sourceID = NodeIdentity(activeNode.Node);
        int[] blockedSelectionIndices = ValidateBlockedSelectionIndices(request, activeNode);
        Dictionary<int, MapResultRenderOptions> renderOptionsBySelection =
            ValidateRenderOptions(request, activeNode, blockedSelectionIndices);
        MapResultRenderLiveKeyframeAnchor? liveKeyframe = ValidateLiveKeyframeAnchor(request,
            activeNode, context.Map.ComputeReadLease);
        SharedKeyframeValidation? sharedKeyframe = ValidateSharedKeyframeAnchor(request,
            context, activeNode, blockedSelectionIndices);
        MapResultRenderSharedLiveProfileMatch[] sharedLiveMatches = sharedKeyframe is null
            ? []
            : ResolveSharedLiveProfileMatches(request, context, activeNode, sharedKeyframe,
                blockedSelectionIndices);
        ResolvedMapResultNode[] selectedContexts = context.ResultContexts
            .Where(candidate => candidate.Selections.Any(selection => SameLocator(selection.Node, activeNode.Node)))
            .Where(candidate => SelectionIndicesFor(candidate, activeNode.Node)
                .Any(index => !blockedSelectionIndices.Contains(index)))
            .OrderBy(candidate => SelectionIndicesFor(candidate, activeNode.Node).DefaultIfEmpty(int.MaxValue).Min())
            .ToArray();

        ResolvedMapResultNode? keyContext = sharedKeyframe?.StoredAnchor ?? (activeNode.NodeKind == "MultiResult"
            ? blockedSelectionIndices.Contains(0) ? null
                : selectedContexts.FirstOrDefault(candidate => SelectionIndicesFor(candidate, activeNode.Node).Contains(0))
            : selectedContexts.FirstOrDefault());
        int selectedProfileIndex = sharedKeyframe?.Anchor.ProfileIndex
            ?? request.ProfileIndex ?? liveKeyframe?.ProfileIndex ?? activeNode.ProfileIndex ?? 0;
        string? selectedProfileTime = sharedKeyframe?.Anchor.Time ?? liveKeyframe?.Time;
        MapResultRenderStep[] steps = sharedKeyframe?.Anchor.Profiles
            .OrderBy(profile => profile.Index)
            .Select(profile => new MapResultRenderStep(profile.Index, profile.Name, profile.Time))
            .ToArray() ?? [];

        if (sharedKeyframe is null && keyContext is not null)
        {
            IHorizontalOutputBlock? keyBlock = keyContext.Result.BaseOutputBlock;
            if (keyBlock is not null)
            {
                ValidateProfiles(keyBlock);
                steps = Enumerable.Range(0, keyBlock.ProfileCount)
                    .Select(index => new MapResultRenderStep(index, keyBlock.ProfileNames[index],
                        FormatTime(keyBlock.Times[index])))
                    .ToArray();
                if (selectedProfileIndex >= 0 && selectedProfileIndex < keyBlock.ProfileCount)
                    selectedProfileTime = FormatTime(keyBlock.Times[selectedProfileIndex]);
            }
        }

        RasterMapViewExtent? viewport = request.Viewport;
        if (viewport is not null) ValidateViewport(viewport);
        List<MapResultRenderLayerPayload> layers = [];
        List<MapResultRenderSourceStatus> statuses = [];

        if (selectedContexts.Length == 0)
        {
            string? reason = sharedLiveMatches.Any(match => match.ProfileIndex is not null)
                ? null
                : activeNode.Selections.Any(selection => selection.SelectedResult is not null)
                ? "The selected Result sources are no longer active in this map context."
                : "This map node has no selected Result sources.";
            context.EnsureUnchanged();
            return new(context.Sources.ContextToken, sourceID, selectedProfileIndex,
                selectedProfileTime, steps, [], [], reason,
                sharedKeyframe?.Identity, sharedLiveMatches);
        }

        int renderLayerCount = selectedContexts.Sum(source => SelectionIndicesFor(source, activeNode.Node)
            .Where(index => !blockedSelectionIndices.Contains(index))
            .GroupBy(index => renderOptionsBySelection.GetValueOrDefault(index, MapResultRenderOptions.Default)).Count());
        MapTypes? mapType = ParseSupportedMapType(activeNode.SelectedMapType);
        foreach (ResolvedMapResultNode source in selectedContexts)
        {
            int[] selectionIndices = SelectionIndicesFor(source, activeNode.Node)
                .Where(index => !blockedSelectionIndices.Contains(index)).ToArray();
            foreach (IGrouping<MapResultRenderOptions, int> optionGroup in selectionIndices
                         .GroupBy(index => renderOptionsBySelection.GetValueOrDefault(index,
                             MapResultRenderOptions.Default)))
            {
                int[] optionSelectionIndices = optionGroup.Order().ToArray();
                MapResultRenderOptions renderOptions = optionGroup.Key;
                string? unavailable = null;
                int? resultProfileIndex = null;
                Terrain? terrain = source.Result.AssociatedTerrain;
                TerrainSourceSnapshot? terrainSnapshot = null;

                if (source.Result.BaseOutputBlock is not IHorizontalOutputBlock outputBlock || outputBlock.ProfileCount <= 0)
                {
                    unavailable = "This Result has no stored horizontal output profiles.";
                }
                else if (mapType is null)
                {
                    unavailable = "The selected Result map type is not supported by the current map renderer.";
                }
                else if (terrain is null)
                {
                    unavailable = "The selected Result has no current-project associated terrain.";
                }
                else if (keyContext is null && liveKeyframe is null && sharedKeyframe is null)
                {
                    unavailable = "The MultiResult has no selected first Result to anchor its profile timeline.";
                }
                else if (sharedKeyframe is null && liveKeyframe is null
                         && (selectedProfileIndex < 0 || selectedProfileIndex >= steps.Length))
                {
                    unavailable = "The selected profile index is outside the Result timeline.";
                }
                else if (sharedKeyframe is not null && activeNode.NodeKind == "MultiResult" &&
                         (resultProfileIndex = sharedKeyframe.StoredAnchor is { } storedAnchor
                             && ReferenceEquals(source.Result, storedAnchor.Result)
                                 ? sharedKeyframe.Anchor.ProfileIndex
                                 : MatchKeyedProfileName(sharedKeyframe.Anchor.ProfileName, outputBlock)) is null)
                {
                    unavailable = "This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.";
                }
                else if (sharedKeyframe is null && activeNode.NodeKind == "MultiResult" && liveKeyframe is not null &&
                         (resultProfileIndex = MatchKeyedProfileName(liveKeyframe.ProfileName, outputBlock)) is null)
                {
                    unavailable = "This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.";
                }
                else if (sharedKeyframe is null && activeNode.NodeKind == "MultiResult" && liveKeyframe is null &&
                         (resultProfileIndex = MatchKeyedProfile(keyContext!.Result.BaseOutputBlock!,
                             selectedProfileIndex, outputBlock)) is null)
                {
                    unavailable = "This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.";
                }
                else
                {
                    resultProfileIndex ??= selectedProfileIndex;
                    if (resultProfileIndex < 0 || resultProfileIndex >= outputBlock.ProfileCount)
                        unavailable = "The selected profile index is outside this Result's stored profiles.";
                }

                if (unavailable is null && terrain is not null)
                {
                    try
                    {
                        terrainSnapshot = CaptureTerrainSources(terrain);
                        RasterMapViewExtent sourceViewport = viewport ?? DefaultViewport(source.Result.Geometry?.Extent
                            ?? throw new InvalidDataException("The selected Result has no embedded geometry extent."));
                        sourceViewport = ConstrainRasterViewport(sourceViewport, renderLayerCount);
                        MapResultRenderLayerPayload? rendered = RenderOne(context.Project, source, activeNode,
                            request.Map.MapID, sourceID, mapType!.Value, resultProfileIndex!.Value, sourceViewport,
                            terrainSnapshot, optionSelectionIndices, renderOptions,
                            request.ForceHorizontalRendering);
                        if (rendered is null)
                            unavailable = "The selected Result has no mapped cells in the current view.";
                        else
                            layers.Add(rendered);
                    }
                    catch (FileNotFoundException)
                    {
                        unavailable = "An associated terrain source file is missing.";
                    }
                    catch (NotSupportedException exception) when (
                        exception.Message.Contains("VRT", StringComparison.OrdinalIgnoreCase) ||
                        exception.Message.Contains("terrain raster", StringComparison.OrdinalIgnoreCase))
                    {
                        unavailable = exception.Message;
                    }
                }

                double[]? validExtent = null;
                if (unavailable is null && mapType is { } selectedMapType)
                {
                    Extent sourceExtent = new ResultMapResampler(source.Result, selectedMapType).ValidExtent;
                    if (sourceExtent.IsFiniteAndValid())
                        validExtent = [sourceExtent.MinX, sourceExtent.MinY,
                            sourceExtent.MaxX, sourceExtent.MaxY];
                }
                statuses.Add(new(source.ResultPath, source.Revision, source.Result.Name,
                    optionSelectionIndices, terrainSnapshot?.Path, terrainSnapshot?.Revision,
                    unavailable is null, resultProfileIndex, unavailable, validExtent));
            }
        }

        context.EnsureUnchanged();
        string? overallUnavailable = layers.Count == 0
            ? statuses.Select(status => status.UnavailableReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
            : null;
        return new(context.Sources.ContextToken, sourceID, selectedProfileIndex,
            selectedProfileTime, steps, layers.ToArray(), statuses.ToArray(), overallUnavailable,
            sharedKeyframe?.Identity, sharedLiveMatches);
    }

    private static int[] ValidateBlockedSelectionIndices(MapResultRenderRequest request,
        MapResultActiveNode activeNode)
    {
        int[] indices = request.BlockedSelectionIndices ?? [];
        if (indices.Length > 32 || indices.Any(index => index < 0 || index >= 32) ||
            indices.Distinct().Count() != indices.Length ||
            indices.Any(index => !activeNode.Selections.Any(selection => selection.SelectionIndex == index)))
            throw new SourceFileChangedException();
        return indices;
    }

    private static Dictionary<int, MapResultRenderOptions> ValidateRenderOptions(
        MapResultRenderRequest request, MapResultActiveNode activeNode,
        IReadOnlyCollection<int> blockedSelectionIndices)
    {
        MapResultRenderOptionOverride[] overrides = request.RenderOptions ?? [];
        if (overrides.Length > 32 || overrides.Select(item => item.SelectionIndex).Distinct().Count() != overrides.Length)
            throw new ArgumentException("Result render options contain duplicate or excessive slot identities.", nameof(request));

        Dictionary<int, MapResultRenderOptions> resolved = [];
        foreach (MapResultRenderOptionOverride option in overrides)
        {
            if (option.SelectionIndex < 0 || option.SelectionIndex >= 32 ||
                blockedSelectionIndices.Contains(option.SelectionIndex))
                throw new SourceFileChangedException();
            MapResultSelectionState? selection = activeNode.Selections.SingleOrDefault(item =>
                item.SelectionIndex == option.SelectionIndex);
            MapResultSourceChoice? selected = selection?.SelectedResult;
            if (selected is null || selected.IsComputing ||
                !Path.IsPathFullyQualified(option.ResultPath) ||
                !SourceFilePaths.SameSourceFile(option.ResultPath, selected.ResultPath) ||
                !string.Equals(option.Revision, selected.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            if (option.VelocityDisplay is not ("Magnitude" or "Vx" or "Vy"))
                throw new ArgumentException("The requested Velocity display mode is not supported.", nameof(request));
            resolved.Add(option.SelectionIndex, new MapResultRenderOptions(
                option.VelocityDisplay, option.MapDiagnostics, option.ForceRecomputeMapPoints));
        }
        return resolved;
    }

    private sealed record MapResultRenderOptions(string VelocityDisplay,
        bool MapDiagnostics, bool ForceRecomputeMapPoints)
    {
        internal static MapResultRenderOptions Default { get; } = new("Magnitude", false, false);
    }

    private sealed record SharedKeyframeValidation(
        MapResultRenderSharedKeyframeAnchor Anchor,
        ResolvedMapResultNode? StoredAnchor,
        MapResultRenderSharedKeyframeIdentity Identity);

    private static MapResultRenderLiveKeyframeAnchor? ValidateLiveKeyframeAnchor(
        MapResultRenderRequest request, MapResultActiveNode activeNode,
        MapResultComputeReadLease? lease)
    {
        MapResultRenderLiveKeyframeAnchor? anchor = request.LiveKeyframeAnchor;
        if (anchor is null) return null;
        if (lease is null || request.Node.NodeType != "MultiResult" ||
            activeNode.NodeKind != "MultiResult" || !SameLocator(activeNode.Node, anchor.Node) ||
            anchor.SelectionIndex != 0 || request.BlockedSelectionIndices?.Contains(0) != true ||
            !Guid.TryParse(anchor.RunID, out Guid anchorRunID) ||
            !Guid.TryParse(lease.RunID, out Guid leaseRunID) || anchorRunID != leaseRunID ||
            !Path.IsPathFullyQualified(anchor.ResultPath) || string.IsNullOrWhiteSpace(anchor.OutputBlockName) ||
            anchor.ProfileCount <= 0 || anchor.ProfileIndex < 0 || anchor.ProfileIndex >= anchor.ProfileCount ||
            string.IsNullOrWhiteSpace(anchor.ProfileName) || string.IsNullOrWhiteSpace(anchor.Time) ||
            string.IsNullOrWhiteSpace(lease.CallbackResultPath) ||
            !SourceFilePaths.SameSourceFile(anchor.ResultPath, lease.CallbackResultPath) ||
            !(lease.ProtectedSourcePaths ?? []).Any(path =>
                SourceFilePaths.SameSourceFile(path, anchor.ResultPath)) ||
            request.ProfileIndex is int requestedIndex && requestedIndex != anchor.ProfileIndex ||
            !DateTime.TryParse(anchor.ProfileName, CultureInfo.CurrentCulture, DateTimeStyles.None,
                out DateTime nameTime) ||
            !DateTime.TryParse(anchor.Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTime sourceTime) ||
            Math.Abs(nameTime.Ticks - sourceTime.Ticks) > MultiResultTimestampToleranceTicks)
            throw new SourceFileChangedException();
        return anchor;
    }

    private static SharedKeyframeValidation? ValidateSharedKeyframeAnchor(
        MapResultRenderRequest request, ResolvedMapResultContext context,
        MapResultActiveNode targetNode, IReadOnlyCollection<int> blockedSelectionIndices)
    {
        MapResultRenderSharedKeyframeAnchor? anchor = request.SharedKeyframeAnchor;
        MapResultRenderSharedLiveProfileTimeline[] liveTimelines = request.SharedLiveProfileTimelines ?? [];
        if (anchor is null)
        {
            if (liveTimelines.Length != 0) throw new SourceFileChangedException();
            return null;
        }

        if (request.LiveKeyframeAnchor is not null || request.Node.NodeType != "MultiResult" ||
            targetNode.NodeKind != "MultiResult" || anchor.Node.NodeType != "MultiResult" ||
            anchor.SelectionIndex != 0 || !Path.IsPathFullyQualified(anchor.ResultPath) ||
            anchor.ProfileCount <= 0 || anchor.ProfileIndex < 0 || anchor.ProfileIndex >= anchor.ProfileCount ||
            request.ProfileIndex is int requestedIndex && requestedIndex != anchor.ProfileIndex ||
            liveTimelines.Length > 32 ||
            anchor.Profiles is null || anchor.Profiles.Length == 0 ||
            anchor.Profiles.Length > anchor.ProfileCount ||
            anchor.Profiles.Select(profile => profile.Index).Distinct().Count() != anchor.Profiles.Length ||
            anchor.Profiles.Any(profile => profile.Index < 0 || profile.Index >= anchor.ProfileCount ||
                string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Time)))
            throw new SourceFileChangedException();

        MapResultActiveNode? anchorNode = context.Sources.ActiveNodes.SingleOrDefault(node =>
            SameLocator(node.Node, anchor.Node));
        MapResultSelectionState? anchorSlot = anchorNode?.Selections.SingleOrDefault(selection =>
            selection.SelectionIndex == 0);
        MapResultRenderSharedKeyframeProfile selectedProfile = anchor.Profiles.SingleOrDefault(profile =>
            profile.Index == anchor.ProfileIndex) ?? throw new SourceFileChangedException();
        if (anchorNode?.NodeKind != "MultiResult" || anchorSlot is null ||
            !string.Equals(selectedProfile.Name, anchor.ProfileName, StringComparison.Ordinal) ||
            !string.Equals(selectedProfile.Time, anchor.Time, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        ResolvedMapResultNode? storedAnchor = null;
        if (anchor.RunID is null)
        {
            MapResultSourceChoice? selected = anchorSlot.SelectedResult;
            if (anchor.Revision is null || anchor.OutputBlockName is not null || selected is null || selected.IsComputing ||
                !SourceFilePaths.SameSourceFile(anchor.ResultPath, selected.ResultPath) ||
                !string.Equals(anchor.Revision, selected.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            storedAnchor = context.ResultContexts.SingleOrDefault(candidate =>
                candidate.Selections.Any(selection => selection.SelectionIndex == 0 &&
                    SameLocator(selection.Node, anchor.Node)) &&
                SourceFilePaths.SameSourceFile(candidate.ResultPath, anchor.ResultPath) &&
                string.Equals(candidate.Revision, anchor.Revision, StringComparison.OrdinalIgnoreCase));
            IHorizontalOutputBlock? block = storedAnchor?.Result.BaseOutputBlock;
            if (storedAnchor is null || block is null)
                throw new SourceFileChangedException();
            ValidateProfiles(block);
            if (anchor.ProfileCount != block.ProfileCount || anchor.Profiles.Length != block.ProfileCount)
                throw new SourceFileChangedException();
            for (int index = 0; index < block.ProfileCount; index++)
            {
                MapResultRenderSharedKeyframeProfile profile = anchor.Profiles.SingleOrDefault(item => item.Index == index)
                    ?? throw new SourceFileChangedException();
                if (!string.Equals(profile.Name, block.ProfileNames[index], StringComparison.Ordinal) ||
                    !string.Equals(profile.Time, FormatTime(block.Times[index]), StringComparison.Ordinal))
                    throw new SourceFileChangedException();
            }
        }
        else
        {
            MapResultComputeReadLease? lease = context.Map.ComputeReadLease;
            if (anchor.Revision is not null || string.IsNullOrWhiteSpace(anchor.OutputBlockName) ||
                !Guid.TryParse(anchor.RunID, out Guid anchorRunID) || lease is null ||
                !Guid.TryParse(lease.RunID, out Guid leaseRunID) || anchorRunID != leaseRunID ||
                string.IsNullOrWhiteSpace(lease.CallbackResultPath) ||
                !SourceFilePaths.SameSourceFile(anchor.ResultPath, lease.CallbackResultPath) ||
                !(lease.ProtectedSourcePaths ?? []).Any(path =>
                    SourceFilePaths.SameSourceFile(path, anchor.ResultPath)) ||
                anchorSlot.SelectedResult is not null)
                throw new SourceFileChangedException();
        }

        if (request.SharedLiveProfileTimelines is { Length: > 0 } && context.Map.ComputeReadLease is null)
            throw new SourceFileChangedException();
        var identity = new MapResultRenderSharedKeyframeIdentity(anchor.Node,
            anchor.SelectionIndex, SourceFilePaths.Canonical(anchor.ResultPath), anchor.Revision,
            anchor.RunID, anchor.ProfileIndex, anchor.ProfileName, anchor.Time);
        return new SharedKeyframeValidation(anchor, storedAnchor, identity);
    }

    private static MapResultRenderSharedLiveProfileMatch[] ResolveSharedLiveProfileMatches(
        MapResultRenderRequest request, ResolvedMapResultContext context,
        MapResultActiveNode targetNode, SharedKeyframeValidation sharedKeyframe,
        IReadOnlyCollection<int> blockedSelectionIndices)
    {
        MapResultRenderSharedLiveProfileTimeline[] timelines = request.SharedLiveProfileTimelines ?? [];
        if (timelines.Length == 0) return [];
        MapResultComputeReadLease lease = context.Map.ComputeReadLease ?? throw new SourceFileChangedException();
        if (timelines.Select(item => item.SelectionIndex).Distinct().Count() != timelines.Length)
            throw new SourceFileChangedException();

        var matches = new List<MapResultRenderSharedLiveProfileMatch>(timelines.Length);
        foreach (MapResultRenderSharedLiveProfileTimeline timeline in timelines)
        {
            MapResultSelectionState? selection = targetNode.Selections.SingleOrDefault(item =>
                item.SelectionIndex == timeline.SelectionIndex);
            if (!SameLocator(targetNode.Node, timeline.Node) ||
                timeline.SelectionIndex < 0 || timeline.SelectionIndex >= 32 ||
                !blockedSelectionIndices.Contains(timeline.SelectionIndex) || selection is null ||
                selection.SelectedResult is not null || !Path.IsPathFullyQualified(timeline.ResultPath) ||
                string.IsNullOrWhiteSpace(timeline.OutputBlockName) ||
                !Guid.TryParse(timeline.RunID, out Guid timelineRunID) ||
                !Guid.TryParse(lease.RunID, out Guid leaseRunID) || timelineRunID != leaseRunID ||
                string.IsNullOrWhiteSpace(lease.CallbackResultPath) ||
                !SourceFilePaths.SameSourceFile(timeline.ResultPath, lease.CallbackResultPath) ||
                !(lease.ProtectedSourcePaths ?? []).Any(path =>
                    SourceFilePaths.SameSourceFile(path, timeline.ResultPath)) ||
                timeline.ProfileCount < 0 || timeline.Profiles is null ||
                timeline.Profiles.Length > timeline.ProfileCount ||
                timeline.Profiles.Select(profile => profile.Index).Distinct().Count() != timeline.Profiles.Length ||
                timeline.Profiles.Any(profile => profile.Index < 0 || profile.Index >= timeline.ProfileCount))
                throw new SourceFileChangedException();

            bool isAnchorSource = sharedKeyframe.Anchor.RunID is not null &&
                SameLocator(sharedKeyframe.Anchor.Node, timeline.Node) &&
                sharedKeyframe.Anchor.SelectionIndex == timeline.SelectionIndex &&
                SourceFilePaths.SameSourceFile(sharedKeyframe.Anchor.ResultPath, timeline.ResultPath) &&
                string.Equals(sharedKeyframe.Anchor.RunID, timeline.RunID, StringComparison.OrdinalIgnoreCase);
            MapResultRenderSharedKeyframeProfile[] orderedProfiles = timeline.Profiles
                .OrderBy(profile => profile.Index).ToArray();
            int? matchedOrdinal = isAnchorSource ? null
                : MatchKeyedProfileName(sharedKeyframe.Anchor.ProfileName,
                    orderedProfiles.Select(profile => profile.Name).ToArray());
            int? matched = isAnchorSource ? sharedKeyframe.Anchor.ProfileIndex
                : matchedOrdinal is int ordinal && ordinal < orderedProfiles.Length
                    ? orderedProfiles[ordinal].Index : null;
            MapResultRenderSharedKeyframeProfile? profile = matched is int index
                ? timeline.Profiles.SingleOrDefault(item => item.Index == index)
                : null;
            string? unavailable = matched is null
                ? "This writer-owned Result has no published profile within the 10 millisecond timestamp tolerance."
                : profile is null || string.IsNullOrWhiteSpace(profile.Time)
                    ? "The matching writer-owned Result profile has no published time metadata."
                    : null;
            matches.Add(new MapResultRenderSharedLiveProfileMatch(timeline.Node,
                timeline.SelectionIndex, SourceFilePaths.Canonical(timeline.ResultPath),
                timeline.RunID, timeline.OutputBlockName,
                unavailable is null ? matched : null,
                unavailable is null ? profile?.Name : null,
                unavailable is null ? profile?.Time : null,
                unavailable));
        }
        return matches.ToArray();
    }

    private static int? MatchKeyedProfileName(string keyProfileName, IHorizontalOutputBlock child)
    {
        ValidateProfiles(child);
        return MatchKeyedProfileName(keyProfileName, child.ProfileNames.ToArray());
    }

    internal static int? MatchKeyedProfileName(string keyProfileName, IReadOnlyList<string> childProfileNames)
    {
        if (!DateTime.TryParse(keyProfileName, CultureInfo.CurrentCulture,
                DateTimeStyles.None, out DateTime target)) return null;

        for (int index = 0; index < childProfileNames.Count; index++)
        {
            if (!DateTime.TryParse(childProfileNames[index], CultureInfo.CurrentCulture,
                    DateTimeStyles.None, out DateTime childTime)) continue;
            long difference = Math.Abs(childTime.Ticks - target.Ticks);
            if (difference <= MultiResultTimestampToleranceTicks) return index;
        }
        return null;
    }

    private static MapResultRenderLayerPayload? RenderOne(Project project,
        ResolvedMapResultNode source, MapResultActiveNode activeNode, string mapID, string sourceID,
        MapTypes mapType, int profileIndex, RasterMapViewExtent viewport,
        TerrainSourceSnapshot terrainSnapshot, int[] selectionIndices,
        MapResultRenderOptions renderOptions, bool forceHorizontalRendering)
    {
        string layerIdentity = string.Join(",", selectionIndices);
        return RenderStoredResult(project, source.Result, source.ResultPath, source.Revision,
            mapID, sourceID, layerIdentity, mapType, profileIndex, viewport, terrainSnapshot,
            selectionIndices, renderOptions.VelocityDisplay, renderOptions.MapDiagnostics,
            renderOptions.ForceRecomputeMapPoints, forceHorizontalRendering);
    }

    /// One retained CPU render shared by persisted map nodes and the linked
    /// Result workspace. Callers own selection and whole-group identity checks.
    internal static MapResultRenderLayerPayload? RenderStoredResult(Project project,
        Result result, string resultPath, string resultRevision, string mapID, string sourceID,
        string layerIdentity, MapTypes mapType, int profileIndex, RasterMapViewExtent viewport,
        TerrainSourceSnapshot terrainSnapshot)
        => RenderStoredResult(project, result, resultPath, resultRevision, mapID, sourceID,
            layerIdentity, mapType, profileIndex, viewport, terrainSnapshot, [], "Magnitude", false, false, false);

    private static MapResultRenderLayerPayload? RenderStoredResult(Project project,
        Result result, string resultPath, string resultRevision, string mapID, string sourceID,
        string layerIdentity, MapTypes mapType, int profileIndex, RasterMapViewExtent viewport,
        TerrainSourceSnapshot terrainSnapshot, int[] selectionIndices, string velocityDisplay,
        bool mapDiagnostics, bool forceRecomputeMapPoints, bool forceHorizontalRendering)
    {
        ValidateViewport(viewport);
        IHorizontalOutputBlock outputBlock = result.BaseOutputBlock
            ?? throw new InvalidDataException("The selected Result has no base output block.");
        ValidateProfiles(outputBlock);
        bool isLegacyMaximum = outputBlock is LegacyOutputBlock && profileIndex == LegacyOutputBlock.MaxProfileIndex;
        if (!isLegacyMaximum && (profileIndex < 0 || profileIndex >= outputBlock.ProfileCount))
            throw new ArgumentOutOfRangeException(nameof(profileIndex), "The selected profile index is outside this Result's stored profiles.");
        Terrain terrain = result.AssociatedTerrain
            ?? throw new InvalidDataException("The selected Result has no current-project associated terrain.");
        MeshPropertyTables[] tables = result.Geometry?.FlowAreaLayer?.AllPropertyTables()
            ?? throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
        if (tables.Length == 0)
            throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");

        var renderLayer = new ResultMapRenderLayer(outputBlock, mapType, terrain, tables);
        renderLayer.ProfileState.CurrentProfileIndex = profileIndex;
        renderLayer.SetDefaultSymbology(project.Units);
        renderLayer.VelocityDisplay = velocityDisplay switch
        {
            "Magnitude" => ResultMapRenderLayer.VelocityDisplayMode.Magnitude,
            "Vx" => ResultMapRenderLayer.VelocityDisplayMode.Vx,
            "Vy" => ResultMapRenderLayer.VelocityDisplayMode.Vy,
            _ => throw new ArgumentOutOfRangeException(nameof(velocityDisplay))
        };
        renderLayer.MapDiagnostics = mapDiagnostics;
        renderLayer.ForceRecomputeMapPoints = forceRecomputeMapPoints;
        renderLayer.ForceHorizontalRendering = forceHorizontalRendering;

        Extent extent = new(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);
        var definition = new RasterDefinition(extent, viewport.PixelHeight, viewport.PixelWidth);
        if (!definition.IsValid() || definition.CellCount <= 0)
            throw new ArgumentException("The current map viewport cannot be sampled at the requested size.", nameof(viewport));

        EnsureUnchanged(resultPath, resultRevision);
        VerifyTerrainSources(terrainSnapshot);
        using var plot = new PlotRequest(definition) { CommonData = new CommonData() };
        // The retained renderer accepts an opaque cache and replaces an
        // unrecognized value with its own MapCache on the first render.
        object renderCache = new();
        bool rendered;
        try
        {
            rendered = renderLayer.Render(plot, ref renderCache);
            if (!rendered || plot.SplatCount == 0)
            {
                EnsureUnchanged(resultPath, resultRevision);
                VerifyTerrainSources(terrainSnapshot);
                return null;
            }

            int[] argb = plot.ColorBufferARGB.Slice(0, definition.CellCount).ToArray();
            byte[] rgba = new byte[checked(argb.Length * 4)];
            for (int index = 0; index < argb.Length; index++)
            {
                uint pixel = unchecked((uint)argb[index]);
                rgba[index * 4] = (byte)(pixel >> 16);
                rgba[index * 4 + 1] = (byte)(pixel >> 8);
                rgba[index * 4 + 2] = (byte)pixel;
                rgba[index * 4 + 3] = (byte)(pixel >> 24);
            }

            EnsureUnchanged(resultPath, resultRevision);
            VerifyTerrainSources(terrainSnapshot);

            string profileTime = isLegacyMaximum ? string.Empty : FormatTime(outputBlock.Times[profileIndex]);
            string profileName = isLegacyMaximum ? "Max" : outputBlock.ProfileNames[profileIndex];
            string resultTypeName = ResultMapRenderLayer.LayerName(mapType);
            string id = string.Join("|", sourceID, layerIdentity,
                SourceFilePaths.Canonical(resultPath), mapType.ToString(), velocityDisplay,
                mapDiagnostics ? "diagnostics" : "no-diagnostics",
                forceHorizontalRendering ? "force-horizontal" : "source-horizontal");
            MapResultRenderDiagnosticPoint[] labels = mapDiagnostics && mapType == MapTypes.Velocity
                ? (plot.Labels ?? []).Select(label => new MapResultRenderDiagnosticPoint(
                    label.Text, label.PointLocation.X, label.PointLocation.Y)).ToArray()
                : [];
            if (labels.Any(label => string.IsNullOrWhiteSpace(label.Text) ||
                    !double.IsFinite(label.X) || !double.IsFinite(label.Y)))
                throw new InvalidDataException("The source Result renderer returned an invalid diagnostic point.");
            return new MapResultRenderLayerPayload(id, sourceID, mapID,
                resultPath, resultRevision, result.Name, resultTypeName,
                terrainSnapshot.Path, terrainSnapshot.Revision, profileIndex, profileName, profileTime,
                LayerUnits(project.Units, mapType), definition.Cols, definition.Rows, rgba,
                definition.MinX, definition.MinY, definition.MaxX, definition.MaxY, NoDataValue,
                renderLayer.RasterSymbology.SurfaceFill?.Colors.Select((color, index) =>
                    new MapResultRenderLegendStop(EncodeColor(color),
                        renderLayer.RasterSymbology.SurfaceFill.ColorValues[index])).ToArray() ?? [],
                selectionIndices, velocityDisplay, mapDiagnostics,
                forceRecomputeMapPoints, labels, forceHorizontalRendering);
        }
        finally
        {
            plot.Clear();
            (renderCache as IDisposable)?.Dispose();
        }
    }

    internal static string LayerUnits(UnitSystem units, MapTypes mapType)
    {
        DefaultUnits defaults = DefaultUnits.FromDefault(units);
        return mapType switch
        {
            MapTypes.Depth or MapTypes.WaterSurfaceElevation => defaults.ShortLengthUnits,
            MapTypes.Velocity => defaults.ShortVelocityUnits,
            MapTypes.DepthTimesVelocitySquared => units == UnitSystem.SI ? "m³/s²" : "ft³/s²",
            _ => string.Empty
        };
    }

    private static string EncodeColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    internal static MapTypes? ParseSupportedMapType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string candidate = value.Trim();
        MapTypes parsed = candidate switch
        {
            "Water Surface" => MapTypes.WaterSurfaceElevation,
            "D*V" => MapTypes.DepthTimesVelocity,
            "D*V^2" => MapTypes.DepthTimesVelocitySquared,
            _ when Enum.TryParse(candidate, ignoreCase: true, out MapTypes enumValue) => enumValue,
            _ => MapTypes.Unknown
        };
        return parsed is MapTypes.WaterSurfaceElevation or MapTypes.Depth or
            MapTypes.Velocity or MapTypes.DepthTimesVelocity or MapTypes.DepthTimesVelocitySquared ? parsed : null;
    }

    internal static int? MatchKeyedProfile(IHorizontalOutputBlock key,
        int keyProfileIndex, IHorizontalOutputBlock child)
    {
        if (keyProfileIndex < 0 || keyProfileIndex >= key.ProfileCount) return null;
        ValidateProfiles(key);
        ValidateProfiles(child);
        if (!DateTime.TryParse(key.ProfileNames[keyProfileIndex], CultureInfo.CurrentCulture,
                DateTimeStyles.None, out DateTime target)) return null;

        int nearestIndex = -1;
        long closestTicks = long.MaxValue;
        for (int index = 0; index < child.ProfileCount; index++)
        {
            if (!DateTime.TryParse(child.ProfileNames[index], CultureInfo.CurrentCulture,
                    DateTimeStyles.None, out DateTime childTime)) continue;
            long difference = Math.Abs(childTime.Ticks - target.Ticks);
            if (difference < closestTicks)
            {
                closestTicks = difference;
                nearestIndex = index;
            }
            if (difference <= MultiResultTimestampToleranceTicks) return index;
        }
        return null;
    }

    internal static void ValidateProfiles(IHorizontalOutputBlock block)
    {
        if (block.ProfileNames is null || block.Times is null ||
            block.ProfileCount != block.Times.Count || block.ProfileCount != block.ProfileNames.Count)
            throw new InvalidDataException("The Result output block has inconsistent profile names and timestamps.");
    }

    internal static TerrainSourceSnapshot CaptureTerrainSources(Terrain terrain)
    {
        if (string.IsNullOrWhiteSpace(terrain.Filename) || !File.Exists(terrain.Filename))
            throw new FileNotFoundException("The associated terrain source file was not found.", terrain.Filename);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddFile(terrain.Filename);
        foreach (RasterTerrainInput raster in terrain.Layers.OfType<RasterTerrainInput>())
        {
            if (!raster.IsValid(out string error))
                throw new FileNotFoundException(error, raster.Filename);
            string rasterPath = SourceFilePaths.Canonical(raster.Filename);
            string extension = Path.GetExtension(rasterPath);
            if (!extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("VRT and non-TIFF terrain raster inputs are not supported because their external dependencies cannot be tracked. Convert the source to a single-file GeoTIFF and reconnect it.");
            AddFile(rasterPath);
            AddOptionalSidecar(rasterPath + ".aux.xml");
            AddOptionalSidecar(Path.ChangeExtension(rasterPath, ".ovr"));
        }

        string canonicalTerrainPath = SourceFilePaths.Canonical(terrain.Filename);
        return new TerrainSourceSnapshot(canonicalTerrainPath, DigestDependencies(files), files);

        void AddFile(string path)
        {
            string canonical = SourceFilePaths.Canonical(path);
            if (!File.Exists(canonical)) throw new FileNotFoundException("An associated terrain source file was not found.", canonical);
            files[canonical] = RevisionFile(canonical);
        }

        void AddOptionalSidecar(string path)
        {
            if (File.Exists(path)) AddFile(path);
        }
    }

    private static string DigestDependencies(SortedDictionary<string, string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string path, string revision) in files)
        {
            byte[] entry = Encoding.UTF8.GetBytes(path + "\0" + revision + "\n");
            hash.AppendData(entry);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static void VerifyTerrainSources(TerrainSourceSnapshot snapshot)
    {
        foreach ((string path, string expectedRevision) in snapshot.Files)
            EnsureUnchanged(path, expectedRevision);
        string current = DigestDependencies(new SortedDictionary<string, string>(snapshot.Files, StringComparer.Ordinal));
        if (!string.Equals(current, snapshot.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    internal static int[] SelectionIndicesFor(ResolvedMapResultNode source, CatalogTreeNodeLocator node) =>
        source.Selections.Where(selection => SameLocator(selection.Node, node))
            .Select(selection => selection.SelectionIndex).Distinct().Order().ToArray();

    internal static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(left.RootName, right.RootName, StringComparison.Ordinal) &&
        string.Equals(left.NodeType, right.NodeType, StringComparison.Ordinal) &&
        string.Equals(left.NodeName, right.NodeName, StringComparison.Ordinal) &&
        (left.NodePath ?? []).SequenceEqual(right.NodePath ?? []) &&
        string.Equals(left.AssetID, right.AssetID, StringComparison.Ordinal) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        string.Equals(left.Filename, right.Filename, StringComparison.Ordinal) &&
        string.Equals(left.FilenameRelative, right.FilenameRelative, StringComparison.Ordinal) &&
        string.Equals(left.ParentBoundaryConditionAssetID, right.ParentBoundaryConditionAssetID, StringComparison.Ordinal) &&
        (left.ParentBoundaryConditionNodePath ?? []).SequenceEqual(right.ParentBoundaryConditionNodePath ?? []) &&
        string.Equals(left.ParentBoundaryConditionNodeName, right.ParentBoundaryConditionNodeName, StringComparison.Ordinal);

    internal static string NodeIdentity(CatalogTreeNodeLocator node)
    {
        // This is the exact wire identity used by Swift's
        // CatalogTreeNodeLocator.sourceIdentity. Keep the full field order,
        // empty tokens for nil fields, UTF-8 byte counts and separators in
        // sync so Result rendering can reject a response for another node.
        string[] components = [node.RootName, node.NodeType, node.NodeName,
            string.Join('.', node.NodePath ?? []), node.AssetID ?? string.Empty,
            node.MapType ?? string.Empty, node.Filename ?? string.Empty,
            node.FilenameRelative ?? string.Empty,
            node.ParentGeometryAssetID ?? string.Empty,
            string.Join('.', node.ParentGeometryNodePath ?? []),
            node.ParentGeometryNodeName ?? string.Empty,
            node.ParentPlanAssetID ?? string.Empty,
            string.Join('.', node.ParentPlanNodePath ?? []),
            node.ParentPlanNodeName ?? string.Empty,
            node.ParentResultNodeType ?? string.Empty,
            node.ParentResultAssetID ?? string.Empty,
            string.Join('.', node.ParentResultNodePath ?? []),
            node.ParentResultNodeName ?? string.Empty,
            node.ParentResultRevision ?? string.Empty,
            node.AssociationAssetID ?? string.Empty,
            node.AssociationKind ?? string.Empty];
        if (node.ParentBoundaryConditionAssetID is not null ||
            node.ParentBoundaryConditionNodePath is not null ||
            node.ParentBoundaryConditionNodeName is not null)
        {
            components = [.. components,
                node.ParentBoundaryConditionAssetID ?? string.Empty,
                string.Join('.', node.ParentBoundaryConditionNodePath ?? []),
                node.ParentBoundaryConditionNodeName ?? string.Empty];
        }
        return string.Join("|", components.Select(component =>
            Encoding.UTF8.GetByteCount(component).ToString(CultureInfo.InvariantCulture) + ":" + component));
    }

    internal static RasterMapViewExtent DefaultViewport(Extent extent)
    {
        if (!extent.IsFiniteAndValid())
            throw new InvalidDataException("The embedded Result geometry has no finite extent.");
        double ratio = extent.Width / extent.Height;
        int width = Math.Clamp((int)Math.Round(1024d * Math.Sqrt(ratio)), 1, MaximumRasterSide);
        int height = Math.Clamp((int)Math.Round(1024d / Math.Sqrt(ratio)), 1, MaximumRasterSide);
        return new RasterMapViewExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY, width, height);
    }

    internal static void ValidateViewport(RasterMapViewExtent viewport)
    {
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY ||
            viewport.PixelWidth < 1 || viewport.PixelHeight < 1 ||
            viewport.PixelWidth > MaximumRasterSide || viewport.PixelHeight > MaximumRasterSide)
            throw new ArgumentException("The map result viewport must have finite ordered bounds and pixel dimensions between 1 and 1536.", nameof(viewport));
    }

    internal static RasterMapViewExtent ConstrainRasterViewport(RasterMapViewExtent viewport, int layerCount)
    {
        ValidateViewport(viewport);
        if (layerCount is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(layerCount));
        long perLayerPixels = MaximumAggregateRasterPixels / layerCount;
        long pixels = (long)viewport.PixelWidth * viewport.PixelHeight;
        if (pixels <= perLayerPixels) return viewport;
        double scale = Math.Sqrt((double)perLayerPixels / pixels);
        int width = Math.Max(1, (int)Math.Floor(viewport.PixelWidth * scale));
        int height = Math.Max(1, (int)Math.Floor(viewport.PixelHeight * scale));
        return viewport with { PixelWidth = width, PixelHeight = height };
    }

    internal static string FormatTime(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string RevisionFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void EnsureUnchanged(string path, string revision)
    {
        if (!File.Exists(path) || !string.Equals(RevisionFile(path), revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    internal sealed record TerrainSourceSnapshot(string Path, string Revision,
        SortedDictionary<string, string> Files);
}
