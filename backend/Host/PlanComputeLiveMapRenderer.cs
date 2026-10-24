using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Structures.Culverts;
using Ras.Layers;
using Ras.Layers.Results;
using Ras.Mapping;
using Ras.Hydraulics.Culverts;
using Ras.Output;
using Ras.Rendering;
using Utility.Files;
using Utility.Memory;

namespace Ras.Backend;

/// <summary>
/// Renders from the retained compute Result on its TimestepAdded callback.
/// It uses ResultMapRenderLayer and the writer's supported write-mode reads;
/// it never creates a second Result/HDF reader or a stable Result revision.
/// </summary>
internal sealed class PlanComputeLiveMapRenderer : IDisposable
{
    private const int MaximumRasterSide = 384;
    private const float NoDataValue = -9999f;
    private static readonly long MinimumFrameInterval = Stopwatch.Frequency / 2;
    private readonly Project project;
    private readonly Result result;
    private readonly string resultPath;
    private readonly Func<ResultComputeEnvelope> computeIdentity;
    private readonly PlanComputeLiveMapTargetRegistry targets;
    private readonly Action<BackendOperationProgress> report;
    private readonly object gate = new();
    private readonly Dictionary<string, long> lastFrameTicks = new(StringComparer.Ordinal);
    private readonly List<CapturedProfile> capturedProfiles = [];
    private readonly Dictionary<int, CapturedCulvertProfile> capturedCulvertProfiles = [];
    private readonly PlanComputeLiveCulvertRowStore capturedCulvertRows = new();
    private readonly IHorizontalOutputBlock? block;
    private CapturedCulvertGeometry? capturedCulvertGeometry;
    private string? capturedCulvertGeometryError;
    private bool disposed;

    private sealed record CapturedProfile(int Index, string Name, DateTime Time, string WireTime);
    private sealed record CapturedCulvertGeometry(string Name, MapResultCulvertBarrel[] Barrels);
    private sealed record CapturedCulvertProfile(int Index, string Name, string WireTime,
        string? UnavailableMessage);

    public PlanComputeLiveMapRenderer(Project project, Result result, string resultPath,
        Func<ResultComputeEnvelope> computeIdentity, PlanComputeLiveMapTargetRegistry targets,
        Action<BackendOperationProgress> report)
    {
        this.project = project;
        this.result = result;
        this.resultPath = SourceFilePaths.Canonical(resultPath);
        this.computeIdentity = computeIdentity;
        this.targets = targets;
        this.report = report;
        block = result.BaseOutputBlock;
        if (block is not null)
        {
            block.TimestepAdded += OnTimestepAdded;
            // Match the profile publisher's initial snapshot. The source may
            // already own rows before this compute-start callback subscribes.
            CaptureProducedProfiles();
        }
    }

    private void OnTimestepAdded()
    {
        lock (gate)
        {
            if (disposed || block is null) return;
            if (!CaptureProducedProfiles()) return;
            RenderTargetsLocked(captureWriterCulvertRows: true);
        }
    }

    /// <summary>
    /// The source appends profile rows and metadata before raising TimestepAdded.
    /// Capture those immutable identities on that source callback; target changes
    /// can then redraw an already-produced row without reading mutable Lists.
    /// </summary>
    private bool CaptureProducedProfiles()
    {
        if (block is null) return false;
        try { MapResultRenderService.ValidateProfiles(block); }
        catch { return false; }

        string[] names = block.ProfileNames.ToArray();
        DateTime[] times = block.Times.ToArray();
        if (names.Length != times.Length || names.Length < capturedProfiles.Count) return false;
        for (int index = 0; index < capturedProfiles.Count; index++)
        {
            CapturedProfile existing = capturedProfiles[index];
            if (!string.Equals(existing.Name, names[index], StringComparison.Ordinal) ||
                existing.Time != times[index]) return false;
        }
        for (int index = capturedProfiles.Count; index < names.Length; index++)
        {
            DateTime time = times[index];
            capturedProfiles.Add(new(index, names[index], time,
                time.ToString("O", CultureInfo.InvariantCulture)));
        }
        return true;
    }

    /// <summary>
    /// Copies produced culvert rows before any target is required, so enabling
    /// particles during a long solver step can use existing profiles. Retain snapshots for the
    /// writer lifetime so target/profile changes can redraw earlier profiles
    /// without reading an append/cache row from a delayed callback.
    /// </summary>
    private void CaptureProducedCulvertProfiles()
    {
        if (block is not OutputBlock output || !output.IsWriting() || !output.HasCulvertOutput) return;

        string? geometryError = EnsureCulvertGeometrySnapshot();
        for (int profileIndex = capturedCulvertProfiles.Count; profileIndex < capturedProfiles.Count; profileIndex++)
        {
            CapturedProfile profile = capturedProfiles[profileIndex];
            // A bad or unavailable row belongs to this profile only. It must
            // not poison later rows from the same writer callback.
            string? unavailable = geometryError;
            bool stored = false;
            if (unavailable is null)
            {
                try
                {
                    if (!output.TryReadCulvertData(profileIndex, out CulvertTimestepData? data) || data is null)
                    {
                        unavailable = "The active writer OutputBlock has no readable culvert output for this profile.";
                    }
                    else
                    {
                        // TryReadCulvertData allocates these arrays and copies
                        // the cache rows into them. Keep that owned snapshot;
                        // never retain the append cache's mutable storage.
                        float[] flowValues = data.Flow ?? [];
                        float[] outsideDepthValues = data.OutsideDepth ?? [];
                        float[] outletVelocityValues = data.OutletVelocity ?? [];
                        int barrelCount = capturedCulvertGeometry?.Barrels.Length ?? 0;
                        if (barrelCount == 0 || flowValues.Length != barrelCount ||
                            outletVelocityValues.Length != barrelCount ||
                            outsideDepthValues.Length != checked(barrelCount * 2))
                        {
                            unavailable = "The live culvert arrays do not align with the embedded Result barrel geometry.";
                        }
                        else if (flowValues.Any(value => !float.IsFinite(value)) ||
                                 outsideDepthValues.Any(value => !float.IsFinite(value)) ||
                                 outletVelocityValues.Any(value => !float.IsFinite(value)))
                        {
                            unavailable = "The live Result contains non-finite culvert output values.";
                        }
                        else
                        {
                            // The row store takes ownership of TryReadCulvertData's
                            // arrays. It keeps early rows in bounded memory and
                            // spills the rest to a private file without losing history.
                            capturedCulvertRows.Append(profileIndex, flowValues,
                                outsideDepthValues, outletVelocityValues);
                            stored = true;
                        }
                    }
                }
                catch (Exception error) when (error is NotImplementedException or ArgumentException or
                                              InvalidDataException or IOException or UnauthorizedAccessException or
                                              NotSupportedException)
                {
                    unavailable = string.IsNullOrWhiteSpace(error.Message)
                        ? "The active writer could not read or store this culvert profile."
                        : error.Message;
                }
            }
            if (!stored) capturedCulvertRows.AppendUnavailable(profileIndex);
            capturedCulvertProfiles.Add(profileIndex,
                new CapturedCulvertProfile(profile.Index, profile.Name, profile.WireTime, unavailable));
        }
    }

    private string? EnsureCulvertGeometrySnapshot()
    {
        if (capturedCulvertGeometry is not null) return null;
        if (capturedCulvertGeometryError is not null) return capturedCulvertGeometryError;
        try
        {
            Geometry? geometry = result.Geometry;
            if (geometry is null)
                throw new NotSupportedException("The active Result has no embedded Geometry for culvert particles.");
            CulvertBarrelLayer? sourceLayer = geometry.CulvertBarrelLayer;
            if (sourceLayer is null || sourceLayer.Features.Count == 0)
                throw new NotSupportedException("The active Result has no embedded culvert barrel layer.");
            string name = geometry.Name;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("The active Result embedded Geometry has no name.");
            MapResultCulvertBarrel[] barrels = sourceLayer.Features.Select((barrel, ordinal) =>
            {
                if (barrel?.Polyline is not { Points.Count: >= 2 } line ||
                    line.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                    throw new InvalidDataException("The active Result contains invalid culvert barrel geometry.");
                return new MapResultCulvertBarrel(
                    "barrel:source:" + ordinal.ToString(CultureInfo.InvariantCulture), ordinal,
                    barrel.Name ?? string.Empty,
                    line.Points.Select(point => new MapResultCulvertCoordinate(point.X, point.Y)).ToArray(),
                    barrel.BarrelPropertyName ?? string.Empty,
                    barrel.UpstreamInvert.ToString("R", CultureInfo.InvariantCulture),
                    barrel.DownstreamInvert.ToString("R", CultureInfo.InvariantCulture),
                    barrel.UpstreamOpeningName ?? string.Empty,
                    barrel.DownstreamOpeningName ?? string.Empty);
            }).ToArray();
            capturedCulvertGeometry = new CapturedCulvertGeometry(name, barrels);
            return null;
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException)
        {
            capturedCulvertGeometryError = string.IsNullOrWhiteSpace(error.Message)
                ? "The active Result culvert geometry could not be read."
                : error.Message;
            return capturedCulvertGeometryError;
        }
    }

    /// <summary>Redraws accepted target changes from cached source profiles.</summary>
    public void TargetsChanged()
    {
        lock (gate)
        {
            if (disposed || block is null) return;
            lastFrameTicks.Clear();
            RenderTargetsLocked();
        }
    }

    private void RenderTargetsLocked(bool captureWriterCulvertRows = false)
    {
        if (block is null || capturedProfiles.Count == 0) return;
        var snapshot = targets.Snapshot();
        ResultComputeEnvelope identity = computeIdentity();
        if (identity.State != "running" || identity.ResultPath is null ||
            SourceFilePaths.Canonical(identity.ResultPath) != resultPath) return;
        if (captureWriterCulvertRows) CaptureProducedCulvertProfiles();

        foreach (PlanComputeLiveMapRenderTarget target in snapshot.Targets)
        {
            if (!TryReserve(target.TargetID)) continue;
            try
            {
                PlanComputeLiveMapRenderFrame? frame = null;
                try
                {
                    frame = Render(target, identity, snapshot.Sequence);
                }
                catch (Exception error) when (error is NotImplementedException or ArgumentException or
                                              InvalidDataException or FileNotFoundException or
                                              NotSupportedException or SourceFileChangedException)
                {
                    // Velocity has its own real payload and unsupported state;
                    // an RGBA render failure must not suppress that event.
                }
                PlanComputeLiveVelocityFrame? velocityFrame = null;
                PlanComputeLiveVelocityUnavailable? velocityUnavailable = null;
                if (target.CaptureVelocityParticles)
                {
                    try
                    {
                        velocityFrame = RenderVelocity(target, identity, snapshot.Sequence);
                    }
                    catch (NotImplementedException error)
                    {
                        velocityUnavailable = VelocityUnavailable(target, identity, snapshot.Sequence,
                            error.Message);
                    }
                    catch (Exception error) when (error is ArgumentException or InvalidDataException or
                                                  FileNotFoundException or NotSupportedException)
                    {
                        velocityUnavailable = VelocityUnavailable(target, identity, snapshot.Sequence,
                            error.Message);
                    }
                }
                PlanComputeLiveCulvertFrame? culvertFrame = null;
                PlanComputeLiveCulvertUnavailable? culvertUnavailable = null;
                if (target.CaptureCulvertParticles)
                {
                    try
                    {
                        culvertFrame = RenderCulvert(target, identity, snapshot.Sequence);
                    }
                    catch (SourceFileChangedException error)
                    {
                        // Clear this target if it is still current. A newer
                        // replacement or terminal compute update owns its own
                        // cleanup and must not receive a delayed stale clear.
                        if (IsCurrentTarget(snapshot.Sequence, target))
                            culvertUnavailable = CulvertUnavailable(target, identity, snapshot.Sequence,
                                string.IsNullOrWhiteSpace(error.Message)
                                    ? "The live culvert source changed while the writer was active."
                                    : error.Message);
                    }
                    catch (Exception error) when (error is NotImplementedException or ArgumentException or
                                                  InvalidDataException or IOException or UnauthorizedAccessException or
                                                  NotSupportedException)
                    {
                        culvertUnavailable = CulvertUnavailable(target, identity, snapshot.Sequence,
                            error.Message);
                    }
                }
                if ((culvertFrame is not null || culvertUnavailable is not null) &&
                    !IsCurrentTarget(snapshot.Sequence, target))
                {
                    culvertFrame = null;
                    culvertUnavailable = null;
                }
                if (frame is not null || velocityFrame is not null || velocityUnavailable is not null ||
                    culvertFrame is not null || culvertUnavailable is not null)
                    targets.TryPublish(snapshot.Sequence, target, () =>
                        report(new(LiveMapFrame: frame, LiveVelocityFrame: velocityFrame,
                            LiveVelocityUnavailable: velocityUnavailable, LiveCulvertFrame: culvertFrame,
                            LiveCulvertUnavailable: culvertUnavailable)));
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or
                                          FileNotFoundException or NotSupportedException or SourceFileChangedException)
            {
                // A stale viewport/source target must not produce a raster.
            }
        }
    }

    private PlanComputeLiveVelocityFrame? RenderVelocity(PlanComputeLiveMapRenderTarget target,
        ResultComputeEnvelope identity, ulong targetSequence)
    {
        if (block is null || !target.CaptureVelocityParticles ||
            target.SelectionIndex != 0 ||
            !string.Equals(target.SelectedMapType, nameof(MapTypes.Velocity), StringComparison.Ordinal) ||
            !string.Equals(target.RunID, identity.RunId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.OperationOwnerToken) ||
            !string.Equals(target.ProjectPath, identity.ProjectPath, PathComparison) ||
            !string.Equals(target.ProjectRevision, identity.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.ProjectXmlFingerprint, identity.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.PlanPath), identity.PlanPath, PathComparison) ||
            !string.Equals(target.PlanRevision, identity.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.Map.ProjectPath), identity.ProjectPath, PathComparison) ||
            !string.Equals(target.Map.ProjectXmlSHA, target.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Node.NodeType, "MultiResult", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.Node.AssetID) ||
            !string.Equals(SourceFilePaths.Canonical(target.Node.AssetID), identity.PlanPath, PathComparison) ||
            string.IsNullOrWhiteSpace(target.ContextToken) || string.IsNullOrWhiteSpace(target.TargetID) ||
            !string.Equals(target.OutputBlockName, block.Name, StringComparison.Ordinal) ||
            !IsSelectedRun(target, identity.ResultPath!)) return null;

        string projectPath = SourceFilePaths.Canonical(target.Map.ProjectPath);
        string planPath = SourceFilePaths.Canonical(target.PlanPath);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath) ||
            !string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeDigest(target.Map.ProjectXml ?? string.Empty), target.Map.ProjectXmlSHA,
                StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();

        if (result.AssociatedTerrain is not { } terrain || result.Geometry?.FlowAreaLayer is not { Count: > 0 } flowArea)
            return null;
        // Live source writers intentionally follow the retained OutputBlock
        // contract: its write-mode per-mesh cache is single-mesh and throws
        // for multiple FlowAreas. Keep that limitation explicit here so the
        // UI receives an unavailable state instead of attempting to combine
        // arrays the source writer never emitted. Read-mode VelocityMapper
        // remains free to sample persisted multi-mesh Results.
        if (flowArea.Count != 1)
            throw new NotSupportedException("The active Result writer does not expose live velocity for multiple FlowAreas.");
        if (block.ProfileCount <= 0 || block.ProfileCount != capturedProfiles.Count) return null;
        int profileIndex = ResolveProfileIndex(target);
        if (profileIndex < 0 || profileIndex >= capturedProfiles.Count) return null;
        CapturedProfile profile = capturedProfiles[profileIndex];
        if (target.ProfileTime is { Length: > 0 } expectedTime &&
            !string.Equals(profile.WireTime, expectedTime, StringComparison.Ordinal)) return null;
        if (target.ProfileName is { Length: > 0 } expectedName &&
            !string.Equals(profile.Name, expectedName, StringComparison.Ordinal)) return null;

        RasterMapViewExtent viewport = BoundViewport(target.Viewport);
        MapResultRenderService.ValidateViewport(viewport);
        var terrainSnapshot = MapResultRenderService.CaptureTerrainSources(terrain);
        var definition = new RasterDefinition(new Extent(viewport.MinX, viewport.MaxX,
            viewport.MinY, viewport.MaxY), viewport.PixelHeight, viewport.PixelWidth);
        if (!definition.IsValid() || definition.CellCount <= 0 ||
            !double.IsFinite(definition.CellSize) || definition.CellSize <= 0) return null;

        var layer = new ResultMapRenderLayer(block, MapTypes.Velocity, terrain, flowArea.AllPropertyTables());
        layer.ForceHorizontalRendering = target.ForceHorizontalRendering;
        if (layer.Mapper is not VelocityMapper mapper) return null;
        var samplingParameters = target.ForceHorizontalRendering
            ? GeometrySamplingParameters.ForceHorizontalMode
            : GeometrySamplingParameters.Default;
        var mapPoints = new GeometryMapPoints(result.Geometry!, definition, samplingParameters);
        using IMemoryOwner<float> terrainOwner = SharedMemoryPool<float>.Rent(definition.CellCount);
        Memory<float> terrainValues = terrainOwner.Memory.Slice(0, definition.CellCount);
        terrain.Resample(new ResampleArgs(definition), terrainValues);
        float[] velocityX = new float[definition.CellCount];
        float[] velocityY = new float[definition.CellCount];
        float[] magnitude = new float[definition.CellCount];
        mapper.Compute(profileIndex, mapPoints, terrainValues, velocityX, velocityY, magnitude);

        MapResultRenderService.VerifyTerrainSources(terrainSnapshot);
        if (!string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(result.Filename ?? string.Empty), resultPath, PathComparison))
            return null;

        byte[] interleaved = PackVelocity(velocityX, velocityY, magnitude);
        return new PlanComputeLiveVelocityFrame(target.TargetID, target.ProjectSessionID,
            target.OperationOwnerToken, target.OperationContextID, identity.RunId, resultPath,
            block.Name, target.ContextToken, target.Map.MapID, target.NodeID, target.SelectionIndex,
            targetSequence, profile.Index, profile.Name, profile.WireTime,
            MapTypes.Velocity.ToString(), definition.Cols, definition.Rows, definition.CellSize,
            definition.MinX, definition.MinY, definition.MaxX, definition.MaxY, NoDataValue, interleaved,
            target.ForceHorizontalRendering);
    }

    private PlanComputeLiveVelocityUnavailable VelocityUnavailable(
        PlanComputeLiveMapRenderTarget target, ResultComputeEnvelope identity,
        ulong targetSequence, string message) => new(target.TargetID, target.ProjectSessionID,
            target.OperationOwnerToken, target.OperationContextID, identity.RunId,
            resultPath, block?.Name ?? target.OutputBlockName, target.ContextToken,
            target.Map.MapID, target.NodeID, target.SelectionIndex, targetSequence,
            string.IsNullOrWhiteSpace(message)
                ? "The active Result did not provide a readable live velocity field."
                : message);

    private PlanComputeLiveCulvertFrame? RenderCulvert(PlanComputeLiveMapRenderTarget target,
        ResultComputeEnvelope identity, ulong targetSequence)
    {
        if (block is null || !target.CaptureCulvertParticles || target.SelectionIndex != 0 ||
            !string.Equals(target.RunID, identity.RunId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.OperationOwnerToken) ||
            string.IsNullOrWhiteSpace(target.ProjectSessionID) ||
            string.IsNullOrWhiteSpace(target.OperationContextID) ||
            !string.Equals(target.ProjectPath, identity.ProjectPath, PathComparison) ||
            !string.Equals(target.ProjectRevision, identity.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.ProjectXmlFingerprint, identity.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.PlanPath), identity.PlanPath, PathComparison) ||
            !string.Equals(target.PlanRevision, identity.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.Map.ProjectPath), identity.ProjectPath, PathComparison) ||
            !string.Equals(target.Map.ProjectXmlSHA, target.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !CulvertNodeMatchesWriter(target, identity.ResultPath!, identity.PlanPath) ||
            string.IsNullOrWhiteSpace(target.NodeID) || string.IsNullOrWhiteSpace(target.TargetID) ||
            string.IsNullOrWhiteSpace(target.ContextToken) ||
            !string.Equals(target.OutputBlockName, block.Name, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.ProfileName) ||
            !IsSelectedRun(target, identity.ResultPath!)) return null;

        string projectPath = SourceFilePaths.Canonical(target.Map.ProjectPath);
        string planPath = SourceFilePaths.Canonical(target.PlanPath);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath) ||
            !string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
        if (!string.Equals(ComputeDigest(target.Map.ProjectXml ?? string.Empty), target.Map.ProjectXmlSHA,
                StringComparison.OrdinalIgnoreCase)) return null;

        if (block.ProfileCount <= 0 || block.ProfileCount != capturedProfiles.Count) return null;
        int profileIndex = ResolveProfileIndex(target);
        if (profileIndex < 0 || profileIndex >= capturedProfiles.Count) return null;
        CapturedProfile profile = capturedProfiles[profileIndex];
        if (!string.Equals(target.ProfileTime, profile.WireTime, StringComparison.Ordinal) ||
            !string.Equals(target.ProfileName, profile.Name, StringComparison.Ordinal)) return null;

        if (!capturedCulvertProfiles.TryGetValue(profileIndex, out CapturedCulvertProfile? data))
            throw new NotSupportedException("The live culvert snapshot for this produced profile is not available.");
        if (data.UnavailableMessage is { Length: > 0 } unavailableMessage)
            throw new NotSupportedException(unavailableMessage);
        if (!capturedCulvertRows.TryRead(profileIndex, out PlanComputeLiveCulvertSnapshotRow row))
            throw new InvalidDataException("The captured live culvert profile row is missing from its private snapshot store.");
        if (capturedCulvertGeometry is not { } geometry || row.Flow.Length == 0 ||
            row.Flow.Length != geometry.Barrels.Length || row.OutletVelocity.Length != geometry.Barrels.Length ||
            row.OutsideDepth.Length != checked(geometry.Barrels.Length * 2))
            throw new InvalidDataException("The captured live culvert row no longer matches its Result geometry.");
        CapturedProfile capturedProfile = capturedProfiles[profileIndex];
        if (data.Index != profileIndex || data.Name != capturedProfile.Name ||
            data.WireTime != capturedProfile.WireTime)
            throw new SourceFileChangedException();

        MapResultRenderService.ValidateViewport(target.Viewport);
        string frameResultPath = SourceFilePaths.Canonical(result.Filename ?? string.Empty);
        if (!string.Equals(frameResultPath, resultPath, PathComparison) ||
            !string.Equals(SourceFilePaths.Canonical(identity.ResultPath!), resultPath, PathComparison))
            throw new SourceFileChangedException();
        if (!string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();

        return new PlanComputeLiveCulvertFrame(target.TargetID, target.ProjectSessionID,
            target.OperationOwnerToken, target.OperationContextID, identity.RunId, resultPath,
            block.Name, target.ContextToken, target.Map.MapID, target.NodeID, 0, targetSequence,
            profile.Index, profile.Name, profile.WireTime, target.Viewport,
            geometry.Name, resultPath, geometry.Barrels.Length, geometry.Barrels,
            ToDoubleArray(row.Flow), ToDoubleArray(row.OutsideDepth), ToDoubleArray(row.OutletVelocity));
    }

    private static double[] ToDoubleArray(ReadOnlyMemory<float> values)
    {
        double[] converted = new double[values.Length];
        ReadOnlySpan<float> source = values.Span;
        for (int index = 0; index < source.Length; index++) converted[index] = source[index];
        return converted;
    }

    private PlanComputeLiveCulvertUnavailable CulvertUnavailable(
        PlanComputeLiveMapRenderTarget target, ResultComputeEnvelope identity,
        ulong targetSequence, string message) => new(target.TargetID, target.ProjectSessionID,
            target.OperationOwnerToken, target.OperationContextID, identity.RunId,
            resultPath, block?.Name ?? target.OutputBlockName, target.ContextToken,
            target.Map.MapID, target.NodeID, target.SelectionIndex, targetSequence,
            target.ProfileIndex, target.ProfileName ?? string.Empty, target.ProfileTime ?? string.Empty,
            target.Viewport,
            string.IsNullOrWhiteSpace(message)
                ? "The active Result did not provide readable live culvert output."
                : message);

    private bool IsCurrentTarget(ulong sequence, PlanComputeLiveMapRenderTarget target)
    {
        var current = targets.Snapshot();
        return current.Sequence == sequence && current.Targets.Any(candidate => candidate == target);
    }

    private static byte[] PackVelocity(float[] velocityX, float[] velocityY, float[] magnitude)
    {
        if (velocityX.Length != velocityY.Length || velocityX.Length != magnitude.Length)
            throw new InvalidDataException("The writer-owned velocity mapper returned arrays with different dimensions.");
        byte[] bytes = new byte[checked(velocityX.Length * 3 * sizeof(float))];
        for (int index = 0; index < velocityX.Length; index++)
        {
            int offset = checked(index * 3 * sizeof(float));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, sizeof(float)), velocityX[index]);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + sizeof(float), sizeof(float)), velocityY[index]);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 2 * sizeof(float), sizeof(float)), magnitude[index]);
        }
        return bytes;
    }

    private bool TryReserve(string targetID)
    {
        long now = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (disposed) return false;
            if (lastFrameTicks.TryGetValue(targetID, out long previous) && now - previous < MinimumFrameInterval)
                return false;
            lastFrameTicks[targetID] = now;
            return true;
        }
    }

    private PlanComputeLiveMapRenderFrame? Render(PlanComputeLiveMapRenderTarget target,
        ResultComputeEnvelope identity, ulong targetSequence)
    {
        if (block is null || capturedProfiles.Count <= 0 ||
            !string.Equals(target.RunID, identity.RunId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.OperationOwnerToken) ||
            !string.Equals(target.ProjectPath, identity.ProjectPath, PathComparison) ||
            !string.Equals(target.ProjectRevision, identity.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.ProjectXmlFingerprint, identity.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.PlanPath), identity.PlanPath, PathComparison) ||
            !string.Equals(target.PlanRevision, identity.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SourceFilePaths.Canonical(target.Map.ProjectPath), identity.ProjectPath, PathComparison) ||
            !string.Equals(target.Map.ProjectXmlSHA, target.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Node.NodeType, "MultiResult", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.Node.AssetID) ||
            !string.Equals(SourceFilePaths.Canonical(target.Node.AssetID), identity.PlanPath, PathComparison) ||
            !string.Equals(target.OutputBlockName, block.Name, StringComparison.Ordinal) ||
            target.SelectionIndex < 0 || target.SelectionIndex >= 32 ||
            string.IsNullOrWhiteSpace(target.ContextToken) || string.IsNullOrWhiteSpace(target.TargetID) ||
            !IsSelectedRun(target, identity.ResultPath!)) return null;

        string projectPath = SourceFilePaths.Canonical(target.Map.ProjectPath);
        string planPath = SourceFilePaths.Canonical(target.PlanPath);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath) ||
            !string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeDigest(target.Map.ProjectXml ?? string.Empty), target.Map.ProjectXmlSHA,
                StringComparison.OrdinalIgnoreCase)) return null;

        MapTypes? mapType = MapResultRenderService.ParseSupportedMapType(target.SelectedMapType);
        if (mapType is null) return null;
        int profileIndex = ResolveProfileIndex(target);
        if (profileIndex < 0 || profileIndex >= capturedProfiles.Count) return null;
        CapturedProfile profile = capturedProfiles[profileIndex];
        string sourceTime = profile.WireTime;
        if (target.ProfileTime is { Length: > 0 } expectedTime &&
            !string.Equals(sourceTime, expectedTime,
                StringComparison.Ordinal)) return null;

        if (result.AssociatedTerrain is not { } terrain ||
            result.Geometry?.FlowAreaLayer?.AllPropertyTables() is not { Length: > 0 } tables) return null;
        var terrainSnapshot = MapResultRenderService.CaptureTerrainSources(terrain);
        RasterMapViewExtent viewport = BoundViewport(target.Viewport);
        MapResultRenderService.ValidateViewport(viewport);
        var layer = new ResultMapRenderLayer(block, mapType.Value, terrain, tables);
        layer.ForceHorizontalRendering = target.ForceHorizontalRendering;
        layer.ProfileState.CurrentProfileIndex = profileIndex;
        layer.SetDefaultSymbology(project.Units);
        var definition = new RasterDefinition(new Extent(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY),
            viewport.PixelHeight, viewport.PixelWidth);
        if (!definition.IsValid() || definition.CellCount <= 0) return null;
        if (!layer.Resample(definition, out float[] values) || values is null) return null;
        MapResultRenderService.VerifyTerrainSources(terrainSnapshot);
        if (!string.Equals(ComputeRevision(projectPath), target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(planPath), target.PlanRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeRevision(visualizationPath), target.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase)) return null;

        var argb = new int[values.Length];
        var symbology = layer.RasterSymbology;
        if (symbology is null || symbology.SurfaceFill is null || symbology.SurfaceFill.Colors.Count == 0 ||
            symbology.SurfaceFill.ColorValues.Length != symbology.SurfaceFill.Colors.Count) return null;
        if (symbology.IsSurfaceFillEnabled) symbology.PaintSurface(values, argb, NoDataValue);
        byte[] rgba = new byte[checked(argb.Length * 4)];
        for (int i = 0; i < argb.Length; i++)
        {
            uint pixel = unchecked((uint)argb[i]);
            rgba[i * 4] = (byte)(pixel >> 16);
            rgba[i * 4 + 1] = (byte)(pixel >> 8);
            rgba[i * 4 + 2] = (byte)pixel;
            rgba[i * 4 + 3] = (byte)(pixel >> 24);
        }
        string frameResultPath = SourceFilePaths.Canonical(result.Filename ?? string.Empty);
        if (frameResultPath != resultPath) return null;
        return new PlanComputeLiveMapRenderFrame(target.TargetID, target.ProjectSessionID,
            target.OperationOwnerToken, target.OperationContextID, identity.RunId, resultPath,
            block.Name, target.ContextToken, target.Map.MapID, target.NodeID,
            target.SelectionIndex, targetSequence, profileIndex, profile.Name,
            sourceTime, mapType.Value.ToString(),
            terrainSnapshot.Path, terrainSnapshot.Revision, MapResultRenderService.LayerUnits(project.Units, mapType.Value),
            definition.Cols, definition.Rows, rgba, definition.MinX, definition.MinY, definition.MaxX, definition.MaxY,
            NoDataValue, symbology.SurfaceFill.Colors.Select((color, index) =>
                new MapResultRenderLegendStop(EncodeColor(color), symbology.SurfaceFill.ColorValues[index])).ToArray(),
            target.ForceHorizontalRendering);
    }

    private static bool IsSelectedRun(PlanComputeLiveMapRenderTarget target, string resultPath)
    {
        if (target.SelectionMode.Equals("Latest", StringComparison.OrdinalIgnoreCase)) return true;
        return target.SelectionMode.Equals("Explicit", StringComparison.OrdinalIgnoreCase) &&
            target.SelectedResultPath is { Length: > 0 } selected &&
            string.Equals(SourceFilePaths.Canonical(selected), SourceFilePaths.Canonical(resultPath), PathComparison);
    }

    private static bool CulvertNodeMatchesWriter(PlanComputeLiveMapRenderTarget target,
        string resultPath, string planPath)
    {
        if (string.IsNullOrWhiteSpace(target.Node.AssetID)) return false;
        return target.Node.NodeType switch
        {
            "MultiResult" => string.Equals(SourceFilePaths.Canonical(target.Node.AssetID),
                planPath, PathComparison),
            "Result" => target.SelectionIndex == 0 && target.SelectionMode == "Explicit" &&
                target.SelectedResultPath is { Length: > 0 } selectedPath &&
                string.Equals(SourceFilePaths.Canonical(target.Node.AssetID), resultPath, PathComparison) &&
                string.Equals(SourceFilePaths.Canonical(selectedPath), resultPath, PathComparison),
            _ => false
        };
    }

    private int ResolveProfileIndex(PlanComputeLiveMapRenderTarget target)
    {
        if (target.SelectionIndex == 0)
            return target.ProfileIndex >= 0 && target.ProfileIndex < capturedProfiles.Count
                ? target.ProfileIndex : -1;
        if (target.ProfileTime is not { Length: > 0 } selectedTime ||
            !DateTime.TryParse(selectedTime, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime targetTime)) return -1;
        foreach (CapturedProfile profile in capturedProfiles)
            if (Math.Abs((profile.Time - targetTime).Ticks) <= TimeSpan.TicksPerMillisecond * 10)
                return profile.Index;
        return -1;
    }

    private static RasterMapViewExtent BoundViewport(RasterMapViewExtent viewport)
    {
        double scale = Math.Min(1, (double)MaximumRasterSide / Math.Max(viewport.PixelWidth, viewport.PixelHeight));
        return viewport with
        {
            PixelWidth = Math.Max(1, (int)Math.Floor(viewport.PixelWidth * scale)),
            PixelHeight = Math.Max(1, (int)Math.Floor(viewport.PixelHeight * scale))
        };
    }

    private static string ComputeRevision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeDigest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string EncodeColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (block is not null) block.TimestepAdded -= OnTimestepAdded;
            lastFrameTicks.Clear();
            capturedProfiles.Clear();
            capturedCulvertProfiles.Clear();
            capturedCulvertRows.Dispose();
            capturedCulvertGeometry = null;
            capturedCulvertGeometryError = null;
        }
    }
}
