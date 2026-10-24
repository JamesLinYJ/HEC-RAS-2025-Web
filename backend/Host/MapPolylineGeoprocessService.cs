using Geospatial.Vectors;
using System.Globalization;

namespace Ras.Backend;

public sealed record MapPolylineGeoprocessRequest(string Operation, double[][] Coordinates,
    double? Epsilon = null, int? WindowSize = null, int? Iterations = null,
    string?[]? ZValues = null, string?[]? MValues = null,
    double? OffsetDistance = null, string? OffsetSides = null, bool? IncludeEndpoints = null,
    MapPolylineGeoprocessAnchor? FromAnchor = null, MapPolylineGeoprocessAnchor? ToAnchor = null);

public sealed record MapPolylineGeoprocessAnchor(int SegmentIndex, double Fraction);
public sealed record MapPolylineGeoprocessPart(double[][] Coordinates, string?[]? ZValues = null,
    string?[]? MValues = null);
public sealed record MapPolylineGeoprocessResult(double[][] Coordinates,
    MapPolylineGeoprocessPart[]? Parts = null);

/// <summary>
/// Runs retained polyline geometry operations without touching a source file.
/// The native editor applies returned parts to its lossless GeoJSON AST after
/// checking its captured owner, source revision, and draft identity.
/// </summary>
public static class MapPolylineGeoprocessService
{
    private const int MaximumPointCount = 10_000;
    private const int MaximumWindowSize = 101;
    private const int MaximumIterations = 20;

    public static MapPolylineGeoprocessResult Transform(MapPolylineGeoprocessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operation is not ("filter" or "smooth" or "offset" or "deepConnect"))
            throw new ArgumentException("Polyline operation must be Filter, Smooth, Offset, or DeepConnect.");

        double[][] coordinates = request.Coordinates ?? throw new ArgumentException("Polyline coordinates are required.");
        if (coordinates.Length < 2 || coordinates.Length > MaximumPointCount ||
            coordinates.Any(point => point is null || point.Length != 2 ||
                !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
            throw new ArgumentException("Polyline operations require finite two-dimensional coordinates and a supported point count.");
        if (!IsNonzero(coordinates))
            throw new ArgumentException("Polyline operations require a nonzero line with at least two vertices.");

        var source = new Polyline(coordinates.Length);
        foreach (double[] coordinate in coordinates)
            source.Add(new Point(coordinate[0], coordinate[1]));

        if (request.Operation == "offset") return Offset(request, source);
        if (request.Operation == "deepConnect") return DeepConnect(request, source);

        Polyline transformed;
        switch (request.Operation)
        {
            case "filter":
            {
                double epsilon = request.Epsilon ?? 1.0;
                if (!double.IsFinite(epsilon) || epsilon <= 0)
                    throw new ArgumentException("Filter tolerance must be finite and positive.");
                transformed = new Polyline(source.DouglasPeuckerReduction(epsilon));
                break;
            }
            case "smooth":
            {
                int windowSize = request.WindowSize ?? 3;
                int iterations = request.Iterations ?? 2;
                if (windowSize < 1 || windowSize > MaximumWindowSize || iterations < 0 || iterations > MaximumIterations)
                    throw new ArgumentException("Smooth window and passes are outside the supported range.");
                // The retained source algorithm itself clamps WindowSize to an
                // odd value >= 3 and passes to >= 1.
                transformed = source.CloneWithMovingAverageSmoothing(windowSize, iterations);
                break;
            }
            default:
                throw new ArgumentException("Polyline operation must be Filter or Smooth.");
        }

        double[][] result = transformed.Points.Select(point => new[] { point.X, point.Y }).ToArray();
        if (result.Length < 2 || result.Length > coordinates.Length ||
            result.Any(point => !double.IsFinite(point[0]) || !double.IsFinite(point[1])) || !IsNonzero(result))
            throw new InvalidDataException("The geometry operation produced an invalid line.");
        return new MapPolylineGeoprocessResult(result);
    }

    private static MapPolylineGeoprocessResult Offset(MapPolylineGeoprocessRequest request, Polyline source)
    {
        if (request.ZValues is not null && request.ZValues.Length != source.Count ||
            request.MValues is not null && request.MValues.Length != source.Count)
            throw new ArgumentException("Offset Z/M coordinates must match the source vertex count.");
        if (request.ZValues is not null)
            source.TrackZ(request.ZValues.Select(ParseOrdinate).ToArray());
        if (request.MValues is not null)
            source.TrackM(request.MValues.Select(ParseOrdinate).ToArray());
        if (!Enum.TryParse(request.OffsetSides ?? "both", ignoreCase: true, out OffsetSides sides))
            throw new ArgumentException("Offset sides must be Both, Left, or Right.");
        double distance = request.OffsetDistance ?? 10;
        if (!double.IsFinite(distance) || distance <= 0)
            throw new ArgumentException("Offset distance must be finite and positive.");
        var results = new List<Polyline>(4);
        if (!OffsetPolylineFactory.TryComputeOffsetParts(source, distance, sides,
                request.IncludeEndpoints ?? false, results) || results.Count == 0)
            throw new InvalidDataException("The retained offset algorithm could not build a usable line.");

        var parts = results.Select(ToPart).ToArray();
        if (parts.Any(part => part.Coordinates.Length < 2 || !IsNonzero(part.Coordinates)))
            throw new InvalidDataException("The retained offset algorithm produced an invalid line.");
        return new MapPolylineGeoprocessResult(Array.Empty<double[]>(), parts);
    }

    private static MapPolylineGeoprocessResult DeepConnect(MapPolylineGeoprocessRequest request, Polyline source)
    {
        MapPolylineGeoprocessAnchor from = request.FromAnchor ??
            throw new ArgumentException("Deep Connect requires a starting anchor.");
        MapPolylineGeoprocessAnchor to = request.ToAnchor ??
            throw new ArgumentException("Deep Connect requires an ending anchor.");
        if ((uint)from.SegmentIndex >= (uint)source.SegmentCount ||
            (uint)to.SegmentIndex >= (uint)source.SegmentCount ||
            !double.IsFinite(from.Fraction) || from.Fraction < 0 || from.Fraction > 1 ||
            !double.IsFinite(to.Fraction) || to.Fraction < 0 || to.Fraction > 1)
            throw new ArgumentException("Deep Connect anchors must identify positions on the captured source line.");

        Polyline subsection = source.SubsectionDirected(source.PointOnLine(from.SegmentIndex, from.Fraction),
            source.PointOnLine(to.SegmentIndex, to.Fraction));
        if (subsection is null || subsection.Count == 0)
            return new MapPolylineGeoprocessResult(Array.Empty<double[]>());
        double[][] coordinates = subsection.Points.Select(point => new[] { point.X, point.Y }).ToArray();
        if (coordinates.Length > source.Count + 2 || coordinates.Any(point =>
                !double.IsFinite(point[0]) || !double.IsFinite(point[1])))
            throw new InvalidDataException("Deep Connect returned invalid source coordinates.");
        return new MapPolylineGeoprocessResult(coordinates);
    }

    private static MapPolylineGeoprocessPart ToPart(Polyline line)
    {
        double[][] coordinates = line.Points.Select(point => new[] { point.X, point.Y }).ToArray();
        string?[]? zValues = line.HasZ
            ? Enumerable.Range(0, line.Count).Select(index => FormatOrdinate(line.Z[index])).ToArray()
            : null;
        string?[]? mValues = line.HasM
            ? Enumerable.Range(0, line.Count).Select(index => FormatOrdinate(line.M[index])).ToArray()
            : null;
        return new MapPolylineGeoprocessPart(coordinates, zValues, mValues);
    }

    private static double ParseOrdinate(string? value)
    {
        if (value is null) return double.NaN;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        throw new ArgumentException("Offset Z/M coordinates must be valid numeric source ordinates.");
    }

    private static string? FormatOrdinate(double value) =>
        double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : null;

    private static bool IsNonzero(IReadOnlyList<double[]> coordinates)
    {
        for (int index = 1; index < coordinates.Count; index++)
            if (coordinates[index][0] != coordinates[index - 1][0] || coordinates[index][1] != coordinates[index - 1][1])
                return true;
        return false;
    }
}
