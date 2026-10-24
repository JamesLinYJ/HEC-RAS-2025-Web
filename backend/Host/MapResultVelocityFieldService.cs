using System.Buffers;
using System.Buffers.Binary;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Mapping;
using Ras.Output;
using Ras.Rendering;
using Utility.Memory;

namespace Ras.Backend;

/// <summary>
/// Supplies the actual map-scoped Result velocity vectors used by the source
/// RasMapVelocityParticlesRenderer. MultiResult follows only its slot-zero
/// keyframe Result, just as the retained renderer does.
/// </summary>
public static class MapResultVelocityFieldService
{
    private const int ValuesPerCell = 3;
    private const int BytesPerValue = sizeof(float);
    private const int MaximumVelocityRasterSide = 384;

    public static MapResultVelocityFieldResult Sample(MapResultVelocityFieldRequest request)
        => Sample(request, requireSelectedVelocityMap: true);

    /// <summary>
    /// Samples the same Result VelocityMapper for the original Result/MultiResult
    /// velocity-arrow annotation. The active Result map may display another
    /// map type; the particle endpoint intentionally keeps its stricter gate.
    /// </summary>
    public static MapResultVelocityFieldResult SampleArrows(MapResultVelocityFieldRequest request)
        => Sample(request, requireSelectedVelocityMap: false);

    private static MapResultVelocityFieldResult Sample(MapResultVelocityFieldRequest request,
        bool requireSelectedVelocityMap)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Node);
        if (string.IsNullOrWhiteSpace(request.RequestID) ||
            string.IsNullOrWhiteSpace(request.ExpectedResultPath) ||
            string.IsNullOrWhiteSpace(request.ExpectedResultRevision))
            throw new ArgumentException("A velocity field request must identify its request and selected Result source.", nameof(request));

        MapResultRenderService.ValidateViewport(request.Viewport);
        if (request.Viewport.PixelWidth > MaximumVelocityRasterSide ||
            request.Viewport.PixelHeight > MaximumVelocityRasterSide)
            throw new ArgumentException("The Result velocity viewport exceeds the bounded raster dimensions.", nameof(request));
        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.SelectionOverrides, request.ContextToken);
        if (!string.Equals(context.Sources.MapID, request.Map.MapID, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        MapResultActiveNode? activeNode = context.Sources.ActiveNodes.SingleOrDefault(node =>
            MapResultRenderService.SameLocator(node.Node, request.Node) &&
            string.Equals(MapResultRenderService.NodeIdentity(node.Node),
                MapResultRenderService.NodeIdentity(request.Node), StringComparison.Ordinal));
        if (activeNode is null)
            throw new SourceFileChangedException();
        if (request.ForceHorizontalRendering && activeNode.NodeKind != "MultiResult")
            throw new SourceFileChangedException();
        if (activeNode.NodeKind is not ("Result" or "MultiResult") ||
            (requireSelectedVelocityMap &&
             MapResultRenderService.ParseSupportedMapType(activeNode.SelectedMapType) != MapTypes.Velocity))
            throw new InvalidOperationException(requireSelectedVelocityMap
                ? "Select a visible Result Velocity map before showing velocity particles."
                : "Select an active Result or MultiResult context before showing velocity arrows.");

        ResolvedMapResultNode[] selectedContexts = context.ResultContexts
            .Where(candidate => candidate.Selections.Any(selection =>
                MapResultRenderService.SameLocator(selection.Node, activeNode.Node)))
            .OrderBy(candidate => MapResultRenderService.SelectionIndicesFor(candidate, activeNode.Node)
                .DefaultIfEmpty(int.MaxValue).Min())
            .ToArray();
        ResolvedMapResultNode? selectedResult = activeNode.NodeKind == "MultiResult"
            ? selectedContexts.FirstOrDefault(candidate =>
                MapResultRenderService.SelectionIndicesFor(candidate, activeNode.Node).Contains(0))
            : selectedContexts.FirstOrDefault();
        if (selectedResult is null)
            throw new InvalidDataException("The active Result map has no selected Result source.");

        string selectedPath = SourceFilePaths.Canonical(selectedResult.ResultPath);
        string expectedPath = SourceFilePaths.Canonical(request.ExpectedResultPath);
        if (!string.Equals(selectedPath, expectedPath, StringComparison.Ordinal) ||
            !string.Equals(selectedResult.Revision, request.ExpectedResultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        IHorizontalOutputBlock outputBlock = selectedResult.Result.BaseOutputBlock
            ?? throw new InvalidDataException("The selected Result has no base horizontal output block.");
        MapResultRenderService.ValidateProfiles(outputBlock);
        if (request.ProfileIndex < 0 || request.ProfileIndex >= outputBlock.ProfileCount)
            throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex),
                "The selected profile is outside the Result's stored profile timeline.");

        Terrain terrain = selectedResult.Result.AssociatedTerrain
            ?? throw new InvalidDataException("The selected Result has no current-project associated terrain.");
        MeshPropertyTables[] tables = selectedResult.Result.Geometry?.FlowAreaLayer?.AllPropertyTables()
            ?? throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");
        if (tables.Length == 0)
            throw new InvalidDataException("The selected Result has no embedded flow-area property tables.");

        MapResultRenderService.TerrainSourceSnapshot terrainSnapshot =
            MapResultRenderService.CaptureTerrainSources(terrain);
        var definition = new RasterDefinition(new Extent(request.Viewport.MinX,
            request.Viewport.MaxX, request.Viewport.MinY, request.Viewport.MaxY),
            request.Viewport.PixelHeight, request.Viewport.PixelWidth);
        if (!definition.IsValid() || definition.CellCount <= 0 ||
            !double.IsFinite(definition.CellSize) || definition.CellSize <= 0)
            throw new ArgumentException("The current map viewport cannot be sampled as a velocity field.", nameof(request));

        var renderLayer = new ResultMapRenderLayer(outputBlock, MapTypes.Velocity, terrain, tables);
        renderLayer.ForceHorizontalRendering = request.ForceHorizontalRendering;
        if (renderLayer.Mapper is not VelocityMapper velocityMapper)
            throw new InvalidDataException("The selected Result Velocity map has no source velocity mapper.");
        var samplingParameters = request.ForceHorizontalRendering
            ? GeometrySamplingParameters.ForceHorizontalMode
            : GeometrySamplingParameters.Default;
        var mapPoints = new GeometryMapPoints(selectedResult.Result.Geometry!, definition, samplingParameters);
        using IMemoryOwner<float> terrainOwner = SharedMemoryPool<float>.Rent(definition.CellCount);
        Memory<float> terrainValues = terrainOwner.Memory.Slice(0, definition.CellCount);
        terrain.Resample(new ResampleArgs(definition), terrainValues);

        float[] velocityX = new float[definition.CellCount];
        float[] velocityY = new float[definition.CellCount];
        float[] magnitude = new float[definition.CellCount];
        MapResultChartService.EnsureUnchanged(selectedPath, selectedResult.Revision);
        MapResultRenderService.VerifyTerrainSources(terrainSnapshot);
        velocityMapper.Compute(request.ProfileIndex, mapPoints, terrainValues,
            velocityX, velocityY, magnitude);

        byte[] interleaved = PackVelocity(velocityX, velocityY, magnitude);
        MapResultChartService.EnsureUnchanged(selectedPath, selectedResult.Revision);
        MapResultRenderService.VerifyTerrainSources(terrainSnapshot);
        context.EnsureUnchanged();

        return new MapResultVelocityFieldResult(request.RequestID, context.Sources.ContextToken,
            MapResultRenderService.NodeIdentity(activeNode.Node), context.Sources.MapID,
            activeNode.Node, selectedPath, selectedResult.Revision, selectedResult.Result.Name,
            terrainSnapshot.Path, terrainSnapshot.Revision, MapTypes.Velocity.ToString(),
            selectedResult.SelectionIndex, request.ProfileIndex,
            outputBlock.ProfileNames[request.ProfileIndex],
            MapResultRenderService.FormatTime(outputBlock.Times[request.ProfileIndex]),
            definition.Cols, definition.Rows, definition.CellSize,
            definition.MinX, definition.MinY, definition.MaxX, definition.MaxY,
            MapResultRenderService.NoDataValue, interleaved, request.ForceHorizontalRendering);
    }

    private static byte[] PackVelocity(float[] velocityX, float[] velocityY, float[] magnitude)
    {
        if (velocityX.Length != velocityY.Length || velocityX.Length != magnitude.Length)
            throw new InvalidDataException("The source velocity mapper returned arrays with different dimensions.");
        int valueCount = checked(velocityX.Length * ValuesPerCell);
        byte[] result = new byte[checked(valueCount * BytesPerValue)];
        for (int index = 0; index < velocityX.Length; index++)
        {
            int offset = checked(index * ValuesPerCell * BytesPerValue);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(offset, BytesPerValue), velocityX[index]);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(offset + BytesPerValue, BytesPerValue), velocityY[index]);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(offset + 2 * BytesPerValue, BytesPerValue), magnitude[index]);
        }
        return result;
    }
}
