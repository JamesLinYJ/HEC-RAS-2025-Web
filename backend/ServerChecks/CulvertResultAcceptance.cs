using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Meteorology;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record CulvertResultAcceptanceReport(
    string State,
    string ManifestPath,
    string ProjectPath,
    string PlanPath,
    string GeometryPath,
    string ResultPath,
    string ResultRevision,
    string VisualizationPath,
    string VisualizationRevision,
    int SourceBarrelCount,
    int ProfileCount,
    bool CpuSolveCompleted,
    bool ResultHdfCulvertOutputVerified,
    bool CulvertProfilesReadBack,
    bool FlowTimeSeriesVerified,
    bool OutletVelocityTimeSeriesVerified,
    bool NonZeroFlowObserved,
    string[] Times,
    string FlowUnits,
    string OutletVelocityUnits,
    double[] FlowValues,
    double[] OutletVelocityValues);

/// <summary>
/// Creates an isolated source fixture with a real culvert, solves its Plan on
/// the CPU, and reads the resulting HDF through the saved-map HTTP contracts.
/// The runner never writes computed Result datasets itself.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string CulvertFixturePlanName = "Plan CPU";
    private const string CulvertFixtureGeometryName = "Solver Geometry";
    private const string CulvertFixtureMapName = "涵洞结果验收地图";
    private const string CulvertFixtureBarrelName = "Acceptance Culvert";
    private const string CulvertFixtureBarrelPropertiesName = "Acceptance Barrel Properties";
    private const string CulvertFixtureUpstreamOpeningName = "Acceptance Upstream Opening";
    private const string CulvertFixtureDownstreamOpeningName = "Acceptance Downstream Opening";

    public static async Task<CulvertResultAcceptanceReport> RunCulvertResultAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Culvert Result acceptance only connects to a loopback HTTP(S) service.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Culvert Result acceptance requires a new output directory; existing data is never overwritten.");
        Directory.CreateDirectory(outputDirectory);

        string fixtureDirectory = Path.Combine(outputDirectory, "culvert-source-fixture");
        TextWriter originalOutput = Console.Out;
        FixtureManifest fixture;
        Console.SetOut(Console.Error);
        try { fixture = PlanComputeFixture.CreateMap(fixtureDirectory); }
        finally { Console.SetOut(originalOutput); }
        ConfigureCulvertFixture(fixture);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The culvert acceptance service did not report ready.");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = fixture.ProjectPath }, cancellation)).Result;
        string projectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        if (!PathEquals(projectPath, fixture.ProjectPath)) throw new SourceFileChangedException();
        string projectXml = StringProperty(opened, "projectXml");
        string projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));

        JsonElement planSnapshot = (await SubmitOperationAsync(client, "model.load", new
        {
            projectPath,
            projectXml,
            assetPath = fixture.PlanPath,
            kind = "Plan",
        }, cancellation)).Result;
        object model = new
        {
            assetPath = fixture.PlanPath,
            kind = "Plan",
            projectPath,
            revision = StringProperty(planSnapshot, "revision"),
            fields = new Dictionary<string, string>(),
            images = new Dictionary<string, ImageData[]>(),
            projectXml,
            sourceState = OptionalString(planSnapshot, "sourceState"),
        };
        JsonElement compute = await ComputeCulvertPlanAsync(client, model, cancellation);
        if (!string.Equals(StringProperty(compute, "state"), "completed", StringComparison.OrdinalIgnoreCase) ||
            !BooleanProperty(Property(compute, "summary"), "success"))
            throw new InvalidDataException("The genuine CPU Plan did not complete successfully.");

        string resultPath = Path.GetFullPath(StringProperty(compute, "resultPath"));
        string resultRevision = StringProperty(compute, "resultRevision");
        if (!File.Exists(resultPath) || !FileSha256(resultPath).Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The computed Result HDF bytes do not match the CPU compute receipt.");

        JsonElement resultCatalog = (await SubmitOperationAsync(client, "result.profileCatalog", new
        {
            resultPath,
            revision = resultRevision,
        }, cancellation)).Result;
        string resultName = StringProperty(resultCatalog, "resultName");

        JsonElement createdMap = (await SubmitOperationAsync(client, "map.document.create", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedVisualizationRevision = (string?)null,
            kind = "Plan",
            name = CulvertFixtureMapName,
            initialPlanPath = fixture.PlanPath,
            initialPlanRevision = FileSha256(fixture.PlanPath),
        }, cancellation)).Result;
        int mapIndex = IntProperty(createdMap, "mapIndex");
        string mapName = StringProperty(createdMap, "mapName");
        string mapType = StringProperty(createdMap, "mapType");
        if (!string.Equals(mapName, CulvertFixtureMapName, StringComparison.Ordinal) || mapType != "Plan")
            throw new InvalidDataException("The saved map creation receipt did not preserve the requested Plan map identity.");

        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        EnsureSerializedDirectResultNode(visualizationPath, mapName, mapType, resultName);
        string visualizationRevision = FileSha256(visualizationPath);
        string mapID = Guid.NewGuid().ToString("D");
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
            projectSessionID = mapID,
        };

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        CatalogTreeNode[] roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNode resultNode = FindExactResultNode(roots, resultPath);
        CatalogTreeNodeLocator resultLocator = resultNode.Locator
            ?? throw new InvalidDataException("The exact computed Result row has no map-tree locator.");
        visualizationRevision = await EnsureNodeCheckedAsync(client, MapRequest,
            visualizationRevision, resultLocator, cancellation);

        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        resultNode = FindExactResultNode(roots, resultPath);
        resultLocator = resultNode.Locator
            ?? throw new InvalidDataException("The refreshed exact Result row has no map-tree locator.");

        MapResultSelectionOverride[] selectionOverrides =
        [new(resultLocator, 0, "Explicit", resultPath, resultRevision)];
        JsonElement resultSources = (await SubmitOperationAsync(client, "map.resultSources", new
        {
            map = MapRequest(visualizationRevision),
            selectionOverrides,
        }, cancellation)).Result;
        string contextToken = StringProperty(resultSources, "contextToken");
        JsonElement[] activeResults = Property(resultSources, "activeNodes").EnumerateArray()
            .Where(node => StringProperty(node, "nodeKind") == "Result" &&
                LocatorEqual(Property(node, "node"), resultLocator)).ToArray();
        if (activeResults.Length != 1)
            throw new InvalidDataException("The saved Plan map did not resolve one active exact Result owner.");
        JsonElement selectedChoice = Property(activeResults[0], "selections").EnumerateArray()
            .Single(slot => IntProperty(slot, "selectionIndex") == 0);
        JsonElement selectedResult = Property(selectedChoice, "selectedResult");
        if (!PathEquals(StringProperty(selectedResult, "resultPath"), resultPath) ||
            !StringProperty(selectedResult, "revision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved map Result context selected a different HDF or revision.");

        JsonElement firstProfile = (await SubmitOperationAsync(client, "map.result.culvertProfile", new
        {
            map = MapRequest(visualizationRevision),
            contextToken,
            selectionOverrides,
            resultNode = resultLocator,
            selectionIndex = 0,
            profileIndex = 0,
        }, cancellation)).Result;
        int profileCount = IntProperty(firstProfile, "profileCount");
        if (!BooleanProperty(firstProfile, "geometryAvailable") ||
            !BooleanProperty(firstProfile, "outputAvailable") || profileCount <= 0)
            throw new InvalidDataException("The computed Result did not expose readable culvert geometry and stored HDF output.");
        JsonElement[] barrels = Property(firstProfile, "barrels").EnumerateArray().ToArray();
        if (barrels.Length != 1 || StringProperty(barrels[0], "id") != "barrel:source:0" ||
            IntProperty(barrels[0], "sourceOrdinal") != 0 ||
            StringProperty(barrels[0], "name") != CulvertFixtureBarrelName ||
            StringProperty(barrels[0], "barrelPropertyName") != CulvertFixtureBarrelPropertiesName ||
            StringProperty(barrels[0], "upstreamOpeningName") != CulvertFixtureUpstreamOpeningName ||
            StringProperty(barrels[0], "downstreamOpeningName") != CulvertFixtureDownstreamOpeningName)
            throw new InvalidDataException("Culvert profile geometry did not retain its source-ordinal barrel identity.");
        JsonElement[] barrelCoordinates = Property(barrels[0], "coordinates").EnumerateArray().ToArray();
        if (barrelCoordinates.Length != 2 ||
            TopologyMapNumberProperty(barrelCoordinates[0], "x") != 0.5 ||
            TopologyMapNumberProperty(barrelCoordinates[0], "y") != 1.5 ||
            TopologyMapNumberProperty(barrelCoordinates[1], "x") != 3.5 ||
            TopologyMapNumberProperty(barrelCoordinates[1], "y") != 1.5)
            throw new InvalidDataException("Culvert profile geometry changed the two source mesh-endpoint coordinates.");
        JsonElement[] baseOutputBlocks = Property(resultCatalog, "outputBlocks").EnumerateArray()
            .Where(block => StringProperty(block, "name") == "Base Output").ToArray();
        if (baseOutputBlocks.Length != 1)
            throw new InvalidDataException("The computed Result HDF must expose one Base Output block.");
        JsonElement baseOutput = baseOutputBlocks[0];
        if (IntProperty(baseOutput, "profileCount") != profileCount)
            throw new InvalidDataException("The culvert profile owner and Result HDF Base Output profile counts disagree.");
        string[] catalogTimes = Property(baseOutput, "times").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToArray();
        if (catalogTimes.Length != profileCount)
            throw new InvalidDataException("Result catalog Base Output times do not match the culvert profile count.");
        string[] times = catalogTimes;
        var profiles = new JsonElement[profileCount];
        ValidateCulvertProfile(firstProfile, 0, profileCount, resultPath, resultRevision);
        profiles[0] = firstProfile;
        for (int profileIndex = 1; profileIndex < profileCount; profileIndex++)
        {
            JsonElement profile = (await SubmitOperationAsync(client, "map.result.culvertProfile", new
            {
                map = MapRequest(visualizationRevision),
                contextToken,
                selectionOverrides,
                resultNode = resultLocator,
                selectionIndex = 0,
                profileIndex,
            }, cancellation)).Result;
            ValidateCulvertProfile(profile, profileIndex, profileCount, resultPath, resultRevision);
            profiles[profileIndex] = profile;
        }

        MapResultGeometryOwner resultOwner = new(resultLocator, contextToken,
            selectionOverrides, 0, resultPath, resultRevision);
        JsonElement flowSeries = (await SubmitOperationAsync(client, "map.result.culvertTimeSeries", new
        {
            map = MapRequest(visualizationRevision),
            resultOwner,
            barrelIDs = new[] { "barrel:source:0" },
            variable = "Flow",
        }, cancellation)).Result;
        JsonElement velocitySeries = (await SubmitOperationAsync(client, "map.result.culvertTimeSeries", new
        {
            map = MapRequest(visualizationRevision),
            resultOwner,
            barrelIDs = new[] { "barrel:source:0" },
            variable = "Velocity",
        }, cancellation)).Result;

        DefaultUnits siUnits = DefaultUnits.FromDefault(UnitSystem.SI);
        double[] flowValues = ValidateCulvertTimeSeries(flowSeries, resultPath, resultRevision,
            "Flow", siUnits.FlowUnits, times, profiles, "flow");
        double[] velocityValues = ValidateCulvertTimeSeries(velocitySeries, resultPath, resultRevision,
            "Velocity", siUnits.ShortVelocityUnits, times, profiles, "outletVelocity");
        bool nonZeroFlow = flowValues.Any(value => Math.Abs(value) > 1e-8);
        if (!nonZeroFlow)
            throw new InvalidDataException("The CPU-generated culvert Flow series contains no nonzero hydraulic transfer.");

        string finalVisualizationRevision = FileSha256(visualizationPath);
        if (!finalVisualizationRevision.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSha256(resultPath).Equals(resultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string manifestPath = Path.Combine(outputDirectory, "culvert-result-http-e2e-manifest.json");
        var report = new CulvertResultAcceptanceReport(
            State: "completed",
            ManifestPath: manifestPath,
            ProjectPath: projectPath,
            PlanPath: fixture.PlanPath,
            GeometryPath: fixture.GeometryPath,
            ResultPath: resultPath,
            ResultRevision: resultRevision,
            VisualizationPath: visualizationPath,
            VisualizationRevision: visualizationRevision,
            SourceBarrelCount: 1,
            ProfileCount: profileCount,
            CpuSolveCompleted: true,
            ResultHdfCulvertOutputVerified: true,
            CulvertProfilesReadBack: true,
            FlowTimeSeriesVerified: true,
            OutletVelocityTimeSeriesVerified: true,
            NonZeroFlowObserved: nonZeroFlow,
            Times: times,
            FlowUnits: siUnits.FlowUnits,
            OutletVelocityUnits: siUnits.ShortVelocityUnits,
            FlowValues: flowValues,
            OutletVelocityValues: velocityValues);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    private static void ConfigureCulvertFixture(FixtureManifest fixture)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(fixture.ProjectPath);
        try
        {
            Plan plan = project.Plans.Single(item => string.Equals(item.Name,
                CulvertFixturePlanName, StringComparison.Ordinal));
            Geometry geometry = plan.PlanAssociation.Geometry
                ?? throw new InvalidDataException("The CPU Plan has no associated Geometry.");
            if (!string.Equals(geometry.Name, CulvertFixtureGeometryName, StringComparison.Ordinal) ||
                !PathEquals(geometry.Filename, fixture.GeometryPath) ||
                project.Geometries.Count(item => PathEquals(item.Filename, fixture.GeometryPath)) != 1)
                throw new InvalidDataException("The CPU Plan must resolve uniquely to its generated Solver Geometry source.");
            Mesh mesh = geometry.Mesh
                ?? throw new InvalidDataException("The generated Solver Geometry has no source mesh.");
            Point upstreamPoint = new(0.5, 1.5);
            Point downstreamPoint = new(3.5, 1.5);
            int[] endpointCells = mesh.FindCells(new[] { upstreamPoint, downstreamPoint });
            if (endpointCells.Length != 2 || endpointCells[0] < 0 || endpointCells[1] < 0 ||
                endpointCells[0] == endpointCells[1])
                throw new InvalidDataException("The culvert endpoints must resolve to two distinct source mesh cells.");
            if (geometry.CulvertBarrelLayer.Count != 0 || geometry.BarrelPropertiesLayer.Count != 0 ||
                geometry.OpeningPropertiesLayer.Count != 0)
                throw new InvalidDataException("The generated Solver Geometry unexpectedly contains preexisting culvert data.");

            geometry.BarrelPropertiesLayer.Add(new BarrelProperties
            {
                Name = CulvertFixtureBarrelPropertiesName,
                Shape = BarrelShape.Circle,
                Rise = 0.35,
                Span = 0.35,
                Mannings = 0.025,
            });
            geometry.OpeningPropertiesLayer.Add(new OpeningProperties
            {
                Name = CulvertFixtureUpstreamOpeningName,
                KIn = 0.5,
                KOut = 1.0,
                OpeningType = BarrelOpeningType.ConcretePipeCulvert_SquareEdgeWithHeadwall,
            });
            geometry.OpeningPropertiesLayer.Add(new OpeningProperties
            {
                Name = CulvertFixtureDownstreamOpeningName,
                KIn = 0.5,
                KOut = 1.0,
                OpeningType = BarrelOpeningType.ConcretePipeCulvert_SquareEdgeWithHeadwall,
            });
            geometry.CulvertBarrelLayer.Add(new CulvertBarrel
            {
                Name = CulvertFixtureBarrelName,
                Polyline = new Polyline(new[] { upstreamPoint, downstreamPoint }),
                BarrelPropertyName = CulvertFixtureBarrelPropertiesName,
                UpstreamInvert = fixture.TerrainElevation,
                DownstreamInvert = fixture.TerrainElevation,
                UpstreamOpeningName = CulvertFixtureUpstreamOpeningName,
                DownstreamOpeningName = CulvertFixtureDownstreamOpeningName,
            });
            geometry.Save();

            BoundaryCondition boundary = project.BoundaryConditions.Single();
            InitialStageLayer stage = boundary.InitialConditions.Stage;
            stage.IsEnabled = true;
            stage.SpatialDataType = SpatialDataType.Point;
            // Point-boundary metadata defaults RasterDefinition to the zero-valued
            // struct. Its IsNull() check only recognizes NaN CellSize, so a saved
            // zero definition is treated as present and attempts to build a
            // TimeSeriesRasterizer with no valid raster. Point gauges do not need
            // a pre-rasterized grid here, so persist the source's explicit null
            // raster sentinel and let the solver sample the gauge resampler.
            stage.Raster = RasterDefinition.NullRaster;
            stage.Gauges.Clear();
            DateTime[] gaugeTimes = [boundary.StartTime, boundary.EndTime];
            if (gaugeTimes[1] <= gaugeTimes[0])
                throw new InvalidDataException("The generated Boundary Condition has an invalid stage-gauge time range.");
            AddStageGauge(boundary, stage, "Culvert Head West South", new Point(0.5, 0.5), 44.5, gaugeTimes);
            AddStageGauge(boundary, stage, "Culvert Head East South", new Point(3.5, 0.5), 43.0, gaugeTimes);
            AddStageGauge(boundary, stage, "Culvert Head West Center", new Point(0.5, 1.5), 44.5, gaugeTimes);
            AddStageGauge(boundary, stage, "Culvert Head East Center", new Point(3.5, 1.5), 43.0, gaugeTimes);
            AddStageGauge(boundary, stage, "Culvert Head West North", new Point(0.5, 3.5), 44.5, gaugeTimes);
            AddStageGauge(boundary, stage, "Culvert Head East North", new Point(3.5, 3.5), 43.0, gaugeTimes);
            boundary.Save();
        }
        finally
        {
            DisposeCulvertFixtureProject(project);
        }

        Project reopened = new(fixture.ProjectPath);
        try
        {
            InitialStageLayer reopenedStage = reopened.BoundaryConditions.Single().InitialConditions.Stage;
            if (reopenedStage.SpatialDataType != SpatialDataType.Point || !reopenedStage.Raster.IsNull())
                throw new InvalidDataException("The reopened point stage did not retain the explicit null-raster sentinel.");
        }
        finally
        {
            DisposeCulvertFixtureProject(reopened);
        }
    }

    private static async Task<JsonElement> ComputeCulvertPlanAsync(HttpClient client, object model,
        CancellationToken cancellation)
    {
        JsonElement inspection = (await SubmitOperationAsync(client, "plan.compute.inspect",
            new { model }, cancellation)).Result;
        if (!BooleanProperty(inspection, "canExecute"))
            throw new InvalidOperationException("真实 CPU 计划未通过 compute 检查：" +
                JsonSerializer.Serialize(PropertyOrNull(inspection, "messages"), JsonOptions));

        JsonElement result = (await SubmitOperationAsync(client, "plan.compute.execute", new
        {
            model,
            inspectionId = StringProperty(inspection, "inspectionId"),
        }, cancellation)).Result;
        if (!string.Equals(StringProperty(result, "state"), "completed", StringComparison.OrdinalIgnoreCase) ||
            !BooleanProperty(Property(result, "summary"), "success"))
            throw new InvalidOperationException("真实 CPU 计划求解未成功；fullcompute.error=" +
                JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static void AddStageGauge(BoundaryCondition boundary, InitialStageLayer stage,
        string name, Point point, double waterSurface, DateTime[] times)
    {
        var station = new MeteorologicStation(name, point);
        boundary.MetStations.Add(station);
        stage.Gauges.Add(MetGaugedData.FromTableData(station,
            TimeSeriesDefinition.Irregular(times), [waterSurface, waterSurface]));
    }

    private static void DisposeCulvertFixtureProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.BoundaryConditions)
                     .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
    }

    private static CatalogTreeNode FindExactResultNode(IEnumerable<CatalogTreeNode> roots, string resultPath)
    {
        CatalogTreeNode[] matches = FlattenMapNodes(roots).Where(node =>
            node.Locator?.NodeType == "Result" && PathEquals(node.Locator.AssetID, resultPath)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("The saved map must resolve exactly one direct Result row for the computed HDF.");
        return matches[0];
    }

    private static void ValidateCulvertProfile(JsonElement profile, int expectedProfileIndex,
        int expectedProfileCount, string resultPath, string resultRevision)
    {
        if (IntProperty(profile, "profileIndex") != expectedProfileIndex ||
            IntProperty(profile, "profileCount") != expectedProfileCount ||
            !BooleanProperty(profile, "geometryAvailable") || !BooleanProperty(profile, "outputAvailable") ||
            !PathEquals(StringProperty(profile, "selectedResultPath"), resultPath) ||
            !StringProperty(profile, "selectedResultRevision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase) ||
            Property(profile, "barrels").GetArrayLength() != 1 ||
            StringProperty(Property(profile, "barrels").EnumerateArray().First(), "id") != "barrel:source:0" ||
            IntProperty(Property(profile, "barrels").EnumerateArray().First(), "sourceOrdinal") != 0)
            throw new InvalidDataException("A stored culvert profile did not resolve the exact source barrel and profile index.");
        double[] flow = Property(profile, "flow").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        double[] outsideDepth = Property(profile, "outsideDepth").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        double[] velocity = Property(profile, "outletVelocity").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        if (flow.Length != 1 || outsideDepth.Length != 2 || velocity.Length != 1 ||
            flow.Any(value => !double.IsFinite(value)) || outsideDepth.Any(value => !double.IsFinite(value)) ||
            velocity.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("A stored culvert profile has invalid or misaligned source arrays.");
    }

    private static double[] ValidateCulvertTimeSeries(JsonElement response, string resultPath,
        string resultRevision, string variable, string expectedUnits, string[] expectedTimes,
        JsonElement[] profiles, string profileArrayName)
    {
        if (StringProperty(response, "variable") != variable ||
            !PathEquals(StringProperty(response, "selectedResultPath"), resultPath) ||
            !StringProperty(response, "selectedResultRevision").Equals(resultRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(StringProperty(response, "units"), expectedUnits, StringComparison.Ordinal))
            throw new InvalidDataException($"The {variable} time series lost its exact Result HDF identity or source units.");
        string[] times = Property(response, "times").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToArray();
        if (times.Length != expectedTimes.Length || times.Where((time, index) =>
                !EquivalentCulvertTime(time, expectedTimes[index])).Any())
            throw new InvalidDataException($"The {variable} time series changed the source Result time axis.");
        JsonElement[] datasets = Property(response, "datasets").EnumerateArray().ToArray();
        if (datasets.Length != 1 || StringProperty(datasets[0], "id") != "barrel:source:0" ||
            IntProperty(datasets[0], "sourceOrdinal") != 0 ||
            StringProperty(datasets[0], "name") != CulvertFixtureBarrelName)
            throw new InvalidDataException($"The {variable} time series did not retain source-ordinal barrel identity.");
        double[] values = Property(datasets[0], "values").EnumerateArray()
            .Select(item => item.GetDouble()).ToArray();
        if (values.Length != expectedTimes.Length || values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException($"The {variable} time series contains invalid or misaligned values.");

        for (int index = 0; index < values.Length; index++)
        {
            double[] profileValues = Property(profiles[index], profileArrayName).EnumerateArray()
                .Select(item => item.GetDouble()).ToArray();
            if (profileValues.Length != 1 || Math.Abs(profileValues[0] - values[index]) > 1e-9)
                throw new InvalidDataException($"The {variable} time series value at profile {index} differs from the stored HDF profile readback.");
        }
        return values;
    }

    private static bool EquivalentCulvertTime(string left, string right) =>
        DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
            out DateTimeOffset leftTime) &&
        DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
            out DateTimeOffset rightTime) && leftTime.UtcDateTime.Ticks == rightTime.UtcDateTime.Ticks;
}
