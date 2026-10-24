using System.Globalization;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>
/// The retained reprojection dialog works on editable coordinate rows and does
/// not persist anything. Text remains text on the wire so an unfinished cell
/// can be represented without inventing a numeric value.
/// </summary>
public sealed record ReprojectionRequest(
    string SourceOption,
    string DestinationOption,
    ProjectionRequest? ProjectProjection,
    string? SourceCustomProjectionText,
    string? DestinationCustomProjectionText,
    ReprojectionRowInput[] Rows,
    string? NumberCulture = null);

public sealed record ReprojectionRowInput(string ID, string? XText, string? YText);

public sealed record ReprojectionSnapshot(
    string SourceSummary,
    string DestinationSummary,
    string? ErrorCode,
    string? ErrorMessage,
    ReprojectionRowResult[] Rows,
    ReprojectionProjectionSummary? SourceProjectionSummary = null,
    ReprojectionProjectionSummary? DestinationProjectionSummary = null);

public sealed record ReprojectionProjectionSummary(
    string Option,
    string? ProjectionName,
    string? Validity,
    string? UnitName,
    bool IsVertical,
    bool IsProjected,
    string? EpsgCode,
    string? UtmZone,
    ProjectionAreaOfUse? AreaOfUse = null);

public sealed record ReprojectionIdentifyRequest(string[] Paths);

public sealed record ReprojectionIdentifySnapshot(
    string? RecognizedPath,
    string? Wkt,
    ReprojectionProjectionSummary? ProjectionSummary,
    string? ErrorMessage = null);

/// <summary>
/// Status is one of transformed, blank, invalid, failed, or unavailable. X/Y
/// are omitted for non-finite and unsuccessful results so JSON never contains
/// NaN or infinity.
/// </summary>
public sealed record ReprojectionRowResult(string ID, string Status, double? X = null, double? Y = null);

public static class ReprojectionService
{
    public static ReprojectionSnapshot TransformReprojection(ReprojectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOption(request.SourceOption, "source");
        ValidateOption(request.DestinationOption, "destination");

        ReprojectionRowInput[] rows = request.Rows
            ?? throw new ArgumentException("Reprojection rows are required.");
        if (rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.ID))
            || rows.Select(row => row.ID).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new ArgumentException("Reprojection row IDs must be non-empty and unique.");

        CultureInfo? numberCulture = ResolveNumberCulture(request.NumberCulture);
        if (numberCulture is null)
        {
            return new ReprojectionSnapshot(request.SourceOption, request.DestinationOption,
                "unsupportedNumberCulture", "The requested number culture is not supported.",
                rows.Select(row => new ReprojectionRowResult(row.ID, "unavailable")).ToArray());
        }

        ProjectionResolution source = ResolveProjection(request.SourceOption,
            request.SourceCustomProjectionText, request.ProjectProjection, numberCulture, "source");
        if (source.ErrorCode is not null || source.Projection is null)
        {
            ProjectionResolution unavailableSource = source.ErrorCode is null
                ? NoProjectProjection("source")
                : source;
            return Unavailable(request, rows, unavailableSource, other: null, sourceError: true);
        }

        ProjectionResolution destination = ResolveProjection(request.DestinationOption,
            request.DestinationCustomProjectionText, request.ProjectProjection, numberCulture, "destination");
        if (destination.ErrorCode is not null || destination.Projection is null)
            return Unavailable(request, rows, destination.ErrorCode is null
                ? NoProjectProjection("destination")
                : destination, source, sourceError: false);

        Projection sourceProjection = source.Projection
            ?? throw new InvalidOperationException("Resolved source projection was unexpectedly empty.");
        Projection destinationProjection = destination.Projection
            ?? throw new InvalidOperationException("Resolved destination projection was unexpectedly empty.");
        ReprojectionRowResult[] results = rows.Select(row => TransformRow(row, sourceProjection,
            destinationProjection, numberCulture)).ToArray();
        return new ReprojectionSnapshot(source.Summary, destination.Summary, null, null, results,
            source.ProjectionSummary, destination.ProjectionSummary);
    }

    public static ReprojectionIdentifySnapshot IdentifyProjection(ReprojectionIdentifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string[] paths = request.Paths ?? throw new ArgumentException("Projection file paths are required.");
        foreach (string path in paths)
        {
            Projection? projection;
            try
            {
                // This retained helper checks project files, .prj files, HDF
                // project metadata, then GDAL-readable raster projections.
                projection = Ras.GeospatialHelpers.TryIdentifyProjection(path);
            }
            catch
            {
                projection = null;
            }

            if (projection is null) continue;
            try
            {
                return new ReprojectionIdentifySnapshot(path, projection.GetWKT(),
                    MakeProjectionSummary("Custom", projection));
            }
            catch (Exception error)
            {
                return new ReprojectionIdentifySnapshot(path, null, null, error.Message);
            }
        }

        return new ReprojectionIdentifySnapshot(null, null, null);
    }

    private static ReprojectionSnapshot Unavailable(
        ReprojectionRequest request,
        ReprojectionRowInput[] rows,
        ProjectionResolution error,
        ProjectionResolution? other,
        bool sourceError)
    {
        ProjectionResolution? source = sourceError ? error : other;
        ProjectionResolution? destination = sourceError ? null : error;
        string sourceSummary = source?.Summary ?? request.SourceOption;
        string destinationSummary = destination?.Summary ?? request.DestinationOption;
        return new ReprojectionSnapshot(sourceSummary, destinationSummary, error.ErrorCode, error.ErrorMessage,
            rows.Select(row => new ReprojectionRowResult(row.ID, "unavailable")).ToArray(),
            source?.ProjectionSummary, destination?.ProjectionSummary);
    }

    private static ReprojectionRowResult TransformRow(
        ReprojectionRowInput row,
        Projection sourceProjection,
        Projection destinationProjection,
        CultureInfo numberCulture)
    {
        if (string.IsNullOrWhiteSpace(row.XText) || string.IsNullOrWhiteSpace(row.YText))
            return new ReprojectionRowResult(row.ID, "blank");

        // BindableDoubleArrayColumn uses the default Float|AllowThousands
        // parsing rules. Select the requested UI culture without mutating the
        // backend process's CurrentCulture.
        const NumberStyles doubleStyles = NumberStyles.Float | NumberStyles.AllowThousands;
        bool hasX = double.TryParse(row.XText, doubleStyles, numberCulture, out double x);
        bool hasY = double.TryParse(row.YText, doubleStyles, numberCulture, out double y);
        if ((hasX && double.IsNaN(x)) || (hasY && double.IsNaN(y)))
            return new ReprojectionRowResult(row.ID, "blank");
        if (!hasX || !hasY)
            return new ReprojectionRowResult(row.ID, "invalid");

        try
        {
            Point point = Projector.TransformPoint(sourceProjection, destinationProjection, new Point(x, y));
            return double.IsFinite(point.X) && double.IsFinite(point.Y)
                ? new ReprojectionRowResult(row.ID, "transformed", point.X, point.Y)
                : new ReprojectionRowResult(row.ID, "failed");
        }
        catch
        {
            // The source dialog catches at row granularity. Keep other rows'
            // successful results even when a single native transform fails.
            return new ReprojectionRowResult(row.ID, "failed");
        }
    }

    private static ProjectionResolution ResolveProjection(
        string option,
        string? customText,
        ProjectionRequest? projectProjection,
        CultureInfo numberCulture,
        string side)
    {
        if (option == "Project")
        {
            if (projectProjection is null)
                return NoProjectProjection(side);

            ProjectionType projectionType = ProjectionDefinition.TryParseProjectionType(projectProjection.Type ?? string.Empty);
            var definition = new ProjectionDefinition(projectionType, projectProjection.Value ?? string.Empty);
            var result = definition.Evaluate(out var projection, out _);
            if (!result.Result)
            {
                string message = string.Join(Environment.NewLine,
                    result.Messages.Select(item => item.EvaluatedMessage));
                return new ProjectionResolution(null, "Project", null, "invalidProjectProjection", message);
            }

            // A Local project has units but no spatial projection in the source
            // VM, so selecting Project cannot produce transformed coordinates.
            if (projection is null)
                return NoProjectProjection(side);
            return new ProjectionResolution(projection, projection.GetName(),
                MakeProjectionSummary("Project", projection), null, null);
        }

        if (option == "WGS84")
            return ResolvedProjection(option, WellKnownProjections.WGS84);
        if (option == "WebMercator")
            return ResolvedProjection(option, WellKnownProjections.WebMercator);

        if (string.IsNullOrWhiteSpace(customText))
            return new ProjectionResolution(null, "Custom", null,
                side == "source" ? "missingCustomSource" : "missingCustomDestination",
                side == "source" ? "Please enter a custom source projection." : "Please enter a custom destination projection.");

        try
        {
            Projection projection;
            if (int.TryParse(customText, NumberStyles.Integer, numberCulture, out int epsgCode))
                projection = new EPSGProjection(epsgCode);
            else if (customText.StartsWith("+proj", StringComparison.Ordinal))
                projection = new Proj4Projection(customText);
            else
                projection = new WKTProjection(customText);
            return ResolvedProjection(option, projection);
        }
        catch
        {
            return new ProjectionResolution(null, "Custom", null,
                side == "source" ? "invalidCustomSource" : "invalidCustomDestination",
                side == "source" ? "Invalid custom source projection." : "Invalid custom destination projection.");
        }
    }

    private static ProjectionResolution NoProjectProjection(string side) =>
        new(null, "Project", null, side == "source" ? "noProjectSource" : "noProjectDestination",
            side == "source"
                ? "No project is open. Please select a different source projection."
                : "No project is open. Please select a different destination projection.");

    private static CultureInfo? ResolveNumberCulture(string? numberCulture)
    {
        if (numberCulture is null) return CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(numberCulture)) return null;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(numberCulture);
            return string.IsNullOrEmpty(culture.Name) ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static ProjectionResolution ResolvedProjection(string option, Projection projection) =>
        new(projection, SummaryName(option, projection), MakeProjectionSummary(option, projection), null, null);

    private static string SummaryName(string option, Projection projection) =>
        string.IsNullOrWhiteSpace(projection.GetName()) ? option : projection.GetName();

    private static ReprojectionProjectionSummary MakeProjectionSummary(string option, Projection projection)
    {
        string? epsgCode = projection.TryGetEPSGCode(out int epsg) ? epsg.ToString(CultureInfo.InvariantCulture) : null;
        string? utmZone = projection.TryGetUTMZone(out int zone) ? zone.ToString(CultureInfo.InvariantCulture) : null;
        return new ReprojectionProjectionSummary(option, projection.GetName(), projection.IsValid().ToString(),
            projection.GetSanitizedLinearUnitName(), projection.IsVertical(), projection.IsProjected(), epsgCode, utmZone,
            ProjectionService.ReadAreaOfUse(projection));
    }

    private static void ValidateOption(string? option, string side)
    {
        if (option is not ("Project" or "WGS84" or "WebMercator" or "Custom"))
            throw new ArgumentException($"Unknown {side} projection option.");
    }

    private sealed record ProjectionResolution(Projection? Projection, string Summary,
        ReprojectionProjectionSummary? ProjectionSummary, string? ErrorCode, string? ErrorMessage);
}
