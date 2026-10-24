using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Terrains;

namespace Ras.Backend.ServerChecks;

internal sealed record TerrainAssociationAcceptanceReport(
    string State,
    string ManifestPath,
    string MapFixturePath,
    string ProjectPath,
    string ProjectRevision,
    string VisualizationRevision,
    string TerrainPath,
    string TerrainRevision,
    string TerrainInputRasterPath,
    string TerrainInputRasterRevision,
    double DirectTerrainSample,
    int RenderWidth,
    int RenderHeight,
    long RenderPixels,
    long RenderWireBytes,
    bool NoDataStayedEmpty,
    bool OriginalFixtureBytesUnchanged);

internal sealed record TerrainAssociationOwnerManifest(
    CatalogTreeNodeLocator[] GeometryNodes,
    CatalogTreeNodeLocator[] PlanNodes,
    MapResultGeometryOwner[] ResultOwners);

internal sealed record TerrainAssociationLayerManifest(
    CatalogTreeNodeLocator Locator,
    string SourcePath,
    string SourceRevision,
    string InputRasterPath,
    string InputRasterRevision,
    AssociatedMapLayerDependency[] Dependencies,
    AssociatedMapLayerOwnerManifest Owners,
    double[] SourceExtent,
    bool IsChecked,
    bool EffectiveVisible);

internal sealed record AssociatedMapLayerOwnerManifest(
    CatalogTreeNodeLocator[] GeometryNodes,
    CatalogTreeNodeLocator[] PlanNodes,
    MapResultGeometryOwner[] ResultOwners);

internal sealed record TerrainAssociationManifest(
    string Schema,
    string MapFixturePath,
    string ResultHoverManifestPath,
    MapTreeRequest MapRequest,
    string ContextToken,
    MapResultSelectionOverride[] SelectionOverrides,
    TerrainAssociationOwnerManifest Owners,
    TerrainAssociationLayerManifest Terrain,
    MapGeometryViewport Viewport,
    int Width,
    int Height,
    MapHoverCoordinate KnownWorldPoint,
    double ExpectedTerrainValue,
    double TerrainTolerance,
    MapHoverCoordinate NoDataWorldPoint,
    string[] NoDataExpected,
    object OriginalCSharpHdfReference,
    object RenderReference,
    string OriginalProjectSHA256,
    string OriginalVisualizationSHA256,
    string OriginalTerrainModelSHA256,
    string OriginalTerrainInputRasterSHA256,
    string[] OriginalResultSHA256,
    object[] VisibilityReceipts);

/// <summary>
/// Clones an already-computed Result-hover fixture, then exercises the real
/// map.associations.load and map.hover.identify operations against its actual
/// Terrain raster. All visibility edits are confined to the new fixture copy.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<TerrainAssociationAcceptanceReport> RunTerrainAssociationAsync(
        Uri baseAddress, string fixtureManifestPath, string resultHoverManifestPath,
        string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Terrain acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string fixturePath = Path.GetFullPath(fixtureManifestPath);
        string resultPath = Path.GetFullPath(resultHoverManifestPath);
        FixtureManifest fixture = JsonSerializer.Deserialize<FixtureManifest>(
            await File.ReadAllTextAsync(fixturePath), JsonOptions)
            ?? throw new InvalidDataException("The source map fixture manifest is empty.");
        ResultHoverManifest resultHover = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(resultPath), JsonOptions)
            ?? throw new InvalidDataException("The Result-hover HDF manifest is empty.");
        if (resultHover.Schema != "hecras-result-hover-e2e/v1" ||
            !PathEquals(fixture.ProjectPath, resultHover.ProjectPath) ||
            fixture.Schema != "hecras-plan-compute-e2e/v1")
            throw new InvalidDataException("The map fixture and direct-HDF reference must describe the same computed project.");

        string sourceRoot = Path.GetFullPath(fixture.RootPath);
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Terrain acceptance requires a new output directory; existing data is never overwritten.");
        string outputRoot = Path.Combine(outputDirectory, Path.GetFileName(sourceRoot));
        string outputFixturePath = Path.Combine(outputDirectory, "map-fixture.json");
        string outputHdfManifestPath = Path.Combine(outputDirectory, "result-hover-manifest.json");
        string outputManifestPath = Path.Combine(outputDirectory, "terrain-association-manifest.json");

        string[] originalWatchedPaths = [fixture.ProjectPath, Path.ChangeExtension(fixture.ProjectPath, ".vis"),
            fixture.PlanPath, fixture.GeometryPath, fixture.BoundaryPath, fixture.TerrainPath,
            .. resultHover.Sources.Select(source => source.ResultPath)];
        CopyDirectory(sourceRoot, outputRoot);
        string Rebase(string path) => RebasePath(path, sourceRoot, outputRoot);
        FixtureManifest copiedFixture = fixture with
        {
            RootPath = outputRoot,
            ProjectPath = Rebase(fixture.ProjectPath),
            PlanPath = Rebase(fixture.PlanPath),
            GeometryPath = Rebase(fixture.GeometryPath),
            BoundaryPath = Rebase(fixture.BoundaryPath),
            TerrainPath = Rebase(fixture.TerrainPath),
        };
        await File.WriteAllTextAsync(outputFixturePath, JsonSerializer.Serialize(copiedFixture, JsonOptions));
        JsonNode copiedHdfManifest = JsonNode.Parse(await File.ReadAllTextAsync(resultPath))
            ?? throw new InvalidDataException("The direct-HDF manifest could not be parsed.");
        RewriteAbsolutePaths(copiedHdfManifest, sourceRoot, outputRoot);
        await File.WriteAllTextAsync(outputHdfManifestPath, copiedHdfManifest.ToJsonString(JsonOptions));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = copiedFixture.ProjectPath }, cancellation)).Result;
        string projectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        if (!PathEquals(projectPath, copiedFixture.ProjectPath)) throw new SourceFileChangedException();
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = Sha256(System.Text.Encoding.UTF8.GetBytes(projectXml));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        string visualizationRevision = FileSha256(visualizationPath);
        string mapName = resultHover.Map.MapName;
        string mapType = resultHover.Map.MapType;
        if (mapType != "Plan") throw new InvalidDataException("The Terrain fixture must use its saved Plan map.");
        string mapID = Guid.NewGuid().ToString("D");
        string planName = Path.GetFileNameWithoutExtension(copiedFixture.PlanPath);
        MapTreeResultSelectionOverride[] treeSelections =
        [
            new(copiedFixture.PlanPath, planName, Rebase(resultHover.Sources[0].ResultPath),
                resultHover.Sources[0].Revision)
        ];
        MapTreeRequest MapRequest(string revision, bool useResultSelection = true) => new(
            projectPath, projectXml, projectXmlSHA, mapID, resultHover.Map.MapIndex,
            mapName, mapType, revision, useResultSelection ? treeSelections : null,
            ProjectSessionID: mapID);

        JsonElement initialTree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision, useResultSelection: false), cancellation)).Result;
        CatalogTreeNode[] initialRoots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(initialTree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNodeLocator sourceMultiResultLocator = JsonSerializer.Deserialize<CatalogTreeNodeLocator>(
            JsonSerializer.Serialize(resultHover.Nodes["multiResult"].Locator, JsonOptions), JsonOptions)
            ?? throw new InvalidDataException("The direct-HDF manifest omitted its exact MultiResult locator.");
        if (sourceMultiResultLocator.NodeType != "MultiResult")
            throw new InvalidDataException("The direct-HDF manifest's exact Result owner is not a MultiResult.");
        CatalogTreeNodeLocator multiResultLocator = RebaseLocator(sourceMultiResultLocator, Rebase);
        _ = FlattenMapNodes(initialRoots).Single(node => node.Locator is { } locator &&
            TerrainLocatorEqual(JsonSerializer.SerializeToElement(locator, JsonOptions), multiResultLocator));
        CatalogTreeNodeLocator planLocator = FindPlanOwner(initialRoots, multiResultLocator).Locator!;
        ResultHoverSourceManifest source = resultHover.Sources[0] with
        {
            ResultPath = Rebase(resultHover.Sources[0].ResultPath)
        };
        if (!FileSha256(source.ResultPath).Equals(source.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        MapResultSelectionOverride[] selections =
        [new(multiResultLocator, 0, "Explicit", source.ResultPath, source.Revision)];

        async Task<(string ContextToken, CatalogTreeNode[] Roots)> ReadResultContextAsync(string revision)
        {
            JsonElement value = (await SubmitOperationAsync(client, "map.resultSources",
                new { map = MapRequest(revision), selectionOverrides = selections }, cancellation)).Result;
            if (!StringProperty(value, "projectRevision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
                !StringProperty(value, "projectXmlSHA").Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
                !StringProperty(value, "visualizationRevision").Equals(revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            CatalogTreeNode[] roots;
            if (value.TryGetProperty("treeRoots", out JsonElement selectedRoots))
                roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(selectedRoots.GetRawText(), JsonOptions) ?? [];
            else
            {
                JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
                    MapRequest(revision), cancellation)).Result;
                roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
                    Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
            }
            return (StringProperty(value, "contextToken"), roots);
        }

        (string contextToken, CatalogTreeNode[] roots) = await ReadResultContextAsync(visualizationRevision);
        planLocator = FindPlanOwner(roots, multiResultLocator).Locator!;
        TreeNodePath planTerrainPath = FindPlanOwnedTerrain(roots, planLocator, expectedModelPath: null);
        string terrainModelPath = TerrainModelPath(planTerrainPath.Node);
        TreeNodePath resultTerrainPath = FindResultOwnedTerrain(roots, terrainModelPath,
            multiResultLocator, source.ResultPath, source.Revision);
        CatalogTreeNodeLocator planTerrainLocator = planTerrainPath.Node.Locator!;
        CatalogTreeNodeLocator resultTerrainLocator = resultTerrainPath.Node.Locator!;
        CatalogTreeNodeLocator planGeometryLocator = FindPlanGeometry(roots, planLocator, copiedFixture.GeometryPath).Node.Locator!;
        CatalogTreeNodeLocator resultGeometryLocator = FindGeometryAncestor(resultTerrainPath).Locator!;
        CatalogTreeNodeLocator[] planOwners = [planLocator];
        string originalTerrainModelPath = SourcePathForCopy(terrainModelPath, sourceRoot, outputRoot);
        if (!File.Exists(originalTerrainModelPath))
            throw new FileNotFoundException("The source fixture's exact Plan Terrain model HDF was not found.", originalTerrainModelPath);
        originalWatchedPaths = [.. originalWatchedPaths, originalTerrainModelPath];
        string[] originalHashes = originalWatchedPaths.Select(FileSha256).ToArray();
        MapResultGeometryOwner[] resultOwners =
        [new(multiResultLocator, contextToken, selections, 0, source.ResultPath, source.Revision)];

        // Persist checked parent rows and both exact Terrain rows only on the
        // isolated fixture copy. Every .vis receipt is read back immediately.
        var checkedLocators = new HashSet<string>(StringComparer.Ordinal);
        var visibilityReceipts = new List<object>();
        foreach (TreeNodePath terrainPath in new[] { planTerrainPath, resultTerrainPath })
        foreach (CatalogTreeNode ancestor in terrainPath.AncestorsAndSelf.Where(node => node.Checked is not null))
        {
            if (ancestor.Checked == "checked") continue;
            CatalogTreeNodeLocator locator = ancestor.Locator
                ?? throw new InvalidDataException("A checkable Terrain owner row has no exact locator.");
            if (!checkedLocators.Add(NodeKey(locator))) continue;
            JsonElement receipt = (await SubmitOperationAsync(client, "map.tree.perform", new
            {
                map = MapRequest(visualizationRevision), action = "visibility", node = locator, @checked = true,
            }, cancellation)).Result;
            if (!StringProperty(receipt, "previousVisualizationRevision")
                    .Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            visualizationRevision = StringProperty(receipt, "visualizationRevision");
            if (!FileSha256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            visibilityReceipts.Add(new
            {
                node = locator,
                previousVisualizationRevision = StringProperty(receipt, "previousVisualizationRevision"),
                visualizationRevision,
                savedBytesSHA256 = FileSha256(visualizationPath),
            });
        }

        (contextToken, roots) = await ReadResultContextAsync(visualizationRevision);
        planLocator = FindPlanOwner(roots, multiResultLocator).Locator!;
        planTerrainPath = FindPlanOwnedTerrain(roots, planLocator, terrainModelPath);
        resultTerrainPath = FindResultOwnedTerrain(roots, terrainModelPath,
            multiResultLocator, source.ResultPath, source.Revision);
        planTerrainLocator = planTerrainPath.Node.Locator!;
        resultTerrainLocator = resultTerrainPath.Node.Locator!;
        planGeometryLocator = FindPlanGeometry(roots, planLocator, copiedFixture.GeometryPath).Node.Locator!;
        resultGeometryLocator = FindGeometryAncestor(resultTerrainPath).Locator!;
        planOwners = [planLocator];
        _ = FlattenMapNodes(roots).Single(node => node.Locator is { } locator &&
            TerrainLocatorEqual(JsonSerializer.SerializeToElement(locator, JsonOptions), multiResultLocator));
        resultOwners = [new(multiResultLocator, contextToken, selections, 0, source.ResultPath, source.Revision)];

        MapGeometryViewport viewport = new(0, 0, 4, 4);
        const int width = 1448;
        const int height = 1448;
        var staticOwners = new TerrainAssociationOwnerManifest([planGeometryLocator], planOwners, []);
        var resultOwnedOwners = new TerrainAssociationOwnerManifest([resultGeometryLocator], planOwners, resultOwners);
        string inputRasterPath = copiedFixture.TerrainPath;
        string inputRasterRevision = FileSha256(inputRasterPath);

        async Task<JsonElement> LoadAssociationAsync(CatalogTreeNodeLocator terrain,
            TerrainAssociationOwnerManifest owners, MapGeometryViewport requestedViewport,
            int requestedWidth, int requestedHeight, string? expectedContext)
        {
            var request = new AssociatedMapLayersRequest(MapRequest(visualizationRevision),
                owners.GeometryNodes, [terrain], requestedViewport, requestedWidth, requestedHeight,
                expectedContext, owners.PlanNodes, owners.ResultOwners);
            JsonElement loaded = (await SubmitOperationAsync(client, "map.associations.load", request, cancellation)).Result;
            if (!StringProperty(loaded, "projectRevision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
                !StringProperty(loaded, "projectXmlSHA").Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
                !StringProperty(loaded, "visualizationRevision").Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
                !MapIdentityMatches(loaded, resultHover.Map))
                throw new SourceFileChangedException();
            return loaded;
        }

        JsonElement staticRender = await LoadAssociationAsync(planTerrainLocator, staticOwners,
            viewport, width, height, expectedContext: null);
        JsonElement staticLayer = Property(staticRender, "layers").EnumerateArray().Single();
        ValidateRenderedLayer(staticLayer, planTerrainLocator, terrainModelPath, width, height);
        AssociatedMapLayerDependency[] terrainDependencies =
            JsonSerializer.Deserialize<AssociatedMapLayerDependency[]>(
                Property(staticLayer, "dependencies").GetRawText(), JsonOptions) ?? [];
        if (!terrainDependencies.Any(dependency => PathEquals(dependency.Path, inputRasterPath) &&
                dependency.Revision.Equals(inputRasterRevision, StringComparison.OrdinalIgnoreCase) &&
                dependency.Role == "terrain-input-raster"))
            throw new InvalidDataException("The Terrain model HDF did not retain its exact input raster dependency and revision.");
        byte[] rgba = Convert.FromBase64String(StringProperty(staticLayer, "rgba"));
        string rgbaSHA = Sha256(rgba);
        long wireBytes = System.Text.Encoding.UTF8.GetByteCount(staticRender.GetRawText());
        if (wireBytes <= 8L * 1024 * 1024)
            throw new InvalidDataException("The Terrain frame did not exercise a response larger than the former 8 MiB protocol line limit.");

        JsonElement resultOwnedRender = await LoadAssociationAsync(resultTerrainLocator,
            resultOwnedOwners, viewport, 256, 256, expectedContext: null);
        JsonElement resultOwnedLayer = Property(resultOwnedRender, "layers").EnumerateArray().Single();
        ValidateRenderedLayer(resultOwnedLayer, resultTerrainLocator, terrainModelPath, 256, 256);
        if (!Property(resultOwnedLayer, "resultOwners").EnumerateArray().Any(owner =>
            TerrainLocatorEqual(Property(owner, "resultNode"), multiResultLocator) &&
                PathEquals(StringProperty(owner, "selectedResultPath"), source.ResultPath) &&
                StringProperty(owner, "selectedResultRevision").Equals(source.Revision, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The Terrain raster lost its exact selected Result owner.");

        const double sampleX = 1.5;
        const double sampleY = 2.5;
        double directTerrainValue = SampleTerrainDirect(projectPath, projectXml, terrainModelPath,
            sampleX, sampleY);
        float directNoDataSample = SampleTerrainRaw(projectPath, projectXml, terrainModelPath, -100, -100);
        if (IsTerrainValue(directNoDataSample))
            throw new InvalidDataException("The outside-raster direct Terrain sample unexpectedly contains data.");

        MapHoverIdentifyRequest HoverRequest(string requestID, MapHoverCoordinate point, string revision) => new(
            MapRequest(revision), contextToken, selections, requestID, point,
            new MapHoverCoordinate(point.X + 0.001, point.Y), 0.001, [],
            [new(planTerrainLocator, "terrain", terrainModelPath,
                FileSha256(terrainModelPath))]);

        JsonElement sampled = (await SubmitOperationAsync(client, "map.hover.identify",
            HoverRequest(Guid.NewGuid().ToString("N"), new(sampleX, sampleY), visualizationRevision), cancellation)).Result;
        JsonElement[] entries = Property(sampled, "entries").EnumerateArray().Where(entry =>
            TerrainLocatorEqual(Property(entry, "node"), planTerrainLocator)).ToArray();
        if (entries.Length != 1 || StringProperty(entries[0], "kind") != "terrain" ||
            !PathEquals(StringProperty(entries[0], "sourcePath"), terrainModelPath) ||
            !StringProperty(entries[0], "sourceRevision").Equals(FileSha256(terrainModelPath), StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(double.Parse(StringProperty(entries[0], "value"), System.Globalization.CultureInfo.InvariantCulture) - directTerrainValue) > 1e-6)
            throw new InvalidDataException("The real Terrain hover value did not match direct sampling from the source raster.");

        JsonElement noDataHover = (await SubmitOperationAsync(client, "map.hover.identify",
            HoverRequest(Guid.NewGuid().ToString("N"), new(-100, -100), visualizationRevision), cancellation)).Result;
        if (Property(noDataHover, "entries").EnumerateArray().Any(entry =>
                TerrainLocatorEqual(Property(entry, "node"), planTerrainLocator)))
            throw new InvalidDataException("The outside-raster Terrain hover produced a value instead of NoData.");

        JsonElement zoomed = await LoadAssociationAsync(planTerrainLocator, staticOwners,
            new MapGeometryViewport(0.5, 0.5, 3.5, 3.5), 256, 192, expectedContext: null);
        JsonElement zoomedLayer = Property(zoomed, "layers").EnumerateArray().Single();
        ValidateRenderedLayer(zoomedLayer, planTerrainLocator, terrainModelPath, 256, 192);
        if (StringProperty(zoomed, "contextToken") == StringProperty(staticRender, "contextToken"))
            throw new InvalidDataException("The association token did not change when its viewport changed.");

        var manifest = new TerrainAssociationManifest(
            "hecras-map-terrain-association-e2e/v1", outputFixturePath, outputHdfManifestPath,
            MapRequest(visualizationRevision), contextToken, selections,
            new TerrainAssociationOwnerManifest([planGeometryLocator, resultGeometryLocator], planOwners, resultOwners),
            new TerrainAssociationLayerManifest(planTerrainLocator, terrainModelPath,
                FileSha256(terrainModelPath), inputRasterPath, inputRasterRevision, terrainDependencies,
                DeserializeOwners(staticLayer), ReadExtent(staticLayer),
                Property(staticLayer, "isChecked").GetBoolean(),
                Property(staticLayer, "effectiveVisible").GetBoolean()),
            viewport, width, height, new(sampleX, sampleY), directTerrainValue, 1e-6,
            new(-100, -100), ["noTerrainHoverEntry"],
            HdfReference(resultHover, source, Rebase, new(sampleX, sampleY)),
            new { rgbaSHA256 = rgbaSHA, wireBytes, pixels = (long)width * height,
                resultOwnedLocator = resultTerrainLocator, resultOwnedOwners,
                viewportChanged = new { viewport = new MapGeometryViewport(0.5, 0.5, 3.5, 3.5), width = 256, height = 192,
                    rgbaSHA256 = Sha256(Convert.FromBase64String(StringProperty(zoomedLayer, "rgba"))) } },
            originalHashes[0], originalHashes[1], originalHashes[^1], originalHashes[5], originalHashes[6..^1],
            visibilityReceipts.ToArray());
        await File.WriteAllTextAsync(outputManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        bool originalUnchanged = originalWatchedPaths.Select(FileSha256).SequenceEqual(originalHashes, StringComparer.Ordinal);
        if (!originalUnchanged) throw new IOException("The source Result-hover fixture changed; all writes must remain inside the copy.");
        long pixels = (long)width * height;
        var report = new TerrainAssociationAcceptanceReport("passed", outputManifestPath, outputFixturePath,
            projectPath, projectRevision, visualizationRevision, terrainModelPath,
            FileSha256(terrainModelPath), inputRasterPath, inputRasterRevision, directTerrainValue, width, height, pixels,
            wireBytes, true, originalUnchanged);
        return report;
    }

    private sealed record TreeNodePath(CatalogTreeNode Node, CatalogTreeNode[] AncestorsAndSelf);

    private static CatalogTreeNode FindPlanOwner(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator multiResultLocator)
    {
        if (multiResultLocator.ParentPlanAssetID is not { Length: > 0 } planAsset ||
            multiResultLocator.ParentPlanNodePath is not { Length: > 0 } planPath ||
            multiResultLocator.ParentPlanNodeName is not { Length: > 0 } planName)
            throw new InvalidDataException("The exact MultiResult locator omitted its parent Plan identity.");
        CatalogTreeNode[] matches = FlattenMapNodes(roots).Where(node => node.Locator is
                { NodeType: "Plan", ParentPlanAssetID: null, ParentResultNodeType: null } locator &&
                locator.RootName == multiResultLocator.RootName && locator.NodeName == planName &&
                PathEquals(locator.AssetID, planAsset) && (locator.NodePath ?? []).SequenceEqual(planPath))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The selected map context must resolve its exact parent Plan; found {matches.Length}.");
        return matches[0];
    }

    private static TreeNodePath FindPlanOwnedTerrain(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator planLocator, string? expectedModelPath)
    {
        if (planLocator.NodeType != "Plan" || planLocator.AssetID is not { Length: > 0 } planAsset ||
            planLocator.NodePath is not { Length: > 0 } planPath)
            throw new InvalidDataException("The exact Plan owner locator is incomplete.");
        TreeNodePath[] matches = WalkPaths(roots, []).Where(path => path.Node.Locator is
                { NodeType: "Terrain", ParentResultNodeType: null, ParentGeometryAssetID: null } locator &&
                PathEquals(locator.ParentPlanAssetID, planAsset) &&
                (locator.ParentPlanNodePath ?? []).SequenceEqual(planPath) &&
                locator.ParentPlanNodeName == planLocator.NodeName &&
                (expectedModelPath is null || PathEquals(locator.AssetID, expectedModelPath)))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The exact Plan owner must expose one direct Terrain association; found {matches.Length}.");
        _ = TerrainModelPath(matches[0].Node);
        return matches[0];
    }

    private static TreeNodePath FindResultOwnedTerrain(CatalogTreeNode[] roots,
        string terrainModelPath, CatalogTreeNodeLocator resultOwner, string resultPath, string resultRevision)
    {
        TreeNodePath[] matches = WalkPaths(roots, []).Where(path => path.Node.Locator is
                { NodeType: "Terrain", ParentResultNodeType: "MultiResult" } locator &&
                PathEquals(locator.AssetID, terrainModelPath) &&
                PathEquals(locator.ParentResultAssetID, resultPath) &&
                string.Equals(locator.ParentResultRevision, resultRevision, StringComparison.OrdinalIgnoreCase) &&
                (locator.ParentResultNodePath ?? []).SequenceEqual(resultOwner.NodePath ?? []) &&
                locator.ParentResultNodeName == resultOwner.NodeName)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The exact selected MultiResult must expose one Terrain association; found {matches.Length}.");
        _ = TerrainModelPath(matches[0].Node);
        return matches[0];
    }

    private static TreeNodePath FindPlanGeometry(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator planLocator, string expectedGeometryPath)
    {
        if (planLocator.NodeType != "Plan" || planLocator.AssetID is not { Length: > 0 } planAsset ||
            planLocator.NodePath is not { Length: > 0 } planPath)
            throw new InvalidDataException("The exact Plan owner locator is incomplete.");
        TreeNodePath[] matches = WalkPaths(roots, []).Where(path => path.Node.Locator is
                { NodeType: "Geometry", ParentResultNodeType: null } locator &&
                PathEquals(locator.AssetID, expectedGeometryPath) &&
                PathEquals(locator.ParentPlanAssetID, planAsset) &&
                (locator.ParentPlanNodePath ?? []).SequenceEqual(planPath) &&
                locator.ParentPlanNodeName == planLocator.NodeName)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The exact Plan owner must resolve one associated Geometry row; found {matches.Length}.");
        return matches[0];
    }

    private static string TerrainModelPath(CatalogTreeNode terrainNode)
    {
        CatalogTreeNodeLocator locator = terrainNode.Locator
            ?? throw new InvalidDataException("The exact Terrain row omitted its locator.");
        if (locator.NodeType != "Terrain" || locator.AssociationKind != "Terrain" ||
            terrainNode.AssetID is not { Length: > 0 } assetPath ||
            locator.AssetID is not { Length: > 0 } locatorPath ||
            locator.AssociationAssetID is not { Length: > 0 } associationPath ||
            !PathEquals(assetPath, locatorPath) || !PathEquals(assetPath, associationPath))
            throw new InvalidDataException("The exact Terrain row did not resolve one consistent model HDF source path.");
        return Path.GetFullPath(associationPath);
    }

    private static string SourcePathForCopy(string copiedPath, string sourceRoot, string destinationRoot)
    {
        string relative = Path.GetRelativePath(destinationRoot, Path.GetFullPath(copiedPath));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException("The Terrain model HDF must belong to the cloned fixture.");
        string sourcePath = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        if (!IsWithin(sourcePath, sourceRoot))
            throw new InvalidDataException("The Terrain model HDF source escaped the original fixture root.");
        return sourcePath;
    }

    private static CatalogTreeNode FindGeometryAncestor(TreeNodePath terrain)
    {
        CatalogTreeNode[] matches = terrain.AncestorsAndSelf.Take(terrain.AncestorsAndSelf.Length - 1)
            .Where(node => node.Locator?.NodeType == "Geometry").ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The Terrain row has no unique Geometry owner.");
        return matches[0];
    }

    private static IEnumerable<TreeNodePath> WalkPaths(IEnumerable<CatalogTreeNode> nodes,
        CatalogTreeNode[] ancestors)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            CatalogTreeNode[] path = [.. ancestors, node];
            yield return new TreeNodePath(node, path);
            if (node.Children is { Length: > 0 })
                foreach (TreeNodePath nested in WalkPaths(node.Children, path)) yield return nested;
        }
    }

    private static void ValidateRenderedLayer(JsonElement layer, CatalogTreeNodeLocator locator,
        string sourcePath, int width, int height)
    {
        if (!TerrainLocatorEqual(Property(layer, "locator"), locator) ||
            StringProperty(layer, "kind") != "Terrain" ||
            !PathEquals(StringProperty(layer, "assetPath"), sourcePath) ||
            !StringProperty(layer, "revision").Equals(FileSha256(sourcePath), StringComparison.OrdinalIgnoreCase) ||
            StringProperty(layer, "status") != "available" ||
            !Property(layer, "isChecked").GetBoolean() || !Property(layer, "effectiveVisible").GetBoolean() ||
            IntProperty(layer, "width") != width || IntProperty(layer, "height") != height ||
            Convert.FromBase64String(StringProperty(layer, "rgba")).LongLength != (long)width * height * 4 ||
            Property(layer, "legend").GetArrayLength() == 0)
            throw new InvalidDataException("The actual Terrain association did not produce a visible typed RGBA frame.");
    }

    private static AssociatedMapLayerOwnerManifest DeserializeOwners(JsonElement layer) => new(
        JsonSerializer.Deserialize<CatalogTreeNodeLocator[]>(Property(layer, "geometryNodes").GetRawText(), JsonOptions) ?? [],
        JsonSerializer.Deserialize<CatalogTreeNodeLocator[]>(Property(layer, "planNodes").GetRawText(), JsonOptions) ?? [],
        JsonSerializer.Deserialize<MapResultGeometryOwner[]>(Property(layer, "resultOwners").GetRawText(), JsonOptions) ?? []);

    private static double[] ReadExtent(JsonElement layer)
    {
        JsonElement extent = Property(layer, "sourceExtent");
        return [NumberProperty(extent, "minX"), NumberProperty(extent, "minY"),
            NumberProperty(extent, "maxX"), NumberProperty(extent, "maxY")];
    }

    private static bool MapIdentityMatches(JsonElement snapshot, ResultHoverMapManifest expected) =>
        IntProperty(snapshot, "mapIndex") == expected.MapIndex &&
        StringProperty(snapshot, "mapName") == expected.MapName && StringProperty(snapshot, "mapType") == expected.MapType;

    private static double NumberProperty(JsonElement value, string name) =>
        Property(value, name).GetDouble();

    private static object HdfReference(ResultHoverManifest manifest, ResultHoverSourceManifest copiedSource,
        Func<string, string> rebase, MapHoverCoordinate knownWorldPoint)
    {
        ResultHoverCaseManifest sampleCase = manifest.Cases.Single(item => item.Id == "multi-A1-to-B0");
        if (sampleCase.Point.X != knownWorldPoint.X || sampleCase.Point.Y != knownWorldPoint.Y)
            throw new InvalidDataException("The direct C# HDF baseline must use the same known world point as Terrain.");
        ResultHoverExpectedEntry entry = sampleCase.Entries.Single(item => item.SelectionIndex == 0);
        if (!PathEquals(entry.ResultPath, copiedSource.ResultPath) ||
            !string.Equals(entry.Revision, copiedSource.Revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The direct C# HDF sample must belong to the selected exact Result source.");
        return new
        {
            source = "original-CSharp-direct-HDF-readback",
            variable = "Cell Depth",
            resultPath = rebase(entry.ResultPath),
            resultRevision = entry.Revision,
            profileIndex = entry.NativeProfileIndex,
            time = sampleCase.Time,
            worldPoint = sampleCase.Point,
            rawCellDepthMeters = entry.RawCellDepth,
            mapDepthMeters = entry.ExpectedValue,
            tolerance = entry.Tolerance,
            resultSources = manifest.Sources.Select(source => new
            {
                source.SelectionIndex,
                path = rebase(source.ResultPath),
                source.Revision,
                source.Times,
                cellDepths = source.CellDepths,
                mapDepths = source.MapDepths,
            }).ToArray(),
            copiedSelectedResultPath = copiedSource.ResultPath,
        };
    }

    private static double SampleTerrainDirect(string projectPath, string projectXml,
        string terrainPath, double x, double y)
    {
        float value = SampleTerrainRaw(projectPath, projectXml, terrainPath, x, y);
        if (!IsTerrainValue(value))
            throw new InvalidDataException("The known world coordinate did not sample valid source Terrain data.");
        return value;
    }

    private static float SampleTerrainRaw(string projectPath, string projectXml,
        string terrainPath, double x, double y)
    {
        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectPath,
            XElement.Parse(projectXml, LoadOptions.PreserveWhitespace), coreLayerContext: null);
        try
        {
            Terrain[] matches = project.Terrains.Where(terrain => PathEquals(terrain.Filename, terrainPath)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("The copied project has no unique source Terrain object.");
            float[] values = new float[1];
            matches[0].SamplePoints(new[] { new Point(x, y) }.AsMemory(), values.AsMemory());
            return values[0];
        }
        finally
        {
            foreach (Result result in project.Results) result.Dispose();
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                         .Concat(project.BoundaryConditions).Concat(project.Terrains).Concat(project.NValues)
                         .Concat(project.ClassificationLayers)) layer.Dispose();
        }
    }

    private static bool IsTerrainValue(float value) => float.IsFinite(value) && value != -9999f;

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string RebasePath(string path, string sourceRoot, string destinationRoot)
    {
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(sourceRoot, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException("Every source fixture file must be inside the copied project root.");
        return Path.GetFullPath(Path.Combine(destinationRoot, relative));
    }

    private static void RewriteAbsolutePaths(JsonNode node, string sourceRoot, string destinationRoot)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(pair => pair.Key).ToArray())
                {
                    JsonNode? value = obj[key];
                    if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? text) &&
                        text is not null && Path.IsPathFullyQualified(text) && IsWithin(text, sourceRoot))
                        obj[key] = RebasePath(text, sourceRoot, destinationRoot);
                    else if (value is not null) RewriteAbsolutePaths(value, sourceRoot, destinationRoot);
                }
                break;
            case JsonArray array:
                foreach (JsonNode? value in array)
                    if (value is not null) RewriteAbsolutePaths(value, sourceRoot, destinationRoot);
                break;
        }
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static string NodeKey(CatalogTreeNodeLocator locator) =>
        $"{locator.RootName}\u001f{locator.NodeType}\u001f{locator.NodeName}\u001f{locator.AssetID}\u001f{string.Join('.', locator.NodePath ?? [])}";

    private static bool TerrainLocatorEqual(JsonElement actual, CatalogTreeNodeLocator expected)
    {
        CatalogTreeNodeLocator? locator = JsonSerializer.Deserialize<CatalogTreeNodeLocator>(actual.GetRawText(), JsonOptions);
        return locator is not null && locator.RootName == expected.RootName && locator.NodeType == expected.NodeType &&
            locator.NodeName == expected.NodeName && PathEquals(locator.AssetID, expected.AssetID) &&
            (locator.NodePath ?? []).SequenceEqual(expected.NodePath ?? []) &&
            locator.MapType == expected.MapType && locator.Filename == expected.Filename &&
            locator.FilenameRelative == expected.FilenameRelative &&
            locator.ParentResultNodeType == expected.ParentResultNodeType &&
            PathEqualsNullable(locator.ParentResultAssetID, expected.ParentResultAssetID) &&
            (locator.ParentResultNodePath ?? []).SequenceEqual(expected.ParentResultNodePath ?? []) &&
            locator.ParentResultNodeName == expected.ParentResultNodeName &&
            PathEqualsNullable(locator.ParentPlanAssetID, expected.ParentPlanAssetID) &&
            (locator.ParentPlanNodePath ?? []).SequenceEqual(expected.ParentPlanNodePath ?? []) &&
            locator.ParentPlanNodeName == expected.ParentPlanNodeName &&
            PathEqualsNullable(locator.ParentGeometryAssetID, expected.ParentGeometryAssetID) &&
            (locator.ParentGeometryNodePath ?? []).SequenceEqual(expected.ParentGeometryNodePath ?? []) &&
            locator.ParentGeometryNodeName == expected.ParentGeometryNodeName &&
            PathEqualsNullable(locator.AssociationAssetID, expected.AssociationAssetID) &&
            locator.AssociationKind == expected.AssociationKind &&
            PathEqualsNullable(locator.ParentBoundaryConditionAssetID, expected.ParentBoundaryConditionAssetID) &&
            (locator.ParentBoundaryConditionNodePath ?? []).SequenceEqual(expected.ParentBoundaryConditionNodePath ?? []) &&
            locator.ParentBoundaryConditionNodeName == expected.ParentBoundaryConditionNodeName &&
            string.Equals(locator.ParentResultRevision, expected.ParentResultRevision, StringComparison.OrdinalIgnoreCase) &&
            Equals(locator.SessionHdfResultOwner, expected.SessionHdfResultOwner);
    }

    private static CatalogTreeNodeLocator RebaseLocator(CatalogTreeNodeLocator locator,
        Func<string, string> rebase)
    {
        string? RebaseOptional(string? path) => path is null ? null : rebase(path);
        return locator with
        {
            AssetID = RebaseOptional(locator.AssetID),
            ParentGeometryAssetID = RebaseOptional(locator.ParentGeometryAssetID),
            AssociationAssetID = RebaseOptional(locator.AssociationAssetID),
            ParentPlanAssetID = RebaseOptional(locator.ParentPlanAssetID),
            ParentResultAssetID = RebaseOptional(locator.ParentResultAssetID),
            ParentBoundaryConditionAssetID = RebaseOptional(locator.ParentBoundaryConditionAssetID),
        };
    }

    private static bool PathEqualsNullable(string? left, string? right) => left is null || right is null
        ? left is null && right is null : PathEquals(left, right);

}
