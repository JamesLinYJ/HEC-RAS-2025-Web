using Geospatial.IO;
using Geospatial.Vectors;
using Geospatial.GDALAssist;
using Ras.Layers;

namespace Ras.Backend;

public sealed record ProjectionRequest(string Type, string Value);
public sealed record ProjectionEvaluation(bool IsValid, string Name, string? Units, string? UnitName,
    int? EpsgCode, int? UtmZone, string? Wkt, string? Error, string NormalizedType, string NormalizedValue,
    string? Validity = null, bool? IsVertical = null, bool? IsProjected = null,
    ProjectionAreaOfUse? AreaOfUse = null);
public sealed record ProjectionAreaBounds(double Left, double Right, double Bottom, double Top);
public sealed record ProjectionAreaOfUse(ProjectionAreaBounds Geographic, ProjectionAreaBounds Projected);
public sealed record ProjectionSearchRequest(string? Query = null, double? Longitude = null, double? Latitude = null, int Limit = 30);
public sealed record ProjectionMatch(string Id, string Name, int Code, string Authority, string? Area = null);

public static class ProjectionService
{
    public static ProjectionEvaluation Evaluate(ProjectionRequest request)
    {
        ProjectionType type = ProjectionDefinition.TryParseProjectionType(request.Type);
        if (type == ProjectionType.Unknown) throw new ArgumentException("Unknown projection definition type.");
        if (request.Value.Length > 4 * 1024 * 1024) throw new ArgumentException("The projection definition is too large.");
        if (type == ProjectionType.ESRI && File.Exists(request.Value) && new FileInfo(request.Value).Length > 4 * 1024 * 1024)
            throw new ArgumentException("The projection definition is too large.");
        var definition = new ProjectionDefinition(type, request.Value);
        var result = definition.Evaluate(out var projection, out var units);
        if (!result.Result)
        {
            int? rejectedEpsgCode = projection is not null && projection.TryGetEPSGCode(out int rejectedCode)
                ? rejectedCode : null;
            int? rejectedUtmZone = projection is not null && projection.TryGetUTMZone(out int rejectedZone)
                && rejectedZone != 0 ? rejectedZone : null;
            return new(false, projection?.GetName() ?? "", null, projection?.GetSanitizedLinearUnitName(),
                rejectedEpsgCode, rejectedUtmZone, null,
                string.Join(Environment.NewLine, result.Messages.Select(message => message.EvaluatedMessage)), request.Type, request.Value,
                projection?.IsValid().ToString(), projection?.IsVertical(), projection?.IsProjected(),
                projection is null ? null : ReadAreaOfUse(projection));
        }
        string unitSystem = ProjectionDefinition.SerializeUnitSystemShort(units);
        if (type == ProjectionType.Local)
            return new(true, "Local", unitSystem, unitSystem, null, null, null, null, "Local", unitSystem);
        string wkt = projection.GetWKT();
        projection.TryGetUTMZone(out int zone);
        // Source ESRI definitions reference a PRJ file. Embed the interpreted WKT
        // on acceptance so the project survives moving the source file or platform.
        return new(true, projection.GetName(), unitSystem, projection.GetSanitizedLinearUnitName(),
            projection.TryGetEPSGCode(out int code) ? code : null, zone == 0 ? null : zone, wkt, null,
            type == ProjectionType.ESRI ? "WKT" : ProjectionDefinition.SerializeProjType(type), type == ProjectionType.ESRI ? wkt : request.Value,
            projection.IsValid().ToString(), projection.IsVertical(), projection.IsProjected(), ReadAreaOfUse(projection));
    }

    internal static ProjectionAreaOfUse? ReadAreaOfUse(Projection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!projection.TryGetAreaOfUse(out Extent geographic, out Extent projected)) return null;

        // The retained summary formats all eight edges. Keep that source result
        // only when every value is representable by the JSON number contract.
        // A non-finite edge is an unavailable area, not a rectangle to repair.
        if (!double.IsFinite(geographic.Left) || !double.IsFinite(geographic.Right)
            || !double.IsFinite(geographic.Bottom) || !double.IsFinite(geographic.Top)
            || !double.IsFinite(projected.Left) || !double.IsFinite(projected.Right)
            || !double.IsFinite(projected.Bottom) || !double.IsFinite(projected.Top))
            return null;

        return new ProjectionAreaOfUse(
            new ProjectionAreaBounds(geographic.Left, geographic.Right, geographic.Bottom, geographic.Top),
            new ProjectionAreaBounds(projected.Left, projected.Right, projected.Bottom, projected.Top));
    }

    public static ProjectionMatch[] Search(ProjectionSearchRequest request)
    {
        if (request.Limit is < 1 or > 100) throw new ArgumentException("Projection result limit must be between 1 and 100.");
        using var catalogue = new ProjDBReader();
        if (request.Longitude != null || request.Latitude != null)
        {
            if (request.Longitude is not double lon || request.Latitude is not double lat)
                throw new ArgumentException("Enter both longitude and latitude.");
            return catalogue.SearchProjectionByLocation(new Point(lon, lat), request.Limit)
                .Select(item => new ProjectionMatch(item.AuthName + ":" + item.Code, item.Name, item.Code, item.AuthName, item.Description)).ToArray();
        }
        string query = request.Query?.Trim() ?? "";
        if (query.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase)) query = query[5..].Trim();
        if (query.Length > 256) throw new ArgumentException("The projection search text is too long.");
        return catalogue.SearchProjectionBySubstring(query, request.Limit)
            .Select(item => new ProjectionMatch(item.auth_name + ":" + item.code, item.alt_name, item.code, item.auth_name)).ToArray();
    }
}
