using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Geospatial.GDALAssist;
using Geospatial.IO.MetImport;
using Geospatial.IO.Network.Cumulus;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Meteorology;
using Utility.Auth.Oidc;
using Utility.Logging;
using Utility.Network.Cumulus;
using Utility.Progress;
using Utility.Units;

namespace Ras.Backend;

public sealed record CumulusEnvironmentRequest(string? EnvironmentId = null);
public sealed record CumulusEnvironmentOption(string Id, string Name, bool RequiresSignIn,
    string? AuthorizeEndpoint, string? ClientId, string? RedirectUri, string? IdpHint,
    string? MockAccessToken, bool IsSelectable = true);
public sealed record CumulusTokenExchangeRequest(string EnvironmentId, string Code, string Verifier);
public sealed record CumulusTokenRefreshRequest(string EnvironmentId, string RefreshToken);
public sealed record CumulusTokenSet(string AccessToken, string? RefreshToken,
    DateTime AccessExpiresUtc, DateTime RefreshExpiresUtc);
public sealed record CumulusProductsRequest(string EnvironmentId, string? AccessToken);
public sealed record CumulusProductOption(string Id, string Name, string? Slug, string? Label,
    string? Parameter, string? Unit, int TemporalResolution, int TemporalDuration,
    string? DssDatatype, string? AfterUtc, string? BeforeUtc, bool IsPrecip);
public sealed record CumulusGridQueryRequest(string EnvironmentId, string? AccessToken,
    string ProductId, string AfterUtc, string BeforeUtc);
public sealed record CumulusGridSourceOption(string Id, string DatetimeUtc, string VersionUtc);
public sealed record CumulusGridFootprintRequest(string EnvironmentId, string? AccessToken,
    string ProductId, string AfterUtc, string BeforeUtc, string SourceId, string DatetimeUtc,
    string VersionUtc, string TargetProjectionWkt);
public sealed record CumulusFootprintCoordinate(double X, double Y);
public sealed record CumulusGridFootprintResult(string SourceId, string DatetimeUtc,
    string VersionUtc, string SourceProjectionWkt, string TargetProjectionWkt,
    CumulusImportExtent Extent, CumulusFootprintCoordinate[] Points);
public sealed record CumulusGridImportSource(string Id, string DatetimeUtc, string VersionUtc);
public sealed record CumulusImportExtent(double MinX, double MaxX, double MinY, double MaxY);
public sealed record CumulusGridImportRequest(ModelSaveRequest Model, string EnvironmentId,
    string? AccessToken, string ProductId, string AfterUtc, string BeforeUtc,
    CumulusGridImportSource[] Sources, string LayerId,
    CumulusImportExtent? ClipExtent = null, string? ClipProjectionWkt = null,
    string PrecipitationDataType = "per-cum", string PrecipitationUnits = "mm");
public sealed record CumulusGridImportResult(string State, ModelSnapshot? Snapshot = null,
    string? ErrorMessage = null);

/// <summary>
/// Holds the real Cumulus selection until it is applied to SaveAsync's staging
/// Boundary Condition. This keeps the downloaded raster and the user's draft
/// in the same atomic model-file transaction.
/// </summary>
public sealed class CumulusGridImportOperation : IDisposable
{
    private readonly IGriddedDataSelection selection;
    private bool disposed;

    internal CumulusGridImportOperation(IGriddedDataSelection selection, string layerId,
        string precipitationDataType, string precipitationUnits, ProgressReporter reporter)
    {
        this.selection = selection;
        LayerId = layerId;
        PrecipitationDataType = precipitationDataType;
        PrecipitationUnits = precipitationUnits;
        Reporter = reporter;
    }

    internal string LayerId { get; }
    internal string PrecipitationDataType { get; }
    internal string PrecipitationUnits { get; }
    internal ProgressReporter Reporter { get; }

    internal void Apply(BoundaryCondition boundary)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(boundary);
        if (Reporter.CancelRequested) throw new OperationCanceledException("Cumulus grid import was cancelled.");

        SpatialBoundaryConditionBase layer = LayerId switch
        {
            "precipitation" => boundary.Precipitation,
            "wind" => boundary.Wind,
            "airPressure" => boundary.AirPressure,
            "airDensity" => boundary.AirDensity,
            _ => throw new ArgumentException("This Boundary Condition layer does not support gridded imports.")
        };

        if (layer is PrecipitationLayer precipitation && selection.SupportsPrecipConversion &&
            selection.Surface is ITimeSeriesRaster<float> timed)
        {
            string parseUnits = PrecipitationDataType switch
            {
                "per-cum" => PrecipitationUnits,
                "per-avg" => PrecipitationUnits + "/hr",
                _ => throw new ArgumentException("Choose a supported precipitation data type.")
            };
            Precipitation units = Precipitation.TryParse(parseUnits)
                ?? throw new ArgumentException("Choose supported precipitation units.");
            Length desiredVerticalUnits = boundary.Project.Units == UnitSystem.USCustomary
                ? Length.Inches : Length.Millimeters;
            precipitation.ImportRaster(timed, selection.Projection, units, desiredVerticalUnits, Reporter);
        }
        else
        {
            // The retained importer writes a non-precipitation selection
            // directly to its selected spatial boundary layer.
            layer.WriteGriddedSelection(selection);
        }

        if (Reporter.CancelRequested) throw new OperationCanceledException("Cumulus grid import was cancelled.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        (selection as IDisposable)?.Dispose();
    }
}

public static partial class ModelEditorAdapter
{
    private static readonly TimeSpan CumulusRequestTimeout = TimeSpan.FromSeconds(90);
    // GDAL_HTTP_HEADERS is process-global. Keep each authenticated remote COG
    // open/read isolated so a probe or import for another environment cannot
    // replace its Authorization header mid-read.
    private static readonly SemaphoreSlim CumulusCogAccessGate = new(1, 1);

    public static async Task<CumulusEnvironmentOption[]> GetCumulusEnvironmentsAsync(
        CumulusEnvironmentRequest? request = null)
    {
        _ = request;
        List<(string Id, CumulusEnvironment Environment, bool IsSelectable)> environments = [];
#if DEBUG
        environments.Add(("local", CumulusEnvironment.Local, true));
#endif
        environments.Add(("dev", CumulusEnvironment.Dev, true));
        environments.Add(("test", CumulusEnvironment.Test, false));
        environments.Add(("prod", CumulusEnvironment.Prod, false));
        List<CumulusEnvironmentOption> result = [];
        foreach ((string id, CumulusEnvironment environment, bool isSelectable) in environments)
        {
            if (isSelectable) await environment.ResolveUrlsAsync().ConfigureAwait(false);
            result.Add(new(id, environment.Name, !environment.UseMock,
                !isSelectable || environment.UseMock ? null : environment.AuthorizeEndpoint,
                !isSelectable || environment.UseMock ? null : environment.ClientId,
                !isSelectable || environment.UseMock ? null : environment.RedirectUri,
                !isSelectable || environment.UseMock ? null : environment.IdpHint,
                environment.UseMock && isSelectable ? environment.MockToken : null, isSelectable));
        }
        return result.ToArray();
    }

    public static async Task<CumulusTokenSet> ExchangeCumulusTokenAsync(CumulusTokenExchangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Verifier))
            throw new ArgumentException("Complete the Cumulus sign-in before exchanging its authorization code.");
        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId).ConfigureAwait(false);
        if (environment.UseMock) throw new ArgumentException("The local Cumulus environment does not use browser sign-in.");
        OidcClient.TokenHttpClient = CreateCacPresentingTokenClient();
        OidcTokenSet tokens = await new OidcClient().ExchangeCodeAsync(environment, request.Code, request.Verifier)
            .ConfigureAwait(false);
        return new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessExpiresUtc, tokens.RefreshExpiresUtc);
    }

    public static async Task<CumulusTokenSet> RefreshCumulusTokenAsync(CumulusTokenRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new ArgumentException("A Cumulus refresh token is required.");
        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId).ConfigureAwait(false);
        if (environment.UseMock) throw new ArgumentException("The local Cumulus environment does not use token refresh.");
        OidcClient.TokenHttpClient = CreateCacPresentingTokenClient();
        OidcTokenSet tokens = await new OidcClient().RefreshAsync(environment, request.RefreshToken)
            .ConfigureAwait(false);
        return new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessExpiresUtc, tokens.RefreshExpiresUtc);
    }

    public static async Task<CumulusProductOption[]> GetCumulusProductsAsync(CumulusProductsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId).ConfigureAwait(false);
        string token = TokenFor(environment, request.AccessToken);
        using CancellationTokenSource timeout = new(CumulusRequestTimeout);
        IReadOnlyList<CumulusProduct> products = await new CumulusClient(environment.ApiBaseUrl, token, environment.AppKey)
            .GetProductsAsync(timeout.Token).ConfigureAwait(false);
        return products.OrderBy(product => product.DisplayName, StringComparer.CurrentCulture)
            .Select(product => new CumulusProductOption(product.Id, product.DisplayName, product.Slug,
                product.Label, product.Parameter, product.Unit, product.TemporalResolution,
                product.TemporalDuration, product.DssDatatype,
                product.After is DateTime after ? FormatUtc(after) : null,
                product.Before is DateTime before ? FormatUtc(before) : null,
                product.IsPrecip)).ToArray();
    }

    public static async Task<CumulusGridSourceOption[]> QueryCumulusGridsAsync(CumulusGridQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId).ConfigureAwait(false);
        string token = TokenFor(environment, request.AccessToken);
        DateTime after = ParseUtc(request.AfterUtc, nameof(request.AfterUtc));
        DateTime before = ParseUtc(request.BeforeUtc, nameof(request.BeforeUtc));
        ValidateCumulusRange(after, before);
        using CancellationTokenSource timeout = new(CumulusRequestTimeout);
        var client = new CumulusClient(environment.ApiBaseUrl, token, environment.AppKey);
        IReadOnlyList<CumulusProduct> products = await client.GetProductsAsync(timeout.Token).ConfigureAwait(false);
        if (!products.Any(product => string.Equals(product.Id, request.ProductId, StringComparison.Ordinal)))
            throw new ArgumentException("The selected Cumulus product is no longer available. Refresh products and try again.");
        IReadOnlyList<CumulusCogFile> files = await client.GetCogFilesAsync(request.ProductId, after, before,
            timeout.Token).ConfigureAwait(false);
        CumulusCogFile[] materialized = files.ToArray();
        if (materialized.Any(file => string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.CogUrl)))
            throw new InvalidDataException("Cumulus returned a grid without a stable identity or COG URL.");
        if (materialized.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new InvalidDataException("Cumulus returned duplicate grid IDs; refresh the query before importing.");
        return materialized.OrderBy(file => file.Datetime)
            .ThenBy(file => file.Version ?? file.Datetime)
            .ThenBy(file => file.Id, StringComparer.Ordinal)
            .Select(file => new CumulusGridSourceOption(file.Id, FormatUtc(file.Datetime),
                FormatUtc(file.Version ?? file.Datetime))).ToArray();
    }

    public static async Task<CumulusGridFootprintResult> ProbeCumulusGridFootprintAsync(
        CumulusGridFootprintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceId) ||
            string.IsNullOrWhiteSpace(request.TargetProjectionWkt))
            throw new ArgumentException("Choose a Cumulus source and a valid target map projection.");

        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId)
            .ConfigureAwait(false);
        string token = TokenFor(environment, request.AccessToken);
        DateTime after = ParseUtc(request.AfterUtc, nameof(request.AfterUtc));
        DateTime before = ParseUtc(request.BeforeUtc, nameof(request.BeforeUtc));
        DateTime expectedDateTime = ParseUtc(request.DatetimeUtc, nameof(request.DatetimeUtc));
        DateTime expectedVersion = ParseUtc(request.VersionUtc, nameof(request.VersionUtc));
        ValidateCumulusRange(after, before);
        if (expectedDateTime < after || expectedDateTime > before)
            throw new ArgumentException("The selected Cumulus source is outside the requested date range.");

        using var timeout = new CancellationTokenSource(CumulusRequestTimeout);
        var client = new CumulusClient(environment.ApiBaseUrl, token, environment.AppKey);
        IReadOnlyList<CumulusProduct> products = await client.GetProductsAsync(timeout.Token).ConfigureAwait(false);
        _ = products.SingleOrDefault(product =>
            string.Equals(product.Id, request.ProductId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The selected Cumulus product is no longer available. Refresh products and try again.");
        IReadOnlyList<CumulusCogFile> files = await client.GetCogFilesAsync(
            request.ProductId, after, before, timeout.Token).ConfigureAwait(false);
        CumulusCogFile[] materialized = files.ToArray();
        if (materialized.Any(file => string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.CogUrl)))
            throw new InvalidDataException("Cumulus returned a grid without a stable identity or COG URL.");
        if (materialized.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new InvalidDataException("Cumulus returned duplicate grid IDs; refresh the query before probing coverage.");
        CumulusCogFile source = materialized.SingleOrDefault(file =>
            string.Equals(file.Id, request.SourceId, StringComparison.Ordinal))
            ?? throw new SourceFileChangedException();
        if (FormatUtc(source.Datetime) != request.DatetimeUtc ||
            FormatUtc(source.Version ?? source.Datetime) != request.VersionUtc ||
            source.Datetime != expectedDateTime || (source.Version ?? source.Datetime) != expectedVersion)
            throw new SourceFileChangedException();

        // Probe the exact COG chosen by the user. GDAL owns the remote read;
        // callers gate late replies against the captured project/map/source identity.
        // The auth config is process-global, so serialize it with actual imports.
        await CumulusCogAccessGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            CumulusGdal.ConfigureHttpAuth(token);
            using var raster = new GDALRaster(client.BuildCogVsiPath(source.CogUrl));
            Extent nativeExtent = raster.GetFullNativeExtents();
            Projection sourceProjection = raster.GetProjection();
            if (sourceProjection is null || !nativeExtent.IsFiniteAndValid())
                throw new InvalidDataException("The selected Cumulus COG has no readable spatial extent or projection.");

            Projection targetProjection = new WKTProjection(request.TargetProjectionWkt);
            string targetWkt = targetProjection.GetWKT();
            string sourceWkt = sourceProjection.GetPrettyWKT();
            if (string.IsNullOrWhiteSpace(targetWkt) || string.IsNullOrWhiteSpace(sourceWkt))
                throw new InvalidDataException("The selected Cumulus COG or target map projection is invalid.");

            List<Point> boundary = new(48);
            static double Lerp(double start, double end, double fraction) => start + (end - start) * fraction;
            for (int index = 0; index < 12; index++)
                boundary.Add(new Point(Lerp(nativeExtent.MinX, nativeExtent.MaxX, index / 12.0), nativeExtent.MinY));
            for (int index = 0; index < 12; index++)
                boundary.Add(new Point(nativeExtent.MaxX, Lerp(nativeExtent.MinY, nativeExtent.MaxY, index / 12.0)));
            for (int index = 0; index < 12; index++)
                boundary.Add(new Point(Lerp(nativeExtent.MaxX, nativeExtent.MinX, index / 12.0), nativeExtent.MaxY));
            for (int index = 0; index < 12; index++)
                boundary.Add(new Point(nativeExtent.MinX, Lerp(nativeExtent.MaxY, nativeExtent.MinY, index / 12.0)));

            Polygon projected = ReprojectionTransformer.GetOrMake(sourceProjection, targetProjection)
                .Transform(new Polygon(boundary));
            CumulusFootprintCoordinate[] points = projected.Points
                .Select(point => new CumulusFootprintCoordinate(point.X, point.Y)).ToArray();
            if (points.Length != 48 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new InvalidDataException("The selected Cumulus footprint could not be transformed to the map projection.");
            double minX = points.Min(point => point.X);
            double maxX = points.Max(point => point.X);
            double minY = points.Min(point => point.Y);
            double maxY = points.Max(point => point.Y);
            if (!(minX < maxX && minY < maxY))
                throw new InvalidDataException("The selected Cumulus footprint is empty in the map projection.");
            timeout.Token.ThrowIfCancellationRequested();

            return new CumulusGridFootprintResult(source.Id, FormatUtc(source.Datetime),
                FormatUtc(source.Version ?? source.Datetime), sourceWkt, request.TargetProjectionWkt,
                new CumulusImportExtent(minX, maxX, minY, maxY), points);
        }
        finally
        {
            CumulusCogAccessGate.Release();
        }
    }

    internal static async Task<CumulusGridImportResult> ImportCumulusGridsDuplexAsync(
        CumulusGridImportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(duplexControls);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing model draft for Cumulus import.");
        ValidateCumulusModel(model, request.LayerId);
        CumulusEnvironment environment = await ResolveCumulusEnvironmentAsync(request.EnvironmentId).ConfigureAwait(false);
        string token = TokenFor(environment, request.AccessToken);
        DateTime after = ParseUtc(request.AfterUtc, nameof(request.AfterUtc));
        DateTime before = ParseUtc(request.BeforeUtc, nameof(request.BeforeUtc));
        ValidateCumulusRange(after, before);
        ValidateSources(request.Sources);

        var reporter = new CumulusReporter(report);
        using var requestCancellation = new CancellationTokenSource();
        using var stopControls = new CancellationTokenSource();
        Task controlPump = PumpControlsAsync(duplexControls, reporter.Progress, requestCancellation, stopControls.Token);
        bool sourceCommitted = false;
        bool ownsCogAccessGate = false;
        ModelSnapshot? committedSnapshot = null;
        try
        {
            reporter.Progress.ReportMessage("Resolving the selected Cumulus grid sources…");
            using var queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation.Token);
            queryTimeout.CancelAfter(CumulusRequestTimeout);
            var client = new CumulusClient(environment.ApiBaseUrl, token, environment.AppKey);
            IReadOnlyList<CumulusProduct> products = await client.GetProductsAsync(queryTimeout.Token).ConfigureAwait(false);
            _ = products.SingleOrDefault(item =>
                string.Equals(item.Id, request.ProductId, StringComparison.Ordinal))
                ?? throw new ArgumentException("The selected Cumulus product is no longer available. Refresh products and try again.");
            IReadOnlyList<CumulusCogFile> availableFiles = await client.GetCogFilesAsync(
                request.ProductId, after, before, queryTimeout.Token).ConfigureAwait(false);
            CumulusCogFile[] available = availableFiles.ToArray();
            if (available.Any(file => string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.CogUrl)))
                throw new InvalidDataException("Cumulus returned a grid without a stable identity or COG URL.");
            if (available.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != available.Length)
                throw new InvalidDataException("Cumulus returned duplicate grid IDs; refresh the query before importing.");
            Dictionary<string, CumulusCogFile> byId = available.ToDictionary(file => file.Id, StringComparer.Ordinal);
            CumulusCogFile[] selected = request.Sources.Select(source =>
            {
                if (!byId.TryGetValue(source.Id, out CumulusCogFile? current) ||
                    FormatUtc(current.Datetime) != source.DatetimeUtc ||
                    FormatUtc(current.Version ?? current.Datetime) != source.VersionUtc)
                    throw new SourceFileChangedException();
                return current;
            }).OrderBy(file => file.Datetime).ToArray();
            if (selected.GroupBy(file => file.Datetime).Any(group => group.Count() > 1))
                throw new ArgumentException("Choose only one Cumulus source version for each valid time.");
            if (reporter.Progress.CancelRequested || requestCancellation.IsCancellationRequested)
                return new("cancelled");
            if (selected.Any(file => file.Datetime < after || file.Datetime > before))
                throw new InvalidDataException("Cumulus returned a selected grid outside the requested date range.");

            await CumulusCogAccessGate.WaitAsync(requestCancellation.Token).ConfigureAwait(false);
            ownsCogAccessGate = true;
            CumulusGdal.ConfigureHttpAuth(token);
            var controller = new CumulusCogRasterImportController();
            if (request.ClipExtent is { } clip)
            {
                ValidateExtent(clip);
                if (string.IsNullOrWhiteSpace(request.ClipProjectionWkt))
                    throw new ArgumentException("The drawn Cumulus area has no captured map projection.");
                controller.ClipExtent = new Extent(clip.MinX, clip.MaxX, clip.MinY, clip.MaxY);
                controller.ClipProjection = new WKTProjection(request.ClipProjectionWkt);
            }
            // Without a download area, retain the source controller's
            // infinite-extent and absent-projection defaults.

            CumulusCogRaster[] sources = selected.Select(file => new CumulusCogRaster(
                client.BuildCogVsiPath(file.CogUrl), file.Datetime, file.Version ?? file.Datetime, file.Id)).ToArray();
            List<(IGriddedDataSource, int)> selectedBands = sources
                .Select(source => ((IGriddedDataSource)source, 0)).ToList();
            OperationResult selectionResult = controller.CreateSelection(selectedBands, out IGriddedDataSelection selection);
            if (!selectionResult)
            {
                (selection as IDisposable)?.Dispose();
                throw new InvalidDataException(selectionResult.GetConcatenatedMessages("; "));
            }
            using var import = new CumulusGridImportOperation(selection, request.LayerId,
                request.PrecipitationDataType, request.PrecipitationUnits, reporter.Progress);
            reporter.Progress.ReportMessage("Applying selected Cumulus grids to the captured Boundary Condition draft…");
            requestCancellation.Token.ThrowIfCancellationRequested();
            ModelSnapshot snapshot = await SaveAsync(model, cumulusImport: import).ConfigureAwait(false);
            committedSnapshot = snapshot;
            sourceCommitted = true;
            reporter.Progress.ReportProgress(100);
            report?.Invoke(new(Fraction: 1, TaskName: "Cumulus grid import complete."));
            return new("completed", snapshot);
        }
        catch (OperationCanceledException) when (reporter.Progress.CancelRequested || requestCancellation.IsCancellationRequested)
        {
            return sourceCommitted ? new("completed", committedSnapshot) : new("cancelled");
        }
        finally
        {
            if (ownsCogAccessGate) CumulusCogAccessGate.Release();
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task PumpControlsAsync(BackendDuplexControlReader controls,
        ProgressReporter reporter, CancellationTokenSource requestCancellation, CancellationToken stop)
    {
        await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stop).ConfigureAwait(false))
        {
            if (!frame.Control.TryGetProperty("type", out var type) || type.ValueKind != System.Text.Json.JsonValueKind.String)
                throw new InvalidDataException("The Cumulus import received an invalid control message.");
            switch (type.GetString())
            {
                case "cancel":
                    reporter.RequestCancel();
                    requestCancellation.Cancel();
                    break;
                default:
                    throw new InvalidDataException("The Cumulus import received an unsupported control message.");
            }
        }
    }

    private static async Task<CumulusEnvironment> ResolveCumulusEnvironmentAsync(string id)
    {
        CumulusEnvironment environment = id switch
        {
#if DEBUG
            "local" => CumulusEnvironment.Local,
#endif
            "dev" => CumulusEnvironment.Dev,
            _ => throw new ArgumentException("Choose an available Cumulus environment.")
        };
        await environment.ResolveUrlsAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(environment.ApiBaseUrl))
            throw new InvalidOperationException("The selected Cumulus service URL could not be resolved.");
        return environment;
    }

    private static string TokenFor(CumulusEnvironment environment, string? token)
    {
        string resolved = environment.UseMock ? environment.MockToken : token ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolved))
            throw new UnauthorizedAccessException("Sign in to the selected Cumulus environment first.");
        return resolved;
    }

    private static DateTime ParseUtc(string value, string field)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
            throw new ArgumentException("The Cumulus date range contains an invalid timestamp.", field);
        return parsed.UtcDateTime;
    }

    private static string FormatUtc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void ValidateCumulusRange(DateTime after, DateTime before)
    {
        if (before <= after) throw new ArgumentException("End date must be after start date.");
        if (before > after.AddMonths(6)) throw new ArgumentException("Limit Cumulus imports to six months at a time.");
    }

    private static void ValidateSources(CumulusGridImportSource[]? sources)
    {
        if (sources is null || sources.Length == 0)
            throw new ArgumentException("Select at least one Cumulus grid time before importing.");
        if (sources.Any(source => string.IsNullOrWhiteSpace(source.Id) ||
            string.IsNullOrWhiteSpace(source.DatetimeUtc) || string.IsNullOrWhiteSpace(source.VersionUtc)) ||
            sources.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != sources.Length)
            throw new ArgumentException("The Cumulus grid selection contains an invalid or duplicate source ID.");
    }

    private static void ValidateExtent(CumulusImportExtent extent)
    {
        if (!double.IsFinite(extent.MinX) || !double.IsFinite(extent.MaxX) ||
            !double.IsFinite(extent.MinY) || !double.IsFinite(extent.MaxY) ||
            extent.MinX >= extent.MaxX || extent.MinY >= extent.MaxY)
            throw new ArgumentException("The drawn Cumulus area must be a finite, non-empty extent.");
    }

    private static void ValidateCumulusModel(ModelSaveRequest model, string layerId)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Kind != "Boundary Conditions" || string.IsNullOrWhiteSpace(model.ProjectPath))
            throw new ArgumentException("Cumulus grids can only be imported into a saved Boundary Condition in an owning project.");
        if (layerId is not ("precipitation" or "wind" or "airPressure" or "airDensity"))
            throw new ArgumentException("Choose a supported spatial Boundary Condition layer.");
        VerifyKind(model);
        string assetPath = Path.GetFullPath(model.AssetPath);
        if (!string.Equals(Revision(assetPath), model.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static HttpClient CreateCacPresentingTokenClient()
    {
        try
        {
            X509Certificate2Collection certificates = [];
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                foreach (X509Certificate2 certificate in store.Certificates)
                    if (certificate.HasPrivateKey && HasClientAuthEku(certificate)) certificates.Add(certificate);
            }
            return new HttpClient(new SocketsHttpHandler
            {
                SslOptions = { ClientCertificates = certificates }
            });
        }
        catch
        {
            // Match the retained authenticator's fallback. Authentication
            // still succeeds only after a real token endpoint response.
            return new HttpClient();
        }
    }

    private static bool HasClientAuthEku(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is not X509EnhancedKeyUsageExtension usages) continue;
            foreach (System.Security.Cryptography.Oid usage in usages.EnhancedKeyUsages)
                if (usage.Value == "1.3.6.1.5.5.7.3.2") return true;
        }
        return false;
    }

    private sealed class CumulusReporter
    {
        public ProgressReporter Progress { get; } = new();
        public CumulusReporter(Action<BackendOperationProgress>? report)
        {
            Progress.ProgressReported += (sender, percent) => report?.Invoke(new(
                Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.TaskName));
            Progress.MessageReported += (sender, message) => report?.Invoke(new(
                Message: message.EvaluatedMessage, TaskName: sender.TaskName));
            report?.Invoke(new(TaskName: "Preparing the Cumulus grid import…"));
        }
    }
}

/// <summary>
/// RPC-facing Cumulus operations. Authentication, query, and import all use
/// the same environment and source semantics as the retained WinUI importer.
/// </summary>
internal static class CumulusImportService
{
    internal static Task<CumulusEnvironmentOption[]> GetEnvironmentsAsync(
        CumulusEnvironmentRequest? request = null) => ModelEditorAdapter.GetCumulusEnvironmentsAsync(request);

    internal static Task<CumulusTokenSet> ExchangeTokenAsync(CumulusTokenExchangeRequest request) =>
        ModelEditorAdapter.ExchangeCumulusTokenAsync(request);

    internal static Task<CumulusTokenSet> RefreshTokenAsync(CumulusTokenRefreshRequest request) =>
        ModelEditorAdapter.RefreshCumulusTokenAsync(request);

    internal static Task<CumulusProductOption[]> GetProductsAsync(CumulusProductsRequest request) =>
        ModelEditorAdapter.GetCumulusProductsAsync(request);

    internal static Task<CumulusGridSourceOption[]> QueryGridsAsync(CumulusGridQueryRequest request) =>
        ModelEditorAdapter.QueryCumulusGridsAsync(request);

    internal static Task<CumulusGridFootprintResult> ProbeGridFootprintAsync(
        CumulusGridFootprintRequest request) => ModelEditorAdapter.ProbeCumulusGridFootprintAsync(request);

    internal static Task<CumulusGridImportResult> ImportDuplexAsync(
        CumulusGridImportRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls) =>
        ModelEditorAdapter.ImportCumulusGridsDuplexAsync(request, report, duplexControls);
}
