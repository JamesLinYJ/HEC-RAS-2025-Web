using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Geospatial.Vectors;
using H5Assist.Caching;
using H5Assist;
using Ras.Layers;
using Ras.Mapping;
using Ras.Output;

namespace Ras.Backend;

/// <summary>
/// Renders one atomic raster group for the Result files selected by Link Plans.
/// The first source remains the timeline key; each child uses the retained
/// KeyedProfileState match returned by LinkedResultProfilesService. Geometry,
/// output blocks, and current-project associated terrain stay source-backed.
/// </summary>
public static class LinkedResultMapService
{
    private const string MinimumFrame = "Minimum";
    private const string MaximumFrame = "Maximum";
    private const string ProfileFrame = "Profile";
    private const string MinimumUnavailable = "minimumUnavailable";
    private const string MaximumUnavailable = "maximumUnavailable";
    private const string NoProfileFrames = "noProfileFrames";
    private const string NoMatchingFrame = "noMatchingFrame";
    private const string NoAssociatedTerrain = "noAssociatedTerrain";
    private const string TerrainUnavailable = "terrainUnavailable";
    private const string RenderUnavailable = "renderUnavailable";
    private const string NoMapData = "noMapData";

    public static LinkedResultMapSnapshot Render(LinkedResultMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Anchor);
        ArgumentNullException.ThrowIfNull(request.SelectedGroups);
        if (request.ProfileIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose an available linked result profile.");
        if (request.FrameMode is not (ProfileFrame or MinimumFrame or MaximumFrame))
            throw new ArgumentException("Choose Profile, Minimum, or Maximum for the linked Result map.", nameof(request.FrameMode));

        MapTypes? selectedMapType = MapResultRenderService.ParseSupportedMapType(request.MapType);
        if (selectedMapType is not MapTypes mapType)
            throw new NotSupportedException("Choose a supported linked Result map type.");

        // This retained service owns strict project/Plan-group/result identity
        // checks and exact 10 ms KeyedProfileState matching. Its normalized
        // project path deliberately preserves a symlink alias as the relative
        // directory base; canonical paths are used only for identities.
        LinkedResultProfilesSnapshot linked = LinkedResultProfilesService.Read(
            new LinkedResultProfilesRequest(request.ProjectPath, request.ProjectXml,
                request.ProjectRevision, request.Anchor, request.SelectedGroups, request.ProfileIndex,
                request.CoreLayerContext));
        string normalizedProjectPath = Path.GetFullPath(request.ProjectPath);
        if (!PathComparer.Equals(SourceFilePaths.Canonical(linked.ProjectPath),
                SourceFilePaths.Canonical(normalizedProjectPath)) ||
            !string.Equals(linked.ProjectRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(linked.AnchorPath),
                SourceFilePaths.Canonical(request.Anchor.ResultPath)) ||
            linked.ProfileIndex != request.ProfileIndex)
            throw new SourceFileChangedException();

        string projectRevision = RevisionFile(normalizedProjectPath);
        if (!string.Equals(projectRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProjectWithMetadata(normalizedProjectPath,
            ParseProjectXml(request.ProjectXml), request.CoreLayerContext);
        try
        {
            var projectResults = project.Results.ToArray();
            var resultsByPath = new Dictionary<string, Result>(PathComparer);
            foreach (LinkedResultProfileFrame frame in linked.Frames)
            {
                string canonicalPath = SourceFilePaths.Canonical(frame.ResultPath);
                Result[] matches = projectResults.Where(result => !string.IsNullOrWhiteSpace(result.Filename) &&
                    PathComparer.Equals(SourceFilePaths.Canonical(result.Filename), canonicalPath)).ToArray();
                if (matches.Length != 1 || matches[0].IsInMemory || !resultsByPath.TryAdd(canonicalPath, matches[0]))
                    throw new SourceFileChangedException();
            }

            RasterMapViewExtent viewport = request.Viewport is { } requestedViewport
                ? ValidateAndUse(requestedViewport)
                : CommonViewport(linked.Frames.Select(frame => resultsByPath[SourceFilePaths.Canonical(frame.ResultPath)]));

            var statuses = new List<LinkedResultMapSourceStatus>(linked.Frames.Length);
            var layers = new List<LinkedResultMapLayer>(linked.Frames.Length);
            var terrainSnapshots = new List<MapResultRenderService.TerrainSourceSnapshot>();
            var terrainSnapshotsByResult = new Dictionary<string, MapResultRenderService.TerrainSourceSnapshot>(PathComparer);
            bool maximumAvailable = linked.Frames.All(frame =>
            {
                Result result = resultsByPath[SourceFilePaths.Canonical(frame.ResultPath)];
                if (result.AssociatedTerrain is not { } terrain || !HasMaximumDatasets(result, mapType))
                    return false;
                try
                {
                    MapResultRenderService.TerrainSourceSnapshot snapshot =
                        MapResultRenderService.CaptureTerrainSources(terrain);
                    terrainSnapshots.Add(snapshot);
                    terrainSnapshotsByResult.Add(SourceFilePaths.Canonical(frame.ResultPath), snapshot);
                    return true;
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (NotSupportedException) { return false; }
                catch (InvalidDataException) { return false; }
            });
            string groupIdentity = GroupIdentity(normalizedProjectPath, linked);

            if (request.FrameMode == MinimumFrame)
            {
                statuses.AddRange(linked.Frames.Select(frame => Status(frame, null, null,
                    false, MinimumUnavailable)));
                return Complete(MinimumUnavailable);
            }

            if (request.FrameMode == MaximumFrame && !maximumAvailable)
            {
                statuses.AddRange(linked.Frames.Select(frame => Status(frame, null, null,
                    false, MaximumUnavailable)));
                return Complete(MaximumUnavailable);
            }

            if (request.FrameMode == ProfileFrame && linked.Steps.Length == 0)
            {
                statuses.AddRange(linked.Frames.Select(frame => Status(frame, null, null,
                    false, NoProfileFrames)));
                return Complete(NoProfileFrames);
            }

            foreach (LinkedResultProfileFrame frame in linked.Frames)
            {
                Result result = resultsByPath[SourceFilePaths.Canonical(frame.ResultPath)];
                int? childProfileIndex = request.FrameMode switch
                {
                    MaximumFrame => int.MaxValue,
                    ProfileFrame => frame.ProfileIndex,
                    _ => null
                };
                string? profileName = null;
                if (request.FrameMode == MaximumFrame)
                    profileName = "Max";
                else if (childProfileIndex is int profile && result.BaseOutputBlock is { } outputBlock &&
                         profile >= 0 && profile < outputBlock.ProfileCount)
                    profileName = outputBlock.ProfileNames[profile];
                string? time = request.FrameMode == MaximumFrame ? null : frame.Time;

                if (childProfileIndex is null)
                {
                    statuses.Add(Status(frame, null, null, false, NoMatchingFrame));
                    continue;
                }
                if (result.AssociatedTerrain is null)
                {
                    statuses.Add(Status(frame, null, null, false, NoAssociatedTerrain));
                    continue;
                }

                try
                {
                    string resultPathIdentity = SourceFilePaths.Canonical(frame.ResultPath);
                    if (!terrainSnapshotsByResult.TryGetValue(resultPathIdentity, out var terrain))
                    {
                        terrain = MapResultRenderService.CaptureTerrainSources(result.AssociatedTerrain);
                        terrainSnapshots.Add(terrain);
                        terrainSnapshotsByResult.Add(resultPathIdentity, terrain);
                    }
                    string resultIdentity = ResultIdentity(frame.ResultPath, frame.Revision);
                    MapResultRenderLayerPayload? payload = MapResultRenderService.RenderStoredResult(
                        project, result, frame.ResultPath, frame.Revision,
                        "linked-results-" + groupIdentity, groupIdentity, resultIdentity,
                        mapType, childProfileIndex.Value, viewport, terrain);
                    if (payload is null)
                    {
                        statuses.Add(Status(frame, terrain.Path, terrain.Revision,
                            false, NoMapData));
                        continue;
                    }

                    layers.Add(new LinkedResultMapLayer(payload.Id, payload.ResultPath,
                        payload.Revision, payload.ResultName, payload.MapType,
                        payload.TerrainPath, payload.TerrainRevision, payload.ProfileIndex,
                        payload.ProfileName, payload.Time, payload.Units, payload.Width,
                        payload.Height, payload.Rgba, payload.MinX, payload.MinY,
                        payload.MaxX, payload.MaxY, payload.LegendStops));
                    statuses.Add(Status(frame, terrain.Path, terrain.Revision,
                        true, null, childProfileIndex, profileName, time));
                }
                catch (FileNotFoundException)
                {
                    statuses.Add(Status(frame, null, null, false, TerrainUnavailable));
                }
                catch (DirectoryNotFoundException)
                {
                    statuses.Add(Status(frame, null, null, false, TerrainUnavailable));
                }
                catch (NotSupportedException)
                {
                    statuses.Add(Status(frame, null, null, false, TerrainUnavailable));
                }
                catch (InvalidDataException)
                {
                    statuses.Add(Status(frame, null, null, false, RenderUnavailable));
                }
            }

            return Complete(layers.Count == 0 ? statuses.FirstOrDefault()?.UnavailableReason : null);

            LinkedResultMapSnapshot Complete(string? unavailable)
            {
                MapResultChartService.EnsureUnchanged(normalizedProjectPath, request.ProjectRevision);
                foreach (LinkedResultProfileFrame source in linked.Frames)
                    MapResultChartService.EnsureUnchanged(source.ResultPath, source.Revision);
                foreach (MapResultRenderService.TerrainSourceSnapshot terrain in terrainSnapshots)
                    MapResultRenderService.VerifyTerrainSources(terrain);
                return new(normalizedProjectPath, request.ProjectRevision,
                    SourceFilePaths.Canonical(request.Anchor.ResultPath), request.ProfileIndex,
                    request.FrameMode, mapType.ToString(), viewport, false, maximumAvailable,
                    statuses.ToArray(), layers.ToArray(), unavailable);
            }

            LinkedResultMapSourceStatus Status(LinkedResultProfileFrame frame,
                string? terrainPath, string? terrainRevision, bool available,
                string? unavailableReason, int? profile = null, string? name = null,
                string? timeValue = null) => new(frame.ResultPath, frame.Revision,
                    frame.Name, terrainPath, terrainRevision, available, profile, name,
                    timeValue, unavailableReason);
        }
        finally
        {
            MapResultChartService.DisposeProject(project);
        }
    }

    private static bool HasMaximumDatasets(Result result, MapTypes mapType)
    {
        // The source type does not advertise dynamic min/max support. Multiplying
        // separate stored maximum depth and velocity datasets would not be max(D*V^2).
        if (mapType == MapTypes.DepthTimesVelocitySquared ||
            result.BaseOutputBlock is not LegacyOutputBlock outputBlock ||
            result.Geometry?.FlowAreaLayer is not { Count: > 0 } flowAreas)
            return false;

        OutputVariable[] variables = mapType is MapTypes.Velocity or MapTypes.DepthTimesVelocity
            ? [OutputVariables.Depth, OutputVariables.Velocity]
            : [OutputVariables.Depth];
        using var reader = new H5Reader(result.Filename);
        foreach (var area in flowAreas)
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

    private static RasterMapViewExtent ValidateAndUse(RasterMapViewExtent viewport)
    {
        MapResultRenderService.ValidateViewport(viewport);
        return viewport;
    }

    private static RasterMapViewExtent CommonViewport(IEnumerable<Result> results)
    {
        Extent[] extents = results.Select(result => result.Geometry?.Extent)
            .Where(extent => extent is not null && extent.Value.IsFiniteAndValid())
            .Select(extent => extent!.Value).ToArray();
        if (extents.Length == 0)
            throw new InvalidDataException("The selected Results have no finite embedded geometry extent.");
        return MapResultRenderService.DefaultViewport(new Extent(
            extents.Min(extent => extent.MinX), extents.Max(extent => extent.MaxX),
            extents.Min(extent => extent.MinY), extents.Max(extent => extent.MaxY)));
    }

    private static string GroupIdentity(string projectPath, LinkedResultProfilesSnapshot snapshot)
    {
        var builder = new StringBuilder(SourceFilePaths.Canonical(projectPath));
        builder.Append('\0').Append(snapshot.ProjectRevision).Append('\0')
            .Append(SourceFilePaths.Canonical(snapshot.AnchorPath));
        foreach (LinkedResultProfileFrame frame in snapshot.Frames)
            builder.Append('\0').Append(SourceFilePaths.Canonical(frame.ResultPath))
                .Append('\0').Append(frame.Revision);
        return HashText(builder.ToString());
    }

    private static string ResultIdentity(string path, string revision) =>
        HashText(SourceFilePaths.Canonical(path) + "\0" + revision);

    private static string HashText(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static XElement ParseProjectXml(string projectXml)
    {
        if (Encoding.UTF8.GetByteCount(projectXml) > 8 * 1024 * 1024)
            throw new ArgumentException("The current project XML context is too large.", nameof(projectXml));
        using XmlReader reader = XmlReader.Create(new StringReader(projectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8L * 1024 * 1024
        });
        return XElement.Load(reader, LoadOptions.None);
    }

    private static string RevisionFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
