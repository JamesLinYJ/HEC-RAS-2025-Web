using System.Globalization;
using Geospatial.GDALAssist;
using Ras.Layers;

namespace Ras.Backend;

public sealed record MapGeoJSONProjectionPoint(string ID, double X, double Y);

public sealed record MapGeoJSONProjectionRequest(
    MapTreeRequest Map,
    string ProjectSessionID,
    string MapWorkflowIdentity,
    string SourceWorkflowIdentity,
    string ContextIdentity,
    CatalogTreeNodeLocator Node,
    string ExpectedSourceSHA,
    string Direction,
    MapGeoJSONProjectionPoint[] Points);

public sealed record MapGeoJSONProjectionResult(
    MapSourceNodeCapture Source,
    string ProjectSessionID,
    string MapWorkflowIdentity,
    string SourceWorkflowIdentity,
    string ContextIdentity,
    string CoreLayerContextIdentity,
    string Direction,
    string SourceProjectionWKT,
    string? ProjectProjectionWKT,
    MapGeoJSONProjectionPoint[] Points);

internal sealed record MapGeoJSONProjectedCoordinates(
    MapGeoJSONProjectionPoint[] Points,
    string ProjectionStatus,
    string SourceProjectionWKT,
    string? ProjectProjectionWKT);

/// <summary>
/// Transforms only the coordinates supplied by the native GeoJSON AST owner.
/// The operation is read-only and bound to one persisted map node and exact
/// source revision; it never writes or rewrites the source GeoJSON file.
/// </summary>
public static class MapGeoJSONProjectionService
{
    private const int MaximumPointCount = 250_000;
    private const int MaximumIdentityLength = 512;
    private const int MaximumWorkflowIdentityLength = 8_192;

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;
        public void Dispose() => DisposeProject(Project);
    }

    public static MapGeoJSONProjectionResult Transform(MapGeoJSONProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedNode(request.Map, request.Node);
        string sourcePath = Path.GetFullPath(location.SourcePath);
        string presentationPath = location.PresentationPath ?? sourcePath;
        if (!Path.GetExtension(presentationPath).Equals(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(presentationPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        MapSourceFamilyCapture family = MapSourceIdentity.CaptureFamily("geojson", sourcePath, []);
        MapSourceFileRevision sourceFile = family.Files.SingleOrDefault(file =>
            file.Required && file.Exists && string.Equals(file.Role, "source", StringComparison.Ordinal))
            ?? throw new SourceFileChangedException();
        if (sourceFile.Revision is null ||
            !sourceFile.Revision.Equals(request.ExpectedSourceSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        MapSourceNodeCapture capture = MapSourceIdentity.ResolvePersistedNode(request.Map, request.Node, family);
        MapSourceIdentity.VerifyPersistedNode(request.Map, capture);

        using ProjectScope projectScope = OpenProject(request.Map);
        Project project = projectScope.Project;
        MapGeoJSONProjectedCoordinates projected = ProjectToMap(project, request.Points, request.Direction);

        MapSourceIdentity.VerifyPersistedNode(request.Map, capture);
        MapSourceFamilyCapture currentFamily = MapSourceIdentity.CaptureFamily("geojson", sourcePath, []);
        if (!SameFamily(family, currentFamily)) throw new SourceFileChangedException();
        return new(capture, request.ProjectSessionID, request.MapWorkflowIdentity,
            request.SourceWorkflowIdentity, request.ContextIdentity,
            request.Map.CoreLayerContext?.IdentityToken ?? string.Empty, request.Direction,
            projected.SourceProjectionWKT, projected.ProjectProjectionWKT, projected.Points);
    }

    /// <summary>Projects already validated RFC 7946 WGS84 coordinates using the same retained map workflow.</summary>
    internal static MapGeoJSONProjectedCoordinates ProjectToMap(
        Project project,
        IReadOnlyList<MapGeoJSONProjectionPoint> points,
        string direction = "sourceToProject")
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(points);
        if (direction is not ("sourceToProject" or "projectToSource") ||
            points.Count > MaximumPointCount || points.Any(point => point is null ||
                string.IsNullOrWhiteSpace(point.ID) || point.ID.Length > MaximumIdentityLength ||
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
            points.Select(point => point.ID).Distinct(StringComparer.Ordinal).Count() != points.Count)
            throw new InvalidDataException("GeoJSON contains invalid or excessive coordinates for map projection.");

        string sourceWkt = WellKnownProjections.WGS84.GetWKT();
        string? projectWkt = project.Projection is null ? null : NonEmptyWkt(project.Projection);
        if (project.ProjectionDefinition.Type == ProjectionType.Local)
        {
            if (!project.ProjectionDefinition.Evaluate(out Projection? evaluatedProjection, out _).Result ||
                evaluatedProjection is not null || project.Projection is not null)
                throw new InvalidDataException("The project does not have a valid Local coordinate system.");
            return new(points.ToArray(), "localProjectSourceCoordinates", sourceWkt, null);
        }

        if (project.Projection is null || projectWkt is null)
            throw new InvalidDataException("The project has no valid target projection for GeoJSON map coordinates.");
        if (points.Count == 0)
            return new([], "projected", sourceWkt, projectWkt);

        var projection = new ProjectionRequest(project.ProjectionDefinition.Type.ToString(),
            project.ProjectionDefinition.Value);
        string sourceOption = direction == "sourceToProject" ? "WGS84" : "Project";
        string destinationOption = direction == "sourceToProject" ? "Project" : "WGS84";
        ReprojectionSnapshot transformed = ReprojectionService.TransformReprojection(new(
            sourceOption, destinationOption, projection, null, null,
            points.Select(point => new ReprojectionRowInput(point.ID,
                point.X.ToString("R", CultureInfo.InvariantCulture),
                point.Y.ToString("R", CultureInfo.InvariantCulture))).ToArray(), "en-US"));
        if (transformed.ErrorCode is not null || transformed.Rows.Length != points.Count ||
            transformed.Rows.Any(row => row.Status != "transformed" || row.X is not double x ||
                row.Y is not double y || !double.IsFinite(x) || !double.IsFinite(y)))
            throw new InvalidDataException(transformed.ErrorMessage ??
                "The project projection could not transform every GeoJSON coordinate.");

        MapGeoJSONProjectionPoint[] output = transformed.Rows.Select(row =>
            new MapGeoJSONProjectionPoint(row.ID, row.X!.Value, row.Y!.Value)).ToArray();
        return new(output, "projected", sourceWkt, projectWkt);
    }

    private static void ValidateRequest(MapGeoJSONProjectionRequest request)
    {
        if (request.Map is null || request.Node is null || request.Points is null ||
            !Guid.TryParse(request.ProjectSessionID, out _) ||
            !string.Equals(request.Map.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal) ||
            !HasIdentity(request.MapWorkflowIdentity, MaximumWorkflowIdentityLength) ||
            !HasIdentity(request.SourceWorkflowIdentity, MaximumWorkflowIdentityLength) ||
            !HasIdentity(request.ContextIdentity, MaximumWorkflowIdentityLength) ||
            request.Direction is not ("sourceToProject" or "projectToSource") ||
            request.Node.NodeType != "MapLayer" ||
            request.Node.MapType is not ("PointsVector" or "PolylinesVector" or "PolygonsVector") ||
            !IsSHA256(request.ExpectedSourceSHA) || request.Points.Length > MaximumPointCount ||
            request.Points.Any(point => point is null || string.IsNullOrWhiteSpace(point.ID) ||
                point.ID.Length > MaximumIdentityLength || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
            request.Points.Select(point => point.ID).Distinct(StringComparer.Ordinal).Count() != request.Points.Length)
            throw new ArgumentException("The map-owned GeoJSON projection request is incomplete or invalid.");
    }

    private static ProjectScope OpenProject(MapTreeRequest map) =>
        new(CoreLayerImportService.OpenProject(Path.GetFullPath(map.ProjectPath), map.ProjectXml,
            map.CoreLayerContext));

    private static string? NonEmptyWkt(Projection projection)
    {
        string value = projection.GetWKT();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(Uri.IsHexDigit);

    private static bool HasIdentity(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength;

    private static bool SameFamily(MapSourceFamilyCapture first, MapSourceFamilyCapture second) =>
        string.Equals(first.Family, second.Family, StringComparison.Ordinal) &&
        string.Equals(first.SourcePath, second.SourcePath, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        string.Equals(first.Revision, second.Revision, StringComparison.OrdinalIgnoreCase) &&
        first.Files.SequenceEqual(second.Files);

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }
}
