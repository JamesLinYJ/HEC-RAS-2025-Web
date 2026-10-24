using System.Globalization;
using System.Text;
using System.Text.Json;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record TerrainPolygonMapAcceptanceReport(
    string State,
    string FixtureDirectory,
    string ManifestPath,
    string ProjectPath,
    string TerrainPath,
    string TerrainInputRasterPath,
    string InputNodeID,
    string InputID,
    int InputIndex,
    string SourceRevision,
    string SavedRevision,
    int InitialFeatureCount,
    int DraftFeatureCount,
    int SavedFeatureCount,
    bool RenderAndTopologyFeatureIDsMatch,
    bool TriangleStampReadback,
    bool RectangleStampReadback,
    bool CustomDrawReadback,
    bool SelectAtReadback,
    bool RemoveUndoRedoReadback,
    bool StaleSourceAndMapContextRejected,
    bool SourceHdfUnchangedBeforeParentSave,
    bool ParentSaveChangedSourceHdf,
    bool InputNamePreserved,
    bool SavedMapReadback);

/// <summary>
/// Exercises the retained source polygon Terrain input through the real map
/// renderer, topology editor and parent HDF save over loopback HTTP. Every
/// mutation is confined to a newly generated fixture directory.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string PolygonTerrainSourceClass = "PolygonRasterModTerrainInputVisualContext";
    private const string SelectedTerrainInputField = "TerrainLayersEditor/SelectedInputWithMap";
    private static readonly TerrainPolygonEditSettings DefaultStampSettings =
        new("Triangle", 2, 2, 0, 20, "SetValue");

    public static async Task<TerrainPolygonMapAcceptanceReport> RunTerrainPolygonMapAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Terrain polygon acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Terrain polygon acceptance requires a new output directory; existing files are never overwritten.");
        Directory.CreateDirectory(outputDirectory);
        string fixtureDirectory = Path.Combine(outputDirectory, "project-fixture");
        FixtureManifest fixture = PlanComputeFixture.CreateMap(fixtureDirectory);
        const string inputName = "地形多边形修改";
        AddPolygonTerrainInput(fixture, inputName);

        string mapFixturePath = Path.Combine(outputDirectory, "map-fixture.json");
        string manifestPath = Path.Combine(outputDirectory, "terrain-polygon-map-manifest.json");
        await File.WriteAllTextAsync(mapFixturePath, JsonSerializer.Serialize(fixture, JsonOptions));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = fixture.ProjectPath }, cancellation)).Result;
        string projectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        string projectXml = StringProperty(opened, "projectXml");
        string projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");

        string mapID = Guid.NewGuid().ToString("D");
        JsonElement createdMap = (await SubmitOperationAsync(client, "map.document.create", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedVisualizationRevision = (string?)null,
            kind = "Project",
            name = "地形多边形编辑验收"
        }, cancellation)).Result;
        int mapIndex = IntProperty(createdMap, "mapIndex");
        string mapName = StringProperty(createdMap, "mapName");
        string mapType = StringProperty(createdMap, "mapType");
        string visualizationRevision = StringProperty(createdMap, "visualizationRevision");
        if (mapIndex != 0 || mapType != "Project" || mapName != "地形多边形编辑验收" ||
            !FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Creating the isolated Terrain map did not persist its exact .vis revision.");

        object MapRequest(string? expectedVis = null) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            mapID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision = expectedVis ?? visualizationRevision,
            projectSessionID = mapID,
        };

        JsonElement initialTree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(), cancellation)).Result;
        JsonElement polygonInputNode = FindPolygonTerrainInput(Property(initialTree, "roots"), inputName);
        string inputNodeID = StringProperty(polygonInputNode, "id");
        string sourceClass = StringProperty(polygonInputNode, "sourceClass");
        string terrainPath = Path.GetFullPath(StringProperty(polygonInputNode, "assetID"));
        if (sourceClass != PolygonTerrainSourceClass || !File.Exists(terrainPath))
            throw new InvalidDataException("The saved map tree did not resolve the exact source Polygon Terrain HDF.");

        string sourceRevision = FileSHA256(terrainPath);
        string terrainInputRasterSHA = FileSHA256(fixture.TerrainPath);
        JsonElement modelSnapshot = (await SubmitOperationAsync(client, "model.load", new
        {
            assetPath = terrainPath,
            kind = "Terrain",
            projectPath,
            projectXml
        }, cancellation)).Result;
        JsonElement[] terrainInputs = Property(Property(modelSnapshot, "terrain"), "inputs")
            .EnumerateArray().ToArray();
        JsonElement selectedInputRow = terrainInputs.Single(row =>
            StringProperty(row, "name") == inputName && StringProperty(row, "type") == "Polygon");
        string inputID = StringProperty(selectedInputRow, "id");
        int inputIndex = IntProperty(selectedInputRow, "sourceVectorIndex");
        if (inputIndex != 0 || TerrainInputSourceVectorIndex(polygonInputNode) != inputIndex)
            throw new InvalidDataException("The isolated fixture must expose its only source vector input at index zero.");

        object MapContext(string? terrainRevision = null, string? expectedVis = null) => new
        {
            map = MapRequest(expectedVis),
            inputNodeID,
            expectedSourceClass = sourceClass,
            expectedTerrainPath = terrainPath,
            expectedInputIndex = inputIndex,
            expectedTerrainRevision = terrainRevision ?? sourceRevision
        };

        JsonElement rendered = (await SubmitOperationAsync(client, "map.terrainInput.render", new
        {
            map = MapRequest(),
            inputNodeID,
            expectedSourceClass = sourceClass,
            expectedTerrainPath = terrainPath,
            expectedInputIndex = inputIndex,
            expectedTerrainRevision = sourceRevision,
        }, cancellation)).Result;
        JsonElement[] renderedFeatures = Property(rendered, "features").EnumerateArray().ToArray();
        if (StringProperty(rendered, "terrainRevision") != sourceRevision ||
            StringProperty(rendered, "terrainPath") != terrainPath ||
            StringProperty(rendered, "sourceClass") != sourceClass ||
            IntProperty(rendered, "inputIndex") != inputIndex || renderedFeatures.Length != 2)
            throw new InvalidDataException("The real Terrain input renderer returned a different source capture or baseline polygon count.");

        string[] renderFeatureIDs = renderedFeatures.Select(feature => StringProperty(feature, "id")).ToArray();
        JsonElement currentSnapshot = modelSnapshot;
        JsonElement currentTopology = default;
        async Task<JsonElement> PolygonCommandAsync(string operation, string[]? featureIDs = null,
            TerrainMapCoordinate[]? points = null, double? mapCellSize = null,
            TerrainPolygonEditSettings? settings = null,
            string? expectedDraftIdentity = null, string? terrainCapture = null,
            string? mapCapture = null)
        {
            JsonElement? previousTopology = currentTopology.ValueKind == JsonValueKind.Undefined
                ? null : currentTopology;
            object request = new
            {
                model = TerrainModel(currentSnapshot, inputID, terrainPath, projectPath, projectXml),
                inputId = inputID,
                operation,
                featureIDs,
                points,
                mapCellSize,
                expectedDraftIdentity = expectedDraftIdentity ?? (operation == "load"
                    ? null : StringProperty(previousTopology!.Value, "draftIdentity")),
                mapContext = MapContext(terrainCapture, mapCapture),
                polygonSettings = settings
            };
            JsonElement result = (await SubmitOperationAsync(client, "model.terrainTopology", request,
                cancellation)).Result;
            currentSnapshot = Property(result, "snapshot");
            currentTopology = Property(result, "topology");
            return result;
        }

        JsonElement loaded = await PolygonCommandAsync("load");
        JsonElement initialTopology = Property(loaded, "topology");
        string[] topologyInitialIDs = TopologyFeatureIDs(initialTopology);
        bool featureIDsMatch = renderFeatureIDs.SequenceEqual(topologyInitialIDs, StringComparer.Ordinal);
        if (!featureIDsMatch || topologyInitialIDs.Length != 2 ||
            !topologyInitialIDs.SequenceEqual(new[] { inputNodeID + ":feature:0", inputNodeID + ":feature:1" },
                StringComparer.Ordinal))
            throw new InvalidDataException("The map renderer and topology editor did not expose the same exact baseline polygon FIDs.");

        JsonElement initialTriangleFeature = Property(initialTopology, "features").EnumerateArray().First();
        TerrainMapCoordinate[] initialTriangle = ReadPoints(Property(initialTriangleFeature, "points"));
        if (initialTriangle.Length < 3)
            throw new InvalidDataException("The generated source Triangle baseline has invalid map coordinates.");

        JsonElement triangleStamped = await PolygonCommandAsync("stamp", points: [new(12, 0)],
            mapCellSize: 0.5, settings: DefaultStampSettings);
        string[] afterTriangleIDs = TopologyFeatureIDs(Property(triangleStamped, "topology"));
        JsonElement triangleTopology = Property(triangleStamped, "topology");
        JsonElement triangleFeature = Property(triangleTopology, "features").EnumerateArray().Last();
        bool triangleStampReadback = afterTriangleIDs.Length == 3 &&
            Property(triangleTopology, "selectedFeatureIDs").EnumerateArray()
                .Select(item => item.GetString()).SequenceEqual(new[] { afterTriangleIDs[^1] }, StringComparer.Ordinal) &&
            Property(triangleFeature, "points").GetArrayLength() == 3 &&
            MetadataValue(triangleFeature, "ModificationType") == "SetValue" &&
            MetadataValue(triangleFeature, "Value") == "20";

        TerrainPolygonEditSettings rectangleSettings = new("Rectangle", 3, 2, 15, 30, "AddValue");
        JsonElement rectangleStamped = await PolygonCommandAsync("stamp", points: [new(22, 0)],
            mapCellSize: 0.5, settings: rectangleSettings);
        string[] afterRectangleIDs = TopologyFeatureIDs(Property(rectangleStamped, "topology"));
        JsonElement rectangleTopology = Property(rectangleStamped, "topology");
        JsonElement rectangleFeature = Property(rectangleTopology, "features").EnumerateArray().Last();
        bool rectangleStampReadback = afterRectangleIDs.Length == 4 &&
            afterRectangleIDs[^1].EndsWith(":feature:3", StringComparison.Ordinal) &&
            Property(rectangleFeature, "points").GetArrayLength() == 4 &&
            MetadataValue(rectangleFeature, "ModificationType") == "AddValue" &&
            MetadataValue(rectangleFeature, "Value") == "30";

        TerrainPolygonEditSettings customSettings = new("Custom", 1, 1, 0, 40, "SetValue");
        TerrainMapCoordinate[] customShape = [new(32, 0), new(35, 0), new(35, 3), new(32, 3)];
        JsonElement customDrawn = await PolygonCommandAsync("draw", points: customShape,
            settings: customSettings);
        JsonElement customTopology = Property(customDrawn, "topology");
        string[] afterDrawIDs = TopologyFeatureIDs(customTopology);
        bool customDrawReadback = afterDrawIDs.Length == 5 &&
            afterDrawIDs[^1].EndsWith(":feature:4", StringComparison.Ordinal) &&
            HasPointSet(Property(customTopology, "features").EnumerateArray().Single(feature =>
                StringProperty(feature, "id") == afterDrawIDs[^1]), customShape);
        if (!triangleStampReadback || !rectangleStampReadback || !customDrawReadback)
            throw new InvalidDataException("Triangle, Rectangle, or custom Draw did not return the expected source-backed polygon features.");

        JsonElement selectedAt = await PolygonCommandAsync("selectAt", points: [new(33.5, 1.5)], mapCellSize: 0.1);
        JsonElement selectAtTopology = Property(selectedAt, "topology");
        string[] selectedAtIDs = Property(selectAtTopology, "selectedFeatureIDs").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        string customID = afterDrawIDs[^1];
        bool selectAtReadback = selectedAtIDs.SequenceEqual(new[] { customID }, StringComparer.Ordinal);
        if (!selectAtReadback)
            throw new InvalidDataException("Map-space selectAt did not select the exact custom polygon rendered at the known interior point.");

        string baselineTriangleID = topologyInitialIDs[0];
        JsonElement selected = await PolygonCommandAsync("select", featureIDs: [baselineTriangleID]);
        JsonElement selectTopology = Property(selected, "topology");
        bool selectedFeatureReadback = Property(selectTopology, "selectedFeatureIDs").EnumerateArray()
            .Select(item => item.GetString()).SequenceEqual(new[] { baselineTriangleID }, StringComparer.Ordinal);
        if (!selectedFeatureReadback)
            throw new InvalidDataException("Explicit polygon selection did not round-trip the chosen baseline FID.");

        JsonElement removed = await PolygonCommandAsync("remove", featureIDs: [baselineTriangleID]);
        bool removedReadback = TopologyFeatureIDs(Property(removed, "topology")).Length == 4;
        JsonElement undoneRemoval = await PolygonCommandAsync("undo");
        bool undoReadback = TopologyFeatureIDs(Property(undoneRemoval, "topology"))
            .SequenceEqual(afterDrawIDs, StringComparer.Ordinal);
        JsonElement redoneRemoval = await PolygonCommandAsync("redo");
        bool redoReadback = TopologyFeatureIDs(Property(redoneRemoval, "topology")).Length == 4;
        JsonElement restoredBaseline = await PolygonCommandAsync("undo");
        string[] finalDraftIDs = TopologyFeatureIDs(Property(restoredBaseline, "topology"));
        bool finalUndoReadback = finalDraftIDs.SequenceEqual(afterDrawIDs, StringComparer.Ordinal);
        bool removeUndoRedoReadback = removedReadback && undoReadback && redoReadback && finalUndoReadback &&
            BooleanProperty(Property(restoredBaseline, "topology"), "canUndo") &&
            BooleanProperty(Property(restoredBaseline, "topology"), "canRedo");
        if (!removeUndoRedoReadback)
            throw new InvalidDataException("Polygon remove/undo/redo did not restore the expected feature identities and history state.");

        bool staleSourceRejected = await OperationRejectedAsync(client, "model.terrainTopology",
            TerrainTopologyRequest(currentSnapshot, inputID, terrainPath, projectPath, projectXml,
                Property(restoredBaseline, "topology"), MapContext(new string('0', 64)), "draw",
                points: customShape, settings: customSettings), cancellation);
        bool staleMapRevisionRejected = await OperationRejectedAsync(client, "model.terrainTopology",
            TerrainTopologyRequest(currentSnapshot, inputID, terrainPath, projectPath, projectXml,
                Property(restoredBaseline, "topology"), MapContext(sourceRevision, new string('0', 64)), "draw",
                points: customShape, settings: customSettings), cancellation);
        bool staleSourceAndMapContextRejected = staleSourceRejected && staleMapRevisionRejected;
        if (!staleSourceAndMapContextRejected)
            throw new InvalidDataException("A polygon draft accepted a stale Terrain source revision or stale map visualization identity.");

        bool sourceHdfUnchangedBeforeParentSave = FileSHA256(terrainPath) == sourceRevision &&
            FileSHA256(fixture.TerrainPath) == terrainInputRasterSHA;
        if (!sourceHdfUnchangedBeforeParentSave)
            throw new InvalidDataException("Polygon map drafts modified the Terrain HDF or source input raster before the parent model save.");

        JsonElement saved = (await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = terrainPath,
            kind = "Terrain",
            projectPath,
            revision = sourceRevision,
            fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SelectedTerrainInputField] = inputID
            },
            images = Property(currentSnapshot, "images"),
            projectXml,
            sourceState = OptionalString(currentSnapshot, "sourceState")
        }, cancellation)).Result;
        string savedRevision = StringProperty(saved, "revision");
        bool parentSaveChangedSourceHdf = !FileSHA256(terrainPath).Equals(sourceRevision,
            StringComparison.OrdinalIgnoreCase) && FileSHA256(terrainPath).Equals(savedRevision,
            StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedSourceHdf)
            throw new InvalidDataException("The parent Terrain save did not publish the polygon draft to the selected HDF.");

        JsonElement reopenedProject = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        string reopenedXml = StringProperty(reopenedProject, "projectXml");
        string reopenedProjectRevision = StringProperty(reopenedProject, "revision");
        JsonElement reopenedModel = (await SubmitOperationAsync(client, "model.load", new
        {
            assetPath = terrainPath,
            kind = "Terrain",
            projectPath,
            projectXml = reopenedXml
        }, cancellation)).Result;
        JsonElement reopenedInputs = Property(Property(reopenedModel, "terrain"), "inputs");
        JsonElement reopenedInput = reopenedInputs.EnumerateArray().Single(row =>
            StringProperty(row, "name") == inputName && StringProperty(row, "type") == "Polygon");
        string reopenedInputID = StringProperty(reopenedInput, "id");
        int reopenedInputIndex = IntProperty(reopenedInput, "sourceVectorIndex");
        bool inputNamePreserved = reopenedInputIndex == inputIndex && StringProperty(reopenedInput, "name") == inputName;
        if (!inputNamePreserved)
            throw new InvalidDataException("The source Polygon Terrain input name or its vector-source index changed after save/reopen.");

        JsonElement reopenedTree = (await SubmitOperationAsync(client, "map.tree.load", new
        {
            projectPath,
            projectXml = reopenedXml,
            projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(reopenedXml)),
            mapID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision = visualizationRevision,
            projectSessionID = mapID
        }, cancellation)).Result;
        JsonElement reopenedPolygonNode = FindPolygonTerrainInput(Property(reopenedTree, "roots"), inputName);
        string reopenedInputNodeID = StringProperty(reopenedPolygonNode, "id");
        if (TerrainInputSourceVectorIndex(reopenedPolygonNode) != reopenedInputIndex ||
            reopenedInputNodeID != inputNodeID)
            throw new InvalidDataException("The saved map tree changed the exact Terrain input map identity after parent save/reopen.");
        string reopenedSourceRevision = FileSHA256(terrainPath);
        JsonElement reopenedRender = (await SubmitOperationAsync(client, "map.terrainInput.render", new
        {
            map = new
            {
                projectPath,
                projectXml = reopenedXml,
                projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(reopenedXml)),
                mapID,
                mapIndex,
                mapName,
                mapType,
                expectedVisualizationRevision = visualizationRevision,
                projectSessionID = mapID
            },
            inputNodeID = reopenedInputNodeID,
            expectedSourceClass = PolygonTerrainSourceClass,
            expectedTerrainPath = terrainPath,
            expectedInputIndex = inputIndex,
            expectedTerrainRevision = reopenedSourceRevision
        }, cancellation)).Result;

        object ReopenedMapContext() => new
        {
            map = new
            {
                projectPath,
                projectXml = reopenedXml,
                projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(reopenedXml)),
                mapID,
                mapIndex,
                mapName,
                mapType,
                expectedVisualizationRevision = visualizationRevision,
                projectSessionID = mapID
            },
            inputNodeID = reopenedInputNodeID,
            expectedSourceClass = PolygonTerrainSourceClass,
            expectedTerrainPath = terrainPath,
            expectedInputIndex = inputIndex,
            expectedTerrainRevision = reopenedSourceRevision
        };
        JsonElement reopenedTopology = (await SubmitOperationAsync(client, "model.terrainTopology", new
        {
            model = TerrainModel(reopenedModel, reopenedInputID, terrainPath, projectPath, reopenedXml),
            inputId = reopenedInputID,
            operation = "load",
            mapContext = ReopenedMapContext()
        }, cancellation)).Result;
        JsonElement reopenedTopologyData = Property(reopenedTopology, "topology");
        string[] persistedIDs = TopologyFeatureIDs(reopenedTopologyData);
        string[] persistedRenderIDs = Property(reopenedRender, "features").EnumerateArray()
            .Select(feature => StringProperty(feature, "id")).ToArray();
        bool savedMapReadback = persistedIDs.Length == 5 && persistedIDs.SequenceEqual(persistedRenderIDs,
            StringComparer.Ordinal) && persistedIDs.SequenceEqual(finalDraftIDs, StringComparer.Ordinal) &&
            StringProperty(reopenedTopologyData, "sourceRevision") == reopenedSourceRevision &&
            StringProperty(reopenedProject, "projectPath") == projectPath;
        if (!savedMapReadback)
            throw new InvalidDataException("Reopening the Project and Terrain did not recover the exact five source-backed polygon features through both map and topology readers.");

        var manifest = new
        {
            schema = "hecras-terrain-polygon-map-e2e/v1",
            mapFixturePath,
            projectPath,
            projectXml = reopenedXml,
            projectRevision = reopenedProjectRevision,
            projectXmlSHA256 = Sha256(Encoding.UTF8.GetBytes(reopenedXml)),
            map = new
            {
                mapID,
                projectSessionID = mapID,
                mapIndex,
                mapName,
                mapType,
                visualizationRevision,
                mapRequest = new
                {
                    projectPath,
                    projectXml = reopenedXml,
                    projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(reopenedXml)),
                    mapID,
                    mapIndex,
                    mapName,
                    mapType,
                    expectedVisualizationRevision = visualizationRevision,
                    projectSessionID = mapID
                }
            },
            terrain = new
            {
                path = terrainPath,
                inputRasterPath = fixture.TerrainPath,
                inputRasterSHA256 = terrainInputRasterSHA,
                sourceClass = PolygonTerrainSourceClass,
                inputNodeID,
                inputID,
                inputIndex,
                name = inputName,
                sourceRevision,
                savedRevision,
                reopenedInputID,
                reopenedSourceRevision,
                initialFeatureIDs = topologyInitialIDs,
                finalFeatureIDs = finalDraftIDs,
                persistedFeatureIDs = persistedIDs,
                renderFeatureIDs = persistedRenderIDs
            },
            editSequence = new object[]
            {
                new { operation = "stamp", settings = DefaultStampSettings, anchor = new { x = 12, y = 0 } },
                new { operation = "stamp", settings = rectangleSettings, anchor = new { x = 22, y = 0 } },
                new { operation = "draw", settings = customSettings, points = customShape },
                new { operation = "selectAt", point = new { x = 33.5, y = 1.5 }, mapCellSize = 0.1 },
                new { operation = "select", featureIDs = new[] { baselineTriangleID } },
                new { operation = "remove", featureIDs = new[] { baselineTriangleID } },
                new { operation = "undo" }, new { operation = "redo" }, new { operation = "undo" }
            },
            sourceHdfSHA256BeforeParentSave = sourceRevision,
            sourceHdfSHA256AfterParentSave = FileSHA256(terrainPath),
            render = new
            {
                sourceRevision,
                inputIndex,
                sourceClass,
                features = renderedFeatures
            },
            checks = new
            {
                featureIDsMatch,
                triangleStampReadback,
                rectangleStampReadback,
                customDrawReadback,
                selectAtReadback,
                removeUndoRedoReadback,
                staleSourceAndMapContextRejected,
                sourceHdfUnchangedBeforeParentSave,
                parentSaveChangedSourceHdf,
                inputNamePreserved,
                savedMapReadback
            }
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);

        _ = await SubmitOperationAsync(client, "backend.info", new { }, cancellation);
        return new TerrainPolygonMapAcceptanceReport("passed", fixtureDirectory, manifestPath,
            projectPath, terrainPath, fixture.TerrainPath, inputNodeID, inputID, inputIndex,
            sourceRevision, savedRevision, topologyInitialIDs.Length, finalDraftIDs.Length,
            persistedIDs.Length, featureIDsMatch, triangleStampReadback, rectangleStampReadback,
            customDrawReadback, selectAtReadback, removeUndoRedoReadback, staleSourceAndMapContextRejected,
            sourceHdfUnchangedBeforeParentSave, parentSaveChangedSourceHdf, inputNamePreserved, savedMapReadback);
    }

    private static object TerrainModel(JsonElement snapshot, string inputID, string terrainPath,
        string projectPath, string projectXml) => new
    {
        assetPath = terrainPath,
        kind = "Terrain",
        projectPath,
        revision = StringProperty(snapshot, "revision"),
        fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SelectedTerrainInputField] = inputID
        },
        images = Property(snapshot, "images"),
        projectXml,
        sourceState = OptionalString(snapshot, "sourceState")
    };

    private static object TerrainTopologyRequest(JsonElement snapshot, string inputID, string terrainPath,
        string projectPath, string projectXml, JsonElement topology, object mapContext, string operation,
        TerrainMapCoordinate[]? points = null, TerrainPolygonEditSettings? settings = null) => new
    {
        model = TerrainModel(snapshot, inputID, terrainPath, projectPath, projectXml),
        inputId = inputID,
        operation,
        points,
        expectedDraftIdentity = StringProperty(topology, "draftIdentity"),
        mapContext,
        polygonSettings = settings
    };

    private static void AddPolygonTerrainInput(FixtureManifest fixture, string inputName)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(fixture.ProjectPath);
        try
        {
            Terrain terrain = project.Terrains.Single();
            var polygons = new PolygonRasterModTerrainInput(terrain, inputName);
            polygons.Add(new PolygonRasterMod(new Polygon(new Point[]
            {
                new Point(-2, -2), new Point(2, -2), new Point(0, 2)
            }), RasterModType.SetValue, 10));
            polygons.Add(new PolygonRasterMod(new Polygon(new Point[]
            {
                new Point(5, -2), new Point(9, -2), new Point(9, 2), new Point(5, 2)
            }), RasterModType.TakeHigher, 3));
            terrain.Add(polygons);
            terrain.Save();
            project.Save();
        }
        finally
        {
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                         .Concat(project.Geometries).Concat(project.BoundaryConditions)
                         .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
                layer.Dispose();
        }
    }

    private static JsonElement FindPolygonTerrainInput(JsonElement roots, string expectedName)
    {
        var matches = new List<JsonElement>();
        void Visit(JsonElement rows)
        {
            if (rows.ValueKind != JsonValueKind.Array) return;
            foreach (JsonElement node in rows.EnumerateArray())
            {
                if (OptionalString(node, "sourceClass") == PolygonTerrainSourceClass &&
                    OptionalString(node, "name") == expectedName)
                    matches.Add(node);
                Visit(PropertyOrNull(node, "children"));
            }
        }
        Visit(roots);
        return matches.Count == 1 ? matches[0] : throw new InvalidDataException(
            $"Expected one exact polygon Terrain input named '{expectedName}', found {matches.Count}.");
    }

    private static string[] TopologyFeatureIDs(JsonElement topology) =>
        Property(topology, "features").EnumerateArray()
            .Select(feature => StringProperty(feature, "id")).ToArray();

    private static int TerrainInputSourceVectorIndex(JsonElement node)
    {
        JsonElement indexProperty = Property(node, "properties").EnumerateArray().Single(property =>
            StringProperty(property, "id") == "TerrainInput/Index");
        return int.Parse(StringProperty(indexProperty, "value"), CultureInfo.InvariantCulture);
    }

    private static TerrainMapCoordinate[] ReadPoints(JsonElement points) => points.EnumerateArray()
        .Select(point => new TerrainMapCoordinate(NumberProperty(point, "x"), NumberProperty(point, "y"))).ToArray();

    private static bool HasPointSet(JsonElement feature, TerrainMapCoordinate[] expected)
    {
        TerrainMapCoordinate[] actual = ReadPoints(Property(feature, "points"));
        return actual.Length == expected.Length && expected.All(point => actual.Any(candidate =>
            Math.Abs(candidate.X - point.X) < 1e-8 && Math.Abs(candidate.Y - point.Y) < 1e-8));
    }

    private static string? MetadataValue(JsonElement feature, string id) =>
        Property(feature, "metadata").EnumerateArray().SingleOrDefault(item =>
            StringProperty(item, "id") == id) is { ValueKind: JsonValueKind.Object } metadata
                ? OptionalString(metadata, "value") : null;

}
