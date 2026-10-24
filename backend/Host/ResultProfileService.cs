using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Geospatial.Vectors;
using Geospatial.Vectors.Helpers;
using H5Assist.VB;
using Ras.Layers;
using Ras.Output;

namespace Ras.Backend;

/// <summary>
/// Reads profile metadata and stored 2D time series from an immutable Result.
/// This service never opens the Result with a writer or resolves its embedded
/// models through the current project.
/// </summary>
public static class ResultProfileService
{
    private const int MaxMapElementLimit = 5000;
    private const int MaxMapVertexLimit = 120_000;

    public static ResultProfileCatalogResult Catalog(ResultProfileCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string resultPath = ValidateIdentity(request.ResultPath, request.Revision);
        using Result result = LoadResult(resultPath);
        ResultProfileFlowArea[] flowAreas = FlowAreas(result);
        ResultProfileOutputBlock[] outputBlocks = (result.OutputBlocks ?? [])
            .Where(block => block.ProfileCount > 0)
            .Select(block => Describe(result, block))
            .ToArray();
        EnsureUnchanged(resultPath, request.Revision);
        return new ResultProfileCatalogResult(resultPath, request.Revision, result.Name, outputBlocks, flowAreas);
    }

    public static ResultProfileSeriesResult Read(ResultProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string resultPath = ValidateIdentity(request.ResultPath, request.Revision);
        using Result result = LoadResult(resultPath);
        IHorizontalOutputBlock? outputBlock = (result.OutputBlocks ?? [])
            .FirstOrDefault(block => string.Equals(block.Name, request.OutputBlockName, StringComparison.Ordinal));
        if (outputBlock is null)
            return Unavailable(result, resultPath, request, string.Empty,
                "The selected result output block is no longer available.", null);

        if (outputBlock.ProfileCount <= 0)
            return Unavailable(result, resultPath, request, string.Empty,
                "This output block has no stored profiles.", null);

        var flowAreas = result.Geometry?.FlowAreaLayer;
        if (flowAreas is null || flowAreas.Count == 0)
            return Unavailable(result, resultPath, request, string.Empty,
                "This result has no embedded 2D flow area mesh.", null);
        if (request.FlowAreaIndex < 0 || request.FlowAreaIndex >= flowAreas.Count)
            throw new ArgumentOutOfRangeException(nameof(request.FlowAreaIndex), "Choose an available 2D flow area.");

        var flowArea = flowAreas[request.FlowAreaIndex];
        OutputVariable variable = FindVariable(outputBlock, request.Variable)
            ?? throw new ArgumentException("The selected output variable is not supported for this result block.");
        if (flowArea is null || flowArea.Mesh is null)
            return Unavailable(result, resultPath, request, flowArea?.Name ?? string.Empty,
                "The selected embedded flow area has no mesh.", variable);

        int elementCount = variable.MeshElement switch
        {
            Geospatial.Vectors.MeshElementType.Cell => flowArea.Mesh.CellCount,
            Geospatial.Vectors.MeshElementType.Face => flowArea.Mesh.FaceCount,
            _ => throw new NotSupportedException("This output variable is not stored on result cells or faces.")
        };
        if (request.ElementIndex < 0 || request.ElementIndex >= elementCount)
            throw new ArgumentOutOfRangeException(nameof(request.ElementIndex), "Choose an element within the selected flow area.");

        DateTime[] times = outputBlock.Times?.ToArray() ?? [];
        string[] profileNames = outputBlock.ProfileNames?.ToArray() ?? [];
        if (times.Length != outputBlock.ProfileCount || profileNames.Length != outputBlock.ProfileCount)
            throw new InvalidDataException("The result output block has inconsistent profile names or timestamps.");
        if (times.Length == 0)
            return Unavailable(result, resultPath, request, flowArea.Name,
                "This output block has no stored profile timestamps.", variable);

        string datasetName = DatasetName(outputBlock, variable, flowArea.Name);
        using var reader = new H5Reader(resultPath);
        if (!reader.ExistsAsDataset(datasetName))
            return Unavailable(result, resultPath, request, flowArea.Name,
                "The selected output variable has no stored values for this flow area.", variable);

        long[] dimensions = reader.GetDatasetDimensions(datasetName);
        if (dimensions.Length != 2 || dimensions[0] != times.Length || dimensions[1] != elementCount)
            throw new InvalidDataException("The stored result profile dataset does not match its output block metadata.");

        float[] values = [];
        reader.ReadColumn(datasetName, request.ElementIndex, ref values);
        if (values.Length != times.Length)
            throw new InvalidDataException("The stored result series has a different number of values than timestamps.");

        ResultProfilePoint[] points = Enumerable.Range(0, times.Length)
            .Select(index => new ResultProfilePoint(index, profileNames[index],
                times[index].ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                float.IsFinite(values[index]) ? values[index] : null))
            .ToArray();
        EnsureUnchanged(resultPath, request.Revision);
        (string quantity, string units) = VariableUnits(result, variable);
        return new ResultProfileSeriesResult(resultPath, request.Revision, result.Name,
            outputBlock.Name, variable.Name, flowArea.Name, request.FlowAreaIndex, request.ElementIndex,
            variable.MeshElement.ToString(), quantity, units, true, null, points);
    }

    /// <summary>
    /// Returns exact, selectable geometry from the selected Result's embedded
    /// flow-area mesh. Oversized extents return no partial feature set, so the
    /// client must zoom in before it can select a map element.
    /// </summary>
    public static ResultProfileMapResult Map(ResultProfileMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string resultPath = ValidateIdentity(request.ResultPath, request.Revision);
        using Result result = LoadResult(resultPath);

        bool isCell = request.ElementKind switch
        {
            "Cell" => true,
            "Face" => false,
            _ => throw new ArgumentException("The result profile map element kind must be Cell or Face.", nameof(request.ElementKind))
        };
        if (request.Limit < 1)
            throw new ArgumentOutOfRangeException(nameof(request.Limit), "The result profile map element limit must be positive.");

        var flowAreas = result.Geometry?.FlowAreaLayer;
        if (flowAreas is null || flowAreas.Count == 0)
            return UnavailableMap(resultPath, request, string.Empty, 0,
                "The selected embedded flow area has no mesh geometry.");
        if (request.FlowAreaIndex < 0 || request.FlowAreaIndex >= flowAreas.Count)
            throw new ArgumentOutOfRangeException(nameof(request.FlowAreaIndex), "Choose an available 2D flow area for the result profile map.");

        var flowArea = flowAreas[request.FlowAreaIndex];
        Mesh? mesh = flowArea?.Mesh;
        string flowAreaName = flowArea?.Name ?? string.Empty;
        if (mesh is null || (isCell ? mesh.CellCount == 0 : mesh.FaceCount == 0))
            return UnavailableMap(resultPath, request, flowAreaName, 0,
                "The selected embedded flow area has no mesh geometry.");

        int totalElements = isCell ? mesh.CellCount : mesh.FaceCount;
        Extent sourceMeshExtent = mesh.Extent;
        if (!ValidExtent(sourceMeshExtent))
        {
            EnsureUnchanged(resultPath, request.Revision);
            throw new InvalidDataException("The selected flow area has no finite mesh extent.");
        }
        var sourceExtent = ToMapExtent(sourceMeshExtent);
        Extent requestedExtent = sourceMeshExtent;
        if (request.Extent is { } requested)
        {
            if (!ValidExtent(requested))
                throw new ArgumentException("The requested result profile map extent must be finite and ordered.", nameof(request.Extent));
            var intersection = Intersect(sourceMeshExtent, requested);
            if (intersection is null)
                return CompleteMap(resultPath, request, flowAreaName, isCell, totalElements,
                    sourceExtent, requested, 0, [], 0);
            requestedExtent = intersection.Value;
        }

        int elementLimit = Math.Min(request.Limit, MaxMapElementLimit);
        var candidates = (isCell ? mesh.GetCellSpatialIndex() : mesh.GetFaceSpatialIndex())
            .GetElements(requestedExtent).Distinct().Order().ToArray();
        var elements = new List<ResultProfileMapElement>(Math.Min(candidates.Length, elementLimit));
        int intersectingElements = 0;
        int vertices = 0;

        foreach (int elementIndex in candidates)
        {
            GeometryTopologyCoordinate[][] paths = isCell
                ? ClipCell(mesh, elementIndex, requestedExtent)
                : ClipFace(mesh, elementIndex, requestedExtent);
            if (paths.Length == 0) continue;

            intersectingElements++;
            int elementVertices = paths.Sum(path => path.Length);
            if (intersectingElements > elementLimit || vertices + elementVertices > MaxMapVertexLimit)
            {
                EnsureUnchanged(resultPath, request.Revision);
                return new ResultProfileMapResult(resultPath, request.Revision,
                    flowAreaName, request.FlowAreaIndex, request.ElementKind, true, null,
                    sourceExtent, ToMapExtent(requestedExtent), totalElements, null, 0,
                    elementLimit, MaxMapVertexLimit, false, true, []);
            }

            vertices += elementVertices;
            elements.Add(new ResultProfileMapElement(elementIndex, paths));
        }

        return CompleteMap(resultPath, request, flowAreaName, isCell, totalElements,
            sourceExtent, ToMapExtent(requestedExtent), intersectingElements, elements.ToArray(), vertices,
            elementLimit);
    }

    private static ResultProfileSeriesResult Unavailable(Result result, string resultPath,
        ResultProfileRequest request, string flowAreaName, string reason, OutputVariable? variable)
    {
        EnsureUnchanged(resultPath, request.Revision);
        (string quantity, string units) = variable is null ? ("Unknown", "") : VariableUnits(result, variable);
        return new ResultProfileSeriesResult(resultPath, request.Revision, result.Name,
            request.OutputBlockName, request.Variable, flowAreaName, request.FlowAreaIndex, request.ElementIndex,
            variable?.MeshElement.ToString() ?? "Unknown", quantity, units, false, reason, []);
    }

    private static ResultProfileMapResult UnavailableMap(string resultPath, ResultProfileMapRequest request,
        string flowAreaName, int totalElements, string reason)
    {
        EnsureUnchanged(resultPath, request.Revision);
        return new ResultProfileMapResult(resultPath, request.Revision,
            flowAreaName, request.FlowAreaIndex, request.ElementKind, false, reason,
            null, null, totalElements, 0, 0, Math.Clamp(request.Limit, 1, MaxMapElementLimit),
            MaxMapVertexLimit, true, false, []);
    }

    private static ResultProfileMapResult CompleteMap(string resultPath, ResultProfileMapRequest request,
        string flowAreaName, bool isCell, int totalElements, GeometryTopologyExtent sourceExtent,
        GeometryTopologyExtent requestedExtent, int intersectingElements,
        ResultProfileMapElement[] elements, int vertices, int? elementLimit = null)
    {
        EnsureUnchanged(resultPath, request.Revision);
        int limit = elementLimit ?? Math.Min(request.Limit, MaxMapElementLimit);
        return new ResultProfileMapResult(resultPath, request.Revision,
            flowAreaName, request.FlowAreaIndex, isCell ? "Cell" : "Face", true, null,
            sourceExtent, requestedExtent, totalElements, intersectingElements, elements.Length,
            limit, MaxMapVertexLimit, true, false, elements);
    }

    private static GeometryTopologyCoordinate[][] ClipCell(Mesh mesh, int elementIndex, Extent extent)
    {
        Polygon? polygon = mesh.CellPolygon(elementIndex);
        if (polygon is null)
            throw new InvalidDataException("The embedded result mesh contains invalid cell geometry.");

        IMemoryOwner<Point>? owner = null;
        try
        {
            ClippedGeometryState state = polygon.ClipPolygon(extent, out owner, out int length);
            if (state == ClippedGeometryState.FullyOutOfBounds) return [];
            Point[] points = state switch
            {
                ClippedGeometryState.FullyInbounds => polygon.Points.ToArray(),
                ClippedGeometryState.Clipped when length >= 3 => owner!.Memory.Span[..length].ToArray(),
                _ => throw new InvalidDataException("The embedded result mesh contains invalid cell geometry.")
            };
            if (points.Length < 3 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new InvalidDataException("The embedded result mesh contains invalid cell geometry.");
            return [points.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray()];
        }
        finally { owner?.Dispose(); }
    }

    private static GeometryTopologyCoordinate[][] ClipFace(Mesh mesh, int elementIndex, Extent extent)
    {
        Polyline polyline = mesh.FacePolyline(elementIndex);
        if (polyline.Points.Count < 2
            || polyline.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("The embedded result mesh contains invalid face geometry.");

        var paths = new List<List<Point>>();
        List<Point>? current = null;
        for (int index = 1; index < polyline.Points.Count; index++)
        {
            Point start = polyline.Points[index - 1];
            Point end = polyline.Points[index];
            if (!ClipSegment(start, end, extent, out Point clippedStart, out Point clippedEnd))
            {
                if (current is { Count: >= 2 }) paths.Add(current);
                current = null;
                continue;
            }

            if (current is null || !SamePoint(current[^1], clippedStart))
            {
                if (current is { Count: >= 2 }) paths.Add(current);
                current = [clippedStart, clippedEnd];
            }
            else if (!SamePoint(current[^1], clippedEnd)) current.Add(clippedEnd);
        }
        if (current is { Count: >= 2 }) paths.Add(current);
        return paths.Select(path => path.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray()).ToArray();
    }

    private static bool ClipSegment(Point start, Point end, Extent extent,
        out Point clippedStart, out Point clippedEnd)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double low = 0;
        double high = 1;
        if (!ClipTest(-dx, start.X - extent.MinX, ref low, ref high)
            || !ClipTest(dx, extent.MaxX - start.X, ref low, ref high)
            || !ClipTest(-dy, start.Y - extent.MinY, ref low, ref high)
            || !ClipTest(dy, extent.MaxY - start.Y, ref low, ref high))
        {
            clippedStart = default;
            clippedEnd = default;
            return false;
        }
        clippedStart = new Point(start.X + low * dx, start.Y + low * dy);
        clippedEnd = new Point(start.X + high * dx, start.Y + high * dy);
        return true;
    }

    private static bool ClipTest(double p, double q, ref double low, ref double high)
    {
        if (p == 0) return q >= 0;
        double ratio = q / p;
        if (p < 0)
        {
            if (ratio > high) return false;
            if (ratio > low) low = ratio;
        }
        else
        {
            if (ratio < low) return false;
            if (ratio < high) high = ratio;
        }
        return true;
    }

    private static bool SamePoint(Point first, Point second) =>
        first.X == second.X && first.Y == second.Y;

    private static bool ValidExtent(Extent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY)
        && double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY)
        && extent.MinX < extent.MaxX && extent.MinY < extent.MaxY;

    private static bool ValidExtent(GeometryTopologyExtent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY)
        && double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY)
        && extent.MinX < extent.MaxX && extent.MinY < extent.MaxY;

    private static Extent? Intersect(Extent source, GeometryTopologyExtent request)
    {
        double minX = Math.Max(source.MinX, request.MinX);
        double minY = Math.Max(source.MinY, request.MinY);
        double maxX = Math.Min(source.MaxX, request.MaxX);
        double maxY = Math.Min(source.MaxY, request.MaxY);
        if (minX >= maxX || minY >= maxY) return null;
        return new Extent(minX, maxX, minY, maxY);
    }

    private static GeometryTopologyExtent ToMapExtent(Extent extent) =>
        new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);

    private static string ValidateIdentity(string resultPath, string revision)
    {
        if (string.IsNullOrWhiteSpace(resultPath) || !Path.IsPathFullyQualified(resultPath))
            throw new ArgumentException("Choose an absolute result file path.", nameof(resultPath));
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("Reload the result to obtain its source revision.", nameof(revision));
        string canonicalPath = SourceFilePaths.Canonical(resultPath);
        if (!File.Exists(canonicalPath)) throw new FileNotFoundException("The selected result file was not found.", canonicalPath);
        EnsureUnchanged(canonicalPath, revision);
        return canonicalPath;
    }

    private static void EnsureUnchanged(string resultPath, string revision)
    {
        if (!string.Equals(Revision(resultPath), revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Result LoadResult(string path)
    {
        var loaded = Result.TryLoad(path, out Result? result);
        if (!loaded || result is null)
            throw new InvalidDataException("The selected file is not a readable HEC-RAS result.");
        return result;
    }

    private static ResultProfileFlowArea[] FlowAreas(Result result)
    {
        if (result.Geometry?.FlowAreaLayer is not { } layer) return [];
        var areas = new List<ResultProfileFlowArea>(layer.Count);
        for (int index = 0; index < layer.Count; index++)
        {
            var area = layer[index];
            var mesh = area?.Mesh;
            areas.Add(new ResultProfileFlowArea(index, area?.Name ?? string.Empty, mesh?.CellCount ?? 0, mesh?.FaceCount ?? 0));
        }
        return areas.ToArray();
    }

    private static ResultProfileOutputBlock Describe(Result result, IHorizontalOutputBlock block)
    {
        string[] names = block.ProfileNames?.ToArray() ?? [];
        DateTime[] timestamps = block.Times?.ToArray() ?? [];
        ResultProfileVariable[] variables = Variables(block).Select(variable =>
        {
            (string quantity, string units) = VariableUnits(result, variable);
            return new ResultProfileVariable(variable.Name, variable.Name,
                variable.MeshElement.ToString(), quantity, units);
        }).ToArray();
        return new ResultProfileOutputBlock(block.Name, block.ProfileCount, names,
            timestamps.Select(time => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture)).ToArray(),
            variables);
    }

    private static IEnumerable<OutputVariable> Variables(IHorizontalOutputBlock block)
    {
        if (block is LegacyOutputBlock)
            return [OutputVariables.Legacy_WaterSurface, OutputVariables.Depth, OutputVariables.Velocity];
        if (block is OutputBlock)
            return [OutputVariables.Depth, OutputVariables.Velocity];
        return [];
    }

    private static (string Quantity, string Units) VariableUnits(Result result, OutputVariable variable)
    {
        DefaultUnits units = DefaultUnits.FromDefault(result.EmbeddedUnitSystem);
        return variable.Name switch
        {
            "Water Surface" or "Cell Depth" => ("Length", units.ShortLengthUnits),
            "Face Velocity" => ("Velocity", units.ShortVelocityUnits),
            _ => ("Unknown", "")
        };
    }

    private static OutputVariable? FindVariable(IHorizontalOutputBlock block, string variableID) =>
        Variables(block).FirstOrDefault(variable => string.Equals(variable.Name, variableID, StringComparison.Ordinal));

    private static string DatasetName(IHorizontalOutputBlock block, OutputVariable variable, string flowAreaName) => block switch
    {
        OutputBlock outputBlock => outputBlock.GetTimeSeriesDatasetName(variable, flowAreaName),
        LegacyOutputBlock legacy => legacy.Get2DOutputDatasetName(variable, flowAreaName, isSummary: false),
        _ => throw new NotSupportedException("This result output block does not expose stored 2D time series.")
    };
}
