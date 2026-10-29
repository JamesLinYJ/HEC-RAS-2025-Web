using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.IO;
using Geospatial.IO.Network.ArcGisRest;
using Geospatial.IO.Network.NFHL;
using Geospatial.IO.Network.NLD;
using Geospatial.IO.Network.NSI;
using Geospatial.IO.Network.OSM;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Ras.Backend;

/// <summary>Cooperative query cancellation confirmed after its owned cleanup completed.</summary>
internal sealed class FeatureQueryCanceledException(Exception inner)
    : OperationCanceledException("Feature query cancellation and cleanup completed.", inner);

/// <summary>
/// Runs the retained National Levee Database, National Structure Inventory,
/// FEMA NFHL, and Overpass query workflows without their WinUI dialogs. Query
/// results are real source features staged across the backend's per-RPC
/// process lifetime; Save exports those staged features with the retained GDAL
/// writers and original naming/CRS rules.
/// </summary>
public static partial class FeatureQueryService
{
    private const int StageVersion = 1;
    private const string StageRootName = "HECRAS-FeatureQueryResults";
    private static readonly TimeSpan StageLifetime = TimeSpan.FromHours(24);
    private static readonly char[] WindowsInvalidFileNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private sealed record StageLayer(
        string FileName,
        string Revision,
        string Name,
        string MapType,
        int FeatureCount,
        string LineColor,
        string FillColor,
        double LineWidth);

    private sealed record StageManifest(
        int Version,
        string ResultId,
        DateTime CreatedUtc,
        MapSourceMapCapture Map,
        string Kind,
        string OptionsFingerprint,
        FeatureQueryExtent? Extent,
        string? NldLayerId,
        bool UseBoundaryPolygon,
        string? PolygonSourceId,
        MapSourceFamilyCapture? PolygonSourceFamily,
        string QueryLabel,
        string? NldAlignmentResponseFile,
        string? NldAlignmentResponseRevision,
        StageLayer[] Layers);

    private sealed record QueryOutput(
        string Name,
        string MapType,
        int FeatureCount,
        string LineColor,
        string FillColor,
        double LineWidth,
        Func<string, OperationResult> WriteProjectWorldGeoJson,
        Func<string, SelectedFormat, Projection, OperationResult> WriteSelectedFormat);

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;

        public void Dispose() => DisposeProject(Project);
    }

    private sealed record PolygonInput(string Id, string Name, PolygonCollection Polygons,
        MapSourceFamilyCapture Family);

    public static FeatureQuerySnapshotResponse Snapshot(FeatureQuerySnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapTreeRequest mapRequest = request.Map ?? throw new ArgumentException("The feature query map is missing.");
        MapSourceMapCapture capture = MapSourceIdentity.CaptureMap(mapRequest);
        MapTreeSnapshot tree = MapTreeService.Load(mapRequest);
        using ProjectScope scope = OpenProjectScope(mapRequest.ProjectPath, mapRequest.ProjectXml,
            mapRequest.CoreLayerContext);
        Project project = scope.Project;
        FeatureQueryPolygonSource[] sources = BuildPolygonSourceChoices(tree.Roots, project).ToArray();
        MapSourceIdentity.VerifyMap(mapRequest, capture);
        return new(capture, sources, SelectedPolygonId: null);
    }

    internal static Task<FeatureQueryQueryResponse> QueryAsync(FeatureQueryQueryRequest request,
        Action<BackendOperationProgress>? report = null,
        BackendDuplexControlReader? duplexControls = null) => QueryCoreAsync(request, report, duplexControls);

    internal static Task<FeatureQuerySaveResponse> SaveAsync(FeatureQuerySaveRequest request,
        Action<BackendOperationProgress>? report = null,
        BackendDuplexControlReader? duplexControls = null) => SaveCoreAsync(request, report, duplexControls);

    public static async Task<FeatureQuerySuggestionResponse> SuggestAsync(
        FeatureQuerySuggestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> values = request.IsKey
            ? await TagInfoClient.SuggestKeysAsync(request.Value ?? string.Empty, cancellationToken).ConfigureAwait(false)
            : string.IsNullOrWhiteSpace(request.Key)
                ? Array.Empty<string>()
                : await TagInfoClient.SuggestValuesAsync(request.Key, request.Value ?? string.Empty,
                    cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
    }

    internal static async Task<FeatureQuerySuggestionResponse> SuggestDuplexAsync(
        FeatureQuerySuggestionRequest request, BackendDuplexControlReader controls)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var cancellation = new CancellationTokenSource();
        using var stopControlPump = new CancellationTokenSource();
        Task? controlPump = StartControlPump(controls, new ProgressReporter(), cancellation, stopControlPump.Token);
        try
        {
            // Endpoint resolution in the retained source has no cancellation
            // parameter. Stop awaiting it as well; the one-request worker exits
            // after delivering this terminal result.
            return await SuggestAsync(request, cancellation.Token).WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (cancellation.IsCancellationRequested)
        {
            throw new FeatureQueryCanceledException(error);
        }
        finally
        {
            stopControlPump.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task<FeatureQueryQueryResponse> QueryCoreAsync(FeatureQueryQueryRequest request,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapTreeRequest mapRequest = request.Map ?? throw new ArgumentException("The feature query map is missing.");
        string kind = ValidateKind(request.Kind);
        if (request.UseBoundaryPolygon && kind != "NSI")
            throw new ValidationException("Polygon boundaries are available only for NSI queries.");
        FeatureQueryExtent? queryExtent = ValidateExtent(request.Extent, required: kind != "NSI" || !request.UseBoundaryPolygon);
        string? nldLayerId = kind == "NLD" ? ValidateNldLayer(request.NldLayerId) : null;
        if (kind == "NSI" && request.UseBoundaryPolygon && string.IsNullOrWhiteSpace(request.PolygonSourceId))
            throw new ValidationException("Choose a polygon source before querying the NSI.");

        MapSourceMapCapture mapCapture = MapSourceIdentity.CaptureMap(mapRequest);
        MapTreeSnapshot tree = MapTreeService.Load(mapRequest);
        using ProjectScope scope = OpenProjectScope(mapRequest.ProjectPath, mapRequest.ProjectXml,
            mapRequest.CoreLayerContext);
        Project project = scope.Project;
        Projection projection = project.Projection
            ?? throw new InvalidOperationException("The current project has no usable coordinate projection.");
        MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
        PolygonInput? polygonInput = kind == "NSI" && request.UseBoundaryPolygon
            ? LoadPolygonInput(mapRequest, tree.Roots, project, projection, request.PolygonSourceId!)
            : null;
        MapSourceFamilyCapture? polygonFamily = polygonInput?.Family;
        string queryLabel = kind == "OSM" ? SanitizeQueryLabel(request.QueryLabel) : string.Empty;
        string optionsFingerprint = FingerprintQueryOptions(request, queryLabel, queryExtent);

        DirectoryInfo stageRoot = GetStageRoot();
        Directory.CreateDirectory(stageRoot.FullName);
        CleanupExpiredStages(stageRoot);
        string resultId = Guid.NewGuid().ToString("N");
        string stageDirectory = Path.Combine(stageRoot.FullName, resultId);
        Directory.CreateDirectory(stageDirectory);
        try
        {
            File.WriteAllText(Path.Combine(stageDirectory, ".owner"), resultId, Encoding.UTF8);
        }
        catch
        {
            try { Directory.Delete(stageDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }

        using StageLease queryStageLease = AcquireStageLease(stageDirectory, resultId);
        var reporter = CreateReporter(report);
        using var queryCancellation = new CancellationTokenSource();
        using var stopControlPump = new CancellationTokenSource();
        Task? controlPump = StartControlPump(duplexControls, reporter, queryCancellation,
            stopControlPump.Token);
        string? alignmentResponseFile = null;
        string? alignmentResponseRevision = null;
        try
        {
            reporter.ReportProgress(0);
            queryCancellation.Token.ThrowIfCancellationRequested();
            string? polygonSourceID = request.UseBoundaryPolygon ? request.PolygonSourceId : null;
            var outputs = new List<QueryOutput>();
            string info;
            if (kind == "NLD")
            {
                Extent extent = ToSourceExtent(queryExtent!);
                (var operation, ArcGisQueryResponse response) = nldLayerId switch
                {
                    "Embankments" => await NLDRequests.QueryEmbankmentsAsync(extent, projection, "*", reporter,
                        queryCancellation.Token).ConfigureAwait(false),
                    "Floodwalls" => await NLDRequests.QueryFloodwallsAsync(extent, projection, "*", reporter,
                        queryCancellation.Token).ConfigureAwait(false),
                    _ => await NLDRequests.QueryAlignmentLinesAsync(extent, projection, "*", reporter,
                        queryCancellation.Token).ConfigureAwait(false)
                };
                queryCancellation.Token.ThrowIfCancellationRequested();
                RequireSuccess(operation, "NLD query");
                if (nldLayerId == "SystemRoutes")
                {
                    alignmentResponseFile = "nld-alignment-response.json";
                    string responsePath = Path.Combine(stageDirectory, alignmentResponseFile);
                    File.WriteAllText(responsePath, JsonSerializer.Serialize(response, BackendJson.Options), Encoding.UTF8);
                    alignmentResponseRevision = HashFile(responsePath);
                }
                OperationResult projectionResult = ArcGisRestConversions.TryGetResponseProjectionOrDefault(
                    response, out Projection sourceProjection);
                RequireSuccess(projectionResult, "NLD response projection");
                OperationResult convertResult = ArcGisRestConversions.TryConvertToPolylines(response,
                    sourceProjection, projection, out PolylineCollection lines, out Table attributes);
                RequireSuccess(convertResult, "NLD geometry conversion");
                string label = NldLayerLabel(nldLayerId!);
                string prefix = nldLayerId == "SystemRoutes" ? "NLD_RASMergedAlignments" : "NLD_" + nldLayerId;
                string lineColor = "#0080FFFF";
                outputs.Add(PolylineOutput(prefix, lines, attributes, lineColor, "#00000000", 3));
                info = BuildFoundInfo(lines.Count, label, "NLD");
            }
            else if (kind == "NSI")
            {
                Extent wgs84Extent;
                if (polygonInput is not null)
                {
                    MapSourceIdentity.VerifyFamily(polygonInput.Family);
                    if (polygonInput.Polygons.Count == 0)
                        throw new ValidationException("The selected polygon source has no features.");
                    string polygonBounds;
                    if (polygonInput.Polygons.Count == 1)
                    {
                        Polygon polygon = polygonInput.Polygons[0];
                        Polygon simplified = polygon.PointCount <= 25 ? polygon : polygon.CloneFilteredIterative(3, 25);
                        polygonBounds = NSIClient.GetNSIBoundingBox(simplified.Reproject(projection,
                            WellKnownProjections.WGS84));
                    }
                    else
                    {
                        polygonBounds = NSIClient.GetNSIBoundingBox(polygonInput.Polygons.Extent.Reproject(
                            projection, WellKnownProjections.WGS84));
                    }
                    var (operation, structures) = await NSIClient.QueryStructuresAsync(polygonBounds, reporter,
                        queryCancellation.Token).ConfigureAwait(false);
                    queryCancellation.Token.ThrowIfCancellationRequested();
                    RequireSuccess(operation, "NSI query");
                    PointFeatureCollection points = BuildNsiPoints(structures, projection);
                    outputs.Add(PointOutput("NSI_Structures", points, "#DC5028FF", "#A02814FF", 2));
                    info = BuildFoundInfo(points.Count, "structures", "NSI");
                }
                else
                {
                    wgs84Extent = ToSourceExtent(queryExtent!).Reproject(projection, WellKnownProjections.WGS84);
                    string polygonBounds = NSIClient.GetNSIBoundingBox(wgs84Extent);
                    var (operation, structures) = await NSIClient.QueryStructuresAsync(polygonBounds, reporter,
                        queryCancellation.Token).ConfigureAwait(false);
                    queryCancellation.Token.ThrowIfCancellationRequested();
                    RequireSuccess(operation, "NSI query");
                    PointFeatureCollection points = BuildNsiPoints(structures, projection);
                    outputs.Add(PointOutput("NSI_Structures", points, "#DC5028FF", "#A02814FF", 2));
                    info = BuildFoundInfo(points.Count, "structures", "NSI");
                }
            }
            else if (kind == "NFHL")
            {
                Extent extent = ToSourceExtent(queryExtent!);
                var (operation, features) = await NFHLRequests.QueryFloodHazardZonesCompleteAsync(extent,
                    projection, reporter, queryCancellation.Token).ConfigureAwait(false);
                queryCancellation.Token.ThrowIfCancellationRequested();
                RequireSuccess(operation, "NFHL query");
                outputs.Add(ComplexPolygonOutput("FEMA_Floodplain", features, "#1E5AA9FF", "#3C82DC50", 1.5));
                info = BuildFoundInfo(features.Count, "flood hazard zones", "NFHL");
            }
            else
            {
                Extent wgs84Extent = ToSourceExtent(queryExtent!).Reproject(projection, WellKnownProjections.WGS84);
                if (!OverpassQlSanitizer.TryPrepare(request.RawQl ?? string.Empty, wgs84Extent,
                        out string preparedQl, out string warning, out string error))
                    throw new ValidationException(error);
                if (!string.IsNullOrWhiteSpace(warning)) reporter.ReportMessage(warning);
                var (operation, response) = await OverpassClient.ExecuteAsync(preparedQl, reporter,
                    queryCancellation.Token).ConfigureAwait(false);
                queryCancellation.Token.ThrowIfCancellationRequested();
                RequireSuccess(operation, "OpenStreetMap query");
                if (!OsmFeatureBuilder.TryBuildFeatureSets(response, projection, out OsmFeatureSets sets,
                        out OperationResult conversion))
                    throw new InvalidOperationException(DescribeFailure("OpenStreetMap geometry conversion", conversion));
                RequireSuccess(conversion, "OpenStreetMap geometry conversion");
                if (sets.PointCount > 0)
                    outputs.Add(PointOutput("OSM_" + queryLabel + OsmBucketSuffixes.Points, sets.Points,
                        "#C83C82FF", "#961E5FFF", 2));
                if (sets.LineCount > 0)
                    outputs.Add(PolylineOutput("OSM_" + queryLabel + OsmBucketSuffixes.Lines,
                        sets.Lines.Features, sets.Lines.AttributeTable, "#3C8C64FF", "#00000000", 2));
                if (sets.PolygonCount > 0)
                    outputs.Add(ComplexPolygonOutput("OSM_" + queryLabel + OsmBucketSuffixes.Polygons,
                        sets.Polygons, "#BE7828FF", "#E6AA5050", 1.5));
                info = sets.HasAny
                    ? $"Found {sets.DescribeCounts()}{Environment.NewLine}{Environment.NewLine}Click 'Save to Disk' to save and add to map."
                    : sets.DescribeCounts();
            }

            queryCancellation.Token.ThrowIfCancellationRequested();
            if (polygonInput is not null) MapSourceIdentity.VerifyFamily(polygonInput.Family);
            MapSourceIdentity.VerifyMap(mapRequest, mapCapture);

            var stageLayers = new List<StageLayer>(outputs.Count);
            for (int index = 0; index < outputs.Count; index++)
            {
                queryCancellation.Token.ThrowIfCancellationRequested();
                QueryOutput output = outputs[index];
                if (output.FeatureCount <= 0) continue;
                string fileName = $"feature-{index:D2}.geojson";
                string sourcePath = Path.Combine(stageDirectory, fileName);
                OperationResult writeResult = await Task.Run(() => output.WriteProjectWorldGeoJson(sourcePath),
                    queryCancellation.Token).ConfigureAwait(false);
                RequireSuccess(writeResult, "Feature query preview staging");
                queryCancellation.Token.ThrowIfCancellationRequested();
                stageLayers.Add(new(fileName, HashFile(sourcePath), output.Name, output.MapType,
                    output.FeatureCount, output.LineColor, output.FillColor, output.LineWidth));
                reporter.ReportProgress((index + 1) * 90 / Math.Max(outputs.Count, 1));
            }

            var manifest = new StageManifest(StageVersion, resultId, DateTime.UtcNow, mapCapture, kind,
                optionsFingerprint, queryExtent, nldLayerId, request.UseBoundaryPolygon, polygonSourceID,
                polygonFamily, queryLabel, alignmentResponseFile, alignmentResponseRevision,
                stageLayers.ToArray());
            WriteManifest(stageDirectory, manifest);
            queryCancellation.Token.ThrowIfCancellationRequested();
            MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
            if (polygonInput is not null) MapSourceIdentity.VerifyFamily(polygonInput.Family);
            reporter.ReportProgress(100);
            FeatureQueryPreviewLayer[] previewLayers = stageLayers.Select(layer => new FeatureQueryPreviewLayer(
                layer.Name, Path.Combine(stageDirectory, layer.FileName), layer.Revision, layer.MapType,
                layer.LineColor, layer.FillColor, layer.LineWidth)).ToArray();
            bool hasFeatures = previewLayers.Length > 0;
            if (!hasFeatures) DeleteOwnedStageDirectory(stageDirectory, resultId, leaseHeld: true);
            return new(mapCapture, kind, resultId, hasFeatures, info, previewLayers);
        }
        catch (OperationCanceledException error) when (queryCancellation.IsCancellationRequested)
        {
            try { DeleteOwnedStageDirectoryStrict(stageDirectory, resultId, leaseHeld: true); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new IOException($"查询已取消，但无法确认暂存清理：{stageDirectory}", cleanupError);
            }
            throw new FeatureQueryCanceledException(error);
        }
        catch
        {
            DeleteOwnedStageDirectory(stageDirectory, resultId, leaseHeld: true);
            throw;
        }
        finally
        {
            stopControlPump.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task<FeatureQuerySaveResponse> SaveCoreAsync(FeatureQuerySaveRequest request,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapTreeRequest mapRequest = request.Map ?? throw new ArgumentException("The feature query map is missing.");
        string resultId = ValidateResultId(request.ResultId);
        SelectedFormat format = ParseFormat(request.FormatId);
        string outputDirectory = ValidateOutputDirectory(request.OutputDirectory);
        MapSourceMapCapture mapCapture = MapSourceIdentity.CaptureMap(mapRequest);
        string stageDirectory = StageDirectory(resultId);
        using StageLease stageLease = AcquireStageLease(stageDirectory, resultId);
        StageManifest manifest = ReadManifest(stageDirectory, resultId, out string manifestRevision);
        if (mapCapture != manifest.Map) throw new SourceFileChangedException();
        MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
        if (manifest.PolygonSourceFamily is { } polygonFamily)
            MapSourceIdentity.VerifyFamily(polygonFamily);

        using ProjectScope scope = OpenProjectScope(mapRequest.ProjectPath, mapRequest.ProjectXml,
            mapRequest.CoreLayerContext);
        Project project = scope.Project;
        Projection projection = project.Projection
            ?? throw new InvalidOperationException("The current project has no usable coordinate projection.");
        var reporter = CreateReporter(report);
        using var saveCancellation = new CancellationTokenSource();
        using var stopControlPump = new CancellationTokenSource();
        Task? controlPump = StartControlPump(duplexControls, reporter, saveCancellation,
            stopControlPump.Token);
        var sources = new List<FeatureQueryExportedSource>();
        var writtenFiles = new List<string>();
        var messages = new List<string>();
        try
        {
            reporter.ReportProgress(0);
            IReadOnlyList<StageLayer> layers = manifest.Layers;
            if (manifest.Kind == "NLD" && manifest.NldLayerId == "SystemRoutes")
            {
                if (manifest.Extent is null || manifest.NldAlignmentResponseFile is null ||
                    manifest.NldAlignmentResponseRevision is null)
                    throw new InvalidDataException("The saved RAS Merged Alignments query is missing its source response.");
                string responsePath = OwnedStageFile(stageDirectory, manifest.NldAlignmentResponseFile);
                if (!string.Equals(HashFile(responsePath), manifest.NldAlignmentResponseRevision,
                        StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
                ArcGisQueryResponse alignmentResponse = JsonSerializer.Deserialize<ArcGisQueryResponse>(
                    File.ReadAllText(responsePath, Encoding.UTF8), BackendJson.Options)
                    ?? throw new InvalidDataException("The saved NLD alignment response could not be read.");
                saveCancellation.Token.ThrowIfCancellationRequested();
                PolylineFeatureCollection merged = await NLDMergedAlignments.QueryMergedAlignmentsAsync(
                    ToSourceExtent(manifest.Extent), projection, ensureElevationData: true,
                    orientToSystemLine: true, alignmentLines: alignmentResponse,
                    reporter: reporter, ct: saveCancellation.Token).ConfigureAwait(false);
                saveCancellation.Token.ThrowIfCancellationRequested();
                if (merged.Count > 0)
                {
                    string outputPath = await ExportLayerAsync(outputDirectory, "NLD_RASMergedAlignments",
                        format, projection, PolylineOutput("NLD_RASMergedAlignments", merged.Features,
                            merged.AttributeTable, "#0080FFFF", "#00000000", 3), stageDirectory,
                        layers: [], saveCancellation.Token).ConfigureAwait(false);
                    writtenFiles.Add(outputPath);
                    sources.Add(CaptureExportedSource(outputPath, "PolylinesVector", merged.Count,
                        "#0080FFFF", "#00000000", 3));
                }
                else
                {
                    messages.Add("No RAS merged alignments were returned.");
                }
            }
            else
            {
                for (int index = 0; index < layers.Count; index++)
                {
                    saveCancellation.Token.ThrowIfCancellationRequested();
                    StageLayer layer = layers[index];
                    string stageFile = OwnedStageFile(stageDirectory, layer.FileName);
                    if (!string.Equals(HashFile(stageFile), layer.Revision, StringComparison.OrdinalIgnoreCase))
                        throw new SourceFileChangedException();
                    string outputPath;
                    try
                    {
                        outputPath = await ExportLayerAsync(outputDirectory, OutputPrefix(manifest, layer),
                            format, projection, null, stageDirectory, [layer], saveCancellation.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is not OperationCanceledException and not SourceFileChangedException
                                                  and not FeatureQueryExportCleanupException)
                    {
                        messages.Add(error.Message);
                        continue;
                    }
                    writtenFiles.Add(outputPath);
                    sources.Add(CaptureExportedSource(outputPath, layer.MapType, layer.FeatureCount,
                        layer.LineColor, layer.FillColor, layer.LineWidth));
                    reporter.ReportProgress((index + 1) * 90 / Math.Max(layers.Count, 1));
                }
            }

            saveCancellation.Token.ThrowIfCancellationRequested();
            MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
            if (manifest.PolygonSourceFamily is { } queryFamily) MapSourceIdentity.VerifyFamily(queryFamily);
            VerifyStageSnapshot(stageDirectory, manifest, manifestRevision);
            if (sources.Count == 0 && messages.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, messages));
            string info = messages.Count == 0
                ? $"Saved {sources.Sum(source => source.FeatureCount):N0} feature(s)."
                : $"Saved {sources.Sum(source => source.FeatureCount):N0} feature(s).{Environment.NewLine}{string.Join(Environment.NewLine, messages)}";
            reporter.ReportProgress(100);
            DeleteOwnedStageDirectory(stageDirectory, resultId, leaseHeld: true);
            return new(mapCapture, sources.ToArray(), info, messages.ToArray());
        }
        catch (Exception error)
        {
            var cleanupProblems = writtenFiles.SelectMany(outputPath => DeleteExportFiles(outputPath, format)).ToList();
            if (error is FeatureQueryExportCleanupException previous)
                cleanupProblems.InsertRange(0, previous.Problems);
            if (cleanupProblems.Count > 0)
                throw new FeatureQueryExportCleanupException(cleanupProblems, error);
            if (error is OperationCanceledException && saveCancellation.IsCancellationRequested)
                throw new FeatureQueryCanceledException(error);
            throw;
        }
        finally
        {
            stopControlPump.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task<string> ExportLayerAsync(string outputDirectory, string prefix,
        SelectedFormat format, Projection projection, QueryOutput? mergedOutput, string stageDirectory,
        IReadOnlyList<StageLayer> layers, CancellationToken cancellationToken)
    {
        string extension = format == SelectedFormat.Shapefile ? ".shp" : ".geojson";
        string safePrefix = ValidateOutputPrefix(prefix);
        string outputPath = FileHelpers.UniqueFilename(Path.Combine(outputDirectory, safePrefix + extension));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationResult operation;
            if (mergedOutput is not null)
            {
                operation = await Task.Run(() => WriteQueryOutput(outputPath, mergedOutput, format, projection),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                StageLayer layer = layers.Single();
                string stagePath = OwnedStageFile(stageDirectory, layer.FileName);
                operation = await Task.Run(() => WriteStagedOutput(stagePath, outputPath, layer, format, projection),
                    cancellationToken).ConfigureAwait(false);
            }
            RequireSuccess(operation, "Feature query export");
            cancellationToken.ThrowIfCancellationRequested();
            return outputPath;
        }
        catch (Exception error)
        {
            IReadOnlyList<string> cleanupProblems = DeleteExportFiles(outputPath, format);
            if (cleanupProblems.Count > 0)
                throw new FeatureQueryExportCleanupException(cleanupProblems, error);
            throw;
        }
    }

    private static OperationResult WriteQueryOutput(string outputPath, QueryOutput output,
        SelectedFormat format, Projection projection)
    {
        return output.WriteSelectedFormat(outputPath, format, projection);
    }

    private static OperationResult WriteStagedOutput(string stagePath, string outputPath,
        StageLayer layer, SelectedFormat format, Projection projection)
    {
        // Staging GeoJSON intentionally retains project-world coordinates. It is
        // internal to the exact map query and lets shapefile export preserve the
        // source feature coordinates while GeoJSON export performs the source
        // GeoJsonWriter conversion to WGS84 exactly once.
        if (layer.MapType == "PointsVector")
        {
            OperationResult read = GeoJsonReader.TryRead(stagePath, out PointFeatureCollection features);
            if (!read.Result) return read;
            return format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(outputPath, features.AsPointCollection(), features.AttributeTable, projection)
                : GeoJsonWriter.Write(outputPath, features.AsPointCollection(), projection, features.AttributeTable);
        }
        if (layer.MapType == "PolylinesVector")
        {
            OperationResult read = GeoJsonReader.TryRead(stagePath, out PolylineFeatureCollection features);
            if (!read.Result) return read;
            List<Polyline> lines = features.Features.ToList();
            return format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(outputPath, lines, features.AttributeTable, projection)
                : GeoJsonWriter.Write(outputPath, lines, projection, features.AttributeTable);
        }
        if (layer.MapType == "PolygonsVector")
        {
            OperationResult read = GeoJsonReader.TryRead(stagePath, out ComplexPolygonFeatureCollection features);
            if (!read.Result) return read;
            List<ComplexPolygon> polygons = features.Features.ToList();
            return format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(outputPath, polygons, features.AttributeTable, projection)
                : GeoJsonWriter.Write(outputPath, polygons, projection, features.AttributeTable);
        }
        throw new NotSupportedException($"The source map does not support exporting {layer.MapType} feature data.");
    }

    private static FeatureQueryExportedSource CaptureExportedSource(string path, string mapType,
        int featureCount, string lineColor, string fillColor, double lineWidth)
    {
        string canonicalPath = Path.GetFullPath(path);
        string primaryRevision = HashFile(canonicalPath);
        MapSourceFamilyCapture family;
        if (canonicalPath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
        {
            family = MapSourceIdentity.CaptureFamily("shapefile", canonicalPath,
            [
                new MapSourceFileInput(ShapefileIO.SHXFilename(canonicalPath), "shx"),
                new MapSourceFileInput(ShapefileIO.DBFFilename(canonicalPath), "dbf"),
                new MapSourceFileInput(Path.ChangeExtension(canonicalPath, ".prj"), "prj", Required: false)
            ]);
        }
        else
        {
            family = MapSourceIdentity.CaptureFamily("geojson", canonicalPath, []);
        }
        MapSourceIdentity.VerifyFamily(family);
        return new(canonicalPath, primaryRevision, Path.GetFileNameWithoutExtension(canonicalPath), mapType,
            featureCount, lineColor, fillColor, lineWidth, family);
    }

    private static string OutputPrefix(StageManifest manifest, StageLayer layer)
    {
        if (manifest.Kind == "OSM") return layer.Name;
        if (manifest.Kind == "NLD" && manifest.NldLayerId == "SystemRoutes") return "NLD_RASMergedAlignments";
        return layer.Name;
    }

    private static string ValidateOutputPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || Path.GetFileName(prefix) != prefix ||
            prefix.Any(character => char.IsControl(character) ||
                Array.IndexOf(WindowsInvalidFileNameChars, character) >= 0))
            throw new InvalidDataException("The feature query result contains an invalid export name.");
        return prefix;
    }

    private static QueryOutput PointOutput(string name, PointFeatureCollection features,
        string lineColor, string fillColor, double lineWidth) =>
        new(name, "PointsVector", features.Count, lineColor, fillColor, lineWidth,
            path => GeoJsonWriter.Write(path, features.AsPointCollection(), null, features.AttributeTable),
            (path, format, projection) => format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(path, features.AsPointCollection(), features.AttributeTable, projection)
                : GeoJsonWriter.Write(path, features.AsPointCollection(), projection, features.AttributeTable));

    private static QueryOutput PolylineOutput(string name, IEnumerable<Polyline> features, Table attributes,
        string lineColor, string fillColor, double lineWidth)
    {
        List<Polyline> lines = features.ToList();
        return new(name, "PolylinesVector", lines.Count, lineColor, fillColor, lineWidth,
            path => GeoJsonWriter.Write(path, lines, null, attributes),
            (path, format, projection) => format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(path, lines, attributes, projection)
                : GeoJsonWriter.Write(path, lines, projection, attributes));
    }

    private static QueryOutput ComplexPolygonOutput(string name, ComplexPolygonFeatureCollection features,
        string lineColor, string fillColor, double lineWidth)
    {
        List<ComplexPolygon> polygons = features.Features.ToList();
        return new(name, "PolygonsVector", features.Count, lineColor, fillColor, lineWidth,
            path => GeoJsonWriter.Write(path, polygons, null, features.AttributeTable),
            (path, format, projection) => format == SelectedFormat.Shapefile
                ? ShapefileIO.Write(path, polygons, features.AttributeTable, projection)
                : GeoJsonWriter.Write(path, polygons, projection, features.AttributeTable));
    }

    private static PointFeatureCollection BuildNsiPoints(IReadOnlyList<NSIStructure> structures,
        Projection targetProjection)
    {
        var points = new Point[structures.Count];
        for (int index = 0; index < structures.Count; index++)
            points[index] = new Point(structures[index].X, structures[index].Y);
        points.AsSpan().ReprojectAll(WellKnownProjections.WGS84, targetProjection);
        return new PointFeatureCollection(new PointCollection(points), NSIStructure.BuildAttributeTable(structures));
    }

    private static PolygonInput LoadPolygonInput(MapTreeRequest mapRequest, CatalogTreeNode[] roots,
        Project project, Projection projectProjection, string sourceID)
    {
        CatalogTreeNode? mapLayer = Descendants(roots).FirstOrDefault(node =>
            string.Equals(node.Id, sourceID, StringComparison.Ordinal) &&
            node.SourceClass == "PolygonMapLayerVisualContext" &&
            node.DiagnosticStatus == "loaded" && node.Locator?.MapType == "PolygonsVector" &&
            node.AssetID is { Length: > 0 });
        if (mapLayer is not null)
        {
            string sourcePath = Path.GetFullPath(mapLayer.AssetID!);
            MapSourceFamilyCapture family = CapturePolygonSourceFamily(sourcePath);
            PolygonCollection polygons = ReadPolygonSource(sourcePath, projectProjection);
            MapSourceIdentity.VerifyFamily(family);
            return new(sourceID, mapLayer.Name, polygons, family);
        }

        foreach (Geometry geometry in project.Geometries)
        {
            string id = GeometryPerimeterSourceId(geometry.Filename);
            if (!string.Equals(id, sourceID, StringComparison.Ordinal)) continue;
            Polygon? perimeter = geometry.FlowAreaLayer.FirstOrDefault()?.Perimeter;
            if (perimeter is null || !Polygon.IsValidPolygon(perimeter))
                throw new SourceFileChangedException();
            string sourcePath = Path.GetFullPath(geometry.Filename);
            MapSourceFamilyCapture family = MapSourceIdentity.CaptureFamily("feature-query-geometry-perimeter",
                sourcePath, []);
            PolygonCollection polygons = new();
            polygons.Add(perimeter);
            MapSourceIdentity.VerifyFamily(family);
            return new(sourceID, geometry.Name + " (Mesh Perimeter)", polygons, family);
        }
        MapSourceIdentity.VerifyMap(mapRequest, MapSourceIdentity.CaptureMap(mapRequest));
        throw new SourceFileChangedException();
    }

    private static PolygonCollection ReadPolygonSource(string path, Projection targetProjection)
    {
        if (path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
        {
            OperationResult operation = ShapefileIO.TryRead(path,
                out PolygonCollectionFeatureCollection collection, targetProjection, ProgressReporter.None());
            RequireSuccess(operation, "Selected polygon source read");
            return new PolygonCollection(collection.ToExplodedPolygonCollection());
        }

        if (!GeoJsonReader.HasJsonFileExtension(path))
            throw new NotSupportedException("The selected polygon source must be a loaded Shapefile or GeoJSON map layer.");
        OperationResult geoJsonOperation = GeoJsonReader.TryRead(path, out PolygonFeatureCollection geoJson);
        RequireSuccess(geoJsonOperation, "Selected polygon source read");
        Polygon[] polygons = geoJson.AsPolygonCollection().ToArray();
        if (!WellKnownProjections.WGS84.IsEqual(targetProjection))
        {
            using var transformer = new ReprojectionTransformer(WellKnownProjections.WGS84, targetProjection);
            foreach (Polygon polygon in polygons)
            {
                Memory<Point> points = polygon.GetPointBufferUnsafe();
                transformer.Transform(points, points);
            }
        }
        var projected = new PolygonCollection();
        foreach (Polygon polygon in polygons) projected.Add(polygon);
        return projected;
    }

    private static IEnumerable<FeatureQueryPolygonSource> BuildPolygonSourceChoices(
        CatalogTreeNode[] roots, Project project)
    {
        foreach (CatalogTreeNode node in Descendants(roots))
        {
            if (node.SourceClass == "PolygonMapLayerVisualContext" && node.DiagnosticStatus == "loaded" &&
                node.Locator?.MapType == "PolygonsVector" && node.AssetID is { Length: > 0 })
                yield return new(node.Id, node.Name);
        }
        foreach (Geometry geometry in project.Geometries)
        {
            Polygon? perimeter = geometry.FlowAreaLayer.FirstOrDefault()?.Perimeter;
            if (perimeter is not null && Polygon.IsValidPolygon(perimeter))
                yield return new(GeometryPerimeterSourceId(geometry.Filename),
                    geometry.Name + " (Mesh Perimeter)");
        }
    }

    private static MapSourceFamilyCapture CapturePolygonSourceFamily(string path)
    {
        if (path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
        {
            return MapSourceIdentity.CaptureFamily("feature-query-polygon", path,
            [
                new MapSourceFileInput(ShapefileIO.SHXFilename(path), "shx"),
                new MapSourceFileInput(ShapefileIO.DBFFilename(path), "dbf"),
                new MapSourceFileInput(Path.ChangeExtension(path, ".prj"), "prj", Required: false)
            ]);
        }
        if (!GeoJsonReader.HasJsonFileExtension(path))
            throw new NotSupportedException("The selected polygon source must be a loaded Shapefile or GeoJSON map layer.");
        return MapSourceIdentity.CaptureFamily("feature-query-polygon", path, []);
    }

    private static IEnumerable<CatalogTreeNode> Descendants(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 })
                foreach (CatalogTreeNode child in Descendants(node.Children)) yield return child;
        }
    }

    private static string GeometryPerimeterSourceId(string geometryPath)
    {
        string canonical = SourceFilePaths.Canonical(Path.GetFullPath(geometryPath));
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return "geometry-perimeter:" + key;
    }

    private static Project OpenProject(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext) =>
        CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml, coreLayerContext);

    private static ProjectScope OpenProjectScope(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext) =>
        new(OpenProject(projectPath, projectXml, coreLayerContext));

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private static ProgressReporter CreateReporter(Action<BackendOperationProgress>? report)
    {
        var reporter = new ProgressReporter();
        if (report is null) return reporter;
        reporter.ProgressReported += (sender, percentage) => report(new(
            Fraction: Math.Clamp(percentage / 100.0, 0, 1), TaskName: sender.TaskName));
        reporter.MessageReported += (sender, message) => report(new(
            Message: message.EvaluatedMessage, TaskName: sender.TaskName));
        return reporter;
    }

    private static Task? StartControlPump(BackendDuplexControlReader? controls, ProgressReporter reporter,
        CancellationTokenSource cancellation, CancellationToken stopToken)
    {
        if (controls is null) return null;
        return Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in controls.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                if (frame.Control.TryGetProperty("type", out JsonElement type) &&
                    string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                {
                    reporter.RequestCancel();
                    try { cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }, stopToken);
    }

    private static string ValidateKind(string? kind) => kind switch
    {
        "NLD" or "NSI" or "NFHL" or "OSM" => kind,
        _ => throw new ArgumentException("Choose NLD, NSI, NFHL, or OSM for a feature query.", nameof(kind))
    };

    private static FeatureQueryExtent? ValidateExtent(FeatureQueryExtent? extent, bool required)
    {
        if (extent is null)
        {
            if (required) throw new ValidationException("The current map extent is unavailable.");
            return null;
        }
        if (!double.IsFinite(extent.MinX) || !double.IsFinite(extent.MinY) ||
            !double.IsFinite(extent.MaxX) || !double.IsFinite(extent.MaxY) ||
            extent.MaxX <= extent.MinX || extent.MaxY <= extent.MinY)
            throw new ValidationException("The current map extent is not a valid area to query.");
        return extent;
    }

    private static Extent ToSourceExtent(FeatureQueryExtent extent) =>
        new(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY);

    private static string ValidateNldLayer(string? layer) => layer switch
    {
        "AlignmentLines" or "Embankments" or "Floodwalls" or "SystemRoutes" => layer,
        _ => throw new ArgumentException("Choose a supported NLD feature layer.", nameof(layer))
    };

    private static string NldLayerLabel(string layer) => layer switch
    {
        "AlignmentLines" => "alignment line features",
        "Embankments" => "embankment features",
        "Floodwalls" => "floodwall features",
        "SystemRoutes" => "RAS merged alignment features",
        _ => throw new ArgumentOutOfRangeException(nameof(layer))
    };

    private static string SanitizeQueryLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "Query";
        string trimmed = label.Trim();
        var result = new StringBuilder(trimmed.Length);
        foreach (char character in trimmed)
        {
            if (char.IsControl(character) || Array.IndexOf(WindowsInvalidFileNameChars, character) >= 0) continue;
            result.Append(char.IsWhiteSpace(character) ? '_' : character);
        }
        return result.Length == 0 ? "Query" : result.ToString();
    }

    private static string BuildFoundInfo(int count, string featureName, string kind) => count == 0
        ? kind == "NFHL" ? "No flood hazard zones found." : "No " + featureName + " found."
        : $"Found {count:N0} {featureName}{Environment.NewLine}{Environment.NewLine}Click 'Save to Disk' to save and add to map.";

    private static string FingerprintQueryOptions(FeatureQueryQueryRequest request, string queryLabel,
        FeatureQueryExtent? extent)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.Kind,
            request.NldLayerId,
            request.UseBoundaryPolygon,
            request.PolygonSourceId,
            request.RawQl,
            QueryLabel = queryLabel,
            Extent = extent
        }, BackendJson.Options);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static SelectedFormat ParseFormat(string? format) => format switch
    {
        "Shapefile" => SelectedFormat.Shapefile,
        "GeoJson" => SelectedFormat.GeoJson,
        _ => throw new ArgumentException("Choose Shapefile or GeoJSON as the export format.", nameof(format))
    };

    private static string ValidateOutputDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Choose an output folder in the native file dialog.", nameof(directory));
        string path = Path.GetFullPath(directory);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The chosen output folder is unavailable.");
        return path;
    }

    private static string ValidateResultId(string? resultId)
    {
        if (!Guid.TryParseExact(resultId, "N", out _))
            throw new ArgumentException("The feature query result is no longer available. Query the map again.", nameof(resultId));
        return resultId!;
    }

    private static string StageRootPath() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), StageRootName));

    private static DirectoryInfo GetStageRoot()
    {
        string path = StageRootPath();
        DirectoryInfo info = new(path);
        if (IsStageLink(info))
            throw new IOException("The feature query staging directory cannot be a symbolic link.");
        return info;
    }

    private static string StageDirectory(string resultId)
    {
        string root = GetStageRoot().FullName;
        string directory = Path.GetFullPath(Path.Combine(root, resultId));
        string relative = Path.GetRelativePath(root, directory);
        if (Path.IsPathRooted(relative) || relative != resultId ||
            !Guid.TryParseExact(resultId, "N", out _))
            throw new ArgumentException("The feature query result identifier is invalid.", nameof(resultId));
        return directory;
    }

    private static string OwnedStageFile(string stageDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            fileName is "." or ".." || fileName.Any(character => char.IsControl(character) ||
                Array.IndexOf(WindowsInvalidFileNameChars, character) >= 0))
            throw new InvalidDataException("The feature query result contains an invalid staging file path.");
        string path = Path.GetFullPath(Path.Combine(stageDirectory, fileName));
        if (!string.Equals(Path.GetDirectoryName(path), stageDirectory,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !File.Exists(path) || IsStageLink(new FileInfo(path)))
            throw new SourceFileChangedException();
        return path;
    }

    private static void WriteManifest(string stageDirectory, StageManifest manifest)
    {
        string manifestPath = Path.Combine(stageDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, BackendJson.Options), Encoding.UTF8);
    }

    private static StageManifest ReadManifest(string stageDirectory, string resultId,
        out string manifestRevision, bool allowExpired = false, bool verifySources = true)
    {
        VerifyStageOwner(stageDirectory, resultId);
        byte[] bytes = ReadStageBytes(OwnedStageFile(stageDirectory, "manifest.json"), 256 * 1024);
        manifestRevision = HashStageBytes(bytes);
        StageManifest manifest = JsonSerializer.Deserialize<StageManifest>(
            WithoutUtf8Bom(bytes).Span, BackendJson.Options)
            ?? throw new InvalidDataException("无法读取查询暂存清单，请重新查询。");
        if (manifest.Version != StageVersion || manifest.ResultId != resultId ||
            manifest.Map is null || manifest.Layers is null || manifest.Layers.Length > 32 ||
            manifest.Kind is not ("NLD" or "NSI" or "NFHL" or "OSM"))
            throw new InvalidDataException("查询暂存清单不完整，请重新查询。");
        ValidateStageManifest(manifest, allowExpired);
        VerifyStageSnapshot(stageDirectory, manifest, manifestRevision, verifySources);
        return manifest;
    }

    private static void CleanupExpiredStages(DirectoryInfo root)
    {
        DateTime cutoff = DateTime.UtcNow - StageLifetime;
        foreach (DirectoryInfo directory in root.EnumerateDirectories())
        {
            if (IsStageLink(directory) || directory.CreationTimeUtc >= cutoff ||
                !Guid.TryParseExact(directory.Name, "N", out _)) continue;
            DeleteOwnedStageDirectory(directory.FullName, directory.Name);
        }
    }

    private static void DeleteOwnedStageDirectory(string directory, string resultId, bool leaseHeld = false)
    {
        try
        {
            DeleteOwnedStageDirectoryStrict(directory, resultId, leaseHeld);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
    }

    private static void DeleteOwnedStageDirectoryStrict(string directory, string resultId, bool leaseHeld)
    {
        string expected = StageDirectory(resultId);
        if (!string.Equals(Path.GetFullPath(directory), expected,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("查询暂存路径与所有者不匹配。");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(expected); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            throw new IOException("查询暂存路径不是普通目录。");
        using StageLease? stageLease = leaseHeld ? null : AcquireStageLease(expected, resultId);
        DeleteStageDirectory(expected, resultId);
        try
        {
            _ = File.GetAttributes(expected);
            throw new IOException("查询暂存删除后仍然存在。");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private sealed class FeatureQueryExportCleanupException : IOException
    {
        public IReadOnlyList<string> Problems { get; }

        public FeatureQueryExportCleanupException(IEnumerable<string> problems, Exception cause)
            : base("查询导出未完成，无法确认以下输出已清理：" + string.Join("；", problems), cause)
        {
            Problems = problems.ToArray();
        }
    }

    private static IReadOnlyList<string> DeleteExportFiles(string primaryPath, SelectedFormat format)
    {
        string[] paths = format == SelectedFormat.GeoJson
            ? [primaryPath]
            : [primaryPath, ShapefileIO.SHXFilename(primaryPath), ShapefileIO.DBFFilename(primaryPath),
                Path.ChangeExtension(primaryPath, ".prj"), Path.ChangeExtension(primaryPath, ".cpg")];
        var problems = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new IOException("输出路径是链接或目录。");
                File.Delete(path);
                try
                {
                    _ = File.GetAttributes(path);
                    throw new IOException("删除后仍可见输出。");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                problems.Add(path + ": " + error.Message);
            }
        }
        return problems;
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RequireSuccess(OperationResult result, string operationName)
    {
        if (!result.Result) throw new InvalidOperationException(DescribeFailure(operationName, result));
    }

    private static string DescribeFailure(string operationName, OperationResult result)
    {
        string messages = result.GetConcatenatedMessages();
        return string.IsNullOrWhiteSpace(messages) ? $"The {operationName} did not complete." : messages;
    }

    private enum SelectedFormat
    {
        Shapefile,
        GeoJson
    }
}
