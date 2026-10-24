using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record MapAnalysisAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string ProjectPath,
    string ResultPath,
    string ResultRevision,
    string VisualizationPath,
    string VisualizationRevision,
    string VectorPath,
    string VectorRevision,
    int CellSeriesCount,
    int FaceSeriesCount,
    int SampleLinePointCount,
    bool CumulativeVolumeTimeAxisVerified,
    bool CellAndFaceResultIdentityVerified,
    bool PersistedSampleLineVerified,
    bool CulvertProfileAbsenceReported,
    bool CulvertTimeSeriesAbsenceRejected,
    bool SourceFixtureBytesUnchanged);

internal sealed record MapAnalysisCoordinateHashVector(
    string Name,
    GeometryTopologyCoordinate[] Coordinates,
    string Sha);

/// <summary>
/// Exercises persisted map Result chart routes against an already completed
/// Result-hover fixture. All writes are isolated to a copied project folder.
/// </summary>
internal static partial class ServerAcceptance
{
    private static readonly TimeSpan MapAnalysisTimeout = TimeSpan.FromMinutes(8);

    public static async Task<MapAnalysisAcceptanceReport> RunMapAnalysisAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Map analysis acceptance only connects to a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(resultHoverManifestPath);
        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(sourceManifestPath), JsonOptions)
            ?? throw new InvalidDataException("The source Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || source.Sources.Length < 2 ||
            !File.Exists(source.ProjectPath) || !File.Exists(source.VisualizationPath))
            throw new InvalidDataException("Map analysis acceptance requires a completed Result-hover project, .vis, and HDF manifest.");

        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source.ProjectPath))
            ?? throw new InvalidDataException("The Result-hover project has no containing directory.");
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Map analysis acceptance requires a new output directory; existing data is never overwritten.");
        if (IsPathWithin(outputDirectory, sourceRoot) || IsPathWithin(sourceRoot, outputDirectory))
            throw new IOException("The isolated map-analysis output must not overlap the Result-hover source fixture.");

        Directory.CreateDirectory(outputDirectory);
        string copyRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        CopyDirectoryNoOverwrite(sourceRoot, copyRoot);
        string Rebase(string path) => RebaseFixturePath(path, sourceRoot, copyRoot);
        string projectPath = Rebase(source.ProjectPath);
        string visualizationPath = Rebase(source.VisualizationPath);
        string planPath = Rebase(source.PlanPath);
        if (!File.Exists(planPath))
            throw new FileNotFoundException("The copied Result-hover Plan is missing.", planPath);

        string[] originalHdfPaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> originalHdfHashes = originalHdfPaths.ToDictionary(
            path => path, MapAnalysisFileSha256, StringComparer.Ordinal);
        foreach (ResultHoverSourceManifest sourceResult in source.Sources)
        {
            string copiedResult = Rebase(sourceResult.ResultPath);
            if (!File.Exists(copiedResult) ||
                !MapAnalysisFileSha256(copiedResult).Equals(sourceResult.Revision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A copied Result HDF does not match the source manifest revision.");
        }
        if (!MapAnalysisFileSha256(visualizationPath).Equals(source.VisualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The copied .vis bytes do not match the source manifest revision.");

        using var timeout = new CancellationTokenSource(MapAnalysisTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The map-analysis server did not report ready.");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        string openedProjectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        if (!PathEquals(openedProjectPath, projectPath))
            throw new SourceFileChangedException();
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = MapAnalysisSha256(Encoding.UTF8.GetBytes(projectXml));
        string visualizationRevision = MapAnalysisFileSha256(visualizationPath);
        string sessionID = Guid.NewGuid().ToString("D");
        string mapID = Guid.NewGuid().ToString("D");
        int mapIndex = source.Map.MapIndex;
        string mapName = source.Map.MapName;
        string mapType = source.Map.MapType;

        object MapRequest(string expectedRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            mapID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision = expectedRevision,
            projectSessionID = sessionID,
        };

        object MapDocument(string expectedRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedVisualizationRevision = expectedRevision,
            kind = mapType,
            mapID,
            mapIndex,
            mapName,
            mapType,
        };

        string resultPath = Rebase(source.Sources.OrderBy(item => item.SelectionIndex).First().ResultPath);
        string resultRevision = MapAnalysisFileSha256(resultPath);
        ResultHoverSourceManifest firstSource = source.Sources.OrderBy(item => item.SelectionIndex).First();
        ResultHoverSourceManifest secondSource = source.Sources.OrderBy(item => item.SelectionIndex).Skip(1).First();
        if (!resultRevision.Equals(firstSource.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        CatalogTreeNode[] nodes = DeserializeMapTree(Property(tree, "roots"));
        CatalogTreeNode resultNode = nodes.SingleOrDefault(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator.AssetID, resultPath))
            ?? throw new InvalidDataException("The copied saved map tree does not resolve the exact copied Result HDF.");
        CatalogTreeNodeLocator resultLocator = resultNode.Locator
            ?? throw new InvalidDataException("The exact Result source node has no locator.");

        // Make this exact Result active in the copied .vis, then reload its locator.
        JsonElement visibilityReceipt = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = MapRequest(visualizationRevision),
            action = "visibility",
            node = resultLocator,
            @checked = true,
        }, cancellation)).Result;
        if (!string.Equals(OptionalString(visibilityReceipt, "previousVisualizationRevision"),
                visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Result visibility transaction used an unexpected previous .vis revision.");
        visualizationRevision = StringProperty(visibilityReceipt, "visualizationRevision");
        AssertDiskRevision(visualizationPath, visualizationRevision,
            "The copied .vis bytes do not match the Result visibility receipt.");
        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        nodes = DeserializeMapTree(Property(tree, "roots"));
        resultNode = nodes.SingleOrDefault(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator.AssetID, resultPath))
            ?? throw new InvalidDataException("The exact Result node disappeared after visibility changed.");
        resultLocator = resultNode.Locator
            ?? throw new InvalidDataException("The refreshed Result source node has no locator.");
        CatalogTreeNode multiResultNode = nodes.SingleOrDefault(node =>
            node.Locator?.NodeType == "MultiResult" &&
            string.Equals(node.Name, Path.GetFileNameWithoutExtension(planPath), StringComparison.Ordinal))
            ?? throw new InvalidDataException("The copied saved map has no exact Plan MultiResult node.");
        CatalogTreeNodeLocator multiResultLocator = multiResultNode.Locator
            ?? throw new InvalidDataException("The exact Plan MultiResult node has no locator.");

        MapResultSelectionOverride[] selectionOverrides =
        [
            new(resultLocator, 0, "Explicit", resultPath, resultRevision),
            new(multiResultLocator, 0, "Explicit", resultPath, resultRevision),
            new(multiResultLocator, 1, "Explicit", Rebase(secondSource.ResultPath), secondSource.Revision),
        ];
        JsonElement resultSources = (await SubmitOperationAsync(client, "map.resultSources", new
        {
            map = MapRequest(visualizationRevision),
            selectionOverrides,
        }, cancellation)).Result;
        string contextToken = StringProperty(resultSources, "contextToken");
        JsonElement[] activeNodes = Property(resultSources, "activeNodes").EnumerateArray().ToArray();
        JsonElement activeResult = activeNodes.SingleOrDefault(node =>
            StringProperty(node, "nodeKind") == "Result" &&
            LocatorMatches(Property(node, "node"), resultLocator));
        if (activeResult.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The Result source selection did not produce one exact active map Result.");
        JsonElement selectedChoice = Property(activeResult, "selections").EnumerateArray()
            .Single(slot => IntProperty(slot, "selectionIndex") == 0);
        JsonElement chosenResult = Property(selectedChoice, "selectedResult");
        if (!PathEquals(StringProperty(chosenResult, "resultPath"), resultPath) ||
            !StringProperty(chosenResult, "revision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Map Result selection returned a different HDF path or revision.");

        object meshOwner = new
        {
            map = MapRequest(visualizationRevision),
            projectSessionID = sessionID,
            contextToken,
            selectionOverrides,
            geometryOrigin = (object?)null,
            resultOrigin = new
            {
                resultNode = resultLocator,
                selectionIndex = 0,
                selectedResultPath = resultPath,
                selectedResultRevision = resultRevision,
            },
        };
        JsonElement meshInspection = (await SubmitOperationAsync(client,
            "map.result.meshTimeSeries.inspect", new { owner = meshOwner }, cancellation)).Result;
        if (StringProperty(meshInspection, "state") != "ready" ||
            StringProperty(meshInspection, "originKind") != "Result" ||
            !PathEquals(StringProperty(meshInspection, "originPath"), resultPath) ||
            !StringProperty(meshInspection, "originRevision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mesh Result inspect did not retain the exact persisted Result identity.");
        JsonElement flowArea = Property(meshInspection, "flowAreas").EnumerateArray()
            .SingleOrDefault(area => IntProperty(area, "flowAreaIndex") == 0 && BooleanProperty(area, "hasMesh"));
        if (flowArea.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The persisted Result has no inspectable mesh in Flow Area zero.");
        string flowAreaID = StringProperty(flowArea, "flowAreaID");
        string flowAreaName = StringProperty(flowArea, "name");
        int cellCount = IntProperty(flowArea, "cellCount");
        int faceCount = IntProperty(flowArea, "faceCount");
        if (cellCount <= 0 || faceCount <= 0 ||
            !Property(meshInspection, "matchingResultNames").EnumerateArray().Any())
            throw new InvalidDataException("Mesh Result inspect returned no real cells, faces, or matching saved Results.");

        JsonElement cellSeries = await ExecuteMeshSeriesAsync("Cell", cellCount);
        JsonElement faceSeries = await ExecuteMeshSeriesAsync("Face", faceCount);

        async Task<JsonElement> ExecuteMeshSeriesAsync(string elementKind, int elementCount)
        {
            if (elementCount <= 0) throw new InvalidDataException("The inspected mesh has no selected element.");
            string operationID = Guid.NewGuid().ToString("N");
            JsonElement result = (await SubmitOperationAsync(client, "map.result.meshTimeSeries.execute", new
            {
                operationID,
                owner = meshOwner,
                flowAreaIndex = 0,
                flowAreaID,
                expectedFlowAreaName = flowAreaName,
                elementKind,
                elementIndex = 0,
            }, cancellation)).Result;
            if (StringProperty(result, "state") != "ready" ||
                StringProperty(result, "operationID") != operationID ||
                StringProperty(result, "elementKind") != elementKind ||
                IntProperty(result, "elementIndex") != 0 ||
                IntProperty(result, "flowAreaIndex") != 0 ||
                StringProperty(result, "flowAreaID") != flowAreaID ||
                !StringProperty(result, "contextToken").Equals(contextToken, StringComparison.Ordinal) ||
                !PathEquals(StringProperty(result, "originPath"), resultPath) ||
                !StringProperty(result, "originRevision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The {elementKind} time-series receipt lost its exact Result/mesh request identity.");
            JsonElement[] series = Property(result, "series").EnumerateArray().ToArray();
            if (series.Length != 1 || !PathEquals(StringProperty(series[0], "resultPath"), resultPath) ||
                !StringProperty(series[0], "resultRevision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase) ||
                IntProperty(series[0], "flowAreaIndex") != 0 ||
                StringProperty(series[0], "variable").Length == 0)
                throw new InvalidDataException($"The {elementKind} time-series did not return the exact persisted Result series.");
            string[] times = Property(series[0], "times").EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty).ToArray();
            double[] values = Property(series[0], "values").EnumerateArray()
                .Select(value => value.GetDouble()).ToArray();
            if (times.Length == 0 || values.Length != times.Length || values.Any(value => !double.IsFinite(value)))
                throw new InvalidDataException($"The {elementKind} time-series has no aligned finite stored values.");
            return result;
        }

        string linePath = Path.Combine(copyRoot, "Map Analysis Sample Line.geojson");
        const string featureID = "map-analysis-line-0";
        string lineJson = "{\"type\":\"FeatureCollection\",\"features\":[" +
                          "{\"type\":\"Feature\",\"id\":\"" + featureID + "\",\"properties\":{\"purpose\":\"map-analysis-acceptance\"}," +
                          "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[0.5,0.5],[3.5,3.5]]}}]}";
        await File.WriteAllTextAsync(linePath, lineJson, new UTF8Encoding(false), cancellation);
        string lineRevision = MapAnalysisFileSha256(linePath);
        JsonElement importReceipt = (await SubmitOperationAsync(client, "map.vector.import", new
        {
            map = MapDocument(visualizationRevision),
            sourcePath = linePath,
            sourceRevision = lineRevision,
            expectedProjectRevision = projectRevision,
            expectedSourceCanonicalPath = Path.GetFullPath(linePath),
        }, cancellation)).Result;
        if (!string.Equals(OptionalString(importReceipt, "previousVisualizationRevision"),
                visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The sample-line import used an unexpected previous .vis revision.");
        visualizationRevision = StringProperty(importReceipt, "visualizationRevision");
        AssertDiskRevision(visualizationPath, visualizationRevision,
            "The copied .vis bytes do not match the sample-line import receipt.");

        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        nodes = DeserializeMapTree(Property(tree, "roots"));
        resultNode = nodes.SingleOrDefault(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator.AssetID, resultPath))
            ?? throw new InvalidDataException("The exact Result node disappeared after importing the sample line.");
        resultLocator = resultNode.Locator
            ?? throw new InvalidDataException("The refreshed Result source node has no exact locator.");
        multiResultNode = nodes.SingleOrDefault(node => node.Locator?.NodeType == "MultiResult" &&
            string.Equals(node.Name, Path.GetFileNameWithoutExtension(planPath), StringComparison.Ordinal))
            ?? throw new InvalidDataException("The exact Plan MultiResult node disappeared after line import.");
        multiResultLocator = multiResultNode.Locator
            ?? throw new InvalidDataException("The refreshed Plan MultiResult node has no locator.");
        selectionOverrides =
        [
            new(resultLocator, 0, "Explicit", resultPath, resultRevision),
            new(multiResultLocator, 0, "Explicit", resultPath, resultRevision),
            new(multiResultLocator, 1, "Explicit", Rebase(secondSource.ResultPath), secondSource.Revision),
        ];
        resultSources = (await SubmitOperationAsync(client, "map.resultSources", new
        {
            map = MapRequest(visualizationRevision),
            selectionOverrides,
        }, cancellation)).Result;
        contextToken = StringProperty(resultSources, "contextToken");
        activeNodes = Property(resultSources, "activeNodes").EnumerateArray().ToArray();
        activeResult = activeNodes.SingleOrDefault(node =>
            StringProperty(node, "nodeKind") == "Result" &&
            LocatorMatches(Property(node, "node"), resultLocator));
        if (activeResult.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The imported sample line invalidated the selected persisted Result context.");
        selectedChoice = Property(activeResult, "selections").EnumerateArray()
            .Single(slot => IntProperty(slot, "selectionIndex") == 0);
        chosenResult = Property(selectedChoice, "selectedResult");
        if (!PathEquals(StringProperty(chosenResult, "resultPath"), resultPath) ||
            !StringProperty(chosenResult, "revision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Refreshing the line-map context changed the selected Result HDF.");
        CatalogTreeNode lineNode = nodes.SingleOrDefault(node => node.Locator is
            { NodeType: "MapLayer", MapType: "PolylinesVector" } locator &&
            PathEquals(locator.AssetID, linePath))
            ?? throw new InvalidDataException("The saved map tree did not resolve the imported polyline source.");
        CatalogTreeNodeLocator lineLocator = lineNode.Locator
            ?? throw new InvalidDataException("The saved sample-line source has no exact locator.");
        JsonElement vectorRead = (await SubmitOperationAsync(client, "map.vector.source.load", new
        {
            map = MapRequest(visualizationRevision),
            node = lineLocator,
            expectedFamily = (object?)null,
        }, cancellation)).Result;
        JsonElement vectorSource = Property(vectorRead, "source");
        JsonElement family = Property(vectorSource, "family");
        JsonElement primaryFile = Property(family, "files").EnumerateArray().Single(file =>
            StringProperty(file, "role") == "source" && BooleanProperty(file, "required") &&
            BooleanProperty(file, "exists") && PathEquals(StringProperty(file, "path"), linePath));
        if (!PathEquals(StringProperty(family, "sourcePath"), linePath) ||
            !StringProperty(primaryFile, "revision").Equals(lineRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The persisted polyline reader returned a different source path or SHA.");
        JsonElement vectorData = Property(vectorRead, "data");
        if (StringProperty(vectorData, "shapeType") != "Polyline" || IntProperty(vectorData, "count") != 1)
            throw new InvalidDataException("The persisted sample-line source did not read as exactly one polyline.");
        JsonElement feature = Property(vectorData, "features").EnumerateArray().Single();
        string returnedFeatureID = StringProperty(feature, "sourceID");
        if (!string.Equals(returnedFeatureID, featureID, StringComparison.Ordinal))
            throw new InvalidDataException("The vector reader did not preserve the imported source feature identity.");
        JsonElement[] parts = Property(feature, "parts").EnumerateArray().ToArray();
        if (parts.Length != 1)
            throw new InvalidDataException("The imported sample line does not contain exactly one persisted path.");
        GeometryTopologyCoordinate[] sampleLine = Property(parts[0], "vertices").EnumerateArray()
            .Select(vertex => new GeometryTopologyCoordinate(
                Property(vertex, "x").GetDouble(), Property(vertex, "y").GetDouble())).ToArray();
        if (sampleLine.Length < 2 || sampleLine.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("The persisted sample-line path has fewer than two finite map coordinates.");
        string coordinateSHA = HostCoordinateSha256(sampleLine);
        JsonElement sampleResult = (await SubmitOperationAsync(client, "map.resultSampleLine", new
        {
            map = MapRequest(visualizationRevision),
            contextToken,
            selectionOverrides,
            feature = new
            {
                vectorPath = linePath,
                vectorRevision = lineRevision,
                featureID = returnedFeatureID,
                coordinateSHA,
            },
            sampleLine,
            mode = "Flow",
        }, cancellation)).Result;
        if (!PathEquals(StringProperty(sampleResult, "projectPath"), projectPath) ||
            !StringProperty(sampleResult, "contextToken").Equals(contextToken, StringComparison.Ordinal) ||
            StringProperty(sampleResult, "mode") != "Flow" ||
            !PathEquals(StringProperty(Property(sampleResult, "feature"), "vectorPath"), linePath) ||
            !StringProperty(Property(sampleResult, "feature"), "vectorRevision")
                .Equals(lineRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(Property(sampleResult, "feature"), "coordinateSHA")
                .Equals(coordinateSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The map sample-line response lost its persisted vector/result context.");
        JsonElement[] sampleSeries = Property(sampleResult, "series").EnumerateArray().ToArray();
        JsonElement[] matchingSampleSeries = sampleSeries.Where(item =>
            PathEquals(StringProperty(Property(item, "series"), "resultPath"), resultPath) &&
            StringProperty(Property(item, "series"), "revision")
                .Equals(resultRevision, StringComparison.OrdinalIgnoreCase) &&
            BooleanProperty(Property(item, "series"), "available")).ToArray();
        if (matchingSampleSeries.Length == 0)
            throw new InvalidDataException("The map sample-line returned no available series from the selected saved Result.");
        JsonElement[] samplePoints = Property(Property(matchingSampleSeries[0], "series"), "points")
            .EnumerateArray().ToArray();
        if (samplePoints.Length == 0 || samplePoints.Any(point =>
                PropertyOrNull(point, "value").ValueKind != JsonValueKind.Number ||
                !double.IsFinite(Property(point, "value").GetDouble())))
            throw new InvalidDataException("The persisted Result sample-line series has no aligned finite values.");

        JsonElement cumulativeResult = (await SubmitOperationAsync(client, "map.resultSampleLine", new
        {
            map = MapRequest(visualizationRevision), contextToken, selectionOverrides,
            feature = new { vectorPath = linePath, vectorRevision = lineRevision,
                featureID = returnedFeatureID, coordinateSHA },
            sampleLine, mode = "CumulativeVolume",
        }, cancellation)).Result;
        if (StringProperty(cumulativeResult, "mode") != "CumulativeVolume" ||
            StringProperty(cumulativeResult, "contextToken") != contextToken ||
            !string.Equals(StringProperty(cumulativeResult, "projectRevision"), projectRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cumulative-volume sampling lost its current project/map owner.");
        JsonElement cumulativeSeries = Property(cumulativeResult, "series").EnumerateArray().First(item =>
            PathEquals(StringProperty(Property(item, "series"), "resultPath"), resultPath) &&
            string.Equals(StringProperty(Property(item, "series"), "revision"), resultRevision,
                StringComparison.OrdinalIgnoreCase));
        JsonElement cumulativeData = Property(cumulativeSeries, "series");
        JsonElement[] volumePoints = Property(cumulativeData, "points").EnumerateArray().ToArray();
        if (!BooleanProperty(cumulativeData, "available") || StringProperty(cumulativeData, "quantity") != "Volume" ||
            volumePoints.Length != samplePoints.Length || volumePoints.Where((point, index) =>
                StringProperty(point, "time") != StringProperty(samplePoints[index], "time") ||
                PropertyOrNull(point, "value").ValueKind != JsonValueKind.Number ||
                !double.IsFinite(Property(point, "value").GetDouble())).Any())
            throw new InvalidDataException("Cumulative-volume sampling did not preserve the Result time axis and finite values.");

        JsonElement culvertProfile = (await SubmitOperationAsync(client, "map.result.culvertProfile", new
        {
            map = MapRequest(visualizationRevision),
            contextToken,
            selectionOverrides,
            resultNode = resultLocator,
            selectionIndex = 0,
            profileIndex = 0,
        }, cancellation)).Result;
        string? culvertDiagnostic = OptionalString(culvertProfile, "diagnostic");
        bool culvertProfileAbsent = !BooleanProperty(culvertProfile, "outputAvailable") &&
            Property(culvertProfile, "barrels").GetArrayLength() == 0 &&
            !string.IsNullOrWhiteSpace(culvertDiagnostic) &&
            culvertDiagnostic.Contains("culvert", StringComparison.OrdinalIgnoreCase) &&
            StringProperty(culvertProfile, "selectedResultRevision")
                .Equals(resultRevision, StringComparison.OrdinalIgnoreCase);
        if (!culvertProfileAbsent)
            throw new InvalidDataException("The no-culvert source Result did not report the expected unavailable profile diagnostic.");

        MapResultGeometryOwner resultOwner = new(resultLocator, contextToken,
            selectionOverrides, 0, resultPath, resultRevision);
        string culvertFailureMessage = await ExpectOperationFailureMessageAsync(client,
            "map.result.culvertTimeSeries", new
            {
                map = MapRequest(visualizationRevision),
                resultOwner,
                barrelIDs = new[] { "barrel:source:0" },
                variable = "Flow",
            }, cancellation);
        bool culvertSeriesAbsent = culvertFailureMessage.Contains(
                "no usable culvert barrel geometry", StringComparison.OrdinalIgnoreCase) ||
            culvertFailureMessage.Contains("no culvert barrel geometry", StringComparison.OrdinalIgnoreCase);
        if (!culvertSeriesAbsent)
            throw new InvalidDataException("The no-culvert Result time-series failure did not identify missing source culvert geometry.");

        bool sourceFixtureBytesUnchanged = originalHdfHashes.All(item =>
            File.Exists(item.Key) && MapAnalysisFileSha256(item.Key).Equals(item.Value, StringComparison.OrdinalIgnoreCase)) &&
            MapAnalysisFileSha256(source.VisualizationPath).Equals(source.VisualizationRevision, StringComparison.OrdinalIgnoreCase) &&
            MapAnalysisFileSha256(source.ProjectPath).Equals(source.ProjectRevision, StringComparison.OrdinalIgnoreCase);
        if (!sourceFixtureBytesUnchanged)
            throw new InvalidDataException("Map analysis acceptance changed bytes in the reusable source fixture.");

        string manifestPath = Path.Combine(outputDirectory, "map-analysis-http-e2e-manifest.json");
        var report = new MapAnalysisAcceptanceReport("completed", manifestPath, sourceManifestPath,
            projectPath, resultPath, resultRevision, visualizationPath, visualizationRevision,
            linePath, lineRevision, Property(cellSeries, "series").GetArrayLength(),
            Property(faceSeries, "series").GetArrayLength(), samplePoints.Length,
            CumulativeVolumeTimeAxisVerified: true,
            CellAndFaceResultIdentityVerified: true, PersistedSampleLineVerified: true,
            CulvertProfileAbsenceReported: culvertProfileAbsent,
            CulvertTimeSeriesAbsenceRejected: culvertSeriesAbsent,
            SourceFixtureBytesUnchanged: sourceFixtureBytesUnchanged);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    /// <summary>Returns cross-runtime vectors from the Host's actual private coordinate hash implementation.</summary>
    public static MapAnalysisCoordinateHashVector[] CoordinateHashVectors()
    {
        GeometryTopologyCoordinate[] asymmetric =
        [
            new(12.5, -7.25),
            new(0.125, 3.5),
            new(-999, 42),
        ];
        GeometryTopologyCoordinate[] negativeZeroAndLarge =
        [
            new(-0d, 1d),
            new(1e20, -123.25),
        ];
        GeometryTopologyCoordinate[] reversed = negativeZeroAndLarge.Reverse().ToArray();
        return
        [
            new("asymmetric-three-points", asymmetric, HostCoordinateSha256(asymmetric)),
            new("negative-zero-and-large-value", negativeZeroAndLarge, HostCoordinateSha256(negativeZeroAndLarge)),
            new("reversed-negative-zero-and-large-value", reversed, HostCoordinateSha256(reversed)),
        ];
    }

    private static string HostCoordinateSha256(IEnumerable<GeometryTopologyCoordinate> coordinates)
    {
        MethodInfo method = typeof(MapResultChartService).GetMethod("HashCoordinates",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(MapResultChartService).FullName, "HashCoordinates");
        return method.Invoke(null, [coordinates]) as string
            ?? throw new InvalidDataException("The Host coordinate hash did not return a SHA-256 string.");
    }

    private static async Task<string> ExpectOperationFailureMessageAsync(HttpClient client,
        string method, object parameters, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}) returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument submission = JsonDocument.Parse(body);
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                if (state != "failed")
                    throw new InvalidDataException($"Expected {method} to reject the missing source, but it ended in {state}.");
                string message = OptionalString(Property(root, "error"), "message") ?? string.Empty;
                if (message.Length == 0)
                    throw new InvalidDataException($"The failed {method} operation omitted its diagnostic message.");
                return message;
            }
            await Task.Delay(50, cancellation);
        }
    }

    private static CatalogTreeNode[] DeserializeMapTree(JsonElement roots)
    {
        CatalogTreeNode[] source = JsonSerializer.Deserialize<CatalogTreeNode[]>(roots.GetRawText(), JsonOptions) ?? [];
        return Flatten(source).ToArray();

        static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> nodes)
        {
            foreach (CatalogTreeNode node in nodes)
            {
                yield return node;
                if (node.Children is { Length: > 0 } children)
                    foreach (CatalogTreeNode child in Flatten(children)) yield return child;
            }
        }
    }

    private static bool LocatorMatches(JsonElement actual, CatalogTreeNodeLocator expected)
    {
        CatalogTreeNodeLocator? locator = JsonSerializer.Deserialize<CatalogTreeNodeLocator>(actual.GetRawText(), JsonOptions);
        return locator is not null && locator.NodeType == expected.NodeType &&
            locator.NodeName == expected.NodeName && locator.RootName == expected.RootName &&
            PathEquals(locator.AssetID, expected.AssetID) &&
            (locator.NodePath ?? []).SequenceEqual(expected.NodePath ?? []);
    }

    private static void CopyDirectoryNoOverwrite(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("The map-analysis fixture copy path already exists: " + destination);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectoryNoOverwrite(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string RebaseFixturePath(string path, string sourceRoot, string destinationRoot)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(sourceRoot, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException("A Result-hover source path is outside the copied fixture directory.");
        return Path.GetFullPath(Path.Combine(destinationRoot, relative));
    }

    private static bool IsPathWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string MapAnalysisFileSha256(string path) => MapAnalysisSha256(File.ReadAllBytes(path));

    private static string MapAnalysisSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void AssertDiskRevision(string path, string expected, string message)
    {
        if (!MapAnalysisFileSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }
}
