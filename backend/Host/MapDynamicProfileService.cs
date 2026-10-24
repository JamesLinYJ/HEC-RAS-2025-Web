using System.Buffers.Binary;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using H5Assist;
using H5Assist.Caching;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Hydraulics;
using Ras.Mapping;
using Ras.Output;
using Ras.Rendering;
using Utility.Files;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Samples active map terrain and Result contexts along the retained dynamic
/// profile polyline contract. Source files are read-only and revision checked
/// before and after sampling.
/// </summary>
public static class MapDynamicProfileService
{
    private const int MaximumLines = 64;
    private const int MaximumProfileSelections = 512;
    private const int MaximumInputPointsPerLine = 10_000;
    private const int MaximumTargetSampleCount = 4096;
    private const int MaximumTotalSourcePoints = 100_000;
    private const int MaximumTotalDensifiedPoints = 524_288;
    private const long MaximumTotalSeriesSamples = 4_000_000;
    private const float NoDataValue = -9999f;
    private const string CoordinateHashDomain = "HECRAS-MAP-LINE-v1\0";

    public static MapDynamicProfileResult Sample(MapDynamicProfileRequest request) => SampleCore(request, null);

    // Hover belongs to one exact visible node. The chart catalog deduplicates
    // shared Result objects across individual and MultiResult nodes; rebind
    // that shared source to the pointer's node before resolving its timeline.
    internal static MapDynamicProfileResult SampleForMapNode(MapDynamicProfileRequest request,
        CatalogTreeNodeLocator resultNode) => SampleCore(request, resultNode);

    private static MapDynamicProfileResult SampleCore(MapDynamicProfileRequest request,
        CatalogTreeNodeLocator? resultNode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ValidateRequest(request);

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.SelectionOverrides, request.ContextToken);
        ValidateLinkedFrameMode(request.LinkedResults, request.LinkedFrameMode);
        LinkedResultProfilesSnapshot? linkedResults = request.LinkedResults is null
            ? null
            : ResolveLinkedResults(context, request.LinkedResults, request.ProfileSelections);
        VectorSourceSnapshot[] vectorSources = ValidateLineFeatures(context, request.Lines);
        Dictionary<string, int> profileSelections = ValidateProfileSelections(request.ProfileSelections,
            context.Sources.ActiveNodes);
        MapTypes? requestedMapType = ParseRequestedMapType(request.MapType);
        MapTypes? linkedMapType = linkedResults is null
            ? null
            : requestedMapType ?? context.ResultContexts.Select(source =>
                MapResultRenderService.ParseSupportedMapType(source.SelectedMapType))
                .FirstOrDefault(value => value is not null) ?? MapTypes.Depth;
        PreparedLine[] lines = PrepareLines(request.Lines, request.TargetSampleCount);

        MapDynamicProfileActiveTerrain[] activeTerrains = request.PlotTerrains &&
            (!request.TerrainRequiresElevationResult || requestedMapType == MapTypes.WaterSurfaceElevation)
            ? ResolveActiveTerrains(context)
            : [];
        ResolvedMapResultNode[] activeResults = request.PlotResults && linkedResults is null
            ? ResolveActiveResults(context, requestedMapType, resultNode)
            : [];
        LinkedResultProfileFrame[] linkedFrames = request.PlotResults && linkedResults is not null
            ? linkedResults.Frames
            : [];
        ValidateResponseSize(lines, activeTerrains.Length + Math.Max(activeResults.Length, linkedFrames.Length));

        var results = new MapDynamicProfileLineResult[lines.Length];
        var terrainSnapshots = new Dictionary<string, TrackedTerrainSnapshot>(PathComparer);
        MapDynamicProfileActiveTerrainState[] terrainStates = activeTerrains.Select(activeTerrain =>
        {
            MapResultRenderService.TerrainSourceSnapshot? snapshot = GetOrCaptureTerrain(
                activeTerrain.Terrain, activeTerrain.Path, terrainSnapshots, out string? reason);
            return new MapDynamicProfileActiveTerrainState(activeTerrain, snapshot, reason);
        }).ToArray();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            PreparedLine line = lines[lineIndex];
            var series = new List<MapDynamicProfileSeries>(terrainStates.Length +
                Math.Max(activeResults.Length, linkedFrames.Length));
            foreach (MapDynamicProfileActiveTerrainState terrainState in terrainStates)
            {
                MapDynamicProfileActiveTerrain activeTerrain = terrainState.ActiveTerrain;
                string seriesID = SeriesIdentity(line.Id, "Terrain", activeTerrain.Path, null, null);
                string? unavailableReason = terrainState.UnavailableReason;
                double?[] samples = unavailableReason is null
                    ? SampleTerrain(activeTerrain.Terrain, line.Points, terrainState.Snapshot!,
                        out unavailableReason)
                    : NullSamples(line.Points.Length);
                series.Add(new MapDynamicProfileSeries(seriesID, line.Id, "Terrain",
                    terrainState.Snapshot?.Path ?? activeTerrain.Path,
                    terrainState.Snapshot?.Revision ?? string.Empty,
                    activeTerrain.Terrain.Name, MapTypes.WaterSurfaceElevation.ToString(), null, null,
                    MapResultRenderService.LayerUnits(context.Project.Units, MapTypes.WaterSurfaceElevation),
                    samples, UnavailableReason: unavailableReason));
            }

            if (linkedResults is not null)
            {
                foreach (LinkedResultProfileFrame frame in linkedFrames)
                {
                    Result result = ResolveLinkedResult(context, frame);
                    series.Add(SampleLinkedResult(context, frame, result, line, linkedMapType,
                        request.LinkedFrameMode, terrainSnapshots));
                }
            }
            else
            {
                foreach (ResolvedMapResultNode source in activeResults)
                    series.Add(SampleResult(context, source, line, requestedMapType, profileSelections,
                        terrainSnapshots, chartFriendly: resultNode is null));
            }

            results[lineIndex] = new MapDynamicProfileLineResult(line.Id, line.OriginalPoints,
                line.SamplePoints, line.Distances, series.ToArray());
        }

        context.EnsureUnchanged();
        if (linkedResults is not null)
            foreach (LinkedResultProfileFrame frame in linkedResults.Frames)
                MapResultChartService.EnsureUnchanged(frame.ResultPath, frame.Revision);
        foreach (TrackedTerrainSnapshot snapshot in terrainSnapshots.Values)
            VerifyTerrainSnapshot(snapshot.Terrain, snapshot.Snapshot);
        foreach (VectorSourceSnapshot source in vectorSources)
            MapResultChartService.EnsureUnchanged(source.Path, source.Revision);

        return new MapDynamicProfileResult(context.Sources.ProjectPath, context.Sources.ProjectRevision,
            context.Sources.ProjectXmlSHA, context.Sources.VisualizationRevision,
            context.Sources.MapID, context.Sources.MapIndex, context.Sources.MapName,
            context.Sources.MapType,
            DistanceUnits(context.Project.Units),
            context.Sources.ContextToken, request.LinesToken,
            request.TargetSampleCount, results);
    }

    internal static string DistanceUnits(UnitSystem units) =>
        units == UnitSystem.USCustomary ? "ft" : "m";

    private static void ValidateRequest(MapDynamicProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContextToken) || request.ContextToken.Length > 512)
            throw new ArgumentException("Refresh the active map profile context before sampling.", nameof(request.ContextToken));
        if (string.IsNullOrWhiteSpace(request.LinesToken) || request.LinesToken.Length > 512)
            throw new ArgumentException("The profile line snapshot token is required.", nameof(request.LinesToken));
        if (request.TargetSampleCount is < 1 or > MaximumTargetSampleCount)
            throw new ArgumentOutOfRangeException(nameof(request.TargetSampleCount),
                $"Target sample count must be between 1 and {MaximumTargetSampleCount}.");
        if (request.Lines is null || request.Lines.Length is < 1 or > MaximumLines)
            throw new ArgumentException($"A dynamic profile request requires between 1 and {MaximumLines} lines.", nameof(request.Lines));

        int totalPoints = 0;
        var lineIDs = new HashSet<string>(StringComparer.Ordinal);
        foreach (MapDynamicProfileLine line in request.Lines)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.Id) || line.Id.Length > 256 ||
                !lineIDs.Add(line.Id))
                throw new ArgumentException("Profile line IDs must be nonempty, unique, and at most 256 characters.", nameof(request.Lines));
            if (line.Points is null || line.Points.Length is < 2 or > MaximumInputPointsPerLine)
                throw new ArgumentException($"Each profile line must contain between 2 and {MaximumInputPointsPerLine} points.", nameof(request.Lines));
            totalPoints = checked(totalPoints + line.Points.Length);
            if (totalPoints > MaximumTotalSourcePoints)
                throw new ArgumentException($"Profile lines may contain at most {MaximumTotalSourcePoints} points in total.", nameof(request.Lines));
            if (line.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("Profile line coordinates must be finite.", nameof(request.Lines));
        }
    }

    private static Dictionary<string, int> ValidateProfileSelections(
        MapDynamicProfileSelection[]? selections, IReadOnlyList<MapResultActiveNode> activeNodes)
    {
        if (selections is { Length: > MaximumProfileSelections })
            throw new ArgumentException($"A dynamic profile request may select at most {MaximumProfileSelections} map profiles.", nameof(selections));
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MapDynamicProfileSelection selection in selections ?? [])
        {
            if (selection is null || selection.Node is null ||
                (selection.ProfileIndex < 0 && selection.ProfileIndex != LegacyOutputBlock.MinProfileIndex))
                throw new ArgumentException("Each selected map profile requires an exact node locator and a stored index or Legacy summary sentinel.", nameof(selections));
            MapResultActiveNode[] matches = activeNodes.Where(node =>
                MapResultRenderService.SameLocator(node.Node, selection.Node)).ToArray();
            if (matches.Length != 1)
                throw new SourceFileChangedException();
            string identity = MapResultRenderService.NodeIdentity(selection.Node);
            if (!result.TryAdd(identity, selection.ProfileIndex))
                throw new ArgumentException("A map node cannot have multiple selected profile indices.", nameof(selections));
        }
        return result;
    }

    private static VectorSourceSnapshot[] ValidateLineFeatures(ResolvedMapResultContext context,
        MapDynamicProfileLine[] lines)
    {
        var sources = new List<VectorSourceSnapshot>();
        foreach (MapDynamicProfileLine line in lines)
        {
            if (line.Feature is not { } feature) continue;
            if (string.IsNullOrWhiteSpace(feature.VectorPath) || !Path.IsPathFullyQualified(feature.VectorPath))
                throw new ArgumentException("Choose a saved vector source from this map.", nameof(line.Feature));
            if (string.IsNullOrWhiteSpace(feature.VectorRevision) || feature.VectorRevision.Length != 64 ||
                string.IsNullOrWhiteSpace(feature.FeatureID) || feature.FeatureID.Length > 1024 ||
                feature.FeatureID.Any(char.IsControl) ||
                !string.Equals(feature.FeatureID, line.Id, StringComparison.Ordinal))
                throw new ArgumentException("The selected map feature identity does not match the profile line.", nameof(line.Feature));

            string vectorPath = SourceFilePaths.Canonical(feature.VectorPath);
            if (!File.Exists(vectorPath))
                throw new FileNotFoundException("The map vector source was not found.", vectorPath);
            MapResultChartService.EnsureUnchanged(vectorPath, feature.VectorRevision);
            bool member = Flatten(context.Tree.Roots).Any(node =>
                node.Locator is { NodeType: "MapLayer" } locator &&
                locator.MapType == "PolylinesVector" &&
                !string.IsNullOrWhiteSpace(node.AssetID) &&
                SourceFilePaths.SameSourceFile(node.AssetID, vectorPath));
            if (!member)
                throw new ArgumentException("The vector source is not a polyline layer in the selected map.", nameof(line.Feature));

            string coordinateSHA = HashCoordinates(line.Points);
            if (string.IsNullOrWhiteSpace(feature.CoordinateSHA) ||
                !string.Equals(feature.CoordinateSHA, coordinateSHA, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The profile line coordinates do not match the selected map feature identity.", nameof(line.Feature));
            sources.Add(new VectorSourceSnapshot(vectorPath, feature.VectorRevision));
        }
        return sources.DistinctBy(source => source.Path, PathComparer).ToArray();
    }

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

    private static LinkedResultProfilesSnapshot ResolveLinkedResults(ResolvedMapResultContext context,
        LinkedResultProfilesRequest request, MapDynamicProfileSelection[]? profileSelections)
    {
        if (profileSelections is { Length: > 0 })
            throw new ArgumentException("Linked Results carry their own exact profile indices; do not also submit base-map profile selections.", nameof(profileSelections));
        string projectXmlSHA = Hash(Encoding.UTF8.GetBytes(request.ProjectXml ?? string.Empty));
        if (!PathComparer.Equals(SourceFilePaths.Canonical(request.ProjectPath),
                SourceFilePaths.Canonical(context.Sources.ProjectPath)) ||
            !string.Equals(request.ProjectRevision, context.Sources.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(projectXmlSHA, context.Sources.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        LinkedResultProfilesSnapshot linked = LinkedResultProfilesService.Read(request);
        if (!PathComparer.Equals(SourceFilePaths.Canonical(linked.ProjectPath),
                SourceFilePaths.Canonical(context.Sources.ProjectPath)) ||
            !string.Equals(linked.ProjectRevision, context.Sources.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(linked.AnchorPath),
                SourceFilePaths.Canonical(request.Anchor.ResultPath)) ||
            linked.ProfileIndex != request.ProfileIndex)
            throw new SourceFileChangedException();

        foreach (LinkedResultProfileFrame frame in linked.Frames)
            _ = ResolveLinkedResult(context, frame);
        return linked;
    }

    private static void ValidateLinkedFrameMode(LinkedResultProfilesRequest? linkedResults, string frameMode)
    {
        if (frameMode is not ("Profile" or "Maximum" or "Minimum"))
            throw new ArgumentException("Choose Profile, Maximum, or Minimum for the linked Result frame.", nameof(frameMode));
        if (linkedResults is null && frameMode != "Profile")
            throw new ArgumentException("A linked Result frame mode requires a Linked Results request.", nameof(frameMode));
    }

    private static Result ResolveLinkedResult(ResolvedMapResultContext context,
        LinkedResultProfileFrame frame)
    {
        string path = SourceFilePaths.Canonical(frame.ResultPath);
        Result[] matches = context.Project.Results.Where(result => !string.IsNullOrWhiteSpace(result.Filename) &&
            PathComparer.Equals(ProjectAssetPath(context.Project, result.Filename), path)).ToArray();
        if (matches.Length != 1 || matches[0].IsInMemory ||
            !PathComparer.Equals(ProjectAssetPath(context.Project, matches[0].Filename), path))
            throw new SourceFileChangedException();
        MapResultChartService.EnsureUnchanged(path, frame.Revision);
        return matches[0];
    }

    private static MapTypes? ParseRequestedMapType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        MapTypes? parsed = MapResultRenderService.ParseSupportedMapType(value);
        if (parsed is MapTypes.WaterSurfaceElevation or MapTypes.Depth or MapTypes.Velocity or
            MapTypes.DepthTimesVelocitySquared)
            return parsed;
        throw new ArgumentException("Dynamic profiles support WaterSurfaceElevation, Depth, Velocity, or DepthTimesVelocitySquared.", nameof(value));
    }

    private static PreparedLine[] PrepareLines(MapDynamicProfileLine[] sourceLines, int targetSampleCount)
    {
        var result = new PreparedLine[sourceLines.Length];
        int totalDensifiedPoints = 0;
        for (int lineIndex = 0; lineIndex < sourceLines.Length; lineIndex++)
        {
            MapDynamicProfileLine source = sourceLines[lineIndex];
            var polyline = new Polyline(source.Points
                .Select(point => new Point(point.X, point.Y)).ToList());
            double length = polyline.Length;
            if (!double.IsFinite(length) || length <= 0)
                throw new ArgumentException("Every profile line must have a finite, nonzero length.", nameof(sourceLines));

            // Matches DynamicProfilePlot.ProfileLine exactly: the requested
            // count determines target spacing, while original vertices remain.
            polyline.Densify(length / targetSampleCount);
            Point[] points = polyline.Points.ToArray();
            if (points.Length < 2 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new InvalidDataException("The retained polyline densifier produced invalid profile points.");
            totalDensifiedPoints = checked(totalDensifiedPoints + points.Length);
            if (totalDensifiedPoints > MaximumTotalDensifiedPoints)
                throw new ArgumentException($"Densified profile lines may contain at most {MaximumTotalDensifiedPoints} points in total.", nameof(sourceLines));

            var distances = new double[points.Length];
            double distance = 0;
            for (int index = 1; index < points.Length; index++)
            {
                distance += Point.Distance(points[index - 1].X, points[index - 1].Y,
                    points[index].X, points[index].Y);
                if (!double.IsFinite(distance))
                    throw new InvalidDataException("The profile line cumulative distance is not finite.");
                distances[index] = distance;
            }

            GeometryTopologyCoordinate[] samplePoints = points
                .Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();
            result[lineIndex] = new PreparedLine(source.Id, source.Points.ToArray(), points,
                samplePoints, distances);
        }
        return result;
    }

    private static MapDynamicProfileActiveTerrain[] ResolveActiveTerrains(ResolvedMapResultContext context)
    {
        CatalogTreeNode[] nodes = Flatten(context.Tree.Roots)
            .Where(node => node.Locator?.NodeType == "Terrain" && node.Checked == "checked")
            .ToArray();
        var byPath = new Dictionary<string, MapDynamicProfileActiveTerrain>(PathComparer);
        foreach (CatalogTreeNode node in nodes)
        {
            string sourcePath = node.Locator?.AssetID ?? node.AssetID
                ?? throw new SourceFileChangedException();
            string canonicalPath = ProjectAssetPath(context.Project, sourcePath);
            Terrain[] matches = context.Project.Terrains.Where(terrain =>
                PathComparer.Equals(ProjectAssetPath(context.Project, terrain.Filename), canonicalPath)).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            if (!byPath.TryAdd(canonicalPath, new MapDynamicProfileActiveTerrain(matches[0], canonicalPath)))
            {
                // The retained map chart stores active visual contexts in a
                // HashSet; duplicate references to the same terrain are one series.
                continue;
            }
        }
        return byPath.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    private static string ProjectAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(project.ProjectDirectory, filename);
        return SourceFilePaths.Canonical(path);
    }

    private static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (CatalogTreeNode child in Flatten(node.Children)) yield return child;
        }
    }

    private static MapResultRenderService.TerrainSourceSnapshot? GetOrCaptureTerrain(Terrain terrain,
        string expectedPath, IDictionary<string, TrackedTerrainSnapshot> snapshots,
        out string? unavailableReason)
    {
        if (snapshots.TryGetValue(expectedPath, out TrackedTerrainSnapshot? existing))
        {
            VerifyTerrainSnapshot(existing.Terrain, existing.Snapshot);
            unavailableReason = null;
            return existing.Snapshot;
        }
        try
        {
            MapResultRenderService.TerrainSourceSnapshot snapshot =
                MapResultRenderService.CaptureTerrainSources(terrain);
            if (!PathComparer.Equals(snapshot.Path, expectedPath))
                throw new SourceFileChangedException();
            snapshots.Add(expectedPath, new TrackedTerrainSnapshot(terrain, snapshot));
            unavailableReason = null;
            return snapshot;
        }
        catch (FileNotFoundException exception)
        {
            unavailableReason = exception.Message;
            return null;
        }
        catch (NotSupportedException exception)
        {
            unavailableReason = exception.Message;
            return null;
        }
    }

    private static double?[] SampleTerrain(Terrain terrain, Point[] points,
        MapResultRenderService.TerrainSourceSnapshot snapshot, out string? unavailableReason)
    {
        var values = new float[points.Length];
        try
        {
            VerifyTerrainSnapshot(terrain, snapshot);
            terrain.SamplePoints(points.AsMemory(), values.AsMemory());
            VerifyTerrainSnapshot(terrain, snapshot);
            unavailableReason = null;
            return ConvertSamples(values, depthNoDataIsZero: false);
        }
        catch (FileNotFoundException exception)
        {
            unavailableReason = exception.Message;
            return NullSamples(points.Length);
        }
    }

    private static void VerifyTerrainSnapshot(Terrain terrain,
        MapResultRenderService.TerrainSourceSnapshot snapshot)
    {
        MapResultRenderService.VerifyTerrainSources(snapshot);
        MapResultRenderService.TerrainSourceSnapshot current =
            MapResultRenderService.CaptureTerrainSources(terrain);
        if (!PathComparer.Equals(current.Path, snapshot.Path) ||
            !string.Equals(current.Revision, snapshot.Revision, StringComparison.OrdinalIgnoreCase) ||
            !current.Files.SequenceEqual(snapshot.Files))
            throw new SourceFileChangedException();
    }

    private static ResolvedMapResultNode[] ResolveActiveResults(ResolvedMapResultContext context,
        MapTypes? requestedMapType, CatalogTreeNodeLocator? resultNode)
    {
        MapResultActiveNode? activeNode = null;
        if (resultNode is not null)
        {
            MapResultActiveNode[] matches = context.Sources.ActiveNodes.Where(node =>
                MapResultRenderService.SameLocator(node.Node, resultNode)).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            activeNode = matches[0];
        }
        var seen = new HashSet<(Result Result, string MapType)>();
        var results = new List<ResolvedMapResultNode>();
        foreach (ResolvedMapResultNode source in context.ResultContexts)
        {
            int[] selectionIndices = resultNode is null ? source.SelectionIndices
                : MapResultRenderService.SelectionIndicesFor(source, resultNode);
            if (resultNode is not null && selectionIndices.Length == 0) continue;
            string mapType = requestedMapType?.ToString()
                ?? MapResultRenderService.ParseSupportedMapType(source.SelectedMapType)?.ToString()
                ?? source.SelectedMapType ?? string.Empty;
            if (seen.Add((source.Result, mapType))) results.Add(activeNode is null ? source : source with
            {
                ActiveNode = activeNode,
                SelectionIndex = selectionIndices[0],
                SelectionIndices = selectionIndices
            });
        }
        return results.ToArray();
    }

    private static MapDynamicProfileSeries SampleResult(ResolvedMapResultContext context,
        ResolvedMapResultNode source, PreparedLine line, MapTypes? requestedMapType,
        IReadOnlyDictionary<string, int> profileSelections,
        IDictionary<string, TrackedTerrainSnapshot> terrainSnapshots, bool chartFriendly)
    {
        CatalogTreeNodeLocator node = source.ActiveNode.Node;
        int selectedKeyProfile = profileSelections.GetValueOrDefault(
            MapResultRenderService.NodeIdentity(node), source.ActiveNode.ProfileIndex ?? 0);
        int[] selectionIndices = MapResultRenderService.SelectionIndicesFor(source, node);
        MapTypes? mapType = requestedMapType ?? MapResultRenderService.ParseSupportedMapType(source.SelectedMapType);
        string? unavailableReason = null;
        string resultPath = SourceFilePaths.Canonical(source.ResultPath);
        string? associatedTerrainPath = null;
        string? associatedTerrainRevision = null;
        int? resultProfileIndex = null;
        string? time = null;
        double?[] samples = NullSamples(line.Points.Length);

        IHorizontalOutputBlock? outputBlock = source.Result.BaseOutputBlock;
        Terrain? terrain = source.Result.AssociatedTerrain;
        MapResultRenderService.TerrainSourceSnapshot? terrainSnapshot = null;
        if (outputBlock is null || outputBlock.ProfileCount <= 0)
        {
            unavailableReason = "This Result has no stored horizontal output profiles.";
        }
        else if (selectedKeyProfile == LegacyOutputBlock.MinProfileIndex)
        {
            resultProfileIndex = LegacyOutputBlock.MinProfileIndex;
            unavailableReason = "The retained Legacy Result profile sampler exposes Maximum summary values, but does not read Minimum summary datasets.";
        }
        else if (selectedKeyProfile == LegacyOutputBlock.MaxProfileIndex && outputBlock is not LegacyOutputBlock)
        {
            unavailableReason = "Legacy Maximum summary sampling is unavailable for this non-Legacy output block.";
        }
        else if (selectedKeyProfile == LegacyOutputBlock.MaxProfileIndex && outputBlock is LegacyOutputBlock &&
                 mapType is MapTypes summaryType && !HasLegacyMaximumDatasets(source.Result, summaryType))
        {
            resultProfileIndex = LegacyOutputBlock.MaxProfileIndex;
            unavailableReason = "This Legacy Result has no valid stored Maximum summary datasets for the selected map type.";
        }
        else if (mapType is not (MapTypes.WaterSurfaceElevation or MapTypes.Depth or MapTypes.Velocity or
                     MapTypes.DepthTimesVelocitySquared))
        {
            unavailableReason = "The selected Result map type is not supported by the current map renderer.";
        }
        else if (terrain is null)
        {
            unavailableReason = "The selected Result has no current-project associated terrain.";
        }
        else
        {
            MapResultRenderService.ValidateProfiles(outputBlock);
            int profileIndex = selectedKeyProfile;
            bool legacyMaximum = profileIndex == LegacyOutputBlock.MaxProfileIndex &&
                outputBlock is LegacyOutputBlock;
            if (legacyMaximum)
            {
                // LegacyOutputBlock routes int.MaxValue to its actual HDF
                // "Maximum ..." summary datasets. A summary has no timeline,
                // so it is not timestamp-matched as a normal MultiResult step.
                resultProfileIndex = LegacyOutputBlock.MaxProfileIndex;
            }
            else if (source.ActiveNode.NodeKind == "MultiResult")
            {
                ResolvedMapResultNode? keyContext = context.ResultContexts.FirstOrDefault(candidate =>
                    MapResultRenderService.SelectionIndicesFor(candidate, node).Contains(0));
                if (keyContext?.Result.BaseOutputBlock is not IHorizontalOutputBlock keyBlock)
                {
                    unavailableReason = "The MultiResult has no selected first Result to anchor its profile timeline.";
                }
                else if (profileIndex < 0 || profileIndex >= keyBlock.ProfileCount)
                {
                    unavailableReason = "The selected profile index is outside the MultiResult timeline.";
                }
                else if (!selectionIndices.Contains(0) &&
                         (resultProfileIndex = MapResultRenderService.MatchKeyedProfile(
                             keyBlock, profileIndex, outputBlock)) is null)
                {
                    unavailableReason = "This Result has no profile within the 10 millisecond MultiResult timestamp tolerance.";
                }
                else
                {
                    resultProfileIndex ??= profileIndex;
                }
            }
            else if (profileIndex < 0 || profileIndex >= outputBlock.ProfileCount)
            {
                unavailableReason = "The selected profile index is outside this Result's stored profiles.";
            }
            else
            {
                resultProfileIndex = profileIndex;
            }

            if (unavailableReason is null && resultProfileIndex is int selectedIndex)
            {
                bool isLegacyMaximumFrame = selectedIndex == LegacyOutputBlock.MaxProfileIndex &&
                    outputBlock is LegacyOutputBlock;
                if (!isLegacyMaximumFrame && (selectedIndex < 0 || selectedIndex >= outputBlock.ProfileCount))
                {
                    unavailableReason = "The selected profile index is outside this Result's stored profiles.";
                }
                else
                {
                    associatedTerrainPath = ProjectAssetPath(context.Project, terrain.Filename);
                    try
                    {
                        terrainSnapshot = GetOrCaptureTerrain(terrain, associatedTerrainPath,
                            terrainSnapshots, out unavailableReason);
                        if (terrainSnapshot is null || unavailableReason is not null)
                            throw new FileNotFoundException(unavailableReason ?? "The associated terrain source file was not found.", associatedTerrainPath);
                        associatedTerrainPath = terrainSnapshot.Path;
                        associatedTerrainRevision = terrainSnapshot.Revision;
                        MeshPropertyTables[] tables = source.Result.Geometry?.FlowAreaLayer?.AllPropertyTables()
                            ?? throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
                        if (tables.Length == 0)
                            throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
                        var renderLayer = new ResultMapRenderLayer(outputBlock, mapType!.Value, terrain, tables);
                        var resampler = renderLayer.MakeResampler(chartFriendly);
                        var values = new float[line.Points.Length];
                        MapResultChartService.EnsureUnchanged(resultPath, source.Revision);
                        VerifyTerrainSnapshot(terrain, terrainSnapshot);
                        resampler.SamplePoints(line.Points.AsMemory(), selectedIndex, values.AsMemory());
                        MapResultChartService.EnsureUnchanged(resultPath, source.Revision);
                        VerifyTerrainSnapshot(terrain, terrainSnapshot);
                        samples = ConvertSamples(values, depthNoDataIsZero: chartFriendly && mapType == MapTypes.Depth);
                        time = !isLegacyMaximumFrame && selectedIndex < outputBlock.Times.Count
                            ? MapResultRenderService.FormatTime(outputBlock.Times[selectedIndex])
                            : null;
                    }
                    catch (FileNotFoundException exception)
                    {
                        unavailableReason = exception.Message;
                    }
                    catch (NotSupportedException exception)
                    {
                        unavailableReason = exception.Message;
                    }
                }
            }
        }

        string mapTypeName = mapType?.ToString() ?? source.SelectedMapType ?? string.Empty;
        string seriesID = SeriesIdentity(line.Id, "Result", resultPath, mapTypeName,
            resultProfileIndex?.ToString(CultureInfo.InvariantCulture));
        return new MapDynamicProfileSeries(seriesID, line.Id, "Result", resultPath,
            source.Revision, source.Result.Name, mapTypeName, resultProfileIndex, time,
            mapType is { } resolvedType
                ? MapResultRenderService.LayerUnits(context.Project.Units, resolvedType)
                : string.Empty,
            samples, associatedTerrainPath, associatedTerrainRevision, unavailableReason);
    }

    private static MapDynamicProfileSeries SampleLinkedResult(ResolvedMapResultContext context,
        LinkedResultProfileFrame frame, Result result, PreparedLine line, MapTypes? mapType,
        string frameMode,
        IDictionary<string, TrackedTerrainSnapshot> terrainSnapshots)
    {
        string resultPath = SourceFilePaths.Canonical(frame.ResultPath);
        int? profileIndex = frameMode switch
        {
            "Maximum" => LegacyOutputBlock.MaxProfileIndex,
            "Minimum" => LegacyOutputBlock.MinProfileIndex,
            _ => frame.ProfileIndex
        };
        string? unavailableReason = frameMode switch
        {
            "Minimum" => "The retained Legacy Result map sampler does not read Minimum summary datasets.",
            "Maximum" => null,
            _ => frame.UnavailableReason
        };
        string? terrainPath = null;
        string? terrainRevision = null;
        double?[] samples = NullSamples(line.Points.Length);
        Terrain? terrain = result.AssociatedTerrain;
        IHorizontalOutputBlock? outputBlock = result.BaseOutputBlock;

        if (unavailableReason is null && profileIndex is null)
            unavailableReason = "This linked Result has no profile within the 10 millisecond MultiResult timestamp tolerance.";
        if (unavailableReason is null && outputBlock is null)
            unavailableReason = "This linked Result has no stored horizontal output profiles.";
        if (unavailableReason is null && mapType is not (MapTypes.WaterSurfaceElevation or MapTypes.Depth or
                MapTypes.Velocity or MapTypes.DepthTimesVelocitySquared))
            unavailableReason = "The selected Result map type is not supported by the current map renderer.";
        if (unavailableReason is null && terrain is null)
            unavailableReason = "The linked Result has no current-project associated terrain.";
        if (unavailableReason is null && frameMode == "Maximum" &&
            (outputBlock is not LegacyOutputBlock || outputBlock.ProfileCount == 0))
            unavailableReason = "Legacy Maximum summary sampling is unavailable for this Result output block.";
        if (unavailableReason is null && frameMode == "Maximum" && mapType is MapTypes maximumMapType &&
            !HasLegacyMaximumDatasets(result, maximumMapType))
            unavailableReason = "This Legacy Result has no valid stored Maximum summary datasets for the selected map type.";

        if (unavailableReason is null && profileIndex is int selectedIndex &&
            outputBlock is IHorizontalOutputBlock block && terrain is not null && mapType is MapTypes selectedMapType)
        {
            MapResultRenderService.ValidateProfiles(block);
            bool maximum = frameMode == "Maximum" && selectedIndex == LegacyOutputBlock.MaxProfileIndex &&
                block is LegacyOutputBlock;
            if (!maximum && (selectedIndex < 0 || selectedIndex >= block.ProfileCount))
            {
                unavailableReason = "The linked Result's exact matched profile is outside its stored timeline.";
            }
            else
            {
                terrainPath = ProjectAssetPath(context.Project, terrain.Filename);
                try
                {
                    MapResultRenderService.TerrainSourceSnapshot? terrainSnapshot = GetOrCaptureTerrain(
                        terrain, terrainPath, terrainSnapshots, out unavailableReason);
                    if (terrainSnapshot is null || unavailableReason is not null)
                        throw new FileNotFoundException(unavailableReason ?? "The associated terrain source file was not found.", terrainPath);
                    terrainPath = terrainSnapshot.Path;
                    terrainRevision = terrainSnapshot.Revision;
                    MeshPropertyTables[] tables = result.Geometry?.FlowAreaLayer?.AllPropertyTables()
                        ?? throw new InvalidDataException("The linked Result has no embedded flow-area property tables.");
                    if (tables.Length == 0)
                        throw new InvalidDataException("The linked Result has no embedded flow-area property tables.");
                    var renderLayer = new ResultMapRenderLayer(block, selectedMapType, terrain, tables);
                    var resampler = renderLayer.MakeResampler(chartFriendly: true);
                    var values = new float[line.Points.Length];
                    MapResultChartService.EnsureUnchanged(resultPath, frame.Revision);
                    VerifyTerrainSnapshot(terrain, terrainSnapshot);
                    resampler.SamplePoints(line.Points.AsMemory(), selectedIndex, values.AsMemory());
                    MapResultChartService.EnsureUnchanged(resultPath, frame.Revision);
                    VerifyTerrainSnapshot(terrain, terrainSnapshot);
                    samples = ConvertSamples(values, depthNoDataIsZero: selectedMapType == MapTypes.Depth);
                }
                catch (FileNotFoundException exception)
                {
                    unavailableReason = exception.Message;
                }
                catch (NotSupportedException exception)
                {
                    unavailableReason = exception.Message;
                }
                catch (InvalidDataException exception)
                {
                    unavailableReason = exception.Message;
                }
            }
        }

        string mapTypeName = mapType?.ToString() ?? string.Empty;
        string seriesID = SeriesIdentity(line.Id, "Result", resultPath, mapTypeName,
            profileIndex?.ToString(CultureInfo.InvariantCulture));
        return new MapDynamicProfileSeries(seriesID, line.Id, "Result", resultPath,
            frame.Revision, frame.Name, mapTypeName, profileIndex,
            frameMode == "Profile" ? frame.Time : null,
            mapType is { } resolvedType
                ? MapResultRenderService.LayerUnits(context.Project.Units, resolvedType)
                : string.Empty,
            samples, terrainPath, terrainRevision, unavailableReason);
    }

    private static bool HasLegacyMaximumDatasets(Result result, MapTypes mapType)
    {
        if (mapType == MapTypes.DepthTimesVelocitySquared ||
            result.BaseOutputBlock is not LegacyOutputBlock outputBlock ||
            outputBlock.ProfileCount == 0 || result.Geometry?.FlowAreaLayer is not { Count: > 0 } flowAreas ||
            string.IsNullOrWhiteSpace(result.Filename) || !File.Exists(result.Filename))
            return false;

        OutputVariable[] variables = mapType == MapTypes.Velocity
            ? [OutputVariables.Depth, OutputVariables.Velocity]
            : [OutputVariables.Depth];
        using var reader = new H5Reader(result.Filename);
        foreach (FlowArea area in flowAreas)
        foreach (OutputVariable variable in variables)
        {
            string dataset = LegacyOutputBlock.Get2DOutputDatasetName(variable,
                outputBlock.Name, area.Name, isSummary: true);
            if (!reader.ExistsAsDataset(dataset)) return false;
            DatasetParameters parameters = reader.GetDatasetParameters(dataset);
            long[] dimensions = parameters.Dimensions;
            long[] chunks = parameters.ChunkDimensions;
            long expectedValues = variable == OutputVariables.Depth
                ? area.Mesh.CellCount
                : area.Mesh.FaceCount;
            if (dimensions.Length != 2 || dimensions[0] < 1 ||
                dimensions[1] != expectedValues || expectedValues <= 0 ||
                chunks.Length != 2 || chunks[0] < 1 || chunks[1] != dimensions[1])
                return false;
        }
        return true;
    }

    private static double?[] ConvertSamples(float[] values, bool depthNoDataIsZero)
    {
        var samples = new double?[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            float value = values[index];
            samples[index] = !float.IsFinite(value) || value == NoDataValue
                ? depthNoDataIsZero ? 0d : null
                : value;
        }
        return samples;
    }

    private static double?[] NullSamples(int count) => new double?[count];

    private static void ValidateResponseSize(IReadOnlyList<PreparedLine> lines, int seriesPerLine)
    {
        long total = lines.Sum(line => (long)line.Points.Length) * seriesPerLine;
        if (total > MaximumTotalSeriesSamples)
            throw new ArgumentException($"The selected profile context would produce more than {MaximumTotalSeriesSamples} series samples.");
    }

    private static string SeriesIdentity(string lineId, string sourceKind, string path,
        string? mapType, string? profileIndex)
    {
        string[] components = [lineId, sourceKind, path, mapType ?? string.Empty, profileIndex ?? string.Empty];
        return string.Join("|", components.Select(component =>
            Encoding.UTF8.GetByteCount(component).ToString(CultureInfo.InvariantCulture) + ":" + component));
    }

    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record MapDynamicProfileActiveTerrain(Terrain Terrain, string Path);

    private sealed record MapDynamicProfileActiveTerrainState(MapDynamicProfileActiveTerrain ActiveTerrain,
        MapResultRenderService.TerrainSourceSnapshot? Snapshot, string? UnavailableReason);

    private sealed record TrackedTerrainSnapshot(Terrain Terrain,
        MapResultRenderService.TerrainSourceSnapshot Snapshot);

    private sealed record VectorSourceSnapshot(string Path, string Revision);

    private sealed record PreparedLine(string Id, GeometryTopologyCoordinate[] OriginalPoints,
        Point[] Points, GeometryTopologyCoordinate[] SamplePoints, double[] Distances);
}
